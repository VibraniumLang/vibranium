WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    uint x1 = 0u;
    switch (x1)
    {
        case 4u:
            static int L2(int a) => 0;
            acc += L2(0);
            return 0;
        case not 4u:
            {
                acc += Snd4(p0);
                acc += Snd5(p1 ? acc % 6 : 4 - p0);
                return 0;
            }
    }
}
static int Snd4(int p) { int acc = 0; var h = new Hold3(p); ReadOnlySpan<int> s = h.View(); if (p > 1) { var it = h.Items; acc += it.Count; } acc += s[0]; return acc; }
static int Snd5(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan()[0..2]; for (int i = 0; i < 2; i++) { s[0] = p; acc += s[0]; if (i == 0) { v = [p]; } } return acc; }
public sealed class Hold3(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); [Mut] public void Push(int k) => Items.Add(k); }
