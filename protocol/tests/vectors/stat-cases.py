#!/usr/bin/env python3
"""stat-cases.py: writes stat-cases.tsv, the list of values both programs' checks of a stat pack are held to.

The game checks a pack's values in C++ (port/core/src/files_stats.cpp) and the launcher and the server in C#
(net/src/Online.Protocol/StatPackRules.cs). Each has a test that reads the list and must say of every case what the
list says (TestTheStatCasesOfBothPrograms; StatPackRulesTests), so the two cannot differ on a value. The verdicts
are worked out here a third time, from the table of ranges on handbook/stat-packs.md and nothing else.

A case: block, offset, the 32 bits, "take" or "refuse", and what it is. For every row of the table, in a machine's
block and in one of each kind of part: the bits of 0, -0, the smallest and the largest denormal, the smallest normal
number, the infinities, a NaN, the largest number, each bound and its neighbours either side, minus the most and
its neighbours, a value inside and its negative; for the points a Z at the least distance and just inside it; for
the two bytes each byte's edge. A machine's block is held to its row; a part's (41 to 115) by the row's size alone:
any sign, no farther from 0 than the most, no floor and no Z rule (the page says why).
Run it after a range changes on the page; the tests fail until it has been. Standard library only."""
import os
import re
import struct

HERE = os.path.dirname(os.path.abspath(__file__))
PAGE = os.path.join(HERE, "..", "..", "..", "handbook", "stat-packs.md")
BLOCKS = [(6, "a machine"), (41, "a body"), (66, "a cockpit"), (91, "a booster")]
MACHINES = 41


def bits(value):
    return struct.unpack(">I", struct.pack(">f", value))[0]


def number(pattern):
    return struct.unpack(">f", struct.pack(">I", pattern))[0]


def single(value):
    """The 32-bit number nearest a decimal of the page, as both programs' tables hold it."""
    return number(bits(value))


def rows():
    """The table's rows: (offset, name, kind, low, high, below, part, z_least)."""
    out, inside = [], False
    with open(PAGE, encoding="utf-8") as f:
        for line in f:
            if line.startswith("| At | Name in a pack |"):
                inside = True
            elif inside and line.startswith("| 0x"):
                at, name, what, low, high, part, _ = [cell.strip() for cell in line.strip().strip("|").split("|")]
                kind = "bytes" if what.startswith("two bytes") else "points" if what.startswith("four points") else "number"
                z = re.search(r"a machine's Z at least ([0-9.]+) from 0", what)
                out.append((int(at, 16), name, kind,
                            float(low.split()[-1]) if kind != "bytes" else 0.0, float(high.split()[-1]), high.startswith("below"),
                            part, float(z[1]) if z else 0.0))
            elif inside and not line.startswith("|"):
                break
    return out


def verdict(block, offset, pattern, row):
    at, _, kind, low, high, below, _, z_least = row
    if kind == "bytes":
        return (pattern >> 16) & 0xFF <= int(high) and pattern & 0xFFFF == 0
    exponent = (pattern >> 23) & 0xFF
    if pattern != 0 and exponent in (0, 0xFF):
        return False
    value = number(pattern)
    if block >= MACHINES:   # a part: what it adds to a custom machine, of either sign; its size alone
        return abs(value) < single(high) if below else abs(value) <= single(high)
    if z_least and (offset - at) // 4 % 3 == 2 and abs(value) < single(z_least):
        return False
    if not value >= single(low):
        return False
    return value < single(high) if below else value <= single(high)


def neighbours(value):
    """The number and the next one either side of it, each with how it lies to the number."""
    pattern = bits(value)
    if pattern == 0:
        return [(0, "at")]
    return [(pattern - 1, "the next number nearer 0 than"), (pattern, "at"), (pattern + 1, "the next number farther from 0 than")]


def cases():
    out = []
    for row in rows():
        at, name, kind, low, high, _, _, z_least = row
        offsets = [at] if kind != "points" else [at, at + 8, at + 0x2C]   # the first X, the first Z, the last Z
        for block, where in BLOCKS:
            for offset in offsets:
                if kind == "bytes":
                    tried = [(0, "both bytes 0"), (0xFF000000 | int(high) << 16, "the most of each byte"),
                             (0xFF000000 | (int(high) + 1) << 16, "the second byte one too many"), (0x03010001, "the last byte not 0"),
                             (0x03010100, "the third byte not 0"), (0xFFFFFFFF, "every bit")]
                else:
                    tried = [(0, "the bits of 0"), (0x80000000, "-0"), (0x00000001, "the smallest denormal"),
                             (0x007FFFFF, "the largest denormal"), (0x80000001, "a negative denormal"),
                             (0x00800000, "the smallest normal number"), (0x7F800000, "infinity"), (0xFF800000, "minus infinity"),
                             (0x7FC00000, "a NaN"), (0x7F7FFFFF, "the largest number"), (0xFF7FFFFF, "the largest negative number")]
                    for bound, what in ((low, "the least"), (high, "the most")) + (((-high, "minus the most"),) if kind == "number" else ()):
                        tried += [(pattern, f"{how} {what}") for pattern, how in neighbours(single(bound))]
                    inside = (single(low) + single(high)) / 2 if low >= 0 else single(high) / 2
                    tried += [(bits(inside), "inside"), (bits(-inside), "inside, negative")]
                    if z_least:
                        for sign in (1, -1):
                            tried += [(pattern, f"{how} the least Z {'above' if sign > 0 else 'below'} 0")
                                      for pattern, how in neighbours(sign * single(z_least))]
                word = (offset - at) // 4
                stat = name if kind != "points" else f"{name.split('0X')[0]}{word // 3}{'XYZ'[word % 3]}"
                for pattern, what in tried:
                    taken = verdict(block, offset, pattern, row)
                    out.append(f"{block}\t0x{offset:02X}\t{pattern:08X}\t{'take' if taken else 'refuse'}\t{stat} of {where}: {what}")
    return out


if __name__ == "__main__":
    lines = ["block\toffset\tbits\tverdict\twhat"] + cases()
    with open(os.path.join(HERE, "stat-cases.tsv"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"stat-cases.tsv: {len(lines) - 1} cases, {sum(1 for line in lines if chr(9) + 'refuse' + chr(9) in line)} refused")
