WriteLine(F(0, false));
int F(int p0, bool p1)
{
    int acc = 0;
    var r1 = new Res(0);
    acc += Eat2(r1);
    int s7 = Try4(0) switch { Ok<int>(var v5) => 0, Err<int> => 0 };
    return 0;
}
static int Eat2(Res x)
{
    using var g = x;
    return x.Id;
}
static Result<int, int> Try4(int k) => Ok(0);
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
