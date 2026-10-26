namespace Online.Protocol;

/// <summary>Whom a host's rule concerns, and so which panels show it.</summary>
public enum RuleReach
{
    /// <summary>It means something in a race alone: a practice shows it, as a custom lobby does.</summary>
    Alone,

    /// <summary>It needs other racers (the course vote, the seats, the CPUs, the finish limit, the allowed
    /// machines): a lobby's only.</summary>
    Together,

    /// <summary>The server's own running (timeouts, relays, builds that race together): the operator's only.</summary>
    Server,
}

/// <summary>What a rule's value is, and so the control that sets it.</summary>
public enum RuleKind
{
    /// <summary>One course, or how the course is found (the vote, a draw, the host's).</summary>
    Course,

    /// <summary>A list of courses.</summary>
    Courses,

    /// <summary>A whole number from <see cref="HostRule.Least"/> to <see cref="HostRule.Most"/>.</summary>
    Number,

    /// <summary>On or off (<see cref="HostRule.Default"/> 1: on).</summary>
    Switch,

    /// <summary>A low and a high end, each a whole number from <see cref="HostRule.Least"/> to
    /// <see cref="HostRule.Most"/> (<see cref="SettingRange"/>): a lobby's racers each keep their own value inside it.
    /// Quick Play's: <see cref="HostRule.Default"/> at both ends. A practice holds one value: the player's own.</summary>
    Range,

    /// <summary>One of a few words.</summary>
    Choice,

    /// <summary>A time in milliseconds from <see cref="HostRule.Least"/> to <see cref="HostRule.Most"/> in steps of
    /// <see cref="HostRule.Step"/>, or 0: off.</summary>
    Delay,

    /// <summary>Machine ids.</summary>
    Machines,

    /// <summary>A stat pack by name, or none.</summary>
    Pack,

    /// <summary>The operator's own text form; where it is a number, from <see cref="HostRule.Least"/> to
    /// <see cref="HostRule.Most"/> (a lobby's times and limits: <see cref="HostRules.VoteSeconds"/> to
    /// <see cref="HostRules.StragglerWait"/>).</summary>
    Text,
}

/// <summary>One of a host's rules: the name the server's operator and the server's rule list give it, the label a
/// player's panel shows, whom it concerns, what it is, and its range and Quick Play's value where it is a number.</summary>
/// <param name="Default">Quick Play's value (a switch: 1 on); 0 where the value is no number.</param>
public sealed record HostRule(string Name, string Label, RuleReach Reach, RuleKind Kind, int Least = 0, int Most = 0,
    int Step = 1, int Default = 0)
{
    /// <summary>Whether the rule takes this number: inside its range on its steps; a delay's 0 (off) too.</summary>
    public bool Takes(int value) =>
        (Kind == RuleKind.Delay && value == 0) || (value >= Least && value <= Most && (value - Least) % Step == 0);

    /// <summary>The rule's range as a refusal says it: "1 to 30".</summary>
    public string Range => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Least} to {Most}");

    /// <summary>The nearest number the rule takes (a delay below its least: off).</summary>
    public int Kept(int value)
    {
        if (Kind == RuleKind.Delay && value < Least)
        {
            return 0;
        }
        var inside = Math.Clamp(value, Least, Most);
        return Math.Min(Most, Least + ((inside - Least + (Step / 2)) / Step * Step));
    }
}

/// <summary>
/// THE list of a host's rules (Practice is a custom lobby's rules panel for one player, from
/// one list): the server's rule names in the operator's order (the coordinator's live rules take these names and no
/// others), each with its player's label, whom it concerns and, for a number, its range and Quick Play's value
/// (the server's own defaults are read from here). A practice shows the rules that are <see cref="RuleReach.Alone"/>.
/// </summary>
public static class HostRules
{
    public static HostRule CourseSelect { get; } = new("course-select", "Course", RuleReach.Together, RuleKind.Course);

    public static HostRule CoursePool { get; } = new("course-pool", "Course pool", RuleReach.Together, RuleKind.Courses);

    public static HostRule Racers { get; } = new("racers", "Racers", RuleReach.Together, RuleKind.Number,
        GameFacts.Limits.RacersMin, GameFacts.Limits.Seats, Default: GameFacts.Limits.Seats);

    public static HostRule Laps { get; } = new("laps", "Laps", RuleReach.Alone, RuleKind.Number,
        GameFacts.Limits.LapsMin, GameFacts.Limits.LapsMax, Default: 3);

    public static HostRule CpuLevel { get; } = new("cpu-level", "CPU level", RuleReach.Together, RuleKind.Number,
        0, GameFacts.Limits.CpuLevelMax, Default: GameFacts.Limits.CpuLevelMax);

    public static HostRule CpuFill { get; } = new("cpu-fill", "CPU racers", RuleReach.Together, RuleKind.Choice);

    /// <summary>Boosting on the first lap: alone too (the game's Practice leaves it off here unless the rule is on,
    /// piconet-runner's port/image/routines/direct.py).</summary>
    public static HostRule BoostStart { get; } = new("boost-start", "Boost from the start", RuleReach.Alone, RuleKind.Switch, 0, 1);

