var c = new C();
var d = Id(c);
return 0;
static T Id<T>(T x) where T : struct => x;
class C { }
