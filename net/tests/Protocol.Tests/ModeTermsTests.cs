using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>Every lobby rule as a setup word (design 5), and a mode's words over a lobby's: <see cref="ModeTerms"/>.</summary>
public sealed class ModeTermsTests
{
    private const string Header = "mode \"T\" { api 1; }\n";

    private static ModeCompilation Compile(string body) => ModeCompiler.Compile(Header + body);

    private static ModeTerms Terms(string body)
    {
        var compiled = Compile(body);
        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Errors));
        return ModeTerms.Of(compiled.Mode!);
    }

    private static string Error(string body) => Assert.Single(Compile(body).Errors);

    /// <summary>The table says which word sets each rule, and every word it names is one; only flow and the practice's
    /// rule have none (the owner: lobby flow stays out of mode files).</summary>
    [Fact]
    public void EveryRuleThatShapesARaceHasItsWord()
    {
        var flow = new[] { "course-pool", "vote-seconds", "choose-seconds", "voting-minimum", "start-countdown", "start-timeout",
            "ready-timeout", "straggler-wait", "latency-limit", "together", "relay-ceiling" };
        Assert.Equal(flow.Order(), HostRules.All.Where(r => r.SetupWord is null).Select(r => r.Name).Order());
        foreach (var rule in HostRules.All.Where(r => r.SetupWord is not null))
        {
            Assert.Contains(ModeWords.Setups, s => s.Name == rule.SetupWord || s.Name.StartsWith(rule.SetupWord + " ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void RestoreIsNoLobbyRule()
    {
        Assert.Null(HostRules.Named("restore"));
        Assert.DoesNotContain(HostRules.All, r => r.Name == "restore");
        Assert.DoesNotContain(HostRules.Race, r => r.Name == "restore");
        Assert.DoesNotContain(LobbyRules.Keys, k => k == "restore");
        Assert.DoesNotContain(LobbyRules.Settable, r => r.Name == "restore");
        Assert.Equal(RuleReach.Practice, HostRules.Restore.Reach);
        Assert.Null(HostRules.Restore.Flag);
        Assert.Contains(HostRules.Restore, HostRules.Alone);   // (a practice keeps it)
        Assert.True(new RaceRules(Restore: true).Value(HostRules.Restore) == 1);   // (old recordings keep it)
    }

    [Fact]
    public void AModeThatSaysNothingOwnsOnlyItsEnding()
    {
        var terms = Terms("");
        Assert.False(terms.CpusAllowed);
        Assert.Equal(0, terms.FinishLimit);
        Assert.Null(terms.Laps(3));
        Assert.Equal(new Dictionary<string, int> { ["finish-limit"] = 0, ["cpu-fill"] = (int)CpuFillMode.Never }, terms.Rules());
    }

    [Theory]
    [InlineData("customs off;", true, null)]
    [InlineData("", false, null)]
    [InlineData("machines [\"Blue Falcon\"];\ncustoms off;", true, 6)]
    [InlineData("machines [\"Blue Falcon\"];", false, 6)]
    public void CustomsAreAllowedUnlessTheModeSaysOffAListOrNot(string words, bool off, int? listed)
    {
        var terms = Terms(words);
        Assert.Equal(off, terms.CustomsOff);
        Assert.Equal(listed is { } machine ? [machine] : null, terms.Machines);
        Assert.DoesNotContain(terms.Rules().Keys, k => k.Contains("custom", StringComparison.Ordinal));   // (no number a host's value could change)
    }

    [Fact]
    public void CustomsOffIsTheCoordinatorsWordNotTheGames()
    {
        var mode = ModeCompiler.Compile("mode \"M\" { api 1; }\ncustoms off;\n").Mode!;
        Assert.Contains(mode.LobbyWords, w => w.Id == ModeSetup.CustomsOff);
        Assert.DoesNotContain(mode.Setups, w => w.Id == ModeSetup.CustomsOff);
    }

    [Fact]
    public void EveryWordSetsItsRule()
    {
        var terms = Terms("""
            cpus allowed;
            laps 7;
            cpu_level 1;
            boost_start on;
            finish_limit 120;
            mine_regen 1000;
            course "Mute City: Sonic Oval";
            racers 2..8;
            machines ["Blue Falcon", "Golden Fox"];
            machine_setting 80..100;
            stats "GX+";
            mixed_packs on;
            teams 2..4;
            """);
        Assert.True(terms.CpusAllowed);
        Assert.Equal(7, terms.Laps(1));
        Assert.Equal((2, 8), terms.Racers);
        Assert.Equal([2, 6], terms.Machines);
        Assert.Equal(new SettingRange(80, 100), terms.Setting);
        Assert.Equal("GX+", terms.Pack);
        Assert.Equal((2, 4), terms.Teams);
        var rules = terms.Rules();
        Assert.Equal(7, rules["laps"]);
        Assert.Equal(1, rules["cpu-level"]);
        Assert.Equal(1, rules["boost-start"]);
        Assert.Equal(120, rules["finish-limit"]);
        Assert.Equal(1000, rules["mine-regen"]);
        Assert.Equal((int)CourseSelectMode.Fixed, rules["course-select"]);
        Assert.Equal(8, rules["racers"]);
        Assert.Equal(80, rules["machine-setting"]);
        Assert.Equal(100, rules[LobbyRules.SettingHighKey]);
        Assert.Equal(1, rules["mixed-packs"]);
        Assert.False(rules.ContainsKey("cpu-fill"));   // (allowed: the lobby's vote stands)
    }

    [Fact]
    public void TheLapsTableIsNoOneNumber()
    {
        var terms = Terms("""laps by course { "Mute City: Sonic Oval": 8; else: 3; };""");
        Assert.Equal(8, terms.Laps(CourseTable.Courses.First(c => c.Track == "Sonic Oval").Course));
        Assert.Equal(3, terms.Laps(8));
        Assert.Equal(ModeTerms.LapsByCourse, terms.Rules()["laps"]);
    }

    [Fact]
    public void AWordTakesAParameter()
    {
        var compiled = Compile("""
            param most "Racers" number 2..30 default 12;
            racers 2..most;
            """);
        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Errors));
        Assert.Equal((2, 12), ModeTerms.Of(compiled.Mode!).Racers);
        Assert.Equal((2, 6), ModeTerms.Of(compiled.Mode!, [6]).Racers);
    }

    /// <summary>The coordinator's words are in the file's text, never the package's SETP: a game of today reads such a
    /// package, and the hash still tells two files apart.</summary>
    [Fact]
    public void TheCoordinatorsWordsStayOutOfThePackage()
    {
        var with = Compile("racers 2..8;\nstats \"GX+\";");
        var without = Compile("");
        Assert.Empty(with.Mode!.Setups);
        Assert.Equal(2, with.Mode.LobbyWords.Count);
        Assert.NotEqual(with.Hash, without.Hash);
        Assert.Empty(ModePackage.Read(with.Package!).Setups);
    }

    [Theory]
    [InlineData("racers 8..2;", "racers: its least is above its most")]
    [InlineData("racers 0..2;", "racers's least is 1..30")]
    [InlineData("teams 1..2;", "teams's least is 2..30")]
    [InlineData("teams 2..31;", "teams's most is 2..30")]
    [InlineData("machine_setting 50..101;", "machine_setting's most is 0..100")]
    [InlineData("machines [\"Nope\"];", "\"Nope\" is no machine of the game's")]
    [InlineData("machines [\"Blue Falcon\", \"Blue Falcon\"];", "\"Blue Falcon\" is in the list twice")]
    [InlineData("machines all;\nmachines [\"Blue Falcon\"];", "machines all and machines: one of them")]
    [InlineData("customs off;\ncustoms off;", "customs off is set twice")]
    [InlineData("course \"Nowhere\";", "\"Nowhere\" is no course of the pool")]
    [InlineData("stats 3;", "stats takes a name in quotes")]
    [InlineData("boost_start on;\nboost_start off;", "boost_start on and boost_start off: one of them")]
    [InlineData("mixed_packs on;\nmixed_packs on;", "mixed_packs on is set twice")]
    public void AWordOutOfItsLimitsIsRefusedWithItsLine(string body, string says)
    {
        var error = Error(body);
        Assert.StartsWith("line ", error, StringComparison.Ordinal);
        Assert.Contains(says, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamsAreGivenOutEvenly()
    {
        Assert.Null(ModeTeams.Assign([null, null], 0));
        Assert.Equal([0, 1, 0, 1], ModeTeams.Assign([null, null, null, null], 2));
        Assert.Equal([1, 1, 0, 0], ModeTeams.Assign([1, 1, null, null], 2));
        Assert.Equal([0, 1, 2, 0], ModeTeams.Assign([0, null, null, null], 3));
        Assert.Equal([0, 1], ModeTeams.Assign([7, null], 2));   // (a team outside the race's: as none)
        Assert.Equal(Enumerable.Range(0, 30), ModeTeams.Assign([.. Enumerable.Repeat<int?>(null, 30)], 30));
        Assert.Equal(0, ModeTeams.Kept(-3));
        Assert.Equal(2, ModeTeams.Kept(1));
        Assert.Equal(30, ModeTeams.Kept(99));
    }

    [Fact]
    public void TheTeamsOfARaceFollowTheModesWord()
    {
        var limited = Terms("teams 3..4;");
        Assert.Equal(3, limited.TeamCount(0));
        Assert.Equal(3, limited.TeamCount(2));
        Assert.Equal(4, limited.TeamCount(9));
        Assert.Equal(0, Terms("").TeamCount(0));
        Assert.Equal(5, Terms("").TeamCount(5));
    }

    [Fact]
    public void ARaceTooSmallForTheModeIsRefused()
    {
        var terms = Terms("racers 4..8;\nteams 2..2;");
        Assert.Null(terms.Refusal(4, 2));
        Assert.Contains("at least 4 racers", terms.Refusal(3, 2), StringComparison.Ordinal);
        Assert.Contains("at least 2 teams", terms.Refusal(4, 1), StringComparison.Ordinal);
        Assert.Null(Terms("").Refusal(1, 0));
    }

    [Fact]
    public void AnOlderLineNeverRacesModes()
    {
        Assert.False(VersionLine.RunsModes("0.5"));
        Assert.True(VersionLine.RunsModes("0.6"));
        Assert.True(VersionLine.RunsModes("0.10"));
        Assert.True(VersionLine.RunsModes("1.0"));
        Assert.True(VersionLine.RunsModes(null));
    }

    [Fact]
    public void ChoosingATeamTravels()
    {
        var text = ControlProtocol.Encode(new ChooseTeam(3, "p2"));
        Assert.Equal(new ChooseTeam(3, "p2"), Assert.IsType<ChooseTeam>(ControlProtocol.DecodeClient(text)));
        Assert.DoesNotContain("\"team\"", System.Text.Json.JsonSerializer.Serialize(new PlayerInfo("p", "n", 1, new Rgb(1, 2, 3)), ControlProtocol.Json), StringComparison.Ordinal);
    }

    /// <summary>A mode's terms are not news beside its name: <see cref="LobbyRules.Differing"/> leaves them out.</summary>
    [Fact]
    public void WhatTheModeOwnsIsNotAmongTheRulesThatDiffer()
    {
        var quickPlay = new LobbyRules(new Dictionary<string, int> { ["finish-limit"] = 90, ["laps"] = 3 });
        var rules = new LobbyRules(new Dictionary<string, int> { ["finish-limit"] = 0, ["laps"] = 5 },
            Mode: new LobbyMode("h", "s", "M", 1, Terms: new Dictionary<string, int> { ["finish-limit"] = 0 }));
        Assert.Equal(["laps"], rules.Differing(quickPlay));
    }

    private const string Params = """
        param n "Laps" number 2..10 step 2 default 4;
        param d "Cut every" delay 10s..60s step 5s default 30s;
        param f "Fast" switch default off;
        param c "Map" choice ["a", "b"] default "b";
        param r "Rate" number 0.5..2.0 default 1.0;
        laps n;

        """;

    private static RotationMode Mode(IReadOnlyList<int>? values = null)
    {
        var compiled = Compile(Params);
        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Errors));
        return new RotationMode("host", compiled.Mode!, compiled.Hash!, values);
    }

    private static int[] Defaults => [.. Mode().Package.Params.Select(p => p.Default)];

    /// <summary>A host's values: the words they feed, and the race's <c>setup.mode.params</c>; none, the defaults.</summary>
    [Fact]
    public void AHostsValuesReachTheTermsAndTheMatchStart()
    {
        Assert.Equal(4, Mode().Laps(1));
        Assert.Equal(Defaults, Mode().Start().Params);
        int[] values = [8, 600, 1, 0, BitConverter.SingleToInt32Bits(1.5f)];
        var chosen = Mode(values);
        Assert.Equal(8, chosen.Laps(1));
        Assert.Equal(values, chosen.Start().Params);
        Assert.Equal(Mode().Hash, chosen.Hash);   // (the same file: the values travel beside its hash)
    }

    [Theory]
    [InlineData(0, 12, "Laps")]        // past the range
    [InlineData(0, 3, "Laps")]         // off its step
    [InlineData(1, 60, "Cut every")]   // 1 s: below the range
    [InlineData(1, 601, "Cut every")]  // off its step
    [InlineData(2, 2, "Fast")]
    [InlineData(3, 2, "Map")]
    public void AValueOutsideItsDeclarationIsRefused(int index, int value, string says)
    {
        var values = Defaults;
        Assert.Null(Mode().Package.ValuesRefusal(values));
        values[index] = value;
        Assert.StartsWith(says + ":", Mode().Package.ValuesRefusal(values), StringComparison.Ordinal);
    }

    [Fact]
    public void ARealOutsideItsRangeAndAWrongCountAreRefused()
    {
        var values = Defaults;
        values[4] = BitConverter.SingleToInt32Bits(2.5f);
        Assert.StartsWith("Rate:", Mode().Package.ValuesRefusal(values), StringComparison.Ordinal);
        values[4] = BitConverter.SingleToInt32Bits(1.25f);
        Assert.Null(Mode().Package.ValuesRefusal(values));
        Assert.Contains("5 settings, not 1", Mode().Package.ValuesRefusal([4]), StringComparison.Ordinal);
    }

    [Fact]
    public void TheParametersTravelInALobbysRules()
    {
        var mode = Mode();
        var named = new LobbyMode(mode.Hash, "S", "T", 1, Params: mode.Package.Params, Values: [8, 600, 1, 0, 0]);
        var rules = new LobbyRules(new Dictionary<string, int>(), Mode: named, ModeValues: [8, 600, 1, 0, 0]);
        var back = Assert.IsType<SetLobbyRules>(ControlProtocol.DecodeClient(ControlProtocol.Encode(new SetLobbyRules(null, rules))));
        Assert.Equal([8, 600, 1, 0, 0], back.Rules.ModeValues!);
        Assert.Equal(mode.Package.Params.Select(p => (p.Label, p.Kind, p.Least, p.Most, p.Step)), back.Rules.Mode!.Params!.Select(p => (p.Label, p.Kind, p.Least, p.Most, p.Step)));
        Assert.Equal(["a", "b"], back.Rules.Mode.Params![3].Choices);
    }

    [Fact]
    public void ASoloModeIsRefusedARace()
    {
        var mode = ModeCompiler.Compile("mode \"S\" { api 1; }\nsolo;\n").Mode!;
        Assert.True(ModeTerms.Of(mode).Solo);
        Assert.Contains("raced alone", ModeTerms.Of(mode).Refusal(1, 0), StringComparison.Ordinal);
    }
}
