using System.Globalization;

namespace Online.Protocol;

/// <summary>What a word of a block of the car data is, for a stat pack.</summary>
public enum StatWordKind
{
    /// <summary>One number.</summary>
    Number,

    /// <summary>The word at 0x48: two bytes, then two that are zero.</summary>
    Bytes,

    /// <summary>Four points of three numbers each: X, Y, Z.</summary>
    Points,
}

/// <summary>One row of the ranges: the words of a block from <paramref name="At"/> on, their name in a pack file, and
/// what they may hold. A number is at least <paramref name="Low"/> and at most <paramref name="High"/> (less than it
/// with <paramref name="Below"/>); for the two bytes <paramref name="High"/> is the most the second may be; for
/// points <paramref name="Low"/> is minus <paramref name="High"/>. <paramref name="ZLeast"/>: how far from 0 a
/// point's Z is at least, in a machine's block (0: no such rule). A custom part's block is held by the row's size
/// alone (<see cref="StatPackRules.Problem"/>). <paramref name="TriedTo"/>: the
/// narrower range of what has been tried, 0 to this: a value beyond it is said where a pack is made
/// (<see cref="StatPackRules.Caution"/>, <see cref="StatPack.UntriedWarning"/>), and nobody refuses it.</summary>
public sealed record StatRule(int At, StatWordKind Kind, string Name, float Low, float High, bool Below,
    float ZLeast = 0, float? TriedTo = null)
{
    /// <summary>How many words of the block the row covers.</summary>
    public int Words => Kind == StatWordKind.Points ? 12 : 1;
}

/// <summary>
/// What a stat pack may hold (handbook/stat-packs.md): the car data's blocks, and for every word of a block
/// a pack can change the range the game's physics is safe in. The list is described once, in protocol/stats.toml,
/// and generated three times: here (<see cref="All"/>, generated/StatTables.g.cs), for the launcher and the server;
/// in the game (<c>port/core/src/stat_ranges.h</c>), which checks the pack it is handed again; and on the
/// handbook's page, which each program's test compares its own list with.
/// <para>Two kinds of range. The ranges proper are the game's: a game build is made with them, so a pack outside
/// them needs a new build. They are safe on their own: a value is the bits of 0 or a normal number (no denormal, no
/// -0, no infinity); in a machine's block a divisor (and the max speed and boost duration) has a floor, any other
/// stat may be negative down to minus its most, and none is more than about ten times the largest the disc holds; a custom part's block, which no race of that
/// build reads, is held by the same size either side of 0 (<see cref="Problem"/>). Every pack is held to them and to nothing narrower, wherever it is
/// made and wherever it arrives (<see cref="StatPack.Of"/>, <see cref="StatPack.FromWire"/>): a launcher takes
/// every pack its game takes, so that a server can hand out a new pack with no new launcher. The ranges of caution (<see cref="StatRule.TriedTo"/>: about twice the largest value the disc has,
/// for the stats that are no divisor) refuse nothing: where a pack is made (the import, a server's start) the
/// values beyond them are said, as a warning to whoever makes it.</para>
/// </summary>
public static partial class StatPackRules
{
    /// <summary>Bytes of one block of the car data.</summary>
    public const int BlockSize = (int)GameFacts.Files.CarBlock;

    /// <summary>The machines' blocks, by id; the custom parts' follow.</summary>
    public const int Machines = (int)GameFacts.Files.CarMachines;

    /// <summary>The parts of one kind: 25 bodies, then 25 cockpits, then 25 boosters.</summary>
    public const int PartsOfAKind = (int)GameFacts.Files.CarParts / 3;

    public const int Blocks = Machines + (int)GameFacts.Files.CarParts;

    /// <summary>The words of a block a pack may change: all but word 0, the place of the name.</summary>
    public const int Words = (BlockSize / 4) - 1;

    public const int MostEntries = Blocks * Words;

    /// <summary>The three kinds of part, as a pack file names them, in the car data's order.</summary>
    public static IReadOnlyList<string> PartKinds { get; } = ["body", "cockpit", "booster"];

    /// <summary>Where a block begins in the car data, the file's and the loaded table's alike.</summary>
    public static int BlockAt(int block) =>
        block < Machines ? block * BlockSize : (int)GameFacts.Files.CarPartsAt + ((block - Machines) * BlockSize);

    /// <summary>The rule of a word of a block, by its offset; null: no word a pack may change is there (word 0, the
    /// name's place, among them).</summary>
    public static StatRule? RuleAt(int offset) =>
        offset % 4 != 0 ? null : All.FirstOrDefault(rule => offset >= rule.At && offset < rule.At + (rule.Words * 4));

