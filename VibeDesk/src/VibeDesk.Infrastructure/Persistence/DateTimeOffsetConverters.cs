using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VibeDesk.Infrastructure.Persistence;

/// <summary>
/// SQLite has no native <see cref="DateTimeOffset"/>, and its default text storage breaks ordering
/// once offsets differ. Storing UTC ticks keeps comparisons and range queries correct — which matters
/// because the calendar grid and Drive listings both sort and filter on these columns.
/// </summary>
public sealed class DateTimeOffsetToTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public static readonly DateTimeOffsetToTicksConverter Instance = new();

    public DateTimeOffsetToTicksConverter()
        : base(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero))
    {
    }
}

public sealed class NullableDateTimeOffsetToTicksConverter : ValueConverter<DateTimeOffset?, long?>
{
    public static readonly NullableDateTimeOffsetToTicksConverter Instance = new();

    public NullableDateTimeOffsetToTicksConverter()
        : base(
            v => v.HasValue ? v.Value.UtcTicks : null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null)
    {
    }
}
