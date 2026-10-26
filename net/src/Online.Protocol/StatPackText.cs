using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Online.Protocol;

/// <summary>
/// The stat pack file (<c>NAME.fzstats</c>; handbook/stat-packs.md): a pack as text a person can read and
/// compare, and the one form a pack is kept in. The first line is <c>fzstats 1</c>, the second <c>name TEXT</c>, the
/// third may be <c>author TEXT</c> (who made the pack: shown beside its name, no part of its fingerprint); then
/// a value a line: <c>machine ID</c> (0 to 40) or <c>body</c>, <c>cockpit</c>, <c>booster</c> and its number (0 to 24),
/// the stat's name (<see cref="StatPackRules.WordName"/>) and the value, a decimal number; for <c>Bytes48</c>, the
/// two bytes. A line <c>constant NAME VALUE</c> is one of the game's code constants (<see cref="StatPackConstants"/>):
/// its name and a decimal number, read as the nearest number of the constant's own size (32 or 64 bits). Empty lines
/// and lines that begin with <c>#</c> are skipped.
/// <para>A number is read as the nearest 32-bit number and written as the shortest text that reads back to the same
/// bits, so a file and the pack it gives are the same values exactly. This program alone reads the text: what
/// travels to the game is the bits (<see cref="StatPackWire"/>).</para>
/// </summary>
public static partial class StatPackText
{
    /// <summary>The file's first line.</summary>
    public const string Header = "fzstats 1";

    /// <summary>The file name's ending.</summary>
    public const string Extension = ".fzstats";

    /// <summary>The longest text read: a whole car data's values are a tenth of it.</summary>
    public const int MostCharacters = 1 << 20;

    /// <summary>A decimal number and nothing else: no sign of plus, no "NaN", no space inside.</summary>
    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?$")]
    private static partial Regex Decimal();

