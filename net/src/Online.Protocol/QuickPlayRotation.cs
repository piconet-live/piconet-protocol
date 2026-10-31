using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace Online.Protocol;

/// <summary>A mode of Quick Play's rotation, compiled (<see cref="ModeCompiler"/>): its file's name, its package and
/// the package's content hash. Values: the parameters as a custom lobby's host set them, in <c>PARM</c>'s order, checked
/// (<see cref="ModePackage.ValuesRefusal"/>); null: their defaults, as Quick Play races every mode.</summary>
public sealed record RotationMode(string File, ModePackage Package, string Hash, IReadOnlyList<int>? Values = null)
{
    /// <summary>What the mode states of the race, on its parameters' values.</summary>
    public ModeTerms Terms { get; } = ModeTerms.Of(Package, Values);

    /// <summary>The race's laps on a course as the mode states them (<c>laps</c>, or its <c>laps by course</c>); null:
    /// it states none, so the lobby's laps. 0: unlimited.</summary>
    public int? Laps(int course) => Terms.Laps(course);

    /// <summary>The mode says <c>cpus on</c>: players may vote CPUs into its race (left out, they may not).</summary>
    public bool CpusAllowed => Terms.CpusAllowed;

    /// <summary>The match start's <c>setup.mode</c>: the package on its parameters' values.</summary>
    public ModeStart Start() => Package.Start(Values);

    /// <summary>The vote card of this mode on a course (<see cref="CourseVote.RandomCourse"/>: laps not known); a spicy
    /// mode's carries its badge, its line and the rules it changes.</summary>
    /// <param name="course">The card's course.</param>
    /// <param name="spicy">A spicy option's: its badge, its line and its rules.</param>
    /// <param name="lobbyLaps">The lobby's laps, for a mode that states none.</param>
    public VoteCard Card(int course, bool spicy, int lobbyLaps = 3) => new(Package.Name,
        course == CourseVote.RandomCourse ? null : Laps(course) ?? lobbyLaps,
        spicy && Package.Badge.Length > 0 ? Package.Badge : null,
        spicy && Package.About.Length > 0 ? Package.About : null,
        spicy && ModeRuleLines.Of(Package) is { Count: > 0 } rules ? rules : null);
}

/// <summary>A spicy option of the rotation: its mode, the courses it is raced on (null: the pool) and its weight in
/// the draw among the spicy options.</summary>
public sealed record RotationSpicy(RotationMode Mode, IReadOnlyList<int>? Courses, int Weight);

/// <summary>One option of Quick Play's vote: a course (<see cref="CourseVote.RandomCourse"/> for "Random course") with
/// the mode it is raced in; Spicy: one of the rotation's spicy options.</summary>
public sealed record QuickPlayOption(int Course, RotationMode Mode, bool Spicy)
{
    public VoteCard Card => Mode.Card(Course, Spicy);
}

/// <summary>
/// Quick Play's rotation (piconet-docs research/game-modes/ design 6): the coordinator's list of mode files, its
/// <c>quickplay.toml</c>, and those files compiled. A vote card is a (course, mode) pair: the vote shows
/// <see cref="Cards"/> course cards, exactly <see cref="SpicyCards"/> of them spicy, and "Random course", which lands
/// on a spicy option with the chance <see cref="RandomSpicyChance"/>. Quick Play never has leaderboards: a file that
/// asks for them is refused. The service reads it at its start, and the launcher's stand-in service the same files,
/// so a test races what the service races.
/// </summary>
public sealed class QuickPlayRotation
{
    /// <summary>The rotation's file, in its folder beside the mode files it names.</summary>
    public const string FileName = "quickplay.toml";

    /// <summary>The steps of a chance's draw.</summary>
    private const int ChanceSteps = 1_000_000;

    private static readonly string[] TopKeys = ["cards", "spicy_cards", "random_spicy_chance", "leaderboards", "show_laps", "normal", "spicy"];

    private QuickPlayRotation(int cards, int spicyCards, double randomSpicyChance, bool showLaps, RotationMode normal,
        IReadOnlyList<int>? normalCourses, IReadOnlyList<RotationSpicy> spicy)
    {
        Cards = cards;
        SpicyCards = spicyCards;
        RandomSpicyChance = randomSpicyChance;
        ShowLaps = showLaps;
        Normal = normal;
        NormalCourses = normalCourses;
        Spicy = spicy;
    }

    /// <summary>The course cards a vote shows, besides "Random course".</summary>
    public int Cards { get; }

    /// <summary>The spicy cards among them (when the pool holds a spicy option's course).</summary>
    public int SpicyCards { get; }

    /// <summary>The chance that "Random course" lands on a spicy option, 0 to 1.</summary>
    public double RandomSpicyChance { get; }

    /// <summary>Whether the cards show the lap count.</summary>
    public bool ShowLaps { get; }

    /// <summary>The normal option: the mode every other card races.</summary>
    public RotationMode Normal { get; }

