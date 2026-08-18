namespace VibeDesk.Application.Scripting;

/// <summary>
/// A five-field cron expression: <c>minute hour day-of-month month day-of-week</c>.
/// </summary>
/// <remarks>
/// Written here rather than taken from a package because the need is small and exact: parse, and
/// answer "when next". Supports <c>*</c>, lists (<c>1,15</c>), ranges (<c>9-17</c>) and steps
/// (<c>*/15</c>), plus the usual shorthands. Day-of-week accepts 0 or 7 for Sunday.
/// </remarks>
public sealed class CronSchedule
{
    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _days = new bool[32];
    private readonly bool[] _months = new bool[13];
    private readonly bool[] _weekdays = new bool[7];

    /// <summary>True when day-of-month and day-of-week are both restricted — cron ORs them.</summary>
    private readonly bool _dayRestricted;
    private readonly bool _weekdayRestricted;

    private CronSchedule(string[] fields)
    {
        Fill(_minutes, fields[0], 0, 59);
        Fill(_hours, fields[1], 0, 23);
        Fill(_days, fields[2], 1, 31);
        Fill(_months, fields[3], 1, 12);
        Fill(_weekdays, Normalise(fields[4]), 0, 6);

        _dayRestricted = fields[2].Trim() != "*";
        _weekdayRestricted = fields[4].Trim() != "*";
    }

    public static bool TryParse(string? expression, out CronSchedule? schedule)
    {
        schedule = null;

        if (string.IsNullOrWhiteSpace(expression)) return false;

        var text = Expand(expression.Trim());
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length != 5) return false;

        try
        {
            schedule = new CronSchedule(fields);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The next firing strictly after <paramref name="afterUtc"/>, in the given zone. Returns null
    /// when nothing matches within a year, which is how an impossible date like 30 February shows up.
    /// </summary>
    public DateTimeOffset? Next(DateTimeOffset afterUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(afterUtc, zone);

        // Start at the next whole minute: cron has minute resolution, and firing twice inside one
        // minute is the classic scheduler bug.
        var candidate = new DateTimeOffset(
            local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset)
            .AddMinutes(1);

        var limit = candidate.AddYears(1);

        while (candidate < limit)
        {
            if (Matches(candidate))
            {
                // Re-resolve the offset: the naive arithmetic above can land on a wall-clock time
                // that a DST transition moved.
                var wall = candidate.DateTime;
                return new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
            }

            candidate = candidate.AddMinutes(1);
        }

        return null;
    }

    /// <summary>
    /// Next firing for an expression, or a day out when it cannot be parsed. Pushing a broken
    /// expression forward rather than retrying it every tick keeps a typo from becoming a hot loop.
    /// </summary>
    public static DateTimeOffset NextRun(string? expression, string timeZoneId, DateTimeOffset nowUtc)
    {
        if (!TryParse(expression, out var schedule) || schedule is null) return nowUtc.AddDays(1);

        return schedule.Next(nowUtc, ResolveZone(timeZoneId)) ?? nowUtc.AddDays(1);
    }

    /// <summary>Windows and Linux disagree on time-zone ids; UTC keeps the schedule running.</summary>
    public static TimeZoneInfo ResolveZone(string id)
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

    private bool Matches(DateTimeOffset t)
    {
        if (!_minutes[t.Minute] || !_hours[t.Hour] || !_months[t.Month]) return false;

        var dayOk = _days[t.Day];
        var weekdayOk = _weekdays[(int)t.DayOfWeek];

        // Standard cron semantics: when both day fields are restricted, either one firing is enough.
        return _dayRestricted && _weekdayRestricted
            ? dayOk || weekdayOk
            : dayOk && weekdayOk;
    }

    private static string Expand(string expression) => expression.ToLowerInvariant() switch
    {
        "@hourly" => "0 * * * *",
        "@daily" or "@midnight" => "0 0 * * *",
        "@weekly" => "0 0 * * 0",
        "@monthly" => "0 0 1 * *",
        "@yearly" or "@annually" => "0 0 1 1 *",
        _ => expression,
    };

    /// <summary>Cron allows 7 for Sunday; the arrays are indexed 0–6.</summary>
    private static string Normalise(string weekday) => weekday.Replace("7", "0");

    private static void Fill(bool[] target, string field, int min, int max)
    {
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var step = 1;
            var body = part;

            if (body.Contains('/'))
            {
                var halves = body.Split('/', 2);
                body = halves[0];
                step = int.Parse(halves[1]);

                if (step <= 0) throw new FormatException("A cron step must be positive.");
            }

            int from, to;

            if (body is "*")
            {
                from = min;
                to = max;
            }
            else if (body.Contains('-'))
            {
                var range = body.Split('-', 2);
                from = int.Parse(range[0]);
                to = int.Parse(range[1]);
            }
            else
            {
                from = to = int.Parse(body);
            }

            if (from < min || to > max || from > to) throw new FormatException($"'{part}' is out of range.");

            for (var value = from; value <= to; value += step) target[value] = true;
        }
    }
}
