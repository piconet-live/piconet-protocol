using System.Globalization;
using System.Text;

namespace Online.Protocol.Chat;

/// <summary>
/// A text brought to the form the word lists are matched in (<see cref="WordList"/>): lower case; accents taken off
/// (é, ñ, å); the letters that only look like Latin ones read as them (Cyrillic а, Greek ο, fullwidth ｆ, 𝐟 and
/// the other mathematical letters, ⓕ, 🅵, small capitals ꜰ); marks that combine with the letter before them, and
/// characters that draw nothing (zero-width spaces, soft hyphens, joiners), taken out. Each character of the folded
/// text knows the characters of the original it came from, so that a match can be masked where it was written.
/// Hand-made tables, not the runtime's normalisation: the projects run with invariant globalization, under which
/// .NET does not normalise (NFKC returns the text as it was).
/// </summary>
public sealed class TextFold
{
    private TextFold(string text, int[] starts, int[] ends)
    {
        Text = text;
        _starts = starts;
        _ends = ends;
    }

    private readonly int[] _starts;
    private readonly int[] _ends;

    /// <summary>The folded text.</summary>
    public string Text { get; }

    /// <summary>Where in the original the folded characters from <paramref name="start"/> (and the next
    /// <paramref name="length"/> - 1) came from: the original's index and length.</summary>
    public (int Start, int Length) Original(int start, int length) =>
        (_starts[start], _ends[start + length - 1] - _starts[start]);

