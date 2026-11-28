WriteLine(F());
static int F()
{
    int x = 1;
    ref readonly int r = ref x;
    return M(ref r, 2);
}
static int M(ref readonly int p, int k) => p + k;
