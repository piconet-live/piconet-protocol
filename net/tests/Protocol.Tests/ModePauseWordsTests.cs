using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>A mode's pause (0.6.5): pause, resume, resume after, say and say off; race.paused, race.paused_steps and the
/// step row's seat.pad.*. The compiler's own words: pauses (a mode that writes pause), say_texts (the texts, UTF-8).</summary>
public sealed class ModePauseWordsTests
{
    private const string Head = "mode \"T\" { api 0.6.5; }\n";

    private static string Rule(string body) => $"rule \"R\" {{ event race_start; do {{ {body} }} }}\n";

    private static ModePackage Package(string source)
    {
        var result = ModeCompiler.Compile(source);
        Assert.Empty(result.Errors);
        return ModePackage.Read(result.Package!);
    }

    [Fact]
    public void AModeThatPausesCarriesTheWordPauses()
    {
        Assert.Contains(Package(Head + Rule("pause;")).Setups, s => s.Id == ModeSetup.Pauses && s.Arguments.Count == 0);
        // resume alone, or a message, is no pause
        Assert.DoesNotContain(Package(Head + Rule("resume; say \"Hi.\";")).Setups, s => s.Id == ModeSetup.Pauses);
    }

    [Fact]
    public void ResumeAfterIsOneWordWithItsTime()
    {
        var source = Head + Rule("pause; resume after 1s; resume after 0; resume;");
        var code = ModeDisassembler.List(ModeCompiler.Compile(source).Package!);
        Assert.Contains("Action resume after", code, StringComparison.Ordinal);
        Assert.Contains("Push 60", code, StringComparison.Ordinal);
        Assert.Contains("resume after takes 1 argument", Assert.Single(ModeCompiler.Compile(Head + Rule("resume after;")).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void SaysTextsAreKeptOnceEachInUtf8()
    {
        var mode = Package(Head + Rule("say \"Hold A to accelerate.\"; say \"Café, 速い!\"; say \"Hold A to accelerate.\"; say off;"));
        // one word a text, in order, its UTF-8 bytes a value each
        Assert.Equal(["Hold A to accelerate.", "Café, 速い!"], ModeSayTexts.Texts(mode.Setups));
        Assert.Equal(2, mode.Setups.Count(s => s.Id == ModeSetup.SayTexts));
        Assert.All(mode.Setups.Where(s => s.Id == ModeSetup.SayTexts).SelectMany(s => s.Arguments), a => Assert.InRange(a.Value, 1, 255));
        var code = ModeDisassembler.List(mode.Write());
        Assert.Contains("say_texts \"Hold A to accelerate.\"", code, StringComparison.Ordinal);
        Assert.Contains("say_texts \"Café, 速い!\"", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("say x;", "in quotes")]
    [InlineData("say \" Hi\";", "no space at either end")]
    [InlineData("say \"\";", "needs a text")]
    public void ASayTextIsRefusedWhenItCannotBeShown(string body, string why)
    {
        Assert.Contains(why, Assert.Single(ModeCompiler.Compile(Head + "global int x;\n" + Rule(body)).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ASayTextHasItsLimits()
    {
        var longest = new string('a', ModeWords.SayText);
        Assert.Empty(ModeCompiler.Compile(Head + Rule($"say \"{longest}\";")).Errors);
        Assert.Contains("at most 160 bytes", Assert.Single(ModeCompiler.Compile(Head + Rule($"say \"{longest}b\";")).Errors), StringComparison.Ordinal);
        var many = string.Concat(Enumerable.Range(0, ModeWords.SayTexts + 1).Select(i => $"say \"Text {i}.\"; "));
        Assert.Contains("at most 32 different texts", Assert.Single(ModeCompiler.Compile(Head + Rule(many)).Errors), StringComparison.Ordinal);
        // the most a mode holds: 32 texts of 160 bytes each, a word each, read back as written
        var full = Enumerable.Range(0, ModeWords.SayTexts).Select(i => $"{i:D2}" + new string('é', (ModeWords.SayText - 2) / 2)).ToArray();
        var mode = Package(Head + Rule(string.Concat(full.Select(t => $"say \"{t}\"; "))));
        Assert.Equal(full, ModeSayTexts.Texts(mode.Setups));
    }

    [Fact]
    public void APackagesSayTextsAreTheCompilersOrItIsRefused()
    {
        var mode = Package(Head + Rule("say \"One.\"; say \"Two.\";"));
        byte[] With(ModeSetupWord more) => new ModePackage
        {
            Api = mode.Api, Name = mode.Name, Params = mode.Params, Setups = [.. mode.Setups, more], Globals = mode.Globals,
            SeatWords = mode.SeatWords, TeamWords = mode.TeamWords, Rules = mode.Rules, Board = mode.Board, Code = mode.Code,
            Source = mode.Source,
        }.Write();
        Assert.Equal(["One.", "Two.", "Three."], ModeSayTexts.Texts(ModePackage.Read(With(ModeSayTexts.Words(["Three."])[0])).Setups));
        Assert.Throws<InvalidDataException>(() => ModePackage.Read(With(ModeSayTexts.Words(["One."])[0])));    // twice
        Assert.Throws<InvalidDataException>(() => ModePackage.Read(With(new ModeSetupWord(ModeSetup.SayTexts, [new(false, 0x41), new(false, 10)]))));
        Assert.Throws<InvalidDataException>(() => ModePackage.Read(With(new ModeSetupWord(ModeSetup.SayTexts, [new(false, 0xC3)]))));
    }

    [Theory]
    [InlineData("pauses;\n")]
    [InlineData("say_texts \"A\";\n")]
    public void AFileCannotWriteTheCompilersWords(string text)
    {
        Assert.Contains("compiler's own", Assert.Single(ModeCompiler.Compile(Head + text).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePadAndThePauseAreRead()
    {
        var code = ModeDisassembler.List(ModeCompiler.Compile(Head +
            "rule \"R\" { event each seat; while race.paused and race.paused_steps > 7s and seat.pad.steer < -0.5 and seat.pad.accel > 0.5 " +
            "and seat.pad.brake < 0.1 and seat.pad.boost; do { resume; } }\n").Package!);
        foreach (var read in new[] { "RaceRead paused", "RaceRead paused_steps", "SeatRead pad.steer", "SeatRead pad.accel", "SeatRead pad.brake", "SeatRead pad.boost" })
        {
            Assert.Contains(read, code, StringComparison.Ordinal);
        }
        Assert.Contains("a seat has no 'pad'", Assert.Single(ModeCompiler.Compile(Head + "rule \"R\" { event each seat; when seat.pad.start; do { } }\n").Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void TheIdsAreTheirs()
    {
        // (the other branches of 0.6.5 hold setup 64 to 69, actions 80 to 83 and seat reads 94 and 95)
        Assert.Equal((70, 71), ((int)ModeSetup.SayTexts, (int)ModeSetup.Pauses));
        Assert.Equal((84, 85, 86, 87, 88), ((int)ModeAction.Pause, (int)ModeAction.Resume, (int)ModeAction.ResumeAfter, (int)ModeAction.Say, (int)ModeAction.SayOff));
        Assert.Equal((96, 97, 98, 99), ((int)ModeSeatRead.PadSteer, (int)ModeSeatRead.PadAccel, (int)ModeSeatRead.PadBrake, (int)ModeSeatRead.PadBoost));
        Assert.Equal((14, 15), ((int)ModeRaceRead.Paused, (int)ModeRaceRead.PausedSteps));
    }
}
