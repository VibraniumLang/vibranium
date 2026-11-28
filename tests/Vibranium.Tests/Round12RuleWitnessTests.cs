using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

// Round-12 rule witnesses: one REJECT and one ACCEPT witness per docs/RULES.md rule that existing tests did not
// already witness on all three frontends. Every reject witness runs the Analyzer, Stage-0 and vib-self; every accept
// witness runs all three, then compares the managed run with the Stage-0 and vib-self native binaries at -O0 and at
// -O3 with ASan/LSan. Test names start with the rule id (R3.3-return-provenance -> R3_3_ReturnProvenance_*).
// Programs avoid every construct of cuts C10–C20 so that the witnesses stay valid after those cuts land.
internal static class Round12RuleWitness
{
    internal sealed record Frontends(string[] Stage0, string[] Analyzer, string[] Self, string SelfOutput);

    internal sealed record Accepted((int Exit, string Out, string Err) Managed, string Stage0Ir, string SelfIr, IReadOnlyList<string> Binaries);

    private static string Src => Path.Combine(Round11AuditAssertions.RepoRoot, "src");

    private static string NewTemp(string kind)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-witness-{kind}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        return temp;
    }

    internal static (int Exit, string Out, string Err) Run(string command, byte[]? stdin, params string[] args)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            process.StandardInput.BaseStream.Write(stdin);
            process.StandardInput.Close();
        }
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"{command} exceeded 120 seconds.");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string[] Ids(IEnumerable<string> ids) => ids.Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();

    // Runs the three frontends on a C#-valid program and returns their VIB ID sets.
    // Every witness must be C#-valid; an accept witness must also compile without any C# warning (AGENTS.md rule 3).
    internal static async Task<Frontends> CheckAllAsync(string source, string fileName = "Program.cs", bool includeStd = true, bool requireNoWarnings = false)
    {
        string temp = NewTemp("check");
        try
        {
            string path = Path.Combine(temp, fileName);
            File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12Witness", includeStd: includeStd);
            var floor = requireNoWarnings ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
            var csharp = compilation.GetDiagnostics().Where(d => d.Severity >= floor).ToArray();
            Assert.True(csharp.Length == 0, (requireNoWarnings ? "an accept witness must compile without C# warnings:\n" : "a witness must be C#-valid:\n") + string.Join("\n", csharp.Select(d => d.ToString())));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var args = new List<string> { Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Src };
            if (!includeStd) args.Add("--no-std");
            var self = Run(Round11AuditAssertions.DotnetExe, null, args.ToArray());
            string selfOutput = self.Out + self.Err;
            string[] selfIds = Ids(Regex.Matches(selfOutput, @"VIB\d{3}").Select(m => m.Value));
            Assert.True((self.Exit != 0) == (selfIds.Length > 0), $"vib-self exit {self.Exit} disagrees with its diagnostics:\n{selfOutput}");
            return new Frontends(
                Ids(stage0.Diagnostics.Select(d => d.Id)),
                Ids(analyzer.Select(d => d.Id)),
                selfIds,
                selfOutput);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // REJECT witness for a VIB rule. Every frontend reports `id`; the Analyzer and Stage-0 report the same ID set
    // (AGENTS.md rule 3). Unless the program contains a banned or cut construct, vib-self reports the same set too
    // (SPEC §4 diagnostic parity). With `message`, every frontend reports exactly that message.
    internal static async Task RejectAsync(string source, string id, string? message = null, bool banned = false, string fileName = "Program.cs", bool exact = false)
    {
        var result = await CheckAllAsync(source, fileName);
        if (exact)
        {
            Assert.Equal(new[] { id }, result.Stage0);
            Assert.Equal(new[] { id }, result.Analyzer);
            Assert.True(result.Self.SequenceEqual(new[] { id }), $"vib-self reported [{string.Join(",", result.Self)}], expected exactly [{id}]\n{result.SelfOutput}");
        }
        Assert.True(result.Stage0.Contains(id), $"Stage-0 did not report {id}: [{string.Join(",", result.Stage0)}]");
        Assert.True(result.Analyzer.Contains(id), $"Analyzer did not report {id}: [{string.Join(",", result.Analyzer)}]");
        Assert.True(result.Self.Contains(id), $"vib-self did not report {id}: [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
        Assert.Equal(result.Stage0, result.Analyzer);
        if (!banned) Assert.Equal(result.Stage0, result.Self);
        if (message is not null) await AssertMessageAsync(source, id, message, fileName, result.SelfOutput);
    }

    private static async Task AssertMessageAsync(string source, string id, string message, string fileName, string selfOutput)
    {
        string temp = NewTemp("message");
        try
        {
            string path = Path.Combine(temp, fileName);
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessMessage", includeStd: true);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics.Where(d => d.Id == id).ToArray();
            var analyzer = (await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id == id).ToArray();
            Assert.Contains(stage0, d => d.Message == message);
            Assert.Contains(analyzer, d => d.GetMessage() == message);
            Assert.Contains(message, selfOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // REJECT witness for a rule whose verdict is a C# error (a genuine C# error or a diagnostic the project promotes to an
    // error). SPEC §4 "C# validity": every compiler must reject it, vib-self (vib-stage2) included and on its own. The
    // SDK must report `csId` as an error; Stage-0 must report `csId` (directly or inside its "C# warning … is not
    // permitted" VIB000) or, with `stage0Specific`, a more specific Vibranium diagnostic with that message; vib-self must
    // reject with some VIB error, because it need not match Roslyn's ID or wording.
    internal static async Task RejectCSharpAsync(string source, string csId, string? stage0Specific = null)
    {
        string temp = NewTemp("csharp");
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessCSharp", includeStd: true);
            var all = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAllDiagnosticsAsync();
            Assert.Contains(all, d => d.Id == csId && d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain(all, d => d.Id == "AD0001");
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success);
            Assert.Contains(stage0.Errors, e => e.Contains(csId, StringComparison.Ordinal)
                || (stage0Specific is not null && e.Contains("VIB000: " + stage0Specific, StringComparison.Ordinal)));
            var self = Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Src);
            Assert.True(self.Exit != 0, $"vib-self accepted a C#-invalid program ({csId}); SPEC §4 requires vib-stage2 to reject it:\n" + self.Out + self.Err);
            Assert.Matches(@"error VIB\d{3}", self.Out + self.Err);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // ACCEPT witness: all three frontends accept the program; the managed run produces the expected output; the
    // Stage-0 and vib-self IR pass opt-18 -passes=verify; and both native binaries, built at -O0 and at -O3 with
    // ASan/LSan, reproduce the managed exit code, stdout and stderr exactly. `stdin` and `appArgs` are given to every
    // run; `inspectBinary` sees each native binary (path, built with sanitizers) before it is deleted. An accept
    // witness compiles without any C# warning, except a warning-only witness (SPEC §4 "C# validity"): with
    // `csharpWarning`, the C# compilation must report exactly that warning ID, as a warning, and nothing else.
    internal static Task<Accepted> AcceptAsync(string source, string expected, int expectedExit = 0, string expectedError = "", string? stdin = null, string[]? appArgs = null, Action<string, bool>? inspectBinary = null, string? csharpWarning = null) =>
        AcceptBytesAsync(source, expected, expectedExit, expectedError, stdin is null ? null : Encoding.UTF8.GetBytes(stdin), appArgs, inspectBinary, csharpWarning);

    internal static async Task<Accepted> AcceptBytesAsync(string source, string expected, int expectedExit, string expectedError, byte[]? stdin, string[]? appArgs = null, Action<string, bool>? inspectBinary = null, string? csharpWarning = null)
    {
        var check = await CheckAllAsync(source, requireNoWarnings: csharpWarning is null);
        Assert.True(check.Stage0.Length == 0, "Stage-0 rejected the accept witness: " + string.Join(",", check.Stage0));
        Assert.True(check.Analyzer.Length == 0, "Analyzer rejected the accept witness: " + string.Join(",", check.Analyzer));
        Assert.True(check.Self.Length == 0, "vib-self rejected the accept witness:\n" + check.SelfOutput);

        string temp = NewTemp("accept");
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessManaged", includeStd: true);
            if (csharpWarning is not null)
            {
                var reported = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
                Assert.True(reported.Length > 0 && reported.All(d => d.Id == csharpWarning && d.Severity == DiagnosticSeverity.Warning),
                    $"a warning-only witness must raise exactly C# warning {csharpWarning}, as a warning:\n" + string.Join("\n", reported.Select(d => d.ToString())));
            }
            string dll = Path.Combine(temp, "Round12WitnessManaged.dll");
            string bootstrap = Path.Combine(Src, "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round12WitnessManaged.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            string[] runArgs = appArgs ?? [];
            var managed = Run(Round11AuditAssertions.DotnetExe, stdin, [dll, .. runArgs]);
            Assert.Equal(expected, managed.Out);
            Assert.Equal(expectedError, managed.Err);
            Assert.Equal(expectedExit, managed.Exit);

            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
            string stage0Ir = Path.Combine(temp, "stage0.ll");
            File.WriteAllText(stage0Ir, stage0.LlvmIr);
            string selfIr = Path.Combine(temp, "self.ll");
            var self = Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler, "emit-ll", path, "--sysroot", Src, "--asan", "-o", selfIr);
            Assert.True(self.Exit == 0, self.Out + self.Err);
            var binaries = new List<string>();
            foreach (string ir in new[] { stage0Ir, selfIr })
            {
                var verify = Run("opt-18", null, "-passes=verify", "-disable-output", ir);
                Assert.True(verify.Exit == 0, verify.Err);
                foreach (bool optimizedWithSanitizers in new[] { false, true })
                {
                    string native = ir + (optimizedWithSanitizers ? ".O3.asan" : ".O0");
                    var args = optimizedWithSanitizers
                        ? new[] { "-O3", ir, "-o", native, "-fsanitize=address,leak" }
                        : new[] { "-O0", ir, "-o", native };
                    var build = Run("clang-18", null, args);
                    Assert.True(build.Exit == 0, build.Err);
                    var actual = Run(native, stdin, runArgs);
                    Assert.True(managed == actual, $"{Path.GetFileName(native)} differs from managed:\nmanaged {managed}\nnative  {actual}");
                    inspectBinary?.Invoke(native, optimizedWithSanitizers);
                    binaries.Add(native);
                }
            }
            return new Accepted(managed, File.ReadAllText(stage0Ir), File.ReadAllText(selfIr), binaries);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

// §0 trust boundary and §1 principles, closed subset and entry point.
public sealed class Round12RuleWitnessSubsetTests
{
    private const string Resource = "sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }";

    // R0-fail-closed-pragma: vib and vib-self must reject even when the SDK path is told to suppress.
    private static async Task RejectDespiteSuppressionAsync(string source, string id, string fileName = "Program.cs", string? editorConfig = null, string? selfId = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-witness-suppressed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, fileName);
            File.WriteAllText(path, source);
            if (editorConfig is not null) File.WriteAllText(Path.Combine(temp, ".editorconfig"), editorConfig);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessSuppressed", includeStd: true);
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success, "vib accepted a suppressed program");
            Assert.Contains(stage0.Errors, e => e.Contains(id, StringComparison.Ordinal));
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Contains($"error {selfId ?? id}:", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private const string UseAfterMove = "Vec<int> a = [1];\nvar b = a;\nWriteLine(a.Count);\nWriteLine(b.Count);\n";

    [Fact]
    public Task R0_FailClosedPragma_Reject_PragmaWarningDisable() =>
        RejectDespiteSuppressionAsync("#pragma warning disable VIB001\n" + UseAfterMove + "#pragma warning restore VIB001\n", "VIB001");

    // Stage-0 still checks the underlying move; self fails closed on F15.
    // CONFORMANCE §7.1 does not compare secondary IDs after a subset rejection.
    [Fact]
    public Task R0_FailClosedPragma_Reject_SuppressMessageAttribute() => RejectDespiteSuppressionAsync("""
        P.Run();
        static class P
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Vibranium", "VIB001")]
            public static void Run() { Vec<int> a = [1]; var b = a; WriteLine(a.Count); WriteLine(b.Count); }
        }
        """, "VIB001", selfId: "VIB000");

    [Fact]
    public Task R0_FailClosedPragma_Reject_GeneratedCodeMarkerAndName() =>
        RejectDespiteSuppressionAsync("// <auto-generated/>\n" + UseAfterMove, "VIB001", fileName: "Program.g.cs");

    [Fact]
    public Task R0_FailClosedPragma_Reject_EditorConfigSeverityNone() =>
        RejectDespiteSuppressionAsync(UseAfterMove, "VIB001", editorConfig: "root = true\n[*.cs]\ndotnet_diagnostic.VIB001.severity = none\n");

    // Promoted warnings must stay errors for vib and vib-self under #pragma (F1 class).
    [Theory]
    [InlineData("CS0183", "int x = Pick();\nbool b = x is int;\nWriteLine(b);\nstatic int Pick() => 1;")]
    [InlineData("CS0184", "int x = Pick();\nbool b = x is long;\nWriteLine(b);\nstatic int Pick() => 1;")]
    [InlineData("CS8509", "int x = Pick();\nint r = x switch { 1 => 10 };\nWriteLine(r);\nstatic int Pick() => 1;")]
    [InlineData("CS8519", "const int c = 1;\nbool b = c is 2;\nWriteLine(b);")]
    [InlineData("CS8520", "const int c = 1;\nbool b = c is 1;\nWriteLine(b);")]
    [InlineData("CS8524", "Mode m = Pick();\nint r = m switch { Mode.A => 1, Mode.B => 2 };\nWriteLine(r);\nstatic Mode Pick() => Mode.A;\nenum Mode { A, B }")]
    [InlineData("CS8793", "const int c = 1;\nbool b = c is not 2;\nWriteLine(b);")]
    [InlineData("CS8794", "int x = Pick();\nbool b = x is not 1 or not 2;\nWriteLine(b);\nstatic int Pick() => 1;")]
    [InlineData("CS8846", "int x = Pick();\nint r = x switch { 1 => 10, var y when y == 2 => 20 };\nWriteLine(r);\nstatic int Pick() => 1;")]
    [InlineData("CS9191", "int x = 1;\nWriteLine(F(ref x));\nstatic int F(in int v) => v;")]
    [InlineData("CS9193", "WriteLine(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(5, 1).Length);")]
    public async Task R0_FailClosedPragma_Reject_PromotedWarningUnderPragma(string id, string body)
    {
        string source = $"#pragma warning disable {id}\n{body}\n";
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-witness-promoted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessPromoted", includeStd: true);
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success, $"vib accepted {id} under #pragma warning disable");
            Assert.True(stage0.Errors.Any(e => e.Contains(id, StringComparison.Ordinal)), $"vib did not report {id} under #pragma warning disable: " + string.Join("\n", stage0.Errors));
            // A promoted diagnostic stays a project-level error (SPEC §4 C# validity), and vib-self has no #pragma escape.
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.True(self.Exit != 0, $"vib-self accepted {id} under #pragma warning disable");
            Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
        await Task.CompletedTask;
    }

    // MSBuild <NoWarn> reaches Roslyn as a suppressing specific diagnostic option; vib must still fail closed.
    [Theory]
    [InlineData("VIB001", UseAfterMove)]
    [InlineData("CS8509", "int x = Pick();\nint r = x switch { 1 => 10 };\nWriteLine(r);\nstatic int Pick() => 1;\n")]
    public void R0_FailClosedPragma_Reject_NoWarnOption(string id, string source)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-witness-nowarn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessNoWarn", includeStd: true);
            compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                compilation.Options.SpecificDiagnosticOptions.SetItem(id, ReportDiagnostic.Suppress)));
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success, $"vib accepted the program with {id} suppressed through the compilation options");
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.True(self.Exit != 0, $"vib-self accepted the program:\n{self.Out}{self.Err}");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public Task R0_FailClosedPragma_Accept() => Round12RuleWitness.AcceptAsync(
        "#pragma warning disable VIB001\nVec<int> a = [1];\nvar b = a;\nWriteLine(b.Count);\n#pragma warning restore VIB001\n", "1\n");

    [Fact]
    public Task R1_1_CSharpValid_Reject() => Round12RuleWitness.RejectCSharpAsync("int x = \"a\";\nWriteLine(x);\n", "CS0029");

    [Fact]
    public Task R1_1_CSharpValid_Accept() => Round12RuleWitness.AcceptAsync("int x = 7;\nWriteLine(x);\n", "7\n");

    [Fact]
    public Task R1_1_PromotedCs0184_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = Pick();\nbool b = o is Some<int>(var v) && v == 1;\nWriteLine(b);\nstatic Option<int> Pick() => Some(1);\n", "True\n");

    [Fact]
    public Task R1_1_PromotedCs8509_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "int x = Pick();\nint r = x switch { 1 => 10 };\nWriteLine(r);\nstatic int Pick() => 1;\n", "CS8509");

    [Fact]
    public Task R1_1_PromotedCs8509_Accept() => Round12RuleWitness.AcceptAsync(
        "int x = Pick();\nint r = x switch { 1 => 10, _ => 20 };\nWriteLine(r);\nstatic int Pick() => 2;\n", "20\n");

    // F1: Stage-0 accepts this program and its native binary crashes; SPEC §1.1 requires rejection.
    [Fact]
    public Task R1_1_NonexhaustiveSwitchFailClosed_Reject() => RejectDespiteSuppressionAsync(
        "#pragma warning disable CS8509\nint x = Pick();\nint r = x switch { 1 => 10 };\nWriteLine(r);\nstatic int Pick() => 2;\n", "VIB000");

    [Fact]
    public Task R1_1_NonexhaustiveSwitchFailClosed_Accept() => Round12RuleWitness.AcceptAsync(
        "#pragma warning disable CS8509\nint x = Pick();\nint r = x switch { 1 => 10, var other => other };\nWriteLine(r);\nstatic int Pick() => 2;\n", "2\n");

    [Fact]
    public Task R1_1_PromotedCs8524_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "Mode m = Pick();\nint r = m switch { Mode.A => 1, Mode.B => 2 };\nWriteLine(r);\nstatic Mode Pick() => Mode.A;\nenum Mode { A, B }\n", "CS8524");

    [Fact]
    public Task R1_1_PromotedCs8524_Accept() => Round12RuleWitness.AcceptAsync(
        "Mode m = Pick();\nint r = m switch { Mode.A => 1, Mode.B => 2, _ => 3 };\nWriteLine(r);\nstatic Mode Pick() => Mode.B;\nenum Mode { A, B }\n", "2\n");

    [Fact]
    public Task R1_1_PromotedCs8793_Accept() => Round12RuleWitness.AcceptAsync(
        "int c = Pick();\nbool b = c is not 2;\nWriteLine(b);\nstatic int Pick() => 1;\n", "True\n");

    [Fact]
    public Task R1_1_PromotedCs8793_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "const int c = 1;\nbool b = c is not 2;\nWriteLine(b);\n", "CS8793");

    [Fact]
    public Task R1_1_PromotedCs8794_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "int x = Pick();\nbool b = x is not 1 or not 2;\nWriteLine(b);\nstatic int Pick() => 1;\n", "CS8794");

    // CS9192 without a user ref readonly parameter (cut C10): a BCL ref readonly parameter, itself outside the subset.
    [Fact]
    public Task R1_1_PromotedCs9192_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "int x = 1;\nvar s = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(x, 1);\nWriteLine(s.Length);\n", "CS9192");

    [Fact]
    public Task R1_1_PromotedCs9192_Accept() => Round12RuleWitness.AcceptAsync(
        "int x = 1;\nWriteLine(F(x) + F(in x));\nstatic int F(in int v) => v;\n", "2\n");

    [Fact]
    public Task R1_1_PromotedCs8846_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "int x = Pick();\nint r = x switch { 1 => 10, var y when y == 2 => 20 };\nWriteLine(r);\nstatic int Pick() => 1;\n", "CS8846");

    [Fact]
    public Task R1_1_PromotedCs8846_Accept() => Round12RuleWitness.AcceptAsync(
        "int x = Pick();\nint r = x switch { 1 => 10, var y when y == 2 => 20, _ => 30 };\nWriteLine(r);\nstatic int Pick() => 2;\n", "20\n");

    [Fact]
    public Task R1_1_PromotedCs9193_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "WriteLine(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(5, 1).Length);\n", "CS9193");

    [Fact]
    public Task R1_2_NullLiteral_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> n = None;\nWriteLine(n.IsSome);\nn = Some(new Node());\nWriteLine(n.IsSome);\nsealed class Node { public int V = 1; }\n", "False\nTrue\n");

    [Fact]
    public Task R1_2_NullForgiving_Accept() => Round12RuleWitness.AcceptAsync(
        "bool b = Pick();\nWriteLine(!b);\nstatic bool Pick() => true;\n", "False\n");

    [Fact]
    public Task R1_2_OptionResult_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<int> a = Some(3);
        Option<int> b = None;
        Result<int, int> c = Ok(4);
        Result<int, int> d = Err(5);
        WriteLine(a switch { Some<int>(var x) => x, _ => 0 });
        WriteLine(b.IsSome);
        WriteLine(c.Unwrap());
        if (d.IsErr(out var ok, out var err)) WriteLine(err.Error); else WriteLine(ok);
        """, "3\nFalse\n4\n5\n");

    [Fact]
    public Task R1_3_AffineMove_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> a = [1, 2];
        var b = a;
        WriteLine(b.Count);
        var n = new Node(3);
        var m = n;
        WriteLine(m.V);
        sealed class Node(int v) { public int V = v; }
        """, "2\n3\n");

    [Fact]
    public Task R1_3_LinearExactOnce_Accept() => Round12RuleWitness.AcceptAsync(
        "using var a = new R(1);\nusing var b = new R(2);\nWriteLine(0);\n" + Resource + "\n", "0\n2\n1\n");

    [Fact]
    public Task R1_SubRefAssignValueConsumed_Reject() => Round12RuleWitness.RejectAsync(
        "int a = 1;\nint b = 2;\nref int r = ref a;\nint value = ((r = ref b) = 3);\nWriteLine(value);\n", "VIB000");

    [Fact]
    public Task R1_SubRefAssignValueConsumed_Accept() => Round12RuleWitness.AcceptAsync(
        "int a = 1;\nint b = 2;\nref int r = ref a;\n(r = ref b) = 3;\nWriteLine(a);\nWriteLine(b);\n", "1\n3\n");

    [Fact]
    public Task R1_SubInheritance_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(new D(new B()).Base.X);\nsealed class B { public int X = 1; }\nsealed class D(B b) { public B Base = b; }\n", "1\n");

    [Fact]
    public Task R1_SubGenericPattern_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(F(1));\nstatic bool F<T>(T x) => x is int;\n", "VIB000");

    [Fact]
    public Task R1_SubVirtual_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(new C().F());\nclass C { public virtual int F() => 1; }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubVirtual_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(new C().F());\nclass C { public int F() => 1; }\n", "1\n");

    [Fact]
    public Task R1_SubInterfaces_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(new C().F());\ninterface IShape { int F(); }\nsealed class C : IShape { public int F() => 1; }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubInterfaces_Accept() => Round12RuleWitness.AcceptAsync(
        "using var r = new R(4);\nWriteLine(r.Id);\n" + Resource + "\n", "4\n4\n");

    [Fact]
    public Task R1_SubInterfaceTyped_Accept() => Round12RuleWitness.AcceptAsync(
        "R r = new R(5);\nusing var d = r;\nWriteLine(0);\n" + Resource + "\n", "0\n5\n");

    [Fact]
    public Task R1_SubObjectInitializer_Accept() => Round12RuleWitness.AcceptAsync(
        "var s = new S(1);\nvar t = new S(2);\nt.A = 3;\nWriteLine(s.A + t.A);\nstruct S(int a) { public int A = a; }\n", "4\n");

    [Fact]
    public Task R1_SubObjectDynamic_Reject() => Round12RuleWitness.RejectAsync(
        "object o = 1;\nWriteLine(o.GetHashCode());\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubObjectDynamic_Accept() => Round12RuleWitness.AcceptAsync("int o = 1;\nlong w = o;\nWriteLine(w);\n", "1\n");

    [Fact]
    public Task R1_SubUnionDeclarations_Reject() => Round12RuleWitness.RejectAsync(
        "Pet p = 1;\nWriteLine(p is int);\nunion Pet(int, long);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubUnionDeclarations_Accept() => Round12RuleWitness.AcceptAsync(
        "Result<int, Code> r = Err(Code.Bad);\nif (r.IsErr(out var ok, out var err)) WriteLine((int)err.Error); else WriteLine(ok);\nenum Code { Good, Bad }\n", "1\n");

    [Fact]
    public Task R1_SubRefStruct_Accept() => Round12RuleWitness.AcceptAsync(
        "var v = new View(1);\nWriteLine(v.X);\nstruct View(int x) { public int X = x; }\n", "1\n");

    [Fact]
    public Task R1_SubRefLocalView_Accept() => Round12RuleWitness.AcceptAsync(
        "Span<int> s = [1, 2];\nref int r = ref s[0];\nr = 5;\nWriteLine(s[0]);\n", "5\n");

    [Fact]
    public Task R1_SubExplicitLayout_Reject() => Round12RuleWitness.RejectAsync("""
        WriteLine(new S().A);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct S { [System.Runtime.InteropServices.FieldOffset(0)] public int A; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubExplicitLayout_Accept() => Round12RuleWitness.AcceptAsync(
        "var s = new S();\nWriteLine(s.A);\ns.A = 2;\nWriteLine(s.A);\nstruct S { public int A; }\n", "0\n2\n");

    [Fact]
    public Task R1_SubInlineArray_Reject() => Round12RuleWitness.RejectAsync("""
        var b = new Buf();
        WriteLine(b[0]);
        [System.Runtime.CompilerServices.InlineArray(4)]
        struct Buf { private int _e; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubInlineArray_Accept() => Round12RuleWitness.AcceptAsync(
        "Span<int> b = [0, 0, 0, 0];\nWriteLine(b[0] + b.Length);\n", "4\n");

    [Fact]
    public Task R1_SubUnionProtocol_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "Option<int> o = Some(1);\nWriteLine(o.HasValue);\n", "CS0619");

    [Fact]
    public Task R1_SubUnionProtocol_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = Some(1);\nWriteLine(o.IsSome);\nWriteLine(o is Some<int>(var v) ? v : 0);\n", "True\n1\n");

    [Fact]
    public Task R1_SubDeconstruct_Reject() => Round12RuleWitness.RejectAsync(
        "var s = new Some<int>(3);\ns.Deconstruct(out var v);\nWriteLine(v);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubDeconstruct_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = Some(3);\nif (o is Some<int>(var v)) WriteLine(v);\n", "3\n");

    // Decision F9: tuples (literals, types, deconstruction) are not supported in v0.2.
    [Fact]
    public Task R1_SubTuples_Reject_NoncopyDeconstruction() => Round12RuleWitness.RejectAsync(
        "var (a, b) = (new Vec<int>(), 1);\nWriteLine(a.Count + b);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubTuples_Reject_CopyDeconstruction() => Round12RuleWitness.RejectAsync(
        "var (a, b) = (1, 2);\nWriteLine(a + b);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubTuples_Reject_TupleLiteral() => Round12RuleWitness.RejectAsync(
        "var t = (1, 2);\nWriteLine(t.Item1);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubTuples_Accept() => Round12RuleWitness.AcceptAsync(
        "var p = new Pair(1, 2);\nWriteLine(p.A + p.B);\nreadonly struct Pair(int a, int b) { public readonly int A = a; public readonly int B = b; }\n", "3\n");

    [Fact]
    public Task R1_SubPropertyNoncopy_Reject() => Round12RuleWitness.RejectAsync(
        "var h = new H([1]);\nWriteLine(h.Items.Count);\nsealed class H(Vec<int> items) { private Vec<int> _items = items; public Vec<int> Items => _items; }\n", "VIB005");

    [Fact]
    public Task R1_SubPropertyNoncopy_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new H([1, 2]);\nWriteLine(h.Count);\nsealed class H(Vec<int> items) { private Vec<int> _items = items; public int Count => _items.Count; }\n", "2\n");

    [Fact]
    public Task R1_SubUnsafe_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "WriteLine(F());\nstatic unsafe int F() { int x = 1; int* p = &x; return *p; }\n", "CS0227");

    [Fact]
    public Task R1_SubUnsafe_Accept() => Round12RuleWitness.AcceptAsync(
        "int x = 1;\nref int p = ref x;\np = 2;\nWriteLine(x);\n", "2\n");

    [Fact]
    public Task R1_SubStackalloc_Accept() => Round12RuleWitness.AcceptAsync(
        "Span<int> s = [0, 0, 0, 0];\ns[1] = 2;\nWriteLine(s.Length + s[1]);\n", "6\n");

    [Fact]
    public Task R1_SubAsync_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(1);\nstatic async System.Threading.Tasks.Task F() { await System.Threading.Tasks.Task.Yield(); }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubAsync_Accept() => Round12RuleWitness.AcceptAsync("WriteLine(F());\nstatic int F() => 1;\n", "1\n");

    [Fact]
    public Task R1_SubIterator_Reject() => Round12RuleWitness.RejectAsync(
        "foreach (var x in F()) WriteLine(x);\nstatic System.Collections.Generic.IEnumerable<int> F() { yield return 1; }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubIterator_Accept() => Round12RuleWitness.AcceptAsync(
        "foreach (var x in F()) WriteLine(x);\nstatic Vec<int> F() => [1, 2];\n", "1\n2\n");

    [Fact]
    public Task R1_SubThreading_Reject() => Round12RuleWitness.RejectAsync("System.Threading.Thread.Sleep(1);\nWriteLine(1);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubThreading_Accept() => Round12RuleWitness.AcceptAsync("WriteLine(1);\n", "1\n");

    [Fact]
    public Task R1_SubLambda_Accept() => Round12RuleWitness.AcceptAsync("WriteLine(F(2));\nstatic int F(int x) => x + 1;\n", "3\n");

    [Fact]
    public Task R1_SubCapturingLocalFunction_Reject() => Round12RuleWitness.RejectAsync("WriteLine(F());\nint F() => 1;\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubCapturingLocalFunction_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(F() + new C().G());\nstatic int F() => 1;\nsealed class C { public int G() => 2; }\n", "3\n");

    [Fact]
    public Task R1_SubExceptions_Accept() => Round12RuleWitness.AcceptAsync(
        "Result<int, int> r = Div(4, 0);\nif (r.IsErr(out var ok, out var err)) WriteLine(err.Error); else WriteLine(ok);\nstatic Result<int, int> Div(int a, int b) => b == 0 ? Err(-1) : Ok(a / b);\n", "-1\n");

    [Fact]
    public Task R1_SubFinalizer_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(new C().X);\nsealed class C { public int X = 1; ~C() { } }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubFinalizer_Accept() => Round12RuleWitness.AcceptAsync(
        "{ using var c = new R(1); }\nWriteLine(2);\n" + Resource + "\n", "1\n2\n");

    [Fact]
    public Task R1_SubStaticCtor_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(C.X);\nstatic class C { public const int X = 1; static C() { } }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubStaticCtor_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(C.X);\nstatic class C { public const int X = 1; }\n", "1\n");

    [Fact]
    public Task R1_SubStaticField_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(C.X + C.Y);\nstatic class C { public const int X = 1; public const int Y = X + 1; }\n", "3\n");

    [Fact]
    public Task R1_SubReflection_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(typeof(C).Name);\nsealed class C { }\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubReflection_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(new C().Kind);\nsealed class C { public int Kind = 3; }\n", "3\n");

    [Fact]
    public Task R1_SubArray_Reject() => Round12RuleWitness.RejectAsync("int[] a = new int[3];\nWriteLine(a.Length);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubArray_Accept() => Round12RuleWitness.AcceptAsync("Vec<int> a = [0, 0, 0];\nWriteLine(a.Count);\n", "3\n");

    [Fact]
    public Task R1_SubGoto_Reject() => Round12RuleWitness.RejectAsync(
        "int i = 0;\ntop:\ni++;\nif (i < 3) goto top;\nWriteLine(i);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubGoto_Accept() => Round12RuleWitness.AcceptAsync(
        "int i = 0;\nwhile (true)\n{\n    i++;\n    if (i < 3) continue;\n    break;\n}\nWriteLine(i);\n", "3\n");

    [Fact]
    public Task R1_SubChecked_Accept() => Round12RuleWitness.AcceptAsync(
        "int x = Pick();\nWriteLine(x + 1);\nstatic int Pick() => int.MaxValue;\n", "-2147483648\n");

    [Fact]
    public Task R1_SubGetEnumerator_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1];\nvar e = v.GetEnumerator();\nWriteLine(0);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_SubGetEnumerator_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2];\nforeach (var x in v) WriteLine(x);\n", "1\n2\n");

    [Fact]
    public Task R1_SubUnwhitelistedBcl_Accept() => Round12RuleWitness.AcceptAsync(
        "int x = Pick();\nWriteLine(x < 0 ? -x : x);\nstatic int Pick() => -4;\n", "4\n");

    [Fact]
    public Task R1_EntryPoint_Reject() => Round12RuleWitness.RejectAsync("WriteLine(args.Length);\n", "VIB000", banned: true);

    [Fact]
    public Task R1_EntryPoint_Accept() => Round12RuleWitness.AcceptAsync(
        "static class P { static int Main() { if (Env.Args().IsErr(out var cli, out _)) return 1; WriteLine(cli.Count); return 3; } }\n", "0\n", expectedExit: 3);
}

