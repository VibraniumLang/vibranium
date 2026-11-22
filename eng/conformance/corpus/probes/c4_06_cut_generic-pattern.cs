WriteLine(F(3));
WriteLine(F(-3));
static int F<T>(T x) => x switch { > 0 => 1, _ => 0 };
