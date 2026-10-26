# Handbook: a stat pack's code constants

A stat pack (`stat-packs.md`) carries the car data's words and, since 0.3.0, the game's **code constants**: words of
main.rel that a mod changes (GX Unleashed's boost energy and speed cap) and the dash plate's two numbers; since 0.4.0
also the energy boost's length and every number of the machine setting's curve (F-Zero GX+ changes one: the speed
side's drift loss). Every slot is optional: a pack names the ones it changes, and every one it leaves out is the
game's own. For modders writing packs by hand: `protocol/packs/stat-pack-guide.md` (this table stays the engineers'
reference). One pack,
one fingerprint, one vote: "vanilla" is no pack, and a pack of the car data alone is the pack it always was (the same
bytes, the same fingerprint, the same replay version). Why constants and not code: the game's code
runs as the compiled native module, so changed code needs a new game build, while that code reads its constants from
memory, so a word laid before the race is read as the car data is; and a constant is shared by several routines, so
only listed words, each with every reader, may change.

## The allowed words

The list is frozen into a game build, like the stat ranges: a word not on it, or a value outside its range, needs the
next build. It is described once, in `protocol/stats.toml` (with each constant's readers, place and GX Unleashed's
value), and `protocol/generate.py` writes it three times: the game's table (`generated/const_ranges.g.h`, which
`port/core/src/const_ranges.h` includes and holds to the facts table's places; it names every word's every reader in
the code), the launcher's and the server's (`generated/StatTables.g.cs`: `StatPackConstants.All`) and the table
below, which each program's test holds its list to (`StatPackConstantsTests`; the core's `TestAStatPacksConstants`).
Every value is the bits of 0 or a normal number: no denormal, -0, infinity or NaN.

<!-- The table below is generated from protocol/stats.toml by protocol/generate.py: change it there. -->

