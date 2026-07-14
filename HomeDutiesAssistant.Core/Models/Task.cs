namespace HomeDutiesAssistant.Models;

public sealed class Task
{
    // 0 means "not yet saved"
    public long Id { get; set; }
    public long HomeId { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Todo;
    public int Priority { get; set; } // manual order (drag-and-drop); assigned by the DB / reorder
}

public enum TaskStatus
{
    Todo,
    InProgress,
    Done,
}