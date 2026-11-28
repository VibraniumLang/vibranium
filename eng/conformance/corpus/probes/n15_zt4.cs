WriteLine(new Zc(3).K);
public sealed class Zc { public int K; public Zc(int k) { K = k; K += Peek(); } int Peek() => K * 2; }
