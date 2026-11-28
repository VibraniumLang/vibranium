int x = 1;
WriteLine(Id(ref x));
static int Id(ref readonly int p) => p;
