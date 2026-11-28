var c = new C();
WriteLine(c.A + new C(5).A);
public sealed class C
{
    public int A = 1;
    public C() { A = 3; }
    public C(int a) { A = a; }
}
