"""The status board's schema (board.toml), loaded and laid out: every block's and record's fields at their offsets,
the blocks on their lines, the board's size. generate.py hands the result to board_cs.py and board_cxx.py. A rule
the schema breaks is a SchemaError that names it. Standard library only."""
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "board.toml")

SIZES = {"u8": 1, "u16": 2, "u32": 4, "u64": 8, "i32": 4, "f32": 4}
KINDS = {"plain", "seqlock", "ring"}
WRITERS = {"game", "launcher", "both"}
BOARD_KEYS = {"name", "magic", "version", "endian", "block_align"}
BLOCK_KEYS = {"name", "kind", "writer", "frozen", "doc", "fields", "record", "slots"}
FIELD_KEYS = {"name", "type", "count", "size", "enum", "flags", "doc"}
IDENTIFIER = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
# The header as version 1 laid it out: no later version may move a word of it (a mismatched pair reads it to say so).
FROZEN_HEADER = [("Magic", "u32", 0), ("Version", "u32", 4), ("Size", "u32", 8), ("LauncherPid", "u32", 12),
                 ("RunnerVersion", "u32", 16), ("RunnerSize", "u32", 20), ("RunnerPid", "u32", 24)]


class SchemaError(Exception):
    pass


def snake(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


class Field:
    """A field of a record or block: a number, `bytes`, or `count` records. offset is from its record's or block's
    first byte."""

    def __init__(self, raw, owner, records, enums, flags):
        def fail(message):
            raise SchemaError(f"{owner}, field {raw.get('name', '?')}: {message}")
        unknown = set(raw) - FIELD_KEYS
        if unknown:
            fail(f"unknown keys {sorted(unknown)}")
        self.name, self.type, self.doc = raw.get("name", ""), raw.get("type", ""), raw.get("doc", "")
        if not IDENTIFIER.match(self.name):
            fail("a name is PascalCase letters and digits")
        self.count, self.record, self.enum, self.flags = raw.get("count"), None, raw.get("enum"), raw.get("flags")
        if self.type in SIZES:
            if self.count is not None or "size" in raw:
                fail("count and size belong to records and bytes")
            self.size, self.align = SIZES[self.type], SIZES[self.type]
        elif self.type == "bytes":
            size = raw.get("size")
            if type(size) is not int or size <= 0 or size % 4:
                fail("bytes need a size, a multiple of 4")
            self.size, self.align = size, 4
        elif self.type in records:
            if type(self.count) is not int or self.count <= 0:
                fail("an array of records needs a count")
            self.record = records[self.type]
            self.size, self.align = self.record.size * self.count, self.record.align
        else:
            fail(f"type {self.type!r} is none of {sorted(SIZES)}, bytes, or a record")
        if self.enum is not None and (self.enum not in enums or enums[self.enum].type != self.type):
            fail(f"enum {self.enum!r} is no enum of type {self.type}")
        if self.flags is not None and (self.flags not in flags or flags[self.flags]["type"] != self.type):
            fail(f"flags {self.flags!r} are no flags of type {self.type}")
        self.offset = 0

    @property
    def snake(self):
        return snake(self.name)


def place(fields, owner, start=0):
    """Offsets from `start`, each field at its natural alignment with no padding put in: a gap is the schema's to
    fill. Gives the end."""
    at, names = start, set()
    for f in fields:
        if f.name in names:
            raise SchemaError(f"{owner}: two fields {f.name}")
        names.add(f.name)
        if at % f.align:
            raise SchemaError(f"{owner}: {f.name} at {at} is not aligned to {f.align} (put a padding field before it)")
        f.offset = at
        at += f.size
    return at


class Record:
    def __init__(self, raw, records, enums, flags):
        self.name, self.doc = raw.get("name", "?"), raw.get("doc", "")
        if set(raw) - {"name", "doc", "fields"} or not IDENTIFIER.match(self.name):
            raise SchemaError(f"record {self.name}: a PascalCase name, a doc and fields")
        self.fields = [Field(f, f"record {self.name}", records, enums, flags) for f in raw.get("fields", [])]
        if any(f.record for f in self.fields):
            raise SchemaError(f"record {self.name}: no records inside a record")
        self.size = place(self.fields, f"record {self.name}")
        self.align = max((f.align for f in self.fields), default=1)
        if not self.fields or self.size % self.align:
            raise SchemaError(f"record {self.name}: its size {self.size} is no multiple of {self.align}")


class Enum:
    def __init__(self, raw, kind):
        self.name, self.type, self.doc = raw.get("name", "?"), raw.get("type"), raw.get("doc", "")
        self.values = raw.get("values", [])
        if set(raw) - {"name", "type", "doc", "values"} or not IDENTIFIER.match(self.name) or self.type not in SIZES:
            raise SchemaError(f"{kind} {self.name}: a PascalCase name, a number type, a doc and values")
        limit = 1 << (8 * SIZES[self.type])
        for v in self.values:
            if set(v) - {"name", "value", "doc"} or not IDENTIFIER.match(v.get("name", "")) or \
                    type(v.get("value")) is not int or not 0 <= v["value"] < limit:
                raise SchemaError(f"{kind} {self.name}: each value has a PascalCase name, a number that fits, a doc")
        for key in ("name", "value"):
            seen = [v[key] for v in self.values]
            if len(seen) != len(set(seen)):
                raise SchemaError(f"{kind} {self.name}: two values share a {key}")


class Block:
    def __init__(self, raw, records, enums, flags):
        self.name, self.doc = raw.get("name", "?"), raw.get("doc", "")
        unknown = set(raw) - BLOCK_KEYS
        if unknown or not IDENTIFIER.match(self.name):
            raise SchemaError(f"block {self.name}: a PascalCase name, and no keys but {sorted(BLOCK_KEYS)}")
        self.kind, self.writer, self.frozen = raw.get("kind"), raw.get("writer"), raw.get("frozen", False)
        if self.kind not in KINDS or self.writer not in WRITERS:
            raise SchemaError(f"block {self.name}: kind is one of {sorted(KINDS)}, writer one of {sorted(WRITERS)}")
        self.fields, self.record, self.slots = [], None, 0
        if self.kind == "ring":
            if "fields" in raw or raw.get("record") not in records or type(raw.get("slots")) is not int or \
                    raw["slots"] < 2:
                raise SchemaError(f"block {self.name}: a ring has a record and slots (2 or more), no fields")
            self.record, self.slots = records[raw["record"]], raw["slots"]
            if [(f.name, f.type) for f in self.record.fields[:1]] != [("Seq", "u32")]:
                raise SchemaError(f"block {self.name}: a ring's record starts with Seq, a u32")
            self.slots_at = 8                     # after Head (u64)
            self.size = self.slots_at + self.slots * self.record.size
            return
        if "record" in raw or "slots" in raw:
            raise SchemaError(f"block {self.name}: record and slots belong to a ring")
        self.fields = [Field(f, f"block {self.name}", records, enums, flags) for f in raw.get("fields", [])]
        if not self.fields:
            raise SchemaError(f"block {self.name}: no fields")
        if self.kind == "plain" and any(f.record or f.type == "bytes" for f in self.fields):
            raise SchemaError(f"block {self.name}: a plain block holds single words only")
        if any(f.name == "Seq" for f in self.fields):
            raise SchemaError(f"block {self.name}: Seq is the generator's")
        self.size = place(self.fields, f"block {self.name}", 4 if self.kind == "seqlock" else 0)

    @property
    def snake(self):
        return snake(self.name)


def load(path=SCHEMA):
    """The board, laid out: (board, enums, flags, records, blocks)."""
    with open(path, "rb") as f:
        try:
            raw = tomllib.load(f)
        except tomllib.TOMLDecodeError as e:
            raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    board = dict(raw.get("board", {}))
    missing, unknown = BOARD_KEYS - set(board), set(board) - BOARD_KEYS
    if missing or unknown:
        raise SchemaError(f"[board]: missing {sorted(missing)}, unknown {sorted(unknown)}")
    if board["endian"] != "little":
        raise SchemaError("[board]: endian is little (no other is written)")
    magic = board["magic"]
    if not (isinstance(magic, str) and magic.isascii() and len(magic) == 4):
        raise SchemaError("[board]: magic is four ASCII characters")
    board["magic_word"] = int.from_bytes(magic.encode("ascii"), "little")
    if type(board["version"]) is not int or not 1 <= board["version"] < 1 << 32:
        raise SchemaError("[board]: version is a number from 1")
    if board["block_align"] not in (8, 16, 32, 64, 128):
        raise SchemaError("[board]: block_align is 8 to 128, a power of two")
    codecs = raw.get("codec", {})
    for language in ("csharp", "cxx"):
        if "namespace" not in codecs.get(language, {}):
            raise SchemaError(f"[codec.{language}]: no namespace")
    board["codec"] = codecs
    enums = {}
    for e in raw.get("enum", []):
        enum = Enum(e, "enum")
        enums[enum.name] = enum
    flags = {}
    for g in raw.get("flags", []):
        group = Enum(g, "flags")
        flags[group.name] = {"name": group.name, "type": group.type, "doc": group.doc, "values": group.values}
    records = {}
    for r in raw.get("record", []):
        record = Record(r, records, enums, flags)
        if record.name in records or record.name in enums or record.name in flags:
            raise SchemaError(f"record {record.name}: the name is taken")
        records[record.name] = record
    blocks = [Block(b, records, enums, flags) for b in raw.get("block", [])]
    names = [b.name for b in blocks]
    if len(names) != len(set(names)):
        raise SchemaError("two blocks share a name")
    if not blocks or blocks[0].name != "Header" or not blocks[0].frozen:
        raise SchemaError("the first block is Header, frozen")
    header = [(f.name, f.type, f.offset) for f in blocks[0].fields]
    if header[:len(FROZEN_HEADER)] != FROZEN_HEADER:
        raise SchemaError(f"the Header block moved a word of version 1's ({FROZEN_HEADER})")
    if any(b.frozen for b in blocks[1:]):
        raise SchemaError("only the Header is frozen")
    at, align = 0, board["block_align"]
    for b in blocks:
        b.offset = at
        at = -(-(at + b.size) // align) * align
    board["size"] = at
    return board, enums, flags, records, blocks
