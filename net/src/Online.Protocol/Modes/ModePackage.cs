using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Online.Protocol;

/// <summary>A parameter's kind, as the lobby shows it (rules.toml's kinds; the package's <c>PARM</c>).</summary>
public enum ModeParamKind : byte
{
    Number = 0,
    /// <summary>A time, kept in steps.</summary>
    Delay = 1,
    /// <summary>On or off: 1 or 0.</summary>
    Switch = 2,
    /// <summary>One of named choices: its index.</summary>
    Choice = 3,
}

/// <summary>A parameter of a mode: the lobby's rule control. <see cref="Type"/> is <see cref="ModeType.Int"/> or
/// <see cref="ModeType.Real"/> as the package keeps it; the range, step and default are cells (a real's bits).
/// A real number with no step written has step 0.</summary>
public sealed record ModeParam(string Name, string Label, ModeParamKind Kind, ModeType Type, int Least, int Most,
    int Step, int Default, IReadOnlyList<string> Choices)
{
    /// <summary>Whether a value is within the range (a real's as a real).</summary>
    public bool Holds(int value) => Type == ModeType.Real
        ? BitConverter.Int32BitsToSingle(value) is var v && v >= BitConverter.Int32BitsToSingle(Least) && v <= BitConverter.Int32BitsToSingle(Most)
        : value >= Least && value <= Most;

    /// <summary>Whether a host may set this value: within the range and, for whole numbers, on a step from the least.
    /// (A real's step is the control's; the game's check is the range.)</summary>
    public bool Fits(int value) => Holds(value) && (Type == ModeType.Real || Step <= 1 || ((long)value - Least) % Step == 0);
}

/// <summary>A preset (the design's section 5): a name, and a value for each of the mode's parameters in the package's order,
/// the preset's own or the parameter's default. What a preset chooses travels as any host's values do, in
/// <c>setup.mode.params</c>: the race, the replay and the share code know no presets.</summary>
public sealed record ModePreset(string Name, IReadOnlyList<int> Values);

/// <summary>A setup word's argument: a constant cell, or the index of the parameter that gives it.</summary>
public sealed record ModeSetupValue(bool FromParam, int Value);

/// <summary>The title <c>results_title</c> carries in the package: a value a character, in the word's arguments. It is
/// drawn by the game's font 0x1D, whose table (main.rel .data 0x1AEA8 + 0x1D x 0x38) holds the glyphs 'A' to 'Z' only; the
/// drawing routine fn_1_4A0D8 steps over a space, and over anything else without drawing it, so only those two are
/// allowed. The game's window for it holds 24 bytes with the NUL. Centred on x 320, a letter takes 24 of the screen's
/// 640 and a space 12 (fn_1_1461C8 sets the space's factor 0.5), so the longest title is 552 wide and fits.</summary>
public static class ModeResultsTitle
{
    /// <summary>Why a title cannot be drawn; null: it can.</summary>
    public static string? Refusal(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Length > ModeWords.ResultsTitle)
        {
            return $"results_title is at most {ModeWords.ResultsTitle} characters: \"{title}\" has {title.Length}";
        }
        if (title.FirstOrDefault(c => c is not (>= 'A' and <= 'Z') and not ' ') is var bad and not '\0')
        {
            return $"results_title: the game's font 0x1D draws A to Z and the space, not '{bad}'";
        }
        return title.Trim(' ').Length != title.Length || title.Length == 0
            ? "results_title needs a title, with no space at either end: the game centres the whole string"
            : null;
    }

    /// <summary>A title as the word's arguments.</summary>
    public static List<ModeSetupValue> Cells(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return [.. title.Select(c => new ModeSetupValue(false, c))];
    }

    /// <summary>The word's arguments as its title.</summary>
    public static string Text(IEnumerable<ModeSetupValue> cells) => new([.. cells.Select(c => (char)c.Value)]);
}

/// <summary>The stats that live on the mode block's stats page: a rule that writes one makes the compiler's setup word
/// <c>stats_page</c>, so a mode without them has the block of before, byte for byte.</summary>
public static class ModeStatsPage
{
    public static bool Uses(ModeAction action) => action is >= ModeAction.SeatStatDriftRotationResistance and <= ModeAction.SeatStatGripWindow;
}

