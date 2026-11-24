using System.Drawing;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// Builds the controls the windows are made of.
/// </summary>
internal static class ControlFactory
{
    /// <summary>A rounded button whose fill is <paramref name="backColor"/>.</summary>
    internal static SoftButton MakeButton(
        string text, Rectangle bounds, Font font, Color backColor, Color foreColor) =>
        new()
        {
            Text = text,
            Bounds = bounds,
            Font = font,
            BackColor = backColor,
            ForeColor = foreColor
        };

    /// <summary>Makes a scrolling control's native scroll bars follow the theme.</summary>
    internal static T WithThemedScrollBars<T>(T control, bool darkMode) where T : Control
    {
        control.HandleCreated += (_, _) =>
            NativeMethods.ThemeScrollBars(control.Handle, darkMode);
        return control;
    }

    internal static Label MakeLabel(
        string text,
        Rectangle bounds,
        Font font,
        Color backColor,
        Color? foreColor = null,
        ContentAlignment alignment = ContentAlignment.MiddleLeft) =>
        new()
        {
            Text = text,
            Bounds = bounds,
            Font = font,
            BackColor = backColor,
            ForeColor = foreColor ?? Palette.Ink,
            TextAlign = alignment,
            AutoEllipsis = true
        };
}
