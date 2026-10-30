using System.Globalization;
using Online.Protocol;

namespace Protocol.Tests;

/// <summary>
/// A stat pack's code constants (<see cref="StatPackConstants"/>): the list against the handbook's table, a pack with
/// constants as a file and as a match start carries it, what is refused, and a pack of the car data alone, which
/// stays the pack it was. The list is described once (protocol/stats.toml) and generated three times: here, in the
/// game (<c>port/core/src/const_ranges.h</c>) and in the table of handbook/stat-pack-constants.md. Each program's
/// test holds its own list to the table, row for row (the game's: TheListIsTheHandbooks,
/// piconet-runner's port/core/tests/test_constants.cpp), as the stat ranges are (StatPackRulesTests): so neither
/// reads the other's source, and each stays in its own repository.
/// </summary>
public sealed class StatPackConstantsTests
{
    // GX Unleashed's three changes behind the made-up pack's first value; the game's tests have the same two texts
    // (port/core/tests/test_constants.cpp)
    private const string UnleashedValues = "BgRFHYAA8AA+GZma8AF/7///8AL/////8ANB8AAA8ARBcAAA";
    private const string UnleashedFingerprint = "f7929faed0937749e3cc81dcd35e2b500bb907a2";

    private static StatEntry Constant(int slot, uint bits) => new(StatPackConstants.Block, slot, bits);

    private static StatEntry[] Unleashed() =>
    [
        StatEntry.Of(6, 0x04, 2520), Constant(0, 0x3E19999A), Constant(1, 0x7FEFFFFF), Constant(2, 0xFFFFFFFF),
        Constant(3, BitConverter.SingleToUInt32Bits(30)), Constant(4, BitConverter.SingleToUInt32Bits(15)),
    ];

    private static string Repository(params string[] path) => TestSupport.RepositoryFolder.Of(path);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>rules.toml's default names a pack the repository holds, by the pack's own name: the name a server
    /// looks for, and a launcher's rows and a practice choose by.</summary>
    [Fact]
    public void TheDefaultPackIsAFileOfTheRepository()
    {
        var names = Directory.EnumerateFiles(Repository("protocol", "packs"), "*" + StatPackText.Extension)
            .Select(f => StatPackText.Read(File.ReadAllText(f)).Pack?.Name).ToList();
        Assert.Contains(HostRules.DefaultStatPack, names);
        Assert.Equal("stat-pack", HostRules.StatPack.Name);
        Assert.Equal(0, HostRules.StatPack.Default);   // (the rule's number stays none: the pack goes by its name)
    }

    [Fact]
    public void TheListIsTheHandbooksRowForRow()
    {
        Assert.Equal(StatPackConstants.Slots, StatPackConstants.All.Count);
        Assert.Equal(Enumerable.Range(0, StatPackConstants.Slots), StatPackConstants.All.Select(c => c.Slot));
        var handbook = File.ReadLines(Repository("handbook", "stat-pack-constants.md"))
            .SkipWhile(line => !line.StartsWith("| Slot | Name in a pack |", StringComparison.Ordinal)).Skip(2)
            .TakeWhile(line => line.StartsWith('|')).ToArray();
        var rows = StatPackConstants.All.Where(c => c.Kind != StatConstantKind.Number64Low).Select(c =>
            $"| {(c.Kind == StatConstantKind.Number64High ? $"{c.Slot}, {c.Slot + 1}" : c.Slot)} | {c.Name} | " +
            $"{(c.Kind == StatConstantKind.Number32 ? 32 : 64)} bits | {StatPackConstants.Text(c, c.Stock, c.Kind == StatConstantKind.Number64High ? StatPackConstants.All[c.Slot + 1].Stock : 0)} | {Number(c.Low)} | {Number(c.High)} |").ToArray();
        Assert.Equal(rows.Length, handbook.Length);
        Assert.All(rows.Zip(handbook), pair => Assert.StartsWith(pair.First, pair.Second, StringComparison.Ordinal));
        // the table has no row of a double's low word: it is the slot after its high word's, of the same name, and
        // bounds nothing (the game's test holds its list to the same)
        Assert.All(StatPackConstants.All.Where(c => c.Kind == StatConstantKind.Number64Low), c =>
            Assert.Equal((StatPackConstants.All[c.Slot - 1].Kind, StatPackConstants.All[c.Slot - 1].Name, 0.0, 0.0),
                (StatConstantKind.Number64High, c.Name, c.Low, c.High)));
        Assert.All(StatPackConstants.All.Where(c => c.Kind == StatConstantKind.Number64High),
            c => Assert.Equal(StatConstantKind.Number64Low, StatPackConstants.All[c.Slot + 1].Kind));
        // every value the game holds is one its own list takes
        Assert.All(StatPackConstants.All.Where(c => c.Kind != StatConstantKind.Number64Low),
            c => Assert.Null(StatPackConstants.Problem(c.Slot, c.Stock, StatPackConstants.All[Math.Min(c.Slot + 1, StatPackConstants.Slots - 1)].Stock)));
    }

