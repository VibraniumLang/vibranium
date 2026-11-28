WriteLine(F(3));
static int F(int x)
{
    if (x > 100)
    {
        VibraniumRuntime.Panic("big");
        Vec<int> v = [1];
        var w = v;
        return v.Count;
    }
    return x;
}
