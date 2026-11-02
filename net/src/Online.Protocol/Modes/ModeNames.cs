namespace Online.Protocol;

/// <summary>What a name declared at the file's level is: a constant (its cell), a parameter, a global, a seat's or a
/// team's variable (their index), or a sub.</summary>
internal enum ModeNameKind
{
    Const,
    Param,
    Global,
    SeatVariable,
    TeamVariable,
    Sub,
}

internal sealed record ModeDeclared(ModeNameKind Kind, ModeType Type, int Index, int Cell = 0, ModeParam? Param = null,
    ModeSubItem? Sub = null);

/// <summary>The names a file declares at its level, in one table: a name names one thing, and is declared before it
/// is used (the language's section 2).</summary>
internal sealed class ModeNames
{
    private readonly Dictionary<string, ModeDeclared> _names = [];

    public int Count(ModeNameKind kind) => _names.Values.Count(n => n.Kind == kind);

    public ModeDeclared? Find(string name) => _names.GetValueOrDefault(name);

    /// <summary>The variable kept per seat or per team named so (a field <c>s.name</c>, <c>s.team.name</c>).</summary>
    public ModeDeclared? Variable(string name, ModeNameKind kind) =>
        Find(name) is { } found && found.Kind == kind ? found : null;

    public void Declare(int line, string name, ModeDeclared declared)
    {
        if (!_names.TryAdd(name, declared))
        {
            throw new ModeCompileException(line, _names[name] is { Kind: ModeNameKind.Sub, Index: -1 }
                ? $"'{name}' is a sub of the standard library's: name yours otherwise"
                : $"'{name}' is already declared");
        }
    }

    /// <summary>A constant expression's type and cell (literals, constants, the operators and functions on them), or
    /// null when it reads anything a race gives.</summary>
    public (ModeType Type, int Cell)? Fold(ModeExpr expr) => expr switch
    {
        ModeLiteral l => (l.Type, l.Bits),
        ModeName n when Find(n.Name) is { Kind: ModeNameKind.Const } c => (c.Type, c.Cell),
        ModeUnary u => FoldUnary(u),
        ModeBinary b => FoldBinary(b),
        ModeCall c => FoldCall(c),
        _ => null,
    };

    /// <summary>A constant of the type wanted (an int becomes a real; nothing else changes type).</summary>
    public int Constant(ModeExpr expr, ModeType wanted, string what)
    {
        var (type, cell) = Fold(expr) ?? throw new ModeCompileException(expr.Line, $"{what} must be a constant");
        if (type == wanted)
        {
            return cell;
        }
        if (type == ModeType.Int && wanted == ModeType.Real)
        {
            return ModeOperators.Cell(cell);
        }
        if (type == ModeType.Seat && wanted == ModeType.Seats)
        {
            return ModeOperators.SeatBit(cell);
        }
        throw new ModeCompileException(expr.Line, $"{what} is {ModeTypes.Name(wanted)}, not {ModeTypes.Name(type)}");
    }

    private (ModeType, int)? FoldUnary(ModeUnary u)
    {
        if (Fold(u.Operand) is not var (type, cell))
        {
            return null;
        }
        return (u.Operator, type) switch
        {
            ("not", ModeType.Bool) => (type, cell == 0 ? 1 : 0),
            ("-", ModeType.Int) => (type, unchecked(-cell)),
            ("-", ModeType.Real) => (type, cell ^ int.MinValue),
            _ => throw new ModeCompileException(u.Line, $"'{u.Operator}' does not take {ModeTypes.Name(type)}"),
        };
    }

    private (ModeType, int)? FoldBinary(ModeBinary b)
    {
        if (Fold(b.Left) is not var (ta, a) || Fold(b.Right) is not var (tb, c))
        {
            return null;
        }
        if (b.Operator is "in" || ta == ModeType.Seats || tb == ModeType.Seats)
        {
            return null;   // (the sets' operations are written as code, never folded)
        }
        if (b.Operator is "and" or "or")
        {
            ModeTypes.Require(b.Left, ta, ModeType.Bool, $"'{b.Operator}'");
            ModeTypes.Require(b.Right, tb, ModeType.Bool, $"'{b.Operator}'");
            return (ModeType.Bool, b.Operator == "and" ? a & c : a | c);
        }
        return Apply(b.Line, b.Operator, ta, a, tb, c);
    }

