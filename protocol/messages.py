"""The lobby link's messages (messages.toml), loaded and checked. generate.py hands the result to messages_cxx.py (the
C++ codec, generated/lobby_messages.g.h) and messages_cs.py (generated/LobbySchema.g.cs, the lines the C# records are
held to by reflection in tests/dotnet-messages). A rule the file breaks is a SchemaError that names it. Standard
library only."""
import json
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "messages.toml")

ROOTS = ("client", "server", "lan", "status")
MESSAGE_ROOTS = ("client", "server")
PLAIN = ("string", "int", "uint", "long", "byte", "bool", "float", "double", "time")
# the plain types that are values in C# (never null unless "?"); the others (string) are references
VALUES = {"int", "uint", "long", "byte", "bool", "float", "double", "time"}
NAME = re.compile(r"[A-Z][A-Za-z0-9]*\Z")
MEMBER = re.compile(r"[a-z][A-Za-z0-9]*\Z")
TYPE_NAME = re.compile(r"[a-z]+[A-Za-z0-9]*\Z")
RECORD_KEYS = {"name", "root", "type", "members"}
MEMBER_KEYS = {"name", "type", "default", "omit", "written"}
ENUM_KEYS = {"name", "as", "underlying", "values"}
LINK_KEYS = {"version": int, "path": str, "keep_alive_seconds": int, "global_channel": str, "lobby_channel": str}
UNDERLYING = ("int", "byte")


class SchemaError(Exception):
    pass


class Type:
    """A member's type: kind (plain, list, map, record, enum), the plain name or the record's or enum's, the element
    (list, map) and whether it may be null."""

    def __init__(self, kind, name, element=None, nullable=False):
        self.kind, self.name, self.element, self.nullable = kind, name, element, nullable

    def text(self):
        inner = {"list": f"list<{self.element.text() if self.element else ''}>",
                 "map": f"map<{self.element.text() if self.element else ''}>"}.get(self.kind, self.name)
        return inner + ("?" if self.nullable else "")

    def is_value(self):
        """A C# value type: null only when "?"."""
        return self.kind == "enum" or (self.kind == "plain" and self.name in VALUES)


def parse_type(text, records, enums, what):
    nullable = text.endswith("?")
    body = text[:-1] if nullable else text
    for kind in ("list", "map"):
        if body.startswith(kind + "<") and body.endswith(">"):
            element = parse_type(body[len(kind) + 1:-1], records, enums, what)
            if element.nullable and element.is_value():
                raise SchemaError(f"{what}: a list or map of values that may be null ({text}) is not supported")
            if kind == "map" and element.text() != "int":
                raise SchemaError(f"{what}: only map<int> is supported (got {text})")
            return Type(kind, kind, element, nullable)
    if body in PLAIN:
        return Type("plain", body, None, nullable)
    if body in records:
        return Type("record", body, None, nullable)
    if body in enums:
        return Type("enum", body, None, nullable)
    raise SchemaError(f"{what}: no type {text!r} (a plain type, list<T>, map<T>, a record's or an enum's name)")


