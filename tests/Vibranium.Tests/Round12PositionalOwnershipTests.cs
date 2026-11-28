using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12PositionalOwnershipTests
{
    [Theory]
    [InlineData("result", false, false, "11\n7\n9\n")]
    [InlineData("result", true, false, "11\n7\n9\n")]
    [InlineData("result", false, true, "11\n7\n3\n")]
    [InlineData("result", true, true, "11\n7\n3\n")]
    [InlineData("option", false, false, "11\n7\n7\n")]
    [InlineData("option", true, false, "11\n7\n7\n")]
    [InlineData("option", false, true, "11\n7\n7\n")]
    [InlineData("option", true, true, "11\n7\n7\n")]
    public Task ComplementWithoutRetestTransfersOwnership(string family, bool statement, bool exhaustive, string expected) =>
        Round12RuleWitness.AcceptAsync(Complement(family, statement, exhaustive, retest: false), expected);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialOtherVariantWithLaterRetestRemainsBorrowedOnRoslyn(bool statement)
    {
        // Self accepts this Result retest until the staged ownership follow-up.
        var actual = await Round12RuleWitness.CheckAllAsync(Complement("result", statement, exhaustive: false, retest: true), requireNoWarnings: true);
        Assert.Equal(new[] { "VIB005" }, actual.Analyzer);
        Assert.Equal(new[] { "VIB005" }, actual.Stage0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OptionRetestAfterNoneRemainsBorrowed(bool statement) =>
        Round12RuleWitness.RejectAsync(Complement("option", statement, exhaustive: false, retest: true), "VIB005", exact: true);

    [Theory]
    [InlineData(false, false, "2\n1\n0\n")]
    [InlineData(false, true, "2\n1\n0\n")]
    [InlineData(true, false, "2\n1\n9\n")]
    [InlineData(true, true, "2\n1\n9\n")]
    public Task RefutableBindingWithOnlyDiscardAfterComplementTransfersOwnership(bool result, bool statement, string expected) =>
        Round12RuleWitness.AcceptAsync(Refutable(result, statement, retest: false), expected);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task RefutableBindingBeforeLaterRetestRemainsBorrowed(bool result, bool statement) =>
        Round12RuleWitness.RejectAsync(Refutable(result, statement, retest: true), "VIB005", exact: true);


    public static IEnumerable<object[]> RvalueOwnershipAccepts()
    {
        yield return ["s01-stmt-pair-then-var", """
            WriteLine(F(3)); WriteLine(F(0));
            static int F(int n)
            {
                switch (Mk(n))
                {
                    case Some<Vec<int>>(var v): return Take(v);
                    case NoneType: return 0;
                    case var x: return x.IsSome ? 1 : 2;
                }
            }
            static Option<Vec<int>> Mk(int n) { if (n == 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            """, "2\n0\n", ""];
        yield return ["s03-stmt-result-pair-then-var", """
            WriteLine(F(3)); WriteLine(F(-3));
            static int F(int n)
            {
                switch (MkR(n))
                {
                    case Ok<Vec<int>>(var v): return Take(v);
                    case Err<Vec<int>>: return 0;
                    case var x: return x.IsOk ? 1 : 2;
                }
            }
            static Result<Vec<int>, Vec<int>> MkR(int n) { Vec<int> v = [n, n + 1, n + 2]; if (n < 0) return Err(v); return Ok(v); }
            static int Take(Vec<int> v) => v.Count;
            """, "3\n0\n", ""];
        yield return ["s04-stmt-pair-both-bind-then-var", """
            WriteLine(F(3)); WriteLine(F(-3));
            static int F(int n)
            {
                switch (MkR(n))
                {
                    case Ok<Vec<int>>(var v): return Take(v);
                    case Err<Vec<int>>(var e): return -Take(e);
                    case var x: return x.IsOk ? 1 : 2;
                }
            }
            static Result<Vec<int>, Vec<int>> MkR(int n) { Vec<int> v = [n, n + 1, n + 2]; if (n < 0) return Err(v); return Ok(v); }
            static int Take(Vec<int> v) => v.Count;
            """, "3\n-3\n", ""];
        yield return ["s05-stmt-none-first-then-some-then-var", """
            WriteLine(F(3)); WriteLine(F(0));
            static int F(int n)
            {
                switch (Mk(n))
                {
                    case NoneType: return 0;
                    case Some<Vec<int>>(var v): return Take(v);
                    case var x: return x.IsSome ? 1 : 2;
                }
            }
            static Option<Vec<int>> Mk(int n) { if (n == 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            """, "2\n0\n", ""];
        yield return ["s07-stmt-pair-then-not-none", """
            WriteLine(F(3)); WriteLine(F(0));
            static int F(int n)
            {
                switch (Mk(n))
                {
                    case Some<Vec<int>>(var v): return Take(v);
                    case NoneType: return 0;
                    case not NoneType: return 5;
                }
                return 6;
            }
            static Option<Vec<int>> Mk(int n) { if (n == 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            """, "2\n0\n", "CS0162"];
        yield return ["d08-rvalue-complement-then-retest-stmt", """
            switch (Mk(3))
            {
                case Some<Vec<int>>(var v): WriteLine(Take(v)); break;
                case NoneType: WriteLine(0); break;
                case var x: WriteLine(x.IsSome ? 1 : 2); break;
            }
            static Option<Vec<int>> Mk(int n) { if (n == 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            """, "2\n", ""];
        yield return ["z08-class-payload-owned", """
            WriteLine(F(2)); WriteLine(F(0)); WriteLine(F(9));
            static int F(int n) => MkB(n) switch { Some<Bag>(var b) when b.V.Count > 5 => -1, Some<Bag>(var b2) => TakeB(b2), NoneType => 0 };
            static Option<Bag> MkB(int n) { if (n == 0) return None; Vec<int> v = []; for (int i = 0; i < n; i++) v.Add(i); return Some(new Bag(v)); }
            static int TakeB(Bag b) => b.V.Count;
            public sealed class Bag(Vec<int> v) { public Vec<int> V = v; }
            """, "2\n0\n-1\n", ""];
        yield return ["z09-method-chain-temp", """
            if (new Maker(3).Make() is Some<Vec<int>>(var v)) WriteLine(Take(v));
            WriteLine(new Maker(0).Make() switch { Some<Vec<int>>(var w) => Take(w), NoneType => -1 });
            static int Take(Vec<int> v) => v.Count;
            public sealed class Maker(int n) { public int N = n; public Option<Vec<int>> Make() { if (N <= 0) return None; Vec<int> v = [N]; return Some(v); } }
            """, "1\n-1\n", ""];
    }

    [Theory]
    [MemberData(nameof(RvalueOwnershipAccepts))]
    public async Task RvalueOwnership_AcceptsWithNativeParity(string shape, string source, string expected, string csharpWarning)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected, csharpWarning: csharpWarning.Length == 0 ? null : csharpWarning);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }

    public static IEnumerable<object[]> RvalueMovesRejected()
    {
        yield return ["s02-expr-pair-then-var", """
            WriteLine(F(3)); WriteLine(F(0));
            static int F(int n) => Mk(n) switch { Some<Vec<int>>(var v) => Take(v), NoneType => 0, var x => x.IsSome ? 1 : 2 };
            static Option<Vec<int>> Mk(int n) { if (n == 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            """];
        yield return ["d07-rvalue-complement-then-retest-expr", """
            WriteLine(Mk(3) switch { Some<Vec<int>>(var v) => Take(v), NoneType => 0, var x => x.IsSome ? 1 : 2 });
            static Option<Vec<int>> Mk(int n) { if (n == 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            """];
        yield return ["z03-field-of-temp-class-move", """
            if (MkH(3).O is Some<Vec<int>>(var v)) WriteLine(Take(v));
            static Holder MkH(int n) => new Holder(Mk(n));
            static Option<Vec<int>> Mk(int n) { if (n <= 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            public sealed class Holder(Option<Vec<int>> o) { public Option<Vec<int>> O = o; }
            """];
        yield return ["z04-field-of-temp-struct-move", """
            if (MkS(3).O is Some<Vec<int>>(var v)) WriteLine(Take(v));
            WriteLine(MkS(0).O switch { Some<Vec<int>>(var w) => Take(w), NoneType => -1 });
            static SHolder MkS(int n) => new SHolder(Mk(n));
            static Option<Vec<int>> Mk(int n) { if (n <= 0) return None; Vec<int> v = [n, n + 1]; return Some(v); }
            static int Take(Vec<int> v) => v.Count;
            struct SHolder(Option<Vec<int>> o) { public Option<Vec<int>> O = o; }
            """];
    }

    [Theory]
    [MemberData(nameof(RvalueMovesRejected))]
    public async Task RvalueMove_IsExactlyVib005OnRoslyn(string shape, string source)
    {
        var actual = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        Assert.True(actual.Analyzer is ["VIB005"] && actual.Stage0 is ["VIB005"],
            $"{shape}: Analyzer [{string.Join(",", actual.Analyzer)}], Stage-0 [{string.Join(",", actual.Stage0)}]");
    }

    private static string Complement(string family, bool statement, bool exhaustive, bool retest)
    {
        bool result = family == "result";
        // NoneType has no partial payload. Make the Option binding itself
        // refutable in the retest row so a later Some(None) remains reachable.
        string resultType = result ? "Result<Box, int>" : retest ? "Option<Option<Box>>" : "Option<Box>";
        string first = result ? "Ok<Box>(var v)" : retest ? "Some<Option<Box>>(Some<Box>(var v))" : "Some<Box>(var v)";
        string second = result ? exhaustive ? "Err<int>(var y)" : "Err<int>(7)" : "NoneType";
        string value = result && exhaustive ? "y" : "7";
        string retestArm = !retest ? "" : statement
            ? result ? "case Err<int>(var y): return y; " : "case Some<Option<Box>>(NoneType): return 9; "
            : result ? ", Err<int>(var y) => y" : ", Some<Option<Box>>(NoneType) => 9";
        string classify = statement
            ? $"{{ switch (Make(n)) {{ case {first}: return Read(v); case {second}: return {value}; {retestArm}default: return 9; }} }}"
            : $"=> Make(n) switch {{ {first} => Read(v), {second} => {value}{retestArm}{(exhaustive || retest && !result ? "" : ", _ => 9")} }};";
        string make = result ? "if (n == 0) return Ok(new Box(11)); return Err(n);"
            : !retest ? "if (n == 0) return Some(new Box(11)); return None;"
            : "if (n == 0) { Option<Box> inner = Some(new Box(11)); return Some(inner); } " +
                "if (n == 7) return None; Option<Box> empty = None; return Some(empty);";
        return $$"""
            WriteLine(F(0)); WriteLine(F(7)); WriteLine(F(3));
            static int F(int n) {{classify}}
            static {{resultType}} Make(int n) { {{make}} }
            static int Read(Box value) => value.N;
            public sealed class Box(int n) { public int N = n; }
            """;
    }

    private static string Refutable(bool result, bool statement, bool retest)
    {
        string wrapper = result ? "Ok" : "Some";
        string other = result ? "Err<int>(var e)" : "NoneType";
        string resultType = result ? "Result<Option<Vec<int>>, int>" : "Option<Option<Vec<int>>>";
        string second = result ? "e" : "0";
        string negative = result ? "Err(-p)" : "None";
        string first = $"{wrapper}<Option<Vec<int>>>(Some<Vec<int>>(var v))";
        string remaining = $"{wrapper}<Option<Vec<int>>>(NoneType)";
        string classify = statement
            ? $"{{ switch (Make(p)) {{ case {first}: return Take(v); case {other}: return {second}; " +
                (retest ? $"case {remaining}: return 1; default: return -1; }} }}" : "default: return 1; } }")
            : $"=> Make(p) switch {{ {first} => Take(v), {other} => {second}, {(retest ? remaining : "_")} => 1 }};";
        return $$"""
            WriteLine(F(5)); WriteLine(F(0)); WriteLine(F(-9));
            static int F(int p) {{classify}}
            static {{resultType}} Make(int p)
            {
                if (p < 0) return {{negative}};
                if (p == 0) { Option<Vec<int>> inner = None; return {{wrapper}}(inner); }
                Vec<int> v = [p, p + 1];
                Option<Vec<int>> full = Some(v);
                return {{wrapper}}(full);
            }
            static int Take(Vec<int> v) => v.Count;
            """;
    }
}
