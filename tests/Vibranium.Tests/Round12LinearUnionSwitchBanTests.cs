using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Compiler.Core;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

// Finding N20 (conformance fuzz, union-switch probes). SPEC §2 "Banned Operations on Linear Unions": a switch statement
// or expression that consumes a linear Option<R> / Result<R, E> rvalue (a call, Move(o)) may test each variant in at
// most one arm, with no `when` guard on a variant arm and no `not` or `or` pattern. Each offending arm is exactly VIB006
// on every frontend, reported at the first token of the arm's pattern with one shared message. Before the fix the
// Analyzer and Stage-0 also reported a knock-on VIB001 ("Use of moved value 'temporary value'") for the switch's own
// scrutinee, which its first arm test had consumed, and vib-self accepted the guarded and repeated forms. `_`,
// `default`, `var u`, a guarded `_` arm and one unguarded arm per variant stay accepted (each runs the managed program
// and the Stage-0 and vib-self native binaries at -O0 and at -O3 with ASan/LSan, and R's Dispose shows exact-once
// disposal). Place scrutinees keep their in-place VIB006 (and VIB005 where an arm moves the payload) and get no arm
// diagnostic. Rows marked "NeedsSelfFixW" need the vib-self half of the fix (self-fix W, src/vib-self/Lower/FuncLowerer.cs).
// Each `/*ban*/` comment marks an arm the ban reports: the diagnostic sits on the token after it.
// Residual N21 (both sides rejected with different IDs): a guarded switch EXPRESSION arm over a non-linear !Copy union
// value was collapsed with a complementary next arm, unlike the same guarded case label, so the Analyzer and Stage-0
// reported VIB001 for the re-tested value where vib-self (and the statement form) report VIB005, or accept.
public sealed class Round12LinearUnionSwitchBanTests
{
    internal const string BanMessage =
        "A switch over a linear union value may test each variant in at most one arm, without a 'when' guard, and may not use 'not' or 'or' patterns; branch inside the arm instead.";

    private const string Marker = "/*ban*/ ";

