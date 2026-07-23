using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace DesktopFamiliar.Views.Controls;

public readonly record struct TrayMenuColors(
    Drawing.Color Background,
    Drawing.Color Foreground,
    Drawing.Color DisabledForeground,
    Drawing.Color HoverBackground,
    Drawing.Color HoverForeground,
    Drawing.Color PressedBackground,
    Drawing.Color Border);

/// <summary>
/// Native ToolStrip submenu owners remain pressed/selected while their child
/// menu is open. Resolve their visual state from the pointer and child-menu
/// visibility so they retain hover without inheriting the darker native state.
/// </summary>
public enum TrayMenuItemHighlightState
{
    None,
    Hover,
    Pressed
}

public static class TrayMenuItemHighlightPolicy
{
    public static TrayMenuItemHighlightState Resolve(
        bool hasSubmenu,
        bool isPointerOver,
        bool isSubmenuOpen,
        bool isSelected,
        bool isPressed)
    {
        if (hasSubmenu)
            return isPointerOver || isSubmenuOpen
                ? TrayMenuItemHighlightState.Hover
                : TrayMenuItemHighlightState.None;

        if (isPressed)
            return TrayMenuItemHighlightState.Pressed;

        return isPointerOver || isSelected
            ? TrayMenuItemHighlightState.Hover
            : TrayMenuItemHighlightState.None;
    }
}

public static class TraySubmenuPointerPolicy
{
    public static bool ShouldKeepOpen(
        bool ownerPointerOver,
        bool dropDownVisible,
        Drawing.Rectangle dropDownBounds,
        Drawing.Point pointerPosition) =>
        dropDownVisible
        && (ownerPointerOver || dropDownBounds.Contains(pointerPosition));
}

/// <summary>
/// Native notification-area menu with the same layout metrics as the WPF
/// overlay menu. It keeps Windows-owned focus and outside-click dismissal.
/// </summary>
public sealed class ThemedTrayContextMenu : Forms.ContextMenuStrip
{
    // Matches the WPF viewport menu's 196 DIP item plus its border/padding.
    public const int ContentWidth = 206;
    public const int ItemHeight = 36;
    public const int SeparatorHeight = 9;
    public const int ItemInset = 4;
    public const int ContentInset = 30;
    public const int TrailingInset = 30;

    private const int OuterCornerRadius = 8;
    private readonly Drawing.Font _menuFont;
    private TrayMenuColors _colors;

