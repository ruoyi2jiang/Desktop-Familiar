using DesktopFamiliar.Models;

namespace DesktopFamiliar.Views.Controls;

public partial class CompanionAvatar : UserControl
{
    public CompanionAvatar() => InitializeComponent();

    public void SetPicture(CompanionPortrait? portrait)
    {
        Picture.ImageSource = CompanionPictureLoader.Load(portrait);
        Placeholder.Visibility = Picture.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;
    }
}
