namespace Online.Protocol;

/// <summary>
/// A release line: the first two numbers of a launcher's version, "0.5" for 0.5.0, 0.5.1 and 0.5.2. The owner's rule
/// (firm): every build with the same first two numbers races together, and only a new middle number (0.6.0) parts
/// players. A launcher names its line in its hello (<see cref="Hello.Line"/>) and the coordinator puts players of one
/// line into the same lobbies and races, whatever their builds (<see cref="Hello.Build"/>). Like the build, the line
/// is the launcher's claim: that the builds of a line compute the same race is the release's to show (the gate row
/// <c>mixed</c>), not the server's.
/// <para>The text: two numbers joined by <c>.</c>, each one to four digits and without a leading zero (but 0
/// itself), so a line has one spelling. "0.0" is no line: a version 0.0.x is a launcher built with no version.</para>
/// </summary>
public static class VersionLine
{
    /// <summary>The longest line ("9999.9999").</summary>
    public const int MaxLength = 9;

    /// <summary>Whether a text is a line as a launcher names it ("0.5").</summary>
    public static bool IsLine(string? text) =>
        text is { Length: >= 3 and <= MaxLength } && text.Split('.') is [var major, var minor] && IsNumber(major) && IsNumber(minor) &&
        text != "0.0";

    /// <summary>The line of a version as a launcher's program carries it ("0.5.2", or "0.5.2+3934d68" with the
    /// commit it was built from): its first two numbers. Null: no version, a version of other than three numbers, a
    /// pre-release ("0.6.0-dev": not a release, so in no release's lobbies), or 0.0.x (built with no version).</summary>
    public static string? Of(string? version)
    {
        if (version is null)
        {
            return null;
        }
        var release = version.Split('+', 2)[0];
        return release.Split('.') is [var major, var minor, var patch] && IsNumber(patch) && $"{major}.{minor}" is var line && IsLine(line)
            ? line
            : null;
    }

    private static bool IsNumber(string part) =>
        part is { Length: >= 1 and <= 4 } && part.All(char.IsAsciiDigit) && (part == "0" || part[0] != '0');
}
