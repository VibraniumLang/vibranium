WriteLine(F(4));
static int F<T>(T x) => x is var v ? 1 : 0;
