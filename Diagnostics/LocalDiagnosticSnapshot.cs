using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ReplayGlitchGTA;

internal sealed class LocalDiagnosticSnapshot(
    FirewallRuleState? ruleState, Exception? firewallError, int processId,
    string gamePath, GameConnectionInspector.ReadResult? connections)
{
    internal DateTime ObservedUtc { get; } = DateTime.UtcNow;
    internal FirewallRuleState? RuleState { get; } = ruleState;
    internal Exception? FirewallError { get; } = firewallError;
    internal int ProcessId { get; } = processId;
    internal string GamePath { get; } = gamePath;
    internal GameConnectionInspector.ReadResult? Connections { get; } = connections;

    internal static LocalDiagnosticSnapshot Read()
    {
        FirewallRuleState? state = null;
        Exception? error = null;
        try
        {
            state = new FirewallService().GetState();
        }
        catch (Exception exception)
        {
            error = exception;
        }
        var verified = GameProcessService.TryGetVerifiedGameProcess(out var pid, out var path);
        return new LocalDiagnosticSnapshot(state, error, pid, path,
            verified ? GameConnectionInspector.ReadConnections(pid) : null);
    }

    // Only fixed labels, counts and error codes belong in the shareable report.
    internal string FormatForSupport()
    {
        var game = Connections is null ? "No verified GTA process" :
            string.Equals(Path.GetFileName(GamePath), "GTA5_Enhanced.exe",
                StringComparison.OrdinalIgnoreCase) ? "GTA V Enhanced (verified)" : "GTA V (verified)";
        var state = RuleState?.ToString() ?? "Unavailable";
        if (FirewallError is not null)
        {
            state += $" (0x{FirewallError.HResult:X8})";
        }
        return string.Join(Environment.NewLine, new[] {
            $"VaultLoop {MainForm.VersionText} | Local diagnostic",
            $"Observed (UTC): {ObservedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
            $"Game: {game}",
            $"Windows Firewall rule: {state}",
            $"TCP IPv4: {FormatFamily(false)}",
            $"TCP IPv6: {FormatFamily(true)}"
        });
    }

    private string FormatFamily(bool ipv6)
    {
        if (Connections is null)
        {
            return "Not read (no verified game)";
        }
        if ((ipv6 ? Connections.Ipv6Error : Connections.Ipv4Error) is not null)
        {
            return "Read incomplete";
        }
        var count = Connections.Connections.Count(row => row.IsVersion6 == ipv6);
        return $"Available ({count} connection{(count == 1 ? "" : "s")})";
    }
}
