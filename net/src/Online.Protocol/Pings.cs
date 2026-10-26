namespace Online.Protocol;

// Each player's ping in the lobby. All of it is optional on both sides: a launcher says in its
// hello that it answers pings and shows them (Hello.Pings), and only such a launcher is sent these messages; a
// launcher of before knows none of them and is sent none, and a server of before sends none.

/// <summary>The coordinator asks: answer at once with the same number (<see cref="LinkPong"/>). It times the answer
/// itself; the number is one it drew, so an answer cannot be sent ahead of the question.</summary>
public sealed record LinkPing(uint Id) : ServerMessage;

/// <summary>The launcher's answer to a <see cref="LinkPing"/>. It carries no measurement: nothing a launcher says
/// about its own connection is taken.</summary>
public sealed record LinkPong(uint Id) : ClientMessage;

/// <summary>One player's ping as the lobby shows it, in whole milliseconds (0 to <see cref="MostMs"/>).</summary>
public sealed record PlayerPing(string Id, int Ms)
{
    /// <summary>The most a ping is shown as: three digits.</summary>
    public const int MostMs = 999;
}

/// <summary>The pings of a lobby's players, sent to its members every few seconds while something changed: for each
/// player the round trip to the coordinator as the coordinator measured it, and, from the moment their match has a
/// relay, the round trip to that relay as their launcher measured it for the match (the ping of the race). A player
/// without a measurement is not in the list.</summary>
public sealed record LobbyPings(string LobbyId, IReadOnlyList<PlayerPing> Pings) : ServerMessage;
