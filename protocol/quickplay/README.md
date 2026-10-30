# protocol/quickplay

Quick Play's rotation (piconet-docs `research/game-modes/`, design 6): `quickplay.toml`, the coordinator's list of
mode files, and the official mode files it names. A vote card is a (course, mode) pair: five course cards, exactly
one of them spicy, and Random, which lands on a spicy option at `random_spicy_chance`. `Online.Protocol`
`QuickPlayRotation` reads the folder and compiles each mode with the one compiler (`ModeCompiler`); the service reads
it at its start (a file that does not load stops the start), and the launcher's stand-in service races the same.

| File | What it is |
|---|---|
| `quickplay.toml` | The rotation: the cards, the normal option and the spicy options |
| `competitive.fzmode` | The normal option: the game's own rules with the competitive scene's laps (`laps by course`) |
| `knockout.fzmode` | A spicy option: a rolling start, then the last racer is out every interval |

Knockout's `board` lines (language 10) are comments: the compiler has no `board` yet.
