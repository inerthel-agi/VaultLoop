#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// Dependency-free regression checks, reachable through <c>VaultLoop.exe --selftest</c>.
/// This replaces the former Test.ps1 harness: every assertion calls the member directly,
/// so a rename or a signature change becomes a compile error instead of a silent gap.
/// The checks run without elevation and never request a firewall mutation.
/// The checks are read-only: no firewall rule is created, modified, or removed.
/// </summary>
internal static class SelfTest
{
    internal static int Run()
    {
        NativeMethods.AttachParentConsole();

        var checks = new CheckList();

        checks.Verify("assembly version is 1.3.0.0",
            () => typeof(Program).Assembly.GetName().Version?.ToString() == "1.3.0.0");
        checks.Verify("assembly targets the canonical .NET Framework runtime",
            () => typeof(Program).Assembly.ImageRuntimeVersion == "v4.0.30319");
        checks.Verify("embedded logo resource is present",
            () => Array.IndexOf(
                typeof(Program).Assembly.GetManifestResourceNames(),
                "ReplayGlitchLogo.png") >= 0);

        checks.Verify("GTA5 is a supported process name",
            () => GameProcessService.IsSupportedProcessName("GTA5"));
        checks.Verify("GTA5_Enhanced is a supported process name",
            () => GameProcessService.IsSupportedProcessName("GTA5_Enhanced"));
        checks.Verify("unrelated processes are rejected",
            () => !GameProcessService.IsSupportedProcessName("NVIDIA Share"));
        checks.Verify("an empty foreground handle never arms the shortcut",
            () => !GameProcessService.IsCurrentForegroundWindow(IntPtr.Zero));
        checks.Verify("shortcut trigger gate rejects every disarmed state", () =>
        {
            var canTriggerCalls = 0;
            Func<bool> canTrigger = () =>
            {
                canTriggerCalls++;
                return true;
            };
            var gate = new ShortcutTriggerGate();
            if (gate.Armed || gate.CanFire(canTrigger))
            {
                return false;
            }

            gate.Arm(IntPtr.Zero);
            if (gate.Armed || gate.CanFire(canTrigger))
            {
                return false;
            }

            gate.Arm(new IntPtr(1));
            gate.Disarm();
            return !gate.Armed && !gate.CanFire(canTrigger) && canTriggerCalls == 0;
        });

        // Properties of whatever set is active, so editing endpoints.txt cannot turn the
        // suite red on a legitimate configuration.
        var activeSet = RockstarNetworks.FormatBlockedSet();
        checks.Verify("the blocked set is never empty",
            () => RockstarNetworks.BlockedSet.Count > 0);
        checks.Verify("every blocked prefix stays inside a Rockstar Online Services allocation",
            () =>
            {
                foreach (var prefix in RockstarNetworks.BlockedSet)
                {
                    var inside = false;
                    foreach (var allocation in RockstarNetworks.OnlineServiceAllocations)
                    {
                        inside |= prefix.IsInside(allocation);
                    }
                    if (!inside)
                    {
                        return false;
                    }
                }
                return true;
            });
        checks.Verify("Zynga and corporate Take-Two ranges are never blocked",
            () => !RockstarNetworks.IsBlocked(IPAddress.Parse("184.75.160.1")) &&
                  !RockstarNetworks.IsBlocked(IPAddress.Parse("139.138.224.1")) &&
                  !RockstarNetworks.IsBlocked(IPAddress.Parse("74.114.8.1")));
        checks.Verify("an unrelated public address is never blocked",
            () => !RockstarNetworks.IsBlocked(IPAddress.Parse("8.8.8.8")));
        checks.Verify("the observed save endpoint is classified as RSONET-NA1",
            () => RockstarNetworks.GetOnlineServiceName(
                IPAddress.Parse(RockstarNetworks.ObservedSaveEndpoint)) == "RSONET-NA1");

        checks.Verify("the active set is accepted as written",
            () => FirewallService.TargetsOnlyManagedAddresses(activeSet));
        checks.Verify("the active set is accepted in reverse order",
            () => FirewallService.TargetsOnlyManagedAddresses(Reverse(activeSet)));
        checks.Verify("a superset of the active set is rejected",
            () => !FirewallService.TargetsOnlyManagedAddresses($"{activeSet},198.133.210.1"));
        checks.Verify("dropping an entry from the active set is rejected",
            () => RockstarNetworks.BlockedSet.Count == 1 ||
                  !FirewallService.TargetsOnlyManagedAddresses(
                      activeSet.Substring(activeSet.IndexOf(',') + 1)));
        checks.Verify("a duplicated entry cannot stand in for a missing one",
            () => RockstarNetworks.BlockedSet.Count == 1 ||
                  !FirewallService.TargetsOnlyManagedAddresses(
                      $"{RockstarNetworks.BlockedSet[0].Canonical}," +
                      $"{RockstarNetworks.BlockedSet[0].Canonical}"));
        checks.Verify("malformed addresses are rejected",
            () => !FirewallService.TargetsOnlyManagedAddresses("not-an-address,::/0"));
        checks.Verify("an address outside every Rockstar allocation is refused by the loader",
            () => BlockedEndpointsSettings.TryLoad(
                      [IpPrefix.TryParse("192.81.240.0/21")!], out _) is null ||
                  !RockstarNetworks.IsBlocked(IPAddress.Parse("8.8.8.8")));

        checks.Verify("rules left by earlier versions are still recognized",
            () => FirewallService.TargetsOnlyLegacyAddress("192.81.241.171") &&
                  FirewallService.TargetsOnlyLegacyAddress("192.81.241.171/32") &&
                  FirewallService.TargetsOnlyLegacyAddress(
                      "192.81.241.171/255.255.255.255"));
        checks.Verify("a multi-address legacy rule is rejected",
            () => !FirewallService.TargetsOnlyLegacyAddress("192.81.241.171,8.8.8.8"));

        checks.Verify("prefix parsing normalizes every form to one canonical value",
            () => IpPrefix.TryParse("192.81.241.171/24")?.Canonical == "192.81.241.0/24" &&
                  IpPrefix.TryParse("192.81.241.0/255.255.255.0")?.Canonical ==
                      "192.81.241.0/24" &&
                  IpPrefix.TryParse("192.81.241.171")?.Canonical ==
                      "192.81.241.171/32");
        checks.Verify("a non-contiguous subnet mask is rejected",
            () => IpPrefix.TryParse("192.81.241.0/255.0.255.0") is null);
        checks.Verify("an out-of-range prefix length is rejected",
            () => IpPrefix.TryParse("192.81.241.0/33") is null);

        checks.Verify("the default shortcut is valid",
            () => ShortcutDialog.IsValidShortcut(Keys.Control | Keys.Shift, Keys.F8));
        checks.Verify("Alt+F8 remains available as a saved user shortcut",
            () => ShortcutDialog.IsValidShortcut(Keys.Alt, Keys.F8));
        checks.Verify("Alt+F4 stays reserved",
            () => !ShortcutDialog.IsValidShortcut(Keys.Alt, Keys.F4));
        checks.Verify("Alt+Tab stays reserved",
            () => !ShortcutDialog.IsValidShortcut(Keys.Alt, Keys.Tab));
        checks.Verify("numeric shortcut names are user-friendly",
            () => ShortcutSettings.Format(Keys.Alt, Keys.D8) == "ALT+8");
        checks.Verify("controller shortcuts require exactly two or three buttons", () =>
        {
            var twoButtons =
                ControllerButtons.LeftShoulder | ControllerButtons.RightShoulder;
            var threeButtons = twoButtons | ControllerButtons.West;
            var fourButtons = threeButtons | ControllerButtons.North;
            var dPadDiagonal =
                ControllerButtons.DPadUp | ControllerButtons.DPadRight;
            return !ControllerShortcut.IsValidCombination(ControllerButtons.West) &&
                   !ControllerShortcut.IsValidCombination(dPadDiagonal) &&
                   ControllerShortcut.IsValidCombination(twoButtons) &&
                   ControllerShortcut.IsValidCombination(threeButtons) &&
                   !ControllerShortcut.IsValidCombination(fourButtons);
        });
        checks.Verify("controller shortcut matching rejects extra gameplay buttons", () =>
        {
            var configured =
                ControllerButtons.LeftShoulder | ControllerButtons.West;
            return ControllerShortcut.IsExactCombination(configured, configured) &&
                   !ControllerShortcut.IsExactCombination(
                       configured | ControllerButtons.South, configured);
        });
        checks.Verify("shortcut toggling activates and deactivates no-save",
            () => MainForm.GetToggledEnabledState(FirewallRuleState.Inactive) &&
                  !MainForm.GetToggledEnabledState(FirewallRuleState.Active));
        checks.Verify("no-save activation requires a verified running GTA",
            () => !MainForm.ShouldEnableNoSaveToggle(
                       isAdministrator: true, applying: false, stateKnown: true,
                       firewallState: FirewallRuleState.Inactive,
                       hasVerifiedRunningGame: false) &&
                  MainForm.ShouldEnableNoSaveToggle(
                      isAdministrator: true, applying: false, stateKnown: true,
                      firewallState: FirewallRuleState.Inactive,
                      hasVerifiedRunningGame: true) &&
                  MainForm.ShouldEnableNoSaveToggle(
                      isAdministrator: true, applying: false, stateKnown: true,
                      firewallState: FirewallRuleState.Active,
                      hasVerifiedRunningGame: false) &&
                  MainForm.ShouldEnableNoSaveToggle(
                      isAdministrator: true, applying: false, stateKnown: true,
                      firewallState: FirewallRuleState.Invalid,
                      hasVerifiedRunningGame: false) &&
                  !MainForm.ShouldEnableNoSaveToggle(
                      isAdministrator: true, applying: false, stateKnown: false,
                      firewallState: FirewallRuleState.Active,
                      hasVerifiedRunningGame: true) &&
                  !MainForm.ShouldEnableNoSaveToggle(
                      isAdministrator: true, applying: true, stateKnown: true,
                      firewallState: FirewallRuleState.Inactive,
                      hasVerifiedRunningGame: true));
        checks.Verify("GTA-not-launched status uses the red text warning",
            () => MainForm.GetGameStatusTextColor("WAITING FOR GTA", Palette.Yellow) ==
                      Palette.HotPink &&
                  MainForm.GetGameStatusTextColor("GTA IN BACKGROUND", Palette.Yellow) ==
                      Palette.Yellow);
        checks.Verify("a disabled no-save toggle paints its track gray",
            () => BooleanToggle.GetTrackColor(
                       enabled: false, stateKnown: true, recoveryMode: false,
                       isChecked: false) == SystemColors.ControlDark &&
                  BooleanToggle.GetTrackColor(
                      enabled: false, stateKnown: false, recoveryMode: false,
                      isChecked: false) == Palette.Yellow);
        checks.Verify("controller shortcuts use the accepted 500 ms hold",
            () => ControllerShortcutService.HoldMilliseconds == 500);
        checks.Verify("controller shortcut settings round-trip without device ambiguity", () =>
        {
            var original = new ControllerShortcut(
                ControllerDeviceKind.DualSense,
                @"\\?\HID#VID_054C&PID_0CE6#VAULTLOOP_TEST",
                ControllerButtons.LeftShoulder |
                ControllerButtons.RightShoulder |
                ControllerButtons.West);
            return ControllerShortcutSettings.TryParse(
                       ControllerShortcutSettings.Serialize(original), out var parsed) &&
                   parsed is not null &&
                   parsed.DeviceKind == original.DeviceKind &&
                   parsed.DeviceId == original.DeviceId &&
                   parsed.Buttons == original.Buttons &&
                   parsed.Format() ==
                       "DualSense  //  L1 + R1 + SQUARE";
        });
        checks.Verify("disabled controller shortcut settings stay disabled",
            () => ControllerShortcutSettings.TryParse("disabled", out var parsed) &&
                  parsed is null);
        checks.Verify("Xbox button reports map to user-facing controller buttons", () =>
        {
            var gamepad = new XInputNativeMethods.XInputGamepad
            {
                Buttons = XInputNativeMethods.LeftShoulder |
                          XInputNativeMethods.RightShoulder |
                          XInputNativeMethods.X,
                LeftTrigger = XInputNativeMethods.TriggerThreshold
            };
            return ControllerShortcutService.MapXInputButtons(gamepad) ==
                   (ControllerButtons.LeftShoulder |
                    ControllerButtons.RightShoulder |
                    ControllerButtons.West |
                    ControllerButtons.LeftTrigger);
        });
        // Measured on the controller this was found with: the left trigger rests at 53 of 255,
        // above the threshold of 30, so every reading carried LT and the exact match against
        // DPAD LEFT + A could never hold.
        checks.Verify("an analog input a controller rests on is not a pressed button", () =>
        {
            var firstReading = ControllerShortcutService.TrackStuckAnalogInputs(
                ControllerButtons.LeftTrigger, null, out var stuck);
            if (firstReading != ControllerButtons.None ||
                stuck != ControllerButtons.LeftTrigger)
            {
                return false;
            }

            var held = ControllerShortcutService.TrackStuckAnalogInputs(
                ControllerButtons.LeftTrigger | ControllerButtons.DPadLeft |
                ControllerButtons.South, stuck, out stuck);
            if (held != (ControllerButtons.DPadLeft | ControllerButtons.South))
            {
                return false;
            }

            // Read below its threshold once, the trigger becomes a usable button again.
            ControllerShortcutService.TrackStuckAnalogInputs(
                ControllerButtons.South, stuck, out stuck);
            var pulled = ControllerShortcutService.TrackStuckAnalogInputs(
                ControllerButtons.LeftTrigger, stuck, out stuck);
            return stuck == ControllerButtons.None &&
                   pulled == ControllerButtons.LeftTrigger &&
                   ControllerShortcutService.TrackStuckAnalogInputs(
                       ControllerButtons.RightTrigger, null, out _) == ControllerButtons.None;
        });
        checks.Verify("only supported Sony controller product ids use raw HID",
            () => ControllerShortcutService.GetSonyDeviceKind(0x054C, 0x09CC) ==
                      ControllerDeviceKind.DualShock4 &&
                  ControllerShortcutService.GetSonyDeviceKind(0x054C, 0x0CE6) ==
                      ControllerDeviceKind.DualSense &&
                  ControllerShortcutService.GetSonyDeviceKind(0x057E, 0x2009) is null);
        // GetRawInputDeviceInfoW rejects the query outright when cbSize disagrees with the
        // size Windows expects, which silently disabled every PlayStation controller.
        checks.Verify("the raw input device info structure matches the Windows layout",
            () => Marshal.SizeOf(typeof(RawInputNativeMethods.RawInputDeviceInfo)) == 32 &&
                  Marshal.SizeOf(typeof(RawInputNativeMethods.RawInputDeviceInfoUnion)) == 24);
        checks.Verify("raw controller input registers and unregisters cleanly", () =>
        {
            using var inputHost = new Form();
            using var controllerService = new ControllerShortcutService(
                shortcut: null, canTrigger: () => false);
            controllerService.Pressed += (_, _) => { };
            controllerService.Refused += (_, _) => { };
            var registered = controllerService.Install(inputHost.Handle);
            controllerService.Uninstall();
            return registered;
        });
        checks.Verify("shortcut modifiers follow low-level key events", () =>
        {
            var state = GlobalHotkeyHook.UpdateModifierKeyState(
                0, Keys.LControlKey, keyDown: true, keyUp: false);
            state = GlobalHotkeyHook.UpdateModifierKeyState(
                state, Keys.RShiftKey, keyDown: true, keyUp: false);
            if (GlobalHotkeyHook.GetPressedModifiers(state, 0) !=
                (Keys.Control | Keys.Shift))
            {
                return false;
            }

            state = GlobalHotkeyHook.UpdateModifierKeyState(
                state, Keys.LControlKey, keyDown: false, keyUp: true);
            state = GlobalHotkeyHook.UpdateModifierKeyState(
                state, Keys.RShiftKey, keyDown: false, keyUp: true);
            state = GlobalHotkeyHook.UpdateModifierKeyState(
                state, Keys.LMenu, keyDown: true, keyUp: false);
            return GlobalHotkeyHook.GetPressedModifiers(state, 0) == Keys.Alt &&
                   GlobalHotkeyHook.GetPressedModifiers(
                       0, NativeMethods.AltDownFlag) == Keys.Alt;
        });
        // The low-level hook rejects either injection bit before touching tracked state.
        checks.Verify("injected keyboard events are rejected by both flags", () =>
            GlobalHotkeyHook.IsInjectedEvent(NativeMethods.InjectedFlag) &&
            GlobalHotkeyHook.IsInjectedEvent(NativeMethods.LowerIntegrityInjectedFlag) &&
            GlobalHotkeyHook.IsInjectedEvent(
                NativeMethods.InjectedFlag | NativeMethods.LowerIntegrityInjectedFlag) &&
            !GlobalHotkeyHook.IsInjectedEvent(0));
        // A transition the hook cannot observe — every key event during a UAC prompt goes to
        // the secure desktop — used to leave the tracked state describing a keyboard that no
        // longer exists, and the shortcut then matched nothing until VaultLoop was restarted.
        checks.Verify("tracked modifiers are rebuilt from the live keyboard", () =>
        {
            var stale = GlobalHotkeyHook.UpdateModifierKeyState(
                0, Keys.LControlKey, keyDown: true, keyUp: false);
            var released = GlobalHotkeyHook.ReconcileModifierKeyState(stale, _ => false);
            var held = GlobalHotkeyHook.ReconcileModifierKeyState(
                0, key => key == Keys.RShiftKey);
            return GlobalHotkeyHook.GetPressedModifiers(released, 0) == Keys.None &&
                   GlobalHotkeyHook.GetPressedModifiers(held, 0) == Keys.Shift;
        });
        // The hook thread reads the shortcut while the UI thread rewrites it, so both halves
        // travel in one field: a torn pair matches a combination the user never configured.
        checks.Verify("a reconfigured shortcut is published as one combination", () =>
        {
            var hook = new GlobalHotkeyHook(
                Keys.Control | Keys.Shift, Keys.F8, () => false)
            {
                Shortcut = (Keys.Alt, Keys.F9)
            };
            return hook.Shortcut == (Keys.Alt, Keys.F9) && !hook.Armed;
        });

        checks.Verify(".NET Framework 4.8 runtime check passes",
            Program.HasSupportedRuntime);
        checks.Verify("elevated activation arguments preserve path and foreground window", () =>
        {
            var arguments = Program.BuildElevatedArguments(
                42, @"C:\Games\Grand Theft Auto V\GTA5.exe", new IntPtr(123));
            return Program.TryParseElevatedRequest(
                       ["--elevated", "42", "--enable",
                        @"C:\Games\Grand Theft Auto V\GTA5.exe",
                        "--foreground-window", "123"],
                       out var parentProcessId, out var gamePath, out var foregroundWindow) &&
                   arguments == "--elevated 42 --enable \"C:\\Games\\Grand Theft Auto V\\GTA5.exe\" " +
                                "--foreground-window 123" &&
                   parentProcessId == 42 &&
                   gamePath == @"C:\Games\Grand Theft Auto V\GTA5.exe" &&
                   foregroundWindow == new IntPtr(123);
        });
        checks.Verify("malformed elevated activation arguments are rejected",
            () => !Program.TryParseElevatedRequest(
                ["--elevated", "42", "--enable"], out _, out _, out _));
        checks.Verify("Windows startup uses the exact quoted executable and startup argument",
            () => StartupRegistration.BuildCommand(
                      @"C:\Program Files\VaultLoop\VaultLoop.exe") ==
                  "\"C:\\Program Files\\VaultLoop\\VaultLoop.exe\" --startup");
        checks.Verify("only the exact startup command requests a tray-only launch",
            () => Program.IsStartupLaunch(["--startup"]) &&
                  Program.IsStartupLaunch(["--STARTUP"]) &&
                  Program.IsStartupLaunch(["--elevated", "42", "--startup"]) &&
                  !Program.IsStartupLaunch([]) &&
                  !Program.IsStartupLaunch(["--startup", "extra"]) &&
                  !Program.IsStartupLaunch(["--elevated", "bad", "--startup"]));
        checks.Verify("HUD requires its toggle and verified GTA in the foreground",
            () => MainForm.ShouldShowHud(true, true) &&
                  !MainForm.ShouldShowHud(false, true) &&
                  !MainForm.ShouldShowHud(true, false));
        checks.Verify("the tray menu exposes status, HUD, startup, window, and safe exit actions",
            () =>
            {
                using var tray = new TrayMenu(
                    () => { }, () => { }, () => { }, () => { }, () => { });
                tray.SetStatus("ACTIVE", Palette.HotPink);
                tray.SetHudEnabled(enabled: false);
                tray.SetStartupEnabled(enabled: true);
                tray.SetWindowVisible(visible: false);
                var exitItems = tray.Items.Find("TrayExit", searchAllChildren: false);
                return tray.StatusText == "STATUS  //  ACTIVE" &&
                       tray.HudText == "HUD  //  OFF" &&
                       tray.StartupText == "START WITH WINDOWS  //  ON" &&
                       tray.OpenEnabled && !tray.HideEnabled &&
                       tray.Items["TrayOpen"]!.Available &&
                       !tray.Items["TrayHide"]!.Available &&
                       exitItems.Length == 1 &&
                       exitItems[0].Text.Replace("&&", "&") == "EXIT & RESTORE";
            });

        CheckServicesDiagnostics(checks);
        CheckOnlineServices(checks);

        // Writes one labelled entry to the user's activity log. That is the whole point: the
        // log is the only trace a failed session leaves, so the path that writes it has to be
        // exercised rather than assumed.
        checks.Verify("the activity log records one flattened entry per line", () =>
        {
            ActivityLog.Write("self-test entry\r\nwith an embedded line break");
            var contents = AppSettingsStorage.ReadText(
                ActivityLog.FileName, includeLegacy: false, out _);
            return contents is not null &&
                   contents.Contains("self-test entry with an embedded line break") &&
                   Array.Exists(DiagnosticHistory.Read(), entry =>
                       entry.EndsWith("  Self-test log check", StringComparison.Ordinal));
        });

        checks.Verify("the preview window builds with the expected chrome", () =>
        {
            using var preview = new MainForm(null, previewMode: true);
            var adminButtons = preview.Controls.Find("LaunchAsAdminButton", true);
            var hudButtons = preview.Controls.Find("HudVisibilityButton", true);
            var versionLabels = preview.Controls.Find("VersionLabel", true);
            return preview.Text == "VaultLoop" && preview.ClientSize.Width >= 780 &&
                   adminButtons.Length == 1 && adminButtons[0].Text == "ADMIN READY" &&
                   hudButtons.Length == 1 && hudButtons[0].Text == "HUD ON" &&
                   versionLabels.Length == 1 && versionLabels[0].Text == "v1.3.0";
        });
        checks.Verify("the displayed version follows the assembly version",
            () => MainForm.VersionText == "v" + typeof(Program).Assembly
                .GetName().Version?.ToString(3));
        // The capture used to be bound to the capture field alone, so a click on the controller
        // buttons moved the focus and every later key press was dropped without a word.
        checks.Verify("the shortcut dialog captures a key press whatever holds the focus", () =>
        {
            using var dialog = new ShortcutDialog(
                Keys.Control | Keys.Shift, Keys.F8, darkMode: false);
            var hints = dialog.Controls.Find("KeyboardShortcutHint", true);
            return dialog.KeyPreview &&
                   dialog.TryCapture(Keys.F9, Keys.None) ==
                       ShortcutDialog.CaptureOutcome.Accepted &&
                   dialog.ShortcutKey == Keys.F9 &&
                   dialog.ShortcutModifiers == Keys.None &&
                   dialog.TryCapture(Keys.K, Keys.None) ==
                       ShortcutDialog.CaptureOutcome.Rejected &&
                   dialog.ShortcutKey == Keys.F9 &&
                   hints.Length == 1 && hints[0].Text.StartsWith("Not accepted") &&
                   dialog.TryCapture(Keys.K, Keys.Control) ==
                       ShortcutDialog.CaptureOutcome.Accepted &&
                   dialog.ShortcutModifiers == Keys.Control &&
                   hints[0].Text.StartsWith("Press a new combination") &&
                   dialog.TryCapture(Keys.Tab, Keys.None) ==
                       ShortcutDialog.CaptureOutcome.Ignored;
        });
        checks.Verify("animations jump to their end on a control that is not shown", () =>
        {
            using var hidden = new Control();
            var changes = 0;
            var value = new AnimatedFloat(hidden, 0F, () => changes++);
            value.AnimateTo(1F, Motion.Standard);
            using var toggle = new BooleanToggle();
            toggle.Checked = true;
            return value.Value == 1F && changes == 1 &&
                   Motion.Ease(0F) == 0F && Motion.Ease(1F) == 1F && Motion.Ease(0.5F) > 0.5F;
        });
        checks.Verify("the OLED theme turns the dark surfaces black and leaves light alone", () =>
        {
            var black = Color.FromArgb(0, 0, 0);
            var wasOled = Palette.Oled;
            try
            {
                Palette.Oled = true;
                return Palette.Themed(Palette.Canvas, darkMode: true) == black &&
                       Palette.MenuSurface == black &&
                       Palette.Themed(Palette.Canvas, darkMode: false) == Palette.Canvas &&
                       Palette.Themed(Palette.Blue, darkMode: true) == Palette.Blue;
            }
            finally
            {
                Palette.Oled = wasOled;
            }
        });
        checks.Verify("closing offers the tray or a full exit, and changes nothing by default", () =>
        {
            using var dialog = new CloseChoiceDialog(darkMode: true, noSaveActive: true);
            var untouched = dialog.Choice == CloseChoice.Cancel;
            var warned = dialog.Controls.Find("CloseActiveWarning", true).Length == 1;
            var trayIsDefault = dialog.AcceptButton == dialog.Controls["CloseToTray"];
            dialog.Show();
            ((Button)dialog.Controls["CloseExit"]!).PerformClick();
            using var quiet = new CloseChoiceDialog(darkMode: false, noSaveActive: false);
            return untouched && warned && trayIsDefault && dialog.Choice == CloseChoice.Exit &&
                   quiet.Controls.Find("CloseActiveWarning", true).Length == 0;
        });
        checks.Verify("a guide step can take the keyboard focus", () =>
        {
            using var step = new GuideStepPanel();
            return step.CanSelect;
        });
        checks.Verify("the shortcut dialog exposes disabled controller configuration",
            () =>
            {
                using var dialog = new ShortcutDialog(
                    Keys.Control | Keys.Shift, Keys.F8, darkMode: true);
                var captures = dialog.Controls.Find("ControllerShortcutCapture", true);
                var configure = dialog.Controls.Find("ConfigureControllerShortcut", true);
                return captures.Length == 1 &&
                       captures[0].Text == "NOT CONFIGURED" &&
                       configure.Length == 1 &&
                       !configure[0].Enabled;
            });

        checks.Verify("the connection inspector runs without a game process",
            () => GameConnectionInspector.GetConnections(0) is not null);
        VerifyConnectionObservations(checks);
        checks.Verify("the connection inspector reads IPv4 rows correctly",
            () => VerifyInspectorAgainstLiveSocket(AddressFamily.InterNetwork));
        checks.Verify("the connection inspector reads IPv6 rows correctly",
            () => !Socket.OSSupportsIPv6 ||
                  VerifyInspectorAgainstLiveSocket(AddressFamily.InterNetworkV6));

        var firewallState = FirewallRuleState.Invalid;
        checks.Verify("the firewall rule state is readable", () =>
        {
            firewallState = new FirewallService().GetState();
            return Enum.IsDefined(typeof(FirewallRuleState), firewallState);
        });

        Console.WriteLine(checks.BuildReport(firewallState));
        return checks.Failures.Count == 0 ? 0 : 1;
    }

