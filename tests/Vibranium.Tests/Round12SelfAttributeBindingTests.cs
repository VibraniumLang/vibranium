using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfAttributeBindingTests
{
    [Theory]
    [InlineData("using CS = System.Runtime.CompilerServices;\nWriteLine(F());\n[CS::MethodImpl(int.MaxValue + 1)]\nstatic int F() => 1;\n", "CS0220")]
    [InlineData("using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(int.MaxValue + 1)]\nstatic int F() => 1;\n", "CS0220")]
    [InlineData("using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(65536)]\nstatic int F() => 1;\n", "CS1503")]
    [InlineData("using CS = System.Runtime.CompilerServices;\nWriteLine(F());\n[CS::MethodImpl(65536)]\nstatic int F() => 1;\n", "CS1503")]
    [InlineData("using CS = System.Runtime.CompilerServices;\nWriteLine(F());\n[CS.MethodImpl(65536)]\nstatic int F() => 1;\n", "CS1503")]
    [InlineData("using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(MethodImplOptions.NoInlining && MethodImplOptions.NoOptimization)]\nstatic int F() => 1;\n", "CS0019")]
    [InlineData("using CS = System.Runtime.CompilerServices;\nWriteLine(F());\n[CS::MethodImplZz(8)]\nstatic int F() => 1;\n", "CS0234")]
    [InlineData("WriteLine(F());\n[System.Runtime.CompilerServices.MethodImplZz(8)]\nstatic int F() => 1;\n", "CS0234")]
    [InlineData("WriteLine(F());\n[Zz(8)]\nstatic int F() => 1;\n", "CS0246")]
    [InlineData("using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(true)]\nstatic int F() => 1;\n", "CS1503")]
    [InlineData("using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(\"text\")]\nstatic int F() => 1;\n", "CS1503")]
    [InlineData("WriteLine(F());\n[System.Obsolete(5)]\nstatic int F() => 1;\n", "CS1503")]
    [InlineData("var s = new S();\nWriteLine(s.Get());\nstruct S { public int Value; [System.Diagnostics.CodeAnalysis.UnscopedRef(1)] public ref int Get() => ref Value; }\n", "CS1729")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F(1)); [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptionsZz.NoOptimization)] static int F(int x) => x + 1;", "CS0103")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(1 + -1)] static int F() => 1;", "CS0121")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(0)] static int F() => 1;", "CS0121")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(noSuchParameter: 8)] static int F() => 1;", "CS1739")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(8, 8)] static int F() => 1;", "CS1729")]
    [InlineData("WriteLine(1); [System.Obsolete(1 + int.MaxValue > 0 ? \"old\" : \"new\")] static int F() => 1;", "CS0220")]
    [InlineData("WriteLine(C.F()); static class C { [System.Obsolete(\"old\", 5)] public static int F() => 1; }", "CS1503")]
    [InlineData("WriteLine(1); [System.Obsolete(DiagnosticId = 1)] static int F() => 1;", "CS0029")]
    [InlineData("WriteLine(1); class C { [global::System.Runtime.CompilerServices.MethodImplZz(8)] public static int F() => 1; }", "CS0234")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(MethodImplOptions.NoInlining, MethodCodeType = MethodCodeType.Missing)] static int F() => 1;", "CS0117")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(int.MaxValue + MethodImplOptions.NoInlining)] static int F() => 1;", "CS0220")]
    public Task RejectsInvalidCSharp(string source, string csharpId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(8)] static int F() => 1;")]
    [InlineData("using M = System.Runtime.CompilerServices.MethodImplAttribute; WriteLine(F()); [M(value: unchecked((short)65544))] static int F() => 2;")]
    [InlineData("using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization, MethodCodeType = MethodCodeType.IL)] static int F() => 4;")]
    [InlineData("using CS = System.Runtime.CompilerServices; WriteLine(C.F()); static class C { [CS.MethodImpl(16 - 8)] public static int F() => 5; }")]
    [InlineData("WriteLine(C.F()); static class C { [System.Obsolete(\"old\", error: false, DiagnosticId = \"OLD\")] public static int G() => 2; public static int F() => 6; }")]
    [InlineData("WriteLine(1); [System.Flags] enum E { A }")]
    [InlineData("WriteLine(1); enum E { [Obsolete] A }")]
    [InlineData("WriteLine(1); sealed class C { [Obsolete] public int P => 1; }")]
    [InlineData("WriteLine(1); sealed class C { public int P { [Obsolete] get => 1; } }")]
    [InlineData("WriteLine(1); [return: Obsolete] static int F() => 1;")]
    [InlineData("WriteLine(1); static int F([Obsolete] int p) => p;")]
    [InlineData("WriteLine(1); sealed class C<[Obsolete] T> { }")]
    [InlineData("WriteLine(1); [System.AttributeUsage(System.AttributeTargets.All)] sealed class C { }")]
    [InlineData("WriteLine(1); [System.Runtime.CompilerServices.CollectionBuilder(typeof(C), \"Build\")] sealed class C { }")]
    [InlineData("WriteLine(1); [System.Diagnostics.CodeAnalysis.SuppressMessage(\"x\", \"y\")] sealed class C { }")]
    [InlineData("[assembly: global::System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")] WriteLine(1);")]
    [InlineData("[assembly: global::System.Reflection.AssemblyVersion(\"1.2.3.4\")] WriteLine(1);")]
    [InlineData("[module: System.Runtime.CompilerServices.SkipLocalsInit] WriteLine(1);")]
    [InlineData("using System.Runtime.InteropServices; WriteLine(4); [StructLayout(LayoutKind.Sequential)] struct C { }")]
    [InlineData("using System.Runtime.InteropServices; WriteLine(4); [StructLayout(LayoutKind.Auto)] struct C { }")]
    [InlineData("using System.Runtime.InteropServices; WriteLine(4); [StructLayout(LayoutKind.Explicit)] struct C { }")]
    [InlineData("WriteLine(4); [System.Diagnostics.CodeAnalysis.DoesNotReturn] static void F() { Panic(\"message\"); }")]
    [InlineData("WriteLine(4); static class C { [Native(\"__vib_sys_close\")] public static int F() => 0; }")]
    public void ClosedListRejectsAttributeApplications(string source)
    {
        // Stage-0 and its Analyzer do not yet implement F15 in the self handoff base.
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-f15-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null,
                Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            string output = self.Out + self.Err;
            Assert.True(self.Exit == 1, output);
            Assert.Equal(new[] { "VIB000" }, Regex.Matches(output, @"error (VIB\d{3}):").Select(m => m.Groups[1].Value).Distinct());
            string message = source.StartsWith("[assembly:", StringComparison.Ordinal) || source.StartsWith("[module:", StringComparison.Ordinal)
                ? "Assembly and module-level attributes are not supported in Vibranium v0.2"
                : "Attribute is not permitted in Vibranium";
            Assert.Contains(message, output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void ConditionalCannotEraseCallsOnlyInManagedExecution() => ClosedListRejectsAttributeApplications("""
        using System.Diagnostics;
        int value = 0;
        F(ref value);
        WriteLine(value);
        [Conditional("DEBUG")] static void F(ref int target) { target = 1; }
        """);

    [Fact]
    public void ModuleInitializerCannotAddManagedOnlySideEffects() => ClosedListRejectsAttributeApplications("""
        using System.Runtime.CompilerServices;
        WriteLine("main");
        static class C { [ModuleInitializer] internal static void Init() { WriteLine("init"); } }
        """);

    [Fact]
    public void OverloadResolutionPriorityCannotSelectADifferentManagedOverload() => ClosedListRejectsAttributeApplications("""
        using System.Runtime.CompilerServices;
        int value = 11;
        WriteLine(Probe.Choose(value));
        static class Probe
        {
            public static int Choose(int value) => 1;
            [OverloadResolutionPriority(1)] public static int Choose(long value) => 2;
        }
        """);

    [Fact]
    public void ExtendedLayoutCannotOverlapFieldsOnlyInManagedExecution() => ClosedListRejectsAttributeApplications("""
        using System.Runtime.InteropServices;
        Probe pair = default;
        pair.First = 49;
        WriteLine(pair.Second);
        [ExtendedLayout(ExtendedLayoutKind.CUnion)] struct Probe
        {
            public int First;
            public int Second;
        }
        """);

    [Theory]
    [InlineData("WriteLine(1); [Mut] struct S { }", "CS0592")]
    [InlineData("WriteLine(1); struct S { [Mut, Vibranium.MutAttribute] public void F() { } }", "CS0579")]
    [InlineData("WriteLine(1); struct S { [Mut(1)] public void F() { } }", "CS1729")]
    [InlineData("WriteLine(1); [System.Diagnostics.CodeAnalysis.DoesNotReturn(1)] static void F() { }", "CS1729")]
    [InlineData("WriteLine(1); [System.Diagnostics.CodeAnalysis.UnscopedRef] static int F() => 1;", "CS9101")]
    [InlineData("WriteLine(1); static int F([System.Diagnostics.CodeAnalysis.UnscopedRef] int n) => n;", "CS9063")]
    [InlineData("WriteLine(1); [return: LifetimeOf(1)] static ref readonly int F(in int value) => ref value;", "CS1503")]
    [InlineData("WriteLine(1); [return: LifetimeOf(missing: \"value\")] static ref readonly int F(in int value) => ref value;", "CS1739")]
    public Task ListedAttributesMustBeValidCSharp(string source, string csharpId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("using U = System.Diagnostics.CodeAnalysis.UnscopedRefAttribute; var s = new S(); s.At() = 4; WriteLine(s.V); struct S { public int V; [U()] public ref int At() => ref V; }", "4\n")]
    [InlineData("WriteLine(Get(out var local)); static ref int Get([System.Diagnostics.CodeAnalysis.UnscopedRef] out int n) { n = 4; return ref n; }", "4\n")]
    [InlineData("int item = 4; WriteLine(F(in item)); [return: LifetimeOf((\"value\"))] static ref readonly int F(in int value) => ref value;", "4\n")]
    [InlineData("int item = 4; WriteLine(C.F(in item)); static class C { private const string Key = \"value\"; [return: LifetimeOf(Key)] public static ref readonly int F(in int value) => ref value; }", "4\n")]
    public Task AcceptedControlsRunTheSameNatively(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);

    [Theory]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public sealed class Mut { } public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } }", "2\n")]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } } public sealed class Mut { }", "2\n")]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public struct Mut { public int X; } public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } }", "2\n")]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public enum Mut { A } public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } }", "2\n")]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } public sealed class Mut { } }", "2\n")]
    [InlineData("int x = 1; int y = 2; ref int r = ref Pick(ref x, ref y); r += 10; WriteLine(x + y); [return: LifetimeOf(\"a\")] static ref int Pick(ref int a, ref int b) { b += 1; return ref a; } public sealed class LifetimeOf { }", "14\n")]
    [InlineData("using System.Diagnostics.CodeAnalysis; var s = new S(); s.Get() = 5; WriteLine(s.X); public struct S { public int X; [UnscopedRef] public ref int Get() => ref X; } public sealed class UnscopedRef { }", "5\n")]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); namespace Mut { public sealed class Z { } } public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } }", "2\n")]
    [InlineData("var c = new C(); c.Inc(); var m = new Mut(); WriteLine(c.V + m.K); public sealed class Mut { public int K = 5; } public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } }", "7\n")]
    public Task NonAttributeTypeOrNamespaceFallsBackToAttributeSuffix(string source, string expectedOutput) =>
        Round12RuleWitness.AcceptAsync(source, expectedOutput);

    [Theory]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public sealed class Mut { } public sealed class C { public int V = 1; [@Mut] public void Inc() { V += 1; } }", "CS0616")]
    [InlineData("var c = new C(); c.Inc(); WriteLine(c.V); public sealed class MutAttribute { } public sealed class C { public int V = 1; [Mut] public void Inc() { V += 1; } }", "CS0616")]
    public Task VerbatimAndSuffixedShadowStillReject(string source, string csharpId) =>
        Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("WriteLine(F()); [] static int F() => 1;", "CS1001")]
    [InlineData("WriteLine((int)E.A); public enum E { [] A = 3 }", "CS1001")]
    [InlineData("WriteLine(F(1)); static int F([] int x) => x;", "CS1001")]
    [InlineData("WriteLine(1); [] public sealed class C { }", "CS1001")]
    [InlineData("WriteLine(F()); [method:] static int F() => 1;", "CS1001")]
    [InlineData("WriteLine(new C().V); public sealed class C { [field:] public int V = 2; }", "CS1001")]
    [InlineData("WriteLine(1); [type:] public sealed class C { }", "CS1001")]
    [InlineData("WriteLine(F()); [return:] static int F() => 1;", "CS1001")]
    public Task EmptyOrTargetOnlyAttributeListsReject(string source, string csharpId) =>
        Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Fact]
    public Task TrailingCommaInAttributeListStaysAccepted() =>
        Round12RuleWitness.AcceptAsync("var c = new C(); c.Inc(); WriteLine(c.V); public sealed class C { public int V; [Mut,] public void Inc() { V += 1; } }", "1\n");
}
