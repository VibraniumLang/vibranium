WriteLine(F(-2, true));
WriteLine(F(2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (p0 > 2)
    {
        var b4 = !p1;
    }
    else
    {
        var x5 = acc - acc;
    }
    acc += Snd3(2);
    acc += Snd2(acc);
    return acc;
}
static int Snd2(int p) { int acc = 0; var h = new Hold1(p); ReadOnlySpan<int> s = h.View(); if (p > 1) { var it = h.Items; acc += it.Count; } acc += s[0]; return acc; }
static int Snd3(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan()[0..2]; for (int i = 0; i < p % 3; i++) { v = [p]; } acc += s[0]; return acc; }
public sealed class Hold1(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); [Mut] public void Push(int k) => Items.Add(k); }
