namespace DesktopFamiliar.Models;

/// <summary>A locally stored calendar event and its reminder state.</summary>
public sealed class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public bool IsAllDay { get; set; }
    public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan EndTime { get; set; } = TimeSpan.FromHours(1);
    public CalendarEventRecurrence Recurrence { get; set; } = CalendarEventRecurrence.OneTime;
    public List<DayOfWeek> RecurringDays { get; set; } = [];
    public CalendarEventCategory Category { get; set; } = CalendarEventCategory.Life;
    public bool IsUrgent { get; set; }
    public bool ReminderEnabled { get; set; } = true;
    public int ReminderAdvanceMinutes { get; set; } = 15;
    public int SnoozeMinutes { get; set; } = 10;
    public bool IsCompleted { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? SnoozedUntil { get; set; }
    public List<DateTime> CompletedOccurrences { get; set; } = [];
    public List<DateTime> ExpiredOccurrences { get; set; } = [];
    public List<DateTime> ExcludedOccurrenceDates { get; set; } = [];

    public CalendarEvent Clone() => new()
    {
        Id = Id,
        Title = Title,
        Notes = Notes,
        Date = Date,
        EndDate = EndDate,
        IsAllDay = IsAllDay,
        StartTime = StartTime,
        EndTime = EndTime,
        Recurrence = Recurrence,
        RecurringDays = [.. RecurringDays],
        Category = Category,
        IsUrgent = IsUrgent,
        ReminderEnabled = ReminderEnabled,
        ReminderAdvanceMinutes = ReminderAdvanceMinutes,
        SnoozeMinutes = SnoozeMinutes,
        IsCompleted = IsCompleted,
        IsExpired = IsExpired,
        SnoozedUntil = SnoozedUntil,
        CompletedOccurrences = [.. CompletedOccurrences],
        ExpiredOccurrences = [.. ExpiredOccurrences],
        ExcludedOccurrenceDates = [.. ExcludedOccurrenceDates]
    };
}
