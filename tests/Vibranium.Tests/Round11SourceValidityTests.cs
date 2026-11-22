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

public sealed class Round11SourceValidityTests
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

    [Theory]
    [InlineData("WriteLine(F(1));static bool F(int x)=>x is >0 and <0;", "CS8518", 20)]
    [InlineData("WriteLine(F(true));static bool F(bool x)=>x is true and false;", "CS8518", 20)]
    [InlineData("WriteLine(F(1));static bool F(int x)=>x is >0 or <=0;", "CS8794", 20)]
    [InlineData("WriteLine(F(true));static bool F(bool x)=>x is true or false;", "CS8794", 0)]
    [InlineData("WriteLine(F(new Box()));static bool F(Box x)=>x is Box and not Box;public sealed class Box{public int Value=1;}", "CS8518", 20)]
    [InlineData("WriteLine(F(new Box()));static bool F(Box x)=>x is Box or not Box;public sealed class Box{public int Value=1;}", "CS8794", 0)]
    [InlineData("int x=1;WriteLine(x is (int));", "CS8794", 0)]
    [InlineData("WriteLine(3.5 is double.NaN);", "CS8519", 0)]
    [InlineData("WriteLine(0.0f is float.NaN);", "CS8519", 0)]
    [InlineData("WriteLine(3.5 is not double.NaN);", "CS8793", 0)]
    [InlineData("const int K=1;WriteLine(0 is K);", "CS8519", 0)]
    [InlineData("WriteLine(0 is >0);", "CS8519", 20)]
    [InlineData("WriteLine(0 is 0);", "CS8520", 0)]
    [InlineData("WriteLine(0 is >-1);", "CS8793", 20)]
    [InlineData("WriteLine(F());static int F(){if(0 is >-1 and var q){}return q;}", "CS8793", 20)]
    [InlineData("WriteLine(F(2));static int F(int x){if(x is (>0 or <=0) and var q){}return q;}", "CS8794", 20)]
    [InlineData("WriteLine(F(1));static bool F(int x)=>x is int;", "CS0183", 0)]
    [InlineData("WriteLine(F(1));static bool F(int x)=>x is string;", "CS0184", 0)]
    [InlineData("int x=1;WriteLine(F(ref x));static int F(in int x)=>x;", "CS9191", 0)]
    public Task SourcePatternProofsAndPromotedWarnings(string source, string roslynId, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertRejected(source, roslynId, "VIB000", noUnassigned: true);

    [Theory]
    [InlineData("int k=1;WriteLine(F(1,k));static int F(int x,int k){switch(x){case k:return 1;default:return 0;}}", 0)]
    [InlineData("WriteLine(F(1,1));static int F(int x,int k)=>x switch{k=>1,_=>0};", 0)]
    [InlineData("WriteLine(F(1,1));static bool F(int x,int k)=>x is >k;", 20)]
    [InlineData("WriteLine(F(1,1));static bool F(int x,int k)=>x is k;", 0)]
    public Task NonconstantPatternOperandsAreRejected(string source, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertRejected(source, "CS9135", "VIB000");

    [Fact]
    public Task SourcePatternAssignment_RelationalAndFormsAreCut() => Round12CutSelfTests.AssertRejectedCut(
        "WriteLine(F(1));static int F(int x){if(x is >-1 and var q){}return q;}", 20);

    [Fact]
    public Task SourceReferencePatternAssignment_RetainsNullFailureBeforeBinding() => AssertRejected(
        "WriteLine(F(new Box()).Value);static Box F(Box x){if(x is Box q){}return q;}public sealed class Box{public int Value=1;}", "CS0165", "VIB001");

    [Theory]
    [InlineData("WriteLine(F(new Box()));static int F(Box x){if(x is Box q)return q.Value;return 0;}public sealed class Box{public int Value=1;}", "1\n", 0)]
    [InlineData("WriteLine(F(new Box()).Value);static Box F(Box x){if(x is var q){}return q;}public sealed class Box{public int Value=1;}", "1\n", 0)]
    [InlineData("int x=1;WriteLine(x is int _);", "True\n", 0)]
    [InlineData("int x=1;WriteLine(x is (int _));", "True\n", 0)]
    [InlineData("WriteLine(F(3));static int F(int x){if(x is var q){}return q;}", "3\n", 0)]
    [InlineData("WriteLine(F(3));static int F(int x){if(x is int q){}return q;}", "3\n", 0)]
    [InlineData("WriteLine(F());static int F(){if(0 is var q){}return q;}", "0\n", 0)]
    [InlineData("WriteLine(F(1));static int F(int x){const int k=1;return x switch{k=>1,_=>0};}", "1\n", 0)]
    [InlineData("WriteLine(F(1));static int F(int x){const int k=1;switch(x){case k:return 1;default:return 0;}}", "1\n", 0)]
    [InlineData("int x=1;WriteLine(F(in x));static int F(in int x)=>x;", "1\n", 0)]
    [InlineData("int x=1;WriteLine(F(in x));static int F(ref readonly int x)=>x;", "1\n", 10)]
    public Task SourcePatternControlsPreserveBindingsAndConstants(string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(source, output);

    [Theory]
    [InlineData("Span<int>s=[1,2];int n=0;foreach(var e in s){n+=e+s.Length;}s[0]=7;WriteLine(n);WriteLine(s[0]);", "7\n7\n")]
    [InlineData("Span<int>s=[1,2];int n=0;foreach(ref readonly var e in s){n+=e+s.Length;}WriteLine(n);", "7\n")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){WriteLine(s[0]);break;}s[0]=7;WriteLine(s[0]);", "1\n7\n")]
    [InlineData("Span<int>s=[1,2];foreach(var e in Move(s))WriteLine(e);", "1\n2\n")]
    [InlineData("Span<int>s=[1,2];foreach(ref var e in s)e+=1;WriteLine(s[0]);", "2\n")]
    [InlineData("Span<int>s=[1,2];ReadOnlySpan<int>r=s;WriteLine(r[0]);s[0]=7;WriteLine(s[0]);", "1\n7\n")]
    [InlineData("WriteLine(F());static int F(){Span<int>s=[1,2];foreach(var e in s)return e+s.Length;return 0;}", "3\n")]
    public Task ForeachSharedReborrowAndOrdinaryNllControls(string source, string output) => AssertDifferential(source, output);

    [Theory]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){s[0]=e;}")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){s[0]=e;break;}")]
    [InlineData("F();static void F(){Span<int>s=[1,2];foreach(var e in s){s[0]=e;return;}}")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){s[0]=e;VibraniumRuntime.Panic(\"stop\");}")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){s[0]=e;while(true){}}")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){Mutate(s);WriteLine(e);}static void Mutate(Span<int>s){s[0]=0;}")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){Span<int>t=s.Slice(0);WriteLine(t[0]+e);break;}")]
    [InlineData("Span<int>s=[1,2];foreach(var e in s){Span<int>t=Move(s);WriteLine(t[0]+e);break;}")]
    [InlineData("Span<int>s=[1,2];foreach(ref var e in s){e=7;WriteLine(s.Length);break;}")]
    public Task ForeachLoanRemainsLiveThroughAllBodyExits(string source) => AssertSubsetRejected(source, "VIB003");

    [Theory]
    [InlineData("WriteLine(F(0));WriteLine(F(0L));static bool F<T>(T x)=>x is 0;", 0)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static bool F<T>(T x)=>x is >0 or <=0;", 20)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static bool F<T>(T x)=>x is int;", 0)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static bool F<T>(T x)=>x is T y;", 0)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static bool F<T>(T x)=>x is int and >0;", 20)]
    [InlineData("WriteLine(F(new Box()));static int F<T>(T x)where T:class=>x is Box b?b.Value:0;public sealed class Box{public int Value=7;}", 0)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static int F<T>(T x)=>x switch{>0=>1,_=>0};", 20)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static int F<T>(T x)=>x switch{int i=>i,_=>0};", 0)]
    [InlineData("WriteLine(F(5));WriteLine(F(5L));static int F<T>(T x){switch(x){case 5:return 1;default:return 0;}}", 0)]
    public Task GenericPatterns_UseTheExplicitSourceCut(string source, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSubsetRejected(source, "VIB000",
        "Patterns other than var and discard on a value of generic type parameter type are not supported in Vibranium v0.2.");

    [Theory]
    [InlineData("WriteLine(F(5));WriteLine(F(7L));static T F<T>(T x){if(x is var v)return v;return x;}", "5\n7\n")]
    [InlineData("WriteLine(F(5));WriteLine(F(7L));static int F<T>(T x)=>x switch{_=>3};", "3\n3\n")]
    [InlineData("WriteLine(F(5));WriteLine(F(7L));static T F<T>(T x)=>x switch{var v=>v};", "5\n7\n")]
    public Task GenericVarAndDiscardPatternsRemainSupported(string source, string output) => AssertDifferential(source, output);

    [Theory]
    [InlineData("WriteLine(F(3));static int F(ushort x){int r=0;switch(x){case >=(ushort)0:r=1;break;default:WriteLine(9);}return r;}", "1\n", 20)]
    [InlineData("WriteLine(F(3));WriteLine(F(-3));static int F(int x){int r=0;switch(x){case >=0:r=1;break;case <0:r=2;break;default:WriteLine(9);}return r;}", "1\n2\n", 20)]
    [InlineData("WriteLine(F(true));WriteLine(F(false));static int F(bool x){int r=0;switch(x){case true:r=1;break;case false:r=2;break;default:WriteLine(9);}return r;}", "1\n2\n", 0)]
    [InlineData("WriteLine(F(3));static int F(int x){int r=0;switch(x){case >=0 when 1<2:r=1;break;case <0:r=2;break;default:WriteLine(9);}return r;}", "1\n", 19)]
    [InlineData("WriteLine(F(E.A));static int F(E x){int r=0;switch(x){case E:r=1;break;default:WriteLine(9);}return r;}public enum E{A=1,B=2}", "1\n", 0)]
    public Task ExhaustiveIntegralAndBoolSwitches_ExcludeDefaultEndpoints(string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(source, output, expectedWarning: "CS0162");

    [Theory]
    [InlineData("WriteLine(F(0));static int F(int x){switch(x){case >0:return 1;case <0:return 2;default:WriteLine(9);}return 0;}", 20)]
    [InlineData("WriteLine(F(0,true));static int F(int x,bool guard){switch(x){case >=0 when guard:return 1;case <0:return 2;default:WriteLine(9);}return 0;}", 20)]
    [InlineData("WriteLine(F(E.A));static int F(E x){switch(x){case E.A:return 1;case E.B:return 2;default:WriteLine(9);}return 0;}public enum E{A=1,B=2}", 0)]
    public Task NonExhaustiveSwitches_StillCheckDefaultEndpoints(string source, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertRejected(source, "CS8070", "VIB000");

    [Fact]
    public Task ForeachExplicitMoveConsumesItsCollection() => AssertSubsetRejected(
        "Span<int>s=[1,2];foreach(var e in Move(s)){WriteLine(e);}WriteLine(s.Length);", "VIB001");

    [Theory]
    [InlineData("int x=1;ref readonly int r=ref x;WriteLine(M(ref r,2));static int M(ref readonly int p,int k)=>p+k;", "CS1510", 10)]
    [InlineData("int x=1;WriteLine(N(in x));static int N(ref readonly int q)=>M(ref q,2);static int M(ref readonly int p,int k)=>p+k;", "CS8329", 10)]
    [InlineData("int x=1;WriteLine(N(in x));static int N(in int q)=>M(ref q,2);static int M(ref readonly int p,int k)=>p+k;", "CS8329", 10)]
    public Task RefReadonlyTargets_DoNotMakeReadonlyRefOperandsWritable(string source, string roslynId, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertRejected(source, roslynId, "VIB004");

    [Theory]
    [InlineData("int x=1;ref readonly int r=ref x;WriteLine(M(in r,2));static int M(ref readonly int p,int k)=>p+k;", 10)]
    [InlineData("int x=1;ref int r=ref x;WriteLine(M(ref r,2));static int M(ref readonly int p,int k)=>p+k;", 10)]
    [InlineData("int x=1;WriteLine(N(in x));static int N(ref readonly int q)=>M(in q,2);static int M(ref readonly int p,int k)=>p+k;", 10)]
    [InlineData("int x=1;WriteLine(N(in x));static int N(in int q)=>M(in q,2);static int M(ref readonly int p,int k)=>p+k;", 10)]
    public Task RefReadonlyArguments_PreserveInAndMutableRefControls(string source, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(source, "3\n");

    [Theory]
    [InlineData("WriteLine(F(double.NegativeInfinity));WriteLine(F(double.PositiveInfinity));WriteLine(F(double.NaN));static int F(double x){switch(x){case <0:return 1;case >=0:return 2;case double.NaN:return 3;default:Vec<int>v=[1];var w=v;return v.Count;}}", "1\n2\n3\n", 18)]
    [InlineData("WriteLine(F(float.NegativeInfinity));WriteLine(F(float.PositiveInfinity));WriteLine(F(float.NaN));static int F(float x){switch(x){case <0:return 1;case >=0:return 2;case float.NaN:return 3;default:Vec<int>v=[1];var w=v;return v.Count;}}", "1\n2\n3\n", 18)]
    [InlineData("WriteLine(F(0.0));WriteLine(F(double.NaN));static int F(double x){switch(x){case not double.NaN:return 1;case double.NaN:return 2;default:Vec<int>v=[1];var w=v;return v.Count;}}", "1\n2\n", 18)]
    [InlineData("WriteLine(F(3.5));static int F(double x){int r=0;switch(x){case <0:r=1;break;case >=0:r=2;break;case double.NaN:r=3;break;default:WriteLine(9);}return r;}", "2\n", 20)]
    public Task ExhaustiveFloatingSwitches_ExcludeDeadDefaultOwnership(string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(source, output, expectedWarning: "CS0162");

    [Theory]
    [InlineData("WriteLine(F(3.5));static int F(double x){switch(x){case <0:return 1;case >=0:return 2;default:Vec<int>v=[1];var w=v;return v.Count;}}", 18)]
    [InlineData("WriteLine(F(3.5f));static int F(float x){switch(x){case <0:return 1;case >=0:return 2;default:Vec<int>v=[1];var w=v;return v.Count;}}", 18)]
    [InlineData("WriteLine(F(3.5));static int F(double x){switch(x){case <0:return 1;case >0:return 2;case double.NaN:return 3;default:Vec<int>v=[1];var w=v;return v.Count;}}", 18)]
    public Task FloatingSwitchCoverageGaps_KeepDefaultOwnershipObligations(string source, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSubsetRejected(source, "VIB001");

    [Theory]
    [InlineData("WriteLine(F(double.NegativeInfinity));WriteLine(F(double.PositiveInfinity));WriteLine(F(double.NaN));static int F(double x){switch(x){case <0:return 1;case >=0:return 2;default:return 3;}}", 20)]
    [InlineData("WriteLine(F(float.NegativeInfinity));WriteLine(F(float.PositiveInfinity));WriteLine(F(float.NaN));static int F(float x){switch(x){case <0:return 1;case >=0:return 2;default:return 3;}}", 20)]
    public Task FloatingSwitchNaNGap_ExecutesTheLiveDefault(string source, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(source, "1\n2\n3\n");

    [Theory]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){switch(x){case Ok<int>(var v) when false:case Err<int>(var e):return e;default:return 0;}}", "7\n0\n", 19)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){switch(x){case Err<int>(var e) when false:case Ok<int>(var v):return v;default:return 0;}}", "0\n4\n", 19)]
    [InlineData("Option<int>a=Some(5);Option<int>b=None;WriteLine(F(a));WriteLine(F(b));static int F(Option<int>x){switch(x){case NoneType when false:case Some<int>(var v):return v;default:return 0;}}", "5\n0\n", 19)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){int acc=0;switch(x){case Ok<int>(var v) when false:case Err<int>(var e):acc+=e;break;}return acc;}", "7\n0\n", 19)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){switch(x){case Ok<int>(var v) when 1>2:case Err<int>(var e):return e;default:return 0;}}", "7\n0\n", 19)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){switch(x){case Ok<int>(var v):case Err<int>(var e):e=9;return e;default:return 0;}}", "9\n9\n", 0)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){bool gate=true;switch(x){case Ok<int>(var v) when gate:case Err<int>(var e):e=9;return e;default:return 0;}}", "9\n9\n", 0)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){switch(x){case Ok<int>(>0 and var v) when false:case Err<int>(var e):return e;default:return 0;}}", "7\n0\n", 19)]
    [InlineData("Result<int,int>a=Err(7);Result<int,int>b=Ok(4);WriteLine(F(a));WriteLine(F(b));static int F(Result<int,int>x){switch(x){case Ok<int>(var v) ok when false:case Err<int>(var e) err:return e+err.Error;default:return 0;}}", "14\n0\n", 19)]
    [InlineData("Option<Cell>a=Some(new Cell(5));Option<Cell>b=None;WriteLine(F(a));WriteLine(F(b));static int F(Option<Cell>x){switch(x){case NoneType when false:case Some<Cell>(var c):return c.Value;default:return 0;}}public sealed class Cell(int v){public int Value=v;}", "5\n0\n", 19)]
    public Task UnionSwitchSections_BindSiblingPatternsWithoutInventingAssignments(string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(source, output);

    [Theory]
    [InlineData("Result<int,int>r=Err(7);WriteLine(F(r));static int F(Result<int,int>x){switch(x){case Ok<int>(var v):case Err<int>(var e):return e;default:return 0;}}", "CS0165", "VIB001", 0)]
    [InlineData("Result<int,int>r=Err(7);WriteLine(F(r,false));static int F(Result<int,int>x,bool gate){switch(x){case Ok<int>(var v) when gate:case Err<int>(var e):return e;default:return 0;}}", "CS0165", "VIB001", 0)]
    [InlineData("Result<int,int>r=Err(7);WriteLine(F(r));static int F(Result<int,int>x){switch(x){case Ok<int>(var v) when false:case Err<int>(var e):return missing;default:return 0;}}", "CS0103", "VIB000", 19)]
    [InlineData("Result<int,int>r=Err(7);WriteLine(F(r));static int F(Result<int,int>x){switch(x){case Ok<int>(var v) when false:case Err<int>(var e):break;}return e;}", "CS0103", "VIB000", 19)]
    public Task UnionSwitchSiblingBindings_PreserveSourceRejections(string source, string roslynId, string selfId, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertRejected(source, roslynId, selfId, requireSelfRoslynCode: false);

    [Fact]
    public Task UnionSwitchSiblingBindings_DoNotHideLiveOwnershipErrors() => AssertSubsetRejected(
        "Result<int,int>r=Err(7);WriteLine(F(r));static int F(Result<int,int>x){switch(x){case Ok<int>(var v):return v;case Err<int>(var e):{Vec<int>owned=[e];var moved=owned;return owned.Count;}default:return 0;}}", "VIB001");

    private static async Task AssertRejected(string source, string roslynId, string selfId, bool noUnassigned = false, bool requireSelfRoslynCode = true)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-source-reject-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Rejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), d => d.Id == roslynId);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
            var all = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAllDiagnosticsAsync();
            Assert.Contains(all, d => d.Severity == DiagnosticSeverity.Error);
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Contains(selfId, self.Out + self.Err, StringComparison.Ordinal);
            if (selfId == "VIB000" && requireSelfRoslynCode) Assert.Contains(roslynId, self.Out + self.Err, StringComparison.Ordinal);
            if (noUnassigned) Assert.DoesNotContain("VIB001", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static async Task AssertSubsetRejected(string source, string id, string? message = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-source-borrow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "RejectedBorrow", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(new[] { id }, frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x));
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { id }, analyzer.Select(d => d.Id).Distinct().OrderBy(x => x));
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(new[] { id }, Regex.Matches(self.Out + self.Err, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(x => x));
            if (message is not null)
            {
                Assert.Single(frontend.Diagnostics);
                Assert.Single(analyzer);
                Assert.Single(Regex.Matches(self.Out + self.Err, @"VIB\d{3}"));
                Assert.Equal(message, frontend.Diagnostics[0].Message);
                Assert.Equal(message, analyzer[0].GetMessage());
                Assert.Contains(message, self.Out + self.Err, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static async Task AssertDifferential(string source, string expected, int expectedExit = 0, string expectedError = "", bool includeUnoptimized = true, string? expectedWarning = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-r11-source-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "Round11SourceManaged", includeStd: true);
            var sourceDiagnostics = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            if (expectedWarning is null) Assert.Empty(sourceDiagnostics);
            else
            {
                Assert.NotEmpty(sourceDiagnostics);
                Assert.All(sourceDiagnostics, d => { Assert.Equal(expectedWarning, d.Id); Assert.Equal(DiagnosticSeverity.Warning, d.Severity); });
            }
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Empty(frontend.Diagnostics);
            var analyzer = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Empty(analyzer);
            string dll = Path.Combine(temp, "Round11SourceManaged.dll");
            string bootstrap = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                File.ReadAllText(bootstrap), new CSharpParseOptions(LanguageVersion.CSharp15), path: bootstrap));
            var emit = managedCompilation.Emit(dll);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(temp, "Vibranium.Core.dll"));
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(temp, "Vibranium.Std.dll"));
            File.WriteAllText(Path.Combine(temp, "Round11SourceManaged.runtimeconfig.json"), """
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
