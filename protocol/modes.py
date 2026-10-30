"""The game mode system's vocabulary and bytecode (modes.toml), loaded and checked. generate.py hands the result to
modes_cs.py (Online.Protocol's ModeWords, which the compiler reads) and modes_cxx.py (the game's fzgx::modes, which
the interpreter reads). A rule the file breaks is a SchemaError that names it. Standard library only."""
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "modes.toml")

TOP_KEYS = {"codec", "api", "op", "event", "setup", "seat_read", "race_read", "action", "choice"}
API_KEYS = {"number", "budget", "queue", "globals", "seat_words", "locals", "rules", "sorts", "package_bytes", "seats",
            "team_words", "teams", "waits", "wait_locals", "subs", "board", "board_label", "board_words", "board_word", "fsqrt_cost", "results_title"}
OP_KEYS = {"name", "code", "operands", "doc"}
EVENT_KEYS = {"name", "id", "kind", "binds", "doc"}
SETUP_KEYS = {"name", "id", "args", "where", "doc", "table", "lobby", "form"}
ARG_KEYS = {"type", "name", "least", "most", "default", "on"}
READ_KEYS = {"name", "id", "type", "doc"}
ACTION_KEYS = {"name", "id", "args", "where", "doc", "join"}
CHOICE_KEYS = {"name", "values", "doc"}
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
        out.append(dict(entry, pascal=pascal(name, what)))
    if len({e["pascal"] for e in out}) != len(out):
        raise SchemaError(f"{what}: two names spell the same")
    return out


def load(path=SCHEMA):
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
                    "size": 1 + sum(OPERANDS[o] for o in operands)})
    if len({o["name"] for o in ops}) != len(ops):
        raise SchemaError("op: a name twice")
    events = numbered(raw.get("event", []), EVENT_KEYS, "event")
    for e in events:
        if e.get("kind") not in ("continuous", "discrete"):
            raise SchemaError(f"event {e['name']}: kind continuous or discrete")
        if e.get("binds", "seat") not in ("seat", "seat seat", "seat int"):
            raise SchemaError(f"event {e['name']}: binds a seat, two seats, a seat and an int, or nothing")
        if "binds" in e and e["kind"] == "continuous" and e["binds"] != "seat":
            raise SchemaError(f"event {e['name']}: a continuous event binds a seat or nothing")
    setups = numbered(raw.get("setup", []), SETUP_KEYS, "setup", gaps=True)
    for s in setups:
        for a in s.get("args", []):
            keys(a, ARG_KEYS, f"setup {s['name']}: an argument")
            if a.get("type") not in ("int", "real", "switch"):
                raise SchemaError(f"setup {s['name']}: an argument is an int, a real or a switch (on, off or a switch parameter)")
            if "on" in a and (a["type"] != "int" or not a.get("least", 0) <= a["on"] <= a.get("most", 0)):
                raise SchemaError(f"setup {s['name']}: an argument's `on` is an int in its range")
        if s.get("form", "") not in ("", "range", "text", "list"):
            raise SchemaError(f"setup {s['name']}: form is range, text or list")
        if s.get("form") and not s.get("lobby") and s["form"] != "text":
            raise SchemaError(f"setup {s['name']}: a range or a list is a word of the coordinator's")
        if s.get("table", "course") != "course":
            raise SchemaError(f"setup {s['name']}: a table is of courses")
    reads = {}
    for kind in ("seat_read", "race_read"):
        reads[kind] = numbered(raw.get(kind, []), READ_KEYS, kind)
        for r in reads[kind]:
            if r.get("type") not in TYPES:
                raise SchemaError(f"{kind} {r['name']}: type one of {sorted(TYPES)}")
    choices = {}
    for entry in raw.get("choice", []):
        keys(entry, CHOICE_KEYS, f"choice {entry.get('name', '?')}")
        name, values = entry.get("name"), entry.get("values")
        if not isinstance(name, str) or not re.fullmatch(r"[a-z]+", name) or name in TYPES or name in choices:
            raise SchemaError(f"choice {name!r}: a new lower-case name, not a type's")
        if (not isinstance(values, list) or not values or len(values) > 255
                or any(not isinstance(v, str) or not re.fullmatch(r"[A-Za-z][A-Za-z_]*", v) for v in values)):
            raise SchemaError(f"choice {name}: values are names")
        choices[name] = {"name": name, "pascal": pascal(name, "choice"), "values": values, "doc": entry.get("doc", "")}
    named = [v for c in choices.values() for v in c["values"]]
    if len(set(named)) != len(named):
        raise SchemaError("choice: a value's name twice")
    actions = numbered(raw.get("action", []), ACTION_KEYS, "action", gaps=True)
    for a in actions:
        if any(t not in TYPES and t not in choices for t in a.get("args", [])):
            raise SchemaError(f"action {a['name']}: argument types are {sorted(TYPES)} or a choice")
        if "join" in a and (not isinstance(a["join"], str) or not re.fullmatch(r"[a-z]+", a["join"])
                            or len(a.get("args", [])) < 2):
            raise SchemaError(f"action {a['name']}: join is the word between its first two arguments")
    return {"namespace_cs": codec.get("csharp", {}).get("namespace", "Online.Protocol"),
            "namespace_cxx": codec.get("cxx", {}).get("namespace", "fzgx::modes"),
            "api": api, "ops": ops, "events": events, "setups": setups,
            "seat_reads": reads["seat_read"], "race_reads": reads["race_read"], "actions": actions,
            "choices": list(choices.values())}
