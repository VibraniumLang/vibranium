using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Vibranium.Compiler.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class SyntaxAndSymbolGatekeeper(
    CSharpCompilation compilation,
    MirModule module,
    List<VibraniumDiagnostic> diagnostics,
    ISet<SyntaxTree>? trustedManagedAdapterTrees = null,
    ISet<SyntaxTree>? trustedSysCatalogTrees = null,
    ISet<SyntaxTree>? officialStdTrees = null,
    bool isBuildingVibraniumStdItself = false,
    SubsetDeclarationCuts? declarationCuts = null)
{
    private static readonly string[] s_promotedSemanticWarnings =
        ["CS0183", "CS0184", "CS8509", "CS8519", "CS8520", "CS8524", "CS8778", "CS8793", "CS8794", "CS8846", "CS9191", "CS9192", "CS9193"];

    internal static bool IsPromotedSemanticWarning(string id) => s_promotedSemanticWarnings.Contains(id);
    private Dictionary<SyntaxTree, List<Diagnostic>>? _semanticWarningsByTree;
    private readonly CSharpCompilation _compilation = compilation;
    private readonly MirModule _module = module;
    private readonly List<VibraniumDiagnostic> _diagnostics = diagnostics;
    private readonly ISet<SyntaxTree>? _trustedManagedAdapterTrees = trustedManagedAdapterTrees;
    private readonly ISet<SyntaxTree>? _trustedSysCatalogTrees = trustedSysCatalogTrees;
    private readonly ISet<SyntaxTree>? _officialStdTrees = officialStdTrees;
    private readonly bool _isBuildingVibraniumStdItself = isBuildingVibraniumStdItself;
    private bool _checkingNonStdUserTree;
    private readonly HashSet<INamedTypeSymbol> _rejectedDisposableStructs = new(SymbolEqualityComparer.Default);
    private readonly HashSet<SyntaxNode> _rejectedGenericPatternExpressions = new();
    private readonly HashSet<IMethodSymbol> _rejectedGenericPatternMethods = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _rejectedGenericPatternInitializerTypes = new(SymbolEqualityComparer.Default);
    internal const string GenericPatternBoundaryMessage = "Patterns other than var and discard on a value of generic type parameter type are not supported in Vibranium v0.2.";

    public bool IsRejectedGenericPatternBody(IMethodSymbol method) =>
        declarationCuts?.Rejects(method) == true ||
        method.ContainingType is not null && declarationCuts?.Rejects(method.ContainingType) == true ||
        _rejectedGenericPatternMethods.Contains(method.OriginalDefinition) ||
        method.MethodKind == MethodKind.Constructor &&
        method.ContainingType is not null && _rejectedGenericPatternInitializerTypes.Contains(method.ContainingType.OriginalDefinition);


    private bool HasTrustedStdBoundary =>
        !_isBuildingVibraniumStdItself &&
        ((_officialStdTrees is { Count: > 0 }) ||
         (_trustedSysCatalogTrees is { Count: > 0 }) ||
         (_trustedManagedAdapterTrees is { Count: > 0 }));

    private bool IsTrustedStdTree(SyntaxTree? tree) =>
        tree is not null &&
        ((_officialStdTrees?.Contains(tree) == true) ||
         (_trustedSysCatalogTrees?.Contains(tree) == true) ||
         VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, _trustedManagedAdapterTrees));

    public void CheckTree(SyntaxTree tree, SemanticModel model)
    {
        if (declarationCuts?.Rejects(tree.GetRoot()) == true ||
            VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, _trustedManagedAdapterTrees))
        {
            return;
        }

        bool prevNonStd = _checkingNonStdUserTree;
        _checkingNonStdUserTree = HasTrustedStdBoundary && !IsTrustedStdTree(tree);
        try
        {
            var root = tree.GetRoot();
            CheckOpenGenericPatterns(root, model);
            // A canonical warning boundary takes precedence over another VIB000
            // at the same expression (e.g. `intValue is string` also trips the
            // closed string-operator gate). Diagnostics deduplicate by ID/span.
            CheckPromotedSemanticWarnings(root, model);
            // A varargs declaration is a token, not an expression. Visit both
            // in source order, including directive roots, to catch every role.
            var reportedKinds = new HashSet<SyntaxKind>();
            foreach (var element in root.DescendantNodesAndTokensAndSelf(descendIntoTrivia: true))
            {
                if (element.IsToken)
                {
                    var token = element.AsToken();
                    // Documentation and directive payload tokens don't form C# code.
                    if (!token.IsMissing && !token.IsPartOfStructuredTrivia() && RejectsSyntaxKind(token.Kind()) &&
                        !IsSkippedSyntax(token.Parent!, model))
                    {
                        ReportUnsupportedKind(token.Kind(), token.GetLocation(), reportedKinds);
                    }
                    continue;
                }

                var node = element.AsNode()!;
                bool isDirective = node is DirectiveTriviaSyntax;
                if ((!isDirective && node.IsPartOfStructuredTrivia()) || IsSkippedSyntax(node, model)) continue;
                if (RejectsSyntaxKind(node.Kind()))
                {
                    var location = node is LabeledStatementSyntax label ? label.Identifier.GetLocation() : node.GetLocation();
                    ReportUnsupportedKind(node.Kind(), location, reportedKinds);
                }
                else if (!isDirective)
                {
                    CheckSyntaxNode(node, model);
                }
            }
            CheckTreeOperations(root, model);
        }
        finally
        {
            _checkingNonStdUserTree = prevNonStd;
        }
    }

    private bool IsSkippedSyntax(SyntaxNode node, SemanticModel model) =>
        declarationCuts?.Rejects(node) == true || IsInsideEmbeddedAttributeType(node, model) || IsInsideRejectedGenericPattern(node);

    private void ReportUnsupportedKind(SyntaxKind kind, Location location, HashSet<SyntaxKind> reportedKinds)
    {
        if (!reportedKinds.Add(kind)) return;
        Emit(VibraniumDiagnosticIds.Unsupported, GetRejectionMessage(kind), VibraniumSymbolHelpers.ToVibraniumSpan(location));
    }

    private static string GetRejectionMessage(SyntaxKind kind) => kind switch
    {
        SyntaxKind.ArgListKeyword or SyntaxKind.MakeRefKeyword or SyntaxKind.RefTypeKeyword or SyntaxKind.RefValueKeyword =>
            $"'{SyntaxFacts.GetText(kind)}' is not supported in Vibranium.",
        SyntaxKind.VolatileKeyword => "'volatile' fields are not supported in Vibranium v0.2; use an ordinary field.",
        SyntaxKind.RequiredKeyword => "'required' members are not supported in Vibranium v0.2; initialize members in a constructor.",
        SyntaxKind.AsExpression => "'as' is not supported in Vibranium v0.2; use a type pattern ('x is C c').",
        SyntaxKind.ListPattern or SyntaxKind.SlicePattern =>
            SubsetDeclarationCuts.ListPatternMessage,
        SyntaxKind.LabeledStatement => "Labeled statements are not supported in Vibranium v0.2 ('goto' is not supported).",
        SyntaxKind.SpreadElement => "Spread elements are not supported in Vibranium v0.2; add each element explicitly.",
        SyntaxKind.TypeOfExpression => "'typeof(...)' is not supported in Vibranium v0.2.",
        _ => $"Unsupported construct '{kind}' in Vibranium v0.2.",
    };

    private bool IsInsideRejectedGenericPattern(SyntaxNode node) =>
        _rejectedGenericPatternExpressions.Count > 0 && node.AncestorsAndSelf().Any(_rejectedGenericPatternExpressions.Contains);

    private void CheckOpenGenericPatterns(SyntaxNode root, SemanticModel model)
    {
        foreach (var node in root.DescendantNodes())
        {
            ExpressionSyntax? governing = null;
            bool reject = false;
            switch (node)
            {
                case IsPatternExpressionSyntax isPattern:
                    governing = isPattern.Expression;
                    reject = HasOpenGenericPattern(isPattern.Pattern, model);
                    break;
                case BinaryExpressionSyntax isType when isType.IsKind(SyntaxKind.IsExpression):
                    governing = isType.Left;
                    reject = model.GetTypeInfo(governing).Type is ITypeParameterSymbol;
                    break;
                case SwitchExpressionSyntax switchExpression:
                    governing = switchExpression.GoverningExpression;
                    reject = switchExpression.Arms.Any(arm => HasOpenGenericPattern(arm.Pattern, model));
                    break;
                case SwitchStatementSyntax switchStatement:
                    governing = switchStatement.Expression;
                    bool openInput = model.GetTypeInfo(governing).Type is ITypeParameterSymbol;
                    reject = switchStatement.Sections.SelectMany(section => section.Labels).Any(label => label switch
                    {
                        CaseSwitchLabelSyntax => openInput,
                        CasePatternSwitchLabelSyntax patternLabel => HasOpenGenericPattern(patternLabel.Pattern, model),
                        _ => false,
                    });
                    break;
            }
            if (!reject || governing is null || !_rejectedGenericPatternExpressions.Add(node)) continue;

            Emit(VibraniumDiagnosticIds.Unsupported, GenericPatternBoundaryMessage,
                VibraniumSymbolHelpers.ToVibraniumSpan(governing));
            var owner = model.GetEnclosingSymbol(governing.SpanStart);
            if (owner is IMethodSymbol method) _rejectedGenericPatternMethods.Add(method.OriginalDefinition);
            else if (owner is IFieldSymbol or IPropertySymbol && owner.ContainingType is { } type)
                _rejectedGenericPatternInitializerTypes.Add(type.OriginalDefinition);
        }
    }

    private static bool HasOpenGenericPattern(PatternSyntax pattern, SemanticModel model)
    {
        if (model.GetOperation(pattern) is not IPatternOperation operation) return false;
        foreach (var part in operation.DescendantsAndSelf().OfType<IPatternOperation>())
        {
            if (part.InputType is not ITypeParameterSymbol) continue;
            SyntaxNode syntax = part.Syntax;
            while (syntax is ParenthesizedPatternSyntax parentheses) syntax = parentheses.Pattern;
            if (syntax is not (VarPatternSyntax or DiscardPatternSyntax)) return true;
        }
        return false;
    }

    private void CheckPromotedSemanticWarnings(SyntaxNode root, SemanticModel model)
    {
        if (!root.DescendantNodesAndSelf().Any(node => node is IsPatternExpressionSyntax or CastExpressionSyntax or SwitchExpressionSyntax or ArgumentSyntax ||
            node.IsKind(SyntaxKind.IsExpression)))
        {
            return;
        }
        if (_semanticWarningsByTree is null)
        {
            var options = _compilation.Options;
            var specific = options.SpecificDiagnosticOptions;
            foreach (string id in s_promotedSemanticWarnings) specific = specific.SetItem(id, ReportDiagnostic.Warn);
            // These are source-language boundaries, not configurable warnings.
            // Reporting suppressed diagnostics preserves the same bound program
            // while recovering #pragma-disabled warnings without rewriting syntax.
            var warningCompilation = _compilation.WithOptions(options
                .WithSpecificDiagnosticOptions(specific)
                .WithGeneralDiagnosticOption(ReportDiagnostic.Default)
                .WithWarningLevel(9999)
                .WithSyntaxTreeOptionsProvider(new SemanticWarningOptions(options.SyntaxTreeOptionsProvider))
                .WithReportSuppressedDiagnostics(true));
            _semanticWarningsByTree = new Dictionary<SyntaxTree, List<Diagnostic>>();
            // Ask once for the complete diagnostic set and retain only the
            // selected source warnings, grouped by their original tree.
            foreach (var diagnostic in warningCompilation.GetDiagnostics())
            {
                if (diagnostic.Location.SourceTree is not { } tree || !s_promotedSemanticWarnings.Contains(diagnostic.Id)) continue;
                if (!_semanticWarningsByTree.TryGetValue(tree, out var warnings))
                {
                    warnings = new List<Diagnostic>();
                    _semanticWarningsByTree.Add(tree, warnings);
                }
                warnings.Add(diagnostic);
            }
        }
        if (!_semanticWarningsByTree.TryGetValue(model.SyntaxTree, out var treeWarnings)) return;
        foreach (var diagnostic in treeWarnings)
        {
            if (_rejectedGenericPatternExpressions.Any(expression => expression.SyntaxTree == model.SyntaxTree &&
                expression.Span.Contains(diagnostic.Location.SourceSpan))) continue;
            Emit(VibraniumDiagnosticIds.Unsupported,
                $"C# warning {diagnostic.Id} is not permitted in Vibranium: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}",
                VibraniumSymbolHelpers.ToVibraniumSpan(diagnostic.Location));
        }
    }

    // Editorconfig's per-tree/global severity provider can override compilation
    // warning options. Ignore only the promoted warnings in that provider while
    // preserving generated classification and every unrelated setting.
    private sealed class SemanticWarningOptions(SyntaxTreeOptionsProvider? original) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) =>
            original?.IsGenerated(tree, cancellationToken) ?? GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Warn;
            return IsPromotedSemanticWarning(diagnosticId) ||
                original is not null && original.TryGetDiagnosticValue(tree, diagnosticId, cancellationToken, out severity);
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Warn;
            return IsPromotedSemanticWarning(diagnosticId) ||
                original is not null && original.TryGetGlobalDiagnosticValue(diagnosticId, cancellationToken, out severity);
        }
    }

    public void CheckDeclaredTypeSymbol(INamedTypeSymbol typeSymbol)
    {
        if (VibraniumSymbolHelpers.IsDeclaredOnlyInTrustedManagedAdapter(typeSymbol, _trustedManagedAdapterTrees))
        {
            return;
        }

        if (HasTrustedStdBoundary &&
            typeSymbol.DeclaringSyntaxReferences.Any(sr => IsTrustedStdTree(sr.SyntaxTree)) &&
            typeSymbol.DeclaringSyntaxReferences.Any(sr => !IsTrustedStdTree(sr.SyntaxTree)))
        {
            var spoofRef = typeSymbol.DeclaringSyntaxReferences.First(sr => !IsTrustedStdTree(sr.SyntaxTree));
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Cannot reopen 'Vibranium.Std' type '{typeSymbol.Name}' as a partial declaration in user code.",
                VibraniumSymbolHelpers.ToVibraniumSpan(spoofRef.GetSyntax()));
            return;
        }

        var location = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource && !VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(l.SourceTree, _trustedManagedAdapterTrees));
        var typeSpan = location is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(location) : new VibraniumSpan(string.Empty, 0, 0, 1, 1);

        if (typeSymbol.TypeKind is TypeKind.Interface or TypeKind.Delegate)
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"User-defined {typeSymbol.TypeKind.ToString().ToLowerInvariant()} '{typeSymbol.Name}' is not supported in Vibranium.", typeSpan);
            return;
        }

        if (typeSymbol.TypeKind == TypeKind.Struct && typeSymbol.IsRefLikeType)
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"User-defined ref struct '{typeSymbol.Name}' is not supported in Vibranium.", typeSpan);
        }

        if (typeSymbol.TypeKind == TypeKind.Enum &&
            typeSymbol.EnumUnderlyingType is not null &&
            typeSymbol.EnumUnderlyingType.SpecialType != SpecialType.System_Int32)
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Enum '{typeSymbol.Name}' must use default 'int' underlying type in Vibranium.", typeSpan);
        }

        foreach (var attr in typeSymbol.GetAttributes())
        {
            string? attrName = attr.AttributeClass?.Name;
            string? attrNs = attr.AttributeClass?.ContainingNamespace?.ToDisplayString();
            if (attrName is "UnionAttribute" or "InlineArrayAttribute")
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Attribute '[{attrName}]' on user-defined type '{typeSymbol.Name}' is not supported in Vibranium.", typeSpan);
            }
            else if (attrName == "NativeAttribute" && attrNs == "Vibranium")
            {
                Emit(VibraniumDiagnosticIds.Unsupported, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods.", typeSpan);
            }
            else if (attrName == "LifetimeOfAttribute" && attrNs == "Vibranium")
            {
                Emit(VibraniumDiagnosticIds.Unsupported, "'[LifetimeOf]' is only supported as '[return: LifetimeOf(...)]' on methods in Vibranium.", typeSpan);
            }
            else if (attrName == "StructLayoutAttribute" &&
                     attr.ConstructorArguments.Length > 0 &&
                     attr.ConstructorArguments[0].Value is int layoutKind &&
                     layoutKind == 2) // LayoutKind.Explicit
            {
                Emit(VibraniumDiagnosticIds.Unsupported, "'[StructLayout(LayoutKind.Explicit)]' is banned in Vibranium.", typeSpan);
            }
        }

        CheckTypeParameters(typeSymbol.TypeParameters, typeSpan);

        if (typeSymbol.TypeKind == TypeKind.Class)
        {
            if (typeSymbol.BaseType is not null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Class inheritance (': {typeSymbol.BaseType.Name}') is banned in Vibranium.", typeSpan);
            }

            if (typeSymbol.IsAbstract && !typeSymbol.IsStatic)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Abstract class '{typeSymbol.Name}' is banned in Vibranium.", typeSpan);
            }
        }

        foreach (var iface in typeSymbol.Interfaces)
        {
            if (iface.SpecialType != SpecialType.System_IDisposable &&
                iface.ToDisplayString() != "System.IDisposable")
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Interface implementation '{iface.Name}' is not supported in Vibranium (only System.IDisposable on classes is supported).", typeSpan);
            }
            else if (typeSymbol.TypeKind == TypeKind.Struct)
            {
                _rejectedDisposableStructs.Add(typeSymbol.OriginalDefinition);
                Emit(VibraniumDiagnosticIds.Unsupported, "IDisposable linear resources must be classes in Vibranium.", typeSpan);
            }
            else if (typeSymbol.TypeKind == TypeKind.Class)
            {
                bool hasPublicDispose = typeSymbol.GetMembers("Dispose")
                    .OfType<IMethodSymbol>()
                    .Any(m =>
                        !m.IsStatic &&
                        m.MethodKind == MethodKind.Ordinary &&
                        m.DeclaredAccessibility == Accessibility.Public &&
                        m.Parameters.Length == 0 &&
                        m.TypeParameters.Length == 0 &&
                        m.ReturnsVoid);
                bool hasExplicitDisposeInSource = typeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Any(m => m.MethodKind == MethodKind.ExplicitInterfaceImplementation &&
                              m.Locations.Any(l => l.IsInSource));
                if (!hasPublicDispose && !hasExplicitDisposeInSource)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                        typeSpan);
                }
            }
        }

        foreach (var capParam in VibraniumSymbolHelpers.GetCapturedPrimaryConstructorParameters(typeSymbol, _compilation))
        {
            var cpLoc = capParam.Locations.FirstOrDefault(l => l.IsInSource);
            var cpSpan = cpLoc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(cpLoc) : typeSpan;
            if (VibraniumSymbolHelpers.IsLinearResourceSymbol(capParam.Type))
            {
                Emit(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    $"Linear IDisposable resource '{capParam.Type.Name}' cannot be captured by primary constructor parameter '{capParam.Name}'.",
                    cpSpan);
            }
        }

        bool hasExplicitInstanceCtor = typeSymbol.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared);

        foreach (var member in typeSymbol.GetMembers())
        {
            if (VibraniumSymbolHelpers.IsDeclaredOnlyInTrustedManagedAdapter(member, _trustedManagedAdapterTrees))
            {
                continue;
            }

            var memberLoc = member.Locations.FirstOrDefault(l => l.IsInSource && !VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(l.SourceTree, _trustedManagedAdapterTrees));
            var memberSpan = memberLoc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(memberLoc) : typeSpan;

            if (!member.IsImplicitlyDeclared)
            {
                if (member.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Protected member '{member.Name}' is banned in Vibranium.", memberSpan);
                }

                if (member.IsAbstract || member.IsVirtual || member.IsOverride)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Virtual, abstract, and override members ('{member.Name}') are banned in Vibranium.", memberSpan);
                }
            }

            if (member is IEventSymbol evtSym && !evtSym.IsImplicitlyDeclared)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Event '{evtSym.Name}' is banned in Vibranium.", memberSpan);
            }

            if (member is IFieldSymbol staticField && staticField.IsStatic && !staticField.IsConst && typeSymbol.TypeKind != TypeKind.Enum)
            {
                var sfLoc = (staticField.AssociatedSymbol ?? staticField).Locations.FirstOrDefault(l => l.IsInSource);
                var sfSpan = sfLoc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(sfLoc) : memberSpan;
                Emit(VibraniumDiagnosticIds.Unsupported, $"Non-const static field or auto-property '{staticField.AssociatedSymbol?.Name ?? staticField.Name}' is banned in Vibranium.", sfSpan);
            }

            if (member is IMethodSymbol methodSym && !methodSym.IsImplicitlyDeclared)
            {
                if (methodSym.MethodKind == MethodKind.StaticConstructor)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Static constructor on '{typeSymbol.Name}' is banned in Vibranium.", memberSpan);
                }
                else if (methodSym.MethodKind == MethodKind.Destructor)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Finalizer '~{typeSymbol.Name}()' is banned in Vibranium.", memberSpan);
                }

                string? nativeSym = VibraniumSymbolHelpers.GetNativeSymbol(methodSym);
                if (nativeSym is not null && !VibraniumSymbolHelpers.TryValidateSysNativeMethod(methodSym, nativeSym, _compilation, _trustedSysCatalogTrees))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"'[Native(\"{nativeSym}\")]' on '{typeSymbol.Name}.{methodSym.Name}' is not in the trusted Vibranium.Std.Sys catalog.",
                        memberSpan);
                }

                if (methodSym.IsExtern || VibraniumSymbolHelpers.HasInteropImportAttribute(methodSym))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"External or P/Invoke method '{typeSymbol.Name}.{methodSym.Name}' ('extern' / '[DllImport]' / '[LibraryImport]') is banned in Vibranium.",
                        memberSpan);
                }
                else if (methodSym.IsPartialDefinition && methodSym.PartialImplementationPart is null && nativeSym is null)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Unimplemented partial method '{typeSymbol.Name}.{methodSym.Name}' is banned in Vibranium.",
                        memberSpan);
                }

                if (methodSym.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion &&
                    !VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(methodSym))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"User-defined operator or conversion '{methodSym.Name}' on '{typeSymbol.Name}' is not supported in Vibranium; only instance 'void operator +=(T)' is supported.",
                        memberSpan);
                }

                if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(methodSym.ReturnType))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Standalone '{methodSym.ReturnType.Name}' return types are not supported in Vibranium.", memberSpan);
                }
                else if (IsBannedDelegateOrInterfaceOrObjectType(methodSym.ReturnType))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Return type '{methodSym.ReturnType.ToDisplayString()}' is banned in Vibranium.", memberSpan);
                }

                CheckTypeParameters(methodSym.TypeParameters, memberSpan);
                CheckParameters(methodSym.Parameters, memberSpan);
            }

            if (member is IPropertySymbol propSym && !propSym.IsImplicitlyDeclared)
            {
                if (VibraniumSymbolHelpers.IsAutoOrInitProperty(propSym))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Auto-property or 'init' property '{typeSymbol.Name}.{propSym.Name}' is banned in Vibranium; declare an explicit backing field and accessor bodies.",
                        memberSpan);
                }
                if (VibraniumSymbolHelpers.GetNativeSymbol(propSym) is not null)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods.", memberSpan);
                }
                if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(propSym.Type))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Closed UTF-8 foreach view '{propSym.Type.Name}' cannot be exposed as a property or indexer; use a method instead.", memberSpan);
                }
                else if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(propSym.Type))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Standalone '{propSym.Type.Name}' properties are not supported in Vibranium.", memberSpan);
                }
                else if (IsBannedDelegateOrInterfaceOrObjectType(propSym.Type))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Property type '{propSym.Type.ToDisplayString()}' is banned in Vibranium.", memberSpan);
                }
                CheckParameters(propSym.Parameters, memberSpan);
            }

            if (member is IFieldSymbol field && !field.IsImplicitlyDeclared && typeSymbol.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                if (IsBannedDelegateOrInterfaceOrObjectType(field.Type))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Field '{field.Name}' of type '{field.Type.ToDisplayString()}' is banned in Vibranium.", memberSpan);
                }
                foreach (var fAttr in field.GetAttributes())
                {
                    if (fAttr.AttributeClass?.Name == "FieldOffsetAttribute")
                    {
                        Emit(VibraniumDiagnosticIds.Unsupported, "'[FieldOffset]' is banned in Vibranium.", memberSpan);
                    }
                    else if (fAttr.AttributeClass?.Name == "NativeAttribute")
                    {
                        Emit(VibraniumDiagnosticIds.Unsupported, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods.", memberSpan);
                    }
                }

                if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(field.Type))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Closed UTF-8 foreach view '{field.Type.Name}' cannot be stored in field '{field.Name}'.",
                        memberSpan);
                }
                else if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(field.Type))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Standalone '{field.Type.Name}' cannot be stored in field '{field.Name}'.",
                        memberSpan);
                }

                if (VibraniumSymbolHelpers.IsLinearResourceSymbol(field.Type))
                {
                    Emit(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"Linear IDisposable resource '{field.Type.Name}' cannot be stored in field '{field.Name}'.",
                        memberSpan);
                }

                if (!field.IsStatic && !hasExplicitInstanceCtor && typeSymbol.TypeKind == TypeKind.Class)
                {
                    bool hasFieldInit = HasFieldInitializer(field);
                    if (!hasFieldInit)
                    {
                        if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(field.Type, _compilation))
                        {
                            Emit(
                                VibraniumDiagnosticIds.NullOrZeroUnsafe,
                                $"Field '{field.Name}' of non-zero-safe type '{field.Type.ToDisplayString()}' must be initialized.",
                                memberSpan);
                        }
                    }
                }
            }
        }
    }

    private void CheckTypeParameters(ImmutableArray<ITypeParameterSymbol> typeParameters, VibraniumSpan fallbackSpan)
    {
        foreach (var tp in typeParameters)
        {
            var tpLoc = tp.Locations.FirstOrDefault(l => l.IsInSource);
            var tpSpan = tpLoc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(tpLoc) : fallbackSpan;

            if (tp.AllowsRefLikeType)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Type parameter '{tp.Name}' cannot use 'allows ref struct' in Vibranium.", tpSpan);
            }

            if (tp.HasConstructorConstraint)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Type parameter '{tp.Name}' cannot use 'new()' constraint in Vibranium.", tpSpan);
            }

            if (tp.ConstraintTypes.Length > 0)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Type parameter '{tp.Name}' cannot use interface or base-class constraints in Vibranium.", tpSpan);
            }
        }
    }

    private void CheckParameters(ImmutableArray<IParameterSymbol> parameters, VibraniumSpan fallbackSpan)
    {
        foreach (var param in parameters)
        {
            var pLoc = param.Locations.FirstOrDefault(l => l.IsInSource);
            var pSpan = pLoc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(pLoc) : fallbackSpan;

            if (param.IsParams)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Parameter '{param.Name}' cannot use 'params' in Vibranium.", pSpan);
            }
            if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(param.Type))
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Closed UTF-8 foreach view '{param.Type.Name}' cannot be used as parameter '{param.Name}'.", pSpan);
                continue;
            }
            if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(param.Type))
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Standalone '{param.Type.Name}' cannot be used as parameter '{param.Name}'.", pSpan);
                continue;
            }
            if (IsBannedDelegateOrInterfaceOrObjectType(param.Type))
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Parameter '{param.Name}' of type '{param.Type.ToDisplayString()}' is banned in Vibranium.", pSpan);
            }

            foreach (var pAttr in param.GetAttributes())
            {
                if (pAttr.AttributeClass?.Name == "LifetimeOfAttribute" &&
                    pAttr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Vibranium")
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, "'[LifetimeOf]' is only supported as '[return: LifetimeOf(...)]' on methods in Vibranium.", pSpan);
                }
            }

            if (param.RefKind is RefKind.Ref or RefKind.Out)
            {
                if (param.Type.IsRefLikeType || _module.IsViewLike(VibraniumSymbolHelpers.MapType(param.Type, _module)))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Parameter '{param.Name}' cannot pass borrowed view '{param.Type.Name}' by ref or out in Vibranium.", pSpan);
                }
                else if (VibraniumSymbolHelpers.IsLinearResourceSymbol(param.Type))
                {
                    Emit(VibraniumDiagnosticIds.LinearResourceViolation, $"Parameter '{param.Name}' cannot pass linear IDisposable resource '{param.Type.Name}' by ref or out.", pSpan);
                }
            }
        }
    }

    private void CheckLocalSymbol(ILocalSymbol localSym, SyntaxNode syntaxNode)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(syntaxNode);
        if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(localSym.Type))
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Closed UTF-8 foreach view '{localSym.Type.Name}' cannot be stored in local variable '{localSym.Name}'; iterate it directly in 'foreach'.", span);
            return;
        }
        if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(localSym.Type))
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Standalone '{localSym.Type.Name}' local variable '{localSym.Name}' is not supported in Vibranium.", span);
            return;
        }
        if (IsBannedDelegateOrInterfaceOrObjectType(localSym.Type))
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Local variable '{localSym.Name}' of type '{localSym.Type.ToDisplayString()}' is banned in Vibranium.", span);
            return;
        }
        if (localSym.IsRef)
        {
            if (localSym.Type.IsRefLikeType || _module.IsViewLike(VibraniumSymbolHelpers.MapType(localSym.Type, _module)))
            {
                Emit(VibraniumDiagnosticIds.Unsupported, $"Ref local '{localSym.Name}' cannot alias borrowed view '{localSym.Type.Name}' in Vibranium.", span);
            }
            else if (VibraniumSymbolHelpers.IsLinearResourceSymbol(localSym.Type))
            {
                Emit(VibraniumDiagnosticIds.LinearResourceViolation, $"Ref local '{localSym.Name}' cannot alias linear IDisposable resource '{localSym.Type.Name}'.", span);
            }
        }
    }

    private static bool HasFieldInitializer(IFieldSymbol field)
    {
        foreach (var syntaxRef in field.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is VariableDeclaratorSyntax declarator && declarator.Initializer is not null)
            {
                return true;
            }
        }
        if (field.AssociatedSymbol is IPropertySymbol assocProp)
        {
            foreach (var syntaxRef in assocProp.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax propSyntax && propSyntax.Initializer is not null)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsInsideEmbeddedAttributeType(SyntaxNode node, SemanticModel model)
    {
        var typeDecl = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) return false;
        var sym = model.GetDeclaredSymbol(typeDecl);
        return sym is not null && VibraniumSymbolHelpers.IsEmbeddedOrCompilerSynthesizedType(sym);
    }

    private static bool IsDirectRefAssignmentTarget(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax paren) expression = paren.Expression;
        return expression is AssignmentExpressionSyntax { Right: RefExpressionSyntax };
    }

    private static bool IsSemanticDiscard(ExpressionSyntax expression, SemanticModel model)
    {
        while (expression is ParenthesizedExpressionSyntax paren) expression = paren.Expression;
        return model.GetOperation(expression) is IDiscardOperation;
    }

    private static bool IsDiscardedExpression(ExpressionSyntax expression, SemanticModel model)
    {
        SyntaxNode current = expression;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax paren:
                    current = paren;
                    continue;
                case CheckedExpressionSyntax check:
                    current = check;
                    continue;
                case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    assignment.Right == current && IsSemanticDiscard(assignment.Left, model):
                    current = assignment;
                    continue;
                case ExpressionStatementSyntax:
                    return true;
                case ForStatementSyntax loop:
                    return loop.Initializers.Any(item => item == current) || loop.Incrementors.Any(item => item == current);
                case ArrowExpressionClauseSyntax arrow:
                    return arrow.Parent is not null && model.GetDeclaredSymbol(arrow.Parent) is IMethodSymbol { ReturnsVoid: true };
                default:
                    return false;
            }
        }
    }

    private void CheckSyntaxNode(SyntaxNode node, SemanticModel model)
    {
        if (node is MemberDeclarationSyntax memberDecl)
        {
            CheckModifiers(memberDecl.Modifiers);
        }
        else if (node is LocalFunctionStatementSyntax localFn)
        {
            if (localFn.Parent is SwitchSectionSyntax)
            {
                Emit(VibraniumDiagnosticIds.Unsupported,
                    SubsetDeclarationCuts.DirectSwitchFunctionMessage,
                    VibraniumSymbolHelpers.ToVibraniumSpan(localFn.Identifier.GetLocation()));
                return;
            }
            CheckModifiers(localFn.Modifiers);
            var fnSpan = VibraniumSymbolHelpers.ToVibraniumSpan(localFn.Identifier.GetLocation());
            var localMethodSym = model.GetDeclaredSymbol(localFn) as IMethodSymbol;
            if (localMethodSym is not null && (localMethodSym.IsExtern || VibraniumSymbolHelpers.HasInteropImportAttribute(localMethodSym)))
            {
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    $"External or P/Invoke local function '{localMethodSym.Name}' ('extern' / '[DllImport]' / '[LibraryImport]') is banned in Vibranium.",
                    fnSpan);
            }
            if (!localFn.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                Emit(VibraniumDiagnosticIds.Unsupported, "Non-static (capturing) local functions are banned in Vibranium; declare local functions 'static'.", fnSpan);
            }
            else if (localMethodSym is not null)
            {
                if (VibraniumSymbolHelpers.GetNativeSymbol(localMethodSym) is not null)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods.", fnSpan);
                }
                if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(localMethodSym.ReturnType))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Standalone '{localMethodSym.ReturnType.Name}' return types are not supported in Vibranium.", fnSpan);
                }
                else if (IsBannedDelegateOrInterfaceOrObjectType(localMethodSym.ReturnType))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Return type '{localMethodSym.ReturnType.ToDisplayString()}' is banned in Vibranium.", fnSpan);
                }
                CheckTypeParameters(localMethodSym.TypeParameters, fnSpan);
                CheckParameters(localMethodSym.Parameters, fnSpan);
            }
        }

        switch (node)
        {
            case AliasQualifiedNameSyntax aliasQualified when
                model.GetAliasInfo(aliasQualified.Alias)?.Target is not INamespaceSymbol { IsGlobalNamespace: true }:
                Emit(VibraniumDiagnosticIds.Unsupported,
                    "User alias-qualified names are not supported in Vibranium v0.2; use a qualified name or 'global::'.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(aliasQualified.Alias));
                break;

            case ExternAliasDirectiveSyntax externAlias:
                Emit(VibraniumDiagnosticIds.Unsupported,
                    "'extern alias' directives are not supported in Vibranium v0.2.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(externAlias));
                break;

            // Roslyn spells the contextual constraint as an IdentifierName.
            // An escaped identifier (e.g. @notnull) is not the constraint keyword.
            case TypeConstraintSyntax { Type: IdentifierNameSyntax { Identifier.Text: "notnull" } constraintType }:
                Emit(VibraniumDiagnosticIds.Unsupported,
                    "The 'notnull' constraint is not supported in Vibranium v0.2; Vibranium types are never null, so omit it.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(constraintType));
                break;

            case AttributeSyntax attribute:
                CheckAttribute(attribute, model);
                break;

            case MethodDeclarationSyntax { ExplicitInterfaceSpecifier: { } explicitMethodSpec }:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(explicitMethodSpec));
                break;

            case PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: { } explicitPropSpec }:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(explicitPropSpec));
                break;

            case IndexerDeclarationSyntax { ExplicitInterfaceSpecifier: { } explicitIndexerSpec }:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(explicitIndexerSpec));
                break;

            case EventDeclarationSyntax { ExplicitInterfaceSpecifier: { } explicitEventSpec }:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(explicitEventSpec));
                break;

            case VariableDeclaratorSyntax varDeclarator:
                if (model.GetDeclaredSymbol(varDeclarator) is ILocalSymbol declLocal)
                {
                    CheckLocalSymbol(declLocal, varDeclarator);
                }
                break;

            case SingleVariableDesignationSyntax varDesig:
                if (model.GetDeclaredSymbol(varDesig) is ILocalSymbol desigLocal)
                {
                    CheckLocalSymbol(desigLocal, varDesig);
                }
                break;

            case ScopedTypeSyntax
                { Parent: ForEachStatementSyntax or DeclarationExpressionSyntax { Parent: ForEachVariableStatementSyntax } } scopedIteration:
                Emit(VibraniumDiagnosticIds.Unsupported,
                    "'scoped' iteration variables are not supported in Vibranium v0.2; omit 'scoped' from the 'foreach' variable.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(scopedIteration.ScopedKeyword.GetLocation()));
                break;

            case ForEachStatementSyntax forEachStmt:
            {
                var collType = model.GetTypeInfo(forEachStmt.Expression).Type;
                if (collType is not null && collType.TypeKind != TypeKind.Error)
                {
                    if (!IsSupportedForeachCollection(collType, out bool isReadOnlyForeach))
                    {
                        Emit(
                            VibraniumDiagnosticIds.Unsupported,
                            $"Type '{collType.ToDisplayString()}' is not a supported 'foreach' collection in Vibranium.",
                            VibraniumSymbolHelpers.ToVibraniumSpan(forEachStmt.Expression));
                        break;
                    }
                    if (forEachStmt.Type is RefTypeSyntax refSyntax && isReadOnlyForeach && !refSyntax.ReadOnlyKeyword.IsKind(SyntaxKind.ReadOnlyKeyword))
                    {
                        Emit(
                            VibraniumDiagnosticIds.MutateThroughSharedBorrow,
                            $"Cannot iterate '{collType.ToDisplayString()}' by mutable 'ref' in Vibranium.",
                            VibraniumSymbolHelpers.ToVibraniumSpan(forEachStmt.Type));
                        break;
                    }
                    if (forEachStmt.Type is RefTypeSyntax && IsUtf8ScanForeachCollection(collType))
                    {
                        Emit(
                            VibraniumDiagnosticIds.MutateThroughSharedBorrow,
                            $"Cannot iterate '{collType.ToDisplayString()}' by 'ref' in Vibranium.",
                            VibraniumSymbolHelpers.ToVibraniumSpan(forEachStmt.Type));
                        break;
                    }
                }
                if (model.GetDeclaredSymbol(forEachStmt) is ILocalSymbol forEachLocal)
                {
                    CheckLocalSymbol(forEachLocal, forEachStmt);
                }
                break;
            }

            // ---- VIB008: Null, ?, !, ??, ?., non-zero-safe default / new S() / new T[n] ----
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.NullLiteralExpression):
                Emit(VibraniumDiagnosticIds.NullOrZeroUnsafe, "Null literals are banned in Vibranium; use Option<T> (Some/None).", VibraniumSymbolHelpers.ToVibraniumSpan(lit));
                break;

            case LiteralExpressionSyntax defLit when defLit.IsKind(SyntaxKind.DefaultLiteralExpression):
            {
                var t = model.GetTypeInfo(defLit).ConvertedType ?? model.GetTypeInfo(defLit).Type;
                CheckDefaultZeroSafety(t, defLit);
                break;
            }

            case LiteralExpressionSyntax strOrNumLit:
                CheckLiteralExpression(strOrNumLit, model);
                break;

            case NullableTypeSyntax nullableType:
                Emit(VibraniumDiagnosticIds.NullOrZeroUnsafe, $"Nullable type '{nullableType}' is banned in Vibranium; use Option<T>.", VibraniumSymbolHelpers.ToVibraniumSpan(nullableType));
                break;

            case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                Emit(VibraniumDiagnosticIds.NullOrZeroUnsafe, "Null-forgiving postfix '!' operator is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(postfix));
                break;

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.CoalesceExpression):
                Emit(VibraniumDiagnosticIds.NullOrZeroUnsafe, "Null-coalescing '??' operator is banned in Vibranium; use Option<T>.UnwrapOr.", VibraniumSymbolHelpers.ToVibraniumSpan(bin));
                break;

            case BinaryExpressionSyntax bin:
                CheckBinaryExpression(bin, model);
                break;

            case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                IsDirectRefAssignmentTarget(assign.Left) && !IsDiscardedExpression(assign, model):
                Emit(VibraniumDiagnosticIds.Unsupported,
                    "Consuming a simple assignment whose direct target is a ref-assignment expression is unsupported with .NET SDK 11.0.100; discard its result or split the assignments.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(assign));
                break;

            case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                Emit(VibraniumDiagnosticIds.NullOrZeroUnsafe, "Null-coalescing '??=' operator is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(assign));
                break;

            case AssignmentExpressionSyntax addAssign when addAssign.IsKind(SyntaxKind.AddAssignmentExpression):
            {
                ExpressionSyntax lhsExpr = addAssign.Left;
                while (lhsExpr is ParenthesizedExpressionSyntax parenLhs)
                {
                    lhsExpr = parenLhs.Expression;
                }
                var lhsType = model.GetTypeInfo(lhsExpr).Type;
                if (lhsType?.SpecialType == SpecialType.System_String)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "String operator '+=' is banned in Vibranium.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(addAssign));
                    break;
                }
                var lhsSym = model.GetSymbolInfo(lhsExpr).Symbol;
                var addOpSym = model.GetSymbolInfo(addAssign).Symbol as IMethodSymbol;
                bool usesInstanceAddAssign =
                    (addOpSym is not null && VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(addOpSym)) ||
                    (lhsType is not null &&
                     !VibraniumSymbolHelpers.IsVibraniumCoreType(lhsType, "Vec") &&
                     lhsType.GetMembers("op_AdditionAssignment").OfType<IMethodSymbol>().Any(VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator));
                if (usesInstanceAddAssign &&
                    lhsSym is IPropertySymbol { ReturnsByRef: false, ReturnsByRefReadonly: false } lhsProp &&
                    !(lhsProp.IsIndexer &&
                      (VibraniumSymbolHelpers.IsVibraniumCoreType(lhsProp.ContainingType, "Vec") ||
                       VibraniumSymbolHelpers.IsSpanType(lhsProp.ContainingType, out _, out _))))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Compound assignment with user-defined 'operator +=' on a non-ref property or indexer is not supported in Vibranium.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(addAssign));
                }
                break;
            }

            case ConditionalAccessExpressionSyntax condAccess:
                Emit(VibraniumDiagnosticIds.NullOrZeroUnsafe, "Null-conditional '?.' / '?[]' operator is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(condAccess));
                break;

            case DefaultExpressionSyntax defExpr:
            {
                var t = model.GetTypeInfo(defExpr).ConvertedType ?? model.GetTypeInfo(defExpr).Type;
                CheckDefaultZeroSafety(t, defExpr);
                break;
            }

            case BaseObjectCreationExpressionSyntax objCreation:
                CheckObjectCreation(objCreation, model);
                break;

            case ArrayCreationExpressionSyntax arrayCreation:
                CheckArrayCreation(arrayCreation, model);
                break;

            case ImplicitArrayCreationExpressionSyntax implicitArray:
                CheckImplicitArrayCreation(implicitArray, model);
                break;

            case StackAllocArrayCreationExpressionSyntax stackAlloc:
                CheckStackAlloc(stackAlloc, model);
                break;

            case ImplicitStackAllocArrayCreationExpressionSyntax implStackAlloc:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "User 'stackalloc' expressions are banned in Vibranium; use Span<T> collection expressions '[...]' or Vec<T>.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(implStackAlloc));
                break;

            case ArrayTypeSyntax arrayType when arrayType.Parent is not ArrayCreationExpressionSyntax and not StackAllocArrayCreationExpressionSyntax:
                CheckArrayType(arrayType, model);
                break;

            case GenericNameSyntax genericName:
                CheckGenericName(genericName, model);
                break;

            case InvocationExpressionSyntax { Expression: IdentifierNameSyntax invocationName } invocation when
                invocationName.Identifier.ValueText == "nameof" && model.GetOperation(invocation) is INameOfOperation:
                Emit(VibraniumDiagnosticIds.Unsupported, "'nameof(...)' expressions are not supported in Vibranium.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(invocation));
                break;

            case InvocationExpressionSyntax invocation:
                CheckInvocation(invocation, model);
                break;

            case ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.None):
                // Bind the actual argument/parameter pair, including named arguments,
                // constructors and overloads. Reduced extension receivers have no
                // ArgumentSyntax and do not require an impossible explicit modifier.
                if (model.GetOperation(argument) is IArgumentOperation { Parameter.RefKind: RefKind.RefReadOnlyParameter })
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "A direct call to a 'ref readonly' parameter requires an explicit 'in' or 'ref' argument.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(argument));
                }
                break;

            // ---- VIB000: Unsupported C# constructs ----
            case CheckedExpressionSyntax checkedExpr when checkedExpr.IsKind(SyntaxKind.CheckedExpression):
                Emit(VibraniumDiagnosticIds.Unsupported, "'checked(...)' expressions are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(checkedExpr));
                break;

            case CheckedStatementSyntax checkedStmt when checkedStmt.IsKind(SyntaxKind.CheckedStatement):
                Emit(VibraniumDiagnosticIds.Unsupported, "'checked' blocks are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(checkedStmt));
                break;

            case InterpolatedStringExpressionSyntax interpStr:
                Emit(VibraniumDiagnosticIds.Unsupported, "String interpolation is not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(interpStr));
                break;

            case ElementAccessExpressionSyntax elemAccess:
                if (model.GetTypeInfo(elemAccess.Expression).Type?.SpecialType == SpecialType.System_String)
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, "String character indexing is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(elemAccess));
                }
                break;

            case PredefinedTypeSyntax predType when predType.Keyword.IsKind(SyntaxKind.DecimalKeyword):
                Emit(VibraniumDiagnosticIds.Unsupported, "'decimal' is not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(predType));
                break;

            case GotoStatementSyntax gotoStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "Unstructured 'goto' statements are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(gotoStmt));
                break;

            case ThrowStatementSyntax throwStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "Exceptions ('throw') are banned in Vibranium; use Result<T, E> or VibraniumRuntime.Panic.", VibraniumSymbolHelpers.ToVibraniumSpan(throwStmt));
                break;

            case ThrowExpressionSyntax throwExpr:
                Emit(VibraniumDiagnosticIds.Unsupported, "Exceptions ('throw') are banned in Vibranium; use Result<T, E> or VibraniumRuntime.Panic.", VibraniumSymbolHelpers.ToVibraniumSpan(throwExpr));
                break;

            case TryStatementSyntax tryStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "'try'/'catch'/'finally' blocks are banned in Vibranium; use Result<T, E> and 'using var'.", VibraniumSymbolHelpers.ToVibraniumSpan(tryStmt));
                break;

            case LambdaExpressionSyntax lambda:
                Emit(VibraniumDiagnosticIds.Unsupported, "Lambdas and closures are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(lambda));
                break;

            case AnonymousMethodExpressionSyntax anonMethod:
                Emit(VibraniumDiagnosticIds.Unsupported, "Anonymous methods and delegates are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(anonMethod));
                break;

            case QueryExpressionSyntax queryExpr:
                Emit(VibraniumDiagnosticIds.Unsupported, "LINQ query expressions are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(queryExpr));
                break;

            case UnsafeStatementSyntax unsafeStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "'unsafe' blocks are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(unsafeStmt));
                break;

            case PointerTypeSyntax ptrType:
                Emit(VibraniumDiagnosticIds.Unsupported, "Raw pointer types are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(ptrType));
                break;

            case FunctionPointerTypeSyntax fnPtrType:
                Emit(VibraniumDiagnosticIds.Unsupported, "Function pointer types are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(fnPtrType));
                break;

            case FixedStatementSyntax fixedStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "'fixed' statements are banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(fixedStmt));
                break;

            case AwaitExpressionSyntax awaitExpr:
                Emit(VibraniumDiagnosticIds.Unsupported, "'await' / async state machines are not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(awaitExpr));
                break;

            case YieldStatementSyntax yieldStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "Iterator 'yield' statements are not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(yieldStmt));
                break;

            case LockStatementSyntax lockStmt:
                Emit(VibraniumDiagnosticIds.Unsupported, "'lock' statements are not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(lockStmt));
                break;

            case UsingStatementSyntax { Declaration: null } usingStmt:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Statement 'using (expr)' on an existing expression is not supported in Vibranium; declare a scoped resource with 'using var x = ...;' or 'using (var x = ...) { ... }'.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(usingStmt.UsingKeyword.GetLocation()));
                break;

            case ExtensionBlockDeclarationSyntax extBlock:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "C# 14 'extension(...)' blocks are not supported in Vibranium; declare classic 'static class' extension methods instead.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(extBlock));
                break;

            case WithExpressionSyntax withExpr:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "'with' expressions are not supported in Vibranium.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(withExpr));
                break;

            case SizeOfExpressionSyntax sizeOfExpr:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "'sizeof(...)' expressions are not supported in Vibranium.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(sizeOfExpr));
                break;

            case SwitchStatementSyntax switchStmt:
                if (model.GetTypeInfo(switchStmt.Expression).Type?.SpecialType == SpecialType.System_String)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Switch on 'string' is banned in Vibranium; match UTF-8 bytes via '.AsUtf8()' instead.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(switchStmt.Expression));
                }
                EmitLinearUnionSwitchBan(switchStmt, model);
                break;

            case SwitchExpressionSyntax switchExpr:
                if (model.GetTypeInfo(switchExpr.GoverningExpression).Type?.SpecialType == SpecialType.System_String)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Switch on 'string' is banned in Vibranium; match UTF-8 bytes via '.AsUtf8()' instead.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(switchExpr.GoverningExpression));
                }
                EmitLinearUnionSwitchBan(switchExpr, model);
                break;

            case UsingDirectiveSyntax usingDir when _checkingNonStdUserTree && usingDir.Name is not null:
                CheckCrossUnitStdAccess(model.GetSymbolInfo(usingDir.Name).Symbol, VibraniumSymbolHelpers.ToVibraniumSpan(usingDir));
                break;

            case IdentifierNameSyntax idSyntax:
                CheckIdentifierUsage(idSyntax, model);
                break;

            case MemberAccessExpressionSyntax memberAccess:
                CheckMemberAccess(memberAccess, model);
                break;
        }
    }

    private void CheckLiteralExpression(LiteralExpressionSyntax lit, SemanticModel model)
    {
        if (lit.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            var t = model.GetTypeInfo(lit).ConvertedType ?? model.GetTypeInfo(lit).Type;
            CheckDefaultZeroSafety(t, lit);
            return;
        }

        var typeInfo = model.GetTypeInfo(lit);
        if (typeInfo.Type?.SpecialType == SpecialType.System_Decimal)
        {
            Emit(VibraniumDiagnosticIds.Unsupported, "'decimal' literals are not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(lit));
            return;
        }

        var cv = model.GetConstantValue(lit);
        if (cv.HasValue && cv.Value is string strVal && ContainsLoneSurrogate(strVal))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                "String literal contains a lone UTF-16 surrogate, which is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(lit));
        }
        else if (cv.HasValue && cv.Value is char ch && char.IsSurrogate(ch))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                "Lone UTF-16 surrogate char literal is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(lit));
        }
    }

    private static bool ContainsLoneSurrogate(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                {
                    return true;
                }
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return true;
            }
        }
        return false;
    }

    private void CheckBinaryExpression(BinaryExpressionSyntax bin, SemanticModel model)
    {
        var leftType = model.GetTypeInfo(bin.Left).Type;
        var rightType = model.GetTypeInfo(bin.Right).Type;
        if (leftType?.SpecialType == SpecialType.System_String || rightType?.SpecialType == SpecialType.System_String)
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"String operator '{bin.OperatorToken.Text}' is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(bin));
        }
    }

    private void CheckInvocation(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(invocation);
        var symInfo = model.GetSymbolInfo(invocation);
        if (symInfo.Symbol is null &&
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
            model.GetConstantValue(invocation) is { HasValue: true, Value: string })
        {
            Emit(VibraniumDiagnosticIds.Unsupported, "'nameof(...)' expressions are not supported in Vibranium.", span);
            return;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax invMae &&
            model.GetSymbolInfo(invMae.Expression).Symbol is ITypeParameterSymbol tpReceiver)
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Static member invocation on type parameter '{tpReceiver.Name}' is not supported in Vibranium.",
                span);
            return;
        }

        var method = symInfo.Symbol as IMethodSymbol ?? symInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method is null)
        {
            return;
        }

        bool isResultIsErrGuard =
            VibraniumSymbolHelpers.IsVibraniumCoreType(method.ContainingType, "Result") &&
            method.Name == "IsErr" &&
            method.Parameters.Length == 2;
        bool isOptionIsNoneGuard =
            VibraniumSymbolHelpers.IsVibraniumCoreType(method.ContainingType, "Option") &&
            method.Name == "IsNone" &&
            method.Parameters.Length == 1;
        if (isResultIsErrGuard || isOptionIsNoneGuard)
        {
            if (!IsDirectTopLevelIfCondition(invocation))
            {
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    $"'{method.ContainingType.Name}.{method.Name}(out ...)' is only permitted as the direct top-level condition of 'if ([!]...)'.",
                    span);
            }
            foreach (var argSyn in invocation.ArgumentList.Arguments)
            {
                var argOp = model.GetOperation(argSyn.Expression);
                bool validFreshOrDiscard = argOp switch
                {
                    IDeclarationExpressionOperation { Expression: ILocalReferenceOperation or IDiscardOperation } => true,
                    IDiscardOperation => true,
                    null => argSyn.Expression is DeclarationExpressionSyntax,
                    _ => false,
                };
                if (!validFreshOrDiscard)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Every 'out' argument of '{method.ContainingType.Name}.{method.Name}' must be a fresh inline declaration ('out var x') or discard ('out _').",
                        VibraniumSymbolHelpers.ToVibraniumSpan(argSyn));
                }
            }
        }

        if (_checkingNonStdUserTree)
        {
            CheckCrossUnitStdAccess(method, span);
        }

        if (method.IsExtern || VibraniumSymbolHelpers.HasInteropImportAttribute(method))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Call to external or P/Invoke method '{method.Name}' is banned in Vibranium.",
                span);
            return;
        }

        if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion &&
            !VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(method))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Invocation of operator or conversion '{method.Name}' is not supported in Vibranium.",
                span);
            return;
        }

        // Ban manual user .Dispose() calls on IDisposable resources (VIB006)
        if (!method.IsStatic &&
            method.Name == "Dispose" &&
            method.Parameters.Length == 0 &&
            (VibraniumSymbolHelpers.ImplementsIDisposable(method.ContainingType) ||
             method.ContainingType?.SpecialType == SpecialType.System_IDisposable))
        {
            Emit(
                VibraniumDiagnosticIds.LinearResourceViolation,
                "Manual '.Dispose()' calls are banned in Vibranium; dispose linear IDisposable resources deterministically via 'using var' or move them by value.",
                span);
            return;
        }

        // Ban direct .Deconstruct(...) calls (closes Some<R>/Ok<R> in-receiver aliasing hole)
        if (method.Name == "Deconstruct")
        {
            if (VibraniumSymbolHelpers.IsLinearResourceSymbol(method.ContainingType))
            {
                Emit(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    "Direct '.Deconstruct(...)' calls on linear union wrappers are banned in Vibranium.",
                    span);
            }
            else
            {
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Direct '.Deconstruct(...)' calls are banned in Vibranium; use pattern matching or '.Value'/'.Error'.",
                    span);
            }
            return;
        }

        if (method.Name == "GetEnumerator" && method.Parameters.Length == 0)
        {
            Emit(VibraniumDiagnosticIds.Unsupported, "Explicit '.GetEnumerator()' calls outside 'foreach' are banned in Vibranium.", span);
            return;
        }

        if ((VibraniumSymbolHelpers.IsClosedUtf8ViewType(method.ContainingType) ||
             (method.ContainingType?.Name == "Enumerator" && method.ContainingType.ContainingNamespace?.ToDisplayString() == "System")) &&
            method.Name == "MoveNext")
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Explicit '{method.Name}()' calls on enumerators outside 'foreach' are banned in Vibranium.", span);
            return;
        }

        if (method.Name == "Move" &&
            method.ContainingType?.Name == "Prelude" &&
            method.ContainingNamespace?.ToDisplayString() == "Vibranium" &&
            method.TypeArguments.Length == 1 &&
            VibraniumSymbolHelpers.IsClosedUtf8ViewType(method.TypeArguments[0]))
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Closed UTF-8 foreach view '{method.TypeArguments[0].Name}' cannot be passed to 'Move(...)'.", span);
            return;
        }

        if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(method.ReturnType) &&
            !IsAllowedClosedUtf8ViewExpressionPosition(invocation, allowForeachCollection: VibraniumSymbolHelpers.IsVibraniumCoreType(method.ReturnType, "Utf8Lines")))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Closed UTF-8 foreach view '{method.ReturnType.Name}' is only permitted as a method return value or direct 'foreach' collection.",
                span);
            return;
        }

        if (method.ContainingType is INamedTypeSymbol { IsGenericType: true } genericOwner)
        {
            if (!VibraniumSymbolHelpers.ValidateL1LinearTypeArguments(genericOwner, genericOwner.TypeArguments, out string ownerErr))
            {
                Emit(VibraniumDiagnosticIds.LinearResourceViolation, ownerErr, span);
            }
            else if (VibraniumSymbolHelpers.IsVibraniumCoreType(genericOwner, "Option"))
            {
                if (method.Name is "UnwrapOr" or "UnwrapOrDefault" or "UnwrapMut" &&
                    VibraniumSymbolHelpers.IsLinearResourceSymbol(genericOwner.TypeArguments[0]))
                {
                    Emit(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"'Option<{genericOwner.TypeArguments[0].Name}>.{method.Name}' is banned on linear resources.",
                        span);
                }
                else if (method.Name == "UnwrapOrDefault")
                {
                    if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(genericOwner.TypeArguments[0], _compilation))
                    {
                        Emit(
                            VibraniumDiagnosticIds.NullOrZeroUnsafe,
                            $"'Option<{genericOwner.TypeArguments[0].ToDisplayString()}>.UnwrapOrDefault()' requires a zero-safe payload type.",
                            span);
                    }
                }
            }
            else if (VibraniumSymbolHelpers.IsVibraniumCoreType(genericOwner, "Result"))
            {
                if (method.Name is "UnwrapOr" or "UnwrapOrDefault" &&
                    VibraniumSymbolHelpers.IsLinearResourceSymbol(genericOwner))
                {
                    Emit(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"'Result<{genericOwner.TypeArguments[0].Name}, {genericOwner.TypeArguments[1].Name}>.{method.Name}' is banned on linear resources.",
                        span);
                }
                else if (method.Name == "UnwrapOrDefault")
                {
                    if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(genericOwner.TypeArguments[0], _compilation))
                    {
                        Emit(
                            VibraniumDiagnosticIds.NullOrZeroUnsafe,
                            $"'Result<{genericOwner.TypeArguments[0].ToDisplayString()}, {genericOwner.TypeArguments[1].ToDisplayString()}>.UnwrapOrDefault()' requires a zero-safe Ok payload type.",
                            span);
                    }
                }
            }
        }

        if (method.IsGenericMethod)
        {
            foreach (var ta in method.TypeArguments)
            {
                if (IsBannedDelegateOrInterfaceOrObjectType(ta))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported, $"Type argument '{ta.ToDisplayString()}' is banned in Vibranium.", span);
                }
            }

            if (!VibraniumSymbolHelpers.ValidateL1LinearTypeArguments(method, method.TypeArguments, out string methodErr))
            {
                Emit(VibraniumDiagnosticIds.LinearResourceViolation, methodErr, span);
            }

            foreach (var p in method.Parameters)
            {
                if (p.RefKind is RefKind.Ref or RefKind.Out)
                {
                    if (p.Type.IsRefLikeType || _module.IsViewLike(VibraniumSymbolHelpers.MapType(p.Type, _module)))
                    {
                        Emit(VibraniumDiagnosticIds.Unsupported, $"Cannot pass borrowed view '{p.Type.Name}' by ref or out in Vibranium.", span);
                    }
                    else if (VibraniumSymbolHelpers.IsLinearResourceSymbol(p.Type))
                    {
                        Emit(VibraniumDiagnosticIds.LinearResourceViolation, $"Cannot pass linear IDisposable resource '{p.Type.Name}' by ref or out.", span);
                    }
                }
            }
        }
    }

    private void CheckModifiers(SyntaxTokenList modifiers)
    {
        foreach (var tok in modifiers)
        {
            switch (tok.Kind())
            {
                case SyntaxKind.UnsafeKeyword:
                    Emit(VibraniumDiagnosticIds.Unsupported, "'unsafe' modifier is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(tok.GetLocation()));
                    break;
                case SyntaxKind.AsyncKeyword:
                    Emit(VibraniumDiagnosticIds.Unsupported, "'async' modifier is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(tok.GetLocation()));
                    break;
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.VirtualKeyword:
                case SyntaxKind.OverrideKeyword:
                    Emit(VibraniumDiagnosticIds.Unsupported, $"'{tok.Text}' modifier is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(tok.GetLocation()));
                    break;
                case SyntaxKind.ProtectedKeyword:
                    Emit(VibraniumDiagnosticIds.Unsupported, "'protected' modifier is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(tok.GetLocation()));
                    break;
                case SyntaxKind.ExternKeyword:
                    Emit(VibraniumDiagnosticIds.Unsupported, "'extern' modifier is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(tok.GetLocation()));
                    break;
            }
        }
    }

    private void CheckDefaultZeroSafety(ITypeSymbol? type, SyntaxNode node)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return;
        }
        if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(type))
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"'default' value of closed UTF-8 foreach view '{type.Name}' is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(node));
            return;
        }
        if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(type))
        {
            Emit(VibraniumDiagnosticIds.Unsupported, $"Standalone '{type.Name}' values are not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(node));
            return;
        }
        if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(type, _compilation))
        {
            Emit(
                VibraniumDiagnosticIds.NullOrZeroUnsafe,
                $"'default' initialization of non-zero-safe type '{type.ToDisplayString()}' is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(node));
        }
    }

    private void CheckObjectCreation(BaseObjectCreationExpressionSyntax objCreation, SemanticModel model)
    {
        var typeInfo = model.GetTypeInfo(objCreation);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        var sym = model.GetSymbolInfo(objCreation).Symbol as IMethodSymbol
            ?? model.GetSymbolInfo(objCreation).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (_checkingNonStdUserTree)
        {
            var ocSpan = VibraniumSymbolHelpers.ToVibraniumSpan(objCreation);
            if (!CheckCrossUnitStdAccess(sym, ocSpan))
            {
                CheckCrossUnitStdAccess(type, ocSpan);
            }
        }

        if (objCreation.Initializer is not null)
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                "Object and collection member initializers are not supported in Vibranium; pass constructor arguments or use collection expressions '[...]'.",
                VibraniumSymbolHelpers.ToVibraniumSpan(objCreation.Initializer));
        }

        if (VibraniumSymbolHelpers.IsClosedUtf8ViewType(type))
        {
            bool allowedArgEnumCtor =
                VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Utf8ArgEnumerator") &&
                objCreation.ArgumentList is { Arguments.Count: 1 } &&
                IsAllowedClosedUtf8ViewExpressionPosition(objCreation, allowForeachCollection: false);
            if (!allowedArgEnumCtor)
            {
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    $"Direct construction of closed UTF-8 foreach view '{type!.Name}' is not permitted here.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(objCreation));
            }
            return;
        }

        if (VibraniumSymbolHelpers.IsSystemIndexOrRangeType(type))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Standalone '{type!.Name}' construction is not supported in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(objCreation));
            return;
        }

        if (sym is not null &&
            sym.ContainingNamespace?.ToDisplayString() == "Vibranium" &&
            VibraniumSymbolHelpers.HasObsoleteAttribute(sym))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Direct call to internal union protocol constructor on '{sym.ContainingType.Name}' is banned.",
                VibraniumSymbolHelpers.ToVibraniumSpan(objCreation));
            return;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } createdGeneric)
        {
            if (!VibraniumSymbolHelpers.ValidateL1LinearTypeArguments(createdGeneric, createdGeneric.TypeArguments, out string ocErr))
            {
                Emit(VibraniumDiagnosticIds.LinearResourceViolation, ocErr, VibraniumSymbolHelpers.ToVibraniumSpan(objCreation));
            }
        }

        if (type is not null && (type.TypeKind == TypeKind.Struct || type.TypeKind == TypeKind.TypeParameter))
        {
            bool isParameterlessDefaultStructCtor =
                (objCreation.ArgumentList is null || objCreation.ArgumentList.Arguments.Count == 0) &&
                (sym is null || (sym.Parameters.Length == 0 && sym.IsImplicitlyDeclared));

            if (isParameterlessDefaultStructCtor &&
                !(type is INamedTypeSymbol namedType && _rejectedDisposableStructs.Contains(namedType.OriginalDefinition)))
            {
                if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(type, _compilation))
                {
                    Emit(
                        VibraniumDiagnosticIds.NullOrZeroUnsafe,
                        $"Parameterless zero-initialization 'new {type.Name}()' of non-zero-safe type '{type.ToDisplayString()}' is banned in Vibranium.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(objCreation));
                }
            }
        }
    }

    private void CheckArrayCreation(ArrayCreationExpressionSyntax arrayCreation, SemanticModel model)
    {
        var arrayTypeSym = model.GetTypeInfo(arrayCreation).Type as IArrayTypeSymbol;
        var elemType = arrayTypeSym?.ElementType ?? model.GetTypeInfo(arrayCreation.Type.ElementType).Type;
        if (elemType is not null)
        {
            if (VibraniumSymbolHelpers.IsLinearResourceSymbol(elemType))
            {
                Emit(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    $"Linear IDisposable resource '{elemType.Name}' cannot be stored in an array.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(arrayCreation));
                return;
            }

            if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(elemType, _compilation))
            {
                Emit(
                    VibraniumDiagnosticIds.NullOrZeroUnsafe,
                    $"Array allocation 'new {elemType.Name}[...]' zero-initializes non-zero-safe element type '{elemType.ToDisplayString()}'.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(arrayCreation));
                return;
            }
        }

        Emit(
            VibraniumDiagnosticIds.Unsupported,
            "Managed arrays 'T[]' are not supported in Vibranium; use Vec<T> or Span<T>.",
            VibraniumSymbolHelpers.ToVibraniumSpan(arrayCreation));
    }

    private void CheckImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax implicitArray, SemanticModel model)
    {
        if (model.GetTypeInfo(implicitArray).Type is IArrayTypeSymbol arr &&
            VibraniumSymbolHelpers.IsLinearResourceSymbol(arr.ElementType))
        {
            Emit(
                VibraniumDiagnosticIds.LinearResourceViolation,
                $"Linear IDisposable resource '{arr.ElementType.Name}' cannot be stored in an array.",
                VibraniumSymbolHelpers.ToVibraniumSpan(implicitArray));
            return;
        }

        Emit(
            VibraniumDiagnosticIds.Unsupported,
            "Managed arrays 'T[]' are not supported in Vibranium; use Vec<T> or Span<T>.",
            VibraniumSymbolHelpers.ToVibraniumSpan(implicitArray));
    }

    private void CheckStackAlloc(StackAllocArrayCreationExpressionSyntax stackAlloc, SemanticModel model)
    {
        if (stackAlloc.Type is ArrayTypeSyntax arrSyntax)
        {
            var elemType = model.GetTypeInfo(arrSyntax.ElementType).Type;
            if (elemType is not null && VibraniumSymbolHelpers.IsLinearResourceSymbol(elemType))
            {
                Emit(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    $"Linear IDisposable resource '{elemType.Name}' cannot be stack-allocated in an array.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(stackAlloc));
                return;
            }
        }

        Emit(
            VibraniumDiagnosticIds.Unsupported,
            "User 'stackalloc' expressions are banned in Vibranium; use Span<T> collection expressions '[...]' or Vec<T>.",
            VibraniumSymbolHelpers.ToVibraniumSpan(stackAlloc));
    }

    private void CheckArrayType(ArrayTypeSyntax arrayType, SemanticModel model)
    {
        var elemType = model.GetTypeInfo(arrayType.ElementType).Type;
        if (elemType is not null && VibraniumSymbolHelpers.IsLinearResourceSymbol(elemType))
        {
            Emit(
                VibraniumDiagnosticIds.LinearResourceViolation,
                $"Linear IDisposable resource '{elemType.Name}' cannot be used as an array element type.",
                VibraniumSymbolHelpers.ToVibraniumSpan(arrayType));
            return;
        }

        Emit(
            VibraniumDiagnosticIds.Unsupported,
            "Managed array types 'T[]' are not supported in Vibranium; use Vec<T>, Span<T>, or ReadOnlySpan<T>.",
            VibraniumSymbolHelpers.ToVibraniumSpan(arrayType));
    }

    private void CheckGenericName(GenericNameSyntax genericName, SemanticModel model)
    {
        var symbolInfo = model.GetSymbolInfo(genericName);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if (_checkingNonStdUserTree &&
            genericName.Parent is not InvocationExpressionSyntax &&
            genericName.Parent is not ObjectCreationExpressionSyntax &&
            !(genericName.Parent is MemberAccessExpressionSyntax gMae && gMae.Name == genericName && gMae.Parent is InvocationExpressionSyntax))
        {
            CheckCrossUnitStdAccess(symbol, VibraniumSymbolHelpers.ToVibraniumSpan(genericName));
        }

        if (symbol is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            Emit(
                VibraniumDiagnosticIds.NullOrZeroUnsafe,
                "System.Nullable<T> is banned in Vibranium; use Option<T>.",
                VibraniumSymbolHelpers.ToVibraniumSpan(genericName));
            return;
        }

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(genericName.TypeArgumentList.Arguments.Count);
        foreach (var argSyntax in genericName.TypeArgumentList.Arguments)
        {
            var argType = model.GetTypeInfo(argSyntax).Type;
            if (argType is not null)
            {
                if (IsBannedDelegateOrInterfaceOrObjectType(argType))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Type argument '{argType.ToDisplayString()}' is banned in Vibranium.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(argSyntax));
                }
                builder.Add(argType);
            }
        }

        if (builder.Count > 0 &&
            !VibraniumSymbolHelpers.ValidateL1LinearTypeArguments(symbol, builder.ToImmutable(), out string errMsg))
        {
            Emit(
                VibraniumDiagnosticIds.LinearResourceViolation,
                errMsg,
                VibraniumSymbolHelpers.ToVibraniumSpan(genericName));
        }
    }

    private void CheckIdentifierUsage(IdentifierNameSyntax idSyntax, SemanticModel model)
    {
        if (idSyntax.Identifier.ValueText == "dynamic")
        {
            var typeInfo = model.GetTypeInfo(idSyntax);
            if (typeInfo.Type?.TypeKind == TypeKind.Dynamic)
            {
                Emit(VibraniumDiagnosticIds.Unsupported, "'dynamic' type is banned in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(idSyntax));
                return;
            }
        }

        if (_checkingNonStdUserTree &&
            idSyntax.Parent is not MemberAccessExpressionSyntax &&
            idSyntax.Parent is not InvocationExpressionSyntax &&
            idSyntax.Parent is not ObjectCreationExpressionSyntax &&
            idSyntax.Parent is not NameColonSyntax)
        {
            var sym = model.GetSymbolInfo(idSyntax).Symbol;
            CheckCrossUnitStdAccess(sym, VibraniumSymbolHelpers.ToVibraniumSpan(idSyntax));
        }
    }

    private void CheckMemberAccess(MemberAccessExpressionSyntax memberAccess, SemanticModel model)
    {
        var symInfo = model.GetSymbolInfo(memberAccess);
        var sym = symInfo.Symbol ?? symInfo.CandidateSymbols.FirstOrDefault();
        if (sym is null)
        {
            return;
        }

        if (_checkingNonStdUserTree &&
            !(memberAccess.Parent is InvocationExpressionSyntax pInv && pInv.Expression == memberAccess))
        {
            CheckCrossUnitStdAccess(sym, VibraniumSymbolHelpers.ToVibraniumSpan(memberAccess));
        }

        if (sym is IMethodSymbol { Name: "Deconstruct" } dm &&
            memberAccess.Parent is not InvocationExpressionSyntax)
        {
            if (VibraniumSymbolHelpers.IsLinearResourceSymbol(dm.ContainingType))
            {
                Emit(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    "Direct access to '.Deconstruct' on linear union wrappers is banned in Vibranium.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(memberAccess));
            }
            else if (dm.ContainingNamespace?.ToDisplayString() == "Vibranium")
            {
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Direct access to '.Deconstruct' is banned in Vibranium.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(memberAccess));
            }
            return;
        }

        if (sym.ContainingNamespace?.ToDisplayString() == "Vibranium" &&
            VibraniumSymbolHelpers.HasObsoleteAttribute(sym))
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Direct access to internal union protocol member '{sym.Name}' is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(memberAccess));
            return;
        }

        if ((VibraniumSymbolHelpers.IsClosedUtf8ViewType(sym.ContainingType) ||
             (sym.ContainingType?.Name == "Enumerator" && sym.ContainingType.ContainingNamespace?.ToDisplayString() == "System")) &&
            sym.Name is "Current" or "MoveNext" or "GetEnumerator" &&
            memberAccess.Parent is not InvocationExpressionSyntax)
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                $"Direct access to enumerator member '.{sym.Name}' outside 'foreach' is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(memberAccess));
            return;
        }

        if (sym.ContainingType?.SpecialType == SpecialType.System_String && sym.Name == "Length")
        {
            Emit(
                VibraniumDiagnosticIds.Unsupported,
                "'string.Length' is banned in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(memberAccess));
        }
    }

    private static bool IsSupportedForeachCollection(ITypeSymbol type, out bool isReadOnly)
    {
        isReadOnly = false;
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Vec"))
        {
            return true;
        }
        if (VibraniumSymbolHelpers.IsSpanType(type, out _, out bool spanRo))
        {
            isReadOnly = spanRo;
            return true;
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Utf8Lines"))
        {
            isReadOnly = true;
            return true;
        }
        if (IsUtf8ScanForeachCollection(type))
        {
            isReadOnly = true;
            return true;
        }
        return false;
    }

    private static bool IsUtf8ScanForeachCollection(ITypeSymbol type)
    {
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Utf8Lines"))
        {
            return true;
        }
        foreach (var m in type.GetMembers("GetEnumerator").OfType<IMethodSymbol>())
        {
            if (!m.IsStatic && m.Parameters.Length == 0 && VibraniumSymbolHelpers.IsClosedUtf8ViewType(m.ReturnType))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAllowedClosedUtf8ViewExpressionPosition(ExpressionSyntax expr, bool allowForeachCollection)
    {
        SyntaxNode cur = expr;
        while (true)
        {
            if (cur.Parent is ParenthesizedExpressionSyntax paren)
            {
                cur = paren;
                continue;
            }
            if (cur.Parent is ConditionalExpressionSyntax cond && (cond.WhenTrue == cur || cond.WhenFalse == cur))
            {
                cur = cond;
                continue;
            }
            break;
        }

        if (allowForeachCollection && cur.Parent is ForEachStatementSyntax forEach && forEach.Expression == cur)
        {
            return true;
        }

        if (cur.Parent is ReturnStatementSyntax)
        {
            var enclosing = cur.Ancestors().FirstOrDefault(
                static a => a is MemberDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
            return enclosing is MethodDeclarationSyntax or LocalFunctionStatementSyntax;
        }

        if (cur.Parent is ArrowExpressionClauseSyntax arrow &&
            arrow.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
        {
            return true;
        }

        return false;
    }

    private void CheckTreeOperations(SyntaxNode root, SemanticModel model)
    {
        var visitedRoots = new HashSet<SyntaxNode>();

        void InspectSyntaxOperation(SyntaxNode? syntax)
        {
            if (syntax is null || IsInsideEmbeddedAttributeType(syntax, model))
            {
                return;
            }
            var op = model.GetOperation(syntax);
            if (op is null || !visitedRoots.Add(op.Syntax))
            {
                return;
            }
            foreach (var sub in op.DescendantsAndSelf())
            {
                CheckSingleOperation(sub);
            }
        }

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case GlobalStatementSyntax gs:
                    InspectSyntaxOperation(gs.Statement);
                    break;
                case BaseMethodDeclarationSyntax md:
                    InspectSyntaxOperation(md.Body);
                    InspectSyntaxOperation(md.ExpressionBody);
                    if (md is ConstructorDeclarationSyntax cd)
                    {
                        InspectSyntaxOperation(cd.Initializer);
                    }
                    break;
                case AccessorDeclarationSyntax acc:
                    InspectSyntaxOperation(acc.Body);
                    InspectSyntaxOperation(acc.ExpressionBody);
                    break;
                case PropertyDeclarationSyntax prop:
                    InspectSyntaxOperation(prop.ExpressionBody);
                    InspectSyntaxOperation(prop.Initializer);
                    break;
                case IndexerDeclarationSyntax idx:
                    InspectSyntaxOperation(idx.ExpressionBody);
                    break;
                case FieldDeclarationSyntax fld:
                    foreach (var v in fld.Declaration.Variables)
                    {
                        InspectSyntaxOperation(v.Initializer);
                    }
                    break;
                case ParameterSyntax paramSyn when paramSyn.Default is not null:
                    InspectSyntaxOperation(paramSyn.Default);
                    break;
            }
        }
    }

    private void CheckSingleOperation(IOperation op)
    {
        if (IsInsideRejectedGenericPattern(op.Syntax)) return;
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(op.Syntax);

        switch (op)
        {
            case IInvocationOperation { TargetMethod.Name: "AsRef" } call
                when VibraniumSymbolHelpers.IsCoreOptionExtensions(call.TargetMethod.ContainingType) &&
                     (call.Instance ?? call.Arguments.FirstOrDefault()?.Value) is { } slot && !HasSourceStorage(slot):
                // Validate before CFG lowering: a flow capture may store an rvalue in a temporary.
                Emit(VibraniumDiagnosticIds.Unsupported,
                    "Option.AsRef requires a receiver with storage; a temporary Option cannot be borrowed.",
                    VibraniumSymbolHelpers.ToVibraniumSpan(slot.Syntax));
                break;

            case IBinaryOperation { OperatorMethod: { } binOp }:
                if (binOp.ContainingType?.SpecialType != SpecialType.System_String)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Operator '{binOp.Name}' on '{binOp.ContainingType?.Name}' is not supported in Vibranium.",
                        span);
                }
                break;

            case IUnaryOperation { OperatorMethod: { } unOp }:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    $"Unary operator '{unOp.Name}' on '{unOp.ContainingType?.Name}' is not supported in Vibranium.",
                    span);
                break;

            case IIncrementOrDecrementOperation { OperatorMethod: { } incOp }:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    $"Operator '{incOp.Name}' on '{incOp.ContainingType?.Name}' is not supported in Vibranium.",
                    span);
                break;

            case IWithOperation when op.Syntax is not WithExpressionSyntax:
                Emit(VibraniumDiagnosticIds.Unsupported, "'with' expressions are not supported in Vibranium.", span);
                break;

            case IObjectOrCollectionInitializerOperation when op.Syntax.Parent is not BaseObjectCreationExpressionSyntax:
                Emit(
                    VibraniumDiagnosticIds.Unsupported,
                    "Object and collection member initializers are not supported in Vibranium; pass constructor arguments or use collection expressions '[...]'.",
                    span);
                break;

            case IConstantPatternOperation constPat
                when constPat.Value.Type?.SpecialType == SpecialType.System_String ||
                     constPat.Value.ConstantValue is { HasValue: true, Value: string }:
                if (constPat.Parent is not (IPatternCaseClauseOperation or ISwitchExpressionArmOperation))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Constant 'string' pattern matching is banned in Vibranium; compare UTF-8 bytes via '.AsUtf8()' instead.",
                        span);
                }
                break;

            case ICompoundAssignmentOperation { OperatorMethod: { } compOp } compAssign:
                if (!VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(compOp))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"Compound assignment operator '{compOp.Name}' on '{compOp.ContainingType?.Name}' is not supported in Vibranium.",
                        span);
                }
                else if (IsNonRefPropertyOrIndexerOperation(compAssign.Target))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Compound assignment with user-defined 'operator +=' on a non-ref property or indexer is not supported in Vibranium.",
                        span);
                }
                else if (_checkingNonStdUserTree)
                {
                    CheckCrossUnitStdAccess(compOp, span);
                }
                break;

            case IConversionOperation convOp:
                if (convOp.Operand.Type?.SpecialType == SpecialType.System_String &&
                    VibraniumSymbolHelpers.IsSpanType(convOp.Type, out _, out _))
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        "Conversion from 'string' to 'ReadOnlySpan<char>' is banned in Vibranium; use '.AsUtf8()' for UTF-8 bytes.",
                        span);
                }
                else if (convOp.Conversion.MethodSymbol is { } convSym)
                {
                    if (convSym.MethodKind == MethodKind.Conversion && !IsSupportedBuiltInConversionMethod(convSym))
                    {
                        Emit(
                            VibraniumDiagnosticIds.Unsupported,
                            $"Conversion operator '{convSym.Name}' on '{convSym.ContainingType?.Name}' is not supported in Vibranium.",
                            span);
                    }
                    else if (_checkingNonStdUserTree)
                    {
                        CheckCrossUnitStdAccess(convSym, span);
                    }
                }
                break;

            case IPropertyReferenceOperation propRef when _checkingNonStdUserTree:
                CheckCrossUnitStdAccess(propRef.Property, span);
                bool isPropWrite =
                    (op.Parent is ISimpleAssignmentOperation sa && sa.Target == op) ||
                    (op.Parent is ICompoundAssignmentOperation ca && ca.Target == op) ||
                    (op.Parent is IIncrementOrDecrementOperation id && id.Target == op);
                if (isPropWrite && propRef.Property.SetMethod is not null)
                {
                    CheckCrossUnitStdAccess(propRef.Property.SetMethod, span);
                }
                if ((!isPropWrite || op.Parent is ICompoundAssignmentOperation or IIncrementOrDecrementOperation) &&
                    propRef.Property.GetMethod is not null)
                {
                    CheckCrossUnitStdAccess(propRef.Property.GetMethod, span);
                }
                break;

            case IImplicitIndexerReferenceOperation implRef when _checkingNonStdUserTree:
                CheckCrossUnitStdAccess(implRef.IndexerSymbol, span);
                CheckCrossUnitStdAccess(implRef.LengthSymbol, span);
                break;
        }
    }

    private static bool IsSupportedBuiltInConversionMethod(IMethodSymbol convMethod)
    {
        var owner = convMethod.ContainingType;
        if (owner is null)
        {
            return false;
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Option") ||
            VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Result") ||
            VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Vec"))
        {
            return true;
        }
        if (VibraniumSymbolHelpers.IsSpanType(owner, out _, out _))
        {
            return true;
        }
        if (owner.Name == "Index" && owner.ContainingNamespace?.ToDisplayString() == "System")
        {
            return true;
        }
        return false;
    }

    private bool CheckCrossUnitStdAccess(ISymbol? symbol, VibraniumSpan span)
    {
        if (symbol is null || !_checkingNonStdUserTree)
        {
            return false;
        }

        ISymbol? cur = symbol is IMethodSymbol { ReducedFrom: { } reducedFrom }
            ? reducedFrom.OriginalDefinition
            : symbol.OriginalDefinition;

        while (cur is not null &&
               cur is not INamespaceSymbol &&
               cur is not IAssemblySymbol &&
               cur is not IModuleSymbol)
        {
            if (cur is INamedTypeSymbol or IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
            {
                bool inTrustedStd = cur.DeclaringSyntaxReferences.Any(sr => IsTrustedStdTree(sr.SyntaxTree));
                if (inTrustedStd && cur.DeclaredAccessibility != Accessibility.Public)
                {
                    Emit(
                        VibraniumDiagnosticIds.Unsupported,
                        $"'{symbol.Name}' is internal to 'Vibranium.Std' and inaccessible from user code.",
                        span);
                    return true;
                }
            }
            cur = cur.ContainingType;
        }

        return false;
    }

    private static bool IsBannedDelegateOrInterfaceOrObjectType(ITypeSymbol? type) =>
        type is not null &&
        type.TypeKind != TypeKind.Error &&
        (type.TypeKind is TypeKind.Delegate or TypeKind.Interface ||
         type.SpecialType is SpecialType.System_Object or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate);

    private static bool IsDirectTopLevelIfCondition(SyntaxNode syntax)
    {
        SyntaxNode cur = syntax;
        while (cur.Parent is ParenthesizedExpressionSyntax ||
               (cur.Parent is PrefixUnaryExpressionSyntax pref && pref.IsKind(SyntaxKind.LogicalNotExpression)))
        {
            cur = cur.Parent;
        }
        return cur.Parent is IfStatementSyntax ifStmt && ifStmt.Condition == cur;
    }

    private static bool HasSourceStorage(IOperation op) => op switch
    {
        IParenthesizedOperation p => HasSourceStorage(p.Operand),
        IConversionOperation { IsImplicit: true, Conversion.IsIdentity: true } c => HasSourceStorage(c.Operand),
        ILocalReferenceOperation l => !l.Local.IsConst,
        IParameterReferenceOperation or IInstanceReferenceOperation or IArrayElementReferenceOperation => true,
        IFieldReferenceOperation f => !f.Field.IsConst &&
            (f.Instance is null || f.Instance.Type?.IsReferenceType == true || HasSourceStorage(f.Instance)),
        IPropertyReferenceOperation p => p.Property.ReturnsByRef || p.Property.ReturnsByRefReadonly,
        IImplicitIndexerReferenceOperation i => i.IndexerSymbol is IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true },
        IInvocationOperation i => i.TargetMethod.ReturnsByRef || i.TargetMethod.ReturnsByRefReadonly,
        ISimpleAssignmentOperation { IsRef: true } or IConditionalOperation { IsRef: true } => true,
        _ => false,
    };

    private static bool IsNonRefPropertyOrIndexerOperation(IOperation op)
    {
        while (op is IConversionOperation conv &&
               conv.IsImplicit &&
               !conv.Conversion.IsUserDefined &&
               conv.Conversion.MethodSymbol is null &&
               SymbolEqualityComparer.Default.Equals(conv.Type, conv.Operand.Type))
        {
            op = conv.Operand;
        }
        return op switch
        {
            IPropertyReferenceOperation p =>
                !p.Property.ReturnsByRef &&
                !p.Property.ReturnsByRefReadonly &&
                !(p.Property.IsIndexer &&
                  (VibraniumSymbolHelpers.IsVibraniumCoreType(p.Property.ContainingType, "Vec") ||
                   VibraniumSymbolHelpers.IsSpanType(p.Property.ContainingType, out _, out _))),
            IImplicitIndexerReferenceOperation impl =>
                !(VibraniumSymbolHelpers.IsVibraniumCoreType(impl.Instance.Type, "Vec") ||
                  VibraniumSymbolHelpers.IsSpanType(impl.Instance.Type, out _, out _)) &&
                impl.IndexerSymbol is IPropertySymbol { ReturnsByRef: false, ReturnsByRefReadonly: false },
            _ => false,
        };
    }

    private void EmitLinearUnionSwitchBan(SyntaxNode switchSyntax, SemanticModel model)
    {
        foreach (var anchor in LinearUnionSwitchBan.Classify(switchSyntax, model).Reported)
        {
            Emit(VibraniumDiagnosticIds.LinearResourceViolation, LinearUnionSwitchBan.Message, anchor);
        }
    }

    private void Emit(string id, string message, VibraniumSpan span)
    {
        _diagnostics.Add(new VibraniumDiagnostic(id, message, span));
    }
}
