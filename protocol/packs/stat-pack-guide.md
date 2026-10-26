# Writing a stat pack by hand

A guide for modders who know F-Zero GX's car data and main module but not this project's code. The engineers'
reference, with every reader in the game's code, is [`handbook/stat-pack-constants.md`](../../handbook/stat-pack-constants.md)
(the constants) and [`handbook/stat-packs.md`](../../handbook/stat-packs.md) (the car data's words and why each range
is what it is). Where this guide and the handbook differ, the handbook is right and this guide is wrong.

## What a stat pack is

A stat pack is the part of a mod that changes how machines drive, written as a short text file (`NAME.fzstats`). A
server hands it to every game of a race, and each game lays it over the disc's own data before the race starts.
Every player's game then races the same numbers, so a pack can't desync a race.

A pack holds **only what it changes**. Anything it leaves out is the game's own value: a pack of one line changes one
number. Two kinds of lines are allowed:

- **car data values**: a machine's or a custom part's stat, as `cardata.lz` holds them;
- **constants**: numbers of the main module's code (`enemy_line/line__.bin`) that a mod may change. The game's
  build has a fixed list of these, below. A pack can't change any other code.

A pack is named by its **fingerprint**, a hash of its values. Two files with the same values are the same pack, and
any changed value makes another pack. The `name` and `author` lines and the comments aren't part of it.

## The file

```
fzstats 1
name My Pack
author Someone
# comments start with #
machine 6 Weight 2520
body 0 Grip2 -0.5
constant DriftFastLoss 1
```

- Line 1 is `fzstats 1`. Line 2 is `name` and 1 to 40 plain characters. Line 3 may be `author` and 1 to 40
  characters.
