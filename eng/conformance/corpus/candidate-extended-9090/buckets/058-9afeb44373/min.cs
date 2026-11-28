WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Span<int> v2 = [];
        foreach (ref var e1 in v2)
        {
            var a4 = new Acc3(v2.Length % (e1 % 3));
            var q6 = new Ctr5();
            acc += q6.Get();
        }
        return 0;
    }
}
public sealed class Acc3(int seed)
{
    public int Total = seed;
}
public struct Ctr5
{
    public int N;
    public readonly int Get() => N;
}
