using System.Windows;
using System.Windows.Controls;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Views.Controls;

public partial class CompanionCard : UserControl
{
    public CompanionCard(CompanionDefinition companion, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(companion);
        InitializeComponent();
        Companion = companion;

        CardBorder.SetResourceReference(
            Border.BackgroundProperty,
            isCurrent ? ThemeTokens.PrimaryBackground : ThemeTokens.PageBackgroundHover);
        CardBorder.SetResourceReference(
            Border.BorderBrushProperty,
            isCurrent ? ThemeTokens.PageAccent : ThemeTokens.PageBackgroundHover);
        PreviewBorder.SetResourceReference(
            Border.BackgroundProperty,
            isCurrent ? ThemeTokens.PageBackgroundHover : ThemeTokens.PageBackground);

        NameText.Text = companion.Name;
        SummaryText.Text = FormatSummary(companion);
        SelectButton.Content = LocalizationService.Get("Common.Select");
        ConfigureButton.Content = LocalizationService.Get("Common.Configure");
        ExportButton.Content = LocalizationService.Get("CompanionBundle.Export");
        DeleteButton.Content = LocalizationService.Get("Common.Delete");

        SelectedIndicatorHost.ToolTip = LocalizationService.Get("Common.Selected");
        SelectButton.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        SelectedIndicatorHost.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Source = CompanionPictureLoader.Load(CompanionPortrait.From(companion));
        PreviewPlaceholder.Visibility = PreviewImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public CompanionDefinition Companion { get; }

    internal static string FormatSummary(CompanionDefinition companion)
    {
        var appearance = companion.ActiveScenes;
        var dimension = LocalizationService.Get(appearance is ModelAppearance ? "Library.Dimension3D" : "Library.Dimension2D");
        var count = ModelAnimationCatalog.VisibleScenes(appearance, showParts: false).Count();
        return LocalizationService.Format(count == 1 ? "Library.AnimationCount.One" : "Library.AnimationCount.Other", dimension, count);
    }

    public event EventHandler? SelectRequested;
    public event EventHandler? ConfigureRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? DeleteRequested;

    private void Select_Click(object sender, RoutedEventArgs e) =>
        SelectRequested?.Invoke(this, EventArgs.Empty);

    private void Configure_Click(object sender, RoutedEventArgs e) =>
        ConfigureRequested?.Invoke(this, EventArgs.Empty);

    private void Export_Click(object sender, RoutedEventArgs e) =>
        ExportRequested?.Invoke(this, EventArgs.Empty);

    private void Delete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);
}
