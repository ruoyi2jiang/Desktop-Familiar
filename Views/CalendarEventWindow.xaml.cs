using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Views;

public partial class CalendarEventWindow : Window
{
    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record CalendarEventOccurrenceTag(Guid EventId, DateTime OccurrenceDate);

    private readonly CompanionManager _manager;
    private readonly Dictionary<DateTime, Border> _dayCells = [];
    private static readonly Brush PastDayBackground = new SolidColorBrush(Color.FromRgb(224, 224, 224));
    private static readonly Brush PastDayBorder = new SolidColorBrush(Color.FromRgb(196, 196, 196));
    private static readonly Brush PastDayText = new SolidColorBrush(Color.FromRgb(119, 119, 119));
    private CalendarEvent _editing = new();
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _rangeStart;
    private DateTime? _rangeEnd;
    private bool _selectingRange;
    private bool _loading;
    private bool _updatingMonthSelection;
    private DateTime? _editingOccurrenceDate;

    public CalendarEventWindow(CompanionManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _manager.CalendarEventsChanged += ManagerCalendarEventsChanged;
        PopulateChoices();
        RefreshMonth();
        Closed += (_, _) => _manager.CalendarEventsChanged -= ManagerCalendarEventsChanged;
    }

    private void PopulateChoices()
    {
        MonthSelector.ItemsSource = Enumerable.Range(1, 12)
            .Select(month => new Choice<int>(
                month,
                CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)))
            .ToList();
        var times = Enumerable.Range(0, 48)
            .Select(index => TimeSpan.FromMinutes(index * 30))
            .Select(time => new Choice<TimeSpan>(time, time.ToString(@"hh\:mm")))
            .ToList();
        StartTimeCombo.ItemsSource = times;
        EndTimeCombo.ItemsSource = times;
        CategoryCombo.ItemsSource = Enum.GetValues<CalendarEventCategory>()
            .Select(category => new Choice<CalendarEventCategory>(category, LocalizationService.Get($"Calendar.Category.{category}")))
            .ToList();
        RecurrenceCombo.ItemsSource = new[]
        {
            new Choice<CalendarEventRecurrence>(CalendarEventRecurrence.OneTime, LocalizationService.Get("Calendar.Recurrence.OneTime")),
            new Choice<CalendarEventRecurrence>(CalendarEventRecurrence.Weekly, LocalizationService.Get("Calendar.Recurrence.Weekly"))
        };
    }

    private void ManagerCalendarEventsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshMonth);

    private void RefreshMonth()
    {
        _displayMonth = CalendarNavigationPolicy.ClampMonth(_displayMonth, DateTime.Today);
        SynchronizeMonthSelectors();
        CalendarGrid.Children.Clear();
        _dayCells.Clear();
        var firstVisibleDay = _displayMonth.AddDays(-(int)_displayMonth.DayOfWeek);
        for (var index = 0; index < 42; index++)
            CalendarGrid.Children.Add(CreateDayCell(firstVisibleDay.AddDays(index)));
    }

    private void SynchronizeMonthSelectors()
    {
        _updatingMonthSelection = true;
        try
        {
            MonthSelector.SelectedItem = MonthSelector.Items
                .OfType<Choice<int>>()
                .First(choice => choice.Value == _displayMonth.Month);
            var knownDates = _manager.Data.CalendarEvents
                .Select(calendarEvent => calendarEvent.EndDate ?? calendarEvent.Date);
            YearSelector.ItemsSource = CalendarNavigationPolicy.GetSelectableYears(
                DateTime.Today,
                _displayMonth,
                knownDates);
            YearSelector.SelectedItem = _displayMonth.Year;
            PreviousMonthButton.IsEnabled = CalendarNavigationPolicy.CanMoveToPreviousMonth(
                _displayMonth,
                DateTime.Today);
        }
        finally
        {
            _updatingMonthSelection = false;
        }
    }

    private void CalendarMonthSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMonthSelection
            || MonthSelector.SelectedItem is not Choice<int> month
            || YearSelector.SelectedItem is not int year)
        {
            return;
        }

        _displayMonth = CalendarNavigationPolicy.ClampMonth(
            new DateTime(year, month.Value, 1),
            DateTime.Today);
        RefreshMonth();
    }

    private Border CreateDayCell(DateTime date)
    {
        var isPast = date < DateTime.Today;
        var isToday = date == DateTime.Today;
        var cell = new Border
        {
            Tag = date,
            Margin = new Thickness(1),
            Padding = new Thickness(7),
            MinHeight = 94,
            Opacity = date.Month == _displayMonth.Month ? 1 : 0.56,
            Cursor = isPast ? Cursors.No : Cursors.Hand,
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true
        };
        // Past calendar cells remain visible so the month retains its natural layout,
        // but they are deliberately not interactive for creating an event. Existing
        // event chips keep their own click handler, so an older event can still be
        // opened and reviewed without making its day selectable.
        if (!isPast)
        {
            cell.MouseLeftButtonDown += CalendarDay_MouseLeftButtonDown;
            cell.MouseEnter += CalendarDay_MouseEnter;
            cell.MouseLeftButtonUp += CalendarDay_MouseLeftButtonUp;
        }
        _dayCells[date] = cell;
        ApplyDayCellAppearance(cell, date);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var dayNumber = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.CurrentCulture),
            FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = isPast
                ? PastDayText
                : (Brush)FindResource(isToday ? ThemeTokens.PrimaryAccent : ThemeTokens.PrimaryTextOnBackground),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        content.Children.Add(dayNumber);

        var eventsPanel = new StackPanel
        {
            Margin = new Thickness(0, 5, 0, 0),
            ClipToBounds = true
        };
        Grid.SetRow(eventsPanel, 1);
        var events = GetEventsForDate(date).ToList();
        foreach (var calendarEvent in events.Take(3))
            eventsPanel.Children.Add(CreateEventBlock(calendarEvent, date));
        if (events.Count > 3)
        {
            eventsPanel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Format("Calendar.Month.More", events.Count - 3),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource(ThemeTokens.PrimaryTextOnBackground),
                Margin = new Thickness(2, 2, 0, 0)
            });
        }
        content.Children.Add(eventsPanel);
        cell.Child = content;
        return cell;
    }

    private void ApplyDayCellAppearance(Border cell, DateTime date)
    {
        if (date < DateTime.Today)
        {
            cell.Background = PastDayBackground;
            cell.BorderBrush = PastDayBorder;
            cell.BorderThickness = new Thickness(1);
            return;
        }

        var isSelected = _rangeStart is DateTime start
            && _rangeEnd is DateTime end
            && date >= Min(start, end)
            && date <= Max(start, end);
        cell.BorderBrush = (Brush)FindResource(isSelected ? ThemeTokens.PrimaryAccent : ThemeTokens.PrimaryBackgroundHover);
        cell.BorderThickness = new Thickness(isSelected ? 1.5 : 1);
        cell.Background = (Brush)FindResource(isSelected
            ? ThemeTokens.PrimaryBackgroundActive
            : date.Month == _displayMonth.Month ? ThemeTokens.PrimaryBackground : ThemeTokens.PrimaryBackgroundHover);
    }

    private void UpdateRangeHighlight()
    {
        foreach (var (date, cell) in _dayCells)
            ApplyDayCellAppearance(cell, date);
    }

    private Border CreateEventBlock(CalendarEvent calendarEvent, DateTime occurrenceDate)
    {
        var status = CalendarOccurrenceStatusTracker.Resolve(calendarEvent, occurrenceDate);
        var isExpired = status == CalendarOccurrenceStatus.Expired;
        var block = new Border
        {
            Tag = new CalendarEventOccurrenceTag(calendarEvent.Id, occurrenceDate.Date),
            Background = isExpired ? null : GetCategoryBrush(calendarEvent.Category),
            BorderBrush = isExpired ? null : Brushes.Transparent,
            BorderThickness = new Thickness(isExpired ? 1 : 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(0, 0, 0, 3),
            ToolTip = $"{FormatEventPeriod(calendarEvent)} - {calendarEvent.Title} · {GetStatusLabel(status)}",
            Opacity = isExpired ? 0.68 : 1,
            Cursor = Cursors.Hand
        };
        if (isExpired)
        {
            block.SetResourceReference(Border.BackgroundProperty, ThemeTokens.InputBackgroundHover);
            block.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.InputAccent);
        }
        block.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        block.MouseLeftButtonUp += CalendarEventBlock_MouseLeftButtonUp;

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var markers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (status == CalendarOccurrenceStatus.Completed)
        {
            markers.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 1,5 L 4,8 L 10,1"),
                Stroke = Brushes.White,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 10,
                Height = 9,
                Stretch = Stretch.Fill,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        if (calendarEvent.IsUrgent)
        {
            var urgentMarker = new TextBlock
            {
                Text = "!",
                FontWeight = FontWeights.Bold,
                Foreground = isExpired ? null : Brushes.White,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isExpired)
                urgentMarker.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.InputTextOnBackground);
            markers.Children.Add(urgentMarker);
        }
        Grid.SetColumn(markers, 0);
        content.Children.Add(markers);
        var text = new TextBlock
        {
            Text = $"{FormatEventPeriod(calendarEvent)} - {calendarEvent.Title}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = isExpired ? null : Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (isExpired)
            text.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.InputTextOnBackground);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        block.Child = content;
        return block;
    }

    private IEnumerable<CalendarEvent> GetEventsForDate(DateTime date) => _manager.Data.CalendarEvents
        .Where(calendarEvent => CalendarOccurrenceCalculator.OccursOnDate(calendarEvent, date))
        .OrderByDescending(calendarEvent => calendarEvent.IsUrgent)
        .ThenBy(calendarEvent => calendarEvent.IsAllDay ? TimeSpan.Zero : calendarEvent.StartTime)
        .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase);

    private static Brush GetCategoryBrush(CalendarEventCategory category) => category switch
    {
        CalendarEventCategory.Anniversary => new SolidColorBrush(Color.FromRgb(194, 84, 121)),
        CalendarEventCategory.Business => new SolidColorBrush(Color.FromRgb(50, 117, 176)),
        CalendarEventCategory.Life => new SolidColorBrush(Color.FromRgb(69, 143, 104)),
        _ => new SolidColorBrush(Color.FromRgb(121, 91, 166))
    };

    private static string FormatEventPeriod(CalendarEvent calendarEvent) => calendarEvent.IsAllDay
        ? LocalizationService.Get("Calendar.Month.AllDay")
        : LocalizationService.Format(
            "Calendar.Reminder.TimeRange",
            calendarEvent.StartTime.ToString(@"hh\:mm"),
            calendarEvent.EndTime.ToString(@"hh\:mm"));

    private void CalendarDay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date })
            return;
        if (date < DateTime.Today)
            return;

        _rangeStart = date;
        _rangeEnd = date;
        _selectingRange = true;
        UpdateRangeHighlight();
        e.Handled = true;
    }

    private void CalendarDay_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_selectingRange
            || e.LeftButton != MouseButtonState.Pressed
            || sender is not Border { Tag: DateTime date }
            || date < DateTime.Today)
        {
            return;
        }

        _rangeEnd = date;
        UpdateRangeHighlight();
    }

    private void CalendarDay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selectingRange || sender is not Border { Tag: DateTime date })
            return;

        _rangeEnd = date < DateTime.Today ? _rangeEnd : date;
        _selectingRange = false;
        if (_rangeStart is not DateTime start || _rangeEnd is not DateTime end)
            return;

        BeginNewEvent(Min(start, end), Max(start, end));
        e.Handled = true;
    }

    private void CalendarEventBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: CalendarEventOccurrenceTag tag })
            return;
        var calendarEvent = _manager.Data.CalendarEvents.FirstOrDefault(candidate => candidate.Id == tag.EventId);
        if (calendarEvent is not null)
            OpenEditor(calendarEvent, tag.OccurrenceDate);
        e.Handled = true;
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        if (!CalendarNavigationPolicy.CanMoveToPreviousMonth(_displayMonth, DateTime.Today))
            return;
        _displayMonth = CalendarNavigationPolicy.ClampMonth(_displayMonth.AddMonths(-1), DateTime.Today);
        RefreshMonth();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RefreshMonth();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RefreshMonth();
    }

    private void NewEvent_Click(object sender, RoutedEventArgs e)
    {
        var date = _displayMonth < DateTime.Today
            ? DateTime.Today
            : _displayMonth;
        BeginNewEvent(date, date);
    }

    private void BeginNewEvent(DateTime startDate, DateTime endDate)
    {
        _editingOccurrenceDate = null;
        _editing = new CalendarEvent
        {
            Date = startDate,
            EndDate = endDate,
            Category = CalendarEventCategory.Life,
            IsAllDay = true,
            ReminderEnabled = true,
            ReminderAdvanceMinutes = 15,
            SnoozeMinutes = 10
        };
        LoadEvent(_editing, isExisting: false);
    }

    private void OpenEditor(CalendarEvent calendarEvent, DateTime occurrenceDate)
    {
        _editingOccurrenceDate = calendarEvent.Recurrence == CalendarEventRecurrence.Weekly
            ? occurrenceDate.Date
            : null;
        _editing = calendarEvent.Clone();
        LoadEvent(_editing, isExisting: true);
    }

    private void LoadEvent(CalendarEvent calendarEvent, bool isExisting)
    {
        _loading = true;
        try
        {
            var earliestDate = isExisting ? calendarEvent.Date.Date : DateTime.Today;
            StartDatePicker.DisplayDateStart = earliestDate;
            EndDatePicker.DisplayDateStart = earliestDate;
            TitleInput.Text = calendarEvent.Title;
            NotesInput.Text = calendarEvent.Notes;
            StartDatePicker.SelectedDate = calendarEvent.Date.Date;
            EndDatePicker.SelectedDate = (calendarEvent.EndDate ?? calendarEvent.Date).Date;
            AllDayCheck.IsChecked = calendarEvent.IsAllDay;
            StartTimeCombo.SelectedItem = FindChoice(StartTimeCombo, calendarEvent.StartTime);
            EndTimeCombo.SelectedItem = FindChoice(EndTimeCombo, calendarEvent.EndTime);
            CategoryCombo.SelectedItem = FindChoice(CategoryCombo, calendarEvent.Category);
            RecurrenceCombo.SelectedItem = FindChoice(RecurrenceCombo, calendarEvent.Recurrence);
            SetWeekdayChecks(calendarEvent.RecurringDays);
            UrgentCheck.IsChecked = calendarEvent.IsUrgent;
            ReminderEnabledCheck.IsChecked = calendarEvent.ReminderEnabled;
            AdvanceMinutesInput.Text = calendarEvent.ReminderAdvanceMinutes.ToString(CultureInfo.CurrentCulture);
            SnoozeMinutesInput.Text = calendarEvent.SnoozeMinutes.ToString(CultureInfo.CurrentCulture);
            DeleteButton.IsEnabled = isExisting;
            UpdateEventStatusBadge(calendarEvent, isExisting);
            EventEditorOverlay.Visibility = Visibility.Visible;
            ApplyControlState();
        }
        finally
        {
            _loading = false;
        }
    }

    private static object? FindChoice<T>(ItemsControl control, T value) => control.Items
        .OfType<Choice<T>>()
        .FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value));

    private void UpdateEventStatusBadge(CalendarEvent calendarEvent, bool isExisting)
    {
        if (!isExisting)
        {
            EventStatusBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var status = CalendarOccurrenceStatusTracker.Resolve(
            calendarEvent,
            _editingOccurrenceDate ?? calendarEvent.Date);
        EventStatusText.Text = GetStatusLabel(status);
        var (backgroundToken, foregroundToken, borderToken) = status switch
        {
            CalendarOccurrenceStatus.Completed => (
                ThemeTokens.PrimaryAccent,
                ThemeTokens.PrimaryTextOnAccent,
                ThemeTokens.PrimaryAccent),
            CalendarOccurrenceStatus.Expired => (
                ThemeTokens.InputBackgroundHover,
                ThemeTokens.InputTextOnBackground,
                ThemeTokens.InputAccent),
            CalendarOccurrenceStatus.ReminderDisabled => (
                ThemeTokens.PrimaryBackgroundHover,
                ThemeTokens.PrimaryTextOnBackground,
                ThemeTokens.PrimaryBackgroundActive),
            _ => (
                ThemeTokens.PrimaryBackgroundHover,
                ThemeTokens.PrimaryTextOnBackground,
                ThemeTokens.PrimaryAccent)
        };
        EventStatusBadge.SetResourceReference(Border.BackgroundProperty, backgroundToken);
        EventStatusBadge.SetResourceReference(Border.BorderBrushProperty, borderToken);
        EventStatusText.SetResourceReference(TextBlock.ForegroundProperty, foregroundToken);
        EventStatusBadge.Visibility = Visibility.Visible;
    }

    private static string GetStatusLabel(CalendarOccurrenceStatus status) => status switch
    {
        CalendarOccurrenceStatus.Completed => LocalizationService.Get("Calendar.Status.Completed"),
        CalendarOccurrenceStatus.Expired => LocalizationService.Get("Calendar.Status.Expired"),
        CalendarOccurrenceStatus.ReminderDisabled => LocalizationService.Get("Calendar.Status.ReminderDisabled"),
        _ => LocalizationService.Get("Calendar.Status.Scheduled")
    };

    private void AllDayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            ApplyControlState();
    }

    private void RecurrenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            ApplyControlState();
    }

    private void ReminderEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            ApplyControlState();
    }

    private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            ApplyControlState();
    }

    private void ApplyControlState()
    {
        TimeRangePanel.IsEnabled = AllDayCheck.IsChecked != true;
        var spansMultipleDays = StartDatePicker.SelectedDate is DateTime start
            && EndDatePicker.SelectedDate is DateTime end
            && end.Date > start.Date;
        RecurrenceCombo.IsEnabled = !spansMultipleDays;
        if (spansMultipleDays)
            RecurrenceCombo.SelectedItem = FindChoice(RecurrenceCombo, CalendarEventRecurrence.OneTime);
        var weekly = !spansMultipleDays
            && (RecurrenceCombo.SelectedItem as Choice<CalendarEventRecurrence>)?.Value == CalendarEventRecurrence.Weekly;
        WeekdaysPanel.IsEnabled = weekly;
        WeekdaysPanel.Opacity = weekly ? 1 : 0.45;
        ReminderOptionsPanel.IsEnabled = ReminderEnabledCheck.IsChecked == true;
        ReminderOptionsPanel.Opacity = ReminderOptionsPanel.IsEnabled ? 1 : 0.45;
    }

    private CalendarEvent BuildEvent()
    {
        if (StartDatePicker.SelectedDate is not DateTime startDate
            || EndDatePicker.SelectedDate is not DateTime endDate)
        {
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.Date"));
        }
        if (!int.TryParse(AdvanceMinutesInput.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var advance))
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.Advance"));
        if (!int.TryParse(SnoozeMinutesInput.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var snooze))
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.Snooze"));

        var calendarEvent = _editing.Clone();
        calendarEvent.Title = TitleInput.Text;
        calendarEvent.Notes = NotesInput.Text;
        calendarEvent.Date = startDate.Date;
        calendarEvent.EndDate = endDate.Date;
        calendarEvent.IsAllDay = AllDayCheck.IsChecked == true;
        calendarEvent.StartTime = (StartTimeCombo.SelectedItem as Choice<TimeSpan>)?.Value ?? TimeSpan.Zero;
        calendarEvent.EndTime = (EndTimeCombo.SelectedItem as Choice<TimeSpan>)?.Value ?? TimeSpan.FromHours(1);
        calendarEvent.Category = (CategoryCombo.SelectedItem as Choice<CalendarEventCategory>)?.Value ?? CalendarEventCategory.Life;
        calendarEvent.Recurrence = (RecurrenceCombo.SelectedItem as Choice<CalendarEventRecurrence>)?.Value ?? CalendarEventRecurrence.OneTime;
        calendarEvent.RecurringDays = GetSelectedWeekdays();
        calendarEvent.IsUrgent = UrgentCheck.IsChecked == true;
        calendarEvent.ReminderEnabled = ReminderEnabledCheck.IsChecked == true;
        calendarEvent.ReminderAdvanceMinutes = advance;
        calendarEvent.SnoozeMinutes = snooze;
        if (calendarEvent.Recurrence == CalendarEventRecurrence.OneTime)
            calendarEvent.RecurringDays.Clear();
        return calendarEvent;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var calendarEvent = BuildEvent();
            _manager.SaveCalendarEvent(calendarEvent);
            _editing = calendarEvent;
            _displayMonth = new DateTime(calendarEvent.Date.Year, calendarEvent.Date.Month, 1);
            CloseEditor();
            RefreshMonth();
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The calendar event could not be saved.", exception);
            MessageBox.Show(exception.Message, LocalizationService.Get("Calendar.SaveFailed.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_manager.Data.CalendarEvents.Any(calendarEvent => calendarEvent.Id == _editing.Id))
            return;
        if (_editing.Recurrence == CalendarEventRecurrence.Weekly
            && _editingOccurrenceDate is DateTime occurrenceDate)
        {
            var choice = new RecurringEventDeleteDialog(_editing.Title, occurrenceDate)
            {
                Owner = this
            };
            choice.ShowDialog();
            if (choice.Choice == RecurringEventDeleteChoice.Cancel)
                return;
            if (choice.Choice == RecurringEventDeleteChoice.Occurrence)
                _manager.DeleteCalendarOccurrence(_editing.Id, occurrenceDate);
            else
                _manager.DeleteCalendarEvent(_editing.Id);
            CloseEditor();
            return;
        }
        if (MessageBox.Show(
                LocalizationService.Format("Calendar.Delete.Message", _editing.Title),
                LocalizationService.Get("Calendar.Delete.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        _manager.DeleteCalendarEvent(_editing.Id);
        CloseEditor();
        RefreshMonth();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void EventEditorOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, EventEditorOverlay))
            return;

        CloseEditor();
        e.Handled = true;
    }

    private void CloseEditor()
    {
        _selectingRange = false;
        _rangeStart = null;
        _rangeEnd = null;
        _editingOccurrenceDate = null;
        EventEditorOverlay.Visibility = Visibility.Collapsed;
        RefreshMonth();
    }

    private IEnumerable<CheckBox> DayChecks =>
    [
        SundayCheck, MondayCheck, TuesdayCheck, WednesdayCheck,
        ThursdayCheck, FridayCheck, SaturdayCheck
    ];

    private void SetWeekdayChecks(IEnumerable<DayOfWeek> days)
    {
        var selected = days.ToHashSet();
        foreach (var (checkBox, day) in DayChecks.Zip(Enum.GetValues<DayOfWeek>()))
            checkBox.IsChecked = selected.Contains(day);
    }

    private List<DayOfWeek> GetSelectedWeekdays() => DayChecks
        .Zip(Enum.GetValues<DayOfWeek>())
        .Where(pair => pair.First.IsChecked == true)
        .Select(pair => pair.Second)
        .ToList();

    private static DateTime Min(DateTime first, DateTime second) => first <= second ? first : second;

    private static DateTime Max(DateTime first, DateTime second) => first >= second ? first : second;
}
