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

// vib-self validity families S1-S6 (docs/SPEC.md §4 "C# validity"): vib-self must reject what C# rejects, and keep
// Stage-0's VIB ID set on every C#-valid program.
// - S1: a field of a struct value returned by a call, property or indexer is not a variable (CS1612/CS0131).
// - S2: the natural type of `?:` and its conversion to the target (CS0266/CS0173/CS0029/CS1503).
// - S3: unreachable code is still bound and type-checked (CS0103/CS0246/CS0029/...).
// - S4: the declared type of an `out` variable must bind and be exactly the parameter type (CS0246/CS1503).
// - S5: `ref` is not an operand (CS1525/CS8171/...).
// - S6: MethodImplOptions.Async (0x2000) cannot be applied manually (CS9330).
public sealed class Round12SelfValidityFamiliesTests
{
    public static IEnumerable<object[]> InvalidCSharp()
    {
        yield return new object[] { "s1-r01-assign", "CS1612", "VIB000", "Make().X = 1; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r02-compound", "CS1612", "VIB000", "Make().X += 1; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r03-incpost", "CS1612", "VIB000", "Make().X++; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r04-incpre", "CS1612", "VIB000", "--Make().X; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r05-nested", "CS1612", "VIB000", "Make().In.Y = 2; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r06-propres", "CS1612", "VIB000", "var h = new Holder(); h.PB.X = 3; WriteLine(h.B.X);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r07-propset-on-value", "CS1612", "VIB000", "Make().P = 3; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r08-refarg", "CS1612", "VIB000", "F(ref Make().X); WriteLine(1); static Box Make() => new Box(1); static void F(ref int a) { a = 2; }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r09-outarg", "CS1612", "VIB000", "F(out Make().X); WriteLine(1); static Box Make() => new Box(1); static void F(out int a) { a = 2; }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r10-inarg-explicit", "CS1612", "VIB000", "F(in Make().X); WriteLine(1); static Box Make() => new Box(1); static void F(in int a) { WriteLine(a); }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r11-reflocal", "CS1612", "VIB000", "ref int r = ref Make().X; r = 2; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r12-cond", "CS0131", "VIB000", "bool c = true; Box a = new Box(1); Box b = new Box(2); (c ? a : b).X = 5; WriteLine(a.X);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r13-cast-nested-prop", "CS1612", "VIB000", "var h = new Holder(); h.PB.In.Y = 3; WriteLine(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r14-newexpr", "CS0131", "VIB000", "new Box(1).X = 4; WriteLine(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r15-nested-compound", "CS1612", "VIB000", "Make().In.Y *= 2; WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r16-refreturn", "CS1612", "VIB000", "WriteLine(G()); static ref int G() { return ref Make().X; } static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r17-static-prop", "CS1612", "VIB000", "S.Cur.X = 1; WriteLine(1);\nstatic class S { public static Box Cur => new Box(1); }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r18-thisprop", "CS1612", "VIB000", "var h = new H2(); h.Run(); WriteLine(1);\nclass H2 { public Box PB => new Box(1); [Mut] public void Run() { PB.X = 1; } }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r19-indexer-user", "CS1612", "VIB000", "Make()[0] = 1; WriteLine(1); static IB Make() => new IB(1);\npublic struct IB { public int V; public IB(int v) { V = v; } public int this[int i] { get => V; set => V = value; } }\n" };
        yield return new object[] { "s1-r20-structprop-inc", "CS1612", "VIB000", "var h = new Holder(); h.PB.X++; WriteLine(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s1-r21-ref-assign", "CS1612", "VIB000", "int z = 1; ref int r = ref z; r = ref Make().X; WriteLine(r); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n" };
        yield return new object[] { "s2-r01-byte-int", "CS0266", "VIB000", "bool c = true; byte a = c ? (byte)2 : 1; WriteLine((int)a);\n" };
        yield return new object[] { "s2-r02-compound-byte", "CS0266", "VIB000", "byte a = 7; bool c = true; a += c ? (byte)2 : 1; WriteLine((int)a);\n" };
        yield return new object[] { "s2-r03-short-ints", "CS0266", "VIB000", "bool c = true; short s = c ? 1 : 2; WriteLine(s);\n" };
        yield return new object[] { "s2-r04-sbyte", "CS0266", "VIB000", "bool c = true; sbyte s = c ? (sbyte)1 : 2; WriteLine(s);\n" };
        yield return new object[] { "s2-r05-ushort", "CS0266", "VIB000", "bool c = true; ushort s = c ? (ushort)1 : 2; WriteLine(s);\n" };
        yield return new object[] { "s2-r06-char-int", "CS0266", "VIB000", "bool c = true; char ch = c ? 'a' : 66; WriteLine((int)ch);\n" };
        yield return new object[] { "s2-r07-uint-neg", "CS0266", "VIB000", "bool c = true; uint u = c ? 1 : -1; WriteLine(u);\n" };
        yield return new object[] { "s2-r09-int-long", "CS0266", "VIB000", "bool c = true; int i = c ? 1 : 2L; WriteLine(i);\n" };
        yield return new object[] { "s2-r10-byte-var", "CS0266", "VIB000", "bool c = true; byte b = 3; byte x = c ? b : 1; WriteLine((int)x);\n" };
        yield return new object[] { "s2-r11-arg-byte", "CS1503", "VIB000", "bool c = true; F(c ? (byte)1 : 2); static void F(byte b) { WriteLine((int)b); }\n" };
        yield return new object[] { "s2-r12-return-byte", "CS0266", "VIB000", "WriteLine((int)G(true)); static byte G(bool c) => c ? (byte)1 : 2;\n" };
        yield return new object[] { "s2-r13-ulong-int", "CS0029", "VIB000", "bool c = true; int i = -1; ulong u = c ? 1UL : i; WriteLine(u);\n" };
        yield return new object[] { "s2-r14-var-no-natural", "CS0173", "VIB000", "bool c = true; var r = c ? Ok(1) : Err(2); WriteLine(1);\n" };
        yield return new object[] { "s2-r15-short-sbyte-byte", "CS0029", "VIB000", "bool c = true; byte b = c ? (byte)1 : (sbyte)2; WriteLine((int)b);\n" };
        yield return new object[] { "s2-r16-field-init", "CS0266", "VIB000", "WriteLine((int)new H(true).B); class H { public byte B; public H(bool c) { B = c ? (byte)1 : 2; } }\n" };
        yield return new object[] { "s2-r17-nested-cond", "CS0266", "VIB000", "bool c = true; bool d = false; byte b = c ? (d ? (byte)1 : (byte)2) : 3; WriteLine((int)b);\n" };
        yield return new object[] { "s2-r20-long-ulong", "CS0173", "VIB000", "bool c = true; long l = 1; ulong u2 = 2; var v = c ? l : u2; WriteLine(1);\n" };
        yield return new object[] { "s2-r21-int-float-to-int", "CS0266", "VIB000", "bool c = true; int i = c ? 1 : 1.0f; WriteLine(i);\n" };
        yield return new object[] { "s3-r01-top-after-return", "CS0103", "VIB000", "WriteLine(1); return 0; q = 1;\n" };
        yield return new object[] { "s3-r02-top-after-return-member", "CS0103", "VIB000", "return 0; static int F() => 1; q.Length = q.Length;\n" };
        yield return new object[] { "s3-r03-body-after-return", "CS0103", "VIB000", "WriteLine(F()); static int F() { return 1; q = 2; }\n" };
        yield return new object[] { "s3-r04-after-break", "CS0103", "VIB000", "for (int i = 0; i < 2; i++) { break; q = 1; } WriteLine(1);\n" };
        yield return new object[] { "s3-r05-after-continue", "CS0103", "VIB000", "for (int i = 0; i < 2; i++) { continue; q = 1; } WriteLine(1);\n" };
        yield return new object[] { "s3-r06-switch-after-break", "CS0103", "VIB000", "int x = 1; switch (x) { case 1: { WriteLine(1); break; q = 1; } default: break; }\n" };
        yield return new object[] { "s3-r07-after-return-type", "CS0246", "VIB000", "WriteLine(F()); static int F() { return 1; Zz z = new Zz(); }\n" };
        yield return new object[] { "s3-r08-after-return-call", "CS0103", "VIB000", "WriteLine(F()); static int F() { return 1; Nope(); }\n" };
        yield return new object[] { "s3-r09-after-return-wrongtype", "CS0029", "VIB000", "WriteLine(F()); static int F() { return 1; int x = \"s\"; }\n" };
        yield return new object[] { "s3-r10-after-return-block", "CS0103", "VIB000", "WriteLine(F()); static int F() { return 1; { q = 2; } }\n" };
        yield return new object[] { "s3-r11-after-while-true", "CS0103", "VIB000", "WriteLine(1); while (true) { } q = 1;\n" };
        yield return new object[] { "s3-r12-after-return-if", "CS0103", "VIB000", "WriteLine(F()); static int F() { return 1; if (q) { } }\n" };
        yield return new object[] { "s3-r13-ctor-after-return", "CS0103", "VIB000", "var c = new C(); WriteLine(1); class C { public C() { return; q = 1; } }\n" };
        yield return new object[] { "s3-r14-void-after-return", "CS0103", "VIB000", "F(); static void F() { return; q = 1; }\n" };
        yield return new object[] { "s3-r15-after-return-nested-fn", "CS0103", "VIB000", "F(); static void F() { return; static int G() => q; }\n" };
        yield return new object[] { "s3-r16-after-return-return-type", "CS0029", "VIB000", "WriteLine(F()); static int F() { return 1; return \"x\"; }\n" };
        yield return new object[] { "s3-r17-after-return-wrong-arg", "CS1503", "VIB000", "WriteLine(F()); static int F() { return 1; G(\"x\"); } static void G(int a) {}\n" };
        yield return new object[] { "s3-r18-loop-after-break-wrongtype", "CS0029", "VIB000", "while (true) { break; int x = true; } WriteLine(1);\n" };
        yield return new object[] { "s3-r20-after-panic-unbound", "CS0103", "VIB000", "VibraniumRuntime.Panic(\"x\"); q = 1;\n" };
        yield return new object[] { "s3-r21-do-after-continue", "CS0103", "VIB000", "int i = 0; do { i++; continue; q = 1; } while (i < 2); WriteLine(i);\n" };
        yield return new object[] { "s3-r22-foreach-after-break", "CS0103", "VIB000", "Vec<int> v = [1]; foreach (var e in v) { break; q = e; } WriteLine(1);\n" };
        yield return new object[] { "s3-r23-after-return-in-else", "CS0103", "VIB000", "WriteLine(F(true)); static int F(bool c) { if (c) return 1; else { return 2; q = 3; } }\n" };
        yield return new object[] { "s3-a10-unreachable-mut-shared", "CS8332", "VIB004", "WriteLine(F(new Box(1))); static int F(in Box b) { return 1; b.X = 2; } public struct Box { public int X; public Box(int x) { X = x; } }\n" };
        yield return new object[] { "s3-a14-unreachable-vec-escape", "CS8168", "VIB007", "WriteLine(F()[0]); static Span<int> F() { Vec<int> v = [1]; return default; return v.AsSpan(); }\n" };
        yield return new object[] { "s3-a18-dup-name-unreachable", "CS0136", "VIB000", "WriteLine(F()); static int F() { int a = 1; return a; { int a = 2; } }\n" };
        yield return new object[] { "s4-r01-err-long-for-int", "CS1503", "VIB000", "Result<int, int> r = Err(7); if (r.IsErr(out var v, out Err<long> e)) { WriteLine(e.Error); } return 0;\n" };
        yield return new object[] { "s4-r02-ok-long-for-int", "CS1503", "VIB000", "Result<int, int> r = Ok(7); if (!r.IsErr(out long v, out var e)) { WriteLine(v); } return 0;\n" };
        yield return new object[] { "s4-r03-ok-int-for-long", "CS1503", "VIB000", "Result<long, long> r = Ok(7L); if (!r.IsErr(out int v, out var e)) { WriteLine(v); } return 0;\n" };
        yield return new object[] { "s4-r04-err-int-for-long", "CS1503", "VIB000", "Result<long, long> r = Err(7L); if (r.IsErr(out var v, out Err<int> e)) { WriteLine(e.Error); } return 0;\n" };
        yield return new object[] { "s4-r05-unbound-err", "CS0246", "VIB000", "Result<int, int> r = Ok(1); if (r.IsErr(out var v, out Zz e)) { WriteLine(1); } return 0;\n" };
        yield return new object[] { "s4-r06-unbound-normal", "CS0246", "VIB000", "if (G(out var v, out Zz e)) { WriteLine(1); } return 0; static bool G(out int a, out int b) { a = 1; b = 2; return true; }\n" };
        yield return new object[] { "s4-r07-unbound-in-class", "CS0246", "VIB000", "return 0; static class K { static bool H() { Result<int, int> r = Ok(1); if (r.IsErr(out var v, out Zz e)) { return true; } return false; } }\n" };
        yield return new object[] { "s4-r08-none-wrong", "CS1503", "VIB000", "Option<int> o = Some(1); if (o.IsNone(out long v)) { return 0; } WriteLine(v); return 0;\n" };
        yield return new object[] { "s4-r09-none-unbound", "CS0246", "VIB000", "Option<int> o = Some(1); if (o.IsNone(out Zz v)) { return 0; } return 0;\n" };
        yield return new object[] { "s4-r10-normal-wrong", "CS1503", "VIB000", "G(out long a); WriteLine(a); static void G(out int x) { x = 1; }\n" };
        yield return new object[] { "s4-r11-normal-wrong-struct", "CS1503", "VIB000", "G(out P a); WriteLine(1); static void G(out Q x) { x = new Q(); } struct P { public int A; } struct Q { public int A; }\n" };
        yield return new object[] { "s4-r12-err-plain-type", "CS1503", "VIB000", "Result<int, int> r = Err(7); if (r.IsErr(out var v, out int e)) { WriteLine(e); } return 0;\n" };
        yield return new object[] { "s4-r13-ok-err-wrapper", "CS1503", "VIB000", "Result<int, int> r = Ok(7); if (!r.IsErr(out Ok<int> v, out var e)) { WriteLine(1); } return 0;\n" };
        yield return new object[] { "s4-r14-none-some-wrapper", "CS1503", "VIB000", "Option<int> o = Some(1); if (o.IsNone(out Some<int> v)) { return 0; } return 0;\n" };
        yield return new object[] { "s4-r15-err-vec-elem", "CS1503", "VIB000", "Result<Vec<int>, int> r = Ok<Vec<int>>([1]); if (!r.IsErr(out Vec<long> v, out var e)) { WriteLine(v.Count); } return 0;\n" };
        yield return new object[] { "s4-r16-generic-wrong", "CS1503", "VIB000", "WriteLine(F<int>(Ok(1))); static int F<T>(Result<T, int> r) { if (!r.IsErr(out int v, out var e)) { return 1; } return 0; }\n" };
        yield return new object[] { "s4-r17-err-string-vs-ros", "CS1503", "VIB000", "Result<int, string> r = Err(\"x\"); if (r.IsErr(out var v, out Err<ReadOnlySpan<byte>> e)) { WriteLine(1); } return 0;\n" };
        yield return new object[] { "s4-r18-try-parse-wrong", "CS1503", "VIB000", "if (int.TryParse(\"12\", out long v)) WriteLine(v);\n" };
        yield return new object[] { "s4-r19-typeparam-unbound-normal", "CS0246", "VIB000", "G(out T v); static void G(out int x) { x = 1; }\n" };
        yield return new object[] { "s5-r01-compound", "CS1525", "VIB000", "int x = 1; int y = 2; ref int r = ref x; r += ref y; WriteLine(x);\n" };
        yield return new object[] { "s5-r02-binary", "CS8171", "VIB000", "int x = 1; int y = ref x + 1; WriteLine(y);\n" };
        yield return new object[] { "s5-r03-binary-rhs", "CS1525", "VIB000", "int x = 1; int y = 1 + ref x; WriteLine(y);\n" };
        yield return new object[] { "s5-r04-paren", "CS1525", "VIB000", "int x = 1; ref int r = (ref x); WriteLine(r);\n" };
        yield return new object[] { "s5-r05-expr-stmt", "CS0116", "VIB000", "int x = 1; ref x; WriteLine(x);\n" };
        yield return new object[] { "s5-r06-call-arg-inner", "CS1525", "VIB000", "int x = 1; F(1 + ref x); static void F(int a) { WriteLine(a); }\n" };
        yield return new object[] { "s5-r07-return-paren", "CS1525", "VIB000", "WriteLine(G()); static int G() { int x = 1; return (ref x); }\n" };
        yield return new object[] { "s5-r08-cond-cond", "CS8171", "VIB000", "bool c = true; int x = 1; int y = ref c ? x : 2; WriteLine(y);\n" };
        yield return new object[] { "s5-r09-index", "CS1615", "VIB000", "Vec<int> v = [1]; int i = 0; WriteLine(v[ref i]);\n" };
        yield return new object[] { "s5-r10-switch-arm", "CS1525", "VIB000", "int x = 1; int k = 0; ref int r = ref k switch { _ => ref x }; WriteLine(r);\n" };
        yield return new object[] { "s5-r11-unary", "CS1525", "VIB000", "int x = 1; int y = -ref x; WriteLine(y);\n" };
        yield return new object[] { "s5-r12-mul-compound", "CS1525", "VIB000", "int x = 1; int y = 2; x *= ref y; WriteLine(x);\n" };
        yield return new object[] { "s5-r13-shift-compound", "CS1525", "VIB000", "int x = 1; int y = 2; x <<= ref y; WriteLine(x);\n" };
        yield return new object[] { "s5-r14-assign-chain", "CS8373", "VIB000", "int x = 1; int y = 2; int z = 3; x = y = ref z; WriteLine(x);\n" };
        yield return new object[] { "s5-r15-field-init", "CS8171", "VIB000", "return 0; class C { int a = 1; int b = ref a; }\n" };
        yield return new object[] { "s5-r16-coll", "CS1525", "VIB000", "int x = 1; Vec<int> v = [ref x]; WriteLine(v[0]);\n" };
        yield return new object[] { "s5-r17-cast", "CS1525", "VIB000", "int x = 1; long y = (long)ref x; WriteLine(y);\n" };
        yield return new object[] { "s5-r18-is", "CS8171", "VIB000", "int x = 1; bool b = ref x is 1; WriteLine(b);\n" };
        yield return new object[] { "s5-r19-ref-ref-local", "CS1525", "VIB000", "int x = 1; ref int r = ref ref x; WriteLine(r);\n" };
        yield return new object[] { "s5-r20-interp", "CS1615", "VIB000", "int x = 1; WriteLine(ref x);\n" };
        yield return new object[] { "s5-r21-not", "CS1525", "VIB000", "bool b = true; bool c = !ref b; WriteLine(c);\n" };
        yield return new object[] { "s5-r22-return-ref-binary", "CS8156", "VIB000", "WriteLine(G(1)); static ref int G(ref int x) { return ref x + 1; }\n" };
        yield return new object[] { "s5-r23-plus-plus", "CS1525", "VIB000", "int x = 1; ref int r = ref x; r++; ++ref r; WriteLine(x);\n" };
        yield return new object[] { "s5-r24-cond-false-ref", "", "VIB000", "bool c = true; int a = 1; int b = 2; int v = c ? a : ref b; WriteLine(v);\n" };
        yield return new object[] { "s5-r25-cond-true-ref", "", "VIB000", "bool c = true; int a = 1; int b = 2; int v = c ? ref a : b; WriteLine(v);\n" };
        yield return new object[] { "s6-CS9330-async-8192", "CS9330", "VIB000", "using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(8192)]\nstatic int F() => 1;\n" };
        yield return new object[] { "s6-CS9330-async-enum", "CS9330", "VIB000", "using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(MethodImplOptions.Async)]\nstatic int F() => 1;\n" };
        yield return new object[] { "s6-CS9330-async-member", "CS9330", "VIB000", "using System.Runtime.CompilerServices;\nWriteLine(C.F());\nstatic class C { [MethodImpl((MethodImplOptions)0x2000)] public static int F() => 2; }\n" };
        yield return new object[] { "s6-CS9330-async-or-inlining", "CS9330", "VIB000", "using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.Async)]\nstatic int F() => 1;\n" };
        yield return new object[] { "s6-CS9330-negative", "CS0591", "VIB000", "using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(-1)]\nstatic int F() => 1;\n" };
        yield return new object[] { "s6-r01-shl", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(1 << 13)] static int F() => 1;\n" };
        yield return new object[] { "s6-r02-m300", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(-300)] static int F() => 1;\n" };
        yield return new object[] { "s6-r03-qualified", "CS9330", "VIB000", "WriteLine(F()); [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Async)] static int F() => 1;\n" };
        yield return new object[] { "s6-r04-short", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl((short)0x2000)] static int F() => 1;\n" };
        yield return new object[] { "s6-r05-ctor", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(new C().X); sealed class C { public int X; [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)] public C() { X = 3; } }\n" };
        yield return new object[] { "s6-r06-inner-local", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(G()); static int G() { return F(); [MethodImpl(0x2000 | 0x100)] static int F() => 1; }\n" };
        yield return new object[] { "s6-r07-struct-method", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(new S().F()); struct S { [MethodImpl(8192 + 256)] public readonly int F() => 1; }\n" };
        yield return new object[] { "s6-r08-xor", "CS9330", "VIB000", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(MethodImplOptions.AggressiveInlining ^ (MethodImplOptions)0x2100)] static int F() => 1;\n" };
    }

    [Theory]
    [MemberData(nameof(InvalidCSharp))]
    public void SelfRejectsWhatCSharpRejects(string name, string roslynId, string selfId, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfValidityRejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && (roslynId.Length == 0 || diagnostic.Id == roslynId));
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Contains($"error {selfId}:", self.Output + self.Error, StringComparison.Ordinal);
        });
    }

    public static IEnumerable<object[]> ValidControls()
    {
        yield return new object[] { "s1-a04-refreturn-prop", "var h = new RH2(); h.R = 7; WriteLine(h.N); class RH2 { public int N; public ref int R => ref N; }\n", "VIB004" };
        yield return new object[] { "s1-a01-call-mutating-method-on-value", "Make().Bump(); WriteLine(1); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a02-class-field-of-call", "MakeC().Z = 3; WriteLine(1); static C MakeC() => new C();\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a03-class-prop-chain", "var h = new Holder(); h.Cls.Z = 4; h.B.X = 5; h.B.In.Y = 6; WriteLine(h.Cls.Z + h.B.X + h.B.In.Y);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a05-refreturn-call", "var h = new RH(); h.Slot() = 4; h.Slot()++; WriteLine(h.N); class RH { public int N; [Mut] public ref int Slot() => ref N; }\n", "" };
        yield return new object[] { "s1-a06-vec-index", "Vec<Box> v = [new Box(1)]; v[0].X = 9; v[0].In.Y += 2; WriteLine(v[0].X);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a07-span-index", "Span<int> s = [1, 2]; s[0] = 5; s[1]++; WriteLine(s[0] + s[1]);\n", "" };
        yield return new object[] { "s1-a08-local", "var b = new Box(1); b.X = 2; b.In.Y = 3; b.X++; F(ref b.X); WriteLine(b.X); static void F(ref int a) { a += 1; }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a09-read-value", "WriteLine(Make().X + Make().In.Y); static Box Make() => new Box(1);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a10-inarg-implicit", "F(Make().X); WriteLine(1); static Box Make() => new Box(1); static void F(in int a) { WriteLine(a); }\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a11-prop-setter-on-class", "var h = new Holder(); h.B = new Box(3); WriteLine(h.B.X);\npublic struct Box { public int X; public Inner In; public Box(int x) { X = x; In = new Inner(x); } public int P { get => X; set => X = value; } [Mut] public void Bump() { X += 1; } }\npublic struct Inner { public int Y; public Inner(int y) { Y = y; } }\npublic class Holder { public Box B = new Box(1); public Box PB => B; public C Cls = new C(); }\npublic class C { public int Z; }\n", "" };
        yield return new object[] { "s1-a12-vec-field-of-call", "MakeV().Items[0] = 3; WriteLine(1); static VB MakeV() => new VB(); public struct VB { public Vec<int> Items; public VB() { Items = [1]; } }\n", "" };
        yield return new object[] { "s1-a13-this-struct", "var b = new SB(); b.Set(); WriteLine(b.X); public struct SB { public int X; public SB() { X = 0; } public void Set() { this.X = 4; X++; } }\n", "" };
        yield return new object[] { "s2-a01-cast-both", "bool c = true; byte a = c ? (byte)2 : (byte)1; WriteLine((int)a);\n", "" };
        yield return new object[] { "s2-a02-cast-whole", "bool c = true; byte a = (byte)(c ? 2 : 1); WriteLine((int)a);\n", "" };
        yield return new object[] { "s2-a03-int-long", "bool c = true; long l = c ? 1 : 2; WriteLine(l);\n", "" };
        yield return new object[] { "s2-a04-uint-const", "bool c = true; uint u = c ? 1u : 2; WriteLine(u);\n", "" };
        yield return new object[] { "s2-a05-double", "bool c = true; double d = c ? 1 : 2.5; WriteLine((long)(d * 10));\n", "" };
        yield return new object[] { "s2-a06-option", "bool c = true; Option<int> o = c ? Some(1) : None; WriteLine(o.IsSome);\n", "" };
        yield return new object[] { "s2-a07-result", "bool c = true; Result<int, int> r = c ? Ok(1) : Err(2); WriteLine(r.IsOk);\n", "" };
        yield return new object[] { "s2-a08-byte-sbyte-to-int", "bool c = true; int i = c ? (byte)1 : (sbyte)2; WriteLine(i);\n", "" };
        yield return new object[] { "s2-a09-enum-zero", "bool c = true; E e = c ? E.B : 0; WriteLine((int)e); enum E { A, B }\n", "" };
        yield return new object[] { "s2-a10-span", "bool c = true; Span<int> s = [1, 2]; ReadOnlySpan<int> r = c ? s : s; WriteLine(r.Length);\n", "" };
        yield return new object[] { "s2-a11-compound-cast", "byte a = 7; bool c = true; a += (byte)(c ? 2 : 1); WriteLine((int)a);\n", "" };
        yield return new object[] { "s2-a12-compound-int", "byte a = 7; bool c = true; a += c ? (byte)2 : (byte)1; WriteLine((int)a);\n", "" };
        yield return new object[] { "s2-a13-float-int", "bool c = true; float f = c ? 1 : 2.5f; WriteLine((long)(f * 10));\n", "" };
        yield return new object[] { "s2-a14-int-float-precision", "bool c = false; int i = 16777217; float f = 0.5f; double d = c ? f : i; WriteLine((long)d);\n", "" };
        yield return new object[] { "s2-a15-char", "bool c = true; char ch = c ? 'a' : 'b'; WriteLine((int)ch);\n", "" };
        yield return new object[] { "s2-a16-int-char", "bool c = true; int i = c ? 'a' : 1; WriteLine(i);\n", "" };
        yield return new object[] { "s2-a17-string", "bool c = true; string s = c ? \"a\" : \"b\"; WriteLine(s);\n", "" };
        yield return new object[] { "s2-a18-ulong-const", "bool c = true; ulong u = c ? 1UL : 2; WriteLine(u);\n", "" };
        yield return new object[] { "s2-a19-long-uint", "bool c = true; int i = 1; uint u = 2; long l = c ? i : u; WriteLine(l);\n", "" };
        yield return new object[] { "s2-r18-switch-expr", "int k = 1; byte b = k switch { 1 => (byte)1, _ => 2 }; WriteLine((int)b);\n", "" };
        yield return new object[] { "s2-r19-ushort-char", "bool c = true; ushort u = c ? 'a' : (ushort)1; WriteLine(u);\n", "" };
        yield return new object[] { "s3-a01-top-unreachable", "WriteLine(1); return 0; WriteLine(2);\n", "" };
        yield return new object[] { "s3-a02-body-unreachable", "WriteLine(F()); static int F() { int x = 1; return x; x = 2; }\n", "" };
        yield return new object[] { "s3-a03-loop-unreachable", "for (int i = 0; i < 2; i++) { break; WriteLine(i); } WriteLine(1);\n", "" };
        yield return new object[] { "s3-a04-unreachable-move", "Vec<int> v = [1]; Sink(v); return 0; Sink(v); static void Sink(Vec<int> x) { WriteLine(x.Count); }\n", "" };
        yield return new object[] { "s3-a05-unreachable-uninit", "int x; WriteLine(1); return 0; WriteLine(x);\n", "" };
        yield return new object[] { "s3-a06-unreachable-borrow", "Vec<int> v = [1]; var s = v.AsSpan(); return 0; v.Add(2); s[0] = 1;\n", "" };
        yield return new object[] { "s3-a07-unreachable-linear", "WriteLine(1); return 0; using var d = new D(); class D : System.IDisposable { public void Dispose() { } }\n", "" };
        yield return new object[] { "s3-a09-unreachable-null-gate", "WriteLine(1); return 0; Vec<int> v = [1]; v.Add(1); Sink(v); Sink(v); static void Sink(Vec<int> x) { }\n", "" };
        yield return new object[] { "s3-a11-unreachable-after-panic", "WriteLine(1); VibraniumRuntime.Panic(\"stop\"); WriteLine(2);\n", "" };
        yield return new object[] { "s3-a12-unreachable-switch", "int x = 1; switch (x) { case 1: WriteLine(1); break; WriteLine(2); default: break; }\n", "" };
        yield return new object[] { "s3-a13-unreachable-local-fn", "WriteLine(F()); static int F() { return G(); static int G() => 1; }\n", "" };
        yield return new object[] { "s3-a15-unreachable-fallthrough", "int x = 1; switch (x) { case 1: return 0; WriteLine(1); default: break; } return 1;\n", "" };
        yield return new object[] { "s3-a16-unreachable-ret-missing", "WriteLine(F()); static int F() { while (true) { } }\n", "" };
        yield return new object[] { "s3-a17-unreachable-label-decl", "WriteLine(F()); static int F() { return 1; int y = 2; WriteLine(y); }\n", "" };
        yield return new object[] { "s3-a08-unreachable-default", "WriteLine(F()); static int F() { return 1; Vec<int> v = default; }\n", "VIB008" };
        yield return new object[] { "s3-r19-lambda-after-return", "return 0; var f = (int x) => x;\n", "VIB000" };
        yield return new object[] { "s4-a01-exact-err", "Result<int, int> r = Err(7); if (r.IsErr(out int v, out Err<int> e)) { WriteLine(e.Error); } return 0;\n", "" };
        yield return new object[] { "s4-a02-exact-ok", "Result<long, long> r = Ok(7L); if (!r.IsErr(out long v, out Err<long> e)) { WriteLine(v); } return 0;\n", "" };
        yield return new object[] { "s4-a03-var", "Result<long, long> r = Ok(7L); if (!r.IsErr(out var v, out var e)) { WriteLine(v); } return 0;\n", "" };
        yield return new object[] { "s4-a04-none-exact", "Option<int> o = Some(1); if (o.IsNone(out int v)) { return 0; } WriteLine(v); return 0;\n", "" };
        yield return new object[] { "s4-a05-discards", "Result<int, int> r = Ok(1); if (r.IsErr(out _, out _)) { return 1; } WriteLine(1); return 0;\n", "" };
        yield return new object[] { "s4-a06-normal-exact", "G(out int a); WriteLine(a); static void G(out int x) { x = 1; }\n", "" };
        yield return new object[] { "s4-a07-system-alias", "G(out System.Int32 a); WriteLine(a); static void G(out int x) { x = 1; }\n", "" };
        yield return new object[] { "s4-a08-generic-exact", "WriteLine(F<int>(Ok(1))); static int F<T>(Result<T, int> r) { if (!r.IsErr(out T v, out Err<int> e)) { return 1; } return 0; }\n", "" };
        yield return new object[] { "s4-a09-tryparse", "if (int.TryParse(\"12\", out int v)) WriteLine(v);\n", "" };
        yield return new object[] { "s4-a10-vec-exact", "Result<Vec<int>, int> r = Ok<Vec<int>>([1]); if (!r.IsErr(out Vec<int> v, out var e)) { WriteLine(v.Count); } return 0;\n", "" };
        yield return new object[] { "s4-a11-named-order", "Result<int, int> r = Ok(1); if (!r.IsErr(ok: out int v, err: out Err<int> e)) { WriteLine(v); } return 0;\n", "" };
        yield return new object[] { "s4-a12-using-alias", "using I = int; G(out I a); WriteLine(a); static void G(out int x) { x = 1; }\n", "" };
        yield return new object[] { "s5-a01-ref-local", "int x = 1; ref int r = ref x; r = 3; WriteLine(x);\n", "" };
        yield return new object[] { "s5-a02-ref-assign", "int x = 1; int y = 2; ref int r = ref x; r = ref y; r = 5; WriteLine(y);\n", "" };
        yield return new object[] { "s5-a03-ref-return", "int x = 1; G(ref x) = 4; WriteLine(x); static ref int G(ref int a) { return ref a; }\n", "" };
        yield return new object[] { "s5-a04-ref-arrow", "int x = 1; G(ref x) = 4; WriteLine(x); static ref int G(ref int a) => ref a;\n", "" };
        yield return new object[] { "s5-a05-ref-arg", "int x = 1; F(ref x); WriteLine(x); static void F(ref int a) { a = 2; }\n", "" };
        yield return new object[] { "s5-a06-compound-plain", "int x = 1; int y = 2; ref int r = ref x; r += y; WriteLine(x);\n", "" };
        yield return new object[] { "s5-a07-ref-readonly-local", "int x = 1; ref readonly int r = ref x; WriteLine(r);\n", "" };
        yield return new object[] { "s5-a09-ref-index", "Vec<int> v = [1, 2]; ref int r = ref v[1]; r = 7; WriteLine(v[1]);\n", "" };
        yield return new object[] { "s5-a10-out-arg", "F(out int x); WriteLine(x); static void F(out int a) { a = 2; }\n", "" };
        yield return new object[] { "s5-a11-in-arg", "int x = 1; F(in x); static void F(in int a) { WriteLine(a); }\n", "" };
        yield return new object[] { "s5-a12-ref-foreach", "Span<int> s = [1, 2]; foreach (ref var e in s) e += 1; WriteLine(s[0]);\n", "" };
        yield return new object[] { "s5-a13-ref-local-cast-free", "Vec<int> v = [1]; ref int r = ref v[0]; r += 1; WriteLine(v[0]);\n", "" };
        yield return new object[] { "s5-a08-prop-ref", "var h = new H(); h.P = 3; WriteLine(h.N); class H { public int N; public ref int P => ref N; }\n", "VIB004" };
        yield return new object[] { "s5-a14-getter-ref", "var h = new H(); h.P = 3; WriteLine(h.N); class H { public int N; public ref int P { get => ref N; } }\n", "VIB004" };
    }

    [Theory]
    [MemberData(nameof(ValidControls))]
    public void ValidControlsKeepStage0VibIdSet(string name, string source, string expectedIds)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SelfValidityAccepted", includeStd: true);
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

    public static IEnumerable<object[]> MethodImplControls()
    {
        yield return new object[] { "s6-a01-m8196", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(-8196)] static int F() => 1;\n" };
        yield return new object[] { "s6-a02-m32768", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(-32768)] static int F() => 1;\n" };
        yield return new object[] { "s6-a03-or", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)] static int F() => 1;\n" };
        yield return new object[] { "s6-a04-0x4000", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl(0x4000)] static int F() => 1;\n" };
        yield return new object[] { "s6-a05-xor-clear", "using System.Runtime.CompilerServices; WriteLine(F()); [MethodImpl((MethodImplOptions)0x2100 ^ MethodImplOptions.Async)] static int F() => 1;\n" };
        yield return new object[] { "s6-a06-ctor", "using System.Runtime.CompilerServices; WriteLine(new C().X); sealed class C { public int X; [MethodImpl(MethodImplOptions.NoInlining)] public C() { X = 3; } }\n" };
        yield return new object[] { "s6-valid-control-aggressive", "using System.Runtime.CompilerServices;\nWriteLine(F());\n[MethodImpl(MethodImplOptions.AggressiveInlining)]\nstatic int F() => 1;\n" };
    }

    [Theory]
    [MemberData(nameof(MethodImplControls))]
    public void F15_MethodImplIsOutsideTheRoslynSubset(string name, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MethodImplF15", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(new[] { "VIB000" }, frontend.Diagnostics.Select(diagnostic => diagnostic.Id).Distinct());
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Equal(new[] { "VIB000" }, analyzer.Select(diagnostic => diagnostic.Id).Distinct());
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Equal(new[] { "VIB000" }, Regex.Matches(self.Output + self.Error, @"VIB\d{3}")
                .Select(match => match.Value).Distinct());
        });
    }

    // The natural type of `?:` decides the arithmetic: `c ? f : i` converts the int to float before the result is
    // widened to double, so 16777217 prints as 16777216 on every backend.
    [Theory]
    [InlineData("bool c = false; int i = 16777217; float f = 0.5f; double d = c ? f : i; WriteLine((long)d);\n", "16777216\n")]
    [InlineData("bool c = true; double d = c ? 1 : 2.5; WriteLine((long)(d * 10));\n", "10\n")]
    [InlineData("bool c = true; int i = 1; uint u = 2; long l = c ? i : u; WriteLine(l);\n", "1\n")]
    public Task ConditionalNaturalTypeRunsIdentically(string source, string expected) =>
        Round11AuditAssertions.AssertDifferential(source, expected, includeUnoptimized: false);

    // Code after a return is lowered and checked, and still never runs.
    [Fact]
    public Task UnreachableCodeRunsIdentically() => Round11AuditAssertions.AssertDifferential(
        "WriteLine(F(2));\nstatic int F(int x)\n{\n    if (x > 1) return x * 3;\n    return x;\n    WriteLine(99);\n}\n", "6\n",
        includeUnoptimized: false, expectedWarning: "CS0162");

    private static void WithSource(string name, string source, Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-self-validity-{name}-{Guid.NewGuid():N}");
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
