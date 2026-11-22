WriteLine(C.M(3, true, -2L));
WriteLine(C.M(2, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        if (!Try1(-1).IsErr(out var v2, out var er3))
        {
            acc += v2;
            var _ = p0 + v2;
            int x4 = _;
            WriteLine(_);
            {
                int.TryParse("18", out int _);
            }
            WriteLine(_);
            {
                int.TryParse("65", out int _);
            }
            WriteLine(_);
        }
        else VibraniumRuntime.Panic("boom");
        int[] arr = [acc, 2];
        WriteLine(arr[0]);
        acc += p0;
        return acc;
    }
    public static Result<int, int> Try1(int k) => k != 4 ? Ok(k) : Err(2 - k);
}
