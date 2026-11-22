using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Frontend.Roslyn;

// A name, attribute, generated-file flag, or matching syntax shape is never an
// authority to skip safety checks. Only audited source at its runtime path is.
internal static partial class TrustedRuntimeSources
{
    internal static bool Authenticate(SyntaxTree tree, string? requiredPath = null)
    {
        string path = VibraniumSymbolHelpers.NormalizePathSeparators(tree.FilePath);
        foreach (var entry in SourceHashes)
        {
            if (requiredPath is not null && entry.Key != requiredPath) continue;
            if (path != entry.Key && !path.EndsWith("/" + entry.Key, StringComparison.Ordinal)) continue;
            string text = tree.GetText().ToString().Replace("\r\n", "\n");
            using var algorithm = SHA256.Create();
            string hash = BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
            return string.Equals(hash, entry.Value, StringComparison.Ordinal);
        }
        return false;
    }

    internal static bool IsStd(SyntaxTree tree) =>
        VibraniumSymbolHelpers.NormalizePathSeparators(tree.FilePath).IndexOf("Vibranium.Std/", StringComparison.Ordinal) >= 0 &&
        Authenticate(tree);

    internal static bool IsBootstrap(SyntaxTree tree) =>
        Authenticate(tree, "Vibranium.Core/Bootstrap/VibraniumAppBootstrap.cs");

    internal static bool IsOfficialStdCompilation(CSharpCompilation compilation) =>
        compilation.SyntaxTrees.Any(tree => Authenticate(tree, "Vibranium.Std/Sys/Sys.cs")) &&
        compilation.SyntaxTrees.All(tree => IsStd(tree) ||
            tree.GetRoot() is CompilationUnitSyntax { Members.Count: 0 });
}
