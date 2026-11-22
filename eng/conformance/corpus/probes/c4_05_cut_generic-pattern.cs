WriteLine(F(1));
WriteLine(F(-1));
static bool F<T>(T x) => x is int and > 0;
