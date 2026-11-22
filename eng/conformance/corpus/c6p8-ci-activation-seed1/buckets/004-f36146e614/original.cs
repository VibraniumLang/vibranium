WriteLine(F(-2, true, -2L));
WriteLine(F(1, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += Us2(2) + Us2(5) * 10 + Us2(8) * 100;
    ushort x3 = (ushort)(p0 * p0);
    ReadOnlySpan<E> ca5 = [E.B, E.A];
    ReadOnlySpan<E> cb6 = [E.C, E.A];
    acc += First4(p1 ? ca5 : cb6);
    return acc;
}
static Option<Vec<int>> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us2(int p) { int n = 0; Option<Vec<int>> o = UsMk1(p); n += o switch { not NoneType => 100, NoneType => 7 }; n += o.IsSome ? 1000 : 0; return n; }
static int First4(scoped ReadOnlySpan<E> q) => q.Length > 0 ? (int)q[0] : -1;
public enum E { A = 1, B = 2, C = 5 }