    [Fact]
    public void APackWithConstantsTravelsAsTheGamesTestsReadIt()
    {
        var (pack, refusal) = StatPack.Of("GX Unleashed", Unleashed().Reverse());
        Assert.Null(refusal);
        Assert.NotNull(pack);
        Assert.Single(pack.Entries);
        Assert.Equal(5, pack.Constants.Count);
        Assert.Equal(UnleashedValues, pack.ToWire().Values);
        Assert.Equal(UnleashedFingerprint, pack.Fingerprint);
        var (back, why) = StatPack.FromWire(pack.ToWire());
        Assert.Null(why);
        Assert.Equal(pack.Fingerprint, back!.Fingerprint);
        Assert.Equal(pack.Constants, back.Constants);
        // the file: one line a constant, a double as one number, and back to the same bits
        var text = StatPackText.Write(pack);
        Assert.Contains("constant BoostEnergyUse 0.15\n", text, StringComparison.Ordinal);
        Assert.Contains("constant SpeedCap 1.7976931348623157E+308\n", text, StringComparison.Ordinal);
        Assert.Contains("constant DashPlateBase 30\nconstant DashPlateScale 15\n", text, StringComparison.Ordinal);
        Assert.Equal(pack.Fingerprint, StatPackText.Read(text).Pack?.Fingerprint);
        Assert.Equal(
            [("Boost energy", "0.15"), ("Speed cap", "none"), ("Dash plate base", "30"), ("Dash plate scale", "15")],
            StatPackConstants.Described(pack.Constants).Select(d => (d.Constant.What, d.Value)));
    }

    [Fact]
    public void APackOfTheCarDataAloneIsThePackItWas()
    {
        var (pack, _) = StatPack.Of("Made", [StatEntry.Of(6, 0x04, 2520)]);
        Assert.Empty(pack!.Constants);
        Assert.Equal("BgRFHYAA", pack.ToWire().Values);
        Assert.DoesNotContain("constant", StatPackText.Write(pack), StringComparison.Ordinal);
        // and a pack of constants alone is a pack
        Assert.NotNull(StatPack.Of("Energy", [Constant(0, BitConverter.SingleToUInt32Bits(0.15f))]).Pack);
        Assert.NotNull(StatPackText.Read("fzstats 1\nname Energy\nconstant BoostEnergyUse 0.15\n").Pack);
    }

