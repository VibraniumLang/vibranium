struct Pair
{
    public int Left;
    public int Right;
    public Pair(int l, int r) { Left = l; Right = r; }
}

static class DisjointTest
{
    static void AddBoth(ref int a, ref int b)
    {
        a += 10;
        b += 20;
    }

    static byte Main()
    {
        var p = new Pair(1, 2);
        ref var l = ref p.Left;
        ref var r = ref p.Right;
        l += 5;
        r += 7;
        AddBoth(ref p.Left, ref p.Right);
        WriteLine(p.Left);
        WriteLine(p.Right);
        return 0;
    }
}