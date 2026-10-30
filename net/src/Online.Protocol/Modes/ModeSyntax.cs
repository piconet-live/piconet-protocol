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

/// <summary>A set helper over the race's seats: <c>count(seats where c)</c> (no <see cref="Value"/>), or
/// <c>min(e for each NAME where c)</c> and <c>max</c>, <c>sum</c>, <c>any</c>, <c>all</c>. <see cref="Bound"/> is the
/// seat variable the value and the condition see (<c>seat</c> unless named).</summary>
internal sealed record ModeSetCall(int Line, string Helper, ModeExpr? Value, string Bound, ModeExpr? Where) : ModeExpr(Line);

internal abstract record ModeStatement(int Line);

/// <summary>A local: <c>real d = e;</c>.</summary>
internal sealed record ModeLocal(int Line, ModeType Type, string Name, ModeExpr? Value) : ModeStatement(Line);

/// <summary><c>target op value;</c>, op one of <c>= += -= *= /=</c>.</summary>
internal sealed record ModeAssign(int Line, ModeExpr Target, string Operator, ModeExpr Value) : ModeStatement(Line);

internal sealed record ModeIf(int Line, ModeExpr Condition, IReadOnlyList<ModeStatement> Then, IReadOnlyList<ModeStatement>? Else)
    : ModeStatement(Line);

internal sealed record ModeSortKey(ModeExpr Key, bool Descending);

/// <summary><c>for each seat [where c] [by key [desc], ...] { }</c>, or <c>for each NAME in seats ...</c>.</summary>
internal sealed record ModeForEach(int Line, string Bound, ModeExpr? Where, IReadOnlyList<ModeSortKey> Keys,
    IReadOnlyList<ModeStatement> Body) : ModeStatement(Line);

internal sealed record ModeRepeat(int Line, ModeExpr Count, IReadOnlyList<ModeStatement> Body) : ModeStatement(Line);

/// <summary>An action word with its arguments: <c>eliminate s;</c>.</summary>
internal sealed record ModeActionStatement(int Line, string Action, IReadOnlyList<ModeExpr> Arguments) : ModeStatement(Line);

internal abstract record ModeItem(int Line);

internal sealed record ModeHeaderItem(int Line, string Name, int? Api, string Badge, string About) : ModeItem(Line);

/// <summary>A parameter. <see cref="Least"/>, <see cref="Most"/>, <see cref="Step"/>, <see cref="Default"/> are
/// literals (null when left out); a choice's default is the name it names, in <see cref="DefaultChoice"/>.</summary>
internal sealed record ModeParamItem(int Line, string Name, string Label, ModeParamKind Kind, ModeLiteral? Least,
    ModeLiteral? Most, ModeLiteral? Step, ModeLiteral? Default, IReadOnlyList<string> Choices, string? DefaultChoice)
    : ModeItem(Line);

/// <summary>A setup argument: its name (empty for the unnamed one) and its value, a literal or a parameter's name.</summary>
internal sealed record ModeSetupArgument(int Line, string Name, ModeExpr Value);

internal sealed record ModeSetupItem(int Line, string Word, IReadOnlyList<ModeSetupArgument> Arguments) : ModeItem(Line);

internal sealed record ModeConstItem(int Line, ModeType Type, string Name, ModeExpr Value) : ModeItem(Line);

/// <summary><c>global TYPE NAME [= const];</c> or <c>seat TYPE NAME [= const];</c>.</summary>
internal sealed record ModeVariableItem(int Line, bool PerSeat, ModeType Type, string Name, ModeExpr? Value) : ModeItem(Line);

/// <summary>A rule. <see cref="Bound"/> is the name its event binds (null: none); <see cref="EveryTime"/> is the time of
/// the <c>every(T)</c> it was expanded from, which the compiler checks.</summary>
internal sealed record ModeRuleItem(int Line, string Label, string Event, string? Bound, IReadOnlyList<ModeExpr> Whens,
    IReadOnlyList<ModeExpr> Whiles, IReadOnlyList<ModeStatement> Body, ModeExpr? EveryTime = null) : ModeItem(Line);
