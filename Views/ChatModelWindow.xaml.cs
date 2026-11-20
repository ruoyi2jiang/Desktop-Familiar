using System.Diagnostics;
using System.Net.Http;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Views;

public partial class ChatModelWindow : Window
{
    private readonly ChatModelStore _models;
    private CancellationTokenSource? _download;
    public string? InstalledId { get; private set; }

    public ChatModelWindow(ChatModelStore models, string? file = null, string? folder = null)
    {
        InitializeComponent();
        _models = models;
        if (file == null && folder == null)
        {
            Choices.DisplayMemberPath = nameof(ChatModel.Name);
            Choices.ItemsSource = ChatModelStore.Catalogue;
            Choices.SelectedIndex = 0;
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
        if (model != null)
        {
            Requirements.Text = _models.DescribeRequirements(model, forDownload: !available);
            Requirements.Visibility = Visibility.Visible;
            DownloadDetails.Text = available ? LocalizationService.Format("Chat.AlreadyAvailable", model.Name)
                : LocalizationService.Format("Chat.DownloadDetails", model.Name, model.DownloadMb);
            Install.Content = LocalizationService.Get(available ? "Chat.UseFile" : "Chat.Download");
        }
        Install.IsEnabled = ChatModelStore.Supported && Choices.SelectedItem != null;
        Status.Text = !ChatModelStore.Supported ? LocalizationService.Get("Chat.Unsupported")
            : available ? LocalizationService.Get("Chat.Installed") : "";
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
        using var operation = new CancellationTokenSource();
        _download = operation;
        Choices.IsEnabled = Install.IsEnabled = false;
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
                    await _models.InstallAsync(model.Id, progress, operation.Token);
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
        finally { _download = null; Choices.IsEnabled = Install.IsEnabled = true; }
        if (InstalledId != null || operation.IsCancellationRequested) Close();
    }
}
