"""The host's rules' schema (rules.toml): what it may say, the words of the race block it gives, and the numbers the
runner's facts table takes from it. No game, no build; check.sh compiles the generated C++ header."""
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))
import generate  # noqa: E402
import replay  # noqa: E402
import rules  # noqa: E402

with open(rules.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()


def load_checked(path):
    """The rules as generate.py takes them: loaded, and their replay features held to replay.toml's."""
    s = rules.load(path)
    protocol, _ = generate.load()
    file = replay.load(replay.SCHEMA, protocol["record_size"])
    rules.check_replay(s, {v["feature"] for v in file["versions"] if v.get("feature")})
    return s


class RulesSchema(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def error(self, old, new):
        self.assertIn(old, SCHEMA)
        path = os.path.join(self.temp.name, "rules.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA.replace(old, new, 1))
        with self.assertRaises(rules.SchemaError) as raised:
            load_checked(path)
        return str(raised.exception)

    def test_the_rules_load_in_the_operators_order(self):
        s = rules.load()
        self.assertEqual(len(s["rules"]), 24)
        self.assertEqual([r.name for r in s["race"]], ["cpu-level", "boost-start", "restore", "finish-limit", "mine-regen"])
        self.assertEqual([r.race.cs for r in s["race"]],
                         ["CpuLevel", "BoostFromStart", "Restore", "FinishLimitSeconds", "MineRegenMs"])

    def test_the_words_are_the_race_blocks_of_before(self):
        """The offsets every released game reads (the runner's port/core/src/files.h before the schema)."""
        names = rules.names(rules.load())
        self.assertEqual({k: v for k, v in names.items() if k.startswith("rule_at_")},
                         {"rule_at_cpu_level": 256, "rule_at_boost_start": 260, "rule_at_restore": 264,
                          "rule_at_finish_limit": 300, "rule_at_mine_regen": 308})
        self.assertEqual((names["rule_least_mine_regen"], names["rule_most_mine_regen"], names["rule_step_mine_regen"]),
                         (50, 5000, 50))
        self.assertEqual((names["rule_most_cpu_level"], names["rule_most_finish_limit"]), (3, 600))

    def test_a_new_rules_word_goes_in_the_free_room(self):
        free = rules.free_words(rules.load())
        self.assertIn(104, free)
        self.assertIn(312, free)
        self.assertNotIn(308, free)
        self.assertNotIn(320, free)

    def test_a_word_on_another_part_is_refused(self):
        self.assertIn("overlaps the race's flow", self.error("at = 308", "at = 304"))
        self.assertIn("overlaps the seats' custom machines", self.error("at = 308", "at = 320"))
        self.assertIn("overlaps rule cpu-level's word", self.error("at = 308", "at = 256"))
        self.assertIn("is outside the race block", self.error("at = 308", "at = 1280"))
        self.assertIn("at is a word's offset", self.error("at = 308", "at = 310"))

    def test_what_a_rule_may_say(self):
        self.assertIn("share a key", self.error('key = "mineRegenMs"', 'key = "cpuLevel"'))
        self.assertIn("share a flag", self.error('flag = "--mine-regen-ms"', 'flag = "--cpu-level"'))
        self.assertIn("absent is a number the rule takes", self.error("absent = 3,", "absent = 4,"))
        self.assertIn("no word of the race block", self.error('kind = "number"\nleast = 0\nmost = 3', 'kind = "choice"\nleast = 0\nmost = 3'))
        self.assertIn("default 210 is no value", self.error("default = 200", "default = 210"))
        self.assertIn("no whole number of steps", self.error("step = 50\n", "step = 10\n"))
        self.assertIn("names a place other than", self.error("in steps of {step}", "in steps of {steps}"))
        self.assertIn("is no version's feature", self.error('replay = "mines"', 'replay = "bees"'))

    def test_takes_says_the_numbers(self):
        by = {r.name: r for r in rules.load()["rules"]}
        self.assertEqual(by["mine-regen"].taken, "0 (mines stay gone) or 50 to 5000 ms, in steps of 50")
        self.assertEqual(by["cpu-level"].taken, "0 to 3")
        self.assertEqual(by["restore"].taken, "on or off")
        self.assertIsNone(by["racers"].taken)   # its range is GameFacts.Limits' (HostRule.Range)

if __name__ == "__main__":
    unittest.main()
