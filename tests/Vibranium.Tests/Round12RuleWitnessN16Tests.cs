using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger v3 after-fixes delta, group N16 (Roslyn fix "roslyn-n16"). A collection expression assigned to a by-value
// ReadOnlySpan<T> parameter gets storage owned by the parameter's scope (SPEC §3.7 "Span-typed collection expressions"),
// so returning the parameter (or a slice of it) afterwards is VIB003 and VIB007. On candidate-c6-pre3 vib-self and
// vib-stage2 report both; the Analyzer and Stage-0 report only VIB007 (conformance-fuzz N16 probes). The assignment-
// expression form `return p = [1, 2];` behaves the same. Add this file and RULES.diff once the fix lands.
public sealed class Round12RuleWitnessN16Tests
{
    private const string Caller = "ReadOnlySpan<int> x = [5, 6, 7];\n";

    [Theory]
    [InlineData(Caller + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; return p; }\n")]
    [InlineData(Caller + "WriteLine(G(x, 2)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k)\n{\n    if (k > 1)\n    {\n        p = [1, 2];\n    }\n    return p;\n}\n")]
    [InlineData(Caller + "WriteLine(G(x)[0]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2, 3]; return p[1..]; }\n")]
    [InlineData(Caller + "WriteLine(G(x).Length);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = []; return p; }\n")]
    [InlineData(Caller + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) { return p = [1, 2]; }\n")]
    [InlineData(Caller + "WriteLine(G(x)[1]);\nstatic ReadOnlySpan<int> G(ReadOnlySpan<int> p) => p = [1, 2];\n")]
    public async Task R3_7_CollectionStorage_Reject_ByValueViewParameter(string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        string[] expected = ["VIB003", "VIB007"];
        Assert.Equal(expected, result.Analyzer);
        Assert.Equal(expected, result.Stage0);
        Assert.True(result.Self.SequenceEqual(expected), $"vib-self reported [{string.Join(",", result.Self)}], expected [VIB003,VIB007]\n{result.SelfOutput}");
    }
}
