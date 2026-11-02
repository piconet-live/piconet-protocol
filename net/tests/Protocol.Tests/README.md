# Protocol.Tests

The tests of `Online.Protocol` alone (xunit v3; the test project is a program): the relay's packets and the stat
packs. They refer to no other project, so they go with the protocol to its own repository. The protocol's tests that need a server (the generated facts, a server's
pack) stay in `Online.Tests`.

| Files | What they test |
|---|---|
| `PacketTests`, `RelayPacketSizeTests` | Relay packets encode and decode; an official packet for thirty seats stays under the MTU |
| `StatPackTests`, `StatPackRulesTests`, `StatPackTextTests` | A stat pack as it travels; its ranges against `handbook/stat-packs.md`'s table and the list of cases `protocol/tests/vectors/stat-cases.tsv` (the game's tests read both too); its file and its author line |
| `ModeCompilerTests` | The game modes' compiler: the vectors of `protocol/tests/vectors/modes/` byte for byte (the package, its listing, its `setup.mode`; `FZGX_WRITE_MODE_VECTORS=1` writes them again), a package read back, the hash and share code, the words vector holding every operation and word, and the errors by their line |
| `QuickPlayRotationTests` | Quick Play's rotation (`QuickPlayRotation`) over `protocol/tests/vectors/quickplay/`: five cards with one spicy and Random, the spicy card at every place, Random's spicy chance, the laps and the CPU flag, the refusals of a file that does not load, the official `protocol/quickplay/` (loads, every card compiles, laps by course, the Knockout card, Random), and the mode on the wire (match start, vote cards, hello) |
| `ModeLanguageTests` | The language past the first slice: the standard library and its macros, subs, waits, teams, sets, vecs and random numbers, each one's load-time errors by their line, the budget of a rule that waits, and the game's square root (`sqrt.tsv`) |
| `ModeLimitsTests` | A hostile mode file: each shape refused with its line on a 256 KB stack and on a pool thread of a program whose pool has that stack (this program run again); where the nesting limit falls; deep JSON; the shipped modes' hashes |
| `AtomicFileTests` | A file written whole (`Shared/AtomicFile.cs`): a failed write leaves the file as it was and nothing beside |
| `StatPackConstantsTests` | A pack's code constants: the list against `handbook/stat-pack-constants.md`'s table (the game's `const_ranges.h` is held to the same table by its own test), as a file and on the wire, what is refused, GX Unleashed of `protocol/packs` |

## Run

```
dotnet build net/net.slnx --disable-build-servers -nodeReuse:false -v quiet -nologo
(cd net && dotnet test --project tests/Protocol.Tests --no-build)
```

The repository's check (README.md) runs them after `protocol/check.sh`.
