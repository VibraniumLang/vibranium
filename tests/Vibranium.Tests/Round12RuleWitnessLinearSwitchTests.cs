using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger: witnesses for the SPEC §2 sentence on switches that consume a linear union (finding N20), rule
// R2-lin-union-switch-arms in docs/RULES.md. On a call or Move(o) scrutinee of a linear Option<R> / Result<R, E>, a
// guarded variant arm, a second arm for the same variant, and a `not` or `or` arm are each exactly VIB006 with one
// message on all three frontends; one unguarded arm per variant, `_`, `default`, `var u`, a guarded `_` arm and
// branching inside the arm stay accepted, and each R is disposed exactly once. The reject witnesses need both halves of
// the fix: the Roslyn ban and self-fix W (vib-self). Round12LinearUnionSwitchBanTests holds the full matrix.
public sealed class Round12RuleWitnessLinearSwitchTests
{
    [Theory]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => Open(p) switch { Some<R>(var v) when p > 5 => 100 + Eat(v), Some<R>(var v) => Eat(v), NoneType => 7 };\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10);\nstatic int F(int p)\n{\n    int n = 0;\n    Option<R> o = Open(p);\n    switch (Move(o))\n    {\n        case NoneType when p < 2: n += 3; break;\n        case Some<R>(var v): n += Eat(v); break;\n        default: n += 7; break;\n    }\n    return n;\n}\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10);\nstatic int F(int p)\n{\n    int n = 0;\n    switch (OpenR(p))\n    {\n        case not Ok<R>: n += 3; break;\n        case Ok<R>(var v): n += Eat(v); break;\n    }\n    return n;\n}\nstatic Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10);\nstatic int F(int p)\n{\n    int n = 0;\n    switch (Open(p))\n    {\n        case NoneType or Some<R>: n += 3; break;\n    }\n    return n;\n}\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n")]
    public Task R2_LinUnionSwitchArms_Reject(string source) =>
        Round12RuleWitness.RejectAsync(source, "VIB006", Round12LinearUnionSwitchBanTests.BanMessage, exact: true);

    [Theory]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p)\n{\n    int n = 0;\n    switch (Open(p))\n    {\n        case Some<R>(var v): n += Eat(v); break;\n        case NoneType: n += 7; break;\n    }\n    return n;\n}\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "-4\n-8\n847\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p)\n{\n    int n = 0;\n    switch (Open(p))\n    {\n        case var u: n += EatO(u); break;\n    }\n    return n;\n}\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\nstatic int EatO(Option<R> o) { if (o.IsNone(out var r)) return 0; using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "-4\n-8\n840\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => Open(p) switch { Some<R>(var v) => Eat(v), _ when p < 2 => 3, _ => 7 };\nstatic Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "-4\n-8\n843\n")]
    [InlineData("WriteLine(F(1) + F(4) * 10 + F(8) * 100);\nstatic int F(int p) => OpenR(p) switch { Ok<R>(var v) => p > 5 ? 100 + Eat(v) : Eat(v), Err<int>(var e) => e < 22 ? 1 : e };\nstatic Result<R, int> OpenR(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }\nstatic int Eat(R r) { using var u = r; return u.Id; }\npublic sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n", "-4\n-8\n10841\n")]
    public Task R2_LinUnionSwitchArms_Accept(string source, string expected) => Round12RuleWitness.AcceptAsync(source, expected);
}
