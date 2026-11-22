WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [8, 1, 10, 16, 1];
    Span<int> s1 = v2[0..5];
    foreach (ref var t3_e1 in s1) t3_e1 += 3;
    Bump7(s1, 3);
    return acc;
}
static void Bump7(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
public sealed class C4(int seed)
{
    public int F = seed;
    public int Run()
    {
        int.TryParse("47", out F);
        return 0;
    }
}
