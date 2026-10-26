using Online.Protocol;

namespace Protocol.Tests;

public sealed class RelayPacketSizeTests
{
    [Fact]
    public void OfficialPacketsForThirtySeatsStayUnderTheMtu()
    {
        var rowSize = 30 * RelayProtocol.RecordSize;
        var frames = Math.Min(RelayProtocol.MaxFramesPerPacket, RelayProtocol.MaxPayload / rowSize);
        var packet = RelayProtocol.Encode(new OfficialPacket(0, frames, 0x3FFFFFFF, new byte[frames * rowSize]));
        Assert.Equal(6, frames);
        Assert.True(packet.Length <= 1232, $"{packet.Length} bytes");
    }
}