| Slot | Name in a pack | Size | The game's | From | To | Read by (main.rel .text) | Where it lies | GX Unleashed 5.0.4 |
|---|---|---|---|---|---|---|---|---|
| 0 | BoostEnergyUse | 32 bits | 0.16666667 | 0 | 1.7 | fn_1_26F34 0x27420 only: while the boost's countdown (+0x18B) runs, energy (+0x184) -= this x the usage factor (+0x4DC) every frame | a seat's record +8 (the game's: .rodata+0xAA0) | 0.15 |
| 1, 2 | SpeedCap | 64 bits | 9990 | 1000 | 1.7976931348623157E+308 | fn_1_294D0 0x29570 only: above this speed (216 x momentum / weight, km/h) the momentum is cut to length 46.25, a near stop | a seat's record +0x10 (the game's: .rodata+0xB20) | the largest double: no cap |
| 3 | DashPlateBase | 32 bits | 0 | 0 | 60 | the patched site in fn_1_26F34 0x271C4 only (`port/image/sites_dash.py`) | a seat's record +0 | 30 |
| 4 | DashPlateScale | 32 bits | 30 | 0 | 45 | the same site | a seat's record +4 | 15 |
| 5 | BoostLengthBase | 32 bits | 0 | 0 | 60 | the boost length's site in fn_1_26F34 0x272D0 only (`sites_dash.py`): an energy boost lasts int(Base + Scale x D) frames, where the game multiplies its 60.0 (.rodata+0xA98, read eight times) | a seat's record +0xc | the game's |
| 6 | BoostLengthScale | 32 bits | 60 | 0 | 60 | the same site | a seat's record +0x18 | the game's |
| 7 | SettingMiddle | 32 bits | 0.5 | 0 | 1 | fn_1_22B10 0x22B1C (its load, the game's .rodata+0x85C): d = setting/100 - this: the setting where nothing changes | a seat's record +0x1c | the game's |
| 8 | DriftFastFloor | 32 bits | 1 | -19 | 19 | fn_1_22B10 0x22B30 (its load, the game's .rodata+0x854): speed side (d > 0): the drift acceleration D changes only above this | a seat's record +0x20 | the game's |
| 9 | AccelFastTarget | 32 bits | 0 | -7 | 7 | fn_1_22B10 0x22B34 (its copy, the game's .rodata+0x84C): speed side: A += AccelGain x (-d) x abs(this - A) | a seat's record +0x24 | the game's |
| 10 | SpeedFastRef | 32 bits | 1 | 0 | 4 | fn_1_22B10 0x22B3C (after its compare, the game's .rodata+0x854): speed side: the max speed's change is d x abs(this - M) x ... | a seat's record +0x28 | the game's |
| 11 | DriftFastLoss | 32 bits | 1.8 | -10 | 10 | fn_1_22B10 0x22B48 (its load, the game's .rodata+0x8BC): speed side: D -= this x D x d | a seat's record +0x2c | the game's |
| 12 | SpeedSlowRef | 32 bits | 0 | 0 | 4 | fn_1_22B10 0x22B60 (its copy, the game's .rodata+0x84C): acceleration side (d <= 0): the max speed's change is d x abs(this - M) x ... | a seat's record +0x30 | the game's |
| 13 | DriftSlowEdge1 | 32 bits | 1 | -19 | 19 | fn_1_22B10 0x22B64 (its load, the game's .rodata+0x854): acceleration side: band 1 is D below this | a seat's record +0x34 | the game's |
| 14 | AccelSlowTarget | 32 bits | 1.4 | -7 | 7 | fn_1_22B10 0x22B68 (its load, the game's .rodata+0x8C0): acceleration side: A += AccelGain x (-d) x abs(this - A) | a seat's record +0x38 | the game's |
| 15 | DriftSlowTarget1 | 32 bits | 2 | -19 | 19 | fn_1_22B10 0x22B74 (its load, the game's .rodata+0x8B8): band 1: D -= DriftSlowGain1 x (this - D) x d | a seat's record +0x3c | the game's |
| 16 | DriftSlowGain1 | 32 bits | 2 | -10 | 10 | fn_1_22B10 0x22B80 (before its use, the game's .rodata+0x8B8): band 1's gain | a seat's record +0x40 | the game's |
| 17 | DriftSlowEdge2 | 32 bits | 1.5 | -19 | 19 | fn_1_22B10 0x22B90 (its load, the game's .rodata+0x8C4): band 2 is D below this (from DriftSlowEdge1); band 3's origin | a seat's record +0x44 | the game's |
| 18 | DriftSlowGain2 | 32 bits | 1.2 | -10 | 10 | fn_1_22B10 0x22BA0 (its load, the game's .rodata+0x8C8): band 2: D -= this x D x d | a seat's record +0x48 | the game's |
| 19 | DriftSlowGain3 | 32 bits | 1.2 | -10 | 10 | fn_1_22B10 0x22BB8 (its load, the game's .rodata+0x8C8): band 3: D -= (this - (D - DriftSlowEdge2)) x D x d | a seat's record +0x4c | the game's |
| 20 | DriftCap | 32 bits | 2.3 | -19 | 19 | fn_1_22B10 0x22BD4 (its load, the game's .rodata+0x8CC): acceleration side and middle: D is at most this | a seat's record +0x50 | the game's |
| 21 | BoostKeepAccel | 32 bits | 0.5 | -7 | 7 | fn_1_22B10 0x22BF4 (its load, the game's .rodata+0x85C): the boost strength is left alone when d < 0, A >= this and M <= BoostKeepSpeed | a seat's record +0x54 | the game's |
| 22 | BoostKeepSpeed | 32 bits | 0.2 | 0 | 4 | fn_1_22B10 0x22C08 (its load, the game's .rodata+0x8A8): the same, the max speed's bound | a seat's record +0x58 | the game's |
| 23 | SpeedFastAccelEdge1 | 32 bits | 0.4 | -7 | 7 | fn_1_22B10 0x22C40 (its load, the game's .rodata+0x8D0): speed side: A below this takes SpeedFastGainLow | a seat's record +0x5c | the game's |
| 24 | SpeedFastGainMid | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22C44 (its load, the game's .rodata+0x854): speed side: the gain between the two edges | a seat's record +0x60 | the game's |
| 25 | SpeedFastGainLow | 32 bits | 3.2 | -10 | 10 | fn_1_22B10 0x22C50 (its load, the game's .rodata+0x8D4): speed side: the gain below SpeedFastAccelEdge1 | a seat's record +0x64 | the game's |
| 26 | SpeedFastAccelEdge2 | 32 bits | 0.5 | -7 | 7 | fn_1_22B10 0x22C58 (its load, the game's .rodata+0x85C): speed side: A at or above this takes one of the two high gains | a seat's record +0x68 | the game's |
| 27 | SpeedFastSpeedEdge | 32 bits | 0.15 | 0 | 4 | fn_1_22B10 0x22C68 (its load, the game's .rodata+0x8D8): speed side, high A: M below this takes SpeedFastGainHighSlow | a seat's record +0x6c | the game's |
| 28 | SpeedFastGainHighSlow | 32 bits | 0 | -10 | 10 | fn_1_22B10 0x22C74 (its copy, the game's .rodata+0x84C): speed side: the gain with A high and M low | a seat's record +0x70 | the game's |
| 29 | SpeedFastGainHigh | 32 bits | -0.25 | -10 | 10 | fn_1_22B10 0x22C7C (its load, the game's .rodata+0x8DC): speed side: the gain with A high and M not low | a seat's record +0x74 | the game's |
| 30 | SpeedFastScale | 32 bits | 0.16 | -10 | 10 | fn_1_22B10 0x22C80 (its load, the game's .rodata+0x8E0): speed side: M += d x abs(SpeedFastRef - M) x this x gain | a seat's record +0x78 | the game's |
| 31 | SpeedSlowOffset | 32 bits | 0.12 | 0 | 4 | fn_1_22B10 0x22C90 (its load, the game's .rodata+0x8E4): acceleration side: t = (M - this) / SpeedSlowSpan | a seat's record +0x7c | the game's |
| 32 | SpeedSlowSpan | 32 bits | 0.08 | 0.01 | 4 | fn_1_22B10 0x22C94 (its load, the game's .rodata+0x8E8): the same, the divisor | a seat's record +0x80 | the game's |
| 33 | SpeedSlowClamp | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22C9C (its load, the game's .rodata+0x854): t is at most this | a seat's record +0x84 | the game's |
| 34 | SpeedSlowSlope | 32 bits | 0.2 | -10 | 10 | fn_1_22B10 0x22CB0 (its load, the game's .rodata+0x8A8): acceleration side: M += d x abs(M) x SpeedSlowScale x (SpeedSlowBase + this x t) | a seat's record +0x88 | the game's |
| 35 | SpeedSlowBase | 32 bits | 0.4 | -10 | 10 | fn_1_22B10 0x22CB4 (its load, the game's .rodata+0x8D0): the same, the base | a seat's record +0x8c | the game's |
| 36 | SpeedSlowScale | 32 bits | 0.45 | -10 | 10 | fn_1_22B10 0x22CBC (its load, the game's .rodata+0x8EC): the same, the scale | a seat's record +0x90 | the game's |
| 37 | AccelSlowHighEdge | 32 bits | 0.6 | -7 | 7 | fn_1_22B10 0x22CD0 (its load, the game's .rodata+0x878): acceleration side: A above this takes the high branch | a seat's record +0x94 | the game's |
| 38 | AccelSlowHighTarget | 32 bits | 0.7 | -7 | 7 | fn_1_22B10 0x22CE8 (its load, the game's .rodata+0x8F0): high branch: A += AccelSlowHighGain x d x abs(this - A) | a seat's record +0x98 | the game's |
| 39 | AccelSlowHighGain | 32 bits | 2 | -10 | 10 | fn_1_22B10 0x22CEC (its load, the game's .rodata+0x8B8): the same, the gain | a seat's record +0x9c | the game's |
| 40, 41 | AccelGain | 64 bits | 0.6000000238418579 | -10 | 10 | fn_1_22B10 0x22D14 (its load (lfd), the game's .rodata+0x8F8): otherwise: A += this x (-d) x abs(target - A) (a double) | a seat's record +0xf0 | the game's |
| 42 | TurnDecelAccelEdge | 32 bits | 0.4 | -7 | 7 | fn_1_22B10 0x22D3C (its load, the game's .rodata+0x8D0): the turn deceleration T and the low-acceleration factors change only when the new A is below this | a seat's record +0xa0 | the game's |
| 43 | TurnDecelLowAccelEdge | 32 bits | 0.31 | -7 | 7 | fn_1_22B10 0x22D48 (its load, the game's .rodata+0x900): A below this: k = TurnDecelLowAccelFactor, the max speed's change x SpeedLowAccelFactor | a seat's record +0xa4 | the game's |
| 44 | TurnDecelBase | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22D4C (its load, the game's .rodata+0x854): k's start | a seat's record +0xa8 | the game's |
| 45 | SpeedLowAccelFactor | 32 bits | 1.5 | -10 | 10 | fn_1_22B10 0x22D58 (its load, the game's .rodata+0x8C4): A below TurnDecelLowAccelEdge: the max speed's change x this | a seat's record +0xac | the game's |
| 46 | TurnDecelLowAccelFactor | 32 bits | 1.5 | -10 | 10 | fn_1_22B10 0x22D5C (its copy, the game's .rodata+0x8C4): the same: k = this | a seat's record +0xb0 | the game's |
| 47 | TurnDecelHighEdge | 32 bits | 0.03 | -0.7 | 0.7 | fn_1_22B10 0x22D68 (its load, the game's .rodata+0x904): T above this: k x TurnDecelHighFactor | a seat's record +0xb4 | the game's |
| 48 | TurnDecelHighFactor | 32 bits | 1.5 | -10 | 10 | fn_1_22B10 0x22D74 (its load, the game's .rodata+0x8C4): the same, the factor | a seat's record +0xb8 | the game's |
| 49 | TurnDecelSlowFactor | 32 bits | 2 | -10 | 10 | fn_1_22B10 0x22D88 (its load, the game's .rodata+0x8B8): d < 0: k x this | a seat's record +0xbc | the game's |
| 50 | TurnDecelGain | 32 bits | 0.7 | -10 | 10 | fn_1_22B10 0x22D94 (its load, the game's .rodata+0x8F0): T -= abs(this x k x T x d) | a seat's record +0xc0 | the game's |
| 51 | TurnDecelFloor | 32 bits | 0.01 | -0.7 | 0.7 | fn_1_22B10 0x22D9C (its load, the game's .rodata+0x908): T is at least this | a seat's record +0xc4 | the game's |
| 52 | LightWeight | 32 bits | 700 | 0 | 25000 | fn_1_22B10 0x22DCC (its load, the game's .rodata+0x90C): a machine lighter than this has its acceleration capped | a seat's record +0xc8 | the game's |
| 53 | LightAccelCap | 32 bits | 0.7 | -7 | 7 | fn_1_22B10 0x22DDC (its load, the game's .rodata+0x8F0): the cap | a seat's record +0xcc | the game's |
| 54 | TurnMoveFastLoss | 32 bits | 0.6 | -10 | 10 | fn_1_22B10 0x22E04 (its load, the game's .rodata+0x878): speed side: turn movement x (TurnMoveFastBase - this x d) | a seat's record +0xd0 | the game's |
| 55 | TurnMoveFastBase | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22E08 (its load, the game's .rodata+0x854): the same, the base | a seat's record +0xd4 | the game's |
| 56 | TurnMoveSlowGain | 32 bits | 0.2 | -10 | 10 | fn_1_22B10 0x22E24 (its load, the game's .rodata+0x8A8): acceleration side: turn movement x (TurnMoveSlowBase - this x d) | a seat's record +0xd8 | the game's |
| 57 | TurnMoveSlowBase | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22E28 (its load, the game's .rodata+0x854): the same, the base | a seat's record +0xdc | the game's |
| 58 | GripGain | 32 bits | 0.25 | -10 | 10 | fn_1_22B10 0x22E40 (its load, the game's .rodata+0x8A0): the two grips x (GripBase + this x d) | a seat's record +0xe0 | the game's |
| 59 | GripBase | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22E48 (its load, the game's .rodata+0x854): the same, the base | a seat's record +0xe4 | the game's |
| 60 | BoostGain | 32 bits | 0.1 | -10 | 10 | fn_1_22B10 0x22E70 (its load, the game's .rodata+0x858): boost strength x (BoostBase + this x d), unless kept | a seat's record +0xe8 | the game's |
| 61 | BoostBase | 32 bits | 1 | -10 | 10 | fn_1_22B10 0x22E7C (before its use, the game's .rodata+0x854): the same, the base | a seat's record +0xec | the game's |

<!-- The end of the generated table. -->

- **How the readers were found**: every load of the game's code whose address is the word, through any pool
  register (a script over the decomp's assembly that follows `lis`/`addi` of each `.rodata` label; the survey's
  reading agrees). Each word has one reader. A word another routine also reads is not allowed unless every reader is
  meant to change: the dash plate's 60.0 and 0.5 are read a dozen times in fn_1_26F34 alone.
- **The dash plate as data.** One patched word of the native module: a dash plate's boost lasts int(A + B x D)
  frames, D the machine's boost duration, A and B the two floats of the machine's seat (`fmuls`, then the double `fadd` GX
  Unleashed's own code uses). The stock A = 0, B = 30 give the game's bits: 0.5 x single(60 x D) = single(30 x D)
  exactly for every D the stat ranges allow (halving is exact, D is 0 or normal, below 4.26), and 0.0 + x = x. The
  most of A and B keep the count inside its byte: 60 + 45 x 4.26 < 256. The fork's image (the JIT reference) has no
  such site: its races are the game's own formula.
- **The speed cap**: "none" in the launcher is the largest double, where the branch never runs.
- **The energy boost's length** (0.4.0): int(BoostLengthBase + BoostLengthScale x D) frames, the dash plate's shape at
  the boost's own multiply (fn_1_26F34 0x272D0); the stock 0 and 60 give the game's bits (single(60 x D) + 0.0).
  The count lives in two bytes (+0x18A, +0x18B): a pack that holds either constant is refused when Base + Scale x the
  longest boost duration its car data gives (a machine's, or a custom machine's three parts summed) is 256 frames or
  more (`StatPackConstants.BoostLengthProblem`; the game's go, `const_pack.cpp` `BoostLengthRefusal`, from the disc's
  durations read before the race's pack is laid). A pack without them races int(60 x D) as every pack before them,
  and is not judged.
- **The machine setting's curve** (0.4.0, slots 7 to 61): fn_1_22B10 (block, s), run in every step on the block
  rebuilt from the car data, s = setting x 0.01, d = s - SettingMiddle. It changes eight stats: acceleration A
  (+0x08), max speed M (+0x0C), the two grips (+0x10, +0x14), drift acceleration D (+0x1C), turn movement (+0x20),
  turn deceleration T (+0x3C) and boost strength (+0x34; left alone when d < 0, A >= BoostKeepAccel and M <=
  BoostKeepSpeed). Every number it reads is a slot of its role, but the six 0.0s it tests d against (they say which
  side a setting is on). The compiler shared pool words between roles (2.0 is the drift band 1's target and gain, the
  high acceleration's gain and a turn deceleration factor), so a slot is a role, not a word: a pool word is never
  written, and its other readers keep it (1.0 has 59 others, 1.8 two in fn_1_290E8). There is no 2.7 word: band 3 is
  (DriftSlowGain3 - (D - DriftSlowEdge2)). The drift cap 2.3 holds on the acceleration side and at the middle only.
  Its readers were found as the others' (every load of main.rel's code whose address is the word). The setting's
  other callers (the Grand Prix's stat check, which no race of ours installs; the menu's preview) run the game's own
  curve.

## The pack file and the wire

- A line `constant NAME VALUE`: the name from the table, a decimal number read as the nearest number of the
  constant's size (32 or 64 bits) and written as the shortest text that reads back to the same bits. The writer puts
  the constants after the car data, under a comment. `fzstats 1` as before: a launcher from before constants refuses
  such a file (no kind `constant`).
- As it travels: an entry of 6 bytes as every value, block 240 (`CONST_BLOCK`), the offset byte the slot, the 32
  bits. A 64-bit number is two entries (high word, then low), both or neither. The constants come after the car
  data's entries (they are in order of block), so the fingerprint is the same formula over both and a pack without
  constants keeps its fingerprint. A game from before constants refuses such a pack (block 240 is no car data
  block), and so does a launcher (`StatPack.FromWire`): no program plays it wrongly.
- **The replay's version.** A race with a pack is version 3 whether or not the pack holds constants (a custom
  machine 4, mines that come back 5, as before); a race with a pack a seat is version 6 ("A pack a seat", below), and
  7 or more is a format no program of this build plays. A program from before constants refuses a version 3
  recording whose pack holds them (its pack is refused, as above), and every recording made before plays as it was
  raced: in a race without constants every seat's record holds the game's own numbers.

## In the game

- **The curve's sites** (`port/image/sites_curve.py`): the game's function stays; a site at each instruction that
  brings a number into its register (47 loads, 4 copies of a pool number, and 3 places where a register's number
  takes a second role: 54 sites) reads the seat's record when the word SEAT_SETTING names one, and is the replaced
  word itself when it is 0. The two calls in a machine's step (fn_1_331EC 0x336B8, 0x336F4, the racer in r27) write
  the seat's record there for the call's length and 0 after it. A site uses r12 and cr7 only (r0 and cr0 are live
  in the function). `port/image/tests/test_curve_sites.py` runs the disc's fn_1_22B10 itself, patched and not, for
  every setting and 172 made-up machines: the game's numbers give the game's bits, and DriftFastLoss 1.0 gives F-Zero
  GX+'s own code's bits.
- `port/core/src/const_pack.cpp`. Every constant lies in a seat's record of the seats' block (`table::SEAT_BLOCK`,
  "A pack a seat" below): the core writes every seat's record before the boot (native form) and every frame until the
  race runs, with the pack's bits or the game's own. main.rel's own words (`.rodata` 0xAA0, 0xB20) are never written:
  the patched sites read the seat's record in a race of ours, and a plain start reads main.rel's words as ever. The
  block is the data window's: no rollback puts it back, and no step writes it.
- **At the go** a race with a pack starts only if every seat's record holds the constants its pack gives and every
  seat's row is on that should be; otherwise the stat pack's refusal, as for the car data. A race without a pack is
  judged on nothing here (its records hold the game's numbers from the boot on).

## The import

`Launcher.App --import-stat-pack CARDATA --main-module LINE__ --pack-name NAME [--pack-author WHO]`: the car data's
differences as before, and the modified `enemy_line/line__.bin` decrypted and unpacked as `MachineStatFile` does,
compared with the disc's own (the pinned disc's main module), keeping only the allowed words. The dash plate's
formula rewritten to int(A + B x D) by two pool loads and an `fadd` (GX Unleashed's shape) is read back into
DashPlateBase and DashPlateScale. A load of the machine setting pointed at another word of its pool (F-Zero GX+'s
`lfs f4,0x7C(r4)` to `lfs f4,0xE8(r4)`), or a changed pool word only the setting reads (.rodata 0x8C0, 0x8CC, 0x8D4,
0x8DC, 0x8E0, 0x8E8, 0x8EC, 0x900, 0x904, 0x90C), is read back into the slots of that load's roles; the energy
boost's 60.0 load pointed at another word, into BoostLengthScale (`StatPackImport.Curve.cs`). A changed word other
routines read too, a load made another instruction, and a change to a test of the setting's side are left out, in a
line each. Every other difference is counted by where it is (code, constants, data, header and relocations) and left
out, in a line each.

## A pack a seat

From 0.3.0 on (mixed packs in lobbies only; Quick Play stays one pack):

- **The format.** A match start may name packs beside its own (`setup.packs`, at most 4, each as `setup.stats` is)
  and a seat its choice (`seats[].pack`: 0 the disc's own stats and constants, k the k-th of `setup.packs`; absent,
  the race's `setup.stats`). A recording of such a race is replay version 6, whatever else it holds: a program from
  before would race every seat on the race's pack. One pack for all writes neither member: the match start, the
  replay's version and every byte of the race are those of before.
- **The seats' block** (`table::SEAT_BLOCK`, 0x8185C000, after the input buffer: 0x2000 bytes in 0.3.0, the data
  window then ending at 0x8185E000; 0x4000 bytes since 0.4.0, ending at 0x81860000, where the host calls begin). A
  byte a seat (its row is on); at +0x20 the word SEAT_SETTING (the curve's seat while the setting runs, else 0); a
  car data row a seat (0xB4 bytes from +0x300); a record a seat of 0x100 bytes from +0x2000 (+0 DashPlateBase, +4
  DashPlateScale, +8 BoostEnergyUse, +12 BoostLengthBase, +16 SpeedCap as a double, +24 BoostLengthScale, +28 the
  curve's numbers in slot order, its one double at +0xF0). The core writes the records before the boot
  (`const_pack.cpp` `SeatBlock`); every seat gets its choice's constants, or the race's pack's, or the game's own.
- **What reads it** (`port/image/sites_seats.py`, `sites_dash.py`, `sites_curve.py`, `sites_custom.py`): every reader of
  the constants runs in a machine's own step (the machine in r30, its racer index at +4, which is its seat), so every
  constant on the list is per seat; none is global. A stock machine whose seat chose a pack of its own takes its
  stats from its row at racer creation (fn_1_83070 0x8324C) and in every step (custom_step's stock path, in place of
  fn_1_82EDC). The row is the machine's block as the loader left it, with the chosen pack's values for it over it,
  made by the core on the first frame the table stands, before the race's pack is laid over the shared table.
- **What is not per seat.** A custom machine's stats are the sum of its parts in the shared table (fn_1_14F6F8 reads
  the table at sixteen places): it races the race's pack's parts, and only its constants follow its seat's choice. A
  per-seat sum needs those sixteen places routed through a table of the seat's own: the next step, not built.
- **The server.** The rule `mixed-packs` (off by default, Quick Play's): on, the server offers every pack of its
  image's folder (1 to 4), names them in the lobby's state (`seatPacks`) and each racer's own beside them
  (`players[].pack`); a machine choice may name one by its fingerprint (`pack`), or "" for the original stats; a pack
  it does not offer is the lobby's. The packs are the server's own files, read and checked as `--stat-pack` is.
- **The launcher.** In a lobby that offers packs, the machine stage has a picker under Ready: the lobby's pack, the
  original stats, or one the server offers; the racers' list writes each racer's own after their machine. The
  Garage's stats stay the lobby's pack's (a Garage view of the chosen pack is not built).

## Launcher and server

- The lobby's pack label and the Garage's head name the pack as before; the constants are named in the pack's
  detail (the label's tip), e.g. "energy a boost costs a frame 0.15 · speed cap none · dash plate 30 + 15 x boost".
  The Garage's stats are the car data's.
- The server's dashboard and status line say how many constants a pack holds; the vote is unchanged.

## Proved

Run on game build image cdd9732b (bundle `dcf33f5d`), module constpacks `dc2b5396` (pgo-v022: 859
functions without their profile, the image's routines having moved), runner `93321afd` (at cd3aaf04), one PC:

- **GX Unleashed in full against the JIT reference** (`pack-reference.sh`, one of the project's test scripts, not public; boosting from the start):
  the reference ran the emulator's own compiler with the fork's bundle in a game folder holding GX Unleashed 5.0.4's
  own `line__.bin` and `cardata.lz` (its own code, dash plate included); the native build ran the pack over the
  disc's files. Racers' hashes equal race frame by race frame: course 16, 1,884 of 1,884; course 1, 1,965 of 1,965.
  The control, the native build on the car data alone (0.2.x's pack), differs from race frame 19 (the player's
  machine) and 253 (the racers): the constants are what makes the race the mod's.
- **Two games on the pack under forced rollbacks** (`pair-run.sh`, a rollback every 7 frames to depths 3, 9 and 17,
  30 ms of lag, 28 simulated players): courses 16 and 1, checksums equal at every step (3,005 and 3,114 steps), all
  four playbacks 0 differing. Then, in the same data folders, a race on 0.2.x's pack file (car data only): equal,
  both playbacks 0 differing (a pack switched between two races; an old pack file loads).
- **The replay set** (copy 035's 16 rows, as the race, headless): 0 steps differing in every row, the six forced
  playbacks among them: one pack for all, and no pack, are the game as before.
- **A mixed race** (course 1, rollbacks forced as above): the race's pack GX Unleashed, game 1's seat on the disc's
  own (pack 0), seats 2 and 3 on a made-up pack (pack 1), game 2's seat a custom machine on GX Unleashed's parts:
  checksums equal at every step (3,006), version 6 recordings, both playbacks 0 differing. The machines' state before
  step 300 holds each seat's own: seat 8 (Blue Falcon, disc) weighs 1,260 beside seat 9 (Blue Falcon, the race's
  pack) at 1,620; seat 3 (machine 9, the made-up pack, which leaves it as the disc has it) 1,390 where GX Unleashed
  has 1,720; the custom machine 2,240, the sum of GX Unleashed's parts.
- Not shown: two PCs, the online service, a pack with constants near their bounds, speeds above 9,990 km/h.

Run for the machine setting's curve and the boost length (0.4.0), on image 86b8e4e6, module
curve040b `f87d7ae0` (lanes2, pgo-v030: 0 profile mismatches), runner `79e7a4db` (at a267887a), one PC, headless:

- **The game's own function, patched and not** (`port/image/tests/test_curve_sites.py`, with the work folder's
  main.rel): fn_1_22B10 of the disc run for every setting 0 to 100 and 172 made-up machines (each drift band, the
  light machines, every acceleration region) with the patch list and the game's numbers in the record, with no
  seat named, and unpatched: the same bytes of the block in every case. With DriftFastLoss 1.0 it gives F-Zero GX+'s
  own main.rel's fn_1_22B10 bit for bit (every fifth setting, 25 machines), and only the speed side's drift changes.
- **The replay set** (`cv-*`, 18 rows: the seventeen of review 040, mixed packs and custom machines among them, and
  review 040's own image's pair race): 0 steps differing in every row, the six forced playbacks among them.
- **F-Zero GX+ under forced rollbacks** (`pair-run.sh`, `cv-pair`): two games on the re-imported pack, both on Super
  Piranha, one at setting 0 and one at 100 (the curve's two ends), 28 simulated players, rollbacks every 7 frames to
  depths 3, 9 and 17, 30 ms of lag: checksums equal at every step (2,879), both playbacks 0 differing, straight and
  with inputs two steps late. `stat-pack-run.sh` `real` (`cv-spr`, the same two settings, 22 computer racers): PASS
  (version 7 recordings with the pack in their header, 0 differing); `refused`: PASS.
- **Against the JIT reference: not shown.** `pack-reference.sh` differs from step 1 or 2 for GX Unleashed and F-Zero
  GX+ alike (`cv-gxu`, `cv-gxp`, `cv-gxp1`), and so it does for GX Unleashed on review 040's launcher from before
  these slots (`cv-gxu40`): a match race of 0.4.0 has the start grid, no rubber band and mines that come back, which
  only the native form's patch list has; the fork's bundle (the JIT reference) races the game's grid and the rubber
  band. The row needs a reference of 0.4.0's rules before it can prove a pack again. What stands for it here: the
  function test above (the mod's own code, bit for bit), and the control: the native race on the pack of before
  (no DriftFastLoss) against the one with it, every seat's setting spread from 0 to 100 (`SPREAD=1`): the racers
  differ from step 1, and seat 0 (setting 0, the acceleration side, which DriftFastLoss does not touch) only from
  step 214, when the others' different drifts reach it.

