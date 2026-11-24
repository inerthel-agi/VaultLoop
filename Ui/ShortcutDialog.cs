using System.Drawing;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed class ShortcutDialog : ThemedDialog
{
    private const string CaptureHint =
        "Press a new combination. Use a modifier, or a function key.";
    private const string RejectedHint =
        "Not accepted. Press a function key, or hold Ctrl, Alt, or Shift with a key.";

    private readonly Button _capturedButton;
    private readonly Label _keyboardHint;
    private readonly Color _hintColor;
    private readonly Color _rejectedColor;
    private readonly Button _controllerCaptureButton;
    private readonly Button _configureControllerButton;
    private readonly Label _controllerHint;
    private readonly ControllerShortcutService? _controllerService;
    private readonly System.Windows.Forms.Timer _controllerTimer;
    private bool _controllerCaptureActive;

    internal Keys ShortcutModifiers { get; private set; }
    internal Keys ShortcutKey { get; private set; }
    internal ControllerShortcut? ControllerShortcut { get; private set; }

    internal ShortcutDialog(
        Keys modifiers, Keys key, bool darkMode,
        ControllerShortcutService? controllerService = null,
        ControllerShortcut? controllerShortcut = null) :
        base("CONFIGURE SHORTCUT", new Size(520, 326), darkMode)
    {
        ShortcutModifiers = modifiers;
        ShortcutKey = key;
        ControllerShortcut = controllerShortcut;
        _controllerService = controllerService;
        // The capture used to be bound to the capture field alone, so every key was ignored
        // once the focus had moved — clicking REPLACE or CLEAR for the controller was enough,
        // and the dialog then looked like it had stopped reading the keyboard. The whole
        // dialog listens instead; navigation keys are still left to the dialog.
        KeyPreview = true;
        KeyDown += CaptureShortcut;
        var canvas = BackColor;
        var textColor = ForeColor;
        var muted = Role(Palette.Muted);
        // The pink is fine on the dark canvas and too pale to read on the light one.
        _rejectedColor = darkMode
            ? Palette.HotPink
            : Palette.Mix(Palette.AlertRed, Palette.Ink, 0.25);

        Controls.Add(new Label
        {
            Text = "Keyboard shortcut",
            Bounds = new Rectangle(19, 50, 460, 26),
            Font = Typography.CardHeading,
            BackColor = canvas,
            ForeColor = textColor
        });
        _hintColor = muted;
        _keyboardHint = new Label
        {
            Text = CaptureHint,
            Bounds = new Rectangle(21, 76, 478, 20),
            Font = Typography.MenuDetail,
            BackColor = canvas,
            ForeColor = muted
        };
        _keyboardHint.Name = "KeyboardShortcutHint";
        Controls.Add(_keyboardHint);

        var captured = ControlFactory.MakeButton(
            ShortcutSettings.Format(modifiers, key), new Rectangle(20, 100, 384, 40),
            Typography.ShortcutCapture, Role(Palette.Surface), textColor);
        captured.Verbatim = true;
        _capturedButton = captured;
        _capturedButton.Name = "ShortcutCapture";
        _capturedButton.AccessibleName = "Keyboard shortcut capture field";
        _capturedButton.AccessibleDescription =
            "Press the shortcut you want to use anywhere in this dialog.";
        Controls.Add(_capturedButton);

        Controls.Add(new Label
        {
            Text = "Controller shortcut",
            Bounds = new Rectangle(19, 150, 460, 26),
            Font = Typography.CardHeading,
            BackColor = canvas,
            ForeColor = textColor
        });
        Controls.Add(new Label
        {
            Text = "Hold an exact combination of 2 or 3 buttons, then release all buttons.",
            Bounds = new Rectangle(21, 176, 478, 20),
            Font = Typography.MenuDetail,
            BackColor = canvas,
            ForeColor = muted
        });

        var controllerCapture = ControlFactory.MakeButton(
            ControllerShortcut?.Format() ?? "NOT CONFIGURED",
            new Rectangle(20, 200, 280, 40), Typography.MenuCaption,
            Role(Palette.Surface), textColor);
        controllerCapture.Verbatim = true;
        _controllerCaptureButton = controllerCapture;
        _controllerCaptureButton.Name = "ControllerShortcutCapture";
        _controllerCaptureButton.AccessibleName = "Controller shortcut capture status";
        _controllerCaptureButton.TabStop = false;

        _configureControllerButton = ControlFactory.MakeButton(
            ControllerShortcut is null ? "CONFIGURE" : "REPLACE",
            new Rectangle(308, 200, 112, 40), Typography.MenuCaption,
            Palette.Blue, Palette.Ink);
        _configureControllerButton.Name = "ConfigureControllerShortcut";
        _configureControllerButton.AccessibleName = "Configure controller shortcut";
        _configureControllerButton.Enabled = controllerService is not null;
        _configureControllerButton.Click += (_, _) => ToggleControllerCapture();

        var clearControllerButton = ControlFactory.MakeButton(
            "CLEAR", new Rectangle(428, 200, 72, 40), Typography.MenuCaption,
            Role(Palette.Control), textColor);
        clearControllerButton.AccessibleName = "Clear controller shortcut";
        clearControllerButton.Enabled = ControllerShortcut is not null;

        _controllerHint = new Label
        {
            Text = controllerService is null
                ? "Controller input is unavailable."
                : ControllerShortcut is null
                    ? "Controller shortcut disabled until configured."
                    : "This controller toggles no-save ON or OFF.",
            Bounds = new Rectangle(21, 244, 478, 20),
            Font = Typography.MenuDetail,
            BackColor = canvas,
            ForeColor = muted
        };
        clearControllerButton.Click += (_, _) =>
        {
            StopControllerCapture();
            ControllerShortcut = null;
            _controllerCaptureButton.Text = "NOT CONFIGURED";
            _configureControllerButton.Text = "CONFIGURE";
            clearControllerButton.Enabled = false;
            _controllerHint.Text = "Controller shortcut disabled until configured.";
        };
        Controls.AddRange([
            _controllerCaptureButton, _configureControllerButton,
            clearControllerButton, _controllerHint]);

        _controllerTimer = new System.Windows.Forms.Timer
        {
            Interval = 50
        };
        _controllerTimer.Tick += (_, _) =>
        {
            if (_controllerService is null)
            {
                return;
            }

            var snapshot = _controllerService.CaptureSnapshot;
            _controllerCaptureButton.Text = snapshot.StatusText;
            if (snapshot.Retry)
            {
                _controllerCaptureActive = false;
                _controllerTimer.Stop();
                _configureControllerButton.Text = "RETRY";
                _controllerHint.Text =
                    "The configured controller disconnected during capture.";
                return;
            }
            if (!snapshot.Complete || snapshot.Shortcut is null)
            {
                return;
            }

            ControllerShortcut = snapshot.Shortcut;
            _controllerCaptureActive = false;
            _controllerTimer.Stop();
            _configureControllerButton.Text = "REPLACE";
            clearControllerButton.Enabled = true;
            _controllerHint.Text =
                "In GTA, hold 0.5 s to toggle ON or OFF. Click SAVE to apply.";
        };

        var secondaryColor = Role(Palette.Control);
        var resetButton = ControlFactory.MakeButton(
            "RESET", new Rectangle(412, 100, 88, 40), Typography.MenuCaption,
            secondaryColor, textColor);
        var saveButton = ControlFactory.MakeButton(
            "SAVE", new Rectangle(414, 276, 86, 34), Typography.MenuCaption,
            Palette.Blue, Palette.Ink);
        var cancelButton = ControlFactory.MakeButton(
            "CANCEL", new Rectangle(322, 276, 84, 34), Typography.MenuCaption,
            secondaryColor, textColor);
        resetButton.Click += (_, _) =>
        {
            (ShortcutModifiers, ShortcutKey) = ShortcutSettings.Default;
            _capturedButton.Text = ShortcutSettings.Format(ShortcutModifiers, ShortcutKey);
        };
        saveButton.Click += (_, _) =>
        {
            StopControllerCapture();
            DialogResult = DialogResult.OK;
            Close();
        };
        cancelButton.Click += (_, _) =>
        {
            StopControllerCapture();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.AddRange([resetButton, saveButton, cancelButton]);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Shown += (_, _) => _capturedButton.Focus();
        FormClosed += (_, _) =>
        {
            StopControllerCapture();
            _controllerTimer.Dispose();
        };
    }

    internal static bool IsValidShortcut(Keys modifiers, Keys key)
    {
        var isFunctionKey = key >= Keys.F1 && key <= Keys.F24;
        var hasModifier = (modifiers & (Keys.Control | Keys.Alt | Keys.Shift)) != Keys.None;
        return key != Keys.None && (isFunctionKey || hasModifier) &&
               !(key == Keys.F4 && modifiers == Keys.Alt) &&
               !(key == Keys.Tab && modifiers == Keys.Alt);
    }

    private void CaptureShortcut(object? sender, KeyEventArgs eventArgs)
    {
        var outcome = TryCapture(eventArgs.KeyCode, eventArgs.Modifiers);
        if (outcome == CaptureOutcome.Rejected)
        {
            System.Media.SystemSounds.Beep.Play();
        }
        if (outcome != CaptureOutcome.Accepted)
        {
            return;
        }
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
    }

    /// <summary>
    /// Applies one key press to the captured shortcut. A press that does not describe a usable
    /// shortcut says so on screen: the rule — a function key, or a modifier with a key — used
    /// to be enforced by a system beep alone, which is inaudible on a muted machine and left
    /// the dialog looking unresponsive.
    /// </summary>
    internal CaptureOutcome TryCapture(Keys key, Keys modifiers)
    {
        if (key is Keys.Tab or Keys.Escape or Keys.Enter or Keys.Space or
            Keys.Left or Keys.Right or Keys.Up or Keys.Down or
            Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
        {
            return CaptureOutcome.Ignored;
        }
        if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey)
        {
            return CaptureOutcome.Ignored;
        }

        var capturedModifiers = modifiers & (Keys.Control | Keys.Alt | Keys.Shift);
        if (!IsValidShortcut(capturedModifiers, key))
        {
            _keyboardHint.Text = RejectedHint;
            _keyboardHint.ForeColor = _rejectedColor;
            return CaptureOutcome.Rejected;
        }

        ShortcutModifiers = capturedModifiers;
        ShortcutKey = key;
        _capturedButton.Text = ShortcutSettings.Format(capturedModifiers, key);
        _keyboardHint.Text = CaptureHint;
        _keyboardHint.ForeColor = _hintColor;
        return CaptureOutcome.Accepted;
    }

    /// <summary>What one key press did to the captured shortcut.</summary>
    internal enum CaptureOutcome
    {
        /// <summary>A navigation or modifier key the dialog leaves alone.</summary>
        Ignored,

        /// <summary>A key press that does not describe a usable shortcut.</summary>
        Rejected,

        /// <summary>The captured shortcut now describes this press.</summary>
        Accepted
    }

    private void ToggleControllerCapture()
    {
        if (_controllerService is null)
        {
            return;
        }
        if (_controllerCaptureActive)
        {
            StopControllerCapture();
            _controllerCaptureButton.Text =
                ControllerShortcut?.Format() ?? "NOT CONFIGURED";
            return;
        }

        _controllerCaptureActive = true;
        _configureControllerButton.Text = "CANCEL CAPTURE";
        _controllerHint.Text = _controllerService.RawInputAvailable
            ? "Xbox, DualShock 4 and DualSense input ready."
            : "Xbox ready. PlayStation input is unavailable.";
        _controllerService.BeginCapture();
        _controllerTimer.Start();
    }

    private void StopControllerCapture()
    {
        if (!_controllerCaptureActive)
        {
            return;
        }
        _controllerCaptureActive = false;
        _controllerTimer.Stop();
        _controllerService?.CancelCapture();
        _configureControllerButton.Text =
            ControllerShortcut is null ? "CONFIGURE" : "REPLACE";
    }
}
