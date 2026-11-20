using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

/// <summary>All pages of a reply share a scene and remember any unavailable playback source.</summary>
internal sealed class ChatReplyPlayback(Guid? expressionSceneId)
{
    private readonly HashSet<Guid> _failed = [];

    public AnimationScene? Scene(CompanionScenes scenes) =>
        Usable(scenes, expressionSceneId) ?? Usable(scenes, scenes.ChatReplyAnimationId);

    private AnimationScene? Usable(CompanionScenes scenes, Guid? id) =>
        id is Guid value && !_failed.Contains(value) ? scenes.FindAnimation(value) : null;

    public void MarkFailed(Guid sceneId) => _failed.Add(sceneId);
}
