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
        var quick = new LobbyRules(new Dictionary<string, int> { ["laps"] = 3, ["machine-setting"] = 100, [LobbyRules.SettingHighKey] = 100 });
        var lobby = new LobbyRules(new Dictionary<string, int> { ["laps"] = 3, ["machine-setting"] = 80, [LobbyRules.SettingHighKey] = 100 },
            "GX Unleashed");
        Assert.Equal(["machine-setting", "stat-pack"], lobby.Differing(quick));
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
}
