using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReplayGlitchGTA;

internal static class DiagnosticHistory
{
    // Older log entries contain free text, including paths and exception messages.
    // Project each known event onto a fixed label before it reaches the panel or clipboard.
    private static readonly (string Prefix, string Label)[] KnownEvents =
    [
        ("session started", "Session started"),
        ("verified game detected", "Verified game detected"),
        ("verified game no longer detected", "Verified game no longer detected"),
        ("no-save enabled from shortcut", "Rule enabled from shortcut"),
        ("no-save enabled from window", "Rule enabled from window"),
        ("no-save disabled from shortcut", "Rule disabled from shortcut"),
        ("no-save disabled from window", "Rule disabled from window"),
        ("no-save change failed", "Firewall change failed"),
        ("no-save disabled automatically:", "Restored after the verified game disappeared"),
        ("automatic restore after game loss failed", "Restore after game loss failed"),
        ("the rule is active but", "Connection warning while rule was active"),
        ("startup recovery: removed", "Stale rule restored at startup"),
        ("startup recovery failed", "Startup recovery failed"),
        ("requesting administrator rights", "Administrator approval requested"),
        ("endpoint configuration refused", "Endpoint configuration rejected"),
        ("watchdog restored", "Watchdog restored the rule after exit"),
        ("watchdog could not restore", "Watchdog restore failed"),
        ("--restore removed", "Rule restored by the restore command"),
        ("--restore failed", "Restore command failed"),
        ("restore on close failed", "Restore on close failed"),
        ("rule restored on close", "Rule restored on close"),
        ("final restore on exit failed", "Final restore on exit failed"),
        ("self-test entry", "Self-test log check")
    ];

    internal static string[] Read()
    {
        var text = AppSettingsStorage.ReadBoundedCurrentText(ActivityLog.FileName, ActivityLog.MaximumFileBytes);
        return Format(text ?? "");
    }

    internal static string[] Format(string text)
    {
        var events = new List<string>();
        var lines = text.Split('\n');
        for (var i = lines.Length - 1; i >= 0 && events.Count < ActivityLog.MaximumEntries; i--)
        {
            var line = lines[i];
            const string marker = "Z  pid ";
            const int dateLength = 19;
            if (line.Length <= dateLength + marker.Length ||
                string.CompareOrdinal(line, dateLength, marker, 0, marker.Length) != 0 ||
                !DateTime.TryParseExact(line.Substring(0, dateLength), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }
            var pidStart = dateLength + marker.Length;
            var separator = line.IndexOf(' ', pidStart);
            if (separator < 0 || !int.TryParse(line.Substring(pidStart, separator - pidStart),
                    NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }
            var message = line.Substring(separator).Trim();
            var label = "Other recorded event";
            foreach (var entry in KnownEvents)
            {
                if (message.StartsWith(entry.Prefix, StringComparison.Ordinal))
                {
                    label = entry.Label;
                    break;
                }
            }
            if (label == "Other recorded event")
            {
                if (message.Contains(" refused:")) label = "Shortcut refused";
                else if (message.EndsWith("keyboard hook could not be installed", StringComparison.Ordinal))
                    label = "Keyboard hook unavailable";
            }
            events.Add(time.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                + "  " + label);
        }
        return events.ToArray();
    }
}
