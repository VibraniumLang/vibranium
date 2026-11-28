using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfOptionAsRefTests
{
    private const string MakeNode = "static Option<Node> MakeNode()=>Some(new Node()); sealed class Node{public int Value=7;}";

    [Theory]
    [InlineData("OptionExtensions.AsRef(slot)")]
    [InlineData("Vibranium.OptionExtensions.AsRef(in slot)")]
    [InlineData("global::Vibranium.OptionExtensions.AsRef<Node>(slot: in slot)")]
    public Task StaticBorrowReadsTheStoredPayload(string call) =>
        Round11AuditAssertions.AssertDifferential("var slot=MakeNode();var view=" + call + ";" +
            "WriteLine(view.IsSome);WriteLine(view switch {Some<Node>(var n)=>n.Value,_=>0});" + MakeNode, "True\n7\n");

    [Theory]
    [InlineData("using Helper=Vibranium.OptionExtensions;var slot=MakeNode();WriteLine(Helper.AsRef(slot).IsSome);" + MakeNode)]
    [InlineData("var box=new Box();WriteLine(OptionExtensions.AsRef(box.Slot).IsSome);sealed class Box{ public Option<int> Slot=Some(3); }")]
    [InlineData("var box=new Box();WriteLine(OptionExtensions.AsRef(box.Get()).IsSome);sealed class Box{ public Option<int> Slot=Some(3); public ref readonly Option<int> Get()=>ref Slot; }")]
    [InlineData("Vec<Option<int>> slots=[Some(3)];WriteLine(OptionExtensions.AsRef(slots[0]).IsSome);")]
    [InlineData("WriteLine(OptionExtensions.AsRef(71));static class OptionExtensions{public static int AsRef(int value)=>value;}")]
    public void StaticBorrowPreservesSourceStorageAndUserTypes(string source) => CheckSelf(source, true, null);

    [Theory]
    [InlineData("WriteLine(OptionExtensions.AsRef(MakeNode()).IsSome);" + MakeNode, "requires a source variable")]
    [InlineData("WriteLine(Vibranium.OptionExtensions.AsRef(MakeNode()).IsSome);" + MakeNode, "requires a source variable")]
    [InlineData("WriteLine(MakeNode().AsRef().IsSome);" + MakeNode, "requires a source variable")]
    [InlineData("var box=new Box();WriteLine(OptionExtensions.AsRef(box.Slot).IsSome);sealed class Box{public Option<int> Slot=>Some(3);}", "requires a source variable")]
    [InlineData("WriteLine(OptionExtensions.AsRef<int>(Some(3)).IsSome);", "requires storage of the expected Option<T> type")]
    public void TemporaryOrConvertedOptionIsNotStorage(string source, string reason) => CheckSelf(source, true, reason);

    [Theory]
    [InlineData("var slot=MakeNode();WriteLine(OptionExtensions.AsRef<int>(slot).IsSome);" + MakeNode, "requires storage of the expected Option<T> type")]
    [InlineData("var slot=MakeNode();WriteLine(OptionExtensions.AsRef(wrong:slot).IsSome);" + MakeNode, "Unsupported OptionExtensions member or overload")]
    public void InvalidStaticOverloadStillRejects(string source, string reason) => CheckSelf(source, false, reason);

    private static void CheckSelf(string source, bool validCSharp, string? reason)
    {
        var temp = Directory.CreateTempSubdirectory("vibranium-self-option-asref-");
        try
        {
            string path = Path.Combine(temp.FullName, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "StaticOptionAsRef", includeStd: true);
            Assert.Equal(validCSharp, !compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler, "check", path,
                "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            if (reason is null) Assert.Equal((0, "", ""), self);
            else
            {
                Assert.Equal(1, self.Exit);
                Assert.Contains("error VIB000:", self.Err, StringComparison.Ordinal);
                Assert.Contains(reason, self.Err, StringComparison.Ordinal);
            }
        }
        finally { temp.Delete(recursive: true); }
    }
}
