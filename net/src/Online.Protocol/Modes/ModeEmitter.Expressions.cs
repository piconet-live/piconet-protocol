namespace Online.Protocol;

// Expressions: each writes code that leaves its value on the stack, and gives its type. An int meets a real as a real
// (IToF on the int's side, right after it is pushed); a real never becomes an int but through floor, ceil, round or
// trunc. `and` and `or` short-circuit: a; Dup; JumpIfZero (or JumpIfNotZero) past; Pop; b.
internal sealed partial class ModeEmitter
{
    private ModeType Emit(ModeExpr expr) => expr switch
    {
        ModeLiteral l => Literal(l),
        ModeName n => Name(n),
        ModeField f => Field(f),
        ModeUnary u => Unary(u),
        ModeBinary b => Binary(b),
        ModeCall c => Call(c),
        ModeSetCall s => SetCall(s),
        _ => throw new ModeCompileException(expr.Line, "this expression is not of api 1"),
    };

    /// <summary>An expression's type, without writing it: it is written and taken back.</summary>
    private ModeType TypeOf(ModeExpr expr)
    {
        var (size, cost, warned) = (_code.Count, Cost, warnings.Count);
        var type = Emit(expr);
        _code.Truncate(size);
        Cost = cost;
        warnings.RemoveRange(warned, warnings.Count - warned);
        return type;
    }

    /// <summary>An expression as the type wanted: an int becomes a real; anything else is an error naming
    /// <paramref name="what"/> takes it.</summary>
    private void Value(ModeExpr expr, ModeType wanted, string what)
    {
        var type = Emit(expr);
        if (type == wanted)
        {
            return;
        }
        if (type == ModeType.Int && wanted == ModeType.Real)
        {
            Op(ModeOp.IToF);
            return;
        }
        var hint = type == ModeType.Real && wanted == ModeType.Int ? ": floor, ceil, round or trunc make an int of a real" : "";
        throw new ModeCompileException(expr.Line, $"{what} takes {ModeTypes.Name(wanted)}, not {ModeTypes.Name(type)}{hint}");
    }

    private ModeType Literal(ModeLiteral literal)
    {
        Push(literal.Bits);
        return literal.Type;
    }

    private ModeType Name(ModeName name)
    {
        if (FindLocal(name.Name) is { } local)
        {
            Op(ModeOp.LoadLocal, local.Slot);
            return local.Type;
        }
        switch (names.Find(name.Name))
        {
            case { Kind: ModeNameKind.Const } c:
                Push(c.Cell);
                return c.Type;
            case { Kind: ModeNameKind.Param } p:
                Op(ModeOp.Param, p.Index);
                return p.Type;
            case { Kind: ModeNameKind.Global } g:
                Op(ModeOp.LoadGlobal, g.Index);
                return g.Type;
            case { Kind: ModeNameKind.SeatVariable }:
                throw new ModeCompileException(name.Line, $"'{name.Name}' is kept per seat: read it as seat.{name.Name} or s.{name.Name}");
        }
        var read = ModeWords.SeatReads.FirstOrDefault(r => r.Name == name.Name);
        if (read.Name is not null && _implicit.Count > 0)
        {
            Op(ModeOp.LoadLocal, _implicit[^1].Slot);
            Op(ModeOp.SeatRead, (long)read.Id);
            return read.Type;
        }
        throw new ModeCompileException(name.Line, name.Name switch
        {
            "seat" => "there is no seat here: an each seat or out rule, or a for each, binds one",
            "race" => "race is read through its fields: race.steps, race.started, race.seats, race.laps",
            "seats" => "seats is the race's seats, for count(seats where ...) and for each ... in seats",
            _ when read.Name is not null => $"'{name.Name}' alone means a seat's read only where a loop names the seat: write seat.{name.Name}",
            _ => $"'{name.Name}' is not declared",
        });
    }

    private bool IsRace(ModeExpr target) => target is ModeName { Name: "race" } && FindLocal("race") is null;

