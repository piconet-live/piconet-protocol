using System.Globalization;

namespace Online.Protocol;

/// <summary>The rules a mode's setup words change, in plain lines for the people who vote on it ("Damage 150%", "No
/// boost"). The file's order; what a parameter gives at its default, as a vote knows no other. The laps have their own
/// place on a card, and a solo mode's words and the results' look change no rule of a race.</summary>
public static class ModeRuleLines
{
    /// <summary>The lines of <paramref name="mode"/>; empty when it changes none of them.</summary>
    public static IReadOnlyList<string> Of(ModePackage mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        int Value(ModeSetupValue v) => v.FromParam ? mode.Params[v.Value].Default : v.Value;
        var lines = new List<string>();
        foreach (var word in mode.Setups)
        {
            var line = Line(word.Id, word.Arguments.Select(Value).ToList());
            if (line is not null)
            {
                lines.Add(line);
            }
        }
        // (a word given an optional parameter at its named value states nothing: no line)
        bool Unset(ModeSetupValue v) => v.FromParam && mode.Params[v.Value].StatesNothing(mode.Params[v.Value].Default);
        lines.AddRange(mode.LobbyWords.Where(w => !w.Values.Any(Unset)).Select(w => Lobby(w, [.. w.Values.Select(Value)])).OfType<string>());
        return lines;
    }

    private static string? Line(ModeSetup word, List<int> a) => word switch
    {
        ModeSetup.Cpus => a[0] == 0 ? "CPU racers Off" : a[0] == 1 ? null : "CPU racers Always",   // (1: the vote, Quick Play's own way)
        ModeSetup.PedalsLocked => "Pedals locked",
        ModeSetup.Boost => a[0] == 0 ? "No boost" : null,
        ModeSetup.FinishLimit => a[0] == 0 ? "Finish limit Off" : $"Finish limit {Say(a[0])} s",
        ModeSetup.MineRegen => a[0] == 0 ? "Mines respawn Off" : $"Mines respawn {Say(a[0])} ms",
        ModeSetup.CpuLevel => $"CPU level {(a[0] is >= 0 and < 4 ? CpuLevels[a[0]] : Say(a[0]))}",
        ModeSetup.Restore => a[0] == 0 ? "Restore Off" : "Restore On",
        ModeSetup.Contact => a[0] == 0 ? "No contact" : null,
        ModeSetup.ClockLimit => $"Time limit {Clock(a[0])}",
        ModeSetup.RollingStart => $"Rolling start at {Say(BitConverter.Int32BitsToSingle(a[0]))} km/h",
        ModeSetup.StartGap => a[0] == 0 ? "Everyone starts on one spot" : $"Starts {Say(BitConverter.Int32BitsToSingle(a[0]))} m apart",
        ModeSetup.StartGapEven => "Starts spread round the lap",
        ModeSetup.RaceWaitsEveryone => "Waits for every racer",
        ModeSetup.Damage => Percent("Damage", a[0]),
        ModeSetup.Knockback => Percent("Knockback", a[0]),
        ModeSetup.WallDamage => Percent("Wall damage", a[0]),
        ModeSetup.WallBounce => Percent("Wall bounce", a[0]),
        ModeSetup.SpinAttacks => a[0] == 0 ? "No spin attacks" : null,
        ModeSetup.SideAttacks => a[0] == 0 ? "No side attacks" : null,
        ModeSetup.SpinImmunity => a[0] != 0 ? "Spin immunity" : null,
        ModeSetup.DamageCap => a[0] == 0 ? "No damage cap" : null,
        ModeSetup.TimeBonus => $"Time bonus {Say(a[0])} seconds",
        ModeSetup.PointsTable => "Points by place",
        ModeSetup.TimeGaps => "Time gaps to other racers",
        ModeSetup.HudClean => "No CHECK sign or name tags",
        ModeSetup.HudLayoutAx => "AX HUD layout",
        _ => null,
    };

    private static string? Lobby(ModeLobbyWord word, List<int> a) => word.Id switch
    {
        ModeSetup.Course => "One course",
        ModeSetup.Racers => a is [_, var most] ? $"Up to {Say(most)} racers" : null,
        ModeSetup.Machines => "Set machines",
        ModeSetup.MachineSetting => a is [var low, var high] && low != high ? $"Machine setting {Say(low)}-{Say(high)}"
            : a is [var one, ..] ? $"Machine setting {Say(one)}" : "Machine setting",
        ModeSetup.Stats => "Stats",
        ModeSetup.BoostStart => a is [var on] ? (on == 0 ? "Boost from the start Off" : "Boost from the start On") : null,
        ModeSetup.Teams => "Teams",
        ModeSetup.Customs => a is [0] ? "No custom machines" : null,
        ModeSetup.Machine => a is [var machine] && GameMachines.Has(machine) ? $"Everyone races {GameMachines.Names[machine]}" : "One machine for everyone",
        ModeSetup.MachineRandom => "Everyone races one random machine",
        ModeSetup.MachineRandomEach => "Random machines",
        ModeSetup.MachineBySeat => "Machines by seat",
        ModeSetup.MachineByTeam => "Machines by team",
        ModeSetup.MachineByPlace => "Machines by last race's place",
        _ => Line(word.Id, a),   // (a game word the coordinator carries: its line as in the package's setup)
    };

    /// <summary>The CPU levels by name, as the launcher's rows say them.</summary>
    public static IReadOnlyList<string> CpuLevels { get; } = ["Novice", "Standard", "Expert", "Master"];

    private static string Percent(string name, int bits) => $"{name} {Say(BitConverter.Int32BitsToSingle(bits) * 100)}%";

    private static string Say(float value) => ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);

    private static string Say(int value) => value.ToString(CultureInfo.InvariantCulture);


    /// <summary>Steps (60 a second) as minutes and seconds.</summary>
    private static string Clock(int steps) => string.Create(CultureInfo.InvariantCulture, $"{steps / 3600}:{steps / 60 % 60:00}");
}
