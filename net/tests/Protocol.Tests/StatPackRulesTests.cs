using System.Globalization;
using Online.Protocol;

namespace Protocol.Tests;

/// <summary>
/// The ranges of a stat pack (<see cref="StatPackRules"/>). The list is described once (protocol/stats.toml) and
/// generated three times: here, in the game (<c>port/core/src/stat_ranges.h</c>) and on the handbook's page
/// (handbook/stat-packs.md). Each program's test holds its own list to the page's table, row for row, so the two
/// programs check a pack by the same numbers.
/// What each program says of a value is held to one list of cases both tests read.
/// </summary>
public sealed class StatPackRulesTests
{
    private static string Row(StatRule r)
    {
        // (a part's block: the row's size alone, either side of 0)
        var (low, high) = (StatPackRules.Text(r.Low), StatPackRules.Text(r.High));
        var tried = r.TriedTo is { } most ? $"0 to {StatPackRules.Text(most)}" : "the whole range";
        var z = r.ZLeast != 0 ? $"; a machine's Z at least {StatPackRules.Text(r.ZLeast)} from 0" : "";
        return Columns() + $" {tried} |";

        string Columns() => r.Kind switch
        {
            StatWordKind.Bytes => $"| 0x{r.At:X2} | {r.Name} | two bytes, then two that are 0 | first: 0 to 255 | second: 0 to {high} | as a machine's |",
            StatWordKind.Points => $"| 0x{r.At:X2} | {r.Name}0X to {r.Name}3Z | four points of X, Y, Z{z} | at least {low} | at most {high} | as a machine's{(r.ZLeast != 0 ? ", any Z" : "")} |",
            _ => $"| 0x{r.At:X2} | {r.Name} | a number | at least {low} | {(r.Below ? "below" : "at most")} {high} | {(r.Below ? "less than" : "to")} {high} either side of 0 |",
        };
    }

    /// <summary>A file of the repository (the tests' source folder's, whatever the build output).</summary>
    private static string Repository(params string[] path) => TestSupport.RepositoryFolder.Of(path);

    private static string[] HandbookRows() =>
        [.. File.ReadLines(Repository("handbook", "stat-packs.md"))
            .SkipWhile(line => !line.StartsWith("| At | Name in a pack |", StringComparison.Ordinal))
            .TakeWhile(line => line.StartsWith('|'))
            .Where(line => line.StartsWith("| 0x", StringComparison.Ordinal))];

    [Fact]
    public void TheRangesAreTheHandbooksRowForRow()
    {
        Assert.Equal(HandbookRows(), StatPackRules.All.Select(Row));
        Assert.Equal(("25000", "0.45", "4.26", "-25", "0.7", "0.01"), (StatPackRules.Text(25000), StatPackRules.Text(0.45f),
            StatPackRules.Text(4.26f), StatPackRules.Text(-25), StatPackRules.Text(0.7f), StatPackRules.Text(0.01f)));
        Assert.All(StatPackRules.All.Where(r => r.Kind == StatWordKind.Points), r => Assert.Equal(-r.High, r.Low));
        // the table's one word of bytes is where the facts table has it (the generated list types the offset)
        Assert.Equal((int)GameFacts.Files.CarBytesAt, StatPackRules.All.Single(r => r.Kind == StatWordKind.Bytes).At);
        // what has been tried is inside what the game takes
        Assert.All(StatPackRules.All.Where(r => r.TriedTo is not null), r => Assert.True(r.Low <= 0 && r.TriedTo <= r.High));
    }

    /// <summary>What this program says of a value, and what the game's reader says (<c>port/core/src/files_stats.cpp</c>),
    /// are held to one list: <c>protocol/tests/vectors/stat-cases.tsv</c>, which <c>stat-cases.py</c> beside it writes from
    /// the handbook's table. The game's test (<c>TestTheStatCasesOfBothPrograms</c>) reads the same file, so the two
    /// programs cannot differ on a denormal, a -0, a floor or a bound.</summary>
    [Fact]
    public void EveryCaseOfTheListBothProgramsAreHeldToIsJudgedAsTheListSays()
    {
        var cases = File.ReadLines(Repository("protocol", "tests", "vectors", "stat-cases.tsv")).Skip(1).Where(line => line.Length > 0)
            .Select(line => line.Split('\t')).ToList();
        Assert.InRange(cases.Count, 1500, 5000);
        var wrong = new List<string>();
        foreach (var cells in cases)
        {
            Assert.Equal(5, cells.Length);
            var (block, offset) = (int.Parse(cells[0], CultureInfo.InvariantCulture), Convert.ToInt32(cells[1], 16));
            var bits = Convert.ToUInt32(cells[2], 16);
            var refused = StatPackRules.Problem(block, offset, bits) is not null;
            Assert.True(cells[3] is "take" or "refuse");
            if (refused != (cells[3] == "refuse"))
            {
                wrong.Add($"{cells[4]} ({cells[2]}): the list says {cells[3]}");
            }
            // and as a pack that arrives (a match start, a replay): this program takes exactly what the game takes
            if ((Arrives(block, offset, bits) is null) != (cells[3] == "refuse"))
            {
                wrong.Add($"{cells[4]} ({cells[2]}) arriving in a match start: the list says {cells[3]}");
            }
        }
        Assert.Empty(wrong);
        Assert.InRange(cases.Count(c => c[3] == "refuse"), 900, cases.Count - 1);
    }

