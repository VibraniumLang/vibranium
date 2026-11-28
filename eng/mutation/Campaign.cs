using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Vibranium.Mutation;

/// <summary>
/// One deterministic campaign: select SDK-valid, self-accepted bases; enumerate mutants in a seeded hash
/// order; keep every SDK-invalid mutant with the verdicts of self, Stage0 and the Analyzer frontend; reduce
/// one representative per first CS ID. Oracle calls run on <c>--workers</c> threads, but candidates are
/// committed in their fixed order, so the kept set does not depend on the worker count.
/// </summary>
internal sealed class Campaign(Options options, string root, string output, Oracles oracles, SeverityTable severities)
{
    private sealed record Evaluated(CsError[] Errors, string Relative, string Hash, FrontendResult? Self, FrontendResult? Stage0, FrontendResult? Analyzer, string[] Cuts, int Bytes);
    private sealed record Candidate(SourceProgram Program, Mutation Mutation, string Source);

    private readonly Statistics _statistics = new();
    private readonly List<Finding> _findings = [];
    private readonly HashSet<string> _hashes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selfAcceptIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _defectIds = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _mutantSelfAccepts;
    private StreamWriter? _journal;

    public IReadOnlyList<Finding> Findings => _findings;

    public Statistics Statistics => _statistics;

    private int Workers => Math.Max(1, options.Number("workers", 3));

    public int Run()
    {
        Directory.CreateDirectory(output);
        foreach (string sub in new[] { "mutants", "bases", "repros", ".scratch" }) Directory.CreateDirectory(Path.Combine(output, sub));
        int limit = options.Number("limit", 500);
        int candidateLimit = options.Number("candidates", 50 * Math.Max(limit, 1));
        int seed = options.Number("seed", 20261117);
        WriteSettings(seed, limit, candidateLimit);
        using var journal = new StreamWriter(Path.Combine(output, "findings.jsonl")) { AutoFlush = true };
        _journal = journal;
        try
        {
            var bases = SelectBases(seed);
            var catalog = new MemberCatalog(oracles.Sdk("return 0;").Compilation);
            int perOperator = options.Number("per-operator", 2);
            var perBase = new IReadOnlyList<Mutation>[bases.Count];
            Parallel.For(0, bases.Count, new ParallelOptions { MaxDegreeOfParallelism = Workers },
                i => perBase[i] = Mutations.Create(bases[i].Source, perOperator, oracles.Sdk(bases[i].Source), catalog));
            var pool = bases.SelectMany((program, i) => perBase[i].Select(mutation => (Program: program, Mutation: mutation)))
                .Select(p => (p.Program, p.Mutation, Key: Files.Hash(seed.ToString(CultureInfo.InvariantCulture) + p.Program.Origin + p.Mutation.Operator
                    + p.Mutation.Position.ToString(CultureInfo.InvariantCulture) + "#" + p.Mutation.Replacement)))
                .OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            Array.Clear(perBase);
            SampleMemory(compact: true);
            _statistics.CandidatePool = pool.Length;
            Console.WriteLine($"POOL {pool.Length} candidate mutants from {bases.Count} bases, elapsed={_clock.Elapsed.TotalSeconds:F0}s");
            int growthStep = Math.Max(1, options.Number("growth-step", Math.Max(limit / 60, 1)));
            var chunk = new List<Candidate>();
            bool stop = false;
            foreach (var entry in pool)
            {
                if (stop) break;
                string source = entry.Mutation.Apply(entry.Program.Source);
                if (!_hashes.Add(Files.Hash(source))) { _statistics.DuplicateMutants++; continue; }
                chunk.Add(new Candidate(entry.Program, entry.Mutation, source));
                if (chunk.Count >= 8 * Workers) stop = ProcessChunk(chunk, limit, candidateLimit, growthStep);
            }
            if (!stop && chunk.Count > 0) ProcessChunk(chunk, limit, candidateLimit, growthStep);
            AddGrowthPoint();
            SampleMemory(compact: true);
            Reports.Write(output, _findings, _statistics, severities);
            if (options.Number("reduce", 32) > 0) Reduce();
            SampleMemory(compact: false);
            _statistics.ElapsedSeconds = Math.Round(_clock.Elapsed.TotalSeconds);
            _statistics.PeakWorkingSetMb = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
            _statistics.CgroupPeakMb = CgroupPeakMb();
            Console.WriteLine($"MEMORY peak working set {_statistics.PeakWorkingSetMb} MB (tool process), cgroup peak {_statistics.CgroupPeakMb} MB (tool, vib-self and every child; -1 when not readable)");
            Reports.Write(output, _findings, _statistics, severities);
            Console.WriteLine($"DONE mutants={_statistics.KeptMutants} probes={_statistics.BaselineProbes} ids={_findings.Select(f => f.FirstCsId).Distinct().Count()} self-accept-ids={_findings.Where(f => f.Self.Status == "accept").Select(f => f.FirstCsId).Distinct().Count()} defect-ids={_findings.Where(SeverityTable.IsDefect).Select(f => f.FirstCsId).Distinct().Count()} elapsed={_clock.Elapsed.TotalSeconds:F0}s");
        }
        finally
        {
            Directory.Delete(Path.Combine(output, ".scratch"), recursive: true);
        }
        return 0;
    }

