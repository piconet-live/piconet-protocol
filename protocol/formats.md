# Formats: the replay file, the launch file, the bundle, the controller helper's lines, the save's options, a race's summary line, the log's stop and freeze lines, the status board

Three kinds of file pass between the launcher and the game. This page is written from the code that reads and
writes them; where the page and the code differ, the code is right and the page is corrected in the same commit.
Every binary number is big-endian. Every JSON text is UTF-8. Keys a reader does not know are ignored in a replay's
header, a bundle's files and the coordinator's match start; in the launch file they are refused.

Paths under `port/` are piconet-runner's and `net/src/Launcher.*` piconet-launcher's; the service's (`Online.Server`)
are not public, nor are the project's test scripts named here (`launch-file.py`, `replay-compare.py`,
`gate-judge.py`, `freeze-run.sh`, `freeze-report.py`, `taskbar-run.sh` and the others).

| File | Written by | Read by |
|---|---|---|
| Replay, `*.fzrp` | the game: `boot::StartReplay`, `net::Recorder` (`port/core/src/replay.cpp`) | the game: `boot::ReadReplay`, `net::ReadRecording` (`files_replay.cpp`); the launcher: `Files/ReplayFile.cs` |
| Launch file | the launcher, for every game it starts (`net/src/Launcher.Core/Files/GameLaunchFile.cs`; its `match` part: `MatchFile.Build`; its `debug` part: what the launcher's own launch file hands on); the test scripts (`launch-file.py`) | the game: `ReadLaunchFile` (`files_launch.cpp`), then `boot::ReadMatch` (`files_match.cpp`) for its race |
| Race summaries, `race-summary.txt` (section 6) | the game, one line when it stops after a race: `net::WriteSummary` (`port/core/src/summary.cpp`, `files_summary.cpp`) | a person, or a tool that reads section 6's format; the game reads it only as lines, to keep the last 300 |
| Bundle (a folder) | `port/image/build.py` (native form); `bundle.py` beside the image's generators (the JIT reference's) | the game: `boot::TheBundle`, `ReadPatches`, `ReadCodes` (`files_bundle.cpp`); the launcher: `RecompLaunch.cs` |

## 1. The replay file (version 2; version 3 for a race with a stat pack; version 4 for a race with a custom machine; version 5 for a race whose mines come back; version 6 for a race with a pack a seat; version 7 for a race of 0.4.0's set-up: the coordinator's start grid, or no rubber band; version 8, the compact file a recording becomes when it ends)

A replay holds everything that rebuilds its race: the race's set-up, every player seat's input for every step, and
the checksum the recording game kept for each step. CPU racers are not recorded; they are computed again.

**The definition is `protocol/replay.toml`**: its parts, marks, versions and the rule that picks
a race's version, the lineup's block, the step, the results' entries and the header's members. `protocol/generate.py`
writes every reader and the writer from it: `generated/replay_file.g.h` (the game's core: `Parse`, the writer's
`EncodeStart`, `EncodeLineup`, `EncodeStep`, `EncodeResults`, `ResultsJson`), `generated/replay_compact.g.h` (the
game's core: the compact file's `Compact`, `Expand`, `ChecksumMatches`, through liblzma), `generated/ReplayFile.g.cs`
(the launcher: `ReplayParts`, `ReplayFormat`; a compact file's header through SharpCompress) and
`generated/replay_file.py` (the test tools' `replay-compare.py` and reports; a compact file through the standard
library's `lzma`).
No reader is written by hand; what follows is the schema in prose, and what each reader refuses (its own policy over
the generated parts). `tests/vectors/replays/` holds a real recording of every version 2 to 7 (cut to 240 steps; as hex text, `NAME.fzrp.hex`), `v8.fzrp.hex` (the version 7 one made compact) and
`replays.tsv`, every part of each as the readers before the schema read it: the game's core test and the protocol's
C# test hold the generated readers to it, and the core test writes each sample again, byte for byte. The runner's
`--replay-info FILE` prints what a file holds as one JSON object, starting no game.

| Part | Size | Content |
|---|---|---|
| Mark | 4 | `FZRP` |
| Version | u16 | (8: the compact file, below) 2; 3 when the race ran on a stat pack (the header's `match.setup.stats`, section 2); 4 when a seat raced a custom machine (a seat's `custom`, section 2), with a stat pack or without (the pack is then read as in version 3); 5 when the race's mines came back (its set-up's `rules.mineRegenMs` gives a step or more, section 2), with a pack or a custom machine or neither; 6 when a seat raced a pack of its own (`setup.packs`, a seat's `pack`), whatever else it holds; 7 when the race started on the coordinator's grid (`setup.grid`, section 2), whatever else it holds. Nothing else of the file differs |
| Header length | u32 | n |
| Header | n | JSON (below) |
| Frames mark | 4 + u32 + u32 | `FZRF`, the player seats' mask (bit i is seat i), the recorder's seat |
| Lineup | 4 + u32 + 124 | `FZRL`, the length (124), the block below |
| Steps | per step, 6 x players + 4 | each player seat's 6-byte control record, in seat order, then the step's checksum (u32) |
| Results | m + u32 + 4, optional | JSON, its length m, `FZRS`. It is the file's last bytes and is found from the end |

**Header.** `match`: the match start as the coordinator sent it (section 2), without `relay` and `token`. `seat`:
the recorder's seat. `mask`: the player seats. `delay`: the input delay in frames. `flow`: 1 for a race of the
game's flow (a machine is out by the game's own verdict, the race waits for the player seats alone:
`port/image/routines/outtest.py`, `raceend.py`; race block +304), absent in every recording made before it, which
then plays with the masks it was raced with; another value is refused. `rubberBand`: false for a race or practice
from 0.4.0 on, whose CPU drivers race at their own pace (race block +100, `port/image/sites_cpu.py`); absent (or
true) in every recording made before it, which then plays with the game's rubber band, as it was raced; a value that
is not true or false is refused. `mod_sha1`, `mod_commit`: the
bundle's image and the commit it was built from. `game`: the disc's ID, `GFZE01`. `engine`, `module_sha1`: what ran
the game, when the runner says. `recorded`: the UTC time, `2026-10-06T12:00:00+00:00`. `practice`: `true` for a practice run (Play's Practice: the
player alone, the run kept as one unbroken line of inputs from the race's first step, with no trace of its rewinds
and loads; `port/core/include/fzgx/practice.h`, `src/practice_session.cpp`); absent in a race. The launcher lists
such a replay under Practice.

**Lineup** (read from the game when the relay says go; the retired replay mode booted from it, and a playback as the
race takes its machines into the race block):

| Bytes | Content |
|---|---|
| 0-29 | each seat's machine (0 to 40; 0x32 for a seat with a custom machine; 0 in the slots after the race's racers) |
| 30-59 | each seat's starting rank |
| 60-119 | each seat's machine setting, s16 |
| 120 | machine 0's control mode (its block, +0x590) |
| 121 | the byte at `.bss`+0xD70 of main.rel |
| 122-123 | padding, zero |

**Results**: `{"results":[{"seat","rank","finished","out","laps","timeMs","bestLapMs"}]}`, one entry for each racer;
`timeMs` is null for a racer that did not finish. Read once, in the first frame after the race settled that finds the
game out of the race phase (every step before it has run on official inputs; the log says before which step), and
written when the recording closes; a recording closed before that holds none. In a race with the rule
`finishLimitSeconds` (section 2) they are read earlier, before the step the race ended for everyone at (the log's
"the race is over before step N"): a racer still racing there has `finished` false, no time, and the `rank` it
held at that step; the game's list and the launcher's say "Disqualified".

