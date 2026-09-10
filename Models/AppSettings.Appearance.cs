namespace VibeAlarm.Models;

/// <summary>
/// Appearance-related settings (transparency, density, motion, notification behavior) for the
/// glass interface. Split from the core settings file so the appearance schema reads as one
/// unit; the properties merge into the same <see cref="AppSettings"/> class serialized to
/// settings.json. All transparency values are user-facing 0–100 percentages where 0 = opaque.
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>Transparency of the primary glass surfaces (sidebar), 0–100%. 0 = opaque.</summary>
    public int GlassTransparency { get; set; } = 30;

    /// <summary>Transparency of raised panels (status bar, toolbars), 0–100%. 0 = opaque.</summary>
    public int PanelTransparency { get; set; } = 15;

    /// <summary>Transparency of content cards (task rows, headers), 0–100%. 0 = opaque.</summary>
    public int CardTransparency { get; set; } = 10;

    /// <summary>Transparency of hairline borders, 0–100%. 0 = opaque.</summary>
    public int BorderTransparency { get; set; } = 25;

    /// <summary>Base corner radius in px (cards); controls use ~2/3 of this. 0–20.</summary>
    public int CornerRadius { get; set; } = 12;

    /// <summary>Layout density: "Comfortable" or "Compact".</summary>
    public string Density { get; set; } = "Comfortable";

    /// <summary>Smooth hover/press transitions on controls.</summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>Show desktop notifications when tasks fire (gates the notification surface,
    /// never the scheduler itself).</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Play the alarm sound when an alarm fires.</summary>
    public bool EnableAlarmSound { get; set; } = true;

    /// <summary>Ask for confirmation before tasks are deleted or cleared.</summary>
    public bool ConfirmBeforeDelete { get; set; } = true;
}
