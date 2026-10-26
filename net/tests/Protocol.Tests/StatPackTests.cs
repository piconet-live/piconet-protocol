using Online.Protocol;

namespace Protocol.Tests;

/// <summary>
/// A stat pack (<see cref="StatPack"/>; handbook/stat-packs.md) as the match start carries it: the bytes,
/// the fingerprint, and every pack that is refused. The made-up pack here is the game's tests' too
/// (piconet-runner's <c>port/core/tests/stat_packs.h</c>, <c>ThePack</c>): its base64 and its fingerprint were
/// worked out by neither program, so the two agree on how a pack travels.
/// </summary>
public sealed class StatPackTests
{
    internal const string Values = "BgRFHYAABkgDAQAAKFy/wAAAKQRD+gAAczRBYAAA";
    internal const string Fingerprint = "8472faeb0e46573e5c279e752ecf65c50452d8ee";

    /// <summary>The Blue Falcon's weight doubled and its two bytes, the last machine's first hover point's Z, the
    /// first body's weight, the last booster's boost strength.</summary>
    internal static StatEntry[] Made =>
    [
        StatEntry.Of(6, 0x04, 2520), new(6, 0x48, 0x03010000), StatEntry.Of(40, 0x5C, -1.5f), StatEntry.Of(41, 0x04, 500),
        StatEntry.Of(115, 0x34, 14),
    ];

    private static StatPack Pack(string name, params StatEntry[] entries)
    {
        var (pack, refusal) = StatPack.Of(name, entries);
        Assert.Null(refusal);
        return pack!;
    }

    private static string? Refusal(params StatEntry[] entries) => StatPack.Of("p", entries).Refusal;

    [Fact]
    public void TheMadeUpPackTravelsAsTheGamesTestsReadIt()
    {
        var pack = Pack("GX Made Up 1.0", Made);

        Assert.Equal(new StatPackWire("GX Made Up 1.0", Fingerprint, Values), pack.ToWire());
        // the values in any order are one pack, and the name is not in the fingerprint
        Assert.Equal(Fingerprint, Pack("Another name", [.. Enumerable.Reverse(Made)]).Fingerprint);
        Assert.Equal(Made, Pack("p", [.. Enumerable.Reverse(Made)]).Entries);
        Assert.NotEqual(Fingerprint, Pack("p", [.. Made[..4]]).Fingerprint);
        Assert.NotEqual(Fingerprint, Pack("p", [.. Made[..4], StatEntry.Of(115, 0x34, 14.5f)]).Fingerprint);
        // and back
        var (back, refusal) = StatPack.FromWire(pack.ToWire());
        Assert.Null(refusal);
        Assert.Equal((pack.Name, pack.Fingerprint), (back!.Name, back.Fingerprint));
        Assert.Equal(pack.Entries, back.Entries);
    }

    [Fact]
    public void TheRaceSetupCarriesItAsOneOptionalMember()
    {
        RaceSetup setup = new(8, 3, [new SeatSetup(0, "p1", "ANA", 6, null)], 1, 2);
        MatchStart start = new("m7", new RelayEndpoint("r", "", 47811), "1f", 0, setup);

        Assert.DoesNotContain("stats", ControlProtocol.Encode(start), StringComparison.Ordinal);
        var text = ControlProtocol.Encode(start with { Setup = setup with { Stats = Pack("Made up", Made).ToWire() } });
        // (the JSON writer escapes the plus of base64; a reader gives it back)
        Assert.Contains($$"""
            "stats":{"name":"Made up","fingerprint":"{{Fingerprint}}","values":"BgRFHYAABkgDAQAAKFy/wAAAKQRD
            """.Trim(), text, StringComparison.Ordinal);
        var back = Assert.IsType<MatchStart>(ControlProtocol.DecodeServer(text));
        Assert.Equal(new StatPackWire("Made up", Fingerprint, Values), back.Setup.Stats);
        // a message from before stat packs has none
        Assert.Null(Assert.IsType<MatchStart>(ControlProtocol.DecodeServer(ControlProtocol.Encode(start))).Setup.Stats);
    }

