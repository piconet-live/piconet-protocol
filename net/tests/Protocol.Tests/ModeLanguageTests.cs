using System.Globalization;
using Online.Protocol;
using TestSupport;

namespace Protocol.Tests;

/// <summary>The mode language past the first slice (piconet-docs research/game-modes/language.md): subs, waits, the
/// team scope, sets and their helpers, vecs, random numbers, the standard library and its macros, the game's square
/// root and the load-time errors of each. The vectors' own tests (every vector compiles to its package) are
/// <see cref="ModeCompilerTests"/>'s; the runner's interpreter runs language, waits and sqrt.tsv the same way.</summary>
public sealed class ModeLanguageTests
{
    private const string Head = "mode \"Test\" { api 1; }\n";

    private static string Vector(string name) => RepositoryFolder.Protocol("protocol", "tests", "vectors", "modes", name);

    private static ModeCompilation Compile(string items)
    {
        var result = ModeCompiler.Compile(Head + items);
        Assert.Empty(result.Errors);
        return result;
    }

    private static string Error(string items) => Assert.Single(ModeCompiler.Compile(Head + items).Errors);

    /// <summary>A real's cell as the language keeps it: its bits, a NaN 0x7FC00000.</summary>
    private static int Cell(float value) => float.IsNaN(value) ? 0x7FC00000 : BitConverter.SingleToInt32Bits(value);

    [Fact]
    public void ARuleWithNothingToKeepStillHasItsLocal()
    {
        var rule = Assert.Single(Compile("global int n;\nrule { event go; do { n += 1; } }\n").Mode!.Rules);
        Assert.Equal(1, rule.Locals);                             // the runner's reader refuses 0
    }

    [Fact]
    public void ByNamesAnEventsSeatAndStaysTheSortWord()
    {
        const string body = "do { award by, 5; for each seat where seat != by by seat.distance { award seat, 1; } }";
        var spelled = Compile("rule { event ko(v, by); " + body + " }\n");
        var plain = Compile("rule { event ko(v, k); " + body.Replace("award by", "award k").Replace("!= by by", "!= k by") + " }\n");
        Assert.Equal(plain.Mode!.Code, spelled.Mode!.Code);       // the same code as with any other name
        Compile("rule { event hit(by, b); do { award by, 1; } }\n");
        // only an event's names: everywhere else it is the language's word
        Assert.Contains("'by' is a word of the language", Error("global int by;\n"));
        Assert.Contains("'by' is a word of the language", Error("rule { event each seat; do { int by = 1; } }\n"));
    }

    [Fact]
    public void TheLibraryReadsAndHoldsItsMacrosAndSubs()
    {
        Assert.Equal(["at", "every", "lives", "pits_off", "time_limit"], ModeCompiler.Library.Macros);
        Assert.Equal(["dist", "gap_to"], ModeCompiler.Library.Subs.Order());
        // a library sub costs nothing where it is not called
        Assert.Equal(0, Compile("global int n;\n").Budget);
        Assert.Empty(Compile("global int n;\n").Mode!.Code);
    }

    [Fact]
    public void AtIsTheLibrarysRule()
    {
        var at = Compile("global int n;\nat(4min) { n += 1; }\n");
        var written = Compile("global int n;\nrule \"at 4min\" {\n  event every step;\n"
            + "  when race.started and race.steps >= 14400;\n  do { n += 1; }\n}\n");
        Assert.Equal(written.Mode!.Rules, at.Mode!.Rules);
        Assert.Equal(written.Mode.Code, at.Mode.Code);
    }

    [Fact]
    public void AMacroPutsItsArgumentsInForItsParameters()
    {
        const string Library = """
            // a test library: an int and a block, a macro inside a macro, a variable it declares
            macro twice(int n, block body) {
              global int twice_count = n;
              rule { event every step; while race.started; do body }
              thrice(n) { twice_count += 1; }
            }
            macro thrice(int t, block body) {
              rule { event every step; when race.steps >= t * 3; do body }
            }
            macro later(time t, block body) {
              rule { event every step; when race.steps >= t; do body }
            }
            sub int doubled(int x) { return x * 2; }
            """;
        var result = ModeCompiler.Compile(Head + "twice(2 + 1) {\n  twice_count = doubled(twice_count);\n}\n", Library);
        Assert.Empty(result.Errors);
        var mode = result.Mode!;
        Assert.Equal([3], mode.Globals);
        Assert.Equal(["twice 2 + 1", "thrice ( 2 + 1 )"], mode.Rules.Select(r => r.Label));
        // a time is checked where it is given: 2 + 1 is no time
        Assert.Equal(["line 2: later takes a time of at least one step (later(10s)) or a delay parameter"],
            ModeCompiler.Compile(Head + "later(2 + 1) { }\n", Library).Errors);
    }