    /// <summary>A pack of one value as a match start carries it, with the fingerprint of its own bytes, through
    /// the check of what arrives; null: refused.</summary>
    private static StatPack? Arrives(int block, int offset, uint bits)
    {
        byte[] values = [(byte)block, (byte)offset, (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits];
#pragma warning disable CA5350   // the fingerprint is a name, not a guard
        var fingerprint = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData([1, .. values]));
#pragma warning restore CA5350
        return StatPack.FromWire(new StatPackWire("p", fingerprint, Convert.ToBase64String(values))).Pack;
    }

    [Fact]
    public void TheRulesCoverABlocksWordsEachOnceAndNeverWordZero()
    {
        var named = Enumerable.Range(0, 0x100).Where(offset => StatPackRules.RuleAt(offset) is not null).ToList();

        Assert.Equal(Enumerable.Range(1, 44).Select(word => word * 4), named);
        Assert.Equal(44, StatPackRules.Words);
        Assert.Equal(44, named.Select(StatPackRules.WordName).Distinct().Count());
        Assert.All(named, offset => Assert.Equal(offset, StatPackRules.OffsetOf(StatPackRules.WordName(offset)!)));
        Assert.Equal(("Weight", "Bytes48", "Hover0X", "Hover0Z", "Hover3Z", "Wall0X", "Wall3Z"), (StatPackRules.WordName(4),
            StatPackRules.WordName(0x48), StatPackRules.WordName(0x54), StatPackRules.WordName(0x5C), StatPackRules.WordName(0x80),
            StatPackRules.WordName(0x84), StatPackRules.WordName(0xB0)));
        Assert.Null(StatPackRules.WordName(0));
        Assert.Null(StatPackRules.OffsetOf("Name"));
        Assert.Null(StatPackRules.OffsetOf("weight"));
        // the blocks: the machines by id, then 25 bodies, 25 cockpits, 25 boosters
        Assert.Equal((116, 40 * 0xB4, 0x1F04, 0x3098, 0x422C, 0x53C0), (StatPackRules.Blocks, StatPackRules.BlockAt(40),
            StatPackRules.BlockAt(41), StatPackRules.BlockAt(66), StatPackRules.BlockAt(91), StatPackRules.BlockAt(115) + 0xB4));
        Assert.Equal(("machine 0", "machine 40", "body 0", "body 24", "cockpit 0", "booster 0", "booster 24"), (StatPackRules.BlockName(0),
            StatPackRules.BlockName(40), StatPackRules.BlockName(41), StatPackRules.BlockName(65), StatPackRules.BlockName(66),
            StatPackRules.BlockName(91), StatPackRules.BlockName(115)));
    }

    private static string? Problem(int block, int at, float value) => StatPackRules.Problem(block, at, BitConverter.SingleToUInt32Bits(value));

