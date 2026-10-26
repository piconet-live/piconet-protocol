using System.Globalization;

namespace RelayRoundTrip;

/// <summary>A packet as the line the vectors file and the C++ test's Describe (protocol/tests/generated/
/// relay_describe.g.h) write: Kind Field=value ..., in the C# record's order, records in lowercase hexadecimal, an
/// absent optional field as -.</summary>
internal static class Packets
{
    public static string Describe(object? packet)
    {
        if (packet is null)
        {
            return "-";
        }
        var type = packet.GetType();
        var fields = type.GetConstructors().Single().GetParameters()
            .Select(p => $" {p.Name}={Value(type.GetProperty(p.Name!)!.GetValue(packet))}");
        return type.GetProperty("Type")!.GetValue(packet) + string.Concat(fields);
    }

    public static string Line(byte[] bytes, object? packet, byte? foreign) =>
        $"{Convert.ToHexStringLower(bytes)}\t{Describe(packet)}\t{(foreign is { } v ? v.ToString(CultureInfo.InvariantCulture) : "-")}";

    private static string Value(object? value) => value switch
    {
        null => "-",
        byte[] bytes => Convert.ToHexStringLower(bytes),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"no text for a {value.GetType().Name}"),
    };
}
