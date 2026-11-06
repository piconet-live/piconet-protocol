# protocol/quickplay

Quick Play's rotation (piconet-docs `research/game-modes/`, design 6): `quickplay.toml`, the coordinator's list of
mode files, and the official mode files it names, and a custom lobby's official modes. A mode file is one choice: its settings are its parameters on their defaults, which a custom lobby's host changes (Quick Play's normal mode: its laps, CPU level, boost, mines, finish limit, machine setting and stats); a variant is a file of its own. Every rule of the race is the mode's: a word left out is the game's own way. A vote card is a (course, mode) pair: five course cards, exactly
one of them spicy, and Random, which lands on a spicy option at `random_spicy_chance`. `Online.Protocol`
`QuickPlayRotation` reads the folder and compiles each mode with the one compiler (`ModeCompiler`); the service reads
it at its start (a file that does not load stops the start), and the launcher's stand-in service races the same.

| File | What it is |
|---|---|
| `quickplay.toml` | The rotation: the card counts, the normal option and the spicy options with their weights. The courses are each mode file's `courses` word (left out: the pool). Also `lobby_modes`: a custom lobby's official modes in the order a host is offered them (Quick Play's normal mode, then the others); the server lists those it can race (`LobbyList.Modes`) |
| `competitive.fzmode` | The normal option: the game's own rules with the competitive scene's laps (`laps by course`), on every course of the pool; its parameters (Laps "By course" or a number, CPU level, boost, mines, finish limit, machine setting, stats) are what a custom lobby's host may change, on Quick Play's values by default |
| `knockout.fzmode` | A spicy option: a rolling start, then the last racer is out every interval, on Lightning: Loop Cross |
| `practice.fzmode` | Not in the rotation: Play's Practice, raced alone (`solo`); the launcher starts it itself, beside the program in this folder |
| `time_trial.fzmode` | Not in the rotation: a time trial, raced alone (`solo`); no launcher page starts it yet |
| `ghost_race.fzmode` | Not in the rotation: a time trial raced together: everyone starts on one spot, nobody touches anybody, the others drawn as ghosts, the laps a parameter; a custom lobby's host may choose it |
| `death_race.fzmode` | Not in the rotation (its `course` word pins Big Blue: Drift Highway): F-Zero X's Death Race, no finish line, three lives each (the game restores a wreck; the last is final), on a six-minute clock; points for every KO the game credits, for staying in and for each life left, a bonus for the first three still in; damage 400% with `damage_cap off`; a custom lobby's host may choose it |
