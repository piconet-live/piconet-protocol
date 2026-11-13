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

    /// <summary><c>tag</c>'s settings (0.6.1, on the word itself): <c>size MIN MAX</c>, a player's name tag at its largest
    /// being 1, and <c>range D m</c>, each once in either order, left out for the defaults (0.5 to 1.2, 200 m). The compiler
    /// keeps them in its own word <c>tag_looks</c>, three values a text, written when one text differs from the defaults; a
    /// text with other settings is another text of <c>tag_texts</c>; <c>show_text</c> and <c>show_score</c> take a text whatever
    /// its settings.</summary>
    [Fact]
    public void TagTakesItsSizeAndRangeAfterItsText()
    {
        var result = Compile(Rule("tag seat, \"CROWN\", size 0.4 1.0, range 10000 m;"));
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal([ModeSetup.TagTexts, ModeSetup.TagLooks], result.Mode!.Setups.Select(s => s.Id));
        Assert.Equal([new ModeTagLookValues(10000.0f, 0.4f, 1.0f)], ModeTagLooks.Of(result.Mode!));
        Assert.Empty(ModeRuleLines.Of(result.Mode!));
        Assert.Contains("tag_looks [range 10000 m size 0.4 1]", ModeDisassembler.List(result.Package!));
        // the order of the settings is free; a whole number is a size too; a range may be written without the m
        var other = Compile(Rule("tag seat, \"CROWN\", range 10000, size 1 2;")).Mode!;
        Assert.Equal([new ModeTagLookValues(10000.0f, 1.0f, 2.0f)], ModeTagLooks.Of(other));
        // one setting, the other the default; no settings, the defaults and no word
        Assert.Equal([new ModeTagLookValues(200.0f, 0.4f, 1.0f)], ModeTagLooks.Of(Compile(Rule("tag seat, \"CROWN\", size 0.4 1;")).Mode!));
        Assert.Equal([new ModeTagLookValues(300.0f, 0.5f, 1.2f)], ModeTagLooks.Of(Compile(Rule("tag seat, \"CROWN\", range 300 m;")).Mode!));
        var plain = Compile(Rule("tag seat, \"CROWN\";")).Mode!;
        Assert.Equal([ModeSetup.TagTexts], plain.Setups.Select(s => s.Id));
        Assert.Equal([ModeTagLooks.Default], ModeTagLooks.Of(plain));
        // one text with two settings is two texts, and the settings written as the defaults are the one text; show_text
        // takes the text whatever its settings
        var two = Compile(Rule("tag seat, \"CROWN\", size 0.4 1; tag seat, \"CROWN\"; tag seat, \"CROWN\", size 0.5 1.2, range 200 m; show_text seat, \"CROWN\";")).Mode!;
        Assert.Equal(["CROWN", "CROWN"], ModeTagTexts.Texts(two.Setups.Single(s => s.Id == ModeSetup.TagTexts).Arguments));
        Assert.Equal([new ModeTagLookValues(200.0f, 0.4f, 1.0f), ModeTagLooks.Default], ModeTagLooks.Of(two));
        Assert.Equal("tag_looks [range 200 m size 0.4 1] [range 200 m size 0.5 1.2]",
            ModeDisassembler.List(Compile(Rule("tag seat, \"CROWN\", size 0.4 1; tag seat, \"CROWN\";")).Package!).Split('\n').Single(l => l.StartsWith("  tag_looks", StringComparison.Ordinal)).Trim());
        // the bounds: the drawing's own, and the least size under the largest
        static string Errors(ModeCompilation c) => string.Join(" ", c.Errors.Select(e => e.ToString()));
        Assert.Contains("tag's range is 1 to 10000 m: 0.5 m is not", Errors(Compile(Rule("tag seat, \"CROWN\", range 0.5 m;"))));
        Assert.Contains("tag's range is 1 to 10000 m: 20000 m is not", Errors(Compile(Rule("tag seat, \"CROWN\", range 20000 m;"))));
        Assert.Contains("tag's size is 0.1 to 5", Errors(Compile(Rule("tag seat, \"CROWN\", size 0 1;"))));
        Assert.Contains("tag's size is 0.1 to 5", Errors(Compile(Rule("tag seat, \"CROWN\", size 1 6;"))));
        Assert.Contains("tag's least size is above its largest: size 1.5 1", Errors(Compile(Rule("tag seat, \"CROWN\", size 1.5 1;"))));
        Assert.Contains("tag takes one size", Errors(Compile(Rule("tag seat, \"CROWN\", size 1 1, size 1 2;"))));
        Assert.Contains("tag takes one range", Errors(Compile(Rule("tag seat, \"CROWN\", range 1 m, range 2 m;"))));
        Assert.Contains("tag's size is its least and its largest", Errors(Compile(Rule("tag seat, \"CROWN\", size 1;"))));
        Assert.Contains("tag's range is the metres", Errors(Compile(Rule("tag seat, \"CROWN\", range 30s;"))));
        Assert.Contains("tag_looks is the compiler's own", Errors(Compile("tag_looks \"X\";\n")));
        Assert.Contains("tag's size and range need api 0.6.1", Errors(ModeCompiler.Compile("mode \"T\" { api 1; }\n" + Rule("tag seat, \"CROWN\", range 300 m;"))));
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
    public void ShowScoreTakesANumberAnOptionalLabelAndAnOptionalFont()
    {
        // SCORE (0.6.1): show_score's number, label and font, the label a tag text (0: the default), the font a `font` (0, BLOCK,
        // when left out); show and hide leave it to show_score
        var code = Code(Rule("show_score seat, seat.points;"));
        var at = code.IndexOf("Action show_score");
        Assert.Equal(["Push 0", "Push 0"], code[(at - 2)..at].Select(Word));
        code = Code(Rule("show_score seat, 7, \"KOS\";") + Rule("tag seat, \"KOS\";"));
        Assert.Equal(["Push 7", "Push 1", "Push 0"], code[(code.IndexOf("Action show_score") - 3)..code.IndexOf("Action show_score")].Select(Word));
        Assert.Equal("Push 1", Word(code[code.IndexOf("Action tag") - 1]));  // the same text, one entry of tag_texts
        code = Code(Rule("show_score seat, 7, \"KOS\", BIG_SERIF;"));
        Assert.Equal(["Push 7", "Push 1", "Push 5"], code[(code.IndexOf("Action show_score") - 3)..code.IndexOf("Action show_score")].Select(Word));
        Assert.Contains("Action show_score off", Code(Rule("show_score seat off;")));
        code = Code(Rule("hud_move seat, SCORE, 552, 86, 0;"));
        Assert.Equal("Push 24", Word(code[code.IndexOf("Action hud_move") - 7]));
        Assert.Contains("hud_move places it", Assert.Single(Compile(Rule("show seat, SCORE;")).Errors));
        Assert.Single(Compile(Rule("hide seat, SCORE;")).Errors);
        foreach (var bad in new[] { "show_score seat, 7, kos;", "show_score seat, 7, \"k,s\";", "show_score seat, 7, \"TOO LONG A LABEL\";", "show_score seat;",
                                    "show_score seat, 1, \"A\", 2;", "show_score seat, 1, \"A\", GOTHIC;", "show_score seat, 1, \"A\", BLOCK, 1;" })
        {
            Assert.Single(Compile(Rule(bad)).Errors);
        }
        Assert.Contains("one of BLOCK, CAPS, PLAIN, NARROW, SERIF, BIG_SERIF", Assert.Single(Compile(Rule("show_score seat, 1, \"A\", GOTHIC;")).Errors));
        var old = ModeCompiler.Compile("mode \"T\" { api 0.6.0; }\n" + Rule("show_score seat, 1;"));
        Assert.Contains(old.Errors, e => e.Contains("needs api 0.6.1", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowTextTakesATextAndAnOptionalFont()
    {
        // TEXT (0.6.1): show_text's text a tag text (the same table as tag's and show_score's), the font a `font` (0, BLOCK, when
        // left out); show and hide leave it to show_text
        var code = Code(Rule("show_text seat, \"GO\";") + Rule("tag seat, \"GO\";") + Rule("show_score seat, 1, \"KOS\", NARROW;"));
        Assert.Equal(["Push 1", "Push 0"], code[(code.IndexOf("Action show_text") - 2)..code.IndexOf("Action show_text")].Select(Word));
        Assert.Equal("Push 1", Word(code[code.IndexOf("Action tag") - 1]));  // the same text, one entry of tag_texts
        Assert.Equal(["Push 1", "Push 2", "Push 3"], code[(code.IndexOf("Action show_score") - 3)..code.IndexOf("Action show_score")].Select(Word));
        code = Code(Rule("show_text seat, \"GO\", SERIF;"));
        Assert.Equal(["Push 1", "Push 4"], code[(code.IndexOf("Action show_text") - 2)..code.IndexOf("Action show_text")].Select(Word));
        Assert.Contains("Action show_text off", Code(Rule("show_text seat off;")));
        code = Code(Rule("hud_move seat, TEXT, 320, 116, 0;"));
        Assert.Equal("Push 25", Word(code[code.IndexOf("Action hud_move") - 7]));
        Assert.Contains("hud_move places it", Assert.Single(Compile(Rule("show seat, TEXT;")).Errors));
        Assert.Single(Compile(Rule("hide seat, TEXT;")).Errors);
        foreach (var bad in new[] { "show_text seat, go;", "show_text seat, \"g,o\";", "show_text seat, \"TOO LONG A TEXT\";", "show_text seat;",
                                    "show_text seat, \"GO\", 2;", "show_text seat, \"GO\", GOTHIC;", "show_text seat, \"GO\", BLOCK, 1;", "show_text seat, 7;" })
        {
            Assert.Single(Compile(Rule(bad)).Errors);
        }
        Assert.Contains("show_text takes its text in quotes", Assert.Single(Compile(Rule("show_text seat, go;")).Errors));
        Assert.Contains("show_text: ", Assert.Single(Compile(Rule("show_text seat, \"g,o\";")).Errors));
        // the eight texts a mode may use are tag's, show_score's and show_text's together
        var texts = string.Concat(Enumerable.Range(0, 8).Select(i => Rule($"show_text seat, \"T{i}\";")));
        Assert.True(Compile(texts).Succeeded);
        Assert.Contains("at most 8 different texts", Assert.Single(Compile(texts + Rule("tag seat, \"ONE MORE\";")).Errors));
        var old = ModeCompiler.Compile("mode \"T\" { api 0.6.0; }\n" + Rule("show_text seat, \"GO\";"));
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
