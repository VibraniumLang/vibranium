using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vibranium.Mutation;

var options = new Options(args);
if (options.Has("help"))
{
    Console.WriteLine("""
        vib-mutation: invalid-program mutation oracle for Vibranium (see docs/MUTATION.md). Use run-mutation.sh: every build and run goes through heavy.
        Contract: a C#-invalid program (a genuine C# error, or a warning Vibranium.App.props promotes explicitly) must be
        rejected by every frontend; accepting one is a defect. First IDs bucket defects for triage; severity classes set
        priority (safety-relevant: P1, or P0 when asan mode shows a memory error; other: P2, a known issue).
          campaign:  --root REPO --output NEW_DIR [--probes DIR[:DIR...]] [--limit 500] [--candidates N] [--seeds-per-group 12]
                     [--per-operator 2] [--seed 20261117] [--workers 3] [--reduce 32] [--reduce-rejected 8] [--max-bytes 16000] [--timeout 15000]
                     [--probe-repairs 120]
          ci:        --ci [--output NEW_DIR] [--known-open FILE]   fixed seed and budget plus eng/mutation/corpus as probes; exit 1 when a
                                                                   frontend accepts a C#-invalid program whose first key is not known-open
          report:    --report RESULTS_DIR                          re-render report.md with the current severity tables and rules
          check:     --check FILE                                  SDK errors, cut forms and the self/Stage0/Analyzer verdicts for one program
          evidence:  --asan RESULTS_DIR [--per-id 3] [--all-ids]   build self's defect accepts with `self build --asan -O1` and run
                                                                   them once (default: safety-relevant keys; --all-ids: every accept)
          self-test: --self-test
        Common: [--self vib-self.dll|NATIVE] [--reference-pack DIR] [--vibranium-references DIR] [--severity-safety FILE]
                [--severity-other FILE] [--self-heap-mb 1024] [--self-native-mb 4096]
        Self: a .dll runs as `dotnet DLL check`, with a managed-heap hard limit (--self-heap-mb). Any other path is a native
        self-hosted compiler (for example bootstrap's vib-stage2), run directly as `PATH check` under an address-space limit
        (--self-native-mb, through prlimit). The SDK oracle references Vibranium.Core.dll and Vibranium.Std.dll from
        --vibranium-references (default: the managed self's directory, or src/vib-self/bin/Release/net11.0 for a native self).
        Memory: oracle work runs in chunks of 8 x workers programs; compilations are dropped after each program, a full GC
        drains finalizers (Stage0's native reference images) after every chunk, and a compacting one runs every 10 chunks.
        Exit codes: 0 clean, 1 defect (a frontend accepted a C#-invalid program), 2 infrastructure failure.
        """);
    return 0;
}
string root = Path.GetFullPath(options.Get("root", Path.Combine(AppContext.BaseDirectory, "../../../../../")));
string self = Path.GetFullPath(options.Get("self", Path.Combine(root, "src/vib-self/bin/Release/net11.0/vib-self.dll")));
string referencePack = Path.GetFullPath(options.Find("reference-pack") ?? Oracles.FindReferencePack());
// The SDK oracle compiles against Vibranium.Core/Std: next to a managed self, else (native self) the managed vib-self build.
string vibraniumReferences = Path.GetFullPath(options.Find("vibranium-references") ?? (self.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
    ? Path.GetDirectoryName(self)! : Path.Combine(root, "src/vib-self/bin/Release/net11.0")));
var severities = SeverityTable.Load(Path.GetFullPath(options.Get("severity-safety", Path.Combine(root, "eng/mutation/severity-safety-ids.txt"))),
    Path.GetFullPath(options.Get("severity-other", Path.Combine(root, "eng/mutation/severity-other-ids.txt"))));