    private ModeType Field(ModeField field)
    {
        if (IsRace(field.Target))
        {
            var race = ModeWords.RaceReads.FirstOrDefault(r => r.Name == field.Field);
            if (race.Name is null)
            {
                throw new ModeCompileException(field.Line, $"race has no '{field.Field}': its reads are {string.Join(", ", ModeWords.RaceReads.Select(r => r.Name))}");
            }
            Op(ModeOp.RaceRead, (long)race.Id);
            return race.Type;
        }
        Value(field.Target, ModeType.Seat, $"'.{field.Field}'");
        var read = ModeWords.SeatReads.FirstOrDefault(r => r.Name == field.Field);
        if (read.Name is not null)
        {
            Op(ModeOp.SeatRead, (long)read.Id);
            return read.Type;
        }
        if (names.Find(field.Field) is { Kind: ModeNameKind.SeatVariable } variable)
        {
            Op(ModeOp.LoadSeatVar, variable.Index);
            return variable.Type;
        }
        throw new ModeCompileException(field.Line, $"a seat has no '{field.Field}' (neither a read of api {ModeWords.Api} nor a seat variable)");
    }

    private ModeType Unary(ModeUnary unary)
    {
        if (unary.Operator == "not")
        {
            Value(unary.Operand, ModeType.Bool, "not");
            Op(ModeOp.Not);
            return ModeType.Bool;
        }
        var type = Emit(unary.Operand);
        Op(type switch
        {
            ModeType.Int => ModeOp.INeg,
            ModeType.Real => ModeOp.FNeg,
            _ => throw new ModeCompileException(unary.Line, $"'-' takes a number, not {ModeTypes.Name(type)}"),
        });
        return type;
    }

    private ModeType Binary(ModeBinary binary)
    {
        if (binary.Operator is "and" or "or")
        {
            Value(binary.Left, ModeType.Bool, $"'{binary.Operator}'");
            Op(ModeOp.Dup);
            var past = JumpAhead(binary.Operator == "and" ? ModeOp.JumpIfZero : ModeOp.JumpIfNotZero);
            Op(ModeOp.Pop);
            Value(binary.Right, ModeType.Bool, $"'{binary.Operator}'");
            Land(past);
            return ModeType.Bool;
        }
        var o = ModeOperators.Binary[binary.Operator];
        var real = ModeTypes.Arithmetic(binary.Line, binary.Operator, TypeOf(binary.Left), TypeOf(binary.Right), o);
        Operand(binary.Left, real == true);
        Operand(binary.Right, real == true);
        Op(real == true ? o.RealOp!.Value : o.IntOp!.Value);
        return o.Compares || real is null ? ModeType.Bool : real == true ? ModeType.Real : ModeType.Int;
    }

    /// <summary>An operand, made a real when the operation is on reals.</summary>
    private void Operand(ModeExpr expr, bool real)
    {
        if (Emit(expr) == ModeType.Int && real)
        {
            Op(ModeOp.IToF);
        }
    }