    [Theory]
    [InlineData("lives;\n", "line 2: lives takes 1 value (n), not 0")]
    [InlineData("every(10s);\n", "line 2: every takes a block: every(t) { ... }")]
    [InlineData("param p \"P\" number 1..3;\nat(p) { }\n", "line 3: at takes a time of at least one step (at(10s)) or a delay parameter")]
    [InlineData("macro m() { }\n", "line 2: macros are the standard library's (api 1): a mode file reuses its code with a sub")]
    public void ALibraryMacroUsedWronglyIsRefusedAtItsUse(string items, string message) =>
        Assert.Equal(message, Error(items));

    [Fact]
    public void TheLibrarysSubsAreCalledLikeAFilesOwn()
    {
        var result = Compile("global real g;\nrule \"R\" { event each seat; do { g = gap_to(seat, none) + dist(seat, none); } }\n");
        Assert.InRange(result.Budget, 1, ModeWords.Budget);
        Assert.Equal("line 2: 'gap_to' is a sub of the standard library's: name yours otherwise",
            Error("sub real gap_to(seat a, seat b) { return 0.; }\n"));
        Assert.Equal("line 2: 'dist' is a sub of the standard library's: name yours otherwise", Error("global int dist;\n"));
    }

    [Theory]
    // subs
    [InlineData("sub a() { call b(); }\nsub b() { call a(); }\n", "line 3: the sub a calls itself (a -> b -> a): a sub may not, directly or through another")]
    [InlineData("sub int f(int x) { return f(x); }\n", "line 2: the sub f calls itself (f -> f): a sub may not, directly or through another")]
    [InlineData("sub f(int x) { }\nrule \"R\" { event every step; do { call f(); } }\n", "line 3: f takes 1 argument, not 0")]
    [InlineData("sub f() { }\nglobal int g;\nrule \"R\" { event every step; do { g = f(); } }\n", "line 4: the sub f gives no value: call it as a statement, call f(...);")]
    [InlineData("rule \"R\" { event every step; do { return; } }\n", "line 2: return is a sub's: a rule's body ends at its end")]
    [InlineData("sub int f() { return; }\n global int g;\nrule \"R\" { event every step; do { g = f(); } }\n", "line 2: the sub f gives an int: return it")]
    [InlineData("global int g;\nrule \"R\" { event every step; do { call g(); } }\n", "line 3: 'g' is not a sub: call takes a sub")]
    [InlineData("sub f(seat s) { int n = racing; }\nrule \"R\" { event each seat; do { call f(seat); } }\n",
        "line 2: 'racing' alone means a seat's read only where a loop names the seat: write seat.racing")]
    // waits
    [InlineData("rule \"R\" { event every step; do { for each seat by speed { wait 1; } } }\n", "line 2: a rule cannot wait inside a sorted loop (for each ... by): the order is the step's")]
    [InlineData("sub int f() { wait 1; return 1; }\nglobal int g;\nrule \"R\" { event every step; do { g = f(); } }\n",
        "line 2: a wait stands in a rule's body (or a sub called with call), not in a sub called inside an expression or a condition")]
    [InlineData("rule \"R\" { event each seat; do { vec a; vec b; vec c; wait 1; } }\n",
        "line 2: a waiting rule keeps at most 8 locals (its event's seat, the loops' and a vec's three included): here it would keep 10")]
    [InlineData("rule \"R\" { event every step; do { wait 1.5; } }\n", "line 2: wait takes an int, not a real: floor, ceil, round or trunc make an int of a real")]
    [InlineData("rule \"A\" { event each seat; do { wait 1; } }\nrule \"B\" { event each seat; do { wait 1; } }\nrule \"C\" { event each seat; do { wait 1; } }\n",
        "line 4: a mode has at most 64 rules waiting at once, a rule on a seat counted for every seat: with this one, 90 could")]
    [InlineData("rule \"R\" { event every step; do { wait 1 abort when 3; } }\n", "line 2: abort when takes a bool, not an int")]
    // teams, sets, vecs
    [InlineData("team int t;\nglobal int g;\nrule \"R\" { event every step; do { g = t; } }\n", "line 4: 't' is kept per team: read it as seat.team.t or s.team.t")]
    [InlineData("global vec v;\n", "line 2: a vec is a value of the step, not one to keep: keep its x, y and z as reals")]
    [InlineData("global int g;\nrule \"R\" { event every step; do { g = 3 & 5; } }\n", "line 3: '&' is the sets' intersection: it takes sets of seats")]
    [InlineData("global seats s;\nrule \"R\" { event every step; do { s = s + 1; } }\n", "line 3: '+' cannot put a set of seats with an int")]
    [InlineData("global seats s;\nrule \"R\" { event every step; do { s = s * s; } }\n", "line 3: '*' does not take a set of seats: + - & == != and in do")]
    [InlineData("global real r;\nrule \"R\" { event each seat; do { r = dot(seat.position, 1.); } }\n", "line 3: a vec's operation takes a vec, not a real")]
    [InlineData("global real r;\nrule \"R\" { event each seat; do { vec v = seat.position * seat.position; } }\n", "line 3: '*' does not take two vecs: + and - do (dot(a, b) multiplies)")]
    [InlineData("global seat s;\nrule \"R\" { event every step; do { s = nth(seats by speed desc); } }\n", "line 3: nth takes its keys, then the place: nth(S where c by key, i)")]
    [InlineData("global int g;\nrule \"R\" { event every step; do { g = random(1.5); } }\n", "line 3: random takes an int, not a real: floor, ceil, round or trunc make an int of a real")]
    [InlineData("global int g;\nrule \"R\" { event every step; do { g = random(); } }\n", "line 3: random takes one whole number, n (0 to n - 1), not 0 arguments")]
    public void TheseAreRefused(string items, string message) => Assert.Equal(message, Error(items));

