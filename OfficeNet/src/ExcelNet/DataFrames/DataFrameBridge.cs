// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Styles;
using Gravicode.Science.GraviFrame;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;

namespace ExcelNet.DataFrames;

/// <summary>
/// Moves data between a worksheet and a <see cref="DataFrame"/>.
/// </summary>
/// <remarks>
/// <para>
/// ExcelNet does not reimplement pandas. GraviFrame — the pandas analogue Gravicode Studios already
/// built — supplies the DataFrame, its typed <see cref="Series"/> columns, group-by, joins, pivots
/// and the rolling and resampling machinery. This class is the whole of ExcelNet's involvement:
/// the conversion at the boundary.
/// </para>
/// <para>
/// The conversion is where the interesting decisions are, and they are all about types. A
/// spreadsheet column is heterogeneous by nature and a DataFrame column is not, so a column has to
/// be given a single type — and the choice cannot be made from the first cell alone, because the
/// first cell of a numeric column that happens to hold "N/A" would make the whole column text.
/// </para>
/// </remarks>
public static class DataFrameBridge
{
    /// <summary>
    /// Reads a worksheet's used range as a DataFrame.
    /// </summary>
    /// <param name="sheet">The sheet to read.</param>
    /// <param name="hasHeader">True when the first row holds column names.</param>
    /// <param name="range">The block to read; the used range when omitted.</param>
    public static DataFrame ToDataFrame(this Worksheet sheet, bool hasHeader = true,
        CellRangeReference? range = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var used = range ?? sheet.UsedRange
            ?? throw new OfficeNetException($"Sheet '{sheet.Name}' is empty.");

        var firstDataRow = hasHeader ? used.Start.Row + 1 : used.Start.Row;
        var rowCount = used.End.Row - firstDataRow + 1;

        if (rowCount <= 0)
        {
            throw new OfficeNetException(
                $"Sheet '{sheet.Name}' has a header but no data rows.");
        }

        var columns = new List<Series>(used.ColumnCount);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        for (var column = used.Start.Column; column <= used.End.Column; column++)
        {
            var name = hasHeader
                ? sheet[new CellReference(used.Start.Row, column)].Text
                : CellReference.ColumnIndexToName(column);

            if (name.Length == 0)
            {
                name = CellReference.ColumnIndexToName(column);
            }

            // Duplicate column names would make lookup by name ambiguous.
            var candidate = name;
            var suffix = 2;

            while (!usedNames.Add(candidate))
            {
                candidate = $"{name}_{suffix++}";
            }

            var values = new CellValue[rowCount];

            for (var i = 0; i < rowCount; i++)
            {
                values[i] = sheet[new CellReference(firstDataRow + i, column)].Value;
            }

            columns.Add(BuildSeries(candidate, values));
        }

        return new DataFrame(columns);
    }

    /// <summary>
    /// Chooses a column's type from its values and builds the matching series.
    /// </summary>
    /// <remarks>
    /// The rule is majority-of-non-empty, not first-non-empty. A column of a thousand numbers with
    /// one "TBD" in it is a numeric column with one missing value — treating it as text because of
    /// that one cell makes every downstream sum and mean impossible.
    /// </remarks>
    private static Series BuildSeries(string name, CellValue[] values)
    {
        int numbers = 0, dates = 0, booleans = 0, texts = 0;

        foreach (var value in values)
        {
            switch (value.ValueType)
            {
                case CellValueType.Number:
                    numbers++;
                    break;
                case CellValueType.DateTime:
                    dates++;
                    break;
                case CellValueType.Boolean:
                    booleans++;
                    break;
                case CellValueType.Text:
                    texts++;
                    break;
            }
        }

        var populated = numbers + dates + booleans + texts;

        if (populated == 0)
        {
            return new TextSeries(name, new string?[values.Length]);
        }

        if (dates > populated / 2)
        {
            var result = new DateTime?[values.Length];

            for (var i = 0; i < values.Length; i++)
            {
                result[i] = values[i].ValueType is CellValueType.DateTime or CellValueType.Number
                    ? values[i].AsDateTime()
                    : null;
            }

            return new DateTimeSeries(name, result);
        }

        if (booleans > populated / 2)
        {
            var result = new bool?[values.Length];

            for (var i = 0; i < values.Length; i++)
            {
                result[i] = values[i].ValueType == CellValueType.Boolean ? values[i].AsBoolean() : null;
            }

            return new BooleanSeries(name, result);
        }

        if (numbers > populated / 2)
        {
            var result = new double[values.Length];

            for (var i = 0; i < values.Length; i++)
            {
                // GraviFrame marks a missing numeric value with NaN, which is what its own CSV
                // reader does and what its statistics skip.
                result[i] = values[i].ValueType is CellValueType.Number or CellValueType.Boolean
                    ? values[i].AsNumber()
                    : double.NaN;
            }

            return new NumericSeries(name, result);
        }

        var text = new string?[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            text[i] = values[i].IsEmpty ? null : values[i].AsText();
        }

        return new TextSeries(name, text);
    }

