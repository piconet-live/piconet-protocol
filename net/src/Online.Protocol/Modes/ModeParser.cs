namespace Online.Protocol;

/// <summary>Reads a mode file's tokens into its tree (the language's grammar, section 13; the first slice's subset:
/// no sub, wait, board, team or seats variables). It knows the syntax only; names and types are the compiler's. The
/// standard library's <c>every(T) { }</c> is expanded here into the rule it stands for (the language's section 9).</summary>
internal sealed class ModeParser
{
    /// <summary>The language's keywords (section 12) and <c>race</c>: none may name a parameter or a variable.</summary>
    public static readonly HashSet<string> Keywords =
    [
        "mode", "api", "badge", "about", "param", "number", "delay", "switch", "choice", "default", "step", "const",
        "global", "seat", "team", "int", "real", "bool", "seats", "sub", "rule", "event", "every", "each", "when",
        "while", "do", "board", "if", "else", "for", "in", "where", "by", "desc", "repeat", "wait", "abort", "restart",
        "call", "return", "and", "or", "not", "true", "false", "none", "on", "off", "laps", "course", "macro", "race",
    ];

    private static readonly Dictionary<string, ModeType> Types = new()
    {
        ["int"] = ModeType.Int,
        ["real"] = ModeType.Real,
        ["bool"] = ModeType.Bool,
        ["seat"] = ModeType.Seat,
    };

    private static readonly HashSet<string> SetHelpers = ["count", "min", "max", "sum", "any", "all"];

    private static readonly string[] Comparisons = ["==", "!=", "<", "<=", ">", ">="];

    private static readonly string[] Assignments = ["=", "+=", "-=", "*=", "/="];

    private readonly List<ModeToken> _tokens;
    private int _at;

    private ModeParser(List<ModeToken> tokens) => _tokens = tokens;

    /// <summary>The file's items in its order, the header first.</summary>
    public static List<ModeItem> Parse(List<ModeToken> tokens) => new ModeParser(tokens).File();

    private ModeToken Current => _tokens[_at];

    private ModeToken PeekToken(int ahead) => _tokens[Math.Min(_at + ahead, _tokens.Count - 1)];

    private ModeToken Take()
    {
        var token = Current;
        if (token.Kind != ModeTokenKind.End)
        {
            _at++;
        }
        return token;
    }

    private bool Accept(string text)
    {
        if (!Current.Is(text))
        {
            return false;
        }
        _at++;
        return true;
    }

    private ModeToken Expect(string text)
    {
        if (!Current.Is(text))
        {
            throw Error($"'{text}' is expected here, not {Describe(Current)}");
        }
        return Take();
    }

    private ModeCompileException Error(string message) => new(Current.Line, message);

    private static string Describe(ModeToken token) =>
        token.Kind == ModeTokenKind.End ? token.Text
        : token.Kind == ModeTokenKind.Text ? $"the string \"{token.Text}\""
        : $"'{token.Text}'";

    private string Text()
    {
        if (Current.Kind != ModeTokenKind.Text)
        {
            throw Error($"a string in quotes is expected here, not {Describe(Current)}");
        }
        return Take().Text;
    }

    /// <summary>A name that is not a keyword: a parameter's, a variable's, a bound seat's.</summary>
    private string NewName(string what)
    {
        if (Current.Kind != ModeTokenKind.Name)
        {
            throw Error($"{what}'s name is expected here, not {Describe(Current)}");
        }
        if (Keywords.Contains(Current.Text))
        {
            throw Error($"'{Current.Text}' is a word of the language and cannot name {what}");
        }
        return Take().Text;
    }

    private ModeType Type()
    {
        if (Current.Kind != ModeTokenKind.Name || !Types.TryGetValue(Current.Text, out var type))
        {
            throw Error($"a type (int, real, bool or seat) is expected here, not {Describe(Current)}");
        }
        _at++;
        return type;
    }

    private List<ModeItem> File()
    {
        var items = new List<ModeItem> { Header() };
        while (Current.Kind != ModeTokenKind.End)
        {
            items.Add(Item());
        }
        return items;
    }

