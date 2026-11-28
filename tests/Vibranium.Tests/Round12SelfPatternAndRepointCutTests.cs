using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfPatternAndRepointCutTests
{
    private const string C15 = "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement.";
    private const string C24 = "Property patterns on Option and Result cases are not supported in Vibranium v0.2; use the positional form, such as Some<T>(var x).";
    private const string C25 = "Conditional and short-circuit expressions in a ref reassignment are not supported in Vibranium v0.2; compute the value in a local first, or use an if statement.";

    [Theory]
    [InlineData("Option<int> o = Some(7);\nWriteLine(o is Some<int>(7) /*cut*/{ Value: 999 });")]
    [InlineData("Option<int> o = None;\nWriteLine(o is NoneType /*cut*/{});\nWriteLine(o is Some<int> { });")]
    [InlineData("Result<int,int> r = Err(3);\nswitch (r) { case Err<int> /*cut*/{ Error: 3 }: WriteLine(3); break; default: break; }")]
    [InlineData("Result<int,int> r = Ok(3);\nWriteLine(r switch { Ok<int>(var k) /*cut*/{ Value: 3 } => k, _ => 0 });")]
    [InlineData("Option<Option<int>> o = Some<Option<int>>(Some(3));\nWriteLine(o is Some<Option<int>>(Some<int> /*cut*/{ Value: 3 }));")]
    [InlineData("Option<Option<int>> o = Some<Option<int>>(Some(3));\nswitch (o) { case Some<Option<int>>(Some<int> /*cut*/{ Value: var n }): WriteLine(n); break; default: break; }")]
    [InlineData("Option<Option<int>> o = Some<Option<int>>(Some(3));\nWriteLine(o switch { Some<Option<int>>(Some<int> /*cut*/{ Value: var n }) => n, _ => 0 });")]
    [InlineData("Option<int> o = Some(3);\nWriteLine(o is global::Vibranium.Some<int> /*cut*/{ Value: 3 });")]
    public Task UnionCasePropertyClausesAreC24(string source) => AssertSelfCutAsync(24, source, exactIds: true);

    [Theory]
    [InlineData("Span<int> s = [4, 7]; ref int r = ref s[0];\nr = /*cut*/ref s[true ? 0 : 1];\nWriteLine(r);")]
    [InlineData("Span<int> s = [4, 7]; ref int r = ref s[0]; bool f = Flag();\nWriteLine((r = /*cut*/ref s[Convert(f && false)]));\nstatic bool Flag() => true; static int Convert(bool b) => b ? 0 : 1;")]
    [InlineData("Span<int> s = [4, 7]; ref int r = ref s[0]; int i = 1;\nr = /*cut*/ref s[Choose(i is 1)];\nWriteLine(r); static int Choose(bool b) => b ? 0 : 1;")]
    [InlineData("Span<int> s = [4, 7]; ref int r = ref s[0]; int i = 1;\nr = /*cut*/ref s[i switch { 0 => 1, _ => 0 }];\nr = ref s[Choose(i is 1)];\nWriteLine(r); static int Choose(bool b) => b ? 0 : 1;")]
    public Task ControlFlowInARepointIsC25(string source) => AssertSelfCutAsync(25, source, exactIds: true);

    [Theory]
    [InlineData("int a = 4; int b = 7; ref int r = ref a; bool c = Flag();\nr = /*cut*/ref (c /*c15*/? ref a : ref b);\nWriteLine(r); static bool Flag() => true;")]
    [InlineData("int a = 4; int b = 7; ref int r = ref a; ref int q = ref b; bool c = Flag();\nr = /*cut*/ref Self(ref (q = ref (c /*c15*/? ref a : ref b)));\nWriteLine(r); static bool Flag() => true; static ref int Self(ref int a) => ref a;")]
    public async Task RepointWithConditionalRefReportsOneOfEachCut(string marked)
    {
        const string c25Marker = "/*cut*/";
        const string c15Marker = "/*c15*/";
        int c25Position = marked.IndexOf(c25Marker, StringComparison.Ordinal);
        string withoutC25 = marked.Replace(c25Marker, "", StringComparison.Ordinal);
        int c15Position = withoutC25.IndexOf(c15Marker, StringComparison.Ordinal);
        string source = withoutC25.Replace(c15Marker, "", StringComparison.Ordinal);
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { "VIB000" }, result.Self);
        Assert.Equal(2, Regex.Matches(result.SelfOutput, ": error VIB000:").Count);
        AssertCutAt(C25, source, c25Position, "Program.cs", result.SelfOutput);
        AssertCutAt(C15, source, c15Position, "Program.cs", result.SelfOutput);
    }

    [Theory]
    [InlineData("int a = 4; int b = 7; bool c = Flag();\nWriteLine((c /*cut*/? ref a : ref b) switch { 4 => 1, _ => 2 });\nstatic bool Flag() => true;")]
    [InlineData("int a = 4; int b = 7; bool c = Flag();\nswitch (c /*cut*/? ref a : ref b) { case 4: WriteLine(1); break; default: break; }\nstatic bool Flag() => true;")]
    [InlineData("Option<int> a = Some(4); Option<int> b = None; bool c = Flag();\nWriteLine((c /*cut*/? ref a : ref b).UnwrapOr(0));\nstatic bool Flag() => true;")]
    public Task ConditionalRefOutsideRefAssignmentIsC15Only(string source) => AssertSelfCutAsync(15, source, exactIds: true);

    [Theory]
    [InlineData("Option<Option<int>> o = Some<Option<int>>(Some(3));\nif (o is Some<Option<int>>(Some<int>(var n))) WriteLine(n);", "3\n")]
    [InlineData("int p = 1; Span<int> s = [4, 7]; ref int r = ref s[p is 1 ? 1 : 0];\nWriteLine(r);", "7\n")]
    [InlineData("Span<int> s = [4, 7]; WriteLine(Pick(s, true));\nstatic ref int Pick(Span<int> s, bool b) => ref s[b ? 1 : 0];", "7\n")]
    [InlineData("bool a = Choose(true); bool b = Choose(false); Span<int> s = [4, 7];\nBump(ref s[a || b ? 1 : 0]); WriteLine(s[1]);\nstatic bool Choose(bool b) => b; static void Bump(ref int x) { x++; }", "8\n")]
    public Task PositionalAndNonAssignmentControls(string source, string output) => Round12RuleWitness.AcceptAsync(source, output);

    [Theory]
    [InlineData("Span<int> s = [4];\nWriteLine(s switch { [4] when W(s) => 1, [7] => 2, _ => 3 });\nstatic bool W(Span<int> x) { x[0] = 7; return false; }\n", "VIB000")]
    [InlineData("Vec<int> w = [4];\nWriteLine(w switch { [4] when Grow(ref w) => 1, [4, 5] => 2, _ => 3 });\nstatic bool Grow(ref Vec<int> v) { v.Add(5); return false; }\n", "VIB000")]
    public Task ListPatternsDoNotCascadeIntoGuardOwnership(string source, string expected) =>
        Round12RuleWitness.RejectAsync(source, expected, exact: true);

    internal static async Task AssertSelfCutAsync(int cut, string marked, string fileName = "Program.cs", bool exactIds = false)
    {
        const string marker = "/*cut*/";
        int position = marked.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(position >= 0);
        string source = marked.Replace(marker, "", StringComparison.Ordinal);
        string message = cut switch { 15 => C15, 24 => C24, 25 => C25, _ => throw new ArgumentOutOfRangeException(nameof(cut)) };
        var checkedSource = await Round12RuleWitness.CheckAllAsync(source, fileName);
        if (exactIds) Assert.Equal(new[] { "VIB000" }, checkedSource.Self);
        AssertCutAt(message, source, position, fileName, checkedSource.SelfOutput);
        if (cut == 15) Assert.DoesNotContain(C25, checkedSource.SelfOutput, StringComparison.Ordinal);
    }

    private static void AssertCutAt(string message, string source, int position, string fileName, string output)
    {
        Assert.Single(Regex.Matches(output, Regex.Escape(message)).Cast<Match>());
        int line = 1 + source[..position].Count(character => character == '\n');
        int column = position - source.LastIndexOf('\n', position);
        Assert.Contains($"{fileName}({line},{column}): error VIB000: {message}", output, StringComparison.Ordinal);
    }
}
