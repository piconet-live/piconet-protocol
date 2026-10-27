# piconet-protocol

[![CI](https://github.com/piconet-live/piconet-protocol/actions/workflows/ci.yml/badge.svg)](https://github.com/piconet-live/piconet-protocol/actions/workflows/ci.yml)

The formats and messages Piconet's parts share, each described once: the relay's UDP packets, the lobby and
relay-link messages, the game's launch file, the runner's exit status, the replay file (`.fzrp`) and the status
board a running game shares with the launcher. C# and C++ are generated from the definitions in `protocol/`;
`Online.Protocol` is the C# library the launcher, the server and the simulated players build with, and the stat
packs that are shipped are kept here. Piconet is online multiplayer for F-Zero GX: see
[github.com/piconet-live](https://github.com/piconet-live).

This repository is a submodule of the others (`external/protocol` in
[piconet-runner](https://github.com/piconet-live/piconet-runner),
[piconet-launcher](https://github.com/piconet-live/piconet-launcher) and the service), which reach it through one
property each (`ProtocolFolder` in `net/Directory.Build.props`, `REPO_PROTOCOL` in the runner's
`port/setup/pins.tsv`). A change to a file format or a message lands here first; each part then takes it with a
submodule bump.

No game data is stored here.

Paths under `port/` named in this repository's comments and pages are piconet-runner's, and `net/src/Launcher.*`
paths piconet-launcher's; the service's (`Online.Server`, `deploy/`) and the project's test scripts are not public.

## Layout

| Folder or file | What it is |
|---|---|
| `protocol/` | The definitions (`relay.toml`, `launch.toml`, `replay.toml`, `board.toml`), the generator (`generate.py`), the generated files, the file formats (`formats.md`), the shipped stat packs (`packs/`) and their checks: [protocol/README.md](protocol/README.md) |
| `net/src/Online.Protocol/` | The C# messages and file formats, and the helpers the launcher and the service share |
| `net/tests/Protocol.Tests/` | Its tests |
| `handbook/stat-packs.md`, `handbook/stat-pack-constants.md` | The stat packs' rules and constants, for modders; the tests on every side read them |
| `handbook/service-protocol.md` | The messages of the three links |

## Requirements

- Python 3.11 or later (standard library only) and g++ with C++20 (checked with GCC 13.3): `sudo apt install
  build-essential python3 python3-venv rsync git`
- the .NET SDK 10.0.401 or a later 10.0 patch (`net/global.json`). Ubuntu's apt package (10.0.1xx) is
  older, so install it with Microsoft's script, then put it on `PATH`:

      curl -sSLO https://dot.net/v1/dotnet-install.sh && bash dotnet-install.sh --channel 10.0
      export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH    # in ~/.bashrc too

  In WSL, Windows' `dotnet.exe` can come first on `PATH`; `command -v dotnet` should name `~/.dotnet/dotnet`.

## Check

    protocol/check.sh                                   # generated files current, generator tests, codecs, board, replays
    (cd net && dotnet test --project tests/Protocol.Tests)

`protocol/generate.py` writes the generated files again after a change to a definition; `--check` writes nothing
and fails when one is stale.

## Contributing

Fork the repository, make a branch, and open a pull request; the check above must pass. Please discuss bigger
changes on Piconet's [Discord](https://discord.gg/DcauFmsGh) first. The details are in
[CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

GPL-3.0-or-later (`LICENSE`). The profanity lists in `net/src/Online.Protocol/Chat/` are MIT, from
dsojevic/profanity-list (their licence beside them); the stat packs in `protocol/packs/` are published with their
authors' permission, as each file's head says.
