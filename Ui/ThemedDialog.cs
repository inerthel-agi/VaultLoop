using System;
using System.Drawing;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The frame every dialog shares: a borderless window in the theme's canvas color, with a
/// draggable title bar and a close button.
/// </summary>
internal abstract class ThemedDialog : Form
{
    private bool _roundedByCompositor;

    protected ThemedDialog(string title, Size size, bool darkMode)
    {
        DarkMode = darkMode;
        Text = title;
        ClientSize = size;
        BackColor = Role(Palette.Canvas);
        ForeColor = Role(Palette.Text);
        Font = Typography.MenuBody;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = BackColor
        };
        var titleLabel = new Label
        {
            Text = DisplayText.Sentence(title),
            Bounds = new Rectangle(16, 0, size.Width - 68, 44),
            BackColor = BackColor,
            ForeColor = ForeColor,
            Font = Typography.MenuTitle,
            TextAlign = ContentAlignment.MiddleLeft
        };
        // Segoe MDL2 Assets: the close glyph Windows draws on its own title bars.
        var closeButton = ControlFactory.MakeButton(
            "", new Rectangle(size.Width - 46, 0, 46, 44), Typography.ChromeGlyph,
            BackColor, ForeColor);
        closeButton.Radius = 0;
        closeButton.HoverBackColor = Palette.AlertRed;
        closeButton.HoverForeColor = Palette.Surface;
        closeButton.AccessibleName = "Close dialog";
        closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        WindowDrag.Attach(this, titleBar, titleLabel);
        titleBar.Controls.AddRange([titleLabel, closeButton]);
        Controls.Add(titleBar);
    }

    protected bool DarkMode { get; }

    /// <summary>A window role of <see cref="Palette"/> in this dialog's theme.</summary>
    protected Color Role(Color light) => Palette.Themed(light, DarkMode);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _roundedByCompositor = NativeMethods.TryRoundCorners(Handle, Role(Palette.Line));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_roundedByCompositor)
        {
            return;
        }

        using var pen = new Pen(Role(Palette.Line));
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}
