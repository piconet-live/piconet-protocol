using Online.Protocol;

namespace Protocol.Tests;

/// <summary>The stat pack file (<see cref="StatPackText"/>; handbook/stat-packs.md): a pack written as text
/// and read back to the same values exactly, and every text that is no pack, refused with its line and the rule.</summary>
public sealed class StatPackTextTests
{
    private const string Text = """
        fzstats 1
        name GX Made Up 1.0
        # Blue Falcon
        machine 6 Weight 2520
        machine 6 Bytes48 3 1
        # Rainbow Phoenix
        machine 40 Hover0Z -1.5
        body 0 Weight 500
        booster 24 BoostStrength 14

        """;

    private static string? Refusal(string text) => StatPackText.Read(text).Refusal;

    private static string Lines(params string[] lines) => "fzstats 1\nname p\n" + string.Join('\n', lines) + "\n";

    [Fact]
    public void APackIsWrittenAsTextAPersonCanReadAndReadBackToTheSameValues()
    {
        var pack = StatPack.Of("GX Made Up 1.0", StatPackTests.Made).Pack!;

        Assert.Equal(Text.ReplaceLineEndings("\n"), StatPackText.Write(pack));
        var (back, refusal) = StatPackText.Read(Text);
        Assert.Null(refusal);
        Assert.Equal((pack.Name, StatPackTests.Fingerprint), (back!.Name, back.Fingerprint));
        Assert.Equal(pack.Entries, back.Entries);
    }

