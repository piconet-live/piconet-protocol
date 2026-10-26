using System.Globalization;
using Online.Protocol;
using RelayRoundTrip;

// The generated C# codec over the vectors file, both directions: each datagram decodes to the packet the file records
// (- none) with the foreign version it records (- none), and each packet encodes back to the very bytes. The file is
// the wire of relay version 3 as the hand-written codec of 0.4.0 decoded it (recorded by the parity test that compared
// the two before the swap), so a change of the wire fails here.
//   RelayRoundTrip VECTORS.tsv
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: RelayRoundTrip VECTORS.tsv");
    return 2;
}
var failures = 0;
void Fail(string what)
{
    if (++failures <= 20)
    {
        Console.Error.WriteLine($"round trip: {what}");
    }
}

int lines = 0, packets = 0;
var kindsSeen = new HashSet<PacketType>();
foreach (var line in File.ReadLines(args[0]))
{
    if (line.Length == 0 || line[0] == '#')
    {
        continue;
    }
    lines++;
    var bytes = Convert.FromHexString(line[..line.IndexOf('\t', StringComparison.Ordinal)]);
    var packet = RelayProtocol.Decode(bytes);
    var got = Packets.Line(bytes, packet, RelayProtocol.ForeignVersion(bytes));
    if (got != line)
    {
        Fail($"decode: [{got}], the vectors [{line}]");
    }
    if (packet is not null)
    {
        packets++;
        kindsSeen.Add(packet.Type);
        if (!RelayProtocol.Encode(packet).AsSpan().SequenceEqual(bytes))
        {
            Fail($"decode then encode differs: {Convert.ToHexStringLower(bytes)}");
        }
    }
}
var kinds = Enum.GetValues<PacketType>().Length;
if (kindsSeen.Count != kinds)
{
    Fail($"the vectors hold {kindsSeen.Count} of the {kinds} kinds of packet: every kind needs lines");
}
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"round trip C#: {lines} vectors, {packets} of them packets ({kinds} kinds); {failures} differences"));
return failures == 0 && lines > 0 ? 0 : 1;
