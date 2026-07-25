using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker.Models;
using DesktopFamiliar.Addons.AutoClicker.Services;

namespace DesktopFamiliar.Addons.AutoClicker.Views;

/// <summary>Registers the Auto Clicker's settings card on the host-managed Addons page.</summary>
public sealed class AutoClickerSettingsSection : IInlineAddonSettingsSection
{
    private readonly AutoClickerCoordinator _coordinator;
    private readonly IAddonStringProvider _strings;

    public AutoClickerSettingsSection(AutoClickerCoordinator coordinator, IAddonStringProvider strings)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
    }

    public string Id => AutoClickerAddonDefinition.Id;
    public int Order => 100;

    public FrameworkElement CreateView() => new AutoClickerSettingsControl(_coordinator, _strings);

    public AddonInlineSettingsContent CreateInlineContent() =>
        AutoClickerSettingsControl.CreateInlineContent(_coordinator, _strings);
}

/// <summary>Addon-owned WPF settings visual for the Auto Clicker.</summary>
public sealed class AutoClickerSettingsControl : Border
{
    private enum IntervalUnit
    {
        Milliseconds,
        Seconds
    }

    private enum HotkeyCaptureTarget
    {
        None,
        Toggle,
        BackgroundMode
    }

    private readonly AutoClickerCoordinator _coordinator;
    private readonly IAddonStringProvider _strings;
    private readonly ToggleButton _enabledToggle = new();
    private readonly StackPanel _optionsPanel = new();
    private readonly FrameworkElement? _inlineHeader;
    private readonly Button _hotkeyButton = new();
    private readonly Button _backgroundModeHotkeyButton = new();
    private readonly TextBox _intervalMinimumBox = new();
    private readonly TextBox _intervalMaximumBox = new();
    private readonly ComboBox _intervalUnitBox = new();
    private readonly CheckBox _backgroundModeCheck = new();
    private HotkeyCaptureTarget _capturingHotkey;
    private AutoClickHotkey _pendingHotkey = null!;
    private AutoClickHotkey _pendingBackgroundModeHotkey = null!;
    private IntervalUnit _intervalUnit = IntervalUnit.Seconds;
    private bool _loaded;
    private bool _updatingIntervalInputs;

    public AutoClickerSettingsControl(AutoClickerCoordinator coordinator, IAddonStringProvider strings)
        : this(coordinator, strings, isInline: false)
    {
    }

