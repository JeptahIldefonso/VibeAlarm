namespace VibeAlarm;

public sealed class TaskItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string RemindTime { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Completed { get; set; }
}
