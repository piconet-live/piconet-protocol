using System.Globalization;

namespace Online.Protocol;

/// <summary>What a mode states of its race, read from its setup words on its parameters' values: the one place a mode's
/// word meets a lobby rule (the overhaul of piconet-docs research/game-modes/ design 5). The race's rules are the
/// mode's alone: a word left out is the game's own way (3 laps, CPU level Master, no boost on the first lap, mines stay
/// gone, each racer's own machine setting, the disc's own stats, one pack for all), the finish limit is off unless the
/// mode states it, and CPUs come only when it says <c>cpus on</c> (then by the vote). The service and the launcher's
/// stand-in both lay these over a lobby. Lobby settings (the seats, the ping limit, the vote's and choice's times, the
/// voting minimum, how the course is found among the mode's) are not a mode's.</summary>
public sealed record ModeTerms
{
    // what `mine_regen on` is, as a switch parameter gives it (1 in the package)
    private static readonly int MineRegenOn = ModeWords.Setups.First(s => s.Id == ModeSetup.MineRegen).Args[0].On!.Value;

    /// <summary>The mode says <c>solo on</c>: a race raced alone, which the launcher starts itself (Play's Practice). No
    /// lobby and no rotation races it.</summary>
    public bool Solo { get; init; }

    /// <summary>The mode says <c>cpus on</c>: players may vote CPUs into its race (it never forces them).</summary>
    public bool CpusAllowed { get; init; }

    /// <summary>The value of <see cref="LobbyRules.Values"/>' "laps" for a mode with a table of laps by course: no one
    /// number. No lobby's value takes it.</summary>
    public const int LapsByCourse = -1;

    private int? LapsAll { get; init; }

    private int? LapsElse { get; init; }

    private Dictionary<int, int> LapsTable { get; init; } = [];

    /// <summary>The CPUs' level; the mode's when stated.</summary>
    public int? CpuLevel { get; init; }

    /// <summary>Boosting on the first lap; the mode's when stated.</summary>
    public bool? BoostStart { get; init; }

    /// <summary>The race's end so long after the first finish, in seconds; 0 unless the mode states it.</summary>
    public int FinishLimit { get; init; }

    /// <summary>A mine's return in ms (0: stays gone); the mode's when stated.</summary>
    public int? MinesRespawn { get; init; }

    /// <summary>The pinned course; null: the lobby's way.</summary>
    public int? Course { get; init; }

    /// <summary>The courses it may race on, by number, in order: what its vote and Random draw from, and what a lobby's
    /// fixed course must be one of. Null: as the lobby's pool; empty: the whole pool (<c>courses all</c>).</summary>
    public IReadOnlyList<int>? Courses { get; init; }

    /// <summary>The racers the mode races with: below Least the race is refused, Most is the lobby's seats.</summary>
    public (int Least, int Most)? Racers { get; init; }

    /// <summary>The machines it allows, by id; null: as the lobby's; empty: all (<c>machines all</c>).</summary>
    public IReadOnlyList<int>? Machines { get; init; }

    /// <summary>No custom machines in its races (<c>customs off</c>). Left out, they are allowed, under a list of
    /// machines too: the list holds the stock machines.</summary>
    public bool CustomsOff { get; init; }

    /// <summary>How the mode sets each racer's machine (<c>machine</c>, <c>machine random</c>, <c>machine by
    /// seat</c>, ...: <see cref="ModeMachines.Of"/>); null: each racer's own choice.</summary>
    public ModeMachineRule? MachineRule { get; init; }

    /// <summary>The range a racer's own setting is brought into.</summary>
    public SettingRange? Setting { get; init; }

    /// <summary>The stat pack by name; "" the disc's own stats, which a mode that names none races (the game's own).
    /// Null only with <see cref="PackFromFile"/>: the host's own file.</summary>
    public string? Pack { get; init; } = "";

    /// <summary>Each racer may race a pack of their own; the mode's when stated.</summary>
    public bool? MixedPacks { get; init; }

