using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfRefExtensionTests
{
    [Theory]
    [InlineData("var s = new S(); s.Bump(); WriteLine(s.X); static class E { public static void Bump(this ref S s) { s.X++; } } class S { public int X; }\n", "CS8337")]
    [InlineData("var s = new S();\nWriteLine(E.Get(ref s));\nstatic class E { public static int Get(this ref S s) => s.X; }\nclass S { public int X = 4; }\n", "CS8337")]
    [InlineData("int x = 1;\nE.Touch(ref x);\nWriteLine(x);\nstatic class E { public static void Touch<T>(this ref T t) { } }\n", "CS8337")]
    [InlineData("string t = \"abc\";\nWriteLine(E.Len(ref t));\nstatic class E { public static int Len(this ref string s) => s.Length; }\n", "CS8337")]
    [InlineData("return 0; static class Ext { static void Touch<T>(this ref T t) where T : class { } }", "CS8337")]
    [InlineData("WriteLine(1); sealed class C {} static class Ext { public static int Get(this in C value) => 5; }", "CS8338")]
    [InlineData("WriteLine(1); static class Ext { public static int Get(this in string value) => 5; }", "CS8338")]
    [InlineData("WriteLine(1); static class Ext { public static int Get<T>(this in T value) => 5; }", "CS8338")]
    [InlineData("WriteLine(1); static class Ext { public static int Get<T>(this in T value) where T : class => 5; }", "CS8338")]
    [InlineData("WriteLine(1); static class Ext { public static int Get<T>(this in T value) where T : struct => 5; }", "CS8338")]
    [InlineData("WriteLine(1); static class Ext { public static int Get(this in System.IDisposable value) => 5; }", "CS8338")]
    [InlineData("WriteLine(1); sealed class C {} static class Ext { public static int Get(this ref readonly C value) => 5; }", "CS8338")]
    public Task RejectsInvalidCSharp(string source, string csharpId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("int value = 1; value.Bump(); WriteLine(value); static class Ext { public static void Bump(this ref int x) { x += 1; } }", "2\n")]
    [InlineData("var value = new S(3); value.Bump(); WriteLine(value.X); struct S { public int X; public S(int x) { X = x; } } static class Ext { public static void Bump(this ref S x) { x.X += 1; } }", "4\n")]
    [InlineData("int value = 7; value.Touch(); WriteLine(value); static class Ext { public static void Touch<T>(this ref T t) where T : struct {} }", "7\n")]
    [InlineData("int value = 7; value.Touch(); WriteLine(value); static class Ext { public static void Touch<T>(this ref T t) where T : unmanaged {} }", "7\n")]
    [InlineData("var value = new S(); WriteLine(value.Get()); class S {} static class Ext { public static int Get(this S s) => 11; }", "11\n")]
    [InlineData("int value = 7; WriteLine(value.Get()); static class Ext { public static int Get(this in int n) => n; }", "7\n")]
    [InlineData("var value = new S(3); WriteLine(value.Get()); struct S { public int X; public S(int x) { X = x; } } static class Ext { public static int Get(this in S x) => x.X; }", "3\n")]
    public Task AcceptedControlsRunTheSameNatively(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);
}
