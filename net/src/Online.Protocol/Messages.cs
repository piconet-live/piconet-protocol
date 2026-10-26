using System.Text.Json;
using System.Text.Json.Serialization;

namespace Online.Protocol;

/// <summary>The launcher-coordinator protocol: JSON messages over a WebSocket, each with a "type" field.</summary>
public static class ControlProtocol
{
    /// <summary>Bumped when a message changes incompatibly; the coordinator refuses other versions.</summary>
    public const int Version = 3;   // 2: server rules, machine settings, the course vote; 3: lasting lobbies, the live
                                    // race, stock colours, the machine choice

    /// <summary>The WebSocket path on the coordinator.</summary>
    public const string Path = "/ws";

    /// <summary>How often each side of a WebSocket pings, well inside a proxy's idle limit (Fly's is about 60 s).</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    /// <summary>The channel every connected player reads.</summary>
    public const string GlobalChannel = "global";

    /// <summary>The channel of the lobby the sender is in.</summary>
    public const string LobbyChannel = "lobby";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(ClientMessage message) => JsonSerializer.Serialize(message, Json);

    public static string Encode(ServerMessage message) => JsonSerializer.Serialize(message, Json);

    public static ClientMessage? DecodeClient(string text) => JsonSerializer.Deserialize<ClientMessage>(text, Json);

    public static ServerMessage? DecodeServer(string text) => JsonSerializer.Deserialize<ServerMessage>(text, Json);
}

/// <summary>A machine colour, chosen freely by the player.</summary>
public sealed record Rgb(byte R, byte G, byte B)
{
    /// <summary>The colour as "#RRGGBB", the one text form the launcher and the server show and read.</summary>
    public static string Hex(Rgb color)
    {
        ArgumentNullException.ThrowIfNull(color);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    /// <summary>A colour typed as "#RRGGBB" or "RRGGBB" (spaces around allowed; also "#RGB"), or null: six
    /// hexadecimal digits and nothing between them.</summary>
    public static Rgb? Parse(string? text)
    {
        var h = (text ?? "").Trim().TrimStart('#');
        if (h.Length == 3)
        {
            h = string.Concat(h.Select(c => $"{c}{c}"));
        }
        if (h.Length != 6 || !h.All(char.IsAsciiHexDigit))
        {
            return null;
        }
        var rgb = Convert.FromHexString(h);   // every character is a digit, so this takes it
        return new Rgb(rgb[0], rgb[1], rgb[2]);
    }
}

/// <summary>A lobby member (Watching: they watch the lobby's races instead of racing, <see cref="WatchRace"/>; a
/// launcher from before it passes over it): StockColor as in <see cref="EnterQuickPlay"/>; Racing: in the lobby's race now (otherwise
/// waiting for the next); Chosen: while the lobby is choosing machines, this player has chosen theirs. Tag: the
/// player's tag (<see cref="PlayerTags"/>), shown after the name; a launcher from before tags passes over it.</summary>
public sealed record PlayerInfo(string Id, string Name, int Machine, Rgb Color, int? StockColor = null, bool Racing = false,
    bool Chosen = false, string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Watching = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pack = null);

/// <summary>A relay the players measure. An empty host means the coordinator's own host.</summary>
public sealed record RelayEndpoint(string Id, string Host, int Port);

/// <summary>A player's measured round trip to one relay; null when it did not answer.</summary>
public sealed record RelayRtt(string RelayId, double? RttMs);

/// <summary>One seat of the race. Player seats have a player id; the others are CPU racers. Setting: the machine's
/// acceleration / top speed setting, 0 (acceleration) to 100 (top speed); 50 is the machine as it comes (CPUs).
/// StockColor 0-3: the machine in that stock colour (the game's own textures); otherwise Color painted on it; CPUs
/// neither (as the game loads them).
/// Custom: a garage-built machine the seat races in place of Machine (<see cref="CustomMachine"/>); null: Machine.
/// Pack: a pack of the seat's own (handbook/stat-pack-constants.md, "A pack a seat"): 0 the disc's own stats and
/// constants, k the race's <see cref="RaceSetup.Packs"/>[k - 1]; null: the race's <see cref="RaceSetup.Stats"/>.</summary>
public sealed record SeatSetup(int Seat, string? PlayerId, string Name, int Machine, Rgb? Color, int Setting = 50,
    int? StockColor = null, CustomMachine? Custom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Pack = null)
{
    public bool Cpu => PlayerId is null;
}

/// <summary>Everything every player's game needs to set up the same race. Stats: the race's stat pack (changed words
/// of the game's car data, handbook/stat-packs.md), which every game lays over its own before the race;
/// without one the race runs on the disc's car data. A game from before stat packs does not know the member.
/// Packs: the packs a seat may race in place of Stats (<see cref="SeatSetup.Pack"/>), at most
/// <see cref="MostSeatPacks"/>; null with no seat on a pack of its own, the race of before.
/// Grid: the start grid, the seats in their starting order, pole first: every seat of the race once (the coordinator's
/// rule, Online.Server StartGrid: the players ahead of the CPUs, in reverse of the lobby's last finish). Null: the
/// game's own grid (the CPUs ahead of seat 0, which starts last), every race before 0.4.0 and every replay of one.</summary>
public sealed record RaceSetup(
    int Course, int Laps, IReadOnlyList<SeatSetup> Seats, uint ShuffleSeed, uint CpuSeed, RaceRules? Rules = null,
    StatPackWire? Stats = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StatPackWire>? Packs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Grid = null)
{
    /// <summary>The most packs a race's seats may race beside its own (the game's MAX_SEAT_PACKS).</summary>
    public const int MostSeatPacks = 4;
}

/// <summary>The race's rules the games apply. CpuLevel: 0 Novice, 1 Standard, 2 Expert, 3 Master. BoostFromStart:
/// boosting allowed on the first lap (the game's Practice rule; Grand Prix waits for the second). Restore: a machine
/// that falls or is wrecked is put back instead of being out. FinishLimitSeconds: the race ends for everyone this
/// long after the first machine finishes (0, and a match start without it: it ends when the last has finished or is
/// out); the games count it in steps of the race, no clock of a server's. MineRegenMs: a mine that went off comes
/// back this long after (0, and a match start without it: it stays gone, the game's own way); 50 to 5,000 in steps
/// of 50, whole steps of the race (50 ms is 3).</summary>
public sealed record RaceRules(int CpuLevel = 3, bool BoostFromStart = false, bool Restore = false, int FinishLimitSeconds = 0,
    int MineRegenMs = 0);

[JsonConverter(typeof(JsonStringEnumConverter<CpuFillMode>))]
public enum CpuFillMode
{
    /// <summary>The lobby votes (more than half: CPUs fill the empty seats).</summary>
    Vote,
    Always,
    Never,
}

[JsonConverter(typeof(JsonStringEnumConverter<CourseSelectMode>))]
public enum CourseSelectMode
{
    /// <summary>When the match forms: five random courses from the pool and "Random course"; a draw weighted by the
    /// votes picks the course.</summary>
    Vote,
    Random,

