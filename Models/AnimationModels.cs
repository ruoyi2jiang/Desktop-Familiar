using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Models;

public sealed class DialogLine
{
    public string Text { get; set; } = string.Empty;
    public string? VoicePath { get; set; }

    public DialogLine Clone() => new() { Text = Text, VoicePath = VoicePath };
}

public sealed class PngAnimationFrame
{
    public string FilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; } = 0.1;

    [JsonIgnore]
    public string FileName => string.IsNullOrWhiteSpace(FilePath)
        ? LocalizationService.Get("Model.Frame.Missing")
        : Path.GetFileName(FilePath);

    public PngAnimationFrame Clone() => new()
    {
        FilePath = FilePath,
        DurationSeconds = DurationSeconds
    };
}

public sealed class AnimationScene
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = LocalizationService.Get("Model.Animation.New");
    public string AnimationPath { get; set; } = string.Empty;
    public List<PngAnimationFrame> PngFrames { get; set; } = [];
    public double DialogCharactersPerSecond { get; set; } = 14;
    public ScenePlaybackMode PlaybackMode { get; set; } = ScenePlaybackMode.LoopUntilDialogEnds;
    public List<DialogLine> DialogLines { get; set; } = [];
    public AnimationFrameLoopMode FrameLoopMode { get; set; } = AnimationFrameLoopMode.EntireAnimation;
    public int LoopStartFrameIndex { get; set; }
    public int LoopEndFrameIndex { get; set; }
    public int DialogStartFrameIndex { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? LocalizationService.Get("Model.Animation.Untitled")
        : Name;

    [JsonIgnore]
    public bool UsesPngSequence => PngFrames.Count > 0;

    [JsonIgnore]
    public string SourceSummary => UsesPngSequence
        ? LocalizationService.Format("Model.Animation.SourcePng", PngFrames.Count)
        : string.IsNullOrWhiteSpace(AnimationPath)
            ? LocalizationService.Get("Model.Animation.SourceMissing")
            : Path.GetFileName(AnimationPath);

    public AnimationScene Clone() => new()
    {
        Id = Id,
        Name = Name,
        AnimationPath = AnimationPath,
        PngFrames = PngFrames.Select(frame => frame.Clone()).ToList(),
        DialogCharactersPerSecond = DialogCharactersPerSecond,
        PlaybackMode = PlaybackMode,
        DialogLines = DialogLines.Select(line => line.Clone()).ToList(),
        FrameLoopMode = FrameLoopMode,
        LoopStartFrameIndex = LoopStartFrameIndex,
        LoopEndFrameIndex = LoopEndFrameIndex,
        DialogStartFrameIndex = DialogStartFrameIndex
    };

    public bool TryGetLoopRange(int frameCount, out int startFrameIndex, out int endFrameIndex)
    {
        startFrameIndex = 0;
        endFrameIndex = 0;
        if (frameCount < 1 || FrameLoopMode == AnimationFrameLoopMode.EntireAnimation)
            return false;

        startFrameIndex = LoopStartFrameIndex;
        endFrameIndex = FrameLoopMode == AnimationFrameLoopMode.SingleFrame
            ? LoopStartFrameIndex
            : LoopEndFrameIndex;
        return startFrameIndex >= 0
            && endFrameIndex >= startFrameIndex
            && endFrameIndex < frameCount;
    }

    public int GetLastFrameReachableBeforeLoopRepeats(int frameCount) =>
        TryGetLoopRange(frameCount, out _, out var loopEnd) ? loopEnd : Math.Max(frameCount - 1, 0);
}
