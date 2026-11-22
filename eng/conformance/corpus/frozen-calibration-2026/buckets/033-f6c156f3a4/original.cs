WriteLine(F(5, true));
WriteLine(F(1, false));
int F(int p0, bool p1)
{
    int acc = p0;
    var r1 = new Res(3);
    ushort x3 = (ushort)(p0 + 6);
    acc += Eat2(r1);
    int s7 = Try4(p0) switch { Ok<int>(var v5) => v5, Err<int> => -3L > -1L ? p0 ^ 0 : acc };
    acc += s7;
    WriteLine(p1 ? p0 * 5 : s7);
    return acc;
}
static int Eat2(Res x)
{
    using var g = x;
    return x.Id;
}
static Result<int, int> Try4(int k) => k > 3 ? Ok(k) : Err(0 - k);
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
