using System.Windows;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Views;

public enum CompanionImportConflictChoice
{
    Cancel,
    ImportAsNew,
    Overwrite
}

public partial class CompanionImportConflictWindow : Window
{
    public CompanionImportConflictWindow(string companionName)
    {
        InitializeComponent();
        ConflictMessage.Text = LocalizationService.Format("CompanionBundle.Conflict.Message", companionName);
    }

    public CompanionImportConflictChoice Choice { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWith(CompanionImportConflictChoice.Cancel, false);

    private void ImportAsNew_Click(object sender, RoutedEventArgs e) => CloseWith(CompanionImportConflictChoice.ImportAsNew, true);

    private void Overwrite_Click(object sender, RoutedEventArgs e) => CloseWith(CompanionImportConflictChoice.Overwrite, true);

    private void CloseWith(CompanionImportConflictChoice choice, bool dialogResult)
    {
        Choice = choice;
        DialogResult = dialogResult;
    }
}