    /// <summary>The courses of the normal option (null: the pool).</summary>
    public IReadOnlyList<int>? NormalCourses { get; }

    /// <summary>The spicy options, in the file's order.</summary>
    public IReadOnlyList<RotationSpicy> Spicy { get; }

    /// <summary>Every mode of the rotation, the normal one first.</summary>
    public IEnumerable<RotationMode> Modes => Spicy.Select(s => s.Mode).Prepend(Normal);

    /// <summary>The newest api the rotation's modes need: a launcher that runs it can race every card
    /// (<see cref="Hello.Modes"/>).</summary>
    public int Api => Modes.Max(m => m.Package.Api);

    /// <summary>Whether any mode of the rotation lets players vote CPUs in (otherwise there is no CPU vote).</summary>
    public bool CpusAllowed => Modes.Any(m => m.CpusAllowed);

    /// <summary>The rotation in a folder: its <see cref="FileName"/> and the mode files it names, compiled. Anything
    /// wrong (a file missing, a key unknown, a value out of range, a mode that does not compile) throws
    /// <see cref="InvalidDataException"/> with the file and what is wrong, a compile error with its line.</summary>
    public static QuickPlayRotation Read(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{path}: no such file");
        }
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path))!;
        }
        catch (Exception error) when (error is not InvalidDataException)
        {
            throw new InvalidDataException($"{FileName}: not TOML: {error.Message}", error);
        }
        if (table.Keys.FirstOrDefault(k => !TopKeys.Contains(k)) is { } unknown)
        {
            throw new InvalidDataException($"{FileName}: '{unknown}' is not a key of the rotation: {string.Join(", ", TopKeys)}");
        }
        var cards = Whole(table, "cards", 5, 1, CoursePool.Default.Count);
        var spicyCards = Whole(table, "spicy_cards", 1, 0, cards);
        var chance = table.TryGetValue("random_spicy_chance", out var c) ? Number(c, "random_spicy_chance") : 0;
        if (chance is < 0 or > 1)
        {
            throw new InvalidDataException($"{FileName}: random_spicy_chance is a chance from 0 to 1, not {chance.ToString(CultureInfo.InvariantCulture)}");
        }
        if (table.TryGetValue("leaderboards", out var boards) && boards is not false)
        {
            throw new InvalidDataException($"{FileName}: Quick Play never has leaderboards: leaderboards = false, or leave it out");
        }
        var showLaps = !table.TryGetValue("show_laps", out var laps) || (laps as bool? ?? throw new InvalidDataException($"{FileName}: show_laps is true or false"));
        var modes = new Dictionary<string, RotationMode>(StringComparer.Ordinal);
        if (!table.TryGetValue("normal", out var n) || n is not TomlTable normal)
        {
            throw new InvalidDataException($"{FileName}: no [normal] option: the mode every other card races");
        }
        var (normalMode, normalCourses, _) = Option(folder, normal, "[normal]", modes, weighted: false);
        var spicy = new List<RotationSpicy>();
        if (table.TryGetValue("spicy", out var s))
        {
            if (s is not TomlTableArray options)
            {
                throw new InvalidDataException($"{FileName}: spicy is a list of options, [[spicy]]");
            }
            foreach (var option in options)
            {
                var (mode, courses, weight) = Option(folder, option, $"[[spicy]] {spicy.Count + 1}", modes, weighted: true);
                spicy.Add(new RotationSpicy(mode, courses, weight));
            }
        }
        if (spicyCards > 0 && spicy.Count == 0)
        {
            throw new InvalidDataException($"{FileName}: spicy_cards is {spicyCards}, and there is no [[spicy]] option");
        }
        return new QuickPlayRotation(cards, spicyCards, chance, showLaps, normalMode, normalCourses, spicy);
    }

    /// <summary>A vote's options: <see cref="Cards"/> course cards from the pool, <see cref="SpicyCards"/> of them
    /// spicy (each a spicy option drawn by weight, on one of its courses the pool holds; a normal card when none is
    /// held), at places drawn among the cards, every course once; then "Random course". <paramref name="draw"/>(n)
    /// draws a whole number from 0 to n - 1.</summary>
    public IReadOnlyList<QuickPlayOption> Vote(IReadOnlyList<int> pool, Func<int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(draw);
        var used = new HashSet<int>();
        var spicy = new List<QuickPlayOption>();
        for (var i = 0; i < SpicyCards && SpicyOption(pool, draw, used) is { } option; i++)
        {
            spicy.Add(option);
            used.Add(option.Course);
        }
        var normal = Shuffled(Within(NormalCourses, pool).Where(x => !used.Contains(x)).ToList(), draw)
            .Take(Cards - spicy.Count).Select(x => new QuickPlayOption(x, Normal, false)).ToList();
        foreach (var option in spicy)
        {
            normal.Insert(draw(normal.Count + 1), option);
        }
        normal.Add(new QuickPlayOption(CourseVote.RandomCourse, Normal, false));
        return normal;
    }

    /// <summary>What "Random course" races: with the chance <see cref="RandomSpicyChance"/> a spicy option (by weight,
    /// on one of its courses the pool holds), otherwise the normal mode on a course of the pool.</summary>
    public QuickPlayOption Random(IReadOnlyList<int> pool, Func<int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(draw);
        if (Spicy.Count > 0 && draw(ChanceSteps) < RandomSpicyChance * ChanceSteps && SpicyOption(pool, draw, []) is { } spicy)
        {
            return spicy;
        }
        var courses = Within(NormalCourses, pool).ToList();
        return new QuickPlayOption(courses.Count == 0 ? pool[draw(pool.Count)] : courses[draw(courses.Count)], Normal, false);
    }

    private QuickPlayOption? SpicyOption(IReadOnlyList<int> pool, Func<int, int> draw, HashSet<int> used)
    {
        var open = Spicy.Select(s => (Option: s, Courses: Within(s.Courses, pool).Where(x => !used.Contains(x)).ToList()))
            .Where(s => s.Courses.Count > 0).ToList();
        if (open.Count == 0)
        {
            return null;
        }
        var ticket = draw(open.Sum(s => s.Option.Weight));
        var chosen = open.First(s => (ticket -= s.Option.Weight) < 0);
        return new QuickPlayOption(chosen.Courses[draw(chosen.Courses.Count)], chosen.Option.Mode, true);
    }

    private static IEnumerable<int> Within(IReadOnlyList<int>? courses, IReadOnlyList<int> pool) =>
        courses is null ? pool : courses.Where(pool.Contains);

    private static List<int> Shuffled(List<int> list, Func<int, int> draw)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = draw(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    private static (RotationMode Mode, IReadOnlyList<int>? Courses, int Weight) Option(string folder, TomlTable option,
        string what, Dictionary<string, RotationMode> modes, bool weighted)
    {
        string[] keys = weighted ? ["mode", "courses", "weight"] : ["mode", "courses"];
        if (option.Keys.FirstOrDefault(k => !keys.Contains(k)) is { } unknown)
        {
            throw new InvalidDataException($"{FileName} {what}: '{unknown}' is not a key of an option: {string.Join(", ", keys)}");
        }
        if (!option.TryGetValue("mode", out var m) || m is not string file || file.Length == 0)
        {
            throw new InvalidDataException($"{FileName} {what}: mode names its mode file, mode = \"competitive.fzmode\"");
        }
        if (!modes.TryGetValue(file, out var mode))
        {
            mode = Compiled(folder, file);
            modes[file] = mode;
        }
        IReadOnlyList<int>? courses = null;
        if (option.TryGetValue("courses", out var c) && c is not "pool")
        {
            if (c is not TomlArray list || list.Count == 0)
            {
                throw new InvalidDataException($"{FileName} {what}: courses is \"pool\" (the server's vote pool) or a list of course numbers");
            }
            courses = [.. list.Select(x => x is long number && CoursePool.Default.Contains((int)number) ? (int)number
                : throw new InvalidDataException($"{FileName} {what}: {x} is not a course of the pool ({string.Join(" ", CoursePool.Default)})"))];
        }
        var weight = weighted ? Whole(option, "weight", 1, 1, 1000, $" {what}") : 0;
        return (mode, courses, weight);
    }

    private static RotationMode Compiled(string folder, string file)
    {
        if (file != Path.GetFileName(file) || !file.EndsWith(".fzmode", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{FileName}: '{file}' is not a mode file beside it (a name ending .fzmode)");
        }
        var path = Path.Combine(folder, file);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{FileName}: its mode {file} is not there ({path})");
        }
        var compiled = ModeCompiler.Compile(File.ReadAllText(path));
        if (!compiled.Succeeded)
        {
            throw new InvalidDataException($"{file} {compiled.Errors[0]}");
        }
        // the rotation's cards name the courses, and a Quick Play lobby's seats are its own
        var terms = ModeTerms.Of(compiled.Mode!);
        if (terms.Course is not null || terms.Racers is not null)
        {
            throw new InvalidDataException($"{file}: a mode of Quick Play's rotation says neither course nor racers: the rotation's cards name the courses, and its lobby the seats");
        }
        if (terms.Solo)
        {
            throw new InvalidDataException($"{file}: a solo mode is raced alone, and no rotation holds it");
        }
        return new RotationMode(file, compiled.Mode!, compiled.Hash!);
    }

    private static int Whole(TomlTable table, string key, int absent, int least, int most, string what = "")
    {
        if (!table.TryGetValue(key, out var v))
        {
            return absent;
        }
        if (v is not long value || value < least || value > most)
        {
            throw new InvalidDataException($"{FileName}{what}: {key} is a whole number from {least} to {most}");
        }
        return (int)value;
    }

    private static double Number(object value, string key) => value switch
    {
        double d => d,
        long l => l,
        _ => throw new InvalidDataException($"{FileName}: {key} is a number"),
    };
}
