using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class CiWorkflowAndShardingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("TRUE")]
    public void NonCiValuesRequireHeavy(string ciValue)
    {
        Assert.False(HeavyGateProcess.IsCiEnvironment(ciValue));
        var start = HeavyGateProcess.CreateStartInfo("tool", ["arg with spaces", ""], ciValue);
        Assert.Equal("heavy", start.FileName);
        Assert.Equal(["tool", "arg with spaces", ""], start.ArgumentList);
    }

    [Fact]
    public void ExactCiValueStartsCommandDirectly()
    {
        Assert.True(HeavyGateProcess.IsCiEnvironment("true"));
        var start = HeavyGateProcess.CreateStartInfo("tool", ["arg with spaces", ""], "true");
        Assert.Equal("tool", start.FileName);
        Assert.Equal(["arg with spaces", ""], start.ArgumentList);
    }

    [Fact]
    public void NullUsesCurrentCiEnvironment()
    {
        // No process-wide environment writes: other test classes start processes in parallel.
        bool isCi = Environment.GetEnvironmentVariable("CI") == "true";
        Assert.Equal(isCi, HeavyGateProcess.IsCiEnvironment(null));
        var start = HeavyGateProcess.CreateStartInfo("tool", ["arg"], null);
        Assert.Equal(isCi ? "tool" : "heavy", start.FileName);
        Assert.Equal(isCi ? ["arg"] : ["tool", "arg"], start.ArgumentList);
    }

    [Fact]
    public void WorkflowRetainsAllCiGates()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("permissions:\n  contents: read", workflow.ReplaceLineEndings("\n"));
        Assert.Contains("shard: [1, 2, 3, 4, 5, 6]", workflow);
        Assert.Contains("fail-fast: false", workflow);
        Assert.Contains("xunit-shards.py verify --shards 6", workflow);
        Assert.Contains("xunit-shards.py run --shard", workflow);
        Assert.Contains("bootstrap.sh --skip-benchmark", workflow);
        Assert.Equal(3, workflow.Split("run: ./eng/conformance/run-conformance.sh ci", StringSplitOptions.None).Length - 1);
        Assert.Contains("CONF_PROFILE: activation", workflow);
        Assert.Contains("CONF_PROFILE: soundness", workflow);
        Assert.Contains("run: ./eng/conformance/run-conformance.sh matrix", workflow);
        Assert.Contains("run-mutation.sh self-test", workflow);
        Assert.Contains("run-mutation.sh ci", workflow);
        Assert.Contains("formal-proofs:", workflow);
        Assert.Contains("lake build && lake exe vibranium-formal-verify", workflow);
        Assert.Contains("actions/checkout@v5", workflow);
        Assert.DoesNotContain("actions/checkout@v4", workflow);

        string setup = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "actions", "setup-toolchain", "action.yml"));
        Assert.Contains("nuget-${{ runner.os }}-${{ inputs.install-llvm }}-${{ hashFiles(", setup);
    }

    [Fact]
    public async Task ShardDriverSelfTestPasses()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = RepoRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "eng/ci/xunit-shards.py", "self-test" },
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
            Assert.Fail("Shard driver self-test timed out.");
        }
        string output = await stdout;
        string error = await stderr;
        Assert.True(process.ExitCode == 0, $"Shard driver exited {process.ExitCode}: {output}{error}");
        Assert.Contains("xUnit shard self-test passed", output);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Vibranium.slnx")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("Vibranium.slnx was not found above the test output directory.");
    }
}