    private ModeHeaderItem Header()
    {
        var line = Current.Line;
        if (!Current.Is("mode"))
        {
            throw Error("a mode file starts with its header: mode \"Name\" { api 1; }");
        }
        _at++;
        var name = Text();
        Expect("{");
        int? api = null;
        string badge = "", about = "";
        while (!Accept("}"))
        {
            if (Accept("api"))
            {
                api = (int)WholeLiteral("the api");
            }
            else if (Accept("badge"))
            {
                badge = Text();
            }
            else if (Accept("about"))
            {
                about = Text();
            }
            else
            {
                throw Error($"the header holds api, badge and about, not {Describe(Current)}");
            }
            Expect(";");
        }
        return new ModeHeaderItem(line, name, api, badge, about);
    }

    private long WholeLiteral(string what)
    {
        if (Current.Kind != ModeTokenKind.Whole)
        {
            throw Error($"{what} is a whole number, not {Describe(Current)}");
        }
        return Take().Whole;
    }

    private ModeItem Item()
    {
        var token = Current;
        if (token.Kind != ModeTokenKind.Name)
        {
            throw Error($"a parameter, a setup word, a variable or a rule is expected here, not {Describe(token)}");
        }
        return token.Text switch
        {
            "param" => Param(),
            "const" => Const(),
            "global" or "seat" => Variable(),
            "rule" => Rule(),
            "every" when PeekToken(1).Is("(") => Every(),
            _ => Setup(),
        };
    }

    private ModeParamItem Param()
    {
        var line = Take().Line;
        var name = NewName("a parameter");
        var label = Text();
        var kindToken = Take();
        var item = kindToken.Text switch
        {
            "number" => RangeParam(line, name, label, ModeParamKind.Number),
            "delay" => RangeParam(line, name, label, ModeParamKind.Delay),
            "switch" => SwitchParam(line, name, label),
            "choice" => ChoiceParam(line, name, label),
            _ => throw new ModeCompileException(kindToken.Line,
                $"a parameter is a number, a delay, a switch or a choice, not {Describe(kindToken)}"),
        };
        Expect(";");
        return item;
    }

    private ModeParamItem RangeParam(int line, string name, string label, ModeParamKind kind)
    {
        var least = Literal();
        Expect("..");
        var most = Literal();
        var step = Accept("step") ? Literal() : null;
        var value = Accept("default") ? Literal() : null;
        return new ModeParamItem(line, name, label, kind, least, most, step, value, [], null);
    }

    private ModeParamItem SwitchParam(int line, string name, string label)
    {
        Expect("default");
        var value = Literal();
        return new ModeParamItem(line, name, label, ModeParamKind.Switch, null, null, null, value, [], null);
    }

    private ModeParamItem ChoiceParam(int line, string name, string label)
    {
        Expect("[");
        var choices = new List<string> { Text() };
        while (Accept(","))
        {
            choices.Add(Text());
        }
        Expect("]");
        var value = Accept("default") ? Text() : null;
        return new ModeParamItem(line, name, label, ModeParamKind.Choice, null, null, null, null, choices, value);
    }

    /// <summary>A literal, a minus in front allowed: a parameter's range, a setup word's value.</summary>
    private ModeLiteral Literal()
    {
        var line = Current.Line;
        var negative = Accept("-");
        if (Atom() is not ModeLiteral literal)
        {
            throw new ModeCompileException(line, "a number is expected here");
        }
        return negative ? Negate(literal) ?? throw new ModeCompileException(line, $"'-{literal.Text}' is not a number") : literal;
    }

    private static ModeLiteral? Negate(ModeLiteral literal) => literal.Type switch
    {
        ModeType.Int => literal with { Bits = unchecked(-literal.Bits), Text = "-" + literal.Text },
        ModeType.Real => literal with { Bits = ModeOperators.Cell(-ModeOperators.Real(literal.Bits)), Text = "-" + literal.Text },
        _ => null,
    };

    private ModeConstItem Const()
    {
        var line = Take().Line;
        var type = Type();
        var name = NewName("a constant");
        Expect("=");
        var value = Expression();
        Expect(";");
        return new ModeConstItem(line, type, name, value);
    }

    private ModeVariableItem Variable()
    {
        var token = Take();
        var type = Type();
        var name = NewName("a variable");
        var value = Accept("=") ? Expression() : null;
        Expect(";");
        return new ModeVariableItem(token.Line, token.Text == "seat", type, name, value);
    }

