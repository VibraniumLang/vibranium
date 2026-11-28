WriteLine(F());
static int F()
{
    int x = 1;
    return N(in x);
}
static int N(ref readonly int q) => M(ref q, 2);
static int M(ref readonly int p, int k) => p + k;
