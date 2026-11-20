using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

internal static class ModelDialogueTiming
{
    internal readonly record struct Caption(double Start, double End, string Text);
    public static double ReadingSeconds(string text, double speed) =>
        Math.Clamp(1.2 + text.Length / Math.Clamp(speed, 5, 40), 1.5, 12);

    public static List<Caption> PreviewCaptions(AnimationScene scene, Func<string, string> format)
    {
        var result = new List<Caption>();
        var end = scene.DialogStartSeconds;
        foreach (var line in scene.DialogLines.Where(HasContent))
        {
            var start = line.StartSeconds ?? end;
            // An explicit cue replaces whatever text would still be on screen.
            if (line.StartSeconds.HasValue)
            {
                result.RemoveAll(caption => caption.Start >= start);
                if (result.Count > 0 && result[^1].End > start)
                    result[^1] = result[^1] with { End = start };
            }
            var text = format(line.Text);
            end = start + ReadingSeconds(text, scene.DialogCharactersPerSecond);
            result.Add(new(start, end, text));
        }
        return result;
    }

    public static bool HasContent(DialogLine line) =>
        !string.IsNullOrWhiteSpace(line.Text) || !string.IsNullOrWhiteSpace(line.VoicePath);

    // Only the first spoken line inherits the scene delay. Other blank times follow the previous line.
    public static IEnumerable<(int Index, double Seconds)> Cues(AnimationScene scene)
    {
        var first = true;
        for (var i = 0; i < scene.DialogLines.Count; i++)
        {
            var line = scene.DialogLines[i];
            if (!HasContent(line)) continue;
            if ((line.StartSeconds ?? (first ? scene.DialogStartSeconds : (double?)null)) is double seconds)
                yield return (i, seconds);
            first = false;
        }
    }

    public static bool ValidSeconds(double seconds) =>
        double.IsFinite(seconds) && seconds is >= 0 and <= AnimationScene.MaximumDialogStartSeconds;

    public static bool HasValidCues(AnimationScene scene)
    {
        if (!ValidSeconds(scene.DialogStartSeconds) || scene.DialogLines.Any(line =>
            line.StartSeconds is double seconds && !ValidSeconds(seconds))) return false;
        var previous = -1d;
        foreach (var cue in Cues(scene))
        {
            if (cue.Seconds <= previous) return false;
            previous = cue.Seconds;
        }
        return true;
    }
}
