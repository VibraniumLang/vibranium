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

public sealed class Round11AuditSelfTests
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

    public static IEnumerable<object[]> ReturnedBorrowRejections()
    {
        yield return new object[] { "lt3b", """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4];
            var s = Pick(a.AsReadOnlySpan(), b.AsReadOnlySpan(), false);
            b.Add(5);
            WriteLine(s[0]);
            return 0;
            [return: LifetimeOf("x")]
            static ReadOnlySpan<int> Pick(ReadOnlySpan<int> x, ReadOnlySpan<int> y, bool f) => f ? x : y;
            """ };
        yield return new object[] { "lt5b", """
            var h = new H([1, 2]);
            Vec<int> b = [3];
            var s = h.Get(b.AsReadOnlySpan());
            b.Add(9);
            WriteLine(s[0]);
            return 0;
            class H(Vec<int> v)
            {
                Vec<int> items = v;
                public ReadOnlySpan<int> Get(ReadOnlySpan<int> other) => other;
            }
            """ };
        yield return new object[] { "lt4", """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4];
            var s = Pick(a.AsReadOnlySpan(), b.AsReadOnlySpan());
            return 0;
            static ReadOnlySpan<int> Pick(ReadOnlySpan<int> x, ReadOnlySpan<int> y) => x;
            """ };
        yield return new object[] { "lt12", """
            var n = new Node(1);
            var m = new Node(2);
            var r = Pick(n.Next.AsRef(), m.Next.AsRef());
            return 0;
            static OptionRef<Node> Pick(OptionRef<Node> x, OptionRef<Node> y) => y;
            class Node(int v) { public int V = v; public Option<Node> Next = None; }
            """ };
        yield return new object[] { "lt11", """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4];
            ref readonly int r = ref Pick(in a, in b);
            b.Add(5);
            WriteLine(r);
            return 0;
            [return: LifetimeOf("x")]
            static ref readonly int Pick(in Vec<int> x, in Vec<int> y) => ref y.AsReadOnlySpan()[0];
            """ };
        yield return new object[] { "lt13", """
            var h = new H([1, 2]);
            Vec<int> b = [3];
            var s = h.Get(in b);
            b.Add(9);
            WriteLine(s[0]);
            return 0;
            class H(Vec<int> v)
            {
                Vec<int> items = v;
                public ReadOnlySpan<int> Get(in Vec<int> other) => other.AsReadOnlySpan();
            }
            """ };
        yield return new object[] { "lt14", """
            var h = new H([1, 2]);
            Vec<int> b = [3];
            ref readonly int s = ref h.Get(ref b);
            b.Add(9);
            WriteLine(s);
            return 0;
            class H(Vec<int> v)
            {
                Vec<int> items = v;
                public ref readonly int Get(ref Vec<int> other) => ref other.AsReadOnlySpan()[0];
            }
            """ };
        yield return new object[] { "e01", """
            var s = F(new Holder());
            WriteLine(s[0]);
            return 0;
            static ReadOnlySpan<int> F(Holder h) => h.Items.AsReadOnlySpan();
            class Holder { public Vec<int> Items = [1, 2]; }
            """ };
        yield return new object[] { "u01", """
            bool c = Cond();
            ReadOnlySpan<int> s = c ? MakeVec().AsReadOnlySpan() : MakeVec().AsReadOnlySpan();
            WriteLine(s[0]);
            return 0;
            static bool Cond() => true;
            static Vec<int> MakeVec() => [1, 2, 3];
            """ };
    }

    [Theory]
    [MemberData(nameof(ReturnedBorrowRejections))]
    public Task ReturnedViews_PreserveParameterContractsAndTemporaryEscape(string caseName, string source)
    {
        Assert.NotEmpty(caseName);
        return AssertSubsetRejected(source, "VIB007");
    }

    [Fact]
    public Task ConditionalArgumentOwnerTemporaries_LiveThroughBothSelectedArms() => AssertDifferential(
        """
        bool c = Cond();
        Vec<int> v = [9];
        WriteLine(Len(c ? v.AsReadOnlySpan() : MakeVec().AsReadOnlySpan()));
        WriteLine(Len(!c ? v.AsReadOnlySpan() : MakeVec().AsReadOnlySpan()));
        return 0;
        static bool Cond() => true;
        static int Len(ReadOnlySpan<int> s) => s.Length;
        static Vec<int> MakeVec() => [1, 2, 3];
        """, "1\n3\n");

    [Theory]
    [InlineData("Vec<int>a=[1,2];Vec<int>b=[3,4];var s=Pick(a.AsReadOnlySpan(),b.AsReadOnlySpan());b.Add(5);WriteLine(s[0]);[return:LifetimeOf(\"x\")]static ReadOnlySpan<int> Pick(ReadOnlySpan<int>x,ReadOnlySpan<int>y)=>x;", "1\n")]
    [InlineData("Vec<int>a=[1,2];var s=First(a.AsReadOnlySpan());WriteLine(s[1]);static ReadOnlySpan<int> First(ReadOnlySpan<int>x)=>x;", "2\n")]
    [InlineData("var h=new H();Vec<int>b=[3];var s=h.Get(b.AsReadOnlySpan());b.Add(9);WriteLine(s[0]);class H{Vec<int>items=[1,2];public ReadOnlySpan<int> Get(ReadOnlySpan<int>other)=>items.AsReadOnlySpan();}", "1\n")]
    public Task ReturnedViews_KeepValidExplicitAndElidedOrigins(string source, string output) => AssertDifferential(source, output);

    [Theory]
    [InlineData("Vec<int>v=[1,2];var s=v.AsSpan();F(s,s,1);static void F(Span<int>a,Span<int>b,int x){a[0]=x;WriteLine(b[0]);}")]
    [InlineData("Vec<int>v=[1,2];var s=v.AsSpan();F(s,s,1);static void F(Span<int>a,ReadOnlySpan<int>b,int x){a[0]=x;WriteLine(b[0]);}")]
    public Task CapturedSpanOperands_ActivateTogetherWithoutSpuriousConflict(string source) => AssertSubsetRejected(source, "VIB002");

    [Fact]
    public Task NestedSpanCall_StillConflictsWithEnclosingLiveOperand() => AssertSubsetRejected(
        "Vec<int>v=[1,2];var s=v.AsSpan();F(s,G(s));static void F(Span<int>a,int x){a[0]=x;}static int G(Span<int>b){b[0]=3;return 1;}", "VIB003");

    [Fact]
    public Task IndependentSpanOperands_ReleaseTheirParentReborrowsAfterInvocation() => AssertDifferential(
        "Vec<int>v=[1,2];Vec<int>w=[5,6];var s=v.AsSpan();var t=w.AsSpan();F(s,t,3);WriteLine(s[0]);WriteLine(t[0]);static void F(Span<int>a,Span<int>b,int x){a[0]=x;b[0]=x+1;}", "3\n4\n");

    [Theory]
    [InlineData("var c=new C();class C{public int F;public C(){F=1;this.M();}void M(){}}")]
    [InlineData("var c=new C();class C{public int F;public int P{get=>F;[Mut]set{F=value;}}public C(){F=1;this.P=2;}}")]
    [InlineData("var c=new C();class C{public int F;public int this[int x]{get=>F+x;[Mut]set{F=value;}}public C(){F=1;WriteLine(this[0]);}}")]
    [InlineData("var c=new C();class C{public int F;public int this[int x]{get=>F+x;[Mut]set{F=value;}}public C(){F=1;this[0]=2;}}")]
    [InlineData("var c=new C();class C{public int F;public C(){F=1;var other=this;WriteLine(other.F);}}")]
    [InlineData("var c=new C();class C{public int F;public C(){F=1;Sink(this);}static void Sink(C c){}}")]
    public Task ConstructorThisRecovery_DoesNotInventDefiniteAssignmentFailures(string source) => AssertSubsetRejected(source, "VIB008");

    private static async Task AssertSubsetRejected(string source, string id, string? message = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-source-borrow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "RejectedBorrow", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(new[] { id }, frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x));
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { id }, analyzer.Select(d => d.Id).Distinct().OrderBy(x => x));
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(new[] { id }, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(x => x));
            if (message is not null)
            {
                Assert.Single(frontend.Diagnostics);
                Assert.Single(analyzer);
                Assert.Single(Regex.Matches(self.Out + self.Err, @"VIB\d{3}"));
                Assert.Equal(message, frontend.Diagnostics[0].Message);
                Assert.Equal(message, analyzer[0].GetMessage());
                Assert.Contains(message, self.Out + self.Err, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "", bool includeUnoptimized = true, string? expectedWarning = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-source-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round11SourceManaged", includeStd: true);
            var sourceDiagnostics = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            if (expectedWarning is null) Assert.Empty(sourceDiagnostics);
            else
            {
                Assert.NotEmpty(sourceDiagnostics);
                Assert.All(sourceDiagnostics, d => { Assert.Equal(expectedWarning, d.Id); Assert.Equal(DiagnosticSeverity.Warning, d.Severity); });
            }
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round11SourceManaged.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round11SourceManaged.runtimeconfig.json"), """
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
                foreach (string optimization in includeUnoptimized ? new[] { "-O0", "-O3" } : new[] { "-O3" })
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + optimization + (sanitize ? ".asan" : ".native");
                    var args = new List<string> { optimization, ir, "-o", native };
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
