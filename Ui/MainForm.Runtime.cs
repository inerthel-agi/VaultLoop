using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ReplayGlitchGTA;

internal sealed partial class MainForm
{
    private const int RefreshIntervalMilliseconds = 1200;

    /// <summary>Roughly six seconds of tolerance before auto-restoring on game loss.</summary>
    private const int MissingGameTicksBeforeRestore = 5;

    /// <summary>Two consecutive ticks, so a single scheduling hiccup cannot raise the alarm.</summary>
    private const int LeakingTicksBeforeWarning = 2;

    private int _runtimeRefreshInProgress;
    private int _runtimeRefreshVersion;
    private int _missingGameTicks;
    private readonly BlockObservationState _blockObservation = new();

    private void QueueRuntimeRefresh()
    {
        if (_applying || _firewall is null ||
            Interlocked.Exchange(ref _runtimeRefreshInProgress, 1) != 0)
        {
            return;
        }

        // The shortcuts are deliberately left as they are while the snapshot is read.
        // Disarming here and re-arming from ApplyGameContext opened a dead window on every
        // tick — and an unbounded one whenever the snapshot was discarded — during which a
        // legitimate keystroke was silently swallowed. Neither guard required by the security
        // model is weakened: ApplyGameContext still disarms as soon as the verified game stops
        // being in the foreground, and ShortcutTriggerGate.CanFire still re-checks the live
        // foreground window before every trigger.
        var version = Interlocked.Increment(ref _runtimeRefreshVersion);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var snapshot = ReadRuntimeSnapshot();
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() => ApplyRuntimeSnapshot(snapshot, version)));
                    }
                }
                catch (InvalidOperationException)
                {
                    // The window closed between the handle check and BeginInvoke.
                }
            }
            finally
            {
                Interlocked.Exchange(ref _runtimeRefreshInProgress, 0);
            }
        });
    }

    private RuntimeSnapshot ReadRuntimeSnapshot()
    {
        var snapshot = new RuntimeSnapshot();
        if (GameProcessService.TryGetVerifiedForegroundGame(
                out var foregroundPath, out var foregroundWindow))
        {
            snapshot.ForegroundPath = foregroundPath;
            snapshot.ForegroundWindow = foregroundWindow;
        }
        else if (GameProcessService.TryFindVerifiedRunningGame(out var runningPath))
        {
            snapshot.RunningPath = runningPath;
        }

        try
        {
            snapshot.FirewallState = _firewall?.GetState();
        }
        catch (Exception exception)
        {
            snapshot.FirewallError = exception;
        }

        if (snapshot.FirewallState == FirewallRuleState.Active &&
            GameProcessService.TryGetVerifiedGameProcess(out var processId, out _))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var identity = $"{processId}:{process.StartTime.ToUniversalTime().Ticks}";
                var ports = ReadBlockedLocalPorts(processId);
                if (!process.HasExited)
                {
                    snapshot.ProcessIdentity = identity;
                    snapshot.BlockedLocalPorts = ports;
                }
            }
            catch (Exception)
            {
                // Missing observation, not evidence that GTA disappeared. Game-loss
                // recovery still uses only the verified context collected above.
            }
        }
        return snapshot;
    }

    /// <summary>
    /// The local ports of the game's established connections to blocked addresses. Comparing
    /// this set across ticks tells an already-open flow — which a new block rule does not tear
    /// down — apart from a flow that completed its handshake through the active rule.
    /// </summary>
    private static HashSet<int>? ReadBlockedLocalPorts(int processId)
    {
        var result = GameConnectionInspector.ReadConnections(processId);
        if (!result.Complete)
        {
            return null;
        }
        var ports = new HashSet<int>();
        foreach (var connection in result.Connections)
        {
            if (connection.State == TcpConnectionState.Established &&
                RockstarNetworks.IsBlocked(connection.RemoteAddress))
            {
                ports.Add(connection.LocalPort);
            }
        }
        return ports;
    }

    private void ApplyRuntimeSnapshot(RuntimeSnapshot snapshot, int version)
    {
        if (version != Volatile.Read(ref _runtimeRefreshVersion) || _applying)
        {
            return;
        }

        ApplyGameContext(
            snapshot.ForegroundPath, snapshot.ForegroundWindow, snapshot.RunningPath);

        if (snapshot.FirewallState.HasValue)
        {
            ApplyFirewallState(snapshot.FirewallState.Value);
        }
        else if (snapshot.FirewallError is not null)
        {
            SetUnknownState();
        }

        EvaluateBlockEffectiveness(snapshot);
        EvaluateGameLoss(snapshot.HasVerifiedGame);
    }

    /// <summary>
    /// Warns when the rule reports Active while the game keeps opening new connections to a
    /// blocked address. The first Active tick only records a baseline: connections that were
    /// already established when the rule went up survive it, and flagging those would cry wolf
    /// on every activation.
    /// </summary>
    private void EvaluateBlockEffectiveness(RuntimeSnapshot snapshot)
    {
        if (!_blockObservation.Observe(snapshot.FirewallState,
                snapshot.ProcessIdentity, snapshot.BlockedLocalPorts))
        {
            return;
        }

        ActivityLog.Write(
            "the rule is active but the game opened a new connection to a blocked address");
        SetGameStatus("BLOCK NOT EFFECTIVE", Palette.HotPink);
        ShowStatusToast("BLOCK NOT EFFECTIVE", Palette.Yellow,
            "New GTA connection to a blocked address. Run --diagnose for details.");
    }

    /// <summary>
    /// Tracks one known activation and process lifetime. Incomplete observations cannot
    /// replace the first complete baseline or count towards either confirmation window.
    /// </summary>
    internal sealed class BlockObservationState
    {
        private string? _processIdentity;
        private HashSet<int>? _baseline;
        private int _leakingTicks;
        private int _cleanTicks;
        private bool _reported;

        internal void Reset()
        {
            _processIdentity = null;
            _baseline = null;
            _leakingTicks = 0;
            _cleanTicks = 0;
            _reported = false;
        }

        internal bool Observe(FirewallRuleState? state, string? processIdentity,
            HashSet<int>? ports)
        {
            if (state != FirewallRuleState.Active)
            {
                Reset();
                return false;
            }
            if (processIdentity is not null && processIdentity != _processIdentity)
            {
                Reset();
                _processIdentity = processIdentity;
            }
            if (processIdentity is null || ports is null)
            {
                _leakingTicks = 0;
                _cleanTicks = 0;
                return false;
            }
            if (_baseline is null)
            {
                _baseline = new HashSet<int>(ports);
                return false;
            }
            if (_baseline.IsSupersetOf(ports))
            {
                _leakingTicks = 0;
                _cleanTicks = Math.Min(_cleanTicks + 1, LeakingTicksBeforeWarning);
                if (_cleanTicks == LeakingTicksBeforeWarning)
                {
                    _reported = false;
                }
                return false;
            }
            _cleanTicks = 0;
            _leakingTicks = Math.Min(_leakingTicks + 1, LeakingTicksBeforeWarning);
            if (_leakingTicks < LeakingTicksBeforeWarning || _reported)
            {
                return false;
            }
            _reported = true;
            return true;
        }
    }

    /// <summary>
    /// Restores the link when the verified game is gone while no-save is still active. The
    /// rule names the game executable by path, so leaving it in place would silently block a
    /// relaunched GTA — the exact failure this application exists to prevent, inverted.
    /// A few ticks of tolerance keep a brief detection gap from cutting no-save mid-activity.
    /// </summary>
    private void EvaluateGameLoss(bool hasVerifiedGame)
    {
        if (hasVerifiedGame || _firewallState != FirewallRuleState.Active)
        {
            _missingGameTicks = 0;
            return;
        }
        if (_applying || _firewall is null || !_stateKnown)
        {
            return;
        }

        _missingGameTicks++;
        if (_missingGameTicks < MissingGameTicksBeforeRestore)
        {
            return;
        }

        _missingGameTicks = 0;
        RestoreAfterGameLoss();
    }

    private void RestoreAfterGameLoss()
    {
        RunExclusive(
            () => _firewall!.SetNoSaveEnabled(false),
            () =>
            {
                ActivityLog.Write(
                    "no-save disabled automatically: the verified GTA process is gone");
                SetDisplayedState(false);
                ShowStatusToast("NO-SAVE RESTORED", Palette.Acid,
                    "The verified GTA process is gone. No-save was disabled automatically.");
            },
            exception =>
            {
                ActivityLog.Write("automatic restore after game loss failed", exception);
                ShowStatusToast("AUTO-RESTORE FAILED", Palette.Yellow, exception.Message);
            });
    }

    private void RefreshGameContext()
    {
        if (GameProcessService.TryGetVerifiedForegroundGame(
                out var foregroundPath, out var foregroundWindow))
        {
            ApplyGameContext(foregroundPath, foregroundWindow, runningPath: null);
            return;
        }

        ApplyGameContext(foregroundPath: null, IntPtr.Zero,
            GameProcessService.TryFindVerifiedRunningGame(out var runningPath)
                ? runningPath
                : null);
    }

    /// <summary>
    /// Publishes the detected game context: the shortcut is armed only for a verified game in
    /// the foreground, and the footer reports which of the three situations applies.
    /// </summary>
    private void ApplyGameContext(
        string? foregroundPath, IntPtr foregroundWindow, string? runningPath)
    {
        var gameDetected = foregroundPath is not null || runningPath is not null;
        if (!_previewMode && gameDetected != (_verifiedGamePath is not null))
        {
            ActivityLog.Write(gameDetected ? "verified game detected" : "verified game no longer detected");
        }
        _hasVerifiedForegroundGame = foregroundPath is not null;
        UpdateHudVisibility();
        if (foregroundPath is not null)
        {
            _verifiedGamePath = foregroundPath;
            _hotkeyHook.Arm(foregroundWindow);
            _controllerShortcutService.Arm(foregroundWindow);
            SetGameStatus("GTA READY  //  SAFE RESTORE", Palette.Acid);
            return;
        }

        _hotkeyHook.Disarm();
        _controllerShortcutService.Disarm();
        _verifiedGamePath = runningPath;
        SetGameStatus(
            runningPath is null ? "WAITING FOR GTA" : "GTA IN BACKGROUND", Palette.Yellow);
    }

    private sealed class RuntimeSnapshot
    {
        internal string? ForegroundPath { get; set; }
        internal IntPtr ForegroundWindow { get; set; }
        internal string? RunningPath { get; set; }
        internal FirewallRuleState? FirewallState { get; set; }
        internal Exception? FirewallError { get; set; }
        internal HashSet<int>? BlockedLocalPorts { get; set; }
        internal string? ProcessIdentity { get; set; }

        internal bool HasVerifiedGame => ForegroundPath is not null || RunningPath is not null;
    }
}
