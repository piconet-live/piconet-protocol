namespace Online.Protocol;

// Expressions: each writes code that leaves its value on the stack, and gives its type. An int meets a real as a real
// (IToF on the int's side, right after it is pushed); a real never becomes an int but through floor, ceil, round or
// trunc; a seat meets a set as its set (SeatBit). `and` and `or` short-circuit: a; Dup; JumpIfZero (or JumpIfNotZero)
// past; Pop; b. A vec is three cells: its operations work them one by one through the compiler's locals.
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
        if (expr is ModeCall call && names.Find(call.Function) is { Kind: ModeNameKind.Sub, Sub.Returns: { } returns })
        {
            return returns;   // (a sub's code is not written twice to learn its type)
        }
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
        if (type == ModeType.Seat && wanted == ModeType.Seats)
        {
            Op(ModeOp.SeatBit);
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
            LoadFrom(local);
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
            case { Kind: ModeNameKind.TeamVariable }:
                throw new ModeCompileException(name.Line, $"'{name.Name}' is kept per team: read it as seat.team.{name.Name} or s.team.{name.Name}");
            case { Kind: ModeNameKind.Sub }:
                throw new ModeCompileException(name.Line, $"'{name.Name}' is a sub: call it, {name.Name}(...)");
        }
        if (name.Name == "seats")
        {
            Op(ModeOp.AllSeats);
            return ModeType.Seats;
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
        if (field.Target is ModeField { Field: "team" } team && names.Variable(field.Field, ModeNameKind.TeamVariable) is { } kept)
        {
            Value(team.Target, ModeType.Seat, $"'.team.{field.Field}'");
            Op(ModeOp.LoadTeamVar, kept.Index);
            return kept.Type;
        }
        if (field.Field is "x" or "y" or "z" && TypeOf(field.Target) == ModeType.Vec)
        {
            var mark = _locals.Count;
            var vec = Spilled(field.Target, ModeType.Vec);
            Op(ModeOp.LoadLocal, vec.Slot + (field.Field[0] - 'x'));
            Free(mark);
            return ModeType.Real;
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
        if (type == ModeType.Vec)
        {
            var mark = _locals.Count;
            var v = Spill(unary.Line, ModeType.Vec);
            for (var k = 0; k < 3; k++)
            {
                Op(ModeOp.LoadLocal, v.Slot + k);
                Op(ModeOp.FNeg);
            }
            Free(mark);
            return type;
        }
        Op(type switch
        {
            ModeType.Int => ModeOp.INeg,
            ModeType.Real => ModeOp.FNeg,
            _ => throw new ModeCompileException(unary.Line, $"'-' takes a number or a vec, not {ModeTypes.Name(type)}"),
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
        var (left, right) = (TypeOf(binary.Left), TypeOf(binary.Right));
        if (binary.Operator == "in")
        {
            Value(binary.Left, ModeType.Seat, "'in'");
            Op(ModeOp.SeatBit);
            Value(binary.Right, ModeType.Seats, "'in'");
            Op(ModeOp.BitAnd);
            Push(0);
            Op(ModeOp.INe);
            return ModeType.Bool;
        }
        if (left == ModeType.Seats || right == ModeType.Seats)
        {
            return Sets(binary, left, right);
        }
        if (left == ModeType.Vec || right == ModeType.Vec)
        {
            return Vecs(binary, left, right);
        }
        if (binary.Operator == "&")
        {
            throw new ModeCompileException(binary.Line, "'&' is the sets' intersection: it takes sets of seats");
        }
        var o = ModeOperators.Binary[binary.Operator];
        var real = ModeTypes.Arithmetic(binary.Line, binary.Operator, left, right, o);
        Operand(binary.Left, real == true);
        Operand(binary.Right, real == true);
        Op(real == true ? o.RealOp!.Value : o.IntOp!.Value);
        return o.Compares || real is null ? ModeType.Bool : real == true ? ModeType.Real : ModeType.Int;
    }

    /// <summary>Sets' operations: <c>+</c> the union, <c>-</c> the difference, <c>&amp;</c> the intersection, a seat on
    /// either side taken as its set; <c>==</c> and <c>!=</c>.</summary>
    private ModeType Sets(ModeBinary binary, ModeType left, ModeType right)
    {
        var op = binary.Operator switch
        {
            "+" => ModeOp.BitOr,
            "-" => ModeOp.BitAndNot,
            "&" => ModeOp.BitAnd,
            "==" => ModeOp.IEq,
            "!=" => ModeOp.INe,
            _ => throw new ModeCompileException(binary.Line, $"'{binary.Operator}' does not take a set of seats: + - & == != and in do"),
        };
        foreach (var (side, type) in new[] { (binary.Left, left), (binary.Right, right) })
        {
            if (type is not (ModeType.Seats or ModeType.Seat))
            {
                throw new ModeCompileException(side.Line, $"'{binary.Operator}' cannot put a set of seats with {ModeTypes.Name(type)}");
            }
            Value(side, ModeType.Seats, $"'{binary.Operator}'");
        }
        Op(op);
        return op is ModeOp.IEq or ModeOp.INe ? ModeType.Bool : ModeType.Seats;
    }

    /// <summary>Vecs' operations, x, y and z each: <c>a + b</c>, <c>a - b</c>, a vec times or by a number, a number
    /// times a vec.</summary>
    private ModeType Vecs(ModeBinary binary, ModeType left, ModeType right)
    {
        var both = left == ModeType.Vec && right == ModeType.Vec;
        var op = (binary.Operator, both) switch
        {
            ("+", true) => ModeOp.FAdd,
            ("-", true) => ModeOp.FSub,
            ("*", false) => ModeOp.FMul,
            ("/", false) when left == ModeType.Vec => ModeOp.FDiv,
            _ => throw new ModeCompileException(binary.Line, both
                ? $"'{binary.Operator}' does not take two vecs: + and - do (dot(a, b) multiplies)"
                : $"'{binary.Operator}' cannot put a vec with {ModeTypes.Name(left == ModeType.Vec ? right : left)}"),
        };
        if (!both && !ModeTypes.IsNumber(left == ModeType.Vec ? right : left))
        {
            throw new ModeCompileException(binary.Line, $"'{binary.Operator}' cannot put a vec with {ModeTypes.Name(left == ModeType.Vec ? right : left)}");
        }
        var mark = _locals.Count;
        var a = Spilled(binary.Left, left == ModeType.Vec ? ModeType.Vec : ModeType.Real);
        var b = Spilled(binary.Right, right == ModeType.Vec ? ModeType.Vec : ModeType.Real);
        for (var k = 0; k < 3; k++)
        {
            Op(ModeOp.LoadLocal, a.Slot + (a.Type == ModeType.Vec ? k : 0));
            Op(ModeOp.LoadLocal, b.Slot + (b.Type == ModeType.Vec ? k : 0));
            Op(op);
        }
        Free(mark);
        return ModeType.Vec;
    }

    /// <summary>An expression as the type into a local of the compiler's.</summary>
    private Local Spilled(ModeExpr expr, ModeType type)
    {
        Value(expr, type, "a vec's operation");
        return Spill(expr.Line, type);
    }

    /// <summary>dot(a, b), or with no <paramref name="b"/> dot(a, a) (a worked out once): a.x b.x + a.y b.y + a.z b.z,
    /// in that order.</summary>
    private void Dot(ModeExpr a, ModeExpr? b)
    {
        var mark = _locals.Count;
        var x = Spilled(a, ModeType.Vec);
        var y = b is null ? x : Spilled(b, ModeType.Vec);
        for (var k = 0; k < 3; k++)
        {
            Op(ModeOp.LoadLocal, x.Slot + k);
            Op(ModeOp.LoadLocal, y.Slot + k);
            Op(ModeOp.FMul);
            if (k > 0)
            {
                Op(ModeOp.FAdd);
            }
        }
        Free(mark);
    }

    /// <summary>A sort key: a number, a bool or a seat.</summary>
    private ModeType Key(ModeExpr key)
    {
        var type = Emit(key);
        if (type is ModeType.Seats or ModeType.Vec)
        {
            throw new ModeCompileException(key.Line, $"a sort key is a number, a bool or a seat, not {ModeTypes.Name(type)}");
        }
        return type;
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
        if (names.Find(call.Function) is { Kind: ModeNameKind.Sub })
        {
            return Inline(call, false)!.Value;
        }
        switch (call.Function, call.Arguments.Count)
        {
            case ("random", 1):
                Value(call.Arguments[0], ModeType.Int, "random");
                Op(ModeOp.Random);
                return ModeType.Int;
            case ("random_real", 0):
                Op(ModeOp.RandomReal);
                return ModeType.Real;
            case ("dot", 2):
                Dot(call.Arguments[0], call.Arguments[1]);
                return ModeType.Real;
            case ("length", 1):
                Dot(call.Arguments[0], null);
                Op(ModeOp.FSqrt);
                return ModeType.Real;
            case ("random" or "random_real" or "dot" or "length", var count):
                throw new ModeCompileException(call.Line, call.Function switch
                {
                    "random" => $"random takes one whole number, n (0 to n - 1), not {count} arguments",
                    "random_real" => "random_real takes nothing: random_real()",
                    "dot" => "dot takes two vecs",
                    _ => "length takes one vec",
                });
        }
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
        if (call.Helper is "first" or "last" or "nth")
        {
            return Pick(call);
        }
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
        Seats(call.Line, call.Bound, call.Where, _ => type = each(call.Value!), true, call.Set);
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

    /// <summary><c>first(S where c by keys)</c>, <c>last</c> and <c>nth(S where c by keys, i)</c>: the seat at that
    /// place in seat order, or in the keys' order (ties to the next key, then the seat, as a sorted loop); none when
    /// there is none (nth: i below 0 or past the last). Without keys, a walk of the seats; with them, a sort buffer
    /// and its walk.</summary>
    private ModeType Pick(ModeSetCall call)
    {
        var keys = call.Keys ?? [];
        var mark = _locals.Count;
        var picked = Temporary(call.Line, ModeType.Seat);
        Push(-1);
        Op(ModeOp.StoreLocal, picked.Slot);
        Local? place = null;
        if (call.Index is { } index)
        {
            Value(index, ModeType.Int, "nth's place");
            place = Spill(call.Line, ModeType.Int);
        }
        // a seat in its order: first keeps the first, last each, nth the one its place counts down to
        void Take(Action load)
        {
            if (call.Helper == "first")
            {
                Op(ModeOp.LoadLocal, picked.Slot);
                Push(-1);
                Op(ModeOp.IEq);
                var taken = JumpAhead(ModeOp.JumpIfZero);
                load();
                Op(ModeOp.StoreLocal, picked.Slot);
                Land(taken);
                return;
            }
            if (place is not null)
            {
                Op(ModeOp.LoadLocal, place.Slot);
                Push(0);
                Op(ModeOp.IEq);
                var before = JumpAhead(ModeOp.JumpIfZero);
                load();
                Op(ModeOp.StoreLocal, picked.Slot);
                Land(before);
                Op(ModeOp.LoadLocal, place.Slot);
                Push(1);
                Op(ModeOp.ISub);
                Op(ModeOp.StoreLocal, place.Slot);
                return;
            }
            load();
            Op(ModeOp.StoreLocal, picked.Slot);
        }
        if (keys.Count == 0)
        {
            Seats(call.Line, call.Bound, call.Where, seat => Take(() => Op(ModeOp.LoadLocal, seat.Slot)), true, call.Set);
        }
        else
        {
            if (_sorts >= ModeWords.Sorts)
            {
                throw new ModeCompileException(call.Line, $"sorted walks (for each ... by, first, last and nth by keys) nest at most {ModeWords.Sorts} deep");
            }
            if (keys.Count > 8)
            {
                throw new ModeCompileException(call.Line, $"{call.Helper} takes at most 8 keys");
            }
            var buffer = _sorts++;
            int descending = 0, reals = 0;
            Op(ModeOp.SortBegin, buffer);
            Seats(call.Line, call.Bound, call.Where, seat =>
            {
                for (var k = 0; k < keys.Count; k++)
                {
                    reals |= Key(keys[k].Key) == ModeType.Real ? 1 << k : 0;
                    descending |= keys[k].Descending ? 1 << k : 0;
                }
                Op(ModeOp.LoadLocal, seat.Slot);
                Op(ModeOp.SortAdd, buffer, keys.Count);
            }, true, call.Set);
            Op(ModeOp.SortRun, buffer, keys.Count, descending, reals);
            if (call.Helper == "first")
            {
                Op(ModeOp.SortNext, buffer);
                Op(ModeOp.StoreLocal, picked.Slot);
            }
            else
            {
                var walked = Temporary(call.Line, ModeType.Seat);
                Loop(ModeWords.Seats, () =>
                {
                    Op(ModeOp.SortNext, buffer);
                    Op(ModeOp.Dup);
                    Op(ModeOp.StoreLocal, walked.Slot);
                    Push(-1);
                    Op(ModeOp.IEq);
                    return [JumpAhead(ModeOp.JumpIfNotZero)];
                }, () => Take(() => Op(ModeOp.LoadLocal, walked.Slot)));
            }
            _sorts--;
        }
        Op(ModeOp.LoadLocal, picked.Slot);
        Free(mark);
        return ModeType.Seat;
    }
}
