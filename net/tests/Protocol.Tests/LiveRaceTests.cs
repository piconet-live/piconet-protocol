using System.Text.Json;
using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>A race's live snapshot: the order the standings and the start grid take from it (the mode's places first,
/// else times and distances), the score it carries, and what a launcher or coordinator from before the places reads.</summary>
public sealed class LiveRaceTests
{
    private static RacerLive Racer(int seat, string state, float distance, int totalMs = 60_000, int place = 0, float? score = null) =>
        new(seat, 0, 0, 0, totalMs, null, 0, 0, 100, 0, 0, state, 0, 0, 0, distance, place, score);

    [Fact]
    public void WithoutPlacesTheFinishedLeadByTimeThenTheRacingByDistanceThenTheKnockedOut()
    {
        var racers = new[]
        {
            Racer(0, RacerLive.Racing, 9_000),
            Racer(1, RacerLive.Racing, 16_500),
            Racer(2, RacerLive.Finished, 24_000, totalMs: 190_000),
            Racer(3, RacerLive.Finished, 24_000, totalMs: 185_000),
            Racer(4, RacerLive.Out, 3_000),
            Racer(5, RacerLive.Out, 12_000),
        };
        Assert.Equal([3, 2, 1, 0, 5, 4], RaceSnapshot.Standings(racers).Select(r => r.Seat));
        Assert.False(RaceSnapshot.Placed(racers));
    }

    /// <summary>A mode that places the racers (live_places, or its end) orders the standings whatever the times and
    /// distances say: a knocked-out racer with more points stands above one still racing; a racer the mode did not
    /// place comes after the placed, by the race's own order.</summary>
    [Fact]
    public void TheModesPlacesOrderTheStandingsBeforeTimesAndDistances()
    {
        var racers = new[]
        {
            Racer(0, RacerLive.Racing, 9_000, place: 3, score: 4),
            Racer(1, RacerLive.Out, 2_000, place: 1, score: 12),
            Racer(2, RacerLive.Finished, 24_000, totalMs: 185_000, place: 4, score: 1),
            Racer(3, RacerLive.Racing, 20_000, place: 2, score: 9),
            Racer(4, RacerLive.Racing, 30_000),
            Racer(5, RacerLive.Finished, 24_000, totalMs: 170_000),
        };
        Assert.Equal([1, 3, 0, 2, 5, 4], RaceSnapshot.Standings(racers).Select(r => r.Seat));
        Assert.True(RaceSnapshot.Placed(racers));
        var snapshot = new RaceSnapshot(600, 0, racers, RaceSnapshot.Points, "POINTS");
        Assert.True(snapshot.Scored);
        Assert.True(snapshot.InRange());
        Assert.False(new RaceSnapshot(600, 0, racers).Scored);
        Assert.False(new RaceSnapshot(600, 0, racers, "laps").InRange());
        Assert.False(new RaceSnapshot(600, 0, [Racer(0, RacerLive.Racing, 0, place: 31)]).InRange());
        Assert.False(new RaceSnapshot(600, 0, [Racer(0, RacerLive.Racing, 0, score: float.NaN)]).InRange());
    }

    /// <summary>The places and scores travel in the JSON the launchers and the coordinator exchange; a snapshot
    /// written before them (a 0.6.0 game or launcher) reads with no place and no score, and a snapshot without them
    /// is written as before (no new member for an older reader to see).</summary>
    [Fact]
    public void PlacesAndScoresTravelInTheJsonAndAnOlderSnapshotReadsWithout()
    {
        var sent = new RaceSnapshot(600, 1, [Racer(0, RacerLive.Racing, 500, place: 2, score: 3), Racer(1, RacerLive.Racing, 900, place: 1, score: 7.5f)],
            RaceSnapshot.Metres);
        var text = JsonSerializer.Serialize(sent, ControlProtocol.Json);
        var back = JsonSerializer.Deserialize<RaceSnapshot>(text, ControlProtocol.Json)!;
        Assert.Equal(sent, back with { Racers = sent.Racers });
        Assert.Equal(sent.Racers, back.Racers);
        Assert.DoesNotContain("scoreLabel", text, StringComparison.Ordinal);

        var old = JsonSerializer.Deserialize<RaceSnapshot>(
            """{"frame":600,"localSeat":1,"racers":[{"seat":0,"rank":1,"lap":0,"lapMs":0,"totalMs":0,"bestLapMs":null,"speed":0,"energy":0,"maxEnergy":100,"kos":0,"topSpeed":0,"state":"racing","x":0,"y":0,"z":0,"distance":500}]}""",
            ControlProtocol.Json)!;
        Assert.Null(old.ScoreKind);
        Assert.Equal((0, null), (old.Racers[0].Place, old.Racers[0].Score));
        Assert.False(old.Scored);

        var plain = JsonSerializer.Serialize(new RaceSnapshot(1, 0, [Racer(0, RacerLive.Racing, 1)]), ControlProtocol.Json);
        Assert.DoesNotContain("score", plain, StringComparison.Ordinal);
    }
}
