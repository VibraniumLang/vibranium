using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>
/// AST-level delta reduction: greedily deletes list children (members, statements, sections, arms,
/// labels, arguments, elements), drops optional children (else, guards, initializers) and replaces
/// nodes by simpler ones (literals, sub-expressions, branch bodies), keeping an edit only when the
/// program still produces the same disagreement signature.
/// </summary>
/// <param name="stop">Polled before every reduction test; when it returns true the reducer keeps its best result so far
/// (campaign mode: a few minutes after the time budget, so draining in-flight reductions stays bounded).</param>
internal sealed class Reducer(Engine engine, int budget, Func<bool>? stop = null)
{
    public sealed record Result(ProgramNode Min, Outcome Outcome, int Tests, int SizeBefore, int SizeAfter);

    private bool Stopped => stop is not null && stop();

    public Result Reduce(ProgramNode start, Outcome startOutcome, string signature, ExecPlan plan)
    {
        var cur = start;
        var curOut = startOutcome;
        string curSrc = Printer.Print(cur);
        var seen = new HashSet<string>(StringComparer.Ordinal) { curSrc };
        int tests = 0;
        int index = 0;
        bool progressThisPass = false;
        while (tests < budget && !Stopped)
        {
            var cands = Candidates(cur).ToList();
            if (index >= cands.Count)
            {
                if (!progressThisPass)
                {
                    break;
                }
                progressThisPass = false;
                index = 0;
                continue;
            }
            bool accepted = false;
            for (; index < cands.Count && tests < budget && !Stopped; index++)
            {
                var (path, repl) = cands[index];
                ProgramNode next;
                try
                {
                    next = (ProgramNode)TreeOps.Replace(cur, path, repl);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                catch (InvalidCastException)
                {
                    continue;
                }
                string src;
                try
                {
                    src = Printer.Print(next);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                if (!seen.Add(src))
                {
                    continue;
                }
                tests++;
                var o = engine.Run(src, Tagger.Collect(next), plan);
                if (KeepsValidity(startOutcome, o) && o.Disagreements.Any(d => d.Signature == signature))
                {
                    cur = next;
                    curOut = o;
                    accepted = true;
                    progressThisPass = true;
                    break;
                }
            }
            if (!accepted && index >= cands.Count && !progressThisPass)
            {
                break;
            }
        }
        return new Result(cur, curOut, tests, Tagger.Size(start), Tagger.Size(cur));
    }

    /// <summary>
    /// A reduction step must not move the program to a worse C# validity class: a valid program must stay
    /// warning-free (TreatWarningsAsErrors would otherwise reject it, e.g. CS8321 after deleting the only call),
    /// a warning-only program may only lose warning IDs, and an invalid program must stay invalid without new warning IDs.
    /// </summary>
    internal static bool KeepsValidity(Outcome start, Outcome next)
    {
        switch (start.Validity)
        {
            case "valid":
                return next.Validity == "valid";
            case "warn-only":
                if (next.Validity == "invalid")
                {
                    return false;
                }
                var allowed = start.Sdk!.Warnings.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
                return next.Sdk!.Warnings.All(w => allowed.Contains(w.Id));
            default:
            {
                // still invalid, and no new warning kinds (e.g. CS8321 after deleting the only call hides the real root)
                if (next.Validity != "invalid")
                {
                    return false;
                }
                var allowedW = start.Sdk!.Warnings.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
                return next.Sdk!.Warnings.All(w => allowedW.Contains(w.Id));
            }
        }
    }

    private static IEnumerable<(int[] Path, Node? Replacement)> Candidates(ProgramNode p)
    {
        foreach (var (path, node) in TreeOps.Walk(p))
        {
            if (path.Length == 0)
            {
                continue;
            }
            var parent = TreeOps.At(p, path[..^1]);
            if (parent.IsList && parent.Kids.Count(k => k is not null) > parent.MinKids && !(parent is SwitchStmt && path[^1] == 0) && !(parent is SwitchSection && path[^1] == 0))
            {
                yield return (path, null);
            }
            if (parent.OptionalKids.Contains(path[^1]))
            {
                yield return (path, null);
            }
            foreach (var s in node.Simplify())
            {
                yield return (path, s.DeepClone());
            }
        }
    }
}
