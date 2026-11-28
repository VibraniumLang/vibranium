using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Finding P1-4 of the views-of-temporaries probe. An rvalue scrutinee of a negated single-arm union pattern
// (`Make() is not Some<T>(var v)`, `is not Ok<T>(var v)`, `is not not ...`, `is not Some<T> s`, nested
// `Some<Option<T>>(Some<T>(var v))`) is consumed by the test exactly as by the positive form, so its bindings own the
// payload (SPEC §3.4 "Rvalue Scrutinees", L1 linear unions without nested subpatterns included): they may be moved,
// mutated, disposed and borrowed after the `if`. Place scrutinees, OptionRef rvalues and logical patterns keep
// borrowing, and the linear bans (an unbound linear arm, nested subpatterns, place matches) are unchanged. Every
// accepted program runs on the Analyzer, Stage-0 and vib-self, and the managed run is compared with the Stage-0 and
// vib-self native binaries at -O0 and at -O3 with ASan/LSan.
public sealed class Round12IsNotRvalueScrutineeTests
{
    // Rejected by the Analyzer and Stage-0 before the fix (VIB004, VIB005 or VIB007 on the binding); accepted by vib-self.
    private static readonly Dictionary<string, (string Source, string Output)> NewlyAccepted = new(StringComparer.Ordinal)
    {
        ["K01-isnot-rvalue-binding-move"] = ("""
            Run();

            static void Run()
            {
                if (MakeNodeV(7) is not Some<NodeV>(var n)) return;
                Sink(n);
            }
            static void Sink(NodeV x) => WriteLine(x.Items[0]);
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "7\n"),
        ["K04-loop-isnot-rvalue-binding-move-out"] = ("""
            Run();

            static void Run()
            {
                NodeV keep = new NodeV(0);
                for (int i = 1; i <= 2; i++)
                {
                    if (MakeNodeV(i * 10) is not Some<NodeV>(var n)) return;
                    if (i == 1) keep = n;
                }
                WriteLine(keep.Items[0]);
            }
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "10\n"),
        ["K05-isnot-rvalue-binding-mutate"] = ("""
            Run();

            static void Run()
            {
                if (MakeNodeV(7) is not Some<NodeV>(var n)) return;
                n.Items.Add(5);
                WriteLine(n.Items.Count);
            }
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "3\n"),
        ["K09-isnot-rvalue-vec-binding-mutate"] = ("""
            Run();

            static void Run()
            {
                if (MakeOptVec(7) is not Some<Vec<int>>(var v)) return;
                v.Add(4);
                WriteLine(v.Count);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "3\n"),
        ["K10-loop-isnot-rvalue-vec-move-out"] = ("""
            Run();

            static void Run()
            {
                Vec<int> keep = [0];
                for (int i = 1; i <= 2; i++)
                {
                    if (MakeOptVec(i * 10) is not Some<Vec<int>>(var v)) return;
                    if (i == 1) keep = v;
                }
                WriteLine(keep[0]);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "10\n"),
        ["K11-isnot-rvalue-vec-move"] = ("""
            Run();

            static void Run()
            {
                if (MakeOptVec(7) is not Some<Vec<int>>(var v)) return;
                Vec<int> w = v;
                WriteLine(w[0]);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "7\n"),
        ["K15-isnot-rvalue-vec-view-after"] = ("""
            Run();

            static void Run()
            {
                if (MakeOptVec(7) is not Some<Vec<int>>(var v)) return;
                ReadOnlySpan<int> s = v.AsReadOnlySpan();
                WriteLine(s[1]);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "8\n"),
        ["E19-is-not-rvalue-owner-view-after"] = ("""
            Run();

            static void Run()
            {
                if (MakeNodeV(7) is not Some<NodeV>(var n)) return;
                ReadOnlySpan<int> s = n.Items.AsReadOnlySpan();
                WriteLine(s[1]);
            }
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "8\n"),
        ["N01-isnot-result-ok-move"] = ("""
            Run();
            static void Run()
            {
                if (MakeRes(7) is not Ok<Vec<int>>(var v)) return;
                Vec<int> w = v;
                WriteLine(w[1]);
            }
            static Result<Vec<int>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; return Ok(v); }
            """, "8\n"),
        ["N02-isnot-result-err-move"] = ("""
            Run();
            static void Run()
            {
                if (MakeRes(7) is not Err<Vec<int>>(var e)) return;
                Vec<int> w = e;
                WriteLine(w[0]);
            }
            static Result<int, Vec<int>> MakeRes(int a) { Vec<int> v = [a, a + 2]; return Err(v); }
            """, "7\n"),
        ["N03-isnot-nested-some-move"] = ("""
            Run();
            static void Run()
            {
                if (MakeOO(7) is not Some<Option<Vec<int>>>(Some<Vec<int>>(var v))) return;
                Vec<int> w = v;
                WriteLine(w[0]);
            }
            static Option<Option<Vec<int>>> MakeOO(int a) { Vec<int> v = [a, a + 1]; Option<Vec<int>> inner = Some(v); return Some(inner); }
            """, "7\n"),
        ["N06-isnot-some-none-path"] = ("""
            Run(0);
            Run(1);
            static void Run(int k)
            {
                if (Make(k) is not Some<Vec<int>>(var v)) { WriteLine(-1); return; }
                v.Add(9);
                WriteLine(v.Count);
            }
            static Option<Vec<int>> Make(int k) { if (k == 0) return None; Vec<int> v = [k]; return Some(v); }
            """, "-1\n2\n"),
        ["N07-double-negation"] = ("""
            Run();
            static void Run()
            {
                if (MakeOptVec(7) is not not Some<Vec<int>>(var v))
                {
                    Vec<int> w = v;
                    WriteLine(w[0]);
                }
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "7\n"),
        ["N17-isnot-loop-continue-move"] = ("""
            Vec<int> total = [];
            for (int i = 0; i < 4; i++)
            {
                if (Make(i) is not Some<Vec<int>>(var v)) continue;
                total = v;
            }
            WriteLine(total.Count);
            static Option<Vec<int>> Make(int k) { if (k % 2 == 0) return None; Vec<int> v = [k, k]; return Some(v); }
            """, "2\n"),
        ["N23-isnot-wrapper-designation"] = ("""
            Run();
            static void Run()
            {
                if (MakeOptVec(7) is not Some<Vec<int>> s) return;
                Option<Vec<int>> o = s;
                WriteLine(o.IsSome);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "True\n"),
        ["N24-isnot-result-nested-subpattern"] = ("""
            Run();
            static void Run()
            {
                if (MakeRes(7) is not Ok<Option<Vec<int>>>(Some<Vec<int>>(var v))) return;
                Vec<int> w = v;
                WriteLine(w[1]);
            }
            static Result<Option<Vec<int>>, int> MakeRes(int a) { Vec<int> v = [a, a + 1]; Option<Vec<int>> inner = Some(v); return Ok(inner); }
            """, "8\n"),
        ["N25-isnot-move-in-while-cond-scope"] = ("""
            int i = 0;
            Vec<int> keep = [0];
            while (i < 3)
            {
                i++;
                if (MakeOptVec(i) is not Some<Vec<int>>(var v)) break;
                keep = v;
            }
            WriteLine(keep[0]);
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "3\n"),
    };

    // Linear `Option<R>` / `Result<R, int>` rvalues: rejected by the Analyzer and Stage-0 before the fix ({VIB005, VIB006});
    // accepted by vib-self. Exact-once disposal on every path: early return, continue, break, using var, a by-value call,
    // a returned binding, a wrapper designation and Move(x).
    private static readonly Dictionary<string, (string Source, string Output)> NewlyAcceptedLinear = new(StringComparer.Ordinal)
    {
        ["N21-isnot-linear-resource"] = ("""
            Run();
            static void Run()
            {
                if (Open(1) is not Some<R>(var r)) return;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) => Some(new R(id));
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "1\n-1\n"),
        ["L01-isnot-some-using"] = ("""
            Run(1);
            Run(0);
            static void Run(int k)
            {
                if (Open(k) is not Some<R>(var r)) { WriteLine(0); return; }
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "1\n-1\n0\n"),
        ["L02-isnot-some-consume-call"] = ("""
            Run(2);
            Run(0);
            static void Run(int k)
            {
                if (Open(k) is not Some<R>(var r)) return;
                Consume(r);
            }
            static void Consume(R r) { using var rr = r; WriteLine(rr.Id); }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "2\n-2\n"),
        ["L03-isnot-some-loop-continue"] = ("""
            for (int i = 0; i < 4; i++)
            {
                if (Open(i % 2 == 0 ? 0 : i) is not Some<R>(var r)) continue;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "1\n-1\n3\n-3\n"),
        ["L04-isnot-some-loop-break"] = ("""
            int i = 3;
            while (true)
            {
                if (Open(i) is not Some<R>(var r)) break;
                using var rr = r;
                WriteLine(rr.Id);
                i--;
            }
            WriteLine(i);
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "3\n-3\n2\n-2\n1\n-1\n0\n"),
        ["L05-isnot-ok-using"] = ("""
            Run(3);
            Run(0);
            static void Run(int k)
            {
                if (OpenRes(k) is not Ok<R>(var r)) { WriteLine(0); return; }
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Result<R, int> OpenRes(int id) { if (id == 0) return Err(42); return Ok(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "3\n-3\n0\n"),
        ["L06-isnot-not-some-using"] = ("""
            Run(4);
            static void Run(int k)
            {
                if (Open(k) is not not Some<R>(var r))
                {
                    using var rr = r;
                    WriteLine(rr.Id);
                }
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "4\n-4\n"),
        ["L07-isnot-some-return-owned"] = ("""
            using var a = Take(5);
            WriteLine(a.Id);
            static R Take(int k)
            {
                if (Open(k) is not Some<R>(var r)) return new R(-1);
                return r;
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "5\n-5\n"),
        ["L14-isnot-wrapper-designation"] = ("""
            Run(6);
            static void Run(int k)
            {
                if (Open(k) is not Some<R> s) return;
                Option<R> o = s;
                if (o.IsNone(out var r)) return;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "6\n-6\n"),
        ["L16-isnot-move-scrutinee"] = ("""
            Run(1);
            static void Run(int k)
            {
                Option<R> o = Open(k);
                if (Move(o) is not Some<R>(var r)) return;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "1\n-1\n"),
    };

    // Accepted by all three before and after the fix: negated patterns without a binding, switch arms on an rvalue
    // (a guarded negated arm followed by an arm that tests the value again keeps borrowing it), the positive forms
    // and place reads.
    private static readonly Dictionary<string, (string Source, string Output)> Unchanged = new(StringComparer.Ordinal)
    {
        ["N04-isnot-nested-none-taken"] = ("""
            Run();
            static void Run()
            {
                if (MakeOO(7) is not Some<Option<Vec<int>>>(Some<Vec<int>>(var v))) { WriteLine(0); return; }
                WriteLine(v.Count);
            }
            static Option<Option<Vec<int>>> MakeOO(int a) { Option<Vec<int>> inner = None; return Some(inner); }
            """, "0\n"),
        ["N05-isnot-none-noname-leakcheck"] = ("""
            for (int i = 0; i < 3; i++)
            {
                if (MakeOptVec(i) is not NoneType) WriteLine(i);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "0\n1\n2\n"),
        ["N14-switch-expr-not-none"] = ("""
            int n = MakeOptVec(7) switch { not NoneType => 1, _ => 0 };
            WriteLine(n);
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "1\n"),
        ["N15-switch-stmt-case-not-none"] = ("""
            switch (MakeOptVec(7))
            {
                case not NoneType:
                {
                    WriteLine(1);
                    break;
                }
                default:
                {
                    WriteLine(0);
                    break;
                }
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "1\n"),
        ["N26-switch-stmt-not-none-guard-then-some"] = ("""
            int k = 3;
            switch (MakeOptVec(7))
            {
                case not NoneType when k > 5:
                {
                    WriteLine(1);
                    break;
                }
                case Some<Vec<int>>(var v):
                {
                    WriteLine(v.Count + v[0]);
                    break;
                }
                default:
                {
                    WriteLine(0);
                    break;
                }
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "9\n"),
        ["N28-switch-expr-not-none-guard-then-default"] = ("""
            int k = 3;
            int n = MakeOptVec(7) switch { not NoneType when k > 5 => 1, _ => 0 };
            WriteLine(n);
            int m = MakeOptVec(8) switch { not NoneType when k > 2 => 1, _ => 0 };
            WriteLine(m);
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "0\n1\n"),
        ["N20-isnot-constant-subpattern"] = ("""
            for (int i = 4; i < 7; i++)
            {
                if (MakeOpt(i) is not Some<int>(5)) WriteLine(i);
            }
            static Option<int> MakeOpt(int a) => Some(a);
            """, "4\n6\n"),
        ["N22-is-expression-value-not"] = ("""
            bool b = MakeOptVec(7) is not NoneType;
            WriteLine(b);
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "True\n"),
        ["K02-is-rvalue-binding-move-body"] = ("""
            if (MakeNodeV(7) is Some<NodeV>(var n)) Sink(n);

            static void Sink(NodeV x) => WriteLine(x.Items[0]);
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "7\n"),
        ["K03-and-rvalue-binding-move"] = ("""
            bool ok = MakeNodeV(7) is Some<NodeV>(var n) && Sink(n);
            WriteLine(ok);

            static bool Sink(NodeV x) { WriteLine(x.Items[0]); return true; }
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "7\nTrue\n"),
        ["K06-is-rvalue-binding-mutate-body"] = ("""
            if (MakeNodeV(7) is Some<NodeV>(var n))
            {
                n.Items.Add(5);
                WriteLine(n.Items.Count);
            }

            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "3\n"),
        ["K07-while-and-rvalue-binding-move"] = ("""
            int i = 0;
            while (i < 3 && MakeNodeV(i) is Some<NodeV>(var n))
            {
                Sink(n);
                i = i + 1;
            }

            static void Sink(NodeV x) => WriteLine(x.Items[0]);
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "0\n1\n2\n"),
        ["K12-is-rvalue-vec-move-body"] = ("""
            if (MakeOptVec(7) is Some<Vec<int>>(var v))
            {
                Vec<int> w = v;
                WriteLine(w[0]);
            }

            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "7\n"),
        ["K13-and-rvalue-vec-move-loop"] = ("""
            Vec<int> keep = [0];
            for (int i = 1; i <= 2; i++)
            {
                if (i > 0 && MakeOptVec(i * 10) is Some<Vec<int>>(var v) && i == 1) keep = v;
            }
            WriteLine(keep[0]);

            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "10\n"),
        ["K14-ternary-rvalue-vec-binding"] = ("""
            int n = MakeOptVec(7) is Some<Vec<int>>(var v) ? Count(v) : 0;
            WriteLine(n);

            static int Count(Vec<int> x) => x.Count;
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "2\n"),
        ["E19b-is-rvalue-owner-view-in-body"] = ("""
            if (MakeNodeV(7) is Some<NodeV>(var n))
            {
                ReadOnlySpan<int> s = n.Items.AsReadOnlySpan();
                WriteLine(s[1]);
            }

            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "8\n"),
        ["N08-not-parenthesized-is"] = ("""
            Run();
            static void Run()
            {
                if (!(MakeOptVec(7) is Some<Vec<int>>(var v))) return;
                Vec<int> w = v;
                WriteLine(w[1]);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "8\n"),
        ["N11-place-isnot-read"] = ("""
            Run();
            static void Run()
            {
                var o = MakeOptVec(7);
                if (o is not Some<Vec<int>>(var v)) return;
                WriteLine(v.Count + v[0]);
                WriteLine(o.IsSome);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "9\nTrue\n"),
        ["N12-switch-stmt-rvalue-move"] = ("""
            switch (MakeOptVec(7))
            {
                case Some<Vec<int>>(var v):
                {
                    Vec<int> w = v;
                    WriteLine(w[0]);
                    break;
                }
                case NoneType:
                {
                    WriteLine(0);
                    break;
                }
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "7\n"),
        ["N13-switch-expr-rvalue-move"] = ("""
            int n = MakeOptVec(7) switch { Some<Vec<int>>(var v) => Count(v), _ => 0 };
            WriteLine(n);
            static int Count(Vec<int> x) => x.Count;
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "2\n"),
        ["N19-isnot-optionref-rvalue"] = ("""
            Option<Vec<int>> o = MakeOptVec(7);
            WriteLine(Peek(o));
            static int Peek(in Option<Vec<int>> o)
            {
                if (o.AsRef() is not Some<Vec<int>>(var v)) return -1;
                return v.Count;
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "2\n"),
        ["N21b-is-linear-resource-positive"] = ("""
            Run();
            static void Run()
            {
                if (Open(1) is Some<R>(var r))
                {
                    using var rr = r;
                    WriteLine(rr.Id);
                }
            }
            static Option<R> Open(int id) => Some(new R(id));
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "1\n-1\n"),
    };

    // Exact ID sets on all three frontends, before and after the fix except E19c and E24 (which were {VIB007} and
    // {VIB003, VIB007} on the Analyzer and Stage-0): a view of an owned binding kept past its scope is VIB003; place
    // scrutinees and OptionRef rvalues keep borrowing; logical patterns are cut (VIB000); an unbound linear arm, an
    // unconsumed linear binding, a linear nested subpattern and a linear error type stay VIB006, as in the positive form.
    private static readonly Dictionary<string, (string Source, string Id)> Rejected = new(StringComparer.Ordinal)
    {
        ["E19c-is-not-rvalue-owner-view-outer"] = ("""
            Run();

            static void Run()
            {
                Vec<int> keep = [1];
                ReadOnlySpan<int> s = keep.AsReadOnlySpan();
                {
                    if (MakeNodeV(7) is not Some<NodeV>(var n)) return;
                    s = n.Items.AsReadOnlySpan();
                }
                WriteLine(s[1]);
            }
            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "VIB003"),
        ["E24-loop-is-not-rvalue-owner-view"] = ("""
            Vec<int> keep = [1];
            ReadOnlySpan<int> saved = keep.AsReadOnlySpan();
            for (int i = 1; i <= 3; i++)
            {
                if (MakeNodeV(i * 10) is not Some<NodeV>(var n)) return;
                if (i == 1) saved = n.Items.AsReadOnlySpan();
            }
            WriteLine(saved[0]);

            static Option<NodeV> MakeNodeV(int v) => Some(new NodeV(v));
            sealed class NodeV { public Vec<int> Items; public NodeV(int v) { Items = [v, v + 1]; } }
            """, "VIB003"),
        ["N09-place-isnot-move"] = ("""
            Run();
            static void Run()
            {
                var o = MakeOptVec(7);
                if (o is not Some<Vec<int>>(var v)) return;
                Vec<int> w = v;
                WriteLine(w[0]);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "VIB005"),
        ["N10-place-isnot-mutate"] = ("""
            Run();
            static void Run()
            {
                var o = MakeOptVec(7);
                if (o is not Some<Vec<int>>(var v)) return;
                v.Add(4);
                WriteLine(v.Count);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "VIB004"),
        ["N19b-isnot-optionref-rvalue-mutate"] = ("""
            Option<Vec<int>> o = MakeOptVec(7);
            WriteLine(Peek(o));
            static int Peek(in Option<Vec<int>> o)
            {
                if (o.AsRef() is not Some<Vec<int>>(var v)) return -1;
                v.Add(1);
                return v.Count;
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "VIB004"),
        ["N16-isnot-binary-and-read"] = ("""
            Run();
            static void Run()
            {
                if (MakeOptVec(7) is not (Some<Vec<int>>(var v) and Some<Vec<int>>)) return;
                WriteLine(v.Count);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "VIB000"),
        ["N16b-isnot-binary-and-move"] = ("""
            Run();
            static void Run()
            {
                if (MakeOptVec(7) is not (Some<Vec<int>>(var v) and Some<Vec<int>>)) return;
                Vec<int> w = v;
                WriteLine(w[0]);
            }
            static Option<Vec<int>> MakeOptVec(int a) { Vec<int> v = [a, a + 1]; return Some(v); }
            """, "VIB000"),
        ["L08-isnot-some-unconsumed-binding"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is not Some<R>(var r)) return;
                WriteLine(r.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L08p-is-some-unconsumed-binding"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is Some<R>(var r)) WriteLine(r.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L09-isnot-none-unbound-some"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is not NoneType) WriteLine(1);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L09p-is-none-unbound-some"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is NoneType) WriteLine(1);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L10-isnot-err-unbound-ok"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (OpenRes(k) is not Err<int>(var e)) return;
                WriteLine(e);
            }
            static Result<R, int> OpenRes(int id) { if (id == 0) return Err(42); return Ok(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L10p-is-err-unbound-ok"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (OpenRes(k) is Err<int>(var e)) WriteLine(e);
            }
            static Result<R, int> OpenRes(int id) { if (id == 0) return Err(42); return Ok(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L11-isnot-some-type-pattern-no-binding"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is not Some<R>) return;
                WriteLine(2);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L11p-is-some-type-pattern-no-binding"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is Some<R>) WriteLine(2);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L12-isnot-nested-subpattern"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (OpenOO(k) is not Some<Option<R>>(Some<R>(var r))) return;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<Option<R>> OpenOO(int id) { Option<R> inner = Some(new R(id)); return Some(inner); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L12p-is-nested-subpattern"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (OpenOO(k) is Some<Option<R>>(Some<R>(var r)))
                {
                    using var rr = r;
                    WriteLine(rr.Id);
                }
            }
            static Option<Option<R>> OpenOO(int id) { Option<R> inner = Some(new R(id)); return Some(inner); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
        ["L13-isnot-and-pattern"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Open(k) is not (Some<R>(var r) and Some<R>)) return;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB000"),
        ["L17-isnot-ok-err-linear-payload"] = ("""
            Run(1);
            static void Run(int k)
            {
                if (Make(k) is not Ok<int>(var v)) return;
                WriteLine(v);
            }
            static Result<int, R> Make(int k) => Ok(k);
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006"),
    };

    // A place match on a linear union is a banned operation (VIB006, SPEC §2): every frontend reports it, and the
    // diagnostics after it may differ (SPEC §4). The Analyzer and Stage-0 also report VIB005 for the move out of the
    // borrowed binding; vib-self stops at the ban. Unchanged by the fix.
    private static readonly Dictionary<string, string> LinearPlaceBan = new(StringComparer.Ordinal)
    {
        ["L15-isnot-place-linear"] = """
            Run(1);
            static void Run(int k)
            {
                Option<R> o = Open(k);
                if (o is not Some<R>(var r)) return;
                using var rr = r;
                WriteLine(rr.Id);
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
        ["L15p-is-place-linear"] = """
            Run(1);
            static void Run(int k)
            {
                Option<R> o = Open(k);
                if (o is Some<R>(var r))
                {
                    using var rr = r;
                    WriteLine(rr.Id);
                }
            }
            static Option<R> Open(int id) { if (id == 0) return None; return Some(new R(id)); }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """,
    };

    public static TheoryData<string> NewlyAcceptedNames => new(NewlyAccepted.Keys);
    public static TheoryData<string> NewlyAcceptedLinearNames => new(NewlyAcceptedLinear.Keys);
    public static TheoryData<string> UnchangedNames => new(Unchanged.Keys);
    public static TheoryData<string> RejectedNames => new(Rejected.Keys);
    public static TheoryData<string> LinearPlaceBanNames => new(LinearPlaceBan.Keys);

    [Theory]
    [MemberData(nameof(NewlyAcceptedNames))]
    public async Task NegatedPatternOnRvalue_BindsByOwnership(string name)
    {
        var (source, output) = NewlyAccepted[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(NewlyAcceptedLinearNames))]
    public async Task NegatedPatternOnLinearRvalue_DisposesExactlyOnce(string name)
    {
        var (source, output) = NewlyAcceptedLinear[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(UnchangedNames))]
    public async Task Neighbour_StaysAcceptedAndRunsAlike(string name)
    {
        var (source, output) = Unchanged[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public async Task Neighbour_HasTheSameIdSetOnEveryFrontend(string name)
    {
        var (source, id) = Rejected[name];
        var result = await Round12RuleWitness.CheckAllAsync(source);
        string[] expected = [id];
        Assert.True(expected.SequenceEqual(result.Analyzer), $"Analyzer reported [{string.Join(",", result.Analyzer)}]");
        Assert.True(expected.SequenceEqual(result.Stage0), $"Stage-0 reported [{string.Join(",", result.Stage0)}]");
        Assert.True(expected.SequenceEqual(result.Self), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }

    [Theory]
    [MemberData(nameof(LinearPlaceBanNames))]
    public async Task LinearPlaceMatch_IsVib006OnEveryFrontend(string name)
    {
        var result = await Round12RuleWitness.CheckAllAsync(LinearPlaceBan[name]);
        Assert.Contains("VIB006", result.Analyzer);
        Assert.Equal(result.Analyzer, result.Stage0);
        Assert.True(result.Self.Contains("VIB006"), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }
}
