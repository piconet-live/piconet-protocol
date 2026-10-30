namespace Online.Protocol;

/// <summary>Checks the types of a file's rules and writes their bytecode (formats.md section 9; the interpreter in the
/// runner's port/core/src/mode.cpp runs it as written here), and counts each rule's operations a step at worst: the
/// static budget (the language's 11.1).
/// <para>The conventions the interpreter is written to: a stack of 32-bit cells; a condition ends with Ret, a body with
/// End; jumps are offsets in the package's code. Local 0 is the seat an <c>each seat</c> or <c>out</c> rule is on;
/// declared locals and the compiler's own (a loop's counter, a helper's sum) follow, each block's freed when it
/// ends (a vec takes three slots, x, y, z). A local is given its value when declared (0, false or none when none is
/// written): nothing rests on the interpreter clearing them, nor on the slots past a wait's live ones.</para>
/// <para>A sub is written where it is called: its arguments into locals of its own (the caller's are out of its
/// sight), its body, its returns jumps to its end with the value in a local. A <c>wait</c> keeps the slots in use
/// (at most the api's wait_locals) and leaves the stack empty: the compiler allows one only where a statement may
/// stand, outside a sorted loop.</para>
/// <para>The count: one an operation, a SortRun 1 + 5 × 30 × its keys; a loop's test once more than its
/// iterations (30 for the seats, a repeat's most), its body once each; an <c>each seat</c> or <c>out</c> rule 30
/// times; a rule that waits twice (ModeCompiler). Both arms of an <c>if</c> are counted, so the count is an upper
/// bound.</para></summary>
internal sealed partial class ModeEmitter(ModeNames names, List<string> warnings)
{
    private sealed record Local(string Name, ModeType Type, int Slot, bool Fixed);

    private readonly ModeBytes _code = new();
    private readonly List<Local> _locals = [];
    // The seats a bare read names (racing for seat.racing): the loops' seats, the innermost last.
    private readonly List<Local> _implicit = [];
    private int _high;
    private long _multiplier = 1;
    private int _seatLoops;
    private int _sorts;
    // the first local a name may find: a sub's own, past its caller's
    private int _scope;
    // inside an expression's sub or a condition: no wait
    private int _noWait;
    private bool _waits;
    // the subs being written, the innermost last: their returns' jumps and value
    private readonly List<(string Name, ModeType? Type, Local? Value, List<int> Jumps)> _subs = [];

    /// <summary>The actions the rules written so far use, each with the line of its first use.</summary>
    public Dictionary<ModeAction, int> Used { get; } = [];

    /// <summary>The operations a step at worst of the rules written so far.</summary>
    public long Cost { get; private set; }

    /// <summary>Counts operations more (a rule that waits, ModeCompiler).</summary>
    public void Count(long operations) => Cost += operations;

    public byte[] Code => _code.ToArray();

    private uint Here => (uint)_code.Count;

    /// <summary>Writes a rule: its conditions and its body, after the code written before it; and whether it waits.</summary>
    public (ModeRule Rule, bool Waits) Rule(ModeRuleItem item, ModeEvent id, bool bindsSeat, ModeType? second = null)
    {
        _locals.Clear();
        _implicit.Clear();
        _high = 0;
        _scope = 0;
        _waits = false;
        _multiplier = bindsSeat ? ModeWords.Seats : 1;
        if (bindsSeat)
        {
            Declare(item.Line, item.Bound ?? "seat", ModeType.Seat, true);
            if (second is { } type)
            {
                // local 1: the event's second value (ko's by, hit's b, out's cause) when the rule names it; the
                // interpreter puts the value there either way, and a local declared later is given its own value
                if (item.Second is { } name)
                {
                    Declare(item.Line, name, type, true);
                }
                // a pair event (hit, overtake) happens at most once a pair a step
                if (id is ModeEvent.Hit or ModeEvent.Overtake)
                {
                    _multiplier = ModeWords.Seats * (ModeWords.Seats - 1) / 2;
                }
            }
        }
        var when = Condition(item.Whens);
        var during = Condition(item.Whiles);
        var body = Here;
        Statements(item.Body);
        Op(ModeOp.End);
        // at least one: formats.md's rule has local 0, and the interpreter refuses a rule without it (a rule on no seat
        // that declares nothing, as `event go; do { n += 1; }`, needs none itself)
        return (new ModeRule(item.Label, id, Math.Max(_high, 1), when, during, body), _waits);
    }

