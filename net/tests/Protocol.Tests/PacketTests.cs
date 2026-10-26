using Online.Protocol;

namespace Protocol.Tests;

public sealed class PacketTests
{
    public static TheoryData<RelayPacket> Packets() =>
    [
        new Ping(7, -12345),
        new Pong(7, 99),
        new Pong(7, 99, 123456),
        new Join(0xDEADBEEF01020304),
        new Joined(3, 5, 0b101111),
        new Ready(42),
        new Go(9, 12),
        new InputPacket(42, 100, 98, 2, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]),
        new OfficialPacket(10, 1, 0b11, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]),
        new ChecksumPacket(42, 59, 0xCAFEF00D),
        new Desync(59),
        new SeatLeft(4),
    ];

    [Theory]
    [MemberData(nameof(Packets))]
    public void PacketsRoundTrip(RelayPacket packet)
    {
        var back = RelayProtocol.Decode(RelayProtocol.Encode(packet));
        Assert.NotNull(back);
        Assert.Equal(RelayProtocol.Encode(packet), RelayProtocol.Encode(back));
    }

    [Fact]
    public void MalformedPacketsAreIgnored()
    {
        Assert.Null(RelayProtocol.Decode([1, 2, 3]));
        Assert.Null(RelayProtocol.Decode([(byte)'F', (byte)'Z', 99, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        var input = RelayProtocol.Encode(new InputPacket(1, 0, 0, 2, new byte[12]));
        Assert.Null(RelayProtocol.Decode(input.AsSpan(0, input.Length - 1)));   // count says 2 records, 1.83 there
    }

    [Fact]
    public void MessagesRoundTripWithTheirTypeField()
    {
        var text = ControlProtocol.Encode(new EnterQuickPlay(6, new Rgb(255, 0, 10)));
        Assert.Contains("\"type\":\"queue\"", text, StringComparison.Ordinal);
        Assert.Equal(new EnterQuickPlay(6, new Rgb(255, 0, 10)), ControlProtocol.DecodeClient(text));
        var start = new MatchStart("m1", new RelayEndpoint("lan", "", 47811), "00ff", 2,
            new RaceSetup(1, 3, [new SeatSetup(0, "p1", "ANA", 6, new Rgb(1, 2, 3)), new SeatSetup(1, null, "CPU", 4, null)], 5, 6));
        var back = Assert.IsType<MatchStart>(ControlProtocol.DecodeServer(ControlProtocol.Encode(start)));
        Assert.Equal(start with { Setup = null! }, back with { Setup = null! });
        Assert.Equal(start.Setup.Seats, back.Setup.Seats);
        Assert.True(back.Setup.Seats[1].Cpu);
    }

    /// <summary>The first message as the coordinator in service reads it (the build check's: name, version,
    /// password, build): those fields are written as they were, the vote counts' flag after them, and a server from
    /// before the flag takes the same text and leaves the flag out, which is why the version did not change.</summary>
    [Fact]
    public void HelloKeepsItsFieldsAndAServerFromBeforeTheVoteCountsIgnoresTheirFlag()
    {
        var v = ControlProtocol.Version;
        var text = ControlProtocol.Encode(new Hello("ANA", v, Build: "i/m/r", VoteCounts: true));
        Assert.Equal($$"""{"type":"hello","name":"ANA","version":{{v}},"build":"i/m/r","voteCounts":true}""", text);
        Assert.Equal(new HelloBefore("ANA", v, null, "i/m/r"),
            System.Text.Json.JsonSerializer.Deserialize<ClientMessageBefore>(text, ControlProtocol.Json));
        // a launcher from before custom lobbies sent the server's password: the field is passed over
        Assert.Equal(new Hello("ANA", v, "i/m/r"),
            ControlProtocol.DecodeClient($$"""{"type":"hello","name":"ANA","version":{{v}},"password":"den","build":"i/m/r"}"""));
        // a launcher from before the flag asks for no counts
        Assert.Equal(new Hello("BEN", v, "i/m/r"),
            ControlProtocol.DecodeClient($$"""{"type":"hello","name":"BEN","version":{{v}},"build":"i/m/r"}"""));
    }

    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(HelloBefore), "hello")]
    private abstract record ClientMessageBefore;

    /// <summary>The first message of the coordinator in service (main at 0859df3).</summary>
    private sealed record HelloBefore(string Name, int Version, string? Password = null, string? Build = null) : ClientMessageBefore;
}
