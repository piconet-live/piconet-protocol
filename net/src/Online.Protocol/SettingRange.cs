using System.Globalization;
using System.Text.Json.Serialization;

namespace Online.Protocol;

/// <summary>
/// A lobby's machine-setting rule: a low and a high end, each 0 (acceleration) to 100 (top
/// speed). The server brings each racer's own setting into it, and that value is the one raced (the race block and
/// the replay carry it); the Garage's slider moves inside it while the player is in that lobby. One value is a
/// fixed setting (Quick Play: 100), 0 to 100 each player's own.
/// </summary>
/// <remarks>On the wire, in the welcome's rules: <c>"machineSetting": {"low": 80, "high": 100}</c>. The operator's
/// text form: "80-100", "100" (fixed), or "choose" (0-100).</remarks>
public sealed record SettingRange(int Low, int High)
{
    /// <summary>Each player's own setting: 0 to 100.</summary>
    public static SettingRange Free { get; } = new(0, GameFacts.Limits.SettingMax);

    /// <summary>Quick Play's: every machine at top speed.</summary>
    public static SettingRange QuickPlay { get; } = Only(HostRules.MachineSetting.Default);

    /// <summary>One setting for every racer.</summary>
    public static SettingRange Only(int setting) => new(setting, setting);

    /// <summary>One value: every racer races it.</summary>
    [JsonIgnore]
    public bool IsFixed => Low == High;

    /// <summary>The whole of 0 to 100: each player's own.</summary>
    [JsonIgnore]
    public bool IsFree => Low == 0 && High == GameFacts.Limits.SettingMax;

    /// <summary>Both ends inside 0 to 100, the low one not above the high one.</summary>
    [JsonIgnore]
    public bool IsSound => Low >= 0 && Low <= High && High <= GameFacts.Limits.SettingMax;

    /// <summary>A setting brought into the range: the nearest end when it is outside (never throws, a range that is
    /// not sound included: the high end then wins).</summary>
    public int Clamp(int setting) => Math.Min(Math.Max(setting, Low), High);

    /// <summary>The operator's text form: "100" fixed, "80-100" a range.</summary>
    [JsonIgnore]
    public string Text => IsFixed ? N(Low) : $"{N(Low)}-{N(High)}";

    /// <summary>The operator's text: "80-100", "100", or "choose" (0-100); null: no range (not numbers, an end
    /// outside 0 to 100, the low end above the high one).</summary>
    public static SettingRange? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }
        text = text.Trim();
        if (text == "choose")
        {
            return Free;
        }
        var ends = text.Split('-');
        if (ends.Length is not (1 or 2) || ends.Any(e => !int.TryParse(e.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }
        var low = int.Parse(ends[0].Trim(), CultureInfo.InvariantCulture);
        var range = new SettingRange(low, ends.Length == 1 ? low : int.Parse(ends[1].Trim(), CultureInfo.InvariantCulture));
        return range.IsSound ? range : null;
    }

    /// <summary>What <see cref="Parse"/> takes, as a refusal says it.</summary>
    public static string Taken { get; } = $"0 to {GameFacts.Limits.SettingMax}, a range LOW-HIGH inside it (80-100), or choose";

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
}
