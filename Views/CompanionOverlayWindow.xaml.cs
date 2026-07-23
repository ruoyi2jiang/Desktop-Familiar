using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Dialogs;
using DesktopFamiliar.Services.Focus;
using DesktopFamiliar.Views.Controls;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace DesktopFamiliar.Views;

public partial class CompanionOverlayWindow : Window
{
    private const double ReservedDialogHeight = 96;

    private readonly CompanionManager _manager;
    private readonly FrameAnimationPlayer _animationPlayer;
    private readonly DispatcherTimer _idleTimer = new();
    private readonly DispatcherTimer _dialogTimer = new();
    private readonly MediaPlayer _voicePlayer = new();
    private readonly Random _random = new();
    private AnimationScene? _activeScene;
    private AnimationSceneRole _sceneRole;
    private int _dialogIndex;
    private bool _closingForExit;
    private bool _dragged;
    private bool _playStartOnNextShow = true;
    private Action? _afterLoopFinishes;
    private int? _pendingDialogStartFrame;
    // The restore indicator is armed only by the user's hide action.
    private bool _inactiveDialogButtonDismissed = true;
    private double _dialogAreaHeight = ReservedDialogHeight;
    private CalendarEvent? _activeReminder;
    private PendingCalendarReminder? _pendingReminder;
    private bool _activeScenePlaybackFailed;
    private Action? _exitCompleted;
    private bool _exitCloseCompleted;

    public CompanionOverlayWindow(CompanionManager manager)
    {
        InitializeComponent();
        _manager = manager;
        ApplyViewportSize();
        ApplySavedPosition();
        _animationPlayer = new FrameAnimationPlayer(SetFrame);
        _animationPlayer.CycleCompleted += AnimationCycleCompleted;
        _animationPlayer.FramePresented += AnimationFramePresented;
        _idleTimer.Tick += IdleTimerTick;
        _dialogTimer.Tick += DialogTimerTick;
        _voicePlayer.MediaEnded += VoicePlayerEnded;
        _voicePlayer.MediaFailed += VoicePlayerFailed;
        _manager.CurrentCompanionChanged += ManagerCurrentCompanionChanged;
        _manager.ActivityStateChanged += ManagerStateChanged;
        _manager.ViewportChanged += ManagerViewportChanged;
        _manager.DialogPresentationChanged += ManagerDialogPresentationChanged;
        _manager.IdleIntervalChanged += ManagerIdleIntervalChanged;
        _manager.UserBirthdayChanged += ManagerUserBirthdayChanged;
        Loaded += (_, _) =>
        {
            UpdateDialogBubbleVisibility();
            UpdateDialogAreaForContent();
        };
        Closing += (_, args) =>
        {
            if (!_closingForExit)
                args.Cancel = true;
        };
        BuildContextMenu();
        ContextMenuOpening += OverlayContextMenuOpening;
    }