    private uint Condition(IReadOnlyList<ModeExpr> conditions)
    {
        if (conditions.Count == 0)
        {
            return ModeRule.None;
        }
        var start = Here;
        var joined = conditions.Skip(1).Aggregate(conditions[0], (a, b) => new ModeBinary(b.Line, "and", a, b));
        _noWait++;
        Value(joined, ModeType.Bool, "a condition");
        _noWait--;
        Op(ModeOp.Ret);
        return start;
    }

    // The operations, counted as they are written.

    private void Op(ModeOp op, params long[] operands)
    {
        _code.U8((int)op);
        var kinds = ModeCode.Operands.GetValueOrDefault(op, []);
        for (var i = 0; i < kinds.Length; i++)
        {
            switch (kinds[i].Bytes)
            {
                case 1:
                    _code.U8((int)operands[i]);
                    break;
                case 2:
                    _code.U16((int)operands[i]);
                    break;
                default:
                    _code.U32(unchecked((uint)operands[i]));
                    break;
            }
        }
        Cost += _multiplier * (op == ModeOp.SortRun ? 1 + (5L * ModeWords.Seats * operands[1]) : 1);
    }

    private void Push(int cell) => Op(ModeOp.Push, cell);

    /// <summary>A jump whose target is written later (<see cref="Land"/>): where its operand is.</summary>
    private int JumpAhead(ModeOp op)
    {
        Op(op, 0);
        return _code.Count - 4;
    }

    private void Land(int jump) => _code.Patch(jump, Here);

    // Locals.

    private Local Declare(int line, string name, ModeType type, bool isFixed = false)
    {
        if (_locals.Skip(_scope).Any(l => l.Name == name) || names.Find(name) is not null)
        {
            throw new ModeCompileException(line, $"'{name}' is already declared");
        }
        return Temporary(line, type, name, isFixed);
    }

    /// <summary>The slots a value of the type takes: a vec three, anything else one.</summary>
    private static int Width(ModeType type) => type == ModeType.Vec ? 3 : 1;

    /// <summary>The slots in use: past the last local's.</summary>
    private int Slots => _locals.Count == 0 ? 0 : _locals[^1].Slot + Width(_locals[^1].Type);

    /// <summary>A local slot; a compiler's own has a name no file can write.</summary>
    private Local Temporary(int line, ModeType type, string name = "", bool isFixed = true)
    {
        var slot = Slots;
        if (slot + Width(type) > ModeWords.Locals)
        {
            throw new ModeCompileException(line, $"a rule has at most {ModeWords.Locals} locals, the loops' and helpers' included");
        }
        var local = new Local(name, type, slot, isFixed);
        _locals.Add(local);
        _high = Math.Max(_high, slot + Width(type));
        return local;
    }

    /// <summary>Pops a value (a vec's three cells, z first) into a local.</summary>
    private void StoreTo(Local local)
    {
        for (var k = Width(local.Type) - 1; k >= 0; k--)
        {
            Op(ModeOp.StoreLocal, local.Slot + k);
        }
    }

    private void LoadFrom(Local local)
    {
        for (var k = 0; k < Width(local.Type); k++)
        {
            Op(ModeOp.LoadLocal, local.Slot + k);
        }
    }

