using System.Runtime.CompilerServices;
WriteLine(F(1));
[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptionsZz.NoOptimization)]
static int F(int x) => x + 1;
