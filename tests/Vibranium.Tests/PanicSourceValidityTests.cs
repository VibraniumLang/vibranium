using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class PanicSourceValidityTests
{
    private static string RepoRoot
    {
        get
        {
            string path = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(path, "Vibranium.slnx"))) path = Directory.GetParent(path)!.FullName;
            return path;
        }
    }

    private static string SelfCompiler
    {
        get
        {
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            return Path.Combine(RepoRoot, "src", "vib-self", "bin", configuration, "net11.0", "vib-self.dll");
        }
    }

    private static string DotnetExe
    {
        get
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
            return File.Exists(local) ? local : "dotnet";
        }
    }

    [Theory]
    [InlineData("local-tail", "WriteLine(F()); static int F(){VibraniumRuntime.Panic(\"stop\");int x;return x;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("join", "WriteLine(F(true)); static int F(bool c){int x;if(c)x=1;else VibraniumRuntime.Panic(\"stop\");return x;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("out-exit", "F(out int x);WriteLine(x);static void F(out int x){VibraniumRuntime.Panic(\"stop\");}", "CS0177", "out parameter", "VIB001")]
    [InlineData("out-join", "F(true,out int x);WriteLine(x);static void F(bool c,out int x){if(c)x=1;else VibraniumRuntime.Panic(\"stop\");}", "CS0177", "out parameter", "VIB001")]
    [InlineData("field-tail", "WriteLine(F());static int F(){VibraniumRuntime.Panic(\"stop\");Pair p;p.X=1;return p.Y;}public struct Pair{public int X;public int Y;}", "CS0170", "definitely assigned", "VIB001")]
    [InlineData("partial-struct", "WriteLine(F().X);static Pair F(){VibraniumRuntime.Panic(\"stop\");Pair p;p.X=1;return p;}public struct Pair{public int X;public int Y;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("call-argument-order", "F();static void F(){VibraniumRuntime.Panic(\"stop\");int x;G(out x,x);}static void G(out int x,int y){x=y;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("ref-local", "WriteLine(F());static ref int F(){VibraniumRuntime.Panic(\"stop\");int x=1;return ref x;}", "CS8168", "ref-safe-context", "VIB007")]
    [InlineData("ref-alias", "WriteLine(F());static ref int F(){VibraniumRuntime.Panic(\"stop\");int x=1;ref int r=ref x;return ref r;}", "CS8157", "ref-safe-context", "VIB007")]
    [InlineData("ref-struct-parameter", "var p=new Pair(1);WriteLine(F(p));static ref int F(Pair p){VibraniumRuntime.Panic(\"stop\");return ref p.X;}public struct Pair{public int X;public Pair(int x){X=x;}}", "CS8167", "ref-safe-context", "VIB007")]
    [InlineData("ref-out-parameter", "F(out int x);static ref int F(out int x){VibraniumRuntime.Panic(\"stop\");x=1;return ref x;}", "CS9075", "ref-safe-context", "VIB007")]
    [InlineData("ref-scoped-parameter", "int x=1;WriteLine(F(ref x));static ref int F(scoped ref int x){VibraniumRuntime.Panic(\"stop\");return ref x;}", "CS9075", "ref-safe-context", "VIB007")]
    [InlineData("ref-reassignment", "int x=1;WriteLine(F(ref x));static ref int F(ref int p){ref int r=ref p;VibraniumRuntime.Panic(\"stop\");int y=1;r=ref y;return ref r;}", "CS8374", "ref reassignment", "VIB007")]
    [InlineData("ref-unused-reassignment", "int x=1;F(ref x);static void F(ref int p){ref int r=ref p;VibraniumRuntime.Panic(\"stop\");{int y=1;r=ref y;}}", "CS8374", "ref reassignment", "VIB007")]
    [InlineData("ref-call", "WriteLine(F());static ref int F(){VibraniumRuntime.Panic(\"stop\");int x=1;return ref Identity(ref x);}static ref int Identity(ref int x)=>ref x;", "CS8168", "ref-safe-context", "VIB007")]
    [InlineData("call-ignores-vibranium-lifetime", "int x=1;WriteLine(F(ref x));static ref int F(ref int p){VibraniumRuntime.Panic(\"stop\");int y=2;return ref Pick(ref p,ref y);}[return:LifetimeOf(\"p\")]static ref int Pick(ref int p,ref int q)=>ref p;", "CS8168", "ref-safe-context", "VIB007")]
    [InlineData("view-scoped-parameter", "ReadOnlySpan<int> s=[1];WriteLine(F(s)[0]);static ReadOnlySpan<int> F(scoped ReadOnlySpan<int> s){VibraniumRuntime.Panic(\"stop\");return s;}", "CS8352", "safe-context", "VIB007")]
    [InlineData("view-local-vector", "WriteLine(F()[0]);static ReadOnlySpan<int> F(){VibraniumRuntime.Panic(\"stop\");var v=new Vec<int>();v.Add(1);return v.AsReadOnlySpan();}", "CS8168", "safe-context", "VIB007")]
    [InlineData("view-pattern-return", "WriteLine(F(1)[0]);static ReadOnlySpan<int> F(int x){VibraniumRuntime.Panic(\"stop\");ReadOnlySpan<int> s=[x];if(s is var p)return p;return [];}", "CS8352", "safe-context", "VIB007")]
    [InlineData("dynamic-collection-return", "WriteLine(F(1)[0]);static ReadOnlySpan<int> F(int x){VibraniumRuntime.Panic(\"stop\");return [x];}", "CS9203", "safe-context", "VIB007")]
    [InlineData("collection-narrow-assignment", "F(1);static void F(int x){VibraniumRuntime.Panic(\"stop\");Span<int> s=[x];{Span<int> inner=[x];s=inner;}WriteLine(s[0]);}", "CS8352", "safe-context", "VIB007")]
    [InlineData("constant-switch-missing-assignment", "WriteLine(F());static int F(){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 2:x=7;break;}return x;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-nonconstant-unmatched", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when c||true:x=7;break;}return x;}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-out-unmatched", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when Set(out x)||true:break;}return x;}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-out-default-reset", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when Set(out x):return x;default:return x;}}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-assignment-unmatched", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when (x=7)>0:break;}return x;}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-conditional-out-unassigned-body", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when c||Set(out x):return x;default:return 0;}}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-out-next-label-reset", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when Set(out x):break;case 1:return x;}return 0;}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("switch-expression-default-reset", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;return Round12Selector.Read(1) switch{1 when Set(out x)=>x,_=>x};}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("switch-expression-post-reset", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;int result=Round12Selector.Read(1) switch{1 when Set(out x)||true=>0,_=>0};return result+x;}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("when-folded-conditional-out", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when(c&&Set(out x))||true:return x;default:return 0;}}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;} public static class Round12Selector{public static int Read(int value)=>value;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("option-guard-next-label-reset", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;Option<int> option=Some(1);switch(option){case Some<int>(var v) when Set(out x):return x+v;case Some<int>(var w):return x+w;case NoneType:return 0;}}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("result-guard-next-label-reset", "WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;Result<int,int> result=Ok(1);switch(result){case Ok<int>(var v) when Set(out x):return x+v;case Ok<int>(var w):return x+w;case Err<int>(var e):return e;}}static bool Set(out int value){value=7;return true;}static int Select(out int value){value=7;return 1;}", "CS0165", "definitely assigned", "VIB001")]
    [InlineData("plain-switch-section", "F(1);static void F(int x){switch(x){case 1:WriteLine(1);case 2:WriteLine(2);break;}}", "CS0163", "switch section", "VIB000")]
    [InlineData("plain-final-section", "F(1);static void F(int x){switch(x){case 1:break;default:WriteLine(2);}}", "CS8070", "switch section", "VIB000")]
    [InlineData("algebraic-switch-section", "F(1,false);static void F(int x,bool b){switch(x){case 1:if(b||true)break;case 2:WriteLine(2);break;}}", "CS0163", "switch section", "VIB000")]
    [InlineData("pattern-switch-section", "F(1,2);static void F(int x,int y){switch(x){case 1:{if(y is var k)break;}case 2:WriteLine(2);break;}}", "CS0163", "switch section", "VIB000")]
    [InlineData("loop-switch-section", "F(1);static void F(int x){switch(x){case 1:while(true){break;}case 2:WriteLine(2);break;}}", "CS0163", "switch section", "VIB000")]
    [InlineData("switch-section", "F(1);static void F(int x){switch(x){case 1:VibraniumRuntime.Panic(\"stop\");case 2:WriteLine(2);break;}}", "CS0163", "switch section", "VIB000")]
    [InlineData("last-switch-section", "F(1);static void F(int x){switch(x){case 1:VibraniumRuntime.Panic(\"stop\");}}", "CS8070", "switch section", "VIB000")]
    public async Task PanicSourceValidity_InvalidCSharp_IsRejectedByEveryFrontend(
        string caseName, string source, string roslynId, string selfMessage, string selfId)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "vibranium-source-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SourceInvalid", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == roslynId && d.Severity == DiagnosticSeverity.Error);
            var allDiagnostics = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAllDiagnosticsAsync();
            Assert.Contains(allDiagnostics, d => d.Id == roslynId && d.Severity == DiagnosticSeverity.Error);
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success);
            Assert.Contains(roslynId, string.Join("\n", stage0.Errors), StringComparison.Ordinal);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
            Assert.Contains(selfId, self.Out + self.Err, StringComparison.Ordinal);
            Assert.Contains(selfMessage, self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData("initialized", "WriteLine(\"before\");WriteLine(F());static int F(){VibraniumRuntime.Panic(\"stop\");int x=7;return x;}")]
    [InlineData("reference-parameter", "WriteLine(\"before\");int x=7;WriteLine(F(ref x));static ref int F(ref int x){VibraniumRuntime.Panic(\"stop\");return ref x;}")]
    [InlineData("reference-elision-is-not-scoped", "WriteLine(\"before\");int x=1;int y=2;WriteLine(F(ref x,ref y));static ref int F(ref int p,ref int q){VibraniumRuntime.Panic(\"stop\");return ref p;}")]
    [InlineData("reference-lifetime-is-not-scoped", "WriteLine(\"before\");int x=1;int y=2;WriteLine(F(ref x,ref y));[return:LifetimeOf(\"q\")]static ref int F(ref int p,ref int q){VibraniumRuntime.Panic(\"stop\");return ref p;}")]
    [InlineData("class-heap-reference", "WriteLine(\"before\");WriteLine(F());static ref int F(){VibraniumRuntime.Panic(\"stop\");var b=new Box(7);return ref b.Value;}public class Box{public int Value;public Box(int x){Value=x;}}")]
    [InlineData("class-heap-view", "WriteLine(\"before\");WriteLine(F()[0]);static ReadOnlySpan<int> F(){VibraniumRuntime.Panic(\"stop\");var b=new Box();return b.Values.AsReadOnlySpan();}public class Box{public Vec<int> Values;public Box(){Values=new Vec<int>();Values.Add(7);}}")]
    [InlineData("unconsumed-resource", "WriteLine(\"before\");WriteLine(F());static int F(){var r=new Resource();VibraniumRuntime.Panic(\"stop\");return 7;}public class Resource:System.IDisposable{public void Dispose(){WriteLine(\"disposed\");}}")]
    [InlineData("move-does-not-unassign", "WriteLine(\"before\");WriteLine(F());static int F(){VibraniumRuntime.Panic(\"stop\");var a=new Box(7);var b=a;WriteLine(b.Value);return a.Value;}public class Box{public int Value;public Box(int x){Value=x;}}")]
    [InlineData("field-assignment", "WriteLine(\"before\");WriteLine(F().Y);static Pair F(){VibraniumRuntime.Panic(\"stop\");Pair p;p.X=1;p.Y=7;return p;}public struct Pair{public int X;public int Y;}")]
    [InlineData("out-branch-assignments", "WriteLine(\"before\");F(true,out int x);WriteLine(x);static void F(bool c,out int x){VibraniumRuntime.Panic(\"stop\");if(c)x=1;else x=2;}")]
    [InlineData("boolean-subconditions", "WriteLine(\"before\");WriteLine(F(false));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;if(c||true)x=7;return x;}")]
    [InlineData("conditional-out", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;if(c&&Try(out x))return x;return 0;}static bool Try(out int x){x=7;return true;}")]
    [InlineData("constant-readonly-collection", "WriteLine(\"before\");WriteLine(F()[0]);static ReadOnlySpan<int> F(){VibraniumRuntime.Panic(\"stop\");return [1,2];}")]
    [InlineData("empty-span-collection", "WriteLine(\"before\");WriteLine(F().Length);static Span<int> F(){VibraniumRuntime.Panic(\"stop\");return [];}")]
    [InlineData("empty-struct", "WriteLine(\"before\");F();static Empty F(){VibraniumRuntime.Panic(\"stop\");Empty e;return e;}public struct Empty{}")]
    [InlineData("scoped-view-top-body-assignment", "WriteLine(\"before\");Span<int> s=[1];F(s,7);static void F(scoped Span<int> s,int x){VibraniumRuntime.Panic(\"stop\");s=[x];WriteLine(s[0]);}")]
    [InlineData("view-pattern-binding", "WriteLine(\"before\");WriteLine(F(7));static int F(int x){VibraniumRuntime.Panic(\"stop\");ReadOnlySpan<int> s=[x];if(s is var p)return p[0];return 0;}")]
    [InlineData("constant-switch-assignment", "WriteLine(\"before\");WriteLine(F());static int F(){VibraniumRuntime.Panic(\"stop\");int x;switch(1){case 1:x=7;break;}return x;}")]
    [InlineData("constant-switch-pattern-binding", "WriteLine(\"before\");WriteLine(F());static int F(){VibraniumRuntime.Panic(\"stop\");int x;switch(1){case >0 and var n:x=n;break;}return x;}")]
    [InlineData("when-out-true-body", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when Set(out x):return x;default:return 0;}}static bool Set(out int value){value=7;return true;} public static class Round12Selector{public static int Read(int value)=>value;}")]
    [InlineData("when-conditional-out-true-body", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case 1 when c&&Set(out x):return x;default:return 0;}}static bool Set(out int value){value=7;return true;} public static class Round12Selector{public static int Read(int value)=>value;}")]
    [InlineData("when-constant-true-assignment", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(1){case 1 when true:x=7;break;}return x;}")]
    [InlineData("when-pattern-and-out-binding", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Round12Selector.Read(1)){case var n when Set(out x):return x+n;default:return 0;}}static bool Set(out int value){value=7;return true;} public static class Round12Selector{public static int Read(int value)=>value;}")]
    [InlineData("switch-expression-conditional-out", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;return Round12Selector.Read(1) switch{1 when c&&Set(out x)=>x,_=>0};}static bool Set(out int value){value=7;return true;} public static class Round12Selector{public static int Read(int value)=>value;}")]
    [InlineData("selector-out-assignment-survives", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;switch(Select(out x)){case 1 when c||true:break;}return x;}static int Select(out int value){value=7;return 1;}")]
    [InlineData("option-guard-out-true-body", "WriteLine(\"before\");WriteLine(F(true));static int F(bool c){VibraniumRuntime.Panic(\"stop\");int x;Option<int> option=Some(1);switch(option){case Some<int>(var v) when Set(out x):return x+v;default:return 0;}}static bool Set(out int value){value=7;return true;}")]
    [InlineData("switch-break", "WriteLine(\"before\");F(1);static void F(int x){switch(x){case 1:VibraniumRuntime.Panic(\"stop\");break;case 2:WriteLine(2);break;}}")]
    public async Task PanicSourceValidity_ValidSource_PreservesImmediateAbort(string caseName, string source)
    {
        if (caseName is "constant-switch-assignment" or "constant-switch-pattern-binding" or "when-constant-true-assignment")
        {
            await Round12CutSelfTests.AssertRejectedCut(source, 19);
            return;
        }
        await AssertDifferential(caseName, source, "before\n", 101, "panic: stop\n");
    }

    [Fact]
    public async Task PanicSourceValidity_ResultAndOptionOutGuards_AssignEveryCSharpOutput()
    {
        const string source = """
            var result = Parse(true);
            if (result.IsErr(out var ok, out var error)) VibraniumRuntime.Panic("bad");
            WriteLine(ok);
            Option<int> option = Some(4);
            if (option.IsNone(out var value)) VibraniumRuntime.Panic("none");
            WriteLine(value);
            static Result<int, int> Parse(bool good)
            {
                if (good) return Ok(5);
                return Err(1);
            }
            """;
        await AssertDifferential("guard-output", source, "5\n4\n", 0, "");
    }

    [Fact]
    public async Task SourceValidity_SwitchGroupedLabelsAndExplicitBreaks_AreAccepted()
    {
        const string source = """
            F(1); F(2); F(3);
            static void F(int value)
            {
                switch (value)
                {
                    case 1:
                    case 2:
                        WriteLine(value);
                        break;
                    default:
                        WriteLine(3);
                        break;
                }
            }
            """;
        await AssertDifferential("grouped-switch-labels", source, "1\n2\n3\n", 0, "");
    }

    private static async Task AssertDifferential(string caseName, string source, string output, int exitCode, string error)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "vibranium-source-valid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SourceValid", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            string dll = Path.Combine(temporary, "SourceValid.dll");
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temporary, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temporary, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temporary, "SourceValid.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            var managed = Run(DotnetExe, dll);
            Assert.Equal((exitCode, output, error), managed);
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
            string stage0Ir = Path.Combine(temporary, "stage0.ll");
            File.WriteAllText(stage0Ir, stage0.LlvmIr);
            string selfIr = Path.Combine(temporary, "self.ll");
            var self = Run(DotnetExe, SelfCompiler, "emit-ll", path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", selfIr);
            Assert.True(self.Exit == 0, caseName + ": " + self.Out + self.Err);
            foreach (string ir in new[] { stage0Ir, selfIr })
            {
                var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.Exit == 0, verify.Err);
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + (sanitize ? ".asan" : ".native");
                    var arguments = new List<string> { "-O3", ir, "-o", native };
                    if (sanitize) arguments.Add("-fsanitize=address,leak");
                    var build = Run("clang-18", arguments.ToArray());
                    Assert.True(build.Exit == 0, build.Err);
                    Assert.Equal(managed, Run(native));
                }
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static (int Exit, string Out, string Err) Run(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
