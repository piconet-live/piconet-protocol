namespace Online.Protocol;

/// <summary>Words renamed before api 1 froze (the owner, 2026-10-31): each old word is refused with a message that names
/// the new one, as <c>event go</c> is, so a file written against an earlier build says what to change. <c>seat.look =
/// ghost</c> stays: it is a look, not contact.</summary>
internal static class ModeOldWords
{
    /// <summary>A seat read's old name and its new one.</summary>
    public static readonly IReadOnlyDictionary<string, string> Reads = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["is_player"] = "is_human",
        ["lap_time"] = "mode_lap_steps",
        ["lap_steps"] = "game_lap_steps",
        ["ghost"] = "no_contact",
    };
}
