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
        if (item.Default is not { Type: ModeType.Bool } value)
        {
            throw new ModeCompileException(item.Line, $"{item.Name} is a switch: its default is on or off");
        }
        return new ModeParam(item.Name, item.Label, ModeParamKind.Switch, ModeType.Int, 0, 1, 1, value.Bits, []);
    }

    /// <summary><c>choice ["a", "b"] [default "a"]</c>: the index of the choice.</summary>
    public static ModeParam Choice(ModeParamItem item)
    {
        var index = item.DefaultChoice is null ? 0 : item.Choices.ToList().IndexOf(item.DefaultChoice);
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

    /// <summary>A preset's value for a parameter, as its cell: a choice's by name, a switch's on or off, a delay's a time, a
    /// number's a number on the parameter's range and steps.</summary>
    public static int PresetCell(ModeParam p, ModePresetValue value)
    {
        string Word() => $"the preset's {p.Name}";
        if (p.Kind == ModeParamKind.Choice)
        {
            var index = value.Choice is null ? -1 : p.Choices.ToList().IndexOf(value.Choice);
            return index >= 0 ? index
                : throw new ModeCompileException(value.Line, $"{Word()} is one of its choices: {string.Join(", ", p.Choices.Select(c => $"\"{c}\""))}");
        }
        if (value.Literal is not { } literal)
        {
            throw new ModeCompileException(value.Line, $"{Word()} is not a choice: it takes {(p.Kind == ModeParamKind.Switch ? "on or off" : "a value of its range")}");
        }
        if (p.Kind == ModeParamKind.Switch)
        {
            return literal.Type == ModeType.Bool ? literal.Bits : throw new ModeCompileException(value.Line, $"{Word()} is a switch: on or off");
        }
        if (p.Kind == ModeParamKind.Delay != literal.Time || literal.Type is not (ModeType.Int or ModeType.Real))
        {
            throw new ModeCompileException(value.Line, $"{Word()} is {(p.Kind == ModeParamKind.Delay ? "a time (30s)" : "a number")}");
        }
        var cell = p.Type == ModeType.Real && literal.Type == ModeType.Int ? ModeOperators.Cell(literal.Bits) : literal.Bits;
        if (p.Type == ModeType.Int && literal.Type == ModeType.Real)
        {
            throw new ModeCompileException(value.Line, $"{Word()} is a whole number");
        }
        return p.Fits(cell) ? cell : throw new ModeCompileException(value.Line, $"{Word()} {literal.Text} is outside what the parameter allows");
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