    /// <summary>The teams a race has (<c>teams</c>); null: the lobby's rule.</summary>
    public (int Least, int Most)? Teams { get; init; }

    /// <summary>The laps on a course: the mode's, or the game's own 3 when it states none. 0: unlimited.</summary>
    public int? Laps(int course) => LapsAll ?? (LapsTable.TryGetValue(course, out var row) ? row : LapsElse) ?? GameLaps;

    /// <summary>The game's own laps (a Grand Prix race's), where a mode states none.</summary>
    public const int GameLaps = 3;

    /// <summary>The game's own CPU level (Master; rules.toml's absent value), where a mode states none.</summary>
    public const int GameCpuLevel = 3;

    /// <summary>The mode's stats choice is the host's own pack file (<c>stats pack;</c> on the choice <c>file</c>): the
    /// host hands it (<see cref="LobbyRules.PackFile"/>), and <see cref="Pack"/> is null.</summary>
    public bool PackFromFile { get; init; }

    /// <summary>The index of the choice parameter that chooses the stat pack (<c>stats pack;</c>); null: none. A launcher
    /// says its choices <see cref="StockChoice"/> and <see cref="FileChoice"/> in words, and offers the file for the second.</summary>
    public int? PackParam { get; init; }

    /// <summary>The choice of a <c>stats</c> parameter that is the disc's own stats.</summary>
    public const string StockChoice = "stock";

    /// <summary>The choice of a <c>stats</c> parameter that is the host's own pack file.</summary>
    public const string FileChoice = "file";

    /// <summary>A mode's terms, on the parameters' values (null: their defaults, as Quick Play races every mode). A word
    /// given an optional parameter at its named value (<see cref="ModeParam.Or"/>) is as if not written, and such words
    /// are read after the others, so a value given wins over the form it stands beside (<c>laps</c> over
    /// <c>laps by course</c>).</summary>
    public static ModeTerms Of(ModePackage mode, IReadOnlyList<int>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        int Cell(ModeSetupValue v) => !v.FromParam ? v.Value : parameters is not null ? parameters[v.Value] : mode.Params[v.Value].Default;
        bool Unset(ModeSetupValue v) => v.FromParam && mode.Params[v.Value].StatesNothing(Cell(v));
        bool Optional(IReadOnlyList<ModeSetupValue> values) => values.Any(v => v.FromParam && mode.Params[v.Value].Or is not null);
        var words = mode.Setups.Select(w => (w.Id, Values: w.Arguments, Text: (string?)null))
            .Concat(mode.LobbyWords.Select(w => (w.Id, w.Values, w.Text)))
            .Where(w => !w.Values.Any(Unset))
            .OrderBy(w => Optional(w.Values));   // (stable: the file's order otherwise)
        var terms = new ModeTerms();
        foreach (var (id, a, text) in words)
        {
            terms = id switch
            {
                ModeSetup.Cpus => terms with { CpusAllowed = Cell(a[0]) != 0 },
                ModeSetup.Solo => terms with { Solo = Cell(a[0]) != 0 },
                ModeSetup.Laps => terms with { LapsAll = Cell(a[0]) },
                ModeSetup.LapsByCourse => terms with
                {
                    LapsElse = Cell(a[0]),
                    LapsTable = Enumerable.Range(0, (a.Count - 1) / 2).ToDictionary(i => a[1 + (2 * i)].Value, i => a[2 + (2 * i)].Value),
                },
                ModeSetup.CpuLevel => terms with { CpuLevel = Cell(a[0]) },
                ModeSetup.FinishLimit => terms with { FinishLimit = Cell(a[0]) },
                ModeSetup.MineRegen => terms with { MinesRespawn = Cell(a[0]) == 1 ? MineRegenOn : Cell(a[0]) },
                ModeSetup.Boost when Cell(a[0]) == 0 => terms with { BoostStart = false },
                ModeSetup.Course => terms with { Course = Cell(a[0]) },
                ModeSetup.CoursesAll => terms with { Courses = [] },
                ModeSetup.Courses => terms with { Courses = [.. a.Select(Cell).Order()] },
                ModeSetup.Racers => terms with { Racers = (Cell(a[0]), Cell(a[1])) },
                ModeSetup.MachinesAll => terms with { Machines = [] },
                ModeSetup.Machines => terms with { Machines = [.. a.Select(Cell).Order()] },
                ModeSetup.Customs => terms with { CustomsOff = Cell(a[0]) == 0 },
                ModeSetup.Machine => terms with { MachineRule = new(ModeMachineWay.Fixed, [Cell(a[0])]) },
                ModeSetup.MachineRandom => terms with { MachineRule = new(ModeMachineWay.Random, []) },
                ModeSetup.MachineRandomEach => terms with { MachineRule = new(ModeMachineWay.RandomEach, []) },
                ModeSetup.MachineBySeat => terms with { MachineRule = new(ModeMachineWay.BySeat, [.. a.Select(Cell)]) },
                ModeSetup.MachineByTeam => terms with { MachineRule = new(ModeMachineWay.ByTeam, [.. a.Select(Cell)]) },
                ModeSetup.MachineByPlace => terms with { MachineRule = new(ModeMachineWay.ByPlace, [.. a.Select(Cell)]) },
                ModeSetup.MachineSetting => terms with { Setting = new SettingRange(Cell(a[0]), Cell(a[1])) },
                ModeSetup.Stats when text is null && a is [{ FromParam: true } chosen] =>
                    Chosen(terms, mode.Params[chosen.Value].Choices[Cell(chosen)]) with { PackParam = chosen.Value },
                ModeSetup.Stats => terms with { Pack = text },
                ModeSetup.StatsStock => terms with { Pack = "" },
                ModeSetup.MixedPacks => terms with { MixedPacks = Cell(a[0]) != 0 },
                ModeSetup.BoostStart => terms with { BoostStart = Cell(a[0]) != 0 },
                ModeSetup.Teams => terms with { Teams = (Cell(a[0]), Cell(a[1])) },
                _ => terms,   // (the game's alone)
            };
        }
        return terms;
    }