    // Banned arms on a linear rvalue or Move(o) scrutinee: exactly VIB006, one ban diagnostic per marked arm.
    private static readonly Dictionary<string, string> Banned = new(StringComparer.Ordinal)
    {
        ["B01-opt-rvalue-stmt-guarded-some-then-some"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case /*ban*/ Some<R>(var v) when p > 5: n += 100 + Eat(v); break;
                    case /*ban*/ Some<R>(var v): n += Eat(v); break;
                    case NoneType: n += 7; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B02-opt-rvalue-expr-guarded-some-then-some"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { /*ban*/ Some<R>(var v) when p > 5 => 100 + Eat(v), /*ban*/ Some<R>(var v) => Eat(v), NoneType => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B03-opt-rvalue-stmt-guarded-none-then-default"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case /*ban*/ NoneType when p < 2: n += 3; break;
                    case Some<R>(var v): n += Eat(v); break;
                    default: n += 7; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B04-opt-rvalue-expr-guarded-none-then-none"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p) => Open(p) switch { /*ban*/ NoneType when p < 2 => 3, Some<R>(var v) => Eat(v), /*ban*/ NoneType => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B05-opt-move-stmt-guarded-some-then-some"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                Option<R> o = Open(p);
                switch (Move(o))
                {
                    case /*ban*/ Some<R>(var v) when p > 5: n += 100 + Eat(v); break;
                    case /*ban*/ Some<R>(var v): n += Eat(v); break;
                    case NoneType: n += 7; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B06-opt-move-expr-guarded-wrapper-then-some"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                Option<R> o = Open(p);
                return Move(o) switch { /*ban*/ Some<R> s when p > 5 => 100 + Eat(s.Value), /*ban*/ Some<R>(var v) => Eat(v), NoneType => 7 };
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B07-opt-rvalue-stmt-not-none"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case /*ban*/ not NoneType: n += 3; break;
                    default: n += 7; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B08-opt-rvalue-stmt-not-some-then-some"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case /*ban*/ not Some<R>: n += 3; break;
                    case Some<R>(var v): n += Eat(v); break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B09-opt-rvalue-expr-parenthesized-not"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p) => Open(p) switch { /*ban*/ (not NoneType) => 3, _ => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B10-opt-rvalue-stmt-or"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case /*ban*/ NoneType or Some<R>: n += 3; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B11-res-rvalue-stmt-guarded-ok-then-ok"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (OpenR(p))
                {
                    case /*ban*/ Ok<R>(var v) when p > 5: n += 100 + Eat(v); break;
                    case /*ban*/ Ok<R>(var v): n += Eat(v); break;
                    case Err<int>(var e): n += e; break;
                }
                return n;
            }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B12-res-rvalue-expr-guarded-err-then-err"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p) => OpenR(p) switch { Ok<R>(var v) => Eat(v), /*ban*/ Err<int>(var e) when e < 22 => 1, /*ban*/ Err<int>(var e) => e };
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B13-res-rvalue-stmt-not-ok-then-ok"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p)
            {
                int n = 0;
                switch (OpenR(p))
                {
                    case /*ban*/ not Ok<R>: n += 3; break;
                    case Ok<R>(var v): n += Eat(v); break;
                }
                return n;
            }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B14-res-move-stmt-err-or"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p)
            {
                int n = 0;
                Result<R, int> r = OpenR(p);
                switch (Move(r))
                {
                    case Ok<R>(var v): n += Eat(v); break;
                    case /*ban*/ Err<int>(21) or Err<int>(22): n += 1; break;
                    default: n += 7; break;
                }
                return n;
            }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B15-opt-rvalue-expr-guarded-some-then-var"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { /*ban*/ Some<R>(var v) when p > 5 => 100 + Eat(v), var u => EatO(u) };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            static int EatO(Option<R> o) { if (o.IsNone(out var r)) return 0; using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B16-res-rvalue-expr-plain-outer-banned-inner"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => OpenR(p) switch { Ok<R>(var v) => Eat(v) + G(p), Err<int>(var e) => e };
            static int G(int p) => Open(p + 1) switch { /*ban*/ Some<R>(var w) when p > 5 => Eat(w), /*ban*/ Some<R>(var w) => Eat(w) + 1, NoneType => 0 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B17-opt-rvalue-expr-in-local-function"] = """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                return L(p);
                static int L(int q) => Open(q) switch { /*ban*/ Some<R>(var v) when q > 5 => Eat(v) + 100, /*ban*/ Some<R>(var v) => Eat(v), NoneType => 7 };
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["B18-res-rvalue-expr-nested-err-then-err"] = """
            WriteLine(F(1) + F(4) * 10);
            static int F(int p) => OpenR(p) switch { Ok<R>(var v) => Eat(v), Err<int>(21) => 1, /*ban*/ Err<int>(var e) => e };
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
    };

    // The `not` / `or` rows: vib-self already reported exactly VIB006 for them before self-fix W (with its own message).
    private static readonly string[] NotOrNames = ["B07-opt-rvalue-stmt-not-none", "B08-opt-rvalue-stmt-not-some-then-some", "B09-opt-rvalue-expr-parenthesized-not", "B10-opt-rvalue-stmt-or", "B13-res-rvalue-stmt-not-ok-then-ok", "B14-res-move-stmt-err-or"];

    // Accepted on all three frontends before and after the fix; the expected output shows each R disposed exactly once.
    private static readonly Dictionary<string, (string Source, string Output)> Accepted = new(StringComparer.Ordinal)
    {
        ["A01-opt-rvalue-stmt-plain"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case Some<R>(var v): n += Eat(v); break;
                    case NoneType: n += 7; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n847\n"),
        ["A02-opt-rvalue-expr-some-then-discard"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { Some<R>(var v) => Eat(v), _ => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n847\n"),
        ["A03-opt-move-stmt-none-some-default"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                Option<R> o = Open(p);
                switch (Move(o))
                {
                    case NoneType: n += 7; break;
                    case Some<R>(var v): n += Eat(v); break;
                    default: n += 9; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n847\n"),
        ["A04-res-rvalue-stmt-plain"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (OpenR(p))
                {
                    case Ok<R>(var v): n += Eat(v); break;
                    case Err<int>(var e): n += e; break;
                }
                return n;
            }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n861\n"),
        ["A05-res-move-expr-ok-then-discard"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                Result<R, int> r = OpenR(p);
                return Move(r) switch { Ok<R>(var v) => Eat(v), _ => 7 };
            }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n847\n"),
        ["A06-opt-rvalue-stmt-var-whole-union"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case var u: n += EatO(u); break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int EatO(Option<R> o) { if (o.IsNone(out var r)) return 0; using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n840\n"),
        ["A07-opt-rvalue-expr-some-then-var-discard"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { Some<R>(var v) => Eat(v), var _ => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n847\n"),
        ["A08-opt-rvalue-expr-some-then-guarded-discard"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { Some<R>(var v) => Eat(v), _ when p < 2 => 3, _ => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n843\n"),
        ["A09-opt-rvalue-stmt-branch-inside-the-arm"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (Open(p))
                {
                    case Some<R>(var v):
                        if (p > 5) n += 100 + Eat(v);
                        else n += Eat(v);
                        break;
                    case NoneType: n += 7; break;
                }
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n10847\n"),
        ["A10-res-rvalue-expr-branch-inside-the-arm"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => OpenR(p) switch { Ok<R>(var v) => p > 5 ? 100 + Eat(v) : Eat(v), Err<int>(var e) => e < 22 ? 1 : e };
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-4\n-8\n10841\n"),
        ["A11-nonlinear-int-expr-guarded-some-then-some"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => OpenI(p) switch { Some<int>(var v) when v > 5 => 100 + v, Some<int>(var v) => v, NoneType => 7 };
            static Option<int> OpenI(int a) { if (a > 2) return Some(a); return None; }
            """, "10847\n"),
        ["A12-nonlinear-class-stmt-not-then-guarded-some"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                switch (OpenN(p))
                {
                    case not Some<N>: n += 7; break;
                    case Some<N>(var v) when v.Id > 5: n += 100 + v.Id; break;
                    case Some<N>(var v): n += v.Id; break;
                }
                return n;
            }
            static Option<N> OpenN(int a) { if (a > 2) return Some(new N(a)); return None; }
            public sealed class N(int id) { public int Id = id; }
            """, "10847\n"),
    };

    // Place scrutinees: no new rule. Their ID sets are unchanged and equal on all three frontends.
    private static readonly Dictionary<string, (string Source, string[] Ids)> Place = new(StringComparer.Ordinal)
    {
        ["P01-opt-place-stmt-guarded-some-then-some-reading-payload"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                Option<R> o = Open(p);
                switch (o)
                {
                    case Some<R>(var v) when p > 5: n += 100 + v.Id; break;
                    case Some<R>(var v): n += v.Id; break;
                    case NoneType: n += 7; break;
                }
                n += EatO(Move(o));
                return n;
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int EatO(Option<R> o) { if (o.IsNone(out var r)) return 0; using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, ["VIB006"]),
        ["P02-opt-place-expr-not-none"] = ("""
            WriteLine(F(1) + F(4) * 10);
            static int F(int p)
            {
                Option<R> o = Open(p);
                int n = o switch { not NoneType => 3, _ => 7 };
                return n + EatO(Move(o));
            }
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int EatO(Option<R> o) { if (o.IsNone(out var r)) return 0; using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, ["VIB006"]),
        ["P03-res-place-stmt-guarded-err-moving-payload"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p)
            {
                int n = 0;
                Result<R, int> r = OpenR(p);
                switch (r)
                {
                    case Ok<R>(var v): n += Eat(v); break;
                    case Err<int>(var e) when e > 21: n += e; break;
                    default: n += 7; break;
                }
                return n;
            }
            static Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, ["VIB005", "VIB006"]),
    };

