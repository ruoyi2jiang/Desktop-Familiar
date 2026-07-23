using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DesktopFamiliar.Services;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DesktopFamiliar.Views;

public partial class ColorWheelWindow : Window
{
    private const int WheelSize = 300;
    private double _hue;
    private double _saturation;
    private bool _dragging;

    public Color SelectedColor { get; private set; } = Colors.White;

    public ColorWheelWindow(string initialColor)
    {
        InitializeComponent();
        try
        {
            SelectedColor = (Color)ColorConverter.ConvertFromString(initialColor);
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The saved font color was invalid; white will be used.", exception);
        }
        RgbToHsv(SelectedColor, out _hue, out _saturation, out var value);
        BrightnessSlider.Value = Math.Max(0.1, value);
        Loaded += (_, _) =>
        {
            RenderWheel();
            PositionMarker();
            UpdatePreview();
        };
    }

    private void RenderWheel()
    {
        var pixels = new byte[WheelSize * WheelSize * 4];
        var radius = WheelSize / 2d - 3;
        var center = WheelSize / 2d;
        var value = BrightnessSlider.Value;
        for (var y = 0; y < WheelSize; y++)
            for (var x = 0; x < WheelSize; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var offset = (y * WheelSize + x) * 4;
                if (distance > radius)
                    continue;
                var saturation = Math.Clamp(distance / radius, 0, 1);
                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var color = HsvToRgb(hue, saturation, value);
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        var bitmap = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), pixels, WheelSize * 4, 0);
        WheelImage.Source = bitmap;
    }

    private void Wheel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        Mouse.Capture((IInputElement)sender);
        SelectAt(e.GetPosition((IInputElement)sender));
    }

    private void Wheel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            if (_dragging)
            {
                _dragging = false;
                Mouse.Capture(null);
            }
            return;
        }
        SelectAt(e.GetPosition((IInputElement)sender));
    }

    private void SelectAt(Point point)
    {
        var center = WheelSize / 2d;
        var radius = WheelSize / 2d - 3;
        var dx = point.X - center;
        var dy = point.Y - center;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > radius)
        {
            var scale = radius / distance;
            dx *= scale;
            dy *= scale;
            distance = radius;
        }
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Clamp(distance / radius, 0, 1);
        PositionMarker();
        UpdatePreview();
    }

    private void PositionMarker()
    {
        var radius = (WheelSize / 2d - 3) * _saturation;
        var radians = _hue * Math.PI / 180;
        Canvas.SetLeft(SelectionMarker, WheelSize / 2d + Math.Cos(radians) * radius - SelectionMarker.Width / 2);
        Canvas.SetTop(SelectionMarker, WheelSize / 2d + Math.Sin(radians) * radius - SelectionMarker.Height / 2);
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;
        RenderWheel();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        SelectedColor = HsvToRgb(_hue, _saturation, BrightnessSlider.Value);
        SelectedPreview.Background = new SolidColorBrush(SelectedColor);
        HexValue.Text = SelectedColor.ToString();
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }
}
