using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12CutSelfTests
{
    internal static string CutMessage(int cut) => cut switch
    {
        10 => "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter.",
        11 => "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard.",
        12 => "Optional parameters are not supported in Vibranium v0.2; declare an overload instead.",
        13 => "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order.",
        14 => "Assigning to this is not supported in Vibranium v0.2; assign the fields individually.",
        15 => "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement.",
        16 => "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first.",
        17 => "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong.",
        18 => "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }.",
        19 => "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition.",
        20 => "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition.",
        21 => "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance.",
        _ => throw new ArgumentOutOfRangeException(nameof(cut)),
    };

    public static IEnumerable<object[]> LateOccurrences()
    {
        yield return new object[] { 10, "class C{public static T Identity<T>(T x)=>x;class Nested{public static int F(/*cut*/ref readonly int x,ref readonly int y)=>x+y;}}", "ref" };
        yield return new object[] { 11, "class C{public static int F(int x){if(x>0){int /*cut*/_=x;return _;}return x;}public int _=>1;}", "_" };
        yield return new object[] { 12, "class C{public static T F<T>(T value,/*cut*/int extra=1)=>value;public static int G(int x=2)=>x;}", "int" };
        yield return new object[] { 13, "class C{public static int F(int x,int y)=>x+y;public static int G<T>(T value)=>F(/*cut*/y:2,x:1)+F(y:4,x:3);}", "y" };
        yield return new object[] { 14, "struct C{public int X;public void F(int x){if(x>0){/*cut*/this=default;}this=default;}}", "this" };
        yield return new object[] { 15, "class C{public static ref int F<T>(bool c,ref int a,ref int b)=>ref(c/*cut*/?ref a:ref b);}", "?" };
        yield return new object[] { 16, "class C{public static int F<T>(bool c){int[] a=c?/*cut*/[1]:[2];return a[0];}}", "[" };
        yield return new object[] { 17, "class C{public static long F<T>(/*cut*/System.IntPtr x)=>(long)x;public nuint Y;}", "System" };
        yield return new object[] { 18, "class C{public static int F<T>(int x){switch(x){case 0:{return 0;}default:/*cut*/int y=x;return y;}}}", "int" };
        yield return new object[] { 19, "class C{const bool Flag=1==1;public static int F<T>(int x){if(x>0){if(/*cut*/Flag)return 1;}if(!false)return 2;return 3;}}", "Flag" };
        yield return new object[] { 20, "class C{public static bool F<T>(int x)=>x is not (/*cut*/>0 and <4);}", ">" };
    }

    [Theory]
    [MemberData(nameof(LateOccurrences))]
    public void LateGenericAndNestedOccurrencesHaveOneDiagnosticPerFile(int cut, string marked, string anchor)
    {
        int position = marked.IndexOf("/*cut*/", StringComparison.Ordinal);
        string source = marked.Replace("/*cut*/", "", StringComparison.Ordinal);
        string[] actual = Inspect(source, "class Clean{}", source);
        string[] matching = actual.Where(value => value.EndsWith("|" + CutMessage(cut), StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { Expected(cut, source, position, anchor, 0), Expected(cut, source, position, anchor, 2) }, matching);
    }

    [Theory]
    [InlineData(10, "partial class Outer<T>{class Inner{public int F(/*cut*/ref readonly int value)=>value;}}", "ref")]
    [InlineData(11, "file class /*cut*/_{}", "_")]
    [InlineData(12, "class C{public int F(int x){switch(x){default:static int G(/*cut*/int value=1)=>value;return G();}}}", "int")]
    [InlineData(19, "partial class C{const bool Flag=true;public int F(){if(/*cut*/Flag)return 1;return 2;}}", "Flag")]
    [InlineData(20, "class Outer<T>{class Inner{public bool F(int x)=>x is /*cut*/>0;}}", ">")]
    [InlineData(13, "class C{public static int F(){return G(/*cut*/y:2,x:1);static int G(int x,int y)=>x+y;}}", "y")]
    [InlineData(16, "class C{public static int F(bool flag){ReadOnlySpan<int> values=flag?[1]!:/*cut*/[2];return values[0];}}", "[")]
    [InlineData(13, "static class C{public static int F(this int value,int x,int y)=>value+x+y;public static int G(int value)=>value.F(/*cut*/y:2,x:1);}", "y")]
    [InlineData(13, "class C{public static int F(int x,int y,int z)=>x+y+z;public static int F(int y,int x)=>x+y;public static int G()=>F(/*cut*/x:1,y:2);}", "x")]
    public void EarlierSubsetCutsDoNotHideLaterCuts(int cut, string marked, string anchor)
    {
        int position = marked.IndexOf("/*cut*/", StringComparison.Ordinal);
        string source = marked.Replace("/*cut*/", "", StringComparison.Ordinal);
        string[] actual = Inspect(source);
        Assert.Contains(Expected(cut, source, position, anchor, 0), actual);
    }

    [Theory]
    [InlineData("using Optional=System.Diagnostics.CodeAnalysis.NotNullAttribute;class C{public static int F([System.Runtime.InteropServices.Optional]int x)=>x;}")]
    [InlineData("using Optional=System.Diagnostics.CodeAnalysis.NotNullAttribute;class C{public static int F([global::System.Runtime.InteropServices.Optional]int x)=>x;}")]
    [InlineData("using DefaultParameterValue=System.Diagnostics.CodeAnalysis.NotNullAttribute;class C{public static int F([System.Runtime.InteropServices.DefaultParameterValue(1)]int x)=>x;}")]
    public Task QualifiedOptionalAttributesIgnoreUnrelatedAliases(string source) => AssertRejectedCut(source, 12);

    [Theory]
    [InlineData("WriteLine(C.F(x:1,y:2));class C{public static int F(int z,int x,int y)=>x+y+z;public static int F(int x,int y)=>x+y;}")]
    [InlineData("WriteLine(new C(x:1,y:2).Value);class C{public int Value;public C(int z,int x,int y){Value=x+y+z;}public C(int x,int y){Value=x+y;}}")]
    public async Task NamedArgumentsIgnoreOverloadsWithMissingRequiredParameters(string source)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-overload-control-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "OverloadControl", includeStd: true);
            Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.True(self.Exit == 0, self.Out + self.Err);
            Assert.Empty(self.Err);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void DifferentCutsSharingOneTokenAreNotDeduplicatedTogether()
    {
        const string source = "class C{public static int F(ref readonly int x=0)=>x;}";
        int position = source.IndexOf("ref readonly", StringComparison.Ordinal);
        string[] actual = Inspect(source);
        Assert.Contains(Expected(10, source, position, "ref", 0), actual);
        Assert.Contains(Expected(12, source, position, "ref", 0), actual);
    }

    [Theory]
    [InlineData("const bool Flag=!false;if(/*cut*/Flag){}", "Flag")]
    [InlineData("if(/*cut*/Flags.Value){}class Flags{public const bool Value=1==1;}", "Flags")]
    [InlineData("class C{public int F(){if(/*cut*/Flag)return 1;return 2;}const bool Flag=true;}", "Flag")]
    [InlineData("class C{const int A=2;const int B=A+1;public int F(){switch(/*cut*/B){default:return 1;}}}", "B")]
    [InlineData("while(/*cut*/(true)){break;}", "(")]
    [InlineData("const bool Flag=true;for(;/*cut*/Flag;){break;}", "Flag")]
    [InlineData("const int A=1;int x=2;WriteLine(x switch{_ when /*cut*/A==1=>1,_=>0});", "A")]
    public void ConstantConditionsUseDeclarationAndExpressionInformation(string marked, string anchor)
    {
        int position = marked.IndexOf("/*cut*/", StringComparison.Ordinal);
        string source = marked.Replace("/*cut*/", "", StringComparison.Ordinal);
        Assert.Contains(Expected(19, source, position, anchor, 0), Inspect(source));
    }

    [Theory]
    [InlineData("if(unchecked((nint)2147483648L)==0){}")]
    [InlineData("if(unchecked((nint)long.MaxValue)==0){}")]
    [InlineData("if(unchecked((nuint)(-1))==0){}")]
    public void NativeRuntimeConversionsAreNotConstantConditions(string source)
    {
        string[] actual = Inspect(source);
        Assert.Contains(actual, diagnostic => diagnostic.EndsWith(CutMessage(17), StringComparison.Ordinal));
        Assert.DoesNotContain(actual, diagnostic => diagnostic.EndsWith(CutMessage(19), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("if(/*cut*/(nint)1==1){}", "(")]
    [InlineData("if(/*cut*/(nint)1.0==1){}", "(")]
    [InlineData("if(/*cut*/(nuint)1f==1){}", "(")]
    [InlineData("class C{const bool Flag=true;public int F(){for(int Flag=0;Flag<1;Flag++){}if(/*cut*/Flag)return 1;return 0;}}", "Flag")]
    public void PortableNativeConstantsAndPostLoopBindingsRemainConstants(string marked, string anchor)
    {
        int position = marked.IndexOf("/*cut*/", StringComparison.Ordinal);
        string source = marked.Replace("/*cut*/", "", StringComparison.Ordinal);
        Assert.Contains(Expected(19, source, position, anchor, 0), Inspect(source));
    }

    [Theory]
    [InlineData("bool value=true;if(value){}while(true){break;}do{break;}while(true);for(;;){break;}for(;true;){break;}")]
    [InlineData("class C{const bool Flag=true;public int F(bool Flag){if(Flag)return 1;return 2;}}")]
    [InlineData("bool flag=true;{const bool Other=true;_ = Other;}if(flag){}")]
    [InlineData("_ = 1;int.TryParse(\"1\",out _);int.TryParse(\"1\",out var _);int x=1;_ = x is var _;_ = x switch{_=>1};")]
    [InlineData("int nint=1;int nuint=2;WriteLine(nint+nuint);")]
    [InlineData("Option<int> option=Some(1);WriteLine(option switch{Some<int>(var _) when Read(true)=>1,Some<int>(var _)=>2,NoneType=>0});static bool Read(bool value)=>value;")]
    [InlineData("class C{public bool F(int nint)=>nint.Equals(1);}")]
    [InlineData("class C{public static int F(int x,int y)=>x+y;public static int G()=>F(x:1,y:2);}")]
    [InlineData("class C{public int F(int x){switch(x){case 1:{int y=x;return y;}default:return x;}}}")]
    [InlineData("class C{public int F(int x){switch(x){case int y:return y;}}}")]
    [InlineData("class C{public bool F(int x)=>x is 1 or 2 or not 3;}")]
    public void NearbyAllowedFormsDoNotProduceNewCuts(string source)
    {
        Assert.DoesNotContain(Inspect(source), value => Enumerable.Range(10, 11).Any(cut => value.EndsWith("|" + CutMessage(cut), StringComparison.Ordinal)));
    }

    [Fact]
    public void CrossFileConstFieldsAreVisibleWithoutLoweringRejectedBodies()
    {
        const string source = "class C{public int F(){if(Flags.Value)return 1;return 2;}}";
        Assert.Contains(Expected(19, source, source.IndexOf("Flags.Value", StringComparison.Ordinal), "Flags", 0),
            Inspect(source, "class Flags{public const bool Value=true;}"));
    }

    [Theory]
    [InlineData("Option<Node> slot=Some(new Node());var m1=slot.AsMut();var m2=m1;m1.Set(None);WriteLine(m2.IsSome);sealed class Node{public int V=1;}")]
    [InlineData("Span<int> s=[1,2];Consume(Move(s));s[0]=1;WriteLine(s[0]);static void Consume(Span<int> a){a[1]=7;}")]
    public void RuleLedgerMovedUsesReportOnlyMoveViolation(string source)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-move-rule-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Equal(new[] { "VIB001" }, Regex.Matches(self.Err, @"VIB\d{3}").Select(match => match.Value).Distinct().Order());
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("Option<Node> slot = Some(new Node()); var m1 = slot.AsMut(); var m2 = m1; m2.Set(None); WriteLine(m2.IsSome); sealed class Node { public int V = 1; }")]
    [InlineData("Option<Node> slot = Some(new Node()); Option<Node> other = Some(new Node()); var m1 = slot.AsMut(); var m2 = m1; m1 = other.AsMut(); m1.Set(None); WriteLine(m2.IsSome); sealed class Node { public int V = 1; }")]
    [InlineData("Span<int> s = [1, 2]; Consume(s); s[0] = 1; WriteLine(s[1]); static void Consume(Span<int> a) { a[1] = 7; }")]
    [InlineData("Span<int> s = [1, 2]; Span<int> moved = Move(s); Consume(moved); WriteLine(moved[1]); static void Consume(Span<int> a) { a[1] = 7; }")]
    public void RuleLedgerValidTransfersAndReborrowsRemainAccepted(string source)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-move-control-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.True(self.Exit == 0, self.Out + self.Err);
            Assert.Empty(self.Err);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("Span<Node> values=[new Node()];WriteLine(values.Length);sealed class Node{public int Value=1;}")]
    [InlineData("ReadOnlySpan<Node> values=[new Node()];WriteLine(values.Length);sealed class Node{public int Value=1;}")]
    [InlineData("Span<Option<Node>> values=[];WriteLine(values.Length);sealed class Node{public int Value=1;}")]
    [InlineData("Span<Owned> values=[];WriteLine(values.Length);struct Owned{public Node Value;}sealed class Node{public int Value=1;}")]
    [InlineData("Span<Resource> values=[];WriteLine(values.Length);sealed class Resource:IDisposable{public void Dispose(){}}")]
    [InlineData("var values=new Vec<Node>();_ = values.AsSpan();sealed class Node{public int Value=1;}")]
    [InlineData("class C{public static void Unused<T>(Span<T> values){}}")]
    public void RuleLedgerSpanElementsRequireCopy(string source)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-span-rule-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Contains("error VIB000: Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2.", self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("Vibranium.Core", "Program", "", "Program.cs")]
    [InlineData("Vibranium.Bootstrap", "VibraniumAppBootstrap", "", "Program.cs")]
    [InlineData("Vibranium.Std.Sys", "Generated", "", "Sys.cs")]
    [InlineData("App", "Generated", "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Vibranium\",\"VIB001\")]", "Program.g.cs")]
    [InlineData("App", "Generated", "[System.Runtime.CompilerServices.CompilerGenerated]", "Program.g.cs")]
    [InlineData("App", "Generated", "[System.CodeDom.Compiler.GeneratedCode(\"probe\",\"1\")]", "Program.designer.cs")]
    [InlineData("App", "VibraniumAppBootstrap", "[System.Runtime.CompilerServices.CompilerGenerated]", "VibraniumAppBootstrap.cs")]
    public void NamesAndGeneratedMarkersDoNotSkipOwnership(string sourceNamespace, string typeName, string attribute, string fileName)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-name-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, fileName);
            string source = "// <auto-generated/>\nnamespace " + sourceNamespace + "{" + attribute + "public static class " + typeName +
                "{public static void F(){Vec<int> owned=[1];var moved=Move(owned);WriteLine(owned.Count);WriteLine(moved.Count);}}}";
            File.WriteAllText(path, source);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            // Attributes are denied by F15. Secondary IDs after that subset
            // rejection are not compared (CONFORMANCE §7.1); keep the ownership control.
            if (attribute.Length != 0)
            {
                Assert.Contains("error VIB000:", self.Err, StringComparison.Ordinal);
                File.WriteAllText(path, source.Replace(attribute, "", StringComparison.Ordinal));
                self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                    "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
                Assert.Equal(1, self.Exit);
            }
            Assert.Contains("error VIB001:", self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("core", false)]
    [InlineData("std", false)]
    [InlineData("core", true)]
    [InlineData("std", true)]
    public void UserManifestUnitNamesCannotEstablishRuntimeTrust(string unit, bool dependency)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-manifest-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Program.cs"), "Span<int> values=[1];Consume(Move(values));WriteLine(values[0]);static void Consume(Span<int> value){}");
            File.WriteAllText(Path.Combine(temporary, "foreign.ll"), "declare void @untrusted_native_fragment()\n");
            string foreign = Path.Combine(temporary, "foreign.manifest");
            File.WriteAllText(foreign, $"unit {unit}\nsource Program.cs\nnative foreign.ll\n");
            string input = foreign;
            if (dependency)
            {
                input = Path.Combine(temporary, "vib.manifest");
                File.WriteAllText(input, "unit app\nuse foreign.manifest\n");
            }
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", input, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Contains("error VIB000: Only the selected sysroot manifests may declare core or std units.", self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationSourceDeduplicationNeverInheritsStdTrust(bool loadTrustedFirst)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-source-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string standard = Path.Combine(Round11AuditAssertions.RepoRoot, "src", "Vibranium.Std");
            string input = Path.Combine(temporary, "vib.manifest");
            string dependency = loadTrustedFirst ? $"use {Path.Combine(standard, "vib.manifest")}\n" : "";
            File.WriteAllText(input, $"unit app\n{dependency}source {Path.Combine(standard, "Sys", "Sys.cs")}\n");
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", input, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Contains("error VIB000:", self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void ApplicationManifestCannotImportNativeFragments()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-native-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string input = Path.Combine(temporary, "vib.manifest");
            File.WriteAllText(input, "unit app\nnative missing.ll\n");
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", input, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Contains("error VIB000: Native fragments are permitted only in the selected sysroot manifests.", self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("sealed class C{public C():base(){}}")]
    [InlineData("sealed class C{public C(int x):base(x){}}")]
    [InlineData("class A{public A():base(){}}class B{public B():base(){}}")]
    [InlineData("class C{public C():this(1){}public C(int x):base(){}}")]
    [InlineData("class Outer<T>{class C{public C():base(){}}}")]
    public void BaseConstructorCutUsesFirstBaseToken(string source)
    {
        string expected = Expected(21, source, source.IndexOf("base", StringComparison.Ordinal), "base", 0);
        Assert.Equal(expected, Assert.Single(Inspect(source), diagnostic => diagnostic.EndsWith("|" + CutMessage(21), StringComparison.Ordinal)));
        if (source.Contains(":this", StringComparison.Ordinal))
            Assert.Single(Inspect(source), diagnostic => diagnostic.EndsWith("|Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method.", StringComparison.Ordinal));
    }

    [Fact]
    public void BaseConstructorCutBudgetIsPerSourceFile()
    {
        const string source = "sealed class C{public C():base(){}}";
        string[] actual = Inspect(source, source);
        Assert.Contains(Expected(21, source, source.IndexOf("base", StringComparison.Ordinal), "base", 0), actual);
        Assert.Contains(Expected(21, source, source.IndexOf("base", StringComparison.Ordinal), "base", 1), actual);
        Assert.Equal(2, actual.Count(diagnostic => diagnostic.EndsWith("|" + CutMessage(21), StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("sealed class C{public C():base(){}}")]
    [InlineData("sealed class C{public C(int x):base(x){}}")]
    [InlineData("class A{public A():base(){}}class B{public B():base(){}}")]
    [InlineData("class C{public C():this(1){}public C(int x):base(){}}")]
    public Task BaseConstructorCutMatchesFrontends(string source) => AssertRejectedCut(source, 21);

    internal static async Task AssertRejectedCut(string source, int cut)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"vibranium-r12-migrated-cut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MigratedCut", includeStd: true);
            string message = CutMessage(cut);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            var canonical = Assert.Single(frontend.Diagnostics, diagnostic => diagnostic.Message == message);
            Assert.Equal("VIB000", canonical.Id);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var live = Assert.Single(analyzer, diagnostic => diagnostic.GetMessage() == message);
            Assert.Equal(canonical.Span.Start, live.Location.SourceSpan.Start);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(1, self.Exit);
            Assert.Single(Regex.Matches(self.Err, Regex.Escape(message)));
            int position = canonical.Span.Start;
            int line = 1 + source[..position].Count(character => character == '\n');
            int column = position - source.LastIndexOf('\n', position);
            Assert.Contains($"Program.cs({line},{column}): error VIB000: {message}", self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string Expected(int cut, string source, int position, string anchor, int file) =>
        $"VIB000|{file}|{Encoding.UTF8.GetByteCount(source.AsSpan(0, position))}|{Encoding.UTF8.GetByteCount(anchor)}|{CutMessage(cut)}";

    internal static string[] Inspect(params string[] sources) =>
        (string[])s_probe.Value.Invoke(null, new object[] { sources })!;

    private static readonly Lazy<MethodInfo> s_probe = new(CreateProbe);

    private static MethodInfo CreateProbe()
    {
        const string harness = """
            using System;
            using System.Text;
            using System.Collections.Generic;
            using Vibranium;
            using Vibranium.Std;
            using Vibranium.Self;
            public static class Round12SelfProbe
            {
                public static string[] Inspect(string[] texts)
                {
                    var sources = new SourceTable(texts.Length);
                    for (int i = 0; i < texts.Length; i++)
                        sources.AddFile(Encoding.UTF8.GetBytes("Source" + i + ".cs"), Encoding.UTF8.GetBytes(texts[i]), UnitKinds.App);
                    var names = new Interner(64);
                    var tokens = new TokenArena(64);
                    var ast = new AstArena(64);
                    var diagnostics = new DiagnosticBag(8);
                    Lexer.LexAll(ref sources, ref names, ref tokens, ref diagnostics);
                    Parser.ParseAll(in sources, in tokens, ref names, ref ast, ref diagnostics);
                    diagnostics.Sort();
                    var result = new List<string>();
                    for (int i = 0; i < diagnostics.Items.Count; i++)
                    {
                        var d = diagnostics.Items[i];
                        result.Add(Encoding.UTF8.GetString(VibCodes.Text(d.Code)) + "|" + d.SourceId + "|" + d.ByteOffset + "|" + d.Length + "|" + Encoding.UTF8.GetString(diagnostics.MsgPool.Get(d.Message)));
                    }
                    return result.ToArray();
                }
            }
            """;
        string self = Round11AuditAssertions.SelfCompiler;
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platform.Concat(new[] { typeof(Option<>).Assembly.Location, typeof(Std.StringBuf).Assembly.Location, self })
            .Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Round12SelfProbe", new[] { CSharpSyntaxTree.ParseText(harness, new CSharpParseOptions(LanguageVersion.CSharp15)) },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        _ = Assembly.LoadFrom(self);
        return Assembly.Load(image.ToArray()).GetType("Round12SelfProbe")!.GetMethod("Inspect")!;
    }
}
