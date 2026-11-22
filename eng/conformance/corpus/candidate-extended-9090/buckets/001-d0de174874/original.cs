WriteLine(F(-1, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s1 = [4, 15, -5, 5, 18];
    foreach (ref var t2_e0 in s1) t2_e0 += 2;
    Span<int> t2_1 = s1[..^4];
    var c6 = new C3(acc);
    acc += c6.Run();
    foreach (var t2_e1 in t2_1) acc += t2_e1;
    ReadOnlySpan<int> t2_ro2 = s1;
    acc += t2_ro2[0] + t2_ro2.Length;
    s1[0] = 4;
    s1[1] = p0;
    acc += s1[1];
    acc += Over7(s1, s1);
    acc += (p1 ? p0 : 9) % p0;
    return acc;
}
static int Over7(Span<int> a, Span<int> b) => a.Length + b.Length;
public sealed class C3(int seed)
{
    public int F = seed;
    public int Run()
    {
        if (F != 1)
        {
            var x4 = F;
        }
        else
        {
            var x5 = (F > 2 ? -1L : 4L) & 4L;
        }
        return F;
    }
}