var oracles = new Oracles(root, self, vibraniumReferences, referencePack, options.Number("timeout", 15000))
{
    SelfEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET_GCHeapHardLimit"] = "0x" + ((long)options.Number("self-heap-mb", 1024) * 1024 * 1024).ToString("x", System.Globalization.CultureInfo.InvariantCulture),
    },
    SelfNativeAddressSpaceMb = options.Number("self-native-mb", 4096),
};
if (options.Has("self-test")) return SelfTests.Run(oracles, severities);
if (options.Find("asan") is not null) return Evidence.Run(options, root, oracles, severities);
if (options.Find("check") is { } checkPath)
{
    // Triage one program: every SDK error with its promotion, the cut forms, and the three frontends' verdicts.
    string file = Path.GetFullPath(checkPath);
    var sdk = oracles.Sdk(File.ReadAllText(file));
    foreach (var error in sdk.Errors)
        Console.WriteLine($"{Classification.Bucket(error)} ({error.Promotion}, severity {severities.For(Classification.Bucket(error))}) {error.Line},{error.Column}: {error.Message}");
    var verdictSelf = oracles.Self(file);
    var verdictStage0 = oracles.Stage0(file);
    Console.WriteLine($"kind={(sdk.Errors.Length > 0 ? Classification.ErrorKind(sdk.Errors) : "valid")} cuts=[{string.Join(",", sdk.Errors.Length > 0 ? Classification.Cuts(sdk) : [])}] self={verdictSelf.Status}[{string.Join(",", verdictSelf.Ids)}] stage0={verdictStage0.Status}[{string.Join(",", verdictStage0.Ids)}] analyzer={Oracles.Analyzer(sdk).Status}");
    return 0;
}
if (options.Find("report") is { } existing)
{
    string results = Path.GetFullPath(existing);
    // Re-key every finding with the current severity tables, so a re-render reflects reclassified or split IDs. Findings from
    // an older tool lack CS0649's zero-safety: recompile those programs (same errors, now with the shape), and write the
    // re-keyed findings back, keeping the first original as findings.orig.jsonl.
    string journal = Path.Combine(results, "findings.jsonl");
    var old = File.ReadLines(journal).Select(l => JsonSerializer.Deserialize<Finding>(l)!)
        .Select(f =>
        {
            var errors = f.Errors;
            if (errors.Any(e => e.Id == "CS0649" && e.ZeroSafe is null)
                && oracles.Sdk(File.ReadAllText(Path.Combine(results, f.Path))).Errors is var fresh
                && fresh.Select(e => e.Id).SequenceEqual(errors.Select(e => e.Id)))
                errors = fresh;
            string key = Classification.Bucket(errors[0]);
            return f with { Errors = errors, FirstCsId = key, Severity = severities.For(key), SafetyIds = severities.SafetyIds(errors) };
        }).ToList();
    string original = Path.Combine(results, "findings.orig.jsonl");
    if (!File.Exists(original)) File.Copy(journal, original);
    File.WriteAllLines(journal, old.Select(f => JsonSerializer.Serialize(f)));
    var statistics = JsonSerializer.Deserialize<Statistics>(File.ReadAllText(Path.Combine(results, "statistics.json")))!;
    Reports.Write(results, old, statistics, severities);
    Console.WriteLine("Rendered " + Path.Combine(results, "report.md"));
    return Gate(old, statistics, severities, null);
}
if (options.Has("ci"))
{
    // A fixed, modest budget: same seed, bases and operators on every run, so a red gate is reproducible.
    options.SetDefault("seed", "20261118");
    options.SetDefault("limit", "400");
    options.SetDefault("seeds-per-group", "8");
    options.SetDefault("reduce", "0");
    // The regression corpus: a reduced repro for every first key under which self has accepted a C#-invalid
    // program, or crashed on one. Each runs unmodified as a baseline probe, so a key that leaves known-open-ids.txt stays fixed.
    options.SetDefault("probes", Path.Combine(root, "eng", "mutation", "corpus"));
    options.SetDefault("probe-repairs", "0");
}
string output = Path.GetFullPath(options.Get("output", Path.Combine(Path.GetTempPath(), "vib-mutation-" + Guid.NewGuid().ToString("N"))));
if (output.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    throw new ArgumentException("Output must be outside the repository.");
if (Directory.Exists(output)) throw new ArgumentException("Output directory must not already exist.");
var campaign = new Campaign(options, root, output, oracles, severities);
campaign.Run();
var knownOpen = options.Find("known-open") is { } knownPath
    ? File.ReadLines(knownPath).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal)
    : null;
Console.WriteLine("Results: " + output);
return Gate(campaign.Findings, campaign.Statistics, severities, knownOpen);

