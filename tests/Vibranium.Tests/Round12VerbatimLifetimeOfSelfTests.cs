using System;
using System.IO;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12VerbatimLifetimeOfSelfTests
{
    [Fact]
    public void ManagedSelfAcceptsVerbatimReturnAttributeTarget()
    {
        const string source = """
            Span<int> s = [4];
            WriteLine(K.First(s, s)[0]);

            static class K
            {
                [@return: LifetimeOf("a")]
                public static ReadOnlySpan<int> First(ReadOnlySpan<int> a, ReadOnlySpan<int> b) => a;
            }
            """;
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-verbatim-return-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var self = Round11AuditAssertions.Run(Round11AuditAssertions.DotnetExe, Round11AuditAssertions.SelfCompiler,
                "run", path, "--sysroot", Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            Assert.Equal(0, self.Exit);
            Assert.Empty(self.Err);
            Assert.Equal("4\n", self.Out.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
