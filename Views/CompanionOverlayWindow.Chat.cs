using System.Diagnostics;
using System.Windows.Threading;
using System.Drawing;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using MonitorScreen = System.Windows.Forms.Screen;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Views;

public partial class CompanionOverlayWindow
{
    private sealed record ChatPage(string Text, ChatReplyPlayback? Playback);
    private readonly Queue<ChatPage> _chatPages = new();
    private readonly DispatcherTimer _chatClock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _readClock = new();
    private ChatPage? _reading;
    private string? _thinking;

    private ChatPromptWindow? _chatPrompt;
    private bool _promptLayoutQueued;

    internal void SetChatPrompt(ChatPromptWindow? prompt)
    {
        _chatPrompt = prompt;
        UpdateDialogAreaForContent();
    }

    private void QueuePromptLayout()
    {
        if (_chatPrompt == null || _promptLayoutQueued) return;
        _promptLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _promptLayoutQueued = false;
            if (_chatPrompt != null) UpdateDialogAreaForContent();
        }));
    }

    private double ArrangeChatPrompt(double characterTop, double dialogueHeight)
    {
        if (_chatPrompt == null) return 0;
        var settings = _manager.Data.Settings;
        var character = new RectangleF((float)_characterPosition.X,
            (float)characterTop, settings.ViewportWidth, settings.ViewportHeight);
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new System.Drawing.Point((int)Math.Round((character.Left + character.Width / 2) * dpi.DpiScaleX),
            (int)Math.Round((characterTop + character.Height / 2) * dpi.DpiScaleY));
        var pixels = MonitorScreen.FromPoint(center).WorkingArea;
        var workArea = new RectangleF((float)(pixels.Left / dpi.DpiScaleX), (float)(pixels.Top / dpi.DpiScaleY),
            (float)(pixels.Width / dpi.DpiScaleX), (float)(pixels.Height / dpi.DpiScaleY));
        var placement = OverlayLayoutPolicy.PlaceChatPrompt(character, workArea, _chatPrompt.Height, dialogueHeight);
        _chatPrompt.Width = placement.Width;
        _chatPrompt.Left = placement.Left;
        _chatPrompt.Top = placement.Top;
        return placement.DialogueClearance;
    }

    public void SetThinking(string? text)
    {
        _thinking = text;
        RenderChat();
    }

    public void QueueReply(string reply, bool animate = true, Guid? expressionSceneId = null)
    {
        _thinking = null;
        var playback = animate ? new ChatReplyPlayback(expressionSceneId) : null;
        foreach (var page in ChatBubbleTiming.Pages(reply)) _chatPages.Enqueue(new(page, playback));
        RenderChat();
    }

    public void ResetChat()
    {
        _chatPages.Clear();
        _reading = null;
        _thinking = null;
        _readClock.Reset();
        RenderChat();
    }

    private void RenderChat(bool updateAnimation = true)
    {
        if (_reading != null && _readClock.Elapsed >= ChatBubbleTiming.ReadingTime(_reading.Text))
        {
            _reading = null;
            _readClock.Reset();
        }
        if (_reading == null && _chatPages.TryDequeue(out var page)) _reading = page;
        var text = _reading?.Text ?? _thinking;
        var visible = text != null && IsVisible && BubbleRoot.Visibility != Visibility.Visible;
        if (visible && _reading != null) _readClock.Start();
        else _readClock.Stop();
        ChatReplyBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ChatReplyText.Text = text ?? string.Empty;
        if (text == null) _chatClock.Stop();
        else _chatClock.Start();
        UpdateDialogAreaForContent();
        if (updateAnimation) UpdateChatReplyAnimation(visible ? _reading?.Playback : null);
    }

    private void UpdateChatReplyAnimation(ChatReplyPlayback? playback)
    {
        if (_closingForExit || !IsVisible || _playbackCompanion is not { } pet
            || pet.Id != _manager.CurrentCompanion?.Id)
            return;

        var scene = playback?.Scene(pet.ActiveScenes);
        if (scene == null || _manager.Data.Settings.FocusModeEnabled)
        {
            if (_sceneRole == AnimationSceneRole.ChatReply && !FinishHeldModelClip(pet))
                StartDefaultForCurrentState(pet);
            return;
        }

        if (_showingAddonStatus || _pointerDown || _afterLoopFinishes != null
            || !ScenePlaybackPolicy.CanStartChatReply(_sceneRole, _manager.ActivityState, HasActiveDialog, _activeScene?.Id, scene.Id))
            return;

        _idleTimer.Stop();
        StartScene(scene, AnimationSceneRole.ChatReply, forceLoop: true);
        if (_activeScenePlaybackFailed)
        {
            playback!.MarkFailed(scene.Id);
            StartDefaultForCurrentState(pet);
        }
    }
}
