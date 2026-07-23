using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace DesktopFamiliar.Views.Controls;

public readonly record struct TrayToggleColors(
    Drawing.Color Background,
    Drawing.Color Foreground,
    Drawing.Color TrackOff,
    Drawing.Color TrackOn,
    Drawing.Color TrackOnHover,
    Drawing.Color ThumbOff,
    Drawing.Color ThumbOn,
    Drawing.Color Border);

/// <summary>
/// A reusable native toggle row for notification-area context menus. The host
/// keeps the native menu lifecycle while the contained control provides a
/// genuine switch rather than presenting focus mode as a command button.
/// </summary>
public sealed class TrayToggleMenuItem : Forms.ToolStripControlHost
{
    // ToolStripDropDownMenu adds this native column allowance to a hosted
    // control's preferred width even when its image/check margins are hidden.
    private const int NativeHostedControlColumnAllowance = 36;

    private readonly ToggleControl _toggle;

    public TrayToggleMenuItem(
        string label,
        bool isChecked,
        TrayToggleColors colors)
        : this(new ToggleControl(label, isChecked, colors))
    {
    }

    private TrayToggleMenuItem(ToggleControl toggle)
        : base(toggle)
    {
        _toggle = toggle;
        AutoSize = false;
        Size = new Drawing.Size(HostedControlWidth, ThemedTrayContextMenu.ItemHeight);
        Margin = Forms.Padding.Empty;
        Padding = Forms.Padding.Empty;
        BackColor = toggle.BackColor;
        ForeColor = toggle.ForeColor;
        _toggle.ToggleRequested += ToggleRequested;
        _toggle.PointerEntered += TogglePointerEntered;
    }

    public bool IsChecked
    {
        get => _toggle.IsChecked;
        set => _toggle.IsChecked = value;
    }

    public event EventHandler? Toggled;

    protected override void OnOwnerChanged(EventArgs e)
    {
        base.OnOwnerChanged(e);
        if (Owner is not null)
            _toggle.Font = Owner.Font;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toggle.ToggleRequested -= ToggleRequested;
            _toggle.PointerEntered -= TogglePointerEntered;
        }
        base.Dispose(disposing);
    }

    public override Drawing.Size GetPreferredSize(Drawing.Size constrainingSize) =>
        new(HostedControlWidth, ThemedTrayContextMenu.ItemHeight);

    private static int HostedControlWidth =>
        ThemedTrayContextMenu.ContentWidth - NativeHostedControlColumnAllowance;

    private void TogglePointerEntered(object? sender, EventArgs e)
    {
        if (Owner is ThemedTrayContextMenu rootMenu)
            rootMenu.CloseOpenRootSubmenusExcept(this);
    }

    private void ToggleRequested(object? sender, EventArgs e)
    {
        Toggled?.Invoke(this, EventArgs.Empty);
        Owner?.Hide();
    }

    private sealed class ToggleControl : Forms.Control
    {
        // ContextMenuStrip places a ToolStripControlHost eight pixels farther
        // in than a normal menu row. Offset the custom drawing so the track
        // shares the standard row's 30 px content origin on screen.
        private const int HostLayoutInset = 8;
        private const int TrackWidth = 36;
        private const int TrackLabelGap = 6;

        private readonly string _label;
        private readonly TrayToggleColors _colors;
        private bool _isChecked;
        private bool _hovered;
        private bool _pressed;

        public ToggleControl(string label, bool isChecked, TrayToggleColors colors)
        {
            _label = label;
            _isChecked = isChecked;
            _colors = colors;
            Size = new Drawing.Size(HostedControlWidth, ThemedTrayContextMenu.ItemHeight);
            BackColor = colors.Background;
            ForeColor = colors.Foreground;
            Cursor = Forms.Cursors.Hand;
            TabStop = true;
            AccessibleName = label;
            AccessibleRole = Forms.AccessibleRole.CheckButton;
            SetStyle(
                Forms.ControlStyles.UserPaint
                | Forms.ControlStyles.AllPaintingInWmPaint
                | Forms.ControlStyles.OptimizedDoubleBuffer
                | Forms.ControlStyles.ResizeRedraw,
                true);
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;
                _isChecked = value;
                Invalidate();
            }
        }

        public event EventHandler? ToggleRequested;

        public event EventHandler? PointerEntered;

        protected override Forms.AccessibleObject CreateAccessibilityInstance() =>
            new ToggleAccessibleObject(this);

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            PointerEntered?.Invoke(this, EventArgs.Empty);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(Forms.MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            ToggleFromUser();
            base.OnClick(e);
        }

        protected override void OnKeyDown(Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Forms.Keys.Space)
            {
                ToggleFromUser();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;

            using (var rowBrush = new Drawing.SolidBrush(_colors.Background))
                e.Graphics.FillRoundedRectangle(rowBrush, ClientRectangle, 5);

            var textColor = _colors.Foreground;
            var trackLeft = ThemedTrayContextMenu.ItemInset
                + ThemedTrayContextMenu.ContentInset
                - HostLayoutInset;
            var labelLeft = trackLeft + TrackWidth + TrackLabelGap;
            var textBounds = new Drawing.Rectangle(labelLeft, 0, Math.Max(0, Width - labelLeft - 12), Height);
            Forms.TextRenderer.DrawText(
                e.Graphics,
                _label,
                Font,
                textBounds,
                textColor,
                Forms.TextFormatFlags.Left
                | Forms.TextFormatFlags.VerticalCenter
                | Forms.TextFormatFlags.EndEllipsis
                | Forms.TextFormatFlags.NoPadding);

            var trackBounds = new Drawing.Rectangle(trackLeft, (Height - 20) / 2, TrackWidth, 20);
            var trackColor = IsChecked
                ? (_hovered || _pressed ? _colors.TrackOnHover : _colors.TrackOn)
                : _colors.TrackOff;
            using (var trackBrush = new Drawing.SolidBrush(trackColor))
                e.Graphics.FillRoundedRectangle(trackBrush, trackBounds, 10);
            using (var borderPen = new Drawing.Pen(_colors.Border, 1f))
                e.Graphics.DrawRoundedRectangle(borderPen, trackBounds, 10);

            var thumbBounds = new Drawing.Rectangle(
                IsChecked ? trackBounds.Right - 18 : trackBounds.Left + 2,
                trackBounds.Top + 2,
                16,
                16);
            using var thumbBrush = new Drawing.SolidBrush(IsChecked ? _colors.ThumbOn : _colors.ThumbOff);
            e.Graphics.FillEllipse(thumbBrush, thumbBounds);
        }

        private void ToggleFromUser()
        {
            IsChecked = !IsChecked;
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private sealed class ToggleAccessibleObject(ToggleControl owner)
            : Forms.Control.ControlAccessibleObject(owner)
        {
            public override Forms.AccessibleStates State => base.State
                | (owner.IsChecked ? Forms.AccessibleStates.Checked : Forms.AccessibleStates.None);
        }
    }
}

internal static class DrawingExtensions
{
    public static void FillRoundedRectangle(
        this Drawing.Graphics graphics,
        Drawing.Brush brush,
        Drawing.Rectangle bounds,
        int radius)
    {
        using var path = CreateRoundedRectangle(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(
        this Drawing.Graphics graphics,
        Drawing.Pen pen,
        Drawing.Rectangle bounds,
        int radius)
    {
        using var path = CreateRoundedRectangle(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    internal static Drawing2D.GraphicsPath CreateRoundedRectangle(Drawing.Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var arc = new Drawing.Rectangle(bounds.Location, new Drawing.Size(diameter, diameter));
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
