using System.Numerics;
using SharpGLTF.Runtime;
using SharpGLTF.Transforms;

namespace DesktopFamiliar.Services.Animation;

/// <summary>Blends from the displayed local pose, including a partially completed transition.</summary>
internal sealed class ModelPoseTransition(ArmatureInstance armature, float duration = ModelPoseTransition.DefaultDuration)
{
    internal const float DefaultDuration = 0.3f;
    internal const float ReturnToIdleDuration = 0.6f;
    public float Duration { get; } = duration;
    private readonly record struct Pose(NodeInstance Node, Matrix4x4 Transform, SparseWeight8 Weights);
    private readonly Pose[] _source = armature.LogicalNodes
        .Select(node => new Pose(node, node.LocalMatrix, node.MorphWeights)).ToArray();

    // The destination clip (or rest pose) must be sampled afresh before each call.
    public void Apply(float seconds)
    {
        var t = Math.Clamp(seconds / Duration, 0, 1);
        if (t == 1) return;
        var amount = t * t * (3 - 2 * t);
        foreach (var (node, from, weights) in _source)
        {
            var target = node.LocalMatrix;
            if (from != target)
                node.LocalMatrix = Interpolate(from, target, amount);
            if (node.MorphWeights != weights)
                node.MorphWeights = SparseWeight8.InterpolateLinear(weights, node.MorphWeights, amount);
        }
    }

    private static Matrix4x4 Interpolate(Matrix4x4 from, Matrix4x4 to, float amount)
    {
        if (amount == 0) return from;
        if (!Matrix4x4.Decompose(from, out var fromScale, out var fromRotation, out var fromPosition)
            || !Matrix4x4.Decompose(to, out var toScale, out var toRotation, out var toPosition))
            return Matrix4x4.Lerp(from, to, amount);

        return Matrix4x4.CreateScale(Vector3.Lerp(fromScale, toScale, amount))
            * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(fromRotation, toRotation, amount))
            * Matrix4x4.CreateTranslation(Vector3.Lerp(fromPosition, toPosition, amount));
    }
}
