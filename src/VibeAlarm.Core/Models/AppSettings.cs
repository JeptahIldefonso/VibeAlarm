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
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>Minutes an alarm can be missed while the computer was asleep/off and still ring normally.
        /// If older than this threshold, it is logged to History as a Missed Alarm and never rings late.</summary>
        public int MissedAlarmGracePeriodMinutes { get; set; } = 10;

        /// <summary>When running at Windows startup, start minimized in the tray without opening the window.</summary>
        public bool StartWithWindowsHidden { get; set; } = true;

        /// <summary>Last active navigation view name, so returning to the app doesn't reset nav.</summary>
        public string LastActiveView { get; set; } = "Tasks";

        /// <summary>Last displayed calendar month (yyyy-MM), preserved across restarts.</summary>
        public string LastViewedCalendarMonth { get; set; } = string.Empty;

        /// <summary>Shared master playback volume (0 to 100).</summary>
        public int MasterVolume { get; set; } = 70;

        /// <summary>Persisted shuffle mode preference for music playback.</summary>
        public bool MusicShuffle { get; set; }

        /// <summary>Persisted repeat mode preference ("off", "all", "one").</summary>
        public string MusicRepeatMode { get; set; } = "off";

        /// <summary>Last played track ID for session restore.</summary>
        public string? LastPlayedTrackId { get; set; }

        /// <summary>Last playback position in milliseconds.</summary>
        public int LastPlaybackPositionMs { get; set; }

        /// <summary>Persisted list of music folder paths added to the library.</summary>
        public List<string> MusicFolders { get; set; } = new();

        /// <summary>Active alarm sound identifier: "Default", "Classic Beep", "Soft Chime", "Digital Pulse", "Rising Tone", or "Custom".</summary>
        public string AlarmSound { get; set; } = "Default";

        /// <summary>Relative file name of the custom alarm sound in Assets\AlarmSounds, if AlarmSound == "Custom".</summary>
        public string? CustomAlarmSoundPath { get; set; }

        /// <summary>Display name of the custom alarm sound.</summary>
        public string? CustomAlarmSoundName { get; set; }

        /// <summary>Alarm playback volume (10-100), separate from ambient and music volume.</summary>
        public int AlarmVolume { get; set; } = 80;

        /// <summary>Minutes the alarm rings before stopping automatically (0 = ring until dismissed).</summary>
        public int AlarmDurationMinutes { get; set; } = 5;

        /// <summary>Snooze duration in minutes (e.g. 5, 9, 10, 15).</summary>
        public int SnoozeDurationMinutes { get; set; } = 9;

        /// <summary>Gradually ramp up the alarm volume over 30 seconds.</summary>
        public bool GradualVolumeIncrease { get; set; } = false;
    }
}