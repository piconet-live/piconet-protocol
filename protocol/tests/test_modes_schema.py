"""The mode table's levels (modes.toml's [levels]): a shipped rule set is frozen entirely, a word carries the rule set
it arrived in, and the generated tables say so to both sides. No game, no build."""
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))
import modes  # noqa: E402
import modes_cs  # noqa: E402
import modes_cxx  # noqa: E402

with open(modes.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()

CPUS = 'args = [{ type = "switch", always = 2, always_since = 2 }]'
LAPS = 'name = "laps"\nid = 0\n'


class ModesLevels(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def changed(self, old, new):
        self.assertEqual(SCHEMA.count(old), 1, old)
        path = os.path.join(self.temp.name, "modes.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA.replace(old, new))
        return path

    def error(self, old, new):
        with self.assertRaises(modes.SchemaError) as raised:
            modes.load(self.changed(old, new))
        return str(raised.exception)

    def test_the_table_loads_and_0_6_0_is_as_shipped(self):
        t = modes.load()
        self.assertEqual([(r["number"], r["version"]) for r in t["levels"]["rule_sets"]], [(1, "0.6.0"), (2, "0.6.5")])
        self.assertIsNone(t["levels"]["rule_sets"][-1]["frozen"])   # 0.6.5's is the open one

    def test_a_value_added_inside_a_shipped_rule_set_is_refused(self):
        # `cpus always` without its rule set would be a new value of 0.6.0's
        self.assertIn("rule set 1 (0.6.0) is shipped and frozen", self.error(CPUS, 'args = [{ type = "switch", always = 2 }]'))

    def test_a_value_is_named_once_in_its_choice(self):
        # one name in two choices is fine when they name one thing (the banner and the part WRONG_WAY); twice in one is not
        self.assertIn("choice banner: a value's name twice",
                      self.error('"GAP", "WRONG_WAY", "TICK"]', '"GAP", "WRONG_WAY", "TICK", "GAP"]'))
        self.assertIn("WRONG_WAY", modes.load()["choices"][0]["values"])  # (`part` is the first choice)

    def test_a_word_added_inside_a_shipped_rule_set_is_refused(self):
        new = '[[setup]]\nname = "zz_new"\nid = 250\nargs = [{ type = "switch" }]\n\n'
        last = SCHEMA.rindex("\n[[setup]]\n")
        end = SCHEMA.find("\n[[", last + 1)
        end = len(SCHEMA) if end < 0 else end + 1
        path = os.path.join(self.temp.name, "modes.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA[:end] + "\n" + new + SCHEMA[end:])
        with self.assertRaises(modes.SchemaError) as raised:
            modes.load(path)
        self.assertIn("shipped and frozen", str(raised.exception))
        # with the open rule set's number it is that one's word
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA[:end] + "\n" + new.replace("id = 250\n", "id = 250\nsince = 2\n") + SCHEMA[end:])
        t = modes.load(path)
        self.assertEqual(t["setups"][-1]["since"], 2)
        self.assertIn("[ModeSetup.ZzNew] = 2,", modes_cs.emit(t))
        self.assertIn("case SETUP_ZZNEW: return 2;", modes_cxx.emit(t))

    def test_a_changed_word_or_limit_of_a_shipped_rule_set_is_refused(self):
        self.assertIn("shipped and frozen", self.error(LAPS, 'name = "laps"\nid = 0\nlobby = true\n'))
        self.assertIn("shipped and frozen", self.error("waits = 64 ", "waits = 65 "))

    def test_the_docs_are_free(self):
        modes.load(self.changed("the coordinator: on (1), players may vote", "the lobby: on (1), players may vote"))

    def test_the_levels_rules_are_named(self):
        self.assertIn("since", self.error(CPUS, 'args = [{ type = "switch", always = 2, always_since = 3 }]'))
        self.assertIn("newest is the last rule set's version or a later one", self.error('newest = "0.6.5"', 'newest = "0.6.0"'))
        self.assertIn("the last rule set is [api] number", self.error("[api]\nnumber = 2", "[api]\nnumber = 3"))
        self.assertIn("only the last one is open", self.error('frozen = "', '# frozen = "'))

    def test_both_sides_get_the_levels(self):
        t = modes.load()
        cs, cxx = modes_cs.emit(t), modes_cxx.emit(t)
        self.assertIn('public const string ApiVersion = "0.6.5";', cs)
        self.assertIn('[(1, "0.6.0"), (2, "0.6.5")]', cs)
        self.assertIn("[(ModeSetup.Cpus, 0, 2)] = 2,", cs)
        self.assertIn("inline constexpr int API = 2;", cxx)
        self.assertIn("inline constexpr int SETUP_CPUS_ALWAYS_SINCE = 2;", cxx)


if __name__ == "__main__":
    unittest.main()
