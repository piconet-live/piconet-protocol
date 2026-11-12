using System.Globalization;

namespace Online.Protocol;

/// <summary>The mode api's levels (protocol/modes.toml's [levels]): a mode file names the game version it was written
/// for (<c>api 0.6.1;</c>), which gives a rule set, the number its package carries; a shipped rule set is frozen, so a
/// file compiles to the same thing in every later build. A version with no rule change of its own has the rule set
/// before it. <c>api 1;</c> is 0.6.0, as its files wrote it.</summary>
public static class ModeLevels
{
    /// <summary>The game version a rule set arrived in ("0.6.1"), as a mode file names it.</summary>
    public static string Version(int ruleSet) =>
        ModeWords.RuleSets.FirstOrDefault(r => r.Number == ruleSet).Version ?? ruleSet.ToString(CultureInfo.InvariantCulture);

    /// <summary>The code of the server's word (an <see cref="ErrorMessage"/>) to a launcher whose hello names a lower
    /// rule set than a lobby's mode is of (<see cref="Hello.Modes"/>): it stays in the lobby, does not race and is sent
    /// no match of it.</summary>
    public const string UpdateCode = "mode_needs_update";

    /// <summary>That word's text, shown as it is: the game version the lobby's mode needs.</summary>
    public static string UpdateText(int ruleSet) =>
        $"This lobby's game mode needs Piconet {Version(ruleSet)}. Update the launcher to race it.";

    /// <summary>The rule set of what a header's <c>api</c> names: a game version this build knows, or 1. False with
    /// the reason, said to the file's author.</summary>
    public static bool TryRuleSet(string api, out int ruleSet, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(api);
        (ruleSet, refusal) = (0, "");
        var first = ModeWords.RuleSets[0];
        if (!api.Contains('.', StringComparison.Ordinal))
        {
            // (the one bare number: 0.6.0's files and replays wrote api 1)
            if (api == "1")
            {
                ruleSet = first.Number;
                return true;
            }
            refusal = $"api {api} is not a level: a mode file names the game version it was written for (api {ModeWords.ApiVersion};), and api 1 is {first.Version}";
            return false;
        }
        if (Parts(api) is not { } version)
        {
            refusal = $"api {api} is not a game version (api {ModeWords.ApiVersion};)";
            return false;
        }
        if (Compare(version, Parts(ModeWords.ApiVersion)!) > 0)
        {
            refusal = $"api {api} is newer than this build, which knows up to api {ModeWords.ApiVersion}: update to use this file";
            return false;
        }
        if (Compare(version, Parts(first.Version)!) < 0)
        {
            refusal = $"api {api} is from before game modes: the first is api {first.Version}";
            return false;
        }
        ruleSet = ModeWords.RuleSets.Last(r => Compare(Parts(r.Version)!, version) <= 0).Number;
        return true;
    }

    /// <summary>The first thing in a package that its own rule set does not have (a word or a value that arrived in a
    /// later one), and the rule set it needs; null: all of it is the package's own.</summary>
    public static (string What, int Since)? Newer(ModePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        foreach (var word in package.Setups.Select(s => (s.Id, s.Arguments))
                     .Concat(package.LobbyWords.Select(w => (w.Id, Arguments: w.Values))))
        {
            var name = ModeWords.Setups.First(s => s.Id == word.Id).Name;
            // (a word the compiler writes for `say` or `pause` is no word of the file's: the action below is named)
            if (ModeWords.SetupsSince.GetValueOrDefault(word.Id, 1) > package.Api && word.Id is not (ModeSetup.SayTexts or ModeSetup.Pauses))
            {
                return (name, ModeWords.SetupsSince[word.Id]);
            }
            // a value that came after its word: written, or within what the parameter it names may give
            foreach (var ((id, argument, value), since) in ModeWords.SetupValuesSince)
            {
                if (id == word.Id && since > package.Api && argument < word.Arguments.Count && Gives(package, word.Arguments[argument], value))
                {
                    return ($"{name} {Said(word.Id, argument, value)}", since);
                }
            }
        }
        foreach (var rule in package.Rules)
        {
            if (ModeWords.EventsSince.TryGetValue(rule.Event, out var since) && since > package.Api)
            {
                return ("the event " + ModeWords.Events.First(e => e.Id == rule.Event).Name, since);
            }
        }
        foreach (var i in ModeCode.Decode(package.Code))
        {
            var (what, since) = i.Op switch
            {
                _ when ModeWords.OpsSince.TryGetValue(i.Op, out var op) => ("the operation " + i.Op, op),
                ModeOp.SeatRead when ModeWords.SeatReadsSince.TryGetValue((ModeSeatRead)i.Operands[0], out var read) =>
                    ("seat." + ModeWords.SeatReads.First(r => (long)r.Id == i.Operands[0]).Name, read),
                ModeOp.RaceRead when ModeWords.RaceReadsSince.TryGetValue((ModeRaceRead)i.Operands[0], out var read) =>
                    ("race." + ModeWords.RaceReads.First(r => (long)r.Id == i.Operands[0]).Name, read),
                ModeOp.Action when ModeWords.ActionsSince.TryGetValue((ModeAction)i.Operands[0], out var action) =>
                    (ModeWords.Actions.First(a => (long)a.Id == i.Operands[0]).Name, action),
                _ => ("", 1),
            };
            if (since > package.Api)
            {
                return (what, since);
            }
        }
        return null;
    }

    private static bool Gives(ModePackage package, ModeSetupValue given, int value) => given.FromParam
        ? given.Value < package.Params.Count && package.Params[given.Value] is var p && p.Least <= value && value <= p.Most
        : given.Value == value;

    private static string Said(ModeSetup word, int argument, int value) =>
        ModeWords.Setups.First(s => s.Id == word).Args[argument].Always == value ? "always" : value.ToString(CultureInfo.InvariantCulture);

    private static int[]? Parts(string version)
    {
        var parts = version.Split('.');
        var numbers = new int[3];
        return parts.Length == 3 && parts.Select((p, i) => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])).All(ok => ok)
            ? numbers : null;
    }

    private static int Compare(int[] a, int[] b) => a.Zip(b, (x, y) => x.CompareTo(y)).FirstOrDefault(c => c != 0);
}
