"""The launch file's schema (launch.toml), loaded and checked: its parts (the file and the objects in it), each
part's members with their types, ranges and the C# writer's rules. generate.py hands the
result to launch_cxx.py and launch_cs.py. A rule the schema breaks is a SchemaError that names it. Standard library
only."""
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "launch.toml")

TOP_KEYS = {"launch", "codec", "part"}
LAUNCH_KEYS = {"name", "max_bytes"}
PART_KEYS = {"name", "under", "key", "cs", "cs_base", "cs_order", "doc", "member"}
MEMBER_KEYS = {"key", "type", "to", "hand", "low", "high", "values", "least", "most", "may_be_empty", "most_bytes",
               "missing", "cs", "doc"}
TYPES = {"bool", "whole", "number", "text", "one_of", "wholes", "object"}
CS_KEYS = {"name", "write", "value"}
# what each `write` takes: the member types it is for
WRITES = {"always": {"whole"}, "required": {"whole", "text", "one_of"}, "set": {"bool", "text", "one_of", "object"},
          "true": {"bool"}, "false": {"bool"}, "positive": {"whole"}}
IDENTIFIER = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
KEY = re.compile(r"[a-z][A-Za-z0-9]*\Z")
CXX_MEMBER = re.compile(r"[a-z][a-z0-9_]*\Z")


class SchemaError(Exception):
    pass


class Member:
    def __init__(self, raw, part):
        def fail(message):
            raise SchemaError(f"part {part}, member {raw.get('key', '?')}: {message}")
        if set(raw) - MEMBER_KEYS:
            fail(f"unknown keys {sorted(set(raw) - MEMBER_KEYS)}")
        self.key, self.type = raw.get("key", ""), raw.get("type", "")
        if not KEY.match(self.key):
            fail("a key is camelCase letters and digits")
        if self.type not in TYPES:
            fail(f"type {self.type!r} is none of {sorted(TYPES)}")
        self.hand = raw.get("hand", False) is True or self.type == "object"
        self.to = raw.get("to")
        if self.hand != (self.to is None) or (self.to is not None and not CXX_MEMBER.match(self.to)):
            fail("a member the generated reader takes has `to` (a C++ member, snake_case), one taken by hand none")
        ranged = self.type in ("whole", "number", "wholes")
        self.low, self.high = raw.get("low"), raw.get("high")
        if ranged != (self.low is not None and self.high is not None) or (ranged and not self.low <= self.high):
            fail("a whole, a number and a list of wholes have `low` <= `high`, no other type has them")
        if self.type in ("whole", "wholes") and not (type(self.low) is int and type(self.high) is int and
                                                      0 <= self.low and self.high <= 0xFFFFFFFF):
            fail("a whole number's range is within 0 to 4294967295")
        self.least, self.most = raw.get("least"), raw.get("most")
        if (self.type == "wholes") != (self.least is not None and self.most is not None):
            fail("a list of wholes has `least` and `most`, no other type has them")
        self.values = raw.get("values")
        if (self.type == "one_of") != (isinstance(self.values, list) and len(self.values) > 0):
            fail("a one_of has its `values`, no other type has them")
        self.may_be_empty = raw.get("may_be_empty", False)
        self.most_bytes = raw.get("most_bytes")
        if (self.may_be_empty or self.most_bytes is not None) and self.type != "text":
            fail("may_be_empty and most_bytes are a text's")
        self.missing = raw.get("missing")
        self.part = None   # an object's part, when it has one (load)
        self.cs = raw.get("cs")
        if self.cs is not None:
            if set(self.cs) - CS_KEYS or self.cs.get("write") not in WRITES:
                fail(f"cs is {{name, write, value}}, write one of {sorted(WRITES)}")
            if self.type not in WRITES[self.cs["write"]]:
                fail(f"write {self.cs['write']!r} is not for a {self.type}")
            if (self.cs["write"] == "always") != ("value" in self.cs) or (self.cs["write"] == "always") == ("name" in self.cs):
                fail("write \"always\" has a `value` and no `name`; every other write a `name` and no `value`")
            if "name" in self.cs and not IDENTIFIER.match(self.cs["name"]):
                fail("a C# name is PascalCase")

    @property
    def upper(self):
        return re.sub(r"(?<!^)(?=[A-Z])", "_", self.key).upper()


class Part:
    def __init__(self, raw):
        name = raw.get("name", "?")
        if set(raw) - PART_KEYS:
            raise SchemaError(f"part {name}: unknown keys {sorted(set(raw) - PART_KEYS)}")
        if not IDENTIFIER.match(name):
            raise SchemaError(f"part {name}: a name is PascalCase")
        self.name, self.under, self.key, self.doc = name, raw.get("under"), raw.get("key"), raw.get("doc", "")
        self.cs, self.cs_base = raw.get("cs"), raw.get("cs_base", False)
        self.members = [Member(m, name) for m in raw.get("member", [])]
        self.raw_order = raw.get("cs_order")

    def written(self):
        """The members the launcher writes, in the order it writes them."""
        return self.cs_members

    def read(self):
        """The members the generated reader takes, in the order it takes them."""
        return [m for m in self.members if not m.hand]


def load(path=SCHEMA):
    """The schema, checked: {"launch", "codec", "parts"}."""
    with open(path, "rb") as f:
        try:
            raw = tomllib.load(f)
        except tomllib.TOMLDecodeError as e:
            raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    if set(raw) - TOP_KEYS:
        raise SchemaError(f"unknown tables {sorted(set(raw) - TOP_KEYS)}")
    launch = raw.get("launch", {})
    if set(launch) != LAUNCH_KEYS:
        raise SchemaError(f"[launch] has {sorted(LAUNCH_KEYS)}")
    codec = raw.get("codec", {})
    for language in ("csharp", "cxx"):
        if "namespace" not in codec.get(language, {}):
            raise SchemaError(f"[codec.{language}]: no namespace")
    parts, by_name = [], {}
    for p in raw.get("part", []):
        part = Part(p)
        if part.name in by_name:
            raise SchemaError(f"two parts {part.name}")
        keys = [m.key for m in part.members]
        twice = sorted({k for k in keys if keys.count(k) > 1})
        if twice or not keys:
            raise SchemaError(f"part {part.name}: members named twice {twice}, or none")
        written = [m.key for m in part.members if m.cs is not None]
        order = part.raw_order or written
        if sorted(order) != sorted(written):
            raise SchemaError(f"part {part.name}: cs_order names every member the launcher writes once")
        part.cs_members = [part.members[keys.index(k)] for k in order]
        if part.cs is None and any(m.cs is not None for m in part.members):
            raise SchemaError(f"part {part.name}: a part with members the launcher writes has `cs`, its record")
        by_name[part.name] = part
        parts.append(part)
    if not parts or parts[0].name != "File" or parts[0].under is not None:
        raise SchemaError("the first part is File, the file itself")
    for part in parts[1:]:
        parent = by_name.get(part.under)
        if parent is None or part.key is None:
            raise SchemaError(f"part {part.name}: `under` a part and its `key` there")
        member = next((m for m in parent.members if m.key == part.key), None)
        if member is None or member.type != "object":
            raise SchemaError(f"part {part.name}: {part.under} has no object {part.key}")
        member.part = part
        if (member.cs is None) != (part.cs is None):
            raise SchemaError(f"part {part.name}: written by the launcher where its member in {part.under} is")
    return {"launch": launch, "codec": codec, "parts": parts}
