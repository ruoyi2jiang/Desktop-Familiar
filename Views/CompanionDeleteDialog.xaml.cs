using System.Windows;
using System.Windows.Input;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Views;

public enum CompanionDeleteChoice
{
    Cancel,
    KeepResources,
    DeleteResources
}

public partial class CompanionDeleteDialog : Window
{
    public CompanionDeleteChoice Choice { get; private set; }

    public CompanionDeleteDialog(string companionName)
    {
        InitializeComponent();
        MessageText.Text = LocalizationService.Format("Library.Delete.Message", companionName);
    }

    private void KeepResources_Click(object sender, RoutedEventArgs e)
    {
        Choice = CompanionDeleteChoice.KeepResources;
        DialogResult = true;
    }

    private void DeleteResources_Click(object sender, RoutedEventArgs e)
    {
        Choice = CompanionDeleteChoice.DeleteResources;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = CompanionDeleteChoice.Cancel;
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Choice = CompanionDeleteChoice.Cancel;
        DialogResult = false;
        e.Handled = true;
    }
}
