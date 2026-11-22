Vec<int> a = [1, 2];
F(ref a);
WriteLine(a.Count);

static void F(ref Vec<int> d)
{
    Span<int> sp = d.AsSpan();
    d = [9];
    WriteLine(sp[0]);
}
