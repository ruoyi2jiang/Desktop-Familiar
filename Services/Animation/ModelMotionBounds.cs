using System.Drawing;
using System.Numerics;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using PropertyPath = SharpGLTF.Schema2.PropertyPath;

namespace DesktopFamiliar.Services.Animation;

/// <summary>Conservative world-space XY reach over configured clips and blends between them.</summary>
internal static class ModelMotionBounds
{
    private sealed class Reach(Node node)
    {
        public Vector3 MinTranslation { get; private set; } = node.LocalMatrix.Translation;
        public Vector3 MaxTranslation { get; private set; } = node.LocalMatrix.Translation;
        public float Scale { get; private set; } = MaxComponent(Vector3.Abs(node.LocalTransform.GetDecomposed().Scale));
        public float Radius = -1;

        public void IncludeTranslation(Vector3 value)
        {
            MinTranslation = Vector3.Min(MinTranslation, value);
            MaxTranslation = Vector3.Max(MaxTranslation, value);
        }

        public void IncludeScale(Vector3 value) => Scale = Math.Max(Scale, MaxComponent(Vector3.Abs(value)));
        public float Offset => Vector3.Max(Vector3.Abs(MinTranslation), Vector3.Abs(MaxTranslation)).Length();
    }

    // A null result asks the overlay to reserve the visible desktop for transforms it cannot bound.
    internal static RectangleF? Calculate(ModelRoot model, IMeshDecoder<Material>[] meshes, HashSet<string> clips, RectangleF restBounds)
    {
        var channels = model.LogicalAnimations.Where(a => a.Name != null && clips.Contains(a.Name))
            .SelectMany(a => a.Channels).ToArray();
        if (channels.Length == 0) return restBounds;
        if (channels.All(c => c.TargetNode != null && c.TargetNodePath == PropertyPath.translation))
            return TranslatedBounds(model, channels, restBounds);
        if (model.LogicalNodes.Any(n => !n.LocalTransform.IsLosslessDecomposable)) return null;

        var reach = model.LogicalNodes.Select(n => new Reach(n)).ToArray();
        var morphWeights = model.LogicalNodes.Select(n => n.GetMorphWeights().Select(Math.Abs).ToArray()).ToArray();
        foreach (var channel in channels)
        {
            if (channel.TargetNode is not { } node) continue;
            var bounds = reach[node.LogicalIndex];
            switch (channel.TargetNodePath)
            {
                case PropertyPath.translation:
                    foreach (var value in ControlPoints(channel.GetTranslationSampler(), OffsetVector)) bounds.IncludeTranslation(value);
                    break;
                case PropertyPath.scale:
                    foreach (var value in ControlPoints(channel.GetScaleSampler(), OffsetVector)) bounds.IncludeScale(value);
                    break;
                case PropertyPath.weights:
                    ref var weights = ref morphWeights[node.LogicalIndex];
                    foreach (var values in ControlPoints(channel.GetMorphSampler(), OffsetWeights))
                    {
                        if (weights.Length < values.Length) Array.Resize(ref weights, values.Length);
                        for (int i = 0; i < values.Length; i++) weights[i] = Math.Max(weights[i], Math.Abs(values[i]));
                    }
                    break;
            }
        }

        foreach (var node in Node.Flatten(model.DefaultScene).Where(n => n.Mesh != null))
        {
            if (node.GetGpuInstancing() != null) return null;
            var joints = node.Skin?.Joints;
            var bind = node.Skin?.InverseBindMatrices;
            foreach (var primitive in meshes[node.Mesh!.LogicalIndex].Primitives)
            {
                if (!primitive.TriangleIndices.Any()) continue;
                for (int v = 0; v < primitive.VertexCount; v++)
                {
                    // Skinning averages the joint-transformed positions with nonnegative weights.
                    // Bounding each contributing joint therefore bounds the resulting vertex.
                    if (joints == null) IncludeVertex(node, Matrix4x4.Identity);
                    else
                    {
                        var skin = primitive.GetSkinWeights(v);
                        if (skin.IsWeightless) return null;
                        foreach (var (index, weight) in skin.GetNonZeroWeights())
                        {
                            if (weight < 0 || index >= joints.Count) return null;
                            IncludeVertex(joints[index], bind is { Count: > 0 } ? bind[index] : Matrix4x4.Identity);
                        }
                    }

                    void IncludeVertex(Node joint, Matrix4x4 inverseBind)
                    {
                        var position = primitive.GetPosition(v);
                        var radius = Vector3.Transform(position, inverseBind).Length();
                        if (primitive.MorphTargetsCount > 0)
                        {
                            var deltas = primitive.GetPositionDeltas(v);
                            var weights = morphWeights[node.LogicalIndex];
                            // The runtime mixes base and absolute target positions, including
                            // extrapolated weights. Bound both contributions in joint space.
                            float baseLength = Vector3.TransformNormal(position, inverseBind).Length();
                            for (int i = 0; i < Math.Min(deltas.Count, weights.Length); i++)
                                radius += weights[i] * (2 * baseLength + Vector3.TransformNormal(deltas[i], inverseBind).Length());
                        }
                        var jointReach = reach[joint.LogicalIndex];
                        jointReach.Radius = Math.Max(jointReach.Radius, radius);
                    }
                }
            }
        }

        RectangleF? result = null;
        foreach (var root in model.LogicalNodes.Where(n => n.VisualParent == null))
        {
            var radius = ScaledRadius(root);
            if (radius < 0) continue;
            var r = reach[root.LogicalIndex];
            var box = RectangleF.FromLTRB(r.MinTranslation.X - radius, r.MinTranslation.Y - radius,
                r.MaxTranslation.X + radius, r.MaxTranslation.Y + radius);
            result = result.HasValue ? RectangleF.Union(result.Value, box) : box;
        }
        return result is { } rect && float.IsFinite(rect.Width) && float.IsFinite(rect.Height) ? rect : null;

        float ScaledRadius(Node node)
        {
            var r = reach[node.LogicalIndex];
            var radius = r.Radius;
            foreach (var child in node.VisualChildren)
            {
                var childRadius = ScaledRadius(child);
                if (childRadius >= 0) radius = Math.Max(radius, reach[child.LogicalIndex].Offset + childRadius);
            }
            // Rotation, including a blend between two poses, preserves distance to the joint.
            return radius < 0 ? -1 : radius * r.Scale;
        }
    }

