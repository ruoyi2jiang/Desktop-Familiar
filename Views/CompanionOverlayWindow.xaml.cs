using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopFamiliar.Localization;
using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Dialogs;
using DesktopFamiliar.Services.Focus;
using DesktopFamiliar.Services.Voice;
using DesktopFamiliar.Views.Controls;
using Brush = System.Windows.Media.Brush;
using RectangleF = System.Drawing.RectangleF;
using static DesktopFamiliar.Views.Controls.CompanionBubbleAppearance;

namespace DesktopFamiliar.Views;

public partial class CompanionOverlayWindow : Window
{
    private const double ReservedDialogHeight = 96;
    private const double DragStartThreshold = 4;
    private static readonly TimeSpan DragHoldDelay = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan AddonStatusDuration = TimeSpan.FromSeconds(3);

    private readonly CompanionManager _manager;
    private readonly IAddonViewportActionRegistry _addonViewportActions;
    private readonly FrameAnimationPlayer _animationPlayer;
    private CompanionDefinition? _playbackCompanion;
    private readonly DispatcherTimer _idleTimer = new();
    private readonly DeferredMenuAction _menuAction = new();
    private readonly DispatcherTimer _dialogTimer = new();
    private readonly DispatcherTimer _autoClickStatusTimer = new();
    private readonly DispatcherTimer _dragHoldTimer = new();
    private MediaPlayer? _voicePlayer;
    private readonly VoicePlayback _sceneSpeech;
    private readonly VoicePlayback _chatSpeech;
    private readonly Random _random = new();
    private AnimationScene? _activeScene;
    private AnimationSceneRole _sceneRole;
    private int _dialogIndex;
    private bool _closingForExit;
    private bool _dragged;
    private bool _dragInteractionPlaybackStarted;
    private bool _pointerDown;
    private Point _pointerDownScreen;
    private Point _lastPointerScreen;
    private readonly SpriteCrossfade _sprites;
    private bool _playStartOnNextShow = true;
    private Action? _afterLoopFinishes;
    private int? _pendingDialogStartFrame;
    private readonly DispatcherTimer _modelDialogDelay = new();
    private readonly System.Diagnostics.Stopwatch _modelDialogClock = new();
    private Queue<(int Index, double Seconds)> _modelCues = new();
    private bool _modelSpeechPending;
    private bool _modelClipEnded;
    private Guid? _idleUnderGreeting;
    private bool IsPlaybackLooping => _playbackCompanion?.Appearance == CompanionAppearance.Model3D
        ? PetModel.IsLooping : _animationPlayer.IsLooping;
    private bool _showingAddonStatus;
    private string? _queuedAddonStatusMessage;
    private string _interruptedDialogText = string.Empty;
    private Visibility _interruptedReminderActionsVisibility;
    // The restore indicator is armed only by the user's hide action.
    private bool _inactiveDialogButtonDismissed = true;
    private double _dialogAreaHeight = ReservedDialogHeight;
    private Point _characterPosition;
    private RectangleF _drawingExtent;
    private bool _reserveDesktopForModel;
    private OverlayDisplayObserver? _displayObserver;
    private CalendarEvent? _activeReminder;
    private PendingCalendarReminder? _pendingReminder;
    private bool _activeScenePlaybackFailed;
    private Action? _exitCompleted;
    private bool _exitCloseCompleted;

    public CompanionOverlayWindow(CompanionManager manager, IAddonViewportActionRegistry addonViewportActions)
        : this(manager, addonViewportActions, (text, voice, token) => App.CurrentApp.Voice.SpeakAsync(text, voice, token)) { }