    /// <summary>The host's course.</summary>
    Fixed,
}

/// <summary>A server's rules, as the launcher shows them and keeps to (allowed machines, the machine setting).
/// MachineSetting: the range each racer's own setting is brought into (one value: fixed; 0-100: each player's own);
/// AllowedMachines: the machine ids racers may take. LatencyLimitMs: the lobby's latency limit
/// (<see cref="HostRules.LatencyLimit"/>), the round trip to the race's relay above which a player watches the race
/// instead of racing it; the coordinator applies it, the launcher only shows it. A server from before the member
/// sends none: Quick Play's 250 (it applied its own, 150 unless its operator set another).</summary>
public sealed record ServerRules(
    int Laps, int Racers, CpuFillMode CpuFill, CourseSelectMode CourseSelect, int? FixedCourse,
    IReadOnlyList<int> AllowedMachines, SettingRange MachineSetting, RaceRules Race,
    int LatencyLimitMs = ServerRules.QuickPlayLatencyLimitMs)
{
    /// <summary>Quick Play's latency limit (<see cref="HostRules.LatencyLimit"/>'s default; a constant for the
    /// member's default).</summary>
    public const int QuickPlayLatencyLimitMs = 250;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(EnterQuickPlay), "queue")]
[JsonDerivedType(typeof(LeaveQuickPlay), "leaveQueue")]
[JsonDerivedType(typeof(SendChat), "chat")]
[JsonDerivedType(typeof(VoteStart), "voteStart")]
[JsonDerivedType(typeof(VoteCpuFill), "voteCpuFill")]
[JsonDerivedType(typeof(RelayLatency), "relayLatency")]
[JsonDerivedType(typeof(VoteCourse), "voteCourse")]
[JsonDerivedType(typeof(MachineChoice), "machineChoice")]
[JsonDerivedType(typeof(RaceOver), "raceOver")]
[JsonDerivedType(typeof(RaceReport), "raceReport")]
[JsonDerivedType(typeof(SetName), "name")]
[JsonDerivedType(typeof(LinkPong), "pong")]
[JsonDerivedType(typeof(WatchRace), "watch")]
[JsonDerivedType(typeof(BrowseLobbies), "browseLobbies")]
[JsonDerivedType(typeof(CreateLobby), "createLobby")]
[JsonDerivedType(typeof(JoinLobby), "joinLobby")]
[JsonDerivedType(typeof(SetLobbyRules), "setLobbyRules")]
[JsonDerivedType(typeof(KickPlayer), "kick")]
[JsonDerivedType(typeof(StartLobbyRace), "startLobbyRace")]
[JsonDerivedType(typeof(LeaveLobby), "leaveLobby")]
public abstract record ClientMessage;

