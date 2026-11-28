WriteLine(F(4));
static int F<T>(T x) => x switch { int i => i, _ => 0 };
