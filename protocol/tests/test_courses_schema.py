"""courses.toml's rules (courses.py) and what its emitters write (courses_cs.py, courses_cxx.py). That the generated
files are current is test_generate.py's. No build."""
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))
import courses  # noqa: E402
import courses_cs  # noqa: E402
import courses_cxx  # noqa: E402

with open(courses.SCHEMA, encoding="utf-8") as f:
    SCHEMA = f.read()


class TheCourses(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def error(self, old, new):
        self.assertIn(old, SCHEMA)
        path = os.path.join(self.temp.name, "courses.toml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(SCHEMA.replace(old, new, 1))
        with self.assertRaises(courses.SchemaError) as raised:
            courses.load(path)
        return str(raised.exception)

    def test_the_file_loads(self):
        t = courses.load()
        self.assertEqual((t["first"], t["chapters"]), (37, 9))
        self.assertEqual(len(t["courses"]), 26)
        self.assertEqual(t["chapters_named"], [(3, 39, "High-Stakes Jumps"), (7, 43, "Championship Circuit"),
                                               (8, 44, "Emperor's Trial"), (9, 45, "Dimension's Edge")])

    def test_broken_rules_are_named(self):
        self.assertIn("named twice", self.error("chapter = 7\n", "chapter = 3\n"))
        self.assertIn("a whole number 1 to 9", self.error("chapter = 9\n", "chapter = 10\n"))
        self.assertIn("a name of letters", self.error('"Emperor\'s Trial"', '"Emperor\\"s Trial"'))
        self.assertIn("a name of letters", self.error('track = "Twist Road"', 'track = "Twist Road!"'))
        self.assertIn("longer than 31", self.error('"Dimension\'s Edge"', '"' + "D" * 32 + '"'))
        self.assertIn("order of their numbers", self.error("course = 5\n", "course = 2\n"))
        self.assertIn("the story's stages' place", self.error('place = "Big Blue"', 'place = "Story"'))
        self.assertIn("unknown keys", self.error("chapter = 3\n", "chapter = 3\nvenue = \"X\"\n"))

    def test_the_emitters_write_every_name(self):
        t = courses.load()
        cs, cxx = courses_cs.emit(t), courses_cxx.emit(t)
        self.assertIn('(3, 39, "High-Stakes Jumps"),', cs)
        self.assertIn('(16, "Casino Palace", "Split Oval"),', cs)
        self.assertIn("public const int FirstStoryStage = 37;", cs)
        self.assertIn('{8, 44, "Emperor\'s Trial"},', cxx)
        self.assertIn("inline constexpr int FIRST_STORY_STAGE = 37, STORY_CHAPTERS = 9;", cxx)
        self.assertIn("constexpr const char* StoryName(int course)", cxx)


if __name__ == "__main__":
    unittest.main()
