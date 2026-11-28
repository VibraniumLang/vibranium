using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// vib-self runs each instance field initializer once per object, in declaration order, before the constructor body, in
// every constructor shape (implicit, explicit, overloaded, primary; class and struct), and each initializer is a scope
// and declaration space of its own (C#): its out and pattern variables are not visible in the body or in another
// initializer (N125, N47). An initializer has no `this` (CS0236, CS0027). Expected output is .NET's; rows come from the
// self-fix-init class table.
public sealed class Round12SelfFieldInitializerScopeTests
{
    private const string Helpers = """
        static class G
        {
            public static int GetI(out int x) { x = 5; return 1; }
            public static int MkV(out Vec<int> v) { v = [1, 2]; return 1; }
            public static int MkD(out D d) { d = new D(); return 1; }
            public static Vec<int> Keep(int k, Vec<int> v) { v.Add(k); return v; }
            public static int Log(string s) { WriteLine(s); return 0; }
            public static int Two(out int a, out int b) { a = 1; b = 2; return 3; }
            public static int Num() => 5;
            public static Option<int> Opt() => Some(5);
        }
        class D { public int N = G.Log("d") + 5; }
        """;

    private static string Program(string program, bool helpers) => helpers ? program + "\n" + Helpers + "\n" : program + "\n";

