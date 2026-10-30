"""The replay schema's rules (replay.toml, replay.py): what it may say, and what the generated Python reader makes of
the shared samples. The C++ and C# readers over the samples: port/core/tests/test_replay_vectors.cpp and
tests/dotnet-replay (check.sh). No game, no build."""
import os
import struct
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
        self.assertEqual([v["number"] for v in s["versions"]], [2, 3, 4, 5, 6, 7, 9])
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
        self.assertIn("a number no [[version]] has", self.error("version = 8 ", "version = 9 "))
        self.assertIn("[compact]: missing", self.error("full_every = 60", "every = 60"))
        self.assertIn("marks differ", self.error('inputs_mark = "FZRI"', 'inputs_mark = "FZRH"'))
        with self.assertRaises(replay.SchemaError):
            replay.load(record_size=None)   # a step's records are the relay's

    def test_the_version_a_race_makes(self):
        self.assertEqual(replay_file.version_of(), 2)
        self.assertEqual(replay_file.version_of(stats=True), 3)
        self.assertEqual(replay_file.version_of(stats=True, custom=True), 4)
        self.assertEqual(replay_file.version_of(custom=True, mines=True), 5)
        self.assertEqual(replay_file.version_of(mines=True, seat_packs=True), 6)
        self.assertEqual(replay_file.version_of(stats=True, seat_packs=True, grid=True), 7)
        self.assertEqual(replay_file.version_of(stats=True, seat_packs=True, grid=True, mode=True), 9)
        with self.assertRaises(ValueError):
            replay_file.version_of(band=True)

    def test_the_python_reader_over_the_samples(self):
        with open(os.path.join(SAMPLES, "replays.tsv"), encoding="utf-8") as f:
            rows = [line.rstrip("\n").split("\t") for line in f if line.strip() and not line.startswith("#")]
        self.assertEqual(sorted({int(r[1]) for r in rows}), [2, 3, 4, 5, 6, 7, 8])
        for r in rows:
            with open(os.path.join(SAMPLES, r[0] + ".hex"), encoding="ascii") as f:
                got = replay_file.parse(bytes.fromhex(f.read().replace("\n", "")))
            self.assertEqual((got.version, got.header_length, got.mask, got.seat), tuple(int(c) for c in r[1:3] + r[4:6]))
            self.assertEqual((len(got.steps), got.whole, got.has_lineup, got.results is not None),
                             (int(r[12]), r[13] == "1", r[6] != "-", r[16] != "-"))
            if got.has_lineup:
                lineup = replay_file.lineup(got.lineup)
                self.assertEqual((bytes(lineup["machines"]).hex(), lineup["control_mode"]), (r[7], int(r[10])))


    def sample(self, name):
        with open(os.path.join(SAMPLES, name + ".hex"), encoding="ascii") as f:
            return bytes.fromhex(f.read().replace("\n", ""))

    def test_the_compact_sample_is_the_v7_sample_made_compact(self):
        whole, compact = self.sample("v7.fzrp"), self.sample("v8.fzrp")
        self.assertEqual(replay_file.compact(whole), compact)
        a, b = replay_file.parse(compact), replay_file.parse(whole)
        self.assertEqual((a.version, a.race_version, a.compact, b.race_version, b.compact), (8, 7, True, 7, False))
        self.assertEqual((a.header, a.lineup, a.results, a.mask, a.seat), (b.header, b.lineup, b.results, b.mask, b.seat))
        self.assertEqual([row for row, _ in a.steps], [row for row, _ in b.steps])
        n = len(a.steps)
        self.assertEqual([t for t, (_, c) in enumerate(a.steps) if c.full], [0, 60, 120, 180, n - 1])
        for (_, x), (_, y) in zip(a.steps, b.steps):
            self.assertEqual(x, y)          # a compact step equals the whole one (folded or whole)
            self.assertEqual(bytes(x) == bytes(y), x.full)

    def test_every_sample_made_compact_reads_as_itself(self):
        for name in ("v2.fzrp", "v3.fzrp", "v4.fzrp", "v5.fzrp", "v6.fzrp", "v7.fzrp"):
            whole = self.sample(name)
            back = replay_file.parse(replay_file.expand(replay_file.compact(whole)))
            b = replay_file.parse(whole)
            self.assertEqual((back.version, back.header, back.lineup, back.results), (b.version, b.header, b.lineup, b.results))
            self.assertEqual([row for row, _ in back.steps], [row for row, _ in b.steps])
        with self.assertRaises(ValueError):
            replay_file.compact(self.sample("v2-no-steps.fzrp"))   # no step: nothing to make compact

    def test_a_checksum_compares_by_its_folded_byte_where_one_is_folded(self):
        C = replay_file.Checksum
        self.assertEqual(replay_file.fold(0x12345678), 0x12 ^ 0x34 ^ 0x56 ^ 0x78)
        whole, folded = C(bytes.fromhex("12345678")), C(bytes((0, 0, 0, 0x12 ^ 0x34 ^ 0x56 ^ 0x78)), full=False)
        self.assertTrue(whole == folded and folded == whole and folded == bytes.fromhex("12345678"))
        self.assertTrue(whole != C(bytes.fromhex("12345679")))
        self.assertTrue(folded != C(bytes.fromhex("12345679")))
        self.assertTrue(C(bytes.fromhex("12345678")) != C(bytes.fromhex("78563412")))   # whole: the word, not the fold

    def test_a_damaged_compact_file_is_refused(self):
        compact = self.sample("v8.fzrp")
        r = replay_file.parse(compact[:-1])
        self.assertTrue(r.marked and r.error and not r.steps)
        starts, at = [], 8
        for _ in range(5):   # a byte inside each part's bytes, and a part's length
            length = struct.unpack_from(">I", compact, at + 4)[0]
            starts.append(at + 8 + length // 2)
            at += 8 + length
        for at in starts[:1] + starts[3:] + [12]:
            broken = bytearray(compact)
            broken[at] ^= 0x40
            with self.assertRaises(ValueError):
                replay_file.expand(bytes(broken))
        older = bytearray(compact)
        older[7] = 10
        with self.assertRaises(ValueError):
            replay_file.expand(bytes(older))


if __name__ == "__main__":
    unittest.main()
