WriteLine(C.M(-1, true));
WriteLine(C.M(5, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        WriteLine((p1 || p1) || (-1.25 < 6.25));
        acc += Us4(2) + Us4(5) * 10 + Us4(9) * 100;
        acc += P5(p1 ? acc : acc, -3);
        acc += Us2(2) + Us2(4) * 10 + Us2(6) * 100;
        return acc;
    }
    public static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
    public static int Us2(int p) { int n = 0; for (int i = 0; i < 2; i++) { switch (UsMk1((p + i))) { case not NoneType: n += 100; continue; case NoneType: n += 7; break; } n += 1; } return n; }
    public static Option<Vec<int>> UsMk3(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
    public static int Us4(int p) { int n = 0; Option<Vec<int>> o = UsMk3(p); n += o switch { not Some<Vec<int>> when p < 3 => 3, Some<Vec<int>>(var v) when p > 5 => 50 + v.Count + v[0], Some<Vec<int>>(var v) => v.Count + v[0], NoneType => 7 }; n += o.IsSome ? 1000 : 0; return n; }
    public static int P5(int c0, int c1) => c0 + c1;
}
