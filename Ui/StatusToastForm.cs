using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed class StatusToastForm : Form
{
    private readonly System.Windows.Forms.Timer _closeTimer;
    private readonly Color _accent;
    private bool _roundedByCompositor;

    internal StatusToastForm(string title, string detail, Color accent)
    {
        var hasDetail = !string.IsNullOrWhiteSpace(detail);
        _accent = accent;
        ClientSize = new Size(360, hasDetail ? 104 : 72);
        BackColor = Palette.MenuSurface;
        ForeColor = Palette.MenuText;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.Alert;
        if (Motion.Enabled)
        {
            Opacity = 0;
        }

        Controls.Add(new Label
        {
            Text = DisplayText.Sentence(title),
            Bounds = new Rectangle(40, 12, 302, 24),
            BackColor = Palette.MenuSurface,
            ForeColor = Palette.MenuText,
            Font = Typography.MenuTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        });
        Controls.Add(new Label
        {
            Text = hasDetail ? detail : "VaultLoop · GTA only",
            Bounds = new Rectangle(40, 38, 302, hasDetail ? 54 : 22),
            BackColor = Palette.MenuSurface,
            ForeColor = Palette.MenuMuted,
            Font = Typography.MenuDetail,
            AutoEllipsis = true
        });

        _closeTimer = new System.Windows.Forms.Timer { Interval = hasDetail ? 8000 : 2200 };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Motion.Run(this, Motion.Standard + 60, progress => Opacity = 1 - progress, Close);
        };
        Shown += (_, _) =>
        {
            var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(
                workingArea.Right - Width - 24,
                workingArea.Bottom - Height - 24);
            AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
            Motion.Run(this, Motion.Standard, progress => Opacity = progress);
            _closeTimer.Start();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int ToolWindow = 0x00000080;
            const int NoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= ToolWindow | NoActivate;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _roundedByCompositor = NativeMethods.TryRoundCorners(Handle, Palette.MenuLine);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // The accent says which kind of event this is; the text stays neutral and readable.
        using (var dot = new SolidBrush(_accent))
        {
            e.Graphics.FillEllipse(dot, 18, 19, 10, 10);
        }

        if (!_roundedByCompositor)
        {
            using var pen = new Pen(Palette.MenuLine);
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closeTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
