using Online.Protocol;

namespace Protocol.Tests;

/// <summary>A launcher's release line (<see cref="VersionLine"/>): the first two numbers of its version, one spelling
/// each, none for a launcher built with no version or a pre-release; and the hello carries it only when there is one.</summary>
public sealed class VersionLineTests
{
    [Theory]
    [InlineData("0.5.0", "0.5")]
    [InlineData("0.5.2", "0.5")]
    [InlineData("0.5.2+3934d68", "0.5")]
    [InlineData("0.6.0", "0.6")]
    [InlineData("1.0.13", "1.0")]
    [InlineData("12.345.6789+x", "12.345")]
    [InlineData("0.0.0", null)]        // (what a program carries when it was built with no version)
    [InlineData("0.0.7", null)]
    [InlineData("0.6.0-dev", null)]    // a pre-release is in no release's lobbies
    [InlineData("0.6.0-rc.1+abc", null)]
    [InlineData("0.5", null)]
    [InlineData("0.5.1.0", null)]
    [InlineData("0.05.1", null)]
    [InlineData("v0.5.1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheLineOfAVersionIsItsFirstTwoNumbers(string? version, string? line) => Assert.Equal(line, VersionLine.Of(version));

    [Theory]
    [InlineData("0.5", true)]
    [InlineData("0.6", true)]
    [InlineData("1.0", true)]
    [InlineData("9999.9999", true)]
    [InlineData("0.0", false)]
    [InlineData("0.5.1", false)]
    [InlineData("0.05", false)]
    [InlineData("00.5", false)]
    [InlineData("10000.1", false)]
    [InlineData("0.5 ", false)]
    [InlineData(" 0.5", false)]
    [InlineData("0,5", false)]
    [InlineData("0.x", false)]
    [InlineData("0.", false)]
    [InlineData(".5", false)]
    [InlineData("٠.٥", false)]   // (digits of another script)
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ALineIsTwoNumbersWithOneSpelling(string? text, bool isLine) => Assert.Equal(isLine, VersionLine.IsLine(text));

    [Fact]
    public void AHelloWritesItsLineOnlyWhenItHasOneAndAHelloFromBeforeReadsAsNone()
    {
        var v = ControlProtocol.Version;
        Assert.Equal($$"""{"type":"hello","name":"ANA","version":{{v}},"build":"i/m/r","voteCounts":false,"line":"0.5"}""",
            ControlProtocol.Encode(new Hello("ANA", v, "i/m/r", Line: "0.5")));
        Assert.DoesNotContain("line", ControlProtocol.Encode(new Hello("ANA", v, "i/m/r")), StringComparison.Ordinal);
        Assert.Equal(new Hello("ANA", v, "i/m/r", Line: "0.5"),
            ControlProtocol.DecodeClient($$"""{"type":"hello","name":"ANA","version":{{v}},"build":"i/m/r","line":"0.5"}"""));
        Assert.Null(Assert.IsType<Hello>(ControlProtocol.DecodeClient($$"""{"type":"hello","name":"BEN","version":{{v}},"build":"i/m/r"}""")).Line);
    }

    [Fact]
    public void AnOtherBuildNamesItsLineOnlyWhenItHasOne()
    {
        Assert.Equal("""{"build":"i/m/r","players":2}""", System.Text.Json.JsonSerializer.Serialize(new OtherBuild("i/m/r", 2), ControlProtocol.Json));
        Assert.Equal("""{"build":"i/m/r","players":2,"line":"0.6"}""",
            System.Text.Json.JsonSerializer.Serialize(new OtherBuild("i/m/r", 2, "0.6"), ControlProtocol.Json));
    }
}
