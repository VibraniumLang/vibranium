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

public sealed class Round12BaseConstructorCutTests
{
    private const string BaseConstructorMessage = "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance.";
    private const string ChainingMessage = "Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method.";

    public static IEnumerable<object[]> BaseConstructorForms()
    {
        yield return new object[] { "var c=new C(3);WriteLine(c.X);sealed class C{public int X;public C(int x):base(){X=x;}}", false };
        yield return new object[] { "WriteLine(0);class B{public B(int x){}}sealed class C:B{public C(int x):base(x){}}", false };
        yield return new object[] { "WriteLine(0);sealed class C{public C():base(){}public C(int x):base(){}}", false };
        yield return new object[] { "WriteLine(0);sealed class C{public C():this(1){}public C(int x):base(){}}", true };
        yield return new object[] { "WriteLine(0);sealed class C{public C(int x):base(){}public C():this(1){}}", true };
    }

    [Theory]
    [MemberData(nameof(BaseConstructorForms))]
    public async Task BaseCallsUseTheirOwnFirstTokenCutAndPreserveThisChaining(string source, bool hasThisInitializer)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-base-cut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "BaseConstructorCut", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            string[] messages = hasThisInitializer ? new[] { BaseConstructorMessage, ChainingMessage } : new[] { BaseConstructorMessage };
            foreach (string message in messages)
            {
                string token = message == BaseConstructorMessage ? "base" : "this";
                int start = source.IndexOf(token + "(", StringComparison.Ordinal);
                var diagnostic = Assert.Single(stage0, value => value.Message == message);
                Assert.Equal("VIB000", diagnostic.Id);
                Assert.Equal(start, diagnostic.Span.Start);
                Assert.Equal(token.Length, diagnostic.Span.Length);
                var live = Assert.Single(analyzer, value => value.GetMessage() == message);
                Assert.Equal("VIB000", live.Id);
                Assert.Equal(start, live.Location.SourceSpan.Start);
                Assert.Equal(token.Length, live.Location.SourceSpan.Length);
            }
            // C21 follows C9: compare primary cuts, not unrelated secondary errors.
            Assert.Equal(messages.OrderBy(message => message), stage0.Where(diagnostic => diagnostic.Message is BaseConstructorMessage or ChainingMessage).Select(diagnostic => diagnostic.Message).OrderBy(message => message));
            Assert.Equal(messages.OrderBy(message => message), analyzer.Where(diagnostic => diagnostic.GetMessage() is BaseConstructorMessage or ChainingMessage).Select(diagnostic => diagnostic.GetMessage()).OrderBy(message => message));
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task ImplicitBaseConstructionRemainsAccepted()
    {
        const string source = "var c=new C(3);WriteLine(c.X);sealed class C{public int X;public C(int x){X=x;}}";
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-implicit-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "ImplicitBaseConstructor", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
            Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
            Assert.Empty(await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync());
            Assert.True(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