    public ThemedTrayContextMenu()
    {
        AutoClose = true;
        AutoSize = true;
        DropShadowEnabled = true;
        ShowItemToolTips = true;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Padding = new Forms.Padding(4);
        _menuFont = new Drawing.Font("Segoe UI Variable Text", 10.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        Font = _menuFont;
    }

    public void ApplyTheme(TrayMenuColors colors)
    {
        _colors = colors;
        BackColor = colors.Background;
        ForeColor = colors.Foreground;
        Renderer = new Windows11TrayMenuRenderer(colors);
        ApplyItemTheme(Items);
        Invalidate(true);
    }

    public void ApplyItemTheme(Forms.ToolStripItemCollection items)
    {
        foreach (Forms.ToolStripItem item in items)
        {
            item.BackColor = _colors.Background;
            item.ForeColor = _colors.Foreground;
            if (item is not Forms.ToolStripMenuItem menuItem || !menuItem.HasDropDownItems)
                continue;

            var dropDown = menuItem.DropDown;
            dropDown.BackColor = _colors.Background;
            dropDown.ForeColor = _colors.Foreground;
            dropDown.Padding = new Forms.Padding(4);
            dropDown.Renderer = Renderer;
            if (dropDown is Forms.ToolStripDropDownMenu dropDownMenu)
            {
                dropDownMenu.ShowImageMargin = false;
                dropDownMenu.ShowCheckMargin = false;
            }

            dropDown.SizeChanged += DropDown_SizeChanged;
            dropDown.Opened += DropDown_Opened;
            ApplyItemTheme(menuItem.DropDownItems);
        }
    }

    internal void CloseOpenRootSubmenusExcept(Forms.ToolStripItem? hoveredItem)
    {
        foreach (var sibling in Items.OfType<Forms.ToolStripMenuItem>())
        {
            if (!ReferenceEquals(sibling, hoveredItem) && sibling.DropDown.Visible)
            {
                sibling.HideDropDown();
                Invalidate(sibling.Bounds);
            }
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _menuFont.Dispose();
        base.Dispose(disposing);
    }

    private static void DropDown_SizeChanged(object? sender, EventArgs e)
    {
        if (sender is Forms.ToolStripDropDown dropDown)
            ApplyRoundedRegion(dropDown);
    }

    private static void DropDown_Opened(object? sender, EventArgs e)
    {
        if (sender is Forms.ToolStripDropDown dropDown)
            ApplyRoundedRegion(dropDown);
    }

    private static void ApplyRoundedRegion(Forms.ToolStrip toolStrip)
    {
        if (toolStrip.Width <= 0 || toolStrip.Height <= 0)
            return;

        using var path = DrawingExtensions.CreateRoundedRectangle(
            new Drawing.Rectangle(0, 0, toolStrip.Width, toolStrip.Height),
            OuterCornerRadius);
        var previous = toolStrip.Region;
        toolStrip.Region = new Drawing.Region(path);
        previous?.Dispose();
    }

    private sealed class Windows11TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private const int ItemCornerRadius = 5;
        private readonly TrayMenuColors _colors;

        public Windows11TrayMenuRenderer(TrayMenuColors colors)
            : base(new NeutralColorTable(colors.Background))
        {
            _colors = colors;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var path = DrawingExtensions.CreateRoundedRectangle(
                new Drawing.Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height),
                OuterCornerRadius);
            using var brush = new Drawing.SolidBrush(_colors.Background);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var path = DrawingExtensions.CreateRoundedRectangle(
                new Drawing.Rectangle(0, 0, Math.Max(0, e.ToolStrip.Width - 1), Math.Max(0, e.ToolStrip.Height - 1)),
                OuterCornerRadius);
            using var pen = new Drawing.Pen(_colors.Border, 1f);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var state = ResolveHighlightState(e.Item);
            var color = state switch
            {
                TrayMenuItemHighlightState.Hover => _colors.HoverBackground,
                TrayMenuItemHighlightState.Pressed => _colors.PressedBackground,
                _ => _colors.Background
            };
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var path = DrawingExtensions.CreateRoundedRectangle(
                new Drawing.Rectangle(
                    ItemInset,
                    0,
                    Math.Max(0, e.Item.Width - (ItemInset * 2)),
                    e.Item.Height),
                ItemCornerRadius);
            using var brush = new Drawing.SolidBrush(color);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is not Forms.ToolStripMenuItem menuItem)
            {
                base.OnRenderItemText(e);
                return;
            }

            var highlight = ResolveHighlightState(menuItem) is not TrayMenuItemHighlightState.None;
            var color = !menuItem.Enabled
                ? _colors.DisabledForeground
                : highlight ? _colors.HoverForeground : _colors.Foreground;
            if (menuItem.Checked)
            {
                Forms.TextRenderer.DrawText(
                    e.Graphics,
                    "\u2713",
                    e.TextFont,
                    new Drawing.Rectangle(ItemInset + 12, 0, 18, menuItem.Height),
                    color,
                    Forms.TextFormatFlags.HorizontalCenter
                    | Forms.TextFormatFlags.VerticalCenter
                    | Forms.TextFormatFlags.NoPadding);
            }

            Forms.TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                new Drawing.Rectangle(
                    ItemInset + ContentInset,
                    0,
                    Math.Max(
                        0,
                        menuItem.Width - (ItemInset * 2) - ContentInset - TrailingInset),
                    menuItem.Height),
                color,
                Forms.TextFormatFlags.Left
                | Forms.TextFormatFlags.VerticalCenter
                | Forms.TextFormatFlags.EndEllipsis
                | Forms.TextFormatFlags.NoPadding);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            var highlight = ResolveHighlightState(e.Item) is not TrayMenuItemHighlightState.None;
            e.ArrowColor = highlight
                ? _colors.HoverForeground
                : _colors.Foreground;
            base.OnRenderArrow(e);
        }

        private static TrayMenuItemHighlightState ResolveHighlightState(Forms.ToolStripItem? item)
        {
            if (item is not Forms.ToolStripMenuItem menuItem)
                return TrayMenuItemHighlightState.None;

            return TrayMenuItemHighlightPolicy.Resolve(
                menuItem.HasDropDownItems,
                menuItem is ThemedTrayMenuItem { IsPointerOver: true },
                menuItem.DropDown.Visible,
                menuItem.Selected,
                menuItem.Pressed);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Drawing.Pen(_colors.Border, 1f);
            e.Graphics.DrawLine(
                pen,
                ItemInset + 8,
                y,
                Math.Max(ItemInset + 8, e.Item.Width - ItemInset - 8),
                y);
        }
    }

    private sealed class NeutralColorTable(Drawing.Color background) : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => background;
        public override Drawing.Color ImageMarginGradientBegin => background;
        public override Drawing.Color ImageMarginGradientMiddle => background;
        public override Drawing.Color ImageMarginGradientEnd => background;
        public override Drawing.Color ToolStripBorder => background;
    }
}

