using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Calendar;

/// <summary>
/// Coordinates reminder presentation with the dedicated runtime reminder state
/// and overlay
/// visibility. Reminder callers do not rely on unrelated event-subscription
/// order to reveal a Focus-hidden companion.
/// </summary>
public sealed class CalendarReminderPresentationCoordinator
{
    private readonly CompanionManager _manager;
    private readonly Action _applyVisibility;
    private readonly Action<CalendarEvent, DateTime> _showReminder;
    private readonly Action _clearReminder;

    public CalendarReminderPresentationCoordinator(
        CompanionManager manager,
        Action applyVisibility,
        Action<CalendarEvent, DateTime> showReminder,
        Action clearReminder)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _applyVisibility = applyVisibility ?? throw new ArgumentNullException(nameof(applyVisibility));
        _showReminder = showReminder ?? throw new ArgumentNullException(nameof(showReminder));
        _clearReminder = clearReminder ?? throw new ArgumentNullException(nameof(clearReminder));
    }

    public void Present(CalendarReminderEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _manager.SetActivityState(CompanionActivityState.Reminder);
        try
        {
            // SetActivityState normally raises the visibility event, but applying the
            // policy explicitly makes reminder delivery independent of event
            // subscription order and covers a manager already in reminder state.
            _applyVisibility();
            _showReminder(args.CalendarEvent, args.OccurrenceStart);
        }
        catch
        {
            _manager.SetActivityState(CompanionActivityState.Idle);
            _applyVisibility();
            throw;
        }
    }

    public void End(CalendarReminderEndedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            if (args.WasPresenting)
                _manager.SetActivityState(CompanionActivityState.Idle);
        }
        finally
        {
            try
            {
                _clearReminder();
            }
            finally
            {
                _applyVisibility();
            }
        }
    }
}
