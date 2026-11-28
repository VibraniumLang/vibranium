using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

// c6 delta: witnesses for cut C21 (decided by main from rule-ledger finding F11). Add this file together with the
// C21 SPEC bullet and the R1-cut-c21-base-constructor ledger rule; it fails on c5, where the Analyzer and Stage-0
// still accept `: base()`.
public sealed class Round12RuleWitnessC21Tests
{
    private const string Message = "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance.";

    // Every frontend reports exactly one C21 VIB000 per file, anchored at the first `base` token.
    private static async Task RejectC21Async(string source)
    {
        await Round12RuleWitness.RejectAsync(source, "VIB000", Message, banned: true);
        int position = source.IndexOf("base", StringComparison.Ordinal);
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-c21-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12WitnessC21", includeStd: true);
            var stage0 = Assert.Single(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics, d => d.Message == Message);
            Assert.Equal(position, stage0.Span.Start);
            Assert.Equal("base".Length, stage0.Span.Length);
            var analyzer = (await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.GetMessage() == Message).ToArray();
            var live = Assert.Single(analyzer);
            Assert.Equal(position, live.Location.SourceSpan.Start);
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Single(Regex.Matches(self.Out + self.Err, Regex.Escape(Message)));
            int line = 1 + source[..position].Count(c => c == '\n');
            int column = position - source.LastIndexOf('\n', position);
            Assert.Contains($"Program.cs({line},{column}): error VIB000: {Message}", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public Task R1_CutC21BaseConstructor_Reject_SealedClass() => RejectC21Async("""
        var c = new C(3);
        WriteLine(c.X);
        sealed class C { public int X; public C(int x) : base() { X = x; } }
        """);

    [Fact]
    public Task R1_CutC21BaseConstructor_Reject_OncePerFile() => RejectC21Async("""
        var c = new C(3);
        WriteLine(c.X);
        class C { public int X; public C(int x) : base() { X = x; } public C() : base() { X = 1; } }
        """);

    [Fact]
    public Task R1_CutC21BaseConstructor_Accept() => Round12RuleWitness.AcceptAsync("""
        var c = new C(3);
        WriteLine(c.X);
        sealed class C { public int X; public C(int x) { X = x; } }
        """, "3\n");
}