/// <summary>The first message: the player's display name and protocol version. A server has no password (a custom
/// lobby may: <see cref="JoinLobby"/>); a "password" field an older launcher sends is passed over as unknown. Build: what the player's game is made of, as its launcher names it (the recompiled game: the SHA-1 of
/// its online part's image, of its native module and of the game program that runs them, "image/module/runner");
/// the server only compares it. Players are put
/// in a lobby, and so matched, only with players of the same build: two games of different builds do not simulate
/// the same race for certain, and a race of them would desync. A server may be told that certain builds race together
/// (a list its operator fills in once a pair was shown to compute the same race): those share lobbies as one build.
/// Null: a launcher from before the check (the Dolphin fork's); those are matched among themselves. VoteCounts: this launcher shows the course vote's counts while the
/// vote runs, so the server sends them (<see cref="CourseVotes"/>); a launcher from before that message says nothing
/// and is sent none, and a server from before it ignores the field and sends none, which is why the version
/// stays. Debug: a test setting that reaches a race is on in this launcher (its launch file's "debug" part: the
/// game's AI drives, lag is added, a scripted visit and so on). A server refuses such a launcher unless it was
/// started to take them (the official service refuses a game that has a debug setting on; a
/// test server can allow it). It guards against an accident, a tester racing with simulated lag left on, not
/// against a changed program: the server cannot check what a launcher says. Written only when true: a player's
/// hello is the text it was, and a launcher from before the field says nothing, which reads as "none on".
/// StatPacks: this launcher hands a race's stat pack (<see cref="RaceSetup.Stats"/>) to its game. A launcher from
/// before stat packs says nothing and would drop the pack, and its game would race the original stats untold: so a
/// server with a pack sends it, and names it in the lobby, only where every member's launcher said this.
/// Pings: this launcher answers the server's ping and shows the lobby's pings (Pings.cs): a launcher from before
/// them is asked nothing and sent no list. Notices: this launcher keeps the server's notices
/// (<see cref="Online.Protocol.Notices"/>) and takes the last hour's again (<see cref="RecentNotices"/>): a launcher
/// from before them is sent only the notices that come while it is connected, as chat lines.
/// Key: the launcher's secret (<see cref="PlayerTags.NewKey"/>), from which the coordinator works out the player's
/// tag (<see cref="PlayerTags.Derived"/>); none (a launcher of 0.2.x): a random tag for this visit; a key that is
/// not one is refused (bad_message). The coordinator keeps it for the connection only and never logs or sends it.
/// A hello cannot choose a tag: a "tag" field (sent by no released launcher) is passed over as unknown. Players: this launcher keeps the live list of connected
/// players (<see cref="PlayersList"/>, <see cref="PlayersChanged"/>): a launcher from before it is sent neither.</summary>
public sealed record Hello(string Name, int Version, string? Build = null, bool VoteCounts = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Debug = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool StatPacks = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Pings = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Notices = false,
    string? Key = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Players = false) : ClientMessage
{
    /// <summary>The longest build name a server takes.</summary>
    public const int MaxBuildLength = 128;

    /// <summary>The code of the error (<see cref="ErrorMessage"/>) a hello with <see cref="Debug"/> is answered with
    /// by a server that takes no such launcher.</summary>
    public const string DebugNotAllowed = "debug_not_allowed";

    /// <summary>The code of the error a hello with a <see cref="Key"/> is answered with when every tag the server
    /// would work out from that secret for that name is held (a launcher's own connections, thousands of them): a
    /// secret is never given a tag other than its own derived ones.</summary>
    public const string TagsTaken = "tags_taken";
}

