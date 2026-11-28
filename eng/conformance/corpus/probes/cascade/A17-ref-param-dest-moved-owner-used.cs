Vec<int> a = [1, 2];
Set(ref a, [3, 4, 5]);
WriteLine(a.Count);

static void Set(ref Vec<int> d, Vec<int> x)
{
    d = x;
    WriteLine(x.Count);
}
