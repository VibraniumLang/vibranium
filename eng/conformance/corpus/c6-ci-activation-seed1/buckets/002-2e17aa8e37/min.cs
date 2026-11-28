WriteLine(0);
public static class C
{
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
}
