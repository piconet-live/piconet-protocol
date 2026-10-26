"""The generator's own rules: what the schema may say, and that a stale generated file is found. No game, no build.
The facts table's wire rows take their values from this schema (port/facts/generate.py schema_names); the round trip
of the generated C++ codec over the vectors is tests/cxx (check.sh)."""
import contextlib
import io
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
folder = os.path.dirname(here)
sys.path.insert(0, folder)
import generate  # noqa: E402

with open(generate.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()


class Schema(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def error(self, old, new):
        self.assertIn(old, SCHEMA)
        path = os.path.join(self.temp.name, "relay.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA.replace(old, new, 1))
        with self.assertRaises(generate.SchemaError) as raised:
            generate.load(path)
        return str(raised.exception)

    def test_the_schema_loads(self):
        protocol, packets = generate.load()
        self.assertEqual(protocol["version"], 3)
        self.assertEqual([p.type for p in packets], list(range(1, 14)))
        official = next(p for p in packets if p.name == "Official")
        self.assertEqual((official.head, official.records.seats.name), (17, "PlayerSeats"))

    def test_broken_rules_are_named(self):
        self.assertIn("share a type", self.error("type = 2\n", "type = 1\n"))
        self.assertIn("must be the last field", self.error(
            '{ name = "RelayMs", type = "u32", optional = true },', '{ name = "RelayMs", type = "u32", optional = true },'
            '\n    { name = "After", type = "u8" },'))
        self.assertIn("no earlier u8 field", self.error('count = "Count", per = "1"', 'count = "Nothing", per = "1"'))
        self.assertIn("neither", self.error('per = "seats:PlayerSeats"', 'per = "seats:FirstFrames"'))
        self.assertIn("type 'u16'", self.error('{ name = "Seat", type = "u8" }]', '{ name = "Seat", type = "u16" }]'))
        self.assertIn("endian is big", self.error('endian = "big"', 'endian = "little"'))
        self.assertIn("unknown keys", self.error('{ name = "Frame", type = "u32" }]', '{ name = "Frame", typ = "u32" }]'))
        self.assertIn("ai_drives", self.error("ai_drives = [0, 0x80, 0, 0, 0, 0]", "ai_drives = [0, 0x80]"))

    def test_version_3s_cookie_rejoin_and_flags(self):
        protocol, packets = generate.load()
        by = {p.name: p for p in packets}
        self.assertEqual([f.name for f in by["Input"].cs_fields][-1], "Cookie")
        self.assertEqual((by["Input"].head, by["Joined"].head, by["Checksum"].head), (25, 30, 24))
        self.assertIn("public const byte Spectator = 4;", generate.codec_cs.emit(protocol, packets))
        self.assertIn("JOINED_SPECTATOR = 4;", generate.codec_cxx.emit(protocol, packets))

    def test_the_seats_key_opens_what_the_relay_sends(self):
        protocol, packets = generate.load()
        self.assertEqual(protocol["key_size"], 8)
        self.assertEqual([p.name for p in packets if p.from_relay],
                         ["Pong", "Joined", "Go", "Official", "Desync", "SeatLeft", "SeatBack"])
        self.assertIn("SeatBack p => p with { Key = key },", generate.codec_cs.emit(protocol, packets))
        self.assertIn("is the first field", self.error(
            '{ name = "Key", type = "u64", default = 0 }, { name = "Frame", type = "u32" }]',
            '{ name = "Frame", type = "u32" }, { name = "Key", type = "u64", default = 0 }]'))

    def test_csharp_order_and_defaults(self):
        old = '[[packet]]\nname = "Join"'
        self.assertIn("cs_order names every field", self.error(old, '[[packet]]\ncs_order = ["Nope"]\nname = "Join"'))
        self.assertIn("no default after", self.error(
            '{ name = "Session", type = "u32" },\n    { name = "StartBarrier"',
            '{ name = "Session", type = "u32", default = 0 },\n    { name = "StartBarrier"'))
        self.assertIn("a default is a number", self.error(
            '{ name = "Records", type = "records", count = "Count", per = "1" }',
            '{ name = "Records", type = "records", count = "Count", per = "1", default = 0 }'))

    def test_a_stale_file_is_found_and_rewritten(self):
        said = self.enterContext(contextlib.redirect_stderr(io.StringIO()))
        for name in generate.OUTPUTS.values():
            os.makedirs(os.path.dirname(os.path.join(self.temp.name, name)), exist_ok=True)
        self.assertEqual(generate.main(["--check"], folder=self.temp.name), 1)
        self.assertEqual(generate.main([], folder=self.temp.name), 0)
        self.assertEqual(generate.main(["--check"], folder=self.temp.name), 0)
        with open(os.path.join(self.temp.name, generate.OUTPUTS["cxx"]), "a", encoding="utf-8") as f:
            f.write("// edited\n")
        self.assertEqual(generate.main(["--check"], folder=self.temp.name), 1)
        self.assertEqual(generate.main(["--bogus"], folder=self.temp.name), 2)
        self.assertIn("stale (run protocol/generate.py)", said.getvalue())

    def test_the_files_on_disk_are_current(self):
        self.assertEqual(generate.main(["--check"]), 0)


if __name__ == "__main__":
    unittest.main()
