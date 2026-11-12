"""The game mode system's vocabulary and bytecode (modes.toml), loaded and checked. generate.py hands the result to
modes_cs.py (Online.Protocol's ModeWords, which the compiler reads) and modes_cxx.py (the game's fzgx::modes, which
the interpreter reads). A rule the file breaks is a SchemaError that names it. Standard library only."""
import hashlib
import json
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "modes.toml")

TOP_KEYS = {"codec", "levels", "api", "op", "event", "setup", "seat_read", "race_read", "action", "choice"}
API_KEYS = {"number", "budget", "queue", "globals", "seat_words", "locals", "rules", "sorts", "package_bytes", "seats",
            "team_words", "teams", "waits", "wait_locals", "subs", "board", "board_label", "board_words", "board_word", "fsqrt_cost", "results_title", "tag_text", "tag_texts", "say_text", "say_texts"}
OP_KEYS = {"name", "code", "operands", "doc", "since"}
LEVELS_KEYS = {"newest", "rule_set", "limit"}
RULE_SET_KEYS = {"number", "version", "frozen"}
VERSION = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z")
EVENT_KEYS = {"name", "id", "kind", "binds", "doc", "since"}
SETUP_KEYS = {"name", "id", "args", "where", "doc", "table", "lobby", "form", "since"}
ARG_KEYS = {"type", "name", "least", "most", "most_was", "most_since", "default", "on", "always", "always_since"}
READ_KEYS = {"name", "id", "type", "doc", "since"}
ACTION_KEYS = {"name", "spelled", "id", "args", "where", "doc", "join", "needs_step", "since"}
CHOICE_KEYS = {"name", "values", "doc", "values_since"}
OPERANDS = {"u8": 1, "u16": 2, "u32": 4, "i32": 4}
TYPES = {"int", "real", "bool", "seat", "seats", "vec"}
PASCAL = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
WORD = re.compile(r"[a-z][a-z_]*(?:[ .][a-z][a-z_]*)*\Z")


class SchemaError(Exception):
    pass


def keys(raw, allowed, what):
    if not isinstance(raw, dict) or set(raw) - allowed:
        raise SchemaError(f"{what}: unknown keys {sorted(set(raw) - allowed) if isinstance(raw, dict) else raw!r}")


def whole(value, what, low, high):
    if type(value) is not int or not low <= value <= high:
        raise SchemaError(f"{what}: a whole number {low} to {high} (got {value!r})")
    return value


def pascal(value, what):
    """A word's name as C# and C++ spell it: `seat.speed` -> SeatSpeed, `cpus on` -> CpusAllowed."""
    return "".join(part[:1].upper() + part[1:] for part in re.split(r"[ ._]", value))


def numbered(raw, allowed, what, high=255, gaps=False):
    """[(entry)] in id order, ids 0, 1, 2 ...; each a dict with `name`, `id`, `pascal`. With gaps, ids only rise: a word
    removed keeps its id unused, so an older package holding it is refused and never read as another word."""
    out = []
    for i, entry in enumerate(raw):
        keys(entry, allowed, f"{what} {entry.get('name', i) if isinstance(entry, dict) else i}")
        name = entry.get("name")
        if not isinstance(name, str) or not WORD.match(name):
            raise SchemaError(f"{what} {i}: a name of lower-case words (got {name!r})")
        ident = whole(entry.get("id"), f"{what} {name}: id", 0, high)
        if ident <= (out[-1]["id"] if out else -1) if gaps else ident != i:
            raise SchemaError(f"{what} {name}: the ids run 0, 1, 2 ... in the file's order (got {entry['id']})")
        # (`spelled`: the code's name when two words would spell the same: `no_contact` and `no contact`)
        out.append(dict(entry, pascal=pascal(entry.get("spelled", name), what)))
    if len({e["pascal"] for e in out}) != len(out):
        raise SchemaError(f"{what}: two names spell the same")
    return out


