using System.Globalization;

namespace VibeDesk.Application.Spreadsheets;

internal enum TokenKind
{
    Number, String, Boolean, Identifier, Reference,
    Plus, Minus, Star, Slash, Caret, Percent, Ampersand,
    Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual,
    LParen, RParen, Comma, Colon, End,
}

internal readonly record struct Token(TokenKind Kind, string Text, double Number = 0, int Position = 0);

/// <summary>
/// Hand-written tokenizer. Recognises references (<c>A1</c>, <c>$B$2</c>, <c>Sheet 2'!A1:C9</c>) as
/// single tokens so the parser never has to reassemble a sheet-qualified address from fragments.
/// </summary>
internal sealed class FormulaLexer(string source)
{
    private readonly string _s = source;
    private int _i;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var t = Next();
            tokens.Add(t);
            if (t.Kind == TokenKind.End) break;
        }
        return tokens;
    }

    private Token Next()
    {
        while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        if (_i >= _s.Length) return new Token(TokenKind.End, string.Empty, Position: _i);

        var start = _i;
        var c = _s[_i];

        switch (c)
        {
            case '+': _i++; return new Token(TokenKind.Plus, "+", Position: start);
            case '-': _i++; return new Token(TokenKind.Minus, "-", Position: start);
            case '*': _i++; return new Token(TokenKind.Star, "*", Position: start);
            case '/': _i++; return new Token(TokenKind.Slash, "/", Position: start);
            case '^': _i++; return new Token(TokenKind.Caret, "^", Position: start);
            case '%': _i++; return new Token(TokenKind.Percent, "%", Position: start);
            case '&': _i++; return new Token(TokenKind.Ampersand, "&", Position: start);
            case '(': _i++; return new Token(TokenKind.LParen, "(", Position: start);
            case ')': _i++; return new Token(TokenKind.RParen, ")", Position: start);
            case ':': _i++; return new Token(TokenKind.Colon, ":", Position: start);
            case ',': case ';': _i++; return new Token(TokenKind.Comma, ",", Position: start);
            case '=': _i++; return new Token(TokenKind.Equal, "=", Position: start);

            case '<':
                _i++;
                if (Peek() == '>') { _i++; return new Token(TokenKind.NotEqual, "<>", Position: start); }
                if (Peek() == '=') { _i++; return new Token(TokenKind.LessEqual, "<=", Position: start); }
                return new Token(TokenKind.Less, "<", Position: start);

            case '>':
                _i++;
                if (Peek() == '=') { _i++; return new Token(TokenKind.GreaterEqual, ">=", Position: start); }
                return new Token(TokenKind.Greater, ">", Position: start);

            case '"':
                return ReadString();

            case '\'':
                return ReadQuotedSheetReference();
        }

        if (char.IsAsciiDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsAsciiDigit(_s[_i + 1])))
            return ReadNumber();

        if (char.IsAsciiLetter(c) || c == '_' || c == '$')
            return ReadWord();

        _i++;
        return new Token(TokenKind.Identifier, c.ToString(), Position: start);
    }

    private char Peek() => _i < _s.Length ? _s[_i] : '\0';

    private Token ReadString()
    {
        var start = _i;
        _i++; // opening quote
        var sb = new System.Text.StringBuilder();
        while (_i < _s.Length)
        {
            if (_s[_i] == '"')
            {
                // "" inside a literal is an escaped quote.
                if (_i + 1 < _s.Length && _s[_i + 1] == '"') { sb.Append('"'); _i += 2; continue; }
                _i++;
                return new Token(TokenKind.String, sb.ToString(), Position: start);
            }
            sb.Append(_s[_i++]);
        }
        // Unterminated literal: accept what we have so a half-typed formula still parses.
        return new Token(TokenKind.String, sb.ToString(), Position: start);
    }

    private Token ReadNumber()
    {
        var start = _i;
        while (_i < _s.Length && (char.IsAsciiDigit(_s[_i]) || _s[_i] == '.')) _i++;

        // Scientific notation, including a signed exponent.
        if (_i < _s.Length && (_s[_i] is 'e' or 'E'))
        {
            var save = _i;
            _i++;
            if (_i < _s.Length && (_s[_i] is '+' or '-')) _i++;
            if (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
            {
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
            }
            else
            {
                _i = save;
            }
        }

        var text = _s[start.._i];
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
        return new Token(TokenKind.Number, text, d, start);
    }

    private Token ReadQuotedSheetReference()
    {
        var start = _i;
        _i++; // opening '
        var sb = new System.Text.StringBuilder("'");
        while (_i < _s.Length)
        {
            if (_s[_i] == '\'')
            {
                if (_i + 1 < _s.Length && _s[_i + 1] == '\'') { sb.Append("''"); _i += 2; continue; }
                sb.Append('\'');
                _i++;
                break;
            }
            sb.Append(_s[_i++]);
        }

        // Consume "!A1[:B9]" so the whole thing is one Reference token.
        if (_i < _s.Length && _s[_i] == '!')
        {
            sb.Append('!');
            _i++;
            sb.Append(ReadReferenceBody());
            return new Token(TokenKind.Reference, sb.ToString(), Position: start);
        }

        return new Token(TokenKind.String, sb.ToString().Trim('\''), Position: start);
    }

    private string ReadReferenceBody()
    {
        var start = _i;
        while (_i < _s.Length && (char.IsAsciiLetterOrDigit(_s[_i]) || _s[_i] == '$')) _i++;

        if (_i < _s.Length && _s[_i] == ':')
        {
            var save = _i;
            _i++;
            var second = _i;
            while (_i < _s.Length && (char.IsAsciiLetterOrDigit(_s[_i]) || _s[_i] == '$')) _i++;
            if (_i == second) _i = save; // dangling colon — leave it for the parser
        }

        return _s[start.._i];
    }

    private Token ReadWord()
    {
        var start = _i;
        while (_i < _s.Length && (char.IsAsciiLetterOrDigit(_s[_i]) || _s[_i] is '_' or '$' or '.')) _i++;
        var word = _s[start.._i];

        // Bare sheet name followed by ! — pull the whole reference in.
        if (_i < _s.Length && _s[_i] == '!')
        {
            _i++;
            var body = ReadReferenceBody();
            return new Token(TokenKind.Reference, $"{word}!{body}", Position: start);
        }

        if (word.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return new Token(TokenKind.Boolean, word, 1, start);
        if (word.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return new Token(TokenKind.Boolean, word, 0, start);

        // A word is a reference only when it parses as A1 *and* isn't immediately a call.
        var isCall = _i < _s.Length && _s[_i] == '(';
        if (!isCall && CellAddress.TryParse(word, out _))
        {
            // Extend across a range colon: A1:B9
            if (_i < _s.Length && _s[_i] == ':')
            {
                var save = _i;
                _i++;
                var second = _i;
                while (_i < _s.Length && (char.IsAsciiLetterOrDigit(_s[_i]) || _s[_i] == '$')) _i++;
                var right = _s[second.._i];
                if (CellAddress.TryParse(right, out _))
                    return new Token(TokenKind.Reference, $"{word}:{right}", Position: start);
                _i = save;
            }
            return new Token(TokenKind.Reference, word, Position: start);
        }

        return new Token(TokenKind.Identifier, word, Position: start);
    }
}

// ─────────────────────────────────────────── AST ───────────────────────────────────────────

internal abstract record Node;

internal sealed record LiteralNode(FormulaValue Value) : Node;

/// <summary>A cell or range reference, kept in source form and resolved at evaluation time.</summary>
internal sealed record ReferenceNode(string Raw) : Node;

/// <summary>A bare identifier: either a named range or an unknown name (→ <c>#NAME?</c>).</summary>
internal sealed record NameNode(string Name) : Node;

internal sealed record UnaryNode(TokenKind Op, Node Operand) : Node;

internal sealed record BinaryNode(TokenKind Op, Node Left, Node Right) : Node;

internal sealed record FunctionNode(string Name, List<Node> Args) : Node;

/// <summary>
/// Recursive-descent parser over the token list, with the standard spreadsheet precedence ladder:
/// comparison &lt; concatenation &lt; additive &lt; multiplicative &lt; power &lt; unary &lt; postfix-%.
/// </summary>
internal sealed class FormulaParser(List<Token> tokens)
{
    private readonly List<Token> _t = tokens;
    private int _p;

    private Token Current => _t[_p];
    private bool Match(TokenKind k)
    {
        if (Current.Kind != k) return false;
        _p++;
        return true;
    }

    public Node ParseExpression()
    {
        var node = ParseComparison();
        return node;
    }

    private Node ParseComparison()
    {
        var left = ParseConcat();
        while (Current.Kind is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less
               or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            var op = Current.Kind;
            _p++;
            left = new BinaryNode(op, left, ParseConcat());
        }
        return left;
    }

    private Node ParseConcat()
    {
        var left = ParseAdditive();
        while (Current.Kind == TokenKind.Ampersand)
        {
            _p++;
            left = new BinaryNode(TokenKind.Ampersand, left, ParseAdditive());
        }
        return left;
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Current.Kind;
            _p++;
            left = new BinaryNode(op, left, ParseMultiplicative());
        }
        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParsePower();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = Current.Kind;
            _p++;
            left = new BinaryNode(op, left, ParsePower());
        }
        return left;
    }

    private Node ParsePower()
    {
        var left = ParseUnary();
        // Right-associative: 2^3^2 is 2^(3^2).
        if (Current.Kind == TokenKind.Caret)
        {
            _p++;
            return new BinaryNode(TokenKind.Caret, left, ParsePower());
        }
        return left;
    }

    private Node ParseUnary()
    {
        if (Current.Kind is TokenKind.Minus or TokenKind.Plus)
        {
            var op = Current.Kind;
            _p++;
            return new UnaryNode(op, ParseUnary());
        }
        return ParsePostfix();
    }

    private Node ParsePostfix()
    {
        var node = ParsePrimary();
        while (Current.Kind == TokenKind.Percent)
        {
            _p++;
            node = new UnaryNode(TokenKind.Percent, node);
        }
        return node;
    }

    private Node ParsePrimary()
    {
        var token = Current;

        switch (token.Kind)
        {
            case TokenKind.Number:
                _p++;
                return new LiteralNode(FormulaValue.Number(token.Number));

            case TokenKind.String:
                _p++;
                return new LiteralNode(FormulaValue.Text(token.Text));

            case TokenKind.Boolean:
                _p++;
                return new LiteralNode(FormulaValue.Boolean(token.Number != 0));

            case TokenKind.Reference:
                _p++;
                return new ReferenceNode(token.Text);

            case TokenKind.LParen:
                _p++;
                var inner = ParseExpression();
                Match(TokenKind.RParen);
                return inner;

            case TokenKind.Identifier:
                _p++;
                if (Match(TokenKind.LParen))
                {
                    var args = new List<Node>();
                    if (Current.Kind != TokenKind.RParen)
                    {
                        do
                        {
                            // Allow empty arguments: IF(A1,,"no") — they evaluate as blank.
                            if (Current.Kind is TokenKind.Comma)
                            {
                                args.Add(new LiteralNode(FormulaValue.Empty));
                                continue;
                            }
                            args.Add(ParseExpression());
                        } while (Match(TokenKind.Comma));
                    }
                    Match(TokenKind.RParen);
                    return new FunctionNode(token.Text.ToUpperInvariant(), args);
                }
                return new NameNode(token.Text);

            default:
                _p++;
                return new LiteralNode(FormulaValue.Error(FormulaError.Value));
        }
    }

    /// <summary>Parses a formula body (without the leading <c>=</c>). Never throws.</summary>
    public static Node Parse(string formulaBody)
    {
        try
        {
            var tokens = new FormulaLexer(formulaBody).Tokenize();
            return new FormulaParser(tokens).ParseExpression();
        }
        catch (Exception)
        {
            return new LiteralNode(FormulaValue.Error(FormulaError.Value));
        }
    }
}
