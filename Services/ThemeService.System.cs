using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// System-theme-mode resolution. The preset catalog stays at exactly two entries (Light /
    /// Dark); "System" is a MODE stored in settings.json that resolves to whichever preset
    /// matches the current Windows app theme, so following the OS never adds a third preset.
    /// </summary>
    public sealed partial class ThemeService
    {
        /// <summary>Resolves the effective preset for a stored theme-mode name, routing
        /// "System" through the OS preference. Never returns null.</summary>
        public ThemePreset ResolveEffective(string? storedName)
        {
            if (SystemThemeProvider.IsSystemMode(storedName))
            {
                ThemePreset? osPreset = FindByName(SystemThemeProvider.IsSystemDark ? "Dark" : "Light");
                return osPreset ?? Default;
            }
            return ResolveThemeName(storedName) ?? Default;
        }

        /// <summary>Same contract as <see cref="ActivateFromSettings"/> but aware of the
        /// "System" mode: sets <see cref="Current"/> from the stored mode + OS preference and
        /// returns it. Use this at startup and whenever the OS theme changes.</summary>
        public ThemePreset ActivateEffectiveFromSettings()
        {
            Current = ResolveEffective(SettingsService.LoadThemeName());
            return Current;
        }
    }
}
