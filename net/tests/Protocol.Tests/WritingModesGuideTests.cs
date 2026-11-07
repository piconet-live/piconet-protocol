using Online.Protocol;
using TestSupport;

namespace Protocol.Tests;

/// <summary>WRITING-MODES.md, the guide for players who write their own game modes, against the compiler: every
/// <c>```fzmode</c> block is a whole file that compiles with no error and no warning; every <c>```fzmode-warns</c> block
/// compiles, and its first warning is written in the guide; every <c>```fzmode-refused</c> block is refused, and its
/// first error is written in the guide; and every official mode the guide quotes is quoted as the file stands, so a
/// change to one fails here until the guide follows.</summary>
public sealed class WritingModesGuideTests
{
    private static readonly string Guide = File.ReadAllText(RepositoryFolder.Protocol("WRITING-MODES.md")).ReplaceLineEndings("\n");

    /// <summary>The guide's code blocks of one kind (the word after the opening fence, exactly), each with the line it
    /// starts on.</summary>
    private static List<(int Line, string Text)> Blocks(string kind)
    {
        var blocks = new List<(int, string)>();
        var lines = Guide.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }
            var opened = lines[i][3..].Trim();
            var start = i + 1;
            do
            {
                i++;
            }
            while (i < lines.Length && lines[i] != "```");
            Assert.True(i < lines.Length, $"WRITING-MODES.md line {start}: a code block is never closed");
            if (opened == kind)
            {
                blocks.Add((start, string.Join('\n', lines[start..i]) + "\n"));
            }
        }
        return blocks;
    }

    public static TheoryData<int> Files() => [.. Blocks("fzmode").Select(b => b.Line)];

    public static TheoryData<int> Warned() => [.. Blocks("fzmode-warns").Select(b => b.Line)];

    public static TheoryData<int> Refused() => [.. Blocks("fzmode-refused").Select(b => b.Line)];

    private static string Block(string kind, int line) => Blocks(kind).Single(b => b.Line == line).Text;

    [Theory]
    [MemberData(nameof(Files))]
    public void AnExampleCompilesWithNoWarning(int line)
    {
        var result = ModeCompiler.Compile(Block("fzmode", line));
        Assert.True(result.Succeeded, $"WRITING-MODES.md line {line}: {string.Join(" ", result.Errors)}");
        Assert.True(result.Warnings.Count == 0, $"WRITING-MODES.md line {line}: {string.Join(" ", result.Warnings)}");
    }

    [Theory]
    [MemberData(nameof(Warned))]
    public void AWarnedExampleCompilesWithTheWarningTheGuideGives(int line)
    {
        var result = ModeCompiler.Compile(Block("fzmode-warns", line));
        Assert.True(result.Succeeded, $"WRITING-MODES.md line {line}: {string.Join(" ", result.Errors)}");
        Assert.NotEmpty(result.Warnings);
        Assert.Contains($"`{result.Warnings[0]}`", Guide, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void ARefusedExampleIsRefusedWithTheMessageTheGuideGives(int line)
    {
        var result = ModeCompiler.Compile(Block("fzmode-refused", line));
        Assert.False(result.Succeeded, $"WRITING-MODES.md line {line} compiles");
        Assert.Contains($"`{result.Errors[0]}`", Guide, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("competitive.fzmode")]
    [InlineData("knockout.fzmode")]
    [InlineData("ghost_race.fzmode")]
    [InlineData("overdrive.fzmode")]
    [InlineData("death_race.fzmode")]
    public void AnOfficialModeIsQuotedAsItStands(string file)
    {
        var text = File.ReadAllText(RepositoryFolder.Protocol("protocol", "quickplay", file)).ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
        Assert.Contains(text, Blocks("fzmode").Select(b => b.Text));
    }

    [Fact]
    public void TheGuideHasExamplesOfEveryKind()
    {
        Assert.True(Blocks("fzmode").Count >= 20);
        Assert.NotEmpty(Blocks("fzmode-warns"));
        Assert.NotEmpty(Blocks("fzmode-refused"));
    }

    /// <summary>The standard library as the guide quotes it is the build's, comments aside.</summary>
    [Fact]
    public void TheLibraryIsQuotedAsTheBuildHasIt()
    {
        static string Code(string text) => string.Join('\n', text.ReplaceLineEndings("\n").Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && l.Trim().Length > 0));
        var library = Code(File.ReadAllText(RepositoryFolder.Protocol("net", "src", "Online.Protocol", "Modes", "std.fzmode")));
        Assert.Contains(library, Blocks("text").Select(b => Code(b.Text)));
    }
}