/// <summary>The texts the <c>tag</c> word draws, which the package carries in the setup word <c>tag_texts</c>: a value a
/// character, a 0 between texts; a <c>tag</c> names one by its place, from 1. They are drawn by the game's font 5 as the
/// name tags are (the characters tested in piconet-docs archive/research/spike-names.md): A to Z, 0 to 9, the space,
/// <c>.</c> and <c>-</c>; it draws a small letter as a capital, a comma as an ampersand, the rest as blanks, so those are
/// refused. At the tags' largest a character takes about 22 of the screen's 640: the longest text is about 220 wide.</summary>
public static class ModeTagTexts
{
    /// <summary>Why a text cannot be drawn; null: it can.</summary>
    public static string? Refusal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > ModeWords.TagText)
        {
            return $"tag: a text is at most {ModeWords.TagText} characters: \"{text}\" has {text.Length}";
        }
        if (text.FirstOrDefault(c => c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or ' ' or '.' or '-')) is var bad and not '\0')
        {
            return $"tag: the game's font 5 draws A to Z, 0 to 9, the space, . and -, not '{bad}'";
        }
        return text.Trim(' ').Length != text.Length || text.Length == 0
            ? "tag needs a text, with no space at either end: the tag is centred over the machine"
            : null;
    }

    /// <summary>The texts as the word's arguments.</summary>
    public static List<ModeSetupValue> Cells(IEnumerable<string> texts) =>
        [.. string.Join('\0', texts).Select(c => new ModeSetupValue(false, c))];

    /// <summary>The word's arguments as its texts.</summary>
    public static string[] Texts(IEnumerable<ModeSetupValue> cells) =>
        new string([.. cells.Select(c => (char)c.Value)]).Split('\0');
}

/// <summary>A setup word as the package keeps it: every argument of the word, in the table's order, the ones the file
/// left out at their defaults (so a reader needs no table of defaults).</summary>
public sealed record ModeSetupWord(ModeSetup Id, IReadOnlyList<ModeSetupValue> Arguments);

/// <summary>A setup word of the coordinator's (modes.toml's <c>lobby</c>), as the compiler read it from the file: its
/// values in order (a range's ends, a course's or the machines' numbers), a stat pack's name. It is not in the
/// package's <c>SETP</c>: the game sees what it sets as the match start's own fields. The content hash holds it all
/// the same, through the file's text in <c>SRC </c>.</summary>
public sealed record ModeLobbyWord(ModeSetup Id, IReadOnlyList<ModeSetupValue> Values, string? Text = null);

/// <summary>A rule as the package keeps it: its conditions' and body's offsets in the code (<see cref="None"/>:
/// none), and its locals, the event's binding (local 0) included.</summary>
public sealed record ModeRule(string Label, ModeEvent Event, int Locals, uint When, uint While, uint Body)
{
    public const uint None = 0xFFFFFFFF;
}

/// <summary>A <c>board</c> line as the package keeps it: its variable's scope, type and word, and its label.</summary>
public sealed record ModeBoardLine(ModeScope Scope, ModeType Type, int Word, string Label)
{
    /// <summary>The words the line names: its variable's whole number picks one. None: it shows the number.</summary>
    public IReadOnlyList<string> Words { get; init; } = [];

    public bool Equals(ModeBoardLine? other) => other is not null && (Scope, Type, Word, Label) == (other.Scope, other.Type, other.Word, other.Label) && Words.SequenceEqual(other.Words);

    public override int GetHashCode() => HashCode.Combine(Scope, Type, Word, Label, Words.Count);
}

/// <summary>The match start's <c>setup.mode</c> (formats.md section 9): the package, its hash, its api, the
/// parameters' values in <c>PARM</c>'s order (a real's bits) and each seat's team, by seat (0 to 29; null: every seat
/// its own, its number).</summary>
public sealed record ModeStart(string Package, string Hash, int Api, IReadOnlyList<int> Params, IReadOnlyList<int>? Teams = null)
{
    public string ToJson() => JsonSerializer.Serialize(this, ControlProtocol.Json);
}

