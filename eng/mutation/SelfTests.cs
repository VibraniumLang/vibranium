using System;
using System.IO;
using System.Linq;

namespace Vibranium.Mutation;

internal static class SelfTests
{
    public static int Run(Oracles oracles, SeverityTable severities)
    {
        int assertions = 0;
        void Check(bool value, string description)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Oracle self-test failed: " + description);
        }
        Check(Oracles.ParseSelfResult(0, "").Status == "accept", "self exit-zero success");
        Check(Oracles.ParseSelfResult(1, "Program.cs(1,1): error VIB000: rejected").Status == "reject", "self real rejection");
        Check(Oracles.ParseSelfResult(134, "Program.cs(1,1): error VIB000: rejected").Status == "failure", "self crash after diagnostic is not rejection");
        Check(Oracles.ParseSelfResult(1, "could not load file VIB000.dll").Status == "failure", "VIB token in infrastructure text is not diagnostic");
        Check(Oracles.ParseSelfResult(0, "Program.cs(1,1): error VIB000: rejected").Status == "failure", "success plus diagnostic is inconsistent");
        var valid = oracles.Sdk("WriteLine(1); return 0;");
        Check(valid.Errors.Length == 0, "app implicit usings and bootstrap compile");
        var validAnalyzer = Oracles.Analyzer(valid);
        Check(validAnalyzer.Status == "accept" && validAnalyzer.Ids.Length == 0, "in-process Analyzer accepts a valid app (trusted bootstrap source)");
        var charError = oracles.Sdk("char c = 65; WriteLine((int)c); return 0;");
        Check(charError.Errors.Any(e => e.Id == "CS0266" && !e.WarningAsError && e.Promotion == "error"), "implicit int-to-char error");
        var warning = oracles.Sdk("int unused = 1; return 0;");
        Check(warning.Errors.Any(e => e.Id == "CS0219" && e.WarningAsError && e.Promotion == "blanket"), "TreatWarningsAsErrors promotion");
        Check(Classification.ErrorKind(warning.Errors) == "blanket-warning", "blanket-warning program kind");
        var nonExhaustive = oracles.Sdk("int a = 1; WriteLine(a switch { 1 => 2 }); return 0;");
        Check(nonExhaustive.Errors.Any(e => e.Id == "CS8509" && e.Promotion == "explicit"), "App.props explicit promotion");
        Check(severities.For("CS0266") == SeverityTable.Safety && severities.For("CS0101") == SeverityTable.Other, "severity tables loaded");
        Check(severities.For("CS99999") == SeverityTable.Unclassified && severities.SafetyIds(charError.Errors).Contains("CS0266"), "unclassified IDs are treated as safety-relevant");
        // CS0649 keys follow Stage0's zero-safety (triage only; CS0649 alone is warning-only parity input).
        foreach (string zeroSafe in new[]
        {
            "WriteLine(new C().Get()); return 0; class C { private int _n; public int Get() => _n; }",
            "WriteLine(new C().IsSome()); return 0; class C { private Option<Node> value; public bool IsSome() => value.IsSome; } class Node { }",
            "WriteLine(new C().Get()); return 0; struct P { public int X; public long Y; } class C { private P _p; public int Get() => _p.X; }",
        })
        {
            var unassigned = oracles.Sdk(zeroSafe);
            Check(unassigned.Errors.Length > 0 && Classification.Bucket(unassigned.Errors[0]) == "CS0649.zero-safe"
                && severities.For("CS0649.zero-safe") == SeverityTable.Other && Classification.ErrorKind(unassigned.Errors) == "blanket-warning",
                "CS0649 on a zero-safe field is keyed CS0649.zero-safe (other) and is warning-only parity input: " + zeroSafe);
        }
        foreach (string hazard in new[]
        {
            "WriteLine(new C().Get()); return 0; class C { private string _s; public C() { } public string Get() => _s; }",
            "WriteLine(new C().Get()); return 0; struct P { public string S; } class C { private P _p; public C() { } public string Get() => _p.S; }",
        })
        {
            var unassigned = oracles.Sdk(hazard);
            Check(unassigned.Errors.Any(e => e.Id == "CS0649" && Classification.Bucket(e) == "CS0649")
                && severities.SafetyIds(unassigned.Errors).Contains("CS0649"), "CS0649 on a non-zero-safe field is keyed CS0649 (safety): " + hazard);
        }
        Check(Classification.Bucket(new CsError("CS0649", 0, 1, 1, "m", true, "blanket")) == "CS0649", "CS0649 without zero-safety keys as CS0649");
        var warningOnly = new Finding(1, "origin", "group", "operator", "hash", "CS8619", [], new FrontendResult("accept", [], 0),
            new FrontendResult("accept", [], 0), new FrontendResult("reject", ["CS8619"], 1), [], SeverityTable.Safety, ["CS8619"],
            "blanket-warning", "path", 1);
        Check(!SeverityTable.IsDefect(warningOnly) && SeverityTable.IsWarningsOnly(warningOnly)
            && SeverityTable.IsDefect(warningOnly with { ErrorKind = "explicit-warning" })
            && !SeverityTable.IsWarningsOnly(warningOnly with { ErrorKind = "explicit-warning" }),
            "blanket-only warnings are reported, not defects; an explicitly promoted warning is C#-invalid");
        var lowRiskError = warningOnly with { FirstCsId = "CS0101", Severity = SeverityTable.Other, ErrorKind = "error" };
        Check(SeverityTable.IsDefect(lowRiskError) && SeverityTable.IsDefect(warningOnly with { ErrorKind = "error" }),
            "any accept of a genuine C# error is a defect, whatever its severity class");
        Check(SeverityTable.IsSubsetRejection(lowRiskError with { Self = new FrontendResult("reject", ["VIB000"], 1), Cuts = ["C12"] })
            && !SeverityTable.IsSubsetRejection(lowRiskError with { Self = new FrontendResult("reject", ["VIB000"], 1) }), "subset rejections need a cut form");
        var optional = oracles.Sdk("WriteLine(F(1, 2)); return 0; static int F(int a = 1, int b) => a + b;");
        Check(optional.Errors.Any(e => e.Id == "CS1737"), "optional-before-required error");
        Check(Classification.Cuts(optional).Contains("C12"), "optional parameter cut");
        var writeIn = oracles.Sdk("int a = 1; F(in a); WriteLine(a); return 0; static void F(in int x) { x = 2; }");
        Check(writeIn.Errors.Any(e => e.Id == "CS8331"), "write-through-in error");
        Check(!Classification.Cuts(writeIn).Contains("C10"), "in parameter is not ref-readonly cut");
        var writeThis = oracles.Sdk("var c = new C(); c.M(); return 0; class C { [Mut] public void M() { this = new C(); } }");
        Check(Classification.Cuts(writeThis).Contains("C14"), "this-assignment cut");
        var mutableCondition = oracles.Sdk("int a = 1; if (a == 1) WriteLine(a); return 0;");
        Check(!Classification.Cuts(mutableCondition).Contains("C19"), "nonconstant condition not over-tagged");
        var constantCondition = oracles.Sdk("if (1 == 1) WriteLine(1); return 0;");
        Check(Classification.Cuts(constantCondition).Contains("C19"), "semantic constant condition cut");
        var infinite = oracles.Sdk("while (true) { WriteLine(1); }");
        Check(!Classification.Cuts(infinite).Contains("C19"), "literal infinite loop exempt from cut");
        Check(!Classification.Cuts(oracles.Sdk("int IntPtr = 1; WriteLine(IntPtr); return 0;")).Contains("C17"), "ordinary IntPtr identifier is not native type");
        Check(Classification.Cuts(oracles.Sdk("nint x = 1; WriteLine((long)x); return 0;")).Contains("C17"), "native integer type is tagged");
        Check(!Classification.Cuts(oracles.Sdk("int a = 1; switch (a) { case 1: { int x = 2; WriteLine(x); break; } } return 0;")).Contains("C18"), "nested switch block scope is retained");
        Check(Classification.Cuts(oracles.Sdk("int a = 1; switch (a) { case 1: int x = 2; WriteLine(x); break; } return 0;")).Contains("C18"), "direct switch declaration is tagged");
        const string original = "var c = new C(); WriteLine(c.F(1, 2)); return 0; class C { public int N = 1; public int F(int a, int b) { int x = a + b; N = x; return N; } }";
        var mutations = Mutations.Create(original, 2).ToArray();
        Check(mutations.Length > 30, "operator diversity");
        Check(mutations.Any(m => m.Operator == "field-private"), "accessibility mutation");
        Check(mutations.Any(m => m.Operator == "delete-return"), "return deletion mutation");
        Check(mutations.All(m => m.Apply(original) != original), "no identity edits");
        Check(mutations.SequenceEqual(Mutations.Create(original, 2)), "deterministic mutation enumeration");
        Check(mutations.GroupBy(m => m.Operator).All(g => g.Count() <= 2), "per-operator budget");
        const string library = "Vec<int> v = [1, 2]; WriteLine(v.Count); return 0;";
        var catalog = new MemberCatalog(oracles.Sdk("return 0;").Compilation);
        Check(catalog.HiddenStaticCalls.Length > 0, "internal Std/Core static calls catalogued");
        var semantic = Mutations.Create(library, 50, oracles.Sdk(library), catalog);
        Check(semantic.Any(m => m.Operator.StartsWith("sem-internal-member:", StringComparison.Ordinal) && m.Replacement == "_count"), "private Vec<T> member reachable by a mutant");
        Check(semantic.Any(m => m.Operator == "sem-write-getonly-property"), "get-only property write mutant");
        var hidden = semantic.First(m => m.Replacement == "_count").Apply(library);
        Check(oracles.Sdk(hidden).Errors.FirstOrDefault()?.Id == "CS0122", "private Std member access is CS0122");
        string scratch = Path.Combine(Path.GetTempPath(), "vib-mutation-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            string testsDirectory = Path.Combine(scratch, "tests", "Vibranium.Tests");
            string probeDirectory = Path.Combine(scratch, "probes");
            Directory.CreateDirectory(testsDirectory);
            Directory.CreateDirectory(probeDirectory);
            const string repro = "int k = 3; if (k is _) WriteLine(1);";
            File.WriteAllText(Path.Combine(testsDirectory, "T.cs"), "class T { string S = \"" + repro + "\"; }");
            File.WriteAllText(Path.Combine(probeDirectory, "p.cs"), repro);
            File.WriteAllText(Path.Combine(probeDirectory, "q.cs"), repro);
            // corpus/pending/ holds repros whose fix has not landed yet: the gate reads only the top level.
            Directory.CreateDirectory(Path.Combine(probeDirectory, "pending"));
            const string pending = "int k = 4; if (k is _) WriteLine(2);";
            File.WriteAllText(Path.Combine(probeDirectory, "pending", "r.cs"), pending);
            var read = Corpus.Read(scratch, probeDirectory, 16000);
            Check(read.Any(p => p.Group == "probes" && p.Source == repro) && read.Any(p => p.Group == "tests" && p.Source == repro),
                "a corpus probe whose text is also a test literal still runs as a probe");
            Check(read.Count(p => p.Group == "probes" && p.Source == repro) == 1, "identical probes run once");
            Check(!read.Any(p => p.Source == pending), "corpus/pending/ is not read as probes");
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
        Check(Files.Hash(original).Length == 64, "SHA-256 provenance");
        Console.WriteLine($"PASS {assertions} mutation-oracle assertions");
        return 0;
    }
}
