using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Online.Protocol;

namespace MessageVectors;

/// <summary>Records of every type of the lobby link with values drawn from a seeded generator (the same file every
/// run): texts with every kind of character JSON escapes, numbers at their ends, floats of any bits that are
/// finite, times of any offset, nulls where a member may be null, lists of none to three. What the vectors hold.</summary>
internal sealed class Samples(int seed)
{
    private readonly Random _random = new(seed);

    private static readonly string[] Texts =
    [
        "", "a", "ANA", "p1", "global", "lobby", "forming", "racing", "Vote", "m-123", "#FFAA00", "0.7",
        "quote \" backslash \\ slash /", "<b>&amp;'+`</b>", "tab\tnew\nline\rret\bback\fform", "\u0001\u001f\u007f",
        "é", "naïve café", "€100", "日本語", "😀 grin", "a\u2028b\u2029c", "\u00A0nbsp", "\uFEFFbom", "Ω≈ç√",
        "mixed é😀\"<\\", "x".PadRight(70, 'y'),
    ];

    private static readonly int[] Ints = [0, 1, -1, 2, 3, 29, 30, 50, 100, 255, 256, 1000, 65535, int.MaxValue, int.MinValue, 47811];

    /// <summary>A value of a member's type (<paramref name="nullable"/>: null now and then), <paramref name="depth"/>
    /// deep in records.</summary>
    public object? Value(Type t, bool nullable, int depth)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null)
        {
            return _random.Next(4) == 0 ? null : Value(underlying, false, depth);
        }
        if (nullable && !t.IsValueType && _random.Next(4) == 0)
        {
            return null;
        }
        if (t == typeof(string)) return Text();
        if (t == typeof(int)) return _random.Next(3) == 0 ? _random.Next(int.MinValue, int.MaxValue) : Ints[_random.Next(Ints.Length)];
        if (t == typeof(uint)) return _random.Next(3) == 0 ? (uint)_random.NextInt64(0, 1L << 32) : new uint[] { 0, 1, 7, 0xFFFFFFFF, 0x80000000 }[_random.Next(5)];
        if (t == typeof(long)) return _random.Next(3) == 0 ? _random.NextInt64(long.MinValue, long.MaxValue) : new[] { 0L, -1L, long.MaxValue, long.MinValue, 1L << 53 }[_random.Next(5)];
        if (t == typeof(byte)) return (byte)_random.Next(256);
        if (t == typeof(bool)) return _random.Next(2) == 0;
        if (t == typeof(float)) return Float();
        if (t == typeof(double)) return Double();
        if (t == typeof(DateTimeOffset)) return Time();
        if (t.IsEnum)
        {
            var values = Enum.GetValues(t);
            return _random.Next(8) == 0 ? Enum.ToObject(t, 7) : values.GetValue(_random.Next(values.Length));   // (7: a value it names not)
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var element = t.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            var count = depth > 3 ? _random.Next(2) : _random.Next(4);
            for (var i = 0; i < count; i++)
            {
                list.Add(Value(element, false, depth + 1));
            }
            return list;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            var map = new Dictionary<string, int>();
            var count = _random.Next(4);
            for (var i = 0; i < count; i++)
            {
                map[Text()] = (int)Value(typeof(int), false, depth)!;
            }
            return map;
        }
        return Record(t, depth + 1);
    }

    /// <summary>A record of the type, every member drawn (its constructor's parameters).</summary>
    public object Record(Type t, int depth)
    {
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var nullability = new NullabilityInfoContext();
        var args = ctor.GetParameters()
            .Select(p => Value(p.ParameterType, nullability.Create(p).ReadState == NullabilityState.Nullable, depth)).ToArray();
        return ctor.Invoke(args);
    }

    private string Text()
    {
        if (_random.Next(3) > 0)
        {
            return Texts[_random.Next(Texts.Length)];
        }
        // any characters: ASCII mostly, then the Latin and other planes, a pair now and then
        var s = new StringBuilder();
        var length = _random.Next(12);
        for (var i = 0; i < length; i++)
        {
            switch (_random.Next(6))
            {
                case 0: s.Append((char)_random.Next(0, 0x20)); break;
                case 1: s.Append((char)_random.Next(0x80, 0x800)); break;
                case 2: s.Append((char)_random.Next(0x800, 0xD800)); break;
                case 3: s.Append(char.ConvertFromUtf32(_random.Next(0x10000, 0x110000))); break;
                default: s.Append((char)_random.Next(0x20, 0x7F)); break;
            }
        }
        return s.ToString();
    }

    private float Float()
    {
        float[] picked = [0f, -0f, 1f, -1f, 0.1f, 1.5f, 123.456f, 1e7f, 1e8f, 1e9f, 99999999f, 1e-4f, 1e-5f, 3.4028235e38f, float.Epsilon, 1.17549435e-38f, 16777216f, 1234567.9f, 2.5e-38f];
        while (true)
        {
            var f = _random.Next(3) switch
            {
                0 => picked[_random.Next(picked.Length)],
                1 => (float)(_random.NextDouble() * 2000 - 1000),
                _ => BitConverter.Int32BitsToSingle(_random.Next(int.MinValue, int.MaxValue)),
            };
            if (float.IsFinite(f))
            {
                return f;
            }
        }
    }

    private double Double()
    {
        double[] picked = [0, -0.0, 1, 0.1, 12.5, 1e15, 1e16, 1e17, 123456789012345680, 1e-4, 1e-5, 1e21, double.Epsilon, double.MaxValue, 1.0 / 3];
        while (true)
        {
            var d = _random.Next(3) switch
            {
                0 => picked[_random.Next(picked.Length)],
                1 => Math.Round(_random.NextDouble() * 500, _random.Next(4)),
                _ => BitConverter.Int64BitsToDouble(_random.NextInt64(long.MinValue, long.MaxValue)),
            };
            if (double.IsFinite(d))
            {
                return d;
            }
        }
    }

    private DateTimeOffset Time()
    {
        while (true)
        {
            var offset = TimeSpan.FromMinutes(_random.Next(3) == 0 ? 0 : _random.Next(-14 * 60, 14 * 60 + 1));
            var ticks = _random.Next(4) switch
            {
                0 => new DateTime(2026, 11, 14, 10, 0, 0).Ticks + (_random.Next(1000) * TimeSpan.TicksPerSecond),
                1 => new DateTime(2026, 11, 14).Ticks + _random.NextInt64(0, TimeSpan.TicksPerDay),
                2 => _random.NextInt64(DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks),
                _ => new DateTime(2026, 1, 1).Ticks + (_random.NextInt64(0, 1000) * 10_000),
            };
            var utc = ticks - offset.Ticks;
            if (utc >= DateTime.MinValue.Ticks && utc <= DateTime.MaxValue.Ticks)
            {
                return new DateTimeOffset(ticks, offset);
            }
        }
    }

    /// <summary>Texts a reader is given that no writer writes: the JSON of a written message changed in one way
    /// (a member's name in another case, members in another order, a number as a string, a member left out or
    /// null, one the record does not know, a duplicate, the type moved). Each with C#'s reading of it.</summary>
    public IEnumerable<string> Variants(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var names = node.Select(p => p.Key).Where(k => k != "type").ToList();
        if (names.Count == 0)
        {
            yield return json.Replace("{", "{ \"extra\" : [1, {\"a\": null}, \"x\"] ,", StringComparison.Ordinal);
            yield break;
        }
        var pick = names[_random.Next(names.Count)];
        // a name in upper case
        yield return Rebuilt(node, (k, v) => [(k == pick ? k.ToUpperInvariant() : k, v)]);
        // the member left out
        yield return Rebuilt(node, (k, v) => k == pick ? [] : [(k, v)]);
        // the member null
        yield return Rebuilt(node, (k, v) => [(k, k == pick ? null : v)]);
        // an unknown member before it, and the member twice (the second wins)
        yield return Rebuilt(node, (k, v) => k == pick ? [("unknownMember", JsonNode.Parse("{\"x\":[1,2.5,null,true]}")), (k, v), (k, v?.DeepClone())] : [(k, v)]);
        // the members in reverse order (the type first still)
        yield return Rebuilt(node, (k, v) => [(k, v)], reverse: true);
        // numbers as strings, everywhere
        yield return Stringed(node.DeepClone())!.ToJsonString();
        // the type not first
        yield return Rebuilt(node, (k, v) => [(k, v)], typeLast: true);
        // spaces between everything
        yield return JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string Rebuilt(JsonObject node, Func<string, JsonNode?, IEnumerable<(string, JsonNode?)>> each, bool reverse = false, bool typeLast = false)
    {
        var parts = new List<string>();
        string? type = null;
        var members = node.ToList();
        if (reverse)
        {
            members.Reverse();
        }
        foreach (var (k, v) in members)
        {
            if (k == "type")
            {
                type = $"\"type\":{v!.ToJsonString()}";
                continue;
            }
            parts.AddRange(each(k, v).Select(x => JsonSerializer.Serialize(x.Item1) + ":" + (x.Item2?.ToJsonString() ?? "null")));
        }
        if (type is not null)
        {
            if (typeLast)
            {
                parts.Add(type);
            }
            else
            {
                parts.Insert(0, type);
            }
        }
        return "{" + string.Join(",", parts) + "}";
    }

    private static JsonNode? Stringed(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var k in o.Select(p => p.Key).ToList())
                {
                    o[k] = Stringed(o[k]?.DeepClone());
                }
                return o;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    a[i] = Stringed(a[i]?.DeepClone());
                }
                return a;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                return JsonValue.Create(v.ToJsonString());
            default:
                return node;
        }
    }
}
