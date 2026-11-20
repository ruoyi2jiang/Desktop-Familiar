using System.Globalization;
using System.Speech.Recognition;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Storage;
using NAudio.Wave;

namespace DesktopFamiliar.Services.Voice;

internal enum RecognitionSource { Windows, Whisper, Missing }
internal sealed record RecognitionPlan(string Language, RecognitionSource Source);

internal sealed class VoiceService(VoiceBankStore banks)
{
    internal VoiceBankStore Banks { get; } = banks;
    internal static IReadOnlyList<string> InstalledRecognizers()
    {
        try { return SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture.Name).Distinct().ToList(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or PlatformNotSupportedException)
        {
            AppLogger.Warning("Windows recognizers are unavailable.", ex);
            return [];
        }
    }

    internal static string ResolveLanguage(string selected, IReadOnlyList<string> installed)
    {
        if (!string.IsNullOrEmpty(selected)) return selected;
        var current = CultureInfo.CurrentCulture.Name;
        return current.Length > 0 ? current : installed.FirstOrDefault() ?? "en-US";
    }

    internal static RecognitionPlan Plan(string selected, bool preferWhisper, IReadOnlyList<string> installed, bool hasWhisper)
    {
        var language = ResolveLanguage(selected, installed);
        var source = !preferWhisper && installed.Contains(language, StringComparer.OrdinalIgnoreCase)
            ? RecognitionSource.Windows : hasWhisper ? RecognitionSource.Whisper : RecognitionSource.Missing;
        return new(language, source);
    }

    internal RecognitionPlan Plan(ChatPreferences prefs) =>
        Plan(prefs.InputLanguage, prefs.PreferWhisper, InstalledRecognizers(), Banks.HasRecognition);

    internal async Task<string> TranscribeAsync(byte[] wave, ChatPreferences prefs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (MicrophoneCapture.Peak(wave) < 0.002) return "";
        var plan = Plan(prefs);
        if (plan.Source == RecognitionSource.Windows)
            return await Task.Run(() => RecognizeWindows(wave, plan.Language, token), token).ConfigureAwait(false);
        if (plan.Source == RecognitionSource.Missing) throw new FileNotFoundException("No offline recognizer for the selected language.");
        var catalogue = VoiceBankStore.Catalogue;
        var input = Path.Combine(Path.GetTempPath(), $"desktop-familiar-mic-{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(input, wave, token).ConfigureAwait(false);
            var result = await SpeechProcess.RunAsync(Banks.PathFor(catalogue.RecognitionEngine),
                ["-m", Banks.PathFor(catalogue.Recognition), "-l", CultureInfo.GetCultureInfo(plan.Language).TwoLetterISOLanguageName,
                 "-f", input, "-nt", "-np", "-ng"], token).ConfigureAwait(false);
            return string.Join(" ", result.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        finally { File.Delete(input); }
    }

    private static async Task<string> RecognizeWindows(byte[] wave, string language, CancellationToken token)
    {
        using var engine = new SpeechRecognitionEngine(CultureInfo.GetCultureInfo(language));
        using var input = new MemoryStream(wave);
        engine.LoadGrammar(new DictationGrammar());
        engine.SetInputToWaveStream(input);
        var result = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SpeechRecognized += (_, e) => result.Add(e.Result.Text);
        engine.RecognizeCompleted += (_, e) =>
        {
            if (e.Error != null) done.TrySetException(e.Error);
            else done.TrySetResult();
        };
        token.ThrowIfCancellationRequested();
        engine.RecognizeAsync(RecognizeMode.Multiple);
        using var stop = token.Register(engine.RecognizeAsyncCancel);
        await done.Task.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return string.Join(" ", result);
    }

    internal async Task<byte[]> SynthesizeAsync(string text, CompanionVoice profile, CancellationToken token)
    {
        profile = profile.Normalize();
        if (profile.Id == "off" || string.IsNullOrWhiteSpace(text)) return [];
        if (profile.Id == "" || profile.Id.StartsWith("windows:", StringComparison.Ordinal))
            return await Task.Run(() => WindowsVoices.Render(text, profile, token), token).ConfigureAwait(false);
        var bank = Banks.Installed().FirstOrDefault(b => b.Id == profile.Id)
            ?? throw new FileNotFoundException("Selected voice bank is unavailable.");
        return await KokoroSpeech.RenderAsync(Banks.PathFor(VoiceBankStore.Catalogue.VoiceEngine), bank, text, profile, token).ConfigureAwait(false);
    }

    internal async Task SpeakAsync(string text, CompanionVoice profile, CancellationToken token)
    {
        var wave = await SynthesizeAsync(text, profile, token).ConfigureAwait(false);
        var gain = profile.Id == "" || profile.Id.StartsWith("windows:")
            ? 1f : Math.Clamp(profile.Volume, 0, 100) / 100f;
        await PlayWaveAsync(wave, token, gain).ConfigureAwait(false);
    }

    internal static async Task PlayWaveAsync(byte[] wave, CancellationToken token, float gain = 1f)
    {
        token.ThrowIfCancellationRequested();
        if (wave.Length == 0) return;
        using var reader = new WaveFileReader(new MemoryStream(wave));
        using var player = new WasapiOut();
        var samples = new NAudio.Wave.SampleProviders.VolumeSampleProvider(reader.ToSampleProvider())
            { Volume = gain };
        player.Init(samples.ToWaveProvider());
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += (_, e) => { if (e.Exception == null) done.TrySetResult(); else done.TrySetException(e.Exception); };
        player.Play();
        using var stop = token.Register(player.Stop);
        await done.Task.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
}
