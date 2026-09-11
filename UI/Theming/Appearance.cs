using VibeAlarm.Models;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Locked appearance constants: effective corner radii and layout density. These were
    /// formerly derived from the persisted settings (corner radius / density sliders); the
    /// sliders were removed and the values the interface was tuned against are now hardcoded
    /// here. <see cref="UIControlFactory"/> and the views read these so the whole app stays
    /// consistent from one place.
    ///
    /// <see cref="Apply"/> is still called by the host on load/settings change — the Animations
    /// toggle remains a live setting, and the cached snapshot keeps control construction
    /// allocation-free.
    /// </summary>
    public static class Appearance
    {
        private static AppSettings current = new();

        /// <summary>Caches the live settings snapshot (called on load and on change) —
        /// consumed by <see cref="Animations"/>, the one appearance setting still live.</summary>
        public static void Apply(AppSettings settings)
        {
            current = settings;
        }

        /// <summary>Card radius in px — square cards, matching the tuned interface (the former
        /// corner-radius slider rested at 0). Buttons do NOT follow this: text buttons keep
        /// their own <see cref="DesignTokens.Radius"/> and icon buttons floor at 4.</summary>
        public const int CardRadius = 0;

        /// <summary>Control (icon button) radius — kept as its own constant so a future
        /// rounded-controls look is a one-line change rather than a re-coupling to cards.</summary>
        public const int ControlRadius = 0;

        /// <summary>Layout density: Compact is locked in (the tuned interface ran with the
        /// density dropdown on Compact).</summary>
        public static bool Compact => true;

        /// <summary>Task row height in px. Flat-list band (Google Tasks / MS To Do): dense
        /// enough to read as a list, tall enough for title + meta at any DPI — the boxy
        /// per-row card padding is gone, so rows no longer need card-sized heights.</summary>
        public static int TaskRowHeight => Compact ? 56 : 64;

        /// <summary>Vertical padding inside settings rows.</summary>
        public static int SettingsRowPadding => Compact ? 8 : 14;

        /// <summary>Guna hover/press animations enabled (still a live setting).</summary>
        public static bool Animations => current.EnableAnimations;
    }
}
