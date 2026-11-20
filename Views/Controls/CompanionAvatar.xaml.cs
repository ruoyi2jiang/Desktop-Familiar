using DesktopFamiliar.Models;

namespace DesktopFamiliar.Views.Controls;

public partial class CompanionAvatar : UserControl
{
    public CompanionAvatar() => InitializeComponent();

    public void SetPicture(CompanionPortrait? portrait, Func<System.Windows.Media.Imaging.BitmapSource>? captureIdle = null)
    {
        Picture.ImageSource = CompanionPictureLoader.Load(portrait, captureIdle);
        Placeholder.Visibility = Picture.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;
    }
}
