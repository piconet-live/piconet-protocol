namespace Online.Protocol.Chat;

/// <summary>
/// The word lists the repository ships (Chat/blocked.txt, Chat/swears.txt, built into this library): the server's
/// always-blocked words, its default (a server's operator changes its own live), and the swear words a launcher masks
/// for a player who hides them. Both were picked from dsojevic/profanity-list, MIT (<see cref="Licence"/>).
/// </summary>
public static class ChatWords
{
    /// <summary>The text of the always-blocked list as shipped.</summary>
    public static string BlockedText { get; } = Read("blocked.txt");

    /// <summary>The text of the swear words as shipped.</summary>
    public static string SwearsText { get; } = Read("swears.txt");

    /// <summary>The licence of the list the two were picked from.</summary>
    public static string Licence { get; } = Read("LICENSE-profanity-list.txt");

    /// <summary>The always-blocked list as shipped.</summary>
    public static WordList Blocked { get; } = WordList.Parse(BlockedText);

    /// <summary>What a launcher masks: the swear words and the always-blocked words (a server of another build may
    /// block other words, or none).</summary>
    public static WordList Masked { get; } = WordList.Parse(SwearsText + "\n" + BlockedText);

    private static string Read(string name)
    {
        using var stream = typeof(ChatWords).Assembly.GetManifestResourceStream("Online.Protocol.Chat." + name)
            ?? throw new InvalidOperationException($"the word list {name} is not built in");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
