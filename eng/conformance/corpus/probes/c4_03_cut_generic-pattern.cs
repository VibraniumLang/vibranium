WriteLine(F(1));
WriteLine(F(1L));
static bool F<T>(T x) => x is int;
