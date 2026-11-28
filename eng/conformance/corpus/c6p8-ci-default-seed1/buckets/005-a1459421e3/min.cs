WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    return acc;
}
public sealed class C1
{
    public static Option<Vec<int>> UsMk2(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
    public static int Us3(int p) { int n = 0; Option<Vec<int>> o = UsMk2(p); n += o switch { not NoneType => 100, _ => 9 }; n += o.IsSome ? 1000 : 0; return n; }
}
