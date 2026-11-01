using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The modern HUD's words: show_bar, show_figure, show_text, show_gap, hud_clear and the gap reads
/// (protocol/modes.toml; the runner's mode_gap.h).</summary>
public sealed class ModeHudWordsTests
{
    private const string Head = "mode \"T\" { api 1; }\nhud modern;\nglobal int round = 1;\n";

    private static string Rule(string body) => $"rule \"R\" {{ event race_start; do {{ for each seat {{ {body} }} }} }}\n";

    private static ModeCompilation Compile(string body, string head = Head) => ModeCompiler.Compile(head + Rule(body));

    private static string Error(string body, string head = Head) => Assert.Single(Compile(body, head).Errors);

    [Theory]
    [InlineData("show_bar seat, strip, seat.energy, seat.max_energy, under_speed;")]
    [InlineData("show_bar seat, wedge, 1, 2, under_times, GOLD;")]
    [InlineData("show_bar seat, cells, 3, 5, under_place, GREEN;")]
    [InlineData("show_figure seat, round, beside_place;")]
    [InlineData("show_figure seat, 4, under_top6, RED;")]
    [InlineData("show_text seat, \"ROUND {round}\", under_place;")]
    [InlineData("show_text seat, \"{round} OF {round + 1}\", message, ORANGE;")]
    [InlineData("show_text seat, \"Größe Πάνω 第二\", message;")]
    [InlineData("show_gap seat, ahead, under_times;")]
    [InlineData("show_gap seat, leader, message, WHITE;")]
    [InlineData("show_gap seat, you, top6;")]
    [InlineData("hud_clear seat, top6;")]
    public void TheWordsCompile(string body) => Assert.Empty(Compile(body).Errors);

    [Theory]
    [InlineData("show_figure seat, 1, under_place;")]
    [InlineData("show_text seat, \"A\", under_place;")]
    [InlineData("show_gap seat, ahead, under_times;")]
    [InlineData("hud_clear seat, under_place;")]
    public void TheyNeedTheModernHud(string body) =>
        Assert.Contains("hud modern", Error(body, "mode \"T\" { api 1; }\n"), StringComparison.Ordinal);

    [Fact]
    public void AGapReadNeedsNoHudButTheGamesKeepTheArrivals()
    {
        var mode = ModeCompiler.Compile("mode \"T\" { api 1; }\nrule \"R\" { event race_start; when seat.gap_ahead > 0; do { } }\n".Replace("event race_start", "event each seat", StringComparison.Ordinal));
        Assert.Empty(mode.Errors);
        Assert.Contains(ModePackage.Read(mode.Package!).Setups, s => s.Id == ModeSetup.TimeGaps);
        var plain = ModeCompiler.Compile("mode \"T\" { api 1; }\n" + Rule("seat.energy = 1;"));
        Assert.DoesNotContain(ModePackage.Read(plain.Package!).Setups, s => s.Id == ModeSetup.TimeGaps);
    }

    [Fact]
    public void AGapWordMakesTheArrivalsKeptAndARuleLine()
    {
        var mode = Compile("show_gap seat, you, top6;");
        Assert.Empty(mode.Errors);
        var lines = ModeRuleLines.Of(ModePackage.Read(mode.Package!));
        Assert.Contains("Time gaps to other racers", lines);
        Assert.Contains("Modern HUD for everyone", lines);
    }

    [Theory]
    [InlineData("show_gap seat, you, under_times;", "top6")]
    [InlineData("show_gap seat, ahead, top6;", "top6")]
    [InlineData("show_bar seat, strip, 1, 2, top6;", "top6")]
    [InlineData("show_figure seat, 1, top6;", "top6")]
    [InlineData("show_text seat, \"A\", top6;", "top6")]
    [InlineData("show_bar seat, strip, 1, 2, nowhere;", "under_place")]
    [InlineData("show_bar seat, strip, 1, 2, under_place, PINK;", "GOLD")]
    [InlineData("show_text seat, \"A very long text here\", beside_place;", "at most 8")]
    [InlineData("show_text seat, \"{1} {2} {3}\", message;", "at most 2 values")]
    [InlineData("show_text seat, \"open {round\", message;", "no }")]
    [InlineData("show_text seat, \"close }\", message;", "no {")]
    [InlineData("show_text seat, \"\", message;", "needs a text")]
    public void WhatIsRefused(string body, string says) => Assert.Contains(says, Error(body), StringComparison.Ordinal);

    [Theory]
    [InlineData("show_bar seat, strip, first(seats).energy, 100, under_speed;")]
    [InlineData("show_figure seat, floor(first(seats).energy), under_speed;")]
    [InlineData("show_text seat, \"{floor(first(seats).energy)}\", under_speed;")]
    public void NoOtherRacersEnergy(string body) => Assert.Contains("never another racer's", Error(body), StringComparison.Ordinal);

    [Fact]
    public void TextsTravelInThePackageOnceEach()
    {
        var mode = Compile("show_text seat, \"R {round}\", message; show_text seat, \"R {round}\", under_place; show_text seat, \"第二\", message;");
        Assert.Empty(mode.Errors);
        var word = Assert.Single(ModePackage.Read(mode.Package!).Setups, s => s.Id == ModeSetup.HudTexts);
        Assert.Equal(["R \u0001", "第二"], ModeHudTexts.Texts(word.Arguments));
        Assert.Contains("hud_texts", ModeDisassembler.List(mode.Package!), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileCannotWriteTheCompilersWords()
    {
        Assert.Contains("compiler's own", Assert.Single(ModeCompiler.Compile(Head + "time_gaps;\n").Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextsLimitIsHeld()
    {
        var eleven = string.Concat(Enumerable.Range(0, ModeWords.HudTexts + 1).Select(i => $"show_text seat, \"T{i}\", message; "));
        Assert.Contains("at most", Error(eleven), StringComparison.Ordinal);
    }
}
