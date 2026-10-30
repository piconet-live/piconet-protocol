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

    /// <summary>A practice's alone: no lobby and no server sets it (<see cref="HostRules.All"/> does not hold it).</summary>
    Practice,
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
/// <param name="Unit">What its number counts ("s", "ms"), as a refusal and the operator's dashboard say it; "": a plain
/// number.</param>
/// <param name="Taken">What it may be, as a refusal says it ("0 to 3", "on or off"); null: a rule of another kind, or
/// one whose range is <see cref="Range"/>.</param>
/// <param name="Flag">The server's command-line option of a rule the games apply (<see cref="HostRules.Race"/>; a
/// switch's takes no value); null: none.</param>
/// <param name="SetupWord">The game mode's setup word that sets the rule (modes.toml; a mode's word wins); null: it runs
/// the lobby or the server before a mode is chosen, or is a practice's, and is no mode's to set.</param>
public sealed record HostRule(string Name, string Label, RuleReach Reach, RuleKind Kind, int Least = 0, int Most = 0,
    int Step = 1, int Default = 0, string Unit = "", string? Taken = null, string? Flag = null, string? SetupWord = null)
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
/// The rules themselves, <see cref="All"/>, <see cref="Alone"/>, <see cref="Race"/> and <see cref="Named"/> are
/// generated from protocol/rules.toml (protocol/generated/HostRules.g.cs).
/// </summary>
public static partial class HostRules
{
    /// <summary>The shortest a start countdown lasts, in seconds (<see cref="StartCountdown"/> is the longest; a
    /// countdown set shorter than this lasts its own length throughout).</summary>
    public const int StartCountdownLeast = 10;
}
