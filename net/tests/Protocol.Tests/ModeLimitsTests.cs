using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Online.Protocol;
using TestSupport;

namespace Protocol.Tests;

/// <summary>A hostile mode file costs a refusal, never the process (Modes/ModeLimits.cs). The service compiles a host's
/// file on its coordinator's one loop, and .NET cannot catch a stack overflow: if any of these overflowed, this test
/// program would die, which is the failure. A hang ends at the test's time limit. Each file is compiled on a thread of a
/// small stack (<see cref="SmallStack"/>), and on a pool thread, in a program whose pool threads have that stack too.</summary>
public sealed class ModeLimitsTests
{
    private const string Head = "mode \"T\" { api 1; }\n";

    // Far under the stack a thread has by default on Linux (ulimit -s, usually 8 MB). A debug build's deepest file fits.
    private const int SmallStack = 256 * 1024;

    // The child program's mark: its pool threads have the small stack (see below).
    private const string SmallPool = "FZGX_TEST_SMALL_POOL";

    private const string TooDeep = "nested too deep";
    private const string TooMuchWork = "too much work to compile";
    private const string TooManyMacros = "the macros' uses so far";

    private static string Times(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    private static string Chain(string term, string op, int count) => string.Join(op, Enumerable.Repeat(term, count));

    private static string InRule(string body) =>
        $"global int g;\nglobal real r;\nrule {{ event every step; do {{ {body} }} }}\n";

    private static string InSeatRule(string body) =>
        $"global int g;\nglobal real r;\nrule {{ event each seat; do {{ {body} }} }}\n";

    /// <summary>Subs s0 to s(count-1), each with the body <paramref name="next"/> makes from its name's number and the
    /// sub below it, and a rule that calls the last.</summary>
    private static string SubChain(int count, string first, Func<string, string> next, string call) =>
        "global int g;\n" + first + "\n"
        + string.Concat(Enumerable.Range(1, count - 1).Select(i => next($"s{i - 1}").Replace("NAME", $"s{i}", StringComparison.Ordinal) + "\n"))
        + $"rule {{ event every step; do {{ {call.Replace("NAME", $"s{count - 1}", StringComparison.Ordinal)} }} }}\n";

    /// <summary>Every recursive path of the parser and the later passes, each past the limit, the long ones as long as an
    /// upload may be; and what each is refused with.</summary>
    private static readonly Dictionary<string, (string Text, string Refusal)> Hostile = new()
    {
        // the parser's descent
        ["brackets in a rule"] = (InRule("g = " + Times("(", 32000) + "1" + Times(")", 32000) + ";"), TooDeep),
        ["brackets in a constant"] = ("const int k = " + Times("(", 32000) + "1" + Times(")", 32000) + ";\n", TooDeep),
        ["brackets never closed"] = (InRule("g = " + Times("(", 60000)), TooDeep),
        ["not"] = (InRule("if (" + Times("not ", 16000) + "true) { }"), TooDeep),
        ["minus on a name"] = (InRule("g = " + Times("- ", 32000) + "g;"), TooDeep),
        ["minus on a number"] = (InRule("g = " + Times("- ", 32000) + "1;"), TooDeep),
        ["a function in a function"] = (InRule("g = " + Times("abs(", 12000) + "g" + Times(")", 12000) + ";"), TooDeep),
        ["min in min"] = (InRule("g = " + Times("min(1,", 9000) + "g" + Times(")", 9000) + ";"), TooDeep),
        ["count in count"] = (InRule("g = " + Times("count(seats where ", 3000) + "true" + Times(")", 3000) + ";"), TooDeep),
        ["a set in a set"] = (InRule("g = count(" + Times("(seats where ", 4000) + "true" + Times(")", 4000) + ");"), TooDeep),
        ["two functions in turn"] = (InRule("g = " + Times("floor(sqrt(", 5000) + "g" + Times("))", 5000) + ";"), TooDeep),
        ["blocks of if"] = (InRule(Times("if (true) { ", 5000) + Times("}", 5000)), TooDeep),
        ["blocks of repeat"] = (InRule(Times("repeat 0 { ", 5000) + Times("}", 5000)), TooDeep),
        ["blocks of for each"] = (InRule(Times("for each a in seats { ", 2500) + Times("}", 2500)), TooDeep),
        ["else if"] = (InRule("if (true) { }" + Times(" else if (true) { }", 3400)), TooDeep),
        ["a sub's blocks"] = ("sub s() { " + Times("if (true) { ", 5000) + Times("}", 5000) + " }\n", TooDeep),
        // left-deep trees: the parser's loops make them, the later passes recurse
        ["a sum in a rule"] = (InRule("g = " + Chain("1", "+", 32000) + ";"), TooDeep),
        ["a sum in a constant"] = ("const int k = " + Chain("1", "+", 32000) + ";\n", TooDeep),
        ["a sum in a start value"] = ("global int k = " + Chain("1", "+", 32000) + ";\n", TooDeep),
        ["a product"] = (InRule("g = " + Chain("1", "*", 32000) + ";"), TooDeep),
        ["and"] = (InRule("if (" + Chain("true", " and ", 6500) + ") { }"), TooDeep),
        ["or in a constant"] = ("const bool k = " + Chain("true", " or ", 7500) + ";\n", TooDeep),
        ["vecs"] = (InRule("vec v = " + Chain("vec(1,2,3)", "+", 5900) + ";"), TooDeep),
        ["fields"] = (InSeatRule("r = seat" + Times(".x", 32000) + ";"), TooDeep),
        ["fields assigned"] = (InSeatRule("seat" + Times(".x", 32000) + " = 1;"), TooDeep),
        ["a set's run"] = (InRule("for each a in " + Chain("seats", "+", 10000) + " { }"), TooDeep),
        ["when after when"] = ("rule { event every step; " + Times("when true; ", 5000) + "do { } }\n", TooDeep),
        // a run inside brackets, then more of it: no one level is long, the tree is
        ["runs in brackets"] = (InRule("g = " + Times("(", 60) + Times("1+1+1+1+1+1)+", 60) + "1;"), TooDeep),
        ["right to left"] = (InRule("g = " + Times("1+(", 15000) + "1" + Times(")", 15000) + ";"), TooDeep),
        // a sub's body inside its call: each file level is short, and only the writing goes deep
        ["subs calling subs"] = (SubChain(128, "sub s0() { g = 1; }", below => $"sub NAME() {{ call {below}(); }}", "call NAME();"), TooDeep),
        ["subs in blocks"] = (SubChain(128, "sub s0() { g = 1; }",
            below => $"sub NAME() {{ {Times("if (true) { ", 20)}call {below}();{Times("}", 20)} }}", "call NAME();"), TooDeep),
        ["subs in expressions"] = (SubChain(128, "sub int s0() { return 1; }",
            below => $"sub int NAME() {{ return {Times("(1+", 15)}{below}(){Times(")", 15)}; }}", "g = NAME();"), TooDeep),
        // the same, each sub calling the one below twice: 2^n calls
        ["subs calling subs twice"] = (SubChain(128, "sub s0() { g = 1; }", below => $"sub NAME() {{ call {below}(); call {below}(); }}", "call NAME();"), TooDeep),
        ["subs adding subs"] = (SubChain(128, "sub int s0() { return 1; }", below => $"sub int NAME() {{ return {below}() + {below}(); }}", "g = NAME();"), TooDeep),
        ["twice, shallow enough"] = (SubChain(40, "sub s0() { g = 1; }", below => $"sub NAME() {{ call {below}(); call {below}(); }}", "call NAME();"), TooMuchWork),
        ["a wide sub at many calls"] = ("global int g;\nsub w() { " + Times("g=1;", 2000) + "}\nrule { event every step; do { " + Times("call w();", 5700) + " } }\n", TooMuchWork),
        ["sums written many times"] = (InRule(Times("g=" + Chain("1", "+", 55) + ";", 480)), TooMuchWork),
        // macros: the arguments are parsed as they are, then expanded in the body
        ["a macro's value in brackets"] = ("lives " + Times("(", 32000) + "1" + Times(")", 32000) + ";\n", TooDeep),
        ["a macro's value, a sum"] = ("lives " + Chain("1", "+", 32000) + ";\n", TooDeep),
        ["a macro's time, a sum"] = ("every(" + Chain("1", "+", 32000) + ") { }\n", TooDeep),
        ["a macro's block"] = ("every(1s) { " + Times("if (true) { ", 5000) + Times("}", 5000) + " }\n", TooDeep),
        ["a macro used many times"] = (Times("lives 1;", 8000), TooManyMacros),
        ["a macro with a block used many times"] = (Times("every(1s){}", 5900), TooManyMacros),
    };

    public static TheoryData<string> HostileNames => [.. Hostile.Keys];

    /// <summary>The file compiled on a new thread of a small stack (this process's life is the check).</summary>
    private static ModeCompilation OnSmallStack(string text)
    {
        ModeCompilation? result = null;
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = ModeCompiler.Compile(text);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                thrown = e;
            }
        }, SmallStack);
        thread.Start();
        thread.Join();
        Assert.Null(thrown);
        return result!;
    }

    private static void AssertRefused(ModeCompilation result, string refusal)
    {
        var error = Assert.Single(result.Errors);
        Assert.Null(result.Package);
        Assert.Matches(@"^line [1-9][0-9]*: ", error);
        Assert.Contains(refusal, error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HostileNames))]
    public void AHostileFileIsRefusedOnASmallStack(string name)
    {
        var (text, refusal) = Hostile[name];
        var source = Head + text;
        Assert.InRange(source.Length, 1, LobbyRules.MostModeFile);   // what an upload may be
        AssertRefused(OnSmallStack(source), refusal);
    }

    /// <summary>The server's case: a handler on a pool thread, with whatever stack the pool's threads have. Alone, a
    /// pool thread's own; through <see cref="ThePoolsSmallStackHoldsEveryHostileFile"/>, the small one.</summary>
    [Fact]
    public async Task EveryHostileFileIsRefusedOnAPoolThread()
    {
        if (Environment.GetEnvironmentVariable(SmallPool) == "1")
        {
            // that the setting took: else the child's pass says nothing
            var room = await Task.Run(() => StackRoomInKilobytes(0), TestContext.Current.CancellationToken);
            Assert.InRange(room, 1, SmallStack / 1024);
        }
        foreach (var (name, (text, refusal)) in Hostile)
        {
            var result = await Task.Run(() => ModeCompiler.Compile(Head + text), TestContext.Current.CancellationToken);
            Assert.True(result.Errors.Count == 1, name);
            AssertRefused(result, refusal);
        }
    }

    /// <summary>How far a thread can go before the runtime says its stack runs short, a kilobyte a call.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int StackRoomInKilobytes(int so)
    {
        Span<byte> kilobyte = stackalloc byte[1024];
        kilobyte[so % 1024] = 1;
        return RuntimeHelpers.TryEnsureSufficientExecutionStack() ? StackRoomInKilobytes(so + 1) + kilobyte[so % 1024] - 1 : so;
    }

    /// <summary>This test program again, with the runtime's setting for the stack of every thread it makes without a size
    /// (<c>DOTNET_Thread_DefaultStackSize</c>, in hex), and only the test above. A stack overflow there ends the child, and
    /// this test fails on its exit status.</summary>
    [Fact]
    public async Task ThePoolsSmallStackHoldsEveryHostileFile()
    {
        if (Environment.GetEnvironmentVariable(SmallPool) == "1")
        {
            return;   // (the child's own run of this class's filter)
        }
        var program = Environment.ProcessPath!;
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(program) == "dotnet")
        {
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add($"{typeof(ModeLimitsTests).FullName}.{nameof(EveryHostileFileIsRefusedOnAPoolThread)}");
        start.Environment["DOTNET_Thread_DefaultStackSize"] = SmallStack.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        start.Environment[SmallPool] = "1";
        using var child = Process.Start(start)!;
        var error = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var output = child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await child.WaitForExitAsync(TestContext.Current.CancellationToken);
        var said = await output;
        Assert.True(child.ExitCode == 0, $"exit status {child.ExitCode}\n{said}\n{await error}");
        Assert.Contains("Failed: 0", said, StringComparison.Ordinal);
        Assert.Contains("Total: 1", said, StringComparison.Ordinal);   // it ran the test, and no other
    }

    /// <summary>The limit, where it falls: the deepest of each shape is compiled, on the small stack too; one past it is
    /// refused at its line. The shipped modes and the test vectors go 12 levels at most, and a stress file 33.</summary>
    [Theory]
    [InlineData("g = BRACKETS;")]
    [InlineData("g = SUM;")]
    [InlineData("g = ABS;")]
    [InlineData("g = MIN;")]
    [InlineData("IFS")]
    [InlineData("ELSEIFS")]
    public void TheLimitIsWhereItSays(string shape)
    {
        string File(int n) => Head + "global int g;\nrule { event every step; do {\n" + shape
            .Replace("BRACKETS", Times("(", n) + "1" + Times(")", n), StringComparison.Ordinal)
            .Replace("SUM", Chain("1", "+", n), StringComparison.Ordinal)
            .Replace("ABS", Times("abs(", n) + "g" + Times(")", n), StringComparison.Ordinal)
            .Replace("MIN", Times("min(1,", n) + "g" + Times(")", n), StringComparison.Ordinal)
            .Replace("ELSEIFS", "if (true) { }" + Times(" else if (true) { }", n), StringComparison.Ordinal)
            .Replace("IFS", Times("if (true) { ", n) + Times("}", n), StringComparison.Ordinal) + "\n} }\n";
        var deepest = Enumerable.Range(1, 80).TakeWhile(n => OnSmallStack(File(n)).Succeeded).Last();
        Assert.InRange(deepest, 50, 64);   // twice the stress file, and no more than the limit
        var refused = Assert.Single(OnSmallStack(File(deepest + 1)).Errors);
        Assert.StartsWith("line 4: this is nested too deep: a mode goes at most 64 levels", refused, StringComparison.Ordinal);
    }

    /// <summary>The type of an operator's two sides was found by writing them, at every level: 2^n writings for a sum of n
    /// terms, so a file of a hundred bytes took days. These end at the test's time limit if that comes back.</summary>
    [Theory]
    [InlineData("g = " + "1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1;", 101)]
    [InlineData("g = min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,min(1,1))))))))))))))))))))))))))))))))))))))));", 83)]
    public void ALongRunCompilesAtOnce(string body, long budget)
    {
        var result = ModeCompiler.Compile(Head + "global int g;\nrule { event every step; do { " + body + " } }\n");
        Assert.Empty(result.Errors);
        Assert.Equal(budget, result.Budget);
    }

    [Fact]
    public void SubsNeverCalledAreWalkedOnce()
    {
        // subs calling the one below twice, and no rule: the cycle check alone took 2^n
        var subs = string.Concat(Enumerable.Range(1, 127).Select(i => $"sub s{i}() {{ call s{i - 1}(); call s{i - 1}(); }}\n"));
        Assert.Empty(ModeCompiler.Compile(Head + "global int g;\nsub s0() { g = 1; }\n" + subs).Errors);
    }

    [Fact]
    public void ASubsCycleIsStillFoundWithItsPath()
    {
        const string subs = "sub a() { call b(); call b(); }\nsub b() { call c(); }\nsub c() { call a(); }\n";
        var error = Assert.Single(ModeCompiler.Compile(Head + subs).Errors);
        Assert.Equal("line 4: the sub a calls itself (a -> b -> c -> a): a sub may not, directly or through another", error);
    }

    /// <summary>Long repeats nested: the count wrapped past a long, and a negative count passed the budget.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(20)]
    public void NestedLongRepeatsArePastTheBudget(int nested)
    {
        var source = Head + InRule(Times("repeat 2147483647 { ", nested) + "g = 1;" + Times("}", nested));
        var error = Assert.Single(ModeCompiler.Compile(source).Errors);
        Assert.StartsWith("line 4: the mode takes ", error, StringComparison.Ordinal);
        Assert.Contains("past api 1's 65536", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("99999999999999999999999999999999999999s")]
    [InlineData("99999999999999999999999999999999999999ms")]
    [InlineData("99999999999999999999999999999999999999min")]
    [InlineData("99999999999999999999999999999999999999 steps")]
    [InlineData("79228162514264337593min")]
    public void ATimeNoDecimalHoldsIsRefused(string time)
    {
        // (a decimal's overflow came out of the compiler as an exception no caller expects)
        var error = Assert.Single(ModeCompiler.Compile(Head + "global int g;\nrule { event every step; do { g = " + time + "; } }\n").Errors);
        Assert.Contains("is longer than an int of steps can hold", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilePastAnyPackageIsRefusedUnread()
    {
        var error = Assert.Single(ModeCompiler.Compile(Times("(", ModeWords.PackageBytes + 1)).Errors);
        Assert.Equal("line 1: the file is 65537 characters, past api 1's 65536 bytes for a whole package", error);
    }

    /// <summary>The rest of the way up: a lobby's message, the mode file inside it, is JSON. The reader counts its depth
    /// and throws what the service's decoder catches (LauncherMessages.Decode). Stat pack files are read a line at a time;
    /// the only TOML is a server's own quickplay.toml, never a host's.</summary>
    [Theory]
    [InlineData("[", "]")]
    [InlineData("{\"a\":", "}")]
    public void DeepJsonIsRefusedOnASmallStack(string open, string close)
    {
        var text = "{\"type\":\"create-lobby\",\"rules\":" + Times(open, 100_000) + "1" + Times(close, 100_000) + "}";
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                ControlProtocol.DecodeClient(text);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                thrown = e;
            }
        }, SmallStack);
        thread.Start();
        thread.Join();
        Assert.NotNull(thrown);
    }

    /// <summary>The compiler's limits change no mode: each shipped file to the hash it had before them. A shipped file's
    /// own change writes its new hash here (<c>ModeCompilerTests</c> holds the vectors' bytes).</summary>
    [Theory]
    [InlineData("competitive", "f5249b0048616c1e643ee4a5caed4fc3b80b55d33dfe5542134981215c4b1cfb", 0)]
    [InlineData("ghost_race", "18983f711714041e291f6ae0167b19ae1114fa1961d635d6379f4a5da145951e", 2204)]
    [InlineData("knockout", "5d1b3d8c191886eef96c136770024ddf1e4e030fb00b016b4be3f17ae22962c1", 40306)]
    [InlineData("practice", "0dee217e76115f93edf9d277382a03076c50f48b84a8268f5595c3468a621ae6", 339)]
    [InlineData("time_trial", "501ee749f21e46dd3e72fb871dfcb07c3812ae13020e046b3bb3de7b4582ef92", 0)]
    public void AShippedModeKeepsItsHash(string name, string hash, long budget)
    {
        var result = OnSmallStack(File.ReadAllText(RepositoryFolder.Protocol("protocol", "quickplay", name + ".fzmode")));
        Assert.Empty(result.Errors);
        Assert.Equal(hash, result.Hash);
        Assert.Equal(budget, result.Budget);
    }

    [Fact]
    public void EveryShippedFileIsPinned()
    {
        var pinned = typeof(ModeLimitsTests).GetMethod(nameof(AShippedModeKeepsItsHash))!
            .GetCustomAttributes<InlineDataAttribute>().Select(a => (string)a.Data[0]!).Order(StringComparer.Ordinal);
        var shipped = Directory.GetFiles(RepositoryFolder.Protocol("protocol", "quickplay"), "*.fzmode")
            .Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal);
        Assert.Equal(shipped, pinned);
    }

    [Fact]
    public void AFileOfManyByteCharactersGetsThePackagesRefusal()
    {
        // the first check of a file's length counts characters: a file of many-byte characters still gets the package's
        var text = new StringBuilder(Head).Append("// ").Append('é', 40000).Append('\n').ToString();
        var error = Assert.Single(ModeCompiler.Compile(text).Errors);
        Assert.Contains("the package is", error, StringComparison.Ordinal);
    }
}
