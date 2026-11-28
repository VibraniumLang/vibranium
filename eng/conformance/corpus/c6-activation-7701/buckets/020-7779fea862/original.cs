using (var f2 = File.OpenAppend("/dev/stdout").Unwrap()) { if (f2.WriteAll("appended\n").IsErr(out _, out var e1)) WriteLine(e1.Error.KindName()); else WriteLine("ok"); }
WriteLine(C.M(-1, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Vw5((acc - -1) - (p0 | acc));
        int x6 = (acc * p0) << p0;
        acc += new Zc3(1).K;
        return acc;
    }
    public static ReadOnlySpan<int> Sx4(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
    public static int Vw5(int p) { Vec<int> a = [p, 5, 6]; var s = Sx4(a.AsReadOnlySpan()); return s[0] + s.Length; }
}
public sealed class Zc3 { public Vec<int> Items; public int K; public Zc3(int k) { K = Peek(); Items = [k]; } int Peek() => Items.Count; }
