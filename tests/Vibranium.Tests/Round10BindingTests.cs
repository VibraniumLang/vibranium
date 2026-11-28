using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round10BindingTests
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
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Round10_BareOutUnderscore_UsesNearestRuntimeBinding_IsRejectedInVersion02()
    {
        const string source = """
            WriteLine(Local()); WriteLine(Parameter(1)); WriteLine(Outer());
            WriteLine(DeclaredDiscards()); WriteLine(UnboundDiscard());
            WriteLine(Guarded(true)); WriteLine(Guarded(false));
            static int Local() { int _ = 1; int.TryParse("42", out _); return _; }
            static int Parameter(int _) { int.TryParse("43", out _); return _; }
            static int Outer() { int _ = 1; { int.TryParse("44", out _); } return _; }
            static int DeclaredDiscards()
            {
                int _ = 1; int.TryParse("45", out var _); int.TryParse("46", out int _); return _;
            }
            static int UnboundDiscard() { int.TryParse("47", out _); return 2; }
            static int Guarded(bool flag)
            {
                int _ = 1; if (flag && int.TryParse("48", out _)) return _; return _;
            }
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 11);
    }

    [Fact]
    public async Task Round10_BareOutUnderscore_FieldRetainsMutationDiagnostic_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(new C().F()); public class C { public int _ = 1; public int F() { int.TryParse(\"42\", out _); return _; } }", 11);
    }

    [Theory]
    [InlineData("int.TryParse(\"42\",out _);int _=1;return _;")]
    [InlineData("const int _=1;int.TryParse(\"42\",out _);return _;")]
    [InlineData("int _=1;return G()+_;static int G(){int.TryParse(\"42\",out _);return 0;}")]
    public async Task Round10_BareOutUnderscore_DoesNotHideInvalidBindings_IsRejectedInVersion02(string body)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F());static int F(){" + body + "}", 11);
    }

    [Theory]
    [InlineData("case 0:int.TryParse(\"5\",out int v);return v;default:int.TryParse(\"6\",out int v);return v;")]
    [InlineData("case 0:int a=int.TryParse(\"5\",out int v)?v:0;return a;default:int b=int.TryParse(\"6\",out int v)?v:0;return b;")]
    [InlineData("case 0:bool a=k is int v&&v>0;return a?1:0;default:bool b=k is int v&&v>5;return b?1:0;")]
    [InlineData("case 0:if(int.TryParse(\"5\",out int v))return v;return 0;default:int v=3;return v;")]
    public async Task Round10_SwitchExpressionDeclarations_RejectOverlappingNames_IsRejectedInVersion02(string sections)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));static int F(int k){switch(k){" + sections + "}}", 18);
    }

    [Theory]
    [InlineData("case 0:int.TryParse(\"5\",out int v);return v;default:v=3;return v;")]
    [InlineData("case 0:if(int.TryParse(\"5\",out int v))return v;return 0;default:v=3;return v;")]
    [InlineData("case 0:return int.TryParse(\"5\",out int v)?v:0;default:v=3;return v;")]
    [InlineData("case 0:bool a=k is int v;return a?v:9;default:v=3;return v;")]
    [InlineData("case 0:int.TryParse(\"5\",out var v);var q=v+1;return q;default:v=3;return v;")]
    public async Task Round10_SwitchExpressionDeclarations_ShareIdentityWithoutInitialization_IsRejectedInVersion02(string sections)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));WriteLine(F(1));static int F(int k){switch(k){" + sections + "}}", 18);
    }

    [Theory]
    [InlineData("case 0 when int.TryParse(\"5\",out int v):return v;default:{int v=3;return v;}", "5\n3\n")]
    [InlineData("case 0:{int.TryParse(\"5\",out int v);return v;}default:{int v=3;return v;}", "5\n3\n")]
    [InlineData("case 0:while(int.TryParse(\"5\",out int v)){return v;}return 0;default:{int v=3;return v;}", "5\n3\n")]
    [InlineData("case int v when v>5:return v;default:{int v=3;return v;}", "3\n3\n")]
    public async Task Round10_SwitchExpressionDeclarations_PreserveNestedAndLabelScopes(string sections, string expected)
    {
        await AssertDifferential("WriteLine(F(0));WriteLine(F(1));static int F(int k){switch(k){" + sections + "}}", expected);
    }

    [Fact]
    public async Task Round10_SwitchExpressionDeclarations_DoNotAssignSkippedOutVariable_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(1));static int F(int k){switch(k){case 0:int.TryParse(\"5\",out int v);return v;default:return v;}}", 18);
    }

    [Fact]
    public async Task Round10_SwitchExpressionDeclarations_KeepTextualDeclarationOrder_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));static int F(int k){switch(k){case 0:v=3;return v;default:int.TryParse(\"5\",out int v);return v;}}", 18);
    }

    [Fact]
    public async Task Round10_StaticLocalFunctions_ResolveEnclosingClassConstants()
    {
        const string source = """
            WriteLine(C.M()); WriteLine(C.Shadow()); WriteLine(C.Nested());
            public class C
            {
                public const int K = 5;
                public const bool T = true;
                public static bool Read(bool value) => value;
                public static int M() { return L(); static int L() => K; }
                public static int Shadow() { const int K=7; return L(); static int L()=>K; }
                public static int Nested()
                {
                    return L();
                    static int L() { return N(); static int N() { if (Read(T)) return K+1; return 0; } }
                }
            }
            """;
        await AssertDifferential(source, "5\n7\n6\n");
    }

    [Theory]
    [InlineData("return L();static int L()=>K;const int K=7;", "CS0844")]
    [InlineData("int K=7;return L()+K;static int L()=>K;", "CS8421")]
    public void Round10_StaticLocalFunctions_RespectNearerLocalBindings(string body, string diagnostic)
    {
        AssertRejected("WriteLine(C.M());public class C{public const int K=5;public static int M(){" + body + "}}", diagnostic, null);
    }

    [Theory]
    [InlineData("return First(c?([x]):([x+1]));")]
    [InlineData("return First(value:c?([x]):([x+1]));")]
    public async Task Round10_ConditionalCollectionArguments_AcceptCandidateTargetType_IsRejectedInVersion02(string body)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(true,7));WriteLine(F(false,7));static int F(bool c,int x){" + body + "}static int First(ReadOnlySpan<int> value)=>value[0];", 16);
    }

    [Fact]
    public async Task Round10_ConditionalCollectionArguments_SelectOnlyOneArmAndEvaluateOnce_IsRejectedInVersion02()
    {
        const string source = """
            WriteLine(F(true)); WriteLine(F(false));
            static int F(bool flag)
            {
                int count=0;
                int result=C.First(Condition(flag,ref count)?([Item(7,ref count)]):([Item(8,ref count)]));
                WriteLine(count);return result;
            }
            static bool Condition(bool flag,ref int count){count+=1;return flag;}
            static int Item(int value,ref int count){count+=10;return value;}
            public static class C
            {
                public static int First(ReadOnlySpan<int> value)=>value[0];
                public static int First(int value)=>value+100;
            }
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Theory]
    [InlineData("case 0:int.TryParse(\"5\",out int v);return v;default:v=3;return v;")]
    [InlineData("default:int.TryParse(\"5\",out int v);return v;case 0:v=3;return v;")]
    public async Task Round10_SwitchExpressionDeclarations_ReserveBeforeAnySectionLowering_IsRejectedInVersion02(string sections)
    {
        string expected = sections.StartsWith("default", StringComparison.Ordinal) ? "3\n5\n" : "5\n3\n";
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));WriteLine(F(1));static int F(int k){switch(k){" + sections + "}}", 18);
    }

    [Theory]
    [InlineData("case int v when v>5:return v;default:int v=3;return v;")]
    [InlineData("case 0 when int.TryParse(\"5\",out int v):return v;default:int v=3;return v;")]
    [InlineData("case 0:while(int.TryParse(\"5\",out int v)){return v;}return 0;default:int v=3;return v;")]
    [InlineData("case 0:{int.TryParse(\"5\",out int v);return v;}default:int v=3;return v;")]
    public async Task Round10_SwitchBlockDeclarations_ConflictWithNestedDeclarationSpaces_IsRejectedInVersion02(string sections)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));static int F(int k){switch(k){" + sections + "}}", 18);
    }

    [Fact]
    public async Task Round10_BareOutUnderscore_UnionGuardRetainsRealDiscard()
    {
        await AssertDifferential("WriteLine(F(Some(5)));WriteLine(F(None));static int F(Option<int> value){if(value.IsNone(out _))return 1;return 2;}", "2\n1\n");
    }

    [Fact]
    public async Task Round10_BareOutUnderscore_UnionGuardRejectsExistingVariable_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(Some(5)));static int F(Option<int> value){int _=1;if(value.IsNone(out _))return 2;return _;}", 11);
    }

    [Theory]
    [InlineData("H(5)")]
    [InlineData("H(x+1)")]
    [InlineData("H(K)")]
    [InlineData("G(in K)")]
    [InlineData("G(in (x+1))")]
    [InlineData("G(in (true?x:K))")]
    public async Task Round10_ReadonlyReferenceArguments_RequireSourceVariables_IsRejectedInVersion02(string call)
    {
        string source = "int x=4;const int K=5;WriteLine(" + call + ");WriteLine(x+K);static int H(ref readonly int value)=>value+2;static int G(in int value)=>value+1;";
        await Round12CutSelfTests.AssertRejectedCut(source, 10);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_KeepOrdinaryImplicitInTemporaries()
    {
        const string source = """
            const int K=5; int x=4;
            WriteLine(G(5)); WriteLine(G(K)); WriteLine(G(x+1));
            WriteLine(H(in x)); WriteLine(G(in x));
            WriteLine(G(in R(ref x))); WriteLine(H(in R(ref x)));
            ReadOnlySpan<int> values=[7,8]; WriteLine(G(in values[0]));
            static int G(in int value)=>value+1;
            static int H(in int value)=>value+2;
            static ref int R(ref int value)=>ref value;
            """;
        await AssertDifferential(source, "6\n6\n6\n6\n5\n5\n6\n8\n");
    }

    [Fact]
    public async Task Round10_DirectSwitchLocalFunctions_AreOutsideVersion02()
    {
        const string source = """
            WriteLine(C.F(0)); WriteLine(C.F(1));
            public static class C
            {
                public const int K=5;
                public static int F(int key)
                {
                    switch(key)
                    {
                        case 0:return L();
                        default:static int L()=>K;return 0;
                    }
                }
            }
            """;
        await AssertDiagnosticParity(source, "VIB000");
    }

    [Theory]
    [InlineData("switch(L()){case 0:static int L()=>0;return 1;default:return 2;}")]
    [InlineData("switch(k){case 0:static int L()=>0;return L();default:static int L()=>1;return L();}")]
    [InlineData("switch(k){case 0:static int L()=>0;return L();default:{static int L()=>1;return L();}}")]
    public async Task Round10_SwitchLocalFunctions_DoNotEscapeOrOverlapDeclarationSpaces_IsRejectedInVersion02(string body)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));static int F(int k){" + body + "}", 18);
    }

    [Theory]
    [InlineData("return First<int>(c?([x]):([x+1]));", "static int First<T>(ReadOnlySpan<T> value) where T:unmanaged=>value.Length;")]
    [InlineData("return First(c?([x]):([x+1]),x);", "static int First<T>(ReadOnlySpan<T> value,T other) where T:unmanaged=>value.Length;")]
    public async Task Round10_ConditionalCollectionArguments_PreserveGenericInference_IsRejectedInVersion02(string body, string target)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(true,7));WriteLine(F(false,7));static int F(bool c,int x){" + body + "}" + target, 16);
    }

    [Theory]
    [InlineData("First<int>(c?([x]):([true]))", "CS1503", "static int First<T>(ReadOnlySpan<T> value) where T:unmanaged=>value.Length;")]
    [InlineData("First(c?([x]):([x+1]))", "CS0411", "static int First<T>(ReadOnlySpan<T> value) where T:unmanaged=>value.Length;")]
    public void Round10_ConditionalCollectionArguments_RejectInvalidConversionsAndUninferredTypes(string call, string diagnostic, string target)
    {
        AssertRejected("WriteLine(F(true,7));static int F(bool c,int x){return " + call + ";}" + target, diagnostic, null);
    }

    [Theory]
    [InlineData("case int p when p==0:var q=p;return q;default:q=3;return q;")]
    [InlineData("case 0 when int.TryParse(\"5\",out int p):var q=p;return q;default:q=3;return q;")]
    [InlineData("case 0:switch(int.TryParse(\"5\",out int p)){case true:return p;default:return 0;}default:p=3;return p;")]
    public async Task Round10_SwitchReservation_UsesSectionTypeContextWithoutExecutingIt_IsRejectedInVersion02(string sections)
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine(F(0));WriteLine(F(1));static int F(int k){switch(k){" + sections + "}}", 18);
    }

    [Fact]
    public async Task Round10_SwitchReservation_InfersGenericNamedOutArgumentBeforeDefaultLowering_IsRejectedInVersion02()
    {
        const string source = """
            WriteLine(F(0));WriteLine(F(1));
            static long F(int key)
            {
                switch(key)
                {
                    default:C.Set(result:out var value,input:5L);return value;
                    case 0:value=9L;return value;
                }
            }
            public static class C
            {
                public static bool Set<T>(T input,out T result) where T:unmanaged {result=input;return true;}
                public static bool Set(bool input,out bool result) {result=input;return true;}
            }
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 18);
    }

    [Fact]
    public async Task Round10_SwitchReservation_PreservesUnionPatternPayloadIdentity_IsRejectedInVersion02()
    {
        const string source = """
            WriteLine(F(0,Some(5)));WriteLine(F(1,None));
            static int F(int key,Option<int> input)
            {
                switch(key)
                {
                    case 0:if(input is Some<int>(var value))return value;return -1;
                    default:value=9;return value;
                }
            }
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 18);
    }

    [Fact]
    public async Task Round10_SwitchReservation_DoesNotEagerlyCallOutInitializers_IsRejectedInVersion02()
    {
        const string source = """
            int count=0;
            for(int i=0;i<3;i++)
            {
                switch(i%2)
                {
                    case 0:Set(ref count,out var value);WriteLine(value);break;
                    default:value=9;WriteLine(value);break;
                }
            }
            WriteLine(count);
            static void Set(ref int count,out int value){count+=1;value=count;}
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 18);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Round10_ConditionalCollectionArguments_RejectInapplicableCandidateRegardlessOfOrder_IsRejectedInVersion02(bool reverse)
    {
        const string ints = "public static int First(ReadOnlySpan<int> value)=>1;";
        const string bools = "public static int First(ReadOnlySpan<bool> value)=>2;";
        string source = "WriteLine(F(true));WriteLine(F(false));static int F(bool c){return C.First(c?([true]):([false]));}public static class C{" + (reverse ? bools + ints : ints + bools) + "}";
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Fact]
    public async Task Round10_ConditionalCollectionArguments_ContextuallyTypeNestedArms_IsRejectedInVersion02()
    {
        const string source = "WriteLine(F(true,false,7));WriteLine(F(false,true,7));static int F(bool a,bool b,int x){return First(a?(b?([x]):([x+1])):([x+2]));}static int First(ReadOnlySpan<int> value)=>value[0];";
        await Round12CutSelfTests.AssertRejectedCut(source, 16);
    }

    [Theory]
    [InlineData("ReadOnlySpan<long>", "c?([x]):([x+1])", "CS0121")]
    [InlineData("ReadOnlySpan<bool>", "c?([]):([])", "CS0121")]
    [InlineData("ReadOnlySpan<bool>", "c?([x]):([true])", "CS1503")]
    public void Round10_ConditionalCollectionArguments_PreserveAmbiguityAndNoApplicableCandidate(string otherType, string argument, string diagnostic)
    {
        string source = "WriteLine(F(true,7));static int F(bool c,int x){return C.First(" + argument + ");}public static class C{public static int First(ReadOnlySpan<int> value)=>1;public static int First(" + otherType + " value)=>2;}";
        AssertRejected(source, diagnostic, null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Round10_ReadonlyReferenceArguments_AreCheckedBeforeAndAfterRuntimePanic(bool afterPanic)
    {
        string source = "F();static void F(){const int K=5;" + (afterPanic ? "VibraniumRuntime.Panic(\"stop\");" : "") + "WriteLine(G(in K));}static int G(in int value)=>value;";
        AssertRejected(source, "CS8156", null);
    }

    [Theory]
    [InlineData("G(in box.P)", "CS8156")]
    [InlineData("H(box.P)", "CS9193")]
    [InlineData("H((int)box.X)", "CS9193")]
    public void Round10_ReadonlyReferenceArguments_DoNotTreatValuePropertiesOrCastsAsVariables(string call, string diagnostic)
    {
        AssertRejected("var box=new C();WriteLine(" + call + ");static int G(in int value)=>value;static int H(ref readonly int value)=>value;public class C{public int X=5;public int P=>6;}", diagnostic, null);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_AcceptFieldsRefPropertiesAndRefConditionals_IsRejectedInVersion02()
    {
        const string source = """
            var box=new C();int value=4;
            WriteLine(G(in box.X));WriteLine(H(in box.Q));WriteLine(G(in box.RP));
            WriteLine(G(in (value>0?ref value:ref box.X)));
            static int G(in int value)=>value+1;
            static int H(ref readonly int value)=>value+2;
            public class C{public int X=5;public readonly int Q=6;public ref readonly int RP=>ref X;}
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 10);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_ValidateExtensionReceivers_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("WriteLine((5).Read());public static class C{public static int Read(this ref readonly int value)=>value;}", 10);
    }

    [Theory]
    [InlineData("H(value)", "static int H(ref readonly int value)=>value+2;")]
    [InlineData("H(value:value)", "static int H(ref readonly int value)=>value+2;")]
    [InlineData("C.H<int>(value:value)", "public static class C{public static int H<T>(ref readonly T value) where T:unmanaged=>1;}")]
    [InlineData("C.Read(value)", "public static class C{public static int Read(this ref readonly int value)=>value;}")]
    public async Task Round10_ReadonlyReferenceArguments_RequireDirectCallModifiersInZeroWarningProfile_IsRejectedInVersion02(string call, string declaration)
    {
        await Round12CutSelfTests.AssertRejectedCut("int value=4;WriteLine(" + call + ");" + declaration, 10);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_ModifierPolicySurvivesWarningSuppression_IsRejectedInVersion02()
    {
        const string source = """
            #pragma warning disable CS9192
            int value=4;WriteLine(H(value));
            static int H(ref readonly int value)=>value+2;
            """;
        await Round12CutSelfTests.AssertRejectedCut(source, 10);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_ReducedExtensionReceiversNeedNoCallModifier_IsRejectedInVersion02()
    {
        const string source = "int value=4;WriteLine(value.Read());public static class C{public static int Read(this ref readonly int value)=>value+2;}";
        await Round12CutSelfTests.AssertRejectedCut(source, 10);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_AcceptExplicitRefModifier_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("int value=4;WriteLine(H(ref value));static int H(ref readonly int value)=>value+2;", 10);
    }

    [Fact]
    public async Task Round10_StrictTrueFinalSwitchGuard_PreservesExhaustiveDefiniteAssignment_IsRejectedInVersion02()
    {
        const string source = "WriteLine(F(true));WriteLine(F(false));static int F(bool b){int x;switch(b){case true:x=1;break;case false when true:x=2;break;}return x;}";
        await Round12CutSelfTests.AssertRejectedCut(source, 19);
    }

    [Fact]
    public void Round10_NonconstantTrueFinalSwitchGuard_DoesNotEstablishDefiniteAssignment()
    {
        const string source = "WriteLine(F(true));static int F(bool b){int x;switch(b){case true:x=1;break;case false when b||true:x=2;break;}return x;}";
        AssertRejected(source, "CS0165", null, selfId: "VIB001");
    }

    [Fact]
    public async Task Round10_StaticLocalFunctions_UseEnclosingConstantsInContextualConversions()
    {
        const string source = "WriteLine(C.M());public static class C{public const int K=5;public static int M(){return L();static int L()=>First([K]);}public static int First(ReadOnlySpan<byte> value)=>value[0];}";
        await AssertDifferential(source, "5\n");
    }

    [Fact]
    public void Round10_StaticLocalFunctions_DoNotLoseConstantExpressionDiagnostics()
    {
        AssertRejected("WriteLine(C.M());public static class C{public const int Zero=0;public static int M(){return L();static int L()=>1/Zero;}}", "CS0020", "constant zero");
    }

    [Theory]
    [InlineData("WriteLine(new C().M());public class C{public int M()=>G(in this);static int G(in C value)=>1;}", "CS8354")]
    [InlineData("WriteLine(G(in Make().X));static C Make()=>new C();static int G(in int value)=>value;public struct C{public int X;}", "CS1612")]
    [InlineData("var c=new C();WriteLine(G(in c[0]));static int G(in int value)=>value;public class C{public int this[int index]=>index;}", "CS8156")]
    public void Round10_ReadonlyReferenceArguments_RejectNonvariableCategories(string source, string diagnostic)
    {
        AssertRejected(source, diagnostic, null);
    }

    [Theory]
    [InlineData("WriteLine(new C().M());public struct C{public int M()=>G(in this);static int G(in C value)=>1;}", "1\n")]
    [InlineData("int x=5;int y=7;ref int r=ref x;WriteLine(G(in(r=ref y)));static int G(in int value)=>value;", "7\n")]
    [InlineData("var c=new C();WriteLine(G(in c[0]));static int G(in int value)=>value;public class C{public int X=5;public ref readonly int this[int index]=>ref X;}", "5\n")]
    [InlineData("WriteLine(G(in Make().X));static C Make()=>new C();static int G(in int value)=>value;public class C{public int X=5;}", "5\n")]
    public async Task Round10_ReadonlyReferenceArguments_AcceptVariableCategories(string source, string expected)
    {
        await AssertDifferential(source, expected);
    }

    [Fact]
    public async Task Round10_ReadonlyReferenceArguments_ExplicitRefStillRequiresMutableSource_IsRejectedInVersion02()
    {
        await Round12CutSelfTests.AssertRejectedCut("int value=5;WriteLine(H(ref R(in value)));static ref readonly int R(in int value)=>ref value;static int H(ref readonly int value)=>value;", 10);
    }

    [Fact]
    public async Task Round10_BracedSwitchLocalFunctions_RemainSupported()
    {
        const string source = "WriteLine(C.F(0));WriteLine(C.F(1));public static class C{public const int K=5;public static int F(int key){switch(key){case 0:{static int L()=>K;return L();}default:{static int L()=>K+1;return L();}}}}";
        await AssertDifferential(source, "5\n6\n");
    }

    [Theory]
    [InlineData("WriteLine(F(Some(2)));static int F(Option<int> o)=>o switch{Some<int>(var _)=>1,Some<int>(var _)=>2,NoneType=>0};", "CS8510", 0)]
    [InlineData("WriteLine(F(Some(2)));static int F(Option<int> o)=>o switch{Some<int>(var _)=>1,Some<int>(2)=>2,NoneType=>0};", "CS8510", 0)]
    [InlineData("WriteLine(F(Some(2)));static int F(Option<int> o){switch(o){case Some<int>(var _):return 1;case Some<int>(2):return 2;default:return 0;}}", "CS8120", 0)]
    [InlineData("WriteLine(F(Some(2)));static int F(Option<int> o)=>o switch{Some<int>(var _) when true=>1,Some<int>(2)=>2,NoneType=>0};", "CS8510", 19)]
    [InlineData("WriteLine(F(Ok(2)));static int F(Result<int,int> r)=>r switch{Ok<int>(var _)=>1,Ok<int>(2)=>2,Err<int>(var _)=>0};", "CS8510", 0)]
    [InlineData("WriteLine(F(Some(2)));static int F(Option<int> o)=>o switch{Some<int>(>0)=>1,Some<int>(2)=>2,Some<int>(var _)=>3,NoneType=>0};", "CS8510", 20)]
    public async Task Round10_UnionSourcePatternCoverage_RejectsSubsumedVariantPayloads(string source, string diagnostic, int version02Cut)
    {
        if (version02Cut != 0) { await Round12CutSelfTests.AssertRejectedCut(source, version02Cut); return; }
        AssertRejected(source, diagnostic, diagnostic);
    }

    [Theory]
    [InlineData("WriteLine(F(Some(2),false));static int F(Option<int> o,bool b)=>o switch{Some<int>(var _) when b=>1,Some<int>(2)=>2,Some<int>(var _)=>3,NoneType=>0};", "2\n")]
    [InlineData("WriteLine(F(Some(2),false));static int F(Option<int> o,bool b)=>o switch{Some<int>(var _) when b||true=>1,Some<int>(2)=>2,Some<int>(var _)=>3,NoneType=>0};", "1\n")]
    public async Task Round10_UnionSourcePatternCoverage_PreservesNonconstantGuards(string source, string expected)
    {
        await AssertDifferential(source, expected);
    }

    [Fact]
    public async Task Round10_UnionSourcePatternCoverage_StrictTrueGuardRemainsExhaustive_IsRejectedInVersion02()
    {
        const string source = "WriteLine(F(Some(2)));WriteLine(F(None));static int F(Option<int> value)=>value switch{Some<int>(var item) when true=>item,NoneType=>0};";
        await Round12CutSelfTests.AssertRejectedCut(source, 19);
    }

    [Fact]
    public async Task Round10_SourcePatternGuards_KeepPatternBindingsAheadOfEnclosingConstants()
    {
        const string source = "WriteLine(C.F(-1));WriteLine(C.F(2));public static class C{public const int X=5;public static int F(int value){return L(value);static int L(int value)=>value switch{int X when X>0=>X,_=>0};}}";
        await AssertDifferential(source, "0\n2\n");
    }

    [Fact]
    public async Task Round10_UnionSourcePatternCoverage_PreservesNominalSourceRemainder()
    {
        const string source = "WriteLine(F(Some(2)));WriteLine(F(None));static int F(Option<int> value)=>value switch{Some<int>(var item)=>item,NoneType=>0,var _=>3};";
        await AssertDifferential(source, "2\n0\n");
    }

    [Fact]
    public async Task Round10_CollectionStorage_TransparentConversionUsesActualDestination()
    {
        await AssertDifferential("ReadOnlySpan<int> result;{result=(ReadOnlySpan<int>)[2];}WriteLine(result[0]);", "2\n", includeUnoptimized: true);
    }

    [Theory]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){var values=(ReadOnlySpan<int>)[7];return values;}")]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){ReadOnlySpan<int> values=[];return(values=(ReadOnlySpan<int>)[7]);}")]
    public Task Round10_CollectionStorage_TransparentConversionPreservesNamedOwner(string source) =>
        AssertDiagnosticParity(source, "VIB003", "VIB007");

    private static async Task AssertDiagnosticParity(string source, params string[] expectedIds)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r10-binding-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "RejectedSubset", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            var ids = frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray();
            Assert.Equal(expectedIds, ids);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(expectedIds, analyzer.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(expectedIds, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(id => id).ToArray());
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static void AssertRejected(string source, string roslynId, string? selfMessage, string caseName = "", string selfId = "VIB000")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r10-binding-reject-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Rejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == roslynId);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.True(self.Exit != 0, caseName + " unexpectedly passed vib-self check");
            Assert.Contains(selfId, self.Out + self.Err, StringComparison.Ordinal);
            if (selfMessage is not null) Assert.Contains(selfMessage, self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "", bool includeUnoptimized = false)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r10-binding-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round10BindingManaged", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round10BindingManaged.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round10BindingManaged.runtimeconfig.json"), """
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
                foreach (string optimization in includeUnoptimized ? new[] { "-O0", "-O3" } : new[] { "-O3" })
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + optimization + (sanitize ? ".asan" : ".native");
                    var args = new List<string> { optimization, ir, "-o", native };
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
