namespace VibeAlarm.Models
{
    /// <summary>
    /// Strongly-typed, versioned application settings persisted to settings.json.
    /// SchemaVersion allows safe forward migration as fields are added over time.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>Bumped when the settings schema changes incompatibly.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>The active theme name (see <see cref="VibeAlarm.Services.ThemeService.Presets"/>).</summary>
        public string Theme { get; set; } = string.Empty;

        /// <summary>True to hide-to-tray (rather than exit) when the window's close button is used.</summary>
        public bool MinimizeToTray { get; set; }

        /// <summary>Absolute path of a user-chosen background image, or empty for the flat preset color.</summary>
        public string BackgroundImagePath { get; set; } = string.Empty;

        /// <summary>0.0–1.0 opacity applied to the background image layer.</summary>
        public double BackgroundOpacity { get; set; } = 1.0;

        /// <summary>Last active navigation view name, so returning to the app doesn't reset nav.</summary>
        public string LastActiveView { get; set; } = "Tasks";

        /// <summary>Last displayed calendar month (yyyy-MM), preserved across restarts.</summary>
        public string LastViewedCalendarMonth { get; set; } = string.Empty;

        /// <summary>Whether a monochrome (desaturated) filter is applied to the background image.</summary>
        public bool ApplyMonochromeFilterToBackground { get; set; } = true;
    }
}