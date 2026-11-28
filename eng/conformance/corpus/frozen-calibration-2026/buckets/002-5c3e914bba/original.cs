WriteLine(F(4, true));
WriteLine(F(-3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    switch (E.B)
    {
        case E.A when p1: int.TryParse("8", out var o1); acc += o1; break;
        case E.B: return acc;
        case E.C: VibraniumRuntime.Panic("bad state"); break;
        default: var c5 = new C2(acc); acc += c5.Run(); return acc;
    }
    return acc;
}
public enum E { A = 1, B = 3, C = 5 }
public sealed class C2(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        const bool K3 = true;
        if (K3 && (K3 && K3))
        {
            var b4 = (K3 ? 9L : -3L) <= (5L % -1L);
        }
        WriteLine(K3);
        int.TryParse("18", out F);
        return F;
    }
}
