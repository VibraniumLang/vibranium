WriteLine(0);
public static class C
{
    public static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
}
public sealed class Gv1(Vec<int> v) { Vec<int> items = v; public ReadOnlySpan<int> Get(ReadOnlySpan<int> other) => other; }