    [Fact]
    public void APackOfValuesThatAreNoPackIsRefusedWithTheRule()
    {
        Assert.Equal("The pack holds no value.", Refusal());
        Assert.Equal("Block 116 is none of the car data's 116.", Refusal(StatEntry.Of(116, 4, 1)));
        Assert.Equal("Block -1 is none of the car data's 116.", Refusal(StatEntry.Of(-1, 4, 1)));
        Assert.Equal("machine 6: word 0 is the place of its name, and a pack changes no name.", Refusal(new StatEntry(6, 0, 0x23801CE0)));
        Assert.Equal("cockpit 24: word 0 is the place of its name, and a pack changes no name.", Refusal(new StatEntry(90, 0, 0)));
        foreach (var at in new[] { 0x06, 0x49, 0xB4, 0xFC, -4, 0x100 })
        {
            Assert.Equal($"machine 6: no word a pack may change is at offset 0x{at:X2}.", Refusal(StatEntry.Of(6, at, 1)));
        }
        Assert.Equal("machine 6 Weight is there twice.", Refusal(StatEntry.Of(6, 4, 900), StatEntry.Of(7, 4, 900), StatEntry.Of(6, 4, 901)));
        Assert.Equal("machine 6 Weight is 0, and must be at least 1.", Refusal(StatEntry.Of(6, 4, 0)));
        Assert.Equal("booster 0 BoostStrength is 220.5, and a part's value is at most 220 from 0.", Refusal(StatEntry.Of(91, 0x34, 220.5f)));
        Assert.Null(Refusal(StatEntry.Of(91, 0x34, 0), StatEntry.Of(41, 0x30, -0.05f)));   // a part: any sign, no floor
        foreach (var name in new[] { "", new string('n', 41), " p", "p ", "p\n", "pé", null })
        {
            Assert.StartsWith("A pack's name is 1 to 40 characters", StatPack.Of(name!, Made).Refusal, StringComparison.Ordinal);
        }
        Assert.NotNull(StatPack.Of(new string('n', 40), Made).Pack);
    }

    [Fact]
    public void WhatAMatchStartCarriesIsCheckedAsTheGameChecksIt()
    {
        static string? Refused(string values, string fingerprint = Fingerprint, string name = "p") =>
            StatPack.FromWire(new StatPackWire(name, fingerprint, values)).Refusal;

        Assert.Null(Refused(Values));
        // the fingerprint is the values' own
        Assert.Equal($"The pack's values have the fingerprint {Fingerprint}, and it says another.", Refused(Values, "8472faea" + Fingerprint[8..]));
        Assert.EndsWith("and it says another.", Refused(Values, ""), StringComparison.Ordinal);
        Assert.EndsWith("and it says another.", Refused(Values, null!), StringComparison.Ordinal);
        // base64 as it is written one way: no space, no other padding, no bits left over; and not more than a table
        foreach (var bad in new[]
                 {
                     Values[1..], "*" + Values[1..], Values[..20] + " " + Values[20..], Values[..20] + "\n" + Values[20..],
                     Values[..38] + "B=", Values[..37] + "B==", new string('A', 42000), "", null,
                 })
        {
            Assert.Equal("The pack's values are not base64, or are more than the whole car data.", Refused(bad!));
        }
        Assert.Equal("The pack's values are 29 bytes, which is not whole entries of 6.", Refused(Values[..39] + "="));
        Assert.Equal("The pack's values are 8 bytes, which is not whole entries of 6.", Refused("BgRFHYAABkg="));
        // in the order of block and offset, each once: the game takes no other order
        var swapped = Convert.ToBase64String([.. Convert.FromBase64String(Values)[6..12], .. Convert.FromBase64String(Values)[..6]]);
        Assert.Equal("The pack's entry 1 is out of order.", Refused(swapped));
        var twice = Convert.ToBase64String([.. Convert.FromBase64String(Values)[..6], .. Convert.FromBase64String(Values)[..6]]);
        Assert.Equal("machine 6 Weight is there twice.", Refused(twice));
        // and everything a pack of values is refused for
        Assert.Equal("machine 6 Weight is 0, and must be at least 1.", Refused(Convert.ToBase64String([6, 4, 0, 0, 0, 0])));
        Assert.StartsWith("A pack's name is", Refused(Values, name: "p\t"), StringComparison.Ordinal);
    }

    /// <summary>All 5,104 words at once: a whole replacement of the car data is a pack like any other, 30 KB as it
    /// travels.</summary>
    [Fact]
    public void TheWholeCarDataIsAPack()
    {
        var all = Enumerable.Range(0, StatPackRules.Blocks).SelectMany(block => Enumerable.Range(1, StatPackRules.Words).Select(word =>
            word * 4 == 0x48 ? new StatEntry(block, 0x48, 0x07030000) : StatEntry.Of(block, word * 4, word == 1 ? 1300 : 0.125f))).ToArray();

        var pack = Pack("Whole", all);

        Assert.Equal((5104, 30624), (pack.Entries.Count, pack.Bytes().Length));
        Assert.Equal(pack.Fingerprint, StatPack.FromWire(pack.ToWire()).Pack!.Fingerprint);
        Assert.InRange(pack.ToWire().Values.Length, 40000, 41000);
    }
}
