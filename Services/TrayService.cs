using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Views.Controls;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopFamiliar.Services;

public sealed class TrayService : IDisposable
{
    private readonly CompanionManager _manager;
    private readonly Action _showCompanionLibrary;
    private readonly Action _showCalendarEvents;
    private readonly Action _showSettings;
    private readonly Action _showHistory;
    private readonly Action _exit;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly ThemedTrayContextMenu _trayMenu;
    private TrayToggleColors _toggleColors;
    private bool _refreshAfterClose;

    public TrayService(
        CompanionManager manager,
        Action showCompanionLibrary,
        Action showCalendarEvents,
        Action showHistory,
        Action showSettings,
        Action exit)
    {
        _manager = manager;
        _showCompanionLibrary = showCompanionLibrary;
        _showCalendarEvents = showCalendarEvents;
        _showSettings = showSettings;
        _showHistory = showHistory;
        _exit = exit;

        // A native notification-area menu lets Windows own activation and
        // outside-click dismissal. A programmatically opened WPF ContextMenu
        // has no native owner here and can remain open after focus moves away.
        _trayMenu = new ThemedTrayContextMenu();
        _trayMenu.Closed += TrayMenu_Closed;
        _manager.CurrentCompanionChanged += ManagerTrayContentChanged;
        _manager.CompanionCollectionChanged += ManagerTrayContentChanged;
        _manager.FocusModeChanged += ManagerTrayContentChanged;
        _manager.ThemeChanged += ManagerTrayContentChanged;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = LocalizationService.Get("App.Name"),
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        BuildTrayMenu();
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
            if (resource is null)
                throw new InvalidOperationException("The embedded application icon could not be found.");
            using var stream = resource.Stream;
            using var icon = new Drawing.Icon(stream);
            return (Drawing.Icon)icon.Clone();
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The application tray icon could not be loaded; the Windows default icon will be used.", exception);
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
    }

    private void BuildTrayMenu()
    {
        ClearTrayItems();
        ApplyTrayTheme(_manager.CurrentSavedTheme.Palette);

        var focusToggle = new TrayToggleMenuItem(
            LocalizationService.Get("FocusMode.Label"),
            _manager.Data.Settings.FocusModeEnabled,
            _toggleColors)
        {
            ToolTipText = LocalizationService.Get("FocusMode.Tooltip")
        };
        focusToggle.Toggled += (_, _) =>
        {
            try
            {
                _manager.SetFocusMode(focusToggle.IsChecked);
            }
            catch
            {
                focusToggle.IsChecked = _manager.Data.Settings.FocusModeEnabled;
                throw;
            }
        };
        _trayMenu.Items.Add(focusToggle);

        var selectCompanion = CreateMenuItem(LocalizationService.Get("Tray.SelectCompanion"));
        if (_manager.Data.Companions.Count == 0)
        {
            selectCompanion.DropDownItems.Add(
                CreateMenuItem(LocalizationService.Get("Tray.NoConfiguredCompanions"), enabled: false));
        }
        else
        {
            foreach (var companion in _manager.Data.Companions.OrderBy(companion => companion.Name))
            {
                var item = CreateMenuItem(companion.Name);
                item.Checked = companion.Id == _manager.Data.Settings.CurrentCompanionId;
                item.Click += (_, _) => _manager.SelectCompanion(companion.Id);
                selectCompanion.DropDownItems.Add(item);
            }
        }
        _trayMenu.Items.Add(selectCompanion);

        var configure = CreateMenuItem(LocalizationService.Get("Tray.ConfigureCompanion"));
        configure.Click += (_, _) => _showCompanionLibrary();
        _trayMenu.Items.Add(configure);

        var setEvent = CreateMenuItem(LocalizationService.Get("Calendar.Menu.SetEvent"));
        setEvent.Click += (_, _) => _showCalendarEvents();
        _trayMenu.Items.Add(setEvent);

        var history = CreateMenuItem(LocalizationService.Get("Chat.History"));
        history.Click += (_, _) => _showHistory();
        _trayMenu.Items.Add(history);

        var settings = CreateMenuItem(LocalizationService.Get("Tray.Settings"));
        settings.Click += (_, _) => _showSettings();
        _trayMenu.Items.Add(settings);

        _trayMenu.Items.Add(new ThemedTraySeparator());
        var exit = CreateMenuItem(LocalizationService.Get("Tray.Exit"));
        exit.Click += (_, _) => _exit();
        _trayMenu.Items.Add(exit);

        _trayMenu.ApplyItemTheme(_trayMenu.Items);
    }

