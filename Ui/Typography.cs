using System.Drawing;

namespace ReplayGlitchGTA;

internal static class Typography
{
    // MenuBody is also the unit the tray menu and the HUD scale their pixel measures from.
    internal static readonly Font WindowHeading = new("Segoe UI Semibold", 17F);
    internal static readonly Font ToggleLabel = new("Segoe UI Semibold", 16F);
    internal static readonly Font CardHeading = new("Segoe UI Semibold", 13F);
    internal static readonly Font MenuTitle = new("Segoe UI Semibold", 11F);
    internal static readonly Font HudStatus = new("Segoe UI Semibold", 10F);
    internal static readonly Font MenuBody = new("Segoe UI", 10F);
    internal static readonly Font MenuCaption = new("Segoe UI Semibold", 9F);
    internal static readonly Font MenuDetail = new("Segoe UI", 9F);

    // Readouts: a captured key combination, the diagnostics report.
    internal static readonly Font ShortcutCapture =
        new("Consolas", 16F, FontStyle.Bold);
    internal static readonly Font CompactMono = new("Consolas", 9F);

    // The minimize and close glyphs Windows draws on its own title bars.
    internal static readonly Font ChromeGlyph = new("Segoe MDL2 Assets", 8F);
}