    // A guarded switch EXPRESSION arm over a non-linear !Copy union value gets the same verdict as the same guarded case
    // label (G03, H05): its test is not collapsed with a complementary next arm, which used to consume the value before
    // the guard and report a knock-on VIB001 when the next arm tested it again.
    private static readonly Dictionary<string, (string Source, string[] Ids)> GuardedExpressionRejected = new(StringComparer.Ordinal)
    {
        ["G01-vec-rvalue-expr-guarded-wrapper-then-none-then-discard"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => MkV(p) switch { Some<Vec<int>> s when p > 5 => 100 + s.Value.Count, NoneType => 7, _ => 9 };
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, ["VIB005"]),
        ["G02-vec-move-expr-guarded-wrapper-then-none-then-discard"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) { Option<Vec<int>> o = MkV(p); return Move(o) switch { Some<Vec<int>> s when p > 5 => 100 + s.Value.Count, NoneType => 7, _ => 9 }; }
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, ["VIB005"]),
        ["G03-vec-rvalue-stmt-guarded-wrapper-then-none-then-default"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) { int n = 0; switch (MkV(p)) { case Some<Vec<int>> s when p > 5: n += 100 + s.Value.Count; break; case NoneType: n += 7; break; default: n += 9; break; } return n; }
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, ["VIB005"]),
    };

    private static readonly Dictionary<string, (string Source, string Output)> GuardedExpressionAccepted = new(StringComparer.Ordinal)
    {
        ["H01-vec-rvalue-expr-guarded-payload-read-then-none"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => MkV(p) switch { Some<Vec<int>>(var v) when p > 5 => 100 + v.Count, NoneType => 7, _ => 9 };
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, "10297\n"),
        ["H02-vec-rvalue-expr-guarded-none-then-some-read"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => MkV(p) switch { NoneType when p < 2 => 3, Some<Vec<int>>(var v) => v.Count, _ => 9 };
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, "223\n"),
        ["H03-vec-rvalue-expr-guarded-none-then-some-move"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => MkV(p) switch { NoneType when p < 2 => 3, Some<Vec<int>>(var v) => Take(v), _ => 9 };
            static int Take(Vec<int> v) => v.Count;
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, "223\n"),
        ["H04-res-vec-rvalue-expr-guarded-ok-then-err"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => MkR(p) switch { Ok<Vec<int>>(var v) when p > 5 => 100 + v.Count, Err<int>(var e) => e, _ => 9 };
            static Result<Vec<int>, int> MkR(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
            """, "10311\n"),
        ["H05-vec-rvalue-stmt-guarded-payload-read-then-none"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) { int n = 0; switch (MkV(p)) { case Some<Vec<int>>(var v) when p > 5: n += 100 + v.Count; break; case NoneType: n += 7; break; default: n += 9; break; } return n; }
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, "10297\n"),
        ["H06-int-rvalue-expr-guarded-wrapper-then-none"] = ("""
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => MkI(p) switch { Some<int> s when p > 5 => 100 + s.Value, NoneType => 7, _ => 9 };
            static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
            """, "10897\n"),
    };

    // A genuine earlier move of the scrutinee is still reported by the switch's first arm test.
    private const string EarlierMove = """
        WriteLine(F(1) + F(4) * 10);
        static int F(int p)
        {
            int n = 0;
            Option<R> o = Open(p);
            n += EatO(o);
            switch (Move(o))
            {
                case /*ban*/ Some<R>(var v) when p > 5: n += 100 + Eat(v); break;
                case /*ban*/ Some<R>(var v): n += Eat(v); break;
                case NoneType: n += 7; break;
            }
            return n;
        }
        static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
        static int Eat(R r) { using var u = r; return u.Id; }
        static int EatO(Option<R> o) { if (o.IsNone(out var r)) return 0; using var u = r; return u.Id; }
        public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
        """;

    public static TheoryData<string> BannedNames => new(Banned.Keys);
    public static TheoryData<string> NotOrBannedNames => new(NotOrNames);
    public static TheoryData<string> AcceptedNames => new(Accepted.Keys);
    public static TheoryData<string> PlaceNames => new(Place.Keys);
    public static TheoryData<string> GuardedExpressionRejectedNames => new(GuardedExpressionRejected.Keys);
    public static TheoryData<string> GuardedExpressionAcceptedNames => new(GuardedExpressionAccepted.Keys);

    [Fact]
    public void NotOrNames_AreBannedRows() => Assert.All(NotOrNames, name => Assert.True(Banned.ContainsKey(name), name));

    [Theory]
    [MemberData(nameof(BannedNames))]
    public async Task Roslyn_BannedArm_IsExactlyVib006AtEachMarkedArm(string name)
    {
        string source = Banned[name];
        var (stage0, analyzer) = await RoslynAsync(source);
        Assert.Equal(new[] { "VIB006" }, stage0.Select(d => d.Id).Distinct().ToArray());
        Assert.Equal(new[] { "VIB006" }, analyzer.Select(d => d.Id).Distinct().ToArray());
        int[] anchors = MarkedAnchors(source);
        Assert.NotEmpty(anchors);
        Assert.Equal(anchors, stage0.Where(d => d.Message == BanMessage).Select(d => d.Span.Start).Order().ToArray());
        Assert.Equal(anchors, analyzer.Where(d => d.GetMessage() == BanMessage).Select(d => d.Location.SourceSpan.Start).Order().ToArray());
    }

    [Theory]
    [MemberData(nameof(NotOrBannedNames))]
    public async Task NotOrArm_IsExactlyVib006OnAllThree(string name)
    {
        var result = await Round12RuleWitness.CheckAllAsync(Banned[name]);
        Assert.Equal(new[] { "VIB006" }, result.Stage0);
        Assert.Equal(new[] { "VIB006" }, result.Analyzer);
        Assert.True(result.Self.SequenceEqual(new[] { "VIB006" }), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }

    [Theory]
    [MemberData(nameof(BannedNames))]
    public async Task BannedArm_IsExactlyVib006AtEachMarkedArmOnAllThree_NeedsSelfFixW(string name)
    {
        string source = Banned[name];
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { "VIB006" }, result.Stage0);
        Assert.Equal(new[] { "VIB006" }, result.Analyzer);
        Assert.True(result.Self.SequenceEqual(new[] { "VIB006" }), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
        var expected = MarkedAnchors(source).Select(offset => LineColumn(source, offset)).ToArray();
        var self = Regex.Matches(result.SelfOutput, @"\((\d+),(\d+)\): error VIB006: " + Regex.Escape(BanMessage))
            .Select(m => (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))).Order().ToArray();
        Assert.True(expected.SequenceEqual(self), $"vib-self ban diagnostics at [{string.Join(",", self)}], expected [{string.Join(",", expected)}]\n{result.SelfOutput}");
    }

    [Theory]
    [MemberData(nameof(AcceptedNames))]
    public async Task Accepted_StaysAcceptedAndRunsTheSameNatively(string name)
    {
        var (source, output) = Accepted[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(PlaceNames))]
    public async Task PlaceScrutinee_KeepsItsIdSetWithoutArmDiagnostics(string name)
    {
        var (source, ids) = Place[name];
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(ids, result.Stage0);
        Assert.Equal(ids, result.Analyzer);
        Assert.True(result.Self.SequenceEqual(ids), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
        var (stage0, analyzer) = await RoslynAsync(source);
        Assert.DoesNotContain(stage0, d => d.Message == BanMessage);
        Assert.DoesNotContain(analyzer, d => d.GetMessage() == BanMessage);
        Assert.DoesNotContain(BanMessage, result.SelfOutput, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(GuardedExpressionRejectedNames))]
    public async Task GuardedArm_HasTheSameIdSetOnEveryFrontend(string name)
    {
        var (source, ids) = GuardedExpressionRejected[name];
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(ids, result.Stage0);
        Assert.Equal(ids, result.Analyzer);
        Assert.True(result.Self.SequenceEqual(ids), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }

    [Theory]
    [MemberData(nameof(GuardedExpressionAcceptedNames))]
    public async Task GuardedArm_IsAcceptedAndRunsTheSameNatively(string name)
    {
        var (source, output) = GuardedExpressionAccepted[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Fact]
    public async Task Roslyn_EarlierMoveOfTheScrutinee_IsStillVib001()
    {
        var (stage0, analyzer) = await RoslynAsync(EarlierMove);
        Assert.Equal(new[] { "VIB001", "VIB006" }, stage0.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "VIB001", "VIB006" }, analyzer.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.Contains(stage0, d => d.Id == "VIB001" && d.Message == "Use of moved value 'o'.");
        Assert.Contains(analyzer, d => d.Id == "VIB001" && d.GetMessage() == "Use of moved value 'o'.");
        Assert.Equal(MarkedAnchors(EarlierMove), stage0.Where(d => d.Message == BanMessage).Select(d => d.Span.Start).Order().ToArray());
    }

    [Fact]
    public async Task EarlierMoveOfTheScrutinee_IsVib001AndVib006OnAllThree_NeedsSelfFixW()
    {
        var result = await Round12RuleWitness.CheckAllAsync(EarlierMove);
        Assert.Equal(new[] { "VIB001", "VIB006" }, result.Stage0);
        Assert.Equal(new[] { "VIB001", "VIB006" }, result.Analyzer);
        Assert.True(result.Self.SequenceEqual(new[] { "VIB001", "VIB006" }), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }

    private static int[] MarkedAnchors(string source)
    {
        var anchors = new List<int>();
        for (int at = source.IndexOf(Marker, StringComparison.Ordinal); at >= 0; at = source.IndexOf(Marker, at + 1, StringComparison.Ordinal))
        {
            anchors.Add(at + Marker.Length);
        }
        return [.. anchors];
    }

    private static (int Line, int Column) LineColumn(string source, int offset)
    {
        int lineStart = source.LastIndexOf('\n', offset - 1) + 1;
        return (source.Take(offset).Count(c => c == '\n') + 1, offset - lineStart + 1);
    }

    // The Stage-0 and Analyzer diagnostics of a C#-valid, warning-free program.
    private static async Task<(VibraniumDiagnostic[] Stage0, Diagnostic[] Analyzer)> RoslynAsync(string source)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-linear-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "LinearUnionSwitchBan", includeStd: true);
            var csharp = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.True(csharp.Length == 0, "a row must be C#-valid and warning-free:\n" + string.Join("\n", csharp.Select(d => d.ToString())));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics.ToArray();
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            return (stage0, [.. analyzer]);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
