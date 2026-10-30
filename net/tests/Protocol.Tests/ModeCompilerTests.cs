using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Online.Protocol;
using TestSupport;

namespace Protocol.Tests;

/// <summary>The game modes' compiler (Online.Protocol's Modes/) over the vectors of protocol/tests/vectors/modes/: each
/// <c>.fzmode</c> compiles to the package its <c>.fzmp.hex</c> holds, which lists as its <c>.fzmp.txt</c> and starts a
/// match as its <c>.mode.json</c>. The runner's interpreter tests load the same files, so a difference here is a change
/// of the bytecode both sides run. <c>FZGX_WRITE_MODE_VECTORS=1</c> writes them again from the compiler (then the
/// diff is the review).</summary>
public sealed class ModeCompilerTests
{
    private static string Vector(string name) => RepositoryFolder.Protocol("protocol", "tests", "vectors", "modes", name);

    private static bool Rewrite => Environment.GetEnvironmentVariable("FZGX_WRITE_MODE_VECTORS") == "1";

    /// <summary>A package as the vectors keep it: lower-case hex, 32 bytes a line, a newline after each.</summary>
    private static string Hex(byte[] bytes) =>
        string.Concat(bytes.Chunk(32).Select(line => Convert.ToHexStringLower(line) + "\n"));

    private static ModeCompilation CompileVector(string name)
    {
        var result = ModeCompiler.Compile(File.ReadAllText(Vector(name + ".fzmode")));
        Assert.Empty(result.Errors);
        return result;
    }

    [Theory]
    [InlineData("overdrive")]
    [InlineData("words")]
    [InlineData("competitive")]
    [InlineData("pairs")]
    [InlineData("language")]
    [InlineData("waits")]
    public void AVectorCompilesToItsPackage(string name)
    {
        var result = CompileVector(name);
        var package = result.Package!;
        var files = new Dictionary<string, string>
        {
            [name + ".fzmp.hex"] = Hex(package),
            [name + ".fzmp.txt"] = ModeDisassembler.List(package),
            [name + ".mode.json"] = result.Mode!.Start().ToJson() + "\n",
        };
        if (Rewrite)
        {
            foreach (var (file, text) in files)
            {
                File.WriteAllText(Vector(file), text);
            }
        }
        foreach (var (file, text) in files)
        {
            Assert.Equal(File.ReadAllText(Vector(file)), text);
        }
        Assert.Empty(result.Warnings);
        Assert.InRange(package.Length, 1, ModeWords.PackageBytes);
        Assert.InRange(result.Budget, 0, ModeWords.Budget);   // (0: a mode of setup words only, as Quick Play)
    }

    [Theory]
    [InlineData("overdrive")]
    [InlineData("words")]
    [InlineData("competitive")]
    [InlineData("pairs")]
    [InlineData("language")]
    [InlineData("waits")]
    public void APackageReadsBackAsItWasWritten(string name)
    {
        var result = CompileVector(name);
        var read = ModePackage.Read(result.Package!);
        Assert.Equal(result.Package, read.Write());
        Assert.Equal(result.Mode!.Rules, read.Rules);
        Assert.Equal(result.Mode.Code, read.Code);
        Assert.Equal(File.ReadAllText(Vector(name + ".fzmode")), read.Source);
    }

