using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Vibranium.Conformance;

var opts = new Dictionary<string, string>(StringComparer.Ordinal);
var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
    {
        opts[args[i][2..]] = args[++i];
    }
    else if (args[i].StartsWith("--", StringComparison.Ordinal))
    {
        opts[args[i][2..]] = "true";
    }
    else
    {
        positional.Add(args[i]);
    }
}
string mode = positional.Count > 0 ? positional[0] : "help";
string Opt(string k, string d) => opts.TryGetValue(k, out var v) ? v : d;

if (mode is "help" or "--help")
{
    Console.WriteLine("""
        vib-conformance <mode> [options]
          fuzz            --seed S --count N | --minutes M  [--workers 4] [--out DIR] [--exec-rate 0.5] [--asan-rate 0.05]
                          [--profile default|extended|activation|soundness]  extended adds Span/Option-slot/[Mut]/UTF-8/Std anchors;
                                                            activation adds call-activation, view-return, conditional
                                                            temporary, float->nint, Std append, C5-C9 and generated-code
                                                            anchors with the vib-reject expectation oracle
                          [--self inproc|process] [--reduce-budget 250] [--reduce-budget-exec 80] [--max-min 6] [--roots FILE]
                          [--fail-on none|new|open|any]     exit 1 when such buckets exist (CI gate)
                          [--no-exec]                       frontend checks only: no native build or run (any profile)
          replay          --seed S --index I [--exec] [--profile P]  regenerate one program and print all oracle outputs
          run             FILE.cs [--exec] [--tags T1,T2]   run all oracles on a file (tags enable the expectation oracles)
          features        [--markdown]                      print the feature catalog
          report          --out DIR [--roots FILE]          markdown summary of a finished fuzz output directory
          matrix          [--no-exec]                       run the banned/cut matrix probes and controls
          selfcheck-modes --seed S --count N                compare in-process vs process-mode vib-self (check + emit-ll)
          sdk-crosscheck  --seed S --count N --dir DIR      write N generated programs as real App.props projects (see run-conformance.sh)
          sdk-compare     --dir DIR --log BUILDLOG          compare a real `dotnet build` log with the in-process SDK oracle
          gen-stats       --seed S --count N [--profile P] [--vib] [--dump DIR]  generator quality: C# validity and error/warning IDs
                                                            (SDK oracle; --vib adds Stage-0, --dump writes each program)
        common: --repo ROOT (default: enclosing repo) --config Release
        """);
    return 0;
}

if (mode == "report")
{
    // markdown summary of a finished fuzz output directory
    string dir = Path.GetFullPath(Opt("out", positional.Count > 1 ? positional[1] : "conformance-out"));
    string rp = Opt("roots", Path.Combine(Opt("repo", FindRepo()), "eng", "conformance", "known-roots.json"));
    Console.WriteLine(RunReport.Render(dir, File.Exists(rp) ? KnownRoot.Load(rp) : []));
    return 0;
}

if (mode == "features")
{
    Console.WriteLine(opts.ContainsKey("markdown") ? Features.Markdown() : string.Join('\n', Features.All.Select(f => $"{f.Tag}\t{f.Kind}\t{f.Spec}\t{string.Join('|', f.Expected)}")));
    return 0;
}

string repo = Path.GetFullPath(Opt("repo", FindRepo()));
string cfg = Opt("config", "Release");
string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
string dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
string refPack = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", Environment.Version.ToString(3), "ref", "net11.0");
var tc = new Toolchain(repo, Path.Combine(repo, "src", "vib-self", "bin", cfg, "net11.0"), refPack, "clang-18", "llvm-readelf-18");
foreach (var p in new[] { tc.SelfDll, tc.CoreDll, tc.StdDll, tc.Bootstrap })
{
    if (!File.Exists(p))
    {
        Console.Error.WriteLine("missing " + p + " (build src/vib-self in " + cfg + " first)");
        return 2;
    }
}
if (!Directory.Exists(refPack))
{
    Console.Error.WriteLine("missing reference pack " + refPack);
    return 2;
}
ulong seed = ulong.Parse(Opt("seed", "1"), CultureInfo.InvariantCulture);
string profile = Opt("profile", "default");
if (profile is not ("default" or "extended" or "activation" or "soundness"))
{
    Console.Error.WriteLine("--profile must be default, extended, activation or soundness");
    return 2;
}
string work = Path.Combine(Path.GetTempPath(), "vib-conformance-" + Environment.ProcessId);