/// <summary>Quick Play with this machine and colour, and its setting when the server lets players choose (0-100).
/// StockColor 0-3: one of the machine's four stock colours (0 its default; <paramref name="Color"/> is then that
/// colour's swatch, for the lists); null: <paramref name="Color"/> painted on the machine.</summary>
public sealed record EnterQuickPlay(int Machine, Rgb Color, int? Setting = null, int? StockColor = null,
    CustomMachine? Custom = null) : ClientMessage;

/// <summary>Leave Quick Play: the lobby one is in, as <see cref="LeaveLobby"/>.</summary>
public sealed record LeaveQuickPlay : ClientMessage;

public sealed record SendChat(string Channel, string Text) : ClientMessage;

/// <summary>The player's name, changed while connected (in the hello it goes once): the lobby is sent again with it,
/// and it is the name of their chat and of their seat from the next race on. Refused with bad_name, or
/// rename_too_soon for a second change within a second. Sent only to a server whose welcome says it takes it
/// (<see cref="Welcome.Renames"/>): an older server knows no such message.</summary>
public sealed record SetName(string Name) : ClientMessage;

/// <summary>In a lobby of at least the voting minimum: start now with whoever is there.</summary>
public sealed record VoteStart(bool Yes) : ClientMessage;

/// <summary>Fill the empty seats with CPU racers.</summary>
public sealed record VoteCpuFill(bool Yes) : ClientMessage;

/// <summary>The answer to <see cref="MeasureRelays"/>.</summary>
public sealed record RelayLatency(string MatchId, IReadOnlyList<RelayRtt> Results) : ClientMessage;

/// <summary>A vote in the forming match's course vote: the index of one of <see cref="CourseVote"/>'s options (it can
/// be changed until the vote ends).</summary>
public sealed record VoteCourse(string MatchId, int Option) : ClientMessage;

/// <summary>The answer to <see cref="ChooseMachine"/>: the machine to race and its look, as in
/// <see cref="EnterQuickPlay"/> (it can be changed until the choice ends). The player races it from then on. Pack:
/// in a lobby with <see cref="LobbyState.SeatPacks"/>, the fingerprint of the pack the racer's machine races, "" the
/// original stats; null: the lobby's own.</summary>
public sealed record MachineChoice(string MatchId, int Machine, Rgb Color, int? Setting = null, int? StockColor = null,
    CustomMachine? Custom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pack = null)
    : ClientMessage;

/// <summary>The player's game is done with the race (it quit after the results, or the player closed it): the player
/// is back in the lobby. Final: the race as the game last saw it (its last <see cref="RaceSnapshot"/>), from which the
/// coordinator takes the lobby's last finish for the next race's start grid; null: none seen (a launcher from before
/// 0.4.0 sends none).</summary>
public sealed record RaceOver(string MatchId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RaceSnapshot? Final = null) : ClientMessage;

