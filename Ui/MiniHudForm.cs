using System;
using System.Drawing;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The in-game status label. It is quiet while saves go through and turns into a solid
/// block of the state color when no-save is on, so the two read apart at a glance instead
/// of by three letters.
/// </summary>
internal sealed class MiniHudForm : Form
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private static readonly Color QuietText = Color.FromArgb(213, 216, 220);

    // Measures are authored at 96 DPI, where the status font is 18 px tall.
    private readonly float _scale = Typography.HudStatus.Height / 18F;
    private readonly AnimatedFloat _blend;
    private bool? _enabled = false;
    private bool _roundedByCompositor;
    private (Color Back, Color Text, Color Ink, float Orb) _from;
    private (Color Back, Color Text, Color Ink, float Orb) _to;

    internal MiniHudForm()
    {
        _blend = new AnimatedFloat(this, 1F, ApplyLook);
        var textWidth = 0;
        foreach (var state in new bool?[] { true, false, null })
        {
            textWidth = Math.Max(textWidth, TextRenderer.MeasureText(
                StatusText(state), Typography.HudStatus, Size.Empty, TextFlags).Width);
        }

        ClientSize = new Size(Px(45) + textWidth + Px(16), Px(44));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        SetState(false);
    }

    internal void ShowOnActiveScreen()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(workingArea.Right - Width - 24, workingArea.Top + 24);
        var fade = Motion.Enabled;
        if (fade)
        {
            Opacity = 0;
        }
        Show();
        if (fade)
        {
            Motion.Run(this, Motion.Standard, progress => Opacity = progress);
        }
    }

    internal void SetState(bool? enabled)
    {
        _enabled = enabled;
        AccessibleName = StatusText(enabled);
        _from = CurrentLook();
        _to = Look(enabled);
        _blend.AnimateFrom(0F, 1F, Motion.Standard);
        ApplyBorder();
    }

    /// <summary>Repaints the current state in the theme that is now active.</summary>
    internal void RefreshTheme() => SetState(_enabled);

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int ToolWindow = 0x00000080;
            const int Transparent = 0x00000020;
            const int NoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= ToolWindow | Transparent | NoActivate;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyBorder();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var look = CurrentLook();

        var orb = Px(26);
        var orbTop = (ClientSize.Height - orb) / 2F;
        using (var orbBrush = new SolidBrush(Palette.Mix(look.Back, look.Ink, look.Orb)))
        {
            graphics.FillEllipse(orbBrush, Px(9), orbTop, orb, orb);
        }
        var cloudScale = _scale * 0.8F;
        var cloudInset = (orb - 20F * cloudScale) / 2F;
        MenuGlyphs.DrawCloud(graphics,
            _enabled switch
            {
                true => CloudMark.Slash,
                false => CloudMark.Check,
                null => CloudMark.Unknown
            },
            new PointF(Px(9) + cloudInset, orbTop + cloudInset), cloudScale, look.Ink);

        var left = Px(45);
        TextRenderer.DrawText(graphics, StatusText(_enabled), Typography.HudStatus,
            new Rectangle(left, 0, ClientSize.Width - left, ClientSize.Height),
            look.Text, look.Back, TextFlags);

        if (!_roundedByCompositor)
        {
            using var pen = new Pen(BorderColor);
            graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    private Color BorderColor => _enabled == false ? Palette.MenuLine : _to.Back;

    /// <summary>Quiet while saves go through, a solid block of the state color otherwise.</summary>
    private static (Color Back, Color Text, Color Ink, float Orb) Look(bool? enabled) =>
        enabled switch
        {
            true => (Palette.HotPink, Palette.Ink, Palette.Ink, 0.14F),
            false => (Palette.MenuSurface, QuietText, Palette.Acid, 0.18F),
            null => (Palette.Yellow, Palette.Ink, Palette.Ink, 0.14F)
        };

    private (Color Back, Color Text, Color Ink, float Orb) CurrentLook()
    {
        var progress = _blend.Value;
        return (Palette.Mix(_from.Back, _to.Back, progress),
            Palette.Mix(_from.Text, _to.Text, progress),
            Palette.Mix(_from.Ink, _to.Ink, progress),
            _from.Orb + (_to.Orb - _from.Orb) * progress);
    }

    private void ApplyLook()
    {
        BackColor = CurrentLook().Back;
        Invalidate();
    }

    private void ApplyBorder()
    {
        if (IsHandleCreated)
        {
            _roundedByCompositor = NativeMethods.TryRoundCorners(Handle, BorderColor);
        }
    }

    private int Px(float value) => (int)Math.Round(value * _scale);

    private static string StatusText(bool? enabled) => enabled switch
    {
        true => "No-save Enabled",
        false => "No-save Disabled",
        null => "No-save Unknown"
    };
}