    [Fact]
    public void AValueOutsideItsRangeIsRefusedWithTheRange()
    {
        Assert.Equal("is 0, and must be at least 1", Problem(6, 0x04, 0));
        Assert.Equal("is 0.5, and must be at least 1", Problem(6, 0x04, 0.5f));
        Assert.Equal("is 25000.5, and must be at most 25000", Problem(6, 0x04, 25000.5f));
        Assert.Equal("is -0.01, and must be at least 0", Problem(6, 0x0C, -0.01f));
        Assert.Equal("is 0.05, and must be at least 0.1", Problem(6, 0x34, 0.05f));
        Assert.Equal("is 4.26, and must be below 4.26", Problem(6, 0x38, 4.26f));
        Assert.Null(Problem(6, 0x10, -0.5f));                                                  // a negative where the disc has none is allowed
        Assert.Null(Problem(11, 0x3C, -0.02f));                                                // F-Zero GX+'s Super Piranha
        Assert.Equal("is -45.5, and must be at least -45", Problem(6, 0x10, -45.5f));
        Assert.Equal("is 45.5, and must be at most 45", Problem(6, 0x10, 45.5f));
        Assert.Equal("is -19.5, and must be at least -19", Problem(6, 0x4C, -19.5f));
        Assert.Equal("is 03040000: the second byte is at most 3 and the last two are 0", StatPackRules.Problem(6, 0x48, 0x03040000));
        Assert.Equal("is 03010001: the second byte is at most 3 and the last two are 0", StatPackRules.Problem(6, 0x48, 0x03010001));
        Assert.Equal("is 0, and a machine's hover point's Z is at least 0.01 from 0", Problem(6, 0x5C, 0));
        Assert.Equal("is -0.005, and a machine's hover point's Z is at least 0.01 from 0", Problem(40, 0x80, -0.005f));
        Assert.Equal("is -25.5, and must be at least -25", Problem(6, 0xB0, -25.5f));
    }

    /// <summary>A custom part's block (41 to 115) is what the part adds to a custom machine, of either sign: a part
    /// may take away what another gives, and GX Unleashed's bodies and cockpits do. So a part's number is held by
    /// its size alone, to a machine's most either side of 0: no sign, no floor and no Z rule of its own. A machine's
    /// block keeps every rule of its row.</summary>
    [Fact]
    public void APartsValueIsHeldByItsSizeAloneAndAMachinesByItsRow()
    {
        foreach (var (block, at, value) in new[]
                 {
                     (41, 0x04, 0f), (41, 0x08, -1f), (41, 0x30, -0.05f), (41, 0x34, -220f), (41, 0x38, -4.2599f), (41, 0x80, -0.005f),
                     (66, 0x3C, -0.7f), (66, 0x5C, 0f), (91, 0x04, -25000f), (91, 0x10, 0f), (91, 0x34, 0f), (115, 0x04, 0.5f),
                 })
        {
            Assert.Null(Problem(block, at, value));
        }
        Assert.Null(StatPackRules.Problem(115, 0x48, 0xFF030000));
        Assert.Equal("is -10.5, and a part's value is at most 10 from 0", Problem(41, 0x30, -10.5f));
        Assert.Equal("is 25000.5, and a part's value is at most 25000 from 0", Problem(115, 0x04, 25000.5f));
        Assert.Equal("is -4.26, and a part's value is less than 4.26 from 0", Problem(94, 0x38, -4.26f));
        Assert.Equal("is 25.5, and a part's value is at most 25 from 0", Problem(66, 0xB0, 25.5f));
        Assert.Equal("is 03040000: the second byte is at most 3 and the last two are 0", StatPackRules.Problem(66, 0x48, 0x03040000));
        // the same values in a machine's block
        Assert.Null(Problem(40, 0x30, -0.05f));
        Assert.Equal("is -10.5, and must be at least -10", Problem(40, 0x30, -10.5f));
        Assert.Equal("is 0, and must be at least 1", Problem(40, 0x04, 0));
        // what has been tried is a machine's matter: a part's value is raced in no build yet, and is not warned of
        Assert.Null(StatPackRules.Caution(41, 0x30, BitConverter.SingleToUInt32Bits(-0.05f)));
        Assert.Null(StatPackRules.Caution(66, 0x10, BitConverter.SingleToUInt32Bits(40)));
        Assert.NotNull(StatPackRules.Caution(40, 0x10, BitConverter.SingleToUInt32Bits(40)));
    }

