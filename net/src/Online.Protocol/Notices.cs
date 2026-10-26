using System.Diagnostics.CodeAnalysis;

namespace Online.Protocol;

/// <summary>
/// A notice from the server's operator to connected players: a <see cref="ChatMessage"/> on the global channel from
/// <see cref="ServerId"/>, with its tier in <see cref="ChatMessage.Level"/>. Four tiers, named after syslog's levels,
/// each doing what the one before it does and more: <see cref="Info"/> (a chat line), <see cref="Notice"/> (a
/// banner for a while), <see cref="Warning"/> (a banner until dismissed; the taskbar flashes), <see cref="Urgent"/>
/// (and a short sound). A launcher that knows no <c>level</c> (0.2.0, 0.2.1) shows the text as it is, so the text
/// carries the tier's word in front (<see cref="Prefixed"/>), which a launcher that knows it takes off again.
/// </summary>
public static class Notices
{
    /// <summary>The sender id of every notice. The coordinator's player ids are "p1", "p2"..., so no player ever
    /// has it; a launcher knows the server by this id, never by a name (names are not unique).</summary>
    public const string ServerId = "server";

    /// <summary>The name a notice is sent under (<see cref="ChatMessage.FromName"/>): what a launcher that knows no
    /// notices shows in front of it.</summary>
    public const string SenderName = "SYSTEM";

    /// <summary>The names no player may take, in any capitals and with spaces around (the server refuses them,
    /// bad_name): on a launcher that knows no notices, a player's line under one of them would look like one. The
    /// third is the service's own name. It is written here, outside the launcher's
    /// Branding.cs where the name otherwise lives, because the server must refuse it and may not refer to the
    /// launcher (Online.Protocol is the only code both share); a rename changes both places.</summary>
    public static IReadOnlyList<string> ReservedNames { get; } = [SenderName, "SERVER", "PICONET"];

    public static bool Reserved(string? name) =>
        name is not null && ReservedNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    public const string Info = "info";
    public const string Notice = "notice";
    public const string Warning = "warning";
    public const string Urgent = "urgent";

    /// <summary>The tiers, least first.</summary>
    public static IReadOnlyList<string> Levels { get; } = [Info, Notice, Warning, Urgent];

    public static bool Known([NotNullWhen(true)] string? level) => level is not null && Levels.Contains(level, StringComparer.Ordinal);

    /// <summary>The text as it goes on the wire: the tier's word in capitals in front ("URGENT: ..."), nothing for
    /// <see cref="Info"/>.</summary>
    public static string Prefixed(string level, string text) => Prefix(level) + text;

    /// <summary>The operator's text of a notice's wire text: the tier's word taken off when it is there.</summary>
    public static string Unprefixed(string level, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var prefix = Prefix(level);
        return prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
    }

    private static string Prefix(string level) => level == Info || !Known(level) ? "" : level.ToUpperInvariant() + ": ";
}