    private static RectangleF? TranslatedBounds(ModelRoot model, AnimationChannel[] channels, RectangleF rest)
    {
        // With fixed orientations and scales, every mesh vertex moves by the sum of its ancestors’
        // translations in world axes. Skinned vertices take a weighted average of joint offsets.
        var offsets = new (Vector2 Min, Vector2 Max)[model.LogicalNodes.Count];
        foreach (var channel in channels)
        {
            var node = channel.TargetNode!;
            var parentMatrix = node.VisualParent?.WorldMatrix ?? Matrix4x4.Identity;
            var origin = node.LocalMatrix.Translation;
            ref var offset = ref offsets[node.LogicalIndex];
            foreach (var value in ControlPoints(channel.GetTranslationSampler(), OffsetVector))
            {
                var delta = Vector3.TransformNormal(value - origin, parentMatrix);
                var xy = new Vector2(delta.X, delta.Y);
                offset.Min = Vector2.Min(offset.Min, xy);
                offset.Max = Vector2.Max(offset.Max, xy);
            }
        }

        var min = Vector2.Zero;
        var max = Vector2.Zero;
        var joints = model.LogicalSkins.SelectMany(s => s.Joints).ToHashSet();
        foreach (var root in model.DefaultScene.VisualChildren) Visit(root, Vector2.Zero, Vector2.Zero);
        var box = RectangleF.FromLTRB(rest.Left + min.X, rest.Top + min.Y, rest.Right + max.X, rest.Bottom + max.Y);
        return float.IsFinite(box.Width) && float.IsFinite(box.Height) ? box : null;

        void Visit(Node node, Vector2 low, Vector2 high)
        {
            low += offsets[node.LogicalIndex].Min;
            high += offsets[node.LogicalIndex].Max;
            if (node.Mesh != null || joints.Contains(node))
            {
                min = Vector2.Min(min, low);
                max = Vector2.Max(max, high);
            }
            foreach (var child in node.VisualChildren) Visit(child, low, high);
        }
    }

    private static float MaxComponent(Vector3 v) => Math.Max(v.X, Math.Max(v.Y, v.Z));

    private static Vector3 OffsetVector(Vector3 value, Vector3 tangent, float factor) => value + tangent * factor;

    private static float[] OffsetWeights(float[] value, float[] tangent, float factor) =>
        value.Zip(tangent, (v, t) => v + t * factor).ToArray();

    private static IEnumerable<T> ControlPoints<T>(IAnimationSampler<T> sampler, Func<T, T, float, T> offset)
    {
        if (sampler.InterpolationMode != AnimationInterpolationMode.CUBICSPLINE)
        {
            foreach (var (_, value) in sampler.GetLinearKeys()) yield return value;
            yield break;
        }
        // A cubic Hermite segment stays inside the hull of its four Bézier control points.
        (float Time, T Value, T Tangent)? previous = null;
        foreach (var (time, (incoming, value, outgoing)) in sampler.GetCubicKeys())
        {
            yield return value;
            if (previous is { } p)
            {
                float third = (time - p.Time) / 3;
                yield return offset(p.Value, p.Tangent, third);
                yield return offset(value, incoming, -third);
            }
            previous = (time, value, outgoing);
        }
    }
}
