using Online.Protocol;
using TestSupport;
using Xunit;

namespace Protocol.Tests;

/// <summary>The mode api's levels (protocol/modes.toml's [levels]; ModeLevels): a file names the game version it was
/// written for, which gives the rule set its package carries; a word or value of a later rule set is refused with the
/// version it needs; 0.6.0's <c>api 1</c> stays what it was.</summary>
public sealed class ModeLevelsTests
{
    private static string File(string api, string body = "") => $"mode \"Test\" {{\n  api {api};\n}}\n{body}";

    private static string Error(string source) => Assert.Single(ModeCompiler.Compile(source).Errors);

    [Fact]
    public void TheRuleSetsAreOneAReleaseThatChangedTheWords()
    {
        Assert.Equal([(1, "0.6.0"), (2, "0.6.1")], ModeWords.RuleSets);
        Assert.Equal((2, "0.6.1"), (ModeWords.Api, ModeWords.ApiVersion));
    }

    /// <summary>A file's version gives the newest rule set of that version or before; the bare 1 is 0.6.0's.</summary>
    [Theory]
    [InlineData("1", 1)]
    [InlineData("0.6.0", 1)]
    [InlineData("0.6.1", 2)]
    public void AFileNamesTheGameVersionItWasWrittenFor(string api, int ruleSet)
    {
        var result = ModeCompiler.Compile(File(api, "laps 3;\n"));
        Assert.Empty(result.Errors);
        Assert.Equal(ruleSet, result.Mode!.Api);
        Assert.Equal(ruleSet, ModePackage.Read(result.Package!).Api);
    }

    /// <summary>The same words under 0.6.0's two spellings are the same rule set; the package differs only by the
    /// source it carries.</summary>
    [Fact]
    public void ApiOneIsZeroSixZero()
    {
        var (bare, named) = (ModeCompiler.Compile(File("1", "laps 3;\n")), ModeCompiler.Compile(File("0.6.0", "laps 3;\n")));
        Assert.Equal(bare.Mode!.Api, named.Mode!.Api);
        Assert.Equal(bare.Mode!.Code, named.Mode!.Code);
    }

    [Theory]
    [InlineData("2", "line 2: api 2 is not a level: a mode file names the game version it was written for (api 0.6.1;), and api 1 is 0.6.0")]
    [InlineData("0", "line 2: api 0 is not a level: a mode file names the game version it was written for (api 0.6.1;), and api 1 is 0.6.0")]
    [InlineData("0.6.2", "line 2: api 0.6.2 is newer than this build, which knows up to api 0.6.1: update to use this file")]
    [InlineData("0.7.0", "line 2: api 0.7.0 is newer than this build, which knows up to api 0.6.1: update to use this file")]
    [InlineData("1.0.0", "line 2: api 1.0.0 is newer than this build, which knows up to api 0.6.1: update to use this file")]
    [InlineData("0.5.4", "line 2: api 0.5.4 is from before game modes: the first is api 0.6.0")]
    [InlineData("0.6", "line 2: the api is the game version the file was written for (api 0.6.1;), not '0.6'")]
    [InlineData("\"0.6.1\"", "line 2: the api is the game version the file was written for (api 0.6.1;), not the string \"0.6.1\"")]
    public void AnApiThisBuildDoesNotKnowIsRefusedInPlainWords(string api, string error) =>
        Assert.Equal(error, Error(File(api)));

    [Fact]
    public void AFileWithoutAnApiIsToldToNameOne() =>
        Assert.Equal("line 1: the header names no api: api 0.6.1;", Error("mode \"Test\" { }\n"));

    /// <summary>A value of a later rule set: written out, or within what a parameter may give.</summary>
    [Theory]
    [InlineData("1", "cpus always;\n", "line 4: cpus always needs api 0.6.1: this file says api 1 (0.6.0)")]
    [InlineData("0.6.0", "cpus always;\n", "line 4: cpus always needs api 0.6.1: this file says api 0.6.0")]
    [InlineData("1", "param c \"CPU racers\" choice [\"Off\", \"Vote\", \"Always\"] default \"Vote\";\ncpus c;\n",
        "line 5: cpus always needs api 0.6.1: this file says api 1 (0.6.0)")]
    public void AValueOfALaterRuleSetNamesTheVersionItNeeds(string api, string body, string error) =>
        Assert.Equal(error, Error(File(api, body)));

    [Theory]
    [InlineData("cpus on;\n")]
    [InlineData("cpus off;\n")]
    [InlineData("param c \"CPU racers\" switch default on;\ncpus c;\n")]
    public void TheFirstRuleSetsOwnValuesStay(string body) =>
        Assert.Empty(ModeCompiler.Compile(File("1", body)).Errors);

    /// <summary>A package that says rule set 1 and holds a value of rule set 2 is no compiler's: refused when read.</summary>
    [Fact]
    public void APackageHoldsNothingNewerThanItsRuleSet()
    {
        var package = ModeCompiler.Compile(File("0.6.1", "cpus always;\n")).Package!;
        Assert.Equal(2, ModePackage.Read(package).Api);
        var older = (byte[])package.Clone();
        older[7] = 1;
        Assert.Equal("the package is of rule set 1 and holds cpus always, which is of rule set 2 (api 0.6.1)",
            Assert.Throws<InvalidDataException>(() => ModePackage.Read(older)).Message);
    }

    /// <summary>Quick Play's own files: only the one that uses a word of 0.6.1 names it.</summary>
    [Theory]
    [InlineData("competitive", 2)]
    [InlineData("knockout", 1)]
    [InlineData("overdrive", 1)]
    [InlineData("death_race", 1)]
    [InlineData("ghost_race", 1)]
    public void TheOfficialFilesKeepTheirLevels(string name, int ruleSet)
    {
        var result = ModeCompiler.Compile(System.IO.File.ReadAllText(RepositoryFolder.Protocol("protocol", "quickplay", name + ".fzmode")));
        Assert.Empty(result.Errors);
        Assert.Equal(ruleSet, result.Mode!.Api);
    }

    [Fact]
    public void ALowerLauncherIsToldTheVersionItNeeds() =>
        Assert.Equal("This lobby's game mode needs Piconet 0.6.1. Update the launcher to race it.", ModeLevels.UpdateText(2));
}
