var h = new H();
long r = ref h.Slot();
r = 7;
WriteLine(h.N);
return 0;
class H { public int N; [Mut] public ref int Slot() => ref N; }
