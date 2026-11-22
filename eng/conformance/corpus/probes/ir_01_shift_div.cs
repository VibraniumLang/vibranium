WriteLine(F(7, 3, 2));
static long F(int a, int n, int d)
{
    long b = a * 9L;
    uint u = (uint)a;
    long acc = (a << n) + (a >> n) + (long)(u >> n) + (b << n) + (b >> n);
    acc += a / d + a % d + b / d + b % d + (long)(u / (uint)d);
    return acc;
}