    /// <summary>A pack file may say who made the pack: an <c>author</c> line after the name. It is shown beside the
    /// name and is no part of the pack's values: the fingerprint, and what travels to the game, are the same with
    /// and without it, and a file without the line reads and writes as it always did.</summary>
    [Fact]
    public void AnAuthorLineNamesWhoMadeThePackAndChangesNothingOfItsValues()
    {
        var with = Text.ReplaceLineEndings("\n").Replace("name GX Made Up 1.0\n", "name GX Made Up 1.0\nauthor A Maker\n", StringComparison.Ordinal);

        var (pack, refusal) = StatPackText.Read(with);

        Assert.Null(refusal);
        Assert.Equal(("GX Made Up 1.0", "A Maker", StatPackTests.Fingerprint), (pack!.Name, pack.Author, pack.Fingerprint));
        Assert.Equal(with, StatPackText.Write(pack));
        var plain = StatPackText.Read(Text).Pack!;
        Assert.Null(plain.Author);
        Assert.Equal(plain.ToWire(), pack.ToWire());                                   // the game is handed the same text
        Assert.Null(StatPack.FromWire(pack.ToWire()).Pack!.Author);                    // which names no author
        Assert.DoesNotContain("author", StatPackText.Write(plain), StringComparison.Ordinal);
        // comments above the first line, as a pack kept in a repository has them
        Assert.Equal("A Maker", StatPackText.Read("# by A Maker, with permission\n# made by the import\n" + with).Pack!.Author);
        foreach (var line in new[] { "author", "author ", "author  two spaces", "author " + new string('n', 41), "author pé" })
        {
            Assert.Equal("Line 3: \"author\" is followed by 1 to 40 characters of plain ASCII.",
                Refusal(with.Replace("author A Maker", line, StringComparison.Ordinal)));
        }
        // once, and before the values
        Assert.Equal("Line 4: \"author\" is not machine, body, cockpit or booster.",
            Refusal(with.Replace("author A Maker\n", "author A Maker\nauthor Another\n", StringComparison.Ordinal)));
        Assert.StartsWith("A pack's author is 1 to 40 characters", StatPack.Of("p", StatPackTests.Made, " x").Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReaderTakesAnyOrderOtherLineEndsCommentsAndSpaces()
    {
        var other = "# a pack somebody wrote by hand\r\n\r\nfzstats 1\r\n\tname GX Made Up 1.0  \r\n"
            + "booster 24   BoostStrength\t14.0\r\n# the rest\r\nbody 0 Weight 5e2\r\n  machine 40 Hover0Z -1.50\r\n"
            + "machine 6 Bytes48 3 1\r\nmachine 6 Weight 2520";

        var (pack, refusal) = StatPackText.Read(other);

        Assert.Null(refusal);
        Assert.Equal(StatPackTests.Fingerprint, pack!.Fingerprint);
    }

    /// <summary>A decimal number is the nearest 32-bit number, and the writer's text is the shortest that reads back
    /// to the same bits: 20,000 values of every size, none changed by a trip through the file.</summary>
    [Fact]
    public void ANumberGoesThroughTheFileWithoutLosingABit()
    {
        Assert.Equal(0x3DCCCCCDu, StatPackText.Read(Lines("machine 0 Grip1 0.1")).Pack!.Entries[0].Bits);
        Assert.Equal("Line 3: machine 0 CameraReorienting is the bits 80000000, which is no number a pack may hold (a denormal, -0, an infinity or none at all).",
            Refusal(Lines("machine 0 CameraReorienting -0")));
        var random = new Random(20261011);
        var entries = new List<StatEntry>();
        for (var i = 0; i < 20000; i++)
        {
            // a wall point's X takes every normal number up to 25 either way: bit patterns at random, kept if in range
            var bits = (uint)random.NextInt64(0, 1L << 32);
            if (StatPackRules.Problem(i % 41, 0x84, bits) is null)
            {
                entries.Add(new StatEntry(i % 41, 0x84, bits));
            }
        }
        Assert.InRange(entries.Count, 8000, 20000);
        foreach (var chunk in entries.Chunk(41))
        {
            var pack = StatPack.Of("p", chunk.DistinctBy(e => e.Block)).Pack!;
            Assert.Equal(pack.Entries, StatPackText.Read(StatPackText.Write(pack)).Pack!.Entries);
        }
    }

    [Fact]
    public void ATextThatIsNoPackIsRefusedWithItsLineAndTheRule()
    {
        Assert.Equal("Line 1: a stat pack file begins with \"fzstats 1\".", Refusal("fzstats 2\nname p\nmachine 6 Weight 9\n"));
        Assert.Equal("Line 2: a stat pack file begins with \"fzstats 1\".", Refusal("# x\nmachine 6 Weight 9\n"));
        Assert.Equal("The file has no \"fzstats 1\" line and \"name\" line at its top.", Refusal("fzstats 1\n"));
        Assert.Equal("The file has no \"fzstats 1\" line and \"name\" line at its top.", Refusal(""));
        Assert.Equal("The pack holds no value.", Refusal("fzstats 1\nname p\n# nothing\n"));
        foreach (var name in new[] { "machine 6 Weight 9", "name", "name " + new string('n', 41), "name pé", "Name p" })
        {
            Assert.StartsWith("Line 2: the second line is \"name\" and the pack's name", Refusal($"fzstats 1\n{name}\nmachine 6 Weight 9\n"), StringComparison.Ordinal);
        }
        Assert.Equal("Line 3: \"car\" is not machine, body, cockpit or booster.", Refusal(Lines("car 6 Weight 9")));
        Assert.Equal("Line 3: \"Machine\" is not machine, body, cockpit or booster.", Refusal(Lines("Machine 6 Weight 9")));
        Assert.Equal("Line 4: a machine's number is 0 to 40.", Refusal(Lines("machine 40 Weight 9", "machine 41 Weight 9")));
        Assert.Equal("Line 3: a booster's number is 0 to 24.", Refusal(Lines("booster 25 Weight 9")));
        foreach (var number in new[] { "-1", "6.0", "0x6", "six", "+6", "0006" })
        {
            Assert.Equal("Line 3: a machine's number is 0 to 40.", Refusal(Lines($"machine {number} Weight 9")));
        }
        Assert.Equal("Line 3: a machine's number is 0 to 40.", Refusal(Lines("machine")));
        Assert.Equal("Line 3: \"Speed\" is no stat of a pack.", Refusal(Lines("machine 6 Speed 9")));
        Assert.Equal("Line 3: \"weight\" is no stat of a pack.", Refusal(Lines("machine 6 weight 9")));
        Assert.Equal("Line 3: \"\" is no stat of a pack.", Refusal(Lines("machine 6")));
        Assert.Equal("Line 3: \"Hover4X\" is no stat of a pack.", Refusal(Lines("machine 6 Hover4X 1")));
        Assert.Equal("Line 3: a pack changes no name.", Refusal(Lines("machine 6 Name Blue Falcon 2")));
        foreach (var value in new[] { "", "NaN", "Infinity", "+9", "9,5", "0x10", "9 9", "1e", ".5", "9.", "nine", "9kg" })
        {
            Assert.Equal("Line 3: Weight's value is one decimal number.", Refusal(Lines($"machine 6 Weight {value}")));
        }
        foreach (var value in new[] { "", "3", "3 1 0", "256 1", "3 -1", "3.0 1", "0x3 1" })
        {
            Assert.Equal("Line 3: Bytes48 is two bytes: two whole numbers from 0 to 255.", Refusal(Lines($"machine 6 Bytes48 {value}")));
        }
        Assert.Equal("Line 5: machine 6 Weight is there twice.", Refusal(Lines("machine 6 Weight 9", "body 0 Weight 9", "machine 6 Weight 9")));
        Assert.Equal("Line 4: machine 6 Weight is 0, and must be at least 1.", Refusal(Lines("machine 5 Weight 9", "machine 6 Weight 0")));
        Assert.Equal("Line 3: machine 6 Weight is the bits 7F800000, which is no number a pack may hold (a denormal, -0, an infinity or none at all).",
            Refusal(Lines("machine 6 Weight 1e99")));
        Assert.StartsWith("Line 3: machine 6 Weight is the bits 00000001,", Refusal(Lines("machine 6 Weight 1e-45")), StringComparison.Ordinal);
        Assert.Equal("Line 3: machine 6 Bytes48 is 03040000: the second byte is at most 3 and the last two are 0.", Refusal(Lines("machine 6 Bytes48 3 4")));
        Assert.Equal("Line 3: booster 0 BoostStrength is -221, and a part's value is at most 220 from 0.", Refusal(Lines("booster 0 BoostStrength -221")));
        Assert.Null(Refusal(Lines("booster 0 BoostStrength 0", "body 0 Grip2 -0.05")));   // a part: any sign, no floor
        Assert.Equal("The file is longer than any stat pack.", Refusal(Lines("machine 6 Weight 9") + new string('\n', 1 << 20)));
    }
}
