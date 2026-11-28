using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Mutation;

internal static class Corpus
{
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp15,
        preprocessorSymbols: ["TRACE", "DEBUG", "NET", "NET11_0", "NETCOREAPP", "NET5_0_OR_GREATER", "NET6_0_OR_GREATER", "NET7_0_OR_GREATER",
            "NET8_0_OR_GREATER", "NET9_0_OR_GREATER", "NET10_0_OR_GREATER", "NET11_0_OR_GREATER", "NETCOREAPP1_0_OR_GREATER",
            "NETCOREAPP1_1_OR_GREATER", "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER",
            "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER"]);

    public static IReadOnlyList<SourceProgram> Read(string root, string? probes, int maxBytes)
    {
        var result = new List<SourceProgram>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenProbes = new HashSet<string>(StringComparer.Ordinal);
        void Add(string origin, string group, string source)
        {
            // Tiny files are noise when discovering bases, but a probe (a regression repro such as `break;`) runs as given.
            if ((group != "probes" && source.Length < 20) || source.Trim().Length == 0 || source.Length > maxBytes) return;
            // Probes are deduplicated among themselves only. A regression repro whose text is also a test literal or a
            // base must still run as a probe; deduplicating it against the bases would silently drop it from the gate.
            if (!(group == "probes" ? seenProbes : seen).Add(Files.Hash(source))) return;
            result.Add(new SourceProgram(origin, group, source));
        }
        foreach (string relative in new[] { "samples", "tests/Vibranium.Tests", "src/Vibranium.Std", "src/vib-self", "eng/conformance/corpus" })
        {
            string directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string origin = Files.Relative(root, file);
                if (origin.Split('/').Any(p => p is "bin" or "obj")) continue;
                string source = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(source, ParseOptions);
                var syntax = tree.GetCompilationUnitRoot();
                if (relative.StartsWith("tests/", StringComparison.Ordinal))
                {
                    foreach (var literal in syntax.DescendantNodes().OfType<LiteralExpressionSyntax>())
                    {
                        if (literal.Token.Value is not string value || !value.Contains(';')) continue;
                        int line = tree.GetLineSpan(literal.Span).StartLinePosition.Line + 1;
                        Add(origin + "#L" + line, "tests", value);
                    }
                    continue;
                }
                string group = relative switch
                {
                    "src/Vibranium.Std" => "stdlib",
                    "src/vib-self" => "self",
                    "eng/conformance/corpus" => "conformance",
                    _ => "samples",
                };
                Add(origin, group, source);
                if (group is not ("stdlib" or "self")) continue;
                // These explicitly labelled projections retain real method syntax, not a new grammar.
                // SDK-invalid projections (missing surrounding context) never enter the mutation campaign.
                foreach (var method in syntax.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(SyntaxKind.StaticKeyword) || method.Body is null && method.ExpressionBody is null) continue;
                    string projected = "return 0;\nstatic class CorpusSlice\n{\n" + method.WithoutLeadingTrivia().ToFullString() + "\n}\n";
                    int line = tree.GetLineSpan(method.Span).StartLinePosition.Line + 1;
                    Add(origin + "#method-L" + line, group, projected);
                }
                // Whole top-level type declarations (struct/class/enum) as their own program, so that field,
                // constructor, property and readonly-member mutations reach real library type shapes.
                foreach (var type in syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Where(t => t.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax))
                {
                    string projected = "return 0;\n" + type.WithoutLeadingTrivia().ToFullString() + "\n";
                    int line = tree.GetLineSpan(type.Span).StartLinePosition.Line + 1;
                    Add(origin + "#type-L" + line, group, projected);
                }
            }
        }
        // Several probe directories may be given, separated by the platform path separator.
        foreach (string directory in (probes ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Probe directory not found: " + directory);
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            string full = Path.GetFullPath(directory);
            bool inRepository = full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs").Order(StringComparer.Ordinal))
                Add(inRepository ? Files.Relative(root, file) : "external-probes/" + name + "/" + Path.GetFileName(file), "probes", File.ReadAllText(file));
        }
        return result;
    }
}