    [Fact]
    public void TheProtocolsGxUnleashedWithoutItsConstantsIsThePackOfBefore()
    {
        // the file before constants (0.2.x, c24e75a8) is the same file without its constant lines: a pack file from
        // before still reads, to the fingerprint it always had
        var text = File.ReadAllText(Repository("protocol", "packs", "gx-unleashed.fzstats"));
        var (pack, refusal) = StatPackText.Read(text);
        Assert.Null(refusal);
        Assert.Equal(1320, pack!.Entries.Count);
        var before = string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("constant ", StringComparison.Ordinal)));
        var (old, _) = StatPackText.Read(before);
        Assert.Empty(old!.Constants);
        Assert.Equal("c24e75a89b536ee679c312f1474082da6418b55c", old.Fingerprint);
    }

    [Fact]
    public void TheCurvesSlotsTravelAndReadBack()
    {
        var gain = BitConverter.DoubleToUInt64Bits(0.75);
        var (pack, refusal) = StatPack.Of("Curve", [Constant(11, BitConverter.SingleToUInt32Bits(1)), Constant(40, (uint)(gain >> 32)), Constant(41, (uint)gain)]);
        Assert.Null(refusal);
        var text = StatPackText.Write(pack!);
        Assert.Contains("constant DriftFastLoss 1\nconstant AccelGain 0.75\n", text, StringComparison.Ordinal);
        Assert.Equal(pack!.Fingerprint, StatPackText.Read(text).Pack?.Fingerprint);
        Assert.Equal([("Setting: drift loss, speed side", "1"), ("Setting: acceleration gain", "0.75")],
            StatPackConstants.Described(pack.Constants).Select(d => (d.Constant.What, d.Value)));
    }

    [Fact]
    public void APackNamingOneCurveSlotHoldsThatSlotAlone()
    {
        // every slot is optional: a file naming one holds one constant, and the others are the game's own
        var (pack, refusal) = StatPackText.Read("fzstats 1\nname One\nconstant DriftCap 2.5\n");
        Assert.Null(refusal);
        var only = Assert.Single(pack!.Constants);
        Assert.Equal((StatPackConstants.Named("DriftCap")!.Slot, BitConverter.SingleToUInt32Bits(2.5f)), (only.At, only.Bits));
        Assert.Empty(pack.Entries);
        Assert.Null(StatPackConstants.BoostLengthProblem(pack.Constants, 4.26f));
        var (back, why) = StatPack.FromWire(pack.ToWire());
        Assert.Null(why);
        Assert.Equal(pack.Fingerprint, back!.Fingerprint);
    }

    [Fact]
    public void ABoostLengthPastItsByteIsAProblem()
    {
        StatEntry[] Length(float b, float s) => [Constant(5, BitConverter.SingleToUInt32Bits(b)), Constant(6, BitConverter.SingleToUInt32Bits(s))];
        Assert.Null(StatPackConstants.BoostLengthProblem([], 9f));                     // the game's own: never judged
        Assert.Null(StatPackConstants.BoostLengthProblem(Length(0, 60), 4.25f));       // 255
        Assert.Null(StatPackConstants.BoostLengthProblem(Length(60, 60), 3.25f));      // 255
        Assert.Contains("is 256 frames", StatPackConstants.BoostLengthProblem(Length(60, 60), 3.27f), StringComparison.Ordinal);
        Assert.Contains("the game counts at most 255", StatPackConstants.BoostLengthProblem([Constant(5, BitConverter.SingleToUInt32Bits(60))], 3.5f), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstantsThatAreNoPackAreRefusedWithTheRule()
    {
        string? Refused(params StatEntry[] entries) => StatPack.Of("p", entries).Refusal;
        Assert.Contains("slot 62", Refused(Constant(62, 0)), StringComparison.Ordinal);
        Assert.Contains("BoostLengthScale is 61, and must be at most 60", Refused(Constant(6, BitConverter.SingleToUInt32Bits(61))), StringComparison.Ordinal);
        Assert.Contains("AccelGain is a 64-bit number", Refused(Constant(40, 0x3FE33333)), StringComparison.Ordinal);
        Assert.Contains("SpeedSlowSpan is 0, and must be at least 0.01", Refused(Constant(32, 0)), StringComparison.Ordinal);
        Assert.Contains("The constant DashPlateBase is there twice", Refused(Constant(3, 0), Constant(3, 0)), StringComparison.Ordinal);
        Assert.Contains("SpeedCap is a 64-bit number", Refused(Constant(1, 0x7FEFFFFF)), StringComparison.Ordinal);
        Assert.Contains("SpeedCap is a 64-bit number", Refused(Constant(2, 0)), StringComparison.Ordinal);
        Assert.Contains("BoostEnergyUse is 1.75, and must be at most 1.7", Refused(Constant(0, BitConverter.SingleToUInt32Bits(1.75f))), StringComparison.Ordinal);
        Assert.Contains("DashPlateBase is -1, and must be at least 0", Refused(Constant(3, BitConverter.SingleToUInt32Bits(-1))), StringComparison.Ordinal);
        Assert.Contains("DashPlateScale is 45.5, and must be at most 45", Refused(Constant(4, BitConverter.SingleToUInt32Bits(45.5f))), StringComparison.Ordinal);
        foreach (var bits in new uint[] { 0x80000000, 0x00000001, 0x7F800000, 0x7FC00000 })
        {
            Assert.Contains("no number a pack may hold", Refused(Constant(3, bits)), StringComparison.Ordinal);
        }
        Assert.Contains("SpeedCap is 999.9999999999999, and must be at least 1000", Refused(Constant(1, 0x408F3FFF), Constant(2, 0xFFFFFFFF)), StringComparison.Ordinal);
        foreach (var (high, low) in new[] { (0x7FF00000u, 0u), (0x7FF80000u, 0u), (0x80000000u, 0u), (0u, 1u) })
        {
            Assert.Contains("no number a pack may hold", Refused(Constant(1, high), Constant(2, low)), StringComparison.Ordinal);
        }
        // as a file
        Assert.Contains("Line 3: \"Gravity\" is no constant of a pack", StatPackText.Read("fzstats 1\nname p\nconstant Gravity 1\n").Refusal, StringComparison.Ordinal);
        Assert.Contains("Line 4: the constant BoostEnergyUse is there twice", StatPackText.Read("fzstats 1\nname p\nconstant BoostEnergyUse 0.1\nconstant BoostEnergyUse 0.1\n").Refusal, StringComparison.Ordinal);
        Assert.Contains("Line 3: the constant SpeedCap is the bits 7FF0000000000000", StatPackText.Read("fzstats 1\nname p\nconstant SpeedCap 1e309\n").Refusal, StringComparison.Ordinal);
        Assert.Contains("Line 3: DashPlateBase's value is one decimal number", StatPackText.Read("fzstats 1\nname p\nconstant DashPlateBase 3 0\n").Refusal, StringComparison.Ordinal);
    }
}
