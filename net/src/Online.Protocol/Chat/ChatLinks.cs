using System.Text.RegularExpressions;

namespace Online.Protocol.Chat;

/// <summary>
/// Links in a chat line: an address with a scheme (https://, discord://), one that starts www., or a name ending in
/// one of the common top-level domains (example.com, discord.gg/abc, t.me/x), written with look-alike characters
/// or not (<see cref="TextFold"/>). A link is allowed when it is on <see cref="Allowed"/>: a host (piconet.live,
/// and its subdomains), or a host and a path (discord.gg/abc, and what is under it).
/// </summary>
public static partial class ChatLinks
{
    /// <summary>The links found in the text, as written there (folded), without the scheme.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        var folded = TextFold.Of(text ?? "").Text;
        try
        {
            return [.. Link().Matches(folded).Select(m => Bare(m.Value))];
        }
        catch (RegexMatchTimeoutException)
        {
            return [folded];
        }
    }

    /// <summary>The links of the text that are not allowed; empty: the text may be sent.</summary>
    public static IReadOnlyList<string> NotAllowed(string text, IReadOnlyCollection<string> allowed) =>
        [.. Find(text).Where(link => !Allowed(link, allowed))];

    /// <summary>Whether a link (as <see cref="Find"/> gives it) is one of these: a host, which allows the host and
    /// its subdomains with any path, or a host and a path, which allows that path and what is under it.</summary>
    public static bool Allowed(string link, IReadOnlyCollection<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var bare = Bare(link).TrimEnd('/', '.');
        var host = bare.Split('/')[0];
        foreach (var entry in allowed.Select(e => Bare(e).TrimEnd('/')).Where(e => e.Length > 0))
        {
            if (entry.Contains('/', StringComparison.Ordinal)
                ? bare == entry || bare.StartsWith(entry + "/", StringComparison.Ordinal)
                : host == entry || host.EndsWith("." + entry, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A link or an allowed entry without its scheme and www., in lower case.</summary>
    private static string Bare(string link)
    {
        var bare = link.Trim().ToLowerInvariant();
        if (bare.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0)
        {
            bare = bare[(scheme + 3)..];
        }
        return bare.StartsWith("www.", StringComparison.Ordinal) ? bare[4..] : bare;
    }

    /// <summary>The common top-level domains, and the ones link shorteners and invitations use; not the ones that
    /// are English words a line may hold with a full stop before them (to, in, it, be, us, am, top, fun).</summary>
    private const string Domains =
        "com|net|org|gg|io|live|tv|me|ly|xyz|ru|co|uk|de|fr|jp|br|info|biz|app|dev|link|site|online|club|shop|store|gl|cc|ca|au|" +
        "nl|pl|eu|cn|kr|gov|edu|fm|ws|ai|tk|ml|ga|cf|gq|pw|su|click|stream|bid|vip|tech|website";

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:[a-z][a-z0-9+.-]{1,15}://[^\s]+|www\.[^\s]+|(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:" + Domains +
        @")(?![\p{L}\p{N}])(?:[/:?#][^\s]*)?)",
        RegexOptions.CultureInvariant, 250)]
    private static partial Regex Link();
}