    [Fact]
    public void OverdriveIsTheDesignsFirstSlice()
    {
        var result = CompileVector("overdrive");
        var mode = result.Mode!;
        Assert.Equal(("Overdrive", "OVERDRIVE", 1), (mode.Name, mode.Badge, mode.Api));
        var rise = Assert.Single(mode.Params);
        Assert.Equal(("rise", ModeType.Int, 5, 50, 5, 10), (rise.Name, rise.Type, rise.Least, rise.Most, rise.Step, rise.Default));
        Assert.Equal([ModeSetup.CpusAllowed, ModeSetup.Laps, ModeSetup.PedalsLocked, ModeSetup.BoostOff], mode.Setups.Select(s => s.Id));
        // laps unlimited: 0; pedals locked: accel 1.0 as written, brake at the word's default 0.0.
        Assert.Equal(new ModeSetupValue(false, 0), Assert.Single(mode.Setups[1].Arguments));
        Assert.Equal([new ModeSetupValue(false, 0x3F800000), new ModeSetupValue(false, 0)], mode.Setups[2].Arguments);
        Assert.Equal([0], mode.SeatWords);
        Assert.Equal([ModeEvent.EachSeat, ModeEvent.Out], mode.Rules.Select(r => r.Event));
        Assert.Equal([1, 1], mode.Rules.Select(r => r.Locals));
        Assert.Equal(ModeRule.None, mode.Rules[0].When);
        Assert.Equal(0u, mode.Rules[0].While);
        // Lock the speed: (7 for its while + 13 for its body) x 30 seats; Out: 4 x 30.
        Assert.Equal(720, result.Budget);
    }

