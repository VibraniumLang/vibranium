WriteLine(Snd2(2));
static int Snd2(int p) { int acc = 0; var h = new Hold1(p); if (p > 1) { var it = h.Items; acc += it.Count; } acc += h.Items.Count; return acc; }
public struct Hold1(int p) { public Vec<int> Items = [p, 2, 3]; }