/// <summary>A mode package, <c>.fzmp</c> (formats.md section 9): what the compiler writes, what a match start carries
/// and what every game runs. <see cref="Read"/> checks one as the service takes an upload: every section in its place,
/// every count within the api's limits, every word known, and the code a run of whole operations whose jumps land
/// on operations.</summary>
public sealed class ModePackage
{
    public const int Format = 1;
    private static readonly string[] Sections = ["HEAD", "PARM", "SETP", "VARS", "RULE", "CODE", "SRC "];

    public required int Api { get; init; }
    public required string Name { get; init; }
    public string Badge { get; init; } = "";
    public string About { get; init; } = "";
    public IReadOnlyList<ModeParam> Params { get; init; } = [];
    public IReadOnlyList<ModeSetupWord> Setups { get; init; } = [];
    /// <summary>The words of the coordinator's, from the compile (not in the bytes: <see cref="Read"/> gives none;
    /// everyone that uses them compiles the file).</summary>
    public IReadOnlyList<ModeLobbyWord> LobbyWords { get; init; } = [];
    /// <summary>The file's presets, in its order, from the compile (not in the bytes, as <see cref="LobbyWords"/>: the host's
    /// launcher compiles the file, and <see cref="Read"/> gives none).</summary>
    public IReadOnlyList<ModePreset> Presets { get; init; } = [];
    /// <summary>Each global's start value.</summary>
    public IReadOnlyList<int> Globals { get; init; } = [];
    /// <summary>Each seat word's start value.</summary>
    public IReadOnlyList<int> SeatWords { get; init; } = [];
    /// <summary>Each team word's start value.</summary>
    public IReadOnlyList<int> TeamWords { get; init; } = [];
    public IReadOnlyList<ModeRule> Rules { get; init; } = [];
    /// <summary>The file's <c>board</c> lines, in its order (the language's section 10): the status board's values.</summary>
    public IReadOnlyList<ModeBoardLine> Board { get; init; } = [];
    public required byte[] Code { get; init; }
    public required string Source { get; init; }

    /// <summary>The package's bytes, big-endian, every section in order.</summary>
    public byte[] Write()
    {
        var w = new ModeBytes();
        w.Bytes("FZMP"u8);
        w.U16(Format);
        w.U16(Api);
        w.Section("HEAD", s =>
        {
            s.Text(Name);
            s.Text(Badge);
            s.Text(About);
        });
        w.Section("PARM", s =>
        {
            s.U16(Params.Count);
            foreach (var p in Params)
            {
                s.Text(p.Name);
                s.Text(p.Label);
                s.U8((int)p.Kind);
                s.U8(p.Type == ModeType.Real ? 1 : 0);
                s.I32(p.Least);
                s.I32(p.Most);
                s.I32(p.Step);
                s.I32(p.Default);
                s.U16(p.Choices.Count);
                foreach (var choice in p.Choices)
                {
                    s.Text(choice);
                }
            }
        });
        w.Section("SETP", s =>
        {
            s.U16(Setups.Count);
            foreach (var word in Setups)
            {
                s.U8((int)word.Id);
                s.U8(word.Arguments.Count);
                foreach (var argument in word.Arguments)
                {
                    s.U8(argument.FromParam ? 1 : 0);
                    s.I32(argument.Value);
                }
            }
        });
        w.Section("VARS", s =>
        {
            s.U16(Globals.Count);
            s.U16(SeatWords.Count);
            foreach (var value in Globals.Concat(SeatWords))
            {
                s.I32(value);
            }
            // a mode with words a team: their count and start values after the seats' (none: nothing, as before teams)
            if (TeamWords.Count > 0)
            {
                s.U16(TeamWords.Count);
                foreach (var value in TeamWords)
                {
                    s.I32(value);
                }
            }
        });
        w.Section("RULE", s =>
        {
            s.U16(Rules.Count);
            foreach (var rule in Rules)
            {
                s.Text(rule.Label);
                s.U8((int)rule.Event);
                s.U8(0);
                s.U16(rule.Locals);
                s.U32(rule.When);
                s.U32(rule.While);
                s.U32(rule.Body);
            }
        });
        w.Section("CODE", s => s.Bytes(Code));
        w.Section("SRC ", s => s.Bytes(Encoding.UTF8.GetBytes(Source)));
        // only in a mode with board lines, so a mode without keeps the bytes and the hash it had before the board
        if (Board.Count > 0)
        {
            w.Section("BORD", s =>
            {
                s.U16(Board.Count);
                foreach (var line in Board)
                {
                    s.U8((int)line.Scope);
                    s.U8((int)line.Type);
                    s.U16(line.Word);
                    s.Text(line.Label);
                    s.U8(line.Words.Count);
                    foreach (var word in line.Words)
                    {
                        s.Text(word);
                    }
                }
            });
        }
        return w.ToArray();
    }

