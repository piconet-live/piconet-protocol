namespace Online.Protocol;

/// <summary>
/// Player names as the game can show them: the results screens' font draws A-Z (lower case
/// as upper case), 0-9, space, '.', '-' and '&amp;' (stored as ','); any other ASCII character draws as a blank and a
/// byte above 0x7F ends the name; a row fits 16 characters. The launcher's name field, the coordinator and the Dolphin
/// fork (FzgxBoot GameText) apply the same rule.
/// </summary>
public static class PlayerNames
{
    public const int MaxLength = 16;

    /// <summary>A character the game draws in a name.</summary>
    public static bool IsAllowed(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or ' ' or '.' or '-' or '&';

    /// <summary>The name as the game shows it: allowed characters only, in upper case, spaces collapsed and trimmed,
    /// at most <see cref="MaxLength"/> characters.</summary>
    public static string Shown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = name.Where(c => IsAllowed(c)).Select(char.ToUpperInvariant).ToArray();
        var text = string.Join(' ', new string(words).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= MaxLength ? text : text[..MaxLength].TrimEnd();
    }

    /// <summary>A name the coordinator accepts: 1 to 16 characters, all of them ones the game draws.</summary>
    public static bool IsValid(string name) =>
        name is { Length: >= 1 and <= MaxLength } && name.All(IsAllowed) && name.Trim().Length > 0;
}