def load(path=SCHEMA):
    """{"enums": [{name, named, values}], "records": [{name, root, type, members: [{name, type: Type, default, omit,
    written}]}]} in the file's order, checked."""
    with open(path, "rb") as f:
        raw = tomllib.load(f)
    if set(raw) - {"link", "enum", "record"}:
        raise SchemaError(f"messages.toml: unknown tables {sorted(set(raw) - {'link', 'enum', 'record'})}")
    link = raw.get("link", {})
    if set(link) != set(LINK_KEYS) or any(type(link[k]) is not t for k, t in LINK_KEYS.items()):
        raise SchemaError(f"[link]: {', '.join(f'{k} ({t.__name__})' for k, t in LINK_KEYS.items())}")
    enums, records = [], []
    names = set()
    for e in raw.get("enum", []):
        if set(e) - ENUM_KEYS or not NAME.match(e.get("name", "")) or e.get("as") not in ("name", "number"):
            raise SchemaError(f"enum {e.get('name')!r}: a PascalCase name, as \"name\" or \"number\", values")
        values = e.get("values", [])
        if not values or any(not isinstance(v, str) or not NAME.match(v) for v in values) or len(set(values)) != len(values):
            raise SchemaError(f"enum {e['name']}: values are distinct PascalCase names")
        if e["name"] in names:
            raise SchemaError(f"enum {e['name']}: a second type of that name")
        names.add(e["name"])
        underlying = e.get("underlying", "int")
        if underlying not in UNDERLYING:
            raise SchemaError(f"enum {e['name']}: underlying is one of {UNDERLYING} (int when left out)")
        enums.append({"name": e["name"], "named": e["as"] == "name", "underlying": underlying, "values": values})
    record_names = [r.get("name", "") for r in raw.get("record", [])]
    enum_names = {e["name"] for e in enums}
    discriminators = {root: set() for root in MESSAGE_ROOTS}
    for r in raw.get("record", []):
        what = f"record {r.get('name')!r}"
        if set(r) - RECORD_KEYS or not NAME.match(r.get("name", "")):
            raise SchemaError(f"{what}: a PascalCase name and only {sorted(RECORD_KEYS)}")
        if r["name"] in names:
            raise SchemaError(f"{what}: a second type of that name")
        names.add(r["name"])
        root = r.get("root")
        if root is not None and root not in ROOTS:
            raise SchemaError(f"{what}: root is one of {ROOTS}")
        disc = r.get("type")
        if (root in MESSAGE_ROOTS) != (disc is not None):
            raise SchemaError(f"{what}: a message (root client or server) has a type, and only a message")
        if disc is not None:
            if not TYPE_NAME.match(disc) or disc in discriminators[root]:
                raise SchemaError(f"{what}: type {disc!r} is a camelCase name no other {root} message has")
            discriminators[root].add(disc)
        members, seen = [], set()
        for m in r.get("members", []):
            mwhat = f"{what} member {m.get('name')!r}"
            if set(m) - MEMBER_KEYS or not MEMBER.match(m.get("name", "")) or not isinstance(m.get("type"), str):
                raise SchemaError(f"{mwhat}: a camelCase name, a type, and only {sorted(MEMBER_KEYS)}")
            if m["name"].lower() in seen or (disc is not None and m["name"].lower() == "type"):
                raise SchemaError(f"{mwhat}: a second member of that name (names are read without regard to case)")
            seen.add(m["name"].lower())
            t = parse_type(m["type"], record_names, enum_names, mwhat)
            default = m.get("default")
            if default is not None:
                default = check_default(default, t, enums, mwhat)
            omit = m.get("omit")
            if omit not in (None, "default"):
                raise SchemaError(f"{mwhat}: omit is \"default\" or nothing (a null is always left out)")
            if omit == "default" and not (t.is_value() and not t.nullable):
                raise SchemaError(f"{mwhat}: omit \"default\" only on a value that is never null")
            written = m.get("written")
            if written is not None and (not isinstance(written, str) or default is not None or omit is not None):
                raise SchemaError(f"{mwhat}: written is a C++ expression, with no default and no omit")
            members.append({"name": m["name"], "type": t, "default": default, "omit": omit, "written": written})
        if len(members) > 32:
            raise SchemaError(f"{what}: at most 32 members (the C++ struct's masks)")
        records.append({"name": r["name"], "root": root, "type": disc, "members": members})
    for root in MESSAGE_ROOTS:
        if not discriminators[root]:
            raise SchemaError(f"no {root} message")
    for root in ("lan", "status"):
        if len([r for r in records if r["root"] == root]) != 1:
            raise SchemaError(f"one record has root {root!r}")
    return {"link": link, "enums": enums, "records": records}


def check_default(text, t, enums, what):
    """A default's JSON text, checked against the member's type; the parsed value."""
    if not isinstance(text, str):
        raise SchemaError(f"{what}: default is JSON text in a string ('null', '0', '\"forming\"')")
    try:
        value = json.loads(text)
    except json.JSONDecodeError as e:
        raise SchemaError(f"{what}: default {text!r} is no JSON ({e})") from None
    ok = (value is None and (t.nullable or not t.is_value())) or \
        (t.kind == "plain" and t.name == "bool" and isinstance(value, bool)) or \
        (t.kind == "plain" and t.name in ("int", "uint", "long", "byte") and type(value) is int) or \
        (t.kind == "plain" and t.name in ("float", "double") and type(value) in (int, float)) or \
        (t.kind == "plain" and t.name == "string" and isinstance(value, str)) or \
        (t.kind == "enum" and isinstance(value, str) and value in next(e["values"] for e in enums if e["name"] == t.name))
    if not ok:
        raise SchemaError(f"{what}: default {text!r} is not a {t.text()}")
    return text


def ordered(table):
    """The records in an order where each comes after every record its members hold (a list's elements may come
    later: a vector of a type declared after it is not needed, but an optional or a member is)."""
    by_name = {r["name"]: r for r in table["records"]}
    done, out, busy = set(), [], set()

    def visit(name):
        if name in done:
            return
        if name in busy:
            raise SchemaError(f"record {name}: holds itself")
        busy.add(name)
        for m in by_name[name]["members"]:
            t = m["type"]
            while t.kind in ("list", "map"):
                t = t.element
            if t.kind == "record":
                visit(t.name)
        busy.discard(name)
        done.add(name)
        out.append(by_name[name])

    for r in table["records"]:
        visit(r["name"])
    return out


def lines(table):
    """The schema as the lines the C# records are compared by (tests/dotnet-messages/Schema.cs writes the same from
    reflection): the enums, then the records, each by name (ordinal)."""
    link = table["link"]
    out = [f"link version={link['version']} path={link['path']} keepAlive={link['keep_alive_seconds']} "
           f"global={link['global_channel']} lobby={link['lobby_channel']}"]
    for e in sorted(table["enums"], key=lambda e: e["name"]):
        out.append(f"enum {e['name']} {'named' if e['named'] else 'number'} {e['underlying']}")
        out += [f"  {v} = {i}" for i, v in enumerate(e["values"])]
    for r in sorted(table["records"], key=lambda r: r["name"]):
        out.append(f"record {r['name']}" + (f" root={r['root']}" if r["root"] else "") + (f" type={r['type']}" if r["type"] else ""))
        for m in r["members"]:
            out.append(f"  {m['name']} {m['type'].text()}" + (f" default={m['default']}" if m["default"] is not None else "")
                       + (f" ignore={m['omit']}" if m["omit"] else "") + (" writeonly" if m["written"] is not None else ""))
    return out