    internal CompanionOverlayWindow(CompanionManager manager, IAddonViewportActionRegistry addonViewportActions,
        Func<string, CompanionVoice, CancellationToken, Task> speak)
    {
        _sceneSpeech = new(speak, Dispatcher.Invoke);
        _chatSpeech = new(speak, Dispatcher.Invoke);
        InitializeComponent();
        _manager = manager;
        _addonViewportActions = addonViewportActions ?? throw new ArgumentNullException(nameof(addonViewportActions));
        ApplyViewportSize();
        ApplySavedPosition();
        _sprites = new SpriteCrossfade(CompanionImage, TransitionImage);
        _animationPlayer = new FrameAnimationPlayer(_sprites.Present);
        _animationPlayer.CycleCompleted += AnimationCycleCompleted;
        PetModel.Completed += AnimationCycleCompleted;
        _modelDialogDelay.Tick += (_, _) => PresentDueModelCue();
        _animationPlayer.FramePresented += AnimationFramePresented;
        _idleTimer.Tick += IdleTimerTick;
        _dialogTimer.Tick += DialogTimerTick;
        _autoClickStatusTimer.Tick += AddonStatusTimerTick;
        _dragHoldTimer.Tick += DragHoldTimerTick;
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
        LocationChanged += (_, _) => QueuePromptLayout();
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _displayObserver = new OverlayDisplayObserver(source, RefreshDisplayLayout);
            _displayObserver.QueueRefresh();
        };
        DpiChanged += (_, _) => _displayObserver?.QueueRefresh();
        Closed += (_, _) => _displayObserver?.Dispose();
        _chatClock.Tick += (_, _) => RenderChat();
        // Scene changes can hide this bubble synchronously while the next scene is starting.
        BubbleRoot.IsVisibleChanged += (_, _) => { if (_chatClock.IsEnabled) RenderChat(updateAnimation: false); };
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
        _menuAction.InvalidateDispatchedAction();
        if (ContextMenu is { } previousMenu)
            previousMenu.Closed -= OverlayContextMenu_Closed;

        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource("Windows11ContextMenu")
        };
        menu.Closed += OverlayContextMenu_Closed;

        var enterFocus = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("FocusMode.Enter"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        enterFocus.Click += (_, _) => _manager.SetFocusMode(true);
        menu.Items.Add(enterFocus);

        if (App.CurrentApp.Chat is { Available: true } chat)
            menu.Items.Add(ChatMenuItem("Chat.Title", chat.OpenPrompt));

        var addonActions = _addonViewportActions.GetVisibleActions();
        var actions = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Overlay.Actions"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        foreach (var action in addonActions)
        {
            var actionItem = new System.Windows.Controls.MenuItem
            {
                Header = action.Header,
                Style = (Style)FindResource("Windows11MenuItem")
            };
            actionItem.Click += (_, _) => _menuAction.Queue(action.Execute);
            actions.Items.Add(actionItem);
        }

        if (_manager.ActivityState == CompanionActivityState.Idle)
        {
            var randomIdle = new System.Windows.Controls.MenuItem
            {
                Header = LocalizationService.Get("Overlay.PlayRandomIdle"),
                IsEnabled = !ChatInProgress && _playbackCompanion is { } companion
                    && ScenePlaybackPolicy.GetRandomIdleCandidates(companion).Count > 0,
                Style = (Style)FindResource("Windows11MenuItem")
            };
            randomIdle.Click += (_, _) => PlayRandomIdleNow();
            actions.Items.Add(randomIdle);
        }
        if (actions.Items.Count > 0) menu.Items.Add(actions);
        var configure = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Overlay.ConfigureCompanions"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        configure.Click += (_, _) => _menuAction.Queue(App.CurrentApp.ShowLibrary);
        var setEvent = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Calendar.Menu.SetEvent"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        setEvent.Click += (_, _) => _menuAction.Queue(App.CurrentApp.ShowCalendarEvents);
        var settings = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Tray.Settings"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        settings.Click += (_, _) => _menuAction.Queue(App.CurrentApp.ShowSettings);
        var exit = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Get("Overlay.Exit"),
            Style = (Style)FindResource("Windows11MenuItem")
        };
        exit.Click += (_, _) => App.CurrentApp.ShutdownApplication();
        menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("Windows11MenuSeparator") });
        menu.Items.Add(setEvent);
        menu.Items.Add(configure);
        menu.Items.Add(ChatMenuItem("Chat.History", App.CurrentApp.ShowChatHistory));
        menu.Items.Add(settings);
        menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("Windows11MenuSeparator") });
        menu.Items.Add(exit);
        ContextMenu = menu;
    }

    private MenuItem ChatMenuItem(string label, Action open)
    {
        var item = new MenuItem { Header = LocalizationService.Get(label), Style = (Style)FindResource("Windows11MenuItem") };
        item.Click += (_, _) => _menuAction.Queue(open);
        return item;
    }

    private void OverlayContextMenu_Closed(object? sender, RoutedEventArgs e) =>
        _menuAction.DispatchPending(action =>
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, action));

    public void ShowCompanion()
    {
        if (IsVisible)
            return;

        _drawingExtent = RectangleF.Empty;
        _playStartOnNextShow = true;
        // Replace the retained image while the transparent window is still
        // hidden so WPF can never paint a frame from the previous state.
        RefreshCompanion();
        Show();
    }

    public void HideCompanion()
    {
        StopAll();
        Hide();
    }

    /// <summary>Temporarily presents an addon status message without changing the active animation.</summary>
    public void ShowAddonStatus(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsVisible)
            return;

        if (_showingAddonStatus)
        {
            _queuedAddonStatusMessage = message;
            return;
        }

        if (_sceneRole == AnimationSceneRole.ChatReply && _playbackCompanion is { } companion)
            StartDefaultForCurrentState(companion);
        _showingAddonStatus = true;
        _interruptedDialogText = DialogText.Text;
        _interruptedReminderActionsVisibility = ReminderActions.Visibility;
        _dialogTimer.Stop();
        StopVoice();
        ReminderActions.Visibility = Visibility.Collapsed;

        DialogText.Text = message;
        _autoClickStatusTimer.Stop();
        _autoClickStatusTimer.Interval = AddonStatusDuration;
        _autoClickStatusTimer.Start();
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
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
        StopVoice();
        ReminderActions.Visibility = Visibility.Visible;
        RemindLaterButton.Content = LocalizationService.Format(
            "Calendar.Reminder.RemindLater", Math.Clamp(pending.CalendarEvent.SnoozeMinutes, 1, 1440));
        AcknowledgeReminderButton.Content = LocalizationService.Get("Calendar.Reminder.Acknowledge");
        DialogText.Text = BuildReminderText(pending.CalendarEvent, pending.OccurrenceStart);
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
        if (ChatInProgress || _manager.ActivityState != CompanionActivityState.Idle
            || _playbackCompanion is not { } companion)
            return;
        StartRandomIdle(companion);
    }

    private void ApplySavedPosition()
    {
        var settings = _manager.Data.Settings;
        var offset = OverlayLayoutPolicy.GetCharacterHorizontalOffset(settings.ViewportWidth);
        var left = settings.CompanionLeft ?? SystemParameters.WorkArea.Right - settings.ViewportWidth - offset - 24;
        var top = settings.CompanionTop ?? SystemParameters.WorkArea.Bottom - settings.ViewportHeight - 24;
        PlaceCharacter(left, top);
    }

    private void ApplyViewportSize()
    {
        _drawingExtent = RectangleF.Empty;
        var characterWidth = _manager.Data.Settings.ViewportWidth;
        OverlayContent.Width = OverlayLayoutPolicy.GetWindowWidth(characterWidth);
        CharacterRow.Height = new GridLength(_manager.Data.Settings.ViewportHeight);
        CompanionViewport.Width = characterWidth;
        var dialogWidth = OverlayLayoutPolicy.GetDialogWidth(characterWidth);
        ChatReplyBorder.Width = dialogWidth;
        BubbleRoot.Width = dialogWidth;
        BubbleRoot.MaxWidth = AppSettings.MaximumDialogBubbleWidth;
        DialogControlRoot.Width = dialogWidth;
        SetDialogAreaHeight(0, preserveCharacterPosition: false);
    }

    private static RectangleF VirtualDesktop => new((float)SystemParameters.VirtualScreenLeft, (float)SystemParameters.VirtualScreenTop,
        (float)SystemParameters.VirtualScreenWidth, (float)SystemParameters.VirtualScreenHeight);

    private void PlaceCharacter(double left, double top)
    {
        var desktop = VirtualDesktop;
        var settings = _manager.Data.Settings;
        var minX = desktop.Left + OverlayLayoutPolicy.GetCharacterHorizontalOffset(settings.ViewportWidth);
        var minY = desktop.Top + _dialogAreaHeight;
        _characterPosition = new Point(
            Math.Clamp(left, minX, Math.Max(minX, minX + desktop.Width - OverlayContent.Width)),
            Math.Clamp(top, minY, Math.Max(minY, desktop.Bottom - settings.ViewportHeight)));
        ApplyDrawingWindow();
    }

    private void ReserveModelExtent(ModelAppearance model)
    {
        var settings = _manager.Data.Settings;
        var area = PetModel.GetConfiguredMotionBounds(model, new Size(settings.ViewportWidth, settings.ViewportHeight));
        _reserveDesktopForModel = area == null;
        if (area is { IsEmpty: false } rect)
        {
            // A character can be moved anywhere on this desktop. Retain every reachable offset,
            // without converting enormous authored coordinates into native window dimensions.
            var desktop = VirtualDesktop;
            rect.Intersect(new Rect(-desktop.Width, -desktop.Height, 2 * desktop.Width, 2 * desktop.Height));
            if (!rect.IsEmpty)
                _drawingExtent = OverlayLayoutPolicy.ExpandModelExtent(_drawingExtent,
                    new RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
        }
    }

    private void RefreshDisplayLayout()
    {
        if (_closingForExit) return;
        if (PetModel.Visibility == Visibility.Visible && _playbackCompanion is { } pet)
            ReserveModelExtent(pet.Model3D);
        PlaceCharacter(_characterPosition.X, _characterPosition.Y);
        if (_chatPrompt != null) UpdateDialogAreaForContent();
    }

    private void ApplyDrawingWindow()
    {
        var settings = _manager.Data.Settings;
        var character = new RectangleF((float)_characterPosition.X, (float)_characterPosition.Y,
            settings.ViewportWidth, settings.ViewportHeight);
        _drawingExtent = OverlayLayoutPolicy.ReserveDrawingExtent(_drawingExtent, settings.ViewportWidth,
            settings.ViewportHeight, (float)_dialogAreaHeight, _manager.CurrentCompanion?.Appearance == CompanionAppearance.Model3D);
        var desktop = VirtualDesktop;
        var bounds = OverlayLayoutPolicy.GetDrawingWindow(character, (float)_dialogAreaHeight, _drawingExtent, desktop);
        if (_reserveDesktopForModel) bounds = RectangleF.Union(bounds, desktop);
        var x = character.X - bounds.X;
        var y = character.Y - bounds.Y;
        Left = bounds.X;
        Top = bounds.Y;
        Width = PetModel.Width = bounds.Width;
        Height = PetModel.Height = bounds.Height;
        Canvas.SetLeft(OverlayContent, x - OverlayLayoutPolicy.GetCharacterHorizontalOffset(settings.ViewportWidth));
        Canvas.SetTop(OverlayContent, y - _dialogAreaHeight);
        PetModel.SetCharacterFrame(new Rect(x, y, character.Width, character.Height));
    }

    private void ManagerStateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        if (!IsVisible)
            return;

        if (_sceneRole == AnimationSceneRole.ChatReply && _playbackCompanion is { Appearance: CompanionAppearance.Model3D } talkingPet)
        {
            StartDefaultForCurrentState(talkingPet);
            return;
        }

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
        _drawingExtent = RectangleF.Empty;
        _inactiveDialogButtonDismissed = true;
        if (IsVisible)
            RefreshCompanion();
    });

    private void ManagerViewportChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        ApplyViewportSize();
        if (PetModel.Visibility == Visibility.Visible && _playbackCompanion is { } pet) ReserveModelExtent(pet.Model3D);
        ApplySavedPosition();
        UpdateDialogAreaForContent();
    });

    private void ManagerDialogPresentationChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
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

    private void UpdateDialogAreaForContent()
    {
        double desiredHeight = 0;
        if (BubbleRoot.Visibility == Visibility.Visible)
        {
            // Measure the bubble directly. Measuring DialogArea reuses the
            // current fixed row constraint and can clip newly added reminder
            // actions, particularly when a snoozed reminder is shown again.
            BubbleRoot.Measure(new Size(BubbleRoot.Width, double.PositiveInfinity));
            desiredHeight = BubbleRoot.DesiredSize.Height;
        }
        if (ChatReplyBorder.Visibility == Visibility.Visible)
        {
            ChatReplyBorder.Measure(new Size(ChatReplyBorder.Width, double.PositiveInfinity));
            desiredHeight = Math.Max(desiredHeight, ChatReplyBorder.DesiredSize.Height);
        }
        SetDialogAreaHeight(desiredHeight, preserveCharacterPosition: IsVisible);
    }

    private void SetDialogAreaHeight(double contentHeight, bool preserveCharacterPosition)
    {
        var dialogueHeight = Math.Max(ReservedDialogHeight, Math.Ceiling(contentHeight));
        var characterTop = _characterPosition.Y;
        var characterHeight = _manager.Data.Settings.ViewportHeight;
        if (preserveCharacterPosition)
        {
            var screenTop = SystemParameters.VirtualScreenTop;
            var lastTop = screenTop + SystemParameters.VirtualScreenHeight - characterHeight - dialogueHeight;
            characterTop = Math.Clamp(characterTop - dialogueHeight, screenTop, Math.Max(screenTop, lastTop)) + dialogueHeight;
        }

        var clearance = ArrangeChatPrompt(characterTop, dialogueHeight);
        DialogArea.Margin = new Thickness(0, 0, 0, clearance);
        _dialogAreaHeight = Math.Max(ReservedDialogHeight, Math.Ceiling(contentHeight + clearance));
        DialogRow.Height = new GridLength(_dialogAreaHeight);
        OverlayContent.Height = _dialogAreaHeight + characterHeight;
        _characterPosition.Y = characterTop;
        ApplyDrawingWindow();
    }

    private void RefreshCompanion()
    {
        _reserveDesktopForModel = false;
        StopAll();
        var source = _manager.CurrentCompanion;
        if (source is null)
        {
            _playbackCompanion = null;
            return;
        }

        var companion = _playbackCompanion = source.Clone();
        ApplyAppearance(source);
        if (_playStartOnNextShow
            && _manager.ActivityState == CompanionActivityState.Idle
            && companion.ActiveScenes.StartAnimation is { } startScene)
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
        CompanionBubbleAppearance.Apply(ChatReplyBorder, companion);
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



    private void StartDefaultForCurrentState(CompanionDefinition companion, bool transition = false)
    {
        if (_manager.ActivityState == CompanionActivityState.Reminder)
        {
            StartReminderState(companion, transition);
            return;
        }

        StartIdleDefault(companion, transition);
        ScheduleRandomIdle();
    }

    private void StartReminderState(CompanionDefinition companion, bool transition = false)
    {
        if (companion.ActiveScenes.ReminderAnimation is { } reminderScene)
        {
            StartScene(reminderScene, AnimationSceneRole.Reminder, forceLoop: true, transition: transition);
            return;
        }

        if (companion.ActiveScenes.IdleDefaultAnimation is { } idleScene)
        {
            StartScene(idleScene, AnimationSceneRole.ReminderFallback, forceLoop: true, transition: transition);
            return;
        }

        ShowMissingAsset(LocalizationService.Get("Overlay.IdleDefaultNotAssigned"));
        TryPresentPendingReminder();
    }

    private void StartIdleDefault(CompanionDefinition companion, bool transition = false)
    {
        var scene = companion.ActiveScenes.IdleDefaultAnimation;
        if (scene is null)
        {
            ShowMissingAsset(LocalizationService.Get("Overlay.IdleDefaultNotAssigned"));
            return;
        }
        StartScene(scene, AnimationSceneRole.IdleDefault, forceLoop: true, transition: transition);
    }

    private void ScheduleRandomIdle()
    {
        _idleTimer.Stop();
        if (_sceneRole == AnimationSceneRole.ChatReply || _manager.ActivityState != CompanionActivityState.Idle
            || _playbackCompanion is not { } companion
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
        var companion = _playbackCompanion;
        if (companion is null || _manager.ActivityState != CompanionActivityState.Idle)
            return;

        if (ChatInProgress)
        {
            ScheduleRandomIdle();
            return;
        }
        if (!FinishCurrentLoopThen(() =>
            {
                if (_manager.ActivityState == CompanionActivityState.Idle
                    && _playbackCompanion is { } current)
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
        // The previous loop can finish after a chat turn was accepted.
        if (ChatInProgress)
        {
            ScheduleRandomIdle();
            return;
        }
        var selection = IdleSceneSelectionPolicy.Select(
            companion,
            _manager.Data.Settings.UserBirthday,
            DateTime.Today,
            _random.NextDouble());
        if (selection is null)
        {
            if (_sceneRole != AnimationSceneRole.IdleDefault || !IsPlaybackLooping)
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
            if (_sceneRole != AnimationSceneRole.IdleDefault || !IsPlaybackLooping)
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
        bool forceOneShot = false,
        bool transition = false)
    {
        _sprites.BeginScene(transition);
        var keepIdleMotion = role == AnimationSceneRole.IdleDefault && _idleUnderGreeting == scene.Id;
        _idleUnderGreeting = null;
        _afterLoopFinishes = null;
        StopDialog();
        _activeScenePlaybackFailed = false;
        var blendSeconds = ScenePlaybackPolicy.ModelBlendSeconds(_sceneRole, role, ChatInProgress);
        _activeScene = scene;
        _sceneRole = role;
        MissingAssetCard.Visibility = Visibility.Collapsed;
        CompanionImage.Visibility = Visibility.Visible;
        var hasDialog = ScenePlaybackPolicy.ShouldPresentSceneDialog(
                _manager.Data.Settings.FocusModeEnabled,
                role, _playbackCompanion?.Appearance ?? CompanionAppearance.Images2D, ChatInProgress)
            && scene.DialogLines.Any(line =>
                !string.IsNullOrWhiteSpace(line.Text)
                || !string.IsNullOrWhiteSpace(line.VoicePath));
        var loop = forceLoop || (!forceOneShot && scene.PlaybackMode == ScenePlaybackMode.LoopUntilDialogEnds && hasDialog);
        _dialogIndex = hasDialog ? 0 : scene.DialogLines.Count;
        _modelSpeechPending = hasDialog && _playbackCompanion?.Appearance == CompanionAppearance.Model3D;
        _pendingDialogStartFrame = hasDialog && !_modelSpeechPending
            ? AnimationFrameSettingsPolicy.GetRuntimeDialogStartFrame(scene)
            : null;
        try
        {
            if (_playbackCompanion is { Appearance: CompanionAppearance.Model3D } modelPet)
            {
                CompanionImage.Visibility = Visibility.Collapsed;
                PetModel.Visibility = Visibility.Visible;
                if (scene.ModelLoop is { } settings)
                    loop = forceLoop || (settings.Mode == ModelLoopMode.UntilDialogueEnds && hasDialog);
                if (!keepIdleMotion)
                    PetModel.PlayScene(modelPet.Model3D.FilePath, scene, loop, blendSeconds);
                ReserveModelExtent(modelPet.Model3D);
                ApplyDrawingWindow();
                if (role == AnimationSceneRole.Start && modelPet.ActiveScenes.IdleDefaultAnimation?.FirstModelClip is { } idleClip)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (_activeScene == scene && _sceneRole == AnimationSceneRole.Start)
                            PetModel.PrepareClip(idleClip);
                    }));
                if (_modelSpeechPending)
                {
                    _modelCues = new(ModelDialogueTiming.Cues(scene));
                    _modelDialogClock.Restart();
                    PresentDueModelCue();
                }
            }
            else
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
            _modelDialogDelay.Stop();
            _modelSpeechPending = false;
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

    private bool PresentDueModelCue()
    {
        _modelDialogDelay.Stop();
        if (!_modelSpeechPending || _showingAddonStatus) return false;
        var elapsed = _modelDialogClock.Elapsed.TotalSeconds;
        int? due = null;
        while (_modelCues.TryPeek(out var cue) && cue.Seconds <= elapsed)
            due = _modelCues.Dequeue().Index;
        if (_modelCues.TryPeek(out var next))
        {
            _modelDialogDelay.Interval = TimeSpan.FromSeconds(Math.Max(0.001, next.Seconds - elapsed));
            _modelDialogDelay.Start();
        }
        if (due is not int index) return false;
        _dialogIndex = index;
        ShowDialogLine();
        return true;
    }

    private void ShowDialogLine()
    {
        if (_showingAddonStatus)
            return;

        if (_activeScene is null || _dialogIndex >= _activeScene.DialogLines.Count)
        {
            DialogFinished();
            return;
        }

        if (_modelSpeechPending && _modelCues.TryPeek(out var next) && _dialogIndex == next.Index)
        {
            DialogText.Text = string.Empty;
            UpdateDialogBubbleVisibility();
            UpdateDialogAreaForContent();
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
        StopVoice();

        if (File.Exists(line.VoicePath))
        {
            try
            {
                _chatSpeech.Stop();
                _voicePlayer = new MediaPlayer { IsMuted = _voiceInputActive };
                _voicePlayer.MediaEnded += VoicePlayerEnded;
                _voicePlayer.MediaFailed += VoicePlayerFailed;
                _voicePlayer.Open(new Uri(line.VoicePath, UriKind.Absolute));
                _voicePlayer.Play();
                return;
            }
            catch (Exception exception)
            {
                AppLogger.Warning("A voice line could not be opened; text timing will be used.", exception);
                StopVoice();
            }
        }
        else if (_playbackCompanion?.Voice is { } voice && ScenePlaybackPolicy.ShouldUseProfileVoice(line, voice, _voiceInputActive))
        {
            SpeakSceneLine(displayedText, voice);
            return;
        }

        StartLineReadingTimer();
    }

    private async void SpeakSceneLine(string text, CompanionVoice voice)
    {
        _chatSpeech.Stop();
        await _sceneSpeech.PlayAsync(text, voice, played =>
        {
            if (played) AdvanceDialog();
            else StartLineReadingTimer();
        });
    }

    private void StartLineReadingTimer()
    {
        if (_activeScene == null || !HasSceneDialog) return;
        var seconds = ModelDialogueTiming.ReadingSeconds(DialogText.Text, _activeScene.DialogCharactersPerSecond);
        _dialogTimer.Interval = TimeSpan.FromSeconds(seconds);
        _dialogTimer.Start();
    }

    private void DialogTimerTick(object? sender, EventArgs e)
    {
        _dialogTimer.Stop();
        AdvanceDialog();
    }

    private void StopVoice()
    {
        _sceneSpeech.Stop();
        if (_voicePlayer == null) return;
        _voicePlayer.MediaEnded -= VoicePlayerEnded;
        _voicePlayer.MediaFailed -= VoicePlayerFailed;
        _voicePlayer.Close();
        _voicePlayer = null;
    }

    private void VoicePlayerEnded(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        if (sender == _voicePlayer) AdvanceDialog();
    });

    private void VoicePlayerFailed(object? sender, ExceptionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_activeScene is null || !HasSceneDialog || sender != _voicePlayer)
                return;
            StopVoice();
            AppLogger.Warning("A recorded dialogue line failed during playback.", e.ErrorException);
            StartLineReadingTimer();
        });
    }

    private void AdvanceDialog()
    {
        if (_showingAddonStatus)
            return;

        StopVoice();
        _dialogIndex++;
        ShowDialogLine();
    }

    private string FormatDialogText(string text)
    {
        var nickname = _playbackCompanion?.UserNickname?.Trim();
        return string.IsNullOrWhiteSpace(nickname)
            ? text
            : text.Replace("{{user}}", nickname, StringComparison.OrdinalIgnoreCase);
    }

    private void DialogFinished()
    {
        var resumeAfterSpeech = _modelClipEnded;
        StopDialog();
        if (resumeAfterSpeech)
        {
            if (_idleUnderGreeting.HasValue && _playbackCompanion is { } pet)
            {
                StartDefaultForCurrentState(pet);
                return;
            }
            AnimationCycleCompleted(PetModel, EventArgs.Empty);
            return;
        }
        if (_playbackCompanion?.Appearance == CompanionAppearance.Model3D && _activeScene?.ModelLoop is { } modelLoop)
        {
            if (ScenePlaybackPolicy.ShouldReleaseModelLoopAfterDialogue(_sceneRole, modelLoop.Mode)
                && PetModel.IsLooping && !PetModel.FinishLoop())
            {
                // A rest pose has no cycle boundary to raise Completed.
                PetModel.Stop();
                AnimationCycleCompleted(PetModel, EventArgs.Empty);
            }
            TryPresentPendingReminder();
            return;
        }
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
        if (sender == PetModel && _modelSpeechPending)
        {
            _modelClipEnded = true;
            if (ScenePlaybackPolicy.BeginIdleUnderDialogue(_sceneRole, _manager.ActivityState)
                && _idleUnderGreeting == null && _playbackCompanion is { } pet && pet.ActiveScenes.IdleDefaultAnimation is { } idle)
            {
                try
                {
                    PetModel.PlayScene(pet.Model3D.FilePath, idle, loop: true, blendFromCurrent: true);
                    _idleUnderGreeting = idle.Id;
                }
                catch (Exception error) when (GlbModelFile.IsLoadError(error))
                {
                    AppLogger.Warning("Could not begin idle motion beneath the entrance greeting.", error);
                }
            }
            return;
        }
        if (_afterLoopFinishes is { } afterLoop && !IsPlaybackLooping)
        {
            _afterLoopFinishes = null;
            afterLoop();
            return;
        }

        if (IsPlaybackLooping)
            return;

        if (_sceneRole == AnimationSceneRole.Start)
        {
            StopDialog();
            if (_playbackCompanion is { } companion)
                StartDefaultForCurrentState(companion);
            return;
        }

        if (ScenePlaybackPolicy.IsInteractionRole(_sceneRole))
        {
            if (_playbackCompanion is { } companion)
                StartDefaultForCurrentState(companion, transition: true);
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

        var finishing = _playbackCompanion?.Appearance == CompanionAppearance.Model3D ? PetModel.FinishLoop() : _animationPlayer.FinishLoop();
        if (!finishing)
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
            || _playbackCompanion is not { } companion)
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
        if (_showingAddonStatus)
        {
            ApplyRestoreIndicatorTone(DialogRestoreIndicatorTone.Inactive);
            BubbleRoot.Visibility = Visibility.Visible;
            HideDialogButton.Visibility = Visibility.Collapsed;
            ShowDialogButton.Visibility = Visibility.Collapsed;
            return;
        }

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
        ClearAddonStatus();
        _modelDialogDelay.Stop();
        _modelDialogClock.Reset();
        _modelCues.Clear();
        _modelSpeechPending = false;
        _modelClipEnded = false;
        _pendingDialogStartFrame = null;
        _dialogTimer.Stop();
        StopVoice();
        if (_activeReminder is null)
        {
            BubbleRoot.Visibility = Visibility.Collapsed;
            SetDialogAreaHeight(0, preserveCharacterPosition: IsVisible);
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

    private void StopAll(bool releaseModel = false)
    {
        _idleUnderGreeting = null;
        _dragHoldTimer.Stop();
        _pointerDown = false;
        _dragged = false;
        _dragInteractionPlaybackStarted = false;
        Cursor = null;
        if (Mouse.Captured == this)
            Mouse.Capture(null);
        _sprites.Clear();
        _afterLoopFinishes = null;
        _idleTimer.Stop();
        StopDialog();
        _animationPlayer.Stop(clearFrame: true);
        PetModel.Visibility = Visibility.Collapsed;
        if (!releaseModel && _manager.CurrentCompanion is { Appearance: CompanionAppearance.Model3D } selected
            && _playbackCompanion?.Id == selected.Id && _playbackCompanion.Model3D.FilePath == selected.Model3D.FilePath)
            PetModel.Stop();
        else
            PetModel.Clear();
        _activeScene = null;
        _activeScenePlaybackFailed = false;
        _sceneRole = AnimationSceneRole.None;
    }

    private void AddonStatusTimerTick(object? sender, EventArgs e)
    {
        _autoClickStatusTimer.Stop();
        if (!_showingAddonStatus)
            return;

        if (_queuedAddonStatusMessage is { } queuedMessage)
        {
            _queuedAddonStatusMessage = null;
            DialogText.Text = queuedMessage;
            _autoClickStatusTimer.Interval = AddonStatusDuration;
            _autoClickStatusTimer.Start();
            UpdateDialogBubbleVisibility();
            UpdateDialogAreaForContent();
            return;
        }

        _showingAddonStatus = false;
        ReminderActions.Visibility = _interruptedReminderActionsVisibility;
        if (_activeReminder is not null)
        {
            DialogText.Text = _interruptedDialogText;
            UpdateDialogBubbleVisibility();
            UpdateDialogAreaForContent();
            return;
        }

        if (InterruptAmbientIdle()) return;
        if (_modelSpeechPending && PresentDueModelCue()) return;
        if (_pendingDialogStartFrame is null && HasSceneDialog)
        {
            ShowDialogLine();
            return;
        }

        DialogText.Text = _interruptedDialogText;
        UpdateDialogBubbleVisibility();
        UpdateDialogAreaForContent();
    }

    private void ClearAddonStatus()
    {
        _autoClickStatusTimer.Stop();
        if (_showingAddonStatus)
            ReminderActions.Visibility = _interruptedReminderActionsVisibility;
        _showingAddonStatus = false;
        _queuedAddonStatusMessage = null;
        _interruptedDialogText = string.Empty;
    }

    private void ShowMissingAsset(string message)
    {
        _sprites.Clear();
        _animationPlayer.Stop(clearFrame: true);
        PetModel.Visibility = Visibility.Collapsed;
        PetModel.Clear();
        _sceneRole = AnimationSceneRole.None;
        CompanionImage.Visibility = Visibility.Collapsed;
        MissingAssetText.Text = message;
        MissingAssetCard.Visibility = Visibility.Visible;
    }





    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || BubbleRoot.IsMouseOver
            || HideDialogButton.IsMouseOver || ShowDialogButton.IsMouseOver)
            return;
        _pointerDown = true;
        _dragged = false;
        _dragInteractionPlaybackStarted = false;
        _pointerDownScreen = PointToScreen(e.GetPosition(this));
        _lastPointerScreen = _pointerDownScreen;
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pointerScreen = PointToScreen(e.GetPosition(this));
        if (!_dragged)
        {
            var totalHorizontal = pointerScreen.X - _pointerDownScreen.X;
            var totalVertical = pointerScreen.Y - _pointerDownScreen.Y;
            if (Math.Abs(totalHorizontal) < DragStartThreshold
                && Math.Abs(totalVertical) < DragStartThreshold)
            {
                return;
            }

            _dragged = true;
            Cursor = Cursors.Hand;
        }

        var horizontalMovement = pointerScreen.X - _lastPointerScreen.X;
        var verticalMovement = pointerScreen.Y - _lastPointerScreen.Y;
        if (horizontalMovement != 0 || verticalMovement != 0)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            PlaceCharacter(_characterPosition.X + horizontalMovement / dpi.DpiScaleX,
                _characterPosition.Y + verticalMovement / dpi.DpiScaleY);
            QueuePromptLayout();
            if (horizontalMovement != 0)
            {
                var state = CompanionInteractionStatePolicy.GetDragState(horizontalMovement);
                if (_playbackCompanion is { } companion
                    && CompanionInteractionStatePolicy.GetAssignedDragScene(companion, state) is not null)
                {
                    StartDragInteraction(state);
                }
            }
            _lastPointerScreen = pointerScreen;
        }

        _dragHoldTimer.Stop();
        if (_playbackCompanion is { } holdCompanion
            && CompanionInteractionStatePolicy.GetAssignedDragScene(
                holdCompanion,
                CompanionInteractionState.DragHold) is not null)
        {
            _dragHoldTimer.Start();
        }
        e.Handled = true;
    }

    private void DragHoldTimerTick(object? sender, EventArgs e)
    {
        _dragHoldTimer.Stop();
        if (_dragged)
            StartDragInteraction(CompanionInteractionState.DragHold);
    }

    private void StartDragInteraction(CompanionInteractionState state)
    {
        var companion = _playbackCompanion;
        var scene = companion is null
            ? null
            : CompanionInteractionStatePolicy.GetAssignedDragScene(companion, state);
        if (scene is null)
            return;

        _dragInteractionPlaybackStarted = true;
        var role = GetInteractionSceneRole(state);
        if (_sceneRole == role && _activeScene?.Id == scene.Id)
            return;

        _idleTimer.Stop();
        StartScene(scene, role, forceLoop: true, transition: true);
    }

    private void StartClickInteraction(Point viewportPoint)
    {
        var companion = _playbackCompanion;
        if (companion is null || !CompanionInteractionStatePolicy.HasAnyClickScene(companion))
            return;

        var scene = CompanionInteractionStatePolicy.GetClickScene(
            companion,
            viewportPoint.X,
            viewportPoint.Y,
            CompanionViewport.ActualWidth,
            CompanionViewport.ActualHeight);
        if (scene is null)
            return;

        _idleTimer.Stop();
        StartScene(
            scene,
            AnimationSceneRole.Click,
            forceLoop: false,
            forceOneShot: true,
            transition: true);
    }

    private static AnimationSceneRole GetInteractionSceneRole(CompanionInteractionState state) => state switch
    {
        CompanionInteractionState.DragLeft => AnimationSceneRole.DragLeft,
        CompanionInteractionState.DragRight => AnimationSceneRole.DragRight,
        CompanionInteractionState.DragHold => AnimationSceneRole.DragHold,
        _ => AnimationSceneRole.None
    };

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
        if (!_pointerDown)
            return;

        var wasDragged = _dragged;
        var restorePlayback = _dragInteractionPlaybackStarted;
        EndPointerInteraction();
        if (wasDragged)
        {
            SaveCharacterPosition();
            RestorePlaybackAfterDrag(restorePlayback);
        }
        else if (_playbackCompanion is { } companion
            && CompanionInteractionStatePolicy.HasAnyClickScene(companion))
        {
            StartClickInteraction(e.GetPosition(CompanionViewport));
        }

        e.Handled = true;
    }

    private void Window_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_pointerDown)
            return;

        var wasDragged = _dragged;
        var restorePlayback = _dragInteractionPlaybackStarted;
        EndPointerInteraction();
        if (!wasDragged)
            return;

        SaveCharacterPosition();
        RestorePlaybackAfterDrag(restorePlayback);
    }

    private void RestorePlaybackAfterDrag(bool restorePlayback)
    {
        if (!restorePlayback || _playbackCompanion is not { } companion)
            return;

        if (FinishHeldModelClip(companion)) return;
        if (_manager.ActivityState == CompanionActivityState.Reminder)
            StartReminderState(companion, transition: true);
        else
        {
            StartIdleDefault(companion, transition: true);
            ScheduleRandomIdle();
        }
    }

    private bool FinishHeldModelClip(CompanionDefinition pet) =>
        pet.Appearance == CompanionAppearance.Model3D && _activeScene?.ModelLoop?.Mode == ModelLoopMode.UntilDialogueEnds
        && FinishCurrentLoopThen(() => StartDefaultForCurrentState(pet, transition: true));

    private void EndPointerInteraction()
    {
        _dragHoldTimer.Stop();
        _pointerDown = false;
        _dragged = false;
        _dragInteractionPlaybackStarted = false;
        Cursor = null;
        if (Mouse.Captured == this)
            Mouse.Capture(null);
    }

    private void SaveCharacterPosition() => _manager.SavePosition(_characterPosition.X, _characterPosition.Y);

    public void CloseForExit(Action? completed = null)
    {
        if (_closingForExit)
            return;

        _closingForExit = true;
        _exitCompleted = completed;
        _pendingReminder = null;
        _activeReminder = null;
        ReminderActions.Visibility = Visibility.Collapsed;
        if (IsVisible && _playbackCompanion?.ActiveScenes.ExitAnimation is { } exitScene)
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
        StopAll(releaseModel: true);
        _animationPlayer.Dispose();
        Close();
        completed?.Invoke();
    }

    private sealed record PendingCalendarReminder(CalendarEvent CalendarEvent, DateTime OccurrenceStart);
}