    /// <summary>A vector's listing's code lines: "  OFFSET  Op operands".</summary>
    private static List<string> Code(string name) =>
        ModeDisassembler.List(CompileVector(name).Package!).Split('\n').Select(l => Regex.Match(l, "^  [0-9a-f]{4}  (.*)$"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public void TheWordsVectorHoldsEveryOperationAndWord()
    {
        var result = CompileVector("words");
        var mode = result.Mode!;
        // every word, and with the language vector every operation (the words vector's are the first slice's)
        var code = Code("words");
        var ops = code.Concat(Code("language")).Select(l => l.Split(' ')[0]).ToHashSet();
        Assert.Equal(Enum.GetNames<ModeOp>().Order(), ops.Order());
        Assert.All(ModeWords.SeatReads, r => Assert.Contains(code.Concat(Code("language")), l => l.EndsWith("SeatRead " + r.Name, StringComparison.Ordinal)));
        Assert.All(ModeWords.RaceReads, r => Assert.Contains(code, l => l.EndsWith("RaceRead " + r.Name, StringComparison.Ordinal)));
        Assert.All(ModeWords.Actions, a => Assert.Contains(code, l => l.EndsWith("Action " + a.Name, StringComparison.Ordinal)));
        // every setup word but laps by course (one of the two laps words: the competitive vector has the other)
        Assert.Equal(ModeWords.Setups.Select(s => s.Id).Where(id => id != ModeSetup.LapsByCourse).Order(), mode.Setups.Select(s => s.Id).Order());
        Assert.Equal(ModeWords.Events.Select(e => e.Id).Order(), mode.Rules.Select(r => r.Event).Distinct().Order());
        Assert.Equal(Enum.GetValues<ModeParamKind>(), mode.Params.Select(p => p.Kind).Distinct().Order());
        Assert.Contains(mode.Params, p => p.Type == ModeType.Real);
        Assert.Contains(mode.Setups.SelectMany(s => s.Arguments), a => a.FromParam);
        Assert.Equal(3845, result.Budget);
    }

    [Fact]
    public void QuickPlaysLapsAreByCourse()
    {
        var mode = CompileVector("competitive").Mode!;
        var read = ModePackage.Read(mode.Write());
        Assert.Equal(8, read.LapsFor(36));   // Mute City: Sonic Oval (courses.toml)
        Assert.Equal(6, read.LapsFor(1));    // Mute City: Twist Road
        Assert.Equal(3, read.LapsFor(2));    // left out: else
        Assert.Equal(0, CompileVector("overdrive").Mode!.LapsFor(1));   // laps unlimited
        Assert.Contains("is no course of the game's", Errors("mode \"M\" { api 1; } laps by course { \"Mute City: Nowhere\": 3; else: 3; }"));
        Assert.Contains("one of them", Errors("mode \"M\" { api 1; } laps 3; laps by course { else: 3; }"));
        Assert.Contains("twice", Errors("mode \"M\" { api 1; } laps by course { \"Mute City: Sonic Oval\": 3; \"Mute City: Sonic Oval\": 4; else: 3; }"));
    }

    private static string Errors(string source) => string.Join("\n", ModeCompiler.Compile(source).Errors.Select(e => e.ToString()));

    [Fact]
    public void TheHashAndShareCodeAreThePackagesOwn()
    {
        var result = CompileVector("overdrive");
        var hash = SHA256.HashData(result.Package!);
        Assert.Equal(Convert.ToHexStringLower(hash), result.Hash);
        Assert.Equal(Base32(hash)[..8], result.ShareCode);
        var start = result.Mode!.Start([25]);
        Assert.Equal((result.Hash, 1), (start.Hash, start.Api));
        Assert.Equal([25], start.Params);
        Assert.Equal(result.Package, Convert.FromBase64String(start.Package));
        Assert.Throws<ArgumentException>(() => result.Mode.Start([55]));
    }

    /// <summary>RFC 4648 base32 of the whole of some bytes, upper case, written the long way for the test.</summary>
    private static string Base32(byte[] bytes)
    {
        var bits = string.Concat(bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        return string.Concat(Enumerable.Range(0, bits.Length / 5).Select(i => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[Convert.ToInt32(bits.Substring(i * 5, 5), 2)]));
    }

    [Fact]
    public void TheDisassemblersOperandsAreTheOpsSizes()
    {
        foreach (var op in Enum.GetValues<ModeOp>())
        {
            Assert.Equal(ModeWords.Size(op), 1 + ModeDisassembler.OperandBytes(op).Sum());
        }
    }

    [Fact]
    public void ThePackageReaderRefusesWhatIsNotAPackage()
    {
        var package = CompileVector("overdrive").Package!;
        var newer = (byte[])package.Clone();
        newer[7] = 2;
        Assert.Contains("api 2", Assert.Throws<InvalidDataException>(() => ModePackage.Read(newer)).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ModePackage.Read(package[..^1]));
        Assert.Throws<InvalidDataException>(() => ModePackage.Read([.. package, 0]));
        Assert.Throws<InvalidDataException>(() => ModePackage.Read([.. "FZMQ"u8, .. package[4..]]));
    }

    private const string Head = "mode \"Test\" { api 1; }\n";

    [Theory]
    [InlineData(Head + "laps 3;\nrolling_start 1000 km/h;\n", "line 3: 'rolling_start' is not a word of api 1")]
    [InlineData(Head + "global int n;\nrule \"R\" {\n  event every step;\n  do { n = 1.5; }\n}\n",
        "line 5: the int n takes an int, not a real: floor, ceil, round or trunc make an int of a real")]
    [InlineData(Head + "rule \"R\" {\n  event out(s);\n  while race.started;\n  do { eliminate s; }\n}\n",
        "line 4: while is for the events that go on (every step, each seat): out happens once, so its rule takes when")]
    [InlineData("mode \"Test\" {\n  api 2;\n}\n", "line 2: api 2 is not one this build knows: its newest is api 1")]
    [InlineData(Head + "param rise \"Rise\" number 5..50 step 5 default 60;\n", "line 2: rise's default 60 is outside 5..50")]
    [InlineData(Head + "param rise \"Rise\" number 50..5;\n", "line 2: rise's range 50..5 is empty: the least comes first")]
    [InlineData(Head + "laps 100;\n", "line 2: laps is 0..99")]
    [InlineData(Head + "rule \"R\" { event every step; do { race.steps = 1; } }\n", "line 2: race.steps is the game's to say: it cannot be assigned")]
    [InlineData(Head + "rule \"R\" { event every step; do { n = 1; } }\n", "line 2: 'n' is not declared")]
    public void AnErrorNamesItsLine(string source, string message)
    {
        var result = ModeCompiler.Compile(source);
        Assert.False(result.Succeeded);
        Assert.Null(result.Package);
        Assert.Equal([message], result.Errors);
    }

    [Theory]
    [InlineData("global real g;\nrule \"R\" { event every step; do { g = min(speed for each seat where racing) + max(energy for each seat)\n"
        + "  + sum(distance for each other where other.lap > 0); } }\n")]
    [InlineData("global bool g;\nrule \"R\" { event every step; do { g = any(broken for each seat) and all(racing for each seat where not out); } }\n")]
    [InlineData("seat int n;\nrule \"R\" { event every step; do { for each seat where index > 0 { seat.n = 1; seat.n += 2; } } }\n")]
    [InlineData("/* a comment\n over lines */ global real g = 2. * 50% + 1 - -1.5; // and one to the end\n"
        + "rule \"R\" { event out; when seat.lap < 2; do { eliminate seat; } }\n")]
    [InlineData("const int n = 2 * 3 + 1;\nglobal int g = n % 4;\nrule \"R\" { event every step; do { int x; seat s; real r = x; g = x + round(r); } }\n")]
    public void TheseCompile(string items)
    {
        var result = ModeCompiler.Compile(Head + items);
        Assert.Empty(result.Errors);
        Assert.NotNull(ModePackage.Read(result.Package!));
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; do { seat = none; } }\n", "line 2: 'seat' is the seat the rule or loop is on: it cannot be assigned")]
    [InlineData("rule \"R\" { event every step; do { int x = racing; } }\n", "line 2: 'racing' alone means a seat's read only where a loop names the seat: write seat.racing")]
    [InlineData("rule \"R\" { event every step; when race.steps; do { } }\n", "line 2: a condition takes a bool, not an int")]
    [InlineData("rule \"R\" { event every step; do { int x = 3 % 1.5; } }\n", "line 2: '%' takes whole numbers: use floor, ceil, round or trunc first")]
    [InlineData("param p \"P\" number 1..3;\nevery(p) { }\n", "line 3: every takes a time of at least one step (every(10s)) or a delay parameter")]
    [InlineData("pedals locked accel 1.5;\n", "line 2: pedals locked's accel is 0..1")]
    public void TheseAreRefused(string items, string message) =>
        Assert.Equal([message], ModeCompiler.Compile(Head + items).Errors);

    [Fact]
    public void AModeOverItsBudgetIsRefused()
    {
        // Every pair of seats (900 a step), each with a long body: some 100 operations a pair.
        var body = string.Concat(Enumerable.Repeat("near = near + a.speed * b.speed - a.energy * b.energy; ", 10));
        var source = Head + "global real near;\nrule \"Heavy\" {\n  event every step;\n  do {\n    for each a in seats {\n"
            + "      for each b in seats { " + body + "}\n    }\n  }\n}\n";
        var result = ModeCompiler.Compile(source);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("line 3: the mode takes ", error, StringComparison.Ordinal);
        Assert.Contains("past api 1's 65536", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedLoopOverEveryPairOfSeatsFits()
    {
        // 30 x 30 pairs, as a slipstream or a haunting needs: inside the budget, with a sorted loop in each seat's too
        var source = Head + "global real near;\nrule \"Pairs\" {\n  event every step;\n  do {\n    for each a in seats {\n"
            + "      for each b in seats { if (a.distance - b.distance < 10.0) { near = near + 1.0; } }\n    }\n  }\n}\n";
        var result = ModeCompiler.Compile(source);
        Assert.Empty(result.Errors);
        Assert.InRange(result.Budget, 900 * 10, ModeWords.Budget);
    }

    [Fact]
    public void ATimeOffTheStepIsRoundedWithAWarning()
    {
        var result = ModeCompiler.Compile(Head + "const int t = 25ms;\n");
        Assert.True(result.Succeeded);
        Assert.Equal(["line 2: 25ms is 1.5 steps at 60 a second: it is taken as 2"], result.Warnings);
    }

    [Fact]
    public void EveryIsTheLibrarysRule()
    {
        var every = ModeCompiler.Compile(Head + "global int n;\nevery(10s) { n += 1; }\n");
        var written = ModeCompiler.Compile(Head + "global int n;\nrule \"every 10s\" {\n  event every step;\n"
            + "  while race.started and race.steps > 0 and race.steps % 600 == 0;\n  do { n += 1; }\n}\n");
        Assert.Equal(written.Mode!.Rules, every.Mode!.Rules);
        Assert.Equal(written.Mode.Code, every.Mode.Code);
        Assert.Equal("every 10s", every.Mode.Rules[0].Label);
    }
}
