using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

// N131: direct-local `out` arguments with subsequent arguments keep MirPlace.Local with an analysis-only
// MirCallArg.Hold (`__out_hold`) instead of a runtime `*__ref` temp so LLVM 18 emission sets `%drop_x = true`
// post-call and avoids false VIB001 use-after-move on moved-from `!Copy` `out` targets.
// All positive rows run on Stage-0 with opt-18 verification and ASan/LSan at BOTH -O0 and -O3 without
// invoking `vib-self` (tracked separately) directly without external wrapper (CI-clean).
public sealed class Round12OutLeakTests
{
    private const string SharedHelpers = """
        sealed class Box(int v) { public int V = v; }
        sealed class VBox(Vec<int> items) { public Vec<int> Items = items; }
        struct BoxPair(Box a, Box b) { public Box A = a; public Box B = b; }
        struct Pair<T1, T2>(T1 first, T2 second) { public T1 First = first; public T2 Second = second; }
        static class H
        {
            public static void Consume(Box b) { }
            public static int ConsumeInt(Box b) => b.V;
            public static void ConsumeVec(Vec<int> v) { }
            public static void ConsumePair(BoxPair p) { }
            public static bool Id(bool b) => b;
            public static int IdInt(int x) => x;
            public static long F(out Box a, long v) { a = new Box(7); return v * 100; }
            public static long F2(out Box a, long v, long w) { a = new Box(7); return v * 100 + w; }
            public static long FVec(out Vec<int> a, long v) { a = [9, 8]; return v * 100; }
            public static long FInt(out int a, long v) { a = 7; return v * 100; }
            public static long Two(out Box a, out Box b) { a = new Box(3); b = new Box(4); return 700; }
            public static long Three(out Box a, out Box b, long k) { a = new Box(3); b = new Box(4); return k * 100; }
            public static long TwoCond(out Box a, out Box b, long k) { a = new Box(3); b = new Box(4); return k * 100; }
        }
        """;

    public static IEnumerable<object[]> NonCopyOutLeakFixedRows()
    {
        yield return ["p01-uninit-local-box", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r = H.F(out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p02-decl-var-box", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.F(out var x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p03-decl-typed-vec", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.FVec(out Vec<int> x, 2);
                    sum += r + x.Count;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4040\n"];

        yield return ["p04-uninit-local-vec-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Vec<int> x;
                    long r = H.FVec(out x, i % 2 == 0 ? 2 : 0);
                    sum += r + x.Count;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "2040\n"];

        yield return ["p05-decl-vec-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.FVec(out Vec<int> x, i % 2 == 0 ? 2 : 0);
                    sum += r + x.Count;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "2040\n"];

        yield return ["p05b-decl-box-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.F(out Box x, i % 2 == 0 ? 2 : 0);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "2140\n"];

        yield return ["e1-vbox-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    VBox x;
                    long r = MakeVBox(out x, i % 2 == 0 ? 2 : 0);
                    sum += r + x.Items.Count;
                }
                return sum;
            }
            static long MakeVBox(out VBox b, long v) { b = new VBox([1, 2, 3]); return v * 100; }
            """ + "\n" + SharedHelpers, "2060\n"];

        yield return ["dp01-nested-ternary-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r = H.F(out x, i % 3 == 0 ? 1 : (i % 2 == 0 ? 2 : 3));
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["dp02-short-circuit-and-or", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    bool b = TakeB(out x, (i > 2 && H.IdInt(i) < 8) || i == 0);
                    sum += (b ? 10 : 1) + x.V;
                }
                return sum;
            }
            static bool TakeB(out Box b, bool f) { b = new Box(7); return f; }
            """ + "\n" + SharedHelpers, "214\n"];

        yield return ["dp03-switch-expr-later-arg", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r = H.F(out x, (i % 3) switch { 0 => 10, 1 => 20, _ => 30 });
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "39140\n"];

        yield return ["x09-nested-out-in-cond-later-arg", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    Box y;
                    long r = H.F(out x, i % 2 == 0 ? H.F(out y, 1) : H.F(out y, 2));
                    sum += r + x.V + y.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "300280\n"];

        yield return ["p06-moved-from-box", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(1);
                    H.Consume(x);
                    long r = H.F(out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p06b-move-before-out-with-tail", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(1);
                    long r = FMove3(x, out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            static long FMove3(Box old, out Box dst, long k) { dst = new Box(old.V + 7); return k * 100; }
            """ + "\n" + SharedHelpers, "4160\n"];

        yield return ["p07-moved-from-vec", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Vec<int> x = [1, 2];
                    H.ConsumeVec(x);
                    long r = H.FVec(out x, 2);
                    sum += r + x.Count;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4040\n"];

        yield return ["p08-moved-from-box-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(1);
                    H.Consume(x);
                    long r = H.F(out x, i % 2 == 0 ? 2 : 0);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "2140\n"];

        yield return ["p09-byval-param-moved-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += Step(new Box(1));
                return sum;
            }
            static long Step(Box p)
            {
                H.Consume(p);
                long r = H.F(out p, 2);
                return r + p.V;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p18-struct-noncopy-moved", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var q = new BoxPair(new Box(1), new Box(2));
                    H.ConsumePair(q);
                    long r = MakePair(out q, 2);
                    sum += r + q.A.V + q.B.V;
                }
                return sum;
            }
            static long MakePair(out BoxPair p, long v) { p = new BoxPair(new Box(7), new Box(8)); return v * 100; }
            """ + "\n" + SharedHelpers, "4300\n"];

        yield return ["c06-ext-method-moved-recv-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(5);
                    long r = x.ExtMove(out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            static class Ext
            {
                public static long ExtMove(this Box self, out Box dst, long k) { dst = new Box(self.V + 10); return k * 100; }
            }
            """ + "\n" + SharedHelpers, "4300\n"];

        yield return ["p10-multi-out-two-only", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.Two(out Box a, out Box b);
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "14140\n"];

        yield return ["p11-multi-out-three", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.Three(out Box a, out Box b, 5);
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "10140\n"];

        yield return ["p12-multi-out-uninit-locals", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box a;
                    Box b;
                    long r = H.Three(out a, out b, 5);
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "10140\n"];

        yield return ["p13-multi-out-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.TwoCond(out Box a, out Box b, i % 2 == 0 ? 1 : 2);
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "3140\n"];

        yield return ["e2-two-out-around-branch", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box a;
                    Box b;
                    long r = H.TwoCond(out a, out b, i % 2 == 0 ? H.IdInt(1) : H.IdInt(2));
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "3140\n"];

        yield return ["e3-nested-out-calls", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box a;
                    Box b;
                    long r = H.F(out a, H.F(out b, 2));
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "400280\n"];

        yield return ["p14-stringbuf-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    StringBuf sb;
                    long r = MakeSb(out sb, 2);
                    sum += r + sb.Length;
                }
                return sum;
            }
            static long MakeSb(out StringBuf sb, long k) { sb = new StringBuf(); sb.Append("hello"); return k * 100; }
            """, "4100\n"];

        yield return ["p15-option-box-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Option<Box> o;
                    long r = MakeOpt(out o, i % 2 == 0 ? 2 : 0);
                    sum += r + (o is Some<Box>(var b) ? b.V : 0);
                }
                return sum;
            }
            static long MakeOpt(out Option<Box> o, long k) { o = Some(new Box(7)); return k * 100; }
            """ + "\n" + SharedHelpers, "2140\n"];

        yield return ["p16-result-box-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Result<Box, int> res;
                    long r = MakeRes(out res, i % 2 == 0 ? 2 : 0);
                    sum += r + (res is Ok<Box>(var b) ? b.V : 0);
                }
                return sum;
            }
            static long MakeRes(out Result<Box, int> res, long k) { res = Ok(new Box(7)); return k * 100; }
            """ + "\n" + SharedHelpers, "2140\n"];

        yield return ["p17-struct-noncopy-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Q q;
                    long r = MakeQ(out q, i % 2 == 0 ? 2 : 0);
                    sum += r + q.P.Count;
                }
                return sum;
            }
            static long MakeQ(out Q q, long k) { q = new Q([1, 2, 3]); return k * 100; }
            struct Q(Vec<int> p) { public Vec<int> P = p; }
            """, "2060\n"];

        yield return ["e4-generic-pair-box-vec-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Pair<Box, Vec<int>> p;
                    long r = MakePair(out p, 2);
                    sum += r + p.First.V + p.Second.Count;
                }
                return sum;
            }
            static long MakePair(out Pair<Box, Vec<int>> p, long k) { p = new Pair<Box, Vec<int>>(new Box(7), [1, 2]); return k * 100; }
            """ + "\n" + SharedHelpers, "4180\n"];

        yield return ["p19-discard-typed", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += H.F(out Box _, 2);
                return sum;
            }
            """ + "\n" + SharedHelpers, "4000\n"];

        yield return ["p20-discard-untyped", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += H.F(out _, 2);
                return sum;
            }
            """ + "\n" + SharedHelpers, "4000\n"];

