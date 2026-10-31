using System.Globalization;
using System.Text;

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
/// macros and subs (<see cref="ModeLibrary"/>, compiled with every file) and the board's lines. The vocabulary is
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
            // (each char is at least a byte of the package's source)
            if (source.Length > ModeWords.PackageBytes)
            {
                throw new ModeCompileException(1, $"the file is {source.Length} characters, past api {ModeWords.Api}'s {ModeWords.PackageBytes} bytes for a whole package");
            }
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
        private readonly List<ModeLobbyWord> _lobbyWords = [];
        private readonly List<int> _globals = [];
        private readonly List<int> _seatWords = [];
        private readonly List<int> _teamWords = [];
        private readonly List<ModeRule> _rules = [];
        private readonly List<ModeBoardLine> _board = [];
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
            if (items.OfType<ModeSetupItem>().FirstOrDefault(s => s.Word == "race_waits everyone") is { } waits
                && !_emitter.Used.ContainsKey(ModeAction.EndRace) && !_setups.Any(s => s.Id == ModeSetup.ClockLimit))
            {
                throw new ModeCompileException(waits.Line, "race_waits everyone waits for every machine: a mode with it needs end_race or clock_limit, or its race may never end");
            }
            if (items.OfType<ModeSetupItem>().FirstOrDefault(s => s.Word == "points_table") is { } table
                && !_emitter.Used.ContainsKey(ModeAction.EndRace) && !_setups.Any(s => s.Id == ModeSetup.ClockLimit))
            {
                throw new ModeCompileException(table.Line, "points_table pays when the mode ends the race: a mode with it needs end_race or clock_limit");
            }
            if (items.OfType<ModeSetupItem>().FirstOrDefault(s => s.Word == "time_bonus") is { } bonus
                && !_setups.Any(s => s.Id == ModeSetup.ClockLimit)
                && _setups.First(s => s.Id == ModeSetup.TimeBonus).Arguments[0] is not { FromParam: false, Value: 0 })
            {
                throw new ModeCompileException(bonus.Line, "time_bonus adds to the clock: a mode with it needs clock_limit");
            }
            if (items.OfType<ModeSetupItem>().FirstOrDefault(s => s.Word == "results points") is { } points
                && !_emitter.Used.ContainsKey(ModeAction.Award) && !_setups.Any(s => s.Id == ModeSetup.PointsTable))
            {
                throw new ModeCompileException(points.Line, "results points shows the points page: a mode with it needs award");
            }
            if (_emitter.Used.TryGetValue(ModeAction.ShowClock, out var clockLine) && _setups.Any(s => s.Id == ModeSetup.ClockLimit))
            {
                throw new ModeCompileException(clockLine, "show_clock and clock_limit both write the game's clock: a mode has one or the other");
            }
            SoloWords(items);
            StartTogetherWords(items);
            if (_emitter.TagTexts.Count > 0)
            {
                _setups.Add(new ModeSetupWord(ModeSetup.TagTexts, ModeTagTexts.Cells(_emitter.TagTexts)));
            }
            var mode = new ModePackage
            {
                Api = api,
                Name = header.Name,
                Badge = header.Badge,
                About = header.About,
                Params = _params,
                Setups = _setups,
                LobbyWords = _lobbyWords,
                Globals = _globals,
                SeatWords = _seatWords,
                TeamWords = _teamWords,
                Rules = _rules,
                Board = _board,
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
                case ModeBoardItem board:
                    Board(board);
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

        /// <summary>A board line: a variable already declared, of its scope, that one word holds; so many a scope.</summary>
        private void Board(ModeBoardItem item)
        {
            var (kind, word) = item.Scope switch
            {
                ModeScope.Seat => (ModeNameKind.SeatVariable, "seat"),
                ModeScope.Team => (ModeNameKind.TeamVariable, "team"),
                _ => (ModeNameKind.Global, "global"),
            };
            var found = _names.Find(item.Name);
            if (found is null || found.Kind != kind)
            {
                throw new ModeCompileException(item.Line, $"board {word} {item.Name}: {item.Name} is no {word} variable declared before this line");
            }
            if (found.Type == ModeType.Vec)
            {
                throw new ModeCompileException(item.Line, $"board {word} {item.Name}: a vec is three values, and a board line shows one");
            }
            if (_board.Any(b => b.Scope == item.Scope && b.Word == found.Index))
            {
                throw new ModeCompileException(item.Line, $"board {word} {item.Name}: it is on the board already");
            }
            if (_board.Count(b => b.Scope == item.Scope) >= ModeWords.Board)
            {
                throw new ModeCompileException(item.Line, $"the board holds at most {ModeWords.Board} {word} values");
            }
            if (Encoding.UTF8.GetByteCount(item.Label) is 0 or > ModeWords.BoardLabel)
            {
                throw new ModeCompileException(item.Line, $"a board line's label is 1 to {ModeWords.BoardLabel} bytes");
            }
            if (item.Words.Count > 0 && found.Type == ModeType.Real)
            {
                throw new ModeCompileException(item.Line, $"board {word} {item.Name}: a real is no whole number, and a line's words are picked by one");
            }
            if (item.Words.Count > ModeWords.BoardWords)
            {
                throw new ModeCompileException(item.Line, $"a board line names at most {ModeWords.BoardWords} words");
            }
            if (item.Words.Any(w => Encoding.UTF8.GetByteCount(w) > ModeWords.BoardWord))
            {
                throw new ModeCompileException(item.Line, $"a board line's word is at most {ModeWords.BoardWord} bytes");
            }
            _board.Add(new ModeBoardLine(item.Scope, found.Type, found.Index, item.Label) { Words = item.Words });
        }

        /// <summary>The words of a race raced alone need <c>solo</c>; and <c>solo</c> is one seat. A word stated
        /// <c>off</c> asks for nothing; a switch parameter may be on, so it counts as on.</summary>
        private void SoloWords(List<ModeItem> items)
        {
            int? Line(ModeSetup id) => items.OfType<ModeSetupItem>().FirstOrDefault(s => ModeWords.Setups.Single(w => w.Name == s.Word).Id == id)?.Line;
            int? On(ModeSetup id) => _setups.FirstOrDefault(s => s.Id == id)?.Arguments[0] is { FromParam: false, Value: 0 } ? null : Line(id);
            foreach (var (line, name) in new[] { (Line(ModeSetup.Rewind), "rewind"), (On(ModeSetup.Pause), "pause"), (On(ModeSetup.SaveState), "save_state"), (Line(ModeSetup.ResumeCountdown), "resume_countdown") })
            {
                if (line is { } at && On(ModeSetup.Solo) is null)
                {
                    throw new ModeCompileException(at, $"{name} is a word of a race raced alone: a mode with it says solo on");
                }
            }
            if (On(ModeSetup.SaveState) is { } saved && Line(ModeSetup.Rewind) is null)
            {
                throw new ModeCompileException(saved, "save_state keeps a moment inside the rewind's reach: a mode with it says rewind");
            }
            if (On(ModeSetup.Solo) is not null)
            {
                foreach (var (at, name) in new[] { (On(ModeSetup.Cpus), "cpus"), (Line(ModeSetup.Racers), "racers"), (Line(ModeSetup.Teams), "teams") })
                {
                    if (at is { } line)
                    {
                        throw new ModeCompileException(line, $"{name}: a solo mode is one seat");
                    }
                }
            }
        }

        /// <summary><c>start_together</c> puts every machine on one spot, which only works with contact off, as it
        /// keeps it from before the first step. A <c>contact</c> that can be on (stated on, or a switch parameter) is refused.</summary>
        private void StartTogetherWords(List<ModeItem> items)
        {
            if (_setups.All(s => s.Id != ModeSetup.StartTogether) || _setups.FirstOrDefault(s => s.Id == ModeSetup.Contact) is not { } contact
                || contact.Arguments[0] is { FromParam: false, Value: 0 })
            {
                return;
            }
            var word = ModeWords.Setups.Single(w => w.Id == ModeSetup.Contact).Name;
            throw new ModeCompileException(items.OfType<ModeSetupItem>().First(s => s.Word == word).Line, "contact on: start_together keeps every racer on one spot, so contact stays off");
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

        /// <summary>The Grand Prix's points for places 1 to 30, main.rel .rodata 0xDB38 (design D9).</summary>
        private static readonly int[] GamePoints =
        [
            100, 93, 87, 81, 76, 71, 66, 62, 58, 54, 50, 47, 44, 41, 38, 35, 33, 31, 29, 27, 25, 23, 22, 21, 20, 19, 18, 17, 16, 15,
        ];

        private List<ModeSetupValue> PointsTableValues(ModeSetupItem item, (string Name, ModeSetup Id, IReadOnlyList<ModeArgument> Args, bool Lobby, string Form) word)
        {
            if (item.Arguments.Count > ModeWords.Seats)
            {
                throw new ModeCompileException(item.Line, $"points_table has a place for each of {ModeWords.Seats} seats at most");
            }
            var values = item.Arguments.Select(a => SetupValue(item, word.Name, word.Args[0], a)).ToList();
            return values.Any(v => v.FromParam)
                ? throw new ModeCompileException(item.Line, "points_table takes numbers, not a parameter")
                : values;
        }

        private void Setup(ModeSetupItem item)
        {
            var word = ModeWords.Setups.Single(s => s.Name == item.Word);
            if (_setups.Any(s => s.Id == word.Id) || _lobbyWords.Any(s => s.Id == word.Id))
            {
                throw new ModeCompileException(item.Line, $"{word.Name} is set twice");
            }
            // two forms of one word (restore on, restore off; machines, machines all): the file says one
            var first = word.Name.Split(' ')[0];
            if (ModeWords.Setups.FirstOrDefault(s => s.Name != word.Name && s.Name.Split(' ')[0] == first &&
                (_setups.Any(w => w.Id == s.Id) || _lobbyWords.Any(w => w.Id == s.Id))) is { Name: not null } other)
            {
                throw new ModeCompileException(item.Line, $"{other.Name} and {word.Name}: one of them");
            }
            if (word.Id == ModeSetup.TagTexts)
            {
                throw new ModeCompileException(item.Line, "tag_texts is the compiler's own: a rule writes tag s, \"TEXT\"; and the text is kept");
            }
            if (word.Id == ModeSetup.PointsTable)
            {
                _setups.Add(new ModeSetupWord(word.Id, item.Arguments.Count == 0
                    ? GamePoints.Select(p => new ModeSetupValue(false, p)).ToList()
                    : PointsTableValues(item, word)));
                return;
            }
            if (word.Id == ModeSetup.ResultsTitle)
            {
                var title = item.Table![0];
                if (ModeResultsTitle.Refusal(title.Key) is { } refused)
                {
                    throw new ModeCompileException(title.Line, refused);
                }
                _setups.Add(new ModeSetupWord(word.Id, ModeResultsTitle.Cells(title.Key)));
                return;
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
            if (word.Lobby)
            {
                _lobbyWords.Add(LobbyWord(item, word.Id, word.Name, values));
                return;
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
                    new ModeSetupArgument(row.Line, "", row.Value!));
                values.Add(laps.FromParam
                    ? throw new ModeCompileException(row.Line, $"a row of {word.Name} is a number, not a parameter")
                    : laps);
            }
            _setups.Add(new ModeSetupWord(word.Id, values));
        }

        /// <summary>A word of the coordinator's: a range's ends in order, a course or machines by their numbers, a pack by
        /// its name.</summary>
        private static ModeLobbyWord LobbyWord(ModeSetupItem item, ModeSetup id, string name, List<ModeSetupValue> values)
        {
            var rows = item.Table ?? [];
            if (id == ModeSetup.Course)
            {
                var course = CourseTable.Courses.FirstOrDefault(c => $"{c.Place}: {c.Track}" == rows[0].Key).Course;
                if (course == 0 || !CoursePool.Default.Contains(course))
                {
                    throw new ModeCompileException(rows[0].Line, $"\"{rows[0].Key}\" is no course of the pool (\"Venue: Track\", as courses.toml names them)");
                }
                values.Add(new ModeSetupValue(false, course));
            }
            if (id == ModeSetup.Machines)
            {
                foreach (var row in rows)
                {
                    var machine = GameMachines.Names.ToList().IndexOf(row.Key);
                    if (machine < 0)
                    {
                        throw new ModeCompileException(row.Line, $"\"{row.Key}\" is no machine of the game's");
                    }
                    if (values.Any(v => v.Value == machine))
                    {
                        throw new ModeCompileException(row.Line, $"\"{row.Key}\" is in the list twice");
                    }
                    values.Add(new ModeSetupValue(false, machine));
                }
            }
            if (values.Count == 2 && !values[0].FromParam && !values[1].FromParam && values[0].Value > values[1].Value &&
                id is ModeSetup.Racers or ModeSetup.MachineSetting or ModeSetup.Teams)
            {
                throw new ModeCompileException(item.Line, $"{name}: its least is above its most");
            }
            var text = id == ModeSetup.Stats ? rows[0].Key : null;
            if (text is { Length: 0 or > 64 })
            {
                throw new ModeCompileException(rows[0].Line, "a stat pack's name is 1 to 64 characters");
            }
            return new ModeLobbyWord(id, values, text);
        }

        /// <summary>A setup word's argument: the parameter it names, the constant given, or the word's default.</summary>
        private ModeSetupValue SetupValue(ModeSetupItem item, string word, ModeArgument argument, ModeSetupArgument? given)
        {
            var what = argument.Name.Length > 0 ? $"{word}'s {argument.Name}" : word;
            if (given is null)
            {
                var value = argument.Default ?? throw new ModeCompileException(item.Line, argument.Type == ModeType.Bool
                    ? $"{what} takes on, off or a switch parameter" : $"{what} needs its value");
                return new ModeSetupValue(false, argument.Type == ModeType.Real ? ModeOperators.Cell((float)value) : (int)value);
            }
            if (argument.Type == ModeType.Bool || argument.On is not null)
            {
                if (given.Value is ModeLiteral { Text: "on" or "off" } state)
                {
                    return new ModeSetupValue(false, state.Bits == 0 ? 0 : argument.On ?? 1);
                }
                if (given.Value is ModeName { Name: var named } && _names.Find(named) is { Kind: ModeNameKind.Param, Type: ModeType.Bool } flag)
                {
                    return new ModeSetupValue(true, flag.Index);
                }
                if (argument.Type == ModeType.Bool)
                {
                    throw new ModeCompileException(given.Line, $"{what} takes on, off or a switch parameter");
                }
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
            if (item.Event == "go")
            {
                throw new ModeCompileException(item.Line, "'go' is called race_start now: write event race_start");
            }
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
