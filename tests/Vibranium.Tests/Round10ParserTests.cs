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

public sealed class Round10ParserTests
{
    private const string DirectSwitchFunctionMessage = "Local functions must be declared inside a block; declaring one directly in a switch section is not supported in Vibranium v0.2.";

    private static string RepoRoot
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "Vibranium.slnx")))
            {
                dir = Directory.GetParent(dir)!.FullName;
            }
            return dir;
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Compiler or native test process exceeded 120 seconds.");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData("c?[x]:[x+1]")]
    [InlineData("c ? [x] : [x+1]")]
    [InlineData("c?/*collection*/[x]:[x+1]")]
    [InlineData("c?([x]):([x+1])")]
    [InlineData("c?[x,x+1,]:[x+1,x+2,]")]
    [InlineData("c?[x>0?x:-x]:[x+1]")]
    [InlineData("c?[x switch{>0=>x,_=>-x}]:[x+1]")]
    [InlineData("c?[Pick(value:x)]:[Pick(value:x+1)]")]
    [InlineData("c?[tail[0]]:[tail[^1]]")]
    [InlineData("!c?[x]:[x+1]")]
    [InlineData("c&&x>0?[x]:[x+1]")]
    public async Task Round10_ConditionalCollectionInitializer_UsesConditionalGrammar_IsRejectedInVersion02(string expression)
    {
        string locals = expression.Contains("tail", StringComparison.Ordinal) ? "ReadOnlySpan<int> tail=[x,x+1];" : "";
        string method = expression.Contains("Pick", StringComparison.Ordinal) ? "static int Pick(int value)=>value;" : "";
        string source="WriteLine(F(true,7));WriteLine(F(false,7));static int F(bool c,int x){"+locals+
            "ReadOnlySpan<int> s="+expression+";return s[0];}"+method;
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Theory]
    [InlineData("a?[x]:b?[x+1]:[x+2]")]
    [InlineData("a?b?[x]:[x+1]:[x+2]")]
    [InlineData("a?(b?[x]:[x+1]):[x+2]")]
    [InlineData("a?b?[x]:[x+1]:b?[x+2]:[x+3]")]
    public async Task Round10_NestedConditionalCollections_PreserveAssociativity_IsRejectedInVersion02(string expression)
    {
        string source="WriteLine(F(true,true,7));WriteLine(F(true,false,7));WriteLine(F(false,true,7));WriteLine(F(false,false,7));"+
            "static int F(bool a,bool b,int x){ReadOnlySpan<int> s="+expression+";return s[0];}";
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Theory]
    [InlineData("ReadOnlySpan<int> s=c?[]:[x];return s.Length;")]
    [InlineData("Span<int> s=c?[x]:[x+1];s[0]+=2;return s[0];")]
    [InlineData("Vec<int> s=c?[x]:[x+1];return s[0];")]
    [InlineData("return First(c?[x]:[x+1]);")]
    public async Task Round10_ConditionalCollectionTargets_PreserveTargetTyping_IsRejectedInVersion02(string body)
    {
        string first = body.Contains("First", StringComparison.Ordinal) ? "static int First(ReadOnlySpan<int> value)=>value[0];" : "";
        string source="WriteLine(F(true,7));WriteLine(F(false,7));static int F(bool c,int x){"+body+"}"+first;
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Fact]
    public async Task Round10_ConditionalCollections_EvaluateOnlySelectedArm_IsRejectedInVersion02()
    {
        const string source="WriteLine(F(true,7));WriteLine(F(false,7));static int F(bool c,int x){int n=0;ReadOnlySpan<int> s=c?[Next(ref n,x)]:[Next(ref n,x+1)];return s[0]+n*100;}static int Next(ref int n,int value){n+=1;return value;}";
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Fact]
    public async Task Round10_ConditionalCollectionAfterPanic_RemainsValidSource_IsRejectedInVersion02()
    {
        const string source="WriteLine(\"before\");WriteLine(F(true,7));static int F(bool c,int x){VibraniumRuntime.Panic(\"stop\");ReadOnlySpan<int> s=c?[x]:[x+1];return s[0];}";
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Fact]
    public async Task Round10_CollectionElementNestedConditionals_PreserveBracketBoundaries_IsRejectedInVersion02()
    {
        string value="x";
        for(int i=0;i<12;i++) value="First(c?["+value+"]:[x+1])";
        string source="WriteLine(F(true,7));WriteLine(F(false,7));static int F(bool c,int x){ReadOnlySpan<int> s=c?["+value+"]:[x+1];return s[0];}static int First(ReadOnlySpan<int> value)=>value[0];";
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Theory]
    [InlineData("s?[0] ?? 'x'")]
    [InlineData("(c ? s?[0] : 'z') ?? 'x'")]
    [InlineData("(c ? s?[c?0:1] : 'z') ?? 'x'")]
    [InlineData("(c ? c ? s?[0] : 'y' : 'z') ?? 'x'")]
    public async Task Round10_GenuineConditionalIndexing_RemainsNullConditional(string expression)
    {
        string source="F(true);static void F(bool c){string s=\"ab\";WriteLine((int)("+expression+"));}";
        await AssertRejectedSubset(source, "VIB008", requireNullConditional: true);
    }

    [Fact]
    public async Task Round10_GenuineConditionalMemberAccess_RemainsRejected()
    {
        await AssertRejectedSubset("string s=\"ab\";WriteLine(s?.Length??0);", "VIB008", requireNullConditional: true);
    }

    [Fact]
    public async Task Round10_ConditionalCollectionPostfixLookahead_PreservesUnsupportedBoundary()
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(true,7));static int F(bool c,int x){ReadOnlySpan<int> s=c?[x]!:[x+1];return s[0];}", 16);
    }

    [Theory]
    [InlineData("c?[x,..tail]:[x+1]")]
    [InlineData("c?[..tail]:[]")]
    public async Task Round10_ConditionalCollectionSpread_PreservesUnsupportedBoundary(string expression)
    {
        string source="WriteLine(F(true,7));static int F(bool c,int x){ReadOnlySpan<int> tail=[x];ReadOnlySpan<int> s="+expression+";return s.Length;}";
        await AssertRejectedSubset(source, "VIB000", requireNullConditional: false);
    }

    [Theory]
    [InlineData("if(c) int x=1;")]
    [InlineData("if(c);else int x=1;")]
    [InlineData("while(c) int x=1;")]
    [InlineData("do int x=1;while(c);")]
    [InlineData("for(;c;) int x=1;")]
    [InlineData("foreach(int value in items) int x=value;")]
    [InlineData("using(var r=new R()) int x=1;")]
    [InlineData("if(c) const int x=1;")]
    [InlineData("if(c) int x=1,y=2;")]
    [InlineData("if(c) var x=1;")]
    [InlineData("if(c) ref int x=ref seed;")]
    [InlineData("if(c) ref readonly int x=ref seed;")]
    [InlineData("if(c) scoped ReadOnlySpan<int> x=[1];")]
    [InlineData("if(c) using var r=new R();")]
    [InlineData("if(c) static int G()=>1;")]
    public void Round10_EmbeddedDeclarations_AreRejectedByCSharpGrammar(string body)
    {
        string source="F(false);static void F(bool c){int seed=1;ReadOnlySpan<int> items=[1];"+body+"}public class R:System.IDisposable{public void Dispose(){}}";
        AssertInvalidSource(source, "CS1023");
    }

    [Fact]
    public async Task Round10_BracedDeclarationsAndLocalFunctions_RemainSupported()
    {
        const string source="F(true);static void F(bool c){if(c){int x=1;WriteLine(x);}else{int x=2;WriteLine(x);}while(c){int x=3;WriteLine(x);break;}bool again=false;do{const int x=4;WriteLine(x);}while(again);for(int i=0;i<1;i++){int x=5;WriteLine(x);}ReadOnlySpan<int> items=[6];foreach(int value in items){int x=value;WriteLine(x);}using(var r=new R()){int x=7;WriteLine(x);}{static int G()=>8;WriteLine(G());}}public class R:System.IDisposable{public void Dispose(){WriteLine(9);}}";
        await AssertDifferential(source,"1\n3\n4\n5\n6\n7\n9\n8\n");
    }

    [Fact]
    public async Task Round10_EmbeddedStatementsAndForHeaderDeclarations_RemainSupported()
    {
        const string source="F(true);F(false);static void F(bool c){int x=0;if(c)x=1;else x=2;while(x<3)x++;do x++;while(x<4);for(int i=0;i<1;i++)x++;ReadOnlySpan<int> items=[1];foreach(int value in items)x+=value;using(var r=new R())x++;WriteLine(x);}public class R:System.IDisposable{public void Dispose(){}}";
        await AssertDifferential(source,"7\n7\n");
    }

    [Theory]
    [InlineData("ReadOnlySpan<int> s=c?[x];WriteLine(s.Length);")]
    [InlineData("ReadOnlySpan<int> s=c?[x]:;WriteLine(s.Length);")]
    [InlineData("ReadOnlySpan<int> s=c?[x] [x+1];WriteLine(s.Length);")]
    public void Round10_MalformedConditionalCollections_AreRejected(string body)
    {
        AssertInvalidSource("F(true,7);static void F(bool c,int x){"+body+"}", expectedRoslynId:null);
    }

    private static void AssertInvalidSource(string source,string? expectedRoslynId)
    {
        string temp=Path.Combine(Path.GetTempPath(),$"vibranium-r10-parser-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path=Path.Combine(temp,"Program.cs");File.WriteAllText(path,source);
            var compilation=VibraniumCompilerDriver.CreateCompilationFromPath(path,"InvalidParserSource",includeStd:true);
            var errors=compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
            Assert.NotEmpty(errors);
            if(expectedRoslynId is not null) Assert.Contains(errors,d=>d.Id==expectedRoslynId);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self=Run(DotnetExe,SelfCompiler,"check",path,"--sysroot",Path.Combine(RepoRoot,"src"));
            Assert.NotEqual(0,self.Exit);
            Assert.Contains("VIB",self.Out+self.Err,StringComparison.Ordinal);
            if(expectedRoslynId=="CS1023") Assert.Contains("VIB000",self.Out+self.Err,StringComparison.Ordinal);
        }
        finally {Directory.Delete(temp,recursive:true);}
    }

    [Fact]
    public void Round10_ConditionalLookahead_RestoresAstDiagnosticsAndFollowingParseState()
    {
        var inspect=CreateParserProbe();
        const string suffix="ReadOnlySpan<int> next=c?[Identity<int>(7)]:[8];WriteLine(next[0]);static int Identity<T>(int value)=>value;";
        const string prefix="bool c=true;string text=\"ab\";int value=";
        string original=Probe(inspect,prefix+"(int)((c ? text?[Identity<int>(0)] : 'z') ?? 'x');"+suffix);
        string parenthesized=Probe(inspect,prefix+"(int)((c ? (text?[Identity<int>(0)]) : 'z') ?? 'x');"+suffix);
        Assert.Equal(parenthesized,original);
        Assert.Contains("8:Null-conditional",original,StringComparison.Ordinal);
        Assert.DoesNotContain("Expected",original,StringComparison.Ordinal);
        string collection=Probe(inspect,"bool c=true;ReadOnlySpan<int> s=c?[Identity<int>(7)]:[8];"+suffix);
        string collectionControl=Probe(inspect,"bool c=true;ReadOnlySpan<int> s=c?([Identity<int>(7)]):[8];"+suffix);
        Assert.Equal(collectionControl,collection);
        Assert.DoesNotContain("Null-conditional",collection,StringComparison.Ordinal);
        Assert.DoesNotContain("Expected",collection,StringComparison.Ordinal);
        string postfix=Probe(inspect,"ReadOnlySpan<int> s=c?[Identity<int>(7)]!:[8];"+suffix);
        string postfixControl=Probe(inspect,"ReadOnlySpan<int> s=c?([Identity<int>(7)]!):[8];"+suffix);
        Assert.Equal(postfixControl,postfix);
        Assert.Contains("Null-forgiving",postfix,StringComparison.Ordinal);
        Assert.DoesNotContain("Null-conditional",postfix,StringComparison.Ordinal);
        string ambiguous="text?[Identity<int>(0)]";
        string explicitAccess="(text?[Identity<int>(0)])";
        for(int i=0;i<24;i++)
        {
            ambiguous="c ? "+ambiguous+" : 'z'";
            explicitAccess="c ? "+explicitAccess+" : 'z'";
        }
        var elapsed=Stopwatch.StartNew();
        string nested=Probe(inspect,prefix+"(int)(("+ambiguous+") ?? 'x');"+suffix);
        string nestedControl=Probe(inspect,prefix+"(int)(("+explicitAccess+") ?? 'x');"+suffix);
        Assert.Equal(nestedControl,nested);
        Assert.DoesNotContain("Expected",nested,StringComparison.Ordinal);
        // A generous regression bound catches recursive speculative explosion,
        // not small timing differences. Normal parsing is not benchmarked here.
        Assert.True(elapsed.Elapsed<TimeSpan.FromSeconds(15),"Nested conditional lookahead exceeded the bounded-work regression limit.");
        string malformed=Probe(inspect,"string prior=null;"+prefix+"(int)((c ? text?[Identity<int>(0)] : ) ?? 'x');"+suffix);
        string malformedControl=Probe(inspect,"string prior=null;"+prefix+"(int)((c ? (text?[Identity<int>(0)]) : ) ?? 'x');"+suffix);
        Assert.Equal(malformedControl,malformed);
        Assert.Contains("Null-conditional",malformed,StringComparison.Ordinal);
    }

    [Fact]
    public void Round10_OutUnderscoreSyntax_PreservesBindingDecision()
    {
        var inspect=CreateParserProbe();
        const string method="static void Set(out int value){value=1;}";
        string unbound=Probe(inspect,"Set(out _);"+method);
        string namedLocal=Probe(inspect,"int _=0;Set(out _);"+method);
        string namedArgument=Probe(inspect,"int _=0;Set(value:out _);"+method);
        string declarationDiscards=Probe(inspect,"Set(out var _);Set(out int _);"+method);
        Assert.Contains("|CallArg[-2;Out;<none>;ExprIdent:_]",unbound,StringComparison.Ordinal);
        Assert.Contains("|CallArg[-2;Out;<none>;ExprIdent:_]",namedLocal,StringComparison.Ordinal);
        Assert.Contains("|CallArg[-2;Out;value;ExprIdent:_]",namedArgument,StringComparison.Ordinal);
        Assert.Equal(2,declarationDiscards.Split("|CallArg[-3;Out;<none>;]",StringSplitOptions.None).Length-1);
        Assert.DoesNotContain("|CallArg[-3;",unbound,StringComparison.Ordinal);
        Assert.DoesNotContain("|CallArg[-2;",declarationDiscards,StringComparison.Ordinal);
        foreach(string result in new[]{unbound,namedLocal,namedArgument,declarationDiscards})
        {
            Assert.DoesNotContain("Expected",result,StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("static int Local(in int input,ref readonly int readonlyInput,ref int writable,int value)=>input;", false, false)]
    [InlineData("static int Outer(){static int Local(in int input,ref readonly int readonlyInput,ref int writable,int value)=>input;return 0;}", false, false)]
    [InlineData("public static class Helpers{public static int Pick(in int input,ref readonly int readonlyInput,ref int writable,int value)=>input;}", false, false)]
    [InlineData("static T Local<T>(in T input,ref readonly T readonlyInput,ref T writable,T value) where T:unmanaged=>input;", false, false)]
    [InlineData("public static class Helpers{public static T Pick<T>(in T input,ref readonly T readonlyInput,ref T writable,T value) where T:unmanaged=>input;}", false, false)]
    [InlineData("static int Local(scoped in int input,scoped ref readonly int readonlyInput,ref int writable,int value)=>input;", true, false)]
    [InlineData("public static class Helpers{public static int Pick(this in int input,ref readonly int readonlyInput,ref int writable,int value)=>input;}", false, true)]
    public void Round10_RefReadonlyParameterMetadata_PreservesSourceDistinction(string source,bool scoped,bool extension)
    {
        var syntax=CSharpSyntaxTree.ParseText(source,new CSharpParseOptions(LanguageVersion.CSharp15));
        Assert.DoesNotContain(syntax.GetDiagnostics(),d=>d.Severity>=DiagnosticSeverity.Warning);
        var inspect=CreateParserProbe();
        string result=Probe(inspect,source);
        Assert.Contains($"|Param[input;In;False;{scoped};{extension}]",result,StringComparison.Ordinal);
        Assert.Contains($"|Param[readonlyInput;In;True;{scoped};False]",result,StringComparison.Ordinal);
        Assert.Contains("|Param[writable;Ref;False;False;False]",result,StringComparison.Ordinal);
        Assert.Contains("|Param[value;ByValue;False;False;False]",result,StringComparison.Ordinal);
        Assert.DoesNotContain("Expected",result,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("switch(k){case 1:return L();default:static int L()=>5;return 0;}")]
    [InlineData("switch(k){case 1:static int L()=>5;return L();default:return 0;}")]
    [InlineData("switch(k){case 1:return L();static int L()=>5;default:return 0;}")]
    [InlineData("switch(k){case 1:return L<int>(5);default:static T L<T>(T value) where T:unmanaged=>value;return 0;}")]
    [InlineData("switch(k){case 1:const int C=5;return L();default:static int L()=>C;return L();}")]
    [InlineData("switch(k){case 1:return L(2);default:static int L(int n)=>n==0?5:L(n-1);return 0;}")]
    [InlineData("switch(k){case 1 when L()>0:return L();default:static int L()=>5;return 0;}")]
    [InlineData("switch(k){case 1:return L();default:int L()=>k;return L();}")]
    public async Task Round10_DirectSwitchSectionLocalFunctions_HaveExactSubsetDiagnostic(string body)
    {
        await AssertDirectSwitchFunctionRejected("WriteLine(F(1));WriteLine(F(2));static int F(int k){"+body+"}");
    }

    [Fact]
    public async Task Round10_DirectSwitchSectionLocalFunction_WithSelectorMember_HasExactSubsetDiagnostic()
    {
        const string source="WriteLine(C.F());public static class C{public static int L()=>1;public static int F(){switch(L()){case 1:static int L()=>5;return L();default:return 0;}}}";
        await AssertDirectSwitchFunctionRejected(source);
    }

    [Fact]
    public async Task Round10_BracedSwitchSectionLocalFunctions_RemainSupported()
    {
        const string source="WriteLine(F(1));WriteLine(F(2));static int F(int k){switch(k){case 1:{static int L()=>5;return L();}default:{static int L()=>7;return L();}}}";
        await AssertDifferential(source,"5\n7\n");
    }

    [Theory]
    [InlineData("switch(k){case 1:static int L()=>5;return L();default:static int L()=>7;return L();}", "CS0128")]
    [InlineData("switch(k){case 1:static int L()=>5;return L();default:int L=7;return L;}", "CS0136")]
    [InlineData("switch(k){case 1:static int L()=>5;break;default:break;}return L();", "CS0103")]
    [InlineData("switch(L()){default:static int L()=>5;return 0;}", "CS0103")]
    [InlineData("switch(k){case 1:static int L()=>5;return L();default:{static int L()=>7;return L();}}", "CS0136")]
    public void Round10_SwitchSectionLocalFunction_InvalidScopeOrDuplicate_IsRejected(string body,string roslynId)
    {
        AssertInvalidSource("WriteLine(F(1));static int F(int k){"+body+"}",roslynId);
    }

    [Theory]
    [InlineData("switch(1){default:break;}", 0)]
    [InlineData("switch(1){default:break;", 0)]
    [InlineData("switch(1){}", 0)]
    [InlineData("switch(1){default:{static int A()=>1;break;}}", 0)]
    [InlineData("switch(1){case 0:static int A()=>1;break;default:static int B()=>2;break;}", 2)]
    [InlineData("switch(1){default:static int A()=>1;break;", 1)]
    [InlineData("switch(1){default:int A()=>1;break;}", 1)]
    public void Round10_SwitchLocalFunctionCut_RestoresPreviousSwitchShape(string source,int rejectedDeclarations)
    {
        string result=Probe(CreateParserProbe(),source);
        Assert.Contains("|StmtSwitch[D=0]",result,StringComparison.Ordinal);
        Assert.Equal(1,result.Split("|StmtSwitch[D=",StringSplitOptions.None).Length-1);
        Assert.Equal(rejectedDeclarations,result.Split("|0:"+DirectSwitchFunctionMessage,StringSplitOptions.None).Length-1);
        Assert.DoesNotContain("Non-static (capturing) local functions",result,StringComparison.Ordinal);
    }

    private static string Probe(System.Reflection.MethodInfo method,string source)=>(string)method.Invoke(null,new object[]{source})!;

    private static System.Reflection.MethodInfo CreateParserProbe()
    {
        const string harness = """
            using System;
            using System.Text;
            using Vibranium;
            using Vibranium.Std;
            using Vibranium.Self;
            public static class ParserProbe
            {
                public static string Inspect(string text)
                {
                    var sources=new SourceTable(1);
                    sources.AddFile("probe.cs"u8,Encoding.UTF8.GetBytes(text),UnitKinds.App);
                    var names=new Interner(64);
                    var tokens=new TokenArena(64);
                    var ast=new AstArena(64);
                    var diagnostics=new DiagnosticBag(8);
                    Lexer.LexAll(ref sources,ref names,ref tokens,ref diagnostics);
                    Parser.ParseAll(in sources,in tokens,ref names,ref ast,ref diagnostics);
                    var result=new StringBuilder();
                    result.Append(ast.Nodes.Count).Append('/').Append(ast.Extra.Count).Append('/').Append(ast.TypeRefs.Count)
                        .Append('/').Append(ast.Funcs.Count).Append('/').Append(ast.Params.Count).Append('/').Append(diagnostics.Items.Count)
                        .Append('/').Append(diagnostics.MsgPool.Add(ReadOnlySpan<byte>.Empty).Offset);
                    for(int i=0;i<ast.Nodes.Count;i++)
                    {
                        var node=ast.Nodes[i];
                        result.Append('|').Append(node.Kind);
                        if(node.Kind==AstKind.StmtSwitch) result.Append("[D=").Append(node.D).Append(']');
                        if(node.Kind==AstKind.CallArg)
                        {
                            int name=CallArgBits.NameId(in node);
                            result.Append('[').Append(node.C).Append(';').Append(CallArgBits.Mode(in node)).Append(';')
                                .Append(name<0?"<none>":Encoding.UTF8.GetString(names.Text(name))).Append(';');
                            if(node.B>=0)
                            {
                                var expression=ast.Nodes[node.B];
                                result.Append(expression.Kind);
                                if(expression.Kind==AstKind.ExprIdent) result.Append(':').Append(Encoding.UTF8.GetString(names.Text(expression.A)));
                            }
                            result.Append(']');
                        }
                    }
                    for(int i=0;i<ast.Params.Count;i++)
                    {
                        var parameter=ast.Params[i];
                        result.Append("|Param[").Append(Encoding.UTF8.GetString(names.Text(parameter.NameId))).Append(';')
                            .Append(parameter.Mode).Append(';').Append((parameter.Flags&DeclFlags.Readonly)!=0).Append(';')
                            .Append((parameter.Flags&DeclFlags.IsScoped)!=0).Append(';').Append((parameter.Flags&DeclFlags.IsExtension)!=0).Append(']');
                    }
                    for(int i=0;i<diagnostics.Items.Count;i++)
                    {
                        var diagnostic=diagnostics.Items[i];
                        result.Append('|').Append(diagnostic.Code).Append(':').Append(Encoding.UTF8.GetString(diagnostics.MsgPool.Get(diagnostic.Message)));
                    }
                    return result.ToString();
                }
            }
            """;
        string[] platform=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references=platform.Concat(new[]{typeof(Vibranium.Option<>).Assembly.Location,typeof(Vibranium.Std.StringBuf).Assembly.Location,SelfCompiler})
            .Distinct(StringComparer.Ordinal).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("Round10ParserProbe",new[]{CSharpSyntaxTree.ParseText(harness,new CSharpParseOptions(LanguageVersion.CSharp15))},
            references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image=new MemoryStream();
        var emit=compilation.Emit(image);
        Assert.True(emit.Success,string.Join("\n",emit.Diagnostics));
        _=System.Reflection.Assembly.LoadFrom(SelfCompiler);
        return System.Reflection.Assembly.Load(image.ToArray()).GetType("ParserProbe")!.GetMethod("Inspect")!;
    }

    private static async Task AssertDirectSwitchFunctionRejected(string source)
    {
        string temp=Path.Combine(Path.GetTempPath(),$"vibranium-r10-switch-function-cut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path=Path.Combine(temp,"Program.cs");
            File.WriteAllText(path,source);
            var compilation=VibraniumCompilerDriver.CreateCompilationFromPath(path,"DirectSwitchFunctionCut",includeStd:true);
            Assert.Empty(compilation.GetDiagnostics().Where(d=>d.Severity>=DiagnosticSeverity.Warning));
            var self=Run(DotnetExe,SelfCompiler,"check",path,"--sysroot",Path.Combine(RepoRoot,"src"));
            Assert.NotEqual(0,self.Exit);
            string selfDiagnostics=self.Out+self.Err;
            Assert.Contains(Round12CutSelfTests.CutMessage(18), selfDiagnostics, StringComparison.Ordinal);
            Assert.Contains("error VIB000: "+DirectSwitchFunctionMessage,selfDiagnostics,StringComparison.Ordinal);
            var frontend=VibraniumFrontend.CompileAndCheck(compilation);
            var diagnostic=Assert.Single(frontend.Diagnostics, value => value.Message == DirectSwitchFunctionMessage);
            Assert.Single(frontend.Diagnostics, value => value.Message == Round12CutSelfTests.CutMessage(18));
            Assert.Equal("VIB000",diagnostic.Id);
            Assert.Equal(DirectSwitchFunctionMessage,diagnostic.Message);
            var analyzer=await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var analyzerDiagnostic=Assert.Single(analyzer, value => value.GetMessage() == DirectSwitchFunctionMessage);
            Assert.Single(analyzer, value => value.GetMessage() == Round12CutSelfTests.CutMessage(18));
            Assert.Equal("VIB000",analyzerDiagnostic.Id);
            Assert.Equal(DirectSwitchFunctionMessage,analyzerDiagnostic.GetMessage());
        }
        finally
        {
            Directory.Delete(temp,recursive:true);
        }
    }

    private static async Task AssertRejectedSubset(string source, string expectedId, bool requireNullConditional)
    {
        string temp=Path.Combine(Path.GetTempPath(),$"vibranium-r10-parser-reject-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path=Path.Combine(temp,"Program.cs");
            File.WriteAllText(path,source);
            var compilation=VibraniumCompilerDriver.CreateCompilationFromPath(path,"RejectedParserSubset",includeStd:true);
            Assert.Empty(compilation.GetDiagnostics().Where(d=>d.Severity>=DiagnosticSeverity.Warning));
            var frontend=VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Contains(frontend.Diagnostics,d=>d.Id==expectedId);
            var analyzer=await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Contains(analyzer,d=>d.Id==expectedId);
            var self=Run(DotnetExe,SelfCompiler,"check",path,"--sysroot",Path.Combine(RepoRoot,"src"));
            Assert.NotEqual(0,self.Exit);
            string diagnostics=self.Out+self.Err;
            Assert.Contains(expectedId,diagnostics,StringComparison.Ordinal);
            if(requireNullConditional) Assert.Contains("Null-conditional",diagnostics,StringComparison.Ordinal);
            else if(expectedId=="VIB008") Assert.DoesNotContain("Null-conditional",diagnostics,StringComparison.Ordinal);
            else Assert.DoesNotContain("VIB008",diagnostics,StringComparison.Ordinal);
            Assert.DoesNotContain("Expected ':'",diagnostics,StringComparison.Ordinal);
            Assert.DoesNotContain("Expected ';'",diagnostics,StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp,recursive:true);
        }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r10-parser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round10ParserManaged", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round10ParserManaged.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round10ParserManaged.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"},"configProperties":{"System.Globalization.Invariant":true}}}
                """);
            var managed = Run(DotnetExe, dll);
            Assert.Equal((expectedExit, expected, expectedError), managed);

            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(stage0.Success, string.Join("\n", stage0.Errors));
            string c0Ir = Path.Combine(temp, "stage0.ll");
            File.WriteAllText(c0Ir, stage0.LlvmIr);
            string selfIr = Path.Combine(temp, "self.ll");
            var self = Run(DotnetExe, SelfCompiler, "emit-ll", path, "--sysroot", Path.Combine(RepoRoot, "src"), "--asan", "-o", selfIr);
            Assert.True(self.Exit == 0, self.Out + self.Err);
            foreach (string ir in new[] { c0Ir, selfIr })
            {
                var verify = Run("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.Exit == 0, verify.Err);
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + (sanitize ? ".asan" : ".native");
                    var args = new List<string> { "-O3", ir, "-o", native };
                    if (sanitize) args.Add("-fsanitize=address,leak");
                    var build = Run("clang-18", args.ToArray());
                    Assert.True(build.Exit == 0, build.Err);
                    var actual = Run(native);
                    Assert.Equal(managed, actual);
                }
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
