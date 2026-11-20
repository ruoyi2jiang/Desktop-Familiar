using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Services.Chat;

internal sealed class ChatModelTestResults
{
    private readonly Dictionary<string, string> _completed = new();

    public void Record(string id, ChatModelTestResult result)
    {
        var rate = result.TokensPerSecond is { } speed ? LocalizationService.Format("Chat.TestSpeed", speed)
            : LocalizationService.Get("Chat.TestSpeedUnknown");
        _completed[id] = LocalizationService.Format("Chat.TestResult",
            result.Elapsed.TotalSeconds, rate, FileSizeFormatter.Format(result.PeakRamBytes), result.Reply);
    }

    public string? Describe(string? id, string? notice = null)
    {
        if (id == null || !_completed.TryGetValue(id, out var result)) return notice;
        return notice == null ? result : notice + Environment.NewLine + result;
    }
}
