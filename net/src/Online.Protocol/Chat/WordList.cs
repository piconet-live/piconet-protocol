using System.Text;
using System.Text.RegularExpressions;

namespace Online.Protocol.Chat;

/// <summary>
/// A list of words that are found in a text however they are written (the server's always-blocked list, the
/// launcher's swear words). A term is a word ("spic"), a phrase ("white power": anything or nothing between its
/// words) or a word with a star at an end ("*fuck*": also inside a longer word on that side, and the whole word is
/// what is found). The text is folded
/// first (<see cref="TextFold"/>: case, accents, look-alike letters, invisible characters), and then a term is
/// found:
/// <list type="bullet">
/// <item>as a whole word only, so that ordinary words with the term inside pass ("Scunthorpe", "snigger",
/// "raccoon"); a plural s after it is the same word;</item>
/// <item>with each letter written once or more ("fuuuck"), but never fewer times than the term has it ("niger" is
/// not "nigger");</item>
/// <item>with digits and signs that stand for letters (4 @ a, 8 b, 3 e, 9 6 g, 1 ! | i, 1 | l, 0 o, 5 $ s, 7 + t,
/// 2 z, v u);</item>
/// <item>with punctuation between its letters ("s.p.i.c", "f_u_c_k"), or its letters spaced out one by one
/// ("s p i c").</item>
/// </list>
/// The text a list is read from: a term a line, '#' to the end of a line a comment (<see cref="Parse"/>).
/// </summary>
public sealed class WordList
{
    private static readonly TimeSpan MostTime = TimeSpan.FromMilliseconds(250);

    /// <summary>What <see cref="First"/> answers for a text that took too long to search (none of the lists' terms
    /// should ever take that long: a text is 200 characters at most).</summary>
    public const string TooSlow = "(took too long to check)";

    private readonly Regex? _pattern;

    private WordList(IReadOnlyList<string> terms, IReadOnlyList<string> rejected)
    {
        Terms = terms;
        Rejected = rejected;
        _pattern = terms.Count == 0 ? null : new Regex(string.Join("|", terms.Select((t, i) => $"(?<t{i}>{Pattern(t)})")),
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, MostTime);
    }

    /// <summary>The list's terms, in the order read, each once.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>The lines that were no term (<see cref="Problem"/>), as written.</summary>
    public IReadOnlyList<string> Rejected { get; }

    public static WordList Empty { get; } = new([], []);

