namespace VibeAlarm.Models
{
    /// <summary>
    /// Strongly-typed, versioned application settings persisted to settings.json.
    /// SchemaVersion allows safe forward migration as fields are added over time.
    /// </summary>
    public sealed partial class AppSettings
    {
        /// <summary>Bumped when the settings schema changes incompatibly. v2 added the
        /// appearance fields (transparency, radius, density, toggles) — additive, so v1 files
        /// load unchanged and take the new defaults.</summary>
        public int SchemaVersion { get; set; } = 2;

        /// <summary>The persisted accent key for the Settings picker — one of the eight named
        /// AccentCatalog accents ("Obsidian Core", "Neo Blue", "Violet System", "Matrix
        /// Green", "Sunset Red", "Solar Amber", "Pearl White", "Platinum Silver").
        /// Defaults to "Matrix Green". Earlier eras' short keys ("Fluent", "Forest", …)
        /// and the retired "Spotify Green" key map onto their corresponding colors on
        /// load. Unknown keys resolve back to the default, never throw.
        /// Additive field: older settings.json files load unchanged.</summary>
        public string AccentColor { get; set; } = "Matrix Green";

        /// <summary>True to hide-to-tray (rather than exit) when the window's close button is used.</summary>
        public bool MinimizeToTray { get; set; }

        /// <summary>Last active navigation view name, so returning to the app doesn't reset nav.</summary>
        public string LastActiveView { get; set; } = "Tasks";

        /// <summary>Last displayed calendar month (yyyy-MM), preserved across restarts.</summary>
        public string LastViewedCalendarMonth { get; set; } = string.Empty;
    }
}