#!/usr/bin/env python3
"""The protocol's contracts, generated: one schema (relay.toml) of the relay's UDP packets, written out as a C# codec
and a C++ header, one (board.toml) of the status board between the launcher and a running game, written out as a C#
view and a C++ header, one (replay.toml) of the replay file, written out as a C++ reader and writer, a C# reader and
a Python reader, one (launch.toml) of the game's launch file, written out as the C++ reader's key reading and the
C# writer, and one (stats.toml) of a stat pack's ranges and code constants, written out as the game's C++ tables, the
C# lists and the two tables of the handbook's pages (PAGES), and one (rules.toml) of a host's rules, written out as
the C# rules list and race rules record and the game's C++ reader of a match start's rules into the race block's
words, so that no layout is typed twice (README.md). Follows
piconet-runner's port/facts/generate.py.

  generate.py           writes the generated files (OUTPUTS)
  generate.py --check   writes nothing, and fails when a generated file on disk is stale

Every path is this folder's own, so the folder can become a repository of its own, but
the handbook's pages (PAGES: the repository's handbook/, beside this folder), whose generated tables are spliced in.
Standard library only (Python 3.11 or later, for tomllib)."""
import os
import re
import sys
import tomllib

import board
import board_cs
import board_cxx
import codec_cs
import codec_cxx
import launch
import launch_cs
import launch_cxx
import replay
import replay_cs
import replay_cxx
import replay_cxx_compact
import replay_py
import rules
import rules_cs
import rules_cxx
import stats
import stats_cs
import stats_cxx
import stats_md
import theme
import theme_axaml
import theme_css
import theme_cxx

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "relay.toml")
# Where each generated file goes, from this folder. The describe file serves the round-trip test only.
OUTPUTS = {"csharp": "generated/RelayPackets.g.cs", "cxx": "generated/relay_packets.g.h",
           "cxx_describe": "tests/generated/relay_describe.g.h",
           "board_csharp": "generated/StatusBoard.g.cs", "board_cxx": "generated/status_board.g.h",
           "replay_csharp": "generated/ReplayFile.g.cs", "replay_cxx": "generated/replay_file.g.h",
           "replay_cxx_compact": "generated/replay_compact.g.h",
           "replay_python": "generated/replay_file.py",
           "launch_csharp": "generated/LaunchFile.g.cs", "launch_cxx": "generated/launch_file.g.h",
           "stats_csharp": "generated/StatTables.g.cs", "stats_ranges_cxx": "generated/stat_ranges.g.h",
           "stats_constants_cxx": "generated/const_ranges.g.h",
           "rules_csharp": "generated/HostRules.g.cs", "rules_cxx": "generated/race_rules.g.h",
           "theme_axaml": "generated/Theme.g.axaml", "theme_cxx": "generated/theme_palette.g.h",
           "theme_css": "generated/theme.g.css"}
# The pages whose tables are generated (between their marker lines, stats_md.py), from the repository's root: the
# folder above this one.
ROOT = os.path.dirname(here)
PAGES = {"stats_ranges": "handbook/stat-packs.md", "stats_constants": "handbook/stat-pack-constants.md"}

SIZES = {"u8": 1, "u32": 4, "u64": 8, "i64": 8}   # every number big-endian
CS_TYPES = {"int"}                                 # a field's `cs`: a wider C# type, written as the field's type
PROTOCOL_KEYS = {"name", "version", "mark", "endian", "header_size", "record_size", "ai_drives",
                 "max_frames_per_packet", "max_payload"}
PACKET_KEYS = {"name", "record", "type", "doc", "fields", "cs_order"}
FIELD_KEYS = {"name", "type", "cs", "optional", "count", "per", "default"}
FLAGS_KEYS = {"name", "type", "cxx_prefix", "doc", "values"}
IDENTIFIER = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
KEY = "Key"   # the seat's key: the first field, a u64, of every packet the relay sends a game (relay.toml's header)


class SchemaError(Exception):
    pass


class Field:
    def __init__(self, raw, packet):
        def fail(message):
            raise SchemaError(f"packet {packet}, field {raw.get('name', '?')}: {message}")
        unknown = set(raw) - FIELD_KEYS
        if unknown:
            fail(f"unknown keys {sorted(unknown)}")
        self.name, self.type = raw.get("name", ""), raw.get("type", "")
        if not IDENTIFIER.match(self.name):
            fail("a name is PascalCase letters and digits")
        if self.type not in SIZES and self.type != "records":
            fail(f"type {self.type!r} is none of {sorted(SIZES) + ['records']}")
        self.default = raw.get("default")
        if self.default is not None and (type(self.default) is not int or self.type not in SIZES):
            fail("a default is a number, of a number field (C# only: a parameter callers may leave out)")
        self.cs = raw.get("cs")
        if self.cs is not None and (self.cs not in CS_TYPES or self.type != "u8"):
            fail(f"cs {self.cs!r}: only a u8 may be a C# int")
        self.optional = raw.get("optional", False)
        if self.optional is not False and (self.optional is not True or self.type == "records"):
            fail("optional is true or absent, and never on records")
        self.count, self.per = raw.get("count"), raw.get("per")
        if (self.type == "records") != (self.count is not None and self.per is not None):
            fail("count and per belong to records, and records need both")
        self.size = SIZES.get(self.type, 0)
        self.offset = 0          # from the body's first byte
        self.seats = None        # records: the mask field whose bits each count once more, or None for "1"

    @property
    def snake(self):
        return re.sub(r"(?<!^)(?=[A-Z])", "_", self.name).lower()


