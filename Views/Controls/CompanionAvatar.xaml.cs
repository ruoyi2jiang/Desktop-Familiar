using DesktopFamiliar.Models;

namespace DesktopFamiliar.Views.Controls;

public partial class CompanionAvatar : UserControl
{
    public CompanionAvatar() => InitializeComponent();

    public void SetCompanion(CompanionDefinition? pet) =>
        SetPicture(pet?.ProfilePicturePath, pet?.IdleDefaultAnimation?.GetPreviewPath());

    public void SetPicture(string? portrait, string? idleFramePath)
    {
        Picture.ImageSource = CompanionPictureLoader.Load(portrait, idleFramePath);
        Placeholder.Visibility = Picture.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;
    }
}
