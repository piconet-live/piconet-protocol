using System.Globalization;

namespace Online.Protocol.Chat;

/// <summary>
/// Why the server did not deliver a chat line: the codes of its <see cref="ErrorMessage"/>, and the label a player is
/// shown (also the message's text, which a launcher from before the filter shows as it is).
/// </summary>
public static class ChatRefusal
{
    /// <summary>A word of the always-blocked list.</summary>
    public const string Blocked = "chat_blocked";

    /// <summary>A link the server does not allow.</summary>
    public const string Link = "chat_link";

    /// <summary>The same as one of the player's last lines.</summary>
    public const string Repeat = "chat_repeat";

    /// <summary>More lines than the server takes in its window.</summary>
    public const string TooFast = "chat_too_fast";

    /// <summary>The player is muted: <see cref="ErrorMessage.Seconds"/> how long still; 0, the mute is over (lifted by
    /// the operator).</summary>
    public const string Muted = "chat_muted";

    /// <summary>One of the codes above.</summary>
    public static bool Is(string? code) => code is Blocked or Link or Repeat or TooFast or Muted;

    /// <summary>The label for a refusal: "Not sent", "Not sent · no links", "Muted for 4:32", "Muted" no more.</summary>
    public static string Label(string code, int? seconds = null) => code switch
    {
        Link => "Not sent · no links",
        Repeat => "Not sent · said already",
        TooFast => "Not sent · too fast",
        Muted when seconds is > 0 and var left => "Muted for " + Clock(left),
        Muted => "You can chat again",
        _ => "Not sent",
    };

    /// <summary>Seconds as minutes and seconds, 4:32.</summary>
    public static string Clock(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
}
