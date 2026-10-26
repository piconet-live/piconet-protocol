using System.Globalization;
using Launcher.Core;

// The generated C# reader over the shared samples: each file's parts as the line replays.tsv holds for it (written by
// the hand-written reader the generated ones replaced), the same line the game's core test reads its files to.
//   ReplayVectors FOLDER (protocol/tests/vectors/replays)
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: ReplayVectors FOLDER");
    return 2;
}
var failures = 0;
var versions = new HashSet<int>();
var temp = Directory.CreateTempSubdirectory("replay-vectors").FullName;
foreach (var line in File.ReadLines(Path.Combine(args[0], "replays.tsv")))
{
    if (line.Length == 0 || line[0] == '#')
    {
        continue;
    }
    var name = line[..line.IndexOf('\t', StringComparison.Ordinal)];
    // the sample as a file (NAME.hex holds its bytes as hex text: the repository takes no binary file)
    var bytes = Convert.FromHexString(string.Concat(File.ReadAllLines(Path.Combine(args[0], name + ".hex"))));
    var path = Path.Combine(temp, name);
    File.WriteAllBytes(path, bytes);
    var got = Describe(name, path, bytes);
    if (got != line)
    {
        failures++;
        Console.Error.WriteLine($"replay vectors: {name}:\n  read    {got}\n  vectors {line}");
    }
    versions.Add(ReplayParts.Read(path).Version);
}
foreach (var v in new[] { 2, 3, 4, 5, 6, 7 })
{
    if (!versions.Contains(v))
    {
        failures++;
        Console.Error.WriteLine($"replay vectors: no sample of version {v}");
    }
}
Directory.Delete(temp, recursive: true);
Console.WriteLine($"replay vectors (C#): {versions.Count} versions, {failures} differences");
return failures == 0 ? 0 : 1;

static string Fnv(ReadOnlySpan<byte> data)
{
    var h = 0xcbf29ce484222325ul;
    foreach (var b in data)
    {
        h = (h ^ b) * 0x100000001b3ul;
    }
    return h.ToString("x16", CultureInfo.InvariantCulture);
}

static string Describe(string name, string path, byte[] d)
{
    var p = ReplayParts.Read(path);
    var cells = new List<string>
    {
        name, I(p.Version), I(p.HeaderLength), Fnv(p.Header), I(p.Mask), I(p.Seat),
    };
    if (p.Lineup)
    {
        var l = ReplayFormat.DecodeLineup(d.AsSpan((int)p.LineupAt, (int)p.LineupLength));
        cells.AddRange([I(p.LineupLength), Convert.ToHexStringLower(l.Machines), Convert.ToHexStringLower(l.Ranks),
            string.Join(",", l.Settings.Select(s => I(s))), I(l.ControlMode), I(l.RelD70)]);
    }
    else
    {
        cells.AddRange(["-", "-", "-", "-", "-", "-"]);
    }
    var rows = new List<byte>();
    var sums = new List<byte>();
    for (var step = 0L; step < p.Steps; step++)
    {
        var at = (int)(p.StepsAt + (step * p.StepSize));
        var row = (int)p.StepSize - ReplayFormat.ChecksumSize;
        rows.AddRange(d.AsSpan(at, row));
        sums.AddRange(d.AsSpan(at + row, ReplayFormat.ChecksumSize));
    }
    cells.AddRange([I(p.Steps), p.Whole ? "1" : "0", Fnv(rows.ToArray()), Fnv(sums.ToArray())]);
    if (p.Results is { } json)
    {
        var entries = ReplayFormat.ParseResults(json).Select(e =>
            $"{I(e.Seat)}:{I(e.Rank)}:{(e.Finished ? 1 : 0)}:{(e.Out ? 1 : 0)}:{I(e.Laps)}:{(e.TimeMs is { } t ? I(t) : "-")}:{I(e.BestLapMs)}");
        cells.AddRange([I(json.Length), Fnv(json), string.Join(" ", entries)]);
    }
    else
    {
        cells.AddRange(["-", "-", "-"]);
    }
    return string.Join("\t", cells);
}

static string I<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
