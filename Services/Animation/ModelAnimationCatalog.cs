using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

internal sealed record ModelAnimationSource(string Name, string[] Clips, double Duration, double LoopStart, double LoopEnd)
{
    public bool Combined => Clips.Length > 1;
}

/// <summary>Offers an activity only when a start, a single matching middle and an end exist.</summary>
internal static class ModelAnimationCatalog
{
    public static List<ModelAnimationSource> List(IReadOnlyDictionary<string, double> clips)
    {
        var result = clips.Select(item => new ModelAnimationSource(item.Key, [item.Key], item.Value, 0, item.Value)).ToList();
        foreach (var start in clips.Keys)
        {
            foreach (var separator in new[] { " ", "_", "-" })
            {
                if (!start.EndsWith(separator + "start", StringComparison.OrdinalIgnoreCase)) continue;
                var name = start[..^(separator.Length + 5)];
                string[] Match(params string[] names) => clips.Keys.Where(clip => names.Contains(clip, StringComparer.OrdinalIgnoreCase)).ToArray();
                var end = Match(name + separator + "end");
                var middle = Match(name, name + separator + "loop");
                if (name.Length == 0 || end.Length != 1 || middle.Length != 1 || clips[middle[0]] <= 0) continue;
                var loopEnd = clips[start] + clips[middle[0]];
                result.Add(new(name, [start, middle[0], end[0]], loopEnd + clips[end[0]], clips[start], loopEnd));
            }
        }
        return result;
    }

    public static IEnumerable<AnimationScene> VisibleScenes(CompanionScenes appearance, bool showParts)
    {
        var scenes = appearance.Animations;
        if (appearance is not ModelAppearance) return scenes;
        var groups = scenes.GroupBy(scene => scene.Id).Where(group => group.Count() == 1)
            .Select(group => group.Single()).Where(scene => scene.ModelParts is { Count: > 0 })
            .ToDictionary(scene => scene.Id);
        var parts = scenes.Where(scene => scene.PartOfSceneId is Guid id && groups.TryGetValue(id, out var group)
                && scene.ModelClip != null && group.ModelParts!.Contains(scene.ModelClip)).ToLookup(scene => scene.PartOfSceneId);
        var children = parts.SelectMany(group => group).ToHashSet();
        return scenes.Where(scene => !children.Contains(scene)).SelectMany(
            scene => showParts ? new[] { scene }.Concat(parts[scene.Id]) : [scene]);
    }
}
