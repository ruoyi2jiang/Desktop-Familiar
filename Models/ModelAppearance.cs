using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Models;

public enum CompanionAppearance { Images2D, Model3D }

public sealed class ModelAppearance : CompanionScenes
{
    public string? FilePath { get; set; }
    // Read only by the schema 12 and bundle format 2 migrations.
    [JsonPropertyName("IdleClip"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyIdleClip { get; set; }
    [JsonPropertyName("GreetingClip"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyGreetingClip { get; set; }

    public void CreateInitialScenes(string? idleClip, string? greetingClip)
    {
        var idle = new AnimationScene { Name = idleClip ?? LocalizationService.Get("Model3D.Rest"),
            ModelClip = idleClip };
        Animations.Add(idle);
        IdleDefaultAnimationId = idle.Id;
        if (greetingClip is null) return;
        var greeting = new AnimationScene { Name = greetingClip, ModelClip = greetingClip };
        Animations.Add(greeting);
        StartAnimationId = greeting.Id;
        InteractionStates.ClickAreas.Add(new CompanionClickArea { Name = greeting.Name, AnimationId = greeting.Id });
    }

    public ModelAppearance Clone() => CopyScenesTo(new ModelAppearance { FilePath = FilePath });
}
