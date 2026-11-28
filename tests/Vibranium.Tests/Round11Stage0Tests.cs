using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round11Stage0Tests
{
    public static IEnumerable<object[]> CompoundOperators()
    {
        foreach (var (op, expected) in new[]
        {
            ("+", 15), ("-", 9), ("*", 36), ("/", 4), ("%", 0),
            ("&", 0), ("|", 15), ("^", 15), ("<<", 96), (">>", 1), (">>>", 1),
        })
        {
            string source = "WriteLine(F(12));static int F(int a){a " + op + "= Set(out a)>0?3:2;return a;}static int Set(out int a){a=90;return 1;}";
            yield return [op, source, expected + "\n"];
        }
    }

    [Theory]
    [MemberData(nameof(CompoundOperators))]
    public Task CompoundAssignment_SnapshotsBeforeBranchingRhs(string caseName, string source, string expected) =>
        AssertExecutionParity(caseName, source, expected);

    [Theory]
    [InlineData("local", "int a=12;a+=Set(out a)>0?3:2;WriteLine(a);static int Set(out int a){a=90;return 1;}", "15\n")]
    [InlineData("ref-local", "int a=12;int b=0;ref int r=ref a;r+=Set(out b)>0?3:2;WriteLine(r);static int Set(out int a){a=90;return 1;}", "15\n")]
    [InlineData("ref-parameter", "int a=12;F(ref a);WriteLine(a);static void F(ref int a){a+=Set(out a)>0?3:2;}static int Set(out int a){a=90;return 1;}", "15\n")]
    [InlineData("out-parameter", "F(out int a);WriteLine(a);static void F(out int a){a=12;a+=Set(out a)>0?3:2;}static int Set(out int a){a=90;return 1;}", "15\n")]
    [InlineData("long", "long a=12;a+=Set(out a)>0?3L:2L;WriteLine(a);static int Set(out long a){a=90;return 1;}", "15\n")]
    [InlineData("byte", "byte a=7;a+=Set(out a)>0?(byte)2:(byte)1;WriteLine((int)a);static int Set(out byte a){a=200;return 1;}", "9\n")]
    [InlineData("struct-field", "S s=new S(12);s.X+=Set(out s.X)>0?3:2;WriteLine(s.X);static int Set(out int a){a=90;return 1;}public struct S{public int X;public S(int x){X=x;}}", "15\n")]
    [InlineData("class-field", "var s=new S(12);int a=0;s.X+=Set(out a)>0?3:2;WriteLine(s.X);static int Set(out int a){a=90;return 1;}public class S{public int X;public S(int x){X=x;}}", "15\n")]
    [InlineData("bool-and", "bool a=false;a&=Set(out a)?false:true;WriteLine(a);static bool Set(out bool a){a=true;return true;}", "False\n")]
    [InlineData("bool-or", "bool a=false;a|=Set(out a)?false:true;WriteLine(a);static bool Set(out bool a){a=true;return true;}", "False\n")]
    [InlineData("bool-xor", "bool a=false;a^=Set(out a)?false:true;WriteLine(a);static bool Set(out bool a){a=true;return true;}", "False\n")]
    [InlineData("ref-rhs", "int a=12;a+=Set(ref a)>0?3:2;WriteLine(a);static int Set(ref int a){a=90;return 1;}", "15\n")]
    [InlineData("nested-conditional", "int a=12;a+=Set(out a)>0?(a>50?3:2):1;WriteLine(a);static int Set(out int a){a=90;return 1;}", "15\n")]
    [InlineData("short-circuit", "int a=12;a+=Set(out a)&&a>50?3:2;WriteLine(a);static bool Set(out int a){a=90;return true;}", "15\n")]
    [InlineData("conditional-target", "int a=12;int b=20;int c=0;bool flag=true;if(flag){a+=Set(out c)>0?3:2;}else{b+=Set(out c)>0?3:2;}WriteLine(a);WriteLine(b);static int Set(out int a){a=90;return 1;}", "15\n20\n")]
    [InlineData("span-index-once", "Span<int> s=[12,20];int count=0;int a=0;s[Index(ref count)]+=Set(out a)>0?3:2;WriteLine(s[0]);WriteLine(count);static int Index(ref int n){n++;return 0;}static int Set(out int a){a=90;return 1;}", "15\n1\n")]
    [InlineData("increment-index-once", "Span<int> s=[12,20];int count=0;WriteLine(s[Index(ref count)]++);WriteLine(++s[Index(ref count)]);WriteLine(s[Index(ref count)]--);WriteLine(--s[Index(ref count)]);WriteLine(count);static int Index(ref int n){n++;return 0;}", "12\n14\n14\n12\n4\n")]
    [InlineData("nested-struct-field", "Outer s=new Outer(12);s.Inner.X+=Set(out s.Inner.X)>0?3:2;WriteLine(s.Inner.X);static int Set(out int a){a=90;return 1;}public struct Inner{public int X;public Inner(int x){X=x;}}public struct Outer{public Inner Inner;public Outer(int x){Inner=new Inner(x);}}", "15\n")]
    [InlineData("vec-index-once", "var v=new Vec<int>();v.Add(12);int count=0;int a=0;v[Index(ref count)]+=Set(out a)>0?3:2;WriteLine(v[0]);WriteLine(count);static int Index(ref int n){n++;return 0;}static int Set(out int a){a=90;return 1;}", "15\n1\n")]
    [InlineData("setter-indexer-once", "var s=new Counter(12);int count=0;int a=0;s[Index(ref count)]+=Set(out a)>0?3:2;WriteLine(s.Value);WriteLine(s.Gets);WriteLine(s.Sets);WriteLine(count);static int Index(ref int n){n++;return 0;}static int Set(out int a){a=90;return 1;}public struct Counter{public int Value;public int Gets;public int Sets;public Counter(int value){Value=value;Gets=0;Sets=0;}public int this[int index]{get{Gets++;return Value+index;}set{Sets++;Value=value;}}}", "15\n1\n1\n1\n")]
    [InlineData("setter-property-once", "var s=new Counter(12);int a=0;s.Item+=Set(out a)>0?3:2;WriteLine(s.Value);WriteLine(s.Gets);WriteLine(s.Sets);static int Set(out int a){a=90;return 1;}public struct Counter{public int Value;public int Gets;public int Sets;public Counter(int value){Value=value;Gets=0;Sets=0;}public int Item{get{Gets++;return Value;}set{Sets++;Value=value;}}}", "15\n1\n1\n")]
    [InlineData("increment-local", "int a=12;WriteLine(a++);WriteLine(++a);WriteLine(a--);WriteLine(--a);", "12\n14\n14\n12\n")]
    [InlineData("increment-conditional-target", "int a=12;int b=20;bool flag=true;if(flag){WriteLine(a++);WriteLine(++a);}else{WriteLine(b++);WriteLine(++b);}WriteLine(a);WriteLine(b);", "12\n14\n14\n20\n")]
    [InlineData("both-target-branches", "F(true);F(false);static void F(bool flag){int a=12;int b=20;int c=0;if(flag){a+=Set(out c)>0?3:2;}else{b+=Set(out c)>0?3:2;}WriteLine(a);WriteLine(b);}static int Set(out int a){a=90;return 1;}", "15\n20\n12\n23\n")]
    [InlineData("both-rhs-branches", "F(true);F(false);static void F(bool flag){int a=12;a+=flag?Set(out a):Set(out a)+1;WriteLine(a);}static int Set(out int a){a=90;return 3;}", "15\n16\n")]
    [InlineData("increment-setter-indexer", "var s=new Counter(12);int count=0;WriteLine(s[Index(ref count)]++);WriteLine(++s[Index(ref count)]);WriteLine(s[Index(ref count)]--);WriteLine(--s[Index(ref count)]);WriteLine(s.Gets);WriteLine(s.Sets);WriteLine(count);static int Index(ref int n){n++;return 0;}public struct Counter{public int Value;public int Gets;public int Sets;public Counter(int value){Value=value;Gets=0;Sets=0;}public int this[int index]{get{Gets++;return Value+index;}set{Sets++;Value=value;}}}", "12\n14\n14\n12\n4\n4\n4\n")]
    [InlineData("compound-ref-indexer", "var s=new Counter(12);int count=0;int a=0;s[Index(ref count)]+=Set(out a)>0?3:2;WriteLine(s.Value);WriteLine(s.Gets);WriteLine(count);static int Index(ref int n){n++;return 0;}static int Set(out int a){a=90;return 1;}public class Counter{public int Value;public int Gets;public Counter(int value){Value=value;Gets=0;}public ref int this[int index]{[Mut]get{Gets++;return ref Value;}}}", "15\n1\n1\n")]
    [InlineData("compound-from-end-indexer", "var s=new Counter(12);int a=0;s[^1]+=Set(out a)>0?3:2;WriteLine(s.Value);WriteLine(s.Gets);WriteLine(s.LengthGets);static int Set(out int a){a=90;return 1;}public class Counter{public int Value;public int Gets;public int LengthGets;public Counter(int value){Value=value;Gets=0;LengthGets=0;}public int Length{[Mut]get{LengthGets++;return 1;}}public ref int this[int index]{[Mut]get{Gets++;return ref Value;}}}", "15\n1\n1\n")]
    [InlineData("compound-temporary-receiver", "int calls=0;int a=0;WriteLine(Make(ref calls).X+=Set(out a)>0?3:2);WriteLine(calls);static Box Make(ref int calls){calls++;return new Box(12);}static int Set(out int a){a=90;return 1;}public class Box{public int X;public Box(int x){X=x;}}", "15\n1\n")]
    [InlineData("compound-nested-updates", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){int a=12;a+=b?(a+=Set(out a)>0?3:2):(a=6);return a;}static int Set(out int a){a=90;return 1;}", "27\n18\n")]
    public Task UpdateTargets_PreserveOldValueAndSingleEvaluation(string caseName, string source, string expected) =>
        AssertExecutionParity(caseName, source, expected);

    [Theory]
    [InlineData("length", "Span<int> s=[1,2];int n=0;foreach(var e in s){n+=e+s.Length;}WriteLine(n);WriteLine(s.Length);", "7\n2\n")]
    [InlineData("index", "Span<int> s=[1,2];int n=0;foreach(var e in s){n+=e+s[0];}WriteLine(n);WriteLine(s[1]);", "5\n2\n")]
    [InlineData("empty", "Span<int> s=[];int n=0;foreach(var e in s){n+=e+s.Length;}WriteLine(n);WriteLine(s.Length);", "0\n0\n")]
    [InlineData("readonly", "ReadOnlySpan<int> s=[1,2];int n=0;foreach(var e in s){n+=e+s.Length;}WriteLine(n);WriteLine(s.Length);", "7\n2\n")]
    [InlineData("parameter", "Span<int> s=[1,2];WriteLine(F(s));static int F(Span<int> s){int n=0;foreach(var e in s){n+=e+s.Length;}return n+s.Length;}", "9\n")]
    [InlineData("nested", "Span<int> s=[1,2];int n=0;foreach(var e in s){foreach(var f in s){n+=e+f+s.Length;}}WriteLine(n);WriteLine(s[0]);", "20\n1\n")]
    [InlineData("continue-break", "Span<int> s=[1,2,3];int n=0;foreach(var e in s){if(e==1)continue;n+=e+s.Length;if(e==2)break;}s[0]=5;WriteLine(n);WriteLine(s[0]);", "5\n5\n")]
    [InlineData("ref-readonly", "Span<int> s=[1,2];int n=0;foreach(ref readonly var e in s){n+=e+s.Length;}WriteLine(n);WriteLine(s[0]);", "7\n1\n")]
    [InlineData("ref-mutable", "Span<int> s=[1,2];foreach(ref var e in s){e+=1;}WriteLine(s[0]);WriteLine(s[1]);", "2\n3\n")]
    [InlineData("early-return-read", "Span<int> s=[1,2];WriteLine(F(s));static int F(Span<int> s){foreach(var e in s){return e+s.Length;}return 0;}", "3\n")]
    [InlineData("mutate-after-break", "Span<int> s=[1,2];foreach(var e in s){WriteLine(e);break;}Mutate(s);WriteLine(s[0]);static void Mutate(Span<int> s){s[0]=5;}", "1\n5\n")]
    [InlineData("ordinary-view-nll", "Span<int> s=[1,2];ReadOnlySpan<int> r=s;WriteLine(r[0]);s[0]=5;WriteLine(s[0]);", "1\n5\n")]
    [InlineData("shared-call", "Span<int> s=[1,2];foreach(var e in s){Read(s);WriteLine(e);}static void Read(ReadOnlySpan<int> s){WriteLine(s[0]);}", "1\n1\n1\n2\n")]
    [InlineData("nested-break", "Span<int> s=[1,2];int n=0;foreach(var e in s){foreach(var f in s){n+=e+f;break;}n+=s.Length;}s[0]=5;WriteLine(n);WriteLine(s[0]);", "9\n5\n")]
    public Task SpanForeach_ReborrowsRatherThanMoves(string caseName, string source, string expected) =>
        AssertExecutionParity(caseName, source, expected);

    [Theory]
    [InlineData("write", "Span<int> s=[1,2];foreach(var e in s){s[0]=e;}")]
    [InlineData("exclusive-call", "Span<int> s=[1,2];foreach(var e in s){Mutate(s);WriteLine(e);}static void Mutate(Span<int> s){s[0]=0;}")]
    [InlineData("exclusive-slice", "Span<int> s=[1,2];foreach(var e in s){Span<int> t=s.Slice(0,1);t[0]=e;}")]
    [InlineData("ref-read-original", "Span<int> s=[1,2];foreach(ref var e in s){WriteLine(s.Length);e+=1;}")]
    [InlineData("write-break", "Span<int> s=[1,2];foreach(var e in s){s[0]=e;break;}")]
    [InlineData("write-return", "Span<int> s=[1,2];WriteLine(F(s));static int F(Span<int> s){foreach(var e in s){s[0]=e;return s[0];}return 0;}")]
    [InlineData("ref-read-break", "Span<int> s=[1,2];foreach(ref var e in s){e++;WriteLine(s.Length);break;}")]
    [InlineData("write-diverge", "Span<int> s=[1,2];foreach(var e in s){s[0]=e;VibraniumRuntime.Panic(\"stop\");}")]
    [InlineData("exclusive-call-break", "Span<int> s=[1,2];foreach(var e in s){Mutate(s);WriteLine(e);break;}static void Mutate(Span<int> s){s[0]=0;}")]
    [InlineData("move-break", "Span<int> s=[1,2];foreach(var e in s){Span<int> t=Move(s);WriteLine(e+t[0]);break;}")]
    [InlineData("write-infinite-tail", "Span<int> s=[1,2];foreach(var e in s){s[0]=e;while(true){}}")]
    public async Task SpanForeach_RejectsConflictingExclusiveUses(string caseName, string source)
    {
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var frontend = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.NotEmpty(frontend);
        Assert.All(frontend, d => Assert.Equal("VIB003", d.Id));
        var analyzer = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(frontend.Select(d => d.Id).Order(), analyzer.Select(d => d.Id).Order());
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation).Success);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains("VIB003", self.Out + self.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("VIB001", self.Out + self.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpanForeach_ExplicitMoveCollectionConsumesSource()
    {
        using var test = new SourceCase("Span<int> s=[1,2];foreach(var e in Move(s)){WriteLine(e);}WriteLine(s.Length);");
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var frontend = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.Single(frontend, diagnostic => diagnostic.Id == "VIB001");
        var analyzer = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(frontend.Select(d => d.Id).Order(), analyzer.Select(d => d.Id).Order());
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation).Success);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, "Explicitly moved foreach source unexpectedly remained usable");
        Assert.Contains("VIB001", self.Out + self.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int a=12;ref int r=ref a;r+=Set(out r)>0?3:2;WriteLine(r);static int Set(out int a){a=90;return 1;}")]
    [InlineData("var s=new S(12);s.X+=Set(out s.X)>0?3:2;WriteLine(s.X);static int Set(out int a){a=90;return 1;}public class S{public int X;public S(int x){X=x;}}")]
    [InlineData("int a=12;int b=20;bool flag=true;ref int target=ref a;if(!flag)target=ref b;target+=Set(out a)>0?3:2;WriteLine(a);static int Set(out int a){a=90;return 1;}")]
    [InlineData("Span<int> s=[12,20];s[0]+=Set(out s[0])>0?3:2;WriteLine(s[0]);static int Set(out int a){a=90;return 1;}")]
    public async Task UpdateTargets_DoNotWeakenBorrowReservations(string source)
    {
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var diagnostics = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic => Assert.Equal("VIB003", diagnostic.Id));
        Assert.All(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync(), diagnostic => Assert.Equal("VIB003", diagnostic.Id));
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation).Success);
    }

    [Theory]
    [InlineData("int a;a+=Set(out a)>0?3:2;WriteLine(a);static int Set(out int a){a=90;return 1;}")]
    [InlineData("F(out int a);WriteLine(a);static void F(out int a){a+=Set(out a)>0?3:2;}static int Set(out int a){a=90;return 1;}")]
    public async Task CompoundAssignment_RequiresOldValueInitializedBeforeRhs(string source)
    {
        using var test = new SourceCase(source);
        Assert.Contains(test.Compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0165" || diagnostic.Id == "CS0269");
        Assert.Contains(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics, diagnostic => diagnostic.Id == "VIB001");
        Assert.Contains(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync(), diagnostic => diagnostic.Id == "VIB001");
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation).Success);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0);
        Assert.Contains("VIB001", self.Out + self.Err, StringComparison.Ordinal);
    }

    private const string GenericPatternBoundaryMessage = "Patterns other than var and discard on a value of generic type parameter type are not supported in Vibranium v0.2.";

    [Theory]
    [InlineData("c4-01", "WriteLine(F(0));\nWriteLine(F(2));\nstatic bool F<T>(T x) => x is 0;\n", 59)]
    [InlineData("c4-02", "WriteLine(F(1L));\nWriteLine(F(-1L));\nstatic bool F<T>(T x) => x is 0 or 1;\n", 62)]
    [InlineData("c4-03", "WriteLine(F(1));\nWriteLine(F(1L));\nstatic bool F<T>(T x) => x is int;\n", 60)]
    [InlineData("c4-04", "WriteLine(F(new Box(3)));\nstatic int F<T>(T x) => x is Box b ? b.V : 0;\npublic sealed class Box(int v) { public int V = v; }\n", 50)]
    [InlineData("c4-05", "WriteLine(F(1));\nWriteLine(F(-1));\nstatic bool F<T>(T x) => x is int or long;\n", 60)]
    [InlineData("c4-06", "WriteLine(F(3));\nWriteLine(F(-3));\nstatic int F<T>(T x) => x switch { 1 => 1, _ => 0 };\n", 59)]
    [InlineData("c4-07", "WriteLine(F(4));\nstatic int F<T>(T x) => x switch { int i => i, _ => 0 };\n", 41)]
    [InlineData("c4-08", "WriteLine(F(5));\nstatic int F<T>(T x)\n{\n    switch (x)\n    {\n        case 5: return 1;\n        default: return 0;\n    }\n}\n", 52)]
    [InlineData("uncalled-method", "WriteLine(0);public static class C{public static bool F<T>(T x)=>x is 0;}", 65)]
    [InlineData("uncalled-type", "WriteLine(0);public class C<T>{public bool F(T x)=>x is int;}", 51)]
    [InlineData("getter", "var c=new C<int>(1);WriteLine(c.Match);public class C<T>(T x){public bool Match=>x is int;}", 81)]
    [InlineData("declared-type-parameter", "WriteLine(F(1));static bool F<T>(T x)=>x is T y;", 39)]
    [InlineData("field-initializer", "var c=new C<int>(1);WriteLine(c.Match);public class C<T>(T x){public bool Match=x is int;}", 80)]
    public async Task OpenGenericPatterns_HaveOneCanonicalSourceBoundary(string caseName, string source, int expectedStart)
    {
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        bool fieldInitializer = caseName == "field-initializer";
        int expectedCount = fieldInitializer ? 2 : 1;
        var diagnostics = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.Equal(expectedCount, diagnostics.Count);
        var diagnostic = Assert.Single(diagnostics, d => d.Message == GenericPatternBoundaryMessage);
        Assert.Equal("VIB000", diagnostic.Id);
        Assert.Equal(expectedStart, diagnostic.Span.Start);
        var analyzerDiagnostics = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(expectedCount, analyzerDiagnostics.Length);
        var analyzer = Assert.Single(analyzerDiagnostics, d => d.GetMessage() == GenericPatternBoundaryMessage);
        Assert.Equal(diagnostic.Id, analyzer.Id);
        Assert.Equal(expectedStart, analyzer.Location.SourceSpan.Start);
        const string initializerCut = "An 'is' expression is not supported in a field or property initializer in Vibranium v0.2; assign the field in the constructor.";
        if (fieldInitializer)
        {
            // C4 is anchored at x. C23 is a distinct cut and is anchored at is.
            int isStart = expectedStart + 2;
            Assert.Equal("is", source.Substring(isStart, 2));
            var c23 = Assert.Single(diagnostics, d => d.Message == initializerCut);
            Assert.Equal("VIB000", c23.Id);
            Assert.Equal(isStart, c23.Span.Start);
            Assert.Equal(2, c23.Span.Length);
            var analyzerC23 = Assert.Single(analyzerDiagnostics, d => d.GetMessage() == initializerCut);
            Assert.Equal(c23.Id, analyzerC23.Id);
            Assert.Equal(isStart, analyzerC23.Location.SourceSpan.Start);
            Assert.Equal(2, analyzerC23.Location.SourceSpan.Length);
        }
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.False(stage0.Success);
        Assert.Equal(expectedCount, stage0.Errors.Count);
        Assert.Single(stage0.Errors, error => error.Contains(GenericPatternBoundaryMessage, StringComparison.Ordinal));
        if (fieldInitializer)
            Assert.Single(stage0.Errors, error => error.Contains(initializerCut, StringComparison.Ordinal));
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        string selfText = self.Out + self.Err;
        Assert.Single(selfText.Split('\n'), line => line.Contains("VIB000", StringComparison.Ordinal));
        // vib-self does not lower a cut initializer, so its one VIB000 line there is C23's, not C4's.
        Assert.Contains(fieldInitializer ? initializerCut : GenericPatternBoundaryMessage, selfText, StringComparison.Ordinal);
        Assert.DoesNotContain("VIB001", selfText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("c4-09", "WriteLine(F(4));\nstatic int F<T>(T x) => x is var v ? 1 : 0;\n", "1\n")]
    [InlineData("c4-10", "WriteLine(F(4));\nstatic int F<T>(T x) => x switch { _ => 1 };\n", "1\n")]
    [InlineData("c4-11", "WriteLine(F(4));\nstatic T F<T>(T x) => x switch { var v => v };\n", "4\n")]
    [InlineData("ordinary-comparison", "WriteLine(F(1));WriteLine(F(0));static bool F(int x)=>x>0;", "True\nFalse\n")]
    [InlineData("concrete-pattern", "WriteLine(F(1));WriteLine(F(-1));static bool F(int x)=>x is 1;", "True\nFalse\n")]
    public Task OpenGenericPatterns_VarDiscardAndConcreteControlsRemainAccepted(string caseName, string source, string expected) =>
        AssertExecutionParity(caseName, source, expected);

    [Fact]
    public async Task OpenGenericPatternCut_DoesNotHideIndependentCallerInitialization()
    {
        using var test = new SourceCase("int a;WriteLine(F(1));WriteLine(a);static bool F<T>(T x)=>x is 0;");
        Assert.Contains(test.Compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0165");
        var frontend = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.Single(frontend, diagnostic => diagnostic.Id == "VIB000" && diagnostic.Message == GenericPatternBoundaryMessage);
        Assert.Contains(frontend, diagnostic => diagnostic.Id == "VIB001");
        var analyzer = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(frontend.Select(diagnostic => diagnostic.Id).Order(), analyzer.Select(diagnostic => diagnostic.Id).Order());
    }

    public static IEnumerable<object[]> PromotedWarnings()
    {
        foreach (var (id, source) in new[]
        {
            ("CS0183", "int x=1;WriteLine(x is int);"),
            ("CS0184", "int x=1;WriteLine(x is string);"),
            ("CS8519", "WriteLine(0 is 1);"),
            ("CS8520", "WriteLine(0 is 0);"),
            ("CS8793", "WriteLine(0 is not 1);"),
            ("CS8794", "WriteLine(F(1));static bool F(int x)=>x is 0 or not 0;"),
            ("CS8794", "int x=1;WriteLine(x is (int));"),
            ("CS8794", "Pair p=new Pair(1);WriteLine(p is (Pair));public readonly struct Pair(int x){public readonly int X=x;}"),
            ("CS8519", "WriteLine(3.5 is double.NaN);"),
            ("CS8793", "WriteLine(3.5 is not double.NaN);"),
            ("CS8519", "WriteLine(0.0f is float.NaN);"),
            ("CS9191", "int x=1;WriteLine(H(ref x));static int H(in int x)=>x;"),
            ("CS9191", "int x=1;WriteLine(H(p:ref x));static int H(in int p)=>p;"),
            ("CS9191", "int x=1;WriteLine(H<int>(ref x));static int H<T>(in T p)where T:unmanaged=>1;"),
            ("CS9191", "int x=1;var r=new R(ref x);WriteLine(r.X);public class R{public int X;public R(in int p){X=p;}}"),
            ("CS9191", "int x=1;WriteLine(Extensions.H(ref x));public static class Extensions{public static int H(this in int p)=>p;}"),
        })
        {
            foreach (string suppression in new[] { "none", "options", "pragma" })
                yield return [id, source, suppression];
        }
    }

    [Theory]
    [MemberData(nameof(PromotedWarnings))]
    public async Task PromotedWarning_IsSourceBoundaryEvenWhenSuppressed(string id, string source, string suppression)
    {
        if (suppression == "pragma") source = "#pragma warning disable " + id + "\n" + source;
        using var test = new SourceCase(source);
        var compilation = test.Compilation;
        Assert.Equal(new Version(5, 12, 0, 0), typeof(CSharpCompilation).Assembly.GetName().Version);
        if (suppression == "none")
        {
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == id && d.Severity == DiagnosticSeverity.Error);
            var raw = compilation.WithOptions(compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Default)
                .WithSpecificDiagnosticOptions(compilation.Options.SpecificDiagnosticOptions.SetItem(id, ReportDiagnostic.Warn)));
            Assert.Contains(raw.GetDiagnostics(), d => d.Id == id && d.Severity == DiagnosticSeverity.Warning);
        }
        else if (suppression == "options")
        {
            compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                compilation.Options.SpecificDiagnosticOptions.SetItem(id, ReportDiagnostic.Suppress)));
        }
        if (suppression != "none") Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Id == id);
        var frontend = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
        var boundary = Assert.Single(frontend, d => d.Id == "VIB000" && d.Message.Contains(id, StringComparison.Ordinal));
        Assert.StartsWith("C# warning " + id + " is not permitted in Vibranium:", boundary.Message, StringComparison.Ordinal);
        var analyzer = await compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Contains(analyzer, d => d.Id == boundary.Id && d.GetMessage() == boundary.Message);
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, id + " unexpectedly passed vib-self check");
        Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
        Assert.Contains(id, self.Out + self.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImpossibleLogicalPattern_RemainsAHardCompilerError()
    {
        using var test = new SourceCase("WriteLine(F(1));static bool F(int x)=>x is not (0 or not 0);");
        Assert.Contains(test.Compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS8518" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(await test.Compilation.WithAnalyzers(Analyzers()).GetAllDiagnosticsAsync(), diagnostic => diagnostic.Id == "CS8518" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics,
            diagnostic => diagnostic.Message.StartsWith("C# warning CS8518", StringComparison.Ordinal));
        var result = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("CS8518", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("declaration", "int x=1;if(x is int y){WriteLine(y);}", "1\n")]
    [InlineData("var", "if(0 is var x){WriteLine(x);}", "0\n")]
    [InlineData("in", "int x=1;WriteLine(H(in x));static int H(in int x)=>x;", "1\n")]
    [InlineData("nonconstant", "WriteLine(F(0));WriteLine(F(1));static bool F(int x)=>x is 0;", "True\nFalse\n")]
    public Task PromotedWarning_NoWarningControlsRemainAccepted(string caseName, string source, string expected) =>
        AssertExecutionParity(caseName, source, expected);

    public static IEnumerable<object[]> CSharpUnreachableOwnershipCases()
    {
        yield return ["z1_rel_default_use_after_move", "WriteLine(F(3));\nstatic int F(int x)\n{\n    switch (x < 0)\n    {\n        case true: return 1;\n        case false: return 2;\n        default:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n    }\n}\n", "2\n"];
        yield return ["z2_bool_default_use_after_move", "WriteLine(F(true));\nstatic int F(bool b)\n{\n    switch (b)\n    {\n        case true: return 1;\n        case false: return 2;\n        default:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n    }\n}\n", "1\n"];
        yield return ["z3_rel_default_overlap", "WriteLine(F(3));\nstatic int F(int x)\n{\n    switch (x < 0)\n    {\n        case true: return 1;\n        case false: return 2;\n        default:\n        {\n            Option<Cell> o = Some(new Cell(2));\n            return Two(o.AsMut(), o.AsMut());\n        }\n    }\n}\nstatic int Two(OptionMut<Cell> a, OptionMut<Cell> b) => a.IsSome ? 1 : 0;\npublic sealed class Cell(int v) { public int V = v; }\n", "2\n"];
        yield return ["z5_after_return_use_after_move", "WriteLine(F(3));\nstatic int F(int x)\n{\n    return x;\n    Vec<int> v = [1];\n    var w = v;\n    return v.Count;\n}\n", "3\n"];
        yield return ["z8_after_panic_use_after_move", "WriteLine(F(3));\nstatic int F(int x)\n{\n    if (x > 100)\n    {\n        VibraniumRuntime.Panic(\"big\");\n        Vec<int> v = [1];\n        var w = v;\n        return v.Count;\n    }\n    return x;\n}\n", "3\n"];
        yield return ["z9_ushort_default_use_after_move", "WriteLine(F(3));\nstatic int F(ushort x)\n{\n    switch (x)\n    {\n        case var value: return 1;\n        default:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n    }\n}\n", "1\n"];
        yield return ["default-first", "WriteLine(F(3));static int F(int x){switch(x<0){default:{Vec<int> v=[1];var w=v;return v.Count;}case true:return 1;case false:return 2;}}", "2\n"];
        yield return ["default-middle", "WriteLine(F(-3));static int F(int x){switch(x<0){case true:return 1;default:{Vec<int> v=[1];var w=v;return v.Count;}case false:return 2;}}", "1\n"];
        yield return ["constant-guard", "WriteLine(F(3));static int F(int x){switch(x<0){case true:return 1;case false:return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "2\n"];
        yield return ["char-domain", "WriteLine(F('x'));static int F(char x){switch(x){case var value:return 1;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "1\n"];
        yield return ["double-domain", "WriteLine(F(3.5));WriteLine(F(double.NaN));static int F(double x){switch(x){case not double.NaN:return x<0?1:2;case double.NaN:return 3;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "2\n3\n"];
        yield return ["float-domain", "WriteLine(F(-3.5f));WriteLine(F(float.NaN));static int F(float x){switch(x){case not float.NaN:return x<0?1:2;case float.NaN:return 3;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "1\n3\n"];
        yield return ["join-definite-assignment", "WriteLine(F(3));static int F(int x){int result;switch(x<0){case true:result=1;break;case false:result=2;break;default:WriteLine(9);break;}return result;}", "2\n"];
        yield return ["join-owner-live", "WriteLine(F(3));static int F(int x){Vec<int> v=[1];switch(x<0){case true:break;case false:break;default:{var w=v;WriteLine(w.Count);break;}}return v.Count;}", "1\n"];
        yield return ["dead-linear-local", "WriteLine(F(3));static int F(int x){switch(x<0){case true:return 1;case false:return 2;default:{R r=new R();return r.Value;}}}public sealed class R:IDisposable{public int Value=9;public void Dispose(){WriteLine(9);}}", "2\n"];
        yield return ["dead-linear-exit", "WriteLine(F(3));static int F(int x){R r=new R();switch(x<0){case true:Consume(r);return 1;case false:Consume(r);return 2;default:return 0;}}static void Consume(R r){using var owned=r;}public sealed class R:IDisposable{public void Dispose(){WriteLine(9);}}", "9\n2\n"];
        yield return ["constructor-field-flow", "var box=new Box(3);WriteLine(box.C.Value);public sealed class Cell(int value){public int Value=value;}public sealed class Box{public Cell C;public Box(int x){switch(x<0){case true:C=new Cell(1);return;case false:C=new Cell(2);return;default:return;}}}", "2\n"];
        yield return ["nonnull-runtime-pruning", "WriteLine(F(new Box(3)));static int F(Box x){switch(x){case Box b:return b.Value;default:{Vec<int> v=[1];var w=v;return v.Count;}}}public sealed class Box(int value){public int Value=value;}", "3\n"];
    }


    [Theory]
    [InlineData("z4_when_false_use_after_move", "WriteLine(F(3));\nstatic int F(int x)\n{\n    switch (x)\n    {\n        case 3 when /*move*/false:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n        default:\n            return 0;\n    }\n}\n")]
    [InlineData("z6_if_false_use_after_move", "WriteLine(F(3));\nstatic int F(int x)\n{\n    if (/*move*/false)\n    {\n        Vec<int> v = [1];\n        var w = v;\n        WriteLine(v.Count);\n    }\n    return x;\n}\n")]
    public Task ConstantDeadPaths_AreRejectedByTheSubsetCut(string caseName, string source)
    {
        Assert.NotEmpty(caseName);
        return Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConstantConditionMessage);
    }

    [Theory]
    [InlineData("int a=1;int b=2;bool flag=true;ref int target=ref (flag/*move*/?ref a:ref b);WriteLine(target);")]
    [InlineData("int a=1;int b=2;bool flag=true;(flag/*move*/?ref a:ref b)++;WriteLine(a);")]
    public Task ConditionalReferenceTargets_AreRejected(string source) =>
        Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConditionalRefMessage);

    [Theory]
    [MemberData(nameof(CSharpUnreachableOwnershipCases))]
    public Task CSharpUnreachablePaths_HaveNoOwnershipOrLinearObligations(string caseName, string source, string expected) =>
        AssertExecutionParity(caseName, source, expected, allowUnreachableWarning: true);

    [Theory]
    [InlineData("reachable-gap", "WriteLine(F(3));static int F(int x){switch(x){case var negative when negative<0:return 1;case var positive when positive>0:return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "VIB001")]
    [InlineData("reachable-guard", "WriteLine(F(3,true));static int F(int x,bool guard){switch(x){case var negative when negative<0:return 1;case var nonnegative when nonnegative>=0 && guard:return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "VIB001")]
    [InlineData("false-guard", "WriteLine(F(3));static bool Never()=>false;static int F(int x){switch(x){case var negative when negative<0:return 1;case var nonnegative when nonnegative>=0 && Never():return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "VIB001")]
    [InlineData("mixed-default-case", "WriteLine(F(3));static int F(int x){switch(x){case var negative when negative<0:return 1;case var positive when positive>0:return 2;case 0:default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "VIB001")]
    [InlineData("enum-named-values-gap", "WriteLine(F(E.A));static int F(E x){switch(x){case E.A:return 1;case E.B:return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}public enum E{A=1,B=2}", "VIB001")]
    [InlineData("double-nan-gap", "WriteLine(F(3.5));static int F(double x){switch(x){case var negative when negative<0:return 1;case var nonnegative when nonnegative>=0:return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}", "VIB001")]
    [InlineData("reachable-linear-exit", "WriteLine(F(3));static int F(int x){R r=new R();switch(x){case var negative when negative<0:Consume(r);return 1;case var positive when positive>0:Consume(r);return 2;default:return 0;}}static void Consume(R r){using var owned=r;}public sealed class R:IDisposable{public void Dispose(){WriteLine(9);}}", "VIB006")]
    public async Task CSharpReachableDefaults_RetainOwnershipAndLinearObligations(string caseName, string source, string expectedId)
    {
        using var test = new SourceCase(source);
        var compilation = test.Compilation.WithOptions(test.Compilation.Options.WithSpecificDiagnosticOptions(
            test.Compilation.Options.SpecificDiagnosticOptions.SetItem("CS0162", ReportDiagnostic.Warn)));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var frontend = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
        Assert.NotEmpty(frontend);
        Assert.All(frontend, diagnostic => Assert.Equal(expectedId, diagnostic.Id));
        var analyzer = await compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(frontend.Select(diagnostic => diagnostic.Id).Order(), analyzer.Select(diagnostic => diagnostic.Id).Order());
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains(expectedId, self.Out + self.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CSharpUnreachableSourceProof_IsIndependentOfWarningSuppression(bool pragma)
    {
        const string body = "WriteLine(F(3));static int F(int x){switch(x<0){case true:return 1;case false:return 2;default:{Vec<int> v=[1];var w=v;return v.Count;}}}";
        using var test = new SourceCase((pragma ? "#pragma warning disable CS0162\n" : "") + body);
        var compilation = pragma ? test.Compilation : test.Compilation.WithOptions(test.Compilation.Options.WithSpecificDiagnosticOptions(
            test.Compilation.Options.SpecificDiagnosticOptions.SetItem("CS0162", ReportDiagnostic.Suppress)));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0162");
        Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
        Assert.Empty(await compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync());
        Assert.True(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit == 0, self.Out + self.Err);
    }

    private static async Task AssertExecutionParity(string caseName, string source, string expected, bool allowUnreachableWarning = false)
    {
        using var test = new SourceCase(source);
        var compilation = test.Compilation;
        if (allowUnreachableWarning)
        {
            compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                compilation.Options.SpecificDiagnosticOptions.SetItem("CS0162", ReportDiagnostic.Warn)));
        }
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning &&
            !(allowUnreachableWarning && d.Id == "CS0162" && d.Severity == DiagnosticSeverity.Warning)));
        Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
        Assert.Empty(await compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync());

        string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
        var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
        string dll = Path.Combine(test.Directory, "Program.dll");
        var emit = managedCompilation.Emit(dll);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(test.Directory, "Vibranium.Core.dll"));
        File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(test.Directory, "Vibranium.Std.dll"));
        File.WriteAllText(Path.Combine(test.Directory, "Program.runtimeconfig.json"), """
            {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
            """);
        var managed = Run(DotnetExe, dll);
        Assert.Equal((0, expected, ""), managed);

        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
        Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
        string stage0Ir = Path.Combine(test.Directory, "stage0.ll");
        File.WriteAllText(stage0Ir, stage0.LlvmIr);
        string selfIr = Path.Combine(test.Directory, "self.ll");
        foreach (string ir in new[] { stage0Ir, selfIr })
        {
            if (ir == selfIr)
            {
                var self = Run(DotnetExe, SelfCompiler, "emit-ll", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", selfIr);
                Assert.True(self.Exit == 0, caseName + ": " + self.Out + self.Err);
            }
            var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
            Assert.True(verify.Exit == 0, verify.Err);
            foreach (var (optimization, sanitize) in new[] { ("O0", false), ("O3", true) })
            {
                string native = ir + "." + optimization + (sanitize ? ".asan" : ".native");
                var arguments = new List<string> { "-" + optimization, ir, "-o", native };
                if (sanitize) arguments.Add("-fsanitize=address,leak");
                var build = Run("clang-18", arguments.ToArray());
                Assert.True(build.Exit == 0, build.Err);
                Assert.Equal(managed, Run(native));
            }
        }
    }

    private static ImmutableArray<DiagnosticAnalyzer> Analyzers() => [new VibraniumDiagnosticAnalyzer()];

    private sealed class SourceCase : IDisposable
    {
        public string Directory { get; }
        public string Path { get; }
        public CSharpCompilation Compilation { get; }

        public SourceCase(string source)
        {
            Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vibranium-r11-stage0-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Path = System.IO.Path.Combine(Directory, "Program.cs");
            File.WriteAllText(Path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(Path, "Program", includeStd: true);
            Compilation = compilation.WithOptions(compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Error));
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static string RepoRoot
    {
        get
        {
            string path = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(path, "Vibranium.slnx"))) path = System.IO.Directory.GetParent(path)!.FullName;
            return path;
        }
    }

    private static string SelfCompiler
    {
        get
        {
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            return Path.Combine(RepoRoot, "src", "vib-self", "bin", configuration, "net11.0", "vib-self.dll");
        }
    }

    private static string DotnetExe
    {
        get
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
            return File.Exists(local) ? local : "dotnet";
        }
    }

    private static (int Exit, string Out, string Err) Run(string command, params string[] arguments)
    {
        var start = HeavyGateProcess.CreateStartInfo(command, arguments);
        start.Environment["ASAN_OPTIONS"] = "detect_leaks=1:halt_on_error=1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Timed out running " + command);
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
