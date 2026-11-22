var c = new C(2);
WriteLine(c.A + c.B);
public sealed class C
{
    public int A;
    public int B;
    public C(int a) : this(a, a * 10) { }
    public C(int a, int b) : base() { A = a; B = b; }
}
