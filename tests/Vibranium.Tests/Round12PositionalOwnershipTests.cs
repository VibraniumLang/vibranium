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
