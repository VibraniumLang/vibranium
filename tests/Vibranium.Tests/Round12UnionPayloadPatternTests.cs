using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12UnionPayloadPatternTests
{
    private const string PropertyMessage = "Property patterns on Option and Result cases are not supported in Vibranium v0.2; use the positional form, such as Some<T>(var x).";
    private const string RelationalMessage = "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition.";

    private static string Match(string form, string pattern, string value = "o", string result = "1") => form switch
    {
        "is" => $"WriteLine({value} is {pattern} ? {result} : 0);",
        "statement" => $"switch ({value}) {{ case {pattern}: WriteLine({result}); break; default: WriteLine(0); break; }}",
        "expression" => $"WriteLine({value} switch {{ {pattern} => {result}, _ => 0 }});",
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    public static IEnumerable<object[]> PropertyCases()
    {
        foreach (string form in new[] { "is", "statement", "expression" })
        {
            yield return new object[] { "Option<int> o = Some(7);\n" + Match(form, "Some<int> /*brace*/{ Value: 7 }") };
            yield return new object[] { "Option<int> o = None;\n" + Match(form, "NoneType /*brace*/{ }") };
            yield return new object[] { "Result<int, int> o = Ok(7);\n" + Match(form, "Ok<int>(7) /*brace*/{ }") };
            yield return new object[] { "Result<int, int> o = Err(7);\n" + Match(form, "Err<int>(7) /*brace*/{ Error: > 2 }") };
            yield return new object[] { "Option<Option<int>> o = Some<Option<int>>(Some(7));\n" +
                Match(form, "Some<Option<int>>(Some<int> /*brace*/{ Value: 7 }) { }") };
        }
        yield return new object[] { "using S = Vibranium.Some<int>;\nOption<int> o = Some(7);\n" + Match("is", "(S /*brace*/{ Value: 7 })") };
        yield return new object[] { "using N = Vibranium.NoneType;\nOption<int> o = None;\n" + Match("statement", "N /*brace*/{ }") };
        yield return new object[] { "using K = Vibranium.Ok<int>;\nOption<Result<int, int>> o = Some<Result<int, int>>(Ok(7));\n" +
            Match("expression", "Some<Result<int, int>>((K(7) /*brace*/{ }))") };
    }

    [Theory]
    [MemberData(nameof(PropertyCases))]
    public async Task C24_RejectsAtFirstCaseBraceInEachFile(string source)
    {
        // Later clauses must not produce another C24 in the same file. A
        // nested brace in the positional part also precedes its outer clause.
        const string second = "static class Other { public static bool F(Option<int> o) => o is NoneType /*brace*/{ }; }";
        source += "\nOption<int> other = Some(7);\nWriteLine(other is Some<int> { });";
        await WithCompilationAsync(source, async compilation =>
        {
            var options = compilation.SyntaxTrees.First(tree => Path.GetFileName(tree.FilePath) == "Program.cs").Options;
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(second,
                (CSharpParseOptions)options, "Other.cs"));
            AssertNoCSharpWarnings(compilation);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await AnalyzeAsync(compilation);
            var cli = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(cli.Success);
            // The Err row also contains C20; it must survive the C24 cut.
            bool hasRelational = source.Contains("Error: > 2", StringComparison.Ordinal);
            Assert.Equal(hasRelational ? 3 : 2, stage0.Count);
            Assert.Equal(stage0.Count, analyzer.Length);
            Assert.Equal(stage0.Count, cli.Errors.Count);
            if (hasRelational)
            {
                var relational = Assert.Single(stage0, d => d.Message == RelationalMessage);
                Assert.Equal(source.IndexOf("> 2", StringComparison.Ordinal), relational.Span.Start);
                Assert.Contains(relational.ToString(), cli.Errors);
                Assert.Equal(relational.Span.Start, Assert.Single(analyzer, d => d.GetMessage() == RelationalMessage).Location.SourceSpan.Start);
            }
            foreach (var (file, text) in new[] { ("Program.cs", source), ("Other.cs", second) })
            {
                int brace = text.IndexOf("/*brace*/{", StringComparison.Ordinal) + "/*brace*/".Length;
                var front = Assert.Single(stage0, d => d.Message == PropertyMessage && Path.GetFileName(d.Span.FilePath) == file);
                Assert.Equal("VIB000", front.Id);
                Assert.Equal(PropertyMessage, front.Message);
                Assert.Equal(brace, front.Span.Start);
                Assert.Equal(1, front.Span.Length);
                Assert.Contains(front.ToString(), cli.Errors);
                var live = Assert.Single(analyzer, d => d.GetMessage() == PropertyMessage && Path.GetFileName(d.Location.SourceTree?.FilePath) == file);
                Assert.Equal("VIB000", live.Id);
                Assert.Equal(PropertyMessage, live.GetMessage());
                Assert.Equal(new Microsoft.CodeAnalysis.Text.TextSpan(brace, 1), live.Location.SourceSpan);
            }
        });
    }

    [Fact]
    public Task RejectedNegatedCaseCannotReachContinuationThatUsesItsBindings() => AssertRoslynRejectsAsync("""
        WriteLine(F(0));
        WriteLine(F(5));
        static int F(int p)
        {
            if (Make(p) is not Some<Vec<int>>(var v) { Value: _ }) return 9;
            return Take(v);
        }
        static Option<Vec<int>> Make(int p) { if (p > 2) { Vec<int> v = [p, p + 1]; return Some(v); } return None; }
        static int Take(Vec<int> v) => v.Count;
        """, "VIB000", PropertyMessage);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task C20AndC24EachKeepTheirOwnAnchor(bool propertyFirst)
    {
        const string init = "Option<int> o = Some(7);\n";
        const string relation = "WriteLine(o is Some<int>(/*relation*/> 3) ? 1 : 0);\n";
        const string property = "WriteLine(o is Some<int> /*brace*/{ } ? 1 : 0);\n";
        string source = init + (propertyFirst ? property + relation : relation + property);
        return WithCompilationAsync(source, async compilation =>
        {
            AssertNoCSharpWarnings(compilation);
            var analyzer = await AnalyzeAsync(compilation);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var cli = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(cli.Success);
            Assert.Equal(2, analyzer.Length);
            Assert.Equal(2, stage0.Count);
            Assert.Equal(2, cli.Errors.Count);
            foreach (var (message, marker) in new[] { (PropertyMessage, "/*brace*/"), (RelationalMessage, "/*relation*/") })
            {
                int index = source.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                var diag = Assert.Single(stage0, d => d.Message == message);
                Assert.Equal("VIB000", diag.Id);
                Assert.Equal(index, diag.Span.Start);
                Assert.Equal(1, diag.Span.Length);
                Assert.Contains(diag.ToString(), cli.Errors);
                var live = Assert.Single(analyzer, d => d.GetMessage() == message);
                Assert.Equal("VIB000", live.Id);
                Assert.Equal(new Microsoft.CodeAnalysis.Text.TextSpan(index, 1), live.Location.SourceSpan);
            }
        });
    }

    [Theory]
    [InlineData("null-before", "VIB008")]
    [InlineData("null-inside", "VIB008")]
    [InlineData("move-before", "VIB001")]
    public async Task NamedPositionalCutDoesNotMaskOtherDiagnostics(string shape, string otherId)
    {
        const string declaration = "public sealed class C(int id) { public readonly int Id = id; }";
        string source = shape switch
        {
            "null-before" => "Option<C> a = Some(new C(1)); WriteLine(a is Some<C>(not null));\n" +
                "Option<int> b = Some(7); WriteLine(b is Some<int>(value: 7));\n" + declaration,
            "null-inside" => "Option<C> a = Some(new C(1)); WriteLine(a is Some<C>(value: not null));\n" + declaration,
            "move-before" => "Vec<int> v = [1, 2]; Vec<int> w = v; WriteLine(v.Count + w.Count);\n" +
                "Option<int> b = Some(7); WriteLine(b is Some<int>(value: 7));",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var actual = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        string[] expected = ["VIB000", otherId];
        Assert.Equal(expected, actual.Analyzer);
        Assert.Equal(expected, actual.Stage0);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("statement")]
    [InlineData("expression")]
    public Task TypedAndNestedPositionalPayloadsRemainValid(string form)
    {
        const string init = "Option<int> s = Some(7);\nResult<int, int> k = Ok(8);\nResult<int, int> e = Err(9);\nOption<Option<int>> n = Some<Option<int>>(Some(5));\n";
        string Body(string pattern, string value) => "\n{\n" + Match(form, pattern, value, result: "x") + "\n}\n";
        return Round12RuleWitness.AcceptAsync(init + Body("Some<int>(int x)", "s") + Body("Ok<int>(int x)", "k") +
            Body("Err<int>(int x)", "e") + Body("Some<Option<int>>(Some<int>(var x))", "n"), "7\n8\n9\n5\n");
    }

    [Fact]
    public Task QualifiedAndParenthesizedPositionalTestRemainsValid() => Round12RuleWitness.AcceptAsync("""
        WriteLine(Fixtures.Classify(1));
        WriteLine(Fixtures.Classify(42));
        static class Fixtures
        {
            public static int Classify(int p) => (Make(p) is (Vibranium.Some<int>(42))) ? 42 : 0;
            private static Option<int> Make(int p) => Some(p);
        }
        """, "0\n42\n");

    [Fact]
    public Task AliasedPositionalCaseRemainsValidOnRoslyn() => WithCompilationAsync("""
        using Pick = Vibranium.Some<int>;
        Option<int> o = Some(42);
        WriteLine((o is (Pick(var n))) ? n : 0);
        """, AssertRoslynAcceptsAsync);

    [Theory]
    [InlineData("is")]
    [InlineData("statement")]
    [InlineData("expression")]
    public async Task NamedPositionalElement_Rejects(string form)
    {
        const string message = "Named positional elements in union-case patterns are not supported in Vibranium v0.2; drop the element name.";
        string source = "Option<int> o = Some(7);\n" + Match(form, "Some<int>(value: 7)");
        await AssertRoslynRejectsAsync(source, "VIB000", message);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("statement")]
    [InlineData("expression")]
    public Task ExtensionDeconstruction_RejectsWithoutAnUninitializedBindingCascade(string form) => AssertRoslynRejectsAsync(
        "Option<int> o = Some(7);\n" + Match(form, "Some<int>(var a, var b)", result: "a + b") + "\n" + """
        static class Extensions
        {
            public static void Deconstruct(this Some<int> s, out int a, out int b) { a = 1; b = 2; }
        }
        """, "VIB000", "Custom union-case deconstruction is not supported in Vibranium v0.2; use the wrapper's single positional payload.");

    [Fact]
    public Task NoneExtensionDeconstruction_RejectsWithoutCascade() => AssertRoslynRejectsAsync("""
        Option<int> o = None;
        if (o is NoneType(var a)) WriteLine(a);
        static class Extensions
        {
            public static void Deconstruct(this NoneType n, out int a) { a = 7; }
        }
        """, "VIB000", "Payload subpatterns on NoneType are not supported in Vibranium v0.2; use NoneType without a payload subpattern.");

    [Theory]
    [InlineData("is")]
    [InlineData("statement")]
    [InlineData("expression")]
    public Task NotNullPayload_RejectsOnlyForNullBan(string form) => Round12RuleWitness.RejectAsync(
        "Option<C> o = Some(new C(7));\n" + Match(form, "Some<C>(not null)") + "\npublic sealed class C(int id) { public readonly int Id = id; }",
        "VIB008", exact: true);

    public static IEnumerable<object[]> NullValueCases()
    {
        foreach (string form in new[] { "is", "statement", "expression" })
        {
            yield return new object[] { form, "not C.Empty", false };
            yield return new object[] { form, "not empty", true };
            yield return new object[] { form, "null", false };
        }
    }

    [Theory]
    [MemberData(nameof(NullValueCases))]
    public Task NullValuedPayloadPatternRejectsOnlyForNullBan(string form, string payload, bool local) => Round12RuleWitness.RejectAsync(
        (local ? "const C? empty = null;\n" : "") + "Option<C> o = Some(new C(7));\n" + Match(form, $"Some<C>({payload})") +
        (payload.Contains("C.Empty", StringComparison.Ordinal)
            ? "\npublic sealed class C(int id) { public const C? Empty = null; public readonly int Id = id; }"
            : "\npublic sealed class C(int id) { public readonly int Id = id; }"),
        "VIB008", exact: true);

    [Theory]
    [InlineData("is")]
    [InlineData("statement")]
    [InlineData("expression")]
    public async Task StringNullConstantPayloadPatternIsConformant(string form)
    {
        // Conformant CONFORMANCE §7.1 row: Roslyn reports VIB000 alongside VIB008; self reports VIB008.
        var actual = await Round12RuleWitness.CheckAllAsync(
            "const string? N = null;\nOption<string> o = Some(\"a\");\n" + Match(form, "Some<string>(not N)"),
            requireNoWarnings: true);
        Assert.Equal(new[] { "VIB000", "VIB008" }, actual.Analyzer);
        Assert.Equal(new[] { "VIB000", "VIB008" }, actual.Stage0);
        Assert.Equal(new[] { "VIB008" }, actual.Self);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("statement")]
    [InlineData("expression")]
    public Task DefaultLiteralPayloadPreservesCSharpSourceError(string form) => Round12RuleWitness.RejectCSharpAsync(
        "Option<C> o = Some(new C(7));\n" + Match(form, "Some<C>(not default)") +
        "\npublic sealed class C(int id) { public readonly int Id = id; }", "CS8505");

    [Theory]
    [InlineData("is", "int", "7")]
    [InlineData("statement", "int", "7")]
    [InlineData("expression", "int", "7")]
    [InlineData("is", "C", "new C(7)")]
    public async Task WideningDeclarationHasNoBindingSuccessEdge(string form, string type, string payload)
    {
        string source = $"Option<{type}> o = Some({payload});\n" + Match(form, $"Some<{type}>(object x)", result: "(x is var _ ? 1 : 2)") +
            "\n" + (type == "C" ? "public sealed class C(int id) { public readonly int Id = id; }" : "");
        var actual = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        Assert.Equal(new[] { "VIB000" }, actual.Analyzer);
        Assert.Equal(new[] { "VIB000" }, actual.Stage0);
        // Self has a separate VIB005 cascade when widening a borrowed class.
        if (type == "int") Assert.Equal(new[] { "VIB000" }, actual.Self);
    }

    [Theory]
    [InlineData("is", "{ Count: 2 } v")]
    [InlineData("statement", "{ Count: 2 }")]
    public Task StructuralPositionalPayload_RemainsUnsupported(string form, string pattern) => AssertRoslynRejectsAsync(
        "Vec<int> value = [1, 2]; Option<Vec<int>> o = Some(value);\n" + Match(form, $"Some<Vec<int>>({pattern})"),
        "VIB000", "Structural payload patterns are not supported in Vibranium v0.2; bind the payload and test its members in a when guard or an if condition.");

    [Theory]
    [InlineData("System.Int32")]
    [InlineData("Int32")]
    public Task ExplicitInt32EnumBaseRemainsValidOnRoslyn(string baseType) => WithCompilationAsync(
        $"WriteLine((int)E.Max); WriteLine((int)E.Min);\nenum E : {baseType} {{ Max = int.MaxValue, Min = int.MinValue }}", AssertRoslynAcceptsAsync);

    private static Task AssertRoslynRejectsAsync(string source, string id, string message) => WithCompilationAsync(source, async compilation =>
    {
        AssertNoCSharpWarnings(compilation);
        var analyzer = Assert.Single(await AnalyzeAsync(compilation));
        Assert.Equal(id, analyzer.Id);
        Assert.Equal(message, analyzer.GetMessage());
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
        Assert.False(stage0.Success);
        Assert.EndsWith($": error {id}: {message}", Assert.Single(stage0.Errors), StringComparison.Ordinal);
    });

    private static async Task AssertRoslynAcceptsAsync(CSharpCompilation compilation)
    {
        AssertNoCSharpWarnings(compilation);
        Assert.Empty(await AnalyzeAsync(compilation));
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
        Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
    }

    private static void AssertNoCSharpWarnings(Compilation compilation)
    {
        var problems = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Empty(problems);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Compilation compilation) => compilation.WithAnalyzers(
        ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();

    private static async Task WithCompilationAsync(string source, Func<CSharpCompilation, Task> check)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vibranium-r12-union-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "Program.cs");
            File.WriteAllText(path, source);
            await check(VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12UnionPayloadPattern", includeStd: true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
