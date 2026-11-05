using System.Text.Json.Serialization;

namespace Online.Protocol;

/// <summary>
/// A custom lobby's rules: the host's value of each rule a host sets (<see cref="Settable"/>), keyed by the rule's
/// name (<see cref="HostRule.Name"/>). A missing key is Quick Play's value: the server's own. The server checks every
/// value against the rule's range in <see cref="HostRules"/>: a number outside it is brought to the nearest one the
/// rule takes (<see cref="HostRule.Kept"/>); an unknown name, or a value that is none of a choice's, is refused
/// (<see cref="CustomLobby.BadRules"/>).
/// </summary>
/// <remarks>Each value is a whole number: a number, delay or lobby time as the rule counts it (seconds for the course
/// vote and the machine choice); a switch 0 off, 1 on; a choice its enum's value in declaration order
/// (<see cref="HostRules.CpuFill"/>: <see cref="CpuFillMode"/>; <see cref="HostRules.CourseSelect"/>:
/// <see cref="CourseSelectMode"/>, with the fixed course under <see cref="FixedCourseKey"/>, a course of
/// <see cref="CoursePool.Default"/>); the machine setting's range its low end under "machine-setting" and its high
/// end under <see cref="SettingHighKey"/>. StatPack: the race's stat pack by its name, one of the server's packs;
/// null: no pack, the disc's own stats, the row's Stock (a new lobby starts on Quick Play's pack, the server's own: LobbyList.QuickPlay's). PackFile: the host's own pack instead, the whole text of a <c>.fzstats</c> file
/// (<see cref="StatPackText"/>), which the server reads and checks as it would a pack file of its own; StatPack then
/// holds that pack's own name (a server from before PackFile looks for a pack of that name, and refuses the rules
/// when it has none). A launcher sends PackFile with a lobby's rules (<see cref="CreateLobby"/>,
/// <see cref="SetLobbyRules"/>); the server sends a lobby's rules with the pack by its name alone. ModeFile: the
/// host's game mode, the whole text of a <c>.fzmode</c> file (piconet-docs research/game-modes/), which the server
/// compiles with the one compiler (<see cref="ModeCompiler"/>) and races in place of Quick Play's rules: a file that
/// does not compile is refused with the compiler's messages (<see cref="CustomLobby.BadMode"/>). The server sends a
/// lobby's rules with the mode as <see cref="Mode"/> alone (its hash, name and badge); a launcher that sends rules again
/// without a ModeFile and with the Mode the server sent keeps it, a Mode of the server's official ones
/// (<see cref="LobbyList.Modes"/>) races that one, and without either races no mode. How the race plays is the mode's:
/// a host's Values hold only <see cref="LobbyKeys"/> and <see cref="NoModeWordYet"/>, and StatPack only beside a
/// PackFile (<see cref="OutsideTheMode"/>). A server from
/// before modes passes over both. ModeValues: the host's values of the mode's parameters, in <c>PARM</c>'s order (a
/// real's bits), checked against its declared ranges (<see cref="ModePackage.ValuesRefusal"/>) and raced in
/// <c>setup.mode.params</c>; null: a new file's defaults, or what the lobby's mode has. The course pool and the allowed machines are Quick Play's in a custom lobby.
/// The server sends a lobby's rules whole (every settable rule's value), so a launcher compares them with Quick
/// Play's (<see cref="LobbyList.QuickPlay"/>) and shows only the rules that differ (<see cref="Differing"/>).</remarks>
public sealed record LobbyRules(IReadOnlyDictionary<string, int> Values,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StatPack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PackFile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LobbyMode? Mode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModeFile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? ModeValues = null)
{
    /// <summary>The longest <see cref="ModeFile"/>, in characters: the package a mode compiles to holds the file's
    /// text and is at most <see cref="ModeWords.PackageBytes"/>.</summary>
    public const int MostModeFile = ModeWords.PackageBytes;

    /// <summary>The longest <see cref="PackFile"/>, in characters: a pack of every value the car data holds, with a
    /// comment a machine, is about half of it.</summary>
    public const int MostPackFile = 256 * 1024;


    /// <summary>The key of the machine setting's high end ("machine-setting" holds the low end).</summary>
    public const string SettingHighKey = "machine-setting-high";

    /// <summary>The key of the lobby's teams under a mode: 0 none (every seat its own), or 2 to
    /// <see cref="ModeTeams.Most"/>. No rule of the table: only a mode's race has teams, in its <c>setup.mode.teams</c>.</summary>
    public const string TeamsKey = "teams";