**Version 7.** A race of 0.4.0's set-up, one that started on the coordinator's grid (its header's match names `setup.grid`) or raced with no rubber band (its header's `rubberBand` false: every race and practice from 0.4.0), is written as version 7 whatever else it holds (a stat pack, a custom machine, mines that come back, a pack a seat), so that a program from before the grid refuses it and does not start the race from the game's own grid; the grid is the header's match start's as it came. A race with neither (a recording made before 0.4.0) kept its version, and plays the game's own sort and the band. Since every race and practice from 0.4.0 has no rubber band, the game writes version 7 alone (replay.toml's `written`); versions 2 to 6 are read, never written. A version 7 file with neither, or another version's with either, is refused (both readers of the game; the launcher lists version 7). Its lineup's starting ranks (bytes 30-59) are the grid as the game placed it: a seat's rank at go is its slot.

**Version 8, the compact file.** When a recording ends (the session's end: a race, a practice) the game makes the
file compact: it writes the compact file beside it (`NAME.fzrp.compact`) and moves it over the recording, so the file is
at every moment one or the other, and a game that stops on the way leaves the recording of the steps written so far
(`CompactReplay`, `port/core/src/replay.cpp`). A file that is not a whole recording (no step, no lineup, a part of a
step at its end) stays as it is. On the 135 replays of one player's folder (295.9 KiB on average) a compact file is
31.8 KiB (10.8%): the header 5.3 KiB, the inputs 17.3, the checksums 8.6, the results 0.45.

| Part | Size | Content |
|---|---|---|
| Mark | 4 | `FZRP` |
| Version | u16 | 8 |
| Recording's version | u16 | the version of the recording it was made of, 2 to 7 (the game makes it of version 7): the features' rules above are that version's |
| Header | 4 + u32 + n | `FZRH`, n, the header's JSON as one xz stream |
| Frames | 4 + u32 + 8 | `FZRF`, 8, the player seats' mask, the recorder's seat |
| Lineup | 4 + u32 + 124 | `FZRL`, 124, the block (the lineup's part above, byte for byte) |
| Inputs | 4 + u32 + 4 + n | `FZRI`, its length, u32 the steps, then the player seats' records in columns as one xz stream: of each step's row (the seats' records in seat order) every step's first byte, then every step's second byte, and so on |
| Checksums | 4 + u32 + n | `FZRC`, n, one xz stream of: a byte for every step, its checksum folded (`c ^ c >> 8 ^ c >> 16 ^ c >> 24`, the low 8 bits: every bit of the word counts), then the whole checksum (u32) of every step that is a multiple of 60 and of the last step, in the steps' order |
| Results | m + u32 + 4, optional | as above: JSON, its length m, `FZRS`, the file's last bytes, found from the end (plain: 0.45 KiB, xz would save 0.25 KiB a file, and a reader takes them as in every version) |

The parts come in this order and fill the file up to the results; each xz stream is one stream, LZMA2 at preset 9 with
the extreme flag and a dictionary of its part's own size (4 KiB at least), checked by its CRC32. A reader takes no
more than 128 MiB of memory for one, no header longer than 1 MiB and exactly the inputs' and checksums' lengths the
steps make. A reader reads the file as the recording it was made of (`Expand`), whose steps between the whole
checksums hold their folded byte; a playback compares each step's byte, and the whole checksum where the file keeps
it (`ChecksumMatches`). The first step that differs is still found: a game that differed stays different (nothing is
repaired), so a difference the byte cannot see (1 in 256) shows a step or a few later, and the next whole checksum is
at most 60 steps on. A program from before version 8 refuses the file by its version (0.5.2's replays need 0.5.2 or
later); every program from 0.5.2 reads versions 2 to 8.

**Version 5.** A race whose mines came back is written as version 5 so that a program from before the rule refuses it and does not play the race with its mines gone; the rule is the header's match start's as it came. A race without it keeps its version, byte for byte as before. A version 5 file without the rule, or another version's with it, is refused (both readers of the game, and the launcher's list says so).

**Version 3.** A race with a stat pack is written as version 3 so that a program from before stat packs (which
knows only version 2) refuses the file and does not play the race on the disc's own car data. The pack is in the
header as it came, and a playback lays it through the same reader as a race. A race without a pack is version 2,
byte for byte as before.

**Writing** (version 7 while the race runs, then version 8). With the launch file's `match.record` the game writes the header and the frames mark when the session starts,
appends the lineup at go, appends every step once it is final (it has run with the relay's official inputs), and
appends the results at the end. A file the game closed always ends on a whole step. Then it makes the file compact
(version 8, above).

**Reading. What each reader refuses:**

| Reader | Refuses |
|---|---|
| `boot::ReadReplay` (sets up the race of a playback) | a file over 256 MiB; more than 1,296,000 steps (six hours); no `FZRP` or a version other than 2 to 8; a version 8 file that does not expand (a part missing, out of its place or of another length, an xz stream that does not read or check, a recording's version other than 2 to 7, a whole checksum whose byte disagrees), and then what follows of the recording it was made of; a version 7 file whose set-up names no start grid and keeps the rubber band, or another version's with a grid or without the band; a version 5 file whose set-up names no mine rule or another version's that names one; a version 3 file whose set-up names no stat pack, a version 2 file whose set-up names one, a version 4 file whose set-up names no custom machine or another version's that names one, a stat pack the game refuses (section 2); no `FZRF`; no `FZRL` or fewer than 122 bytes after it; a header that is not a JSON object, or one nested more than 32 levels deep; a `game` other than `GFZE01` (a header without `game` is accepted); a frames mark whose mask has no seat or a seat above 29, or whose recorder's seat is above 29; no step, or a part of a step, between the lineup and the results; a header without `match`, its `setup` or its `seats`; a set-up the game does not have (section 2: seats, course, laps, seeds, a seat's machine); a header's `seat` (0-29), `delay` (0-60), `mask` (seats 0-29 only) or `flow` (0-1) that is not a whole number in its range; a lineup machine above 40 in any of the 30 slots, the race's racers and the slots after them (0 in every recording written: all 30 go into the race block), but 0x32 in the slot of a seat whose set-up names a custom machine |
| `net::ReadRecording` (the steps a playback runs and compares) | a file over 256 MiB; more than 1,296,000 steps (six hours); no `FZRP` version 2 to 8; a version 8 file that does not expand; no `FZRF`; a mask with no seat or a seat above 29; a recorder's seat above 29. The lineup and the results are skipped when present, and a part of a step at the end is not read |
| `ReplayFile.Read`, `ReplayFile.Problem` (the launcher's list, and why a file is not played) | a version other than 2 to 8 (a version 8 file is judged by its recording's version, and one of a recording's version other than 2 to 7 is another version's), a version 8 file whose parts are not whole or whose header does not read, another game, a version and a mine rule that disagree, a file that ends early, each as a sentence for the player; a header or results block longer than 1 MiB; a header or results whose members are missing, of another kind or out of a number's range ("not a replay", never an exception in the window). A missing lineup is not refused here |
| `RecompLaunch.RecordedWithThisImage` (the launcher only; the game does not check) | a `mod_sha1` that is neither the bundle's image nor named for it in `same-race.json` (section 3). A replay that names no image is played |

- A header's `mask` replaces the mask worked out from the seats. Seats are 0 to 29: the game's two readers refuse a
  mask with bit 30 or 31 set; `replay-compare.py` counts all 32 bits.
- Refused means refused: a number outside its range is never bent into it (the game's set-up routines check
  nothing, and a race that is played must be the race the file names).
- A set-up without `rules` is a recording from before the host's rules: its seats' settings default to 100, not 50.
- In a race of fewer than 30, the ranks of the absent racers are 0 in the file; only the race's own racers are
  placed on the grid.
- The results are the file's last bytes only when their JSON lies after the frames' part (replay.toml); the launcher
  reads no header or results longer than 1 MiB (`max_json`).
- A playback compares every step's checksum with the file's (a version 8 file's: above); a difference counts as a
  desync. It also compares the race's results with the file's, where a recording takes them (`CheckResults`,
  `port/core/src/playback.cpp`), and says `results: the recording's` or `results: DIFFER ...` (and in its summary:
  `playback: results the recording's|DIFFER from the recording's|not in the recording|not reached`); the gates'
  judge fails a playback whose results differ. A recording of a version before 7 (before 0.4.0) is not judged by
  its results: some took them from a state that was not the race's end (one of 4 October holds every lap 0 and
  times of hours); a difference there reads `results: other than the recording's` and `results other, not judged`.
- Files are named `yyyyMMdd-HHmmss-MATCHID.fzrp` (`RecompLaunch.ReplayFileFor`).

**Comparing recordings**: `replay-compare.py [--same-length] A.fzrp B.fzrp [...]` reads files only and
says whether they are one race. One line a file: `FOLDER/FILE: seen from seat S, N player seats, M steps, course C,
L lap(s), engine E, results yes|no` (`M steps (cut inside a step)` for a file that ends in a part of a step,
`M steps, compact` for a version 8 file). Where either file of a pair keeps a step's folded byte alone (version 8),
the bytes are compared (the reader's `Checksum`): a compact file compares with a whole one or another compact one. One
line a pair: `files I and J: N steps in both; inputs equal|differ first at step T; checksums equal at every
step|differ first at step T (K steps differ); the same lineup|ANOTHER lineup`, then `, the same results|, OTHER
results` when both files hold results, then `; LENGTHS DIFFER: A and B steps (file I is K short)` when they do.
Files with other player seats: `other player seats: not one race`. Exit status 0 when every pair has equal inputs,
checksums and lineup over the steps both hold: a recording cut short is equal to a whole one that way, so with
`--same-length` unequal lengths fail too. Whether each game ran its whole race is `gate-judge.py`'s to say.

## 2. The launch file

**The definition is `protocol/launch.toml`**: every member, its type, range and values, and what
the launcher writes. `protocol/generate.py` writes the launcher's writer (`generated/LaunchFile.g.cs`) and the game's
reading of every member of a plain kind (`generated/launch_file.g.h`); the rules between members are the reader's
(`files_launch.cpp`). The tables below say what each member does. `protocol/tests/vectors/launch/` holds the files
the launcher writes, which the game's tests read.

JSON, one object: **every setting of one start of the game**. The runner is started with `--config FILE` and takes
nothing else of ours but `--fzgx-log FILE` (where its own output goes). Plain JSON: no comments, nothing after the
object, at most 1 MiB. The reader is `ReadLaunchFile` (`port/core/src/files_launch.cpp`); it fills the one settings
struct, `fzgx::Options` (`port/core/include/fzgx/core.h`), and nothing else fills it. Neither the core nor the
adapter reads an environment variable, and in a start with a launch file the patched emulator reads none for a
test setting of its own either (below, "The emulator's side"). A file or a folder the launch file names is UTF-8 text, as the core opens
it; the path given to `--config` arrives in the system's code page, as the runner's own options do, and the adapter
turns it into UTF-8 (a letter the code page does not have is lost before the runner sees it: the fork's matter).

| Key | Content | When absent |
|---|---|---|
| `about` | a line that says what this start is for | refused |
| `bundle` | the bundle's folder (section 3) | refused with `match` or `replay`; a plain start of the fork's kind needs none |
| `widescreen` | `true` or `false`: the game's own 16:9 mode | `true` |
| `trueWidescreen` | `true` or `false`: in a race or a playback, true widescreen: the 16:9 view keeps the whole vertical view of the game's Normal and widens the sides (85.6 degrees across at the base fovy against the game's own 76.2), where the game's own Wide Screen cuts the vertical view by 0.866. For the picture only: the core writes the image's word `TRUE_ASPECT` (the aspect's float bits, 16/9; 0 off) every frame, read by two sites (`port/image/sites_view.py`). Refused with `widescreen` `false`. The launcher writes it from its "Widescreen" setting (True) | `false` |
| `windowName` | text, at most 64 bytes: the name in the game window's strip (the launcher's own; the runner has none compiled in) | the strip shows no name |
| `windowNamePicture` | `{"width": 1-512, "height": 1-64, "rgba": TEXT}`: the name as a picture, drawn in place of its text: the pixels row by row from the top, four bytes each (red, green, blue, opacity), as base64. The launcher gives its title bar's lettering, which it makes from the player's disc | the name's text |
| `showFrameRate` | `true` or `false`: the game window's strip shows the frame rate (the player's setting in the launcher). Always `true` in a test: a test build's launcher (a review or try copy) and a launcher started with test settings write it whatever the setting says (`LauncherSettings.FrameRateShown`), and every file `launch-file.py` writes has it unless the named file or a `--set` says otherwise. The figure is the emulator's count of pictures presented (`Core::Callback_FramePresented` on `after_present_event`, `PerformanceMetrics::CountFrame`), so pictures drawn between steps count too | `false` |
| `rumble` | `true` or `false`: the player's controllers may rumble (the launcher's Settings, Game, "Rumble"; it writes the key only as `false`, so a runner from before the key takes its files while the switch is on). `false`: no command that runs a motor reaches a pad in this start. `true`: only while the player drives ("Rumble", below). A player's key: no test setting | `true` |
| `present` | when a picture goes to the screen: `console` (at the time a console puts its first line out), or one of the two ways kept for comparison, which the log calls test settings: `field` (as up to 0.2.0) or `copy` (when the game copies it out). "The picture's moment and the delay", below. Outside `debug`: a launcher writes it from its settings file | `console` |
| `verticalSync` | `true` or `false`: pictures wait for the display's refresh (the player's setting in the launcher). The runner sets the video backend's own setting by it, whatever the user folder's `GFX.ini` says | `false` |
| `refreshRate` | pictures a second in a race or a replay: `60` (the game's own picture a step), `120`, `240`, or `display` (the display's own rate): pictures between two steps, made by the emulator's GPU thread drawing the step's recorded frame again with corrected matrices (`fzgx/hfr.h`, `VideoCommon/FrameRedraw.h`): the world by the chase camera's blend, each machine and what is drawn at its place by its own; the HUD, flat draws and offscreen passes as the step drew them. Nothing of the game runs again. The simulation keeps the console's 59.94 steps a second; the pictures between a step and the next are made at the next one's draw-done, so every true picture is held a field (and 1.5 ms) past its console moment and the ones between at their fractions of a field after it. Pictures are left out, never steps, when the GPU thread's budget is spent. Needs `present` `console` for its timing (other ways show the pictures at once). A player's setting | `60` |
| `fullScreen` | `borderless` or `exclusive`: what the window's full screen is (the player's setting in the launcher, which always writes it). `borderless`: the window without a frame, the size of its monitor, as up to 0.2.0. `exclusive`: on top of that the video backend's own full screen ("The game's window", below) | `borderless` |
| `taskbar` | `{"id": TEXT, "program": PATH, "name": TEXT}`, all three: the game window joins the launcher's button on the Windows taskbar ("The game's window", below). `id`: the launcher's application id, at most 128 letters, digits, `.`, `-`, `_`; `program`: the launcher's program file by its whole path on this computer: a drive letter (no network path, no path from where the game was started), ending in `.exe`, one line of at most 1,024 bytes without a quotation mark, and a file that is there; `name`: what a pinned button is called, one line of at most 64 bytes. A player's key: no test setting | the game window has a taskbar button of its own |
| `board` | the launcher's status board for this start, a file of its own data folder (section 8). A player's key: no test setting | the game tells the launcher nothing but its end (exit code, log) |
| `controllerSetup` | `true`: a plain start that opens the game's own controller options and closes the game when the player leaves them (below). A player's key: no test setting | `false` |
| `lanes` | `0` only: every launcher writes `0` into every launch file. The multicore physics (a step's per-machine updates on several threads) is not in the game: it comes back only rebuilt on the rollback library; a game refuses any other count. A player's key: no test setting | `0` |
| `match` | a race: the table below | no race |
| `replay` | a playback, as the race (the game's replay mode and the key `view` that chose it are gone since 0.3.0): `file` (the replay), `otherSounds`, `scrubber` (`true` or `false`: the replay scrubber, "The replay scrubber" below; on unless it says `false`, and off with `debug.forceRollback` or `debug.lateSteps`, which refuse it named `true`) | no playback |
| `debug` | the test settings: the second table below | **every test setting is off** |

With neither `match` nor `replay` the start is a plain one (offline play, a gate scene). Both together are refused.

**The replay scrubber** (`port/core/src/scrub.cpp`, `scrub_move.cpp`; the runner's `replay_bar.cpp`,
`replay_bar_paint.cpp`, `replay_audio.cpp`). In a playback with `replay.scrubber` on (every player's), in both views:
a bar along the bottom of the game's window (44 px at 96 dpi, GDI+, the launcher's design language): the time at the
picture, a rounded track of the race from its first step to its end (played, buffered and not yet, the leader's
laps as gaps), the play mark (a pill while playing, a knob while it stands, red while a scrub is beyond the buffer),
the frame; under the pointer the track grows and a box gives the time and frame there. A press counts on the track. While the replay plays, the rest of its race is simulated ahead of the picture in the same game
(40 % of a frame's time), so any buffered moment is shown at once: Space pauses and plays, Left and Right jump five
seconds, Home goes to the race's first step, End to the newest step buffered, `.` and `,` step one frame on or back
(paused), `[` and `]` change the racer watched (the race view); the mouse held on the bar scrubs, a click jumps. Keys
come from the game window's key messages only (Left, Right, `.`, `,` repeat while held). Before the race nothing is shown: the panel's `LOADING` stays up, with no speed limit and no sound, through the load, the course intro (cut short) and the countdown, until the race's first step, the bar's first ("The panel before a race", below). The sound is heard only while the replay plays forward
(the stream's volume faded over 100 ms, never above the player's own). The replay ends where its race settles,
paused on its last picture; the game is not stopped (Space there starts again from the race's first step). Every
step at the front edge and every step shown is compared with the recording. The front edge's log is packed (zstd)
and held to `debug.replayMemory`; a step whose log was dropped is run again from a key (one every 128 steps).

**`controllerSetup`** (`port/core/src/options_screen.cpp`; the launcher's Controller page, "Set up in the game").
The start is a normal boot: the logos, then the title's memory card sequence, which loads the player's save and
which the player answers if it asks (no card, no save yet). Once the title stands after it (`MD_TITLE`,
`SMD_TITLE_DEMO`, the byte at 0x801A66B9 set: the title has set the save's defaults, so its exit will not put them
over the loaded save, and nothing else is asked for), the core stores the game's own request for its Options
(`MODE_REQUEST` = `MD_OPTION`), as the main menu's Options entry does. Options' module asks for its first menu
itself when it starts, so the controller menu (`SMD_OPTION_CTRL_MENU`: Calibration, Customize, Controller Rumble)
is asked for one frame later, when that menu stands, as choosing Controller there does. A boot reaches the menu in
about 9 seconds (field 549 in runs ctlb3 and ctlb4: the title is left two fields after it stands, before it loads
its demonstration race). The player's pad is the player's: nothing
is pressed. A screen under the menu saves through the game's own save screen when the player changed something and
confirmed, and the game is back in the menu only when the card is written. B in the menu asks for Options' first
menu: the core then stops the game as closing its window does (`fzgx: controller setup: the player left the
controller options; the game saved N times in the visit; stopping`), and the emulator writes the card's file as it
closes. A visit that changes nothing leaves the save file as it was, byte for byte. Refused with `match` or
`replay`. The watch runs once a picture and acts on the mode the game is in, which stands for a frame or longer;
if the game leaves Options another way, it is stopped too.

**Rumble** (`port/core/src/rumble.cpp`, the runner's `rumble.cpp`). The game asks for rumble
for the racer the camera follows (the image's `seatport.py` gives that racer pad 0, the local controller), so without the cut a
spectator would feel the watched racer's hits. A pad rumbles only while `rumble` is `true` and the player drives a
seat of their own (`net::RumbleWanted`): never in a spectator's start (`match.spectate`) or a playback, nor while the
racer is out or over the line (`Watch` not `Racing`), nor while a game started again catches up with the AI driving
its seat (until the hand-back's step); a plain start (menus, offline play) as the game asks. The cut is the
emulator's one rumble path (recompcore 0048: the host hook `rumble_allowed`, asked in
`CSIDevice_GCController::Rumble` for a standard controller and the adapter alike): a command that would run a motor
goes on as a stop, and when the answer turns to no every port's motor is stopped once. Nothing of the game changes:
its rumble requests are computed as ever (the image's `mute.py` still drops those of a step simulated again), so
exactness is untouched. The pacing log's R records (`debug.paceLog`) are what reached a pad, and the log ends with
`fzgx: rumble: N commands ran a motor, M were held back`. The game's own Controller Rumble option (its controller
menu; "Set up in the game") applies before all this: off there is off, whatever the launcher says; both on, a pad
rumbles while the player drives only.

**`match`**: what the launcher writes for one race (`MatchFile.Build`), with where the race is recorded.

| Key | Content | When absent |
|---|---|---|
| `match` | the coordinator's match start, as it came (below) | refused |
| `relayHost` | the relay's host when `match.relay.host` is empty | the race is refused when both are empty |
| `delay` | input delay in steps: 0, 1 or 2, the player's setting in the launcher; more is refused ("The picture's moment and the delay", below) | 2 |
| `overclock` | the emulated CPU clock's factor, 1 to 16 | 6.0 |
| `otherSounds` | `full`, `quieter` or `off`: other racers' player-only sounds | `quieter` |
| `record` | the file the race is recorded to | not recorded |
| `practice` | `true`: a practice (Play's Practice): the match names one seat, seat 0, a player's, and no relay or token; the race is raced alone with the rewind, each step's row the pad's (`port/core/src/practice_session.cpp`) | a race with a relay |
| `camera` | 1, 2 or 3: the view of the chase camera the local racer's race starts in (the game's camera button, the D-pad's up and down by its default controls, moves between these three; `port/image/routines/raceview.py`): the player's last, which the launcher keeps from the status board's `Camera` events (section 8). The player's own picture: the core writes it, until the relay's go, where the game keeps its saved camera (the options record `[.bss 0x84420]` +8, which the race start's camera set-up reads); no step reads the view and the checksum does not cover the camera, so every game of a race may start in another. Not part of the match start, the recording or its header: a playback starts in the game's own view. Written only when the launcher holds one: a runner from before the key refuses it | the game's own (1) |

**The picture's moment and the delay**. Neither is part of `debug`: a player's launcher sets them, on a server that takes no game with a
`debug` setting. The game's log says both in plain lines (`PlayersSettingsSaid` in `files_launch.cpp`), and names
the two ways of `present` that are kept for comparison as test settings (`fzgx: TEST SETTING: ...`,
`LatencySettingsOn`).

- `present`. The game's loop copies the last pass's picture out right after a field's start. A console puts that
  picture's first line out 58 half-lines (1.84 ms) later (this game's 448-line picture; measured).
  `console`, and a file without the key (every player's game from 0.2.1 on): the picture is held from the copy
  until that line's time on the host's clock, which the emulator works out for every field, and presented then; a
  copy that comes after that time is presented at once, and nothing is held longer than 4 ms
  (`port/core/include/fzgx/present.h`, tested there). `field`, as up to 0.2.0: the emulator hands the copy to the
  screen when its emulated video output reaches that line, which the game's thread, busy with the next pass,
  reaches about 4.5 ms of real time after the field's start. `copy`: presented when the copy is made, 1.8 ms sooner
  than a console. `console` and `copy` are the emulator's own "Immediately Present XFB", set by
  `port/moderngekko/src/pictures.cpp` after the pins, the hold for `console` added there. Nothing the game computes
  or reads changes, and the game's thread waits for real time where it did. A start with `debug.singleCore` draws
  its picture on the game's own thread, where no GPU loop presents a held picture: `console` is `field` there, and
  the log and the summary line say `field`. The launcher writes the key only when its settings file says
  `"present": "field"` or `"copy"`. **No thread waits for the moment** (0.2.3): the picture and its time are kept
  at the copy (`Presenter::SetImmediateSwapHold`, recompcore's patch 0027), and the GPU thread's loop presents it
  once the time has come: between its passes over the game's commands, or from its idle wait, which the time bounds
  (`BlockingLoop::WakeBy`; within 2 ms of the time the loop does not sleep) and new commands end at once. Up to
  0.2.2 the GPU thread waited for the time at the copy, inside such a pass; a test build of 0.2.3 waited after the
  pass. Either way the game's switch of its two command buffers raced with the GPU's lag, which then read garbage
  ("FIFO: Unknown Opcode": every player's picture broke in race m23 of 0.2.2, whose replay hit it every time with
  `console` and never with `field` or `copy`). 0.2.3's first build (0c9170ab) had the launcher ask for `field`
  instead.
- `match.delay`. The delay is the game's own: its input for step `s + delay` is the pad of the frame that runs
  step `s`, and no other game is told. A lower delay puts this game further ahead of the relay's clock
  (`port/core/src/schedule.cpp`, `StepsAhead`) and makes its own rollbacks one step deeper for each frame removed;
  the depth at which a frame still adds a catch-up step grows by as much (`CatchUpOrHold`: 6 steps at 2, 7 at 1, 8
  at 0). The launcher writes the player's setting (`"inputDelay"` in its settings file, the Settings page's "Input
  delay": 0, 1 or 2; anything else reads as 2: `LauncherSettings.RaceInputDelay`), and the game refuses a launch
  file that asks for more than 2, whoever wrote it. A playback takes no delay from a launch file: its recording's
  header names the one it was recorded with (0 to 60 are read; every recording written holds 0, 1 or 2 from here
  on, and older ones 2). A recording's header and a race's summary line name the delay.
- **What a player's files cannot change** (`port/moderngekko/src/pins.cpp`, `PinPresentation`): `Dolphin.ini`'s
  `RushFramePresentation`, `SmoothEarlyPresentation` and `PrecisionFrameTiming`, and `GFX.ini`'s `EarlyXFBOutput`,
  `CapImmediateXFB`, `SkipDuplicateXFBs` and `ImmediateXFBEnable` are the run's own values; `[Hardware] VSync` is
  the launch file's `verticalSync`. A file that says otherwise is named in the log's "not used" line.

**`debug`**: one build serves players and tests; a test setting is on only when this part
names it. The log's first line of ours says which are on (`fzgx: TEST: launch file ... turns on: ...`) or that none is.

| Key | Content |
|---|---|
| `testLagMs` | a round trip added to every packet between this game and the relay, 0 to 10,000 |
| `jitterMs` | each packet held up to this many ms longer, 0 to 10,000 |
| `autopilot` | `true`: the game's AI drives this player's seat |
| `forceRollback` | `{"every": 1-65535, "depths": [1-20, ...]}`: a rollback every so many displayed frames, the depths in turn |
| `drawnFrames` | 1 to 3: pictures between two steps asked for in every pass, at 1/(n+1) apart, whatever the display (the high refresh rate's exactness gate: a recording plays back with 0 steps differing) |
| `redraw` | `{"replays": 1-3, "compareEvery": N, "log": FILE, "dump": PREFIX, "turn": DEGREES, "budgetMs": MS, "at": "copy" or "drawDone"}`: the re-draw spike: every frame recorded and drawn again into a second frame buffer at its copy to the display, nothing shown (VideoCommon/FrameRedraw.h); every N frames the first replay compared with the true frame, pixel by pixel; a CSV line a frame; the first differing pictures as PPM files; "turn" (-90 to 90): the replays' 3D draws seen from a camera turned so many degrees (the projection times one 4x4; the HUD untouched); "budgetMs": a frame's replays stop once the next would pass so much video thread time; "at": "drawDone" replays at the next draw-done, before it is signalled, instead of at the copy (no compares) |
| `lateSteps` | 1 to 20: a playback's inputs become known this many steps late, so its race runs on rollbacks |
| `lateSeats` | with `lateSteps`, only these seats' rows (0 to 29, a list) are known late; the others' when their step runs (one late player among many) |
| `lateNoise` | 1 to 100: a late seat's predicted row has its steering moved by up to this much, so most predictions are wrong |
| `partialRollback` | the per-machine rollback (`port/moderngekko/src/lanes_partial.h`), **on when this key is absent**: a step run again runs only the machines whose input row, records, locked-cache reads or starting floating-point status differ from the step's last run, and racer 0's call (the contact task runs whole); a job that wrote a word of the guard in every run. `"off"`: every step run again runs whole, the CPU core running the task (for gates and comparisons). `"check"`: every step run again also runs whole from the same memory and the two results are compared word for word (main memory, locked cache, data window, block-copy registers, flags, each job's registers and cycles); the whole run's result is kept. `"writes"`: the path on, and every word of main memory a job (a machine's task) writes outside its own machine's records noted (`O` lines, regions at the end): the check that the guard's list is whole. As an object: `{"mode": "off", "check" or "writes", "log": FILE, "traceSteps": [FROM, TO], "widen": BOOL, "watch": "HEX,...", "guard": BOOL}` (`traceSteps`: the steps checked or traced for writes; `widen`, check only: a job whose locked-cache check fails is put back all the same; `guard: false`: no guard, the fault it is there for, for tests). `{"log": FILE}` or `{"guard": false}` alone keep the path on; a log writes a line a batch (the machines chosen and run: a live race's counts). A file asking for the contact jobs (`contact`) or `"trace"` is refused. A test's `machineLanes.lanes` takes the task when on |
| `skipDraws` | 1 to 600: the drawing of every Nth race frame is skipped whatever its time, as a late frame's is (the picture before shown again; `core/src/present.cpp`) |
| `alterStep` | this game's state is altered before that step: a desync on purpose |
| `handbackAt` | a rejoin's hand-back with control back at that step (the core counts `memmap.HANDBACK`, the steps left, down to it): the 3-2-1 on the start's floating monitors shows over the 180 steps before it (flying in from 361 before), GO at it; picture only, so a playback with it plays the same race |
| `clock` | text, 1 to 8 hexadecimal digits: the real-time clock value the game starts from instead of the fixed one |
| `undoLog` | how the undo log finds a first write to a page it does not watch: `"guard"` (the default: write-protected pages), `"scan"` (every page compared before a frame's own step and the one after), `"both"` (the guard checked at every step against every page compared; a test, row `undo`) |
| `keepScratch` | `true`: the locked cache left out of the undo log (the fault of 5.16; the gates' control row) |
| `bufferShare` | 5 to 95: the share of a displayed frame, in percent, the replay scrubber's steps ahead may take while the replay plays (40) |
| `bufferSteps` | 1 to 64: the scrubber's steps ahead, that many every frame whatever the time |
| `replayMemory` | 64 to 65,536: MiB the scrubber may hold beyond the game's own (512); over it the front's packed log and then its keys go, farthest from the play position first |
| `replayNear` | 64 to 65,536: steps either side of the play position whose log and keys the budget never drops (1024) |
| `replayScript` | the scrubber driven by frames of the race: `FRAME:WHAT[:N],...`, `p` pause or play, `j:STEP` jump, `h` the race's first step, `e` the newest step buffered, `s:STEP` the bar pressed there, `f:N` and `k:N` its pointer moved N steps a frame forwards or back, `r` released |
| `replayEndQuits` | `true`: at the replay's end the playback says its summary and the game stops (a check's; a player's stays paused) |
| `hitLog` | a file: a line for every step that left a machine contact or a side attack, and for every frame that showed a hit or a lunge a rollback revealed (`port/core/src/hits.cpp`) |
| `slowSteps` | `"FROM-TO"`: while a frame shows one of these steps the game runs at a hundredth of its speed, so that a harness takes a picture of every frame (before/after picture sequences) |
| `undoTrace` | `{"file": FILE, "heaps": HEX, "check": [FROM, TO]}`: the undo log's own account, a line an event (`port/core/src/undo_trace.cpp`; it reads only). `late`: a page first seen changing after the learning steps, and whether the step that changed it is known (with the guard, the default way, always). `stale`, `words`: a restore put such a page back to an earlier step than the rest of memory (the scan way only). `guard`: when it goes on and off, and before each step it caught a first write for, the pages caught (late, or written again after a rest), the faults, the pages rested, guarded and watched. With `heaps` (the address of the game's OS heap table: 804B56E0 in a race of the released build), `heap`: a heap's cells stopped or started adding up to its size, and before which step. With `check`, `faithful` or `unfaithful` for every restore to a step of that span, against the whole tracked memory kept before each of its steps (19 MiB copied a step: a span of some tens of steps), and the tracked memory written out at a heap's break (`FILE.badN.now.ram` and the last three steps' `.beforeSTEP.ram`: game data) |
| `fullScreenWithoutFocus` | `true`: a window in full screen asks the video backend for its own, exclusive full screen whatever `fullScreen` says and also while another window has the focus (a test's window is shown without it; "The game's window", below) |
| `forcePatches` | `true`: a patched word is written whatever the game holds there (the gate scenes' saved states) |
| `singleCore` | `true`: the picture's work on the emulated CPU's thread (the gates' single-core runs) |
| `rollbackLog` | a file: one line for every displayed race frame |
| `trace`, `traceStates` | a file with a line for every checksum kept: the step, how it ran (`L` a frame's last or only step, `F` its first, `M` one between), the checksum, the game's race clock after it; `[FROM, TO]`: those steps' machine state into `<trace>.states` |
| `standStill` | `{"steps": [N, ...], "tenths": [T, ...]}`, one to four of each, the steps ascending: before the displayed frame that begins at each step the game's thread sleeps for so many tenths of a second (1 to 1,200), once each. A freeze on purpose, for the freeze report (section 7; `freeze-run.sh`); nothing of the game is read or written, so the race is the same race, late |
| `dump` | `{"step": N, "prefix": TEXT}`: whole memory, the locked cache and the data window before and after that step. A game with this key also tells the image to keep its three test logs in the data window (control block +0x3C4); no other game's image writes them |

**The emulator's side of `debug`.** The runner's patched emulator and its runtime have test settings of their own,
which were environment variables. Naming a launch file (`--config`) tells the emulator that this file rules the
start (`RunSettings::RuleByLaunchFile`, `forks/recompcore/2-port`): from then on it asks one place for each of them,
`RunSettings::Get`, which answers only what the adapter handed it from the keys below. A variable of that name in
the environment does nothing. Six keys are the runner's own; without one the runner refuses the option by name
(status 2), leaves the thing unread, or decides it itself:

| Key | Without it | With `true` |
|---|---|---|
| `jit` | the native module runs the game, whatever `MODERNGEKKO_STATICRECOMP` or a settings file says; `--allow-interpreter` is refused | the emulator's own compiler (the gates' reference) |
| `cheats` | cheats are off and the user folder's `GameSettings\GFZE01.ini` is not read (its settings, its Gecko and Action Replay codes, its `[OnFrame]` patches, which the emulator applies whether cheats are on or not). Nothing of the system folder's files for the game (`Sys\GameSettings\GFZ.ini`, `GFZE01.ini`, beside the runner) patches memory either: their `[OnFrame]` patches and their codes are not loaded (their `[Core]` and `[Video_Hacks]` settings are read, and the runner sets those itself: below). The user folder's map file (`Maps\GFZE01.map`), by which a game function can be given the name of one the emulator replaces, is not loaded | the user's file is read, cheats are as the settings say, and both folders' patches, codes and the map file are loaded |
| `mods` | no mod folder is searched (`Mods` beside the runner and in the user folder included); `--mods DIR` is refused | the runner's mod folders are searched |
| `automation` | `--automation-dir DIR` is refused (its commands press buttons, save states, stop the game) | allowed: every scripted run needs it |
| `loadState` | `--load-state FILE` is refused | allowed: the gate scenes |
| `userSettings` | the runner sets every emulator setting that decides what the game computes or how fast it runs, whatever the user folder's `Config\Dolphin.ini` and `GFX.ini` and the system folder's file for the game say (the list is below) | those files' values hold: the harness's user folder (no speed limit, the fixed clock) |

| Key | Content (the variable it was) |
|---|---|
| `pad` | text: `neutral`, or `FIELD:BUTTONS_HEX[:SX:SY[:CX:CY]],...`: port 1 is driven by this script only (`FZGX_PAD`) |
| `poke`, `pokeTask`, `pokeTaskByFrame` | text `ADDR:VALUE[@FROM-TO],...`: bytes written to memory every field, or at every per-machine task call; `true`: the task list's FROM-TO are race frames (`FZGX_POKE8`, `FZGX_POKE8_TASK`, `FZGX_POKE8_TASK_BY`) |
| `timebaseByCharge`, `noHookFlush`, `noBusyWait`, `noGatherInline`, `noIcacheFast`, `keepHostCall`, `noReinterpretReuse` | `true`: one comparison switch of the CPU core, the mod loader or the texture cache (`FZGX_TIMEBASE_BY_CHARGE`, `FZGX_NO_HOOK_FLUSH`, `FZGX_NO_BUSYWAIT`, `FZGX_GATHER_INLINE=0`, `FZGX_ICACHE_FAST=0`, `FZGX_KEEP_HOSTCALL`, `FZGX_REINTERPRET_REUSE=0`) |
| `fallbackRanges` | text `START-END,...` (hexadecimal): code forced onto the fallback JIT; the settings file's own list is not read in a start with a launch file (`STATICRECOMP_FALLBACK_RANGES`) |
| `lockstep`, `lockstepStart`, `lockstepLimit`, `lockstepFilter`, `lockstepNoDedup`, `lockstepMaxReport`, `lockstepStepCap`, `lockstepTrace`, `lockstepRepeat`, `lockstepWhitelist` | the emulator's lockstep verifier (`STATICRECOMP_LOCKSTEP` and its nine) |
| `probe`, `probeNoHash` | a file: a line of hashes and timings per field; `true`: without the hashes (`FZGX_PROBE`, `FZGX_PROBE_HASH=0`) |
| `paceLog` | a file: the pacing log (`FZGX_PACE_LOG`) |
| `taskTimers`, `taskDumps`, `taskList`, `dumpFields`, `dumpRam` | the probe's timers; race frames `N,M,...` whose memory is written to `<probe>.taskN.ram`; the task runner's targets at shutdown; fields `N,M,...` whose main.rel data is written; with it the whole memory (`FZGX_TASKPROF`, `FZGX_TASKDUMP`, `FZGX_TASKLIST`, `FZGX_DUMP_FIELDS`, `FZGX_DUMP_RAM`) |
| `sample`, `sampleTasks`, `samplePhysics`, `census`, `accessTrace` or `accessTraceNoInterrupts` | the profilers (`FZGX_SAMPLE`, `FZGX_SAMPLE_TASKS`, `FZGX_SAMPLE_PHYSICS`, `FZGX_CENSUS`, `FZGX_TRACE=1` or `noirq`) |
| `noSlowLog` or `slowLogEverySecond`, `voiceLog`, `dispatchSamples`, `dispatchTrace` | the slow-second line off or for every second; a file of every voice's parameter block; the CPU core's dispatch samples and trace file (`FZGX_SLOW_LOG=0` or `all`, `FZGX_AXLOG`, `STATICRECOMP_DISPATCH_SAMPLES`, `STATICRECOMP_TRACE_FILE`) |
| `windowNoActivate` | `true`: the window is shown without taking the keyboard (`FZGX_WINDOW_NOACTIVATE`) |
| `windowControls` | `true`: the game window has its controls that change the game: Save State and Load State (the File menu, F1), Pause, fast forward at twice the speed while Space is held (`FZGX_WINDOW_CONTROLS`). With it the window is the system's own, with its title bar and the menu bar (File, View: Fullscreen, Mute Audio). Without it the window has no menu at all: its frame is the runner's strip ("The game's window", below), F1 and Space do nothing, and a command message with one of their numbers is not acted on. Every window has full screen (F11, Alt+Enter; Escape leaves it) and closing (Escape, the close button, Alt+F4) |

**The game's window.** In place of the system's title bar and menu the runner draws a strip in the launcher's look
across the window's top: `windowName` at the left (as `windowNamePicture` when the file
carries one), then four buttons: full screen, minimise,
maximise or restore, close; with `showFrameRate` the frame rate before the buttons, read twice a second. The picture is
a child window under the strip, and in full screen the whole screen with no strip. The look is the launcher's,
value for value (white on black), in one header the strip and every overlay
read, `port/moderngekko/src/overlay.h`: the strip's ground `#0D0E0D` with a raised line `#232623` under it (the
window's border too), the name in the display face (Saira Condensed ExtraBold, leaning 12 degrees), a test game's
title and the frame rate in the secondary tone `#BCC2BC` (the rate's figures on one pitch), a button one tone up
under the pointer and red behind close; the replay bar, its lanes, the observer HUD, the countdown figure and the
course map's rim take the same tones, the one green accent `#3DFF6A` (black text on it) for what is active or
played, gold `#FFD500` as the mark only (the leader), red `#FF4D4D` for danger (out, knocked out, a scrub beyond
the buffer); soft corners (6 to 9 px at 720 lines) on panels, cards, boxes and the bar's handle, square buttons,
flat fills, no outlines, no glow; motion about 120 ms, ease-out. The font files beside the runner in `Fonts` are the
three `fzgx/strip.h` names (ExtraBold, SemiBold, Semi Condensed Regular) with their licence. **Full screen** (F11, Alt+Enter,
the strip's button; Escape leaves it, and a held Escape leaves it and closes nothing) is the window without a frame,
the size of its monitor (`port/moderngekko/src/frame_screen.cpp`). With `"fullScreen": "borderless"`, and in a file
without the key, that is all, as up to 0.2.0. With `"exclusive"` the Vulkan backend is asked for its own, exclusive
full screen on top of it while the game's window has the focus (`screen.cpp`): a display with variable refresh then
follows the game's pictures with the drivers' default settings; a display of a fixed rate tears there with vertical
sync off, which a borderless window never does (hence the setting). Where the driver refuses, the borderless window
stays as it is; the exclusive mode is given back when another window gets the focus and asked for again when the
focus returns; a player's `GFX.ini` with `[Settings] BorderlessFullscreen = True` keeps the borderless window too.
The backend follows the window at a present: while the game hands no picture over, a change waits for the next
one. The Direct3D backends are not asked (their swap chain is the flip-model kind). What is asked when, a refusal
and the giving back are plain state in the core (`port/core/include/fzgx/screen.h`), tested there. **The log says what the swap chain is**, after the
first picture and whenever it changed: `fzgx: screen: backend Vulkan, present mode immediate, exclusive full screen
supported, vertical sync off; the game is in a window` (or `in full screen, the backend's own (exclusive)`, or `in
full screen as a borderless window: WHY`, as `the setting is Borderless`; Direct3D: `flip-model swap chain, tearing-capable`). **The window's place**:
a player's window (no `debug` setting on) opens where it last was, at its last size, maximised or in full screen as
it was; kept in the user folder's `window-place.txt` (one line, `fzgx-window 1 LEFT TOP RIGHT BOTTOM MAXIMIZED
FULLSCREEN`: `port/core/include/fzgx/place.h`) whenever the window was moved, resized, maximised, put back or taken
to full screen, and when it closes. A place whose strip is on no monitor that is connected now is not taken: the
window opens at its usual place. **On a scaled screen** (125 to
200 %) the runner scales itself (`port/moderngekko/src/runner.manifest`: per monitor, version 2), so Windows does not
stretch the window as one picture: the strip's lengths follow the window's dots per inch, the picture fills the
window at the screen's own pixels, and the window's first size, which the settings name in 1/96 inch, is made as many
pixels larger (the same share of the screen as before; never more than the screen's free area: `strip.h`,
`FirstSize`). On a 100 % screen nothing differs. Not scaled: the emulator's own on-screen messages, and the first
size of a `debug.windowControls` window. `taskbar-run.sh` prints what the window says of its scaling; nobody has
looked at a scaled screen yet. A test game (any `debug` setting
on) also shows its window title in the strip, which names the test. The window's title is the runner's `--title`; the frame rate
the runner appends to it every second (" | 59.9 FPS") stays only with `showFrameRate`, since the taskbar and the window
switcher show the title (`strip.h`, `FrameRateInTitle`). The strip's sizes
and colours are the launcher's, copied by hand into `port/core/include/fzgx/strip.h` (a change of the launcher's title
bar is made there too; the layout is tested without a window);
`port/moderngekko/src/frame.cpp` answers the system for the window and `frame_paint.cpp` draws; the emulator's part is
`Common/WindowFrame.h` (`forks/recompcore/2-port`). The type is the launcher's font files in `Fonts\` beside the
runner (`stage-launcher.sh` copies them); without them the system's font. With `debug.windowControls` there is no
strip.

**The game's window on the taskbar** (`port/moderngekko/src/taskbar.cpp`). Windows puts
windows under one taskbar button when they carry one application id. A launcher with a window of its own takes its
id for its process before it makes the window, says it on the window too, and gives it to every game it starts as
`taskbar` (`Launcher.App/Taskbar.cs`; the id is `Branding.AppId`). The runner takes the id for its process before
the game's window is made, and says on the window the id and what a pinned button is: the launcher's program file
(the command and the icon) and its name, so that pinning the button gives the launcher, never the bare runner. It
takes them off when the window goes. A start without `taskbar` keeps a button of its own: the launcher's modes
without a window (`--play-offline` and the others, Quick Play's test door) give none, so every scripted test game
stands apart from a player's launcher. With `debug.windowControls` only the process's id is set (the window's part
needs the strip's hook). `taskbar-run.sh RUN` reads both windows' ids from outside and passes
when they are equal and a pinned button would start the launcher. **It proves what the windows say, the stored
properties Windows groups by, and not what Explorer then shows**: the taskbar itself only a person can look at.
`ODD=1` runs it with the launcher's program renamed, under a folder with a space and a letter outside ASCII.

**The icon.** The runner's program file carries the app's icon (`port/moderngekko/src/runner.rc`): the launcher's
own file, `net/src/Launcher.App/Assets/Icon/app.ico`, which `build.sh` puts beside the sources, so there is one
copy. The game's window takes it from there at the launcher's window's two sizes, 24 and 16 pixels at the screen's
scaling. In the file the sizes of 16, 20 and 24 pixels are the machine with the ring's arc, cut from the artwork
below its lettering, which is unreadable that small; 32 pixels and more are the whole badge (made outside the
repository: `net/src/Launcher.App/Assets/README.md`).

**The panel before a race** (`port/core/include/fzgx/panel.h`, `port/moderngekko/src/panel.cpp`). A
start that goes straight into a race or a replay shows the game's start-up pictures for 20 fields (the logos, the
title's first frame); then the game draws nothing until the course is loaded (8.7 s in runs `wp-base` and
`v021j-d0b`: fields 20 to 543), so the last of them stood on the screen; then the course intro plays (14 s), and on
its last frame, faded to black, the game holds at the start gate and draws nothing until the relay's go. The runner
draws a panel over the picture, black with a line of text in the middle: `LOADING` from the first picture until the
game's race mode is in the course intro's phase (0x27) or a later one and two pictures more (below), `WAITING FOR PLAYERS` at the gate once a
joined game has held there a quarter of a second, until the go. A playback takes its go at once and shows only
`LOADING`; a plain start (offline play, the controller options) shows no panel; a game the relay never answered
holds at the gate with the black picture it had.
- **A replay is held to the race** (a playback with `replay.scrubber` on, every player's: the board's
  `until_race`, set at the start only for a start with a replay file, so never in a live race). `LOADING` stays up
  past the load, and the load's lifted speed limit and sound with it, until the race's first step, which is the
  scrubber's first (`scrub.cpp`, `Fork`: the first frame of the race phase, step 364 in the gate recordings):
  there the panel comes down and the pace and sound are given back, so the first picture is the race's start with
  the bar ready, and the sound gate opens it. Meanwhile, unseen: the course intro, cut short as the game's own skip
  does it (`core.cpp`, `CutTheIntro`: `fn_1_391F0` takes player 1's A or START by putting the intro's countdown,
  main.rel `.bss` 0x26C28, at 30, the closing fade, and only while its loads are done (`fn_1_467F4`), no fade runs
  (`fn_1_4C10`) and `fn_1_EB200` allows it; the core cuts under the same three conditions, facts `INTRO_*`. Cut at
  the intro's first frame instead, while the last loads were queued, two replays as the race crashed in the
  countdown); then the start gate, the go at once, and the countdown's recorded steps, each run and checked as
  ever. The intro is before the recording's first step and reaches no step of it (run
  `rpi-skipA-1`, the intro skipped by a pad's A in a playback: 0 of 4,240 steps differing), and races were
  recorded with and without it skipped. The meter: the load fills its first 600 thousandths, and what runs unseen
  after it a thousandth a field, up to 990 (`Progress::Held`). The game draws again from the intro on: its own
  pictures carry the panel there, and no present is added. Runs `rpj-*` (runner c6474489, module
  v022): recordings of 0.2.0, 0.2.1 and 0.2.2 in both views with the scrubber, 0 steps differing; the intro cut by
  666 fields, the race begins at step 364 as before. Opening a replay to its first picture (efxgat4-pair-1, replay
  view, the PC shared with other test games): 7.45 s from the game's first field before, 7.00 and 5.11 s after;
  the countdown's 364 steps are most of what remains.
- **Drawn** into the emulator's own on-screen frame (the black rectangle and one texture, from the hook before a
  present), so no emulator patch. At the gate the game hands no picture over: the core tells the host on the
  game's thread, which queues a present for the picture's thread and does not wait for it (when the panel comes up,
  and twice a second after). Nothing of the game's memory is read for it but the mode and phase words, nothing is
  written, and after the go a picture pays one load of a flag. The log says `fzgx: panel: down; it was over N
  pictures of the game's, and M presents were made for it at the start gate`.
- **The first picture after the load is still the start-up picture.** The game copies a picture out at the top of
  a pass, before the frame tick that finds the course loaded, and the first copy after the load holds the logos
  (run `wp-x`: each picture after the load held on the screen for 0.8 s; the first is the logos, the next four the
  intro's black). It reaches the picture's thread before the tick's change or after it (9 or 10 pictures covered,
  run to run), so `LOADING` stays over the two pictures that follow the change: whatever was copied out before it
  is that one and at most one of the intro's black ones. Runs `wp-y` and `wp-z` (the race's view and the replay
  mode, each uncovered picture held 0.8 s): no start-up picture after `LOADING`.
- **The load runs with no speed limit and no sound** (`port/moderngekko/src/host.cpp`, `LiftLoadPace`; the core
  asks through `Host::LoadPace`). From before the emulated system starts until the frame tick that takes `LOADING`
  down, the emulator's speed limit is off (`EmulationSpeed` 0) and its sound muted (`[DSP] Muted`, the stream's
  volume); both are then set back to what they were (speed 1.0; a muted player stays muted). Only the host's
  waiting changes: the emulated clock, the disc's emulated times and every word the game computes are a paced
  load's. The intro, the gate and the race are paced as ever.
  Measured (window's first picture to the first picture after the load, pacing logs):
  9.1 to 9.4 s before (ten games), 1.2 to 1.6 s after (seven games; 30 player seats 1.3 to 1.4 s), the load itself
  0.8 to 1.2 s; it ends at the same field with and without (553 in both games of run `wp-mix`, one of them the
  released 0.2.1), and 6 to 13 ms of it are the fallback's. What does move with the host's pace, as it always
  did with a slower or faster PC: the console's clock runs on host time, so the system time the game takes at
  boot is a second or two apart between games (the go line names it); no step reads it (run `wp-mix`: equal at
  every one of 3,171 steps). The log says `fzgx: load: no speed limit and no sound until the course is loaded` and `fzgx: load:
  the speed limit (1.00) and the sound are back, N s after they were lifted`.
- **The look**, all of it read from the player's own game files at run time.
  The words are in the venue name's lettering of the course intro: letters cut from the twelve strips of
  `bmp_e/bmp_view.tpl.lz` texture 0 (432 by 36 each), found and not written down: in each strip the connected
  regions of near-white solid pixels, left to right, are the letters of that venue's name (a strip whose count
  differs gives none), each with the glow pixels within 5 of it that are nearer to it than to its neighbours;
  set a pixel tighter than the letters' widths, a space 9 pixels, as the strips are. They give A B C D E F G H I
  L M N O P R S T U W Y. Under `LOADING` the game's energy meter (`bmp_e/bmp_rac.tpl.lz`: the frame texture 23,
  136 by 24; the fill texture 22, 128 by 16, 4 pixels inside it, cut off at the right). Sizes: the
  meter exactly as large as the race's own energy meter, which the game draws at its picture's size on a screen
  of 640 by 480, a pixel a 480th of the picture's height each way (measured on a race picture of 2,334 by 1,344:
  the fill 350 by 45); the lettering at two thirds, capitals 16 of that screen (`METER_SCALE`, `LETTERS_SCALE`). What cannot be had (a file, a texture, a letter) is drawn in
  the game's small 24 by 24 font (`font/fnt_asc_24x24.bin`: 1,387 characters of 288 bytes, I4 blocks; the space
  first, `/` the 19th, the digits from the 59th, then A to Z and a to z) or left out, and the log says so. The
  count's digits and slash are in no strip: that line is the small font's.
- **The meter is the load's progress** (`panel::Progress`; `fzgx::LoadField`, every field on the game's thread),
  from what the load shows and nothing kept from another load or by course. The game loads by draining a queue
  of file preloads (main.rel `.bss`: a ring of 512 entries of 12 bytes at 0x3C4D8, the count queued at 0x384C0,
  the count done at 0x384C4; read in dumps of run `wp-q`). The first drain stands queued when the race's mode
  begins (93 entries, 14.2 MB in that run: 70 machine files, the course's three, cameras, backdrop, 8 of `init`)
  and is about three quarters of the load: it fills the first 750 thousandths by its files' bytes done (sizes
  from the disc's file table in memory, [0x80000038]). The rest has no such measure (the queue stands at one
  entry for 80 fields): two thousandths a field up to 990; full at the intro. It never goes back, and is shown
  no faster than a whole bar in half a second.
- **Presents while the game draws none.** During the load too, now: every field, at most once in 8 ms, the
  game's thread queues a present of the standing picture with the panel over it (about 100 in a load of a
  second), so that the meter moves. The pacing log's P records of that stretch are these.
- **No count of ready players** (`3/5` under the second panel). Neither the relay nor the coordinator tells a game
  or a launcher who is ready: the relay answers a game's "ready" only with the "go" (`RaceSession.Handle`), and the
  coordinator hears of a race again only when it runs. The panel takes a count (`panel::AtGate`, `Lines`) and
  shows the line when one is given. It needs a relay that sends one: a new packet type to every joined seat
  whenever a seat gets ready (ready seats, seats still awaited), which the released games pass over (an unknown
  type is ignored), read in `relay_wire.cpp`.

A script or a list holds only its own characters (a stray one is refused), and two keys of one thing (`accessTrace`
with `accessTraceNoInterrupts`) are refused together. The table is `EMULATOR_KEYS` in `files_launch.cpp`; a new
setting of the emulator's is a row there and a name given to `RunSettings::Get`, never a `getenv`.

**The emulator's settings the runner sets** (82; `port/moderngekko/src/pins.cpp`), each as the run's own value, which
wins over every settings file: the emulator's stock value unless a value is given here. A setting a file had set to
something else is named in the game's log (`fzgx: the runner sets what decides a race, so these settings of the user
folder are not used: Core.EmulationSpeed, ...`). Not with `debug.userSettings`.

| What | The settings (`Dolphin.ini` section and key; `GFX.ini` where said) |
|---|---|
| How fast the game runs | `[Core] EmulationSpeed` 1.0 (a race's catch-up changes it for a while), `OverclockEnable` and `Overclock` (the race's `match.overclock`: 6), `VIOverclockEnable`, `VIOverclock`, `StaticRecompIdlePC` 0x8001071C (the operating system's idle loop), `StaticRecompIdlePCs`, `TimingVariance`, `CorrectTimeDrift`, `SyncOnSkipIdle`, `SyncGPU`, `SyncGpuMaxDistance`, `SyncGpuMinDistance`, `SyncGpuOverclock`, `MaxFallback` and `GFX.ini`'s `[Hacks] VISkip` (off: behind by more than `MaxFallback` milliseconds, an emulator with `VISkip` on leaves out the picture's interrupt, which the game counts its fields by), `FastDiscSpeed` (off; a race turns it on) |
| What the emulated CPU computes | `[Core] FPRF` on (the emulator's own setting for the game), `AccurateNaNs`, `AccurateFmadds`, `FloatExceptions`, `DivByZeroExceptions`, `DisableICache`, `AccurateCPUCache`, `LowDCBZHack`, `MMU`, `Fastmem`, `FastmemArena` (off under the native module, so that the undo log's guard can protect main memory: `page_guard.h`), `PageTableFastmem`, `LargeEntryPointsMap`, `JITFollowBranch`, `RAMOverrideEnable`, `MEM1Size`, `MEM2Size`, `ARAMExpansionSize`; the thirteen of `[Debug]` that switch parts of the emulator's own compiler off (`JitOff`, `JitLoadStoreOff`, ...) |
| The console, and a debugger's doors | `[Core] DSPHLE`, `[DSP] EnableJIT`, `[Core] HSPDevice`, `SkipIPL`, `SelectedLanguage`, `OverrideRegionSettings`, `GPUDeterminismMode`, `PauseOnPanic`; `[General] GDBPort`, `GDBSocket` (a debugger there reads and writes the game's memory); `[Interface] DebugModeEnabled`, `PauseOnFocusLost`, `UsePanicHandlers` off (an alert of the emulator's is a line of its log: a message box would hold the game until somebody answers it) |
| What of the picture reaches back into the game (`GFX.ini`) | `[Hacks] EFBAccessEnable`, `EFBAccessDeferInvalidation`, `EFBAccessTileSize` 0 (the emulator's own setting for the game), `BBoxEnable`, `ForceProgressive`, `EFBToTextureEnable`, `XFBToTextureEnable`, `DisableCopyToVRAM`, `DeferEFBCopies`, `ImmediateXFBEnable`, `EFBEmulateFormatChanges`; `[Settings] SafeTextureCacheColorSamples`; `[GameSpecific] PerfQueriesEnable` |
| How the picture looks (`GFX.ini`) | `[Enhancements] ArbitraryMipmapDetection` on (the game's textures whose mipmap levels are other pictures: the recovery strips turn purple and blue farther away, as on a console; its threshold stays stock) |
| When a picture is handed to the screen | `[Core] RushFramePresentation`, `SmoothEarlyPresentation`, `PrecisionFrameTiming` (the wait for a console's moment is the emulator's precise one); `GFX.ini`'s `[Hacks] EarlyXFBOutput` (off, a picture goes out a field later than a console's), `CapImmediateXFB`, `SkipDuplicateXFBs`; and `[Hardware] VSync`, which is the launch file's `verticalSync`: the player's setting in the launcher, not the file's |
| Set before this package, in every start with a launch file | `[Core] CPUCore` (the native module's, or the emulator's own compiler with `debug.jit`), `CPUThread` (on; off with `debug.singleCore`), `EnableCheats` (off; as the file says with `debug.cheats`); for a race `FastDiscSpeed`, `EnableCustomRTC` and `CustomRTCValue` |

Left to the user folder, because they are the player's: the picture (`config.ini`'s size and backend, `GFX.ini`'s
resolution, enhancements but mipmaps, shader settings, free look), the sound (`[DSP] Volume`, `Muted`, `Backend`, the
mixer's `AudioLatency`, `AudioBufferSize`, `AudioFillGaps`, `AudioPreservePitch`: none of them moves the emulated
time), the controllers (`Config\GCPadNew.ini`; `[Core] SIDevice0` to `SIDevice3`: which port has a standard
controller, the GameCube adapter or nothing; `AdapterRumble0` to `3`; but a race's port 1 is a standard controller
unless it is the adapter's, whatever `SIDevice0` names: `pins.cpp` `PinRaceController`), the memory cards (`SlotA`,
`SlotB`, their paths), the window's place, the real-time clock of a start that is no race, the logs and the dumps
(files and lost time only).

**What the reader refuses**, each with a sentence that names the key (`"debug.lateSteps" is not a whole number from
1 to 16`); the game then does not start (status 2):
- a key it does not know, at the top and in `match`, `replay`, `debug`, `debug.forceRollback`, `debug.dump`
  (`"debug.lateStep" is not a setting of a launch file`; a variable's own name, `"debug.FZGX_PAD"`, is no key). Only
  inside the coordinator's match start (`match.match`) are unknown members passed over: it is the server's message;
- `testLagMs` or `autopilot` in the `match` part, where a match file once held them: they are `debug` settings, and
  no test setting comes in through a match;
- a value of the wrong kind (text for a number, a number for `true`), an empty text where a file or folder is named,
  a number with a fraction or outside its range. Nothing is bent into its range;
- a file that is not one JSON object, is nested more than 32 levels deep, has text after the object, or holds a
  number too large to be one (`1e999`: the parser ends the program on such a number, so `boot::TooLarge` looks for
  one first, here and in a replay's header and the bundle's patch list and manifest).
A key written twice is not noticed (the later one counts).

**What stays outside the file.** The runner's own options are the upstream program's: `--game`, `--module`,
`--user-dir`, `--no-mods`, `--title`, `--mute`, `--graphics` and `--audio` change nothing the game computes; `--mods`,
`--automation-dir`, `--load-state` and `--allow-interpreter` need their key of `debug` (above); the runner's own
netplay (`--netplay-host`, `--netplay-join`) is refused, with a launch file and without. In the environment stays what a library
reads for itself: SDL's hints (`SDL_JOYSTICK_HIDAPI_GAMECUBE=0` for every test game), and
`MODERNGEKKO_CACHE_AFFINITY` (which processor cores the program runs on). In the user folder stay the player's own
settings (the picture, the sound, the controllers, the memory card: the list above; `config.ini`: the graphics
backend, the picture's size); what decides a race is the runner's to set, whatever `Config\Dolphin.ini` says.

**A start with no launch file is refused**: the runner prints `fzgx: this runner starts a game only from a launch
file: give --config FILE ...` and ends with status 2 (`fzgx_port::RefusedStart`, asked once the arguments are read;
`--help` still prints the usage). Such a start was the emulator alone, with its test settings read from the
environment. The timing batch's JIT runs, which were such starts, have launch files (`gate-scene-reference.json`). recomp-kit's harness started by hand needs
`--arg=--config --arg=FILE` (a file written by `launch-file.py ... --harness SCENE`), and its own batch commands
(`recomp.py time`, `pgo`, `batch`), which give the runner none, do not start this runner. The variables' rows in
`switches.tsv` say what the emulator reads in a build without the port.

Named launch files for tests are `port/configs/*.json` (its README lists them); `launch-file.py` writes
a run's file from one, and the scripts that start a game name one (`CONFIG=`) and export no setting.

**`match.match`** (`MatchStart` in `net/src/Online.Protocol/Messages.cs`; keys in camel case):

| Key | Content |
|---|---|
| `matchId` | the race's name on the server |
| `relay` | `host`, `port` (1-65535) (and `id`) |
| `token` | hexadecimal, not zero; what the game joins the relay with |
| `seat` | this player's seat, 0-29 (default 0) |
| `setup.course`, `setup.laps` | the stage number (default 1); the laps, 0-99 (default 3) |
| `setup.shuffleSeed`, `setup.cpuSeed` | the two seeds every game starts the race from |
| `setup.seats[]` | `seat` (0-29), `machine` (0-40, default 0), `setting` (0-100, default 50), `playerId` (a player seat when present), `name`, `cpu`, `stockColor` (0-3) or `color` (`r`, `g`, `b`), `custom` (optional: a garage-built machine raced in place of `machine`: `body` 0-24, `cockpit` 25-49, `booster` 50-74, `pilot` 0-40 (the stock machine whose pilot drives it), `colors` three 32-bit words (red, green, blue, the part's material byte; absent or 0: the part's own colour). The game races it as id 0x32 and reads it from the race block's table, `memmap.RACE_CUSTOM`, `port/image/sites_custom.py`) |
| `setup.rules` | `cpuLevel` (0-3, default 3), `boostFromStart`, `restore`, `finishLimitSeconds` (0-600, default 0: the race ends for everyone that long after the first machine finishes; 0, and a set-up without it, as every replay from before the rule: when the last has finished or is out), `mineRegenMs` (0-5000, default 0: a mine that went off is armed again that long after; 0, and a set-up without it, as every replay from before the rule: it stays gone) |
| `setup.stats` | optional: the race's stat pack (`handbook/stat-packs.md`). `name` (1 to 40 characters of plain ASCII), `fingerprint` (40 hexadecimal digits) and `values`: base64 of 6 bytes a value (the block 0-115, the offset in the block, the word's 32 bits high byte first), in the order of block and offset, each word once; then the game's code constants the pack changes, each an entry of block 240 whose offset byte is its slot (0-4; a 64-bit number two entries, high word first: `handbook/stat-pack-constants.md`). Absent or `null`: the disc's own car data and the game's own constants, and nothing of the race differs from a game without stat packs |
| `setup.packs` | optional: at most 4 packs, each as `setup.stats`, that a seat may race in place of the race's (`seats[].pack`). Absent or `null`: none (`handbook/stat-pack-constants.md`, "A pack a seat") |
| `setup.grid` | optional: the start grid, the seats in starting order, pole first (the coordinator's rule: the players ahead of the CPUs, in reverse of the lobby's last finish; `Online.Server/Matchmaking/StartGrid.cs`). The core writes it into the race block (`memmap.py` `RACE_GRID_ON`, `RACE_GRID`), and the image hands it to the game's grid sort in place of its own (`port/image/sites_grid.py`). Absent or `null`: the game's own grid (the CPUs ahead of seat 0, which starts last), as every match and replay before it |
| `seats[].pack` | optional: the seat's own pack, 0 the disc's own stats and constants, k the k-th of `setup.packs`; absent, the race's `setup.stats`. A stock machine races a row of its own; a custom machine takes its constants only |
| `type`, `reportLive` | the message's kind (`matchStart`) and whether the launcher reports the live race; the game ignores both |

**What the game refuses of a match start** (`boot::ReadMatch`, and `FromMatch` for the set-up, which a replay's
header goes through too). A refused match starts no race, and the log says which rule (`fzgx: FILE: ...: refused`):

| Part | Refused |
|---|---|
| Missing parts | no `relay`, `setup` or `setup.seats` |
| `seat`, `relay.port` | present and not a whole number in its range: 0-29, 0-65535 |
| The relay | none to join: no host (in `match.relay.host` or `relayHost`), a port of 0 or none, a token that is not hexadecimal or is zero. Without the refusal such a match would start the race with no session |
| `setup.seats` | an empty list |
| `setup.course` | not one of the 37 stage numbers that hold a course (the 26 cup courses, the story's stages 37 to 45, the scenes 49 and 50; `COURSES` in `files_match.cpp`) |
| `setup.laps`, `setup.shuffleSeed`, `setup.cpuSeed` | present and not a whole number in its range: 0-99; 0 to 4,294,967,295 for each seed |
| A seat's `machine` | present and not a whole number from 0 to 40 (the game's 41 machines) |
| `setup.grid` | present, not `null`, and not a list of every seat of the race once: as many entries as racers, each a whole number below that, none twice (`GridIntoRace`) |
| A seat's `custom` | present and not an object with `body`, `cockpit`, `booster` and `pilot`, each a whole number of its kind's range (a part of another kind is read past the game's tables: fgx-re's exploit), and `colors`, when present, three whole numbers of 32 bits |
| `setup.stats` (`StatsOfSetup`, `files_stats.cpp`) | not an object; a `name` that is not 1 to 40 characters of plain ASCII or has a space at either end; no `fingerprint` or `values` text; `values` that is not base64 written the one way (the standard alphabet, padded, no space, no bits left over), is no whole number of 6-byte entries, has none or more than 5,109; an entry of block 240 whose slot is above 4, or a 64-bit constant with one of its two entries, or a constant outside its range (`port/core/src/const_ranges.h`); an entry of another block above 115, whose offset is 0 (a name's place), no multiple of 4 or beyond 0xB0, that is there twice or out of order, or whose value is outside the game's range for that word (`port/core/src/stat_ranges.h`; the table: `handbook/stat-packs.md`); a `fingerprint` that is not SHA-1 of the byte 1 and the decoded values. The sentence says which entry and which rule |

- A whole number is a JSON number with no fraction; text, a fraction or a number outside the range is refused,
  never bent into the range. A member that is absent takes its default.
- An entry of `seats` whose `seat` is absent, not a whole number or outside 0 to 29 is skipped. A setting is
  clamped to 0 to 100, a level to 0 to 3, each part of a painted colour to 0 to 255.
- `finishLimitSeconds` reaches the game as steps (60 a second) in a word of the race block, the block of the data
  window the game's set-up is written into (`memmap.py`: `RACE`, +300 `RACE_FINISH_LIMIT`; +256 to +264 the other
  rules, +268 the seats' settings). What ends the race is the image's (`port/image/routines/raceend.py`): no clock
  of the server's, the relay's or the host computer's. The results begin 480 steps after the last player seat's
  finish inside the rule's steps, at most the rule's steps + 480 after the first finish. The rule ends the wait for
  machines still racing; it takes no finish away: a racer that crosses the line after the rule's steps and before
  the results begin is a finisher with a time (`finished` true in the results).
- A replay with the rule is still version 2. The rule is one more key of the header's match start, which a reader
  that does not know it passes over, and it moves no machine: it decides when the results begin, and the steps and
  their checksums are the race's either way (a kept recording with a 5 s rule put into a copy's header played on
  image ba291317 with 0 of 7,512 steps differing, run `raceend-late`; the other way round, a replay with the rule
  on a build from before it, has not been run). A stat pack changes the race itself, which is why it has version 3.
- `mineRegenMs` reaches the game as whole steps (ms x 60 / 1000, clamped to 0-5000 first: 50 ms is 3 steps, less
  than 17 ms none) in the race block's word +308 (`memmap.py` `RACE_MINE_REGEN`), which the image's
  `port/image/sites_mines.py` counts. It moves machines, so a race with it is version 5 (section 1).
- A stock colour wins over a painted colour.
- A player's name is cut to what the results screens draw: A-Z, 0-9, space, `.`, `-`, `&`; 16 characters.
- The race has as many racers as `seats` has entries, at most 30.

## 3. The bundle

A folder, named by the launch file's `bundle`: the online part of the game, the same files for every race. Two kinds:
the **native form** (the players' kind: the image and the patched words are compiled into the native module) and
the **JIT reference's** (the emulator's own compiler with patch codes; the gates compare against it).

| File | Kind | Content |
|---|---|---|
| `mod.bin` | both | the image: PowerPC routines and their data, placed at 0x81800000. `FZMD` at byte 0, the seat routines' slot `FZSI` at byte 0x10 |
| `manifest.json` | both | `commit`, `image_sha1`, `image`, `slot`, `race`, `names`, `name_size`, `version`; the native form adds `form` (`native`), `complete`, `mode`, `host_net`, `host_tick`, `host_linked`, `main_rel` |
| `patches.json` | native | `version`, `complete`, `image_sha1`, `main_rel`, `patches`: for each patched word of the game `address`, `original`, `new` (eight hexadecimal digits each) and `what` |
| `symbols.json` | native | the image's routines and the host calls: name to address. The module's build keeps a copy; the game does not read it |
| `codes.txt` | reference | sections `$Name`, each followed by lines of two hexadecimal words (Gecko codes) |
| `same-race.json` | staged beside them | `rows` of `recorded`, `plays_on` (two image SHA-1s) and `evidence`; `released`: each released build's `version` and `image` (the launcher reads `rows` only) |

**What the game checks** (`boot::TheBundle`; a bundle that fails is refused and no race starts):

- `mod.bin` has at least 0x40 bytes and at most 0x10000, with both marks in place.
- `manifest.json` gives `commit` and `form`. A manifest that is missing or unreadable makes the bundle the
  reference's kind.
- Native form: `patches.json` has `complete` true (a list built without the game's files is refused), its
  `image_sha1` is the SHA-1 of `mod.bin`, and every entry parses with an address that is a multiple of 4.
- Reference kind: `codes.txt` parses.

**What the game writes for one race** into its copy of the image, at the slot: +4 this player's seat, +32 zero,
+36 one (uniform rules), +40 the player seats' mask, +44 the input delay. The mode word is 1 for a race and for a
replay, which plays as the race.

**What the launcher checks** (`RecompLaunch.cs`): `mod.bin` exists, or no race can start. A bundle with
`patches.json` is the native form and goes along in every start, plain starts too. A game build's name is the
SHA-1s of `mod.bin`, the module and the runner; players are matched only within one build.

**`same-race.json`**: the launcher plays a replay that names an image only on that image, or on one a row names
for it; a replay that names none is played, and the game itself checks neither. A row is added only for a run in
which a recording made with `recorded` played on `plays_on` with no step differing; never by reasoning. An image
that has no row from a `released` image refuses that build's replays: `stage-launcher.sh` says so, `package.py`
refuses the player copy (`port/build/same_race.py`).

## 4. The controller helper

`moderngekko-run.exe --controllers USER_FOLDER` starts no game: it starts the emulator's input layer on that user
folder as a game start does (`port/moderngekko/src/controllers.cpp`) and talks in lines of JSON, one object a line,
on its standard input and output. The launcher's Controller page, its check before a start and its pad reader (the pad that works the launcher's
window, run only while the window is in front and no game runs: `Launcher.Core/Pads/PadReader.cs`) are its only
callers (`net/src/Launcher.Core/ControllerHelper.cs`; every line it says is checked in `Inbound/ControllerMessages.cs`). It
reads the settings and never writes them, takes no launch file, and ends when its input closes. A line of its output
that is no object with an `"is"` (a library's own words) is skipped by the reader.

| The helper says (`"is"`) | With | When |
|---|---|---|
| `devices` | `default` (the device a port with none named gets), `devices`: `id` (`BACKEND/INDEX/NAME`, the settings' own name), `backend`, `index`, `name`, `gamepad` (SDL knows its layout), `rumble` | at the start; when one is plugged in or removed |
| `ports` | four of `kind` (the settings' number: 0 nothing, 6 a controller, 12 the adapter), `device`, `connected` | at the start; when it changes; after `reload` |
| `adapter` | `state`: `notAsked`, `unused` (no port is set to it: the emulator does not look), `found`, `none`, `noDriver`, `inUse`, `error`; `detail` (the emulator's own words for the last three); `ports`: which of its four have a controller | at the start; when it changes |
| `ready` | `version` (1) | once, after the first three: the device list has stood still for 250 ms |
| `pad` | `port`, `kind`, `connected`, `buttons` (A B X Y Z Start L R Up Down Left Right), `stick` and `cStick` ([x, y], 0 to 255, 128 at rest), `l`, `r` (0 to 255) | while a port is watched, whenever it changes: what the game would read |
| `captured` | `id` (the `capture`'s own), `device`, `input` and `expression` (what the settings would write for it; both null: none in time) | once for each `capture` |
| `problem` | `text` | a line that is no command |

| The launcher says (`"do"`) | With | What happens |
|---|---|---|
| `reload` | | the settings on disk are read again |
| `adapter` | | the native adapter is looked for from now on, as a game start does; never before |
| `watch` | `port` (0 to 3; -1: none) | that port's `pad` lines |
| `capture` | `id`, `device` (`""`: stop), `ms` (500 to 15,000) | the emulator's own detector listens on that device for the first input pressed or pushed |
| `rumble` | `port`, `ms` (50 to 1,000) | that port's motor runs for so long |

The emulator turns SDL's own driver for the adapter on or off at its start by whether a port is set to the adapter;
the launcher therefore starts the helper anew when that changes, and ends every helper before it starts a game.

## 5. The game's save file, read for the controller options

A race reads one thing of the player's saved game: the controller options (`boot::ReadSavedOptions`,
`port/core/src/files_save.cpp`: a start straight into a race never runs the title's memory card sequence,
which otherwise puts them in memory). The file is the emulator's GCI file of the
game's save in the folder the host names (`Host::CardFolder`: slot A's folder of saves,
`<user folder>/GC/USA/Card A` unless the emulator's settings name another): a 64-byte directory entry, then the
save's four blocks of 8,192 bytes. Only read, never written. Offsets below count from the save (the file's byte 64).

**Which file.** The one the emulator's folder card gives the game, found as the emulator finds it: every file of
the folder whose name ends in `.gci` (in any case), in the order of the names' bytes, by the directory entry inside
the file (game `GFZE`, maker `8P`, file name `f_zero.dat`) and not by its name. The game's own save is
`8P-GFZE-f_zero.dat.gci`; a save imported under another name is taken all the same. A file whose entry cannot be
read, and one whose size is not its entry's block count, is passed over (the emulator does not put it on the card).
Of several files of the save the first by name is the card's: the log's line names the others as left out.

| Bytes of the save | Content |
|---|---|
| 0-1 | the checksum of bytes 2 to the end: reflected CRC-16, polynomial 0x8408, from 0xFFFF, inverted |
| 3 | the save's version, 11 |
| 0x2060, 0x59E0 bytes | the game's own block (main.rel `.bss` 0x718E0 while it runs); the six blocks below are in it |
| +0x8, 0x40 | stick calibration: a port 8 corner points (x, y: signed bytes), from the right, counter-clockwise |
| +0x48, 8 | dead zones: a port 2 bytes (across, up), 0 to 50 |
| +0x50, 0x150 | button settings: a port three sets of 0x1C bytes (the standard pad, two steering wheels), a set four 16-bit and five 32-bit bindings: steering, sliding, nose up and down, a pair of buttons, accelerator, brake, three buttons |
| +0x5542, 8 | a steering wheel's range: a port 2 bytes (to the right 21 to 127, to the left 21 to 128) |
| +0x554A, 8 | a steering wheel's play: a port the same byte twice, 0 to 20 |
| +0x5556, 4 | the control scheme: a port a byte, 0 or 1 |

A race is given the first three (stick calibration, dead zones, button settings): the standard controller's. The
steering wheel's three are read and checked with the rest and not written into a race: a wheel is not supported
online yet (the kind of controller in port 1 becomes machine 0's control mode, which is in the checksum, so a
wheel would race another race).

**What the reader refuses** (a save is refused whole; the race then starts on the game's stock options and the log
says `fzgx: controller options: the game's defaults, the player's are not loaded: FILE: WHY`): no folder; no file
of the save in it; a file that is there and does not open, or whose bytes cannot be read (another program holds it:
the line says which, never "no save file" or a size); a file that is not 32,832 bytes; a directory entry of another game (`GFZE`), maker (`8P`), file name (`f_zero.dat`) or
block count (4); a checksum that is not the bytes' (the game's own check); another version; and any value the game's
two Options screens cannot write: a dead zone above 50, a gate corner outside its eighth of the circle or nearer
than the calibration screen starts (37 for a cardinal corner; 26, 26 for a diagonal one), two neighbouring corners at
one angle or the wrong way round, a wheel range or play
outside the ranges above, a scheme above 1, a binding that is not exactly one bit of its set's mask (16-bit entries:
0x81FF, 0x811F, 0x831F; 32-bit: 0x803FFF1F, 0x80303F1F, 0x80F03F1F; the top bit is "nothing"). The card's serial
number in the save is not checked: the emulator rewrites it in its own copy at every load.

## 6. A race's summary line (`race-summary.txt`)

After "it felt laggy" there was nothing to read: the relay's log counts every late input, the countdown's too,
which are late by design. So every race leaves one line of text, in a file a tester can send.

**Where.** `race-summary.txt` in the game's user folder, the one the runner is started with (`--user-dir`,
`Host::UserFolder`): `recomp-user\race-summary.txt` in the player's data folder (`%LOCALAPPDATA%\fzgx-launcher\`; a
player copy: its `data\`), beside `runner.log`. There because it is the game's own folder of the data folder, which
the game is handed in every start (no new setting, and a launcher of any age), and where a tester is sent for the
game's log already. A scripted run's is in its data folder's `recomp-user`, and the same line is in the run's
`fzgx.log` (`fzgx: race summary: ...`).

**When.** Once, when the game stops (`net::Shutdown`): the player left the results, closed the window in mid-race,
or the game stopped itself. Only for a race that got the relay's go; a playback leaves none; a game that is killed
or crashes leaves none (its `runner.log` is what there is), and so does a game whose emulator never finishes its
shutdown and is ended from outside (its log then has the freeze lines and the stop line of section 7, and no
summary). It is the last thing the session does, after the
replay is closed with its results, and whatever goes wrong in it ends there as one line of the log
(`fzgx: the race's summary was not written to race-summary.txt: ...`, which names no folder): a report never costs
a replay.

**The file.** It keeps its last 300 lines, found from its end. The new text is written beside it
(`race-summary.txt.new`) and moved over it once every byte is in that file, so a game ended half way or a full
disk leaves the file as it was, and nothing beside it. A file that is there and cannot be opened or read is left
alone and no line is written (read as empty, its lines would become one). A file over 4 MiB is not the game's:
it is not read, and is replaced. A line is ended by a line feed; a carriage return before it and empty lines are
dropped when the file is next written.

**What it costs.** While the race runs: counters in memory (a few additions a displayed frame, a comparison of six
bytes for each frame the relay closes; no allocation, no file). A race's frame reads no clock for it: `behind` is
the figure the time sync has just worked out for that frame, `frame` is the same reading against the frame
before's, and the pictures' times are stamped by the thread that presents and taken once a displayed frame
(`Host::PictureIntervals`; a short lock, no clock read on the game's thread). A frame of the countdown and of the time after the
race, where the time sync works out none, reads the clock once more for it. The file is read and written once,
after the emulation has stopped.

**The line** (format 1). Plain ASCII, one line, words separated by one space, in this order. ` | ` separates four
parts: the head, then `countdown`, `race`, `after`. Every word but the head's first two and a part's name is
`key=value`; a reader takes them by key and passes over a key it does not know. A value `A/B` is the median and
the worst; `-` where nothing was counted.

| Part | Word | Content |
|---|---|---|
| head | `1` | the format |
| | `2026-10-11T16:21:07Z` | when the line was written, UTC |
| | `match`, `course`, `racers` | the match's id (`-` unless letters, digits, `-`, `_`), the stage number, the racers in the race |
| | `players`, `seat`, `delay` | the player seats, this game's seat, the input delay in steps |
| | `present` | how pictures went to the screen: `field` (as ever), `console` or `copy` (the launch file's `present`) |
| | `end` | `finished`: the race ran to its end in this game. `closed`: the game stopped before that. `desync`: the relay found this game's state different from the others'. `lost`: no official inputs for 15 s |
| | `steps` | the step the last displayed frame began at |
| | `freezes`, `stood`, `stuck`, `waits` | from 0.2.2 on (section 7). `freezes`: how often, from the relay's go, no game frame began for 2 s or longer (in the countdown: 5 s) while the core was not waiting for the relay. `stood`: the longest of those in seconds, counted up to the moment a stop was asked for (`-`: none). `stuck`: `yes` when such a freeze was going on at the moment the game's first stop was asked for, else `no`. `waits`: how often a frame was held that long by the core's own wait for the relay's official inputs; a wait is counted apart and is in none of the other three. A race the player closed after a freeze reads `end=closed ... stuck=yes`; a race the player simply left reads `stuck=no`; a race lost to the relay reads `end=lost` with its waits. A line without these words is an older build's and says nothing either way |
| each phase | `s`, `frames` | its length in seconds, its displayed frames |
| | `ping` | the round trip of each ping, ms (a test's lag included): median/highest |
| | `fps` | displayed frames in each whole second: median/lowest (the second a phase begins in is not counted) |
| | `behind` | steps behind the schedule the time sync aims at, at each displayed frame (below 0: ahead): median/most. In the race it is meant to be between -2 and 1; from about 2.5 on the player's inputs reach the relay too late for their frames. In `countdown` and `after` the game waits for each official frame and is behind by design, by about the round trip in steps and 3 |
| | `late` | frames the relay closed with another input for this seat than the one the game stamped for that frame (the game's input came too late and the relay used the one before it). Counted by the phase the input was stamped in. A late input equal to the one before it is not counted: nothing was replaced |
| | `frame`, `slow` | ms from one displayed frame's start to the next one's, by the hook's clock: median/longest; and how many came more than 2 ms over a frame's length (16.67): the frame before did not fit its field |
| | `shown`, `uneven` | ms from one picture handed to the screen to the next (the present call's return, stamped by the runner): median/longest; and how many were more than 2 ms off a frame's length. `-` on a host that shows none |
| `race` only | `rollbacks`, `depth` | how many, and the steps gone back: median/deepest |
| | `catchups` | steps added to catch up with the schedule (one a frame at most, and none on top of a rollback deeper than 6 steps: `port/core/src/schedule.cpp`) |
| | `held` | frames that ran no new step, to fall back to the schedule |

The phases: `countdown` from the relay's go to the first frame that runs ahead on predicted inputs (the course's
introduction and the countdown: lockstep); `race` from there to the frame that settles the race; `after` what
follows (the finish sequence once every step is official, the results). Nothing in the line names a person: the
match's id and the seat are in the replay file too.

## 7. The game's log: why it stopped, and a game that stands still

The game's log is `runner.log` in the game's user folder (`recomp-user` in the player's data folder; the same
lines of ours are in `Logs\fzgx.log`, with the start before's kept as `Logs\fzgx.1.log`). **The runner keeps the
last 20**: `runner.log` and `runner.1.log` (the game before) to `runner.19.log` (`WriteOutputTo`,
`port/moderngekko/src/adapter.cpp`; the moving is `LogFileToWrite`, `port/core/src/files_logs.cpp`, tested in the
core). **A log that cannot be moved aside is not emptied**: a game whose emulator never finished shutting down
still holds its `runner.log` (the one with the freeze report), so the next start leaves it and writes
`runner.held1.log` (to `.held5`, the first that is free; the same for `fzgx.log`); the held log takes its place in
the row at the first start after its game is gone. A game's log is 7 KiB at the median and 47 KiB at most over
1,124 kept test games; a freeze adds about 2 KiB a report, and the emulator's lines at most 520 lines a game: 20
logs are under half a megabyte as a rule and a few megabytes at the worst. No line of ours names a folder of the
player's (the save file of the controller options is named by its file name alone).

**Why the game stopped.** Every way the game is asked to stop writes one line before the shutdown
(`NoteStop`, `port/moderngekko/src/freeze_watch.cpp`; the wording is `freeze::StopLine`, tested in the core), the
first as `fzgx: stop: WHY`, a later one as `fzgx: stop (asked again): WHY`:

| WHY | What asked |
|---|---|
| `the game's window was asked to close: the strip's close button` | the button of the runner's strip |
| `the game's window was asked to close: Alt+F4` | the keys, by their state when the system's close command came |
| `the game's window was asked to close: the system's close command (the window's menu or the taskbar)` | the system's command without those keys |
| `the game's window was asked to close: a close message from outside the window (another program, the launcher)` | a close message nothing of the window led to |
| `Escape was pressed in the game's window` | Escape in a window (in full screen it leaves full screen and stops nothing) |
| `the game asked for it itself (its own line above says why)` | the core's own stops, each with its line before: `the player left the results; stopping`, a desync, a replay's end, a refused stat pack, the controller options left, `the launcher asked the game to stop (its status board's mailbox); stopping` (section 8: exit code 0, no race kept for a rejoin) |
| `the emulator stops with no stop asked for by the window, the keyboard or the game (...)` | none of the above: a test's stop command, or the emulator's own. Also every stop of a `debug.windowControls` window, whose messages the runner does not see |

**The process's exit code** (the runner's; the launcher reads it, `AppState.KeepsTheRace`): 0, a stop of the game's
own or any close outside a race's racing; 3 (`EXIT_RACE_OVER`), a game started again into a race the relay says is
over; 4 (`EXIT_LEFT_RACING`), any stop not the game's own (the window, Escape, the emulator) while the player's seat
still raced: the launcher keeps the race and offers Rejoin. Both are the
core's (`fzgx::ExitCode`), ended with by the adapter after the shutdown; a crash or a kill gives the system's own.

When a freeze was going on at that moment the line ends `; the game stood still when it was asked (for N s by
then)`: the length of that freeze, not of an earlier one. After the shutdown: `fzgx: stop: the emulator has shut
down, N s after the stop was asked for`. A shutdown that has not finished after 5 s gets `fzgx: stop: the emulator
has not shut down 5 s after the stop was asked for; where its threads are:` and the freeze lines below, once. A log
that ends without "has shut down" is a game that was ended from outside, or crashed. **No stop line at all**: the
launcher ending the game's process (its `Kill()`, when a game does not close in time), the task manager, a crash:
nothing of the game runs to write one.

**A game that stands still** (`port/core/include/fzgx/freeze.h`, tested in the core; `freeze_watch.cpp`,
`threads.cpp`). The watch starts at the game's first picture (a boot that fails starts nothing). From the relay's
go until the session ends, a thread of the runner looks four times a second at a count of game frames that the
game's thread publishes (one store a frame; the frame tick). When none began for 2 s it writes a report, and again
when the game has stood 4, 8, 16 and 32 s, then once a minute, ten reports a freeze at most. **In the countdown**
(from the go to the first frame that runs ahead: lockstep, with the course's loads, where a healthy frame reaches
0.5 to 0.7 s on a fast PC) the first report waits 5 s, then 8, 16, 32. A playback is watched too. **A game that
hangs at the start gate before the relay's go is not watched** (the gate's wait is a loop of the game's own that
begins no frame, for as long as the other games load).

**A wait is no freeze.** When the frame is held by the core's own wait for the relay's official inputs (lockstep:
the countdown and the results; it gives up after 15 s with `no official inputs`, and the race is lost), the log
gets one line, `fzgx: wait: no game frame for N s: the core waits for the relay's inputs (report K of this wait;
...)`, at the same times, with no sampling; it is counted in the summary's `waits` and in none of `freezes`,
`stood`, `stuck`. What a stand is, is settled at its first report.

| Line (`fzgx: freeze: ...`) | What it says |
|---|---|
| `no game frame for N s (report K of this freeze; game frame F, race step S; in the countdown or in the race; the core is not waiting for the relay)` | how long, and where in the race |
| `emulated CPU, 8 samples 10 ms apart (...): pc P lr L (PLACE) xN, ...` | the emulated CPU's program counter and link register as last stored (native code stores them at its exits), each different pair with its count. One pair: a wait or a host call; several: a loop. PLACE names the game's wait for the graphics side (`IDLE_GPU_SYNC`), its two disc waits, the system's idle loop, a host call, the online part's code |
| `in those 70 ms: emulated time advanced T ticks, H network hook calls, W data window accesses; msr ..., pending exceptions ..., interrupt cause C mask M` | whether the emulated machine runs at all (ticks), whether the online part's code runs (hook calls, window accesses), and which interrupts are raised and let through (0400: draw done) |
| `one place with interrupts off while emulated time runs: a halt loop, ...` | only then: the CPU spins where nothing interrupts it, as the game's system does after a panic (`OSPanic`); the game's own words are the `fzgx: emulator: N[OSREPORT]` lines before the report (a file and line of the game's source, and a stack of return addresses) |
| `the game's thread, host code, 8 samples: FILE+OFFSET xN, ...` | where the host thread that runs the game is, sampled as often |
| `the game's thread, call stack (innermost first): FILE+OFFSET < ...`, `the picture's thread, call stack ...` | both threads' call stacks, by program or library file and offset |
| `command list: write W read R (safe S) distance D, base, end, breakpoint; read enable, link, ...; a command processor interrupt waits or not; picture on its own thread, deterministic, sync; emulator running or stopping` | the emulator's hand-over of the command list between the two threads, as its command processor holds it |
| `the game moves again; it stood for N s (K reports)` | a frame began again (`fzgx: wait: the game moves again; it waited for ...` after a wait) |
| `the race is over for this game (lost, or its recording ended); it had stood for N s (K reports)` | the race ended for this game while the report ran (lost to the relay, a playback's end): nothing stands any more, and a stop asked for later finds no freeze |

**What the sampling does, and when.** Nothing of the emulated machine is written. To read where a thread is, the
watcher stops that thread for the copy of its registers and of the top of its stack (microseconds), lets it go on,
and works the call stack out from the copy. It does so in a game that has begun no frame for 2 s (5 s in the
countdown) and is not in the core's wait for the relay; and once, 5 s after a stop was asked for, in a game whose
emulator has not shut down, which may be a game that only shuts down slowly. So a healthy thread can be stopped
for those microseconds in that one case and in a long stall that ends by itself. **Known limit:** working the
stack out asks the system for unwinding records and file names (the loader's lock), and the lines are written
with the C library (the heap's lock). If the thread that stands still holds one of those locks, the watcher
stands with it: the report's first line is in the log and nothing after it.

How to tell the cases apart: **the game's own panic** shows the halt line, one pc with interrupts off, and report
lines before it (a playback's: `Cannot free memory. in "effect.c" on line 6101.`); **the game's wait
for the graphics side** shows the place `the game's wait for the
graphics side` with ticks advancing and the game's thread in the native module or the run loop; **a loop of the
game's own code** shows several pc and lr pairs or several host places inside the native module, with ticks
advancing; **a host thread that waits** shows one host place in a system library under an emulator function, and no
ticks. `freeze-report.py LOG --map moderngekko-run.exe=FILE.map` prints the stacks with each
offset's function, from the linker's map that is kept beside every runner (`ModernGekko\build\moderngekko-run.map`;
a release must keep its runner's map: without it the offsets name nothing).

**The emulator's own lines** (`port/moderngekko/src/emulator_lines.cpp`; which lines and how many:
`port/core/include/fzgx/loglines.h`, tested in the core). The emulator writes its log (`dolphin.log`) only when
its settings say so, which a test's do and a player's do not. From the first picture on the runner copies into the
game's log, as `fzgx: emulator: LEVEL[PART]: text`, every error of the emulator's (`E[Video]: FIFO: Unknown Opcode
...`) and every notice of the game's own report channel (`N[OSREPORT]: [warning] GP WAIT Timeout... (sending
object a/b)`, a panic's words and stack).
- **Two budgets**: 400 lines a start for the game's report lines, 120 for the emulator's errors, so that a burst of
  errors cannot keep the game's own words out. **A run of like lines is written once**: an error that differs from
  the one before in its numbers only, a report line that is the same text; `fzgx: emulator: (N more like it:
  "...")` follows when another line comes, before a freeze's report and at the stop.
- **Unknown Opcode lines do not by themselves mark a freeze**: a race that finished normally had 24 of them in
  26 ms shortly after the go (run frz-whole).
- **No folder of the player's**: the user folder and the account's folder above it become `<user folder>` and
  `<home>`, however written; any other absolute Windows path is cut down to its file's name. Not caught: a path
  that names a folder and ends in plain words (its last folder's name stays), a relative path.
- **What is turned on for it**: the emulator's log parts for the report channel (OSREPORT, OSREPORT_HLE), the
  video side (Video, CP, PE), the CPU (PowerPC), the core and its master log, and its verbosity raised to "error"
  for the run; each is put back when the game has stopped, and the user folder's `Logger.ini` is left as it was.
  Only parts whose being on changes nothing but what is logged: "Host GPU" is not among them (with it the Vulkan
  backend makes a debug messenger and OpenGL turns its debug output on).

**The test** (`freeze-run.sh RUN`, under 2 minutes; not a row of the table): a match race with
`debug.standStill` at step 150 for 2.6 s (the countdown: must not be reported), at step 1,000 for 2.6 s and at
step 1,500 for 30 s; the script closes the window from outside when the last freeze's second report is in the
log. `freeze-report.py RUN --expect-test` passes when the stand in the countdown was not reported, the first
freeze in the race was reported once and moved again, the second twice or more, every report has its five lines,
the stop line names the close from outside and the length of the freeze it stopped in, and the summary line
reads `freezes=2 ... stuck=yes waits=0`. `CLOSE=0` leaves the window alone: the game then waits for a relay that
has given its seat up and loses the race (a wait, a race that ends under it, `stuck=no`); printed, not judged.

## 8. The status board

The channel between the launcher and one running game (a file both map, never a localhost
socket or a pipe). Defined once in `protocol/board.toml`, generated into the launcher's `BoardView`
(`protocol/generated/StatusBoard.g.cs`, compiled into Launcher.Core) and the core's `View`
(`protocol/generated/status_board.g.h`); `protocol/README.md` says how it is checked. **Version 1, 4,096 bytes**,
little-endian, every word naturally aligned, every block on a 64-byte line:

| Block | At | Kind, writer | Holds |
|---|---|---|---|
| Header | 0 | single words; launcher, then game | `Magic` "FZSB", `Version`, `Size`, `LauncherPid`; the game writes `RunnerVersion`, `RunnerSize`, `RunnerPid` when it takes the board, and also when it refuses it. Never moves from one version to the next |
| Pulse | 64 | single words; game | `Heartbeat` (u64): one more each game frame begun in a race's or replay's start (the frame tick), each picture in a plain start and while a race's course loads; `WatchBeat`: the runner's watching thread, four times a second; `StopReason`: the first stop asked for |
| Status | 128 | seqlock; game | `Phase` (None, Starting, Offline, Loading, Joining, Gate, Countdown, Racing, Finished, Playback, Left, Stopping), `Flags` (waits for the relay, practice, watching, rejoin), `Step`, `StopSeen`, `NoticeSeen` |
| Snapshot | 192 | seqlock; game | the race every displayed race frame (not a replay's): `Serial`, `Frame` (the step), `LocalSeat`, `Count`, 30 racer records of 52 bytes (seat, state, rank, flags, lap, knockouts, maximum energy, lap time, race time, best lap, speed, energy, top speed, x, y, z, distance): what the UDP feed carried as JSON before |
| Events | 1792 | ring of 64 records of 32 bytes; game (several threads) | `Head`, then events: Phase, Stop, Stands and Moves (the runner's freeze reports), Notice (taken), Desync, Camera (the local racer's chase camera view, 1 to 3, said at its first reading in a race and at each change while the racer races: the launcher keeps the last for the next race's `match.camera`); overwrite-oldest, the reader counts what it lost |
| Mailbox | 3904 | seqlock; launcher | `StopSerial` (one more: stop gracefully), `NoticeSerial`, `NoticeLength`, `NoticeText` (128 bytes UTF-8) |

A **seqlock** group starts with a sequence word: the writer makes it odd, writes, makes it even; a reader that saw it
odd or changed reads again (as iRacing's and Assetto Corsa's shared memory). Neither side ever waits on the other.

**The start.** The launcher makes the file for every start (`<data>\boards\game-<id>.board`; boards left by a
launcher that ended are removed when the next is made) and names it in the launch file (`board`). The core maps it at
Init (`port/core/src/board.cpp`, `board_map.cpp`) and takes it: no magic, nothing written; another version, or too
small a file: its own version, size and process id written, then nothing else, and the log says `fzgx: board:
FILE refused: ...`; else `fzgx: board: taken (FILE, version 1)`. A launcher whose game refused goes without (no live
race, no freeze detection) and says so in its log; the exit code and the log's stop lines stay the floor.

**What the launcher does with it** (`net/src/Launcher.Core/Board/`): looks every 15 ms; passes each new snapshot on
as `RaceSnapshot` (only one that can be shown), which the race page, Discord and the lobby's watchers take as they
took the UDP feed; and judges the heartbeat (`FreezeJudge`): a game whose heartbeat has stood **10 s** is hung, but
not before its first beat, not at the start gate (the game's own wait begins no frame) and not while the core waits
for the relay's inputs (it gives up by itself). Then the Play column says "Game not responding" with **End game**,
which ends the process by its handle; its replay holds every step made final, and the race is kept for a rejoin as
after a crash. A game that stops through the mailbox stops as its window's close does, with exit code 0.

**Room for later, not built: a read-only export for community tools and OBS.** An OBS
integration is a possibility (race state published as iRacing's shared memory is). The way in: a second mapping the launcher
publishes under a fixed name, read-only for others, with the same header and the Snapshot and Status blocks copied
from the game's board (a version of its own, so that a tool can refuse cleanly), never this board itself: this one
is the launcher's private channel, in its own data folder, and the mailbox stays the launcher's alone.


## 9. The mode package

A game mode (piconet-docs `research/game-modes/`: the design, the vocabulary, the language) is a text file,
`.fzmode`, which one compiler (`Online.Protocol` `ModeCompiler`, D11) turns into a **package**, `.fzmp`: the bytes a
match start carries and every game runs. The words and the bytecode's operations are defined once,
`protocol/modes.toml` (generated: `generated/ModeWords.g.cs` for the compiler, `generated/mode_words.g.h` for the
runner's interpreter, `port/core/src/mode.cpp`). Api 1, the first slice: the words Overdrive needs.

**The package.** Big-endian; a string is u16 its byte length and UTF-8. The mark `FZMP`, u16 the format (1), u16 the
api; then seven sections in this order, each u32 its tag (four letters), u32 its length and its bytes:

| Section | Content |
|---|---|
| `HEAD` | the header: name, badge, about (strings; an empty string when left out) |
| `PARM` | u16 the parameters, then each: name, label (strings), u8 its kind (0 number, 1 delay, 2 switch, 3 choice), u8 its type (0 int, 1 real), i32 least, i32 most, i32 step, i32 default (a real's binary32 bits; a delay in steps; a switch 0 or 1; a choice its index), u16 the choices, each a string |
| `SETP` | u16 the setup words, then each: u8 its id (`[[setup]]`), u8 its arguments, each u8 its source (0 a constant, 1 a parameter) and i32 the constant or the parameter's index |
| `VARS` | u16 the globals, u16 the words a seat; then each global's start value, then each seat word's (i32) |
| `RULE` | u16 the rules, in the file's order; then each: its label (string), u8 its event (`[[event]]`), u8 0, u16 its locals (the event's binding is local 0), u32 its `when` condition, u32 its `while` condition (each an offset in `CODE`, 0xFFFFFFFF none: several `when`s are one condition, joined by `and`), u32 its body |
| `CODE` | the bytecode (`[[op]]`): a condition ends with `Ret`, a body with `End` |
| `SRC ` | the mode file's text, as it was compiled (a replay can show what was raced) |

The **content hash** is the SHA-256 of the package's bytes; its **share code** the first 8 characters of the hash's
base32 (RFC 4648, upper case, no padding). At most 64 KiB, the source included.

**In a match start**: `setup.mode` = `{"package": BASE64, "hash": HEX, "api": N, "params": [i32, ...]}`, the
parameters in `PARM`'s order (a real's bits). The game refuses the race (section 2's refusals) when the hash is not
the package's, its api is newer than the build's, a parameter is missing or outside its range, or the package does
not read. A replay carries it in its header's match as it came (section 1, version 9), so a replay plays with no
download.

**The run** (the language's section 6), once a step at the mode point (main.rel 0x8548C, every step, re-run ones
too): every state the mode keeps is in its block in the game's main heap, under the undo log and in the checksum;
nothing is kept outside it. The rules run in this order: the discrete events (`out`, by seat), each rule on it in the
file's order; then the continuous rules in the file's order (`each seat`: by seat). `while` runs the body every step
its condition holds; `when` on the step it becomes true (an edge, kept a rule and seat in the block); a discrete
event's `when` is tested at each occurrence. Reads of the game see the step as it stood at the mode point; actions
are queued and applied after the run in their order (the last write to a field wins). The operations of a step are
counted (`[[op]]`'s cost); past `[api] budget`, a full queue, or a non-finite real for a game field is a **fault**:
the race ends for everyone at that step with the results as they stand, and the log says `mode error` (D6).
