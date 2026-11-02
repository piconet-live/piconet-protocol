"""A host's rules (rules.toml), loaded and checked: every rule in the operator's order, with its range, and the rules
the games apply with their race block's words. generate.py hands the result to rules_cs.py and rules_cxx.py; the
runner's port/facts/generate.py takes the words' offsets and the limits from `names`. A rule the file breaks is a
SchemaError that names it. Standard library only."""
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "rules.toml")

TOP_KEYS = {"codec", "race_block", "rule"}
BLOCK_KEYS = {"size", "steps_a_second", "others"}
RULE_KEYS = {"name", "setup", "label", "cs", "reach", "kind", "least", "most", "step", "default", "unit", "takes", "doc", "race"}
RACE_KEYS = {"key", "absent", "word", "at", "steps", "flag", "replay", "doc"}
REACHES = {"alone": "Alone", "together": "Together", "server": "Server", "practice": "Practice"}
KINDS = {"course": "Course", "courses": "Courses", "number": "Number", "switch": "Switch", "range": "Range",
         "choice": "Choice", "delay": "Delay", "machines": "Machines", "pack": "Pack", "text": "Text"}
RACE_KINDS = {"number", "switch", "delay"}     # what a word of the race block can hold
STEPS = {None: 1, "seconds": 1, "ms": 1000}    # a value's unit, per second (None: the value itself)
NAME = re.compile(r"[a-z][a-z0-9]*(-[a-z0-9]+)*\Z")
IDENTIFIER = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
CONSTANT = re.compile(r"[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+\Z")   # a C# constant by name
KEY = re.compile(r"[a-z][A-Za-z0-9]*\Z")
WORD = re.compile(r"RACE_[A-Z0-9_]+\Z")
FLAG = re.compile(r"--[a-z0-9]+(-[a-z0-9]+)*\Z")
PLACES = re.compile(r"\{(range|least|most|step)\}")


class SchemaError(Exception):
    pass


def snake(name):
    return name.replace("-", "_")


def pascal(key):
    return key[0].upper() + key[1:]


class Race:
    """A rule the games apply: its member of RaceRules and its word of the race block."""

    def __init__(self, raw, rule):
        def fail(message):
            raise SchemaError(f"rule {rule.name}, race: {message}")
        if not isinstance(raw, dict) or set(raw) - RACE_KEYS:
            fail(f"a table of {sorted(RACE_KEYS)}")
        self.key, self.word, self.at = raw.get("key", ""), raw.get("word", ""), raw.get("at")
        if not KEY.match(self.key):
            fail("key is camelCase letters and digits")
        self.cs = pascal(self.key)
        if not WORD.match(self.word):
            fail("word is RACE_ and capitals")
        if type(self.at) is not int or self.at % 4:
            fail("at is a word's offset in the race block")
        if rule.kind not in RACE_KINDS:
            fail(f"a {rule.kind} is no word of the race block (only {sorted(RACE_KINDS)})")
        if any(type(v) is not int for v in (rule.least, rule.most, rule.step, rule.default)):
            fail("the range and the default of a rule the games apply are whole numbers (the game's own)")
        self.absent = raw.get("absent")
        if rule.kind == "switch":
            if type(self.absent) is not bool:
                fail("a switch's absent is true or false")
        elif type(self.absent) is not int or not rule.takes(self.absent):
            fail("absent is a number the rule takes")
        self.steps = raw.get("steps")
        if self.steps not in STEPS or (self.steps and rule.kind == "switch"):
            fail("steps is \"seconds\" or \"ms\", or none; a switch has none")
        self.flag, self.replay, self.doc = raw.get("flag", ""), raw.get("replay"), raw.get("doc", "")
        if rule.reach == "practice":
            if self.flag:
                fail("a practice's rule has no server's option")
        elif not FLAG.match(self.flag):
            fail("flag is the server's option, --words-with-dashes")
        if not self.doc:
            fail("no doc")
        # the game's clamp: a delay from 0 (off), the rest from their least; to the most
        self.low = 0 if rule.kind == "delay" else rule.least
        self.high = rule.most
        self.per_second = STEPS[self.steps]