    /// <summary>A <c>stats</c> parameter's choice: a pack by its name, <see cref="StockChoice"/> or <see cref="FileChoice"/>.</summary>
    private static ModeTerms Chosen(ModeTerms terms, string choice) => choice switch
    {
        StockChoice => terms with { Pack = "", PackFromFile = false },
        FileChoice => terms with { Pack = null, PackFromFile = true },
        _ => terms with { Pack = choice, PackFromFile = false },
    };

    /// <summary>The race's rules under the mode, every one (a word left out: the game's own), as
    /// <see cref="LobbyRules.Values"/> holds a rule: what goes over the server's, and what a lobby's panel shows as the
    /// mode's. The stat pack, the machines and a list of courses are no number:
    /// see <see cref="Pack"/>, <see cref="Machines"/>, <see cref="Courses"/>.</summary>
    public IReadOnlyDictionary<string, int> Rules()
    {
        var rules = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [HostRules.FinishLimit.Name] = FinishLimit,
        };
        void Put(string key, int? value)
        {
            if (value is { } v)
            {
                rules[key] = v;
            }
        }
        rules[HostRules.CpuFill.Name] = (int)(CpusAllowed ? CpuFillMode.Vote : CpuFillMode.Never);
        rules[HostRules.Laps.Name] = LapsAll ?? (LapsElse is null ? GameLaps : LapsByCourse);
        rules[HostRules.CpuLevel.Name] = CpuLevel ?? GameCpuLevel;
        rules[HostRules.BoostStart.Name] = BoostStart == true ? 1 : 0;
        rules[HostRules.MinesRespawn.Name] = MinesRespawn ?? 0;
        if (Course is { } course)
        {
            rules[HostRules.CourseSelect.Name] = (int)CourseSelectMode.Fixed;
            rules[LobbyRules.FixedCourseKey] = course;
        }
        Put(HostRules.Racers.Name, Racers?.Most);
        var range = Setting ?? SettingRange.Free;   // (the game's own: each racer's own)
        rules[HostRules.MachineSetting.Name] = range.Low;
        rules[LobbyRules.SettingHighKey] = range.High;
        rules[HostRules.MixedPacks.Name] = MixedPacks == true ? 1 : 0;
        return rules;
    }

    /// <summary>The teams a lobby offers under this mode: its <c>teams</c>' most (a race needs players on at least its
    /// least, <see cref="Refusal"/>; a host chooses the count through the mode's parameter, <c>teams n..n</c>); a server's
    /// rule within it; no <c>teams</c> word: the rule's. 0: no teams, every seat its own.</summary>
    public int TeamCount(int lobbyTeams)
    {
        if (Teams is { } teams)
        {
            return lobbyTeams == 0 ? teams.Most : Math.Clamp(lobbyTeams, teams.Least, teams.Most);
        }
        return lobbyTeams;
    }

    /// <summary>Why a race of so many racers (CPUs counted) on so many teams holding one is refused; null: it is not.</summary>
    public string? Refusal(int racers, int teamsHeld)
    {
        if (Solo)
        {
            return "This mode is raced alone, from Play's Practice.";
        }
        if (Racers is { } range && racers < range.Least)
        {
            return string.Create(CultureInfo.InvariantCulture, $"This mode needs at least {range.Least} racers.");
        }
        return Teams is { } teams && teamsHeld < teams.Least
            ? string.Create(CultureInfo.InvariantCulture, $"This mode needs racers on at least {teams.Least} teams.")
            : null;
    }
}