// SPEC §1 ban list (Round-12 proposal): constructs outside the subset that SPEC did not name before candidate 5.
// Banned constructs: vib-self may report other secondary IDs and messages, so reject witnesses compare the
// construct's own ID only (R4-banned-fail-closed). Each accept witness is the nearest supported form.
public sealed class Round12RuleWitnessBanListTests
{
    [Fact]
    public Task R1_SubNullOperators_Reject_Conditional() => Round12RuleWitness.RejectAsync("""
        var c = new C();
        c?.Touch();
        WriteLine(1);
        sealed class C { public int X = 1; [Mut] public void Touch() { X = 2; } }
        """, "VIB008", banned: true);

    [Fact]
    public Task R1_SubNullOperators_Reject_Coalescing() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        WriteLine(s ?? "b");
        static string Pick() => "a";
        """, "VIB008", banned: true);

    [Fact]
    public Task R1_SubNullOperators_Reject_CoalescingAssignment() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        s ??= "b";
        WriteLine(s);
        static string Pick() => "a";
        """, "VIB008", banned: true);

    [Fact]
    public Task R1_SubNullOperators_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<int> o = Pick();
        WriteLine(o.UnwrapOr(2));
        static Option<int> Pick() => None;
        """, "2\n");

    [Fact]
    public Task R1_SubWith_Reject() => Round12RuleWitness.RejectAsync("""
        var p = new P(1);
        var q = p with { X = 2 };
        WriteLine(q.X);
        struct P(int x) { public int X = x; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubWith_Reject_Empty() => Round12RuleWitness.RejectAsync("""
        var p = new P(1);
        var q = p with { };
        WriteLine(q.X);
        struct P(int x) { public int X = x; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubWith_Accept() => Round12RuleWitness.AcceptAsync("""
        var p = new P(1);
        var q = p;
        q.X = 2;
        WriteLine(p.X + q.X);
        struct P(int x) { public int X = x; }
        """, "3\n");

    [Fact]
    public Task R1_SubSizeof_Reject() => Round12RuleWitness.RejectAsync("""
        WriteLine(sizeof(int));
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubSizeof_Reject_Long() => Round12RuleWitness.RejectAsync("""
        WriteLine(sizeof(long));
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubSizeof_Accept() => Round12RuleWitness.AcceptAsync("""
        long bytes = 3L * 4;
        WriteLine(bytes);
        """, "12\n");

    [Fact]
    public Task R1_SubNameof_Reject_Local() => Round12RuleWitness.RejectAsync("""
        int x = 1;
        WriteLine(nameof(x));
        WriteLine(x);
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubNameof_Reject_Method() => Round12RuleWitness.RejectAsync("""
        WriteLine(nameof(Pick));
        static int Pick() => 1;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubNameof_Accept() => Round12RuleWitness.AcceptAsync("""
        int count = 1;
        WriteLine("count");
        WriteLine(count);
        """, "count\n1\n");

    [Fact]
    public Task R1_SubParams_Reject_Span() => Round12RuleWitness.RejectAsync("""
        WriteLine(F(1, 2, 3));
        static int F(params ReadOnlySpan<int> xs) => xs.Length;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubParams_Reject_Array() => Round12RuleWitness.RejectAsync("""
        WriteLine(F(1, 2));
        static int F(params int[] xs) => 2;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubParams_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(F([1, 2, 3]));
        static int F(ReadOnlySpan<int> xs) => xs.Length;
        """, "3\n");

    [Fact]
    public Task R1_SubDecimal_Reject() => Round12RuleWitness.RejectAsync("""
        decimal d = Pick();
        WriteLine(d > 1);
        static decimal Pick() => 1.5m;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubDecimal_Accept() => Round12RuleWitness.AcceptAsync("""
        long cents = Pick();
        WriteLine(cents > 100);
        static long Pick() => 150;
        """, "True\n");

    [Fact]
    public Task R1_SubExtensionBlock_Reject_Instance() => Round12RuleWitness.RejectAsync("""
        WriteLine(3.Twice());
        static class E { extension(int x) { public int Twice() => x * 2; } }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubExtensionBlock_Reject_Static() => Round12RuleWitness.RejectAsync("""
        WriteLine(int.Seven());
        static class E { extension(int) { public static int Seven() => 7; } }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubExtensionBlock_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(3.Twice());
        WriteLine(E.Twice(4));
        static class E { public static int Twice(this int x) => x * 2; }
        """, "6\n8\n");

    [Fact]
    public Task R1_SubStringOperators_Reject_Equality() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        WriteLine(s == "a");
        static string Pick() => "a";
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubStringOperators_Reject_Inequality() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        WriteLine(s != "a");
        static string Pick() => "a";
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubStringOperators_Reject_Concatenation() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        string t = s + "b";
        WriteLine(t);
        static string Pick() => "a";
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubStringOperators_Reject_ConstantPattern() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        WriteLine(s is "a");
        static string Pick() => "a";
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubStringOperators_Reject_SwitchStatement() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        switch (s)
        {
            case "a": WriteLine(1); break;
            default: WriteLine(2); break;
        }
        static string Pick() => "a";
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubStringOperators_Reject_SwitchExpression() => Round12RuleWitness.RejectAsync("""
        string s = Pick();
        int k = s switch { "a" => 1, _ => 2 };
        WriteLine(k);
        static string Pick() => "a";
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubStringOperators_Accept() => Round12RuleWitness.AcceptAsync("""
        string s = Pick();
        WriteLine(s.AsUtf8().SequenceEqual("a".AsUtf8()));
        static string Pick() => "a";
        """, "True\n");

    [Fact]
    public Task R1_SubUserOperators_Reject_Binary() => Round12RuleWitness.RejectAsync("""
        var a = new P(1);
        var b = a + a;
        WriteLine(b.X);
        struct P(int x) { public int X = x; public static P operator +(P l, P r) => new P(l.X + r.X); }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUserOperators_Reject_Unary() => Round12RuleWitness.RejectAsync("""
        var a = new P(1);
        var b = -a;
        WriteLine(b.X);
        struct P(int x) { public int X = x; public static P operator -(P p) => new P(-p.X); }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUserOperators_Reject_Equality() => Round12RuleWitness.RejectAsync("""
        WriteLine(new P(1) == new P(1));
        struct P(int x) { public int X = x; public static bool operator ==(P l, P r) => l.X == r.X; public static bool operator !=(P l, P r) => l.X != r.X; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUserOperators_Reject_ExplicitConversion() => Round12RuleWitness.RejectAsync("""
        var a = new P(1);
        int k = (int)a;
        WriteLine(k);
        struct P(int x) { public int X = x; public static explicit operator int(P p) => p.X; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUserOperators_Reject_ImplicitConversion() => Round12RuleWitness.RejectAsync("""
        P a = 5;
        WriteLine(a.X);
        struct P(int x) { public int X = x; public static implicit operator P(int v) => new P(v); }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUserOperators_Accept() => Round12RuleWitness.AcceptAsync("""
        var a = new P(1);
        a += 2;
        WriteLine(a.X);
        struct P(int x) { public int X = x; public void operator +=(int d) { X += d; } }
        """, "3\n");

    [Fact]
    public Task R1_SubSpread_Reject_Vec() => Round12RuleWitness.RejectAsync("""
        Vec<int> a = [1, 2];
        Vec<int> b = [..a.AsReadOnlySpan(), 3];
        WriteLine(b.Count);
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubSpread_Reject_Span() => Round12RuleWitness.RejectAsync("""
        ReadOnlySpan<int> a = [1, 2];
        Span<int> b = [..a, 3];
        WriteLine(b.Length);
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubSpread_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> b = [1, 2, 3];
        WriteLine(b.Count);
        """, "3\n");

    [Fact]
    public Task R1_SubExplicitInterface_Reject() => Round12RuleWitness.RejectAsync("""
        using var r = new R(1);
        WriteLine(0);
        sealed class R(int id) : IDisposable { public int Id = id; void IDisposable.Dispose() { WriteLine(Id); } }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubExplicitInterface_Accept() => Round12RuleWitness.AcceptAsync("""
        using var r = new R();
        WriteLine(1);
        sealed class R : IDisposable { public void Dispose() { WriteLine(2); } }
        """, "1\n2\n");

    [Fact]
    public Task R1_SubGotoCase_Reject_Case() => Round12RuleWitness.RejectAsync("""
        int k = Pick();
        switch (k)
        {
            case 1:
                WriteLine(1);
                goto case 2;
            case 2:
                WriteLine(2);
                break;
        }
        static int Pick() => 1;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubGotoCase_Reject_Default() => Round12RuleWitness.RejectAsync("""
        int k = Pick();
        switch (k)
        {
            case 1:
                WriteLine(1);
                goto default;
            default:
                WriteLine(2);
                break;
        }
        static int Pick() => 1;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubGotoCase_Accept() => Round12RuleWitness.AcceptAsync("""
        int k = Pick();
        switch (k)
        {
            case 1:
                WriteLine(1);
                WriteLine(2);
                break;
            default:
                WriteLine(2);
                break;
        }
        static int Pick() => 1;
        """, "1\n2\n");

    [Fact]
    public Task R1_SubAutoProperty_Reject_GetSet() => Round12RuleWitness.RejectAsync("""
        var c = new C();
        c.X = 2;
        WriteLine(c.X);
        sealed class C { public int X { get; set; } }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubAutoProperty_Reject_GetOnly() => Round12RuleWitness.RejectAsync("""
        var c = new C(2);
        WriteLine(c.X);
        sealed class C { public int X { get; } public C(int x) { X = x; } }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubAutoProperty_Reject_Init() => Round12RuleWitness.RejectAsync("""
        var c = new C();
        WriteLine(c.X);
        sealed class C { public int X { get; init; } = 3; }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubAutoProperty_Accept_Accessors() => Round12RuleWitness.AcceptAsync("""
        var c = new C();
        c.X = 4;
        WriteLine(c.X);
        sealed class C { private int _x; public int X { get { return _x; } [Mut] set { _x = value; } } }
        """, "4\n");

    [Fact]
    public Task R1_SubAutoProperty_Accept_ExpressionBodied() => Round12RuleWitness.AcceptAsync("""
        var c = new C(2);
        WriteLine(c.X);
        sealed class C(int x) { private int _x = x; public int X => _x; }
        """, "2\n");

    [Fact]
    public Task R1_SubUsingExisting_Reject() => Round12RuleWitness.RejectAsync("""
        var r = new R(1);
        using (r)
        {
            WriteLine(0);
        }
        sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUsingExisting_Accept() => Round12RuleWitness.AcceptAsync("""
        using (var r = new R(1))
        {
            WriteLine(0);
        }
        using var s = new R(2);
        WriteLine(3);
        sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
        """, "0\n1\n3\n2\n");

    [Fact]
    public Task R1_SubUnchecked_Reject_Checked() => Round12RuleWitness.RejectAsync("""
        int a = Pick();
        int b = checked(a - 1);
        WriteLine(b);
        static int Pick() => 5;
        """, "VIB000", banned: true);

    [Fact]
    public Task R1_SubUnchecked_Accept() => Round12RuleWitness.AcceptAsync("""
        int a = Pick();
        int b = unchecked(a + 1);
        int c;
        unchecked { c = a + 2; }
        WriteLine(b);
        WriteLine(c);
        WriteLine(unchecked((int)0xFFFFFFFF));
        static int Pick() => int.MaxValue;
        """, "-2147483648\n-2147483647\n-1\n");
}

// §2: types, UTF-8 and string, zero-safety and construction, linear resources, generics.
public sealed class Round12RuleWitnessTypeTests
{
    private const string Resource = "sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }";
    private const string Node = "sealed class Node { public int V = 1; }";

    [Fact]
    public Task R2_CopyTypes_Accept() => Round12RuleWitness.AcceptAsync(
        "var a = new P(1, 2);\nvar b = a;\nb.X = 5;\nWriteLine(a.X);\nWriteLine(b.X);\nstruct P(int x, int y) { public int X = x; public int Y = y; }\n", "1\n5\n");

    [Fact]
    public Task R2_UnionCopyIffPayload_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Node> a = Some(new Node());\nvar b = a;\nWriteLine(a.IsSome);\nWriteLine(b.IsSome);\n" + Node + "\n", "VIB001");

    [Fact]
    public Task R2_UnionCopyIffPayload_Reject_Result() => Round12RuleWitness.RejectAsync(
        "Result<int, Node> a = Ok(1);\nvar b = a;\nWriteLine(a.IsOk);\nWriteLine(b.IsOk);\n" + Node + "\n", "VIB001");

    [Fact]
    public Task R2_UnionCopyIffPayload_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> a = Some(1);\nvar b = a;\nResult<int, int> r = Ok(2);\nvar s = r;\nWriteLine(a.Unwrap() + b.Unwrap() + r.Unwrap() + s.Unwrap());\n", "6\n");

    // A view of a by-value owner's heap field is C#-valid but escapes its owner (VIB007 everywhere).
    [Fact]
    public Task R2_U8Static_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(F(new H([1, 2])).Length);\nstatic ReadOnlySpan<byte> F(H h) => h.Bytes.AsReadOnlySpan();\nsealed class H(Vec<byte> b) { public Vec<byte> Bytes = b; }\n", "VIB007");

    [Fact]
    public Task R2_U8Static_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(F(0).Length);
        WriteLine(F(1).Length);
        WriteLine(E().Length);
        Stdio.WriteOut(F(0));
        static ReadOnlySpan<byte> F(int k) => k == 0 ? "zero\n"u8 : "other"u8;
        static ReadOnlySpan<byte> E() => ""u8;
        """, "5\n5\n0\nzero\n");

    [Fact]
    public Task R2_StringCopy_Accept() => Round12RuleWitness.AcceptAsync(
        "string a = \"x\";\nstring b = a;\nWriteLine(a);\nWriteLine(b);\n", "x\nx\n");

    [Fact]
    public Task R2_StringZero_Reject() => Round12RuleWitness.RejectAsync("string s = default;\nWriteLine(s);\n", "VIB008");

    [Fact]
    public Task R2_StringZero_Accept() => Round12RuleWitness.AcceptAsync("string s = \"\";\nWriteLine(s);\nWriteLine(s.AsUtf8().Length);\n", "\n0\n");

    [Fact]
    public Task R2_EnumInt_Accept() => Round12RuleWitness.AcceptAsync("""
        Mode m = (Mode)Pick();
        WriteLine((int)m);
        switch (m)
        {
            case Mode.Read: WriteLine(0); break;
            case Mode.Write: WriteLine(1); break;
            default: WriteLine(2); break;
        }
        static int Pick() => 1;
        enum Mode { Read = 0, Write = 1 }
        """, "1\n1\n");

    [Fact]
    public Task R2_ClassOwned_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> head = None;
        for (int i = 0; i < 5; i++) head = Some(new Node(i, head));
        int total = 0;
        for (var cur = head.AsRef(); cur is Some<Node>(var n); cur = n.Next.AsRef()) total += n.V;
        WriteLine(total);
        sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
        """, "10\n");

    [Fact]
    public Task R2_OwnedStruct_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1];\nvar w = v;\nw.Push(2);\nWriteLine(w.Count);\nvar b = new StringBuf();\nb.Append(\"ab\");\nvar c = b;\nWriteLine(c.Length);\n", "2\n2\n");

    [Fact]
    public Task R2_SharedViews_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2];\nReadOnlySpan<int> a = v.AsReadOnlySpan();\nvar b = a;\nWriteLine(a[0] + b[1]);\nWriteLine(Read(in v));\nstatic int Read(in Vec<int> x) => x[1];\n", "3\n2\n");

    // Decision F4: using a moved OptionMut is exactly {VIB001}.
    [Fact]
    public Task R2_ExclusiveViews_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Node> slot = Some(new Node());\nvar m1 = slot.AsMut();\nvar m2 = m1;\nm1.Set(None);\nWriteLine(m2.IsSome);\n" + Node + "\n", "VIB001", exact: true);

    [Fact]
    public Task R2_ExclusiveViews_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> slot = Some(new Node());\nvar m1 = slot.AsMut();\nvar m2 = m1;\nm2.Set(None);\nWriteLine(slot.IsSome);\n" + Node + "\n", "False\n");

    [Fact]
    public Task R2_StringSurrogate_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(\"é😀\");\nWriteLine(\"é😀\".AsUtf8().Length);\n", "é😀\n6\n");

    [Fact]
    public Task R2_Asutf8Static_Reject() => Round12RuleWitness.RejectAsync(
        "var h = new H(new StringBuf());\nWriteLine(F(h).Length);\nstatic ReadOnlySpan<byte> F(H h) => h.Text[..];\nsealed class H(StringBuf t) { public StringBuf Text = t; }\n", "VIB007");

    [Fact]
    public Task R2_Asutf8Static_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(F().Length);\nStdio.WriteOut(F());\nstatic ReadOnlySpan<byte> F() => \"abc\\n\".AsUtf8();\n", "4\nabc\n");

    [Fact]
    public Task R2_StringOps_Accept() => Round12RuleWitness.AcceptAsync(
        "string s = \"ab\";\nWriteLine(s.AsUtf8().Length);\nWriteLine(s.AsUtf8().SequenceEqual(\"ab\"u8));\n", "2\nTrue\n");

    [Fact]
    public Task R2_Utf8ViewsClosed_Reject() => Round12RuleWitness.RejectAsync(
        "var lines = \"a\\nb\".AsUtf8().Lines();\nWriteLine(0);\n", "VIB000", banned: true);

    [Fact]
    public Task R2_Utf8ViewsClosed_Accept() => Round12RuleWitness.AcceptAsync(
        "foreach (var l in \"a\\nbc\".AsUtf8().Lines()) WriteLine(l.Length);\nforeach (var l in \"x\\r\\ny\".Lines()) WriteLine(l);\n", "1\n2\nx\ny\n");

    // SPEC §2: `foreach (ref var …)` over a closed view is C#-invalid (CS1510) and every compiler rejects it.
    [Fact]
    public Task R2_Utf8ViewsRefForeach_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "foreach (ref var l in \"a\\nb\".AsUtf8().Lines()) WriteLine(l.Length);\n", "CS1510");

    [Fact]
    public Task R2_Utf8ViewsRefForeach_Accept() => Round12RuleWitness.AcceptAsync(
        "foreach (var l in \"a\\nb\".AsUtf8().Lines()) WriteLine(l.Length);\n", "1\n1\n");

    [Fact]
    public Task R2_LinesSplit_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(Count(""));
        WriteLine(Count("a\n"));
        WriteLine(Count("a\r\n"));
        WriteLine(Count("\n"));
        WriteLine(Count("a\n\nb\rc"));
        foreach (var l in "a\r\n\rb".Lines()) WriteLine(l.Length);
        static int Count(string s) { int n = 0; foreach (var l in s.Lines()) n++; return n; }
        """, "0\n1\n1\n1\n4\n1\n0\n1\n");

    [Fact]
    public Task R2_LinesLoan_Reject() => Round12RuleWitness.RejectAsync(
        "var buf = new StringBuf();\nbuf.Append(\"a\\nb\");\nforeach (var l in buf.Lines()) buf.Append(\"x\");\nWriteLine(buf.Length);\n", "VIB003");

    // Decision F3: moving the owner inside the loop is exactly {VIB003}.
    [Fact]
    public Task R2_LinesLoan_Reject_MoveOwner() => Round12RuleWitness.RejectAsync(
        "var buf = new StringBuf();\nbuf.Append(\"a\\nb\");\nforeach (var l in buf.Lines()) Sink(buf);\nstatic void Sink(StringBuf b) { }\n", "VIB003", exact: true);

    [Fact]
    public Task R2_LinesLoan_Accept() => Round12RuleWitness.AcceptAsync("""
        var buf = new StringBuf();
        buf.Append("ab\ncd");
        scoped ReadOnlySpan<byte> last = ""u8;
        foreach (var l in buf.Lines()) last = l;
        WriteLine(last);
        WriteLine(buf.Length);
        """, "cd\n5\n");

    [Fact]
    public Task R2_NativeByteLengths_Accept() => Round12RuleWitness.AcceptAsync(
        "Stdio.WriteOut(\"a\\0b\\n\"u8);\nWrite(\"c\\0d\"u8);\nWriteLine();\n", "a\0b\nc\0d\n");

    [Fact]
    public Task R2_ZeroSafeTypes_Accept() => Round12RuleWitness.AcceptAsync("""
        int i = default;
        Option<int> o = default;
        Span<int> s = default;
        Mode m = default;
        P p = default;
        WriteLine(i);
        WriteLine(o.IsSome);
        WriteLine(s.Length);
        WriteLine((int)m);
        WriteLine(p.X);
        p.X = 4;
        WriteLine(p.X);
        enum Mode { A }
        struct P { public int X; }
        """, "0\nFalse\n0\n0\n0\n4\n");

    [Fact]
    public Task R2_NonZeroSafeTypes_Reject() => Round12RuleWitness.RejectAsync("Result<int, int> r = default;\nWriteLine(r.IsOk);\n", "VIB008");

    [Fact]
    public Task R2_NonZeroSafeTypes_Reject_Some() => Round12RuleWitness.RejectAsync("Some<int> s = default;\nWriteLine(s.Value);\n", "VIB008");

    [Fact]
    public Task R2_1_DefaultNonzerosafe_Reject() => Round12RuleWitness.RejectAsync("Vec<int> v = default;\nWriteLine(v.Count);\n", "VIB008");

    [Fact]
    public Task R2_1_DefaultNonzerosafe_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [];\nvar w = new Vec<int>();\nvar c = new Vec<int>(4);\nWriteLine(v.Count + w.Count + c.Count);\nWriteLine(c.Capacity >= 4);\n", "0\nTrue\n");

    [Fact]
    public Task R2_1_ImplicitNewStruct_Accept() => Round12RuleWitness.AcceptAsync(
        "var p = new P();\nWriteLine(p.X + p.Y);\np.X = 1;\np.Y = 2;\nWriteLine(p.X + p.Y);\nstruct P { public int X; public int Y; }\n", "0\n3\n");

    [Fact]
    public Task R2_1_VecCtor_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(new C().N);\nsealed class C { public Vec<int> V; public int N; public C() { N = 1; } }\n", "VIB008");

    [Fact]
    public Task R2_2_CtorFieldsAssigned_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(new C(true).N);\nsealed class C { public Vec<int> V; public int N; public C(bool b) { if (b) V = []; N = 1; } }\n", "VIB008");

    [Fact]
    public Task R2_2_CtorFieldsAssigned_Accept() => Round12RuleWitness.AcceptAsync(
        "var c = new C();\nWriteLine(c.N + c.Z);\nc.Z = 5;\nWriteLine(c.Z + c.V.Count);\nsealed class C { public int Z; public int N; public Vec<int> V; public C() { N = 1; V = []; } }\n", "1\n5\n");

    [Fact]
    public Task R2_2_CtorThis_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(new C(2).K);\nsealed class C { public int K; public int J; public C(int k) { K = k; J = K + 1; K += Twice(k); } static int Twice(int x) => x * 2; }\n", "6\n");

    [Fact]
    public Task R2_3_PctorSupported_Accept() => Round12RuleWitness.AcceptAsync("""
        var c = new Counter(4);
        WriteLine(c.Next());
        var s = new Scale(3);
        WriteLine(s.Apply(5));
        var h = new Holder([1, 2]);
        WriteLine(h.Count());
        sealed class Counter(int start) { public int Next() => start + 1; }
        readonly struct Scale(int factor) { public int Apply(int x) => x * factor; }
        sealed class Holder(Vec<int> items) { public int Count() => items.Count; }
        """, "5\n15\n2\n");

    [Fact]
    public Task R2_3_PctorMoveOut_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new Holder([1, 2, 3]);\nWriteLine(h.Count());\nsealed class Holder(Vec<int> items) { public int Count() => items.Count; }\n", "3\n");

    [Fact]
    public Task R2_3_PctorDoubleMove_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new H([1, 2], 3);\nWriteLine(h.N());\nsealed class H(Vec<int> v, int k) { public Vec<int> A = v; public int N() => k + A.Count; }\n", "5\n");

    [Fact]
    public Task R2_3_PctorImmutable_Accept() => Round12RuleWitness.AcceptAsync(
        "var c = new C(1);\nWriteLine(c.F());\nsealed class C(int x) { public int F() => x + 1; }\n", "2\n");

    [Fact]
    public Task R2_3_PctorReceiver_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new Holder([1]);\nh.Add(2);\nWriteLine(h.Count());\nsealed class Holder(Vec<int> items) { [Mut] public void Add(int x) { items.Push(x); } public int Count() => items.Count; }\n", "2\n");

    [Fact]
    public Task R2_3_PctorLinear_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new Holder(7);\nusing var r = new R(h.Id());\nWriteLine(0);\nsealed class Holder(int id) { public int Id() => id; }\n" + Resource + "\n", "0\n7\n");

    [Fact]
    public Task R2_LinStructIdisposable_Accept() => Round12RuleWitness.AcceptAsync(
        "using var r = new R(2);\nWriteLine(1);\n" + Resource + "\n", "1\n2\n");

    [Fact]
    public Task R2_LinL1Wrappers_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<R> o = Some(new R(1));\nResult<R, int> r = Ok(new R(2));\nusing var a = o.Unwrap();\nif (r.IsErr(out var ok, out var err)) WriteLine(err.Error); else { using var b = ok; WriteLine(0); }\n" + Resource + "\n",
        "0\n2\n1\n");

    [Fact]
    public Task R2_LinResultErrorCopy_Accept() => Round12RuleWitness.AcceptAsync(
        "Result<R, int> r = Ok(new R(1));\nusing var x = r.Unwrap();\nWriteLine(0);\n" + Resource + "\n", "0\n1\n");

    [Fact]
    public Task R2_LinGenericInstantiation_Reject() => Round12RuleWitness.RejectAsync(
        "var b = new Box<R>(new R(1));\nWriteLine(0);\nsealed class Box<T>(T item) { public T Item = item; }\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R2_LinGenericInstantiation_Reject_InferredMethod() => Round12RuleWitness.RejectAsync(
        "var r = Id(new R(1));\nusing var x = r;\nstatic T Id<T>(T x) => x;\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R2_LinGenericInstantiation_Accept() => Round12RuleWitness.AcceptAsync(
        "var r = Move(new R(1));\nOption<R> o = Some(r);\nusing var x = o.Unwrap();\nWriteLine(0);\n" + Resource + "\n", "0\n1\n");

    [Fact]
    public Task R2_LinUnionConsume_Accept() => Round12RuleWitness.AcceptAsync("""
        if (Open(1).IsNone(out var a)) WriteLine(0); else { using var x = a; }
        using var b = Open(2).Unwrap();
        int n = Move(Open(3)) switch { Some<R>(var r) => Keep(r), _ => 0 };
        WriteLine(n);
        Sink(Open(4));
        static Option<R> Open(int id) => Some(new R(id));
        static int Keep(R r) { using var k = r; return k.Id + 10; }
        static void Sink(Option<R> o) { using var x = o.Unwrap(); }
        sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
        """, "1\n3\n13\n4\n2\n");

    [Fact]
    public Task R2_LinUnionField_Reject() => Round12RuleWitness.RejectAsync(
        "var h = new H(Some(new R(1)));\nWriteLine(0);\nsealed class H(Option<R> r) { public Option<R> Slot = r; }\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R2_LinUnionField_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new H(Some(new Node()));\nWriteLine(h.Slot.IsSome);\nsealed class H(Option<Node> r) { public Option<Node> Slot = r; }\n" + Node + "\n", "True\n");

    [Fact]
    public Task R2_LinUnionUnwrapor_Reject() => Round12RuleWitness.RejectAsync(
        "Option<R> o = Some(new R(1));\nusing var x = o.UnwrapOr(new R(2));\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R2_LinUnionUnwrapor_Accept() => Round12RuleWitness.AcceptAsync("Option<int> o = None;\nWriteLine(o.UnwrapOr(7));\n", "7\n");

    [Fact]
    public Task R2_LinUnionSlotOps_Reject() => Round12RuleWitness.RejectAsync(
        "Option<R> o = Some(new R(1));\nWriteLine(o.AsRef().IsSome);\nusing var x = o.Unwrap();\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R2_LinUnionSlotOps_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> o = Some(new Node());\nWriteLine(o.AsRef().IsSome);\nvar t = o.Take();\nWriteLine(t.IsSome);\nWriteLine(o.IsSome);\n" + Node + "\n", "True\nTrue\nFalse\n");

    [Fact]
    public Task R2_LinUnionPlaceMatch_Reject() => Round12RuleWitness.RejectAsync(
        "Option<R> o = Some(new R(1));\nif (o is Some<R>(var r)) WriteLine(r.Id);\nusing var x = o.Unwrap();\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R2_LinUnionPlaceMatch_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<R> o = Some(new R(1));\nint n = Move(o) switch { Some<R>(var r) => Keep(r), _ => 0 };\nWriteLine(n);\nstatic int Keep(R r) { using var k = r; return k.Id + 1; }\n" + Resource + "\n", "1\n2\n");

    [Fact]
    public Task R2_LinUnionNestedPattern_Reject() => Round12RuleWitness.RejectAsync(
        "Option<R> o = Some(new R(1));\nint n = Move(o) switch { Some<R>({ } r) => Keep(r), _ => 0 };\nstatic int Keep(R r) { using var k = r; return k.Id; }\n" + Resource + "\n", "VIB006", banned: true);

    [Fact]
    public Task R2_LinUnionNestedPattern_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = Some(5);\nint n = o switch { Some<int>(5) => 1, _ => 0 };\nWriteLine(n);\n", "1\n");

    [Fact]
    public Task R2_LinUnionDeconstruct_Reject() => Round12RuleWitness.RejectAsync(
        "var s = new Some<R>(new R(1));\ns.Deconstruct(out var v);\nusing var u = v;\n" + Resource + "\n", "VIB006", banned: true);

    [Fact]
    public Task R2_LinUnionDeconstruct_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<R> o = Some(new R(3));\nif (!Move(o).IsNone(out var v)) { using var u = v; WriteLine(0); }\n" + Resource + "\n", "0\n3\n");

    [Fact]
    public Task R2_LinManualDispose_Accept() => Round12RuleWitness.AcceptAsync(
        "using var r = new R(1);\nWriteLine(0);\n" + Resource + "\n", "0\n1\n");

    [Fact]
    public Task R2_LinRefAlias_Accept() => Round12RuleWitness.AcceptAsync(
        "var r = new R(1);\nWriteLine(F(in r));\nusing var x = r;\nstatic int F(in R r) => r.Id + 4;\n" + Resource + "\n", "5\n1\n");

    [Fact]
    public Task R2_GenConstraints_Accept() => Round12RuleWitness.AcceptAsync(
        "var p = new P();\np.X = 4;\nWriteLine(A(1) + B(p) + C(new Node()));\nstatic int A<T>(T x) where T : unmanaged => 1;\nstatic int B<T>(T x) where T : struct => 2;\nstatic int C<T>(T x) where T : class => 3;\nstruct P { public int X; }\n" + Node + "\n", "6\n");

    // Decision F2: SPEC §2 names this program; it is exactly {VIB005} on every frontend.
    [Fact]
    public Task R2_GenAffineDefinition_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(Dup(1));\nstatic T Dup<T>(in T x) => x;\n", "VIB005", exact: true);

    [Fact]
    public Task R2_GenAffineDefinition_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(Dup(1));\nstatic T Dup<T>(in T x) where T : unmanaged => x;\n", "1\n");

    [Fact]
    public Task R2_GenUnmanagedNonzero_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(Pair(3));\nstatic T Pair<T>(T x) where T : unmanaged { T y = x; return y; }\n", "3\n");

    [Fact]
    public Task R2_GenPolymorphicRecursion_Reject() => Round12RuleWitness.RejectAsync(
        "WriteLine(F(1, 3));\nstatic int F<T>(T x, int n) => n == 0 ? 0 : F(new Wrap<T>(x), n - 1);\nsealed class Wrap<T>(T v) { public T V = v; }\n", "VIB000", banned: true);

    [Fact]
    public Task R2_GenPolymorphicRecursion_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(F(1, 3));\nstatic int F<T>(T x, int n) where T : unmanaged => n == 0 ? 0 : 1 + F(x, n - 1);\n", "3\n");
}

// §3.1–§3.4: receivers and mutation, views and loans, call-site borrows, slots and cursors, lifetimes, pattern matching.
public sealed class Round12RuleWitnessBorrowTests
{
    private const string Node = "sealed class Node { public int V = 1; }";

    // §3.1 receivers and [Mut].
    [Fact]
    public Task R3_1_ClassReceiver_Reject() => Round12RuleWitness.RejectAsync(
        "var c = new C();\nc.Set(2);\nWriteLine(c.X);\nsealed class C { public int X; public void Set(int v) { X = v; } }\n", "VIB004");

    [Fact]
    public Task R3_1_ClassReceiver_Accept() => Round12RuleWitness.AcceptAsync("""
        var c = new C();
        c.Set(2);
        c.P = 3;
        WriteLine(c.X + c.P);
        sealed class C { public int X; private int _p; [Mut] public void Set(int v) { X = v; } public int P { get => _p; [Mut] set => _p = value; } }
        """, "5\n");

    [Fact]
    public Task R3_1_MutProperty_Reject() => Round12RuleWitness.RejectAsync("""
        var c = new C();
        c.P = 2;
        WriteLine(c.P);
        sealed class C { private int _x; private int _reads; [Mut] public int P { get { _reads++; return _x; } set => _x = value; } }
        """, "VIB004");

    [Fact]
    public Task R3_1_MutProperty_Accept() => Round12RuleWitness.AcceptAsync("""
        var c = new C();
        c.P = 2;
        WriteLine(Read(c));
        static int Read(C c) => c.P;
        sealed class C { private int _x; [Mut] public int P { get => _x; set => _x = value; } }
        """, "2\n");

    [Fact]
    public Task R3_1_StructReceiver_Accept() => Round12RuleWitness.AcceptAsync("""
        var s = new S(1);
        s.Bump();
        s += 4;
        WriteLine(s.Read());
        struct S(int x) { public int X = x; public void Bump() { X++; } public void operator +=(int d) { X += d; } public readonly int Read() => X; }
        """, "6\n");

    [Fact]
    public Task R3_1_DefensiveCopy_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> v = [1, 2];
        WriteLine(F(in v));
        static int F(in Vec<int> v) { int t = v[0]; foreach (var x in v) t += x; return t; }
        """, "4\n");

    [Fact]
    public Task R3_1_DeepImmutability_Accept() => Round12RuleWitness.AcceptAsync("""
        var c = new C(new Leaf());
        c.Touch();
        WriteLine(c.L.W);
        sealed class Leaf { public int W = 1; }
        sealed class C(Leaf l) { public Leaf L = l; [Mut] public void Touch() { L.W = 2; } }
        """, "2\n");

    // §3.2 views and loans.
    [Fact]
    public Task R3_2_SpanCopyMove_Reject() => Round12RuleWitness.RejectAsync(
        "Span<int> s = [1, 2];\nSpan<int> t = s;\ns[0] = 3;\nWriteLine(t[0]);\n", "VIB001");

    [Fact]
    public Task R3_2_SpanCopyMove_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> v = [1, 2];
        ReadOnlySpan<int> r = v.AsReadOnlySpan();
        ReadOnlySpan<int> t = r;
        WriteLine(r[0] + t[1]);
        Span<int> s = v.AsSpan();
        Span<int> u = s;
        u[0] = 5;
        WriteLine(u[0]);
        """, "3\n5\n");

    [Fact]
    public Task R3_2_SpanReborrow_Reject() => Round12RuleWitness.RejectAsync(
        "Span<int> s = [1, 2, 3];\nSpan<int> t = s[0..2];\ns[0] = 9;\nt[0] = 1;\nWriteLine(s[0]);\n", "VIB003");

    // Decision F5 (main): a span used directly after Move(s) is exactly VIB001 on every frontend.
    [Fact]
    public Task R3_2_SpanExplicitMove_Reject_DirectUse() => Round12RuleWitness.RejectAsync(
        "Span<int> s = [1, 2];\nConsume(Move(s));\ns[0] = 1;\nstatic void Consume(Span<int> a) { }\n", "VIB001", exact: true);

    [Fact]
    public Task R3_2_SpanExplicitMove_Accept_DirectMove() => Round12RuleWitness.AcceptAsync(
        "Span<int> s = [1, 2];\nWriteLine(Consume(Move(s)));\nstatic int Consume(Span<int> a) => a[0] + a[1];\n", "3\n");

    // Decision F6 (main): a Span/ReadOnlySpan whose element type is not Copy is VIB000 with one exact message.
    private const string SpanElementCopyMessage = "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2.";

    [Fact]
    public Task R3_2_SpanElementCopy_Reject_SpanLocal() => Round12RuleWitness.RejectAsync(
        "Span<Node> s = [new Node()];\nWriteLine(s.Length);\n" + Node + "\n", "VIB000", SpanElementCopyMessage, exact: true);

    [Fact]
    public Task R3_2_SpanElementCopy_Reject_ReadOnlySpanParameter() => Round12RuleWitness.RejectAsync(
        "WriteLine(Count([]));\nstatic int Count(ReadOnlySpan<Node> s) => s.Length;\n" + Node + "\n", "VIB000", SpanElementCopyMessage, exact: true);

    [Fact]
    public Task R3_2_SpanElementCopy_Accept() => Round12RuleWitness.AcceptAsync("""
        Span<int> s = [1, 2];
        Vec<P> v = [new P(3, 4), new P(5, 6)];
        ReadOnlySpan<P> r = v.AsReadOnlySpan();
        WriteLine(s[0] + s[1] + r[0].X + r[1].Y);
        struct P(int x, int y) { public int X = x; public int Y = y; }
        """, "12\n");

    [Fact]
    public Task R3_2_IndexSharedRead_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1];\nF(in v);\nstatic void F(in Vec<int> v) { v[0] = 1; }\n", "VIB004");

    [Fact]
    public Task R3_2_IndexSharedRead_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2, 3];\nWriteLine(F(in v));\nstatic int F(in Vec<int> v) => v[0] + v[^1];\n", "4\n");

    [Fact]
    public Task R3_2_IndexExclusiveWrite_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1];\nF(in v);\nstatic void F(in Vec<int> v) { ref int r = ref v[0]; r = 2; }\n", "VIB004");

    [Fact]
    public Task R3_2_IndexExclusiveWrite_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1];\nref int r = ref v[0];\nr = 2;\nv[0] += 3;\nWriteLine(v[0]);\n", "5\n");

    [Fact]
    public Task R3_2_RangeSlice_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1, 2, 3];\nF(in v);\nstatic void F(in Vec<int> v) { var s = v[1..2]; WriteLine(s.Length); }\n", "VIB004");

    [Fact]
    public Task R3_2_RangeSlice_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2, 3];\nF(in v);\nstatic void F(in Vec<int> v) { var a = v.ReadOnlySlice(1, 1); var b = v.AsReadOnlySpan()[1..3]; WriteLine(a[0] + b.Length); }\n", "4\n");

    [Fact]
    public Task R3_2_IndexRangeValues_Reject_IndexLocal() => Round12RuleWitness.RejectAsync(
        "Index i = ^1;\nVec<int> v = [1];\nWriteLine(v[i]);\n", "VIB000");

    [Fact]
    public Task R3_2_IndexRangeValues_Reject_RangeLocal() => Round12RuleWitness.RejectAsync(
        "Range r = 0..1;\nVec<int> v = [1];\nWriteLine(v[r].Length);\n", "VIB000");

    [Fact]
    public Task R3_2_ForeachVec_Reject_MutationInLoop() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1, 2];\nforeach (var x in v) v.Push(x);\n", "VIB003");

    [Fact]
    public Task R3_2_ForeachVec_Reject_RefLoopOverSharedPlace() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1, 2];\nF(in v);\nstatic void F(in Vec<int> v) { foreach (ref var x in v) x += 1; }\n", "VIB004");

    [Fact]
    public Task R3_2_ForeachVec_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> v = [1, 2];
        WriteLine(Sum(in v));
        foreach (ref var x in v) x += 1;
        WriteLine(Sum(in v));
        static int Sum(in Vec<int> v) { int t = 0; foreach (var x in v) t += x; return t; }
        """, "3\n5\n");

    // §3.3 call-site borrows, slots, cursors and lifetimes.
    [Fact]
    public Task R3_3_ReservedTarget_Accept() => Round12RuleWitness.AcceptAsync("""
        var h = new H(new Node());
        h.N = Make(5);
        WriteLine(h.N.V);
        static Node Make(int v) { var n = new Node(); n.V = v; return n; }
        sealed class Node { public int V = 1; }
        sealed class H(Node n) { public Node N = n; }
        """, "5\n");

    [Fact]
    public Task R3_3_SharedAlias_Reject() => Round12RuleWitness.RejectAsync(
        "int x = 1;\nG(ref x, in x);\nstatic void G(ref int a, in int b) { a = b; }\n", "VIB002");

    [Fact]
    public Task R3_3_SharedAlias_Accept() => Round12RuleWitness.AcceptAsync("""
        int x = 1;
        Vec<int> v = [1, 2];
        WriteLine(F(in x, in x) + H(v.AsReadOnlySpan(), v.AsReadOnlySpan()));
        static int F(in int a, in int b) => a + b;
        static int H(ReadOnlySpan<int> a, ReadOnlySpan<int> b) => a[0] + b[1];
        """, "5\n");

    // A slot projection through a property is a C# error (CS0206 on the ref receiver of Take); vib-self rejects it too.
    [Fact]
    public Task R3_3_SlotFields_Reject() => Round12RuleWitness.RejectCSharpAsync("""
        var h = new H();
        var t = h.Next.Take();
        WriteLine(t.IsSome);
        sealed class Node { public int V = 1; }
        sealed class H { private Option<Node> _n = None; public Option<Node> Next { get => _n; [Mut] set => _n = value; } }
        """, "CS0206");

    [Fact]
    public Task R3_3_SlotFields_Accept() => Round12RuleWitness.AcceptAsync("""
        var h = new H();
        h.Next = Some(new Node());
        var t = h.Next.Take();
        WriteLine(t.IsSome);
        WriteLine(h.Next.IsSome);
        sealed class Node { public int V = 1; }
        sealed class H { public Option<Node> Next = None; }
        """, "True\nFalse\n");

    [Fact]
    public Task R3_3_OptionMutAsRefFreeze_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Node> slot = Some(new Node());\nvar m = slot.AsMut();\nvar r = m.AsRef();\nm.Set(None);\nWriteLine(r.IsSome);\n" + Node + "\n", "VIB003");

    [Fact]
    public Task R3_3_OptionMutAsRefFreeze_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> slot = Some(new Node());\nvar m = slot.AsMut();\nvar r = m.AsRef();\nWriteLine(r.IsSome);\nm.Set(None);\nWriteLine(slot.IsSome);\n" + Node + "\n", "True\nFalse\n");

    [Fact]
    public Task R3_3_CursorProvenance_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> head = Some(new Node(1, None));
        Append(ref head, 2);
        Append(ref head, 3);
        RemoveFirst(ref head, 2);
        int t = 0;
        for (var c = head.AsRef(); c is Some<Node>(var n); c = n.Next.AsRef()) t = t * 10 + n.V;
        WriteLine(t);
        static void Append(ref Option<Node> head, int v) { var s = head.AsMut(); while (s is Some<Node>(var n)) s = n.Next.AsMut(); s.Set(Some(new Node(v, None))); }
        static void RemoveFirst(ref Option<Node> head, int v) { for (var s = head.AsMut(); s is Some<Node>(var n); s = n.Next.AsMut()) { if (n.V != v) continue; s.Set(n.Next.Take()); return; } }
        sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
        """, "13\n");

    [Fact]
    public Task R3_3_ReturnEscape_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> v = [1, 2];
        WriteLine(F(in v)[1]);
        WriteLine(G(ref v)[0]);
        static ReadOnlySpan<int> F(in Vec<int> v) => v.AsReadOnlySpan();
        static Span<int> G(ref Vec<int> v) => v.AsSpan();
        """, "2\n1\n");

    [Fact]
    public Task R3_3_ElisionAmbiguous_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> a = [1];
        Vec<int> b = [2];
        WriteLine(Pick(a.AsReadOnlySpan(), b.AsReadOnlySpan(), Flag())[0]);
        static bool Flag() => true;
        [return: LifetimeOf("x", "y")]
        static ReadOnlySpan<int> Pick(ReadOnlySpan<int> x, ReadOnlySpan<int> y, bool f) => f ? x : y;
        """, "1\n");

    // Returning a scoped parameter is a C# ref-safety error (CS8352); vib-self reports VIB007.
    [Fact]
    public Task R3_3_Scoped_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "Vec<int> a = [1];\nWriteLine(F(a.AsReadOnlySpan())[0]);\nstatic ReadOnlySpan<int> F(scoped ReadOnlySpan<int> x) => x;\n", "CS8352");

    [Fact]
    public Task R3_3_Scoped_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> a = [1];
        Vec<int> b = [2, 3];
        WriteLine(F(a.AsReadOnlySpan(), b.AsReadOnlySpan())[0]);
        static ReadOnlySpan<int> F(scoped ReadOnlySpan<int> x, ReadOnlySpan<int> y) => x.Length > 5 ? y[1..] : y;
        """, "2\n");

    [Fact]
    public Task R3_3_LifetimeOfPlacement_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> a = [1];\nWriteLine(F(a.AsReadOnlySpan())[0]);\nstatic ReadOnlySpan<int> F([LifetimeOf(\"x\")] ReadOnlySpan<int> x) => x;\n", "VIB000");

    [Fact]
    public Task R3_3_LifetimeOfPlacement_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> a = [1];
        Vec<int> b = [2];
        Vec<int> c = [3];
        var r = Pick(a.AsReadOnlySpan(), b.AsReadOnlySpan(), Flag());
        c.Push(3);
        WriteLine(r[0] + c.Count);
        static bool Flag() => true;
        [return: LifetimeOf("x", "y")]
        static ReadOnlySpan<int> Pick(ReadOnlySpan<int> x, ReadOnlySpan<int> y, bool f) => f ? x : y;
        """, "3\n");

    [Fact]
    public Task R3_3_ViewRefParam_Accept() => Round12RuleWitness.AcceptAsync(
        "Span<int> s = [1];\nF(s);\nWriteLine(s[0]);\nstatic void F(Span<int> s) { s[0] = 2; }\n", "2\n");

    // §3.4 pattern matching.
    [Fact]
    public Task R3_4_UnionBoxing_Reject_ObjectPattern() => Round12RuleWitness.RejectAsync(
        "Option<int> o = Some(1);\nWriteLine(o is object x);\n", "VIB000");

    // Decision F7 (main): `{ } x` on a union is VIB000; only `var x` binds the union directly.
    [Fact]
    public Task R3_4_UnionBoxing_Reject_EmptyPropertyPattern() => Round12RuleWitness.RejectAsync(
        "Option<int> o = Some(1);\nif (o is { } y) WriteLine(y.IsSome);\n", "VIB000", exact: true);

    [Fact]
    public Task R3_4_UnionBoxing_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = Some(1);\nif (o is var x) WriteLine(x.IsSome);\nOption<int> n = None;\nif (n is var y) WriteLine(y.IsSome);\n", "True\nFalse\n");

    [Fact]
    public Task R3_4_RvalueScrutinee_Reject_UseAfterMove() => Round12RuleWitness.RejectAsync(
        "Option<Node> o = Some(new Node());\nint n = Move(o) switch { Some<Node>(var x) => x.V, _ => 0 };\nWriteLine(o.IsSome);\n" + Node + "\n", "VIB001");

    [Fact]
    public Task R3_4_RvalueScrutinee_Reject_LinearDiscarded() => Round12RuleWitness.RejectAsync("""
        Option<R> o = Some(new R(1));
        int n = Move(o) switch { Some<R>(var x) when x.Id == 2 => Keep(x), _ => 0 };
        static int Keep(R r) { using var k = r; return k.Id; }
        sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
        """, "VIB006");

    [Fact]
    public Task R3_4_RvalueScrutinee_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> o = Some(new Node());
        var kept = Move(o) switch { Some<Node>(var x) => Keep(x), _ => new Holder(new Node()) };
        WriteLine(kept.N.V);
        static Holder Keep(Node n) => new Holder(n);
        sealed class Node { public int V = 1; }
        sealed class Holder(Node n) { public Node N = n; }
        """, "1\n");

    [Fact]
    public Task R3_4_PlaceScrutinee_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Vec<int>> ov = Some(new Vec<int>());\nif (ov is Some<Vec<int>>(var v)) v.Push(1);\n", "VIB004");

    [Fact]
    public Task R3_4_PlaceBinding_Reject_Move() => Round12RuleWitness.RejectAsync(
        "Option<Vec<int>> ov = Some(new Vec<int>());\nif (ov is Some<Vec<int>>(var v)) Sink(v);\nstatic void Sink(Vec<int> v) { }\n", "VIB005");

    [Fact]
    public Task R3_4_PlaceBinding_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<int> o = Some(1);
        if (o is Some<int>(var x)) { x = x + 1; WriteLine(x); }
        WriteLine(o.Unwrap());
        Option<Vec<int>> ov = Some(new Vec<int>());
        if (ov is Some<Vec<int>>(var v)) WriteLine(v.Count);
        ref var m = ref ov.UnwrapMut();
        m.Push(1);
        WriteLine(ov.UnwrapMut().Count);
        """, "2\n1\n0\n1\n");

    [Fact]
    public Task R3_4_OptionMutBinding_Reject_RefArgument() => Round12RuleWitness.RejectAsync(
        "Option<Node> o = Some(new Node());\nif (o.AsMut() is Some<Node>(var n)) Bump(ref n);\nstatic void Bump(ref Node n) { }\n" + Node + "\n", "VIB004");

    [Fact]
    public Task R3_4_OptionMutBinding_Reject_Move() => Round12RuleWitness.RejectAsync(
        "Option<Node> o = Some(new Node());\nif (o.AsMut() is Some<Node>(var n)) Sink(n);\nstatic void Sink(Node n) { }\n" + Node + "\n", "VIB005");

    [Fact]
    public Task R3_4_OptionMutBinding_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> o = Some(new Node());
        if (o.AsMut() is Some<Node>(var n)) { n.V = 5; n.Add(1); }
        WriteLine(o.Unwrap().V);
        sealed class Node { public int V = 1; [Mut] public void Add(int d) { V += d; } }
        """, "6\n");
}

// §3.5–§3.7 and §4: Core intrinsics, the standard library, flow analysis and validation outcomes.
public sealed class Round12RuleWitnessIntrinsicTests
{
    // Cut C18's message: Stage-0 reports it before the Roslyn error on C#-invalid switch sections (SPEC §4).
    private const string C18 = "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }.";

    private const string Node = "sealed class Node { public int V = 1; }";
    private const string Resource = "sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }";

    // §3.5 Vibranium.Core intrinsics.
    [Fact]
    public Task R3_5_FailClosedMembers_Accept() => Round12RuleWitness.AcceptAsync(
        "Span<int> s = [1, 2, 3];\nVec<int> v = [4];\nWriteLine(s.Length + s.Slice(1).Length + v.Count + (s.IsEmpty ? 1 : 0));\n", "6\n");

    [Fact]
    public Task R3_5_Prelude_Reject() => Round12RuleWitness.RejectAsync(
        "var n = new Node();\nvar o = Some(n);\nWriteLine(n.V);\n" + Node + "\n", "VIB001");

    [Fact]
    public Task R3_5_Prelude_Accept() => Round12RuleWitness.AcceptAsync(
        "Result<Unit, int> r = Ok();\nWriteLine(r.IsOk);\nint k = 1;\nOption<int> o = Some(k);\nWriteLine(k + o.Unwrap());\n", "True\n2\n");

    [Fact]
    public Task R3_5_WrapperValue_Reject() => Round12RuleWitness.RejectAsync(
        "var h = new H(new Some<Node>(new Node()));\nWriteLine(h.S.Value.V);\n" + Node + "\nsealed class H(Some<Node> s) { public Some<Node> S = s; }\n", "VIB005");

    [Fact]
    public Task R3_5_WrapperValue_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(new Some<int>(3).Value);\nWriteLine(new Err<int>(4).Error);\n", "3\n4\n");

    [Fact]
    public Task R3_5_OptionConversions_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<int> a = new Some<int>(1);
        Option<int> b = (Option<int>)None;
        Result<int, int> c = new Ok<int>(2);
        Result<int, int> d = (Result<int, int>)new Err<int>(3);
        Option<int> e = Some(4);
        Result<int, int> f = Err(5);
        WriteLine(a.IsSome);
        WriteLine(b.IsEmpty);
        WriteLine(c.IsOk);
        WriteLine(d.IsOk);
        WriteLine(e.Unwrap() + f.UnwrapOr(6));
        """, "True\nTrue\nTrue\nFalse\n10\n");

    [Fact]
    public Task R3_5_OptionQueries_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> o = Some(new Node());\nOption<Node> n = None;\nWriteLine(F(in o));\nWriteLine(F(in n));\nstatic bool F(in Option<Node> o) => o.IsSome && !o.IsEmpty;\n" + Node + "\n", "True\nFalse\n");

    [Fact]
    public Task R3_5_GuardOnly_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = Some(1);\nif (!o.IsNone(out var v)) WriteLine(v);\nResult<int, int> r = Err(2);\nif (r.IsErr(out _, out var e)) WriteLine(e.Error);\n", "1\n2\n");

    [Fact]
    public Task R3_5_Unwrap_Reject() => Round12RuleWitness.RejectAsync(
        "var h = new H(Some(new Node()));\nvar n = h.Next.Unwrap();\nWriteLine(n.V);\n" + Node + "\nsealed class H(Option<Node> n) { public Option<Node> Next = n; }\n", "VIB005");

    [Fact]
    public Task R3_5_Unwrap_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new H(Some(new Node()));\nvar n = h.Next.Take().Unwrap();\nWriteLine(n.V);\nWriteLine(h.Next.IsSome);\n" + Node + "\nsealed class H(Option<Node> n) { public Option<Node> Next = n; }\n", "1\nFalse\n");

    [Fact]
    public Task R3_5_UnwrapOr_Reject() => Round12RuleWitness.RejectAsync(
        "Option<R> o = Some(new R(1));\nusing var x = o.UnwrapOr(new R(2));\nWriteLine(x.Id);\n" + Resource + "\n", "VIB006");

    [Fact]
    public Task R3_5_UnwrapOr_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Vec<int>> a = Some(new Vec<int>(2));
        Vec<int> f = [1, 2, 3];
        var x = a.UnwrapOr(f);
        WriteLine(x.Count);
        Result<Vec<int>, int> b = Err(4);
        Vec<int> g = [1];
        WriteLine(b.UnwrapOr(g).Count);
        """, "0\n1\n");

    [Fact]
    public Task R3_5_UnwrapMut_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Vec<int>> o = Some(new Vec<int>());\nref var v = ref o.UnwrapMut();\no = None;\nv.Push(1);\n", "VIB003");

    [Fact]
    public Task R3_5_UnwrapMut_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Vec<int>> o = Some(new Vec<int>());\nref var v = ref o.UnwrapMut();\nv.Push(1);\nWriteLine(o.Unwrap().Count);\n", "1\n");

    [Fact]
    public Task R3_5_SlotExtensions_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Node> slot = Some(new Node());\nvar m = slot.AsMut();\nslot = None;\nm.Set(None);\n" + Node + "\n", "VIB003");

    [Fact]
    public Task R3_5_SlotExtensions_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> slot = Some(new Node());\nvar t = slot.Take();\nWriteLine(slot.IsSome);\nvar prior = slot.Replace(t);\nWriteLine(prior.IsSome);\nWriteLine(slot.Unwrap().V);\n" + Node + "\n", "False\nFalse\n1\n");

    [Fact]
    public Task R3_5_OptionMutOps_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<Node> slot = Some(new Node());\nvar m = slot.AsMut();\nWriteLine(m.IsSome);\nm.Set(Some(new Node()));\nvar old = m.Take();\nWriteLine(old.IsSome);\nWriteLine(slot.IsEmpty);\n" + Node + "\n", "True\nTrue\nTrue\n");

    [Fact]
    public Task R3_5_VecConstruct_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> a = [];\nvar b = new Vec<int>();\nvar c = new Vec<int>(8);\nVec<int> d = [1, 2, 3];\nWriteLine(a.Count + b.Count + c.Count + d.Count);\nWriteLine(c.Capacity >= 8);\n", "3\nTrue\n");

    [Fact]
    public Task R3_5_VecShared_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1];\nvar r = v.AsReadOnlySpan();\nv.Push(1);\nWriteLine(r[0]);\n", "VIB003");

    [Fact]
    public Task R3_5_VecShared_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2, 3];\nWriteLine(F(in v));\nstatic int F(in Vec<int> v) => v.Count + (v.Capacity >= 3 ? 1 : 0) + v.AsReadOnlySpan()[0] + v.ReadOnlySlice(1, 2)[1];\n", "8\n");

    [Fact]
    public Task R3_5_VecExclusive_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1];\nF(in v);\nstatic void F(in Vec<int> v) { v.Clear(); }\n", "VIB004");

    [Fact]
    public Task R3_5_VecExclusive_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<int> v = [1, 2, 3];
        v.Push(4);
        v += 5;
        WriteLine(v.Pop().Unwrap());
        v.Truncate(2);
        WriteLine(v.Count);
        v.Reserve(10);
        v.Resize(4, 9);
        WriteLine(v[3]);
        v.Clear();
        WriteLine(v.Count);
        """, "5\n2\n9\n0\n");

    [Fact]
    public Task R3_5_VecExtendSelf_Reject() => Round12RuleWitness.RejectAsync("Vec<int> v = [1];\nv.Extend(v.AsReadOnlySpan());\n", "VIB003");

    [Fact]
    public Task R3_5_VecExtendSelf_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1];\nVec<int> w = [2, 3];\nv.Extend(w.AsReadOnlySpan());\nWriteLine(v.Count);\n", "3\n");

    [Fact]
    public Task R3_5_VecSlice_Reject() => Round12RuleWitness.RejectAsync(
        "Vec<int> v = [1, 2];\nSpan<int> s = v.AsSpan();\nWriteLine(v.Count);\ns[0] = 1;\n", "VIB003");

    [Fact]
    public Task R3_5_VecSlice_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<int> v = [1, 2, 3];\nSpan<int> s = v[1..3];\ns[0] = 9;\nWriteLine(v[1]);\n", "9\n");

    [Fact]
    public Task R3_5_SpanMembers_Reject() => Round12RuleWitness.RejectAsync(
        "Span<int> s = [1, 2];\nReadOnlySpan<int> r = s;\ns[0] = 1;\nWriteLine(r[0]);\n", "VIB003");

    [Fact]
    public Task R3_5_SpanMembers_Accept() => Round12RuleWitness.AcceptAsync(
        "Span<int> s = [1, 2];\nReadOnlySpan<int> r = s;\nWriteLine(r[0]);\ns[0] = 1;\nWriteLine(s[^1] + s.Slice(1, 1)[0] + s[0..1].Length);\n", "1\n5\n");

    [Fact]
    public Task R3_5_Expect_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<R> o = Some(new R(1));\nusing var x = o.Expect(\"no file\");\nWriteLine(x.Id + 1);\n" + Resource + "\n", "2\n1\n");

    [Fact]
    public Task R3_5_UnwrapOrDefault_Accept() => Round12RuleWitness.AcceptAsync(
        "Option<int> o = None;\nWriteLine(o.UnwrapOrDefault());\nResult<int, int> r = Err(1);\nWriteLine(r.UnwrapOrDefault());\n", "0\n0\n");

    [Fact]
    public Task R3_5_Stdio_Accept() => Round12RuleWitness.AcceptAsync(
        "Stdio.WriteOut(\"out\\n\"u8);\nStdio.WriteErr(\"err\\n\"u8);\nStdio.WriteOut(\"s\\n\");\nStdio.WriteErr(\"t\\n\");\nWrite(\"w\"u8);\nWriteLine(\"l\"u8);\nWrite(\"x\");\nWriteLine(\"y\");\n",
        "out\ns\nwl\nxy\n", expectedError: "err\nt\n");

    [Fact]
    public Task R3_5_WriteFormatting_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(sbyte.MinValue);
        WriteLine(byte.MaxValue);
        WriteLine(short.MinValue);
        WriteLine(ushort.MaxValue);
        WriteLine(int.MinValue);
        WriteLine(uint.MaxValue);
        WriteLine(long.MinValue);
        WriteLine(ulong.MaxValue);
        WriteLine(true);
        WriteLine(false);
        Write(5);
        WriteLine();
        """, "-128\n255\n-32768\n65535\n-2147483648\n4294967295\n-9223372036854775808\n18446744073709551615\nTrue\nFalse\n5\n");

    [Fact]
    public Task R3_5_BclAliases_Accept() => Round12RuleWitness.AcceptAsync("""
        ReadOnlySpan<byte> s = "hello"u8;
        WriteLine(s.StartsWith("he"u8));
        WriteLine(s.IndexOf("l"u8));
        WriteLine(s.SequenceEqual("hello"u8));
        WriteLine(s.EndsWith("lo"));
        if (int.TryParse("42", out int n)) WriteLine(n);
        if (long.TryParse("-7"u8, out long m)) WriteLine(m);
        """, "True\n2\nTrue\nTrue\n42\n-7\n");

    [Fact]
    public Task R3_5_Panic_Accept() => Round12RuleWitness.AcceptAsync(
        "Write(\"x\");\nVibraniumRuntime.Panic(\"boom\");\n", "x", expectedExit: 101, expectedError: "panic: boom\n");

    [Fact]
    public Task R3_5_ConsumingIntrinsics_Reject() => Round12RuleWitness.RejectAsync(
        "Option<Node> o = Some(new Node());\nvar a = o.Unwrap();\nvar b = o.Unwrap();\n" + Node + "\n", "VIB001");

    [Fact]
    public Task R3_5_ConsumingIntrinsics_Accept() => Round12RuleWitness.AcceptAsync(
        "var h = new H(Some(3));\nOption<int> p = Some(4);\nWriteLine(F(in p) + h.O.Unwrap() + h.O.Unwrap());\nstatic int F(in Option<int> o) => o.Unwrap();\nsealed class H(Option<int> o) { public Option<int> O = o; }\n", "10\n");

    [Fact]
    public Task R3_5_GuardBindings_Accept() => Round12RuleWitness.AcceptAsync(
        "Result<int, int> r = Ok(1);\nif (r.IsErr(out var ok, out var err)) WriteLine(err.Error); else WriteLine(ok);\nResult<int, int> s = Err(2);\nif (s.IsErr(out var ok2, out var err2)) WriteLine(err2.Error); else WriteLine(ok2);\n", "1\n2\n");

    [Fact]
    public Task R3_5_GuardDivergence_Reject() => Round12RuleWitness.RejectAsync(
        "Option<int> o = Some(1);\nif (o.IsNone(out var v)) WriteLine(0);\nWriteLine(v);\n", "VIB001");

    [Fact]
    public Task R3_5_GuardDivergence_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(F(Some(1)));\nWriteLine(F(None));\nstatic int F(Option<int> o) { if (o.IsNone(out var v)) return 0; return v; }\n", "1\n0\n");

    // §3.6 Vibranium.Std runtime behaviour, managed and native equal.
    [Fact]
    public Task R3_6_TextBytes_Accept() => Round12RuleWitness.AcceptAsync("""
        Vec<byte> bad = [0xC3, 0x28];
        WriteLine(Utf8.IsValid("h\u00e9llo"u8));
        WriteLine(Utf8.IsValid(bad.AsReadOnlySpan()));
        WriteLine(Utf8.IsBoundary("h\u00e9"u8, 2));
        WriteLine(Bytes.IndexOf("hello world", "world"));
        WriteLine(Bytes.LastIndexOfByte("a/b/c", (byte)'/'));
        WriteLine(Bytes.EqIgnoreAsciiCase("HeLLo", "hello"));
        Stdio.WriteOut(Bytes.TrimAscii("  pad  "));
        Stdio.WriteOut("|\n"u8);
        Stdio.WriteOut(" \tx y\t ".AsUtf8().Trim());
        Stdio.WriteOut("|\n"u8);
        Parse("2147483647");
        Parse("2147483648");
        Parse("-2147483648");
        Parse("");
        Parse("-");
        Parse("12x");
        var sb = new StringBuf(4);
        sb.Append("n=");
        sb.Append(-42);
        sb.AppendAscii((byte)' ');
        sb.Append(true);
        sb.AppendRune(0x20AC);
        sb.AppendLine();
        Write(in sb);
        WriteLine(sb.Length);
        static void Parse(string text)
        {
            if (Bytes.TryParseInt32(text, out int value)) WriteLine(value);
            else WriteLine("none");
        }
        """, "True\nFalse\nFalse\n6\n3\nTrue\npad|\nx y|\n2147483647\nnone\n-2147483648\nnone\nnone\nnone\nn=-42 True\u20ac\n14\n");

    [Fact]
    public Task R3_6_ConsoleIo_Accept()
    {
        var input = new List<byte>();
        input.AddRange(Encoding.UTF8.GetBytes("alpha\nbeta\r\ncr\rinside\n\nabcdefg\n"));
        input.Add(0xFF);
        input.Add((byte)'\n');
        input.AddRange(Encoding.UTF8.GetBytes("tail"));
        return Round12RuleWitness.AcceptBytesAsync("""
            Dump(ReadLine());
            Dump(ReadLine());
            Dump(ReadLine());
            Dump(ReadLine());
            Dump(ReadLine(4));
            Dump(ReadLine());
            Dump(ReadLine());
            Dump(ReadLine());
            Dump(ReadLine());
            static void Dump(Result<Option<StringBuf>, IoError> r)
            {
                if (r.IsErr(out var opt, out var err))
                {
                    Write("ERR:");
                    WriteLine(err.Error.KindName());
                    return;
                }
                if (opt.IsNone(out var line))
                {
                    WriteLine("EOF");
                    return;
                }
                Write("OK:");
                Write(line.Length);
                Write(":");
                Write(in line);
                WriteLine("");
            }
            """, "OK:5:alpha\nOK:4:beta\nOK:9:cr\rinside\nOK:0:\nERR:FileTooLarge\nOK:2:fg\nERR:InvalidInput\nOK:4:tail\nEOF\n", 0, "", input.ToArray());
    }

    [Fact]
    public Task R3_6_Collections_Accept() => Round12RuleWitness.AcceptAsync("""
        var imap = new IntMap<long>(4);
        for (int i = 0; i < 150; i++) imap.Set(i * 17, i * 100L);
        WriteLine(imap.Count);
        WriteLine(imap.Get(170).Unwrap());
        WriteLine(imap.Remove(170).Unwrap());
        WriteLine(imap.Remove(170).IsSome);
        WriteLine(imap.Contains(170));
        imap.Set(170, 9999L);
        WriteLine(imap.Get(170).Unwrap());
        WriteLine(imap.Count);
        imap.Clear();
        WriteLine(imap.Count);
        var smap = new StrMap<int>(4);
        var key = new StringBuf(16);
        for (int i = 0; i < 120; i++)
        {
            key.Clear();
            key.Append("k_");
            key.AppendInt(i);
            smap.Set(key.AsUtf8(), i + 1);
        }
        WriteLine(smap.Count);
        WriteLine(smap.Get("k_0").Unwrap());
        WriteLine(smap.Get("k_119").Unwrap());
        WriteLine(smap.Remove("k_50").Unwrap());
        WriteLine(smap.Contains("k_50"));
        smap.Set("k_50", 777);
        WriteLine(smap.Get("k_50").Unwrap());
        var interner = new Interner(8);
        int a = interner.Intern("alpha");
        int b = interner.Intern("beta");
        int a2 = interner.Intern("alpha");
        WriteLine(a == a2);
        WriteLine(a != b);
        WriteLine(Bytes.Eq(interner.Text(b), "beta"));
        WriteLine(interner.Count);
        """, "150\n1000\n1000\nFalse\nFalse\n9999\n150\n0\n120\n1\n120\n51\nFalse\n777\nTrue\nTrue\nTrue\n2\n");

    [Fact]
    public Task R3_6_EnvProcessNet_Accept() => Round12RuleWitness.AcceptAsync("""
        if (Env.Args().IsErr(out var cli, out _)) return 1;
        WriteLine(cli.Count);
        foreach (var arg in cli)
        {
            Write(arg.Length);
            Write(":");
            Stdio.WriteOut(arg);
            WriteLine("");
        }
        var sh = new Command("/bin/sh");
        sh.Arg("-c");
        sh.Arg("exit 42");
        WriteLine(Process.Run(in sh).Unwrap());
        var missing = new Command("/nonexistent_vib_r12_binary");
        if (Process.Run(in missing).IsErr(out _, out var runErr)) WriteLine(runErr.Error.KindName());
        if (TcpListener.Bind(new Ipv4EndPoint(Ipv4Address.Loopback(), 0), 4).IsErr(out var rawListener, out _)) return 2;
        using var listener = rawListener;
        int port = listener.LocalPort().Unwrap();
        if (TcpStream.Connect(new Ipv4EndPoint(Ipv4Address.Loopback(), port)).IsErr(out var rawClient, out _)) return 3;
        using var client = rawClient;
        if (listener.Accept().IsErr(out var rawServer, out _)) return 4;
        using var server = rawServer;
        _ = client.WriteAll("ping").Unwrap();
        _ = client.ShutdownWrite().Unwrap();
        var buf = new Vec<byte>(16);
        buf.Resize(16, 0);
        int n = server.Read(buf.AsSpan()).Unwrap();
        Stdio.WriteOut(buf.ReadOnlySlice(0, n));
        WriteLine("");
        WriteLine(server.Read(buf.AsSpan()).Unwrap());
        return 0;
        """, "3\n0:\n5:hello\n4:\u03bb-x\n42\nNotFound\nping\n0\n", appArgs: ["", "hello", "\u03bb-x"]);

    [Fact]
    public Task R3_6_NativeCatalog_Accept() => Round12RuleWitness.AcceptAsync("WriteLine(File.Exists(\"/definitely/not/here\"));\n", "False\n");

    // §3.7 flow analysis.
    [Fact]
    public Task R3_7_PanicContinuation_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(F());\nstatic int F() { VibraniumRuntime.Panic(\"x\"); return 0; }\n", "", expectedExit: 101, expectedError: "panic: x\n");

    [Fact]
    public Task R3_7_UnionSwitchExit_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(F(Some(1)));
        WriteLine(F(None));
        static int F(Option<int> o)
        {
            switch (o)
            {
                case Some<int>(var v): return v;
                case NoneType: return 0;
            }
            return -1;
        }
        """, "1\n0\n");

    [Fact]
    public Task R3_7_EndpointReachability_Accept() => Round12RuleWitness.AcceptAsync(
        "int k = Pick();\nswitch (k)\n{\n    case 1:\n        WriteLine(1);\n        break;\n    case 2:\n        break;\n}\nstatic int Pick() => 1;\n", "1\n");

    // A use after move that only an unconditional Panic precedes is not on a runtime-reachable path.
    [Fact]
    public Task R3_7_RuntimeReachability_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(F(1));
        static int F(int k)
        {
            Vec<int> v = [1];
            var w = v;
            if (k == 1) return w.Count;
            VibraniumRuntime.Panic("stop");
            return v.Count;
        }
        """, "1\n");

    // Storing an inner-scope view in an outer local is a C# ref-safety error (CS8352); vib-self rejects it too.
    [Fact]
    public Task R3_7_LexicalRefSafety_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "ReadOnlySpan<int> outer = [];\n{\n    Span<int> inner = [1, 2];\n    outer = inner;\n}\nWriteLine(outer.Length);\n", "CS8352");

    [Fact]
    public Task R3_7_LexicalRefSafety_Accept() => Round12RuleWitness.AcceptAsync(
        "{\n    Span<int> inner = [1, 2];\n    ReadOnlySpan<int> view = inner;\n    WriteLine(view.Length);\n}\n", "2\n");

    // The reject declares locals directly in switch sections, which cut C18 also rejects. The SDK reports CS0128;
    // Stage-0 reports C18 (a more specific Vibranium rule, SPEC §4) and vib-self rejects.
    [Fact]
    public Task R3_7_SwitchDeclarationSpace_Reject() => Round12RuleWitness.RejectCSharpAsync("""
        int k = Pick();
        switch (k)
        {
            case 1:
                int a = 1;
                WriteLine(a);
                break;
            case 2:
                int a = 2;
                WriteLine(a);
                break;
        }
        static int Pick() => 1;
        """, "CS0128", stage0Specific: C18);

    [Fact]
    public Task R3_7_SwitchUsing_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "int k = Pick();\nswitch (k)\n{\n    case 1:\n        using var r = new R(1);\n        break;\n}\nstatic int Pick() => 1;\n" + Resource + "\n", "CS8647", stage0Specific: C18);

    [Fact]
    public Task R3_7_SwitchUsing_Accept() => Round12RuleWitness.AcceptAsync(
        "int k = Pick();\nswitch (k)\n{\n    case 1:\n    {\n        using var r = new R(1);\n        break;\n    }\n}\nstatic int Pick() => 1;\n" + Resource + "\n", "1\n");

    // §4 validation outcomes.
    [Fact]
    public Task R4_MovedDispose_Reject() => Round12RuleWitness.RejectAsync(
        "var r = new R(1);\nSink(r);\nWriteLine(r.Id);\nstatic void Sink(R x) { using var y = x; }\n" + Resource + "\n", "VIB001");

    [Fact]
    public Task R4_MovedDispose_Accept() => Round12RuleWitness.AcceptAsync(
        "var r = new R(1);\nWriteLine(r.Id);\nSink(r);\nstatic void Sink(R x) { using var y = x; }\n" + Resource + "\n", "1\n1\n");

    [Fact]
    public Task R4_CSharpInvalidSelf_Reject() => Round12RuleWitness.RejectCSharpAsync(
        "Vec<int> v = [1];\nint n = v;\nWriteLine(n);\n", "CS0029");

    // SPEC §4 "C# validity": a genuine C# error requires rejection even when accepting the program would not affect
    // safety, and vib-stage2 must reject it on its own. These errors are safety-neutral.
    [Fact]
    public Task R4_GenuineCSharpError_Reject_DuplicateLocal() => Round12RuleWitness.RejectCSharpAsync(
        "int a = 1;\nint a = 2;\nWriteLine(a);\n", "CS0128");

    [Fact]
    public Task R4_GenuineCSharpError_Reject_DuplicateMethod() => Round12RuleWitness.RejectCSharpAsync(
        "WriteLine(P.F());\nstatic class P { public static int F() => 1; public static int F() => 2; }\n", "CS0111");

    [Fact]
    public Task R4_GenuineCSharpError_Reject_ArgumentCount() => Round12RuleWitness.RejectCSharpAsync(
        "WriteLine(P.F(1, 2));\nstatic class P { public static int F(int x) => x; }\n", "CS1501");

    [Fact]
    public Task R4_GenuineCSharpError_Reject_DuplicateType() => Round12RuleWitness.RejectCSharpAsync(
        "WriteLine(new A().V);\nsealed class A { public int V = 1; }\nsealed class A { public int V = 2; }\n", "CS0101");

    [Fact]
    public Task R4_GenuineCSharpError_Accept() => Round12RuleWitness.AcceptAsync(
        "int a = 1;\nint b = 2;\nWriteLine(a + b);\nWriteLine(P.F(3));\nstatic class P { public static int F(int x) => x; }\n", "3\n3\n");

    [Fact]
    public Task R4_CSharpInvalidSelf_Accept() => Round12RuleWitness.AcceptAsync("Vec<int> v = [1];\nint n = v.Count;\nWriteLine(n);\n", "1\n");

    // SPEC §4 "C# validity": only C# errors make a program C#-invalid. A C# warning outside the 13 diagnostics that
    // Vibranium.App.props promotes is not a language error, so the Analyzer, Stage-0 and vib-self accept a program whose
    // only C# diagnostic is such a warning, and its native runs equal the managed run.
    [Theory]
    [InlineData("int unused = 1;\nWriteLine(2);\n", "CS0219", "2\n")]
    [InlineData("int declared;\nWriteLine(3);\n", "CS0168", "3\n")]
    public Task R4_PromotedDiagnostics_Accept_WarningOnly(string source, string warning, string expected) =>
        Round12RuleWitness.AcceptAsync(source, expected, csharpWarning: warning);

    // SPEC §4 "C# validity", build policy: `dotnet build` under the default Vibranium.App.props profile refuses the
    // same warning-only programs, because that profile also sets TreatWarningsAsErrors; the corrected program builds.
    // The project imports this repository's Vibranium.App.props and references the built Vibranium.Core and the
    // Vibranium.Analyzer assemblies.
    [Theory]
    [InlineData("int unused = 1;\nWriteLine(2);\n", "CS0219")]
    [InlineData("int declared;\nWriteLine(3);\n", "CS0168")]
    [InlineData("int used = 1;\nWriteLine(used + 1);\n", null)]
    public void R4_DefaultProfileBuild_RefusesWarningOnly(string source, string? error)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-witness-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string props = System.Security.SecurityElement.Escape(Path.Combine(Round11AuditAssertions.RepoRoot, "Vibranium.App.props"))!;
            string core = System.Security.SecurityElement.Escape(typeof(Vibranium.Option<>).Assembly.Location)!;
            string analyzers = string.Concat(new[]
            {
                typeof(VibraniumDiagnosticAnalyzer).Assembly.Location,
                typeof(VibraniumFrontend).Assembly.Location,
                typeof(Vibranium.Analysis.BorrowAndMoveAnalyzer).Assembly.Location,
                typeof(Vibranium.Compiler.Core.MirModule).Assembly.Location,
            }.Select(path => $"<Analyzer Include=\"{System.Security.SecurityElement.Escape(path)}\" />"));
            File.WriteAllText(Path.Combine(temp, "Profile.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
                <Import Project="{props}" />
                <ItemGroup><Reference Include="Vibranium.Core"><HintPath>{core}</HintPath></Reference>{analyzers}</ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(temp, "Program.cs"), source);
            var build = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe,
                "build", "--disable-build-servers", Path.Combine(temp, "Profile.csproj"), "--nologo");
            string output = build.Out + build.Err;
            Assert.DoesNotContain("error VIB", output, StringComparison.Ordinal);
            if (error is null)
            {
                Assert.True(build.Exit == 0, "the corrected program must build under the default profile:\n" + output);
                Assert.DoesNotContain("error CS", output, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(build.Exit != 0, $"dotnet build under the default profile accepted a program with warning {error}:\n" + output);
                Assert.Contains($"error {error}:", output, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

// §5: native lowering, drops, arithmetic, emitted attributes and the panic contract (managed and native equal).
public sealed class Round12RuleWitnessRuntimeTests
{
    // Identity functions hide operands from constant folding; a program declares only the ones it calls (no CS8321).
    private static string Ids(string body) => string.Concat(
        new[] { ("Id(", "static int Id(int x) => x;\n"), ("Id64(", "static long Id64(long x) => x;\n"), ("IdU(", "static uint IdU(uint x) => x;\n"), ("IdB(", "static byte IdB(byte x) => x;\n") }
            .Where(f => body.Contains(f.Item1, StringComparison.Ordinal)).Select(f => f.Item2));

    private static Task PanicsAsync(string body, string message, string stdout = "a") =>
        Round12RuleWitness.AcceptAsync("Write(\"a\");\n" + body + "\nWriteLine(\"unreachable\");\n" + Ids(body), stdout, expectedExit: 101, expectedError: "panic: " + message + "\n");

    // §5.1 memory model.
    [Fact]
    public Task R5_1_HeapAlloc_Accept() => Round12RuleWitness.AcceptAsync("""
        var e = new Empty();
        var h = new Holder(new Vec<int>(4), new Leaf(3));
        h.Items.Push(1);
        WriteLine(h.Items.Count + h.L.W);
        e = new Empty();
        WriteLine(Touch(e));
        static int Touch(Empty e) => 7;
        sealed class Empty { }
        sealed class Leaf(int w) { public int W = w; }
        sealed class Holder(Vec<int> items, Leaf l) { public Vec<int> Items = items; public Leaf L = l; }
        """, "4\n7\n");

    [Fact]
    public Task R5_1_IterativeDrop_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> head = None;
        for (int i = 0; i < 1000000; i++) head = Some(new Node(i, Move(head)));
        int n = 0;
        for (var c = head.AsRef(); c is Some<Node>(var x); c = x.Next.AsRef()) n++;
        WriteLine(n);
        Option<Tree> t = None;
        for (int i = 0; i < 200000; i++) t = Some(new Tree(Move(t), Some(new Tree(None, None))));
        WriteLine(t.IsSome);
        sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
        sealed class Tree(Option<Tree> left, Option<Tree> right) { public Option<Tree> Left = left; public Option<Tree> Right = right; }
        """, "1000000\nTrue\n");

    private const string SpanParameters = """
        Vec<int> v = [1, 2, 3];
        int k = 5;
        WriteLine(Sum(v.AsReadOnlySpan(), in k));
        Fill(v.AsSpan(), 2);
        WriteLine(v[0] + v[2]);
        WriteLine(Sum(v.AsReadOnlySpan()[0..0], in k));
        static int Sum(ReadOnlySpan<int> s, in int bias) { int t = bias; foreach (var x in s) t += x; return t; }
        static void Fill(Span<int> s, int x) { for (int i = 0; i < s.Length; i++) s[i] = x; }
        """;

    private static string Parameters(string ir, string function)
    {
        var match = Regex.Match(ir, @"^define [^@]*@vib_fn_[^(]*_" + function + @"_[^(]*\((?<p>[^)]*)\)", RegexOptions.Multiline);
        Assert.True(match.Success, $"no definition of {function} in the IR");
        return match.Groups["p"].Value;
    }

    [Fact]
    public async Task R5_1_SpanLoopAttributes_Accept()
    {
        var accepted = await Round12RuleWitness.AcceptAsync(SpanParameters, "11\n4\n5\n");
        foreach (string ir in new[] { accepted.Stage0Ir, accepted.SelfIr })
        {
            Assert.DoesNotContain("mustprogress", ir, StringComparison.Ordinal);
            Assert.DoesNotContain("dereferenceable", ir, StringComparison.Ordinal);
            foreach (string function in new[] { "Sum", "Fill" })
            {
                string spanPointer = Parameters(ir, function).Split(',')[0];
                Assert.Contains("%arg_0_ptr", spanPointer, StringComparison.Ordinal);
                Assert.DoesNotContain("nonnull", spanPointer, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task R5_3_NoAlias_Accept()
    {
        var accepted = await Round12RuleWitness.AcceptAsync(SpanParameters, "11\n4\n5\n");
        foreach (string ir in new[] { accepted.Stage0Ir, accepted.SelfIr })
        {
            string shared = Parameters(ir, "Sum");
            Assert.DoesNotContain("noalias", shared, StringComparison.Ordinal);
            Assert.Contains("readonly", shared, StringComparison.Ordinal);
            Assert.Contains("noalias", Parameters(ir, "Fill").Split(',')[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public Task R5_3_LibcOnly_Accept() => Round12RuleWitness.AcceptAsync(
        "Vec<double> v = [5.5, -2.0];\nWriteLine(v[0] % v[1] == 1.5);\nWriteLine(File.Exists(\"/definitely/missing\"));\n", "True\nFalse\n",
        inspectBinary: (binary, sanitized) =>
        {
            if (sanitized) return;
            var dynamic = Round12RuleWitness.Run("readelf", null, "-d", binary);
            Assert.Equal(0, dynamic.Exit);
            string[] needed = Regex.Matches(dynamic.Out, @"\(NEEDED\)\s+Shared library: \[(?<lib>[^\]]+)\]").Select(m => m.Groups["lib"].Value).ToArray();
            Assert.Contains("libc.so.6", needed);
            Assert.All(needed, lib => Assert.Contains(lib, new[] { "libc.so.6", "ld-linux-x86-64.so.2" }));
        });

    // §5.2 drops and slot writes.
    [Fact]
    public Task R5_2_SlotWrites_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> slot = Some(new Node(1));
        var m = slot.AsMut();
        m.Set(Some(new Node(2)));
        var taken = m.Take();
        m.Set(Some(new Node(3)));
        var prior = m.Replace(Some(new Node(4)));
        WriteLine(taken.Unwrap().V + prior.Unwrap().V);
        ref var r = ref slot.UnwrapMut();
        r = new Node(5);
        WriteLine(slot.Unwrap().V);
        sealed class Node(int v) { public int V = v; }
        """, "5\n5\n");

    // §5.3 integer semantics, with runtime operands so that nothing is constant-folded.
    [Fact]
    public Task R5_3_Wrapping_Accept() => Round12RuleWitness.AcceptAsync("""
        int max = Id(int.MaxValue);
        int min = Id(int.MinValue);
        WriteLine(max + 1);
        WriteLine(-min);
        WriteLine(min - 1);
        WriteLine(max * 2);
        long big = Id64(long.MaxValue);
        WriteLine(big * 3);
        WriteLine(-Id64(long.MinValue));
        byte b = IdB(200);
        int p = b + b;
        WriteLine(p);
        WriteLine((byte)(b + b));
        WriteLine(IdU(0) - 1);
        """ + "\n" + Ids("Id( Id64( IdU( IdB("), "-2147483648\n-2147483648\n2147483647\n-2\n9223372036854775805\n-9223372036854775808\n400\n144\n4294967295\n");

    [Fact]
    public Task R5_3_ShiftMask_Accept() => Round12RuleWitness.AcceptAsync("""
        WriteLine(1 << Id(33));
        WriteLine(Id64(1) << Id(65));
        WriteLine(-1 >> Id(40));
        WriteLine(IdU(0x80000000u) >> Id(63));
        WriteLine(Id64(-256) >> Id(68));
        """ + "\n" + Ids("Id( Id64( IdU("), "2\n2\n-1\n1\n-16\n");

    // §5.4 the panic contract and its fixed messages.
    [Fact]
    public Task R5_4_PanicContract_Accept() => Round12RuleWitness.AcceptAsync("""
        using var r = new R(1);
        Write("pending");
        VibraniumRuntime.Panic("stop");
        sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
        """, "pending", expectedExit: 101, expectedError: "panic: stop\n");

    [Theory]
    [InlineData("Vec<int> v = [1];\nWriteLine(v[Id(1)]);")]
    [InlineData("Span<int> s = [1];\nWriteLine(s[Id(-1)]);")]
    [InlineData("Vec<int> v = [1];\nReadOnlySpan<int> s = v.AsReadOnlySpan();\nWriteLine(s[Id(2)]);")]
    public Task R5_4_IndexOutOfBounds_Accept(string body) => PanicsAsync(body, "index out of bounds");

    [Theory]
    [InlineData("Vec<int> v = [1, 2];\nWriteLine(v[0..Id(3)].Length);")]
    [InlineData("Span<int> s = [1, 2];\nWriteLine(s.Slice(1, Id(5)).Length);")]
    [InlineData("Vec<int> v = [1, 2];\nWriteLine(v.ReadOnlySlice(Id(2), 1).Length);")]
    public Task R5_4_SliceOutOfBounds_Accept(string body) => PanicsAsync(body, "slice out of bounds");

    [Theory]
    [InlineData("Vec<int> v = [1];\nv.Truncate(Id(2));")]
    [InlineData("Vec<int> v = [1];\nv.Truncate(Id(-1));")]
    public Task R5_4_Truncate_Accept(string body) => PanicsAsync(body, "truncate out of bounds");

    [Fact]
    public Task R5_4_ResizeNegative_Accept() => PanicsAsync("Vec<int> v = [1];\nv.Resize(Id(-1), 0);", "Vec length cannot be negative");

    [Theory]
    [InlineData("WriteLine(7 / Id(0));")]
    [InlineData("WriteLine(7 % Id(0));")]
    [InlineData("WriteLine(7L / Id64(0));")]
    [InlineData("WriteLine(7u % IdU(0));")]
    public Task R5_4_DivideByZero_Accept(string body) => PanicsAsync(body, "attempt to divide by zero");

    [Theory]
    [InlineData("WriteLine(Id(int.MinValue) / Id(-1));")]
    [InlineData("WriteLine(Id(int.MinValue) % Id(-1));")]
    [InlineData("WriteLine(Id64(long.MinValue) / Id64(-1));")]
    public Task R5_4_ArithmeticOverflow_Accept(string body) => PanicsAsync(body, "arithmetic overflow");

    [Fact]
    public Task R5_4_OptionUnwrap_Accept() => PanicsAsync("Option<int> o = None;\nWriteLine(o.Unwrap());", "called Option.Unwrap() on None");

    [Fact]
    public Task R5_4_OptionExpect_Accept() => PanicsAsync("Option<int> o = None;\nWriteLine(o.Expect(\"no value here\"));", "no value here");

    [Fact]
    public Task R5_4_OptionUnwrapMut_Accept() => PanicsAsync("Option<Vec<int>> o = None;\nref var v = ref o.UnwrapMut();\nv.Push(1);", "called Option.UnwrapMut() on None");

    [Fact]
    public Task R5_4_ResultUnwrap_Accept() => PanicsAsync("Result<int, int> r = Err(1);\nWriteLine(r.Unwrap());", "called Result.Unwrap() on Err");

    [Fact]
    public Task R5_4_ResultExpect_Accept() => PanicsAsync("Result<int, int> r = Err(1);\nWriteLine(r.Expect(\"bad result\"));", "bad result");

    [Fact]
    public Task R5_4_CapacityNegative_Accept() => PanicsAsync("var v = new Vec<int>(Id(-1));\nWriteLine(v.Count);", "Vec capacity cannot be negative");

    [Theory]
    [InlineData("var v = new Vec<int>(Id(int.MaxValue));\nWriteLine(v.Count);")]
    [InlineData("Vec<int> v = [1];\nv.Reserve(Id(0x7FFFFFC8));\nWriteLine(v.Count);")]
    public Task R5_4_CapacityOverflow_Accept(string body) => PanicsAsync(body, "Vec capacity overflow");
}
