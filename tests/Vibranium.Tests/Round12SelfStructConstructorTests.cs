using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfStructConstructorTests
{
    [Theory]
    [InlineData("var s = new S(); WriteLine(s.V); struct S { public int V; private S() { V = 3; } }", "CS8958")]
    [InlineData("var s = new S(); WriteLine(s.V); struct S { public int V; internal S() { V = 3; } }", "CS8958")]
    [InlineData("var s = new S(); WriteLine(s.V); struct S { public int V;  S() { V = 3; } }", "CS8958")]
    [InlineData("var s = new S(); WriteLine(s.V); ref struct S { public int V; private S() { V = 3; } }", "CS8958")]
    [InlineData("var s = new S(); WriteLine(s.V); readonly struct S { public readonly int V; internal S() { V = 3; } }", "CS8958")]
    [InlineData("var s = new S(); WriteLine(s.V); readonly ref struct S { public readonly int V; private S() { V = 3; } }", "CS8958")]
    public Task RejectsInvalidCSharp(string source, string csharpId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("var s = new S(); WriteLine(s.V); struct S { public int V; public S() { V = 3; } }", "3\n")]
    [InlineData("var s = new S(); WriteLine(s.V); readonly struct S { public readonly int V; public S() { V = 3; } }", "3\n")]
    [InlineData("var s = new S(); WriteLine(s.V); public struct S { public int V; }", "0\n")]
    [InlineData("var s = S.Create(); WriteLine(s.V); class S { public readonly int V; private S() { V = 3; } public static S Create() => new S(); }", "3\n")]
    public Task AcceptedControlsRunTheSameNatively(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);
}
