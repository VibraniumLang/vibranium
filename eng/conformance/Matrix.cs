using System;
using System.Collections.Generic;

namespace Vibranium.Conformance;

/// <summary>
/// One tiny reference probe per banned-by-design / cut row of the matrix (docs/CONFORMANCE.md).
/// These are matrix rows, not campaign templates: the fuzzer never emits them verbatim.
/// </summary>
internal static class MatrixProbes
{
    public static readonly (string Tag, string Source)[] Rows =
    [
        ("banned.null", "WriteLine(F(1));\nstatic int F(int p)\n{\n    int? q = p > 0 ? p : null;\n    return q ?? 0;\n}\n"),
        ("banned.nullable", "WriteLine(F(1));\nstatic int F(int p)\n{\n    int? q = p;\n    return q ?? 0;\n}\n"),
        ("banned.null-forgiving", "string? s = \"a\";\nWriteLine(s!);\n"),
        ("banned.goto", "WriteLine(F(1));\nstatic int F(int p)\n{\n    if (p > 0) goto Done;\n    p = 5;\nDone:\n    return p;\n}\n"),
        ("banned.string-concat", "string a = \"x\";\nstring b = a + \"y\";\nWriteLine(b);\n"),
        ("banned.string-equality", "string a = \"x\";\nWriteLine(a == \"x\");\n"),
        ("banned.string-length", "string a = \"abc\";\nWriteLine(a.Length);\n"),
        ("banned.interpolation", "int v = 3;\nWriteLine($\"v={v}\");\n"),
        ("banned.struct-idisposable", "using (var d = new D(1)) { WriteLine(d.X); }\npublic struct D(int x) : IDisposable { public int X = x; public void Dispose() => WriteLine(0); }\n"),
        ("banned.manual-dispose", "var r = new R(1);\nr.Dispose();\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() => WriteLine(Id); }\n"),
        ("banned.linear-field", "var h = new H(new R(1));\nWriteLine(h.Res.Id);\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() => WriteLine(Id); }\npublic sealed class H(R r) { public R Res = r; }\n"),
        ("banned.try", "try { WriteLine(1); } finally { WriteLine(2); }\n"),
        ("banned.throw", "WriteLine(F(1));\nstatic int F(int p) => p > 10 ? throw new InvalidOperationException() : p;\n"),
        ("banned.lambda", "Func<int, int> f = x => x + 1;\nWriteLine(f(1));\n"),
        ("banned.nonstatic-localfunc", "WriteLine(F(1));\nstatic int F(int p)\n{\n    return G(2);\n    int G(int z) => z + p;\n}\n"),
        ("banned.static-field", "G.N = 3;\nWriteLine(G.N);\npublic static class G { public static int N; }\n"),
        ("banned.array", "int[] a = [1, 2];\nWriteLine(a[1]);\n"),
        ("banned.object", "object o = 1;\nWriteLine(o is int);\n"),
        ("banned.checked", "int v = 3;\nWriteLine(checked(v + 1));\n"),
        ("banned.inheritance", "WriteLine(new D().V);\npublic class B { public int V = 1; }\npublic sealed class D : B { }\n"),
        ("banned.object-initializer", "S s = new S { A = 1, B = 2 };\nWriteLine(s.A + s.B);\npublic struct S { public int A; public int B; }\n"),
        ("banned.default-nonzerosafe", "Vec<int> v = default;\nWriteLine(v.Count);\n"),
        ("banned.collection-return-span", "WriteLine(G().Length);\nstatic ReadOnlySpan<int> G() { return []; }\n"),
        ("banned.collection-return-span", "WriteLine(G()[1]);\nstatic ReadOnlySpan<int> G() { return [1, 2]; }\n"),
        ("cut.localfunc-in-switch-section", "WriteLine(F(1));\nWriteLine(F(2));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case 1:\n            static int L(int a) => a + 1;\n            return L(k);\n        default:\n            return 0;\n    }\n}\n"),
        ("cut.struct-piecewise-owned", "WriteLine(F());\nstatic int F()\n{\n    S s;\n    s.V = new Vec<int>();\n    s.X = 2;\n    return s.X + s.V.Count;\n}\npublic struct S { public Vec<int> V; public int X; }\n"),
        // C4 (decided cut): every pattern except var and discard on a value of open type-parameter type is VIB000 (the
        // relational and `and` forms of these probes moved to `or` forms when C20 cut them; see the C4+C20 probe below)
        ("cut.generic-pattern", "WriteLine(F(0));\nWriteLine(F(2));\nstatic bool F<T>(T x) => x is 0;\n"),
        ("cut.generic-pattern", "WriteLine(F(1L));\nWriteLine(F(-1L));\nstatic bool F<T>(T x) => x is 1L or -1L;\n"),
        ("cut.generic-pattern", "WriteLine(F(1));\nWriteLine(F(1L));\nstatic bool F<T>(T x) => x is int;\n"),
        ("cut.generic-pattern", "WriteLine(F(new Box(3)));\nstatic int F<T>(T x) => x is Box b ? b.V : 0;\npublic sealed class Box(int v) { public int V = v; }\n"),
        ("cut.generic-pattern", "WriteLine(F(1));\nWriteLine(F(-1));\nstatic bool F<T>(T x) => x is int or long;\n"),
        ("cut.generic-pattern", "WriteLine(F(3));\nWriteLine(F(-3));\nstatic int F<T>(T x) => x switch { 3 => 1, _ => 0 };\n"),
        // C4 and C20 in one file (relational pattern on an open type parameter): one VIB000 per cut
        ("cut.relational-and-pattern", "WriteLine(F(3));\nstatic bool F<T>(T x) => x is > 0;\n"),
        ("cut.generic-pattern", "WriteLine(F(4));\nstatic int F<T>(T x) => x switch { int i => i, _ => 0 };\n"),
        ("cut.generic-pattern", "WriteLine(F(5));\nstatic int F<T>(T x)\n{\n    switch (x)\n    {\n        case 5: return 1;\n        default: return 0;\n    }\n}\n"),
        // C5–C9 (decided cuts): one VIB000 per file at the first occurrence
        ("cut.preprocessor", "#define FOO\n#if FOO\nWriteLine(1);\n#else\nWriteLine(2);\n#endif\n"),
        ("cut.preprocessor", "#if DEBUG\nWriteLine(3);\n#endif\nWriteLine(0);\n"),
        ("cut.preprocessor", "WriteLine(F(1));\nstatic int F(int p)\n{\n#if NET11_0_OR_GREATER\n    return p + 4;\n#else\n    return p;\n#endif\n}\n"),
        ("cut.file-type", "WriteLine(Hidden.K());\nfile static class Hidden\n{\n    public static int K() => 7;\n}\n"),
        ("cut.partial", "WriteLine(M.Twice(4));\nstatic partial class M { public static partial int Twice(int x); }\nstatic partial class M { public static partial int Twice(int x) => x * 2; }\n"),
        ("cut.partial", "WriteLine(new P(3).V);\npublic sealed partial class P(int v) { public int V = v; }\n"),
        ("cut.nested-in-generic", "var b = new B<int>.Inner(2);\nWriteLine(b.X);\npublic sealed class B<T>(T t) where T : unmanaged\n{\n    public T Val = t;\n    public sealed class Inner(T x) { public T X = x; }\n}\n"),
        ("cut.ctor-chaining", "var c = new C(2);\nWriteLine(c.A + c.B);\npublic sealed class C\n{\n    public int A;\n    public int B;\n    public C(int a) : this(a, a * 10) { }\n    public C(int a, int b) { A = a; B = b; }\n}\n"),
        // C10–C20 (user-approved 2026-11-17) and C21: exactly one VIB000 per cut per file, with the exact message, at the anchor
        // of the cut's first occurrence (CutScan.cs computes it; the matrix checks message and position)
        ("cut.ref-readonly-param", "int x = 4;\nWriteLine(F(in x));\nstatic int F(ref readonly int p) => p + 1;\n"),
        ("cut.ref-readonly-param", "int x = 4;\nWriteLine(new C(in x).V + G(in x));\nstatic int G(ref readonly int q) => q * 2;\npublic sealed class C\n{\n    public int V;\n    public C(ref readonly int a) { V = a; }\n}\n"),
        ("cut.underscore-decl", "int _ = 3;\nWriteLine(_ + 1);\n"),
        ("cut.underscore-decl", "var _ = 7;\nWriteLine(_);\n"),
        ("cut.underscore-decl", "WriteLine(F(2));\nstatic int F(int _) => _ * 3;\n"),
        ("cut.underscore-decl", "WriteLine(new C().Get());\npublic sealed class C\n{\n    public int _ = 5;\n    public int Get() => _;\n}\n"),
        ("cut.underscore-decl", "Vec<int> v = [1, 2, 3];\nint n = 0;\nforeach (var _ in v)\n{\n    n++;\n}\nWriteLine(n);\n"),
        ("cut.underscore-decl", "WriteLine(F<int>(4));\nstatic int F<_>(int a) => a + 1;\n"),
        // namespace `_` and using alias `_` (decided by main: C11 is literal)
        ("cut.underscore-decl", "WriteLine(_.K.V());\nnamespace _\n{\n    public static class K\n    {\n        public static int V() => 3;\n    }\n}\n"),
        ("cut.underscore-decl", "using _ = System.Int64;\n_ x = 5;\nWriteLine(x + 1);\n"),
        ("cut.optional-param", "WriteLine(F(1));\nstatic int F(int a, int b = 2) => a + b;\n"),
        ("cut.optional-param", "WriteLine(new C(1).V);\npublic sealed class C\n{\n    public int V;\n    public C(int a, int b = 5) { V = a + b; }\n}\n"),
        ("cut.optional-param", "WriteLine(F(1, 2));\nstatic int F(int a, [System.Runtime.InteropServices.Optional] int b) => a + b;\n"),
        ("cut.named-arg-order", "WriteLine(F(b: 2, a: 1));\nstatic int F(int a, int b) => a * 10 + b;\n"),
        ("cut.named-arg-order", "WriteLine(F(1, c: 3, b: 2));\nstatic int F(int a, int b, int c) => a * 100 + b * 10 + c;\n"),
        ("cut.this-assign", "var s = new S(1, 2);\ns.Reset();\nWriteLine(s.A + s.B);\npublic struct S(int a, int b)\n{\n    public int A = a;\n    public int B = b;\n    [Mut] public void Reset() { this = new S(0, 0); }\n}\n"),
        ("cut.this-assign", "var s = new S(1);\ns.Bump();\nWriteLine(s.A);\npublic struct S(int a)\n{\n    public int A = a;\n    [Mut] public void Bump() { Inc(ref this); }\n    static void Inc(ref S t) { t.A += 1; }\n}\n"),
        ("cut.conditional-ref", "int a = 1;\nint b = 2;\nbool c = a < b;\nref int r = ref (c ? ref a : ref b);\nr = 5;\nWriteLine(a * 10 + b);\n"),
        ("cut.conditional-ref", "int a = 1;\nint b = 2;\nbool c = a > b;\nInc(ref (c ? ref a : ref b));\nWriteLine(a * 10 + b);\nstatic void Inc(ref int x) { x += 1; }\n"),
        ("cut.nested-collection", "int k = 2;\nReadOnlySpan<int> s = k > 1 ? [1, 2] : [3];\nWriteLine(s.Length);\n"),
        ("cut.nested-collection", "int[][] jag = [[1], [2, 3]];\nWriteLine(jag.Length);\n"),
        ("cut.nested-collection", "int k = 2;\nReadOnlySpan<int> s = k switch { 1 => [1], _ => [2, 3] };\nWriteLine(s.Length);\n"),
        ("cut.native-int", "nint n = 5;\nWriteLine((long)n + 1);\n"),
        ("cut.native-int", "WriteLine((long)nint.MaxValue > 0);\n"),
        ("cut.native-int", "long x = 7;\nWriteLine((long)(nuint)x);\n"),
        ("cut.native-int", "IntPtr p = IntPtr.Zero;\nWriteLine(p == IntPtr.Zero);\n"),
        ("cut.native-int", "Vec<nint> v = [1, 2];\nWriteLine(v.Count);\n"),
        ("cut.switch-section-decl", "WriteLine(F(1));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case 1:\n            int t = k * 2;\n            return t;\n        default:\n            return 0;\n    }\n}\n"),
        ("cut.switch-section-decl", "WriteLine(F(1));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case 1:\n            int.TryParse(\"5\", out var q);\n            return q;\n        default:\n            return 0;\n    }\n}\n"),
        ("cut.switch-section-decl", "WriteLine(F(2));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case 2:\n            if (k is int z && z > 1) return z;\n            return 0;\n        default:\n            return 1;\n    }\n}\n"),
        ("cut.constant-condition", "if (true) WriteLine(1);\nWriteLine(2);\n"),
        ("cut.constant-condition", "int n = 0;\ndo\n{\n    n++;\n} while (false);\nWriteLine(n);\n"),
        ("cut.constant-condition", "const int K = 3;\nWriteLine(K > 2 ? 1 : 0);\n"),
        ("cut.constant-condition", "int k = 1;\nWriteLine(k switch { 1 when true => 5, _ => 6 });\n"),
        ("cut.constant-condition", "WriteLine(4 switch { 4 => 1, _ => 0 });\n"),
        ("cut.constant-condition", "if (1 == 1) WriteLine(1);\nif (2 > 1) WriteLine(2);\n"),
        ("cut.relational-and-pattern", "int k = 4;\nWriteLine(k is > 3);\n"),
        ("cut.relational-and-pattern", "WriteLine(F(-2));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case < 0:\n            return 1;\n        default:\n            return 0;\n    }\n}\n"),
        ("cut.relational-and-pattern", "int k = 4;\nWriteLine(k is not 0 and not 5);\n"),
        ("cut.relational-and-pattern", "int k = 12;\nWriteLine(k switch { >= 10 => 1, _ => 0 });\n"),
        // C21 (decided 2026-11-18): `: base(...)` with or without arguments; one VIB000 per file at the first `base`
        ("cut.base-ctor-call", "var c = new C(2);\nWriteLine(c.A);\npublic sealed class C\n{\n    public int A;\n    public C(int a) : base() { A = a; }\n}\n"),
        ("cut.base-ctor-call", "WriteLine(new D(4).V);\npublic class B\n{\n    public int V;\n    public B(int v) { V = v; }\n}\npublic sealed class D : B\n{\n    public D(int v) : base(v + 1) { }\n}\n"),
        ("cut.base-ctor-call", "WriteLine(new C(1).A + new E(2).A);\npublic sealed class C\n{\n    public int A;\n    public C(int a) : base() { A = a; }\n}\npublic sealed class E\n{\n    public int A;\n    public E(int a) : base() { A = a * 2; }\n}\n"),
        ("cut.base-ctor-call", "var c = new C(2);\nWriteLine(c.A + c.B);\npublic sealed class C\n{\n    public int A;\n    public int B;\n    public C(int a) : this(a, a * 10) { }\n    public C(int a, int b) : base() { A = a; B = b; }\n}\n"),
        // two cuts in one file (C11 and C19): one VIB000 per cut, each at its own anchor
        ("cut.underscore-decl", "int _ = 1;\nif (true) WriteLine(_);\n"),
    ];