    /// <summary>The value on the stack into a new local of the compiler's.</summary>
    private Local Spill(int line, ModeType type)
    {
        var local = Temporary(line, type);
        StoreTo(local);
        return local;
    }

    /// <summary>A loop's seat: <c>seat</c> may name the loop's seat inside a rule or loop that has one already (the
    /// innermost is meant); any other name is new.</summary>
    private Local Bind(int line, string bound) =>
        bound == "seat" ? Temporary(line, ModeType.Seat, "seat") : Declare(line, bound, ModeType.Seat, true);

    /// <summary>Frees the locals declared after a mark (a block's end).</summary>
    private void Free(int mark) => _locals.RemoveRange(mark, _locals.Count - mark);

    private Local? FindLocal(string name) => _locals.Skip(_scope).LastOrDefault(l => l.Name == name && l.Name.Length > 0);

    // Loops.

    /// <summary>A bounded loop: its test (which jumps out) once more than its iterations, then its body each time,
    /// then back to the test. The test returns the jumps to land after the loop.</summary>
    private void Loop(long iterations, Func<int[]> test, Action body)
    {
        var outer = _multiplier;
        var top = Here;
        _multiplier = outer * (iterations + 1);
        var exits = test();
        _multiplier = outer * iterations;
        body();
        Op(ModeOp.Jump, top);
        _multiplier = outer;
        foreach (var exit in exits)
        {
            Land(exit);
        }
    }

    /// <summary>The race's seats in seat order: one local is the counter and the seat, from 0 while below
    /// <c>race.seats</c>; a seat not in <paramref name="set"/> (null: every seat; worked out once, before the loop) or
    /// that fails <paramref name="where"/> is skipped. <paramref name="body"/> sees the seat as
    /// <paramref name="bound"/>, and bare reads (<c>racing</c>) in the condition and in the body given
    /// <paramref name="implicitReads"/> are of it.</summary>
    private void Seats(int line, string bound, ModeExpr? where, Action<Local> body, bool implicitReads, ModeExpr? set = null)
    {
        if (_seatLoops >= 2)
        {
            throw new ModeCompileException(line, "loops over the seats nest at most two deep (30 × 30)");
        }
        var mark = _locals.Count;
        Local? members = null;
        if (set is not null)
        {
            Value(set, ModeType.Seats, "for each ... in");
            members = Spill(line, ModeType.Seats);
        }
        var seat = Bind(line, bound);
        _seatLoops++;
        Push(0);
        Op(ModeOp.StoreLocal, seat.Slot);
        Loop(ModeWords.Seats, () =>
        {
            Op(ModeOp.LoadLocal, seat.Slot);
            Op(ModeOp.RaceRead, (long)ModeRaceRead.Seats);
            Op(ModeOp.ILt);
            return [JumpAhead(ModeOp.JumpIfZero)];
        }, () =>
        {
            _implicit.Add(seat);
            var skips = new List<int>();
            if (members is not null)
            {
                Op(ModeOp.LoadLocal, seat.Slot);
                Op(ModeOp.SeatBit);
                Op(ModeOp.LoadLocal, members.Slot);
                Op(ModeOp.BitAnd);
                skips.Add(JumpAhead(ModeOp.JumpIfZero));
            }
            if (where is not null)
            {
                Value(where, ModeType.Bool, "where");
                skips.Add(JumpAhead(ModeOp.JumpIfZero));
            }
            if (!implicitReads)
            {
                _implicit.RemoveAt(_implicit.Count - 1);
            }
            body(seat);
            if (implicitReads)
            {
                _implicit.RemoveAt(_implicit.Count - 1);
            }
            foreach (var skip in skips)
            {
                Land(skip);
            }
            Op(ModeOp.LoadLocal, seat.Slot);
            Push(1);
            Op(ModeOp.IAdd);
            Op(ModeOp.StoreLocal, seat.Slot);
        });
        _seatLoops--;
        Free(mark);
    }
}
