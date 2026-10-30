namespace Online.Protocol;

// Statements. An action pushes its arguments in order (the seat first) and then Action; a seat variable is stored
// with the seat pushed before the value (StoreSeatVar pops the value, then the seat). `x op= e` loads x, works it
// with e and stores it back.
internal sealed partial class ModeEmitter
{
    private void Statements(IReadOnlyList<ModeStatement> statements)
    {
        var mark = _locals.Count;
        foreach (var statement in statements)
        {
            Statement(statement);
        }
        Free(mark);
    }

    private void Statement(ModeStatement statement)
    {
        switch (statement)
        {
            case ModeLocal local:
                LocalStatement(local);
                break;
            case ModeAssign assign:
                Assign(assign);
                break;
            case ModeIf branch:
                If(branch);
                break;
            case ModeForEach loop when loop.Keys.Count == 0:
                Seats(loop.Line, loop.Bound, loop.Where, _ => Statements(loop.Body), false, loop.Set);
                break;
            case ModeForEach loop:
                Sorted(loop);
                break;
            case ModeRepeat repeat:
                Repeat(repeat);
                break;
            case ModeActionStatement action:
                Act(action);
                break;
            case ModeWait wait:
                Wait(wait);
                break;
            case ModeCallStatement call:
                if (names.Find(call.Call.Function) is not { Kind: ModeNameKind.Sub })
                {
                    throw new ModeCompileException(call.Line, $"'{call.Call.Function}' is not a sub: call takes a sub");
                }
                Inline(call.Call, true);
                break;
            case ModeReturn back:
                Return(back);
                break;
            default:
                throw new ModeCompileException(statement.Line, "this statement is not of api 1");
        }
    }

    private void LocalStatement(ModeLocal local)
    {
        if (local.Value is null)
        {
            Zero(local.Type);
        }
        else
        {
            Value(local.Value, local.Type, $"the {ModeTypeWord(local.Type)} {local.Name}");
        }
        StoreTo(Declare(local.Line, local.Name, local.Type));
    }

    /// <summary>A type's start value: 0, false, none, the empty set, a vec of zeros.</summary>
    private void Zero(ModeType type)
    {
        for (var k = 0; k < Width(type); k++)
        {
            Push(type == ModeType.Seat ? -1 : 0);
        }
    }

    private static string ModeTypeWord(ModeType type) => ModeTypes.Name(type)[(type == ModeType.Int ? 3 : 2)..];

    private void Assign(ModeAssign assign)
    {
        switch (assign.Target)
        {
            case ModeName name when FindLocal(name.Name) is { } local:
                if (local.Fixed)
                {
                    throw new ModeCompileException(assign.Line, $"'{name.Name}' is the seat the rule or loop is on: it cannot be assigned");
                }
                Work(assign, local.Type, $"the {ModeTypeWord(local.Type)} {name.Name}", () => LoadFrom(local));
                StoreTo(local);
                break;
            case ModeName name when names.Find(name.Name) is { Kind: ModeNameKind.Global } global:
                Work(assign, global.Type, $"the {ModeTypeWord(global.Type)} {name.Name}", () => Op(ModeOp.LoadGlobal, global.Index));
                Op(ModeOp.StoreGlobal, global.Index);
                break;
            case ModeName name:
                throw new ModeCompileException(assign.Line, names.Find(name.Name) switch
                {
                    { Kind: ModeNameKind.Const } => $"'{name.Name}' is a constant: it cannot be assigned",
                    { Kind: ModeNameKind.Param } => $"'{name.Name}' is a parameter: the lobby sets it, a rule cannot",
                    { Kind: ModeNameKind.SeatVariable } => $"'{name.Name}' is kept per seat: write seat.{name.Name} or s.{name.Name}",
                    { Kind: ModeNameKind.TeamVariable } => $"'{name.Name}' is kept per team: write seat.team.{name.Name} or s.team.{name.Name}",
                    { Kind: ModeNameKind.Sub } => $"'{name.Name}' is a sub: it cannot be assigned",
                    _ => $"'{name.Name}' is not declared",
                });
            case ModeField field:
                AssignField(assign, field);
                break;
            default:
                throw new ModeCompileException(assign.Line, "only a variable or a seat's field can be assigned");
        }
    }