    /// <summary>The text folded.</summary>
    public static TextFold Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var folded = new StringBuilder(text.Length);
        var starts = new List<int>(text.Length);
        var ends = new List<int>(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var at = i;
            var cp = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i) : text[i];
            i += cp > 0xFFFF ? 2 : 1;
            foreach (var c in Fold(cp))
            {
                folded.Append(c);
                starts.Add(at);
                ends.Add(i);
            }
        }
        return new TextFold(folded.ToString(), [.. starts], [.. ends]);
    }

    /// <summary>What one code point becomes: nothing, one character, or two (ß: ss).</summary>
    private static string Fold(int cp)
    {
        if (cp < 0x80)
        {
            return cp is >= 'A' and <= 'Z' ? ((char)(cp + 32)).ToString() : ((char)cp).ToString();
        }
        if (Table.TryGetValue(cp, out var mapped))
        {
            return mapped;
        }
        if (Alphanumeric(cp) is { } c)
        {
            return c.ToString();
        }
        if (cp is >= 0xD800 and <= 0xDFFF)
        {
            return "";   // (half of a pair, alone: draws nothing a word could be made of)
        }
        var category = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.Format)
        {
            return "";
        }
        var text = char.ConvertFromUtf32(cp).ToLowerInvariant();
        return text.Length == 1 && Table.TryGetValue(text[0], out var lower) ? lower : text;
    }

    /// <summary>The letter or digit of the blocks that are the Latin ones in another style: fullwidth, the
    /// mathematical alphanumerics, circled, parenthesised, squared and the regional indicators (🇦).</summary>
    private static char? Alphanumeric(int cp) => cp switch
    {
        >= 0xFF21 and <= 0xFF3A => (char)('a' + (cp - 0xFF21)),
        >= 0xFF41 and <= 0xFF5A => (char)('a' + (cp - 0xFF41)),
        >= 0xFF10 and <= 0xFF19 => (char)('0' + (cp - 0xFF10)),
        >= 0xFF01 and <= 0xFF5E => (char)(cp - 0xFEE0),
        >= 0x1D400 and <= 0x1D6A3 => (char)('a' + ((cp - 0x1D400) % 52 % 26)),
        >= 0x1D7CE and <= 0x1D7FF => (char)('0' + ((cp - 0x1D7CE) % 10)),
        >= 0x24B6 and <= 0x24CF => (char)('a' + (cp - 0x24B6)),
        >= 0x24D0 and <= 0x24E9 => (char)('a' + (cp - 0x24D0)),
        >= 0x249C and <= 0x24B5 => (char)('a' + (cp - 0x249C)),
        >= 0x1F110 and <= 0x1F129 => (char)('a' + (cp - 0x1F110)),
        >= 0x1F130 and <= 0x1F149 => (char)('a' + (cp - 0x1F130)),
        >= 0x1F150 and <= 0x1F169 => (char)('a' + (cp - 0x1F150)),
        >= 0x1F170 and <= 0x1F189 => (char)('a' + (cp - 0x1F170)),
        >= 0x1F1E6 and <= 0x1F1FF => (char)('a' + (cp - 0x1F1E6)),
        >= 0x2460 and <= 0x2468 => (char)('1' + (cp - 0x2460)),
        >= 0x2776 and <= 0x277E => (char)('1' + (cp - 0x2776)),
        _ => null,
    };

    /// <summary>Each line: the letter (or letters) the characters after the first space read as.</summary>
    private static readonly string[] Rows =
    [
        "a àáâãäåāăąǎǟǡǻȁȃȧạảấầẩẫậắằẳẵặⱥаαάɑᵃₐᴀΑА",
        "b ƀƃɓвʙβᵇΒВ",
        "c çćĉċčƈȼсϲᶜᴄ¢С",
        "d ďđɗԁᵈᴅ",
        "e èéêëēĕėęěȅȇȩẹẻẽếềểễệеёєεέɛᵉₑᴇ€ΕЕ",
        "f ƒꜰᶠ",
        "g ĝğġģǧǵɠɡᵍɢ",
        "h ĥħȟһʜʰₕΗН",
        "i ìíîïĩīĭįıǐȉȋịỉіїιίϊɩᶦᵢɪΙІ",
        "j ĵǰјʲⱼᴊЈ",
        "k ķĸƙǩкκᵏₖᴋΚК",
        "l ĺļľŀłƚǀˡₗʟ",
        "m мᵐₘᴍΜМ",
        "n ñńņňŉŋǹɲηпᶰₙɴΝ",
        "o òóôõöøōŏőơǒǫǭǿȍȏȫȭȯȱọỏốồổỗộớờởỡợоοόᵒₒᴏΟО",
        "p ƥрρᵖₚᴘΡР",
        "q ʠԛ",
        "r ŕŗřȑȓɍгʳᵣʀ",
        "s śŝşšșѕʂˢₛꜱЅ",
        "t ţťŧțƭтτᵗₜᴛΤТ",
        "u ùúûüũūŭůűųưǔǖǘǚǜȕȗụủứừửữựμυύϋᵘᵤᴜ",
        "v ʋνѵᵛᵥᴠ",
        "w ŵẁẃẅẉωшᵂʷᴡ",
        "x хχˣₓΧХ",
        "y ýÿŷƴȳỳỵỷỹуүγʸʏΥУ",
        "z źżžƶȥʐᶻᴢΖ",
        "ss ß",
        "ae æǣǽ",
        "oe œ",
        "th þ",
        "dh ð",
    ];

    /// <summary>Each character of <see cref="Rows"/> to its letters, and the letterlike symbols (ℎ, ℓ, ℝ).</summary>
    private static readonly Dictionary<int, string> Table = Build();

    private static Dictionary<int, string> Build()
    {
        var table = new Dictionary<int, string>();
        foreach (var row in Rows)
        {
            var space = row.IndexOf(' ', StringComparison.Ordinal);
            foreach (var c in row[(space + 1)..])
            {
                table.TryAdd(c, row[..space]);
                var upper = char.ToUpperInvariant(c);
                if (upper != c && c < 0x370)   // (a Greek or Cyrillic capital looks like another letter: its own row)
                {
                    table.TryAdd(upper, row[..space]);
                }
            }
        }
        foreach (var (symbol, letter) in "ℂcℊgℋhℌhℍhℎhℐiℑiℒlℓlℕnℙpℚqℛrℜrℝrℤzℨzℬbℭcℯeℰeℱfℳmℴo".Chunk(2).Select(p => (p[0], p[1])))
        {
            table[symbol] = letter.ToString();
        }
        return table;
    }
}