class Rule:
    def __init__(self, raw):
        name = raw.get("name", "?")

        def fail(message):
            raise SchemaError(f"rule {name}: {message}")
        if set(raw) - RULE_KEYS:
            fail(f"unknown keys {sorted(set(raw) - RULE_KEYS)}")
        if not NAME.match(name):
            fail("a name is lower-case words between dashes")
        self.name, self.label, self.doc = name, raw.get("label", ""), raw.get("doc", "")
        self.setup = raw.get("setup")
        if not self.setup:
            fail("no setup: its mode word, or flow, or practice")
        if not self.label:
            fail("no label")
        self.cs = raw.get("cs")
        if self.cs is not None and not IDENTIFIER.match(self.cs):
            fail("cs is a PascalCase member name")
        self.reach, self.kind = raw.get("reach"), raw.get("kind")
        if self.reach not in REACHES:
            fail(f"reach is one of {sorted(REACHES)}")
        if self.kind not in KINDS:
            fail(f"kind is one of {sorted(KINDS)}")
        for key, fallback in (("least", 0), ("most", 0), ("step", 1), ("default", 0)):
            v = raw.get(key, fallback)
            if not (type(v) is int or (isinstance(v, str) and CONSTANT.match(v))):
                fail(f"{key} is a whole number or a C# constant by name (\"GameFacts.Limits.Seats\")")
            setattr(self, key, v)
        if all(type(v) is int for v in (self.least, self.most, self.step)):
            if self.least > self.most or self.step < 1:
                fail("least is at most most, and step is 1 or more")
            if type(self.default) is int and self.default and not self.takes(self.default):
                fail(f"the default {self.default} is no value the rule takes")
        if self.kind == "switch" and (self.least, self.most, self.step) != (0, 1, 1):
            fail("a switch is 0 to 1")
        self.unit = raw.get("unit", "")
        if self.unit not in ("", "s", "ms"):
            fail("unit is \"s\" or \"ms\", or none")
        self.taken = self.taken_text(raw.get("takes"), fail)
        self.race = Race(raw["race"], self) if "race" in raw else None

    def takes(self, value):
        """HostRule.Takes: inside the range on its steps; a delay's 0 (off) too."""
        return (self.kind == "delay" and value == 0) or (
            self.least <= value <= self.most and (value - self.least) % self.step == 0)

    def taken_text(self, takes, fail):
        """What the rule may be, as a refusal says it (`takes` with its places filled), for a number, a delay and a
        switch; None for the rest."""
        if self.kind not in ("number", "delay", "switch"):
            if takes is not None:
                fail("takes is a number's, a delay's or a switch's")
            return None
        constants = not all(type(getattr(self, k)) is int for k in ("least", "most", "step"))
        if takes is None:
            if self.kind == "delay":
                fail("a delay says what it takes (takes)")
            if constants:
                return None   # its range is C#'s own (HostRule.Range)
            takes = "on or off" if self.kind == "switch" else "{range}"
        if not isinstance(takes, str) or not takes:
            fail("takes is a text")
        places = set(PLACES.findall(takes))
        if places and constants:
            fail("takes names numbers that are C# constants: write it without them")
        if re.sub(PLACES, "", takes).count("{") or re.sub(PLACES, "", takes).count("}"):
            fail("takes names a place other than {range}, {least}, {most}, {step}")
        return PLACES.sub(lambda m: f"{self.least} to {self.most}" if m.group(1) == "range"
                          else str(getattr(self, m.group(1))), takes)


