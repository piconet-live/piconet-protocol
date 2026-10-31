using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The language's section 10 (a mode's values on the status board) and a solo mode's words: what the compiler
/// keeps, what it refuses, and that a mode with neither keeps the bytes it had.</summary>
public sealed class ModeBoardTests
{
    private const string Head = "mode \"B\" { api 1; }\n";

    private static ModeCompilation Compile(string body) => ModeCompiler.Compile(Head + body);

    [Fact]
    public void BoardLinesAreKeptInTheFilesOrderAndReadBack()
    {
        var result = Compile("global int a;\nseat real b;\nseat int c;\nteam int d;\nboard seat c \"C\";\nboard global a \"A\";\nboard seat b \"B\";\nboard team d \"D\";\n");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        var expected = new[]
        {
            new ModeBoardLine(ModeScope.Seat, ModeType.Int, 1, "C"),
            new ModeBoardLine(ModeScope.Global, ModeType.Int, 0, "A"),
            new ModeBoardLine(ModeScope.Seat, ModeType.Real, 0, "B"),
            new ModeBoardLine(ModeScope.Team, ModeType.Int, 0, "D"),
        };
        Assert.Equal(expected, result.Mode!.Board);
        Assert.Equal(expected, ModePackage.Read(result.Package!).Board);
    }

    [Fact]
    public void ALinesWordsAreKeptAndReadBack()
    {
        var result = Compile("seat int s;\nglobal bool b;\nboard seat s \"S\" [\"Racing\", \"Out\"];\nboard global b \"B\";\n");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal("Racing|Out", string.Join("|", result.Mode!.Board[0].Words));
        Assert.Empty(result.Mode.Board[1].Words);
        Assert.Equal(result.Mode.Board, ModePackage.Read(result.Package!).Board);
    }

    [Fact]
    public void AnEmptyWordShowsNothingAndIsKept()
    {
        var result = Compile("seat int infected;\nboard seat infected \"Status\" [\"\", \"Infected\"];\n");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal("|Infected", string.Join("|", ModePackage.Read(result.Package!).Board[0].Words));
    }

    [Fact]
    public void TheLongestListAndWordFit()
    {
        var words = string.Join(", ", Enumerable.Range(0, ModeWords.BoardWords).Select(_ => "\"" + new string('x', ModeWords.BoardWord) + "\""));
        Assert.True(Compile($"global int a;\nboard global a \"A\" [{words}];\n").Succeeded);
    }

