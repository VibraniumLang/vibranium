Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
if (b.Count > 1)
{
    Span<int> t = s;
    WriteLine(t.Length);
}
WriteLine(b[0]);
WriteLine(s.Length);
