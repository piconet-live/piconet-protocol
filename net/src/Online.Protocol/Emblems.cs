using System.Security.Cryptography;

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

    /// <summary>The emblems a machine carries at most (the game's garage record has four slots).</summary>
    public const int Slots = 4;

    /// <summary>A picture's name: the SHA-256 of its 8192 bytes, 64 lowercase hex digits.</summary>
    public const int HashLength = 64;

    /// <summary>The limits of a slot's placement as the game's paste editor keeps them (customize.rel fn_3_11A38 and
    /// fn_3_10CB8): the tilt from 11.25 degrees above the horizon to straight down, the size. The target's own limit
    /// is the machine's part (a game's check, against its disc's tables); here only that it is a small number.</summary>
    public const int TiltMin = 0x0800, TiltMax = 0x3FFF;
    public const float SizeMin = 0.05f, SizeMax = 0.35f, TargetMax = 64f;

    /// <summary>True for a picture's name as <see cref="HashOf"/> writes one.</summary>
    public static bool ValidHash(string? hash)
    {
        if (hash is null || hash.Length != HashLength)
        {
            return false;
        }
        foreach (char c in hash)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The name of a picture given as its base64 text (<see cref="Valid"/>); null when it is no picture.</summary>
    public static string? HashOf(string? text)
    {
        if (text is null || text.Length != TextLength)
        {
            return null;
        }
        byte[] bytes = new byte[Bytes + 3];
        if (!Convert.TryFromBase64String(text, bytes, out int written) || written != Bytes)
        {
            return null;
        }
        return Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, Bytes)));
    }

    /// <summary>True for a slot a game may be handed: a picture's name and a placement inside the editor's limits.
    /// The game still checks it against the seat's machine (the part, the target inside the part's box).</summary>
    public static bool Valid(EmblemSlot? s) =>
        s is not null && ValidHash(s.Picture) && s.Part is >= 0 and <= 2 && s.Tilt is >= TiltMin and <= TiltMax &&
        s.Turn is >= short.MinValue and <= short.MaxValue && float.IsFinite(s.X) && float.IsFinite(s.Y) &&
        MathF.Abs(s.X) <= TargetMax && MathF.Abs(s.Y) <= TargetMax && s.Size >= SizeMin && s.Size <= SizeMax;

    /// <summary>A racer's emblems as a player sent them, checked: the valid ones of the first <see cref="Slots"/>, in
    /// their order; null when none is left.</summary>
    public static IReadOnlyList<EmblemSlot>? Checked(IReadOnlyList<EmblemSlot>? slots)
    {
        if (slots is null || slots.Count == 0)
        {
            return null;
        }
        var kept = new List<EmblemSlot>(Slots);
        for (int i = 0; i < slots.Count && i < Slots; i++)
        {
            if (Valid(slots[i]))
            {
                kept.Add(slots[i]);
            }
        }
        return kept.Count == 0 ? null : kept;
    }
}

/// <summary>One emblem of a machine's four (<see cref="EnterQuickPlay"/>, <see cref="MachineChoice"/>,
/// <see cref="CreateLobby"/>, <see cref="JoinLobby"/> from the player; <see cref="PlayerInfo"/> and
/// <see cref="SeatSetup"/> to the others), as the game's garage record holds a slot. Picture: the picture's name
/// (<see cref="Emblems.HashOf"/>: the SHA-256 of its 8192 bytes), never the picture: a game that lacks it asks for it
/// once (<see cref="AskEmblems"/>, <see cref="EmblemPicture"/>) and keeps it. Part: 0 the body; a parts machine 1 the
/// cockpit, 2 the booster. Tilt and Turn: the projector's angles (the game's 16-bit angles, 0x4000 a quarter turn).
/// X, Y: its target in its own frame. Size: the emblem's width and height. Flip: the picture mirrored left to
/// right.</summary>
public sealed record EmblemSlot(string Picture, int Part, int Tilt, int Turn, float X, float Y, float Size,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Flip = false);

/// <summary>A picture the server asked this player for (<see cref="EmblemsWanted"/>): one of the pictures their
/// racer's emblems name, as base64 (<see cref="Emblems"/>). The server names it itself by its bytes; a picture the
/// player's emblems do not name is dropped.</summary>
public sealed record EmblemUpload(string Picture) : ClientMessage;

/// <summary>The pictures this game lacks, by name: each comes back as an <see cref="EmblemPicture"/>, at once or when
/// the server has it.</summary>
public sealed record AskEmblems(IReadOnlyList<string> Pictures) : ClientMessage;

/// <summary>To a player whose racer's emblems name pictures the server does not hold: send them
/// (<see cref="EmblemUpload"/>), once each.</summary>
public sealed record EmblemsWanted(IReadOnlyList<string> Pictures) : ServerMessage;

/// <summary>A picture a game asked for (<see cref="AskEmblems"/>): its name and its base64. From another player: a
/// game takes it only when it is one picture (<see cref="Emblems.Valid(string?)"/>) of that name.</summary>
public sealed record EmblemPicture(string Hash, string Picture) : ServerMessage;

/// <summary>A player reports another player's emblems (the native program's Social card, REPORT): PlayerId the
/// reported player's connection (<see cref="PlayerInfo.Id"/>), Pictures the names the reporter's game holds for them
/// (at most <see cref="Emblems.Slots"/>). The server names the reported pictures itself, from the emblems that player's
/// racer names now, and keeps them for an operator to look at; it punishes nobody. Sent only to a server whose welcome
/// says it takes reports (<see cref="Welcome.EmblemReports"/>). Answered by <see cref="EmblemReported"/>.</summary>
public sealed record EmblemReport(string PlayerId, IReadOnlyList<string>? Pictures = null) : ClientMessage;

/// <summary>The answer to an <see cref="EmblemReport"/>: PlayerId as the report named it; Result
/// <see cref="Taken"/> (kept for an operator), <see cref="TooMany"/> (this player's reports are past the limit for
/// now), <see cref="NoPlayer"/> (no connected player of that id, or the reporter themself).</summary>
public sealed record EmblemReported(string PlayerId, string Result) : ServerMessage
{
    public const string Taken = "taken";

    public const string TooMany = "too_many";

    public const string NoPlayer = "no_player";
}
