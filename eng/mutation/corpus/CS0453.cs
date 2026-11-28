var c = new C();
var d = Id(c);
WriteLine(d.N);
return 0;
static T Id<T>(T x) where T : struct => x;
class C { public int N; }
