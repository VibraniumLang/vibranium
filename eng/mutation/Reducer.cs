using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Mutation;

internal static class Reducer
{
    public static (string Source, int Attempts) Minimize(string source, string id, string selfStatus, string errorKind, string[] cuts,
        Oracles oracles, string scratchFile, int budget)
    {
        int attempts = 0;
        // Keep an entry point when the original has one, so that the repro stays a whole program.
        bool needsEntry = CSharpSyntaxTree.ParseText(source, Corpus.ParseOptions).GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any();
        bool Preserve(string candidate)
        {
            attempts++;
            if (needsEntry && !CSharpSyntaxTree.ParseText(candidate, Corpus.ParseOptions).GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any()) return false;
            var sdk = oracles.Sdk(candidate);
            // A reduction may drop a cut form but never add one, so a cut-free repro shows that the ID is
            // reachable without any C10-C20 construct.
            // The error kind is kept too, so a defect (C#-invalid input) never reduces to warnings-only input.
            if (sdk.Errors.Length == 0 || Classification.Bucket(sdk.Errors[0]) != id || Classification.ErrorKind(sdk.Errors) != errorKind
                || Classification.Cuts(sdk).Except(cuts, StringComparer.Ordinal).Any()) return false;
            File.WriteAllText(scratchFile, candidate);
            return oracles.Self(scratchFile).Status == selfStatus;
        }
        // Syntax-aware deletion is ddmin over declarations and statements. Reparse after every
        // accepted deletion, and then try individual statements. It is budgeted, not globally minimal.
        int partitions = 2;
        while (attempts < budget)
        {
            var root = CSharpSyntaxTree.ParseText(source, Corpus.ParseOptions).GetRoot();
            var nodes = root.DescendantNodes().Where(n => n is MemberDeclarationSyntax or StatementSyntax)
                .Where(n => n is not (GlobalStatementSyntax or BlockSyntax or BaseNamespaceDeclarationSyntax))
                .OrderBy(n => n.SpanStart).ThenByDescending(n => n.Span.Length).ToArray();
            if (nodes.Length == 0) break;
            bool changed = false;
            int chunk = Math.Max(1, (nodes.Length + partitions - 1) / partitions);
            for (int start = 0; start < nodes.Length && attempts < budget; start += chunk)
            {
                var selected = nodes.Skip(start).Take(chunk).ToArray();
                var spans = selected.Where(n => !selected.Any(other => other != n && other.Span.Contains(n.Span)))
                    .Select(n => n.Span).OrderByDescending(s => s.Start).ToArray();
                string candidate = source;
                foreach (var span in spans) candidate = candidate.Remove(span.Start, span.Length);
                if (candidate.Length > 0 && Preserve(candidate))
                {
                    source = candidate;
                    partitions = Math.Max(2, partitions - 1);
                    changed = true;
                    break;
                }
            }
            if (changed) continue;
            if (chunk == 1) break;
            partitions = Math.Min(nodes.Length, partitions * 2);
        }
        return (source, attempts);
    }
}
