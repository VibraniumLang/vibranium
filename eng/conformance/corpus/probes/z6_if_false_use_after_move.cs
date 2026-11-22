WriteLine(F(3));
static int F(int x)
{
    if (false)
    {
        Vec<int> v = [1];
        var w = v;
        WriteLine(v.Count);
    }
    return x;
}
