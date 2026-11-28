using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12OptionAsRefStorageTests
{
    private const string MakeNode = "static Option<Node> MakeNode() => Some(new Node()); sealed class Node {}";
    private const string Message = "Option.AsRef requires a receiver with storage; a temporary Option cannot be borrowed.";

    [Theory]
    [InlineData("WriteLine(MakeNode().AsRef().IsSome);" + MakeNode, "MakeNode()")]
    [InlineData("WriteLine(Vibranium.OptionExtensions.AsRef(MakeNode()).IsSome);" + MakeNode, "MakeNode()")]
    [InlineData("var h=new Holder(); WriteLine(h.Item.AsRef().IsSome); sealed class Holder { public Option<int> Item => Some(3); }", "h.Item")]
    [InlineData("WriteLine(Make().Item.AsRef().IsSome); static Holder Make()=>new Holder(3); struct Holder { public Option<int> Item; public Holder(int x) { Item=Some(x); } }", "Make().Item")]
    [InlineData("var a=MakeNode(); var b=MakeNode(); bool first=true; WriteLine((first ? a : b).AsRef().IsSome);" + MakeNode, "first ? a : b")]
    [InlineData("var slot=MakeNode(); WriteLine(((Option<Node>)slot).AsRef().IsSome);" + MakeNode, "(Option<Node>)slot")]
    [InlineData("WriteLine(OptionExtensions.AsRef<Node>(Some(new Node())).IsSome); sealed class Node {}", "Some(new Node())")]
    [InlineData("WriteLine(Test(Some(3))); static bool Test(Some<int> wrapper)=>OptionExtensions.AsRef<int>(@wrapper).IsSome;", "@wrapper")]
    [InlineData("WriteLine(0); static class Generic { public static bool Test<T>(T value)=>Make(value).AsRef().IsSome; static Option<T> Make<T>(T value)=>Some(value); }", "Make(value)")]
    public async Task RejectsNonStorageAtTheActualReceiver(string source, string receiver)
    {
        string temp = Directory.CreateTempSubdirectory("vibranium-option-storage-").FullName;
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "OptionStorageReject", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var check = VibraniumFrontend.CompileAndCheck(compilation);
            var diag = Assert.Single(check.Diagnostics);
            int start = source.IndexOf(receiver, StringComparison.Ordinal);
            Assert.Equal(("VIB000", Message, start, receiver.Length), (diag.Id, diag.Message, diag.Span.Start, diag.Span.Length));
            var live = Assert.Single(await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync());
            Assert.Equal((diag.Id, diag.Message, diag.Span.Start, diag.Span.Length), (live.Id, live.GetMessage(), live.Location.SourceSpan.Start, live.Location.SourceSpan.Length));
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compilation).Success);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Theory]
    [InlineData("var slot=MakeNode(); WriteLine(slot.AsRef().IsSome);" + MakeNode)]
    [InlineData("var slot=MakeNode(); WriteLine(Vibranium.OptionExtensions.AsRef(slot).IsSome);" + MakeNode)]
    [InlineData("var h=new Holder(3); WriteLine(h.Slot.AsRef().IsSome); sealed class Holder { public Option<int> Slot; public Holder(int x) { Slot=Some(x); } }")]
    [InlineData("var h=new Holder(3); WriteLine(h.Get().AsRef().IsSome); sealed class Holder { public Option<int> Slot; public Holder(int x) { Slot=Some(x); } public ref readonly Option<int> Get()=>ref Slot; }")]
    [InlineData("Vec<Option<int>> slots=[Some(3)]; WriteLine(slots[0].AsRef().IsSome);")]
    [InlineData("var slot=MakeNode(); WriteLine(IsSet(in slot)); static bool IsSet(in Option<Node> slot)=>slot.AsRef().IsSome;" + MakeNode)]
    [InlineData("var slot=MakeNode(); WriteLine(slot.AsMut().AsRef().IsSome);" + MakeNode)]
    [InlineData("WriteLine(new Counter().AsRef()); sealed class Counter { public int AsRef()=>7; }")]
    [InlineData("var slot=MakeNode(); WriteLine(OptionExtensions.AsRef(in slot).IsSome);" + MakeNode)]
    [InlineData("WriteLine(Vibranium.Nested.OptionExtensions.AsRef(7)); namespace Vibranium { static class Nested { public static class OptionExtensions { public static int AsRef(int x)=>x; } } }")]
    public async Task AcceptsStorageAndUnrelatedMethods(string source)
    {
        string temp = Directory.CreateTempSubdirectory("vibranium-option-storage-").FullName;
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "OptionStorageAccept", includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            Assert.Empty(VibraniumFrontend.CompileAndCheck(compilation).Diagnostics);
            Assert.Empty(await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync());
            var emit = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.True(emit.Success, string.Join("\n", emit.Errors));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public Task SelfAndRoslynRejectTemporaryOption() => Round11AuditAssertions.AssertRejected(
        "WriteLine(MakeNode().AsRef().IsSome);" + MakeNode, "VIB000");

    [Fact]
    public Task LocalOptionExecutesAcrossAllFrontends() => Round11AuditAssertions.AssertDifferential(
        "var slot=MakeNode(); WriteLine(slot.AsRef().IsSome);" + MakeNode, "True\n");
}
