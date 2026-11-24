using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfSwitchGuardBorrowTests
{
    [Theory]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nswitch (o)\n{\n    case Some<Nd>(var d) when (d.V = 14) < 0: WriteLine(1); break;\n    case Some<Nd>(var d) when d.V == 14: WriteLine(2); break;\n    default: WriteLine(3); break;\n}\npublic sealed class Nd(int v) { public int V = v; }\n", "VIB004")]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nswitch (o)\n{\n    case Some<Nd>(var d) when Bump(ref d.V): WriteLine(1); break;\n    case Some<Nd>(var d) when d.V == 14: WriteLine(2); break;\n    default: WriteLine(3); break;\n}\nstatic bool Bump(ref int v) { v = 14; return false; }\npublic sealed class Nd(int v) { public int V = v; }\n", "VIB004")]
    [InlineData("Vec<int> init = [4];\nOption<Vec<int>> o = Some(init);\nswitch (o)\n{\n    case Some<Vec<int>>(var v) when (v[0] = 14) < 0: WriteLine(1); break;\n    case Some<Vec<int>>(var v) when v[0] == 14: WriteLine(2); break;\n    default: WriteLine(3); break;\n}\n", "VIB004")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Vec<int> w = [p];\n    switch (w[0])\n    {\n        case 4 when Grow(ref w): n = 1; break;\n        case 4: n = 20 + w.Count; break;\n        default: n = 30 + w.Count; break;\n    }\n    return n;\n}\nstatic bool Grow(ref Vec<int> w) { w.Add(5); return false; }\n", "VIB003")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Result<int, int> r = MkR(p);\n    switch (r)\n    {\n        case Ok<int>(var a) when ClearR(ref r): n = 1; break;\n        case Err<int>(var e): n = e; break;\n        default: n = 3; break;\n    }\n    return n;\n}\nstatic bool ClearR(ref Result<int, int> x) { x = Err(9); return false; }\nstatic Result<int, int> MkR(int a) { if (a > 2) return Ok(a); return Err(a + 20); }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }\n", "VIB003")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Option<Vec<int>> o = MkV(p);\n    switch (o)\n    {\n        case Some<Vec<int>>(var v) when ClearV(ref o): n = 1; break;\n        case Some<Vec<int>>(var v): n = 10 + v.Count; break;\n        default: n = 3; break;\n    }\n    return n;\n}\nstatic bool ClearV(ref Option<Vec<int>> x) { x = None; return false; }\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "VIB003")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Vec<int> w = [p];\n    n = w[0] switch { 4 when Grow(ref w) => 1, 4 => 20 + w.Count, _ => 30 + w.Count };\n    return n;\n}\nstatic bool Grow(ref Vec<int> w) { w.Add(5); return false; }\n", "VIB003")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Nd d = new Nd(p);\n    switch (d.V) { case 4 when d.Bump(): n = 1; break; case 14: n = 2; break; default: n = 3 + d.V; break; }\n    return n;\n}\npublic sealed class Nd(int v) { public int V = v; [Mut] public bool Bump() { V += 10; return false; } }\n", "VIB003")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Option<Nd> o = p > 2 ? Some(new Nd(p)) : None;\n    switch (o) { case Some<Nd>(var d) when d.Bump(): n = 1; break; case Some<Nd>(var d) when d.V == 14: n = 2; break; default: n = 3; break; }\n    return n;\n}\npublic sealed class Nd(int v) { public int V = v; [Mut] public bool Bump() { V += 10; return false; } }\n", "VIB004")]
    [InlineData("WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);\nstatic int G(int p)\n{\n    int n = 0;\n    Vec<int> w = [p];\n    switch (w[0]) { case 4 when Set0(ref w): n = 1; break; case 14: n = 2; break; default: n = 3; break; }\n    return n;\n}\nstatic bool Set0(ref Vec<int> w) { w[0] = 14; return false; }\n", "VIB003")]
    [InlineData("Option<int> o=Some(3);\nWriteLine(o switch {Some<int> when Clear(ref o)=>1, not NoneType=>2, _=>3});\nstatic bool Clear(ref Option<int> x){x=None; return false;}\n", "VIB003")]
    [InlineData("Option<int> o=Some(3);\nswitch(o){\n case Some<int> when Clear(ref o): WriteLine(1); break;\n case not NoneType: WriteLine(2); break;\n default: WriteLine(3); break;\n}\nstatic bool Clear(ref Option<int> x){x=None; return false;}\n", "VIB003")]
    [InlineData("Option<int> o=Some(3);\nWriteLine(o switch {Some<int> when Change(ref o)=>1, Some<int>(var n)=>n, _=>0});\nstatic bool Change(ref Option<int> x){x=Some(8); return false;}\n", "VIB003")]
    [InlineData("WriteLine(F(Some(3)));\nstatic int F(Option<int> o) => o switch { Some<int> when Change(out o) => 1, _ => 2 };\nstatic bool Change(out Option<int> o) { o = None; return true; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nWriteLine(F(ref o));\nstatic int F(ref Option<int> o) => o switch { Some<int> when Change(ref o) => 1, _ => 2 };\nstatic bool Change(ref Option<int> o) { o = None; return true; }\n", "VIB003")]
    [InlineData("S s = new S();\ns.O = Some(3);\nWriteLine(s.O switch { Some<int> when Change(ref s.O) => 1, _ => 2 });\nstatic bool Change(ref Option<int> o) { o = None; return false; }\nstruct S { public Option<int> O; }\n", "VIB003")]
    [InlineData("S s = new S();\ns.O = Some(3);\nWriteLine(s.O switch { Some<int> when Change(ref s) => 1, _ => 2 });\nstatic bool Change(ref S s) { s = default; return true; }\nstruct S { public Option<int> O; }\n", "VIB003")]
    [InlineData("Span<Option<int>> s = [Some(3), Some(4)];\nWriteLine(s[0] switch { Some<int> when Change(ref s[0]) => 1, _ => 2 });\nstatic bool Change(ref Option<int> o) { o = None; return false; }\n", "VIB003")]
    [InlineData("Vec<int> v = [3];\nWriteLine(v switch { var _ when Take(v) => 1, _ => 2 });\nstatic bool Take(Vec<int> x) => x.Count > 0;\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nref Option<int> a = ref o;\nWriteLine(o switch { Some<int> when Change(ref a) => 1, _ => 2 });\nstatic bool Change(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nswitch (o)\n{\n    case NoneType: WriteLine(0); break;\n    case Some<int> when Change(ref o): WriteLine(1); break;\n}\nstatic bool Change(ref Option<int> r) { r = None; return true; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nOption<int> inner = Some(4);\nWriteLine(o switch\n{\n    Some<int> when (inner switch { Some<int> when Change(ref o) => true, _ => false }) => 1,\n    _ => 2\n});\nstatic bool Change(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Vec<int> v = [3];\nOption<Vec<int>> o = Some(v);\nWriteLine(o switch { Some<Vec<int>>(var payload) when Change(ref o) => payload.Count, _ => 2 });\nstatic bool Change(ref Option<Vec<int>> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("int i = 3;\nWriteLine(((i)) switch { 3 when (i = 0) > 0 => 1, 3 => 2, _ => 3 });\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nref Option<int> p = ref o;\nWriteLine(p switch { Some<int> when (p = None).IsSome => 1, _ => 2 });\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nWriteLine(o switch { Some<int> when o.IsSome && Change(ref o) => 1, _ => 2 });\nstatic bool Change(ref Option<int> x) { x = None; return false; }\n", "VIB003")]
    [InlineData("S s = new S();\ns.N = 3;\nWriteLine(s switch { var _ when (s.N = 0) > 0 => 1, _ => 2 });\nstruct S { public int N; }\n", "VIB003")]
    [InlineData("Vec<int> w = [4];\nswitch (w[0])\n{\n    case 4 when Grow(ref w): WriteLine(1); break;\n}\nstatic bool Grow(ref Vec<int> w) { w.Add(5); return true; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nWriteLine(o.UnwrapMut() switch { 3 when Clear(ref o) => 1, _ => 2 });\nstatic bool Clear(ref Option<int> o) { o = None; return false; }", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nswitch (o.UnwrapMut()) { case 3 when Clear(ref o): WriteLine(1); break; default: WriteLine(2); break; }\nstatic bool Clear(ref Option<int> o) { o = None; return false; }", "VIB003")]
    [InlineData("Vec<int> w = [4];\nWriteLine(At(ref w) switch { 4 when Grow(ref w) => 1, _ => 2 });\nstatic ref int At(ref Vec<int> v) => ref v[0];\nstatic bool Grow(ref Vec<int> w) { w.Add(5); return false; }", "VIB003")]
    [InlineData("Vec<int> w = [4];\nswitch (At(ref w)) { case 4 when Grow(ref w): WriteLine(1); break; default: WriteLine(2); break; }\nstatic ref int At(ref Vec<int> v) => ref v[0];\nstatic bool Grow(ref Vec<int> w) { w.Add(5); return false; }", "VIB003")]
    [InlineData("Option<int> o = Some(4);\nWriteLine(At(ref o) switch { Some<int> when Clear(out o) => 1, _ => 2 });\nstatic ref Option<int> At(ref Option<int> o) => ref o;\nstatic bool Clear(out Option<int> o) { o = None; return false; }", "VIB003")]
    [InlineData("Option<int> o = Some(4);\nswitch (At(ref o)) { case Some<int> when Clear(out o): WriteLine(1); break; default: WriteLine(2); break; }\nstatic ref Option<int> At(ref Option<int> o) => ref o;\nstatic bool Clear(out Option<int> o) { o = None; return false; }", "VIB003")]
    [InlineData("var b = new Box();\nWriteLine(b.Slot switch { 4 when (b.Value = 9) > 0 => 1, _ => 2 });\nsealed class Box { public int Value = 4; public ref int Slot { [Mut] get => ref Value; } }", "VIB003")]
    [InlineData("var b = new Box();\nswitch (b.Slot) { case 4 when (b.Value = 9) > 0: WriteLine(1); break; default: WriteLine(2); break; }\nsealed class Box { public int Value = 4; public ref int Slot { [Mut] get => ref Value; } }", "VIB003")]
    [InlineData("var b = new Box();\nWriteLine(b.Slot switch { Some<int> when b.Clear() => 1, _ => 2 });\nsealed class Box { public Option<int> Value = Some(4); public ref Option<int> Slot { [Mut] get => ref Value; } [Mut] public bool Clear() { Value = None; return false; } }", "VIB003")]
    [InlineData("var b = new Box();\nswitch (b.Slot) { case Some<int> when b.Clear(): WriteLine(1); break; default: WriteLine(2); break; }\nsealed class Box { public Option<int> Value = Some(4); public ref Option<int> Slot { [Mut] get => ref Value; } [Mut] public bool Clear() { Value = None; return false; } }", "VIB003")]
    [InlineData("var b = new Box();\nWriteLine(b[0] switch { 4 when b.Bump() => 1, _ => 2 });\nsealed class Box { public int Value = 4; public ref int this[int i] { [Mut] get => ref Value; } [Mut] public bool Bump() { Value++; return false; } }", "VIB003")]
    [InlineData("var b = new Box();\nswitch (b[0]) { case 4 when b.Bump(): WriteLine(1); break; default: WriteLine(2); break; }\nsealed class Box { public int Value = 4; public ref int this[int i] { [Mut] get => ref Value; } [Mut] public bool Bump() { Value++; return false; } }", "VIB003")]
    [InlineData("Span<int> s = [4, 5];\nWriteLine(s[0] switch { 4 when (s[1] = 9) > 0 => 1, _ => 2 });\n", "VIB003")]
    [InlineData("Span<int> s = [4, 5];\nswitch (s[0]) { case 4 when (s[1] = 9) > 0: WriteLine(1); break; default: WriteLine(2); break; }\n", "VIB003")]
    [InlineData("Span<Option<int>> s = [Some(3), Some(4)];\nWriteLine(s[0] switch { Some<int>(var n) when Change(ref s[1]) && n > 0 => 1, _ => 2 });\nstatic bool Change(ref Option<int> x) { x = None; return true; }", "VIB003")]
    [InlineData("Span<Option<int>> s = [Some(3), Some(4)];\nswitch (s[0]) { case Some<int>(var n) when Change(ref s[1]) && n > 0: WriteLine(1); break; default: WriteLine(2); break; }\nstatic bool Change(ref Option<int> x) { x = None; return true; }", "VIB003")]
    [InlineData("Vec<int> v = [4, 5];\nWriteLine(v[0] switch { 4 when (v[1] = 9) > 0 => 1, _ => 2 });\n", "VIB003")]
    [InlineData("Vec<int> v = [4, 5];\nswitch (v[0]) { case 4 when (v[1] = 9) > 0: WriteLine(1); break; default: WriteLine(2); break; }\n", "VIB003")]
    [InlineData("int a = 4; int b = 5; ref int x = ref a;\nWriteLine(x switch { 4 when (x = ref b) == 0 => 1, _ => 2 });\n", "VIB003")]
    [InlineData("int a = 4; int b = 5; ref int x = ref a;\nswitch (x) { case 4 when (x = ref b) == 0: WriteLine(1); break; default: WriteLine(2); break; }\n", "VIB003")]
    [InlineData("Box b = new Box(3);\nWriteLine(b switch\n{\n    var x when Reinit(out b) => x.V,\n    var y => y.V\n});\nstatic bool Reinit(out Box slot) { slot = new Box(42); return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Box b = new Box(3);\nswitch (b)\n{\n    case var x when Reinit(out b): WriteLine(x.V); break;\n    case var y: WriteLine(y.V); break;\n}\nstatic bool Reinit(out Box slot) { slot = new Box(42); return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Vec<int>> o = Some(Make(3));\nWriteLine(o switch\n{\n    var x when Reinit(out o) => x.IsSome ? 1 : 0,\n    Some<Vec<int>>(var v) => v[0],\n    _ => 99\n});\nstatic Vec<int> Make(int a) { Vec<int> v = [a]; return v; }\nstatic bool Reinit(out Option<Vec<int>> slot) { Vec<int> v = [42]; slot = Some(v); return false; }\n", "VIB003")]
    [InlineData("Option<Vec<int>> o = Some(Make(3));\nswitch (o)\n{\n    case var x when Reinit(out o): WriteLine(x.IsSome ? 1 : 0); break;\n    case Some<Vec<int>>(var v): WriteLine(v[0]); break;\n    default: WriteLine(99); break;\n}\nstatic Vec<int> Make(int a) { Vec<int> v = [a]; return v; }\nstatic bool Reinit(out Option<Vec<int>> slot) { Vec<int> v = [42]; slot = Some(v); return false; }\n", "VIB003")]
    [InlineData("Box b = new Box(3);\nWriteLine(b switch\n{\n    var x when (b = new Box(42)).V == 0 => x.V,\n    var y => y.V\n});\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Vec<int>> o = Some(Make(3));\nWriteLine(o switch\n{\n    var x when Reinit(out o) => x.IsSome ? 1 : 0,\n    _ => 99\n});\nstatic Vec<int> Make(int a) { Vec<int> v = [a]; return v; }\nstatic bool Reinit(out Option<Vec<int>> slot) { slot = None; return false; }\n", "VIB003")]
    [InlineData("Box b = new Box(3);\nWriteLine(b switch\n{\n    Box x when Reinit(out b) => x.V,\n    var y => y.V\n});\nstatic bool Reinit(out Box slot) { slot = new Box(42); return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("int a = 4;\nint c = 0;\nref int x = ref c;\nWriteLine((x = ref a) switch { 4 when (a = 9) == 0 => 1, 9 => 2, _ => 3 });\n", "VIB003")]
    [InlineData("Option<int> o = Some(3);\nOption<int> d = None;\nref Option<int> x = ref d;\nWriteLine((x = ref o) switch { Some<int> when Clear(ref o) => 1, not NoneType => 2, _ => 3 });\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("int a = 4;\nint b = 7;\nint c = 0;\nref int x = ref c;\nWriteLine((x = ref a) switch { 4 when (x = ref b) == 0 => 1, 7 => 2, _ => 3 });\n", "VIB003")]
    [InlineData("int a = 4; int b = 0; ref int x = ref b; ref int y = ref b;\nswitch (((x = ref (y = ref a)))) { case 4 when (a = 9) == 0: WriteLine(1); break; default: WriteLine(2); break; }", "VIB003")]
    [InlineData("int a = 4; int b = 0; ref int x = ref b;\nswitch ((x = ref Slot(ref a))) { case 4 when (x = ref b) == 0: WriteLine(1); break; default: WriteLine(2); break; }\nstatic ref int Slot(ref int n) => ref n;", "VIB003")]
    [InlineData("var b = new Box(); int other = 0; ref int x = ref other;\nWriteLine((x = ref b.Slot) switch { 4 when (b.Value = 9) > 0 => 1, _ => 2 });\nsealed class Box { public int Value = 4; public ref int Slot { [Mut] get => ref Value; } }", "VIB003")]
    [InlineData("var b = new Box(); int other = 0; ref int x = ref other;\nswitch ((x = ref b[0])) { case 4 when b.Bump(): WriteLine(1); break; default: WriteLine(2); break; }\nsealed class Box { public int Value = 4; public ref int this[int i] { [Mut] get => ref Value; } [Mut] public bool Bump() { Value++; return false; } }", "VIB003")]
    [InlineData("bool c = Pick(false); int a = 4; int b = 7; ref int x = ref a;\nWriteLine((x = ref (c ? ref a : ref b)) switch { 4 when c => 1, _ => 2 });\nstatic bool Pick(bool c) => c;", "VIB000")]
    public async Task SelfRejectsWithDecidedIdSet(string source, string expected)
    {
        // The paired Roslyn change ships independently. The shared witness
        // still proves the source is valid C# before checking self's exact set.
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { expected }, result.Self);
    }

    [Theory]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nvar read = o.AsRef();\nswitch (o)\n{\n    case Some<Nd>(var d) when (d.V = 14) < 0: WriteLine(1); break;\n    default: WriteLine(3); break;\n}\nWriteLine(read.IsSome);\npublic sealed class Nd(int v) { public int V = v; }\n", "VIB003,VIB004")]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nWriteLine(o switch { Some<Nd>(var d) when (At(ref d.V) = 14) < 0 => 1, Some<Nd>(var d) when d.V == 14 => 2, _ => 3 });\nstatic ref int At(ref int x) => ref x;\npublic sealed class Nd(int v) { public int V = v; }\n", "VIB004")]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nWriteLine(o switch { Some<Nd>(var d) when (d[0] = 14) < 0 => 1, Some<Nd>(var d) when d.V == 14 => 2, _ => 3 });\npublic sealed class Nd(int v) { public int V = v; [Mut] public ref int this[int i] => ref V; }\n", "VIB004")]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nWriteLine(o switch { Some<Nd>(var d) when (d.RefV() = 14) < 0 => 1, Some<Nd>(var d) when d.V == 14 => 2, _ => 3 });\npublic sealed class Nd(int v) { public int V = v; [Mut] public ref int RefV() => ref V; }\n", "VIB004")]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nWriteLine(o switch { Some<Nd>(var d) when (d.RefV = 14) < 0 => 1, Some<Nd>(var d) when d.V == 14 => 2, _ => 3 });\npublic sealed class Nd(int v) { public int V = v; [Mut] public ref int RefV => ref V; }\n", "VIB004")]
    [InlineData("Option<Box> o1 = Some(new Box(9));\nOption<Box> o2 = Some(new Box(2));\nref Option<Box> x = ref o1;\nWriteLine(x.AsRef() switch { not Some<Box> when (x = ref o2).IsEmpty || Clear(ref o1) => 1, Some<Box>(var b) => b.V, _ => 0 });\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nref Option<int> x = ref o1;\nWriteLine(x.AsRef() switch { Some<int>(4) when Clear(ref o1) => 1, Some<int>(4) => 9, _ => 3 });\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch { Some<int> when Set7(ref o1) => 1, var w => w switch { Some<int>(var n) => n, _ => 0 } });\nstatic bool Set7(ref Option<int> r) { r = Some(7); return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch { Some<int> when Clear(ref o1) => 1, var w => w.IsSome ? 2 : 3 });\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nswitch (o1.AsRef())\n{\n    case Some<int> when Clear(ref o1): WriteLine(1); break;\n    case var w: WriteLine(w.IsSome ? 2 : 3); break;\n}\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch { Some<int>(4) when Clear(ref o1) => 1, Some<int>(4) => 9, _ => 3 });\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nref Option<int> x = ref o1;\nWriteLine(x.AsRef() switch { Some<int> when Clear(ref o1) => 1, var w => w.IsSome ? 2 : 3 });\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(View(in o1) switch { Some<int> when Clear(ref o1) => 1, var w => w.IsSome ? 2 : 3 });\nstatic OptionRef<int> View(in Option<int> o) => o.AsRef();\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(9);\nOption<int> o2 = Some(2);\nref Option<int> x = ref o1;\nWriteLine(x.AsRef() switch { Some<int> when (x = ref o2).IsSome == false || Kill(ref o1) => 1, var w => w.IsSome ? 2 : 3 });\nstatic bool Kill(ref Option<int> o) { o = None; return false; }\n", "VIB003")]
    [InlineData("Vec<int> v1 = [4];\nWriteLine(At(ref v1, 0) switch { 4 when v1.Count == 0 => 1, 4 => 9, _ => 3 });\nstatic ref int At(ref Vec<int> v, int i) => ref v[i];\n", "VIB003")]
    [InlineData("Option<int> o = Some(4);\nWriteLine(o.UnwrapMut() switch { 4 when o.IsSome => 1, 4 => 9, _ => 3 });\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch\n{\n    Some<int>(4) when Set7(ref o1) => 1,\n    Some<int>(7) => 7,\n    Some<int>(4) => 4,\n    _ => 0\n});\nstatic bool Set7(ref Option<int> r) { r = Some(7); return false; }\n", "VIB003")]
    [InlineData("Option<Box> o1 = Some(new Box(9));\nWriteLine(o1.AsRef() switch\n{\n    Some<Box> when Clear(ref o1) => 1,\n    Some<Box> => 9,\n    _ => 0\n});\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("int a = 4;\nref int r = ref a;\nWriteLine(r switch { 4 when a == 0 => 1, 4 => 9, _ => 3 });\n", "VIB003")]
    [InlineData("Pair p = new Pair(4, 10);\nref Pair r = ref p;\nWriteLine(r.A switch { 4 when p.B == 0 => 1, 4 => 9, _ => 3 });\nstruct Pair(int a, int b) { public int A = a; public int B = b; }\n", "VIB003")]
    [InlineData("int a = 4;\nref int r = ref a;\nWriteLine(r switch { 4 when a == 0 => 1, var w => w });\n", "VIB003")]
    [InlineData("int a = 4, b = 7;\nref int x = ref a;\nref int y = ref x;\nWriteLine(y switch { 4 when (x = ref b) == 0 => 1, var w => w });\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch { Some<int>(4) when Clear(ref o1) => 1, var w => w.IsSome ? 2 : 3 });\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("Option<int> o1 = Some(4);\nswitch (o1.AsRef()) { case Some<int>(4) when Clear(ref o1): WriteLine(1); break; case var w: WriteLine(w.IsSome ? 2 : 3); break; }\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "VIB003")]
    [InlineData("int a = 4;\nref int r = ref a;\nWriteLine(r switch { 4 when a == 0 => 1, _ when (a = 9) == 0 => 3, _ => 5 });\n", "VIB003")]
    public async Task ExistingSharedReaderStillConflicts(string source, string expected)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        string[] ids = expected.Split(',');
        Assert.Equal(ids, result.Stage0);
        Assert.Equal(ids, result.Analyzer);
        Assert.Equal(ids, result.Self);
    }

    // A guarded cursor arm cannot take an exclusive payload binding, even if unused.
    [Theory]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { Some<Box>(var b) when k > 0 => b.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nswitch (m) { case Some<Box>(var b) when b.V > 0: b.V = 5; WriteLine(b.V); break; default: WriteLine(0); break; }\nWriteLine(o.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { Some<Box>(var b) when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nswitch (m) { case Some<Box>(var b) when k > 0: WriteLine(1); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    public async Task GuardedMutableCursorPayloadBindingsAreExclusive(string source, string expected)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { expected }, result.Self);
    }

    [Theory]
    [InlineData("int x = 3; int k = 0; WriteLine(x switch { 3 when Bump(ref k) => x + k, _ => 0 }); static bool Bump(ref int k) { k = 7; return true; }", "10\n")]
    [InlineData("int x = 3; switch (x) { case 3 when Read(in x): x = 8; break; default: break; } WriteLine(x); static bool Read(in int n) => n == 3;", "8\n")]
    [InlineData("Option<int> o = Some(7); WriteLine(o switch { Some<int> when o.IsSome => 1, _ => 0 });", "1\n")]
    [InlineData("Option<int> o = Some(7); WriteLine(Id(o) switch { Some<int> when Clear(ref o) => 1, Some<int>(var v) => v, _ => 0 }); static Option<int> Id(Option<int> o) => o; static bool Clear(ref Option<int> o) { o = None; return false; }", "7\n")]
    [InlineData("Vec<int> v = [3]; int n = v[0]; WriteLine(n switch { 3 when Push(ref v) => v.Count, _ => 0 }); static bool Push(ref Vec<int> v) { v.Push(4); return true; }", "2\n")]
    [InlineData("var s = new S(3, 0); WriteLine(s.X switch { 3 when Bump(ref s.Y) => s.Y, _ => 0 }); static bool Bump(ref int x) { x = 8; return true; } struct S { public int X; public int Y; public S(int x, int y) { X = x; Y = y; } }", "8\n")]
    [InlineData("int x = 3; WriteLine(x switch { 3 when Read(in x) => Change(ref x), _ => 0 }); WriteLine(x); static bool Read(in int n) => n == 3; static int Change(ref int n) { n = 6; return n; }", "6\n6\n")]
    [InlineData("Span<int> s = [4, 7]; int i = 0;\nWriteLine(s[i] switch { 4 when i++ != 0 => 1, 4 => 2, _ => 3 });\nWriteLine(i);\n", "2\n1\n")]
    [InlineData("Span<int> s = [4, 7]; int i = 0;\nswitch (s[i]) { case 4 when i++ != 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\nWriteLine(i);\n", "2\n1\n")]
    [InlineData("Vec<int> v = [4, 7]; int i = 0;\nWriteLine(v[i] switch { 4 when i++ != 0 => 1, 4 => 2, _ => 3 });\nWriteLine(i);\n", "2\n1\n")]
    [InlineData("Vec<int> v = [4, 7]; int i = 0;\nswitch (v[i]) { case 4 when i++ != 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\nWriteLine(i);\n", "2\n1\n")]
    [InlineData("Box b = new Box(3);\nWriteLine(b switch { var x when x.V == 3 => x.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }", "3\n")]
    [InlineData("Option<Vec<int>> o = Some(Make(3));\nswitch (o) { case var x when x.IsSome: WriteLine(x.IsSome); break; default: WriteLine(false); break; }\nstatic Vec<int> Make(int a) { Vec<int> v = [a]; return v; }", "True\n")]
    [InlineData("int a = 4; int b = 0; int c = 0; ref int x = ref b; ref int y = ref c;\nswitch (((x = ref (y = ref a)))) { case 4 when (b = 8) == 8: x = 6; break; default: break; }\nWriteLine(a); WriteLine(b);", "6\n8\n")]
    [InlineData("int a = 0;\nWriteLine((a = 4) switch { 4 when (a = 9) == 0 => 1, 4 => 2, _ => 3 });\nWriteLine(a);", "2\n9\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { Some<Box> when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(1)); var m = o.AsMut(); int k = 1; switch (m) { case Some<Box> when k > 0: WriteLine(1); break; default: WriteLine(0); break; } public sealed class Box(int v) { public int V = v; }", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nWriteLine(m switch { Some<Box>(var b) => b.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar r = o.AsRef();\nWriteLine(r switch { Some<Box>(var b) when b.V > 0 => b.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nint k = 1;\nWriteLine(o switch { Some<Box>(var b) when k > 0 => b.V, _ => 0 });\nswitch (o) { case Some<Box>(var c) when c.V > 0: WriteLine(c.V + 1); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "1\n2\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nif (m is Some<Box>(var b) && k > 0) WriteLine(b.V);\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { Some<Box>(_) when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("int a = 4;\nref int x = ref a;\nWriteLine(x switch { 4 when a == 4 => 1, _ => 2 });\n", "1\n")]
    [InlineData("Option<int> a = Some(4);\nref Option<int> x = ref a;\nWriteLine(x switch { Some<int>(var n) when a.IsSome => n, _ => 2 });\n", "4\n")]
    [InlineData("Vec<int> v = [4, 5];\nref int x = ref v[0];\nWriteLine(x switch { 4 when v.Count == 2 => 1, _ => 2 });\n", "1\n")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref readonly Vec<int> x = ref v1;\nWriteLine(At(x.AsReadOnlySpan(), 0) switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x.Count);\nstatic ref readonly int At(ReadOnlySpan<int> s, int i) => ref s[i];\n", "9\n1\n")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nWriteLine(At(x.AsReadOnlySpan(), 0) switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x.Count);\nstatic ref readonly int At(ReadOnlySpan<int> s, int i) => ref s[i];\n", "9\n1\n")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref readonly Vec<int> x = ref v1;\nWriteLine(At(View(in x), 0) switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x.Count);\nstatic ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();\nstatic ref readonly int At(ReadOnlySpan<int> s, int i) => ref s[i];\n", "9\n1\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar r = o.AsRef();\nint k = 1;\nWriteLine(r switch { not Some<Box> when k > 5 => 1, Some<Box>(var b) => b.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "9\n")]
    [InlineData("Option<Box> o1 = Some(new Box(9));\nOption<Box> o2 = Some(new Box(2));\nref Option<Box> x = ref o1;\nWriteLine(x.AsRef() switch { not Some<Box> when (x = ref o2).IsEmpty => 1, Some<Box>(var b) => b.V, _ => 0 });\nWriteLine(x.AsRef() switch { Some<Box>(var b) => b.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "9\n9\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar r = o.AsRef();\nint k = 1;\nswitch (r)\n{\n    case not NoneType when k > 0: WriteLine(1); break;\n    default: WriteLine(0); break;\n}\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<int> o1 = Some(4);\nOption<int> o2 = Some(7);\nref Option<int> x = ref o1;\nWriteLine(x.AsRef() switch { Some<int>(4) when (x = ref o2).IsEmpty => 1, Some<int>(7) => 2, Some<int>(4) => 9, _ => 3 });\nWriteLine(x.IsSome);\n", "9\nTrue\n")]
    [InlineData("Option<Box> o1 = Some(new Box(9));\nOption<Box> o2 = Some(new Box(2));\nref Option<Box> x = ref o1;\nWriteLine(x.AsRef() switch { Some<Box> when (x = ref o2).IsSome == false => 1, Some<Box>(var b) => b.V, _ => 0 });\nWriteLine(x.AsRef() switch { Some<Box>(var c) => c.V, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "9\n2\n")]
    [InlineData("Option<Box> o1 = Some(new Box(9));\nWriteLine(o1.AsRef() switch\n{\n    Some<Box> when Clear(ref o1) => 1,\n    _ => 9\n});\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "9\n")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch\n{\n    Some<int>(4) when Clear(ref o1) => 1,\n    _ => 9\n});\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "9\n")]
    [InlineData("int a = 4, b = 7;\nref int x = ref a;\nref int y = ref x;\nWriteLine(y switch { 4 when (x = ref b) == 0 => 1, _ => 2 });\n", "2\n")]
    [InlineData("int a = 4, b = 7;\nref int x = ref a;\nref int y = ref x;\nref int z = ref y;\nWriteLine(z switch { 4 when (x = ref b) == 0 => 1, _ => 2 });\n", "2\n")]
    [InlineData("int a = 4;\nref int r = ref a;\nWriteLine(r switch { 4 when a == 0 => 1, var _ => 3 });\n", "3\n")]
    [InlineData("Option<Box> o = Some(new Box(4));\nWriteLine(o.AsRef() switch { Some<Box> when Clear(ref o) => 1, var _ => 2 });\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "2\n")]
    [InlineData("Option<Box> o = Some(new Box(4));\nswitch (o.AsRef()) { case Some<Box> when Clear(ref o): WriteLine(1); break; case var _: WriteLine(2); break; }\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "2\n")]
    [InlineData("Option<Box> o = Some(new Box(4));\nWriteLine(o.AsMut() switch { Some<Box> when Clear(ref o) => 1, var _ => 2 });\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "2\n")]
    [InlineData("int a = 4, b = 7;\nref int x = ref a;\nref int y = ref x;\nWriteLine(y switch { 4 when (x = ref b) == 0 => 1, var _ => 2 });\n", "2\n")]
    [InlineData("Option<int> o1 = Some(4);\nWriteLine(o1.AsRef() switch\n{\n    Some<int>(4) when Clear(ref o1) => 1,\n    var _ => 9\n});\nstatic bool Clear(ref Option<int> r) { r = None; return false; }\n", "9\n")]
    [InlineData("int a = 4;\nint k = 1;\nref int r = ref a;\nWriteLine(r switch { 4 when a == 0 => 1, _ when k > 0 => 3, _ => 5 });\nWriteLine(k);\n", "3\n1\n")]
    [InlineData("int a = 4;\nint k = 1;\nref int r = ref a;\nWriteLine(r switch { 4 when a == 0 => 1, var _ when k > 0 => 3, _ => 5 });\nWriteLine(k);\n", "3\n1\n")]
    [InlineData("int a = 4;\nint k = 1;\nref int r = ref a;\nswitch (r) { case 4 when a == 0: WriteLine(1); break; case var _ when k > 0: WriteLine(3); break; default: WriteLine(5); break; }\nWriteLine(k);\n", "3\n1\n")]
    [InlineData("Vec<int> v = [4];\nint k = 1;\nWriteLine(At(ref v, 0) switch { 4 when v.Count == 0 => 1, _ when k > 0 => 3, _ => 5 });\nstatic ref int At(ref Vec<int> w, int i) => ref w[i];\n", "3\n")]
    [InlineData("Option<int> o = Some(4);\nint k = 1;\nWriteLine(o.UnwrapMut() switch { 4 when o.IsSome == false => 1, _ when k > 0 => 3, _ => 5 });\n", "3\n")]
    [InlineData("int a = 4, b = 7;\nbool flag = true;\nref int x = ref a;\nref int y = ref x;\nswitch (y)\n{\n    case 4 when (x = ref b) == 0: WriteLine(1); break;\n    case var _ when flag: WriteLine(2); break;\n    default: WriteLine(3); break;\n}\n", "2\n")]
    [InlineData("int a = 4, b = 7;\nbool flag = true;\nref int x = ref a;\nref int y = ref x;\nWriteLine(y switch { 4 when (x = ref b) == 0 => 1, _ when flag => 2, _ => 3 });\n", "2\n")]
    [InlineData("Vec<int> v = [4];\nWriteLine(At(ref v, 0) switch { 4 when v.Count == 0 => 1, _ => 3 });\nstatic ref int At(ref Vec<int> w, int i) => ref w[i];\n", "3\n")]
    [InlineData("Vec<int> v = [4, 5];\nint k = 1;\nref Vec<int> x = ref v;\nWriteLine(x[0] switch { 4 when v.Count == 0 => 1, _ when k > 0 => 3, _ => 5 });\n", "3\n")]
    public Task AcceptedControlsHaveNativeParity(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);

    // The paired Roslyn borrow stream is applied after y1. These witnesses
    // pin the self decision without requiring Roslyn's successor first.
    [Theory]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nWriteLine(m switch { Some<Box>(var n) when k>0 => n.V, _=>0 });\npublic sealed class Box(int v) { public int V=v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var n) when Touch(ref o) => n.V, _ => 0 });\nstatic bool Touch(ref Option<Box> owner) { owner = None; return true; }\npublic sealed class Box(int v) { public int V=v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(9)); switch(o.AsMut()) { case Some<Box>(var n) when Touch(ref o): WriteLine(n.V); break; default: WriteLine(0); break; }\nstatic bool Touch(ref Option<Box> owner) { owner = None; return true; }\npublic sealed class Box(int v) { public int V=v; }\n", "VIB003")]
    [InlineData("int a = 4;\nint b = 7;\nref int x = ref a;\nref int y = ref x;\nWriteLine(y switch { 4 when (x = ref b) == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x);\n", "VIB003")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nWriteLine(x[0] switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x[0]);\n", "VIB003")]
    [InlineData("Vec<int> v0 = [0];\nVec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v0;\nWriteLine((x = ref v1)[0] switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x[0]);\n", "VIB003")]
    [InlineData("int a = 4;\nint b = 7;\nref int x = ref a;\nWriteLine(AtRo(in x) switch { 4 when (x = ref b) == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x);\nstatic ref readonly int AtRo(in int r) => ref r;\n", "VIB003")]
    [InlineData("int a = 4;\nint b = 7;\nref int x = ref a;\nswitch (AtRo(in x))\n{\n    case 4 when (x = ref b) == 0: WriteLine(1); break;\n    case 7: WriteLine(2); break;\n    case 4: WriteLine(9); break;\n    default: WriteLine(3); break;\n}\nWriteLine(x);\nstatic ref readonly int AtRo(in int r) => ref r;\n", "VIB003")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nswitch (x[0])\n{\n    case 4 when (x = ref v2).Count == 0: WriteLine(1); break;\n    case 7: WriteLine(2); break;\n    case 4: WriteLine(9); break;\n    default: WriteLine(3); break;\n}\nWriteLine(x[0]);\n", "VIB003")]
    [InlineData("int a = 4;\nref readonly int x = ref a;\nref readonly int y = ref x;\nWriteLine(y switch { 4 when (a = 5) == 0 => 1, 4 => 2, _ => 3 });\n", "VIB003")]
    [InlineData("int a = 4, b = 7;\nref readonly int x = ref a;\nref readonly int y = ref x;\nWriteLine(y switch { 4 when (y = ref b) == 0 => 1, 4 => 2, _ => 3 });\n", "VIB003")]
    [InlineData("int a = 4, b = 7;\nref readonly int x = ref a;\nWriteLine(AtRo(x) switch { 4 when (x = ref b) == 0 => 1, 4 => 2, _ => 3 });\nstatic ref readonly int AtRo(in int r) => ref r;\n", "VIB003")]
    [InlineData("int a = 4;\nref readonly int x = ref a;\nref readonly int y = ref x;\nswitch (y) { case 4 when (a = 5) == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\n", "VIB003")]
    [InlineData("int a = 4, b = 7;\nref readonly int x = ref a;\nref readonly int y = ref x;\nswitch (y) { case 4 when (y = ref b) == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\n", "VIB003")]
    [InlineData("int a = 4, b = 7;\nref readonly int x = ref a;\nswitch (AtRo(x)) { case 4 when (x = ref b) == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\nstatic ref readonly int AtRo(in int r) => ref r;\n", "VIB003")]
    [InlineData("Holder h1 = new Holder(4);\nHolder h2 = new Holder(7);\nref Holder x = ref h1;\nWriteLine(x.RefV switch { 4 when (x = ref h2).V == 0 => 1, 4 => 2, _ => 3 });\npublic sealed class Holder(int v) { public int V = v; public ref readonly int RefV => ref V; }\n", "VIB003")]
    [InlineData("Holder h1 = new Holder(4);\nHolder h2 = new Holder(7);\nref Holder x = ref h1;\nWriteLine(x[0] switch { 4 when (x = ref h2).V == 0 => 1, 4 => 2, _ => 3 });\npublic sealed class Holder(int v) { public int V = v; public ref readonly int this[int i] { get => ref V; } }\n", "VIB003")]
    [InlineData("Holder h1 = new Holder(4);\nHolder h2 = new Holder(7);\nref Holder x = ref h1;\nswitch (x.RefV) { case 4 when (x = ref h2).V == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\npublic sealed class Holder(int v) { public int V = v; public ref readonly int RefV => ref V; }\n", "VIB003")]
    [InlineData("Holder h1 = new Holder(4);\nHolder h2 = new Holder(7);\nref Holder x = ref h1;\nswitch (x[0]) { case 4 when (x = ref h2).V == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\npublic sealed class Holder(int v) { public int V = v; public ref readonly int this[int i] { get => ref V; } }\n", "VIB003")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nref int e = ref x[0];\nWriteLine(e switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, _ => 3 });\n", "VIB003")]
    [InlineData("Span<int> s = [1, 2];\nSpan<int> t = [7, 8];\nref int sink = ref s[0];\nref int d = ref sink;\nsink = ref t[1];\nd = 9;\nWriteLine(sink + d + s[0]);\n", "VIB003")]
    public async Task DirectRefAccessAndCursorConflictsKeepExactIds(string source, string expected)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        Assert.Equal(new[] { expected }, result.Self);
    }

    [Theory]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { Some<Box>(var b) => b.V, NoneType when k > 0 => 2, _ => 3 });\npublic sealed class Box(int v) { public int V = v; }\n", "9\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nint k = 1;\nswitch (m) { case Some<Box>(var b): WriteLine(b.V); break; case NoneType when k > 0: WriteLine(2); break; default: WriteLine(3); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "9\n")]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nWriteLine(m switch { not NoneType when k>0 => 8, _=>0 });\npublic sealed class Box(int v) { public int V=v; }\n", "8\n")]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nswitch (m) { case not NoneType when k>0: WriteLine(8); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "8\n")]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nWriteLine(m switch { Some<Box> or NoneType when k>0 => 8, _=>0 });\npublic sealed class Box(int v) { public int V=v; }\n", "8\n")]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nswitch (m) { case Some<Box> or NoneType when k>0: WriteLine(8); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "8\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); var m = o.AsMut(); int k = 1;\nWriteLine(m switch { Some<Box>(_) when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); var m = o.AsMut(); int k = 1;\nswitch (m) { case Some<Box>(_) when k > 0: WriteLine(1); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nint k = 1;\nWriteLine(o.AsMut() switch { Some<Box>(var n) when k > 0 => n.V, _ => 0 });\npublic sealed class Box(int v) { public int V=v; }\n", "9\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); int k = 1;\nswitch(o.AsMut()) { case Some<Box>(var n) when k > 0: WriteLine(n.V); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "9\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); var m = o.AsMut(); int k = 1;\nWriteLine(m switch { Some<Box>(Box _) when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V=v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); var m = o.AsMut(); int k = 1;\nswitch (m) { case Some<Box>(Box _) when k > 0: WriteLine(1); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); var m = o.AsMut(); int k = 1;\nWriteLine(m switch { Some<Box>(Box _) when k < 0 => -1, Some<Box>(var n) => n.V, _ => 0 });\npublic sealed class Box(int v) { public int V=v; }\n", "9\n")]
    [InlineData("int a = 4, b = 7;\nref readonly int x = ref a;\nref readonly int y = ref x;\nWriteLine(y switch { 4 when (x = ref b) == 0 => 1, 4 => 2, _ => 3 });\n", "2\n")]
    [InlineData("Span<int> s = [4, 7]; int i = 0, j = 1; ref readonly int idx = ref i;\nWriteLine(s[idx] switch { 4 when (idx = ref j) == 0 => 1, 4 => 2, _ => 3 });\n", "2\n")]
    [InlineData("int a = 4, b = 7;\nref readonly int x = ref a;\nref readonly int y = ref x;\nswitch (y) { case 4 when (x = ref b) == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\n", "2\n")]
    [InlineData("Span<int> s = [4, 7]; int i = 0, j = 1; ref readonly int idx = ref i;\nswitch (s[idx]) { case 4 when (idx = ref j) == 0: WriteLine(1); break; case 4: WriteLine(2); break; default: WriteLine(3); break; }\n", "2\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { Some<Box> when k < 0 => 2, Some<Box>(var b) => b.V, _ => 3 });\npublic sealed class Box(int v) { public int V = v; }\n", "9\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nint k = 1;\nswitch (m) { case Some<Box> when k < 0: WriteLine(2); break; case Some<Box>(var b): WriteLine(b.V); break; default: WriteLine(3); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "9\n")]
    [InlineData("Option<Box> o = Some(new Box(9)); var m = o.AsMut(); int k = 1;\nswitch (m) { case Some<Box>(Box _) when k < 0: WriteLine(-1); break; case Some<Box>(var n): WriteLine(n.V); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "9\n")]
    [InlineData("int a = 4;\nref int x = ref a;\nref int y = ref x;\nWriteLine(y);\n", "4\n")]
    [InlineData("int a = 4;\nref readonly int x = ref a;\nref readonly int y = ref x;\nWriteLine(y);\n", "4\n")]
    public Task CursorTagsAndCopiedRefSlotsMatchDotnet(string source, string expected) => AssertSelfRuntimeAsync(source, expected);

    // The guarded binding is an independent copy; a failing guard's write
    // must not affect the later arm that matches the original payload.
    [Theory]
    [InlineData("Option<int> o = Some(4);\nswitch (o)\n{\n    case Some<int>(var n) when Inc(ref n): WriteLine(1); break;\n    case Some<int>(4): WriteLine(2); break;\n    case Some<int>(var m): WriteLine(m); break;\n    default: WriteLine(3); break;\n}\nstatic bool Inc(ref int x) { x = 5; return false; }\n", "2\n")]
    [InlineData("Result<int, int> r = Ok(4);\nWriteLine(r switch { Ok<int>(var n) when Inc(ref n) => 1, Ok<int>(4) => 2, _ => 3 });\nstatic bool Inc(ref int x) { x = 5; return false; }\n", "2\n")]
    public Task CopyPayloadBindingsCanBePassedByRefInGuard(string source, string expected) => AssertSelfRuntimeAsync(source, expected);

    [Theory]
    [InlineData("Option<Box> a = Some(new Box(4));\nref Option<Box> x = ref a;\nWriteLine(x switch { Some<Box>(var value) when value.V > 0 => value.V, _ => 0 });\na = None;\nWriteLine(a.IsEmpty);\npublic sealed class Box(int v) { public int V = v; }\n", "4\nTrue\n")]
    [InlineData("Option<Box> a = Some(new Box(4));\nref Option<Box> x = ref a;\nswitch (x)\n{\n    case Some<Box>(var value) when value.V > 0: WriteLine(value.V); break;\n    default: WriteLine(0); break;\n}\na = None;\nWriteLine(a.IsEmpty);\npublic sealed class Box(int v) { public int V = v; }\n", "4\nTrue\n")]
    [InlineData("Option<Box> a = Some(new Box(4));\nWriteLine(Slot(ref a) switch { Some<Box>(var value) when value.V > 0 => value.V, _ => 0 });\na = None;\nWriteLine(a.IsEmpty);\nstatic ref Option<Box> Slot(ref Option<Box> p) => ref p;\npublic sealed class Box(int v) { public int V = v; }\n", "4\nTrue\n")]
    [InlineData("Option<Box> a = Some(new Box(4));\nswitch (Slot(ref a))\n{\n    case Some<Box>(var value) when value.V > 0: WriteLine(value.V); break;\n    default: WriteLine(0); break;\n}\na = None;\nWriteLine(a.IsEmpty);\nstatic ref Option<Box> Slot(ref Option<Box> p) => ref p;\npublic sealed class Box(int v) { public int V = v; }\n", "4\nTrue\n")]
    [InlineData("Option<Box> a = Some(new Box(4));\nOption<Box> b = None;\nref Option<Box> x = ref b;\nWriteLine((x = ref a) switch { Some<Box>(var value) when value.V > 0 => value.V, _ => 0 });\na = None;\nWriteLine(a.IsEmpty);\npublic sealed class Box(int v) { public int V = v; }\n", "4\nTrue\n")]
    [InlineData("Option<Box> a = Some(new Box(4));\nOption<Box> b = None;\nref Option<Box> x = ref b;\nswitch (x = ref a)\n{\n    case Some<Box>(var value) when value.V > 0: WriteLine(value.V); break;\n    default: WriteLine(0); break;\n}\na = None;\nWriteLine(a.IsEmpty);\npublic sealed class Box(int v) { public int V = v; }\n", "4\nTrue\n")]
    public Task GuardedBorrowFromRefReachedPlaceMatchesDotnet(string source, string expected) => AssertSelfRuntimeAsync(source, expected);

    // Exact replay witnesses from the paired borrow contract. Keep the
    // sources embedded: the standalone solution tests require no handoff.
    [Theory]
    [InlineData("Span<int> s = [4, 7];\nSpan<int> t = [8, 9];\nref int sink = ref s[0];\nsink = ref Pick(s, (sink = ref t[0]));\nWriteLine(sink);\nstatic ref int Pick(Span<int> data, int i) => ref data[i & 1];\n", "VIB003")]
    [InlineData("Span<int> s = [4, 7];\nSpan<int> t = [8, 9];\nref int sink = ref s[0];\nsink = ref Pick((sink = ref t[0]), s);\nWriteLine(sink);\nstatic ref int Pick(int i, Span<int> data) => ref data[i & 1];\n", "VIB003")]
    [InlineData("Span<int> s = [4, 7];\nref int sink = ref s[0];\nsink = ref Pick(sink, s);\nWriteLine(sink);\nstatic ref int Pick(int i, Span<int> s) => ref s[i - 3];\n", "VIB003")]
    [InlineData("Span<int> s = [4, 7];\nref int sink = ref s[0];\nsink = ref Pick(sink, s, 0);\nWriteLine(sink);\nstatic ref int Pick(int i, Span<int> data, int tag) => ref data[(i + tag) & 1];\n", "VIB003")]
    [InlineData("Span<int> s = [4, 7];\nref int sink = ref s[0];\nsink = ref Pick(0, sink, s);\nWriteLine(sink);\nstatic ref int Pick(int tag, int i, Span<int> data) => ref data[(i + tag) & 1];\n", "VIB003")]
    [InlineData("Span<int> s = [4, 7];\nint k = 1;\nref int r = ref s[0];\nr = ref Pick(s, (r = ref k));\nWriteLine(r);\nstatic ref int Pick(Span<int> data, int i) => ref data[i];\n", "VIB003")]
    [InlineData("WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Vec<int> v = [1, p, 0, 3]; int o = 5; ref int sink = ref v[0]; sink = ref PickAt(sink, ref v); return sink + o; } static ref int PickAt(int i, ref Vec<int> v) => ref v[i]; }\n", "VIB003")]
    [InlineData("int a = 4;\nint b = 7;\nref int x = ref b;\nWriteLine(((x = ref a)) switch { 4 when (x = ref b) == 0 => 1, _ => 2 });\n", "VIB003")]
    [InlineData("int a = 4;\nint b = 7;\nref int x = ref b;\nswitch (((x = ref a)))\n{\n    case 4 when (x = ref b) == 0: WriteLine(1); break;\n    default: WriteLine(2); break;\n}\n", "VIB003")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nint k = 1;\nbool flag = k > 0;\nr = ref data[Id(Index(flag && k > 0))];\nWriteLine(r);\nstatic int Id(int x) => x;\nstatic int Index(bool b) => b ? 1 : 2;\n", "VIB000")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nint k = 1;\nbool flag = k > 0;\nWriteLine((r = ref data[Id(Index(flag || k == 1))]));\nWriteLine(r);\nstatic int Id(int x) => x;\nstatic int Index(bool b) => b ? 1 : 2;\n", "VIB000")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nint k = 1;\nbool flag = k > 0;\nr = ref data[Id(k switch { 1 => 1, _ => 2 })];\nWriteLine(r);\nstatic int Id(int x) => x;\nWriteLine(flag);\n", "VIB000")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nint k = 1;\nbool flag = k > 0;\nr = ref data[Id(Index(k is 1))];\nWriteLine(r);\nWriteLine(flag);\nstatic int Id(int x) => x;\nstatic int Index(bool b) => b ? 1 : 2;\n", "VIB000")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nint k = 1;\nbool flag = k > 0;\nr = ref data[(r = ref data[flag ? 0 : 1]) > 0 ? 1 : 2];\nr = ref data[Index(flag || k > 0)];\nWriteLine(r);\nstatic int Index(bool b) => b ? 1 : 2;\n", "VIB000")]
    [InlineData("WriteLine(C.F(false));\nstatic class C\n{\nstatic int Unused<T>(bool flag)\n{\n    Span<int> data = [4, 7, 9];\n    ref int r = ref data[0];\n    r = ref data[flag ? 1 : 2];\n    return r;\n}\npublic static int F(bool flag)\n{\n    Span<int> data = [4, 7, 9];\n    ref int r = ref data[0];\n    r = ref data[flag ? 1 : 2];\n    return r;\n}\n}\n", "VIB000")]
    [InlineData("WriteLine(F(false));\nstatic int F(bool flag)\n{\n    Span<int> s = [4, 7];\n    ref int sink = ref s[0];\n    sink = ref Pick(flag ? sink : 0, s);\n    return sink;\n}\nstatic ref int Pick(int i, Span<int> data) => ref data[i & 1];\n", "VIB000")]
    [InlineData("WriteLine(F(false));\nstatic int F(bool flag)\n{\n    Span<int> s = [4, 7];\n    ref int sink = ref s[0];\n    sink = ref Pick(flag && sink > 0, s);\n    return sink;\n}\nstatic ref int Pick(bool second, Span<int> data) => ref data[(second ? 1 : 0)];\n", "VIB000")]
    [InlineData("WriteLine(F(true) + F(false));\nstatic int F(bool flag)\n{\n    Span<int> data = [1, 4, 7];\n    ref int old = ref data[0];\n    old = ref data[flag ? old : 2];\n    return old;\n}\n", "VIB000")]
    [InlineData("WriteLine(F(true) + F(false));\nstatic int F(bool flag)\n{\n    Span<int> data = [1, 4, 7];\n    ref int old = ref data[0];\n    old = ref data[flag ? 1 : 2];\n    return old;\n}\n", "VIB000")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nOption<int> o = Some(3);\nr = ref data[Index(o is Some<int>)];\nWriteLine(r);\nstatic int Index(bool b) => b ? 1 : 2;\n", "VIB000")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => Open(p) switch { Some<R> { Value: var v } when p > 5 => Eat(v) + 100, NoneType => 7, _ => 9 };\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "VIB000")]
    [InlineData("Span<int> data = [4, 7, 9];\nref int r = ref data[0];\nOption<int> o = Some(3);\nr = ref data[Index(o is Some<int> { Value: 3 })];\nWriteLine(r);\nstatic int Index(bool b) => b ? 1 : 2;\n", "VIB000")]
    [InlineData("Option<Nd> o = Some(new Nd(4));\nWriteLine(o switch { Some<Nd>(var d) when (d.V = 14) < 0 => 1, Some<Nd>(var d) when d.V == 14 => 2, _ => 3 });\npublic sealed class Nd(int v) { public int V = v; }\n", "VIB004")]
    [InlineData("Vec<int> init = [4];\nOption<Vec<int>> o = Some(init);\nWriteLine(o switch { Some<Vec<int>>(var v) when (v[0] = 14) < 0 => 1, Some<Vec<int>>(var v) when v[0] == 14 => 2, _ => 3 });\n", "VIB004")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nWriteLine(At(x.AsSpan(), 0) switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x.Count);\nstatic ref int At(Span<int> s, int i) => ref s[i];\n", "VIB003")]
    [InlineData("Vec<Option<int>> v = [Some(4)];\nswitch (v.AsSpan()[0])\n{\n    case Some<int>(4) when Grow(ref v): WriteLine(1); break;\n    case Some<int>(var n): WriteLine(n); break;\n    default: WriteLine(0); break;\n}\nstatic bool Grow(ref Vec<Option<int>> v) { for (int i = 0; i < 64; i++) v.Add(None); v[0] = Some(5); return false; }\n", "VIB003")]
    [InlineData("Vec<Option<int>> v1 = [Some(4)];\nVec<Option<int>> v2 = [Some(7)];\nref Vec<Option<int>> x = ref v1;\nswitch (x.AsSpan()[0])\n{\n    case Some<int>(4) when (x = ref v2).Count == 0 || Grow(ref v1): WriteLine(1); break;\n    case Some<int>(var n): WriteLine(n); break;\n    default: WriteLine(0); break;\n}\nstatic bool Grow(ref Vec<Option<int>> v) { for (int i = 0; i < 64; i++) v.Add(None); v[0] = Some(5); return false; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nWriteLine(F(o.AsMut(), 3));\nstatic int F(OptionMut<Box> m, int k) => m switch { Some<Box>(var b) when k > 0 => b.V, _ => 1 };\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nWriteLine(F(o.AsMut(), 3));\nstatic int F(OptionMut<Box> m, int k) { switch (m) { case Some<Box>(var b) when k > 0: return b.V; default: return 1; } }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nOptionMut<Box> m = o.AsMut();\nWriteLine(m switch { Some<Box>(var b) when b.V > 0 => b.V, _ => 1 });\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nOptionMut<Box> m = o.AsMut();\nint k = 3;\nswitch (m) { case Some<Box>(var b) when k > 0: WriteLine(b.V); break; default: WriteLine(1); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o1 = Some(new Box(9));\nWriteLine(o1.AsMut() switch\n{\n    Some<Box> when Clear(ref o1) => 1,\n    Some<Box> => 9,\n    _ => 0\n});\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nWriteLine(Cur(ref o) switch { Some<Box> when Clear(ref o) => 1, not NoneType => 2, _ => 3 });\nstatic OptionMut<Box> Cur(ref Option<Box> x) => x.AsMut();\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nswitch (o.AsMut())\n{\n    case Some<Box> when Clear(ref o): WriteLine(1); break;\n    case NoneType: WriteLine(5); break;\n    default: WriteLine(2); break;\n}\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(4));\nWriteLine(o.AsMut() switch { Some<Box> when Clear(ref o) => 1, not NoneType => 2, _ => 3 });\nstatic bool Clear(ref Option<Box> r) { r = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    public async Task BorrowContractRejectsWithExactSelfIds(string source, string expected)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { expected }, result.Self);
    }

    [Theory]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nswitch (m) { case not NoneType when k > 0: WriteLine(8); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "8\n")]
    [InlineData("Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;\nswitch (m) { case Some<Box> or NoneType when k > 0: WriteLine(8); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V=v; }\n", "8\n")]
    [InlineData("Option<Box> o = None;\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { not Some<Box> when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = None;\nvar m = o.AsMut();\nint k = 1;\nswitch (m) { case not Some<Box> when k > 0: WriteLine(1); break; default: WriteLine(0); break; }\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nint k = 1;\nWriteLine(m switch { not NoneType when k > 0 => 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "1\n")]
    [InlineData("Result<int, int> r = Err(4);\nswitch (r)\n{\n    case Err<int>(var n) when Inc(ref n): WriteLine(1); break;\n    case Err<int>(4): WriteLine(2); break;\n    default: WriteLine(3); break;\n}\nstatic bool Inc(ref int n) { n = 5; return false; }\n", "2\n")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nWriteLine(x.AsSpan()[0] switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x.Count);\n", "9\n1\n")]
    [InlineData("Vec<int> v1 = [4];\nVec<int> v2 = [7];\nref Vec<int> x = ref v1;\nWriteLine(x.Slice(0, 1)[0] switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });\nWriteLine(x.Count);\n", "9\n1\n")]
    [InlineData("Option<Box> o = Some(new Box(4));\nOptionMut<Box> m = o.AsMut();\nint k = 3;\nint r = m switch { not Some<Box> when k > 0 => 1, _ => 2 };\nWriteLine(r);\npublic sealed class Box(int v) { public int V = v; }\n", "2\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nint k = 1;\nWriteLine(o.AsMut() switch { not Some<Box> when k > 5 => 1, Some<Box>(var c) => c.V + 1, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "10\n")]
    [InlineData("Option<Box> o = Some(new Box(3));\nint k = 1;\nWriteLine(o.AsMut() switch { Some<Box>(var b) when b.V > k => b.V * 2, Some<Box>(var c) => c.V, _ => 0 });\nWriteLine(o.AsMut() switch { Some<Box>(var b) when b.V > 50 => b.V * 2, Some<Box>(var c) => c.V + 100, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\n", "6\n103\n")]
    public Task BorrowContractAcceptsWithRuntimeParity(string source, string expected) => AssertSelfRuntimeAsync(source, expected);

    [Theory]
    [InlineData("Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var b) when (b.V = b.V + 5) < 0 => 1, Some<Box>(var c) => c.V, _ => 0 });\nWriteLine(o.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; }\n", "14\n14\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var b) when (b.V = b.V + 5) < 0 => 1, Some<Box>(var c) => c.V, _ => 0 });\nWriteLine(o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 });\npublic sealed class Box(int v) { public int V = v; }\n", "14\n14\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var b) when (b.V = b.V + 5) < 0 => 1, Some<Box>(var c) when (c.V = c.V * 2) < 0 => 2, Some<Box>(var d) => d.V, _ => 0 });\nWriteLine(o.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; }\n", "28\n28\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var b) when b.Bump() < 0 => 1, Some<Box>(var c) => c.V, _ => 0 });\nWriteLine(o.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; [Mut] public int Bump() { V += 5; return V; } }\n", "14\n14\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var b) when Add5(ref b.V) < 0 => 1, Some<Box>(var c) => c.V, _ => 0 });\nWriteLine(o.Unwrap().V);\nstatic int Add5(ref int x) { x += 5; return x; }\npublic sealed class Box(int v) { public int V = v; }\n", "14\n14\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nswitch (o.AsMut()) { case Some<Box>(var b) when (b.V = b.V + 5) < 0: WriteLine(1); break; case Some<Box>(var c): WriteLine(c.V); break; default: WriteLine(0); break; }\nWriteLine(o.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; }\n", "14\n14\n")]
    public Task RvalueCursorGuardWritesThroughOwnBinding_AcceptsWithNativeParity(string source, string expected) =>
        Round12RuleWitness.AcceptAsync(source, expected);

    [Theory(Skip = "Pending y2a: two-hop cursor mutation through a pattern binding falsely reports VIB003; a naive IsAncestorCarrier skip allows cyclic ref-repoint UAF.")]
    [InlineData("Option<Outer> o = Some(new Outer(Some(new Box(9))));\nWriteLine(o.AsMut() switch { Some<Outer>(var b) when !b.InnerOpt.AsMut().Replace(Some(new Box(42))).IsSome => 1, Some<Outer>(var c) => c.InnerOpt.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 }, _ => 0 });\npublic sealed class Box(int v) { public int V = v; }\npublic sealed class Outer(Option<Box> inner) { public Option<Box> InnerOpt = inner; }\n", "42\n")]
    [InlineData("Option<Outer> o = Some(new Outer(Some(new Box(9))));\nif (o.AsMut() is Some<Outer>(var b)) { _ = b.InnerOpt.AsMut().Replace(Some(new Box(42))); WriteLine(b.InnerOpt.IsSome); }\npublic sealed class Box(int v) { public int V = v; }\npublic sealed class Outer(Option<Box> inner) { public Option<Box> InnerOpt = inner; }\n", "True\n")]
    public Task TwoHopCursorMutationThroughPatternBinding_PendingY2a(string source, string expected) =>
        Round12RuleWitness.AcceptAsync(source, expected);

    [Fact]
    public Task RvalueCursorGuardTouchingOwnerStillRejects() =>
        Round12RuleWitness.RejectAsync(
            "Option<Box> o = Some(new Box(9));\nWriteLine(o.AsMut() switch { Some<Box>(var b) when Kill(ref o) => b.V, Some<Box>(var c) => c.V + 1, _ => 0 });\nstatic bool Kill(ref Option<Box> o) { o = None; return false; }\npublic sealed class Box(int v) { public int V = v; }\n",
            "VIB003", exact: true);

    [Fact]
    public Task TwoHopCursorMutationWithLiveSiblingBorrowStillRejects() =>
        Round12RuleWitness.RejectAsync(
            "Option<Outer> o = Some(new Outer(Some(new Box(9))));\nif (o.AsMut() is Some<Outer>(var b)) { var r = b.InnerOpt.AsRef(); _ = b.InnerOpt.AsMut().Replace(Some(new Box(42))); WriteLine(r.IsSome); }\npublic sealed class Box(int v) { public int V = v; }\npublic sealed class Outer(Option<Box> inner) { public Option<Box> InnerOpt = inner; }\n",
            "VIB003", exact: true);

    [Theory]
    [InlineData("Option<Node> a = Some(new Node(1, Some(new Node(2, None))));\nref Option<Node> rx = ref a;\nvar m = rx.AsMut();\nif (m is Some<Node>(var k)) { rx = ref k.Next; }\nm.Set(None);\nrx = Some(new Node(7, None));\nWriteLine(a.IsSome);\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n")]
    [InlineData("Option<Node> a = Some(new Node(1, Some(new Node(2, None))));\nref Option<Node> rx = ref a;\nvar m = rx.AsMut();\nswitch (m) { case Some<Node>(var k): rx = ref k.Next; break; default: break; }\nm.Set(None);\nrx = Some(new Node(7, None));\nWriteLine(a.IsSome);\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n")]
    [InlineData("Option<Node> a = Some(new Node(1, Some(new Node(2, None))));\nref Option<Node> rx = ref a;\nvar m = rx.AsMut();\nif (m is Some<Node>(var k)) rx = ref k.Next;\nOption<Node> taken = m.Take();\ntaken = None;\nWriteLine(rx.AsRef() switch { Some<Node>(var z) => z.V, _ => -1 });\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n")]
    [InlineData("Option<Node> a = Some(new Node(1, Some(new Node(2, None))));\nref Option<Node> rx = ref a;\nvar m = rx.AsMut();\nif (m is Some<Node>(var k)) { rx = ref k.Next; }\n_ = m.Take();\nWriteLine(rx.IsSome);\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n")]
    [InlineData("Option<Node> a = Some(new Node(1, Some(new Node(2, None))));\nOption<Node> spare = None;\nref Option<Node> rx = ref a;\nvar m = rx.AsMut();\nrx = ref spare;\nif (m is Some<Node>(var k)) { rx = ref k.Next; }\n_ = m.Replace(None);\nWriteLine(rx.IsSome);\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n")]
    [InlineData("Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));\nref Option<Node> rx = ref a;\nvar m = rx.AsMut();\nfor (int i = 0; i < 1; i++) { if (m is Some<Node>(var k)) rx = ref k.Next; }\n_ = m.Replace(Some(new Node(9, None)));\nrx = None;\nWriteLine(a.Unwrap().V);\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n")]
    public Task RefRepointIntoCursorPayloadMutation_RejectsExactVib003(string source) =>
        Round12RuleWitness.RejectAsync(source, "VIB003", exact: true);

    private static async Task AssertSelfRuntimeAsync(string source, string expected)
    {
        // The corresponding Roslyn borrow stream is applied after y1.
        var check = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        Assert.True(check.Self.Length == 0, check.SelfOutput);

        string src = Path.Combine(Round11AuditAssertions.RepoRoot, "src");
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-f13-ref-place-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            string managedDll = Path.Combine(temp, "F13RefPlace.dll");
            string bootstrap = Path.Combine(src, "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "F13RefPlace", includeStd: true);
            var emit = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(File.ReadAllText(bootstrap),
                new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap)).Emit(managedDll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "F13RefPlace.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            string dotnet = Round11AuditAssertions.DotnetExe;
            var managed = Round12RuleWitness.Run(dotnet, null, managedDll);
            Assert.Equal((0, expected, ""), managed);

            string ir = Path.Combine(temp, "self.ll");
            var lower = Round12RuleWitness.Run(dotnet, null, Round11AuditAssertions.SelfCompiler,
                "emit-ll", path, "--sysroot", src, "--asan", "-o", ir);
            Assert.True(lower.Exit == 0, lower.Out + lower.Err);
            var verify = Round12RuleWitness.Run("opt-18", null, "-passes=verify", "-disable-output", ir);
            Assert.True(verify.Exit == 0, verify.Err);
            string native = Path.Combine(temp, "program");
            foreach (string optimization in new[] { "-O0", "-O3" })
            {
                var build = Round12RuleWitness.Run("clang-18", null, optimization, ir, "-o", native, "-fsanitize=address,leak");
                Assert.True(build.Exit == 0, build.Err);
                Assert.Equal(managed, Round12RuleWitness.Run(native, null));
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
