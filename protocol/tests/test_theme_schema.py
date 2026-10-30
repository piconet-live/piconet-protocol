"""theme.tokens.json's rules (theme.py) and what the look's emitters write (theme_axaml.py, theme_cxx.py,
theme_css.py). That the generated files are current is test_generate.py's. No build."""
import json
import os
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))
import theme  # noqa: E402
import theme_axaml  # noqa: E402
import theme_css  # noqa: E402
import theme_cxx  # noqa: E402

with open(theme.SCHEMA, encoding="utf-8") as f:
    SCHEMA = json.load(f)


class TheTokens(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)

    def load(self, change):
        raw = json.loads(json.dumps(SCHEMA))
        change(raw)
        path = os.path.join(self.temp.name, "theme.tokens.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(raw, f)
        return theme.load(path)

    def error(self, change):
        with self.assertRaises(theme.SchemaError) as raised:
            self.load(change)
        return str(raised.exception)

    def test_the_file_loads(self):
        tokens = theme.load()
        self.assertEqual({t.group for t in tokens}, set(theme.GROUPS))
        accent = next(t for t in tokens if t.group == "color" and t.name == "accent")
        self.assertEqual((accent.colour, theme.axaml_key(accent), accent.constant), ("#3DFF6A", "AccentColor", "ACCENT"))

    def test_a_colour_is_six_hex_digits_in_capitals_or_a_colour_by_name(self):
        def lower(raw):
            raw["color"]["ground"]["$value"] = "#00000a"
        self.assertIn("color.ground: a colour is #RRGGBB", self.error(lower))

        def nowhere(raw):
            raw["brush"]["ground"]["$value"] = "{color.nowhere}"
        self.assertIn("brush.ground: {color.nowhere} is no colour", self.error(nowhere))

        def colour_alias(raw):
            raw["color"]["deep"]["$value"] = "{color.ground}"
        self.assertIn("a brush names one", self.error(colour_alias))

    def test_only_a_brush_has_an_opacity_and_it_is_between_0_and_1(self):
        def on_colour(raw):
            raw["color"]["ground"]["$extensions"] = {"live.piconet": {"opacity": 0.5}}
        self.assertIn("an opacity is a brush's", self.error(on_colour))

        def whole(raw):
            raw["brush"]["accent-tint"]["$extensions"]["live.piconet"]["opacity"] = 1.0
        self.assertIn("between 0 and 1", self.error(whole))

    def test_values_names_groups_and_keys_are_checked(self):
        def px(raw):
            raw["size"]["gap8"]["$value"] = "8"
        self.assertIn("size.gap8: a dimension is a number of px", self.error(px))
        self.assertIn("lower-case words", self.error(lambda raw: raw["color"].update({"Ground": {"$value": "#000000"}})))
        self.assertIn("the groups are", self.error(lambda raw: raw.pop("shadow")))
        self.assertIn("$extensions holds only",
                      self.error(lambda raw: raw["color"]["ground"].update({"$extensions": {"other": {}}})))

        def same_key(raw):
            raw["brush"]["gem-ax"]["$extensions"]["live.piconet"]["key"] = "AccentBrush"
        self.assertIn("two tokens are the launcher's AccentBrush", self.error(same_key))

    def test_a_brush_and_a_colour_of_one_name_are_one_colour(self):
        def other(raw):
            raw["brush"]["hover"]["$value"] = "#123456"
        tokens = self.load(other)
        with self.assertRaises(theme.SchemaError):
            theme.opaque_colours(tokens)


class TheOutputs(unittest.TestCase):
    tokens = theme.load()

    def test_the_launcher_gets_each_kind_of_resource_as_it_wrote_it(self):
        text = theme_axaml.emit(self.tokens)
        for line in ('<Color x:Key="GroundColor">#000000</Color>',
                     '<SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource WellColor}" />',
                     '<SolidColorBrush x:Key="AccentTintBrush" Color="{StaticResource AccentColor}" Opacity="0.1" />',
                     '<SolidColorBrush x:Key="TierNoticeTintBrush" Color="#7AFF96" Opacity="0.16" />',
                     '<SolidColorBrush x:Key="GemAXBrush" Color="#F2A33A" />',
                     '<x:Double x:Key="Display64">64</x:Double>', '<x:Double x:Key="LabelSpacing">0.9</x:Double>',
                     '<x:TimeSpan x:Key="Quick">0:0:0.12</x:TimeSpan>',
                     '<SplineEasing x:Key="EaseOut" X1="0.2" Y1="0.8" X2="0.2" Y2="1" />',
                     '<BoxShadows x:Key="FloatShadow">0 8 24 0 #73000000</BoxShadows>'):
            self.assertIn(line, text)

    def test_the_runner_gets_the_opaque_colours_once_each(self):
        text = theme_cxx.emit(self.tokens)
        self.assertIn("constexpr std::uint32_t ROW_LINE = 0x2A2E2A;", text)
        self.assertEqual(text.count(" HOVER = "), 1)
        self.assertNotIn("TINT", text)

    def test_the_css_has_tints_sizes_motion_and_shadows(self):
        text = theme_css.emit(self.tokens)
        for line in ("--accent: #3DFF6A;", "--accent-tint: rgba(61, 255, 106, 0.1);", "--tag-scale: 0.8;",
                     "--gap16: 16px;", "--quick: 120ms;", "--ease-out: cubic-bezier(0.2, 0.8, 0.2, 1);",
                     "--float-shadow: 0px 8px 24px 0px rgba(0, 0, 0, 0.451);"):
            self.assertIn(line, text)


if __name__ == "__main__":
    unittest.main()
