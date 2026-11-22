using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger v3 after-fixes delta, group P1-4 (Roslyn fix "roslyn-isnot-rvalue"). On candidate-c6-pre3 the Analyzer and
// Stage-0 treat the binding of a negated pattern (`is not Some<T>(var x)`) on an rvalue scrutinee as a shared borrow of a
// hidden temporary (`CfgMirLowerer.Expressions.cs`, `ContainsUnionPattern` path, `scrutIsConsumedByValue: false`): they
// report VIB005 on a move of the binding, VIB004 on a mutation, VIB007 on a view of it stored in a local, and an extra
// VIB007 next to VIB003. SPEC §3.4 says an rvalue scrutinee is consumed and its bindings own their payloads, as vib-self
// and vib-stage2 already implement (native = managed, ASan/LSan clean, views-of-temporaries probe). Add this file and
// RULES.diff once the fix lands.
public sealed class Round12RuleWitnessIsNotRvalueTests
{
    private const string NodeV = "static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));\nsealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }\n";
    private const string OptVec = "static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n";

    // The binding owns its payload: it may be moved (also out of a loop), mutated, and viewed from a local.
    [Theory]
    [InlineData("Run();\nstatic void Run()\n{\n    if (MakeNodeV(7) is not Some<NodeV>(var n)) return;\n    Sink(n);\n}\nstatic void Sink(NodeV x) => WriteLine(x.Items[0]);\n" + NodeV, "7\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    NodeV keep = new NodeV(0);\n    for (int i = 1; i <= 2; i++)\n    {\n        if (MakeNodeV(i * 10) is not Some<NodeV>(var n)) return;\n        if (i == 1) keep = n;\n    }\n    WriteLine(keep.Items[0]);\n}\n" + NodeV, "10\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    if (MakeNodeV(7) is not Some<NodeV>(var n)) return;\n    n.Items.Add(5);\n    WriteLine(n.Items.Count);\n}\n" + NodeV, "3\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    if (MakeOptVec(7) is not Some<Vec<int>>(var v)) return;\n    v.Add(4);\n    WriteLine(v.Count);\n}\n" + OptVec, "3\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    Vec<int> keep = [0];\n    for (int i = 1; i <= 2; i++)\n    {\n        if (MakeOptVec(i * 10) is not Some<Vec<int>>(var v)) return;\n        if (i == 1) keep = v;\n    }\n    WriteLine(keep[0]);\n}\n" + OptVec, "10\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    if (MakeOptVec(7) is not Some<Vec<int>>(var v)) return;\n    Vec<int> w = v;\n    WriteLine(w[0]);\n}\n" + OptVec, "7\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    if (MakeOptVec(7) is not Some<Vec<int>>(var v)) return;\n    ReadOnlySpan<int> s = v.AsReadOnlySpan();\n    WriteLine(s[1]);\n}\n" + OptVec, "8\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    if (MakeNodeV(7) is not Some<NodeV>(var n)) return;\n    ReadOnlySpan<int> s = n.Items.AsReadOnlySpan();\n    WriteLine(s[1]);\n}\n" + NodeV, "8\n")]
    public Task R3_4_RvalueScrutinee_Accept_NegatedPatternBinding(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);

    // An owned binding is dropped at the end of its scope, so a view of it that outlives the scope is VIB003 (and only
    // VIB003: the binding is not a temporary).
    [Theory]
    [InlineData("Run();\nstatic void Run()\n{\n    Vec<int> keep = [1];\n    ReadOnlySpan<int> s = keep.AsReadOnlySpan();\n    {\n        if (MakeNodeV(7) is not Some<NodeV>(var n)) return;\n        s = n.Items.AsReadOnlySpan();\n    }\n    WriteLine(s[1]);\n}\n" + NodeV)]
    [InlineData("Vec<int> keep = [1];\nReadOnlySpan<int> saved = keep.AsReadOnlySpan();\nfor (int i = 1; i <= 3; i++)\n{\n    if (MakeNodeV(i * 10) is not Some<NodeV>(var n)) return;\n    if (i == 1) saved = n.Items.AsReadOnlySpan();\n}\nWriteLine(saved[0]);\n" + NodeV)]
    public Task R3_4_RvalueScrutinee_Reject_NegatedPatternViewOutlivesBinding(string source) => Round12RuleWitness.RejectAsync(source, "VIB003", exact: true);
}
