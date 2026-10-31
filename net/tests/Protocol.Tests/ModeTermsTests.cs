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
        var flow = new[] { "vote-seconds", "choose-seconds", "voting-minimum", "start-countdown", "start-timeout",
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

    /// <summary>The word left out changes nothing, <c>all</c> is the whole pool, a list keeps its courses in order; none
    /// of it is a number a host's value could change, and none of it reaches the game's words.</summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("courses all;", new int[0])]
    [InlineData("courses [\"Casino Palace: Split Oval\", \"Mute City: Sonic Oval\"];", new[] { 16, 36 })]
    [InlineData("courses [\"Mute City: Sonic Oval\"];", new[] { 36 })]
    public void CoursesAreTheLobbysUnlessTheModeStatesThem(string words, int[]? listed)
    {
        var terms = Terms(words);
        Assert.Equal(listed, terms.Courses);
        Assert.Null(terms.Course);
        Assert.DoesNotContain(terms.Rules().Keys, k => k.Contains("course", StringComparison.Ordinal));
        Assert.DoesNotContain(Compile(words).Mode!.Setups, w => w.Id is ModeSetup.Courses or ModeSetup.CoursesAll);
    }

    /// <summary>The list names what the pool holds, by the names the launcher and the vote show (the story's too); a
    /// course of the game's outside the pool is refused, as <c>course</c> refuses it.</summary>
    [Fact]
    public void EveryCourseOfThePoolCanBeListedAndNoOther()
    {
        var names = string.Join(", ", CoursePool.Default.Select(c => $"\"{CourseNames.Name(c)}\""));
        Assert.Equal(CoursePool.Default.Order(), Terms($"courses [{names}];").Courses);
        var outside = CourseTable.Courses.Where(c => !CoursePool.Default.Contains(c.Course)).Select(c => $"{c.Place}: {c.Track}").FirstOrDefault()
            ?? CourseNames.Name(CourseNames.FirstStoryStage);
        Assert.Contains("is no course of the pool", Error($"courses [\"{outside}\"];"), StringComparison.Ordinal);
    }

    /// <summary>A lap table within the list compiles, with its <c>else</c>; only a list is checked (<c>courses all</c>
    /// and no word leave the table alone); a pin keeps its own way.</summary>
    [Theory]
    [InlineData("courses [\"Mute City: Sonic Oval\"];\nlaps by course { \"Mute City: Sonic Oval\": 3; else: 2; }")]
    [InlineData("courses all;\nlaps by course { \"Casino Palace: Split Oval\": 3; else: 2; }")]
    [InlineData("laps by course { \"Casino Palace: Split Oval\": 3; else: 2; }")]
    public void ALapTableWithinTheCoursesCompiles(string words) => Assert.True(Compile(words).Succeeded, string.Join("; ", Compile(words).Errors));

    [Fact]
    public void CustomsOffIsTheCoordinatorsWordNotTheGames()
    {
        var mode = ModeCompiler.Compile("mode \"M\" { api 1; }\ncustoms off;\n").Mode!;
        Assert.Contains(mode.LobbyWords, w => w.Id == ModeSetup.Customs);
        Assert.DoesNotContain(mode.Setups, w => w.Id == ModeSetup.Customs);
    }

    [Fact]
    public void EveryWordSetsItsRule()
    {
        var terms = Terms("""
            cpus on;
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
    [InlineData("customs off;\ncustoms off;", "customs is set twice")]
    [InlineData("course \"Nowhere\";", "\"Nowhere\" is no course of the pool")]
    [InlineData("courses [\"Nowhere\"];", "\"Nowhere\" is no course of the pool")]
    [InlineData("courses [];", "courses takes names in quotes")]
    [InlineData("courses [\"Mute City: Sonic Oval\", \"Mute City: Sonic Oval\"];", "\"Mute City: Sonic Oval\" is in the list twice")]
    [InlineData("courses all;\ncourses [\"Mute City: Sonic Oval\"];", "courses all and courses: one of them")]
    [InlineData("course \"Mute City: Sonic Oval\";\ncourses [\"Mute City: Sonic Oval\"];", "course and courses: one of them")]
    [InlineData("courses [\"Mute City: Sonic Oval\"];\ncourse \"Mute City: Sonic Oval\";", "courses and course: one of them")]
    [InlineData("course \"Mute City: Sonic Oval\";\ncourses all;", "course and courses all: one of them")]
    [InlineData("courses [\"Mute City: Sonic Oval\"];\nlaps by course { \"Casino Palace: Split Oval\": 3; else: 2; }", "laps by course names \"Casino Palace: Split Oval\"")]
    [InlineData("laps by course { \"Casino Palace: Split Oval\": 3; else: 2; }\ncourses [\"Mute City: Sonic Oval\"];", "laps by course names \"Casino Palace: Split Oval\"")]
    [InlineData("stats 3;", "stats takes a name in quotes")]
    [InlineData("boost_start on;\nboost_start off;", "boost_start is set twice")]
    [InlineData("mixed_packs on;\nmixed_packs on;", "mixed_packs is set twice")]
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
        var mode = ModeCompiler.Compile("mode \"S\" { api 1; }\nsolo on;\n").Mode!;
        Assert.True(ModeTerms.Of(mode).Solo);
        Assert.Contains("raced alone", ModeTerms.Of(mode).Refusal(1, 0), StringComparison.Ordinal);
    }

    private const string Sw = "mode \"S\" { api 1; }\nparam p \"P\" switch default off;\nparam n \"N\" number 50..5000 default 200;\n";

    /// <summary>An on/off word's three spellings, in the terms it reaches: on, off, a switch parameter's two values.</summary>
    [Theory]
    [InlineData("cpus")]
    [InlineData("boost_start")]
    [InlineData("mixed_packs")]
    [InlineData("customs")]
    [InlineData("boost")]
    [InlineData("solo")]
    public void AnOnOffWordTakesOnOffOrASwitchParameter(string word)
    {
        bool? Read(ModeTerms t) => word switch
        {
            "cpus" => t.CpusAllowed,
            "boost_start" => t.BoostStart,
            "mixed_packs" => t.MixedPacks,
            "customs" => !t.CustomsOff,
            "solo" => t.Solo,
            _ => t.BoostStart != false,
        };
        ModeTerms Of(string value, IReadOnlyList<int>? values = null) =>
            ModeTerms.Of(ModeCompiler.Compile($"{Sw}{word} {value};\n").Mode!, values);
        Assert.True(Read(Of("on")));
        Assert.False(Read(Of("off")));
        Assert.False(Read(Of("p")));                // its default
        Assert.True(Read(Of("p", [1, 200])));
        Assert.False(Read(Of("p", [0, 200])));
    }

    [Theory]
    [InlineData("off", null, 0)]
    [InlineData("on", null, 200)]
    [InlineData("750", null, 750)]
    [InlineData("n", 900, 900)]
    [InlineData("p", 0, 0)]
    [InlineData("p", 1, 200)]
    public void AMineRegenIsOnOffANumberOrAParameter(string value, int? given, int ms)
    {
        var mode = ModeCompiler.Compile($"{Sw}mine_regen {value};\n").Mode!;
        var values = given is { } g ? (value == "n" ? new[] { 0, g } : [g, 200]) : null;
        Assert.Equal(ms, ModeTerms.Of(mode, values).MinesReturn);
    }

    [Theory]
    [InlineData("boost;", "boost takes on, off or a switch parameter")]
    [InlineData("boost 1;", "boost takes on, off or a switch parameter")]
    [InlineData("boost true;", "boost takes on, off or a switch parameter")]
    [InlineData("boost n;", "boost takes on, off or a switch parameter")]
    [InlineData("boost nope;", "boost takes on, off or a switch parameter")]
    [InlineData("cpus allowed;", "cpus takes on, off or a switch parameter")]
    [InlineData("restore;", "restore takes on, off or a switch parameter")]
    [InlineData("contact 0;", "contact takes on, off or a switch parameter")]
    [InlineData("solo;", "solo takes on, off or a switch parameter")]
    [InlineData("customs n;", "customs takes on, off or a switch parameter")]
    [InlineData("mine_regen 49;", "mine_regen is 50..5000")]
    [InlineData("mine_regen true;", "mine_regen")]
    [InlineData("pause on;", "says solo on")]
    [InlineData("solo off;\nrewind 1min;", "says solo on")]
    [InlineData("solo on;\nsave_state on;", "says rewind")]
    [InlineData("solo p;\ncpus p;", "one seat")]
    [InlineData("time_limit 5min;", "not a word of api")]
    [InlineData("rule \"R\" { event race_start; do { eliminate seat; } }", "eliminate")]
    public void AnOnOffWordGivenAnythingElseIsRefused(string body, string says)
    {
        var result = ModeCompiler.Compile(Sw + body + "\n");
        Assert.Null(result.Mode);
        Assert.Contains(says, Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pause off;")]
    [InlineData("save_state off;")]
    [InlineData("pause p;\nsolo p;")]
    [InlineData("solo off;\ncpus on;")]
    public void AnOffWordAsksForNothing(string body) => Assert.Empty(ModeCompiler.Compile(Sw + body + "\n").Errors);

    [Theory]
    [InlineData("A")]
    [InlineData("KNOCKOUT")]
    [InlineData("VS TOTAL RANKING")]
    [InlineData("ABCDEFGHIJKLM OPQRSTUVW")]   // 23, the window's 24 bytes with the NUL
    public void AResultsTitleTravelsInThePackage(string title)
    {
        var mode = ModeCompiler.Compile($"mode \"S\" {{ api 1; }}\nresults_title \"{title}\";\n");
        var word = Assert.Single(ModePackage.Read(mode.Package!).Setups);
        Assert.Equal(ModeSetup.ResultsTitle, word.Id);
        Assert.Equal(title, ModeResultsTitle.Text(word.Arguments));
        Assert.Contains($"results_title \"{title}\"", ModeDisassembler.List(mode.Package!), StringComparison.Ordinal);
    }

    private static string TagRule(string body) => "rule \"R\" { event each seat; do { " + body + " } }\n";

    [Fact]
    public void TagsTextsTravelInThePackageOnceEach()
    {
        var body = TagRule("tag seat, \"BOMB\"; tag seat, \"IT 2\"; tag seat, \"BOMB\"; tag seat off;");
        var mode = ModeCompiler.Compile($"mode \"S\" {{ api 1; }}\n{body}");
        Assert.Empty(mode.Errors);
        var word = Assert.Single(ModePackage.Read(mode.Package!).Setups);
        Assert.Equal(ModeSetup.TagTexts, word.Id);
        Assert.Equal(["BOMB", "IT 2"], ModeTagTexts.Texts(word.Arguments));
        var listing = ModeDisassembler.List(mode.Package!);
        Assert.Contains("tag_texts \"BOMB\" \"IT 2\"", listing, StringComparison.Ordinal);
        Assert.Contains("tag off", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void AModeWithoutTagsHasNoTagTexts()
    {
        var mode = ModeCompiler.Compile("mode \"S\" { api 1; }\nrule \"R\" { event race_start; do { } }\n");
        Assert.DoesNotContain(ModePackage.Read(mode.Package!).Setups, s => s.Id == ModeSetup.TagTexts);
    }

    [Fact]
    public void EightTagTextsAreAllowed()
    {
        var tags = string.Concat(Enumerable.Range(0, ModeWords.TagTexts).Select(i => $"tag seat, \"T{i}\"; "));
        Assert.Empty(ModeCompiler.Compile(Sw + TagRule(tags)).Errors);
    }

    [Theory]
    [InlineData("tag seat, \"\";", "tag needs a text")]
    [InlineData("tag seat, \" A\";", "no space at either end")]
    [InlineData("tag seat, \"A \";", "no space at either end")]
    [InlineData("tag seat, \"Bomb\";", "not 'o'")]
    [InlineData("tag seat, \"A,B\";", "not ','")]
    [InlineData("tag seat, \"GO!\";", "not '!'")]
    [InlineData("tag seat, \"\u00C9\";", "font 5 draws")]
    [InlineData("tag seat, \"ABCDEFGHIJK\";", "at most 10 characters")]
    [InlineData("tag seat, 1;", "tag takes its text in quotes")]
    [InlineData("tag seat, BOMB;", "tag takes its text in quotes")]
    [InlineData("tag seat;", "tag takes 2 arguments")]
    [InlineData("show_number seat, \"A\";", "")]
    [InlineData("tag seat, \"T0\"; tag seat, \"T1\"; tag seat, \"T2\"; tag seat, \"T3\"; tag seat, \"T4\"; tag seat, \"T5\"; tag seat, \"T6\"; tag seat, \"T7\"; tag seat, \"T8\";", "at most 8 different texts")]
    public void ATagTheGameCannotDrawIsRefused(string body, string says)
    {
        var result = ModeCompiler.Compile(Sw + TagRule(body));
        Assert.Null(result.Mode);
        Assert.Contains(says, Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileCannotWriteTheTagTextsItself()
    {
        var result = ModeCompiler.Compile(Sw + "tag_texts \"BOMB\";\n");
        Assert.Contains("the compiler's own", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("results_title knockout;", "takes a name in quotes")]
    [InlineData("results_title p;", "takes a name in quotes")]
    [InlineData("results_title \"\";", "needs a title")]
    [InlineData("results_title \"   \";", "needs a title")]
    [InlineData("results_title \" KO\";", "no space at either end")]
    [InlineData("results_title \"KO \";", "no space at either end")]
    [InlineData("results_title \"Knockout\";", "A to Z and the space, not 'n'")]
    [InlineData("results_title \"ROUND 2\";", "A to Z and the space, not '2'")]
    [InlineData("results_title \"GO!\";", "A to Z and the space, not '!'")]
    [InlineData("results_title \"\u00C9TAPE\";", "A to Z and the space")]
    [InlineData("results_title \"ABCDEFGHIJKLMNOPQRSTUVWX\";", "at most 23 characters")]
    [InlineData("results_title \"A\";\nresults_title \"B\";", "results_title is set twice")]
    public void AResultsTitleTheGameCannotDrawIsRefused(string body, string says)
    {
        var result = ModeCompiler.Compile(Sw + body + "\n");
        Assert.Null(result.Mode);
        Assert.Contains(says, Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuleLinesSayWhatTheModeChanges()
    {
        var result = ModeCompiler.Compile("""
            mode "M" { api 1; about "A."; }
            param time "Time" delay 1min..10min step 30s default 3min;
            laps 3;
            boost off;
            restore off;
            clock_limit time;
            damage 150%;
            spin_attacks off;
            spin_immunity off;
            time_bonus 10;
            racers 2..8;
            """);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal(
            ["No boost", "No restore", "Time limit 3:00", "Damage 150%", "No spin attacks", "Time bonus 10 seconds", "Up to 8 racers"],
            ModeRuleLines.Of(result.Mode!));
    }

    [Fact]
    public void ARuleNotChangedSaysNothing()
    {
        var result = ModeCompiler.Compile("""
            mode "M" { api 1; about "A."; }
            laps 3;
            boost on;
            contact on;
            """);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Empty(ModeRuleLines.Of(result.Mode!));
    }
}
