namespace Online.Protocol;

// A mode file as the parser reads it (ModeParser): the tree the compiler checks and turns into bytecode. Nothing here
// is typed yet; every node keeps its line, for the messages.

internal abstract record ModeExpr(int Line);

/// <summary>A literal, already a cell: an int, a real's bits, a bool (0 or 1), a seat (-1 none). A time is an int of
/// steps (<see cref="Time"/>); <see cref="Text"/> is the literal as the file wrote it.</summary>
internal sealed record ModeLiteral(int Line, ModeType Type, int Bits, string Text, bool Time = false) : ModeExpr(Line);

internal sealed record ModeName(int Line, string Name) : ModeExpr(Line);

/// <summary><c>target.field</c>: a read of the race (<c>race.steps</c>) or of a seat, or a seat's variable.</summary>
internal sealed record ModeField(int Line, ModeExpr Target, string Field) : ModeExpr(Line);

/// <summary><c>-x</c> or <c>not x</c>.</summary>
internal sealed record ModeUnary(int Line, string Operator, ModeExpr Operand) : ModeExpr(Line);

internal sealed record ModeBinary(int Line, string Operator, ModeExpr Left, ModeExpr Right) : ModeExpr(Line);

/// <summary>A built-in function: <c>min(a, b)</c>, <c>floor(x)</c>.</summary>
internal sealed record ModeCall(int Line, string Function, IReadOnlyList<ModeExpr> Arguments) : ModeExpr(Line);

/// <summary>A set helper over a set of seats: <c>count(S where c)</c>, <c>first(S where c by key)</c>, <c>last</c> and
/// <c>nth(S where c by key, i)</c> (no <see cref="Value"/>: <see cref="Set"/> is S), or <c>min(e for each NAME in S
/// where c)</c> and <c>max</c>, <c>sum</c>, <c>any</c>, <c>all</c> (S null: the race's seats). <see cref="Bound"/> is
/// the seat variable the value, the condition and the keys see (<c>seat</c> unless named); <see cref="Index"/> is
/// nth's i.</summary>
internal sealed record ModeSetCall(int Line, string Helper, ModeExpr? Value, string Bound, ModeExpr? Where,
    ModeExpr? Set = null, IReadOnlyList<ModeSortKey>? Keys = null, ModeExpr? Index = null) : ModeExpr(Line);

internal abstract record ModeStatement(int Line);

/// <summary>A local: <c>real d = e;</c>.</summary>
internal sealed record ModeLocal(int Line, ModeType Type, string Name, ModeExpr? Value) : ModeStatement(Line);

/// <summary><c>target op value;</c>, op one of <c>= += -= *= /=</c>.</summary>
internal sealed record ModeAssign(int Line, ModeExpr Target, string Operator, ModeExpr Value) : ModeStatement(Line);

internal sealed record ModeIf(int Line, ModeExpr Condition, IReadOnlyList<ModeStatement> Then, IReadOnlyList<ModeStatement>? Else)
    : ModeStatement(Line);

internal sealed record ModeSortKey(ModeExpr Key, bool Descending);

/// <summary><c>for each seat [where c] [by key [desc], ...] { }</c>, or <c>for each NAME in S ...</c>
/// (<see cref="Set"/> S, null for the race's seats).</summary>
internal sealed record ModeForEach(int Line, string Bound, ModeExpr? Where, IReadOnlyList<ModeSortKey> Keys,
    IReadOnlyList<ModeStatement> Body, ModeExpr? Set = null) : ModeStatement(Line);

internal sealed record ModeRepeat(int Line, ModeExpr Count, IReadOnlyList<ModeStatement> Body) : ModeStatement(Line);

/// <summary>An action word with its arguments: <c>eliminate s;</c>.</summary>
internal sealed record ModeActionStatement(int Line, string Action, IReadOnlyList<ModeExpr> Arguments) : ModeStatement(Line);

