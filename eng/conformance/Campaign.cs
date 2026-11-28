using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Vibranium.Conformance;

internal sealed record CampaignOptions(
    ulong Seed, int Count, double Minutes, int Workers, string OutDir,
    double ExecRate, double AsanRate, bool InProcessSelf, int ReduceBudget, int ReduceBudgetExec, int MaxMinimizePerSignature,
    string Profile = "default", bool NoExec = false);

/// <summary>A known root cause expressed as a predicate over minimized buckets (known-roots.json).</summary>
internal sealed record KnownRoot(string Id, string Title, string Status, string[] Kinds, string? Frontends, string? Ids, string? Msg, string[] TagsAll, string[] TagsAny)
{
    public bool Matches(Disagreement d, ISet<string> tags) =>
        Kinds.Contains(d.Kind)
        && (Frontends is null || Regex.IsMatch(d.Frontends, Frontends))
        && (Ids is null || Regex.IsMatch(d.Ids, Ids))
        && (Msg is null || Regex.IsMatch(d.Msg, Msg))
        && TagsAll.All(tags.Contains)
        && (TagsAny.Length == 0 || TagsAny.Any(tags.Contains));

    /// <summary>
    /// Category prefix for a root's status: <c>open</c> (acknowledged, being fixed) gives <c>known:</c>, <c>new</c>
    /// (vetted, not yet triaged) gives <c>new:</c>, <c>fixed</c> (kept as a regression detector) gives
    /// <c>regression:</c>, and <c>cut</c> (an approved cut that the tree does not implement yet) gives <c>known-cut:</c>.
    /// </summary>
    public static string CategoryPrefix(string status) => status switch
    {
        "cut" => "known-cut:",
        "new" => "new:",
        "fixed" => "regression:",
        _ => "known:",
    };

    public static List<KnownRoot> Load(string path) =>
        JsonSerializer.Deserialize<List<KnownRoot>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
}

internal sealed class Bucket
{
    public required string Key { get; init; }
    public required string Id { get; init; }
    public required Disagreement D { get; init; }
    public required SortedSet<string> MinTags { get; init; }
    public required string Category { get; set; }
    public int Hits { get; set; }
    public long FirstProgram { get; init; }
    public double FirstSeconds { get; init; }
}

internal sealed class SigInfo
{
    public int Hits { get; set; }
    public long FirstProgram { get; set; } = -1;
    public double FirstSeconds { get; set; }
    public List<SortedSet<string>> MinimizedTags { get; } = [];
    public int Attempts { get; set; }
    public bool InProgress { get; set; }
}

