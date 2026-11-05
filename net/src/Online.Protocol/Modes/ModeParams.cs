using System.Globalization;

namespace Online.Protocol;

/// <summary>A parameter's declaration made the package's record (formats.md section 9's <c>PARM</c>), with its range
/// checked: least at most most, a step above 0, the default inside the range and, for a whole-number range, on a
/// step from the least.</summary>
internal static class ModeParams
{
    /// <summary><c>number A..B [step S] [default D]</c>: a real when any of them is written as one, else an int.</summary>
    public static ModeParam Number(ModeParamItem item)
    {
        var literals = new[] { item.Least, item.Most, item.Step, item.Default };
        if (literals.Any(l => l is { Type: not (ModeType.Int or ModeType.Real) }))
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s range is numbers");
        }
        var type = literals.Any(l => l?.Type == ModeType.Real) ? ModeType.Real : ModeType.Int;
        int Cell(ModeLiteral l) => type == ModeType.Real && l.Type == ModeType.Int ? ModeOperators.Cell(l.Bits) : l.Bits;
        var least = Cell(item.Least!);
        var most = Cell(item.Most!);
        var step = item.Step is null ? (type == ModeType.Int ? 1 : 0) : Cell(item.Step);
        var value = item.Default is null ? least : Cell(item.Default);
        return Checked(item, new ModeParam(item.Name, item.Label, ModeParamKind.Number, type, least, most, step, value, []), item.Step is not null);
    }

    /// <summary><c>delay T1..T2 [step T] [default T]</c>: times, kept in whole steps.</summary>
    public static ModeParam Delay(ModeParamItem item)
    {
        var literals = new[] { item.Least, item.Most, item.Step, item.Default };
        if (literals.Any(l => l is { Time: false }))
        {
            throw new ModeCompileException(item.Line, $"{item.Name} is a delay: its range is times (10s..2min)");
        }
        var least = item.Least!.Bits;
        var param = new ModeParam(item.Name, item.Label, ModeParamKind.Delay, ModeType.Int, least, item.Most!.Bits,
            item.Step?.Bits ?? 1, item.Default?.Bits ?? least, []);
        return Checked(item, param, item.Step is not null);
    }

    /// <summary><c>switch default on|off</c>.</summary>
    public static ModeParam Switch(ModeParamItem item)
    {
        if (item.Or is not null && item.Default is null && item.DefaultChoice == item.Or)
        {
            return new ModeParam(item.Name, item.Label, ModeParamKind.Switch, ModeType.Int, 0, 1, 1, 0, []);
        }
        if (item.Default is not { Type: ModeType.Bool } value)
        {
            throw new ModeCompileException(item.Line, $"{item.Name} is a switch: its default is on or off");
        }
        return new ModeParam(item.Name, item.Label, ModeParamKind.Switch, ModeType.Int, 0, 1, 1, value.Bits, []);
    }

    /// <summary><c>choice ["a", "b"] [default "a"]</c>: the index of the choice.</summary>
    public static ModeParam Choice(ModeParamItem item)
    {
        var index = item.DefaultChoice is null ? 0
            : item.Or is not null && item.DefaultChoice == item.Or ? item.Choices.Count
            : item.Choices.ToList().IndexOf(item.DefaultChoice);
        if (index < 0)
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s default \"{item.DefaultChoice}\" is not one of its choices");
        }
        if (item.Choices.Distinct().Count() != item.Choices.Count)
        {
            throw new ModeCompileException(item.Line, $"{item.Name} names a choice twice");
        }
        return new ModeParam(item.Name, item.Label, ModeParamKind.Choice, ModeType.Int, 0, item.Choices.Count - 1, 1, index, item.Choices);
    }

    /// <summary>A parameter with <c>or "Name"</c>: one value more, one step past its range (a choice's: after its
    /// choices; a switch's: 2), named, that states nothing, and its default when the default names it. Whole numbers
    /// only: a real has no next step.</summary>
    public static ModeParam Optional(ModeParamItem item, ModeParam p)
    {
        if (item.Or is not { } or)
        {
            return item.DefaultChoice is not null && item.Kind != ModeParamKind.Choice
                ? throw new ModeCompileException(item.Line, $"{item.Name}'s default \"{item.DefaultChoice}\" is no value of it")
                : p;
        }
        if (p.Type == ModeType.Real)
        {
            throw new ModeCompileException(item.Line, $"{item.Name} is a real: or \"{or}\" goes on a whole number, a delay, a switch or a choice");
        }
        if (or.Length == 0 || p.Choices.Contains(or))
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s or \"{or}\" is a name of its own, not one of its choices");
        }
        if (item.DefaultChoice is { } named && named != or && item.Kind != ModeParamKind.Choice)
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s default \"{named}\" is not \"{or}\"");
        }
        var unset = p.Kind == ModeParamKind.Choice ? p.Choices.Count : (long)p.Most + Math.Max(p.Step, 1);
        if (unset > int.MaxValue)
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s range has no step past its most for or \"{or}\"");
        }
        var stated = item.DefaultChoice == or || (p.Kind == ModeParamKind.Choice && p.Default == p.Choices.Count);
        return p with { Most = (int)unset, Default = stated ? (int)unset : p.Default, Or = or };
    }

    private static ModeParam Checked(ModeParamItem item, ModeParam p, bool stepWritten)
    {
        string Show(int cell) => p.Type == ModeType.Real
            ? ModeOperators.Real(cell).ToString(CultureInfo.InvariantCulture)
            : cell.ToString(CultureInfo.InvariantCulture);
        var real = p.Type == ModeType.Real;
        double At(int cell) => real ? ModeOperators.Real(cell) : cell;
        if (!(At(p.Least) <= At(p.Most)))
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s range {Show(p.Least)}..{Show(p.Most)} is empty: the least comes first");
        }
        if (stepWritten && !(At(p.Step) > 0))
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s step is above 0");
        }
        if (!p.Holds(p.Default))
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s default {Show(p.Default)} is outside {Show(p.Least)}..{Show(p.Most)}");
        }
        if (!real && (((long)p.Default - p.Least) % p.Step != 0))
        {
            throw new ModeCompileException(item.Line, $"{item.Name}'s default {Show(p.Default)} is not on a step of {Show(p.Step)} from {Show(p.Least)}");
        }
        return p;
    }
}
