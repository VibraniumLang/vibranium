using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12FieldInitializerCutTests
{
    private const string Suffix = " is not supported in a field or property initializer in Vibranium v0.2; assign the field in the constructor.";

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["and-field-class", "&&", "'&&'", """
            WriteLine(new C().X);
            class C { public bool X = H.Flag() /*C23*/&& H.Side(); }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["and-field-struct", "&&", "'&&'", """
            WriteLine(new C().X);
            struct C { public bool X = H.Flag() /*C23*/&& H.Side(); public C() { } }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["or-field-class", "||", "'||'", """
            WriteLine(new C().X);
            class C { public bool X = H.Yes() /*C23*/|| H.Side(); }

            public static class H
            {
                public static bool Yes() { WriteLine("yes"); return true; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["or-field-struct", "||", "'||'", """
            WriteLine(new C().X);
            struct C { public bool X = H.Yes() /*C23*/|| H.Side(); public C() { } }

            public static class H
            {
                public static bool Yes() { WriteLine("yes"); return true; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["and-in-arg-field-class", "&&", "'&&'", """
            WriteLine(new C().X.IsSome);
            class C { public Option<bool> X = Some(H.Flag() /*C23*/&& H.Side()); }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["and-in-collection-field-class", "&&", "'&&'", """
            WriteLine(new C().X.Count);
            class C { public Vec<bool> X = [H.Flag() /*C23*/&& H.Side()]; }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["not-and-field-class", "&&", "'&&'", """
            WriteLine(new C().X);
            class C { public bool X = !(H.Flag() /*C23*/&& H.Side()); }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["and-in-new-field-class", "&&", "'&&'", """
            WriteLine(new C().X.V);
            class C { public C3 X = new C3(H.Flag() /*C23*/&& H.Side() ? 1 : 2); }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
            }
            public class C3 { public int V; public C3(int v) { WriteLine("c3"); V = v; } }
            """];
        yield return ["primary-and-field-class", "&&", "'&&'", """
            WriteLine(new C(2).X);
            class C(int p) { public bool X = p > 5 /*C23*/&& H.Side(); }

            public static class H
            {
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["primary-or-field-class", "||", "'||'", """
            WriteLine(new C(2).X);
            class C(int p) { public bool X = p > 0 /*C23*/|| H.Side(); }

            public static class H
            {
                public static bool Side() { WriteLine("side"); return true; }
            }
            """];
        yield return ["and-const-field-class", "&&", "'&&'", """
            WriteLine(new C().X);
            class C { public bool X = false /*C23*/&& true; }

            public static class H
            {

            }
            """];
        yield return ["conditional-field-class", "?", "The '?:' operator", """
            WriteLine(new C().X);
            class C { public int X = H.Flag() /*C23*/? 2 : 3; }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
            }
            """];
        yield return ["conditional-const-field-struct", "?", "The '?:' operator", """
            WriteLine(new C().X);
            struct C { public int X = true /*C23*/? 2 : 3; public C() { } }

            public static class H
            {

            }
            """];
        yield return ["switch-field-class", "switch", "A switch expression", """
            WriteLine(new C().X);
            class C { public int X = H.Num() /*C23*/switch { 1 => 10, _ => 20 }; }

            public static class H
            {
                public static int Num() { WriteLine("num"); return 3; }
            }
            """];
        yield return ["switch-field-struct", "switch", "A switch expression", """
            WriteLine(new C().X);
            struct C { public int X = H.Num() /*C23*/switch { 1 => 10, _ => 20 }; public C() { } }

            public static class H
            {
                public static int Num() { WriteLine("num"); return 3; }
            }
            """];
        yield return ["is-pattern-field-class", "is", "An 'is' expression", """
            WriteLine(new C().X);
            class C { public bool X = H.Num() /*C23*/is > 0 and < 5; }

            public static class H
            {
                public static int Num() { WriteLine("num"); return 3; }
            }
            """];
        yield return ["or-pattern-field-class", "is", "An 'is' expression", """
            WriteLine(new C().X);
            class C { public bool X = H.Num() /*C23*/is 1 or 3; }

            public static class H
            {
                public static int Num() { WriteLine("num"); return 3; }
            }
            """];
        yield return ["not-pattern-field-struct", "is", "An 'is' expression", """
            WriteLine(new C().X);
            struct C { public bool X = H.Num() /*C23*/is not 4; public C() { } }

            public static class H
            {
                public static int Num() { WriteLine("num"); return 3; }
            }
            """];
        yield return ["or-in-new", "||", "'||'", """
            WriteLine(new C().X.Value);
            class C { public B X = new B(H.Flag() /*C23*/|| H.Side()); }
            class B(bool value) { public bool Value = value; }

            static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
                public static bool Id(bool b) => b;
            }
            """];
        yield return ["or-constant", "||", "'||'", """
            WriteLine(new C().X);
            class C { public bool X = true /*C23*/|| H.Side(); }

            static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
                public static bool Id(bool b) => b;
            }
            """];
        yield return ["nested-call-argument", "&&", "'&&'", """
            WriteLine(new C().X);
            class C { public bool X = H.Id(H.Id(H.Flag() /*C23*/&& H.Side())); }

            static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
                public static bool Id(bool b) => b;
            }
            """];
        yield return ["type-is-legacy", "is", "An 'is' expression", """
            WriteLine(new C<int>(2).X);
            class C<T>(T value) { public bool X = value /*C23*/is int; }
            """];
        yield return ["nested-operator-first-token", "&&", "'&&'", """
            WriteLine(new C().X);
            class C { public bool X = (H.Flag() /*C23*/&& H.Side()) || H.Flag(); }

            static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
                public static bool Id(bool b) => b;
            }
            """];
        yield return ["multiple-classes-and-fields-first-token", "||", "'||'", """
            WriteLine(new B().X);
            class Generic<T> { public bool First = H.Flag() /*C23*/|| H.Side(), Second = H.Flag() && H.Side(); }
            class B { public bool X = H.Flag() && H.Side(); public bool Y = H.Flag() is true; }

            static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
                public static bool Id(bool b) => b;
            }
            """];
        yield return ["out-var-and-explicit-ctor", "&&", "'&&'", """
            WriteLine(new C().B);
            class C { const int v = 7; public bool A = int.TryParse("5", out int v) /*C23*/&& v > 0; public int B; public C() { B = v; } }
            """];
        yield return ["out-var-and-implicit-ctor", "&&", "'&&'", """
            var c = new C();
            WriteLine(c.B);
            WriteLine(c.A);
            class C { const int v = 7; public bool A = int.TryParse("5", out int v) /*C23*/&& v > 0; public int B = v; }
            """];
        yield return ["out-var-and-primary-ctor", "&&", "'&&'", """
            WriteLine(new C(1).B);
            class C(int k) { const int v = 7; public bool A = int.TryParse("5", out int v) /*C23*/&& v > k; public int B = v; }
            """];
        yield return ["out-var-and-struct-ctor", "&&", "'&&'", """
            WriteLine(new S(1).B);
            struct S { const int v = 7; public bool A = int.TryParse("5", out int v) /*C23*/&& v > 0; public int B; public S(int k) { B = v + k - 1; } }
            """];
        yield return ["out-var-or-explicit-ctor", "||", "'||'", """
            WriteLine(new C().B);
            class C { const int v = 7; public bool A = H.Get(out int v) /*C23*/|| v > 0; public int B; public C() { B = v; } }

            static class H
            {
                public static bool Get(out int x) { x = 5; return false; }
            }
            """];
        yield return ["out-var-conditional-explicit-ctor", "?", "The '?:' operator", """
            WriteLine(new C().B);
            class C { const int v = 7; public int A = int.TryParse("5", out int v) /*C23*/? v : 0; public int B; public C() { B = v; } }
            """];
        yield return ["pattern-var-implicit-ctor", "is", "An 'is' expression", """
            WriteLine(new C().A);
            class C { public bool A = H.Opt() /*C23*/is Some<int>(var q); }

            static class H
            {
                public static Option<int> Opt() => Some(5);
            }
            """];
        yield return ["switch-arm-var-primary-ctor", "switch", "A switch expression", """
            WriteLine(new C(1).A);
            class C(int k) { public int A = k /*C23*/switch { 4 => 0, var q => q }; }
            """];
        yield return ["nested-type-field-first-token", "?", "The '?:' operator", """
            WriteLine(new C.N().X + new C().Y);
            class C { public class N { public int X = H.Num() > 0 /*C23*/? 1 : 2; } public int Y = H.Num() switch { _ => 3 }; }

            static class H
            {
                public static int Num() => 5;
            }
            """];
        yield return ["constructor-overloads-first-token", "&&", "'&&'", """
            WriteLine(new C(1).X);
            class C
            {
                public bool X = H.Flag() /*C23*/&& H.Side();
                public C() { }
                public C(int x) { WriteLine(x); }
            }

            static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Side() { WriteLine("side"); return true; }
                public static bool Id(bool b) => b;
            }
            """];
    }

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public async Task FirstOperatorHasExactDiagnosticParity(string name, string token, string construct, string marked)
    {
        const string marker = "/*C23*/";
        int start = marked.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        var source = marked.Remove(start, marker.Length);
        Assert.Equal(token, source.Substring(start, token.Length));
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-c23-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "FieldInitializerCut", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { "VIB000" }, stage0.Select(d => d.Id).Distinct().ToArray());
            Assert.Equal(new[] { "VIB000" }, analyzer.Select(d => d.Id).Distinct().ToArray());
            var cut = Assert.Single(stage0, d => d.Message.EndsWith(Suffix, StringComparison.Ordinal));
            var live = Assert.Single(analyzer, d => d.GetMessage().EndsWith(Suffix, StringComparison.Ordinal));
            Assert.Equal(construct + Suffix, cut.Message);
            Assert.Equal(cut.Message, live.GetMessage());
            Assert.Equal(start, cut.Span.Start);
            Assert.Equal(start, live.Location.SourceSpan.Start);
            Assert.Equal(token.Length, cut.Span.Length);
            Assert.Equal(token.Length, live.Location.SourceSpan.Length);
            var position = live.Location.GetLineSpan().StartLinePosition;
            Assert.Equal(position.Line + 1, cut.Span.Line);
            Assert.Equal(position.Character + 1, cut.Span.Column);
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            var selfErrors = (self.Out + self.Err).Split('\n').Where(l => l.Contains(": error VIB", StringComparison.Ordinal)).ToArray();
            Assert.True(self.Exit == 1 && selfErrors.All(l => l.Contains(": error VIB000: ", StringComparison.Ordinal)), self.Out + self.Err);
            var selfCut = Assert.Single(selfErrors, l => l.EndsWith(Suffix, StringComparison.Ordinal));
            Assert.EndsWith($"({cut.Span.Line},{cut.Span.Column}): error VIB000: {cut.Message}", selfCut, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlainFieldInitializers_AcceptWithNativeParity()
    {
        var result = await Round12RuleWitness.AcceptAsync("""
            var c = new C(3);
            WriteLine(c.A + c.B);
            WriteLine(c.Name);
            WriteLine(c.O.UnwrapOr(0));
            WriteLine(c.V.Count);
            WriteLine(c.Neg);
            WriteLine(c.Inner.N);
            WriteLine(c.Call);
            var s = new S(4);
            WriteLine(s.Q);
            class C(int p)
            {
                public int A = p * 2 + 1;
                public int B = H.Twice(p);
                public string Name = "c";
                public Option<int> O = Some(p);
                public Vec<int> V = [1, 2, p];
                public bool Neg = !H.False();
                public D Inner = new D(p);
                public int Call = H.Log("init");
            }
            class D(int n) { public int N = n; }
            struct S(int q) { public int Q = q + 1; }
            static class H
            {
                public static int Twice(int x) => x * 2;
                public static bool False() => false;
                public static int Log(string s) { WriteLine(s); return 1; }
            }
            """, "init\n13\nc\n3\n3\nTrue\n3\n1\n5\n");
        Assert.Equal(0, result.Managed.Exit);
    }
}