    /// <summary>The pack as its file's text: the values in the order of block and offset, a comment with the
    /// machine's name before each machine's.</summary>
    public static string Write(StatPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var text = new StringBuilder().Append(Header).Append('\n').Append("name ").Append(pack.Name).Append('\n');
        if (pack.Author is { } by)
        {
            text.Append("author ").Append(by).Append('\n');
        }
        var last = -1;
        foreach (var entry in pack.Entries)
        {
            if (entry.Block != last && entry.Block < StatPackRules.Machines)
            {
                text.Append("# ").Append(GameMachines.Names[entry.Block]).Append('\n');
            }
            last = entry.Block;
            var value = entry.At == (int)GameFacts.Files.CarBytesAt
                ? $"{entry.Bits >> 24} {(entry.Bits >> 16) & 0xFF}"
                : StatPackRules.Number(entry.Value);
            text.Append(StatPackRules.BlockName(entry.Block)).Append(' ').Append(StatPackRules.WordName(entry.At)).Append(' ')
                .Append(value).Append('\n');
        }
        if (pack.Constants.Count > 0)
        {
            text.Append("# the game's code constants\n");
        }
        for (var i = 0; i < pack.Constants.Count; i++)
        {
            var constant = StatPackConstants.All[pack.Constants[i].At];
            if (constant.Kind == StatConstantKind.Number64Low)
            {
                continue;
            }
            var low = constant.Kind == StatConstantKind.Number64High ? pack.Constants[i + 1].Bits : 0;
            text.Append("constant ").Append(constant.Name).Append(' ')
                .Append(StatPackConstants.Text(constant, pack.Constants[i].Bits, low)).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>The pack of a file's text; or the sentence that says what is wrong with it, with the line's number.</summary>
    public static (StatPack? Pack, string? Refusal) Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MostCharacters)
        {
            return (null, "The file is longer than any stat pack.");
        }
        string? name = null, author = null;
        var (headed, values) = (false, false);
        var entries = new List<StatEntry>();
        var seen = new HashSet<(int, int)>();
        var lines = text.Split('\n');
        for (var n = 1; n <= lines.Length; n++)
        {
            var line = lines[n - 1].TrimEnd('\r').Trim(' ', '\t');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            if (!headed)
            {
                if (!string.Equals(line, Header, StringComparison.Ordinal))
                {
                    return (null, $"Line {n}: a stat pack file begins with \"{Header}\".");
                }
                headed = true;
            }
            else if (name is null)
            {
                if (!line.StartsWith("name ", StringComparison.Ordinal) || !StatPack.IsName(line[5..]))
                {
                    return (null, $"Line {n}: the second line is \"name\" and the pack's name, 1 to {StatPack.MostName} characters of plain ASCII.");
                }
                name = line[5..];
            }
            else if (!values && author is null && (line == "author" || line.StartsWith("author ", StringComparison.Ordinal)))
            {
                if (line.Length < 8 || !StatPack.IsName(line[7..]))
                {
                    return (null, $"Line {n}: \"author\" is followed by 1 to {StatPack.MostName} characters of plain ASCII.");
                }
                author = line[7..];
            }
            else if (line.StartsWith("constant ", StringComparison.Ordinal) || line == "constant")
            {
                values = true;
                var (constant, problem) = Constant(line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
                if (constant is null)
                {
                    return (null, $"Line {n}: {problem}");
                }
                if (!seen.Add((StatPackConstants.Block, constant[0].At)))
                {
                    return (null, $"Line {n}: the constant {StatPackConstants.All[constant[0].At].Name} is there twice.");
                }
                entries.AddRange(constant);
            }
            else
            {
                values = true;
                var (entry, problem) = Entry(line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
                if (entry is not { } e)
                {
                    return (null, $"Line {n}: {problem}");
                }
                var word = $"{StatPackRules.BlockName(e.Block)} {StatPackRules.WordName(e.At)}";
                if (!seen.Add((e.Block, e.At)))
                {
                    return (null, $"Line {n}: {word} is there twice.");
                }
                if (StatPackRules.Problem(e.Block, e.At, e.Bits) is { } range)
                {
                    return (null, $"Line {n}: {word} {range}.");
                }
                entries.Add(e);
            }
        }
        return name is null
            ? (null, $"The file has no \"{Header}\" line and \"name\" line at its top.")
            : StatPack.Of(name, entries, author);
    }

    /// <summary>A value line's words as an entry, or what is wrong with them. The value's range is the pack's to
    /// check (<see cref="StatPack.Of"/>).</summary>
    private static (StatEntry? Entry, string? Problem) Entry(string[] words)
    {
        var part = StatPackRules.PartKinds.Index().Where(k => k.Item == words[0]).Select(k => (int?)k.Index).FirstOrDefault();
        if (part is null && words[0] != "machine")
        {
            return (null, $"\"{Shown(words[0])}\" is not machine, body, cockpit or booster.");
        }
        var count = part is null ? StatPackRules.Machines : StatPackRules.PartsOfAKind;
        if (words.Length < 2 || !Whole(words[1], count - 1, out var number))
        {
            return (null, $"a {words[0]}'s number is 0 to {count - 1}.");
        }
        var block = part is { } kind ? StatPackRules.Machines + (kind * StatPackRules.PartsOfAKind) + number : number;
        if (words.Length >= 3 && words[2] == "Name")
        {
            return (null, "a pack changes no name.");
        }
        if (words.Length < 3 || StatPackRules.OffsetOf(words[2]) is not { } at)
        {
            return (null, $"\"{Shown(words.Length < 3 ? "" : words[2])}\" is no stat of a pack.");
        }
        if (at == (int)GameFacts.Files.CarBytesAt)
        {
            return words.Length == 5 && Whole(words[3], 255, out var first) && Whole(words[4], 255, out var second)
                ? (new StatEntry(block, at, ((uint)first << 24) | ((uint)second << 16)), null)
                : (null, $"{words[2]} is two bytes: two whole numbers from 0 to 255.");
        }
        return words.Length == 4 && Decimal().IsMatch(words[3])
            ? (StatEntry.Of(block, at, float.Parse(words[3], NumberStyles.Float, CultureInfo.InvariantCulture)), null)
            : (null, $"{words[2]}'s value is one decimal number.");
    }

    /// <summary>A constant's line (<c>constant NAME VALUE</c>) as its entries (a 64-bit number's two), its value
    /// checked; or what is wrong with it.</summary>
    private static (StatEntry[]? Entries, string? Problem) Constant(string[] words)
    {
        if (words.Length < 2 || StatPackConstants.Named(words[1]) is not { } constant)
        {
            return (null, $"\"{Shown(words.Length < 2 ? "" : words[1])}\" is no constant of a pack.");
        }
        if (words.Length != 3 || !Decimal().IsMatch(words[2]))
        {
            return (null, $"{constant.Name}'s value is one decimal number.");
        }
        uint bits, low = 0;
        if (constant.Kind == StatConstantKind.Number32)
        {
            bits = BitConverter.SingleToUInt32Bits(float.Parse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        else
        {
            var all = BitConverter.DoubleToUInt64Bits(double.Parse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture));
            (bits, low) = ((uint)(all >> 32), (uint)all);
        }
        if (StatPackConstants.Problem(constant.Slot, bits, low) is { } range)
        {
            return (null, $"the constant {constant.Name} {range}.");
        }
        return constant.Kind == StatConstantKind.Number64High
            ? ([new(StatPackConstants.Block, constant.Slot, bits), new(StatPackConstants.Block, constant.Slot + 1, low)], null)
            : ([new(StatPackConstants.Block, constant.Slot, bits)], null);
    }

    private static bool Whole(string text, int most, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= most && text.Length <= 3;

    /// <summary>A word of the file for a sentence: its first characters, and only plain ones.</summary>
    private static string Shown(string word) =>
        new([.. word.Take(24).Select(c => c is >= ' ' and <= '~' ? c : '?')]);
}
