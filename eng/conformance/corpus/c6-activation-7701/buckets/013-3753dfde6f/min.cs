WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vw3(p0 * 7);
    acc += Vw5(acc);
    return 0;
}
static OptionRef<Nd1> PickO2(OptionRef<Nd1> x, OptionRef<Nd1> y) => y;
static int Vw3(int p) { var n = new Nd1(p); var m = new Nd1(2); var r = PickO2(n.Next.AsRef(), m.Next.AsRef()); return r.IsSome ? 1 : 0; }
static ReadOnlySpan<int> Sx4(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
static int Vw5(int p) { Vec<int> a = [p, 5, 6]; var s = Sx4(a.AsReadOnlySpan()); return s[0] + s.Length; }
public sealed class Nd1(int v) { public int V = v; public Option<Nd1> Next = None; }
