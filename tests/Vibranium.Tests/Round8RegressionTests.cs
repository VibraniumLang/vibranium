using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round8RegressionTests
{
    private static string RepoRoot
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "Vibranium.slnx")))
            {
                dir = Directory.GetParent(dir)!.FullName;
            }
            return dir;
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

    private static (int Exit, string Out, string Err) Run(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Round8_AssignmentTargetSnapshots_PreserveReferentAndValue_Differential()
    {
        const string source = """
            Simple();
            Compound();
            NestedLeft();
            Field();
            Result();
            Sequential();
            ReadOrder();
            ParamCaller();
            ParamDerivedCaller();
            DisjointFields();
            Returned();
            Branch(true);
            Branch(false);
            Vector();
            static void Simple()
            {
                int a = 1; int b = 2; ref int r = ref a;
                r = (r = ref b);
                WriteLine(r);
                r = 7; WriteLine(a); WriteLine(b);
            }
            static void Compound()
            {
                int a = 1; int b = 2; ref int r = ref a;
                r += (r = ref b);
                WriteLine(r); WriteLine(a); WriteLine(b);
            }
            static void NestedLeft()
            {
                int a = 1; int b = 2; ref int r = ref b;
                (r = ref a) = (r = ref b);
                WriteLine(r); WriteLine(a); WriteLine(b);
            }
            static void Field()
            {
                var a = new Pair(1, 10); var b = new Pair(2, 20); ref Pair r = ref a;
                r.X = (r = ref b).X;
                WriteLine(r.Y); WriteLine(r.X); WriteLine(a.X); WriteLine(a.Y);
                r = ref a;
                int result = r.X += (r = ref b).X;
                WriteLine(result); WriteLine(r.X); WriteLine(a.X); WriteLine(b.X);
            }
            static void Result()
            {
                int a = 1; int b = 2; int c = 3; ref int r = ref a;
                int result = (r = (r = ref b)) + (r = ref c);
                WriteLine(result); WriteLine(r); WriteLine(a); WriteLine(b);
                r = ref a;
                result = (r += (r = ref b)) + (r = ref c);
                WriteLine(result); WriteLine(r); WriteLine(a); WriteLine(b);
            }
            static void Sequential()
            {
                int a = 1; int b = 2; int c = 3; ref int r = ref a;
                r = ref b; r = 4; r = ref c; r = 5;
                WriteLine(a); WriteLine(b); WriteLine(c);
            }
            static void ReadOrder()
            {
                int a = 1; int b = 2; ref int r = ref a;
                int value = r + (r = ref b);
                WriteLine(value); WriteLine(r); WriteLine(a); WriteLine(b);
            }
            static void ParamCaller()
            {
                int a = 1; int b = 2;
                Param(ref a, ref b);
                WriteLine(a); WriteLine(b);
            }
            static void Param(ref int r, ref int other)
            {
                r += other;
                WriteLine(r);
                r = 8;
            }
            static void ParamDerivedCaller()
            {
                int a = 1; int b = 2;
                ParamDerived(ref a, ref b);
                WriteLine(a); WriteLine(b);
            }
            static void ParamDerived(ref int a, ref int b)
            {
                ref int r = ref a;
                r += (r = ref b);
                WriteLine(r);
            }
            static void DisjointFields()
            {
                var a = new Pair(1, 2); ref Pair r = ref a;
                r.X = (r.Y = 3);
                WriteLine(r.X); WriteLine(r.Y);
            }
            static ref int Identity(ref int value) => ref value;
            static void Returned()
            {
                int a = 1; int b = 2;
                ref int r = ref Identity(ref a);
                r = (r = ref b);
                WriteLine(r); WriteLine(a); WriteLine(b);
            }
            static void Branch(bool choose)
            {
                int a = 1; int b = 2; int c = 3; ref int r = ref a;
                if (choose) r = ref b;
                r = (r = ref c);
                WriteLine(r); WriteLine(a); WriteLine(b); WriteLine(c);
            }
            static void Vector()
            {
                Vec<int> v = [1, 2]; int b = 3;
                ref int r = ref v[0];
                r += (r = ref b);
                WriteLine(r); WriteLine(v[0]); WriteLine(v[1]); WriteLine(b);
                v.Add(4); WriteLine(v[2]);
            }
            public struct Pair(int x, int y)
            {
                public int X = x;
                public int Y = y;
            }
            """;
        const string expected = "2,2,7,2,3,2,2,2,2,20,2,2,10,4,2,4,2,5,3,2,2,7,3,4,2,1,4,5,3,2,1,2,3,8,2,2,3,2,3,3,2,2,2,3,1,3,3,3,3,2,3,3,4,2,3,4";
        await AssertDifferential(source, expected.Replace(",", "\n", StringComparison.Ordinal) + "\n");
    }

    [Fact]
    public async Task Round8_Strings_ConstantsAliasesAndConditionals_RemainSupported()
    {
        const string source = """
            const string first = "x";
            const string alias = first;
            string selected = Choose(true, alias, "y");
            WriteLine(selected);
            selected = Choose(false, first, "z");
            WriteLine(selected);
            static string Choose(bool condition, string left, string right) => condition ? left : right;
            """;
        await AssertDifferential(source, "x\nz\n");
    }

    [Fact]
    public async Task Round8_SourceReachability_ConstantLoops_AreCut()
    {
        const string source = """
            // Valid C# constant loops now exercise the C19 rejection boundary.
            WriteLine(A()); WriteLine(B()); WriteLine(C()); WriteLine(D()); WriteLine(E());
            WriteLine(F()); WriteLine(G()); WriteLine(H()); WriteLine(I()); WriteLine(J());
            WriteLine(K()); WriteLine(M());
            return 0;
            static int A() { while (/*move*/1 << 2 == 4) { return 1; } }
            static int B() { while (0x10 > 5) { return 2; } }
            static int C() { while (int.MaxValue > 0) { return 3; } }
            static int D() { while (5 % 2 == 1) { return 4; } }
            static int E() { while ('a' < 'b') { return 5; } }
            static int F() { while (1L == 1) { return 6; } }
            static int G() { while (default(int) == 0) { return 7; } }
            static int H() { while (Mode.A != Mode.B) { return 8; } }
            static int I() { while (unchecked((byte)300) == 44) { return 9; } }
            static int J() { while (2.0 * 3 == 6) { return 10; } }
            static int K() { while (-1 < 0u) { return 11; } }
            static int M() { for (int i = 0; true; i++) { if (i > 3) return 13; } }
            enum Mode { A = 0, B = 1 }
            """;
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConstantConditionMessage);
    }

    [Fact]
    public async Task Round8_SourceReachability_ReturnsAndNestedLoops_Differential()
    {
        const string source = """
            // Each function is VALID C# (no CS0161). vib-self must accept all.
            WriteLine(A()); WriteLine(B(1)); WriteLine(C()); WriteLine(D(true)); WriteLine(E(2));
            WriteLine(F()); WriteLine(G(3)); WriteLine(H(4)); WriteLine(I()); WriteLine(J(true));
            return 0;

            static int A() { bool flag = true; if (flag) return 1; return 0; }
            static int B(int x) { while (true) { if (x > 0) return x; x++; } }
            static int C() { for (;;) { return 3; } }
            static int D(bool b) { do { if (b) return 4; } while (true); }
            static int E(int x) { switch (x) { case 1: return 1; default: return 5; } }
            static int F() { bool flag = true; while (flag) { return 6; } return 0; }
            static int G(int x) { while (x > 0) { return x + 4; } return 0; }
            static int H(int x) { if (x > 0) return x + 4; return 0; }
            static int I() { int n = (int)1.5; while (n == 1) { return 9; } return 0; }
            static int J(bool b) { while (true) { switch (b) { case true: break; case false: break; } return 10; } }
            """;
        await AssertDifferential(source, "1\n1\n3\n4\n5\n6\n7\n8\n9\n10\n");
    }

    [Fact]
    public async Task Round8_SourceReachability_ExhaustiveSwitches_Differential()
    {
        const string source = """
            // switch statement exhaustive on bool / patterns: is end reachable per Roslyn?
            WriteLine(P(true)); WriteLine(Q(5)); WriteLine(S(1.0));
            return 0;
            static int P(bool b) { switch (b) { case true: return 1; case false: return 0; } }
            static int Q(int x) { switch (x) { case var value when value < 0: return -1; default: return 1; } }
            static int S(double d) { switch (d) { case double.NaN: return 3; case var value when value < 0.0: return 1; default: return 2; } }
            """;
        await AssertDifferential(source, "1\n1\n2\n");
    }

    [Fact]
    public async Task Round8_PanicWithSyntacticReturn_RemainsDivergent()
    {
        const string source = """
            WriteLine(F());
            static int F() { VibraniumRuntime.Panic("boom"); return 123; }
            """;
        await AssertDifferential(source, string.Empty, expectedExit: 101, expectedError: "panic: boom\n");
    }

    [Theory]
    [InlineData("while (x || true) { }")]
    [InlineData("if (x || true) return 1;")]
    [InlineData("while (x ? true : true) { return 1; }")]
    [InlineData("while (true && !(x && false)) { return 1; }")]
    [InlineData("VibraniumRuntime.Panic(\"boom\");")]
    [InlineData("if (x) return 1; VibraniumRuntime.Panic(\"boom\");")]
    [InlineData("if (x) { while (true) { break; } } else return 1;")]
    [InlineData("while (x) { while (true) { break; } if (x) return 1; }")]
    public void Round8_SourceReachability_RejectsMissingReturn(string body)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r8-return-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, "WriteLine(F(true)); static int F(bool x) { " + body + " }");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MissingReturn", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == "CS0161");
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
            Assert.Contains("Not all code paths return a value", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task Round8_ConditionalBooleanArms_PreserveDefiniteAssignment_Differential()
    {
        const string source = """
            Parse(true); Parse(false);
            Pattern(true); Pattern(false);
            WriteLine(Guard(false)); WriteLine(Guard(true));
            WriteLine(Pick(true)); WriteLine(Pick(false));
            Nested(true); Nested(false);
            static void Parse(bool c)
            {
                if (c ? int.TryParse("5", out var p) : false) WriteLine(p);
                while (c ? int.TryParse("7", out var q) : false) { WriteLine(q); break; }
            }
            static void Pattern(bool c)
            {
                Option<int> o = Some(3);
                if (c ? o is Some<int>(var v) : false) WriteLine(v);
                while (c ? o is Some<int>(var w) : false) { WriteLine(w); break; }
                if (c && o is Some<int>(var x)) WriteLine(x);
            }
            static int Guard(bool c)
            {
                if (!(c ? false : int.TryParse("6", out var p))) return -1;
                return p;
            }
            static int Pick(bool c) => (c ? int.TryParse("4", out var q) : int.TryParse("5", out q)) ? q : -1;
            static void Nested(bool c)
            {
                if (c ? (c ? int.TryParse("9", out var n) : false) : false) WriteLine(n);
            }
            """;
        await AssertDifferential(source, "5\n7\n3\n3\n3\n6\n-1\n4\n5\n9\n");
    }

    [Fact]
    public async Task Round8_NullRecovery_PreservesIndependentOwnershipDiagnostics()
    {
        const string source = """
            var a = new Node();
            var invalid = a?.Inner;
            var b = new Node();
            var illegalMove = b.Inner;
            int value = 1;
            ref int r = ref value;
            value = 2;
            WriteLine(r);
            public class Node { public Leaf Inner = new Leaf(); }
            public class Leaf { public int W = 3; }
            """;
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r8-null-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "NullRecovery", includeStd: true);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            string[] ids = frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray();
            Assert.Equal(["VIB003", "VIB005", "VIB008"], ids);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(ids, analyzer.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    public static IEnumerable<object[]> RejectedPrograms()
    {
        yield return ["ConditionalTrueArmUninitialized", "VIB001", "bool c=true; if(c?int.TryParse(\"5\",out var p):true) WriteLine(p);"];
        yield return ["ConditionalOutAfterJoinUninitialized", "VIB001", "bool c=true; if(c?int.TryParse(\"5\",out var p):false) WriteLine(p); WriteLine(p);"];
        yield return ["ConditionalPatternUninitialized", "VIB001", "bool c=true; Option<int> o=Some(3); if(c?o is Some<int>(var p):true) WriteLine(p);"];
        yield return ["StringFieldCompoundConcat", "VIB000", "var h=new Holder(); h.Add(); WriteLine(h.Name); class Holder { public string Name=\"a\"; [Mut] public void Add()=>Name+=\"b\"; }"];
        yield return ["NullConditionalField", "VIB008", "var a = new Node(1); var c = a?.Inner; return 0; class Node(int v) { public int V = v; public Leaf Inner = new Leaf(); } class Leaf { public int W = 3; }"];
        yield return ["NullCoalescingClass", "VIB008", "var a = new Node(1); var b = a ?? new Node(2); WriteLine(b.V); return 0; class Node(int v) { public int V = v; }"];
        yield return ["NullForgivingClass", "VIB008", "var a = new Node(1); WriteLine(a!.V); return 0; class Node(int v) { public int V = v; }"];
        yield return ["NullCoalescing", "VIB008", "string s=\"x\"; WriteLine(s??\"y\");"];
        yield return ["NullConditional", "VIB008", "var c=new C(); c?.Print(); public class C { public void Print() { WriteLine(1); } }"];
        yield return ["OldScalarWrite", "VIB003", "int a=1; int b=2; ref int r=ref a; r=(r=ref b)+(a=3); WriteLine(r);"];
        yield return ["OldScalarCompoundWrite", "VIB003", "int a=1; int b=2; ref int r=ref a; r+=(r=ref b)+(a=3); WriteLine(r);"];
        yield return ["OldFieldWrite", "VIB003", "var a=new S(1); var b=new S(2); ref S r=ref a; r.X=(r=ref b).X+(a.X=3); WriteLine(r.X); public struct S(int x) { public int X=x; }"];
        yield return ["OldVectorReallocation", "VIB003", "Vec<int> v=[1,2]; int b=3; ref int r=ref v[0]; r=(r=ref b)+Grow(ref v); WriteLine(r); static int Grow(ref Vec<int> v) { v.Add(4); return 0; }"];
        yield return ["ReturnedOldScalarWrite", "VIB003", "int a=1; int b=2; ref int r=ref Identity(ref a); r=(r=ref b)+(a=3); WriteLine(r); static ref int Identity(ref int x)=>ref x;"];
        yield return ["JoinedOldScalarWrite", "VIB003", "Bad(true); static void Bad(bool choose) { int a=1; int b=2; int c=3; ref int r=ref a; if(choose) r=ref b; r=(r=ref c)+(a=4); WriteLine(r); }"];
        yield return ["NamedReborrowUnchanged", "VIB003", "int a=1; int b=2; ref int r=ref a; ref int s=ref r; r=ref b; a=3; WriteLine(s); WriteLine(r);"];
        yield return ["StringCompoundConcat", "VIB000", "string s=\"x\"; s+=\"y\"; WriteLine(s);"];
        yield return ["StringCompoundResult", "VIB000", "string s=\"x\"; WriteLine(s+=\"y\");"];
        yield return ["StringConstantConcat", "VIB000", "const string s=\"x\"+\"y\"; WriteLine(s);"];
        yield return ["StringRuntimeConcat", "VIB000", "string s=\"x\"; WriteLine(s+\"y\");"];
        yield return ["StringConstantEquality", "VIB000", "WriteLine(\"x\"==\"y\");"];
        yield return ["StringConstantInequality", "VIB000", "WriteLine(\"x\"!=\"y\");"];
    }

    [Theory]
    [MemberData(nameof(RejectedPrograms))]
    public async Task Round8_Rejections_PreserveAnalyzerStage0SelfParity(string name, string expectedId, string source)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r8-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, name + ".cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, name, includeStd: true);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            var ids = frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray();
            Assert.Contains(expectedId, ids);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(ids, analyzer.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Contains(expectedId, self.Out + self.Err, StringComparison.Ordinal);
            var selfIds = Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(id => id).ToArray();
            Assert.Equal(ids, selfIds);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r8-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round8Managed", includeStd: true);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round8Managed.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round8Managed.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            var managed = Run(DotnetExe, dll);
            Assert.Equal((expectedExit, expected, expectedError), managed);

            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
            string c0Ir = Path.Combine(temp, "stage0.ll");
            File.WriteAllText(c0Ir, stage0.LlvmIr);
            string selfIr = Path.Combine(temp, "self.ll");
            var self = Run(DotnetExe, SelfCompiler, "emit-ll", path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", selfIr);
            Assert.True(self.Exit == 0, self.Out + self.Err);
            foreach (string ir in new[] { c0Ir, selfIr })
            {
                var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.Exit == 0, verify.Err);
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + (sanitize ? ".asan" : ".native");
                    var args = new List<string> { "-O3", ir, "-o", native };
                    if (sanitize) args.Add("-fsanitize=address,leak");
                    var build = Run("clang-18", args.ToArray());
                    Assert.True(build.Exit == 0, build.Err);
                    var actual = Run(native);
                    Assert.Equal(managed, actual);
                }
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
