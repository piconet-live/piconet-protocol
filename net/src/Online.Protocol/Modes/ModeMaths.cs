namespace Online.Protocol;

/// <summary>The game's own arithmetic a mode's reals use where the language says so (language.md section 5).</summary>
public static class ModeMaths
{
    /// <summary>The game's square root (main.dol 0x8006D0B4), which FSqrt is, as the interpreter works it
    /// (port/core/src/mode.cpp's GameSqrt, the same steps): a NaN stays one, infinity itself; zero, a negative number
    /// or a denormal 0; else x times 1/sqrt(x), from the processor's estimate (frsqrte) and two Newton steps
    /// y = y (1.5 - 0.5 x y y), each operation the processor's single-precision one (fmuls rounds its second operand
    /// to 25 bits first; fnmsubs is fused, then rounded to single).</summary>
    public static float GameSqrt(float x)
    {
        if (float.IsNaN(x) || float.IsPositiveInfinity(x))
        {
            return x;
        }
        if (!(x > 0) || !float.IsNormal(x))
        {
            return 0;
        }
        var half = (float)(0.5 * x);
        var y = ApproximateRsqrt(x);
        for (var i = 0; i < 2; i++)
        {
            var square = (float)(y * Force25Bit(y));
            var step = -SingleFma(square, half, -1.5);
            y = (float)(y * step);
        }
        return (float)(y * x);
    }

    /// <summary>The processor's frsqrte: a table's estimate of 1/sqrt(x) for a positive normal x (Dolphin's
    /// Common::ApproximateReciprocalSquareRoot, GPL-2.0-or-later, as the recomp's runtime has it).</summary>
    private static double ApproximateRsqrt(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var mantissa = bits & 0x000FFFFFFFFFFFFFUL;
        var exponent = (long)(bits & 0x7FF0000000000000UL);
        var lsb = (ulong)exponent & 0x0010000000000000UL;
        exponent = (0x3FF0000000000000L - ((exponent - 0x3FE0000000000000L) / 2)) & 0x7FF0000000000000L;
        var i = (uint)((lsb | mantissa) >> 37);
        var (based, dec) = RsqrtTable[i / 2048];
        return BitConverter.UInt64BitsToDouble((ulong)exponent | ((ulong)(based + (dec * (int)(i % 2048))) << 26));
    }

    private static readonly (int Base, int Dec)[] RsqrtTable =
    [
        (0x1a7e800, -0x568), (0x17cb800, -0x4f3), (0x1552800, -0x48d), (0x130c000, -0x435),
        (0x10f2000, -0x3e7), (0x0eff000, -0x3a2), (0x0d2e000, -0x365), (0x0b7c000, -0x32e),
        (0x09e5000, -0x2fc), (0x0867000, -0x2d0), (0x06ff000, -0x2a8), (0x05ab800, -0x283),
        (0x046a000, -0x261), (0x0339800, -0x243), (0x0218800, -0x226), (0x0105800, -0x20b),
        (0x3ffa000, -0x7a4), (0x3c29000, -0x700), (0x38aa000, -0x670), (0x3572000, -0x5f2),
        (0x3279000, -0x584), (0x2fb7000, -0x524), (0x2d26000, -0x4cc), (0x2ac0000, -0x47e),
        (0x2881000, -0x43a), (0x2665000, -0x3fa), (0x2468000, -0x3c2), (0x2287000, -0x38e),
        (0x20c1000, -0x35e), (0x1f12000, -0x332), (0x1d79000, -0x30a), (0x1bf4000, -0x2e6),
    ];

    /// <summary>A double's mantissa rounded to 25 bits, as the processor takes fmuls's second operand (a normal
    /// number's; the square root meets no other).</summary>
    private static double Force25Bit(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        return BitConverter.UInt64BitsToDouble((bits & 0xFFFFFFFFF8000000UL) + (bits & 0x0000000008000000UL));
    }

    /// <summary>a x c + addend, fused and rounded once to single (round to nearest): the double fma's result, its
    /// tie at single precision settled by the exact error.</summary>
    private static float SingleFma(double a, double c, double addend)
    {
        c = Force25Bit(c);
        var result = Math.FusedMultiplyAdd(a, c, addend);
        var bits = BitConverter.DoubleToUInt64Bits(result);
        if ((bits & 0x1FFFFFFFUL) == 0x10000000UL)
        {
            var aPrime = addend - result;
            var bPrime = result + aPrime;
            var error = Math.FusedMultiplyAdd(a, c, aPrime) + (addend - bPrime);
            if (error != 0.0)
            {
                result = BitConverter.UInt64BitsToDouble((error > 0.0) == (result > 0.0) ? bits + 1 : bits - 1);
            }
        }
        return (float)result;
    }
}