    /// <summary>The key of the fixed course, with <see cref="HostRules.CourseSelect"/> at
    /// <see cref="CourseSelectMode.Fixed"/>.</summary>
    public const string FixedCourseKey = "course-select-course";

    /// <summary>No rule of the host's own: Quick Play's rules throughout, with the disc's own stats.</summary>
    public static LobbyRules QuickPlay { get; } = new(new Dictionary<string, int>());

    /// <summary>The rules a custom lobby's host sets: those of <see cref="HostRules.All"/> that reach a race alone or
    /// a lobby (not the server's own, <see cref="RuleReach.Server"/>), but the course pool and the allowed machines.</summary>
    public static IReadOnlyList<HostRule> Settable { get; } =
        [.. HostRules.All.Where(r => r.Reach != RuleReach.Server && r.Kind is not (RuleKind.Courses or RuleKind.Machines))];

    /// <summary>The keys <see cref="Values"/> may hold: the settable rules' names but the stat pack's (its own
    /// member), and the two extra keys.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        [.. Settable.Where(r => r.Kind != RuleKind.Pack).Select(r => r.Name), SettingHighKey, FixedCourseKey, TeamsKey];

    /// <summary>The keys a host sets beside the mode: the lobby's own settings (the player limit, capped by the mode's
    /// <c>racers</c>; the ping limit; the vote's and the machine choice's times; the voting minimum; how the course is
    /// found: a vote, a random draw or a fixed course, among the mode's <c>courses</c>, and none when its <c>course</c>
    /// pins one). They shape the lobby, not how the race plays (piconet-docs research/game-modes/ design 5, D10).</summary>
    public static IReadOnlyList<string> LobbyKeys { get; } =
        [HostRules.Racers.Name, HostRules.LatencyLimit.Name, HostRules.VoteSeconds.Name, HostRules.ChooseSeconds.Name,
            HostRules.VotingMinimum.Name, HostRules.CourseSelect.Name, FixedCourseKey];

    /// <summary>The keys that are the mode's alone (each has its setup word, <see cref="HostRule.SetupWord"/>, or is a
    /// mode's parameter: the teams, the CPUs): a host sends none of them, and a server refuses rules that hold one
    /// (<see cref="OutsideTheMode"/>). The stat pack by name is the mode's too (<c>stats</c>).</summary>
    public static IReadOnlyList<string> ModeKeys { get; } = [.. Keys.Where(k => !LobbyKeys.Contains(k))];

    /// <summary>Why a host's rules are refused: they set a rule of the race, which is the mode's (<see cref="ModeKeys"/>),
    /// or a server's stat pack by name (a name goes only with the host's own file); null: they do not. One line, as a refusal says it. (A host's own pack file goes
    /// only with a mode whose stats choice is the host's file: the server checks it against the mode.)</summary>
    public string? OutsideTheMode()
    {
        if (Values?.Keys.FirstOrDefault(ModeKeys.Contains) is { } key)
        {
            var label = key == TeamsKey ? "Teams" : (HostRules.Named(key) ?? HostRules.MachineSetting).Label;
#pragma warning disable CA1308 // (a label inside a line of words, not a key compared)
            return $"the mode sets {label.ToLowerInvariant()} now: choose a mode that sets it, or a mode file of your own";
#pragma warning restore CA1308
        }
        return StatPack is not null && PackFile is null
            ? "the mode sets the stats now: choose a mode that names the pack, or a mode file of your own"
            : null;
    }

    /// <summary>The value under a key; null: Quick Play's.</summary>
    public int? Value(string key) => Values is not null && Values.TryGetValue(key, out var v) ? v : null;

    /// <summary>The value of a rule; null: Quick Play's.</summary>
    public int? Value(HostRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Value(rule.Name);
    }

    /// <summary>The names of the settable rules whose value here is not <paramref name="quickPlay"/>'s, in the rules'
    /// order (<see cref="Settable"/>): what a lobby's rules summary shows. A key missing here is Quick Play's.</summary>
    public IReadOnlyList<string> Differing(LobbyRules quickPlay)
    {
        ArgumentNullException.ThrowIfNull(quickPlay);
        bool Differs(string key) => Value(key) is { } v && v != quickPlay.Value(key);
        // (what the lobby's mode states is the mode's, not the host's, and is not news beside its name)
        return [.. Settable.Where(rule => Mode?.Terms?.ContainsKey(rule.Name) != true && !(rule == HostRules.StatPack && Mode?.Pack is not null)).Where(rule => rule.Kind switch
        {
            RuleKind.Pack => StatPack != quickPlay.StatPack,
            RuleKind.Range => Differs(rule.Name) || Differs(SettingHighKey),
            RuleKind.Course => Differs(rule.Name) || (Value(rule) == (int)CourseSelectMode.Fixed && Differs(FixedCourseKey)),
            _ => Differs(rule.Name),
        }).Select(r => r.Name)];
    }
}

