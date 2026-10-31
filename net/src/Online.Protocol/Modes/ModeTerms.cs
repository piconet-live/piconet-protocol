using System.Globalization;

namespace Online.Protocol;

/// <summary>What a mode states of its race, read from its setup words on its parameters' values: the one place a mode's
/// word meets a lobby rule (the overhaul of piconet-docs research/game-modes/ design 5). A word left out leaves the
/// lobby's rule, but two: a mode owns its race's ending, so the finish limit is off unless the mode states it, and CPUs
/// come only when it says <c>cpus on</c>. The service and the launcher's stand-in both lay these over a lobby.
/// Lobby flow (the vote's and choice's times, the timeouts, the ping limit) is not a mode's.</summary>
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
    public int? MinesReturn { get; init; }

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

    /// <summary>The range a racer's own setting is brought into.</summary>
    public SettingRange? Setting { get; init; }

    /// <summary>The stat pack by name ("" the disc's own stats); null: the lobby's.</summary>
    public string? Pack { get; init; }

    /// <summary>Each racer may race a pack of their own; the mode's when stated.</summary>
    public bool? MixedPacks { get; init; }

    /// <summary>The teams a race has (<c>teams</c>); null: the lobby's rule.</summary>
    public (int Least, int Most)? Teams { get; init; }

    /// <summary>The laps on a course as the mode states them; null: the lobby's. 0: unlimited.</summary>
    public int? Laps(int course) => LapsAll ?? (LapsTable.TryGetValue(course, out var row) ? row : LapsElse);

    /// <summary>A mode's terms, on the parameters' values (null: their defaults, as Quick Play races every mode).</summary>
    public static ModeTerms Of(ModePackage mode, IReadOnlyList<int>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        int Cell(ModeSetupValue v) => !v.FromParam ? v.Value : parameters is not null ? parameters[v.Value] : mode.Params[v.Value].Default;
        var terms = new ModeTerms();
        foreach (var word in mode.Setups)
        {
            var a = word.Arguments;
            terms = word.Id switch
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
                ModeSetup.MineRegen => terms with { MinesReturn = Cell(a[0]) == 1 ? MineRegenOn : Cell(a[0]) },
                ModeSetup.Boost when Cell(a[0]) == 0 => terms with { BoostStart = false },
                _ => terms,   // (the game's alone)
            };
        }
        foreach (var word in mode.LobbyWords)
        {
            var a = word.Values;
            terms = word.Id switch
            {
                ModeSetup.Course => terms with { Course = Cell(a[0]) },
                ModeSetup.CoursesAll => terms with { Courses = [] },
                ModeSetup.Courses => terms with { Courses = [.. a.Select(Cell).Order()] },
                ModeSetup.Racers => terms with { Racers = (Cell(a[0]), Cell(a[1])) },
                ModeSetup.MachinesAll => terms with { Machines = [] },
                ModeSetup.Machines => terms with { Machines = [.. a.Select(Cell).Order()] },
                ModeSetup.Customs => terms with { CustomsOff = Cell(a[0]) == 0 },
                ModeSetup.MachineSetting => terms with { Setting = new SettingRange(Cell(a[0]), Cell(a[1])) },
                ModeSetup.Stats => terms with { Pack = word.Text },
                ModeSetup.StatsStock => terms with { Pack = "" },
                ModeSetup.MixedPacks => terms with { MixedPacks = Cell(a[0]) != 0 },
                ModeSetup.BoostStart => terms with { BoostStart = Cell(a[0]) != 0 },
                ModeSetup.Teams => terms with { Teams = (Cell(a[0]), Cell(a[1])) },
                _ => terms,
            };
        }
        return terms;
    }

    /// <summary>The rules the mode owns, as <see cref="LobbyRules.Values"/> holds a rule: what goes over the host's
    /// values, and what a lobby's panel shows as the mode's. The stat pack, the machines and a list of courses are no number:
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
        if (!CpusAllowed)
        {
            rules[HostRules.CpuFill.Name] = (int)CpuFillMode.Never;
        }
        Put(HostRules.Laps.Name, LapsAll ?? (LapsElse is null ? null : LapsByCourse));
        Put(HostRules.CpuLevel.Name, CpuLevel);
        Put(HostRules.BoostStart.Name, BoostStart is { } boost ? boost ? 1 : 0 : null);
        Put(HostRules.MinesReturn.Name, MinesReturn);
        if (Course is { } course)
        {
            rules[HostRules.CourseSelect.Name] = (int)CourseSelectMode.Fixed;
            rules[LobbyRules.FixedCourseKey] = course;
        }
        Put(HostRules.Racers.Name, Racers?.Most);
        if (Setting is { } setting)
        {
            rules[HostRules.MachineSetting.Name] = setting.Low;
            rules[LobbyRules.SettingHighKey] = setting.High;
        }
        Put(HostRules.MixedPacks.Name, MixedPacks is { } mixed ? mixed ? 1 : 0 : null);
        return rules;
    }

    /// <summary>The teams a lobby's race has under this mode, from the lobby's rule: within the mode's <c>teams</c> (no
    /// rule: its least); no <c>teams</c> word: the rule's. 0: no teams, every seat its own.</summary>
    public int TeamCount(int lobbyTeams)
    {
        if (Teams is { } teams)
        {
            return lobbyTeams == 0 ? teams.Least : Math.Clamp(lobbyTeams, teams.Least, teams.Most);
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
