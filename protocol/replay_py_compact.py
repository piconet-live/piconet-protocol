"""The compact replay file's Python code (version 8, replay.toml's [compact]) that replay_py.py writes into
generated/replay_file.py: the fold, which steps keep a whole checksum, the checksum that compares by its folded byte,
xz both ways, compact (a recording to the compact file) and expand (back). Its names (FULL_EVERY, XZ_PRESET, ...)
are the generated file's constants. Standard library only."""

CODE = r'''
def fold(checksum):
    """A step's checksum as the compact file keeps it between its whole ones: its four bytes XORed."""
    return (checksum ^ checksum >> 8 ^ checksum >> 16 ^ checksum >> 24) & 0xFF


def is_full(step, steps):
    """Whether a compact file of `steps` steps keeps step's whole checksum: every FULL_EVERY-th and the last."""
    return step % FULL_EVERY == 0 or step == steps - 1


class Checksum(bytes):
    """A step's checksum, its four bytes; full says the file holds it whole (a compact file's other steps hold the
    folded byte, as 0, 0, 0, the byte). Two are equal when both are whole and the same, or else when their folded
    bytes are: so a compact file compares with a whole one, or with another compact one, at every step."""

    def __new__(cls, four, full=True):
        c = super().__new__(cls, bytes(four))
        c.full = full
        return c

    def value(self):
        return int.from_bytes(self, "big")

    def __eq__(self, other):
        if not isinstance(other, (bytes, bytearray)) or len(other) != len(self):
            return NotImplemented
        if self.full and getattr(other, "full", True):
            return bytes(self) == bytes(other)
        return fold(self.value()) == fold(int.from_bytes(other, "big"))

    def __ne__(self, other):
        equal = self.__eq__(other)
        return equal if equal is NotImplemented else not equal

    def __hash__(self):
        return hash(fold(self.value()))


def xz(data):
    """data as the compact file's xz stream: LZMA2 at XZ_PRESET with the extreme flag, the dictionary data's size."""
    size = min(max(len(data), DICT_MIN), DICT_MAX)
    return lzma.compress(data, format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC32,
                         filters=[{"id": lzma.FILTER_LZMA2, "preset": XZ_PRESET | lzma.PRESET_EXTREME, "dict_size": size}])


def unxz(data, most, exact=False):
    """One xz stream, whole and checked, of at most `most` bytes (exactly `most` with exact); else ValueError."""
    d = lzma.LZMADecompressor(format=lzma.FORMAT_XZ, memlimit=XZ_MEMLIMIT)
    try:
        out = d.decompress(bytes(data), max_length=most + 1)
    except lzma.LZMAError as e:
        raise ValueError(f"an xz part does not read: {e}") from None
    if not d.eof or d.unused_data or len(out) > most or (exact and len(out) != most):
        raise ValueError("an xz part is cut, longer than its part, or followed by other bytes")
    return out


def _section(mark, body):
    return mark + struct.pack(">I", len(body)) + body


def compact(data):
    """A whole recording of a version the game plays (VERSIONS: header, frames, lineup, whole steps, results or
    none) as the compact file; ValueError for any other."""
    r = parse(data)
    if not r.marked or r.version not in VERSIONS or r.header is None or not r.frames_marked or not r.has_lineup \
            or r.lineup_length != LINEUP_SIZE or not r.steps or not r.whole:
        raise ValueError("not a whole recording")
    n, width = len(r.steps), len(r.seats) * RECORD_SIZE
    rows = b"".join(row for row, _ in r.steps)
    planes = b"".join(rows[j::width] for j in range(width))
    sums = [c.value() for _, c in r.steps]
    checks = bytes(fold(c) for c in sums) + b"".join(struct.pack(">I", sums[t]) for t in range(n) if is_full(t, n))
    out = MARK + struct.pack(">HH", COMPACT_VERSION, r.version) + _section(HEADER_MARK, xz(r.header))
    out += _section(FRAMES_MARK, struct.pack(">II", r.mask, r.seat)) + _section(LINEUP_MARK, r.lineup)
    out += _section(INPUTS_MARK, struct.pack(">I", n) + xz(planes)) + _section(CHECKSUMS_MARK, xz(checks))
    if r.results is not None:
        out += r.results + struct.pack(">I", len(r.results)) + RESULTS_MARK
    return out


def expand(data):
    """A compact file as the recording it was made of, but that a step whose whole checksum
    the compact file does not keep holds its folded byte (0, 0, 0, the byte); ValueError for a file that is not one."""
    data = bytes(data)
    if len(data) < 8 or data[:4] != MARK or struct.unpack_from(">H", data, 4)[0] != COMPACT_VERSION:
        raise ValueError("not a compact replay")
    race = struct.unpack_from(">H", data, 6)[0]
    if race not in VERSIONS:
        raise ValueError(f"a recording of version {race}, which the game does not play")
    if len(data) > MAX_BYTES:
        raise ValueError("longer than MAX_BYTES")
    end, results = len(data), b""
    if end - 8 >= RESULTS_TAIL and data[-4:] == RESULTS_MARK:
        length = struct.unpack_from(">I", data, end - 8)[0]
        if length + RESULTS_TAIL <= end - 8:
            end -= RESULTS_TAIL + length
            results = data[end:]
    at, parts = 8, []
    for mark in (HEADER_MARK, FRAMES_MARK, LINEUP_MARK, INPUTS_MARK, CHECKSUMS_MARK):
        if end - at < 8 or data[at:at + 4] != mark:
            raise ValueError(f"no {mark.decode()} part where it belongs")
        length = struct.unpack_from(">I", data, at + 4)[0]
        if length > end - at - 8:
            raise ValueError(f"the {mark.decode()} part ends after the file")
        parts.append(data[at + 8:at + 8 + length])
        at += 8 + length
    if at != end:
        raise ValueError("bytes between the parts and the results")
    header_xz, frames, lineup, inputs, checks_xz = parts
    header = unxz(header_xz, MAX_JSON)
    if len(frames) != FRAMES_SIZE - 4 or len(inputs) < 4:
        raise ValueError("a frames or inputs part of another size")
    mask, seat = struct.unpack(">II", frames)
    n = struct.unpack_from(">I", inputs)[0]
    width = bin(mask).count("1") * RECORD_SIZE
    if width == 0 or not 0 < n <= MAX_STEPS:
        raise ValueError("no player seat, or no step, or more than MAX_STEPS")
    planes = unxz(inputs[4:], n * width, exact=True)
    full = [t for t in range(n) if is_full(t, n)]
    checks = unxz(checks_xz, n + 4 * len(full), exact=True)
    rows = bytearray(n * width)
    for j in range(width):
        rows[j::width] = planes[j * n:(j + 1) * n]
    sums = [bytes((0, 0, 0, b)) for b in checks[:n]]
    for k, t in enumerate(full):
        whole = checks[n + 4 * k:n + 4 * k + 4]
        if fold(int.from_bytes(whole, "big")) != checks[t]:
            raise ValueError(f"step {t}'s whole checksum and its byte disagree")
        sums[t] = whole
    out = bytearray(MARK + struct.pack(">HI", race, len(header)) + header)
    out += FRAMES_MARK + frames + LINEUP_MARK + struct.pack(">I", len(lineup)) + lineup
    for t in range(n):
        out += rows[t * width:(t + 1) * width] + sums[t]
    return bytes(out + results)
'''
