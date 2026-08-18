using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;

namespace VibeDesk.Ai.Plugins;

/// <summary>
/// Date and time, answered in the user's own zone. Without this the model dates everything from its
/// training cutoff, which is wrong in a calendar app in a way users notice immediately.
/// </summary>
public sealed class TimePlugin(string timeZoneId)
{
    private readonly TimeZoneInfo _zone = Resolve(timeZoneId);

    [KernelFunction("current_datetime")]
    [Description("The current date and time in the user's time zone. Call this before any reasoning that depends on today's date.")]
    public string Now()
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _zone);

        return $"{local:dddd, d MMMM yyyy HH:mm} ({_zone.Id}, UTC{local.Offset:hh\\:mm})";
    }

    [KernelFunction("date_add")]
    [Description("Shifts a date by a number of days and returns the result. Use for questions like 'three weeks from Tuesday'.")]
    public string DateAdd(
        [Description("Start date as yyyy-MM-dd. Leave empty for today.")] string? date,
        [Description("Days to add; may be negative.")] int days)
    {
        var start = ParseOrToday(date);

        return start.AddDays(days).ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture);
    }

    [KernelFunction("days_between")]
    [Description("Whole days from the first date to the second. Negative when the second date is earlier.")]
    public int DaysBetween(
        [Description("First date as yyyy-MM-dd.")] string from,
        [Description("Second date as yyyy-MM-dd.")] string to) =>
        (int)(ParseOrToday(to) - ParseOrToday(from)).TotalDays;

    private DateTime ParseOrToday(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _zone).Date;

    /// <summary>
    /// Windows and Linux disagree on time-zone ids, and .NET only bridges them where ICU is present.
    /// Falling back to UTC keeps the tool answering rather than throwing on a mismatch.
    /// </summary>
    private static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