    public static IEnumerable<object[]> Accepted()
    {
        yield return ["impl-out-const-later", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                const int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B = q;
            }
            """, true, "6\n7\n"];
        yield return ["impl-vec-none-laterdecl", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                public int A = G.MkV(out Vec<int> q) + q.Count;
                public int B = G.GetI(out int q) * q;
            }
            """, true, "3\n5\n"];
        yield return ["impl-cls-const-later", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                const int q = 7;
                public int A = G.MkD(out D q) + q.N;
                public int B = q;
            }
            """, true, "d\n6\n7\n"];
        yield return ["expl-out-const-body", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                const int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B;
                public C() { WriteLine("ctor"); B = q; }
            }
            """, true, "ctor\n6\n7\n"];
        yield return ["expl-vec-none-ctorlocal", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                public int A = G.MkV(out Vec<int> q) + q.Count;
                public int B;
                public C() { WriteLine("ctor"); int q = 9; B = q; }
            }
            """, true, "ctor\n3\n9\n"];
        yield return ["expl-cls-field-body", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                public int q = 7;
                public int A = G.MkD(out D q) + q.N;
                public int B;
                public C() { WriteLine("ctor"); B = q; }
            }
            """, true, "d\nctor\n6\n7\n"];
        yield return ["expl2-out-const-later", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            var e = new C(3);
            WriteLine(e.A);
            WriteLine(e.B);
            class C
            {
                const int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B = q;
                public C() { WriteLine("ctor"); }
                public C(int k) { WriteLine("ctor"); WriteLine(k);}
            }
            """, true, "ctor\n6\n7\nctor\n3\n6\n7\n"];
        yield return ["expl2-move-none-ctorlocal", """
            var c = new C();
            WriteLine(c.A.Count);
            WriteLine(c.B);
            var e = new C(3);
            WriteLine(e.A.Count);
            WriteLine(e.B);
            class C
            {
                public Vec<int> A = G.Keep(G.MkV(out Vec<int> q), q);
                public int B;
                public C() { WriteLine("ctor"); int q = 9; B = q; }
                public C(int k) { WriteLine("ctor"); int q = 9; B = q; WriteLine(k);}
            }
            """, true, "ctor\n3\n9\nctor\n3\n3\n9\n"];
        yield return ["expl2-cls-const-later", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            var e = new C(3);
            WriteLine(e.A);
            WriteLine(e.B);
            class C
            {
                const int q = 7;
                public int A = G.MkD(out D q) + q.N;
                public int B = q;
                public C() { WriteLine("ctor"); }
                public C(int k) { WriteLine("ctor"); WriteLine(k);}
            }
            """, true, "d\nctor\n6\n7\nd\nctor\n3\n6\n7\n"];
        yield return ["primk-out-const-later", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            WriteLine(c.K);
            class C(int k)
            {
                public int K = k;
                const int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B = q;
            }
            """, true, "6\n7\n1\n"];
        yield return ["primk-vec-none-laterdecl", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            WriteLine(c.K);
            class C(int k)
            {
                public int K = k;
                public int A = G.MkV(out Vec<int> q) + q.Count;
                public int B = G.GetI(out int q) * q;
            }
            """, true, "3\n5\n1\n"];
        yield return ["primk-side-const-laterdecl", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            WriteLine(c.K);
            class C(int k)
            {
                public int K = k;
                const int q = 7;
                public int A = G.Log("a") + G.GetI(out int q) + q;
                public int B = G.GetI(out int q) * q;
            }
            """, true, "a\n6\n5\n1\n"];
        yield return ["sexpl-out-const-body", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            struct C
            {
                const int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B;
                public C(int k) { WriteLine("ctor"); B = q; }
            }
            """, true, "ctor\n6\n7\n"];
        yield return ["sexpl-cls-none-ctorlocal", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            struct C
            {
                public int A = G.MkD(out D q) + q.N;
                public int B;
                public C(int k) { WriteLine("ctor"); int q = 9; B = q; }
            }
            """, true, "d\nctor\n6\n9\n"];
        yield return ["sexpl0-out-none-laterdecl", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            struct C
            {
                public int A = G.GetI(out int q) + q;
                public int B = G.GetI(out int q) * q;
                public C() { WriteLine("ctor"); }
            }
            """, true, "ctor\n6\n5\n"];
        yield return ["sexpl0-move-const-body", """
            var c = new C();
            WriteLine(c.A.Count);
            WriteLine(c.B);
            struct C
            {
                const int q = 7;
                public Vec<int> A = G.Keep(G.MkV(out Vec<int> q), q);
                public int B;
                public C() { WriteLine("ctor"); B = q; }
            }
            """, true, "ctor\n3\n7\n"];
        yield return ["sexpl0-side-field-body", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            struct C
            {
                public int q = 7;
                public int A = G.Log("a") + G.GetI(out int q) + q;
                public int B;
                public C() { WriteLine("ctor"); B = q; }
            }
            """, true, "a\nctor\n6\n7\n"];
        yield return ["sprimk-out-const-later", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            WriteLine(c.K);
            struct C(int k)
            {
                public int K = k;
                const int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B = q;
            }
            """, true, "6\n7\n1\n"];
        yield return ["sprimk-cls-const-laterdecl", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            WriteLine(c.K);
            struct C(int k)
            {
                public int K = k;
                const int q = 7;
                public int A = G.MkD(out D q) + q.N;
                public int B = G.GetI(out int q) * q;
            }
            """, true, "d\n6\n5\n1\n"];
        yield return ["sprimk-vec-none-laterdecl", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            WriteLine(c.K);
            struct C(int k)
            {
                public int K = k;
                public int A = G.MkV(out Vec<int> q) + q.Count;
                public int B = G.GetI(out int q) * q;
            }
            """, true, "3\n5\n1\n"];
        yield return ["n47-implicit-class", """
            WriteLine(new C().B); class C { public int A = Init.First(), B = Init.Second(); }
                    static class Init
                    {
                        public static int First() { WriteLine("first"); return 7; }
                        public static int Second() { WriteLine("second"); return 9; }
                    }
            """, false, "first\nsecond\n9\n"];
        yield return ["n47-nested-new", """
            WriteLine(new Outer().Inner.V); class Outer { public C Inner = new C(); public int After = Init.Second(); } class C { public int V = Init.First(); }
                    static class Init
                    {
                        public static int First() { WriteLine("first"); return 7; }
                        public static int Second() { WriteLine("second"); return 9; }
                    }
            """, false, "first\nsecond\n7\n"];
        yield return ["n47-original", """
            WriteLine(new C().X);
            class C { public int X = H.Pick(); }

            public static class H
            {
                public static bool Flag() { WriteLine("flag"); return false; }
                public static bool Yes() { WriteLine("yes"); return true; }
                public static int Num() { WriteLine("num"); return 3; }
                public static int Pick() { WriteLine("pick"); return 7; }
                public static int Add(int a, int b) { WriteLine("add"); return a + b; }
                public static bool Side() { WriteLine("side"); return true; }
                public static int Bump(ref int v) { WriteLine("bump"); v = v + 1; return v; }
                public static C2 Make() { WriteLine("make"); return new C2(); }
                public static Vec<int> MakeVec() { WriteLine("makevec"); return [4, 5, 6]; }
            }
            public class C2 { public int V = 1; }
            public class C3 { public int V; public C3(int v) { WriteLine("c3"); V = v; } }
            """, false, "pick\n7\n"];
        yield return ["n47-owned-vec", """
            WriteLine(new C().V.Count); class C { public Vec<int> V = Make(); private static Vec<int> Make() { WriteLine("vec"); return [4, 5, 6]; } }
                    static class Init
                    {
                        public static int First() { WriteLine("first"); return 7; }
                        public static int Second() { WriteLine("second"); return 9; }
                    }
            """, false, "vec\n3\n"];
        yield return ["n47-order-three-fields-impl", """
            var c = new C();
            WriteLine(c.A + c.B + c.Z);
            class C { public int A = G.Log("1"); public int B = G.Log("2"), Z = G.Log("3"); }
            """, true, "1\n2\n3\n0\n"];
        yield return ["n47-out-vec-dropped-each-init", """
            var c = new C();
            WriteLine(c.A + c.B);
            class C { public int A = G.MkV(out Vec<int> q) + q.Count; public int B = G.MkV(out Vec<int> q) + q.Count; }
            """, true, "6\n"];
        yield return ["n47-vec-field-impl-lsan", """
            var c = new C();
            WriteLine(c.V.Count + c.W.Count);
            class C { public Vec<int> V = G.Keep(G.Log("v"), []); public Vec<int> W = [1, 2, 3]; }
            """, true, "v\n4\n"];
        yield return ["fld-f07-expl-two-plain-out", """
            C c = new C(); WriteLine(c.A + c.B);
            sealed class C { public int A = G.GetI(out int q) + q; public int B = G.GetI(out int q) * q; public C() { } }
            static class G { public static bool GetB(out int v) { v = 3; return true; } public static int GetI(out int v) { v = 3; return 4; } }
            """, false, "19\n"];
        yield return ["fld-f08-expl-plain-out-and-ctor-local", """
            C c = new C(); WriteLine(c.A);
            sealed class C { public int A = G.GetI(out int q) + q; public C() { int q = 5; A += q; } }
            static class G { public static bool GetB(out int v) { v = 3; return true; } public static int GetI(out int v) { v = 3; return 4; } }
            """, false, "12\n"];
        yield return ["so-r02-plain-outvar-fieldinit-vs-const-ctor-body", """
            WriteLine(new C().B);
            class C { const int q = 7; public int A = G.GetI(out int q) + q; public int B; public C() { B = q; } }
            static class G { public static int GetI(out int x) { x = 5; return 1; } }
            """, false, "7\n"];
        yield return ["so-r03-plain-outvar-fieldinit-vs-const-later-init", """
            WriteLine(new C().B);
            class C { const int q = 7; public int A = G.GetI(out int q) + q; public int B = q; public C() { } }
            static class G { public static int GetI(out int x) { x = 5; return 1; } }
            """, false, "7\n"];
        yield return ["impl-psprop", """
            var c = new C();
            WriteLine(c.B);
            class C
            {
                static int P => 7;
                public int B = P + 1;
            }
            """, true, "8\n"];
        yield return ["sexpl0-psmeth", """
            var c = new C();
            WriteLine(c.B);
            struct C
            {
                static int M() => 7;
                public int B = M() + 1;
                public C() { WriteLine("ctor"); }
            }
            """, true, "ctor\n8\n"];
        yield return ["sexpl-pconst", """
            var c = new C(1);
            WriteLine(c.B);
            struct C
            {
                const int q = 7;
                public int B = q + 1;
                public C(int k) { WriteLine("ctor"); }
            }
            """, true, "ctor\n8\n"];
        yield return ["expl2-pqual", """
            var c = new C();
            WriteLine(c.B);
            var e = new C(3);
            WriteLine(e.B);
            class C
            {
                const int q = 7;
                public int B = C.q + 1;
                public C() { WriteLine("ctor"); }
                public C(int k) { WriteLine("ctor"); WriteLine(k);}
            }
            """, true, "ctor\n8\nctor\n3\n8\n"];
        yield return ["prim-ppar", """
            var c = new C(1);
            WriteLine(c.B);
            class C(int k)
            {
                public int B = k + 1;
            }
            """, true, "2\n"];
        yield return ["sprim-pparshadowfield", """
            var c = new C(1);
            WriteLine(c.B);
            struct C(int k)
            {
                public int k = 7; public int B = k + 1;
            }
            """, true, "2\n"];
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public Task EachInitializerRunsOnceInItsOwnScope_AcceptsWithNativeParity(string name, string program, bool helpers, string expected)
    {
        _ = name;
        return Round12RuleWitness.AcceptAsync(Program(program, helpers), expected);
    }

    public static IEnumerable<object[]> CSharpInvalid()
    {
        yield return ["expl-out-none-body", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            class C
            {
                public int A = G.GetI(out int q) + q;
                public int B;
                public C() { WriteLine("ctor"); B = q; }
            }
            """, true, "CS0103"];
        yield return ["prim-out-none-later", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            class C(int k)
            {
                public int A = G.GetI(out int q) + q;
                public int B = q;
            }
            """, true, "CS0103"];
        yield return ["sexpl-out-none-body", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            struct C
            {
                public int A = G.GetI(out int q) + q;
                public int B;
                public C(int k) { WriteLine("ctor"); B = q; }
            }
            """, true, "CS0103"];
        yield return ["sprim-side-none-later", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            struct C(int k)
            {
                public int A = G.Log("a") + G.GetI(out int q) + q;
                public int B = q;
            }
            """, true, "CS0103"];
        yield return ["so-r08-b15-fieldinit-outvar-vec-leak-into-ctor-body", """
            var c = new C();
            WriteLine(c.N);
            class C { public bool A = G.Mk(out Vec<int> w) && w.Count > 0; public int N; public C() { N = w.Count; w.Add(4); N += w.Count; } }
            static class G { public static bool Mk(out Vec<int> v) { v = new Vec<int>(); v.Add(1); return true; } }
            """, false, "CS0103"];
        yield return ["cs0128-two-out-same-name", """
            WriteLine(new C().A);
            class C { public int A = G.Two(out int q, out int q); }
            """, true, "CS0128"];
        yield return ["cs0841-use-before-out-none", """
            WriteLine(new C().A);
            class C { public int A = q + G.GetI(out int q); }
            """, true, "CS0841"];
        yield return ["cs0841-use-before-out-expl", """
            WriteLine(new C().A);
            class C { const int q = 7; public int A = q + G.GetI(out int q); public C() { } }
            """, true, "CS0844"];
        yield return ["cs0136-switch-arm-redeclares", """
            WriteLine(new C().A);
            class C { public int A = G.GetI(out int q) + (G.Num() switch { var q => q }); }
            """, true, "CS0136"];
        yield return ["impl-n236", """
            var c = new C();
            WriteLine(c.B);
            class C
            {
                public int q = 7;
                public int B = q + 1;
            }
            """, true, "CS0236"];
        yield return ["expl-n236call", """
            var c = new C();
            WriteLine(c.B);
            class C
            {
                public int q = 7;
                public int B = G.Num() + q;
                public C() { WriteLine("ctor"); }
            }
            """, true, "CS0236"];
        yield return ["expl2-out-field-later", """
            var c = new C();
            WriteLine(c.A);
            WriteLine(c.B);
            var e = new C(3);
            WriteLine(e.A);
            WriteLine(e.B);
            class C
            {
                public int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B = q;
                public C() { WriteLine("ctor"); }
                public C(int k) { WriteLine("ctor"); WriteLine(k);}
            }
            """, true, "CS0236"];
        yield return ["prim-n236", """
            var c = new C(1);
            WriteLine(c.B);
            class C(int k)
            {
                public int q = 7;
                public int B = q + 1;
            }
            """, true, "CS0236"];
        yield return ["sexpl-n236call", """
            var c = new C(1);
            WriteLine(c.B);
            struct C
            {
                public int q = 7;
                public int B = G.Num() + q;
                public C(int k) { WriteLine("ctor"); }
            }
            """, true, "CS0236"];
        yield return ["sexpl0-n236", """
            var c = new C();
            WriteLine(c.B);
            struct C
            {
                public int q = 7;
                public int B = q + 1;
                public C() { WriteLine("ctor"); }
            }
            """, true, "CS0236"];
        yield return ["sprim-out-field-later", """
            var c = new C(1);
            WriteLine(c.A);
            WriteLine(c.B);
            struct C(int k)
            {
                public int q = 7;
                public int A = G.GetI(out int q) + q;
                public int B = q;
            }
            """, true, "CS0236"];
        yield return ["impl-n027call", """
            var c = new C();
            WriteLine(c.B);
            class C
            {
                public int q = 7;
                public int B = G.Num() + this.q;
            }
            """, true, "CS0027"];
        yield return ["expl-n027", """
            var c = new C();
            WriteLine(c.B);
            class C
            {
                public int q = 7;
                public int B = this.q;
                public C() { WriteLine("ctor"); }
            }
            """, true, "CS0027"];
        yield return ["expl2-n027call", """
            var c = new C();
            WriteLine(c.B);
            var e = new C(3);
            WriteLine(e.B);
            class C
            {
                public int q = 7;
                public int B = G.Num() + this.q;
                public C() { WriteLine("ctor"); }
                public C(int k) { WriteLine("ctor"); WriteLine(k);}
            }
            """, true, "CS0027"];
        yield return ["prim-n027", """
            var c = new C(1);
            WriteLine(c.B);
            class C(int k)
            {
                public int q = 7;
                public int B = this.q;
            }
            """, true, "CS0027"];
        yield return ["sexpl-n027call", """
            var c = new C(1);
            WriteLine(c.B);
            struct C
            {
                public int q = 7;
                public int B = G.Num() + this.q;
                public C(int k) { WriteLine("ctor"); }
            }
            """, true, "CS0027"];
        yield return ["sexpl0-n027", """
            var c = new C();
            WriteLine(c.B);
            struct C
            {
                public int q = 7;
                public int B = this.q;
                public C() { WriteLine("ctor"); }
            }
            """, true, "CS0027"];
        yield return ["sprim-n027", """
            var c = new C(1);
            WriteLine(c.B);
            struct C(int k)
            {
                public int q = 7;
                public int B = this.q;
            }
            """, true, "CS0027"];
    }

    [Theory]
    [MemberData(nameof(CSharpInvalid))]
    public Task InitializerScopeOrThis_RejectsCSharpInvalid(string name, string program, bool helpers, string csharpId)
    {
        _ = name;
        return Round12SelfCSharpValidity.RejectAsync(Program(program, helpers), csharpId);
    }
}
