using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12TupleGateTests
{
    private const string TupleMessage = "Tuples are not supported in Vibranium v0.2; use a struct with named fields.";
    private const string WithElementMessage = "Collection-expression 'with(...)' arguments are not supported in Vibranium v0.2; create the collection, then add its elements.";

    [Fact]
    public void SyntaxKindTableClassifiesTupleAndWithElementKinds()
    {
        Assert.Equal(SyntaxAndSymbolGatekeeper.SyntaxKindSupport.Unsupported, SyntaxAndSymbolGatekeeper.ClassifySyntaxKind(SyntaxKind.TupleType));
        Assert.Equal(SyntaxAndSymbolGatekeeper.SyntaxKindSupport.Unsupported, SyntaxAndSymbolGatekeeper.ClassifySyntaxKind(SyntaxKind.TupleExpression));
        Assert.Equal(SyntaxAndSymbolGatekeeper.SyntaxKindSupport.Unsupported, SyntaxAndSymbolGatekeeper.ClassifySyntaxKind(SyntaxKind.WithElement));
        Assert.Equal(SyntaxAndSymbolGatekeeper.SyntaxKindSupport.CheckedRestriction, SyntaxAndSymbolGatekeeper.ClassifySyntaxKind(SyntaxKind.TupleElement));
        Assert.True(SyntaxAndSymbolGatekeeper.RejectsSyntaxKind(SyntaxKind.TupleType));
        Assert.True(SyntaxAndSymbolGatekeeper.RejectsSyntaxKind(SyntaxKind.TupleExpression));
        Assert.True(SyntaxAndSymbolGatekeeper.RejectsSyntaxKind(SyntaxKind.WithElement));
        Assert.False(SyntaxAndSymbolGatekeeper.RejectsSyntaxKind(SyntaxKind.TupleElement));
    }

    [Theory]
    [InlineData("q01-tuple-field-first-element-struct", """
        var s = new S(); s.V = 5; s.T.A = 0x0102030405060708L; WriteLine(s.V); WriteLine(s.T.A);
        struct S { public (long A, int B) T; public int V; }
        """)]
    [InlineData("q02-tuple-field-first-element-class", """
        var c = new C(); c.Set(0x0102030405060708L); WriteLine(c.Get()); WriteLine(c.V());
        class C { private (long A, long B) t; private int v = 5; [Mut] public void Set(long a) { t.A = a; } public long Get() => t.A; public int V() => v; }
        """)]
    [InlineData("q03-tuple-field-first-element-read", """
        var s = new S(); s.V = 7; WriteLine(s.T.A); WriteLine(s.V);
        struct S { public (long A, int B) T; public int V; }
        """)]
    [InlineData("q04-tuple-field-item1-int", """
        var s = new S(); s.W = 9; s.T.Item1 = -1; WriteLine(s.W); WriteLine(s.T.Item1);
        struct S { public (int, int) T; public byte W; }
        """)]
    [InlineData("q05-tuple-field-vec-element", """
        Vec<S> v = [];
        var a = new S(); a.T.A = -1L; a.V = 1; v.Add(a);
        var b = new S(); b.T.A = 2L; b.V = 2; v.Add(b);
        WriteLine(v[0].V); WriteLine(v[1].V); WriteLine(v[0].T.A); WriteLine(v[1].T.A);
        struct S { public (long A, long B) T; public int V; }
        """)]
    [InlineData("o04-tuple-field-used", """
        var c = new C(); c.Set(3, 4); WriteLine(c.Sum());
        class C { private (int A, int B) t; [Mut] public void Set(int a, int b) { t.A = a; t.B = b; } public int Sum() => t.A + t.B; }
        """)]
    [InlineData("o01-tuple-return-default", """
        var t = K.F();
        WriteLine(t.Item1 + t.Item2);
        static class K { public static (int, int) F() => default; }
        """)]
    [InlineData("o03-tuple-generic-argument", """
        Vec<(int, int)> v = [];
        Option<(int X, int Y)> o = None;
        WriteLine(v.Count); WriteLine(o.IsSome);
        """)]
    [InlineData("o14-tuple-in-localfn-signature-unused", """
        WriteLine(1);
        #pragma warning disable CS8321
        static Option<(int, Vec<int>)> F((int, int) a, ref (long, long) b) => None;
        """)]
    [InlineData("t01-tuple-param-type-only", """
        WriteLine(K.F(default));
        static class K { public static int F((int, int) t) => 1; }
        """)]
    [InlineData("t02-tuple-field-type-only", """
        var s = new S(); s.V = 1; WriteLine(s.V);
        struct S { public (int A, int B) T; public int V; }
        """)]
    [InlineData("pos-field", """
        var s = new S(); s.X = 1; WriteLine(s.X);
        struct S { public (int, int) T; public int X; }
        """)]
    [InlineData("pos-return", """
        WriteLine(K.F().Item1);
        static class K { public static (int, int) F() => default; }
        """)]
    [InlineData("pos-parameter", """
        WriteLine(K.F(default));
        static class K { public static int F((int, int) p) => 1; }
        """)]
    [InlineData("pos-local", """
        (int, int) t = default;
        WriteLine(t.Item1);
        """)]
    [InlineData("pos-generic-argument", """
        WriteLine(K.Id<(int, int)>(default).Item1);
        static class K { public static T Id<T>(T x) => x; }
        """)]
    [InlineData("pos-nested-vec", """
        Vec<(int, int)> v = [];
        WriteLine(v.Count);
        """)]
    [InlineData("pos-local-function-signature", """
        WriteLine(1);
        #pragma warning disable CS8321
        static (int, int) F((int, int) p) => p;
        """)]
    [InlineData("pos-cast", """
        WriteLine(K.F(default));
        static class K { public static long F((int, int) p) => (((int, long))p).Item2; }
        """)]
    [InlineData("pos-default", """
        var t = default((int, int));
        WriteLine(t.Item1);
        """)]
    [InlineData("pos-option-type-argument", """
        Option<(int, int)> o = None;
        WriteLine(o.IsSome);
        """)]
    public async Task TupleTypesRejectWithExactVib000(string name, string source)
    {
        var (stage0, analyzer, root) = await CompileAndCheckWithRootAsync(name, source);
        Assert.Equal(["VIB000"], stage0.Select(d => d.Id).Distinct().ToArray());
        Assert.Equal(["VIB000"], analyzer.Select(d => d.Id).Distinct().ToArray());
        var expectedSpan = root.DescendantNodes().OfType<TupleTypeSyntax>().First().Span;
        var s0 = Assert.Single(stage0, d => d.Message == TupleMessage);
        var an = Assert.Single(analyzer, d => d.GetMessage() == TupleMessage);
        Assert.Equal(expectedSpan.Start, s0.Span.Start);
        Assert.Equal(expectedSpan.Length, s0.Span.Length);
        Assert.Equal(expectedSpan.Start, an.Location.SourceSpan.Start);
        Assert.Equal(expectedSpan.Length, an.Location.SourceSpan.Length);
    }

    [Theory]
    [InlineData("o02-tuple-local-named-elements", """
        (int A, long B) t;
        t.A = H.Num(); t.B = 5;
        WriteLine(t.A + t.B);

        static class H
        {
            public static int Num() { WriteLine("num"); return 3; }
        }
        """)]
    [InlineData("o05-tuple-param-ref-and-copy", """
        (int A, int B) t;
        t.A = 1; t.B = 2;
        K.Swap(ref t);
        var u = t;
        u.A = 9;
        WriteLine(t.A * 10 + t.B); WriteLine(u.A);
        static class K { public static void Swap(ref (int A, int B) t) { int x = t.A; t.A = t.B; t.B = x; } }
        """)]
    [InlineData("o06-tuple-of-vec-ownership", """
        (Vec<int> V, int N) t;
        t.V = H.MakeVec(); t.N = 1;
        var u = t;
        u.V.Add(7);
        WriteLine(u.V.Count + u.N);

        static class H
        {
            public static Vec<int> MakeVec() { WriteLine("makevec"); return [4, 5, 6]; }
        }
        """)]
    public async Task TupleLocalsReportVib000Present(string name, string source)
    {
        var (stage0, analyzer, root) = await CompileAndCheckWithRootAsync(name, source);
        string[] stage0Ids = stage0.Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] analyzerIds = analyzer.Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(stage0Ids, analyzerIds);
        Assert.Contains("VIB000", stage0Ids);
        var expectedSpan = root.DescendantNodes().OfType<TupleTypeSyntax>().First().Span;
        var s0 = Assert.Single(stage0, d => d.Message == TupleMessage);
        var an = Assert.Single(analyzer, d => d.GetMessage() == TupleMessage);
        Assert.Equal(expectedSpan.Start, s0.Span.Start);
        Assert.Equal(expectedSpan.Length, s0.Span.Length);
        Assert.Equal(expectedSpan.Start, an.Location.SourceSpan.Start);
        Assert.Equal(expectedSpan.Length, an.Location.SourceSpan.Length);
    }

    [Theory]
    [InlineData("o07-tuple-cast-and-default-expr", """
        var t = default((int, int));
        var u = ((int, long))t;
        WriteLine(t.Item1 + u.Item2);
        """)]
    [InlineData("t03-tuple-deconstruct-var", """
        var (a, b) = new P(1, 2);
        WriteLine(a + b);
        struct P(int x, int y) { public int X = x; public int Y = y; public void Deconstruct(out int a, out int b) { a = X; b = Y; } }
        """)]
    [InlineData("t04-tuple-equality-and-swap", """
        int a = 1, b = 2;
        (a, b) = (b, a);
        WriteLine((a, b) == (2, 1));
        """)]
    [InlineData("r38-var-deconstruction-user", """
        var p = new P(); var (a, b) = p; WriteLine(a + b);
        struct P { public void Deconstruct(out int a, out int b) { a = 1; b = 2; } }
        """)]
    [InlineData("r39-foreach-deconstruction", """
        Vec<P> v = []; v.Add(new P());
        foreach (var (a, b) in v.AsSpan()) { WriteLine(a + b); }
        struct P { public readonly void Deconstruct(out int a, out int b) { a = 1; b = 2; } }
        """)]
    [InlineData("r40-deconstruction-assignment", """
        var p = new P(); int a; int b; (a, b) = p; WriteLine(a + b);
        struct P { public void Deconstruct(out int a, out int b) { a = 1; b = 2; } }
        """)]
    [InlineData("r41-tuple-expression-var", """
        var t = (1, 2); WriteLine(t.Item1 + t.Item2);
        """)]
    [InlineData("r42-tuple-equality-constant", """
        int a = H1.One();
        WriteLine((a, 2) == (1, 2));
        static class H1 { public static int One() => 1; }
        """)]
    [InlineData("r43-tuple-discard", """
        _ = (1, 2);
        WriteLine(1);
        """)]
    [InlineData("r44-tuple-swap", """
        int a = 1; int b = 2; (a, b) = (b, a); WriteLine(a * 10 + b);
        """)]
    [InlineData("r67-tuple-pattern-switch", """
        int a = H1.Seven(); int b = 2;
        WriteLine((a, b) switch { (7, 2) => 1, _ => 0 });
        static class H1 { public static int Seven() => 7; }
        """)]
    public async Task TupleExpressionsAndDeconstructionsStayVib000(string name, string source)
    {
        var (stage0, analyzer, root) = await CompileAndCheckWithRootAsync(name, source);
        string[] stage0Ids = stage0.Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] analyzerIds = analyzer.Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(stage0Ids, analyzerIds);
        Assert.Contains("VIB000", stage0Ids);
        SyntaxNode? gated = root.DescendantNodes().OfType<TupleExpressionSyntax>().FirstOrDefault()
            ?? (SyntaxNode?)root.DescendantNodes().OfType<TupleTypeSyntax>().FirstOrDefault();
        if (gated is not null)
        {
            var s0 = Assert.Single(stage0, d => d.Message == TupleMessage);
            var an = Assert.Single(analyzer, d => d.GetMessage() == TupleMessage);
            Assert.Equal(gated.Span.Start, s0.Span.Start);
            Assert.Equal(gated.Span.Length, s0.Span.Length);
            Assert.Equal(gated.Span.Start, an.Location.SourceSpan.Start);
            Assert.Equal(gated.Span.Length, an.Location.SourceSpan.Length);
        }
    }

    [Theory]
    [InlineData("o08-with-element-vec-args", """
        Vec<int> v = [with(), 1, 2];
        WriteLine(v.Count + v[1]);
        """)]
    [InlineData("o09-with-element-only", """
        Vec<int> v = [with()];
        v.Add(4);
        WriteLine(v.Count + v[0]);
        """)]
    [InlineData("t06-with-element-empty", """
        Vec<int> v = [with(), 1, 2];
        WriteLine(v.Count);
        """)]
    [InlineData("r70-with-element-side-effect-order", """
        Vec<int> v = [with(), H.Num(), H.Id(2)];
        WriteLine(v.Count + v[0] + v[1]);
        static class H
        {
            public static int Num() { WriteLine("num"); return 3; }
            public static int Id(int x) { WriteLine("id"); return x; }
        }
        """)]
    [InlineData("with-vec-explicit", """
        Vec<int> v = [with(), 3, 4];
        WriteLine(v.Count + v[0]);
        """)]
    public async Task WithElementRejectsWithExactVib000(string name, string source)
    {
        var (stage0, analyzer, root) = await CompileAndCheckWithRootAsync(name, source);
        Assert.Equal(["VIB000"], stage0.Select(d => d.Id).Distinct().ToArray());
        Assert.Equal(["VIB000"], analyzer.Select(d => d.Id).Distinct().ToArray());
        var expectedSpan = root.DescendantNodes().OfType<WithElementSyntax>().First().Span;
        var s0 = Assert.Single(stage0, d => d.Message == WithElementMessage);
        var an = Assert.Single(analyzer, d => d.GetMessage() == WithElementMessage);
        Assert.Equal(expectedSpan.Start, s0.Span.Start);
        Assert.Equal(expectedSpan.Length, s0.Span.Length);
        Assert.Equal(expectedSpan.Start, an.Location.SourceSpan.Start);
        Assert.Equal(expectedSpan.Length, an.Location.SourceSpan.Length);
    }

    [Fact]
    public async Task WithElementReportsOncePerFileAtFirstOccurrence()
    {
        const string template = "class TYPE { public static int F() { Vec<int> a = [with(), 1]; Vec<int> b = [with(), 2]; return a.Count + b.Count; } }";
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-tuple-with-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string[] names = ["First.cs", "Second.cs"];
            foreach (string file in names)
            {
                File.WriteAllText(Path.Combine(directory, file), template.Replace("TYPE", Path.GetFileNameWithoutExtension(file), StringComparison.Ordinal));
            }
            File.WriteAllText(Path.Combine(directory, "Program.cs"), "WriteLine(0);");
            File.WriteAllText(Path.Combine(directory, "vib.manifest"), "unit app\nsource Program.cs\nsource First.cs\nsource Second.cs\n");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(directory, "TupleGateWith", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(["VIB000"], stage0.Select(d => d.Id).Distinct().ToArray());
            Assert.Equal(["VIB000"], analyzer.Select(d => d.Id).Distinct().ToArray());
            Assert.Equal(2, stage0.Count(d => d.Message == WithElementMessage));
            Assert.Equal(2, analyzer.Count(d => d.GetMessage() == WithElementMessage));
            foreach (string file in names)
            {
                var tree = compilation.SyntaxTrees.Single(t => Path.GetFileName(t.FilePath) == file);
                var firstSpan = tree.GetRoot().DescendantNodes().OfType<WithElementSyntax>().First().Span;
                var s0 = Assert.Single(stage0, d => Path.GetFileName(d.Span.FilePath) == file && d.Message == WithElementMessage);
                var an = Assert.Single(analyzer, d => Path.GetFileName(d.Location.SourceTree!.FilePath) == file && d.GetMessage() == WithElementMessage);
                Assert.Equal(firstSpan.Start, s0.Span.Start);
                Assert.Equal(firstSpan.Length, s0.Span.Length);
                Assert.Equal(firstSpan.Start, an.Location.SourceSpan.Start);
                Assert.Equal(firstSpan.Length, an.Location.SourceSpan.Length);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("k20-positional-union-patterns", """
        Option<int> o = Some(3);
        if (o is Some<int>(var n)) WriteLine(n);
        if (o is Some<int>(3)) WriteLine(1);
        switch (o) { case Some<int>(var m): WriteLine(m); break; default: WriteLine(0); break; }
        Result<int, int> r = Err(5);
        WriteLine(r switch { Ok<int>(var x) => x, Err<int>(var e) => -e });
        """)]
    [InlineData("p05-var-pattern-and-not-or", """
        int n = H.Num();
        if (n is var q && q > 2) WriteLine(q);
        if (n is not (1 or 2)) WriteLine(1);
        WriteLine(n switch { 1 or 2 => 10, (3) => 30, not 4 => 40, _ => 50 });
        switch (n) { case 1 or 3: WriteLine(13); break; case var z when z > 5: WriteLine(z); break; default: WriteLine(0); break; }

        static class H
        {
            public static int Num() { WriteLine("num"); return 3; }
        }
        """)]
    [InlineData("p07-implicit-new-default-forms", """
        C c = new(); S s = new(3); Vec<int> v = new();
        v.Add(K.Zero() + K.Take(default) + c.V + s.V);
        WriteLine(v[0]);
        class C { public int V = 1; }
        struct S(int v) { public int V = v; }
        static class K { public static int Zero() { int z = default; return z; } public static int Take(long x) => (int)x + 5; }
        """)]
    [InlineData("k18-switch-labels-ternary-named-args", """
        int n = H.Num();
        switch (n) { case 3: WriteLine(1); break; default: WriteLine(0); break; }
        WriteLine(n > 2 ? K.F(a: 1, b: 2) : 0);
        static class K { public static int F(int a, int b) => a - b; }

        static class H
        {
            public static int Num() { WriteLine("num"); return 3; }
        }
        """)]
    public async Task AcceptedControlsRemainAccepted(string name, string source)
    {
        var (stage0, analyzer) = await CompileAndCheckAsync(name, source, requireNoWarnings: true);
        Assert.Empty(stage0);
        Assert.Empty(analyzer);
    }

    [Fact]
    public Task TupleTypeRejectedByAllFrontends() => Round12RuleWitness.RejectAsync(
        "var s = new S(); s.W = 9; s.T.Item1 = -1; WriteLine(s.W); WriteLine(s.T.Item1); struct S { public (int, int) T; public byte W; }",
        "VIB000", banned: true, exact: true);

    [Fact]
    public Task WithElementRejectedByAllFrontends() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [with(), 1, 2]; WriteLine(v.Count + v[1]);",
        "VIB000", banned: true, exact: true);

    [Fact]
    public Task WithElementAcceptControl() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2]; v.Add(3); WriteLine(v.Count + v[2]);",
        "6\n");

    private static async Task<(IReadOnlyList<Vibranium.Compiler.Core.VibraniumDiagnostic> Stage0, ImmutableArray<Diagnostic> Analyzer)> CompileAndCheckAsync(string name, string source, bool requireNoWarnings = false)
    {
        var (stage0, analyzer, _) = await CompileAndCheckWithRootAsync(name, source, requireNoWarnings);
        return (stage0, analyzer);
    }

    private static async Task<(IReadOnlyList<Vibranium.Compiler.Core.VibraniumDiagnostic> Stage0, ImmutableArray<Diagnostic> Analyzer, SyntaxNode Root)> CompileAndCheckWithRootAsync(string name, string source, bool requireNoWarnings = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-tuple-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "TupleGate", includeStd: true);
            var floor = requireNoWarnings ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
            var csharpDiagnostics = compilation.GetDiagnostics().Where(d => d.Severity >= floor).ToArray();
            Assert.True(csharpDiagnostics.Length == 0, $"{name}: unexpected C# diagnostics:\n{string.Join("\n", csharpDiagnostics.Select(d => d.ToString()))}");
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var root = compilation.SyntaxTrees.Single(t => t.FilePath == path).GetRoot();
            return (stage0, analyzer, root);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
