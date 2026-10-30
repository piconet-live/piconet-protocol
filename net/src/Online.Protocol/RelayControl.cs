using System.Text.Json;
using System.Text.Json.Serialization;

namespace Online.Protocol;

/// <summary>
/// The link between the coordinator and a relay it does not host itself (an online relay in another region): a
/// WebSocket of JSON messages at <see cref="Path"/> on the coordinator, opened by the relay. The relay says hello with
/// its id, region, the public address players reach it on and the shared key; the coordinator then sends it races to
/// create (the players' tokens), and the relay reports how busy it is and the seats it disconnected for a desync.
/// </summary>
public static class RelayControl
{
    /// <summary>Bumped when a message changes incompatibly; the coordinator refuses other versions.</summary>
    public const int Version = 1;

    public const string Path = "/relay";

    /// <summary>A relay that says this in its hello (<see cref="RelayHello.Takes"/>) takes <see cref="EndRelayRace"/>.</summary>
    public const string TakesEndRace = "endRace";

    /// <summary>A relay that says this in its hello takes <see cref="EndRelaySeat"/>.</summary>
    public const string TakesEndSeat = "endSeat";

    /// <summary>A relay that says this in its hello takes <see cref="AddRelaySpectator"/>.</summary>
    public const string TakesSpectators = "spectators";

    /// <summary>How often a relay reports its status (and so shows it is alive).</summary>
    public const int StatusIntervalMs = 5000;

    public static string Encode(RelayControlMessage message) => JsonSerializer.Serialize(message, ControlProtocol.Json);

    public static RelayControlMessage? Decode(string text) =>
        JsonSerializer.Deserialize<RelayControlMessage>(text, ControlProtocol.Json);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RelayHello), "hello")]
[JsonDerivedType(typeof(RelayWelcome), "welcome")]
[JsonDerivedType(typeof(RelayRefused), "refused")]
[JsonDerivedType(typeof(CreateRace), "createRace")]
[JsonDerivedType(typeof(RelayStatus), "status")]
[JsonDerivedType(typeof(SeatDesynced), "seatDesynced")]
[JsonDerivedType(typeof(EndRelayRace), "endRace")]
[JsonDerivedType(typeof(EndRelaySeat), "endSeat")]
[JsonDerivedType(typeof(AddRelaySpectator), "addSpectator")]
public abstract record RelayControlMessage;

/// <summary>Relay to coordinator, first: who it is (<paramref name="Id"/>, unique; players see it as the relay's id),
/// where (<paramref name="Region"/>), the host and UDP port players send to, the protocol versions and the key, and how
/// many races it takes at once (<paramref name="MaxRaces"/>, 0: no limit). <paramref name="Takes"/>: the messages
/// beyond version 1's that it understands (<see cref="RelayControl.TakesEndRace"/>, <see cref="RelayControl.TakesEndSeat"/>);
/// null: none. The coordinator sends a relay only what it said it takes: a message type an end does not know ends the
/// link (<c>RelayLinkMessages.ReadAsync</c>), so a relay from before it is never sent one, and an older coordinator
/// passes over the field.</summary>
public sealed record RelayHello(string Id, string Region, string Host, int Port, int Version, int RelayVersion, string Key,
    int MaxRaces = 0, IReadOnlyList<string>? Takes = null) : RelayControlMessage;

public sealed record RelayWelcome(string Id) : RelayControlMessage;

/// <summary>Codes: bad_key, bad_version.</summary>
public sealed record RelayRefused(string Code, string Message) : RelayControlMessage;

/// <summary>Coordinator to relay: a race to host, one token per player seat (hex, as in MatchStart). The later fields
/// are the operator's live settings (null: the relay's own, from its command line): how long the race waits for a
/// game it has not heard from, and for one still loading, before the AI takes the seat. A relay from before them
/// passes over them and keeps its own. NoRejoin: a seat that leaves does not come back: the race's mode does not say
/// <c>cpus on</c>, and a CPU drives a seat while its game catches up (the owner, 2026-10-30).</summary>
public sealed record CreateRace(string MatchId, IReadOnlyList<RaceSeatToken> Seats, int? StartTimeoutMs = null,
    int? ReadyTimeoutMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NoRejoin = false) : RelayControlMessage;

public sealed record RaceSeatToken(int Seat, string Token);

