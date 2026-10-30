"""The look's tokens (theme.tokens.json, the Design Tokens Community Group's format as Style Dictionary reads it),
loaded and checked. generate.py hands the result to theme_axaml.py (the launcher's resources), theme_cxx.py (the
runner's palette) and theme_css.py (the site's and the service's custom properties). A rule the file breaks is a
SchemaError that names it. Standard library only.

A token's names: its group and name in the file (color.on-accent); the launcher's key, the name in PascalCase with the
group's kind (OnAccentColor, RowLineBrush, Display64: theme_axaml.py), or the key live.piconet gives (GemAXBrush);
the C++ constant, in capitals (ON_ACCENT); the CSS property, the name (--on-accent)."""
import json
import os
import re

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "theme.tokens.json")

# each group: its $type, and what it is in the launcher (the element, and the key's ending)
GROUPS = {"color": "color", "brush": "color", "size": "dimension", "duration": "duration", "easing": "cubicBezier",
          "shadow": "shadow"}
TOKEN_KEYS = {"$value", "$type", "$description", "$extensions"}
GROUP_KEYS = {"$type", "$description"}
EXTENSION = "live.piconet"
EXTENSION_KEYS = {"key", "opacity"}
NAME = re.compile(r"[a-z][a-z0-9]*(-[a-z0-9]+)*\Z")
HEX = re.compile(r"#[0-9A-F]{6}\Z")
HEX8 = re.compile(r"#[0-9A-F]{8}\Z")
ALIAS = re.compile(r"\{color\.([a-z0-9-]+)\}\Z")
DIMENSION = re.compile(r"(-?\d+(?:\.\d+)?)px\Z")
DURATION = re.compile(r"(\d+)ms\Z")


class SchemaError(Exception):
    pass


class Token:
    """One token: Group, Name, Type, Description; Colour (#RRGGBB, an alias's resolved), Alias (the colour it names,
    or None), Opacity (1 but for a tint), Number (a dimension's px or a number), Ms, Curve, Shadow; Key (the
    launcher's key)."""

    def __init__(self, group, name, raw, kind):
        self.group, self.name, self.raw = group, name, raw
        self.type = raw.get("$type", kind)
        self.description = raw.get("$description", "")
        self.alias, self.colour, self.opacity = None, None, 1.0
        self.number = self.ms = self.curve = self.shadow = None
        extra = raw.get("$extensions", {})
        own = extra.get(EXTENSION, {}) if isinstance(extra, dict) else None
        if not isinstance(own, dict) or set(extra) - {EXTENSION} or set(own) - EXTENSION_KEYS:
            self.fail(f"$extensions holds only {EXTENSION}, with {sorted(EXTENSION_KEYS)}")
        self.key = own.get("key")
        if "opacity" in own:
            self.opacity = own["opacity"]
            if group != "brush" or type(self.opacity) is not float or not 0 < self.opacity < 1:
                self.fail("an opacity is a brush's, a number between 0 and 1")

    def fail(self, message):
        raise SchemaError(f"{self.group}.{self.name}: {message}")

    @property
    def pascal(self):
        return "".join(part[:1].upper() + part[1:] for part in self.name.split("-"))

    @property
    def constant(self):
        return self.name.replace("-", "_").upper()


def number(text):
    value = float(text)
    return int(value) if value == int(value) else value


