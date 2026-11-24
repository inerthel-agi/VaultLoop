using System.Drawing;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>What the user wants the close button to do this time.</summary>
internal enum CloseChoice
{
    Cancel,
    HideToTray,
    Exit
}

/// <summary>
/// Asked when the window is closed: keep VaultLoop running in the tray, or stop it. Closing
/// the dialog itself, or pressing Escape, changes nothing.
/// </summary>
internal sealed class CloseChoiceDialog : ThemedDialog
{
    /// <param name="noSaveActive">
    /// Adds the warning that exiting restores the Rockstar link, so the caller does not have
    /// to ask the same question a second time.
    /// </param>
    internal CloseChoiceDialog(bool darkMode, bool noSaveActive) :
        base("CLOSE VAULTLOOP", new Size(440, noSaveActive ? 236 : 188), darkMode)
    {
        Controls.Add(ControlFactory.MakeLabel(
            "Keep VaultLoop running?", new Rectangle(26, 56, 388, 30),
            Typography.CardHeading, BackColor, ForeColor));
        Controls.Add(new Label
        {
            Text = "In the tray, the shortcut and the HUD keep working. Exit stops VaultLoop.",
            Bounds = new Rectangle(28, 88, 386, 40),
            BackColor = BackColor,
            ForeColor = Role(Palette.Muted)
        });

        var buttonsTop = 136;
        if (noSaveActive)
        {
            Controls.Add(new Label
            {
                Name = "CloseActiveWarning",
                Text = "No-save is ACTIVE. Exiting restores the Rockstar link.",
                Bounds = new Rectangle(28, 132, 384, 36),
                BackColor = Palette.Mix(BackColor, Palette.HotPink, 0.22),
                ForeColor = ForeColor,
                Font = Typography.MenuCaption,
                Padding = new Padding(10, 0, 10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            buttonsTop = 184;
        }

        var cancel = MakeChoice(
            "CANCEL", "CloseCancel", new Rectangle(28, buttonsTop, 96, 36),
            Role(Palette.Control), ForeColor, CloseChoice.Cancel);
        var exit = MakeChoice(
            "EXIT COMPLETELY", "CloseExit", new Rectangle(152, buttonsTop, 136, 36),
            Role(Palette.Control), ForeColor, CloseChoice.Exit);
        var tray = MakeChoice(
            "HIDE TO TRAY", "CloseToTray", new Rectangle(296, buttonsTop, 118, 36),
            Palette.Blue, Palette.Ink, CloseChoice.HideToTray);
        Controls.AddRange([tray, exit, cancel]);
        AcceptButton = tray;
        CancelButton = cancel;
    }

    internal CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    private SoftButton MakeChoice(
        string text, string name, Rectangle bounds, Color backColor, Color foreColor,
        CloseChoice choice)
    {
        var button = ControlFactory.MakeButton(
            text, bounds, Typography.MenuCaption, backColor, foreColor);
        button.Name = name;
        button.Click += (_, _) =>
        {
            Choice = choice;
            DialogResult = choice == CloseChoice.Cancel ? DialogResult.Cancel : DialogResult.OK;
            Close();
        };
        return button;
    }
}
