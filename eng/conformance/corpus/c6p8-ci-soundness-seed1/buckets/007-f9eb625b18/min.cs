WriteLine(0);
public static class C
{
    public static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
    public static int Us2(int p) { int n = 0; switch (UsMk1(p)) { case var w when p < 3: n += w.IsSome ? 100 : 200; break; case Some<int>(var v) when p >= 3: n += 50 + v; break; case Some<int>(var v): n += v; break; default: n += 9; break; } return n; }
}