    public static HostRule Restore { get; } = new("restore", "Restore", RuleReach.Alone, RuleKind.Switch, 0, 1);

    /// <summary>The race's end this long after the first finish: with one racer the first finish is the last.</summary>
    public static HostRule FinishLimit { get; } = new("finish-limit", "Finish limit", RuleReach.Together, RuleKind.Number,
        0, GameFacts.Limits.FinishLimitMax, Default: 90);

    /// <summary>A mine comes back this many milliseconds after it went off (0: it stays gone, the game's own way);
    /// Quick Play: 200.</summary>
    public static HostRule MinesReturn { get; } = new("mine-regen", "Mines return", RuleReach.Alone, RuleKind.Delay,
        GameFacts.Limits.MineRegenStep, GameFacts.Limits.MineRegenMax, GameFacts.Limits.MineRegenStep, Default: 200);

    public static HostRule Machines { get; } = new("machines", "Machines", RuleReach.Together, RuleKind.Machines);

    /// <summary>A range the racers' own settings are brought into; Quick Play: top speed, fixed.</summary>
    public static HostRule MachineSetting { get; } = new("machine-setting", "Machine setting", RuleReach.Alone, RuleKind.Range,
        0, GameFacts.Limits.SettingMax, Default: GameFacts.Limits.SettingMax);

    public static HostRule StatPack { get; } = new("stat-pack", "Stats", RuleReach.Alone, RuleKind.Pack);
    /// <summary>Each racer may race a pack of their own for their machine (handbook/stat-pack-constants.md, "A pack a
    /// seat"); Quick Play: off, one pack for all (mixed packs only in lobbies).</summary>
    public static HostRule MixedPacks { get; } = new("mixed-packs", "Mixed packs", RuleReach.Together, RuleKind.Switch, 0, 1);

    /// <summary>The course vote's length, in seconds.</summary>
    public static HostRule VoteSeconds { get; } = Lobby("vote-seconds", "Course vote", 5, 120, RuleReach.Together);

    /// <summary>The machine choice's length, in seconds.</summary>
    public static HostRule ChooseSeconds { get; } = Lobby("choose-seconds", "Machine choice", 5, 120, RuleReach.Together);

    /// <summary>The players a lobby needs before its race starts by vote; one above the lobby's racers
    /// (<see cref="Racers"/>) is taken, and such a lobby never starts by vote.</summary>
    public static HostRule VotingMinimum { get; } = Lobby("voting-minimum", "Voting minimum", 1, GameFacts.Limits.Seats, RuleReach.Together);

    /// <summary>How long the relay waits for every game of a race to join, in seconds.</summary>
    public static HostRule StartTimeout { get; } = Lobby("start-timeout", "Start timeout", 10, 600);

    /// <summary>How long the relay waits for every game of a race to be ready, in seconds.</summary>
    public static HostRule ReadyTimeout { get; } = Lobby("ready-timeout", "Ready timeout", 10, 600);

    /// <summary>How long a lobby waits for the last racers of its race once half are back, in seconds; -1 (a number
    /// below the range, which <see cref="HostRule.Takes"/> refuses): until the last is back.</summary>
    public static HostRule StragglerWait { get; } = Lobby("straggler-wait", "Straggler wait", 0, 600);

    /// <summary>The round trip to the race's relay a player may have to race, in milliseconds; a player above it watches
    /// that race from the lobby. A lobby's rule, which its rules line shows (<see cref="ServerRules.LatencyLimitMs"/>);
    /// Quick Play: 250. The range is 30 to 300; 30 is the least, for a host whose players are all close (a LAN, one city).</summary>
    public static HostRule LatencyLimit { get; } = new("latency-limit", "Latency limit", RuleReach.Together, RuleKind.Number,
        30, 300, Default: ServerRules.QuickPlayLatencyLimitMs);

    /// <summary>Every rule, in the operator's order: the race's, the lobbies' times and limits, the server's own.</summary>
    public static IReadOnlyList<HostRule> All { get; } =
    [
        CourseSelect, CoursePool, Racers, Laps, CpuLevel, CpuFill, BoostStart, Restore, FinishLimit, MinesReturn, Machines,
        MachineSetting, StatPack, MixedPacks,
        VoteSeconds, ChooseSeconds, VotingMinimum, StartTimeout, ReadyTimeout, StragglerWait, LatencyLimit,
        new("together", "Builds together", RuleReach.Server, RuleKind.Text),
        new("relay-ceiling", "Relay ceiling", RuleReach.Server, RuleKind.Text),
    ];

    /// <summary>The rules that mean something in a race alone, in order: what a practice shows.</summary>
    public static IReadOnlyList<HostRule> Alone { get; } = [.. All.Where(r => r.Reach == RuleReach.Alone)];

    /// <summary>A rule by its name; null: no such rule.</summary>
    public static HostRule? Named(string name) => All.FirstOrDefault(r => r.Name == name);

    // a lobby's time or limit in the operator's text form ("30", "-1"), with its range: the server's command line and
    // its live rules both check it here (ServerOptions.Ranges, Coordinator.RuleValues)
    private static HostRule Lobby(string name, string label, int least, int most, RuleReach reach = RuleReach.Server) =>
        new(name, label, reach, RuleKind.Text, least, most);
}
