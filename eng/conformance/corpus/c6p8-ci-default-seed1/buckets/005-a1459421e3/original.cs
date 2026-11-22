WriteLine(F(1, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    C1 c5 = new C1();
    acc += c5.Run();
    return acc;
}
public sealed class C1
{
    public int F = 2;
    [Mut] public int Run()
    {
        WriteLine(Us3(0) + Us3(5) * 10 + Us3(6) * 100);
        WriteLine((long)F);
        WriteLine(First4([(byte)2]));
        F += (F != 0 ? -2 : F) & (F - F);
        return F;
    }
    public static Option<Vec<int>> UsMk2(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
    public static int Us3(int p) { int n = 0; Option<Vec<int>> o = UsMk2(p); n += o switch { not NoneType => 100, _ => 9 }; n += o.IsSome ? 1000 : 0; return n; }
    public static int First4(ReadOnlySpan<byte> q) => q.Length > 0 ? (int)q[0] : -1;
}
