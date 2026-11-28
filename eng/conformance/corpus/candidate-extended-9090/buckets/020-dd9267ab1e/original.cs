WriteLine(F(5, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [8, 1, 10, 16, 1];
    Span<int> s1 = v2[0..5];
    foreach (ref var t3_e0 in s1) t3_e0 += 1;
    foreach (ref var t3_e1 in s1) t3_e1 += 3;
    ReadOnlySpan<int> t3_2 = s1[3..^1];
    var c6 = new C4(p0);
    acc += c6.Run();
    acc += t3_2.Length;
    Bump7(s1, 3);
    acc += s1[0];
    acc += v2.Count + v2[0];
    return acc;
}
static void Bump7(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
public sealed class C4(int seed)
{
    public int F = seed;
    public int Run()
    {
        bool b5 = ((F < 1) || (F < 1)) || ((F < 1) && (F != 4));
        int.TryParse("47", out F);
        return F;
    }
}