    [Fact]
    public void ARuleThatWaitsIsCountedTwice()
    {
        var once = Compile("seat int n;\nrule \"R\" { event each seat; do { seat.n += 1; } }\n").Budget;
        var waits = Compile("seat int n;\nrule \"R\" { event each seat; do { seat.n += 1; wait 1; } }\n").Budget;
        Assert.Equal(2 * (once + (30 * 2)), waits);   // its Push and Wait more, a seat each
    }

    [Fact]
    public void TheWaitsVectorKeepsItsLocalsAndItsTeams()
    {
        var mode = ModeCompiler.Compile(File.ReadAllText(Vector("waits.fzmode"))).Mode!;
        Assert.Equal([0], mode.TeamWords);
        Assert.Equal([0, 0], mode.SeatWords);
        var listing = ModeDisassembler.List(mode.Write());
        Assert.Contains("Wait locals 2\n", listing, StringComparison.Ordinal);
        Assert.Contains("Wait locals 1 abort when", listing, StringComparison.Ordinal);
        Assert.Contains("Wait locals 0 restart when", listing, StringComparison.Ordinal);
        // a match start with teams: the seats' team numbers ride with it; out of range, refused
        var start = mode.Start(teams: [0, 0, 1, 1]);
        Assert.Contains("\"teams\":[0,0,1,1]", start.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("teams", mode.Start().ToJson(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => mode.Start(teams: [30]));
    }

    [Theory]
    [InlineData("global seats g = none;\n", 0)]
    [InlineData("global seats g;\n", 0)]
    [InlineData("const seat s = none;\nglobal seats g = s;\n", 0)]
    public void ASetsStartIsAConstant(string items, int start) => Assert.Equal([start], Compile(items).Mode!.Globals);

    [Theory]
    [InlineData("global seats m;\nglobal bool b;\nrule \"R\" { event each seat; do { m += seat; m -= seat; b = seat in m and m == seats - seat & m; } }\n")]
    [InlineData("global seat s;\nrule \"R\" { event every step; do { s = first(seats); s = last(seats by distance); s = nth(seats where racing, 3); } }\n")]
    [InlineData("global real r;\nrule \"R\" { event every step; do { r = max(seat.speed for each seat in seats - first(seats) where racing); } }\n")]
    [InlineData("global real r;\nrule \"R\" { event each seat; do { vec v = seat.position; v = v / 2 + 2 * v - -v; r = length(v) + v.z; } }\n")]
    [InlineData("global real r;\nrule \"R\" { event every step; do { r = random_real() * random(10); } }\n")]
    [InlineData("sub tag(seat s) { if (s == none) { return; } s.n += 1; }\nseat int n;\nrule \"R\" { event each seat; do { call tag(seat); } }\n")]
    [InlineData("team seats mates;\nrule \"R\" { event each seat; do { seat.team.mates += seat; } }\n")]
    public void TheseCompile(string items) => Assert.NotNull(ModePackage.Read(Compile(items).Package!));

    [Fact]
    public void ASubSeesItsOwnLocalsNotItsCallers()
    {
        Assert.Equal("line 2: 'x' is not declared",
            Error("sub int f() { return x; }\nglobal int g;\nrule \"R\" { event every step; do { int x = 1; g = f(); } }\n"));
        // the same names in the sub and its caller are two locals
        Compile("sub int f(int x) { int y = x * 2; return y; }\nglobal int g;\nrule \"R\" { event every step; do { int x = 3; int y = f(x); g = y; } }\n");
    }

    // The game's square root (ModeOperators.GameSqrt, the interpreter's GameSqrt): sqrt.tsv holds its bits for a range
    // of inputs, so the compiler's folds and the interpreter's runs give the same; FZGX_WRITE_MODE_VECTORS=1 writes it.
    [Fact]
    public void TheGamesSquareRootIsItsVectors()
    {
        var inputs = new List<int>
        {
            0, int.MinValue, 1, 0x007FFFFF, 0x00800000, 0x3F800000, 0x40000000, 0x40400000, 0x3E800000, 0x7F7FFFFF,
            0x7F800000, unchecked((int)0xFF800000), 0x7FC00000, unchecked((int)0xBF800000), 0x447A0000, 0x461C4000,
        };
        var x = 0x2545F491u;
        for (var i = 0; i < 240; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            inputs.Add((int)(0x00800000 + (x % (0x7F800000 - 0x00800000))));   // positive normal numbers
        }
        var lines = inputs.Select(i => string.Create(CultureInfo.InvariantCulture,
            $"{i:x8}\t{Cell(ModeMaths.GameSqrt(BitConverter.Int32BitsToSingle(i))):x8}\n")).ToList();
        var text = "# input bits\tthe game's square root's bits (ModeOperators.GameSqrt; the runner's GameSqrt)\n" + string.Concat(lines);
        if (Environment.GetEnvironmentVariable("FZGX_WRITE_MODE_VECTORS") == "1")
        {
            File.WriteAllText(Vector("sqrt.tsv"), text);
        }
        Assert.Equal(File.ReadAllText(Vector("sqrt.tsv")), text);
        // the edges, and within two units in the last place of the correctly rounded root everywhere else
        Assert.Equal(0f, ModeMaths.GameSqrt(-1f));
        Assert.Equal(0f, ModeMaths.GameSqrt(float.Epsilon));
        Assert.True(float.IsPositiveInfinity(ModeMaths.GameSqrt(float.PositiveInfinity)));
        Assert.True(float.IsNaN(ModeMaths.GameSqrt(float.NaN)));
        foreach (var i in inputs.Skip(16))
        {
            var v = BitConverter.Int32BitsToSingle(i);
            var off = Math.Abs(BitConverter.SingleToInt32Bits(ModeMaths.GameSqrt(v)) - BitConverter.SingleToInt32Bits(MathF.Sqrt(v)));
            Assert.InRange(off, 0, 2);
        }
        // a constant folds as the game's root
        Assert.Equal([Cell(ModeMaths.GameSqrt(2f))], Compile("global real r = sqrt(2);\n").Mode!.Globals);
    }
}
