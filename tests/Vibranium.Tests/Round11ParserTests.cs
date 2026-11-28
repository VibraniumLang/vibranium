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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round11ParserTests
{

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
    [InlineData("+", "9\n8\n")]
    [InlineData("-", "3\n4\n")]
    [InlineData("*", "18\n12\n")]
    [InlineData("/", "2\n3\n")]
    [InlineData("%", "0\n0\n")]
    [InlineData("<<", "48\n24\n")]
    [InlineData(">>", "0\n1\n")]
    [InlineData(">>>", "0\n1\n")]
    [InlineData("&", "2\n2\n")]
    [InlineData("|", "7\n6\n")]
    [InlineData("^", "5\n4\n")]
    public async Task Round11_SwitchRightOperand_BindsBeforeBinaryOperator(string op, string expected)
    {
        await AssertDifferential("WriteLine(F(6,4));WriteLine(F(6,5));static int F(int x,int y){return x " + op + " y switch{4=>3,_=>2};}", expected);
    }

    [Theory]
    [InlineData("<", "1\n0\n")]
    [InlineData("<=", "1\n0\n")]
    [InlineData(">", "0\n1\n")]
    [InlineData(">=", "0\n1\n")]
    [InlineData("==", "0\n0\n")]
    [InlineData("!=", "1\n1\n")]
    public async Task Round11_SwitchRightOperand_PreservesComparisonPrecedence(string op, string expected)
    {
        await AssertDifferential("WriteLine(F(6,4));WriteLine(F(6,5));static int F(int x,int y){return x " + op + " y switch{4=>7,_=>5}?1:0;}", expected);
    }

    [Theory]
    [InlineData("&&", "1\n0\n0\n0\n")]
    [InlineData("||", "1\n1\n1\n0\n")]
    public async Task Round11_SwitchRightOperand_PreservesBooleanPrecedence(string op, string expected)
    {
        await AssertDifferential("WriteLine(F(true,4));WriteLine(F(true,5));WriteLine(F(false,4));WriteLine(F(false,5));static int F(bool left,int y){return left " + op + " y switch{4=>true,_=>false}?1:0;}", expected);
    }

    [Theory]
    [InlineData("-x switch{-4=>1,_=>2}", "1\n")]
    [InlineData("+x switch{4=>1,_=>2}", "1\n")]
    [InlineData("~x switch{-5=>1,_=>2}", "1\n")]
    [InlineData("++x switch{5=>1,_=>2}", "1\n")]
    [InlineData("x++ switch{4=>1,_=>2}", "1\n")]
    [InlineData("(int)x switch{4=>1,_=>2}", "1\n")]
    [InlineData("x switch{4=>1,_=>2} switch{1=>3,_=>4}", "3\n")]
    [InlineData("(6+x) switch{10=>1,_=>2}", "1\n")]
    public async Task Round11_SwitchOperand_PreservesUnaryAndExplicitGrouping(string expression, string expected)
    {
        await AssertDifferential("WriteLine(F(4));static int F(int x){return " + expression + ";}", expected);
    }

    [Fact]
    public async Task Round11_SwitchPrecedence_PreservesEvaluationOrderAndSingleEvaluation()
    {
        const string source = "WriteLine(F());static int F(){return Mark(6)%Mark(4) switch{4=>Mark(3),_=>Mark(2)};}static int Mark(int value){WriteLine(value);return value;}";
        await AssertDifferential(source, "6\n4\n3\n0\n");
    }

    [Fact]
    public async Task Round11_RangeOperands_KeepExplicitArithmeticGrouping()
    {
        const string source = "ReadOnlySpan<int> values=[1,2,3,4,5];int start=0;int end=4;ReadOnlySpan<int> first=values[(start+1)..(end-1)];ReadOnlySpan<int> second=values[..^1];WriteLine(first[0]);WriteLine(first.Length);WriteLine(second.Length);";
        await AssertDifferential(source, "2\n2\n4\n");
    }

    [Theory]
    [InlineData("(int)-2", "-2\n")]
    [InlineData("(int)+2", "2\n")]
    [InlineData("(System.Int32)(-2)", "-2\n")]
    [InlineData("(global::System.Int32)(-2)", "-2\n")]
    public async Task Round11_CastDisambiguation_PreservesValidCasts(string expression, string expected)
    {
        await AssertDifferential("WriteLine((long)(" + expression + "));", expected);
    }

    [Theory]
    [InlineData("(nint)~2")]
    [InlineData("(nint)(-2)")]
    [InlineData("(nint)(+2)")]
    public async Task Round12_NativeIntegerCasts_AreRejected(string expression)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine((long)(" + expression + "));", 17);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("dynamic")]
    [InlineData("nint")]
    [InlineData("nuint")]
    public async Task Round11_ParenthesizedName_RemainsAnExpressionDespiteKnownType(string name)
    {
        await AssertDifferential("int " + name + "=5;WriteLine((" + name + ")-2);WriteLine((" + name + ")+2);class C{}", "3\n7\n");
    }

    [Theory]
    [InlineData("(nint)-2", "CS0075")]
    [InlineData("(nint)+2", "CS0119")]
    [InlineData("(nuint)-2", "CS0075")]
    [InlineData("(nuint)+2", "CS0119")]
    [InlineData("(System.Int32)-2", "CS0075")]
    [InlineData("(System.Int32)+2", "CS0119")]
    [InlineData("(C)-2", "CS0075")]
    [InlineData("(C)+2", "CS0119")]
    [InlineData("(global::System.Int32)-2", "CS0075")]
    [InlineData("(global::System.Int32)+2", "CS0119")]
    [InlineData("0.0%0.0 switch{_=>0}", "CS0266")]
    public void Round11_InvalidCastOrSwitchGrouping_HasCanonicalSourceDiagnostic(string expression, string roslynId)
    {
        AssertInvalidSource("WriteLine(F());static int F(){return " + expression + ";}class C{}", roslynId);
    }

    [Theory]
    [InlineData("values[1+1..3]")]
    [InlineData("values[1..2+1]")]
    public void Round11_RangeArithmetic_RequiresParenthesizedOperands(string expression)
    {
        AssertInvalidSource("ReadOnlySpan<int> values=[1,2,3,4];var result=" + expression + ";WriteLine(result.Length);", "CS0019");
    }

    public static IEnumerable<object[]> GrammarExpressions()
    {
        foreach (string op in new[] { "*", "/", "%", "+", "-", "<<", ">>", ">>>", "<", "<=", ">", ">=", "==", "!=", "&", "^", "|", "&&", "||", "??", "=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>=", "??=" })
        {
            yield return new object[] { "x " + op + " y switch{_=>z}" };
        }
        foreach (string expression in new[] { "-x switch{_=>y}", "+x switch{_=>y}", "!x switch{_=>y}", "~x switch{_=>y}", "++x switch{_=>y}", "--x switch{_=>y}", "x++ switch{_=>y}", "x-- switch{_=>y}", "^x switch{_=>y}", "(C)x switch{_=>y}", "x switch{_=>y} switch{_=>z}", "x is int switch{_=>y}", "x..y switch{_=>z}", "x.. switch{_=>z}", "..y switch{_=>z}", ".. switch{_=>z}", "x+y..z", "x..y+z", "x*y..z", "x..y*z", "(x+y)..(z+q)", "x..^y", "x?..y:z", "x?(y..z):(q..r)", "x?.5:1", "x?.0:1", "x?.9:1", "x is int value switch{_=>y}", "(x is int) switch{_=>y}", "x is (int) switch{_=>y}", "(x is (int)) switch{_=>y}", "x is {} switch{_=>y}", "x is int+1" })
        {
            yield return new object[] { expression };
        }
        foreach (string type in new[] { "nint", "nuint", "dynamic", "C", "Ns.C", "C<int>", "C<C<int>>", "global::System.Int32", "global::C", "global::C<Ns.C>", "global::C<int>.D", "int", "int?", "C[]" })
        {
            foreach (string tail in new[] { "+2", "-2", "~x", "!x", "(x)", "x", "2", "^x", "++x", "--x", "[x]", "[]", "switch{_=>x}" })
            {
                yield return new object[] { "(" + type + ")" + tail };
            }
        }
    }

    [Theory]
    [MemberData(nameof(GrammarExpressions))]
    public void Round11_ExpressionGrammar_AgreesWithPinnedRoslyn(string expression)
    {
        var reference = SyntaxFactory.ParseExpression(expression, options: new CSharpParseOptions(LanguageVersion.CSharp15));
        string[] actual = (string[])s_parserProbe.Value.Invoke(null, new object[] { expression })!;
        if (reference.ContainsDiagnostics)
        {
            Assert.NotEqual("", actual[1]);
            return;
        }
        Assert.Equal(Shape(reference), actual[0]);
    }

    [Theory]
    [InlineData("x is (int)", "0")]
    [InlineData("x is int _", "1")]
    [InlineData("x is int q", "1")]
    public void Round11_TypePatternMetadata_DistinguishesExplicitDesignations(string expression, string expectedDesignationFlags)
    {
        string[] actual = (string[])s_parserProbe.Value.Invoke(null, new object[] { expression })!;
        Assert.Empty(actual[1]);
        Assert.Equal(expectedDesignationFlags, actual[2]);
    }

    [Fact]
    public async Task Round11_ConditionalDotDigit_RemainsANumericTrueBranch()
    {
        await AssertDifferential("WriteLine((int)(F(true)*10));WriteLine((int)(F(false)*10));static double F(bool c){return c?.5:1;}", "5\n10\n");
    }

    [Fact]
    public void Round11_WithExpression_RemainsOutsideSubset()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-with-{Guid.NewGuid():N}"); Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, "var s=new S(1);var t=s with{X=2};WriteLine(t.X);public struct S(int x){public int X=x;}");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "WithBoundary", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            Assert.Contains(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics, d => d.Id == "VIB000" && d.Message.Contains("'with'", StringComparison.Ordinal));
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit); Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static void AssertInvalidSource(string source, string expectedId)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-invalid-{Guid.NewGuid():N}"); Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs"); File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "InvalidGrammar", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == expectedId && d.Severity == DiagnosticSeverity.Error);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit); Assert.Contains("VIB000", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static string Shape(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax p => Shape(p.Expression),
        IdentifierNameSyntax i => "id:" + i.Identifier.ValueText,
        AliasQualifiedNameSyntax a => Shape(a.Name),
        GenericNameSyntax g => "id:" + g.Identifier.ValueText,
        LiteralExpressionSyntax l => l.Token.Text,
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.IsExpression) => "is(" + Shape(b.Left) + ")",
        BinaryExpressionSyntax b => "binary:" + b.OperatorToken.Text + "(" + Shape(b.Left) + "," + Shape(b.Right) + ")",
        AssignmentExpressionSyntax a => "binary:" + a.OperatorToken.Text + "(" + Shape(a.Left) + "," + Shape(a.Right) + ")",
        PrefixUnaryExpressionSyntax u => "unary:" + u.OperatorToken.Text + "(" + Shape(u.Operand) + ")",
        PostfixUnaryExpressionSyntax u => "post:" + u.OperatorToken.Text + "(" + Shape(u.Operand) + ")",
        CastExpressionSyntax c => "cast(" + Shape(c.Expression) + ")",
        SwitchExpressionSyntax s => "switch(" + Shape(s.GoverningExpression) + ")",
        RangeExpressionSyntax r => "range(" + (r.LeftOperand is null ? "_" : Shape(r.LeftOperand)) + "," + (r.RightOperand is null ? "_" : Shape(r.RightOperand)) + ")",
        IsPatternExpressionSyntax p => "is(" + Shape(p.Expression) + ")",
        ConditionalExpressionSyntax c => "conditional(" + Shape(c.Condition) + "," + Shape(c.WhenTrue) + "," + Shape(c.WhenFalse) + ")",
        MemberAccessExpressionSyntax m => "member(" + Shape(m.Expression) + "," + m.Name.Identifier.ValueText + ")",
        ElementAccessExpressionSyntax e => "index(" + Shape(e.Expression) + "," + string.Join(",", e.ArgumentList.Arguments.Select(a => Shape(a.Expression))) + ")",
        CollectionExpressionSyntax c => "collection(" + string.Join(",", c.Elements.Cast<ExpressionElementSyntax>().Select(e => Shape(e.Expression))) + ")",
        _ => expression.Kind().ToString(),
    };

    private static readonly Lazy<MethodInfo> s_parserProbe = new(CreateParserProbe);

    private static MethodInfo CreateParserProbe()
    {
        const string harness = """
            using System;
            using System.Text;
            using System.Collections.Generic;
            using Vibranium;
            using Vibranium.Std;
            using Vibranium.Self;
            public static class ParserProbe
            {
                public static string[] Inspect(string expression)
                {
                    string text="var result="+expression+";class C{}class C<T>{}namespace Ns{class C{}}";
                    var sources=new SourceTable(1);sources.AddFile("probe.cs"u8,Encoding.UTF8.GetBytes(text),UnitKinds.App);
                    var names=new Interner(64);var tokens=new TokenArena(64);var ast=new AstArena(64);var diagnostics=new DiagnosticBag(8);
                    Lexer.LexAll(ref sources,ref names,ref tokens,ref diagnostics);Parser.ParseAll(in sources,in tokens,ref names,ref ast,ref diagnostics);
                    string Name(int id)=>Encoding.UTF8.GetString(names.Text(id));
                    string TokenText(int index)
                    {
                        var token=tokens.Tokens[index];return text.Substring(token.Start,token.Length);
                    }
                    string Walk(int id)
                    {
                        if(id<0)return "_";
                        var n=ast.Nodes[id];
                        switch(n.Kind)
                        {
                            case AstKind.ExprIdent:return "id:"+Name(n.A);
                            case AstKind.ExprIntLit:case AstKind.ExprFloatLit:case AstKind.ExprBoolLit:return TokenText(n.TokIdx);
                            case AstKind.ExprBinary:case AstKind.ExprAssign:return "binary:"+TokenText(n.TokIdx)+"("+Walk(n.B)+","+Walk(n.C)+")";
                            case AstKind.ExprUnary:return (n.C==1?"post:":"unary:")+TokenText(n.TokIdx)+"("+Walk(n.B)+")";
                            case AstKind.ExprFromEnd:return "unary:^("+Walk(n.A)+")";
                            case AstKind.ExprCast:return "cast("+Walk(n.B)+")";
                            case AstKind.ExprSwitch:return "switch("+Walk(n.A)+")";
                            case AstKind.ExprRange:return "range("+Walk(n.A)+","+Walk(n.B)+")";
                            case AstKind.ExprIsPattern:return "is("+Walk(n.A)+")";
                            case AstKind.ExprCond:return "conditional("+Walk(n.A)+","+Walk(n.B)+","+Walk(n.C)+")";
                            case AstKind.ExprMember:return "member("+Walk(n.A)+","+Name(n.B)+")";
                            case AstKind.ExprIndex:
                            {
                                var args=new List<string>();for(int i=0;i<n.C;i++)args.Add(Walk(ast.Nodes[ast.Extra[n.B+i]].B));
                                return "index("+Walk(n.A)+","+string.Join(",",args)+")";
                            }
                            case AstKind.ExprCollLit:
                            {
                                var args=new List<string>();for(int i=0;i<n.B;i++)args.Add(Walk(ast.Extra[n.A+i]));
                                return "collection("+string.Join(",",args)+")";
                            }
                            default:return n.Kind.ToString();
                        }
                    }
                    var messages=new StringBuilder();for(int i=0;i<diagnostics.Items.Count;i++)messages.Append(diagnostics.Items[i].Code).Append(':').Append(Encoding.UTF8.GetString(diagnostics.MsgPool.Get(diagnostics.Items[i].Message))).Append('|');
                    var designationFlags=new List<string>();
                    for(int i=0;i<ast.Nodes.Count;i++)
                    {
                        if(ast.Nodes[i].Kind==AstKind.PatDecl)designationFlags.Add(ast.Nodes[i].D.ToString());
                    }
                    return new[]{Walk(ast.Nodes[ast.TopStmts[0]].C),messages.ToString(),string.Join(",",designationFlags)};
                }
            }
            """;
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var refs = platform.Concat(new[] { typeof(Vibranium.Option<>).Assembly.Location, typeof(Vibranium.Std.StringBuf).Assembly.Location, SelfCompiler }).Distinct(StringComparer.Ordinal).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Round11ParserProbe", new[] { CSharpSyntaxTree.ParseText(harness, new CSharpParseOptions(LanguageVersion.CSharp15)) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream(); var emit = compilation.Emit(image); Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        _ = Assembly.LoadFrom(SelfCompiler); return Assembly.Load(image.ToArray()).GetType("ParserProbe")!.GetMethod("Inspect")!;
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-parser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round11ParserManaged", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round11ParserManaged.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round11ParserManaged.runtimeconfig.json"), """
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
                foreach (string mode in new[] { "O0", "O3", "asan" })
                {
                    string native = ir + "." + mode;
                    var args = new List<string> { mode == "O0" ? "-O0" : "-O3", ir, "-o", native };
                    if (mode == "asan") args.Add("-fsanitize=address,leak");
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
