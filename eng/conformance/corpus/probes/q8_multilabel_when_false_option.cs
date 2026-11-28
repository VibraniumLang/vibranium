Option<int> o = Some(5);
WriteLine(F(o));
static int F(Option<int> x)
{
    switch (x)
    {
        case NoneType when false:
        case Some<int>(var v): return v;
        default: return 0;
    }
}
