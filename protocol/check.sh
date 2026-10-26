#!/usr/bin/env bash
# The protocol folder's checks: the generated files are current, the generator's rules hold, the generated relay
# codecs give the vectors' packets and bytes in both directions (relay version 3 as 0.4.0 speaks it), and the status
# board's two views agree and hold under stress (README.md). No game, no game build. Needs python3 (3.11+), g++
# (C++20) and the .NET 10 SDK. Every path is this folder's own.
#   check.sh          everything
#   check.sh board    the generated files and the status board's part only
set -euo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
temp=$(mktemp -d)
trap 'rm -rf "$temp"' EXIT

echo "check: protocol generated files"
python3 "$here/generate.py" --check

echo "check: protocol status board (C++: layout, seqlock, ring, version, torn reads by two threads and two processes)"
timeout 300 python3 -W error -m unittest -q "$here/tests/test_board.py"
g++ -std=c++20 -O1 -Wall -Wextra -Werror -I"$here/generated" "$here/tests/cxx/test_board.cpp" -o "$temp/test_board"
timeout 120 "$temp/test_board"
echo "check: protocol status board (C#; each language reads the board the other wrote; a C++ game process, a C# reader)"
dotnet build -v q -nologo "$here/tests/dotnet-board/BoardCheck.csproj" -o "$temp/board-cs" > "$temp/board-build.txt" ||
    { tail -20 "$temp/board-build.txt" >&2; exit 1; }
"$temp/test_board" --write "$temp/cxx.board"
timeout 120 dotnet "$temp/board-cs/BoardCheck.dll"
timeout 60 dotnet "$temp/board-cs/BoardCheck.dll" --read "$temp/cxx.board"
timeout 60 dotnet "$temp/board-cs/BoardCheck.dll" --write "$temp/cs.board"
timeout 60 "$temp/test_board" --read "$temp/cs.board"
timeout 120 dotnet "$temp/board-cs/BoardCheck.dll" --live "$temp/test_board"
[ "${1:-}" != board ] || exit 0

echo "check: protocol stat tables' C++ headers, on their own"
printf '#include "stat_ranges.g.h"\n#include "const_ranges.g.h"\nint main() { return fzgx::stats::RULES.size() == 22 && fzgx::consts::CONSTANTS.size() == 62 ? 0 : 1; }\n' |
    g++ -std=c++20 -Wall -Wextra -Werror -I"$here/generated" -x c++ - -o "$temp/stat_tables"
"$temp/stat_tables"

echo "check: protocol generator tests"
timeout 300 python3 -W error -m unittest discover -s "$here/tests" -q

echo "check: protocol relay codecs' round trip over the vectors (C#, C++)"
dotnet build -v q -nologo "$here/tests/dotnet/RelayRoundTrip.csproj" -o "$temp/relay-cs" > "$temp/relay-build.txt" ||
    { tail -20 "$temp/relay-build.txt" >&2; exit 1; }
timeout 120 dotnet "$temp/relay-cs/RelayRoundTrip.dll" "$here/tests/vectors/relay.tsv"
g++ -std=c++20 -O1 -Wall -Wextra -Werror -I"$here/generated" -I"$here/tests/generated" \
    "$here/tests/cxx/test_relay_roundtrip.cpp" -o "$temp/test_relay_roundtrip"
timeout 120 "$temp/test_relay_roundtrip" "$here/tests/vectors/relay.tsv"

echo "check: protocol replay readers over the shared samples (C#; the C++ reader's: port/core/tests/test_replay_vectors.cpp)"
dotnet build -v q -nologo "$here/tests/dotnet-replay/ReplayVectors.csproj" -o "$temp/replay-cs" > "$temp/replay-build.txt" ||
    { tail -20 "$temp/replay-build.txt" >&2; exit 1; }
timeout 120 dotnet "$temp/replay-cs/ReplayVectors.dll" "$here/tests/vectors/replays"