class Packet:
    def __init__(self, raw, protocol):
        name = raw.get("name", "?")
        unknown = set(raw) - PACKET_KEYS
        if unknown:
            raise SchemaError(f"packet {name}: unknown keys {sorted(unknown)}")
        if not IDENTIFIER.match(name):
            raise SchemaError(f"packet {name}: a name is PascalCase letters and digits")
        self.name, self.record, self.doc = name, raw.get("record", name), raw.get("doc", "")
        self.type = raw.get("type")
        if type(self.type) is not int or not 0 <= self.type <= 0xFF:
            raise SchemaError(f"packet {name}: type is a number 0 to 255")
        self.fields = [Field(f, name) for f in raw.get("fields", [])]
        self.raw_order = raw.get("cs_order")
        self.place(protocol)

    def place(self, protocol):
        """Offsets, the fixed part's size, and the rules of the optional field and the records."""
        names, at = {}, 0
        for i, f in enumerate(self.fields):
            last = i == len(self.fields) - 1
            if f.name in names:
                raise SchemaError(f"packet {self.name}: two fields {f.name}")
            if (f.optional or f.type == "records") and not last:
                raise SchemaError(f"packet {self.name}: {f.name} must be the last field")
            if f.type == "records":
                count = names.get(f.count)
                if count is None or count.type != "u8":
                    raise SchemaError(f"packet {self.name}: records count {f.count!r} is no earlier u8 field")
                if f.per != "1":
                    mask = names.get(f.per.removeprefix("seats:")) if f.per.startswith("seats:") else None
                    if mask is None or mask.type != "u32":
                        raise SchemaError(f"packet {self.name}: per {f.per!r} is neither \"1\" nor seats:<u32 field>")
                    f.seats = mask
                f.count = count
            f.offset = at
            names[f.name] = f
            at += f.size
        self.head = sum(f.size for f in self.fields if not f.optional)
        self.optional = next((f for f in self.fields if f.optional), None)
        self.records = next((f for f in self.fields if f.type == "records"), None)
        self.protocol = protocol
        order = self.raw_order or [f.name for f in self.fields]
        if sorted(order) != sorted(names):
            raise SchemaError(f"packet {self.name}: cs_order names every field once")
        self.cs_fields = [names[n] for n in order]
        seen_default = False
        for f in self.cs_fields:
            if seen_default and f.default is None and not f.optional:
                raise SchemaError(f"packet {self.name}: {f.name} has no default after a field with one (cs_order)")
            seen_default = seen_default or f.default is not None or f.optional

    @property
    def from_relay(self):
        """A packet the relay sends a game: its body opens with the seat's key."""
        return self.fields[0].name == KEY if self.fields else False

    @property
    def fixed(self):
        """The fields at fixed places: every field but the records."""
        return [f for f in self.fields if f.type != "records"]


def load(path=SCHEMA):
    """The schema, checked: the protocol's numbers and its packets. A rule broken is a SchemaError naming it."""
    with open(path, "rb") as f:
        try:
            raw = tomllib.load(f)
        except tomllib.TOMLDecodeError as e:
            raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    protocol = dict(raw.get("protocol", {}))
    missing, unknown = PROTOCOL_KEYS - set(protocol), set(protocol) - PROTOCOL_KEYS
    if missing or unknown:
        raise SchemaError(f"[protocol]: missing {sorted(missing)}, unknown {sorted(unknown)}")
    if protocol["endian"] != "big":
        raise SchemaError("[protocol]: endian is big (no other is written)")
    mark = protocol["mark"]
    if not (isinstance(mark, str) and mark.isascii() and 1 <= len(mark) <= 4):
        raise SchemaError("[protocol]: mark is one to four ASCII characters")
    if protocol["header_size"] != len(mark) + 2:
        raise SchemaError("[protocol]: header_size is the mark, the version and the type")
    if not 0 <= protocol["version"] <= 0xFF:
        raise SchemaError("[protocol]: version is one byte")
    drives = protocol["ai_drives"]
    if len(drives) != protocol["record_size"] or not all(0 <= b <= 0xFF for b in drives):
        raise SchemaError("[protocol]: ai_drives is one record of bytes")
    codecs = raw.get("codec", {})
    for language in ("csharp", "cxx"):
        if "namespace" not in codecs.get(language, {}):
            raise SchemaError(f"[codec.{language}]: no namespace")
    protocol["codec"] = codecs
    packets = [Packet(p, protocol) for p in raw.get("packet", [])]
    for key in ("name", "record", "type"):
        seen = [getattr(p, key) for p in packets]
        twice = sorted({str(v) for v in seen if seen.count(v) > 1})
        if twice:
            raise SchemaError(f"packets share a {key}: {', '.join(twice)}")
    if not packets:
        raise SchemaError("no packets")
    for p in packets:
        if any(f.name == KEY for f in p.fields) and (p.fields[0].name != KEY or p.fields[0].type != "u64"):
            raise SchemaError(f"packet {p.name}: {KEY} (the seat's key) is the first field, a u64")
    protocol["key_size"] = SIZES["u64"]
    protocol["flags"] = [flags(f) for f in raw.get("flags", [])]
    return protocol, packets


