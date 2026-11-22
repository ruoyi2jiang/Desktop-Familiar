using System.Diagnostics;
using System.Net.Http;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Views;

public partial class ChatModelWindow : Window
{
    private readonly ChatModelStore _models;
    private CancellationTokenSource? _download;
    private readonly ChatModelTestResults? _tests;
    public string? InstalledId { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var anchor = Owner ?? this;
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(anchor).Handle);
        MaxHeight = screen.WorkingArea.Height / VisualTreeHelper.GetDpi(anchor).DpiScaleY;
    }

    // Discovery and selection can add rows after Windows has centered the dialog.
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (!IsLoaded || !info.HeightChanged || info.NewSize.Height <= info.PreviousSize.Height) return;
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var bottom = screen.WorkingArea.Bottom / scale;
        if (Top + ActualHeight > bottom) Top = Math.Max(screen.WorkingArea.Top / scale, bottom - ActualHeight);
    }

    public ChatModelWindow(ChatModelStore models, string? file = null, string? folder = null) : this(models, file, folder, null) { }

    internal ChatModelWindow(ChatModelStore models, string? file, string? folder, ChatModelTestResults? tests)
    {
        InitializeComponent();
        _models = models;
        _tests = tests;
        if (file == null && folder == null)
        {
            Choices.DisplayMemberPath = nameof(ChatModel.Name);
            FilterHint.Visibility = Discover.Visibility = Visibility.Visible;
            RefreshCatalogue();
        }
        else
        {
            Title = Heading.Text = LocalizationService.Get("Chat.LocalModels");
            Terms.Visibility = Visibility.Collapsed;
            Install.Content = LocalizationService.Get("Chat.UseFile");
            Install.IsEnabled = false;
            DownloadDetails.Text = LocalizationService.Get("Chat.LinkDetails");
            if (!models.HasEngine) DownloadDetails.Text += " " + LocalizationService.Get("Chat.EngineNeeded");
            if (file != null)
            {
                Choices.ItemsSource = new[] { file };
                Choices.SelectedIndex = 0;
            }
            else Loaded += async (_, _) => await ScanAsync(folder!);
        }
        Closing += (_, e) => { if (_download != null) { e.Cancel = true; _download.Cancel(); } };
    }

    private void RefreshCatalogue()
    {
        var pc = _models.ReadDownloadPc();
        var previous = (Choices.SelectedItem as ChatModel)?.Id;
        var choices = _models.DownloadChoices(pc);
        Choices.ItemsSource = choices;
        Choices.SelectedItem = choices.FirstOrDefault(m => m.Id == previous) ?? choices.FirstOrDefault();
        Choices.Visibility = choices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Discover.IsEnabled = pc.Supported;
        Install.IsEnabled = choices.Count > 0;
        if (choices.Count == 0)
        {
            Terms.Visibility = DownloadDetails.Visibility = Visibility.Collapsed;
            Status.Text = LocalizationService.Get(!pc.Supported ? "Chat.Unsupported"
                : pc.FreeRam == null || pc.FreeDisk == null ? "Chat.Fit.CannotCheck" : "Chat.Fit.NoMatches");
        }
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (_download != null) return;
        using var operation = new CancellationTokenSource();
        _download = operation;
        Discover.IsEnabled = Install.IsEnabled = Choices.IsEnabled = false;
        DiscoveryStatus.Visibility = Visibility.Visible;
        DiscoveryStatus.Text = LocalizationService.Get("Chat.Hub.Searching");
        try
        {
            var result = await _models.DiscoverAsync(operation.Token);
            RefreshCatalogue();
            DiscoveryStatus.Text = LocalizationService.Get(result.Incomplete ? "Chat.Hub.Partial" : "Chat.Hub.Updated");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        finally { _download = null; Choices.IsEnabled = true; }
        if (operation.IsCancellationRequested) Close();
    }

    private async Task ScanAsync(string folder)
    {
        using var operation = new CancellationTokenSource();
        _download = operation;
        Status.Text = LocalizationService.Get("Chat.Scanning");
        try
        {
            var files = await ChatModelStore.ScanFolderAsync(folder, operation.Token);
            Choices.ItemsSource = files;
            if (files.Length > 0) Choices.SelectedIndex = 0;
            else Status.Text = LocalizationService.Get("Chat.ScanEmpty");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("Could not scan the selected chat model folder.", ex);
            Status.Text = LocalizationService.Get("Chat.ScanFailed");
        }
        finally { _download = null; }
        if (operation.IsCancellationRequested) Close();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var model = Choices.SelectedItem as ChatModel;
        var available = model != null && _models.AvailableIdFor(model) != null;
        Strengths.Visibility = ResourceGroup.Visibility = ModelSummary.Visibility =
            model == null ? Visibility.Collapsed : Visibility.Visible;
        if (model != null)
        {
            Terms.Visibility = DownloadDetails.Visibility = Visibility.Visible;
            TermsLabel.Text = LocalizationService.Get(model.PublisherRelease == null ? "Chat.Terms" : "Chat.Hub.Card");
            Strengths.Text = LocalizationService.Get(model.StrengthsKey);
            Requirements.Text = model.Requirements.Describe(forDownload: !available);
            ModelSummary.Text = ChatModelTestResults.Summary(model, _tests?.Get(model.Sha256, _models.GpuEnabled));
            DownloadDetails.Text = available ? LocalizationService.Format("Chat.AlreadyAvailable", model.Name)
                : LocalizationService.Format("Chat.ModelFileSize", FileSizeFormatter.Format(model.FileBytes))
                    + (_models.HasEngine ? "" : " " + LocalizationService.Get("Chat.EngineNeeded"));
            Install.Content = LocalizationService.Get(available ? "Chat.UseFile" : "Chat.Download");
        }
        Install.IsEnabled = ChatModelStore.Supported && Choices.SelectedItem != null;
        Status.Text = !ChatModelStore.Supported ? LocalizationService.Get("Chat.Unsupported") : "";
    }

    private void Terms_Click(object sender, RoutedEventArgs e)
    {
        if (Choices.SelectedItem is not ChatModel model) return;
        try { Process.Start(new ProcessStartInfo(model.TermsUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLogger.Warning("Could not open the model terms in a browser.", ex);
            Status.Text = model.TermsUrl;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var selected = Choices.SelectedItem;
        if (selected == null || _download != null) return;
        if (selected is ChatModel candidate && !_models.FitsPicker(candidate, _models.ReadDownloadPc()))
        {
            RefreshCatalogue();
            if (Choices.SelectedItem != null) Status.Text = LocalizationService.Get("Chat.Fit.Changed");
            return;
        }
        using var operation = new CancellationTokenSource();
        _download = operation;
        Choices.IsEnabled = Install.IsEnabled = Discover.IsEnabled = false;
        Status.Text = LocalizationService.Get(selected is string ? "Chat.CheckingFile" : "Chat.Downloading");
        long lastMb = -1;
        var progress = new Progress<long>(bytes =>
        {
            var mb = bytes / 1_000_000;
            if (mb == lastMb || _download != operation) return;
            lastMb = mb;
            Status.Text = LocalizationService.Format("Chat.Progress", mb);
        });
        try
        {
            if (selected is ChatModel model)
            {
                InstalledId = _models.AvailableIdFor(model);
                if (InstalledId == null)
                {
                    await _models.InstallAsync(model, progress, operation.Token);
                    InstalledId = model.Id;
                }
            }
            else InstalledId = await _models.LinkFileAsync((string)selected, progress, operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.Error("Chat model installation failed.", ex);
            Status.Text = LocalizationService.Get(selected is ChatModel ? "Chat.DownloadFailed"
                : ex is HttpRequestException && !_models.HasEngine ? "Chat.EngineFailed" : "Chat.LinkFailed");
        }
        finally { _download = null; Choices.IsEnabled = Install.IsEnabled = Discover.IsEnabled = true; }
        if (InstalledId != null || operation.IsCancellationRequested) Close();
    }
}
