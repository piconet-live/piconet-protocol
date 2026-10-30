namespace Online.Protocol;

/// <summary>Reads a mode file's tokens into its tree (the language's grammar, section 13; no board yet). It knows the
/// syntax only; names and types are the compiler's. A use of a standard library macro (<see cref="ModeLibrary"/>:
/// <c>every(10s) { }</c>, <c>lives 3;</c>) is expanded here into the items it stands for, its arguments' tokens put
/// in for its parameters' names (the language's section 9).</summary>
internal sealed class ModeParser
{
    /// <summary>The language's keywords (section 12) and <c>race</c>: none may name a parameter or a variable.</summary>
    public static readonly HashSet<string> Keywords =
    [
        "mode", "api", "badge", "about", "param", "number", "delay", "switch", "choice", "default", "step", "const",
        "global", "seat", "team", "int", "real", "bool", "seats", "sub", "rule", "event", "every", "each", "when",
        "while", "do", "board", "if", "else", "for", "in", "where", "by", "desc", "repeat", "wait", "abort", "restart",
        "call", "return", "and", "or", "not", "true", "false", "none", "on", "off", "laps", "course", "macro", "race",
        "vec",
    ];

    /// <summary>A macro's uses inside macros' bodies go at most this deep (the library's are two).</summary>
    private const int MostDepth = 4;

    private static readonly Dictionary<string, ModeType> Types = new()
    {
        ["int"] = ModeType.Int,
        ["real"] = ModeType.Real,
        ["bool"] = ModeType.Bool,
        ["seat"] = ModeType.Seat,
        ["seats"] = ModeType.Seats,
        ["vec"] = ModeType.Vec,
    };

    private static readonly HashSet<string> SetHelpers = ["count", "min", "max", "sum", "any", "all", "first", "last", "nth"];

    /// <summary>The helpers whose first argument is a set of seats (the others' is a value for each seat).</summary>
    private static readonly HashSet<string> OverSets = ["count", "first", "last", "nth"];

    private static readonly string[] Comparisons = ["==", "!=", "<", "<=", ">", ">="];

    private static readonly string[] Assignments = ["=", "+=", "-=", "*=", "/="];

    private readonly List<ModeToken> _tokens;
    private readonly IReadOnlyDictionary<string, ModeMacro> _macros;
    private readonly int _depth;
    // A macro's expansion: the label its rules take when they have none ("every 10s").
    private readonly string _label;
    private int _at;

    private ModeParser(List<ModeToken> tokens, IReadOnlyDictionary<string, ModeMacro> macros, int depth = 0, string label = "")
    {
        _tokens = tokens;
        _macros = macros;
        _depth = depth;
        _label = label;
    }

    /// <summary>The file's items in its order, the header first; the uses of <paramref name="macros"/> expanded.</summary>
    public static List<ModeItem> Parse(List<ModeToken> tokens, IReadOnlyDictionary<string, ModeMacro> macros) =>
        new ModeParser(tokens, macros).File();