/// <summary>A race's teams (piconet-docs research/game-modes/ design 3): the launcher's or host's choice for each player,
/// the rest given out so the teams stay even. They travel in the match start's <c>setup.mode.teams</c> and so the
/// replay's header; display is the launcher's.</summary>
public static class ModeTeams
{
    /// <summary>The fewest teams a lobby may have.</summary>
    public const int Least = 2;

    /// <summary>The most, the seats of a race.</summary>
    public const int Most = ModeWords.Teams;

    /// <summary>A team as players read it: "Team 1".</summary>
    public static string Word(int team) => string.Create(CultureInfo.InvariantCulture, $"Team {team + 1}");

    /// <summary>A seat's team in a race's <c>setup.mode.teams</c>, in words: "" when the race has no teams. A seat past
    /// the list's end is a team of its own, its number (formats.md section 9).</summary>
    public static string Said(IReadOnlyList<int>? teams, int seat) => teams is null ? "" : Word(seat < teams.Count ? teams[seat] : seat);

    /// <summary>A lobby's teams rule as it applies: 0 none, else in range (a stray 1 is 2).</summary>
    public static int Kept(int value) => value <= 0 ? 0 : Math.Clamp(value, Least, Most);

    /// <summary>Each seat's team, by seat: a seat's chosen team where it has one inside <paramref name="teams"/>, every
    /// other (a player with none, a CPU) on the team with the fewest so far, the lowest number first. Null with no
    /// teams.</summary>
    /// <param name="chosen">The seats' choices, by seat (null: none).</param>
    /// <param name="teams">The teams of the race; 0: none.</param>
    public static IReadOnlyList<int>? Assign(IReadOnlyList<int?> chosen, int teams)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        if (teams == 0)
        {
            return null;
        }
        var size = new int[teams];
        var seats = chosen.Select(c => c is { } t && t >= 0 && t < teams ? t : -1).ToArray();
        foreach (var t in seats.Where(t => t >= 0))
        {
            size[t]++;
        }
        for (var i = 0; i < seats.Length; i++)
        {
            if (seats[i] < 0)
            {
                seats[i] = Array.IndexOf(size, size.Min());
                size[seats[i]]++;
            }
        }
        return seats;
    }
}
