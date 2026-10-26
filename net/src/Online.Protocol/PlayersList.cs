namespace Online.Protocol;

// The live list of connected players, for the launcher's Players tab. All of it is optional
// on both sides: a launcher says in its hello that it keeps the list (Hello.Players), and only such a launcher is
// sent these messages; a launcher of before knows none of them and is sent none, and a server of before sends none.
// The list is the whole of what it says of a player: no ping, region or build.

/// <summary>One connected player as the list shows them: id, name and tag (<see cref="PlayerTags"/>), the colour
/// they last queued with (null: not yet), and what they are doing (<see cref="Status"/>, with the lobby's id for all
/// but <see cref="Online"/>).</summary>
public sealed record ListedPlayer(string Id, string Name, string Tag, Rgb? Color, string Status, string? Lobby = null)
{
    /// <summary>Connected, in no lobby.</summary>
    public const string Online = "online";

    /// <summary>In <see cref="Lobby"/>, not racing and not watching its race.</summary>
    public const string InLobby = "lobby";

    /// <summary>In a race of <see cref="Lobby"/> (its race, or one it moved on from).</summary>
    public const string Racing = "racing";

    /// <summary>In <see cref="Lobby"/> while its race runs without them: they watch it.</summary>
    public const string Watching = "watching";
}

/// <summary>The whole list, sent once after the welcome, in parts of at most a few dozen players: the launcher starts
/// a new list at part 0 and has the whole of it after part <paramref name="Parts"/> - 1. The list is as the players'
/// changes last went out (<see cref="PlayersChanged"/>): the player who just came is in the next change, as joined.</summary>
public sealed record PlayersList(IReadOnlyList<ListedPlayer> Players, int Part = 0, int Parts = 1) : ServerMessage;

/// <summary>What changed in the list since the last list or change: players who came (whole), players whose name,
/// tag, colour or status changed (whole, as they are now), and the ids of players who left. Coalesced: at most a few
/// a second, and at most a few dozen players a message; what did not fit goes in the next.</summary>
public sealed record PlayersChanged(IReadOnlyList<ListedPlayer> Joined, IReadOnlyList<ListedPlayer> Changed, IReadOnlyList<string> Left)
    : ServerMessage;

/// <summary>The player's tag changed while connected, after a rename (<see cref="PlayerTags"/>): with a key, the tag
/// is worked out again for the new name (counter 0 first, so a connection that had a clash's tag can get its own
/// back); without one, a new tag is drawn when the new name and the tag are another connected player's. It lasts
/// for this connection: the launcher shows and uses it and saves nothing. A launcher of before knows no such message
/// and passes over it.</summary>
public sealed record TagChanged(string Tag) : ServerMessage;
