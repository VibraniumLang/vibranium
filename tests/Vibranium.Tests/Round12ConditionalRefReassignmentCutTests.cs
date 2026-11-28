using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12ConditionalRefReassignmentCutTests
{
    private const string Message = "Conditional and short-circuit expressions in a ref reassignment are not supported in Vibranium v0.2; compute the value in a local first, or use an if statement.";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["c25-with-c24-first-token", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            Option<int> o = Some(3);
            r = /*C25*/ref data[Index(o is Some<int> { Value: 3 })];
            WriteLine(r);
            static int Index(bool b) => b ? 1 : 2;
            """];
        yield return ["c25-is-type-spelling", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            Option<int> o = Some(3);
            r = /*C25*/ref data[Index(o is Some<int>)];
            WriteLine(r);
            static int Index(bool b) => b ? 1 : 2;
            """];
        yield return ["c25-switch-refassign-selector-expr", """
            int a = 4;
            int b = 7;
            bool c = a < b;
            int old = 0;
            ref int x = ref old;
            WriteLine((x = /*C25*/ref (c ? ref a : ref b)) switch { 4 when c => 1, _ => 2 });
            WriteLine(x);
            """];
        yield return ["c25-switch-refassign-selector-stmt", """
            int a = 4;
            int b = 7;
            bool c = a < b;
            int old = 0;
            ref int x = ref old;
            switch ((x = /*C25*/ref (c ? ref a : ref b)))
            {
                case 4 when c: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            WriteLine(x);
            """];
        yield return ["c25-and-depth", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            int k = 1;
            bool flag = k > 0;
            r = /*C25*/ref data[Id(Index(flag && k > 0))];
            WriteLine(r);
            static int Id(int x) => x;
            static int Index(bool b) => b ? 1 : 2;
            """];
        yield return ["c25-or-nested", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            int k = 1;
            bool flag = k > 0;
            WriteLine((r = /*C25*/ref data[Id(Index(flag || k == 1))]));
            WriteLine(r);
            static int Id(int x) => x;
            static int Index(bool b) => b ? 1 : 2;
            """];
        yield return ["c25-conditional-constant", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            r = /*C25*/ref data[true ? 1 : 2];
            WriteLine(r);
            """];
        yield return ["c25-switch-inside-index", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            int k = 1;
            bool flag = k > 0;
            r = /*C25*/ref data[Id(k switch { 1 => 1, _ => 2 })];
            WriteLine(r);
            static int Id(int x) => x;
            WriteLine(flag);
            """];
        yield return ["c25-is-in-nested-call", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            int k = 1;
            bool flag = k > 0;
            r = /*C25*/ref data[Id(Index(k is 1))];
            WriteLine(r);
            WriteLine(flag);
            static int Id(int x) => x;
            static int Index(bool b) => b ? 1 : 2;
            """];
        yield return ["c25-one-first-source", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            int k = 1;
            bool flag = k > 0;
            r = /*C25*/ref data[(r = ref data[flag ? 0 : 1]) > 0 ? 1 : 2];
            r = ref data[Index(flag || k > 0)];
            WriteLine(r);
            static int Index(bool b) => b ? 1 : 2;
            """];
        yield return ["c25-first-uninstantiated", """
            WriteLine(C.F(false));
            static class C
            {
            static int Unused<T>(bool flag)
            {
                Span<int> data = [4, 7, 9];
                ref int r = ref data[0];
                r = /*C25*/ref data[flag ? 1 : 2];
                return r;
            }
            public static int F(bool flag)
            {
                Span<int> data = [4, 7, 9];
                ref int r = ref data[0];
                r = ref data[flag ? 1 : 2];
                return r;
            }
            }
            """];
        yield return ["n53-call-conditional-not-taken", """
            WriteLine(F(false));
            static int F(bool flag)
            {
                Span<int> s = [4, 7];
                ref int sink = ref s[0];
                sink = /*C25*/ref Pick(flag ? sink : 0, s);
                return sink;
            }
            static ref int Pick(int i, Span<int> data) => ref data[i & 1];
            """];
        yield return ["n53-call-short-circuit-not-evaluated", """
            WriteLine(F(false));
            static int F(bool flag)
            {
                Span<int> s = [4, 7];
                ref int sink = ref s[0];
                sink = /*C25*/ref Pick(flag && sink > 0, s);
                return sink;
            }
            static ref int Pick(bool second, Span<int> data) => ref data[(second ? 1 : 0)];
            """];
        yield return ["n53-span-conditional-index", """
            WriteLine(F(true) + F(false));
            static int F(bool flag)
            {
                Span<int> data = [1, 4, 7];
                ref int old = ref data[0];
                old = /*C25*/ref data[flag ? old : 2];
                return old;
            }
            """];
        yield return ["n53-span-conditional-index-unrelated", """
            WriteLine(F(true) + F(false));
            static int F(bool flag)
            {
                Span<int> data = [1, 4, 7];
                ref int old = ref data[0];
                old = /*C25*/ref data[flag ? 1 : 2];
                return old;
            }
            """];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FirstCutMatchesBetweenAnalyzerAndStage0(string name, string markedSource)
    {
        const string marker = "/*C25*/";
        int start = markedSource.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        string source = markedSource.Remove(start, marker.Length);
        Assert.Equal("ref", source.Substring(start, 3));
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-c25-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "ConditionalRefReassignment", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var cut = Assert.Single(stage0, d => d.Message == Message);
            var live = Assert.Single(analyzer, d => d.GetMessage() == Message);
            Assert.Equal("VIB000", cut.Id);
            Assert.Equal(cut.Id, live.Id);
            Assert.Equal(start, cut.Span.Start);
            Assert.Equal(3, cut.Span.Length);
            Assert.Equal(start, live.Location.SourceSpan.Start);
            Assert.Equal(3, live.Location.SourceSpan.Length);
            if (name == "c25-with-c24-first-token")
            {
                const string propertyMessage = "Property patterns on Option and Result cases are not supported in Vibranium v0.2; use the positional form, such as Some<T>(var x).";
                int brace = source.IndexOf("{ Value:", StringComparison.Ordinal);
                Assert.Equal(2, stage0.Count);
                Assert.Equal(2, analyzer.Length);
                var c24 = Assert.Single(stage0, d => d.Message == propertyMessage);
                var liveC24 = Assert.Single(analyzer, d => d.GetMessage() == propertyMessage);
                Assert.Equal("VIB000", c24.Id);
                Assert.Equal(c24.Id, liveC24.Id);
                Assert.Equal(brace, c24.Span.Start);
                Assert.Equal(1, c24.Span.Length);
                Assert.Equal(brace, liveC24.Location.SourceSpan.Start);
                Assert.Equal(1, liveC24.Location.SourceSpan.Length);
            }
            var position = live.Location.GetLineSpan().StartLinePosition;
            Assert.Equal(position.Line + 1, cut.Span.Line);
            Assert.Equal(position.Character + 1, cut.Span.Column);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
