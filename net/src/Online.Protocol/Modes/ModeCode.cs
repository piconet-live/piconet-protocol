using System.Buffers.Binary;

namespace Online.Protocol;

/// <summary>An operation of a package's code as read: its offset, its op and its operands' values in order.</summary>
internal sealed record ModeInstruction(int Offset, ModeOp Op, IReadOnlyList<long> Operands);

/// <summary>The bytecode read operation by operation: each op's operands (protocol/modes.toml's <c>operands</c>,
/// which the generated <see cref="ModeWords.Size"/> gives only as a total), and the check a package's code passes
/// before a game runs it.</summary>
internal static class ModeCode
{
    /// <summary>The operands of the ops that have any, in order: each one's size in bytes and whether it is signed.
    /// <see cref="ModeWords.Size"/> is one plus their sizes (a test holds them together).</summary>
    public static readonly Dictionary<ModeOp, (int Bytes, bool Signed)[]> Operands = new()
    {
        [ModeOp.Push] = [(4, true)],
        [ModeOp.LoadLocal] = [(1, false)],
        [ModeOp.StoreLocal] = [(1, false)],
        [ModeOp.LoadGlobal] = [(2, false)],
        [ModeOp.StoreGlobal] = [(2, false)],
        [ModeOp.LoadSeatVar] = [(2, false)],
        [ModeOp.StoreSeatVar] = [(2, false)],
        [ModeOp.Param] = [(1, false)],
        [ModeOp.SeatRead] = [(1, false)],
        [ModeOp.RaceRead] = [(1, false)],
        [ModeOp.LoadTeamVar] = [(2, false)],
        [ModeOp.StoreTeamVar] = [(2, false)],
        [ModeOp.Jump] = [(4, false)],
        [ModeOp.JumpIfZero] = [(4, false)],
        [ModeOp.JumpIfNotZero] = [(4, false)],
        [ModeOp.Wait] = [(1, false), (1, false), (4, false)],
        [ModeOp.SortBegin] = [(1, false)],
        [ModeOp.SortAdd] = [(1, false), (1, false)],
        [ModeOp.SortRun] = [(1, false), (1, false), (1, false), (1, false)],
        [ModeOp.SortNext] = [(1, false)],
        [ModeOp.Action] = [(1, false)],
    };

    public static bool IsJump(ModeOp op) => op is ModeOp.Jump or ModeOp.JumpIfZero or ModeOp.JumpIfNotZero;

    /// <summary>The code's operations in order; an unknown op or one cut short throws.</summary>
    public static List<ModeInstruction> Decode(byte[] code)
    {
        var list = new List<ModeInstruction>();
        var at = 0;
        while (at < code.Length)
        {
            var op = (ModeOp)code[at];
            var size = ModeWords.Size(op);
            if (size == 0 && op != ModeOp.End || !Enum.IsDefined(op))
            {
                throw new InvalidDataException($"the code holds an unknown operation 0x{code[at]:x2} at {at}");
            }
            if (at + size > code.Length)
            {
                throw new InvalidDataException($"the code ends inside {op} at {at}");
            }
            var operands = new List<long>();
            var next = at + 1;
            foreach (var (bytes, signed) in Operands.GetValueOrDefault(op, []))
            {
                var span = code.AsSpan(next, bytes);
                operands.Add(bytes switch
                {
                    1 => span[0],
                    2 => BinaryPrimitives.ReadUInt16BigEndian(span),
                    _ => signed ? BinaryPrimitives.ReadInt32BigEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span),
                });
                next += bytes;
            }
            list.Add(new ModeInstruction(at, op, operands));
            at += size;
        }
        return list;
    }

    /// <summary>Checks a package's code: whole operations, jumps and rule entries on an operation's start, every
    /// index within its table (a local within its rules' most, a global, a seat word, a parameter, a read, an action,
    /// a sort buffer), and the code ends with an End or a Ret.</summary>
    public static void Check(ModePackage package)
    {
        var instructions = Decode(package.Code);
        var starts = instructions.Select(i => (uint)i.Offset).ToHashSet();
        var locals = package.Rules.Count == 0 ? 0 : package.Rules.Max(r => r.Locals);
        foreach (var rule in package.Rules)
        {
            if (new[] { rule.When, rule.While, rule.Body }.Any(o => o != ModeRule.None && !starts.Contains(o)))
            {
                throw new InvalidDataException($"the rule \"{rule.Label}\" starts inside an operation");
            }
        }
        if (instructions.Count > 0 && instructions[^1].Op is not (ModeOp.End or ModeOp.Ret))
        {
            throw new InvalidDataException("the code does not end with End or Ret");
        }
        foreach (var i in instructions)
        {
            var limit = Limit(i.Op, package, locals);
            if (limit is { } most && i.Operands[0] >= most)
            {
                throw new InvalidDataException($"{i.Op} at {i.Offset} names {i.Operands[0]}, past its table's {most}");
            }
            if (IsJump(i.Op) && !starts.Contains((uint)i.Operands[0]))
            {
                throw new InvalidDataException($"{i.Op} at {i.Offset} jumps inside an operation");
            }
            if (i.Op == ModeOp.Wait && (i.Operands[1] > 2 || (i.Operands[2] != ModeRule.None && !starts.Contains((uint)i.Operands[2]))))
            {
                throw new InvalidDataException($"the wait at {i.Offset} is of no kind, or its condition starts inside an operation");
            }
        }
    }

    /// <summary>The bound of an op's first operand, null for an op whose operand is not an index.</summary>
    private static long? Limit(ModeOp op, ModePackage package, int locals) => op switch
    {
        ModeOp.LoadLocal or ModeOp.StoreLocal => locals,
        ModeOp.LoadGlobal or ModeOp.StoreGlobal => package.Globals.Count,
        ModeOp.LoadSeatVar or ModeOp.StoreSeatVar => package.SeatWords.Count,
        ModeOp.LoadTeamVar or ModeOp.StoreTeamVar => package.TeamWords.Count,
        ModeOp.Wait => Math.Min(locals, ModeWords.WaitLocals) + 1,
        ModeOp.Param => package.Params.Count,
        ModeOp.SeatRead => ModeWords.SeatReads.Count,
        ModeOp.RaceRead => ModeWords.RaceReads.Count,
        ModeOp.Action => ModeWords.Actions.Count,
        ModeOp.SortBegin or ModeOp.SortAdd or ModeOp.SortRun or ModeOp.SortNext => ModeWords.Sorts,
        _ => null,
    };
}
