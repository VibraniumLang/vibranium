using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vibranium.Compiler.Core;
using Vibranium.Frontend.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Vibranium.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VibraniumDiagnosticAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UnsupportedRule = CreateRule(
        VibraniumDiagnosticIds.Unsupported,
        "Unsupported C# construct in Vibranium",
        "Vibranium.Language");

    public static readonly DiagnosticDescriptor UseAfterMoveRule = CreateRule(
        VibraniumDiagnosticIds.UseAfterMove,
        "Use of moved or uninitialized value",
        "Vibranium.Ownership");

    public static readonly DiagnosticDescriptor OverlappingCallBorrowsRule = CreateRule(
        VibraniumDiagnosticIds.OverlappingCallBorrows,
        "Overlapping borrows in a single call",
        "Vibranium.Borrowing");

    public static readonly DiagnosticDescriptor ConflictWithActiveBorrowRule = CreateRule(
        VibraniumDiagnosticIds.ConflictWithActiveBorrow,
        "Mutation, move, or scope exit conflicts with active borrow",
        "Vibranium.Borrowing");

    public static readonly DiagnosticDescriptor MutateThroughSharedBorrowRule = CreateRule(
        VibraniumDiagnosticIds.MutateThroughSharedBorrow,
        "Mutation through shared borrow",
        "Vibranium.Borrowing");

    public static readonly DiagnosticDescriptor IllegalFieldOrElementMoveRule = CreateRule(
        VibraniumDiagnosticIds.IllegalFieldOrElementMove,
        "Illegal move out of field, indexed element, or borrowed place",
        "Vibranium.Ownership");

    public static readonly DiagnosticDescriptor LinearResourceViolationRule = CreateRule(
        VibraniumDiagnosticIds.LinearResourceViolation,
        "Linear IDisposable resource violation",
        "Vibranium.Resources");

    public static readonly DiagnosticDescriptor EscapingBorrowRule = CreateRule(
        VibraniumDiagnosticIds.EscapingBorrow,
        "Returned reference or view escapes local or by-value owner",
        "Vibranium.Borrowing");

    public static readonly DiagnosticDescriptor NullOrZeroUnsafeRule = CreateRule(
        VibraniumDiagnosticIds.NullOrZeroUnsafe,
        "Forbidden null, nullable, or non-zero-safe initialization",
        "Vibranium.Safety");

    private static readonly Dictionary<string, DiagnosticDescriptor> RulesById = new(StringComparer.Ordinal)
    {
        [VibraniumDiagnosticIds.Unsupported] = UnsupportedRule,
        [VibraniumDiagnosticIds.UseAfterMove] = UseAfterMoveRule,
        [VibraniumDiagnosticIds.OverlappingCallBorrows] = OverlappingCallBorrowsRule,
        [VibraniumDiagnosticIds.ConflictWithActiveBorrow] = ConflictWithActiveBorrowRule,
        [VibraniumDiagnosticIds.MutateThroughSharedBorrow] = MutateThroughSharedBorrowRule,
        [VibraniumDiagnosticIds.IllegalFieldOrElementMove] = IllegalFieldOrElementMoveRule,
        [VibraniumDiagnosticIds.LinearResourceViolation] = LinearResourceViolationRule,
        [VibraniumDiagnosticIds.EscapingBorrow] = EscapingBorrowRule,
        [VibraniumDiagnosticIds.NullOrZeroUnsafe] = NullOrZeroUnsafeRule,
    };

    private static DiagnosticDescriptor CreateRule(string id, string title, string category) =>
        new(
            id: id,
            title: title,
            messageFormat: "{0}",
            category: category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            // VIB diagnostics are mandatory compiler boundaries surfaced through
            // an analyzer. NotConfigurable blocks pragma/editorconfig/NoWarn;
            // Compiler also blocks SuppressMessageAttribute source suppression.
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable, WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            UnsupportedRule,
            UseAfterMoveRule,
            OverlappingCallBorrowsRule,
            ConflictWithActiveBorrowRule,
            MutateThroughSharedBorrowRule,
            IllegalFieldOrElementMoveRule,
            LinearResourceViolationRule,
            EscapingBorrowRule,
            NullOrZeroUnsafeRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        if (context.Compilation is not CSharpCompilation csharpCompilation)
        {
            return;
        }

        context.CancellationToken.ThrowIfCancellationRequested();

        VibraniumCheckResult result;
        if (VibraniumFrontend.HasRegisteredTrustedTrees(csharpCompilation))
        {
            result = VibraniumFrontend.CompileAndCheck(csharpCompilation);
        }
        else
        {
            var trustedManagedAdapterTrees = new HashSet<SyntaxTree>();
            var trustedSysCatalogTrees = new HashSet<SyntaxTree>();
            if (string.Equals(csharpCompilation.AssemblyName, "Vibranium.Std", StringComparison.Ordinal))
            {
                foreach (var tree in csharpCompilation.SyntaxTrees)
                {
                    if (VibraniumFrontend.IsOfficialStdSysManagedTreeStructure(tree))
                    {
                        trustedManagedAdapterTrees.Add(tree);
                    }
                    else if (VibraniumFrontend.IsOfficialStdSysCatalogTreeStructure(tree))
                    {
                        trustedSysCatalogTrees.Add(tree);
                    }
                }
            }
            result = VibraniumFrontend.CompileAndCheck(
                csharpCompilation,
                trustedManagedAdapterTrees,
                trustedSysCatalogTrees,
                officialStdTrees: null);
        }

        foreach (var diag in result.Diagnostics)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!RulesById.TryGetValue(diag.Id, out var descriptor))
            {
                descriptor = UnsupportedRule;
            }

            var location = ResolveLocation(csharpCompilation, diag.Span);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, diag.Message));
        }
    }

    private static Location ResolveLocation(CSharpCompilation compilation, VibraniumSpan span)
    {
        SyntaxTree? matchedTree = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (string.Equals(tree.FilePath ?? string.Empty, span.FilePath ?? string.Empty, StringComparison.Ordinal))
            {
                if (span.Start >= 0 && span.Start + span.Length <= tree.Length)
                {
                    matchedTree = tree;
                    break;
                }
                matchedTree ??= tree;
            }
        }

        if (matchedTree is null)
        {
            return Location.None;
        }

        int treeLen = matchedTree.Length;
        int start = Math.Max(0, Math.Min(span.Start, treeLen));
        int length = Math.Max(0, Math.Min(span.Length, treeLen - start));
        return Location.Create(matchedTree, new TextSpan(start, length));
    }
}
