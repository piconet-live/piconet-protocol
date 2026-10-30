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
                Seats(loop.Line, loop.Bound, loop.Where, _ => Statements(loop.Body), false);
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
            default:
                throw new ModeCompileException(statement.Line, "this statement is not of api 1");
        }
    }

    private void LocalStatement(ModeLocal local)
    {
        if (local.Value is null)
        {
            Push(local.Type == ModeType.Seat ? -1 : 0);
        }
        else
        {
            Value(local.Value, local.Type, $"the {ModeTypeWord(local.Type)} {local.Name}");
        }
        Op(ModeOp.StoreLocal, Declare(local.Line, local.Name, local.Type).Slot);
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
                Work(assign, local.Type, $"the {ModeTypeWord(local.Type)} {name.Name}", () => Op(ModeOp.LoadLocal, local.Slot));
                Op(ModeOp.StoreLocal, local.Slot);
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
    /// with e.</summary>
    private void Work(ModeAssign assign, ModeType type, string what, Action load)
    {
        if (assign.Operator == "=")
        {
            Value(assign.Value, type, what);
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
        Op(ModeOp.SortBegin, buffer);
        Seats(loop.Line, loop.Bound, loop.Where, seat =>
        {
            for (var k = 0; k < loop.Keys.Count; k++)
            {
                reals |= Emit(loop.Keys[k].Key) == ModeType.Real ? 1 << k : 0;
                descending |= loop.Keys[k].Descending ? 1 << k : 0;
            }
            Op(ModeOp.LoadLocal, seat.Slot);
            Op(ModeOp.SortAdd, buffer, loop.Keys.Count);
        }, true);
        Op(ModeOp.SortRun, buffer, loop.Keys.Count, descending, reals);
        var mark = _locals.Count;
        var walked = Bind(loop.Line, loop.Bound);
        _sorts++;
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
    }
}