- A car data line is `KIND NUMBER STAT VALUE`. KIND is `machine` (0 to 40, by the game's machine id), `body`,
  `cockpit` or `booster` (0 to 24 each). The values are decimal numbers, written as the game stores them
  (32-bit floats). `Bytes48` takes two whole numbers.
- A constant line is `constant NAME VALUE`.
- Each value may appear once. Values stand in the order of the car data, then the constants in slot order. The
  launcher's import writes them that way, and a reader refuses a file with a value twice.
- A value must be 0 or a normal number. Denormals, -0, infinities and NaN are refused.

## The car data stats

These apply to a machine's block. A custom part's value only has to be within the stat's size either side of 0,
because a custom machine is the sum of its three parts. "Tried" is what someone has raced: a value outside it is
allowed, but the import and the server warn about it.

| Stat | From | To | Tried | Why the floor |
|---|---|---|---|---|
| Weight | 1 | 25000 | all | a divisor of the physics |
| Acceleration | -7 | 7 | all | |
| MaxSpeed | 0 | 4 | all | a divisor that stays positive only from 0 up |
| Grip1 | -45 | 45 | 0 to 9 | |
| Grip3 | -3.5 | 3.5 | 0 to 0.7 | |
| TurnTension | -2.2 | 2.2 | 0 to 0.45 | |
| DriftAcceleration | -19 | 19 | 0 to 4 | |
| TurnMovement | -2000 | 2000 | 0 to 400 | |
| StrafeTurn | -1000 | 1000 | 0 to 200 | |
| Strafe | -500 | 500 | 0 to 100 | |
| TurnReaction | -300 | 300 | 0 to 60 | |
| Grip2 | -10 | 10 | 0 to 2 | |
| BoostStrength | 0.1 | 220 | all | a divisor while boosting |
| BoostDuration | 0 | below 4.26 | all | the boost's frame count lives in one byte |
| TurnDeceleration | -0.7 | 0.7 | all | |
| Drag | -0.15 | 0.15 | all | |
| Body | -13 | 13 | all | |
| Bytes48 | 0 to 255, then 0 to 3 | | all | two bytes |
| CameraReorienting | -19 | 19 | all | |
| CameraRepositioning | -15 | 15 | all | |
| Hover0X .. Hover3Z | -25 | 25 | all | a machine's Z must be at least 0.01 from 0 (a divisor) |
| Wall0X .. Wall3Z | -25 | 25 | all | |

## The constants

Each constant is optional, and one you leave out is the game's own value. Each one applies to every machine in the
race. (In a lobby with packs per seat, each racer gets the constants of the pack they chose.)

### Boost energy and speed cap

| Slot | The game's | Range | What it does |
|---|---|---|---|
| BoostEnergyUse | 0.16666667 | 0 to 1.7 | Energy a boost uses each frame (times the machine's usage factor). |
| SpeedCap | 9990 | 1000 to the largest number | Above this speed (km/h) the game cuts the machine's momentum to nearly nothing. `1.7976931348623157E+308` means no cap. A 64-bit number. |

### Dash plate

A dash plate's boost lasts `int(DashPlateBase + DashPlateScale x D)` frames, where D is the machine's
BoostDuration.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| DashPlateBase | 0 | 0 to 60 | Frames every dash plate boost gets. |
| DashPlateScale | 30 | 0 to 45 | Frames per second of BoostDuration. |

### Boost length

An energy boost (the one you trigger) lasts `int(BoostLengthBase + BoostLengthScale x D)` frames, where D is the
machine's BoostDuration. The game's own formula is `int(60 x D)`.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| BoostLengthBase | 0 | 0 to 60 | Frames every boost gets. |
| BoostLengthScale | 60 | 0 to 60 | Frames per second of BoostDuration. |

The count is kept in one byte. A pack that sets either slot is **refused** if any machine could get a boost longer
than 255 frames. The test uses the longest BoostDuration in the pack's own car data: the longest of the 41 machines,
or the longest body, cockpit and booster added together (a custom machine).

### The machine setting

The machine setting slider (0 = full acceleration, 100 = full max speed) changes eight of a machine's stats in every
step. The game rebuilds the stats from the car data and puts them through one function (fn_1_22B10), so the slider
never builds up over time. Every number of that function is a slot here. Write

- `s` = setting / 100, and `d = s - SettingMiddle` (so d runs from -0.5 to 0.5 with the game's middle);
- "speed side" = `d > 0`; "acceleration side" = `d <= 0` (the middle setting included);
- A, M, D, T for the machine's acceleration, max speed, drift acceleration and turn deceleration.

The formulas below are the game's, in its own order. The function works in 32-bit floats; one step (the
acceleration gain) works in a double. The game's own numbers give exactly the game's results.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| SettingMiddle | 0.5 | 0 to 1 | d = setting/100 - this: the setting where nothing changes. |

**Drift acceleration (D).**

- Speed side: if `D > DriftFastFloor`, then `D -= DriftFastLoss x D x d`. There's no cap on this side.
- Acceleration side (d is 0 or negative, so these raise D):
  - if `D < DriftSlowEdge1`: `D -= DriftSlowGain1 x (DriftSlowTarget1 - D) x d`
  - else if `D < DriftSlowEdge2`: `D -= DriftSlowGain2 x D x d`
  - else: `D -= (DriftSlowGain3 - (D - DriftSlowEdge2)) x D x d`; with the game's numbers, `(2.7 - D)`
  - then D is at most DriftCap. (This is the "2.3 cap", and it applies on this side only.)

| Slot | The game's | Range | What it does |
|---|---|---|---|
| DriftFastFloor | 1 | -19 to 19 | Speed side (d > 0): the drift acceleration D changes only above this. |
| DriftFastLoss | 1.8 | -10 to 10 | Speed side: D -= this x D x d. |
| DriftSlowEdge1 | 1 | -19 to 19 | Acceleration side: band 1 is D below this. |
| DriftSlowTarget1 | 2 | -19 to 19 | Band 1: D -= DriftSlowGain1 x (this - D) x d. |
| DriftSlowGain1 | 2 | -10 to 10 | Band 1's gain. |
| DriftSlowEdge2 | 1.5 | -19 to 19 | Band 2 is D below this (from DriftSlowEdge1); band 3's origin. |
| DriftSlowGain2 | 1.2 | -10 to 10 | Band 2: D -= this x D x d. |
| DriftSlowGain3 | 1.2 | -10 to 10 | Band 3: D -= (this - (D - DriftSlowEdge2)) x D x d. |
| DriftCap | 2.3 | -19 to 19 | Acceleration side and middle: D is at most this. |

**Max speed (M).** The change is worked out first and added after the acceleration and the turn deceleration
(see below):

- Speed side: `change = d x abs(SpeedFastRef - M) x SpeedFastScale x gain`. The gain is SpeedFastGainLow if
  `A < SpeedFastAccelEdge1`. If `A >= SpeedFastAccelEdge2`, it is SpeedFastGainHighSlow when
  `M < SpeedFastSpeedEdge`, else SpeedFastGainHigh. Otherwise it is SpeedFastGainMid.
- Acceleration side: `t = min((M - SpeedSlowOffset) / SpeedSlowSpan, SpeedSlowClamp)`, then
  `change = d x abs(SpeedSlowRef - M) x SpeedSlowScale x (SpeedSlowBase + SpeedSlowSlope x t)`.
- If the new A is below TurnDecelAccelEdge and also below TurnDecelLowAccelEdge: `change x= SpeedLowAccelFactor`.
- `M += change`.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| SpeedFastRef | 1 | 0 to 4 | Speed side: the max speed's change is d x abs(this - M) x .. |
| SpeedFastAccelEdge1 | 0.4 | -7 to 7 | Speed side: A below this takes SpeedFastGainLow. |
| SpeedFastAccelEdge2 | 0.5 | -7 to 7 | Speed side: A at or above this takes one of the two high gains. |
| SpeedFastSpeedEdge | 0.15 | 0 to 4 | Speed side, high A: M below this takes SpeedFastGainHighSlow. |
| SpeedFastGainLow | 3.2 | -10 to 10 | Speed side: the gain below SpeedFastAccelEdge1. |
| SpeedFastGainMid | 1 | -10 to 10 | Speed side: the gain between the two edges. |
| SpeedFastGainHigh | -0.25 | -10 to 10 | Speed side: the gain with A high and M not low. |
| SpeedFastGainHighSlow | 0 | -10 to 10 | Speed side: the gain with A high and M low. |
| SpeedFastScale | 0.16 | -10 to 10 | Speed side: M += d x abs(SpeedFastRef - M) x this x gain. |
| SpeedSlowRef | 0 | 0 to 4 | Acceleration side (d <= 0): the max speed's change is d x abs(this - M) x .. |
| SpeedSlowOffset | 0.12 | 0 to 4 | Acceleration side: t = (M - this) / SpeedSlowSpan. |
| SpeedSlowSpan | 0.08 | 0.01 to 4 | The same, the divisor. |
| SpeedSlowClamp | 1 | -10 to 10 | T is at most this. |
| SpeedSlowBase | 0.4 | -10 to 10 | The same, the base. |
| SpeedSlowSlope | 0.2 | -10 to 10 | Acceleration side: M += d x abs(M) x SpeedSlowScale x (SpeedSlowBase + this x t). |
| SpeedSlowScale | 0.45 | -10 to 10 | The same, the scale. |
| SpeedLowAccelFactor | 1.5 | -10 to 10 | A below TurnDecelLowAccelEdge: the max speed's change x this. |

**Acceleration (A).**

- Acceleration side with `A > AccelSlowHighEdge`: `A += AccelSlowHighGain x d x abs(AccelSlowHighTarget - A)`.
- Otherwise: `A += AccelGain x (-d) x abs(target - A)`. The target is AccelFastTarget on the speed side and
  AccelSlowTarget on the acceleration side. AccelGain is a 64-bit number.
- Later, after the turn deceleration: if the machine's weight is below LightWeight and A is above LightAccelCap,
  A becomes LightAccelCap.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| AccelFastTarget | 0 | -7 to 7 | Speed side: A += AccelGain x (-d) x abs(this - A). |
| AccelSlowTarget | 1.4 | -7 to 7 | Acceleration side: A += AccelGain x (-d) x abs(this - A). |
| AccelGain | 0.6000000238418579 | -10 to 10 | Otherwise: A += this x (-d) x abs(target - A) (a double). |
| AccelSlowHighEdge | 0.6 | -7 to 7 | Acceleration side: A above this takes the high branch. |
| AccelSlowHighTarget | 0.7 | -7 to 7 | High branch: A += AccelSlowHighGain x d x abs(this - A). |
| AccelSlowHighGain | 2 | -10 to 10 | The same, the gain. |
| LightWeight | 700 | 0 to 25000 | A machine lighter than this has its acceleration capped. |
| LightAccelCap | 0.7 | -7 to 7 | The cap. |

**Turn deceleration (T).** This changes only when the new A is below TurnDecelAccelEdge:

- `k = TurnDecelBase`; if `A < TurnDecelLowAccelEdge`, `k = TurnDecelLowAccelFactor`
- if `T > TurnDecelHighEdge`: `k x= TurnDecelHighFactor`
- on the acceleration side (`d < 0`): `k x= TurnDecelSlowFactor`
- `T -= abs(TurnDecelGain x k x T x d)`, then T is at least TurnDecelFloor.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| TurnDecelAccelEdge | 0.4 | -7 to 7 | The turn deceleration T and the low-acceleration factors change only when the new A is below this. |
| TurnDecelLowAccelEdge | 0.31 | -7 to 7 | A below this: k = TurnDecelLowAccelFactor, the max speed's change x SpeedLowAccelFactor. |
| TurnDecelBase | 1 | -10 to 10 | K's start. |
| TurnDecelLowAccelFactor | 1.5 | -10 to 10 | The same: k = this. |
| TurnDecelHighEdge | 0.03 | -0.7 to 0.7 | T above this: k x TurnDecelHighFactor. |
| TurnDecelHighFactor | 1.5 | -10 to 10 | The same, the factor. |
| TurnDecelSlowFactor | 2 | -10 to 10 | D < 0: k x this. |
| TurnDecelGain | 0.7 | -10 to 10 | T -= abs(this x k x T x d). |
| TurnDecelFloor | 0.01 | -0.7 to 0.7 | T is at least this. |

**Turn movement.** On the speed side it is multiplied by `(TurnMoveFastBase - TurnMoveFastLoss x d)`, on the
acceleration side by `(TurnMoveSlowBase - TurnMoveSlowGain x d)`.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| TurnMoveFastBase | 1 | -10 to 10 | The same, the base. |
| TurnMoveFastLoss | 0.6 | -10 to 10 | Speed side: turn movement x (TurnMoveFastBase - this x d). |
| TurnMoveSlowBase | 1 | -10 to 10 | The same, the base. |
| TurnMoveSlowGain | 0.2 | -10 to 10 | Acceleration side: turn movement x (TurnMoveSlowBase - this x d). |

**The two grips** (Grip1 and Grip3) are both multiplied by `(GripBase + GripGain x d)`.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| GripBase | 1 | -10 to 10 | The same, the base. |
| GripGain | 0.25 | -10 to 10 | The two grips x (GripBase + this x d). |

**Boost strength** is multiplied by `(BoostBase + BoostGain x d)`. The exception is a machine on the acceleration
side (`d < 0`) with `A >= BoostKeepAccel` and `M <= BoostKeepSpeed`, whose boost strength is left alone.

| Slot | The game's | Range | What it does |
|---|---|---|---|
| BoostKeepAccel | 0.5 | -7 to 7 | The boost strength is left alone when d < 0, A >= this and M <= BoostKeepSpeed. |
| BoostKeepSpeed | 0.2 | 0 to 4 | The same, the max speed's bound. |
| BoostBase | 1 | -10 to 10 | The same, the base. |
| BoostGain | 0.1 | -10 to 10 | Boost strength x (BoostBase + this x d), unless kept. |

The setting's six tests of `d` against 0 (which side a setting is on) are not slots. The setting's curve in the
game's menus (the machine setting screen's preview) is always the game's own.

## Making a pack from a mod: the import

The launcher makes a pack from a mod's files, compared with your unchanged disc image:

```
Launcher.App --import-stat-pack cardata.lz [--main-module line__.bin] --disc "F-Zero GX (USA).iso" \
    --pack-name "My Pack" [--pack-author "Me"] [--out my-pack.fzstats]
```

- **`cardata.lz`**: every word that differs from the disc's becomes a car data line. Changed names aren't carried,
  and the import says so.
- **`line__.bin`** (optional; the encrypted main module, as on the disc): what the import carries:
  - BoostEnergyUse and SpeedCap, when their words are changed;
  - the dash plate's formula rewritten as `int(A + B x D)` in GX Unleashed's way: DashPlateBase and DashPlateScale;
  - a machine setting load pointed at another word of its pool (for example F-Zero GX+'s `lfs f4,0x7C(r4)` to
    `lfs f4,0xE8(r4)`, which turns DriftFastLoss 1.8 into 1.0), or a changed pool word that only the setting reads:
    the slot (or slots) of that load;
  - the energy boost's `60.0` load pointed at another word: BoostLengthScale.
- **What it leaves out**, one line each in its report: a changed pool word that other code reads too (changing
  it would change that code as well), a setting load turned into another instruction, a change to one of the
  setting's side tests, and every other changed word of the main module, counted by where it is (code, constants,
  data, header). A code word is named by its place (`.text+0x...`). A race on the pack runs the disc's code there.
