using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The modern HUD's words, held out of api 1 (the owner, 2026-11-02; protocol/modes.toml's <c>held</c>): the
/// compiler refuses each and a package's reader refuses their ids, which stay theirs. The gap reads stay (the runner's
/// mode_gap.h).</summary>
public sealed class ModeHudWordsTests
{
    private const string Head = "mode \"T\" { api 1; }\n";

    private static string Rule(string body) => $"rule \"R\" {{ event race_start; do {{ for each seat {{ {body} }} }} }}\n";

    [Theory]
    [InlineData("hud modern;\n", "hud modern")]
    [InlineData("hud_texts \"A\";\n", "hud_texts")]
    [InlineData("show_bar seat, strip, seat.energy, seat.max_energy, under_speed;", "show_bar")]
    [InlineData("show_figure seat, 4, under_top6, RED;", "show_figure")]
    [InlineData("show_text seat, \"ROUND 1\", under_place;", "show_text")]
    [InlineData("show_gap seat, you, top6;", "show_gap")]
    [InlineData("hud_clear seat, top6;", "hud_clear")]
    public void TheWordsAreNotInThisVersion(string text, string word)
    {
        var source = text.EndsWith('\n') ? Head + text : Head + Rule(text);
        var error = Assert.Single(ModeCompiler.Compile(source).Errors);
        Assert.Contains($"{word} is not in this version of the mode language", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheirIdsStayTheirs()
    {
        Assert.Equal([ModeSetup.HudModern, ModeSetup.HudTexts], ModeWords.HeldSetups.Order());
        Assert.Equal([ModeAction.ShowBar, ModeAction.ShowFigure, ModeAction.ShowText, ModeAction.ShowGap, ModeAction.HudClear],
            ModeWords.HeldActions.Order());
        Assert.Equal(50, (int)ModeSetup.HudModern);
        Assert.Equal(73, (int)ModeAction.HudClear);
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
