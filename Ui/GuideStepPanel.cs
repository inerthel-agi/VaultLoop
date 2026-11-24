using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// One row of the guide's step list: a rounded fill with the step number and title on it,
/// focusable and announced as a radio button.
/// </summary>
internal sealed class GuideStepPanel : Panel
{
    private readonly AnimatedFloat _blend;
    private bool _isCurrent;
    private Color _fromFill;
    private Color _toFill;

    internal GuideStepPanel()
    {
        // The labels are transparent: they show this fill and have to follow it.
        _blend = new AnimatedFloat(this, 1F, () => Invalidate(true));
        // A Panel is not selectable, so Tab skipped every step and the arrow keys only
        // worked after a mouse click had put the focus there.
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }
            _isCurrent = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
    }

    /// <summary>
    /// Repaints the step in one pair of colors. The number keeps its own muted color, so it
    /// is the one child left untouched.
    /// </summary>
    internal void SetStepColors(Color fill, Color foreground)
    {
        for (var index = 1; index < Controls.Count; index++)
        {
            Controls[index].ForeColor = foreground;
        }
        if (fill == _toFill)
        {
            return;
        }
        _fromFill = DisplayedFill;
        _toFill = fill;
        _blend.AnimateFrom(0F, 1F, Motion.Standard);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = MenuGlyphs.RoundedRectangle(new RectangleF(0, 0, Width, Height), 6F))
        using (var brush = new SolidBrush(DisplayedFill))
        {
            e.Graphics.FillPath(brush, shape);
        }

        if (Focused)
        {
            using var ring = new Pen(Palette.Blue, 2F);
            using var shape = MenuGlyphs.RoundedRectangle(
                new RectangleF(1, 1, Width - 2, Height - 2), 5F);
            e.Graphics.DrawPath(ring, shape);
        }
    }

    private Color DisplayedFill => Palette.Mix(_fromFill, _toFill, _blend.Value);

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate(true);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate(true);
    }

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new GuideStepAccessibleObject(this);

    internal void PerformDefaultAction() => OnClick(EventArgs.Empty);

    private sealed class GuideStepAccessibleObject(GuideStepPanel owner)
        : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            base.State |
            (owner.IsCurrent
                ? AccessibleStates.Checked | AccessibleStates.Selected
                : AccessibleStates.None);

        public override void DoDefaultAction() => owner.PerformDefaultAction();
    }
}
