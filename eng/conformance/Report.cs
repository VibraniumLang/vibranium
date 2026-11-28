using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vibranium.Conformance;

/// <summary>Markdown summary of a fuzz output directory (summary.json, curve.csv, buckets/*/info.json).</summary>
internal static class RunReport
{
    public static string Render(string outDir, List<KnownRoot> roots)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "summary.json")));
        var s = doc.RootElement;
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"### Run seed {s.GetProperty("seed").GetString()}: {s.GetProperty("programs").GetInt64()} programs in {s.GetProperty("seconds").GetDouble():F0} s\n\n");
        sb.Append(inv, $"* throughput: **{s.GetProperty("programsPerMinute").GetDouble():F1} programs/min** including reduction; frontends {s.GetProperty("avgFrontendMsPerProgram").GetInt64()} ms/program, execution {s.GetProperty("avgExecMsPerExecutedProgram").GetInt64()} ms/executed program, reduction {s.GetProperty("reductionTests").GetInt64()} oracle runs in {s.GetProperty("reductionSeconds").GetInt64()} worker-seconds\n");
        var v = s.GetProperty("validity");
        int Get(string k) => v.TryGetProperty(k, out var x) ? x.GetInt32() : 0;
        sb.Append(inv, $"* C# validity: valid {Get("valid")}, warning-only {Get("warn-only")}, invalid {Get("invalid")}; executed (all frontends accept) {s.GetProperty("executed").GetInt64()}{(s.TryGetProperty("nonTerminatingSkipped", out var nt) ? $", of which non-terminating (managed run timed out or overflowed the call stack, comparison skipped) {nt.GetInt64()}" : "")}{(s.TryGetProperty("nativeStackExhausted", out var se) ? $"; native legs excused for call-stack exhaustion only (they match the managed run with an unlimited stack; the other legs are compared) in {se.GetInt64()} programs" : "")}{(s.TryGetProperty("nativeSlowExcused", out var sl) ? $"; native legs excused for exceeding the 20 s run budget only (they match the managed run with the long budget) in {sl.GetInt64()} programs" : "")}\n");
        if (s.TryGetProperty("nonTerminatingPrograms", out var ntp) && ntp.GetArrayLength() > 0)
        {
            sb.Append("* skipped or excused programs (replay with `replay --seed S --index I --exec`): ")
                .Append(string.Join(", ", ntp.EnumerateArray().Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.GetProperty("index").GetInt64()} ({p.GetProperty("plan").GetString()}, {p.GetProperty("reason").GetString()})"))))
                .Append('\n');
        }
        var uncovered = s.GetProperty("uncoveredCatalogTags").EnumerateArray().Select(x => x.GetString()).ToList();
        sb.Append(inv, $"* catalog tags never generated: {(uncovered.Count == 0 ? "none" : string.Join(", ", uncovered.Select(t => "`" + t + "`")))}\n");
        var pairs = s.GetProperty("anchorPairs").EnumerateObject().ToList();
        int schedulePairs = s.TryGetProperty("schedulePairs", out var sp) ? sp.GetInt32() : Gen.DefaultAnchors.Length * Gen.DefaultAnchors.Length;
        string profile = s.GetProperty("options").TryGetProperty("Profile", out var pf) ? pf.GetString() ?? "default" : "default";
        sb.Append(inv, $"* generator profile: {profile}; ordered anchor pairs covered: {pairs.Count} of {schedulePairs}\n\n");

        var buckets = s.GetProperty("buckets").EnumerateArray().ToList();
        var first = s.GetProperty("knownRootsFirstHit");
        // re-attribute every bucket with the current known-roots.json, so a predicate added after the run
        // (for a vetted variant) is reflected; the stored category stays visible when it differs
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in buckets)
        {
            string id = b.GetProperty("id").GetString()!;
            string stored = b.GetProperty("category").GetString()!;
            string info = Path.Combine(outDir, "buckets", id, "info.json");
            string cat = stored;
            if (File.Exists(info) && roots.Count > 0)
            {
                using var idoc = JsonDocument.Parse(File.ReadAllText(info));
                var ie = idoc.RootElement;
                var d = new Disagreement(ie.GetProperty("kind").GetString()!, ie.GetProperty("frontends").GetString()!, ie.GetProperty("ids").GetString()!, ie.GetProperty("msg").GetString()!);
                var tags = new HashSet<string>(ie.GetProperty("minTags").EnumerateArray().Select(t => t.GetString()!), StringComparer.Ordinal);
                cat = "new";
                foreach (var root in roots)
                {
                    if (root.Matches(d, tags))
                    {
                        cat = KnownRoot.CategoryPrefix(root.Status) + root.Id;
                        break;
                    }
                }
            }
            else if (roots.Count > 0 && stored.IndexOf(':') is int colon and > 0)
            {
                // unreduced bucket (no info.json): keep its stored root, but apply that root's current status
                string rid = stored[(colon + 1)..];
                foreach (var root in roots)
                {
                    if (root.Id == rid)
                    {
                        cat = KnownRoot.CategoryPrefix(root.Status) + root.Id;
                        break;
                    }
                }
            }
            current[id] = cat;
        }
        string Cat(JsonElement b)
        {
            string id = b.GetProperty("id").GetString()!;
            string stored = b.GetProperty("category").GetString()!;
            return current[id] == stored ? stored : $"{current[id]} (run: {stored})";
        }
        sb.Append("| Root | Status | First hit (program / s) | Buckets | Title |\n|---|---|---|---|---|\n");
        // Round-10 roots (O, A), cuts, then fuzzer-found roots (N), each in numeric order
        static (int, int, string) Order(string id)
        {
            var m = Regex.Match(id, @"^([A-Z]+)(\d*)(.*)$");
            int group = m.Groups[1].Value switch { "O" => 0, "A" => 1, "CUT" => 2, "N" => 3, _ => 4 };
            return (group, m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0, m.Groups[3].Value);
        }
        foreach (var g in roots.GroupBy(r => r.Id).OrderBy(k => Order(k.Key)))
        {
            var r0 = g.First();
            var mine = buckets.Where(b => current[b.GetProperty("id").GetString()!].EndsWith(":" + g.Key, StringComparison.Ordinal)).ToList();
            // a root's first hit is recorded when its first bucket is created, so no bucket means not found
            string hit = mine.Count == 0
                ? "**not found**"
                : first.TryGetProperty(g.Key, out var h) && mine.Any(b => b.GetProperty("category").GetString()!.EndsWith(":" + g.Key, StringComparison.Ordinal))
                    ? $"{h.GetProperty("program").GetInt64()} / {h.GetProperty("seconds").GetDouble():F0}"
                    : $"{mine.Min(b => b.GetProperty("firstProgram").GetInt64())} / {mine.Min(b => b.GetProperty("firstSeconds").GetDouble()):F0} (re-attributed)";
            sb.Append(inv, $"| {g.Key} | {r0.Status} | {hit} | {mine.Count} | {r0.Title.Replace("|", "\\|", StringComparison.Ordinal)} |\n");
        }
        sb.Append("\n| Bucket | Category | Signature | Hits | First program | min.cs lines |\n|---|---|---|---|---|---|\n");
        foreach (var b in buckets)
        {
            string id = b.GetProperty("id").GetString()!;
            string min = Path.Combine(outDir, "buckets", id, "min.cs");
            int lines = File.Exists(min) ? File.ReadAllLines(min).Length : 0;
            string sig = b.GetProperty("signature").GetString()!.Replace("|", "\\|", StringComparison.Ordinal);
            sb.Append(inv, $"| {id} | {Cat(b)} | {(sig.Length > 140 ? sig[..140] + "…" : sig)} | {b.GetProperty("hits").GetInt32()} | {b.GetProperty("firstProgram").GetInt64()} | {lines} |\n");
        }
        sb.Append("\n| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |\n|---|---|---|---|---|---|\n");
        foreach (var w in s.GetProperty("warningOnlyById").EnumerateObject())
        {
            var x = w.Value;
            sb.Append(inv, $"| {w.Name} | {x.GetProperty("programs").GetInt32()} | {x.GetProperty("analyzerClean").GetInt32()} | {x.GetProperty("stage0Accepts").GetInt32()} | {x.GetProperty("selfAccepts").GetInt32()} | {x.GetProperty("firstProgram").GetInt64()} |\n");
        }
        if (s.TryGetProperty("vibInvalidTags", out var vi) && vi.EnumerateObject().Any())
        {
            // C#-valid, Vibranium-invalid shapes: frontends must agree on the IDs; acceptance by all of them is a lead to vet
            sb.Append("\n| Near-miss tag (C#-valid, Vibranium-invalid) | Programs | Accepted by every frontend | First accepted program |\n|---|---|---|---|\n");
            foreach (var t in vi.EnumerateObject())
            {
                var x = t.Value;
                long fa = x.GetProperty("firstAccepted").GetInt64();
                sb.Append(inv, $"| `{t.Name}` | {x.GetProperty("programs").GetInt32()} | {x.GetProperty("acceptedByAll").GetInt32()} | {(fa < 0 ? "—" : fa.ToString(inv))} |\n");
            }
        }
        string curvePath = Path.Combine(outDir, "curve.csv");
        if (File.Exists(curvePath))
        {
            var rows = File.ReadAllLines(curvePath).Skip(1).Select(l => l.Split(',')).Where(c => c.Length >= 6).ToList();
            sb.Append("\nDiscovery curve (one row per 5 minutes; last event in each window): \n\n| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |\n|---|---|---|---|---|---|\n");
            foreach (var win in rows.GroupBy(c => (int)(double.Parse(c[0], inv) / 300)).OrderBy(g => g.Key))
            {
                var last = win.Last();
                sb.Append(inv, $"| ≤{(win.Key + 1) * 5} | {last[1]} | {last[2]} | {last[3]} | {last[4]} | {last[5]} |\n");
            }
        }
        return sb.ToString();
    }
}
