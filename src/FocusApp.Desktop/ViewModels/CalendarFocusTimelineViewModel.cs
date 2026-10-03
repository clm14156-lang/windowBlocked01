namespace FocusApp.Desktop.ViewModels;

public sealed record CalendarFocusTimelineSegment(
    FocusSessionRecordViewModel Record,
    DateTime StartTime,
    DateTime EndTime,
    double StartRatio,
    double WidthRatio);

public sealed record CalendarFocusTimelineViewModel(
    int StartHour,
    IReadOnlyList<CalendarFocusTimelineSegment> Segments)
{
    public static CalendarFocusTimelineViewModel Create(
        DateTime date, IEnumerable<FocusSessionRecordViewModel> records)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        var intervals = records
            .Where(record => record.EndTime > record.StartTime && record.StartTime < dayEnd && record.EndTime > dayStart)
            .OrderBy(record => record.StartTime)
            .Select(record => new
            {
                Record = record,
                Start = record.StartTime < dayStart ? dayStart : record.StartTime,
                End = record.EndTime > dayEnd ? dayEnd : record.EndTime
            }).ToArray();
        // Keep the reference's 08:00–24:00 scale unless it would hide an early session.
        var startHour = intervals.Any(interval => interval.Start < dayStart.AddHours(8)) ? 0 : 8;
        var axisStart = dayStart.AddHours(startHour);
        var axisTicks = (dayEnd - axisStart).Ticks;
        return new CalendarFocusTimelineViewModel(startHour, intervals.Select(interval =>
            new CalendarFocusTimelineSegment(interval.Record, interval.Start, interval.End,
                (interval.Start - axisStart).Ticks / (double)axisTicks,
                (interval.End - interval.Start).Ticks / (double)axisTicks)).ToArray());
    }
}