/// <summary>From a racer asked to (<see cref="MatchStart.ReportLive"/>): the race as its game sees it, a few times a
/// second, for the lobby's waiting players.</summary>
public sealed record RaceReport(string MatchId, RaceSnapshot Snapshot) : ClientMessage;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(LobbyState), "lobby")]
[JsonDerivedType(typeof(LeftLobby), "leftLobby")]
[JsonDerivedType(typeof(ChatMessage), "chat")]
[JsonDerivedType(typeof(RecentNotices), "recentNotices")]
[JsonDerivedType(typeof(MeasureRelays), "measureRelays")]
[JsonDerivedType(typeof(MatchStart), "matchStart")]
[JsonDerivedType(typeof(Requeued), "requeued")]
[JsonDerivedType(typeof(QuickPlayEnded), "quickPlayEnded")]
[JsonDerivedType(typeof(CourseVote), "courseVote")]
[JsonDerivedType(typeof(CourseVotes), "courseVotes")]
[JsonDerivedType(typeof(CourseChosen), "courseChosen")]
[JsonDerivedType(typeof(ChooseMachine), "chooseMachine")]
[JsonDerivedType(typeof(LiveRace), "liveRace")]
[JsonDerivedType(typeof(RaceDesynced), "raceDesynced")]
[JsonDerivedType(typeof(LinkPing), "ping")]
[JsonDerivedType(typeof(LobbyPings), "pings")]
[JsonDerivedType(typeof(StatPackValues), "statPack")]
[JsonDerivedType(typeof(PlayersList), "players")]
[JsonDerivedType(typeof(PlayersChanged), "playersChanged")]
[JsonDerivedType(typeof(TagChanged), "tag")]
[JsonDerivedType(typeof(WatchStart), "watchStart")]
[JsonDerivedType(typeof(RulesChanged), "rules")]
[JsonDerivedType(typeof(LobbyList), "lobbyList")]
public abstract record ServerMessage;

/// <summary>The server's stat pack, whole, once after the welcome, to a launcher whose hello said it knows stat
/// packs (<see cref="Hello.StatPacks"/>): for what the launcher shows before a race (its Garage, in a lobby whose
/// state names this pack by its fingerprint). Not what a race runs on: that is the match start's own
/// (<see cref="RaceSetup.Stats"/>). Sent again when the operator changes the pack; <see cref="None"/> when it is set
/// to none (no values: every launcher, released ones too, then shows no pack, as <c>StatPack.FromWire</c> refuses it).</summary>
public sealed record StatPackValues(StatPackWire Pack) : ServerMessage
{
    public static StatPackValues None { get; } = new(new StatPackWire("", "", ""));
}

/// <summary>The hello taken. Tag: the player's tag on this connection (<see cref="PlayerTags"/>): worked out from the
/// hello's <see cref="Hello.Key"/> (the player's own, or for this connection when that name and tag are another
/// connected player's), or random when the hello sent no key. The launcher saves nothing of it. A launcher from
/// before tags passes over it.</summary>
public sealed record Welcome(string PlayerId, string ServerName, int Version, ServerRules? Rules = null, bool Renames = false,
    string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Spectators = false) : ServerMessage;

/// <summary>The server's rules changed while this player is connected (the operator's live rules): the whole rules,
/// as a welcome carries them, to every welcomed launcher, in a lobby or not. They are what the launcher shows and
/// keeps to from now on; a race already running keeps the set-up of its match start, and the next match start
/// carries the new rules. A launcher from before 0.3.0 drops it as an unknown type.</summary>
public sealed record RulesChanged(ServerRules Rules) : ServerMessage;

/// <summary>A refused request, with a short text a launcher can show as it is. Codes: bad_name, bad_version,
/// bad_build, not_ready, already_welcomed (a second hello), machine_not_allowed (queueing or choosing),
/// not_in_lobby, voting_closed, bad_channel, bad_text, rate_limited (the connection, before it is closed),
/// rename_too_soon, bad_message; a custom lobby's: <see cref="CustomLobby"/>'s codes; a chat line the filter refused: <see cref="Chat.ChatRefusal"/>'s codes. Seconds:
/// how long a chat mute lasts still (0: it is over); written only with a value, so a launcher from before it reads
/// the message as it did.</summary>
public sealed record ErrorMessage(string Code, string Message, int? Seconds = null) : ServerMessage;

