WriteLine(F(-1, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Us2(0) + Us2(3) * 10 + Us2(9) * 100;
    const long K3 = 8L;
    ushort x4 = (ushort)K3;
    static int L5(int a)
    {
        int x8 = Find6(a) switch { Some<int>(_) => a, NoneType when a < 4 => -1, _ => a };
        return 0 + ((int)K3);
    }
    acc += L5(2);
    return acc;
}
static Option<Vec<int>> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us2(int p) { int n = 0; Option<Vec<int>> o = UsMk1(p); n += o switch { not NoneType when p != 3 => 100, Some<Vec<int>>(var v) when p >= 3 => 50 + v.Count + v[0], Some<Vec<int>>(var v) => v.Count + v[0], NoneType => 7 }; n += o.IsSome ? 1000 : 0; return n; }
static Option<int> Find6(int k) => k > 3 ? (Option<int>)Some(k * 4) : None;
