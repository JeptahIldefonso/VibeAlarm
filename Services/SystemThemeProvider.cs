using System;
using Microsoft.Win32;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Detects the Windows app theme (light/dark) for the "System" theme mode. Reads the
    /// AppsUseLightTheme personalization value from HKCU; the result is cached because the
    /// registry read happens on every theme resolution, and refreshed on demand (the host
    /// re-evaluates when the OS theme preference changes via UserPreferenceChanged and when
    /// the window is activated).
    ///
    /// The detected value is injectable so tests and design-time code can force either mode
    /// without depending on the machine's real OS setting.
    /// </summary>
    public static class SystemThemeProvider
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";

        private static bool? cachedIsDark;
        private static bool? forcedIsDark;

        /// <summary>True when the Windows app theme is dark. Test-injectable via <see cref="ForceDark"/>.</summary>
        public static bool IsSystemDark
        {
            get
            {
                if (forcedIsDark.HasValue)
                {
                    return forcedIsDark.Value;
                }
                if (cachedIsDark.HasValue)
                {
                    return cachedIsDark.Value;
                }

                cachedIsDark = ReadOsPreference();
                return cachedIsDark.Value;
            }
        }

        /// <summary>The stored theme-mode string for following the OS ("System").</summary>
        public const string SystemModeName = "System";

        /// <summary>True when the stored theme name means "follow the OS".</summary>
        public static bool IsSystemMode(string? storedThemeName)
            => string.Equals(storedThemeName, SystemModeName, StringComparison.OrdinalIgnoreCase);

        /// <summary>Forces the detected value (tests / design time). Pass null to clear.</summary>
        public static void ForceDark(bool? isDark)
        {
            forcedIsDark = isDark;
        }

        /// <summary>Clears the cached registry read so the next query re-reads the OS value.</summary>
        public static void Refresh()
        {
            cachedIsDark = null;
        }

        private static bool ReadOsPreference()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
                if (key?.GetValue(AppsUseLightTheme) is int useLight)
                {
                    // 0 = dark apps, 1 = light apps; missing value = older Windows → light.
                    return useLight == 0;
                }
            }
            catch
            {
                // Registry access can fail under odd permissions — default to light rather than crash.
            }
            return false;
        }
    }
}
