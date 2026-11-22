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
using System.Text;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round11AuditParserTests
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



    private static string CutMessage(int cut) => cut switch
    {
        5 => "Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef) are not supported in Vibranium v0.2; .NET and native builds must compile the same code.",
        6 => "File-local types (file class, file struct) are not supported in Vibranium v0.2; declare the type without the file modifier.",
        7 => "Partial types and members are not supported in Vibranium v0.2.",
        8 => "Types nested inside a generic type are not supported in Vibranium v0.2; declare the type at namespace level.",
        9 => "Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method.",
        _ => throw new ArgumentOutOfRangeException(nameof(cut)),
    };

    public static IEnumerable<object[]> CutCases()
    {
        foreach (string directive in new[] { "if", "elif", "else", "endif", "define", "undef" })
        {
            yield return new object[] { 5, "#" + directive + " FLAG\nWriteLine(1);", directive };
        }
        yield return new object[] { 5, "#define FLAG\n#if FLAG\nWriteLine(1);\n#else\nWriteLine(2);\n#endif\n#if DEBUG\nWriteLine(3);\n#endif", "define" };
        yield return new object[] { 5, "#if false\nthis is not C# @@@\n#endif\nWriteLine(1);", "if" };
        yield return new object[] { 5, "class Incomplete {\n# if true\n", "if" };
        yield return new object[] { 6, "file static class Hidden {public static int K()=>7;}", "file" };
        yield return new object[] { 6, "// π\nfile struct Hidden {public int X;} file class Second{}", "file" };
        yield return new object[] { 6, "file enum Hidden{A}", "file" };
        yield return new object[] { 6, "file delegate int Hidden();", "file" };
        yield return new object[] { 8, "class G<T>{public delegate int Inner();}", "Inner" };
        yield return new object[] { 8, "class G<T>{public delegate ref readonly int Inner();}", "Inner" };
        yield return new object[] { 6, "file record Hidden(int X);", "file" };
        yield return new object[] { 6, "file record struct Hidden(int X);", "file" };
        yield return new object[] { 7, "partial record R;", "partial" };
        yield return new object[] { 7, "partial record class R;", "partial" };
        yield return new object[] { 7, "partial record struct R;", "partial" };
        yield return new object[] { 8, "class G<T>{record Inner;}", "Inner" };
        yield return new object[] { 8, "class G<T>{record struct Inner;}", "Inner" };
        yield return new object[] { 7, "partial class P{}", "partial" };
        yield return new object[] { 7, "partial struct P{} partial struct P{}", "partial" };
        yield return new object[] { 7, "static partial class M{public static partial int F(int x);}static partial class M{public static partial int F(int x)=>x;}", "partial" };
        yield return new object[] { 7, "class P{partial void F();}", "partial" };
        yield return new object[] { 7, "namespace Vibranium.Std;public static partial class Sys{[Native(\"__vib_sys_close\")]internal static partial void Close(int h);}", "partial" };
        yield return new object[] { 8, "class G<T>{public class Inner{}public struct Second{}}", "Inner" };
        yield return new object[] { 8, "class G<T>{public enum Inner{A}}", "Inner" };
        yield return new object[] { 8, "class G<T>{public class Inner<U>{public class Deep{}}}", "Inner" };
        yield return new object[] { 9, "class C{public C():this(1){}public C(int x):this(x,2){}public C(int x,int y){}}", "this" };
    }

    [Theory]
    [MemberData(nameof(CutCases))]
    public void Round11Audit_Cuts_HaveOneCanonicalDiagnosticAtFirstToken(int cut, string source, string anchor)
    {
        Assert.Equal(new[] { Expected(cut, source, anchor, 0) }, Inspect(source));
    }

    [Theory]
    [MemberData(nameof(CutCases))]
    public void Round11Audit_Cuts_AreDeduplicatedPerFileNotPerProgram(int cut, string source, string anchor)
    {
        string clean = "class Clean{public static int F()=>1;}";
        Assert.Equal(new[] { Expected(cut, source, anchor, 0), Expected(cut, source, anchor, 2) }, Inspect(source, clean, source));
    }

    [Fact]
    public void Round11Audit_DistinctNestedCuts_AllSurviveCascadeSuppression()
    {
        const string source = "partial class G<T>{class Inner{public Inner():this(1){}public Inner(int x){}}}file class Hidden{}";
        Assert.Equal(new[] { Expected(7,source,"partial",0), Expected(8,source,"Inner",0), Expected(9,source,"this",0), Expected(6,source,"file",0) }, Inspect(source));
    }

    [Fact]
    public void Round11Audit_CutDeclaration_SuppressesOnlyItsOwnCascades()
    {
        const string source = "[Native(\"bad\")] partial class P{protected void F(){}} unsafe class Other{}";
        string[] actual = Inspect(source);
        Assert.Equal(2, actual.Length);
        Assert.Equal(Expected(7,source,"partial",0), actual[0]);
        Assert.EndsWith("|'unsafe' modifier is banned in Vibranium.", actual[1], StringComparison.Ordinal);
        Assert.Contains("|"+source.IndexOf("unsafe",StringComparison.Ordinal)+"|6|",actual[1],StringComparison.Ordinal);
    }

    [Fact]
    public void Round11Audit_BaseInitializer_ReportsCutC21AtBaseToken()
    {
        const string source = "class C{public C():base(){}}";
        string diagnostic = Assert.Single(Inspect(source));
        Assert.EndsWith("|Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance.", diagnostic, StringComparison.Ordinal);
        Assert.Contains("|"+source.IndexOf("base",StringComparison.Ordinal)+"|", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("// #if DEBUG\n/* #define FLAG */\nWriteLine(1);", "1\n")]
    [InlineData("WriteLine(\"#if DEBUG\");", "#if DEBUG\n")]
    [InlineData("#nullable enable\n#pragma warning disable CS0168\n#region Keep\nWriteLine(7);\n#endregion", "7\n")]
    [InlineData("int file=2;int partial=3;WriteLine(file+partial);", "5\n")]
    [InlineData("var c=new @record();WriteLine(c.X);class @record{public int X=7;}", "7\n")]
    [InlineData("var c=new @file();WriteLine(c.X);class @file{public int X=7;}", "7\n")]
    [InlineData("WriteLine(Outer.Inner.F());class Outer{public static T Identity<T>(T x) where T:unmanaged=>x;public class Inner{public static int F()=>7;}}", "7\n")]
    [InlineData("var c=new C(2);WriteLine(c.A+c.B);class C{public int A;public int B;public C(int a){A=a;B=a*10;}public C(int a,int b){A=a;B=b;}}", "22\n")]
    [InlineData("var c=C.Create(3);WriteLine(c.X);class C{public int X;public C(int x){X=x;}public static C Create(int x)=>new C(x);}", "3\n")]
    public async Task Round11Audit_AllowedControls_PreserveFullDifferentialParity(string source,string expected)
    {
        Assert.Empty(Inspect(source));
        await AssertDifferential(source,expected);
    }

    [Fact]
    public void Round11Audit_TrustedSysCatalog_RemainsAcceptedBySelf()
    {
        var result=Run(DotnetExe,SelfCompiler,"check",Path.Combine(RepoRoot,"src","Vibranium.Std","vib.manifest"));
        Assert.True(result.Exit==0,result.Out+result.Err);
        Assert.Empty(result.Out+result.Err);
    }

    [Theory]
    [InlineData(5,"#define FLAG\n#if FLAG\nWriteLine(1);\n#endif")]
    [InlineData(6,"WriteLine(Hidden.F());file static class Hidden{public static int F()=>7;}")]
    [InlineData(7,"partial class C{}partial class C{}")]
    [InlineData(8,"class G<T>{public class Inner{}}")]
    [InlineData(9,"class C{public C():this(1){}public C(int x){}}")]
    public void Round11Audit_SelfCli_ReportsOnlyTheCanonicalCut(int cut,string source)
    {
        string temp=Path.Combine(Path.GetTempPath(),$"vibranium-r11-audit-cut-{Guid.NewGuid():N}");Directory.CreateDirectory(temp);
        try
        {
            string path=Path.Combine(temp,"Program.cs");File.WriteAllText(path,source);
            var result=Run(DotnetExe,SelfCompiler,"check",path,"--sysroot",Path.Combine(RepoRoot,"src"));
            Assert.Equal(1,result.Exit);Assert.Empty(result.Out);
            string diagnostic=Assert.Single(result.Err.Split('\n',StringSplitOptions.RemoveEmptyEntries));
            Assert.EndsWith(": error VIB000: "+CutMessage(cut),diagnostic,StringComparison.Ordinal);
        }
        finally{Directory.Delete(temp,recursive:true);}
    }

    private static string Expected(int cut,string source,string anchor,int file)
    {
        int start=source.IndexOf(anchor,StringComparison.Ordinal);
        Assert.True(start>=0);
        int offset=Encoding.UTF8.GetByteCount(source.AsSpan(0,start));
        return $"VIB000|{file}|{offset}|{Encoding.UTF8.GetByteCount(anchor)}|{CutMessage(cut)}";
    }

    private static string[] Inspect(params string[] sources) => (string[])s_parserProbe.Value.Invoke(null,new object[]{sources})!;
    private static readonly Lazy<MethodInfo> s_parserProbe=new(CreateParserProbe);

    private static MethodInfo CreateParserProbe()
    {
        const string harness="""
            using System;
            using System.Text;
            using System.Collections.Generic;
            using Vibranium;
            using Vibranium.Std;
            using Vibranium.Self;
            public static class AuditParserProbe
            {
                public static string[] Inspect(string[] texts)
                {
                    var sources=new SourceTable(texts.Length);
                    for(int i=0;i<texts.Length;i++)sources.AddFile(Encoding.UTF8.GetBytes("Source"+i+".cs"),Encoding.UTF8.GetBytes(texts[i]),UnitKinds.App);
                    var names=new Interner(64);var tokens=new TokenArena(64);var ast=new AstArena(64);var diagnostics=new DiagnosticBag(8);
                    Lexer.LexAll(ref sources,ref names,ref tokens,ref diagnostics);Parser.ParseAll(in sources,in tokens,ref names,ref ast,ref diagnostics);diagnostics.Sort();
                    var result=new List<string>();
                    for(int i=0;i<diagnostics.Items.Count;i++)
                    {
                        var d=diagnostics.Items[i];result.Add(Encoding.UTF8.GetString(VibCodes.Text(d.Code))+"|"+d.SourceId+"|"+d.ByteOffset+"|"+d.Length+"|"+Encoding.UTF8.GetString(diagnostics.MsgPool.Get(d.Message)));
                    }
                    return result.ToArray();
                }
            }
            """;
        string[] platform=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var refs=platform.Concat(new[]{typeof(Vibranium.Option<>).Assembly.Location,typeof(Vibranium.Std.StringBuf).Assembly.Location,SelfCompiler}).Distinct(StringComparer.Ordinal).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("Round11AuditParserProbe",new[]{CSharpSyntaxTree.ParseText(harness,new CSharpParseOptions(LanguageVersion.CSharp15))},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image=new MemoryStream();var emit=compilation.Emit(image);Assert.True(emit.Success,string.Join("\n",emit.Diagnostics));
        _=Assembly.LoadFrom(SelfCompiler);return Assembly.Load(image.ToArray()).GetType("AuditParserProbe")!.GetMethod("Inspect")!;
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-audit-parser-{Guid.NewGuid():N}");
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
