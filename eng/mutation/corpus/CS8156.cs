var box=new C();const int value=4;

WriteLine(G(in (value>0?ref value:ref box.X)));
static int G(in int value)=>value+1;

public class C{public int X=5;public ref readonly int RP=>ref X;}