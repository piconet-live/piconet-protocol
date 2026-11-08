namespace Online.Protocol;

/// <summary>How a mode sets its racers' machines (protocol/modes.toml, setup words 58 to 63): one machine for every
/// seat, one drawn for the race, a draw a seat, or a list by seat, by team or by the lobby's last race's place.</summary>
public enum ModeMachineWay
{
    /// <summary><c>machine "Name"</c>: every seat races it.</summary>
    Fixed,

    /// <summary><c>machine random</c>: one machine drawn for the race, every seat races it.</summary>
    Random,

    /// <summary><c>machine random each</c>: every seat its own draw.</summary>
    RandomEach,

    /// <summary><c>machine by seat [...]</c>.</summary>
    BySeat,

    /// <summary><c>machine by team [...]</c>.</summary>
    ByTeam,

    /// <summary><c>machine by place [...]</c>.</summary>
    ByPlace,
}

/// <summary>The machines a mode sets: its way and its entries (a machine's id 0 to 40, <see cref="ModeMachines.Own"/>
/// or <see cref="ModeMachines.Random"/>; <c>machine "Name"</c> one entry, the two random ways none).</summary>
public sealed record ModeMachineRule(ModeMachineWay Way, IReadOnlyList<int> Entries);

/// <summary>Each seat's machine as a mode sets it, decided once by the coordinator when the race's machine choice opens
/// and carried in the match start's <c>setup.seats[].machine</c>: every game, every replay, the lobby and the results
/// name the machine raced, and no game draws anything. A set machine is the stock one; null is the racer's own (for a
/// CPU, its usual draw).</summary>
public static class ModeMachines
{
    /// <summary>An entry that leaves the seat's machine its own: the racer's choice (a custom one too unless the mode
    /// says <c>customs off</c>), a CPU's usual draw.</summary>
    public const int Own = -1;

    /// <summary>An entry drawn from the machines the mode allows, a draw of its own.</summary>
    public const int Random = -2;

    /// <summary>Each seat's machine, by seat; null: its own.</summary>
    /// <param name="rule">The mode's way (<see cref="ModeTerms.MachineRule"/>).</param>
    /// <param name="seats">The race's seats.</param>
    /// <param name="teams">Each seat's team (the match start's <c>setup.mode.teams</c>); null: no teams. A seat past its
    /// end is a team of its own, its number.</param>
    /// <param name="places">Each seat's place in the lobby's last race, 0 its winner; null (or a null entry): not
    /// ranked there.</param>
    /// <param name="allowed">The machines a draw takes from: the mode's <c>machines</c>, else all.</param>
    /// <param name="draw">The coordinator's generator: a number from 0 to n - 1.</param>
    public static IReadOnlyList<int?> Of(ModeMachineRule rule, int seats, IReadOnlyList<int>? teams,
        IReadOnlyList<int?>? places, IReadOnlyList<int> allowed, Func<int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(draw);
        if (allowed.Count == 0)
        {
            throw new ArgumentException("a draw needs a machine to draw from", nameof(allowed));
        }
        int? Entry(int? at)
        {
            if (at is not { } i || i < 0 || i >= rule.Entries.Count)
            {
                return null;
            }
            return rule.Entries[i] switch
            {
                Own => null,
                Random => allowed[draw(allowed.Count)],
                var machine => machine,
            };
        }
        var one = rule.Way switch
        {
            ModeMachineWay.Fixed => rule.Entries[0],
            ModeMachineWay.Random => allowed[draw(allowed.Count)],
            _ => (int?)null,
        };
        var machines = new int?[seats];
        for (var s = 0; s < seats; s++)
        {
            machines[s] = rule.Way switch
            {
                ModeMachineWay.Fixed or ModeMachineWay.Random => one,
                ModeMachineWay.RandomEach => allowed[draw(allowed.Count)],
                ModeMachineWay.BySeat => Entry(s),
                ModeMachineWay.ByTeam => Entry(teams is not null && s < teams.Count ? teams[s] : s),
                ModeMachineWay.ByPlace => Entry(places is not null && s < places.Count ? places[s] : null),
                _ => null,
            };
        }
        return machines;
    }
}
