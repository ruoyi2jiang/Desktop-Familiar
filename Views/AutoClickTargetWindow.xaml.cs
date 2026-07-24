using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;

namespace DesktopFamiliar.Views;

public sealed class AutoClickTargetSelectedEventArgs(DrawingPoint target) : EventArgs
{
    public DrawingPoint Target { get; } = target;
}

public partial class AutoClickTargetWindow : Window
{
    private bool _selected;

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
        _selected = true;
        TargetSelected?.Invoke(this, new AutoClickTargetSelectedEventArgs(Forms.Cursor.Position));
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }
}