    /// <summary>A library's text: its macros (each with its body's tokens, kept to be put in at a use) and its subs.</summary>
    public static (Dictionary<string, ModeMacro> Macros, List<ModeSubItem> Subs) Library(List<ModeToken> tokens)
    {
        var parser = new ModeParser(tokens, new Dictionary<string, ModeMacro>());
        var macros = new Dictionary<string, ModeMacro>();
        var subs = new List<ModeSubItem>();
        while (parser.Current.Kind != ModeTokenKind.End)
        {
            if (parser.Current.Is("macro"))
            {
                var macro = parser.Macro();
                macros.Add(macro.Name, macro);
            }
            else if (parser.Current.Is("sub"))
            {
                subs.Add(parser.Sub());
            }
            else
            {
                throw parser.Error($"a library holds macros and subs, not {Describe(parser.Current)}");
            }
        }
        return (macros, subs);
    }

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
            throw Error($"a type (int, real, bool, seat, seats or vec) is expected here, not {Describe(Current)}");
        }
        _at++;
        return type;
    }

    private List<ModeItem> File()
    {
        var items = new List<ModeItem> { Header() };
        Items(items);
        return items;
    }

    private void Items(List<ModeItem> items)
    {
        while (Current.Kind != ModeTokenKind.End)
        {
            if (Current.Kind == ModeTokenKind.Name && _macros.TryGetValue(Current.Text, out var macro))
            {
                items.AddRange(Expand(macro));
            }
            else
            {
                items.Add(Item());
            }
        }
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
        var apiLine = line;
        string badge = "", about = "";
        while (!Accept("}"))
        {
            if (Accept("api"))
            {
                apiLine = Current.Line;
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
        return new ModeHeaderItem(line, name, api, apiLine, badge, about);
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
            throw Error($"a parameter, a setup word, a variable, a sub or a rule is expected here, not {Describe(token)}");
        }
        return token.Text switch
        {
            "param" => Param(),
            "const" => Const(),
            "global" or "seat" or "team" => Variable(),
            "sub" => Sub(),
            "rule" => Rule(),
            "macro" => throw Error("macros are the standard library's (api 1): a mode file reuses its code with a sub"),
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
        var scope = token.Text switch
        {
            "seat" => ModeScope.Seat,
            "team" => ModeScope.Team,
            _ => ModeScope.Global,
        };
        return new ModeVariableItem(token.Line, scope, type, name, value);
    }

    /// <summary><c>sub [TYPE] NAME(TYPE NAME, ...) { }</c>.</summary>
    private ModeSubItem Sub()
    {
        var line = Take().Line;
        ModeType? returns = Current.Kind == ModeTokenKind.Name && Types.ContainsKey(Current.Text) ? Type() : null;
        var name = NewName("a sub");
        Expect("(");
        var parameters = new List<ModeSubParam>();
        if (!Accept(")"))
        {
            do
            {
                var type = Type();
                parameters.Add(new ModeSubParam(type, NewName("a sub's parameter")));
            }
            while (Accept(","));
            Expect(")");
        }
        return new ModeSubItem(line, returns, name, parameters, Block());
    }

    /// <summary>A library's <c>macro NAME(KIND NAME, ...) { items }</c>: its body kept as tokens.</summary>
    private ModeMacro Macro()
    {
        var line = Take().Line;
        var name = Take().Text;
        Expect("(");
        var parameters = new List<(string Kind, string Name)>();
        if (!Accept(")"))
        {
            do
            {
                var kind = Take().Text;
                if (kind is not ("time" or "int" or "block"))
                {
                    throw Error($"a macro's parameter is a time, an int or a block, not '{kind}'");
                }
                parameters.Add((kind, Take().Text));
            }
            while (Accept(","));
            Expect(")");
        }
        if (parameters.Take(parameters.Count - 1).Any(p => p.Kind == "block"))
        {
            throw new ModeCompileException(line, $"the macro {name}'s block is its last parameter");
        }
        var start = _at + 1;
        SkipBraces();
        return new ModeMacro(name, parameters, _tokens.Skip(start).Take(_at - start - 1).ToList());
    }

    /// <summary>Past a <c>{ }</c> and everything in it, braces nested.</summary>
    private void SkipBraces()
    {
        Expect("{");
        for (var depth = 1; depth > 0; _at++)
        {
            if (Current.Kind == ModeTokenKind.End)
            {
                throw Error("a block opened with { is never closed with }");
            }
            depth += Current.Is("{") ? 1 : Current.Is("}") ? -1 : 0;
        }
    }

    /// <summary>A macro's use, <c>NAME(args) { block }</c> or <c>NAME args;</c>: its body's items, with the tokens of
    /// each argument put in for its parameter's name (an expression in parentheses) and the use's line on every
    /// token of the body. Its rules without a label are labelled by the use (<c>every 10s</c>).</summary>
    private List<ModeItem> Expand(ModeMacro macro)
    {
        var use = Take();
        if (_depth >= MostDepth)
        {
            throw new ModeCompileException(use.Line, $"the macro {macro.Name} is used inside macros {MostDepth} deep");
        }
        var values = macro.Params.Where(p => p.Kind != "block").ToList();
        var arguments = new List<(List<ModeToken> Tokens, ModeExpr? Value)>();
        var parenthesised = Accept("(");
        if (parenthesised ? !Current.Is(")") : !Current.Is(";"))
        {
            do
            {
                var start = _at;
                var value = Expression();
                arguments.Add((_tokens.Skip(start).Take(_at - start).ToList(), value));
            }
            while (Accept(","));
        }
        Expect(parenthesised ? ")" : ";");
        if (arguments.Count != values.Count)
        {
            throw new ModeCompileException(use.Line, $"{macro.Name} takes {values.Count} value{(values.Count == 1 ? "" : "s")} ({string.Join(", ", values.Select(v => v.Name))}), not {arguments.Count}");
        }
        if (macro.Params.Count > 0 && macro.Params[^1].Kind == "block")
        {
            if (!parenthesised || !Current.Is("{"))
            {
                throw new ModeCompileException(use.Line, $"{macro.Name} takes a block: {macro.Name}({string.Join(", ", values.Select(v => v.Name))}) {{ ... }}");
            }
            var start = _at;
            SkipBraces();
            arguments.Add((_tokens.Skip(start).Take(_at - start).ToList(), null));
            Accept(";");
        }
        var items = new List<ModeItem>();
        var given = new Dictionary<string, List<ModeToken>>();
        for (var i = 0; i < macro.Params.Count; i++)
        {
            var (kind, name) = macro.Params[i];
            var tokens = arguments[i].Tokens;
            if (kind == "time")
            {
                items.Add(new ModeTimeArgument(use.Line, macro.Name, arguments[i].Value!));
            }
            given[name] = kind == "block" ? tokens
                : [new ModeToken(ModeTokenKind.Symbol, "(", tokens[0].Line), .. tokens, new ModeToken(ModeTokenKind.Symbol, ")", tokens[^1].Line)];
        }
        var body = new List<ModeToken>();
        foreach (var token in macro.Body)
        {
            if (token.Kind == ModeTokenKind.Name && given.TryGetValue(token.Text, out var argument))
            {
                body.AddRange(argument);
            }
            else
            {
                body.Add(token with { Line = use.Line });
            }
        }
        body.Add(new ModeToken(ModeTokenKind.End, $"the end of {macro.Name}", use.Line));
        var label = string.Join(", ", arguments.Where(a => a.Value is not null).Select(a => string.Join(' ', a.Tokens.Select(t => t.Text))));
        var expansion = new ModeParser(body, _macros, _depth + 1, label.Length > 0 ? $"{macro.Name} {label}" : macro.Name);
        expansion.Items(items);
        return items;
    }

    /// <summary>A setup word (its words matched against the api's, the longest first) and its arguments.</summary>
    private ModeSetupItem Setup()
    {
        var line = Current.Line;
        var word = ModeWords.Setups.Select(s => s.Name).OrderByDescending(n => n.Length).FirstOrDefault(StartsHere)
            ?? throw Error($"'{Current.Text}' is not a word of api {ModeWords.Api}");
        _at += word.Split(' ').Length;
        if (ModeWords.Setups.Single(s => s.Name == word).Id == ModeSetup.LapsByCourse)
        {
            return SetupTable(line, word);
        }
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

    /// <summary>A setup word's table: <c>{ "Venue: Track": n; ...; else: n; }</c>, its <c>else</c> the word's
    /// argument.</summary>
    private ModeSetupItem SetupTable(int line, string word)
    {
        Expect("{");
        var arguments = new List<ModeSetupArgument>();
        var rows = new List<ModeSetupRow>();
        while (!Accept("}"))
        {
            var rowLine = Current.Line;
            if (Accept("else"))
            {
                Expect(":");
                arguments.Add(new ModeSetupArgument(rowLine, "else", SetupValue()));
            }
            else
            {
                if (Current.Kind != ModeTokenKind.Text)
                {
                    throw Error($"a course's name in quotes or else is expected here, not {Describe(Current)}");
                }
                var key = Take().Text;
                Expect(":");
                rows.Add(new ModeSetupRow(rowLine, key, SetupValue()));
            }
            Expect(";");
        }
        Accept(";");
        return new ModeSetupItem(line, word, arguments, rows);
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
        var label = Current.Kind == ModeTokenKind.Text ? Take().Text : _label;
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
            "wait" when token.Kind == ModeTokenKind.Name => Wait(),
            "call" when token.Kind == ModeTokenKind.Name => CallStatement(),
            "return" when token.Kind == ModeTokenKind.Name => Return(),
            _ when IsActionWord(token) => ActionStatement(),
            _ => Assignment(),
        };
    }

    private bool IsActionWord(ModeToken token) =>
        token.Kind == ModeTokenKind.Name && !PeekToken(1).Is(".") && !Assignments.Any(a => PeekToken(1).Is(a))
        && ModeWords.Actions.Any(a => a.Name == token.Text);

    private ModeWait Wait()
    {
        var line = Take().Line;
        var steps = Expression();
        var kind = Current.Is("abort") || Current.Is("restart") ? Take().Text : "";
        ModeExpr? condition = null;
        if (kind.Length > 0)
        {
            Expect("when");
            condition = Expression();
        }
        Expect(";");
        return new ModeWait(line, steps, kind, condition);
    }

    private ModeCallStatement CallStatement()
    {
        var line = Take().Line;
        if (Current.Kind != ModeTokenKind.Name || !PeekToken(1).Is("("))
        {
            throw Error("call names a sub and its arguments: call name(a, b);");
        }
        var name = Take();
        Expect("(");
        var call = Call(name);
        Expect(";");
        return new ModeCallStatement(line, call);
    }

    private ModeReturn Return()
    {
        var line = Take().Line;
        var value = Current.Is(";") ? null : Expression();
        Expect(";");
        return new ModeReturn(line, value);
    }

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
            ?? throw (target is ModeName word
                ? new ModeCompileException(line, $"'{word.Name}' is not a word of api {ModeWords.Api}")
                : Error($"a statement is expected here: {Describe(Current)} does not start one"));
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
        ModeExpr? set = null;
        if (Accept("seat"))
        {
            bound = "seat";
        }
        else
        {
            bound = NewName("the loop's seat");
            Expect("in");
            set = AllSeats(Expression());
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
        return new ModeForEach(line, bound, where, keys, Block(), set);
    }

    /// <summary>A loop's or helper's set: null for <c>seats</c>, the race's every seat (a walk with no set to test).</summary>
    private static ModeExpr? AllSeats(ModeExpr set) => set is ModeName { Name: "seats" } ? null : set;

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
        var op = Current.Is("in") ? "in" : Comparisons.FirstOrDefault(Current.Is);
        if (op is null || (Current.Kind != ModeTokenKind.Symbol && op != "in"))
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
        while (Current.Kind == ModeTokenKind.Symbol && (Current.Text == "*" || Current.Text == "/" || Current.Text == "%" || Current.Text == "&"))
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
            if (Current.Is("for") || Current.Is("where") || OverSets.Contains(token.Text))
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

    /// <summary>The rest of a set helper after its first expression: <c>count(S where c)</c>,
    /// <c>first(S where c by key [desc], ...)</c> and <c>last</c>, <c>nth(S where c by key, i)</c> (with keys, the
    /// last of the list is i), or <c>min(e for each NAME in S where c)</c>.</summary>
    private ModeSetCall SetCall(ModeToken helper, ModeExpr first)
    {
        var bound = "seat";
        ModeExpr? value = first;
        ModeExpr? set = null;
        if (OverSets.Contains(helper.Text))
        {
            set = AllSeats(first);
            value = null;
        }
        else if (Accept("for"))
        {
            Expect("each");
            bound = Accept("seat") ? "seat" : NewName("the helper's seat");
            if (Accept("in"))
            {
                set = AllSeats(Expression());
            }
        }
        var where = Accept("where") ? Expression() : null;
        var keys = new List<ModeSortKey>();
        if (helper.Text is "first" or "last" or "nth" && Accept("by"))
        {
            do
            {
                keys.Add(new ModeSortKey(Expression(), Accept("desc")));
            }
            while (Accept(","));
        }
        ModeExpr? index = null;
        if (helper.Text == "nth")
        {
            if (keys.Count > 0)
            {
                var last = keys[^1];
                keys.RemoveAt(keys.Count - 1);
                index = last.Descending || keys.Count == 0
                    ? throw new ModeCompileException(helper.Line, "nth takes its keys, then the place: nth(S where c by key, i)")
                    : last.Key;
            }
            else
            {
                Expect(",");
                index = Expression();
            }
        }
        Expect(")");
        return new ModeSetCall(helper.Line, helper.Text, value, bound, where, set, keys, index);
    }
}
