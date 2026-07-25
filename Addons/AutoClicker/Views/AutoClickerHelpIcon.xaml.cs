using System.Windows;
using System.Windows.Controls;

namespace DesktopFamiliar.Addons.AutoClicker.Views;

/// <summary>Addon-owned themed help icon with an immediately available tooltip.</summary>
public partial class AutoClickerHelpIcon : UserControl
{
    public AutoClickerHelpIcon(string helpText)
    {
        InitializeComponent();
        HelpButton.ToolTip = helpText;
    }
}
