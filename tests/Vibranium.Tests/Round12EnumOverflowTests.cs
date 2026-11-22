using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12EnumOverflowTests
{
    public static IEnumerable<object[]> OverflowCases()
    {
        foreach (var (underlying, minimum) in new[]
        {
            ("long", "long.MinValue"),
            ("byte", "0"),
            ("ulong", "0"),
        })
        {
            foreach (string expression in new[] { $"(E){underlying}.MaxValue + 1", $"(E){minimum} - 1" })
            {
                yield return new object[] { underlying, expression, "local" };
                yield return new object[] { underlying, expression, "const" };
                yield return new object[] { underlying, expression, "argument" };
            }
        }
    }

    [Theory]
    [MemberData(nameof(OverflowCases))]
    public Task CheckedEnumOverflow_RejectsWithoutAnalyzerException(string underlying, string expression, string site) =>
        RejectOverflowAsync(underlying, expression, site);

    internal static Task RejectOverflowAsync(string underlying, string expression, string site)
    {
        string source = site switch
        {
            "local" => $"E e = {expression};\nWriteLine(({underlying})e);\n",
            "const" => $"const E e = {expression};\nWriteLine(({underlying})e);\n",
            _ => $"Print({expression});\nstatic void Print(E e) => WriteLine(({underlying})e);\n",
        };
        // The byte cases are diagnosed as out-of-range constant conversions.
        string csId = underlying == "byte" ? "CS0221" : "CS0220";
        return Round12RuleWitness.RejectCSharpAsync(source + $"enum E : {underlying} {{ A }}\n", csId);
    }

    [Theory]
    [InlineData("long", "unchecked((E)long.MaxValue + 1)")]
    [InlineData("long", "E.A")]
    [InlineData("byte", "(E)byte.MaxValue - 1")]
    [InlineData("short", "(E)short.MinValue + 1")]
    [InlineData("uint", "(E)uint.MaxValue - 1")]
    [InlineData("ulong", "(E)ulong.MaxValue - 1")]
    public Task UnsupportedEnumConstant_RejectsWithExactlyVib000(string underlying, string expression) =>
        Round12RuleWitness.RejectAsync($"E e = {expression};\nWriteLine(({underlying})e);\nenum E : {underlying} {{ A }}\n", "VIB000", exact: true);

    public static IEnumerable<object[]> AdditionalEnumUseSites()
    {
        yield return new object[] { "bcl-long-iocontrolcode", """
            System.Net.Sockets.IOControlCode c = System.Net.Sockets.IOControlCode.AsyncIO;
            WriteLine((long)c);
            """ };
        yield return new object[] { "e-sbyte-default", """
            F(); static void F(E e = E.High) => WriteLine((sbyte)e);
            enum E : sbyte { Low = sbyte.MinValue, High = sbyte.MaxValue }
            """ };
        yield return new object[] { "e-sbyte-field", """
            WriteLine((sbyte)C.Field);
            static class C { public const E Field = E.High; }
            enum E : sbyte { Low = sbyte.MinValue, High = sbyte.MaxValue }
            """ };
        yield return new object[] { "e-sbyte-switch", """
            F(E.High); static void F(E e) { switch(e) { case E.High: WriteLine(1); break; default: WriteLine(2); break; } }
            enum E : sbyte { Low = sbyte.MinValue, High = sbyte.MaxValue }
            """ };
        yield return new object[] { "e-ushort-default", """
            F(); static void F(E e = E.High) => WriteLine((ushort)e);
            enum E : ushort { Low = ushort.MinValue, High = ushort.MaxValue }
            """ };
        yield return new object[] { "e-ushort-field", """
            WriteLine((ushort)C.Field);
            static class C { public const E Field = E.High; }
            enum E : ushort { Low = ushort.MinValue, High = ushort.MaxValue }
            """ };
        yield return new object[] { "e-ushort-switch", """
            F(E.High); static void F(E e) { switch(e) { case E.High: WriteLine(1); break; default: WriteLine(2); break; } }
            enum E : ushort { Low = ushort.MinValue, High = ushort.MaxValue }
            """ };
        yield return new object[] { "bcl-byte-jsontokentype", """
            System.Text.Json.JsonTokenType t = System.Text.Json.JsonTokenType.Number;
            WriteLine((byte)t);
            """ };
        yield return new object[] { "bcl-ushort-ilopcode", """
            System.Reflection.Metadata.ILOpCode op = System.Reflection.Metadata.ILOpCode.Ret;
            WriteLine((ushort)op);
            """ };
    }

    [Theory]
    [MemberData(nameof(AdditionalEnumUseSites))]
    public Task UnsupportedEnumUseSites_RejectWithoutAnalyzerException(string name, string source) =>
        Round12RuleWitness.RejectAsync($"// {name}\n{source}", "VIB000", exact: true);

    [Fact]
    public Task BoundaryIntEnumConstants_AreRepresentable() =>
        Round12RuleWitness.AcceptAsync("""
            const E high = (E)int.MaxValue;
            E low = (E)int.MinValue;
            WriteLine((int)high);
            WriteLine((int)low);
            enum E : int { A }
            """, "2147483647\n-2147483648\n");
}
