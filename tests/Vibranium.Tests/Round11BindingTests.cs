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

public sealed class Round11BindingTests
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

    [Theory]
    [InlineData("true")]
    [InlineData("1 > 0")]
    [InlineData("0.0 >= 0.0")]
    [InlineData("0.0f >= 0.0f")]
    [InlineData("!(2 < 1)")]
    [InlineData("(1 + 2) == 3")]
    public async Task Round11_ConstantTrueGuards_CompleteScalarExpressionDomains_IsRejectedInVersion02(string guard)
    {
        string source = $$"""
            WriteLine(F(-1)); WriteLine(F(3)); WriteLine(B(true)); WriteLine(B(false)); WriteLine(S(1));
            static int F(int value) => value switch { < 0 => 0, >= 0 when {{guard}} => 1 };
            static int B(bool value) => value switch { true => 2, false when {{guard}} => 3 };
            static int S(int value) { switch (value) { case < 0: return 4; case >= 0 when {{guard}}: return 5; } }
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 19);
    }

    [Fact]
    public async Task Round11_ConstantTrueGuard_UnionAndCatchallBodiesRemainEquivalent_IsRejectedInVersion02()
    {
        const string source = """
            WriteLine(F(true)); WriteLine(F(false)); WriteLine(G(2));
            static Result<int, int> Make(bool first) { if (first) return Ok(4); return Err(7); }
            static int F(bool first) => Make(first) switch {
                Ok<int>(var value) when 0.0 >= 0.0 => value,
                Err<int>(var error) when 1 > 0 => error,
                _ => 9
            };
            static int G(int value) => value switch { var found when true => found };
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 19);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("1 < 0")]
    [InlineData("0.0 < 0.0")]
    public async Task Round11_ConstantFalseGuards_DoNotCompleteExpressionDomain_IsRejectedInVersion02(string guard)
    {
        string source = $$"""
            WriteLine(F(1L));
            static int F(long value) => value switch { < 0L => 0, >= 0L when {{guard}} => 1 };
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 20);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1 > 0")]
    [InlineData("0.0 >= 0.0")]
    public async Task Round11_ConstantTrueGuards_SubsumesLaterArms_IsRejectedInVersion02(string guard)
    {
        string source = $$"""
            WriteLine(F(1));
            static int F(int value) => value switch { >= 0 when {{guard}} => 1, 0 => 2, _ => 3 };
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 20);
    }

    [Fact]
    public async Task Round11_GuardPatternBinding_ShadowsEnclosingConstant()
    {
        const string source = """
            WriteLine(C.F(-2)); WriteLine(C.F(2));
            public static class C {
                public const int Value = 1;
                public static int F(int x) => x switch { int Value when Value > 0 => 1, _ => 2 };
            }
            """;
        await AssertDifferential(source, "2\n1\n");
    }

    [Theory]
    [InlineData("x switch { < 0L => 0, >= 0L when false => 1 }")]
    [InlineData("x switch { < 0L => 0 }")]
    [InlineData("y")]
    public async Task Round11_ResultRemainderBody_IsCheckedAfterBothRuntimeVariants_IsRejectedInVersion02(string expression)
    {
        string source = $$"""
            WriteLine(F(1L));
            static int F(long x) { int y; return Make() switch {
                Ok<int>(var value) => value, Err<int>(var error) => error, _ => {{expression}}
            }; }
            static Result<int, int> Make() => Ok(2);
            """;
        if (expression == "y") AssertRejected(source, "CS0165", null, selfId: "VIB001");
        else await Round12CutSelfTests.AssertRejectedCut(source, 20);
    }

    [Theory]
    [InlineData("x switch { < 0L => 0, >= 0L when false => 1 }", "CS8846")]
    [InlineData("x switch { < 0L => 0 }", "CS8509")]
    [InlineData("y", "CS0165")]
    public async Task Round11_OptionRemainderBody_IsCheckedAfterBothRuntimeVariants(string expression, string roslynId)
    {
        string source = $$"""
            WriteLine(F(1L));
            static int F(long x) { int y; return Make() switch {
                Some<int>(var value) => value, NoneType => 0, _ => {{expression}}
            }; }
            static Option<int> Make() => Some(2);
            """;
        if (expression == "y") AssertRejected(source, roslynId, null, selfId: "VIB001");
        else await Round12CutSelfTests.AssertRejectedCut(source, 20);
    }

    [Theory]
    [InlineData("_", "true")]
    [InlineData("_", "1 > 0")]
    [InlineData("var rest", "true")]
    public async Task Round11_UnionSourceRemainderGuard_RetainsDefiniteAssignment_IsRejectedInVersion02(string pattern, string guard)
    {
        string source = $$"""
            WriteLine(F());
            static int F() { int y; return Make() switch {
                Some<int>(var value) => value, NoneType => 0, {{pattern}} when {{guard}} => y
            }; }
            static Option<int> Make() => Some(2);
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 19);
    }

    [Fact]
    public async Task Round11_ValidNominalRemainder_IsNeverExecuted()
    {
        const string source = """
            WriteLine(F(true)); WriteLine(F(false));
            static Result<int, int> Make(bool first) { if (first) return Ok(4); return Err(7); }
            static int F(bool first) { int assigned = 9; return Make(first) switch {
                Ok<int>(var value) => value, Err<int>(var error) => error,
                _ => Unused(assigned)
            }; }
            static int Unused(int value) { WriteLine(999); return value; }
            """;
        await AssertDifferential(source, "4\n7\n");
    }

    private static void AssertRejected(string source, string roslynId, string? selfMessage, string caseName = "", string selfId = "VIB000")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-binding-reject-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Rejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == roslynId);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            // C#-invalid inputs are rejected by the combined compiler/analyzer
            // pipeline; additional canonical Vibranium diagnostics may coexist.
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAllDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Contains(analyzer, d => d.Id == roslynId && d.Severity == DiagnosticSeverity.Error);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
            Assert.Equal(new[] { selfId }, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(id => id).ToArray());
            if (selfMessage is not null) Assert.Contains(selfMessage, self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "", bool includeUnoptimized = false)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-binding-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round11BindingManaged", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round11BindingManaged.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round11BindingManaged.runtimeconfig.json"), """
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
