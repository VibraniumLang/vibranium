WriteLine(0);
public static class C
{
    public static int Snd2(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); for (int i = 0; i < 2; i++) { acc += s.Length; if (i == 0) { var w = v; acc += w.Count; } } return acc; }
}
