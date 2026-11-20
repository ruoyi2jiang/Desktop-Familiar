using System.Globalization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Services.Voice;

internal static class KokoroSpeech
{
    public static async Task<byte[]> RenderAsync(string engine, LocalVoiceBank bank, string text,
        CompanionVoice voice, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(bank.Model)!;
        var output = Path.Combine(Path.GetTempPath(), $"desktop-familiar-voice-{Guid.NewGuid():N}.wav");
        try
        {
            var args = new[] { "--kokoro-model=" + bank.Model, "--kokoro-voices=" + Path.Combine(directory, "voices.bin"),
                "--kokoro-tokens=" + Path.Combine(directory, "tokens.txt"), "--kokoro-data-dir=" + Path.Combine(directory, "espeak-ng-data"),
                "--num-threads=2", "--sid=" + Math.Clamp(voice.Speaker, 0, bank.Speakers - 1),
                "--speed=" + Math.Pow(1.12, voice.Rate).ToString("F3", CultureInfo.InvariantCulture),
                "--output-filename=" + output, text.StartsWith('-') ? " " + text : text };
            await SpeechProcess.RunAsync(engine, args, token).ConfigureAwait(false);
            return await File.ReadAllBytesAsync(output, token).ConfigureAwait(false);
        }
        finally { File.Delete(output); }
    }
}
