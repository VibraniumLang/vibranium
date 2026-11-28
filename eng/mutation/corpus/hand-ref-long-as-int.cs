var h = new H();
h.N = 0x1122334455667788;
ref int r = ref h.Slot();
r = 0;
WriteLine(h.N);
return 0;
class H { public long N; [Mut] public ref int Slot() => ref N; }