def load(path=SCHEMA):
    """The file, checked: {"codec", "block", "rules", "race"} (race: the rules the games apply, in RaceRules' order,
    which is the operator's)."""
    with open(path, "rb") as f:
        try:
            raw = tomllib.load(f)
        except tomllib.TOMLDecodeError as e:
            raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    if set(raw) - TOP_KEYS:
        raise SchemaError(f"unknown tables {sorted(set(raw) - TOP_KEYS)}")
    codec = raw.get("codec", {})
    for language in ("csharp", "cxx"):
        if "namespace" not in codec.get(language, {}):
            raise SchemaError(f"[codec.{language}]: no namespace")
    block = raw.get("race_block", {})
    if set(block) != BLOCK_KEYS or type(block["size"]) is not int or type(block["steps_a_second"]) is not int:
        raise SchemaError(f"[race_block] has {sorted(BLOCK_KEYS)}")
    others = []
    for o in block["others"]:
        if set(o) != {"at", "end", "what"} or not 0 <= o["at"] < o["end"] <= block["size"]:
            raise SchemaError(f"[race_block] others: {o} is no part of the block (at, end, what)")
        others.append((o["at"], o["end"], o["what"]))
    rules = [Rule(r) for r in raw.get("rule", [])]
    if not rules:
        raise SchemaError("no rules")
    for key in ("name", "cs"):
        seen = [getattr(r, key) for r in rules if getattr(r, key) is not None]
        twice = sorted({v for v in seen if seen.count(v) > 1})
        if twice:
            raise SchemaError(f"rules share a {key}: {', '.join(twice)}")
    race = [r for r in rules if r.race]
    for key in ("key", "word", "flag"):
        seen = [getattr(r.race, key) for r in race if getattr(r.race, key)]
        twice = sorted({v for v in seen if seen.count(v) > 1})
        if twice:
            raise SchemaError(f"rules the games apply share a {key}: {', '.join(twice)}")
    taken = list(others)
    for r in race:
        at = r.race.at
        if not 0 <= at <= block["size"] - 4:
            raise SchemaError(f"rule {r.name}: its word at {at} is outside the race block's {block['size']} bytes")
        clash = next((t for t in taken if at < t[1] and t[0] < at + 4), None)
        if clash:
            raise SchemaError(f"rule {r.name}: its word at {at} overlaps {clash[2]} ({clash[0]} to {clash[1]})")
        if r.race.per_second > 1 and r.step * block["steps_a_second"] % r.race.per_second:
            raise SchemaError(f"rule {r.name}: its step of {r.step} {r.race.steps} is no whole number of steps")
        if r.race.high * block["steps_a_second"] > 0xFFFFFFFF:
            raise SchemaError(f"rule {r.name}: its most, as steps, does not fit a word")
        if r.cs is None:
            raise SchemaError(f"rule {r.name}: a rule the games apply has its HostRules member (cs)")
        taken.append((at, at + 4, f"rule {r.name}'s word"))
    return {"codec": codec, "block": block, "others": others, "rules": rules, "race": race}


def free_words(s):
    """The offsets of the race block's words no rule and no other part holds: where a new rule's word may go."""
    taken = s["others"] + [(r.race.at, r.race.at + 4) for r in s["race"]]
    return [at for at in range(0, s["block"]["size"] - 3, 4) if not any(at < t[1] and t[0] < at + 4 for t in taken)]


def names(s):
    """The numbers piconet-runner's facts table may name (port/facts/generate.py schema_names): per rule the games
    apply rule_at_NAME (its word's offset), rule_least_NAME, rule_most_NAME, rule_step_NAME; rule_race_size,
    rule_steps_a_second."""
    out = {"rule_race_size": s["block"]["size"], "rule_steps_a_second": s["block"]["steps_a_second"]}
    for r in s["race"]:
        n = snake(r.name)
        out.update({f"rule_at_{n}": r.race.at, f"rule_least_{n}": r.least, f"rule_most_{n}": r.most,
                    f"rule_step_{n}": r.step})
    return out


def check_replay(s, features):
    """Every rule's replay feature is one of the replay file's (replay.toml's [[version]] features)."""
    for r in s["race"]:
        if r.race.replay is not None and r.race.replay not in features:
            raise SchemaError(f"rule {r.name}: replay {r.race.replay!r} is no version's feature of replay.toml")
