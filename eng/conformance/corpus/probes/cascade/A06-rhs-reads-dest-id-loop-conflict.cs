Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
for (int i = 0; i < 2; i++)
{
    b[0] = i;
    s = Id(s);
}
WriteLine(s[0]);

static Span<int> Id(Span<int> x) => x;
