using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;

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
        SummaryText.Text = LocalizationService.Format(
            "Library.CompanionSummary",
            companion.Animations.Count,
            companion.IdleRandomAnimationIds.Count);
        SelectButton.Content = LocalizationService.Get("Common.Select");
        ConfigureButton.Content = LocalizationService.Get("Common.Configure");
        ExportButton.Content = LocalizationService.Get("CompanionBundle.Export");
        DeleteButton.Content = LocalizationService.Get("Common.Delete");
        PreviewBorder.ToolTip = LocalizationService.Get("Library.PreviewIdleDefault");
        SelectedIndicatorHost.ToolTip = LocalizationService.Get("Common.Selected");
        SelectButton.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        SelectedIndicatorHost.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        PreviewContent.Content = CreatePreview(companion);
    }

    public CompanionDefinition Companion { get; }

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

    private static FrameworkElement CreatePreview(CompanionDefinition companion)
    {
        var preview = LoadPreview(GetIdleDefaultPreviewPath(companion));
        if (preview is not null)
        {
            return new Image
            {
                Source = preview,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
        }

        return new TextBlock
        {
            Text = LocalizationService.Get("Library.PreviewMissing"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static string? GetIdleDefaultPreviewPath(CompanionDefinition companion)
    {
        var scene = companion.IdleDefaultAnimation;
        return scene?.PngFrames.FirstOrDefault()?.FilePath ?? scene?.AnimationPath;
    }

    private static ImageSource? LoadPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;
            var firstFrame = BitmapFrame.Create(decoder.Frames[0]);
            firstFrame.Freeze();
            return firstFrame;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"The idle-default preview '{path}' could not be loaded.", exception);
            return null;
        }
    }
}