    private static (ModeType, int) Apply(int line, string op, ModeType ta, int a, ModeType tb, int b)
    {
        var o = ModeOperators.Binary[op];
        var real = ModeTypes.Arithmetic(line, op, ta, tb, o);
        if (real is null)
        {
            return (ModeType.Bool, o.FoldInt!(a, b));
        }
        if (real == true)
        {
            var (x, y) = (ta == ModeType.Int ? (float)a : ModeOperators.Real(a), tb == ModeType.Int ? (float)b : ModeOperators.Real(b));
            return (o.Compares ? ModeType.Bool : ModeType.Real, o.FoldReal!(x, y));
        }
        return (o.Compares ? ModeType.Bool : ModeType.Int, o.FoldInt!(a, b));
    }

    private (ModeType, int)? FoldCall(ModeCall c)
    {
        if (c.Function is not ("min" or "max") && !ModeOperators.Functions.ContainsKey(c.Function))
        {
            return null;   // a sub, random, dot, length: not a constant
        }
        var arguments = c.Arguments.Select(Fold).ToList();
        if (arguments.Any(a => a is null))
        {
            return null;
        }
        if (c.Function is "min" or "max" && arguments.Count >= 2)
        {
            var (type, cell) = arguments[0]!.Value;
            foreach (var next in arguments.Skip(1))
            {
                (type, cell) = Apply(c.Line, c.Function, type, cell, next!.Value.Type, next.Value.Cell);
            }
            return (type, cell);
        }
        if (ModeOperators.Functions.TryGetValue(c.Function, out var f) && arguments.Count == 1)
        {
            var (type, cell) = arguments[0]!.Value;
            var result = ModeTypes.FunctionResult(c, type, f);
            return (result, ModeOperators.FoldFunction(c.Function, type, cell));
        }
        throw new ModeCompileException(c.Line, $"'{c.Function}' is not a function of api {ModeWords.Api}, or not with {arguments.Count} arguments");
    }
}

/// <summary>The type rules the folder and the emitter share (the language's section 4).</summary>
internal static class ModeTypes
{
    public static string Name(ModeType type) => type switch
    {
        ModeType.Int => "an int",
        ModeType.Real => "a real",
        ModeType.Bool => "a bool",
        ModeType.Seat => "a seat",
        ModeType.Seats => "a set of seats",
        _ => "a vec",
    };

    public static bool IsNumber(ModeType type) => type is ModeType.Int or ModeType.Real;

    public static void Require(ModeExpr at, ModeType type, ModeType wanted, string what)
    {
        if (type != wanted)
        {
            throw new ModeCompileException(at.Line, $"{what} takes {Name(wanted)}, not {Name(type)}");
        }
    }

    /// <summary>How an operator works on two types: true on reals (an int side becomes a real), false on ints, null
    /// on cells compared as they are (bools, seats: == and != only). Anything else is an error.</summary>
    public static bool? Arithmetic(int line, string op, ModeType a, ModeType b, ModeOperator o)
    {
        if (IsNumber(a) && IsNumber(b))
        {
            var real = a == ModeType.Real || b == ModeType.Real;
            if (real && o.RealOp is null)
            {
                throw new ModeCompileException(line, $"'{op}' takes whole numbers: use floor, ceil, round or trunc first");
            }
            return real;
        }
        if (a == b && op is "==" or "!=")
        {
            return null;
        }
        throw new ModeCompileException(line, a == b
            ? $"'{op}' does not take {Name(a)}"
            : $"'{op}' cannot put {Name(a)} with {Name(b)}");
    }

    /// <summary>A one-argument function's result type for its argument's type.</summary>
    public static ModeType FunctionResult(ModeCall c, ModeType argument, (ModeOp? IntOp, ModeOp RealOp, ModeType Result) f)
    {
        if (!IsNumber(argument))
        {
            throw new ModeCompileException(c.Line, $"{c.Function} takes a number, not {Name(argument)}");
        }
        if (argument == ModeType.Int && f.Result == ModeType.Int)
        {
            throw new ModeCompileException(c.Line, $"{c.Function} takes a real: its argument is already an int");
        }
        return c.Function == "abs" ? argument : f.Result;
    }
}
