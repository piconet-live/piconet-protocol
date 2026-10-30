using System.Globalization;
using System.Text;

namespace Online.Protocol;

/// <summary>What a token of a mode file is (the language's section 3).</summary>
internal enum ModeTokenKind
{
    /// <summary>A name or a keyword: the parser tells them apart.</summary>
    Name,
    /// <summary>A whole number: <c>42</c>, <c>0x1F</c>.</summary>
    Whole,
    /// <summary>A real: <c>1.5</c>, <c>2.</c>, and the literals that are reals: <c>50%</c>, <c>1000 km/h</c>,
    /// <c>300 m</c>.</summary>
    Fraction,
    /// <summary>A time, already in whole steps at 60 a second: <c>30s</c>, <c>500ms</c>, <c>2min</c>,
    /// <c>90 steps</c>.</summary>
    Time,
    Text,
    Symbol,
    End,
}

/// <summary>A token: its kind, its text as the file wrote it (a string's without its quotes and escapes), its line,
/// and a number's value (a whole number or a time's steps in <see cref="Whole"/>, a real in <see cref="Real"/>).</summary>
internal sealed record ModeToken(ModeTokenKind Kind, string Text, int Line, long Whole = 0, float Real = 0)
{
    public bool Is(string text) => (Kind == ModeTokenKind.Name || Kind == ModeTokenKind.Symbol) && Text == text;
}

/// <summary>A mode file that does not compile: the line it names and what is wrong, as the author reads it.</summary>
internal sealed class ModeCompileException(int line, string message) : Exception(message)
{
    public int Line { get; } = line;
}

/// <summary>Cuts a mode file into tokens (the language's section 3): comments dropped, numbers worked out with their
/// units, strings unescaped. A time that is not a whole number of steps is rounded (a half up) and warned of.</summary>
internal sealed class ModeLexer
{
    // The symbols, the longest first, so that ".." is not read as "." twice.
    private static readonly string[] Symbols =
        ["..", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "{", "}", "(", ")", "[", "]", ";", ",", ".", "=", "<", ">",
         "+", "-", "*", "×", "/", "%", "&"];

    private readonly string _text;
    private readonly List<string> _warnings;
    private int _at;
    private int _line = 1;

    private ModeLexer(string text, List<string> warnings)
    {
        _text = text;
        _warnings = warnings;
    }

    /// <summary>The file's tokens, ending with one of kind <see cref="ModeTokenKind.End"/>.</summary>
    public static List<ModeToken> Tokens(string text, List<string> warnings)
    {
        var lexer = new ModeLexer(text, warnings);
        var tokens = new List<ModeToken>();
        ModeToken token;
        do
        {
            token = lexer.Next();
            tokens.Add(token);
        }
        while (token.Kind != ModeTokenKind.End);
        return tokens;
    }

    private char Peek(int ahead = 0) => _at + ahead < _text.Length ? _text[_at + ahead] : '\0';

    private ModeToken Next()
    {
        SkipSpaceAndComments();
        if (_at >= _text.Length)
        {
            return new ModeToken(ModeTokenKind.End, "the end of the file", _line);
        }
        var c = Peek();
        if (char.IsAsciiDigit(c))
        {
            return Number();
        }
        if (char.IsAsciiLetter(c) || c == '_')
        {
            return new ModeToken(ModeTokenKind.Name, Word(), _line);
        }
        if (c == '"')
        {
            return Text();
        }
        foreach (var symbol in Symbols)
        {
            if (string.CompareOrdinal(_text, _at, symbol, 0, symbol.Length) == 0)
            {
                _at += symbol.Length;
                return new ModeToken(ModeTokenKind.Symbol, symbol == "×" ? "*" : symbol, _line);
            }
        }
        throw new ModeCompileException(_line, $"'{c}' is not part of the language");
    }

    private void SkipSpaceAndComments()
    {
        while (_at < _text.Length)
        {
            var c = Peek();
            if (c == '\n')
            {
                _line++;
                _at++;
            }
            else if (char.IsWhiteSpace(c))
            {
                _at++;
            }
            else if (c == '/' && Peek(1) == '/')
            {
                while (_at < _text.Length && Peek() != '\n')
                {
                    _at++;
                }
            }
            else if (c == '/' && Peek(1) == '*')
            {
                SkipBlockComment();
            }
            else
            {
                return;
            }
        }
    }

