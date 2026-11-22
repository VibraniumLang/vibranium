var s = new S();
WriteLine(s.Get());
struct S { public int Value; [System.Diagnostics.CodeAnalysis.UnscopedRef(1)] public ref int Get() => ref Value; }