    private void TrayMenu_Closed(object? sender, Forms.ToolStripDropDownClosedEventArgs e)
    {
        if (!_refreshAfterClose)
            return;

        _refreshAfterClose = false;
        BuildTrayMenu();
    }

    private void ManagerTrayContentChanged(object? sender, EventArgs e)
    {
        if (_trayMenu.Visible)
        {
            _refreshAfterClose = true;
            return;
        }

        BuildTrayMenu();
    }

    private void ClearTrayItems()
    {
        foreach (var item in _trayMenu.Items.Cast<Forms.ToolStripItem>().ToArray())
            item.Dispose();
        _trayMenu.Items.Clear();
    }

    private void ApplyTrayTheme(CustomThemePalette palette)
    {
        var background = ParseColor(palette.PageBackground, Drawing.Color.White);
        var foreground = ParseColor(palette.PageTextOnBackground, Drawing.Color.Black);
        var hover = ParseColor(palette.PageAccent, Drawing.Color.LightGray);
        var hoverForeground = ParseColor(palette.PageTextOnAccent, Drawing.Color.Black);
        var hoverActive = ParseColor(palette.PageAccentHover, hover);
        var pressed = ParseColor(palette.PageAccentActive, hover);
        var border = ParseColor(palette.PageAccent, hover);

        _toggleColors = new TrayToggleColors(
            Background: background,
            Foreground: foreground,
            TrackOff: ParseColor(palette.PageBackgroundHover, background),
            TrackOn: hover,
            TrackOnHover: hoverActive,
            ThumbOff: hover,
            ThumbOn: hoverForeground,
            Border: border);

        _trayMenu.BackColor = background;
        _trayMenu.ForeColor = foreground;
        _trayMenu.ApplyTheme(new TrayMenuColors(
            Background: background,
            Foreground: foreground,
            DisabledForeground: Blend(foreground, background, 0.5f),
            HoverBackground: hover,
            HoverForeground: hoverForeground,
            PressedBackground: pressed,
            Border: border));
    }

    private static Forms.ToolStripMenuItem CreateMenuItem(string text, bool enabled = true) =>
        new ThemedTrayMenuItem(text, enabled);

    private static Drawing.Color ParseColor(string value, Drawing.Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var hex = value.Trim().TrimStart('#');
        try
        {
            return hex.Length switch
            {
                8 => Drawing.Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16),
                    Convert.ToInt32(hex[6..8], 16)),
                6 => Drawing.Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16)),
                _ => fallback
            };
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static Drawing.Color Blend(Drawing.Color foreground, Drawing.Color background, float foregroundWeight)
    {
        var backgroundWeight = 1f - foregroundWeight;
        return Drawing.Color.FromArgb(
            (int)((foreground.R * foregroundWeight) + (background.R * backgroundWeight)),
            (int)((foreground.G * foregroundWeight) + (background.G * backgroundWeight)),
            (int)((foreground.B * foregroundWeight) + (background.B * backgroundWeight)));
    }

    public void Dispose()
    {
        _notifyIcon.ContextMenuStrip = null;
        _trayMenu.Closed -= TrayMenu_Closed;
        _manager.CurrentCompanionChanged -= ManagerTrayContentChanged;
        _manager.CompanionCollectionChanged -= ManagerTrayContentChanged;
        _manager.FocusModeChanged -= ManagerTrayContentChanged;
        _manager.ThemeChanged -= ManagerTrayContentChanged;
        _trayMenu.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }

}
