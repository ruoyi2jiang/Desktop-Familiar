using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Models;

public sealed class CompanionDefinition : CompanionScenes
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = LocalizationService.Get("Model.Companion.New");
    public string Birthday { get; set; } = string.Empty;
    public string UserNickname { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string ThinkingPhrase { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = "Segoe UI";
    public string? ImportedFontPath { get; set; }
    public string FontColor { get; set; } = "#FFFFFFFF";
    public string? ProfilePicturePath { get; set; }
    public string? BubbleImagePath { get; set; }
    public CompanionAppearance Appearance { get; set; }
    public ModelAppearance Model3D { get; set; } = new();
    [JsonIgnore]
    public CompanionScenes ActiveScenes => Appearance == CompanionAppearance.Model3D ? Model3D : this;
    public IEnumerable<string> GetDialogueExamples() => ActiveScenes.Animations
        .SelectMany(scene => scene.DialogLines)
        .Where(line => !string.IsNullOrWhiteSpace(line.Text))
        .Select(line => line.Text.Trim())
        .Distinct(StringComparer.Ordinal);

    public IEnumerable<string?> GetAssetPaths()
    {
        yield return Model3D.FilePath;
        yield return ImportedFontPath;
        yield return ProfilePicturePath;
        yield return BubbleImagePath;
        foreach (var scene in Animations.Concat(Model3D.Animations))
        {
            yield return scene.AnimationPath;
            foreach (var frame in scene.PngFrames)
                yield return frame.FilePath;
            foreach (var line in scene.DialogLines)
                yield return line.VoicePath;
        }
    }

    public CompanionDefinition Clone() => CopyScenesTo(new CompanionDefinition()
    {
        Id = Id,
        Name = Name,
        Birthday = Birthday,
        UserNickname = UserNickname,
        Description = Description ?? string.Empty,
        Personality = Personality ?? string.Empty,
        ThinkingPhrase = ThinkingPhrase ?? string.Empty,
        FontFamilyName = FontFamilyName,
        ImportedFontPath = ImportedFontPath,
        FontColor = FontColor,
        ProfilePicturePath = ProfilePicturePath,
        BubbleImagePath = BubbleImagePath,
        Appearance = Appearance,
        Model3D = Model3D.Clone()
    });
}
