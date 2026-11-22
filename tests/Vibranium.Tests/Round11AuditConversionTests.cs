using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed partial class VibraniumEndToEndTests
{
    [Fact]
    public async Task Round11_AuditConversion_NativeOutOfRangeConstants_UseRuntimeSaturation()
    {
        const string source = """
            nint x = unchecked((nint)1e30);
            WriteLine(x);
            WriteLine(unchecked((nint)(-1e30)));
            WriteLine(unchecked((nuint)2e19));
            WriteLine(unchecked((nint)double.PositiveInfinity));
            WriteLine(unchecked((nint)9.3e18f));
            WriteLine(unchecked((nint)double.NaN));
            WriteLine(unchecked((nuint)float.NaN));
            WriteLine(unchecked((nint)float.NegativeInfinity));
            """;
        await AssertRound11AuditConversion(source, 8,
            "9223372036854775807\n-9223372036854775808\n18446744073709551615\n9223372036854775807\n9223372036854775807\n0\n0\n-9223372036854775808\n");
    }

    [Theory]
    [InlineData("nint")]
    [InlineData("nuint")]
    [InlineData("long")]
    [InlineData("ulong")]
    [InlineData("int")]
    [InlineData("uint")]
    public async Task Round11_AuditConversion_FloatingConstantsAndRuntimeValues_MatchRoslyn(string target)
    {
        var source = new StringBuilder();
        int count = 0;
        string[] doubles = ["0.0", "-0.0", "0.75", "-0.75", "12.75", "-12.75", "-1.0",
            "2147483647.75", "2147483648.0", "-2147483648.75", "-2147483649.0",
            "4294967295.75", "4294967296.0", "9223372036854775808.0 - 1024.0",
            "9223372036854775808.0", "-9223372036854775808.0", "-9223372036854775808.0 - 2048.0",
            "18446744073709551616.0 - 2048.0", "18446744073709551616.0", "1e30", "-1e30", "2e19",
            "double.NaN", "double.PositiveInfinity", "double.NegativeInfinity"];
        string[] singles = ["0.0f", "-0.0f", "0.75f", "-0.75f", "12.75f", "-12.75f", "-1.0f",
            "2147483520.0f", "2147483648.0f", "-2147483648.0f", "-2147483904.0f",
            "4294967040.0f", "4294967296.0f", "9223372036854775808.0f", "18446744073709551616.0f",
            "1e30f", "-1e30f", "2e19f", "9.3e18f", "float.NaN", "float.PositiveInfinity", "float.NegativeInfinity"];
        foreach (var (type, values) in new[] { ("double", doubles), ("float", singles) })
        {
            for (int i = 0; i < values.Length; i++)
            {
                // Fixed-width invalid unchecked constants fold to zero; native
                // nonconstants and non-const locals use runtime saturation.
                source.AppendLine($"WriteLine(unchecked(({target})({values[i]})));");
                source.AppendLine($"{type} value{type}{i} = {values[i]}; WriteLine(unchecked(({target})value{type}{i}));");
                count += 2;
            }
        }
        source.AppendLine($"const double Large = 1e30; WriteLine(unchecked(({target})Large));");
        source.AppendLine($"WriteLine(unchecked(({target})ConversionValues.Large));");
        source.AppendLine($"WriteLine(unchecked(({target})(1e29 * 10.0)));");
        string inside = target == "nuint" ? "4294967295.75" : target == "nint" ? "2147483647.75" : "12.75";
        source.AppendLine($"const {target} Local = unchecked(({target})({inside})); WriteLine(Local); WriteLine(ConversionValues.Inside);");
        if (target != "nint" && target != "nuint")
        {
            source.AppendLine($"const {target} Zero = unchecked(({target})double.PositiveInfinity); WriteLine(Zero); WriteLine(ConversionValues.Zero);");
            count += 2;
        }
        source.AppendLine($"public static class ConversionValues {{ public const double Large = 1e30; public const {target} Inside = unchecked(({target})({inside}));");
        if (target != "nint" && target != "nuint") source.AppendLine($"public const {target} Zero = unchecked(({target})double.NaN);");
        source.AppendLine("}");
        await AssertRound11AuditConversion(source.ToString(), count + 5);
    }

    [Theory]
    [InlineData("nint")]
    [InlineData("nuint")]
    public async Task Round11_AuditConversion_IntegralNativeCasts_KeepUncheckedAndVariableSemantics(string target)
    {
        string[] values = ["0", "-1", "2147483647", "-2147483648", "2147483648L", "-2147483649L",
            "4294967295U", "4294967296L", "long.MaxValue", "long.MinValue", "ulong.MaxValue",
            "9223372036854775808UL", "(nint)1", "(nuint)1", "(nint)(-1)", "(nuint)4294967295U"];
        var source = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            source.AppendLine($"WriteLine(unchecked(({target})({values[i]}))); ");
            // A non-const local never produces the portability warning. Its
            // ordinary cast must match the explicit unchecked constant cast.
            source.AppendLine($"var value{i} = {values[i]}; WriteLine(({target})value{i});");
        }
        source.AppendLine($"WriteLine(unchecked(({target})IntegralValues.Outside));");
        source.AppendLine($"WriteLine(unchecked(({target})IntegralValues.Outside) == 0);");
        source.AppendLine("public static class IntegralValues { public const long Outside = long.MaxValue; }");
        await AssertRound11AuditConversion(source.ToString(), values.Length * 2 + 2);
    }

    [Fact]
    public async Task Round11_AuditConversion_NestedNativeCasts_AreNotFalseIntegerOrBooleanConstants()
    {
        const string source = """
            WriteLine(unchecked((long)(nint)1e30));
            WriteLine(unchecked((ulong)(nuint)2e19));
            WriteLine(unchecked((nint)1e30) == 0);
            WriteLine(unchecked((nint)1e30) > 0);
            WriteLine(unchecked((nint)(-1e30)) < 0);
            WriteLine(unchecked((nint)1e30) == 0 ? 11 : 22);
            WriteLine(unchecked((double)(nint)1e30) == 9223372036854775808.0);
            WriteLine(Return());
            static long Return() => unchecked((long)(nint)double.PositiveInfinity);
            """;
        await AssertRound11AuditConversion(source, 8,
            "9223372036854775807\n18446744073709551615\nFalse\nTrue\nTrue\n22\nTrue\n9223372036854775807\n");
    }

    [Theory]
    [InlineData("nint", "2147483647.75", true)]
    [InlineData("nint", "-2147483648.75", true)]
    [InlineData("nuint", "4294967295.75", true)]
    [InlineData("nuint", "-0.75", true)]
    [InlineData("nint", "2147483648.0", false)]
    [InlineData("nint", "-2147483649.0", false)]
    [InlineData("nuint", "4294967296.0", false)]
    [InlineData("nuint", "-1.0", false)]
    [InlineData("nint", "2147483520.0f", true)]
    [InlineData("nint", "-2147483648.0f", true)]
    [InlineData("nuint", "4294967040.0f", true)]
    [InlineData("nuint", "-0.75f", true)]
    [InlineData("nint", "2147483648.0f", false)]
    [InlineData("nint", "-2147483904.0f", false)]
    [InlineData("nuint", "4294967296.0f", false)]
    [InlineData("nuint", "-1.0f", false)]
    [InlineData("nint", "double.NaN", false)]
    [InlineData("nuint", "double.NaN", false)]
    [InlineData("nint", "double.PositiveInfinity", false)]
    [InlineData("nuint", "double.PositiveInfinity", false)]
    [InlineData("nint", "double.NegativeInfinity", false)]
    [InlineData("nuint", "double.NegativeInfinity", false)]
    [InlineData("nint", "float.NaN", false)]
    [InlineData("nuint", "float.NaN", false)]
    [InlineData("nint", "float.PositiveInfinity", false)]
    [InlineData("nuint", "float.PositiveInfinity", false)]
    [InlineData("nint", "float.NegativeInfinity", false)]
    [InlineData("nuint", "float.NegativeInfinity", false)]
    [InlineData("nint", "0", true)]
    [InlineData("nint", "-1", true)]
    [InlineData("nint", "2147483647L", true)]
    [InlineData("nint", "-2147483648L", true)]
    [InlineData("nint", "2147483648L", false)]
    [InlineData("nint", "-2147483649L", false)]
    [InlineData("nint", "4294967295U", false)]
    [InlineData("nint", "long.MaxValue", false)]
    [InlineData("nint", "long.MinValue", false)]
    [InlineData("nint", "ulong.MaxValue", false)]
    [InlineData("nint", "9223372036854775808UL", false)]
    [InlineData("nint", "(nuint)4294967295U", false)]
    [InlineData("nint", "(nuint)1", true)]
    [InlineData("nuint", "0", true)]
    [InlineData("nuint", "-1", false)]
    [InlineData("nuint", "2147483648L", true)]
    [InlineData("nuint", "4294967295U", true)]
    [InlineData("nuint", "4294967296L", false)]
    [InlineData("nuint", "long.MaxValue", false)]
    [InlineData("nuint", "long.MinValue", false)]
    [InlineData("nuint", "ulong.MaxValue", false)]
    [InlineData("nuint", "9223372036854775808UL", false)]
    [InlineData("nuint", "(nint)(-1)", false)]
    [InlineData("nuint", "(nint)1", true)]
    public void Round11_AuditConversion_NativeConstantEligibility_AgreesForLocalsAndFields(string target, string value, bool constant)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-native-constant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            string cast = $"unchecked(({target})({value}))";
            File.WriteAllText(path, $"WriteLine({cast});");
            var ordinary = VibraniumCompilerDriver.CreateCompilationFromPath(path);
            Assert.Empty(ordinary.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var tree = ordinary.SyntaxTrees.First(t => t.FilePath == path);
            var node = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().First();
            Assert.Equal(constant, ordinary.GetSemanticModel(tree).GetConstantValue(node).HasValue);

            foreach (bool field in new[] { false, true })
            {
                string source = field
                    ? $"WriteLine(C.Value); public static class C {{ public const {target} Value = {cast}; }}"
                    : $"const {target} value = {cast}; WriteLine(value);";
                File.WriteAllText(path, source);
                var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path);
                var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
                if (constant) Assert.Empty(diagnostics);
                else Assert.Contains(diagnostics, d => d.Id == "CS0133");
                var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
                Assert.False(stage0.Success);
                Assert.Contains(Round12CutSelfTests.CutMessage(17), string.Join("\n", stage0.Errors), StringComparison.Ordinal);
                var self = RunProcess(DotnetExe, ResolveRound5VibSelfDllPath(), "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
                Assert.NotEqual(0, self.ExitCode);
                Assert.Contains(Round12CutSelfTests.CutMessage(17), self.Stdout + self.Stderr, StringComparison.Ordinal);
                if (!constant) Assert.Contains("VIB000", self.Stdout + self.Stderr);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("nint", "1e30", "CS0221")]
    [InlineData("nint", "-1e30", "CS0221")]
    [InlineData("nuint", "2e19", "CS0221")]
    [InlineData("nuint", "-1.0", "CS0221")]
    [InlineData("nint", "double.NaN", "CS0221")]
    [InlineData("nuint", "double.NaN", "CS0221")]
    [InlineData("nint", "double.PositiveInfinity", "CS0221")]
    [InlineData("nuint", "double.NegativeInfinity", "CS0221")]
    [InlineData("nint", "9.3e18f", "CS0221")]
    [InlineData("nint", "float.NaN", "CS0221")]
    [InlineData("nuint", "float.PositiveInfinity", "CS0221")]
    [InlineData("nint", "float.NegativeInfinity", "CS0221")]
    [InlineData("nint", "2147483648.0", "CS8778")]
    [InlineData("nint", "-2147483649.0", "CS8778")]
    [InlineData("nuint", "4294967296.0", "CS8778")]
    [InlineData("nint", "2147483648.0f", "CS8778")]
    [InlineData("nint", "-2147483904.0f", "CS8778")]
    [InlineData("nuint", "4294967296.0f", "CS8778")]
    [InlineData("nint", "2147483648L", "CS8778")]
    [InlineData("nint", "-2147483649L", "CS8778")]
    [InlineData("nint", "4294967295U", "CS8778")]
    [InlineData("nint", "long.MaxValue", "CS8778")]
    [InlineData("nint", "long.MinValue", "CS8778")]
    [InlineData("nint", "(nuint)4294967295U", "CS8778")]
    [InlineData("nint", "ulong.MaxValue", "CS0221")]
    [InlineData("nint", "9223372036854775808UL", "CS0221")]
    [InlineData("nuint", "-1", "CS0221")]
    [InlineData("nuint", "long.MinValue", "CS0221")]
    [InlineData("nuint", "(nint)(-1)", "CS0221")]
    [InlineData("nuint", "4294967296L", "CS8778")]
    [InlineData("nuint", "long.MaxValue", "CS8778")]
    [InlineData("nuint", "ulong.MaxValue", "CS8778")]
    [InlineData("nuint", "9223372036854775808UL", "CS8778")]
    public void Round11_AuditConversion_NativeConstantsWithoutUnchecked_StillRequireValidSource(string target, string value, string diagnostic)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-native-checked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, $"WriteLine(({target})({value}));");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == diagnostic);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = RunProcess(DotnetExe, ResolveRound5VibSelfDllPath(), "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.True(self.ExitCode != 0, self.Stdout + self.Stderr);
            Assert.Contains("VIB000", self.Stdout + self.Stderr);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task AssertRound11AuditConversion(string source, int lines, string? expected = null)
    {
        var syntax = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp15));
        if (syntax.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Any(name => name.Identifier.ValueText is "nint" or "nuint"))
        {
            await Round12CutSelfTests.AssertRejectedCut(source, 17);
            return;
        }

        // Reuse the existing managed oracle and Stage0 -O3 ASan execution helper.
        var baseline = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal((0, ""), (baseline.ManagedExit, baseline.ManagedErr));
        Assert.Equal(lines, baseline.ManagedOut.Count(c => c == '\n'));
        if (expected is not null) Assert.Equal(expected, baseline.ManagedOut);
        Assert.Equal((baseline.ManagedExit, baseline.ManagedOut, baseline.ManagedErr),
            (baseline.NativeExit, baseline.NativeOut, baseline.NativeErr));

        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-conversion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            foreach (string compiler in new[] { typeof(VibraniumCompilerDriver).Assembly.Location, ResolveRound5VibSelfDllPath() })
            {
                foreach (string optimization in new[] { "-O0", "-O3" })
                {
                    string binary = Path.Combine(temp, Path.GetFileNameWithoutExtension(compiler) + optimization);
                    var build = RunProcess(DotnetExe, compiler, "build", path, "--sysroot", Path.Combine(RepoRoot, "src"), optimization, "-o", binary);
                    Assert.True(build.ExitCode == 0, build.Stdout + build.Stderr);
                    Assert.Equal((0, baseline.ManagedOut, ""), RunProcess(binary));
                }
                string ir = Path.Combine(temp, Path.GetFileNameWithoutExtension(compiler) + ".ll");
                var emit = RunProcess(DotnetExe, compiler, "emit-ll", path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", ir);
                Assert.True(emit.ExitCode == 0, emit.Stdout + emit.Stderr);
                var verify = RunProcess("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.ExitCode == 0, verify.Stderr);
                string sanitized = ir + ".asan";
                var link = RunProcess("clang-18", "-O3", "-fsanitize=address,leak", ir, "-o", sanitized);
                Assert.True(link.ExitCode == 0, link.Stderr);
                Assert.Equal((0, baseline.ManagedOut, ""), RunProcess(sanitized));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
