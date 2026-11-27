using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// N90: a Core wrapper's payload (Some.Value, Ok.Value, Err.Error) — or a value-type field chain inside it — read in place
// as a struct receiver (including readonly property/indexer setters, ref-returning and view-returning members, and Option.AsRef)
// or `in` argument stays borrowed until the call (and through any returned ref/view), so a later argument that writes the
// wrapper is VIB003 (F14). N91: a guard that rebinds a by-value view parameter that is the switch scrutinee is VIB003 (F13).
// N116: an `out` argument borrows its target exclusively until the call returns, so a later argument using it is VIB003.
public sealed class Round12RbWrapTests
{
    public static IEnumerable<object[]> RejectRows()
    {
        yield return ["p15-vec-readonlyslice-noncopy", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(0, Reset(out s))[0]);
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 1; }
            """];
        yield return ["p16-vec-readonlyslice-branch", """
            bool c = true;
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(0, c ? Reset(out s) : 1)[0]);
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 1; }
            """];
        yield return ["p17-vec-readonlyslice-ok-subfield", """
            Vec<int> v = [10, 20, 30];
            var k = Ok(new VecWrap(Move(v)));
            WriteLine(k.Value.V.ReadOnlySlice(0, Reset(out k))[0]);
            static int Reset(out Ok<VecWrap> k) { Vec<int> w = [99, 88, 77]; k = Ok(new VecWrap(Move(w))); return 1; }
            public struct VecWrap(Vec<int> v) { public Vec<int> V = Move(v); }
            """];
        yield return ["p65-vec-readonlyslice-start-arg", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(Reset(out s), 1)[0]);
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 0; }
            """];
        yield return ["p66-vec-readonlyslice-branch-start", """
            bool c = true;
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(c ? Reset(out s) : 0, 1)[0]);
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 0; }
            """];
        yield return ["p67-vec-readonlyslice-err", """
            Vec<int> v = [10, 20, 30];
            var e = Err(Move(v));
            WriteLine(e.Error.ReadOnlySlice(0, Reset(out e))[0]);
            static int Reset(out Err<Vec<int>> e) { Vec<int> w = [99, 88, 77]; e = Err(Move(w)); return 1; }
            """];
        yield return ["p69-vec-readonlyslice-panic-divergence", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(0, ClearToEmpty(out s))[0]);
            static int ClearToEmpty(out Some<Vec<int>> s) { Vec<int> w = new Vec<int>(); s = Some(Move(w)); return 1; }
            """];
        yield return ["p70-vec-readonlyslice-plain-local-ctrl", """
            Vec<int> v = [10, 20, 30];
            WriteLine(v.ReadOnlySlice(0, Reset(out v))[0]);
            static int Reset(out Vec<int> v) { v = [99, 88, 77]; return 1; }
            """];
        yield return ["r3-p21-double-branch-getter", """
            var s = Some(new Node(4));
            bool f = Yes();
            WriteLine(s.Value[(f ? Reset(out s) : 2)]);
            static bool Yes() => true;
            static int Reset(out Some<Node> v) { v = Some(new Node(16)); return 3; }
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val = new Cell(n); public readonly int this[int i] { get => Val.N + i; set { WriteLine(Val.N * 10 + value); } } }
            """];
        yield return ["r3-p21-double-branch-in-arg-compound", """
            var s = Some(new Node(4));
            var sink = new Sink();
            bool f = Yes();
            sink[in s.Value, (f ? 1 : 2)] -= (f ? Reset(out s) : 11);
            s = Some(new Node(20)); WriteLine(s.Value.Val.N);
            static bool Yes() => true;
            static int Reset(out Some<Node> v) { v = Some(new Node(16)); return 9; }
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val = new Cell(n); }
            public struct Sink { public int this[in Node n, int i] { get => n.Val.N + i; set { WriteLine(n.Val.N * 10 + value); } } }
            """];
        yield return ["r3-p21-double-branch-plain-setter", """
            var s = Some(new Node(4));
            bool f = Yes();
            s.Value[(f ? 1 : 2)] = (f ? Reset(out s) : 11);
            s = Some(new Node(20)); WriteLine(s.Value.Val.N);
            static bool Yes() => true;
            static int Reset(out Some<Node> v) { v = Some(new Node(16)); return 9; }
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val = new Cell(n); public readonly int this[int i] { get => Val.N + i; set { WriteLine(Val.N * 10 + value); } } }
            """];
        yield return ["r3-vec-roslice-ret-view-call-reinit", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(TakeRos(s.Value.ReadOnlySlice(0, 1), Reset(out s)));
            static int TakeRos(ReadOnlySpan<int> r, int y) => r[0] + y;
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 0; }
            """];
        yield return ["p03-n90-idx-in-compound-noncopy", """
            var sink = new Sink();
            var s = Some(new Nc(1));
            sink[in s.Value] += Reset(out s);
            WriteLine(s.Value.X);
            static int Reset(out Some<Nc> s) { s = Some(new Nc(5)); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p04b-n90-idx-in-compound-noncopy-cond", """
            bool c = true;
            var sink = new Sink();
            var s = Some(new Nc(1));
            sink[in s.Value] += (c ? Reset(out s) : 20);
            static int Reset(out Some<Nc> s) { s = Some(new Nc(5)); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p04d-n90-idx-in-compound-noncopy-subfield", """
            var sink = new Sink();
            var s = Some(new Outer(new Box(1), new Inner(1)));
            sink[in s.Value.In] += Reset(out s);
            WriteLine(s.Value.In.X);
            static int Reset(out Some<Outer> s) { s = Some(new Outer(new Box(5), new Inner(5))); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Box b, Inner inner) { public Box B = b; public Inner In = inner; }
            public struct Sink
            {
                public readonly int this[in Inner p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p04e-n90-idx-in-compound-noncopy-ok", """
            var sink = new Sink();
            var s = Ok(new Nc(1));
            sink[in s.Value] += Reset(out s);
            WriteLine(s.Value.X);
            static int Reset(out Ok<Nc> s) { s = Ok(new Nc(5)); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p04f-n90-idx-in-compound-noncopy-err", """
            var sink = new Sink();
            var s = Err(new Nc(1));
            sink[in s.Error] += Reset(out s);
            WriteLine(s.Error.X);
            static int Reset(out Err<Nc> s) { s = Err(new Nc(5)); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p04g-n90-idx-in-compound-noncopy-branch-idx", """
            bool c = true;
            var sink = new Sink();
            var s = Some(new Nc(1));
            sink[in s.Value, c ? 0 : 1] += Reset(out s);
            WriteLine(s.Value.X);
            static int Reset(out Some<Nc> s) { s = Some(new Nc(5)); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p, int k]
                {
                    get => p.X + k;
                    set { WriteLine(p.X * 100 + value + k); }
                }
            }
            """];
        yield return ["p04h-n90-idx-in-compound-noncopy-vec", """
            var sink = new Sink();
            Vec<int> v1 = [1, 2];
            var s = Some(v1);
            sink[in s.Value] += Reset(out s);
            WriteLine(s.Value[0]);
            static int Reset(out Some<Vec<int>> s) { Vec<int> v2 = [5, 6]; s = Some(v2); return 10; }
            public struct Sink
            {
                public readonly int this[in Vec<int> v]
                {
                    get => v[0];
                    set { WriteLine(v[0] * 100 + value); }
                }
            }
            """];
        yield return ["p05-n90-idx-in-compound-copy", """
            var sink = new Sink();
            var s = Some(new Pt(1));
            sink[in s.Value] += Set(ref s);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x) { public int X = x; }
            public struct Sink
            {
                public readonly int this[in Pt p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p36-n90-idx-in-compound-copy-branch-idx", """
            bool c = true;
            var sink = new Sink();
            var s = Some(new Pt(1));
            sink[in s.Value, c ? 0 : 1] += Set(ref s);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x) { public int X = x; }
            public struct Sink
            {
                public readonly int this[in Pt p, int k]
                {
                    get => p.X + k;
                    set { WriteLine(p.X * 100 + value + k); }
                }
            }
            """];
        yield return ["p46c-n90-idx-in-compound-noncopy-plain-assign-in-lambda-free-rhs", """
            var sink = new Sink();
            var s = Some(new Nc(1));
            sink[in s.Value] += TakeVal((s = Some(new Nc(5))).Value.X);
            static int TakeVal(int k) => k + 5;
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["p54-n90-idx-in-compound-sub-noncopy", """
            var sink = new Sink();
            var s = Some(new Nc(1));
            sink[in s.Value] -= Reset(out s);
            WriteLine(s.Value.X);
            static int Reset(out Some<Nc> s) { s = Some(new Nc(5)); return 10; }
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X + 20;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """];
        yield return ["a116-b01-read-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, c ? y : 0) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b02-in-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, c ? Peek(in y) : 0) * 10 + y;
            }
            static int Peek(in int p) => p;
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b03-ref-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, c ? Bump(ref y) : 0) * 10 + y;
            }
            static int Bump(ref int p) { p += 1; return p; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b04-assign-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, c ? (y = 4) : 0) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b05-nested-out-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            return HD.F(out int y, c ? Put(out y) : Put(out y)) * 10 + y;
            }
            static int Put(out int p) { p = 7; return 1; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b07-assign-and", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, c && (y = 4) > 0 ? 1 : 0) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b08-assign-switch", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, c switch { true => y = 4, false => 0 }) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b09-field-out-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            P p = new P(); return HD.F(out p.A, c ? (p.A = 4) : 0) * 10 + p.A;
            }
            struct P { public int A; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b10-third-arg-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.G(out y, 1, c ? (y = 4) : 0) * 10 + y;
            }
            static class HD { public static long G(out int a, long u, long v) { a = 5; return u + v; } public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b11-class-field-out-cond", """
            var h = new H();
            WriteLine(Run(h, true));
            static long Run(H h, bool c) => HD.F(out h.X, c ? (h.X = 1) : 0) * 10 + h.X;
            public sealed class H { public int X; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b12-vec-elem-out-cond", """
            Vec<int> v = [0, 0];
            WriteLine(Run(ref v, true));
            static long Run(ref Vec<int> v, bool c) => HD.F(out v[0], c ? Set(ref v) : 0) * 10 + v[0];
            static int Set(ref Vec<int> w) { w[0] = 1; return 1; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b13-class-field-out-cond-read", """
            var h = new H();
            WriteLine(Run(h, true));
            static long Run(H h, bool c) => HD.F(out h.X, c ? h.X + 2 : 0) * 10 + h.X;
            public sealed class H { public int X = 3; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-b14-vec-elem-out-cond-read", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var v = new Vec<S>();
                v.Add(new S(3, 4));
                return F(out v[0].A, c ? Read(v[0].A) : 0);
            }
            static int F(out int dst, int y) { dst = 10; return dst + y; }
            static int Read(int x) => x;
            public struct S(int a, int b) { public int A = a; public int B = b; }
            """];
        yield return ["a116-c01-ctor-out-cond", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return new K(out y, c ? (y = 4) : 0).V * 10 + y;
            }
            sealed class K { public long V; public K(out int a, long v) { a = 5; V = v; } }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-c05-box-nested-out-cond", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                return HD.F(out Box x, c ? Put(out x) : Put(out x)) + x.V;
            }
            static int Put(out Box b) { b = new Box(7); return 2; }
            static class HD { public static long F(out Box a, long v) { a = new Box(1); return v; } }
            sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a116-k03-struct-straight-nested-out", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Q x, Put(out x)) + x.P.Count;
            }
            static int Put(out Q b) { b = new Q(7); return 2; }
            static class HD { public static long F(out Q a, long v) { a = new Q(1); return v; } }
            struct Q(int n) { public Vec<int> P = [n, n, n]; }
            """];
        yield return ["a116-m01-vec-cond-nested-out", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Vec<int> x, c ? Put(out x) : Put(out x)) + x.Count;
            }
            static int Put(out Vec<int> b) { b = [1, 2, 3, 4, 5, 6, 7]; return 2; }
            static class HD { public static long F(out Vec<int> a, long v) { a = [9]; return v; } }
            """];
        yield return ["a116-m02-box-cond-nested-out", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Box x, c ? Put(out x) : Put(out x)) + x.V;
            }
            static int Put(out Box b) { b = new Box(7); return 2; }
            static class HD { public static long F(out Box a, long v) { a = new Box(1); return v; } }
            sealed class Box(int v) { public int V = v; public Vec<int> Pad = [1, 2, 3, 4]; }
            """];
        yield return ["a116-m03-struct-cond-nested-out", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Q x, c ? Put(out x) : 0) + x.P.Count;
            }
            static int Put(out Q b) { b = new Q(7); return 2; }
            static class HD { public static long F(out Q a, long v) { a = new Q(1); return v; } }
            struct Q(int n) { public Vec<int> P = [n, n, n]; }
            """];
        yield return ["a116-m04-vec-cond-out-then-ref", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Vec<int> x, c ? Put(out x) + Grow(ref x) : 0) + x.Count;
            }
            static int Put(out Vec<int> b) { b = [1, 2, 3, 4, 5, 6, 7]; return 2; }
            static int Grow(ref Vec<int> b) { b.Add(8); return b.Count; }
            static class HD { public static long F(out Vec<int> a, long v) { a = [9]; return v; } }
            """];
        yield return ["a116-m08-vec-callee-throws", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Vec<int> x, c ? Put(out x) : Put(out x)) + x.Count;
            }
            static int Put(out Vec<int> b) { b = [1, 2, 3, 4, 5, 6, 7]; return 2; }
            static class HD { public static long F(out Vec<int> a, long v) { if (v > 100) { a = [1]; return 0; } a = [9]; return v; } }
            """];
        yield return ["a116-pos-dctor", """
            WriteLine(Run(false, false, 1));
            static long Run(bool b, bool c, int k)
            {
                return new PD(out int y, (c ? (y = 1) : (y = 0)) + y).R;
            }
            sealed class PD { public long R; public PD(out int a, long v) { a = 1; R = v; } public PD(out Box a, long v) { a = new Box(1); R = v; } }
            sealed class Box(int v) { public int V = v; public int Get() => V; }
            """];
        yield return ["a116-pos-dext", """
            WriteLine(Run(false, false, 1));
            static long Run(bool b, bool c, int k)
            {
                return new ID().FX(out int y, (c ? (y = 1) : (y = 0)) + y);
            }
            sealed class ID { public long F(out int a, long v) { a = 1; return v; } public long F(out Box a, long v) { a = new Box(1); return v; } }
            static class XD { public static long FX(this ID b, out int a, long v) { a = 1; return v; } public static long FX(this ID b, out Box a, long v) { a = new Box(1); return v; } public static long GX<T>(this ID b, out int a, T v) { a = 1; return 7; } public static long GX<T>(this ID b, out Box a, T v) { a = new Box(1); return 7; } }
            sealed class Box(int v) { public int V = v; public int Get() => V; }
            """];
        yield return ["a116-pos-dgeneric", """
            WriteLine(Run(false, false, 1));
            static long Run(bool b, bool c, int k)
            {
                return GD.F(out int y, (c ? (y = 1) : (y = 0)) + y);
            }
            static class GD { public static long F<T>(out int a, T v) { a = 1; return 7; } public static long F<T>(out Box a, T v) { a = new Box(1); return 7; } }
            sealed class Box(int v) { public int V = v; public int Get() => V; }
            """];
        yield return ["a116-pos-dgenext", """
            WriteLine(Run(false, false, 1));
            static long Run(bool b, bool c, int k)
            {
                return new ID().GX(out int y, (c ? (y = 1) : (y = 0)) + y);
            }
            sealed class ID { public long F(out int a, long v) { a = 1; return v; } public long F(out Box a, long v) { a = new Box(1); return v; } }
            static class XD { public static long FX(this ID b, out int a, long v) { a = 1; return v; } public static long FX(this ID b, out Box a, long v) { a = new Box(1); return v; } public static long GX<T>(this ID b, out int a, T v) { a = 1; return 7; } public static long GX<T>(this ID b, out Box a, T v) { a = new Box(1); return 7; } }
            sealed class Box(int v) { public int V = v; public int Get() => V; }
            """];
        yield return ["a116-pos-dinst", """
            WriteLine(Run(false, false, 1));
            static long Run(bool b, bool c, int k)
            {
                return new ID().F(out int y, (c ? (y = 1) : (y = 0)) + y);
            }
            sealed class ID { public long F(out int a, long v) { a = 1; return v; } public long F(out Box a, long v) { a = new Box(1); return v; } }
            sealed class Box(int v) { public int V = v; public int Get() => V; }
            """];
        yield return ["a116-pos-dstatic", """
            WriteLine(Run(false, false, 1));
            static long Run(bool b, bool c, int k)
            {
                return HD.F(out int y, (c ? (y = 1) : (y = 0)) + y);
            }
            static class HD { public static long F(out int a, long v) { a = 1; return v; } public static long F(out Box a, long v) { a = new Box(1); return v; } }
            sealed class Box(int v) { public int V = v; public int Get() => V; }
            """];
        yield return ["a116-s01-read", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, y) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-s02-in", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, Peek(in y)) * 10 + y;
            }
            static int Peek(in int p) => p;
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-s03-ref", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, Bump(ref y)) * 10 + y;
            }
            static int Bump(ref int p) { p += 1; return p; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-s04-assign", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            int y = 3; return HD.F(out y, y = 4) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-s05-nested-out", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
            return HD.F(out int y, Put(out y)) * 10 + y;
            }
            static int Put(out int p) { p = 7; return 1; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-v01-decl-cond-nested-out", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                return HD.F(out int y, c ? Put(out y) : 0) * 10 + y;
            }
            static int Put(out int p) { p = 7; return 1; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-v03-local-cond-nested-out", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                int y;
                return HD.F(out y, c ? Put(out y) : 0) * 10 + y;
            }
            static int Put(out int p) { p = 7; return 1; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-v05-decl-cond-assign", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                return HD.F(out int y, c ? (y = 3) : 0) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-v06-decl-cond-in-call", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                return HD.F(out int y, Set(c ? Put(out y) : 0)) * 10 + y;
            }
            static int Put(out int p) { p = 7; return 1; }
            static int Set(int v) => v;
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a116-v07-initlocal-cond-nested-out", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                int y = 0;
                return HD.F(out y, c ? Put(out y) : 0) * 10 + y;
            }
            static int Put(out int p) { p = 7; return 1; }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } }
            """];
        yield return ["a90-b01-some-unwrapor-plain", """
            Some<Option<int>> s = Some<Option<int>>(Some(1));
            WriteLine(s.Value.UnwrapOr(Set(ref s)));
            WriteLine(s.Value.IsSome);
            static int Set(ref Some<Option<int>> x) { x = Some<Option<int>>(None); return 5; }
            """];
        yield return ["a90-b02-some-unwrapor-cond", """
            Some<Option<int>> s = Some<Option<int>>(Some(1));
            bool f = Id(true);
            WriteLine(s.Value.UnwrapOr(f ? Set(ref s) : 3));
            WriteLine(s.Value.IsSome);
            static int Set(ref Some<Option<int>> x) { x = Some<Option<int>>(None); return 5; }
            static bool Id(bool b) => b;
            """];
        yield return ["a90-b03-ok-unwrapor", """
            Ok<Option<int>> k = Ok<Option<int>>(Some(1));
            WriteLine(k.Value.UnwrapOr(SetK(ref k)));
            static int SetK(ref Ok<Option<int>> x) { x = Ok<Option<int>>(None); return 5; }
            """];
        yield return ["a90-b04-err-expect", """
            Err<Option<int>> e = Err<Option<int>>(Some(7));
            WriteLine(e.Error.Expect(Msg(ref e)));
            static string Msg(ref Err<Option<int>> x) { x = Err<Option<int>>(None); return "gone"; }
            """];
        yield return ["a90-b05-pattern-wrapper-unwrapor", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>> s)
            {
                WriteLine(s.Value.UnwrapOr(Set(ref s)));
                WriteLine(s.Value.IsSome);
            }
            static int Set(ref Some<Option<int>> x) { x = Some<Option<int>>(None); return 5; }
            """];
        yield return ["a90-e01-ext-this-in-plain", """
            Some<P> s = Some(new P(1));
            WriteLine(s.Value.Ext(Set(ref s)));
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; }
            public static class PX { public static int Ext(this in P p, int d) => p.X + d; }
            """];
        yield return ["a90-e02-ext-this-in-cond", """
            Some<P> s = Some(new P(1));
            WriteLine(s.Value.Ext(Id(true) ? Set(ref s) : 3));
            static bool Id(bool b) => b;
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; }
            public static class PX { public static int Ext(this in P p, int d) => p.X + d; }
            """];
        yield return ["a90-f01-subfield-recv-plain", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(s.Value.In.Read(Set(ref s)));
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f02-subfield-recv-cond", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var s = Some(new Outer(new Inner(1)));
                return s.Value.In.Read(c ? Set(ref s) : 0);
            }
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f03-subfield-in-arg-plain", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(TakeIn(in s.Value.In, Set(ref s)));
            static int TakeIn(in Inner i, int y) => i.X + y;
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f04-subfield-in-arg-cond", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var s = Some(new Outer(new Inner(1)));
                return TakeIn(in s.Value.In, c ? Set(ref s) : 0);
            }
            static int TakeIn(in Inner i, int y) => i.X + y;
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f05-subfield-ext-this-in", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(s.Value.In.ExtRead(Set(ref s)));
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Inner i) { public Inner In = i; }
            public static class Ext { public static int ExtRead(this in Inner i, int y) => i.X + y; }
            """];
        yield return ["a90-f06-subfield-indexer", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(s.Value.In[Set(ref s)]);
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int this[int y] => X + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f07-subfield-opt-unwrapor", """
            var s = Some(new WithOpt(Some(1)));
            WriteLine(s.Value.Opt.UnwrapOr(Set(ref s)));
            static int Set(ref Some<WithOpt> s) { s = Some(new WithOpt(Some(5))); return 9; }
            public struct WithOpt(Option<int> opt) { public Option<int> Opt = opt; }
            """];
        yield return ["a90-f08-subfield-opt-expect", """
            var s = Some(new WithOpt(Some(1)));
            WriteLine(s.Value.Opt.Expect(SetMsg(ref s)));
            static string SetMsg(ref Some<WithOpt> s) { s = Some(new WithOpt(Some(5))); return "boom"; }
            public struct WithOpt(Option<int> opt) { public Option<int> Opt = opt; }
            """];
        yield return ["a90-f09-subfield-nested-some", """
            var s = Some(new WithSome(Some(new Inner(1))));
            WriteLine(s.Value.Inner.Value.Read(Set(ref s)));
            static int Set(ref Some<WithSome> s) { s = Some(new WithSome(Some(new Inner(5)))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct WithSome(Some<Inner> inner) { public Some<Inner> Inner = inner; }
            """];
        yield return ["a90-f10-subfield-nested-some-subfield", """
            var s = Some(new WithSome(Some(new Outer(new Inner(1)))));
            WriteLine(s.Value.Inner.Value.In.Read(Set(ref s)));
            static int Set(ref Some<WithSome> s) { s = Some(new WithSome(Some(new Outer(new Inner(5))))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            public struct WithSome(Some<Outer> inner) { public Some<Outer> Inner = inner; }
            """];
        yield return ["a90-f11-subfield-deep-chain", """
            var s = Some(new A(new B(new C(1))));
            WriteLine(s.Value.BVal.CVal.Read(Set(ref s)));
            static int Set(ref Some<A> s) { s = Some(new A(new B(new C(5)))); return 0; }
            public struct C(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct B(C c) { public C CVal = c; }
            public struct A(B b) { public B BVal = b; }
            """];
        yield return ["a90-f12-subfield-ok-recv", """
            var k = Ok(new Outer(new Inner(1)));
            WriteLine(k.Value.In.Read(Set(ref k)));
            static int Set(ref Ok<Outer> k) { k = Ok(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f13-subfield-err-in", """
            var e = Err(new Outer(new Inner(1)));
            WriteLine(TakeIn(in e.Error.In, Set(ref e)));
            static int TakeIn(in Inner i, int y) => i.X + y;
            static int Set(ref Err<Outer> e) { e = Err(new Outer(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-f14-subfield-structfield-wrapper", """
            var h = new H(Some(new R(new P(1))));
            WriteLine(h.W.Value.In.Plus(SetH(ref h)));
            static int SetH(ref H x) { x = new H(Some(new R(new P(40)))); return 5; }
            public struct H(Some<R> w) { public Some<R> W = w; }
            public readonly struct P(int x) { public readonly int X = x; public int Plus(int d) => X * 100 + d; public int this[int d] => X * 100 + d; }
            public readonly struct R(P p) { public readonly P In = p; public readonly Option<int> O = Some(p.X); }
            """];
        yield return ["a90-f15-subfield-scalar-in", """
            Some<R> s = Some(new R(new P(1)));
            WriteLine(F(in s.Value.In.X, SetR(ref s)));
            static int F(in int p, int d) => p * 100 + d;
            static int SetR(ref Some<R> x) { x = Some(new R(new P(40))); return 5; }
            public readonly struct P(int x) { public readonly int X = x; public int Plus(int d) => X * 100 + d; public int this[int d] => X * 100 + d; }
            public readonly struct R(P p) { public readonly P In = p; public readonly Option<int> O = Some(p.X); }
            """];
        yield return ["a90-g01-refret-method-plain", """
            var s = Some(new Pt(1));
            int r = s.Value.GetRef(Set(ref s));
            WriteLine(r);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(int y) => ref X;
            }
            """];
        yield return ["a90-g02-refret-method-cond", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var s = Some(new Pt(1));
                return s.Value.GetRef(c ? Set(ref s) : 0);
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(int y) => ref X;
            }
            """];
        yield return ["a90-g03-refret-indexer-plain", """
            var s = Some(new Pt(1));
            int r = s.Value[Set(ref s)];
            WriteLine(r);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly ref readonly int this[int i]
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => ref X;
                }
            }
            """];
        yield return ["a90-g04-refret-indexer-cond", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var s = Some(new Pt(1));
                int r = s.Value[c ? Set(ref s) : 0];
                return r;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly ref readonly int this[int i]
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => ref X;
                }
            }
            """];
        yield return ["a90-g05-refret-multi-indexer", """
            var s = Some(new Pt(1));
            int r = s.Value[0, Set(ref s)];
            WriteLine(r);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly ref readonly int this[int a, int b]
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => ref X;
                }
            }
            """];
        yield return ["a90-g06-refret-subfield-method", """
            var s = Some(new Outer(new Pt(1)));
            int r = s.Value.In.GetRef(Set(ref s));
            WriteLine(r);
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Pt(5))); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(int y) => ref X;
            }
            public struct Outer(Pt p) { public Pt In = p; }
            """];
        yield return ["a90-i01-user-readonly-indexer", """
            Some<P> s = Some(new P(1));
            WriteLine(s.Value[Set(ref s)]);
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; public int this[int d] => X + d; }
            """];
        yield return ["a90-i02-user-readonly-indexer-cond", """
            Some<P> s = Some(new P(1));
            bool c = Id(true);
            WriteLine(s.Value[c ? Set(ref s) : 0]);
            static bool Id(bool b) => b;
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; public int this[int d] => X * 100 + d; }
            """];
        yield return ["a90-k01-noncopy-user-out-reinit", """
            Some<Q> s = Some(new Q(1));
            WriteLine(s.Value.Plus(Reinit(out s)));
            WriteLine(s.Value.Plus(0));
            static int Reinit(out Some<Q> x) { x = Some(new Q(40)); return 5; }
            public struct Q(int x) { public Box B = new Box(x); public readonly int Plus(int d) => B.V + d; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a90-k02-noncopy-user-cond-out-reinit", """
            Some<Q> s = Some(new Q(1));
            WriteLine(s.Value.Plus(Id(true) ? Reinit(out s) : 3));
            static bool Id(bool b) => b;
            static int Reinit(out Some<Q> x) { x = Some(new Q(40)); return 5; }
            public struct Q(int x) { public Box B = new Box(x); public readonly int Plus(int d) => B.V + d; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a90-k03-noncopy-in-arg-out-reinit", """
            Some<Q> s = Some(new Q(1));
            WriteLine(F(in s.Value, Reinit(out s)));
            static int F(in Q q, int d) => q.B.V + d;
            static int Reinit(out Some<Q> x) { x = Some(new Q(40)); return 5; }
            public struct Q(int x) { public Box B = new Box(x); }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a90-k04-noncopy-option-unwrapor-out", """
            Some<Option<Box>> s = Some<Option<Box>>(Some(new Box(1)));
            WriteLine(s.Value.UnwrapOr(Mk(Reinit(out s))).V);
            static Box Mk(int v) => new Box(v);
            static int Reinit(out Some<Option<Box>> x) { x = Some<Option<Box>>(Some(new Box(40))); return 5; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a90-k05-noncopy-subfield-recv", """
            var s = Some(new Outer(new Inner(new Box(1))));
            WriteLine(s.Value.In.Read(Reset(out s)));
            static int Reset(out Some<Outer> s) { s = Some(new Outer(new Inner(new Box(5)))); return 0; }
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-k06-noncopy-subfield-in-arg", """
            var s = Some(new Outer(new Inner(new Box(1))));
            WriteLine(TakeIn(in s.Value.In, Reset(out s)));
            static int TakeIn(in Inner i, int y) => i.B.V + y;
            static int Reset(out Some<Outer> s) { s = Some(new Outer(new Inner(new Box(5)))); return 0; }
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; }
            public struct Outer(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-k07-noncopy-outer-copy-opt-unwrapor", """
            var s = Some(new WithOptAndBox(Some(1), new Box(2)));
            WriteLine(s.Value.Opt.UnwrapOr(Reset(out s)));
            static int Reset(out Some<WithOptAndBox> s) { s = Some(new WithOptAndBox(Some(5), new Box(6))); return 9; }
            public sealed class Box(int v) { public int V = v; }
            public struct WithOptAndBox(Option<int> opt, Box b) { public Option<int> Opt = opt; public Box B = b; }
            """];
        yield return ["a90-k08-noncopy-refret-method", """
            var s = Some(new Owned(new Box(1)));
            int r = s.Value.GetRef(Reset(out s));
            WriteLine(r);
            static int Reset(out Some<Owned> s) { s = Some(new Owned(new Box(5))); return 0; }
            public sealed class Box(int v) { public int V = v; }
            public struct Owned(Box b)
            {
                public Box B = b;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(int y) => ref B.V;
            }
            """];
        yield return ["a90-k09-noncopy-ro-prop-setter", """
            var s = Some(new Pt(new Box(1)));
            s.Value.Prop = Reset(out s);
            static int Reset(out Some<Pt> s) { s = Some(new Pt(new Box(5))); return 10; }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b)
            {
                public Box B = b;
                public readonly int Prop { set { WriteLine(B.V + value); } }
            }
            """];
        yield return ["a90-k10-noncopy-ro-prop-setter-cond", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(new Box(1)));
                s.Value.Prop = c ? Reset(out s) : 10;
            }
            static int Reset(out Some<Pt> s) { s = Some(new Pt(new Box(5))); return 10; }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int Prop { set { WriteLine(B.V + value); } } }
            """];
        yield return ["a90-k11-noncopy-ro-indexer-setter", """
            var s = Some(new Pt(new Box(1)));
            s.Value[Reset(out s)] = 10;
            static int Reset(out Some<Pt> s) { s = Some(new Pt(new Box(5))); return 0; }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int this[int i] { set { WriteLine(B.V + i + value); } } }
            """];
        yield return ["a90-k12-noncopy-chain-refret-read", """
            var s = Some(new Pt(new Inner(new Box(1))));
            WriteLine(s.Value.GetInner().Read(Reset(out s)));
            static int Reset(out Some<Pt> s) { s = Some(new Pt(new Inner(new Box(5)))); return 0; }
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
            }
            """];
        yield return ["a90-k13-noncopy-asref-arg-before-set", """
            var s = Some<Option<Box>>(Some(new Box(1)));
            WriteLine(Take(s.Value.AsRef(), Reset(out s)));
            static int Take(OptionRef<Box> r, int y) => (r is Some<Box>(var x) ? x.V : 0) + y;
            static int Reset(out Some<Option<Box>> s) { s = Some<Option<Box>>(Some(new Box(5))); return 0; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a90-n01-in-arg-plain", """
            Some<P> s = Some(new P(1));
            WriteLine(F(in s.Value, Set(ref s)));
            static int F(in P p, int d) => p.X + d;
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; }
            """];
        yield return ["a90-n02-in-arg-cond", """
            Some<P> s = Some(new P(1));
            WriteLine(F(in s.Value, Id(true) ? Set(ref s) : 3));
            static bool Id(bool b) => b;
            static int F(in P p, int d) => p.X + d;
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; }
            """];
        yield return ["a90-n03-writeonly-indexer-setter-in-cond", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var sink = new Sink();
                var s = Some(new Pt(1));
                sink[in s.Value, c ? Set(ref s) : 0] = 10;
                return sink.Last;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x) { public int X = x; }
            public sealed class Sink
            {
                public int Last;
                [Mut]
                public int this[in Pt p, int y] { set { Last = p.X + y + value; } }
            }
            """];
        yield return ["a90-n04-subfield-in-indexer-setter", """
            var sink = new Sink();
            var s = Some(new Outer(new Inner(1)));
            sink[in s.Value.In] = Set(ref s);
            WriteLine(sink.Last);
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return 10; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Inner i) { public Inner In = i; }
            public sealed class Sink
            {
                public int Last;
                [Mut]
                public int this[in Inner i] { set { Last = i.X + value; } }
            }
            """];
        yield return ["a90-ns01-vib-method-cond", """
            Run(true);
            static void Run(bool c){var w=Some(new Vibranium.Tally(3));WriteLine(w.Value.Add(c?Reset(ref w):2));}
            static int Reset(ref Some<Vibranium.Tally> w){w=Some(new Vibranium.Tally(17));return 1;}
            namespace Vibranium { public struct Tally(int x){public int X=x;public readonly int Add(int n)=>X+n;} }
            """];
        yield return ["a90-ns02-vib-in-arg-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                WriteLine(Vibranium.Ops.AddIn(in w.Value, c ? Reset(ref w) : 2));
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 1; }
            namespace Vibranium
            {
                public struct Tally(int x) { public int X = x; }
                public static class Ops { public static int AddIn(in Tally t, int n) => t.X + n; }
            }
            """];
        yield return ["a90-ns03-vib-ext-this-in-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                WriteLine(w.Value.AddExt(c ? Reset(ref w) : 2));
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 1; }
            namespace Vibranium
            {
                public struct Tally(int x) { public int X = x; }
                public static class TallyExt { public static int AddExt(this in Tally t, int n) => t.X + n; }
            }
            """];
        yield return ["a90-ns04-vib-ctor-in-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                var b = new Vibranium.CtorBox(in w.Value, c ? Reset(ref w) : 2);
                WriteLine(b.Sum);
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 1; }
            namespace Vibranium
            {
                public struct Tally(int x) { public int X = x; }
                public struct CtorBox
                {
                    public int Sum;
                    public CtorBox(in Tally t, int n) { Sum = t.X + n; }
                }
            }
            """];
        yield return ["a90-ns05-vib-class-ctor-in-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                var b = new Vibranium.CtorCls(in w.Value, c ? Reset(ref w) : 2);
                WriteLine(b.Sum);
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 1; }
            namespace Vibranium
            {
                public struct Tally(int x) { public int X = x; }
                public sealed class CtorCls
                {
                    public int Sum;
                    public CtorCls(in Tally t, int n) { Sum = t.X + n; }
                }
            }
            """];
        yield return ["a90-ns06-vib-ro-idx-getter-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                WriteLine(w.Value[c ? Reset(ref w) : 2]);
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 1; }
            namespace Vibranium
            {
                public struct Tally(int x)
                {
                    public int X = x;
                    public readonly int this[int i] => X + i;
                }
            }
            """];
        yield return ["a90-ns07-vib-ro-prop-setter-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                w.Value.Sink = c ? Reset(ref w) : 10;
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 10; }
            namespace Vibranium
            {
                public struct Tally(int x)
                {
                    public int X = x;
                    public readonly int Sink { set { WriteLine(X + value); } }
                }
            }
            """];
        yield return ["a90-ns08-vib-ro-idx-setter-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                w.Value[c ? Reset(ref w) : 0] = 10;
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 0; }
            namespace Vibranium
            {
                public struct Tally(int x)
                {
                    public int X = x;
                    public readonly int this[int i] { set { WriteLine(X + i + value); } }
                }
            }
            """];
        yield return ["a90-ns09-vib-writeonly-idx-setter-in-cond", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var sink = new Vibranium.Sink();
                var w = Some(new Vibranium.Tally(3));
                sink[in w.Value, c ? Reset(ref w) : 0] = 10;
                return sink.Last;
            }
            static int Reset(ref Some<Vibranium.Tally> w) { w = Some(new Vibranium.Tally(17)); return 0; }
            namespace Vibranium
            {
                public struct Tally(int x) { public int X = x; }
                public sealed class Sink
                {
                    public int Last;
                    [Mut]
                    public int this[in Tally t, int y] { set { Last = t.X + y + value; } }
                }
            }
            """];
        yield return ["a90-ns10-vib-option-arity2-cond", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Option<int, int>(3));
                WriteLine(w.Value.Add(c ? Reset(ref w) : 2));
            }
            static int Reset(ref Some<Vibranium.Option<int, int>> w) { w = Some(new Vibranium.Option<int, int>(17)); return 1; }
            namespace Vibranium
            {
                public struct Option<T, U>(int x) { public int X = x; public readonly int Add(int n) => X + n; }
            }
            """];
        yield return ["a90-p01-ro-prop-setter-rhs", """
            var s = Some(new Pt(1));
            s.Value.Prop = Set(ref s);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int Prop { set { WriteLine(X + value); } }
            }
            """];
        yield return ["a90-p02-ro-prop-setter-rhs-cond", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(1));
                s.Value.Prop = c ? Set(ref s) : 10;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int Prop { set { WriteLine(X + value); } }
            }
            """];
        yield return ["a90-p03-ro-indexer-setter-idx", """
            var s = Some(new Pt(1));
            s.Value[Set(ref s)] = 10;
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int this[int i] { set { WriteLine(X + i + value); } }
            }
            """];
        yield return ["a90-p04-ro-indexer-setter-rhs", """
            var s = Some(new Pt(1));
            s.Value[0] = Set(ref s);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int this[int i] { set { WriteLine(X + i + value); } }
            }
            """];
        yield return ["a90-p05-ro-indexer-setter-rhs-cond", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(1));
                s.Value[0] = c ? Set(ref s) : 10;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int this[int i] { set { WriteLine(X + i + value); } }
            }
            """];
        yield return ["a90-p06-ro-indexer-setter-idx-cond", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(1));
                s.Value[c ? Set(ref s) : 0] = 10;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 0; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int this[int i] { set { WriteLine(X + i + value); } }
            }
            """];
        yield return ["a90-p07-ro-indexer-setter-double-branch-rhs", """
            Run(false, true);
            static void Run(bool c1, bool c2)
            {
                var s = Some(new Pt(1));
                s.Value[c1 ? 1 : 0] = c2 ? Set(ref s) : 10;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x) { public int X = x; public readonly int this[int i] { set { WriteLine(X + i + value); } } }
            """];
        yield return ["a90-p08-subfield-ro-prop-setter-cond", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Outer(new Pt(1)));
                s.Value.In.Prop = c ? Set(ref s) : 10;
            }
            static int Set(ref Some<Outer> s) { s = Some(new Outer(new Pt(5))); return 10; }
            public struct Pt(int x) { public int X = x; public readonly int Prop { set { WriteLine(X + value); } } }
            public struct Outer(Pt p) { public Pt In = p; }
            """];
        yield return ["a90-r01-wrapper-valparam", """
            WriteLine(G(Some<P>(new P(1))));
            static int G(Some<P> s) => s.Value.Plus(WS(ref s));
            static int WS(ref Some<P> x) { x = Some<P>(new P(40)); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-r02-wrapper-in-structfield-cond", """
            H h = new H(Some<Option<int>>(Some(1)));
            WriteLine(h.S.Value.UnwrapOr(Id(true) ? WH(ref h) : 3));
            static int WH(ref H x) { x = new H(Some<Option<int>>(None)); return 5; }
            static bool Id(bool b) => b;
            public struct H(Some<Option<int>> s) { public Some<Option<int>> S = s; }
            """];
        yield return ["a90-r03-wrapper-in-vec-elem", """
            Vec<Some<P>> v = [Some<P>(new P(1))];
            WriteLine(v[0].Value.Plus(WW(ref v)));
            static int WW(ref Vec<Some<P>> w) { w[0] = Some<P>(new P(40)); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-r04-nested-wrapper", """
            Some<Some<Option<int>>> s = Some(Some<Option<int>>(Some(1)));
            WriteLine(s.Value.Value.UnwrapOr(Set(ref s)));
            static int Set(ref Some<Some<Option<int>>> x) { x = Some(Some<Option<int>>(None)); return 5; }
            """];
        yield return ["a90-r05-is-designation-cond", """
            Option<P> oo = Some<P>(new P(1));
            if (oo is Some<P> s) { WriteLine(s.Value.Plus(Id(true) ? WS(ref s) : 3)); }
            static int WS(ref Some<P> x) { x = Some<P>(new P(40)); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-r06-assign-in-arg", """
            Some<Option<int>> s = Some<Option<int>>(Some(1));
            WriteLine(s.Value.UnwrapOr(Val(s = Some<Option<int>>(None))));
            static int Val(Some<Option<int>> x) => x.Value.IsSome ? 1 : 5;
            """];
        yield return ["a90-r07-or-arg", """
            Some<Option<int>> s = Some<Option<int>>(Some(1));
            bool f = !s.Value.IsSome;
            WriteLine(s.Value.UnwrapOr(f || Set(ref s) ? 5 : 3));
            static bool Set(ref Some<Option<int>> x) { x = Some<Option<int>>(None); return true; }
            """];
        yield return ["a90-u01-some-user-plain", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(s.Value.Plus(WS(ref s)));
            static int WS(ref Some<P> x) { x = Some<P>(new P(40)); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-u02-some-user-cond", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(s.Value.Plus(Id(true) ? WS(ref s) : 3));
            static int WS(ref Some<P> x) { x = Some<P>(new P(40)); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-u03-ok-user-plain", """
            Ok<P> s = Ok<P>(new P(1));
            WriteLine(s.Value.Plus(WK(ref s)));
            static int WK(ref Ok<P> x) { x = Ok<P>(new P(40)); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-u04-err-user-cond", """
            Err<P> s = Err<P>(new P(1));
            WriteLine(s.Error.Plus(Id(true) ? WE(ref s) : 3));
            static int WE(ref Err<P> x) { x = Err<P>(new P(40)); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["a90-u05-user-readonly-member", """
            Some<P> s = Some(new P(1));
            WriteLine(s.Value.Plus(Set(ref s)));
            static int Set(ref Some<P> x) { x = Some(new P(40)); return 5; }
            public readonly struct P(int x) { public readonly int X = x; public int Plus(int d) => X + d; }
            """];
        yield return ["a90-v01-chain-refret-0arg-read", """
            var s = Some(new Pt(new Inner(1)));
            WriteLine(s.Value.GetInner().Read(Set(ref s)));
            static int Set(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
            }
            """];
        yield return ["a90-v02-chain-refprop-read", """
            var s = Some(new Pt(new Inner(1)));
            WriteLine(s.Value.RefProp.Read(Set(ref s)));
            static int Set(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                public readonly ref readonly Inner RefProp
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => ref In;
                }
            }
            """];
        yield return ["a90-v03-chain-refidx-read", """
            var s = Some(new Pt(new Inner(1)));
            WriteLine(s.Value[0].Read(Set(ref s)));
            static int Set(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                public readonly ref readonly Inner this[int k]
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => ref In;
                }
            }
            """];
        yield return ["a90-v04-chain-static-in-refret-read", """
            var s = Some(new Pt(new Inner(1)));
            WriteLine(GetInner(in s.Value).Read(Set(ref s)));
            static int Set(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return 0; }
            [return: LifetimeOf("p")]
            static ref readonly Inner GetInner(in Pt p) => ref p.In;
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Pt(Inner i) { public Inner In = i; }
            """];
        yield return ["a90-v05-chain-refret-cond-read", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var s = Some(new Pt(new Inner(1)));
                return s.Value.GetInner().Read(c ? Set(ref s) : 0);
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
            }
            """];
        yield return ["a90-v06-chain-view-method-arg", """
            var s = Some(new Holder(Some(1)));
            WriteLine(Take(s.Value.View(), Set(ref s)));
            static int Take(OptionRef<int> r, int y) => (r is Some<int>(var x) ? x : 0) + y;
            static int Set(ref Some<Holder> s) { s = Some(new Holder(Some(5))); return 0; }
            public struct Holder(Option<int> o)
            {
                public Option<int> O = o;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly OptionRef<int> View() => O.AsRef();
            }
            """];
        yield return ["a90-v07-chain-view-prop-arg", """
            var s = Some(new Holder(Some(1)));
            WriteLine(Take(s.Value.ViewProp, Set(ref s)));
            static int Take(OptionRef<int> r, int y) => (r is Some<int>(var x) ? x : 0) + y;
            static int Set(ref Some<Holder> s) { s = Some(new Holder(Some(5))); return 0; }
            public struct Holder(Option<int> o)
            {
                public Option<int> O = o;
                public readonly OptionRef<int> ViewProp
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => O.AsRef();
                }
            }
            """];
        yield return ["a90-v08-asref-arg-before-set", """
            var s = Some<Option<int>>(Some(1));
            WriteLine(Take(s.Value.AsRef(), Set(ref s)));
            static int Take(OptionRef<int> r, int y) => (r is Some<int>(var x) ? x : 0) + y;
            static int Set(ref Some<Option<int>> s) { s = Some<Option<int>>(Some(5)); return 0; }
            """];
        yield return ["a90-v09-asref-subfield-arg-before-set", """
            var s = Some(new Holder(Some(1)));
            WriteLine(Take(s.Value.O.AsRef(), Set(ref s)));
            static int Take(OptionRef<int> r, int y) => (r is Some<int>(var x) ? x : 0) + y;
            static int Set(ref Some<Holder> s) { s = Some(new Holder(Some(5))); return 0; }
            public struct Holder(Option<int> o) { public Option<int> O = o; }
            """];
        yield return ["a90-v10-asref-cond-arg", """
            WriteLine(Run(true));
            static int Run(bool c)
            {
                var s = Some<Option<int>>(Some(1));
                return Take(s.Value.AsRef(), c ? Set(ref s) : 0);
            }
            static int Take(OptionRef<int> r, int y) => (r is Some<int>(var x) ? x : 0) + y;
            static int Set(ref Some<Option<int>> s) { s = Some<Option<int>>(Some(5)); return 0; }
            """];
        yield return ["a90-v11-prop-level-unscoped-refprop", """
            var s = Some(new Pt(new Inner(1)));
            WriteLine(s.Value.RefProp.Read(Set(ref s)));
            static int Set(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                public readonly ref readonly Inner RefProp { [return: LifetimeOf("this")] get => ref In; }
            }
            """];
        yield return ["a90-v12-prop-level-unscoped-viewprop", """
            var s = Some(new Holder(Some(1)));
            WriteLine(Take(s.Value.ViewProp, Set(ref s)));
            static int Set(ref Some<Holder> s) { s = Some(new Holder(Some(5))); return 0; }
            static int Take(OptionRef<int> r, int y) => (r is Some<int>(var x) ? x : 0) + y;
            public struct Holder(Option<int> o)
            {
                public Option<int> O = o;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                public readonly OptionRef<int> ViewProp { [return: LifetimeOf("this")] get => O.AsRef(); }
            }
            """];
        yield return ["a91-d34-optref-param-switch-rebind", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = n) => 1, Some<Box> => 2, _ => 3 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t01-optref-tag-arms-assign", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = n) => 1, Some<Box> => 2, _ => 3 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t02-optref-tag-arms-call-dest", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = Pick(n)) => 1, Some<Box> => 2, _ => 3 };
            static OptionRef<Box> Pick(OptionRef<Box> z) => z;
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t04-optref-var-arm", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = n) => 1, var z => z.IsSome ? 2 : 3 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t05-optref-not-none-arm", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { not NoneType when Re(m = n) => 1, NoneType => 4, _ => 2 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t06-optref-stmt", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n)
            {
                switch (m)
                {
                    case Some<Box> when Re(m = n):
                        return 1;
                    case Some<Box>:
                        return 2;
                    default:
                        return 3;
                }
            }
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t08-optref-later-binding-arm", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = Some(new Box(50));
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = n) => 0, Some<Box>(var c) => c.V + 100, _ => -1 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-t11-optref-no-later-test", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = n) => 1, _ => 3 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-v06-optref-param-stmt", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n)
            {
                switch (m)
                {
                    case Some<Box> when Re(m = n):
                        return 1;
                    case Some<Box>:
                        return 2;
                    default:
                        return 3;
                }
            }
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["a91-v09-optref-param-no-later-test", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsRef(), y.AsRef()));
            static int G(OptionRef<Box> m, OptionRef<Box> n) => m switch { Some<Box> when Re(m = n) => 1, _ => 3 };
            static bool Re(OptionRef<Box> z) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
    }

    [Theory]
    [MemberData(nameof(RejectRows))]
    public Task UseOfBorrowedWrapperOrOutTarget_IsExactlyVib003(string shape, string source) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, "VIB003");

    // Exact Roslyn ID sets: the last-argument twins (VIB002), rows that already had another violation, unchanged rows, and
    // accepted rows whose natives or vib-self are pinned elsewhere (the N131 out-argument leak; vib-self differences).
    public static IEnumerable<object[]> ControlRows()
    {
        yield return ["p72-vec-readonlyslice-ref-moved-wins", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(0, SetRef(ref s))[0]);
            static int SetRef(ref Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 1; }
            """, "VIB001"];
        yield return ["r3-35-nested-branch-some", """
            var s = Some(new Node(4));
            bool f = Yes();
            s.Value[(f ? Reset(out s) : 2)] -= (f ? Reset(out s) : 11);
            s = Some(new Node(20)); WriteLine(s.Value.Val.N);
            static bool Yes() => true;
            static int Reset(out Some<Node> v) { v = Some(new Node(16)); return 3; }
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val=new Cell(n); public readonly int this[int i] { get=>Val.N+i; set { WriteLine(Val.N*10+value); } } }
            """, ""];
        yield return ["r3-36-nested-branch-ok", """
            var s = Ok(new Node(4));
            bool f = Yes();
            s.Value[(f ? Reset(out s) : 2)] -= 4;
            s = Ok(new Node(20)); WriteLine(s.Value.Val.N);
            static bool Yes() => true;
            static int Reset(out Ok<Node> v) { v = Ok(new Node(16)); return 3; }
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val=new Cell(n); public readonly int this[int i] { get=>Val.N+i; set { WriteLine(Val.N*10+value); } } }
            """, ""];
        yield return ["r3-37-nested-branch-err", """
            var s = Err(new Node(4));
            bool f = Yes();
            s.Error[1] -= (f ? Reset(out s) : 11);
            s = Err(new Node(20)); WriteLine(s.Error.Val.N);
            static bool Yes() => true;
            static int Reset(out Err<Node> v) { v = Err(new Node(16)); return 3; }
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val=new Cell(n); public readonly int this[int i] { get=>Val.N+i; set { WriteLine(Val.N*10+value); } } }
            """, ""];
        yield return ["r3-38-nested-branch-some", """
            var s = Some(new Node(4));
            bool f = Yes();
            s.Value[(f ? 1 : 2)] -= (f ? 10 : 11);
            s = Some(new Node(20)); WriteLine(s.Value.Val.N);
            static bool Yes() => true;
            public sealed class Cell(int k) { public int N = k; }
            public struct Node(int n) { public Cell Val=new Cell(n); public readonly int this[int i] { get=>Val.N+i; set { WriteLine(Val.N*10+value); } } }
            """, ""];
        yield return ["r3-vec-roslice-ret-view-dangling", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            var ro = s.Value.ReadOnlySlice(0, 1);
            WriteLine(ro[0]);
            """, "VIB007"];
        yield return ["p55-n90-idx-in-compound-noncopy-ctrl-then-reassign", """
            var sink = new Sink();
            var s = Some(new Nc(1));
            sink[in s.Value] += 10;
            s = Some(new Nc(5));
            WriteLine(s.Value.X);
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """, ""];
        yield return ["r-idx-arg-cmp-copy-out", """
            var s = Some(new Payload(1));
            s.Value[Reset(out s)] += 10;
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }

            public struct Payload(int x)
            {
                public int X = x;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-idx-arg-inc-branch-nc-out", """
            var s = Some(new Payload(1));
            var c = Test();
            s.Value[(c ? Reset(out s) : 1)]++;
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }
            static bool Test() => true;
            public sealed class Box(int x) { public int X = x; }
            public struct Payload(int x)
            {
                public Box B = new Box(x); public readonly int X => B.X;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-idx-rhs-branch-nc-out", """
            var s = Some(new Payload(1));
            var c = Test();
            s.Value[0] += (c ? Reset(out s) : 20);
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }
            static bool Test() => true;
            public sealed class Box(int x) { public int X = x; }
            public struct Payload(int x)
            {
                public Box B = new Box(x); public readonly int X => B.X;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-implicit-arg-cmp-copy-out", """
            var s = Some(new Payload(1));
            s.Value[^Reset(out s)] += 10;
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }

            public struct Payload(int x)
            {
                public int X = x;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-implicit-arg-inc-copy-out", """
            var s = Some(new Payload(1));
            s.Value[^Reset(out s)]++;
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }

            public struct Payload(int x)
            {
                public int X = x;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-implicit-rhs-branch-nc-out", """
            var s = Some(new Payload(1));
            var c = Test();
            s.Value[^1] += (c ? Reset(out s) : 20);
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }
            static bool Test() => true;
            public sealed class Box(int x) { public int X = x; }
            public struct Payload(int x)
            {
                public Box B = new Box(x); public readonly int X => B.X;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-prop-rhs-branch-nc-out", """
            var s = Some(new Payload(1));
            var c = Test();
            s.Value.Prop += (c ? Reset(out s) : 20);
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }
            static bool Test() => true;
            public sealed class Box(int x) { public int X = x; }
            public struct Payload(int x)
            {
                public Box B = new Box(x); public readonly int X => B.X;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-prop-rhs-copy-out", """
            var s = Some(new Payload(1));
            s.Value.Prop += Reset(out s);
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }

            public struct Payload(int x)
            {
                public int X = x;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            """, ""];
        yield return ["r-subfield-rhs-branch-nc", """
            var s = Some(new Outer(new Payload(1)));
            var c = Test();
            s.Value.In.Prop += (c ? Reset(out s) : 20);
            static int Reset(out Some<Outer> s) { s = Some(new Outer(new Payload(5))); return 2; }
            static bool Test() => true;
            public sealed class Box(int x) { public int X = x; }
            public struct Payload(int x)
            {
                public Box B = new Box(x); public readonly int X => B.X;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                
            }
            public struct Outer(Payload inner)
            {
                public Payload In = inner;
            }
            """, ""];
        yield return ["z-in-arg-inc-release", """
            var sink = new Sink();
            var s = Some(new Nc(1));
            WriteLine(sink[in s.Value]++);
            s = Some(new Nc(5));
            WriteLine(s.Value.X);
            public sealed class Box(int x) { public int X = x; }
            public struct Nc(int x) { public Box B = new Box(x); public readonly int X => B.X; }
            public struct Sink
            {
                public readonly int this[in Nc p]
                {
                    get => p.X;
                    set { WriteLine(p.X * 100 + value); }
                }
            }
            """, ""];
        yield return ["a116-k-j02-decl-cond-later-arg", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Vec<int> x, c ? 2 : 0) + x.Count;
            }
            static class HD { public static long F(out Vec<int> a, long v) { a = [9]; return v; } }
            """, ""];
        yield return ["a116-k-j07-uninit-local-cond-later-arg", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                Vec<int> x;
                long r = HD.F(out x, c ? 2 : 0);
                return r + x.Count;
            }
            static class HD { public static long F(out Vec<int> a, long v) { a = [9]; return v; } }
            """, ""];
        yield return ["a116-l01-outvar-then-out-same-call", """
            WriteLine(HO.Two(out int y, out y));
            static class HO { public static long Two(out int a, out int b) { a = 1; b = 2; return 1; } }
            """, "VIB002"];
        yield return ["a116-q3-01-outvar-out-tail-cond", """
            WriteLine(Run(true)); static int Run(bool c)=>F(out int y,out y,c?1:2); static int F(out int a,out int b,int i){a=3;b=5;return i;}
            """, "VIB002,VIB003"];
        yield return ["a116-x01-box-assign-then-eq-null", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                return HD.F(out Box x, Two(c ? (x = new Box(7)).V : (x = new Box(8)).V, 0)) + x.V;
            }
            static int Two(int a, int b) => a + b;
            static class HD { public static long F(out Box a, long v) { a = new Box(1); return v; } }
            sealed class Box(int v) { public int V = v; }
            """, "VIB001,VIB003"];
        yield return ["a116-x02-vec-cond-assign-expr", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                return HD.F(out Vec<int> x, c ? (x = [1, 2, 3]).Count : 0) + x.Count;
            }
            static class HD { public static long F(out Vec<int> a, long v) { a = [9]; return v; } }
            """, "VIB001,VIB003"];
        yield return ["a116-x03-linear-cond-assign", """
            WriteLine(Run(true));
            static long Run(bool c)
            {
                long r = HD.F(out Res x, (c ? (x = new Res(7)) : (x = new Res(8))).Id);
                using (x) { r += x.Id; }
                return r;
            }
            static class HD { public static long F(out Res a, long v) { a = new Res(1); return v; } }
            sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() => WriteLine("dispose " + Id); }
            """, "VIB000,VIB001,VIB003,VIB006"];
        yield return ["a90-c03-later-args-read", """
            Some<P> s = Some(new P(1));
            WriteLine(F(in s.Value, s.Value.X));
            WriteLine(s.Value.Plus(s.Value.X));
            WriteLine(s.Value.Plus(Id(true) ? s.Value.X : 0));
            static bool Id(bool b) => b;
            static int F(in P p, int d) => p.X + d;
            public readonly struct P(int x) { public readonly int X = x; public int Plus(int d) => X + d; }
            """, ""];
        yield return ["a90-c05-in-arg-dest-same", """
            Some<P> s = Some<P>(new P(1));
            s = N(in s.Value, 5);
            WriteLine(s.Value.X);
            static Some<P> N(in P a, int d) => Some<P>(new P(a.X + d));
            public struct P(int x) { public int X = x; }
            """, ""];
        yield return ["a90-c10-ref-readonly-local-read", """
            Some<P> s = Some(new P(7));
            ref readonly P r = ref s.Value;
            WriteLine(r.X);
            Some<Option<int>> t = Some<Option<int>>(Some(3));
            ref readonly Option<int> q = ref t.Value;
            WriteLine(q.UnwrapOr(0));
            public readonly struct P(int x) { public readonly int X = x; public readonly int Y = 9; }
            """, ""];
        yield return ["a90-c12-noncopy-recv-then-reuse", """
            Some<Q> s = Some(new Q(1));
            int r = s.Value.Plus(2);
            WriteLine(r);
            WriteLine(s.Value.Plus(3));
            public struct Q(int x) { public Box B = new Box(x); public readonly int Plus(int d) => B.V + d; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["a90-c13-noncopy-in-arg-then-reassign", """
            Some<Q> s = Some(new Q(1));
            int r = Sum(in s.Value, 2);
            s = Some(new Q(7));
            WriteLine(r);
            WriteLine(Sum(in s.Value, 0));
            static int Sum(in Q q, int d) => q.B.V + d;
            public struct Q(int x) { public Box B = new Box(x); }
            public sealed class Box(int v) { public int V = v; }
            """, ""];
        yield return ["a90-c23-noncopy-refret-moved-wins", """
            var s = Some(new Pt(new Box(1)));
            int r = s.Value.GetRef(Take(s));
            WriteLine(r);
            static int Take(Some<Pt> s) => s.Value.B.V;
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b)
            {
                public Box B = b;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(int y) => ref B.V;
            }
            """, "VIB001"];
        yield return ["a90-c24-ro-prop-setter-ok-ctrl", """
            var s = Some(new Pt(1));
            s.Value.Prop = 10;
            public struct Pt(int x) { public int X = x; public readonly int Prop { set { WriteLine(X + value); } } }
            """, ""];
        yield return ["a90-c26-compound-ro-prop-ctrl", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(1));
                s.Value.Prop += c ? Set(ref s) : 10;
            }
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 10; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int Prop { get => X; set { WriteLine(X + value); } }
            }
            """, ""];
        yield return ["a90-c27-noncopy-compound-idx-cond-ctrl", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(new Box(1)));
                s.Value[c ? 0 : 1] += 10;
                s = Some(new Pt(new Box(5)));
                WriteLine(s.Value.B.V);
            }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int this[int i] { get => B.V + i; set { WriteLine(B.V + i + value); } } }
            """, ""];
        yield return ["a90-c28-noncopy-prop-setter-then-reassign", """
            var s = Some(new Pt(new Box(1)));
            s.Value.Prop = 10;
            s = Some(new Pt(new Box(5)));
            WriteLine(s.Value.B.V);
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int Prop { set { WriteLine(B.V + value); } } }
            """, ""];
        yield return ["a90-c29-noncopy-prop-setter-cond-then-reassign", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(new Box(1)));
                s.Value.Prop = c ? 10 : 20;
                s = Some(new Pt(new Box(5)));
                WriteLine(s.Value.B.V);
            }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int Prop { set { WriteLine(B.V + value); } } }
            """, ""];
        yield return ["a90-c30-noncopy-indexer-setter-cond-then-reassign", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(new Box(1)));
                s.Value[c ? 0 : 1] = 10;
                s = Some(new Pt(new Box(5)));
                WriteLine(s.Value.B.V);
            }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int this[int i] { set { WriteLine(B.V + i + value); } } }
            """, ""];
        yield return ["a90-c33-noncopy-asref-then-reassign", """
            var s = Some<Option<Box>>(Some(new Box(1)));
            WriteLine(Take(s.Value.AsRef(), 2));
            s = Some<Option<Box>>(Some(new Box(5)));
            WriteLine(s.Value.Unwrap().V);
            static int Take(OptionRef<Box> r, int y) => (r is Some<Box>(var x) ? x.V : 0) + y;
            public sealed class Box(int v) { public int V = v; }
            """, ""];
        yield return ["a90-c34-noncopy-compound-prop-cond-ctrl", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new Pt(new Box(1)));
                s.Value.Prop += c ? 10 : 20;
                s = Some(new Pt(new Box(5)));
                WriteLine(s.Value.B.V);
            }
            public sealed class Box(int v) { public int V = v; }
            public struct Pt(Box b) { public Box B = b; public readonly int Prop { get => B.V; set { WriteLine(B.V + value); } } }
            """, ""];
        yield return ["a90-c36-ns-vib-ctor-in-ctrl", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                var b = new Vibranium.CtorBox(in w.Value, c ? w.Value.X : 2);
                WriteLine(b.Sum);
            }
            namespace Vibranium
            {
                public struct Tally(int x) { public int X = x; }
                public struct CtorBox
                {
                    public int Sum;
                    public CtorBox(in Tally t, int n) { Sum = t.X + n; }
                }
            }
            """, ""];
        yield return ["a90-l01-recv-ref-last", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(s.Value.PlusRefS(ref s));
            public struct P(int x) { public int X = x; public readonly int PlusRef(ref P q) { q = new P(40); return X; } public readonly int PlusRefS(ref Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusOutS(out Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusRefH(ref H q) { q = new H(new P(40)); return X; } }
            public struct H(P p) { public P P = p; }
            """, "VIB002"];
        yield return ["a90-l02-recv-out-last", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(s.Value.PlusOutS(out s));
            public struct P(int x) { public int X = x; public readonly int PlusRef(ref P q) { q = new P(40); return X; } public readonly int PlusRefS(ref Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusOutS(out Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusRefH(ref H q) { q = new H(new P(40)); return X; } }
            public struct H(P p) { public P P = p; }
            """, "VIB002"];
        yield return ["a90-l03-in-then-ref", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(G(in s.Value, ref s));
            static int G(in P a, ref Some<P> b) { b = Some<P>(new P(40)); return a.X; }
            public struct P(int x) { public int X = x; public readonly int PlusRef(ref P q) { q = new P(40); return X; } public readonly int PlusRefS(ref Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusOutS(out Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusRefH(ref H q) { q = new H(new P(40)); return X; } }
            public struct H(P p) { public P P = p; }
            """, "VIB002"];
        yield return ["a90-l04-in-then-out", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(G(in s.Value, out s));
            static int G(in P a, out Some<P> b) { b = Some<P>(new P(40)); return a.X; }
            public struct P(int x) { public int X = x; public readonly int PlusRef(ref P q) { q = new P(40); return X; } public readonly int PlusRefS(ref Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusOutS(out Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusRefH(ref H q) { q = new H(new P(40)); return X; } }
            public struct H(P p) { public P P = p; }
            """, "VIB002"];
        yield return ["a90-l05-twin-local-recv-ref-last", """
            P o = new P(1);
            WriteLine(o.PlusRef(ref o));
            public struct P(int x) { public int X = x; public readonly int PlusRef(ref P q) { q = new P(40); return X; } public readonly int PlusRefS(ref Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusOutS(out Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusRefH(ref H q) { q = new H(new P(40)); return X; } }
            public struct H(P p) { public P P = p; }
            """, "VIB002"];
        yield return ["a90-l06-twin-local-in-then-ref", """
            H h = new H(new P(1));
            WriteLine(F(in h.P, ref h));
            static int F(in P a, ref H b) { b = new H(new P(40)); return a.X; }
            public struct P(int x) { public int X = x; public readonly int PlusRef(ref P q) { q = new P(40); return X; } public readonly int PlusRefS(ref Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusOutS(out Some<P> q) { q = Some<P>(new P(40)); return X; } public readonly int PlusRefH(ref H q) { q = new H(new P(40)); return X; } }
            public struct H(P p) { public P P = p; }
            """, "VIB002"];
        yield return ["a90-l07-subfield-recv-ref-last", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(s.Value.In.Read(ref s));
            public struct Inner(int x) { public int X = x; public readonly int Read(ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return X; } }
            public struct Outer(Inner i) { public Inner In = i; }
            """, "VIB002"];
        yield return ["a90-l08-subfield-in-then-ref", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(TakeIn(in s.Value.In, ref s));
            static int TakeIn(in Inner i, ref Some<Outer> s) { s = Some(new Outer(new Inner(5))); return i.X; }
            public struct Inner(int x) { public int X = x; }
            public struct Outer(Inner i) { public Inner In = i; }
            """, "VIB002"];
        yield return ["a90-l09-refret-method-ref-last", """
            var s = Some(new Pt(1));
            int r = s.Value.GetRef(ref s);
            WriteLine(r);
            public struct Pt(int x)
            {
                public int X = x;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(ref Some<Pt> s) { s = Some(new Pt(5)); return ref X; }
            }
            """, "VIB002"];
        yield return ["a90-l10-chain-refret-ref-last", """
            var s = Some(new Pt(new Inner(1)));
            WriteLine(s.Value.GetInner().Read(ref s));
            public struct Inner(int x)
            {
                public int X = x;
                public readonly int Read(ref Some<Pt> s) { s = Some(new Pt(new Inner(5))); return X; }
            }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
            }
            """, "VIB002"];
        yield return ["a90-l11-asref-ref-last", """
            var s = Some<Option<int>>(Some(1));
            WriteLine(Take(s.Value.AsRef(), ref s));
            static int Take(OptionRef<int> r, ref Some<Option<int>> s) { s = Some<Option<int>>(Some(5)); return r is Some<int>(var x) ? x : 0; }
            """, "VIB002"];
        yield return ["a90-q1-01-chain0-move", """
            var s = Some(new Pt(new Inner(new Box(1))));
            WriteLine(s.Value.GetInner().Read(Use(s)));
            static int Use(Some<Pt> x) => 0;
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInnerA(int k) => ref In;
            }
            """, "VIB001,VIB003"];
        yield return ["a90-q1-03-chain0-ref", """
            var s = Some(new Pt(new Inner(new Box(1))));
            WriteLine(s.Value.GetInner().Read(Bump(ref s)));
            static int Bump(ref Some<Pt> x) => 0;
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInnerA(int k) => ref In;
            }
            """, "VIB001,VIB003"];
        yield return ["a90-q1-04-chain0-read-payload", """
            var s = Some(new Pt(new Inner(new Box(1))));
            WriteLine(s.Value.GetInner().Read(s.Value.In.B.V));
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInnerA(int k) => ref In;
            }
            """, "VIB001,VIB003"];
        yield return ["a90-q1-05-asref-move", """
            var s = Some<Option<Box>>(Some(new Box(1)));
            WriteLine(Take(s.Value.AsRef(), UseO(s)));
            static int Take(OptionRef<Box> r, int y) => (r is Some<Box>(var x) ? x.V : 0) + y;
            static int UseO(Some<Option<Box>> x) => 0;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001,VIB003"];
        yield return ["a90-q1-07-asref-ref", """
            var s = Some<Option<Box>>(Some(new Box(1)));
            WriteLine(Take(s.Value.AsRef(), BumpO(ref s)));
            static int Take(OptionRef<Box> r, int y) => (r is Some<Box>(var x) ? x.V : 0) + y;
            static int BumpO(ref Some<Option<Box>> x) => 0;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001,VIB003"];
        yield return ["a90-q1-09-chain0-move-cond", """
            WriteLine(Run(true));
            static int Run(bool c) { var s = Some(new Pt(new Inner(new Box(1)))); return s.Value.GetInner().Read(c ? Use(s) : 0); }
            static int Use(Some<Pt> x) => 0;
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInnerA(int k) => ref In;
            }
            """, "VIB001,VIB003"];
        yield return ["a90-q2-01-noncopy-last-direct-out", """
            var s=Some(new Leaf(new Box(6))); WriteLine(s.Value.R(out s));
            public struct Leaf(Box b){public Box B=b; public readonly int R(out Some<Leaf> target){target=Some(new Leaf(new Box(8)));return B.X;}}
            public sealed class Box(int x){public int X=x;}
            """, "VIB003"];
        yield return ["a90-q2-02-noncopy-last-direct-ref", """
            var s=Some(new Leaf(new Box(6))); WriteLine(s.Value.R(ref s));
            public struct Leaf(Box b){public Box B=b; public readonly int R(ref Some<Leaf> target){target=Some(new Leaf(new Box(8)));return B.X;}}
            public sealed class Box(int x){public int X=x;}
            """, "VIB001"];
        yield return ["a90-q2-03-noncopy-last-inarg-out", """
            var s=Some(new Leaf(new Box(6))); WriteLine(Read(in s.Value, out s));
            static int Read(in Leaf p, out Some<Leaf> target){target=Some(new Leaf(new Box(8)));return p.B.X;}
            public struct Leaf(Box b){public Box B=b;}
            public sealed class Box(int x){public int X=x;}
            """, "VIB003"];
        yield return ["a90-q2-04-noncopy-last-inarg-ref", """
            var s=Some(new Leaf(new Box(6))); WriteLine(Read(in s.Value, ref s));
            static int Read(in Leaf p, ref Some<Leaf> target){target=Some(new Leaf(new Box(8)));return p.B.X;}
            public struct Leaf(Box b){public Box B=b;}
            public sealed class Box(int x){public int X=x;}
            """, "VIB001"];
        yield return ["a90-q2-05-noncopy-last-asref-out", """
            var s=Some<Option<Box>>(Some(new Box(6))); WriteLine(Read(s.Value.AsRef(), out s));
            static int Read(OptionRef<Box> r,out Some<Option<Box>> target){target=Some<Option<Box>>(Some(new Box(8)));return r is Some<Box>(var x)?x.X:0;}
            public sealed class Box(int x){public int X=x;}
            """, "VIB002"];
        yield return ["a90-q2-06-noncopy-last-asref-ref", """
            var s=Some<Option<Box>>(Some(new Box(6))); WriteLine(Read(s.Value.AsRef(), ref s));
            static int Read(OptionRef<Box> r,ref Some<Option<Box>> target){target=Some<Option<Box>>(Some(new Box(8)));return r is Some<Box>(var x)?x.X:0;}
            public sealed class Box(int x){public int X=x;}
            """, "VIB001,VIB002"];
        yield return ["a90-t01-twin-class-local-out", """
            Box b = new Box(1);
            WriteLine(b.Get(Reinit(out b)));
            WriteLine(b.V);
            static int Reinit(out Box x) { x = new Box(40); return 5; }
            public sealed class Box(int v) { public int V = v; public int Get(int d) => V + d; }
            """, "VIB003"];
        yield return ["a90-t02-twin-noncopy-local-out", """
            Q q = new Q(1);
            WriteLine(q.Plus(Reinit(out q)));
            static int Reinit(out Q x) { x = new Q(40); return 5; }
            public struct Q(int x) { public Box B = new Box(x); public readonly int Plus(int d) => B.V + d; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["a90-t03-twin-local-in-arg", """
            P s = new P(1);
            WriteLine(F(in s, Set(ref s)));
            static int F(in P p, int d) => p.X + d;
            static int Set(ref P x) { x = new P(40); return 5; }
            public readonly struct P(int x) { public readonly int X = x; }
            """, "VIB003"];
        yield return ["a90-t04-twin-local-indexer", """
            P p = new P(1);
            WriteLine(p[Set(ref p)]);
            static int Set(ref P x) { x = new P(40); return 5; }
            public readonly struct P(int x) { public readonly int X = x; public int this[int d] => X + d; }
            """, "VIB003"];
        yield return ["a91-d3-d22-ros-param-local-copy", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                ReadOnlySpan<int> t = r;
                return t[(t = q).Length - 3];
            }
            static bool Id(bool x) => x;
            """, "VIB003"];
        yield return ["a91-d3-d25-span-param-index-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];

            WriteLine(G(a.AsSpan(), b.AsSpan()));
            static int G(Span<int> s, Span<int> q) { return s[(s = q).Length - 3]; }
            """, "VIB003"];
        yield return ["a91-d3-d26-span-param-slice-rebind-cond", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];

            WriteLine(G(a.AsSpan(), b.AsSpan(), Id(true)));
            static int G(Span<int> s, Span<int> q, bool f) { return s.Slice(f ? (s = q).Length - 3 : 0)[0]; }
            static bool Id(bool x) => x;
            """, "VIB003"];
        yield return ["a91-d3-d31-ros-in-param-rebind-cut", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];

            ReadOnlySpan<int> x = a.AsReadOnlySpan();
            WriteLine(G(ref x, b.AsReadOnlySpan()));
            static int G(ref ReadOnlySpan<int> r, ReadOnlySpan<int> q) { return r[(r = q).Length - 3]; }
            """, "VIB000,VIB003,VIB004"];
        yield return ["a91-d3-d32-ros-param-rebind-via-helper-ref", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];

            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) { return r[Re(ref r, q)]; }
            static int Re(ref ReadOnlySpan<int> r, ReadOnlySpan<int> q) { r = q; return 4; }
            """, "VIB000,VIB003,VIB004"];
        yield return ["a91-k34-L7-operand-then-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q)
            {
                ReadOnlySpan<int> k = r;
                int x = Pair(r, r = q);
                r = k;
                int y = Pair(r.Slice(1), r = q);
                r = k;
                int z = In2(in r[0], (r = q)[0]);
                return x * 10000 + y * 100 + z;
            }
            static int Pair(ReadOnlySpan<int> p, ReadOnlySpan<int> q) => p[0] + q[0] % 10;
            static int In2(in int p, int q) => p + q % 10;
            """, ""];
        yield return ["pin-e18r-expect-message-writer-result", """
            Result<Box, int> r = Ok(new Box(5));
            WriteLine(r.Expect(Id(true) ? Msg(out r) : "b").V);
            static string Msg(out Result<Box, int> r) { r = Ok(new Box(8)); return "m"; }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
    }

    [Theory]
    [MemberData(nameof(ControlRows))]
    public Task Controls_HaveExactRoslynIds(string shape, string source, string ids) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, ids);

    public static IEnumerable<object[]> AcceptedRows()
    {
        yield return ["p71-vec-readonlyslice-post-reinit-ctrl", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.ReadOnlySlice(0, 1)[0]);
            Vec<int> w = [99, 88];
            s = Some(Move(w));
            WriteLine(s.Value.Count);
            """, "10\n2\n"];
        yield return ["r3-vec-asrospan-index-reset-ctrl", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.AsReadOnlySpan()[Reset(out s)]);
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 1; }
            """, "20\n"];
        yield return ["r3-vec-asrospan-post-reinit-ctrl", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(s.Value.AsReadOnlySpan()[0]);
            Vec<int> w = [99, 88];
            s = Some(Move(w));
            WriteLine(s.Value.Count);
            """, "10\n2\n"];
        yield return ["r3-vec-asrospan-take-reset-ctrl", """
            Vec<int> v = [10, 20, 30];
            var s = Some(Move(v));
            WriteLine(TakeRos(s.Value.AsReadOnlySpan(), Reset(out s)));
            static int TakeRos(ReadOnlySpan<int> r, int y) => r[0] + y;
            static int Reset(out Some<Vec<int>> s) { Vec<int> w = [99, 88, 77]; s = Some(Move(w)); return 0; }
            """, "10\n"];
        yield return ["r3-vec-roslice-class-box-ctrl", """
            Vec<int> v = [10, 20, 30];
            var s = Some(new Box(Move(v)));
            WriteLine(s.Value.V.ReadOnlySlice(0, Reset(out s))[0]);
            static int Reset(out Some<Box> s) { Vec<int> w = [99, 88, 77]; s = Some(new Box(Move(w))); return 1; }
            public sealed class Box(Vec<int> v) { public Vec<int> V = Move(v); }
            """, "10\n"];
        yield return ["r-range-branch-nc-out", """
            var s = Some(new Payload(1));
            var c = Test();
            WriteLine(s.Value[(c ? Reset(out s) : 1)..^1]);
            static int Reset(out Some<Payload> s) { s = Some(new Payload(5)); return 2; }
            static bool Test() => true;
            public sealed class Box(int x) { public int X = x; }
            public struct Payload(int x)
            {
                public Box B = new Box(x); public readonly int X => B.X;
                public readonly int Prop { get => X; set { WriteLine(X * 100 + value); } }
                public readonly int this[int i] { get => X + i; set { WriteLine(X * 100 + value); } }
                public readonly int Length => X;
                public readonly int Slice(int start, int len) => X * 100 + start * 10 + len;
            }
            """, "118\n"];
        yield return ["a116-k-h05-single-out", """
            WriteLine(Run());
            static long Run()
            {
                long r = HD.F(out Vec<int> x);
                return r + x.Count;
            }
            static class HD { public static long F(out Vec<int> a) { a = [9]; return 2; } }
            """, "3\n"];
        yield return ["a116-k-j08-init-local-cond-later-arg", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                Vec<int> x = [1, 2];
                long r = HD.F(out x, c ? 2 : 0);
                return r + x.Count;
            }
            static class HD { public static long F(out Vec<int> a, long v) { a = [9]; return v; } }
            """, "400\n"];
        yield return ["a116-k-k02-struct-reassign-initialized", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                Put(out Q x);
                return HD.F(out x, 2) + x.P.Count;
            }
            static int Put(out Q b) { b = new Q(7); return 2; }
            static class HD { public static long F(out Q a, long v) { a = new Q(1); return v; } }
            struct Q(int n) { public Vec<int> P = [n, n, n]; }
            """, "1000\n"];
        yield return ["a116-k-k05-vec-out-then-ref", """
            long t = 0;
            for (int i = 0; i < 200; i++) { t += Run(i % 2 == 0); }
            WriteLine(t);
            static long Run(bool c)
            {
                Put(out Vec<int> x);
                long r = c ? Grow(ref x) : 0;
                return r + x.Count;
            }
            static int Put(out Vec<int> b) { b = [1, 2, 3, 4, 5, 6, 7]; return 2; }
            static int Grow(ref Vec<int> b) { b.Add(8); return b.Count; }
            """, "2300\n"];
        yield return ["a116-k-r05-in-cond-read", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
                int y = 3; return HD.I(in y, c ? y : 0) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } public static long R(ref int a, long v) { a += 100; return v; } public static long I(in int a, long v) => a * 1000 + v; }
            """, "30033\n30003\n"];
        yield return ["a116-k-r06-ref-cond-read", """
            WriteLine(Run(true));
            WriteLine(Run(false));
            static long Run(bool c)
            {
                int y = 3; return HD.R(ref y, c ? y : 0) * 10 + y;
            }
            static class HD { public static long F(out int a, long v) { a = 5; return v; } public static long R(ref int a, long v) { a += 100; return v; } public static long I(in int a, long v) => a * 1000 + v; }
            """, "133\n103\n"];
        yield return ["a90-c01-class-payload-out", """
            Some<Box> s = Some(new Box(1));
            WriteLine(s.Value.Get(Reinit(out s)));
            WriteLine(s.Value.V);
            static int Reinit(out Some<Box> x) { x = Some(new Box(40)); return 5; }
            public sealed class Box(int v) { public int V = v; public int Get(int d) => V + d; }
            """, "6\n40\n"];
        yield return ["a90-c02-mut-member-copy", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(s.Value.Bump(WS(ref s)));
            static int WS(ref Some<P> x) { x = Some<P>(new P(40)); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "6\n"];
        yield return ["a90-c04-recv-dest-same", """
            Some<P> s = Some<P>(new P(1));
            s = s.Value.Next(5);
            WriteLine(s.Value.X);
            public struct P(int x) { public int X = x; public readonly Some<P> Next(int d) => Some<P>(new P(X + d)); }
            """, "6\n"];
        yield return ["a90-c06-recv-in-wrapper-last", """
            Some<P> s = Some<P>(new P(1));
            WriteLine(s.Value.PlusIn(in s));
            public struct P(int x) { public int X = x; public readonly int PlusIn(in Some<P> q) => X + q.Value.X; }
            """, "2\n"];
        yield return ["a90-c07-noarg-member-then-write", """
            Some<P> s = Some(new P(1));
            WriteLine(s.Value.Plus(2));
            WriteLine(s.Value.X);
            public readonly struct P(int x) { public readonly int X = x; public int Plus(int d) => X + d; }
            """, "3\n1\n"];
        yield return ["a90-c08-vec-payload-index", """
            Some<Vec<int>> s = Some<Vec<int>>([1, 2]);
            WriteLine(s.Value[Reinit(out s)]);
            static int Reinit(out Some<Vec<int>> x) { x = Some<Vec<int>>([40, 50]); return 1; }
            """, "2\n"];
        yield return ["a90-c09-switch-payload-guard-writes", """
            Some<Option<int>> s = Some<Option<int>>(Some(1));
            WriteLine(s.Value switch { Some<int> when W(ref s) => 1, Some<int> => 2, _ => 3 });
            static bool W(ref Some<Option<int>> x) { x = Some<Option<int>>(None); return false; }
            """, "2\n"];
        yield return ["a90-c11-noncopy-recv-then-reassign", """
            Some<Q> s = Some(new Q(1));
            int r = s.Value.Plus(2);
            s = Some(new Q(7));
            WriteLine(r);
            WriteLine(s.Value.Plus(0));
            public struct Q(int x) { public Box B = new Box(x); public readonly int Plus(int d) => B.V + d; }
            public sealed class Box(int v) { public int V = v; }
            """, "3\n7\n"];
        yield return ["a90-c14-noncopy-unwrapor-plain-then-reassign", """
            Some<Option<Box>> s = Some<Option<Box>>(Some(new Box(1)));
            WriteLine(s.Value.UnwrapOr(new Box(2)).V);
            s = Some<Option<Box>>(Some(new Box(7)));
            WriteLine(s.Value.UnwrapOr(new Box(0)).V);
            public sealed class Box(int v) { public int V = v; }
            """, "1\n7\n"];
        yield return ["a90-c15-noncopy-unwrapor-cond-then-reassign", """
            Some<Option<Box>> s = Some<Option<Box>>(Some(new Box(1)));
            bool c = Id(true);
            WriteLine(s.Value.UnwrapOr(c ? new Box(2) : new Box(0)).V);
            s = Some<Option<Box>>(Some(new Box(7)));
            WriteLine(s.Value.UnwrapOr(new Box(0)).V);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "1\n7\n"];
        yield return ["a90-c16-subfield-control-ok", """
            var s = Some(new Outer(new Inner(1)));
            WriteLine(s.Value.In.Read(2));
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """, "3\n"];
        yield return ["a90-c17-class-deref-stops-subfield", """
            var s = Some(new Holder(new Box(new Inner(1))));
            WriteLine(s.Value.B.In.Read(Reset(out s)));
            static int Reset(out Some<Holder> s) { s = Some(new Holder(new Box(new Inner(5)))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public sealed class Box(Inner i) { public Inner In = i; }
            public struct Holder(Box b) { public Box B = b; }
            """, "1\n"];
        yield return ["a90-c18-noncopy-subfield-recv-then-reassign", """
            var s = Some(new Outer(new Inner(new Box(1))));
            WriteLine(s.Value.In.Read(2));
            s = Some(new Outer(new Inner(new Box(5))));
            WriteLine(s.Value.In.B.V);
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Outer(Inner i) { public Inner In = i; }
            """, "3\n5\n"];
        yield return ["a90-c19-noncopy-outer-copy-opt-unwrapor-then-reassign", """
            var s = Some(new WithOptAndBox(Some(1), new Box(2)));
            WriteLine(s.Value.Opt.UnwrapOr(9));
            s = Some(new WithOptAndBox(Some(5), new Box(6)));
            WriteLine(s.Value.B.V);
            public sealed class Box(int v) { public int V = v; }
            public struct WithOptAndBox(Option<int> opt, Box b) { public Option<int> Opt = opt; public Box B = b; }
            """, "1\n6\n"];
        yield return ["a90-c20-noncopy-outer-copy-opt-unwrapor-cond-then-reassign", """
            Run(true);
            static void Run(bool c)
            {
                var s = Some(new WithOptAndBox(Some(1), new Box(2)));
                WriteLine(s.Value.Opt.UnwrapOr(c ? 9 : 8));
                s = Some(new WithOptAndBox(Some(5), new Box(6)));
                WriteLine(s.Value.B.V);
            }
            public sealed class Box(int v) { public int V = v; }
            public struct WithOptAndBox(Option<int> opt, Box b) { public Option<int> Opt = opt; public Box B = b; }
            """, "1\n6\n"];
        yield return ["a90-c21-refret-method-ok-ctrl", """
            var s = Some(new Pt(1));
            int r = s.Value.GetRef(2);
            WriteLine(r);
            public struct Pt(int x)
            {
                public int X = x;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly int GetRef(int y) => ref X;
            }
            """, "1\n"];
        yield return ["a90-c22-refret-fromend-indexer-ctrl", """
            var s = Some(new Pt(1));
            int r = s.Value[^Set(ref s)];
            WriteLine(r);
            static int Set(ref Some<Pt> s) { s = Some(new Pt(5)); return 1; }
            public struct Pt(int x)
            {
                public int X = x;
                public readonly int Length => 1;
                public readonly ref readonly int this[int i]
                {
                    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                    [return: LifetimeOf("this")]
                    get => ref X;
                }
            }
            """, "1\n"];
        yield return ["a90-c25-class-prop-setter-out-ctrl", """
            var s = Some(new Box(1));
            s.Value.Prop = Reset(out s);
            static int Reset(out Some<Box> s) { s = Some(new Box(5)); return 10; }
            public sealed class Box(int x)
            {
                public int X = x;
                public int Prop { set { WriteLine(X + value); } }
            }
            """, "11\n"];
        yield return ["a90-c31-chain-refret-through-class-ctrl", """
            var s = Some(new Holder(new Box(new Inner(1))));
            WriteLine(s.Value.B.GetInner().Read(Reset(out s)));
            static int Reset(out Some<Holder> s) { s = Some(new Holder(new Box(new Inner(5)))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public sealed class Box(Inner i)
            {
                public Inner In = i;
                [return: LifetimeOf("this")]
                public ref readonly Inner GetInner() => ref In;
            }
            public struct Holder(Box b) { public Box B = b; }
            """, "1\n"];
        yield return ["a90-c32-noncopy-chain-refret-then-reassign", """
            var s = Some(new Pt(new Inner(new Box(1))));
            WriteLine(s.Value.GetInner().Read(2));
            s = Some(new Pt(new Inner(new Box(5))));
            WriteLine(s.Value.In.B.V);
            public sealed class Box(int v) { public int V = v; }
            public struct Inner(Box b) { public Box B = b; public readonly int Read(int y) => B.V + y; }
            public struct Pt(Inner i)
            {
                public Inner In = i;
                [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
                [return: LifetimeOf("this")]
                public readonly ref readonly Inner GetInner() => ref In;
            }
            """, "3\n5\n"];
        yield return ["a90-c35-ns-vib-sound-ctrl", """
            Run(true);
            static void Run(bool c)
            {
                var w = Some(new Vibranium.Tally(3));
                WriteLine(w.Value.Add(c ? w.Value.X : 2));
            }
            namespace Vibranium
            {
                public struct Tally(int x)
                {
                    public int X = x;
                    public readonly int Add(int n) => X + n;
                }
            }
            """, "6\n"];
        yield return ["a90-c37-non-unscoped-heap-refret-ctrl", """
            var s = Some(new Pt(new Box(new Inner(1))));
            WriteLine(s.Value.GetBoxIn().Read(Reset(out s)));
            static int Reset(out Some<Pt> s) { s = Some(new Pt(new Box(new Inner(5)))); return 0; }
            public struct Inner(int x) { public int X = x; public readonly int Read(int y) => X + y; }
            public sealed class Box(Inner i) { public Inner In = i; }
            public struct Pt(Box b) { public Box B = b; [return: LifetimeOf("this")] public readonly ref readonly Inner GetBoxIn() => ref B.In; }
            """, "1\n"];
        yield return ["a91-d3-d01-ros-param-index-plain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[(r = q).Length - 3];
            }
            static bool Id(bool x) => x;
            """, "30\n"];
        yield return ["a91-d3-d02-ros-param-hat-len-order", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[^((r = q).Length - 4)];
            }
            static bool Id(bool x) => x;
            """, "50\n"];
        yield return ["a91-d3-d03-ros-param-range-from-len-order", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r[((r = q).Length - 4)..];
                return t.Length * 1000 + t[^1];
            }
            static bool Id(bool x) => x;
            """, "4050\n"];
        yield return ["a91-d3-d04-ros-param-range-to", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r[..((r = q).Length - 3)];
                return t.Length * 1000 + t[^1];
            }
            static bool Id(bool x) => x;
            """, "2020\n"];
        yield return ["a91-d3-d05-ros-param-range-hat-from", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r[^((r = q).Length - 2)..];
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "3030\n"];
        yield return ["a91-d3-d06-ros-param-slice1", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r.Slice((r = q).Length - 4);
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "4020\n"];
        yield return ["a91-d3-d07-ros-param-slice2-first", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r.Slice((r = q).Length - 4, 2);
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "2020\n"];
        yield return ["a91-d3-d08-ros-param-slice2-second", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r.Slice(1, (r = q).Length - 3);
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "2020\n"];
        yield return ["a91-d3-d09-ros-param-slice2-two-rebinds", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r.Slice((r = q).Length - 5, (r = p).Length - 1);
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "1100\n"];
        yield return ["a91-d3-d10-ros-param-index-cond", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[f ? (r = q).Length - 3 : 0];
            }
            static bool Id(bool x) => x;
            """, "30\n"];
        yield return ["a91-d3-d11-ros-param-hat-cond", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[^(f ? (r = q).Length - 4 : 1)];
            }
            static bool Id(bool x) => x;
            """, "50\n"];
        yield return ["a91-d3-d12-ros-param-range-cond", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r[(f ? (r = q).Length - 4 : 0)..];
                return t.Length * 1000 + t[^1];
            }
            static bool Id(bool x) => x;
            """, "4050\n"];
        yield return ["a91-d3-d13-ros-param-slice-switch", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                int k = f ? 1 : 2;
                var t = r.Slice(k switch { 1 => (r = q).Length - 4, _ => 0 });
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "4020\n"];
        yield return ["a91-d3-d14-ros-param-args-order", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                int x = Two(r[0], r[(r = q).Length - 3]);
                int y = Two(r.Length, (r = p).Length);
                return x * 10000 + y;
            }
            static bool Id(bool x) => x;
            static int Two(int x, int y) => x * 100 + y;
            """, "1300502\n"];
        yield return ["a91-d3-d15-ros-param-pair-byvalue-order", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return Pair(r, r = q);
            }
            static bool Id(bool x) => x;
            static int Pair(ReadOnlySpan<int> x, ReadOnlySpan<int> y) => x[0] * 100 + y[0];
            """, "110\n"];
        yield return ["a91-d3-d16-ros-param-nested-index", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[r[(r = p).Length - 2] / 100];
            }
            static bool Id(bool x) => x;
            """, "200\n"];
        yield return ["a91-d3-d17-ros-param-self-slice-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[(r = r.Slice(1)).Length - 1] * 10 + r.Length;
            }
            static bool Id(bool x) => x;
            """, "32\n"];
        yield return ["a91-d3-d18-ros-param-range-both-hat-rebinds", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                var t = r[^((r = q).Length - 3)..^((r = p).Length - 2)];
                return t.Length * 1000 + t[0];
            }
            static bool Id(bool x) => x;
            """, "2100\n"];
        yield return ["a91-d3-d19-ros-param-slice-chain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                int x = r.Slice((r = q).Length - 4).Slice(1)[0];
                int y = r[((r = p).Length - 1)..][^1];
                return x * 1000 + y;
            }
            static bool Id(bool x) => x;
            """, "30200\n"];
        yield return ["a91-d3-d20-ros-param-loop-compound", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                int acc = 0;
                for (int i = 0; i < 3; i++) { acc += r[(r = i == 1 ? q : p).Length - 2]; }
                return acc;
            }
            static bool Id(bool x) => x;
            """, "240\n"];
        yield return ["a91-d3-d21-ros-param-index-and-or", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                int x = r[f && (r = q).Length > 4 ? 4 : 0];
                int y = r[!f || (r = p).Length > 1 ? 1 : 0];
                return x * 1000 + y;
            }
            static bool Id(bool x) => x;
            """, "50200\n"];
        yield return ["a91-d3-d23-ros-param-binary-order", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[0] * 10000 + r[(r = q).Length - 3] * 100 + r[1];
            }
            static bool Id(bool x) => x;
            """, "13020\n"];
        yield return ["a91-d3-d24-ros-param-hat-plain-len5", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), c.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, ReadOnlySpan<int> p, bool f)
            {
                return r[^Len(r = q, 4)];
            }
            static bool Id(bool x) => x;
            static int Len(ReadOnlySpan<int> x, int d) => x.Length - d;
            """, "50\n"];
        yield return ["a91-d3-d27-ros-byte-param-ext-rebind", """
            WriteLine(Eq("abc"u8, "abd"u8));
            WriteLine(Sw("xbc"u8, "ab"u8));
            WriteLine(Ix("abc"u8, "cba"u8));
            static bool Eq(ReadOnlySpan<byte> r, ReadOnlySpan<byte> q) => r.SequenceEqual(r = q);
            static bool Sw(ReadOnlySpan<byte> r, ReadOnlySpan<byte> q) => r.StartsWith(r = q);
            static int Ix(ReadOnlySpan<byte> r, ReadOnlySpan<byte> q) => r.IndexOf((r = q)[0]);
            """, "False\nFalse\n2\n"];
        yield return ["a91-d3-d28-ros-option-elem-param-rebind", """
            Vec<Option<int>> a = [None];
            Vec<Option<int>> b = [Some(2), Some(3)];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            WriteLine(K(a.AsReadOnlySpan(), b.AsReadOnlySpan(), Id(true)));
            static int G(ReadOnlySpan<Option<int>> r, ReadOnlySpan<Option<int>> q) => r[0].UnwrapOr((r = q)[0].UnwrapOr(9)) * 10 + r.Length;
            static int K(ReadOnlySpan<Option<int>> r, ReadOnlySpan<Option<int>> q, bool f) => r[0].UnwrapOr(f ? (r = q)[1].UnwrapOr(9) : 7) * 10 + r.Length;
            static bool Id(bool x) => x;
            """, "22\n32\n"];
        yield return ["a91-d3-d29-string-param-index-rebind", """
            WriteLine(G("abc", "zz"));
            static byte G(string s, string t) => s.AsUtf8()[(s = t).AsUtf8().Length - 1];
            """, "98\n"];
        yield return ["a91-d3-d30-ros-param-instance-method", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];

            var k = new K();
            WriteLine(k.M(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            WriteLine(K.S(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            public struct K
            {
                public int Z;
                public readonly int M(ReadOnlySpan<int> r, ReadOnlySpan<int> q) => r[(r = q).Length - 3] + Z;
                public static int S(ReadOnlySpan<int> r, ReadOnlySpan<int> q) { int n = r[^((r = q).Length - 4)]; return n + r.Length; }
            }
            """, "30\n55\n"];
        yield return ["a91-d3-d33-ros-param-foreach-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Vec<int> c = [100, 200];

            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q)
            {
                int acc = 0;
                foreach (int x in r) { acc += x; r = q; }
                return acc * 10 + r.Length;
            }
            """, "65\n"];
        yield return ["a91-k-v03-ros-elem-scrutinee", """
            Vec<int> a = [1, 2];
            Vec<int> b = [5, 6];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) => r[0] switch { 1 when Chk((r = q).Length) => 10, 1 => 20, _ => 30 };
            static bool Chk(int n) => false;
            """, "20\n"];
        yield return ["a91-k-v04-ros-option-elem-scrutinee", """
            Vec<Option<int>> a = [Some(1)];
            Vec<Option<int>> b = [None];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<Option<int>> r, ReadOnlySpan<Option<int>> q) => r[0] switch { Some<int>(var v) when Chk((r = q).Length) => v, Some<int>(var w) => w + 100, _ => -1 };
            static bool Chk(int n) => false;
            """, "101\n"];
        yield return ["a91-k13-L1b-ros-elem-scrutinee", """
            Vec<Option<int>> a = [Some(1)];
            Vec<Option<int>> b = [None];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<Option<int>> r, ReadOnlySpan<Option<int>> q) => r[0] switch { Some<int>(var v) when Chk((r = q).Length) => v, Some<int>(var w) => w + 100, _ => -1 };
            static bool Chk(int n) => false;
            """, "101\n"];
        yield return ["a91-k14-L1b-ros-elem-scrutinee-slice-dest", """
            Vec<Option<int>> a = [Some(1)];
            Vec<Option<int>> b = [None];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<Option<int>> r, ReadOnlySpan<Option<int>> q) => r[0] switch { Some<int> when Chk((r = q.Slice(0)).Length) => 1, Some<int>(var w) => w + 100, _ => -1 };
            static bool Chk(int n) => false;
            """, "101\n"];
        yield return ["a91-k16-L1b-ros-int-elem-scrutinee", """
            Vec<int> a = [1, 2];
            Vec<int> b = [5, 6];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) => r[0] switch { 1 when Chk((r = q).Length) => 10, 1 => 20, _ => 30 };
            static bool Chk(int n) => false;
            """, "20\n"];
        yield return ["a91-k17-L1b-ros-length-scrutinee", """
            WriteLine(G("abc"u8, "z"u8));
            static int G(ReadOnlySpan<byte> r, ReadOnlySpan<byte> q) => r.Length switch { 3 when Chk((r = q).Length) => 1, 3 => 2, _ => 9 };
            static bool Chk(int n) => false;
            """, "2\n"];
        yield return ["a91-k18-L2-ros-index-assign", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) => r[(r = q).Length - 3];
            """, "30\n"];
        yield return ["a91-k19-L2-ros-index-call-dest", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) => r[(r = Pick(q)).Length - 3];
            static ReadOnlySpan<int> Pick(ReadOnlySpan<int> z) => z;
            """, "30\n"];
        yield return ["a91-k20-L2-ros-index-intrinsic-dest", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) => r[(r = q.Slice(1)).Length - 3] * 100 + r[^((r = q[2..]).Length - 2)];
            """, "3050\n"];
        yield return ["a91-k24-L2b-ros-elem-recv", """
            Vec<Option<int>> a = [Some(1)];
            Vec<Option<int>> b = [Some(2), Some(3)];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<Option<int>> r, ReadOnlySpan<Option<int>> q) => r[0].UnwrapOr((r = q)[0].UnwrapOr(9)) * 10 + r.Length;
            """, "12\n"];
        yield return ["a91-k27-L4-ros-elem-ref-then-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) { ref readonly int e = ref r[0]; r = q; return e * 1000 + r[0]; }
            """, "1010\n"];
        yield return ["a91-k28-L4-span-elem-ref-then-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsSpan(), b.AsSpan()));
            WriteLine(a[0] * 100 + b[0]);
            static int G(Span<int> s, Span<int> q) { ref int e = ref s[0]; s = q; e = 5; return s[0]; }
            """, "10\n510\n"];
        yield return ["a91-k29-L5-ros-slice-then-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) { var t = r.Slice(1); r = q; return t[0] * 1000 + r[0] + t.Length; }
            """, "2012\n"];
        yield return ["a91-k30-L5-optmut-asref-then-rebind", """
            Option<Box> x = Some(new Box(1));
            Option<Box> y = None;
            WriteLine(G(x.AsMut(), y.AsMut()));
            static int G(OptionMut<Box> m, OptionMut<Box> n) { OptionRef<Box> v = m.AsRef(); m = n; return (v.IsSome ? 10 : 20) + (m.IsSome ? 1 : 2); }
            public sealed class Box(int v) { public int V = v; }
            """, "12\n"];
        yield return ["a91-k31-L6-ros-foreach-rebind", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan()));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q) { int acc = 0; foreach (int x in r) { acc += x; r = q; } return acc * 10 + r.Length; }
            """, "65\n"];
        yield return ["pin-c3a-cond-same-local-unwrapor-out", """
            Option<Node> x = Some(new Node(1, None));
            bool c = x.IsSome;
            Node n = (c ? x : x).UnwrapOr(Fill(out x));
            WriteLine(n.V);
            WriteLine(x.IsSome);
            static Node Fill(out Option<Node> o) { o = Some(new Node(5, None)); return new Node(0, None); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "1\nTrue\n"];
        yield return ["pin-c3b-switch-same-local-unwrapor-out", """
            Option<Node> x = Some(new Node(1, None));
            int k = 1;
            Node n = (k switch { 0 => x, _ => x }).UnwrapOr(Fill(out x));
            WriteLine(n.V);
            WriteLine(x.IsSome);
            static Node Fill(out Option<Node> o) { o = Some(new Node(5, None)); return new Node(0, None); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "1\nTrue\n"];
    }

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public async Task SoundRows_AcceptWithNativeParity(string shape, string source, string expected)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }
}
