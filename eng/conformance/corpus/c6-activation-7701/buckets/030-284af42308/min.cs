WriteLine(0);
public static class C
{
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
}
public sealed class C3(int seed)
{
    public int F = seed;
    public int Run()
    {
        int.TryParse("44", out F);
        return 0;
    }
}
