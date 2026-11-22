using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Vibranium.Conformance;

internal sealed record Disagreement(string Kind, string Frontends, string Ids, string Msg)
{
    public string Signature => $"{Kind} | {Frontends} | {Ids} | {Msg}";

    /// <summary>Kinds whose reproduction needs native execution.</summary>
    public bool NeedsExecution => Kind is "output-mismatch" or "native-build-failure" or "sanitizer" or "link-contract" or "run-timeout" or "managed-crash";
}

/// <summary>Which execution legs to run for a program.</summary>
internal enum ExecPlan { None, O0, O0AndO3Asan, AsanO0AndO3 }

/// <summary>All oracle outputs for one program.</summary>
internal sealed class Outcome
{
    public required string Source { get; init; }
    public required SortedSet<string> Tags { get; init; }
    public SdkOracle.Result? Sdk { get; set; }
    public Stage0Oracle.Result? Stage0 { get; set; }
    public ProcResult? Self { get; set; }
    public List<DiagLine> SelfDiags { get; set; } = [];
    public SortedDictionary<string, string> Legs { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, ProcResult> Runs { get; } = new(StringComparer.Ordinal);
    public List<Disagreement> Disagreements { get; } = [];
    public string Validity { get; set; } = "?";
    public bool Executed { get; set; }
    public bool NonTerminating { get; set; }
    public long FrontMs { get; set; }
    public long ExecMs { get; set; }

    public bool SelfAccepted => Self is { Exit: 0, TimedOut: false };

    /// <summary>First occurrence of every C10–C21 cut in the program (<see cref="CutScan"/>).</summary>
    public List<CutHit> CutHits { get; set; } = [];

    /// <summary>Native legs that only exhausted the default call stack (they match the managed run with an unlimited stack).</summary>
    public List<string> StackExhaustedLegs { get; } = [];

    /// <summary>Native legs that only exceeded the 20 s run budget: re-run with the long budget, they match the managed
    /// run (release-7; a slower `-O0` binary is not a parity difference).</summary>
    public List<string> SlowLegs { get; } = [];

    /// <summary>Positions behind a cut-anchor disagreement (kept out of the signature).</summary>
    public string? CutAnchorDetail { get; set; }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append("validity: ").Append(Validity).Append('\n');
        sb.Append("tags: ").Append(string.Join(' ', Tags)).Append('\n');
        if (Sdk is not null)
        {
            sb.Append("sdk-roslyn errors: ").Append(Sdk.Errors.Count == 0 ? "none" : string.Join(" ; ", Sdk.Errors)).Append('\n');
            sb.Append("sdk-roslyn warnings (errors under TreatWarningsAsErrors): ").Append(Sdk.Warnings.Count == 0 ? "none" : string.Join(" ; ", Sdk.Warnings)).Append('\n');
            sb.Append("analyzer: ").Append(Sdk.Analyzer.Count == 0 ? "none" : string.Join(" ; ", Sdk.Analyzer)).Append(Sdk.AnalyzerCrash is null ? "" : " CRASH " + Sdk.AnalyzerCrash).Append('\n');
        }
        if (Stage0 is not null)
        {
            sb.Append("stage0 check: ").Append(Stage0.Accepted ? "OK" : "REJECT " + string.Join(" ; ", Stage0.Diags)).Append(Stage0.Crash is null ? "" : " CRASH " + Stage0.Crash).Append('\n');
        }
        if (Self is not null)
        {
            sb.Append("self check: ").Append(Self.Short).Append('\n');
        }
        foreach (var (k, v) in Legs)
        {
            sb.Append(k).Append(": ").Append(v).Append('\n');
        }
        foreach (var (k, v) in Runs)
        {
            sb.Append(k).Append(" run: ").Append(v.Short).Append('\n');
        }
        if (CutHits.Count > 0)
        {
            sb.Append("cuts (C10–C21 scan, first occurrence): ").Append(string.Join(" ; ", CutHits.Select(h => $"{h.Cut} {h.Tag} ({h.Line},{h.Col})"))).Append('\n');
        }
        if (CutAnchorDetail is not null)
        {
            sb.Append("cut anchors: ").Append(CutAnchorDetail).Append('\n');
        }
        foreach (var d in Disagreements)
        {
            sb.Append("DISAGREEMENT: ").Append(d.Signature).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>Runs every oracle on one program and classifies disagreements.</summary>
internal sealed class Engine(Toolchain tc, SdkOracle sdk, Stage0Oracle stage0, SelfOracle self, string workDir)
{
    private static readonly Regex QuotedRx = new("'[^']*'", RegexOptions.Compiled);
    private static readonly Regex NumRx = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex PathRx = new(@"/[^\s:]+", RegexOptions.Compiled);
    private static readonly Regex SelfDiagRx = new(@"\((\d+),(\d+)\): error ([A-Z]+\d+): (.*)$", RegexOptions.Compiled);
    // release-7.1: a ViewMove helper is one line, `[public ]static int VmN(int p) { ... }` (ProductionsViewMove.cs)
    private static readonly Regex ViewMoveHelperRx = new(@"^\s*(public\s+)?static\s+int\s+Vm\d+\(int p\)", RegexOptions.Compiled);

    private bool _managedLibsCopied;

    public string WorkDir => workDir;

    public Outcome Run(ProgramNode ast, ExecPlan plan) => Run(Printer.Print(ast), Tagger.Collect(ast), plan);

    /// <summary>
    /// A source line that makes <see cref="Run(string, SortedSet{string}, ExecPlan)"/> write the program as
    /// <c>Program.g.cs</c> (a generated-code file name, where the Analyzer must still report). Text-based, so a saved
    /// reproduction keeps the file name.
    /// </summary>
    public const string GeneratedFileMarker = "// vib-conformance: file-name Program.g.cs";

    public Outcome Run(string source, SortedSet<string> tags, ExecPlan plan)
    {
        Directory.CreateDirectory(workDir);
        bool genName = source.Contains(GeneratedFileMarker, StringComparison.Ordinal);
        string path = Path.Combine(workDir, genName ? "Program.g.cs" : "Program.cs");
        File.Delete(Path.Combine(workDir, genName ? "Program.cs" : "Program.g.cs"));
        File.WriteAllText(path, source);
        var o = new Outcome { Source = source, Tags = tags };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        o.Sdk = sdk.Check(source, path);
        // the tool's own C10–C21 detector: a cut form adds its cut.* tag (expected VIB000 with the exact message at the anchor)
        var tree = o.Sdk.Compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath == path);
        if (tree is not null)
        {
            o.CutHits = CutScan.Scan(o.Sdk.Compilation, tree);
            foreach (var h in o.CutHits)
            {
                o.Tags.Add(h.Tag);
            }
        }
        o.Stage0 = stage0.Check(path);
        o.Self = self.Check(path);
        o.SelfDiags = ParseSelf(o.Self.Stderr);
        o.FrontMs = sw.ElapsedMilliseconds;
        o.Validity = o.Sdk.Errors.Count > 0 ? "invalid" : o.Sdk.Warnings.Count > 0 ? "warn-only" : "valid";
        ClassifyFrontends(o);
        bool allAccept = o.Sdk.Errors.Count == 0 && o.Sdk.Analyzer.Count == 0 && o.Stage0.Accepted && o.SelfAccepted;
        if (allAccept)
        {
            // static lowering contract (release-7): emitted IR only, nothing is compiled by clang or run
            CheckIrContract(o, path);
        }
        if (allAccept && plan != ExecPlan.None)
        {
            sw.Restart();
            Execute(o, path, plan);
            o.ExecMs = sw.ElapsedMilliseconds;
        }
        return o;
    }

    public static List<DiagLine> ParseSelf(string stderr)
    {
        var res = new List<DiagLine>();
        foreach (var raw in stderr.Split('\n'))
        {
            var m = SelfDiagRx.Match(raw.TrimEnd());
            if (m.Success)
            {
                res.Add(new DiagLine(m.Groups[3].Value, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[4].Value));
            }
        }
        return res;
    }

    public static string Norm(string msg)
    {
        string m = PathRx.Replace(msg, "<path>");
        m = QuotedRx.Replace(m, "'X'");
        m = NumRx.Replace(m, "N");
        m = m.Trim().ToLowerInvariant();
        return m.Length > 90 ? m[..90] : m;
    }

    private static string IdSet(IEnumerable<DiagLine> ds) => string.Join(",", ds.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));

    private static string FirstMsg(IEnumerable<DiagLine> ds) => ds.OrderBy(d => d.Line).ThenBy(d => d.Col).Select(d => Norm(d.Message)).FirstOrDefault() ?? "";

    private void ClassifyFrontends(Outcome o)
    {
        var s0 = o.Stage0!;
        var sdkr = o.Sdk!;
        var selfR = o.Self!;
        if (s0.Crash is not null)
        {
            o.Disagreements.Add(new("frontend-crash", "stage0", "", Norm(s0.Crash)));
        }
        if (sdkr.AnalyzerCrash is not null)
        {
            o.Disagreements.Add(new("frontend-crash", "analyzer", "", Norm(sdkr.AnalyzerCrash)));
        }
        if (selfR.TimedOut)
        {
            o.Disagreements.Add(new("frontend-crash", "self", "timeout", ""));
            return;
        }
        if (selfR.Exit is not (0 or 1))
        {
            string first = selfR.Stderr.Split('\n').FirstOrDefault(l => l.StartsWith("panic:", StringComparison.Ordinal)) ?? ("exit " + selfR.Exit);
            o.Disagreements.Add(new("frontend-crash", "self", "exit" + selfR.Exit, Norm(first)));
        }
        bool selfAcc = o.SelfAccepted;
        if (sdkr.Errors.Count > 0)
        {
            var acc = new List<string>();
            if (s0.Accepted)
            {
                acc.Add("stage0");
            }
            if (selfAcc)
            {
                acc.Add("self");
            }
            if (acc.Count > 0)
            {
                o.Disagreements.Add(new("invalid-accepted", string.Join("+", acc), IdSet(sdkr.Errors), ""));
            }
            return;
        }
        // C#-valid (possibly with warnings that TreatWarningsAsErrors would turn into errors)
        string an = IdSet(sdkr.Analyzer);
        bool s0Cs = !s0.Accepted && s0.Diags.Any(d => d.Id.StartsWith("CS", StringComparison.Ordinal));
        if (s0Cs)
        {
            o.Disagreements.Add(new("sdk-vs-stage0-csharp", "stage0", IdSet(s0.Diags), FirstMsg(s0.Diags)));
        }
        else if (s0.Crash is null)
        {
            string s0Ids = s0.Accepted ? "" : IdSet(s0.Diags);
            if (an != s0Ids)
            {
                o.Disagreements.Add(new("analyzer-vs-stage0", "analyzer+stage0", $"analyzer:{{{an}}} stage0:{{{s0Ids}}}", ""));
            }
        }
        // Decided parity rule: a program that contains a banned-by-design (or cut) construct must be rejected by
        // every frontend, and each frontend must report the documented ID of a banned construct it contains.
        // Secondary diagnostics after such a fail-closed subset rejection are not compared, so accept/reject and
        // ID-set comparison between Stage-0 and self apply only to programs without banned constructs.
        var bannedTags = o.Tags.Where(Features.Expected.ContainsKey).ToList();
        if (s0.Crash is null && !s0Cs && bannedTags.Count == 0)
        {
            if (s0.Accepted && !selfAcc && selfR.Exit == 1)
            {
                o.Disagreements.Add(new("accept-reject", "stage0-accepts", "self:" + IdSet(o.SelfDiags), FirstMsg(o.SelfDiags)));
            }
            else if (!s0.Accepted && selfAcc)
            {
                o.Disagreements.Add(new("accept-reject", "self-accepts", "stage0:" + IdSet(s0.Diags), FirstMsg(s0.Diags)));
            }
            else if (!s0.Accepted && !selfAcc && selfR.Exit == 1 && IdSet(s0.Diags) != IdSet(o.SelfDiags))
            {
                // keyed by the symmetric difference so reduction can drop the diagnostics both agree on
                var a = s0.Diags.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                var b = o.SelfDiags.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                string only0 = string.Join(",", a.Except(b).Order(StringComparer.Ordinal));
                string onlyS = string.Join(",", b.Except(a).Order(StringComparer.Ordinal));
                o.Disagreements.Add(new("id-mismatch", "stage0+self", $"stage0-only:{{{only0}}} self-only:{{{onlyS}}}", ""));
            }
        }
        if (bannedTags.Count == 0)
        {
            ClassifyExpectedReject(o, sdkr, s0, selfR, selfAcc, s0Cs);
            ClassifyExpectedAccept(o, sdkr, s0, selfR, selfAcc, s0Cs);
            return;
        }
        var expected = bannedTags.SelectMany(t => Features.Expected[t]).ToHashSet(StringComparer.Ordinal);
        string tagKey = string.Join("+", bannedTags.Order(StringComparer.Ordinal));
        var accepted = new List<string>();
        var wrong = new List<string>();
        void Judge(string fe, bool acc, IEnumerable<DiagLine> ds)
        {
            if (acc)
            {
                accepted.Add(fe);
            }
            else if (!ds.Any(d => expected.Contains(d.Id)))
            {
                wrong.Add(fe + ":" + IdSet(ds));
            }
        }
        Judge("analyzer", sdkr.Analyzer.Count == 0, sdkr.Analyzer);
        if (!s0Cs && s0.Crash is null)
        {
            Judge("stage0", s0.Accepted, s0.Diags);
        }
        if (selfR.Exit is 0 or 1)
        {
            Judge("self", selfAcc, o.SelfDiags);
        }
        if (accepted.Count > 0)
        {
            o.Disagreements.Add(new("banned-accepted", string.Join("+", accepted), tagKey, ""));
        }
        if (wrong.Count > 0)
        {
            o.Disagreements.Add(new("banned-wrong-id", tagKey, string.Join(" ", wrong), "expected " + string.Join("|", expected.Order(StringComparer.Ordinal))));
        }
        // C10–C21 policy: a rejecting frontend reports exactly one VIB000 per cut, with the exact message, at the
        // anchor of the cut's first occurrence. Frontends that accepted are already banned-accepted.
        var anchor = CutAnchorProblems(o, s0Cs);
        if (anchor.Count > 0)
        {
            o.Disagreements.Add(new("cut-anchor", string.Join("+", anchor.Select(a => a.Tag).Distinct().Order(StringComparer.Ordinal)), string.Join(" ", anchor.Select(a => a.Tag + ">" + a.Frontend + ":" + a.Got)), ""));
            o.CutAnchorDetail = string.Join(" ; ", anchor.Select(a => a.Tag + ">" + a.Frontend + ": " + a.Detail));
        }
    }

    /// <summary>
    /// For every C10–C21 cut in the program, every rejecting frontend must report exactly one VIB000 with the cut's
    /// exact message, at the anchor <see cref="CutScan"/> computed. Returns (tag, frontend, problem class: missing,
    /// duplicate or position, and the positions for the report).
    /// </summary>
    public static List<(string Tag, string Frontend, string Got, string Detail)> CutAnchorProblems(Outcome o, bool s0Cs)
    {
        var res = new List<(string, string, string, string)>();
        if (o.CutHits.Count == 0 || o.Sdk is null || o.Sdk.Errors.Count > 0)
        {
            return res;
        }
        var fes = new List<(string Name, bool Rejected, List<DiagLine> Diags)> { ("analyzer", o.Sdk.Analyzer.Count > 0, o.Sdk.Analyzer) };
        if (o.Stage0 is { Crash: null } && !s0Cs)
        {
            fes.Add(("stage0", !o.Stage0.Accepted, o.Stage0.Diags));
        }
        if (o.Self is { Exit: 1 })
        {
            fes.Add(("self", true, o.SelfDiags));
        }
        foreach (var h in o.CutHits)
        {
            foreach (var (fe, rejected, ds) in fes.Where(f => f.Rejected))
            {
                var mine = ds.Where(d => d.Id == "VIB000" && d.Message.Trim() == h.Message).ToList();
                if (mine.Count == 1 && mine[0].Line == h.Line && mine[0].Col == h.Col)
                {
                    continue;
                }
                string got = mine.Count == 0 ? "missing" : mine.Count > 1 ? "duplicate" : "position";
                string at = mine.Count == 0 ? "no VIB000 with the exact message" : "at " + string.Join(",", mine.Select(d => $"({d.Line},{d.Col})"));
                res.Add((h.Tag, fe, got, $"{at}, want ({h.Line},{h.Col})"));
            }
        }
        return res;
    }

    /// <summary>
    /// Expectation oracle for vib-reject tags (C#-valid shapes that SPEC rejects with a known ID): when every frontend
    /// accepts, SPEC is violated identically by all of them (<c>spec-accepted</c>), which differential comparison alone
    /// cannot see; when every frontend rejects but one lacks a tag's expected ID, <c>spec-wrong-id</c>. Partial
    /// acceptance is already an accept-reject or analyzer-vs-stage0 disagreement.
    /// </summary>
    private static void ClassifyExpectedReject(Outcome o, SdkOracle.Result sdkr, Stage0Oracle.Result s0, ProcResult selfR, bool selfAcc, bool s0Cs)
    {
        var rejectTags = o.Tags.Where(Features.ExpectedReject.ContainsKey).Order(StringComparer.Ordinal).ToList();
        if (rejectTags.Count == 0 || s0Cs || s0.Crash is not null || selfR.Exit is not (0 or 1))
        {
            return;
        }
        string tagKey = string.Join("+", rejectTags);
        bool anAcc = sdkr.Analyzer.Count == 0;
        if (anAcc && s0.Accepted && selfAcc)
        {
            string exp = string.Join(" ", rejectTags.Select(t => t + ":" + string.Join("|", Features.ExpectedReject[t])));
            o.Disagreements.Add(new("spec-accepted", "analyzer+stage0+self", tagKey, "expected " + exp));
            return;
        }
        if (anAcc || s0.Accepted || selfAcc)
        {
            return;
        }
        var wrong = new List<string>();
        foreach (var t in rejectTags)
        {
            var exp = Features.ExpectedReject[t];
            void Judge(string fe, IEnumerable<DiagLine> ds)
            {
                if (!ds.Any(d => exp.Contains(d.Id, StringComparer.Ordinal)))
                {
                    wrong.Add(t + ">" + fe + ":" + IdSet(ds));
                }
            }
            Judge("analyzer", sdkr.Analyzer);
            Judge("stage0", s0.Diags);
            Judge("self", o.SelfDiags);
        }
        if (wrong.Count > 0)
        {
            o.Disagreements.Add(new("spec-wrong-id", tagKey, string.Join(" ", wrong), "expected " + string.Join(" ", rejectTags.Select(t => t + ":" + string.Join("|", Features.ExpectedReject[t])))));
        }
    }

    /// <summary>
    /// release-7.1 expectation oracle for the accepted shapes of the ViewMove anchor (<see cref="Features.ExpectedAccept"/>):
    /// in a C#-valid program without any vib-reject tag, every ViewMove helper is an accepted shape, so a diagnostic on a
    /// helper's line is wrong. When every frontend reports one there (<c>spec-rejected</c>), SPEC is violated identically
    /// by all of them, a shared over-rejection such as the N17 class in both code bases, which differential comparison
    /// alone cannot see. Diagnostics elsewhere (other anchors, near-miss mutations) do not count.
    /// </summary>
    private static void ClassifyExpectedAccept(Outcome o, SdkOracle.Result sdkr, Stage0Oracle.Result s0, ProcResult selfR, bool selfAcc, bool s0Cs)
    {
        var acceptTags = o.Tags.Where(Features.ExpectedAccept.Contains).Order(StringComparer.Ordinal).ToList();
        if (acceptTags.Count == 0 || s0Cs || s0.Crash is not null || selfR.Exit != 1 || s0.Accepted || selfAcc
            || sdkr.Analyzer.Count == 0 || sdkr.Warnings.Count > 0 || o.Tags.Any(Features.ExpectedReject.ContainsKey))
        {
            return;
        }
        var lines = new HashSet<int>();
        string[] src = o.Source.Split('\n');
        for (int i = 0; i < src.Length; i++)
        {
            if (ViewMoveHelperRx.IsMatch(src[i]))
            {
                lines.Add(i + 1);
            }
        }
        static string On(IEnumerable<DiagLine> ds, HashSet<int> ls) =>
            string.Join(",", ds.Where(d => ls.Contains(d.Line)).Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));
        string an = On(sdkr.Analyzer, lines), st = On(s0.Diags, lines), sf = On(o.SelfDiags, lines);
        if (an.Length > 0 && st.Length > 0 && sf.Length > 0)
        {
            o.Disagreements.Add(new("spec-rejected", "analyzer+stage0+self", $"{string.Join("+", acceptTags)} analyzer:{{{an}}} stage0:{{{st}}} self:{{{sf}}}", "expected accept"));
        }
    }

