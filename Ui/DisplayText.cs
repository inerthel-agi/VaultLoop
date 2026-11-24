using System.Collections.Generic;
using System.Linq;

namespace ReplayGlitchGTA;

/// <summary>
/// Turns the capitalised strings the logic and the self-test are keyed on into what the
/// window shows. The strings themselves stay as they are; only their display changes.
/// </summary>
internal static class DisplayText
{
    private const string Separator = "  //  ";

    private static readonly Dictionary<string, string> ProperNames = new()
    {
        ["gta"] = "GTA",
        ["hud"] = "HUD",
        ["vaultloop"] = "VaultLoop",
        ["rockstar"] = "Rockstar"
    };

    /// <summary>
    /// <c>GTA READY  //  SAFE RESTORE</c> becomes <c>GTA ready · safe restore</c>. Words
    /// holding a digit or a plus sign are key names (<c>F4</c>, <c>CTRL+F4</c>) and are kept.
    /// </summary>
    internal static string Sentence(string text)
    {
        var segments = text.Split([Separator], System.StringSplitOptions.None);
        for (var segment = 0; segment < segments.Length; segment++)
        {
            var words = segments[segment].Split(' ');
            for (var word = 0; word < words.Length; word++)
            {
                var current = words[word];
                if (current.Any(character => char.IsDigit(character) || character == '+'))
                {
                    continue;
                }

                var lower = current.ToLowerInvariant();
                words[word] = ProperNames.TryGetValue(lower, out var proper)
                    ? proper
                    : segment == 0 && word == 0 && lower.Length > 0
                        ? char.ToUpperInvariant(lower[0]) + lower.Substring(1)
                        : lower;
            }
            segments[segment] = string.Join(" ", words);
        }
        return string.Join(" · ", segments);
    }
}
