using System.Globalization;

namespace Online.Protocol;

/// <summary>How a code constant is held: one 32-bit number, or a 64-bit one in two slots (high word first).</summary>
public enum StatConstantKind
{
    /// <summary>One 32-bit number.</summary>
    Number32,

    /// <summary>The high word of a 64-bit number; the next slot is its low word, and a pack holds both or neither.</summary>
    Number64High,

    /// <summary>The low word of a 64-bit number.</summary>
    Number64Low,
}

/// <summary>One allowed word of the game's code constants: its slot (an entry's offset in the constants' block), its
/// name in a pack file, how it is held, the game's own bits, the range a value is held to, and what it does.</summary>
public sealed record StatConstant(int Slot, string Name, StatConstantKind Kind, uint Stock, double Low, double High, string What);

/// <summary>
/// What a stat pack may hold beside the car data: the allowed words of the game's code constants (handbook/
/// stat-packs.md, "Constants"). A pack names one by its slot, in the constants' block (<see cref="Block"/>): an
/// entry (<see cref="Block"/>, slot, the 32 bits). The list is described once, in protocol/stats.toml, and generated
/// three times, as the stat ranges are: here (<see cref="All"/>, <see cref="SlotCount"/>: generated/StatTables.g.cs),
/// for the launcher and the server; in the game (<c>port/core/src/const_ranges.h</c>, which names every reader of
/// each word in the game's code); and on the handbook's page. Like the ranges it is
/// frozen into a game build: a word not on it needs the next build. A value is the bits of 0 or a normal number.
/// </summary>
public static partial class StatPackConstants
{
    /// <summary>The block of a pack's entries that are code constants.</summary>
    public const int Block = GameFacts.Files.ConstBlock;

    /// <summary>The slots there are (<see cref="All"/>'s count).</summary>
    public static int Slots => SlotCount;

    /// <summary>The first slot of the constant a pack file names so; null: none.</summary>
    public static StatConstant? Named(string name) =>
        All.FirstOrDefault(c => c.Kind != StatConstantKind.Number64Low && string.Equals(c.Name, name, StringComparison.Ordinal));

