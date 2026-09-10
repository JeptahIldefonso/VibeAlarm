using VibeAlarm.Models;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Live appearance state derived from the persisted settings: effective corner radii,
    /// layout density, and animation policy. <see cref="UIControlFactory"/> and the views read
    /// these instead of the static <see cref="DesignTokens.Radius"/> constants (which remain
    /// the defaults) so the Settings sliders take effect without touching every call site.
    ///
    /// <see cref="Apply"/> is called by the host whenever appearance settings load or change;
    /// the cached snapshot keeps control construction allocation-free.
    /// </summary>
    public static class Appearance
    {
        private static AppSettings current = new();

        /// <summary>Caches the effective appearance snapshot (called on load and on change).</summary>
        public static void Apply(AppSettings settings)
        {
            current = settings;
        }

        public static AppSettings Current => current;

        /// <summary>Card radius in px (0–20), straight from the user's Corner radius setting.</summary>
        public static int CardRadius => Math.Clamp(current.CornerRadius, 0, 20);

        /// <summary>Control (button/input/cell) radius — proportionally smaller than cards.</summary>
        public static int ControlRadius => Math.Max(0, CardRadius * 2 / 3);

        /// <summary>True when the user chose the Compact layout density.</summary>
        public static bool Compact => string.Equals(current.Density, "Compact", StringComparison.OrdinalIgnoreCase);

        /// <summary>Task row height in px (§12: 72–88px band).</summary>
        public static int TaskRowHeight => Compact ? 68 : 80;

        /// <summary>Vertical padding inside settings rows.</summary>
        public static int SettingsRowPadding => Compact ? 8 : 14;

        /// <summary>Guna hover/press animations enabled.</summary>
        public static bool Animations => current.EnableAnimations;
    }
}
