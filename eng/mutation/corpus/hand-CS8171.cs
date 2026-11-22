var h = new H();
long r = ref h.Slot();
class H { public int N; [Mut] public ref int Slot() => ref N; }
