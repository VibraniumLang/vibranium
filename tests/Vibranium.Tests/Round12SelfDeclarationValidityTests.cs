using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Frontend.Roslyn;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

// vib-self declaration validity families O1-O5 (docs/SPEC.md §4 "C# validity"): vib-self must reject what C# rejects,
// and keep Stage-0's VIB ID set on every C#-valid program.
// - O1: duplicate member names and signatures (CS0102/CS0111/CS0663/CS0542/CS0082).
// - O2: modifier validity (CS0106/CS0107/CS1004/CS1107/CS1527/CS0238/CS0441/CS0504/CS8657/CS9048).
// - O3: static-class members (CS0708/CS0710/CS0720/CS0715).
// - O4: an application needs exactly one valid entry point (CS5001/CS0017); libraries need none.
// - O5: patterns C# does not accept: a type pattern other than a case type on a union value, the union type included
//   (CS8121), and a bare `_` case label or `is _` (CS0103/CS0246; `case (_):` and `case var _:` are discards).
public sealed class Round12SelfDeclarationValidityTests
{
    public static IEnumerable<object[]> InvalidCSharp()
    {
        yield return new object[] { "o1-r01-field-method", "CS0102", "VIB000", "return 0; class C { public int N; public int N() => 1; }\n" };
        yield return new object[] { "o1-r02-field-field", "CS0102", "VIB000", "return 0; class C { public int N; public long N; }\n" };
        yield return new object[] { "o1-r03-field-prop", "CS0102", "VIB000", "return 0; class C { public int N; public int N => 1; }\n" };
        yield return new object[] { "o1-r04-prop-method", "CS0102", "VIB000", "return 0; class C { public int N => 1; public int N() => 1; }\n" };
        yield return new object[] { "o1-r05-nested-field", "CS0102", "VIB000", "return 0; class C { public int N; public class N { } }\n" };
        yield return new object[] { "o1-r06-nested-method", "CS0102", "VIB000", "return 0; class C { public void N() { } public struct N { } }\n" };
        yield return new object[] { "o1-r07-method-dup", "CS0111", "VIB000", "return 0; class C { public int F(int a) => a; public long F(int b) => b; }\n" };
        yield return new object[] { "o1-r08-method-dup-static", "CS0111", "VIB000", "return 0; class C { public static int F(int a) => a; public int F(int b) => b; }\n" };
        yield return new object[] { "o1-r09-method-generic-dup", "CS0111", "VIB000", "return 0; static class C { public static T F<T>(T a) => a; public static U F<U>(U b) => b; }\n" };
        yield return new object[] { "o1-r10-method-ref-out", "CS0663", "VIB000", "return 0; static class C { public static void F(ref int a) { } public static void F(out int a) { a = 1; } }\n" };
        yield return new object[] { "o1-r11-method-in-ref", "CS0663", "VIB000", "return 0; static class C { public static void F(in int a) { } public static void F(ref int a) { } }\n" };
        yield return new object[] { "o1-r12-ctor-dup", "CS0111", "VIB000", "return 0; class C { public C(int a) { } public C(int b) { } }\n" };
        yield return new object[] { "o1-r13-const-field", "CS0102", "VIB000", "return 0; class C { public const int N = 1; public int N; }\n" };
        yield return new object[] { "o1-r14-const-const", "CS0102", "VIB000", "return 0; static class C { public const int N = 1; public const int N = 2; }\n" };
        yield return new object[] { "o1-r15-enum-dup", "CS0102", "VIB000", "return 0; enum E { A, A }\n" };
        yield return new object[] { "o1-r16-struct-dup", "CS0102", "VIB000", "return 0; struct S { public int A; public int A; }\n" };
        yield return new object[] { "o1-r17-primary-ctor-dup", "CS0111", "VIB000", "return 0; class C(int a) { public C(int b) : this(b) { } }\n" };
        yield return new object[] { "o1-r18-indexer-dup", "CS0111", "VIB000", "return 0; class C { public int this[int i] => i; public int this[int j] => j; }\n" };
        yield return new object[] { "o1-r19-indexer-item", "CS0102", "VIB000", "return 0; class C { public int this[int i] => i; public int Item => 1; }\n" };
        yield return new object[] { "o1-r20-prop-prop", "CS0102", "VIB000", "return 0; class C { public int P => 1; public long P => 2; }\n" };
        yield return new object[] { "o1-r21-top-local-fn-dup", "CS0128", "VIB000", "F(); static void F() { } static void F() { }\n" };
        yield return new object[] { "o1-r22-opadd-dup", "CS0111", "VIB000", "return 0; struct S { public int X; public void operator +=(int a) { X += a; } public void operator +=(int b) { X += b; } }\n" };
        yield return new object[] { "o1-r23-field-tp", "CS0102", "VIB000", "return 0; class C<T> { public int T; }\n" };
        yield return new object[] { "o1-r24-method-member-same-as-type", "CS0542", "VIB000", "return 0; class C { public int C; }\n" };
        yield return new object[] { "o1-r25-nested-generic-vs-field", "CS0102", "VIB000", "return 0; class C { public int N; public class N<T> { } }\n" };
        yield return new object[] { "o1-r26-alias-sig", "CS0111", "VIB000", "return 0; static class C { public static void F(int a) { } public static void F(System.Int32 a) { } }\n" };
        yield return new object[] { "o1-r27-two-files-partial-like", "CS0111", "VIB000", "return 0; class C { public int F() => 1; public int F() => 2; }\n" };
        yield return new object[] { "o1-r28-getter-method-clash", "CS0082", "VIB000", "return 0; class C { public int P => 1; public int get_P() => 2; }\n" };
        yield return new object[] { "o1-r28b-setter-method-clash", "CS0082", "VIB000", "return 0; class C { int _p; public int P { get { return _p; } set { _p = value; } } public void set_P(int v) { } }\n" };
        yield return new object[] { "o1-r28c-indexer-getter-clash", "CS0082", "VIB000", "return 0; class C { public int this[int i] => i; public int get_Item(int i) => i; }\n" };
        yield return new object[] { "o1-r28f-getter-generic", "CS0082", "VIB000", "return 0; class C { public int P => 1; public int get_P<T>() => 2; }\n" };
        yield return new object[] { "o1-r28g-getonly-set-method", "CS0082", "VIB000", "return 0; class C { public int P => 1; public void set_P(int v) { } }\n" };
        yield return new object[] { "o1-r28h-indexer-getonly-set-item", "CS0082", "VIB000", "return 0; class C { public int this[int i] => i; public void set_Item(int i, int v) { } }\n" };
        yield return new object[] { "o1-r29-generic-class-method", "CS0111", "VIB000", "return 0; class C<T> { public void F(T a) { } public void F(T b) { } }\n" };
        yield return new object[] { "o1-r30-vec-sig", "CS0111", "VIB000", "return 0; static class C { public static void F(Vec<int> a) { } public static void F(Vec<int> b) { } }\n" };
        yield return new object[] { "o2-r01-readonly-static-method", "CS0106", "VIB000", "namespace App; static class Program { readonly static int Main() { return 0; } }\n" };
        yield return new object[] { "o2-r02-readonly-static-class", "CS0106", "VIB000", "namespace App; readonly static class Program { static int Main() { return 0; } }\n" };
        yield return new object[] { "o2-r03-private-public-struct", "CS0107", "VIB000", "Holder h; private public struct Holder { }\n" };
        yield return new object[] { "o2-r04-private-public-field", "CS0107", "VIB000", "Pair p; public struct Pair { private public int Y; }\n" };
        yield return new object[] { "o2-r05-static-static-class", "CS1004", "VIB000", "return 0; static static class CorpusSlice { }\n" };
        yield return new object[] { "o2-r06-scoped-scoped", "CS1107", "VIB000", "static void F(scoped scoped Span<int> s, int x) { }\n" };
        yield return new object[] { "o2-r07-private-toplevel", "CS1527", "VIB000", "return 0; private class G { }\n" };
        yield return new object[] { "o2-r08-sealed-static-method", "CS0238", "VIB000", "namespace App; static class Program { sealed static int Main() { return 0; } }\n" };
        yield return new object[] { "o2-r09-sealed-static-class", "CS0441", "VIB000", "namespace App; sealed static class Program { static int Main() { return 0; } }\n" };
        yield return new object[] { "o2-r10-static-const", "CS0504", "VIB000", "return 0; public static class U { static public const int Std = 1; }\n" };
        yield return new object[] { "o2-r11-readonly-static-in-struct", "CS8657", "VIB000", "return 0; public readonly struct S { readonly internal static int F() => 1; }\n" };
        yield return new object[] { "o2-r12-scoped-int", "CS9048", "VIB000", "static bool F(scoped int x) => x is 0;\n" };
        yield return new object[] { "o2-r13-scoped-T", "CS9048", "VIB000", "static T F<T>(scoped T x) => x;\n" };
        yield return new object[] { "o2-r13b-scoped-T-local", "CS9048", "VIB000", "static void F<T>(T x) { scoped T y = x; }\n" };
        yield return new object[] { "o2-r14-readonly-class", "CS0106", "VIB000", "return 0; readonly class C { }\n" };
        yield return new object[] { "o2-r15-readonly-class-method", "CS0106", "VIB000", "return 0; class C { public readonly int F() => 1; }\n" };
        yield return new object[] { "o2-r16-sealed-struct", "CS0106", "VIB000", "return 0; sealed struct S { }\n" };
        yield return new object[] { "o2-r17-static-struct", "CS0106", "VIB000", "return 0; static struct S { }\n" };
        yield return new object[] { "o2-r18-readonly-enum", "CS0106", "VIB000", "return 0; readonly enum E { A }\n" };
        yield return new object[] { "o2-r19-sealed-field", "CS0106", "VIB000", "return 0; class C { sealed int x; }\n" };
        yield return new object[] { "o2-r20-readonly-ctor", "CS0106", "VIB000", "return 0; struct S { public int X; readonly public S(int x) { X = x; } }\n" };
        yield return new object[] { "o2-r21-readonly-const", "CS0106", "VIB000", "return 0; class C { readonly const int X = 1; }\n" };
        yield return new object[] { "o2-r22-public-public", "CS1004", "VIB000", "return 0; public public class C { }\n" };
        yield return new object[] { "o2-r23-internal-public", "CS0107", "VIB000", "return 0; class C { internal public int X; }\n" };
        yield return new object[] { "o2-r24-readonly-readonly-field", "CS1004", "VIB000", "return 0; class C { readonly readonly int X = 1; }\n" };
        yield return new object[] { "o2-r25-this-this", "CS1107", "VIB000", "return 0; static class E { public static int F(this this int x) => x; }\n" };
        yield return new object[] { "o2-r26-ref-ref-param", "CS1107", "VIB000", "static void F(ref ref int x) { }\n" };
        yield return new object[] { "o2-r27-ref-out-param", "CS8328", "VIB000", "static void F(ref out int x) { x = 1; }\n" };
        yield return new object[] { "o2-r28-in-out", "CS8328", "VIB000", "static void F(in out int x) { x = 1; }\n" };
        yield return new object[] { "o2-r29-this-out", "CS8328", "VIB000", "return 0; static class E { public static void F(this out int x) { x = 1; } }\n" };
        yield return new object[] { "o2-r30-sealed-method-class", "CS0238", "VIB000", "return 0; class C { public sealed void F() { } }\n" };
        yield return new object[] { "o2-r31-static-indexer", "CS0106", "VIB000", "return 0; class C { public static int this[int i] => i; }\n" };
        yield return new object[] { "o2-r32-static-local", "CS0106", "VIB000", "static int x = 1; WriteLine(x);\n" };
        yield return new object[] { "o2-r33-readonly-local-fn", "CS0106", "VIB000", "WriteLine(F()); readonly static int F() => 1;\n" };
        yield return new object[] { "o2-r34-sealed-prop", "CS0238", "VIB000", "return 0; class C { public sealed int P => 1; }\n" };
        yield return new object[] { "o2-r35-private-nested-public-dup", "CS1004", "VIB000", "return 0; class C { private private class N { } }\n" };
        yield return new object[] { "o2-r36-readonly-prop-in-class", "CS0106", "VIB000", "return 0; class C { public readonly int P => 1; }\n" };
        yield return new object[] { "o2-r37-readonly-static-prop-struct", "CS8657", "VIB000", "return 0; struct S { public static readonly int P => 1; }\n" };
        yield return new object[] { "o2-r38-scoped-local-int", "CS9048", "VIB000", "scoped int x = 1; WriteLine(x);\n" };
        yield return new object[] { "o2-r39-static-ctor-ish", "CS1004", "VIB000", "return 0; class C { public static static void F() { } }\n" };
        yield return new object[] { "o2-r40-readonly-accessor-class", "CS0106", "VIB000", "return 0; class C { int _x; public int P { readonly get => _x; } }\n" };
        yield return new object[] { "o2-r41-static-sealed-order", "CS0441", "VIB000", "namespace App; static sealed class Program { static int Main() { return 0; } }\n" };
        yield return new object[] { "o2-r42-internal-private-nested", "CS0107", "VIB000", "return 0; class C { internal private class N { } }\n" };
        yield return new object[] { "o2-r43-readonly-method-static-struct-explicit", "CS8657", "VIB000", "return 0; struct S { public static readonly void F() { } }\n" };
        yield return new object[] { "o2-r44-scoped-string", "CS9048", "VIB000", "static int F(scoped string s) => 1;\n" };
        yield return new object[] { "o2-r45-scoped-struct", "CS9048", "VIB000", "static int F(scoped P p) => 1; struct P { }\n" };
        yield return new object[] { "o2-r46-readonly-op", "CS0106", "VIB000", "return 0; class C { public int X; public readonly void operator +=(int a) { } }\n" };
        yield return new object[] { "o2-r47-sealed-ctor", "CS0106", "VIB000", "return 0; class C { sealed public C() { } }\n" };
        yield return new object[] { "o2-r48-static-enum-member", "CS0106", "VIB000", "return 0; enum E { A } class C { public static E Get() => E.A; } static enum F { B }\n" };
        yield return new object[] { "o2-r49-accessor-dup-mod", "CS1004", "VIB000", "return 0; class C { int _x; public int P { get => _x; private private set => _x = value; } }\n" };
        yield return new object[] { "o2-r50-accessor-static", "CS0106", "VIB000", "return 0; class C { int _x; public int P { static get => _x; } }\n" };
        yield return new object[] { "o3-r01-instance-method", "CS0708", "VIB000", "namespace App; static class Program { int Main() { return 0; } }\n" };
        yield return new object[] { "o3-r02-instance-field", "CS0708", "VIB000", "return 0; static class K { public int X; }\n" };
        yield return new object[] { "o3-r03-instance-prop", "CS0708", "VIB000", "return 0; static class K { public int P => 1; }\n" };
        yield return new object[] { "o3-r04-ctor", "CS0710", "VIB000", "return 0; static class K { public K() { } }\n" };
        yield return new object[] { "o3-r05-new", "CS0712", "VIB000", "var k = new K(); return 0; static class K { public static int F() => 1; }\n" };
        yield return new object[] { "o3-r06-local-type", "CS0723", "VIB000", "K k; return 0; static class K { public static int F() => 1; }\n" };
        yield return new object[] { "o3-r07-param-type", "CS0721", "VIB000", "return 0; static void F(K k) { } static class K { }\n" };
        yield return new object[] { "o3-r08-type-arg", "CS0718", "VIB000", "Vec<K> v = []; return 0; static class K { }\n" };
        yield return new object[] { "o3-r10-indexer", "CS0720", "VIB000", "return 0; static class K { public int this[int i] => i; }\n" };
        yield return new object[] { "o3-r11-opadd", "CS0715", "VIB000", "return 0; static class K { public void operator +=(int a) { } }\n" };
        yield return new object[] { "o3-r12-readonly-field", "CS0708", "VIB000", "return 0; static class K { public readonly int X = 1; }\n" };
        yield return new object[] { "o3-r13-instance-method-public", "CS0708", "VIB000", "WriteLine(1); static class K { public void F() { } }\n" };
        yield return new object[] { "o3-r14-field-of-static-type", "CS0723", "VIB000", "return 0; class C { K k; } static class K { }\n" };
        yield return new object[] { "o3-r15-generic-arg-method", "CS0718", "VIB000", "WriteLine(Id<K>()); static int Id<T>() => 1; static class K { }\n" };
        yield return new object[] { "o3-r16-cast", "CS0716", "VIB000", "int x = 1; var y = (K)x; return 0; static class K { }\n" };
        yield return new object[] { "o3-r17-nested-static-instance", "CS0708", "VIB000", "return 0; class O { public static class K { public int X; } }\n" };
        yield return new object[] { "o3-r18-primary-ctor", "CS0710", "VIB000", "return 0; static class K(int x) { }\n" };
        yield return new object[] { "o4-r01-empty", "CS5001", "VIB000", "\n" };
        yield return new object[] { "o4-r02-comment", "CS5001", "VIB000", "// nothing\n" };
        yield return new object[] { "o4-r03-types-only", "CS5001", "VIB000", "namespace App; class C { }\n" };
        yield return new object[] { "o4-r04-instance-main", "CS5001", "VIB000", "namespace App; class Program { int Main() { return 0; } }\n" };
        yield return new object[] { "o4-r05-main-params", "CS5001", "VIB000", "namespace App; static class Program { static int Main(int x) { return x; } }\n" };
        yield return new object[] { "o4-r06-generic-main", "CS5001", "VIB000", "namespace App; static class Program { static int Main<T>() { return 0; } }\n" };
        yield return new object[] { "o4-r07-main-wrong-return", "CS5001", "VIB000", "namespace App; static class Program { static long Main() { return 0; } }\n" };
        yield return new object[] { "o4-r08-two-mains", "CS0017", "VIB000", "namespace App; static class A { static void Main() { } } static class B { static void Main() { } }\n" };
        yield return new object[] { "o4-r09-main-in-generic", "CS5001", "VIB000", "namespace App; static class Outer<T> { static void Main() { } }\n" };
        yield return new object[] { "o4-r10-using-only", "CS5001", "VIB000", "using System;\n" };
        yield return new object[] { "o4-r12-enum-only", "CS5001", "VIB000", "enum E { A }\n" };
        yield return new object[] { "o5-d01-stmt-discard-guard", "CS0103", "VIB000", "int k = 3;\nswitch (Mk(7)) { case _ when k > 5: WriteLine(1); break; case Some<int>(var v): WriteLine(v); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-d02-stmt-discard", "CS0103", "VIB000", "int k = 3;\nswitch (k) { case 1: WriteLine(1); break; case _: WriteLine(0); break; }\n" };
        yield return new object[] { "o5-d03-is-discard", "CS0246", "VIB000", "int k = 3;\nif (k is _) WriteLine(1);\n" };
        yield return new object[] { "o5-d04-stmt-int-discard-guard", "CS0103", "VIB000", "int k = 3;\nswitch (k) { case _ when k > 2: WriteLine(1); break; default: WriteLine(0); break; }\n" };
        yield return new object[] { "o5-d06-is-not-discard", "CS8518", "VIB000", "int k = 3;\nif (k is not _) WriteLine(1); else WriteLine(2);\n" };
        yield return new object[] { "o5-d09-stmt-local-underscore", "CS9135", "VIB000", "int k = 3;\nint _ = 3;\nswitch (k) { case _: WriteLine(1); break; default: WriteLine(0); break; }\n" };
        yield return new object[] { "o5-d13-stmt-discard-section-second-label", "CS0103", "VIB000", "int k = 3;\nswitch (k) { case 1: case _: WriteLine(1); break; }\n" };
        yield return new object[] { "o5-u01-stmt-option-typedecl", "CS8121", "VIB000", "int k = 4;\nswitch (Mk(k)) { case Some<int>(var v) when v > 5: WriteLine(v); break; case Option<int> o: WriteLine(o.IsSome); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u02-expr-option-typedecl", "CS8121", "VIB000", "int k = 4;\nWriteLine(Mk(k) switch { Some<int>(var v) => v, Option<int> o => o.IsSome ? 1 : 0 });\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u03-is-option-typedecl", "CS8121", "VIB000", "int k = 4;\nOption<int> a = Mk(k);\nif (a is Option<int> b) WriteLine(b.IsSome);\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u04-stmt-option-typepattern", "CS8121", "VIB000", "int k = 4;\nswitch (Mk(k)) { case Some<int>(var v): WriteLine(v); break; case Option<int>: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u05-stmt-result-typedecl", "CS8121", "VIB000", "int k = 4;\nswitch (MkR(k)) { case Ok<int>(var v): WriteLine(v); break; case Result<int, int> r: WriteLine(r.IsOk); break; }\nstatic Result<int, int> MkR(int a) { if (a > 2) return Ok(a); return Err(a + 20); }\n" };
        yield return new object[] { "o5-u06-nested-payload-union-typedecl", "CS8121", "VIB000", "int k = 4;\nOption<Option<int>> a = Some(Mk(k));\nswitch (a) { case Some<Option<int>>(Option<int> inner): WriteLine(inner.IsSome); break; case NoneType: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u07-stmt-int-typedecl", "CS8121", "VIB000", "int k = 4;\nswitch (Mk(k)) { case int x: WriteLine(x); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u08-is-not-option-type", "CS8121", "VIB000", "int k = 4;\nOption<int> a = Mk(k);\nif (a is not Option<int>) WriteLine(1); else WriteLine(2);\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u09-stmt-option-typedecl-guard", "CS8121", "VIB000", "int k = 4;\nswitch (Mk(k)) { case Option<int> o when o.IsSome: WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u10-stmt-option-property-typed", "CS8121", "VIB000", "int k = 4;\nswitch (Mk(k)) { case Option<int> { IsSome: true }: WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u11-expr-option-or-type", "CS8121", "VIB000", "int k = 4;\nWriteLine(Mk(k) switch { Option<int> or NoneType => 1, _ => 0 });\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u13-is-int-typedecl", "CS8121", "VIB000", "int k = 4;\nOption<int> a = Mk(k);\nif (a is int x) WriteLine(x); else WriteLine(0);\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u14-expr-int-typedecl", "CS8121", "VIB000", "int k = 4;\nWriteLine(Mk(k) switch { int x => x, _ => 0 });\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
        yield return new object[] { "o5-u15-res-err-payload-union-typedecl", "CS8121", "VIB000", "int k = 4;\nswitch (MkR(k)) { case Ok<int>(var v): WriteLine(v); break; case Err<Option<int>>(Option<int> e): WriteLine(e.IsSome); break; }\nstatic Result<int, Option<int>> MkR(int a) { if (a > 2) return Ok(a); Option<int> s = Some(a); return Err(s); }\n" };
        yield return new object[] { "o5-u16-stmt-not-option-type", "CS8121", "VIB000", "int k = 4;\nswitch (Mk(k)) { case not Option<int>: WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n" };
    }

    [Theory]
    [MemberData(nameof(InvalidCSharp))]
    public void SelfRejectsWhatCSharpRejects(string name, string roslynId, string selfId, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfDeclarationRejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && (roslynId.Length == 0 || diagnostic.Id == roslynId));
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Contains($"error {selfId}:", self.Output + self.Error, StringComparison.Ordinal);
        });
    }

    public static IEnumerable<object[]> ValidControls()
    {
        yield return new object[] { "o1-a01-overload-types", "WriteLine(C.F(1) + C.F(2L)); static class C { public static int F(int a) => a; public static int F(long b) => (int)b; }\n", "" };
        yield return new object[] { "o1-a02-overload-arity", "WriteLine(C.F(1) + C.F<int>(2)); static class C { public static int F(int a) => a; public static int F<T>(int b) => b; }\n", "" };
        yield return new object[] { "o1-a03-overload-ref", "int x = 1; WriteLine(C.F(x) + C.F(ref x)); static class C { public static int F(int a) => a; public static int F(ref int b) => b; }\n", "" };
        yield return new object[] { "o1-a04-overload-count", "WriteLine(C.F(1) + C.F(1, 2)); static class C { public static int F(int a) => a; public static int F(int a, int b) => a + b; }\n", "" };
        yield return new object[] { "o1-a05-ctor-overloads", "WriteLine(new C(1).V + new C(1, 2).V); class C { public int V; public C(int a) { V = a; } public C(int a, int b) { V = a + b; } }\n", "" };
        yield return new object[] { "o1-a06-nested-arity", "WriteLine(1); class C { public class N { } public class N<T> { } }\n", "" };
        yield return new object[] { "o1-a07-indexer-overloads", "var c = new C(); WriteLine(c[1] + c[1L]); class C { public int this[int i] => i; public int this[long j] => (int)j; }\n", "" };
        yield return new object[] { "o1-a08-same-name-diff-types", "WriteLine(new A().N + new B().N); class A { public int N = 1; } class B { public int N = 2; }\n", "" };
        yield return new object[] { "o1-a09-generic-overload-param", "WriteLine(C.F(1) + C.F<long>(2L)); static class C { public static int F(int a) => a; public static int F<T>(T a) where T : unmanaged => 2; }\n", "" };
        yield return new object[] { "o1-a10-prop-getset", "var c = new C(); c.P = 3; WriteLine(c.P); class C { private int _p; public int P { get => _p; [Mut] set => _p = value; } }\n", "" };
        yield return new object[] { "o1-a11-span-overloads", "WriteLine(C.F(\"ab\") + C.F(\"ab\"u8)); static class C { public static int F(string s) => 1; public static int F(ReadOnlySpan<byte> s) => s.Length; }\n", "" };
        yield return new object[] { "o1-a12-opadd-overloads", "var c = new C(); c += 1; c += 2L; WriteLine(c.X); class C { public long X; [Mut] public void operator +=(int a) { X += a; } [Mut] public void operator +=(long b) { X += b; } }\n", "" };
        yield return new object[] { "o1-a13-enum-distinct", "WriteLine((int)E.B); enum E { A, B }\n", "" };
        yield return new object[] { "o1-a14-local-fn-shadow-param", "WriteLine(F(1)); static int F(int a) { return G(a); static int G(int a) => a; }\n", "" };
        yield return new object[] { "o1-a15-generic-type-overload", "WriteLine(C.F(new Box<int>(1)) + C.F(new Box<long>(1))); static class C { public static int F(Box<int> b) => 1; public static int F(Box<long> b) => 2; } class Box<T> { public T V; public Box(T v) { V = v; } }\n", "" };
        yield return new object[] { "o1-a28d-setter-other-type", "return 0; class C { int _p; public int P { get { return _p; } set { _p = value; } } public void set_P(long v) { } }\n", "VIB004" };
        yield return new object[] { "o1-a28e-getter-with-param", "return 0; class C { public int P => 1; public int get_P(int x) => x; }\n", "" };
        yield return new object[] { "o1-a28i-get-item-other-type", "return 0; class C { public int this[int i] => i; public int get_Item(long i) => 0; }\n", "" };
        yield return new object[] { "o1-a28j-setter-ref-param", "return 0; class C { public int P => 1; public void set_P(ref int v) { } }\n", "" };
        yield return new object[] { "o2-a01-readonly-struct", "WriteLine(new S(1).X); readonly struct S { public readonly int X; public S(int x) { X = x; } }\n", "" };
        yield return new object[] { "o2-a02-readonly-member", "WriteLine(new S(1).Get()); struct S { public int X; public S(int x) { X = x; } public readonly int Get() => X; }\n", "" };
        yield return new object[] { "o2-a03-static-class", "WriteLine(K.F()); static class K { public static int F() => 1; public const int C = 2; }\n", "" };
        yield return new object[] { "o2-a04-sealed-class", "WriteLine(new C().X); sealed class C { public int X = 1; }\n", "" };
        yield return new object[] { "o2-a05-internal-sealed", "WriteLine(new C().X); internal sealed class C { public int X = 1; }\n", "" };
        yield return new object[] { "o2-a06-scoped-span", "Span<int> s = [1]; F(s); static void F(scoped Span<int> s) { WriteLine(s[0]); }\n", "" };
        yield return new object[] { "o2-a07-scoped-ref", "int x = 1; F(ref x); static void F(scoped ref int x) { x = 2; WriteLine(x); }\n", "" };
        yield return new object[] { "o2-a08-scoped-ros", "F([1]); static void F(scoped ReadOnlySpan<int> s) { WriteLine(s[0]); }\n", "" };
        yield return new object[] { "o2-a09-this-ext", "WriteLine(1.Twice()); static class E { public static int Twice(this int x) => x * 2; }\n", "" };
        yield return new object[] { "o2-a10-this-ref-ext", "var s = new S(); s.Bump(); WriteLine(s.X); static class E { public static void Bump(this ref S s) { s.X++; } } struct S { public int X; }\n", "" };
        yield return new object[] { "o2-a11-this-in-ext", "var s = new S(); WriteLine(s.Get()); static class E { public static int Get(this in S s) => s.X; } struct S { public int X; }\n", "" };
        yield return new object[] { "o2-a12-readonly-field", "WriteLine(new C().X); class C { public readonly int X = 1; }\n", "" };
        yield return new object[] { "o2-a13-private-nested", "WriteLine(C.Get()); static class C { private class N { public int V = 3; } public static int Get() => new N().V; }\n", "" };
        yield return new object[] { "o2-a14-const-public", "WriteLine(K.N); static class K { public const int N = 4; }\n", "" };
        yield return new object[] { "o2-a15-readonly-getter", "WriteLine(new S(2).P); struct S { private int _x; public S(int x) { _x = x; } public int P { readonly get => _x; set => _x = value; } }\n", "" };
        yield return new object[] { "o2-a16-private-setter", "var c = new C(); c.Set(4); WriteLine(c.P); class C { private int _p; public int P { get => _p; [Mut] private set => _p = value; } [Mut] public void Set(int v) { P = v; } }\n", "" };
        yield return new object[] { "o2-a17-static-local-fn", "WriteLine(F()); static int F() => 1;\n", "" };
        yield return new object[] { "o2-a18-readonly-prop-struct", "WriteLine(new S(5).P); struct S { private int _x; public S(int x) { _x = x; } public readonly int P => _x; }\n", "" };
        yield return new object[] { "o2-a19-scoped-in", "int x = 3; F(in x); static void F(scoped in int x) { WriteLine(x); }\n", "" };
        yield return new object[] { "o2-a20-scoped-local-span", "Span<int> s = [1]; scoped Span<int> t = s; WriteLine(t[0]);\n", "" };
        yield return new object[] { "o2-a21-scoped-out", "F(out int x); WriteLine(x); static void F(scoped out int x) { x = 2; }\n", "" };
        yield return new object[] { "o2-a22-public-static", "WriteLine(K.F()); public static class K { public static int F() => 6; }\n", "" };
        yield return new object[] { "o2-a23-static-public-order", "WriteLine(K.F()); static public class K { static public int F() => 6; }\n", "" };
        yield return new object[] { "o2-a24-readonly-indexer-struct", "WriteLine(new S(2)[0]); struct S { private int _x; public S(int x) { _x = x; } public readonly int this[int i] => _x + i; }\n", "" };
        yield return new object[] { "o2-a25-ref-readonly-return", "var s = new S(3); WriteLine(s.Get()); struct S { private int _x; public S(int x) { _x = x; } [System.Diagnostics.CodeAnalysis.UnscopedRef] public readonly ref readonly int Get() => ref _x; }\n", "" };
        yield return new object[] { "o2-a26-internal-nested", "WriteLine(new C.N().V); class C { internal class N { public int V = 1; } }\n", "" };
        yield return new object[] { "o2-a27-public-ctor", "WriteLine(new C(2).V); class C { public int V; public C(int v) { V = v; } }\n", "" };
        yield return new object[] { "o2-a28-private-ctor-factory", "WriteLine(C.Make().V); class C { public int V; private C(int v) { V = v; } public static C Make() => new C(3); }\n", "" };
        yield return new object[] { "o2-a29-readonly-op", "var s = new S(); s += 2; WriteLine(s.X); struct S { public int X; public void operator +=(int a) { X += a; } }\n", "" };
        yield return new object[] { "o2-a30-unsafe-free-static-readonly-struct", "WriteLine(K.Z.X); static class K { public static S Z => new S(1); } readonly struct S { public readonly int X; public S(int x) { X = x; } }\n", "" };
        yield return new object[] { "o3-a01-all-static", "WriteLine(K.F() + K.C); static class K { public const int C = 1; public static int F() => 2; }\n", "" };
        yield return new object[] { "o3-a02-nested-types", "WriteLine(new K.N().V); static class K { public class N { public int V = 3; } }\n", "" };
        yield return new object[] { "o3-a03-ext", "WriteLine(2.Twice()); static class K { public static int Twice(this int x) => x * 2; }\n", "" };
        yield return new object[] { "o3-a04-static-prop", "WriteLine(K.P); static class K { public static int P => 5; }\n", "" };
        yield return new object[] { "o3-a05-static-main", "namespace App; static class Program { static int Main() { WriteLine(1); return 0; } }\n", "" };
        yield return new object[] { "o3-a06-static-generic-method", "WriteLine(K.Id(4)); static class K { public static T Id<T>(T x) => x; }\n", "" };
        yield return new object[] { "o3-r09-return-type", "return 0; static K F() => throw null; static class K { }\n", "VIB000,VIB008" };
        yield return new object[] { "o4-a01-top", "WriteLine(1);\n", "" };
        yield return new object[] { "o4-a02-main", "namespace App; static class Program { static void Main() { WriteLine(1); } }\n", "" };
        yield return new object[] { "o4-a03-int-main", "namespace App; static class Program { static int Main() { return 0; } }\n", "" };
        yield return new object[] { "o4-a04-top-and-type", "WriteLine(new C().X); class C { public int X = 1; }\n", "" };
        yield return new object[] { "o4-a05-main-nonstatic-class", "namespace App; class Program { static void Main() { WriteLine(1); } }\n", "" };
        yield return new object[] { "o4-a06-top-and-main", "WriteLine(1); static class P2 { static void Main() { } }\n", "" };
        yield return new object[] { "o4-a07-local-fn-only", "static void F() { }\n", "" };
        yield return new object[] { "o5-c01-stmt-var", "int k = 4;\nswitch (Mk(k)) { case Some<int>(var v) when v > 5: WriteLine(v); break; case var o: WriteLine(o.IsSome); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c02-stmt-wrapper-designation", "int k = 4;\nswitch (Mk(k)) { case Some<int> s: WriteLine(s.Value); break; case NoneType: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c03-stmt-typed-payload", "int k = 4;\nswitch (Mk(k)) { case Some<int>(int v): WriteLine(v); break; case NoneType: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c04-is-variant-type", "int k = 4;\nOption<int> a = Mk(k);\nif (a is Some<int>) WriteLine(1); else WriteLine(2);\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c05-nested-payload-union-var", "int k = 4;\nOption<Option<int>> a = Some(Mk(k));\nswitch (a) { case Some<Option<int>>(var inner): WriteLine(inner.IsSome); break; case NoneType: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c06-nested-payload-union-variant", "int k = 4;\nOption<Option<int>> a = Some(Mk(k));\nswitch (a) { case Some<Option<int>>(Some<int>(var v)): WriteLine(v); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c07-is-var", "int k = 4;\nOption<int> a = Mk(k);\nif (a is var b) WriteLine(b.IsSome);\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c08-res-err-payload-union-var", "int k = 4;\nswitch (MkR(k)) { case Ok<int>(var v): WriteLine(v); break; case Err<Option<int>>(var e): WriteLine(e.IsSome); break; }\nstatic Result<int, Option<int>> MkR(int a) { if (a > 2) return Ok(a); Option<int> s = Some(a); return Err(s); }\n", "" };
        yield return new object[] { "o5-c09-is-not-variant", "int k = 4;\nOption<int> a = Mk(k);\nif (a is not NoneType) WriteLine(1); else WriteLine(2);\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-c10-int-is-int", "int k = 4;\nif (k is int j) WriteLine(j);\n", "" };
        yield return new object[] { "o5-d05-stmt-or-discard", "int k = 3;\nswitch (k) { case 1 or _: WriteLine(1); break; }\n", "" };
        yield return new object[] { "o5-d07-stmt-paren-discard", "int k = 3;\nswitch (k) { case 1: WriteLine(1); break; case (_): WriteLine(0); break; }\n", "" };
        yield return new object[] { "o5-d08-stmt-const-underscore", "int k = 3;\nconst int _ = 3;\nswitch (k) { case _: WriteLine(1); break; default: WriteLine(0); break; }\n", "VIB000" };
        yield return new object[] { "o5-d10-is-paren-discard", "int k = 3;\nif (k is (_)) WriteLine(1);\n", "" };
        yield return new object[] { "o5-d11-expr-discard-arm", "int k = 3;\nWriteLine(k switch { 1 => 1, _ => 0 });\n", "" };
        yield return new object[] { "o5-d12-stmt-and-discard", "int k = 3;\nswitch (k) { case > 1 and _: WriteLine(1); break; default: WriteLine(0); break; }\n", "VIB000" };
        yield return new object[] { "o5-e01-stmt-nested-discard", "int k = 4;\nswitch (Mk(k)) { case Some<int>(_): WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "" };
        yield return new object[] { "o5-e02-expr-discard-guard", "int k = 3;\nWriteLine(k switch { 1 => 1, _ when k > 2 => 2, _ => 0 });\n", "" };
        yield return new object[] { "o5-e03-stmt-var-discard-guard", "int k = 3;\nswitch (k) { case var _ when k > 2: WriteLine(1); break; default: WriteLine(0); break; }\n", "" };
        yield return new object[] { "o5-u12-stmt-option-property-untyped", "int k = 4;\nswitch (Mk(k)) { case { IsSome: true }: WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }\n", "VIB000" };
    }

    [Theory]
    [MemberData(nameof(ValidControls))]
    public void ValidControlsKeepStage0VibIdSet(string name, string source, string expectedIds)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfDeclarationAccepted", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            string[] expected = expectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(id => id).ToArray();
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(expected, frontend.Diagnostics.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Equal(expected, analyzer.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var self = RunSelf(path);
            Assert.Equal(expected.Length == 0 ? 0 : 1, self.Exit);
            Assert.Equal(expected, Regex.Matches(self.Output + self.Error, @"VIB\d{3}").Select(match => match.Value).Distinct().OrderBy(id => id));
        });
    }

    private static void WithSource(string name, string source, Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-self-declaration-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            action(path);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (int Exit, string Output, string Error) RunSelf(string path)
    {
        Assert.True(File.Exists(Round11AuditAssertions.SelfCompiler), "Build src/vib-self/VibraniumSelfCompiler.csproj before running self-hosted tests.");
        var start = new ProcessStartInfo(Round11AuditAssertions.DotnetExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src") })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Self source validation must terminate within thirty seconds.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
