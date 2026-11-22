using System.Runtime.CompilerServices;
WriteLine(F());
[MethodImpl(MethodImplOptions.NoInlining && MethodImplOptions.NoOptimization)]
static int F() => 1;
