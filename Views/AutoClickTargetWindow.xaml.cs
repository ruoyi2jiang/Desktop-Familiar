using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DesktopFamiliar.Views;

public sealed class AutoClickTargetSelectedEventArgs(DrawingRectangle targetArea) : EventArgs
{
    public DrawingRectangle TargetArea { get; } = targetArea;
}

public partial class AutoClickTargetWindow : Window
{
    private bool _selected;
    private bool _selecting;
    private DrawingPoint _selectionStart;

    public AutoClickTargetWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
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
        _selectionStart = Forms.Cursor.Position;
        SelectionRectangle.Visibility = Visibility.Visible;
        UpdateSelectionRectangle(_selectionStart, _selectionStart);
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_selecting || e.LeftButton != MouseButtonState.Pressed)
            return;

        UpdateSelectionRectangle(_selectionStart, Forms.Cursor.Position);
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
        var targetArea = CreateTargetArea(_selectionStart, Forms.Cursor.Position);
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
        Canvas.SetLeft(SelectionRectangle, topLeft.X);
        Canvas.SetTop(SelectionRectangle, topLeft.Y);
        SelectionRectangle.Width = Math.Max(1, bottomRight.X - topLeft.X);
        SelectionRectangle.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
    }

    private static DrawingRectangle CreateTargetArea(DrawingPoint start, DrawingPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        return new DrawingRectangle(
            left,
            top,
            Math.Max(1, Math.Abs(end.X - start.X) + 1),
            Math.Max(1, Math.Abs(end.Y - start.Y) + 1));
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }
}
