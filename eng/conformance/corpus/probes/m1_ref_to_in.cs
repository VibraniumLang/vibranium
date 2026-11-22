WriteLine(F(1));
static int F(int k)
{
    int x = k;
    return G(ref x);
}
static int G(in int p) => p + 1;