    /// <summary>The content hash of a package's bytes: SHA-256, lower-case hex.</summary>
    public static string Hash(ReadOnlySpan<byte> package) => Convert.ToHexStringLower(SHA256.HashData(package));

    /// <summary>The share code of a content hash: the first 8 characters of its bytes' base32 (RFC 4648, upper case,
    /// no padding), the first 40 bits.</summary>
    public static string ShareCode(string hash)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = Convert.FromHexString(hash.AsSpan(0, 10));
        var bits = 0L;
        foreach (var b in bytes)
        {
            bits = (bits << 8) | b;
        }
        var code = new char[8];
        for (var i = 0; i < 8; i++)
        {
            code[i] = Alphabet[(int)((bits >> (35 - (5 * i))) & 31)];
        }
        return new string(code);
    }

    /// <summary>The match start's <c>setup.mode</c>, with the parameters given (in <c>PARM</c>'s order; null or left
    /// out: their defaults). A value outside its range is refused. The package is <see cref="Write"/>'s bytes, which
    /// are the bytes it was read from (a package reads only as its writer writes it).</summary>
    /// <summary>The race's laps on <paramref name="course"/> (the game's course number) as the mode sets them: its
    /// <c>laps by course</c> row (or its <c>else</c>), or its <c>laps</c>; 0 is the game's free run (<c>unlimited</c>).
    /// Null: the mode leaves the laps to the lobby's rules. A value a parameter gives is read from
    /// <paramref name="values"/> (in <c>PARM</c>'s order; left out, the parameters' defaults).</summary>
    public int? LapsFor(int course, IReadOnlyList<int>? values = null)
    {
        int Value(ModeSetupValue v) =>
            !v.FromParam ? v.Value : values is not null && v.Value < values.Count ? values[v.Value] : Params[v.Value].Default;
        foreach (var word in Setups)
        {
            if (word.Id == ModeSetup.Laps)
            {
                return Value(word.Arguments[0]);
            }
            if (word.Id == ModeSetup.LapsByCourse)
            {
                for (var i = 1; i + 1 < word.Arguments.Count; i += 2)
                {
                    if (word.Arguments[i].Value == course)
                    {
                        return word.Arguments[i + 1].Value;
                    }
                }
                return Value(word.Arguments[0]);
            }
        }
        return null;
    }

    /// <summary>Why a host's parameter values are not this mode's, in a line for them; null: they are. One value a
    /// parameter in <c>PARM</c>'s order, each within its declared range and on its step.</summary>
    public string? ValuesRefusal(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != Params.Count)
        {
            return $"the mode has {Params.Count} settings, not {values.Count}";
        }
        for (var i = 0; i < values.Count; i++)
        {
            if (!Params[i].Fits(values[i]))
            {
                return $"{Params[i].Label}: that value is outside what the mode allows";
            }
        }
        return null;
    }

    public ModeStart Start(IReadOnlyList<int>? values = null, IReadOnlyList<int>? teams = null)
    {
        if (teams is not null && (teams.Count > ModeWords.Seats || teams.Any(t => t is < 0 or >= ModeWords.Teams)))
        {
            throw new ArgumentException($"a team is 0 to {ModeWords.Teams - 1}, at most one a seat", nameof(teams));
        }
        var package = Write();
        if (values is not null && values.Count > Params.Count)
        {
            throw new ArgumentException($"the mode has {Params.Count} parameters, not {values.Count}", nameof(values));
        }
        var chosen = Params.Select((p, i) => values is not null && i < values.Count ? values[i] : p.Default).ToList();
        for (var i = 0; i < chosen.Count; i++)
        {
            if (!Params[i].Holds(chosen[i]))
            {
                throw new ArgumentException($"the parameter {Params[i].Name} is outside its range", nameof(values));
            }
        }
        return new ModeStart(Convert.ToBase64String(package), Hash(package), Api, chosen, teams);
    }

    /// <summary>Reads and checks a package; a package that does not read throws <see cref="InvalidDataException"/>
    /// with what is wrong.</summary>
    public static ModePackage Read(byte[] package)
    {
        if (package.Length > ModeWords.PackageBytes)
        {
            throw new InvalidDataException($"a package is at most {ModeWords.PackageBytes} bytes, not {package.Length}");
        }
        var r = new ModeReader(package);
        if (!r.Take(4).SequenceEqual("FZMP"u8))
        {
            throw new InvalidDataException("not a mode package: no FZMP mark");
        }
        var format = r.U16();
        var api = r.U16();
        if (format != Format)
        {
            throw new InvalidDataException($"the package's format is {format}, not {Format}");
        }
        if (api > ModeWords.Api)
        {
            throw new InvalidDataException($"the package is of api {api}, newer than this build's {ModeWords.Api}");
        }
        var sections = Sections.Select(tag => r.Section(tag)).ToArray();
        var board = r.Left > 0 ? ReadBoard(r.Section("BORD")) : [];
        r.End();
        var head = sections[0];
        var (name, badge, about) = (head.Text(), head.Text(), head.Text());
        head.End();
        var parameters = ReadParams(sections[1]);
        var code = sections[5].Take(sections[5].Left).ToArray();
        var result = new ModePackage
        {
            Api = api,
            Name = name,
            Badge = badge,
            About = about,
            Params = parameters,
            Setups = ReadSetups(sections[2], parameters.Count),
            Globals = ReadVars(sections[3], out var seatWords, out var teamWords),
            SeatWords = seatWords,
            TeamWords = teamWords,
            Rules = ReadRules(sections[4], code.Length),
            Board = board,
            Code = code,
            Source = sections[6].Utf8(sections[6].Left),
        };
        ModeCode.Check(result);
        return result;
    }

    private static List<ModeBoardLine> ReadBoard(ModeReader s)
    {
        var count = s.U16();
        if (count == 0)
        {
            throw new InvalidDataException("a BORD section holds a line");
        }
        var list = new List<ModeBoardLine>();
        for (var i = 0; i < count; i++)
        {
            var (scope, type, word, label) = (s.U8(), s.U8(), s.U16(), s.Text());
            var words = new List<string>();
            for (var left = s.U8(); left > 0; left--)
            {
                words.Add(s.Text());
            }
            if (words.Count > ModeWords.BoardWords || type == (int)ModeType.Real && words.Count > 0 || words.Any(w => Encoding.UTF8.GetByteCount(w) > ModeWords.BoardWord))
            {
                throw new InvalidDataException($"the board's line \"{label}\" names words not of api {ModeWords.Api}");
            }
            if (scope > (int)ModeScope.Team || type >= (int)ModeType.Vec || Encoding.UTF8.GetByteCount(label) is 0 or > ModeWords.BoardLabel)
            {
                throw new InvalidDataException($"the board's line \"{label}\" is not one of api {ModeWords.Api}");
            }
            list.Add(new ModeBoardLine((ModeScope)scope, (ModeType)type, word, label) { Words = words });
        }
        s.End();
        if (list.GroupBy(l => l.Scope).Any(g => g.Count() > ModeWords.Board))
        {
            throw new InvalidDataException($"the board holds at most {ModeWords.Board} values a scope");
        }
        return list;
    }

    private static List<ModeParam> ReadParams(ModeReader s)
    {
        var count = s.U16();
        var list = new List<ModeParam>();
        for (var i = 0; i < count; i++)
        {
            var (name, label, kind, type) = (s.Text(), s.Text(), s.U8(), s.U8());
            var (least, most, step, value) = (s.I32(), s.I32(), s.I32(), s.I32());
            var choices = Enumerable.Range(0, s.U16()).Select(_ => s.Text()).ToList();
            if (kind > (int)ModeParamKind.Choice || type > 1)
            {
                throw new InvalidDataException($"the parameter {name}'s kind or type is unknown");
            }
            var param = new ModeParam(name, label, (ModeParamKind)kind, type == 1 ? ModeType.Real : ModeType.Int,
                least, most, step, value, choices);
            if (!param.Holds(value) || (param.Kind == ModeParamKind.Choice && most != choices.Count - 1))
            {
                throw new InvalidDataException($"the parameter {name}'s default is outside its range");
            }
            list.Add(param);
        }
        s.End();
        return list;
    }

    private static List<ModeSetupWord> ReadSetups(ModeReader s, int parameters)
    {
        var count = s.U16();
        var list = new List<ModeSetupWord>();
        for (var i = 0; i < count; i++)
        {
            var id = s.U8();
            var word = ModeWords.Setups.FirstOrDefault(w => (int)w.Id == id);
            if (word.Name is null)
            {
                throw new InvalidDataException($"setup word {id} is not of api {ModeWords.Api}");
            }
            var arguments = Enumerable.Range(0, s.U8()).Select(_ => new ModeSetupValue(s.U8() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException($"the setup word {word.Name}'s argument has an unknown source"),
            }, s.I32())).ToList();
            if (word.Id is ModeSetup.ResultsTitle or ModeSetup.TagTexts or ModeSetup.PointsTable)
            {
                if (!OwnValues(word.Id, arguments))
                {
                    throw new InvalidDataException($"the setup word {word.Name}'s arguments are not its own");
                }
                list.Add(new ModeSetupWord(word.Id, arguments));
                continue;
            }
            var table = word.Id == ModeSetup.LapsByCourse;  // (modes.toml's `table`: a pair a course after the arguments)
            if ((table ? arguments.Count < word.Args.Count || (arguments.Count - word.Args.Count) % 2 != 0
                       : arguments.Count != word.Args.Count) ||
                arguments.Any(a => a.FromParam && (a.Value < 0 || a.Value >= parameters)) ||
                (table && arguments.Skip(word.Args.Count).Any(a => a.FromParam)))
            {
                throw new InvalidDataException($"the setup word {word.Name}'s arguments are not its own");
            }
            list.Add(new ModeSetupWord(word.Id, arguments));
        }
        s.End();
        return list;
    }

    /// <summary>Whether a word of many values holds what the compiler would write for it.</summary>
    private static bool OwnValues(ModeSetup id, List<ModeSetupValue> arguments)
    {
        if (id == ModeSetup.PointsTable)
        {
            return arguments.Count is > 0 and <= ModeWords.Seats && !arguments.Any(a => a.FromParam || a.Value is < 0 or > 999);
        }
        if (arguments.Any(a => a.FromParam || a.Value is < 0 or > 127))
        {
            return false;
        }
        if (id == ModeSetup.ResultsTitle)
        {
            return ModeResultsTitle.Refusal(ModeResultsTitle.Text(arguments)) is null;
        }
        var texts = ModeTagTexts.Texts(arguments);
        return texts.Length <= ModeWords.TagTexts && texts.Distinct().Count() == texts.Length && !texts.Any(t => ModeTagTexts.Refusal(t) is not null);
    }

    private static List<int> ReadVars(ModeReader s, out List<int> seatWordValues, out List<int> teamWordValues)
    {
        var (globals, seatWords) = (s.U16(), s.U16());
        if (globals > ModeWords.Globals || seatWords > ModeWords.SeatWords)
        {
            throw new InvalidDataException($"at most {ModeWords.Globals} globals and {ModeWords.SeatWords} words a seat");
        }
        var g = Enumerable.Range(0, globals).Select(_ => s.I32()).ToList();
        seatWordValues = Enumerable.Range(0, seatWords).Select(_ => s.I32()).ToList();
        teamWordValues = [];
        if (s.Left > 0)
        {
            var teamWords = s.U16();
            if (teamWords == 0 || teamWords > ModeWords.TeamWords)
            {
                throw new InvalidDataException($"1 to {ModeWords.TeamWords} words a team, when a mode keeps any");
            }
            teamWordValues = Enumerable.Range(0, teamWords).Select(_ => s.I32()).ToList();
        }
        s.End();
        return g;
    }

    private static List<ModeRule> ReadRules(ModeReader s, int code)
    {
        var count = s.U16();
        if (count > ModeWords.Rules)
        {
            throw new InvalidDataException($"at most {ModeWords.Rules} rules, not {count}");
        }
        var list = new List<ModeRule>();
        for (var i = 0; i < count; i++)
        {
            var (label, id, zero, locals) = (s.Text(), s.U8(), s.U8(), s.U16());
            var rule = new ModeRule(label, (ModeEvent)id, locals, s.U32(), s.U32(), s.U32());
            if (!ModeWords.Events.Any(e => (int)e.Id == id) || zero != 0 || locals > ModeWords.Locals
                || (rule.When != ModeRule.None && rule.When >= code) || (rule.While != ModeRule.None && rule.While >= code)
                || rule.Body >= code)
            {
                throw new InvalidDataException($"the rule \"{label}\" is not a rule of api {ModeWords.Api}");
            }
            list.Add(rule);
        }
        s.End();
        return list;
    }
}

