using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The main window's button: a rounded fill that paints itself. <c>BackColor</c> is the
/// fill and <c>Text</c> is drawn in sentence case, so the theme controller and the
/// self-test keep addressing it like any other button.
/// </summary>
internal sealed class SoftButton : Button
{
    private readonly AnimatedFloat _hover;
    private readonly AnimatedFloat _press;
    private readonly AnimatedFloat _switch;
    private bool? _switchOn;

    internal SoftButton()
    {
        _hover = new AnimatedFloat(this, 0F, Invalidate);
        _press = new AnimatedFloat(this, 0F, Invalidate);
        _switch = new AnimatedFloat(this, 0F, Invalidate);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    internal int Radius { get; set; } = 6;

    /// <summary>Draws <c>Text</c> as it is: a readout such as a captured key combination.</summary>
    internal bool Verbatim { get; set; }

    /// <summary>
    /// When set, the button is drawn as a switch in that position instead of as a labelled
    /// fill; <c>BackColor</c> is then the off track. <c>Text</c> still names the state for a
    /// screen reader.
    /// </summary>
    internal bool? SwitchOn
    {
        get => _switchOn;
        set
        {
            // The first position is placed, not animated: it is the state the window opens in.
            var moving = _switchOn is not null && value is not null && value != _switchOn;
            _switchOn = value;
            var target = value == true ? 1F : 0F;
            if (moving)
            {
                _switch.AnimateTo(target, Motion.Standard);
            }
            else
            {
                _switch.Snap(target);
            }
        }
    }

    /// <summary>Hover fill; a slightly stronger shade of the fill when left unset.</summary>
    internal Color? HoverBackColor { get; set; }
    internal Color? HoverForeColor { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover.AnimateTo(1F, Motion.Quick);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover.AnimateTo(0F, Motion.Standard);
        _press.AnimateTo(0F, Motion.Quick);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _press.AnimateTo(1F, Motion.Quick / 2);
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _press.AnimateTo(0F, Motion.Standard);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        // A disabled button gets no mouse-leave: without this, one disabled under the
        // pointer came back looking hovered.
        _hover.Snap(0F);
        _press.Snap(0F);
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        PaintParent(e);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var hover = Enabled ? _hover.Value : 0F;
        if (_switchOn is not null)
        {
            MenuGlyphs.DrawSwitch(graphics, new RectangleF(0, 0, Width, Height), _switch.Value,
                Palette.Mix(BackColor, ForeColor, 0.18 * hover));
            if (Focused && ShowFocusCues)
            {
                using var switchRing = new Pen(ForeColor, 2F);
                using var switchShape = MenuGlyphs.RoundedRectangle(
                    new RectangleF(1, 1, Width - 2, Height - 2), (Height - 2) / 2F);
                graphics.DrawPath(switchRing, switchShape);
            }
            return;
        }

        var fill = Palette.Mix(
            BackColor, HoverBackColor ?? Palette.Mix(BackColor, ForeColor, 0.10), hover);
        fill = Palette.Mix(fill, ForeColor, 0.08 * _press.Value);
        var ink = !Enabled
            ? Palette.Mix(BackColor, ForeColor, 0.5)
            : Palette.Mix(ForeColor, HoverForeColor ?? ForeColor, hover);

        using (var brush = new SolidBrush(fill))
        {
            if (Radius <= 0)
            {
                graphics.FillRectangle(brush, ClientRectangle);
            }
            else
            {
                using var shape = MenuGlyphs.RoundedRectangle(
                    new RectangleF(0, 0, Width, Height), Radius);
                graphics.FillPath(brush, shape);
            }
        }

        TextRenderer.DrawText(
            graphics, Verbatim ? Text : DisplayText.Sentence(Text), Font, ClientRectangle, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Palette.Blue, 2F);
            using var shape = MenuGlyphs.RoundedRectangle(
                new RectangleF(1, 1, Width - 2, Height - 2), Math.Max(Radius, 1));
            graphics.DrawPath(ring, shape);
        }
    }

    /// <summary>
    /// Paints what lies behind the rounded corners. The fill is <c>BackColor</c>, so the
    /// framework's own transparent-background path is not available here.
    /// </summary>
    private void PaintParent(PaintEventArgs e)
    {
        if (Parent is null)
        {
            return;
        }

        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(-Left, -Top);
        using (var parentArgs = new PaintEventArgs(e.Graphics, Bounds))
        {
            InvokePaintBackground(Parent, parentArgs);
            InvokePaint(Parent, parentArgs);
        }
        e.Graphics.Restore(state);
    }
}