def read(token, colours):
    """The token's value, checked and taken apart by its type."""
    value = token.raw["$value"]
    if token.type == "color":
        alias = ALIAS.match(value) if isinstance(value, str) else None
        if alias:
            if token.group != "brush" or alias.group(1) not in colours:
                token.fail(f"{value} is no colour of the color group (a brush names one)")
            token.alias, token.colour = alias.group(1), colours[alias.group(1)].colour
        elif isinstance(value, str) and HEX.match(value):
            token.colour = value
        else:
            token.fail(f"a colour is #RRGGBB in capitals or {{color.NAME}} (got {value!r})")
    elif token.type == "dimension":
        m = DIMENSION.match(value) if isinstance(value, str) else None
        if not m:
            token.fail(f"a dimension is a number of px (got {value!r})")
        token.number = number(m.group(1))
    elif token.type == "number":
        if type(value) not in (int, float):
            token.fail(f"a number (got {value!r})")
        token.number = value
    elif token.type == "duration":
        m = DURATION.match(value) if isinstance(value, str) else None
        if not m:
            token.fail(f"a duration is a whole number of ms (got {value!r})")
        token.ms = int(m.group(1))
    elif token.type == "cubicBezier":
        if not (isinstance(value, list) and len(value) == 4 and all(type(v) in (int, float) for v in value)):
            token.fail("a cubic Bézier is four numbers")
        token.curve = value
    elif token.type == "shadow":
        keys = {"color", "offsetX", "offsetY", "blur", "spread"}
        if not isinstance(value, dict) or set(value) != keys or not HEX8.match(value["color"]) or not all(
                DIMENSION.match(value[k]) for k in keys - {"color"}):
            token.fail("a shadow is {color: #RRGGBBAA, offsetX, offsetY, blur, spread: px}")
        token.shadow = {k: number(DIMENSION.match(v).group(1)) if k != "color" else v for k, v in value.items()}
    else:
        token.fail(f"type {token.type!r} is none of color, dimension, number, duration, cubicBezier, shadow")


def load(path=SCHEMA):
    """The tokens, group by group in the file's order, checked."""
    try:
        with open(path, encoding="utf-8") as f:
            raw = json.load(f)
    except json.JSONDecodeError as e:
        raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    groups = [g for g in raw if not g.startswith("$")]
    if groups != [g for g in GROUPS if g in raw] or set(GROUPS) - set(groups):
        raise SchemaError(f"the groups are {', '.join(GROUPS)}, in that order")
    tokens, colours = [], {}
    for group in groups:
        body = raw[group]
        if body.get("$type") != GROUPS[group] or set(k for k in body if k.startswith("$")) - GROUP_KEYS:
            raise SchemaError(f"{group}: $type {GROUPS[group]}, and no group keys but {sorted(GROUP_KEYS)}")
        for name, value in body.items():
            if name.startswith("$"):
                continue
            if not NAME.match(name):
                raise SchemaError(f"{group}.{name}: a name is lower-case words joined by -")
            if not isinstance(value, dict) or "$value" not in value or set(value) - TOKEN_KEYS:
                raise SchemaError(f"{group}.{name}: a token has a $value, and no keys but {sorted(TOKEN_KEYS)}")
            token = Token(group, name, value, GROUPS[group])
            if group != "size" and token.type != GROUPS[group]:
                token.fail(f"the group's tokens are all {GROUPS[group]}")
            if group == "size" and token.type not in ("dimension", "number"):
                token.fail("a size is a dimension or a number")
            read(token, colours)
            if group == "color":
                colours[name] = token
            tokens.append(token)
    keys = [axaml_key(t) for t in tokens]
    twice = sorted({k for k in keys if keys.count(k) > 1})
    if twice:
        raise SchemaError(f"two tokens are the launcher's {', '.join(twice)}")
    return tokens


def axaml_key(token):
    """The launcher's key: live.piconet's, else the name in PascalCase, with Color or Brush for a colour."""
    if token.key:
        return token.key
    return token.pascal + {"color": "Color", "brush": "Brush"}.get(token.group, "")


def opaque_colours(tokens):
    """The colours by one name each, as the CSS and the C++ palette have them: every colour, then every brush of a
    name no colour has and with no opacity of its own. A brush and a colour of one name are one colour."""
    seen = {}
    for t in tokens:
        if t.group == "color":
            seen[t.name] = t
    for t in tokens:
        if t.group == "brush" and t.opacity == 1.0:
            if t.name in seen:
                if seen[t.name].colour != t.colour:
                    t.fail(f"the colour of the same name is {seen[t.name].colour}")
                continue
            seen[t.name] = t
    return seen
