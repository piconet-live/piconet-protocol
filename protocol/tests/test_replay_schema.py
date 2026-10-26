"""The replay schema's rules (replay.toml, replay.py): what it may say, and what the generated Python reader makes of
the shared samples. The C++ and C# readers over the samples: port/core/tests/test_replay_vectors.cpp and
tests/dotnet-replay (check.sh). No game, no build."""
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
folder = os.path.dirname(here)
sys.path.insert(0, folder)
sys.path.insert(0, os.path.join(folder, "generated"))
import replay  # noqa: E402
import replay_file  # noqa: E402

with open(replay.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()
SAMPLES = os.path.join(here, "vectors", "replays")


class ReplaySchema(unittest.TestCase):
    def error(self, old, new):
        self.assertIn(old, SCHEMA)
        with tempfile.TemporaryDirectory() as temp:
            path = os.path.join(temp, "replay.toml")
            with open(path, "w", encoding="utf-8") as f:
                f.write(SCHEMA.replace(old, new, 1))
            with self.assertRaises(replay.SchemaError) as raised:
                replay.load(path, 6)
        return str(raised.exception)

    def test_the_schema_loads(self):
        s = replay.load(record_size=6)
        self.assertEqual([v["number"] for v in s["versions"]], [2, 3, 4, 5, 6, 7])
        self.assertEqual((s["written"], s["head_size"], s["frames_size"], s["lineup_size"]), (7, 10, 12, 124))
        self.assertEqual([m.key for m in s["result"]], ["seat", "rank", "finished", "out", "laps", "timeMs", "bestLapMs"])

    def test_broken_rules_are_named(self):
        self.assertIn("written is one of the versions", self.error("written = 7", "written = 8"))
        self.assertIn("the first has no feature", self.error('name = "Stats"\nfeature = "stats"\n', 'name = "Stats"\n'))
        self.assertIn("rising order", self.error("number = 3", "number = 9"))
        self.assertIn("four ASCII", self.error('mark = "FZRF"', 'mark = "FZR"'))
        self.assertIn("marks differ", self.error('mark = "FZRL"', 'mark = "FZRF"'))
        self.assertIn("Version (u16), then HeaderLength", self.error('{ name = "Version", type = "u16" }', '{ name = "Version", type = "u32" }'))
        self.assertIn("least within the block", self.error("least = 122", "least = 125"))
        self.assertIn("the default is in the range", self.error("default = 2\n", "default = 61\n"))
        with self.assertRaises(replay.SchemaError):
            replay.load(record_size=None)   # a step's records are the relay's

    def test_the_version_a_race_makes(self):
        self.assertEqual(replay_file.version_of(), 2)
        self.assertEqual(replay_file.version_of(stats=True), 3)
        self.assertEqual(replay_file.version_of(stats=True, custom=True), 4)
        self.assertEqual(replay_file.version_of(custom=True, mines=True), 5)
        self.assertEqual(replay_file.version_of(mines=True, seat_packs=True), 6)
        self.assertEqual(replay_file.version_of(stats=True, seat_packs=True, grid=True), 7)
        with self.assertRaises(ValueError):
            replay_file.version_of(band=True)

    def test_the_python_reader_over_the_samples(self):
        with open(os.path.join(SAMPLES, "replays.tsv"), encoding="utf-8") as f:
            rows = [line.rstrip("\n").split("\t") for line in f if line.strip() and not line.startswith("#")]
        self.assertEqual(sorted({int(r[1]) for r in rows}), [2, 3, 4, 5, 6, 7])
        for r in rows:
            with open(os.path.join(SAMPLES, r[0] + ".hex"), encoding="ascii") as f:
                got = replay_file.parse(bytes.fromhex(f.read().replace("\n", "")))
            self.assertEqual((got.version, got.header_length, got.mask, got.seat), tuple(int(c) for c in r[1:3] + r[4:6]))
            self.assertEqual((len(got.steps), got.whole, got.has_lineup, got.results is not None),
                             (int(r[12]), r[13] == "1", r[6] != "-", r[16] != "-"))
            if got.has_lineup:
                lineup = replay_file.lineup(got.lineup)
                self.assertEqual((bytes(lineup["machines"]).hex(), lineup["control_mode"]), (r[7], int(r[10])))


if __name__ == "__main__":
    unittest.main()
