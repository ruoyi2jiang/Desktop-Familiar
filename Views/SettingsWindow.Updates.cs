using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Updates;
using Microsoft.Win32;

namespace DesktopFamiliar.Views;

public partial class SettingsWindow
{
    private readonly CancellationTokenSource _updateLifetime = new();
    private readonly AppDeployment _updateDeployment = AppUpdateHandoff.GetDeployment();
    private AppRelease? _availableUpdate;
    private string? _stagedUpdate;
    private bool _updateBusy;

    private async void CheckAppUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateBusy || _stagedUpdate != null) return;
        _availableUpdate = null;
        SetUpdateBusy(true);
        AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.Checking");
        var token = _updateLifetime.Token;
        try
        {
            var release = await App.CurrentApp.Updates.FindLatestAsync(_updateDeployment, token);
            token.ThrowIfCancellationRequested();
            if (release == null)
                AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.None");
            else if (release.Version == App.CurrentVersion)
                AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.Current");
            else if (release.Version < App.CurrentVersion)
                AppUpdateStatus.Text = LocalizationService.Format("Settings.Updates.Ahead", release.Version);
            else
            {
                _availableUpdate = release;
                var status = release.Asset switch
                {
                    null => "Settings.Updates.NoAsset",
                    {} asset when asset.CanApplyTo(_updateDeployment) => "Settings.Updates.Available",
                    _ when _updateDeployment == AppDeployment.Installed => "Settings.Updates.PortableOnly",
                    _ when _updateDeployment == AppDeployment.Portable => "Settings.Updates.InstallerOnly",
                    _ => "Settings.Updates.Separate"
                };
                AppUpdateStatus.Text = LocalizationService.Format(status, release.Version);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLogger.Warning("Could not check for Desktop Familiar releases.", exception);
            AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.CheckFailed");
        }
        finally { SetUpdateBusy(false); }
    }

    private async void ApplyAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _availableUpdate?.Asset is not {} asset || !asset.CanApplyTo(_updateDeployment)) return;
        if (_stagedUpdate == null)
        {
            await DownloadAppUpdateAsync(null);
            return;
        }
        try
        {
            AppUpdateHandoff.Start(_stagedUpdate, asset, LocalizationService.Get("Settings.Updates.Title"),
                LocalizationService.Get("Settings.Updates.ApplyFailed"));
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not start the Desktop Familiar update helper.", exception);
            AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.ApplyFailed");
            return;
        }
        _stagedUpdate = null; // The helper owns the file once it starts.
        SetUpdateBusy(true);
        App.CurrentApp.ShutdownApplication();
    }

    private async void SaveAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _availableUpdate?.Asset is not {} asset) return;
        var dialog = new SaveFileDialog
        {
            FileName = asset.Name, DefaultExt = ".exe", Filter = LocalizationService.Get("Settings.Updates.FileFilter"),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.SamePath");
            return;
        }
        await DownloadAppUpdateAsync(dialog.FileName);
    }

    private async Task DownloadAppUpdateAsync(string? destination)
    {
        if (_availableUpdate is not { Asset: {} asset } release) return;
        SetUpdateBusy(true);
        var token = _updateLifetime.Token;
        string? downloaded = null;
        try
        {
            var directory = destination != null ? Path.GetDirectoryName(destination)!
                : asset.IsInstaller ? Path.GetTempPath() : Path.GetDirectoryName(Environment.ProcessPath!)!;
            void ShowProgress(int percent)
            {
                if (!token.IsCancellationRequested && _updateBusy)
                    AppUpdateStatus.Text = LocalizationService.Format("Settings.Updates.Downloading", release.Version, percent);
            }
            ShowProgress(0);
            downloaded = await App.CurrentApp.Updates.DownloadAsync(asset, directory, new Progress<int>(ShowProgress), token);
            token.ThrowIfCancellationRequested();
            if (destination == null)
            {
                _stagedUpdate = downloaded;
                AppUpdateStatus.Text = LocalizationService.Format("Settings.Updates.Ready", release.Version);
            }
            else
            {
                File.Move(downloaded, destination, overwrite: true);
                AppUpdateStatus.Text = LocalizationService.Format("Settings.Updates.Saved", release.Version, destination);
            }
            downloaded = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLogger.Warning("Could not download or save a Desktop Familiar update.", exception);
            AppUpdateStatus.Text = LocalizationService.Get("Settings.Updates.DownloadFailed");
        }
        finally
        {
            AppUpdateService.DiscardDownload(downloaded);
            SetUpdateBusy(false);
        }
    }

    private void SetUpdateBusy(bool busy)
    {
        _updateBusy = busy;
        var canDownload = _availableUpdate?.Asset != null;
        CheckAppUpdates.IsEnabled = !busy && _stagedUpdate == null;
        ApplyAppUpdate.Visibility = _availableUpdate?.Asset?.CanApplyTo(_updateDeployment) == true ? Visibility.Visible : Visibility.Collapsed;
        ApplyAppUpdate.IsEnabled = SaveAppUpdate.IsEnabled = !busy;
        ApplyAppUpdate.Content = LocalizationService.Get(_stagedUpdate == null ? "Settings.Updates.Download"
            : _availableUpdate!.Asset!.IsInstaller ? "Settings.Updates.StartSetup" : "Settings.Updates.Restart");
        SaveAppUpdate.Visibility = canDownload && _stagedUpdate == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseAppUpdates()
    {
        _updateLifetime.Cancel();
        _updateLifetime.Dispose();
        AppUpdateService.DiscardDownload(_stagedUpdate);
        _stagedUpdate = null;
    }
}