/// <summary>Relay to coordinator, every <see cref="RelayControl.StatusIntervalMs"/>: the races it is hosting, their
/// players, and the matches still running (the coordinator's status page drops a race the relay no longer has). The
/// later fields are optional (an older end passes over them, or leaves them null): <paramref name="Details"/>, each
/// race as the relay sees it; <paramref name="Incidents"/>, what happened since the last status (a seat gone
/// silent, checksums that differed, a race ended by its time limit or by the operator); <paramref name="Cpu"/>, the
/// relay process's share of the Machine's CPUs over the last interval (0 to 1); <paramref name="BytesOut"/>, the UDP
/// bytes it has sent since it started; <paramref name="Over"/>, the matches of <paramref name="Matches"/> whose race
/// a game said is over (no seat comes back into it: the launchers stop offering Rejoin).</summary>
public sealed record RelayStatus(int Races, int Players, IReadOnlyList<string>? Matches = null,
    IReadOnlyList<RaceDetail>? Details = null, IReadOnlyList<RelayIncident>? Incidents = null, double? Cpu = null,
    long? BytesOut = null, IReadOnlyList<string>? Over = null) : RelayControlMessage;

/// <summary>A race as its relay sees it: the frames closed, how long since go (null: not started), the frames closed
/// early (every seat's input there), the inputs that came late, the checksum votes counted and those whose reports
/// differed, and each player seat.</summary>
public sealed record RaceDetail(string MatchId, uint Frame, long? RunningMs, long EarlyFrames, int LateInputs,
    long ChecksumVotes, int ChecksumsDiffered, IReadOnlyList<SeatDetail> Seats);

/// <summary>A player seat of a race on the relay: its race seat number, its state (<see cref="SeatDetail.Joining"/>,
/// ready, racing, silent, never ready, desynced, ended, removed) and its late inputs.</summary>
public sealed record SeatDetail(int Seat, string State, int LateInputs)
{
    public const string Joining = "joining";
    public const string Ready = "ready";
    public const string Racing = "racing";
    public const string Silent = "silent";
    public const string NeverReady = "never ready";
    public const string Desynced = "desynced";
    public const string Ended = "ended";
    public const string Removed = "removed";
}

/// <summary>Something a relay saw happen: <see cref="RelayIncident.Kind"/> silent (a seat handed to the AI after a
/// silence), desync (checksums differed; Seats: the seats the vote went against, or every reporting seat without a
/// majority), backstop (the race reached its time limit), ended (the operator ended it), removed (the operator took
/// the seat's player off the server: <see cref="EndRelaySeat"/>); the race, the frame and the race seats.</summary>
public sealed record RelayIncident(string Kind, string MatchId, uint Frame, IReadOnlyList<int> Seats)
{
    public const string Silent = "silent";
    public const string Desync = "desync";
    public const string Backstop = "backstop";
    public const string Ended = "ended";
    public const string Removed = "removed";
}

/// <summary>Coordinator to relay, only to a relay whose hello said it takes it (<see cref="RelayControl.TakesEndRace"/>):
/// the operator ended this race. The relay ends it as its time limit does: every seat out, nothing more sent.</summary>
public sealed record EndRelayRace(string MatchId) : RelayControlMessage;

/// <summary>Coordinator to relay, only to a relay whose hello said it takes it (<see cref="RelayControl.TakesEndSeat"/>):
/// the operator kicked or banned the player of this race seat. The relay hands the seat to the AI as it does a silent
/// one (its official input is "AI drives" from then on, the same in every game, and the others are told), and takes
/// nothing more from it.</summary>
public sealed record EndRelaySeat(string MatchId, int Seat) : RelayControlMessage;

/// <summary>Coordinator to relay, only to a relay whose hello said it takes it (<see cref="RelayControl.TakesSpectators"/>):
/// a spectator's token (hex) for this race. Its game receives the
/// race and sends no input; it is no seat and has no vote.</summary>
public sealed record AddRelaySpectator(string MatchId, string Token) : RelayControlMessage;

/// <summary>Relay to coordinator, as it happens: a seat of a race went out of sync at <paramref name="Frame"/> and was
/// disconnected (the AI drives it), for the coordinator to tell its player.</summary>
public sealed record SeatDesynced(string MatchId, int Seat, uint Frame) : RelayControlMessage;