/// <summary><c>wait t;</c>, <c>wait t abort when c;</c> or <c>wait t restart when c;</c> (<see cref="Kind"/> "",
/// "abort" or "restart").</summary>
internal sealed record ModeWait(int Line, ModeExpr Steps, string Kind, ModeExpr? Condition) : ModeStatement(Line);

/// <summary><c>call name(args);</c>: a sub, its value (if it gives one) dropped.</summary>
internal sealed record ModeCallStatement(int Line, ModeCall Call) : ModeStatement(Line);

/// <summary><c>return [e];</c>, in a sub.</summary>
internal sealed record ModeReturn(int Line, ModeExpr? Value) : ModeStatement(Line);

internal abstract record ModeItem(int Line);

/// <summary>The header; <see cref="ApiLine"/> is the line of its api.</summary>
internal sealed record ModeHeaderItem(int Line, string Name, int? Api, int ApiLine, string Badge, string About) : ModeItem(Line);

/// <summary>A parameter. <see cref="Least"/>, <see cref="Most"/>, <see cref="Step"/>, <see cref="Default"/> are
/// literals (null when left out); a choice's default is the name it names, in <see cref="DefaultChoice"/>.</summary>
internal sealed record ModeParamItem(int Line, string Name, string Label, ModeParamKind Kind, ModeLiteral? Least,
    ModeLiteral? Most, ModeLiteral? Step, ModeLiteral? Default, IReadOnlyList<string> Choices, string? DefaultChoice)
    : ModeItem(Line);

/// <summary>A setup argument: its name (empty for the unnamed one) and its value, a literal or a parameter's name.</summary>
internal sealed record ModeSetupArgument(int Line, string Name, ModeExpr Value);

/// <summary>A row of a setup word's table (<c>laps by course</c>): the course as players write it and its value; or a
/// name of a word that takes names (<c>stats</c>, <c>machines</c>): the name, and no value.</summary>
internal sealed record ModeSetupRow(int Line, string Key, ModeExpr? Value);

internal sealed record ModeSetupItem(int Line, string Word, IReadOnlyList<ModeSetupArgument> Arguments,
    IReadOnlyList<ModeSetupRow>? Table = null) : ModeItem(Line);

internal sealed record ModeConstItem(int Line, ModeType Type, string Name, ModeExpr Value) : ModeItem(Line);

/// <summary>Where a variable is kept (the language's section 4): one for the race, one a seat, one a team.</summary>
internal enum ModeScope
{
    Global,
    Seat,
    Team,
}

/// <summary><c>global TYPE NAME [= const];</c>, <c>seat TYPE NAME [= const];</c> or <c>team TYPE NAME [= const];</c>.</summary>
internal sealed record ModeVariableItem(int Line, ModeScope Scope, ModeType Type, string Name, ModeExpr? Value) : ModeItem(Line);

internal sealed record ModeSubParam(ModeType Type, string Name);

/// <summary><c>sub [TYPE] NAME(TYPE NAME, ...) { }</c>: <see cref="Returns"/> null for a sub that gives no value.
/// Its code is written where it is called (a sub is never called through the bytecode).</summary>
internal sealed record ModeSubItem(int Line, ModeType? Returns, string Name, IReadOnlyList<ModeSubParam> Params,
    IReadOnlyList<ModeStatement> Body) : ModeItem(Line);

/// <summary>A library macro's argument of kind <c>time</c> as it was given, for the compiler's check (at least one
/// step, or a delay parameter that is never less): <c>every(10s)</c>'s 10s.</summary>
internal sealed record ModeTimeArgument(int Line, string Macro, ModeExpr Value) : ModeItem(Line);

/// <summary>A rule. <see cref="Bound"/> is the name its event binds (null: none), <see cref="Second"/> the second
/// name (<c>ko(v, by)</c>'s <c>by</c>; null: none).</summary>
internal sealed record ModeRuleItem(int Line, string Label, string Event, string? Bound, IReadOnlyList<ModeExpr> Whens,
    IReadOnlyList<ModeExpr> Whiles, IReadOnlyList<ModeStatement> Body, string? Second = null) : ModeItem(Line);
