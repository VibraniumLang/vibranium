WriteLine(new D(4).V);
public class B
{
    public int V;
    public B(int v) { V = v; }
}
public sealed class D : B
{
    public D(int v) : base(v + 1) { }
}
