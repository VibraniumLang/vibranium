WriteLine(F(true));
static int F(bool b)
{
    switch (b)
    {
        case true: return 1;
        case false: return 2;
        default:
            Vec<int> v = [1];
            var w = v;
            return v.Count;
    }
}
