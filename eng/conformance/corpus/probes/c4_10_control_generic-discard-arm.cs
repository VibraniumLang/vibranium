WriteLine(F(4));
static int F<T>(T x) => x switch { _ => 1 };
