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

// vib-self valid-C# parity families V1-V6 (docs/SPEC.md §4 "C# validity"): on a C#-valid program vib-self reports
// exactly Stage-0's VIB ID set, and an accepted program runs identically managed, on Stage-0 and on vib-self.
// - V1/V2: an rvalue argument reaches an `in` parameter through an implicit conversion or a collection expression.
// - V3: a struct member may return a reference or view through a class-typed field of `this`.
// - V4: the value of an assignment expression moves a non-Copy Span/OptionMut destination (VIB001 on a later use).
// - V5: whole-union arms and case labels (`not NoneType when ...`, `A or B`, `var o`) of Option/Result switches test the
//   union without consuming it, so later arms still see the payload; switch statements no longer skip such labels.
// - V6: an in-place `is`/`is not` match of a linear union place is {VIB005,VIB006} on every frontend.
public sealed class Round12SelfValidParityTests
{
    public static IEnumerable<object[]> ValidPrograms()
    {
        yield return new object[] { "v12-A07m-in-param-conversion-noview", "WriteLine(Has(Some(new Node(7))));\n\nstatic bool Has(in Option<Node> o) => o.IsSome;\nsealed class Node(int v) { public int V = v; }\n", "" };
        yield return new object[] { "v12-A07n-byvalue-param-conversion", "WriteLine(Has(Some(new Node(7))));\n\nstatic bool Has(Option<Node> o) => o.IsSome;\nsealed class Node(int v) { public int V = v; }\n", "" };
        yield return new object[] { "v12-A07o-in-param-conversion-int", "WriteLine(Has(Some(7)));\n\nstatic bool Has(in Option<int> o) => o.IsSome;\n", "" };
        yield return new object[] { "v12-A07p-in-param-conversion-var-result", "bool b = Peek(Some(new Node(7))).IsSome;\nWriteLine(b);\n\nstatic OptionRef<Node> Peek(in Option<Node> o) => o.AsRef();\nsealed class Node(int v) { public int V = v; }\n", "" };
        yield return new object[] { "v12-D02c-collexpr-in-param-expr", "WriteLine(View([5, 6, 7])[1]);\n\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\n", "" };
        yield return new object[] { "v12-D02k-collexpr-byvalue-param", "WriteLine(Sum([5, 6, 7]));\n\nstatic int Sum(Vec<int> v) => v[0] + v[2];\n", "" };
        yield return new object[] { "v12-D02l-collexpr-in-param-noview", "WriteLine(First([5, 6, 7]));\n\nstatic int First(in Vec<int> v) => v[0];\n", "" };
        yield return new object[] { "v12-D02m-collexpr-in-param-int-result", "int n = Count([5, 6, 7]);\nWriteLine(n);\n\nstatic int Count(in Vec<int> v) => v.Count;\n", "" };
        yield return new object[] { "v12-D02-collexpr-in-param-store", "var s = View([5, 6, 7]);\nWriteLine(s[1]);\n\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\n", "VIB007" };
        yield return new object[] { "v12-v1a01-long-var", "int k = 5; WriteLine(Twice(k)); WriteLine(k); static long Twice(in long v) => v * 2;\n", "" };
        yield return new object[] { "v12-v1a03-instance", "var c = new C(); WriteLine(c.Has(Some(3))); sealed class C { public bool Has(in Option<int> o) => o.IsSome; }\n", "" };
        yield return new object[] { "v12-v1a04-ctor", "var c = new C(Some(3)); WriteLine(c.V); sealed class C { public int V; public C(in Option<int> o) { V = o.IsSome ? 1 : 0; } }\n", "" };
        yield return new object[] { "v12-v1a05-literal-long", "WriteLine(Twice(21)); static long Twice(in long v) => v * 2;\n", "" };
        yield return new object[] { "v12-v1a06-move-source", "Vec<int> v = [1, 2]; Some<Vec<int>> s = Some(v); WriteLine(Count(s)); static int Count(in Option<Vec<int>> o) => o is Some<Vec<int>>(var x) ? x.Count : 0;\n", "" };
        yield return new object[] { "v12-v1a09-explicit-in-identity", "long k = 5; WriteLine(Twice(in k)); static long Twice(in long v) => v * 2;\n", "" };
        yield return new object[] { "v12-v2a01-span-in", "WriteLine(Sum([1, 2, 3])); static int Sum(in ReadOnlySpan<int> s) { int t = 0; foreach (int x in s) t += x; return t; }\n", "" };
        yield return new object[] { "v12-v1r01-move-source-use", "Vec<int> v = [1, 2]; Some<Vec<int>> s = Some(v); WriteLine(Count(s)); WriteLine(Count(s)); static int Count(in Option<Vec<int>> o) => o is Some<Vec<int>>(var x) ? x.Count : 0;\n", "VIB001" };
        yield return new object[] { "v3-C10c-structtemp-ref-method-expr", "WriteLine(MakeBox(7).First());\n\nstatic Box MakeBox(int v) => new Box(v);\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-C13-struct-local-ref-method-control", "var b = MakeBox(7);\nref int r = ref b.First();\nr = r + 1;\nWriteLine(b.H.Items[0]);\n\nstatic Box MakeBox(int v) => new Box(v);\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-C15c-classtemp-view-method-expr", "WriteLine(MakeHolder(7).View()[1]);\n\nstatic Holder MakeHolder(int v) => new Holder(v);\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); }\n", "" };
        yield return new object[] { "v3-C16c-structtemp-view-property-expr", "WriteLine(MakeBox(7).Items[1]);\n\nstatic Box MakeBox(int v) => new Box(v);\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ReadOnlySpan<int> Items => H.Items.AsReadOnlySpan(); }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-C17-struct-ref-method-minimal", "var b = new Box(7);\nWriteLine(b.First());\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-C18-struct-readonly-ref-method-minimal", "var b = new Box(7);\nWriteLine(b.First());\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ref readonly int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-C10-structtemp-ref-method-store", "ref int r = ref MakeBox(7).First();\nr = r + 1;\nWriteLine(r);\n\nstatic Box MakeBox(int v) => new Box(v);\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "VIB007" };
        yield return new object[] { "v3-C11-structtemp-field-vec-span-store", "ReadOnlySpan<int> s = MakeBox(7).Items.AsReadOnlySpan();\nWriteLine(s[1]);\n\nstatic Option<Node> MakeNode(int v) => Some(new Node(v));\nstatic Box MakeBox(int v) => new Box(v);\nsealed class Node(int v) { public int V = v; }\nstruct Box { public Option<Node> Slot; public Vec<int> Items; public Box(int v) { Slot = Some(new Node(v)); Items = [v, v + 1]; } }\n", "VIB007" };
        yield return new object[] { "v3-C12-structnew-ref-method-store", "ref int r = ref new Box(7).First();\nWriteLine(r);\n\nstatic Box MakeBox(int v) => new Box(v);\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "VIB007" };
        yield return new object[] { "v3-C15-classtemp-view-method-store", "ReadOnlySpan<int> s = MakeHolder(7).View();\nWriteLine(s[1]);\n\nstatic Holder MakeHolder(int v) => new Holder(v);\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); }\n", "VIB007" };
        yield return new object[] { "v3-C16-structtemp-view-property-store", "ReadOnlySpan<int> s = MakeBox(7).Items;\nWriteLine(s[1]);\n\nstatic Box MakeBox(int v) => new Box(v);\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ReadOnlySpan<int> Items => H.Items.AsReadOnlySpan(); }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "VIB007" };
        yield return new object[] { "v3-C14-classtemp-ref-indexer-store", "ref int r = ref MakeHolder(7)[1];\nWriteLine(r);\n\nstatic Holder MakeHolder(int v) => new Holder(v);\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } [Mut] public ref int this[int i] => ref Items[i]; }\n", "VIB004,VIB007" };
        yield return new object[] { "v3-C14c-classtemp-ref-indexer-expr", "WriteLine(MakeHolder(7)[1]);\n\nstatic Holder MakeHolder(int v) => new Holder(v);\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } [Mut] public ref int this[int i] => ref Items[i]; }\n", "VIB004" };
        yield return new object[] { "v3-v3a01-view-prop", "var b = new Box(7); WriteLine(b.Items[1]); struct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ReadOnlySpan<int> Items => H.Items.AsReadOnlySpan(); } sealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-v3a02-nested-struct", "var o = new Outer(7); WriteLine(o.First()); struct Outer { public Box B; public Outer(int v) { B = new Box(v); } public ref int First() => ref B.H.Items[0]; } struct Box { public Holder H; public Box(int v) { H = new Holder(v); } } sealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "" };
        yield return new object[] { "v3-v3r03-class-field-escape-store", "ref int r = ref Make(); WriteLine(r); static ref int Make() { var b = new Box(7); return ref b.First(); } struct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; } sealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "VIB003,VIB007" };
        yield return new object[] { "v4-nb45_span_param_empty_assignment_value", "Span<int> x = [5, 6, 7];\nWriteLine(G(x));\nstatic int G(Span<int> p) { return (p = []).Length + p.Length; }\n", "VIB001" };
        yield return new object[] { "v4-x01_span_local_assignment_value", "Span<int> s = [1];\nint n = (s = []).Length;\nWriteLine(n + s.Length);\n", "VIB001" };
        yield return new object[] { "v4-x02_vec_local_assignment_value", "Vec<int> v = [1];\nint n = (v = [2, 3]).Count;\nWriteLine(n + v.Count);\n", "VIB001" };
        yield return new object[] { "v4-x03_span_param_assignment_value_index", "Span<int> x = [5, 6, 7];\nWriteLine(G(x));\nstatic int G(Span<int> p) { int n = (p = []).Length; return n + p.Length; }\n", "VIB001" };
        yield return new object[] { "v4-v4r02-call-arg", "Vec<int> a = [1, 2]; Span<int> s = a.AsSpan(); WriteLine(Len(s = [])); WriteLine(s.Length); static int Len(Span<int> x) => x.Length;\n", "VIB001" };
        yield return new object[] { "v4-v4a01-ros", "Vec<int> a = [1, 2]; Vec<int> b = [3]; ReadOnlySpan<int> s = a.AsReadOnlySpan(); int n = (s = b.AsReadOnlySpan()).Length; WriteLine(n + s.Length);\n", "" };
        yield return new object[] { "v4-v4a02-no-later-use", "Vec<int> a = [1, 2]; Span<int> s = a.AsSpan(); int n = (s = []).Length; WriteLine(n);\n", "" };
        yield return new object[] { "v4-v4a03-reassigned", "Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); int n = (s = []).Length; s = b.AsSpan(); WriteLine(n + s.Length);\n", "" };
        yield return new object[] { "v4-v4a04-stmt", "Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); s = b.AsSpan(); WriteLine(s.Length);\n", "" };
        yield return new object[] { "v4-v4a05-int", "int x = 1; int n = (x = 5) + x; WriteLine(n);\n", "" };
        yield return new object[] { "v4-v4a06-optionmut-no-later", "Option<Node> o = Some(new Node(1)); Option<Node> p = Some(new Node(2)); OptionMut<Node> m = o.AsMut(); bool b = (m = p.AsMut()).IsSome; WriteLine(b); sealed class Node(int v) { public int V = v; }\n", "" };
        yield return new object[] { "v4-v4a07-loop-stmt", "Vec<int> a = [1, 2]; Span<int> s = a.AsSpan(); int n = 0; for (int i = 0; i < 2; i = i + 1) { s = a.AsSpan(); n += s.Length; } WriteLine(n);\n", "" };
        yield return new object[] { "v4-v4a08-void-arrow", "Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); Fill(ref s, b.AsSpan()); WriteLine(s.Length); static void Fill(ref Span<int> d, Span<int> x) => d = x;\n", "VIB000" };
        yield return new object[] { "v5-a01-not-none-guard-false", "int k = 3;\nint n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a02-not-none-guard-true", "int k = 9;\nint n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a04-result-not-err-guard", "int k = 3;\nint n = MakeRes(7) switch { not Err<int> when k > 5 => 1, Ok<Vec<int>>(var v) => v.Count + v[0], Err<int>(var e) => e };\nWriteLine(n);\nstatic Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }\n", "" };
        yield return new object[] { "v5-a05-result-not-ok-guard", "int k = 3;\nint n = MakeRes(7) switch { not Ok<int> when k > 5 => 1, Ok<int>(var v) => v, Err<Vec<int>>(var e) => e.Count + e[0] };\nWriteLine(n);\nstatic Result<int, Vec<int>> MakeRes(int a) { Vec<int> v = [a, a + 2]; return Err(v); }\n", "" };
        yield return new object[] { "v5-a07-not-none-loop", "int total = 0;\nfor (int k = 0; k < 4; k++)\n{\n    total += MakeOptVec(k) switch { not NoneType when k > 2 => 100, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\n}\nWriteLine(total);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a == 1) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a08-two-fallbacks", "int k = 3;\nint n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, not NoneType when k > 4 => 2, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a09-not-none-string", "int k = 3;\nint n = MakeOpt(7) switch { not NoneType when k > 5 => 1, Some<StringBuf>(var s) => s.Length, _ => 0 };\nWriteLine(n);\nstatic Option<StringBuf> MakeOpt(int a) { StringBuf s = new StringBuf(); s.Append(\"abc\"); return Some(s); }\n", "" };
        yield return new object[] { "v5-a10-place-scrutinee", "int k = 3;\nOption<Vec<int>> o = MakeOptVec(7);\nint n = o switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a11-not-none-no-guard-then-some", "int n = MakeOptVec(7) switch { not NoneType => 1, _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a12-or-pattern-guard", "int k = 3;\nint n = MakeOptVec(7) switch { Some<Vec<int>> or NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a14-place-stmt", "int k = 3;\nOption<Vec<int>> o = MakeOptVec(7);\nint n = 0;\nswitch (o) { case not NoneType when k > 5: n = 1; break; case Some<Vec<int>>(var v): n = v.Count + v[0]; break; default: n = 0; break; }\nWriteLine(n);\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a16-place-not-none-use-after", "int k = 9;\nOption<Vec<int>> o = MakeOptVec(7);\nint n = o switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-a17-place-result-not-err", "int k = 3;\nResult<Vec<int>, int> r = MakeRes(7);\nint n = r switch { not Err<int> when k > 5 => 1, Ok<Vec<int>>(var v) => v.Count + v[0], Err<int>(var e) => e };\nWriteLine(n);\nstatic Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }\n", "" };
        yield return new object[] { "v5-a06-var-guard", "int k = 3;\nint n = MakeOptVec(7) switch { var o when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "VIB001" };
        yield return new object[] { "v5-r01-use-after-arm-move", "int k = 3;\nVec<int> keep = [1];\nint n = MakeOptVec(7) switch { not NoneType when Take(keep) > 5 => 1, Some<Vec<int>>(var v) => v.Count + keep.Count, _ => 0 };\nWriteLine(n);\nstatic int Take(Vec<int> v) => v.Count;\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "VIB001" };
        yield return new object[] { "v5-s01-not-none-guard-k3", "int k = 3;\nswitch (MakeOptVec(7)) { case not NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s01-not-none-guard-k9", "int k = 9;\nswitch (MakeOptVec(7)) { case not NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s02-place-not-none-guard-k3", "int k = 3;\nOption<Vec<int>> o = MakeOptVec(7);\nswitch (o) { case not NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s02-place-not-none-guard-k9", "int k = 9;\nOption<Vec<int>> o = MakeOptVec(7);\nswitch (o) { case not NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s03-or-guard-k3", "int k = 3;\nswitch (MakeOptVec(7)) { case Some<Vec<int>> or NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s03-or-guard-k9", "int k = 9;\nswitch (MakeOptVec(7)) { case Some<Vec<int>> or NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s04-result-not-err-guard-k3", "int k = 3;\nswitch (MakeRes(7)) { case not Err<int> when k > 5: WriteLine(1); break; case Ok<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; case Err<int>(var e): WriteLine(e); break; }\nstatic Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }\n", "" };
        yield return new object[] { "v5-s04-result-not-err-guard-k9", "int k = 9;\nswitch (MakeRes(7)) { case not Err<int> when k > 5: WriteLine(1); break; case Ok<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; case Err<int>(var e): WriteLine(e); break; }\nstatic Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }\n", "" };
        yield return new object[] { "v5-s05-not-some-none-k3", "int k = 3;\nswitch (MakeOptVec(k)) { case not Some<Vec<int>>: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s05-not-some-none-k9", "int k = 9;\nswitch (MakeOptVec(k)) { case not Some<Vec<int>>: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s06-not-none-noguard-k3", "int k = 3;\nswitch (MakeOptVec(k)) { case not NoneType: WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s06-not-none-noguard-k9", "int k = 9;\nswitch (MakeOptVec(k)) { case not NoneType: WriteLine(1); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s07-expr-not-some-k3", "int k = 3;\nint n = MakeOptVec(k) switch { not Some<Vec<int>> => 1, Some<Vec<int>>(var v) => v.Count + v[0] };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s07-expr-not-some-k9", "int k = 9;\nint n = MakeOptVec(k) switch { not Some<Vec<int>> => 1, Some<Vec<int>>(var v) => v.Count + v[0] };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s08-int-option-not-none-k3", "int k = 3;\nswitch (MakeOpt(k)) { case not NoneType when k > 5: WriteLine(1); break; case Some<int>(var v): WriteLine(v); break; default: WriteLine(0); break; }\nstatic Option<int> MakeOpt(int a) { if (a < 0) return None; return Some(a * 2); }\n", "" };
        yield return new object[] { "v5-s08-int-option-not-none-k9", "int k = 9;\nswitch (MakeOpt(k)) { case not NoneType when k > 5: WriteLine(1); break; case Some<int>(var v): WriteLine(v); break; default: WriteLine(0); break; }\nstatic Option<int> MakeOpt(int a) { if (a < 0) return None; return Some(a * 2); }\n", "" };
        yield return new object[] { "v5-s09-var-guard-only-k3", "int k = 3;\nswitch (MakeOptVec(7)) { case var o when k > 5: WriteLine(o.IsSome); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s09-var-guard-only-k9", "int k = 9;\nswitch (MakeOptVec(7)) { case var o when k > 5: WriteLine(o.IsSome); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s11-place-not-some-k3", "int k = 3;\nOption<Vec<int>> o = MakeOptVec(k);\nswitch (o) { case not Some<Vec<int>>: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; }\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s11-place-not-some-k9", "int k = 9;\nOption<Vec<int>> o = MakeOptVec(k);\nswitch (o) { case not Some<Vec<int>>: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; }\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s12-result-err-payload-k3", "int k = 3;\nswitch (MakeRes(k)) { case not Ok<int> when k > 5: WriteLine(1); break; case Ok<int>(var v): WriteLine(v); break; case Err<Vec<int>>(var e): WriteLine(e.Count + e[0]); break; }\nstatic Result<int, Vec<int>> MakeRes(int a) { if (a > 100) return Ok(a); Vec<int> v = [a, a + 2]; return Err(v); }\n", "" };
        yield return new object[] { "v5-s12-result-err-payload-k9", "int k = 9;\nswitch (MakeRes(k)) { case not Ok<int> when k > 5: WriteLine(1); break; case Ok<int>(var v): WriteLine(v); break; case Err<Vec<int>>(var e): WriteLine(e.Count + e[0]); break; }\nstatic Result<int, Vec<int>> MakeRes(int a) { if (a > 100) return Ok(a); Vec<int> v = [a, a + 2]; return Err(v); }\n", "" };
        yield return new object[] { "v5-s13-loop-continue-k3", "int k = 3;\nint total = 0;\nfor (int i = 0; i < 4; i++)\n{\n    switch (MakeOptVec(i)) { case not NoneType when i > 2 && k > 5: total += 100; continue; case Some<Vec<int>>(var v): total += v.Count + v[0]; break; default: total += 1000; break; }\n    total += 1;\n}\nWriteLine(total);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a == 1) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s13-loop-continue-k9", "int k = 9;\nint total = 0;\nfor (int i = 0; i < 4; i++)\n{\n    switch (MakeOptVec(i)) { case not NoneType when i > 2 && k > 5: total += 100; continue; case Some<Vec<int>>(var v): total += v.Count + v[0]; break; default: total += 1000; break; }\n    total += 1;\n}\nWriteLine(total);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a == 1) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s17-var-discard-guard-then-some-k3", "int k = 3;\nswitch (MakeOptVec(7)) { case var _ when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s19-guard-assigns-k3", "int k = 3;\nint x;\nswitch (MakeOptVec(k)) { case not NoneType when k > 5: x = 1; break; case Some<Vec<int>>(var v): x = v.Count; break; default: x = 2; break; }\nWriteLine(x);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-s16-var-guard-then-some-k3", "int k = 3;\nswitch (MakeOptVec(7)) { case var o when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "VIB001" };
        yield return new object[] { "v5-s18-remainder-only-k3", "int k = 3;\nint x;\nswitch (MakeOptVec(k)) { case not Some<Vec<int>> and not NoneType: x = 1; break; case Some<Vec<int>>(var v): x = v.Count; break; case NoneType: x = 0; break; }\nWriteLine(x);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "VIB000" };
        yield return new object[] { "v5-N26-switch-stmt-not-none-guard-then-some", "int k = 3;\nswitch (MakeOptVec(7))\n{\n    case not NoneType when k > 5:\n    {\n        WriteLine(1);\n        break;\n    }\n    case Some<Vec<int>>(var v):\n    {\n        WriteLine(v.Count + v[0]);\n        break;\n    }\n    default:\n    {\n        WriteLine(0);\n        break;\n    }\n}\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v5-N27-switch-expr-not-none-guard-then-some", "int k = 3;\nint n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "" };
        yield return new object[] { "v6-L15-isnot-place-linear", "Run(1);\nstatic void Run(int k)\n{\n    Option<R> o = Open(k);\n    if (o is not Some<R>(var r)) return;\n    using var rr = r;\n    WriteLine(rr.Id);\n}\nstatic Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }\nsealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "VIB005,VIB006" };
        yield return new object[] { "v6-L15p-is-place-linear", "Run(1);\nstatic void Run(int k)\n{\n    Option<R> o = Open(k);\n    if (o is Some<R>(var r))\n    {\n        using var rr = r;\n        WriteLine(rr.Id);\n    }\n}\nstatic Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }\nsealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "VIB005,VIB006" };
    }

    [Theory]
    [MemberData(nameof(ValidPrograms))]
    public void ValidProgramsKeepStage0VibIdSet(string name, string source, string expectedIds)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfValidParity", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            string[] expected = expectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(id => id).ToArray();
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(expected, frontend.Diagnostics.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Equal(expected, analyzer.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var self = RunSelf(path);
            Assert.Equal(expected.Length == 0 ? 0 : 1, self.Exit);
            Assert.Equal(expected, SelfIds(self));
        });
    }

    public static IEnumerable<object[]> AssignmentValueMoves()
    {
        yield return new object[] { "v4-v4r01-view-src", "Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); int n = (s = b.AsSpan()).Length; WriteLine(n); WriteLine(s.Length);\n" };
        yield return new object[] { "v4-v4r03-chain", "Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); Span<int> t = a.AsSpan(); t = s = b.AsSpan(); WriteLine(t.Length); WriteLine(s.Length);\n" };
        yield return new object[] { "v4-v4r04-index", "Vec<int> a = [1, 2]; Vec<int> b = [3, 4]; Span<int> s = a.AsSpan(); int n = (s = b.AsSpan())[0]; WriteLine(n + s[1]);\n" };
        yield return new object[] { "v4-v4r05-optionmut", "Option<Node> o = Some(new Node(1)); Option<Node> p = Some(new Node(2)); OptionMut<Node> m = o.AsMut(); bool b = (m = p.AsMut()).IsSome; WriteLine(b); WriteLine(m.IsSome); sealed class Node(int v) { public int V = v; }\n" };
    }

    // vib-self reports exactly VIB001. Stage-0 and the Analyzer agree with each other and report VIB001; their extra
    // VIB003 cascade on these shapes is removed separately on the Roslyn side.
    [Theory]
    [MemberData(nameof(AssignmentValueMoves))]
    public void AssignmentValueMovesSpanDestination(string name, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfValidParityMove", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id).ToArray();
            Assert.Contains("VIB001", frontend);
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Equal(frontend, analyzer.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Equal(new[] { "VIB001" }, SelfIds(self));
        });
    }

    public static IEnumerable<object[]> InvalidCSharp()
    {
        yield return new object[] { "v12-v1a02-generic", "CS0411", "VIB000", "WriteLine(Has(Some(7))); static bool Has<T>(in Option<T> o) => o.IsSome;\n" };
        yield return new object[] { "v12-v1a07-overload-pref", "CS0128", "VIB000", "WriteLine(F(1)); static int F(in long v) => 1; static int F(int v) => 2;\n" };
        yield return new object[] { "v12-v1a08-overload-in-vs-val", "CS0128", "VIB000", "WriteLine(F(1)); static int F(in long v) => 1; static int F(long v) => 2;\n" };
        yield return new object[] { "v12-v1r02-explicit-in-conv", "CS1503", "VIB000", "int k = 5; WriteLine(Twice(in k)); static long Twice(in long v) => v * 2;\n" };
        yield return new object[] { "v3-v3r01-struct-own-field", "CS8170", "VIB007", "var b = new Box(7); WriteLine(b.First()); struct Box { public int X; public Box(int v) { X = v; } public ref int First() => ref X; }\n" };
        yield return new object[] { "v3-v3r02-struct-vec-field", "CS8170", "VIB007", "var b = new Box(7); WriteLine(b.First()); struct Box { public Vec<int> Items; public Box(int v) { Items = [v]; } public ref int First() => ref Items[0]; }\n" };
        yield return new object[] { "v5-r02-unassigned-after-guard", "CS0165", "VIB001", "int k = 3;\nint x;\nswitch (MakeOptVec(k)) { case not NoneType when k > 5: x = 1; break; case Some<Vec<int>>(var v): break; default: x = 2; break; }\nWriteLine(x);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n" };
    }

    [Theory]
    [MemberData(nameof(InvalidCSharp))]
    public void NeighbouringInvalidCSharpStaysRejected(string name, string roslynId, string selfId, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfValidParityRejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id == roslynId);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Contains($"error {selfId}:", self.Output + self.Error, StringComparison.Ordinal);
        });
    }

    public static IEnumerable<object[]> RunPrograms()
    {
        yield return new object[] { "v12-A07m-in-param-conversion-noview", "WriteLine(Has(Some(new Node(7))));\n\nstatic bool Has(in Option<Node> o) => o.IsSome;\nsealed class Node(int v) { public int V = v; }\n", "True\n" };
        yield return new object[] { "v12-A07o-in-param-conversion-int", "WriteLine(Has(Some(7)));\n\nstatic bool Has(in Option<int> o) => o.IsSome;\n", "True\n" };
        yield return new object[] { "v12-D02c-collexpr-in-param-expr", "WriteLine(View([5, 6, 7])[1]);\n\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\n", "6\n" };
        yield return new object[] { "v12-D02m-collexpr-in-param-int-result", "int n = Count([5, 6, 7]);\nWriteLine(n);\n\nstatic int Count(in Vec<int> v) => v.Count;\n", "3\n" };
        yield return new object[] { "v12-v1a01-long-var", "int k = 5; WriteLine(Twice(k)); WriteLine(k); static long Twice(in long v) => v * 2;\n", "10\n5\n" };
        yield return new object[] { "v12-v1a05-literal-long", "WriteLine(Twice(21)); static long Twice(in long v) => v * 2;\n", "42\n" };
        yield return new object[] { "v12-v1a06-move-source", "Vec<int> v = [1, 2]; Some<Vec<int>> s = Some(v); WriteLine(Count(s)); static int Count(in Option<Vec<int>> o) => o is Some<Vec<int>>(var x) ? x.Count : 0;\n", "2\n" };
        yield return new object[] { "v12-v2a01-span-in", "WriteLine(Sum([1, 2, 3])); static int Sum(in ReadOnlySpan<int> s) { int t = 0; foreach (int x in s) t += x; return t; }\n", "6\n" };
        yield return new object[] { "v3-C10c-structtemp-ref-method-expr", "WriteLine(MakeBox(7).First());\n\nstatic Box MakeBox(int v) => new Box(v);\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "7\n" };
        yield return new object[] { "v3-C13-struct-local-ref-method-control", "var b = MakeBox(7);\nref int r = ref b.First();\nr = r + 1;\nWriteLine(b.H.Items[0]);\n\nstatic Box MakeBox(int v) => new Box(v);\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "8\n" };
        yield return new object[] { "v3-C16c-structtemp-view-property-expr", "WriteLine(MakeBox(7).Items[1]);\n\nstatic Box MakeBox(int v) => new Box(v);\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ReadOnlySpan<int> Items => H.Items.AsReadOnlySpan(); }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "8\n" };
        yield return new object[] { "v3-C17-struct-ref-method-minimal", "var b = new Box(7);\nWriteLine(b.First());\n\nstruct Box { public Holder H; public Box(int v) { H = new Holder(v); } public ref int First() => ref H.Items[0]; }\nsealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "7\n" };
        yield return new object[] { "v3-v3a01-view-prop", "var b = new Box(7); WriteLine(b.Items[1]); struct Box { public Holder H; public Box(int v) { H = new Holder(v); } public readonly ReadOnlySpan<int> Items => H.Items.AsReadOnlySpan(); } sealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "8\n" };
        yield return new object[] { "v3-v3a02-nested-struct", "var o = new Outer(7); WriteLine(o.First()); struct Outer { public Box B; public Outer(int v) { B = new Box(v); } public ref int First() => ref B.H.Items[0]; } struct Box { public Holder H; public Box(int v) { H = new Holder(v); } } sealed class Holder { public Vec<int> Items; public Holder(int v) { Items = [v, v + 1]; } }\n", "7\n" };
        yield return new object[] { "v4-v4a01-ros", "Vec<int> a = [1, 2]; Vec<int> b = [3]; ReadOnlySpan<int> s = a.AsReadOnlySpan(); int n = (s = b.AsReadOnlySpan()).Length; WriteLine(n + s.Length);\n", "2\n" };
        yield return new object[] { "v4-v4a03-reassigned", "Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); int n = (s = []).Length; s = b.AsSpan(); WriteLine(n + s.Length);\n", "1\n" };
        yield return new object[] { "v4-v4a06-optionmut-no-later", "Option<Node> o = Some(new Node(1)); Option<Node> p = Some(new Node(2)); OptionMut<Node> m = o.AsMut(); bool b = (m = p.AsMut()).IsSome; WriteLine(b); sealed class Node(int v) { public int V = v; }\n", "True\n" };
        yield return new object[] { "v4-v4a07-loop-stmt", "Vec<int> a = [1, 2]; Span<int> s = a.AsSpan(); int n = 0; for (int i = 0; i < 2; i = i + 1) { s = a.AsSpan(); n += s.Length; } WriteLine(n);\n", "4\n" };
        yield return new object[] { "v5-N27-switch-expr-not-none-guard-then-some", "int k = 3;\nint n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "9\n" };
        yield return new object[] { "v5-a02-not-none-guard-true", "int k = 9;\nint n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\n" };
        yield return new object[] { "v5-a04-result-not-err-guard", "int k = 3;\nint n = MakeRes(7) switch { not Err<int> when k > 5 => 1, Ok<Vec<int>>(var v) => v.Count + v[0], Err<int>(var e) => e };\nWriteLine(n);\nstatic Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }\n", "9\n" };
        yield return new object[] { "v5-a05-result-not-ok-guard", "int k = 3;\nint n = MakeRes(7) switch { not Ok<int> when k > 5 => 1, Ok<int>(var v) => v, Err<Vec<int>>(var e) => e.Count + e[0] };\nWriteLine(n);\nstatic Result<int, Vec<int>> MakeRes(int a) { Vec<int> v = [a, a + 2]; return Err(v); }\n", "9\n" };
        yield return new object[] { "v5-a07-not-none-loop", "int total = 0;\nfor (int k = 0; k < 4; k++)\n{\n    total += MakeOptVec(k) switch { not NoneType when k > 2 => 100, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\n}\nWriteLine(total);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a == 1) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "106\n" };
        yield return new object[] { "v5-a10-place-scrutinee", "int k = 3;\nOption<Vec<int>> o = MakeOptVec(7);\nint n = o switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "9\n" };
        yield return new object[] { "v5-a16-place-not-none-use-after", "int k = 9;\nOption<Vec<int>> o = MakeOptVec(7);\nint n = o switch { not NoneType when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\nTrue\n" };
        yield return new object[] { "v5-s01-not-none-guard-k9", "int k = 9;\nswitch (MakeOptVec(7)) { case not NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\n" };
        yield return new object[] { "v5-s02-place-not-none-guard-k9", "int k = 9;\nOption<Vec<int>> o = MakeOptVec(7);\nswitch (o) { case not NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\nTrue\n" };
        yield return new object[] { "v5-s03-or-guard-k9", "int k = 9;\nswitch (MakeOptVec(7)) { case Some<Vec<int>> or NoneType when k > 5: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; default: WriteLine(0); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\n" };
        yield return new object[] { "v5-s04-result-not-err-guard-k9", "int k = 9;\nswitch (MakeRes(7)) { case not Err<int> when k > 5: WriteLine(1); break; case Ok<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; case Err<int>(var e): WriteLine(e); break; }\nstatic Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }\n", "1\n" };
        yield return new object[] { "v5-s05-not-some-none-k3", "int k = 3;\nswitch (MakeOptVec(k)) { case not Some<Vec<int>>: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; }\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\n" };
        yield return new object[] { "v5-s08-int-option-not-none-k9", "int k = 9;\nswitch (MakeOpt(k)) { case not NoneType when k > 5: WriteLine(1); break; case Some<int>(var v): WriteLine(v); break; default: WriteLine(0); break; }\nstatic Option<int> MakeOpt(int a) { if (a < 0) return None; return Some(a * 2); }\n", "1\n" };
        yield return new object[] { "v5-s11-place-not-some-k3", "int k = 3;\nOption<Vec<int>> o = MakeOptVec(k);\nswitch (o) { case not Some<Vec<int>>: WriteLine(1); break; case Some<Vec<int>>(var v): WriteLine(v.Count + v[0]); break; }\nWriteLine(o.IsSome);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "1\nFalse\n" };
        yield return new object[] { "v5-s12-result-err-payload-k3", "int k = 3;\nswitch (MakeRes(k)) { case not Ok<int> when k > 5: WriteLine(1); break; case Ok<int>(var v): WriteLine(v); break; case Err<Vec<int>>(var e): WriteLine(e.Count + e[0]); break; }\nstatic Result<int, Vec<int>> MakeRes(int a) { if (a > 100) return Ok(a); Vec<int> v = [a, a + 2]; return Err(v); }\n", "5\n" };
        yield return new object[] { "v5-s13-loop-continue-k9", "int k = 9;\nint total = 0;\nfor (int i = 0; i < 4; i++)\n{\n    switch (MakeOptVec(i)) { case not NoneType when i > 2 && k > 5: total += 100; continue; case Some<Vec<int>>(var v): total += v.Count + v[0]; break; default: total += 1000; break; }\n    total += 1;\n}\nWriteLine(total);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a == 1) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "1109\n" };
        yield return new object[] { "v5-s19-guard-assigns-k3", "int k = 3;\nint x;\nswitch (MakeOptVec(k)) { case not NoneType when k > 5: x = 1; break; case Some<Vec<int>>(var v): x = v.Count; break; default: x = 2; break; }\nWriteLine(x);\nstatic Option<Vec<int>> MakeOptVec(int a) { if (a < 5) return None; Vec<int> v = [a, a + 1]; return Some(v); }\n", "2\n" };
    }

    // Managed, Stage-0 native and vib-self native (-O3, with and without ASan/LSan) print the same output.
    [Theory]
    [MemberData(nameof(RunPrograms))]
    public Task ValidProgramsRunIdentically(string name, string source, string expected)
    {
        Assert.False(string.IsNullOrEmpty(name));
        return Round11AuditAssertions.AssertDifferential(source, expected, includeUnoptimized: false);
    }

    private static string[] SelfIds((int Exit, string Output, string Error) self) =>
        Regex.Matches(self.Output + self.Error, @"VIB\d{3}").Select(match => match.Value).Distinct().OrderBy(id => id).ToArray();

    private static void WithSource(string name, string source, Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-self-valid-parity-{name}-{Guid.NewGuid():N}");
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