        yield return ["p21-discard-loop", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += H.FVec(out _, 2);
                return sum;
            }
            """ + "\n" + SharedHelpers, "4000\n"];

        yield return ["p22-discard-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += H.F(out _, i % 2 == 0 ? 2 : 0);
                return sum;
            }
            """ + "\n" + SharedHelpers, "2000\n"];

        yield return ["neg20-discard-independent", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var z = new Box(5);
                    sum += H.F(out _, 2) + z.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4100\n"];

        yield return ["e01-multi-out-discard-straight", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += H.TwoCond(out Box _, out _, 2);
                return sum;
            }
            """ + "\n" + SharedHelpers, "4000\n"];

        yield return ["e02-multi-out-discard-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += H.TwoCond(out Box _, out _, i % 2 == 0 ? 3 : 4);
                return sum;
            }
            """ + "\n" + SharedHelpers, "7000\n"];

        yield return ["e14-field-init-out-discard", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += new Host().N;
                return sum;
            }
            sealed class Host { public int N = (int)H.F(out _, 2); }
            """ + "\n" + SharedHelpers, "4000\n"];

        yield return ["h13-field-init-out-var-used", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += new Host().N;
                return sum;
            }
            sealed class Host { public int N = (int)H.F(out var x, 2) + x.V; }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p23-ctor-out-arg", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var h = new Host(out Box x, 2);
                    sum += h.R + x.V;
                }
                return sum;
            }
            sealed class Host
            {
                public long R;
                public Host(out Box a, long v) { a = new Box(7); R = v * 100; }
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p24-instance-method-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new C(5);
                    long r = x.M(out C y, 2);
                    sum += r + x.V + y.V;
                }
                return sum;
            }
            sealed class C(int v)
            {
                public int V = v;
                public long M(out C b, int k) { b = new C(V + 10); return k * 100; }
            }
            """, "4400\n"];

        yield return ["p25-ext-method-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(5);
                    long r = x.Ext(out Box y, 2);
                    sum += r + y.V;
                }
                return sum;
            }
            static class BoxExtensions
            {
                public static long Ext(this Box x, out Box y, int k) { y = new Box(x.V + 10); return k * 100; }
            }
            """ + "\n" + SharedHelpers, "4300\n"];

        yield return ["p26-generic-method-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box a;
                    Vec<int> v;
                    sum += Make<Box>(out a, new Box(7), 2) + a.V;
                    sum += Make<Vec<int>>(out v, [1, 2], 3) + v.Count;
                }
                return sum;
            }
            static long Make<T>(out T dst, T src, int k) { dst = src; return k * 100; }
            """ + "\n" + SharedHelpers, "10180\n"];

        yield return ["p27b-named-args-in-order", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.F(a: out Box x, v: 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p28-while-continue-break-out-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                int i = 0;
                while (i < 20)
                {
                    i++;
                    Box x;
                    long r = H.F(out x, i % 2 == 0 ? 1 : 2);
                    sum += r + x.V;
                    if (i < 10) continue;
                    if (i == 15) break;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "2405\n"];

        yield return ["p29-loop-outer-reassigned", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                Box x;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.F(out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p30-if-else-branch-init", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r;
                    if (i % 2 == 0) r = H.F(out x, 2);
                    else r = H.F(out x, 3);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "5140\n"];

        yield return ["p31-if-partial-no-use", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    if (i % 2 == 0) sum += H.F(out x, 2);
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "2000\n"];

        yield return ["p32-early-return", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++) sum += Step(i % 2 == 0);
                return sum;
            }
            static long Step(bool c)
            {
                long r = H.F(out Box x, c ? 1 : 2);
                if (c) return r + x.V;
                return r + 5;
            }
            """ + "\n" + SharedHelpers, "3120\n"];

        yield return ["p33-ctor-field-out-overwrite", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var h = new Holder(i % 2 == 0);
                    sum += h.R + h.B.V;
                }
                return sum;
            }
            sealed class Holder
            {
                public Box B;
                public long R;
                public Holder(bool c)
                {
                    R = H.F(out B, c ? 1 : 2);
                    B = new Box(9);
                }
            }
            """ + "\n" + SharedHelpers, "3180\n"];

        yield return ["p34-out-param-forward", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r = G(out x);
                    sum += r + x.V;
                }
                return sum;
            }
            static long G(out Box p) => H.F(out p, 2);
            """ + "\n" + SharedHelpers, "4140\n"];

        yield return ["p35-out-param-forward-use-after", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r = G(out x);
                    sum += r + x.V;
                }
                return sum;
            }
            static long G(out Box p) { long r = H.F(out p, 2); return r + p.V; }
            """ + "\n" + SharedHelpers, "4280\n"];

        yield return ["p36-out-param-forward-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    long r = G(out x, i % 2 == 0);
                    sum += r + x.V;
                }
                return sum;
            }
            static long G(out Box p, bool c) => H.F(out p, c ? 2 : 3);
            """ + "\n" + SharedHelpers, "5140\n"];

        yield return ["p43-ref-return-out-with-later-arg", """
            using System.Diagnostics.CodeAnalysis;
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    ref Box r = ref RefOut(out x, 2);
                    sum += r.V + x.V;
                }
                return sum;
            }
            static ref Box RefOut([UnscopedRef] out Box x, long v)
            {
                x = new Box((int)v + 7);
                return ref x;
            }
            """ + "\n" + SharedHelpers, "360\n"];

        yield return ["cf09-n90-wrapper-recv-box-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var s = Some(new B(5));
                    long r = s.Value.M(out s, 2);
                    sum += r + s.Value.X;
                }
                return sum;
            }
            sealed class B(int x)
            {
                public int X = x;
                public long M(out Some<B> o, int k) { o = Some(new B(9)); return X + k; }
            }
            """, "320\n"];

        yield return ["e7-discarded-noncopy-ret-with-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    _ = MakeBoxRet(out x, 2);
                    sum += x.V;
                }
                return sum;
            }
            static Box MakeBoxRet(out Box a, long k) { a = new Box(7); return new Box((int)k + 10); }
            """ + "\n" + SharedHelpers, "140\n"];

        yield return ["dp05-outvar-branch-later", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    long r = H.F(out var x, i % 2 == 0 ? 1 : 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "3140\n"];

        yield return ["dp08-option-switch-expr-later", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box x;
                    Option<int> o = i % 2 == 0 ? Some(5) : None;
                    long r = H.F(out x, o switch { Some<int>(var v) => v, _ => 0 });
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "5140\n"];

        yield return ["d07-mixed-out-and-outvar-before-cond", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    Box a;
                    long r = H.TwoCond(out a, out Box b, i % 2 == 0 ? 1 : 2);
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "3140\n"];

        yield return ["b14-moved-then-multi-out", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var a = new Box(1);
                    var b = new Box(2);
                    H.Consume(a);
                    H.Consume(b);
                    long r = H.Two(out a, out b);
                    sum += r + a.V + b.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "14140\n"];

        yield return ["cf08-n90-wrap-copy-out-other-box", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var s = Some(new P(5));
                    long r = s.Value.ReadMBox(out Box b, 2);
                    sum += r + s.Value.X + b.V;
                }
                return sum;
            }
            readonly struct P(int x)
            {
                public readonly int X = x;
                public long ReadMBox(out Box b, int k) { b = new Box(9); return X + k * 100; }
            }
            """ + "\n" + SharedHelpers, "4380\n"];

        yield return ["h05-n90-in-arg-out-other-box", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var s = Some(new P(5));
                    long r = FInOutBox(in s.Value, out Box b, 2);
                    sum += r + s.Value.X + b.V;
                }
                return sum;
            }
            static long FInOutBox(in P p, out Box b, int k) { b = new Box(9); return p.X + k * 100; }
            readonly struct P(int x) { public readonly int X = x; }
            """ + "\n" + SharedHelpers, "4380\n"];
    }

    [Theory]
    [MemberData(nameof(NonCopyOutLeakFixedRows))]
    public Task NonCopyOutWithLaterArgs_Stage0AsanLsanCleanAtO0AndO3(string shape, string source, string expectedOut) =>
        AssertStage0CleanAsync(shape, source, expectedOut);

    public static IEnumerable<object[]> BoundaryAndControlPositiveRows()
    {
        yield return ["nl01-init-struct-field-out", """
            var s = new BoxPair(new Box(1), new Box(2));
            long r = H.F(out s.A, 2);
            WriteLine(r + s.A.V + s.B.V);
            """ + "\n" + SharedHelpers, "209\n", 0, ""];

        yield return ["nl02-init-nested-struct-field-out", """
            var o = new Outer(new BoxPair(new Box(1), new Box(2)));
            long r = H.F(out o.Inner.A, 2);
            WriteLine(r + o.Inner.A.V + o.Inner.B.V);
            struct Outer(BoxPair inner) { public BoxPair Inner = inner; }
            """ + "\n" + SharedHelpers, "209\n", 0, ""];

        yield return ["nl03-class-field-out", """
            var h = new Holder(new Box(1));
            long r = H.F(out h.B, 2);
            WriteLine(r + h.B.V);
            sealed class Holder(Box b) { public Box B = b; }
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["nl07-uninit-copy-struct-field-out", """
            CPair p;
            long r = H.FInt(out p.A, 2);
            p.B = 5;
            WriteLine(r + p.A + p.B);
            struct CPair { public int A; public int B; }
            """ + "\n" + SharedHelpers, "212\n", 0, ""];

        yield return ["nl08-ref-local-out", """
            Box a = new Box(1);
            ref Box r = ref a;
            long k = H.F(out r, 2);
            WriteLine(k + a.V);
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["nl09-vec-elem-out", """
            Vec<int> v = [1];
            long k = H.FInt(out v[0], 2);
            WriteLine(k + v[0]);
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["nl10-span-elem-out", """
            Vec<int> v = [1];
            Span<int> s = v.AsSpan();
            long k = H.FInt(out s[0], 2);
            WriteLine(k + v[0]);
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["nl11-ref-ret-call-out", """
            var h = new Holder(new Box(1));
            long k = H.F(out h.RefB(), 2);
            WriteLine(k + h.B.V);
            sealed class Holder(Box b)
            {
                public Box B = b;
                [Mut] public ref Box RefB() => ref B;
            }
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["nl12-side-effect-index-order", """
            Vec<int> v = [10, 20];
            int i = 0;
            long r = H.FInt(out v[Inc(ref i)], i);
            WriteLine(r + v[0] + v[1]);
            static int Inc(ref int x) { x += 1; return 0; }
            """ + "\n" + SharedHelpers, "127\n", 0, ""];

        yield return ["nl13-index-var-mutated-later", """
            Vec<int> v = [10, 20];
            int i = 0;
            long r = H.FInt(out v[i], Inc(ref i));
            WriteLine(r + v[0] + v[1] + i);
            static int Inc(ref int x) { x += 1; return 2; }
            """ + "\n" + SharedHelpers, "228\n", 0, ""];

        yield return ["nl17-ref-local-struct-field-rebind", """
            var s1 = new S(10, 20);
            var s2 = new S(30, 40);
            ref S r = ref s1;
            long k = F(out r.A, (r = ref s2).B);
            WriteLine(k + s1.A + s1.B + s2.A + s2.B);
            static long F(out int a, int b) { a = 700; return b * 10000; }
            struct S(int a, int b) { public int A = a; public int B = b; }
            """, "400790\n", 0, ""];

        yield return ["r03-ref-local-noncopy-struct-field-rebind", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var p1 = new BoxPair(new Box(1), new Box(2));
                    var p2 = new BoxPair(new Box(3), new Box(4));
                    ref BoxPair r = ref p1;
                    long k = H.F(out r.A, (r = ref p2).B.V);
                    sum += k + p1.A.V + p1.B.V + p2.A.V + p2.B.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "8320\n", 0, ""];

        yield return ["p37-ref-param-as-out", """
            var x = new Box(1);
            long r = G(ref x);
            WriteLine(r + x.V);
            static long G(ref Box p) => H.F(out p, 2);
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["p38-ref-param-as-out-use-after", """
            var x = new Box(1);
            long r = G(ref x);
            WriteLine(r + x.V);
            static long G(ref Box p) { long r = H.F(out p, 2); return r + p.V; }
            """ + "\n" + SharedHelpers, "214\n", 0, ""];

        yield return ["p39-init-local-overwrite", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(1);
                    long r = H.F(out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            """ + "\n" + SharedHelpers, "4140\n", 0, ""];

        yield return ["p40-init-local-overwrite-twice-in-callee", """
            WriteLine(Run());
            static long Run()
            {
                long sum = 0;
                for (int i = 0; i < 20; i++)
                {
                    var x = new Box(1);
                    long r = Twice(out x, 2);
                    sum += r + x.V;
                }
                return sum;
            }
            static long Twice(out Box a, long v) { a = new Box(5); a = new Box(7); return v * 100; }
            """ + "\n" + SharedHelpers, "4140\n", 0, ""];

        yield return ["sib01-ref-noncopy-local", """
            var x = new Box(1);
            long r = RefBox(ref x, 2);
            WriteLine(r + x.V);
            static long RefBox(ref Box a, long v) { a = new Box(a.V + 6); return v * 100; }
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["sib02-ref-noncopy-cond", """
            var x = new Box(1);
            long r = RefBox(ref x, H.Id(true) ? 2 : 3);
            WriteLine(r + x.V);
            static long RefBox(ref Box a, long v) { a = new Box(a.V + 6); return v * 100; }
            """ + "\n" + SharedHelpers, "207\n", 0, ""];

        yield return ["sib03-in-noncopy-local", """
            var x = new Box(5);
            long r = InBox(in x, 2);
            WriteLine(r + x.V);
            static long InBox(in Box a, long v) => a.V + v * 100;
            """ + "\n" + SharedHelpers, "210\n", 0, ""];

        yield return ["sib04-in-noncopy-cond", """
            var x = new Box(5);
            long r = InBox(in x, H.Id(true) ? 2 : 3);
            WriteLine(r + x.V);
            static long InBox(in Box a, long v) => a.V + v * 100;
            """ + "\n" + SharedHelpers, "210\n", 0, ""];

        yield return ["sib05-ref-vec-replace", """
            Vec<int> v = [1, 2];
            long r = ReplaceVec(ref v, H.Id(true) ? 2 : 3);
            WriteLine(r + v.Count);
            static long ReplaceVec(ref Vec<int> v, long k) { v = [3, 4, 5]; return k * 100; }
            """ + "\n" + SharedHelpers, "203\n", 0, ""];

        yield return ["sib06-ref-two-phase-read", """
            int y = 41;
            WriteLine(R(ref y, y));
            static int R(ref int a, int b) { a = 2; return a + b; }
            """, "43\n", 0, ""];

        yield return ["pan01-uninit-panic-later", """
            Box x;
            long r = H.F(out x, Boom());
            WriteLine(r + x.V);
            static long Boom() { if (H.Id(true)) VibraniumRuntime.Panic("boom"); return 0; }
            """ + "\n" + SharedHelpers, "", 101, "panic: boom\n"];

        yield return ["pan02-init-panic-later", """
            var x = new Box(1);
            long r = H.F(out x, Boom());
            WriteLine(r + x.V);
            static long Boom() { if (H.Id(true)) VibraniumRuntime.Panic("boom"); return 0; }
            """ + "\n" + SharedHelpers, "", 101, "panic: boom\n"];

        yield return ["pan03-moved-panic-later", """
            var x = new Box(1);
            H.Consume(x);
            long r = H.F(out x, Boom());
            WriteLine(r + x.V);
            static long Boom() { if (H.Id(true)) VibraniumRuntime.Panic("boom"); return 0; }
            """ + "\n" + SharedHelpers, "", 101, "panic: boom\n"];

        yield return ["nl16-oob-index-before-later-panic", """
            Vec<int> v = [1];
            long r = H.FInt(out v[99], Boom());
            WriteLine(r);
            static long Boom() { if (H.Id(true)) VibraniumRuntime.Panic("boom"); return 0; }
            """ + "\n" + SharedHelpers, "", 101, "panic: index out of bounds\n"];

        yield return ["p06c-move-before-out-two-args", """
            var x = new Box(1);
            long r = FMove2(x, out x);
            WriteLine(r + x.V);
            static long FMove2(Box old, out Box dst) { dst = new Box(old.V + 7); return 100; }
            """ + "\n" + SharedHelpers, "108\n", 0, ""];

        yield return ["p42-single-out-control", """
            Box x;
            H.F(out x, 2);
            WriteLine(x.V);
            """ + "\n" + SharedHelpers, "7\n", 0, ""];

        yield return ["neg21-read-before-out", """
            int y = 5;
            WriteLine(ReadOut(y, out y));
            static long ReadOut(int a, out int b) { b = 3; return a * 10 + b; }
            """, "53\n", 0, ""];

        yield return ["neg22-read-before-out-with-tail", """
            int y = 5;
            WriteLine(ReadOutTail(y, out y, 2));
            static long ReadOutTail(int a, out int b, int k) { b = 3; return a * 10 + b + k; }
            """, "55\n", 0, ""];

        yield return ["x08-disjoint-struct-fields-out-later", """
            var p = new BoxPair(new Box(1), new Box(2));
            long r = PairF(out p.A, out p.B, 2);
            WriteLine(r + p.A.V + p.B.V);
            static long PairF(out Box a, out Box b, long k) { a = new Box(5); b = new Box(6); return k * 100; }
            """ + "\n" + SharedHelpers, "211\n", 0, ""];
    }

    [Theory]
    [MemberData(nameof(BoundaryAndControlPositiveRows))]
    public Task BoundaryAndControlRows_Stage0AsanLsanCleanAtO0AndO3(
        string shape, string source, string expectedOut, int expectedExit, string expectedErr) =>
        AssertStage0CleanAsync(shape, source, expectedOut, expectedExit, expectedErr);

    public static IEnumerable<object[]> RejectRows()
    {
        yield return ["neg01-same-type-read", """
            int y = 1;
            WriteLine(H.FInt(out y, y));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg02-noncopy-init-move-later", """
            var y = new Box(1);
            WriteLine(FMove(out y, y));
            static long FMove(out Box a, Box b) { a = new Box(7); return b.V; }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg03-noncopy-init-read-field", """
            var y = new Box(1);
            WriteLine(H.F(out y, y.V));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg04-noncopy-init-in-later", """
            var y = new Box(1);
            WriteLine(H.F(out y, ReadIn(in y)));
            static long ReadIn(in Box b) => b.V;
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg05-noncopy-init-ref-later", """
            var y = new Box(1);
            WriteLine(H.F(out y, ReadRef(ref y)));
            static long ReadRef(ref Box b) => b.V;
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg06-noncopy-init-nested-out-later", """
            var y = new Box(1);
            WriteLine(H.F(out y, H.F(out y, 1)));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg07-out-same-call-out", """
            Box y;
            WriteLine(H.Two(out y, out y) + y.V);
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["neg08-outvar-same-call-out", """
            WriteLine(H.Two(out Box y, out y) + y.V);
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["neg09-out-same-call-ref", """
            var y = new Box(1);
            WriteLine(OutRef(out y, ref y) + y.V);
            static long OutRef(out Box a, ref Box b) { a = new Box(7); return b.V; }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["neg10-out-same-call-in", """
            var y = new Box(1);
            WriteLine(OutIn(out y, in y) + y.V);
            static long OutIn(out Box a, in Box b) { a = new Box(7); return b.V; }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["neg11-outvar-out-tail-cond", """
            WriteLine(H.Three(out Box y, out y, H.Id(true) ? 1 : 2) + y.V);
            """ + "\n" + SharedHelpers, "VIB002,VIB003"];

        yield return ["neg12-out-param-later-read", """
            Box x;
            WriteLine(G(out x) + x.V);
            static long G(out Box p) { p = new Box(1); return H.F(out p, p.V); }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg13-out-param-later-nested-out", """
            Box x;
            WriteLine(G(out x) + x.V);
            static long G(out Box p) => H.F(out p, H.F(out p, 1));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg14-ref-param-live-alias-across-out", """
            var x = new Box(1);
            WriteLine(G(ref x) + x.V);
            static long G(ref Box p) { ref Box a = ref p; long r = H.F(out p, 2); return r + a.V; }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg16-cond-3rd-arg-read", """
            int y = 1;
            WriteLine(F3(out y, H.Id(true) ? 1 : 2, y));
            static long F3(out int a, long k, int b) { a = 7; return k + b; }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg18-byval-param-out-then-read", """
            WriteLine(G(new Box(1)));
            static long G(Box p) => H.F(out p, p.V);
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg19-ctor-field-out-then-read", """
            WriteLine(new Holder().R);
            sealed class Holder
            {
                public Box B;
                public long R;
                public Holder() { B = new Box(1); R = H.F(out B, B.V); }
            }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg23-cond-3rd-arg-noncopy-read", """
            var y = new Box(1);
            WriteLine(H.F2(out y, H.Id(true) ? 1 : 2, y.V));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg24-moved-out-cond-then-read-3rd", """
            var y = new Box(1);
            WriteLine(H.F2(out y, H.Id(true) ? H.ConsumeInt(y) : 2, 3));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg25-out-then-consume-call", """
            var x = new Box(1);
            WriteLine(H.F(out x, H.ConsumeInt(x)));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg26-out-then-nested-ref-in-middle", """
            var y = new Box(1);
            WriteLine(H.F2(out y, ReadRef(ref y), 2));
            static long ReadRef(ref Box r) => r.V;
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg27-three-out-same-local-straight", """
            Box y;
            WriteLine(H.Three(out y, out y, 2) + y.V);
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["neg28-vec-out-while-span-live", """
            Vec<int> v = [1, 2];
            var s = v.AsSpan();
            long r = H.FVec(out v, 2);
            WriteLine(r + s.Length);
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["neg30-vec-out-with-span-arg", """
            Vec<int> v = [1, 2];
            var s = v.AsSpan();
            WriteLine(TakeVS(out v, s));
            static long TakeVS(out Vec<int> v, Span<int> s) { v = [3]; return v.Count + s.Length; }
            """, "VIB002,VIB003"];

        yield return ["neg31-span-arg-then-vec-out-with-tail", """
            Vec<int> v = [1, 2];
            var s = v.AsSpan();
            WriteLine(TakeSV(s, out v, 2));
            static long TakeSV(Span<int> s, out Vec<int> v, int k) { v = [3]; return s.Length + v.Count + k; }
            """, "VIB002"];

        yield return ["neg32-ref-return-out-conflict-after", """
            using System.Diagnostics.CodeAnalysis;
            var x = new Box(1);
            ref Box r = ref RefOut(out x, 2);
            long k = H.F(out x, 3);
            WriteLine(k + r.V);
            static ref Box RefOut([UnscopedRef] out Box x, long v)
            {
                x = new Box((int)v + 7);
                return ref x;
            }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["nl04-uninit-struct-noncopy-field-out", """
            BoxPair p;
            long r = H.F(out p.A, 2);
            p.B = new Box(3);
            WriteLine(r + p.A.V + p.B.V);
            """ + "\n" + SharedHelpers, "VIB000"];

        yield return ["nl05-uninit-struct-noncopy-field-out-cond", """
            BoxPair p;
            long r = H.F(out p.A, H.Id(true) ? 2 : 3);
            p.B = new Box(3);
            WriteLine(r + p.A.V + p.B.V);
            """ + "\n" + SharedHelpers, "VIB000"];

        yield return ["nl06-moved-struct-noncopy-field-out", """
            var p = new BoxPair(new Box(1), new Box(2));
            H.ConsumePair(p);
            long r = H.F(out p.A, 2);
            WriteLine(r + p.A.V);
            """ + "\n" + SharedHelpers, "VIB001"];

        yield return ["nl14-ref-local-rebound-later", """
            Box b1 = new Box(1);
            Box b2 = new Box(2);
            ref Box r = ref b1;
            long k = H.F(out r, (r = ref b2).V);
            WriteLine(k + b1.V + b2.V);
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["nl15-ref-local-rebind-expr-later", """
            Box b1 = new Box(1);
            Box b2 = new Box(2);
            ref Box r = ref b1;
            long k = H.F(out r, 1 + (r = ref b2).V);
            WriteLine(k + b1.V + b2.V);
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["lin01-linear-out-param", """
            Res r;
            WriteLine(G(out r));
            using (r) { }
            static long G(out Res p) { p = new Res(1); return 1; }
            sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() { } }
            """, "VIB000,VIB006"];

        yield return ["c01-class-recv-out-self", """
            var x = new C(5);
            WriteLine(x.M(out x, 2) + x.V);
            sealed class C(int v)
            {
                public int V = v;
                public long M(out C b, int k) { b = new C(V + 10); return k * 100; }
            }
            """, "VIB002"];

        yield return ["c04-mut-struct-recv-out-self", """
            var s = new MutBox(new Box(5));
            WriteLine(s.MutM(out s, 2) + s.B.V);
            struct MutBox(Box b)
            {
                public Box B = b;
                [Mut] public long MutM(out MutBox o, int k) { o = new MutBox(new Box(9)); return k; }
            }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["c05-ro-struct-recv-out-self", """
            var s = new RS(new Box(5));
            WriteLine(s.ReadM(out s, 2) + s.B.V);
            readonly struct RS(Box b)
            {
                public readonly Box B = b;
                public long ReadM(out RS o, int k) { o = new RS(new Box(9)); return k; }
            }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["c08-n90-wrap-copy-out-self", """
            var s = Some(new P(5));
            WriteLine(s.Value.ReadM(out s, 2) + s.Value.X);
            readonly struct P(int x)
            {
                public readonly int X = x;
                public long ReadM(out Some<P> o, int k) { o = Some(new P(9)); return X + k; }
            }
            """, "VIB002"];

        yield return ["c08b-n90-in-copy-out-self", """
            var s = Some(new P(5));
            WriteLine(FInOut(in s.Value, out s, 2) + s.Value.X);
            static long FInOut(in P p, out Some<P> o, int k) { o = Some(new P(9)); return p.X + k; }
            readonly struct P(int x) { public readonly int X = x; }
            """, "VIB002"];

        yield return ["c12-n90-noncopy-wrap-out-self", """
            var s = Some(new NP(new Box(5)));
            WriteLine(s.Value.M(out s, 2));
            readonly struct NP(Box b)
            {
                public readonly Box B = b;
                public long M(out Some<NP> o, int k) { o = Some(new NP(new Box(9))); return B.V + k; }
            }
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["c10-n90-wrap-last-arg-move", """
            var h = new WrapHolder(Some(new P(5)));
            WriteLine(h.W.Value.Take(h));
            sealed class WrapHolder(Some<P> w) { public Some<P> W = w; }
            readonly struct P(int x)
            {
                public readonly int X = x;
                public long Take(WrapHolder h) => X + h.W.Value.X;
            }
            """, "VIB003"];

        yield return ["h04-n90-in-arg-last-move", """
            var h = new WrapHolder(Some(new P(5)));
            WriteLine(TakeIn(in h.W.Value, h));
            static long TakeIn(in P p, WrapHolder h) => p.X + h.W.Value.X;
            sealed class WrapHolder(Some<P> w) { public Some<P> W = w; }
            readonly struct P(int x) { public readonly int X = x; }
            """, "VIB003"];

        yield return ["p03-optmut-pat-out-multi", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            if (m is Some<Box>(var b)) WriteLine(H.F(out b, 2) + b.V);
            """ + "\n" + SharedHelpers, "VIB004"];

        yield return ["p03c-optmut-pat-out-cond", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            if (m is Some<Box>(var b)) WriteLine(H.F(out b, H.Id(true) ? 2 : 0) + b.V);
            """ + "\n" + SharedHelpers, "VIB004"];

        yield return ["x10-nested-out-same-local-in-cond", """
            Box x;
            long r = H.F(out x, H.Id(true) ? H.F(out x, 1) : 2);
            WriteLine(r + x.V);
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["o05-partial-moved-struct-out", """
            var p = new BoxPair(new Box(1), new Box(2));
            H.Consume(p.A);
            long r = MakePair(out p, 2);
            WriteLine(r + p.A.V + p.B.V);
            static long MakePair(out BoxPair p, long v) { p = new BoxPair(new Box(7), new Box(8)); return v * 100; }
            """ + "\n" + SharedHelpers, "VIB005"];

        yield return ["p27-named-args-reordered", """
            Box x;
            WriteLine(H.F(v: 2, a: out x) + x.V);
            """ + "\n" + SharedHelpers, "VIB000"];

        yield return ["p41-struct-this-out", """
            var s = new MutOut();
            s.Run();
            WriteLine(s.V);
            struct MutOut
            {
                public int V;
                [Mut] public void Run() { Set(out this, 2); }
                static void Set(out MutOut m, int k) { m = new MutOut(); m.V = k; }
            }
            """, "VIB000"];

        yield return ["c07-ext-in-recv-out-self", """
            var x = new Pt(5);
            WriteLine(x.ExtIn(out x, 2) + x.V);
            struct Pt(int v) { public int V = v; }
            static class PtExtensions
            {
                public static long ExtIn(this in Pt x, out Pt y, int k) { y = new Pt(x.V + 10); return k * 100; }
            }
            """, "VIB002"];

        yield return ["e06-overlap-out-ref-3arg", """
            var y = new Box(1);
            WriteLine(OutRef3(out y, ref y, 2) + y.V);
            static long OutRef3(out Box a, ref Box b, int k) { a = new Box(7); return b.V + k; }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["e06b-overlap-ref-out-3arg", """
            var y = new Box(1);
            WriteLine(RefOut3(ref y, out y, 2) + y.V);
            static long RefOut3(ref Box a, out Box b, int k) { b = new Box(7); return a.V + k; }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["e08b-overlap-out-vec-as-span-3arg", """
            Vec<int> v = [1, 2];
            WriteLine(TakeVS3(out v, v.AsSpan(), 2));
            static long TakeVS3(out Vec<int> v, Span<int> s, int k) { v = [3]; return v.Count + s.Length + k; }
            """, "VIB002"];

        yield return ["e09b-overlap-out-vec-as-rospan-3arg", """
            Vec<int> v = [1, 2];
            WriteLine(TakeVRo3(out v, v.AsReadOnlySpan(), 2));
            static long TakeVRo3(out Vec<int> v, ReadOnlySpan<int> s, int k) { v = [3]; return v.Count + s.Length + k; }
            """, "VIB002"];

        yield return ["d04-switch-expr-reads-x", """
            var x = new Box(1);
            WriteLine(H.F(out x, (x.V % 2) switch { 0 => 10, _ => 20 }) + x.V);
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["d09-out-cond-then-3rd-out-same", """
            Box x;
            WriteLine(F3(out x, H.Id(true) ? 1 : 2, out x) + x.V);
            static long F3(out Box a, long k, out Box b) { a = new Box(3); b = new Box(4); return k * 100; }
            """ + "\n" + SharedHelpers, "VIB002"];

        yield return ["d11b-out-cond-with-3rd-arg-reads-x", """
            var x = new Box(1);
            WriteLine(H.F2(out x, H.Id(true) ? 1 : 2, x.V));
            """ + "\n" + SharedHelpers, "VIB003"];

        yield return ["cf15-primary-ctor-param-out", """
            WriteLine(new Prim(5).Run());
            sealed class Prim(int v)
            {
                public int Run() => (int)H.FInt(out v, 2) + v;
            }
            """ + "\n" + SharedHelpers, "VIB004"];
    }

    [Theory]
    [MemberData(nameof(RejectRows))]
    public Task ConflictingAndInvalidOutRows_Stage0AndAnalyzerMatch(string shape, string source, string expectedIds) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, expectedIds);

    private static async Task AssertStage0CleanAsync(
        string shape,
        string source,
        string expectedOut,
        int expectedExit = 0,
        string expectedErr = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-outleak-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string src = Path.Combine(Round11AuditAssertions.RepoRoot, "src");
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12OutLeak", includeStd: true, sysroot: src);
            var csharp = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.True(csharp.Length == 0, $"{shape}: unexpected C# diagnostics:\n" + string.Join("\n", csharp.Select(d => d.ToString())));

            var stage0Diags = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            Assert.True(stage0Diags.Count == 0, $"{shape}: Stage-0 rejected:\n" + string.Join("\n", stage0Diags.Select(d => d.ToString())));

            var analyzerDiags = await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
            Assert.True(analyzerDiags.Length == 0, $"{shape}: Analyzer rejected:\n" + string.Join("\n", analyzerDiags.Select(d => d.ToString())));

            string managedDll = Path.Combine(temp, "Round12OutLeakManaged.dll");
            string bootstrap = Path.Combine(src, "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap),
                new CSharpParseOptions(LanguageVersion.CSharp15),
                path: bootstrap));
            var emit = managedCompilation.Emit(managedDll);
            Assert.True(emit.Success, $"{shape}: managed emit failed:\n" + string.Join("\n", emit.Diagnostics));

            File.Copy(typeof(Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round12OutLeakManaged.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);

            var managed = RunProcess(Round11AuditAssertions.DotnetExe, withAsan: false, managedDll);
            Assert.True(managed == (expectedExit, expectedOut, expectedErr),
                $"{shape}: managed output mismatch\nexpected: exit={expectedExit} out=[{expectedOut}] err=[{expectedErr}]\nactual:   exit={managed.Exit} out=[{managed.Out}] err=[{managed.Err}]");

            var stage0Ir = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(stage0Ir.Success && stage0Ir.LlvmIr is not null, $"{shape}: Stage-0 emit-ll failed:\n" + string.Join("\n", stage0Ir.Errors));

            string irPath = Path.Combine(temp, "stage0.ll");
            File.WriteAllText(irPath, stage0Ir.LlvmIr, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var verify = RunProcess("opt-18", withAsan: false, "-passes=verify", "-disable-output", irPath);
            Assert.True(verify.Exit == 0, $"{shape}: opt-18 verify failed:\n{verify.Err}");

            foreach (string optimization in new[] { "-O0", "-O3" })
            {
                string native = Path.Combine(temp, $"native{optimization}");
                var build = RunProcess("clang-18", withAsan: false, optimization, irPath, "-o", native, "-fsanitize=address,leak");
                Assert.True(build.Exit == 0, $"{shape}: clang-18 {optimization} failed:\n{build.Err}");

                var actual = RunProcess(native, withAsan: true);
                Assert.True(actual == managed,
                    $"{shape}: native {optimization} ASan/LSan differs from managed:\nmanaged: exit={managed.Exit} out=[{managed.Out}] err=[{managed.Err}]\nnative:  exit={actual.Exit} out=[{actual.Out}] err=[{actual.Err}]");
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static (int Exit, string Out, string Err) RunProcess(string command, bool withAsan, params string[] args)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (withAsan)
        {
            start.Environment["ASAN_OPTIONS"] = "detect_leaks=1:halt_on_error=1";
        }
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"{command} exceeded 120 seconds.");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
