var a = new BoxedInt(42);
var b = a;
WriteLine(b.Value);
return a.Value;

class BoxedInt(int v)
{
    public int Value = v;
}
