using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed class BooleanToggle : Control
{
    private readonly AnimatedFloat _position;
    private readonly AnimatedFloat _colorBlend;
    private bool _checked;
    private bool _isStateKnown = true;
    private bool _isRecoveryMode;
    private Color _fromTrack = Palette.Acid;
    private Color _toTrack = Palette.Acid;

    internal bool IsRecoveryMode
    {
        get => _isRecoveryMode;
        set
        {
            if (_isRecoveryMode == value)
            {
                return;
            }
            _isRecoveryMode = value;
            AccessibleDefaultActionDescription = value
                ? "Restore firewall rule"
                : Checked ? "Disable no-save" : "Enable no-save";
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            AnimateToState();
        }
    }

    internal bool IsStateKnown
    {
        get => _isStateKnown;
        set
        {
            if (_isStateKnown == value)
            {
                return;
            }
            _isStateKnown = value;
            AccessibleDefaultActionDescription = value
                ? (Checked ? "Disable no-save" : "Enable no-save")
                : "State unavailable";
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            AnimateToState();
        }
    }

    internal bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }
            _checked = value;
            AccessibleDefaultActionDescription = value ? "Disable no-save" : "Enable no-save";
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            AnimateToState();
        }
    }

    internal event EventHandler? ToggleRequested;

    internal BooleanToggle()
    {
        _position = new AnimatedFloat(this, 0F, Invalidate);
        _colorBlend = new AnimatedFloat(this, 1F, Invalidate);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                 ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        // The track is rounded; the card behind it shows through the corners.
        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleDefaultActionDescription = "Enable no-save";
    }

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new BooleanToggleAccessibleObject(this);

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        AnimateToState();
    }

    /// <summary>The track color on screen: on its way from the previous state's to this one's.</summary>
    private Color DisplayedTrack => Palette.Mix(_fromTrack, _toTrack, _colorBlend.Value);

    /// <summary>
    /// Slides the knob to the current position and lets the track color settle into the
    /// current state's. Purely visual: the state itself changed before this was called.
    /// </summary>
    private void AnimateToState()
    {
        var target = GetTrackColor(Enabled, IsStateKnown, IsRecoveryMode, Checked);
        if (target != _toTrack)
        {
            _fromTrack = DisplayedTrack;
            _toTrack = target;
            _colorBlend.AnimateFrom(0F, 1F, Motion.Standard);
        }
        _position.AnimateTo(Checked ? 1F : 0F, Motion.Standard);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const TextFormatFlags Centered =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, 0, Width, Height);
        const int Inset = 6;
        var knobWidth = Width * 3 / 8;
        var position = _position.Value;
        var knob = new Rectangle(
            Inset + (int)Math.Round((Width - knobWidth - 2 * Inset) * position),
            Inset, knobWidth, Height - 2 * Inset);
        // The label sits on the side the knob is leaving, and fades through the middle.
        var showsOn = position >= 0.5F;
        var labelArea = showsOn
            ? new Rectangle(0, 0, knob.X, Height)
            : new Rectangle(knob.Right, 0, Width - knob.Right, Height);
        var trackColor = DisplayedTrack;

        using (var trackShape = MenuGlyphs.RoundedRectangle(track, 12F))
        using (var trackBrush = new SolidBrush(trackColor))
        {
            graphics.FillPath(trackBrush, trackShape);
        }

        var labelFont = Typography.ToggleLabel;
        if (IsRecoveryMode)
        {
            TextRenderer.DrawText(graphics, "Restore", labelFont, track, Palette.Ink, Centered);
        }
        else if (!IsStateKnown)
        {
            TextRenderer.DrawText(graphics, "State ?", labelFont, track, Palette.Ink, Centered);
        }
        else
        {
            using (var knobShape = MenuGlyphs.RoundedRectangle(knob, 8F))
            {
                graphics.FillPath(Brushes.White, knobShape);
            }
            TextRenderer.DrawText(graphics, showsOn ? "On" : "Off", labelFont, labelArea,
                Palette.Mix(trackColor, Palette.Ink, Math.Abs(2F * position - 1F)), Centered);
            TextRenderer.DrawText(graphics, "No-save", Typography.MenuCaption, knob,
                Palette.Ink, Centered);
        }

        if (Focused)
        {
            using var ring = new Pen(Palette.Ink, 2F);
            using var ringShape = MenuGlyphs.RoundedRectangle(
                new RectangleF(3, 3, Width - 6, Height - 6), 9F);
            graphics.DrawPath(ring, ringShape);
        }
    }

    // The focus ring is part of the painting, so it has to follow the focus.
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        RequestToggle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            RequestToggle();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void RequestToggle()
    {
        if (Enabled && (IsStateKnown || IsRecoveryMode))
        {
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static Color GetTrackColor(
        bool enabled, bool stateKnown, bool recoveryMode, bool isChecked) =>
        !enabled && stateKnown && !recoveryMode
            ? SystemColors.ControlDark
            : !stateKnown ? Palette.Yellow : isChecked ? Palette.HotPink : Palette.Acid;

    private sealed class BooleanToggleAccessibleObject(BooleanToggle owner)
        : ControlAccessibleObject(owner)
    {
        public override string? DefaultAction =>
            owner.IsRecoveryMode ? "Restore firewall rule" :
            !owner.IsStateKnown ? "State unavailable" :
            owner.Checked ? "Disable no-save" : "Enable no-save";

        public override AccessibleStates State =>
            base.State |
            (owner.IsStateKnown && owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);

        public override void DoDefaultAction() => owner.RequestToggle();
    }
}
