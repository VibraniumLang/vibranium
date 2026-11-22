WriteLine(new Zc(3).K);
public sealed class Zc { public Vec<int> Items; public int K; public Zc(int k) { K = Peek(); Items = [k]; } int Peek() => Items.Count; }
