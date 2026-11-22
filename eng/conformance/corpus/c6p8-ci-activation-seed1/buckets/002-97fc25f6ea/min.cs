WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vm2((acc % p0) & ((int)6L));
    return 0;
}
static int Vm2(int p) { Option<Nd1> o = Some(new Nd1(p)); OptionMut<Nd1> s = o.AsMut(); int n = 0; OptionMut<Nd1> t = s; n += (t.IsSome ? 2 : 0); n += (o.IsSome ? 3 : 0); n += (s.IsSome ? 1 : 0); return n; }
public sealed class Nd1(int v) { public int V = v; }
