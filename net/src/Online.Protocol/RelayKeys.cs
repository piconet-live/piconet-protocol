using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Online.Protocol;

/// <summary>
/// A seat's key: the secret every packet the relay sends a game opens with (protocol/relay.toml), added after a
/// security review: the game took any packet from the relay's address and port, so whoever could send from there
/// (on the path, or forging that source towards the player's port) could stop its game with a Desync or feed it inputs. The
/// key is worked out from the seat's token, which only its player and the relay know (the coordinator sends it to
/// each over TLS or its private network): the first 8 bytes, big-endian, of SHA-1("fzgx relay key" + the token as 8
/// big-endian bytes). It shows nothing of the token, which an Input still carries in clear.
/// The game's side: piconet-runner's port/core/src/relay_wire.cpp, RelayKey.
/// </summary>
public static class RelayKeys
{
    private static readonly byte[] Label = Encoding.ASCII.GetBytes("fzgx relay key");

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA-1's known " +
        "weakness is collisions; the key needs only that the token cannot be found from it (a preimage), and the game's " +
        "core has SHA-1 and no other hash")]
    public static ulong Of(ulong token)
    {
        Span<byte> input = stackalloc byte[Label.Length + 8];
        Label.CopyTo(input);
        BinaryPrimitives.WriteUInt64BigEndian(input[Label.Length..], token);
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(input, hash);
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }
}
