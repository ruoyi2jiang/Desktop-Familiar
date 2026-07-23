using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Views;

public partial class CrashReportWindow : Window
{
    public CrashReportWindow()
    {
        InitializeComponent();
        LogPathText.Text = LocalizationService.Format("Crash.LogPath", AppLogger.LogFilePath);
        ReportText.Text = AppLogger.BuildCrashReport();
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ReportText.Text);
            CopyStatus.Text = LocalizationService.Get("Crash.CopySuccess");
        }
        catch (Exception exception)
        {
            AppLogger.Error("The crash report could not be copied to the clipboard.", exception);
            CopyStatus.Text = LocalizationService.Format("Crash.CopyFailed", exception.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
