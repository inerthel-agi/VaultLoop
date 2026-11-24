using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The tray menu: a status card over the actions. Every item paints itself from a caption
/// and keeps its original <c>Text</c> as the accessible name — that text is what tells a
/// screen reader whether a switch is on, now that the switch itself is only drawn.
/// </summary>
internal sealed class TrayMenu : ContextMenuStrip
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

    // Measures are authored at 96 DPI, where the body font is 18 px tall.
    private static readonly float DpiScale = Typography.MenuBody.Height / 18F;
    private static readonly int MenuWidth = Px(280);

    private readonly StatusCard _statusItem;
    private readonly MenuRow _openItem;
    private readonly MenuRow _hideItem;
    private readonly MenuRow _hudItem;
    private readonly MenuRow _startupItem;
    private bool _roundedByCompositor;

    internal TrayMenu(
        Action openWindow, Action hideWindow, Action toggleHud,
        Action toggleStartup, Action exit)
    {
        BackColor = Palette.MenuSurface;
        ForeColor = Palette.MenuText;
        Font = Typography.MenuBody;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Renderer = new TrayMenuRenderer();

        var title = new ToolStripLabel
        {
            Name = "TrayTitle",
            Text = "VaultLoop",
            AutoSize = false,
            Size = new Size(MenuWidth, Px(24)),
            Margin = new Padding(0, Px(6), 0, 0),
            Padding = new Padding(Px(4), 0, 0, Px(4)),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Typography.MenuCaption,
            ForeColor = Palette.MenuMuted
        };

        _statusItem = new StatusCard
        {
            Name = "TrayStatus",
            Text = "STATUS  //  UNKNOWN",
            AutoSize = false,
            Size = new Size(MenuWidth, Px(62)),
            Margin = new Padding(0, 0, 0, Px(6))
        };

        _openItem = CreateRow(
            "Open VaultLoop", "OPEN VAULTLOOP", "TrayOpen", MenuGlyph.Open, openWindow);
        _hideItem = CreateRow(
            "Hide to tray", "HIDE TO TRAY", "TrayHide", MenuGlyph.Hide, hideWindow);
        _hudItem = CreateRow("HUD", "HUD  //  ON", "TrayHud", MenuGlyph.Hud, toggleHud);
        _hudItem.SwitchOn = true;
        _startupItem = CreateRow(
            "Start with Windows", "START WITH WINDOWS  //  OFF", "TrayStartup",
            MenuGlyph.Startup, toggleStartup);
        _startupItem.SwitchOn = false;
        var exitItem = CreateRow(
            "Exit & restore", "EXIT && RESTORE", "TrayExit", MenuGlyph.Exit, exit);
        exitItem.Danger = true;
        exitItem.Margin = new Padding(0, 0, 0, Px(6));

        Items.AddRange([
            title,
            _statusItem,
            _openItem,
            _hideItem,
            _hudItem,
            _startupItem,
            new ToolStripSeparator
            {
                AutoSize = false,
                Size = new Size(MenuWidth, Px(13)),
                Margin = Padding.Empty
            },
            exitItem
        ]);
    }

    internal string StatusText => _statusItem.Text;
    internal string HudText => _hudItem.Text;
    internal string StartupText => _startupItem.Text;
    internal bool OpenEnabled => _openItem.Enabled;
    internal bool HideEnabled => _hideItem.Enabled;

    internal void AddDiagnostics(Action open) =>
        Items.Insert(Items.IndexOf(_hideItem) + 1,
            CreateRow("Services & diagnostics", "SERVICES && DIAGNOSTICS", "TrayDiagnostics",
                MenuGlyph.Diagnostics, open));

    internal void SetStatus(string status, Color color)
    {
        _statusItem.Status = status;
        _statusItem.StateColor = color;
        _statusItem.Text = $"STATUS  //  {status}";
        _statusItem.Invalidate();
    }

    /// <summary>The line under the status, e.g. what the firewall rule is doing to the link.</summary>
    internal void SetStatusDetail(string detail)
    {
        _statusItem.Detail = detail;
        _statusItem.Invalidate();
    }

    internal void SetHudEnabled(bool enabled) =>
        SetSwitch(_hudItem, enabled, enabled ? "HUD  //  ON" : "HUD  //  OFF");

    internal void SetStartupEnabled(bool enabled) =>
        SetSwitch(_startupItem, enabled,
            enabled ? "START WITH WINDOWS  //  ON" : "START WITH WINDOWS  //  OFF");

    /// <summary>Only the window action that applies is listed; the other one is hidden.</summary>
    internal void SetWindowVisible(bool visible)
    {
        _openItem.Enabled = _openItem.Available = !visible;
        _hideItem.Enabled = _hideItem.Available = visible;
    }

    /// <summary>
    /// The drop-down layout reserves a column for submenu arrows on the right; this menu has
    /// none, so the width is pinned.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(MenuWidth, base.GetPreferredSize(proposedSize).Height);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _roundedByCompositor = NativeMethods.TryRoundCorners(Handle, Palette.MenuLine);
    }

    /// <summary>The theme may have changed since the menu was last shown.</summary>
    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        base.OnOpening(e);
        BackColor = Palette.MenuSurface;
        if (IsHandleCreated)
        {
            _roundedByCompositor = NativeMethods.TryRoundCorners(Handle, Palette.MenuLine);
        }
    }

    private static void SetSwitch(MenuRow row, bool on, string accessibleText)
    {
        row.SwitchOn = on;
        row.Text = accessibleText;
        row.Invalidate();
    }

    private static MenuRow CreateRow(
        string caption, string text, string name, MenuGlyph glyph, Action action)
    {
        var item = new MenuRow
        {
            Name = name,
            Text = text,
            Caption = caption,
            Glyph = glyph,
            AutoSize = false,
            Size = new Size(MenuWidth, Px(36)),
            Margin = Padding.Empty
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static int Px(float value) => (int)Math.Round(value * DpiScale);

    /// <summary>
    /// Where an item draws, in its own coordinates: the menu width minus an 8-unit gutter on
    /// each side. The drop-down layout decides each item's X by its own rules (labels are
    /// indented, menu items are not), so every item spans the menu and positions itself here.
    /// </summary>
    private static RectangleF ContentBounds(ToolStripItem item) =>
        new(Px(8) - item.Bounds.X, 0, MenuWidth - 2 * Px(8), item.Height);

    private sealed class StatusCard : ToolStripLabel
    {
        internal string Status { get; set; } = "UNKNOWN";
        internal string Detail { get; set; } = "";
        internal Color StateColor { get; set; } = Palette.Yellow;

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var surface = new SolidBrush(Palette.MenuSurface))
            {
                graphics.FillRectangle(surface, 0, 0, Width, Height);
            }

            var content = ContentBounds(this);
            var fill = Palette.Mix(Palette.MenuSurface, StateColor, 0.10);
            using (var card = MenuGlyphs.RoundedRectangle(
                       new RectangleF(content.X + 0.5F, 0.5F, content.Width - 1F, Height - 1F),
                       Px(6)))
            using (var fillBrush = new SolidBrush(fill))
            using (var edge = new Pen(Palette.Mix(Palette.MenuSurface, StateColor, 0.34)))
            {
                graphics.FillPath(fillBrush, card);
                graphics.DrawPath(edge, card);
            }

            var orb = Px(36);
            var orbTop = (Height - orb) / 2F;
            using (var orbBrush = new SolidBrush(Palette.Mix(fill, StateColor, 0.18)))
            {
                graphics.FillEllipse(orbBrush, content.X + Px(12), orbTop, orb, orb);
            }
            MenuGlyphs.DrawCloud(graphics, MenuGlyphs.MarkFor(Status),
                new PointF(
                    content.X + Px(12) + (orb - 20F * DpiScale) / 2F,
                    orbTop + (orb - 20F * DpiScale) / 2F),
                DpiScale, StateColor);

            var left = (int)content.X + Px(60);
            var width = (int)content.Right - left - Px(10);
            var title = $"No-save {Status.ToLowerInvariant()}";
            var titleHeight = Typography.MenuTitle.Height;
            if (Detail.Length == 0)
            {
                TextRenderer.DrawText(graphics, title, Typography.MenuTitle,
                    new Rectangle(left, 0, width, Height), Color.White, fill, TextFlags);
                return;
            }

            var detailHeight = Typography.MenuDetail.Height;
            var top = (Height - titleHeight - detailHeight) / 2;
            TextRenderer.DrawText(graphics, title, Typography.MenuTitle,
                new Rectangle(left, top, width, titleHeight), Color.White, fill, TextFlags);
            TextRenderer.DrawText(graphics, DisplayText.Sentence(Detail), Typography.MenuDetail,
                new Rectangle(left, top + titleHeight, width, detailHeight),
                Palette.Mix(fill, Color.White, 0.72), fill, TextFlags);
        }
    }

    private sealed class MenuRow : ToolStripMenuItem
    {
        internal string Caption { get; set; } = "";
        internal MenuGlyph Glyph { get; set; }
        internal bool Danger { get; set; }

        /// <summary>Null for a plain action; otherwise the position of the drawn switch.</summary>
        internal bool? SwitchOn { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var surface = new SolidBrush(Palette.MenuSurface))
            {
                graphics.FillRectangle(surface, 0, 0, Width, Height);
            }

            var content = ContentBounds(this);
            var back = Palette.MenuSurface;
            if (Selected)
            {
                back = Palette.MenuHover;
                using var highlight = MenuGlyphs.RoundedRectangle(content, Px(6));
                using var hover = new SolidBrush(back);
                graphics.FillPath(hover, highlight);
            }

            MenuGlyphs.Draw(graphics, Glyph,
                new PointF(content.X + Px(10), (Height - 16F * DpiScale) / 2F), DpiScale,
                Danger ? Palette.MenuDanger : Palette.MenuMuted);

            var left = (int)content.X + Px(38);
            TextRenderer.DrawText(graphics, Caption, Font,
                new Rectangle(left, 0, (int)content.Right - left - Px(52), Height),
                Danger ? Palette.MenuDanger : Palette.MenuText, back, TextFlags);

            if (SwitchOn is not bool on)
            {
                return;
            }

            var track = new RectangleF(
                content.Right - Px(10) - Px(34), (Height - Px(18)) / 2F, Px(34), Px(18));
            MenuGlyphs.DrawSwitch(graphics, track, on ? 1F : 0F, Palette.SwitchOff);
        }
    }

    private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        internal TrayMenuRenderer() : base(new TrayColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs eventArgs)
        {
            // Windows 11 draws the rounded border itself; a square one on top would show
            // as clipped corners.
            if (eventArgs.ToolStrip is TrayMenu { _roundedByCompositor: true })
            {
                return;
            }

            using var pen = new Pen(Palette.MenuLine);
            var bounds = eventArgs.AffectedBounds;
            eventArgs.Graphics.DrawRectangle(
                pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs eventArgs)
        {
            using var pen = new Pen(Palette.MenuLine);
            var content = ContentBounds(eventArgs.Item);
            var middle = eventArgs.Item.Height / 2;
            eventArgs.Graphics.DrawLine(
                pen, content.X + Px(2), middle, content.Right - Px(2), middle);
        }
    }

    private sealed class TrayColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Palette.MenuSurface;
        public override Color ImageMarginGradientBegin => Palette.MenuSurface;
        public override Color ImageMarginGradientMiddle => Palette.MenuSurface;
        public override Color ImageMarginGradientEnd => Palette.MenuSurface;
        public override Color MenuBorder => Palette.MenuLine;
    }
}
