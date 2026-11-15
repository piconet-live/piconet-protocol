using System.Text;
using System.Text.Json;
using MessageVectors;
using Online.Protocol;

// The lobby link's records against messages.toml and the vectors (MessageVectors.csproj says why).
//   MessageVectors schema          the records' lines equal messages.toml's (generated/LobbySchema.g.cs)
//   MessageVectors check FILE      every line of FILE (ROOT, TYPE, INPUT, EXPECTED, as vectors/messages/messages.tsv)
//                                  reads as EXPECTED says: "=" written again as INPUT, "!" refused, else written as it
//   MessageVectors write FILE      writes the vectors (a seeded draw of every record, each in some forms no writer
//                                  writes, and the fixed cases), each line as C# reads and writes it
//   MessageVectors lines | toml    the records' lines, or the records as messages.toml would write them
var (records, enums) = Schema.Reflect();
switch (args)
{
    case ["schema"]:
        return SchemaCheck();
    case ["check", var file]:
        return Check(file);
    case ["write", var file]:
        Write(file);
        return 0;
    case ["toml"]:
        Console.Write(Schema.Toml(records, enums));   // (the records as a first messages.toml: for a new record to start from)
        return 0;
    case ["lines"]:
        Schema.Lines(records, enums).ForEach(Console.WriteLine);
        return 0;
    default:
        Console.Error.WriteLine("usage: MessageVectors schema | check FILE | write FILE | lines | toml");
        return 2;
}

int SchemaCheck()
{
    var actual = Schema.Lines(records, enums);
    var expected = LobbySchema.Lines;
    var failures = 0;
    foreach (var line in actual.Except(expected))
    {
        Console.Error.WriteLine($"lobby schema: Online.Protocol has  {line}");
        failures++;
    }
    foreach (var line in expected.Except(actual))
    {
        Console.Error.WriteLine($"lobby schema: messages.toml has    {line}");
        failures++;
    }
    if (failures == 0 && !actual.SequenceEqual(expected))
    {
        Console.Error.WriteLine("lobby schema: the same lines in another order (a member moved)");
        failures++;
    }
    Console.WriteLine($"lobby schema: {records.Count} records and {enums.Count} enums of Online.Protocol " +
        (failures == 0 ? "equal messages.toml" : $"differ from messages.toml in {failures} lines"));
    return failures == 0 ? 0 : 1;
}

// What C# makes of a text of a root: its writing again; "!" when it reads no message (an error, or null); "~" when
// it reads one it cannot write (a float that is not finite: System.Text.Json writes none).
static string Read(string root, string input)
{
    object? read;
    try
    {
        read = root switch
        {
            "client" => ControlProtocol.DecodeClient(input),
            "server" => ControlProtocol.DecodeServer(input),
            "lan" => JsonSerializer.Deserialize<LanAnnouncement>(input, ControlProtocol.Json),
            "status" => JsonSerializer.Deserialize<ServiceStatus>(input, ControlProtocol.Json),
            _ => throw new InvalidOperationException(root),
        };
    }
    catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
    {
        // (an ArgumentException: a record's constructor refused a value)
        return "!";
    }
    try
    {
        return read switch
        {
            null => "!",
            ClientMessage c => ControlProtocol.Encode(c),
            ServerMessage s => ControlProtocol.Encode(s),
            _ => JsonSerializer.Serialize(read, read.GetType(), ControlProtocol.Json),
        };
    }
    catch (ArgumentException)
    {
        return "~";
    }
}

int Check(string file)
{
    int lines = 0, failures = 0;
    foreach (var line in File.ReadLines(file, Encoding.UTF8))
    {
        if (line.Length == 0 || line[0] == '#')
        {
            continue;
        }
        var f = line.Split('\t');
        if (f.Length != 4)
        {
            Console.Error.WriteLine($"lobby vectors: not four columns: {line}");
            failures++;
            continue;
        }
        lines++;
        var got = Read(f[0], f[2]);
        var want = f[3] == "=" ? f[2] : f[3];
        if (got != want)
        {
            failures++;
            if (failures <= 20)
            {
                Console.Error.WriteLine($"lobby vectors: {f[0]} {f[1]}\n  input    {f[2]}\n  C#       {got}\n  expected {want}");
            }
        }
    }
    Console.WriteLine($"lobby vectors (C#): {lines} lines of {Path.GetFileName(file)}, {failures} differences");
    return failures == 0 && lines > 0 ? 0 : 1;
}

void Write(string file)
{
    var samples = new Samples(20261114);
    var output = new List<string>
    {
        "# The lobby link's vectors (protocol/README.md): ROOT, TYPE, INPUT, EXPECTED, a tab between. EXPECTED: \"=\" the",
        "# input is what C# writes, and reads to the same; \"!\" C# reads no message from it; \"~\" it reads one it cannot",
        "# write (a float not finite); else C#'s writing of what it reads. Written by tests/dotnet-messages (write) from Online.Protocol's records: a seeded draw of every record,",
        "# some of each in forms no writer writes, then the fixed cases. A change here is a change of the wire.",
    };
    var seen = new HashSet<string>();
    void Add(string root, string type, string input)
    {
        if (input.Contains('\t', StringComparison.Ordinal) || input.Contains('\n', StringComparison.Ordinal) || !seen.Add(root + "\t" + input))
        {
            return;
        }
        var read = Read(root, input);
        output.Add($"{root}\t{type}\t{input}\t{(read == input ? "=" : read)}");
    }
    foreach (var r in records.Where(r => r.Root is not null))
    {
        var count = r.Root is "status" ? 3 : r.Members.Count == 0 ? 1 : 10;
        for (var i = 0; i < count; i++)
        {
            var value = samples.Record(r.Clr, 0);
            var json = r.Root switch
            {
                "client" => ControlProtocol.Encode((ClientMessage)value),
                "server" => ControlProtocol.Encode((ServerMessage)value),
                _ => JsonSerializer.Serialize(value, r.Clr, ControlProtocol.Json),
            };
            Add(r.Root!, r.Discriminator ?? r.Name, json);
            if (i < 2)
            {
                foreach (var v in samples.Variants(json))
                {
                    Add(r.Root!, r.Discriminator ?? r.Name, v);
                }
            }
        }
    }
    foreach (var (root, input) in FixedCases.All)
    {
        Add(root, "fixed", input);
    }
    File.WriteAllLines(file, output, new UTF8Encoding(false));
    Console.WriteLine($"lobby vectors: {output.Count - 4} lines written to {file}");
}
