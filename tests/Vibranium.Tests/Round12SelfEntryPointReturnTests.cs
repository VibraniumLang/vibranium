using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfEntryPointReturnTests
{
    [Theory]
    [InlineData("if (args.Length > 5) return 1; WriteLine(0);", "CS0161")]
    [InlineData("if (args.Length > 5) return; return 1;", "CS0126")]
    [InlineData("if (Early()) return 1; WriteLine(0); static bool Early() => false;", "CS0161")]
    [InlineData("if (Early()) return; return 1; static bool Early() => false;", "CS0126")]
    [InlineData("if (false) return 0;", "CS0161")]
    public Task RejectsInvalidCSharp(string source, string csharpId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("if (Early()) return 1; WriteLine(0); return 0; static bool Early() => false;", "0\n")]
    [InlineData("if (Early()) return; WriteLine(0); static bool Early() => false;", "0\n")]
    [InlineData("WriteLine(F()); static int F() { return 2; }", "2\n")]
    [InlineData("if (Early()) { WriteLine(3); return 0; } else { WriteLine(4); return 0; } static bool Early() => false;", "4\n")]
    public Task AcceptedControlsRunTheSameNatively(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);
}
