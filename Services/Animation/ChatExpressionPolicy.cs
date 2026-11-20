using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

internal static class ChatExpressionPolicy
{
    internal static IReadOnlyList<string> Keys { get; } = ["happy", "sad", "surprised", "confused", "angry", "thoughtful"];

    internal static AnimationScene? MatchName(CompanionScenes scenes, string key)
    {
        var alias = key switch { "happy" => "smile", "thoughtful" => "ponder", _ => null };
        foreach (var name in new[] { key, alias })
        {
            if (name == null) continue;
            var matches = scenes.Animations.Where(scene => scene.PartOfSceneId == null
                && string.Equals(scene.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 0) return matches.Length == 1 ? matches[0] : null;
        }
        return null;
    }

    internal static Dictionary<string, Guid> Available(CompanionScenes scenes)
    {
        var available = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            var scene = scenes.ChatExpressions.TryGetValue(key, out var id) ? scenes.FindAnimation(id) : MatchName(scenes, key);
            if (scene != null) available.Add(key, scene.Id);
        }
        return available;
    }
}
