using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The modern HUD's words, retired with it (2026-11-04) and never in an api: the compiler knows none of them,
/// and their ids (setup words 50 and 51, actions 69 to 73) stay unused, so no other word ever takes one (protocol/modes.toml's
/// head). The gap reads stay (the runner's mode_gap.h).</summary>
public sealed class ModeGapWordsTests
{
    private const string Head = "mode \"T\" { api 1; }\n";

    private static string Rule(string body) => $"rule \"R\" {{ event race_start; do {{ for each seat {{ {body} }} }} }}\n";

    [Theory]
    [InlineData("hud modern;\n")]
    [InlineData("hud_texts \"A\";\n")]
    [InlineData("show_bar seat, strip, seat.energy, seat.max_energy, under_speed;")]
    [InlineData("show_figure seat, 4, under_top6, RED;")]
    [InlineData("show_gap seat, you, top6;")]
    [InlineData("hud_clear seat, top6;")]
    public void TheRetiredWordsAreNoWordsOfTheApi(string text)
    {
        var source = text.EndsWith('\n') ? Head + text : Head + Rule(text);
        Assert.Contains("of api 0.6.5", Assert.Single(ModeCompiler.Compile(source).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void TheModernHudsShowTextFormIsNotTheNewWords()
    {
        // show_text is a word again since 0.6.5 (the TEXT part, in a font of the game's): the modern HUD's form, with a place, is refused
        var error = Assert.Single(ModeCompiler.Compile(Head + Rule("show_text seat, \"ROUND 1\", under_place;")).Errors);
        Assert.Contains("show_text takes one of BLOCK", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRetiredIdsStayUnused()
    {
        Assert.DoesNotContain(ModeWords.Setups, s => (int)s.Id is 50 or 51);
        Assert.DoesNotContain(ModeWords.Actions, a => (int)a.Id is >= 69 and <= 73);
        Assert.Equal(52, (int)ModeSetup.TimeGaps);
        Assert.Equal(74, (int)ModeAction.FinishSeat);
    }

    [Fact]
    public void AGapReadNeedsNoHudButTheGamesKeepTheArrivals()
    {
        var mode = ModeCompiler.Compile(Head + "rule \"R\" { event each seat; when seat.gap_ahead > 0; do { } }\n");
        Assert.Empty(mode.Errors);
        Assert.Contains(ModePackage.Read(mode.Package!).Setups, s => s.Id == ModeSetup.TimeGaps);
        Assert.Contains("Time gaps to other racers", ModeRuleLines.Of(ModePackage.Read(mode.Package!)));
        var plain = ModeCompiler.Compile(Head + Rule("seat.energy = 1;"));
        Assert.DoesNotContain(ModePackage.Read(plain.Package!).Setups, s => s.Id == ModeSetup.TimeGaps);
    }

    [Fact]
    public void AFileCannotWriteTheCompilersWords()
    {
        Assert.Contains("compiler's own", Assert.Single(ModeCompiler.Compile(Head + "time_gaps;\n").Errors), StringComparison.Ordinal);
    }
}