    private void SkipBlockComment()
    {
        var line = _line;
        var end = _text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new ModeCompileException(line, "a comment opened with /* is never closed with */");
        }
        _line += _text.AsSpan(_at, end - _at).Count('\n');
        _at = end + 2;
    }

    private string Word()
    {
        var start = _at;
        while (char.IsAsciiLetterOrDigit(Peek()) || Peek() == '_')
        {
            _at++;
        }
        return _text[start.._at];
    }

    private ModeToken Text()
    {
        var line = _line;
        var value = new StringBuilder();
        _at++;
        while (true)
        {
            var c = Peek();
            if (_at >= _text.Length || c == '\n')
            {
                throw new ModeCompileException(line, "a string is not closed with \" on its line");
            }
            _at++;
            if (c == '"')
            {
                return new ModeToken(ModeTokenKind.Text, value.ToString(), line);
            }
            if (c == '\\')
            {
                var escaped = Peek();
                if (escaped != '"' && escaped != '\\')
                {
                    throw new ModeCompileException(line, "a string may escape only \\\" and \\\\");
                }
                _at++;
                c = escaped;
            }
            value.Append(c);
        }
    }

    private ModeToken Number()
    {
        var start = _at;
        if (Peek() == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
        {
            return Hex();
        }
        while (char.IsAsciiDigit(Peek()))
        {
            _at++;
        }
        var fraction = false;
        // "2." is a real, "5..50" a range.
        if (Peek() == '.' && Peek(1) != '.')
        {
            fraction = true;
            _at++;
            while (char.IsAsciiDigit(Peek()))
            {
                _at++;
            }
        }
        var digits = _text[start.._at];
        return Unit(digits, fraction) ?? Plain(digits, fraction);
    }

    private ModeToken Hex()
    {
        _at += 2;
        var start = _at;
        while (char.IsAsciiHexDigit(Peek()))
        {
            _at++;
        }
        var digits = _text[start.._at];
        if (digits.Length == 0 || digits.Length > 8 || char.IsAsciiLetterOrDigit(Peek()))
        {
            throw new ModeCompileException(_line, $"0x{digits} is not a whole number of at most 8 hex digits");
        }
        // Up to 0xFFFFFFFF: a word's bits, as the int they are.
        var value = (int)uint.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new ModeToken(ModeTokenKind.Whole, "0x" + digits, _line, value);
    }

    private ModeToken Plain(string digits, bool fraction)
    {
        if (char.IsAsciiLetter(Peek()) || Peek() == '_')
        {
            throw new ModeCompileException(_line, $"'{digits}{Word()}' is not a number: a time is written 30s, 500ms, 2min or 90 steps");
        }
        if (fraction)
        {
            return new ModeToken(ModeTokenKind.Fraction, digits, _line, Real: ParseReal(digits));
        }
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var whole) || whole > int.MaxValue)
        {
            throw new ModeCompileException(_line, $"{digits} is larger than an int can hold ({int.MaxValue})");
        }
        return new ModeToken(ModeTokenKind.Whole, digits, _line, whole);
    }

    /// <summary>A number with a unit: written against it (<c>50%</c>, <c>30s</c>, <c>500ms</c>, <c>2min</c>) or after a
    /// space (<c>90 steps</c>, <c>1000 km/h</c>, <c>300 m</c>). Null when none follows.</summary>
    private ModeToken? Unit(string digits, bool fraction)
    {
        if (Peek() == '%')
        {
            _at++;
            return new ModeToken(ModeTokenKind.Fraction, digits + "%", _line, Real: ParseReal(digits + "E-2"));
        }
        var save = _at;
        var spaced = false;
        while (Peek() == ' ' || Peek() == '\t')
        {
            _at++;
            spaced = true;
        }
        if (!char.IsAsciiLetter(Peek()))
        {
            _at = save;
            return null;
        }
        var unit = Word();
        var per = Peek() == '/' && Peek(1) == 'h' && !char.IsAsciiLetterOrDigit(Peek(2)) && unit == "km";
        if (per)
        {
            _at += 2;
            unit = "km/h";
        }
        var text = digits + (spaced ? " " : "") + unit;
        switch (spaced, unit)
        {
            case (false, "s"):
                return Time(text, decimal.Parse(digits, CultureInfo.InvariantCulture) * 60);
            case (false, "ms"):
                return Time(text, decimal.Parse(digits, CultureInfo.InvariantCulture) * 60 / 1000);
            case (false, "min"):
                return Time(text, decimal.Parse(digits, CultureInfo.InvariantCulture) * 3600);
            case (_, "steps"):
                return Time(text, decimal.Parse(digits, CultureInfo.InvariantCulture));
            case (true, "km/h"):
            case (true, "m"):
                return new ModeToken(ModeTokenKind.Fraction, text, _line, Real: ParseReal(digits));
            default:
                if (!spaced)
                {
                    throw new ModeCompileException(_line, $"'{text}' is not a number: a time is written 30s, 500ms, 2min or 90 steps");
                }
                // A name after a number: not a unit, the parser's to judge.
                _at = save;
                return null;
        }
    }

    private ModeToken Time(string text, decimal steps)
    {
        var whole = decimal.Floor(steps + 0.5m);
        if (whole != steps)
        {
            _warnings.Add(ModeCompiler.Message(_line,
                $"{text} is {steps.ToString(CultureInfo.InvariantCulture)} steps at 60 a second: it is taken as {whole.ToString(CultureInfo.InvariantCulture)}"));
        }
        if (whole > int.MaxValue)
        {
            throw new ModeCompileException(_line, $"{text} is longer than an int of steps can hold");
        }
        return new ModeToken(ModeTokenKind.Time, text, _line, (long)whole);
    }

    /// <summary>A real literal as binary32, correctly rounded (float.Parse rounds the decimal text once).</summary>
    private static float ParseReal(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
