# protocol/quickplay

Quick Play's rotation (piconet-docs `research/game-modes/`, design 6): `quickplay.toml`, the coordinator's list of
mode files, and the official mode files it names. A vote card is a (course, mode) pair: five course cards, exactly
one of them spicy, and Random, which lands on a spicy option at `random_spicy_chance`. `Online.Protocol`
`QuickPlayRotation` reads the folder and compiles each mode with the one compiler (`ModeCompiler`); the service reads
it at its start (a file that does not load stops the start), and the launcher's stand-in service races the same.

| File | What it is |
|---|---|
| `quickplay.toml` | The rotation: the card counts, the normal option and the spicy options with their weights. The courses are each mode file's `courses` word (left out: the pool) |
| `competitive.fzmode` | The normal option: the game's own rules with the competitive scene's laps (`laps by course`), on every course of the pool |
| `knockout.fzmode` | A spicy option: a rolling start, then the last racer is out every interval, on Lightning: Loop Cross |
| `practice.fzmode` | Not in the rotation: Play's Practice, raced alone (`solo`); the launcher starts it itself, beside the program in this folder |
| `time_trial.fzmode` | Not in the rotation: a time trial, raced alone (`solo`); no launcher page starts it yet |
| `ghost_race.fzmode` | Not in the rotation: a time trial raced together: everyone starts on one spot, nobody touches anybody, the others drawn as ghosts, the laps a parameter; no launcher page lists it yet |
