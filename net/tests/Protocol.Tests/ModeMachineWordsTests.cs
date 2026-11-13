using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The words that set racers' machines (setup words 58 to 63, <c>machine ...</c>): what the compiler takes and
/// refuses, the terms the coordinator reads (<see cref="ModeTerms.MachineRule"/>), each seat's machine as
/// <see cref="ModeMachines.Of"/> decides it, and the rule lines a vote shows.</summary>
public sealed class ModeMachineWordsTests
{
    private const string Header = "mode \"T\" { api 0.6.5; }\n";

    private static ModeCompilation Compile(string body) => ModeCompiler.Compile(Header + body);

    private static ModeTerms Terms(string body)
    {
        var compiled = Compile(body);
        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Errors));
        return ModeTerms.Of(compiled.Mode!);
    }

    private static string Error(string body) => Assert.Single(Compile(body).Errors);

    // the machines' ids, as GameMachines names them
    private const int RedGazelle = 0, WhiteCat = 1, GoldenFox = 2, FireStingray = 4, BlueFalcon = 6, BlackBull = 29;

    public static TheoryData<string, ModeMachineWay, int[]> Forms => new()
    {
        { "machine \"Blue Falcon\";", ModeMachineWay.Fixed, [BlueFalcon] },
        { "machine random;", ModeMachineWay.Random, [] },
        { "machine random each;", ModeMachineWay.RandomEach, [] },
        { "machine by seat [\"Blue Falcon\", own, random, \"Black Bull\"];", ModeMachineWay.BySeat, [BlueFalcon, ModeMachines.Own, ModeMachines.Random, BlackBull] },
        { "teams 2..4;\nmachine by team [\"Blue Falcon\", \"Fire Stingray\"];", ModeMachineWay.ByTeam, [BlueFalcon, FireStingray] },
        { "machine by place [\"Black Bull\", \"Black Bull\", own];", ModeMachineWay.ByPlace, [BlackBull, BlackBull, ModeMachines.Own] },
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public void EachFormCompilesToItsRule(string body, ModeMachineWay way, int[] entries)
    {
        var rule = Terms(body).MachineRule!;
        Assert.Equal(way, rule.Way);
        Assert.Equal(entries, rule.Entries);
    }

    /// <summary>The coordinator's alone: no word of them reaches the package's setup, so a game of 0.6.0 races the mode
    /// as it is; left out, each racer's own.</summary>
    [Fact]
    public void TheWordsAreTheCoordinatorsAndLeftOutEachRacerRacesTheirOwn()
    {
        var compiled = Compile("machine random;");
        Assert.Empty(compiled.Mode!.Setups);
        Assert.Equal(ModeSetup.MachineRandom, Assert.Single(compiled.Mode.LobbyWords).Id);
        Assert.Empty(ModePackage.Read(compiled.Package!).Setups);
        Assert.Null(Terms("").MachineRule);
        Assert.Null(Terms("machines [\"Blue Falcon\"];").MachineRule);
    }

    [Theory]
    [InlineData("machine \"Nope\";", "\"Nope\" is no machine of the game's")]
    [InlineData("machine Blue;", "machine takes a name in quotes")]
    [InlineData("machine by seat [\"Nope\"];", "\"Nope\" is no machine of the game's")]
    [InlineData("machine by seat [mine];", "machine by seat takes a machine's name in quotes, own or random, not mine")]
    [InlineData("machine by seat [3];", "machine by seat takes machines' names in quotes, own or random")]
    [InlineData("machine random;\nmachine \"Blue Falcon\";", "machine random and machine: one of them")]
    [InlineData("machine \"Blue Falcon\";\nmachine by seat [own];", "machine and machine by seat: one of them")]
    [InlineData("machine random;\nmachine random;", "machine random is set twice")]
    [InlineData("machine by team [\"Blue Falcon\"];", "machine by team gives each team its machine: a mode with it says teams")]
    [InlineData("teams 2..2;\nmachine by team [own, own, own];", "machine by team lists 3 machines, and the race has at most 2 teams")]
    [InlineData("solo on;\nmachine \"Blue Falcon\";", "a solo mode races the player's own")]
    public void AWrongFormIsRefusedWithItsLine(string body, string says)
    {
        var error = Error(body);
        Assert.StartsWith("line ", error, StringComparison.Ordinal);
        Assert.Contains(says, error, StringComparison.Ordinal);
    }

    [Fact]
    public void AListHoldsThirtyEntriesAtMost()
    {
        var thirty = string.Join(", ", Enumerable.Repeat("own", 30));
        Assert.Equal(30, Terms($"machine by seat [{thirty}];").MachineRule!.Entries.Count);
        Assert.Contains("machine by place lists at most 30 machines, one a place", Error($"machine by place [{thirty}, own];"), StringComparison.Ordinal);
    }

    /// <summary>A draw that names its turns: 0, 1, 2, ... in order, each taken modulo the count.</summary>
    private static Func<int, int> Counting()
    {
        var turn = 0;
        return n => turn++ % n;
    }

    private static readonly int[] Allowed = [RedGazelle, WhiteCat, GoldenFox];

    [Fact]
    public void OneMachineOrOneDrawRacesEverySeat()
    {
        Assert.Equal([BlueFalcon, BlueFalcon, BlueFalcon], ModeMachines.Of(new(ModeMachineWay.Fixed, [BlueFalcon]), 3, null, null, Allowed, Counting()));
        var draws = 0;
        var one = ModeMachines.Of(new(ModeMachineWay.Random, []), 4, null, null, Allowed, n => { draws++; return 2; });
        Assert.Equal([GoldenFox, GoldenFox, GoldenFox, GoldenFox], one);
        Assert.Equal(1, draws);   // one draw for the race
        Assert.Equal([RedGazelle, WhiteCat, GoldenFox, RedGazelle],
            ModeMachines.Of(new(ModeMachineWay.RandomEach, []), 4, null, null, Allowed, Counting()));
    }

    [Fact]
    public void ASeatsEntryIsItsMachineOwnAndPastTheEndAreItsOwnRandomADrawOfItsOwn()
    {
        var rule = new ModeMachineRule(ModeMachineWay.BySeat, [BlackBull, ModeMachines.Own, ModeMachines.Random, ModeMachines.Random]);
        Assert.Equal([BlackBull, null, RedGazelle, WhiteCat, null], ModeMachines.Of(rule, 5, null, null, Allowed, Counting()));
    }

    [Fact]
    public void ATeamsEntryRacesEverySeatOfTheTeam()
    {
        var rule = new ModeMachineRule(ModeMachineWay.ByTeam, [BlueFalcon, FireStingray]);
        Assert.Equal([FireStingray, BlueFalcon, FireStingray, BlueFalcon], ModeMachines.Of(rule, 4, [1, 0, 1, 0], null, Allowed, Counting()));
        Assert.Equal([BlueFalcon, null, null], ModeMachines.Of(rule, 3, [0, 2, 3], null, Allowed, Counting()));   // (a team past the list: own)
        Assert.Equal([BlueFalcon, FireStingray, null], ModeMachines.Of(rule, 3, null, null, Allowed, Counting()));   // (no teams: each seat its own number)
    }

    [Fact]
    public void APlacesEntryRacesTheLastRacesPlaceAndTheUnrankedTheirOwn()
    {
        var rule = new ModeMachineRule(ModeMachineWay.ByPlace, [BlackBull, ModeMachines.Own, BlueFalcon]);
        Assert.Equal([BlueFalcon, BlackBull, null, null, null],
            ModeMachines.Of(rule, 5, null, [2, 0, 1, null, 7], Allowed, Counting()));
        Assert.Equal([null, null], ModeMachines.Of(rule, 2, null, null, Allowed, Counting()));   // (the lobby's first race)
    }

    [Theory]
    [InlineData("machine \"Blue Falcon\";", "Everyone races Blue Falcon")]
    [InlineData("machine random;", "Everyone races one random machine")]
    [InlineData("machine random each;", "Random machines")]
    [InlineData("machine by seat [own];", "Machines by seat")]
    [InlineData("teams 2..2;\nmachine by team [own];", "Machines by team")]
    [InlineData("machine by place [own];", "Machines by last race's place")]
    public void TheVoteSaysTheWayInPlainWords(string body, string line)
    {
        Assert.Contains(line, ModeRuleLines.Of(Compile(body).Mode!));
    }
}
