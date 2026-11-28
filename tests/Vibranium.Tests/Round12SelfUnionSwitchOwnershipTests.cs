using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfUnionSwitchOwnershipTests
{
    [Theory]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => MkV(p) switch { Some<Vec<int>>(var v) when p > 5 => 100 + Take(v), NoneType => 7, _ => 9 };\nstatic int Take(Vec<int> v) => v.Count;\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "VIB005")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) { int n = 0; switch (MkV(p)) { case Some<Vec<int>>(var v) when p > 5: n += 100 + Take(v); break; case NoneType: n += 7; break; default: n += 9; break; } return n; }\nstatic int Take(Vec<int> v) => v.Count;\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "VIB005")]
    [InlineData("int k = 3;\nOption<Vec<int>> o = MkV(7);\nint n = o switch { var p when k > 5 => 1, Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "VIB001")]
    [InlineData("int k = 3;\nOption<Vec<int>> o = MkV(7);\nint n = 0;\nswitch (o) { case var p when k > 5: n = 1; break; case Some<Vec<int>>(var v): n = v.Count + v[0]; break; default: n = 0; break; }\nWriteLine(n);\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "VIB001")]
    [InlineData("int k = 3;\nOption<Vec<int>> o = MkV(7);\nint n = o switch { var p when k > 5 => Take(p), Some<Vec<int>>(var v) => v.Count + v[0], _ => 0 };\nWriteLine(n);\nstatic int Take(Option<Vec<int>> x) { if (x.IsNone(out var v)) return 0; return v.Count; }\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "VIB001")]
    public Task RejectsWithTheSameIdSet(string source, string diagnostic) => Round12RuleWitness.RejectAsync(source, diagnostic, exact: true);

    [Theory]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => MkV(p) switch { Some<Vec<int>>(var v) when p > 5 => 100 + v.Count, NoneType => 7, _ => 9 };\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "10297\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) { int n = 0; switch (MkV(p)) { case Some<Vec<int>>(var v) when p > 5: n += 100 + v.Count; break; case NoneType: n += 7; break; default: n += 9; break; } return n; }\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "10297\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => MkV(p) switch { NoneType when p < 2 => 3, Some<Vec<int>>(var v) => Take(v), _ => 9 };\nstatic int Take(Vec<int> v) => v.Count;\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "223\n")]
    [InlineData("int k = 3;\nOption<Vec<int>> o = MkV(7);\nint n = o switch { var p when k > 5 => 1, _ => 0 };\nWriteLine(n);\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "0\n")]
    [InlineData("int k = 3;\nOption<Vec<int>> o = MkV(7);\nint n = o switch { var p => Take(p) };\nWriteLine(n + k);\nstatic int Take(Option<Vec<int>> x) { if (x.IsNone(out var v)) return 0; return v.Count; }\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "5\n")]
    [InlineData("int k = 3;\nOption<int> o = MkI(7);\nint n = o switch { var p when k > 5 => 1, Some<int>(var v) => v, _ => 0 };\nWriteLine(n);\nstatic Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }\n\n", "7\n")]
    [InlineData("int k = 3;\nOption<Vec<int>> o = MkV(7);\nint n = o switch { Some<Vec<int>>(var v) when k > 5 => v.Count, var p => k };\nWriteLine(n);\nstatic Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }\n", "3\n")]
    public Task AcceptedControlsRunTheSameNatively(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);
}
