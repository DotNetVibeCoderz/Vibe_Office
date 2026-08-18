namespace VibeDesk.Application.Spreadsheets;

/// <summary>
/// What the evaluator needs from the workbook. Implemented by <see cref="FormulaEngine"/> over a
/// <c>SpreadsheetModel</c>, but kept as an interface so formulas can also be evaluated against a
/// lightweight in-memory grid in tests.
/// </summary>
public interface IFormulaContext
{
    /// <summary>Resolves one cell; <paramref name="sheetName"/> null means the formula's own sheet.</summary>
    FormulaValue GetCell(string? sheetName, CellAddress address);

    /// <summary>Row-major enumeration of a range, including blanks.</summary>
    IReadOnlyList<FormulaValue> GetRange(string? sheetName, CellRange range);

    /// <summary>Returns the A1 target of a named range, or null when the name is unknown.</summary>
    string? ResolveName(string name);

    /// <summary>Stable "now" for the whole recalculation pass so NOW() is consistent across cells.</summary>
    DateTimeOffset Now { get; }
}

/// <summary>
/// Tree-walking evaluator for a parsed formula. One instance per cell evaluation; the shared
/// <see cref="IFormulaContext"/> handles memoisation and cycle detection.
/// </summary>
internal sealed class FormulaEvaluator(IFormulaContext context, string? currentSheet)
{
    private readonly IFormulaContext _ctx = context;
    private readonly string? _sheet = currentSheet;

    /// <summary>Guards against a malformed formula recursing without bound.</summary>
    private const int MaxDepth = 128;
    private int _depth;

    /// <summary>
    /// Names bound by an enclosing <c>LET</c>. Null until one is used, because the overwhelming
    /// majority of formulas never bind anything and should not pay for a dictionary.
    /// </summary>
    private Dictionary<string, FormulaValue>? _bindings;

