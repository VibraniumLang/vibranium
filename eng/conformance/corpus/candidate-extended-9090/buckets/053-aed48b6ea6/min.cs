WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var q4 = new Ctr3();
    acc += q4.Get();
    acc += M7(ref acc, 0);
    return 0;
}
static int M7(in int p, int k) { return 0; }
public struct Ctr3
{
    public int N;
    public readonly int Get() => N;
}
