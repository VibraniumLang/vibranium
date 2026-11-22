using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger v3: witnesses for the SPEC §3.2 assignment-value sentence, rule R3.2-assignment-value in docs/RULES.md. Using
// the value of an assignment expression moves a !Copy destination, so a later use of the destination is VIB001; a
// Copy destination stays usable. The Span<T> reject witness waits for the V4 vib-self fix and lives in the
// rule-ledger-v3 after-fixes delta (vib-self accepts it today; the Analyzer and Stage-0 report VIB001).
public sealed class Round12RuleWitnessAssignmentValueTests
{
    private const string MakeVec = "static Vec<int> MakeVec(int a) { Vec<int> v = [a, a + 1, a + 2]; return v; }\n";
    private const string Node = "sealed class Node(int v) { public int V = v; }\n";

    // The assignment's value read through a member, passed as an argument, and assigned again (a = d = e), with a
    // Vec<int> or class destination used afterwards.
    [Theory]
    [InlineData("Vec<int> v = [1];\nint n = (v = MakeVec(7)).Count;\nWriteLine(n);\nWriteLine(v.Count);\n" + MakeVec)]
    [InlineData("Vec<int> v = [1];\nWriteLine(Count(v = MakeVec(7)));\nWriteLine(v.Count);\nstatic int Count(Vec<int> x) => x.Count;\n" + MakeVec)]
    [InlineData("Vec<int> a = [1];\nVec<int> b = [2];\na = b = MakeVec(7);\nWriteLine(a.Count);\nWriteLine(b.Count);\n" + MakeVec)]
    [InlineData("Node x = new Node(1);\nint k = (x = new Node(2)).V;\nWriteLine(k);\nWriteLine(x.V);\n" + Node)]
    public Task R3_2_AssignmentValue_Reject(string source) => Round12RuleWitness.RejectAsync(source, "VIB001", exact: true);

    // The same shapes without a later use of the moved destination (or after reassigning it), a Copy ReadOnlySpan<T>
    // destination that stays usable, and a Span<T> destination that is not used again.
    [Theory]
    [InlineData("Vec<int> v = [1];\nint n = (v = MakeVec(7)).Count;\nWriteLine(n);\n" + MakeVec, "3\n")]
    [InlineData("Vec<int> v = [1];\nint n = (v = MakeVec(7)).Count;\nv = MakeVec(n);\nWriteLine(v[0]);\n" + MakeVec, "3\n")]
    [InlineData("Vec<int> a = [1];\nVec<int> b = [2];\na = b = MakeVec(7);\nWriteLine(a.Count);\n" + MakeVec, "3\n")]
    [InlineData("Node x = new Node(1);\nint k = (x = new Node(2)).V;\nWriteLine(k);\n" + Node, "2\n")]
    [InlineData("Vec<int> a = [1, 2];\nVec<int> b = [3, 4, 5];\nReadOnlySpan<int> r = a.AsReadOnlySpan();\nint n = (r = b.AsReadOnlySpan()).Length;\nWriteLine(n + r.Length);\nWriteLine(r[0]);\n", "6\n3\n")]
    [InlineData("Vec<int> a = [1, 2];\nVec<int> b = [3, 4, 5];\nSpan<int> s = a.AsSpan();\ns[0] = 9;\nint n = (s = b.AsSpan()).Length;\nWriteLine(n);\nWriteLine(a[0]);\n", "3\n9\n")]
    public Task R3_2_AssignmentValue_Accept(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);
}
