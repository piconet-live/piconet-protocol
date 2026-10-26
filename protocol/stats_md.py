"""The stat pack's two tables of the handbook generate.py writes into their pages: handbook/stat-packs.md (the
ranges) and handbook/stat-pack-constants.md (the code constants), each between its page's two marker lines (BEGIN,
END), the rest of the page as it is. The tests of both programs and tests/vectors/stat-cases.py read these tables."""
from stats import dotnet_text, handbook_text, SchemaError

BEGIN = "<!-- The table below is generated from protocol/stats.toml by protocol/generate.py: change it there. -->"
END = "<!-- The end of the generated table. -->"
RANGES_PAGE, CONSTANTS_PAGE = "../handbook/stat-packs.md", "../handbook/stat-pack-constants.md"


def range_row(r):
    low, high = handbook_text(r.low), handbook_text(r.high)
    tried = f"0 to {handbook_text(r.tried)}" if r.tried is not None else "the whole range"
    if r.kind == "bytes":
        cells = [f"0x{r.at:02X}", r.name, "two bytes, then two that are 0", "first: 0 to 255", f"second: 0 to {high}",
                 "as a machine's"]
    elif r.kind == "points":
        z = f"; a machine's Z at least {handbook_text(r.z_least)} from 0" if r.z_least else ""
        cells = [f"0x{r.at:02X}", f"{r.name}0X to {r.name}3Z", f"four points of X, Y, Z{z}", f"at least {low}",
                 f"at most {high}", "as a machine's" + (", any Z" if r.z_least else "")]
    else:
        cells = [f"0x{r.at:02X}", r.name, "a number", f"at least {low}", f"{'below' if r.below else 'at most'} {high}",
                 f"{'less than' if r.below else 'to'} {high} either side of 0"]
    return row(cells + [tried])


def ranges(tables):
    head = ["At", "Name in a pack", "What", "From", "To", "In a part's block", "Tried"]
    return [row(head), rule_line(len(head))] + [range_row(r) for r in tables.rules]


def constant_row(c):
    slots = f"{c.slot}, {c.slot + 1}" if c.wide else str(c.slot)
    place = str(c.seat) if c.seat < 10 else f"{c.seat:#x}"
    where = f"a seat's record +{place}" + (f" (the game's: {c.game_place})" if c.game_place else "")
    cells = [slots, c.name, f"{c.bits} bits", dotnet_text(c.stock_value, not c.wide), dotnet_text(c.low, False),
             dotnet_text(c.high, False), c.read_by, where, c.unleashed]
    return row(cells)


def constants(tables):
    head = ["Slot", "Name in a pack", "Size", "The game's", "From", "To", "Read by (main.rel .text)", "Where it lies",
            "GX Unleashed 5.0.4"]
    return [row(head), rule_line(len(head))] + [constant_row(c) for c in tables.constants]


def row(cells):
    return "| " + " | ".join(cells) + " |"


def rule_line(count):
    return "|" + "---|" * count


def splice(page, path, table):
    """The page with its generated table in place of what stands between the markers."""
    if page.count(BEGIN) != 1 or page.count(END) != 1 or page.index(BEGIN) > page.index(END):
        raise SchemaError(f"{path}: the page has no one pair of the generated table's marker lines")
    before, rest = page.split(BEGIN, 1)
    _, after = rest.split(END, 1)
    return before + BEGIN + "\n\n" + "\n".join(table) + "\n\n" + END + after
