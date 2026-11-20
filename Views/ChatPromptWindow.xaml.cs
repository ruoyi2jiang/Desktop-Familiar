using System.Windows.Input;
using DesktopFamiliar.Models;
using DesktopFamiliar.Views.Controls;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Voice;
using System.Windows.Threading;

namespace DesktopFamiliar.Views;

public partial class ChatPromptWindow : Window
{
    private readonly Func<string, Task<bool>> _send;

    private readonly ChatPreferences _prefs;
    private readonly CompanionOverlayWindow _overlay;
    private readonly PushToTalkSession _talk;
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Func<bool>? _held;
    private long _start;

    public ChatPromptWindow(CompanionOverlayWindow overlay, CompanionDefinition pet, Func<string, Task<bool>> send, ChatPreferences prefs)
    {
        InitializeComponent();
        _send = send;
        _prefs = prefs;
        _talk = new(() => new MicrophoneCapture(prefs.MicrophoneId),
            (audio, token) => App.CurrentApp.Voice.TranscribeAsync(audio, prefs, token));
        _overlay = overlay;
        TalkButton.Visibility = prefs.VoiceEnabled ? Visibility.Visible : Visibility.Collapsed;
        _holdTimer.Tick += (_, _) =>
        {
            if ((_held != null && !_held()) || Environment.TickCount64 - _start >= MicrophoneCapture.MaximumSeconds * 1000)
                EndTalk();
        };
        Owner = overlay;
        CompanionBubbleAppearance.Apply(Surface, pet);
        // Establish the owner-relative width before WPF creates the native window.
        overlay.SetChatPrompt(this);
        Loaded += (_, _) => { overlay.SetChatPrompt(this); Input.Focus(); };
        SizeChanged += (_, e) =>
        {
            if (e.HeightChanged && IsLoaded && IsVisible) overlay.SetChatPrompt(this);
        };
        Closed += (_, _) =>
        {
            _talk.Dispose();
            overlay.SetVoiceInputActive(false);
            _holdTimer.Stop();
            overlay.SetChatPrompt(null);
        };
    }

    public void FocusPrompt() { Activate(); Keyboard.Focus(Input); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Prompt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.Enter && Input.IsKeyboardFocused) { e.Handled = true; await SubmitAsync(); }
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0 || !SendButton.IsEnabled || _talk.Recording || _talk.Transcribing) return;
        if (!PromptFits()) return;
        Status(null);
        Input.Clear();
        SendButton.IsEnabled = false;
        Input.Focus();
        try
        {
            if (!await _send(text) && Input.Text.Length == 0) Input.Text = text;
        }
        finally { SendButton.IsEnabled = true; }
    }

    private void Status(string? key)
    {
        VoiceStatus.Text = key == null ? "" : LocalizationService.Get(key);
        VoiceStatus.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool PromptFits()
    {
        if (Input.Text.Length <= Input.MaxLength) return true;
        VoiceStatus.Text = LocalizationService.Format("Voice.PromptTooLong", Input.MaxLength);
        VoiceStatus.Visibility = Visibility.Visible;
        return false;
    }

    internal void BeginTalk(Func<bool>? held = null)
    {
        if (!_prefs.VoiceEnabled || _talk.Recording || _talk.Transcribing || !SendButton.IsEnabled || !IsVisible) return;
        try
        {
            if (App.CurrentApp.Voice.Plan(_prefs).Source == RecognitionSource.Missing) { Status("Voice.NeedRecognition"); return; }
            _overlay.SetVoiceInputActive(true);
            if (!_talk.Begin()) return;
            _held = held;
            _start = Environment.TickCount64;
            _holdTimer.Start();
            Status("Voice.Listening");
        }
        catch (Exception ex)
        {
            _overlay.SetVoiceInputActive(false);
            AppLogger.Warning("Could not start push-to-talk.", ex);
            Status("Voice.MicError");
        }
    }

    private async void EndTalk()
    {
        if (!_talk.Recording || !IsVisible) return;
        _holdTimer.Stop();
        _held = null;
        var transcript = _talk.ReleaseAsync();
        _overlay.SetVoiceInputActive(false);
        if (TalkButton.IsMouseCaptured) TalkButton.ReleaseMouseCapture();
        Status("Voice.Transcribing");
        try
        {
            var text = await transcript;
            if (_talk.Closed) return;
            if (string.IsNullOrWhiteSpace(text)) { Status("Voice.NoSpeech"); return; }
            Input.Text = PushToTalkSession.AppendTranscript(Input.Text, text);
            Input.CaretIndex = Input.Text.Length;
            Status(null);
            Input.Focus();
            if (PromptFits() && _prefs.SendOnRelease) await SubmitAsync();
        }
        catch (OperationCanceledException) when (_talk.Closed) { }
        catch (Exception ex) { AppLogger.Warning("Could not transcribe the recording.", ex); if (!_talk.Closed) Status("Voice.RecognitionError"); }
    }

    private void Talk_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BeginTalk();
        if (_talk.Recording) TalkButton.CaptureMouse();
    }
    private void Talk_Up(object sender, MouseButtonEventArgs e) { e.Handled = true; EndTalk(); }
    private void Talk_LostCapture(object sender, MouseEventArgs e) { if (_held == null) EndTalk(); }
    private void Talk_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        e.Handled = true;
        if (!e.IsRepeat) BeginTalk(() => Keyboard.IsKeyDown(Key.Space));
    }
    private void Talk_KeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Space) { e.Handled = true; EndTalk(); } }
}
