using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfAliasQualifiedAttributeTests
{
    [Theory]
    [InlineData("using Mine = Vibranium; var c = new C(7); c.Add(3); WriteLine(c.Value); sealed class C(int n) { public int Value = n; [Mine::Mut] public void Add(int d) { Value += d; } }")]
    [InlineData("using Mine = Vibranium; var c = new C(7); c.Add(3); WriteLine(c.Value); sealed class C(int n) { public int Value = n; [Mine::MutAttribute] public void Add(int d) { Value += d; } }")]
    [InlineData("using Mine = System.Diagnostics.CodeAnalysis; S s = new S(5); WriteLine(s.ValueRef()); struct S(int n) { public int Value = n; [Mine::UnscopedRef] public ref int ValueRef() => ref Value; }")]
    [InlineData("using Mine = System.Diagnostics.CodeAnalysis; WriteLine(F(out int i)); static ref int F([Mine::UnscopedRefAttribute] out int x) { x = 42; return ref x; }")]
    [InlineData("using Mine = Vibranium; int x = 42; WriteLine(C.F(in x)); static class C { [return: Mine::LifetimeOf(\"x\")] public static ref readonly int F(in int x) => ref x; }")]
    [InlineData("using Mine = Vibranium; int x = 42; WriteLine(F(in x)); [return: Mine::LifetimeOfAttribute(\"x\")] static ref readonly int F(in int x) => ref x;")]
    [InlineData("using Mine = System; WriteLine(42); [Mine::Obsolete] struct C { }")]
    [InlineData("using Mine = System; WriteLine(42); struct C { [Mine::Obsolete] public int Field; }")]
    [InlineData("using CA=System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[CA::UnscopedRef] public ref int Get()=>ref Value;}")]
    [InlineData("using CS=System.Runtime.CompilerServices;WriteLine(F());[CS::MethodImpl(8)]static int F()=>1;")]
    public async Task OrdinaryAliasQualifierIsRejectedInAttributeName(string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { "VIB000" }, result.Stage0);
        Assert.Equal(new[] { "VIB000" }, result.Analyzer);
        Assert.Equal(new[] { "VIB000" }, result.Self);
        Assert.Contains("A user alias followed by '::' is not supported", result.SelfOutput);
    }

    [Theory]
    [InlineData("var c = new C(7); c.Add(3); WriteLine(c.Value); sealed class C(int n) { public int Value = n; [global::Vibranium.Mut] public void Add(int d) { Value += d; } }", "10\n")]
    [InlineData("WriteLine(F(out int i)); static ref int F([global::System.Diagnostics.CodeAnalysis.UnscopedRefAttribute] out int x) { x = 42; return ref x; }", "42\n")]
    [InlineData("int x = 42; WriteLine(F(in x)); [return: global::Vibranium.LifetimeOfAttribute(\"x\")] static ref readonly int F(in int x) => ref x;", "42\n")]
    public Task GlobalQualifierRemainsAllowed(string source, string output) => Round12RuleWitness.AcceptAsync(source, output);
}
