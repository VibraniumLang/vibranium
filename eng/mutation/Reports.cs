using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Vibranium.Mutation;

/// <summary>
/// Writes statistics.json, report.md and defect-table.md. Severity classes and keys are re-read from the current tables,
/// and the growth curve is recomputed from the findings, so <c>--report</c> can re-render an older campaign under the
/// current rules. The counts are a measured baseline, not a release certification.
/// </summary>
internal static class Reports
{
    private static bool Accept(Finding f) => f.Self.Status == "accept";

    private static bool Failure(Finding f) => f.Self.Status is not ("accept" or "reject");

    private static bool Probe(Finding f) => f.Operator == "baseline-probe";

    private static bool Genuine(Finding f) => SeverityTable.IsInvalid(f);

    public static void Write(string output, IReadOnlyList<Finding> findings, Statistics statistics, SeverityTable severities)
    {
        Files.WriteJson(Path.Combine(output, "statistics.json"), statistics);
        var groups = findings.GroupBy(f => f.FirstCsId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var mutants = findings.Where(f => !Probe(f)).ToArray();
        var genuine = findings.Where(Genuine).ToArray();
        var defects = findings.Where(SeverityTable.IsDefect).ToArray();
        var warningsOnly = findings.Where(SeverityTable.IsWarningsOnly).ToArray();
        var subset = findings.Where(SeverityTable.IsSubsetRejection).ToArray();
        var defectKeys = defects.Select(f => f.FirstCsId).Distinct().ToArray();
        string Risk(string id) => severities.For(id);
        var evidence = File.Exists(Path.Combine(output, "asan.jsonl"))
            ? File.ReadLines(Path.Combine(output, "asan.jsonl")).Select(l => JsonSerializer.Deserialize<NativeEvidence>(l)!).ToLookup(e => e.Id)
            : Array.Empty<NativeEvidence>().ToLookup(e => e.Id);
        // P0: a safety-relevant accept whose asan-mode build ran into a memory error (ASan report) or was emitted as invalid IR.
        bool MemoryUnsafe(string id) => evidence[id].Any(e => e.Run is "asan-error" || e.Build is "clang-rejected-ir");
        string Priority(string id) => Risk(id) == SeverityTable.Other ? "P2" : MemoryUnsafe(id) ? "P0" : "P1";
        string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        var text = new StringBuilder("# Invalid-program mutation oracle: report\n\n");
        text.AppendLine("Contract: a C#-invalid program (a genuine C# error, or a warning that Vibranium.App.props promotes explicitly) must be rejected by every frontend; each accept of one is a defect. First keys bucket defects for triage; severity classes set priority (safety-relevant: P1, P0 with asan-mode evidence of a memory error; other: P2, a known issue). These counts are a measured baseline, not a release certification.\n");
        text.AppendLine($"* Kept {statistics.KeptMutants} SDK-invalid mutants and {statistics.BaselineProbes} unmodified invalid probes, from {statistics.SeedsValid} SDK-valid, self-accepted bases ({string.Join(", ", statistics.ValidSeedsByGroup.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}"))}). Findings in this file: {findings.Count}.");
        text.AppendLine($"* Candidate pool {statistics.CandidatePool}; tried {statistics.Candidates}; SDK-valid mutants discarded {statistics.ValidMutantsDiscarded}; duplicates {statistics.DuplicateMutants}.");
        text.AppendLine($"* C#-invalid programs: {genuine.Length} under {genuine.Select(f => f.FirstCsId).Distinct().Count()} first keys. **Self defects (accepts): {defects.Length} programs under {defectKeys.Length} first keys** (safety-relevant {defectKeys.Count(k => Risk(k) == SeverityTable.Safety)}, other {defectKeys.Count(k => Risk(k) == SeverityTable.Other)}, unclassified {defectKeys.Count(k => Risk(k) == SeverityTable.Unclassified)}). Self crashes, timeouts or out-of-memory: {findings.Count(Failure)}.");
        text.AppendLine($"* Warning-only parity programs (only blanket-promoted warnings, so valid C#; reported, not gated): {warningsOnly.Length}; self accepts {warningsOnly.Count(Accept)}, Stage0 accepts {warningsOnly.Count(f => f.Stage0.Status == "accept")}, self and Stage0 disagree on {warningsOnly.Count(f => Accept(f) != (f.Stage0.Status == "accept"))}.");
        text.AppendLine($"* Subset rejections (self rejected a C#-invalid program that also contains a C10–C20 cut construct): {subset.Length}.");
        text.AppendLine($"* Stage0 accepts of a C#-invalid program: {genuine.Count(f => f.Stage0.Status == "accept")}. Analyzer-frontend accepts of one: {genuine.Count(f => f.Analyzer.Status == "accept")} (SDK + analyzer, so it cannot accept one).");
        text.AppendLine($"* Infrastructure failures: base self={statistics.BaseSelfFailures}, repro={statistics.ReproFailures}, stage0={findings.Count(f => f.Stage0.Status is not ("accept" or "reject"))}, analyzer={findings.Count(f => f.Analyzer.Status is not ("accept" or "reject"))}. Elapsed {statistics.ElapsedSeconds:F0} s. Peak memory: tool working set {statistics.PeakWorkingSetMb} MB, cgroup {statistics.CgroupPeakMb} MB (-1: not readable).\n");

        text.AppendLine("## Growth curve (mutants only, in the seeded candidate order, recomputed under the current rules)\n");
        text.AppendLine("| Mutants kept | Distinct first keys | First keys with a self defect | …of them safety-relevant or unclassified | Self defects |\n|---:|---:|---:|---:|---:|");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var defectSeen = new HashSet<string>(StringComparer.Ordinal);
        int defectCount = 0;
        int step = Math.Max(1, mutants.Length / 10);
        for (int i = 0; i < mutants.Length; i++)
        {
            var f = mutants[i];
            seen.Add(f.FirstCsId);
            if (SeverityTable.IsDefect(f)) { defectSeen.Add(f.FirstCsId); defectCount++; }
            if ((i + 1) % step == 0 || i == mutants.Length - 1)
                text.AppendLine($"| {i + 1} | {seen.Count} | {defectSeen.Count} | {defectSeen.Count(k => Risk(k) != SeverityTable.Other)} | {defectCount} |");
        }
        if (mutants.Length > 0)
        {
            int third = mutants.Length * 2 / 3;
            var early = mutants.Take(third).Where(SeverityTable.IsDefect).Select(f => f.FirstCsId).ToHashSet(StringComparer.Ordinal);
            var late = mutants.Skip(third).Where(SeverityTable.IsDefect).Select(f => f.FirstCsId).Distinct().Where(id => !early.Contains(id)).Order(StringComparer.Ordinal).ToArray();
            text.AppendLine($"\nDefect keys first seen in the last third (after mutant {third}): {(late.Length == 0 ? "none (stable)" : string.Join(", ", late))}.");
        }

        string Repro(string id) => File.Exists(Path.Combine(output, "repros", id + ".cs")) ? "repros/" + id + ".cs" : "—";
        string CutOnly(string id)
        {
            var accepts = groups[id].Where(SeverityTable.IsDefect).ToArray();
            if (accepts.Length == 0) return "—";
            return accepts.Any(f => f.Cuts.Length == 0) ? "no" : "only via " + string.Join("/", accepts.SelectMany(f => f.Cuts).Distinct().Order(StringComparer.Ordinal));
        }
        var table = new StringBuilder();
        string Native(string id)
        {
            var items = evidence[id].ToArray();
            if (items.Length == 0) return "—";
            return string.Join(", ", items.Select(e => e.Build == "built" ? e.Run : e.Build).GroupBy(v => v).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + (g.Count() > 1 ? " ×" + g.Count() : "")));
        }
        table.AppendLine("| First key | Severity | Priority | Self defects (mutants/probes) | C#-invalid programs kept | Self crashes | Likely self component | Reached only through a cut? | Native evidence (asan mode) | Minimal repro | Severity rationale |\n|---|---|---|---:|---:|---:|---|---|---|---|---|");
        foreach (var id in groups.Keys.Where(id => groups[id].Any(f => SeverityTable.IsDefect(f) || (Genuine(f) && Failure(f))))
            .OrderBy(id => Risk(id) == SeverityTable.Other ? 1 : 0).ThenByDescending(id => groups[id].Count(SeverityTable.IsDefect)).ThenBy(id => id, StringComparer.Ordinal))
        {
            var g = groups[id];
            table.AppendLine($"| {id} | {Risk(id)} | {Priority(id)} | {N(g.Count(f => SeverityTable.IsDefect(f) && !Probe(f)))}/{N(g.Count(f => SeverityTable.IsDefect(f) && Probe(f)))} | {N(g.Count(Genuine))} | {N(g.Count(f => Genuine(f) && Failure(f)))} | {severities.Component(id)} | {CutOnly(id)} | {Native(id)} | {Repro(id)} | {severities.Reason(id)} |");
        }
        File.WriteAllText(Path.Combine(output, "defect-table.md"), table.ToString());
        text.AppendLine("\n## Self defects and crashes by first key (safety-relevant and unclassified first)\n");
        text.Append(table);

        text.AppendLine("\n## First keys with a C#-invalid program that self always rejected\n");
        var rejected = groups.Keys.Where(id => groups[id].Any(Genuine) && !groups[id].Any(f => SeverityTable.IsDefect(f) || Failure(f))).Order(StringComparer.Ordinal).ToArray();
        text.AppendLine(string.Join(", ", rejected.Select(id => $"{id} ({N(groups[id].Count(Genuine))}{(Risk(id) == SeverityTable.Other ? ", other" : "")})")));

        text.AppendLine($"\n## Warning-only parity programs by first key ({warningsOnly.Length}; blanket-promoted warnings only; not gated)\n");
        text.AppendLine("| First key | Kind | Programs | Self accepts | Stage0 accepts | Self and Stage0 disagree |\n|---|---|---:|---:|---:|---:|");
        foreach (var g in warningsOnly.GroupBy(f => (f.FirstCsId, f.ErrorKind)).OrderByDescending(g => g.Count()))
            text.AppendLine($"| {g.Key.FirstCsId} | {g.Key.ErrorKind} | {g.Count()} | {g.Count(Accept)} | {g.Count(f => f.Stage0.Status == "accept")} | {g.Count(f => Accept(f) != (f.Stage0.Status == "accept"))} |");

        text.AppendLine($"\n## Subset rejections by first key ({subset.Length})\n");
        text.AppendLine("Self rejected these C#-invalid programs, but each also contains a construct that the v0.2 subset cuts. The rejection may come from the cut, so it does not show that self checks the C# error.\n");
        text.AppendLine("| First key | Programs | Cut forms present |\n|---|---:|---|");
        foreach (var g in subset.GroupBy(f => f.FirstCsId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            text.AppendLine($"| {g.Key} | {g.Count()} | {string.Join(", ", g.SelectMany(f => f.Cuts).Distinct().Order(StringComparer.Ordinal))} |");

        var stage0 = findings.Where(f => f.Stage0.Status == "accept" || f.Analyzer.Status == "accept").ToArray();
        text.AppendLine($"\n## Stage0 and Analyzer accepts ({stage0.Length})\n");
        if (stage0.Length > 0)
        {
            text.AppendLine("| Frontend | First key | Error kind | Programs | All error IDs | Example |\n|---|---|---|---:|---|---|");
            foreach (var g in stage0.GroupBy(f => (Frontend: f.Stage0.Status == "accept" ? "Stage0" : "Analyzer", f.FirstCsId, f.ErrorKind)).OrderBy(g => g.Key.ErrorKind, StringComparer.Ordinal).ThenBy(g => g.Key.FirstCsId, StringComparer.Ordinal))
                text.AppendLine($"| {g.Key.Frontend} | {g.Key.FirstCsId} | {g.Key.ErrorKind} | {g.Count()} | {string.Join(", ", g.SelectMany(f => f.Errors.Select(e => e.Id + (e.Promotion == "error" ? "" : "(" + e.Promotion + ")"))).Distinct().Order(StringComparer.Ordinal))} | {g.OrderBy(f => f.SourceBytes).First().Path} |");
        }
        text.AppendLine("\n## Mutation operator families\n");
        text.AppendLine("| Family | Kept | Self defects | Distinct first keys |\n|---|---:|---:|---:|");
        foreach (var g in mutants.GroupBy(f => f.Operator.Split(':')[0]).OrderByDescending(g => g.Count()))
            text.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(SeverityTable.IsDefect)} | {g.Select(f => f.FirstCsId).Distinct().Count()} |");
        text.AppendLine("\n## Reading this report\n");
        text.AppendLine("* A program counts once, under its first SDK error (Roslyn 5.12.0 in process, .NET 11 reference pack, the app's implicit usings and bootstrap, Nullable=enable, WarningLevel 11, TreatWarningsAsErrors and the WarningsAsErrors list of Vibranium.App.props). The first key is triage only: any genuine error, or any explicitly promoted warning, makes an accept a defect.");
        text.AppendLine("* Error kind: `error` means at least one diagnostic whose default severity is Error (a genuine C# error). `explicit-warning` means only warnings, at least one of them listed in Vibranium.App.props: also C#-invalid (the explicit Vibranium warning policy). `blanket-warning` means only warnings, all promoted by TreatWarningsAsErrors alone: warning-only parity, valid C#, never gated.");
        text.AppendLine("* The campaign and the gate ask self only to `check`. The separate asan mode builds selected self accepts with self's own emitter (`build --asan -O1`, clang-18 ASan/LSan) and runs each once: a memory error or invalid IR marks the key P0. A clean run is not proof of safety.");
        text.AppendLine("* \"Reached only through a cut\" means every defect for that key contains a C10–C20 construct.");
        text.AppendLine("* A crash, a timeout, or an exit that is neither 0 nor 1 with a diagnostic is a failure, never an accept or a reject.");
        File.WriteAllText(Path.Combine(output, "report.md"), text.ToString());
    }
}
