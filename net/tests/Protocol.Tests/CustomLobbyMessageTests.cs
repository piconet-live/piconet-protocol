using Online.Protocol;

namespace Protocol.Tests;

/// <summary>Custom lobbies' messages on the wire: their type names, their rules, and what an older end reads.</summary>
public sealed class CustomLobbyMessageTests
{
    [Fact]
    public void TheClientsMessagesKeepTheirFields()
    {
        var rules = new LobbyRules(new Dictionary<string, int> { ["laps"] = 5, [LobbyRules.SettingHighKey] = 90 }, "GX Unleashed");
        var create = ControlProtocol.Encode(new CreateLobby("FRIDAY", rules, "pit", 3, new Rgb(1, 2, 3), 70));
        Assert.Contains("\"type\":\"createLobby\"", create, StringComparison.Ordinal);
        Assert.Contains("\"values\":{\"laps\":5,\"machine-setting-high\":90}", create, StringComparison.Ordinal);
        var back = Assert.IsType<CreateLobby>(ControlProtocol.DecodeClient(create));
        Assert.Equal(5, back.Rules.Value(HostRules.Laps));
        Assert.Equal("GX Unleashed", back.Rules.StatPack);
        Assert.Equal(70, back.Setting);
        Assert.IsType<StartLobbyRace>(ControlProtocol.DecodeClient("""{"type":"startLobbyRace"}"""));
        Assert.IsType<LeaveLobby>(ControlProtocol.DecodeClient("""{"type":"leaveLobby"}"""));
        Assert.Equal(new KickPlayer("p2"), ControlProtocol.DecodeClient("""{"type":"kick","playerId":"p2"}"""));
        Assert.Equal(new BrowseLobbies(true), ControlProtocol.DecodeClient("""{"type":"browseLobbies","on":true}"""));
    }

