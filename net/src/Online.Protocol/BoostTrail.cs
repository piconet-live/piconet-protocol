namespace Online.Protocol;

/// <summary>A racer's boost trail colours, the same on every player's screen: the flame while cruising, the flame
/// while boosting, and what a boost throws off. Picture only: the race never reads them, so a racer's choice cannot change a race.
/// Any left out (null): the game's own colour for it.</summary>
/// <param name="Normal">The flame while not boosting.</param>
/// <param name="Boost">The flame while boosting.</param>
/// <param name="Sparks">What a boost throws off: the bolts, the sparkles and the streak.</param>
public sealed record BoostTrail(Rgb? Normal = null, Rgb? Boost = null, Rgb? Sparks = null)
{
    /// <summary>Nothing chosen: the machine's own colours.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsStock => Normal is null && Boost is null && Sparks is null;

    /// <summary>The colour the game draws for a chosen one. The flame's textures are grey and added to the picture, so
    /// a dark colour would draw nothing: one whose brightest part is under
    /// <see cref="GameFacts.Limits.TrailLeastBrightness"/> keeps its hue and is lifted to that (black to a plain grey).
    /// The game's reader does the same sum (port/core/src/files_match.cpp TrailWord).</summary>
    public static Rgb Lifted(Rgb color)
    {
        System.ArgumentNullException.ThrowIfNull(color);
        var least = GameFacts.Limits.TrailLeastBrightness;
        var most = System.Math.Max(color.R, System.Math.Max(color.G, color.B));
        return most >= least ? color
            : most == 0 ? new Rgb((byte)least, (byte)least, (byte)least)
            : new Rgb((byte)(color.R * least / most), (byte)(color.G * least / most), (byte)(color.B * least / most));
    }
}
