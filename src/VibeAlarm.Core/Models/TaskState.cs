namespace VibeAlarm.Models;

/// <summary>
/// Lifecycle state of a scheduled task, persisted alongside the task so that it
/// can be reconstructed deterministically on restart from (persisted state +
/// scheduled time + current system time).
/// </summary>
public enum TaskState
{
    /// <summary>Not yet reached its scheduled date/time.</summary>
    Scheduled,

    /// <summary>Scheduled time reached within the current capture window; the
    /// alarm/notification has not already been fired for this occurrence.</summary>
    Due,

    /// <summary>The trigger (alarm/notification) has been fired.</summary>
    Triggered,

    /// <summary>Explicitly finished by the user (mirrors the legacy Completed flag).</summary>
    Completed,

    /// <summary>Its scheduled date has passed without a trigger; retained as history.</summary>
    Expired
}