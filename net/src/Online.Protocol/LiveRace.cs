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
/// <param name="ScoreKind">What the racers' <see cref="RacerLive.Score"/> is, when the race's mode scores: <see cref="Points"/>
/// (the mode's show_score, under <paramref name="ScoreLabel"/>) or <see cref="Metres"/> (its show_distance, a mode scored by
/// distance). Null: no score; the standings show times and laps. A game from before 0.6.5 sends none.</param>
/// <param name="ScoreLabel">The label the game's HUD gives the points (POINTS unless the mode names its own); null with no
/// points.</param>
public sealed record RaceSnapshot(int Frame, int LocalSeat, IReadOnlyList<RacerLive> Racers,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ScoreKind = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ScoreLabel = null)
{
    /// <summary>Scores are points (whole numbers, the HUD's SCORE).</summary>
    public const string Points = "points";

    /// <summary>Scores are metres (the HUD's DISTANCE as the mode feeds it).</summary>
    public const string Metres = "metres";

    /// <summary>The most characters a score's label has (the board's field; the HUD's fit fewer).</summary>
    public const int MaxLabel = 16;

    /// <summary>Every number is one a game can send (<see cref="RacerLive.InRange"/>): what the coordinator takes
    /// from a launcher. A number written too large for its type is read as infinity, which cannot be written again.</summary>
    public bool InRange() => Frame >= 0 && LocalSeat is >= 0 and < RacerLive.Seats && Racers.All(r => r.InRange()) &&
        ScoreKind is null or Points or Metres && (ScoreLabel is null || ScoreLabel.Length <= MaxLabel);

    /// <summary>Whether the racers' scores are shown: a kind, and a racer with a score.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Scored => ScoreKind is Points or Metres && Racers.Any(r => r.Score is not null);

    /// <summary>The racers in race order. The racers the race's mode placed (<see cref="RacerLive.Place"/>, the order
    /// the game's HUD shows: by points, distance, cuts, whatever the mode scores by) come first, by their place; then
    /// the rest as a race without a mode orders them: the finished by their time, then the racing by the distance
    /// covered, then the knocked out (by distance); seat order between equals. The launcher's standings and the
    /// coordinator's last finish (the start grid of the lobby's next race) are both this order.</summary>
    public static IReadOnlyList<RacerLive> Standings(IEnumerable<RacerLive> racers) =>
        [.. racers.OrderBy(r => r.Place > 0 ? 0 : 1)
            .ThenBy(r => r.Place)
            .ThenBy(r => r.State switch { RacerLive.Finished => 0, RacerLive.Racing => 1, _ => 2 })
            .ThenBy(r => r.State == RacerLive.Finished ? r.TotalMs : 0)
            .ThenByDescending(r => r.Distance)
            .ThenBy(r => r.Seat)];

    /// <summary>Whether the mode placed the racers: any racer with a <see cref="RacerLive.Place"/>.</summary>
    public static bool Placed(IEnumerable<RacerLive> racers) => racers.Any(r => r.Place > 0);
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
/// <param name="Rank">The game's ranking byte (progress record +0x115); not the race position by itself: the standings
/// come from <paramref name="Place"/> when the mode gives one, else from <paramref name="Distance"/> and the finish
/// times (<see cref="RaceSnapshot.Standings"/>).</param>
/// <param name="Distance">Distance covered over all laps (progress record +0xF8).</param>
/// <param name="Place">The place the race's mode gives the racer, 1 first: the one the game's HUD shows (its
/// live_places order during the race, its results order once the mode ended the race). 0: none (no mode, a mode that
/// places no one, or a game from before 0.6.5): the standings order by times and distances.</param>
/// <param name="Score">The mode's number for the racer, of the snapshot's <see cref="RaceSnapshot.ScoreKind"/>: points,
/// or metres. Null: the mode shows this racer none.</param>
public sealed record RacerLive(
    int Seat, int Rank, int Lap, int LapMs, int TotalMs, int? BestLapMs, float Speed, float Energy, float MaxEnergy,
    int Kos, float TopSpeed, string State, float X, float Y, float Z, float Distance = 0, int Place = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] float? Score = null)
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
        Number(Distance) && Place is >= 0 and <= Seats && (Score is null || Number(Score.Value));

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
