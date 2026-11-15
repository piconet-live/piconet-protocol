using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Online.Protocol;

namespace MessageVectors;

/// <summary>A member of a record as System.Text.Json writes and reads it: its JSON name, its schema type
/// (<see cref="Schema.TypeName"/>), the constructor parameter's default as JSON text (null: none), when it is left out
/// beyond a null (<c>default</c>: also when it is its type's default), and whether it is only written (a property
/// with no constructor parameter: read back, it is passed over).</summary>
internal sealed record Member(string Name, string Type, string? Default, string? Ignore, bool WriteOnly, PropertyInfo Property);

/// <summary>A record of the lobby link: its name, its root (client, server, or a document of its own), the name of
/// its message type (null: no message), and its members in the order they are written.</summary>
internal sealed record RecordDesc(string Name, string? Root, string? Discriminator, IReadOnlyList<Member> Members, Type Clr);

/// <summary>An enum: written by name (a string converter) or as its number; its values.</summary>
internal sealed record EnumDesc(string Name, bool Named, string Underlying, IReadOnlyList<(string Name, long Value)> Values, Type Clr);

/// <summary>The lobby link's types as Online.Protocol has them, by reflection over System.Text.Json's own metadata
/// (the properties in the order it writes them, the message types' names): what messages.toml must say.</summary>
internal static class Schema
{
    /// <summary>The roots: the two message families, and the documents of their own the launcher reads with the
    /// same options (the LAN announcement, the server's public status).</summary>
    public static readonly (string Root, Type Type)[] Roots =
    [
        ("client", typeof(ClientMessage)), ("server", typeof(ServerMessage)),
        ("lan", typeof(LanAnnouncement)), ("status", typeof(ServiceStatus)),
    ];

    /// <summary>The options the protocol writes and reads with (made read-only, its metadata resolved, as by its first use).</summary>
    public static JsonSerializerOptions Options
    {
        get
        {
            _ = ControlProtocol.Encode(new LeaveQuickPlay());
            return ControlProtocol.Json;
        }
    }

