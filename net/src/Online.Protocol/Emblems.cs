namespace Online.Protocol;

/// <summary>A player's emblem as the lobby link carries it (<see cref="EnterQuickPlay"/>, <see cref="MachineChoice"/>,
/// <see cref="CreateLobby"/>, <see cref="JoinLobby"/> from the player, <see cref="SeatSetup"/> to every game of the
/// race): the game's own emblem picture, 64 x 64 texels in the GameCube's RGB5A3 format (4 x 4 tiles, 16-bit big-endian
/// texels), 8192 bytes, the payload of the game's emblem save file, written as base64 with its padding (10924
/// characters). Every 16-bit value is a texel, so a picture is checked by its size alone; text of any other length, or
/// that is not base64, is no emblem. Pictures come from other players: a reader takes exactly this or nothing.</summary>
public static class Emblems
{
    /// <summary>The picture's bytes (64 x 64 x 2).</summary>
    public const int Bytes = 8192;

    /// <summary>Its base64 text's length, padding included.</summary>
    public const int TextLength = (Bytes + 2) / 3 * 4;

    /// <summary>True for the base64 text of exactly one picture.</summary>
    public static bool Valid(string? text)
    {
        if (text is null || text.Length != TextLength)
        {
            return false;
        }
        Span<byte> bytes = stackalloc byte[Bytes + 3];
        return Convert.TryFromBase64String(text, bytes, out int written) && written == Bytes;
    }

    /// <summary>The text when it is one picture, else null (no emblem).</summary>
    public static string? Checked(string? text) => Valid(text) ? text : null;
}
