WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Result<int, int> x2 = Ok(0);
    switch (x2)
    {
        case Ok<int>:
            int x5 = 0;
            ref readonly int r7 = ref x5;
            acc += M6(ref r7, 0);
            bool b9 = false;
            WriteLine(b9);
            break;
    }
    return 0;
}
static int M6(ref readonly int p, int k) { return 0; }
