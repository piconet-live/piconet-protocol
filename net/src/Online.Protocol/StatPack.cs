using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Online.Protocol;

/// <summary>One changed word of the game's car data: the block (0 to 40 a machine by id, then 25 bodies, 25
/// cockpits, 25 boosters), the word's offset in the block, and the 32 bits it is to hold as the game holds them (a
/// number's bit pattern, never its decimal text, so that no two programs can round it differently).</summary>
public readonly record struct StatEntry(int Block, int At, uint Bits)
{
    /// <summary>The value as a number (every word but the two bytes at 0x48 is one).</summary>
    public float Value => BitConverter.UInt32BitsToSingle(Bits);

    public static StatEntry Of(int block, int at, float value) => new(block, at, BitConverter.SingleToUInt32Bits(value));
}

/// <summary>A stat pack as the match start carries it (<see cref="RaceSetup.Stats"/>): its name, its fingerprint,
/// and its values as base64 of 6 bytes each (block, offset, the 32 bits high byte first) in the order of block and
/// offset. What arrives is checked by <see cref="StatPack.FromWire"/>, and by the game again.</summary>
public sealed record StatPackWire(string Name, string Fingerprint, string Values);

/// <summary>What names a server's stat pack where it is only shown: in a lobby's state (<see cref="LobbyState.StatPack"/>)
/// and in a server's status. Author: who made the pack, where its file says (<see cref="StatPack.Author"/>); a pack
/// without one is written as it always was. The values travel with the race (<see cref="RaceSetup.Stats"/>), and
/// once to a launcher for its Garage (<see cref="StatPackValues"/>), not here.</summary>
public sealed record StatPackName(string Name, string Fingerprint, string? Author = null);

/// <summary>
/// A stat pack (handbook/stat-packs.md): a named table of changed words of the game's car data, which a
/// server hands every game of a race. One of these is always a checked pack: every entry names a word a pack may
/// change and a value in its range (<see cref="StatPackRules"/>), each word once, in the order of block and offset.
/// Made from a pack file (<see cref="StatPackText"/>), from a changed car data file (the launcher's import), or from
/// what a match start carries.
/// </summary>
public sealed class StatPack
{
    /// <summary>The longest name.</summary>
    public const int MostName = 40;

    private StatPack(string name, StatEntry[] entries, StatEntry[] constants, string? author)
    {
        Name = name;
        Entries = entries;
        Constants = constants;
        Author = author;
        Fingerprint = FingerprintOf(Bytes());
    }

    public string Name { get; }

    /// <summary>Who made the pack, where its file says (a pack file's <c>author</c> line); null: it does not. Shown
    /// beside the name. Like the name it is no part of the fingerprint and does not travel to the game: a match
    /// start's pack has none.</summary>
    public string? Author { get; }

    /// <summary>The car data's values, by block and offset, each word once.</summary>
    public IReadOnlyList<StatEntry> Entries { get; }

    /// <summary>The game's code constants the pack changes (<see cref="StatPackConstants"/>): entries of the block
    /// <see cref="StatPackConstants.Block"/>, by slot, each once; they travel after the car data's. None in a pack
    /// of the car data alone, which is the pack it always was (the same bytes, the same fingerprint).</summary>
    public IReadOnlyList<StatEntry> Constants { get; }

    /// <summary>Every entry as it travels: the car data's, then the constants'.</summary>
    public IEnumerable<StatEntry> All => Entries.Concat(Constants);

    /// <summary>What names the pack: SHA-1 (lower-case hexadecimal) of the format's byte and the values as they
    /// travel. The name is not in it: two packs of one name and other values are two packs.</summary>
    public string Fingerprint { get; }

    /// <summary>Whether a text can be a pack's name, or its author's: 1 to 40 characters of plain ASCII, no space at
    /// either end, so that a log line and a list show it as it is.</summary>
    public static bool IsName(string? name) =>
        name is { Length: >= 1 and <= MostName } && name[0] != ' ' && name[^1] != ' ' && name.All(c => c is >= ' ' and <= '~');