/// <summary>One custom lobby as the lobby browser lists it. Players: its members now; Capacity: its racers rule.
/// Locked: joining asks for its password. Rules: its rules whole (<see cref="LobbyRules"/>). Stage: as
/// <see cref="LobbyState.Stage"/> (nobody joins while it is starting or choosing).</summary>
public sealed record LobbySummary(string Id, string Name, string HostName, int Players, int Capacity, bool Locked,
    LobbyRules Rules, string Stage);

/// <summary>Custom lobbies' limits and the codes of their refusals (<see cref="ErrorMessage"/>).</summary>
public static class CustomLobby
{
    /// <summary>The longest lobby name (after trimming); at least one character.</summary>
    public const int MaxName = 32;

    /// <summary>The longest password; an empty one is none.</summary>
    public const int MaxPassword = 64;

    /// <summary>Joining a locked lobby without its password or with another ("Wrong password"); also a password
    /// longer than <see cref="MaxPassword"/>.</summary>
    public const string BadPassword = "bad_lobby_password";

    /// <summary>A host's request from a member who is not the host (or from a player in no custom lobby).</summary>
    public const string NotHost = "not_host";

    /// <summary>No such lobby (it closed, or never was), or one whose players' build does not race with the joiner's.</summary>
    public const string NoSuchLobby = "no_such_lobby";

    /// <summary>The lobby has as many members as its racers rule.</summary>
    public const string LobbyFull = "lobby_full";

    /// <summary>The lobby takes nobody now (its match is starting or choosing machines), or not this player (the
    /// host removed them from it).</summary>
    public const string LobbyClosed = "lobby_closed";

    /// <summary>An unknown rule name, a choice's value it does not have, a stat pack the server does not have, or a
    /// host's pack file that is not a pack (<see cref="LobbyRules.PackFile"/>; the error's message says why).</summary>
    public const string BadRules = "bad_rules";

    /// <summary>The rules change only while the lobby waits (forming), not during the course vote, the machine choice
    /// or a race.</summary>
    public const string RulesLocked = "rules_locked";

    /// <summary>A host's mode file that is too long or does not compile (<see cref="LobbyRules.ModeFile"/>; the
    /// error's message holds the compiler's first error, "line N: ..."), or a <see cref="LobbyRules.Mode"/> the lobby
    /// does not hold.</summary>
    public const string BadMode = "bad_mode";

    /// <summary>The lobby's mode cannot race now: fewer racers or teams than it says (<c>racers</c>, <c>teams</c>); the
    /// error's message says what it needs. The lobby stays as it is.</summary>
    public const string ModeRefuses = "mode_refuses";

    /// <summary>A lobby name that is empty, too long, or holds characters a name does not.</summary>
    public const string BadName = "bad_lobby_name";

    /// <summary><see cref="LeftLobby.Reason"/>: the host removed the player.</summary>
    public const string Kicked = "kicked";

    /// <summary><see cref="LeftLobby.Reason"/>: the lobby closed.</summary>
    public const string Closed = "closed";
}

/// <summary>Subscribe to the lobby browser (<see cref="LobbyList"/>), or stop. Connected players only; the list
/// holds the lobbies whose build races with the player's.</summary>
public sealed record BrowseLobbies(bool On) : ClientMessage;

/// <summary>A new custom lobby with this name, these rules (<see cref="LobbyRules"/>) and, when not null or empty,
/// this password. The creator joins it as its host, leaving the lobby they were in, with the racer as
/// <see cref="EnterQuickPlay"/> names one (machine, colour, setting, stock colour, a garage-built machine): in a
/// custom lobby the launcher sends no <see cref="EnterQuickPlay"/>.</summary>
public sealed record CreateLobby(string Name, LobbyRules Rules, string? Password, int Machine, Rgb Color, int? Setting = null,
    int? StockColor = null, CustomMachine? Custom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BoostTrail? Trail = null) : ClientMessage;