    /// <summary>A list from its text.</summary>
    public static WordList Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var terms = new List<string>();
        var rejected = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = (raw.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0 ? raw[..hash] : raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (Problem(line) is null)
            {
                var term = Term(line);
                if (!terms.Contains(term))
                {
                    terms.Add(term);
                }
            }
            else
            {
                rejected.Add(line);
            }
        }
        return new WordList(terms, rejected);
    }

    /// <summary>A list of these terms (each must be one: <see cref="Problem"/>).</summary>
    public static WordList Of(IEnumerable<string> terms) => new([.. terms.Select(Term).Distinct()], []);

    /// <summary>The term as a list keeps it: lower case, one space between words.</summary>
    public static string Term(string written) =>
        string.Join(' ', written.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Why the text is no term, or null: 2 to 40 characters, letters a-z, digits and spaces, a star at
    /// either end only.</summary>
    public static string? Problem(string written)
    {
        var term = Term(written ?? "");
        var core = term.Trim('*');
        return term.Length is < 2 or > 40 ? "a term is 2 to 40 characters"
            : core.Any(c => !(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or ' ')) ? "a term is letters a-z, digits and spaces (a star at either end)"
            : core.Count(c => c != ' ') < 2 || core.StartsWith(' ') || core.EndsWith(' ') ? "a term has two letters or more"
            : null;
    }

    /// <summary>The list with a term more (the same list when it has it).</summary>
    public WordList With(string term) => Terms.Contains(Term(term)) ? this : new WordList([.. Terms, Term(term)], []);

    /// <summary>The list without a term.</summary>
    public WordList Without(string term) => new([.. Terms.Where(t => t != Term(term))], []);

    /// <summary>The list as text, a term a line.</summary>
    public string Text(string? heading = null) =>
        (heading is null ? "" : string.Concat(heading.Split('\n').Select(l => $"# {l}\n"))) + string.Concat(Terms.Select(t => t + "\n"));

    /// <summary>The first term found in the text, or null.</summary>
    public string? First(string text)
    {
        var folded = TextFold.Of(text);
        try
        {
            return _pattern?.Match(folded.Text) is { Success: true } m ? TermOf(m) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return TooSlow;
        }
    }

    /// <summary>Every place a term is found: where in the text as written, and the term.</summary>
    public IReadOnlyList<(int Start, int Length, string Term)> Find(string text)
    {
        var folded = TextFold.Of(text);
        var found = new List<(int, int, string)>();
        try
        {
            for (var m = _pattern?.Match(folded.Text); m is { Success: true }; m = m.NextMatch())
            {
                if (m.Length > 0)
                {
                    var (start, length) = folded.Original(m.Index, m.Length);
                    found.Add((start, length, TermOf(m)));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [(0, text.Length, TooSlow)];
        }
        return found;
    }

    /// <summary>The text with every character of each term found but spaces written as '*'.</summary>
    public string Masked(string text)
    {
        var found = Find(text);
        if (found.Count == 0)
        {
            return text;
        }
        var masked = new StringBuilder(text);
        foreach (var (start, length, _) in found)
        {
            for (var i = start; i < start + length; i++)
            {
                if (!char.IsWhiteSpace(masked[i]))
                {
                    masked[i] = '*';
                }
            }
        }
        return masked.ToString();
    }

    private string TermOf(Match m)
    {
        for (var i = 0; i < Terms.Count; i++)
        {
            if (m.Groups[$"t{i}"].Success)
            {
                return Terms[i];
            }
        }
        return Terms[0];
    }

    // ---- a term's pattern ----

    private const string NotWord = @"(?<![\p{L}\p{N}])";

    private const string WordEnds = @"(?![\p{L}\p{N}])";

    /// <summary>Between two letters of a word: punctuation and signs (not a space, and none of the signs that stand
    /// for a letter).</summary>
    private const string Between = @"[^\p{L}\p{N}\s!|@$+]*";

    /// <summary>Between two letters spaced out one by one: a space at least, and punctuation. What comes before the
    /// first space takes no space itself, so a run splits around a space one way only: with a space on both sides
    /// of it, every split of each run was tried again for every later run, and a line of a word's letters with long
    /// runs between them, short of its last letter, took the whole time allowed (security audit 030, S2).</summary>
    private const string Spaced = @"[^\p{L}\p{N}\s!|@$+]*\s[^\p{L}\p{N}!|@$+]*";

    /// <summary>Between the words of a phrase: anything but letters and digits, or nothing.</summary>
    private const string Gap = @"[^\p{L}\p{N}]*";

    private const string Plural = "[s5$z]*";

    private const string WordStart = @"[\p{L}\p{N}]*?";

    private const string WordRest = @"[\p{L}\p{N}]*";

    private static string Pattern(string term)
    {
        var inside = term.StartsWith('*');
        var insideAfter = term.EndsWith('*');
        var words = term.Trim('*').Split(' ');
        var written = string.Join(Gap, words.Select(w => string.Join(Between, w.Select(c => Class(c) + "+"))));
        if (words.Length == 1 && words[0].Length >= 3)
        {
            written = $"(?:{written}|{string.Join(Spaced, words[0].Select(Class))})";
        }
        // a term found inside a longer word takes the whole word (masked whole: "motherfucker")
        return NotWord + (inside ? WordStart : "") + written + (insideAfter ? WordRest : Plural + WordEnds);
    }

    /// <summary>The characters a letter of a term may be written as.</summary>
    private static string Class(char c) => c switch
    {
        'a' => "[a4@]",
        'b' => "[b8]",
        'e' => "[e3]",
        'g' => "[g96]",
        'i' => "[i1!|]",
        'l' => "[l1|]",
        'o' => "[o0]",
        's' => "[s5$]",
        't' => "[t7+]",
        'u' => "[uv]",
        'z' => "[z2]",
        _ => Regex.Escape(c.ToString()),
    };
}
