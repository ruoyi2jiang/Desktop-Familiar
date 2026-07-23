using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Views;

public enum RecurringEventDeleteChoice
{
    Cancel,
    Occurrence,
    Series
}

public partial class RecurringEventDeleteDialog : Window
{
    public RecurringEventDeleteChoice Choice { get; private set; }

    public RecurringEventDeleteDialog(string eventTitle, DateTime occurrenceDate)
    {
        InitializeComponent();
        MessageText.Text = LocalizationService.Format(
            "Calendar.Delete.Recurring.Message",
            eventTitle,
            occurrenceDate.ToString("D", CultureInfo.CurrentCulture));
    }

    private void Occurrence_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecurringEventDeleteChoice.Occurrence;
        DialogResult = true;
    }

    private void Series_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecurringEventDeleteChoice.Series;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecurringEventDeleteChoice.Cancel;
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Choice = RecurringEventDeleteChoice.Cancel;
        DialogResult = false;
        e.Handled = true;
    }
}