    /// <summary>The pack of these values, in any order; or the sentence that says what is wrong with them: the
    /// name, no value at all, a block or a word a pack cannot change, a word named twice, a value outside the game's
    /// range (<see cref="StatPackRules.Problem"/>: the game's own rows, and nothing narrower). Every pack this
    /// program holds went through here: one from a pack file, one from the import, one a match start carries.</summary>
    public static (StatPack? Pack, string? Refusal) Of(string name, IEnumerable<StatEntry> entries, string? author = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!IsName(name))
        {
            return (null, $"A pack's name is 1 to {MostName} characters of plain ASCII, without a space at either end.");
        }
        if (author is not null && !IsName(author))
        {
            return (null, $"A pack's author is 1 to {MostName} characters of plain ASCII, without a space at either end.");
        }
        var all = entries.OrderBy(e => e.Block).ThenBy(e => e.At).ToArray();
        if (all.Length == 0)
        {
            return (null, "The pack holds no value.");
        }
        var constants = all.Where(e => e.Block == StatPackConstants.Block).ToArray();
        var sorted = all.Where(e => e.Block != StatPackConstants.Block).ToArray();
        for (var i = 1; i < constants.Length; i++)
        {
            if (constants[i - 1].At == constants[i].At)
            {
                var twice = constants[i].At is >= 0 and < StatPackConstants.SlotCount ? StatPackConstants.All[constants[i].At].Name : $"of slot {constants[i].At}";
                return (null, $"The constant {twice} is there twice.");
            }
        }
        if (StatPackConstants.ProblemOf(constants) is { } constantProblem)
        {
            return (null, constantProblem);
        }
        for (var i = 0; i < sorted.Length; i++)
        {
            var (block, at, bits) = sorted[i];
            if (block is < 0 or >= StatPackRules.Blocks)
            {
                return (null, $"Block {block} is none of the car data's {StatPackRules.Blocks}.");
            }
            var where = StatPackRules.BlockName(block);
            if (at == 0)
            {
                return (null, $"{where}: word 0 is the place of its name, and a pack changes no name.");
            }
            if (StatPackRules.WordName(at) is not { } word)
            {
                return (null, $"{where}: no word a pack may change is at offset 0x{at:X2}.");
            }
            if (i > 0 && sorted[i - 1].Block == block && sorted[i - 1].At == at)
            {
                return (null, $"{where} {word} is there twice.");
            }
            if (StatPackRules.Problem(block, at, bits) is { } problem)
            {
                return (null, $"{where} {word} {problem}.");
            }
        }
        return (new StatPack(name, sorted, constants, author), null);
    }

    /// <summary>The values outside what has been tried (<see cref="StatPackRules.Caution"/>), a sentence each
    /// ("machine 6 Grip1 is 9.5, outside what has been tried (0 to 9)."). The game takes them, and so does every
    /// program that is handed the pack.</summary>
    public IReadOnlyList<string> Untried =>
    [
        .. from e in Entries
           let caution = StatPackRules.Caution(e.Block, e.At, e.Bits)
           where caution is not null
           select $"{StatPackRules.BlockName(e.Block)} {StatPackRules.WordName(e.At)} {caution}.",
    ];

    /// <summary>What a program that makes a pack or loads one for a server says of <see cref="Untried"/>, a line
    /// each: how many there are, the first <paramref name="shown"/> of them, and how many more. Nothing when every
    /// value has been tried. It is a warning: the pack is written, and the server starts.</summary>
    public IReadOnlyList<string> UntriedWarning(int shown = 10)
    {
        var untried = Untried;
        if (untried.Count == 0)
        {
            return [];
        }
        var lines = new List<string>
        {
            $"warning: {untried.Count} of the pack's values {(untried.Count == 1 ? "is" : "are")} outside what has been tried. The game takes them; nobody has raced them:",
        };
        lines.AddRange(untried.Take(shown).Select(sentence => "  " + sentence));
        if (untried.Count > shown)
        {
            lines.Add($"  and {untried.Count - shown} more.");
        }
        return lines;
    }

    /// <summary>The values as they travel: 6 bytes each, the car data's and then the constants'.</summary>
    public byte[] Bytes()
    {
        var size = (int)GameFacts.Files.StatEntry;
        var all = All.ToArray();
        var bytes = new byte[all.Length * size];
        for (var i = 0; i < all.Length; i++)
        {
            bytes[i * size] = (byte)all[i].Block;
            bytes[(i * size) + 1] = (byte)all[i].At;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan((i * size) + 2), all[i].Bits);
        }
        return bytes;
    }

    // SHA-1 as a name, not as a guard: the game's core has this hash and works the same fingerprint out, and two
    // programs agreeing on which pack they hold is all it is for.
#pragma warning disable CA5350
    private static string FingerprintOf(byte[] values) =>
        Convert.ToHexStringLower(SHA1.HashData([GameFacts.Files.StatFormat, .. values]));
#pragma warning restore CA5350

    public StatPackWire ToWire() => new(Name, Fingerprint, Convert.ToBase64String(Bytes()));

    /// <summary>The pack a match start carries; or the sentence that says why it is none: base64 that is not written
    /// the one way, bytes that are not whole entries, entries out of order, another fingerprint than the values',
    /// and everything <see cref="Of"/> refuses. A value is held to the game's own rows and to nothing narrower: a
    /// pack the game takes is one this program takes, so that no later pack needs a new launcher.</summary>
    public static (StatPack? Pack, string? Refusal) FromWire(StatPackWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        var size = (int)GameFacts.Files.StatEntry;
        var bytes = new byte[(StatPackRules.MostEntries + StatPackConstants.Slots) * size];
        if (wire.Values is not { Length: > 0 } text || text.Length > (bytes.Length + 2) / 3 * 4
            || !Convert.TryFromBase64String(text, bytes, out var length)
            || !string.Equals(Convert.ToBase64String(bytes, 0, length), text, StringComparison.Ordinal))
        {
            return (null, "The pack's values are not base64, or are more than the whole car data.");
        }
        if (length == 0 || length % size != 0)
        {
            return (null, $"The pack's values are {length} bytes, which is not whole entries of {size}.");
        }
        var entries = new StatEntry[length / size];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = bytes.AsSpan(i * size, size);
            entries[i] = new StatEntry(entry[0], entry[1], BinaryPrimitives.ReadUInt32BigEndian(entry[2..]));
            if (i > 0 && (entries[i - 1].Block, entries[i - 1].At).CompareTo((entries[i].Block, entries[i].At)) > 0)
            {
                return (null, $"The pack's entry {i} is out of order.");
            }
        }
        var (pack, refusal) = Of(wire.Name, entries);
        return pack is not null && !string.Equals(pack.Fingerprint, wire.Fingerprint, StringComparison.Ordinal)
            ? (null, $"The pack's values have the fingerprint {pack.Fingerprint}, and it says another.")
            : (pack, refusal);
    }
}
