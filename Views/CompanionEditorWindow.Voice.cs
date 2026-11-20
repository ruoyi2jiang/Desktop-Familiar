using System.Globalization;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Voice;
using Microsoft.Win32;

namespace DesktopFamiliar.Views;

public partial class CompanionEditorWindow
{
    private bool _refreshingVoice;
    private CancellationTokenSource? _voicePreview, _bankInstall;
    private static string V(string key) => LocalizationService.Get("Voice." + key);
    private void ProfileVoice_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _companion != null) RefreshBanks();
        else _voicePreview?.Cancel();
    }

    private void RefreshBanks_Click(object sender, RoutedEventArgs e) => RefreshBanks();
    private void OpenSpeechPacks_Click(object sender, RoutedEventArgs e)
    {
        if (!WindowsVoices.OpenLanguageSettings()) ProfileVoiceStatus.Text = V("WindowsPacksHint");
    }

    private void RefreshBanks()
    {
        _refreshingVoice = true;
        try
        {
            var voice = _companion.Voice.Normalize();
            var store = App.CurrentApp.Voice.Banks;
            var banks = store.Installed();
            var voices = new List<SpeakerChoice> { new("", LocalizationService.Get(VoiceBankStore.Catalogue.Windows.NameKey)), new("off", V("Off")) };
            voices.AddRange(WindowsVoices.Installed());
            voices.AddRange(banks.Select(b => new SpeakerChoice(b.Id, b.Name, "", b.Speakers)));
            if (voices.All(v => v.Id != voice.Id)) voices.Add(new(voice.Id, V("Unavailable")));
            VoiceBank.ItemsSource = voices;
            VoiceBank.SelectedItem = voices.First(v => v.Id == voice.Id);
            var languages = VoiceBankStore.Catalogue.Languages.Concat(voices.Select(v => v.Language)).Append(voice.ReplyLanguage)
                .Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(l => new DeviceChoice(l, CultureInfo.GetCultureInfo(l).DisplayName)).OrderBy(l => l.Name)
                .Prepend(new("", V("MatchMessage"))).ToList();
            ReplyLanguage.ItemsSource = languages;
            ReplyLanguage.SelectedItem = languages.First(l => l.Id == voice.ReplyLanguage);
            VoiceRate.Value = voice.Rate; VoicePitch.Value = voice.Pitch; VoiceGain.Value = voice.Volume;
            ShowSpeakerOptions(voice.Speaker);
            BankDownloads.Children.Clear();
            foreach (var asset in VoiceBankStore.Catalogue.Banks.Where(a => banks.All(b => b.Id != a.Id)))
            {
                var label = LocalizationService.Format("Voice.DownloadBank", LocalizationService.Get(asset.NameKey!),
                    FileSizeFormatter.Format(asset.Size + (store.HasVoiceEngine ? 0 : VoiceBankStore.Catalogue.VoiceEngine.Size)));
                var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = asset.Terms };
                button.Click += (_, _) => InstallVoice(asset);
                BankDownloads.Children.Add(button);
            }
            if (banks.Count > 0 && !store.HasVoiceEngine)
            {
                var button = new Button { Content = V("DownloadEngine"), HorizontalAlignment = HorizontalAlignment.Left };
                button.Click += (_, _) => InstallVoice(null);
                BankDownloads.Children.Add(button);
            }
        }
        catch (Exception ex) { AppLogger.Warning("Could not list speech voices.", ex); ProfileVoiceStatus.Text = V("ScanError"); }
        finally { _refreshingVoice = false; }
    }

    private void ShowSpeakerOptions(int index)
    {
        if (VoiceBank.SelectedItem is not SpeakerChoice choice) return;
        VoiceTuning.IsEnabled = choice.Id != "off";
        VoicePitch.IsEnabled = choice.Id == "" || choice.Id.StartsWith("windows:", StringComparison.Ordinal);
        SpeakerLabel.Visibility = VoiceSpeaker.Visibility = choice.Speakers > 1 ? Visibility.Visible : Visibility.Collapsed;
        VoiceSpeaker.ItemsSource = Enumerable.Range(1, choice.Speakers).Select(n => LocalizationService.Format("Voice.SpeakerNumber", n)).ToArray();
        VoiceSpeaker.SelectedIndex = Math.Clamp(index, 0, choice.Speakers - 1);
    }

    private void ProfileVoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingVoice || _companion == null || VoiceBank.SelectedItem is not SpeakerChoice choice) return;
        if (sender == VoiceBank)
        {
            _refreshingVoice = true;
            try { ShowSpeakerOptions(0); }
            finally { _refreshingVoice = false; }
        }
        _companion.Voice = _companion.Voice with { Id = choice.Id, Speaker = Math.Max(0, VoiceSpeaker.SelectedIndex),
            ReplyLanguage = (ReplyLanguage.SelectedItem as DeviceChoice)?.Id ?? "" };
        QueueVoicePreview();
    }

    private void VoiceTuning_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshingVoice || _companion == null || VoiceGain == null || VoiceBank.SelectedItem == null) return;
        _companion.Voice = _companion.Voice with { Rate = (int)VoiceRate.Value, Pitch = (int)VoicePitch.Value, Volume = (int)VoiceGain.Value };
        QueueVoicePreview();
    }

    private void PreviewVoice_Click(object sender, RoutedEventArgs e) => QueueVoicePreview(immediately: true);
    private async void QueueVoicePreview(bool immediately = false)
    {
        _voicePreview?.Cancel();
        ProfileVoiceStatus.Text = "";
        if (_companion.Voice.Id == "off" || !ProfileVoicePanel.IsVisible) return;
        using var operation = new CancellationTokenSource();
        _voicePreview = operation;
        try
        {
            if (!immediately) await Task.Delay(420, operation.Token);
            var line = _companion.GetDialogueExamples().FirstOrDefault() ?? V("Sample");
            line = line.Replace("{{user}}", UserNicknameBox.Text, StringComparison.OrdinalIgnoreCase);
            ProfileVoiceStatus.Text = V("Previewing");
            await App.CurrentApp.Voice.SpeakAsync(line[..Math.Min(220, line.Length)], _companion.Voice, operation.Token);
            if (!operation.IsCancellationRequested) ProfileVoiceStatus.Text = "";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { AppLogger.Warning("Voice preview failed.", ex); if (!operation.IsCancellationRequested) ProfileVoiceStatus.Text = V("PreviewError"); }
        finally { if (ReferenceEquals(_voicePreview, operation)) _voicePreview = null; }
    }

    private void ImportVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_bankInstall != null) return;
        var picker = new OpenFolderDialog { Title = V("Import") };
        if (picker.ShowDialog(this) == true) InstallVoice(null, picker.FolderName);
    }

    private async void InstallVoice(SpeechAsset? asset, string? folder = null)
    {
        if (_bankInstall != null) return;
        _voicePreview?.Cancel();
        using var operation = new CancellationTokenSource();
        _bankInstall = operation;
        BankDownloads.IsEnabled = false;
        try
        {
            var store = App.CurrentApp.Voice.Banks;
            var progress = new Progress<int>(n => { if (ReferenceEquals(_bankInstall, operation)) ProfileVoiceStatus.Text = LocalizationService.Format("Voice.Downloading", n); });
            string? id = asset?.Id;
            if (folder != null)
            {
                ProfileVoiceStatus.Text = V("Importing");
                id = (await store.ImportAsync(folder, operation.Token)).Id;
            }
            else if (asset != null) await store.InstallBankAsync(asset, progress, operation.Token);
            else await store.InstallEngineAsync(progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (id != null) _companion.Voice = _companion.Voice with { Id = id, Speaker = 0 };
            RefreshBanks();
            if (store.HasVoiceEngine) QueueVoicePreview();
            else ProfileVoiceStatus.Text = V("NeedsEngine");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { AppLogger.Warning("Could not set up a voice bank.", ex); ProfileVoiceStatus.Text = V(folder == null ? "InstallError" : "ImportError"); }
        finally { _bankInstall = null; BankDownloads.IsEnabled = true; }
    }
}
