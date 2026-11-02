namespace Online.Protocol;

/// <summary>The subs a sub calls, for the compiler's refusal of a cycle (the language's section 7: a sub cannot call
/// itself, directly or through another): a walk of its body's statements and expressions.</summary>
internal static class ModeSubCalls
{
    /// <summary>Refuses <paramref name="sub"/> when a chain of its calls comes back to it, at the line of the call that
    /// closes it; <paramref name="find"/> gives the sub a name names (null: not a sub).</summary>
    public static void RefuseCycle(ModeSubItem sub, Func<string, ModeSubItem?> find)
    {
        var path = new List<string>();
        // each sub walked once: subs calling others twice each make 2^n paths, and one walked found no way back
        var walked = new HashSet<string>();
        void Walk(ModeSubItem at)
        {
            walked.Add(at.Name);
            path.Add(at.Name);
            foreach (var call in Calls(at.Body))
            {
                if (find(call.Function) is not { } called)
                {
                    continue;
                }
                if (called.Name == sub.Name)
                {
                    throw new ModeCompileException(call.Line, $"the sub {sub.Name} calls itself ({string.Join(" -> ", path.Append(sub.Name))}): a sub may not, directly or through another");
                }
                if (!walked.Contains(called.Name))
                {
                    Walk(called);
                }
            }
            path.RemoveAt(path.Count - 1);
        }
        Walk(sub);
    }

    private static IEnumerable<ModeCall> Calls(IEnumerable<ModeStatement> statements) => statements.SelectMany(Calls);

    private static IEnumerable<ModeCall> Calls(ModeStatement statement) => statement switch
    {
        ModeLocal l => Calls(l.Value),
        ModeAssign a => Calls(a.Target).Concat(Calls(a.Value)),
        ModeIf i => Calls(i.Condition).Concat(Calls(i.Then)).Concat(Calls(i.Else ?? [])),
        ModeForEach f => Calls(f.Set).Concat(Calls(f.Where)).Concat(f.Keys.SelectMany(k => Calls(k.Key))).Concat(Calls(f.Body)),
        ModeRepeat r => Calls(r.Count).Concat(Calls(r.Body)),
        ModeActionStatement a => a.Arguments.SelectMany(Calls),
        ModeWait w => Calls(w.Steps).Concat(Calls(w.Condition)),
        ModeCallStatement c => Calls(c.Call),
        ModeReturn r => Calls(r.Value),
        _ => [],
    };

    private static IEnumerable<ModeCall> Calls(ModeExpr? expr) => expr switch
    {
        ModeCall c => c.Arguments.SelectMany(Calls).Prepend(c),
        ModeField f => Calls(f.Target),
        ModeUnary u => Calls(u.Operand),
        ModeBinary b => Calls(b.Left).Concat(Calls(b.Right)),
        ModeSetCall s => Calls(s.Value).Concat(Calls(s.Where)).Concat(Calls(s.Set)).Concat(Calls(s.Index))
            .Concat((s.Keys ?? []).SelectMany(k => Calls(k.Key))),
        _ => [],
    };
}