    [Fact]
    public void AQuickPlayLobbyAndALeftLobbyReadAsBefore()
    {
        var text = ControlProtocol.Encode(new LobbyState("l1", [], 30, 2, false, 0, 0));
        Assert.DoesNotContain("custom", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hostId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("locked", text, StringComparison.Ordinal);
        Assert.Equal("""{"type":"leftLobby","lobbyId":"l1"}""", ControlProtocol.Encode(new LeftLobby("l1")));
        Assert.Equal(new LeftLobby("l1", CustomLobby.Kicked),
            ControlProtocol.DecodeServer("""{"type":"leftLobby","lobbyId":"l1","reason":"kicked"}"""));
    }

    [Fact]
    public void ACustomLobbysStateCarriesItsNameAndLock()
    {
        var text = ControlProtocol.Encode(new LobbyState("l1", [], 8, 2, false, 0, 0, Custom: true, HostId: "p1",
            Rules: LobbyRules.QuickPlay, Name: "FRIDAY", Locked: true));
        Assert.Contains("\"name\":\"FRIDAY\"", text, StringComparison.Ordinal);
        var back = Assert.IsType<LobbyState>(ControlProtocol.DecodeServer(text));
        Assert.Equal("FRIDAY", back.Name);
        Assert.True(back.Locked);
        var older = Assert.IsType<LobbyState>(ControlProtocol.DecodeServer(
            """{"type":"lobby","lobbyId":"l1","players":[],"capacity":8,"votingMinimum":2,"votingOpen":false,"startVotes":0,"cpuFillVotes":0,"custom":true}"""));
        Assert.Null(older.Name);
        Assert.False(older.Locked);
    }

    [Fact]
    public void OnlyTheRulesThatDifferFromQuickPlaysAreNamed()
    {
        var quick = new LobbyRules(new Dictionary<string, int> { ["racers"] = 30, ["course-select"] = (int)CourseSelectMode.Vote, ["laps"] = 3 });
        var lobby = new LobbyRules(new Dictionary<string, int> { ["racers"] = 30, ["course-select"] = (int)CourseSelectMode.Fixed,
            [LobbyRules.FixedCourseKey] = 3, ["laps"] = 5, ["machine-setting"] = 80 }, "GX Unleashed");
        // (laps, the setting and the stats are the mode's: no lobby setting, not named)
        Assert.Equal(["course-select"], lobby.Differing(quick));
        Assert.Empty(LobbyRules.QuickPlay.Differing(quick));
    }

    [Fact]
    public void NoServerRuleIsAHostsRule()
    {
        Assert.DoesNotContain(LobbyRules.Settable, r => r.Reach == RuleReach.Server);
        Assert.Contains(HostRules.CpuFill, LobbyRules.Settable);
        Assert.Contains(HostRules.StatPack, LobbyRules.Settable);
        Assert.DoesNotContain("stat-pack", LobbyRules.Keys);
    }

    [Fact]
    public void AHostsPackFileTravelsWithTheRulesAndAnOlderEndPassesOverIt()
    {
        const string File = "fzstats 1\nname My Pack\n# a comment\nmachine 6 Weight 2520\n";
        var rules = new LobbyRules(new Dictionary<string, int>(), "My Pack", File);
        var text = ControlProtocol.Encode(new SetLobbyRules(null, rules));
        var back = Assert.IsType<SetLobbyRules>(ControlProtocol.DecodeClient(text));
        Assert.Equal(("My Pack", File), (back.Rules.StatPack, back.Rules.PackFile));
        Assert.DoesNotContain("packFile", ControlProtocol.Encode(new SetLobbyRules(null, rules with { PackFile = null })),
            StringComparison.Ordinal);   // (none: the rules as before)
        var older = Assert.IsType<SetLobbyRules>(ControlProtocol.DecodeClient(
            """{"type":"setLobbyRules","rules":{"values":{},"statPack":"GX Unleashed"}}"""));
        Assert.Null(older.Rules.PackFile);
    }

    [Fact]
    public void APackOfEveryValueFitsAPackFile()
    {
        var entries = new List<StatEntry>();
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            foreach (var rule in StatPackRules.All)
            {
                for (var word = 0; word < rule.Words; word++)
                {
                    var at = rule.At + (word * 4);
                    var bits = new[] { rule.High, rule.Low, 1f, 0f }.Select(BitConverter.SingleToUInt32Bits)
                        .Where(b => StatPackRules.Problem(block, at, b) is null).Cast<uint?>().FirstOrDefault();
                    if (bits is { } b)
                    {
                        entries.Add(new StatEntry(block, at, b));
                    }
                }
            }
        }
        var (pack, refusal) = StatPack.Of("Every Value", entries);
        Assert.True(pack is not null, refusal);
        var file = StatPackText.Write(pack);
        Assert.True(file.Length <= LobbyRules.MostPackFile / 2, $"{file.Length} characters");
    }
    /// <summary>How the race plays is the mode's: a host's rules that set laps, CPUs' level, a machine setting or a
    /// server's pack by name are refused in one line; the lobby's own settings, the rules with no mode word yet and the
    /// host's own pack file are not.</summary>
    [Fact]
    public void ARuleOfTheRaceOutsideTheModeIsRefused()
    {
        Assert.Equal("the mode sets laps now: choose a mode that sets it, or a mode file of your own",
            new LobbyRules(new Dictionary<string, int> { ["laps"] = 5 }).OutsideTheMode());
        Assert.Contains("the mode sets machine setting now", new LobbyRules(new Dictionary<string, int> { [LobbyRules.SettingHighKey] = 90 }).OutsideTheMode(), StringComparison.Ordinal);
        Assert.StartsWith("the mode sets the stats now", new LobbyRules(new Dictionary<string, int>(), "GX Unleashed").OutsideTheMode(), StringComparison.Ordinal);
        Assert.Null(new LobbyRules(new Dictionary<string, int>
        {
            ["racers"] = 8, ["latency-limit"] = 150, ["vote-seconds"] = 20, ["choose-seconds"] = 20, ["voting-minimum"] = 2,
            ["course-select"] = 2, [LobbyRules.FixedCourseKey] = 8,
        }, "Own", "name Own\n").OutsideTheMode());
        Assert.Equal(["laps", "cpu-level", "cpu-fill", "boost-start", "finish-limit", "mine-regen", "machine-setting", "mixed-packs",
            LobbyRules.SettingHighKey, LobbyRules.TeamsKey], LobbyRules.ModeKeys);
        Assert.All(LobbyRules.ModeKeys.Where(k => k is not (LobbyRules.SettingHighKey or LobbyRules.TeamsKey)), k => Assert.NotNull(HostRules.Named(k)!.SetupWord));
        Assert.Equal("the mode sets teams now: choose a mode that sets it, or a mode file of your own",
            new LobbyRules(new Dictionary<string, int> { [LobbyRules.TeamsKey] = 2 }).OutsideTheMode());
        Assert.StartsWith("the mode sets cpu racers now", new LobbyRules(new Dictionary<string, int> { ["cpu-fill"] = 1 }).OutsideTheMode(), StringComparison.Ordinal);
    }

    /// <summary>The lobby browser carries the server's official modes; an older end passes over them.</summary>
    [Fact]
    public void TheLobbyListCarriesTheOfficialModes()
    {
        var mode = new LobbyMode("ab", "CODE", "Ghost race", 1);
        var text = ControlProtocol.Encode(new LobbyList([], null, [mode]));
        Assert.Contains("\"modes\":[{", text, StringComparison.Ordinal);
        var back = Assert.IsType<LobbyList>(ControlProtocol.DecodeServer(text));
        Assert.Equal("Ghost race", Assert.Single(back.Modes!).Name);
        Assert.DoesNotContain("modes", ControlProtocol.Encode(new LobbyList([])), StringComparison.Ordinal);
    }
}
