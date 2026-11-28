WriteLine(F(0, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s1 = [16, 18, 11, 12, 17];
    acc += s1[^1] + s1[^2];
    foreach (ref var t2_e1 in s1) t2_e1 += 2;
    Bump3(s1[..^1], 2);
    acc += s1[0];
    WriteLine(-1);
    var s4 = E.A switch { E.C => acc - p0, E.B => acc, E.A when p1 => p0, _ => acc };
    acc += s4;
    return acc;
}
static void Bump3(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
public enum E { A = 1, B = 4, C = 7 }
