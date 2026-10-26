namespace Online.Protocol;

/// <summary>A garage-built machine: a body, a cockpit and a booster from the
/// game's 75 parts, the pilot who drives it, and each part's colour. The game makes its stats, model and name from the
/// three parts alone; its name is the game's own rule, never typed. Emblems are not
/// carried. The game takes a part only of its own kind (an id of another kind is read past its tables: fgx-re's
/// exploit), so every side checks <see cref="Problem"/> before passing one on, and the game checks again.</summary>
/// <param name="Body">The body's part id, 0-24.</param>
/// <param name="Cockpit">The cockpit's part id, 25-49.</param>
/// <param name="Booster">The booster's part id, 50-74.</param>
/// <param name="Pilot">The pilot: the stock machine 0-40 whose pilot drives it (as the game's garage record holds it).</param>
/// <param name="Colors">Each part's colour word as the game's record holds it: red, green, blue, then the paint slot,
/// which is always <see cref="GameFacts.Files.PartPaintSlot"/> (the slot of the meshes the colour paints; another
/// paints nothing); null, or 0 for a part: the part's stock colour 0.</param>
public sealed record CustomMachine(int Body, int Cockpit, int Booster, int Pilot, IReadOnlyList<uint>? Colors = null)
{
    public const int Bodies = 0, Cockpits = 25, Boosters = 50, Parts = 75, LastPilot = 40;

    /// <summary>Why the game cannot build it, as a short sentence; null: it can.</summary>
    public string? Problem() =>
        Body is < Bodies or >= Cockpits ? "the body is not one of the 25 bodies (0-24)"
        : Cockpit is < Cockpits or >= Boosters ? "the cockpit is not one of the 25 cockpits (25-49)"
        : Booster is < Boosters or >= Parts ? "the booster is not one of the 25 boosters (50-74)"
        : Pilot is < 0 or > LastPilot ? "the pilot is not one of the 41 (0-40)"
        : Colors is { Count: not 3 } ? "three colours, one a part, or none"
        : Colors is not null && Colors.Any(c => c != 0 && (c & 0xFF) != GameFacts.Files.PartPaintSlot)
            ? "a colour's last byte is not the paint slot (1): it would paint nothing"
        : null;

    /// <summary>The machine when the game can build it; otherwise null (a seat then races its stock machine).</summary>
    public static CustomMachine? Checked(CustomMachine? machine) => machine?.Problem() is null ? machine : null;
}
