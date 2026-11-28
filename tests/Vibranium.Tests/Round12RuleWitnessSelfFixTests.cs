using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger v3 after-fixes delta, group "self-fix" (V1–V4). These witnesses fail on candidate-c6-pre3 because vib-self
// (and vib-stage2) deviates from the Analyzer and Stage-0, which main ruled right. Add this file and RULES.diff once
// the vib-self fixes land:
// - V1 (views-of-temporaries probe P1-1): a converted rvalue bound to an `in` parameter. vib-self reports VIB000
//   ("Unsupported, unresolved, or inapplicable method call" / "... member access") where the others accept, and VIB000
//   instead of VIB007 on the stored-view form.
// - V2 (P1-2): a collection expression bound to an `in Vec<T>` parameter. vib-self reports VIB000 (and VIB001) where
//   the others accept, and VIB000,VIB001 instead of VIB007 on the stored-view form.
// - V3 (P1-3): a struct member returning a ref or view reached through a class-typed field of `this`. vib-self reports
//   VIB007 at the return site; the others accept (SPEC §3.3 ties the return to the borrowed `this`).
// - V4: the value of an assignment expression moves a Span<T> destination (SPEC §3.2). The Analyzer and Stage-0 report
//   VIB001 on the later use; vib-self accepts.
public sealed class Round12RuleWitnessSelfFixTests
{
    private const string PeekNode = "static OptionRef<Node> Peek(in Option<Node> o) => o.AsRef();\nsealed class Node(int v) { public int V = v; }\n";
    private const string BoxHolder = "struct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n";

    // V1: a converted rvalue (implicit Some<T> -> Option<T>) bound to an `in` parameter, used in the same expression.
    [Theory]
    [InlineData("WriteLine(Has(Some(7)));\nstatic bool Has(in Option<int> o) => o.IsSome;\n", "True\n")]
    [InlineData("WriteLine(Has(Some(new Node(7))));\nstatic bool Has(in Option<Node> o) => o.IsSome;\nsealed class Node(int v) { public int V = v; }\n", "True\n")]
    [InlineData("WriteLine(Peek(Some(new Node(7))).IsSome);\n" + PeekNode, "True\n")]
    [InlineData("bool b = Peek(Some(new Node(7))).IsSome;\nWriteLine(b);\n" + PeekNode, "True\n")]
    public Task R3_3_TemporaryBorrow_Accept_InParameterConversion(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);

    // V1: the stored-view form of the same call is the temporary rule's VIB007 on every frontend.
    [Fact]
    public Task R3_3_TemporaryBorrow_Reject_InParameterConversion() => Round12RuleWitness.RejectAsync(
        "var r = Peek(Some(new Node(7)));\nif (r is Some<Node>(var n)) WriteLine(n.V);\n" + PeekNode, "VIB007", exact: true);

    // V2: a collection expression bound to an `in Vec<int>` parameter, used in the same expression.
    [Theory]
    [InlineData("int n = Count([5, 6, 7]);\nWriteLine(n);\nstatic int Count(in Vec<int> v) => v.Count;\n", "3\n")]
    [InlineData("WriteLine(First([5, 6, 7]));\nstatic int First(in Vec<int> v) => v[0];\n", "5\n")]
    [InlineData("WriteLine(View([5, 6, 7])[1]);\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\n", "6\n")]
    public Task R3_3_TemporaryBorrow_Accept_CollectionToInParameter(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);

    // V2: the stored-view form is VIB007 on every frontend.
    [Fact]
    public Task R3_3_TemporaryBorrow_Reject_CollectionToInParameter() => Round12RuleWitness.RejectAsync(
        "var s = View([5, 6, 7]);\nWriteLine(s[1]);\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\n", "VIB007", exact: true);

    // V3: an unannotated struct member returning a ref or view through a class-typed field of `this` ties the result to
    // the borrowed `this`: on a local, on a temporary used in the same expression, `readonly ref readonly`, and a
    // `readonly` view property.
    [Theory]
    [InlineData("var b = new Box(7);\nWriteLine(b.First());\n" + BoxHolder, "7\n")]
    [InlineData("var b = MakeBox(7);\nref int r = ref b.First();\nr = r + 1;\nWriteLine(b.H.Items[0]);\nstatic Box MakeBox(int v) => new Box(v);\n" + BoxHolder, "8\n")]
    [InlineData("WriteLine(MakeBox(7).First());\nstatic Box MakeBox(int v) => new Box(v);\n" + BoxHolder, "7\n")]
    [InlineData("var b = new Box(7);\nWriteLine(b.First());\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ref readonly int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "7\n")]
    [InlineData("WriteLine(MakeBox(7).Items[1]);\nstatic Box MakeBox(int v) => new Box(v);\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ReadOnlySpan<int> Items => H.Items.AsReadOnlySpan(); }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "8\n")]
    public Task R3_3_ReturnProvenance_Accept_StructThisClassField(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);

    // V4: using the value of an assignment moves a Span<T> destination; the later use is VIB001 on every frontend.
    [Fact]
    public Task R3_2_AssignmentValue_Reject_Span() => Round12RuleWitness.RejectAsync(
        "Vec<int> a = [1, 2];\nSpan<int> s = a.AsSpan();\nint n = (s = []).Length;\nWriteLine(n);\nWriteLine(s.Length);\n", "VIB001", exact: true);
}
