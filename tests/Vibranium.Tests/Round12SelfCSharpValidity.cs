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
using Xunit;

namespace Vibranium.Tests;

internal static class Round12SelfCSharpValidity
{
    internal static async Task RejectAsync(string source, string csharpId, string selfId = "VIB000")
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-csharp-validity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12CSharpValidity", includeStd: true);
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()));
            var errors = (await analyzer.GetAllDiagnosticsAsync()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.True(errors.Any(d => d.Id == csharpId), $"Expected {csharpId} from the analyzer host:\n{string.Join("\n", errors.Select(d => d.ToString()))}");
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var (exit, output, error) = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null,
                Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.True(exit == 1, $"Expected vib-self to reject with exit 1, got {exit}:\n{output}{error}");
            Assert.Equal(new[] { selfId }, Regex.Matches(output + error, @"error (VIB\d{3}):")
                .Select(m => m.Groups[1].Value).Distinct());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