    private void OverlayContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!FocusModeVisibilityPolicy.CanOpenOverlayContextMenu(_manager.Data.Settings.FocusModeEnabled))
        {
            e.Handled = true;
            return;
        }

        BuildContextMenu();
    }

    private void BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource("Windows11ContextMenu")
        };

        var enterFocus = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("FocusMode.Enter"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        enterFocus.Click += (_, _) => _manager.SetFocusMode(true);
        menu.Items.Add(enterFocus);

        if (_manager.ActivityState == CompanionActivityState.Idle)
        {
            var randomIdle = new System.Windows.Controls.MenuItem
            {
                Header = LocalizationService.Get("Overlay.PlayRandomIdle"),
                IsEnabled = _manager.CurrentCompanion is { } companion
                    && ScenePlaybackPolicy.GetRandomIdleCandidates(companion).Count > 0,
                Style = (Style)FindResource("Windows11MenuItem")
            };
            randomIdle.Click += (_, _) => PlayRandomIdleNow();
            menu.Items.Add(randomIdle);
        }
        var configure = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Overlay.ConfigureCompanions"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        configure.Click += (_, _) => App.CurrentApp.ShowLibrary();
        var setEvent = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Calendar.Menu.SetEvent"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        setEvent.Click += (_, _) => App.CurrentApp.ShowCalendarEvents();
        var exit = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Overlay.Exit"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        exit.Click += (_, _) => App.CurrentApp.ShutdownApplication();
        menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("Windows11MenuSeparator") });
        menu.Items.Add(setEvent);
        menu.Items.Add(configure);
        menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("Windows11MenuSeparator") });
        menu.Items.Add(exit);
        ContextMenu = menu;
    }

    public void ShowCompanion()
    {
        if (IsVisible)
            return;

        _playStartOnNextShow = true;
        // Replace the retained image while the transparent window is still
        // hidden so WPF can never paint a frame from the previous state.
        RefreshCompanion();
        Show();
    }

    public void HideCompanion()
    {
        StopAll(clearFrame: true);
        Hide();
    }

    public void ShowCalendarReminder(CalendarEvent calendarEvent, DateTime occurrenceStart)
    {
        _pendingReminder = new PendingCalendarReminder(calendarEvent.Clone(), occurrenceStart);
        _inactiveDialogButtonDismissed = true;
        TryPresentPendingReminder();
    }

    private bool TryPresentPendingReminder()
    {
        if (_pendingReminder is not { } pending
            || _activeReminder is not null
            || _manager.ActivityState != CompanionActivityState.Reminder)
            return false;

        if (!ScenePlaybackPolicy.CanPresentReminder(
                _sceneRole,
                HasSceneDialog || _pendingDialogStartFrame is not null,
                _activeScenePlaybackFailed))
        {
            return false;
        }

        _pendingReminder = null;
        _activeReminder = pending.CalendarEvent;
        _dialogTimer.Stop();
        _voicePlayer.Stop();
        _voicePlayer.Close();
        ReminderActions.Visibility = Visibility.Visible;
        RemindLaterButton.Content = LocalizationService.Format(
            "Calendar.Reminder.RemindLater", Math.Clamp(pending.CalendarEvent.SnoozeMinutes, 1, 1440));
        AcknowledgeReminderButton.Content = LocalizationService.Get("Calendar.Reminder.Acknowledge");
        DialogText.Text = BuildReminderText(pending.CalendarEvent, pending.OccurrenceStart);
        ApplyDialogTextPresentation();
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
        return true;
    }

    public void ClearCalendarReminder()
    {
        _pendingReminder = null;
        _activeReminder = null;
        _inactiveDialogButtonDismissed = true;
        ReminderActions.Visibility = Visibility.Collapsed;
        DialogText.Text = string.Empty;
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
    }

    private void PlayRandomIdleNow()
    {
        if (_manager.ActivityState != CompanionActivityState.Idle
            || _manager.CurrentCompanion is not { } companion)
            return;
        StartRandomIdle(companion);
    }

    private void ApplySavedPosition()
    {
        var characterHeight = _manager.Data.Settings.ViewportHeight;
        if (_manager.Data.Settings.CompanionLeft is double left && _manager.Data.Settings.CompanionTop is double top)
        {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
            Top = Math.Clamp(top - _dialogAreaHeight, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            var characterTop = SystemParameters.WorkArea.Bottom - characterHeight - 24;
            Top = Math.Clamp(characterTop - _dialogAreaHeight, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        }
    }

    private void ApplyViewportSize()
    {
        Width = _manager.Data.Settings.ViewportWidth;
        CharacterRow.Height = new GridLength(_manager.Data.Settings.ViewportHeight);
        BubbleRoot.Width = Width;
        BubbleRoot.MaxWidth = Width;
        SetDialogAreaHeight(ReservedDialogHeight, preserveCharacterPosition: false);
        ApplyDialogTextPresentation();
    }

    private void ManagerStateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        if (!IsVisible)
            return;

        if (_sceneRole is AnimationSceneRole.IdleDefault
                or AnimationSceneRole.Reminder
                or AnimationSceneRole.ReminderFallback
                or AnimationSceneRole.IdleRandom
                or AnimationSceneRole.SelfBirthday
                or AnimationSceneRole.UserBirthday
            && FinishCurrentLoopThen(RefreshCompanion))
            return;

        RefreshCompanion();
    });

    private void ManagerCurrentCompanionChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        _inactiveDialogButtonDismissed = true;
        if (IsVisible)
            RefreshCompanion();
    });

    private void ManagerViewportChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        ApplyViewportSize();
        ApplySavedPosition();
        UpdateDialogAreaForContent();
    });

    private void ManagerDialogPresentationChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        ApplyDialogTextPresentation();
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
    });

    private void ManagerIdleIntervalChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        if (IsVisible && _manager.ActivityState == CompanionActivityState.Idle)
            ScheduleRandomIdle();
    });

    private void ManagerUserBirthdayChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        if (IsVisible && _manager.ActivityState == CompanionActivityState.Idle)
            ScheduleRandomIdle();
    });

    private void ApplyDialogTextPresentation()
    {
        var truncate = _manager.Data.Settings.TruncateLongDialogLines;
        DialogText.TextWrapping = truncate ? TextWrapping.NoWrap : TextWrapping.Wrap;
        DialogText.TextTrimming = truncate ? TextTrimming.CharacterEllipsis : TextTrimming.None;
    }

    private void UpdateDialogAreaForContent()
    {
        var desiredHeight = ReservedDialogHeight;
        if (BubbleRoot.Visibility == Visibility.Visible)
        {
            // Measure the bubble directly. Measuring DialogArea reuses the
            // current fixed row constraint and can clip newly added reminder
            // actions, particularly when a snoozed reminder is shown again.
            BubbleRoot.Measure(new Size(Math.Max(Width, 1), double.PositiveInfinity));
            desiredHeight = Math.Max(ReservedDialogHeight, BubbleRoot.DesiredSize.Height);
        }
        SetDialogAreaHeight(desiredHeight, preserveCharacterPosition: IsVisible);
    }

    private void SetDialogAreaHeight(double height, bool preserveCharacterPosition)
    {
        height = Math.Max(ReservedDialogHeight, Math.Ceiling(height));
        var characterTop = Top + _dialogAreaHeight;
        _dialogAreaHeight = height;
        DialogRow.Height = new GridLength(_dialogAreaHeight);
        Height = _dialogAreaHeight + _manager.Data.Settings.ViewportHeight;

        if (preserveCharacterPosition)
        {
            Top = Math.Clamp(
                characterTop - _dialogAreaHeight,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        }
    }

    private void RefreshCompanion()
    {
        StopAll(clearFrame: true);
        var companion = _manager.CurrentCompanion;
        if (companion is null)
            return;

        ApplyAppearance(companion);
        if (_playStartOnNextShow
            && _manager.ActivityState == CompanionActivityState.Idle
            && companion.StartAnimation is { } startScene)
        {
            _playStartOnNextShow = false;
            StartScene(startScene, AnimationSceneRole.Start, forceLoop: false, forceOneShot: true);
            return;
        }

        _playStartOnNextShow = false;
        StartDefaultForCurrentState(companion);
    }

    private void ApplyAppearance(CompanionDefinition companion)
    {
        DialogText.Foreground = TryBrush(companion.FontColor, (Brush)FindResource(ThemeTokens.TextOnOverlay));
        DialogText.FontFamily = ResolveFont(companion);
        ApplyDialogTextPresentation();
        MissingAssetText.Text = companion.Name;

        if (!string.IsNullOrWhiteSpace(companion.BubbleImagePath) && File.Exists(companion.BubbleImagePath))
        {
            try
            {
                BubbleImage.Source = LoadBitmap(companion.BubbleImagePath);
                BubbleImage.Visibility = Visibility.Visible;
                BubbleBorder.Background = Brushes.Transparent;
                BubbleBorder.Padding = new Thickness(22, 16, 22, 16);
            }
            catch (Exception exception)
            {
                AppLogger.Warning("The imported chat-bubble image could not be displayed; the default bubble will be used.", exception);
                ApplyDefaultBubble();
            }
        }
        else
        {
            ApplyDefaultBubble();
        }
    }

    private void ApplyDefaultBubble()
    {
        BubbleImage.Source = null;
        BubbleImage.Visibility = Visibility.Collapsed;
        BubbleBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, ThemeTokens.OverlaySurface);
        BubbleBorder.Padding = new Thickness(16, 12, 16, 12);
    }

    private static FontFamily ResolveFont(CompanionDefinition companion)
    {
        if (!string.IsNullOrWhiteSpace(companion.ImportedFontPath) && File.Exists(companion.ImportedFontPath))
        {
            try
            {
                var folder = new Uri(Path.GetDirectoryName(companion.ImportedFontPath)! + Path.DirectorySeparatorChar, UriKind.Absolute);
                return new FontFamily(folder, $"./#{companion.FontFamilyName}");
            }
            catch (Exception exception)
            {
                AppLogger.Warning("The imported font could not be loaded; an installed font will be used.", exception);
                // Fall through to an installed font.
            }
        }
        return new FontFamily(companion.FontFamilyName);
    }

    private void StartDefaultForCurrentState(CompanionDefinition companion)
    {
        if (_manager.ActivityState == CompanionActivityState.Reminder)
        {
            StartReminderState(companion);
            return;
        }

        StartIdleDefault(companion);
        ScheduleRandomIdle();
    }

    private void StartReminderState(CompanionDefinition companion)
    {
        if (companion.ReminderAnimation is { } reminderScene)
        {
            StartScene(reminderScene, AnimationSceneRole.Reminder, forceLoop: true);
            return;
        }

        if (companion.IdleDefaultAnimation is { } idleScene)
        {
            StartScene(idleScene, AnimationSceneRole.ReminderFallback, forceLoop: true);
            return;
        }

        ShowMissingAsset(LocalizationService.Get("Overlay.IdleDefaultNotAssigned"));
        TryPresentPendingReminder();
    }

    private void StartIdleDefault(CompanionDefinition companion)
    {
        var scene = companion.IdleDefaultAnimation;
        if (scene is null)
        {
            ShowMissingAsset(LocalizationService.Get("Overlay.IdleDefaultNotAssigned"));
            return;
        }
        StartScene(scene, AnimationSceneRole.IdleDefault, forceLoop: true);
    }

    private void ScheduleRandomIdle()
    {
        _idleTimer.Stop();
        if (_manager.ActivityState != CompanionActivityState.Idle
            || _manager.CurrentCompanion is not { } companion
            || !IdleSceneSelectionPolicy.HasAnyCandidate(
                companion,
                _manager.Data.Settings.UserBirthday,
                DateTime.Today))
        {
            return;
        }

        _idleTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(
            _manager.Data.Settings.IdleDelaySeconds,
            AppSettings.MinimumIdleDelaySeconds,
            AppSettings.MaximumIdleDelaySeconds));
        _idleTimer.Start();
    }

    private void IdleTimerTick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        var companion = _manager.CurrentCompanion;
        if (companion is null || _manager.ActivityState != CompanionActivityState.Idle)
            return;

        if (!FinishCurrentLoopThen(() =>
            {
                if (_manager.ActivityState == CompanionActivityState.Idle
                    && _manager.CurrentCompanion is { } current)
                {
                    StartScheduledIdleScene(current);
                }
            }))
        {
            StartScheduledIdleScene(companion);
        }
    }

    private void StartScheduledIdleScene(CompanionDefinition companion)
    {
        var selection = IdleSceneSelectionPolicy.Select(
            companion,
            _manager.Data.Settings.UserBirthday,
            DateTime.Today,
            _random.NextDouble());
        if (selection is null)
        {
            if (_sceneRole != AnimationSceneRole.IdleDefault || !_animationPlayer.IsLooping)
                StartIdleDefault(companion);
            ScheduleRandomIdle();
            return;
        }

        StartScene(
            selection.Candidates[_random.Next(selection.Candidates.Count)],
            selection.Role,
            forceLoop: false);
    }

    private void StartRandomIdle(CompanionDefinition companion)
    {
        var scenes = ScenePlaybackPolicy.GetRandomIdleCandidates(companion);
        if (scenes.Count == 0)
        {
            // A scene may have been removed after the timer was armed. If the
            // default loop was already being finished, restore it immediately.
            if (_sceneRole != AnimationSceneRole.IdleDefault || !_animationPlayer.IsLooping)
                StartIdleDefault(companion);
            ScheduleRandomIdle();
            return;
        }
        StartScene(scenes[_random.Next(scenes.Count)], AnimationSceneRole.IdleRandom, forceLoop: false);
    }

    private void StartScene(
        AnimationScene scene,
        AnimationSceneRole role,
        bool forceLoop,
        bool forceOneShot = false)
    {
        _afterLoopFinishes = null;
        StopDialog();
        _activeScenePlaybackFailed = false;
        _activeScene = scene;
        _sceneRole = role;
        MissingAssetCard.Visibility = Visibility.Collapsed;
        CompanionImage.Visibility = Visibility.Visible;
        var hasDialog = ScenePlaybackPolicy.ShouldPresentSceneDialog(
                _manager.Data.Settings.FocusModeEnabled,
                role)
            && scene.DialogLines.Any(line =>
                !string.IsNullOrWhiteSpace(line.Text)
                || !string.IsNullOrWhiteSpace(line.VoicePath));
        var loop = forceLoop || (!forceOneShot && scene.PlaybackMode == ScenePlaybackMode.LoopUntilDialogEnds && hasDialog);
        _dialogIndex = 0;
        _pendingDialogStartFrame = hasDialog
            ? AnimationFrameSettingsPolicy.GetRuntimeDialogStartFrame(scene)
            : null;
        try
        {
            _animationPlayer.Play(scene, loop);
            if (_pendingDialogStartFrame is int pending
                && pending >= _animationPlayer.FrameCount)
            {
                _pendingDialogStartFrame = Math.Max(_animationPlayer.FrameCount - 1, 0);
            }
            if (_pendingDialogStartFrame == _animationPlayer.CurrentFrameIndex)
            {
                _pendingDialogStartFrame = null;
                ShowDialogLine();
            }
            TryPresentPendingReminder();
        }
        catch (Exception exception)
        {
            _pendingDialogStartFrame = null;
            _activeScenePlaybackFailed = true;
            AppLogger.Error($"Animation scene '{scene.DisplayName}' could not be played.", exception);
            ShowMissingAsset(LocalizationService.Format("Overlay.CouldNotPlay", scene.Name, exception.Message));
            TryPresentPendingReminder();
            if (role == AnimationSceneRole.Exit)
            {
                CompleteCloseForExit();
                return;
            }
            if (_manager.ActivityState == CompanionActivityState.Idle)
                ScheduleRandomIdle();
            return;
        }

    }

    private void AnimationFramePresented(object? sender, AnimationFramePresentedEventArgs e)
    {
        if (_pendingDialogStartFrame != e.FrameIndex)
            return;

        _pendingDialogStartFrame = null;
        ShowDialogLine();
    }

    private void ShowDialogLine()
    {
        if (_activeScene is null || _dialogIndex >= _activeScene.DialogLines.Count)
        {
            DialogFinished();
            return;
        }

        var line = _activeScene.DialogLines[_dialogIndex];
        if (string.IsNullOrWhiteSpace(line.Text) && string.IsNullOrWhiteSpace(line.VoicePath))
        {
            _dialogIndex++;
            ShowDialogLine();
            return;
        }

        var displayedText = FormatDialogText(line.Text);
        if (_activeReminder is not null)
        {
            UpdateDialogBubbleVisibility();
            return;
        }
        DialogText.Text = displayedText;
        _inactiveDialogButtonDismissed = false;
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
        _dialogTimer.Stop();
        _voicePlayer.Close();

        if (!string.IsNullOrWhiteSpace(line.VoicePath) && File.Exists(line.VoicePath))
        {
            try
            {
                _voicePlayer.Open(new Uri(line.VoicePath, UriKind.Absolute));
                _voicePlayer.Play();
                return;
            }
            catch (Exception exception)
            {
                AppLogger.Warning("A voice line could not be opened; text timing will be used.", exception);
                // Fall back to text timing if the audio cannot be opened.
            }
        }

        var speed = Math.Clamp(_activeScene.DialogCharactersPerSecond, 5, 40);
        var seconds = Math.Clamp(1.2 + displayedText.Length / speed, 1.5, 12);
        _dialogTimer.Interval = TimeSpan.FromSeconds(seconds);
        _dialogTimer.Start();
    }

    private void DialogTimerTick(object? sender, EventArgs e)
    {
        _dialogTimer.Stop();
        AdvanceDialog();
    }

    private void VoicePlayerEnded(object? sender, EventArgs e) => Dispatcher.Invoke(AdvanceDialog);

    private void VoicePlayerFailed(object? sender, ExceptionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_activeScene is null)
                return;
            var line = _activeScene.DialogLines[Math.Min(_dialogIndex, _activeScene.DialogLines.Count - 1)];
            var displayedText = FormatDialogText(line.Text);
            var seconds = Math.Clamp(1.2 + displayedText.Length / Math.Clamp(_activeScene.DialogCharactersPerSecond, 5, 40), 1.5, 12);
            _dialogTimer.Interval = TimeSpan.FromSeconds(seconds);
            _dialogTimer.Start();
        });
    }

    private void AdvanceDialog()
    {
        _voicePlayer.Stop();
        _voicePlayer.Close();
        _dialogIndex++;
        ShowDialogLine();
    }

    private string FormatDialogText(string text)
    {
        var nickname = _manager.CurrentCompanion?.UserNickname?.Trim();
        return string.IsNullOrWhiteSpace(nickname)
            ? text
            : text.Replace("{{user}}", nickname, StringComparison.OrdinalIgnoreCase);
    }

    private void DialogFinished()
    {
        StopDialog();
        if (TryPresentPendingReminder())
            return;
        if (_activeScene is null
            || !ScenePlaybackPolicy.ShouldFinishLoopWhenDialogEnds(_sceneRole, _activeScene.PlaybackMode))
        {
            return;
        }

        Action completed = _sceneRole == AnimationSceneRole.Exit
            ? CompleteCloseForExit
            : FinishIdleTransient;
        if (FinishCurrentLoopThen(completed))
            return;

        _animationPlayer.Stop(clearFrame: false);
        completed();
    }

    private void AnimationCycleCompleted(object? sender, EventArgs e)
    {
        if (_afterLoopFinishes is { } afterLoop && !_animationPlayer.IsLooping)
        {
            _afterLoopFinishes = null;
            afterLoop();
            return;
        }

        if (_animationPlayer.IsLooping)
            return;

        if (_sceneRole == AnimationSceneRole.Start)
        {
            StopDialog();
            if (_manager.CurrentCompanion is { } companion)
                StartDefaultForCurrentState(companion);
            return;
        }

        if (_sceneRole is AnimationSceneRole.IdleRandom
            or AnimationSceneRole.SelfBirthday
            or AnimationSceneRole.UserBirthday)
        {
            FinishIdleTransient();
            return;
        }

        if (_sceneRole == AnimationSceneRole.Exit)
            CompleteCloseForExit();
    }

    private bool FinishCurrentLoopThen(Action continuation)
    {
        if (_afterLoopFinishes is not null)
        {
            _afterLoopFinishes = continuation;
            return true;
        }

        if (!_animationPlayer.FinishLoop())
            return false;

        _afterLoopFinishes = continuation;
        _idleTimer.Stop();
        StopDialog();
        return true;
    }

    private void FinishIdleTransient()
    {
        StopDialog();
        if (_manager.ActivityState != CompanionActivityState.Idle
            || _manager.CurrentCompanion is not { } companion)
            return;

        StartIdleDefault(companion);
        ScheduleRandomIdle();
    }

    private void HideDialogBubble_Click(object sender, RoutedEventArgs e)
    {
        var previouslyDismissed = _inactiveDialogButtonDismissed;
        _inactiveDialogButtonDismissed = false;
        if (!TrySetDialogBubbleHidden(true))
        {
            _inactiveDialogButtonDismissed = previouslyDismissed;
            UpdateDialogBubbleVisibility();
            return;
        }
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
    }

    private void ShowDialogBubble_Click(object sender, RoutedEventArgs e)
    {
        var previouslyDismissed = _inactiveDialogButtonDismissed;
        _inactiveDialogButtonDismissed = true;
        if (_manager.Data.Settings.DialogBubbleHidden)
        {
            if (!TrySetDialogBubbleHidden(false))
            {
                _inactiveDialogButtonDismissed = previouslyDismissed;
                UpdateDialogBubbleVisibility();
            }
        }
        else
            UpdateDialogBubbleVisibility();
    }

    private bool TrySetDialogBubbleHidden(bool hidden)
    {
        try
        {
            _manager.SetDialogBubbleHidden(hidden);
            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The chat-bubble visibility preference could not be saved.", exception);
            MessageBox.Show(
                LocalizationService.Get("Overlay.DialogVisibilityFailed"),
                LocalizationService.Get("Overlay.DialogVisibilityFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private void UpdateDialogBubbleVisibility()
    {
        var presentation = DialogIndicatorPolicy.Resolve(
            HasActiveDialog,
            HasDialogContent,
            _manager.Data.Settings.DialogBubbleHidden,
            _inactiveDialogButtonDismissed);
        ApplyRestoreIndicatorTone(presentation.RestoreTone);
        BubbleRoot.Visibility = presentation.ShowBubble
            ? Visibility.Visible
            : Visibility.Collapsed;
        HideDialogButton.Visibility = presentation.ShowHideButton
            ? Visibility.Visible
            : Visibility.Collapsed;
        ShowDialogButton.Visibility = presentation.ShowRestoreButton
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyRestoreIndicatorTone(DialogRestoreIndicatorTone tone) =>
        ShowDialogButton.Style = (Style)FindResource(tone == DialogRestoreIndicatorTone.Active
            ? "OverlayBubbleRestoreButton"
            : "OverlayBubbleInactiveRestoreButton");

    private bool HasSceneDialog => _activeScene is not null && _dialogIndex < _activeScene.DialogLines.Count;
    private bool HasActiveDialog => _activeReminder is not null || HasSceneDialog;
    private bool HasDialogContent => !string.IsNullOrWhiteSpace(DialogText.Text);

    private void StopDialog()
    {
        _pendingDialogStartFrame = null;
        _dialogTimer.Stop();
        _voicePlayer.Stop();
        _voicePlayer.Close();
        if (_activeReminder is null)
        {
            BubbleRoot.Visibility = Visibility.Collapsed;
            SetDialogAreaHeight(ReservedDialogHeight, preserveCharacterPosition: IsVisible);
        }
        if (_activeReminder is null)
        {
            ApplyRestoreIndicatorTone(DialogRestoreIndicatorTone.Inactive);
            HideDialogButton.Visibility = Visibility.Collapsed;
            ShowDialogButton.Visibility = _manager.Data.Settings.DialogBubbleHidden
                && !_inactiveDialogButtonDismissed
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            DialogText.Text = string.Empty;
        }
        else
        {
            UpdateDialogBubbleVisibility();
            UpdateDialogAreaForContent();
        }
    }

    private void StopAll(bool clearFrame)
    {
        _afterLoopFinishes = null;
        _idleTimer.Stop();
        StopDialog();
        _animationPlayer.Stop(clearFrame);
        _activeScene = null;
        _activeScenePlaybackFailed = false;
        _sceneRole = AnimationSceneRole.None;
    }

    private void SetFrame(BitmapSource? frame)
    {
        CompanionImage.Source = frame;
        CompanionImage.Visibility = frame is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowMissingAsset(string message)
    {
        _animationPlayer.Stop(clearFrame: true);
        _sceneRole = AnimationSceneRole.None;
        CompanionImage.Visibility = Visibility.Collapsed;
        MissingAssetText.Text = message;
        MissingAssetCard.Visibility = Visibility.Visible;
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush TryBrush(string value, Brush fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"The saved color '{value}' could not be parsed.", exception);
            return fallback;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || BubbleRoot.IsMouseOver
            || HideDialogButton.IsMouseOver || ShowDialogButton.IsMouseOver)
            return;
        _dragged = true;
        try { DragMove(); }
        catch (InvalidOperationException exception)
        {
            AppLogger.Warning("The companion overlay drag operation was interrupted.", exception);
        }
        finally
        {
            if (_dragged)
            {
                _dragged = false;
                _manager.SavePosition(Left, Top + _dialogAreaHeight);
            }
        }
    }

    private void RemindLaterButton_Click(object sender, RoutedEventArgs e) =>
        App.CurrentApp.SnoozeCalendarReminder();

    private void AcknowledgeReminderButton_Click(object sender, RoutedEventArgs e) =>
        App.CurrentApp.AcknowledgeCalendarReminder();

    private static string BuildReminderText(CalendarEvent calendarEvent, DateTime occurrenceStart)
    {
        var urgency = calendarEvent.IsUrgent
            ? LocalizationService.Get("Calendar.Reminder.UrgentPrefix") + " "
            : string.Empty;
        var timing = calendarEvent.IsAllDay
            ? LocalizationService.Get("Calendar.Reminder.AllDay")
            : LocalizationService.Format("Calendar.Reminder.StartsAt", occurrenceStart.ToString("HH:mm"));
        return LocalizationService.Format("Calendar.Reminder.Message", urgency + calendarEvent.Title, timing);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragged)
            return;
        _dragged = false;
        _manager.SavePosition(Left, Top + _dialogAreaHeight);
    }

    public void CloseForExit(Action? completed = null)
    {
        if (_closingForExit)
            return;

        _closingForExit = true;
        _exitCompleted = completed;
        _pendingReminder = null;
        _activeReminder = null;
        ReminderActions.Visibility = Visibility.Collapsed;
        if (IsVisible && _manager.CurrentCompanion?.ExitAnimation is { } exitScene)
        {
            StartScene(exitScene, AnimationSceneRole.Exit, forceLoop: false);
            return;
        }

        CompleteCloseForExit();
    }

    private void CompleteCloseForExit()
    {
        if (_exitCloseCompleted)
            return;
        _exitCloseCompleted = true;
        var completed = _exitCompleted;
        _exitCompleted = null;
        _manager.CurrentCompanionChanged -= ManagerCurrentCompanionChanged;
        _manager.ActivityStateChanged -= ManagerStateChanged;
        _manager.ViewportChanged -= ManagerViewportChanged;
        _manager.DialogPresentationChanged -= ManagerDialogPresentationChanged;
        _manager.IdleIntervalChanged -= ManagerIdleIntervalChanged;
        _manager.UserBirthdayChanged -= ManagerUserBirthdayChanged;
        StopAll(clearFrame: true);
        _voicePlayer.Close();
        _animationPlayer.Dispose();
        Close();
        completed?.Invoke();
    }

    private sealed record PendingCalendarReminder(CalendarEvent CalendarEvent, DateTime OccurrenceStart);
}
