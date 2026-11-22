WriteLine(F(5, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [12, -5, 6, 14];
    Span<int> s1 = v2.Slice(0, 4);
    ReadOnlySpan<int> t3_r0 = s1.Slice(3, 1);
    acc += t3_r0.Length + (t3_r0.IsEmpty ? 0 : t3_r0[^1]);
    foreach (ref var t3_e1 in s1) t3_e1 += 3;
    C4 c5 = new C4(s1.Length % acc);
    acc += c5.Run();
    acc += v2.Count + v2[0];
    return acc;
}
public sealed class C4(int seed)
{
    public int F = seed;
    public int Run()
    {
        WriteLine(9L <= 8L ? -2L - 7L : 5L);
        F += F;
        return F;
    }
}
