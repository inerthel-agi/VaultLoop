using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed partial class MainForm : Form
{
    private readonly FirewallService? _firewall;
    private readonly BooleanToggle _toggle;
    private readonly Label _stateTitle;
    private readonly Label _stateDetail;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly bool _previewMode;
    private readonly bool _isAdministrator;
    private readonly Image _logoImage;
    private readonly GlobalHotkeyHook _hotkeyHook;
    private readonly ControllerShortcutService _controllerShortcutService;
    private readonly MiniHudForm? _miniHud;
    private readonly SoftButton _shortcutBadge;
    private readonly SoftButton[] _themeButtons;
    private readonly SoftButton _hudVisibilityButton;
    private readonly SoftButton _startupSwitch;
    private readonly Label _gameStatusLabel;
    private readonly ThemeController _themeController;
    private Color _stateColor = Palette.Acid;
    private Color _fromStateColor = Palette.Acid;
    private readonly AnimatedFloat _stateBlend;
    private CloudMark _stateMark = CloudMark.Unknown;
    private Color _gameStatusColor = Palette.HotPink;
    private bool _roundedByCompositor;
    private bool _closeChoiceOpen;
    private bool _exitWarningShown;

    // Read from the keyboard hook thread through the _canTrigger delegate, written on the UI
    // thread. Without volatile the hook can observe a stale value, swallow the keystroke, and
    // post a toggle that then no-ops on the UI thread — the key press disappears silently.
    private volatile bool _applying;
    private volatile bool _stateKnown = true;

    private AppTheme _theme;
    private bool _darkMode;
    private bool _hudEnabled;
    private bool _hasVerifiedForegroundGame;
    private bool _hotkeyRegistered;
    private bool _controllerRawInputRegistered;
    private FirewallRuleState _firewallState = FirewallRuleState.Inactive;
    private string? _verifiedGamePath;

    internal MainForm(FirewallService? firewall, bool previewMode = false,
        bool previewState = false, bool previewUnknown = false)
    {
        _firewall = firewall;
        // One pixel wider than the card: its antialiased edge spills onto the next column.
        _stateBlend = new AnimatedFloat(
            this, 1F, () => Invalidate(Rectangle.Inflate(StatusCard, 1, 1), true));
        _previewMode = previewMode;
        _isAdministrator = _previewMode || Program.IsRunningAsAdministrator();
        _logoImage = LoadLogo();
        var shortcut = ShortcutSettings.Load();
        _hotkeyHook = new GlobalHotkeyHook(
            shortcut.Modifiers, shortcut.Key,
            () => _isAdministrator && !_applying && _stateKnown);
        _hotkeyHook.Pressed += HandleHotkeyPressed;
        _hotkeyHook.Refused += HandleHotkeyRefused;
        _controllerShortcutService = new ControllerShortcutService(
            ControllerShortcutSettings.Load(),
            () => _isAdministrator && !_applying && _stateKnown);
        _controllerShortcutService.Pressed += HandleHotkeyPressed;
        _controllerShortcutService.Refused += HandleHotkeyRefused;
        _theme = ThemeSettings.Load();
        _darkMode = _theme != AppTheme.Light;
        Palette.Oled = _theme == AppTheme.Oled;
        // Preview mode has no HUD to show, and reading the preference there would make the
        // rendered window depend on it. It keeps the default instead.
        _hudEnabled = _previewMode || HudSettings.Load();

        var chrome = BuildLayout();
        _shortcutBadge = chrome.ShortcutBadge;
        _themeButtons = chrome.ThemeButtons;
        _toggle = chrome.Toggle;
        _stateTitle = chrome.StateTitle;
        _stateDetail = chrome.StateDetail;
        _hudVisibilityButton = chrome.HudVisibilityButton;
        _startupSwitch = chrome.StartupSwitch;
        _gameStatusLabel = chrome.GameStatusLabel;
        _miniHud = _previewMode ? null : new MiniHudForm();
        if (!_previewMode)
        {
            InitializeTray();
        }

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) => QueueRuntimeRefresh();
        FormClosing += HandleClosing;
        Shown += HandleShown;
        Resize += (_, _) =>
        {
            if (!_previewMode && WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        };
        _themeController = new ThemeController(this, [_toggle, .. _themeButtons]);
        _themeController.CaptureThemeColors();
        _themeController.ApplyTheme(_darkMode);
        ShowSelectedTheme();

        if (_previewMode)
        {
            if (previewUnknown)
            {
                SetUnknownState();
            }
            else
            {
                SetDisplayedState(previewState);
            }
        }
        else
        {
            ActivityLog.Write("session started");
            RefreshRuntimeState(showErrors: true);
            _refreshTimer.Start();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowEdge();
        if (!_previewMode)
        {
            _hotkeyRegistered = _hotkeyHook.Install();
            _controllerRawInputRegistered =
                _controllerShortcutService.Install(Handle);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!_previewMode)
        {
            _controllerShortcutService.Uninstall();
            _hotkeyHook.Uninstall();
            _hotkeyRegistered = false;
            _controllerRawInputRegistered = false;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _miniHud?.Dispose();
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            _trayMenu?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _controllerShortcutService.Dispose();
            _logoImage.Dispose();
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (!_previewMode)
        {
            if (message.Msg == RawInputNativeMethods.InputMessage)
            {
                _controllerShortcutService.ProcessRawInput(message.LParam);
            }
            else if (message.Msg == RawInputNativeMethods.InputDeviceChangeMessage)
            {
                _controllerShortcutService.ProcessRawInputDeviceChange(
                    message.LParam, message.WParam.ToInt32());
            }
            else if (message.Msg == NativeMethods.SystemCommandMessage &&
                     (message.WParam.ToInt64() & 0xFFF0) == NativeMethods.CloseCommand)
            {
                // Alt+F4 and the taskbar's "Close window" get the same question as the button.
                RequestClose();
                return;
            }
        }
        base.WndProc(ref message);
    }

    /// <summary>
    /// The user asked to close the window: offer the tray, a full exit, or nothing. Code
    /// that calls <see cref="Form.Close"/> itself — the tray's exit, the relaunch as
    /// administrator — has already decided and does not come through here.
    /// </summary>
    private void RequestClose()
    {
        if (_previewMode)
        {
            Close();
            return;
        }
        if (_closeChoiceOpen)
        {
            return;
        }

        var noSaveActive = _firewallState == FirewallRuleState.Active;
        CloseChoice choice;
        _closeChoiceOpen = true;
        try
        {
            using var dialog = new CloseChoiceDialog(_darkMode, noSaveActive);
            dialog.ShowDialog(this);
            choice = dialog.Choice;
        }
        finally
        {
            _closeChoiceOpen = false;
        }

        if (choice == CloseChoice.HideToTray)
        {
            HideToTray();
        }
        else if (choice == CloseChoice.Exit)
        {
            // The dialog showed the active-state warning, so the closing handler skips its own.
            _exitWarningShown = noSaveActive;
            try
            {
                Close();
            }
            finally
            {
                _exitWarningShown = false;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var surface = Palette.Themed(Palette.Surface, _darkMode);
        var line = Palette.Themed(Palette.Line, _darkMode);
        MenuGlyphs.DrawCard(e.Graphics, ControlCard, surface, line);
        MenuGlyphs.DrawCard(e.Graphics, SettingsCard, surface, line);
        MenuGlyphs.DrawCard(e.Graphics, FooterCard, surface, line);
        DrawStatusCard(e.Graphics, surface);
        using (var dot = new SolidBrush(ReadableAccent(_gameStatusColor)))
        {
            e.Graphics.FillEllipse(dot, GameStatusDot);
        }

        if (!_roundedByCompositor)
        {
            using var borderPen = new Pen(line);
            e.Graphics.DrawRectangle(
                borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    /// <summary>Rounded corners and the window edge, in the current theme's line color.</summary>
    private void ApplyWindowEdge()
    {
        if (IsHandleCreated)
        {
            _roundedByCompositor = NativeMethods.TryRoundCorners(
                Handle, Palette.Themed(Palette.Line, _darkMode));
        }
    }

    internal void SavePreview(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
        bitmap.Save(fullPath, ImageFormat.Png);
    }

    private void HandleShown(object? sender, EventArgs eventArgs)
    {
        _trayMenu?.SetWindowVisible(visible: true);
        UpdateHudVisibility();
        if (!_previewMode && !_hotkeyRegistered)
        {
            ActivityLog.Write($"the {ShortcutText} keyboard hook could not be installed");
            MessageBox.Show(this,
                $"The {ShortcutText} keyboard hook could not be installed.\n" +
                "The on-screen toggle remains available.",
                "Shortcut unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (!_previewMode && !_controllerRawInputRegistered &&
            _controllerShortcutService.Shortcut?.DeviceKind is
                ControllerDeviceKind.DualShock4 or ControllerDeviceKind.DualSense)
        {
            MessageBox.Show(this,
                "PlayStation controller input could not be registered.\n" +
                "The keyboard and Xbox shortcuts remain available.",
                "Controller shortcut unavailable",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        // A rejected endpoints.txt used to fall back to the built-in set in silence: the rule
        // then blocked something other than what the file asked for, with nothing on screen to
        // say so. The reason was only reachable through --diagnose.
        if (!_previewMode &&
            RockstarNetworks.BlockedConfigurationError is { } configurationError)
        {
            ActivityLog.Write($"endpoint configuration refused: {configurationError}");
            MessageBox.Show(this,
                $"{configurationError}\n\n" +
                "Correct the file in %LOCALAPPDATA%\\VaultLoop and restart VaultLoop, " +
                "or delete it to keep the built-in address set.",
                "Endpoint configuration ignored",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void HandleHotkeyPressed(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() => ToggleState(fromHotkey: true)));
        }
        catch (InvalidOperationException)
        {
            // The window closed between the handle check and BeginInvoke. The controller
            // poll timer does not wait for its callback to finish, so this event can be
            // raised while the window is being torn down.
        }
    }

    /// <summary>
    /// Explains a shortcut press the gate refused. Every condition it names is deliberate, but
    /// a press that toggles nothing and says nothing is indistinguishable from a keyboard that
    /// stopped working, and the two conditions a user hits — no administrator rights, and GTA
    /// not in the foreground — are both fixable in a few seconds once named.
    /// </summary>
    private void HandleHotkeyRefused(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _applying)
                {
                    return;
                }

                var reason = GetShortcutRefusalReason();
                if (reason is null)
                {
                    return;
                }
                ActivityLog.Write($"{ShortcutText} refused: {reason}");
                ShowStatusToast("SHORTCUT UNAVAILABLE", Palette.Yellow, reason);
            }));
        }
        catch (InvalidOperationException)
        {
            // The window closed between the handle check and BeginInvoke.
        }
    }

    /// <summary>
    /// The missing condition, or <c>null</c> when the press was refused by a race the user
    /// cannot act on — a mutation that finished in the meantime explains nothing.
    /// </summary>
    private string? GetShortcutRefusalReason()
    {
        if (!_isAdministrator)
        {
            return "VaultLoop must run as administrator. " +
                   "Select LAUNCH AS ADMIN, then approve the Windows prompt.";
        }
        if (!_stateKnown)
        {
            return "The Windows Firewall state is unavailable, so no-save cannot be changed.";
        }
        if (!_hasVerifiedForegroundGame)
        {
            return "A verified GTA V window must be in the foreground.";
        }
        return null;
    }

    private void RefreshRuntimeState(bool showErrors = false)
    {
        RefreshGameContext();
        RefreshState(showErrors);
    }

    private void SetGameStatus(string text, Color color)
    {
        _gameStatusLabel.Text = DisplayText.Sentence(text);
        // The status color goes to the dot beside the text. In particular, WAITING FOR GTA
        // is the red warning that no verified GTA process is running.
        _gameStatusColor = GetGameStatusTextColor(text, color);
        Invalidate(FooterCard);
        UpdateToggleAvailability();
    }

    internal static Color GetGameStatusTextColor(string text, Color requestedColor) =>
        text == "WAITING FOR GTA" ? Palette.HotPink : requestedColor;

    private string ShortcutText
    {
        get
        {
            var shortcut = _hotkeyHook.Shortcut;
            return ShortcutSettings.Format(shortcut.Modifiers, shortcut.Key);
        }
    }

    private void ConfigureShortcut()
    {
        _hotkeyHook.CapturingShortcut = true;
        try
        {
            var shortcut = _hotkeyHook.Shortcut;
            using var dialog = new ShortcutDialog(
                shortcut.Modifiers, shortcut.Key, _darkMode,
                _controllerShortcutService, _controllerShortcutService.Shortcut);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var newModifiers = dialog.ShortcutModifiers;
            var newKey = dialog.ShortcutKey;
            ShortcutSettings.Save(newModifiers, newKey);
            ControllerShortcutSettings.Save(dialog.ControllerShortcut);
            _hotkeyHook.Shortcut = (newModifiers, newKey);
            _controllerShortcutService.Shortcut = dialog.ControllerShortcut;
            _shortcutBadge.Text = ShortcutText;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The shortcut could not be saved:\n{exception.Message}",
                "Shortcut error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _controllerShortcutService.CancelCapture();
            _hotkeyHook.CapturingShortcut = false;
        }
    }

    private void LaunchAsAdministrator()
    {
        if (_isAdministrator)
        {
            return;
        }

        try
        {
            Program.RelaunchElevated(null, IntPtr.Zero);
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"VaultLoop could not be launched as administrator:\n{exception.Message}",
                "Administrator launch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleHudVisibility()
    {
        _hudEnabled = !_hudEnabled;
        _hudVisibilityButton.Text = _hudEnabled ? "HUD ON" : "HUD OFF";
        _hudVisibilityButton.SwitchOn = _hudEnabled;
        _hudVisibilityButton.AccessibleName =
            _hudEnabled ? "Hide the no-save HUD" : "Show the no-save HUD";
        _trayMenu?.SetHudEnabled(_hudEnabled);
        UpdateHudVisibility();
        try
        {
            HudSettings.Save(_hudEnabled);
        }
        catch (Exception exception)
        {
            ShowFromTray();
            MessageBox.Show(this,
                $"The HUD changed for this session but could not be saved:\n{exception.Message}",
                "HUD preference", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateHudVisibility()
    {
        if (_miniHud is null)
        {
            return;
        }

        if (ShouldShowHud(_hudEnabled, _hasVerifiedForegroundGame))
        {
            if (!_miniHud.Visible)
            {
                _miniHud.ShowOnActiveScreen();
            }
        }
        else if (_miniHud.Visible)
        {
            _miniHud.Hide();
        }
    }

    internal static bool ShouldShowHud(
        bool hudEnabled, bool hasVerifiedForegroundGame) =>
        hudEnabled && hasVerifiedForegroundGame;

    private void SetTheme(AppTheme theme)
    {
        if (theme == _theme)
        {
            return;
        }

        _theme = theme;
        _darkMode = theme != AppTheme.Light;
        Palette.Oled = theme == AppTheme.Oled;
        _themeController.ApplyTheme(_darkMode);
        ShowSelectedTheme();
        ApplyWindowEdge();
        // The HUD is never light, but it does follow the OLED black.
        _miniHud?.RefreshTheme();
        try
        {
            ThemeSettings.Save(theme);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The theme changed for this session but could not be saved:\n{exception.Message}",
                "Theme preference", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleState(bool fromHotkey = false)
    {
        if (!_stateKnown)
        {
            return;
        }
        ApplyState(GetToggledEnabledState(_firewallState), fromHotkey);
    }

    internal static bool GetToggledEnabledState(FirewallRuleState currentState) =>
        currentState == FirewallRuleState.Inactive;

    private void ApplyState(bool enabled, bool fromHotkey = false)
    {
        if (!_stateKnown || _applying || _firewall is null)
        {
            return;
        }

        string? gamePath;
        IntPtr requestedForegroundWindow;
        try
        {
            gamePath = ResolveGamePath(enabled, fromHotkey, out requestedForegroundWindow);
        }
        catch (Exception exception)
        {
            // Nothing was written to the firewall, so the displayed state still holds and
            // there is nothing to resynchronize.
            ReportMutationFailure(exception, fromHotkey);
            return;
        }

        if (!Program.IsRunningAsAdministrator())
        {
            try
            {
                Program.RelaunchElevated(gamePath, requestedForegroundWindow);
                Close();
            }
            catch (Exception exception)
            {
                ReportMutationFailure(exception, fromHotkey);
            }
            return;
        }

        RunExclusive(
            () => _firewall.SetNoSaveEnabled(enabled, gamePath),
            () =>
            {
                ActivityLog.Write(enabled
                    ? $"no-save enabled from {(fromHotkey ? "shortcut" : "window")} for {gamePath}"
                    : $"no-save disabled from {(fromHotkey ? "shortcut" : "window")}");
                SetDisplayedState(enabled);
                if (enabled)
                {
                    if (fromHotkey)
                    {
                        ShowStatusToast("NO-SAVE ACTIVE", Palette.HotPink);
                    }
                }
                else
                {
                    ShowStatusToast("NO-SAVE INACTIVE", Palette.Acid,
                        "Check the save result in GTA. Replay outcomes are not verified by VaultLoop.");
                }
            },
            exception => ReportMutationFailure(exception, fromHotkey));
    }

    /// <summary>
    /// Resolves the executable the rule must name and arms both shortcuts on the window that
    /// was verified. Throws when no-save is being enabled without a verified game — the one
    /// condition that has to stop a mutation before it starts.
    /// </summary>
    private string? ResolveGamePath(
        bool enabled, bool fromHotkey, out IntPtr requestedForegroundWindow)
    {
        requestedForegroundWindow = IntPtr.Zero;
        if (!enabled)
        {
            return null;
        }

        if (fromHotkey)
        {
            if (!GameProcessService.TryGetVerifiedForegroundGame(
                    out var foregroundPath, out var liveForegroundWindow) ||
                !GameProcessService.IsCurrentForegroundWindow(liveForegroundWindow))
            {
                throw new InvalidOperationException(
                    "GTA V must remain in the foreground to use the shortcut.");
            }
            requestedForegroundWindow = liveForegroundWindow;
            _verifiedGamePath = foregroundPath;
            _hotkeyHook.Arm(liveForegroundWindow);
            _controllerShortcutService.Arm(liveForegroundWindow);
            return foregroundPath;
        }

        if (GameProcessService.TryFindVerifiedRunningGame(out var runningPath))
        {
            _verifiedGamePath = runningPath;
            return runningPath;
        }

        throw new InvalidOperationException(
            "Start a verified copy of GTA V before enabling no-save.");
    }

    private void ReportMutationFailure(Exception exception, bool fromHotkey)
    {
        ActivityLog.Write("no-save change failed", exception);
        if (fromHotkey)
        {
            ShowStatusToast("NO-SAVE ERROR", Palette.Yellow, exception.Message);
        }
        else
        {
            MessageBox.Show(this, exception.Message, "Firewall error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Runs a firewall mutation on a thread-pool thread with the toggle locked and the refresh
    /// loop invalidated, then applies its outcome back on the UI thread.
    /// </summary>
    /// <remarks>
    /// The mutation used to run inline. Confirming a rule costs seven polls with an
    /// exponential backoff — up to about two seconds, and twice that when an activation fails
    /// and rolls back — during which the message loop was blocked: the window stopped
    /// repainting, the wait cursor never appeared, and queued raw input messages were not
    /// pumped. Only the firewall call moves; the decision of what to write, and every UI
    /// update, still happen on the UI thread.
    /// </remarks>
    private void RunExclusive(
        Action mutation, Action onSuccess, Action<Exception> reportFailure)
    {
        if (_applying || _firewall is null)
        {
            return;
        }

        _applying = true;
        Interlocked.Increment(ref _runtimeRefreshVersion);
        _toggle.Enabled = false;
        UseWaitCursor = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Exception? failure = null;
            try
            {
                mutation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            CompleteExclusive(failure, onSuccess, reportFailure);
        });
    }

    /// <summary>
    /// Applies a finished mutation back on the UI thread, resynchronizing the display from the
    /// firewall itself when it failed — a failed mutation leaves the real state unknown. The
    /// window can be gone by the time the mutation returns, so the exclusive flag is released
    /// on every path: left set, it would keep the toggle and both shortcuts inert.
    /// </summary>
    private void CompleteExclusive(
        Exception? failure, Action onSuccess, Action<Exception> reportFailure)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            _applying = false;
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed)
                {
                    _applying = false;
                    return;
                }

                try
                {
                    if (failure is null)
                    {
                        onSuccess();
                    }
                    else
                    {
                        reportFailure(failure);
                        ResynchronizeState();
                    }
                }
                finally
                {
                    UseWaitCursor = false;
                    _applying = false;
                    UpdateToggleAvailability();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window closed between the handle check and BeginInvoke.
            _applying = false;
        }
    }

    private void ResynchronizeState()
    {
        try
        {
            ApplyFirewallState(_firewall!.GetState());
        }
        catch
        {
            SetUnknownState();
        }
    }

    private void RefreshState(bool showErrors = false)
    {
        if (_applying || _firewall is null)
        {
            return;
        }

        try
        {
            ApplyFirewallState(_firewall.GetState());
        }
        catch (Exception exception)
        {
            SetUnknownState();
            if (showErrors)
            {
                MessageBox.Show(this, exception.Message, "Unable to read Windows Firewall",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void ApplyFirewallState(FirewallRuleState state)
    {
        if (state == FirewallRuleState.Invalid)
        {
            SetInvalidState();
            return;
        }
        SetDisplayedState(state == FirewallRuleState.Active);
    }

    private void SetDisplayedState(bool enabled)
    {
        if (!enabled)
        {
            _blockObservation.Reset();
        }
        _stateKnown = true;
        _firewallState = enabled ? FirewallRuleState.Active : FirewallRuleState.Inactive;
        _toggle.IsRecoveryMode = false;
        _toggle.IsStateKnown = true;
        _toggle.Checked = enabled;
        UpdateToggleAvailability();
        _miniHud?.SetState(enabled);
        RenderStatus(
            enabled ? Palette.HotPink : Palette.Acid,
            enabled ? "ACTIVE" : "INACTIVE",
            enabled ? "ROCKSTAR LINK BLOCKED" : "ROCKSTAR LINK ONLINE",
            enabled ? "No-save active" : "No-save inactive");
    }

    private void SetInvalidState()
    {
        _blockObservation.Reset();
        _stateKnown = true;
        _firewallState = FirewallRuleState.Invalid;
        _toggle.IsStateKnown = false;
        _toggle.IsRecoveryMode = true;
        UpdateToggleAvailability();
        _miniHud?.SetState(null);
        RenderStatus(Palette.Yellow, "INVALID", "CLICK RESTORE, THEN RETRY",
            "Restore an invalid VaultLoop firewall rule");
    }

    private void SetUnknownState()
    {
        _blockObservation.Reset();
        _stateKnown = false;
        _firewallState = FirewallRuleState.Invalid;
        _toggle.IsRecoveryMode = false;
        _toggle.IsStateKnown = false;
        UpdateToggleAvailability();
        _miniHud?.SetState(null);
        RenderStatus(Palette.Yellow, "UNKNOWN", "FIREWALL STATE UNAVAILABLE",
            "No-save state unknown");
    }

    /// <summary>Paints the status card and its accessible label for one displayed state.</summary>
    private void RenderStatus(
        Color color, string title, string detail, string toggleAccessibleName)
    {
        _fromStateColor = DisplayedStateColor;
        _stateColor = color;
        _stateBlend.AnimateFrom(0F, 1F, Motion.Standard);
        _stateMark = MenuGlyphs.MarkFor(title);
        _stateTitle.Text = DisplayText.Sentence(title);
        _stateDetail.Text = DisplayText.Sentence(detail);
        _toggle.AccessibleName = toggleAccessibleName;
        _trayMenu?.SetStatus(title, color);
        _trayMenu?.SetStatusDetail(detail);
        if (_trayIcon is not null)
        {
            _trayIcon.Text = $"VaultLoop - No-save {title.ToLowerInvariant()}";
        }
        // Children too: the HUD button paints the status card behind its rounded corners,
        // and would keep the previous state's tint there.
        Invalidate(true);
    }

    /// <summary>
    /// Keeps the no-save control available for deactivation and invalid-rule recovery, while
    /// requiring a verified running game for activation. This is the one source of truth used
    /// after game-context, firewall-state, and mutation updates.
    /// </summary>
    private void UpdateToggleAvailability()
    {
        var enabled = ShouldEnableNoSaveToggle(
            _isAdministrator, _applying, _stateKnown, _firewallState,
            _verifiedGamePath is not null);
        if (_toggle.Enabled != enabled)
        {
            _toggle.Enabled = enabled;
            _toggle.Invalidate();
        }
    }

    internal static bool ShouldEnableNoSaveToggle(
        bool isAdministrator, bool applying, bool stateKnown,
        FirewallRuleState firewallState, bool hasVerifiedRunningGame)
    {
        if (!isAdministrator || applying || !stateKnown)
        {
            return false;
        }

        return firewallState != FirewallRuleState.Inactive || hasVerifiedRunningGame;
    }

    private static StatusToastForm? _activeToast;

    /// <summary>
    /// Shows one toast at a time. They all open in the same corner, so a second one used to
    /// land exactly on the first and the two read as one garbled message.
    /// </summary>
    private static void ShowStatusToast(string title, Color color, string? detail = null)
    {
        _activeToast?.Close();
        var toast = new StatusToastForm(title, detail ?? "", color);
        toast.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_activeToast, toast))
            {
                _activeToast = null;
            }
            toast.Dispose();
        };
        _activeToast = toast;
        toast.Show();
    }

    private void HandleClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _refreshTimer.Stop();
        if (_previewMode || _firewall is null)
        {
            return;
        }

        if (eventArgs.CloseReason == CloseReason.UserClosing &&
            _firewallState == FirewallRuleState.Active && !_exitWarningShown)
        {
            var closeAnswer = MessageBox.Show(this,
                "No-save is ACTIVE.\nClosing the application will restore the Rockstar link.\n\nClose now?",
                "No-save active", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (closeAnswer == DialogResult.No)
            {
                eventArgs.Cancel = true;
                _refreshTimer.Start();
                return;
            }
        }

        if (!Program.IsRunningAsAdministrator())
        {
            if (_firewallState != FirewallRuleState.Inactive)
            {
                try
                {
                    Program.RelaunchElevated(null, IntPtr.Zero);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this,
                        $"The Rockstar link could not be restored:\n{exception.Message}",
                        "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    eventArgs.Cancel = true;
                    _refreshTimer.Start();
                }
            }
            return;
        }

        try
        {
            _firewall.SetNoSaveEnabled(false);
            if (_firewallState == FirewallRuleState.Active)
            {
                ActivityLog.Write("rule restored on close");
            }
        }
        catch (Exception exception)
        {
            ActivityLog.Write("restore on close failed", exception);
            if (eventArgs.CloseReason != CloseReason.UserClosing)
            {
                return;
            }
            var answer = MessageBox.Show(this,
                $"The Rockstar link could not be restored:\n{exception.Message}\n\nExit anyway?",
                "Restore failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.No)
            {
                eventArgs.Cancel = true;
                _refreshTimer.Start();
            }
        }
    }

}
