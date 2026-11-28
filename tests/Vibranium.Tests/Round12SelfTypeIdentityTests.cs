using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfTypeIdentityTests
{
    private const string Duplicate = "error VIB000: A type with this name and generic arity is already declared in this scope.";

    [Theory]
    [InlineData("WriteLine(new A().V);\nsealed class A { public int V = 1; }\nsealed class A { public int V = 2; }\n", "", "CS0101", "Program.cs(3,14)")]
    [InlineData("WriteLine(0);namespace N{class A{}}", "namespace N { struct A{} }", "CS0101", "Other.cs(1,22)")]
    [InlineData("WriteLine(0);namespace N.M{class A<T>{}}", "namespace N{namespace M{class A<U>{}}}", "CS0101", "Other.cs(1,31)")]
    [InlineData("WriteLine(0);enum E{A}\nenum E{B}", "", "CS0101", "Program.cs(2,6)")]
    [InlineData("WriteLine(0);class Outer{class A{}\nstruct A{} }", "", "CS0102", "Program.cs(2,8)")]
    [InlineData("WriteLine(0);class Outer{class A<T>{}\nclass A<U>{} }", "", "CS0102", "Program.cs(2,7)")]
    public void RejectsDuplicateAtTheSecondName(string first, string second, string csId, string anchor) =>
        Check(first, second, csId, anchor);

    [Theory]
    [InlineData("WriteLine(0);class A{} class A<T>{} struct A<T,U>{}", "")]
    [InlineData("WriteLine(0);namespace N{ public class A{} }", "namespace M{ public struct A{} }")]
    [InlineData("WriteLine(0);class L{public class A{}}class R{public class A{}}", "")]
    [InlineData("WriteLine(0);class A{} class Outer{public class A{} public class A<T>{}}", "")]
    [InlineData("WriteLine(0);namespace N{public class A{}}", "namespace N{public class B{}}")]
    public void DistinctDeclarationSpacesOrAritiesRemainLegal(string first, string second) => Check(first, second, null, null);

    private static void Check(string first, string second, string? csId, string? anchor)
    {
        var temp = Directory.CreateTempSubdirectory("vibranium-type-identity-");
        try
        {
            string path = Path.Combine(temp.FullName, "Program.cs");
            File.WriteAllText(path, first);
            string[] files = [path];
            if (second.Length != 0)
            {
                string other = Path.Combine(temp.FullName, "Other.cs");
                File.WriteAllText(other, second);
                files = [path, other];
            }
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPaths(files, "TypeIdentity", includeStd: true);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
            if (csId is null) Assert.Empty(errors);
            else Assert.Contains(errors, d => d.Id == csId);

            string manifest = Path.Combine(temp.FullName, "vib.manifest");
            File.WriteAllText(manifest, "unit app\nsource Program.cs\n" + (second.Length == 0 ? "" : "source Other.cs\n"));
            var result = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "check", manifest, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            if (csId is null) Assert.Equal((0, "", ""), result);
            else
            {
                Assert.Equal(1, result.Exit);
                string diagnostic = Assert.Single(result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                Assert.EndsWith(anchor + ": " + Duplicate, diagnostic, StringComparison.Ordinal);
            }
        }
        finally { temp.Delete(recursive: true); }
    }
}
