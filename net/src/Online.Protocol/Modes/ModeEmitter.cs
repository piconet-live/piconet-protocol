namespace Online.Protocol;

/// <summary>Checks the types of a file's rules and writes their bytecode (formats.md section 9; the interpreter in the
/// runner's port/core/src/mode.cpp runs it as written here), and counts each rule's operations a step at worst: the
/// static budget (the language's 11.1).
/// <para>The conventions the interpreter is written to: a stack of 32-bit cells; a condition ends with Ret, a body with
/// End; jumps are offsets in the package's code. Local 0 is the seat an <c>each seat</c> or <c>out</c> rule is on;
/// declared locals and the compiler's own (a loop's counter, a helper's sum) follow, each block's freed when it
/// ends. A local is given its value when declared (0, false or none when none is written): nothing rests on the
/// interpreter clearing them.</para>
/// <para>The count: one an operation, a SortRun 1 + 5 × 30 × its keys; a loop's test once more than its
/// iterations (30 for the seats, a repeat's most), its body once each; an <c>each seat</c> or <c>out</c> rule 30
/// times. Both arms of an <c>if</c> are counted, so the count is an upper bound.</para></summary>
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

    /// <summary>The operations a step at worst of the rules written so far.</summary>
    public long Cost { get; private set; }

    public byte[] Code => _code.ToArray();

    private uint Here => (uint)_code.Count;

    /// <summary>Writes a rule: its conditions and its body, after the code written before it.</summary>
    public ModeRule Rule(ModeRuleItem item, ModeEvent id, bool bindsSeat)
    {
        _locals.Clear();
        _implicit.Clear();
        _high = 0;
        _multiplier = bindsSeat ? ModeWords.Seats : 1;
        if (bindsSeat)
        {
            Declare(item.Line, item.Bound ?? "seat", ModeType.Seat, true);
        }
        var when = Condition(item.Whens);
        var during = Condition(item.Whiles);
        var body = Here;
        Statements(item.Body);
        Op(ModeOp.End);
        return new ModeRule(item.Label, id, _high, when, during, body);
    }

    private uint Condition(IReadOnlyList<ModeExpr> conditions)
    {
        if (conditions.Count == 0)
        {
            return ModeRule.None;
        }
        var start = Here;
        var joined = conditions.Skip(1).Aggregate(conditions[0], (a, b) => new ModeBinary(b.Line, "and", a, b));
        Value(joined, ModeType.Bool, "a condition");
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
        if (_locals.Any(l => l.Name == name) || names.Find(name) is not null)
        {
            throw new ModeCompileException(line, $"'{name}' is already declared");
        }
        return Temporary(line, type, name, isFixed);
    }

    /// <summary>A local slot; a compiler's own has a name no file can write.</summary>
    private Local Temporary(int line, ModeType type, string name = "", bool isFixed = true)
    {
        var slot = _locals.Count == 0 ? 0 : _locals[^1].Slot + 1;
        if (slot >= ModeWords.Locals)
        {
            throw new ModeCompileException(line, $"a rule has at most {ModeWords.Locals} locals, the loops' and helpers' included");
        }
        var local = new Local(name, type, slot, isFixed);
        _locals.Add(local);
        _high = Math.Max(_high, slot + 1);
        return local;
    }

    /// <summary>A loop's seat: <c>seat</c> may name the loop's seat inside a rule or loop that has one already (the
    /// innermost is meant); any other name is new.</summary>
    private Local Bind(int line, string bound) =>
        bound == "seat" ? Temporary(line, ModeType.Seat, "seat") : Declare(line, bound, ModeType.Seat, true);

    /// <summary>Frees the locals declared after a mark (a block's end).</summary>
    private void Free(int mark) => _locals.RemoveRange(mark, _locals.Count - mark);

    private Local? FindLocal(string name) => _locals.LastOrDefault(l => l.Name == name && l.Name.Length > 0);

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
    /// <c>race.seats</c>; a seat that fails <paramref name="where"/> is skipped. <paramref name="body"/> sees the seat
    /// as <paramref name="bound"/>, and bare reads (<c>racing</c>) in the condition and in the body given
    /// <paramref name="implicitReads"/> are of it.</summary>
    private void Seats(int line, string bound, ModeExpr? where, Action<Local> body, bool implicitReads)
    {
        if (_seatLoops >= 2)
        {
            throw new ModeCompileException(line, "loops over the seats nest at most two deep (30 × 30)");
        }
        var mark = _locals.Count;
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
            var skip = -1;
            if (where is not null)
            {
                Value(where, ModeType.Bool, "where");
                skip = JumpAhead(ModeOp.JumpIfZero);
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
            if (skip >= 0)
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
