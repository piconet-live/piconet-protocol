# protocol/quickplay

Quick Play's rotation (piconet-docs `research/game-modes/`, design 6): `quickplay.toml`, the coordinator's list of
mode files, and the official mode files it names, and a custom lobby's official modes. A mode file is one choice: its settings are its parameters on their defaults, which a custom lobby's host changes (Quick Play's normal mode: its laps, CPU level, boost, mines, finish limit, machine setting and stats); a variant is a file of its own. Every rule of the race is the mode's: a word left out is the game's own way. A vote card is a (course, mode) pair: five course cards, two
of them spicy (two different spicy options, each on its own course), and Random, which lands on a spicy option at `random_spicy_chance`. `Online.Protocol`
`QuickPlayRotation` reads the folder and compiles each mode with the one compiler (`ModeCompiler`); the service reads
it at its start (a file that does not load stops the start), and the launcher's stand-in service races the same.

Every word a mode file can use: [MODE-REFERENCE.md](../../MODE-REFERENCE.md).

| File | What it is |
|---|---|
| `quickplay.toml` | The rotation: the card counts, the normal option and the spicy options with their weights. The courses are each mode file's `courses` word (left out: the pool). Also `lobby_modes`: a custom lobby's official modes in the order a host is offered them (Quick Play's normal mode, then the others); the server lists those it can race (`LobbyList.Modes`) |
| `competitive.fzmode` | The normal option: the game's own rules with the competitive scene's laps (`laps by course`), on every course of the pool; three lives each (`lives`: every wreck the game restores costs one, as in Death Race; the last is final); its parameters (Laps "By course" or a number, CPU level, boost, mines, finish limit, machine setting, stats, CPU racers, Lives 1 to 9) are what a custom lobby's host may change, on Quick Play's values by default |
| `knockout.fzmode` | A spicy option: a rolling start, then the last racer is out every interval, on any course of the pool |
| `practice.fzmode` | Not in the rotation: Play's Practice, raced alone (`solo`); the launcher starts it itself, beside the program in this folder |
| `time_trial.fzmode` | Not in the rotation: a time trial, raced alone (`solo`); no launcher page starts it yet |
| `ghost_race.fzmode` | A spicy option: a time trial raced together: everyone starts on one spot, nobody touches anybody, the others drawn as ghosts; on any course of the pool, with Quick Play's laps by course (its own copy of `competitive.fzmode`'s table, kept alike by `QuickPlayRotationTests`; Laps "By course" or a number); a custom lobby's host may choose it |
| `overdrive.fzmode` | A spicy option: a rolling start at 1000 km/h, then every machine's speed is locked and rises 10 km/h every 10 s (Speed added every 10 s, 5 to 50), a little more every step; the accelerator, the brake and boost do nothing; machines are out the normal way (energy, off the course) and the last one in wins; places by survival (the winner, then the racers out, last out first) on a times list headed OVERDRIVE; no clock; on any course of the pool; a custom lobby's host may choose it |
| `death_race.fzmode` | Not in the rotation (its `course` word pins Big Blue: Drift Highway): F-Zero X's Death Race, no finish line, three lives each (the game restores a wreck; the last is final), boosts at half their energy (seat.stat.boost_energy), on a six-minute clock; points for every KO the game credits, for staying in and for each life left, a bonus for the first three still in; damage 400% with `damage_cap off`; a custom lobby's host may choose it |
