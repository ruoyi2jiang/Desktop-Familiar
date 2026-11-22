using DesktopFamiliar.Localization;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Services.Chat;

internal sealed class ChatModelTestResults
{
    private readonly Dictionary<(string Key, bool GpuEnabled), ChatModelTestResult> _completed = new();

    public void Record(string key, bool gpuEnabled, ChatModelTestResult result) => _completed[(key, gpuEnabled)] = result;
    public ChatModelTestResult? Get(string key, bool gpuEnabled) => _completed.GetValueOrDefault((key, gpuEnabled));

    public static string Summary(ChatModel? model, ChatModelTestResult? result)
    {
        if (result == null)
            return model?.SampleReplyTokens is { } sample
                ? LocalizationService.Format("Chat.SampleBeforeTest", sample) : LocalizationService.Get("Chat.MeasureOnPc");
        var speed = result.TokensPerSecond is { } rate ? LocalizationService.Format("Chat.TestSpeed", rate)
            : LocalizationService.Get("Chat.TestSpeedUnknown");
        var length = result.AverageReplyTokens is { } tokens ? LocalizationService.Format("Chat.SampleLength", tokens)
            : LocalizationService.Get("Chat.TestLengthUnknown");
        return LocalizationService.Format("Chat.MeasuredSummary", speed, length);
    }

    public static string Details(ChatModelTestResult result) =>
        LocalizationService.Format("Chat.SampleDetails", LocalizationService.Get("Chat.Backend." + result.Backend),
            result.Elapsed.TotalSeconds, FileSizeFormatter.Format(result.PeakRamBytes), result.Reply);
}
