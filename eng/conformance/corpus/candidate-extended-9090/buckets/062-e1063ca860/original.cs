WriteLine(F(1, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var c6 = new C1((acc | 0) * p0);
    acc += c6.Run();
    return acc;
}
public sealed class C1(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        Vec<int> v3 = [3, 15, 18];
        Span<int> s2 = v3.Slice(0, 3);
        ReadOnlySpan<int> t4_ro0 = s2;
        WriteLine(t4_ro0[0] + t4_ro0.Length);
        s2[0] = 9;
        foreach (ref var t4_e1 in s2) t4_e1 += 2;
        s2[1] += s2.Length;
        WriteLine(s2[1]);
        WriteLine(SumRo5(s2));
        WriteLine(F switch { < 4 => F, >= 4 when true => 6 });
        if (F < 1) VibraniumRuntime.Panic("bad state");
        WriteLine(v3.Count + v3[0]);
        F += 1;
        return F;
    }
    public static int SumRo5(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
