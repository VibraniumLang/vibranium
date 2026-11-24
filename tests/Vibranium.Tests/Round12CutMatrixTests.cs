using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12CutMatrixTests
{
    private static readonly IReadOnlyDictionary<int, string> Messages = new Dictionary<int, string>
    {
        [10] = "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter.",
        [11] = "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard.",
        [12] = "Optional parameters are not supported in Vibranium v0.2; declare an overload instead.",
        [13] = "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order.",
        [14] = "Assigning to this is not supported in Vibranium v0.2; assign the fields individually.",
        [15] = "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement.",
        [16] = "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first.",
        [17] = "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong.",
        [18] = "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }.",
        [19] = "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition.",
        [20] = "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition.",
    };

    // Every baseline row contains two occurrences. Reuse the same declaration in
    // two independent source files to prove the budget is per cut AND per file.
    public static IEnumerable<object[]> RepeatedCuts()
    {
        yield return new object[] { 10, "class TYPE{public static int F(/*cut*/ref readonly int x,ref readonly int y)=>x+y;}", "ref" };
        yield return new object[] { 11, "class TYPE{public static int F(int /*cut*/_)=>_;public static int G(int _)=>_;}", "_" };
        yield return new object[] { 12, "class TYPE{public static int F(/*cut*/int x=1,int y=2)=>x+y;}", "int" };
        yield return new object[] { 13, "class TYPE{public static int F(int x,int y)=>x+y;public static int G()=>F(/*cut*/y:2,x:1)+F(y:4,x:3);}", "y" };
        yield return new object[] { 14, "struct TYPE{public int X;public void F(){/*cut*/this=default;this=default;}}", "this" };
        yield return new object[] { 15, "class TYPE{public static int F(bool c){int a=1;int b=2;ref int r=ref(c /*cut*/? ref a:ref b);ref int s=ref(c?ref b:ref a);return r+s;}}", "?" };
        yield return new object[] { 16, "class TYPE{public static int F(bool c){int[] a=c?/*cut*/[1]:[2];return a[0];}}", "[" };
        yield return new object[] { 17, "class TYPE{public static long F(){/*cut*/nint a=1;nuint b=2;return (long)a+(long)b;}}", "nint" };
        yield return new object[] { 18, "class TYPE{public static int F(int x){switch(x){case 0:/*cut*/int a=1;return a;default:int b=2;return b;}}}", "int" };
        yield return new object[] { 19, "class TYPE{public static int F(){if(/*cut*/true)return 1;if(false)return 2;return 3;}}", "true" };
        yield return new object[] { 20, "class TYPE{public static bool F(int x)=>x is /*cut*/> 0 and < 10;}", ">" };
    }

    public static IEnumerable<object[]> CutForms()
    {
        yield return new object[] { 10, "class P{public P(/*cut*/ref readonly int x){}}", "ref" };
        yield return new object[] { 10, "class P{public static int F(){int x=2;return G(in x);static int G(/*cut*/ref readonly int y)=>y;}}", "ref" };
        yield return new object[] { 10, "static class P{public static int F(this /*cut*/ref readonly int x)=>x;}", "ref" };
        yield return new object[] { 11, "class P{public static int F(){int /*cut*/_=1;return _;}}", "_" };
        yield return new object[] { 11, "class P{public static int F(){int /*cut*/\\u005F=1;return \\u005F;}}", "\\u005F" };
        yield return new object[] { 11, "class P{public int /*cut*/_=1;}", "_" };
        yield return new object[] { 11, "class P{public int /*cut*/_=>1;}", "_" };
        yield return new object[] { 11, "class P{public int /*cut*/_()=>1;}", "_" };
        yield return new object[] { 11, "class /*cut*/_{}", "_" };
        yield return new object[] { 11, "class P</*cut*/_>{}", "_" };
        yield return new object[] { 11, "class P{public static int F(){goto _;/*cut*/_:return 1;}}", "_" };
        yield return new object[] { 11, "class P{public static int F(){Vec<int> v=[1];int n=0;foreach(int /*cut*/_ in v)n+=_;return n;}}", "_" };
        yield return new object[] { 11, "class P{public static int F(){int.TryParse(\"1\",out int /*cut*/@_);return @_;}}", "@_" };
        yield return new object[] { 11, "class P{public static int F(int x)=>x is int /*cut*/@_?@_:0;}", "@_" };
        yield return new object[] { 11, "class P{public static int F(){var (/*cut*/@_,y)=(1,2);return @_+y;}}", "@_" };
        yield return new object[] { 11, "namespace /*cut*/_{class P{}}", "_" };
        yield return new object[] { 11, "using /*cut*/_=System.Int32;class P{}", "_" };
        yield return new object[] { 11, "using System.Linq;class P{public static int F(){int[] a=[1];return (from /*cut*/_ in a select _).First();}}", "_" };
        yield return new object[] { 11, "class P{public static (int /*cut*/_,int y) F()=>(1,2);}", "_" };
        yield return new object[] { 11, "class P{public static int F(){var pair=(/*cut*/_:1,y:2);return pair._;}}", "_" };
        yield return new object[] { 11, "class P{public static int F(){var value=new{/*cut*/_=1};return value._;}}", "_" };
        yield return new object[] { 12, "class P{public static int F(/*cut*/[System.Runtime.InteropServices.Optional]int x)=>x;}", "[" };
        yield return new object[] { 12, "class P{public static int F(/*cut*/[System.Runtime.InteropServices.DefaultParameterValue(3)]int x)=>x;}", "[" };
        yield return new object[] { 12, "using Opt=System.Runtime.InteropServices.OptionalAttribute;class P{public static int F(/*cut*/[Opt]int x)=>x;}", "[" };
        yield return new object[] { 12, "class P{public P(/*cut*/int x=0){}}", "int" };
        yield return new object[] { 13, "class P{public P(int x,int y){}public static P F()=>new P(/*cut*/y:2,x:1);}", "y" };
        yield return new object[] { 13, "class P{public int this[int x,int y]=>x+y;public int F()=>this[/*cut*/y:2,x:1];}", "y" };
        yield return new object[] { 13, "static class P{public static int F(this int value,int x,int y)=>value+x+y;public static int G()=>(1).F(/*cut*/y:2,x:1);}", "y" };
        yield return new object[] { 14, "struct P{public int X;public void F(){Take(ref /*cut*/this);}public static void Take(ref P p){p.X=2;}}", "this" };
        yield return new object[] { 14, "struct P{public int X;public void F(){Take(out /*cut*/this);}public static void Take(out P p){p=default;}}", "this" };
        yield return new object[] { 14, "struct P{public int X;public void F(){ref P p=ref /*cut*/this;p.X=2;}}", "this" };
        yield return new object[] { 14, "struct P{public int X;public void F(){(/*cut*/this,X)=(default(P),2);}}", "this" };
        yield return new object[] { 14, "struct P{public static P operator ++(P p)=>p;public void F(){/*cut*/this++;}}", "this" };
        yield return new object[] { 14, "struct P{public static P operator --(P p)=>p;public void F(){--/*cut*/this;}}", "this" };
        yield return new object[] { 15, "class P{public static ref int F(bool c,ref int a,ref int b)=>ref(c /*cut*/? ref a:ref b);}", "?" };
        yield return new object[] { 16, "class P{public static int F(){int[][] a=[/*cut*/[1],[2]];return a[0][0];}}", "[" };
        yield return new object[] { 16, "class P{public static int F(){int[] a=[..(int[])/*cut*/[1]];return a[0];}}", "[" };
        yield return new object[] { 16, "class P{public static int F(int x){int[] a=x switch{0=>/*cut*/[1],_=>[2]};return a[0];}}", "[" };
        yield return new object[] { 17, "class P{public static ulong F(/*cut*/nuint x)=>(ulong)x;}", "nuint" };
        yield return new object[] { 17, "class P{public static long F()=> (long)(/*cut*/nint)1;}", "nint" };
        yield return new object[] { 17, "class P{public static long F()=> (long)/*cut*/nint.MaxValue;}", "nint" };
        yield return new object[] { 17, "class P{public static long F()=>(long)/*cut*/System.IntPtr.MaxValue;}", "System" };
        yield return new object[] { 17, "class P{public static /*cut*/System.IntPtr F()=>default;}", "System" };
        yield return new object[] { 17, "using Alias=/*cut*/System.UIntPtr;class P{public static Alias F()=>default;}", "System" };
        yield return new object[] { 17, "class P{public static Vec</*cut*/nint> F()=>[];}", "nint" };
        yield return new object[] { 18, "class P{public static int F(int x){switch(x){case 0:/*cut*/int.TryParse(\"1\",out var n);return n;default:return 2;}}}", "int" };
        yield return new object[] { 18, "class P{public static int F(int x){switch(x){case 0:/*cut*/if(int.TryParse(\"1\",out var n))return n;return 0;default:return 2;}}}", "if" };
        yield return new object[] { 18, "class P{public static int F(int x){switch(x){case 0:/*cut*/var (a,b)=(1,2);return a+b;default:return 2;}}}", "var" };
        yield return new object[] { 18, "class P{public static bool F(int x){switch(x){case 0:/*cut*/_ = x is var n;return n==0;default:return false;}}}", "_" };
        yield return new object[] { 18, "class P{public static int F(int x){switch(x){case 1:return L();default:/*cut*/static int L()=>5;return 0;}}}", "static" };
        yield return new object[] { 19, "class P{public static int F(){const bool Flag=true;if(/*cut*/Flag)return 1;return 0;}}", "Flag" };
        yield return new object[] { 19, "class P{public const bool Flag=true;public static int F(){if(/*cut*/Flag)return 1;return 0;}}", "Flag" };
        yield return new object[] { 19, "class P{public static int F(){if(/*cut*/sizeof(int)==4)return 1;return 0;}}", "sizeof" };
        yield return new object[] { 19, "class P{public static int F(){if(/*cut*/nameof(F)==\"F\")return 1;return 0;}}", "nameof" };
        yield return new object[] { 19, "class P{public static int F(){if(/*cut*/!true)return 1;return 0;}}", "!" };
        yield return new object[] { 19, "class P{public static int F(){while(/*cut*/false){}return 0;}}", "false" };
        yield return new object[] { 19, "class P{public static int F(){do{}while(/*cut*/1==2);return 0;}}", "1" };
        yield return new object[] { 19, "class P{public static int F(){for(;/*cut*/false;){}return 0;}}", "false" };
        yield return new object[] { 19, "class P{public static int F()=>/*cut*/1==1?2:3;}", "1" };
        yield return new object[] { 19, "class P{public static int F(int x){switch(x){case 0 when /*cut*/true:return 1;default:return 0;}}}", "true" };
        yield return new object[] { 19, "class P{public static int F(int x)=>x switch{0 when /*cut*/false=>1,_=>0};}", "false" };
        yield return new object[] { 19, "class P{public static int F(){switch(/*cut*/1){case 1:return 1;default:return 0;}}}", "1" };
        yield return new object[] { 19, "class P{public static int F()=>/*cut*/1 switch{1=>2,_=>0};}", "1" };
        yield return new object[] { 19, "class P{public static int F(){while(/*cut*/(true)){return 1;}}}", "(" };
        yield return new object[] { 20, "class P{public static bool F(int x)=>x is not 0 /*cut*/and not 1;}", "and" };
        yield return new object[] { 20, "class P{public static int F(int x){switch(x){case /*cut*/<= 0:return 1;default:return 0;}}}", "<=" };
        yield return new object[] { 20, "class P{public static int F(int x)=>x switch{/*cut*/>= 0=>1,_=>0};}", ">=" };
        yield return new object[] { 20, "class P{public int X;public static bool F(P p)=>p is {X:/*cut*/> 0};}", ">" };
    }

    [Theory]
    [MemberData(nameof(RepeatedCuts))]
    public Task EveryCutReportsOncePerFileAtItsFirstOccurrence(int cut, string source, string token) =>
        AssertCut(cut, source, token, multipleFiles: true, generated: false);

    [Theory]
    [MemberData(nameof(CutForms))]
    public Task EveryConstructUsesTheCanonicalCutAndAnchor(int cut, string source, string token) =>
        AssertCut(cut, source, token, multipleFiles: false, generated: false);

    [Fact]
    public async Task DistinctCutsAtTheSameTokenDoNotCollapse()
    {
        const string source = "class P{public static long F(/*cut*/nint x=0)=>(long)x;}";
        await AssertCut(12, source, "nint", multipleFiles: false, generated: false);
        await AssertCut(17, source, "nint", multipleFiles: false, generated: false);
        const string switchSource = "class P{public static long F(int n){switch(n){case 0:/*cut*/nint x=1;return (long)x;default:return 0;}}}";
        await AssertCut(17, switchSource, "nint", multipleFiles: false, generated: false);
        await AssertCut(18, switchSource, "nint", multipleFiles: false, generated: false);
    }

    [Fact]
    public Task ActiveCodeCutsSurviveConditionalCompilationCut() => AssertCut(10,
        "#if true\nclass P{public static int F(/*cut*/ref readonly int x)=>x;}\n#endif\n",
        "ref", multipleFiles: false, generated: false);

    [Theory]
    [MemberData(nameof(RepeatedCuts))]
    public Task GeneratedCodeDoesNotEscapeAnyCut(int cut, string source, string token) =>
        AssertCut(cut, "// <auto-generated/>\n" + source, token, multipleFiles: false, generated: true);

    private static async Task AssertCut(int cut, string markedSource, string token, bool multipleFiles, bool generated)
    {
        const string marker = "/*cut*/";
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-cut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string[] names = multipleFiles ? new[] { "First.cs", "Second.cs" } : new[] { generated ? "First.g.cs" : "First.cs" };
            var positions = new Dictionary<string, int>();
            var sources = new Dictionary<string, string>();
            foreach (string name in names)
            {
                string marked = markedSource.Replace("TYPE", Path.GetFileNameWithoutExtension(name).Replace(".g", "", StringComparison.Ordinal), StringComparison.Ordinal);
                int position = marked.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(position >= 0);
                string source = marked.Replace(marker, "", StringComparison.Ordinal);
                positions[name] = position;
                sources[name] = source;
                File.WriteAllText(Path.Combine(temp, name), source);
            }
            File.WriteAllText(Path.Combine(temp, "Program.cs"), "WriteLine(0);");
            File.WriteAllText(Path.Combine(temp, "vib.manifest"), "unit app\nsource Program.cs\n" + string.Concat(names.Select(name => $"source {name}\n")));
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(temp, "Round12Cuts", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            if (generated) compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new GeneratedTrees()));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            string message = Messages[cut];
            var stageCuts = stage0.Where(diagnostic => diagnostic.Message == message).ToArray();
            var analyzerCuts = analyzer.Where(diagnostic => diagnostic.GetMessage() == message).ToArray();
            Assert.Equal(names.Length, stageCuts.Length);
            Assert.Equal(names.Length, analyzerCuts.Length);
            foreach (string name in names)
            {
                var diagnostic = Assert.Single(stageCuts, value => Path.GetFileName(value.Span.FilePath) == name);
                Assert.Equal("VIB000", diagnostic.Id);
                Assert.Equal(positions[name], diagnostic.Span.Start);
                Assert.Equal(token.Length, diagnostic.Span.Length);
                var live = Assert.Single(analyzerCuts, value => Path.GetFileName(value.Location.SourceTree!.FilePath) == name);
                Assert.Equal("VIB000", live.Id);
                Assert.Equal(diagnostic.Span.Start, live.Location.SourceSpan.Start);
                Assert.Equal(diagnostic.Span.Length, live.Location.SourceSpan.Length);
            }
            // Secondary diagnostics on invalid programs are deliberately not part
            // of the policy. Compare every *cut* produced, not unrelated cascades.
            Assert.Equal(stage0.Where(diagnostic => Messages.Values.Contains(diagnostic.Message))
                    .Select(diagnostic => (Path.GetFileName(diagnostic.Span.FilePath), diagnostic.Span.Start, diagnostic.Message)).OrderBy(value => value),
                analyzer.Where(diagnostic => Messages.Values.Contains(diagnostic.GetMessage()))
                    .Select(diagnostic => (Path.GetFileName(diagnostic.Location.SourceTree!.FilePath), diagnostic.Location.SourceSpan.Start, diagnostic.GetMessage())).OrderBy(value => value));
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", temp, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            string output = self.Out + self.Err;
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(names.Length, Regex.Matches(output, Regex.Escape(message)).Count);
            foreach (string name in names)
            {
                int position = positions[name];
                string source = sources[name];
                int line = 1 + source[..position].Count(character => character == '\n');
                int column = position - source.LastIndexOf('\n', position);
                Assert.Contains($"{name}({line},{column}): error VIB000: {message}", output, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("Program.cs(", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("int x=4;WriteLine(Read(in x));static int Read(in int value)=>value;", "4\n")]
    [InlineData("int x=5;ref readonly int r=ref Read(in x);WriteLine(r);static ref readonly int Read(in int value)=>ref value;", "5\n")]
    [InlineData("_ = 1;int.TryParse(\"2\".AsUtf8(),out _);int.TryParse(\"3\".AsUtf8(),out var _);int x=4;WriteLine(x is var _);WriteLine(x switch{_=>7});", "True\n7\n")]
    [InlineData("WriteLine(P.F(2));WriteLine(P.F(2,3));static class P{public static int F(int x)=>F(x,1);public static int F(int x,int y)=>x+y;}", "3\n5\n")]
    [InlineData("WriteLine(F(x:2,y:3));static int F(int x,int y)=>x*10+y;", "23\n")]
    [InlineData("WriteLine((1).F(x:2,y:3));static class P{public static int F(this int value,int x,int y)=>value+x+y;}", "6\n")]
    [InlineData("var p=new P();p.Set(6);WriteLine(p.Read());struct P{public int X;[Mut]public void Set(int x){this.X=x;}public readonly int Read(){var copy=this;return copy.X;}}", "6\n")]
    [InlineData("var p=new P();p.X=7;WriteLine(p.Read());struct P{public int X;public readonly int Read(){ref readonly P copy=ref this;return copy.X;}}", "7\n")]
    [InlineData("bool choose=true;int a=1;int b=2;if(choose)a=3;else b=4;WriteLine(a+b);", "5\n")]
    [InlineData("Vec<int> a=[1];Vec<int> b=[2];bool choose=true;if(choose)WriteLine(a[0]);else WriteLine(b[0]);", "1\n")]
    [InlineData("ReadOnlySpan<int> a=[1];ReadOnlySpan<int> b=[2];bool choose=true;ReadOnlySpan<int> c=choose?a:b;WriteLine(c[0]);", "1\n")]
    [InlineData("var value=new IntPtr();WriteLine(value.X);class IntPtr{public int X=7;}", "7\n")]
    [InlineData("int nint=3;int nuint=4;WriteLine(nint+nuint);", "7\n")]
    [InlineData("long a=long.MaxValue;ulong b=ulong.MaxValue;WriteLine(a);WriteLine(b);", "9223372036854775807\n18446744073709551615\n")]
    [InlineData("int x=1;switch(x){case 1:{int value=7;WriteLine(value);break;}default:{WriteLine(0);break;}}", "7\n")]
    [InlineData("int x=1;switch(x){case 1:{if(int.TryParse(\"7\".AsUtf8(),out int value))WriteLine(value);break;}default:break;}", "7\n")]
    [InlineData("int x=3;switch(x){case var n when n>0:WriteLine(n);break;default:WriteLine(0);break;}", "3\n")]
    [InlineData("int n=0;while(true){n++;break;}do{n++;break;}while(true);for(;;){n++;break;}for(;true;){n++;break;}WriteLine(n);", "4\n")]
    [InlineData("int x=2;if(x>0&&1==1)WriteLine(x);WriteLine(x switch{2 when x>0=>3,_=>0});", "2\n3\n")]
    [InlineData("int x=2;WriteLine(x is 1 or 2);WriteLine(x is not 0);", "True\nTrue\n")]
    public Task NearMissControlsKeepManagedAndNativeParity(string source, string expected) =>
        Round11AuditAssertions.AssertDifferential(source, expected, includeUnoptimized: true);

    public static IEnumerable<object[]> SuppressedWarningCases()
    {
        foreach (var (id, source) in new[]
        {
            ("CS0183", "int x=1;WriteLine(x is int);"),
            ("CS0184", "int x=1;WriteLine(x is string);"),
            ("CS8509", "int x=Pick();int r=x switch{1=>10};WriteLine(r);static int Pick()=>2;"),
            ("CS8519", "WriteLine(0 is 1);"),
            ("CS8520", "WriteLine(0 is 0);"),
            ("CS8524", "E x=E.A;WriteLine(x switch{E.A=>1,E.B=>2});enum E{A,B}"),
            ("CS8778", "WriteLine((nint)2147483648L);"),
            ("CS8793", "WriteLine(0 is not 1);"),
            ("CS8794", "int x=1;WriteLine(x is 0 or not 0);"),
            ("CS8846", "bool x=Pick();bool guard=Pick();WriteLine(x switch{true=>1,false when guard=>2});static bool Pick()=>false;"),
            ("CS9191", "int x=1;WriteLine(F(ref x));static int F(in int value)=>value;"),
            ("CS9192", "int x=1;WriteLine(F(x));static int F(ref readonly int value)=>value;"),
            ("CS9193", "WriteLine(F(1));static int F(ref readonly int value)=>value;"),
        })
        foreach (string suppression in new[] { "none", "pragma-id", "pragma-all", "options", "tree", "global", "attribute" })
            yield return new object[] { id, source, suppression };
    }

    [Theory]
    [MemberData(nameof(SuppressedWarningCases))]
    public async Task EveryPromotedWarningFailsClosedInAllFrontends(string id, string source, string suppression)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-warning-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            source = suppression switch
            {
                "pragma-id" => $"#pragma warning disable {id}\n" + source,
                "pragma-all" => "#pragma warning disable\n" + source,
                _ => source,
            };
            if (suppression == "attribute")
            {
                // Member attributes are accepted by all three frontends. Do not
                // let an unsupported assembly-attribute target mask the warning.
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source).GetRoot();
                string statements = string.Concat(root.Members.OfType<GlobalStatementSyntax>().Select(value => value.ToFullString()));
                string declarations = string.Concat(root.Members.Where(value => value is not GlobalStatementSyntax).Select(value => value.ToFullString()));
                source = "WarningProbe.Run();public static class WarningProbe{" +
                    $"[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Compiler\",\"{id}\")]" +
                    "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Vibranium.Language\",\"VIB000\")]" +
                    "public static void Run(){" + statements + "}}" + declarations;
            }
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12Warnings", includeStd: true);
            if (suppression == "none") Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == id);
            if (suppression == "options") compilation = compilation.WithOptions(compilation.Options
                .WithGeneralDiagnosticOption(ReportDiagnostic.Suppress).WithWarningLevel(0)
                .WithSpecificDiagnosticOptions(compilation.Options.SpecificDiagnosticOptions.SetItem(id, ReportDiagnostic.Suppress)));
            if (suppression is "tree" or "global") compilation = compilation.WithOptions(compilation.Options
                .WithSyntaxTreeOptionsProvider(new SuppressedWarnings(suppression == "global")));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                !VibraniumFrontend.IsPromotedSemanticWarning(diagnostic.Id));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            Assert.NotEmpty(stage0);
            Assert.Equal(new[] { "VIB000" }, stage0.Select(diagnostic => diagnostic.Id).Distinct());
            if (id is not ("CS8778" or "CS9192" or "CS9193"))
                Assert.Contains(stage0, diagnostic => diagnostic.Message.StartsWith($"C# warning {id} is not permitted in Vibranium:", StringComparison.Ordinal));
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(stage0.Select(diagnostic => (diagnostic.Id, diagnostic.Message, diagnostic.Span.Start)).OrderBy(value => value),
                analyzer.Select(diagnostic => (diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location.SourceSpan.Start)).OrderBy(value => value));
            var emit = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(emit.Success);
            Assert.Null(emit.LlvmIr);
            Assert.Contains(emit.Errors, error => error.Contains("VIB000", StringComparison.Ordinal));
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(new[] { "VIB000" }, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(match => match.Value).Distinct());
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task SuppressMessageCannotSilenceOwnershipCompilerDiagnostics()
    {
        const string source = "P.Run();public static class P{[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Vibranium.Ownership\",\"VIB001\")]" +
            "public static void Run(){Vec<int> values=[1];var moved=values;WriteLine(values.Count);WriteLine(moved.Count);}}";
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-suppress-message-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12SuppressMessage", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { "VIB000", "VIB001" }, stage0.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal));
            Assert.Equal(stage0.Select(diagnostic => (diagnostic.Id, diagnostic.Message, diagnostic.Span.Start)).OrderBy(value => value.Start),
                analyzer.Select(diagnostic => (diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location.SourceSpan.Start)).OrderBy(value => value.Start));
            // F15 rejects the user attribute, but the attempted suppression must
            // not erase the original ownership error on either Roslyn frontend.
            Assert.Contains(stage0, diagnostic => diagnostic.Id == "VIB001");
            Assert.Contains(analyzer, diagnostic => diagnostic.Id == "VIB001");
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(new[] { "VIB000" }, Regex.Matches(self.Out + self.Err, @"\bVIB\d{3}(?=:)")
                .Select(match => match.Value).Distinct().OrderBy(id => id, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("#pragma warning disable\nvar holder=new Holder();WriteLine(holder.Value.X);class Node{public int X=1;}class Holder{public Node Value;}")]
    [InlineData("#pragma warning disable\nNode node=null;WriteLine(node.X);class Node{public int X=1;}")]
    public async Task NullableWarningsHaveIndependentSafetyBoundaries(string source)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-nullability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round12Nullability", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
            Assert.Contains(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics, diagnostic => diagnostic.Id == "VIB008");
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Contains(analyzer, diagnostic => diagnostic.Id == "VIB008");
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Contains("VIB008", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void CliCannotEmitNonExhaustiveSwitchWhenAllWarningsAreDisabled()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-switch-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            string binary = Path.Combine(temp, "rejected");
            File.WriteAllText(path, "#pragma warning disable\nint x=Pick();WriteLine(x switch{1=>10});static int Pick()=>2;");
            foreach (string compiler in new[] { typeof(VibraniumCompilerDriver).Assembly.Location, Round11AuditAssertions.SelfCompiler })
            foreach (string command in new[] { "check", "-O0", "-O3" })
            {
                var arguments = new List<string> { compiler, command == "check" ? "check" : "build", path,
                    "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src") };
                if (command != "check")
                {
                    arguments.Add(command);
                    arguments.Add("-o");
                    arguments.Add(binary);
                    if (command == "-O3") arguments.Add("--asan");
                }
                var result = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, arguments.ToArray());
                Assert.NotEqual(0, result.Exit);
                Assert.Contains("VIB000", result.Out + result.Err, StringComparison.Ordinal);
                Assert.False(File.Exists(binary));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void NoWarnEditorConfigAndSuppressMessageCannotDisableTheMsbuildBoundary()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-warning-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string core = System.Security.SecurityElement.Escape(typeof(Vibranium.Option<>).Assembly.Location)!;
            string analyzers = string.Concat(new[]
            {
                typeof(VibraniumDiagnosticAnalyzer).Assembly.Location,
                typeof(VibraniumFrontend).Assembly.Location,
                typeof(Vibranium.Analysis.BorrowAndMoveAnalyzer).Assembly.Location,
                typeof(Vibranium.Compiler.Core.MirModule).Assembly.Location,
            }.Select(path => $"<Analyzer Include=\"{System.Security.SecurityElement.Escape(path)}\" />"));
            File.WriteAllText(Path.Combine(temp, "Suppressed.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                <TargetFramework>net11.0</TargetFramework><LangVersion>15.0</LangVersion>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors><WarningsAsErrors></WarningsAsErrors>
                <WarningLevel>0</WarningLevel><NoWarn>CS8509;VIB000</NoWarn>
                </PropertyGroup><ItemGroup><Reference Include="Vibranium.Core"><HintPath>{core}</HintPath></Reference>{analyzers}</ItemGroup></Project>
                """);
            File.WriteAllText(Path.Combine(temp, ".editorconfig"), "root = true\n[*.cs]\ndotnet_diagnostic.CS8509.severity = none\ndotnet_diagnostic.VIB000.severity = none\n");
            File.WriteAllText(Path.Combine(temp, "Program.cs"), """
                #pragma warning disable
                public static class P
                {
                    [System.Diagnostics.CodeAnalysis.SuppressMessage("Compiler", "CS8509")]
                    [System.Diagnostics.CodeAnalysis.SuppressMessage("Vibranium.Language", "VIB000")]
                    public static int F(int x) => x switch { 1 => 10 };
                }
                """);
            var build = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe,
                "build", "--disable-build-servers", Path.Combine(temp, "Suppressed.csproj"), "--nologo");
            Assert.NotEqual(0, build.Exit);
            Assert.Contains("error VIB000", build.Out + build.Err, StringComparison.Ordinal);
            Assert.Contains("CS8509", build.Out + build.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("int.MaxValue")]
    [InlineData("0x7FFFFFC8")]
    public Task CapacityOverflowPanicsBeforeAttemptingManagedAllocation(string capacity) =>
        Round11AuditAssertions.AssertDifferential(
            $"Write(\"a\");var values=new Vec<int>(Id({capacity}));WriteLine(values.Count);static int Id(int value)=>value;",
            "a", expectedExit: 101, expectedError: "panic: Vec capacity overflow\n");

    [Theory]
    [InlineData("WriteLine(Dup(1));static T Dup<T>(in T x)=>x;")]
    [InlineData("WriteLine(Api.Dup(1));static class Api{public static T Dup<T>(in T x)=>x;}")]
    public Task OpenGenericDefinitionsAreCheckedEvenWhenOnlyCopyIsInstantiated(string source) =>
        Round11AuditAssertions.AssertRejected(source, "VIB005");

    [Fact]
    public Task BorrowRejectedForeachOwnerMoveDoesNotMoveTheOwner() =>
        Round11AuditAssertions.AssertRejected("StringBuf buf=new StringBuf();buf.Append(\"a\\nb\");foreach(var line in buf.Lines()){Sink(buf);}static void Sink(StringBuf value){}", "VIB003");

    [Theory]
    [InlineData("sealed class", false)]
    [InlineData("sealed class", true)]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    public async Task RejectedFieldMovesKeepTheirOwnFormDiagnosticWithoutConsumingTheOwner(string holderKind, bool liveView)
    {
        string source = $$"""
            WriteLine(F(3));
            static int F(int p)
            {
                var h = new Holder(p);
                int acc = 0;
                {{(liveView ? holderKind == "struct" ? "ReadOnlySpan<int> view = h.Items.AsReadOnlySpan();" : "ReadOnlySpan<int> view = h.View();" : "")}}
                if (p > 1)
                {
                    var items = h.Items;
                    acc += items[0];
                }
                {{(liveView ? "acc += view[0];" : "")}}
                acc += h.Items.Count;
                return acc;
            }
            public {{holderKind}} Holder
            {
                public Vec<int> Items;
                public Holder(int p) { Items = [p, 2]; }
                {{(holderKind == "struct" ? "" : "public ReadOnlySpan<int> View() => Items.AsReadOnlySpan();")}}
            }
            """;
        string[] expectedIds = liveView ? new[] { "VIB003", "VIB005" } : new[] { "VIB005" };
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-field-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "RejectedFieldMove", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            Assert.Equal(expectedIds, stage0.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(
                stage0.Select(diagnostic => (diagnostic.Id, diagnostic.Message, diagnostic.Span.Start)).OrderBy(diagnostic => diagnostic.Id).ThenBy(diagnostic => diagnostic.Start),
                analyzer.Select(diagnostic => (diagnostic.Id, Message: diagnostic.GetMessage(), Start: diagnostic.Location.SourceSpan.Start)).OrderBy(diagnostic => diagnostic.Id).ThenBy(diagnostic => diagnostic.Start));
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(expectedIds, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(match => match.Value).Distinct().OrderBy(id => id));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("WriteLine(Dup(1));static T Dup<T>(in T x) where T:unmanaged=>x;", "1\n")]
    [InlineData("Vec<int> value=[3];var result=Identity(value);WriteLine(result[0]);static T Identity<T>(T value)=>value;", "3\n")]
    [InlineData("StringBuf buf=new StringBuf();buf.Append(\"a\\nb\");foreach(var line in buf.Lines()){WriteLine(line);}Sink(buf);static void Sink(StringBuf value){WriteLine(value.Length);}", "a\nb\n3\n")]
    public Task CycleABoundaryControlsKeepDifferentialParity(string source, string expected) =>
        Round11AuditAssertions.AssertDifferential(source, expected);

    public static IEnumerable<object[]> NonCopySpanPrograms()
    {
        foreach (string span in new[] { "Span", "ReadOnlySpan" })
        foreach (string element in new[] { "Node", "Vec<int>", "StringBuf", "Option<Node>", "Owner", "Resource" })
            yield return new object[] { $"WriteLine(0);class Node{{public int X=1;}}struct Owner{{public Vec<int> Values;}}class Resource:System.IDisposable{{public void Dispose(){{}}}}static class Api{{public static void F({span}<{element}> values){{}}}}" };
        yield return new object[] { "Span<Node> values=[new Node()];WriteLine(values.Length);class Node{public int X=1;}" };
        yield return new object[] { "ReadOnlySpan<StringBuf> values=[new StringBuf()];WriteLine(values.Length);" };
        // Vec<Node> also violates the managed unmanaged constraint. The explicit
        // subset boundary must nevertheless recognize the inferred span result.
        yield return new object[] { "Vec<Node> values=new Vec<Node>();var span=values.AsSpan();WriteLine(span.Length);class Node{public int X=1;}" };
    }

    [Theory]
    [MemberData(nameof(NonCopySpanPrograms))]
    public async Task EveryNonCopySpanDeclarationAndExpressionIsRejected(string source)
    {
        const string message = "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2.";
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-span-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SpanBoundary", includeStd: true);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            Assert.Contains(stage0, diagnostic => diagnostic.Id == "VIB000" && diagnostic.Message == message);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Contains(analyzer, diagnostic => diagnostic.Id == "VIB000" && diagnostic.GetMessage() == message);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Contains(message, self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void PinnedRuntimeSourceHashesMatchEveryShippedSource()
    {
        string root = Path.Combine(Round11AuditAssertions.RepoRoot, "src");
        var files = Directory.EnumerateFiles(Path.Combine(root, "Vibranium.Std"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Append(Path.Combine(root, "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs"));
        foreach (string path in files)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
            Assert.True(VibraniumFrontend.IsAuthenticatedRuntimeSource(tree),
                $"Runtime source hash mismatch for {Path.GetRelativePath(root, path)}. After reviewing the change, run python3 src/Vibranium.Frontend.Roslyn/GenerateTrustedSourceHashes.py and commit the generated manifest.");
            Assert.False(VibraniumFrontend.IsAuthenticatedRuntimeSource(tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(tree.GetText() + "\n// modified"))));
            Assert.False(VibraniumFrontend.IsAuthenticatedRuntimeSource(tree.WithFilePath("Untrusted.cs")));
        }
    }

    [Theory]
    [InlineData("Vibranium.Core")]
    [InlineData("Vibranium.Std")]
    public async Task ReservedAssemblyNamesDoNotBypassOwnershipChecking(string assemblyName)
    {
        const string message = "The assembly names Vibranium.Core and Vibranium.Std are reserved for the Vibranium runtime and standard library.";
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-reserved-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, "Vec<int> v=[1];Consume(v);WriteLine(v.Count);static void Consume(Vec<int> value){}");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, assemblyName, includeStd: true);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            Assert.Contains(stage0, diagnostic => diagnostic.Id == "VIB000" && diagnostic.Message == message);
            Assert.Contains(stage0, diagnostic => diagnostic.Id == "VIB001");
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Contains(analyzer, diagnostic => diagnostic.Id == "VIB000" && diagnostic.GetMessage() == message);
            Assert.Contains(analyzer, diagnostic => diagnostic.Id == "VIB001");
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("Vibranium.Core")]
    [InlineData("Vibranium.Std")]
    public void ReservedAssemblyNamesFailInMsbuildAndTheirSourceFailsInCli(string assemblyName)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-reserved-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string analyzers = string.Concat(new[]
            {
                typeof(VibraniumDiagnosticAnalyzer).Assembly.Location,
                typeof(VibraniumFrontend).Assembly.Location,
                typeof(Vibranium.Analysis.BorrowAndMoveAnalyzer).Assembly.Location,
                typeof(Vibranium.Compiler.Core.MirModule).Assembly.Location,
            }.Select(path => $"<Analyzer Include=\"{System.Security.SecurityElement.Escape(path)}\" />"));
            string project = Path.Combine(temp, "Spoof.csproj");
            File.WriteAllText(project, $"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                <TargetFramework>net11.0</TargetFramework><LangVersion>15.0</LangVersion>
                <AssemblyName>{assemblyName}</AssemblyName><WarningLevel>0</WarningLevel><NoWarn>VIB000;VIB001</NoWarn>
                </PropertyGroup><ItemGroup>{analyzers}</ItemGroup></Project>
                """);
            File.WriteAllText(Path.Combine(temp, "Program.cs"), """
                #pragma warning disable
                public class Node { public int X=1; }
                public static class P
                {
                    public static void Main(){F();}
                    [System.Diagnostics.CodeAnalysis.SuppressMessage("Vibranium.Ownership", "VIB001")]
                    public static int F(){var node=new Node();Take(node);return node.X;}
                    static void Take(Node value){}
                }
                """);
            var build = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe,
                "build", "--disable-build-servers", project, "--nologo");
            string output = build.Out + build.Err;
            Assert.NotEqual(0, build.Exit);
            Assert.Contains("error VIB000", output, StringComparison.Ordinal);
            Assert.Contains("The assembly names Vibranium.Core and Vibranium.Std are reserved", output, StringComparison.Ordinal);
            Assert.Contains("error VIB001", output, StringComparison.Ordinal);
            foreach (string command in new[] { "check", "build" })
            {
                var arguments = new List<string> { typeof(VibraniumCompilerDriver).Assembly.Location, command,
                    Path.Combine(temp, "Program.cs"), "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src") };
                string binary = Path.Combine(temp, "rejected");
                if (command == "build") { arguments.Add("-o"); arguments.Add(binary); }
                var check = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, arguments.ToArray());
                Assert.NotEqual(0, check.Exit);
                Assert.Contains("VIB001", check.Out + check.Err, StringComparison.Ordinal);
                Assert.False(File.Exists(binary));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task SelectedSysrootTrustsModifiedStdByLocationRatherThanPinnedHash()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-selected-sysroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string original = Path.Combine(Round11AuditAssertions.RepoRoot, "src");
            string sysroot = Path.Combine(temp, "sysroot");
            foreach (string unit in new[] { "Vibranium.Core", "Vibranium.Std" })
            foreach (string file in Directory.EnumerateFiles(Path.Combine(original, unit), "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(original, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) continue;
                if (Path.GetExtension(file) is not (".cs" or ".ll") && Path.GetFileName(file) != "vib.manifest") continue;
                string target = Path.Combine(sysroot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string managedPath = Path.Combine(sysroot, "Vibranium.Std", "Sys", "Sys.Managed.cs");
            File.AppendAllText(managedPath, "\n// Explicitly selected modified sysroot.\n");
            Assert.False(VibraniumFrontend.IsAuthenticatedRuntimeSource(CSharpSyntaxTree.ParseText(File.ReadAllText(managedPath), path: managedPath)));
            string source = Path.Combine(temp, "Program.cs");
            File.WriteAllText(source, "WriteLine(1);");
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(source, "SelectedSysroot", includeStd: true, sysroot: sysroot);
            Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            foreach (string compiler in new[] { typeof(VibraniumCompilerDriver).Assembly.Location, Round11AuditAssertions.SelfCompiler })
            {
                var result = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, compiler, "check", source, "--sysroot", sysroot);
                Assert.True(result.Exit == 0, result.Out + result.Err);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("core")]
    [InlineData("std")]
    public void UserManifestsCannotClaimSysrootUnitIdentity(string unit)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-unit-spoof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "vib.manifest"), $"unit {unit}\nsource Program.cs\n");
            File.WriteAllText(Path.Combine(temp, "Program.cs"), "class P{public static int F(int x=1)=>x;}");
            foreach (string compiler in new[] { typeof(VibraniumCompilerDriver).Assembly.Location, Round11AuditAssertions.SelfCompiler })
            foreach (string command in new[] { "check", "build" })
            {
                var arguments = new List<string> { compiler, command, temp,
                    "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src") };
                string binary = Path.Combine(temp, "rejected");
                if (command == "build") { arguments.Add("-o"); arguments.Add(binary); }
                var result = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, arguments.ToArray());
                Assert.NotEqual(0, result.Exit);
                Assert.Contains("error VIB000: Only the selected sysroot manifests may declare core or std units.", result.Out + result.Err, StringComparison.Ordinal);
                Assert.False(File.Exists(binary));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("[System.Runtime.CompilerServices.CompilerGenerated]public static class P", "Program.cs")]
    [InlineData("namespace Vibranium.Internal;internal static class VibraniumAppBootstrap", "VibraniumAppBootstrap.cs")]
    [InlineData("namespace Microsoft.CodeAnalysis;internal static class EmbeddedAttribute", "EmbeddedAttribute.cs")]
    [InlineData("namespace System.Runtime.CompilerServices;internal static class NullableAttribute", "NullableAttribute.cs")]
    [InlineData("namespace Vibranium.Std;public static partial class Sys", "Vibranium.Std/Sys/Sys.Managed.cs")]
    public async Task NamesAttributesAndGeneratedPathsCannotGrantSourceTrust(string declaration, string name)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r12-spoof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, declaration + "{public static void F(){Vec<int> v=[1];Consume(v);WriteLine(v.Count);}static void Consume(Vec<int> value){}}");
            var initial = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SpoofedSource", includeStd: false);
            var tree = initial.SyntaxTrees.Single(value => value.FilePath == path);
            // A public API's proposed trust set is not authority by itself.
            var proposed = new HashSet<SyntaxTree> { tree };
            VibraniumFrontend.RegisterTrustedTrees(initial, proposed, proposed, proposed);
            var stage0 = VibraniumFrontend.CompileAndCheck(initial, proposed, proposed, proposed).Diagnostics;
            Assert.NotEmpty(stage0);
            if (!declaration.Contains("partial", StringComparison.Ordinal))
                Assert.Contains(stage0, diagnostic => diagnostic.Id == "VIB001");
            else
                Assert.Contains(stage0, diagnostic => diagnostic.Id == "VIB000");
            var analyzer = await initial.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.NotEmpty(analyzer);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("#pragma warning disable\nint x=2;WriteLine(x switch{1=>10,_=>20});", "20\n")]
    [InlineData("#pragma warning disable\nint n=0;if(false&&F())n++;WriteLine(n);static bool F()=>true;", "0\n")]
    [InlineData("#pragma warning disable CS9191\nint x=4;WriteLine(F(in x));static int F(in int value)=>value;", "4\n")]
    public Task SuppressionDoesNotRejectValidPrograms(string source, string expected) =>
        Round11AuditAssertions.AssertDifferential(source, expected, includeUnoptimized: true);

    private sealed class SuppressedWarnings(bool globalOnly) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.Unknown;
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Suppress;
            return !globalOnly;
        }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Suppress;
            return globalOnly;
        }
    }

    private sealed class GeneratedTrees : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.MarkedGenerated;
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }
    }
}
