using System.Text.RegularExpressions;
using Online.Protocol;
using TestSupport;

namespace Protocol.Tests;

/// <summary>MODE-REFERENCE.md, the mode language's reference, against the language: every <c>```fzmode</c> block
/// compiles with no error and no warning, the Knockout quote is the file as it ships, and the reference's tables and
/// the vocabulary name the same words. Each word section's tables (the <c>##</c> headings below) are read row by row:
/// every form a row's first cell writes in backticks names a word of that section's kind (<see cref="ModeWords"/>,
/// which protocol/modes.toml generates, and the standard library std.fzmode), and every word a file can write has a
/// row, so a word added, renamed or retired fails here until the reference follows.</summary>
public sealed class ModeReferenceTests
{
    private static readonly string Reference = File.ReadAllText(RepositoryFolder.Protocol("MODE-REFERENCE.md")).ReplaceLineEndings("\n");

    /// <summary>The setup words only the compiler writes: a file that writes one is refused ("… is the compiler's
    /// own", ModeCompiler), so the reference leaves them out.</summary>
    private static readonly HashSet<string> CompilersOwn = ["tag_texts", "stats_page", "time_gaps", "say_texts", "pauses"];

    /// <summary>The kinds of word a section of the reference names, by its heading.</summary>
    private static readonly Dictionary<string, string[]> Sections = new()
    {
        ["Setup words"] = ["setup"],
        ["Events"] = ["event"],
        ["Seat reads"] = ["seat_read"],
        ["Machine stats"] = ["seat_read", "action"],
        ["Race reads"] = ["race_read"],
        ["Actions"] = ["action"],
        ["Display words"] = ["action"],
        ["Display values"] = ["choice"],
        ["Library"] = ["library"],
    };

