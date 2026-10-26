using System.Text;
using System.Text.Json;

namespace Online.Protocol;

/// <summary>
/// LAN discovery: the server broadcasts this as UDP JSON to <see cref="Port"/> every couple of seconds; a launcher
/// listening there lists the servers it hears.
/// </summary>
public sealed record LanAnnouncement(string Service, string Name, int ControlPort, int Version)
{
    public const int Port = 47800;
    public const string ServiceName = "fzgx-online";

    public byte[] Encode() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, ControlProtocol.Json));

    public static LanAnnouncement? Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            var a = JsonSerializer.Deserialize<LanAnnouncement>(data, ControlProtocol.Json);
            return a?.Service == ServiceName ? a : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