    /// <summary>
    /// Writes a DataFrame to a worksheet, with a formatted header row.
    /// </summary>
    /// <param name="workbook">The workbook to write into.</param>
    /// <param name="frame">The data.</param>
    /// <param name="sheetName">The sheet to write to; created when it does not exist.</param>
    /// <param name="headerColor">The header fill colour.</param>
    public static Worksheet WriteDataFrame(this Workbook workbook, DataFrame frame,
        string sheetName = "Data", OfficeColor? headerColor = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(frame);

        var trimmed = sheetName.Length > 31 ? sheetName[..31] : sheetName;
        var sheet = workbook.Find(trimmed) ?? workbook.AddSheetUnique(trimmed);

        sheet.WriteHeader(new CellReference(0, 0), [.. frame.ColumnNames],
            headerColor ?? OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        var dateStyle = CellStyle.Default.WithNumberFormat(NumberFormats.ShortDate);
        var numberStyle = CellStyle.Default.WithNumberFormat(NumberFormats.ThousandsTwoDecimals);

        for (var column = 0; column < frame.ColumnCount; column++)
        {
            var series = frame.Columns[column];

            for (var row = 0; row < frame.RowCount; row++)
            {
                var reference = new CellReference(row + 1, column);

                if (series.IsMissing(row))
                {
                    continue;
                }

                switch (series)
                {
                    case NumericSeries numeric:
                    {
                        var value = numeric.Values[row];

                        // NaN is GraviFrame's missing marker, not a value to write; a cell holding
                        // NaN shows as #NUM! in Excel.
                        if (double.IsNaN(value))
                        {
                            continue;
                        }

                        sheet.SetValue(reference, CellValue.FromNumber(value));

                        // Whole numbers get no decimals: an integer column formatted "#,##0.00"
                        // reads as money when it is a count.
                        if (row == 0 && HasFractionalValues(numeric))
                        {
                            sheet.SetStyle(reference, numberStyle);
                        }

                        break;
                    }

                    case DateTimeSeries:
                        sheet.SetValue(reference,
                            CellValue.FromDateTime((DateTime)series.GetValue(row)!));
                        sheet.SetStyle(reference, dateStyle);
                        break;

                    case BooleanSeries:
                        sheet.SetValue(reference, CellValue.FromBoolean((bool)series.GetValue(row)!));
                        break;

                    default:
                        sheet.SetValue(reference,
                            CellValue.FromText(series.GetValue(row)?.ToString() ?? string.Empty));
                        break;
                }
            }

            // Applying the numeric format once per column rather than per cell keeps the style
            // table small; the range applies it to the rows that hold values.
            if (series is NumericSeries n && HasFractionalValues(n) && frame.RowCount > 0)
            {
                sheet.Range(1, column, frame.RowCount, column).ApplyStyle(numberStyle);
            }
            else if (series is DateTimeSeries && frame.RowCount > 0)
            {
                sheet.Range(1, column, frame.RowCount, column).ApplyStyle(dateStyle);
            }
        }

        sheet.AutoFitColumns();
        return sheet;
    }

    private static bool HasFractionalValues(NumericSeries series)
    {
        foreach (var value in series.Values)
        {
            if (!double.IsNaN(value) && Math.Abs(value % 1) > 1e-9)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads a worksheet's used range as a DataFrame and returns a summary of it.</summary>
    /// <remarks>
    /// A convenience over <see cref="ToDataFrame"/> plus GraviFrame's <c>Describe</c>, for the
    /// common "what is in this file" question.
    /// </remarks>
    public static DataFrame Describe(this Worksheet sheet, bool hasHeader = true) =>
        sheet.ToDataFrame(hasHeader).Describe();
}
