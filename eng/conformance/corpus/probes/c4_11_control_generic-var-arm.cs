WriteLine(F(4));
static T F<T>(T x) => x switch { var v => v };
