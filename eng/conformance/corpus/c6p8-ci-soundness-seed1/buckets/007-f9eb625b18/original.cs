WriteLine(C.M(1, true, -2L));
WriteLine(C.M(7, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        acc += Us2(0) + Us2(3) * 10 + Us2(8) * 100;
        switch (Try3(acc))
        {
            case Ok<int>(var v4) when v4 < 5:
                {
                    ushort x6 = (ushort)(v4);
                    bool b8 = acc is var q7 && q7 > 2;
                    WriteLine(b8);
                    break;
                }
            case Err<int>(var e5):
                {
                    int x9 = p0;
                    acc += x9;
                }
                return 8;
        }
        return acc;
    }
    public static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
    public static int Us2(int p) { int n = 0; switch (UsMk1(p)) { case var w when p < 3: n += w.IsSome ? 100 : 200; break; case Some<int>(var v) when p >= 3: n += 50 + v; break; case Some<int>(var v): n += v; break; default: n += 9; break; } return n; }
    public static Result<int, int> Try3(int k) => k < 0 ? Ok(k) : Err(3 - k);
}
