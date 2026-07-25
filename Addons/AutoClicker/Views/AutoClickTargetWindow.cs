using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Runtime.InteropServices;
using DesktopFamiliar.AddonSdk;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DesktopFamiliar.Addons.AutoClicker.Views;

public sealed class AutoClickTargetSelectedEventArgs(DrawingRectangle targetArea) : EventArgs
{
    public DrawingRectangle TargetArea { get; } = targetArea;
}

/// <summary>Full-screen, addon-owned selection window for an auto-click target area.</summary>
public sealed class AutoClickTargetWindow : Window
{
    private readonly Canvas _selectionCanvas = new() { IsHitTestVisible = false };
    private readonly Rectangle _selectionRectangle = new() { Visibility = Visibility.Collapsed, StrokeThickness = 2, Opacity = 0.28 };
    private bool _selected;
    private bool _selecting;
    private DrawingPoint _selectionStart;

    public AutoClickTargetWindow(IAddonStringProvider strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = true;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _selectionRectangle.SetResourceReference(Shape.StrokeProperty, "PrimaryAccentBrush");
        _selectionRectangle.SetResourceReference(Shape.FillProperty, "PrimaryAccentBrush");
        _selectionCanvas.Children.Add(_selectionRectangle);

        var content = new Grid();
        content.SetResourceReference(Panel.BackgroundProperty, "OverlaySurfaceBrush");
        content.SetResourceReference(TextElement.ForegroundProperty, "TextOnOverlayBrush");
        content.Children.Add(_selectionCanvas);

        var message = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(28, 20, 28, 20),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        message.SetResourceReference(Border.BackgroundProperty, "OverlaySurfaceHoverBrush");
        message.SetResourceReference(Border.BorderBrushProperty, "OverlayBorderBrush");
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = strings.Get("AutoClick.Target.Title"),
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        text.Children.Add(new TextBlock
        {
            Text = strings.Get("AutoClick.Target.Description"),
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            TextAlignment = TextAlignment.Center
        });
        text.Children.Add(new TextBlock
        {
            Text = strings.Get("AutoClick.Target.Cancel"),
            Margin = new Thickness(0, 10, 0, 0),
            Opacity = 0.76,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        message.Child = text;
        content.Children.Add(message);
        Content = content;

        KeyDown += Window_KeyDown;
        MouseLeftButtonDown += Window_MouseLeftButtonDown;
        MouseMove += Window_MouseMove;
        MouseLeftButtonUp += Window_MouseLeftButtonUp;
        Closed += (_, _) =>
        {
            if (!_selected)
                Cancelled?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler<AutoClickTargetSelectedEventArgs>? TargetSelected;
    public event EventHandler? Cancelled;

    public void BeginSelection()
    {
        Show();
        Activate();
        Focus();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        _selecting = true;
        _selectionStart = GetCursorPosition();
        _selectionRectangle.Visibility = Visibility.Visible;
        UpdateSelectionRectangle(_selectionStart, _selectionStart);
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_selecting || e.LeftButton != MouseButtonState.Pressed)
            return;

        UpdateSelectionRectangle(_selectionStart, GetCursorPosition());
        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting)
            return;

        _selecting = false;
        if (Mouse.Captured == this)
            Mouse.Capture(null);

        _selected = true;
        var targetArea = CreateTargetArea(_selectionStart, GetCursorPosition());
        Hide();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            TargetSelected?.Invoke(this, new AutoClickTargetSelectedEventArgs(targetArea));
            Close();
        }));
        e.Handled = true;
    }

    private void UpdateSelectionRectangle(DrawingPoint start, DrawingPoint end)
    {
        var area = CreateTargetArea(start, end);
        var topLeft = PointFromScreen(new Point(area.Left, area.Top));
        var bottomRight = PointFromScreen(new Point(area.Right, area.Bottom));
        Canvas.SetLeft(_selectionRectangle, topLeft.X);
        Canvas.SetTop(_selectionRectangle, topLeft.Y);
        _selectionRectangle.Width = Math.Max(1, bottomRight.X - topLeft.X);
        _selectionRectangle.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
    }

    private static DrawingRectangle CreateTargetArea(DrawingPoint start, DrawingPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        return new DrawingRectangle(left, top, Math.Max(1, Math.Abs(end.X - start.X) + 1), Math.Max(1, Math.Abs(end.Y - start.Y) + 1));
    }

    private static DrawingPoint GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
            throw new InvalidOperationException("Windows could not read the current cursor position.");
        return new DrawingPoint(point.X, point.Y);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
