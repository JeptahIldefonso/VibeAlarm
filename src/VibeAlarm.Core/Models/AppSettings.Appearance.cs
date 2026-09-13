namespace VibeAlarm.Models;

/// <summary>
/// Appearance-related settings for the glass interface. Split from the core settings file so
/// the appearance schema reads as one unit; the properties merge into the same
/// <see cref="AppSettings"/> class serialized to settings.json. The former transparency,
/// corner-radius, density, and background-image settings were removed wholesale — those
/// values are now hardcoded design constants in
/// <see cref="VibeAlarm.UI.Theming.Appearance"/> and
/// <see cref="VibeAlarm.UI.Theming.GlassSurface"/>, so Settings offers only behavior
/// toggles, not layout dials.
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>Show desktop notifications when tasks fire (gates the notification surface,
    /// never the scheduler itself).</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Play the alarm sound when an alarm fires.</summary>
    public bool EnableAlarmSound { get; set; } = true;

    /// <summary>Ask for confirmation before tasks are deleted or cleared.</summary>
    public bool ConfirmBeforeDelete { get; set; } = true;
}
