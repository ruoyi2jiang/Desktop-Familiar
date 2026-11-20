using System.Globalization;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Voice;

namespace DesktopFamiliar.Views;

public partial class SettingsWindow
{
    private CancellationTokenSource? _voiceSetup;
    private CancellationTokenSource? _micTest;
    private bool _hasTestDevice;
    private void CancelVoiceSettingsOperations() { _voiceSetup?.Cancel(); _micTest?.Cancel(); }

    private void RefreshVoiceSettings(ChatPreferences prefs)
    {
        EnableVoice.IsChecked = prefs.VoiceEnabled;
        VoiceAutoSend.IsChecked = prefs.SendOnRelease;
        PreferWhisper.IsChecked = prefs.PreferWhisper;
        VoiceOptions.IsEnabled = prefs.Enabled && prefs.VoiceEnabled;
        VoiceOptions.Opacity = prefs.VoiceEnabled ? 1 : 0.5;
        TalkShortcut.Text = prefs.TalkShortcut;
        TalkShortcutHint.Text = LocalizationService.Get(App.CurrentApp.Chat.TalkShortcutAvailable || !App.CurrentApp.Chat.Available
            ? "Voice.ShortcutHint" : "Chat.ShortcutUnavailable");
        if (!VoiceOptions.IsEnabled) { CancelVoiceSettingsOperations(); return; }
        try
        {
            var installed = VoiceService.InstalledRecognizers();
            var languages = VoiceBankStore.Catalogue.Languages.Concat(installed).Append(prefs.InputLanguage)
                .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(l => new DeviceChoice(l, CultureInfo.GetCultureInfo(l).DisplayName))
                .OrderBy(item => item.Name).Prepend(new("", LocalizationService.Get("Voice.SystemLanguage"))).ToList();
            BindVoiceChoice(VoiceLanguage, languages, prefs.InputLanguage);
            var plan = VoiceService.Plan(prefs.InputLanguage, prefs.PreferWhisper, installed, App.CurrentApp.Voice.Banks.HasRecognition);
            string key = plan.Source switch { RecognitionSource.Windows => "Voice.NativeReady", RecognitionSource.Whisper => "Voice.WhisperReady",
                _ => prefs.PreferWhisper ? "Voice.WhisperMissing" : "Voice.RecognizerMissing" };
            RecognitionStatus.Text = LocalizationService.Format(key, CultureInfo.GetCultureInfo(plan.Language).DisplayName);
            var catalogue = VoiceBankStore.Catalogue;
            GetRecognizer.Content = LocalizationService.Format("Voice.DownloadRecognizer",
                FileSizeFormatter.Format(catalogue.Recognition.Size + catalogue.RecognitionEngine.Size));
            GetRecognizer.Visibility = plan.Source == RecognitionSource.Missing ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { AppLogger.Warning("Could not enumerate recognizers.", ex); RecognitionStatus.Text = LocalizationService.Get("Voice.RecognitionError"); }
        try
        {
            var devices = MicrophoneCapture.Devices();
            _hasTestDevice = string.IsNullOrEmpty(prefs.MicrophoneId) ? devices.Count > 0 : devices.Any(d => d.Id == prefs.MicrophoneId);
            TestMic.IsEnabled = _hasTestDevice && _micTest == null;
            if (devices.Count == 0) MicStatus.Text = LocalizationService.Get("Voice.NoDevices");
            else if (MicStatus.Text == LocalizationService.Get("Voice.NoDevices")) MicStatus.Text = "";
            if (!string.IsNullOrEmpty(prefs.MicrophoneId) && devices.All(d => d.Id != prefs.MicrophoneId))
                devices.Add(new(prefs.MicrophoneId, LocalizationService.Get("Voice.MissingDevice")));
            devices.Insert(0, new("", LocalizationService.Get("Voice.SystemDevice")));
            BindVoiceChoice(VoiceDevice, devices, prefs.MicrophoneId ?? "");
        }
        catch (Exception ex)
        {
            _hasTestDevice = TestMic.IsEnabled = false;
            AppLogger.Warning("Microphone discovery failed.", ex);
            MicStatus.Text = LocalizationService.Get("Voice.MicError");
        }
    }

    private static void BindVoiceChoice(ComboBox box, List<DeviceChoice> choices, string id)
    {
        box.DisplayMemberPath = nameof(DeviceChoice.Name);
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => c.Id == id);
    }