def flags(raw):
    """A group of named bits a field carries (as Joined's Flags): constants, in C# a static class, in C++ PREFIX_NAME."""
    unknown = set(raw) - FLAGS_KEYS
    name = raw.get("name", "?")
    if unknown or not IDENTIFIER.match(name) or raw.get("type") not in SIZES:
        raise SchemaError(f"flags {name}: a PascalCase name, a number type, and no keys but {sorted(FLAGS_KEYS)}")
    for v in raw.get("values", []):
        if set(v) - {"name", "value", "doc"} or not IDENTIFIER.match(v.get("name", "")) or type(v.get("value")) is not int:
            raise SchemaError(f"flags {name}: each value has a PascalCase name, a number and a doc")
    return raw


def generated(path=SCHEMA, board_path=board.SCHEMA, replay_path=replay.SCHEMA, launch_path=launch.SCHEMA,
              stats_path=stats.SCHEMA, rules_path=rules.SCHEMA, theme_path=theme.SCHEMA):
    protocol, packets = load(path)
    laid = board.load(board_path)
    file = replay.load(replay_path, protocol["record_size"])   # a step's records are the relay's
    started = launch.load(launch_path)
    tables = stats.load(stats_path)
    host = rules.load(rules_path)
    rules.check_replay(host, {v["feature"] for v in file["versions"] if v.get("feature")})
    tokens = theme.load(theme_path)
    return {OUTPUTS["csharp"]: codec_cs.emit(protocol, packets), OUTPUTS["cxx"]: codec_cxx.emit(protocol, packets),
            OUTPUTS["cxx_describe"]: codec_cxx.emit_describe(protocol, packets),
            OUTPUTS["board_csharp"]: board_cs.emit(*laid), OUTPUTS["board_cxx"]: board_cxx.emit(*laid),
            OUTPUTS["replay_csharp"]: replay_cs.emit(file), OUTPUTS["replay_cxx"]: replay_cxx.emit(file),
            OUTPUTS["replay_cxx_compact"]: replay_cxx_compact.emit(file),
            OUTPUTS["replay_python"]: replay_py.emit(file),
            OUTPUTS["launch_csharp"]: launch_cs.emit(started), OUTPUTS["launch_cxx"]: launch_cxx.emit(started),
            OUTPUTS["stats_csharp"]: stats_cs.emit(tables), OUTPUTS["stats_ranges_cxx"]: stats_cxx.emit_ranges(tables),
            OUTPUTS["stats_constants_cxx"]: stats_cxx.emit_constants(tables),
            OUTPUTS["rules_csharp"]: rules_cs.emit(host), OUTPUTS["rules_cxx"]: rules_cxx.emit(host),
            OUTPUTS["theme_axaml"]: theme_axaml.emit(tokens), OUTPUTS["theme_cxx"]: theme_cxx.emit(tokens),
            OUTPUTS["theme_css"]: theme_css.emit(tokens)}


def pages(root=ROOT, stats_path=stats.SCHEMA):
    """The handbook's pages, each with its generated table spliced in: path from the root -> the page's text."""
    tables = stats.load(stats_path)
    out = {}
    for key, table in (("stats_ranges", stats_md.ranges(tables)), ("stats_constants", stats_md.constants(tables))):
        with open(os.path.join(root, PAGES[key]), encoding="utf-8", newline="") as f:
            out[PAGES[key]] = stats_md.splice(f.read(), PAGES[key], table)
    return out


def main(args, folder=here, path=SCHEMA, root=ROOT):
    """`folder` takes the generated files, `root` holds the pages (PAGES): a page is written only when its table
    changed."""
    if args not in ([], ["--check"]):
        print(__doc__, file=sys.stderr)
        return 2
    try:
        files = {os.path.join(folder, name): text for name, text in generated(path).items()}
        files.update({os.path.join(root, name): text for name, text in pages(root).items()})
    except (SchemaError, board.SchemaError, replay.SchemaError, launch.SchemaError, stats.SchemaError,
            rules.SchemaError, theme.SchemaError) as e:
        print(f"generate.py: {e}", file=sys.stderr)
        return 1
    stale = []
    for full, text in files.items():
        name = os.path.relpath(full, folder)
        try:
            with open(full, encoding="utf-8", newline="") as f:
                current = f.read()
        except FileNotFoundError:
            current = None
        if args:
            if current != text:
                stale.append(name)
        elif current != text:
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
    if stale:
        print("stale (run protocol/generate.py): " + ", ".join(stale), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
