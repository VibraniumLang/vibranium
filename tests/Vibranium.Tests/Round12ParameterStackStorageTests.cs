using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

// Root N16. A collection expression assigned directly to a by-value parameter gets storage owned by the parameter's
// lexical scope, the method body (SPEC §3.7). Returning a view of that storage is VIB003 (the storage exits scope while
// the returned view still borrows it) and VIB007 (the view escapes the method), exactly as for a local destination,
// on the Analyzer, Stage-0 and vib-self alike. Accepted neighbours run on all three frontends and compare the managed
// run with the Stage-0 and vib-self native binaries (-O0, and -O3 with ASan/LSan).
public sealed class Round12ParameterStackStorageTests
{
    private const string Ros = "ReadOnlySpan<int> x = [5, 6, 7];\n";
    private const string Ros2 = "ReadOnlySpan<int> x = [5, 6, 7];\nReadOnlySpan<int> y = [8, 9];\n";

    private static readonly Dictionary<string, string> Rejected = new(StringComparer.Ordinal)
    {
        // The four conformance probes of N16 (s3p_01, s3p_04, s3p_05, s3p_09).
        ["assigned-then-returned"] = Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n",
        ["assigned-on-one-branch"] = Ros + "WriteLine(G(x, 2)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k)\n{\n    if (k > 1)\n    {\n        p = [1, 2];\n    }\n    return p;\n}\n",
        ["assigned-then-slice-returned"] = Ros + "WriteLine(G(x)[0]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2, 3]; return p[1..]; }\n",
        ["empty-assigned-then-returned"] = Ros + "WriteLine(G(x).Length);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = []; return p; }\n",
        // Neighbours of the same shape.
        ["span-parameter-empty"] = "Span<int> x = [5, 6, 7];\nWriteLine(G(x).Length);\nstatic Span<int> G(Span<int> p) { p = []; return p; }\n",
        ["assigned-in-loop"] = Ros + "WriteLine(G(x, 2)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k)\n{\n    while (k > 0)\n    {\n        p = [1, 2];\n        k--;\n    }\n    return p;\n}\n",
        // The value of an assignment expression whose destination is the parameter (SPEC §3.7 "return assignment").
        ["return-assignment"] = Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { return p = [1, 2]; }\n",
        ["expression-bodied-assignment"] = Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) => p = [1, 2];\n",
        ["chained-assignment-into-local"] = Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { ReadOnlySpan<int> q = p = [1, 2]; return q; }\n",
        ["return-assignment-sliced"] = Ros + "WriteLine(G(x)[0]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { return (p = [1, 2, 3])[1..]; }\n",
        ["span-parameter-return-assignment-empty"] = "Span<int> x = [5, 6, 7];\nWriteLine(G(x).Length);\nstatic Span<int> G(Span<int> p) { return p = []; }\n",
        ["copied-to-local-then-returned"] = Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; ReadOnlySpan<int> q = p; return q; }\n",
        ["returned-from-nested-scope"] = Ros + "WriteLine(G(x, 2)[0]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k)\n{\n    p = [1, 2, 3];\n    for (int i = 0; i < k; i++)\n    {\n        int j = i;\n        if (j > 0)\n        {\n            return p;\n        }\n    }\n    return p[1..];\n}\n",
        ["lifetimeof-parameter"] = Ros2 + "WriteLine(G(x, y)[1]);\n[return: LifetimeOf(\"p\")]\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, ReadOnlySpan<int> q) { p = [1, 2]; WriteLine(q[0]); return p; }\n",
        ["local-function-in-method"] = "WriteLine(H.M());\nstatic class H\n{\n    public static int M()\n    {\n        ReadOnlySpan<int> x = [5, 6, 7];\n        return L(x)[1];\n        static ReadOnlySpan<int> L(ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n    }\n}\n",
        ["static-method-called-from-top-level"] = Ros + "WriteLine(H.G(x)[1]);\nstatic class H\n{\n    public static ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n}\n",
        ["instance-method"] = Ros + "var h = new H();\nWriteLine(h.G(x)[1]);\nsealed class H\n{\n    [return: LifetimeOf(\"p\")]\n    public ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n}\n",
        ["ref-to-element-returned"] = Ros + "WriteLine(G(x));\nstatic ref readonly int G(ReadOnlySpan<int> p) { p = [1, 2]; return ref p[0]; }\n",
        // Already {VIB003, VIB007} before N16: the storage is owned by a local (s3p_10).
        ["local-destination"] = Ros + "WriteLine(G(x, 2)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k) { ReadOnlySpan<int> q = [1, 2]; p = k > 1 ? q : p; return p; }\n",
    };

    // Storage created anonymously inside a larger expression (here an argument) lives for the whole method frame
    // (SPEC §3.7): returning it is VIB007 only, also when the result is stored in a parameter.
    private static readonly Dictionary<string, string> FrameOwned = new(StringComparer.Ordinal)
    {
        ["argument-storage-through-parameter"] = Ros + "WriteLine(G(x)[0]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = Id([7]); return p; }\nstatic ReadOnlySpan<int> Id(ReadOnlySpan<int> v) => v;\n",
    };

    private static readonly Dictionary<string, (string Source, string Output)> Accepted = new(StringComparer.Ordinal)
    {
        // s3p_06: the parameter's own provenance may be returned.
        ["slice-of-parameter-returned"] = (Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { return p[1..]; }\n", "7\n"),
        ["reassigned-from-own-slice"] = (Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = p[1..]; return p; }\n", "7\n"),
        ["reassigned-from-other-parameter"] = (Ros2 + "WriteLine(G(x, y)[1]);\n[return: LifetimeOf(\"p\", \"q\")]\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, ReadOnlySpan<int> q) { p = q; return p; }\n", "9\n"),
        ["stack-storage-replaced-before-return"] = (Ros2 + "WriteLine(G(x, y)[1]);\n[return: LifetimeOf(\"p\", \"q\")]\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, ReadOnlySpan<int> q) { p = [1, 2]; WriteLine(p[0]); p = q; return p; }\n", "1\n9\n"),
        ["utf8-literal-returned"] = ("ReadOnlySpan<byte> x = \"xyz\"u8;\nWriteLine(G(x).Length);\nstatic ReadOnlySpan<byte> G(ReadOnlySpan<byte> p) { p = \"ab\"u8; return p; }\n", "2\n"),
        ["utf8-literal-on-one-branch"] = ("ReadOnlySpan<byte> x = \"xyz\"u8;\nWriteLine(G(x, 2).Length);\nstatic ReadOnlySpan<byte> G(ReadOnlySpan<byte> p, int k)\n{\n    if (k > 1)\n    {\n        p = \"ab\"u8;\n    }\n    return p[1..];\n}\n", "1\n"),
        ["element-returned"] = (Ros + "WriteLine(G(x));\nstatic int G(ReadOnlySpan<int> p) { p = [1, 2]; return p[1]; }\n", "2\n"),
        ["view-local-used-before-return"] = (Ros + "WriteLine(G(x));\nstatic int G(ReadOnlySpan<int> p) { p = [1, 2, 3]; ReadOnlySpan<int> q = p[1..]; return q[1]; }\n", "3\n"),
        ["foreach-return-of-element"] = (Ros + "WriteLine(G(x));\nstatic int G(ReadOnlySpan<int> p) { p = [1, 2, 3]; foreach (var v in p) { if (v > 1) { return v; } } return 0; }\n", "2\n"),
        ["assigned-in-loop-not-returned"] = (Ros + "WriteLine(G(x, 3));\nstatic int G(ReadOnlySpan<int> p, int k)\n{\n    int s = 0;\n    for (int i = 0; i < k; i++)\n    {\n        s += p[0];\n        p = [1, 2];\n    }\n    return s;\n}\n", "7\n"),
        ["scoped-parameter-not-returned"] = (Ros + "WriteLine(G(x));\nstatic int G(scoped ReadOnlySpan<int> p) { p = [1, 2]; return p[1]; }\n", "2\n"),
        ["scoped-parameter-nonconstant-elements"] = (Ros + "WriteLine(G(x, 4));\nstatic int G(scoped ReadOnlySpan<int> p, int k) { p = [k, 2]; return p[0] + p[1]; }\n", "6\n"),
        ["in-parameter-slice-returned"] = (Ros + "WriteLine(G(in x)[1]);\nstatic ReadOnlySpan<int> G(in ReadOnlySpan<int> p) { return p[1..]; }\n", "7\n"),
        ["scoped-in-parameter-copied"] = (Ros + "WriteLine(G(in x));\nstatic int G(scoped in ReadOnlySpan<int> p) { ReadOnlySpan<int> q = p; return q[1]; }\n", "6\n"),
        ["ref-to-parameter-element-returned"] = (Ros + "WriteLine(G(x));\nstatic ref readonly int G(ReadOnlySpan<int> p) { return ref p[1]; }\n", "6\n"),
        // The value of an assignment expression into a parameter, used inside the method: Stage-0 now stores the
        // collection expression into the parameter and snapshots it (CfgMirLowerer.LowerSimpleAssignment).
        ["assignment-value-used-before-return"] = (Ros + "WriteLine(G(x));\nstatic int G(ReadOnlySpan<int> p) { return (p = [1, 2])[1] + p[0]; }\n", "3\n"),
        ["scoped-parameter-assignment-value"] = (Ros + "WriteLine(G(x));\nstatic int G(scoped ReadOnlySpan<int> p) { return (p = [1, 2])[1] + p[0]; }\n", "3\n"),
        ["chained-assignment-used"] = (Ros + "WriteLine(G(x));\nstatic int G(ReadOnlySpan<int> p) { ReadOnlySpan<int> q = p = [4, 5]; return q[0] + p[1]; }\n", "9\n"),
        ["assignment-value-as-argument"] = (Ros + "WriteLine(G(x));\nstatic int G(ReadOnlySpan<int> p) { return Sum(p = [1, 2, 3]) + p.Length; }\nstatic int Sum(ReadOnlySpan<int> v) { int s = 0; foreach (var e in v) { s += e; } return s; }\n", "9\n"),
        ["assignment-value-in-loop"] = (Ros + "WriteLine(G(x, 3));\nstatic int G(ReadOnlySpan<int> p, int k)\n{\n    int s = 0;\n    for (int i = 0; i < k; i++)\n    {\n        s += (p = [2, 1])[0] + p.Length;\n    }\n    return s + p[1];\n}\n", "13\n"),
    };

    // Programs with a construct the subset bans: every frontend reports that construct's VIB000; the diagnostics after
    // it may differ between compilers (SPEC §4), so the Analyzer and Stage-0 must agree and vib-self must report VIB000.
    // Where all three already agreed before N16, the exact set is pinned too: destinations that are not by-value
    // parameters (a ref struct field, a ref or out view parameter, a ref view local) keep their old lowering.
    private static readonly Dictionary<string, (string Source, string? Exact)> Banned = new(StringComparer.Ordinal)
    {
        ["user-ref-struct-span-field"] = (Ros + "WriteLine(G(new R(x))[1]);\nstatic ReadOnlySpan<int> G(R r) { r.S = [1, 2]; return r.S; }\nref struct R { public ReadOnlySpan<int> S; public R(ReadOnlySpan<int> s) { S = s; } }\n", null),
        ["non-static-local-function"] = (Ros + "WriteLine(G(x)[1]);\nReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n", null),
        ["user-ref-struct-field-assignment-value"] = (Ros + "WriteLine(G(new R(x))[1]);\nstatic ReadOnlySpan<int> G(R r) { return r.S = [1, 2]; }\nref struct R { public ReadOnlySpan<int> S; public R(ReadOnlySpan<int> s) { S = s; } }\n", null),
        ["ref-view-parameter-assigned"] = (Ros + "WriteLine(G(ref x)[1]);\nstatic ReadOnlySpan<int> G(ref ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n", "VIB000,VIB004,VIB007"),
        ["ref-view-parameter-assignment-value"] = (Ros + "WriteLine(G(ref x)[1]);\nstatic ReadOnlySpan<int> G(ref ReadOnlySpan<int> p) { return p = [1, 2]; }\n", "VIB000,VIB004,VIB007"),
        ["out-view-parameter-assignment-value"] = ("WriteLine(G(out ReadOnlySpan<int> x));\nWriteLine(x[1]);\nstatic int G(out ReadOnlySpan<int> p) { return (p = [1, 2])[0]; }\n", "VIB000,VIB004"),
        ["ref-view-local-assignment-value"] = ("WriteLine(G());\nstatic int G() { ReadOnlySpan<int> q = [5]; ref ReadOnlySpan<int> r = ref q; return (r = [1, 2])[1] + q[0]; }\n", "VIB000,VIB004"),
    };

    // C#-invalid neighbours: Roslyn rejects them, so every compiler must reject them (SPEC §4 C# validity).
    private static readonly Dictionary<string, (string Source, string CSharpId)> CSharpInvalid = new(StringComparer.Ordinal)
    {
        ["span-parameter-returned"] = ("Span<int> x = [5, 6, 7];\nWriteLine(G(x)[1]);\nstatic Span<int> G(Span<int> p) { p = [1, 2]; return p; }\n", "CS9203"),
        ["span-parameter-not-returned"] = ("Span<int> x = [5, 6, 7];\nWriteLine(G(x));\nstatic int G(Span<int> p) { p = [1, 2]; return p[1]; }\n", "CS9203"),
        ["nonconstant-elements-returned"] = (Ros + "WriteLine(G(x, 4)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k) { p = [k, 2]; return p; }\n", "CS9203"),
        ["scoped-parameter-returned"] = (Ros + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(scoped ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n", "CS8352"),
        ["in-parameter-assigned"] = (Ros + "WriteLine(G(in x)[1]);\nstatic ReadOnlySpan<int> G(in ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n", "CS8331"),
    };

    public static TheoryData<string> RejectedNames => new(Rejected.Keys);
    public static TheoryData<string> FrameOwnedNames => new(FrameOwned.Keys);
    public static TheoryData<string> AcceptedNames => new(Accepted.Keys);
    public static TheoryData<string> BannedNames => new(Banned.Keys);
    public static TheoryData<string> CSharpInvalidNames => new(CSharpInvalid.Keys);

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public async Task ReturnedViewOfDestinationStorage_IsVib003AndVib007OnEveryFrontend(string name)
    {
        var result = await Round12RuleWitness.CheckAllAsync(Rejected[name]);
        string[] expected = ["VIB003", "VIB007"];
        Assert.True(expected.SequenceEqual(result.Analyzer), $"Analyzer reported [{string.Join(",", result.Analyzer)}]");
        Assert.True(expected.SequenceEqual(result.Stage0), $"Stage-0 reported [{string.Join(",", result.Stage0)}]");
        Assert.True(expected.SequenceEqual(result.Self), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }

    [Theory]
    [MemberData(nameof(FrameOwnedNames))]
    public async Task ReturnedViewOfFrameStorage_IsVib007OnlyOnEveryFrontend(string name)
    {
        var result = await Round12RuleWitness.CheckAllAsync(FrameOwned[name]);
        string[] expected = ["VIB007"];
        Assert.True(expected.SequenceEqual(result.Analyzer), $"Analyzer reported [{string.Join(",", result.Analyzer)}]");
        Assert.True(expected.SequenceEqual(result.Stage0), $"Stage-0 reported [{string.Join(",", result.Stage0)}]");
        Assert.True(expected.SequenceEqual(result.Self), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }

    [Fact]
    public async Task ParameterScopeExit_NamesTheParameterOnStage0AndAnalyzer()
    {
        const string message = "Stack-allocated buffer 'p' exits scope while borrowed by the returned value.";
        string source = Rejected["assigned-then-returned"];
        string temp = Directory.CreateTempSubdirectory("vibranium-n16-message-").FullName;
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "ParameterStackStorage", includeStd: true);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            int returned = source.IndexOf("p; }", StringComparison.Ordinal);
            var conflict = Assert.Single(stage0, d => d.Id == "VIB003");
            Assert.Equal((message, returned), (conflict.Message, conflict.Span.Start));
            var live = Assert.Single(analyzer, d => d.Id == "VIB003");
            Assert.Equal((message, returned), (live.GetMessage(), live.Location.SourceSpan.Start));
            Assert.Equal(stage0.Select(d => (d.Id, d.Message, d.Span.Start)).OrderBy(d => d.Start).ThenBy(d => d.Id),
                analyzer.Select(d => (d.Id, Message: d.GetMessage(), Start: d.Location.SourceSpan.Start)).OrderBy(d => d.Start).ThenBy(d => d.Id));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Theory]
    [MemberData(nameof(AcceptedNames))]
    public async Task AcceptedNeighbour_RunsAlikeManagedAndNative(string name)
    {
        var (source, output) = Accepted[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(BannedNames))]
    public async Task BannedNeighbour_IsVib000OnEveryFrontend(string name)
    {
        var (source, exact) = Banned[name];
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Contains("VIB000", result.Analyzer);
        Assert.Equal(result.Analyzer, result.Stage0);
        Assert.True(result.Self.Contains("VIB000"), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
        if (exact is not null)
        {
            string[] expected = exact.Split(',');
            Assert.Equal(expected, result.Analyzer);
            Assert.True(expected.SequenceEqual(result.Self), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
        }
    }

    [Theory]
    [MemberData(nameof(CSharpInvalidNames))]
    public Task CSharpInvalidNeighbour_IsRejectedByEveryCompiler(string name)
    {
        var (source, csId) = CSharpInvalid[name];
        return Round12RuleWitness.RejectCSharpAsync(source, csId);
    }
}