    private AutoClickerSettingsControl(AutoClickerCoordinator coordinator, IAddonStringProvider strings, bool isInline)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        if (isInline)
        {
            _inlineHeader = BuildInlineHeader();
            BuildOptionsPanel();
            _inlineHeader.PreviewKeyDown += Control_PreviewKeyDown;
            _optionsPanel.PreviewKeyDown += Control_PreviewKeyDown;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "PrimaryBackgroundBrush");
            SetResourceReference(BorderBrushProperty, "PrimaryAccentBrush");
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(14);
            Padding = new Thickness(20);
            SetResourceReference(TextElement.ForegroundProperty, "TextOnPrimaryBackgroundBrush");
            Child = BuildContent();
        }
        if (!isInline)
            PreviewKeyDown += Control_PreviewKeyDown;
        Populate(_coordinator.Settings);
        _loaded = true;
    }

    public static AddonInlineSettingsContent CreateInlineContent(
        AutoClickerCoordinator coordinator,
        IAddonStringProvider strings)
    {
        var control = new AutoClickerSettingsControl(coordinator, strings, isInline: true);
        return new AddonInlineSettingsContent(
            control._inlineHeader ?? throw new InvalidOperationException("The inline settings header was not created."),
            control._optionsPanel);
    }

    private FrameworkElement BuildContent()
    {
        var panel = new StackPanel();
        var header = new DockPanel();
        _enabledToggle.Style = FindStyle("SettingsToggle");
        _enabledToggle.ToolTip = _strings.Get("Settings.AutoClick.Enabled");
        _enabledToggle.Checked += EnabledToggle_Changed;
        _enabledToggle.Unchecked += EnabledToggle_Changed;
        DockPanel.SetDock(_enabledToggle, Dock.Right);
        header.Children.Add(_enabledToggle);
        header.Children.Add(Title("Settings.AutoClick.Title", 20));
        panel.Children.Add(header);
        panel.Children.Add(Description("Settings.AutoClick.Description", new Thickness(0, 6, 0, 20)));
        BuildOptionsPanel();
        panel.Children.Add(_optionsPanel);
        return panel;
    }

    private FrameworkElement BuildInlineHeader()
    {
        var header = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        _enabledToggle.Style = FindStyle("SettingsToggle");
        _enabledToggle.ToolTip = _strings.Get("Settings.AutoClick.Enabled");
        _enabledToggle.Checked += EnabledToggle_Changed;
        _enabledToggle.Unchecked += EnabledToggle_Changed;
        DockPanel.SetDock(_enabledToggle, Dock.Right);
        header.Children.Add(_enabledToggle);
        header.Children.Add(Description("Settings.AutoClick.Description", new Thickness(0, 0, 14, 0)));
        return header;
    }

    private void BuildOptionsPanel()
    {
        _optionsPanel.Children.Add(Title("Settings.AutoClick.Hotkey", 16));
        _optionsPanel.Children.Add(Description("Settings.AutoClick.Hotkey.Description", new Thickness(0, 5, 0, 12)));
        ConfigureActionButton(_hotkeyButton, AutoClickHotkey_Click);
        _optionsPanel.Children.Add(_hotkeyButton);

        _optionsPanel.Children.Add(Title("Settings.AutoClick.BackgroundModeHotkey", 16, new Thickness(0, 14, 0, 0)));
        _optionsPanel.Children.Add(Description("Settings.AutoClick.BackgroundModeHotkey.Description", new Thickness(0, 5, 0, 12)));
        ConfigureActionButton(_backgroundModeHotkeyButton, AutoClickBackgroundModeHotkey_Click);
        _optionsPanel.Children.Add(_backgroundModeHotkeyButton);

        _optionsPanel.Children.Add(Divider());
        _optionsPanel.Children.Add(Title("Settings.AutoClick.Interval", 16));
        _optionsPanel.Children.Add(Description("Settings.AutoClick.Interval.Description", new Thickness(0, 5, 0, 12)));
        _optionsPanel.Children.Add(BuildIntervalInputs());

        _optionsPanel.Children.Add(Divider());
        var backgroundModeRow = new StackPanel { Orientation = Orientation.Horizontal };
        _backgroundModeCheck.Content = _strings.Get("Settings.AutoClick.BackgroundMode");
        _backgroundModeCheck.Margin = new Thickness(0);
        backgroundModeRow.Children.Add(_backgroundModeCheck);
        backgroundModeRow.Children.Add(new AutoClickerHelpIcon(
            _strings.Get("Settings.AutoClick.BackgroundMode.Description")));
        _optionsPanel.Children.Add(backgroundModeRow);
        var backgroundModeWarning = new TextBlock
        {
            Text = _strings.Get("Settings.AutoClick.BackgroundMode.Warning"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        backgroundModeWarning.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryAccentBrush");
        _optionsPanel.Children.Add(backgroundModeWarning);
    }

    private FrameworkElement BuildIntervalInputs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(BuildIntervalField(_strings.Get("Settings.AutoClick.Interval.Minimum"), _intervalMinimumBox, true));
        row.Children.Add(BuildIntervalField(_strings.Get("Settings.AutoClick.Interval.Maximum"), _intervalMaximumBox, false, new Thickness(10, 0, 0, 0)));

        _intervalUnitBox.Width = 150;
        _intervalUnitBox.Margin = new Thickness(10, 18, 14, 0);
        _intervalUnitBox.VerticalAlignment = VerticalAlignment.Top;
        _intervalUnitBox.Items.Add(new ComboBoxItem { Tag = "milliseconds", Content = _strings.Get("Settings.AutoClick.Milliseconds") });
        _intervalUnitBox.Items.Add(new ComboBoxItem { Tag = "seconds", Content = _strings.Get("Settings.AutoClick.Seconds") });
        _intervalUnitBox.SelectionChanged += IntervalUnit_Changed;
        row.Children.Add(_intervalUnitBox);

        var apply = new Button
        {
            Content = _strings.Get("Settings.AutoClick.Apply"),
            Style = FindStyle("PrimaryButton"),
            Height = 38,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 18, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        apply.Click += Apply_Click;
        row.Children.Add(apply);
        return row;
    }

    private FrameworkElement BuildIntervalField(string label, TextBox input, bool isMinimum, Thickness? margin = null)
    {
        var panel = new StackPanel { Width = 120, Margin = margin ?? new Thickness() };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        input.Style = FindStyle("ThemedTextInput");
        input.Margin = new Thickness(0);
        if (isMinimum)
            input.TextChanged += IntervalMinimum_TextChanged;
        panel.Children.Add(input);
        return panel;
    }

    private void ConfigureActionButton(Button button, RoutedEventHandler handler)
    {
        button.Style = FindStyle("OutlineButton");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Height = 38;
        button.MinWidth = 170;
        button.Padding = new Thickness(16, 0, 16, 0);
        button.Margin = new Thickness(0);
        button.Click += handler;
    }

    private void Populate(AutoClickerSettings settings)
    {
        AutoClickerSettingsStore.Normalize(settings);
        _enabledToggle.IsChecked = settings.Enabled;
        _backgroundModeCheck.IsChecked = settings.BackgroundModeEnabled;
        _pendingHotkey = AutoClickHotkeyPolicy.TryParse(settings.Hotkey, out var hotkey)
            ? hotkey
            : throw new InvalidOperationException("The normalized Auto Clicker hotkey is invalid.");
        _pendingBackgroundModeHotkey = AutoClickHotkeyPolicy.TryParse(settings.BackgroundModeHotkey, out var backgroundHotkey)
            ? backgroundHotkey
            : throw new InvalidOperationException("The normalized Auto Clicker input-mode hotkey is invalid.");
        _hotkeyButton.Content = _pendingHotkey.DisplayText;
        _backgroundModeHotkeyButton.Content = _pendingBackgroundModeHotkey.DisplayText;
        _intervalUnitBox.SelectedIndex = 1;
        _intervalMinimumBox.Text = (settings.IntervalMilliseconds / 1000d).ToString("0.###", CultureInfo.CurrentCulture);
        _intervalMaximumBox.Text = (settings.IntervalMaximumMilliseconds / 1000d).ToString("0.###", CultureInfo.CurrentCulture);
        UpdateOptionsVisibility();
    }

    private void EnabledToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;

        try
        {
            if (!_coordinator.TrySetEnabled(_enabledToggle.IsChecked == true))
                ShowWarningForKey("Settings.AutoClick.Hotkey.Unavailable");
        }
        catch (Exception exception)
        {
            ShowError(_strings.Format("Settings.AutoClick.Enabled.Failed", exception.Message));
        }

        var settings = _coordinator.Settings;
        _loaded = false;
        _enabledToggle.IsChecked = settings.Enabled;
        _loaded = true;
        UpdateOptionsVisibility();
    }

    private void UpdateOptionsVisibility() =>
        _optionsPanel.Visibility = _coordinator.Settings.Enabled ? Visibility.Visible : Visibility.Collapsed;

    private void AutoClickHotkey_Click(object sender, RoutedEventArgs e) => BeginHotkeyCapture(HotkeyCaptureTarget.Toggle);

    private void AutoClickBackgroundModeHotkey_Click(object sender, RoutedEventArgs e) => BeginHotkeyCapture(HotkeyCaptureTarget.BackgroundMode);

    private void BeginHotkeyCapture(HotkeyCaptureTarget target)
    {
        _capturingHotkey = target;
        var button = target == HotkeyCaptureTarget.Toggle ? _hotkeyButton : _backgroundModeHotkeyButton;
        button.Content = _strings.Get("Settings.AutoClick.Hotkey.Capturing");
        button.Focus();
    }

    private void Control_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingHotkey == HotkeyCaptureTarget.None)
            return;

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            RestoreCapturedHotkeyButton();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!AutoClickHotkeyPolicy.TryCreate(GetModifiers(Keyboard.Modifiers), (uint)KeyInterop.VirtualKeyFromKey(key), out var hotkey))
        {
            SetCapturedHotkeyButtonContent(_strings.Get("Settings.AutoClick.Hotkey.Invalid"));
            return;
        }

        if (_capturingHotkey == HotkeyCaptureTarget.Toggle)
            _pendingHotkey = hotkey;
        else
            _pendingBackgroundModeHotkey = hotkey;
        SetCapturedHotkeyButtonContent(hotkey.DisplayText);
        _capturingHotkey = HotkeyCaptureTarget.None;
    }

    private void RestoreCapturedHotkeyButton()
    {
        SetCapturedHotkeyButtonContent(_capturingHotkey == HotkeyCaptureTarget.Toggle
            ? _pendingHotkey.DisplayText
            : _pendingBackgroundModeHotkey.DisplayText);
        _capturingHotkey = HotkeyCaptureTarget.None;
    }

    private void SetCapturedHotkeyButtonContent(string content)
    {
        if (_capturingHotkey == HotkeyCaptureTarget.Toggle)
            _hotkeyButton.Content = content;
        else if (_capturingHotkey == HotkeyCaptureTarget.BackgroundMode)
            _backgroundModeHotkeyButton.Content = content;
    }

    private void IntervalUnit_Changed(object sender, SelectionChangedEventArgs e)
    {
        var nextUnit = GetSelectedIntervalUnit();
        if (!_loaded || nextUnit == _intervalUnit)
            return;

        _updatingIntervalInputs = true;
        try
        {
            if (TryGetMilliseconds(_intervalMinimumBox, _intervalUnit, out var minimum))
                _intervalMinimumBox.Text = FormatInterval(minimum, nextUnit);
            if (TryGetMilliseconds(_intervalMaximumBox, _intervalUnit, out var maximum))
                _intervalMaximumBox.Text = FormatInterval(maximum, nextUnit);
        }
        finally
        {
            _updatingIntervalInputs = false;
        }
        _intervalUnit = nextUnit;
    }

    private void IntervalMinimum_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || _updatingIntervalInputs
            || !TryGetMilliseconds(_intervalMinimumBox, _intervalUnit, out var minimum)
            || !TryGetMilliseconds(_intervalMaximumBox, _intervalUnit, out var maximum)
            || maximum >= minimum)
        {
            return;
        }

        _updatingIntervalInputs = true;
        try
        {
            _intervalMaximumBox.Text = FormatInterval(minimum, _intervalUnit);
        }
        finally
        {
            _updatingIntervalInputs = false;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _intervalUnit = GetSelectedIntervalUnit();
        if (!TryGetMilliseconds(_intervalMinimumBox, _intervalUnit, out var minimum)
            || minimum < AutoClickerSettings.MinimumIntervalMilliseconds
            || minimum > AutoClickerSettings.MaximumIntervalMilliseconds)
        {
            ShowWarningMessage(_strings.Format("Settings.AutoClick.Interval.Invalid", AutoClickerSettings.MinimumIntervalMilliseconds, AutoClickerSettings.MaximumIntervalMilliseconds));
            return;
        }
        if (!TryGetMilliseconds(_intervalMaximumBox, _intervalUnit, out var maximum)
            || maximum < minimum
            || maximum > AutoClickerSettings.MaximumIntervalMilliseconds)
        {
            ShowWarningMessage(_strings.Format("Settings.AutoClick.Interval.Range.Invalid", AutoClickerSettings.MinimumIntervalMilliseconds, AutoClickerSettings.MaximumIntervalMilliseconds));
            return;
        }
        try
        {
            if (!_coordinator.TryUpdateSettings(
                    _pendingHotkey.DisplayText,
                    _pendingBackgroundModeHotkey.DisplayText,
                    minimum,
                    maximum,
                    _backgroundModeCheck.IsChecked == true))
            {
                ShowWarningForKey("Settings.AutoClick.Hotkey.Unavailable");
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError(_strings.Format("Settings.AutoClick.Enabled.Failed", exception.Message));
            return;
        }

        Populate(_coordinator.Settings);
    }

    private IntervalUnit GetSelectedIntervalUnit() =>
        (_intervalUnitBox.SelectedItem as ComboBoxItem)?.Tag as string == "milliseconds"
            ? IntervalUnit.Milliseconds
            : IntervalUnit.Seconds;

    private static bool TryGetMilliseconds(TextBox input, IntervalUnit unit, out int milliseconds)
    {
        var valid = double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!valid || value <= 0)
        {
            milliseconds = 0;
            return false;
        }

        var rawMilliseconds = unit == IntervalUnit.Seconds ? value * 1000d : value;
        if (rawMilliseconds > int.MaxValue)
        {
            milliseconds = 0;
            return false;
        }

        milliseconds = (int)Math.Round(rawMilliseconds, MidpointRounding.AwayFromZero);
        return true;
    }

    private static string FormatInterval(int milliseconds, IntervalUnit unit) =>
        (unit == IntervalUnit.Seconds ? milliseconds / 1000d : milliseconds).ToString("0.###", CultureInfo.CurrentCulture);

    private static uint GetModifiers(ModifierKeys modifiers)
    {
        var value = 0u;
        if ((modifiers & ModifierKeys.Control) != 0)
            value |= AutoClickHotkeyPolicy.ModifierControl;
        if ((modifiers & ModifierKeys.Alt) != 0)
            value |= AutoClickHotkeyPolicy.ModifierAlt;
        if ((modifiers & ModifierKeys.Shift) != 0)
            value |= AutoClickHotkeyPolicy.ModifierShift;
        if ((modifiers & ModifierKeys.Windows) != 0)
            value |= AutoClickHotkeyPolicy.ModifierWin;
        return value;
    }

    private TextBlock Title(string key, double fontSize, Thickness? margin = null) => new()
    {
        Text = _strings.Get(key),
        FontSize = fontSize,
        FontWeight = FontWeights.SemiBold,
        Margin = margin ?? new Thickness(),
        VerticalAlignment = VerticalAlignment.Center
    };

    private TextBlock Description(string key, Thickness margin) => new()
    {
        Text = _strings.Get(key),
        Opacity = 0.72,
        Margin = margin,
        TextWrapping = TextWrapping.Wrap
    };

    private FrameworkElement Divider()
    {
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 16, 0, 16),
            Opacity = 0.5
        };
        divider.SetResourceReference(BackgroundProperty, "PrimaryAccentBrush");
        return divider;
    }

    private static Style FindStyle(string key) =>
        Application.Current.TryFindResource(key) as Style
        ?? throw new InvalidOperationException($"The host did not provide the '{key}' control style.");

    private void ShowWarningForKey(string key) => ShowWarningMessage(_strings.Get(key));

    private void ShowWarningMessage(string message) => MessageBox.Show(message, _strings.Get("App.Name"), MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ShowError(string message) => MessageBox.Show(message, _strings.Get("App.Name"), MessageBoxButton.OK, MessageBoxImage.Error);
}
