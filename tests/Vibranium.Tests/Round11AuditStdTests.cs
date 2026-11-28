using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed partial class VibraniumEndToEndTests
{
    [Fact]
    public async Task Round11_AuditStd_AppendRegularFile_PreservesContentsAcrossHandles()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-append-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "data.txt");
            string source = $$"""
                using File = Vibranium.Std.File;
                File.WriteAllText("{{path}}", "start").Expect("initial");
                {
                    using var first = File.OpenAppend("{{path}}").Expect("open first");
                    using var second = File.OpenAppend("{{path}}").Expect("open second");
                    first.WriteAll("-a").Expect("first");
                    second.WriteAll("-b").Expect("second");
                    first.WriteAll("-c").Expect("third");
                }
                var contents = File.ReadAllText("{{path}}").Expect("contents");
                WriteLine(contents.AsUtf8());
                File.WriteAllText("{{path}}", "reset").Expect("truncate");
                using var reader = File.OpenRead("{{path}}").Expect("read open");
                Span<byte> bytes = [0, 0, 0, 0, 0];
                WriteLine(reader.Read(bytes).Expect("read"));
                WriteLine(bytes);
                """;
            await AssertRound11StdPipelines(source, "start-a-b-c\n5\nreset\n",
                afterRun: () => Assert.Equal("reset", File.ReadAllText(path)));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("OpenAppend")]
    [InlineData("Create")]
    public async Task Round11_AuditStd_NonSeekableStdout_WritesWithoutSeeking(string mode)
    {
        string source = $$"""
            using File = Vibranium.Std.File;
            {
                using var output = File.{{mode}}("/dev/stdout").Expect("open stdout");
                output.WriteAll("alpha\n").Expect("first write");
                output.WriteAll("beta\n").Expect("second write");
            }
            WriteLine("ok");
            """;
        // The shared RunProcess helper redirects stdout to an anonymous pipe.
        await AssertRound11StdPipelines(source, "alpha\nbeta\nok\n");
    }

    [Theory]
    [InlineData("OpenAppend")]
    [InlineData("Create")]
    [InlineData("OpenRead")]
    public async Task Round11_AuditStd_Fifo_AllFileModesRespectNonSeekability(string mode)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-append-fifo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string fifo = Path.Combine(temp, "channel");
            var create = RunProcess("mkfifo", fifo);
            Assert.True(create.ExitCode == 0, create.Stderr);
            // Linux O_RDWR anchors both ends without blocking while each child
            // opens its read-only or write-only handle. No seek is performed.
            using var anchor = new FileStream(fifo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 0, FileOptions.None);
            Assert.False(anchor.CanSeek);
            string source = mode == "OpenRead" ? $$"""
                using File = Vibranium.Std.File;
                using var input = File.OpenRead("{{fifo}}").Expect("open fifo");
                Span<byte> bytes = [0, 0, 0, 0];
                WriteLine(input.Read(bytes).Expect("read fifo"));
                WriteLine(bytes);
                """ : $$"""
                using File = Vibranium.Std.File;
                using var output = File.{{mode}}("{{fifo}}").Expect("open fifo");
                output.WriteAll("alpha").Expect("first write");
                output.WriteAll("|beta").Expect("second write");
                WriteLine("ok");
                """;
            if (mode == "OpenRead")
            {
                await AssertRound11StdPipelines(source, "4\nread\n", beforeRun: () => anchor.Write("read"u8));
            }
            else
            {
                await AssertRound11StdPipelines(source, "ok\n", afterRun: () =>
                {
                    // Bound the FIFO read so a future missing-write regression
                    // fails rather than hanging the whole test process.
                    var read = RunProcess("timeout", "15s", "dd", $"if={fifo}", "bs=1", "count=10", "status=none");
                    Assert.Equal((0, "alpha|beta", ""), read);
                });
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task AssertRound11StdPipelines(string source, string expected, Action? beforeRun = null, Action? afterRun = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-std-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round11StdManaged", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            string managed = Path.Combine(temp, "Round11StdManaged.dll");
            var emit = managedCompilation.Emit(managed);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round11StdManaged.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            // Reuse the common process/compiler helpers, with per-execution I/O
            // hooks needed to seed/drain the FIFO and compare file effects.
            var programs = new List<(string Command, string[] Arguments)> { (DotnetExe, [managed]) };
            foreach (string compiler in new[] { typeof(VibraniumCompilerDriver).Assembly.Location, ResolveRound5VibSelfDllPath() })
            {
                string prefix = Path.GetFileNameWithoutExtension(compiler);
                foreach (string optimization in new[] { "-O0", "-O3" })
                {
                    string binary = Path.Combine(temp, prefix + optimization);
                    var build = RunProcess(DotnetExe, compiler, "build", path, "--sysroot", Path.Combine(RepoRoot, "src"), optimization, "-o", binary);
                    Assert.True(build.ExitCode == 0, build.Stdout + build.Stderr);
                    programs.Add((binary, []));
                }
                string ir = Path.Combine(temp, prefix + ".ll");
                var nativeEmit = RunProcess(DotnetExe, compiler, "emit-ll", path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", ir);
                Assert.True(nativeEmit.ExitCode == 0, nativeEmit.Stdout + nativeEmit.Stderr);
                var verify = RunProcess("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.ExitCode == 0, verify.Stderr);
                string sanitized = ir + ".asan";
                var link = RunProcess("clang-18", "-O3", "-fsanitize=address,leak", ir, "-o", sanitized);
                Assert.True(link.ExitCode == 0, link.Stderr);
                programs.Add((sanitized, []));
            }
            foreach (var (command, arguments) in programs)
            {
                beforeRun?.Invoke();
                Assert.Equal((0, expected, ""), RunProcess(command, arguments));
                afterRun?.Invoke();
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