    [Theory]
    [InlineData("global real a;\nboard global a \"A\" [\"x\"];\n", 3, "a real is no whole number")]
    [InlineData("global vec a;\nboard global a \"A\" [\"x\"];\n", 2, "a vec is a value of the step")]
    [InlineData("global int a;\nboard global a \"A\" [];\n", 3, "")]
    [InlineData("global int a;\nboard global a \"A\" [\"x\", \"12345678901234567\"];\n", 3, "at most 16 bytes")]
    [InlineData("global int a;\nboard global a \"A\" [\"é\", \"éééééééééééééééééé\"];\n", 3, "at most 16 bytes")]
    [InlineData("global int a;\nboard global a \"A\" [\"0\",\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\",\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\"];\n", 3, "at most 16 words")]
    public void ABadWordListIsRefusedByLine(string body, int line, string expected)
    {
        var error = Assert.Single(Compile(body).Errors);
        Assert.StartsWith($"line {line}", error);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void AModeWithNoBoardHasNoSection()
    {
        var package = Compile("global int a;\n").Package!;
        Assert.Equal(-1, System.Text.Encoding.ASCII.GetString(package).IndexOf("BORD", StringComparison.Ordinal));
        Assert.Empty(ModePackage.Read(package).Board);
    }

    [Theory]
    [InlineData("board global a \"A\";\nglobal int a;\n", "declared before")]
    [InlineData("seat int a;\nboard global a \"A\";\n", "no global variable")]
    [InlineData("global int a;\nboard seat a \"A\";\n", "no seat variable")]
    [InlineData("global int a;\nboard team a \"A\";\n", "no team variable")]
    [InlineData("global vec a;\nboard global a \"A\";\n", "a vec")]
    [InlineData("global int a;\nboard global a \"A\";\nboard global a \"B\";\n", "already")]
    [InlineData("global int a;\nboard global a \"\";\n", "label")]
    [InlineData("global int a;\nboard global a \"123456789012345678901234567890123\";\n", "label")]
    [InlineData("global int a;\nboard everyone a \"A\";\n", "global, seat or team")]
    public void ABadBoardLineIsRefused(string body, string says)
    {
        var result = Compile(body);
        Assert.False(result.Succeeded);
        Assert.Contains(says, result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void NineLinesAScopeAreTooMany()
    {
        var names = Enumerable.Range(0, 9).Select(i => $"v{(char)('a' + i)}").ToList();
        var text = string.Concat(names.Select(n => $"global int {n};\n")) + string.Concat(names.Select(n => $"board global {n} \"{n}\";\n"));
        Assert.Contains("at most 8", Compile(text).Errors[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rewind 1min;\n", "says solo")]
    [InlineData("pause on;\n", "says solo")]
    [InlineData("resume_countdown 1s;\n", "says solo")]
    [InlineData("solo on;\nsave_state on;\n", "says rewind")]
    [InlineData("solo on;\ncpus on;\n", "one seat")]
    [InlineData("solo on;\nracers 1..2;\n", "one seat")]
    [InlineData("solo on;\nteams 2..2;\n", "one seat")]
    [InlineData("solo on;\nrewind 2min;\n", "rewind")]
    public void ASoloWordOutOfPlaceIsRefused(string body, string says)
    {
        var result = Compile(body);
        Assert.False(result.Succeeded);
        Assert.Contains(says, result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheOfficialFilesCompile()
    {
        var folder = Path.Combine(ProtocolFolder(), "protocol", "quickplay");
        var knockout = ModeCompiler.Compile(File.ReadAllText(Path.Combine(folder, "knockout.fzmode")));
        Assert.Equal(["Out", "Next cut"], knockout.Mode!.Board.Select(l => l.Label));
        var practice = ModeCompiler.Compile(File.ReadAllText(Path.Combine(folder, "practice.fzmode")));
        Assert.True(practice.Succeeded, string.Join(" ", practice.Errors));
        Assert.True(ModeTerms.Of(practice.Mode!).Solo);
        Assert.Equal(0, ModeTerms.Of(practice.Mode!).Laps(1));
        Assert.Equal(["restore_on", "lap_count"], practice.Mode!.Params.Select(p => p.Name));
        Assert.Equal(5, ModeTerms.Of(practice.Mode!, [0, 5]).Laps(1));
        var trial = ModeCompiler.Compile(File.ReadAllText(Path.Combine(folder, "time_trial.fzmode")));
        Assert.True(trial.Succeeded, string.Join(" ", trial.Errors));
        Assert.True(ModeTerms.Of(trial.Mode!).Solo);
        Assert.Equal(3, ModeTerms.Of(trial.Mode!).Laps(1));
        Assert.Equal(7, ModeTerms.Of(trial.Mode!, [7]).Laps(1));
        var ghosts = ModeCompiler.Compile(File.ReadAllText(Path.Combine(folder, "ghost_race.fzmode")));
        Assert.True(ghosts.Succeeded, string.Join(" ", ghosts.Errors));
        Assert.False(ModeTerms.Of(ghosts.Mode!).Solo);
        Assert.Contains(ghosts.Mode!.Setups, s => s.Id == ModeSetup.StartTogether);
        Assert.Equal(3, ModeTerms.Of(ghosts.Mode!).Laps(1));
        Assert.Equal(7, ModeTerms.Of(ghosts.Mode!, [7]).Laps(1));
    }

    [Fact]
    public void ASquareRootCountsItsCost()
    {
        var plain = Compile("global real a;\nrule \"R\" { event every step; do { a = abs(a); } }\n");
        var root = Compile("global real a;\nrule \"R\" { event every step; do { a = sqrt(a); } }\n");
        Assert.Equal(plain.Budget + ModeWords.FsqrtCost - 1, root.Budget);
        Assert.Equal(8, ModeWords.FsqrtCost);
    }

    private static string ProtocolFolder()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "protocol", "quickplay")))
        {
            at = at.Parent;
        }
        return at?.FullName ?? throw new DirectoryNotFoundException("the protocol folder");
    }
}