    private void WriteSettings(int seed, int limit, int candidateLimit)
    {
        string self = oracles.SelfDll;
        string selfDirectory = Path.GetDirectoryName(self)!;
        Files.WriteJson(Path.Combine(output, "settings.json"), new
        {
            Mode = options.Has("ci") ? "ci" : "campaign", Seed = seed, Limit = limit, Candidates = candidateLimit,
            SeedsPerGroup = options.Number("seeds-per-group", 12), PerOperator = options.Number("per-operator", 2), Workers,
            Reduce = options.Number("reduce", 32), ReduceRejected = options.Number("reduce-rejected", 8),
            Probes = options.Find("probes") is { } probes ? probes.Split(Path.PathSeparator).Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p))).ToArray() : [],
            Roslyn = typeof(Microsoft.CodeAnalysis.Compilation).Assembly.GetName().Version?.ToString(),
            Runtime = Environment.Version.ToString(), WarningsAsErrors = true, MaxBytes = options.Number("max-bytes", 16000), Timeout = options.Number("timeout", 15000),
            PreprocessorSymbols = Corpus.ParseOptions.PreprocessorSymbolNames.ToArray(),
            DependencyHashes = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                // A managed self is vib-self.dll plus its Core/Std; a native self (vib-stage2) is one executable.
                ["self/" + Path.GetFileName(self)] = Files.HashFile(self),
                ["self/Vibranium.Core.dll"] = HashIfPresent(Path.Combine(selfDirectory, "Vibranium.Core.dll")),
                ["self/Vibranium.Std.dll"] = HashIfPresent(Path.Combine(selfDirectory, "Vibranium.Std.dll")),
                // The SDK oracle's own Vibranium references (the managed build, also for a native self).
                ["sdk/Vibranium.Core.dll"] = Files.HashFile(Path.Combine(oracles.VibraniumReferences, "Vibranium.Core.dll")),
                ["sdk/Vibranium.Std.dll"] = Files.HashFile(Path.Combine(oracles.VibraniumReferences, "Vibranium.Std.dll")),
                ["tool/vib-mutation.dll"] = Files.HashFile(typeof(Oracles).Assembly.Location),
                ["tool/Vibranium.Analyzer.dll"] = Files.HashFile(typeof(Vibranium.Analyzer.VibraniumDiagnosticAnalyzer).Assembly.Location),
                ["tool/vib.dll"] = Files.HashFile(typeof(Vibranium.Cli.VibraniumCompilerDriver).Assembly.Location),
                ["tool/Vibranium.Frontend.Roslyn.dll"] = Files.HashFile(typeof(Vibranium.Frontend.Roslyn.VibraniumFrontend).Assembly.Location),
                ["tool/Microsoft.CodeAnalysis.dll"] = Files.HashFile(typeof(Microsoft.CodeAnalysis.Compilation).Assembly.Location),
                ["tool/Microsoft.CodeAnalysis.CSharp.dll"] = Files.HashFile(typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.Location),
                ["repo/Vibranium.App.props"] = Files.HashFile(Path.Combine(root, "Vibranium.App.props")),
                ["repo/Directory.Build.props"] = Files.HashFile(Path.Combine(root, "Directory.Build.props")),
                ["severity/severity-safety-ids.txt"] = Files.HashFile(severities.SafetyPath),
                ["severity/severity-other-ids.txt"] = Files.HashFile(severities.OtherPath),
            },
            ReferenceAssemblyHashes = Directory.GetFiles(oracles.ReferencePack, "*.dll").Order(StringComparer.Ordinal).ToDictionary(path => Path.GetFileName(path), Files.HashFile),
        });

        static string HashIfPresent(string path) => File.Exists(path) ? Files.HashFile(path) : "absent";
    }

    private List<SourceProgram> SelectBases(int seed)
    {
        var programs = Corpus.Read(root, options.Find("probes"), options.Number("max-bytes", 16000));
        _statistics.SeedsDiscovered = programs.Count;
        int perGroup = options.Number("seeds-per-group", 12);
        var bases = new List<SourceProgram>();
        using var seedJournal = new StreamWriter(Path.Combine(output, "bases.jsonl")) { AutoFlush = true };
        foreach (var group in programs.GroupBy(p => p.Group).OrderBy(g => g.Key == "probes" ? "0" : g.Key, StringComparer.Ordinal))
        {
            int accepted = 0;
            // Stable hash ordering avoids privileging short/generated tests or filesystem enumeration order.
            var ordered = group.OrderBy(p => Files.Hash(seed.ToString(CultureInfo.InvariantCulture) + p.Origin), StringComparer.Ordinal).ToArray();
            int unlimited = group.Key == "probes" ? int.MaxValue : perGroup;
            for (int start = 0; start < ordered.Length && accepted < unlimited; start += 8 * Workers)
            {
                var slice = ordered.Skip(start).Take(8 * Workers).ToArray();
                var verdicts = new (CsError[] Errors, SdkResult? Sdk, FrontendResult? Self)[slice.Length];
                Parallel.For(0, slice.Length, new ParallelOptions { MaxDegreeOfParallelism = Workers }, i =>
                {
                    var sdk = oracles.Sdk(slice[i].Source);
                    if (sdk.Errors.Length > 0) { verdicts[i] = (sdk.Errors, sdk, null); return; }
                    string scratch = Path.Combine(output, ".scratch", "base-" + Files.Hash(slice[i].Origin)[..16] + ".cs");
                    File.WriteAllText(scratch, slice[i].Source);
                    verdicts[i] = (sdk.Errors, null, oracles.Self(scratch));
                    File.Delete(scratch);
                });
                for (int i = 0; i < slice.Length && accepted < unlimited; i++)
                {
                    var program = slice[i];
                    if (verdicts[i].Errors.Length > 0)
                    {
                        _statistics.SeedsInvalid++;
                        if (program.Group != "probes") continue;
                        if (_hashes.Add(Files.Hash(program.Source))) Commit(program, "baseline-probe", Evaluate(program.Source, verdicts[i].Sdk!));
                        accepted += Repair(program, seedJournal);
                        continue;
                    }
                    var baseSelf = verdicts[i].Self!;
                    seedJournal.WriteLine(JsonSerializer.Serialize(new { program.Origin, program.Group, Hash = Files.Hash(program.Source), Self = baseSelf.Status }));
                    if (baseSelf.Status is not ("accept" or "reject")) _statistics.BaseSelfFailures++;
                    if (baseSelf.Status != "accept") { _statistics.SeedsSelfRejected++; continue; }
                    AddBase(program, bases, ref accepted);
                }
            }
            Console.WriteLine($"BASES {group.Key}: {accepted}, discovered={group.Count()}, elapsed={_clock.Elapsed.TotalSeconds:F0}s");
        }
        return bases;

        int Repair(SourceProgram program, StreamWriter seedJournal)
        {
            // Search single-edit repairs of real invalid probes. Only SDK-valid/self-accepted repairs
            // become mutation bases, with the repair and original provenance recorded.
            foreach (var repair in Mutations.Create(program.Source, 2).Take(options.Number("probe-repairs", 120)))
            {
                string repairedSource = repair.Apply(program.Source);
                if (oracles.Sdk(repairedSource).Errors.Length != 0) continue;
                string scratch = Path.Combine(output, ".scratch", "repair.cs");
                File.WriteAllText(scratch, repairedSource);
                var repairedSelf = oracles.Self(scratch);
                if (repairedSelf.Status is not ("accept" or "reject")) _statistics.BaseSelfFailures++;
                if (repairedSelf.Status != "accept") continue;
                var repaired = program with { Origin = program.Origin + "#repair:" + repair.Operator, Source = repairedSource };
                seedJournal.WriteLine(JsonSerializer.Serialize(new { repaired.Origin, repaired.Group, Hash = Files.Hash(repaired.Source), Self = "accept" }));
                int one = 0;
                AddBase(repaired, bases, ref one);
                return one;
            }
            return 0;
        }
    }

    private void AddBase(SourceProgram program, List<SourceProgram> bases, ref int accepted)
    {
        bases.Add(program);
        _statistics.SeedsValid++;
        accepted++;
        _statistics.ValidSeedsByGroup[program.Group] = _statistics.ValidSeedsByGroup.GetValueOrDefault(program.Group) + 1;
        File.WriteAllText(Path.Combine(output, "bases", Files.Hash(program.Source)[..20] + ".cs"), program.Source);
    }

    /// <summary>Runs the oracles on an SDK-invalid program; thread-safe.</summary>
    private Evaluated Evaluate(string source, SdkResult sdk)
    {
        string hash = Files.Hash(source);
        string relative = "mutants/" + hash[..20] + ".cs";
        string path = Path.Combine(output, relative);
        File.WriteAllText(path, source);
        var self = oracles.Self(path);
        var stage0 = oracles.Stage0(path);
        var analyzer = Oracles.Analyzer(sdk);
        return new Evaluated(sdk.Errors, relative, hash, self, stage0, analyzer, Classification.Cuts(sdk), source.Length);
    }

    private bool ProcessChunk(List<Candidate> chunk, int limit, int candidateLimit, int growthStep)
    {
        var results = new Evaluated?[chunk.Count];
        Parallel.For(0, chunk.Count, new ParallelOptions { MaxDegreeOfParallelism = Workers }, i =>
        {
            var sdk = oracles.Sdk(chunk[i].Source);
            results[i] = sdk.Errors.Length == 0 ? null : Evaluate(chunk[i].Source, sdk);
        });
        bool stop = false;
        for (int i = 0; i < chunk.Count; i++)
        {
            if (stop || _statistics.KeptMutants >= limit || _statistics.Candidates >= candidateLimit)
            {
                stop = true;
                if (results[i] is { } dropped && !_findings.Any(f => f.Hash == dropped.Hash)) File.Delete(Path.Combine(output, dropped.Relative));
                continue;
            }
            _statistics.Candidates++;
            if (results[i] is not { } evaluated) { _statistics.ValidMutantsDiscarded++; continue; }
            Commit(chunk[i].Program, chunk[i].Mutation.Operator, evaluated);
            if (_statistics.KeptMutants % growthStep == 0) AddGrowthPoint();
        }
        chunk.Clear();
        Array.Clear(results);
        if (++_chunks % 10 == 0) SampleMemory(compact: true);
        else DrainFinalizers();
        return stop;
    }

    private void Commit(SourceProgram original, string operation, Evaluated evaluated)
    {
        string firstId = Classification.Bucket(evaluated.Errors[0]);
        var finding = new Finding(_findings.Count + 1, original.Origin, original.Group, operation, evaluated.Hash, firstId,
            evaluated.Errors, evaluated.Self!, evaluated.Stage0!, evaluated.Analyzer!, evaluated.Cuts, severities.For(firstId),
            severities.SafetyIds(evaluated.Errors), Classification.ErrorKind(evaluated.Errors), evaluated.Relative, evaluated.Bytes);
        _findings.Add(finding);
        _journal!.WriteLine(JsonSerializer.Serialize(finding));
        if (operation == "baseline-probe") _statistics.BaselineProbes++;
        else
        {
            _statistics.KeptMutants++;
            _ids.Add(firstId);
            if (finding.Self.Status == "accept")
            {
                _mutantSelfAccepts++;
                _selfAcceptIds.Add(firstId);
                if (SeverityTable.IsDefect(finding)) _defectIds.Add(firstId);
            }
        }
        _statistics.KeptByGroup[original.Group] = _statistics.KeptByGroup.GetValueOrDefault(original.Group) + 1;
        string family = operation.Split(':')[0];
        _statistics.KeptByOperator[family] = _statistics.KeptByOperator.GetValueOrDefault(family) + 1;
        string flags = (SeverityTable.IsDefect(finding) ? " SELF-ACCEPT DEFECT" : finding.Self.Status == "accept" ? " SELF-ACCEPT warnings-only" : "") + (finding.Stage0.Status == "accept" ? " STAGE0-ACCEPT" : "");
        Console.WriteLine($"{_findings.Count}: {firstId} self={finding.Self.Status} stage0={finding.Stage0.Status} {original.Group}/{operation}{flags} elapsed={_clock.Elapsed.TotalSeconds:F0}s");
        if (_findings.Count % 200 == 0) Reports.Write(output, _findings, _statistics, severities);
    }

    private int _chunks;

    /// <summary>
    /// Each Stage0 call opens every platform reference with <c>MetadataReference.CreateFromFile</c>, which copies each
    /// image (about 64 MB in all) into native memory that only a finalizer frees. Those objects outlive gen0, so without
    /// a full collection per chunk the native memory of thousands of calls piles up before a gen2 GC runs.
    /// </summary>
    private static void DrainFinalizers()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Records managed heap and working set; with <paramref name="compact"/> first runs a blocking, compacting
    /// full GC (large object heap included) after draining finalizers, so dead compilations and the native metadata
    /// of Stage0's per-call platform references are returned to the OS between chunks.
    /// </summary>
    private void SampleMemory(bool compact)
    {
        if (compact)
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            // Stage0's driver opens every platform reference per call; their native metadata is released by finalizers.
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        var sample = new MemorySample(_findings.Count, GC.GetTotalMemory(false) / (1024 * 1024), Environment.WorkingSet / (1024 * 1024));
        _statistics.Memory.Add(sample);
        Console.WriteLine($"MEMORY findings={sample.Findings} managed={sample.ManagedHeapMb} MB working-set={sample.WorkingSetMb} MB");
    }

    /// <summary>memory.peak of this process's cgroup (the memcap scope when run under it), or -1.</summary>
    private static long CgroupPeakMb()
    {
        try
        {
            string line = File.ReadAllLines("/proc/self/cgroup").First(l => l.StartsWith("0::", StringComparison.Ordinal));
            string peak = Path.Combine("/sys/fs/cgroup", line[3..].TrimStart('/'), "memory.peak");
            return File.Exists(peak) ? long.Parse(File.ReadAllText(peak).Trim(), CultureInfo.InvariantCulture) / (1024 * 1024) : -1;
        }
        catch (IOException)
        {
            return -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private void AddGrowthPoint()
    {
        var point = new GrowthPoint(_statistics.KeptMutants, _ids.Count, _selfAcceptIds.Count, _defectIds.Count, _mutantSelfAccepts);
        if (_statistics.Growth.Count == 0 || _statistics.Growth[^1].Mutants != point.Mutants) _statistics.Growth.Add(point);
    }

    private void Reduce()
    {
        var groups = _findings.GroupBy(f => f.FirstCsId).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        int failures = 0;
        Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = Workers }, group =>
        {
            // Prefer a self accept without a cut form, so the repro shows the ID is reachable without one.
            var representative = group.OrderBy(f => SeverityTable.IsDefect(f) ? 0 : f.Self.Status == "accept" ? 1 : 2).ThenBy(f => f.Cuts.Length).ThenBy(f => f.SourceBytes).First();
            string source = File.ReadAllText(Path.Combine(output, representative.Path));
            string scratch = Path.Combine(output, ".scratch", "reduce-" + group.Key + ".cs");
            int budget = representative.Self.Status == "accept" ? options.Number("reduce", 32) : options.Number("reduce-rejected", 8);
            var reduction = Reducer.Minimize(source, group.Key, representative.Self.Status, representative.ErrorKind, representative.Cuts, oracles, scratch, budget);
            string repro = Path.Combine(output, "repros", group.Key + ".cs");
            File.WriteAllText(repro, reduction.Source);
            var sdk = oracles.Sdk(reduction.Source);
            var reducedSelf = oracles.Self(repro);
            var reducedStage0 = oracles.Stage0(repro);
            var reducedAnalyzer = Oracles.Analyzer(sdk);
            if (sdk.Errors.Length == 0 || Classification.Bucket(sdk.Errors[0]) != group.Key || reducedSelf.Status != representative.Self.Status
                || reducedStage0.Status is not ("accept" or "reject") || reducedAnalyzer.Status is not ("accept" or "reject"))
                System.Threading.Interlocked.Increment(ref failures);
            Files.WriteJson(Path.Combine(output, "repros", group.Key + ".json"), new
            {
                Original = representative, MinimizedBytes = reduction.Source.Length, reduction.Attempts,
                Errors = sdk.Errors, Cuts = sdk.Errors.Length > 0 ? Classification.Cuts(sdk) : [], Self = reducedSelf, Stage0 = reducedStage0, Analyzer = reducedAnalyzer,
            });
            Console.WriteLine($"REDUCED {group.Key}: {source.Length} -> {reduction.Source.Length} bytes, {reduction.Attempts} attempts");
        });
        _statistics.ReproFailures += failures;
    }
}