    /// <summary>The value of a constant's bits (a 64-bit one with its low word) as a number.</summary>
    public static double Value(StatConstant constant, uint bits, uint lowBits = 0)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return constant.Kind == StatConstantKind.Number32
            ? BitConverter.UInt32BitsToSingle(bits)
            : BitConverter.UInt64BitsToDouble(((ulong)bits << 32) | lowBits);
    }

    /// <summary>A value as a pack file writes it: the shortest text that reads back to the same bits.</summary>
    public static string Text(StatConstant constant, uint bits, uint lowBits = 0)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return constant.Kind == StatConstantKind.Number32
            ? BitConverter.UInt32BitsToSingle(bits).ToString("R", CultureInfo.InvariantCulture)
            : Value(constant, bits, lowBits).ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A value as a person reads it beside a pack's name: the largest number is "no cap".</summary>
    public static string Shown(StatConstant constant, uint bits, uint lowBits = 0)
    {
        ArgumentNullException.ThrowIfNull(constant);
        var value = Value(constant, bits, lowBits);
        return constant.Kind == StatConstantKind.Number64High && value == double.MaxValue
            ? "none"
            : value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>Why a slot may not hold these bits, as the end of a sentence ("is 2, and must be at most 1.7");
    /// null: it may. A 64-bit number's high word is judged with its low word; a low word alone is never judged. The
    /// game's reader (<c>port/core/src/const_pack.cpp</c>) says the same.</summary>
    public static string? Problem(int slot, uint bits, uint lowBits = 0)
    {
        var constant = All[slot];
        if (constant.Kind == StatConstantKind.Number64Low)
        {
            return null;
        }
        var single = constant.Kind == StatConstantKind.Number32;
        var all = single ? bits : ((ulong)bits << 32) | lowBits;
        var exponent = single ? (all >> 23) & 0xFF : (all >> 52) & 0x7FF;
        var most = single ? 0xFFUL : 0x7FFUL;
        if (all != 0 && (exponent == 0 || exponent == most))
        {
            var hex = single ? bits.ToString("X8", CultureInfo.InvariantCulture) : all.ToString("X16", CultureInfo.InvariantCulture);
            return $"is the bits {hex}, which is no number a pack may hold (a denormal, -0, an infinity or none at all)";
        }
        var value = Value(constant, bits, lowBits);
        var text = Text(constant, bits, lowBits);
        if (!(value >= constant.Low))
        {
            return $"is {text}, and must be at least {Number(constant.Low)}";
        }
        return !(value <= constant.High) ? $"is {text}, and must be at most {Number(constant.High)}" : null;
    }

    /// <summary>The constants of a pack's entries (block <see cref="Block"/>, in slot order), judged whole: a 64-bit
    /// number's two slots stand together, and every value is in its range. Null: they may stand; else the sentence.</summary>
    public static string? ProblemOf(IReadOnlyList<StatEntry> constants)
    {
        ArgumentNullException.ThrowIfNull(constants);
        for (var i = 0; i < constants.Count; i++)
        {
            var entry = constants[i];
            if (entry.At < 0 || entry.At >= Slots)
            {
                return $"No code constant a pack may change is at slot {entry.At}: the game has {Slots}.";
            }
            var constant = All[entry.At];
            var high = constant.Kind == StatConstantKind.Number64High;
            var paired = high
                ? i + 1 < constants.Count && constants[i + 1].At == entry.At + 1
                : constant.Kind != StatConstantKind.Number64Low || (i > 0 && constants[i - 1].At + 1 == entry.At);
            if (!paired)
            {
                return $"The constant {constant.Name} is a 64-bit number, and the pack holds one of its two words.";
            }
            if (Problem(entry.At, entry.Bits, high ? constants[i + 1].Bits : 0) is { } problem)
            {
                return $"The constant {constant.Name} {problem}.";
            }
        }
        return null;
    }

    /// <summary>The constants of a pack as a person reads them beside its name: each one's plain name and value
    /// ("Boost energy", "0.15"; the largest speed cap is "none"); empty: none.</summary>
    public static IReadOnlyList<(StatConstant Constant, string Value)> Described(IReadOnlyList<StatEntry> constants)
    {
        ArgumentNullException.ThrowIfNull(constants);
        var list = new List<(StatConstant, string)>();
        for (var i = 0; i < constants.Count; i++)
        {
            var constant = All[constants[i].At];
            if (constant.Kind == StatConstantKind.Number64Low)
            {
                continue;
            }
            var low = constant.Kind == StatConstantKind.Number64High && i + 1 < constants.Count ? constants[i + 1].Bits : 0;
            list.Add((constant, Shown(constant, constants[i].Bits, low)));
        }
        return list;
    }

    /// <summary>The slots of the energy boost's length, int(base + scale x boost duration) frames.</summary>
    public const int BoostLengthBase = 5, BoostLengthScale = 6;

    /// <summary>The most frames a boost's count holds (a byte, +0x18A and +0x18B).</summary>
    public const int MostBoostFrames = 255;

    /// <summary>Why a pack's boost length may not stand with the longest boost duration its car data gives a machine
    /// or a custom one (<paramref name="longest"/>, seconds), as a sentence; null: it may. A pack that holds neither
    /// of the two constants races the game's own int(60 x D), as every pack before them did, and is never judged
    /// here. The game's reader (<c>port/core/src/const_pack.cpp</c>) says the same.</summary>
    public static string? BoostLengthProblem(IReadOnlyList<StatEntry> constants, float longest)
    {
        ArgumentNullException.ThrowIfNull(constants);
        if (!constants.Any(c => c.At is BoostLengthBase or BoostLengthScale))
        {
            return null;
        }
        var bases = constants.Where(c => c.At == BoostLengthBase).Select(c => (uint?)c.Bits).LastOrDefault() ?? All[BoostLengthBase].Stock;
        var scale = constants.Where(c => c.At == BoostLengthScale).Select(c => (uint?)c.Bits).LastOrDefault() ?? All[BoostLengthScale].Stock;
        // as the game counts: the single product, then the double sum, cut toward 0
        var frames = BitConverter.UInt32BitsToSingle(bases) + (double)(BitConverter.UInt32BitsToSingle(scale) * longest);
        return frames < MostBoostFrames + 1 ? null : string.Create(CultureInfo.InvariantCulture,
            $"The energy boost's length {Text(All[BoostLengthBase], bases)} + {Text(All[BoostLengthScale], scale)} x boost duration is {Math.Floor(frames)} frames for the longest boost duration its car data gives ({longest.ToString("R", CultureInfo.InvariantCulture)}), and the game counts at most {MostBoostFrames}.");
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
