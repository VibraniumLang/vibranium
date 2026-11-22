WriteLine(F(new Box(3)));
static int F<T>(T x) => x is Box b ? b.V : 0;
public sealed class Box(int v) { public int V = v; }
