WriteLine(Snd2(2));
static int Snd2(int p) { int acc = 0; var h = new Hold1(p); ReadOnlySpan<int> s = h.View(); if (p > 1) { var it = h.Items; acc += it.Count; } acc += s[0]; return acc; }
public sealed class Hold1(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); }