    /// <summary>The value an assignment stores, on the stack: <c>e</c> for <c>=</c>, else the target loaded, worked
    /// with e (a set's <c>+=</c> and <c>-=</c> add and take away a seat or a set).</summary>
    private void Work(ModeAssign assign, ModeType type, string what, Action load)
    {
        if (assign.Operator == "=")
        {
            Value(assign.Value, type, what);
            return;
        }
        if (type == ModeType.Seats && assign.Operator is "+=" or "-=")
        {
            load();
            Value(assign.Value, ModeType.Seats, what);
            Op(assign.Operator == "+=" ? ModeOp.BitOr : ModeOp.BitAndNot);
            return;
        }
        if (!ModeTypes.IsNumber(type))
        {
            throw new ModeCompileException(assign.Line, $"'{assign.Operator}' works numbers, and {what} is {ModeTypes.Name(type)}");
        }
        load();
        Value(assign.Value, type, what);
        var o = ModeOperators.Binary[assign.Operator[..1]];
        Op(type == ModeType.Real ? o.RealOp!.Value : o.IntOp!.Value);
    }

    private void AssignField(ModeAssign assign, ModeField field)
    {
        if (IsRace(field.Target))
        {
            throw new ModeCompileException(assign.Line, $"race.{field.Field} is the game's to say: it cannot be assigned");
        }
        if (field.Target is ModeField { Field: "team" } team && names.Variable(field.Field, ModeNameKind.TeamVariable) is { } kept)
        {
            Value(team.Target, ModeType.Seat, $"'.team.{field.Field}'");
            Work(assign, kept.Type, $"the {ModeTypeWord(kept.Type)} {field.Field}", () =>
            {
                Emit(team.Target);
                Op(ModeOp.LoadTeamVar, kept.Index);
            });
            Op(ModeOp.StoreTeamVar, kept.Index);
            return;
        }
        var action = ModeWords.Actions.FirstOrDefault(a => a.Name == "seat." + field.Field);
        var read = ModeWords.SeatReads.FirstOrDefault(r => r.Name == field.Field);
        Value(field.Target, ModeType.Seat, $"'.{field.Field}'");
        if (action.Name is not null)
        {
            Work(assign, action.Args[1], $"seat.{field.Field}", () =>
            {
                Emit(field.Target);
                Op(ModeOp.SeatRead, (long)read.Id);
            });
            Op(ModeOp.Action, (long)action.Id);
            return;
        }
        if (names.Find(field.Field) is { Kind: ModeNameKind.SeatVariable } variable)
        {
            Work(assign, variable.Type, $"the {ModeTypeWord(variable.Type)} {field.Field}", () =>
            {
                Emit(field.Target);
                Op(ModeOp.LoadSeatVar, variable.Index);
            });
            Op(ModeOp.StoreSeatVar, variable.Index);
            return;
        }
        throw new ModeCompileException(assign.Line, read.Name is not null
            ? $"seat.{field.Field} is the game's to say: it cannot be assigned"
            : $"a seat has no '{field.Field}' (neither a word of api {ModeWords.Api} nor a seat variable)");
    }

    private void If(ModeIf branch)
    {
        Value(branch.Condition, ModeType.Bool, "if");
        var otherwise = JumpAhead(ModeOp.JumpIfZero);
        Statements(branch.Then);
        if (branch.Else is null)
        {
            Land(otherwise);
            return;
        }
        var end = JumpAhead(ModeOp.Jump);
        Land(otherwise);
        Statements(branch.Else);
        Land(end);
    }

