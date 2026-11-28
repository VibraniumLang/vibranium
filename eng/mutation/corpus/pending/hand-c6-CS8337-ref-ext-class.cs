var s = new S();
WriteLine(E.Get(ref s));
static class E { public static int Get(this ref S s) => s.X; }
class S { public int X = 4; }
