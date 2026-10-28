"""The replay file's schema (replay.toml), loaded and laid out: the versions and their features, the fixed parts'
fields at their offsets, the lineup's block, the step's size from the relay's record (relay.toml), and the header's
and the results' JSON members. generate.py hands the result to replay_cxx.py, replay_cs.py and replay_py.py. A rule
the schema breaks is a SchemaError that names it. Standard library only."""
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "replay.toml")

SIZES = {"u8": 1, "i16": 2, "u16": 2, "u32": 4, "pad": 1}
REPLAY_KEYS = {"name", "mark", "endian", "written", "max_bytes", "max_steps", "max_json", "max_seats"}
TOP_KEYS = {"replay", "compact", "codec", "version", "head", "frames", "lineup", "steps", "results", "result", "header"}
COMPACT_KEYS = {"version", "header_mark", "inputs_mark", "checksums_mark", "full_every", "check", "preset",
                "dict_min", "dict_max", "memlimit"}
VERSION_KEYS = {"number", "name", "feature", "doc"}
FIELD_KEYS = {"name", "type", "count", "doc"}
MEMBER_KEYS = {"key", "type", "null", "optional", "default", "low", "high", "doc"}
MEMBER_TYPES = {"int", "bool", "text", "object"}
IDENTIFIER = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
FEATURE = re.compile(r"[a-z][a-z0-9_]*\Z")
KEY = re.compile(r"[A-Za-z][A-Za-z0-9_]*\Z")


class SchemaError(Exception):
    pass


