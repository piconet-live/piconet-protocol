using System.Text.RegularExpressions;
using Online.Protocol;
using Xunit;

namespace Protocol.Tests;

/// <summary>The words that place the race HUD and mark the course map: <c>hud_layout ax</c>, <c>hud_move</c>,
/// <c>map_char</c>.</summary>
public sealed class ModeHudPlaceTests
{
    private const string Header = "mode \"T\" { api 1; }\n";

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
    public void HudMoveTakesAPartAndTwoReals()
    {
        var code = Code(Rule("hud_move seat, DISTANCE, 0, 60;"));
        var at = code.IndexOf("Action hud_move");
        // the seat, DISTANCE (the part's place in its choice), 0 and 60 as reals
        Assert.Equal(["Push 1", "Push 0", "IToF", "Push 60", "IToF"], code[(at - 5)..at].Select(Word));
        Assert.Single(Compile(Rule("hud_move seat, 1, 0, 60;")).Errors);  // a number is no part
        Assert.Single(Compile(Rule("hud_move seat, RANK, 0;")).Errors);
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
