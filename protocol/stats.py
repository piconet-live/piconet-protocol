"""A stat pack's tables (stats.toml), loaded and checked: the ranges of a block's words and the code constants'
slots. generate.py hands the result to stats_cxx.py, stats_cs.py and stats_md.py. A rule the file breaks is a
SchemaError that names it. Standard library only."""
import math
import os
import re
import struct
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "stats.toml")

TOP_KEYS = {"codec", "ranges", "constant"}
RULE_KEYS = {"at", "name", "kind", "low", "high", "below", "z_least", "tried"}
CONSTANT_KEYS = {"name", "bits", "stock", "low", "high", "seat", "game_place", "what", "read_by", "unleashed"}
KINDS = {"number": 1, "bytes": 1, "points": 12}   # the words a row covers
BLOCK_WORDS = 44     # the words of a car data block a pack may change: all but word 0 (the facts' CAR_BLOCK / 4 - 1)
BYTES_AT = 0x48      # the one word of four bytes (the facts' CAR_BYTES_AT; the runner's stat_ranges.h asserts it)
SEAT_RECORD = 0x100  # bytes of a seat's constants (the facts' SEAT_CONSTANT)
MAX_DOUBLE = 1.7976931348623157e308
IDENTIFIER = re.compile(r"[A-Z][A-Za-z0-9]*\Z")


class SchemaError(Exception):
    pass


def f32(value):
    """The 32-bit float a C++ or C# literal of this value is."""
    return struct.unpack("<f", struct.pack("<f", value))[0]


def number(value):
    return type(value) in (int, float) and not isinstance(value, bool)


class Rule:
    def __init__(self, raw):
        name = raw.get("name", "?")

        def fail(message):
            raise SchemaError(f"range {name}: {message}")
        if set(raw) - RULE_KEYS:
            fail(f"unknown keys {sorted(set(raw) - RULE_KEYS)}")
        if not IDENTIFIER.match(name):
            fail("a name is PascalCase letters and digits")
        self.name, self.kind = name, raw.get("kind")
        if self.kind not in KINDS:
            fail(f"kind {self.kind!r} is none of {sorted(KINDS)}")
        self.at = raw.get("at")
        if type(self.at) is not int or self.at % 4 or not 4 <= self.at < 4 * (BLOCK_WORDS + 1):
            fail("at is a word's offset in a block, 4 or more")
        for key in ("low", "high"):
            if not number(raw.get(key)) or not math.isfinite(raw[key]):
                fail(f"{key} is a number")
        self.low, self.high = raw["low"], raw["high"]
        self.below = raw.get("below", False)
        self.z_least = raw.get("z_least", 0)
        self.tried = raw.get("tried")
        if self.below not in (True, False) or (self.below and self.kind != "number"):
            fail("below is true or absent, and a number's only")
        if not number(self.z_least) or (self.z_least and (self.kind != "points" or self.z_least <= 0)):
            fail("z_least is a positive number, and points' only")
        if self.tried is not None and (not number(self.tried) or self.kind != "number" or self.low > 0
                                       or not 0 < self.tried <= self.high):
            fail("tried is a number's, from 0 to at most high (and low is 0 or less)")
        if not self.low <= self.high:
            fail("low is at most high")
        if self.kind == "points" and self.low != -self.high:
            fail("points' low is -high")
        if self.kind == "bytes" and (self.at != BYTES_AT or self.low != 0 or type(self.high) is not int
                                     or not 0 <= self.high <= 0xFF):
            fail(f"bytes is the word at 0x{BYTES_AT:02X}: low 0, high the most of its second byte")
        self.words = KINDS[self.kind]


class Constant:
    def __init__(self, raw, slot):
        name = raw.get("name", "?")

        def fail(message):
            raise SchemaError(f"constant {name}: {message}")
        if set(raw) - CONSTANT_KEYS:
            fail(f"unknown keys {sorted(set(raw) - CONSTANT_KEYS)}")
        if not IDENTIFIER.match(name):
            fail("a name is PascalCase letters and digits")
        self.name, self.slot, self.bits = name, slot, raw.get("bits")
        if self.bits not in (32, 64):
            fail("bits is 32 or 64")
        self.stock, self.seat = raw.get("stock"), raw.get("seat")
        if type(self.stock) is not int or not 0 <= self.stock < 1 << self.bits:
            fail(f"stock is the game's {self.bits} bits")
        if type(self.seat) is not int or self.seat % 4 or not 0 <= self.seat <= SEAT_RECORD - self.bits // 8:
            fail(f"seat is a word's offset in a seat's record ({SEAT_RECORD:#x} bytes)")
        for key in ("low", "high"):
            if not number(raw.get(key)) or not math.isfinite(raw[key]):
                fail(f"{key} is a number")
        self.low, self.high = float(raw["low"]), float(raw["high"])
        if not self.low <= self.stock_value <= self.high:
            fail("the game's own value is in its range")
        for key in ("what", "read_by"):
            if not isinstance(raw.get(key), str) or not raw[key]:
                fail(f"{key} is a text")
        self.what, self.read_by = raw["what"], raw["read_by"]
        self.game_place, self.unleashed = raw.get("game_place"), raw.get("unleashed", "the game's")
        for key, value in (("game_place", self.game_place), ("unleashed", self.unleashed)):
            if value is not None and (not isinstance(value, str) or not value or "|" in value):
                fail(f"{key} is a text with no |")
        if "|" in self.what + self.read_by:
            fail("what and read_by have no |: they are cells of the handbook's table")

    @property
    def wide(self):
        return self.bits == 64

    @property
    def stock_value(self):
        if self.wide:
            return struct.unpack(">d", self.stock.to_bytes(8, "big"))[0]
        return struct.unpack(">f", self.stock.to_bytes(4, "big"))[0]

    def words(self):
        """The constant's slots: (slot, kind, the seat's place, the stock's word, low, high); a 64-bit one's low word
        bounds nothing (0, 0)."""
        if not self.wide:
            return [(self.slot, "Number32", self.seat, self.stock, self.low, self.high)]
        return [(self.slot, "Number64High", self.seat, self.stock >> 32, self.low, self.high),
                (self.slot + 1, "Number64Low", self.seat + 4, self.stock & 0xFFFFFFFF, 0.0, 0.0)]


