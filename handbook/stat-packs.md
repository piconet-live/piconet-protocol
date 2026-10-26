# Handbook: stat packs

Changed machine stats that every game of a race loads: the pack file, the ranges and who holds them, the import, a
server's pack and a stack's, the launcher's side, the game's side, and what was run to prove it. The game's own
table of stats is read from the disc by the launcher (`net/src/Launcher.Core/Files/MachineStatFile.cs`); the match
start and the replay are `protocol/formats.md`.

A stat pack is **a table of changed words of the car data** (the car data file of the disc), with a name and, where its
file says, an author. The server hands a race's pack to every game inside the match start; each game lays it over
its loaded car data before the race. Built: the pack file, the import that makes one, a server's one
pack, the launcher's side (the lobby's label, the Garage's stats in such a lobby), the game's side, and one real
pack, GX Unleashed. **Not built**: a list of packs, a vote, a lobby's own
choice; the game's letters for a pack (the main module's table).

- **One pack a server.** From 0.2.0: the game's side, and **one pack a server**, which the operator
  switches on for chosen sessions: a server started with `--stat-pack FILE`, or on a stack its own setting on
  Fly, a pack's name (`deploy.sh STACK pack NAME|none`, the service's deploy script). A race without a pack runs bit for bit
  as before (no byte of the table touched, the replay's version unchanged). After 0.2.0, as a launcher
  and server update with **no new game build**: a vote between the original stats and a pack. A match start
  carries no pack, or one.
- **GX Unleashed, by AKC12** (`protocol/packs/gx-unleashed.fzstats`): a rebalance of the 41 machines and the 75
  custom parts, 1,320 values, in the repository with its author's permission and with the author named in the
  file, in the patch notes and in a player copy's READ ME. The file is the pack only, made by the import from a
  copy of the mod's car data file (its head says the command); the car data file itself is never
  committed. Fingerprint `573d7fde...` since it carries the mod's main-module constants too (`stat-pack-constants.md`); without its constant lines it is 0.2.x's pack, `c24e75a8...`. `RepositoryPacksTests` reads every pack of that folder as a server does.
- **A server's one pack** (`Online.Server/ServerStatPack.cs`). The file is read and checked when the server's
  options are read, by the pack file's reader and its ranges: a file that is missing, is no pack or holds a value
  outside a range stops the start with the line and the rule (status 2). The pack goes into every match start
  (`setup.stats`); its name and fingerprint are in the lobby's state (`statPack`: the launcher's
  `QuickPlaySession.Lobby.StatPack.Name` is what a label shows; no page shows it yet), in the server's start line
  and on its status page. The Garage shows the original stats whatever the server's pack. **Only where every
  member's launcher takes packs**: a launcher says so in its hello (`statPacks`, optional: no new protocol
  version), and a lobby with one member whose launcher did not gets no pack in its match start and none in its
  state, so it races the original stats and is told so (`ServerStatPack.For`).
- **The author.** A pack file may have a third line, `author TEXT` (1 to 40 plain characters, as a name). It is
  shown beside the name wherever the pack is named: the lobby's state (`statPack.author`), the status page, the
  server's start line, the launcher's label. It is **not** in the fingerprint and **not** in the match start: the
  game's reader knows nothing of it, so a pack with and without the line is the same pack to a game. A file
  without the line reads and writes as before.
- **The pack's values for the launcher.** After the welcome the server sends its pack whole, once, to a launcher
  whose hello said `statPacks` (`statPack`: the match start's three texts). It is for what the launcher shows
  before a race; the race's own pack is still the match start's. A launcher from before stat packs is sent nothing.
- **In the launcher: the course band and the Garage.** The pack is a race rule: the machine stage's course band
  names it and its author beside the laps ("Fire Field · Cylinder Knot · 3 laps · GX Unleashed · AKC12"; "Stock"
  for a lobby on the original stats; `LobbyViewModel.BandPackText`), cut at 280 px with an ellipsis (whole when
  pointed at). The lobby's top line holds no pack (on that crowded line it would be cut off or dropped), and the
  Garage's heading row has no Stats box in a lobby (the pack cannot be changed there). While the lobby's
  state names the pack the server sent (the same fingerprint: `QuickPlaySession.LobbyStatPack`), the Garage shows
  and sorts by the pack's values for the 41 machines, laid over the disc's (`MachineStatTable.With(pack)`); out of
  the lobby, and in a lobby without a pack, the disc's. At the head of the stats the pack is named, with its
  author (`GarageViewModel.PackText`): the Garage's own page has no lobby line, and the numbers must say whose
  they are. **The letters stay the disc's** (body, boost, grip): they
  are another table of the game's than the car data (the main module's table), a pack does not carry them and
  nothing is known that derives them from the numbers; the game's own machine select shows the disc's letters in a
  pack race too. No value stands on a machine's picture. `LobbyStatPackTests`.
