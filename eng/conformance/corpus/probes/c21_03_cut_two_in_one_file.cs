WriteLine(new C(1).A + new E(2).A);
public sealed class C
{
    public int A;
    public C(int a) : base() { A = a; }
}
public sealed class E
{
    public int A;
    public E(int a) : base() { A = a * 2; }
}
