using System.Globalization;

namespace Online.Protocol;

/// <summary>What compiling a mode file gave: the package (null when the file has an error), its content hash and
/// share code, the static budget (the operations a step at worst, the language's 11.1), and the messages as
/// "line N: message".</summary>
public sealed record ModeCompilation(byte[]? Package, ModePackage? Mode, string? Hash, string? ShareCode, long Budget,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>The one compiler of game modes (D11 of the design, piconet-docs research/game-modes/): a <c>.fzmode</c>
/// file to its package (formats.md section 9), for the launcher (checks while writing, the lobby) and the service
/// (uploads). The language is language.md's: the header, parameters, the setup words, constants, variables kept for
/// the race, a seat or a team, subs, rules, waits, the set helpers, vecs, random numbers, and the standard library's
/// macros and subs (<see cref="ModeLibrary"/>, compiled with every file); no board yet. The vocabulary is
/// protocol/modes.toml's, through the generated <see cref="ModeWords"/>. The first error stops the compile; warnings
/// are gathered.</summary>
public static class ModeCompiler
{
    /// <summary>A message as the compiler gives them: "line N: message".</summary>
    public static string Message(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"line {line}: {message}");

    public static ModeCompilation Compile(string source) =>
        Compile(source, ModeLibrary.Macros, ModeLibrary.Subs);

    /// <summary>The standard library's macros and subs by name (std.fzmode).</summary>
    public static (IReadOnlyList<string> Macros, IReadOnlyList<string> Subs) Library =>
        ([.. ModeLibrary.Macros.Keys.Order(StringComparer.Ordinal)], [.. ModeLibrary.Subs.Select(s => s.Name)]);

    /// <summary>A compile with another library's text than the build's (a test's own macros and subs).</summary>
    public static ModeCompilation Compile(string source, string library)
    {
        var (macros, subs) = ModeLibrary.Read(library);
        return Compile(source, macros, subs);
    }

    private static ModeCompilation Compile(string source, IReadOnlyDictionary<string, ModeMacro> macros,
        IReadOnlyList<ModeSubItem> subs)
    {
        var warnings = new List<string>();
        try
        {
            var items = ModeParser.Parse(ModeLexer.Tokens(source, warnings), macros);
            var (mode, budget) = new Build(warnings, subs).File(items, source);
            var package = mode.Write();
            if (package.Length > ModeWords.PackageBytes)
            {
                throw new ModeCompileException(1, $"the package is {package.Length} bytes, past api {ModeWords.Api}'s {ModeWords.PackageBytes} (the source included)");
            }
            var hash = ModePackage.Hash(package);
            return new ModeCompilation(package, mode, hash, ModePackage.ShareCode(hash), budget, [], warnings);
        }
        catch (ModeCompileException error)
        {
            return new ModeCompilation(null, null, null, null, 0, [Message(error.Line, error.Message)], warnings);
        }
    }

    /// <summary>One compile: the file's items in their order, each declared before it is used.</summary>
    private sealed class Build(List<string> warnings, IReadOnlyList<ModeSubItem> library)
    {
        private readonly ModeNames _names = new();
        private readonly List<ModeParam> _params = [];
        private readonly List<ModeSetupWord> _setups = [];
        private readonly List<int> _globals = [];
        private readonly List<int> _seatWords = [];
        private readonly List<int> _teamWords = [];
        private readonly List<ModeRule> _rules = [];
        private ModeEmitter? _emitter;
        // the rule instances that may wait at once, so far (the api's waits)
        private int _waiting;

        public (ModePackage Mode, long Budget) File(List<ModeItem> items, string source)
        {
            var header = (ModeHeaderItem)items[0];
            var api = header.Api ?? throw new ModeCompileException(header.Line, "the header names no api: api 1;");
            if (api > ModeWords.Api || api < 1)
            {
                throw new ModeCompileException(header.ApiLine, $"api {api} is not one this build knows: its newest is api {ModeWords.Api}");
            }
            _emitter = new ModeEmitter(_names, warnings);
            Subs(items.OfType<ModeSubItem>().ToList());
            foreach (var item in items.Skip(1))
            {
                Item(item);
            }
            // stop_all ends nothing (rule 0x80 only brakes): a mode with it must reach an end of its own
            if (_emitter.Used.TryGetValue(ModeAction.StopAll, out var stopLine) && !_emitter.Used.ContainsKey(ModeAction.EndRace)
                && !_setups.Any(s => s.Id == ModeSetup.ClockLimit))
            {
                throw new ModeCompileException(stopLine, "stop_all only brakes the machines: a mode with it needs end_race or clock_limit, or its race never ends");
            }
            var mode = new ModePackage
            {
                Api = api,
                Name = header.Name,
                Badge = header.Badge,
                About = header.About,
                Params = _params,
                Setups = _setups,
                Globals = _globals,
                SeatWords = _seatWords,
                TeamWords = _teamWords,
                Rules = _rules,
                Code = _emitter.Code,
                Source = source,
            };
            return (mode, _emitter.Cost);
        }

        private void Item(ModeItem item)
        {
            switch (item)
            {
                case ModeParamItem param:
                    Param(param);
                    break;
                case ModeSetupItem setup:
                    Setup(setup);
                    break;
                case ModeConstItem constant:
                    _names.Declare(constant.Line, constant.Name, new ModeDeclared(ModeNameKind.Const, constant.Type, 0,
                        _names.Constant(constant.Value, constant.Type, $"the constant {constant.Name}")));
                    break;
                case ModeVariableItem variable:
                    Variable(variable);
                    break;
                case ModeRuleItem rule:
                    Rule(rule);
                    break;
                case ModeSubItem:
                    break;   // (declared before the items, Subs)
                case ModeTimeArgument time:
                    TimeArgument(time);
                    break;
                default:
                    throw new ModeCompileException(item.Line, "this is not an item of api 1");
            }
        }

        private void Param(ModeParamItem item)
        {
            if (_params.Count >= 256)
            {
                throw new ModeCompileException(item.Line, "a mode has at most 256 parameters");
            }
            var param = item.Kind switch
            {
                ModeParamKind.Number => ModeParams.Number(item),
                ModeParamKind.Delay => ModeParams.Delay(item),
                ModeParamKind.Switch => ModeParams.Switch(item),
                _ => ModeParams.Choice(item),
            };
            var type = item.Kind == ModeParamKind.Switch ? ModeType.Bool : param.Type;
            _names.Declare(item.Line, item.Name, new ModeDeclared(ModeNameKind.Param, type, _params.Count, Param: param));
            _params.Add(param);
        }

        private void Setup(ModeSetupItem item)
        {
            var word = ModeWords.Setups.Single(s => s.Name == item.Word);
            if (_setups.Any(s => s.Id == word.Id))
            {
                throw new ModeCompileException(item.Line, $"{word.Name} is set twice");
            }
            var given = new Dictionary<string, ModeSetupArgument>();
            foreach (var argument in item.Arguments)
            {
                var name = argument.Name.Length > 0 ? argument.Name
                    : word.Args.Any(a => a.Name.Length == 0) ? ""
                    : throw new ModeCompileException(argument.Line, word.Args.Count == 0
                        ? $"{word.Name} takes no value"
                        : $"{word.Name}'s values are named: {string.Join(", ", word.Args.Select(a => a.Name))}");
                if (!given.TryAdd(name, argument))
                {
                    throw new ModeCompileException(argument.Line, $"{word.Name} is given {(name.Length > 0 ? name : "its value")} twice");
                }
            }
            var values = word.Args.Select(a => SetupValue(item, word.Name, a, given.GetValueOrDefault(a.Name))).ToList();
            if (word.Id is ModeSetup.Laps or ModeSetup.LapsByCourse &&
                _setups.Any(s => s.Id is ModeSetup.Laps or ModeSetup.LapsByCourse))
            {
                throw new ModeCompileException(item.Line, "laps and laps by course: one of them");
            }
            // a table's rows: a pair a course, its number and its value (in the range of the word's first argument)
            var courses = new HashSet<int>();
            foreach (var row in item.Table ?? [])
            {
                var course = CourseTable.Courses.FirstOrDefault(c => $"{c.Place}: {c.Track}" == row.Key).Course;
                if (course == 0)
                {
                    throw new ModeCompileException(row.Line, $"\"{row.Key}\" is no course of the game's (\"Venue: Track\", as courses.toml names them)");
                }
                if (!courses.Add(course))
                {
                    throw new ModeCompileException(row.Line, $"\"{row.Key}\" is in the table twice");
                }
                values.Add(new ModeSetupValue(false, course));
                var laps = SetupValue(item, $"{word.Name} \"{row.Key}\"", word.Args[0],
                    new ModeSetupArgument(row.Line, "", row.Value));
                values.Add(laps.FromParam
                    ? throw new ModeCompileException(row.Line, $"a row of {word.Name} is a number, not a parameter")
                    : laps);
            }
            _setups.Add(new ModeSetupWord(word.Id, values));
        }

        /// <summary>A setup word's argument: the parameter it names, the constant given, or the word's default.</summary>
        private ModeSetupValue SetupValue(ModeSetupItem item, string word, ModeArgument argument, ModeSetupArgument? given)
        {
            var what = argument.Name.Length > 0 ? $"{word}'s {argument.Name}" : word;
            if (given is null)
            {
                var value = argument.Default ?? throw new ModeCompileException(item.Line, $"{what} needs its value");
                return new ModeSetupValue(false, argument.Type == ModeType.Real ? ModeOperators.Cell((float)value) : (int)value);
            }
            if (given.Value is ModeName name && _names.Find(name.Name) is { Kind: ModeNameKind.Param } param)
            {
                if (param.Param!.Type != argument.Type || param.Type == ModeType.Bool)
                {
                    throw new ModeCompileException(given.Line, $"{what} takes {ModeTypes.Name(argument.Type)}, and the parameter {name.Name} is not one");
                }
                if (!Within(argument, param.Param.Least) || !Within(argument, param.Param.Most))
                {
                    throw new ModeCompileException(given.Line, $"the parameter {name.Name}'s range goes outside {what}'s {Range(argument)}");
                }
                return new ModeSetupValue(true, param.Index);
            }
            var cell = _names.Constant(given.Value, argument.Type, what);
            if (!Within(argument, cell))
            {
                throw new ModeCompileException(given.Line, $"{what} is {Range(argument)}");
            }
            return new ModeSetupValue(false, cell);
        }

        private static bool Within(ModeArgument argument, int cell)
        {
            double value = argument.Type == ModeType.Real ? ModeOperators.Real(cell) : cell;
            return value >= argument.Least && value <= argument.Most;
        }

        private static string Range(ModeArgument argument) =>
            string.Create(CultureInfo.InvariantCulture, $"{argument.Least}..{argument.Most}");

        private void Variable(ModeVariableItem item)
        {
            var (kind, list, limit, most) = item.Scope switch
            {
                ModeScope.Seat => (ModeNameKind.SeatVariable, _seatWords, ModeWords.SeatWords, "words a seat"),
                ModeScope.Team => (ModeNameKind.TeamVariable, _teamWords, ModeWords.TeamWords, "words a team"),
                _ => (ModeNameKind.Global, _globals, ModeWords.Globals, "globals"),
            };
            if (list.Count >= limit)
            {
                throw new ModeCompileException(item.Line, $"a mode keeps at most {limit} {most}");
            }
            if (item.Type == ModeType.Vec)
            {
                throw new ModeCompileException(item.Line, "a vec is a value of the step, not one to keep: keep its x, y and z as reals");
            }
            var start = item.Value is null ? (item.Type == ModeType.Seat ? -1 : 0)
                : _names.Constant(item.Value, item.Type, $"{item.Name}'s start value");
            _names.Declare(item.Line, item.Name, new ModeDeclared(kind, item.Type, list.Count));
            list.Add(start);
        }

        private void Rule(ModeRuleItem item)
        {
            if (_rules.Count >= ModeWords.Rules)
            {
                throw new ModeCompileException(item.Line, $"a mode has at most {ModeWords.Rules} rules");
            }
            var e = ModeWords.Events.FirstOrDefault(x => x.Name == item.Event);
            if (e.Name is null)
            {
                throw new ModeCompileException(item.Line, $"'{item.Event}' is not an event of api {ModeWords.Api}: {string.Join(", ", ModeWords.Events.Select(x => x.Name))}");
            }
            if (item.Bound is not null && (!e.BindsSeat || e.Continuous))
            {
                throw new ModeCompileException(item.Line, $"{e.Name} binds {(e.BindsSeat ? "seat, and takes no name" : "nothing")}");
            }
            if (item.Second is not null && e.Second is null)
            {
                throw new ModeCompileException(item.Line, $"{e.Name} binds one name, its seat");
            }
            if (!e.Continuous && item.Whiles.Count > 0)
            {
                throw new ModeCompileException(item.Whiles[0].Line, $"while is for the events that go on (every step, each seat): {e.Name} happens once, so its rule takes when");
            }
            var before = _emitter!.Cost;
            var (rule, waits) = _emitter.Rule(item, e.Id, e.BindsSeat, e.Second);
            _rules.Add(rule);
            if (waits)
            {
                // a waiting instance resumes in the step's waits and may fire again later in the step: twice its code
                _emitter.Count(_emitter.Cost - before);
                _waiting += e.BindsSeat ? ModeWords.Seats : 1;
                if (_waiting > ModeWords.Waits)
                {
                    throw new ModeCompileException(item.Line, $"a mode has at most {ModeWords.Waits} rules waiting at once, a rule on a seat counted for every seat: with this one, {_waiting} could");
                }
            }
            if (_emitter.Cost > ModeWords.Budget)
            {
                throw new ModeCompileException(item.Line, string.Create(CultureInfo.InvariantCulture,
                    $"the mode takes {_emitter.Cost} operations a step at worst, past api {ModeWords.Api}'s {ModeWords.Budget} (this rule takes {_emitter.Cost - before})"));
            }
        }

        /// <summary>A library macro's time (<c>every(T)</c>'s T): a time of at least one step, or a delay parameter that
        /// is never less.</summary>
        private void TimeArgument(ModeTimeArgument item)
        {
            var time = item.Value;
            if (time is ModeLiteral { Time: true } literal && literal.Bits >= 1)
            {
                return;
            }
            if (time is ModeName name && _names.Find(name.Name) is { Kind: ModeNameKind.Param, Param.Kind: ModeParamKind.Delay } param
                && param.Param.Least >= 1)
            {
                return;
            }
            throw new ModeCompileException(time.Line, $"{item.Macro} takes a time of at least one step ({item.Macro}(10s)) or a delay parameter");
        }

        /// <summary>The library's subs and the file's, declared before any item (a sub may be called from anywhere in
        /// the file); a sub that calls itself, directly or through others, is refused.</summary>
        private void Subs(List<ModeSubItem> subs)
        {
            foreach (var sub in library)
            {
                _names.Declare(sub.Line, sub.Name, new ModeDeclared(ModeNameKind.Sub, sub.Returns ?? ModeType.Int, -1, Sub: sub));
            }
            if (subs.Count > ModeWords.Subs)
            {
                throw new ModeCompileException(subs[ModeWords.Subs].Line, $"a mode has at most {ModeWords.Subs} subs");
            }
            foreach (var sub in subs)
            {
                _names.Declare(sub.Line, sub.Name, new ModeDeclared(ModeNameKind.Sub, sub.Returns ?? ModeType.Int, 0, Sub: sub));
            }
            foreach (var sub in subs)
            {
                ModeSubCalls.RefuseCycle(sub, name => _names.Find(name)?.Sub);
            }
        }
    }
}