- **In the launcher: a race's pack.** A match start's pack is checked again, by the game's own ranges and nothing
  narrower: a launcher takes every pack its game takes. One the game would refuse is not acted on
  (`QuickPlaySession.StatPackProblem` is the sentence). The lobby's page shows it whole in its notice, which wraps,
  and the session's status says it, until a race starts, the lobby is left, or the lobby's state names another
  pack or none: the lobby's everyday words (its count, a vote, the machine stage) do not word it away, a server's
  own word (an error's text, a requeue) is not hidden by it, and "Starting race server" keeps its place beside the
  steps. `--play-match` refuses such a match file with status 1. The Replays list shows a replay's pack by name under it
  (`ReplayInfo.StatPackLabel`); a replay whose version and pack disagree is listed as "Stat pack not playable"
  and is not started.

- **A value is a block, an offset and 32 bits.** Blocks: 0 to 40 the machines by id, 41 to 65 the bodies, 66 to 90
  the cockpits, 91 to 115 the boosters (25 each, from 0x1F04 on). The offset is a multiple of 4 from 0x04 to 0xB0:
  never 0, a name's place. The bits are the word as the game holds it: a number's bit pattern, never decimal text,
  so that no two programs can round it differently. The word at 0x48 is its two bytes and two zero bytes.
- **The pack file** (`NAME.fzstats`, text a person can read and compare; `Online.Protocol/StatPackText.cs` reads
  and writes it, for the launcher and the server alike):

  ```
  fzstats 1
  name GX Made Up 1.0
  # Blue Falcon
  machine 6 Weight 2520
  machine 6 Bytes48 3 1
  machine 40 Hover0Z -1.5
  body 0 Weight 500
  booster 24 BoostStrength 14
  ```

  A line is `machine ID` (0 to 40) or `body`, `cockpit`, `booster` and its number (0 to 24), the stat's name from
  the table below (a point's as `Hover0X` to `Hover3Z`, `Wall0X` to `Wall3Z`), and the value: a decimal number, read
  as the nearest 32-bit number and written as the shortest text that reads back to the same bits; for `Bytes48` the
  two bytes. Empty lines and lines that begin with `#` are skipped. The writer puts the values in the order of
  block and offset, so two files of one pack are the same text.
- **What the reader refuses**, with the line's number and which rule: no `fzstats 1` or no `name` at the top; a name
  that is not 1 to 40 characters of plain ASCII; a kind, a number or a stat it does not know; `Name` (a pack changes
  no name); a value that is no decimal number; a word named twice; a value outside its range; a file with no value,
  or of more than 1 MiB.
- **The fingerprint** names a pack: SHA-1 of the byte 1 and the values in order as they travel (6 bytes each: block,
  offset, the bits high byte first). The name is not in it. `StatPack.Fingerprint`; the game works it out again.
- **The ranges.** One list, described once in `protocol/stats.toml` and written three times by
  `protocol/generate.py`: `StatPackRules.All` (`generated/StatTables.g.cs`: launcher and server),
  `generated/stat_ranges.g.h` (the game: `port/core/src/stat_ranges.h` includes it), and this table, which each
  program's test compares its own list with (`StatPackRulesTests`, `TestTheStatRangesAreTheHandbooks`). What each program says of
  a value is held to one list of 2,208 cases, `protocol/tests/vectors/stat-cases.tsv` (denormals, -0, the floors, the
  bounds either side of 0, each in a machine and in each kind of part; `stat-cases.py` beside it writes the list
  from this table), which both programs' tests read: so the two cannot differ on a value. Two kinds of range, and
  who holds which:
  - **From, To, In a part's block: the game holds these, and so do the launcher and the server.** A game build is
    made with them (they are in the runner), so **a pack outside them needs a new game build**, and nothing after
    the game's reader holds a value back. They are safe on their own: a value is the bits of 0 or a normal number
    (a denormal, -0, an infinity and a NaN are refused on the bits, before anything is compared); in a machine's
    block a divisor has a floor (and so do the max speed and the boost duration), any other stat may be negative to
    minus its most, and none is more than about ten
    times the largest the disc holds; a custom part's block is held by that size alone, either side of 0 ("Parts",
    below).
  - **Tried: nobody holds these; they are a warning where a pack is made.** A number outside "Tried" (about twice
    the largest value the disc has, for the nine stats that are no divisor) is one the game takes and nobody has
    raced. The import and a server's start (`--stat-pack`) say which values are beyond it (`warning: N of the
    pack's values ...`, the first ten named), write the pack and start the server all the same. **A launcher that
    is handed a pack holds it to the game's rows only** (a match start, a match file, a replay), so a pack that
    0.2.0's game takes needs no new launcher: a server's setting is all. The 2,208 cases are
    read a second time through that arrival check (`StatPack.FromWire`): it takes and refuses exactly as the game.

<!-- The table below is generated from protocol/stats.toml by protocol/generate.py: change it there. -->

| At | Name in a pack | What | From | To | In a part's block | Tried |
|---|---|---|---|---|---|---|
| 0x04 | Weight | a number | at least 1 | at most 25000 | to 25000 either side of 0 | the whole range |
| 0x08 | Acceleration | a number | at least -7 | at most 7 | to 7 either side of 0 | the whole range |
| 0x0C | MaxSpeed | a number | at least 0 | at most 4 | to 4 either side of 0 | the whole range |
| 0x10 | Grip1 | a number | at least -45 | at most 45 | to 45 either side of 0 | 0 to 9 |
| 0x14 | Grip3 | a number | at least -3.5 | at most 3.5 | to 3.5 either side of 0 | 0 to 0.7 |
| 0x18 | TurnTension | a number | at least -2.2 | at most 2.2 | to 2.2 either side of 0 | 0 to 0.45 |
| 0x1C | DriftAcceleration | a number | at least -19 | at most 19 | to 19 either side of 0 | 0 to 4 |
| 0x20 | TurnMovement | a number | at least -2000 | at most 2000 | to 2000 either side of 0 | 0 to 400 |
| 0x24 | StrafeTurn | a number | at least -1000 | at most 1000 | to 1000 either side of 0 | 0 to 200 |
| 0x28 | Strafe | a number | at least -500 | at most 500 | to 500 either side of 0 | 0 to 100 |
| 0x2C | TurnReaction | a number | at least -300 | at most 300 | to 300 either side of 0 | 0 to 60 |
| 0x30 | Grip2 | a number | at least -10 | at most 10 | to 10 either side of 0 | 0 to 2 |
| 0x34 | BoostStrength | a number | at least 0.1 | at most 220 | to 220 either side of 0 | the whole range |
| 0x38 | BoostDuration | a number | at least 0 | below 4.26 | less than 4.26 either side of 0 | the whole range |
| 0x3C | TurnDeceleration | a number | at least -0.7 | at most 0.7 | to 0.7 either side of 0 | the whole range |
| 0x40 | Drag | a number | at least -0.15 | at most 0.15 | to 0.15 either side of 0 | the whole range |
| 0x44 | Body | a number | at least -13 | at most 13 | to 13 either side of 0 | the whole range |
| 0x48 | Bytes48 | two bytes, then two that are 0 | first: 0 to 255 | second: 0 to 3 | as a machine's | the whole range |
| 0x4C | CameraReorienting | a number | at least -19 | at most 19 | to 19 either side of 0 | the whole range |
| 0x50 | CameraRepositioning | a number | at least -15 | at most 15 | to 15 either side of 0 | the whole range |
| 0x54 | Hover0X to Hover3Z | four points of X, Y, Z; a machine's Z at least 0.01 from 0 | at least -25 | at most 25 | as a machine's, any Z | the whole range |
| 0x84 | Wall0X to Wall3Z | four points of X, Y, Z | at least -25 | at most 25 | as a machine's | the whole range |

<!-- The end of the generated table. -->

- **Where the ranges come from.**
  - **Bits.** The race runs with denormals read as zero, so a denormal
    weight is a division by nothing whatever "above 0" says of it: every value is the bits of 0 or a normal number.
  - **Floors of the divisors**, read from the physics code (the main module's 0x21CFC to 0x2C97C): the weight
    (position += momentum / weight, and a dozen more), the boost strength (while boosting), a hover point's Z (one
    of the four was found to be a divisor, not which one, so all four). Each floor is a hundredth of
    the smallest value the disc holds, or less: weight 1 (the lightest part: 140), boost strength 0.1 (the least:
    11.2), a hover Z 0.01 from 0 (the nearest: 1). 60 times the boost duration, cut to a whole number, is kept in
    one byte: under 4.26. Max speed is in a divisor that stays positive from 0 up.
  - **Signs.** The disc holds no negative number but in the points. Negative values are allowed as long as
    the game still runs with them: every stat but the floors above may be, to minus its most, the
    same size either side of 0 as a part's. Kept: the weight (at least 1), the boost strength (0.1), a hover Z
    (0.01 from 0): divisors; the max speed (0: its divisor is shown to stay positive from 0 up, not below); the
    boost duration (0: 60 times it, cut to a whole number, is kept in a byte, which a negative wraps). A negative
    value has a meaning the physics gives it and nobody has studied it (a negative drag, a negative grip): what it
    does is the same in every game of the race. The warning says a negative value outside "Tried" where a stat
    has that range (0 to its number); for a stat whose "Tried" is the whole range nothing is said. The first pack
    to use it: F-Zero GX+'s Super Piranha, TurnDeceleration -0.02.
  - **Caps.** About ten times the largest value the disc holds for the stat, rounded. The argument: a term the
    physics makes of a machine's stats is then at most 10 to the power of the stats in it times its size on the
    disc's own machines; even a product of all 19 numbers is 19 orders of magnitude up, and a 32-bit number holds
    38. It is an argument about one step's products, not a proof about a race: nobody has read how each stat feeds
    back over many steps. What a wrong value does is the same in every game of the race (no desync); between
    "Tried" and a cap is ground nobody has raced, which the warning tells whoever makes a pack.
  - **Parts.** A custom part's block (41 to 115) is not a machine's stats: it is what the part adds to a custom
    machine, whose block is the sum of its three parts' (`fn_1_14F6F8`), and a part may take away what another
    gives. GX Unleashed's bodies and cockpits do: 108 of its 1,320 values are negative numbers in a part's block.
    So a part's number is held by its size alone: the bits of 0 or a normal number, no farther from 0 than a
    machine's most; no sign, no floor and no Z rule of its own. **Why that is safe in the build these rows freeze
    with**, read in the main module's code: the car data's address is formed in 18 places; two copy
    a machine's block, and only when the id is below 41 (`fn_1_82EDC` at 0x82EEC, racer creation `fn_1_83070` at
    0x83244: `cmpwi 41`, and at or above it the sum is called instead); the other 16 are the sum and the parts'
    own look-up beside it (`fn_1_14F608`, called from one place, the Garage's parts). The sum's four callers all
    stand behind an id of 41 or more or a garage record. A match's and a replay's machines are 0 to 40
    (`files_match.cpp`: a match's 30 bytes begin as 0 and only a listed seat's checked machine is written;
    `files_replay.cpp`: all 30 bytes of a replay's lineup are checked, the slots after its racers too), and a
    pack is laid for a match's race and a playback only, never in offline play. So no race of this build reads a
    part's block at all. **For the build that lets custom machines race**: **before custom machines
    race, the sum of the three parts must be checked by the machines' rules**. What must be sound is the sum, held
    to a machine's row; a per-part rule could not give it.
    Not read: the menu modules that show custom machines, `customize.rel` (the Garage) and `pilotpoint.rel`
    (two calls of `fn_1_14F6F8`, at 0x742C and 0x7540: the review's finding). No pack is laid while they run.
- **The import.** `Launcher.App --import-stat-pack CARDATA --pack-name "NAME" [--pack-author "WHO"] [--disc IMAGE]
  [--out FILE]`, without the window: reads the disc's car data (the image named, or the settings' disc; it must
  be the verified disc, by the pins the preparation holds an image to, unless `--any-disc`) and the changed file
  (`cardata.lz`, or unpacked),
  and writes the pack of every word that differs, in all 116 blocks, the two bytes and the points included, never
  word 0 (over a file already at the output's place only with `--overwrite`). It prints how many values differ
  and where it wrote. It refuses a file that is not car data (its length, a block's word 0 that is not 0, the zero
  bytes before the names), a file with no difference, and a value outside its range, naming it (the first one:
  `--report` in place of a name lists every difference with the disc's value, the file's, and what the ranges say
  of it, then the totals, and writes nothing). Names a changed file has changed are left out, and it says so. **A
  whole replacement file is thus just a large pack**: all 5,104 words at most, 30 KB as it travels (41 KB as the
  match start's text).
  `Launcher.Core/Files/StatPackImport.cs`. Status 0 written, 2 refused, 5 the output could not be written.
- **In the match start**: `setup.stats`, with `name`, `fingerprint` and `values` (base64 of the 6-byte values);
  what the game refuses of it, and the replay's version 3: `protocol/formats.md`, sections 1 and 2.
- **In the game** (`port/core/src/stat_pack.cpp`, called from the frame tick `boot::OnFrame` and from the go):
  - **Laid over the loaded table**, every frame from the boot on until the race runs: only the words that differ,
    never word 0, and only when the table stands as the loader leaves it (every block's name word in its place: a
    game that has not loaded the file yet is not written). Log: `fzgx: stat pack NAME (8 digits): N of its M values
    written over the game's car data`.
  - **Before the undo log starts.** The table lies in the memory a rollback puts back (below `STATE_END`), and the
    undo log starts at the race's first rollback frame: what was written before is what a rollback goes back to. So
    once the race runs nothing is written: the table is compared every frame, and a table that lost a value is
    said in the log, once (the game goes on: its machines are in the checksum).
  - **At the go** the race starts only if the table stands and holds every value of the pack. Otherwise `fzgx: stat
    pack: WHY: the race is refused; stopping`, and the game closes before its first step: its seat is the game's
    AI's, as for any game that closes. What the racers hold is said and never judged (the table and the checksum
    cover a pack that did not arrive): `... of N racers H hold their machines' weights from it and K another`, with
    the first racer that holds another (its block +0x08 against its machine's weight in the table; a racer whose
    weight is 0 is not filled yet and not counted).
  - **Nothing here stops a race for a reason that is not certain**: the game's side is in a game build before any
    server sends a pack, and a wrong refusal would need a new build.
  - **Two games with different packs** each start (a game knows only its own pack) and differ in the racers' blocks
    from the first step: the relay sees it at the first checksum it compares, as any desync. Which pack two players
    hold is not compared before the start, by design: a stock launcher hands on what
    the server sent, so only a changed launcher that drops or swaps the pack can cause it; that game desyncs itself
    and the relay reports it: detected, never repaired, as any desync.
  - A race without a pack touches not one byte of the table. A playback lays its recording's pack the same way.
- **The proving run**: `LAUNCHER=NAME stat-pack-run.sh RUN` (one of the project's test scripts, not public; about 45
  minutes; `ONLY=PART` runs and judges one part), on packs it makes up from the disc's car data and on the
  repository's own.
  **Run sp4, on the rows above: two parts, both passed** (runner 2c5ca762, built at 3fd78ae with the
  parts' rule and the replay lineup's check; module w12, image aa956d74; one PC, course 16, one lap):
  - **GX Unleashed, two games** (`ONLY=real`, 13 of 13 lines). The pack read from its file a third way, by the
    run's own script: the fingerprint the file's head says. Both players on the Blue Falcon among 6 simulated
    players and 22 computer racers at CPU level 3, a rollback forced every 7 frames to depths 3, 9 and 17 (235 and
    236 frames with one), a test lag of 30 ms: all 1,320 values written and held at the go in both games, 30 of 30
    racers on their machines' weights (the Blue Falcon's 1,620, the disc's 1,260), 2,686 steps in both recordings
    with every checksum equal, both version 3 and played back with 0 of 2,804 and 2,686 steps differing.
  - **Three packs refused, each by the launcher and by the game** (`ONLY=refused`, 7 of 7 lines): a machine's
    weight of the bits 00000001; a machine's Grip1 of -0.5 (`must be at least 0`: a machine keeps its row); a
    body's Grip2 of -10.5 (`a part's value is at most 10 from 0`: a part is held by its size). The launcher,
    handed each in a match file, started no game; the game, handed each past the launcher, set up no race.
  The rest was not run again on these rows: it is run sp3's, below, on the rows before the parts' rule.
  **Run sp3 passed** (50 of 50 lines; the same script before the `real` part). This
  build: runner 98de8b6e (the branch at a1c5553), module w12, image aa956d74. "The older build": a players'
  build before stat packs, runner 672749d2, with no stat pack code in it. All on one PC, course 16, one lap:
  - **No pack, one game.** The gates' match race (29 computer racers) against the JIT reference's kept run: 2,430
    of 2,430 steps' machine hashes equal, the recording version 2, no line of a stat pack in the log.
  - **No pack, two games and rollbacks.** Two games of this build among 6 simulated players and 22 computer racers
    (CPU level 3), a rollback forced every 7 frames to depths 3, 9 and 17 (263 and 261 frames with one), a test lag
    of 30 ms: version 2 recordings, no line of a stat pack, 2,932 steps in both with every checksum equal, and both
    played back on the older build with 0 of 2,934 and 2,932 steps differing. The other way round: two recordings
    the older build made (two launchers, 4 simulated players, 24 computer racers) played back on this
    build with a rollback forced every 2 frames (2,398 in each, to 17 deep): 0 of 5,836 and 6,015 steps differing.
  - **Two games on one pack** (A: the Blue Falcon's weight doubled, one of its hover points moved, the first body
    heavier; both players on the Blue Falcon) among 6 simulated players and 22 computer racers at CPU level 3,
    rollbacks forced as above (399 frames in each game): the pack laid in both, 30 of 30 racers on their machines'
    weights at the go, 2,520 in the racers' blocks before steps 300 and 301 (the disc's: 1,260), 3,530 steps in both
    recordings with every checksum equal, both version 3 and played back with 0 steps differing.
  - **Two games handed different packs** (the weight doubled, and tripled; 28 simulated players): both started,
    their recordings' checksums differ from step 0, and the test server's relay said desync at frame 59, the first
    checksum it compares.
  - **A pack from a server.** A test server started with `--stat-pack` on pack W's file (a whole file's 5,104
    values, the two bytes among them; every machine 2% heavier and one byte at 0x48 changed, so 42 differ from the
    disc), `--cpu-fill always --cpu-level 1`; two launchers through Quick Play (the lobby, the start vote, the match
    start, the game), a test lag of 50 ms, 28 computer racers. The server named W at its start; both games wrote
    the 42 and held all 5,104 at the go, with 30 of 30 racers on their machines' weights (1,285.2 on the Blue
    Falcon); 3,140 steps in both replays with every checksum equal, both version 3 and played back with 0 steps
    differing. Only 3 frames in each game had a rollback (two players whose AI seldom changes its input): the
    rollbacks of a race on a pack are the forced ones of the race before.
  - **Refused.** A pack with a weight of the bits 00000001 and one with a Grip1 of -0.5. The launcher, handed each
    in a match file (`--play-match`), refused with its sentence and started no game; the game, handed each past the
    launcher (`stat-pack-refused.sh`), said `the race's stat pack is refused` with the value, and set up no race.
  - The launcher's import of the changed car data file gave the fingerprint the script had worked out by itself.
  - And by hand: the older build, given a version 3 recording, says `is not a version 2 replay file` and plays
    nothing.
  **Not shown**: one run each, and no gate row. No race with a pack on two PCs or on the online service. Computer
  racers on a pack at levels 3 and 1 only, at the one machine setting the server sends; at both levels every racer
  held its machine's weight from the table. A match start refused inside Quick Play (`QuickPlaySession`) is in the
  launcher's tests only; the game run refuses through `--play-match`. No pack with values near the caps. The
  real pack has raced only as above: one race, two games on one PC, both players on one machine.
- **Looked at**: a real launcher window in a lobby of a local server on GX Unleashed with five simulated players
  (no game), and the window drawn without a screen on a led server. The pack's label is on the course band; the
  Garage's stats name the pack at their head.
