WriteLine(new Zc(3).K);
public sealed class Zc { public Vec<int> Items; public int K; public Zc(int k) { Items = [k]; K = Peek(); } int Peek() => Items.Count; }
