namespace Online.Protocol;

/// <summary>An operator of the language: its operation on ints and on reals (null: it takes none of that kind), and
/// whether it compares (its result a bool). The fold functions work a constant out as the interpreter would (cells
/// in, a cell out), for <c>const</c> and the start values.</summary>
internal sealed record ModeOperator(ModeOp? IntOp, ModeOp? RealOp, bool Compares, Func<int, int, int>? FoldInt,
    Func<float, float, int>? FoldReal);

/// <summary>The language's operators and built-in functions, and its cells: an int as it is, a real as its binary32
/// bits (a NaN always 0x7FC00000), a bool 0 or 1, a seat 0 to 29 or -1.</summary>
internal static class ModeOperators
{
    public const int NaN = 0x7FC00000;

    public static int Cell(float value) => float.IsNaN(value) ? NaN : BitConverter.SingleToInt32Bits(value);

    public static float Real(int cell) => BitConverter.Int32BitsToSingle(cell);

    private static int Truth(bool value) => value ? 1 : 0;

    /// <summary>Whole numbers wrap; by 0 gives 0, and the least number by -1 itself (modes.toml's IDiv).</summary>
    public static int Divide(int a, int b) => b == 0 ? 0 : b == -1 ? unchecked(-a) : a / b;

    public static int Remainder(int a, int b) => b == 0 || b == -1 ? 0 : a % b;

    /// <summary>A real to an int as the interpreter does it: saturating at the int's ends, NaN 0.</summary>
    public static int Saturate(double value) =>
        double.IsNaN(value) ? 0 : value >= 2147483648.0 ? int.MaxValue : value < -2147483648.0 ? int.MinValue : (int)value;

    public static readonly Dictionary<string, ModeOperator> Binary = new()
    {
        ["+"] = new(ModeOp.IAdd, ModeOp.FAdd, false, (a, b) => unchecked(a + b), (a, b) => Cell(a + b)),
        ["-"] = new(ModeOp.ISub, ModeOp.FSub, false, (a, b) => unchecked(a - b), (a, b) => Cell(a - b)),
        ["*"] = new(ModeOp.IMul, ModeOp.FMul, false, (a, b) => unchecked(a * b), (a, b) => Cell(a * b)),
        ["/"] = new(ModeOp.IDiv, ModeOp.FDiv, false, Divide, (a, b) => Cell(a / b)),
        ["%"] = new(ModeOp.IMod, null, false, Remainder, null),
        ["=="] = new(ModeOp.IEq, ModeOp.FEq, true, (a, b) => Truth(a == b), (a, b) => Truth(a == b)),
        ["!="] = new(ModeOp.INe, ModeOp.FNe, true, (a, b) => Truth(a != b), (a, b) => Truth(a != b)),
        ["<"] = new(ModeOp.ILt, ModeOp.FLt, true, (a, b) => Truth(a < b), (a, b) => Truth(a < b)),
        ["<="] = new(ModeOp.ILe, ModeOp.FLe, true, (a, b) => Truth(a <= b), (a, b) => Truth(a <= b)),
        [">"] = new(ModeOp.IGt, ModeOp.FGt, true, (a, b) => Truth(a > b), (a, b) => Truth(a > b)),
        [">="] = new(ModeOp.IGe, ModeOp.FGe, true, (a, b) => Truth(a >= b), (a, b) => Truth(a >= b)),
        // min and max take two (or more, pairwise): b when b < a, else a (modes.toml's FMin; a NaN b gives a).
        ["min"] = new(ModeOp.IMin, ModeOp.FMin, false, Math.Min, (a, b) => Cell(b < a ? b : a)),
        ["max"] = new(ModeOp.IMax, ModeOp.FMax, false, Math.Max, (a, b) => Cell(b > a ? b : a)),
    };

    /// <summary>The functions of one argument: the op on an int and on a real, and whether the result is an int
    /// (the real-to-int ones). An int given to a real-only function becomes a real first.</summary>
    public static readonly Dictionary<string, (ModeOp? IntOp, ModeOp RealOp, ModeType Result)> Functions = new()
    {
        ["abs"] = (ModeOp.IAbs, ModeOp.FAbs, ModeType.Real),
        ["floor"] = (null, ModeOp.Floor, ModeType.Int),
        ["ceil"] = (null, ModeOp.Ceil, ModeType.Int),
        ["round"] = (null, ModeOp.Round, ModeType.Int),
        ["trunc"] = (null, ModeOp.Trunc, ModeType.Int),
        ["sqrt"] = (null, ModeOp.FSqrt, ModeType.Real),
    };

    /// <summary>A one-argument function on a constant cell of the given type.</summary>
    public static int FoldFunction(string function, ModeType type, int cell)
    {
        if (type == ModeType.Int)
        {
            return function == "abs" ? (cell == int.MinValue ? cell : Math.Abs(cell)) : FoldFunction(function, ModeType.Real, Cell(cell));
        }
        var x = Real(cell);
        return function switch
        {
            "abs" => cell & int.MaxValue,
            "floor" => Saturate(Math.Floor(x)),
            "ceil" => Saturate(Math.Ceiling(x)),
            "round" => Saturate(Math.Round((double)x, MidpointRounding.AwayFromZero)),
            "trunc" => Saturate(Math.Truncate(x)),
            _ => Cell(x < 0 ? float.NaN : MathF.Sqrt(x)),
        };
    }
}
