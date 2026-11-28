WriteLine(F(1L));
WriteLine(F(-1L));
static bool F<T>(T x) => x is > 0 or <= 0;