/// <summary>Bytes written big-endian, as a package's are.</summary>
internal sealed class ModeBytes
{
    private readonly List<byte> _bytes = [];

    public int Count => _bytes.Count;

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            _bytes.Add(b);
        }
    }

    public void U8(int value) => _bytes.Add(checked((byte)value));

    public void U16(int value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, checked((ushort)value));
        Bytes(b);
    }

    public void I32(int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        Bytes(b);
    }

    public void U32(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        Bytes(b);
    }

    /// <summary>Drops the bytes after the first <paramref name="count"/>.</summary>
    public void Truncate(int count) => _bytes.RemoveRange(count, _bytes.Count - count);

    /// <summary>Writes an earlier u32 again (a jump's target, once it is known).</summary>
    public void Patch(int at, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        for (var i = 0; i < 4; i++)
        {
            _bytes[at + i] = b[i];
        }
    }

    public void Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        U16(bytes.Length);
        Bytes(bytes);
    }

    public void Section(string tag, Action<ModeBytes> write)
    {
        var content = new ModeBytes();
        write(content);
        Bytes(Encoding.ASCII.GetBytes(tag));
        U32((uint)content.Count);
        Bytes(content.ToArray());
    }

    public byte[] ToArray() => [.. _bytes];
}

/// <summary>Reads a package's bytes big-endian; a read past the end is an <see cref="InvalidDataException"/>.</summary>
internal sealed class ModeReader(byte[] bytes, int start = 0, int? end = null)
{
    private readonly int _end = end ?? bytes.Length;
    private int _at = start;

    public int Left => _end - _at;

    public ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Left)
        {
            throw new InvalidDataException("the package ends too soon");
        }
        var span = bytes.AsSpan(_at, count);
        _at += count;
        return span;
    }

    public int U8() => Take(1)[0];

    public int U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

    public int I32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

    public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));

    public string Utf8(int count)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(Take(count));
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("a string of the package is not UTF-8");
        }
    }

    public string Text() => Utf8(U16());

    /// <summary>The next section, which must be the one tagged so: a reader of its bytes alone.</summary>
    public ModeReader Section(string tag)
    {
        var found = Encoding.ASCII.GetString(Take(4));
        if (found != tag)
        {
            throw new InvalidDataException($"the section {tag.Trim()} is expected, not {found.Trim()}");
        }
        var length = U32();
        if (length > Left)
        {
            throw new InvalidDataException($"the section {tag.Trim()} runs past the package's end");
        }
        var section = new ModeReader(bytes, _at, _at + (int)length);
        _at += (int)length;
        return section;
    }

    public void End()
    {
        if (Left != 0)
        {
            throw new InvalidDataException($"{Left} bytes more than the package's layout");
        }
    }
}
