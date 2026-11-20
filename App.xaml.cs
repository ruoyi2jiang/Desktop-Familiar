using System.Threading;
using System.Reflection;
using System.Net.Http;
using System.Windows.Threading;
using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Calendar;
using DesktopFamiliar.Services.Addons;
using DesktopFamiliar.Services.Updates;
using DesktopFamiliar.Views;

namespace DesktopFamiliar;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private TrayService? _trayService;
    private CompanionOverlayWindow? _overlay;
    private CompanionLibraryWindow? _library;
    private CompanionDeletionService _deletions = null!;
    private SettingsWindow? _settings;
    private ChatHistoryWindow? _historyWindow;
    public ChatController Chat { get; private set; } = null!;
    private CalendarEventWindow? _calendarEvents;
    private CalendarReminderService? _calendarReminders;
    private CalendarReminderPresentationCoordinator? _reminderPresentation;
    private AddonRuntimeLoader? _addonRuntimeLoader;
    private readonly HttpClient _httpClient = new();
    internal AppUpdateService Updates { get; private set; } = null!;
    private readonly AddonViewportActionRegistry _addonViewportActions = new();
    private readonly AddonSettingsSectionRegistry _addonSettingsSections = new();
    private int _fatalReportShown;
    private bool _shutdownRequested;

    public CompanionStorage Storage { get; private set; } = null!;
    public CompanionManager Manager { get; private set; } = null!;
    public AddonInstallationStore AddonInstallations { get; private set; } = null!;
    public AddonSettingsStore AddonSettings { get; private set; } = null!;
    public AddonManagerService AddonManager { get; private set; } = null!;

    public static App CurrentApp => (App)Current;
    internal static Version CurrentVersion { get; } = new(Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
    public static string DisplayVersion => $"v{CurrentVersion}";

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppLogger.Initialize();
        RegisterExceptionHandlers();
        try
        {
            if (!ApplicationRestartService.WaitForRestartParent(e.Args))
                AppLogger.Warning("The previous Desktop Familiar process did not exit before the restart timeout.");
            LocalizationService.Initialize();
            _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\DesktopFamiliar-84D01F52-E8F4-4D45-9D86-D25CC33290BA", out var isFirst);
            if (!isFirst)
            {
                System.Windows.MessageBox.Show(LocalizationService.Get("App.AlreadyRunning"), LocalizationService.Get("App.Name"));
                Shutdown();
                return;
            }

            base.OnStartup(e);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            AppLogger.Info("Application startup began.");
            Storage = new CompanionStorage();
            Manager = new CompanionManager(Storage);
            AddonSettings = new AddonSettingsStore(Storage.RootDirectory);
            try
            {
                new LegacyAutoClickerSettingsMigrator(Storage.RootDirectory, AddonSettings).MigrateIfNeeded();
            }
            catch (Exception exception)
            {
                // The legacy addon import can be retried on the next launch.
                AppLogger.Warning("Could not migrate legacy Auto Clicker settings to addon storage.", exception);
            }
            AddonInstallations = new AddonInstallationStore(Storage.RootDirectory);
            AddonManager = new AddonManagerService(_httpClient, AddonInstallations);
            Updates = new AppUpdateService(_httpClient);
            ShowStorageRecoveryNotice(Storage.LastRecoveryNotice);
            ThemeService.Apply(Manager.CurrentSavedTheme.Palette);
            Manager.ThemeChanged += (_, _) => ThemeService.Apply(Manager.CurrentSavedTheme.Palette);
            _overlay = new CompanionOverlayWindow(Manager, _addonViewportActions);
            Chat = new ChatController(Manager, _overlay, new ChatModelStore(Storage.RootDirectory, _httpClient), new ChatHistoryStore(Storage.RootDirectory));
            _deletions = new CompanionDeletionService(Manager, Storage, Chat.History);
            _deletions.RetryPending(deferSave: true);
            _addonRuntimeLoader = new AddonRuntimeLoader(AddonInstallations, AppLogger.Error);
            _addonRuntimeLoader.LoadEnabled(new DesktopFamiliarAddonHost(
                _addonViewportActions,
                _addonSettingsSections,
                AddonSettings,
                new DesktopFamiliarAddonStringProvider(),
                new DispatcherAddonCompanionStatusPresenter(Dispatcher, message => _overlay?.ShowAddonStatus(message))));
            Manager.FocusModeChanged += (_, _) => ApplyVisibility();
            Manager.ActivityStateChanged += (_, _) => ApplyVisibility();
            Manager.CurrentCompanionChanged += (_, _) => ApplyVisibility();
            _trayService = new TrayService(Manager, ShowLibrary, ShowCalendarEvents, ShowChatHistory, ShowSettings, ShutdownApplication);
            _reminderPresentation = new CalendarReminderPresentationCoordinator(
                Manager,
                ApplyVisibility,
                (calendarEvent, occurrenceStart) => _overlay.ShowCalendarReminder(calendarEvent, occurrenceStart),
                () => _overlay.ClearCalendarReminder());
            ApplyVisibility();

            _calendarReminders = new CalendarReminderService(Manager);
            _calendarReminders.ReminderRaised += (_, args) => ShowCalendarReminder(args);
            _calendarReminders.ReminderEnded += (_, args) => EndCalendarReminderPresentation(args);
            _calendarReminders.Start();

            if (Manager.Data.Companions.Count == 0)
                ShowLibrary();
            AppLogger.Info("Application startup completed.");
        }
        catch (Exception exception)
        {
            ShowFatalReport("Application startup failed.", exception);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        System.Windows.Forms.Application.ThreadException += OnFormsThreadException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (TryHandleRecoverableException(e.Exception))
            return;

        ShowFatalReport("An unhandled UI exception stopped the application.", e.Exception);
    }

    private void OnFormsThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!TryHandleRecoverableException(e.Exception))
                ShowFatalReport("An unhandled notification-area exception stopped the application.", e.Exception);
        });
    }

    private static bool TryHandleRecoverableException(Exception exception)
    {
        if (exception is not AppPersistenceException)
            return false;

        System.Windows.MessageBox.Show(
            exception.Message,
            LocalizationService.Get("App.Name"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException($"A non-exception object was thrown: {e.ExceptionObject}");
        if (Dispatcher.HasShutdownStarted)
        {
            AppLogger.Fatal("An unhandled background exception occurred during shutdown.", exception);
            return;
        }
        try
        {
            Dispatcher.Invoke(() => ShowFatalReport("An unhandled background exception stopped the application.", exception));
        }
        catch (Exception reportingException)
        {
            AppLogger.Fatal("The fatal exception reporter also failed.", reportingException);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("An unobserved task exception was captured.", e.Exception);
        e.SetObserved();
    }

    private void ShowFatalReport(string context, Exception exception)
    {
        AppLogger.Fatal(context, exception);
        if (Interlocked.Exchange(ref _fatalReportShown, 1) != 0)
            return;

        try
        {
            _trayService?.Dispose();
            _overlay?.Hide();
            var report = new CrashReportWindow();
            report.ShowDialog();
        }
        catch (Exception reportingException)
        {
            AppLogger.Fatal("The crash-report window could not be displayed.", reportingException);
            MessageBox.Show(
                GetLocalizedOrDefault("App.FatalFallback.Message", $"Desktop Familiar stopped unexpectedly. The session log is stored at:{Environment.NewLine}{AppLogger.LogFilePath}", AppLogger.LogFilePath),
                GetLocalizedOrDefault("App.FatalFallback.Title", "Desktop Familiar stopped unexpectedly"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown(-1);
        }
    }

    private static string GetLocalizedOrDefault(string key, string fallback, params object[] arguments)
    {
        try
        {
            return arguments.Length == 0
                ? LocalizationService.Get(key)
                : LocalizationService.Format(key, arguments);
        }
        catch
        {
            return fallback;
        }
    }

    private static void ShowStorageRecoveryNotice(StorageRecoveryNotice? notice)
    {
        if (notice is null)
            return;

        var message = LocalizationService.Format("Storage.LibraryUnreadable", notice.FilePath, notice.ErrorMessage);
        MessageBox.Show(
            message,
            LocalizationService.Get("App.Name"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ApplyVisibility()
    {
        if (_overlay is null)
            return;
        if (Manager.ShouldDisplayCompanion)
            _overlay.ShowCompanion();
        else
            _overlay.HideCompanion();
    }

    public void ShowLibrary()
    {
        if (_library is { IsLoaded: true })
        {
            _library.Activate();
            return;
        }

        _library = new CompanionLibraryWindow(Manager, Storage, _deletions);
        _library.Show();
        _library.Activate();
    }

    public void ShowCurrentCompanionEditor()
    {
        if (Manager.CurrentCompanion is null)
        {
            ShowLibrary();
            return;
        }
        var editor = new CompanionEditorWindow(Manager, Storage, Manager.CurrentCompanion);
        editor.Show();
        editor.Activate();
    }

    public void ShowChatHistory()
    {
        if (_historyWindow is { IsLoaded: true }) { _historyWindow.Activate(); return; }
        _historyWindow = new ChatHistoryWindow(Manager, Chat);
        _historyWindow.Show();
    }

    public void ShowSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(
            Manager,
            AddonManager,
            _addonSettingsSections);
        _settings.Show();
        _settings.Activate();
    }

    public void ShowCalendarEvents()
    {
        if (_calendarEvents is { IsLoaded: true })
        {
            _calendarEvents.Activate();
            return;
        }

        _calendarEvents = new CalendarEventWindow(Manager);
        _calendarEvents.Show();
        _calendarEvents.Activate();
    }

    public void SnoozeCalendarReminder() => ExecuteCalendarReminderAction(
        () => _calendarReminders?.SnoozeActiveReminder());

    public void AcknowledgeCalendarReminder() => ExecuteCalendarReminderAction(
        () => _calendarReminders?.AcknowledgeActiveReminder());

    private static void ExecuteCalendarReminderAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            AppLogger.Warning("A calendar reminder action could not be saved.", exception);
            System.Windows.MessageBox.Show(
                LocalizationService.Get("Calendar.Reminder.ActionFailed"),
                LocalizationService.Get("Calendar.Reminder.ActionFailedTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void ShowCalendarReminder(CalendarReminderEventArgs args)
    {
        if (_overlay is null || _reminderPresentation is null)
            return;
        Manager.SetDialogBubbleHidden(false, automatic: true);
        AppLogger.Info($"Calendar reminder presentation requested while Focus mode is {Manager.Data.Settings.FocusModeEnabled}.");
        _reminderPresentation.Present(args);
    }

    private void EndCalendarReminderPresentation(CalendarReminderEndedEventArgs args)
    {
        _reminderPresentation?.End(args);
    }

    public void ShutdownApplication()
    {
        if (_shutdownRequested)
            return;
        _shutdownRequested = true;
        AppLogger.Info("Application shutdown was requested.");
        _addonRuntimeLoader?.Dispose();
        _trayService?.Dispose();
        _calendarReminders?.Dispose();
        Chat?.Dispose();
        _httpClient.Dispose();
        if (_overlay is not null)
        {
            _overlay.CloseForExit(CompleteNormalShutdown);
            return;
        }

        CompleteNormalShutdown();
    }

    private void CompleteNormalShutdown()
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        Shutdown();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_fatalReportShown == 0)
            AppLogger.Info($"Application exited normally with code {e.ApplicationExitCode}.");
        _trayService?.Dispose();
        _addonRuntimeLoader?.Dispose();
        _calendarReminders?.Dispose();
        Chat?.Dispose();
        _httpClient.Dispose();
        base.OnExit(e);
    }
}
