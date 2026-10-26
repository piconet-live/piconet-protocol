using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Online.Protocol;

/// <summary>
/// A player's tag: names are not unique and there are no accounts, so each player also has a
/// short tag, shown as "NAME #4F2A". Four characters of an alphabet with nothing that reads as another character (no
/// 0 or O, no 1, I or L). A player never chooses or changes their tag: the launcher keeps a
/// random secret (<see cref="NewKey"/>) and sends it in every hello (<see cref="Hello.Key"/>), and the coordinator
/// works the tag out from it with a key of its own (<see cref="Derived"/>, counter 0). When that name and tag are
/// another connected player's (<see cref="Key"/>), counter 1, 2 and so on give a tag for this connection only
/// (<see cref="Welcome.Tag"/>, <see cref="TagChanged"/>); the next visit has counter 0's again. A launcher that sends
/// no secret (0.2.x) gets a random tag each visit (<see cref="New"/>). Tags are for players to tell each other apart
/// in the launcher; the race shows plain names (the game draws no tag).
/// </summary>
public static class PlayerTags
{
    /// <summary>The characters a tag is made of: digits 2 to 9 and the capitals but O, I and L (31).</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public const int Length = 4;

    /// <summary>How many tags there are: 31 to the fourth, 923,521.</summary>
    public const int Count = 31 * 31 * 31 * 31;

    /// <summary>A tag as the coordinator gives it: exactly <see cref="Length"/> characters of <see cref="Alphabet"/>
    /// (capitals; a lower-case tag is no tag).</summary>
    public static bool IsValid(string? tag) => tag is { Length: Length } && tag.All(c => Alphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>A new tag, from the cryptographic random source: a tag cannot be guessed ahead from the ones seen.</summary>
    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>The tag numbered <paramref name="index"/> (0 to <see cref="Count"/> - 1), for going through all of
    /// them in order.</summary>
    public static string At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        Span<char> tag = stackalloc char[Length];
        for (var i = Length - 1; i >= 0; i--)
        {
            tag[i] = Alphabet[index % Alphabet.Length];
            index /= Alphabet.Length;
        }
        return new string(tag);
    }

    /// <summary>How many bytes a launcher's secret has (<see cref="NewKey"/>).</summary>
    public const int KeyBytes = 32;

    /// <summary>A secret as text: <see cref="KeyBytes"/> bytes in base64url without padding (43 characters).</summary>
    public const int KeyLength = 43;

    /// <summary>What the derivation hashes first, so that a tag's hash is of no other use.</summary>
    private static readonly byte[] DerivationLabel = Encoding.ASCII.GetBytes("piconet player tag v1");

    /// <summary>A launcher's new secret: <see cref="KeyBytes"/> bytes of the cryptographic random source, in
    /// base64url. Made once and kept in the launcher's settings; never shown.</summary>
    public static string NewKey() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));

    /// <summary>The bytes of a secret as <see cref="NewKey"/> writes it: exactly <see cref="KeyLength"/> base64url
    /// characters that decode to <see cref="KeyBytes"/> bytes and are those bytes' only spelling (so one secret is
    /// one text); null for anything else.</summary>
    public static byte[]? KeyBytesOf(string? key)
    {
        if (key is not { Length: KeyLength } || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return null;
        }
        var bytes = new byte[KeyBytes];
        bool read;
        try
        {
            read = Base64Url.TryDecodeFromChars(key, bytes, out var written) && written == KeyBytes;
        }
        catch (FormatException)
        {
            read = false;   // (a last character with bits that are no byte's: another spelling)
        }
        if (!read || Base64Url.EncodeToString(bytes) != key)
        {
            CryptographicOperations.ZeroMemory(bytes);
            return null;
        }
        return bytes;
    }

    /// <summary>The text is a secret as <see cref="NewKey"/> writes it (<see cref="KeyBytesOf"/>).</summary>
    public static bool IsKey(string? key)
    {
        if (KeyBytesOf(key) is not { } bytes)
        {
            return false;
        }
        CryptographicOperations.ZeroMemory(bytes);
        return true;
    }

    /// <summary>The key of a server that has none of its own (a LAN server, a test, a local run: the online stacks have
    /// theirs, the coordinator's FZGX_TAG_KEY). It is in the source, so on such a server a chosen tag can be searched
    /// for offline; with a key nobody outside the server knows, every guess is a hello.</summary>
    public static ReadOnlySpan<byte> DevelopmentKey => "piconet development tag key: never the online service's"u8;

    /// <summary>The secret's tag number <paramref name="counter"/> on a server with this <paramref name="serverKey"/>:
    /// HMAC-SHA-256 keyed by the server's key, over <see cref="DerivationLabel"/>, the counter (4 bytes, big-endian)
    /// and the secret's bytes; the hash's first 4 bytes (big-endian) modulo <see cref="Count"/> name the tag
    /// (<see cref="At"/>; the modulo makes some tags likelier than others by 1 in 4,650 at most). Counter 0 is the
    /// player's own tag; the others are for a connection whose own pair is taken. The server's key is what makes a
    /// tag impossible to choose: without it, a secret giving a chosen tag cannot be searched for offline (with the
    /// SHA-256 alone that took under a second). A changed server key gives every player another tag.</summary>
    public static string Derived(ReadOnlySpan<byte> serverKey, ReadOnlySpan<byte> secret, int counter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        Span<byte> input = stackalloc byte[DerivationLabel.Length + 4 + secret.Length];
        DerivationLabel.CopyTo(input);
        BinaryPrimitives.WriteUInt32BigEndian(input[DerivationLabel.Length..], (uint)counter);
        secret.CopyTo(input[(DerivationLabel.Length + 4)..]);
        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(serverKey, input, hash);
        CryptographicOperations.ZeroMemory(input);
        return At((int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % Count));
    }

    /// <summary>The number of a tag (<see cref="At"/>'s inverse).</summary>
    public static int IndexOf(string tag)
    {
        if (!IsValid(tag))
        {
            throw new ArgumentException("not a tag", nameof(tag));
        }
        var index = 0;
        foreach (var c in tag)
        {
            index = (index * Alphabet.Length) + Alphabet.IndexOf(c, StringComparison.Ordinal);
        }
        return index;
    }

    /// <summary>A name and tag as players see them: "NAME #4F2A".</summary>
    public static string Shown(string name, string tag) => $"{name} #{tag}";

    /// <summary>What makes a name and tag one player's: the name as the game shows it (capitals, spaces collapsed:
    /// <see cref="PlayerNames.Shown"/>, so "ana" and "ANA" are one name) and the tag.</summary>
    public static string Key(string name, string tag) => PlayerNames.Shown(name) + "#" + tag;
}
