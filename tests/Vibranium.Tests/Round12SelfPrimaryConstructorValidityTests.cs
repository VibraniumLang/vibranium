using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfPrimaryConstructorValidityTests
{
    [Theory]
    [InlineData("struct S(Span<int> p) { public int First => p[0]; }", "CS9110")]
    [InlineData("struct S(ReadOnlySpan<int> p) { public int First() => 2 + p[0]; }", "CS9110")]
    [InlineData("record struct S(ReadOnlySpan<int> p) { public int First => p[0]; }", "CS8345")]
    [InlineData("sealed class S(Span<int> p) { public int First { get { return p[0]; } } }", "CS9110")]
    [InlineData("sealed class S(global::System.ReadOnlySpan<int> p) { public int First() { if (p.Length > 0) { return p[0]; } return 0; } }", "CS9110")]
    [InlineData("struct S(Span<int> p) { public Span<int> View = p; }", "CS8345")]
    [InlineData("sealed class S(ReadOnlySpan<int> p) { public ReadOnlySpan<int> View = p; }", "CS8345")]
    [InlineData("struct S(ref int p) { public int First => p; }", "CS9109")]
    [InlineData("struct S(in int p) { public int First() => p; }", "CS9109")]
    [InlineData("sealed class S(out int p) { public int First { get { return p; } } }", "CS9109")]
    public Task RejectsInvalidStorageOrCapture(string type, string csharpId) =>
        Round12SelfCSharpValidity.RejectAsync("WriteLine(1);\n" + type, csharpId);

    [Theory]
    [InlineData("Span<int> values = [9]; var s = new S(values); WriteLine(s.First); struct S(Span<int> p) { public int First = p[0]; }")]
    [InlineData("ReadOnlySpan<int> values = [9]; var s = new S(values); WriteLine(s.First); sealed class S(ReadOnlySpan<int> p) { public int First = p[0]; }")]
    public Task ConstructorCanReadTheViewWithoutCapturingIt(string source) => Round12RuleWitness.AcceptAsync(source, "9\n");
}