/// <summary>Join a custom lobby of the browser, leaving the lobby one is in, with the racer as
/// <see cref="EnterQuickPlay"/> names one. A locked lobby needs its password: another is refused with
/// <see cref="CustomLobby.BadPassword"/>.</summary>
public sealed record JoinLobby(string LobbyId, string? Password, int Machine, Rgb Color, int? Setting = null,
    int? StockColor = null, CustomMachine? Custom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BoostTrail? Trail = null) : ClientMessage;

/// <summary>The host's change to their lobby while it is forming: a new name (null: the same), the rules whole
/// (a missing key: Quick Play's), a new password (null: the same), or none (<paramref name="ClearPassword"/>).
/// Refused with <see cref="CustomLobby.NotHost"/> or <see cref="CustomLobby.RulesLocked"/>.</summary>
public sealed record SetLobbyRules(string? Name, LobbyRules Rules, string? Password = null, bool ClearPassword = false)
    : ClientMessage;

/// <summary>The host removes a member: they are sent <see cref="LeftLobby"/> with <see cref="CustomLobby.Kicked"/>
/// and cannot join that lobby again.</summary>
public sealed record KickPlayer(string PlayerId) : ClientMessage;

/// <summary>The host starts the next race of their forming lobby with whoever is there (the start vote also does
/// when more than half of the present vote, from the voting minimum on).</summary>
public sealed record StartLobbyRace : ClientMessage;

/// <summary>Leave the lobby one is in, custom or Quick Play's (as <see cref="LeaveQuickPlay"/>): answered with
/// <see cref="LeftLobby"/>.</summary>
public sealed record LeaveLobby : ClientMessage;

/// <summary>The lobby browser: every custom lobby the player may join (their build's), sent on
/// <see cref="BrowseLobbies"/> and then when the list changes, at most about twice a second. QuickPlay: the server's
/// own rules in the same form, whole: what a lobby's rules are compared with (<see cref="LobbyRules.Differing"/>).
/// Modes: the server's official modes a host may give a lobby (its catalog's lobby modes, in order: Quick Play's
/// normal mode first, then the others), each named as a lobby's mode is, with its
/// settings on their defaults; the host chooses one by naming it in <see cref="LobbyRules.Mode"/> (its hash), or sends
/// a file of their own (<see cref="LobbyRules.ModeFile"/>). Null: a server from before them.</summary>
public sealed record LobbyList(IReadOnlyList<LobbySummary> Lobbies,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LobbyRules? QuickPlay = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<LobbyMode>? Modes = null) : ServerMessage;

/// <summary>A custom lobby's game mode as the server names it (<see cref="LobbyRules.Mode"/>): the package's content
/// hash (<see cref="ModePackage.Hash"/>) and share code, the mode's name, its badge and its line of what it changes
/// (the header's; "" when it has none), and the api it needs (<see cref="Hello.Modes"/>). Terms: the rules the mode
/// races, every rule of the race by its key as <see cref="LobbyRules.Values"/> holds them (<see cref="ModeTerms.Rules"/>:
/// a word left out is the game's own way); Pack: the stat pack it races on its values now ("" the disc's own stats; null:
/// its stats choice is the host's own file, <see cref="LobbyRules.PackFile"/>). CpusAllowed is in Terms (cpu-fill: the
/// vote, or never). Null Terms: a server from before them. Params: the mode's parameters as its package declares them, and
/// Values their values now, in the same order: the lobby's rule controls and what its panel shows. Null: it has none,
/// or a server from before them. Machines: the machine ids it allows when it states a list, so a Garage shows them
/// before the machine choice opens; null: it states none (the lobby's machines stand). CustomsOff: it races no custom
/// machines (<c>customs off</c>), so a Garage holds them back; a reader from before passes over it. Courses: the course
/// numbers it may race on when it states a list, so a host's course row stays within them; null: it states none (the
/// lobby's pool stands); a reader from before passes over it. PackParam: the index of the parameter that chooses the stat
/// pack (<see cref="ModeTerms.PackParam"/>); null: none.</summary>
public sealed record LobbyMode(string Hash, string ShareCode, string Name, int Api, string Badge = "", string About = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? Terms = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ModeParam>? Params = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Values = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Machines = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CustomsOff = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Courses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PackParam = null);

/// <summary>The player's team in a custom lobby with teams: their own, or (from the host) another member's
/// (<paramref name="PlayerId"/>); null Team: none, the race gives them one. A race's seats get their teams from these
/// (<see cref="ModeTeams.Assign"/>). Refused: <see cref="CustomLobby.NotHost"/> for another's, or a team outside the
/// lobby's. A launcher from before it never sends it.</summary>
public sealed record ChooseTeam(int? Team, string? PlayerId = null) : ClientMessage;
