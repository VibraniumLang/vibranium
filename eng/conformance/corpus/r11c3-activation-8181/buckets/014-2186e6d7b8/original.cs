WriteLine(F(6, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += new Gb1<int>.Inner(2).X;
    Result<int, int> u4 = Try2(1);
    if (u4 is Err<int>(var v3))
    {
        acc += v3;
        acc += v3;
    }
    return acc;
}
static Result<int, int> Try2(int k) => k < 1 ? Ok(k) : Err(2 - k);
public sealed class Gb1<T>(T t) where T : unmanaged { public T Val = t; public sealed class Inner(T x) { public T X = x; } }
