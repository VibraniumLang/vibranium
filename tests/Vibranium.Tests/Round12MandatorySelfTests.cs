using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Frontend.Roslyn;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12MandatorySelfTests
{
    public static IEnumerable<object[]> RecursiveStructs()
    {
        yield return new object[] { "direct", "var value=new S();WriteLine(value.X);struct S{public S Inner;public int X;}" };
        yield return new object[] { "mutual", "var value=new A();WriteLine(value.X);struct A{public B Inner;public int X;}struct B{public A Inner;}" };
        yield return new object[] { "generic", "var value=new S<int>();WriteLine(value.X);struct S<T>{public S<T> Inner;public int X;}" };
        yield return new object[] { "option", "var value=new S();WriteLine(value.X);struct S{public Option<S> Inner;public int X;}" };
        yield return new object[] { "result", "var value=new S();WriteLine(value.X);struct S{public Result<S,int> Inner;public int X;}" };
        yield return new object[] { "unused", "WriteLine(1);struct S{public S Inner;public int X;}" };
    }

    [Theory]
    [MemberData(nameof(RecursiveStructs))]
    public void RecursiveStructLayoutIsRejectedPromptly(string name, string source)
    {
        AssertRejected(name, "CS0523", source);
    }

    [Theory]
    [InlineData("struct-class-indirection", "var value=new Link();WriteLine(value.Tail.IsEmpty);class Box{public Link Value;}struct Link{public Option<Box> Tail;}")]
    [InlineData("shared-leaf", "var value=new Pair();WriteLine(value.Left.Value+value.Right.Value);struct Leaf{public int Value;}struct Pair{public Leaf Left;public Leaf Right;}")]
    [InlineData("nested-generic", "var value=new Box<Box<int>>(new Box<int>(7));WriteLine(value.Value.Value);struct Box<T>{public T Value;public Box(T value){Value=value;}}")]
    public void FiniteStructLayoutsRemainAccepted(string name, string source)
    {
        AssertAccepted(name, source);
    }

    public static IEnumerable<object[]> MandatoryInvalidCSharp()
    {
        yield return new object[] { "unscoped-using-target-missing-suffix", "", "using UR=System.Diagnostics.CodeAnalysis.UnscopedRef;WriteLine(1);" };
        yield return new object[] { "unscoped-using-peer-out-of-scope", "", "using D=System.Diagnostics.CodeAnalysis;using UR=D.UnscopedRefAttribute;WriteLine(1);" };
        yield return new object[] { "methodimpl-unrelated-type-does-not-mask-bad-options", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1)]static int F()=>1;namespace Other{public struct MethodImplAttribute{}}" };
        yield return new object[] { "unscoped-closer-type-before", "", "WriteLine(1);namespace Outer{public class System{}public struct S{public int Value;[System.Diagnostics.CodeAnalysis.UnscopedRef]public ref int Get()=>ref Value;}}" };
        yield return new object[] { "unscoped-closer-namespace-before", "", "WriteLine(1);namespace Outer{namespace System{public struct Marker{}}public struct S{public int Value;[System.Diagnostics.CodeAnalysis.UnscopedRef]public ref int Get()=>ref Value;}}" };
        yield return new object[] { "unscoped-closer-nested-before", "", "using System.Diagnostics.CodeAnalysis;WriteLine(1);public struct S{public int Value;public struct UnscopedRefAttribute{}[UnscopedRef]public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-closer-type-after", "", "WriteLine(1);namespace Outer{public struct S{public int Value;[System.Diagnostics.CodeAnalysis.UnscopedRef]public ref int Get()=>ref Value;}public class System{}}" };
        yield return new object[] { "unscoped-closer-namespace-after", "", "WriteLine(1);namespace Outer{public struct S{public int Value;[System.Diagnostics.CodeAnalysis.UnscopedRef]public ref int Get()=>ref Value;}namespace System{public struct Marker{}}}" };
        yield return new object[] { "unscoped-closer-nested-after", "", "using System.Diagnostics.CodeAnalysis;WriteLine(1);public struct S{public int Value;[UnscopedRef]public ref int Get()=>ref Value;public struct UnscopedRefAttribute{}}" };
        yield return new object[] { "unscoped-child-namespace-is-not-imported", "CS0246", "using System.Diagnostics;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[CodeAnalysis.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "methodimpl-invalid-1", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-2", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(2)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-3", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(3)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-5", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(5)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-(MethodImplOptions)2", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((MethodImplOptions)2)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-(short)1", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((short)1)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-((1))", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(((1)))]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-1|4", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1|4)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-1+4", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1+4)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-unchecked((short)65537)", "CS0591", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(unchecked((short)65537))]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-alias-type", "CS0591", "using MI=System.Runtime.CompilerServices.MethodImplAttribute;WriteLine(F());[MI(1)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-alias-namespace-dot", "CS0591", "using CS=System.Runtime.CompilerServices;WriteLine(F());[CS.MethodImpl(1)]static int F()=>1;" };
        yield return new object[] { "methodimpl-invalid-alias-namespace-colon", "CS0591", "using CS=System.Runtime.CompilerServices;WriteLine(F());[CS::MethodImpl(1)]static int F()=>1;" };
        yield return new object[] { "unscoped-wrong-global-namespace", "CS0400", "return 0;struct S{public int Value;[global::SystemZz.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-missing-colon-alias", "CS0432", "return 0;struct S{public int Value;[missing::System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-qualified-missing-namespace", "", "return 0;struct S{public int Value;[System.DiagnosticsZz.CodeAnalysis.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-global-short-name", "", "return 0;struct S{public int Value;[global::UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-bare-without-import", "", "return 0;struct S{public int Value;[UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "char-main-valid-return", "CS0028", "static class Program{static char Main(){return 'a';}}" };
        yield return new object[] { "main-with-parameter", "CS0028", "static class Program{static int Main(int x)=>x;}" };
        yield return new object[] { "method-code-type-member", "CS0117", "using System.Runtime.CompilerServices;return 0;[MethodImpl(MethodImplOptions.NoInlining,MethodCodeType=MethodCodeType.Missing)]static void F(){}" };
        yield return new object[] { "campaign3-bare-value-statement", "", "int x=1; x;\n" };
        yield return new object[] { "campaign3-ctor-arrow-field", "", "return 0; class C { public int X=1; public C()=>X; }\n" };
        yield return new object[] { "campaign3-foreach-assignment", "", "Span<int> s=[]; foreach(var e in s) { e=e; }\n" };
        yield return new object[] { "campaign3-foreach-increment", "", "Span<int> s=[1]; foreach(var e in s) { e++; }\n" };
        yield return new object[] { "campaign3-foreach-out-argument", "", "Span<int> s=[1]; foreach(var e in s) { F(out e); } static void F(out int x) { x=2; }\n" };
        yield return new object[] { "campaign3-foreach-ref-alias", "", "Span<int> s=[1]; foreach(var e in s) { ref int r=ref e; r=2; }\n" };
        yield return new object[] { "campaign3-foreach-ref-argument", "", "Span<int> s=[1]; foreach(var e in s) { F(ref e); } static void F(ref int x) { x=2; }\n" };
        yield return new object[] { "campaign3-foreach-struct-field", "", "Span<P> s=[new P(1)]; foreach(var e in s) { e.X=2; } struct P { public int X; public P(int x){X=x;} }\n" };
        yield return new object[] { "campaign3-foreach-utf8-reassign", "", "foreach(var line in \"a\\nb\".Lines()) { line=\"x\".AsUtf8(); }\n" };
        yield return new object[] { "campaign3-using-reassignment", "", "using var d=new D(); d=new D(); class D:System.IDisposable { public void Dispose(){} }\n" };
        yield return new object[] { "campaign3-using-ref-alias", "", "using var d=new D(); ref D alias=ref d; class D:System.IDisposable { public void Dispose(){} }\n" };
        yield return new object[] { "campaign3-void-arrow-binary", "", "static void F()=>1+2;\n" };
        yield return new object[] { "campaign3-void-arrow-field", "", "return 0; class C { private int secret=4; public void Get()=>secret; }\n" };
        yield return new object[] { "campaign3-void-arrow-literal", "", "static void F()=>1;\n" };
        yield return new object[] { "helper-char-int-constant", "", "char c = 65; WriteLine((int)c); return 0;\n" };
        yield return new object[] { "helper-char-int-zero", "", "char c = 0; WriteLine((int)c); return 0;\n" };
        yield return new object[] { "helper-char-int-collection", "", "Vec<char> chars = [65]; WriteLine((int)chars[0]); return 0;\n" };
        yield return new object[] { "helper-char-int-return", "", "WriteLine((int)F()); return 0; static char F() => 65;\n" };
        yield return new object[] { "implicit-long-narrowing", "CS0266", "long big=5000000000;int x=big;WriteLine(x);" };
        yield return new object[] { "helper-constructor-value-return", "", "return 0; struct P { public int X; public P(int x) { X = x; return 1; } }\n" };
        yield return new object[] { "helper-continue-outside", "", "continue;\n" };
        yield return new object[] { "helper-foreach-ref-mismatch", "", "Vec<int> v = [1, 2]; Span<int> s = v.AsSpan(); foreach (ref long e in s) { WriteLine(e); } return 0;\n" };
        yield return new object[] { "helper-pattern-mismatch-expression", "", "Option<int> x = Some(1); WriteLine(x switch { Some<long>(var v) => v, _ => 0 }); return 0;\n" };
        yield return new object[] { "helper-pattern-mismatch-is", "", "Option<int> x = Some(1); if (x is Some<long>(var v)) WriteLine(v); return 0;\n" };
        yield return new object[] { "helper-ref-in-alias", "", "int x = 1; F(in x); return 0; static void F(in int x) { ref int r = ref x; r = 2; }\n" };
        yield return new object[] { "helper-ref-in-increment", "", "int x = 1; F(in x); return 0; static void F(in int x) { x++; }\n" };
        yield return new object[] { "helper-ref-local-mismatch", "", "long x = 1; ref int r = ref x; WriteLine(r); return 0;\n" };
        yield return new object[] { "helper-ref-local-uninit", "", "int a = 1;\nref int r;\nr = 2;\nWriteLine(a);\nreturn 0;\n" };
        yield return new object[] { "helper-ref-readonly-temp", "", "ref readonly int r = ref F(); WriteLine(r); return 0; static int F() => 1;\n" };
        yield return new object[] { "helper-ref-readonly-write", "", "int x = 1; ref readonly int r = ref x; r = 2; return 0;\n" };
        yield return new object[] { "helper-ref-return-value", "", "return 0; class H { public int N; [Mut] public ref int Slot() => N; }\n" };
        yield return new object[] { "helper-switch-continue-outside", "", "int n = 1; switch(n) { default: continue; } return 0;\n" };
        yield return new object[] { "helper-value-return-ref", "", "return 0; class H { public int N; public int Slot() => ref N; }\n" };
        yield return new object[] { "recursive-vec-element", "CS8377", "WriteLine(0);struct S{public Vec<S> Values;}" };
        yield return new object[] { "CS8331", "CS8331", "int a = 1;\nF(in a);\nWriteLine(a);\nreturn 0;\nstatic void F(in int x) { x = 2; }\n" };
        yield return new object[] { "CS1604", "CS1604", "return 0;\nclass C { [Mut] public void M() { this = new C(); } }\n" };
        yield return new object[] { "CS0191", "CS0191", "static int F(int p0)\n{\n    if (p0 > 100)\n    {\n        SO s;\n        s.A = 1;\n    }\n    return p0;\n}\npublic struct SO(int a, int b, Vec<int> n)\n{\n    readonly public int A = a;\n    public int B = b;\n    public Vec<int> V = n;\n}\n" };
        yield return new object[] { "CS8340", "CS8340", "return 0;\npublic readonly struct ArgBorrowSummary\n{\n    public int IsMut;\n}\n" };
        yield return new object[] { "CS0453", "CS0453", "var c = new C();\nvar d = Id(c);\nreturn 0;\nstatic T Id<T>(T x) where T : struct => x;\nclass C { }\n" };
        yield return new object[] { "CS0266", "CS0266", "char c = 65;\nWriteLine((int)c);\nreturn 0;\n" };
        yield return new object[] { "CS0122", "CS0122", "var c = new C();\nWriteLine(c.secret);\nreturn 0;\nclass C { private int secret = 4; }\n" };
        yield return new object[] { "CS0122-vec", "CS0122", "Vec<int> v = [1, 2, 3];\nv._count = 100000;\nWriteLine(v[50000]);\nreturn 0;\n" };
        yield return new object[] { "CS0139", "CS0139", "break;\n" };
        yield return new object[] { "CS0127", "CS0127", "F();\nreturn 0;\nstatic void F() { return 1; }\n" };
        yield return new object[] { "CS0246", "CS0246", "static int F()\n{\n    Vec<int> v = [1, 2, 3];\n    Span<int> s = v.AsSpan();\n    foreach (ref varZz e in s) \n    return v[0] + v.Count;\n}\n" };
        yield return new object[] { "CS0103", "CS0103", "static int F(Option<int> value){if(value.IsNone( _))return 2;}\n" };
        yield return new object[] { "CS8151", "CS8151", "return 0;\nclass H { public long N; [Mut] public ref int Slot() => ref N; }\n" };
        yield return new object[] { "CS8171", "CS8171", "var h = new H();\nlong r = ref h.Slot();\nclass H { public int N; [Mut] public ref int Slot() => ref N; }\n" };
        yield return new object[] { "CS8172", "CS8172", "var h = new H();\nref int r = h.Slot();\nclass H { public int N; [Mut] public ref int Slot() => ref N; }\n" };
        yield return new object[] { "CS8121", "CS8121", "static int F(Result<int, int> x)\n{\n    switch (x)\n    {\n        case Err<Vec<int>>(var e): \n        default: \n    }\n}\n" };
        yield return new object[] { "CS0305", "CS0305", "Option<int> option = Some(1);\nswitch (option)\n{\n    case Some<int, int>(var v): WriteLine(v); break;\n    default: break;\n}\nreturn 0;\n" };
        yield return new object[] { "CS0718", "CS0718", "static class Program\n{\n    static int Consume(Vec<int> xs) => xs[0];\n    static int UnwrapNested(Option<Option<Vec<int>>> outer) =>\n        Move(outer) switch\n        {\n            Some<Program>(Some<Vec<int>>(var inner)) => Consume(Move(inner)),\n            _ => 0,\n        };\n    public static void Main()\n    {\n    }\n}\n" };
        yield return new object[] { "CS8377", "CS8377", "static class Program\n{\n    static int Consume(Vec<int> xs) => xs[0];\n    static int UnwrapComplementaryResult(Result<Vec<int>, Vec<int>> res) =>\n        Move(res) switch\n        {\n            Ok<Vec<string>>(var ok) => Consume(Move(ok)),\n            Err<Vec<int>>(var err) => Consume(Move(err)) * 10,\n        };\n    public static void Main()\n    {\n    }\n}\n" };
        yield return new object[] { "in-increment", "CS8331", "int x=1;F(in x);static void F(in int value){value++;}" };
        yield return new object[] { "readonly-ref-write", "CS0131", "int x=1;ref readonly int r=ref x;r=2;" };
        yield return new object[] { "readonly-ref-alias", "CS1510", "int x=1;ref readonly int r=ref x;ref int w=ref r;w=2;" };
        yield return new object[] { "readonly-field-increment", "CS0191", "var s=new S(1);s.X++;struct S{public readonly int X;public S(int x){X=x;}}" };
        yield return new object[] { "continue-outside-loop", "CS0139", "continue;" };
        yield return new object[] { "continue-switch", "CS0139", "int x=1;switch(x){case 1:continue;default:break;}" };
        yield return new object[] { "ref-type-mismatch", "CS8173", "long x=1;ref int r=ref x;WriteLine(r);" };
        yield return new object[] { "value-return-ref", "CS8149", "WriteLine(F());static int F(){int x=1;return ref x;}" };
        yield return new object[] { "ref-return-value", "CS8150", "return 0;class C{public int X;[Mut]public ref int F(){return X;}}" };
        yield return new object[] { "unknown-identifier", "CS0103", "WriteLine(unknownName);" };
        yield return new object[] { "unmanaged-string-method", "CS8377", "WriteLine(Id(\"hello\"));static T Id<T>(T value) where T:unmanaged=>value;" };
        yield return new object[] { "unmanaged-managed-struct", "CS8377", "var s=new S(\"hello\");var t=Id(s);WriteLine(t.Text);static T Id<T>(T value) where T:unmanaged=>value;struct S{public string Text;public S(string text){Text=text;}}" };
        yield return new object[] { "unmanaged-string-vec", "CS8377", "Vec<string> values=new Vec<string>();WriteLine(values.Count);" };
        yield return new object[] { "struct-type-constraint", "CS0453", "var value=new Box<C>();return 0;struct Box<T> where T:struct{public int X;}class C{}" };
        yield return new object[] { "class-type-constraint", "CS0452", "var value=new Box<int>();return 0;struct Box<T> where T:class{public int X;}" };
        yield return new object[] { "class-method-constraint", "CS0452", "WriteLine(Id(1));static T Id<T>(T x) where T:class=>x;" };
    }

    [Theory]
    [MemberData(nameof(MandatoryInvalidCSharp))]
    public void InvalidCSharpNeverSelfAccepts(string name, string roslynId, string source)
    {
        AssertRejected(name, roslynId, source);
    }

    public static IEnumerable<object[]> MandatoryAcceptedControls()
    {
        yield return new object[] { "unscoped-outer-namespace-chain", "var s=new Outer.Inner.S();\ns.Get()=7;\nWriteLine(s.Value);\nnamespace Outer\n{\n    using D=System.Diagnostics;\n    namespace Inner\n    {\n        using CA=D.CodeAnalysis;\n        public struct S\n        {\n            public int Value;\n            [CA.UnscopedRef]\n            public ref int Get()=>ref Value;\n        }\n    }\n}\n" };
        yield return new object[] { "unscoped-outer-type-chain", "var s=new Outer.Inner.S();\nWriteLine(s.Get());\nnamespace Outer\n{\n    using D=System.Diagnostics.CodeAnalysis;\n    namespace Inner\n    {\n        using UR=D.UnscopedRefAttribute;\n        public struct S\n        {\n            [UR]\n            public int Get()=>7;\n        }\n    }\n}\n" };
        yield return new object[] { "unscoped-imported-framework-type", "using System.Diagnostics.CodeAnalysis;var s=new Outer.S();s.Get()=7;WriteLine(s.Value);namespace Outer{using UR=UnscopedRefAttribute;public struct S{public int Value;[UR]public ref int Get()=>ref Value;}}" };
        yield return new object[] { "unscoped-outer-type-alias-chain", "using BaseAttr=global::System.Diagnostics.CodeAnalysis.UnscopedRefAttribute;var s=new Outer.S();s.Get()=7;WriteLine(s.Value);namespace Outer{using UR=BaseAttr;public struct S{public int Value;[UR]public ref int Get()=>ref Value;}}" };
        yield return new object[] { "unscoped-outer-global-local-precedence", "global using UR=Local.Other;var s=new Outer.S();s.Get()=7;WriteLine(s.Value);namespace Outer{using UR=global::System.Diagnostics.CodeAnalysis.UnscopedRefAttribute;public struct S{public int Value;[UR]public ref int Get()=>ref Value;}}namespace Local{public class Other{}}" };
        yield return new object[] { "shadowing-user-type-alias", "using UR=Local.UnscopedRefAttribute;var box=new UR();WriteLine(box.X);namespace Local{public class UnscopedRefAttribute{public int X=5;}}" };
        yield return new object[] { "unscoped-unrelated-generic-nested", "using System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[UnscopedRef]public ref int Get()=>ref Value;public struct UnscopedRefAttribute<T>{}}" };
        yield return new object[] { "unscoped-relative-type-alias", "var s=new System.Diagnostics.CodeAnalysis.Nested.S();s.Get()=7;WriteLine(s.Value);namespace System.Diagnostics.CodeAnalysis{using UR=UnscopedRefAttribute;namespace Nested{public struct S{public int Value;[UR]public ref int Get()=>ref Value;}}}" };
        yield return new object[] { "unscoped-type-alias", "using UR=System.Diagnostics.CodeAnalysis.UnscopedRefAttribute;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[UR] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-relative-namespace-alias", "var s=new System.Diagnostics.Nested.S();s.Get()=7;WriteLine(s.Value);namespace System.Diagnostics{using CA=CodeAnalysis;namespace Nested{public struct S{public int Value;[CA.UnscopedRef]public ref int Get()=>ref Value;}}}" };
        yield return new object[] { "unscoped-enclosing-namespace", "var s=new System.Diagnostics.CodeAnalysis.Nested.S();s.Get()=7;WriteLine(s.Value);namespace System.Diagnostics.CodeAnalysis.Nested{public struct S{public int Value;[UnscopedRef]public ref int Get()=>ref Value;}}" };
        yield return new object[] { "unscoped-unrelated-namespace", "using System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[UnscopedRef]public ref int Get()=>ref Value;}namespace Other{struct UnscopedRefAttribute{}}" };
        yield return new object[] { "unscoped-unrelated-nested", "using System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[UnscopedRef]public ref int Get()=>ref Value;}class Other{public struct UnscopedRefAttribute{}}" };
        yield return new object[] { "unscoped-global", "var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[global::System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-global-suffix", "var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[global::System.Diagnostics.CodeAnalysis.UnscopedRefAttribute] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-qualified", "var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-import", "using System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-import-suffix", "using System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[UnscopedRefAttribute] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-namespace-alias-dot", "using CA=System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[CA.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "unscoped-global-alias", "global using CA=global::System.Diagnostics.CodeAnalysis;var s=new S();s.Get()=7;WriteLine(s.Value);struct S{public int Value;[CA.UnscopedRef] public ref int Get()=>ref Value;}" };
        yield return new object[] { "readonly-field-in-control", "var p=new Pair(new Box(1));Read(in p.Value);static void Read(in Box x){WriteLine(x.N);}struct Pair{public readonly Box Value;public Pair(Box value){Value=value;}}class Box{public int N;public Box(int n){N=n;}}" };
        yield return new object[] { "writable-field-out-control", "var p=new Pair(new Box(1));Set(out p.Value);WriteLine(p.Value.N);static void Set(out Box x){x=new Box(9);}struct Pair{public Box Value;public Pair(Box value){Value=value;}}class Box{public int N;public Box(int n){N=n;}}" };
        yield return new object[] { "single-ref-arg", "int x=1;F(ref x);WriteLine(x);static void F(ref int value){value+=1;}" };
        yield return new object[] { "struct-explicit-ctor-initializer", "var s=new S(7);WriteLine(s.X);struct S{public int X=4;public S(int x){X=x;}}" };
        yield return new object[] { "int-main", "static class Program{static int Main(){return 0;}}" };
        yield return new object[] { "void-main", "static class Program{static void Main(){WriteLine(1);}}" };
        yield return new object[] { "ignored-wrong-main-overload", "static class Program{static char Main(int x)=>(char)x;static void Main(){WriteLine(1);}}" };
        yield return new object[] { "readonly-field-ref-read", "return 0;class Box{public readonly int Value=7;public ref readonly int Get()=>ref Value;}" };
        yield return new object[] { "zero-safe-private-field", "return 0;class C{private int secret;public int Get()=>secret;}" };
        yield return new object[] { "regular-overload-no-default", "WriteLine(F());static int F()=>G(1);static int G(int x)=>x;" };
        yield return new object[] { "nonconstant-condition", "int x=1;if(x>0)WriteLine(x);" };
        yield return new object[] { "boolean-unary", "bool value=false;WriteLine(!value);" };
        yield return new object[] { "unchecked-explicit-conversion", "int value=unchecked((int)2147483648L);WriteLine(value);" };
        yield return new object[] { "campaign3-ctor-arrow-assignment", "var p=new P(2);WriteLine(p.X);return 0; struct P{public int X;public P(int x)=>X=x;}\n" };
        yield return new object[] { "campaign3-ctor-arrow-call", "var c=new C();return 0;class C{public C()=>WriteLine(1);}\n" };
        yield return new object[] { "campaign3-foreach-read", "Span<int> s=[1,2]; int sum=0; foreach(var e in s){sum+=e;} WriteLine(sum); return 0;\n" };
        yield return new object[] { "campaign3-foreach-ref-readonly", "Span<int> s=[1,2]; int sum=0; foreach(ref readonly var e in s){sum+=e;} WriteLine(sum); return 0;\n" };
        yield return new object[] { "campaign3-foreach-ref-write", "Span<int> s=[1,2]; foreach(ref var e in s){e+=1;} WriteLine(s[0]+s[1]); return 0;\n" };
        yield return new object[] { "campaign3-foreach-utf8-read", "foreach(var line in \"a\\nb\".Lines()){WriteLine(line);} return 0;\n" };
        yield return new object[] { "campaign3-using-identity", "using var d=new D(); d.X=2; d.Touch(); return 0; class D:System.IDisposable { public int X=1; public void Touch()=>WriteLine(X); public void Dispose()=>WriteLine(X); }\n" };
        yield return new object[] { "campaign3-using-statement", "using(var d=new D()){d.X=2;WriteLine(d.X);} return 0; class D:System.IDisposable { public int X=1; public void Dispose()=>WriteLine(X); }\n" };
        yield return new object[] { "campaign3-void-arrow-assign", "int x=1;F(ref x);WriteLine(x);return 0; static void F(ref int x)=>x=2;\n" };
        yield return new object[] { "campaign3-void-arrow-call", "F(); return 0; static void F()=>WriteLine(1);\n" };
        yield return new object[] { "campaign3-void-arrow-incdec", "int x=1;F(ref x);G(ref x);WriteLine(x);return 0; static void F(ref int x)=>x++;static void G(ref int x)=>--x;\n" };
        yield return new object[] { "campaign3-void-arrow-new", "F(); return 0; static void F()=>new C(); class C{public int X=1;}\n" };
        yield return new object[] { "campaign3-void-arrow-value-call", "F(); return 0; static void F()=>G(); static int G(){WriteLine(1);return 2;}\n" };
        yield return new object[] { "helper-char-explicit-cast", "char c = (char)65; WriteLine((int)c); return 0;\n" };
        yield return new object[] { "helper-char-literal-widen", "char c = 'A'; int i = c; ushort u = c; WriteLine(i + u); return 0;\n" };
        yield return new object[] { "helper-char-collection", "Vec<char> chars = ['A', (char)66]; WriteLine((int)chars[1]); return 0;\n" };
        yield return new object[] { "helper-ushort-int-constant", "ushort x = 65; WriteLine(x); return 0;\n" };
        yield return new object[] { "helper-ref-multiple-declarators", "int a=1; int b=2; ref int x = ref a, y = ref b; x=3; y=4; WriteLine(a+b); return 0;\n" };
        yield return new object[] { "helper-unused-top-local", "static int F(int value) => value + 1;\n" };
        yield return new object[] { "helper-unused-top-locals-sibling", "static int F(int value) => G(value); static int G(int value) => value + 1;\n" };
        yield return new object[] { "unused-top-level-local", "static int F()=>1;" };
        yield return new object[] { "explicit-char-cast", "char c=(char)65;WriteLine((int)c);" };
        yield return new object[] { "char-literal", "char c='A';WriteLine((int)c);" };
        yield return new object[] { "readonly-primary-capture", "var s=new S(7);WriteLine(s.Get());readonly struct S(int value){public int Get()=>value;}" };
        yield return new object[] { "helper-ctor-expression-body", "var p = new P(2); WriteLine(p.X); return 0; struct P { public int X; public P(int x) => X = x; }\n" };
        yield return new object[] { "helper-foreach-numeric", "Vec<int> v = [257, 2]; int sum = 0; foreach (byte e in v) { sum += e; } WriteLine(sum); return 0;\n" };
        yield return new object[] { "helper-foreach-ref", "Vec<int> v = [1, 2, 3]; Span<int> s = v.AsSpan(); foreach (ref int e in s) { e += 1; } WriteLine(v[0]); return 0;\n" };
        yield return new object[] { "helper-foreach-widen", "Vec<int> v = [1, 2]; long sum = 0; foreach (long e in v) { sum += e; } WriteLine(sum); return 0;\n" };
        yield return new object[] { "helper-guard-out-discard", "Option<int> x = Some(1); if (x.IsNone(out _)) return 0; return 0;\n" };
        yield return new object[] { "helper-guard-out", "Option<int> x = Some(1); if (x.IsNone(out var value)) return 0; WriteLine(value); return 0;\n" };
        yield return new object[] { "helper-in-read", "int a = 1; WriteLine(F(in a)); return 0; static int F(in int x) => x + 1;\n" };
        yield return new object[] { "helper-loop-switch-continue", "int n = 0; for (int i = 0; i < 3; i++) { switch (i) { case 1: continue; default: break; } n++; } WriteLine(n); return 0;\n" };
        yield return new object[] { "helper-option-pattern", "Option<int> x = Some(1); switch (x) { case Some<int>(var v): WriteLine(v); break; default: break; } return 0;\n" };
        yield return new object[] { "helper-private-own", "var p = new P(); WriteLine(p.Read()); return 0; class P { private int value = 3; public int Read() => this.value; }\n" };
        yield return new object[] { "helper-public-field", "var p = new P(); p.Value = 2; WriteLine(p.Value); return 0; class P { public int Value = 1; }\n" };
        yield return new object[] { "helper-readonly-ctor-read", "P p = new P(2); WriteLine(p.Read()); return 0; readonly struct P { public readonly int X; public P(int x) { X = x; } public int Read() => X; }\n" };
        yield return new object[] { "helper-readonly-field-ctor", "P p = new P(2); WriteLine(p.X); return 0; struct P { public readonly int X; public P(int x) { this.X = x; } }\n" };
        yield return new object[] { "helper-ref-local", "int x = 1; ref int r = ref x; r = 2; WriteLine(x); return 0;\n" };
        yield return new object[] { "helper-ref-readonly-local", "int x = 1; ref readonly int r = ref x; WriteLine(r); return 0;\n" };
        yield return new object[] { "helper-ref-return-copy", "var h = new H(); long r = h.Slot(); WriteLine(r); return 0; class H { public int N; [Mut] public ref int Slot() => ref N; }\n" };
        yield return new object[] { "helper-ref-return", "var h = new H(); ref int r = ref h.Slot(); r = 2; WriteLine(h.N); return 0; class H { public int N; [Mut] public ref int Slot() => ref N; }\n" };
        yield return new object[] { "helper-union-pattern", "Result<int, int> x = Err(42); switch (x) { case Err<int>(var e): WriteLine(e); break; default: WriteLine(0); break; } return 0;\n" };
        yield return new object[] { "helper-value-widen", "int x = 2; long big = x; WriteLine(big); return 0;\n" };
        yield return new object[] { "helper-void-expression-body", "F(); return 0; static void F() => WriteLine(1);\n" };
        yield return new object[] { "helper-void-return", "F(); return 0; static void F() { WriteLine(1); return; }\n" };
        yield return new object[] { "in-read", "int x=1;WriteLine(F(in x));static int F(in int value)=>value;" };
        yield return new object[] { "mutable-this-field", "var c=new C();c.Set();WriteLine(c.Value);class C{public int Value;[Mut]public void Set(){this.Value=1;}}" };
        yield return new object[] { "readonly-field-constructor", "var s=new S(7);WriteLine(s.X);struct S{public readonly int X;public S(int x){X=x;}}" };
        yield return new object[] { "readonly-struct", "var s=new S(7);WriteLine(s.X);readonly struct S{public readonly int X;public S(int x){X=x;}}" };
        yield return new object[] { "struct-method-constraint", "WriteLine(Id(7));static T Id<T>(T x) where T:struct=>x;" };
        yield return new object[] { "struct-managed-payload", "var s=Id(new S(\"ok\"));WriteLine(s.Text);static T Id<T>(T x) where T:struct=>x;struct S{public string Text;public S(string text){Text=text;}}" };
        yield return new object[] { "explicit-narrowing", "long value=7;int x=(int)value;WriteLine(x);" };
        yield return new object[] { "private-in-owner", "var c=new C();WriteLine(c.Read());class C{private int secret=4;public int Read()=>secret;}" };
        yield return new object[] { "vec-public-count", "Vec<int> values=[1,2,3];WriteLine(values.Count);" };
        yield return new object[] { "break-in-loop", "while(true){WriteLine(1);break;}" };
        yield return new object[] { "continue-in-loop", "for(int i=0;i<2;i++){if(i==0)continue;WriteLine(i);}" };
        yield return new object[] { "break-in-switch", "int x=1;switch(x){case 1:WriteLine(x);break;default:break;}" };
        yield return new object[] { "void-return", "F();static void F(){return;}" };
        yield return new object[] { "foreach-explicit-type", "Vec<int> values=[1,2,3];foreach(ref int item in values.AsSpan()){item+=1;}WriteLine(values[0]);" };
        yield return new object[] { "out-discard", "Option<int> value=Some(7);if(value.IsNone(out _))WriteLine(0);else WriteLine(1);" };
        yield return new object[] { "ref-return-write", "var h=new H();ref int r=ref h.Slot();r=4;WriteLine(r);class H{public int N;[Mut]public ref int Slot()=>ref N;}" };
        yield return new object[] { "ref-return-value-copy", "var h=new H();int r=h.Slot();WriteLine(r);class H{public int N;[Mut]public ref int Slot()=>ref N;}" };
        yield return new object[] { "readonly-ref-read", "int x=7;ref readonly int r=ref x;WriteLine(r);" };
        yield return new object[] { "matching-err-pattern", "Result<int,int> value=Err(7);switch(value){case Err<int>(var e):WriteLine(e);break;default:break;}" };
        yield return new object[] { "matching-some-pattern", "Option<int> value=Some(7);switch(value){case Some<int>(var x):WriteLine(x);break;default:break;}" };
        yield return new object[] { "unmanaged-primitive", "WriteLine(Id(7));static T Id<T>(T x) where T:unmanaged=>x;" };
        yield return new object[] { "unmanaged-struct", "var s=Id(new S(7));WriteLine(s.X);static T Id<T>(T x) where T:unmanaged=>x;struct S{public int X;public S(int x){X=x;}}" };
        yield return new object[] { "unmanaged-forwarding", "WriteLine(Outer(7));static T Outer<T>(T x) where T:unmanaged=>Inner(x);static T Inner<T>(T x) where T:unmanaged=>x;" };
        yield return new object[] { "struct-forwarding", "WriteLine(Outer(7));static T Outer<T>(T x) where T:struct=>Inner(x);static T Inner<T>(T x) where T:struct=>x;" };
        yield return new object[] { "class-forwarding", "var c=Outer(new C());WriteLine(c.Value);static T Outer<T>(T x) where T:class=>Inner(x);static T Inner<T>(T x) where T:class=>x;class C{public int Value=7;}" };
        yield return new object[] { "struct-type-constraint-control", "var value=new Box<int>();WriteLine(value.X);struct Box<T> where T:struct{public int X;}" };
        yield return new object[] { "class-type-constraint-control", "var value=new Box<C>();WriteLine(value.X);struct Box<T> where T:class{public int X;}class C{}" };
    }

    public static IEnumerable<object[]> F15FormerMethodImplAccepts()
    {
        yield return new object[] { "methodimpl-valid-alias-type", "using MI=System.Runtime.CompilerServices.MethodImplAttribute;WriteLine(F());[MI(8)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-(short)0", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((short)0)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-(MethodImplOptions)0", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((MethodImplOptions)0)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl()]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-4", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(4)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-8", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(8)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-1024", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1024)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-16384", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(16384)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-1+3", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1+3)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-(short)(1+3)", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((short)(1+3))]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-unchecked((short)65536)", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(unchecked((short)65536))]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-(MethodImplOptions)32768", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((MethodImplOptions)32768)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid--8196", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(-8196)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid--32768", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(-32768)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-4|8", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(4|8)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-1*8", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(1*8)]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-(MethodImplOptions)(-2147483648 >> 32)", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl((MethodImplOptions)(-2147483648 >> 32))]static int F()=>1;" };
        yield return new object[] { "methodimpl-valid-alias-namespace-dot", "using CS=System.Runtime.CompilerServices;WriteLine(F());[CS.MethodImpl(8)]static int F()=>1;" };
        yield return new object[] { "attribute-source-shadow", "using System.Runtime.CompilerServices;WriteLine(F());[MethodImpl(MethodImplOptions.Custom)]static int F()=>7;static class MethodImplOptions{public const short Custom=8;}" };
        yield return new object[] { "methodimpl-member", "using System.Runtime.CompilerServices;WriteLine(F(1));[MethodImpl(MethodImplOptions.NoInlining)]static int F(int x)=>x+1;" };
        yield return new object[] { "methodimpl-flags", "using System.Runtime.CompilerServices;WriteLine(F(1));[MethodImpl(MethodImplOptions.NoInlining|MethodImplOptions.NoOptimization)]static int F(int x)=>x+1;" };
        yield return new object[] { "methodimpl-cast", "using System.Runtime.CompilerServices;WriteLine(F(1));[MethodImpl((MethodImplOptions)8)]static int F(int x)=>x+1;" };
    }

    [Theory]
    [MemberData(nameof(F15FormerMethodImplAccepts))]
    public void CSharpValidMethodImplApplicationsAreOutsideTheF15List(string name, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MandatoryF15", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Equal(new[] { "VIB000" }, Regex.Matches(self.Error + self.Output, @"error (VIB\d{3}):")
                .Select(match => match.Groups[1].Value).Distinct());
            Assert.Contains("Attribute is not permitted in Vibranium", self.Error, StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(MandatoryAcceptedControls))]
    public void NearbyValidProgramsRemainAccepted(string name, string source)
    {
        AssertAccepted(name, source);
    }

    [Theory]
    [InlineData("Vec<int> values=[257,2];int sum=0;foreach(byte item in values){sum+=item;}WriteLine(sum);", "3\n")]
    [InlineData("Vec<int> values=[1,2];long sum=0;foreach(long item in values){sum+=item;}WriteLine(sum);", "3\n")]
    [InlineData("var h=new H();ref int r=ref h.Slot();r=4;WriteLine(r);class H{public int N=0;[Mut]public ref int Slot()=>ref N;}", "4\n")]
    [InlineData("var p=new P(2);WriteLine(p.X);F();static void F()=>WriteLine(1);struct P{public int X;public P(int x)=>X=x;}", "2\n1\n")]
    [InlineData("WriteLine(Outer(7));static T Outer<T>(T x) where T:unmanaged=>Inner(x);static T Inner<T>(T x) where T:unmanaged=>x;", "7\n")]
    [InlineData("using var d=new D(); d.X=2; d.Touch(); return 0; class D:System.IDisposable { public int X=1; public void Touch()=>WriteLine(X); public void Dispose()=>WriteLine(X); }\n", "2\n2\n")]
    [InlineData("Span<int> s=[1,2]; foreach(ref var e in s){e+=1;} WriteLine(s[0]+s[1]); return 0;\n", "5\n")]
    [InlineData("Vec<int>v=[2,2,3];var s=v.AsReadOnlySpan();int sum=0;for(int i=0;i<2;i++){sum+=s[0];}sum+=Sink(v);WriteLine(sum);static int Sink(Vec<int>x)=>x.Count;", "7\n")]
    [InlineData("StringBuf buf=new StringBuf();buf.Append(\"a\\nb\");foreach(var line in buf.Lines()){WriteLine(line);}Sink(buf);static void Sink(StringBuf value){WriteLine(value.Length);}", "a\nb\n3\n")]
    public Task AcceptedControlExecutionStaysEquivalent(string source, string expected) =>
        Round11AuditAssertions.AssertDifferential(source, expected, includeUnoptimized: false);

    public static IEnumerable<object[]> FrozenMandatoryRepros()
    {
        yield return new object[] { "CS0023", "return 0;\npublic readonly struct MirOperand\n{\n    public readonly int Kind;\n    \n    \n    \n    \n    \n\n    public MirOperand(int kind, int placeOrLitIdx, long intVal, int typeId, int tokIdx, int sourceStartTokIdx = !1)\n    {\n        Kind = kind;\n        \n        \n        \n        \n        \n    }\n}\n\n", "Optional parameters" };
        yield return new object[] { "CS0028", "namespace App;\nstatic class Program { static char Main() {  return 0; } }\n", "" };
        yield return new object[] { "CS0029", "\nstatic int F(int p0, bool p1)\n{\n    \n    ushort x4;\n    char s5 = 0 switch { < 2 when true => 0, >= 0 => x4 < (ushort)0 ? 0 : 0 };\n    \n}\n", "Constant conditions" };
        yield return new object[] { "CS0103", "static int F(Option<int> value){if(value.IsNone(ref _))return 2;}", "" };
        yield return new object[] { "CS0117", "using System.Runtime.CompilerServices;\n\n\n[MethodImpl(MethodImplOptions.NoInliningZz)]\nstatic int F(int a) => a + 1;\n", "" };
        yield return new object[] { "CS0122", "Holder h;h.Value=Ok(1);public struct Holder{ Result<int,int> Value;}", "" };
        yield return new object[] { "CS0127", "namespace App;\nstatic class Program { static void Main() {  return 0; } }\n", "" };
        yield return new object[] { "CS0139", "\nbreak; \n\n", "" };
        yield return new object[] { "CS0201", "\n\nreturn 0;\nclass C { private int secret = 4; public void Get() => secret; }\n", "" };
        yield return new object[] { "CS0220", "return 0;\nstatic class CorpusSlice\n{\npublic static Result<Vec<byte>, IoError> ReadAllBytes(global::System.ReadOnlySpan<byte> path, int maxBytes = int.MaxValue + 1) =>\n        File.ReadAllBytes(path, maxBytes);\n\n}\n", "Optional parameters" };
        yield return new object[] { "CS0221", "\nstatic int F(int p0, bool p1, long p2)\n{\n    \n    int s2 = 0 switch { < 0 => 0, >= 0 when (ushort)int.MaxValue + 1 != (ushort)0 => 0 };\n    \n}\n", "Constant conditions" };
        yield return new object[] { "CS0246", "WriteLine(0);\npublic static class C\n{\n    public static int M(int p0, bool p1, long p2)\n    {\n        int acc = 0;\n        Span<int> s1 = [2, 14, -5, 18, 14, 7];\n        foreach (ref varZz t2_e1 in s1) t2_e1 += 3;\n        \n        return acc;\n    }\n}\n", "" };
        yield return new object[] { "CS0266", "char b = 300;\n\n\n", "" };
        yield return new object[] { "CS0305", "static int F(Result<int,int>x){switch(x){case Ok<int>(var v):case Err<int, int>(var e):default:}}", "" };
        yield return new object[] { "CS0452", "\nWriteLine(Id(5L));\n\nstatic T Id<T>(T x) where T : class => x;\n", "" };
        yield return new object[] { "CS0453", "\nWriteLine(Id(\"text\"));\n\nstatic T Id<T>(T x) where T : struct => x;\n", "" };
        yield return new object[] { "CS1525", "int a=12;F(ref ref a);static void F(ref int a){}", "" };
        yield return new object[] { "CS1604", "\n\nreturn 0;\nclass C {  [Mut] public void M() { this = new C(); } }\n", "Assigning to this" };
        yield return new object[] { "CS1656", "Span<int> s=[];foreach(var e in s){ e = e; }", "" };
        yield return new object[] { "CS1741", "static bool Set(out int a = default){return true;}", "Optional parameters" };
        yield return new object[] { "CS1750", "return 0;\nstatic class CorpusSlice\n{\npublic static Result<Vec<byte>, IoError> ReadAllBytes(string path, int maxBytes = 1L) =>\n        File.ReadAllBytes(path.AsUtf8(), maxBytes);\n\n}\n", "Optional parameters" };
        yield return new object[] { "CS8121", "static int F(Option<int> o){switch(o){case Some<string>(var v):case var _:}}", "" };
        yield return new object[] { "CS8151", "static ref int F(){var b=new Box(7);return ref b.Value;}public class Box{public long Value;public Box(int x){}}", "" };
        yield return new object[] { "CS8160", "static ref int F(){var b=new Box(7);return ref b.Value;}public class Box{readonly public int Value;public Box(int x){}}", "" };
        yield return new object[] { "CS8331", "\n\n\n\nstatic void F(in int x) { x = 2; }\n", "" };
        yield return new object[] { "CS8340", "Holder h;readonly public struct Holder{public Result<int,int> Value;}", "" };
        yield return new object[] { "CS8377", "return 0;\npublic struct IntMap<V> where V : struct\n{\n    private Vec<long> _keys;\n    private Vec<V> _vals;\n    private Vec<byte> _state;\n    private Vec<int> _index;\n    private int _live;\n\n    \n\n    \n    public readonly int EntryCount => _keys.Count;\n    public readonly bool IsLive(int entry) => _state[entry] != 0;\n    public readonly long KeyAt(int entry) => _keys[entry];\n    public readonly V ValueAt(int entry) => _vals[entry];\n\n    private static int Slot(long key, int mask)\n    {\n        long z = key;\n        z = (z ^ (z >>> 30)) * -0x40A7B892E31B1A47L;\n        z = (z ^ (z >>> 27)) * -0x6B2FB644ECCEEE15L;\n        z = z ^ (z >>> 31);\n        return (int)z & mask;\n    }\n\n    public readonly int Find(long key)\n    {\n        int capacity = _index.Count;\n        if (capacity == 0) return -1;\n        int mask = capacity - 1;\n        int slot = Slot(key, mask);\n        while (true)\n        {\n            int e = _index[slot];\n            if (e == 0) return -1;\n            if (_state[e - 1] != 0 && _keys[e - 1] == key) return e - 1;\n            slot = (slot + 1) & mask;\n        }\n    }\n\n    public readonly bool Contains(long key) => Find(key) >= 0;\n\n    public readonly Option<V> Get(long key)\n    {\n        int e = Find(key);\n        if (e < 0) return Prelude.None;\n        return Prelude.Some(_vals[e]);\n    }\n\n    public Option<V> Set(long key, V value)\n    {\n        int e = Find(key);\n        if (e >= 0)\n        {\n            V previous = _vals[e];\n            _vals[e] = value;\n            return Prelude.Some(previous);\n        }\n        if ((_keys.Count + 1) * 4L > _index.Count * 3L) Rehash();\n        _keys.Push(key);\n        _vals.Push(value);\n        _state.Push(1);\n        Link(_keys.Count - 1);\n        _live += 1;\n        return Prelude.None;\n    }\n\n    public Option<V> Remove(long key)\n    {\n        int e = Find(key);\n        if (e < 0) return Prelude.None;\n        _state[e] = 0;\n        _live -= 1;\n        return Prelude.Some(_vals[e]);\n    }\n\n    public void Clear()\n    {\n        _keys.Clear();\n        _vals.Clear();\n        _state.Clear();\n        _index.Clear();\n        _live = 0;\n    }\n\n    private void Link(int entry)\n    {\n        int mask = _index.Count - 1;\n        int slot = Slot(_keys[entry], mask);\n        while (_index[slot] != 0)\n        {\n            slot = (slot + 1) & mask;\n        }\n        _index[slot] = entry + 1;\n    }\n\n    private void Rehash()\n    {\n        int n = _keys.Count;\n        \n        \n        \n        \n        \n        \n        \n        \n        \n        \n        \n        \n    }\n}\n\n", "" };
        yield return new object[] { "CS8625", "\n\nstatic int Look5(in Acc3 x = default) => x.Sum() + x.Total;\npublic sealed class Acc3(int seed)\n{\n    public int Total = seed;\n    \n    public int Sum() { int t = 0;  return t; }\n}\n\n", "Optional parameters" };
        yield return new object[] { "CS9200", "static int H<T>(ref readonly T value = default){return 6;}", "ref readonly parameters" };
        yield return new object[] { "CS0523", "return 0;\npublic struct BinderCtx\n{ public BinderCtx SelfLoop; \n    public IntMap<int> StdTypeByNameArity;\n    public IntMap<int> AppGlobalTypeByNameArity;\n    public Vec<int> PendingTypeIdx;\n    public int NextPendingTypeCursor;\n    public Vec<int> FuncWorkQueue;\n    public int NextFuncWorkCursor;\n    public bool CompilingStdItself;\n    public bool HasStdUnit;\n    public Vec<int> ExtensionFuncs;\n    public Vec<int> UsingBoundKind;\n    public Vec<int> UsingBoundId;\n    public Vec<int> UsingBoundTypeId;\n    public Vec<int> ConstStrChunks;\n\n    // Cached common interned NameIds\n    public int IdThis;\n    public int IdCtor;\n    public int IdMain;\n    public int IdDispose;\n    public int IdValue;\n    public int IdError;\n    public int IdOption;\n    public int IdOptionRef;\n    public int IdOptionMut;\n    public int IdSome;\n    public int IdNone;\n    public int IdNoneType;\n    public int IdResult;\n    public int IdOk;\n    public int IdErr;\n    public int IdUnit;\n    public int IdVec;\n    public int IdSpan;\n    public int IdReadOnlySpan;\n    public int IdNullable;\n    public int IdPrelude;\n    public int IdIo;\n    public int IdStdio;\n    public int IdVibraniumRuntime;\n    public int IdMove;\n    public int IdWrite;\n    public int IdWriteLine;\n    public int IdWriteOut;\n    public int IdWriteErr;\n    public int IdPanic;\n    public int IdConsoleIo;\n    public int IdBytes;\n    public int IdByteSpanExtensions;\n    public int IdStringUtf8Extensions;\n    public int IdUtf8SpanExtensions;\n    public int IdUtf8Lines;\n    public int IdUtf8ArgEnumerator;\n    public int IdAsUtf8;\n    public int IdLines;\n    public int IdGetEnumerator;\n    public int IdMoveNext;\n    public int IdCurrent;\n    public int IdExpect;\n    public int IdUnwrapOrDefault;\n    public int IdAdd;\n    public int IdTryParse;\n    public int IdTryParseInt32;\n    public int IdTryParseInt64;\n    public int IdMemoryExtensions;\n    public int IdSystem;\n    public int IdVibranium;\n    public int IdVibraniumStd;\n    public int IdGlobalPrefix;\n    public int IdGlobalSystem;\n    public int IdGlobalVibranium;\n    public int IdGlobalVibraniumStd;\n    public int IdIDisposable;\n    public int IdObject;\n    public int IdInt;\n    public int IdInt32;\n    public int IdLong;\n    public int IdInt64;\n    public int IdGetItem;\n    public int IdSetItem;\n    public int IdGetLength;\n    public int IdGetCount;\n    public int IdLength;\n    public int IdCount;\n    public int IdSlice;\n    public int IdSequenceEqual;\n    public int IdStartsWith;\n    public int IdEndsWith;\n    public int IdIndexOf;\n    public int IdLastIndexOf;\n    public int IdContains;\n    public int IdConsole;\n    public int IdArray;\n\n    public BinderCtx(ref Interner names, bool compilingStdItself, bool hasStdUnit = true)\n    {\n        StdTypeByNameArity = new IntMap<int>(64);\n        AppGlobalTypeByNameArity = new IntMap<int>(64);\n        PendingTypeIdx = new Vec<int>(64);\n        NextPendingTypeCursor = 0;\n        FuncWorkQueue = new Vec<int>(128);\n        NextFuncWorkCursor = 0;\n        CompilingStdItself = compilingStdItself;\n        HasStdUnit = hasStdUnit;\n        ExtensionFuncs = new Vec<int>(32);\n        UsingBoundKind = new Vec<int>(64);\n        UsingBoundId = new Vec<int>(64);\n        UsingBoundTypeId = new Vec<int>(64);\n        ConstStrChunks = new Vec<int>(32);\n\n        IdThis = names.Intern(\"this\"u8);\n        IdCtor = names.Intern(\".ctor\"u8);\n        IdMain = names.Intern(\"Main\"u8);\n        IdDispose = names.Intern(\"Dispose\"u8);\n        IdValue = names.Intern(\"Value\"u8);\n        IdError = names.Intern(\"Error\"u8);\n        IdOption = names.Intern(\"Option\"u8);\n        IdOptionRef = names.Intern(\"OptionRef\"u8);\n        IdOptionMut = names.Intern(\"OptionMut\"u8);\n        IdSome = names.Intern(\"Some\"u8);\n        IdNone = names.Intern(\"None\"u8);\n        IdNoneType = names.Intern(\"NoneType\"u8);\n        IdResult = names.Intern(\"Result\"u8);\n        IdOk = names.Intern(\"Ok\"u8);\n        IdErr = names.Intern(\"Err\"u8);\n        IdUnit = names.Intern(\"Unit\"u8);\n        IdVec = names.Intern(\"Vec\"u8);\n        IdSpan = names.Intern(\"Span\"u8);\n        IdReadOnlySpan = names.Intern(\"ReadOnlySpan\"u8);\n        IdNullable = names.Intern(\"Nullable\"u8);\n        IdPrelude = names.Intern(\"Prelude\"u8);\n        IdIo = names.Intern(\"Io\"u8);\n        IdStdio = names.Intern(\"Stdio\"u8);\n        IdVibraniumRuntime = names.Intern(\"VibraniumRuntime\"u8);\n        IdMove = names.Intern(\"Move\"u8);\n        IdWrite = names.Intern(\"Write\"u8);\n        IdWriteLine = names.Intern(\"WriteLine\"u8);\n        IdWriteOut = names.Intern(\"WriteOut\"u8);\n        IdWriteErr = names.Intern(\"WriteErr\"u8);\n        IdPanic = names.Intern(\"Panic\"u8);\n        IdConsoleIo = names.Intern(\"ConsoleIo\"u8);\n        IdBytes = names.Intern(\"Bytes\"u8);\n        IdByteSpanExtensions = names.Intern(\"ByteSpanExtensions\"u8);\n        IdStringUtf8Extensions = names.Intern(\"StringUtf8Extensions\"u8);\n        IdUtf8SpanExtensions = names.Intern(\"Utf8SpanExtensions\"u8);\n        IdUtf8Lines = names.Intern(\"Utf8Lines\"u8);\n        IdUtf8ArgEnumerator = names.Intern(\"Utf8ArgEnumerator\"u8);\n        IdAsUtf8 = names.Intern(\"AsUtf8\"u8);\n        IdLines = names.Intern(\"Lines\"u8);\n        IdGetEnumerator = names.Intern(\"GetEnumerator\"u8);\n        IdMoveNext = names.Intern(\"MoveNext\"u8);\n        IdCurrent = names.Intern(\"Current\"u8);\n        IdExpect = names.Intern(\"Expect\"u8);\n        IdUnwrapOrDefault = names.Intern(\"UnwrapOrDefault\"u8);\n        IdAdd = names.Intern(\"Add\"u8);\n        IdTryParse = names.Intern(\"TryParse\"u8);\n        IdTryParseInt32 = names.Intern(\"TryParseInt32\"u8);\n        IdTryParseInt64 = names.Intern(\"TryParseInt64\"u8);\n        IdMemoryExtensions = names.Intern(\"MemoryExtensions\"u8);\n        IdSystem = names.Intern(\"System\"u8);\n        IdVibranium = names.Intern(\"Vibranium\"u8);\n        IdVibraniumStd = names.Intern(\"Vibranium.Std\"u8);\n        IdGlobalPrefix = names.Intern(\"global::\"u8);\n        IdGlobalSystem = names.Intern(\"global::System\"u8);\n        IdGlobalVibranium = names.Intern(\"global::Vibranium\"u8);\n        IdGlobalVibraniumStd = names.Intern(\"global::Vibranium.Std\"u8);\n        IdIDisposable = names.Intern(\"IDisposable\"u8);\n        IdObject = names.Intern(\"Object\"u8);\n        IdInt = names.Intern(\"int\"u8);\n        IdInt32 = names.Intern(\"Int32\"u8);\n        IdLong = names.Intern(\"long\"u8);\n        IdInt64 = names.Intern(\"Int64\"u8);\n        IdGetItem = names.Intern(\"get_Item\"u8);\n        IdSetItem = names.Intern(\"set_Item\"u8);\n        IdGetLength = names.Intern(\"get_Length\"u8);\n        IdGetCount = names.Intern(\"get_Count\"u8);\n        IdLength = names.Intern(\"Length\"u8);\n        IdCount = names.Intern(\"Count\"u8);\n        IdSlice = names.Intern(\"Slice\"u8);\n        IdSequenceEqual = names.Intern(\"SequenceEqual\"u8);\n        IdStartsWith = names.Intern(\"StartsWith\"u8);\n        IdEndsWith = names.Intern(\"EndsWith\"u8);\n        IdIndexOf = names.Intern(\"IndexOf\"u8);\n        IdLastIndexOf = names.Intern(\"LastIndexOf\"u8);\n        IdContains = names.Intern(\"Contains\"u8);\n        IdConsole = names.Intern(\"Console\"u8);\n        IdArray = names.Intern(\"Array\"u8);\n    }\n}\n\n", "" };
        yield return new object[] { "CS8174", "int x=5;int y=7;ref int r;WriteLine(G(in(r=ref y)));static int G(in int value)=>value;", "" };
        yield return new object[] { "CS0191", "static int F(int p0)\n{\n    if (p0 > 100)\n    {\n        SO s;\n        s.A = 1;\n    }\n    return p0;\n}\npublic struct SO(int a, int b, Vec<int> n)\n{\n    readonly public int A = a;\n    public int B = b;\n    public Vec<int> V = n;\n}\n", "" };
        yield return new object[] { "CS0718", "static class Program\n{\n    static int Consume(Vec<int> xs) => xs[0];\n    static int UnwrapNested(Option<Option<Vec<int>>> outer) =>\n        Move(outer) switch\n        {\n            Some<Program>(Some<Vec<int>>(var inner)) => Consume(Move(inner)),\n            _ => 0,\n        };\n    public static void Main()\n    {\n    }\n}\n", "" };
        yield return new object[] { "CS8171", "var h = new H();\nlong r = ref h.Slot();\nclass H { public int N; [Mut] public ref int Slot() => ref N; }\n", "" };
        yield return new object[] { "CS8172", "var h = new H();\nref int r = h.Slot();\nclass H { public int N; [Mut] public ref int Slot() => ref N; }\n", "" };
        yield return new object[] { "CS8983", "return 0;\nstruct C { private int secret = 4; }\n", "" };
    }

    [Theory]
    [MemberData(nameof(FrozenMandatoryRepros))]
    public void FrozenMandatoryFirstIdsAreRejected(string roslynId, string source, string cutMessage)
    {
        AssertRejected("frozen-" + roslynId, roslynId, source, cutMessage);
    }

    public static IEnumerable<object[]> FieldInitializationParity()
    {
        yield return new object[] { "class-no-ctor", "return 0;class C{private Node value;}class Node{}", "VIB008" };
        yield return new object[] { "class-explicit-ctor", "return 0;class C{private Node value;public C(){}}class Node{}", "VIB008" };
        yield return new object[] { "vec-no-ctor", "return 0;class C{private Vec<int> value;}", "VIB008" };
        yield return new object[] { "stringbuf-no-ctor", "return 0;class C{private Vibranium.Std.StringBuf value;}", "VIB008" };
        yield return new object[] { "owned-struct-no-ctor", "return 0;class C{private S value;}struct S{public Node Value;public S(Node value){Value=value;}}class Node{}", "VIB008" };
        yield return new object[] { "option-none-zero-safe", "return 0;class C{private Option<Node> value;public bool IsSome()=>value.IsSome;}class Node{}", "" };
        yield return new object[] { "zero-safe-private-int", "return 0;class C{private int value;public int Read()=>value;}", "" };
        yield return new object[] { "initialized-class-field", "return 0;class C{private Node value=new Node();public int Read()=>value.Value;}class Node{public int Value=7;}", "" };
    }

    [Theory]
    [MemberData(nameof(FieldInitializationParity))]
    public void WarningOnlyFieldsPreserveSafetyParity(string name, string source, string expectedId)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "FieldInitializationParity", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            string[] expected = expectedId.Length == 0 ? Array.Empty<string>() : new[] { expectedId };
            Assert.Equal(expected, frontend.Diagnostics.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Equal(expected, analyzer.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var self = RunSelf(path);
            Assert.Equal(expected.Length == 0 ? 0 : 1, self.Exit);
            Assert.Equal(expected, Regex.Matches(self.Output + self.Error, @"VIB\d{3}").Select(match => match.Value).Distinct().OrderBy(id => id));
        });
    }

    [Theory]
    [InlineData("using System.Runtime.CompilerServices;[MethodImpl(1)]static int F()=>1;class MethodImplAttribute:System.Attribute{public MethodImplAttribute(int value){}}")]
    [InlineData("using MI=Local.MarkerAttribute;[MI(1)]static int F()=>1;namespace Local{class MarkerAttribute:System.Attribute{public MarkerAttribute(int value){}}}")]
    public void SourceAttributeShadowsDoNotAcquireFrameworkFlagValidation(string source)
    {
        Assert.DoesNotContain(Round12CutSelfTests.Inspect(source), diagnostic => diagnostic.Contains("Invalid MethodImpl options", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using A=System.Diagnostics.CodeAnalysis.UnscopedRefAttribute;WriteLine(1);static void F(A value){}")]
    [InlineData("using A=System.Runtime.CompilerServices.MethodImplAttribute;WriteLine(1);static void F(A value){}")]
    [InlineData("using A=System.ApplicationException;WriteLine(1);static void F(A value){}")]
    public void FrameworkAttributeAliasDoesNotAdmitArbitraryRuntimeTypes(string source)
    {
        WithSource("metadata-only-alias", source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MetadataOnlyAlias", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Contains("error VIB000:", self.Error, StringComparison.Ordinal);
        });
    }

    public static IEnumerable<object[]> FrozenMandatoryV2Repros()
    {
        yield return new object[] { "CS0029-accept", "CS0029", " WriteLine(C.F(2));\npublic static class C {\n    \n    public static char F(int x) => x switch { int Value when Value > 0 => 1, _ => 2 };\n}", "-" };
        yield return new object[] { "CS0191-accept", "CS0191", "\nstatic int F(int p0)\n{\n    if (p0 > 100)\n    {\n        VibraniumRuntime.Panic(\"big\");\n        SO s;\n        s.A = 1;\n        s.B = 2;\n        s.V = [1];\n        return s.A;\n    }\n    return p0;\n}\npublic struct SO(int a, int b, Vec<int> n)\n{\n    public int A = a;\n    public int B = b;\n    readonly public Vec<int> V = n;\n}\n\n", "-" };
        yield return new object[] { "CS0192-accept", "CS0192", "var p=new Pair(new Box(1));Set(out p.Value,9);static void Set(out Box x,int n){x=new Box(n);}public struct Pair{readonly public Box Value;public Pair(Box value){Value=value;}}public class Box{public Box(int n){}}", "-" };
        yield return new object[] { "CS0400-accept", "CS0400", "return 0;\npublic struct Interner\n{\n    private StrMap<int> _map;\n\n    \n\n    \n\n    public int Intern(global::System.ReadOnlySpan<byte> text)\n    {\n        \n        \n        int id = _map.EntryCount;\n        _map.Set(text, id);\n        return id;\n    }\n\n    public int Intern(string text) => Intern(text.AsUtf8());\n\n    [global::SystemZz.Diagnostics.CodeAnalysis.UnscopedRef]\n    public readonly global::System.ReadOnlySpan<byte> Text(int id) => _map.KeyAt(id);\n}\n\n", "-" };
        yield return new object[] { "CS0432-accept", "CS0432", "return 0;\npublic struct ArgList\n{\n    private Vec<byte> _bytes;\n    private Vec<int> _ends;\n\n    \n\n    \n\n    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]\n    public readonly global::System.ReadOnlySpan<byte> Get(int index)\n    {\n        int end = _ends[index];\n        int start = index == 0 ? 0 : _ends[index - 1] + 1;\n        return _bytes.AsReadOnlySpan().Slice(start, end - start);\n    }\n\n    [globalZz::System.Diagnostics.CodeAnalysis.UnscopedRef]\n    public readonly global::System.ReadOnlySpan<byte> this[int index] => Get(index);\n\n    \n}\n\n", "-" };
        yield return new object[] { "CS0523-accept", "CS0523", "struct Node\n{\n    \n    public Option<Node> Next;\n    \n}\n\n", "-" };
        yield return new object[] { "CS0523-failure", "CS0523", "Pair p;Set(out p.X);p.Y=2;WriteLine(p.X+p.Y);static void Set(out int x){x=3;}public struct Pair{ public Pair SelfLoop; public int X;public int Y;}", "-" };
        yield return new object[] { "CS0591-accept", "CS0591", "using System.Runtime.CompilerServices;\n\n\n[MethodImpl(1)]\nstatic int F(int a) => a + 1;\n", "-" };
        yield return new object[] { "CS0718-accept", "CS0718", "WriteLine(C.M(-1, true));\nWriteLine(C.M(1, false));\npublic static class C\n{\n    public static int M(int p0, bool p1)\n    {\n        int acc = p0;\n        Option<Cell1> o2 = None;\n        \n        \n        \n        \n        \n        if (o2.AsRef() is Some<C>(var o2_f)) WriteLine(o2_f.V); else \n        \n        WriteLine(acc);\n        return acc;\n    }\n    \n    \n}\npublic sealed class Cell1(int v)\n{\n    public int V = v;\n    public Option<Cell1> Next = None;\n}\n", "-" };
        yield return new object[] { "CS1059-accept", "CS1059", "return 0;\npublic readonly struct MirOperand\n{\n    public readonly int Kind;\n    \n    \n    \n    \n    \n\n    public MirOperand(int kind, int placeOrLitIdx, long intVal, int typeId, int tokIdx, int sourceStartTokIdx = --1)\n    {\n        Kind = kind;\n        \n        \n        \n        \n        \n    }\n}\n\n", "C12" };
        yield return new object[] { "CS8156-accept", "CS8156", "var box=new C();const int value=4;\n\nWriteLine(G(in (value>0?ref value:ref box.X)));\nstatic int G(in int value)=>value+1;\n\npublic class C{public int X=5;public ref readonly int RP=>ref X;}", "C15+C19" };
        yield return new object[] { "CS8171-accept", "CS8171", "var h = new H();\nlong r = ref h.Slot();\n\n\n\nclass H { public int N; [Mut] public ref int Slot() => ref N; }\n", "-" };
        yield return new object[] { "CS8172-accept", "CS8172", "ReadOnlySpan<int> outer=[1];ref readonly int r=outer[0];{}", "-" };
        yield return new object[] { "CS8174-failure", "CS8174", "var h = new H();\nref int r ;\n\n\nreturn 0;\nclass H {   }\n", "-" };
        yield return new object[] { "CS8327-accept", "CS8327", "long a=12;int b=20;bool flag=true;WriteLine((flag?ref a:ref b)++);", "C15" };
        yield return new object[] { "CS8506-accept", "CS8506", "WriteLine(0);\npublic static class C\n{\n    public static int M(int p0, bool p1)\n    {\n        int acc = 0;\n        var s5 = false switch { true when true => 2147483648, false => 0 };\n        return acc;\n    }\n}\n", "C19" };
        yield return new object[] { "CS8602-accept", "CS8602", "static void Take(Pair p = default){WriteLine(p.Value.Value);}public struct Pair{public Box Value;}public class Box{public int Value;}", "C12" };
        yield return new object[] { "CS8983-accept", "CS8983", "var c=new @record();struct @record{public int X=7;}", "-" };
    }

    [Theory]
    [MemberData(nameof(FrozenMandatoryV2Repros))]
    public void FrozenV2MandatoryFirstIdsAreRejected(string name, string roslynId, string source, string cuts)
    {
        AssertRejected("frozen-v2-" + name, roslynId, source);
        if (cuts == "-") return;
        WithSource("frozen-v2-cuts-" + name, source, path =>
        {
            var self = RunSelf(path);
            foreach (string cut in cuts.Split('+'))
                Assert.Contains(Round12CutSelfTests.CutMessage(int.Parse(cut.AsSpan(1))), self.Error, StringComparison.Ordinal);
        });
    }

    public static IEnumerable<object[]> RejectedMoveParityCases()
    {
        yield return new object[] { "r12f3_loop_move_borrowed", "WriteLine(Snd3(2));\nstatic int Sink2(Vec<int> x) => x.Count;\nstatic int Snd3(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; ReadOnlySpan<int> s = v.AsReadOnlySpan(); for (int i = 0; i < 2; i++) { acc += s[0]; acc += Sink2(v); } return acc; }\n", "VIB003" };
        yield return new object[] { "r12f3_loop_move_borrowed_reborrow", "WriteLine(Snd3(2));\nstatic ReadOnlySpan<int> Head1(ReadOnlySpan<int> x) => x[..1];\nstatic int Sink2(Vec<int> x) => x.Count;\nstatic int Snd3(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; ReadOnlySpan<int> s = Head1(v.AsReadOnlySpan()); for (int i = 0; i < 2; i++) { acc += s[0]; if (i == 0) { acc += Sink2(v); } } return acc; }\n", "VIB003" };
        yield return new object[] { "r12f3_field_move_borrowed", "WriteLine(Snd2(2));\nstatic int Snd2(int p) { int acc = 0; var h = new Hold1(p); ReadOnlySpan<int> s = h.View(); if (p > 1) { var it = h.Items; acc += it.Count; } acc += s[0]; return acc; }\npublic sealed class Hold1(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); }\n", "VIB003,VIB005" };
        yield return new object[] { "r12f3_field_move_noborrow_control", "WriteLine(Snd2(2));\nstatic int Snd2(int p) { int acc = 0; var h = new Hold1(p); if (p > 1) { var it = h.Items; acc += it.Count; } acc += h.Items.Count; return acc; }\npublic sealed class Hold1(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); }\n", "VIB005" };
        yield return new object[] { "r12f3_struct_field_move_noborrow_control", "WriteLine(Snd2(2));\nstatic int Snd2(int p) { int acc = 0; var h = new Hold1(p); if (p > 1) { var it = h.Items; acc += it.Count; } acc += h.Items.Count; return acc; }\npublic struct Hold1(int p) { public Vec<int> Items = [p, 2, 3]; }\n", "VIB005" };
        yield return new object[] { "loop-view-used-after-call", "WriteLine(F(2));static int F(int p){Vec<int> v=[p,2,3];ReadOnlySpan<int>s=v.AsReadOnlySpan();int acc=0;for(int i=0;i<2;i++){acc+=Sink(v);}return acc+s[0];}static int Sink(Vec<int>x)=>x.Count;", "VIB003" };
        yield return new object[] { "foreach-lines", "StringBuf buf=new StringBuf();buf.Append(\"a\\nb\");foreach(var line in buf.Lines()){Sink(buf);}static void Sink(StringBuf value){}", "VIB003" };
        yield return new object[] { "assignment-rejected-move", "Vec<int> v=[1,2,3];ReadOnlySpan<int>s=v.AsReadOnlySpan();var other=v;WriteLine(s[0]);WriteLine(v.Count);WriteLine(other.Count);", "VIB003" };
        yield return new object[] { "second-move-after-loan-expires", "Vec<int> v=[1,2,3];ReadOnlySpan<int>s=v.AsReadOnlySpan();Sink(v);WriteLine(s[0]);Sink(v);static void Sink(Vec<int>x){WriteLine(x.Count);}", "VIB003" };
        yield return new object[] { "class-field-borrowed-direct", "var h=new H();var s=h.Items.AsReadOnlySpan();var moved=h.Items;WriteLine(s[0]);WriteLine(moved.Count);sealed class H{public Vec<int> Items=[1,2,3];}", "VIB003,VIB005" };
        yield return new object[] { "real-move-still-consumes", "Vec<int>v=[1,2,3];var moved=v;WriteLine(moved.Count);WriteLine(v.Count);", "VIB001" };
        yield return new object[] { "move-after-loop-view-expires", "Vec<int>v=[2,2,3];var s=v.AsReadOnlySpan();int sum=0;for(int i=0;i<2;i++){sum+=s[0];}sum+=Sink(v);WriteLine(sum);static int Sink(Vec<int>x)=>x.Count;", "" };
        yield return new object[] { "move-after-lines-expires", "StringBuf buf=new StringBuf();buf.Append(\"a\\nb\");foreach(var line in buf.Lines()){WriteLine(line);}Sink(buf);static void Sink(StringBuf value){WriteLine(value.Length);}", "" };
    }

    [Theory]
    [MemberData(nameof(RejectedMoveParityCases))]
    public void RejectedMovesDoNotConsumeButKeepFormDiagnostics(string name, string source, string expectedIds)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "RejectedMoveParity", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            string[] expected = expectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(id => id).ToArray();
            var frontend = VibraniumFrontend.CompileAndCheck(compilation);
            Assert.Equal(expected, frontend.Diagnostics.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var analyzer = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Assert.Equal(expected, analyzer.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id));
            var self = RunSelf(path);
            Assert.Equal(expected.Length == 0 ? 0 : 1, self.Exit);
            Assert.Equal(expected, Regex.Matches(self.Output + self.Error, @"VIB\d{3}").Select(match => match.Value).Distinct().OrderBy(id => id));
        });
    }

    private static void AssertRejected(string name, string roslynId, string source, string cutMessage = "")
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MandatoryRejected", includeStd: true);
            Assert.Contains(compilation.GetDiagnostics(), diagnostic => roslynId.Length == 0
                ? diagnostic.Severity == DiagnosticSeverity.Error
                : diagnostic.Id == roslynId);
            var self = RunSelf(path);
            Assert.Equal(1, self.Exit);
            Assert.Matches(@"\berror VIB\d{3}:", self.Error);
            if (cutMessage.Length > 0) Assert.Contains(cutMessage, self.Error, StringComparison.Ordinal);
        });
    }

    private static void AssertAccepted(string name, string source)
    {
        WithSource(name, source, path =>
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "MandatoryAccepted", includeStd: true);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var self = RunSelf(path);
            Assert.True(self.Exit == 0, self.Output + self.Error);
            Assert.Empty(self.Error);
        });
    }

    private static void WithSource(string name, string source, Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vibranium-r12-mandatory-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Program.cs");
            File.WriteAllText(path, source);
            action(path);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (int Exit, string Output, string Error) RunSelf(string path)
    {
        Assert.True(File.Exists(Round11AuditAssertions.SelfCompiler), "Build src/vib-self/VibraniumSelfCompiler.csproj before running self-hosted tests.");
        var start = new ProcessStartInfo(Round11AuditAssertions.DotnetExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { Round11AuditAssertions.SelfCompiler, "check", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src") })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Self source validation must terminate within ten seconds.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
