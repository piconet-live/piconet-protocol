"""stats.toml's rules (stats.py) and what the stat tables' emitters write (stats_md.py's splice, the numbers' texts).
That the generated files and pages are current is test_generate.py's; that the programs' lists are the handbook's,
StatPackRulesTests, StatPackConstantsTests and the core's tests. No game, no build."""
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))
import stats  # noqa: E402
import stats_md  # noqa: E402

with open(stats.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()


class TheTables(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def error(self, old, new):
        self.assertIn(old, SCHEMA)
        path = os.path.join(self.temp.name, "stats.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA.replace(old, new, 1))
        with self.assertRaises(stats.SchemaError) as raised:
            stats.load(path)
        return str(raised.exception)

    def test_the_tables_load(self):
        tables = stats.load()
        self.assertEqual((len(tables.rules), len(tables.slots)), (22, 62))
        self.assertEqual([c.slot for c in tables.constants if c.wide], [1, 40])
        self.assertEqual(tables.slots[2][2:5], ("Number64Low", 0x14, 0))

    def test_a_broken_rule_is_named(self):
        self.assertIn("every word once", self.error('at = 0x08, name = "Acceleration"', 'at = 0x0C, name = "Acceleration"'))
        self.assertIn("points' low is -high", self.error('name = "Wall", kind = "points", low = -25', 'name = "Wall", kind = "points", low = -24'))
        self.assertIn("bytes is the word at 0x48", self.error('name = "Bytes48", kind = "bytes", low = 0, high = 3',
                                                             'name = "Bytes48", kind = "bytes", low = 0, high = 3.5'))
        self.assertIn("tried is a number's", self.error("high = 45, tried = 9", "high = 45, tried = 46"))
        self.assertIn("the game's own value is in its range", self.error("low = 0\nhigh = 1.7\n", "low = 0\nhigh = 0.1\n"))
        self.assertIn("constants share a name", self.error('name = "DashPlateScale"', 'name = "DashPlateBase"'))
        self.assertIn("constants share a seat", self.error("seat = 0x04\n", "seat = 0x00\n"))

    def test_the_numbers_are_written_as_each_reader_reads_them(self):
        self.assertEqual([stats.literal(v, True) for v in (1, -3.5, 0.01, 4.26)], ["1", "-3.5f", "0.01f", "4.26f"])
        self.assertIsNone(stats.literal(stats.MAX_DOUBLE, False))
        self.assertEqual([stats.dotnet_text(v, False) for v in (0.0, 9990.0, stats.MAX_DOUBLE, 0.6000000238418579, 1e-05)],
                         ["0", "9990", "1.7976931348623157E+308", "0.6000000238418579", "1E-05"])
        self.assertEqual(stats.dotnet_text(stats.f32(1 / 6), True), "0.16666667")
        self.assertEqual([stats.handbook_text(v) for v in (25000, 0.45, 4.26, -25, 0.01)], ["25000", "0.45", "4.26", "-25", "0.01"])

    def test_a_page_takes_its_table_between_its_markers(self):
        page = f"before\n\n{stats_md.BEGIN}\n\n| old |\n\n{stats_md.END}\n\nafter\n"
        self.assertEqual(stats_md.splice(page, "p.md", ["| new |"]),
                         f"before\n\n{stats_md.BEGIN}\n\n| new |\n\n{stats_md.END}\n\nafter\n")
        with self.assertRaises(stats.SchemaError):
            stats_md.splice("no markers\n", "p.md", ["| new |"])


if __name__ == "__main__":
    unittest.main()
