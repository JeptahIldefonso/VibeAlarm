namespace VibeAlarm.Models;

public sealed class TaskItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ScheduledDate { get; set; } = string.Empty;
    public string RemindTime { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Completed { get; set; }

    /// <summary>Persisted lifecycle state (see <see cref="TaskState"/>). Absent on
    /// old records; normalized by SchedulerService on load.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Gets the current state, mapping the legacy Completed flag when a
    /// persisted State is not present (backward compatibility with older tasks.json).</summary>
    public TaskState GetState()
    {
        if (Enum.TryParse(State, ignoreCase: true, out TaskState parsed) && parsed != TaskState.Scheduled)
        {
            return parsed;
        }
        return Completed ? TaskState.Completed : TaskState.Scheduled;
    }

    /// <summary>Sets the lifecycle state and keeps the legacy Completed flag in sync.</summary>
    public void SetState(TaskState state)
    {
        State = state.ToString();
        Completed = state is TaskState.Completed or TaskState.Triggered;
    }
}