    public static (List<RecordDesc> Records, List<EnumDesc> Enums) Reflect()
    {
        var records = new List<RecordDesc>();
        var enums = new List<EnumDesc>();
        var seen = new HashSet<Type>();
        var queue = new Queue<(Type Type, string? Root, string? Disc)>();
        foreach (var (root, type) in Roots)
        {
            var info = Options.GetTypeInfo(type);
            if (info.PolymorphismOptions is { } poly)
            {
                foreach (var d in poly.DerivedTypes)
                {
                    queue.Enqueue((d.DerivedType, root, (string)d.TypeDiscriminator!));
                }
            }
            else
            {
                queue.Enqueue((type, root, null));
            }
        }
        while (queue.Count > 0)
        {
            var (type, root, disc) = queue.Dequeue();
            if (!seen.Add(type))
            {
                continue;
            }
            if (type.IsEnum)
            {
                enums.Add(Enum(type));
                continue;
            }
            var info = Options.GetTypeInfo(type);
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = ctor.GetParameters();
            var nullability = new NullabilityInfoContext();
            var members = new List<Member>();
            foreach (var p in info.Properties)
            {
                var property = (PropertyInfo)p.AttributeProvider!;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                {
                    continue;   // never written nor read
                }
                var parameter = parameters.FirstOrDefault(x => string.Equals(x.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                var n = parameter is not null ? nullability.Create(parameter) : nullability.Create(property);
                var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition switch
                {
                    null => null,
                    JsonIgnoreCondition.WhenWritingNull => null,   // (the options' own: a null is left out)
                    JsonIgnoreCondition.WhenWritingDefault => "default",
                    var c => throw new InvalidOperationException($"{type.Name}.{property.Name}: ignore condition {c}"),
                };
                members.Add(new Member(p.Name, TypeName(p.PropertyType, n),
                    parameter is { HasDefaultValue: true } ? DefaultText(parameter.DefaultValue) : null, ignore, parameter is null, property));
                foreach (var t in Reached(p.PropertyType))
                {
                    queue.Enqueue((t, null, null));
                }
            }
            records.Add(new RecordDesc(type.Name, root, disc, members, type));
        }
        return (records, enums);
    }

    private static EnumDesc Enum(Type type)
    {
        var named = type.GetCustomAttribute<JsonConverterAttribute>() is not null;
        var values = System.Enum.GetValues(type).Cast<object>()
            .Select(v => (System.Enum.GetName(type, v)!, Convert.ToInt64(v, CultureInfo.InvariantCulture))).ToList();
        return new EnumDesc(type.Name, named, Plain(System.Enum.GetUnderlyingType(type), null), values, type);
    }

    /// <summary>The records and enums a member's type reaches.</summary>
    private static IEnumerable<Type> Reached(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsEnum)
        {
            yield return t;
        }
        else if (t.IsGenericType)
        {
            foreach (var a in t.GetGenericArguments().SelectMany(Reached))
            {
                yield return a;
            }
        }
        else if (t.Namespace == "Online.Protocol" && t.IsClass)
        {
            yield return t;
        }
    }

    /// <summary>A member's type as messages.toml writes it: string, int, uint, long, byte, bool, float, double, time (a
    /// DateTimeOffset), list&lt;T&gt;, map&lt;T&gt; (string keys), a record's or an enum's name; "?" after it: may be
    /// null (a reference type's annotation, or a Nullable value).</summary>
    public static string TypeName(Type t, NullabilityInfo? n)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null)
        {
            return Plain(underlying, null) + "?";
        }
        var name = Plain(t, n);
        return !t.IsValueType && n is { ReadState: NullabilityState.Nullable } ? name + "?" : name;
    }

    private static string Plain(Type t, NullabilityInfo? n)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(uint)) return "uint";
        if (t == typeof(long)) return "long";
        if (t == typeof(byte)) return "byte";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(float)) return "float";
        if (t == typeof(double)) return "double";
        if (t == typeof(DateTimeOffset)) return "time";
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return $"list<{TypeName(t.GetGenericArguments()[0], n?.GenericTypeArguments[0])}>";
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && t.GetGenericArguments()[0] == typeof(string))
        {
            return $"map<{TypeName(t.GetGenericArguments()[1], n?.GenericTypeArguments[1])}>";
        }
        if (t.Namespace == "Online.Protocol")
        {
            return t.Name;
        }
        throw new InvalidOperationException($"no schema type for {t}");
    }

    private static string DefaultText(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => JsonSerializer.Serialize(s, Options),
        System.Enum e => JsonSerializer.Serialize(e, e.GetType(), Options),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"no default text for {value}"),
    };

    /// <summary>The schema as the lines messages.toml's are compared by (<c>LobbySchema.Lines</c>, which generate.py
    /// writes the same way): the enums, then the records, each by name.</summary>
    public static List<string> Lines(List<RecordDesc> records, List<EnumDesc> enums)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"link version={ControlProtocol.Version} path={ControlProtocol.Path} " +
                $"keepAlive={ControlProtocol.KeepAlive.TotalSeconds} global={ControlProtocol.GlobalChannel} lobby={ControlProtocol.LobbyChannel}"),
        };
        foreach (var e in enums.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            lines.Add($"enum {e.Name} {(e.Named ? "named" : "number")} {e.Underlying}");
            lines.AddRange(e.Values.Select(v => $"  {v.Name} = {v.Value.ToString(CultureInfo.InvariantCulture)}"));
        }
        foreach (var r in records.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            lines.Add($"record {r.Name}" + (r.Root is null ? "" : $" root={r.Root}") + (r.Discriminator is null ? "" : $" type={r.Discriminator}"));
            lines.AddRange(r.Members.Select(m => $"  {m.Name} {m.Type}" + (m.Default is null ? "" : $" default={m.Default}") +
                (m.Ignore is null ? "" : $" ignore={m.Ignore}") + (m.WriteOnly ? " writeonly" : "")));
        }
        return lines;
    }

    /// <summary>The schema as a first messages.toml (the records' order of reaching), for a new record to start from.</summary>
    public static string Toml(List<RecordDesc> records, List<EnumDesc> enums)
    {
        var s = new StringBuilder();
        foreach (var e in enums)
        {
            s.Append(CultureInfo.InvariantCulture, $"\n[[enum]]\nname = \"{e.Name}\"\nas = \"{(e.Named ? "name" : "number")}\"\n");
            s.Append(e.Underlying == "int" ? "" : $"underlying = \"{e.Underlying}\"\n");
            s.Append("values = [");
            s.AppendJoin(", ", e.Values.Select(v => $"\"{v.Name}\""));
            s.Append("]\n");
            if (e.Values.Select((v, i) => v.Value != i).Any(x => x))
            {
                throw new InvalidOperationException($"{e.Name}: values not 0, 1, 2...");
            }
        }
        foreach (var r in records)
        {
            s.Append(CultureInfo.InvariantCulture, $"\n[[record]]\nname = \"{r.Name}\"\n");
            if (r.Root is not null)
            {
                s.Append(CultureInfo.InvariantCulture, $"root = \"{r.Root}\"\n");
            }
            if (r.Discriminator is not null)
            {
                s.Append(CultureInfo.InvariantCulture, $"type = \"{r.Discriminator}\"\n");
            }
            s.Append("members = [\n");
            foreach (var m in r.Members)
            {
                s.Append(CultureInfo.InvariantCulture, $"    {{ name = \"{m.Name}\", type = \"{m.Type}\"");
                if (m.Default is not null)
                {
                    s.Append(CultureInfo.InvariantCulture, $", default = '{m.Default}'");
                }
                if (m.Ignore is not null)
                {
                    s.Append(CultureInfo.InvariantCulture, $", omit = \"{m.Ignore}\"");
                }
                if (m.WriteOnly)
                {
                    s.Append(", written = \"(a C++ expression)\"");
                }
                s.Append(" },\n");
            }
            s.Append("]\n");
        }
        return s.ToString();
    }
}