static int Gate(IReadOnlyList<Finding> findings, Statistics statistics, SeverityTable severities, HashSet<string>? knownOpen)
{
    // A self failure (crash, timeout, out of memory) on a known-open first key is a known self defect (CS0523 sends self
    // into unbounded allocation); every other frontend failure is an infrastructure failure.
    bool Open(Finding f) => knownOpen is not null && knownOpen.Contains(f.FirstCsId);
    var failures = findings.Where(f => (f.Self.Status is not ("accept" or "reject") && !Open(f))
        || f.Stage0.Status is not ("accept" or "reject") || f.Analyzer.Status is not ("accept" or "reject")).ToArray();
    foreach (var group in failures.GroupBy(f => f.FirstCsId).OrderBy(g => g.Key, StringComparer.Ordinal))
        Console.WriteLine($"GATE failure: {group.Count()} program(s) with first key {group.Key} crashed, timed out or ran out of memory in a frontend, e.g. {group.First().Path}");
    var defects = findings.Where(SeverityTable.IsDefect).Where(f => !Open(f)).ToArray();
    var frontendAccepts = findings.Where(f => SeverityTable.IsInvalid(f) && (f.Stage0.Status == "accept" || f.Analyzer.Status == "accept")).ToArray();
    foreach (var group in defects.GroupBy(f => f.FirstCsId).OrderBy(g => g.Key, StringComparer.Ordinal))
        Console.WriteLine($"GATE defect: self accepts {group.Count()} C#-invalid program(s), first key {group.Key} [{severities.For(group.Key)} {SeverityTable.Priority(severities.For(group.Key))}: {severities.Reason(group.Key)}], e.g. {group.OrderBy(f => f.SourceBytes).First().Path}");
    foreach (var group in frontendAccepts.GroupBy(f => f.FirstCsId).OrderBy(g => g.Key, StringComparer.Ordinal))
        Console.WriteLine($"GATE defect: Stage0/Analyzer accept {group.Count()} C#-invalid program(s), first key {group.Key}, e.g. {group.OrderBy(f => f.SourceBytes).First().Path}");
    // Warnings-only input (blanket-promoted warnings only) is valid C#: reported with self/Stage0 agreement, never failed here.
    var warningsOnly = findings.Where(SeverityTable.IsWarningsOnly).ToArray();
    if (warningsOnly.Length > 0)
        Console.WriteLine($"GATE note: warnings-only programs {warningsOnly.Length}: self accepts {warningsOnly.Count(f => f.Self.Status == "accept")}, Stage0 accepts {warningsOnly.Count(f => f.Stage0.Status == "accept")}, self and Stage0 disagree on {warningsOnly.Count(f => (f.Self.Status == "accept") != (f.Stage0.Status == "accept"))}");
    int subset = findings.Count(SeverityTable.IsSubsetRejection);
    if (subset > 0)
        Console.WriteLine($"GATE note: subset rejections {subset}: self rejected a C#-invalid program that also contains a C10-C20 cut construct, so the cut may be what rejected it");
    if (knownOpen is not null)
    {
        // Known-open keys are reported, not failed. A listed key with no self defect or crash in this run (the corpus holds a
        // repro of each) has most likely been fixed: remove it from the file so the corpus keeps it fixed.
        var open = findings.Where(f => Open(f) && (SeverityTable.IsDefect(f) || f.Self.Status is not ("accept" or "reject"))).ToArray();
        foreach (var group in open.GroupBy(f => f.FirstCsId).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"GATE known-open {group.Key}: self accepts or crashes on {group.Count()} C#-invalid program(s) (not failing)");
        foreach (string key in knownOpen.Except(open.Select(f => f.FirstCsId)).Order(StringComparer.Ordinal))
            Console.WriteLine($"GATE known-open {key}: no self defect or crash in this run; remove it from the known-open file if its fix has landed");
    }
    if (statistics.BaseSelfFailures > 0 || statistics.ReproFailures > 0 || failures.Length > 0)
    {
        Console.WriteLine("GATE infrastructure failure: see report.md");
        return 2;
    }
    Console.WriteLine(defects.Length + frontendAccepts.Length == 0 ? "GATE PASS" : "GATE FAIL");
    return defects.Length + frontendAccepts.Length == 0 ? 0 : 1;
}
