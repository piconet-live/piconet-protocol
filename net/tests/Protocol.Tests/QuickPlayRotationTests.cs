using Online.Protocol;
using TestSupport;

namespace Protocol.Tests;

/// <summary>Quick Play's rotation (QuickPlayRotation) over protocol/tests/vectors/quickplay/, a rotation of today's
/// words, and the official protocol/quickplay/ as far as today's compiler takes it: the cards of a vote, Random's draw,
/// the laps and the CPU flag, what a file that does not load says, and the messages that carry a mode.</summary>
public sealed class QuickPlayRotationTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rotation-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Vectors => RepositoryFolder.Protocol("protocol", "tests", "vectors", "quickplay");

    private static QuickPlayRotation Rotation() => QuickPlayRotation.Read(Vectors);

    /// <summary>The vectors' rotation copied to a folder of the test's own, its file replaced by <paramref name="toml"/>.</summary>
    private string WithFile(string toml)
    {
        foreach (var file in Directory.GetFiles(Vectors, "*.fzmode"))
        {
            File.Copy(file, Path.Combine(_folder, Path.GetFileName(file)), overwrite: true);
        }
        File.WriteAllText(Path.Combine(_folder, QuickPlayRotation.FileName), toml);
        return _folder;
    }

    [Fact]
    public void TheVectorsRotationReads()
    {
        var rotation = Rotation();
        Assert.Equal(5, rotation.Cards);
        Assert.Equal(1, rotation.SpicyCards);
        Assert.Equal(0.15, rotation.RandomSpicyChance);
        Assert.Equal("Quick Play", rotation.Normal.Package.Name);
        var spicy = Assert.Single(rotation.Spicy);
        Assert.Equal("OVERDRIVE", spicy.Mode.Package.Badge);
        Assert.Equal([8], spicy.Courses!);
        Assert.Equal(1, rotation.Api);
        Assert.True(rotation.CpusAllowed);
    }

    [Fact]
    public void EveryVoteHasFiveCardsOneSpicyAndRandom()
    {
        var rotation = Rotation();
        var random = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            var vote = rotation.Vote(CoursePool.Default, random.Next);
            Assert.Equal(6, vote.Count);
            Assert.Equal(CourseVote.RandomCourse, vote[^1].Course);
            var spicy = Assert.Single(vote, o => o.Spicy);
            Assert.Equal(8, spicy.Course);
            Assert.Equal("OVERDRIVE", spicy.Card.Badge);
            Assert.Equal(0, spicy.Card.Laps);   // laps unlimited
            Assert.Equal(5, vote.Take(5).Select(o => o.Course).Distinct().Count());
            Assert.All(vote.Take(5).Where(o => !o.Spicy), o => Assert.Equal((3, (string?)null), (o.Card.Laps!.Value, o.Card.Badge)));
            Assert.Null(vote[^1].Card.Laps);
        }
    }

    [Fact]
    public void TheSpicyCardTakesEveryPlace()
    {
        var rotation = Rotation();
        var random = new Random(3);
        var places = Enumerable.Range(0, 400).Select(_ => rotation.Vote(CoursePool.Default, random.Next).ToList().FindIndex(o => o.Spicy));
        Assert.Equal([0, 1, 2, 3, 4], places.Distinct().Order());
    }

    [Fact]
    public void APoolWithoutTheSpicyCourseShowsNormalCardsOnly()
    {
        var pool = CoursePool.Default.Where(c => c != 8).ToList();
        var vote = Rotation().Vote(pool, new Random(1).Next);
        Assert.Equal(6, vote.Count);
        Assert.DoesNotContain(vote, o => o.Spicy);
        Assert.All(vote.Take(5), o => Assert.Contains(o.Course, pool));
    }

    [Fact]
    public void RandomLandsOnSpicyAtItsChance()
    {
        var rotation = Rotation();
        var random = new Random(11);
        var draws = Enumerable.Range(0, 20_000).Select(_ => rotation.Random(CoursePool.Default, random.Next)).ToList();
        var spicy = draws.Count(d => d.Spicy) / (double)draws.Count;
        Assert.InRange(spicy, 0.13, 0.17);
        Assert.All(draws.Where(d => d.Spicy), d => Assert.Equal(8, d.Course));
        Assert.All(draws.Where(d => !d.Spicy), d => Assert.Same(rotation.Normal, d.Mode));
    }

    [Fact]
    public void AChanceOfNoneNeverLandsOnSpicy()
    {
        var rotation = QuickPlayRotation.Read(WithFile("""
            random_spicy_chance = 0
            [normal]
            mode = "competitive.fzmode"
            [[spicy]]
            mode = "overdrive.fzmode"
            courses = [8]
            """));
        var random = new Random(5);
        Assert.DoesNotContain(Enumerable.Range(0, 2000).Select(_ => rotation.Random(CoursePool.Default, random.Next)), d => d.Spicy);
    }

    [Fact]
    public void TheMatchStartCarriesTheModeOnItsDefaults()
    {
        var spicy = Rotation().Spicy[0].Mode;
        var start = spicy.Start();
        Assert.Equal(spicy.Hash, start.Hash);
        Assert.Equal([10], start.Params);
        Assert.Equal(spicy.Hash, ModePackage.Hash(Convert.FromBase64String(start.Package)));
    }

    [Theory]
    [InlineData("leaderboards = true\n[normal]\nmode = \"competitive.fzmode\"", "never has leaderboards")]
    [InlineData("cards = 5", "no [normal] option")]
    [InlineData("votes = 3\n[normal]\nmode = \"competitive.fzmode\"", "'votes' is not a key")]
    [InlineData("random_spicy_chance = 1.5\n[normal]\nmode = \"competitive.fzmode\"", "a chance from 0 to 1")]
    [InlineData("[normal]\nmode = \"missing.fzmode\"", "missing.fzmode is not there")]
    [InlineData("[normal]\nmode = \"../competitive.fzmode\"", "is not a mode file beside it")]
    [InlineData("[normal]\nmode = \"competitive.fzmode\"\ncourses = [2]", "2 is not a course of the pool")]
    [InlineData("[normal]\nmode = \"competitive.fzmode\"", "spicy_cards is 1, and there is no [[spicy]] option")]
    public void AFileThatDoesNotLoadSaysWhy(string toml, string says)
    {
        var error = Assert.Throws<InvalidDataException>(() => QuickPlayRotation.Read(WithFile(toml)));
        Assert.Contains(says, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModeThatDoesNotCompileIsNamedWithItsLine()
    {
        var folder = WithFile("spicy_cards = 0\n[normal]\nmode = \"broken.fzmode\"");
        File.WriteAllText(Path.Combine(folder, "broken.fzmode"), "mode \"Broken\" {\n  api 1;\n}\n\nlaps 100;\n");
        var error = Assert.Throws<InvalidDataException>(() => QuickPlayRotation.Read(folder));
        Assert.StartsWith("broken.fzmode line 5: ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The official rotation is the target, not yet loadable: its first refusal is the compiler's, knockout's
    /// show_number (a display word, feat/mode-words-display). When this fails, the compiler took it: the official
    /// rotation's tests take this one's place.</summary>
    [Fact]
    public void TheOfficialRotationWaitsForShowNumber()
    {
        var error = Assert.Throws<InvalidDataException>(() => QuickPlayRotation.Read(RepositoryFolder.Protocol("protocol", "quickplay")));
        Assert.StartsWith("knockout.fzmode line 43: ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Quick Play's own end stays: a mode's race has no finish limit unless it states it.</summary>
    [Fact]
    public void TheOfficialNormalModeStatesTheFinishLimit()
    {
        var compiled = ModeCompiler.Compile(File.ReadAllText(RepositoryFolder.Protocol("protocol", "quickplay", "competitive.fzmode")));
        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Errors));
        var terms = ModeTerms.Of(compiled.Mode!);
        Assert.Equal(90, terms.FinishLimit);
        Assert.True(terms.CpusAllowed);
        Assert.Equal(8, terms.Laps(CourseTable.Courses.First(c => c.Track == "Sonic Oval").Course));
    }

    [Fact]
    public void AModeTravelsInTheMessagesAndAnOlderEndReadsNone()
    {
        var spicy = Rotation().Spicy[0].Mode;
        var setup = new RaceSetup(8, 0, [], 1, 2, Mode: spicy.Start());
        var start = ControlProtocol.Encode(new MatchStart("m1", new RelayEndpoint("r", "h", 1), "t", 0, setup));
        Assert.Contains("\"mode\":{\"package\":", start, StringComparison.Ordinal);
        var back = Assert.IsType<MatchStart>(ControlProtocol.DecodeServer(start));
        Assert.Equal(spicy.Hash, back.Setup.Mode!.Hash);
        Assert.DoesNotContain("\"mode\"", ControlProtocol.Encode(new MatchStart("m1", new RelayEndpoint("r", "h", 1), "t", 0,
            new RaceSetup(8, 3, [], 1, 2))), StringComparison.Ordinal);

        var vote = ControlProtocol.Encode(new CourseVote("m1", [8, CourseVote.RandomCourse], 10_000,
            [spicy.Card(8, spicy: true), new VoteCard("Quick Play", null)]));
        Assert.Contains("\"cards\":[{\"mode\":\"Overdrive\",\"laps\":0,\"badge\":\"OVERDRIVE\"", vote, StringComparison.Ordinal);
        Assert.Equal("OVERDRIVE", Assert.IsType<CourseVote>(ControlProtocol.DecodeServer(vote)).Cards![0].Badge);
        Assert.DoesNotContain("cards", ControlProtocol.Encode(new CourseVote("m1", [8], 10_000)), StringComparison.Ordinal);

        Assert.Contains("\"modes\":1", ControlProtocol.Encode(new Hello("p", ControlProtocol.Version, Modes: 1)), StringComparison.Ordinal);
        Assert.DoesNotContain("modes", ControlProtocol.Encode(new Hello("p", ControlProtocol.Version)), StringComparison.Ordinal);
        Assert.Equal(0, Assert.IsType<Hello>(ControlProtocol.DecodeClient($$"""{"type":"hello","name":"p","version":{{ControlProtocol.Version}}}""")).Modes);
    }
}