switch (mode)
{
    case "fuzz":
    {
        var co = new CampaignOptions(
            seed,
            int.Parse(Opt("count", "0"), CultureInfo.InvariantCulture),
            double.Parse(Opt("minutes", "0"), CultureInfo.InvariantCulture),
            int.Parse(Opt("workers", "4"), CultureInfo.InvariantCulture),
            Path.GetFullPath(Opt("out", "conformance-out")),
            double.Parse(Opt("exec-rate", "0.5"), CultureInfo.InvariantCulture),
            double.Parse(Opt("asan-rate", "0.05"), CultureInfo.InvariantCulture),
            Opt("self", "inproc") == "inproc",
            int.Parse(Opt("reduce-budget", "250"), CultureInfo.InvariantCulture),
            int.Parse(Opt("reduce-budget-exec", "80"), CultureInfo.InvariantCulture),
            int.Parse(Opt("max-min", "6"), CultureInfo.InvariantCulture),
            profile,
            opts.ContainsKey("no-exec"));
        if (co.Count == 0 && co.Minutes <= 0)
        {
            Console.Error.WriteLine("give --count or --minutes");
            return 2;
        }
        string rootsPath = Opt("roots", Path.Combine(repo, "eng", "conformance", "known-roots.json"));
        var roots = File.Exists(rootsPath) ? KnownRoot.Load(rootsPath) : [];
        Campaign.Log($"fuzz seed={seed} profile={co.Profile} count={co.Count} minutes={co.Minutes} workers={co.Workers} self={(co.InProcessSelf ? "inproc" : "process")} exec-rate={(co.NoExec ? "0 (--no-exec: frontend checks only)" : co.Profile == "soundness" ? "1 (soundness: ASan/LSan at O0 and O3)" : co.ExecRate)} asan-rate={(co.NoExec ? 0 : co.Profile == "soundness" ? 1 : co.AsanRate)} roots={roots.Count}");
        var campaign = new Campaign(tc, co, roots);
        campaign.Run();
        // --fail-on none (default) | new (unexplained buckets, roots with status "new", regressions of "fixed" roots)
        //           | open (also roots with status "open") | any (also roots with status "cut")
        string failOn = Opt("fail-on", "none");
        var cats = campaign.Categories;
        int failing = failOn switch
        {
            "new" => cats.Count(c => c.StartsWith("new", StringComparison.Ordinal) || c.StartsWith("regression", StringComparison.Ordinal)),
            "open" => cats.Count(c => !c.StartsWith("known-cut", StringComparison.Ordinal)),
            "any" => cats.Count,
            _ => 0,
        };
        Campaign.Log($"fail-on={failOn}: {failing} failing bucket(s)");
        return failing == 0 ? 0 : 1;
    }
    case "replay":
    case "run":
    {
        string source;
        SortedSet<string> tags;
        if (mode == "replay")
        {
            long index = long.Parse(Opt("index", "0"), CultureInfo.InvariantCulture);
            var (gp, _) = Campaign.Make(seed, index, 0, 0, profile);
            source = Printer.Print(gp.Ast);
            tags = Tagger.Collect(gp.Ast);
            Console.WriteLine($"// seed {seed} profile {profile} index {index} plan {gp.Plan} mutations [{string.Join(',', gp.Mutations)}]");
        }
        else
        {
            source = File.ReadAllText(positional[1]);
            // --tags a,b: feature tags for the expectation oracles (banned/cut/vib-reject), e.g. from a bucket's tags.txt
            tags = new SortedSet<string>(Opt("tags", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
        }
        Console.WriteLine(source);
        using var self = new SelfOracle(tc, Opt("self", "inproc") == "inproc");
        var engine = new Engine(tc, new SdkOracle(tc), new Stage0Oracle(tc), self, Path.Combine(work, "w"));
        var o = engine.Run(source, tags, opts.ContainsKey("exec") ? (profile == "soundness" ? ExecPlan.AsanO0AndO3 : opts.ContainsKey("asan") ? ExecPlan.O0AndO3Asan : ExecPlan.O0) : ExecPlan.None);
        Console.WriteLine(o.Report());
        Cleanup(work);
        return 0;
    }
    case "matrix":
    {
        using var self = new SelfOracle(tc, true);
        var engine = new Engine(tc, new SdkOracle(tc), new Stage0Oracle(tc), self, Path.Combine(work, "w"));
        var rows = MatrixProbes.Rows.Select(r => (Tag: r.Tag, Expected: Features.Expected.TryGetValue(r.Tag, out var e) ? string.Join("|", e) : "accept", r.Source))
            .Concat(MatrixProbes.Controls.Select(c => (Tag: c.Name, Expected: "accept", c.Source)))
            .Concat(MatrixProbes.Decided.Select(d => (Tag: d.Name, d.Expected, d.Source)))
            .Concat(MatrixProbes.DecidedPending.Select(d => (Tag: d.Name, d.Expected, d.Source)));
        // release-7: the matrix is a CI gate (the only detector of some seeded bugs, CONFORMANCE.md §6.16): the
        // command exits 1 when any row is a violation or a probe error; the summary goes to stderr.
        // release-7.1: a violation of a DecidedPending row whose root is not fixed is reported as that root's
        // category and fails like a gate bucket under --fail-on (open by default; new passes an open root)
        string matrixFailOn = Opt("fail-on", "open");
        string matrixRootsPath = Opt("roots", Path.Combine(repo, "eng", "conformance", "known-roots.json"));
        var rootStatus = (File.Exists(matrixRootsPath) ? KnownRoot.Load(matrixRootsPath) : [])
            .GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Status, StringComparer.Ordinal);
        var pendingRoot = MatrixProbes.DecidedPending.ToDictionary(d => d.Name, d => d.Root, StringComparer.Ordinal);
        var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (tag, exp, src) in rows)
        {
            // release-7.1: the linear-switch rows (N20) run like the soundness profile, ASan/LSan at -O0 and -O3 for both
            // native compilers, so their controls are exec controls for the linear drops
            var plan = opts.ContainsKey("no-exec") ? ExecPlan.None : tag.StartsWith("decided.linear-switch-", StringComparison.Ordinal) ? ExecPlan.AsanO0AndO3 : ExecPlan.O0;
            var o = engine.Run(src, new SortedSet<string>(StringComparer.Ordinal) { tag }, plan);
            static string Ids(IEnumerable<DiagLine> ds) => string.Join(",", ds.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));
            string sdkV = o.Sdk!.Errors.Count > 0 ? "C#-invalid " + Ids(o.Sdk.Errors) : o.Sdk.Warnings.Count > 0 ? "warn " + Ids(o.Sdk.Warnings) : "C#-valid";
            string an = o.Sdk.Analyzer.Count == 0 ? "accept" : Ids(o.Sdk.Analyzer);
            string s0 = o.Stage0!.Accepted ? "accept" : Ids(o.Stage0.Diags);
            string sf = o.SelfAccepted ? "accept" : Ids(o.SelfDiags);
            // OK: every frontend gives the expected result; OK*: the expected ID plus secondary IDs; violation otherwise
            string Verdict()
            {
                string[] got = [an, s0, sf];
                if (exp == "accept")
                {
                    return got.All(g => g == "accept") && o.Disagreements.Count == 0 ? "OK" : "violation";
                }
                if (exp.StartsWith('='))
                {
                    return got.All(g => g == exp[1..]) ? "OK" : "violation"; // exact ID set on every frontend
                }
                var want = exp.Split('|');
                if (got.Any(g => g == "accept" || !g.Split(',').Any(want.Contains)))
                {
                    return "violation";
                }
                return got.All(g => g.Split(',').All(want.Contains)) ? "OK" : "OK*";
            }
            string runs = string.Join(" ", o.Runs.Select(kv => kv.Key + "=" + kv.Value.Exit));
            // C10–C21 rows: the exact message at the anchor of each cut's first occurrence (CutScan), once per file
            bool s0Cs = o.Stage0 is { Accepted: false } && o.Stage0.Diags.Any(d => d.Id.StartsWith("CS", StringComparison.Ordinal));
            var anchor = Engine.CutAnchorProblems(o, s0Cs);
            string verdict = Verdict();
            if (CutScan.Tags.Contains(tag) && !o.CutHits.Any(h => h.Tag == tag) || exp == "accept" && o.CutHits.Count > 0)
            {
                verdict = "probe-error"; // the probe does not contain its cut, or a control contains one
            }
            else if (verdict != "violation" && exp != "accept" && anchor.Count > 0)
            {
                verdict = "violation";
            }
            if (verdict == "violation" && pendingRoot.TryGetValue(tag, out var pr) && rootStatus.TryGetValue(pr, out var prs) && prs != "fixed")
            {
                verdict = KnownRoot.CategoryPrefix(prs) + pr;
            }
            string cuts = o.CutHits.Count == 0 ? "" : "cuts=" + string.Join(",", o.CutHits.Select(h => $"{h.Cut}@{h.Line}.{h.Col}")) + (anchor.Count == 0 ? (exp == "accept" ? "" : " anchors=OK") : " anchors=" + string.Join(" ", anchor.Select(a => a.Frontend + ":" + a.Tag + ":" + a.Detail)));
            Console.WriteLine($"{tag}\tsdk={sdkV}\tanalyzer={an}\tstage0={s0}\tself={sf}\texpected={exp}\tverdict={verdict}\t{runs}\t{cuts}\t{string.Join(" ; ", o.Disagreements.Select(d => d.Signature))}");
            verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
        }
        Cleanup(work);
        int failed = matrixFailOn == "none" ? 0 : verdicts.Where(kv => kv.Key is "violation" or "probe-error"
            || kv.Key.StartsWith("new:", StringComparison.Ordinal)
            || matrixFailOn is "open" or "any" && kv.Key.StartsWith("known:", StringComparison.Ordinal)).Sum(kv => kv.Value);
        Console.Error.WriteLine($"matrix: {verdicts.Values.Sum()} rows; " + string.Join(", ", verdicts.Select(kv => $"{kv.Key} {kv.Value}"))
            + (failed > 0 ? $"; FAIL ({failed} rows)" : "; pass"));
        return failed > 0 ? 1 : 0;
    }
    case "selfcheck-modes":
    {
        int n = int.Parse(Opt("count", "50"), CultureInfo.InvariantCulture);
        using var inproc = new SelfOracle(tc, true);
        using var proc = new SelfOracle(tc, false);
        Directory.CreateDirectory(Path.Combine(work, "w"));
        string path = Path.Combine(work, "w", "Program.cs");
        int same = 0;
        int diff = 0;
        int irSame = 0;
        int irDiff = 0;
        long tIn = 0;
        long tProc = 0;
        for (long i = 0; i < n; i++)
        {
            var (gp, _) = Campaign.Make(seed, i, 0, 0, profile);
            File.WriteAllText(path, Printer.Print(gp.Ast));
            var a = inproc.Check(path);
            var b = proc.Check(path);
            tIn += a.Ms;
            tProc += b.Ms;
            if (a.Exit == b.Exit && a.Stdout == b.Stdout && a.Stderr == b.Stderr)
            {
                same++;
            }
            else
            {
                diff++;
                Console.WriteLine($"DIFF index {i}: inproc {a.Short}\n            process {b.Short}");
            }
            if (a.Exit == 0)
            {
                string la = Path.Combine(work, "w", "a.ll");
                string lb = Path.Combine(work, "w", "b.ll");
                var ea = inproc.EmitLl(path, la, false);
                var eb = proc.EmitLl(path, lb, false);
                if (ea.Exit == eb.Exit && File.Exists(la) && File.Exists(lb) && File.ReadAllBytes(la).AsSpan().SequenceEqual(File.ReadAllBytes(lb)))
                {
                    irSame++;
                }
                else
                {
                    irDiff++;
                    Console.WriteLine($"IR DIFF index {i}: {ea.Short} / {eb.Short}");
                }
                File.Delete(la);
                File.Delete(lb);
            }
        }
        Console.WriteLine($"selfcheck-modes seed={seed} programs={n} check-identical={same} check-different={diff} ir-identical={irSame} ir-different={irDiff} avg-ms inproc={tIn / Math.Max(1, n)} process={tProc / Math.Max(1, n)}");
        Cleanup(work);
        return diff == 0 && irDiff == 0 ? 0 : 1;
    }
    case "sdk-crosscheck":
        return SdkCrossCheck.Write(tc, seed, int.Parse(Opt("count", "30"), CultureInfo.InvariantCulture), Path.GetFullPath(Opt("dir", Path.Combine(repo, "eng", "sdkcheck.tmp"))));
    case "sdk-compare":
        return SdkCrossCheck.Compare(tc, Path.GetFullPath(Opt("dir", Path.Combine(repo, "eng", "sdkcheck.tmp"))), Opt("log", "build.log"));
    case "gen-stats":
    {
        // generator quality: C# validity classes and the C# error/warning IDs of generated programs (SDK oracle only)
        int n = int.Parse(Opt("count", "500"), CultureInfo.InvariantCulture);
        var sdk = new SdkOracle(tc);
        // --vib: also run Stage-0 on the C#-valid and warning-only programs (how much of the output Vibranium accepts)
        var stage0 = opts.ContainsKey("vib") ? new Stage0Oracle(tc) : null;
        string gsDir = Path.Combine(work, "gen-stats");
        var vibRows = new (bool Ran, bool Accepted, string Ids, string[] Tags)[n];
        var rows = new (string Validity, bool Mutated, string Plan, List<DiagLine> Errors, List<DiagLine> Warnings)[n];
        var cutRows = new List<CutHit>[n];
        // a deliberate C1 shape (a local function directly in a switch section) is a C18 form too; counted apart
        var c1Only = new bool[n];
        Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = int.Parse(Opt("workers", "4"), CultureInfo.InvariantCulture) }, i =>
        {
            GenProgram gp;
            try
            {
                (gp, _) = Campaign.Make(seed, i, 0, 0, profile);
            }
            catch (InvalidOperationException ex)
            {
                rows[i] = ("generator-error", false, ex.Message, [], []);
                return;
            }
            var r = sdk.Check(Printer.Print(gp.Ast), "Program.cs");
            string v = r.Errors.Count > 0 ? "invalid" : r.Warnings.Count > 0 ? "warn-only" : "valid";
            rows[i] = (v, gp.Mutations.Length > 0, gp.Plan, r.Errors, r.Warnings);
            cutRows[i] = CutScan.Scan(r.Compilation, r.Compilation.SyntaxTrees.First(t => t.FilePath == "Program.cs"));
            c1Only[i] = cutRows[i].Count > 0 && cutRows[i].All(h => h.Cut == "C18") && Tagger.Collect(gp.Ast).Contains("cut.localfunc-in-switch-section");
            string? dumpLine = null;
            if (stage0 is not null && v != "invalid")
            {
                string d = Path.Combine(gsDir, "p" + i.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(d);
                string f = Path.Combine(d, "Program.cs");
                File.WriteAllText(f, Printer.Print(gp.Ast));
                var s0 = stage0.Check(f);
                vibRows[i] = (true, s0.Accepted, string.Join(',', s0.Diags.Select(x => x.Id).Distinct().Order(StringComparer.Ordinal)), [.. Tagger.Collect(gp.Ast)]);
                dumpLine = "stage0: " + (s0.Accepted ? "accept" : string.Join(" | ", s0.Diags.Select(x => $"{x.Id} ({x.Line},{x.Col}) {x.Message}")));
                Directory.Delete(d, recursive: true);
            }
            // --dump DIR: every program with its plan, tags, C# diagnostics and (with --vib) Stage-0 result, for template work
            if (opts.TryGetValue("dump", out var dumpDir))
            {
                Directory.CreateDirectory(dumpDir);
                string head = $"// index {i} plan {gp.Plan} mutations [{string.Join(',', gp.Mutations)}] validity {v}\n// tags {string.Join(' ', Tagger.Collect(gp.Ast))}\n"
                    + string.Concat(cutRows[i].Select(h => $"// cut {h.Cut} {h.Tag} ({h.Line},{h.Col})\n"))
                    + string.Concat(r.Errors.Concat(r.Warnings).Select(x => $"// {x.Id} ({x.Line},{x.Col}) {x.Message}\n"))
                    + (dumpLine is null ? "" : "// " + dumpLine.Replace("\n", " ", StringComparison.Ordinal) + "\n");
                File.WriteAllText(Path.Combine(dumpDir, $"p{i:D5}.cs"), head + Printer.Print(gp.Ast));
            }
        });
        if (stage0 is not null)
        {
            var ran = Enumerable.Range(0, n).Where(i => vibRows[i].Ran).ToList();
            Console.WriteLine($"stage0 on C#-valid/warn-only programs: {ran.Count(i => vibRows[i].Accepted)} accepted of {ran.Count}");
            foreach (var g in ran.GroupBy(i => rows[i].Plan.Split('>')[0]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var nmFree = g.Where(i => !vibRows[i].Tags.Any(Features.VibInvalid.Contains) && !vibRows[i].Tags.Any(Features.Expected.ContainsKey) && !vibRows[i].Tags.Any(Features.ExpectedReject.ContainsKey)).ToList();
                var ids = nmFree.Where(i => !vibRows[i].Accepted).GroupBy(i => vibRows[i].Ids).OrderByDescending(x => x.Count()).Take(4).Select(x => $"{(x.Key.Length == 0 ? "crash" : x.Key)}:{x.Count()} (e.g. {x.First()})");
                Console.WriteLine($"  outer {g.Key}: accepted {g.Count(i => vibRows[i].Accepted)}/{g.Count()}; without near-miss/banned tags accepted {nmFree.Count(i => vibRows[i].Accepted)}/{nmFree.Count} rejected as [{string.Join(' ', ids)}]");
            }
            foreach (var t in Features.VibInvalid.Concat(Features.ExpectedReject.Keys).Order(StringComparer.Ordinal))
            {
                var with = ran.Where(i => vibRows[i].Tags.Contains(t)).ToList();
                if (with.Count > 0)
                {
                    Console.WriteLine($"  {(Features.ExpectedReject.TryGetValue(t, out var ex) ? "vib-reject(" + string.Join("|", ex) + ")" : "near-miss")} {t}: {with.Count} programs, stage0 accepts {with.Count(i => vibRows[i].Accepted)}, IDs [{string.Join(' ', with.Where(i => !vibRows[i].Accepted).GroupBy(i => vibRows[i].Ids).Select(x => x.Key + ":" + x.Count()))}]");
                }
            }
        }
        // the per-process work dir held only gen-stats/; remove it whole (with or without --vib) so no empty /tmp dir is left
        Cleanup(work);
        // C10–C21 cut forms (the tool's own scan) per cut: programs, how many of them unmutated and C#-valid, top plans
        var withCut = Enumerable.Range(0, n).Where(i => cutRows[i] is { Count: > 0 } && !c1Only[i]).ToList();
        Console.WriteLine($"cut forms (C10–C21): {withCut.Count} of {n} programs ({withCut.Count(i => !rows[i].Mutated && rows[i].Validity != "invalid")} unmutated C#-valid/warn-only); "
            + $"{c1Only.Count(b => b)} more carry only the C18 of a deliberate C1 shape (local function directly in a switch section)");
        foreach (var g in withCut.SelectMany(i => cutRows[i].Select(h => (h.Cut, h.Tag, i))).GroupBy(x => x.Cut + " " + x.Tag).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var plans = g.GroupBy(x => rows[x.i].Plan.Split('>')[0] + (rows[x.i].Mutated ? "(mut)" : "")).OrderByDescending(p => p.Count()).Take(5).Select(p => p.Key + ":" + p.Count());
            Console.WriteLine($"  {g.Key}\t{g.Count()}\te.g. index {string.Join(",", g.Take(3).Select(x => x.i))}\t[{string.Join(' ', plans)}]");
        }
        foreach (var g in rows.GroupBy(r => (r.Mutated, r.Validity)).OrderBy(g => g.Key.Mutated).ThenBy(g => g.Key.Validity, StringComparer.Ordinal))
        {
            Console.WriteLine($"{(g.Key.Mutated ? "mutated" : "unmutated")}\t{g.Key.Validity}\t{g.Count()}");
        }
        void Ids(string title, Func<int, bool> filter, Func<int, List<DiagLine>> pick)
        {
            Console.WriteLine(title);
            var idx = Enumerable.Range(0, n).Where(filter).ToList();
            foreach (var g in idx.SelectMany(i => pick(i).Select(d => (d.Id, i, d.Message)).DistinctBy(x => x.Id)).GroupBy(x => x.Id).OrderByDescending(g => g.Count()))
            {
                var ex = g.First();
                var plans = g.Select(x => rows[x.i].Plan.Split('>')[0]).GroupBy(p => p).OrderByDescending(p => p.Count()).Take(3).Select(p => p.Key + ":" + p.Count());
                Console.WriteLine($"  {g.Key}\t{g.Count()}\te.g. index {ex.i}: {ex.Message}\t[{string.Join(' ', plans)}]");
                if (opts.ContainsKey("show"))
                {
                    foreach (var (_, i, _) in g.Take(int.Parse(Opt("show", "1"), CultureInfo.InvariantCulture)))
                    {
                        var (gp2, _) = Campaign.Make(seed, i, 0, 0, profile);
                        var lines = Printer.Print(gp2.Ast).Split('\n');
                        var at = pick(i).Where(d => d.Id == g.Key).Select(d => d.Line).ToHashSet();
                        Console.WriteLine($"    --- index {i} plan {rows[i].Plan} mutations [{string.Join(',', gp2.Mutations)}]");
                        for (int ln = 0; ln < lines.Length; ln++)
                        {
                            Console.WriteLine($"    {(at.Contains(ln + 1) ? ">>" : "  ")}{ln + 1,3} {lines[ln]}");
                        }
                    }
                }
            }
        }
        Ids("errors in unmutated programs:", i => !rows[i].Mutated && rows[i].Validity == "invalid", i => rows[i].Errors);
        Ids("errors in mutated programs:", i => rows[i].Mutated && rows[i].Validity == "invalid", i => rows[i].Errors);
        Ids("warnings in warn-only programs:", i => rows[i].Validity == "warn-only", i => rows[i].Warnings);
        return 0;
    }
    default:
        Console.Error.WriteLine("unknown mode " + mode);
        return 2;
}

static void Cleanup(string dir)
{
    try
    {
        Directory.Delete(dir, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

static string FindRepo()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "Vibranium.slnx")))
    {
        d = d.Parent;
    }
    return d?.FullName ?? Environment.CurrentDirectory;
}
