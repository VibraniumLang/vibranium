WriteLine(F(2, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Vm2((acc % p0) & ((int)6L));
    acc += new Yc3(2).A + new Yc3(1, 2).B;
    acc += p0;
    if (acc > 3)
    {
        acc += 6;
    }
    return acc;
}
static int Vm2(int p) { Option<Nd1> o = Some(new Nd1(p)); OptionMut<Nd1> s = o.AsMut(); int n = 0; OptionMut<Nd1> t = s; n += (t.IsSome ? 2 : 0); n += (o.IsSome ? 3 : 0); n += (s.IsSome ? 1 : 0); return n; }
public sealed class Nd1(int v) { public int V = v; }
public sealed class Yc3 { public int A; public int B; public Yc3(int a) { A = a; B = a * 10; } public Yc3(int a, int b) { A = a; B = b; } }