    /// <summary><c>for each ... by</c>: the seats that pass, each with its keys, into a sort buffer (the loops'
    /// depth: 0, or 1 inside another sorted loop); SortRun; then the buffer's walk, until SortNext gives -1.</summary>
    private void Sorted(ModeForEach loop)
    {
        if (_sorts >= ModeWords.Sorts)
        {
            throw new ModeCompileException(loop.Line, $"sorted loops (for each ... by) nest at most {ModeWords.Sorts} deep");
        }
        if (loop.Keys.Count > 8)
        {
            throw new ModeCompileException(loop.Line, "a sorted loop takes at most 8 keys");
        }
        var buffer = _sorts;
        int descending = 0, reals = 0;
        _sorts++;   // (a key's own sorted helper takes the next buffer)
        Op(ModeOp.SortBegin, buffer);
        Seats(loop.Line, loop.Bound, loop.Where, seat =>
        {
            for (var k = 0; k < loop.Keys.Count; k++)
            {
                reals |= Key(loop.Keys[k].Key) == ModeType.Real ? 1 << k : 0;
                descending |= loop.Keys[k].Descending ? 1 << k : 0;
            }
            Op(ModeOp.LoadLocal, seat.Slot);
            Op(ModeOp.SortAdd, buffer, loop.Keys.Count);
        }, true, loop.Set);
        Op(ModeOp.SortRun, buffer, loop.Keys.Count, descending, reals);
        var mark = _locals.Count;
        var walked = Bind(loop.Line, loop.Bound);
        _seatLoops++;
        Loop(ModeWords.Seats, () =>
        {
            Op(ModeOp.SortNext, buffer);
            Op(ModeOp.Dup);
            Push(-1);
            Op(ModeOp.IEq);
            return [JumpAhead(ModeOp.JumpIfNotZero)];
        }, () =>
        {
            Op(ModeOp.StoreLocal, walked.Slot);
            Statements(loop.Body);
        });
        Op(ModeOp.Pop);
        _seatLoops--;
        _sorts--;
        Free(mark);
    }

    /// <summary><c>repeat N</c>: a counter from N down to 0. N is a whole-number constant or parameter; the count takes
    /// a parameter's most.</summary>
    private void Repeat(ModeRepeat repeat)
    {
        long times;
        if (names.Fold(repeat.Count) is var (type, value) && type == ModeType.Int)
        {
            Push(value);
            times = Math.Max(0, value);
        }
        else if (repeat.Count is ModeName name && names.Find(name.Name) is { Kind: ModeNameKind.Param, Type: ModeType.Int } param)
        {
            Op(ModeOp.Param, param.Index);
            times = Math.Max(0, param.Param!.Most);
        }
        else
        {
            throw new ModeCompileException(repeat.Line, "repeat takes a whole-number constant or parameter");
        }
        var mark = _locals.Count;
        var counter = Temporary(repeat.Line, ModeType.Int);
        Op(ModeOp.StoreLocal, counter.Slot);
        Loop(times, () =>
        {
            Op(ModeOp.LoadLocal, counter.Slot);
            Push(0);
            Op(ModeOp.IGt);
            return [JumpAhead(ModeOp.JumpIfZero)];
        }, () =>
        {
            Statements(repeat.Body);
            Op(ModeOp.LoadLocal, counter.Slot);
            Push(1);
            Op(ModeOp.ISub);
            Op(ModeOp.StoreLocal, counter.Slot);
        });
        Free(mark);
    }

    private void Act(ModeActionStatement statement)
    {
        var action = ModeWords.Actions.First(a => a.Name == statement.Action);
        if (statement.Arguments.Count != action.Args.Count)
        {
            throw new ModeCompileException(statement.Line, $"{action.Name} takes {action.Args.Count} argument{(action.Args.Count == 1 ? "" : "s")}, not {statement.Arguments.Count}");
        }
        for (var i = 0; i < action.Args.Count; i++)
        {
            if (statement.Arguments[i] is ModeLiteral { Type: ModeType.Seat, Bits: -1 })
            {
                warnings.Add(ModeCompiler.Message(statement.Line, $"{action.Name} on none does nothing"));
            }
            Value(statement.Arguments[i], action.Args[i], action.Name);
        }
        Op(ModeOp.Action, (long)action.Id);
        Used.TryAdd(action.Id, statement.Line);
    }
}

