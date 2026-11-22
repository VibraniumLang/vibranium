WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Us2(2) + Us2(5) * 10 + Us2(8) * 100;
    ReadOnlySpan<E> ca5 = [];
    acc += First4(ca5);
    return 0;
}
static Option<Vec<int>> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us2(int p) { int n = 0; Option<Vec<int>> o = UsMk1(p); n += o switch { not NoneType => 100, NoneType => 7 }; n += o.IsSome ? 1000 : 0; return n; }
static int First4(scoped ReadOnlySpan<E> q) => 0;
public enum E { A = 1, B = 2, C = 5 }
