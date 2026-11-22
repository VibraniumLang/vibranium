WriteLine(F(5, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [9, 3, p0];
    foreach (ref var e1 in v2)
    {
        e1 += 1;
        switch (E.B)
        {
            case E.A when true: break;
            case E.B: int x3 = acc; break;
            default:
                x3 = e1;
                acc += x3;
                double d4 = (double)e1;
                Vec<int> v6 = [9, 7];
                acc += M5(ref v6[1], 2) + (2 != acc ? acc : e1 - 5);
                var x7 = acc;
                break;
        }
        if (p1) continue;
        acc += e1;
    }
    var x8 = p0;
    return acc;
}
static int M5(in int p, int k) { return p * 1; }
public enum E { A = 2, B = 5, C = 7 }
