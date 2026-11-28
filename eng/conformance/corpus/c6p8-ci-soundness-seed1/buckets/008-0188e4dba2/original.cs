WriteLine(C.M(2, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        {
            bool b4 = p1;
            acc += Us3(2) + Us3(3) * 10 + Us3(6) * 100;
            static int L1(int a)
            {
                WriteLine(a > 4);
                return a;
            }
            acc += L1(7);
        }
        return acc;
    }
    public static Result<Vec<int>, int> UsMk2(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
    public static int Us3(int p) { int n = 0; Result<Vec<int>, int> o = UsMk2(p); switch (o) { case not Ok<Vec<int>> when p != 3: n += 3; break; case not Err<int> when p >= 3: n += 30; break; case Ok<Vec<int>>(var v) when p >= 3: n += 50 + v.Count + v[0]; break; case Ok<Vec<int>>(var v): n += v.Count + v[0]; break; case Err<int>(var e): n += e; break; } n += o.IsOk ? 1000 : 0; return n; }
}