    public FormulaValue Evaluate(Node node)
    {
        if (++_depth > MaxDepth) return FormulaValue.Error(FormulaError.Num);
        try
        {
            return node switch
            {
                LiteralNode l => l.Value,
                ReferenceNode r => EvaluateReference(r.Raw),
                NameNode n => EvaluateName(n.Name),
                UnaryNode u => EvaluateUnary(u),
                BinaryNode b => EvaluateBinary(b),
                FunctionNode f => FormulaFunctions.Invoke(f.Name, f.Args, this),
                ArrayNode a => EvaluateArrayLiteral(a),
                _ => FormulaValue.Error(FormulaError.Value),
            };
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// Flattens an argument to scalar values. A range argument contributes every cell, which is what
    /// aggregate functions like SUM want; a scalar contributes itself.
    /// </summary>
    public IReadOnlyList<FormulaValue> EvaluateToList(Node node) => EvaluateToList(node, out _);

    /// <inheritdoc cref="EvaluateToList(Node)"/>
    /// <param name="isCollection">
    /// True when the argument was a range or produced an array. Aggregates need this: text inside a
    /// collection is skipped, while text passed directly is coerced and may error.
    /// </param>
    public IReadOnlyList<FormulaValue> EvaluateToList(Node node, out bool isCollection)
    {
        if (node is ReferenceNode r && SheetRange.TryParse(r.Raw, out var sr) && sr.Value.Range.CellCount > 1)
        {
            isCollection = true;
            return _ctx.GetRange(sr.Value.SheetName ?? _sheet, sr.Value.Range);
        }

        var v = Evaluate(node);
        isCollection = v.Kind == FormulaValueKind.Array;

        return isCollection ? v.Items : [v];
    }

    /// <summary>
    /// Builds an inline array. Nested arrays and range references flatten into the result, so
    /// <c>{A1:A3, 99}</c> is one list rather than a list holding a list.
    /// </summary>
    private FormulaValue EvaluateArrayLiteral(ArrayNode node)
    {
        var values = new List<FormulaValue>(node.Items.Count);

        foreach (var item in node.Items)
        {
            var v = Evaluate(item);

            if (v.Kind == FormulaValueKind.Array) values.AddRange(v.Items);
            else values.Add(v);
        }

        return FormulaValue.Array([.. values]);
    }

    /// <summary>Resolves an argument that must be a range (MATCH, VLOOKUP table, …).</summary>
    public bool TryEvaluateRange(Node node, out CellRange range, out string? sheet)
    {
        range = default;
        sheet = _sheet;

        var raw = node switch
        {
            ReferenceNode r => r.Raw,
            NameNode n => _ctx.ResolveName(n.Name),
            _ => null,
        };

        if (raw is null) return false;
        if (!SheetRange.TryParse(raw, out var sr)) return false;

        range = sr.Value.Range;
        sheet = sr.Value.SheetName ?? _sheet;
        return true;
    }

    public IReadOnlyList<FormulaValue> GetRange(CellRange range, string? sheet) =>
        _ctx.GetRange(sheet ?? _sheet, range);

    public FormulaValue GetCell(CellAddress address, string? sheet) =>
        _ctx.GetCell(sheet ?? _sheet, address);

    public DateTimeOffset Now => _ctx.Now;

    private FormulaValue EvaluateReference(string raw)
    {
        if (!SheetRange.TryParse(raw, out var sr))
            return FormulaValue.Error(FormulaError.Ref);

        var sheet = sr.Value.SheetName ?? _sheet;
        var range = sr.Value.Range;

        if (range.CellCount == 1)
            return _ctx.GetCell(sheet, range.Start);

        // A multi-cell reference used in scalar position becomes an array; aggregate functions
        // consume it via EvaluateToList, and scalar contexts take the first value.
        return FormulaValue.Array([.. _ctx.GetRange(sheet, range)]);
    }

    /// <summary>
    /// Binds a name for the duration of <paramref name="body"/>, then restores whatever it shadowed.
    /// Restoring matters: <c>LET(x,1,LET(x,2,x)+x)</c> must see 2 inside and 1 outside.
    /// </summary>
    public FormulaValue EvaluateWithBinding(string name, FormulaValue value, Node body)
    {
        _bindings ??= new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);

        var hadPrevious = _bindings.TryGetValue(name, out var previous);
        _bindings[name] = value;

        try
        {
            return Evaluate(body);
        }
        finally
        {
            if (hadPrevious) _bindings[name] = previous;
            else _bindings.Remove(name);
        }
    }

    private FormulaValue EvaluateName(string name)
    {
        // A LET binding shadows a workbook named range, which is what a reader expects when the
        // name is declared two characters to the left.
        if (_bindings is not null && _bindings.TryGetValue(name, out var bound)) return bound;

        var target = _ctx.ResolveName(name);
        if (target is null) return FormulaValue.Error(FormulaError.Name);
        return EvaluateReference(target);
    }

    private FormulaValue EvaluateUnary(UnaryNode u)
    {
        var v = Evaluate(u.Operand);

        switch (u.Op)
        {
            case TokenKind.Minus:
                var neg = v.ToNumber();
                return neg.IsError ? neg : FormulaValue.Number(-neg.RawNumber);

            case TokenKind.Plus:
                return v.ToNumber();

            case TokenKind.Percent:
                var pct = v.ToNumber();
                return pct.IsError ? pct : FormulaValue.Number(pct.RawNumber / 100d);

            default:
                return FormulaValue.Error(FormulaError.Value);
        }
    }

    private FormulaValue EvaluateBinary(BinaryNode b)
    {
        var left = Evaluate(b.Left);
        if (left.IsError && b.Op != TokenKind.Ampersand) return left;

        var right = Evaluate(b.Right);
        if (right.IsError && b.Op != TokenKind.Ampersand) return right;

        switch (b.Op)
        {
            case TokenKind.Ampersand:
                if (left.IsError) return left;
                if (right.IsError) return right;
                return FormulaValue.Text(Scalar(left).ToDisplayString() + Scalar(right).ToDisplayString());

            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Star:
            case TokenKind.Slash:
            case TokenKind.Caret:
                return Lift(b.Op, left, right, Arithmetic);

            case TokenKind.Equal:
            case TokenKind.NotEqual:
            case TokenKind.Less:
            case TokenKind.LessEqual:
            case TokenKind.Greater:
            case TokenKind.GreaterEqual:
                return Lift(b.Op, left, right, Comparison);

            default:
                return FormulaValue.Error(FormulaError.Value);
        }
    }

    /// <summary>
    /// Applies a scalar operator elementwise when either side is an array, so <c>A1:A3&gt;99</c> is a
    /// mask of three booleans rather than one comparison of the first cell.
    /// </summary>
    /// <remarks>
    /// This is what makes FILTER usable: without it the mask has one element, the lengths disagree,
    /// and the honest answer is a refusal — which reads as the function being broken. A scalar on
    /// either side is broadcast against the array; two arrays of different lengths are refused,
    /// because pairing them off and dropping the tail would compute a plausible wrong answer.
    /// </remarks>
    private static FormulaValue Lift(
        TokenKind op, FormulaValue l, FormulaValue r,
        Func<TokenKind, FormulaValue, FormulaValue, FormulaValue> apply)
    {
        var leftIsArray = l.Kind == FormulaValueKind.Array;
        var rightIsArray = r.Kind == FormulaValueKind.Array;

        if (!leftIsArray && !rightIsArray) return apply(op, l, r);

        var count = leftIsArray ? l.Items.Count : r.Items.Count;

        if (leftIsArray && rightIsArray && l.Items.Count != r.Items.Count)
        {
            return FormulaValue.Error(FormulaError.Value);
        }

        var results = new FormulaValue[count];

        for (var i = 0; i < count; i++)
        {
            results[i] = apply(
                op,
                leftIsArray ? l.Items[i] : l,
                rightIsArray ? r.Items[i] : r);
        }

        return FormulaValue.Array(results);
    }

    /// <summary>Collapses an array to its first value, as a scalar operator position requires.</summary>
    private static FormulaValue Scalar(FormulaValue v) =>
        v.Kind == FormulaValueKind.Array ? (v.Items.Count > 0 ? v.Items[0] : FormulaValue.Empty) : v;

    private static FormulaValue Arithmetic(TokenKind op, FormulaValue l, FormulaValue r)
    {
        var a = Scalar(l).ToNumber();
        if (a.IsError) return a;
        var b = Scalar(r).ToNumber();
        if (b.IsError) return b;

        var x = a.RawNumber;
        var y = b.RawNumber;

        return op switch
        {
            TokenKind.Plus => FormulaValue.Number(x + y),
            TokenKind.Minus => FormulaValue.Number(x - y),
            TokenKind.Star => FormulaValue.Number(x * y),
            TokenKind.Slash => Math.Abs(y) < double.Epsilon
                ? FormulaValue.Error(FormulaError.Div0)
                : FormulaValue.Number(x / y),
            TokenKind.Caret => Power(x, y),
            _ => FormulaValue.Error(FormulaError.Value),
        };
    }

    private static FormulaValue Power(double x, double y)
    {
        // Math.Pow yields NaN for a negative base with a fractional exponent; spreadsheets report #NUM!.
        var result = Math.Pow(x, y);
        return double.IsNaN(result) || double.IsInfinity(result)
            ? FormulaValue.Error(FormulaError.Num)
            : FormulaValue.Number(result);
    }

    private static FormulaValue Comparison(TokenKind op, FormulaValue l, FormulaValue r)
    {
        var cmp = FormulaValue.Compare(Scalar(l), Scalar(r));
        if (cmp is null) return FormulaValue.Error(FormulaError.Value);

        return FormulaValue.Boolean(op switch
        {
            TokenKind.Equal => cmp == 0,
            TokenKind.NotEqual => cmp != 0,
            TokenKind.Less => cmp < 0,
            TokenKind.LessEqual => cmp <= 0,
            TokenKind.Greater => cmp > 0,
            TokenKind.GreaterEqual => cmp >= 0,
            _ => false,
        });
    }
}
