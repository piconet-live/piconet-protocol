using System.Globalization;
using System.Text;

namespace Online.Protocol;

/// <summary>A package as text, for the tests and for review: every section, each rule's entries, and the code
/// operation by operation with its offset (the words by their names, a push as its int and its bits as a real).
/// The vectors' <c>.fzmp.txt</c> files are its listings, so a change here is a change of them.</summary>
public static class ModeDisassembler
{
    public static string List(byte[] package)
    {
        var mode = ModePackage.Read(package);
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append('\n');
        Line(Invariant($"FZMP format {ModePackage.Format} api {mode.Api}, {package.Length} bytes, hash {ModePackage.Hash(package)}"));
        Line(Invariant($"HEAD name {Quote(mode.Name)} badge {Quote(mode.Badge)} about {Quote(mode.About)}"));
        Line(Invariant($"PARM {mode.Params.Count}"));
        for (var i = 0; i < mode.Params.Count; i++)
        {
            Line(Invariant($"  {i} {Param(mode.Params[i])}"));
        }
        Line(Invariant($"SETP {mode.Setups.Count}"));
        foreach (var word in mode.Setups)
        {
            Line("  " + Setup(word, mode));
        }
        Line(Invariant($"VARS globals {mode.Globals.Count}, seat words {mode.SeatWords.Count}")
            + (mode.TeamWords.Count > 0 ? Invariant($", team words {mode.TeamWords.Count}") : ""));
        for (var i = 0; i < mode.Globals.Count; i++)
        {
            Line(Invariant($"  global {i} = {Cell(mode.Globals[i])}"));
        }
        for (var i = 0; i < mode.SeatWords.Count; i++)
        {
            Line(Invariant($"  seat {i} = {Cell(mode.SeatWords[i])}"));
        }
        for (var i = 0; i < mode.TeamWords.Count; i++)
        {
            Line(Invariant($"  team {i} = {Cell(mode.TeamWords[i])}"));
        }
        Line(Invariant($"RULE {mode.Rules.Count}"));
        for (var i = 0; i < mode.Rules.Count; i++)
        {
            var r = mode.Rules[i];
            var e = ModeWords.Events.First(x => x.Id == r.Event).Name;
            Line(Invariant($"  {i} {Quote(r.Label)} event {e}, locals {r.Locals}, when {Entry(r.When)}, while {Entry(r.While)}, body {Entry(r.Body)}"));
        }
        Line(Invariant($"CODE {mode.Code.Length} bytes"));
        var entries = mode.Rules.SelectMany((r, i) => new[] { (r.When, $"rule {i} when"), (r.While, $"rule {i} while"), (r.Body, $"rule {i} body") })
            .Where(x => x.Item1 != ModeRule.None).ToLookup(x => x.Item1, x => x.Item2);
        foreach (var instruction in ModeCode.Decode(mode.Code))
        {
            foreach (var entry in entries[(uint)instruction.Offset])
            {
                Line($"  ; {entry}");
            }
            Line(Invariant($"  {instruction.Offset:x4}  {Instruction(instruction, mode)}"));
        }
        Line(Invariant($"SRC  {Encoding.UTF8.GetByteCount(mode.Source)} bytes"));
        if (mode.Board.Count > 0)
        {
            Line(Invariant($"BORD {mode.Board.Count}"));
            foreach (var line in mode.Board)
            {
                Line(Invariant($"  {line.Scope} {line.Type} word {line.Word} {Quote(line.Label)}"));
            }
        }
        return text.ToString();
    }

    /// <summary>The sizes in bytes of an op's operands, in order, as the disassembler and the package's check read
    /// them (their sum and the op's byte are <see cref="ModeWords.Size"/>).</summary>
    public static IReadOnlyList<int> OperandBytes(ModeOp op) => [.. ModeCode.Operands.GetValueOrDefault(op, []).Select(o => o.Bytes)];

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Entry(uint offset) => offset == ModeRule.None ? "none" : Invariant($"{offset:x4}");

