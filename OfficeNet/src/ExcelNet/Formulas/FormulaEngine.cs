// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using OfficeNet.Core;

namespace ExcelNet.Formulas;

/// <summary>
/// Evaluates the formulas in a workbook and caches their results.
/// </summary>
/// <remarks>
/// <para>
/// Excel recalculates when it opens a file, so this exists for everything that is not Excel: a PDF
/// export, a CSV export, a web preview, or code reading <c>cell.Value</c>. Without it a
/// freshly-written <c>=SUM(B2:B10)</c> reads back as empty, because the file stores the formula and
/// a cached result that does not exist yet.
/// </para>
/// <para>
/// The supported function set is the spreadsheet vocabulary a report actually uses. It is not all
/// of Excel's 500-odd functions, and an unsupported one evaluates to <c>#NAME?</c> — the same error
/// Excel gives — rather than throwing, so one unknown function does not abort a workbook-wide
/// recalculation.
/// </para>
/// </remarks>
public sealed class FormulaEngine
{
    private readonly Workbook _workbook;
    private readonly Dictionary<(string Sheet, CellReference Cell), CellValue> _computed = [];
    private readonly HashSet<(string Sheet, CellReference Cell)> _inProgress = [];

    /// <summary>Creates an engine over a workbook.</summary>
    public FormulaEngine(Workbook workbook) =>
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));

    /// <summary>Evaluates every formula in the workbook, caching each result in its cell.</summary>
    /// <returns>How many formulas were evaluated.</returns>
    public int EvaluateAll()
    {
        var count = 0;

        foreach (var sheet in _workbook.Worksheets)
        {
            foreach (var reference in sheet.RawCells
                         .Where(pair => pair.Value.Formula is not null)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                var result = Evaluate(sheet, reference);

                if (sheet.TryGetCellData(reference, out var data))
                {
                    data.Value = result;
                    sheet.PutCellData(reference, data);
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>Evaluates a single formula string in the context of a sheet.</summary>
    public CellValue Evaluate(Worksheet sheet, string formula)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(formula);

        try
        {
            var parser = new Parser(formula, this, sheet);
            return parser.ParseExpression();
        }
        catch (FormulaException ex)
        {
            return CellValue.FromError(ex.Code);
        }
    }

    private CellValue Evaluate(Worksheet sheet, CellReference reference)
    {
        var key = (sheet.Name, reference);

        if (_computed.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // A formula that reaches itself is a circular reference. Excel reports #REF! and stops;
        // without this guard the evaluation recurses until the stack runs out.
        if (!_inProgress.Add(key))
        {
            return CellValue.FromError("#REF!");
        }

        try
        {
            var formula = sheet.GetFormula(reference);

            var result = formula is null
                ? sheet.GetValue(reference)
                : Evaluate(sheet, formula);

            _computed[key] = result;
            return result;
        }
        finally
        {
            _inProgress.Remove(key);
        }
    }

    internal CellValue ValueOf(Worksheet sheet, CellReference reference) => Evaluate(sheet, reference);

    internal Worksheet ResolveSheet(Worksheet current, string? name)
    {
        if (name is null)
        {
            return current;
        }

        // A quoted sheet name in a reference doubles its own apostrophes.
        var cleaned = name.Trim('\'').Replace("''", "'");

        return _workbook.Find(cleaned)
               ?? throw new FormulaException("#REF!");
    }

    // ---- Parser ---------------------------------------------------------------------------------

    private sealed class FormulaException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }

    /// <summary>
    /// A recursive-descent parser and evaluator over one formula string.
    /// </summary>
    /// <remarks>
    /// Parsing and evaluation are fused: a formula is evaluated once per recalculation, so building
    /// a syntax tree first would allocate for no benefit. The precedence climbs through
    /// comparison, concatenation, addition, multiplication, exponent, unary and postfix percent —
    /// which is Excel's own order, and getting <c>-2^2</c> wrong (it is 4 in Excel, not -4) is the
    /// classic sign that it was implemented from intuition.
    /// </remarks>
    /// <summary>
    /// A range argument, carrying the shape a flat list loses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An argument is a list because <c>SUM(A1:A10)</c> passes ten values through one position. That
    /// is enough for every function that treats its input as a bag of numbers, and not enough for
    /// the lookup family: <c>VLOOKUP(x, A1:C50, 3)</c> has to know the table is three columns wide,
    /// and a flat list of 150 values cannot say whether it is 3x50, 50x3, or 150x1.
    /// </para>
    /// <para>
    /// It derives from the list rather than replacing it, so the hundred functions that do not care
    /// about shape are untouched and the handful that do can ask.
    /// </para>
    /// </remarks>
    private sealed class RangeArgument : List<CellValue>
    {
        public RangeArgument(IEnumerable<CellValue> values, int rows, int columns) : base(values)
        {
            Rows = rows;
            Columns = columns;
        }

        public int Rows { get; }

        public int Columns { get; }

        /// <summary>The value at a zero-based row and column. Values are stored row-major.</summary>
        public CellValue At(int row, int column) =>
            row < 0 || row >= Rows || column < 0 || column >= Columns
                ? CellValue.FromError("#REF!")
                : this[(row * Columns) + column];
    }

    private sealed class Parser(string text, FormulaEngine engine, Worksheet sheet)
    {
        private int _position;

        internal CellValue ParseExpression()
        {
            var value = ParseComparison();
            SkipWhitespace();

            return value;
        }

        private CellValue ParseComparison()
        {
            var left = ParseConcatenation();

            while (true)
            {
                SkipWhitespace();

                var op = PeekOperator();

                if (op is not ("=" or "<>" or "<" or ">" or "<=" or ">="))
                {
                    return left;
                }

                _position += op.Length;
                var right = ParseConcatenation();

                var comparison = Compare(left, right);

                left = CellValue.FromBoolean(op switch
                {
                    "=" => comparison == 0,
                    "<>" => comparison != 0,
                    "<" => comparison < 0,
                    ">" => comparison > 0,
                    "<=" => comparison <= 0,
                    _ => comparison >= 0,
                });
            }
        }

        private static int Compare(CellValue a, CellValue b)
        {
            // Text compares case-insensitively in Excel, and text always sorts after any number.
            var aIsText = a.ValueType == CellValueType.Text;
            var bIsText = b.ValueType == CellValueType.Text;

            if (aIsText && bIsText)
            {
                return string.Compare(a.AsText(), b.AsText(), StringComparison.OrdinalIgnoreCase);
            }

            if (aIsText != bIsText)
            {
                return aIsText ? 1 : -1;
            }

            return a.AsNumber().CompareTo(b.AsNumber());
        }

        private CellValue ParseConcatenation()
        {
            var left = ParseAdditive();

            while (true)
            {
                SkipWhitespace();

                if (_position >= text.Length || text[_position] != '&')
                {
                    return left;
                }

                _position++;
                var right = ParseAdditive();
                left = CellValue.FromText(left.AsText() + right.AsText());
            }
        }

        private CellValue ParseAdditive()
        {
            var left = ParseMultiplicative();

            while (true)
            {
                SkipWhitespace();

                if (_position >= text.Length || text[_position] is not ('+' or '-'))
                {
                    return left;
                }

                var op = text[_position++];
                var right = ParseMultiplicative();

                Propagate(left, right);

                left = CellValue.FromNumber(op == '+'
                    ? left.AsNumber() + right.AsNumber()
                    : left.AsNumber() - right.AsNumber());
            }
        }

        private CellValue ParseMultiplicative()
        {
            var left = ParseExponent();

            while (true)
            {
                SkipWhitespace();

                if (_position >= text.Length || text[_position] is not ('*' or '/'))
                {
                    return left;
                }

                var op = text[_position++];
                var right = ParseExponent();

                Propagate(left, right);

                if (op == '/')
                {
                    var divisor = right.AsNumber();

                    if (divisor == 0)
                    {
                        throw new FormulaException("#DIV/0!");
                    }

                    left = CellValue.FromNumber(left.AsNumber() / divisor);
                }
                else
                {
                    left = CellValue.FromNumber(left.AsNumber() * right.AsNumber());
                }
            }
        }

        private CellValue ParseExponent()
        {
            var left = ParseUnary();

            SkipWhitespace();

            if (_position >= text.Length || text[_position] != '^')
            {
                return left;
            }

            _position++;

            // Right-associative: 2^3^2 is 2^(3^2) = 512.
            var right = ParseExponent();
            Propagate(left, right);
            return CellValue.FromNumber(Math.Pow(left.AsNumber(), right.AsNumber()));
        }

        private CellValue ParseUnary()
        {
            SkipWhitespace();

            if (_position < text.Length && text[_position] is '-' or '+')
            {
                var negate = text[_position] == '-';
                _position++;

                // Unary minus binds tighter than exponentiation in Excel, so -2^2 is 4. Recursing
                // into ParseUnary rather than into ParseExponent is what produces that.
                var operand = ParseUnary();
                return negate ? CellValue.FromNumber(-operand.AsNumber()) : operand;
            }

            return ParsePostfix();
        }

        private CellValue ParsePostfix()
        {
            var value = ParsePrimary();

            SkipWhitespace();

            while (_position < text.Length && text[_position] == '%')
            {
                _position++;
                value = CellValue.FromNumber(value.AsNumber() / 100);
                SkipWhitespace();
            }

            return value;
        }

        private CellValue ParsePrimary()
        {
            SkipWhitespace();

            if (_position >= text.Length)
            {
                throw new FormulaException("#VALUE!");
            }

            var c = text[_position];

            if (c == '(')
            {
                _position++;
                var value = ParseComparison();
                Expect(')');
                return value;
            }

            if (c == '"')
            {
                return CellValue.FromText(ReadString());
            }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                return CellValue.FromNumber(ReadNumber());
            }

            if (c == '#')
            {
                return CellValue.FromError(ReadErrorLiteral());
            }

            if (char.IsAsciiLetter(c) || c is '_' or '$' or '\'')
            {
                return ReadNameOrReference();
            }

            throw new FormulaException("#VALUE!");
        }

        private string ReadString()
        {
            _position++; // opening quote
            var builder = new StringBuilder();

            while (_position < text.Length)
            {
                var c = text[_position++];

                if (c != '"')
                {
                    builder.Append(c);
                    continue;
                }

                // A doubled quote inside a string literal is one quote character.
                if (_position < text.Length && text[_position] == '"')
                {
                    builder.Append('"');
                    _position++;
                    continue;
                }

                return builder.ToString();
            }

            throw new FormulaException("#VALUE!");
        }

        private double ReadNumber()
        {
            var start = _position;

            while (_position < text.Length &&
                   (char.IsAsciiDigit(text[_position]) || text[_position] == '.'))
            {
                _position++;
            }

            // Scientific notation: the sign after E belongs to the exponent, not to a following
            // operator.
            if (_position < text.Length && text[_position] is 'E' or 'e')
            {
                var save = _position;
                _position++;

                if (_position < text.Length && text[_position] is '+' or '-')
                {
                    _position++;
                }

                if (_position < text.Length && char.IsAsciiDigit(text[_position]))
                {
                    while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                    {
                        _position++;
                    }
                }
                else
                {
                    _position = save;
                }
            }

            return double.TryParse(text.AsSpan(start, _position - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormulaException("#VALUE!");
        }

        private string ReadErrorLiteral()
        {
            var start = _position;
            _position++;

            while (_position < text.Length &&
                   (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] is '/' or '!' or '?'))
            {
                _position++;
            }

            return text[start.._position];
        }

        private CellValue ReadNameOrReference()
        {
            var start = _position;
            string? sheetName = null;

            if (text[_position] == '\'')
            {
                _position++;

                while (_position < text.Length && text[_position] != '\'')
                {
                    _position++;
                }

                _position++; // closing quote

                if (_position >= text.Length || text[_position] != '!')
                {
                    throw new FormulaException("#NAME?");
                }

                sheetName = text[(start + 1)..(_position - 1)];
                _position++;
                start = _position;
            }

            while (_position < text.Length &&
                   (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] is '_' or '.' or '$'))
            {
                _position++;
            }

            var word = text[start.._position];

            SkipWhitespace();

            // An unquoted sheet-qualified reference: Sheet1!A1.
            if (sheetName is null && _position < text.Length && text[_position] == '!')
            {
                sheetName = word;
                _position++;
                start = _position;

                while (_position < text.Length &&
                       (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] is '_' or '$'))
                {
                    _position++;
                }

                word = text[start.._position];
            }

            if (_position < text.Length && text[_position] == '(')
            {
                _position++;
                return CallFunction(word.ToUpperInvariant());
            }

            var target = engine.ResolveSheet(sheet, sheetName);

            // A range only appears as a function argument; a bare range in a scalar position is an
            // implicit intersection in Excel and is not supported here.
            if (_position < text.Length && text[_position] == ':')
            {
                var range = ReadRangeTail(word);
                var values = ReadRangeValues(target, range).ToList();
                return values.Count > 0 ? values[0] : CellValue.Empty;
            }

            if (CellReference.TryParse(word, out var reference))
            {
                return engine.ValueOf(target, reference);
            }

            return word.ToUpperInvariant() switch
            {
                "TRUE" => CellValue.FromBoolean(true),
                "FALSE" => CellValue.FromBoolean(false),
                _ => throw new FormulaException("#NAME?"),
            };
        }

        private CellRangeReference ReadRangeTail(string first)
        {
            _position++; // ':'
            var start = _position;

            while (_position < text.Length &&
                   (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] == '$'))
            {
                _position++;
            }

            var second = text[start.._position];

            if (!CellReference.TryParse(first, out var a) || !CellReference.TryParse(second, out var b))
            {
                throw new FormulaException("#REF!");
            }

            return new CellRangeReference(a, b);
        }

        private IEnumerable<CellValue> ReadRangeValues(Worksheet target, CellRangeReference range)
        {
            foreach (var reference in range.Cells())
            {
                yield return engine.ValueOf(target, reference);
            }
        }

        private CellValue CallFunction(string name)
        {
            var arguments = new List<List<CellValue>>();

            SkipWhitespace();

            if (_position < text.Length && text[_position] == ')')
            {
                _position++;
                return Functions.Call(name, arguments);
            }

            while (true)
            {
                arguments.Add(ParseArgument());

                SkipWhitespace();

                if (_position >= text.Length)
                {
                    throw new FormulaException("#VALUE!");
                }

                if (text[_position] == ',')
                {
                    _position++;
                    continue;
                }

                if (text[_position] == ')')
                {
                    _position++;
                    return Functions.Call(name, arguments);
                }

                throw new FormulaException("#VALUE!");
            }
        }

        /// <summary>
        /// Parses one argument, which may be a range and therefore several values.
        /// </summary>
        /// <remarks>
        /// An argument is a list rather than a scalar because <c>SUM(A1:A10)</c> passes ten values
        /// through one argument position. Flattening ranges at the call site instead would make
        /// <c>IF</c> and <c>VLOOKUP</c>, which care which argument a value came from, impossible.
        /// </remarks>
        private List<CellValue> ParseArgument()
        {
            SkipWhitespace();

            var save = _position;

            // A bare range argument is recognised by scanning ahead for "REF:REF" before falling
            // back to the general expression parser.
            if (TryReadRange(out var target, out var range))
            {
                SkipWhitespace();

                if (_position >= text.Length || text[_position] is ',' or ')')
                {
                    return new RangeArgument(ReadRangeValues(target, range),
                        range.RowCount, range.ColumnCount);
                }

                _position = save;
            }

            // An argument that evaluates to an error becomes an error VALUE rather than aborting
            // the formula. IFERROR exists precisely to receive one, and it never would if the
            // exception unwound past the function call.
            try
            {
                return [ParseComparison()];
            }
            catch (FormulaException ex)
            {
                RecoverToArgumentBoundary();
                return [CellValue.FromError(ex.Code)];
            }
        }

        /// <summary>
        /// Skips to the comma or bracket that ends the current argument, after an error inside it.
        /// </summary>
        /// <remarks>
        /// The parse position is wherever the failure happened, which is usually mid-expression.
        /// Without resynchronising, the caller's loop reads the rest of the failed argument as if
        /// it were the next one.
        /// </remarks>
        private void RecoverToArgumentBoundary()
        {
            var depth = 0;

            while (_position < text.Length)
            {
                var c = text[_position];

                if (c == '"')
                {
                    ReadString();
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        return;
                    }

                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    return;
                }

                _position++;
            }
        }

        private bool TryReadRange(out Worksheet target, out CellRangeReference range)
        {
            target = sheet;
            range = default;

            var save = _position;
            string? sheetName = null;

            if (_position < text.Length && text[_position] == '\'')
            {
                var quoteStart = ++_position;

                while (_position < text.Length && text[_position] != '\'')
                {
                    _position++;
                }

                if (_position >= text.Length)
                {
                    _position = save;
                    return false;
                }

                sheetName = text[quoteStart.._position];
                _position++;

                if (_position >= text.Length || text[_position] != '!')
                {
                    _position = save;
                    return false;
                }

                _position++;
            }

            var start = _position;

            while (_position < text.Length &&
                   (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] is '_' or '$'))
            {
                _position++;
            }

            var word = text[start.._position];

            if (sheetName is null && _position < text.Length && text[_position] == '!')
            {
                sheetName = word;
                _position++;
                start = _position;

                while (_position < text.Length &&
                       (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] is '_' or '$'))
                {
                    _position++;
                }

                word = text[start.._position];
            }

            if (_position >= text.Length || text[_position] != ':')
            {
                _position = save;
                return false;
            }

            try
            {
                target = engine.ResolveSheet(sheet, sheetName);
                range = ReadRangeTail(word);
                return true;
            }
            catch (FormulaException)
            {
                _position = save;
                return false;
            }
        }

        private string PeekOperator()
        {
            if (_position >= text.Length)
            {
                return string.Empty;
            }

            if (_position + 1 < text.Length)
            {
                var two = text.Substring(_position, 2);

                if (two is "<>" or "<=" or ">=")
                {
                    return two;
                }
            }

            return text[_position] switch
            {
                '=' => "=",
                '<' => "<",
                '>' => ">",
                _ => string.Empty,
            };
        }

        private void Expect(char c)
        {
            SkipWhitespace();

            if (_position >= text.Length || text[_position] != c)
            {
                throw new FormulaException("#VALUE!");
            }

            _position++;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        /// <summary>Propagates an operand's error, which is what Excel's arithmetic does.</summary>
        private static void Propagate(CellValue a, CellValue b)
        {
            if (a.ValueType == CellValueType.Error)
            {
                throw new FormulaException(a.AsText());
            }

            if (b.ValueType == CellValueType.Error)
            {
                throw new FormulaException(b.AsText());
            }
        }
    }

    /// <summary>The built-in functions.</summary>
    private static class Functions
    {
        /// <summary>
        /// The functions that must see an error argument rather than propagate it.
        /// </summary>
        /// <remarks>
        /// Excel's default is propagation — <c>SUM</c> over a range containing <c>#DIV/0!</c> is
        /// <c>#DIV/0!</c>, not a total of the rest. The exceptions are the functions whose whole
        /// purpose is to inspect or replace an error, plus the counting functions, which skip what
        /// they cannot count.
        /// </remarks>
        private static readonly HashSet<string> ErrorTolerant = new(StringComparer.Ordinal)
        {
            "IFERROR", "IFNA", "ISERROR", "ISERR", "ISNA", "ERROR.TYPE",
            "COUNT", "COUNTA", "COUNTBLANK", "IF",
        };

        internal static CellValue Call(string name, List<List<CellValue>> arguments)
        {
            // Flattened arguments, for the functions that treat every operand alike.
            var flat = arguments.SelectMany(a => a).ToList();

            if (!ErrorTolerant.Contains(name))
            {
                foreach (var value in flat)
                {
                    if (value.ValueType == CellValueType.Error)
                    {
                        return value;
                    }
                }
            }
            var numbers = flat.Where(v => v.ValueType is CellValueType.Number or CellValueType.DateTime
                                          or CellValueType.Boolean)
                .Select(v => v.AsNumber())
                .ToList();

            CellValue Arg(int index) =>
                index < arguments.Count && arguments[index].Count > 0
                    ? arguments[index][0]
                    : CellValue.Empty;

            return name switch
            {
                "SUM" => CellValue.FromNumber(numbers.Sum()),
                "PRODUCT" => CellValue.FromNumber(numbers.Count == 0 ? 0 : numbers.Aggregate(1.0, (a, b) => a * b)),
                "AVERAGE" => numbers.Count == 0
                    ? CellValue.FromError("#DIV/0!")
                    : CellValue.FromNumber(numbers.Average()),
                "MEDIAN" => Median(numbers),
                "MIN" => CellValue.FromNumber(numbers.Count == 0 ? 0 : numbers.Min()),
                "MAX" => CellValue.FromNumber(numbers.Count == 0 ? 0 : numbers.Max()),
                // COUNT counts numbers; COUNTA counts anything non-empty. Conflating them is the
                // most common spreadsheet bug there is.
                "COUNT" => CellValue.FromNumber(numbers.Count),
                "COUNTA" => CellValue.FromNumber(flat.Count(v => !v.IsEmpty)),
                "COUNTBLANK" => CellValue.FromNumber(flat.Count(v => v.IsEmpty)),
                "STDEV" or "STDEV.S" => StandardDeviation(numbers, sample: true),
                "STDEVP" or "STDEV.P" => StandardDeviation(numbers, sample: false),
                "VAR" or "VAR.S" => Variance(numbers, sample: true),
                "VARP" or "VAR.P" => Variance(numbers, sample: false),

                "ABS" => CellValue.FromNumber(Math.Abs(Arg(0).AsNumber())),
                "SQRT" => Arg(0).AsNumber() < 0
                    ? CellValue.FromError("#NUM!")
                    : CellValue.FromNumber(Math.Sqrt(Arg(0).AsNumber())),
                "POWER" => CellValue.FromNumber(Math.Pow(Arg(0).AsNumber(), Arg(1).AsNumber())),
                "EXP" => CellValue.FromNumber(Math.Exp(Arg(0).AsNumber())),
                "LN" => Arg(0).AsNumber() <= 0
                    ? CellValue.FromError("#NUM!")
                    : CellValue.FromNumber(Math.Log(Arg(0).AsNumber())),
                "LOG10" => Arg(0).AsNumber() <= 0
                    ? CellValue.FromError("#NUM!")
                    : CellValue.FromNumber(Math.Log10(Arg(0).AsNumber())),
                "ROUND" => CellValue.FromNumber(Round(Arg(0).AsNumber(), (int)Arg(1).AsNumber())),
                "ROUNDUP" => CellValue.FromNumber(RoundAway(Arg(0).AsNumber(), (int)Arg(1).AsNumber(), up: true)),
                "ROUNDDOWN" => CellValue.FromNumber(RoundAway(Arg(0).AsNumber(), (int)Arg(1).AsNumber(), up: false)),
                "INT" => CellValue.FromNumber(Math.Floor(Arg(0).AsNumber())),
                "MOD" => Arg(1).AsNumber() == 0
                    ? CellValue.FromError("#DIV/0!")
                    : CellValue.FromNumber(Modulo(Arg(0).AsNumber(), Arg(1).AsNumber())),
                "SIGN" => CellValue.FromNumber(Math.Sign(Arg(0).AsNumber())),

                // IF is tolerant so an error in the branch it does not take is ignored, which is
                // what Excel's lazy evaluation produces. An error in the CONDITION is still one.
                "IF" => Arg(0).ValueType == CellValueType.Error ? Arg(0)
                    : Arg(0).AsBoolean() ? Arg(1)
                    : arguments.Count > 2 ? Arg(2) : CellValue.FromBoolean(false),
                "ISERROR" or "ISERR" => CellValue.FromBoolean(Arg(0).ValueType == CellValueType.Error),
                "ISNA" => CellValue.FromBoolean(Arg(0).ValueType == CellValueType.Error &&
                                                Arg(0).AsText() == "#N/A"),
                "IFNA" => Arg(0).ValueType == CellValueType.Error && Arg(0).AsText() == "#N/A"
                    ? Arg(1)
                    : Arg(0),
                "IFERROR" => Arg(0).ValueType == CellValueType.Error ? Arg(1) : Arg(0),
                "AND" => CellValue.FromBoolean(flat.Count > 0 && flat.All(v => v.AsBoolean())),
                "OR" => CellValue.FromBoolean(flat.Any(v => v.AsBoolean())),
                "NOT" => CellValue.FromBoolean(!Arg(0).AsBoolean()),
                "TRUE" => CellValue.FromBoolean(true),
                "FALSE" => CellValue.FromBoolean(false),

                "LEN" => CellValue.FromNumber(Arg(0).AsText().Length),
                "UPPER" => CellValue.FromText(Arg(0).AsText().ToUpperInvariant()),
                "LOWER" => CellValue.FromText(Arg(0).AsText().ToLowerInvariant()),
                "TRIM" => CellValue.FromText(Arg(0).AsText().Trim()),
                "LEFT" => Substring(Arg(0).AsText(), 0, arguments.Count > 1 ? (int)Arg(1).AsNumber() : 1),
                "RIGHT" => Right(Arg(0).AsText(), arguments.Count > 1 ? (int)Arg(1).AsNumber() : 1),
                // MID's start is one-based.
                "MID" => Substring(Arg(0).AsText(), (int)Arg(1).AsNumber() - 1, (int)Arg(2).AsNumber()),
                "CONCATENATE" or "CONCAT" => CellValue.FromText(string.Concat(flat.Select(v => v.AsText()))),
                "TEXTJOIN" => TextJoin(arguments),
                "SUBSTITUTE" => CellValue.FromText(
                    Arg(0).AsText().Replace(Arg(1).AsText(), Arg(2).AsText(), StringComparison.Ordinal)),
                "VALUE" => double.TryParse(Arg(0).AsText(), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var parsed)
                    ? CellValue.FromNumber(parsed)
                    : CellValue.FromError("#VALUE!"),

                "TODAY" => CellValue.FromDateTime(DateTime.Today),
                "NOW" => CellValue.FromDateTime(DateTime.Now),
                "DATE" => MakeDate(Arg(0), Arg(1), Arg(2)),
                "YEAR" => CellValue.FromNumber(Arg(0).AsDateTime().Year),
                "MONTH" => CellValue.FromNumber(Arg(0).AsDateTime().Month),
                "DAY" => CellValue.FromNumber(Arg(0).AsDateTime().Day),
                "HOUR" => CellValue.FromNumber(Arg(0).AsDateTime().Hour),
                "MINUTE" => CellValue.FromNumber(Arg(0).AsDateTime().Minute),
                "SECOND" => CellValue.FromNumber(Arg(0).AsDateTime().Second),
                "WEEKDAY" => CellValue.FromNumber((int)Arg(0).AsDateTime().DayOfWeek + 1),

                "SUMIF" => Conditional(arguments, sum: true),
                "COUNTIF" => Conditional(arguments, sum: false),
                "VLOOKUP" => VLookup(arguments),
                "HLOOKUP" => HLookup(arguments),
                "INDEX" => Index(arguments),
                "MATCH" => Match(arguments),
                "XLOOKUP" => XLookup(arguments),

                _ => CellValue.FromError("#NAME?"),
            };
        }

        private static CellValue Median(List<double> numbers)
        {
            if (numbers.Count == 0)
            {
                return CellValue.FromError("#NUM!");
            }

            var sorted = numbers.Order().ToList();
            var middle = sorted.Count / 2;

            return CellValue.FromNumber(sorted.Count % 2 == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) / 2);
        }

        private static CellValue Variance(List<double> numbers, bool sample)
        {
            var divisor = sample ? numbers.Count - 1 : numbers.Count;

            if (divisor <= 0)
            {
                return CellValue.FromError("#DIV/0!");
            }

            var mean = numbers.Average();
            var sum = numbers.Sum(v => (v - mean) * (v - mean));
            return CellValue.FromNumber(sum / divisor);
        }

        private static CellValue StandardDeviation(List<double> numbers, bool sample)
        {
            var variance = Variance(numbers, sample);

            return variance.ValueType == CellValueType.Error
                ? variance
                : CellValue.FromNumber(Math.Sqrt(variance.AsNumber()));
        }

        private static double Round(double value, int digits)
        {
            // Excel rounds halves away from zero; .NET's default is banker's rounding, which gives
            // 2 for ROUND(2.5,0) where Excel gives 3.
            var factor = Math.Pow(10, digits);
            return Math.Round(value * factor, MidpointRounding.AwayFromZero) / factor;
        }

        private static double RoundAway(double value, int digits, bool up)
        {
            var factor = Math.Pow(10, digits);
            var scaled = value * factor;

            // ROUNDUP and ROUNDDOWN work on the magnitude: ROUNDDOWN(-2.7, 0) is -2, not -3.
            var rounded = up
                ? value >= 0 ? Math.Ceiling(scaled) : Math.Floor(scaled)
                : value >= 0 ? Math.Floor(scaled) : Math.Ceiling(scaled);

            return rounded / factor;
        }

        private static double Modulo(double a, double b)
        {
            // Excel's MOD takes the divisor's sign: MOD(-3, 2) is 1, where C#'s % gives -1.
            var result = a % b;
            return result != 0 && result < 0 != b < 0 ? result + b : result;
        }

        private static CellValue Substring(string text, int start, int length)
        {
            if (length < 0 || start < 0)
            {
                return CellValue.FromError("#VALUE!");
            }

            if (start >= text.Length)
            {
                return CellValue.FromText(string.Empty);
            }

            return CellValue.FromText(text.Substring(start, Math.Min(length, text.Length - start)));
        }

        private static CellValue Right(string text, int length)
        {
            if (length < 0)
            {
                return CellValue.FromError("#VALUE!");
            }

            return CellValue.FromText(length >= text.Length ? text : text[^length..]);
        }

        private static CellValue TextJoin(List<List<CellValue>> arguments)
        {
            if (arguments.Count < 3)
            {
                return CellValue.FromError("#VALUE!");
            }

            var separator = arguments[0].FirstOrDefault().AsText();
            var ignoreEmpty = arguments[1].FirstOrDefault().AsBoolean();

            var values = arguments.Skip(2).SelectMany(a => a)
                .Where(v => !ignoreEmpty || !v.IsEmpty)
                .Select(v => v.AsText());

            return CellValue.FromText(string.Join(separator, values));
        }

        private static CellValue MakeDate(CellValue year, CellValue month, CellValue day)
        {
            try
            {
                // Excel accepts out-of-range months and days and rolls them over: DATE(2024,13,1)
                // is January 2025.
                var baseDate = new DateTime((int)year.AsNumber(), 1, 1);
                return CellValue.FromDateTime(baseDate
                    .AddMonths((int)month.AsNumber() - 1)
                    .AddDays((int)day.AsNumber() - 1));
            }
            catch (ArgumentOutOfRangeException)
            {
                return CellValue.FromError("#NUM!");
            }
        }

        private static CellValue Conditional(List<List<CellValue>> arguments, bool sum)
        {
            if (arguments.Count < 2)
            {
                return CellValue.FromError("#VALUE!");
            }

            var range = arguments[0];
            var criterion = arguments[1].FirstOrDefault();
            var sumRange = sum && arguments.Count > 2 ? arguments[2] : range;

            var predicate = BuildPredicate(criterion);

            double total = 0;
            var count = 0;

            for (var i = 0; i < range.Count; i++)
            {
                if (!predicate(range[i]))
                {
                    continue;
                }

                count++;

                if (sum && i < sumRange.Count)
                {
                    total += sumRange[i].AsNumber();
                }
            }

            return CellValue.FromNumber(sum ? total : count);
        }

        /// <summary>
        /// Turns a criterion into a predicate, honouring the comparison prefixes Excel allows.
        /// </summary>
        /// <remarks>
        /// A criterion is not just a value: <c>"&gt;100"</c>, <c>"&lt;&gt;OK"</c> and
        /// <c>"&gt;=2024-01-01"</c> are all legal and are what makes SUMIF useful. Treating the
        /// criterion as a plain equality test silently returns zero for every one of them.
        /// </remarks>
        private static Func<CellValue, bool> BuildPredicate(CellValue criterion)
        {
            if (criterion.ValueType != CellValueType.Text)
            {
                var target = criterion.AsNumber();
                return v => Math.Abs(v.AsNumber() - target) < 1e-10;
            }

            var text = criterion.AsText().Trim();

            foreach (var op in (string[])[">=", "<=", "<>", ">", "<", "="])
            {
                if (!text.StartsWith(op, StringComparison.Ordinal))
                {
                    continue;
                }

                var operand = text[op.Length..].Trim();

                if (double.TryParse(operand, NumberStyles.Any, CultureInfo.InvariantCulture,
                        out var number))
                {
                    return op switch
                    {
                        ">=" => v => v.AsNumber() >= number,
                        "<=" => v => v.AsNumber() <= number,
                        "<>" => v => Math.Abs(v.AsNumber() - number) >= 1e-10,
                        ">" => v => v.AsNumber() > number,
                        "<" => v => v.AsNumber() < number,
                        _ => v => Math.Abs(v.AsNumber() - number) < 1e-10,
                    };
                }

                return op switch
                {
                    "<>" => v => !string.Equals(v.AsText(), operand, StringComparison.OrdinalIgnoreCase),
                    _ => v => string.Equals(v.AsText(), operand, StringComparison.OrdinalIgnoreCase),
                };
            }

            return v => string.Equals(v.AsText(), text, StringComparison.OrdinalIgnoreCase);
        }

        // ---- The lookup family -----------------------------------------------------------------
        //
        // All five share one problem: they are the only functions whose answer depends on the shape
        // of a range and not just on the values in it. RangeArgument carries that shape; everything
        // below is arithmetic on it.

        /// <summary>Reads an argument as a shaped range, treating a scalar as a 1x1 one.</summary>
        private static RangeArgument Shape(List<List<CellValue>> arguments, int index)
        {
            if (index >= arguments.Count)
            {
                return new RangeArgument([], 0, 0);
            }

            return arguments[index] as RangeArgument
                   ?? new RangeArgument(arguments[index], arguments[index].Count, 1);
        }

        /// <summary>Compares two values the way Excel does inside a lookup.</summary>
        /// <remarks>
        /// Text compares case-insensitively, which is Excel's behaviour and surprises people coming
        /// from a database. Numbers compare with a tolerance, because a lookup key that came from a
        /// division would otherwise never match the same number typed in.
        /// </remarks>
        private static int CompareForLookup(CellValue a, CellValue b)
        {
            if (a.ValueType == CellValueType.Text || b.ValueType == CellValueType.Text)
            {
                return string.Compare(a.AsText(), b.AsText(), StringComparison.OrdinalIgnoreCase);
            }

            var difference = a.AsNumber() - b.AsNumber();

            return Math.Abs(difference) < 1e-10 ? 0 : Math.Sign(difference);
        }

        private static bool WildcardMatch(CellValue candidate, CellValue pattern)
        {
            var text = pattern.AsText();

            if (!text.Contains('*', StringComparison.Ordinal) &&
                !text.Contains('?', StringComparison.Ordinal))
            {
                return CompareForLookup(candidate, pattern) == 0;
            }

            // Excel's wildcards are * and ?, and ~ escapes them. Everything else is a literal, which
            // is why the pattern is escaped for the regex engine rather than handed to it.
            var builder = new System.Text.StringBuilder("^");

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '~' && i + 1 < text.Length)
                {
                    builder.Append(System.Text.RegularExpressions.Regex.Escape(text[++i].ToString()));
                }
                else if (text[i] == '*')
                {
                    builder.Append(".*");
                }
                else if (text[i] == '?')
                {
                    builder.Append('.');
                }
                else
                {
                    builder.Append(System.Text.RegularExpressions.Regex.Escape(text[i].ToString()));
                }
            }

            builder.Append('$');

            return System.Text.RegularExpressions.Regex.IsMatch(candidate.AsText(), builder.ToString(),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// <c>VLOOKUP(needle, table, columnIndex, [rangeLookup])</c>.
        /// </summary>
        /// <remarks>
        /// The fourth argument defaults to <c>TRUE</c> — approximate match — which is the single most
        /// common source of a wrong answer in a real spreadsheet: it assumes the first column is
        /// sorted, and silently returns the wrong row when it is not. The default is kept because
        /// Excel's is, and a formula that behaves differently here than in Excel is worse than one
        /// that shares its trap.
        /// </remarks>
        private static CellValue VLookup(List<List<CellValue>> arguments)
        {
            if (arguments.Count < 3)
            {
                return CellValue.FromError("#VALUE!");
            }

            var needle = arguments[0].FirstOrDefault();
            var table = Shape(arguments, 1);
            var columnIndex = (int)arguments[2].FirstOrDefault().AsNumber();

            if (columnIndex < 1 || table.Rows == 0)
            {
                return CellValue.FromError("#VALUE!");
            }

            if (columnIndex > table.Columns)
            {
                return CellValue.FromError("#REF!");
            }

            var approximate = arguments.Count < 4 || arguments[3].FirstOrDefault().AsBoolean();
            var row = FindRow(table, needle, approximate, column: 0);

            return row < 0 ? CellValue.FromError("#N/A") : table.At(row, columnIndex - 1);
        }

        /// <summary><c>HLOOKUP(needle, table, rowIndex, [rangeLookup])</c>.</summary>
        private static CellValue HLookup(List<List<CellValue>> arguments)
        {
            if (arguments.Count < 3)
            {
                return CellValue.FromError("#VALUE!");
            }

            var needle = arguments[0].FirstOrDefault();
            var table = Shape(arguments, 1);
            var rowIndex = (int)arguments[2].FirstOrDefault().AsNumber();

            if (rowIndex < 1 || table.Columns == 0)
            {
                return CellValue.FromError("#VALUE!");
            }

            if (rowIndex > table.Rows)
            {
                return CellValue.FromError("#REF!");
            }

            var approximate = arguments.Count < 4 || arguments[3].FirstOrDefault().AsBoolean();
            var column = FindColumn(table, needle, approximate, row: 0);

            return column < 0 ? CellValue.FromError("#N/A") : table.At(rowIndex - 1, column);
        }

        /// <summary>Finds a row by its value in one column, exactly or by the largest value not over.</summary>
        private static int FindRow(RangeArgument table, CellValue needle, bool approximate, int column)
        {
            if (!approximate)
            {
                for (var row = 0; row < table.Rows; row++)
                {
                    if (WildcardMatch(table.At(row, column), needle))
                    {
                        return row;
                    }
                }

                return -1;
            }

            // Approximate means "the last row whose key does not exceed the needle", which is only
            // meaningful on sorted data — and returns nonsense rather than an error when it is not.
            var best = -1;

            for (var row = 0; row < table.Rows; row++)
            {
                if (CompareForLookup(table.At(row, column), needle) <= 0)
                {
                    best = row;
                }
                else
                {
                    break;
                }
            }

            return best;
        }

        private static int FindColumn(RangeArgument table, CellValue needle, bool approximate, int row)
        {
            if (!approximate)
            {
                for (var column = 0; column < table.Columns; column++)
                {
                    if (WildcardMatch(table.At(row, column), needle))
                    {
                        return column;
                    }
                }

                return -1;
            }

            var best = -1;

            for (var column = 0; column < table.Columns; column++)
            {
                if (CompareForLookup(table.At(row, column), needle) <= 0)
                {
                    best = column;
                }
                else
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>
        /// <c>INDEX(range, rowNumber, [columnNumber])</c>, one-based.
        /// </summary>
        /// <remarks>
        /// A zero means "the whole row" or "the whole column" in Excel, which only makes sense inside
        /// an array formula. Here it returns the first cell of that row or column, which is what a
        /// non-array context collapses to anyway.
        /// </remarks>
        private static CellValue Index(List<List<CellValue>> arguments)
        {
            if (arguments.Count < 2)
            {
                return CellValue.FromError("#VALUE!");
            }

            var range = Shape(arguments, 0);
            var row = (int)arguments[1].FirstOrDefault().AsNumber();
            var column = arguments.Count > 2 ? (int)arguments[2].FirstOrDefault().AsNumber() : 0;

            if (range.Rows == 0 || range.Columns == 0)
            {
                return CellValue.FromError("#REF!");
            }

            // A single row or column takes one index, and it counts along the range rather than down
            // it. INDEX(A1:E1, 3) is the third cell, not the third row of a one-row range.
            if (arguments.Count == 2 && range.Rows == 1)
            {
                (row, column) = (1, row);
            }

            if (row < 0 || column < 0 || row > range.Rows || column > range.Columns)
            {
                return CellValue.FromError("#REF!");
            }

            return range.At(Math.Max(0, row - 1), Math.Max(0, column - 1));
        }

        /// <summary>
        /// <c>MATCH(needle, range, [matchType])</c>, returning a one-based position.
        /// </summary>
        /// <remarks>
        /// The match type defaults to 1: the largest value not over the needle, assuming ascending
        /// order. 0 is exact and supports wildcards; -1 is the smallest value not under, assuming
        /// descending order. Only 0 is safe on unsorted data.
        /// </remarks>
        private static CellValue Match(List<List<CellValue>> arguments)
        {
            if (arguments.Count < 2)
            {
                return CellValue.FromError("#VALUE!");
            }

            var needle = arguments[0].FirstOrDefault();
            var range = Shape(arguments, 1);
            var type = arguments.Count > 2 ? (int)arguments[2].FirstOrDefault().AsNumber() : 1;

            if (range.Count == 0)
            {
                return CellValue.FromError("#N/A");
            }

            if (type == 0)
            {
                for (var i = 0; i < range.Count; i++)
                {
                    if (WildcardMatch(range[i], needle))
                    {
                        return CellValue.FromNumber(i + 1);
                    }
                }

                return CellValue.FromError("#N/A");
            }

            var best = -1;

            for (var i = 0; i < range.Count; i++)
            {
                var comparison = CompareForLookup(range[i], needle);

                if (type > 0 ? comparison <= 0 : comparison >= 0)
                {
                    best = i;
                }
                else
                {
                    break;
                }
            }

            return best < 0 ? CellValue.FromError("#N/A") : CellValue.FromNumber(best + 1);
        }

        /// <summary>
        /// <c>XLOOKUP(needle, lookupRange, returnRange, [ifNotFound], [matchMode], [searchMode])</c>.
        /// </summary>
        /// <remarks>
        /// The one worth reaching for. It defaults to an <em>exact</em> match rather than an
        /// approximate one, the lookup and return ranges are separate so the key need not be to the
        /// left of the answer, and a miss can carry its own value instead of <c>#N/A</c>.
        /// Match modes: 0 exact (the default), -1 exact or next smaller, 1 exact or next larger,
        /// 2 wildcard. Search modes: 1 first to last (the default), -1 last to first.
        /// </remarks>
        private static CellValue XLookup(List<List<CellValue>> arguments)
        {
            if (arguments.Count < 3)
            {
                return CellValue.FromError("#VALUE!");
            }

            var needle = arguments[0].FirstOrDefault();
            var lookup = Shape(arguments, 1);
            var result = Shape(arguments, 2);

            if (lookup.Count == 0)
            {
                return CellValue.FromError("#N/A");
            }

            var matchMode = arguments.Count > 4 ? (int)arguments[4].FirstOrDefault().AsNumber() : 0;
            var reversed = arguments.Count > 5 && arguments[5].FirstOrDefault().AsNumber() < 0;

            var found = -1;
            double bestDistance = 0;

            for (var step = 0; step < lookup.Count; step++)
            {
                var i = reversed ? lookup.Count - 1 - step : step;
                var comparison = CompareForLookup(lookup[i], needle);

                switch (matchMode)
                {
                    case 2 when WildcardMatch(lookup[i], needle):
                    case 0 when comparison == 0:
                        found = i;
                        break;

                    case -1 or 1 when comparison == 0:
                        return Result(i);

                    // Nearest smaller or nearest larger, which unlike VLOOKUP does not assume the
                    // data is sorted: the whole range is scanned and the closest candidate kept.
                    case -1 when comparison < 0:
                    case 1 when comparison > 0:
                    {
                        var distance = Math.Abs(lookup[i].AsNumber() - needle.AsNumber());

                        if (found < 0 || distance < bestDistance)
                        {
                            (found, bestDistance) = (i, distance);
                        }

                        break;
                    }
                }

                if (found >= 0 && matchMode is 0 or 2)
                {
                    return Result(found);
                }
            }

            if (found >= 0)
            {
                return Result(found);
            }

            // The fourth argument is what makes XLOOKUP readable: IFNA(VLOOKUP(...), "-") in one place.
            return arguments.Count > 3 && arguments[3].Count > 0
                ? arguments[3][0]
                : CellValue.FromError("#N/A");

            CellValue Result(int index) =>
                index < result.Count ? result[index] : CellValue.FromError("#REF!");
        }
    }
}