    /// <summary>On the bits, before anything is compared: the race reads a denormal as zero, so a denormal weight is
    /// a division by nothing and a denormal Z is "not 0" only to a comparison. A value is the bits of 0 or a normal
    /// number, in every row.</summary>
    [Theory]
    [InlineData(0x00000001u)]
    [InlineData(0x007FFFFFu)]
    [InlineData(0x80000000u)]
    [InlineData(0x80000001u)]
    [InlineData(0x7F800000u)]
    [InlineData(0xFF800000u)]
    [InlineData(0x7FC00000u)]
    public void ADenormalAMinusZeroAnInfinityAndANaNAreRefusedOnTheirBitsInEveryRow(uint bits)
    {
        var said = $"is the bits {bits:X8}, which is no number a pack may hold (a denormal, -0, an infinity or none at all)";
        foreach (var rule in StatPackRules.All.Where(r => r.Kind != StatWordKind.Bytes))
        {
            for (var block = 6; block <= 91; block += block == 6 ? 35 : 25)   // a machine, a body, a cockpit, a booster
            {
                Assert.Equal(said, StatPackRules.Problem(block, rule.At, bits));
            }
        }
        Assert.Equal("machine 6 Weight " + said + ".", StatPack.Of("p", [new StatEntry(6, 4, bits)]).Refusal);
        Assert.Equal("machine 6 Weight " + said + ".",
            StatPack.FromWire(new StatPackWire("p", "", Convert.ToBase64String([6, 4, (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits]))).Refusal);
    }

    /// <summary>The ranges of caution refuse nothing (a later pack must need a server change
    /// only, never a new launcher): a value the game takes is taken where a pack is made, where a server reads its
    /// file and where a launcher is handed one, and a value beyond what has been tried is only said, as a warning
    /// to whoever makes the pack.</summary>
    [Fact]
    public void WhatHasBeenTriedIsSaidWhereAPackIsMadeAndRefusedNowhere()
    {
        string[] cautious = ["Grip1", "Grip3", "TurnTension", "DriftAcceleration", "TurnMovement", "StrafeTurn", "Strafe", "TurnReaction", "Grip2"];
        Assert.Equal(cautious, StatPackRules.All.Where(r => r.TriedTo is not null).Select(r => r.Name));
        var beyond = StatEntry.Of(6, 0x10, 9.5f);
        Assert.Null(StatPackRules.Problem(6, 0x10, beyond.Bits));                              // the game takes it
        Assert.Null(StatPackRules.Caution(6, 0x10, BitConverter.SingleToUInt32Bits(9)));
        Assert.Null(StatPackRules.Caution(6, 0x04, BitConverter.SingleToUInt32Bits(24999)));   // a weight has no such range
        const string said = "is 9.5, outside what has been tried (0 to 9)";
        Assert.Equal(said, StatPackRules.Caution(6, 0x10, beyond.Bits));
        // made, read from a file, and arriving in a match start: a pack each time, and the same one
        var made = Assert.IsType<StatPack>(StatPack.Of("p", [beyond]).Pack);
        var read = Assert.IsType<StatPack>(StatPackText.Read("fzstats 1\nname p\nmachine 6 Grip1 9.5\n").Pack);
        var arrived = Assert.IsType<StatPack>(StatPack.FromWire(made.ToWire()).Pack);
        Assert.Equal((made.Fingerprint, made.Fingerprint), (read.Fingerprint, arrived.Fingerprint));
        Assert.Equal(["machine 6 Grip1 " + said + "."], arrived.Untried);
        Assert.Equal(["warning: 1 of the pack's values is outside what has been tried. The game takes them; nobody has raced them:",
            "  machine 6 Grip1 " + said + "."], made.UntriedWarning());
        Assert.Empty(StatPack.Of("p", [StatEntry.Of(6, 0x10, 9)]).Pack!.UntriedWarning());
        // to the game's own bound and not past it: every row's most is taken on arrival, the next number is not
        foreach (var rule in StatPackRules.All.Where(r => r.TriedTo is not null))
        {
            Assert.NotNull(StatPack.FromWire(StatPack.Of("p", [StatEntry.Of(6, rule.At, rule.High)]).Pack!.ToWire()).Pack);
            Assert.Null(StatPack.Of("p", [StatEntry.Of(6, rule.At, MathF.BitIncrement(rule.High))]).Pack);
        }
        // a long list is cut: the count, the first ten, and how many more
        var many = StatPack.Of("p", Enumerable.Range(0, 41).Select(machine => StatEntry.Of(machine, 0x10, 9.5f))).Pack!;
        var lines = many.UntriedWarning();
        Assert.Equal(12, lines.Count);
        Assert.Equal(("warning: 41 of the pack's values are outside what has been tried. The game takes them; nobody has raced them:",
            "  machine 0 Grip1 " + said + ".", "  and 31 more."), (lines[0], lines[1], lines[^1]));
    }

    [Fact]
    public void AValueAtTheEdgeOfItsRangeAndAPartsZeroAreTaken()
    {
        Assert.All(new (int Block, int At, float Value)[]
        {
            (6, 0x04, 1), (6, 0x04, 25000), (6, 0x08, 0), (6, 0x10, 45), (6, 0x38, 4.2599f), (6, 0x4C, 19), (6, 0x54, 0), (6, 0x58, 0),
            (6, 0x5C, -0.01f), (6, 0x8C, 0), (41, 0x34, 0), (66, 0x5C, 0), (91, 0x10, 0), (91, 0x34, 0.1f),
        }, v => Assert.Null(Problem(v.Block, v.At, v.Value)));
        Assert.Null(StatPackRules.Problem(6, 0x48, 0xFF030000));
    }
}
