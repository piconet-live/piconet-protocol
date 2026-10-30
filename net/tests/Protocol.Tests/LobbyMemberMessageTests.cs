using Online.Protocol;

namespace Protocol.Tests;

/// <summary>A lobby member's machine on the wire: a garage-built machine reaches the other members (the lobby's state
/// and the public status), and a member on a stock machine reads as before.</summary>
public sealed class LobbyMemberMessageTests
{
    private static readonly CustomMachine Built = new(15, 44, 58, 6, [0xFF000001u, 0, 0x00FF0001u]);

    [Fact]
    public void ALobbyMemberCarriesTheirCustomMachine()
    {
        var text = ControlProtocol.Encode(new LobbyState("l1", [new PlayerInfo("p1", "ANA", 6, new Rgb(1, 2, 3), Custom: Built)],
            30, 2, false, 0, 0));
        Assert.Contains("\"custom\":{\"body\":15,\"cockpit\":44,\"booster\":58,\"pilot\":6,", text, StringComparison.Ordinal);
        var member = Assert.IsType<LobbyState>(ControlProtocol.DecodeServer(text)).Players.Single();
        Assert.Equal((15, 44, 58, 6), (member.Custom!.Body, member.Custom.Cockpit, member.Custom.Booster, member.Custom.Pilot));
        Assert.Equal(Built.Colors, member.Custom.Colors);
    }

    [Fact]
    public void AMemberOnAStockMachineReadsAsBefore()
    {
        var text = ControlProtocol.Encode(new LobbyState("l1", [new PlayerInfo("p1", "ANA", 6, new Rgb(1, 2, 3))], 30, 2, false, 0, 0));
        Assert.DoesNotContain("custom", text, StringComparison.Ordinal);
        var older = Assert.IsType<LobbyState>(ControlProtocol.DecodeServer(
            """{"type":"lobby","lobbyId":"l1","players":[{"id":"p1","name":"ANA","machine":6,"color":{"r":1,"g":2,"b":3}}],"capacity":30,"votingMinimum":2,"votingOpen":false,"startVotes":0,"cpuFillVotes":0}"""));
        Assert.Null(older.Players.Single().Custom);
    }

    [Fact]
    public void TheStatusShowsARacersCustomMachine()
    {
        var text = System.Text.Json.JsonSerializer.Serialize(new RacerView("ANA", 6, "#010203", "E99P", Built), ControlProtocol.Json);
        Assert.Contains("\"custom\":{\"body\":15", text, StringComparison.Ordinal);
        var back = System.Text.Json.JsonSerializer.Deserialize<RacerView>(text, ControlProtocol.Json)!;
        Assert.Equal(58, back.Custom!.Booster);
        Assert.DoesNotContain("custom",
            System.Text.Json.JsonSerializer.Serialize(new RacerView("ANA", 6, "#010203"), ControlProtocol.Json), StringComparison.Ordinal);
    }
}
