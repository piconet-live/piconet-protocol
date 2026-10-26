# Contributing to piconet-protocol

This repository holds the file formats and messages the other parts share.

## Building

The [README](README.md) lists the requirements, with versions, and how to build and check this repository. Clone it
with `--recurse-submodules`.

## Making a change

1. Fork the repository and make a branch for your change.
2. Keep a pull request to one change, with a message that says what it changes and why.
3. Open the pull request against `main`. `protocol/check.sh` and the tests under `net/` must pass, and so must CI.

A change to a file format or a message starts here. Change its definition in `protocol/`, run
`protocol/generate.py`, and commit the generated files with it; the other parts then take the change with a
submodule bump.

Please discuss bigger changes on the [Discord](https://discord.gg/DcauFmsGh) before you start on them. Bugs and
small fixes can go straight to an issue or a pull request.

## Licence

piconet-protocol is licensed under GPL-3.0-or-later. By opening a pull request you agree that your contribution is
licensed under the same terms.
