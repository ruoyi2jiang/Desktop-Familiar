using System.Windows.Automation;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Views.Controls;

public partial class ChatHistoryPetRow : UserControl
{
    public ChatHistoryPetRow(string name, string? portrait, string? idleFrame, string? sizeText, bool dividerBefore)
    {
        InitializeComponent();
        PetDivider.Visibility = dividerBefore ? Visibility.Visible : Visibility.Collapsed;
        Avatar.SetPicture(portrait, idleFrame);
        PetName.Text = name;
        if (sizeText == null)
        {
            Avatar.Width = Avatar.Height = 36;
            PetName.FontSize = 14;
            PetName.FontWeight = FontWeights.Normal;
            RowBody.Margin = new Thickness(8, 8, 14, 8);
            HistorySize.Visibility = ClearButton.Visibility = Visibility.Collapsed;
            return;
        }
        HistorySize.Text = sizeText;
        AutomationProperties.SetHelpText(ClearButton, sizeText);
        var label = LocalizationService.Format("Chat.ClearPet", name);
        ClearButton.ToolTip = label;
        AutomationProperties.SetName(ClearButton, label);
    }
}