    /// <summary>A setup word (its words matched against the api's, the longest first) and its arguments.</summary>
    private ModeSetupItem Setup()
    {
        var line = Current.Line;
        var word = ModeWords.Setups.Select(s => s.Name).OrderByDescending(n => n.Length).FirstOrDefault(StartsHere)
            ?? throw Error($"'{Current.Text}' is not a word of api {ModeWords.Api}");
        _at += word.Split(' ').Length;
        var arguments = new List<ModeSetupArgument>();
        while (!Accept(";"))
        {
            var argumentLine = Current.Line;
            var name = "";
            if (ModeWords.Setups.Single(s => s.Name == word).Args.Any(a => a.Name.Length > 0 && Current.Is(a.Name)))
            {
                name = Take().Text;
            }
            arguments.Add(new ModeSetupArgument(argumentLine, name, SetupValue()));
        }
        return new ModeSetupItem(line, word, arguments);
    }

    private bool StartsHere(string words)
    {
        var parts = words.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            if (!PeekToken(i).Is(parts[i]))
            {
                return false;
            }
        }
        return true;
    }

    private ModeExpr SetupValue()
    {
        if (Current.Is("unlimited"))
        {
            return new ModeLiteral(Take().Line, ModeType.Int, 0, "unlimited");
        }
        if (Current.Kind == ModeTokenKind.Name && !Keywords.Contains(Current.Text))
        {
            var token = Take();
            return new ModeName(token.Line, token.Text);
        }
        return Literal();
    }

    private ModeRuleItem Rule()
    {
        var line = Take().Line;
        var label = Current.Kind == ModeTokenKind.Text ? Take().Text : "";
        Expect("{");
        var (eventName, bound) = Event();
        var whens = new List<ModeExpr>();
        var whiles = new List<ModeExpr>();
        while (Current.Is("when") || Current.Is("while"))
        {
            (Take().Text == "when" ? whens : whiles).Add(Expression());
            Expect(";");
        }
        Expect("do");
        var body = Block();
        Expect("}");
        return new ModeRuleItem(line, label, eventName, bound, whens, whiles, body);
    }

    /// <summary><c>event NAME [(names)];</c>: the event's words as one name, and the name it binds.</summary>
    private (string Event, string? Bound) Event()
    {
        Expect("event");
        var words = new List<string>();
        while (Current.Kind == ModeTokenKind.Name)
        {
            words.Add(Take().Text);
        }
        if (words.Count == 0)
        {
            throw Error($"an event is expected here, not {Describe(Current)}");
        }
        string? bound = null;
        if (Accept("("))
        {
            bound = NewName("the event's seat");
            if (Current.Is(","))
            {
                throw Error("an event of api 1 binds one name, its seat");
            }
            Expect(")");
        }
        Expect(";");
        return (string.Join(' ', words), bound);
    }

    /// <summary><c>every(T) { body }</c>: the standard library's macro, a rule on every step while the race clock is on
    /// a multiple of T after GO.</summary>
    private ModeRuleItem Every()
    {
        var line = Take().Line;
        Expect("(");
        var start = _at;
        var time = Expression();
        var text = string.Join(' ', _tokens.Skip(start).Take(_at - start).Select(t => t.Text));
        Expect(")");
        var body = Block();
        ModeExpr Race(string field) => new ModeField(line, new ModeName(line, "race"), field);
        var steps = Race("steps");
        var zero = new ModeLiteral(line, ModeType.Int, 0, "0");
        var condition = new ModeBinary(line, "and",
            new ModeBinary(line, "and", Race("started"), new ModeBinary(line, ">", steps, zero)),
            new ModeBinary(line, "==", new ModeBinary(line, "%", steps, time), zero));
        return new ModeRuleItem(line, "every " + text, "every step", null, [], [condition], body, time);
    }

    private List<ModeStatement> Block()
    {
        Expect("{");
        var statements = new List<ModeStatement>();
        while (!Accept("}"))
        {
            if (Current.Kind == ModeTokenKind.End)
            {
                throw Error("a block opened with { is never closed with }");
            }
            statements.Add(Statement());
        }
        return statements;
    }

    private ModeStatement Statement()
    {
        var token = Current;
        if (token.Kind == ModeTokenKind.Name && Types.ContainsKey(token.Text) && PeekToken(1).Kind == ModeTokenKind.Name)
        {
            return LocalStatement();
        }
        return token.Text switch
        {
            "if" when token.Kind == ModeTokenKind.Name => If(),
            "for" when token.Kind == ModeTokenKind.Name => ForEach(),
            "repeat" when token.Kind == ModeTokenKind.Name => Repeat(),
            _ when IsActionWord(token) => ActionStatement(),
            _ => Assignment(),
        };
    }

    private bool IsActionWord(ModeToken token) =>
        token.Kind == ModeTokenKind.Name && !PeekToken(1).Is(".") && !Assignments.Any(a => PeekToken(1).Is(a))
        && ModeWords.Actions.Any(a => a.Name == token.Text);

    private ModeLocal LocalStatement()
    {
        var line = Current.Line;
        var type = Type();
        var name = NewName("a local");
        var value = Accept("=") ? Expression() : null;
        Expect(";");
        return new ModeLocal(line, type, name, value);
    }

    private ModeAssign Assignment()
    {
        var line = Current.Line;
        var target = Postfix();
        var op = Assignments.FirstOrDefault(Current.Is)
            ?? throw Error($"a statement is expected here: {Describe(Current)} does not start one");
        _at++;
        var value = Expression();
        Expect(";");
        return new ModeAssign(line, target, op, value);
    }

    private ModeActionStatement ActionStatement()
    {
        var token = Take();
        var arguments = new List<ModeExpr>();
        if (!Current.Is(";"))
        {
            arguments.Add(Expression());
            while (Accept(","))
            {
                arguments.Add(Expression());
            }
        }
        Expect(";");
        return new ModeActionStatement(token.Line, token.Text, arguments);
    }

    private ModeIf If()
    {
        var line = Take().Line;
        Expect("(");
        var condition = Expression();
        Expect(")");
        var then = Block();
        List<ModeStatement>? otherwise = null;
        if (Accept("else"))
        {
            otherwise = Current.Is("if") ? [If()] : Block();
        }
        return new ModeIf(line, condition, then, otherwise);
    }

    private ModeForEach ForEach()
    {
        var line = Take().Line;
        Expect("each");
        string bound;
        if (Accept("seat"))
        {
            bound = "seat";
        }
        else
        {
            bound = NewName("the loop's seat");
            Expect("in");
            if (!Current.Is("seats"))
            {
                throw Error($"for each walks the race's seats (in seats), not {Describe(Current)}");
            }
            _at++;
        }
        var where = Accept("where") ? Expression() : null;
        var keys = new List<ModeSortKey>();
        if (Accept("by"))
        {
            do
            {
                keys.Add(new ModeSortKey(Expression(), Accept("desc")));
            }
            while (Accept(","));
        }
        return new ModeForEach(line, bound, where, keys, Block());
    }

    private ModeRepeat Repeat()
    {
        var line = Take().Line;
        var count = Expression();
        return new ModeRepeat(line, count, Block());
    }

    // Expressions, the loosest first (the language's section 8).

    public ModeExpr Expression() => Or();

    private ModeExpr Or()
    {
        var left = And();
        while (Current.Is("or"))
        {
            var line = Take().Line;
            left = new ModeBinary(line, "or", left, And());
        }
        return left;
    }

    private ModeExpr And()
    {
        var left = Not();
        while (Current.Is("and"))
        {
            var line = Take().Line;
            left = new ModeBinary(line, "and", left, Not());
        }
        return left;
    }

    private ModeExpr Not()
    {
        if (Current.Is("not"))
        {
            var line = Take().Line;
            return new ModeUnary(line, "not", Not());
        }
        return Comparison();
    }

    private ModeExpr Comparison()
    {
        var left = Sum();
        var op = Comparisons.FirstOrDefault(Current.Is);
        if (op is null || Current.Kind != ModeTokenKind.Symbol)
        {
            return left;
        }
        var line = Take().Line;
        return new ModeBinary(line, op, left, Sum());
    }

    private ModeExpr Sum()
    {
        var left = Product();
        while (Current.Kind == ModeTokenKind.Symbol && (Current.Text == "+" || Current.Text == "-"))
        {
            var token = Take();
            left = new ModeBinary(token.Line, token.Text, left, Product());
        }
        return left;
    }

    private ModeExpr Product()
    {
        var left = Unary();
        while (Current.Kind == ModeTokenKind.Symbol && (Current.Text == "*" || Current.Text == "/" || Current.Text == "%"))
        {
            var token = Take();
            left = new ModeBinary(token.Line, token.Text, left, Unary());
        }
        return left;
    }

    private ModeExpr Unary()
    {
        if (Current.Kind == ModeTokenKind.Symbol && Current.Text == "-")
        {
            var line = Take().Line;
            var operand = Unary();
            // A negative literal is a literal: -1 is one Push.
            return operand is ModeLiteral literal && Negate(literal) is { } negative ? negative : new ModeUnary(line, "-", operand);
        }
        return Postfix();
    }

    private ModeExpr Postfix()
    {
        var expr = Atom();
        while (Current.Is("."))
        {
            _at++;
            if (Current.Kind != ModeTokenKind.Name)
            {
                throw Error($"a field's name is expected after '.', not {Describe(Current)}");
            }
            var field = Take();
            expr = new ModeField(field.Line, expr, field.Text);
        }
        return expr;
    }

    private ModeExpr Atom()
    {
        var token = Current;
        switch (token.Kind)
        {
            case ModeTokenKind.Whole:
            case ModeTokenKind.Time:
                _at++;
                return new ModeLiteral(token.Line, ModeType.Int, unchecked((int)token.Whole), token.Text, token.Kind == ModeTokenKind.Time);
            case ModeTokenKind.Fraction:
                _at++;
                return new ModeLiteral(token.Line, ModeType.Real, ModeOperators.Cell(token.Real), token.Text);
            case ModeTokenKind.Name:
                return NameAtom();
            case ModeTokenKind.Text:
                throw Error("a mode cannot work with text: a string is only a label, a name or a choice");
            default:
                if (Accept("("))
                {
                    var inner = Expression();
                    Expect(")");
                    return inner;
                }
                throw Error($"a value is expected here, not {Describe(token)}");
        }
    }

    private static readonly Dictionary<string, (ModeType Type, int Bits)> Words = new()
    {
        ["true"] = (ModeType.Bool, 1),
        ["false"] = (ModeType.Bool, 0),
        ["on"] = (ModeType.Bool, 1),
        ["off"] = (ModeType.Bool, 0),
        ["none"] = (ModeType.Seat, -1),
    };

    private ModeExpr NameAtom()
    {
        var token = Take();
        if (Words.TryGetValue(token.Text, out var word))
        {
            return new ModeLiteral(token.Line, word.Type, word.Bits, token.Text);
        }
        if (!Current.Is("("))
        {
            return new ModeName(token.Line, token.Text);
        }
        _at++;
        if (SetHelpers.Contains(token.Text) && !Current.Is(")"))
        {
            var first = Expression();
            if (Current.Is("for") || Current.Is("where") || token.Text == "count")
            {
                return SetCall(token, first);
            }
            var arguments = new List<ModeExpr> { first };
            while (Accept(","))
            {
                arguments.Add(Expression());
            }
            Expect(")");
            return new ModeCall(token.Line, token.Text, arguments);
        }
        return Call(token);
    }

    private ModeCall Call(ModeToken name)
    {
        var arguments = new List<ModeExpr>();
        if (!Accept(")"))
        {
            do
            {
                arguments.Add(Expression());
            }
            while (Accept(","));
            Expect(")");
        }
        return new ModeCall(name.Line, name.Text, arguments);
    }

    /// <summary>The rest of a set helper after its first expression: <c>count(seats where c)</c>,
    /// <c>min(e for each NAME where c)</c>.</summary>
    private ModeSetCall SetCall(ModeToken helper, ModeExpr first)
    {
        var bound = "seat";
        ModeExpr? value = first;
        if (helper.Text == "count")
        {
            if (first is not ModeName { Name: "seats" })
            {
                throw new ModeCompileException(helper.Line, "count counts the race's seats: count(seats where c)");
            }
            value = null;
        }
        if (Accept("for"))
        {
            Expect("each");
            bound = Accept("seat") ? "seat" : NewName("the helper's seat");
        }
        var where = Accept("where") ? Expression() : null;
        Expect(")");
        return new ModeSetCall(helper.Line, helper.Text, value, bound, where);
    }
}
