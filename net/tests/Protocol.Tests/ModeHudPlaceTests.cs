using System.Text.RegularExpressions;
using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The words that place the race HUD and mark the course map: <c>hud_layout ax</c>, <c>hud_move</c>,
/// <c>map_char</c>.</summary>
public sealed class ModeHudPlaceTests
{
    private const string Header = "mode \"T\" { api 0.6.1; }\n";

    private static ModeCompilation Compile(string body) => ModeCompiler.Compile(Header + body);

    private static List<string> Code(string body)
    {
        var result = Compile(body);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return ModeDisassembler.List(result.Package!).Split('\n').Select(l => Regex.Match(l, "^  [0-9a-f]{4}  (.*)$"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
    }

    private static string Word(string line) => line.Split(" (")[0];

    private static string Rule(string action) => $"rule \"R\" {{ event race_start; do {{ for each seat {{ {action} }} }} }}\n";

    [Fact]
    public void TheAxLayoutIsASetupWordWithItsLine()
    {
        var result = Compile("hud_layout ax;\n");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal([ModeSetup.HudLayoutAx], result.Mode!.Setups.Select(s => s.Id));
        Assert.Equal(["AX HUD layout"], ModeRuleLines.Of(result.Mode!));
        // beside hud clean: another word, not a form of it
        Assert.True(Compile("hud_layout ax;\nhud clean;\n").Succeeded);
    }

    [Fact]
    public void HudMoveTakesAPartAPlaceAndAnAngle()
    {
        var code = Code(Rule("hud_move seat, DISTANCE, 320, 60, 90;"));
        var at = code.IndexOf("Action hud_move");
        // the seat, DISTANCE (the part's place in its choice), the centre's x and y and the angle as reals
        Assert.Equal(["Push 1", "Push 320", "IToF", "Push 60", "IToF", "Push 90", "IToF"], code[(at - 7)..at].Select(Word));
        Assert.Single(Compile(Rule("hud_move seat, 1, 320, 60, 0;")).Errors);  // a number is no part
        Assert.Single(Compile(Rule("hud_move seat, RANK, 0, 0;")).Errors);     // the old form: dx, dy
        // SPARE (the spare-machine counter) is a part hud_move places, of 0.6.1; show and hide leave it to show_number
        code = Code(Rule("hud_move seat, SPARE, 320, 444, 0;"));
        Assert.Equal("Push 18", Word(code[code.IndexOf("Action hud_move") - 7]));
        Assert.Contains("Action hud_move off", Code(Rule("hud_move seat, SPARE off;")));
        Assert.Single(Compile(Rule("show seat, SPARE;")).Errors);
        Assert.Single(Compile(Rule("hide seat, SPARE;")).Errors);
        var old = ModeCompiler.Compile("mode \"T\" { api 0.6.0; }\n" + Rule("show_number seat, 2;"));
        Assert.True(old.Succeeded, string.Join("; ", old.Errors));
        old = ModeCompiler.Compile("mode \"T\" { api 0.6.0; }\n" + Rule("hud_move seat, SPARE, 320, 444, 0;"));
        Assert.Contains(old.Errors, e => e.Contains("needs api 0.6.1", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesOwnSignsArePartsForHudMoveAlone()
    {
        // SKULLS, WRONG_WAY, BANNERS, FINISH, CHECK (0.6.1): placed, never shown or hidden; WRONG_WAY is the banner's name too
        foreach (var (part, index) in new[] { ("SKULLS", 19), ("WRONG_WAY", 20), ("BANNERS", 21), ("FINISH", 22), ("CHECK", 23) })
        {
            var code = Code(Rule($"hud_move seat, {part}, 320, 240, 90;"));
            Assert.Equal($"Push {index}", Word(code[code.IndexOf("Action hud_move") - 7]));
            Assert.Contains("Action hud_move off", Code(Rule($"hud_move seat, {part} off;")));
            Assert.Contains("hud_move places it", Assert.Single(Compile(Rule($"show seat, {part};")).Errors));
            Assert.Single(Compile(Rule($"hide seat, {part};")).Errors);
            var old = ModeCompiler.Compile("mode \"T\" { api 0.6.0; }\n" + Rule($"hud_move seat, {part}, 320, 240, 0;"));
            Assert.Contains(old.Errors, e => e.Contains("needs api 0.6.1", StringComparison.Ordinal));
        }
        var banner = Code(Rule("stock_banner seat, WRONG_WAY;"));
        Assert.Equal("Push 4", Word(banner[banner.IndexOf("Action stock_banner") - 1]));
    }

    [Fact]
    public void ShowScoreTakesANumberAndAnOptionalLabel()
    {
        // SCORE (0.6.1): show_score's number and label, the label a tag text (0: the default); show and hide leave it to show_score
        var code = Code(Rule("show_score seat, seat.points;"));
        var at = code.IndexOf("Action show_score");
        Assert.Equal("Push 0", Word(code[at - 1]));
        code = Code(Rule("show_score seat, 7, \"KOS\";") + Rule("tag seat, \"KOS\";"));
        Assert.Equal(["Push 7", "Push 1"], code[(code.IndexOf("Action show_score") - 2)..code.IndexOf("Action show_score")].Select(Word));
        Assert.Equal("Push 1", Word(code[code.IndexOf("Action tag") - 1]));  // the same text, one entry of tag_texts
        Assert.Contains("Action show_score off", Code(Rule("show_score seat off;")));
        code = Code(Rule("hud_move seat, SCORE, 552, 86, 0;"));
        Assert.Equal("Push 24", Word(code[code.IndexOf("Action hud_move") - 7]));
        Assert.Contains("hud_move places it", Assert.Single(Compile(Rule("show seat, SCORE;")).Errors));
        Assert.Single(Compile(Rule("hide seat, SCORE;")).Errors);
        foreach (var bad in new[] { "show_score seat, 7, kos;", "show_score seat, 7, \"k,s\";", "show_score seat, 7, \"TOO LONG A LABEL\";", "show_score seat;", "show_score seat, 1, \"A\", 2;" })
        {
            Assert.Single(Compile(Rule(bad)).Errors);
        }
        var old = ModeCompiler.Compile("mode \"T\" { api 0.6.0; }\n" + Rule("show_score seat, 1;"));
        Assert.Contains(old.Errors, e => e.Contains("needs api 0.6.1", StringComparison.Ordinal));
    }

    [Fact]
    public void MapCharPushesTheCharacterOrTheShape()
    {
        var code = Code(Rule("map_char seat, \"K\";"));
        Assert.Equal("Push 75", Word(code[code.IndexOf("Action map_char") - 1]));
        code = Code(Rule("map_char seat, CROWN;"));
        Assert.Equal("Push 1114112", Word(code[code.IndexOf("Action map_char") - 1]));  // 0x110000: past Unicode
        code = Code(Rule("map_char seat, \"\u265B\";"));
        Assert.Equal("Push 9819", Word(code[code.IndexOf("Action map_char") - 1]));
        code = Code(Rule("map_char seat, \"\U0001F451\";"));  // one code point beyond the BMP: one character
        Assert.Equal("Push 128081", Word(code[code.IndexOf("Action map_char") - 1]));
        code = Code(Rule("map_char seat off;"));
        Assert.Contains("Action map_char off", code);
        foreach (var bad in new[] { "\"KK\"", "\"\"", "\" \"", "\"\t\"", "\"\u265B\u265B\"", "75", "STAR" })
        {
            Assert.Single(Compile(Rule($"map_char seat, {bad};")).Errors);
        }
    }
}