// Waits and subs.
internal sealed partial class ModeEmitter
{
    /// <summary><c>wait t [abort|restart when c];</c>: the condition's code (jumped over: the interpreter runs it from
    /// the wait record, with the saved locals), then t, then Wait, which keeps the slots in use.</summary>
    private void Wait(ModeWait wait)
    {
        if (_noWait > 0)
        {
            throw new ModeCompileException(wait.Line, "a wait stands in a rule's body (or a sub called with call), not in a sub called inside an expression or a condition");
        }
        if (_sorts > 0)
        {
            throw new ModeCompileException(wait.Line, "a rule cannot wait inside a sorted loop (for each ... by): the order is the step's");
        }
        if (Slots > ModeWords.WaitLocals)
        {
            throw new ModeCompileException(wait.Line, $"a waiting rule keeps at most {ModeWords.WaitLocals} locals (its event's seat, the loops' and a vec's three included): here it would keep {Slots}");
        }
        var condition = ModeRule.None;
        if (wait.Condition is not null)
        {
            var over = JumpAhead(ModeOp.Jump);
            condition = Here;
            _noWait++;
            Value(wait.Condition, ModeType.Bool, $"{wait.Kind} when");
            _noWait--;
            Op(ModeOp.Ret);
            Land(over);
        }
        Value(wait.Steps, ModeType.Int, "wait");
        Op(ModeOp.Wait, Slots, wait.Kind switch { "abort" => 1, "restart" => 2, _ => 0 }, condition);
        _waits = true;
    }

    /// <summary>A sub, written here: the arguments (the caller's sight) into its parameters (a new scope: the caller's
    /// locals and loop seats out of its sight), its body, its returns landed at its end; in an expression, its value
    /// pushed. A sub inside an expression may not wait.</summary>
    private ModeType? Inline(ModeCall call, bool statement)
    {
        var sub = names.Find(call.Function)!.Sub!;
        if (_subs.Any(s => s.Name == sub.Name))
        {
            throw new ModeCompileException(call.Line, $"the sub {sub.Name} calls itself: a sub may not, directly or through another");
        }
        if (call.Arguments.Count != sub.Params.Count)
        {
            throw new ModeCompileException(call.Line, $"{sub.Name} takes {sub.Params.Count} argument{(sub.Params.Count == 1 ? "" : "s")}, not {call.Arguments.Count}");
        }
        if (!statement && sub.Returns is null)
        {
            throw new ModeCompileException(call.Line, $"the sub {sub.Name} gives no value: call it as a statement, call {sub.Name}(...);");
        }
        var mark = _locals.Count;
        for (var i = 0; i < sub.Params.Count; i++)
        {
            Value(call.Arguments[i], sub.Params[i].Type, $"{sub.Name}'s {sub.Params[i].Name}");
        }
        var (scope, seen) = (_scope, _implicit.ToList());
        _scope = _locals.Count;
        _implicit.Clear();
        var parameters = sub.Params.Select(p => Declare(call.Line, p.Name, p.Type)).ToList();
        for (var i = parameters.Count - 1; i >= 0; i--)
        {
            StoreTo(parameters[i]);
        }
        Local? value = null;
        if (sub.Returns is { } type)
        {
            Zero(type);
            value = Spill(call.Line, type);
        }
        var jumps = new List<int>();
        _subs.Add((sub.Name, sub.Returns, value, jumps));
        _noWait += statement ? 0 : 1;
        Statements(sub.Body);
        _noWait -= statement ? 0 : 1;
        _subs.RemoveAt(_subs.Count - 1);
        foreach (var jump in jumps)
        {
            Land(jump);
        }
        _scope = scope;
        _implicit.AddRange(seen);
        if (!statement && value is not null)
        {
            LoadFrom(value);
        }
        Free(mark);
        return sub.Returns;
    }

    private void Return(ModeReturn back)
    {
        if (_subs.Count == 0)
        {
            throw new ModeCompileException(back.Line, "return is a sub's: a rule's body ends at its end");
        }
        var (name, type, value, jumps) = _subs[^1];
        if (back.Value is not null)
        {
            if (type is not { } wanted)
            {
                throw new ModeCompileException(back.Line, $"the sub {name} gives no value: return;");
            }
            Value(back.Value, wanted, $"{name}'s return");
            StoreTo(value!);
        }
        else if (type is not null)
        {
            throw new ModeCompileException(back.Line, $"the sub {name} gives {ModeTypes.Name(type.Value)}: return it");
        }
        jumps.Add(JumpAhead(ModeOp.Jump));
    }
}