    /// <summary>
    /// The emitted IR of both native compilers must mask shift counts to the width minus one, guard every division by
    /// a non-constant divisor with a zero test and every signed division that can overflow with a -1/MIN test
    /// (<see cref="IrContract"/>, SPEC §5.3). A violation is an <c>ir-contract</c> disagreement keyed by the rule; the
    /// first offending instruction goes into the oracle file.
    /// </summary>
    private void CheckIrContract(Outcome o, string path)
    {
        string? s0ir = o.Stage0?.Module is { } mod ? stage0.EmitIr(mod) : null;
        string llPath = Path.Combine(workDir, "contract.ll");
        File.Delete(llPath);
        var se = self.EmitLl(path, llPath, false);
        string? selfIr = se.Exit == 0 && File.Exists(llPath) ? File.ReadAllText(llPath) : null;
        File.Delete(llPath);
        foreach (var (fe, ir) in new[] { ("stage0", s0ir), ("self", selfIr) })
        {
            if (ir is null)
            {
                o.Legs[fe + "-ir"] = "not emitted";
                continue;
            }
            var v = IrContract.Check(ir);
            o.Legs[fe + "-ir-contract"] = v.Count == 0 ? "OK" : string.Join(" ; ", v.Select(x => x.Rule + ": " + Trunc(x.Example)));
            if (v.Count > 0)
            {
                o.Disagreements.Add(new("ir-contract", fe, string.Join(",", v.Select(x => x.Rule).Order(StringComparer.Ordinal)), ""));
            }
        }
    }

