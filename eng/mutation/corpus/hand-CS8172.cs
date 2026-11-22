var h = new H();
ref int r = h.Slot();
class H { public int N; [Mut] public ref int Slot() => ref N; }