    /// <summary>
    /// Owner decisions on semantics that SPEC already states but the frontends disagreed on (N8: `foreach (var e in span)`
    /// over a <c>Span&lt;T&gt;</c> is a shared reborrow; N13: flow checks follow Roslyn reachability), or where SPEC was
    /// aligned to the frontends (N15: no instance call on `this` in a constructor). Expected is "accept" or the VIB ID
    /// every frontend must report.
    /// </summary>
    public static readonly (string Name, string Expected, string Source)[] Decided =
    [
        ("decided.span-foreach-read-in-body", "accept", "WriteLine(F());\nstatic int F()\n{\n    Span<int> s = [1, 2];\n    int n = 0;\n    foreach (var e in s)\n    {\n        n += e + s.Length + s[0];\n    }\n    return n;\n}\n"),
        ("decided.span-foreach-use-after-loop", "accept", "WriteLine(F());\nstatic int F()\n{\n    Span<int> s = [1, 2];\n    int n = 0;\n    foreach (var e in s)\n    {\n        n += e;\n    }\n    s[0] = 5;\n    return n + s[0];\n}\n"),
        ("decided.span-param-foreach-read-in-body", "accept", "Span<int> t = [1, 2];\nWriteLine(G(t));\nstatic int G(Span<int> s)\n{\n    int n = 0;\n    foreach (var e in s)\n    {\n        n += e + s.Length;\n    }\n    return n;\n}\n"),
        ("decided.span-foreach-write-in-body", "VIB003", "WriteLine(F());\nstatic int F()\n{\n    Span<int> s = [1, 2];\n    int n = 0;\n    foreach (var e in s)\n    {\n        s[0] = e;\n        n += e;\n    }\n    return n;\n}\n"),
        ("decided.span-foreach-ref-read-in-body", "VIB003", "WriteLine(F());\nstatic int F()\n{\n    Span<int> s = [1, 2];\n    int n = 0;\n    foreach (ref var e in s)\n    {\n        n += e + s.Length;\n    }\n    return n;\n}\n"),
        // N13 (decided: flow checks follow Roslyn reachability): a default section that Roslyn proves unreachable is not
        // flow-checked (since the C10–C20 cuts: `0`/`not 0` instead of relational labels, and a braced default section)
        ("decided.unreachable-default-use-after-move", "accept", "WriteLine(F(3));\nstatic int F(int x)\n{\n    switch (x)\n    {\n        case 0: return 1;\n        case not 0: return 2;\n        default:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n    }\n}\n"),
        ("decided.unreachable-bool-default-use-after-move", "accept", "WriteLine(F(true));\nstatic int F(bool b)\n{\n    switch (b)\n    {\n        case true: return 1;\n        case false: return 2;\n        default:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n    }\n}\n"),
        ("decided.unreachable-default-overlap", "accept", "WriteLine(F(3));\nstatic int F(int x)\n{\n    switch (x)\n    {\n        case 0: return 1;\n        case not 0: return 2;\n        default:\n        {\n            Option<Cell> o = Some(new Cell(2));\n            return Two(o.AsMut(), o.AsMut());\n        }\n    }\n}\nstatic int Two(OptionMut<Cell> a, OptionMut<Cell> b) => a.IsSome ? 1 : 0;\npublic sealed class Cell(int v) { public int V = v; }\n"),
        ("decided.reachable-default-use-after-move", "VIB001", "WriteLine(F(3));\nstatic int F(int x)\n{\n    switch (x)\n    {\n        case 0: return 1;\n        case 1: return 2;\n        default:\n        {\n            Vec<int> v = [1];\n            var w = v;\n            return v.Count;\n        }\n    }\n}\n"),
        // N15 (decided: SPEC aligned to the implementations): in v0.2 a constructor may not call an instance method on
        // `this`, read an instance property through `this` or let `this` escape, whatever is assigned (VIB008)
        ("decided.ctor-this-call-after-full-init", "VIB008", "WriteLine(new Zc(3).N);\npublic sealed class Zc\n{\n    public Vec<int> Items;\n    public Zc(int k)\n    {\n        Items = [k];\n        WriteLine(Peek());\n    }\n    public int N => Items.Count;\n    int Peek() => Items.Count;\n}\n"),
        ("decided.ctor-this-call-zero-safe-fields-only", "VIB008", "WriteLine(new Zc(3).K);\npublic sealed class Zc\n{\n    public int K;\n    public Zc(int k)\n    {\n        K = k;\n        K += Peek();\n    }\n    int Peek() => K * 2;\n}\n"),
        ("decided.ctor-this-call-before-init", "VIB008", "WriteLine(new Zc(3).K);\npublic sealed class Zc\n{\n    public Vec<int> Items;\n    public int K;\n    public Zc(int k)\n    {\n        K = Peek();\n        Items = [k];\n    }\n    int Peek() => Items.Count;\n}\n"),
        ("decided.ctor-static-call-control", "accept", "WriteLine(new Zc(3).K);\npublic sealed class Zc\n{\n    public Vec<int> Items;\n    public int K;\n    public Zc(int k)\n    {\n        Items = [k];\n        K = Twice(k);\n    }\n    static int Twice(int x) => x * 2;\n}\n"),
        // SPEC §3.4 (release-7, from the seeded-bug recall): a `!Copy` union payload binds as a shared borrow, so a plain
        // reassignment of the binding is VIB004; a `Copy` payload binds by value and may be reassigned
        ("decided.noncopy-pattern-binding-reassign", "VIB004", "Option<C> o = Some(new C(1));\nC d = new C(2);\nif (o is Some<C>(var c))\n{\n    c = d;\n    WriteLine(c.F);\n}\npublic sealed class C(int f)\n{\n    public int F = f;\n}\n"),
        ("decided.copy-pattern-binding-reassign-control", "accept", "Option<int> o = Some(1);\nif (o is Some<int>(var k))\n{\n    k = 3;\n    WriteLine(k);\n}\n"),
        // N20 controls (release-7.1): plain variant arms, at most one per variant, plus _/default, on an rvalue or Move(o)
        ("decided.linear-switch-plain-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkL(p))\n    {\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType: n += 7; break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-expr-discard-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    n += MkL(p) switch { Some<R>(var v) => Eat(v), _ => 7 };\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-move-place-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    Option<R> o = MkL(p);\n    switch (Move(o))\n    {\n        case NoneType: n += 7; break;\n        case Some<R>(var v): n += Eat(v); break;\n        default: n += 9; break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-result-plain-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkRL(p))\n    {\n        case Ok<R>(var v): n += Eat(v); break;\n        case Err<int>(var e): n += e; break;\n    }\n    return n;\n}\nstatic Result<R, int> MkRL(int a)\n{\n    if (a > 2) return Ok(new R(a));\n    return Err(a + 20);\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        // N20 narrowed (main, 2026-11-18): `var u` over the whole union and a guarded discard arm after the variant arms
        // are valid (all three frontends accept them and they run correctly)
        ("decided.linear-switch-var-whole-union-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkL(p))\n    {\n        case var u: n += Take(u); break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Take(Option<R> u)\n{\n    int n = 0;\n    switch (Move(u))\n    {\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType: n += 7; break;\n    }\n    return n;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-move-place-expr-var-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    Option<R> o = MkL(p);\n    n += Move(o) switch { var u => Take(u) };\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Take(Option<R> u)\n{\n    int n = 0;\n    switch (Move(u))\n    {\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType: n += 7; break;\n    }\n    return n;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-expr-guarded-discard-control", "accept", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    n += MkL(p) switch { Some<R>(var v) => Eat(v), _ when p < 2 => 3, _ => 7 };\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
    ];

    /// <summary>
    /// Decided rows whose expectation waits on a root of known-roots.json (release-7.1). An expectation that starts
    /// with <c>=</c> is exact: every frontend must give exactly that ID set (no secondary IDs). While the root is not
    /// <c>fixed</c>, a violation of such a row is reported as the root's category (<c>known:N20</c> for an open root,
    /// <c>new:N20</c> for a new one) and fails <c>matrix</c> under <c>--fail-on open</c> (the default) like a gate
    /// bucket of that root, but not under <c>--fail-on new</c> for an open root. Once the root is <c>fixed</c>, a
    /// violation is a plain violation.
    /// </summary>
    public static readonly (string Name, string Expected, string Root, string Source)[] DecidedPending =
    [
        // N20 (decided 2026-11-18, narrowed the same day): in a switch statement or expression over a LINEAR union
        // with an rvalue or Move(o) scrutinee, an arm that tests a variant (Some<R>(...), NoneType, Ok<R>(...),
        // Err<E>(...)) may not carry a when guard, a variant may be tested by at most one arm, and not / or patterns
        // are not allowed: each is exactly {VIB006} on all three frontends. `var u` over the whole union and a guarded
        // discard arm stay valid (controls below). A place scrutinee keeps what all three report (VIB006 for the
        // borrow-match in place, plus VIB005 where an arm moves the payload); a matrix row does not fix that set.
        ("decided.linear-switch-guard-repeat-variant", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkL(p))\n    {\n        case Some<R>(var v) when p > 5: n += 100 + Eat(v); break;\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType: n += 7; break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-guarded-none-default", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkL(p))\n    {\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType when p < 2: n += 3; break;\n        default: n += 7; break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-expr-guard-repeat-variant", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    n += MkL(p) switch { Some<R>(var v) when p > 5 => 100 + Eat(v), Some<R>(var v) => Eat(v), NoneType => 7 };\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-result-guarded-err", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkRL(p))\n    {\n        case Ok<R>(var v): n += Eat(v); break;\n        case Err<int>(var e) when e > 21: n += e; break;\n        default: n += 7; break;\n    }\n    return n;\n}\nstatic Result<R, int> MkRL(int a)\n{\n    if (a > 2) return Ok(new R(a));\n    return Err(a + 20);\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-move-place-guarded-none", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    Option<R> o = MkL(p);\n    switch (Move(o))\n    {\n        case NoneType when p < 2: n += 3; break;\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType: n += 7; break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-not-label", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkL(p))\n    {\n        case not Some<R>: n += 3; break;\n        case Some<R>(var v): n += Eat(v); break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\nstatic int Eat(R r)\n{\n    using var u = r;\n    return u.Id;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
        ("decided.linear-switch-or-label", "=VIB006", "N20", "WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);\nstatic int Us(int p)\n{\n    int n = 0;\n    switch (MkL(p))\n    {\n        case NoneType or Some<R>: n += 5; break;\n    }\n    return n;\n}\nstatic Option<R> MkL(int a)\n{\n    if (a > 2) return Some(new R(a));\n    return None;\n}\npublic sealed class R(int id) : IDisposable\n{\n    public int Id = id;\n    public void Dispose() { WriteLine(Id); }\n}\n"),
    ];

    /// <summary>Controls that must stay valid next to the cuts.</summary>
    public static readonly (string Name, string Source)[] Controls =
    [
        ("control.localfunc-in-braced-section-block", "WriteLine(F(1));\nWriteLine(F(2));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case 1:\n        {\n            static int L(int a) => a + 1;\n            return L(k);\n        }\n        default:\n            return 0;\n    }\n}\n"),
        ("control.struct-plain-fieldwise", "WriteLine(F());\nstatic int F()\n{\n    S s;\n    s.A = 1;\n    s.B = 2;\n    return s.A + s.B;\n}\npublic struct S { public int A; public int B; }\n"),
        ("control.struct-owned-overwrite-after-full-init", "WriteLine(F());\nstatic int F()\n{\n    S s = new S(new Vec<int>(), 1);\n    s.V = new Vec<int>();\n    s.X = 4;\n    return s.X + s.V.Count;\n}\npublic struct S(Vec<int> v, int x) { public Vec<int> V = v; public int X = x; }\n"),
        // C4 controls: var and discard patterns on a value of open type-parameter type stay valid
        ("control.generic-var-pattern", "WriteLine(F(4));\nstatic int F<T>(T x) => x is var v ? 1 : 0;\n"),
        ("control.generic-discard-arm", "WriteLine(F(4));\nstatic int F<T>(T x) => x switch { _ => 1 };\n"),
        ("control.generic-var-arm", "WriteLine(F(4));\nstatic T F<T>(T x) => x switch { var v => v };\n"),
        // C5/C8/C9 controls: #region, nesting in a non-generic type and several constructors without chaining stay valid
        ("control.region", "#region R\nWriteLine(5);\n#endregion\n"),
        ("control.nested-in-nongeneric", "var b = new B.Inner(2);\nWriteLine(b.X);\npublic sealed class B\n{\n    public int Val;\n    public sealed class Inner(int x) { public int X = x; }\n}\n"),
        ("control.multi-ctor-no-chaining", "var c = new C(2);\nWriteLine(c.A + new C(1, 2).B);\npublic sealed class C\n{\n    public int A;\n    public int B;\n    public C(int a) { A = a; B = a * 10; }\n    public C(int a, int b) { A = a; B = b; }\n}\n"),
        // C10–C21 controls and C11 near-misses (real discards stay valid)
        ("control.c10-in-param", "int x = 4;\nWriteLine(F(in x) + F(x));\nstatic int F(in int p) => p + 1;\n"),
        ("control.c10-ref-readonly-local", "int x = 4;\nref readonly int r = ref x;\nWriteLine(r);\n"),
        ("nearmiss.c11-discard-assign", "int v = 2;\n_ = F(v);\nWriteLine(v);\nstatic int F(int a) => a + 1;\n"),
        ("nearmiss.c11-discard-out", "bool ok = int.TryParse(\"12\", out _);\nWriteLine(ok);\n"),
        ("nearmiss.c11-discard-out-var", "if (int.TryParse(\"12\", out var _)) WriteLine(1);\nif (int.TryParse(\"x\", out int _)) WriteLine(2);\n"),
        ("nearmiss.c11-discard-arm", "int k = 3;\nWriteLine(k switch { 1 => 10, _ => 20 });\n"),
        ("nearmiss.c11-discard-type-pattern", "long k = 3;\nswitch (k)\n{\n    case 1:\n        WriteLine(1);\n        break;\n    case long _:\n        WriteLine(2);\n        break;\n}\n"),
        ("control.c12-overloads", "WriteLine(M.F(1) + M.F(1, 2));\npublic static class M\n{\n    public static int F(int a) => F(a, 2);\n    public static int F(int a, int b) => a + b;\n}\n"),
        ("control.c13-named-in-position", "WriteLine(F(a: 1, b: 2) + F(1, b: 2));\nstatic int F(int a, int b) => a * 10 + b;\n"),
        ("control.c14-field-assign", "var s = new S(1, 2);\ns.Reset();\nWriteLine(s.A + s.B);\npublic struct S(int a, int b)\n{\n    public int A = a;\n    public int B = b;\n    [Mut] public void Reset() { this.A = 0; B = 0; }\n}\n"),
        ("control.c15-if-ref", "int a = 1;\nint b = 2;\nbool c = a > b;\nif (c) Inc(ref a); else Inc(ref b);\nWriteLine(a * 10 + b);\nstatic void Inc(ref int x) { x += 1; }\n"),
        ("control.c16-collection-locals", "int k = 2;\nReadOnlySpan<int> a = [1, 2];\nReadOnlySpan<int> b = [3];\nReadOnlySpan<int> s = k > 1 ? a : b;\nWriteLine(s.Length);\n"),
        ("control.c17-long", "long n = 5;\nWriteLine(n + 1);\nulong u = 7;\nWriteLine(u);\n"),
        ("control.c18-braced-section", "WriteLine(F(1));\nstatic int F(int k)\n{\n    switch (k)\n    {\n        case 1:\n        {\n            int t = k * 2;\n            return t;\n        }\n        case int n when n > 3:\n            return n;\n        default:\n            return 0;\n    }\n}\n"),
        ("control.c19-loop-true", "int n = 0;\nwhile (true)\n{\n    n++;\n    if (n > 2) break;\n}\nfor (;;)\n{\n    n++;\n    break;\n}\ndo\n{\n    n++;\n    if (n > 5) break;\n} while (true);\nWriteLine(n);\n"),
        ("control.c19-constant-operand", "int k = 2;\nconst int K = 1;\nif (k > K) WriteLine(k);\nWriteLine(k > K ? 1 : 0);\n"),
        ("control.c20-or-not-when", "int k = 4;\nWriteLine(k is 1 or 4);\nWriteLine(k is not 0);\nWriteLine(k switch { int n when n > 3 => 1, _ => 0 });\n"),
        ("control.c21-ctor-without-initializer", "var c = new C();\nWriteLine(c.A + new C(5).A);\npublic sealed class C\n{\n    public int A = 1;\n    public C() { A = 3; }\n    public C(int a) { A = a; }\n}\n"),
    ];
}