def snake(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def camel_to_pascal(key):
    """A JSON key as a type's member: timeMs -> TimeMs, mod_sha1 -> ModSha1."""
    return "".join(part[:1].upper() + part[1:] for part in key.split("_"))


class Field:
    def __init__(self, raw, part):
        def fail(message):
            raise SchemaError(f"[{part}], field {raw.get('name', '?')}: {message}")
        if set(raw) - FIELD_KEYS:
            fail(f"unknown keys {sorted(set(raw) - FIELD_KEYS)}")
        self.name, self.type, self.doc = raw.get("name", ""), raw.get("type", ""), raw.get("doc", "")
        if not IDENTIFIER.match(self.name):
            fail("a name is PascalCase letters and digits")
        if self.type not in SIZES:
            fail(f"type {self.type!r} is none of {sorted(SIZES)}")
        self.count = raw.get("count")
        if self.count is not None and (type(self.count) is not int or self.count < 1):
            fail("a count is a whole number above 0")
        if self.type == "pad" and self.count is None:
            fail("padding needs a count")
        self.size = SIZES[self.type] * (self.count or 1)
        self.offset = 0
        self.snake = snake(self.name)


def fields(raw, part):
    out, at, names = [], 0, set()
    for f in (Field(r, part) for r in raw):
        if f.name in names:
            raise SchemaError(f"[{part}]: two fields {f.name}")
        names.add(f.name)
        f.offset, at = at, at + f.size
        out.append(f)
    return out, at


class Member:
    def __init__(self, raw, part):
        def fail(message):
            raise SchemaError(f"[[{part}]] {raw.get('key', '?')}: {message}")
        if set(raw) - MEMBER_KEYS:
            fail(f"unknown keys {sorted(set(raw) - MEMBER_KEYS)}")
        self.key, self.type, self.doc = raw.get("key", ""), raw.get("type", ""), raw.get("doc", "")
        if not KEY.match(self.key):
            fail("a key is letters, digits and _")
        if self.type not in MEMBER_TYPES:
            fail(f"type {self.type!r} is none of {sorted(MEMBER_TYPES)}")
        self.null, self.optional = raw.get("null", False), raw.get("optional", False)
        self.default, self.low, self.high = raw.get("default"), raw.get("low"), raw.get("high")
        ranged = self.low is not None or self.high is not None or self.default is not None
        if ranged and self.type != "int":
            fail("low, high and default belong to whole numbers")
        if self.type == "int" and (type(self.low) is not int or type(self.high) is not int or self.low > self.high):
            fail("a whole number has a low and a high")
        if self.default is not None and not self.low <= self.default <= self.high:
            fail("the default is in the range")
        self.name = camel_to_pascal(self.key)
        self.snake = snake(self.name)


def load(path=SCHEMA, record_size=None):
    """The schema, checked. record_size: a step's control record, relay.toml's (generate.py passes it)."""
    with open(path, "rb") as f:
        try:
            raw = tomllib.load(f)
        except tomllib.TOMLDecodeError as e:
            raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    if set(raw) - TOP_KEYS:
        raise SchemaError(f"unknown tables {sorted(set(raw) - TOP_KEYS)}")
    file = dict(raw.get("replay", {}))
    missing, unknown = REPLAY_KEYS - set(file), set(file) - REPLAY_KEYS
    if missing or unknown:
        raise SchemaError(f"[replay]: missing {sorted(missing)}, unknown {sorted(unknown)}")
    if file["endian"] != "big":
        raise SchemaError("[replay]: endian is big (no other is written)")
    marks = {"replay": file["mark"], "frames": raw.get("frames", {}).get("mark"),
             "lineup": raw.get("lineup", {}).get("mark"), "results": raw.get("results", {}).get("mark")}
    compact = dict(raw.get("compact", {}))
    if set(compact) != COMPACT_KEYS:
        raise SchemaError(f"[compact]: missing {sorted(COMPACT_KEYS - set(compact))}, unknown {sorted(set(compact) - COMPACT_KEYS)}")
    for part in ("header", "inputs", "checksums"):
        marks[f"compact {part}"] = compact[f"{part}_mark"]
    for part, mark in marks.items():
        if not (isinstance(mark, str) and mark.isascii() and len(mark) == 4):
            raise SchemaError(f"[{part}]: a mark is four ASCII characters")
    if len(set(marks.values())) != len(marks):
        raise SchemaError("the marks differ from each other")
    if file["max_seats"] > 32:
        raise SchemaError("[replay]: a mask is 32 bits")
    codecs = raw.get("codec", {})
    if "namespace" not in codecs.get("csharp", {}) or "prefix" not in codecs["csharp"]:
        raise SchemaError("[codec.csharp]: a namespace and a prefix")
    if "namespace" not in codecs.get("cxx", {}):
        raise SchemaError("[codec.cxx]: no namespace")

    versions = []
    for v in raw.get("version", []):
        if set(v) - VERSION_KEYS or type(v.get("number")) is not int or not IDENTIFIER.match(v.get("name", "")):
            raise SchemaError(f"[[version]] {v.get('number', '?')}: a number, a PascalCase name, a feature, a doc")
        if "feature" in v and not FEATURE.match(v["feature"]):
            raise SchemaError(f"[[version]] {v['number']}: a feature is snake_case")
        versions.append(v)
    numbers = [v["number"] for v in versions]
    if not versions or numbers != sorted(set(numbers)) or not 0 <= numbers[0] or numbers[-1] > 0xFFFF:
        raise SchemaError("[[version]]: one or more, in rising order, each a u16 once")
    if "feature" in versions[0] or any("feature" not in v for v in versions[1:]):
        raise SchemaError("[[version]]: the first has no feature, every later one has one")
    if len({v.get("feature") for v in versions}) != len(versions) or len({v["name"] for v in versions}) != len(versions):
        raise SchemaError("[[version]]: names and features once each")
    if file["written"] not in numbers:
        raise SchemaError("[replay]: written is one of the versions")
    if compact["version"] != numbers[-1] + 1 or compact["version"] > 0xFFFF:
        raise SchemaError("[compact]: version is the number after the last [[version]]")
    if compact["check"] != "crc32" or compact["preset"] not in range(10) or type(compact["full_every"]) is not int \
            or compact["full_every"] < 1 or not 4096 <= compact["dict_min"] <= compact["dict_max"] <= compact["memlimit"]:
        raise SchemaError("[compact]: check crc32, a preset 0 to 9, full_every above 0, dict_min <= dict_max <= memlimit")

    head, head_size = fields(raw.get("head", {}).get("fields", []), "head")
    if [(f.name, f.type) for f in head] != [("Version", "u16"), ("HeaderLength", "u32")]:
        raise SchemaError("[head]: Version (u16), then HeaderLength (u32)")
    frames, frames_size = fields(raw.get("frames", {}).get("fields", []), "frames")
    if [(f.name, f.type) for f in frames] != [("Mask", "u32"), ("Seat", "u32")]:
        raise SchemaError("[frames]: Mask (u32), then Seat (u32)")
    lineup_raw = raw.get("lineup", {})
    lineup, lineup_size = fields(lineup_raw.get("fields", []), "lineup")
    least = lineup_raw.get("least", lineup_size)
    if not lineup or type(least) is not int or not 0 < least <= lineup_size:
        raise SchemaError("[lineup]: fields, and least within the block")
    if set(lineup_raw) - {"mark", "least", "doc", "fields"}:
        raise SchemaError(f"[lineup]: unknown keys {sorted(set(lineup_raw) - {'mark', 'least', 'doc', 'fields'})}")
    if raw.get("steps", {}).get("checksum") != "u32" or set(raw["steps"]) != {"checksum"}:
        raise SchemaError("[steps]: checksum = \"u32\"")
    if type(record_size) is not int or record_size < 1:
        raise SchemaError("the relay's record size is needed (relay.toml's record_size)")
    results_raw = raw.get("results", {})
    if set(results_raw) != {"mark", "list"} or not KEY.match(results_raw.get("list", "")):
        raise SchemaError("[results]: a mark and the list's key")
    result = [Member(m, "result") for m in raw.get("result", [])]
    header = [Member(m, "header") for m in raw.get("header", [])]
    for part, members in (("result", result), ("header", header)):
        if not members or len({m.key for m in members}) != len(members):
            raise SchemaError(f"[[{part}]]: one or more, each key once")
    if any(m.type == "object" or m.optional for m in result):
        raise SchemaError("[[result]]: numbers and booleans only, every one in every entry")
    if any(m.null for m in header):
        raise SchemaError("[[header]]: no member is null (optional ones are left out)")

    file.update(compact=compact, codec=codecs, versions=versions, head=head, head_size=len(file["mark"]) + head_size,
                frames=frames, frames_size=len(marks["frames"]) + frames_size, frames_mark=marks["frames"],
                lineup=lineup, lineup_size=lineup_size, lineup_least=least, lineup_mark=marks["lineup"],
                lineup_doc=lineup_raw.get("doc", ""), record_size=record_size, checksum_size=4,
                results_mark=marks["results"], results_list=results_raw["list"], result=result, header=header)
    return file
