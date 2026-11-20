using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace DesktopFamiliar.Views.Controls;

internal sealed class SpriteCrossfade(Image current, Image outgoing)
{
    private bool _awaitingFirstFrame;
    private int _version;

    public void BeginScene(bool transition)
    {
        Clear();
        if (!transition || current.Source is not BitmapSource source) return;
        outgoing.Source = source;
        outgoing.Opacity = 1;
        _awaitingFirstFrame = true;
    }

    public void Present(BitmapSource? frame)
    {
        current.Source = frame;
        current.Visibility = frame is null ? Visibility.Collapsed : Visibility.Visible;
        if (!_awaitingFirstFrame || frame is null) return;
        _awaitingFirstFrame = false;
        var version = _version;
        var duration = new Duration(TimeSpan.FromMilliseconds(120));
        var fadeOut = new DoubleAnimation(1, 0, duration);
        fadeOut.Completed += (_, _) =>
        {
            if (_version == version) Clear();
        };
        current.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
        outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    public void Clear()
    {
        _version++;
        _awaitingFirstFrame = false;
        current.BeginAnimation(UIElement.OpacityProperty, null);
        outgoing.BeginAnimation(UIElement.OpacityProperty, null);
        outgoing.Source = null;
        outgoing.Opacity = 0;
    }
}
