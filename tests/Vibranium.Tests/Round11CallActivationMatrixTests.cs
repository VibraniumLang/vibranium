using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round11CallActivationMatrixTests
{
    private static readonly string[] Loans = ["in", "readonly", "ref", "out", "rospan", "span", "asmut", "slice"];
    private static readonly string[] Moves = ["value", "move", "consume", "assignment"];
    private static readonly string[] Forms = ["local", "static", "instance", "extension", "constructor"];

    // The readonly dimension is a ref-readonly local passed to an in parameter;
    // C10 cuts only ref-readonly parameters, not this retained borrow form.
    // Exhaustive compatible argument cross product: 8 loans x 4 moves x 3
    // overlapping paths x 5 call forms. Receiver/operator/indexer/assignment
    // operands have different C# signatures and are covered separately below;
    // e.g. C# operators/indexers cannot declare ref/out parameters.
    public static IEnumerable<object[]> RejectedArguments()
    {
        foreach (string loan in Loans)
        foreach (string move in Moves)
        foreach (string relation in new[] { "same", "prefix", "extension" })
        foreach (string form in Forms)
        {
            var (source, _) = MakeArgumentCase(loan, move, relation, form, "");
            string place = relation == "same" ? "v" : relation == "prefix" ? "h" : "h.N";
            yield return new object[] { $"{loan}/{move}/{relation}/{form}", source, place };
        }
    }

    [Theory]
    [MemberData(nameof(RejectedArguments))]
    public Task EarlierArgumentLivesThroughInvocation(string name, string source, string place)
    {
        Assert.NotEmpty(name);
        // A borrow-rejected field move still violates the field-move form (F3).
        return Round11AuditAssertions.AssertRejected(source, "VIB003", Message(place), exactPosition: true, additionalId: place == "h.N" ? "VIB005" : null);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("static")]
    [InlineData("instance")]
    [InlineData("extension")]
    [InlineData("constructor")]
    public Task EveryUnrelatedOwnerControlExecutes(string form)
    {
        var statements = new List<string>();
        var declarations = new List<string>();
        int id = 0;
        foreach (string loan in Loans)
        foreach (string move in Moves)
        {
            var (body, types) = MakeArgumentCase(loan, move, "unrelated", form, (++id).ToString(), split: true);
            statements.Add("{" + body + "}");
            declarations.Add(types);
        }
        string source = string.Join("\n", statements) + "\n" + string.Join("\n", declarations);
        return Round11AuditAssertions.AssertDifferential(source, string.Concat(Enumerable.Repeat("1\n", id)));
    }

    private static string Message(string place) => $"Cannot move '{place}' while it is borrowed by an earlier argument or receiver of the same call.";

    private static (string Source, string Declarations) MakeArgumentCase(string loan, string move, string relation, string form, string suffix, bool split = false)
    {
        string node = "Node" + suffix, holder = "Holder" + suffix, api = "Api" + suffix, product = "Product" + suffix, extension = "Extensions" + suffix;
        bool option = loan == "asmut";
        string owner = option ? $"Option<{node}>" : "Vec<int>";
        string init = option ? $"Some(new {node}())" : "[1,2]";
        bool extended = relation == "extension";
        string borrowed = relation == "prefix" ? "h.N" : extended ? "h" : "v";
        string borrowedType = extended ? holder : owner;
        string movedType = relation == "prefix" ? holder : owner;
        string moved = relation == "same" ? "v" : relation == "prefix" ? "h" : extended ? "h.N" : "other";
        string newMoved = relation == "prefix" ? $"new {holder}({init})" : init;
        string marked = relation == "unrelated" ? moved : "/*move*/" + moved;
        string rhs = move switch { "move" => $"Move({marked})", "consume" => $"{marked}.Consume{suffix}()", "assignment" => $"(destination={marked})", _ => marked };
        string argument = loan switch
        {
            "in" => "in " + borrowed,
            "readonly" => "in readonlyLoan",
            "ref" => "ref " + borrowed,
            "out" => "out " + borrowed,
            "rospan" => borrowed + ".AsReadOnlySpan()",
            "span" => borrowed + ".AsSpan()",
            "asmut" => borrowed + ".AsMut()",
            _ => borrowed + ".AsSpan()[..]",
        };
        string first = loan switch
        {
            "in" => $"in {borrowedType} a",
            "readonly" => $"in {borrowedType} a",
            "ref" => $"ref {borrowedType} a",
            "out" => $"out {borrowedType} a",
            "rospan" => "ReadOnlySpan<int> a",
            "asmut" => $"OptionMut<{node}> a",
            _ => "Span<int> a",
        };
        string body = loan == "out" ? $"a={(extended ? $"new {holder}({init})" : init)};" : "";
        string read = loan == "asmut" ? "a.IsSome?1:0" :
            extended && loan is "in" or "readonly" or "ref" or "out" ? "a.N[0]" : "a[0]";
        string parameters = first + "," + movedType + " b";
        string method = $"int Use({parameters}){{{body}return {read};}}";
        string invocation = form switch
        {
            "static" => $"{api}.Use({argument},{rhs})",
            "instance" or "extension" => $"api.Use({argument},{rhs})",
            "constructor" => $"new {product}({argument},{rhs}).Value",
            _ => $"Use({argument},{rhs})",
        };
        string readonlyLocal = loan == "readonly" ? $"ref readonly {borrowedType} readonlyLoan=ref {borrowed};" : "";
        string code = $"{owner} v={init};{owner} other={init};{holder} h=new {holder}({init});var api=new {api}();{movedType} destination={newMoved};{readonlyLocal}WriteLine({invocation});";
        if (form == "local") code += "static " + method;
        string holderView = option
            ? $"[Mut]public OptionMut<{node}> AsMut()=>N.AsMut();"
            : "[Mut]public Span<int> AsSpan()=>N.AsSpan();public ReadOnlySpan<int> AsReadOnlySpan()=>N.AsReadOnlySpan();";
        string types = $"class {node}{{public int Value=1;}}class {holder}({owner} n){{public {owner} N=n;{holderView}}}" +
            $"class {api}{{" + (form == "static" ? "public static " + method : form == "instance" ? "public " + method : "") + "}" +
            $"static class {extension}{{public static {movedType} Consume{suffix}(this {movedType} value)=>value;" +
            (form == "extension" ? $"public static int Use(this {api} self,{parameters}){{{body}return {read};}}" : "") + "}" +
            (form == "constructor" ? $"class {product}{{public int Value;public {product}({parameters}){{{body}Value={read};}}}}" : "");
        return (split ? code : code + types, types);
    }

    private static readonly string[] ReceiverForms = ["shared", "mut", "thisin", "thisref", "indexget", "indexset", "operator"];

    public static IEnumerable<object[]> RejectedReceivers()
    {
        foreach (string form in ReceiverForms)
        foreach (string move in Moves)
        foreach (string relation in new[] { "same", "prefix", "extension" })
        {
            var (code, types) = MakeReceiverCase(form, move, relation, "");
            yield return new object[] { $"{form}/{move}/{relation}", code + types, relation == "same" ? "v" : relation == "prefix" ? "h" : "h.N" };
        }
    }

    [Theory]
    [MemberData(nameof(RejectedReceivers))]
    public Task ReceiverIndexerAndOperatorCrossProduct(string name,string source,string place)
    {
        Assert.NotEmpty(name);
        return Round11AuditAssertions.AssertRejected(source,"VIB003",Message(place),exactPosition:true,additionalId:place=="h.N"?"VIB005":null);
    }

    [Fact]
    public Task EveryUnrelatedReceiverIndexerAndOperatorControlExecutes()
    {
        var statements = new List<string>();
        var declarations = new List<string>();
        int id=0;
        foreach(string form in ReceiverForms)
        foreach(string move in Moves)
        {
            var (code,types)=MakeReceiverCase(form,move,"unrelated",(++id).ToString());
            statements.Add("{"+code+"}");declarations.Add(types);
        }
        return Round11AuditAssertions.AssertDifferential(string.Join("\n",statements)+"\n"+string.Join("\n",declarations),string.Concat(Enumerable.Repeat("1\n",id)));
    }

    private static (string Code,string Types) MakeReceiverCase(string form,string move,string relation,string suffix)
    {
        string box="Box"+suffix,holder="Holder"+suffix,extension="Extensions"+suffix;
        string movedType=relation=="prefix"?holder:box;
        string receiverType=relation=="extension"?holder:box;
        string receiver=relation=="prefix"?"h.N":relation=="extension"?"h":"v";
        string moved=relation=="prefix"?"h":relation=="extension"?"h.N":relation=="unrelated"?"other":"v";
        string marked=relation=="unrelated"?moved:"/*move*/"+moved;
        string rhs=move switch{"move"=>$"Move({marked})","consume"=>$"{marked}.Consume{suffix}()","assignment"=>$"(destination={marked})",_=>marked};
        string init=$"new {box}([1])",hinit=$"new {holder}({init})";
        string operation=form switch
        {
            "shared"=>$"WriteLine({receiver}.Probe({rhs}));",
            "mut"=>$"WriteLine({receiver}.Touch({rhs}));",
            "thisin"=>$"WriteLine({receiver}.UseIn({rhs}));",
            "thisref"=>$"WriteLine({receiver}.UseRef({rhs}));",
            "indexget"=>$"WriteLine({receiver}[{rhs}]);",
            "indexset"=>$"{receiver}[{rhs}]=1;",
            _=>$"{receiver}+={rhs};",
        };
        string code=$"var v={init};var other={init};var h={hinit};{movedType} destination={(relation=="prefix"?hinit:init)};"+operation;
        string Members(string read) => $"public readonly int Probe({movedType} x)=>{read};[Mut]public int Touch({movedType} x)=>{read};public int this[{movedType} x]{{readonly get=>{read};[Mut]set{{WriteLine({read}+value-1);}}}}[Mut]public void operator +=({movedType} x){{WriteLine({read});}}";
        string receiverRead=receiverType==holder?"a.N.Data[0]":"a.Data[0]";
        string types=$"struct {box}(Vec<int> data){{public Vec<int> Data=data;{Members("Data[0]")}}}struct {holder}({box} n){{public {box} N=n;{Members("N.Data[0]")}}}"+
            $"static class {extension}{{public static {movedType} Consume{suffix}(this {movedType} value)=>value;public static int UseIn(this in {receiverType} a,{movedType} x)=>{receiverRead};public static int UseRef(this ref {receiverType} a,{movedType} x)=>{receiverRead};}}";
        return(code,types);
    }

    [Theory]
    [InlineData("Vec<int>v=[1,2];WriteLine(v[Count(/*move*/v)]);static int Count(Vec<int>x)=>0;", "v")]
    [InlineData("Vec<int>v=[1,2];v[0]=Count(/*move*/v);static int Count(Vec<int>x)=>0;", "v")]
    [InlineData("Vec<int>v=[1,2];v[0]+=Count(/*move*/v);static int Count(Vec<int>x)=>0;", "v")]
    [InlineData("Vec<int>v=[1,2];ref Vec<int>r=ref v;r=Move(/*move*/v);", "v")]
    [InlineData("var h=new H(new N());Use(ref h.N,/*move*/h);static void Use(ref N n,H h){}class N{public int V=1;}class H(N n){public N N=n;}", "h")]
    public Task EvaluatedProjectionAndAssignmentTargetsRetainTheirOwners(string source, string place) =>
        Round11AuditAssertions.AssertRejected(source, "VIB003", Message(place), exactPosition: true);

    [Theory]
    [InlineData("Vec<int>v=[1,2];v=Move(v);WriteLine(v.Count);", "2\n")]
    [InlineData("Vec<int>v=[1,2];v=Identity(v);WriteLine(v.Count);static Vec<int> Identity(Vec<int>x)=>x;", "2\n")]
    [InlineData("Vec<int>v=[1,2];v.Add(v.Count);WriteLine(v[2]);", "2\n")]
    [InlineData("Vec<int>v=[1,2,3];Span<int>s=v.AsSpan();Span<int>t=First(s);t[0]=4;WriteLine(s.Length);WriteLine(s[0]);static Span<int>First(Span<int>x)=>x[..2];", "3\n4\n")]
    [InlineData("Vec<int>v=[1,2,3];Edit(v.AsSpan());static void Edit(Span<int>s){Span<int>t=First(s);t[0]=4;WriteLine(s.Length);WriteLine(s[0]);}static Span<int>First(Span<int>x)=>x[..2];", "3\n4\n")]
    public Task NllTwoPhaseReborrowsAndPlainSlotReplacementStayLegal(string source, string output) => Round11AuditAssertions.AssertDifferential(source, output);
    [Theory]
    [InlineData("Vec<int>v=[1];Use(v,in v);static void Use(Vec<int>a,in Vec<int>b){}")]
    [InlineData("Vec<int>v=[1];Use(b:Move(v),a:in v);static void Use(Vec<int>b,in Vec<int>a){}")]
    public Task EarlierMoveStillRejectsLaterBorrowAsUseAfterMove(string source) => Round11AuditAssertions.AssertRejected(source,"VIB001");

    [Theory]
    [InlineData("Span<int>s=[1,2];F(s,s,1);static void F(Span<int>a,Span<int>b,int k){}")]
    [InlineData("Span<int>s=[1,2];F(s,s,1);static void F(Span<int>a,ReadOnlySpan<int>b,int k){}")]
    [InlineData("Vec<int>v=[1];F(ref v,ref v);static void F(ref Vec<int>a,ref Vec<int>b){}")]
    public Task OverlappingOperandsKeepTheirCallPairDiagnostic(string source) => Round11AuditAssertions.AssertRejected(source,"VIB002");

    [Theory]
    [InlineData("Span<int>s=[1,2];F(s,G(s));static void F(Span<int>a,int k){}static int G(Span<int>s){s[0]=3;return 1;}")]
    [InlineData("Vec<int>v=[1,2];Span<int>s=v.AsSpan();Span<int>t=First(s);WriteLine(s.Length);WriteLine(t[0]);static Span<int>First(Span<int>x)=>x[..1];")]
    public Task NestedInvocationAndLiveReturnedChildStillFreezeTheirParent(string source) => Round11AuditAssertions.AssertRejected(source,"VIB003");

    [Theory]
    [InlineData("Vec<int>v=[1];F(b:in v,a:/*move*/v);static void F(in Vec<int>b,Vec<int>a){}", "v", null)]
    [InlineData("Vec<int>v=[1,2];WriteLine(Both(v.AsReadOnlySpan(),/*move*/v));static int Both<T>(ReadOnlySpan<int>a,T b)=>a[0];", "v", null)]
    [InlineData("var r=new R(1);Use(in r,/*move*/r);static void Use(in R a,R b){Sink(b);WriteLine(a.Id);}static void Sink(R x){using var resource=x;}class R(int id):IDisposable{public int Id=id;public void Dispose(){WriteLine(Id);}}", "r", null)]
    [InlineData("var h=new H(new N());ref readonly N r=ref h.N;F(in h,/*move*/r);static void F(in H h,N n){}class N{public int X=1;}class H(N n){public N N=n;}", "r", "VIB005")]
    [InlineData("Vec<int>v=[1];Vec<int>d=[2];bool flag=Flag();F(in v,flag?(d=Move(/*move*/v)):new Vec<int>());static bool Flag()=>true;static void F(in Vec<int>a,Vec<int>b){}", "v", null)]
    public Task NamedGenericAndLinearCallsUseTheSameActivationRule(string source,string place,string? additionalId) => Round11AuditAssertions.AssertRejected(source,"VIB003",Message(place),exactPosition:true,additionalId:additionalId);

    [Theory]
    [InlineData("Vec<int>v=[1,2,3,4];Span<int>s=v.AsSpan();ref int r=ref At(s,1);r=40;s[0]=3;WriteLine(v[0]+v[1]);static ref int At(Span<int>x,int i)=>ref x[i];", "43\n")]
    [InlineData("Vec<int>v=[1,2,3,4];Span<int>s=v.AsSpan();Span<int>t=Pass(s);t[0]=4;s[1]=3;WriteLine(v[0]+v[1]);static Span<int>Pass(Span<int>x)=>x;", "7\n")]
    public Task CallReturnedRefAndWholeSpanAreParentReborrows(string source,string expected) => Round11AuditAssertions.AssertDifferential(source,expected);

    [Fact]
    public Task PlainStructFieldStoreRetainsItsExistingMovedStructDiagnostic() => Round11AuditAssertions.AssertRejected(
        "var s=new S([1]);s.F=Take(s);static Vec<int>Take(S x)=>new Vec<int>();struct S(Vec<int>f){public Vec<int>F=f;}", "VIB001");

}
