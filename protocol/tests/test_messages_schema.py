"""messages.toml's rules (messages.py) and what its emitters write (messages_cs.py, messages_cxx.py). That the generated
files are current is test_generate.py's; that the C# records equal the file, and the codecs the vectors, check.sh's
(tests/dotnet-messages, tests/cxx/test_lobby_messages.cpp). No build."""
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))
import messages  # noqa: E402
import messages_cs  # noqa: E402
import messages_cxx  # noqa: E402

with open(messages.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()


class TheMessages(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def error(self, old, new):
        self.assertIn(old, SCHEMA)
        path = os.path.join(self.temp.name, "messages.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA.replace(old, new, 1))
        with self.assertRaises(messages.SchemaError) as raised:
            messages.load(path)
        return str(raised.exception)

    def test_the_file_loads(self):
        table = messages.load()
        roots = [r["root"] for r in table["records"]]
        self.assertEqual(roots.count("client"), 26)
        self.assertEqual(roots.count("server"), 29)
        self.assertEqual(table["link"]["version"], 3)

    def test_a_type_must_be_known(self):
        self.assertIn("no type", self.error('{ name = "name", type = "string" },', '{ name = "name", type = "strng" },'))

    def test_two_messages_of_one_side_cannot_share_a_type(self):
        self.assertIn("no other client message", self.error('type = "leaveQueue"', 'type = "queue"'))

    def test_a_default_must_be_of_the_type(self):
        self.assertIn("is not a", self.error("default = '50'", "default = '\"fifty\"'"))

    def test_omit_default_only_on_values(self):
        self.assertIn("only on a value", self.error('{ name = "line", type = "string?", default = \'null\' }',
                                                    '{ name = "line", type = "string?", default = \'null\', omit = "default" }'))

    def test_records_come_after_what_they_hold(self):
        order = [r["name"] for r in messages.ordered(messages.load())]
        self.assertLess(order.index("Rgb"), order.index("PlayerInfo"))
        self.assertLess(order.index("SeatSetup"), order.index("RaceSetup"))
        self.assertLess(order.index("RaceSetup"), order.index("MatchStart"))

    def test_keywords_get_an_underscore(self):
        self.assertEqual(messages_cxx.ident("default"), "default_")
        self.assertEqual(messages_cxx.ident("or"), "or_")
        self.assertEqual(messages_cxx.ident("final"), "final")

    def test_the_lines_name_every_member(self):
        table = messages.load()
        lines = messages.lines(table)
        self.assertEqual(lines[0], "link version=3 path=/ws keepAlive=20 global=global lobby=lobby")
        self.assertIn("  stage string default=\"forming\"", lines)
        self.assertIn("  cpu bool writeonly", lines)
        self.assertIn('"  cpu bool writeonly",', messages_cs.emit(lines))


if __name__ == "__main__":
    unittest.main()
