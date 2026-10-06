namespace HomeDutiesAssistant.Models;

// A duty that is due on a reminder day (reminders cover duties only, not tasks).
public sealed record Reminder(long HomeId, long DutyId, string Title, DateOnly DueDate);
