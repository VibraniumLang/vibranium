WriteLine(new Zc(3).N);
public sealed class Zc { public Vec<int> Items; public Zc(int k) { Items = [k]; int t = Peek(); WriteLine(t); } public int N => Items.Count; int Peek() => Items.Count; }
