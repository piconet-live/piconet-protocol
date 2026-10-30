# Online.Protocol

The messages of the three links, and nothing that acts on them: what the server, the bots and the launcher send each
other. Each link has a version number; an optional JSON field needs no new version, an incompatible change does
(`handbook/service-protocol.md` lists every message and who sends it).

| File | Its one job |
|---|---|
| `Messages.cs` | Launcher and coordinator, JSON over a WebSocket: `ControlProtocol` (version 3, path `/ws`, encode and decode), every `ClientMessage` and `ServerMessage`, the race set-up and the server's rules |
| `RelayControl.cs` | Relay and coordinator, JSON over a WebSocket: `RelayControl` (version 1, path `/relay`) and its messages |
| `LiveRace.cs` | The live report of a race: one racer's snapshot of all racers, and what a lobby shows of its race |
| `ServiceStatus.cs` | The JSON of `/api/status` |
| `Lan.cs` | A LAN server's announcement (UDP 47800) |
| `PlayerNames.cs` | Which names the game can draw |
| `PlayerTags.cs` | A player's tag ("NAME #4F2A"): its alphabet, a new one from the cryptographic source, the key that makes a name and tag one player's |
| `PlayersList.cs` | The live list of connected players (`players`, `playersChanged`) and a rename's new tag (`tag`); all optional, behind the hello's `players` |
| `Notices.cs` | A notice from the server's operator: the sender id only the server uses, the four tiers, the tier's word in front of the text for launchers that know no tier |
| `Pings.cs` | Each player's ping in the lobby: the coordinator's question and the launcher's answer (`ping`, `pong`), the list the lobby's members are sent (`pings`); all optional, behind the hello's `pings` |
| `CourseNames.cs`, `CoursePool.cs` | Course names by the game's course number; the courses a server races unless told others |
| `HostRules.cs` | The one list of a host's rules: the server's rule names (its live rules take these and no others), each with a player's label, whom it concerns (alone, together, the server's own), its range and Quick Play's value; a practice shows the rules alone, a custom lobby will show these too |
| `StatPack.cs`, `StatPackRules.cs`, `StatPackText.cs` | A stat pack (`handbook/stat-packs.md`): its checked values, fingerprint and form in a match start; the ranges; the pack file's text read and written |
| `Shared/` | The few helpers the launcher and the service (server, bots) both use, one way each: `AtomicFile` (a file written whole or not at all), `RelayPing` (the round trip to a relay) |
| `GameMachines.cs` | The game's machines: how many, which ids are one, their names by id (the one list), which ones the computer drives |
| `Chat/` | The chat's text matching, shared by the server's filter and the launcher's mask: `TextFold` (case, accents, look-alikes), `WordList` (a term found however written, whole words only), `ChatLinks`, `ChatRefusal` (the codes and labels), `ChatWords` (the shipped lists, built in: `blocked.txt`, `swears.txt`, MIT `LICENSE-profanity-list.txt`) |
| `Modes/` | The game modes' one compiler (piconet-docs `research/game-modes/`, D11): `ModeCompiler.Compile`, a `.fzmode` file to its package (formats.md section 9) with its hash, share code, static budget and messages ("line N: ..."); `ModePackage`, a package read and checked (as the service takes an upload) and the match start's `setup.mode` (`ModeStart`); `ModeDisassembler`, a package as text; `ModeMaths`, the game's square root as the bytecode has it. The standard library, `std.fzmode`, is embedded and compiled with every file (`ModeLibrary`: its macros, expanded by the parser, and its subs). Inside: `ModeLexer`, `ModeParser` (the tree, `ModeSyntax`), `ModeNames` (declarations, constants), `ModeParams`, `ModeEmitter` (types, bytecode and the budget; a sub written where it is called), `ModeSubCalls` (a sub's calls, for the refusal of a cycle), `ModeCode` (the operations' operands, the code's check) |
| `ModeWords.g.cs` (in `protocol/generated/`, compiled in by the project file) | The modes' vocabulary and bytecode, generated from `protocol/modes.toml` (never edited by hand) |
| `RelayPackets.g.cs` (in `protocol/generated/`, compiled in by the project file) | Game and relay, UDP: `RelayProtocol` (encode and decode) and the packets, generated from `protocol/relay.toml` (never edited by hand; `protocol/README.md`) |
| `GameFacts.g.cs` (in `protocol/generated/`, compiled in by the project file) | Generated from `port/facts/game.tsv` (never edited by hand; the stat pack's files use its car data layout, `port/facts/README.md`) |

## What may depend on what

- This project refers to no other project: not the server, not the bots, not the launcher. `Online.Server`,
  `Online.Bots`, `Launcher.Core` and the tests refer to it.
- A type here carries data and its encoding (and `Shared/`'s helpers). A rule (who may send what, when) belongs to
  the program that enforces it.
- The game speaks the UDP link from C++ (`port/core`) by the header generated beside: a packet changes in
  `protocol/relay.toml` (both codecs written again by `protocol/generate.py`), with a new `version`.

## Tests

`net/tests/Protocol.Tests` (the UDP codec, the stat packs, `AtomicFile`), `net/tests/Online.Tests` (`GameFactsTests`, the
messages), `protocol/check.sh` (the codecs' round trip). One class: `cd net && dotnet test --project tests/Protocol.Tests --filter-class Protocol.Tests.PacketTests`.
