using System.Text.Json.Serialization;

namespace Online.Protocol;

/// <summary>
/// What a coordinator shows of itself (its status page and GET /api/status, JSON): the players online, the open
/// lobbies, the matches being set up, the races running (with the latest live report) and the relays. Player names, machines and colours only;
/// nothing that joins a race (no tokens). StatPack: the stat pack every race of the server runs on; null: none.
/// PackSetting: the stat pack's name as the server found it set where it runs, at its start (the environment's
/// FZGX_STAT_PACK), empty when none was set; null: a server from before that setting, which does not read it. The
/// deploy tooling asks it before it switches a stack's pack; nothing else reads it. Together: the builds whose players
/// share lobbies and matches on this server; null: none, a lobby holds one build. TogetherSetting: that group's name
/// as the server found it set where it runs, at its start (the environment's FZGX_BUILDS_TOGETHER), empty when none
/// was set; null: a server from before that setting. TogetherFault: why a setting that was found is not in force (the
/// server then runs with builds apart); null: nothing is wrong. TogetherFiles: the group files the server holds
/// beside its program, by name, each with its fingerprint (see <see cref="TogetherFileView"/>); null: a server from
/// before the setting. The deploy tooling reads the four; no launcher does. LineBuilds: the released builds whose
/// launchers name no line (<see cref="Hello.Line"/>), each with the line the server puts them in (the file the
/// server holds beside its program); null: a server from before lines, or one that holds none. LineBuildsFault: why
/// that file is not in force (those builds then race by their build alone); null: nothing is wrong. Motd: the message
/// of the day while the operator has one set, as the notice the coordinator sends at each welcome (the same id, tier,
/// text without the tier's word, and time), so that a launcher on its home screen, which says no hello, shows it
/// too; null: none, or a server from before it (written only when set).
/// </summary>
public sealed record ServiceStatus(
    string Name, DateTimeOffset Time, int PlayersOnline, IReadOnlyList<LobbyView> Lobbies,
    IReadOnlyList<FormingMatchView> Starting, IReadOnlyList<RaceView> Races, IReadOnlyList<RelayView> Relays,
    StatPackName? StatPack = null, string? PackSetting = null, TogetherView? Together = null, string? TogetherSetting = null,
    string? TogetherFault = null, IReadOnlyList<TogetherFileView>? TogetherFiles = null,
    IReadOnlyList<LineBuildView>? LineBuilds = null, string? LineBuildsFault = null, RecentNotice? Motd = null);

/// <summary>A released build that names no line, and the line a server puts it in (<see cref="ServiceStatus.LineBuilds"/>):
/// the build with each hash cut to eight characters, its line ("0.5") and what the file calls it ("0.5.1").</summary>
public sealed record LineBuildView(string Build, string Line, string? Label = null);

/// <summary>A group file a server holds (the folder <c>together</c> beside its program): the name a setting would
/// say, and the fingerprint of the group it is. No fingerprint: the file is no group (it names fewer than two
/// builds, or something that is no build), and a setting that named it would not be in force.</summary>
public sealed record TogetherFileView(string Name, string? Fingerprint = null);

/// <summary>The builds that race together on a server: the group's name, its builds in its file's order (each hash
/// cut to eight characters; Label: what the file calls the build, "0.2.0"), and the fingerprint of the whole texts.</summary>
public sealed record TogetherView(string Name, IReadOnlyList<TogetherBuildView> Builds, string Fingerprint);

public sealed record TogetherBuildView(string Build, string? Label = null);

/// <summary>The players of one build in a lobby that holds several (<see cref="LobbyView.Builds"/>).</summary>
public sealed record LobbyBuildView(string Build, int Players);

/// <summary>A racer as shown: the name the game shows, the machine (the game's id) and colour ("#RRGGBB", null for a
/// CPU's stock colour); a lobby's player also their tag (<see cref="PlayerTags"/>; a reader from before passes over it)
/// and their garage-built machine (<see cref="PlayerInfo.Custom"/>; null: Machine; a reader from before passes over it).</summary>
public sealed record RacerView(string Name, int Machine, string? Color, string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomMachine? Custom = null);

/// <summary>A lobby: its players, racing or waiting. Stage: forming, starting, choosing or racing (RaceId: its race).
/// Build: the game build its players share (<see cref="Hello.Build"/>), each hash cut to eight characters; null: none
/// named. In a lobby of the builds that race together it is the group's first build, whoever is in the lobby, and
/// Builds says how many of its players have which build while they have more than one; null: they all have one.
/// Line: the release line the lobby is kept under (<see cref="Hello.Line"/>), whatever its players' builds; Build is
/// then null and Builds says the builds. Null: a lobby of one build (or group), as before lines.</summary>
public sealed record LobbyView(string Id, IReadOnlyList<RacerView> Players, int Capacity, bool VotingOpen, int StartVotes,
    string Stage = "forming", string? RaceId = null, int Racing = 0, string? Build = null,
    IReadOnlyList<LobbyBuildView>? Builds = null, string? Line = null);

/// <summary>A match between its lobby and its race. Stage: "picking a relay" (the players measure the relays),
/// "course vote", "course drawn" (the vote's draw shown), "choosing machines".</summary>
public sealed record FormingMatchView(string Id, int Players, string Stage);

public sealed record RaceView(
    string Id, string Relay, string Region, int Course, string CourseName, int Laps, IReadOnlyList<RacerView> Players,
    int CpuRacers, DateTimeOffset Started, RaceSnapshot? Live = null);

/// <summary>A relay: its id, region, its load (null when it does not report one, or is not running) and the races it
/// takes at once (null: no limit). For a relay the coordinator starts and stops itself also: State
/// (stopped, starting, running, stopping; null for any other relay, which is listed while it is connected), its
/// public address (null: none allocated), its Machine size, IdleUntil (when it is stopped if nothing uses it),
/// AddressUntil (when its address is released if it stays stopped) and Error (set while its last start failed).</summary>
public sealed record RelayView(string Id, string Region, int? Races, int? Players, int? MaxRaces = null, string? State = null,
    string? Address = null, string? Size = null, DateTimeOffset? IdleUntil = null, DateTimeOffset? AddressUntil = null,
    string? Error = null);
