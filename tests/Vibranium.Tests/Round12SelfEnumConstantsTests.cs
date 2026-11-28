using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfEnumConstantsTests
{
    [Theory]
    [InlineData("E e = (E)int.MaxValue + 1;\nWriteLine((int)e);\nenum E { A }\n", "CS0220")]
    [InlineData("B e = (B)255 + 1;\nWriteLine((int)e);\nenum B : byte { A }\n", "CS0221")]
    [InlineData("const E C = (E)int.MaxValue;\nE d = C + 1;\nWriteLine((int)d);\nenum E { A }\n", "CS0220")]
    [InlineData("L e = (L)long.MaxValue + 1;\nWriteLine((long)e);\nenum L : long { A }\n", "CS0220")]
    [InlineData("WriteLine((int)E.A);\nenum E { A = int.MaxValue + 1 }\n", "CS0220")]
    [InlineData("Span<E> values = [E.A];\nvalues[0] = (E)int.MaxValue + 1;\nWriteLine((int)values[0]);\npublic enum E { A = 7 }\n", "CS0220")]
    [InlineData("E e = (E)int.MinValue - 1;\nWriteLine((int)e);\nenum E { A }\n", "CS0220")]
    [InlineData("WriteLine((int)((E)127 + 1)); enum E : sbyte { A }", "CS0221")]
    [InlineData("WriteLine((int)((E)32767 + 1)); enum E : short { A }", "CS0221")]
    [InlineData("WriteLine((int)((E)65535 + 1)); enum E : ushort { A }", "CS0221")]
    [InlineData("WriteLine((int)((E)uint.MaxValue + 1u)); enum E : uint { A }", "CS0220")]
    [InlineData("WriteLine((int)((E)ulong.MaxValue + 1UL)); enum E : ulong { A }", "CS0220")]
    [InlineData("WriteLine((int)C.X); enum E { A } static class C { public const E X = (E)int.MaxValue + 1; }", "CS0220")]
    [InlineData("E e = E.A; switch (e) { case (E)int.MaxValue + 1: WriteLine(1); break; default: break; } enum E { A }", "CS0220")]
    [InlineData("byte x = 0; switch (x) { case 100 + 156: WriteLine(1); break; default: break; }", "CS0031")]
    [InlineData("E e = E.A; WriteLine(e switch { (E)int.MaxValue + 1 => 1, _ => 0 }); enum E { A }", "CS0220")]
    [InlineData("WriteLine((E)int.MaxValue - (E)(-1)); enum E { A }", "CS0220")]
    [InlineData("WriteLine((int)E.B); enum E { A = int.MaxValue, B }", "CS0543")]
    [InlineData("WriteLine(0f - (E)int.MinValue); enum E { A }", "CS0220")]
    [InlineData("const double Z = -0.0; int n = checked(Z - (E)int.MinValue); WriteLine(n); enum E { A }", "CS0220")]
    [InlineData("const int n = 0f - (E)int.MinValue; WriteLine(n); enum E { A }", "CS0220")]
    [InlineData("WriteLine(C.N); enum E { A } static class C { public const int N = 0d - (E)int.MinValue; }", "CS0220")]
    [InlineData("WriteLine(F(0)); static int F(int k) { switch (k) { case 0f - (E)int.MinValue: return 1; default: return 0; } } enum E { A }", "CS0220")]
    [InlineData("WriteLine((int)((E)2 + 0f)); enum E { A }", "CS0019")]
    [InlineData("WriteLine((int)(0d + (E)2)); enum E { A }", "CS0019")]
    [InlineData("WriteLine((int)((E)2 - 1f)); enum E { A }", "CS0019")]
    [InlineData("WriteLine((int)(1d - (E)2)); enum E { A }", "CS0019")]
    public Task RejectsInvalidCSharp(string source, string csharpId) => Round12SelfCSharpValidity.RejectAsync(source, csharpId);

    [Theory]
    [InlineData("E e = unchecked((E)int.MaxValue + 1); WriteLine((int)e); enum E { A }", "-2147483648\n")]
    [InlineData("const E C = (E)42; E d = C + 1; WriteLine((int)d); enum E { A }", "43\n")]
    [InlineData("Span<E> values = [E.A]; values[0] = (E)10 - 1; WriteLine((int)values[0]); enum E { A }", "9\n")]
    [InlineData("int x = 1; E value = (E)int.MaxValue + x; WriteLine((int)value); enum E { A }", "-2147483648\n")]
    [InlineData("WriteLine((int)E.B); enum E { A = int.MaxValue, B = unchecked(A + 1) }", "-2147483648\n")]
    [InlineData("WriteLine(F(E.A)); static int F(E e) => e switch { E.A | E.B => 9, _ => 7 }; enum E { A = 3, B = 2 }", "9\n")]
    [InlineData("WriteLine(F(7)); static int F(int x) { switch (x) { case (1 << 2) | 3: return 7; default: return 0; } }", "7\n")]
    [InlineData("WriteLine(unchecked(0f - (E)int.MinValue)); enum E { A }", "-2147483648\n")]
    [InlineData("const float Z = 1f - 1f; const int n = (E)4 - Z; WriteLine(n); enum E { A }", "4\n")]
    [InlineData("WriteLine(C.N); enum E { A } static class C { private const double Z = -0.0; public const int N = unchecked(Z - (E)int.MinValue); }", "-2147483648\n")]
    [InlineData("WriteLine(F(4)); static int F(int n) { switch (n) { case (E)4 - 0.0: return 11; default: return 0; } } enum E { A }", "11\n")]
    [InlineData("WriteLine((int)(2 + (E)7)); WriteLine((int)((E)7 + 2)); WriteLine((int)(2 - (E)7)); WriteLine((int)((E)7 - 2)); enum E { A }", "9\n9\n-5\n5\n")]
    public Task AcceptedControlsRunTheSameNatively(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);
}
