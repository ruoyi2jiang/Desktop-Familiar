namespace DesktopFamiliar.Views;

public partial class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow(string title, string message, string action, string? option = null, bool optionSelected = false)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = action;
        Option.Visibility = option == null ? Visibility.Collapsed : Visibility.Visible;
        OptionLabel.Text = option;
        Option.IsChecked = option != null && optionSelected;
    }

    public bool OptionSelected => Option.IsChecked == true;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
