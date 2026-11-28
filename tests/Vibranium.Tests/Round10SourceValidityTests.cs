using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

public sealed class Round10SourceValidityTests
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
    [InlineData("default-enum", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");return [E.A];}public enum E{A=7}", "", 101, "panic: stop\n")]
    [InlineData("explicit-int-enum", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");return [E.A,E.B];}public enum E:int{A=-2147483648,B=2147483647}", "", 101, "panic: stop\n")]
    [InlineData("enum-cast", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");return [(E)7];}public enum E{A=7}", "", 101, "panic: stop\n")]
    [InlineData("enum-zero", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");return [0];}public enum E{A=7}", "", 101, "panic: stop\n")]
    [InlineData("enum-source-constant", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");const E value=E.A;return [value];}public enum E{A=7}", "", 101, "panic: stop\n")]
    [InlineData("enum-view-local", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");ReadOnlySpan<E> values=[E.A];return values;}public enum E{A=7}", "", 101, "panic: stop\n")]
    [InlineData("enum-readonly-ref", "WriteLine((int)F());static ref readonly E F(){VibraniumRuntime.Panic(\"stop\");ReadOnlySpan<E> values=[E.A];return ref values[0];}public enum E{A=7}", "", 101, "panic: stop\n")]
    [InlineData("live-local-readonly", "ReadOnlySpan<E> values=[E.A,(E)8];WriteLine((int)values[0]);WriteLine((int)values[1]);public enum E{A=7}", "7\n8\n", 0, "")]
    [InlineData("live-local-mutable", "Span<E> values=[E.A];values[0]=(E)9;WriteLine((int)values[0]);public enum E{A=7}", "9\n", 0, "")]
    public Task ConstantEnumCollection_PreservesSourceAndRuntimeContexts(
        string caseName, string source, string output, int exitCode, string error) =>
        AssertDifferential(caseName, source, output, exitCode, error);

    [Theory]
    [InlineData("dynamic-enum", "WriteLine((int)F(E.A)[0]);static ReadOnlySpan<E> F(E value){VibraniumRuntime.Panic(\"stop\");return [value];}public enum E{A=7}", "CS9203", 0)]
    [InlineData("nonconstant-local", "WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){VibraniumRuntime.Panic(\"stop\");E value=E.A;return [value];}public enum E{A=7}", "CS9203", 0)]
    [InlineData("mixed-elements", "WriteLine((int)F(E.A)[0]);static ReadOnlySpan<E> F(E value){VibraniumRuntime.Panic(\"stop\");return [E.A,value];}public enum E{A=7}", "CS9203", 0)]
    [InlineData("folded-not-source-constant", "WriteLine((int)F(true)[0]);static ReadOnlySpan<E> F(bool b){VibraniumRuntime.Panic(\"stop\");return [b?E.A:E.A];}public enum E{A=7}", "CS9203", 0)]
    [InlineData("mutable-return", "WriteLine((int)F()[0]);static Span<E> F(){VibraniumRuntime.Panic(\"stop\");return [E.A];}public enum E{A=7}", "CS9203", 0)]
    [InlineData("mutable-ref-return", "WriteLine((int)F());static ref E F(){VibraniumRuntime.Panic(\"stop\");Span<E> values=[E.A];return ref values[0];}public enum E{A=7}", "CS8352", 0)]
    [InlineData("native-integer-not-eligible", "WriteLine((int)F()[0]);static ReadOnlySpan<nint> F(){VibraniumRuntime.Panic(\"stop\");return [(nint)7];}", "CS9203", 17)]
    public Task NonconstantOrMutableCollection_RemainsSourceInvalid(string caseName, string source, string roslynId, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSourceRejection(caseName, source, roslynId, "VIB007");

    [Theory]
    [InlineData("D11_struct_full_fields","WriteLine(F().B); static S F() {     S s;     s.A = 1;     s.B = 2;     return s; } public struct S { public int A; public int B; }","2\n")]
    [InlineData("D25_struct_branch_init","WriteLine(F(true)); WriteLine(F(false)); static int F(bool c) {     S s;     if (c) { s.A = 1; s.B = 2; } else { s.A = 3; s.B = 4; }     return s.A + s.B; } public struct S { public int A; public int B; }","3\n7\n")]
    [InlineData("D26_struct_out_fieldwise","M(out S s); WriteLine(s.A + s.B); static void M(out S s) { s.A = 1; s.B = 2; } public struct S { public int A; public int B; }","3\n")]
    [InlineData("D28_struct_read_field_after_field_assign","WriteLine(F()); static int F() {     S s;     s.A = 4;     return s.A; } public struct S { public int A; public int B; }","4\n")]
    [InlineData("nested-completion","Outer o;o.P.X=2;o.P.Y=3;o.Z=4;WriteLine(Sum(o));static int Sum(Outer o)=>o.P.X+o.P.Y+o.Z;public struct Pair{public int X;public int Y;}public struct Outer{public Pair P;public int Z;}","9\n")]
    [InlineData("nested-initialized-field","Outer o;o.P.X=2;WriteLine(o.P.X);public struct Pair{public int X;public int Y;}public struct Outer{public Pair P;public int Z;}","2\n")]
    [InlineData("empty-struct","Empty e;Take(e);static void Take(Empty e){WriteLine(1);}public struct Empty{}","1\n")]
    [InlineData("empty-nested-completion","Outer o;o.X=2;Take(o);static void Take(Outer o){WriteLine(o.X);}public struct Empty{}public struct Outer{public Empty E;public int X;}","2\n")]
    [InlineData("field-branch-common","Pair p;if(Read(true)){p.X=2;}else{p.X=3;}WriteLine(p.X);static bool Read(bool b)=>b;public struct Pair{public int X;public int Y;}","2\n")]
    [InlineData("whole-then-field","Pair p;p=new Pair();p.X=3;WriteLine(p.X+p.Y);public struct Pair{public int X;public int Y;}","3\n")]
    [InlineData("O01_selector", "WriteLine(F());static int F(){Pair p;p.Y=2;switch(Set(out p.X)){case 1:break;}return p.X+p.Y;}static int Set(out int x){x=3;return 0;}public struct Pair{public int X;public int Y;}", "5\n")]
    [InlineData("O03_multi", "Pair p;Set(out p.X,out p.Y);WriteLine(p.X+p.Y);static void Set(out int x,out int y){x=3;y=2;}public struct Pair{public int X;public int Y;}", "5\n")]
    [InlineData("O06_nested_initialized_prefix", "Outer p;p.A=new Pair();Set(out p.A.X);WriteLine(p.A.X);static void Set(out int x){x=3;}public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}", "3\n")]
    [InlineData("O09_out_parameter", "Make(out Pair p);WriteLine(p.X+p.Y);static void Make(out Pair p){Set(out p.X,out p.Y);}static void Set(out int x,out int y){x=3;y=2;}public struct Pair{public int X;public int Y;}", "5\n")]
    [InlineData("O10_named_reordered", "Pair p;Set(x:out p.X,y:out p.Y);WriteLine(p.X+p.Y);static void Set(out int x,out int y){x=3;y=2;}public struct Pair{public int X;public int Y;}", "5\n")]
    [InlineData("O11_after_failure_false", "Pair p;p.Y=2;if(Try(out p.X))WriteLine(9);WriteLine(p.X+p.Y);static bool Try(out int x){x=3;return false;}public struct Pair{public int X;public int Y;}", "5\n")]
    [InlineData("nested-partial-out", "Outer p;Set(out p.A.X,out p.A.Y);p.B=3;WriteLine(p.A.X+p.A.Y+p.B);static void Set(out int x,out int y){x=1;y=2;}public struct Pair{public int X;public int Y;}public struct Outer{public Pair A;public int B;}", "6\n")]
    [InlineData("copy-result-fields", "Holder h;h.X=1;h.R=Ok(2);WriteLine(h.X);public struct Holder{public int X;public Result<int,int> R;}", "1\n")]
    public Task RuntimeStructInitialization_TracksFieldsAndPreservesCleanup(string caseName, string source, string output) =>
        AssertDifferential(caseName, source, output, 0, "");

    [Theory]
    [InlineData("partial-whole","WriteLine(F().X);static Pair F(){Pair p;p.X=1;return p;}public struct Pair{public int X;public int Y;}","CS0165")]
    [InlineData("partial-sibling","Pair p;p.X=1;WriteLine(p.Y);public struct Pair{public int X;public int Y;}","CS0170")]
    [InlineData("partial-branch","Pair p;if(Read(true)){p.X=1;}else{p.Y=2;}WriteLine(p.X);static bool Read(bool b)=>b;public struct Pair{public int X;public int Y;}","CS0170")]
    [InlineData("partial-method-receiver","Pair p;p.X=1;WriteLine(p.Sum());public struct Pair{public int X;public int Y;public readonly int Sum()=>X+Y;}","CS0165")]
    [InlineData("partial-nested","Outer o;o.P.X=1;o.Z=2;WriteLine(o.P.Y);public struct Pair{public int X;public int Y;}public struct Outer{public Pair P;public int Z;}","CS0170")]
    [InlineData("uninitialized-class-container","Box b;b.P.X=1;public sealed class Box{public Pair P;}public struct Pair{public int X;public int Y;}","CS0165")]
    [InlineData("uninitialized-index-container","Span<Pair> p;p[0].X=1;public struct Pair{public int X;public int Y;}","CS0165")]
    [InlineData("partial-out","F(out Pair p);WriteLine(p.X);static void F(out Pair p){p.X=1;}public struct Pair{public int X;public int Y;}","CS0177")]
    public Task RuntimeStructInitialization_PartialReadsRemainSourceInvalid(string caseName, string source, string roslynId) =>
        AssertSourceRejection(caseName, source, roslynId, "VIB001");

    [Fact]
    public Task RuntimeStructInitialization_LinearFieldsRemainForbidden() =>
        AssertSubsetRejection("WriteLine(F()); static int F() {     S s;     s.R = new Res(4);     s.X = 1;     int v = s.R.Value + s.X;     s.R.Dispose();     return v; } public sealed class Res : IDisposable { public int Value; public Res(int v) { Value = v; } public void Dispose() { WriteLine(\"dispose\"); } } public struct S { public Res R; public int X; }", new[] { "VIB006" }, new[] { "VIB006" });

    [Theory]
    [InlineData("Holder h=new Holder(new Vec<int>(),2);Holder other=h;WriteLine(other.X);WriteLine(h.X);public struct Holder{public Vec<int> V;public int X;public Holder(Vec<int> v,int x){V=v;X=x;}}")]
    [InlineData("Holder h=new Holder(new Vec<int>(),2);Holder other=h;WriteLine(other.X);h.X=3;public struct Holder{public Vec<int> V;public int X;public Holder(Vec<int> v,int x){V=v;X=x;}}")]
    public Task RuntimeStructInitialization_MovedParentsStillInvalidateFields(string source) =>
        AssertSubsetRejection(source, new[] { "VIB001" }, new[] { "VIB001" });

    [Theory]
    [InlineData("conditional-vec-index", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Vec<int> v=[1,2];(b?ref v[0]:ref v[1])=Flag(false)?7:8;return v[0]*10+v[1];}static bool Flag(bool b)=>b;", "82\n18\n", 15)]
    [InlineData("conditional-span-index", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Span<int> v=[1,2];(b?ref v[0]:ref v[1])=Flag(false)?7:8;return v[0]*10+v[1];}static bool Flag(bool b)=>b;", "82\n18\n", 15)]
    [InlineData("conditional-vec-struct-field", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Vec<S> v=[new S(1),new S(2)];(b?ref v[0].X:ref v[1].X)=Flag(false)?7:8;return v[0].X*10+v[1].X;}static bool Flag(bool b)=>b;public struct S{public int X;public S(int x){X=x;}}", "82\n18\n", 15)]
    [InlineData("conditional-heap-field", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){var x=new Box(1);var y=new Box(2);(b?ref x.X:ref y.X)=Flag(false)?7:8;return x.X*10+y.X;}static bool Flag(bool b)=>b;public sealed class Box{public int X;public Box(int x){X=x;}}", "82\n18\n", 15)]
    [InlineData("conditional-scalar-local", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){int x=1;int y=2;(b?ref x:ref y)=Flag(false)?7:8;return x*10+y;}static bool Flag(bool b)=>b;", "82\n18\n", 15)]
    [InlineData("conditional-whole-struct", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){S x=new S(1);S y=new S(2);(b?ref x:ref y)=new S(8);return x.X*10+y.X;}public struct S{public int X;public S(int x){X=x;}}", "82\n18\n", 15)]
    [InlineData("nested-conditional-vec-index", "WriteLine(F(true,false));WriteLine(F(false,true));WriteLine(F(false,false));static int F(bool a,bool b){Vec<int> v=[1,2,3];(a?ref v[0]:ref (b?ref v[1]:ref v[2]))=9;return v[0]*100+v[1]*10+v[2];}", "923\n193\n129\n", 15)]
    [InlineData("conditional-index-evaluation-order", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Vec<int> v=[1,2];(b?ref v[At(0)]:ref v[At(1)])=Flag(false)?7:8;return v[0]*10+v[1];}static int At(int i){WriteLine(i);return i;}static bool Flag(bool b)=>b;", "0\n82\n1\n18\n", 15)]
    [InlineData("single-vec-index-snapshot", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Vec<int> v=[1,2];v[0]=b?7:8;return v[0]*10+v[1];}", "72\n82\n", 0)]
    [InlineData("single-heap-field-snapshot", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){var x=new Box(1);x.X=b?7:8;return x.X;}public sealed class Box{public int X;public Box(int x){X=x;}}", "7\n8\n", 0)]
    public Task RuntimeReferenceSnapshots_OnlyStableStructPathsAffectInitialization(string caseName, string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(caseName, source, output, 0, "", includeUnoptimized: true);

    [Fact]
    public Task RuntimeReferenceSnapshots_RejectedOwnerMoveHasOnlyActivationDiagnostic() =>
        AssertSubsetRejection(
            "class Node{public int V;public Option<Node> Next;public Node(int v,Option<Node> n){V=v;Next=n;}}static class App{static int Main(){var a=new Node(1,None);a.Next=Some(a);return 0;}}",
            new[] { "VIB003" }, new[] { "VIB003" });

    [Fact]
    public Task ReturnValueSnapshot_PreservesEvaluationBeforeCleanup() =>
        AssertDifferential("cleanup-return-value", "WriteLine(F());static int F(){using var r=new R(1);return r.Value;}public sealed class R:IDisposable{public int Value;public R(int value){Value=value;}[Mut]public void Dispose(){Value=2;WriteLine(Value);}}", "2\n1\n", 0, "");

    [Theory]
    [InlineData("D27_struct_heap_field_fieldwise", "WriteLine(F()); static int F() {     H h;     h.N = 3;     h.V = new Vec<int>();     h.V.Add(7);     return h.N + h.V[0]; } public struct H { public int N; public Vec<int> V; }")]
    [InlineData("O7a_class_field_return", "WriteLine(F().N.Value); static S F() {     S s;     s.N = new Node(7);     s.X = 2;     return s; } public sealed class Node { public int Value; public Node(int v) { Value = v; } } public struct S { public Node N; public int X; }")]
    [InlineData("O7b_class_field_scope_exit_partial", "WriteLine(F()); static int F() {     S s;     s.N = new Node(7);     return 1; } public sealed class Node { public int Value; public Node(int v) { Value = v; } } public struct S { public Node N; public int X; }")]
    [InlineData("O7c_vec_field_full_scope_exit", "WriteLine(F()); static int F() {     S s;     s.V = new Vec<int>();     s.X = 2;     s.V.Add(5);     return s.V[0] + s.X; } public struct S { public Vec<int> V; public int X; }")]
    [InlineData("O7d_vec_field_partial_scope_exit", "WriteLine(F()); static int F() {     S s;     s.V = new Vec<int>();     s.V.Add(5);     return s.V[0]; } public struct S { public Vec<int> V; public int X; }")]
    [InlineData("O7e_vec_field_full_move_whole", "WriteLine(F()); static int F() {     S s;     s.V = new Vec<int>();     s.X = 2;     S t = s;     t.V.Add(9);     return t.V[0] + t.X; } public struct S { public Vec<int> V; public int X; }")]
    [InlineData("O7f_reassign_field_after_full", "WriteLine(F()); static int F() {     S s;     s.V = new Vec<int>();     s.X = 2;     s.V = new Vec<int>();     s.V.Add(3);     return s.V.Count + s.X; } public struct S { public Vec<int> V; public int X; }")]
    [InlineData("owned-branch-completion", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Holder h;if(b){h.V=new Vec<int>();h.X=1;}else{h.V=new Vec<int>();h.X=2;}h.V.Add(4);return h.V[0]+h.X;}public struct Holder{public Vec<int> V;public int X;}")]
    [InlineData("owned-partial-branch-cleanup", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){Holder h;if(b){h.V=new Vec<int>();h.V.Add(4);}return 1;}public struct Holder{public Vec<int> V;public int X;}")]
    [InlineData("nested-owned-partial-cleanup", "WriteLine(F());static int F(){Outer o;o.H.V=new Vec<int>();o.H.V.Add(5);return o.H.V[0];}public struct Outer{public Holder H;public int X;}public struct Holder{public Vec<int> V;public Node N;}public sealed class Node{public int Value;}")]
    [InlineData("owned-out-field", "Holder h;Make(out h.V);h.X=2;WriteLine(h.X);static void Make(out Vec<int> v){v=new Vec<int>();v.Add(3);}public struct Holder{public Vec<int> V;public int X;}")]
    [InlineData("owned-nested-out", "Outer o;Make(out o.H.V);o.H.X=2;o.Z=3;WriteLine(o.H.X+o.Z);static void Make(out Vec<int> v){v=new Vec<int>();v.Add(3);}public struct Holder{public Vec<int> V;public int X;}public struct Outer{public Holder H;public int Z;}")]
    [InlineData("owned-option-field", "Holder h;h.X=1;h.N=None;WriteLine(h.X);public sealed class Node{}public struct Holder{public int X;public Option<Node> N;}")]
    public Task OwnedStructInitialization_PiecewiseStorageIsOutsideV02(string caseName, string source)
    {
        _ = caseName;
        return AssertSubsetRejection(source, new[] { "VIB000" }, new[] { "VIB000" },
            "A struct with owned fields must be initialized in one assignment; initializing it field by field (including through out) is not supported in Vibranium v0.2.");
    }

    [Theory]
    [InlineData("whole-owned-struct", "Holder h=new Holder(new Vec<int>(),2);h.V.Add(5);WriteLine(h.V[0]+h.X);public struct Holder{public Vec<int> V;public int X;public Holder(Vec<int> v,int x){V=v;X=x;}}", "7\n")]
    [InlineData("initialized-owned-field-overwrite", "Holder h=new Holder(new Vec<int>(),2);h.V.Add(9);h.V=new Vec<int>();h.V.Add(3);WriteLine(h.V.Count+h.X);public struct Holder{public Vec<int> V;public int X;public Holder(Vec<int> v,int x){V=v;X=x;}}", "3\n")]
    [InlineData("initialized-owned-out-field", "Holder h=new Holder(new Vec<int>(),2);h.V.Add(9);Make(out h.V);WriteLine(h.V[0]+h.X);static void Make(out Vec<int> v){v=new Vec<int>();v.Add(3);}public struct Holder{public Vec<int> V;public int X;public Holder(Vec<int> v,int x){V=v;X=x;}}", "5\n")]
    [InlineData("class-resource-acquisition-transfer", "R r=new R();Consume(r);static void Consume(R r){using var owned=r;WriteLine(1);}public sealed class R:IDisposable{public void Dispose(){WriteLine(2);}}", "1\n2\n")]
    public Task OwnedStructInitialization_WholeConstructionAndInitializedWritesRemainValid(string caseName, string source, string output) =>
        AssertDifferential(caseName, source, output, 0, "", includeUnoptimized: true);

    [Theory]
    [InlineData("R r;Consume(r);static void Consume(R r){using var owned=r;}public struct R:IDisposable{public void Dispose(){WriteLine(1);}}")]
    [InlineData("using var r=Make();static R Make(){R r;return r;}public struct R:IDisposable{public void Dispose(){WriteLine(1);}}")]
    [InlineData("R r;using var owned=r;public struct R:IDisposable{public void Dispose(){WriteLine(1);}}")]
    public Task UnsupportedStructResource_UnacquiredStorageIsNotVacuouslyInitialized(string source) =>
        AssertSubsetRejection(source, new[] { "VIB000", "VIB001" }, new[] { "VIB000", "VIB001" });

    [Fact]
    public Task UnsupportedStructResource_ExplicitEmptyConstructionPreservesDiagnosticParity() =>
        AssertSubsetRejection("using var r=new R();WriteLine(1);public struct R:IDisposable{public void Dispose(){WriteLine(2);}}",
            new[] { "VIB000" }, new[] { "VIB000" });

    [Theory]
    [InlineData("N01-block-literal-assignment", "ReadOnlySpan<int> result;{result=[2];}WriteLine(result[0]);", "2\n", 0)]
    [InlineData("N02-block-argument-temp", "ReadOnlySpan<int> result;{result=Id([2]);}WriteLine(result[0]);static ReadOnlySpan<int> Id(ReadOnlySpan<int> p)=>p;", "2\n", 0)]
    [InlineData("N04-block-argument-named-alias", "ReadOnlySpan<int> result;{ReadOnlySpan<int> inner=Id([2]);result=inner;}WriteLine(result[0]);static ReadOnlySpan<int> Id(ReadOnlySpan<int> p)=>p;", "2\n", 0)]
    [InlineData("N05-block-argument-consumed", "ReadOnlySpan<int> result=[1];{WriteLine(Id([2])[0]);}WriteLine(result[0]);static ReadOnlySpan<int> Id(ReadOnlySpan<int> p)=>p;", "2\n1\n", 0)]
    [InlineData("N07-loop-literal-assignment", "ReadOnlySpan<int> result=[];for(int i=0;i<1;i++){result=[2];}WriteLine(result[0]);", "2\n", 0)]
    [InlineData("conditional-both-branches", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){ReadOnlySpan<int> result;{result=b?[2]:[3];}return result[0];}", "2\n3\n", 16)]
    [InlineData("retained-view-constant-loop", "ReadOnlySpan<int> result=[];ReadOnlySpan<int> previous=[];for(int i=0;i<3;i++){previous=result;result=i%2==0?(ReadOnlySpan<int>)[2]:(ReadOnlySpan<int>)[3];}WriteLine(previous[0]);WriteLine(result[0]);", "3\n2\n", 16)]
    public Task CollectionExpressionStorage_AnonymousBuffersFollowFrameLifetime(string caseName, string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(caseName, source, output, 0, "", includeUnoptimized: true);

    [Theory]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){ReadOnlySpan<int> values=[7];return values;}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){ReadOnlySpan<int> values=[7];ReadOnlySpan<int> alias=values;return alias;}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F(true)[0]);static ReadOnlySpan<int> F(bool b){ReadOnlySpan<int> values=[7];ReadOnlySpan<int> other=[8];return b?values:other;}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){ReadOnlySpan<int> values=[7,8];return values[1..];}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F());static ref readonly int F(){ReadOnlySpan<int> values=[7];return ref values[0];}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){{ReadOnlySpan<int> values=[7];return values;}}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){ReadOnlySpan<int> values=[7];return (values);}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){return ([7]);}", "VIB007", 0)]
    [InlineData("WriteLine(F(true)[0]);static ReadOnlySpan<int> F(bool b){return b?[7]:[8];}", "VIB007", 16)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){using var r=new R();return [7];}public sealed class R:IDisposable{public void Dispose(){WriteLine(1);}}", "VIB007", 0)]
    [InlineData("WriteLine(F()[0]);static ReadOnlySpan<int> F(){ReadOnlySpan<int> values=[];return (values=[7]);}", "VIB003,VIB007", 0)]
    [InlineData("WriteLine(F(true)[0]);static ReadOnlySpan<int> F(bool b){ReadOnlySpan<int> values=b?[7]:[8];return values;}", "VIB007", 16)]
    public Task CollectionExpressionStorage_PreservesNamedOwnersAndReturnEscapes(string source, string expectedIds, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSubsetRejection(source, expectedIds.Split(','), expectedIds.Split(','));

    [Theory]
    [InlineData("dynamic-outer-assignment", "WriteLine(F(2));static int F(int x){ReadOnlySpan<int> result;{result=[x];}return result[0];}", "CS9203", "VIB007", 0)]
    [InlineData("dynamic-argument-escape", "WriteLine(F(2));static int F(int x){ReadOnlySpan<int> result;{result=Id([x]);}return result[0];}static ReadOnlySpan<int> Id(ReadOnlySpan<int> value)=>value;", "CS9203", "VIB007", 0)]
    [InlineData("dynamic-conditional-escape", "WriteLine(F(2,true));static int F(int x,bool b){ReadOnlySpan<int> result;{result=b?[x]:[x+1];}return result[0];}", "CS9203", "VIB007", 16)]
    [InlineData("dynamic-loop-escape", "WriteLine(F(2));static int F(int x){ReadOnlySpan<int> result=[];for(int i=0;i<2;i++){result=[x+i];}return result[0];}", "CS9203", "VIB007", 0)]
    [InlineData("dynamic-named-owner-escape", "WriteLine(F(2));static int F(int x){ReadOnlySpan<int> result;{ReadOnlySpan<int> inner=[x];result=inner;}return result[0];}", "CS8352", "VIB003,VIB007", 0)]
    public Task CollectionExpressionStorage_DynamicSourceSafeContextsRemainRestricted(string caseName, string source, string roslynId, string selfIds, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSourceRejection(caseName, source, roslynId, selfIds);

    private static async Task AssertSourceRejection(string caseName, string source, string roslynId, string selfId)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "vibranium-r10-source-invalid-" + Guid.NewGuid().ToString("N"));
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
            Assert.Equal(selfId.Split(','), DiagnosticIds(self.Out + self.Err));
            if (selfId == "VIB007") Assert.Contains("safe-context", self.Out + self.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData("WriteLine((int)F()[0]);static ReadOnlySpan<E> F(){return [E.A];}public enum E{A=7}", "VIB007")]
    [InlineData("WriteLine((int)F());static ref readonly E F(){ReadOnlySpan<E> values=[E.A];return ref values[0];}public enum E{A=7}", "VIB003,VIB007")]
    public Task LiveEscapingCollection_RetainsIndependentRuntimeRejection(string source, string expectedIds) =>
        AssertSubsetRejection(source, expectedIds.Split(','), expectedIds.Split(','));

    [Theory]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    public Task UnsupportedEnumWidths_RemainOutsideSubset(string underlyingType) =>
        AssertSubsetRejection("ReadOnlySpan<E> values=[E.A];WriteLine((int)values[0]);public enum E:" + underlyingType + "{A=7}", "VIB000");

    [Theory]
    [InlineData("option-return", "WriteLine(F(Some(4)));static int F(Option<int> o){switch(o){case Some<int>(var v):return v;case NoneType:return 0;}}", "CS0161", "VIB000", 0)]
    [InlineData("result-return", "WriteLine(F(Ok(4)));static int F(Result<int,int> r){switch(r){case Ok<int>(var v):return v;case Err<int>(var e):return e;}}", "CS0161", "VIB000", 0)]
    [InlineData("option-assignment", "WriteLine(F(Some(4)));static int F(Option<int> o){int x;switch(o){case Some<int>(var v):x=v;break;case NoneType:x=0;break;}return x;}", "CS0165", "VIB001", 0)]
    [InlineData("result-assignment", "WriteLine(F(Ok(4)));static int F(Result<int,int> r){int x;switch(r){case Ok<int>(var v):x=v;break;case Err<int>(var e):x=e;break;}return x;}", "CS0165", "VIB001", 0)]
    [InlineData("option-default-reset", "WriteLine(F(Some(4)));static int F(Option<int> o){int x;switch(o){case Some<int>(var v):x=v;break;case NoneType:x=0;break;default:break;}return x;}", "CS0165", "VIB001", 0)]
    [InlineData("option-var-reset", "WriteLine(F(Some(4)));static int F(Option<int> o){int x;switch(o){case Some<int>(var v):x=v;break;case NoneType:x=0;break;case var _:break;}return x;}", "CS0165", "VIB001", 0)]
    [InlineData("option-true-guards", "WriteLine(F(Some(4)));static int F(Option<int> o){switch(o){case Some<int>(var v) when true:return v;case NoneType when true:return 0;}}", "CS0161", "VIB000", 19)]
    [InlineData("option-nonconstant-catchall", "WriteLine(F(Some(4),true));static int F(Option<int> o,bool b){switch(o){case Some<int>(var v):return v;case var _ when b||true:return 0;}}", "CS0161", "VIB000", 0)]
    [InlineData("option-logical-variants", "WriteLine(F(Some(4)));static int F(Option<int> o){switch(o){case Some<int> or NoneType:return 0;}}", "CS0161", "VIB000", 0)]
    [InlineData("option-nested-section", "WriteLine(F(Some(4),1));static int F(Option<int> o,int k){switch(k){case 1:switch(o){case Some<int>(var v):return v;case NoneType:return 0;}case 2:return 2;default:return 0;}}", "CS0163", "VIB000", 0)]
    [InlineData("option-panic-tail", "WriteLine(F(Some(4)));static int F(Option<int> o){VibraniumRuntime.Panic(\"stop\");switch(o){case Some<int>(var v):return v;case NoneType:return 0;}}", "CS0161", "VIB000", 0)]
    public Task UnionStatement_RetainsSourceUnmatchedPath(string caseName, string source, string roslynId, string selfId, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSourceRejection(caseName, source, roslynId, selfId);

    [Theory]
    [InlineData("option-default", "WriteLine(F(Some(4)));WriteLine(F(None));static int F(Option<int> o){switch(o){case Some<int>(var v):return v;case NoneType:return 0;default:return -1;}}", "4\n0\n", 0)]
    [InlineData("result-default", "WriteLine(F(Ok(4)));WriteLine(F(Err(5)));static int F(Result<int,int> r){int x;switch(r){case Ok<int>(var v):x=v;break;case Err<int>(var e):x=e;break;default:x=-1;break;}return x;}", "4\n5\n", 0)]
    [InlineData("option-expression", "WriteLine(F(Some(4)));WriteLine(F(None));static int F(Option<int> o)=>o switch{Some<int>(var v)=>v,NoneType=>0};", "4\n0\n", 0)]
    [InlineData("option-catchall", "WriteLine(F(Some(4)));WriteLine(F(None));static int F(Option<int> o){switch(o){case Some<int>(var v):return v;case var _:return 0;}}", "4\n0\n", 0)]
    [InlineData("option-true-catchall", "WriteLine(F(Some(4)));WriteLine(F(None));static int F(Option<int> o){switch(o){case Some<int>(var v):return v;case var _ when true:return 0;}}", "4\n0\n", 19)]
    [InlineData("option-logical-complement", "WriteLine(F(Some(4)));WriteLine(F(None));static int F(Option<int> o){switch(o){case Some<int> or not Some<int>:return 1;}}", "1\n1\n", 0)]
    [InlineData("bool-source-exhaustive", "WriteLine(F(true));WriteLine(F(false));static int F(bool b){switch(b){case true:return 1;case false:return 0;}}", "1\n0\n", 0)]
    public Task UnionCatchallsAndExpressions_PreserveRuntimeExhaustiveness(string caseName, string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(caseName, source, output, 0, "");

    [Theory]
    [InlineData("ushort-impossible-expression", "WriteLine(F(3));static int F(ushort w)=>w switch{<0=>-1,_=>0};", "CS8510", 20)]
    [InlineData("ushort-impossible-statement", "WriteLine(F(3));static int F(ushort w){switch(w){case <0:return -1;default:return 0;}}", "CS8120", 20)]
    [InlineData("constant-selector-type-domain", "const ushort W=65535;WriteLine(F());static int F()=>W switch{<0=>-1,65535=>7,_=>0};", "CS8510", 19)]
    [InlineData("subsumed-statement", "WriteLine(F(3));static int F(int w){switch(w){case >1:return 1;case 5:return 5;default:return 0;}}", "CS8120", 20)]
    [InlineData("true-guard-subsumption", "WriteLine(F(3));static int F(int w){switch(w){case >1 when true:return 1;case 5:return 5;default:return 0;}}", "CS8120", 19)]
    [InlineData("ushort-upper-bound", "WriteLine(F(3));static int F(ushort w)=>w switch{>65535=>1,_=>0};", "CS8510", 20)]
    [InlineData("int-upper-bound", "WriteLine(F(3));static int F(int w)=>w switch{>2147483647=>1,_=>0};", "CS8510", 20)]
    [InlineData("sbyte-lower-bound", "WriteLine(F(3));static int F(sbyte w)=>w switch{< -128=>1,_=>0};", "CS8510", 20)]
    [InlineData("uint-lower-bound", "WriteLine(F(3));static int F(uint w)=>w switch{<0=>1,_=>0};", "CS8510", 20)]
    [InlineData("ulong-lower-bound", "WriteLine(F(3));static int F(ulong w)=>w switch{<0=>1,_=>0};", "CS8510", 20)]
    [InlineData("boolean-true-guard", "WriteLine(F(true));static int F(bool b)=>b switch{true when true=>1,true=>2,false=>3};", "CS8510", 19)]
    [InlineData("union-of-ranges", "WriteLine(F(3));static int F(int w)=>w switch{>=0=>1,<0=>2,0=>3};", "CS8510", 20)]
    [InlineData("intersection-subsumes-constant", "WriteLine(F(3));static int F(int w)=>w switch{>0 and <10=>1,5=>2,_=>0};", "CS8510", 20)]
    [InlineData("floating-subsumption", "WriteLine(F(3));static int F(double w)=>w switch{>0=>1,5=>2,_=>0};", "CS8510", 20)]
    [InlineData("enum-constant-subsumption", "WriteLine(F(E.A));static int F(E e)=>e switch{E.A=>1,E.A=>2,_=>0};public enum E{A=7}", "CS8510", 0)]
    [InlineData("nonnumeric-catchall", "WriteLine(F(new Holder()));static int F(Holder h)=>h switch{var _=>1,_=>2};public class Holder{}", "CS8510", 0)]
    public Task ImpossibleOrSubsumedSourcePatterns_AreRejected(string caseName, string source, string roslynId, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertSourceRejection(caseName, source, roslynId, "VIB000");

    [Theory]
    [InlineData("guarded-overlap", "WriteLine(F(5,false));WriteLine(F(5,true));static int F(ushort w,bool b){switch(w){case >1 when b:return 1;case 5:return 5;default:return 0;}}", "5\n1\n", 20)]
    [InlineData("nonconstant-true-guard", "WriteLine(F(5,false));static int F(ushort w,bool b){switch(w){case >1 when b||true:return 1;case 5:return 5;default:return 0;}}", "1\n", 20)]
    [InlineData("partial-overlap", "WriteLine(F(1));WriteLine(F(5));static int F(ushort w){switch(w){case >1:return 1;case >0:return 2;default:return 0;}}", "2\n1\n", 20)]
    [InlineData("constant-selector-not-domain", "const ushort W=0;WriteLine(W switch{1=>1,_=>0});", "0\n", 19)]
    [InlineData("boolean-guard-overlap", "WriteLine(F(true,false));WriteLine(F(true,true));static int F(bool b,bool c)=>b switch{true when c=>1,true=>2,false=>3};", "2\n1\n", 0)]
    [InlineData("false-guard-expression", "WriteLine(F(true));static int F(bool b)=>b switch{true when false=>1,true=>2,false=>3};", "2\n", 19)]
    [InlineData("overlapping-intersections", "WriteLine(F(7));WriteLine(F(12));static int F(int w)=>w switch{>0 and <10=>1,>=5 and <15=>2,_=>0};", "1\n2\n", 20)]
    [InlineData("floating-uncovered-zero", "WriteLine(F(0));static int F(double w)=>w switch{<0=>1,>0=>2,0=>3,_=>4};", "3\n", 20)]
    public Task ReachableSourcePatterns_PreserveGuardedAndPartialOverlap(string caseName, string source, string output, int version02Cut) =>
        version02Cut != 0 ? Round12CutSelfTests.AssertRejectedCut(source, version02Cut) :
        AssertDifferential(caseName, source, output, 0, "");

    [Theory]
    [InlineData("nested-derived-ref", "int value=1;ref readonly int result=ref value;{ReadOnlySpan<int> inner=[2];result=ref inner[0];}WriteLine(result);")]
    [InlineData("nested-derived-view", "ReadOnlySpan<int> result;{ReadOnlySpan<int> inner=[2];result=inner;}WriteLine(result[0]);")]
    [InlineData("loop-allocation-escape", "ReadOnlySpan<int> result=[];for(int i=0;i<1;i++){ReadOnlySpan<int> inner=[2];result=inner;}WriteLine(result[0]);")]
    public Task StackAllocation_ActualBackingScopeInvalidatesLiveDerivedBorrow(string caseName, string source)
    {
        _ = caseName;
        return AssertSubsetRejection(source, "VIB003");
    }

    [Theory]
    [InlineData("view-alias-is-not-owner", "ReadOnlySpan<int> outer=[1];ref readonly int r=ref outer[0];{ReadOnlySpan<int> copy=outer;WriteLine(copy[0]);}WriteLine(r);", "1\n1\n")]
    [InlineData("nested-allocation-value-return", "WriteLine(F());static int F(){{ReadOnlySpan<int> inner=[2];return inner[0];}}", "2\n")]
    [InlineData("loop-allocation-consumed", "for(int i=0;i<3;i++){ReadOnlySpan<int> inner=[i];WriteLine(inner[0]);}", "0\n1\n2\n")]
    public Task StackAllocation_ViewAliasAndConsumedScopeAreAccepted(string caseName, string source, string output) =>
        AssertDifferential(caseName, source, output, 0, "");

    private static string[] DiagnosticIds(string text) =>
        Regex.Matches(text, @"VIB\d{3}").Select(m => m.Value).Distinct().OrderBy(id => id).ToArray();

    private static Task AssertSubsetRejection(string source, string expectedId) =>
        AssertSubsetRejection(source, new[] { expectedId }, new[] { expectedId });

    private static async Task AssertSubsetRejection(string source, string[] expectedIds, string[] expectedSelfIds, string? expectedMessage = null)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "vibranium-r10-source-subset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SourceSubset", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(expectedIds, frontend.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(expectedIds, analyzer.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success);
            Assert.Equal(expectedIds, DiagnosticIds(string.Join("\n", stage0.Errors)));
            var self = Run(DotnetExe, SelfCompiler, "check", path, "--sysroot", Path.Combine(RepoRoot, "src"));
            Assert.NotEqual(0, self.Exit);
            Assert.Equal(expectedSelfIds, DiagnosticIds(self.Out + self.Err));
            if (expectedMessage is not null)
            {
                Assert.Contains(expectedMessage, string.Join("\n", frontend.Diagnostics.Select(d => d.Message)), StringComparison.Ordinal);
                Assert.Contains(expectedMessage, self.Out + self.Err, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task AssertDifferential(string caseName, string source, string output, int exitCode, string error, bool includeUnoptimized = false)
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
                foreach (string optimization in includeUnoptimized ? new[] { "-O0", "-O3" } : new[] { "-O3" })
                foreach (bool sanitize in new[] { false, true })
                {
                    string native = ir + optimization + (sanitize ? ".asan" : ".native");
                    var arguments = new List<string> { optimization, ir, "-o", native };
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