def version(text, what):
    """A game version "0.6.1" as (0, 6, 1)."""
    if not isinstance(text, str) or not VERSION.match(text):
        raise SchemaError(f"{what}: a game version, three numbers (got {text!r})")
    return tuple(int(n) for n in text.split("."))


def levels(raw, api):
    """[levels] checked: {newest, rule_sets: [{number, version, frozen or None}], limits: {limit: rule set}}: the rule
    sets 1, 2, ... in order, their versions rising, every one frozen but the last (which may be), the last one [api]
    number, and newest at or after the last one's version; `limit`: the rule set a limit of [api] arrived in, when
    after the first."""
    keys(raw, LEVELS_KEYS, "levels")
    sets = raw.get("rule_set", [])
    if not isinstance(sets, list) or not sets:
        raise SchemaError("levels: needs its rule sets")
    for i, entry in enumerate(sets):
        keys(entry, RULE_SET_KEYS, f"levels: rule set {i + 1}")
        if entry.get("number") != i + 1:
            raise SchemaError(f"levels: the rule sets run 1, 2, 3 ... in the file's order (got {entry.get('number')!r})")
        version(entry.get("version"), f"levels: rule set {i + 1}'s version")
        if i and version(entry["version"], "") <= version(sets[i - 1]["version"], ""):
            raise SchemaError(f"levels: rule set {i + 1}'s version is after rule set {i}'s")
        if "frozen" not in entry and i != len(sets) - 1:
            raise SchemaError(f"levels: rule set {i + 1} is shipped, so frozen: only the last one is open")
    if sets[-1]["number"] != api["number"]:
        raise SchemaError("levels: the last rule set is [api] number")
    if version(raw.get("newest"), "levels.newest") < version(sets[-1]["version"], ""):
        raise SchemaError("levels: newest is the last rule set's version or a later one")
    limits = raw.get("limit", {})
    for name, arrived in limits.items() if isinstance(limits, dict) else [("", 0)]:
        if name not in api or name == "number" or type(arrived) is not int or not 2 <= arrived <= api["number"]:
            raise SchemaError(f"levels.limit: a limit of [api] and the rule set it arrived in, after the first (got {name!r})")
    return {"newest": raw["newest"], "limits": dict(limits),
            "rule_sets": [{"number": e["number"], "version": e["version"], "frozen": e.get("frozen")} for e in sets]}


def since(entry, what, newest):
    """The rule set a word arrived in (`key`, none: 1), a known one."""
    return whole(entry.get("since", 1), f"{what}: since", 1, newest)


