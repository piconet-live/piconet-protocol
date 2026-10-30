namespace Online.Protocol;

/// <summary>
/// A moment of a race as the game simulates it, every racer: what the launcher shows during a race (its race page)
/// and what a lobby's waiting players watch. The Dolphin fork sends it to its launcher every frame (JSON
/// over UDP on this computer, Core.FzgxLive); some racers' launchers pass it on to the coordinator
/// (<see cref="RaceReport"/>), which sends it to the players waiting in the lobby (<see cref="LiveRace"/>). Every game
/// simulates every machine, so any racer's game describes the whole race; rollback can correct the other racers'
/// numbers a moment later.
/// </summary>
/// <param name="Frame">The race frame (60 a second) since go.</param>
/// <param name="LocalSeat">The seat of the game that sent it.</param>
public sealed record RaceSnapshot(int Frame, int LocalSeat, IReadOnlyList<RacerLive> Racers)
{
    /// <summary>Every number is one a game can send (<see cref="RacerLive.InRange"/>): what the coordinator takes
    /// from a launcher. A number written too large for its type is read as infinity, which cannot be written again.</summary>
    public bool InRange() => Frame >= 0 && LocalSeat is >= 0 and < RacerLive.Seats && Racers.All(r => r.InRange());

    /// <summary>The racers in race order: the finished by their time, then the racing by the distance covered, then
    /// the knocked out (by distance); seat order between equals. The launcher's standings and the coordinator's
    /// last finish (the start grid of the lobby's next race) are both this order.</summary>
    public static IReadOnlyList<RacerLive> Standings(IEnumerable<RacerLive> racers) =>
        [.. racers.OrderBy(r => r.State switch { RacerLive.Finished => 0, RacerLive.Racing => 1, _ => 2 })
            .ThenBy(r => r.State == RacerLive.Finished ? r.TotalMs : 0)
            .ThenByDescending(r => r.Distance)
            .ThenBy(r => r.Seat)];
}

/// <summary>
/// One racer at a moment of the race. Times in milliseconds; the position is the machine's in the course (the game's
/// X, Y, Z), for the track map.
/// </summary>
/// <param name="Lap">Laps completed.</param>
/// <param name="LapMs">The current lap's time.</param>
/// <param name="TotalMs">The race time so far (the finish time once finished).</param>
/// <param name="BestLapMs">The best completed lap; null before the first.</param>
/// <param name="Speed">km/h.</param>
/// <param name="Energy">Energy, 0 to <paramref name="MaxEnergy"/>.</param>
/// <param name="Kos">Machines this racer knocked out.</param>
/// <param name="TopSpeed">The race's top speed so far, km/h.</param>
/// <param name="State">racing, finished or out (knocked out or retired).</param>
/// <param name="Rank">The game's ranking byte (progress record +0x115); not the race position: the standings come from
/// <paramref name="Distance"/> and the finish times (Launcher.Core RaceStandings).</param>
/// <param name="Distance">Distance covered over all laps (progress record +0xF8).</param>
public sealed record RacerLive(
    int Seat, int Rank, int Lap, int LapMs, int TotalMs, int? BestLapMs, float Speed, float Energy, float MaxEnergy,
    int Kos, float TopSpeed, string State, float X, float Y, float Z, float Distance = 0)
{
    public const string Racing = "racing";
    public const string Finished = "finished";
    public const string Out = "out";

    /// <summary>A race's seats: 0 to 29.</summary>
    public const int Seats = 30;

    /// <summary>No time is longer: a day (the game's own clock stops at 255'255"65535, a little over four hours).</summary>
    public const int MaxMs = 86_400_000;

    /// <summary>No count (the ranking byte, laps, knockouts) is higher (the game's own are a byte or sixteen bits).</summary>
    public const int MaxCount = 65_535;

    /// <summary>No speed, energy, distance or coordinate is further from zero: far beyond any the game reaches (speeds
    /// are thousands of km/h, a course a few tens of thousands of units across), and far inside what a float holds.</summary>
    public const float MaxMagnitude = 1e9f;

    /// <summary>Every number is finite and within the bounds above (the game writes a value that is not finite as 0).</summary>
    public bool InRange() =>
        Seat is >= 0 and < Seats && Rank is >= 0 and <= MaxCount && Lap is >= 0 and <= MaxCount && Kos is >= 0 and <= MaxCount &&
        LapMs is >= 0 and <= MaxMs && TotalMs is >= 0 and <= MaxMs && BestLapMs is null or (>= 0 and <= MaxMs) &&
        Number(Speed) && Number(Energy) && Number(MaxEnergy) && Number(TopSpeed) && Number(X) && Number(Y) && Number(Z) &&
        Number(Distance);

    private static bool Number(float v) => float.IsFinite(v) && MathF.Abs(v) <= MaxMagnitude;
}

/// <summary>A lobby's race, for its members (racing or waiting): the match, the course, the laps and the seats (names,
/// machines, colours; CPU seats too), and when it started. <paramref name="Over"/>: its relay says the race is over (a
/// game's race-over test said so): its racers may still be on the results, but no game comes back into it (Rejoin);
/// a launcher from before it passes over it. <paramref name="Teams"/>: each seat's team when the race is a mode's with
/// teams, as its <c>setup.mode.teams</c>; null: none.</summary>
public sealed record LobbyRace(string MatchId, int Course, int Laps, IReadOnlyList<SeatSetup> Seats, DateTimeOffset Started,
    bool Over = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Teams = null);
