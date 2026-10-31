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
        lines.AddRange(mode.LobbyWords.Select(Lobby).OfType<string>());
        return lines;
    }

    private static string? Line(ModeSetup word, List<int> a) => word switch
    {
        ModeSetup.Cpus => a[0] == 0 ? "No CPUs" : null,
        ModeSetup.PedalsLocked => "Pedals locked",
        ModeSetup.Boost => a[0] == 0 ? "No boost" : null,
        ModeSetup.FinishLimit => $"{Say(a[0])} seconds to finish after the winner",
        ModeSetup.MineRegen => "Mines return",
        ModeSetup.CpuLevel => $"CPU level {Say(a[0])}",
        ModeSetup.Restore => a[0] == 0 ? "No restore" : "Restore",
        ModeSetup.Contact => a[0] == 0 ? "No contact" : null,
        ModeSetup.ClockLimit => $"Time limit {Clock(a[0])}",
        ModeSetup.RollingStart => $"Rolling start at {Say(BitConverter.Int32BitsToSingle(a[0]))} km/h",
        ModeSetup.RaceWaitsEveryone => "Waits for every racer",
        ModeSetup.Damage => Percent("Damage", a[0]),
        ModeSetup.Knockback => Percent("Knockback", a[0]),
        ModeSetup.WallDamage => Percent("Wall damage", a[0]),
        ModeSetup.WallBounce => Percent("Wall bounce", a[0]),
        ModeSetup.SpinAttacks => a[0] == 0 ? "No spin attacks" : null,
        ModeSetup.SideAttacks => a[0] == 0 ? "No side attacks" : null,
        ModeSetup.SpinImmunity => a[0] != 0 ? "Spin immunity" : null,
        ModeSetup.TimeBonus => $"Time bonus {Say(a[0])} seconds",
        ModeSetup.PointsTable => "Points by place",
        _ => null,
    };

    private static string? Lobby(ModeLobbyWord word) => word.Id switch
    {
        ModeSetup.Course => "One course",
        ModeSetup.Racers => word.Values is [_, var most] ? $"Up to {Say(most.Value)} racers" : null,
        ModeSetup.Machines => "Set machines",
        ModeSetup.MachineSetting => "Set machine setting",
        ModeSetup.Stats => "Stat pack",
        ModeSetup.BoostStart => word.Values is [{ Value: 0 }] ? "No boost on the first lap" : null,
        ModeSetup.Teams => "Teams",
        ModeSetup.Customs => word.Values is [{ Value: 0 }] ? "No custom machines" : null,
        _ => null,
    };

    private static string Percent(string name, int bits) => $"{name} {Say(BitConverter.Int32BitsToSingle(bits) * 100)}%";

    private static string Say(float value) => ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);

    private static string Say(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Steps (60 a second) as minutes and seconds.</summary>
    private static string Clock(int steps) => string.Create(CultureInfo.InvariantCulture, $"{steps / 3600}:{steps / 60 % 60:00}");
}