def fingerprint(t, rule_set):
    """What rule set `rule_set` is, as a SHA-256: the limits and every word and value of it and of those before, by
    what the package and the two sides read (not the docs or the generated code's spellings)."""
    def kept(entry):
        return {k: v for k, v in entry.items() if k not in ("doc", "where", "pascal", "since", "spelled")}

    def setup(s):
        out = kept(s)
        out["args"] = [{k: a["most_was"] if k == "most" and a.get("most_since", 1) > rule_set else v for k, v in a.items()
                        if k not in ("always_since", "most_was", "most_since") and (k != "always" or a.get("always_since", 1) <= rule_set)}
                       for a in s.get("args", [])]
        return out

    def of(kind, shape=kept):
        return [shape(e) for e in t[kind] if e.get("since", 1) <= rule_set]

    what = {"limits": {k: v for k, v in t["api"].items() if k != "number" and t["levels"]["limits"].get(k, 1) <= rule_set}, "ops": of("ops"), "events": of("events"),
            "setups": of("setups", setup), "seat_reads": of("seat_reads"), "race_reads": of("race_reads"),
            "actions": of("actions"),
            "choices": [{k: v for k, v in kept(c).items() if k != "values_since"} | {"values": [v for v in c["values"] if c["values_since"].get(v, 1) <= rule_set]}
                        for c in t["choices"]]}
    return hashlib.sha256(json.dumps(what, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def load(path=SCHEMA, frozen=True):
    """{namespace_cs, namespace_cxx, api: {...}, ops: [...], events, setups, seat_reads, race_reads, actions},
    checked."""
    try:
        with open(path, "rb") as f:
            raw = tomllib.load(f)
    except tomllib.TOMLDecodeError as e:
        raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    keys(raw, TOP_KEYS, "modes.toml")
    codec = raw.get("codec", {})
    api = raw.get("api", {})
    keys(api, API_KEYS, "api")
    if set(api) != API_KEYS:
        raise SchemaError(f"api: needs {sorted(API_KEYS - set(api))}")
    for k, v in api.items():
        whole(v, f"api.{k}", 1, 1 << 20)
    if api["seats"] != 30:
        raise SchemaError("api.seats: the game's 30")
    sets = levels(raw.get("levels", {}), api)
    newest = api["number"]
    ops, codes = [], set()
    for entry in raw.get("op", []):
        keys(entry, OP_KEYS, f"op {entry.get('name', '?')}")
        name = entry.get("name")
        if not isinstance(name, str) or not PASCAL.match(name):
            raise SchemaError(f"op {name!r}: a PascalCase name")
        code = whole(entry.get("code"), f"op {name}: code", 0, 255)
        if code in codes:
            raise SchemaError(f"op {name}: code {code:#04x} is taken")
        codes.add(code)
        operands = entry.get("operands", [])
        if any(o not in OPERANDS for o in operands):
            raise SchemaError(f"op {name}: operands are {sorted(OPERANDS)}")
        ops.append({"name": name, "code": code, "operands": operands, "doc": entry.get("doc", ""),
                    "size": 1 + sum(OPERANDS[o] for o in operands), "since": since(entry, f"op {name}", newest)})
    if len({o["name"] for o in ops}) != len(ops):
        raise SchemaError("op: a name twice")
    events = numbered(raw.get("event", []), EVENT_KEYS, "event")
    for e in events:
        e["since"] = since(e, f"event {e['name']}", newest)
        if e.get("kind") not in ("continuous", "discrete"):
            raise SchemaError(f"event {e['name']}: kind continuous or discrete")
        if e.get("binds", "seat") not in ("seat", "seat seat", "seat int"):
            raise SchemaError(f"event {e['name']}: binds a seat, two seats, a seat and an int, or nothing")
        if "binds" in e and e["kind"] == "continuous" and e["binds"] != "seat":
            raise SchemaError(f"event {e['name']}: a continuous event binds a seat or nothing")
    setups = numbered(raw.get("setup", []), SETUP_KEYS, "setup", gaps=True)
    for s in setups:
        s["since"] = since(s, f"setup {s['name']}", newest)
        for a in s.get("args", []):
            keys(a, ARG_KEYS, f"setup {s['name']}: an argument")
            if a.get("type") not in ("int", "real", "switch"):
                raise SchemaError(f"setup {s['name']}: an argument is an int, a real or a switch (on, off or a switch parameter)")
            if "on" in a and (a["type"] != "int" or not a.get("least", 0) <= a["on"] <= a.get("most", 0)):
                raise SchemaError(f"setup {s['name']}: an argument's `on` is an int in its range")
            # (a switch that takes a third value, the word `always`: off 0, on 1 as ever, and this above them)
            if "always" in a and (a["type"] != "switch" or type(a["always"]) is not int or a["always"] != 2):
                raise SchemaError(f"setup {s['name']}: an argument's `always` is a switch's third value, 2")
            # (the rule set that value arrived in, when after its word's)
            if "always_since" in a and ("always" not in a or type(a["always_since"]) is not int
                                        or not s["since"] <= a["always_since"] <= newest):
                raise SchemaError(f"setup {s['name']}: `always_since` is the rule set its `always` arrived in, its word's or a later one")
            # (a top raised after its word: `most_was` the top before rule set `most_since`)
            if ("most_was" in a) != ("most_since" in a) or "most_since" in a and (
                    type(a["most_since"]) is not int or not s["since"] < a["most_since"] <= newest
                    or type(a["most_was"]) is not type(a.get("most")) or not a["most_was"] < a["most"]):
                raise SchemaError(f"setup {s['name']}: `most_was` is the lower top before rule set `most_since`, a later one than its word's")
        if s.get("form", "") not in ("", "range", "text", "list"):
            raise SchemaError(f"setup {s['name']}: form is range, text or list")
        if s.get("form") and not s.get("lobby") and s["form"] != "text":
            raise SchemaError(f"setup {s['name']}: a range or a list is a word of the coordinator's")
        if s.get("table", "course") != "course":
            raise SchemaError(f"setup {s['name']}: a table is of courses")
    reads = {}
    for kind in ("seat_read", "race_read"):
        reads[kind] = numbered(raw.get(kind, []), READ_KEYS, kind, gaps=True)
        for r in reads[kind]:
            r["since"] = since(r, f"{kind} {r['name']}", newest)
            if r.get("type") not in TYPES:
                raise SchemaError(f"{kind} {r['name']}: type one of {sorted(TYPES)}")
    choices = {}
    for entry in raw.get("choice", []):
        keys(entry, CHOICE_KEYS, f"choice {entry.get('name', '?')}")
        name, values = entry.get("name"), entry.get("values")
        if not isinstance(name, str) or not re.fullmatch(r"[a-z]+", name) or name in TYPES or name in choices:
            raise SchemaError(f"choice {name!r}: a new lower-case name, not a type's")
        if (not isinstance(values, list) or not values or len(values) > 255
                or any(not isinstance(v, str) or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", v) for v in values)):
            raise SchemaError(f"choice {name}: values are names")
        # (a value added to a shipped choice: `values_since = { NAME = rule set }`, the open one or a later shipped one)
        added = entry.get("values_since", {})
        if (not isinstance(added, dict) or any(v not in values for v in added)
                or any(type(r) is not int or not 1 < r <= newest for r in added.values())):
            raise SchemaError(f"choice {name}: `values_since` names values of the choice and the rule set 2 .. {newest} each arrived in")
        choices[name] = {"name": name, "pascal": pascal(name, "choice"), "values": values, "doc": entry.get("doc", ""),
                         "values_since": added}
    named = [v for c in choices.values() for v in c["values"]]
    if len(set(named)) != len(named):
        raise SchemaError("choice: a value's name twice")
    actions = numbered(raw.get("action", []), ACTION_KEYS, "action", gaps=True)
    for a in actions:
        a["since"] = since(a, f"action {a['name']}", newest)
        if any(t not in TYPES and t not in choices for t in a.get("args", [])):
            raise SchemaError(f"action {a['name']}: argument types are {sorted(TYPES)} or a choice")
        if "join" in a and (not isinstance(a["join"], str) or not re.fullmatch(r"[a-z]+", a["join"])
                            or len(a.get("args", [])) < 2):
            raise SchemaError(f"action {a['name']}: join is the word between its first two arguments")
    table = {"namespace_cs": codec.get("csharp", {}).get("namespace", "Online.Protocol"),
             "namespace_cxx": codec.get("cxx", {}).get("namespace", "fzgx::modes"),
             "api": api, "levels": sets, "ops": ops, "events": events, "setups": setups,
             "seat_reads": reads["seat_read"], "race_reads": reads["race_read"], "actions": actions,
             "choices": list(choices.values())}
    for rule_set in sets["rule_sets"] if frozen else []:
        if rule_set["frozen"] is not None and rule_set["frozen"] != fingerprint(table, rule_set["number"]):
            raise SchemaError(f"levels: rule set {rule_set['number']} ({rule_set['version']}) is shipped and frozen, and a word, a value "
                              "or a limit of it changed: a change belongs to the open rule set (`since`), or to a new one")
    return table