public sealed class ThemedTrayMenuItem : Forms.ToolStripMenuItem
{
    private readonly Forms.Timer _submenuLeaveTimer;

    public ThemedTrayMenuItem(string text, bool enabled = true)
        : base(text)
    {
        Enabled = enabled;
        AutoSize = true;
        CheckOnClick = false;
        Margin = Forms.Padding.Empty;
        Padding = new Forms.Padding(0);
        _submenuLeaveTimer = new Forms.Timer { Interval = 120 };
        _submenuLeaveTimer.Tick += SubmenuLeaveTimer_Tick;
    }

    internal bool IsPointerOver { get; private set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _submenuLeaveTimer.Stop();
        IsPointerOver = true;
        if (Owner is ThemedTrayContextMenu rootMenu)
            rootMenu.CloseOpenRootSubmenusExcept(this);
        Owner?.Invalidate(Bounds);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        IsPointerOver = false;
        Owner?.Invalidate(Bounds);
        base.OnMouseLeave(e);
        if (HasDropDownItems && DropDown.Visible)
            _submenuLeaveTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _submenuLeaveTimer.Stop();
            _submenuLeaveTimer.Tick -= SubmenuLeaveTimer_Tick;
            _submenuLeaveTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void SubmenuLeaveTimer_Tick(object? sender, EventArgs e)
    {
        if (TraySubmenuPointerPolicy.ShouldKeepOpen(
                IsPointerOver,
                DropDown.Visible,
                DropDown.Bounds,
                Forms.Cursor.Position))
        {
            return;
        }

        _submenuLeaveTimer.Stop();
        if (DropDown.Visible)
        {
            HideDropDown();
            Owner?.Invalidate(Bounds);
        }
    }

    public override Drawing.Size GetPreferredSize(Drawing.Size constrainingSize)
        => new(
            ThemedTrayContextMenu.ContentWidth,
            ThemedTrayContextMenu.ItemHeight);
}

public sealed class ThemedTraySeparator : Forms.ToolStripSeparator
{
    public ThemedTraySeparator()
    {
        AutoSize = false;
        Size = new Drawing.Size(
            ThemedTrayContextMenu.ContentWidth,
            ThemedTrayContextMenu.SeparatorHeight);
        Margin = Forms.Padding.Empty;
        Padding = Forms.Padding.Empty;
    }
}
