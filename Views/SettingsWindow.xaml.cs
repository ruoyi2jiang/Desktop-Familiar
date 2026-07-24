using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using System.Reflection;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.AutoClick;

namespace DesktopFamiliar.Views;

public partial class SettingsWindow : Window
{
    private sealed record ThemeTokenDefinition(string PropertyName, string LabelKey);

    private sealed record ThemeTokenGroup(string LabelKey, IReadOnlyList<ThemeTokenDefinition> Tokens);

    private sealed record CustomThemeTokenControl(TextBox Input, Border Preview);

    private static readonly ThemeTokenGroup[] CustomThemeTokenGroups =
    [
        new("Settings.Theme.Group.Page",
        [
            new(nameof(CustomThemePalette.PageBackground), "Settings.Theme.Token.PageBackground"),
            new(nameof(CustomThemePalette.PageTextOnBackground), "Settings.Theme.Token.PageTextOnBackground"),
            new(nameof(CustomThemePalette.PageBackgroundHover), "Settings.Theme.Token.PageBackgroundHover"),
            new(nameof(CustomThemePalette.PageBackgroundActive), "Settings.Theme.Token.PageBackgroundActive"),
            new(nameof(CustomThemePalette.PageAccent), "Settings.Theme.Token.PageAccent"),
            new(nameof(CustomThemePalette.PageTextOnAccent), "Settings.Theme.Token.PageTextOnAccent"),
            new(nameof(CustomThemePalette.PageAccentHover), "Settings.Theme.Token.PageAccentHover"),
            new(nameof(CustomThemePalette.PageAccentActive), "Settings.Theme.Token.PageAccentActive")
        ]),
        new("Settings.Theme.Group.Primary",
        [
            new(nameof(CustomThemePalette.PrimaryBackground), "Settings.Theme.Token.PrimaryBackground"),
            new(nameof(CustomThemePalette.PrimaryTextOnBackground), "Settings.Theme.Token.PrimaryTextOnBackground"),
            new(nameof(CustomThemePalette.PrimaryBackgroundHover), "Settings.Theme.Token.PrimaryBackgroundHover"),
            new(nameof(CustomThemePalette.PrimaryBackgroundActive), "Settings.Theme.Token.PrimaryBackgroundActive"),
            new(nameof(CustomThemePalette.PrimaryAccent), "Settings.Theme.Token.PrimaryAccent"),
            new(nameof(CustomThemePalette.PrimaryTextOnAccent), "Settings.Theme.Token.PrimaryTextOnAccent"),
            new(nameof(CustomThemePalette.PrimaryAccentHover), "Settings.Theme.Token.PrimaryAccentHover"),
            new(nameof(CustomThemePalette.PrimaryAccentActive), "Settings.Theme.Token.PrimaryAccentActive")
        ]),
        new("Settings.Theme.Group.Secondary",
        [
            new(nameof(CustomThemePalette.SecondaryBackground), "Settings.Theme.Token.SecondaryBackground"),
            new(nameof(CustomThemePalette.SecondaryTextOnBackground), "Settings.Theme.Token.SecondaryTextOnBackground"),
            new(nameof(CustomThemePalette.SecondaryBackgroundHover), "Settings.Theme.Token.SecondaryBackgroundHover"),
            new(nameof(CustomThemePalette.SecondaryBackgroundActive), "Settings.Theme.Token.SecondaryBackgroundActive"),
            new(nameof(CustomThemePalette.SecondaryAccent), "Settings.Theme.Token.SecondaryAccent"),
            new(nameof(CustomThemePalette.SecondaryTextOnAccent), "Settings.Theme.Token.SecondaryTextOnAccent"),
            new(nameof(CustomThemePalette.SecondaryAccentHover), "Settings.Theme.Token.SecondaryAccentHover"),
            new(nameof(CustomThemePalette.SecondaryAccentActive), "Settings.Theme.Token.SecondaryAccentActive")
        ]),
        new("Settings.Theme.Group.Input",
        [
            new(nameof(CustomThemePalette.InputBackground), "Settings.Theme.Token.InputBackground"),
            new(nameof(CustomThemePalette.InputTextOnBackground), "Settings.Theme.Token.InputTextOnBackground"),
            new(nameof(CustomThemePalette.InputBackgroundHover), "Settings.Theme.Token.InputBackgroundHover"),
            new(nameof(CustomThemePalette.InputBackgroundActive), "Settings.Theme.Token.InputBackgroundActive"),
            new(nameof(CustomThemePalette.InputAccent), "Settings.Theme.Token.InputAccent"),
            new(nameof(CustomThemePalette.InputTextOnAccent), "Settings.Theme.Token.InputTextOnAccent"),
            new(nameof(CustomThemePalette.InputAccentHover), "Settings.Theme.Token.InputAccentHover"),
            new(nameof(CustomThemePalette.InputAccentActive), "Settings.Theme.Token.InputAccentActive")
        ]),
        new("Settings.Theme.Group.Danger",
        [
            new(nameof(CustomThemePalette.Danger), "Settings.Theme.Token.Danger"),
            new(nameof(CustomThemePalette.DangerHover), "Settings.Theme.Token.DangerHover"),
            new(nameof(CustomThemePalette.DangerActive), "Settings.Theme.Token.DangerActive"),
            new(nameof(CustomThemePalette.TextOnDanger), "Settings.Theme.Token.TextOnDanger")
        ]),
        new("Settings.Theme.Group.Overlay",
        [
            new(nameof(CustomThemePalette.OverlaySurface), "Settings.Theme.Token.OverlaySurface"),
            new(nameof(CustomThemePalette.OverlaySurfaceHover), "Settings.Theme.Token.OverlaySurfaceHover"),
            new(nameof(CustomThemePalette.OverlayBorder), "Settings.Theme.Token.OverlayBorder"),
            new(nameof(CustomThemePalette.TextOnOverlay), "Settings.Theme.Token.TextOnOverlay")
        ])
    ];

