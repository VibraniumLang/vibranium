WriteLine(G(-5, 3, -1));
static long G(int a, int n, int d)
{
    long b = a * 3L;
    byte c = (byte)n;
    ulong w = (ulong)b;
    long acc = a / -1 + a % -1 + b / -1L + b % d + (int.MinValue + a) / d;
    acc += (c << n) + (long)(w >> n) + (long)(w << c);
    int k = a;
    k >>= n;
    k <<= 2;
    acc += k + (b >> 70);
    return acc;
}
