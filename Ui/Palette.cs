using System;
using System.Drawing;

namespace ReplayGlitchGTA;

internal static class Palette
{
    internal static readonly Color Ink = Color.FromArgb(17, 17, 17);
    internal static readonly Color Yellow = Color.FromArgb(255, 215, 56);
    internal static readonly Color Blue = Color.FromArgb(91, 134, 255);
    internal static readonly Color Acid = Color.FromArgb(185, 255, 61);
    internal static readonly Color HotPink = Color.FromArgb(255, 83, 112);
    internal static readonly Color AlertRed = Color.FromArgb(232, 54, 70);

    /// <summary>
    /// The OLED theme: every dark surface becomes true black, so the pixels behind it are
    /// off. Set by the main window when the theme changes; there is one window and one
    /// theme per process, which is why this is a switch and not a parameter.
    /// </summary>
    internal static bool Oled { get; set; }

    // Tray menu, in-game HUD and toast. These are never light: they sit over a game or
    // the desktop. They follow the OLED switch only.
    internal static Color MenuSurface => Oled ? Rgb(0, 0, 0) : Rgb(23, 25, 29);
    internal static Color MenuLine => Oled ? Rgb(38, 38, 42) : Rgb(43, 46, 52);
    internal static Color MenuHover => Oled ? Rgb(22, 22, 25) : Rgb(35, 38, 44);
    internal static Color SwitchOff => Oled ? Rgb(44, 44, 49) : Rgb(58, 62, 70);
    internal static readonly Color MenuText = Rgb(236, 238, 240);
    internal static readonly Color MenuMuted = Rgb(169, 174, 182);
    internal static readonly Color MenuDanger = Rgb(255, 138, 154);

    // Window roles, named by their light value. Themed() gives the dark or OLED counterpart.
    internal static readonly Color Canvas = Rgb(243, 244, 246);
    internal static readonly Color Surface = Rgb(255, 255, 255);
    internal static readonly Color Line = Rgb(217, 220, 225);
    internal static readonly Color Text = Rgb(27, 29, 33);
    internal static readonly Color Muted = Rgb(90, 95, 103);
    internal static readonly Color Control = Rgb(233, 235, 239);
    internal static readonly Color Track = Rgb(190, 195, 203);

    private static readonly (Color Light, Color Dark, Color Oled)[] Roles =
    [
        (Canvas, Rgb(23, 25, 29), Rgb(0, 0, 0)),
        (Surface, Rgb(31, 34, 39), Rgb(10, 10, 11)),
        (Line, Rgb(43, 46, 52), Rgb(38, 38, 42)),
        (Text, Rgb(236, 238, 240), Rgb(242, 242, 243)),
        (Muted, Rgb(169, 174, 182), Rgb(158, 158, 166)),
        (Control, Rgb(44, 48, 55), Rgb(26, 26, 29)),
        (Track, Rgb(58, 62, 70), Rgb(44, 44, 49))
    ];

    /// <summary>
    /// The color a window role takes in the given theme. Anything that is not a role — an
    /// accent, the ink on an accent, transparency — is returned unchanged in every theme.
    /// </summary>
    internal static Color Themed(Color light, bool darkMode)
    {
        if (darkMode)
        {
            foreach (var role in Roles)
            {
                if (role.Light == light)
                {
                    return Oled ? role.Oled : role.Dark;
                }
            }
        }
        return light;
    }

    /// <summary>
    /// <paramref name="over"/> laid on <paramref name="under"/> at the given opacity, as one
    /// opaque color. GDI text needs an opaque background, so tints are mixed up front.
    /// </summary>
    internal static Color Mix(Color under, Color over, double amount) => Color.FromArgb(
        (int)Math.Round(under.R + (over.R - under.R) * amount),
        (int)Math.Round(under.G + (over.G - under.G) * amount),
        (int)Math.Round(under.B + (over.B - under.B) * amount));

    private static Color Rgb(int red, int green, int blue) => Color.FromArgb(red, green, blue);
}