    private void EnableVoice_Click(object sender, RoutedEventArgs e) =>
        ApplyChat(_manager.Data.Settings.Chat with { VoiceEnabled = EnableVoice.IsChecked == true });
    private void AutoSend_Click(object sender, RoutedEventArgs e) =>
        ApplyChat(_manager.Data.Settings.Chat with { SendOnRelease = VoiceAutoSend.IsChecked == true });
    private void RefreshVoice_Click(object sender, RoutedEventArgs e) => RefreshChatSettings();
    private void OpenSpeechPacks_Click(object sender, RoutedEventArgs e)
    {
        if (!WindowsVoices.OpenLanguageSettings()) RecognitionStatus.Text = LocalizationService.Get("Voice.WindowsPacksHint");
    }
    private void PreferWhisper_Click(object sender, RoutedEventArgs e) =>
        ApplyChat(_manager.Data.Settings.Chat with { PreferWhisper = PreferWhisper.IsChecked == true });

    private void VoiceChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingChat || sender is not ComboBox { SelectedItem: DeviceChoice choice } box) return;
        _micTest?.Cancel();
        MicStatus.Text = "";
        var prefs = _manager.Data.Settings.Chat;
        ApplyChat(box == VoiceDevice ? prefs with { MicrophoneId = choice.Id == "" ? null : choice.Id } : prefs with { InputLanguage = choice.Id });
    }

    private async void GetRecognizer_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceSetup != null || !VoiceOptions.IsEnabled) return;
        using var operation = new CancellationTokenSource();
        _voiceSetup = operation;
        GetRecognizer.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(n => { if (ReferenceEquals(_voiceSetup, operation)) RecognitionStatus.Text = LocalizationService.Format("Voice.Downloading", n); });
            await App.CurrentApp.Voice.Banks.InstallRecognitionAsync(progress, operation.Token);
            if (!operation.IsCancellationRequested) RefreshChatSettings();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { AppLogger.Warning("Could not install the recognizer.", ex); RecognitionStatus.Text = LocalizationService.Get("Voice.InstallError"); }
        finally { _voiceSetup = null; GetRecognizer.IsEnabled = true; }
    }

    private async void TestMic_Click(object sender, RoutedEventArgs e)
    {
        if (_micTest != null || !VoiceOptions.IsEnabled) return;
        using var operation = new CancellationTokenSource();
        _micTest = operation;
        TestMic.IsEnabled = false;
        var playing = false;
        try
        {
            MicStatus.Text = LocalizationService.Get("Voice.Testing");
            var peak = await MicrophoneTest.RunAsync(() => new MicrophoneCapture(_manager.Data.Settings.Chat.MicrophoneId),
                TimeSpan.FromSeconds(3), (wave, token) =>
                {
                    playing = true;
                    MicStatus.Text = LocalizationService.Get("Voice.MicPlayback");
                    return VoiceService.PlayWaveAsync(wave, token);
                }, operation.Token);
            MicStatus.Text = peak < 0.002 ? LocalizationService.Get("Voice.NoSignal") : LocalizationService.Format("Voice.Signal", Math.Round(peak * 100));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { MicStatus.Text = ""; }
        catch (Exception ex)
        {
            AppLogger.Warning("Microphone test failed.", ex);
            MicStatus.Text = LocalizationService.Get(playing ? "Voice.MicPlaybackError" : "Voice.MicError");
        }
        finally { _micTest = null; TestMic.IsEnabled = _hasTestDevice; }
    }
}
