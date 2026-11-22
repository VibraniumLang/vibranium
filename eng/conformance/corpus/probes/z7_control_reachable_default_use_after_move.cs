WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case < 0: return 1;
        case > 0: return 2;
        default:
            Vec<int> v = [1];
            var w = v;
            return v.Count;
    }
}