    private ModeType Call(ModeCall call)
    {
        if (call.Function is "min" or "max" && call.Arguments.Count >= 2)
        {
            var o = ModeOperators.Binary[call.Function];
            var types = call.Arguments.Select(TypeOf).ToList();
            if (types.FirstOrDefault(t => !ModeTypes.IsNumber(t)) is var other && !ModeTypes.IsNumber(other))
            {
                throw new ModeCompileException(call.Line, $"{call.Function} takes numbers, not {ModeTypes.Name(other)}");
            }
            var real = types.Contains(ModeType.Real);
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                Operand(call.Arguments[i], real);
                if (i > 0)
                {
                    Op(real ? o.RealOp!.Value : o.IntOp!.Value);
                }
            }
            return real ? ModeType.Real : ModeType.Int;
        }
        if (!ModeOperators.Functions.TryGetValue(call.Function, out var f) || call.Arguments.Count != 1)
        {
            throw new ModeCompileException(call.Line, ModeOperators.Functions.ContainsKey(call.Function) || call.Function is "min" or "max"
                ? $"{call.Function} takes {(call.Function is "min" or "max" ? "two numbers or more" : "one number")}"
                : $"'{call.Function}' is not a function of api {ModeWords.Api}");
        }
        var argument = Emit(call.Arguments[0]);
        var result = ModeTypes.FunctionResult(call, argument, f);
        if (argument == ModeType.Int && f.IntOp is { } intOp)
        {
            Op(intOp);
            return result;
        }
        if (argument == ModeType.Int)
        {
            Op(ModeOp.IToF);
        }
        Op(f.RealOp);
        return result;
    }

    // The set helpers: a loop over the seats with a local that gathers the answer, pushed when the loop is done.

    private ModeType SetCall(ModeSetCall call)
    {
        var mark = _locals.Count;
        var gathered = Temporary(call.Line, ModeType.Int);
        var type = call.Helper switch
        {
            "count" => Gather(call, gathered, 0, _ =>
            {
                Op(ModeOp.LoadLocal, gathered.Slot);
                Push(1);
                Op(ModeOp.IAdd);
                Op(ModeOp.StoreLocal, gathered.Slot);
                return ModeType.Int;
            }),
            "sum" => Gather(call, gathered, 0, value =>
            {
                Op(ModeOp.LoadLocal, gathered.Slot);
                var type = Number(value, "sum");
                Op(type == ModeType.Real ? ModeOp.FAdd : ModeOp.IAdd);
                Op(ModeOp.StoreLocal, gathered.Slot);
                return type;
            }),
            "any" or "all" => Gather(call, gathered, call.Helper == "any" ? 0 : 1, value =>
            {
                Value(value, ModeType.Bool, call.Helper);
                var skip = JumpAhead(call.Helper == "any" ? ModeOp.JumpIfZero : ModeOp.JumpIfNotZero);
                Push(call.Helper == "any" ? 1 : 0);
                Op(ModeOp.StoreLocal, gathered.Slot);
                Land(skip);
                return ModeType.Bool;
            }),
            _ => Extreme(call, gathered),
        };
        Op(ModeOp.LoadLocal, gathered.Slot);
        Free(mark);
        return type;
    }

    private ModeType Number(ModeExpr value, string what)
    {
        var type = Emit(value);
        if (!ModeTypes.IsNumber(type))
        {
            throw new ModeCompileException(value.Line, $"{what} takes numbers, not {ModeTypes.Name(type)}");
        }
        return type;
    }

    /// <summary>The gathering local set to its start, then the loop; <paramref name="each"/> writes a seat's part
    /// and gives the value's type.</summary>
    private ModeType Gather(ModeSetCall call, Local gathered, int start, Func<ModeExpr, ModeType> each)
    {
        if (call.Helper != "count" && call.Value is null)
        {
            throw new ModeCompileException(call.Line, $"{call.Helper} takes a value: {call.Helper}(e for each seat where c)");
        }
        Push(start);
        Op(ModeOp.StoreLocal, gathered.Slot);
        var type = ModeType.Int;
        Seats(call.Line, call.Bound, call.Where, _ => type = each(call.Value!), true);
        return type;
    }

    /// <summary><c>min(e for each seat where c)</c> and <c>max</c>: the first seat's value, then the least (most) of
    /// it and each next one's; 0 when no seat is taken.</summary>
    private ModeType Extreme(ModeSetCall call, Local gathered)
    {
        var found = Temporary(call.Line, ModeType.Bool);
        Push(0);
        Op(ModeOp.StoreLocal, found.Slot);
        return Gather(call, gathered, 0, value =>
        {
            Op(ModeOp.LoadLocal, found.Slot);
            var first = JumpAhead(ModeOp.JumpIfZero);
            Op(ModeOp.LoadLocal, gathered.Slot);
            var type = Number(value, call.Helper);
            var o = ModeOperators.Binary[call.Helper];
            Op(type == ModeType.Real ? o.RealOp!.Value : o.IntOp!.Value);
            Op(ModeOp.StoreLocal, gathered.Slot);
            var next = JumpAhead(ModeOp.Jump);
            Land(first);
            Emit(value);
            Op(ModeOp.StoreLocal, gathered.Slot);
            Push(1);
            Op(ModeOp.StoreLocal, found.Slot);
            Land(next);
            return type;
        });
    }
}
