using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// Switches the window between the light, dark and OLED palettes.
/// </summary>
/// <remarks>
/// The colors captured once at construction are the light-theme originals, and every later
/// switch is computed from them rather than from what is currently on screen: mapping a mapped
/// color would drift a control further away from its palette on each change. Only the window
/// roles of <see cref="Palette.Themed"/> change; accents, and the ink sitting on them, are the
/// same in every theme. Controls whose color carries state — the toggle, the selected theme
/// button — are excluded and colored by the window itself.
/// </remarks>
internal sealed class ThemeController
{
    private readonly Form _form;
    private readonly HashSet<Control> _excludedControls;
    private readonly Dictionary<Control, (Color Back, Color Fore)> _originalColors = new();

    internal ThemeController(Form form, Control[] excludedControls)
    {
        _form = form;
        _excludedControls = new HashSet<Control>(excludedControls);
    }

    internal void CaptureThemeColors()
    {
        CaptureThemeColors(_form);
    }

    /// <summary>Dark and OLED both pass true; <see cref="Palette.Oled"/> tells them apart.</summary>
    internal void ApplyTheme(bool darkMode)
    {
        _form.BackColor = Palette.Themed(Palette.Canvas, darkMode);
        _form.ForeColor = Palette.Themed(Palette.Text, darkMode);
        foreach (var entry in _originalColors)
        {
            var (originalBack, originalFore) = entry.Value;
            entry.Key.BackColor = Palette.Themed(originalBack, darkMode);
            entry.Key.ForeColor = Palette.Themed(originalFore, darkMode);
        }
        _form.Invalidate(true);
    }

    private void CaptureThemeColors(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (!_excludedControls.Contains(control))
            {
                _originalColors[control] = (control.BackColor, control.ForeColor);
            }
            CaptureThemeColors(control);
        }
    }
}