/// <summary>The lobby the player is in, sent to its members whenever it changes. A lobby lasts from race to race and
/// holds up to <paramref name="Capacity"/> players, racing or waiting for the next race. Stage: forming (gathering,
/// voting to start), starting (a match forming: relays measured, the course vote), choosing (the course decided, the
/// racers choose their machines), racing (<paramref name="Race"/>; players who joined since wait and watch). Nobody
/// joins a lobby while it is starting or choosing. Waiting: what the lobby's race waits for once its machines are
/// chosen (<see cref="WaitingForRelay"/>: its race server is still being started); null: nothing.
/// OtherBuilds: the players in Quick Play with another build of the game (<see cref="Hello.Build"/>), by build: they
/// are in lobbies of their own and are never matched with this lobby's players; the launcher says which part of the
/// game differs. Null: there are none. The builds a server lets race together count as one build here: they are not
/// "other" to each other, and other lobbies are told of them as one entry, under the first build of the server's list. VotingOpen, StartVotes and CpuFillVotes are of the members present, those
/// whose <see cref="PlayerInfo"/> does not say racing: a member still out in a race the lobby moved on from has no
/// vote until they are back, and more than half of the present start the next race. StatPack: the stat pack every
/// race of this lobby runs on (its name, for a label; null: the original stats, which is also what a lobby of a
/// server with a pack gets while one of its members' launchers does not know stat packs). A launcher from before
/// the member passes over it. SeatPacks: with the server's rule mixed-packs on, the packs a racer may race in place of
/// StatPack, each by its name (a <see cref="MachineChoice.Pack"/> names one by its fingerprint, "" the original
/// stats); null: every racer races StatPack (Quick Play). Each racer's own is <see cref="PlayerInfo.Pack"/>.
/// Custom: a custom lobby (<see cref="CreateLobby"/>), whose host is <paramref name="HostId"/> (the member present
/// longest takes over when the host leaves) and whose rules are <paramref name="Rules"/>, whole; sent again when
/// the host or the rules change. Name: a custom lobby's name; Locked: joining it asks for a password; both sent
/// again when the host renames the lobby or sets or clears its password. Quick Play's lobbies have none of these,
/// and a launcher from before them passes over them.</summary>
public sealed record LobbyState(
    string LobbyId, IReadOnlyList<PlayerInfo> Players, int Capacity, int VotingMinimum, bool VotingOpen,
    int StartVotes, int CpuFillVotes, string Stage = LobbyState.Forming, LobbyRace? Race = null, string? Waiting = null,
    IReadOnlyList<OtherBuild>? OtherBuilds = null, StatPackName? StatPack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StatPackName>? SeatPacks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Custom = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HostId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LobbyRules? Rules = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Locked = false)
    : ServerMessage
{
    /// <summary><see cref="Waiting"/>: the race server (relay) is being started.</summary>
    public const string WaitingForRelay = "relay";

    public const string Forming = "forming";
    public const string Starting = "starting";
    public const string Choosing = "choosing";
    public const string Racing = "racing";
}

/// <summary>Players in Quick Play whose game is another build than the lobby's (<see cref="LobbyState.OtherBuilds"/>):
/// their build as their launchers named it (null: launchers that name none) and how many they are.</summary>
public sealed record OtherBuild(string? Build, int Players);

/// <summary>The player is out of the lobby: they left (Reason null), the host removed them
/// (<see cref="CustomLobby.Kicked"/>) or it closed (<see cref="CustomLobby.Closed"/>).</summary>
public sealed record LeftLobby(string LobbyId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null) : ServerMessage;

/// <summary>A chat line. From the server's operator (<see cref="Notices"/>), as it is sent: <see cref="FromId"/> is
/// <see cref="Notices.ServerId"/>, the channel global, and two optional fields that launchers before them ignore:
/// the tier (<see cref="Level"/>) and the notice's id. From a player: <see cref="FromTag"/>, their tag
/// (<see cref="PlayerTags"/>), which a launcher from before tags passes over; a notice has none.</summary>
public sealed record ChatMessage(string Channel, string FromId, string FromName, string Text, DateTimeOffset Time,
    string? Level = null, string? NoticeId = null, string? FromTag = null)
    : ServerMessage;

/// <summary>The server's notices of the last hour that were for this player, oldest first, each with its own time:
/// sent after the welcome (those to everyone) and on joining a lobby (that lobby's), only to a launcher whose hello
/// said <see cref="Hello.Notices"/>. Never a chat line: the launcher keeps them in its list, once each by id.</summary>
public sealed record RecentNotices(IReadOnlyList<RecentNotice> Notices) : ServerMessage;

/// <summary>One notice of <see cref="RecentNotices"/>: its id, tier, text (without the tier's word) and time.</summary>
public sealed record RecentNotice(string Id, string Level, string Text, DateTimeOffset Time);

/// <summary>A match is forming: measure the round trip to each relay and answer within the timeout. It can come again
/// for the same match, with the relay started for it (and once more for a player it did not answer).</summary>
public sealed record MeasureRelays(string MatchId, IReadOnlyList<RelayEndpoint> Relays, int TimeoutMs) : ServerMessage;

