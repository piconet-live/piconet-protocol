using System.Diagnostics.CodeAnalysis;

namespace Online.Protocol;

/// <summary>A value's type in a game mode (the language's section 4): what a read gives, what an action or a setup
/// word takes. On the interpreter's stack each is one 32-bit cell: an int, a real's binary32 bits, a bool (0 or 1), a
/// seat (0 to 29, or -1 none), a set of seats (bit i: seat i); but a vec, which is three, x then y then z.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The language's own type names " +
    "(int, real, bool, seat), which protocol/modes.toml and the generated ModeWords write as these members.")]
public enum ModeType
{
    Int,
    Real,
    Bool,
    Seat,
    Seats,
    Vec,
}

/// <summary>An argument of a setup word (protocol/modes.toml's <c>[[setup]]</c>): its name as a mode file writes it
/// (empty for the word's one unnamed argument, as <c>laps 3</c>), its type, its range, and the value it takes when the
/// file leaves it out (null: it must be written).</summary>
public sealed record ModeArgument(string Name, ModeType Type, double Least, double Most, double? Default);