- It writes the file, reads it back, prints the fingerprint, and warns about values outside "Tried". It refuses a
  pack that breaks a rule (a range, the boost length's 255 frames).

Example report (F-Zero GX+):

```
stat pack F-Zero GX+: 2062 values differ from the disc's, in 41 machines and 75 custom parts
the main module: 3 of the game's code constants differ from the disc's
  its dash plate's code is int(A + B x boost duration), A = 30, B = 15: carried as DashPlateBase and DashPlateScale
  its machine setting's DriftFastLoss is 1 (the game's 1.8): carried
  783 other words of its data (.data): not on the list of allowed words, left out
fingerprint: 783e8adf67b256e92500a095ceea1c6ce9c76b55
```

## Example: a pack of two constants

A pack that keeps every machine as the disc has it, removes the drift cap on the acceleration side, and makes energy
boosts a little longer:

```
fzstats 1
name Long Drift
author Me
# no car data lines: every machine is the disc's
constant BoostLengthBase 10
constant DriftCap 19
```

Every other constant is the game's own. BoostLengthBase is checked against the 255-frame byte. The disc's longest
BoostDuration is 2.0 s (a machine's; on the disc only boosters carry one, the longest 1.9 s), so the longest boost
here is 10 + 60 x 2 = 130 frames, well inside it. If the import or the server refuses the file, its message names
the line and the rule.

## Where packs live

A server runs a pack with `--stat-pack FILE`. The packs that ship with the project are in this folder, each one
included with its author's permission (`README.md`).