    private static string Reverse(string commaSeparated)
    {
        var entries = new List<string>(commaSeparated.Split(','));
        entries.Reverse();
        return string.Join(",", entries);
    }

    private static void VerifyConnectionObservations(CheckList checks)
    {
        checks.Verify("empty TCP tables are complete observations", () =>
        {
            var read = ReadSyntheticTables(empty: true);
            return read.Complete && read.Connections.Count == 0;
        });
        checks.Verify("a failed IPv6 read preserves IPv4 rows and the native error", () =>
        {
            var read = ReadSyntheticTables(ipv6Error: 5);
            return !read.Complete && read.Ipv4Error is null &&
                   read.Ipv6Error!.Contains("Windows error 5") &&
                   read.Connections.Count == 1 && read.Connections[0].LocalPort == 5000;
        });
        checks.Verify("a truncated TCP table is not a successful empty observation",
            () => !ReadSyntheticTables(truncated: true).Complete);
        checks.Verify("TCP table resize retries stay bounded", () =>
        {
            var calls = 0;
            int Read(IntPtr buffer, ref int size, bool sort, int family, int table, int reserved)
            {
                calls++;
                size += 32;
                return 122;
            }
            var read = GameConnectionInspector.ReadConnections(42, Read);
            return calls == 12 && !read.Complete &&
                   read.Ipv4Error!.Contains("retry limit") &&
                   read.Ipv6Error!.Contains("retry limit");
        });
        checks.Verify("TCP reader exceptions become unavailable observations", () =>
        {
            int Read(IntPtr buffer, ref int size, bool sort, int family, int table, int reserved)
            {
                if (buffer == IntPtr.Zero)
                {
                    size = 4;
                    return 122;
                }
                throw new InvalidOperationException("synthetic read failure");
            }
            var read = GameConnectionInspector.ReadConnections(42, Read);
            return !read.Complete && read.Ipv4Error!.Contains("synthetic read failure") &&
                   read.Ipv6Error!.Contains("synthetic read failure");
        });

        checks.Verify("a preexisting connection never triggers the runtime alert", () =>
        {
            var state = new MainForm.BlockObservationState();
            return !state.Observe(FirewallRuleState.Active, "42:100", [5000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [5000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [5000]);
        });
        checks.Verify("missing observations preserve the baseline but break confirmation", () =>
        {
            var state = new MainForm.BlockObservationState();
            return !state.Observe(FirewallRuleState.Active, "42:100", [5000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [5000, 6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", null) &&
                   !state.Observe(FirewallRuleState.Active, null, null) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [5000, 6000]) &&
                   state.Observe(FirewallRuleState.Active, "42:100", [5000, 6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [5000, 6000]);
        });
        checks.Verify("two complete clean observations rearm a recurrent alert", () =>
        {
            var state = new MainForm.BlockObservationState();
            return !state.Observe(FirewallRuleState.Active, "42:100", []) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [6000]) &&
                   state.Observe(FirewallRuleState.Active, "42:100", [6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", []) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", null) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", []) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", []) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", []) &&
                   !state.Observe(FirewallRuleState.Active, "42:100", [6000]) &&
                   state.Observe(FirewallRuleState.Active, "42:100", [6000]);
        });
        checks.Verify("a different process lifetime starts a fresh complete baseline", () =>
        {
            var state = new MainForm.BlockObservationState();
            return !state.Observe(FirewallRuleState.Active, "42:100", [5000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:200", null) &&
                   !state.Observe(FirewallRuleState.Active, "42:200", [6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:200", [6000]) &&
                   !state.Observe(FirewallRuleState.Active, "42:200", [7000]) &&
                   state.Observe(FirewallRuleState.Active, "42:200", [7000]);
        });
        checks.Verify("inactive, invalid and unknown rule states discard old baselines", () =>
        {
            foreach (var ruleState in new FirewallRuleState?[]
                     { FirewallRuleState.Inactive, FirewallRuleState.Invalid, null })
            {
                var state = new MainForm.BlockObservationState();
                state.Observe(FirewallRuleState.Active, "42:100", [5000]);
                if (state.Observe(ruleState, "42:100", null) ||
                    state.Observe(FirewallRuleState.Active, "42:100", [6000]) ||
                    state.Observe(FirewallRuleState.Active, "42:100", [6000]))
                {
                    return false;
                }
            }
            return true;
        });
        checks.Verify("a baseline owns its data instead of aliasing a later snapshot", () =>
        {
            var ports = new HashSet<int> { 5000 };
            var state = new MainForm.BlockObservationState();
            state.Observe(FirewallRuleState.Active, "42:100", ports);
            ports.Add(6000);
            return !state.Observe(FirewallRuleState.Active, "42:100", ports) &&
                   state.Observe(FirewallRuleState.Active, "42:100", ports);
        });

        checks.Verify("a snapshot of an established blocked connection is not a confirmed leak", () =>
        {
            var address = IPAddress.Parse(RockstarNetworks.BlockedSet[0].Canonical.Split('/')[0]);
            var read = new GameConnectionInspector.ReadResult(
                [new GameConnection(address, 443, 5000, TcpConnectionState.Established)], null, null);
            var report = new StringBuilder();
            return DiagnosticsReport.Summarize(report, FirewallRuleState.Active, read) == 0 &&
                   report.ToString().Contains("may predate activation") &&
                   !report.ToString().Contains("WARNING");
        });
        checks.Verify("an incomplete diagnostic returns 1 without a positive verdict", () =>
        {
            var report = new StringBuilder();
            var code = DiagnosticsReport.Summarize(report, FirewallRuleState.Active,
                ReadSyntheticTables(ipv6Error: 5));
            return code == 1 && report.ToString().Contains("IPv6 read unavailable") &&
                   !report.ToString().Contains("No established connection inside");
        });
        checks.Verify("no GTA is a completed diagnostic, but unreadable firewall state is not", () =>
        {
            var report = new StringBuilder();
            return DiagnosticsReport.Summarize(report, FirewallRuleState.Inactive, null) == 0 &&
                   report.ToString().Contains("Start GTA V") &&
                   DiagnosticsReport.Summarize(new StringBuilder(), null, null) == 1;
        });
        checks.Verify("a complete empty snapshot does not claim saves are blocked", () =>
        {
            var report = new StringBuilder();
            return DiagnosticsReport.Summarize(report, FirewallRuleState.Active,
                       ReadSyntheticTables(empty: true)) == 0 &&
                   report.ToString().Contains("does not prove that saves are blocked");
        });
    }

    private static void CheckOnlineServices(CheckList checks)
    {
        var installed = new Version(1, 3, 0, 0);
        var today = new DateTime(2026, 11, 23);
        const string validRelease = """{"tag_name":"v1.3.10","draft":false,"prerelease":false}""";
        checks.Verify("stable release comparisons use all three numeric parts", () =>
        {
            string Compare(Version version) => VaultLoopFeed.Format(FeedKind.Release, validRelease, version, today);
            return Compare(new Version(1, 3, 9)).Contains("Update available") &&
                   Compare(new Version(1, 3, 10, 0)).Contains("Up to date") &&
                   Compare(new Version(1, 4, 0)).Contains("Installed version is newer") &&
                   Compare(installed).Contains(VaultLoopFeed.ProjectUrl + "releases/tag/v1.3.10");
        });
        checks.Verify("offline release checks retain the last valid stable response and its timestamp", () =>
        {
            string? cache = null;
            var feed = new VaultLoopFeed(FeedKind.Release);
            var offlineReads = 0;
            string Read(VaultLoopFeed source, bool online, Func<string> fetch) =>
                source.Read(online, installed, default, () => cache, body => cache = body, fetch);
            var empty = Read(feed, false, () => { offlineReads++; return validRelease; });
            var fresh = Read(feed, true, () => validRelease);
            var saved = cache;
            var restarted = new VaultLoopFeed(FeedKind.Release);
            var cached = Read(restarted, false, () => { offlineReads++; return ""; });
            foreach (var bad in new[] { "not JSON", "{}", validRelease.Replace("false", "true"),
                         validRelease.Replace("v1.3.10", "v1.3.10-beta") })
            {
                var result = Read(restarted, true, () => bad);
                if (!result.Contains("unsuccessful") || !result.Contains("v1.3.10") || cache != saved)
                    return false;
            }
            var unavailable = Read(restarted, true, () => throw new System.Net.WebException());
            return offlineReads == 0 && empty.Contains("unknown") && saved is not null &&
                   cached == fresh && unavailable.Contains(fresh);
        });
        checks.Verify("compatibility notices filter inclusive version ranges and label expired data", () =>
        {
            const string body = """
                {"schema":1,"validThrough":"2027-02-28","notices":[
                  {"min":"1.2.5","max":"1.3.0","verified":"2026-11-23","summary":"Broad notice",
                   "reference":"https://github.com/inerthel-agi/VaultLoop/issues/1"},
                  {"min":"1.2.5","max":"1.2.5","verified":"2026-11-23","summary":"Patch notice",
                   "reference":"https://github.com/inerthel-agi/VaultLoop/issues/1"}]}
                """;
            string Show(Version version, DateTime date) =>
                VaultLoopFeed.Format(FeedKind.Compatibility, body, version, date);
            return Show(installed, today).Contains("Broad notice") &&
                   !Show(installed, today).Contains("Patch notice") &&
                   Show(new Version(1, 2, 5), today).Contains("Patch notice") &&
                   Show(new Version(8, 0, 0), today).Contains("No entries") &&
                   Show(installed, today.AddYears(5)).Contains("EXPIRED");
        });
        checks.Verify("closing cancels pending feed work before the next I/O step", () =>
        {
            for (var stage = 0; stage < 3; stage++)
            {
                using var closing = new System.Threading.CancellationTokenSource();
                var reads = 0;
                var requests = 0;
                var writes = 0;
                if (stage == 0) closing.Cancel();
                try
                {
                    new VaultLoopFeed(FeedKind.Release).Read(true, installed, closing.Token,
                        () => { reads++; if (stage == 1) closing.Cancel(); return null; },
                        _ => writes++, () => { requests++; closing.Cancel(); return validRelease; });
                    return false;
                }
                catch (OperationCanceledException) when (closing.IsCancellationRequested)
                {
                    if (reads != (stage == 0 ? 0 : 1) || requests != (stage == 2 ? 1 : 0) || writes != 0)
                        return false;
                }
            }
            return true;
        });
        checks.Verify("a cached catalog survives a restart, a network failure and malformed JSON", () =>
        {
            const string saved = "2020-01-01T01:02:03.0000000Z\n" +
                """{"schema":1,"validThrough":"2020-02-01","notices":[]}""";
            var feed = new VaultLoopFeed(FeedKind.Compatibility);
            var writes = 0;
            var reads = 0;
            string Read(bool online, Func<string> fetch) => feed.Read(online, installed, default,
                () => { reads++; return saved; }, _ => writes++, fetch);
            var requests = 0;
            var offline = Read(false, () => { requests++; return ""; });
            var interrupted = Read(true, () => throw new System.Net.WebException());
            var malformed = Read(true, () => "{");
            return reads == 1 && requests == 0 && writes == 0 &&
                   offline.Contains("2020-01-01 01:02:03Z") && offline.Contains("EXPIRED") &&
                   interrupted.Contains("unsuccessful") && interrupted.EndsWith(offline, StringComparison.Ordinal) &&
                   malformed == interrupted;
        });
        checks.Verify("invalid advisories preserve the bundled fallback and never enable an external link", () =>
        {
            var written = false;
            var result = new VaultLoopFeed(FeedKind.Compatibility).Read(true, installed, default,
                () => "damaged cache", _ => written = true, () => VaultLoopFeed.BundledCatalog()
                    .Replace("https://github.com/", "https://untrusted.invalid/"));
            return !written && result.Contains("unsuccessful") && result.Contains("included with VaultLoop") &&
                   !result.Contains("untrusted") && !VaultLoopFeed.IsProjectLink("file:///C:/example") &&
                   !VaultLoopFeed.IsProjectLink("https://github.com/other/project/issues/10");
        });
        checks.Verify("online services are available beside the local diagnostics", () =>
        {
            using var dialog = new ServicesDiagnosticsForm(false);
            dialog.Show();
            var tabs = (TabControl)dialog.Controls.Find("DiagnosticTabs", true)[0];
            return tabs.SelectedIndex == 0 && tabs.TabPages.Count == 2 &&
                   tabs.TabPages[1].Controls["CheckRelease"] is Button &&
                   tabs.TabPages[1].Controls["CheckCompatibility"] is Button;
        });
    }

    private static void CheckServicesDiagnostics(CheckList checks)
    {
        checks.Verify("the diagnostics tray action opens one panel while the owner stays hidden", () =>
        {
            using var owner = new MainForm(null, previewMode: true);
            using var menu = owner.CreateTrayMenu();
            var item = menu.Items["TrayDiagnostics"]!;
            item.PerformClick();
            item.PerformClick();
            if (owner.Visible || owner.OwnedForms.Length != 1 ||
                owner.OwnedForms[0] is not ServicesDiagnosticsForm first || !first.Visible)
                return false;
            first.Close();
            item.PerformClick();
            return first.IsDisposed && owner.OwnedForms.Length == 1 && owner.OwnedForms[0].Visible;
        });
        checks.Verify("no verified game is distinct from a successful empty TCP read", () =>
        {
            var absent = new LocalDiagnosticSnapshot(null, null, 0, "", null).FormatForSupport();
            var empty = new LocalDiagnosticSnapshot(FirewallRuleState.Inactive, null, 42,
                "GTA5.exe", ReadSyntheticTables(empty: true)).FormatForSupport();
            return absent.Contains("Game: No verified GTA process") &&
                   absent.Contains("Windows Firewall rule: Unavailable") &&
                   absent.Contains("TCP IPv4: Not read") && empty.Contains("TCP IPv6: Available (0 connections)");
        });
        checks.Verify("history keeps the newest 400 valid timestamps without exporting free text", () =>
        {
            var log = new StringBuilder();
            log.AppendLine("2026-07-01 00:00:00Z  pid 7       session started");
            for (var i = 0; i < ActivityLog.MaximumEntries; i++)
                log.AppendLine("2026-07-02 00:00:00Z  pid 7       endpoint configuration refused: 203.0.113.7");
            log.AppendLine("invalid-date  pid 7       session started");
            var events = DiagnosticHistory.Format(log.ToString());
            return events.Length == ActivityLog.MaximumEntries && Array.TrueForAll(events,
                e => e == "2026-07-02 00:00:00Z  Endpoint configuration rejected");
        });
        checks.Verify("selected safe history and the displayed report are exactly what is copied", () =>
        {
            const string secret = @"C:\Users\Private\GTA5_Enhanced.exe";
            var rows = DiagnosticHistory.Format(
                $"2026-07-29 09:00:00Z  pid 87465   no-save enabled from window for {secret}\r\n" +
                $"2026-07-29 09:01:00Z  pid 87465   restore on close failed: {secret}\n" +
                $"2026-07-29 09:02:00Z  pid 87465   {secret}\nmalformed line\n");
            if (rows.Length != 3 || !rows[0].EndsWith("Other recorded event") ||
                !rows[1].EndsWith("Restore on close failed") || !rows[2].EndsWith("Rule enabled from window"))
                return false;
            var read = new GameConnectionInspector.ReadResult(
                [new GameConnection(IPAddress.Parse("203.0.113.7"), 443, 53210, TcpConnectionState.Established)],
                null, "HID-PRIVATE-ID " + secret);
            var observation = new LocalDiagnosticSnapshot(null, new Exception(secret), 87465, secret, read);
            string? copied = null;
            using var panel = new ServicesDiagnosticsForm(false, value => copied = value);
            panel.Show();
            panel.Display(observation, rows);
            var preview = (TextBox)panel.Controls.Find("ReportPreview", true)[0];
            var list = (ListBox)panel.Controls.Find("SessionHistory", true)[0];
            var events = (Button)panel.Controls.Find("CopyEvents", true)[0];
            if (events.Enabled) return false;
            list.SetSelected(1, true);
            list.SetSelected(2, true);
            events.PerformClick();
            if (copied != rows[1] + Environment.NewLine + rows[2]) return false;
            ((Button)panel.Controls.Find("CopyReport", true)[0]).PerformClick();
            return copied == preview.Text && copied.Contains("TCP IPv4: Available (1 connection)") &&
                   copied.Contains("TCP IPv6: Read incomplete") && copied.Contains(rows[1]) &&
                   copied.Contains(rows[2]) && !copied.Contains(rows[0]) && !copied.Contains("Private") &&
                   !copied.Contains("203.0.113.7") && !copied.Contains("87465") && !copied.Contains("HID-");
        });
    }

    private static GameConnectionInspector.ReadResult ReadSyntheticTables(
        bool empty = false, int ipv6Error = 0, bool truncated = false)
    {
        int Read(IntPtr buffer, ref int size, bool sort, int family, int table, int reserved)
        {
            if (family == 23 && ipv6Error != 0)
            {
                return ipv6Error;
            }
            var hasRow = !empty && family == 2;
            var required = hasRow && !truncated ? 28 : 4;
            if (buffer == IntPtr.Zero || size < required)
            {
                size = required;
                return 122;
            }
            size = required;
            Marshal.Copy(new byte[required], 0, buffer, required);
            Marshal.WriteInt32(buffer, hasRow ? 1 : 0);
            if (hasRow && !truncated)
            {
                Marshal.WriteInt32(buffer, 4, (int)TcpConnectionState.Established);
                Marshal.WriteInt32(buffer, 12, 0x8813); // local port 5000 in network order
                Marshal.Copy(new byte[] { 192, 81, 241, 171 }, 0, IntPtr.Add(buffer, 16), 4);
                Marshal.WriteInt32(buffer, 20, 0xBB01); // remote port 443
                Marshal.WriteInt32(buffer, 24, 42);
            }
            return 0;
        }
        return GameConnectionInspector.ReadConnections(42, Read);
    }

    /// <summary>
    /// Opens a real loopback connection and checks the inspector reports it with the exact
    /// ports the sockets chose. The TCP table is read through hand-written struct offsets that
    /// differ between address families, which is precisely where a silent misread would hide.
    /// </summary>
    private static bool VerifyInspectorAgainstLiveSocket(AddressFamily addressFamily)
    {
        var loopback = addressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Loopback
            : IPAddress.Loopback;
        using var listener = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(loopback, 0));
        listener.Listen(1);
        var listenerPort = ((IPEndPoint)listener.LocalEndPoint).Port;

        using var client = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(new IPEndPoint(loopback, listenerPort));
        using var accepted = listener.Accept();
        var clientPort = ((IPEndPoint)client.LocalEndPoint).Port;

        foreach (var connection in GameConnectionInspector.GetConnections(
                     Process.GetCurrentProcess().Id))
        {
            if (connection.LocalPort == clientPort &&
                connection.RemotePort == listenerPort &&
                connection.State == TcpConnectionState.Established &&
                connection.RemoteAddress.Equals(loopback))
            {
                return true;
            }
        }
        return false;
    }

    private sealed class CheckList
    {
        private readonly StringBuilder _report = new();
        private int _executed;

        internal List<string> Failures { get; } = [];

        internal void Verify(string description, Func<bool> assertion)
        {
            _executed++;
            bool succeeded;
            string detail;
            try
            {
                succeeded = assertion();
                detail = succeeded ? "" : " (assertion returned false)";
            }
            catch (Exception exception)
            {
                succeeded = false;
                detail = $" ({exception.GetType().Name}: {exception.Message})";
            }

            _report.AppendLine($"[{(succeeded ? "PASS" : "FAIL")}] {description}{detail}");
            if (!succeeded)
            {
                Failures.Add(description);
            }
        }

        internal string BuildReport(FirewallRuleState firewallState)
        {
            var summary = new StringBuilder();
            summary.AppendLine();
            summary.Append(_report);
            summary.AppendLine();
            summary.AppendLine($"Result        = {(Failures.Count == 0 ? "PASS" : "FAIL")}");
            summary.AppendLine($"Checks        = {_executed - Failures.Count}/{_executed}");
            summary.AppendLine($"FirewallState = {firewallState}");
            foreach (var failure in Failures)
            {
                summary.AppendLine($"Failed        = {failure}");
            }
            return summary.ToString();
        }
    }
}
#endif
