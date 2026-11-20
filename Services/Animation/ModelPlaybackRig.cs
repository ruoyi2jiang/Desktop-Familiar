using System.Text.Json.Nodes;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace DesktopFamiliar.Services.Animation;

/// <summary>A renderable scene whose animation curves are bound on first use.</summary>
internal sealed class ModelPlaybackRig
{
    private readonly (Node Source, NodeInstance Target)[] _nodes;
    private readonly Dictionary<string, ModelClip> _clips = new(StringComparer.Ordinal);
    public ModelRoot Model { get; }
    public SceneInstance Scene { get; }

    internal ModelPlaybackRig(ModelRoot model, ModelRoot rig)
    {
        Model = model;
        if (model.LogicalAnimations.Count == 0)
        {
            Scene = SceneTemplate.Create(rig.DefaultScene).CreateInstance();
            _nodes = [];
            return;
        }
        // Node names are optional and may repeat. Carry the file's node indices
        // through the runtime's reordering of the default scene.
        foreach (var node in rig.LogicalNodes) node.Extras = JsonValue.Create(node.LogicalIndex);
        var options = new RuntimeOptions { ExtrasConverterCallback = value => value is Node node ? node.LogicalIndex : null };
        Scene = SceneTemplate.Create(rig.DefaultScene, options).CreateInstance();
        _nodes = Scene.Armature.LogicalNodes.Select(node => (model.LogicalNodes[(int)node.Extras!], node)).ToArray();
    }

    public ModelClip? GetClip(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_clips.TryGetValue(name, out var clip)) return clip;
        var animation = Model.LogicalAnimations.FirstOrDefault(item => item.Name == name);
        if (animation is null) return null;
        clip = new ModelClip(animation, _nodes);
        _clips.Add(name, clip);
        return clip;
    }
}

internal sealed class ModelClip
{
    private readonly List<Action<float>> _channels = [];
    public float Duration { get; }

    internal ModelClip(SharpGLTF.Schema2.Animation animation, (Node Source, NodeInstance Target)[] nodes)
    {
        Duration = animation.Duration;
        foreach (var (source, target) in nodes)
        {
            var curves = source.GetCurveSamplers(animation);
            if (curves.HasTransformCurves)
            {
                var rest = source.LocalTransform.GetDecomposed();
                var scale = curves.Scale?.CreateCurveSampler(true);
                var rotation = curves.Rotation?.CreateCurveSampler(true);
                var translation = curves.Translation?.CreateCurveSampler(true);
                _channels.Add(time => target.LocalMatrix = new AffineTransform(
                    scale?.GetPoint(time) ?? rest.Scale,
                    rotation?.GetPoint(time) ?? rest.Rotation,
                    translation?.GetPoint(time) ?? rest.Translation).Matrix);
            }
            if (curves.GetMorphingSampler<SparseWeight8>()?.CreateCurveSampler(true) is { } weights)
                _channels.Add(time => target.MorphWeights = weights.GetPoint(time));
            if (curves.Visibility?.CreateCurveSampler(true) is { } visibility)
                _channels.Add(time => target.IsVisible = visibility.GetPoint(time));
        }
    }

    public void Apply(float time)
    {
        foreach (var setChannel in _channels) setChannel(time);
    }
}