    /// <summary>A word's name as a pack file writes it: "Weight", "Hover2Z"; null where no rule is.</summary>
    public static string? WordName(int offset)
    {
        if (RuleAt(offset) is not { } rule)
        {
            return null;
        }
        var word = (offset - rule.At) / 4;
        return rule.Kind == StatWordKind.Points ? $"{rule.Name}{word / 3}{"XYZ"[word % 3]}" : rule.Name;
    }

    /// <summary>The offset of the word a pack file names so; null: no such name.</summary>
    public static int? OffsetOf(string name)
    {
        for (var offset = 4; offset < BlockSize; offset += 4)
        {
            if (string.Equals(WordName(offset), name, StringComparison.Ordinal))
            {
                return offset;
            }
        }
        return null;
    }

    /// <summary>A block as a pack file names it: "machine 6", "body 0", "booster 24".</summary>
    public static string BlockName(int block) => block < Machines
        ? $"machine {block}"
        : $"{PartKinds[(block - Machines) / PartsOfAKind]} {(block - Machines) % PartsOfAKind}";

    /// <summary>A number as the handbook's table writes it: six decimals at most, no zeros at the end ("100000",
    /// "0.45").</summary>
    public static string Text(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Why a block's word may not hold these 32 bits, as the end of a sentence ("is 0, and must be at least
    /// 1"); null: it may. The block and the offset are a block and a word a pack may change. A machine's block (0
    /// to 40) is held to its row. A custom part's (41 to 115) is what the part adds to a custom machine, whose block
    /// is the sum of its three parts', and a part may take away what another gives: its number is the bits of 0 or a
    /// normal number no farther from 0 than a machine's most, with no floor and no sign of its own (no race of the
    /// game build these rows freeze with has a custom machine; <c>port/core/src/stat_ranges.h</c> says how that was
    /// checked). The game's own check (<c>port/core/src/files_stats.cpp</c>) says the same of every value: both
    /// are held to one list of cases.</summary>
    public static string? Problem(int block, int offset, uint bits)
    {
        var rule = RuleAt(offset) ?? throw new ArgumentOutOfRangeException(nameof(offset));
        if (rule.Kind == StatWordKind.Bytes)
        {
            return ((bits >> 16) & 0xFF) > (uint)rule.High || (bits & 0xFFFF) != 0
                ? $"is {bits:X8}: the second byte is at most {Text(rule.High)} and the last two are 0"
                : null;
        }
        // on the bits, before anything is compared: the bits of 0, or a normal number (the race reads a denormal as
        // zero, so one is neither above a floor nor "not 0" whatever a comparison says)
        var exponent = (bits >> 23) & 0xFF;
        if (bits != 0 && exponent is 0 or 0xFF)
        {
            return $"is the bits {bits:X8}, which is no number a pack may hold (a denormal, -0, an infinity or none at all)";
        }
        var value = BitConverter.UInt32BitsToSingle(bits);
        if (block >= Machines)
        {
            // a custom part: what it adds to a custom machine, of either sign; held by its size alone
            return (rule.Below ? !(MathF.Abs(value) < rule.High) : !(MathF.Abs(value) <= rule.High))
                ? $"is {Number(value)}, and a part's value is {(rule.Below ? "less than" : "at most")} {Text(rule.High)} from 0"
                : null;
        }
        if (rule.ZLeast != 0 && (offset - rule.At) / 4 % 3 == 2 && MathF.Abs(value) < rule.ZLeast)
        {
            return $"is {Number(value)}, and a machine's hover point's Z is at least {Text(rule.ZLeast)} from 0";
        }
        if (!(value >= rule.Low))
        {
            return $"is {Number(value)}, and must be at least {Text(rule.Low)}";
        }
        if (rule.Below ? !(value < rule.High) : !(value <= rule.High))
        {
            return $"is {Number(value)}, and must be {(rule.Below ? "below" : "at most")} {Text(rule.High)}";
        }
        return null;
    }

    /// <summary>What is said of a value the game takes and nobody has raced: a number outside what has been tried
    /// (<see cref="StatRule.TriedTo"/>), as the end of a sentence; null: it is inside, or the word has no such
    /// range. A machine's block only: a custom part's value is raced in no build yet, and nothing is said of it.
    /// A warning where a pack is made, never a refusal.</summary>
    public static string? Caution(int block, int offset, uint bits)
    {
        var value = BitConverter.UInt32BitsToSingle(bits);
        return block < Machines && RuleAt(offset) is { TriedTo: { } most } && !(value >= 0 && value <= most)
            ? $"is {Number(value)}, outside what has been tried (0 to {Text(most)})"
            : null;
    }

    /// <summary>A number as a pack file writes it: the shortest text that reads back to the same 32 bits.</summary>
    public static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
