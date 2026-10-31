namespace Online.Protocol;

/// <summary>What the compiler itself takes from a file, so that any text it is handed costs a bounded stack and a bounded
/// time. The service compiles a host's file on its coordinator's one loop, and .NET cannot catch a stack overflow: a file
/// past these limits is refused with the others' plain "line N: ..." message.
/// <para>These are the compiler's and no part of a package: the game never sees them, and a package's hash does not
/// depend on them.</para></summary>
internal static class ModeLimits
{
    /// <summary>The levels a file may nest, counted as the compiler recurses: a bracket, a block, an <c>else if</c>, a
    /// <c>not</c> or a minus in front, a function's call, a field, each operator of a run (<c>1+1+1</c> is a tree as deep as it
    /// is long) and, while it is written, a sub's body inside its call. The one limit for every pass.</summary>
    public const int MostNesting = 64;

    /// <summary>The tokens all a file's macro uses may expand to: more than a whole file's tokens in the blocks of
    /// its 256 rules. Parsing is the cost: a few characters of <c>lives 1;</c> put in a hundred tokens.</summary>
    public const int MostExpandedTokens = 1 << 16;

    /// <summary>The operations a compile may write, those it takes back to learn a type included: subs are written at
    /// each call, and subs calling others twice each double at every level. Past what any package holds (one of 64 KiB
    /// has some 30,000 operations), each taken back several times. A count, not a clock, so that a file gets the same
    /// answer on the service and in the launcher, on a busy computer too.</summary>
    public const long MostWritten = 1 << 18;

    public static ModeCompileException TooDeep(int line) =>
        new(line, $"this is nested too deep: a mode goes at most {MostNesting} levels (brackets, blocks, a run of operators, subs calling subs); put a part of it in a local");

    /// <summary>The depth of a node over its parts; a node past the limit is refused as it is made.</summary>
    public static int Depth(int line, params IEnumerable<ModeExpr?> parts)
    {
        var depth = 1;
        foreach (var part in parts)
        {
            depth = Math.Max(depth, 1 + (part?.Depth ?? 0));
        }
        return depth > MostNesting ? throw TooDeep(line) : depth;
    }
}
