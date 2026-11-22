using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger v3: witnesses for the SPEC §3.3 "Temporaries" rule (VIB007) and its `foreach` consequence, rules
// R3.3-temporary-borrow, R3.3-temporary-return and R3.3-temporary-foreach in docs/RULES.md. A borrow rooted at a
// temporary is valid within the expression that creates it and in the payload bindings of the patterns it feeds; a
// named variable cannot receive it and a function cannot return it. Every reject program is C#-valid, so VIB007 is the
// only verdict, and every frontend reports exactly VIB007. The views-of-temporaries probe (rule-ledger2, job 1) found
// all of these shapes identical on the Analyzer, Stage-0, vib-self and vib-stage2.
public sealed class Round12RuleWitnessTemporaryTests
{
    private const string MakeVec = "static Vec<int> MakeVec(int a) { Vec<int> v = [a, a + 1, a + 2]; return v; }\n";
    private const string PeekNode = "static OptionRef<Node> Peek(in Option<Node> o) => o.AsRef();\nstatic Option<Node> MakeNode(int v) => Some(new Node(v));\nsealed class Node(int v) { public int V = v; }\n";
    private const string MakeBuf = "static StringBuf MakeBuf(string s) { var b = new StringBuf(); b.Append(s); return b; }\n";
    private const string MakeMap = "static StrMap<int> MakeMap() { var m = new StrMap<int>(4); _ = m.Set(\"alpha\", 1); return m; }\n";

    // A named variable receives a reference or view whose provenance includes a temporary: a local view, a local
    // OptionRef through an elided call, a `ref` local through `in`, an `is var` binding, and a Vibranium.Std view.
    [Theory]
    [InlineData("Span<int> s = MakeVec(7).AsSpan();\ns[0] = s[0] + 1;\nWriteLine(s[0]);\n" + MakeVec)]
    [InlineData("var r = Peek(MakeNode(7));\nWriteLine(r.IsSome);\nif (r is Some<Node>(var n)) WriteLine(n.V);\n" + PeekNode)]
    [InlineData("ref readonly Vec<int> r = ref Id(MakeVec(7));\nWriteLine(r[0]);\nstatic ref readonly Vec<int> Id(in Vec<int> v) => ref v;\n" + MakeVec)]
    [InlineData("if (Peek(MakeNode(7)) is var r && r.IsSome) WriteLine(1);\nif (r is Some<Node>(var n)) WriteLine(n.V);\n" + PeekNode)]
    [InlineData("ReadOnlySpan<byte> k = MakeMap().KeyAt(0);\nWriteLine(k);\n" + MakeMap)]
    public Task R3_3_TemporaryBorrow_Reject(string source) => Round12RuleWitness.RejectAsync(source, "VIB007", exact: true);

    // The same borrows used only within the expression that creates the temporary, or through the payload bindings of
    // the patterns it feeds (also after the statement, through `is not ... return`).
    [Theory]
    [InlineData("WriteLine(MakeVec(7).AsSpan()[1]);\n" + MakeVec, "8\n")]
    [InlineData("WriteLine(Id(MakeVec(7))[0]);\nstatic ref readonly Vec<int> Id(in Vec<int> v) => ref v;\n" + MakeVec, "7\n")]
    [InlineData("if (Peek(MakeNode(7)) is Some<Node>(var n)) WriteLine(n.V);\n" + PeekNode, "7\n")]
    [InlineData("Run();\nstatic void Run()\n{\n    if (Peek(MakeNode(7)) is not Some<Node>(var n)) return;\n    WriteLine(n.V);\n}\n" + PeekNode, "7\n")]
    [InlineData("WriteLine(MakeMap().KeyAt(0));\n" + MakeMap, "alpha\n")]
    public Task R3_3_TemporaryBorrow_Accept_SameExpression(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);

    // "Bind the owner to a local first": the rejected shapes become valid once the owner is a named local.
    [Theory]
    [InlineData("var v = MakeVec(7);\nSpan<int> s = v.AsSpan();\ns[0] = s[0] + 1;\nWriteLine(s[0]);\n" + MakeVec, "8\n")]
    [InlineData("var o = MakeNode(7);\nvar r = Peek(o);\nWriteLine(r.IsSome);\nif (r is Some<Node>(var n)) WriteLine(n.V);\n" + PeekNode, "True\n7\n")]
    [InlineData("var m = MakeMap();\nReadOnlySpan<byte> k = m.KeyAt(0);\nWriteLine(k);\n" + MakeMap, "alpha\n")]
    public Task R3_3_TemporaryBorrow_Accept_OwnerLocal(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);

    // A function cannot return a reference or view whose provenance includes a temporary (both programs are C#-valid).
    [Theory]
    [InlineData("WriteLine(Bad()[0]);\nstatic Span<int> Bad() => MakeHolder(7).Items.AsSpan();\nstatic Holder MakeHolder(int v) => new Holder(v);\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n")]
    [InlineData("WriteLine(Bad());\nstatic ref int Bad() => ref MakeBox(7).First();\nstatic Box MakeBox(int v) => new Box(v);\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n")]
    public Task R3_3_TemporaryReturn_Reject(string source) => Round12RuleWitness.RejectAsync(source, "VIB007", exact: true);

    // The accepted counterpart returns a view of a borrowed parameter; the caller passes a temporary and uses the view
    // within the same expression.
    [Fact]
    public Task R3_3_TemporaryReturn_Accept() => Round12RuleWitness.AcceptAsync(
        "WriteLine(View(MakeVec(7))[0]);\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\n" + MakeVec, "7\n");

    // Consequence (v0.2): a foreach whose iteration variable is a view or reference into a temporary is VIB007.
    [Theory]
    [InlineData("foreach (var line in MakeBuf(\"a\\nbb\\nccc\").Lines()) WriteLine(line);\n" + MakeBuf)]
    [InlineData("foreach (ref var x in MakeVec(7)) { x = x + 1; WriteLine(x); }\n" + MakeVec)]
    public Task R3_3_TemporaryForeach_Reject(string source) => Round12RuleWitness.RejectAsync(source, "VIB007", exact: true);

    // The same loops over a local owner, and a by-value loop over the Copy elements of a temporary, are valid.
    [Theory]
    [InlineData("var b = MakeBuf(\"a\\nbb\\nccc\");\nforeach (var line in b.Lines()) WriteLine(line);\n" + MakeBuf, "a\nbb\nccc\n")]
    [InlineData("var v = MakeVec(7);\nforeach (ref var x in v) { x = x + 1; WriteLine(x); }\n" + MakeVec, "8\n9\n10\n")]
    [InlineData("foreach (var x in MakeVec(7)) WriteLine(x);\n" + MakeVec, "7\n8\n9\n")]
    public Task R3_3_TemporaryForeach_Accept(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);
}
