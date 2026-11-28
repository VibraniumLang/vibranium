WriteLine(0);
public static class C
{
    public static Option<Vec<int>> UsMk3(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
    public static int Us4(int p) { int n = 0; Option<Vec<int>> o = UsMk3(p); n += o switch { not Some<Vec<int>> when p < 3 => 3, Some<Vec<int>>(var v) when p > 5 => 50 + v.Count + v[0], Some<Vec<int>>(var v) => v.Count + v[0], NoneType => 7 }; n += o.IsSome ? 1000 : 0; return n; }
}