/// <summary>The race: join the relay with the token, as this seat. The relay's host is an address when the
/// coordinator started the relay itself (it changes from one start to the next); a name otherwise. ReportLive: send the race as the game sees it
/// (<see cref="RaceReport"/>) for the lobby's waiting players.</summary>
public sealed record MatchStart(string MatchId, RelayEndpoint Relay, string Token, int Seat, RaceSetup Setup,
    bool ReportLive = false) : ServerMessage;

/// <summary>Taken out of a forming match; the player stays in the lobby for the next one. Reasons: latency (too slow
/// to the chosen relay), no_relay (none connected), relay_failed (the race server could not be started, in any region).</summary>
public sealed record Requeued(string MatchId, string Reason) : ServerMessage;

/// <summary>Taken out of a forming match and out of Quick Play, not queued again (the launcher goes back to its home
/// screen). Reasons: at_capacity (every relay is at its race limit); or, from a coordinator that takes no players
/// any more (retired: a new build has its own servers), a few plain words, with <paramref name="Message"/> the
/// operator's whole text. A launcher that knows no Message shows the reason, in brackets, cut
/// to 40 characters.</summary>
public sealed record QuickPlayEnded(string MatchId, string Reason, string? Message = null) : ServerMessage;

/// <summary>The forming match's course vote (like Overwatch's map vote): the options (course numbers;
/// <see cref="RandomCourse"/> for "Random course") and how long it lasts.</summary>
public sealed record CourseVote(string MatchId, IReadOnlyList<int> Options, int DurationMs) : ServerMessage
{
    public const int RandomCourse = -1;
}

/// <summary>The course vote as it stands (the counts are visible to everyone during the vote):
/// the votes per option, in the options' order, and who has voted (player ids), sent to everyone in the lobby who
/// asked (<see cref="Hello.VoteCounts"/>) each time a vote is cast or changed or a voter leaves.</summary>
public sealed record CourseVotes(string MatchId, IReadOnlyList<int> Votes, IReadOnlyList<string> Voted) : ServerMessage;

/// <summary>The vote's result: the votes per option, the option drawn (a draw weighted by the votes; every option
/// alike when nobody voted) and the course (drawn from the pool when "Random course" won). For the launcher's
/// animation; the match start follows.</summary>
public sealed record CourseChosen(string MatchId, IReadOnlyList<int> Options, IReadOnlyList<int> Votes, int Winner, int Course)
    : ServerMessage;

/// <summary>The match's machine choice, its own step after the course is decided: the course,
/// the machines racers may take, and how long the choice lasts. It ends when every racer has chosen
/// (<see cref="MachineChoice"/>) or the time is up; a racer who has not chosen races the machine they last raced or
/// queued with.</summary>
public sealed record ChooseMachine(string MatchId, int Course, IReadOnlyList<int> Machines, int DurationMs) : ServerMessage;

/// <summary>The lobby's race as a racer's game sees it, for the players waiting in the lobby.</summary>
public sealed record LiveRace(string MatchId, RaceSnapshot Snapshot) : ServerMessage;

/// <summary>The player's game went out of sync with the others in this race: the relay disconnected it (the AI drives
/// its machine for the others). The player is back in the lobby, waiting for the next race; the launcher stops the
/// game and says why.</summary>
public sealed record RaceDesynced(string MatchId) : ServerMessage;

/// <summary>A lobby member watches the lobby's races instead of racing (Yes), or races again from the next (a
/// spectator seat). A watcher is not in the next match and has no vote; while
/// the lobby races, and at every race's start, they are sent <see cref="WatchStart"/>. Sent only to a server whose
/// welcome says it takes it (<see cref="Welcome"/>'s Spectators): an older server knows no such message.</summary>
public sealed record WatchRace(bool Yes) : ClientMessage;

/// <summary>Watch the lobby's race: join the relay with this spectator's token (its game receives the race and sends
/// no input), watching the machine of <paramref name="Seat"/> at first, as <see cref="MatchStart"/> with no seat of
/// one's own. A race already running is caught up behind LOADING.</summary>
public sealed record WatchStart(string MatchId, RelayEndpoint Relay, string Token, int Seat, RaceSetup Setup) : ServerMessage;
