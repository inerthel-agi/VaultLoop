using System;

namespace ReplayGlitchGTA;

internal enum AppTheme
{
    Light,
    Dark,
    Oled
}

internal static class ThemeSettings
{
    internal static AppTheme Load() =>
        AppSettingsStorage.ReadPreference<AppTheme>(
            "theme.txt", includeLegacy: true, TryParse, Save, fallback: AppTheme.Light);

    /// <summary>
    /// Anything other than "dark" or "oled" means the light theme, so parsing never fails.
    /// A version from before the OLED theme reads "oled" the same way and opens light.
    /// </summary>
    private static bool TryParse(string rawValue, out AppTheme theme)
    {
        var value = rawValue.Trim();
        theme = value.Equals("dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark
            : value.Equals("oled", StringComparison.OrdinalIgnoreCase) ? AppTheme.Oled
            : AppTheme.Light;
        return true;
    }

    internal static void Save(AppTheme theme)
    {
        AppSettingsStorage.WriteText("theme.txt", theme switch
        {
            AppTheme.Dark => "dark",
            AppTheme.Oled => "oled",
            _ => "light"
        });
    }
}
