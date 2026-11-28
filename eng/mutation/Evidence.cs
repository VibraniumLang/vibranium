using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Vibranium.Mutation;

/// <summary>
/// Extra evidence for self accepts of C#-invalid programs: build each selected program with self's own
/// emitter (<c>vib-self build --asan -O1</c>, clang-18 with ASan/LSan) and run it once with no input in a
/// scratch directory. A clang failure means self emitted malformed IR; a sanitizer report means unsafe code.
/// Either marks a safety-relevant key P0 in the report; otherwise it stays P1. A clean run is not proof of safety:
/// most contract breaks (access, readonly) are silent at run time. By default only safety-relevant (and unclassified)
/// defect keys are selected; <c>--all-ids</c> selects every self accept.
/// </summary>
internal static class Evidence
{
    public static int Run(Options options, string root, Oracles oracles, SeverityTable severities)
    {
        string results = Path.GetFullPath(options.Get("asan", ""));
        int perId = options.Number("per-id", 3);
        bool everything = options.Has("all-ids");
        var findings = File.ReadLines(Path.Combine(results, "findings.jsonl")).Select(l => JsonSerializer.Deserialize<Finding>(l)!).ToList();
        var selected = new List<(string Id, string Path, string Source)>();
        foreach (var group in findings.Where(f => f.Self.Status == "accept" && (everything || (SeverityTable.IsDefect(f) && severities.For(f.FirstCsId) != SeverityTable.Other)))
            .GroupBy(f => f.FirstCsId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string repro = Path.Combine(results, "repros", group.Key + ".cs");
            string sidecar = Path.ChangeExtension(repro, ".json");
            if (File.Exists(repro) && File.Exists(sidecar) && JsonDocument.Parse(File.ReadAllText(sidecar)).RootElement.GetProperty("Self").GetProperty("Status").GetString() == "accept")
                selected.Add((group.Key, "repros/" + group.Key + ".cs", "repro"));
            foreach (var finding in group.OrderBy(f => f.SourceBytes).ThenBy(f => f.Index).Take(perId - selected.Count(s => s.Id == group.Key)))
                selected.Add((group.Key, finding.Path, finding.Operator));
        }
        string scratchRoot = Path.Combine(Path.GetTempPath(), "vib-mutation-asan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);
        var evidence = new List<NativeEvidence>();
        try
        {
            int number = 0;
            foreach (var (id, relative, origin) in selected)
            {
                string scratch = Path.Combine(scratchRoot, (++number).ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(scratch);
                string file = Path.Combine(results, relative);
                string program = Path.Combine(scratch, "program");
                string[] buildArguments = ["build", file, "--sysroot", Path.Combine(root, "src"), "--asan", "-O1", "-o", program];
                var build = oracles.SelfIsNative
                    ? Oracles.RunProcess(oracles.SelfDll, buildArguments, scratch, options.Number("build-timeout", 300_000))
                    : Oracles.RunProcess("dotnet", [oracles.SelfDll, .. buildArguments], scratch, options.Number("build-timeout", 300_000), oracles.SelfEnvironment);
                string buildOutcome = build.TimedOut ? "build-timeout" : build.ExitCode == 0 && File.Exists(program) ? "built"
                    : build.Output.Contains("suitable for an entry point", StringComparison.Ordinal) ? "no-entry-point"
                    : build.Output.Contains("clang", StringComparison.Ordinal) || build.Output.Contains(".ll:", StringComparison.Ordinal) ? "clang-rejected-ir"
                    : build.ExitCode == 1 ? "self-build-rejected" : "self-build-crash";
                string runOutcome = "not-run";
                int runExit = -1;
                string excerpt = build.Output;
                string source = File.ReadAllText(file);
                bool risky = source.Contains("TcpListener", StringComparison.Ordinal) || source.Contains("Command(", StringComparison.Ordinal) || source.Contains("Process.", StringComparison.Ordinal);
                if (buildOutcome == "built" && !risky)
                {
                    var run = Oracles.RunProcess(program, [], scratch, options.Number("run-timeout", 10_000),
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["ASAN_OPTIONS"] = "detect_leaks=1:abort_on_error=0:halt_on_error=1:hard_rss_limit_mb=2048:allocator_may_return_null=1" });
                    runExit = run.ExitCode;
                    excerpt = run.Output;
                    runOutcome = run.TimedOut ? "timeout"
                        : run.Output.Contains("ERROR: AddressSanitizer", StringComparison.Ordinal) ? "asan-error"
                        : run.Output.Contains("ERROR: LeakSanitizer", StringComparison.Ordinal) ? "lsan-leak"
                        : run.ExitCode == 101 && run.Output.Contains("panic:", StringComparison.Ordinal) ? "panic-101"
                        : run.ExitCode is < 0 or > 128 ? "signal-" + run.ExitCode
                        : "exit-" + run.ExitCode;
                }
                else if (risky) runOutcome = "skipped-network-or-process";
                evidence.Add(new NativeEvidence(id, relative, origin, buildOutcome, runOutcome, build.ExitCode, runExit, Scrub(excerpt, root)));
                Console.WriteLine($"ASAN {id} {relative}: {buildOutcome} {runOutcome}");
                Directory.Delete(scratch, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
        using (var writer = new StreamWriter(Path.Combine(results, "asan.jsonl")))
            foreach (var item in evidence) writer.WriteLine(JsonSerializer.Serialize(item));
        var text = new StringBuilder("# Native evidence for self-accepted C#-invalid programs\n\n");
        text.AppendLine("Each program was built by self's own emitter (`build --asan -O1`, clang-18 with ASan/LSan) and run once with no input, no arguments and a 10 s limit in a scratch directory. An `asan-error` run or `clang-rejected-ir` build marks a safety-relevant key P0 in report.md. A clean run is not proof of safety: most contract breaks (access control, readonly, conversions) are silent at run time.\n");
        text.AppendLine("| First CS | Programs | Build outcomes | Run outcomes |\n|---|---:|---|---|");
        foreach (var group in evidence.GroupBy(e => e.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string Count(IEnumerable<string> values) => string.Join(", ", values.GroupBy(v => v).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " ×" + g.Count()));
            text.AppendLine($"| {group.Key} | {group.Count()} | {Count(group.Select(e => e.Build))} | {Count(group.Select(e => e.Run))} |");
        }
        File.WriteAllText(Path.Combine(results, "asan.md"), text.ToString());
        Console.WriteLine($"ASAN DONE programs={evidence.Count} ids={evidence.Select(e => e.Id).Distinct().Count()}");
        return 0;
    }

    private static string Scrub(string text, string root)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        text = text.Replace(root.TrimEnd('/'), "<repo>", StringComparison.Ordinal).Replace(Path.GetTempPath().TrimEnd('/'), "<tmp>", StringComparison.Ordinal);
        if (home.Length > 1) text = text.Replace(home, "<home>", StringComparison.Ordinal);
        return text.Length <= 1500 ? text : text[..1500] + "\n[...]";
    }
}