    /// <summary>A cell as its int, and as a real when its bits are not a small int's.</summary>
    private static string Cell(int cell) =>
        cell is > -65536 and < 65536 ? cell.ToString(CultureInfo.InvariantCulture)
        : Invariant($"{cell} (0x{cell:x8}, real {ModeOperators.Real(cell).ToString("R", CultureInfo.InvariantCulture)})");

    private static string Typed(ModeType type, int cell) => type == ModeType.Real
        ? ModeOperators.Real(cell).ToString("R", CultureInfo.InvariantCulture) + Invariant($" (0x{cell:x8})")
        : cell.ToString(CultureInfo.InvariantCulture);

    private static string Param(ModeParam p)
    {
        var kind = p.Kind.ToString().ToLowerInvariant();
        var type = p.Type == ModeType.Real ? "real" : "int";
        var choices = p.Choices.Count == 0 ? "" : " [" + string.Join(", ", p.Choices.Select(Quote)) + "]";
        return Invariant($"{p.Name} {Quote(p.Label)} {kind} {type} {Typed(p.Type, p.Least)}..{Typed(p.Type, p.Most)} step {Typed(p.Type, p.Step)} default {Typed(p.Type, p.Default)}{choices}");
    }

    private static string Setup(ModeSetupWord word, ModePackage mode)
    {
        var table = ModeWords.Setups.First(s => s.Id == word.Id);
        var parts = new List<string> { table.Name };
        for (var i = table.Args.Count; i + 1 < word.Arguments.Count; i += 2)  // a table's rows (laps by course)
        {
            var course = CourseTable.Courses.FirstOrDefault(c => c.Course == word.Arguments[i].Value);
            parts.Add(Invariant($"course {word.Arguments[i].Value} ({course.Place}: {course.Track}) {word.Arguments[i + 1].Value}"));
        }
        for (var i = 0; i < table.Args.Count; i++)
        {
            var a = word.Arguments[i];
            var name = table.Args[i].Name.Length > 0 ? table.Args[i].Name + " " : "";
            parts.Add(name + (a.FromParam ? Invariant($"param {a.Value} ({mode.Params[a.Value].Name})") : Typed(table.Args[i].Type, a.Value)));
        }
        return string.Join(' ', parts);
    }

    private static string Instruction(ModeInstruction i, ModePackage mode)
    {
        var operand = i.Operands.Count == 0 ? 0 : i.Operands[0];
        var detail = i.Op switch
        {
            ModeOp.Push => Cell((int)operand),
            ModeOp.SeatRead => ModeWords.SeatReads[(int)operand].Name,
            ModeOp.RaceRead => ModeWords.RaceReads[(int)operand].Name,
            ModeOp.Action => ModeWords.Actions[(int)operand].Name,
            ModeOp.Param => Invariant($"{operand} ({mode.Params[(int)operand].Name})"),
            ModeOp.Jump or ModeOp.JumpIfZero or ModeOp.JumpIfNotZero => Invariant($"{operand:x4}"),
            ModeOp.SortRun => Invariant($"buffer {i.Operands[0]} keys {i.Operands[1]} desc 0x{i.Operands[2]:x2} reals 0x{i.Operands[3]:x2}"),
            ModeOp.SortAdd => Invariant($"buffer {i.Operands[0]} keys {i.Operands[1]}"),
            ModeOp.Wait => Invariant($"locals {i.Operands[0]}") + (i.Operands[1] switch
            {
                0 => "",
                var kind => Invariant($" {(kind == 1 ? "abort" : "restart")} when {i.Operands[2]:x4}"),
            }),
            _ => string.Join(' ', i.Operands.Select(o => o.ToString(CultureInfo.InvariantCulture))),
        };
        return detail.Length == 0 ? i.Op.ToString() : i.Op + " " + detail;
    }
}
