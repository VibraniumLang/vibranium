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

public sealed class Round10Stage0DefiniteAssignmentTests
{
    [Theory]
    [InlineData("boolean", "WriteLine(F(true)); WriteLine(F(false));\nstatic int F(bool b)\n{\n    int x;\n    switch (b) { case true: x = 1; break; case false: x = 2; break; }\n    return x;\n}", "1\n2\n")]
    [InlineData("relational-int", "WriteLine(F(-3)); WriteLine(F(3));\nstatic int F(int i)\n{\n    int x;\n    switch (i) { case var value when value < 0: x = -1; break; default: x = 1; break; }\n    return x;\n}", "-1\n1\n")]
    [InlineData("mixed-return", "WriteLine(F(false));static int F(bool b){int x;switch(b){case true:return 7;case false:x=2;break;}return x;}", "2\n")]
    [InlineData("guard-out-reset", "WriteLine(F(false));static int F(bool b){int x;switch(b){case true when Set(out x):return x;case true:x=1;break;case false:x=2;break;}return x;}static bool Set(out int x){x=3;return false;}", "2\n")]
    [InlineData("double-nan", "WriteLine(F(double.NaN));static int F(double d){int x;switch(d){case double.NaN:x=0;break;case var value when value<0:x=-1;break;default:x=1;break;}return x;}", "0\n")]
    [InlineData("owned-reinitialize", "F(true);F(false);static void F(bool b){var x=new Box(9);Consume(x);switch(b){case true:x=new Box(1);break;case false:x=new Box(2);break;}WriteLine(x.Value);}static void Consume(Box x){WriteLine(x.Value);}public class Box{public int Value;public Box(int x){Value=x;}}", "9\n1\n9\n2\n")]
    [InlineData("owned-reinitialize-return", "F(true);F(false);static void F(bool b){var x=new Box(9);Consume(x);switch(b){case true:return;case false:x=new Box(2);break;}WriteLine(x.Value);}static void Consume(Box x){WriteLine(x.Value);}public class Box{public int Value;public Box(int x){Value=x;}}", "9\n9\n2\n")]
    [InlineData("nested", "WriteLine(F(true,1));WriteLine(F(false,-1));static int F(bool b,int i){int x;switch(b){case true:switch(i){case var value when value<0:x=1;break;default:x=2;break;}break;case false:x=3;break;}return x;}", "2\n3\n")]
    [InlineData("local-function-guard", "WriteLine(F(true));static int F(bool b){int x;switch(b){case true when G():x=1;break;case true:x=2;break;case false:x=3;break;}return x;static bool G(){return false;}}", "2\n")]
    [InlineData("loop-continue", "F();static void F(){for(int i=0;i<3;i++){int x;switch(i){case var value when value<0:continue;default:x=i;break;}WriteLine(x);}}", "0\n1\n2\n")]
    [InlineData("fresh-name-collision", "WriteLine(F(false));static int F(bool b){int __vibSwitchDispatch0=8;int x;switch(b){case true:x=1;break;case false:x=2;break;}return x+__vibSwitchDispatch0;}", "10\n")]
    [InlineData("integer-domains", "WriteLine(U(0U));WriteLine(U(uint.MaxValue));WriteLine(L(long.MinValue));WriteLine(L(long.MaxValue));WriteLine(B((byte)0));WriteLine(B((byte)255));static int U(uint n){int x;switch(n){case 0:x=0;break;default:x=1;break;}return x;}static int L(long n){int x;switch(n){case var value when value<0:x=-1;break;default:x=1;break;}return x;}static int B(byte n){int x;switch(n){case var value when value<128:x=0;break;default:x=1;break;}return x;}", "0\n1\n-1\n1\n0\n1\n")]
    [InlineData("float-nan-infinities", "WriteLine(F(float.NaN));WriteLine(F(float.NegativeInfinity));WriteLine(F(float.PositiveInfinity));static int F(float n){int x;switch(n){case float.NaN:x=0;break;case var value when value<0:x=-1;break;default:x=1;break;}return x;}", "0\n-1\n1\n")]
    [InlineData("binary-pattern", "WriteLine(F(0));WriteLine(F(-1));WriteLine(F(1));static int F(int n){int x;switch(n){case not 0:x=1;break;case 0:x=0;break;}return x;}", "0\n1\n1\n")]
    [InlineData("runtime-final-guard", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){int x;switch(b){case true:x=1;break;case false when !b:x=2;break;default:x=2;break;}return x;}", "1\n2\n")]
    [InlineData("struct-fields", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Pair p;switch(b){case true:p.X=1;p.Y=2;break;case false:p.X=3;p.Y=4;break;}return p.X+p.Y;}public struct Pair{public int X;public int Y;}", "3\n7\n")]
    [InlineData("top-level", "int x;switch(F()){case true:x=1;break;case false:x=2;break;}WriteLine(x);static bool F(){return false;}", "2\n")]
    [InlineData("constructor", "var box=new Box(false);WriteLine(box.Value);public class Box{public int Value;public Box(bool b){int x;switch(b){case true:x=1;break;case false:x=2;break;}Value=x;}}", "2\n")]
    [InlineData("linear-initialization", "F(true);F(false);static void F(bool b){Resource r;switch(b){case true:r=new Resource(1);break;case false:r=new Resource(2);break;}using var owned=r;}public class Resource:System.IDisposable{public int Value;public Resource(int value){Value=value;}public void Dispose(){WriteLine(Value);}}", "1\n2\n")]
    [InlineData("reference-rebind", "F(true);F(false);static void F(bool flag){int a=1;int b=2;ref int r=ref a;switch(flag){case true:r=ref b;break;case false:r=ref b;break;}a=3;WriteLine(r);WriteLine(a);}", "2\n3\n2\n3\n")]
    [InlineData("incomplete-arm-local", "F(0);static void F(int k){switch(k){case 1:{int x=1;WriteLine(x);break;}case 2:WriteLine(2);break;}}", "")]
    [InlineData("incomplete-initialized", "WriteLine(F(0));static int F(int k){int x=9;switch(k){case 1:x=1;break;case 2:x=2;break;}return x;}", "9\n")]
    [InlineData("incomplete-selector-out", "WriteLine(F());static int F(){int x;switch(Set(out x)){case 1:WriteLine(x);break;}return x;}static int Set(out int x){x=3;return 0;}", "3\n")]
    [InlineData("incomplete-pattern-local", "WriteLine(F(0));static int F(int k){switch(k){case var n when n>0:WriteLine(n);break;}return 9;}", "9\n")]
    [InlineData("incomplete-struct-selector", "WriteLine(F());static int F(){Pair p=new Pair();p.Y=2;switch(Set(out p.X)){case 1:break;}return p.X+p.Y;}static int Set(out int x){x=3;return 0;}public struct Pair{public int X;public int Y;}", "5\n")]
    [InlineData("selector-declaration-escapes", "switch(Set(out int x)){case 1:WriteLine(x);break;}WriteLine(x);static int Set(out int x){x=7;return 0;}", "7\n")]
    [InlineData("embedded-incomplete", "F(0);static void F(int k){if(k==0)switch(k){case 1:WriteLine(1);break;}WriteLine(9);}", "9\n")]
    [InlineData("section-constant", "WriteLine(F(0));static int F(int mode){int x;switch(mode){case 0:{const int C=7;x=C;break;}default:x=0;break;}return x;}", "7\n")]
    [InlineData("incomplete-guard", "WriteLine(F(true,false));static int F(bool b,bool c){int x=9;switch(b){case true when c:x=1;break;case false:x=2;break;}return x;}", "9\n")]
    [InlineData("incomplete-enum", "WriteLine(F((E)2));static int F(E e){int x=9;switch(e){case E.A:x=1;break;case E.B:x=2;break;}return x;}enum E{A,B}", "9\n")]
    [InlineData("incomplete-nan", "WriteLine(F(double.NaN));static int F(double d){int x=9;switch(d){case var negative when negative<0:x=-1;break;case var nonnegative when nonnegative>=0:x=1;break;}return x;}", "9\n")]
    [InlineData("in-explicit","int value=4;WriteLine(H(in value));static int H(in int value){return value+2;}","6\n")]
    [InlineData("in-implicit","int value=4;WriteLine(H(value));static int H(in int value){return value+2;}","6\n")]
    [InlineData("ordinary-in-rvalue","WriteLine(H(4+1));static int H(in int value){return value+2;}","7\n")]
    [InlineData("in-reduced-extension","int value=4;WriteLine(value.H());public static class Extensions{public static int H(this in int value){return value+2;}}","6\n")]
    [InlineData("in-generic-named","int value=4;WriteLine(H<int>(value:in value));static int H<T>(in T value){return 6;}","6\n")]
    [InlineData("in-constructor","int value=4;var b=new Box(in value);WriteLine(b.Value);public class Box{public int Value;public Box(in int value){Value=value;}}","4\n")]
    [InlineData("in-overload","int value=4;WriteLine(C.H(value));WriteLine(C.H(in value));public static class C{public static int H(int value){return value+1;}public static int H(in int value){return value+2;}}","5\n6\n")]
    [InlineData("out-selector","WriteLine(F());static int F(){Pair p;p.Y=2;switch(Set(out p.X)){case 1:break;}return p.X+p.Y;}static int Set(out int x){x=3;return 0;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-single","Pair p;Set(out p.X);p.Y=2;WriteLine(p.X+p.Y);static void Set(out int x){x=3;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-multiple","Pair p;Set(out p.X,out p.Y);WriteLine(p.X+p.Y);static void Set(out int x,out int y){x=3;y=2;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-unrelated-read","Pair p;p.Y=2;Set(out p.X,p.Y);WriteLine(p.X+p.Y);static void Set(out int x,int y){x=y+1;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-nested-initialized-prefix","Outer p;p.A=new Pair();Set(out p.A.X);WriteLine(p.A.X);static void Set(out int x){x=3;}public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}","3\n")]
    [InlineData("out-parameter","Make(out Pair p);WriteLine(p.X+p.Y);static void Make(out Pair p){Set(out p.X,out p.Y);}static void Set(out int x,out int y){x=3;y=2;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-named-in-position","Pair p;Set(x:out p.X,y:out p.Y);WriteLine(p.X+p.Y);static void Set(out int x,out int y){x=3;y=2;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-false-return","Pair p;p.Y=2;if(Try(out p.X))WriteLine(9);WriteLine(p.X+p.Y);static bool Try(out int x){x=3;return false;}public struct Pair{public int X;public int Y;}","5\n")]
    [InlineData("out-nested-fields","Outer p;Set(out p.A.X,out p.A.Y);p.B=3;WriteLine(p.A.X+p.A.Y+p.B);static void Set(out int x,out int y){x=1;y=2;}public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}","6\n")]
    [InlineData("nested-field-assignment","Outer p;p.A.X=1;p.A.Y=2;p.B=3;WriteLine(p.A.X+p.A.Y+p.B);public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}","6\n")]
    [InlineData("out-owned-overwrite","var p=new Pair(new Box(1));Set(out p.Value,9);WriteLine(p.Value.Value);static void Set(out Box x,int n){x=new Box(n);}public struct Pair{public Box Value;public Pair(Box value){Value=value;}}public class Box{public int Value;public Box(int n){Value=n;}}","9\n")]
    [InlineData("empty-root","Empty e;Take(e);static void Take(Empty e){WriteLine(1);}public struct Empty{}","1\n")]
    [InlineData("empty-nested","Outer o;o.X=2;Take(o);static void Take(Outer o){WriteLine(o.X);}public struct Empty{}public struct Outer{public Empty E;public int X;}","2\n")]
    [InlineData("nested-subtree-read","Outer p;p.A.X=1;p.A.Y=2;Take(p.A);static void Take(Pair p){WriteLine(p.X+p.Y);}public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}","3\n")]
    [InlineData("nested-branch-meet","WriteLine(F(true));WriteLine(F(false));static int F(bool b){Outer p;if(b){p.A.X=1;p.A.Y=2;}else{p.A=new Pair(3,4);}p.B=5;return p.A.X+p.A.Y+p.B;}public struct Pair{public int X;public int Y;public Pair(int x,int y){X=x;Y=y;}}public struct Outer{public Pair A;public int B;}","8\n12\n")]
    [InlineData("empty-subtree-read","Outer p;Take(p.E);static void Take(Empty e){WriteLine(1);}public struct Empty{}public struct Outer{public Empty E;public int X;}","1\n")]
    [InlineData("empty-out","Make(out Empty e);Take(e);static void Make(out Empty e){}static void Take(Empty e){WriteLine(1);}public struct Empty{}","1\n")]
    [InlineData("empty-nested-out","Make(out Outer e);Take(e);static void Make(out Outer e){e.X=2;}static void Take(Outer e){WriteLine(e.X);}public struct Empty{}public struct Outer{public Empty E;public int X;}","2\n")]
    [InlineData("braced-section-local-function","F(1);static void F(int n){switch(n){case 1:{static int G(){return 4;}WriteLine(G());break;}}}","4\n")]
    [InlineData("owned-whole-assignment","Pair p;p=new Pair(new Box(1));WriteLine(p.Value.Value);public struct Pair{public Box Value;public Pair(Box value){Value=value;}}public class Box{public int Value;public Box(int n){Value=n;}}","1\n")]
    [InlineData("owned-whole-out","Make(out Pair p);WriteLine(p.Value.Value);static void Make(out Pair p){p=new Pair(new Box(1));}public struct Pair{public Box Value;public Pair(Box value){Value=value;}}public class Box{public int Value;public Box(int n){Value=n;}}","1\n")]
    [InlineData("owned-whole-move-reinitialize","var p=new Pair(new Box(1));Take(p);p=new Pair(new Box(2));WriteLine(p.Value.Value);static void Take(Pair p){WriteLine(p.Value.Value);}public struct Pair{public Box Value;public Pair(Box value){Value=value;}}public class Box{public int Value;public Box(int n){Value=n;}}","1\n2\n")]
    [InlineData("owned-option-take-reassign","var p=new Holder(Some(new Box(1)));var b=p.Value.Take().Unwrap();WriteLine(b.Value);p.Value=Some(new Box(2));WriteLine(p.Value.IsSome);public struct Holder{public Option<Box> Value;public Holder(Option<Box> value){Value=value;}}public class Box{public int Value;public Box(int n){Value=n;}}","1\nTrue\n")]
    [InlineData("copy-result-fieldwise","Holder h;h.Value=Ok(1);WriteLine(h.Value.IsOk);public struct Holder{public Result<int,int> Value;}","True\n")]
    [InlineData("copy-option-fieldwise","Holder h;h.Value=Some(1);WriteLine(h.Value.IsSome);public struct Holder{public Option<int> Value;}","True\n")]
    public async Task Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity(string caseName, string source, string expected)
    {
        using var test = new SourceCase(source);
        var compilation = test.Compilation;
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
        Assert.Empty(await compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync());

        string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
        var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
        string dll = Path.Combine(test.Directory, "Program.dll");
        var emit = managedCompilation.Emit(dll);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(test.Directory, "Vibranium.Core.dll"));
        File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(test.Directory, "Vibranium.Std.dll"));
        File.WriteAllText(Path.Combine(test.Directory, "Program.runtimeconfig.json"), """
            {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
            """);
        var managed = Run(DotnetExe, dll);
        Assert.Equal((0, expected, ""), managed);

        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
        Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
        string stage0Ir = Path.Combine(test.Directory, "stage0.ll");
        File.WriteAllText(stage0Ir, stage0.LlvmIr);
        string selfIr = Path.Combine(test.Directory, "self.ll");
        foreach (string ir in new[] { stage0Ir, selfIr })
        {
            if (ir == selfIr)
            {
                var self = Run(DotnetExe, SelfCompiler, "emit-ll", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", selfIr);
                Assert.True(self.Exit == 0, caseName + ": " + self.Out + self.Err);
            }
            var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
            Assert.True(verify.Exit == 0, verify.Err);
            // O3 can eliminate nonescaping allocations and hide missing cleanup.
            // Keep an O0 sanitizer run so projected initialization must really drop.
            foreach (var (optimization, sanitize) in new[] { ("O3", false), ("O3", true), ("O0", true) })
            {
                string native = ir + "." + optimization + (sanitize ? ".asan" : ".native");
                var arguments = new List<string> { "-" + optimization, ir, "-o", native };
                if (sanitize) arguments.Add("-fsanitize=address,leak");
                var build = Run("clang-18", arguments.ToArray());
                Assert.True(build.Exit == 0, build.Err);
                Assert.Equal(managed, Run(native));
            }
        }
    }

    [Theory]
    [InlineData("integer-gap", "WriteLine(F(1));\nstatic int F(int k)\n{\n    int x;\n    switch (k) { case 1: x = 1; break; case 2: x = 2; break; }\n    return x;\n}")]
    [InlineData("guard-out-failure", "WriteLine(F(1, false)); WriteLine(F(2, true));\nstatic int F(int k, bool c)\n{\n    int x;\n    switch (k)\n    {\n        case 1 when c || Set(out x): return 1;\n        default: return x;\n    }\n}\nstatic bool Set(out int v) { v = 7; return false; }")]
    [InlineData("expression-guard-reset", "WriteLine(F(1)); WriteLine(F(2));\nstatic int F(int k)\n{\n    int x;\n    int r = k switch { 1 when Set(out x) => x, _ => x };\n    return r;\n}\nstatic bool Set(out int v) { v = 7; return false; }")]
    [InlineData("panic-source-continuation", "WriteLine(F(true));\nstatic int F(bool c)\n{\n    int x;\n    if (c) x = 1; else VibraniumRuntime.Panic(\"p\");\n    return x;\n}")]
    [InlineData("option-is-not-exhaustive", "WriteLine(F(Some(4))); WriteLine(F(None));\nstatic int F(Option<int> o)\n{\n    int x;\n    switch (o) { case Some<int>(var v): x = v; break; case NoneType: x = 0; break; }\n    return x;\n}")]
    [InlineData("result-is-not-exhaustive", "WriteLine(F(4)); WriteLine(F(-5));\nstatic int F(int k)\n{\n    Result<int, int> r = k > 0 ? Ok(k) : Err(-k);\n    int x;\n    switch (r) { case Ok<int>(var v): x = v; break; case Err<int>(var e): x = -e; break; }\n    return x;\n}")]
    [InlineData("boolean-guard-gap", "WriteLine(F(false,false));static int F(bool b,bool c){int x;switch(b){case true when c:x=1;break;case false:x=2;break;}return x;}")]
    [InlineData("nan-gap", "WriteLine(F(double.NaN));static int F(double d){int x;switch(d){case var negative when negative<0:x=-1;break;case var nonnegative when nonnegative>=0:x=1;break;}return x;}")]
    [InlineData("enum-gap", "WriteLine(F((E)2));static int F(E e){int x;switch(e){case E.A:x=1;break;case E.B:x=2;break;}return x;}enum E{A,B}")]
    [InlineData("nested-gap", "WriteLine(F(0,0));static int F(int k,int i){int x;switch(k){case 1:switch(i){case 1:x=1;break;}break;}return x;}")]
    [InlineData("relational-zero-gap", "WriteLine(F(0));static int F(int n){int x;switch(n){case var negative when negative<0:x=1;break;case var positive when positive>0:x=2;break;}return x;}")]
    public async Task Round10_StatementSwitch_IncompleteSourceAssignmentRemainsRejected(string caseName, string source)
    {
        using var test = new SourceCase(source);
        Assert.Contains(test.Compilation.GetDiagnostics(), d => d.Id == "CS0165" && d.Severity == DiagnosticSeverity.Error);
        var diagnostics = await test.Compilation.WithAnalyzers(Analyzers()).GetAllDiagnosticsAsync();
        Assert.Contains(diagnostics, d => d.Id == "CS0165" && d.Severity == DiagnosticSeverity.Error);
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.False(stage0.Success);
        Assert.Contains("CS0165", string.Join("\n", stage0.Errors), StringComparison.Ordinal);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains("VIB001", self.Out + self.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("move-in-arm", "F(true);static void F(bool b){var x=new Box(1);switch(b){case true:Consume(x);break;case false:break;}WriteLine(x.Value);}static void Consume(Box x){WriteLine(x.Value);}public class Box{public int Value;public Box(int x){Value=x;}}", "VIB001")]
    [InlineData("missing-reinitialization", "F(true);static void F(bool b){var x=new Box(1);Consume(x);switch(b){case true:x=new Box(2);break;case false:break;}WriteLine(x.Value);}static void Consume(Box x){WriteLine(x.Value);}public class Box{public int Value;public Box(int x){Value=x;}}", "VIB001")]
    [InlineData("live-borrow", "F(true);static void F(bool b){var x=new Box(1);ref int r=ref x.Value;switch(b){case true:Consume(x);break;case false:break;}WriteLine(r);}static void Consume(Box x){WriteLine(x.Value);}public class Box{public int Value;public Box(int x){Value=x;}}", "VIB003")]
    [InlineData("out-moved-owner","var p=new Pair(new Box(1));var q=p;WriteLine(q.Value.Value);Set(out p.X);static void Set(out int x){x=3;}public struct Pair{public Box Value;public int X;public Pair(Box v){Value=v;X=0;}}public class Box{public int Value;public Box(int n){Value=n;}}","VIB001")]
    [InlineData("out-reserved-mutation","var p=new Pair();Set(out p.X,Mutate(ref p));WriteLine(p.X);static void Set(out int x,int y){x=y;}static int Mutate(ref Pair p){p.Y=4;return 3;}public struct Pair{public int X;public int Y;}","VIB003")]
    [InlineData("out-moved-owner-snapshot","var p=new Pair(new Box(1));var q=p;WriteLine(q.Value.Value);Set(out p.X,3);static void Set(out int x,int n){x=n;}public struct Pair{public Box Value;public int X;public Pair(Box v){Value=v;X=0;}}public class Box{public int Value;public Box(int n){Value=n;}}","VIB001")]
    [InlineData("out-overlapping-fields","Pair p;Set(out p.X,out p.X);p.Y=2;WriteLine(p.X+p.Y);static void Set(out int x,out int y){x=3;y=4;}public struct Pair{public int X;public int Y;}","VIB002")]
    [InlineData("owned-field-move-reassign","var p=new Pair(new Box(1));var b=p.Value;p.Value=new Box(2);WriteLine(b.Value);public struct Pair{public Box Value;public Pair(Box value){Value=value;}}public class Box{public int Value;public Box(int n){Value=n;}}","VIB005")]
    [InlineData("empty-field-moved-owner","var p=new Outer(new Box(1));var q=p;WriteLine(q.Value.Value);Take(p.E);static void Take(Empty e){WriteLine(1);}public struct Empty{}public struct Outer{public Empty E;public Box Value;public Outer(Box v){E=new Empty();Value=v;}}public class Box{public int Value;public Box(int n){Value=n;}}","VIB001")]
    public async Task Round10_StatementSwitch_ReachableOwnershipViolationsRemainRejected(string caseName, string source, string id)
    {
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var frontend = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.Contains(frontend, d => d.Id == id);
        var analyzer = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(frontend.Select(d => d.Id).OrderBy(x => x).ToArray(), analyzer.Select(d => d.Id).OrderBy(x => x).ToArray());
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains(id, self.Out + self.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Round10_StatementSwitch_AllDivergentArmsHaveNoLinearResourceExit()
    {
        const string source = "F(true);static void F(bool b){var r=new Resource(1);WriteLine(r.Value);switch(b){case true:while(true){}case false:while(true){}}}public class Resource:System.IDisposable{public int Value;public Resource(int n){Value=n;}public void Dispose(){WriteLine(Value);}}";
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.True(test.Compilation.Emit(Stream.Null).Success);
        Assert.Empty(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics);
        Assert.Empty(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync());
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
        string ir = Path.Combine(test.Directory, "divergent.ll");
        File.WriteAllText(ir, stage0.LlvmIr);
        var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
        Assert.True(verify.Exit == 0, verify.Err);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.Equal((0, "", ""), self);
        // This program intentionally never completes: compilation only, no execution.
    }

    [Theory]
    [InlineData("F(0);static void F(int n){var r=new Resource(1);WriteLine(r.Value);switch(n){case 1:while(true){}}}public class Resource:System.IDisposable{public int Value;public Resource(int n){Value=n;}public void Dispose(){WriteLine(Value);}}", "VIB006")]
    public async Task Round10_StatementSwitch_DivergentPartialCoverageRetainsLinearExit(string source, string id)
    {
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.Contains(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics, d => d.Id == id);
        Assert.Contains(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync(), d => d.Id == id);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.NotEqual(0, self.Exit);
        Assert.Contains(id, self.Out + self.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Round10_StatementSwitch_ProofCacheIsConsistentWithConcurrentWarningsAsErrorsAnalysis()
    {
        const string source = "WriteLine(F(true));static int F(bool b){int x;switch(b){case true:x=1;break;case false:x=2;break;}return x;}";
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            Assert.Empty(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync());
            Assert.Empty(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics);
        }));
    }

    [Theory]
    [InlineData("local","int value=4;WriteLine(H(value));static int H(ref readonly int value){return value+2;}","CS9192")]
    [InlineData("named","int value=4;WriteLine(H(value:value));static int H(ref readonly int value){return value+2;}","CS9192")]
    [InlineData("generic","int value=4;WriteLine(H<int>(value));static int H<T>(ref readonly T value){return 6;}","CS9192")]
    [InlineData("constructor","int value=4;var b=new Box(value);WriteLine(b.Value);public class Box{public int Value;public Box(ref readonly int value){Value=value;}}","CS9192")]
    [InlineData("static-extension","int value=4;WriteLine(Extensions.H(value));public static class Extensions{public static int H(this ref readonly int value){return value+2;}}","CS9192")]
    [InlineData("rvalue","WriteLine(H(4));static int H(ref readonly int value){return value+2;}","CS9193")]
    public async Task Round10_RefReadonlyParameterCut_IsPrimaryRegardlessOfArgumentWarnings(
        string caseName, string source, string compilerId)
    {
        using var test = new SourceCase(source);
        var raw = test.Compilation.WithOptions(test.Compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Default)
            .WithSpecificDiagnosticOptions(test.Compilation.Options.SpecificDiagnosticOptions.SetItem(compilerId, ReportDiagnostic.Warn)));
        Assert.Contains(raw.GetDiagnostics(), d => d.Id == compilerId && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(test.Compilation.GetDiagnostics(), d => d.Id == compilerId && d.Severity == DiagnosticSeverity.Error);
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation).Success);

        using var pragma = new SourceCase("#pragma warning disable CS9192, CS9193\n" + source);
        var suppressed = raw.WithOptions(raw.Options.WithSpecificDiagnosticOptions(raw.Options.SpecificDiagnosticOptions
            .SetItem("CS9192", ReportDiagnostic.Suppress).SetItem("CS9193", ReportDiagnostic.Suppress)));
        foreach (var compilation in new[] { raw, suppressed, pragma.Compilation })
        {
            Assert.Single(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics, d => d.Id == "VIB000" && d.Message == Round11AuditAssertions.RefReadonlyParameterMessage);
            Assert.Single(await compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync(), d => d.Id == "VIB000" && d.GetMessage() == Round11AuditAssertions.RefReadonlyParameterMessage);
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success);
            Assert.Contains("VIB000", string.Join("\n", stage0.Errors), StringComparison.Ordinal);
        }
        var self = Run(DotnetExe, SelfCompiler, "check", pragma.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains(Round11AuditAssertions.RefReadonlyParameterMessage, self.Out + self.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("out-source-uninitialized","Pair p;p.Y=2;Set(out p.X,p.X);WriteLine(p.X);static void Set(out int x,int y){x=y;}public struct Pair{public int X;public int Y;}","CS0170")]
    [InlineData("out-missing-sibling","Pair p;Set(out p.X);WriteLine(p.Y);static void Set(out int x){x=3;}public struct Pair{public int X;public int Y;}","CS0170")]
    [InlineData("out-heap-owner-uninitialized","Box p;Set(out p.X);static void Set(out int x){x=3;}public class Box{public int X;}","CS0165")]
    [InlineData("out-nested-sibling-missing","Outer p;Set(out p.A.X);p.B=3;WriteLine(p.A.Y);static void Set(out int x){x=1;}public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}","CS0170")]
    public async Task Round10_OutField_ReadsAndUninitializedBasesRemainRejected(string caseName, string source, string compilerId)
    {
        using var test = new SourceCase(source);
        Assert.Contains(test.Compilation.GetDiagnostics(), d => d.Id == compilerId && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics, d => d.Id == "VIB001");
        Assert.Contains(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync(), d => d.Id == "VIB001");
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.False(stage0.Success);
        Assert.Contains(compilerId, string.Join("\n", stage0.Errors), StringComparison.Ordinal);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains("VIB001", self.Out + self.Err, StringComparison.Ordinal);
    }

    private const string PartialOwnedInitializationMessage = "A struct with owned fields must be initialized in one assignment; initializing it field by field (including through out) is not supported in Vibranium v0.2.";
    private const string DirectSectionFunctionMessage = "Local functions must be declared inside a block; declaring one directly in a switch section is not supported in Vibranium v0.2.";

    [Theory]
    [InlineData("out-owned-field","Pair p;Set(out p.Value,7);p.X=2;WriteLine(p.Value.Value+p.X);static void Set(out Box x,int n){x=new Box(n);}public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-partial-scope","Pair p;Set(out p.Value,7);WriteLine(1);static void Set(out Box x,int n){x=new Box(n);}public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-nested-out","Outer p;Set(out p.A.Value,7);p.A.X=2;p.B=3;WriteLine(p.A.Value.Value+p.A.X+p.B);static void Set(out Box x,int n){x=new Box(n);}public struct Pair{public Box Value;public int X;}public struct Outer{public Pair A;public int B;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-branch-completion","F(true);F(false);static void F(bool b){Pair p;if(b){Set(out p.Value,7);}else{p.Value=new Box(9);}p.X=2;WriteLine(p.Value.Value+p.X);}static void Set(out Box x,int n){x=new Box(n);}public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-partial-loop","for(int i=0;i<3;i++){Pair p;Set(out p.Value,i);WriteLine(i);}static void Set(out Box x,int n){x=new Box(n);}public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-direct-assignment","Pair p;p.Value=new Box(1);p.X=2;WriteLine(p.Value.Value+p.X);public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-primitive-field-first","Pair p;p.X=2;p.Value=new Box(1);WriteLine(p.Value.Value+p.X);public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-vec-field","Holder h;h.Value=new Vec<int>();h.Value.Add(1);WriteLine(h.Value.Count);public struct Holder{public Vec<int> Value;}")]
    [InlineData("owned-stringbuf-field","Holder h;h.Value=new StringBuf();h.Value.Append(\"x\");WriteLine(h.Value.Length);public struct Holder{public StringBuf Value;}")]
    [InlineData("owned-option-field","Holder h;h.Value=Some(new Box(1));WriteLine(h.Value.IsSome);public struct Holder{public Option<Box> Value;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-result-field","Holder h;h.Value=Ok(new Box(1));WriteLine(h.Value.IsOk);public struct Holder{public Result<Box,int> Value;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("owned-out-struct-parameter","Make(out Pair p);WriteLine(p.Value.Value+p.X);static void Make(out Pair p){p.Value=new Box(1);p.X=2;}public struct Pair{public Box Value;public int X;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    public Task Round10_PartialOwnedStructInitialization_IsOutsideV02(string caseName, string source) =>
        AssertSourceCut(caseName, source, PartialOwnedInitializationMessage);

    [Theory]
    [InlineData("direct-static", "F(1);static void F(int n){switch(n){case 1:static int G(){return 4;}WriteLine(G());break;}}")]
    [InlineData("direct-nonstatic", "F(1);static void F(int n){switch(n){case 1:int G(){return 4;}WriteLine(G());break;}}")]
    [InlineData("direct-capturing-caller", "WriteLine(F(1));static int F(int k){switch(k){case 1:return L();default:int L()=>k;return L();}}")]
    public Task Round10_DirectSwitchSectionLocalFunction_IsOutsideV02(string caseName, string source) =>
        AssertSourceCut(caseName, source, DirectSectionFunctionMessage);

    [Fact]
    public async Task Round10_RejectedDisposableStructConstruction_HasNoZeroSafeCascade()
    {
        const string source = "using var r=new R();WriteLine(1);public struct R:IDisposable{public void Dispose(){WriteLine(2);}}";
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var frontend = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        Assert.Equal("VIB000", Assert.Single(frontend).Id);
        var analyzer = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.Equal("VIB000", Assert.Single(analyzer).Id);
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.False(stage0.Success);
        Assert.Contains("VIB000", Assert.Single(stage0.Errors), StringComparison.Ordinal);
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0);
        Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("VIB008", self.Out + self.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pair p=new Pair();WriteLine(p.Value.Value);public struct Pair{public Box Value;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    [InlineData("Pair p=default;WriteLine(p.Value.Value);public struct Pair{public Box Value;}public class Box{public int Value;public Box(int n){Value=n;}}")]
    public async Task Round10_ValidOwnedStruct_ZeroInitializationStillRejected(string source)
    {
        using var test = new SourceCase(source);
        Assert.Contains(VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics, d => d.Id == "VIB008");
        Assert.Contains(await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync(), d => d.Id == "VIB008");
        Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation).Success);
    }

    private static async Task AssertSourceCut(string caseName, string source, string message)
    {
        using var test = new SourceCase(source);
        Assert.Empty(test.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var frontend = VibraniumFrontend.CompileAndCheck(test.Compilation).Diagnostics;
        const string sectionCut = "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }.";
        string[] messages = message == DirectSectionFunctionMessage ? [message, sectionCut] : [message];
        Assert.NotEmpty(frontend);
        Assert.All(frontend, d => { Assert.Equal("VIB000", d.Id); Assert.Contains(d.Message, messages); });
        Assert.All(messages, expected => Assert.Contains(frontend, d => d.Message == expected));
        var analyzer = await test.Compilation.WithAnalyzers(Analyzers()).GetAnalyzerDiagnosticsAsync();
        Assert.NotEmpty(analyzer);
        Assert.All(analyzer, d => { Assert.Equal("VIB000", d.Id); Assert.Contains(d.GetMessage(), messages); });
        Assert.All(messages, expected => Assert.Contains(analyzer, d => d.GetMessage() == expected));
        var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(test.Compilation);
        Assert.False(stage0.Success);
        Assert.All(stage0.Errors, error => Assert.Contains(messages, expected => error.Contains(expected, StringComparison.Ordinal)));
        Assert.All(messages, expected => Assert.Contains(stage0.Errors, error => error.Contains(expected, StringComparison.Ordinal)));
        var self = Run(DotnetExe, SelfCompiler, "check", test.Path, "--sysroot", Path.Combine(RepoRoot, "src"));
        Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
        Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
        Assert.All(messages, expected => Assert.Contains(expected, self.Out + self.Err, StringComparison.Ordinal));
    }

    private static ImmutableArray<DiagnosticAnalyzer> Analyzers() => [new VibraniumDiagnosticAnalyzer()];

    private sealed class SourceCase : IDisposable
    {
        public string Directory { get; }
        public string Path { get; }
        public CSharpCompilation Compilation { get; }

        public SourceCase(string source)
        {
            Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vibranium-r10-stage0-da-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Path = System.IO.Path.Combine(Directory, "Program.cs");
            File.WriteAllText(Path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(Path, "Program", includeStd: true);
            Compilation = compilation.WithOptions(compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Error));
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static string RepoRoot
    {
        get
        {
            string path = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(path, "Vibranium.slnx"))) path = System.IO.Directory.GetParent(path)!.FullName;
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

    private static (int Exit, string Out, string Err) Run(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo("heavy")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(command);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ASAN_OPTIONS"] = "detect_leaks=1:halt_on_error=1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Timed out running " + command);
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