class Tables:
    def __init__(self, codec, rules, constants):
        self.codec, self.rules, self.constants = codec, rules, constants

    @property
    def slots(self):
        """Every slot in order: (constant, slot, kind, the seat's place, the stock's word, low, high)."""
        return [(c,) + w for c in self.constants for w in c.words()]


def load(path=SCHEMA):
    with open(path, "rb") as f:
        try:
            raw = tomllib.load(f)
        except tomllib.TOMLDecodeError as e:
            raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    if set(raw) - TOP_KEYS:
        raise SchemaError(f"unknown tables {sorted(set(raw) - TOP_KEYS)}")
    codec = raw.get("codec", {})
    if not {"ranges_namespace", "constants_namespace"} <= set(codec.get("cxx", {})) or "namespace" not in codec.get("csharp", {}):
        raise SchemaError("[codec.cxx] names ranges_namespace and constants_namespace, [codec.csharp] namespace")
    if set(raw.get("ranges", {})) != {"rule"}:
        raise SchemaError("[ranges] holds rule = [...] alone")
    rules = [Rule(r) for r in raw["ranges"]["rule"]]
    word = 1
    for r in rules:
        if r.at != 4 * word:
            raise SchemaError(f"range {r.name}: at 0x{r.at:02X}, where the rows before end at 0x{4 * word:02X} "
                              "(by offset, every word once)")
        word += r.words
    if word != BLOCK_WORDS + 1:
        raise SchemaError(f"the ranges cover {word - 1} words of a block, not {BLOCK_WORDS}")
    constants, slot = [], 0
    for c in raw.get("constant", []):
        constants.append(Constant(c, slot))
        slot += 2 if constants[-1].wide else 1
    if not constants:
        raise SchemaError("no constants")
    for key in ("name", "seat"):
        seen = [getattr(c, key) for c in constants]
        twice = sorted({str(v) for v in seen if seen.count(v) > 1})
        if twice:
            raise SchemaError(f"constants share a {key}: {', '.join(twice)}")
    places = sorted((c.seat, c.seat + c.bits // 8, c.name) for c in constants)
    for (_, end, a), (start, _, b) in zip(places, places[1:]):
        if start < end:
            raise SchemaError(f"constants {a} and {b} overlap in a seat's record")
    return Tables(codec, rules, constants)


# The numbers as each output writes them.
def literal(value, single):
    """A C++ or C# literal: an integer as one; else the decimal, a float's with f. The largest double: None."""
    if value == MAX_DOUBLE:
        return None
    if float(value).is_integer() and abs(value) < 1e15:
        return str(int(value))
    return repr(float(value)) + ("f" if single else "")


def dotnet_text(value, single):
    """The shortest text that reads back to the same bits, as .NET's "R" writes it (what the C# tests compare the
    handbook with): plain decimals, and E+NN or E-NN from an exponent of 15 on or of -5 and below."""
    from decimal import Decimal
    if value == 0:
        return "-0" if math.copysign(1, value) < 0 else "0"
    if single:
        text = next(t for t in (f"{value:.{p}g}" for p in range(1, 10)) if f32(float(t)) == value)
    else:
        text = repr(value)
    sign, digits, exponent = Decimal(text).normalize().as_tuple()
    places = len(digits) - 1 + exponent
    if places >= 15 or places < -4:
        mantissa = str(digits[0]) + ("." + "".join(map(str, digits[1:])) if len(digits) > 1 else "")
        return f"{'-' if sign else ''}{mantissa}E{'+' if places >= 0 else '-'}{abs(places):02d}"
    return format(Decimal(text).normalize(), "f")


def handbook_text(value):
    """A range's number as the handbook's table writes it: six decimals at most, no zeros at the end."""
    text = f"{f32(value):.6f}".rstrip("0").rstrip(".")
    return "0" if text == "-0" else text
