using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfAliasTargetTests
{
    [Theory]
    [InlineData("System.ObsoleteAttribute")]
    [InlineData("global::System.ObsoleteAttribute")]
    [InlineData("System.Runtime.CompilerServices.MethodImplAttribute")]
    [InlineData("System.Diagnostics.ConditionalAttribute")]
    [InlineData("System.Runtime.InteropServices.StructLayoutAttribute")]
    [InlineData("System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute")]
    [InlineData("Vibranium.NativeAttribute")]
    [InlineData("System.DayOfWeek")]
    [InlineData("System.Array")]
    [InlineData("global::System.Array")]
    [InlineData("System.Math")]
    [InlineData("System.Collections.Generic.List<int>")]
    [InlineData("System.Environment.SpecialFolder")]
    public async Task UnusedAliasesCannotNameDeniedTypes(string target)
    {
        var result = await Round12RuleWitness.CheckAllAsync($"using Denied = {target}; WriteLine(5);");
        Assert.Equal(new[] { "VIB000" }, result.Self);
    }

    [Theory]
    [InlineData("using static System.IDisposable; WriteLine(5);")]
    [InlineData("using static global::System.IDisposable; WriteLine(5);")]
    [InlineData("using D = System.IDisposable; WriteLine(5); namespace N { using static D; }")]
    [InlineData("WriteLine(5); static void F(global::System.IDisposable d) { }")]
    [InlineData("using D = System.IDisposable; WriteLine(5); static void F(D d) { }")]
    public async Task IDisposableCannotBeUsedOutsideItsAllowedPositions(string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { "VIB000" }, result.Self);
    }

    [Theory]
    [InlineData("using static Vibranium.MutAttribute; WriteLine(5);")]
    [InlineData("using static Vibranium.LifetimeOfAttribute; WriteLine(5);")]
    [InlineData("using static Vibranium.NativeAttribute; WriteLine(5);")]
    [InlineData("using static Vibranium.VecBuilder; WriteLine(5);")]
    [InlineData("using static Vibranium.VibraniumPanicException; WriteLine(5);")]
    [InlineData("using static System.Array; WriteLine(1);")]
    [InlineData("using static global::System.Array; WriteLine(1);")]
    [InlineData("using static Vibranium.Std.Sys; WriteLine(5);")]
    [InlineData("using static global::Vibranium.Std.Sys; WriteLine(5);")]
    [InlineData("using S = Vibranium.Std; WriteLine(5); namespace N { using static S.Sys; }")]
    public async Task RuntimeOnlyTypesCannotBeImportedStatically(string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { "VIB000" }, result.Self);
    }

    [Theory]
    [InlineData("using X = Vibranium.VecBuilder; WriteLine(5);")]
    [InlineData("using X = Vibranium.VibraniumPanicException; WriteLine(5);")]
    [InlineData("using A = System.Array; WriteLine(A.MaxLength > 0 ? 1 : 0);")]
    [InlineData("WriteLine(System.Array.MaxLength > 0 ? 1 : 0);")]
    [InlineData("using X = Vibranium.Std.Sys; WriteLine(5);")]
    [InlineData("using X = Vibranium.MutAttribute; WriteLine(5); static void F(X value) { }")]
    [InlineData("using X = Vibranium.LifetimeOfAttribute; WriteLine(5); static void F(X value) { }")]
    public async Task RuntimeAliasUseHasTheSameTypeRestrictions(string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { "VIB000" }, result.Self);
    }

    [Theory]
    [InlineData("", "using D = System.IDisposable; WriteLine(5);", "5\n")]
    [InlineData("namespace N { using DispAlias = System.IDisposable; public sealed class H : DispAlias { public void Dispose() { WriteLine(7); } } }", "using var h = new N.H();", "7\n")]
    [InlineData("sealed class C { public int Value; [M] public void Set() { Value = 5; } }", "using M = global::Vibranium.MutAttribute; var c = new C(); c.Set(); WriteLine(c.Value);", "5\n")]
    [InlineData("static class C { [return: L(\"x\")] public static ref readonly int F(in int x) => ref x; }", "using L = Vibranium.LifetimeOfAttribute; int x = 5; WriteLine(C.F(in x));", "5\n")]
    [InlineData("", "using V = global::Vibranium; V.Vec<int> xs = [5]; WriteLine(xs.Count);", "1\n")]
    [InlineData("struct S { public int V; [UR] public ref int At() => ref V; }", "using UR = System.Diagnostics.CodeAnalysis.UnscopedRefAttribute; var s = new S(); s.At() = 4; WriteLine(s.V);", "4\n")]
    [InlineData("", "using IntVec = Vibranium.Vec<int>; IntVec xs = [3]; WriteLine(xs[0]);", "3\n")]
    public Task PermittedAliasesRemainAccepted(string declarations, string statements, string stdout) =>
        Round12RuleWitness.AcceptAsync(statements + "\n" + declarations, stdout);
}
