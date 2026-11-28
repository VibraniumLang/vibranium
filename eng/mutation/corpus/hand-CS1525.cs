var a = new S(1, 2); var b = new S(3, 4);
ref S r = ref a;
Bump(ref ref (r = ref b).X);
static void Bump(ref int x) { x += 1; }
public struct S(int x, int y)
{
    public int X = x; 
}
