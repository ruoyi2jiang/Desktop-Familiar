using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Storage;
using NAudio.Wave;

namespace DesktopFamiliar.Services.Voice;

internal sealed record SpeakerChoice(string Id, string Name, string Language = "", int Speakers = 1);

internal static class WindowsVoices
{
    internal static bool OpenLanguageSettings()
    {
        try
        {
            using var settings = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:regionlanguage")
                { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) { AppLogger.Warning("Windows language settings could not be opened.", ex); return false; }
    }

    private static dynamic Create(string name) => Activator.CreateInstance(Type.GetTypeFromProgID(name, true)!)!;
    private static void Release(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
    }

    public static List<SpeakerChoice> Installed()
    {
        var voices = new List<SpeakerChoice>();
        foreach (var location in VoiceBankStore.Catalogue.Windows.Categories)
        {
            dynamic category = Create("SAPI.SpObjectTokenCategory");
            dynamic? tokens = null;
            try
            {
                category.SetId(location, false);
                tokens = category.EnumerateTokens("", "");
                for (int i = 0; i < (int)tokens.Count; i++)
                {
                    dynamic item = tokens.Item(i);
                    try
                    {
                        string language = item.GetAttribute("Language");
                        var culture = int.TryParse(language.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
                            ? CultureInfo.GetCultureInfo(id).Name : "";
                        voices.Add(new("windows:" + (string)item.Id, (string)item.GetDescription(), culture));
                    }
                    finally { Release(item); }
                }
            }
            catch (COMException ex) when ((uint)ex.HResult is 0x8004503A or 0x80070002) { /* Optional voice category is absent. */ }
            finally { Release(tokens); Release(category); }
        }
        return voices.DistinctBy(v => v.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static byte[] Render(string text, CompanionVoice prefs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        dynamic voice = Create("SAPI.SpVoice");
        dynamic? stream = null, selected = null, format = null;
        try
        {
            if (prefs.Id.Length > 0)
            {
                var match = Installed().FirstOrDefault(v => v.Id == prefs.Id) ?? throw new IOException("Selected Windows voice is unavailable.");
                selected = Create("SAPI.SpObjectToken");
                selected.SetId(match.Id["windows:".Length..], "", false);
                voice.Voice = selected;
            }
            stream = Create("SAPI.SpMemoryStream");
            format = stream.Format;
            format.Type = 18; // SAPI 16 kHz, 16-bit mono PCM
            stream.Format = format;
            voice.AudioOutputStream = stream;
            voice.Rate = prefs.Rate;
            voice.Volume = prefs.Volume;
            string xml = $"<pitch absmiddle=\"{prefs.Pitch}\">{SecurityElement.Escape(text)}</pitch>";
            voice.Speak(xml, 9); // asynchronous SAPI XML
            while (!(bool)voice.WaitUntilDone(40))
                if (token.IsCancellationRequested) { voice.Speak("", 3); token.ThrowIfCancellationRequested(); }
            token.ThrowIfCancellationRequested();
            byte[] pcm = (byte[])stream.GetData();
            using var audio = new MemoryStream();
            using var raw = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(16000, 16, 1));
            WaveFileWriter.WriteWavFileToStream(audio, raw);
            return audio.ToArray();
        }
        finally { Release(voice); Release(format); Release(stream); Release(selected); }
    }
}
