WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case < 4 when false:
            Vec<int> v = [1];
            var w = v;
            return v.Count;
        default:
            return 0;
    }
}
