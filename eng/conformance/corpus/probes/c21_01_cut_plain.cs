var c = new C(2);
WriteLine(c.A);
public sealed class C
{
    public int A;
    public C(int a) : base() { A = a; }
}
