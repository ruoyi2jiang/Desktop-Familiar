using System.Diagnostics;
using System.Windows.Threading;
using System.Drawing;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using MonitorScreen = System.Windows.Forms.Screen;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Views;

public partial class CompanionOverlayWindow
{
    private readonly ChatReplyPages _replies = new();
    private readonly DispatcherTimer _chatClock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _readClock = new();
    private ChatReplyPages.Page? _voicedPage;
    private bool _chatTurnPending;
    private bool ChatInProgress => _chatTurnPending || _replies.HasPages;
    private bool _voiceInputActive;
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
        var placement = OverlayLayoutPolicy.PlaceChatPrompt(character, workArea, _chatPrompt.ActualHeight, dialogueHeight);
        _chatPrompt.Width = placement.Width;
        _chatPrompt.Left = placement.Left;
        _chatPrompt.Top = placement.Top;
        return placement.DialogueClearance;
    }

    internal void BeginChatTurn()
    {
        _chatTurnPending = true;
        _chatSpeech.Stop();
        _replies.DiscardSuperseded(keepVisiblePage: ChatReplyBorder.IsVisible);
        InterruptAmbientIdle();
    }

    internal void EndChatTurn()
    {
        _chatTurnPending = false;
        SetThinking(null);
    }

    private bool InterruptAmbientIdle()
    {
        if (!ChatInProgress || !IsVisible || _closingForExit || _showingAddonStatus || _activeReminder != null
            || _manager.ActivityState != CompanionActivityState.Idle || !ScenePlaybackPolicy.IsAmbientIdle(_sceneRole)
            || _playbackCompanion is not { } pet)
            return false;

        if (_sceneRole != AnimationSceneRole.IdleDefault || _afterLoopFinishes != null || !IsPlaybackLooping)
        {
            // StartScene cancels outgoing lines and cues, and blends from the displayed pose.
            StartDefaultForCurrentState(pet, transition: true);
            return true;
        }
        if (!HasSceneDialog) return false;
        _dialogIndex = _activeScene!.DialogLines.Count;
        StopDialog();
        return true;
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
        _replies.Enqueue(reply, playback);
        RenderChat();
    }

    public void ResetChat()
    {
        _chatTurnPending = false;
        _chatSpeech.Stop();
        _voicedPage = null;
        _replies.DiscardSuperseded(keepVisiblePage: false);
        _thinking = null;
        _readClock.Reset();
        RenderChat();
    }

    private void RenderChat(bool updateAnimation = true)
    {
        var previous = _replies.Current;
        _replies.Update(_readClock.Elapsed, _chatSpeech.Playing);
        var reading = _replies.Current;
        if (!ReferenceEquals(previous, reading)) _readClock.Reset();
        var text = reading?.Text ?? _thinking;
        var visible = text != null && IsVisible && BubbleRoot.Visibility != Visibility.Visible
            && !_sceneSpeech.Playing && _voicePlayer == null;
        if (!visible || _manager.Data.Settings.FocusModeEnabled)
            _chatSpeech.Stop();
        else if (!_voiceInputActive && reading is { Playback.SpeechInterrupted: false } spoken && !ReferenceEquals(_voicedPage, spoken)
            && _manager.CurrentCompanion is { Voice.Id: not "off" } companion)
        {
            _voicedPage = spoken;
            _ = _chatSpeech.PlayAsync(spoken.Text, companion.Voice);
        }
        if (visible && reading != null) _readClock.Start();
        else _readClock.Stop();
        ChatReplyBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ChatReplyText.Text = text ?? string.Empty;
        if (text == null) _chatClock.Stop();
        else _chatClock.Start();
        UpdateDialogAreaForContent();
        if (updateAnimation) UpdateChatReplyAnimation(visible ? reading?.Playback : null);
    }

    internal void SetVoiceInputActive(bool active)
    {
        if (_voiceInputActive == active) return;
        _voiceInputActive = active;
        if (_voicePlayer != null) _voicePlayer.IsMuted = active;
        if (!active) return;
        if (_sceneSpeech.Playing)
        {
            _sceneSpeech.Stop();
            StartLineReadingTimer();
        }
        _replies.InterruptSpeech();
        _chatSpeech.Stop();
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
                StartDefaultForCurrentState(pet, transition: true);
            return;
        }

        if (_showingAddonStatus || _pointerDown || _afterLoopFinishes != null
            || (pet.Appearance == CompanionAppearance.Images2D && TransitionImage.Source != null)
            || !ScenePlaybackPolicy.CanStartChatReply(_sceneRole, _manager.ActivityState, HasActiveDialog, _activeScene?.Id, scene.Id))
            return;

        _idleTimer.Stop();
        StartScene(scene, AnimationSceneRole.ChatReply, forceLoop: true, transition: true);
        if (_activeScenePlaybackFailed)
        {
            playback!.MarkFailed(scene.Id);
            StartDefaultForCurrentState(pet);
        }
    }
}