    /// <summary>The long budget of a native leg that exceeded the 20 s run budget while the managed run finished.</summary>
    private const int SlowLegBudgetMs = 200_000;

    /// <summary>
    /// release-7.1: a native leg of the same program that matched the managed run in under this time shows that the
    /// program is not slow, so another leg that exceeds the 20 s budget hangs and gets no slow re-run.
    /// </summary>
    private const int FastLegEvidenceMs = 2_000;

    private void Execute(Outcome o, string path, ExecPlan plan)
    {
        o.Executed = true;
        string dll = Path.Combine(workDir, "Program.dll");
        if (!_managedLibsCopied)
        {
            File.Copy(tc.CoreDll, Path.Combine(workDir, "Vibranium.Core.dll"), overwrite: true);
            File.Copy(tc.StdDll, Path.Combine(workDir, "Vibranium.Std.dll"), overwrite: true);
            File.WriteAllText(Path.Combine(workDir, "Program.runtimeconfig.json"), """{"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true,"System.IO.DisableFileLocking":true}}}""");
            _managedLibsCopied = true;
        }
        string? emitErr = SdkOracle.Emit(o.Sdk!.Compilation, dll);
        if (emitErr is not null)
        {
            o.Legs["managed-emit"] = "FAIL " + emitErr;
            o.Disagreements.Add(new("managed-crash", "managed", "emit", Norm(emitErr)));
            return;
        }
        var managed = Proc.Run("dotnet", [dll], 20_000, workDir);
        o.Runs["managed"] = managed;
        if (managed.TimedOut)
        {
            // the reference run does not finish: the generated program does not terminate (for example a nested
            // anchor writes the loop counter). Partial outputs are not comparable, so this is not a disagreement.
            o.NonTerminating = true;
            o.Legs["managed"] = "TIMEOUT: non-terminating program, native legs skipped";
            return;
        }
        if (managed.Exit == 134 && managed.Stderr.StartsWith("Stack overflow", StringComparison.Ordinal))
        {
            // unbounded recursion: call-stack exhaustion is excluded from the parity guarantee (SPEC scope), and
            // the runtimes end it differently (CLR abort 134, native SIGSEGV 139), so the native legs are skipped
            o.NonTerminating = true;
            o.Legs["managed"] = "STACK OVERFLOW: call-stack exhaustion is outside the parity guarantee, native legs skipped";
            return;
        }
        if (managed.Exit is not (0 or 101))
        {
            o.Disagreements.Add(new("managed-crash", "managed", "exit" + managed.Exit, Norm(managed.Stderr.Split('\n')[0])));
        }
        var legs = plan == ExecPlan.AsanO0AndO3
            ? new List<(string Name, bool Asan, int Opt, bool IsSelf)> { ("stage0-O0-asan", true, 0, false), ("self-O0-asan", true, 0, true) }
            : [("stage0-O0", false, 0, false), ("self-O0", false, 0, true)];
        if (plan is ExecPlan.O0AndO3Asan or ExecPlan.AsanO0AndO3)
        {
            legs.Add(("stage0-O3-asan", true, 3, false));
            legs.Add(("self-O3-asan", true, 3, true));
        }
        var mismatches = new List<(string Leg, string Aspect)>();
        (string Leg, long Ms)? fastMatch = null;
        foreach (var (name, asan, opt, isSelf) in legs)
        {
            string bin = Path.Combine(workDir, name + ".bin");
            File.Delete(bin);
            bool ok;
            string log;
            if (isSelf)
            {
                // the real `vib-self build` (in process): its own clang invocation and link flags
                var b = self.Build(path, bin, opt, asan);
                ok = b.Exit == 0 && File.Exists(bin);
                log = b.Stderr + b.Stdout;
            }
            else
            {
                (ok, log) = stage0.Build(o.Stage0!.Module!, bin, opt, asan);
            }
            if (!ok)
            {
                o.Legs[name + "-build"] = "FAIL " + Trunc(log);
                o.Disagreements.Add(new("native-build-failure", name, "", NativeFailureKey(log)));
                continue;
            }
            if (!asan)
            {
                var needed = Needed(bin);
                o.Legs[name + "-needed"] = string.Join(" ", needed);
                var extra = needed.Where(n => !n.StartsWith("libc.so", StringComparison.Ordinal) && !n.StartsWith("ld-linux", StringComparison.Ordinal)).ToList();
                if (extra.Count > 0)
                {
                    o.Disagreements.Add(new("link-contract", name, string.Join(",", extra), "dt_needed beyond libc"));
                }
            }
            var run = Proc.Run(bin, [], 20_000, workDir);
            o.Runs[name] = run;
            if (managed.Exit is 0 or 101 && (run.Exit == 139 || run.Stderr.Contains("AddressSanitizer: stack-overflow", StringComparison.Ordinal)))
            {
                // a SIGSEGV (or an ASan stack-overflow) where the managed run finished: re-run with an unlimited stack.
                // If the leg then matches the managed run, the only difference is call-stack exhaustion at the 8 MiB
                // default (native frames differ in size between Stage-0, self, O0 and O3), which SPEC excludes from the
                // parity guarantee. The leg is excused and listed (never skipped silently); any other SIGSEGV stays.
                var big = Proc.Run("/bin/sh", ["-c", "ulimit -s unlimited && exec \"$0\"", bin], 20_000, workDir);
                if (!big.TimedOut && big.Exit == managed.Exit && big.Stdout == managed.Stdout && big.Stderr == managed.Stderr)
                {
                    o.StackExhaustedLegs.Add(name);
                    o.Legs[name] = "STACK EXHAUSTION at the 8 MiB default stack; with an unlimited stack the leg matches the managed run";
                    File.Delete(bin);
                    continue;
                }
            }
            if (managed.Exit is 0 or 101 && run.TimedOut && fastMatch is { } fm)
            {
                // release-7.1: another native leg of this program matched the managed run quickly, so this leg hangs
                // (the N19 class made self loop): no slow re-run, which would cost the 200 s budget per leg
                o.Legs[name] = $"TIMEOUT: over the 20 s budget while {fm.Leg} matched the managed run in {fm.Ms} ms (no slow re-run)";
            }
            else if (managed.Exit is 0 or 101 && run.TimedOut)
            {
                // release-7: the managed run finished but the native leg exceeded the 20 s budget. A long-running
                // program (for example a loop that ends only when an int counter wraps) can need a few seconds under
                // the optimizing CLR JIT and more than 20 s as an unoptimized -O0 binary. Re-run the leg once with
                // the long budget; if it then matches the managed run, the only difference is speed, which is not
                // part of the parity guarantee: the leg is excused and listed (never skipped silently).
                var slow = Proc.Run(bin, [], SlowLegBudgetMs, workDir);
                if (!slow.TimedOut && slow.Exit == managed.Exit && slow.Stdout == managed.Stdout && slow.Stderr == managed.Stderr)
                {
                    o.SlowLegs.Add(name);
                    o.Runs[name] = slow;
                    o.Legs[name] = $"SLOW: over the 20 s budget; with {SlowLegBudgetMs / 1000} s it matches the managed run ({slow.Ms} ms native, {managed.Ms} ms managed)";
                    File.Delete(bin);
                    continue;
                }
                run = slow;
                o.Runs[name] = slow;
            }
            File.Delete(bin);
            if (run.Stderr.Contains("AddressSanitizer", StringComparison.Ordinal) || run.Stderr.Contains("LeakSanitizer", StringComparison.Ordinal))
            {
                var m = Regex.Match(run.Stderr, @"(?:AddressSanitizer|LeakSanitizer): ([a-z\-]+(?: [a-z\-]+)?)");
                o.Disagreements.Add(new("sanitizer", name, m.Success ? m.Groups[1].Value : "report", ""));
                continue;
            }
            if (run.TimedOut)
            {
                o.Disagreements.Add(new("run-timeout", name, "", ""));
                continue;
            }
            var aspects = new List<string>();
            if (run.Exit != managed.Exit)
            {
                aspects.Add("exit");
            }
            if (run.Stdout != managed.Stdout)
            {
                aspects.Add("stdout");
            }
            if (run.Stderr != managed.Stderr)
            {
                aspects.Add("stderr");
            }
            if (aspects.Count > 0)
            {
                mismatches.Add((name, string.Join("+", aspects)));
            }
            else if (run.Ms < FastLegEvidenceMs && fastMatch is null)
            {
                fastMatch = (name, run.Ms);
            }
        }
        foreach (var g in mismatches.GroupBy(m => m.Aspect))
        {
            string fes = string.Join("+", g.Select(x => x.Leg.Split('-')[0]).Distinct());
            o.Disagreements.Add(new("output-mismatch", fes, g.Key, string.Join(",", g.Select(x => x.Leg))));
        }
    }

    /// <summary>Stable key for a native build failure: the undefined symbol, or the first error line without temp names/offsets.</summary>
    public static string NativeFailureKey(string log)
    {
        var undef = Regex.Matches(log, @"undefined reference to [`']([^'`]+)'").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();
        if (undef.Count > 0)
        {
            return "undefined reference to " + string.Join(",", undef);
        }
        string first = log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? log.Split('\n')[0];
        first = Regex.Replace(first, @"[\w.\-/]*\.(ll|o|bin)\b", "<file>");
        first = Regex.Replace(first, @"\(\.[a-z]+\+0x[0-9a-f]+\)", "");
        return Norm(first);
    }

    private static string Trunc(string s) => s.Length > 1500 ? s[..1500] + "..." : s;

    private List<string> Needed(string bin)
    {
        var r = Proc.Run(tc.ReadElf, ["-d", bin], 20_000);
        var res = new List<string>();
        foreach (Match m in Regex.Matches(r.Stdout, @"\(NEEDED\)\s+Shared library: \[([^\]]+)\]"))
        {
            res.Add(m.Groups[1].Value);
        }
        return res;
    }
}
