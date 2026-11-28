using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfX3Tests
{
    [Theory]
    [MemberData(nameof(ValidPrograms))]
    public Task ValidProgramsKeepManagedAndNativeParity(string source, string output) => Round12RuleWitness.AcceptAsync(source, output);

    [Theory]
    [MemberData(nameof(InvalidCSharpPrograms))]
    public Task InvalidCSharpIsRejected(string source, string csharpId, string selfId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId, selfId);

    [Theory]
    [MemberData(nameof(OutsideSubsetPrograms))]
    public Task SubsetDiagnosticSetsMatch(string source, string id) => Round12RuleWitness.RejectAsync(source, id, exact: true);

    [Fact]
    public Task VerbatimReturnTargetKeepsFrontendAndRuntimeParity() => Round12RuleWitness.AcceptAsync(
        """
            Span<int> s = [4];
            WriteLine(K.First(s, s)[0]);

            static class K
            {
                [@return: LifetimeOf("a")]
                public static ReadOnlySpan<int> First(ReadOnlySpan<int> a, ReadOnlySpan<int> b) => a;
            }
            """, "4\n");

    [Theory]
    [InlineData("default", "VIB008")]
    [InlineData("default!", "VIB008")]
    [InlineData("default(string)", "VIB008")]
    [InlineData("default(string[])", "VIB008")]
    [InlineData("(default(string[]))", "VIB008")]
    [InlineData("default(System.String[])", "VIB008")]
    [InlineData("default(int)", "VIB000")]
    [InlineData("null", "VIB008")]
    [InlineData("null!", "VIB008")]
    [InlineData("(string)null", "VIB008")]
    [InlineData("new string[] { \"x\" }", "VIB000")]
    [InlineData("$\"{\"x\"}\"", "VIB000")]
    [InlineData("true ? \"x\" : \"y\"", "VIB000")]
    [InlineData("(string)\"x\"", "VIB000")]
    [InlineData("(string)Key", "VIB000")]
    [InlineData("(string)((\"x\"))", "VIB000")]
    [InlineData("$\"x\"", "VIB000")]
    [InlineData("$\"{Key}\"", "VIB000")]
    [InlineData("new string[] { \"span\" }", "VIB000")]
    [InlineData("new string[] { \"x\", \"y\" }", "VIB000")]
    [InlineData("\"sp\" + \"an\"", "VIB000")]
    public void F15LifetimeArgumentHasExactSelfVerdict(string argument, string expectedId) =>
        Assert.Equal(new[] { expectedId }, CheckLifetimeArgumentInSelf(argument));

    [Fact]
    public void F15LifetimeArgumentNameofSpanReportsExactVIB000() =>
        Assert.Equal(new[] { "VIB000" }, CheckLifetimeSourceInSelf("""
            ReadOnlySpan<int> s = [7, 8, 9];
            WriteLine(K.Head(s)[0]);
            static class K { [return: LifetimeOf(nameof(span))] public static ReadOnlySpan<int> Head(ReadOnlySpan<int> span) => span.Slice(0, 1); }
            """));

    [Theory]
    [InlineData("\"x\"!")]
    [InlineData("Key!")]
    [InlineData("(null)")]
    [InlineData("\"x\" + null")]
    [InlineData("[\"x\", null!]")]
    public void F15LifetimeArgumentWithForbiddenNullSyntaxReportsVIB008(string argument) =>
        Assert.Contains("VIB008", CheckLifetimeArgumentInSelf(argument));

    [Theory]
    [InlineData("((\"x\"))", "")]
    [InlineData("(Key)", "const string Key = \"x\";")]
    public Task F15ParenthesizedStringAndConstantNameKeepRuntimeParity(string argument, string declaration) =>
        Round12RuleWitness.AcceptAsync($$"""
            {{declaration}}
            int a = 1; int b = 2;
            ref int r = ref Pick(ref a, ref b);
            r = 9;
            WriteLine(a);
            [return: LifetimeOf({{argument}})]
            static ref int Pick(ref int x, ref int y) => ref x;
            """, "9\n");

    [Theory]
    [InlineData("WriteLine(5); static class C { [return: LifetimeOf(new string[] { \"x\", null! })] public static ref int F(ref int x) => ref x; }")]
    [InlineData("WriteLine(5); [return: LifetimeOf(new string[] { \"x\", null! })] static ref int F(ref int x) => ref x;")]
    [InlineData("WriteLine(5); sealed class C { int x = 5; public int P { [return: LifetimeOf(new string[] { \"x\", null! })] get => x; } }")]
    public void F15LifetimeArgumentExplicitArrayRetainsNestedNullDiagnostic(string source) =>
        Assert.Equal(new[] { "VIB000", "VIB008" }, CheckLifetimeSourceInSelf(source));

    [Theory]
    [InlineData("WriteLine(5); static class K { const string Key = \"x\"; [return: LifetimeOf(new string[] { \"x\", null! })] public static ref int F(ref int x) { return ref x; } }")] // life-method-ordinary-explicit-array-null-bang
    [InlineData("WriteLine(5); static class K { const string Key = \"x\"; [return: LifetimeOf(new string[] { \"x\", null! })] public static ref int F(ref int x) { if (true) return ref x; return ref x; } }")] // life-method-cut-explicit-array-null-bang
    [InlineData("const string Key = \"x\"; WriteLine(5); [return: LifetimeOf(new string[] { \"x\", null! })] static ref int F(ref int x) { return ref x; }")] // life-local-ordinary-explicit-array-null-bang
    [InlineData("const string Key = \"x\"; WriteLine(5); [return: LifetimeOf(new string[] { \"x\", null! })] static ref int F(ref int x) { if (true) return ref x; return ref x; }")] // life-local-cut-explicit-array-null-bang
    [InlineData("WriteLine(5); sealed class K { const string Key = \"this\"; int x = 5; public int P { [return: LifetimeOf(new string[] { \"x\", null! })] get { return x; } } }")] // life-getter-ordinary-explicit-array-null-bang
    [InlineData("WriteLine(5); sealed class K { const string Key = \"this\"; int x = 5; public int P { [return: LifetimeOf(new string[] { \"x\", null! })] get { if (true) return x; return x; } } }")] // life-getter-cut-explicit-array-null-bang
    [InlineData("WriteLine(5); static class K { const string Key = \"x\"; [return: LifetimeOf(\"x\" + default(string))] public static ref int F(ref int x) { if (true) return ref x; return ref x; } }")] // life-method-cut-concat-default
    [InlineData("WriteLine(5); static class K { const string Key = \"x\"; [return: LifetimeOf(\"x\" + default(string))] public static ref int F(ref int x) { return ref x; } }")] // life-method-cut-concat-default-nocut
    [InlineData("const string Key = \"x\"; WriteLine(5); [return: LifetimeOf(\"x\" + default(string))] static ref int F(ref int x) { if (true) return ref x; return ref x; }")] // life-local-cut-concat-default
    [InlineData("const string Key = \"x\"; WriteLine(5); [return: LifetimeOf(\"x\" + default(string))] static ref int F(ref int x) { return ref x; }")] // life-local-cut-concat-default-nocut
    [InlineData("WriteLine(5); sealed class K { const string Key = \"this\"; int x = 5; public int P { [return: LifetimeOf(\"x\" + default(string))] get { if (true) return x; return x; } } }")] // life-getter-cut-concat-default
    [InlineData("WriteLine(5); sealed class K { const string Key = \"this\"; int x = 5; public int P { [return: LifetimeOf(\"x\" + default(string))] get { return x; } } }")] // life-getter-cut-concat-default-nocut
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { null })] public static ref int F(ref int x) { if (true) return ref x; return ref x; } }")] // cut-array-null
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { null })] public static ref int F(ref int x) { return ref x; } }")] // cut-array-null-nocut
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { null })] public static ref int F(ref int x) => ref x; }")] // ordinary-array-null
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { default(string) })] public static ref int F(ref int x) { if (true) return ref x; return ref x; } }")] // cut-array-default
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { default(string) })] public static ref int F(ref int x) { return ref x; } }")] // cut-array-default-nocut
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { default(string) })] public static ref int F(ref int x) => ref x; }")] // ordinary-array-default
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { \"x\"! })] public static ref int F(ref int x) { if (true) return ref x; return ref x; } }")] // cut-array-bang
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { \"x\"! })] public static ref int F(ref int x) { return ref x; } }")] // cut-array-bang-nocut
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(new string[] { \"x\"! })] public static ref int F(ref int x) => ref x; }")] // ordinary-array-bang
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(\"x\" + default(string))] public static ref int F(ref int x) { if (true) return ref x; return ref x; } }")] // cut-concat-default
    [InlineData("WriteLine(5); static class K { [return: LifetimeOf(\"x\" + default(string))] public static ref int F(ref int x) { return ref x; } }")] // cut-concat-default-nocut
    [InlineData("using S = string; WriteLine(5); static class K { [return: LifetimeOf(new string[] { default(S) })] public static ref int F(ref int x) => ref x; }")] // d06-lt-array-default-string-alias
    [InlineData("using S = System.String; WriteLine(5); static class K { [return: LifetimeOf(new string[] { default(S) })] public static ref int F(ref int x) => ref x; }")] // d26-lt-array-of-default-str-array
    [InlineData("using S = string; WriteLine(5); static class K { [return: LifetimeOf(\"a\" + default(S))] public static ref int F(ref int x) => ref x; }")] // a16-lt-concat-default-string-alias
    [InlineData("using S = System.String; WriteLine(5); static class K { [return: LifetimeOf(\"a\" + default(S))] public static ref int F(ref int x) => ref x; }")] // d07-lt-concat-default-sys-string-alias
    public void F15LifetimeArgumentNestedNullFormsRetainVIB000AndVIB008(string source) =>
        Assert.Equal(new[] { "VIB000", "VIB008" }, CheckLifetimeSourceInSelf(source));

    [Theory]
    [InlineData("using A = string[]; WriteLine(5); static class K { [return: LifetimeOf(default(A))] public static ref int F(ref int x) => ref x; }", "VIB008")]
    [InlineData("using I = int; WriteLine(5); static class K { [return: LifetimeOf(default(I))] public static ref int F(ref int x) => ref x; }", "VIB000")]
    public void F15LifetimeArgumentAliasedDefaultRespectsReferenceTypeNullness(string source, string expectedId) =>
        Assert.Equal(new[] { expectedId }, CheckLifetimeSourceInSelf(source));

    [Theory]
    [InlineData("S s = new S(); WriteLine(s.Val); struct S { int _v; public int Val { get => _v; [return: LifetimeOf(default)] init => _v = value; } }")] // a47-lt-init-lifetimeof-default
    [InlineData("S s = new S(); WriteLine(s.Val); struct S { int _v; public int Val { get => _v; init { string t = null; _v = value; } } }")] // a48-lt-init-body-null
    [InlineData("S s = new S(); WriteLine(s[0]); struct S { int _v; public int this[int i] { get => _v; init { string t = null; _v = value; } } }")] // indexer-init-body-null
    public async Task InitAccessorWithPhase2NullStillReportsVIB000AndSharesStage0Id(string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Contains("VIB000", result.Self);
        Assert.Contains("VIB008", result.Self);
        Assert.Contains("VIB000", result.Stage0);
        Assert.Contains("VIB000", result.Analyzer);
    }

    [Theory]
    [InlineData("WriteLine(5); static class C { public static ref int F([LifetimeOf(default)] ref int x) => ref x; }")]
    [InlineData("WriteLine(5); sealed class C { private static int x; public ref int P { [return: LifetimeOf(default)] get => ref x; } }")]
    [InlineData("WriteLine(5); sealed class Owner { public int Value = 5; } static class C { [return: LifetimeOf(default)] public static ref int Escape(ref int x, ref int y) { var local = new Owner(); return ref local.Value; } }")]
    public void F15LifetimeArgumentBareDefaultReportsVIB008DespiteOtherErrors(string source) =>
        Assert.Contains("VIB008", CheckLifetimeSourceInSelf(source));

    private static string[] CheckLifetimeArgumentInSelf(string argument) => CheckLifetimeSourceInSelf($$"""
        int a = 1; int b = 2;
        ref int r = ref C.Pick(ref a, ref b);
        r = 9;
        WriteLine(a);
        static class C
        {
            private const string Key = "x";
            [return: LifetimeOf({{argument}})]
            public static ref int Pick(ref int x, ref int y) => ref x;
        }
        """);

    private static string[] CheckLifetimeSourceInSelf(string source)
    {
        // The paired Roslyn gate and analyzer do not yet implement all of these F15 verdicts.
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-self-lifetime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            var self = Round12RuleWitness.Run(Round11AuditAssertions.DotnetExe, null,
                Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            return Regex.Matches(self.Out + self.Err, @"error ((?:VIB|CS)\d+):")
                .Select(match => match.Groups[1].Value).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static IEnumerable<object[]> ValidPrograms()
    {
        // Type-gate review F3: the constant belongs to the enclosing top-level block.
        yield return ["""
            const string Key = "x";

            int a = 10; int b = 20;
            ref int r = ref Pick(ref a, ref b);
            r += 5;
            WriteLine(a);
            [return: LifetimeOf(Key)]
            static ref int Pick(ref int x, ref int y) => ref x;
            """, "15\n"];
        // F4a-lifetimeof-static-local-const-local
        yield return ["""
            Span<int> buf = [42];
            WriteLine(Run(buf));

            static int Run(ReadOnlySpan<int> input)
            {
                const string Key = "s";
                [return: LifetimeOf(Key)]
                static ReadOnlySpan<int> First(ReadOnlySpan<int> s, ReadOnlySpan<int> other) => s;
                return First(input, input)[0];
            }
            """, "42\n"];
        // F4b-lifetimeof-static-local-const-owner
        yield return ["""
            Span<int> buf = [42];
            WriteLine(C.Run(buf));

            static class C
            {
                private const string Key = "s";

                public static int Run(ReadOnlySpan<int> input)
                {
                    [return: LifetimeOf(Key)]
                    static ReadOnlySpan<int> First(ReadOnlySpan<int> s, ReadOnlySpan<int> other) => s;
                    return First(input, input)[0];
                }
            }
            """, "42\n"];
        // F4c-lifetimeof-static-local-const-qual-priv
        yield return ["""
            Span<int> buf = [42];
            WriteLine(C.Run(buf));

            static class C
            {
                private const string Key = "s";

                public static int Run(ReadOnlySpan<int> input)
                {
                    [return: LifetimeOf(C.Key)]
                    static ReadOnlySpan<int> First(ReadOnlySpan<int> s, ReadOnlySpan<int> other) => s;
                    return First(input, input)[0];
                }
            }
            """, "42\n"];
        // F5a-pctor-shadow-guard-isnone-span
        yield return ["""
            Span<int> buf = [1, 2, 3];
            var h = new Holder(buf);
            WriteLine(h.Read(Some(4)));

            struct Holder(Span<int> p)
            {
                public int Len = p.Length;

                public int Read(Option<int> o)
                {
                    if (o.IsNone(out var p))
                    {
                        return Len;
                    }
                    return p + Len;
                }
            }
            """, "7\n"];
        // F5b-pctor-shadow-if-else-isnone-span
        yield return ["""
            Span<int> buf = [1, 2, 3];
            var h = new Holder(buf);
            WriteLine(h.Read(Some(4)));

            struct Holder(Span<int> p)
            {
                public int Len = p.Length;

                public int Read(Option<int> o)
                {
                    if (o.IsNone(out var p))
                    {
                        return Len;
                    }
                    else
                    {
                        return p + Len;
                    }
                }
            }
            """, "7\n"];
        // F5c-pctor-shadow-guard-isnone-vec
        yield return ["""
            var v = new Vec<int>();
            v.Add(10);
            var h = new Holder(v);
            WriteLine(h.Read(Some(5)));

            struct Holder(Vec<int> p)
            {
                public Vec<int> Kept = p;

                public int Read(Option<int> o)
                {
                    if (o.IsNone(out var p))
                    {
                        return Kept.Count;
                    }
                    return p + Kept.Count;
                }
            }
            """, "6\n"];
        // F5d-pctor-shadow-if-else-isnone-vec
        yield return ["""
            var v = new Vec<int>();
            v.Add(10);
            var h = new Holder(v);
            WriteLine(h.Read(Some(5)));

            struct Holder(Vec<int> p)
            {
                public Vec<int> Kept = p;

                public int Read(Option<int> o)
                {
                    if (o.IsNone(out var p))
                    {
                        return Kept.Count;
                    }
                    else
                    {
                        return p + Kept.Count;
                    }
                }
            }
            """, "6\n"];
        // F7a-local-const-bool-enum-gt-float0
        yield return ["""
            WriteLine(Run());

            static int Pick(bool b) => b ? 11 : 22;

            static int Run()
            {
                const bool B = E.A > 0f;
                return Pick(B);
            }

            enum E { A = 1 }
            """, "11\n"];
        // F7b-local-const-bool-float0-eq-enum
        yield return ["""
            WriteLine(Run());

            static int Pick(bool b) => b ? 11 : 22;

            static int Run()
            {
                const bool B = 0.0 == E.Zero;
                return Pick(B);
            }

            enum E { Zero = 0 }
            """, "11\n"];
        // life-local-const
        yield return ["""
            int value=9; const string Key="x"; WriteLine(F(in value)); [return: LifetimeOf(Key)] static ref readonly int F(in int x) => ref x;
            """, "9\n"];
        // life-local-outerconst
        yield return ["""
            int value=9; const string Outer="x"; WriteLine(F(in value)); [return: LifetimeOf(Outer)] static ref readonly int F(in int x) => ref x;
            """, "9\n"];
        // alias-local-and-qualified-constant
        yield return ["""
            using L = Vibranium.LifetimeOfAttribute;
            WriteLine(C.Run());
            static class C
            {
                private const string Key = "y";
                public static int Run()
                {
                    int x = 21; int y = 42;
                    const string Local = (C.Key);
                    [return: @L((Local))] static ref readonly int Select(in int x, in int y) => ref y;
                    return Select(in x, in y);
                }
            }
            """, "42\n"];
        // verbatim-parameter-target
        yield return ["""
            using U = System.Diagnostics.CodeAnalysis.UnscopedRefAttribute;
            WriteLine(F(out int i));
            static ref int F([@param: @U] out int x) { x = 42; return ref x; }
            """, "42\n"];
        // verbatim-exact-name
        yield return ["""
            int x = 42;
            WriteLine(F(in x));
            [return: global::Vibranium.@LifetimeOfAttribute("x")] static ref readonly int F(in int x) => ref x;
            """, "42\n"];
        // underlying-cast-valid
        yield return ["""
            WriteLine((int)E.B); enum E : int { A = 1, B = unchecked((int)(E)2) + A }
            """, "3\n"];
        // other-enum-member-valid
        yield return ["""
            WriteLine((int)E.A); enum E { A = F.B + 1 } enum F { B = 1 }
            """, "2\n"];
        // enum-zero-0-==
        yield return ["""
            const bool test = (E.Zero) == (0f);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-1-==
        yield return ["""
            const bool test = (0.0) == (E.Zero);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-0-!=
        yield return ["""
            const bool test = (E.Pos) != (0f);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-1-!=
        yield return ["""
            const bool test = (0.0) != (E.Pos);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-0-<
        yield return ["""
            const bool test = (E.Neg) < (0f);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-1-<
        yield return ["""
            const bool test = (0.0) > (E.Neg);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-0->
        yield return ["""
            const bool test = (E.Pos) > (0f);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-1->
        yield return ["""
            const bool test = (0.0) < (E.Pos);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-0-<=
        yield return ["""
            const bool test = (E.Zero) <= (0f);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-1-<=
        yield return ["""
            const bool test = (0.0) >= (E.Zero);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "True\n"];
        // enum-zero-0->=
        yield return ["""
            const bool test = (E.Neg) >= (0f);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "False\n"];
        // enum-zero-1->=
        yield return ["""
            const bool test = (0.0) <= (E.Neg);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "False\n"];
        // x3b-top-using-static (Opus P1-2)
        yield return ["""
            using static C;
            int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; WriteLine(a);
            [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x;
            static class C { public const string K = "x"; }
            """, "9\n"];
        // x3b-a34-top-using-static (Neon F2)
        yield return ["""
            using static K;
            int x = 7, y = 8;
            WriteLine(Pick(ref x, ref y));
            [return: LifetimeOf(Key)]
            static ref int Pick(ref int a, ref int b) => ref a;
            static class K { public const string Key = "a"; }
            """, "7\n"];
        // x3b-class-using-static (control)
        yield return ["""
            using static D;
            int a = 1; int b = 2; ref int r = ref C.P(ref a, ref b); r = 9; WriteLine(a);
            static class C { [return: LifetimeOf(K)] public static ref int P(ref int x, ref int y) => ref x; }
            static class D { public const string K = "x"; }
            """, "9\n"];
        // x3b-class-localfn-using-static (control)
        yield return ["""
            using static D;
            WriteLine(C.Run());
            static class C { public static int Run() { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } }
            static class D { public const string K = "x"; }
            """, "9\n"];
        // x3b-m-shadow-class (control: const local before use shadows class const)
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "y"; public static int Run() { const string K = "x"; int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } }
            """, "9\n"];
        // x3b-m-sibling-block (control: sibling block does not shadow class const)
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; { const string K = "y"; WriteLine(K); } { ref int r = ref P(ref a, ref b); r = 9; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } return a; } }
            """, "y\n9\n"];
        // x3b-own-param-not-shadowing (control: local fn own param does not shadow outer const)
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "K"; public static int Run() { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a; [return: LifetimeOf(K)] static ref int P(ref int K, ref int y) => ref K; } }
            """, "9\n"];
        // x3b-outer-const-chain-not-shadowed-by-inner-localfn-param
        yield return ["""
            WriteLine(C.Run());
            static class C
            {
                const string A = "x";
                public static int Run()
                {
                    const string K = A;
                    int a = 1; int b = 2;
                    return Outer(0, ref a, ref b);
                    static int Outer(int A, ref int p, ref int q)
                    {
                        ref int r = ref P(ref p, ref q);
                        r = 9;
                        return p + A;
                        [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x;
                    }
                }
            }
            """, "9\n"];
        // x3c-opus-enum-in-pctor-class (Opus R2-1 witness 1)
        yield return ["""
            WriteLine((int)C.E.B + new C(5).V);
            sealed class C(int A) { const int A = 1; public int V = A; public enum E { X = 1, B = A + 1 } }
            """, "7\n"];
        // x3c-opus-pctor-param-and-const (Opus R2-1 witness 2)
        yield return ["""
            WriteLine(new C(4).V); int a = 1; int b = 2; ref int r = ref C.P(ref a, ref b); r = 9; WriteLine(a);
            sealed class C(int K) { const string K = "x"; public int V = K; [return: LifetimeOf(K)] public static ref int P(ref int x, ref int y) => ref x; }
            """, "4\n9\n"];
        // x3c-opus-struct-member (Opus R2-1 witness 3)
        yield return ["""
            int a = 1; int b = 2; ref int r = ref S.P(ref a, ref b); r = 9; WriteLine(a + new S(2).V);
            struct S(int K) { const string K = "x"; public int V = K; [return: LifetimeOf(K)] public static ref int P(ref int x, ref int y) => ref x; }
            """, "11\n"];
        // x3c-opus-struct-member-localfn (Opus R2-1 witness 4)
        yield return ["""
            WriteLine(S.Run() + new S(2).V);
            struct S(int K) { const string K = "x"; public int V = K; public static int Run() { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } }
            """, "11\n"];
        // x3c-opus-nested-outer-member (Opus R2-1 witness 5)
        yield return ["""
            int a = 1; int b = 2; ref int r = ref O.I.P(ref a, ref b); r = 9; WriteLine(a + new O(2).V);
            sealed class O(int K) { const string K = "x"; public int V = K; public static class I { [return: LifetimeOf(K)] public static ref int P(ref int x, ref int y) => ref x; } }
            """, "11\n"];
        // x3c-opus-case-label (control: const member wins in switch case)
        yield return ["""
            WriteLine(C.F(3) + new C(5).V);
            sealed class C(int K) { const int K = 3; public int V = K; public static int F(int x) { switch (x) { case K: return 1; default: return 2; } } }
            """, "6\n"];
        // x3c-opus-const-local (control: const member wins in local const)
        yield return ["""
            WriteLine(C.F() + new C(5).V);
            sealed class C(int K) { const int K = 3; public int V = K; public static int F() { const int L = K + 1; return L; } }
            """, "9\n"];
        // x3c-sibling-nested-const-in-pctor-outer (outer const member wins in nested type const initializer)
        yield return ["""
            WriteLine(O.I.J + new O(5).V);
            sealed class O(int K) { const int K = 3; public int V = K; public sealed class I { public const int J = K + 1; } }
            """, "9\n"];
        // x3c-sibling-inst-method-lifetimeof (const member wins in instance method attribute)
        yield return ["""
            var c = new C(4); int a = 1; int b = 2; ref int r = ref c.P(ref a, ref b); r = 9; WriteLine(a + c.V);
            sealed class C(int K) { const string K = "x"; public int V = K; [return: LifetimeOf(K)] public ref int P(ref int x, ref int y) => ref x; }
            """, "13\n"];
    }

    public static IEnumerable<object[]> InvalidCSharpPrograms()
    {
        // F2a-verbatim-at-unscopedref
        yield return ["""
            using System.Diagnostics.CodeAnalysis;
            var b = new Box(9);
            ref int r = ref b.Ref();
            WriteLine(r);

            struct Box
            {
                public int Value;
                public Box(int v) { Value = v; }

                [@UnscopedRef]
                public ref int Ref() => ref Value;
            }
            """, "CS0246", "VIB000"];
        // F2b-verbatim-at-mut
        yield return ["""
            var c = new Counter(1);
            c.Inc();
            WriteLine(c.Value);

            struct Counter
            {
                public int Value;
                public Counter(int v) { Value = v; }

                [@Mut]
                public void Inc()
                {
                    Value += 1;
                }
            }
            """, "CS0246", "VIB000"];
        // F2c-verbatim-at-lifetimeof
        yield return ["""
            Span<int> s = [4];
            WriteLine(K.First(s, s)[0]);

            static class K
            {
                [return: @LifetimeOf("a")]
                public static ReadOnlySpan<int> First(ReadOnlySpan<int> a, ReadOnlySpan<int> b) => a;
            }
            """, "CS0246", "VIB000"];
        // F3a-lifetimeof-trailing-comma
        yield return ["""
            Span<int> buf = [7, 8];
            WriteLine(C.Id(buf)[0]);

            static class C
            {
                [return: LifetimeOf("value",)]
                public static ReadOnlySpan<int> Id(ReadOnlySpan<int> value) => value;
            }
            """, "CS1525", "VIB000"];
        // F3b-lifetimeof-set-accessor-badarg
        yield return ["""
            S s = default;
            s.P = 9;
            WriteLine(s.P);

            struct S
            {
                private int _v;
                public int P
                {
                    get => _v;
                    [return: LifetimeOf(123)]
                    set => _v = value;
                }
            }
            """, "CS1503", "VIB000"];
        // F6-enum-init-self-cast
        yield return ["""
            WriteLine((int)E.A);

            enum E
            {
                A = (E)5
            }
            """, "CS0266", "VIB000"];
        // F1a-unscopedref-out-escape-struct
        yield return ["""
            using System.Diagnostics.CodeAnalysis;
            S s = new S(77);
            ref int r = ref s.X;
            {
                int narrow;
                r = ref s.Pick(out narrow);
            }
            WriteLine(r);

            struct S
            {
                public int X;
                public S(int x) { X = x; }

                [UnscopedRef]
                public ref int Pick([UnscopedRef] out int slot)
                {
                    slot = 0;
                    return ref X;
                }
            }
            """, "CS8374", "VIB007"];
        // F1b-unscopedref-out-escape-lifetimeof-ref
        yield return ["""
            using System.Diagnostics.CodeAnalysis;
            int keep = 10;
            ref int r = ref keep;
            {
                int narrow;
                r = ref K.Pick(ref keep, out narrow);
            }
            WriteLine(r);

            static class K
            {
                [return: LifetimeOf("a")]
                public static ref int Pick(ref int a, [UnscopedRef] out int slot)
                {
                    slot = 0;
                    return ref a;
                }
            }
            """, "CS8374", "VIB007"];
        // F1c-unscopedref-out-escape-lifetimeof-span
        yield return ["""
            using System.Diagnostics.CodeAnalysis;
            Span<int> top = [55];
            ReadOnlySpan<int> esc = top;
            {
                int narrow;
                esc = K.Pick(top, out narrow);
            }
            WriteLine(esc[0]);

            static class K
            {
                [return: LifetimeOf("a")]
                public static ReadOnlySpan<int> Pick(ReadOnlySpan<int> a, [UnscopedRef] out int slot)
                {
                    slot = 0;
                    return a;
                }
            }
            """, "CS8352", "VIB007"];
        // out-unscoped-escape-via-ltof
        yield return ["""
            using System.Diagnostics.CodeAnalysis;
            WriteLine(C.Bad());
            static class C { [return: LifetimeOf("y")] static ref int F([UnscopedRef] out int x, ref int y) { x = 1; return ref y; } static ref int Leak(ref int k) { return ref F(out int t, ref k); } public static int Bad() { int q = 3; return Leak(ref q); } }
            """, "CS8168", "VIB007"];
        // init-cast-plus
        yield return ["""
            WriteLine((int)F.Y);
            enum F { X = 3, Y = (F)2 + 1 }
            """, "CS0266", "VIB000"];
        // qualified-verbatim-no-suffix
        yield return ["""
            int x = 42;
            WriteLine(F(in x));
            [return: global::Vibranium.@LifetimeOf("x")] static ref readonly int F(in int x) => ref x;
            """, "CS0234", "VIB000"];
        // init-invalid-argument
        yield return ["""
            WriteLine(42);
            sealed class C { private int v = 1; public int V { get => v; [return: LifetimeOf(123)] init => v = value; } }
            """, "CS1503", "VIB000"];
        // getter-invalid-argument
        yield return ["""
            WriteLine(new C().V);
            sealed class C { public int V { [return: LifetimeOf(123)] get => 42; } }
            """, "CS1503", "VIB000"];
        // other-enum-cast-invalid
        yield return ["""
            WriteLine((int)E.A); enum E { A = checked((F)1) } enum F { B = 1 }
            """, "CS0266", "VIB000"];
        // nested-real-capture
        yield return ["""
            Span<int> a = [4];
            WriteLine(new S(a).Get());
            struct S(Span<int> p)
            {
                public int Get()
                {
                    { if (Some(1).IsNone(out var p)) return 0; WriteLine(p); }
                    return p[0];
                }
            }
            """, "CS9110", "VIB000"];
        // x3b-opus-m-local-after-class
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; const string K = "y"; } }
            """, "CS0844", "VIB000"];
        // x3b-opus-m-nested-block-after-2
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; { ref int r = ref P(ref a, ref b); r = 9; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } const string K = "y"; return a; } }
            """, "CS0844", "VIB000"];
        // x3b-opus-m-for-var
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; for (int K = 0; K < 1; K++) { ref int r = ref P(ref a, ref b); r = 9; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } return a; } }
            """, "CS1503", "VIB000"];
        // x3b-opus-foreach-var
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; Span<int> s = [1]; foreach (var K in s) { ref int r = ref P(ref a, ref b); r = 9; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } return a; } }
            """, "CS1503", "VIB000"];
        // x3b-opus-m-out-var
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; bool ok = int.TryParse("5", out int K); ref int r = ref P(ref a, ref b); r = 9; return a + K - 5; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } }
            """, "CS1503", "VIB000"];
        // x3b-opus-m-param-named-K
        yield return ["""
            WriteLine(C.Run(0));
            static class C { const string K = "x"; public static int Run(int K) { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a + K; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } }
            """, "CS1503", "VIB000"];
        // x3b-neon-d01-str-param
        yield return ["""
            int x = 7, y = 8;
            WriteLine(C.Run("b", ref x, ref y));
            static class C
            {
                const string Key = "a";
                public static int Run(string Key, ref int p, ref int q)
                {
                    return Pick(ref p, ref q);
                    [return: LifetimeOf(Key)]
                    static ref int Pick(ref int a, ref int b) => ref a;
                }
            }
            """, "CS0182", "VIB000"];
        // x3b-neon-a38-int-param
        yield return ["""
            int x = 7, y = 8;
            WriteLine(C.Run(1, ref x, ref y));
            static class C
            {
                const string Key = "a";
                public static int Run(int Key, ref int p, ref int q)
                {
                    return Pick(ref p, ref q);
                    [return: LifetimeOf(Key)]
                    static ref int Pick(ref int a, ref int b) => ref a;
                }
            }
            """, "CS1503", "VIB000"];
        // x3b-neon-d02-outer-localfn-param
        yield return ["""
            const string Key = "a";
            int x = 7, y = 8;
            WriteLine(Outer("b", ref x, ref y));
            static int Outer(string Key, ref int p, ref int q)
            {
                return Pick(ref p, ref q);
                [return: LifetimeOf(Key)]
                static ref int Pick(ref int a, ref int b) => ref a;
            }
            """, "CS0182", "VIB000"];
        // x3b-neon-d04-foreach-shadow
        yield return ["""
            int x = 7, y = 8;
            ReadOnlySpan<int> s = [1];
            WriteLine(C.Run(s, ref x, ref y));
            static class C
            {
                const string Key = "a";
                public static int Run(ReadOnlySpan<int> s, ref int p, ref int q)
                {
                    foreach (int Key in s)
                    {
                        return Pick(ref p, ref q) + Key;
                        [return: LifetimeOf(Key)]
                        static ref int Pick(ref int a, ref int b) => ref a;
                    }
                    return 0;
                }
            }
            """, "CS1503", "VIB000"];
        // x3b-neon-d21-for-header-shadow
        yield return ["""
            int x = 7, y = 8;
            WriteLine(C.Run(ref x, ref y));
            static class C
            {
                const string Key = "a";
                public static int Run(ref int p, ref int q)
                {
                    for (int Key = 0; Key < 1; Key++)
                    {
                        return Pick(ref p, ref q) + Key;
                        [return: LifetimeOf(Key)]
                        static ref int Pick(ref int a, ref int b) => ref a;
                    }
                    return 0;
                }
            }
            """, "CS1503", "VIB000"];
        // x3b-sibling-out-var-after-localfn
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; bool ok = int.TryParse("5", out int K); return a + K - 5; } }
            """, "CS0844", "VIB000"];
        // x3b-sibling-top-after-using-static
        yield return ["""
            using static C;
            int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; WriteLine(a);
            [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x;
            const string K = "y";
            static class C { public const string K = "x"; }
            """, "CS0841", "VIB000"];
        // x3b-sibling-primary-ctor-param-shadows-outer-const
        yield return ["""
            var s = new C.S(1);
            WriteLine(s.Run());
            static class C { const string K = "x"; public struct S(int K) { public int Run() { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a + K - 1; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } } }
            """, "CS1503", "VIB000"];
        // x3b-sibling-setter-value-shadows-const
        yield return ["""
            var s = new S();
            s.Val = 1;
            WriteLine(s.Val);
            struct S { const string value = "x"; int _v; public int Val { get => _v; [Mut] set { int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; _v = a + value; [return: LifetimeOf(value)] static ref int P(ref int x, ref int y) => ref x; } } }
            """, "CS1503", "VIB000"];
        // x3b-sibling-self-const-cycle
        yield return ["""
            WriteLine(C.Run());
            static class C { const string K = "x"; public static int Run() { const string K = K; int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; return a; [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x; } }
            """, "CS0110", "VIB000"];
        // x3b-sibling-top-using-static-private-const
        yield return ["""
            using static C;
            int a = 1; int b = 2; ref int r = ref P(ref a, ref b); r = 9; WriteLine(a);
            [return: LifetimeOf(K)] static ref int P(ref int x, ref int y) => ref x;
            static class C { private const string K = "x"; }
            """, "CS0122", "VIB000"];
        // x3c-opus-const-field-init (primary-ctor param wins in own const initializer)
        yield return ["""
            WriteLine(C.J + new C(5).V);
            sealed class C(int K) { const int K = 3; public const int J = K + 1; public int V = K; }
            """, "CS9105", "VIB000"];
        // x3c-opus-pctor-param-outer-const (primary-ctor param shadows outer type const when own type has no member)
        yield return ["""
            int a = 1; int b = 2; ref int r = ref O.I.P(ref a, ref b); r = 9; WriteLine(a + new O.I(3).V);
            static class O { const string K = "x"; public sealed class I(int K) { public int V = K; [return: LifetimeOf(K)] public static ref int P(ref int x, ref int y) => ref x; } }
            """, "CS9105", "VIB000"];
        // x3c-sibling-nested-const-outer-pctor-no-member (enclosing primary-ctor param shadows outer const when enclosing type has no member)
        yield return ["""
            WriteLine(Outer.O.I.J + new Outer.O(5).V);
            static class Outer { const int K = 3; public sealed class O(int K) { public int V = K; public sealed class I { public const int J = K + 1; } } }
            """, "CS9105", "VIB000"];
        // x3c-neon-e04-const-shadowed-by-prop (property hides imported static const)
        yield return ["""
            using static K;
            int x = 7, y = 8;
            WriteLine(C.Pick(ref x, ref y));
            static class K { public const string Key = "a"; }
            static class C
            {
                public static int Key => 1;
                [return: LifetimeOf(Key)]
                public static ref int Pick(ref int a, ref int b) => ref a;
            }
            """, "CS1503", "VIB000"];
        // x3c-neon-e05-const-shadowed-by-method (method hides imported static const)
        yield return ["""
            using static K;
            int x = 7, y = 8;
            WriteLine(C.Pick(ref x, ref y));
            static class K { public const string Key = "a"; }
            static class C
            {
                public static int Key() => 1;
                [return: LifetimeOf(Key)]
                public static ref int Pick(ref int a, ref int b) => ref a;
            }
            """, "CS1503", "VIB000"];
        // x3c-neon-e06-const-shadowed-by-outer-prop (inner property hides outer type const)
        yield return ["""
            int x = 7, y = 8;
            WriteLine(Outer.Inner.Pick(ref x, ref y));
            static class Outer
            {
                const string Key = "a";
                public static class Inner
                {
                    public static string Key => "b";
                    [return: LifetimeOf(Key)]
                    public static ref int Pick(ref int a, ref int b) => ref a;
                }
            }
            """, "CS0182", "VIB000"];
    }

    public static IEnumerable<object[]> OutsideSubsetPrograms()
    {
        // lt-nameof
        yield return ["""
            int a = 1; int b = 2;
            ref int r = ref C.Pick(ref a, ref b);
            r = 9;
            WriteLine(a);
            static class C { [return: LifetimeOf(nameof(x))] public static ref int Pick(ref int x, ref int y) => ref x; }
            """, "VIB000"];
        // lt-concat
        yield return ["""
            int a = 1; int b = 2;
            ref int r = ref C.Pick(ref a, ref b);
            r = 9;
            WriteLine(a);
            static class C { [return: LifetimeOf("" + "x")] public static ref int Pick(ref int x, ref int y) => ref x; }
            """, "VIB000"];
        // non-static-remains-outside-subset
        yield return ["""
            int x = 42;
            const string Key = "x";
            WriteLine(F(in x));
            [return: LifetimeOf(Key)] ref readonly int F(in int x) => ref x;
            """, "VIB000"];
        // enum-zero-2-==
        yield return ["""
            const bool test = (E.Zero) == (0m);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "VIB000"];
        // enum-zero-2-!=
        yield return ["""
            const bool test = (E.Pos) != (0m);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "VIB000"];
        // enum-zero-2-<
        yield return ["""
            const bool test = (E.Neg) < (0m);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "VIB000"];
        // enum-zero-2->
        yield return ["""
            const bool test = (E.Pos) > (0m);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "VIB000"];
        // enum-zero-2-<=
        yield return ["""
            const bool test = (E.Zero) <= (0m);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "VIB000"];
        // enum-zero-2->=
        yield return ["""
            const bool test = (E.Neg) >= (0m);
            WriteLine(test);
            enum E { Neg = -1, Zero = 0, Pos = int.MaxValue }
            """, "VIB000"];
    }
}
