using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round9RegressionTests
{
    private static string RepoRoot
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "Vibranium.slnx")))
            {
                dir = Directory.GetParent(dir)!.FullName;
            }
            return dir;
        }
    }

    private static string DotnetExe
    {
        get
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
            return File.Exists(local) ? local : "dotnet";
        }
    }

    private static string SelfCompiler
    {
        get
        {
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            return Path.Combine(RepoRoot, "src", "vib-self", "bin", configuration, "net11.0", "vib-self.dll");
        }
    }

    private static (int Exit, string Out, string Err) Run(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Round9_ParameterConstants_RespectNearestRuntimeBinding_Differential()
    {
        const string source = """
            WriteLine(C.F(false)); WriteLine(C.F(true));
            WriteLine(C.G(2)); WriteLine(C.G(9));
            WriteLine(C.H(1.5)); WriteLine(C.H(8.5));
            WriteLine(C.S(2.5f)); WriteLine(C.S(8.5f));
            WriteLine(C.K(E.A)); WriteLine(C.K(E.B));
            WriteLine(C.T("parameter"));
            public enum E { A, B }
            public class C
            {
                public const bool Flag = true;
                public const int Number = 2;
                public const double Fraction = 1.5;
                public const float Single = 2.5f;
                public const E Kind = E.A;
                public const string Text = "field";
                public static int F(bool Flag) { if (Flag) return 1; return 2; }
                public static int G(int Number) { if ((Number + 1) * 2 == 6) return 3; return 4; }
                public static int H(double Fraction) { if (Fraction + 0.5 == 2.0) return 5; return 6; }
                public static int S(float Single) { if (Single + 0.5f == 3.0f) return 7; return 8; }
                public static int K(E Kind) { if (Kind == E.A) return 9; return 10; }
                public static string T(string Text) => Text;
            }
            """;
        await AssertDifferential(source, "2\n1\n3\n4\n5\n6\n7\n8\n9\n10\nparameter\n");
    }

    [Fact]
    public async Task Round9_LocalAndImportedConstants_RespectLexicalScope_Differential()
    {
        const string source = """
            using static Constants;
            WriteLine(C.Local(false)); WriteLine(C.Local(true));
            WriteLine(C.Inner(false)); WriteLine(C.Inner(true));
            WriteLine(C.DifferentType(3.5));
            WriteLine(Imported(false)); WriteLine(Imported(true));
            WriteLine(ImportedNumber(9)); WriteLine(ImportedText("argument"));
            static int Imported(bool Flag) { if (!(!Flag)) return 1; return 2; }
            static int ImportedNumber(int Number) => Number + 1;
            static string ImportedText(string Text) => Text;
            public class Constants
            {
                public const bool Flag = true;
                public const int Number = 2;
                public const string Text = "field";
            }
            public class C
            {
                public const bool Flag = true;
                public const int Number = 2;
                public static int Local(bool input)
                {
                    bool Flag = input;
                    int Number = input ? 2 : 9;
                    if (Flag && Number == 2) return 1;
                    return 2;
                }
                public static int Inner(bool input)
                {
                    int result = 0;
                    { bool Flag = input; if (Flag) result = 3; else result = 4; }
                    bool fieldFlag = Flag;
                    return fieldFlag ? result : -1;
                }
                public static int DifferentType(double Number) { if (Number == 2.0) return 1; return 2; }
            }
            """;
        await AssertDifferential(source, "2\n1\n4\n3\n2\n2\n1\n10\nargument\n");
    }

    [Fact]
    public async Task Round9_LegitimateLocalConstants_PreserveTypedEvaluation_Differential()
    {
        const string source = """
            C.Run();
            public enum E { A = 2, B = 3 }
            public class C
            {
                public const bool Flag = true;
                public const int Number = 99;
                public const E Kind = E.A;
                public const string Text = "field";
                public static void Run()
                {
                    const int Number = 7;
                    const int alias = Number + 1;
                    const int nested = (int)((long)alias << 1);
                    const int overflow = unchecked(int.MaxValue + 1);
                    const byte wrap = unchecked((byte)257);
                    const ulong high = ulong.MaxValue;
                    const E Kind = E.B;
                    const E enumAlias = Kind;
                    const bool Flag = false;
                    const string Text = "local";
                    const string textAlias = Text;
                    const double fraction = 1.75;
                    const int truncated = (int)fraction;
                    const float single = (float)fraction;
                    bool chooseAlias = true;
                    int selected = chooseAlias ? alias : nested;
                    WriteLine(Number); WriteLine(alias); WriteLine(nested);
                    WriteLine(overflow); WriteLine((int)wrap); WriteLine(high);
                    bool localFlag = Flag;
                    WriteLine((int)enumAlias); WriteLine(localFlag ? 1 : 0);
                    WriteLine(textAlias); WriteLine(truncated); WriteLine((int)single); WriteLine(selected);
                    { const int inner = nested + 1; WriteLine(inner); }
                }
            }
            """;
        await AssertDifferential(source, "7\n8\n16\n-2147483648\n1\n18446744073709551615\n3\n0\nlocal\n1\n1\n8\n17\n");
    }

    [Fact]
    public async Task Round9_ImportedConstants_PreserveKindsAndAliases_Differential()
    {
        const string source = """
            using static Constants<int>;
            const int number = Number + 1;
            const bool flag = Flag;
            const E kind = Kind;
            const double fraction = Fraction + 0.5;
            const string text = Text;
            bool runtimeFlag = flag;
            WriteLine(number); WriteLine(runtimeFlag ? 1 : 0); WriteLine((int)kind);
            WriteLine((int)fraction); WriteLine(text); WriteLine(Complete());
            WriteLine(Shadow(false)); WriteLine(Shadow(true));
            static int Complete() { bool runtimeFlag = Flag; if (runtimeFlag) return Number; return 0; }
            static int Shadow(bool Flag) { if (Flag) return 1; return 2; }
            public enum E { A, B }
            public class Constants<T> where T : unmanaged
            {
                public const int Number = 2;
                public const bool Flag = true;
                public const E Kind = E.B;
                public const double Fraction = 1.5;
                public const string Text = "imported";
            }
            """;
        await AssertDifferential(source, "3\n1\n1\n2\nimported\n2\n2\n1\n");
    }

    [Fact]
    public async Task Round9_ConstantSwitchSelectors_AreCut()
    {
        const string source = """
            WriteLine(Literal()); WriteLine(Alias()); WriteLine(Boolean());
            WriteLine(EnumCase()); WriteLine(Relational()); WriteLine(Composite());
            WriteLine(Single()); WriteLine(Double()); WriteLine(Nan());
            WriteLine(Guard()); WriteLine(Bound());
            static int Literal() { switch (/*move*/1) { case 1: return 5; } }
            static int Alias() { const int x = 2; switch (x + 1) { case 3: return 6; } }
            static int Boolean() { switch (!false) { case true: return 7; } }
            static int EnumCase() { switch (E.B) { case E.B: return 8; } }
            static int Relational() { switch (3) { case > 1: return 9; } }
            static int Composite() { switch (3) { case > 1 and < 4: return 10; } }
            static int Single() { switch (1.5f) { case 1.5f: return 11; } }
            static int Double() { switch (1.5) { case >= 1.0: return 12; } }
            static int Nan() { switch (0.0 / 0.0) { case double.NaN: return 13; } }
            static int Guard() { switch (1) { case 1 when 1 + 1 == 2: return 14; } }
            static int Bound() { switch (2) { case int x: return x + 13; } }
            public enum E { A, B }
            """;
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConstantConditionMessage);
    }

    [Fact]
    public async Task Round9_SwitchGuardAndBreakPaths_PreserveRuntimeDispatch_Differential()
    {
        const string source = """
            WriteLine(Guarded(false)); WriteLine(Guarded(true));
            WriteLine(Break(false)); WriteLine(Break(true));
            WriteLine(Miss()); WriteLine(Runtime(false)); WriteLine(Runtime(true));
            static int Guarded(bool b) { int selector=1; switch (selector) { case 1 when b: return 1; default: return 2; } }
            static int Break(bool b) { int selector=1; switch (selector) { case 1 when b: break; default: return 3; } return 4; }
            static int Miss() { int selector=2; switch (selector) { case 1: return 5; default: return 6; } }
            static int Runtime(bool b) { switch (b || true) { case true: return 7; default: return 8; } }
            """;
        await AssertDifferential(source, "2\n1\n3\n4\n6\n7\n7\n");
    }

    public static IEnumerable<object[]> NonconstantInitializers()
    {
        foreach ((string type, string value, string expr) in new[]
        {
            ("bool", "true", "Value"), ("bool", "true", "!(!Value)"),
            ("int", "2", "Value"), ("int", "2", "Value + 1"),
            ("int", "2", "(int)((long)Value + 1)"), ("int", "2", "Value > 0 ? 1 : Value"),
            ("double", "1.5", "Value + 0.5"), ("float", "1.5f", "Value + 0.5f"),
            ("string", "\"field\"", "Value"), ("string", "\"field\"", "Value.AsUtf8().Length > 0 ? Value : \"other\""),
            ("E", "E.A", "Value"), ("E", "E.A", "Value == E.A ? Value : E.B"),
        })
        {
            yield return [type, expr, $"C.F({value}); public enum E {{ A, B }} public class C {{ public const {type} Value = {value}; public static void F({type} Value) {{ const {type} alias = {expr}; }} }}"];
            yield return [type + "Local", expr, $"C.F({value}); public enum E {{ A, B }} public class C {{ public const {type} Value = {value}; public static void F({type} input) {{ {type} Value = input; const {type} alias = {expr}; }} }}"];
        }
    }

    [Theory]
    [MemberData(nameof(NonconstantInitializers))]
    public void Round9_ShadowedConstantInitializers_AreRejected(string type, string expression, string source)
    {
        AssertRejected(source, "CS0133", "compile-time constant", type + ": " + expression);
    }

    [Theory]
    [InlineData("bool", "Number", "CS0029")]
    [InlineData("int", "Flag", "CS0029")]
    [InlineData("int", "Kind", "CS0266")]
    [InlineData("E", "Number", "CS0266")]
    [InlineData("int", "Fraction", "CS0266")]
    [InlineData("string", "Number", "CS0029")]
    public void Round9_ImportedConstants_RejectWrongKinds(string type, string expression, string roslynId)
    {
        string source = $$"""
            using static Constants<int>;
            const {{type}} value = {{expression}};
            _ = value;
            public enum E { A, B }
            public class Constants<T> where T : unmanaged
            {
                public const int Number = 2;
                public const bool Flag = true;
                public const E Kind = E.B;
                public const double Fraction = 1.5;
            }
            """;
        AssertRejected(source, roslynId, null, type + ": " + expression);
    }

    [Theory]
    [InlineData("if (Flag) return 1;")]
    [InlineData("if (Number == 2) return 1;")]
    [InlineData("if (Number + 1 == 3) return 1;")]
    [InlineData("while (Flag) { return 1; }")]
    [InlineData("switch (Number) { case 2: return 1; }")]
    [InlineData("switch (Flag) { case true: return 1; }")]
    [InlineData("switch (Number) { case 1 when Flag: return 1; }")]
    public void Round9_ShadowedReturnConditions_AreNotConstants(string body)
    {
        AssertRejected("WriteLine(C.F(false, 9)); public class C { public const bool Flag=true; public const int Number=2; public static int F(bool Flag, int Number) { " + body + " } }", "CS0161", "Not all code paths return a value");
    }

    [Theory]
    [InlineData("switch (b ? 2 : 3) { case 1: return 1; }")]
    [InlineData("switch (b ? 1 : 2) { case 1: break; }")]
    [InlineData("switch (b ? 1 : 2) { case 1 when !b: return 1; }")]
    [InlineData("switch (b ? 1 : 2) { case 1 when b: return 1; }")]
    [InlineData("switch (b ? 1 : 2) { case 1 when b || true: return 1; }")]
    [InlineData("switch (b || true) { case true: return 1; }")]
    [InlineData("switch (b ? 1 : 1) { case 1: return 1; }")]
    [InlineData("switch (b ? 1 : 2) { case int x when x == 1: return 1; }")]
    public void Round9_NonexhaustiveSwitchPaths_RemainReachable(string body)
    {
        AssertRejected("WriteLine(F(true)); static int F(bool b) { " + body + " }", "CS0161", "Not all code paths return a value");
    }

    [Fact]
    public async Task Round9_RefAssignmentRoots_PreserveProjectedStorage_Differential()
    {
        const string source = """
            var a = new S(1, 2); var b = new S(3, 4);
            ref S r = ref a;
            (r = ref b).X = 5;
            (r = ref a).Y += 10;
            WriteLine(a.X); WriteLine(a.Y); WriteLine(b.X); WriteLine(b.Y);
            (r = ref b).Inc();
            WriteLine(a.X); WriteLine(b.X);
            (r = ref b).Y = (r = ref a).X + 7;
            WriteLine(a.Y); WriteLine(b.Y);
            Bump(ref (r = ref b).X);
            WriteLine(b.X);
            static void Bump(ref int x) { x += 1; }
            public struct S(int x, int y)
            {
                public int X = x; public int Y = y;
                public void Inc() { X += 100; }
            }
            """;
        await AssertDifferential(source, "1\n12\n5\n4\n1\n105\n12\n8\n106\n");
    }

    [Fact]
    public async Task Round9_RefAssignmentNoncopyRoots_BorrowRatherThanMove_Differential()
    {
        const string source = """
            var w1 = new Wrap([1, 2]);
            var w2 = new Wrap([3]);
            ref Wrap r = ref w1;
            int n = (r = ref w2).V.Count;
            WriteLine(n); WriteLine(r.V.Count);
            (r = ref w2).V.Add(9);
            WriteLine(r.V.Count); WriteLine(w1.V.Count); WriteLine(w2.V.Count);
            WriteLine((r = ref w1).V[1]);
            public struct Wrap(Vec<int> v) { public Vec<int> V = v; }
            """;
        await AssertDifferential(source, "1\n1\n2\n2\n2\n2\n");
    }

    [Fact]
    public async Task Round9_ParameterWritesAndLocalRefCarriers_RemainSupported_Differential()
    {
        const string source = """
            int a = 1; int b = 2;
            Write(ref a, ref b);
            WriteLine(a); WriteLine(b);
            WriteLine(Read(in a, in b));
            WriteLine(Scoped(ref a)); WriteLine(a);
            static void Write(ref int x, ref int y)
            {
                ref int local = ref x;
                x += (local = ref y);
                local += 5;
            }
            static int Read(in int x, in int y)
            {
                ref readonly int local = ref x;
                int first = local;
                local = ref y;
                return first + local;
            }
            static int Scoped(scoped ref int x)
            {
                x += 1;
                ref int local = ref x;
                int stack = 10;
                local = ref stack;
                local += 1;
                return stack;
            }
            """;
        await AssertDifferential(source, "3\n7\n10\n11\n4\n");
    }

    [Theory]
    [InlineData("ref int x, ref int y", "ref a, ref b", "x = ref y;")]
    [InlineData("ref int x, ref int y", "ref a, ref b", "x = (x = ref y);")]
    [InlineData("ref int x, ref int y", "ref a, ref b", "x += (x = ref y);")]
    [InlineData("in int x, in int y", "in a, in b", "x = ref y;")]
    [InlineData("scoped ref int x, ref int y", "ref a, ref b", "int local = 5; x = ref local;")]
    [InlineData("out int x, ref int y", "out a, ref b", "x = 0; x = ref y; x = 3;")]
    public async Task Round9_ParameterRebinding_IsConsistentlyOutsideSubset(string parameters, string arguments, string body)
    {
        string source = "int a=1; int b=2; F(" + arguments + "); WriteLine(a); WriteLine(b); static void F(" + parameters + ") { " + body + " }";
        await AssertDiagnosticParity(source, "VIB000");
    }

    [Theory]
    [InlineData("int a = X; int X = 7; return a + X;")]
    [InlineData("int a = X, X = 7; return a + X;")]
    [InlineData("if (X == 5) return 1; int X = 7; return X;")]
    [InlineData("int a = X; Set(out int X); return a + X;")]
    [InlineData("for (int a = X, X = 7; a < X; a++) {} return 0;")]
    [InlineData("switch (k) { case 0: { return X; int X = 7; } default: return 0; }")]
    public void Round9_LexicalReservation_RejectsFieldFallbackBeforeDeclaration(string body)
    {
        string source = "WriteLine(C.F(1)); public static class C { public const int X = 5; public static int F(int k) { " + body + " } static void Set(out int value) { value=7; } }";
        AssertRejected(source, "CS0844", "before its declaration");
    }

    [Fact]
    public void Round9_LexicalReservation_RejectsInstanceFieldFallback()
    {
        AssertRejected("var c = new C(); WriteLine(c.F()); public class C { public int X=5; public int F() { int a=X; int X=7; return a+X; } }", "CS0844", "before its declaration");
    }

    [Theory]
    [InlineData("WriteLine(X); int X=7;", "CS0841")]
    [InlineData("int X=X; WriteLine(X);", "CS0165")]
    [InlineData("var X=X; WriteLine(X);", "CS0841")]
    [InlineData("int a=1; ref int X=ref X; WriteLine(a); WriteLine(X);", "CS0165")]
    [InlineData("const int X=X; WriteLine(X);", "CS0110")]
    [InlineData("if (b) { int X=1; WriteLine(X); } int X=2; WriteLine(X);", "CS0136")]
    [InlineData("switch(k) { case 0: { int X=1; WriteLine(X); int X=2; WriteLine(X); break; } default: break; }", "CS0128")]
    public void Round9_LexicalReservation_RejectsInvalidDeclarationSpaces(string body, string roslynId)
    {
        AssertRejected("F(true,1); static void F(bool b,int k) { " + body + " }", roslynId, null, selfId: roslynId == "CS0165" && body.StartsWith("int X=", StringComparison.Ordinal) ? "VIB001" : "VIB000");
    }

    [Fact]
    public async Task Round9_LexicalReservation_PreservesNonoverlappingAndQualifiedBindings_Differential()
    {
        const string source = """
            WriteLine(C.F());
            public static class C
            {
                public const int X=5;
                public static int F()
                {
                    int sum=C.X;
                    { int X=1; sum+=X; }
                    { int X=2; sum+=X; }
                    for(int X=0;X<2;X++) sum+=X;
                    for(int X=0;X<2;X++) sum+=X;
                    int value=(sum switch { int X when X>0 => X, _ => 0 });
                    int initialized=(initialized=3);
                    int slot=4; slot+=1;
                    return value+initialized+slot;
                }
            }
            """;
        await AssertDifferential(source, "18\n");
    }

    [Fact]
    public async Task Round9_StaticConstantFields_AreValuesNotMutablePlaces_Differential()
    {
        const string source = """
            WriteLine(F()); WriteLine(G(int.MaxValue)); WriteLine(G(C.Two)); WriteLine(H());
            static int F() { double value=double.NaN; switch(value) { case double.NaN: return 1; default:return 0; } }
            static int G(in int x) => x;
            static int H() { E value=E.A; switch(value) { case E.A: return 3; default:return 0; } }
            public static class C { public const int Two=2; }
            public enum E { A, B }
            """;
        await AssertDifferential(source, "1\n2147483647\n2\n3\n");
    }

    [Theory]
    [InlineData("int value=((r=ref a)=2); WriteLine(value);")]
    [InlineData("int value=((r=ref a)=(r=ref b)); WriteLine(value);")]
    [InlineData("WriteLine((r=ref a)=2);")]
    [InlineData("a=(r=ref b)=3;")]
    [InlineData("int value=(_=((r=ref a)=2)); WriteLine(value);")]
    [InlineData("int slot=0; slot=((r=ref a)=2); WriteLine(slot);")]
    [InlineData("int value=a==1 ? ((r=ref a)=2) : 0; WriteLine(value);")]
    [InlineData("int value=a switch { _ => ((r=ref a)=2) }; WriteLine(value);")]
    [InlineData("a+=((r=ref b)=3);")]
    [InlineData("WriteLine(_=((r=ref a)=2));")]
    public async Task Round9_ConsumedSimpleRefTargetAssignments_HaveSdkCompatibilityDiagnostic(string body)
    {
        await AssertDiagnosticParity("int a=1; int b=2; ref int r=ref b; " + body, "VIB000");
    }

    [Fact]
    public async Task Round9_DiscardedAndUnaffectedRefTargetAssignments_RemainSupported_Differential()
    {
        const string source = """
            int a=1; int b=2; ref int r=ref b;
            (r=ref a)=3; WriteLine(a);
            _=((r=ref a)=4); WriteLine(a);
            _=(_=((r=ref a)=5)); WriteLine(a);
            _=unchecked((r=ref a)=6); WriteLine(a);
            for((r=ref a)=7; a<8; (r=ref a)=8) { WriteLine(a); }
            for(int i=0; i<1; i++, _=((r=ref a)=9)) { WriteLine(a); }
            WriteLine(a);
            int value=((r=ref a)+=2); WriteLine(value);
            value=(r=(r=ref b)); WriteLine(value); WriteLine(a);
            Set();
            static void Set() { int a=1; int b=2; ref int r=ref a; M(ref r,ref b); WriteLine(a); WriteLine(b); }
            static void M(ref int a,ref int b) => a=7;
            """;
        await AssertDifferential(source, "3\n4\n5\n6\n7\n8\n9\n11\n2\n2\n7\n2\n");
    }

    [Fact]
    public async Task Round9_ReadonlyClassReferentTarget_PreservesSnapshot_Differential()
    {
        const string source = """
            var h1=new Holder(new Node(1),5); var h2=new Holder(new Node(2),6);
            ref Holder r=ref h1;
            r.N.Val=K(ref (r=ref h2))+10;
            WriteLine(h1.N.Val); WriteLine(h2.N.Val);
            static int K(ref Holder h)=>h.K;
            public struct Holder(Node n,int k) { public readonly Node N=n; public int K=k; }
            public class Node(int v) { public int Val=v; }
            """;
        await AssertDifferential(source, "16\n2\n");
    }

    [Theory]
    [InlineData("struct", "=")]
    [InlineData("class", "=")]
    [InlineData("struct", "+=")]
    public async Task Round9_ReadonlyClassReferentTarget_PreservesOwnerReservation(string holderKind, string assignment)
    {
        string source = "var h=new Holder(new Node(1)); h.N.Val " + assignment + " Swap(ref h); WriteLine(h.N.Val); static int Swap(ref Holder h) { h=new Holder(new Node(9)); return 5; } public " + holderKind + " Holder(Node n) { public readonly Node N=n; } public class Node(int v) { public int Val=v; }";
        await AssertDiagnosticParity(source, "VIB003");
    }

    [Theory]
    [InlineData("h.N=new Node(2);", "CS0191")]
    [InlineData("h.S.X=2;", "CS1648")]
    public void Round9_ReadonlyFinalDestinations_RemainRejected(string statement, string roslynId)
    {
        AssertRejected("var h=new Holder(new Node(1),new S(1)); " + statement + " WriteLine(h.N.Val); public class Holder(Node n,S s) { public readonly Node N=n; public readonly S S=s; } public struct S(int x) { public int X=x; } public class Node(int v) { public int Val=v; }", roslynId, null, selfId: "VIB004");
    }

    [Fact]
    public async Task Round9_StaticLocalFunctions_SeeEnclosingConstantsWithoutCaptures_Differential()
    {
        const string source = """
            const bool K=true;
            const int N=3;
            const string S="ok";
            const double D=1.5;
            WriteLine(F()); WriteLine(G()); WriteLine(H()); WriteLine((int)I()); WriteLine(J(7));
            static int F() { bool value=K; while(value) { return 1; } return 0; }
            static int G() { int value=N; while(value==3) { return 2; } return 0; }
            static string H() => S;
            static double I() => D;
            static int J(int N) => N;
            """;
        await AssertDifferential(source, "1\n2\nok\n1\n7\n");
    }

    [Fact]
    public async Task Round9_NestedStaticLocalFunctions_UseNearestEnclosingConstant_Differential()
    {
        const string source = """
            WriteLine(F());
            static int F()
            {
                const int N=1;
                _=N;
                return G();
                static int G()
                {
                    const int N=2;
                    return H();
                    static int H() => N;
                }
            }
            """;
        await AssertDifferential(source, "2\n");
    }

    [Fact]
    public void Round9_StaticLocalFunctions_CannotCaptureRuntimeBindingsDespiteFields()
    {
        AssertRejected("WriteLine(C.F(2)); public static class C { public const int N=1; public static int F(int N) { return G(); static int G()=>N; } }", "CS8421", "cannot capture");
    }

    [Theory]
    [InlineData("WriteLine(F()); const int K=22; static int F()=>K;", "22\n")]
    [InlineData("{ const int K=33; static int F()=>K; WriteLine(F()); }", "33\n")]
    [InlineData("const int K=5; WriteLine(F()); static int F(){const int B=K+50;return B;}", "55\n")]
    public async Task Round9_EnclosingConstants_RespectLexicalDeclarationOrder_Differential(string source, string expected)
    {
        await AssertDifferential(source, expected);
    }

    [Fact]
    public Task Round9_OptionalParameterWithEnclosingConstant_IsCut() =>
        Round11AuditAssertions.AssertCutRejected(
            "const int K=9; WriteLine(F()); static int F(/*move*/int x=K)=>x;",
            "Optional parameters are not supported in Vibranium v0.2; declare an overload instead.");

    [Theory]
    [InlineData("static int F()=>K; const int K=11; WriteLine(F());")]
    [InlineData("{ static int F()=>K; WriteLine(F()); } const int K=11; _=K;")]
    public void Round9_EnclosingConstants_RejectTextuallyEarlierReferences(string source)
    {
        AssertRejected(source, "CS0841", "before its declaration");
    }

    [Fact]
    public async Task Round9_SwitchBlockConstants_AreRejectedWithoutBraces()
    {
        const string source = """
            #pragma warning disable CS0162
            WriteLine(F(2)); WriteLine(C.G()); WriteLine(H(1));
            static int F(int value)
            {
                switch(value)
                {
                    case 1: /*move*/const int K=101; return K;
                    case 2: return K+1;
                    default: return 0;
                }
            }
            static int H(int value)
            {
                int result;
                switch(1)
                {
                    case >0 and var n when value>0: result=n; break;
                    default: result=0; break;
                }
                return result;
            }
            public static class C
            {
                public const int K=2;
                public static int G()
                {
                    switch(K) { case 1: const int K=7; return K; case 2: return K+1; default:return 0; }
                }
            }
            """;
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.SwitchDeclarationMessage);
    }

    [Theory]
    [InlineData("case 1: x=7; break;")]
    [InlineData("case >0 and var n: x=n; break;")]
    public async Task Round9_ConstantSwitchDispatch_IsRejectedBeforeDefiniteAssignment(string section)
    {
        string source="WriteLine(F(false)); static int F(bool b){ int x; switch(/*move*/1){"+section+"} return x; }";
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConstantConditionMessage);
    }

    [Theory]
    [InlineData("uint.MaxValue", "case >0:")]
    [InlineData("ulong.MaxValue", "case >0UL:")]
    [InlineData("-1L", "case <0L:")]
    [InlineData("1.5", "case >1.0 and <2.0:")]
    [InlineData("double.NaN", "case double.NaN:")]
    [InlineData("float.NaN", "case float.NaN:")]
    [InlineData("1", "case not 0:")]
    [InlineData("1", "case 0 or 1:")]
    public async Task Round9_ConstantScalarSwitchSelectors_AreRejected(string selector, string label)
    {
        string source = "WriteLine(F());static int F(){int x;switch(/*move*/"+selector+"){"+label+"x=7;break;}return x;}";
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConstantConditionMessage);
    }

    [Fact]
    public void Round9_NonconstantSwitchGuard_DoesNotEstablishDefiniteAssignment()
    {
        AssertRejected("WriteLine(F(false)); static int F(bool b){int x;switch(b?1:2){case 1 when b||true:x=7;break;}return x;}",
            "CS0165", "definitely assigned", selfId: "VIB001");
    }

    [Theory]
    [InlineData("int x=1;", "x=2;WriteLine(x);")]
    [InlineData("var x=1;", "x=2;WriteLine(x);")]
    [InlineData("int x=1,y=2;", "x=3;y=4;WriteLine(x+y);")]
    [InlineData("ref int x=ref a;", "x=ref b;WriteLine(x);")]
    public async Task Round9_SwitchBlockRuntimeLocal_RequiresBraces(string declaration, string use)
    {
        string source="F(1);static void F(int mode){int a=1;int b=3;if(mode<0)WriteLine(a+b);switch(mode){case 0:/*move*/"+declaration+"WriteLine(x);break;case 1:"+use+"break;}}";
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.SwitchDeclarationMessage);
    }

    [Fact]
    public void Round9_SwitchBlockUnassignedLocal_DoesNotFallBackToConstantField()
    {
        AssertRejected("C.F(1);static class C{const int x=90;public static void F(int mode){switch(mode){case 0:{int x;WriteLine(x);break;}case 1:WriteLine(x);break;}}}",
            "CS0165", "definitely assigned", selfId: "VIB001");
    }

    [Fact]
    public async Task Round9_SwitchLocalInitializationAndCleanup_RepeatWithoutEagerValues()
    {
        const string source = "for(int i=0;i<3;i++){switch(i%2){case 0:{Box x=new Box(i);WriteLine(x.X);break;}case 1:{Box x=new Box(i);WriteLine(x.X);break;}}}public class Box(int value){public int X=value;}";
        await AssertDifferential(source, "0\n1\n2\n");
    }

    [Fact]
    public void Round9_UsingDeclaration_DirectSwitchSectionIsInvalidCSharp()
    {
        AssertRejected("F(1);static void F(int x){switch(x){case 1:using var r=new R();break;}}public class R:System.IDisposable{public void Dispose(){}}",
            "CS8647", Round11AuditAssertions.SwitchDeclarationMessage);
    }

    [Fact]
    public async Task Round9_BlockScopedSwitchUsing_DisposesOnlyExecutedDeclarations()
    {
        const string source="for(int i=0;i<3;i++){switch(i%2){case 0:{using var r=new R();WriteLine(i);break;}case 1:WriteLine(i);break;}}public class R:System.IDisposable{public void Dispose(){WriteLine(9);}}";
        await AssertDifferential(source, "0\n9\n1\n2\n9\n");
    }

    [Fact]
    public async Task Round9_ConstantSwitchExpression_IsRejected()
    {
        const string source="WriteLine(F());static int F(){int x;int y=/*move*/1 switch{>0 and var n=>(x=n),_=>0};return x+y;}";
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.ConstantConditionMessage);
    }

    [Fact]
    public async Task Round9_SwitchBlockMultipleConstants_AreRejectedWithoutBraces()
    {
        const string source="F(1);static void F(int x){switch(x){case 0:/*move*/const int a=1,b=2;WriteLine(a+b);break;case 1:WriteLine(a+b);break;}}";
        await Round11AuditAssertions.AssertCutRejected(source, Round11AuditAssertions.SwitchDeclarationMessage);
    }

    private static async Task AssertDiagnosticParity(string source, params string[] expectedIds)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r9-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "RejectedSubset", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            var ids = frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray();
            Assert.Equal(expectedIds, ids);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(expectedIds, analyzer.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(expectedIds, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(id => id).ToArray());
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static void AssertRejected(string source, string roslynId, string? selfMessage, string caseName = "", string selfId = "VIB000")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r9-reject-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Rejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == roslynId);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
            Assert.Contains(selfId, self.Out + self.Err, StringComparison.Ordinal);
            if (selfMessage is not null) Assert.Contains(selfMessage, self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r9-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round9Managed", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round9Managed.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round9Managed.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            var managed = Run(DotnetExe, dll);
            Assert.Equal((expectedExit, expected, expectedError), managed);

            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
            string c0Ir = Path.Combine(temp, "stage0.ll");
            File.WriteAllText(c0Ir, stage0.LlvmIr);
            string selfIr = Path.Combine(temp, "self.ll");
            var self = Run(DotnetExe, SelfCompiler, "emit-ll", path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", selfIr);
            Assert.True(self.Exit == 0, self.Out + self.Err);
            foreach (string ir in new[] { c0Ir, selfIr })
            {
                var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.Exit == 0, verify.Err);
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + (sanitize ? ".asan" : ".native");
                    var args = new List<string> { "-O3", ir, "-o", native };
                    if (sanitize) args.Add("-fsanitize=address,leak");
                    var build = Run("clang-18", args.ToArray());
                    Assert.True(build.Exit == 0, build.Err);
                    var actual = Run(native);
                    Assert.Equal(managed, actual);
                }
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