    private static readonly ThemeTokenDefinition[] CustomThemeTokens = CustomThemeTokenGroups
        .SelectMany(group => group.Tokens)
        .ToArray();

    private static readonly IReadOnlyDictionary<string, PropertyInfo> CustomThemeProperties = typeof(CustomThemePalette)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(string) && property.CanRead && property.CanWrite)
        .ToDictionary(property => property.Name, StringComparer.Ordinal);

    private enum SettingsPage
    {
        General,
        AutoClick,
        User,
        Companion,
        Theme
    }

    private enum AutoClickIntervalUnit
    {
        Milliseconds,
        Seconds
    }

    private readonly CompanionManager _manager;
    private readonly AutoClickCoordinator _autoClickCoordinator;
    private readonly Dictionary<string, CustomThemeTokenControl> _customThemeTokenControls = new(StringComparer.Ordinal);
    private CustomThemePalette _editingPalette = new();
    private Guid? _editingThemeId;
    private bool _loaded;
    private bool _capturingAutoClickHotkey;
    private AutoClickHotkey _pendingAutoClickHotkey = null!;
    private AutoClickIntervalUnit _autoClickIntervalUnit = AutoClickIntervalUnit.Seconds;

    public SettingsWindow(CompanionManager manager, AutoClickCoordinator autoClickCoordinator)
    {
        InitializeComponent();
        _manager = manager;
        _autoClickCoordinator = autoClickCoordinator;
        RunAtStartupCheck.IsChecked = _manager.Data.Settings.RunAtStartup;
        UserBirthdayPicker.SelectedDate = _manager.Data.Settings.UserBirthday;
        ViewportWidthBox.Text = _manager.Data.Settings.ViewportWidth.ToString();
        ViewportHeightBox.Text = _manager.Data.Settings.ViewportHeight.ToString();
        TruncateDialogLinesCheck.IsChecked = _manager.Data.Settings.TruncateLongDialogLines;
        IdleIntervalBox.Text = _manager.Data.Settings.IdleDelaySeconds.ToString("0.##", CultureInfo.CurrentCulture);
        _pendingAutoClickHotkey = AutoClickHotkeyPolicy.TryParse(_manager.Data.Settings.AutoClickHotkey, out var savedHotkey)
            ? savedHotkey
            : throw new InvalidOperationException("The normalized auto-click hotkey is invalid.");
        AutoClickHotkeyButton.Content = _pendingAutoClickHotkey.DisplayText;
        AutoClickIntervalUnitComboBox.SelectedIndex = 1;
        AutoClickIntervalBox.Text = (_manager.Data.Settings.AutoClickIntervalMilliseconds / 1000d)
            .ToString("0.###", CultureInfo.CurrentCulture);
        RefreshThemeCards();
        Loaded += (_, _) => _loaded = true;
    }

    private void RunAtStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        var enabled = RunAtStartupCheck.IsChecked == true;
        try
        {
            _manager.SetRunAtStartup(enabled);
        }
        catch (Exception exception)
        {
            AppLogger.Error("The Windows startup setting could not be changed.", exception);
            MessageBox.Show(
                LocalizationService.Format("Settings.RunAtStartup.Failed", exception.Message),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _loaded = false;
            RunAtStartupCheck.IsChecked = _manager.Data.Settings.RunAtStartup;
            _loaded = true;
        }
    }

    private void ApplyViewport_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ViewportWidthBox.Text, out var width)
            || !int.TryParse(ViewportHeightBox.Text, out var height)
            || width < AppSettings.MinimumViewportSize
            || width > AppSettings.MaximumViewportSize
            || height < AppSettings.MinimumViewportSize
            || height > AppSettings.MaximumViewportSize)
        {
            MessageBox.Show(
                LocalizationService.Format(
                    "Settings.Companion.CharacterViewport.Invalid",
                    AppSettings.MinimumViewportSize,
                    AppSettings.MaximumViewportSize),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _manager.SetViewportSize(width, height);
        ViewportWidthBox.Text = _manager.Data.Settings.ViewportWidth.ToString();
        ViewportHeightBox.Text = _manager.Data.Settings.ViewportHeight.ToString();
    }

    private void TruncateDialogLines_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            _manager.SetTruncateLongDialogLines(TruncateDialogLinesCheck.IsChecked == true);
    }

    private void ApplyIdleInterval_Click(object sender, RoutedEventArgs e)
    {
        var valid = double.TryParse(IdleIntervalBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds)
            || double.TryParse(IdleIntervalBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        if (!valid
            || seconds < AppSettings.MinimumIdleDelaySeconds
            || seconds > AppSettings.MaximumIdleDelaySeconds)
        {
            MessageBox.Show(
                LocalizationService.Format(
                    "Settings.RandomIdleInterval.Invalid",
                    AppSettings.MinimumIdleDelaySeconds,
                    AppSettings.MaximumIdleDelaySeconds),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _manager.SetIdleDelaySeconds(seconds);
        IdleIntervalBox.Text = _manager.Data.Settings.IdleDelaySeconds.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void GeneralMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.General);

    private void AutoClickMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.AutoClick);

    private void UserMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.User);

    private void CompanionMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.Companion);

    private void ThemeMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.Theme);

    private void AutoClickHotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturingAutoClickHotkey = true;
        AutoClickHotkeyButton.Content = LocalizationService.Get("Settings.AutoClick.Hotkey.Capturing");
        AutoClickHotkeyButton.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingAutoClickHotkey)
            return;

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _capturingAutoClickHotkey = false;
            AutoClickHotkeyButton.Content = _pendingAutoClickHotkey.DisplayText;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!AutoClickHotkeyPolicy.TryCreate(GetAutoClickModifiers(Keyboard.Modifiers), virtualKey, out var hotkey))
        {
            AutoClickHotkeyButton.Content = LocalizationService.Get("Settings.AutoClick.Hotkey.Invalid");
            return;
        }

        _pendingAutoClickHotkey = hotkey;
        _capturingAutoClickHotkey = false;
        AutoClickHotkeyButton.Content = hotkey.DisplayText;
    }

    private void AutoClickIntervalUnit_Changed(object sender, SelectionChangedEventArgs e)
    {
        var nextUnit = GetSelectedAutoClickIntervalUnit();
        if (!_loaded || nextUnit == _autoClickIntervalUnit)
            return;

        if (TryGetAutoClickIntervalMilliseconds(_autoClickIntervalUnit, out var intervalMilliseconds))
            AutoClickIntervalBox.Text = FormatAutoClickInterval(intervalMilliseconds, nextUnit);
        _autoClickIntervalUnit = nextUnit;
    }

    private void ApplyAutoClickSettings_Click(object sender, RoutedEventArgs e)
    {
        _autoClickIntervalUnit = GetSelectedAutoClickIntervalUnit();
        if (!TryGetAutoClickIntervalMilliseconds(_autoClickIntervalUnit, out var intervalMilliseconds)
            || intervalMilliseconds < AppSettings.MinimumAutoClickIntervalMilliseconds
            || intervalMilliseconds > AppSettings.MaximumAutoClickIntervalMilliseconds)
        {
            MessageBox.Show(
                LocalizationService.Format(
                    "Settings.AutoClick.Interval.Invalid",
                    AppSettings.MinimumAutoClickIntervalMilliseconds,
                    AppSettings.MaximumAutoClickIntervalMilliseconds),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!_autoClickCoordinator.TryUpdateSettings(_pendingAutoClickHotkey.DisplayText, intervalMilliseconds))
        {
            MessageBox.Show(
                LocalizationService.Get("Settings.AutoClick.Hotkey.Unavailable"),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _pendingAutoClickHotkey = AutoClickHotkeyPolicy.TryParse(_manager.Data.Settings.AutoClickHotkey, out var savedHotkey)
            ? savedHotkey
            : _pendingAutoClickHotkey;
        AutoClickHotkeyButton.Content = _pendingAutoClickHotkey.DisplayText;
        AutoClickIntervalBox.Text = FormatAutoClickInterval(
            _manager.Data.Settings.AutoClickIntervalMilliseconds,
            _autoClickIntervalUnit);
    }

    private AutoClickIntervalUnit GetSelectedAutoClickIntervalUnit() =>
        (AutoClickIntervalUnitComboBox.SelectedItem as ComboBoxItem)?.Tag as string == "milliseconds"
            ? AutoClickIntervalUnit.Milliseconds
            : AutoClickIntervalUnit.Seconds;

    private bool TryGetAutoClickIntervalMilliseconds(AutoClickIntervalUnit unit, out int milliseconds)
    {
        var valid = double.TryParse(AutoClickIntervalBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(AutoClickIntervalBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!valid || value <= 0)
        {
            milliseconds = 0;
            return false;
        }

        var rawMilliseconds = unit == AutoClickIntervalUnit.Seconds ? value * 1000d : value;
        if (rawMilliseconds > int.MaxValue)
        {
            milliseconds = 0;
            return false;
        }

        milliseconds = (int)Math.Round(rawMilliseconds, MidpointRounding.AwayFromZero);
        return true;
    }

    private static string FormatAutoClickInterval(int milliseconds, AutoClickIntervalUnit unit) =>
        (unit == AutoClickIntervalUnit.Seconds ? milliseconds / 1000d : milliseconds)
            .ToString("0.###", CultureInfo.CurrentCulture);

    private static uint GetAutoClickModifiers(ModifierKeys modifiers)
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

    private void UserBirthdayPicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded)
            return;

        try
        {
            _manager.SetUserBirthday(UserBirthdayPicker.SelectedDate);
        }
        catch
        {
            _loaded = false;
            UserBirthdayPicker.SelectedDate = _manager.Data.Settings.UserBirthday;
            _loaded = true;
            throw;
        }
    }

    private void ClearUserBirthday_Click(object sender, RoutedEventArgs e) =>
        UserBirthdayPicker.SelectedDate = null;

    private void RefreshThemeCards()
    {
        var previousSourceId = (ThemeSourceComboBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        ThemeOptionsPanel.Children.Clear();
        ThemeSourceComboBox.Items.Clear();
        foreach (var theme in _manager.Data.Settings.SavedThemes)
        {
            ThemeOptionsPanel.Children.Add(CreateThemeCard(theme));
            ThemeSourceComboBox.Items.Add(new ComboBoxItem { Content = theme.Name, Tag = theme.Id });
        }

        var sourceId = previousSourceId is Guid existingId
            && _manager.Data.Settings.SavedThemes.Any(theme => theme.Id == existingId)
            ? existingId
            : _manager.Data.Settings.SelectedThemeId;
        ThemeSourceComboBox.SelectedItem = ThemeSourceComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is Guid id && id == sourceId);
    }

    private Border CreateThemeCard(SavedTheme theme)
    {
        var isSelected = _manager.Data.Settings.SelectedThemeId == theme.Id;
        var card = new Border
        {
            Width = 220,
            Height = 160,
            Background = (Brush)FindResource(isSelected ? ThemeTokens.PrimaryBackgroundActive : ThemeTokens.PrimaryBackgroundHover),
            BorderBrush = (Brush)FindResource(isSelected ? ThemeTokens.PrimaryAccent : ThemeTokens.PrimaryBackgroundHover),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 14, 14),
            Cursor = Cursors.Hand,
            Tag = theme.Id
        };
        card.MouseLeftButtonUp += ThemeCard_MouseLeftButtonUp;

        var titleRow = new Grid { Height = 28 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        var title = new TextBlock
        {
            Text = theme.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Foreground = (Brush)FindResource(ThemeTokens.PrimaryTextOnBackground),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        titleRow.Children.Add(title);

        if (!theme.IsBuiltIn)
        {
            var delete = new Button
            {
                Content = "\u00D7",
                ToolTip = LocalizationService.Get("Common.Delete"),
                Style = (Style)FindResource("DangerButton"),
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                FontSize = 18,
                Tag = theme.Id
            };
            delete.Click += DeleteTheme_Click;
            Grid.SetColumn(delete, 1);
            titleRow.Children.Add(delete);
        }

        var paletteBlocks = new UniformGrid
        {
            Columns = 6,
            Height = 88
        };
        AddPaletteBlock(paletteBlocks, theme.Palette.PageBackground, "Page background");
        AddPaletteBlock(paletteBlocks, theme.Palette.PageAccent, "Page accent");
        AddPaletteBlock(paletteBlocks, theme.Palette.PrimaryBackground, "Primary background");
        AddPaletteBlock(paletteBlocks, theme.Palette.PrimaryAccent, "Primary accent");
        AddPaletteBlock(paletteBlocks, theme.Palette.SecondaryBackground, "Secondary background");
        AddPaletteBlock(paletteBlocks, theme.Palette.SecondaryAccent, "Secondary accent");
        var palette = new Border
        {
            Height = 88,
            Margin = new Thickness(0, 10, 0, 0),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = paletteBlocks
        };

        card.Child = new StackPanel
        {
            Children =
            {
                titleRow,
                palette
            }
        };
        return card;
    }

    private static void AddPaletteBlock(Panel panel, string value, string label)
    {
        panel.Children.Add(new Border
        {
            Background = TryParseColor(value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent,
            ToolTip = label
        });
    }

    private void ThemeCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid id } || IsWithinButton(e.OriginalSource as DependencyObject))
            return;

        _manager.SelectSavedTheme(id);
        OpenThemeEditor(_manager.CurrentSavedTheme);
        RefreshThemeCards();
    }

    private static bool IsWithinButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button)
                return true;
            element = element is Visual
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void CreateTheme_Click(object sender, RoutedEventArgs e)
    {
        var sourceId = (ThemeSourceComboBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        var source = sourceId is Guid id
            ? _manager.Data.Settings.SavedThemes.FirstOrDefault(theme => theme.Id == id) ?? _manager.CurrentSavedTheme
            : _manager.CurrentSavedTheme;
        var theme = new SavedTheme
        {
            Name = LocalizationService.Get("Settings.Theme.NewName"),
            Palette = source.Palette.Clone()
        };
        OpenThemeEditor(theme);
    }

    private void OpenThemeEditor(SavedTheme theme)
    {
        _editingThemeId = theme.Id;
        _editingPalette = theme.Palette.Clone();
        ThemeNameBox.Text = theme.Name;
        BuildCustomThemeEditor();
        ThemeEditor.Visibility = Visibility.Visible;
    }

    private static Color ToColor(string value) => (Color)ColorConverter.ConvertFromString(value);

    private void BuildCustomThemeEditor()
    {
        CustomThemeTokenPanel.Children.Clear();
        _customThemeTokenControls.Clear();

        foreach (var group in CustomThemeTokenGroups)
        {
            var tokenPanel = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var token in group.Tokens)
            {
                var preview = new Border
                {
                    Width = 30,
                    Height = 34,
                    CornerRadius = new CornerRadius(7),
                    BorderBrush = (Brush)FindResource(ThemeTokens.InputAccent),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var input = new TextBox
                {
                    Text = GetThemeToken(_editingPalette, token.PropertyName),
                    Width = 80,
                    Height = 34,
                    Margin = new Thickness(0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = (Style)FindResource("SettingsTextInput")
                };
                UpdateColorPreview(preview, input.Text);
                input.TextChanged += (_, _) => UpdateColorPreview(preview, input.Text);

                var picker = new Button
                {
                    Content = LocalizationService.Get("Settings.Theme.Custom.Pick"),
                    Style = (Style)FindResource("OutlineButton"),
                    Height = 34,
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(0)
                };
                picker.Click += PickCustomThemeColor_Click;

                var controls = new Grid();
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(preview, 0);
                Grid.SetColumn(input, 2);
                Grid.SetColumn(picker, 4);
                controls.Children.Add(preview);
                controls.Children.Add(input);
                controls.Children.Add(picker);

                picker.Tag = new CustomThemeTokenControl(input, preview);
                _customThemeTokenControls[token.PropertyName] = (CustomThemeTokenControl)picker.Tag;
                tokenPanel.Children.Add(new Border
                {
                    Width = 208,
                    Background = (Brush)FindResource(ThemeTokens.PrimaryBackground),
                    BorderBrush = (Brush)FindResource(ThemeTokens.PrimaryAccent),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 8, 8),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = LocalizationService.Get(token.LabelKey),
                                FontWeight = FontWeights.SemiBold,
                                Foreground = (Brush)FindResource(ThemeTokens.PrimaryTextOnBackground),
                                Margin = new Thickness(0, 0, 0, 7)
                            },
                            controls
                        }
                    }
                });
            }

            CustomThemeTokenPanel.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8),
                Children =
                {
                    new TextBlock
                    {
                        Text = LocalizationService.Get(group.LabelKey),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource(ThemeTokens.PrimaryTextOnBackground),
                        Margin = new Thickness(0, 0, 0, 7)
                    },
                    tokenPanel
                }
            });
        }
    }

    private void PickCustomThemeColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CustomThemeTokenControl control })
            return;

        var picker = new ColorWheelWindow(control.Input.Text) { Owner = this };
        if (picker.ShowDialog() != true)
            return;

        control.Input.Text = picker.SelectedColor.ToString();
        UpdateColorPreview(control.Preview, control.Input.Text);
    }

    private void SaveTheme_Click(object sender, RoutedEventArgs e)
    {
        if (_editingThemeId is not Guid editingThemeId)
            return;

        var name = ThemeNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                LocalizationService.Get("Settings.Theme.NameRequired"),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var next = _editingPalette.Clone();
        foreach (var token in CustomThemeTokens)
        {
            var value = _customThemeTokenControls[token.PropertyName].Input.Text.Trim();
            if (!TryParseColor(value, out var color))
            {
                MessageBox.Show(
                    LocalizationService.Format("Settings.Theme.Custom.Invalid", LocalizationService.Get(token.LabelKey)),
                    LocalizationService.Get("App.Name"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CustomThemeProperties[token.PropertyName].SetValue(next, color.ToString());
        }

        _editingPalette = next;
        var existingTheme = _manager.Data.Settings.SavedThemes
            .FirstOrDefault(theme => theme.Id == editingThemeId);
        _manager.SaveSavedTheme(new SavedTheme
        {
            Id = editingThemeId,
            Name = name,
            IsBuiltIn = existingTheme?.IsBuiltIn == true,
            Palette = next
        });
        _manager.SelectSavedTheme(editingThemeId);
        RefreshThemeCards();
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var themeId = sender is Button { Tag: Guid id } ? id : _editingThemeId;
        if (themeId is not Guid idToDelete)
            return;

        if (_manager.Data.Settings.SavedThemes.Count <= 1)
        {
            MessageBox.Show(
                LocalizationService.Get("Settings.Theme.DeleteLast"),
                LocalizationService.Get("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var theme = _manager.Data.Settings.SavedThemes.FirstOrDefault(candidate => candidate.Id == idToDelete);
        if (theme?.IsBuiltIn == true)
            return;
        if (theme is null)
        {
            if (_editingThemeId == idToDelete)
            {
                _editingThemeId = null;
                ThemeEditor.Visibility = Visibility.Collapsed;
            }
            return;
        }

        var result = MessageBox.Show(
            LocalizationService.Format("Settings.Theme.DeleteConfirm", theme.Name),
            LocalizationService.Get("Settings.Theme.DeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        _manager.DeleteSavedTheme(idToDelete);
        if (_editingThemeId == idToDelete)
        {
            _editingThemeId = null;
            ThemeEditor.Visibility = Visibility.Collapsed;
        }
        RefreshThemeCards();
    }

    private static string GetThemeToken(CustomThemePalette palette, string propertyName) =>
        (string)CustomThemeProperties[propertyName].GetValue(palette)!;

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch (Exception)
        {
            color = default;
            return false;
        }
    }

    private static void UpdateColorPreview(Border preview, string value)
    {
        preview.Background = TryParseColor(value, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;
    }

    private void ShowPage(SettingsPage page)
    {
        GeneralPanel.Visibility = page == SettingsPage.General ? Visibility.Visible : Visibility.Collapsed;
        AutoClickPanel.Visibility = page == SettingsPage.AutoClick ? Visibility.Visible : Visibility.Collapsed;
        UserPanel.Visibility = page == SettingsPage.User ? Visibility.Visible : Visibility.Collapsed;
        CompanionPanel.Visibility = page == SettingsPage.Companion ? Visibility.Visible : Visibility.Collapsed;
        ThemePanel.Visibility = page == SettingsPage.Theme ? Visibility.Visible : Visibility.Collapsed;
    }
}