    /// <summary>The reference's code blocks of one kind (the word after the opening fence, exactly), each with the
    /// line it starts on.</summary>
    private static List<(int Line, string Text)> Blocks(string kind)
    {
        var blocks = new List<(int, string)>();
        var lines = Reference.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }
            var opened = lines[i][3..].Trim();
            var start = i + 1;
            do
            {
                i++;
            }
            while (i < lines.Length && lines[i] != "```");
            Assert.True(i < lines.Length, $"MODE-REFERENCE.md line {start}: a code block is never closed");
            if (opened == kind)
            {
                blocks.Add((start, string.Join('\n', lines[start..i]) + "\n"));
            }
        }
        return blocks;
    }

    public static TheoryData<int> Files() => [.. Blocks("fzmode").Select(b => b.Line)];

    [Theory]
    [MemberData(nameof(Files))]
    public void AFileCompilesWithNoWarning(int line)
    {
        var result = ModeCompiler.Compile(Blocks("fzmode").Single(b => b.Line == line).Text);
        Assert.True(result.Succeeded, $"MODE-REFERENCE.md line {line}: {string.Join(" ", result.Errors)}");
        Assert.True(result.Warnings.Count == 0, $"MODE-REFERENCE.md line {line}: {string.Join(" ", result.Warnings)}");
    }

    [Fact]
    public void KnockoutIsQuotedAsItShips()
    {
        var text = File.ReadAllText(RepositoryFolder.Protocol("protocol", "quickplay", "knockout.fzmode")).ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
        Assert.Contains(text, Blocks("fzmode").Select(b => b.Text));
    }

    /// <summary>The forms each table row's first cell writes, under each <c>##</c> heading: (heading, line, form),
    /// the form without its closing <c>;</c>.</summary>
    private static List<(string Section, int Line, string Form)> Forms()
    {
        var forms = new List<(string, int, string)>();
        var lines = Reference.Split('\n');
        var section = "";
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = line[3..].Trim();
            }
            else if (!fenced && line.StartsWith('|') && !line.StartsWith("|---", StringComparison.Ordinal)
                && !(i + 1 < lines.Length && lines[i + 1].StartsWith("|---", StringComparison.Ordinal)))
            {
                var cell = line.Split('|')[1];
                foreach (Match code in Regex.Matches(cell, "`([^`]+)`"))
                {
                    forms.Add((section, i + 1, code.Groups[1].Value.Trim().TrimEnd(';').TrimEnd()));
                }
            }
        }
        return forms;
    }

    /// <summary>Every word a file can write, by kind, with the test of whether a form writes it.</summary>
    private static IEnumerable<(string Kind, string Name, Func<string, bool> Writes)> Words()
    {
        foreach (var s in ModeWords.Setups.Where(s => !CompilersOwn.Contains(s.Name)))
        {
            yield return ("setup", s.Name, f => f == s.Name || f.StartsWith(s.Name + " ", StringComparison.Ordinal) || f.StartsWith(s.Name + "{", StringComparison.Ordinal));
        }
        foreach (var e in ModeWords.Events)
        {
            yield return ("event", e.Name, f => f == e.Name || f.StartsWith(e.Name + "(", StringComparison.Ordinal));
        }
        foreach (var r in ModeWords.SeatReads)
        {
            yield return ("seat_read", r.Name, f => f == "seat." + r.Name);
        }
        foreach (var r in ModeWords.RaceReads)
        {
            yield return ("race_read", r.Name, f => f == "race." + r.Name);
        }
        foreach (var a in ModeWords.Actions)
        {
            yield return ("action", a.Name, ActionForm(a.Name));
        }
        foreach (var value in ModeWords.Choices.Values.SelectMany(v => v))
        {
            yield return ("choice", value, f => f == value);
        }
        foreach (var name in Library())
        {
            yield return ("library", name, f => Regex.IsMatch(f, $@"^{Regex.Escape(name)}(\(| |$)"));
        }
    }

    /// <summary>An action's form: its seat (or team) goes after the first part of a name of more than one, as
    /// <c>respawns s off</c> and <c>team t no contact</c> write it; a name of one part is followed by its values.</summary>
    private static Func<string, bool> ActionForm(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length == 1)
        {
            return f => f == name || f.StartsWith(name + " ", StringComparison.Ordinal);
        }
        var pattern = new Regex($@"^{Regex.Escape(parts[0])}( [^ ]+)? {string.Join(" ", parts.Skip(1).Select(Regex.Escape))}( .*)?$");
        return pattern.IsMatch;
    }

    /// <summary>The standard library's macros and subs, from its text as the build carries it.</summary>
    private static List<string> Library()
    {
        var text = File.ReadAllText(RepositoryFolder.Protocol("net", "src", "Online.Protocol", "Modes", "std.fzmode"));
        return [.. Regex.Matches(text, @"^(?:macro|sub(?: \w+)?) (\w+)\(", RegexOptions.Multiline).Select(m => m.Groups[1].Value)];
    }

    /// <summary>The word a form names in its section: of the section's kinds, the longest name the form writes
    /// (<c>boost s off</c> writes <c>boost off</c>, not <c>boost</c>), one a kind.</summary>
    private static List<(string Kind, string Name)> Named(string section, string form) =>
        [.. Sections[section].Select(kind => Words().Where(w => w.Kind == kind && w.Writes(form)).OrderByDescending(w => w.Name.Length).FirstOrDefault())
            .Where(w => w.Name is not null).Select(w => (w.Kind, w.Name))];

    [Fact]
    public void EveryWordAFileCanWriteHasARow()
    {
        var named = Forms().Where(f => Sections.ContainsKey(f.Section)).SelectMany(f => Named(f.Section, f.Form)).ToHashSet();
        var missing = Words().Select(w => (w.Kind, w.Name)).Distinct().Where(w => !named.Contains(w)).ToList();
        Assert.True(missing.Count == 0, "MODE-REFERENCE.md has no row for " + string.Join(", ", missing.Select(m => $"{m.Kind} '{m.Name}'")));
    }

    [Fact]
    public void EveryFormARowWritesIsAWordOfTheLanguage()
    {
        var forms = Forms().Where(f => Sections.ContainsKey(f.Section)).ToList();
        Assert.True(forms.Count > 250, $"MODE-REFERENCE.md: only {forms.Count} forms in the word sections");
        Assert.Equal(Sections.Keys.Order(), forms.Select(f => f.Section).Distinct().Order());
        var unknown = forms.Where(f => Named(f.Section, f.Form).Count == 0).ToList();
        Assert.True(unknown.Count == 0, "MODE-REFERENCE.md names no word of api 1: " + string.Join("; ", unknown.Select(u => $"line {u.Line} `{u.Form}`")));
    }

    /// <summary>Calls of numbers, of a set and of a value for each seat (the set helpers'): a name the compiler has not
    /// got is refused as such, or the call is no call at all, in every one; a name it has is refused, if at all, for
    /// its arguments.</summary>
    private static readonly string[] Probes = ["1.5, 2.5, 3.5, 4.5, 5.5", "seats", "seats, 1", "seat.speed for each seat"];

    /// <summary>The functions and set helpers, which no table of the vocabulary holds: the compiler knows each name
    /// (a call with the wrong arguments is refused for them, never as a name it has not got).</summary>
    [Fact]
    public void EveryFunctionTheReferenceNamesIsTheCompilers()
    {
        static bool Unknown(string name) => Probes.All(arguments =>
            ModeCompiler.Compile($"mode \"T\" {{ api 1; }}\nrule {{ event every step; do {{ real r = {name}({arguments}); }} }}\n").Errors
                .Any(e => e.Contains($"'{name}' is not", StringComparison.Ordinal) || e.Contains("is expected here", StringComparison.Ordinal)));
        Assert.True(Unknown("nosuch"));
        var names = Forms().Where(f => f.Section == "Operators and functions")
            .Select(f => Regex.Match(f.Form, @"^(\w+)\(")).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(names.Count >= 18, $"MODE-REFERENCE.md: only {names.Count} functions");
        var unknown = names.Where(Unknown).ToList();
        Assert.True(unknown.Count == 0, "MODE-REFERENCE.md names functions the compiler has not got: " + string.Join(", ", unknown));
    }

    /// <summary>The course and machine names, as the compiler takes them in <c>courses</c> and <c>machines</c>.</summary>
    [Fact]
    public void TheCourseAndMachineNamesAreTheCompilers()
    {
        var section = Reference[Reference.IndexOf("\n## Courses and machines\n", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 1, StringComparison.Ordinal)];
        var names = section.Split('\n').Where(l => l.StartsWith('|'))
            .SelectMany(l => Regex.Matches(l, "`([^`]+)`").Select(m => m.Groups[1].Value)).ToList();
        var courses = names.Where(n => n.Contains(':', StringComparison.Ordinal)).ToList();
        var machines = names.Except(courses).ToList();
        Assert.Equal(30, courses.Count);
        Assert.Equal(41, machines.Count);
        var list = (IEnumerable<string> items) => string.Join(", ", items.Select(n => $"\"{n}\""));
        var result = ModeCompiler.Compile($"mode \"T\" {{ api 1; }}\ncourses [{list(courses)}];\nmachines [{list(machines)}];\n");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
    }
}