internal sealed class Campaign(Toolchain tc, CampaignOptions opt, List<KnownRoot> roots)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SigInfo> _sigs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _validity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _warnIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Stage0Accept, int SelfAccept, int AnalyzerClean)> _warnFrontends = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _warnExample = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _tagCover = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Programs, int Valid, int Disagree)> _anchorPairs = new(StringComparer.Ordinal);
    private readonly List<string> _curve = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _next;
    private long _done;
    private long _executed;
    private long _nonTerminating;
    private readonly List<(long Index, string Plan, string Reason)> _nonTerminatingPrograms = [];
    private long _stackExhausted;
    private long _slowLegs;
    private long _frontMs;
    private long _execMs;
    private long _reduceTests;
    private long _reduceMs;
    private readonly Dictionary<string, (long Program, double Seconds)> _rootFirst = new(StringComparer.Ordinal);

    // vib-invalid (near-miss) tags: programs carrying the tag, and how many of them every frontend accepted
    private readonly Dictionary<string, (int Programs, int AcceptedByAll, long FirstAccepted)> _vibInvalid = new(StringComparer.Ordinal);

    /// <summary>
    /// The shuffled ordered anchor pairs of a campaign. The default profile pairs the 19 default anchors (361 pairs);
    /// the extended profile pairs all 24 anchors and keeps only pairs with at least one extended anchor (215 pairs), so
    /// it adds coverage without repeating the default schedule. The activation profile pairs the 18 default anchors
    /// without Banned and the 9 activation anchors (27 anchors) and keeps only pairs with at least one activation
    /// anchor or with <c>UnionSwitch</c> (440 pairs); the soundness profile likewise pairs the 18 default anchors
    /// without Banned and the 7 soundness anchors (336 pairs). Release-7.1's <c>UnionSwitch</c> (the self switch-label
    /// class) is thus paired with every anchor of the activation and soundness profiles too.
    /// </summary>
    public static List<(Anchor, Anchor)> Schedule(ulong seed, string profile = "default")
    {
        var pairs = new List<(Anchor, Anchor)>();
        var all = Gen.Anchors(profile);
        foreach (var a in all)
        {
            foreach (var b in all)
            {
                bool keep = profile switch
                {
                    "extended" => Gen.ExtendedAnchors.Contains(a) || Gen.ExtendedAnchors.Contains(b),
                    "activation" => Gen.ActivationAnchors.Contains(a) || Gen.ActivationAnchors.Contains(b) || a == Anchor.UnionSwitch || b == Anchor.UnionSwitch,
                    "soundness" => Gen.SoundnessAnchors.Contains(a) || Gen.SoundnessAnchors.Contains(b) || a == Anchor.UnionSwitch || b == Anchor.UnionSwitch,
                    _ => true,
                };
                if (keep)
                {
                    pairs.Add((a, b));
                }
            }
        }
        new Rng(Rng.Mix(seed, 0xC0FFEE)).Shuffle(pairs);
        return pairs;
    }

    /// <summary>Deterministically regenerates program <paramref name="index"/> of campaign <paramref name="seed"/>.</summary>
    public static (GenProgram Program, ExecPlan Plan) Make(ulong seed, long index, double execRate, double asanRate, string profile = "default")
    {
        var sched = Schedule(seed, profile);
        var (a, b) = sched[(int)(index % sched.Count)];
        ulong ps = Rng.Mix(seed, (ulong)index);
        var side = new Rng(Rng.Mix(ps, 0xABCDEF));
        Anchor? third = side.Chance(0.3) ? side.Pick(Gen.Anchors(profile)) : null;
        int maxLines = profile switch { "extended" => MaxLinesExtended, "activation" or "soundness" => MaxLinesActivation, _ => MaxLines };
        double roll = side.Int(1_000_000) / 1_000_000.0;
        // soundness profile: every accepted program runs under ASan/LSan at O0 and O3 (100 % sanitizer rate)
        var plan = profile == "soundness" ? ExecPlan.AsanO0AndO3 : roll < asanRate ? ExecPlan.O0AndO3Asan : roll < execRate ? ExecPlan.O0 : ExecPlan.None;
        // programs are bounded to MaxLines printed lines: deterministic re-draws (same anchors) until one fits
        GenProgram? best = null;
        int bestLines = int.MaxValue;
        for (ulong attempt = 0; attempt < 12; attempt++)
        {
            var gen = new Gen(attempt == 0 ? ps : Rng.Mix(ps, 0x5EED0000 + attempt)) { Profile = profile };
            GenProgram gp;
            try
            {
                gp = gen.Generate(a, b, third);
            }
            catch (InvalidOperationException) when (attempt < 11 || best is not null)
            {
                continue;
            }
            int lines = Printer.Print(gp.Ast).Count(c => c == '\n');
            if (lines <= maxLines)
            {
                return (gp, plan);
            }
            if (lines < bestLines)
            {
                best = gp;
                bestLines = lines;
            }
        }
        return (best!, plan);
    }

    /// <summary>Upper bound on the printed size of a generated program.</summary>
    public const int MaxLines = 30;

    /// <summary>Upper bound for the extended profile, whose anchors add helper types and functions.</summary>
    public const int MaxLinesExtended = 45;

    /// <summary>Upper bound for the activation profile, whose scenarios add one-line helpers and types.</summary>
    public const int MaxLinesActivation = 60;

    /// <summary>Bucket categories found by the last <see cref="Run"/> (new, known:ID, known-cut[:ID]).</summary>
    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (_gate)
            {
                return [.. _buckets.Values.Select(b => b.Category)];
            }
        }
    }

    public void Run()
    {
        Directory.CreateDirectory(opt.OutDir);
        Directory.CreateDirectory(Path.Combine(opt.OutDir, "buckets"));
        var sdk = new SdkOracle(tc);
        var stage0 = new Stage0Oracle(tc);
        string work = Path.Combine(Path.GetTempPath(), "vib-conformance-work-" + Environment.ProcessId);
        var selves = Enumerable.Range(0, opt.Workers).Select(i => new SelfOracle(tc, opt.InProcessSelf)).ToList();
        var threads = new List<Thread>();
        for (int w = 0; w < opt.Workers; w++)
        {
            var engine = new Engine(tc, sdk, stage0, selves[w], Path.Combine(work, "w" + w));
            var t = new Thread(() => Worker(engine), 64 * 1024 * 1024) { IsBackground = true, Name = "worker" + w };
            threads.Add(t);
            t.Start();
        }
        var progress = new Thread(() =>
        {
            while (threads.Any(t => t.IsAlive))
            {
                Thread.Sleep(30_000);
                Log(StatusLine());
            }
        }) { IsBackground = true };
        progress.Start();
        foreach (var t in threads)
        {
            t.Join();
        }
        foreach (var s in selves)
        {
            s.Dispose();
        }
        WriteSummary();
        Log("DONE " + StatusLine());
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private bool TimeUp => (opt.Minutes > 0 && _clock.Elapsed.TotalMinutes >= opt.Minutes);

    /// <summary>Reductions still running 5 minutes after the time budget stop and keep their best result.</summary>
    private bool PastGrace => (opt.Minutes > 0 && _clock.Elapsed.TotalMinutes >= opt.Minutes + 5);

    private void Worker(Engine engine)
    {
        var reducer = new Reducer(engine, opt.ReduceBudget, () => PastGrace);
        var reducerExec = new Reducer(engine, opt.ReduceBudgetExec, () => PastGrace);
        while (true)
        {
            long i = Interlocked.Increment(ref _next) - 1;
            if ((opt.Count > 0 && i >= opt.Count) || TimeUp)
            {
                return;
            }
            GenProgram gp;
            ExecPlan plan;
            try
            {
                (gp, plan) = Make(opt.Seed, i, opt.ExecRate, opt.AsanRate, opt.Profile);
                if (opt.NoExec)
                {
                    // frontend checks only: nothing is compiled natively or run (also in the soundness profile)
                    plan = ExecPlan.None;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException)
            {
                Log($"generator error at index {i}: {ex.Message}");
                continue;
            }
            Outcome o;
            try
            {
                o = engine.Run(gp.Ast, plan);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                Log($"engine error at index {i}: {ex.Message}");
                continue;
            }
            Record(i, gp, o);
            foreach (var d in o.Disagreements)
            {
                if (!ShouldMinimize(d, o.Tags))
                {
                    continue;
                }
                var sw = Stopwatch.StartNew();
                var red = d.NeedsExecution ? reducerExec : reducer;
                Reducer.Result r;
                try
                {
                    // release-7.1: a run-timeout bucket, and any executed bucket of a program with a hanging leg, keeps
                    // the original program: every reduction test would wait out the 20 s run budget of each hanging
                    // leg (the N19 class made self loop)
                    r = d.Kind == "run-timeout" || (d.NeedsExecution && o.Disagreements.Any(x => x.Kind == "run-timeout"))
                        ? new Reducer.Result(gp.Ast, o, 0, Tagger.Size(gp.Ast), Tagger.Size(gp.Ast))
                        : red.Reduce(gp.Ast, o, d.Signature, d.NeedsExecution ? (plan == ExecPlan.None ? ExecPlan.O0 : plan) : ExecPlan.None);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    Log($"reduce error at index {i}: {ex.Message}");
                    lock (_gate)
                    {
                        _sigs[d.Signature].InProgress = false;
                    }
                    continue;
                }
                Interlocked.Add(ref _reduceTests, r.Tests);
                Interlocked.Add(ref _reduceMs, sw.ElapsedMilliseconds);
                AddBucket(i, gp, o, d, r);
            }
        }
    }

    private void Record(long i, GenProgram gp, Outcome o)
    {
        lock (_gate)
        {
            _done++;
            _frontMs += o.FrontMs;
            _execMs += o.ExecMs;
            if (o.Executed)
            {
                _executed++;
            }
            if (o.NonTerminating)
            {
                _nonTerminating++;
                // listed (up to 200) so a skipped class of programs is visible and each one can be replayed and triaged
                if (_nonTerminatingPrograms.Count < 200)
                {
                    _nonTerminatingPrograms.Add((i, gp.Plan, o.Legs.GetValueOrDefault("managed", "").StartsWith("TIMEOUT", StringComparison.Ordinal) ? "managed timeout" : "managed stack overflow"));
                }
            }
            if (o.StackExhaustedLegs.Count > 0)
            {
                // the other legs are still compared; the excused legs are listed with the skipped programs
                _stackExhausted++;
                if (_nonTerminatingPrograms.Count < 200)
                {
                    _nonTerminatingPrograms.Add((i, gp.Plan, "native stack exhaustion only (" + string.Join(",", o.StackExhaustedLegs) + "; matches managed with an unlimited stack)"));
                }
            }
            if (o.SlowLegs.Count > 0)
            {
                // release-7: legs that only exceeded the 20 s budget (they match the managed run with the long budget)
                _slowLegs++;
                if (_nonTerminatingPrograms.Count < 200)
                {
                    _nonTerminatingPrograms.Add((i, gp.Plan, "native leg slower than the 20 s budget only (" + string.Join(",", o.SlowLegs) + "; matches managed with the long budget)"));
                }
            }
            _validity[o.Validity] = _validity.GetValueOrDefault(o.Validity) + 1;
            if (o.Validity == "warn-only")
            {
                foreach (var id in o.Sdk!.Warnings.Select(w => w.Id).Distinct())
                {
                    _warnIds[id] = _warnIds.GetValueOrDefault(id) + 1;
                    // how the Vibranium frontends treat programs whose only C# findings are warnings
                    var w = _warnFrontends.GetValueOrDefault(id);
                    _warnFrontends[id] = (w.Stage0Accept + (o.Stage0!.Accepted ? 1 : 0), w.SelfAccept + (o.SelfAccepted ? 1 : 0), w.AnalyzerClean + (o.Sdk.Analyzer.Count == 0 ? 1 : 0));
                    if (!_warnExample.ContainsKey(id))
                    {
                        _warnExample[id] = i;
                    }
                }
            }
            bool allAccept = o.Validity != "invalid" && o.Sdk is { Analyzer.Count: 0 } && o.Stage0 is { Accepted: true } && o.SelfAccepted;
            foreach (var t in o.Tags.Where(t => Features.VibInvalid.Contains(t) || Features.ExpectedReject.ContainsKey(t)))
            {
                var nv = _vibInvalid.GetValueOrDefault(t, (0, 0, -1));
                _vibInvalid[t] = (nv.Programs + 1, nv.AcceptedByAll + (allAccept ? 1 : 0), allAccept && nv.FirstAccepted < 0 ? i : nv.FirstAccepted);
            }
            foreach (var t in o.Tags)
            {
                _tagCover[t] = _tagCover.GetValueOrDefault(t) + 1;
                if (!Features.Known.Contains(t))
                {
                    _tagCover["UNCATALOGUED:" + t] = 1;
                }
            }
            string pair = gp.Plan.Split('>')[0] + ">" + gp.Plan.Split('>')[1];
            var pc = _anchorPairs.GetValueOrDefault(pair);
            _anchorPairs[pair] = (pc.Programs + 1, pc.Valid + (o.Validity == "valid" ? 1 : 0), pc.Disagree + (o.Disagreements.Count > 0 ? 1 : 0));
            foreach (var d in o.Disagreements)
            {
                if (!_sigs.TryGetValue(d.Signature, out var si))
                {
                    _sigs[d.Signature] = si = new SigInfo { FirstProgram = i, FirstSeconds = _clock.Elapsed.TotalSeconds };
                    _curve.Add(CurveRow());
                }
                si.Hits++;
            }
        }
    }

    private bool ShouldMinimize(Disagreement d, SortedSet<string> tags)
    {
        lock (_gate)
        {
            var si = _sigs[d.Signature];
            if (si.InProgress || si.Attempts >= opt.MaxMinimizePerSignature)
            {
                return false;
            }
            // a program whose tags contain an already minimized tag set most likely shares that root
            if (si.MinimizedTags.Any(m => m.IsSubsetOf(tags)))
            {
                return false;
            }
            si.InProgress = true;
            si.Attempts++;
            return true;
        }
    }

    private static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..10];

    private void AddBucket(long i, GenProgram gp, Outcome o, Disagreement d, Reducer.Result r)
    {
        var minTags = r.Outcome.Tags;
        string key = d.Signature + " | tags: " + string.Join(' ', minTags);
        lock (_gate)
        {
            var si = _sigs[d.Signature];
            si.InProgress = false;
            si.MinimizedTags.Add(minTags);
            if (_buckets.TryGetValue(key, out var existing))
            {
                existing.Hits++;
                return;
            }
            // only known-roots.json predicates classify; on a tree that implements the cuts, a cut-tagged
            // disagreement that no "cut" root explains is a regression and stays "new"
            string category = "new";
            foreach (var root in roots)
            {
                if (root.Matches(d, minTags))
                {
                    // status "cut": user-approved v0.2 cut; "new": a root first found by this fuzzer (vetted, not Round 10)
                    category = KnownRoot.CategoryPrefix(root.Status) + root.Id;
                    if (!_rootFirst.ContainsKey(root.Id))
                    {
                        _rootFirst[root.Id] = (i, _clock.Elapsed.TotalSeconds);
                    }
                    break;
                }
            }
            string id = $"{_buckets.Count + 1:D3}-{Hash(key)}";
            var b = new Bucket { Key = key, Id = id, D = d, MinTags = minTags, Category = category, Hits = 1, FirstProgram = i, FirstSeconds = _clock.Elapsed.TotalSeconds };
            _buckets[key] = b;
            _curve.Add(CurveRow());
            string dir = Path.Combine(opt.OutDir, "buckets", id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "min.cs"), r.Outcome.Source);
            File.WriteAllText(Path.Combine(dir, "min.oracles.txt"), r.Outcome.Report());
            File.WriteAllText(Path.Combine(dir, "original.cs"), o.Source);
            File.WriteAllText(Path.Combine(dir, "original.oracles.txt"), o.Report());
            File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(new
            {
                signature = d.Signature,
                category,
                kind = d.Kind,
                frontends = d.Frontends,
                ids = d.Ids,
                msg = d.Msg,
                minTags,
                seed = opt.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile = opt.Profile,
                index = i,
                plan = gp.Plan,
                mutations = gp.Mutations,
                reduceTests = r.Tests,
                sizeBefore = r.SizeBefore,
                sizeAfter = r.SizeAfter,
                firstSeconds = Math.Round(_clock.Elapsed.TotalSeconds, 1),
            }, JsonOpts));
            Log($"BUCKET {id} [{category}] {d.Signature} | tags: {string.Join(' ', minTags)} (program {i}, {r.Tests} reduction tests, size {r.SizeBefore}->{r.SizeAfter})");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// seconds,programs,signatures,buckets,new,unattributed: new = not explained by a Round-10 root or a cut
    /// (including roots this fuzzer found earlier, category new:ID); unattributed = new buckets that
    /// no root predicate explains yet. Caller holds _gate.
    /// </summary>
    private string CurveRow() =>
        $"{_clock.Elapsed.TotalSeconds:F0},{_done},{_sigs.Count},{_buckets.Count},{_buckets.Values.Count(b => b.Category.StartsWith("new", StringComparison.Ordinal))},{_buckets.Values.Count(b => b.Category == "new")}";

    private string StatusLine()
    {
        lock (_gate)
        {
            double min = _clock.Elapsed.TotalMinutes;
            return $"t={_clock.Elapsed.TotalSeconds:F0}s programs={_done} ({_done / Math.Max(min, 1e-9):F0}/min) executed={_executed} valid={_validity.GetValueOrDefault("valid")} warn-only={_validity.GetValueOrDefault("warn-only")} invalid={_validity.GetValueOrDefault("invalid")} signatures={_sigs.Count} buckets={_buckets.Count} new={_buckets.Values.Count(b => b.Category.StartsWith("new", StringComparison.Ordinal))}";
        }
    }

    private static readonly Lock LogGate = new();

    public static void Log(string line)
    {
        lock (LogGate)
        {
            Console.Out.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {line}");
            Console.Out.Flush();
        }
    }

    private void WriteSummary()
    {
        lock (_gate)
        {
            double minutes = _clock.Elapsed.TotalMinutes;
            var summary = new
            {
                seed = opt.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                options = opt,
                seconds = Math.Round(_clock.Elapsed.TotalSeconds, 1),
                programs = _done,
                programsPerMinute = Math.Round(_done / Math.Max(minutes, 1e-9), 1),
                executed = _executed,
                nonTerminatingSkipped = _nonTerminating,
                nativeStackExhausted = _stackExhausted,
                nativeSlowExcused = _slowLegs,
                nonTerminatingPrograms = _nonTerminatingPrograms.OrderBy(p => p.Index).Select(p => new { index = p.Index, plan = p.Plan, reason = p.Reason }).ToArray(),
                avgFrontendMsPerProgram = _done == 0 ? 0 : _frontMs / _done,
                avgExecMsPerExecutedProgram = _executed == 0 ? 0 : _execMs / _executed,
                reductionTests = _reduceTests,
                reductionSeconds = _reduceMs / 1000,
                validity = _validity,
                warningOnlyById = _warnIds.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => new
                {
                    programs = kv.Value,
                    analyzerClean = _warnFrontends[kv.Key].AnalyzerClean,
                    stage0Accepts = _warnFrontends[kv.Key].Stage0Accept,
                    selfAccepts = _warnFrontends[kv.Key].SelfAccept,
                    firstProgram = _warnExample[kv.Key],
                }),
                signatures = _sigs.OrderByDescending(kv => kv.Value.Hits).Select(kv => new { signature = kv.Key, hits = kv.Value.Hits, firstProgram = kv.Value.FirstProgram, firstSeconds = Math.Round(kv.Value.FirstSeconds, 1), minimized = kv.Value.MinimizedTags.Count }),
                buckets = _buckets.Values.OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => new { id = b.Id, category = b.Category, signature = b.D.Signature, minTags = b.MinTags, hits = b.Hits, firstProgram = b.FirstProgram, firstSeconds = Math.Round(b.FirstSeconds, 1) }),
                knownRootsFirstHit = _rootFirst.ToDictionary(kv => kv.Key, kv => new { program = kv.Value.Program, seconds = Math.Round(kv.Value.Seconds, 1) }),
                tagCoverage = _tagCover.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                uncoveredCatalogTags = Features.All.Where(f => !f.Anchor.StartsWith(Features.MatrixOnly, StringComparison.Ordinal) && f.Kind != "retired" && Features.InProfile(f, opt.Profile)).Select(f => f.Tag).Where(t => !_tagCover.ContainsKey(t)).ToArray(),
                schedulePairs = Schedule(opt.Seed, opt.Profile).Count,
                vibInvalidTags = _vibInvalid.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => new { programs = kv.Value.Programs, acceptedByAll = kv.Value.AcceptedByAll, firstAccepted = kv.Value.FirstAccepted }),
                anchorPairs = _anchorPairs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => new { kv.Value.Programs, kv.Value.Valid, kv.Value.Disagree }),
            };
            File.WriteAllText(Path.Combine(opt.OutDir, "summary.json"), JsonSerializer.Serialize(summary, JsonOpts));
            File.WriteAllLines(Path.Combine(opt.OutDir, "curve.csv"), ["seconds,programs,signatures,buckets,new,unattributed", .. _curve]);
        }
    }
}
