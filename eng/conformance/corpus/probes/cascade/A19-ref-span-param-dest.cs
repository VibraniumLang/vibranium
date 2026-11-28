Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
Set(ref s, b.AsSpan());
WriteLine(s.Length);

static void Set(ref Span<int> d, Span<int> x)
{
    d = x;
    WriteLine(x.Length);
}
