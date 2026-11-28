using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Vibranium.Compiler.Core;
using Vibranium.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Vibranium.Frontend.Roslyn;

public sealed record VibraniumCheckResult(MirModule Module, IReadOnlyList<VibraniumDiagnostic> Diagnostics);

public static class VibraniumFrontend
{
    private const int MaxGenericNestingDepth = 16;

    // The CLI must let the frontend map these source-language boundaries to VIB
    // diagnostics instead of returning an option-dependent compiler warning ID.
    public static bool IsPromotedSemanticWarning(string id) => SyntaxAndSymbolGatekeeper.IsPromotedSemanticWarning(id);

    private sealed class TreeTrustFlags
    {
        public bool IsManagedAdapter;
        public bool IsSysCatalog;
        public bool IsOfficialStd;
    }

    private static readonly object s_trustGate = new();
    private static readonly ConditionalWeakTable<SyntaxTree, TreeTrustFlags> s_registeredTreeTrust = new();

    public static void RegisterTrustedTrees(
        CSharpCompilation compilation,
        ISet<SyntaxTree> trustedManagedAdapterTrees,
        ISet<SyntaxTree> trustedSysCatalogTrees,
        ISet<SyntaxTree> officialStdTrees,
        string? trustedStdDirectory = null)
    {
        if (compilation is null) throw new ArgumentNullException(nameof(compilation));
        bool IsWithinSelectedStd(SyntaxTree tree, string? relativePath = null)
        {
            if (trustedStdDirectory is null || string.IsNullOrEmpty(tree.FilePath)) return false;
            string root = Path.GetFullPath(trustedStdDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string source = Path.GetFullPath(tree.FilePath);
            if (!source.StartsWith(root, StringComparison.Ordinal)) return false;
            string relative = source.Substring(root.Length);
            return relativePath is null || VibraniumSymbolHelpers.NormalizePathSeparators(relative) == relativePath;
        }
        lock (s_trustGate)
        {
            static TreeTrustFlags GetOrCreate(SyntaxTree t)
            {
                if (!s_registeredTreeTrust.TryGetValue(t, out var flags))
                {
                    flags = new TreeTrustFlags();
                    s_registeredTreeTrust.Add(t, flags);
                }
                return flags;
            }

            if (trustedManagedAdapterTrees is not null)
            {
                foreach (var t in trustedManagedAdapterTrees)
                {
                    if (trustedStdDirectory is not null ? IsWithinSelectedStd(t, "Sys/Sys.Managed.cs") : VibraniumSymbolHelpers.IsOfficialStdSysManagedTreeStructure(t)) GetOrCreate(t).IsManagedAdapter = true;
                }
            }
            if (trustedSysCatalogTrees is not null)
            {
                foreach (var t in trustedSysCatalogTrees)
                {
                    if (trustedStdDirectory is not null ? IsWithinSelectedStd(t, "Sys/Sys.cs") : VibraniumSymbolHelpers.IsOfficialStdSysCatalogTreeStructure(t)) GetOrCreate(t).IsSysCatalog = true;
                }
            }
            if (officialStdTrees is not null)
            {
                foreach (var t in officialStdTrees)
                {
                    if (trustedStdDirectory is not null ? IsWithinSelectedStd(t) : TrustedRuntimeSources.IsStd(t)) GetOrCreate(t).IsOfficialStd = true;
                }
            }
        }
    }

    public static bool HasRegisteredTrustedTrees(CSharpCompilation compilation)
    {
        if (compilation is null) return false;
        lock (s_trustGate)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (s_registeredTreeTrust.TryGetValue(tree, out _))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public static bool IsOfficialStdSysManagedTreeStructure(SyntaxTree tree) =>
        VibraniumSymbolHelpers.IsOfficialStdSysManagedTreeStructure(tree);

    public static bool IsOfficialStdSysCatalogTreeStructure(SyntaxTree tree) =>
        VibraniumSymbolHelpers.IsOfficialStdSysCatalogTreeStructure(tree);

    public static bool IsAuthenticatedRuntimeSource(SyntaxTree tree) => TrustedRuntimeSources.Authenticate(tree);

    // Driver entry points must recognize syntax cuts before Roslyn errors in the
    // rejected declaration/file can replace the canonical Vibranium diagnostic.
    public static bool HasDeclarationCuts(CSharpCompilation compilation)
    {
        var managed = new HashSet<SyntaxTree>();
        var catalog = new HashSet<SyntaxTree>();
        lock (s_trustGate)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!s_registeredTreeTrust.TryGetValue(tree, out var flags)) continue;
                if (flags.IsManagedAdapter) managed.Add(tree);
                if (flags.IsSysCatalog) catalog.Add(tree);
            }
        }
        return new SubsetDeclarationCuts(compilation, managed, catalog).Diagnostics.Count > 0;
    }

    public static VibraniumCheckResult CompileAndCheck(
        CSharpCompilation compilation,
        ISet<SyntaxTree>? trustedManagedAdapterTrees = null,
        ISet<SyntaxTree>? trustedSysCatalogTrees = null,
        ISet<SyntaxTree>? officialStdTrees = null)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (trustedManagedAdapterTrees is null && trustedSysCatalogTrees is null && officialStdTrees is null)
        {
            lock (s_trustGate)
            {
                HashSet<SyntaxTree>? regManaged = null;
                HashSet<SyntaxTree>? regCatalog = null;
                HashSet<SyntaxTree>? regStd = null;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (s_registeredTreeTrust.TryGetValue(tree, out var flags))
                    {
                        if (flags.IsManagedAdapter) (regManaged ??= new HashSet<SyntaxTree>()).Add(tree);
                        if (flags.IsSysCatalog) (regCatalog ??= new HashSet<SyntaxTree>()).Add(tree);
                        if (flags.IsOfficialStd) (regStd ??= new HashSet<SyntaxTree>()).Add(tree);
                    }
                }
                trustedManagedAdapterTrees = regManaged;
                trustedSysCatalogTrees = regCatalog;
                officialStdTrees = regStd;
            }
        }

        // A selected CLI sysroot confers path-based trust. Without a sysroot
        // (Analyzer/MSBuild), only the pinned audited source hashes confer it.
        bool HasRegisteredTrust(SyntaxTree tree, Func<TreeTrustFlags, bool> accepts)
        {
            lock (s_trustGate)
                return s_registeredTreeTrust.TryGetValue(tree, out var flags) && accepts(flags);
        }
        trustedManagedAdapterTrees = trustedManagedAdapterTrees is null ? null : new HashSet<SyntaxTree>(trustedManagedAdapterTrees.Where(tree =>
            HasRegisteredTrust(tree, flags => flags.IsManagedAdapter) || VibraniumSymbolHelpers.IsOfficialStdSysManagedTreeStructure(tree)));
        trustedSysCatalogTrees = trustedSysCatalogTrees is null ? null : new HashSet<SyntaxTree>(trustedSysCatalogTrees.Where(tree =>
            HasRegisteredTrust(tree, flags => flags.IsSysCatalog) || VibraniumSymbolHelpers.IsOfficialStdSysCatalogTreeStructure(tree)));
        officialStdTrees = officialStdTrees is null ? null : new HashSet<SyntaxTree>(officialStdTrees.Where(tree =>
            HasRegisteredTrust(tree, flags => flags.IsOfficialStd) || TrustedRuntimeSources.IsStd(tree)));

        var module = new MirModule();
        bool isBuildingVibraniumStdItself = string.Equals(compilation.AssemblyName, "Vibranium.Std", StringComparison.Ordinal) &&
            (TrustedRuntimeSources.IsOfficialStdCompilation(compilation) ||
             trustedSysCatalogTrees is { Count: > 0 } && compilation.SyntaxTrees.All(tree =>
                 officialStdTrees?.Contains(tree) == true || trustedManagedAdapterTrees?.Contains(tree) == true ||
                 tree.GetRoot() is CompilationUnitSyntax { Members.Count: 0 }));
        var diagnostics = new List<VibraniumDiagnostic>();
        if (compilation.AssemblyName is "Vibranium.Core" or "Vibranium.Std" && !isBuildingVibraniumStdItself)
        {
            var tree = compilation.SyntaxTrees.FirstOrDefault();
            var location = tree?.GetRoot().GetFirstToken().GetLocation();
            diagnostics.Add(new VibraniumDiagnostic(VibraniumDiagnosticIds.Unsupported,
                "The assembly names Vibranium.Core and Vibranium.Std are reserved for the Vibranium runtime and standard library.",
                location is null ? new VibraniumSpan(string.Empty, 0, 0, 1, 1) : VibraniumSymbolHelpers.ToVibraniumSpan(location)));
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, trustedManagedAdapterTrees)) continue;
            if (!IsPurelyEmbeddedTree(tree, compilation))
            {
                CfgMirLowerer.CheckConditionalRefReassignments(tree.GetRoot(), diagnostics);
                CfgMirLowerer.CheckFieldInitializerControlFlow(tree.GetRoot(), diagnostics);
            }
        }

        var declarationCuts = new SubsetDeclarationCuts(compilation, trustedManagedAdapterTrees, trustedSysCatalogTrees);
        declarationCuts.CheckUnionPatternCuts(compilation, trustedManagedAdapterTrees);
        var userTypes = CollectUserDeclaredTypes(compilation);

        var registeredTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var typeQueue = new Queue<INamedTypeSymbol>();

        void EnqueueType(INamedTypeSymbol t)
        {
            if (t.TypeKind is not (TypeKind.Class or TypeKind.Struct) || t.IsStatic) return;
            bool inCurrentAsm = SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly);
            bool inVibraniumStdAsm = string.Equals(t.ContainingAssembly?.Name, "Vibranium.Std", StringComparison.Ordinal);
            if (!inCurrentAsm && !inVibraniumStdAsm) return;
            if (VibraniumSymbolHelpers.IsEmbeddedOrCompilerSynthesizedType(t)) return;
            if (ContainsOpenTypeParameter(t)) return;
            if (GetTypeNestingDepth(t) > MaxGenericNestingDepth)
            {
                var loc = t.Locations.FirstOrDefault(l => l.IsInSource);
                var span = loc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(loc) : new VibraniumSpan(string.Empty, 0, 0, 1, 1);
                diagnostics.Add(new VibraniumDiagnostic(
                    VibraniumDiagnosticIds.Unsupported,
                    $"Polymorphic recursion or excessive generic type nesting in '{t.Name}' is banned in Vibranium.",
                    span));
                return;
            }
            if (registeredTypes.Add(t))
            {
                typeQueue.Enqueue(t);
            }
        }

        foreach (var t in userTypes)
        {
            if (!ContainsOpenTypeParameter(t))
            {
                EnqueueType(t);
            }
        }

        if (!isBuildingVibraniumStdItself)
        {
            RegisterReferencedVibraniumStdConcreteTypes(compilation, EnqueueType);
        }

        DrainTypeRegistrationQueue(module, typeQueue, EnqueueType, compilation, diagnostics, trustedManagedAdapterTrees);

        // Run the full Gatekeeper across all declared types and trees (including Vibranium.Std T1 + Sys.cs),
        // skipping only compiler-embedded attribute stubs and the explicitly verified managed BCL adapter tree.
        var gatekeeper = new SyntaxAndSymbolGatekeeper(
            compilation,
            module,
            diagnostics,
            trustedManagedAdapterTrees,
            trustedSysCatalogTrees,
            officialStdTrees,
            isBuildingVibraniumStdItself,
            declarationCuts);
        foreach (var t in userTypes)
        {
            if (!declarationCuts.Rejects(t)) gatekeeper.CheckDeclaredTypeSymbol(t);
        }
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (IsPurelyEmbeddedTree(tree, compilation) ||
                VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, trustedManagedAdapterTrees))
            {
                continue;
            }
            var model = compilation.GetSemanticModel(tree);
            gatekeeper.CheckTree(tree, model);
        }

        var entryPoint = compilation.GetEntryPoint(CancellationToken.None);

        var loweredMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        var methodQueue = new Queue<IMethodSymbol>();

        void EnqueueMethod(IMethodSymbol m)
        {
            if (declarationCuts.Rejects(m) || m.ContainingType is not null && declarationCuts.Rejects(m.ContainingType)) return;
            bool inCurrentAsm = SymbolEqualityComparer.Default.Equals(m.ContainingAssembly, compilation.Assembly);
            bool inVibraniumStdAsm = string.Equals(m.ContainingAssembly?.Name, "Vibranium.Std", StringComparison.Ordinal);
            if (!inCurrentAsm && !inVibraniumStdAsm) return;
            if (m.ContainingType is not null && VibraniumSymbolHelpers.IsEmbeddedOrCompilerSynthesizedType(m.ContainingType)) return;
            if (ContainsOpenTypeParameter(m)) return;
            if (VibraniumSymbolHelpers.IsDeclaredOnlyInTrustedManagedAdapter(m, trustedManagedAdapterTrees)) return;

            if (GetMethodNestingDepth(m) > MaxGenericNestingDepth)
            {
                var loc = m.Locations.FirstOrDefault(l => l.IsInSource);
                var span = loc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(loc) : new VibraniumSpan(string.Empty, 0, 0, 1, 1);
                diagnostics.Add(new VibraniumDiagnostic(
                    VibraniumDiagnosticIds.Unsupported,
                    $"Polymorphic recursion in generic method '{m.Name}' is banned in Vibranium.",
                    span));
                return;
            }

            string? nativeSym = VibraniumSymbolHelpers.GetNativeSymbol(m);
            if (nativeSym is not null && VibraniumSymbolHelpers.TryValidateSysNativeMethod(m, nativeSym, compilation, trustedSysCatalogTrees))
            {
                module.UsesStdNative = true;
                string sysKey = VibraniumSymbolHelpers.GetMethodKey(m);
                if (!module.Signatures.ContainsKey(sysKey))
                {
                    module.Signatures[sysKey] = BuildSignature(m, module, nativeSym, isInternalHelper: true);
                }
                return;
            }

            if (m.IsExtern || VibraniumSymbolHelpers.HasInteropImportAttribute(m))
            {
                return;
            }

            if (m.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion &&
                !VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(m))
            {
                return;
            }

            if (m.AssociatedSymbol is IPropertySymbol assocAutoProp &&
                VibraniumSymbolHelpers.IsAutoOrInitProperty(assocAutoProp))
            {
                return;
            }

            bool isStdHelper = !isBuildingVibraniumStdItself &&
                (inVibraniumStdAsm ||
                 (m.ContainingType is not null && IsSolelyInOfficialStdSource(m.ContainingType, officialStdTrees, trustedManagedAdapterTrees)));

            string key = VibraniumSymbolHelpers.GetMethodKey(m);
            if (!module.Signatures.ContainsKey(key))
            {
                module.Signatures[key] = BuildSignature(m, module, nativeSymbol: null, isInternalHelper: isStdHelper);
            }

            // Metadata-only Vibranium.Std references (e.g. Vibranium.Analyzer running on downstream .csproj) only need signatures
            if (!inCurrentAsm)
            {
                return;
            }

            if (loweredMethodKeys.Add(key))
            {
                methodQueue.Enqueue(m);
            }
        }

        if (entryPoint is not null && SymbolEqualityComparer.Default.Equals(entryPoint.ContainingAssembly, compilation.Assembly))
        {
            EnqueueMethod(entryPoint);
            module.EntryPointKey = VibraniumSymbolHelpers.GetMethodKey(entryPoint);
        }

        foreach (var typeSym in userTypes)
        {
            if (!isBuildingVibraniumStdItself && IsSolelyInOfficialStdSource(typeSym, officialStdTrees, trustedManagedAdapterTrees)) continue;
            if (ContainsOpenTypeParameter(typeSym)) continue;
            if (typeSym.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;

            foreach (var member in typeSym.GetMembers())
            {
                if (member is IMethodSymbol method)
                {
                    if (ContainsOpenTypeParameter(method)) continue;
                    if (VibraniumSymbolHelpers.IsDeclaredOnlyInTrustedManagedAdapter(method, trustedManagedAdapterTrees)) continue;

                    switch (method.MethodKind)
                    {
                        case MethodKind.Ordinary:
                        case MethodKind.PropertyGet:
                        case MethodKind.PropertySet:
                        case MethodKind.UserDefinedOperator:
                            if (!method.IsImplicitlyDeclared || method.Name == "<Main>$")
                            {
                                EnqueueMethod(method);
                            }
                            break;

                        case MethodKind.Constructor when !method.IsStatic:
                            if (!method.IsImplicitlyDeclared || (typeSym.TypeKind == TypeKind.Class && !typeSym.IsStatic))
                            {
                                EnqueueMethod(method);
                            }
                            break;
                    }
                }
            }
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (IsPurelyEmbeddedTree(tree, compilation) ||
                VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, trustedManagedAdapterTrees))
            {
                continue;
            }
            if (!isBuildingVibraniumStdItself && officialStdTrees is not null && officialStdTrees.Contains(tree))
            {
                continue;
            }
            var model = compilation.GetSemanticModel(tree);
            foreach (var localFnSyntax in tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                if (!localFnSyntax.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
                if (model.GetDeclaredSymbol(localFnSyntax) is IMethodSymbol localMethod &&
                    !ContainsOpenTypeParameter(localMethod))
                {
                    EnqueueMethod(localMethod);
                }
            }
        }

        if (entryPoint is null)
        {
            var topLevelMain = FindTopLevelMain(compilation);
            if (topLevelMain is not null)
            {
                EnqueueMethod(topLevelMain);
                module.EntryPointKey = VibraniumSymbolHelpers.GetMethodKey(topLevelMain);
                entryPoint = topLevelMain;
            }
        }

        while (methodQueue.Count > 0 || typeQueue.Count > 0)
        {
            DrainTypeRegistrationQueue(module, typeQueue, EnqueueType, compilation, diagnostics, trustedManagedAdapterTrees);
            if (methodQueue.Count == 0) break;

            var method = methodQueue.Dequeue();
            var fn = LowerMethodSymbol(compilation, module, method, entryPoint, diagnostics, EnqueueMethod, EnqueueType, trustedManagedAdapterTrees, gatekeeper, declarationCuts);
            if (fn is not null)
            {
                module.Functions.Add(fn);
            }
        }

        DrainTypeRegistrationQueue(module, typeQueue, EnqueueType, compilation, diagnostics, trustedManagedAdapterTrees);

        diagnostics.AddRange(BorrowAndMoveAnalyzer.AnalyzeModule(module));

        AnalyzeOpenGenericDefinitions(
            compilation,
            module,
            userTypes,
            isBuildingVibraniumStdItself,
            diagnostics,
            trustedManagedAdapterTrees,
            officialStdTrees,
            gatekeeper,
            declarationCuts);

        diagnostics.RemoveAll(diagnostic => declarationCuts.Rejects(diagnostic.Span) &&
            !CfgMirLowerer.IsFieldInitializerCutMessage(diagnostic.Message) &&
            diagnostic.Message is not CfgMirLowerer.ConditionalRefReassignmentMessage and
                not "The assembly names Vibranium.Core and Vibranium.Std are reserved for the Vibranium runtime and standard library.");
        diagnostics.AddRange(declarationCuts.Diagnostics);

        var sortedDiagnostics = DeduplicateAndSort(diagnostics);
        return new VibraniumCheckResult(module, sortedDiagnostics);
    }

    private static bool IsSolelyInOfficialStdSource(
        INamedTypeSymbol type,
        ISet<SyntaxTree>? officialStdTrees,
        ISet<SyntaxTree>? trustedManagedAdapterTrees)
    {
        if ((officialStdTrees is null || officialStdTrees.Count == 0) &&
            (trustedManagedAdapterTrees is null || trustedManagedAdapterTrees.Count == 0))
        {
            return false;
        }
        if (type.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }
        return type.DeclaringSyntaxReferences.All(sr =>
            (officialStdTrees is not null && officialStdTrees.Contains(sr.SyntaxTree)) ||
            VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(sr.SyntaxTree, trustedManagedAdapterTrees));
    }

    private static void RegisterReferencedVibraniumStdConcreteTypes(
        CSharpCompilation compilation,
        Action<INamedTypeSymbol> enqueueType)
    {
        foreach (var refSym in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!string.Equals(refSym.Name, "Vibranium.Std", StringComparison.Ordinal))
            {
                continue;
            }
            var stdTypes = new List<INamedTypeSymbol>();
            CollectNamespaceTypes(refSym.GlobalNamespace, stdTypes);
            foreach (var t in stdTypes)
            {
                if (!ContainsOpenTypeParameter(t))
                {
                    enqueueType(t);
                }
            }
        }
    }

    private static int GetTypeNestingDepth(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            int maxArg = 0;
            foreach (var ta in named.TypeArguments)
            {
                int d = GetTypeNestingDepth(ta);
                if (d > maxArg) maxArg = d;
            }
            return 1 + maxArg;
        }
        return 0;
    }

    private static int GetMethodNestingDepth(IMethodSymbol method)
    {
        int maxDepth = method.ContainingType is not null ? GetTypeNestingDepth(method.ContainingType) : 0;
        foreach (var ta in method.TypeArguments)
        {
            int d = GetTypeNestingDepth(ta);
            if (d > maxDepth) maxDepth = d;
        }
        return maxDepth;
    }

    private static List<VibraniumDiagnostic> DeduplicateAndSort(IEnumerable<VibraniumDiagnostic> items)
    {
        var seen = new HashSet<(string Id, string FilePath, int Start, int Length, string Cut)>();
        var list = new List<VibraniumDiagnostic>();
        foreach (var d in items)
        {
            // Distinct subset cuts may deliberately share a token (e.g. an
            // optional nint parameter is both C12 and C17). Ordinary diagnostics
            // keep their existing ID/span precedence and cascade suppression.
            string cut = SubsetDeclarationCuts.IsCanonicalCutMessage(d.Message) || d.Message == CfgMirLowerer.ConditionalRefReassignmentMessage ||
                CfgMirLowerer.IsFieldInitializerCutMessage(d.Message)
                ? d.Message : string.Empty;
            if (seen.Add((d.Id, d.Span.FilePath ?? string.Empty, d.Span.Start, d.Span.Length, cut)))
            {
                list.Add(d);
            }
        }
        list.Sort(static (a, b) =>
        {
            int cmp = string.CompareOrdinal(a.Span.FilePath, b.Span.FilePath);
            if (cmp != 0) return cmp;
            cmp = a.Span.Start.CompareTo(b.Span.Start);
            if (cmp != 0) return cmp;
            cmp = a.Span.Length.CompareTo(b.Span.Length);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Id, b.Id);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Message, b.Message);
        });
        return list;
    }

    private static void AnalyzeOpenGenericDefinitions(
        CSharpCompilation compilation,
        MirModule baseModule,
        List<INamedTypeSymbol> userTypes,
        bool isBuildingVibraniumStdItself,
        List<VibraniumDiagnostic> diagnostics,
        ISet<SyntaxTree>? trustedManagedAdapterTrees,
        ISet<SyntaxTree>? officialStdTrees,
        SyntaxAndSymbolGatekeeper gatekeeper,
        SubsetDeclarationCuts declarationCuts)
    {
        var localMethods = compilation.SyntaxTrees
            .Where(tree => !VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, trustedManagedAdapterTrees))
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>()
                .Select(syntax => compilation.GetSemanticModel(tree).GetDeclaredSymbol(syntax)).OfType<IMethodSymbol>())
            .ToArray();
        // Synthesized top-level Program is intentionally omitted from the
        // ordinary user-type list; its generic local definitions still count.
        foreach (var typeSym in userTypes.Concat(localMethods.Select(method => method.ContainingType))
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            if (!isBuildingVibraniumStdItself && IsSolelyInOfficialStdSource(typeSym, officialStdTrees, trustedManagedAdapterTrees)) continue;
            if (typeSym.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;
            var methods = typeSym.GetMembers().OfType<IMethodSymbol>().Concat(
                localMethods.Where(method => SymbolEqualityComparer.Default.Equals(method.ContainingType, typeSym)));
            foreach (var method in methods)
            {
                if (method.IsImplicitlyDeclared) continue;
                if (VibraniumSymbolHelpers.IsDeclaredOnlyInTrustedManagedAdapter(method, trustedManagedAdapterTrees)) continue;
                if (method.IsExtern || VibraniumSymbolHelpers.HasInteropImportAttribute(method)) continue;
                if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion &&
                    !VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(method)) continue;
                if (method.AssociatedSymbol is IPropertySymbol openProp &&
                    VibraniumSymbolHelpers.IsAutoOrInitProperty(openProp)) continue;
                if (!ContainsOpenTypeParameter(typeSym) && !ContainsOpenTypeParameter(method)) continue;

                var fn = LowerMethodSymbol(
                    compilation,
                    baseModule,
                    method,
                    entryPoint: null,
                    diagnostics,
                    enqueueMethod: static _ => { },
                    enqueueType: static _ => { },
                    trustedManagedAdapterTrees,
                    gatekeeper,
                    declarationCuts);
                if (fn is not null)
                {
                    diagnostics.AddRange(BorrowAndMoveAnalyzer.AnalyzeFunction(baseModule, fn));
                }
            }
        }
    }

    private static MirFunction? LowerMethodSymbol(
        CSharpCompilation compilation,
        MirModule module,
        IMethodSymbol method,
        IMethodSymbol? entryPoint,
        List<VibraniumDiagnostic> diagnostics,
        Action<IMethodSymbol> enqueueMethod,
        Action<INamedTypeSymbol> enqueueType,
        ISet<SyntaxTree>? trustedManagedAdapterTrees,
        SyntaxAndSymbolGatekeeper gatekeeper,
        SubsetDeclarationCuts declarationCuts)
    {
        if (gatekeeper.IsRejectedGenericPatternBody(method)) return null;
        // The gatekeeper already rejected this declaration. Keep its registered
        // signature for caller analysis, but do not diagnose its unsupported body.
        if (method.MethodKind == MethodKind.LocalFunction && method.OriginalDefinition.DeclaringSyntaxReferences.Any(
            syntax => syntax.GetSyntax() is LocalFunctionStatementSyntax { Parent: SwitchSectionSyntax }))
        {
            return null;
        }
        string key = VibraniumSymbolHelpers.GetMethodKey(method);
        if (!module.Signatures.TryGetValue(key, out var signature))
        {
            signature = BuildSignature(method, module);
            module.Signatures[key] = signature;
        }

        var loc = method.Locations.FirstOrDefault(l => l.IsInSource && !VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(l.SourceTree, trustedManagedAdapterTrees))
            ?? method.ContainingType?.Locations.FirstOrDefault(l => l.IsInSource && !VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(l.SourceTree, trustedManagedAdapterTrees));
        var fnSpan = loc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(loc) : new VibraniumSpan(string.Empty, 0, 0, 1, 1);

        var typeMap = BuildTypeSubstitutionMap(method);
        var lowerer = new CfgMirLowerer(compilation, module, diagnostics, enqueueMethod, enqueueType, typeMap, declarationCuts);

        if (method.MethodKind == MethodKind.Constructor && !method.IsStatic && method.ContainingType is not null)
        {
            var ctorCfg = TryCreateControlFlowGraph(compilation, method.OriginalDefinition, trustedManagedAdapterTrees);
            return lowerer.LowerConstructor(method, method.ContainingType, signature, ctorCfg, fnSpan);
        }

        var cfg = TryCreateControlFlowGraph(compilation, method.OriginalDefinition, trustedManagedAdapterTrees);
        if (cfg is null)
        {
            return null;
        }

        foreach (var localFn in cfg.LocalFunctions)
        {
            if (localFn.IsStatic && !localFn.IsGenericMethod && !ContainsOpenTypeParameter(method))
            {
                enqueueMethod(localFn);
            }
        }

        bool isEntry = entryPoint is not null && SymbolEqualityComparer.Default.Equals(method, entryPoint);
        return lowerer.LowerRegularMethod(method, signature, cfg, isEntry, fnSpan);
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        CSharpCompilation compilation,
        IMethodSymbol origMethod,
        ISet<SyntaxTree>? trustedManagedAdapterTrees)
    {
        if (origMethod.MethodKind == MethodKind.LocalFunction && origMethod.ContainingSymbol is IMethodSymbol enclosingMethod)
        {
            var parentCfg = TryCreateControlFlowGraph(compilation, enclosingMethod.OriginalDefinition, trustedManagedAdapterTrees);
            if (parentCfg is not null)
            {
                foreach (var lf in parentCfg.LocalFunctions)
                {
                    if (SymbolEqualityComparer.Default.Equals(lf.OriginalDefinition, origMethod.OriginalDefinition))
                    {
                        return parentCfg.GetLocalFunctionControlFlowGraph(lf);
                    }
                }
            }
            return null;
        }

        foreach (var syntaxRef in origMethod.DeclaringSyntaxReferences)
        {
            if (VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(syntaxRef.SyntaxTree, trustedManagedAdapterTrees))
            {
                continue;
            }

            var syntax = syntaxRef.GetSyntax();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);

            switch (syntax)
            {
                case BaseMethodDeclarationSyntax baseMethodSyntax:
                    if (baseMethodSyntax.Body is null && baseMethodSyntax.ExpressionBody is null)
                    {
                        continue;
                    }
                    return ControlFlowGraph.Create(baseMethodSyntax, model);

                case LocalFunctionStatementSyntax localFnSyntax:
                    if (localFnSyntax.Body is null && localFnSyntax.ExpressionBody is null)
                    {
                        continue;
                    }
                    return ControlFlowGraph.Create(localFnSyntax, model);

                case AccessorDeclarationSyntax accessorSyntax:
                    if (accessorSyntax.Body is null && accessorSyntax.ExpressionBody is null)
                    {
                        continue;
                    }
                    return ControlFlowGraph.Create(accessorSyntax, model);

                case ArrowExpressionClauseSyntax arrowSyntax when arrowSyntax.Parent is BasePropertyDeclarationSyntax:
                    return ControlFlowGraph.Create(arrowSyntax, model);

                case PropertyDeclarationSyntax { ExpressionBody: { } propArrow }:
                    return ControlFlowGraph.Create(propArrow, model);

                case IndexerDeclarationSyntax { ExpressionBody: { } idxArrow }:
                    return ControlFlowGraph.Create(idxArrow, model);

                case CompilationUnitSyntax compUnit:
                    return ControlFlowGraph.Create(compUnit, model);
            }
        }

        return null;
    }

    internal static MirFunctionSignature BuildSignature(
        IMethodSymbol method,
        MirModule module,
        string? nativeSymbol = null,
        bool isInternalHelper = false)
    {
        string key = VibraniumSymbolHelpers.GetMethodKey(method);
        string displayName = $"{method.ContainingType?.Name}.{method.Name}";

        var lifetimeAllowedParams = VibraniumSymbolHelpers.GetReturnLifetimeOfParameters(method);
        var paramSigs = new List<MirParamSig>();

        bool isCtorFactory = method.MethodKind == MethodKind.Constructor && !method.IsStatic;
        MirType returnType = isCtorFactory && method.ContainingType is not null
            ? VibraniumSymbolHelpers.MapType(method.ContainingType, module)
            : VibraniumSymbolHelpers.MapType(method.ReturnType, module);

        bool returnsByRef = !isCtorFactory && (method.ReturnsByRef || method.ReturnsByRefReadonly);
        bool returnRefMutable = !isCtorFactory && method.ReturnsByRef && !method.ReturnsByRefReadonly;
        bool returnsBorrowOrView = returnsByRef || module.IsViewLike(returnType) || (!isCtorFactory && method.ReturnType.IsRefLikeType);

        bool hasImplicitThis = !method.IsStatic && !isCtorFactory && method.ContainingType is not null;

        if (hasImplicitThis)
        {
            var recvType = VibraniumSymbolHelpers.MapType(method.ContainingType!, module);
            var recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(method);
            bool unscopedRef = VibraniumSymbolHelpers.HasUnscopedRefAttribute(method);
            if (!unscopedRef && method.AssociatedSymbol is IPropertySymbol assocProp)
            {
                unscopedRef = VibraniumSymbolHelpers.HasUnscopedRefAttribute(assocProp);
            }

            bool recvScoped = lifetimeAllowedParams is not null && !lifetimeAllowedParams.Contains("this");
            paramSigs.Add(new MirParamSig("this", recvType, recvPass, recvScoped, unscopedRef));
        }

        int unannotatedBorrowedParamCount = 0;
        if (returnsBorrowOrView && lifetimeAllowedParams is null && !hasImplicitThis)
        {
            foreach (var p in method.Parameters)
            {
                if (p.ScopedKind != ScopedKind.None) continue;
                var pType = VibraniumSymbolHelpers.MapType(p.Type, module);
                var pass = VibraniumSymbolHelpers.GetParamPassKind(p);
                if (pass is MirPassKind.In or MirPassKind.Ref or MirPassKind.Out ||
                    module.IsViewLike(pType) ||
                    p.Type.IsRefLikeType)
                {
                    unannotatedBorrowedParamCount++;
                }
            }
        }

        foreach (var p in method.Parameters)
        {
            var pType = VibraniumSymbolHelpers.MapType(p.Type, module);
            var pass = VibraniumSymbolHelpers.GetParamPassKind(p);
            bool isScoped = p.ScopedKind != ScopedKind.None;
            if (lifetimeAllowedParams is not null)
            {
                if (!lifetimeAllowedParams.Contains(p.Name))
                {
                    isScoped = true;
                }
            }
            else if (returnsBorrowOrView)
            {
                if (hasImplicitThis || unannotatedBorrowedParamCount >= 2)
                {
                    isScoped = true;
                }
            }
            paramSigs.Add(new MirParamSig(p.Name, pType, pass, isScoped, false));
        }

        return new MirFunctionSignature(
            key,
            displayName,
            paramSigs,
            returnType,
            returnsByRef,
            returnRefMutable,
            NativeSymbol: nativeSymbol,
            IsInternalHelper: isInternalHelper);
    }

    private static void DrainTypeRegistrationQueue(
        MirModule module,
        Queue<INamedTypeSymbol> queue,
        Action<INamedTypeSymbol> enqueueType,
        CSharpCompilation compilation,
        List<VibraniumDiagnostic> diagnostics,
        ISet<SyntaxTree>? trustedManagedAdapterTrees)
    {
        while (queue.Count > 0)
        {
            var typeSym = queue.Dequeue();
            if (typeSym.IsStatic) continue;
            string typeKey = VibraniumSymbolHelpers.GetTypeKey(typeSym);

            var fields = new List<MirFieldDef>();
            foreach (var f in VibraniumSymbolHelpers.GetInstanceFields(typeSym, compilation))
            {
                if (f.Type is INamedTypeSymbol fNamed) enqueueType(fNamed);
                fields.Add(new MirFieldDef(f.Name, VibraniumSymbolHelpers.MapType(f.Type, module), f.IsReadOnly));
            }

            if (typeSym.TypeKind == TypeKind.Class)
            {
                bool isDisp = VibraniumSymbolHelpers.ImplementsIDisposable(typeSym);
                string? disposeKey = null;
                if (isDisp)
                {
                    var dispMethod = typeSym.GetMembers("Dispose")
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault(m =>
                            !m.IsStatic &&
                            m.MethodKind == MethodKind.Ordinary &&
                            m.DeclaredAccessibility == Accessibility.Public &&
                            m.Parameters.Length == 0 &&
                            m.TypeParameters.Length == 0 &&
                            m.ReturnsVoid);
                    if (dispMethod is not null)
                    {
                        disposeKey = VibraniumSymbolHelpers.GetMethodKey(dispMethod);
                    }
                    else if (!VibraniumSymbolHelpers.IsDeclaredOnlyInTrustedManagedAdapter(typeSym, trustedManagedAdapterTrees) &&
                             !typeSym.GetMembers().OfType<IMethodSymbol>().Any(m => m.MethodKind == MethodKind.ExplicitInterfaceImplementation && m.Locations.Any(l => l.IsInSource)) &&
                             !diagnostics.Any(d => d.Id == VibraniumDiagnosticIds.Unsupported))
                    {
                        var loc = typeSym.Locations.FirstOrDefault(l => l.IsInSource && !VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(l.SourceTree, trustedManagedAdapterTrees));
                        var span = loc is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(loc) : new VibraniumSpan(string.Empty, 0, 0, 1, 1);
                        diagnostics.Add(new VibraniumDiagnostic(
                            VibraniumDiagnosticIds.Unsupported,
                            "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                            span));
                    }
                }
                module.Classes[typeKey] = new MirClassDef(typeKey, isDisp && disposeKey is not null, disposeKey, fields);
            }
            else if (typeSym.TypeKind == TypeKind.Struct)
            {
                bool isDisp = VibraniumSymbolHelpers.ImplementsIDisposable(typeSym);
                module.Structs[typeKey] = new MirStructDef(typeKey, typeSym.IsReadOnly, typeSym.IsRefLikeType, isDisp, fields);
            }
        }
    }

    private static ImmutableDictionary<ITypeParameterSymbol, ITypeSymbol> BuildTypeSubstitutionMap(IMethodSymbol method)
    {
        var builder = ImmutableDictionary.CreateBuilder<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);

        var curType = method.ContainingType;
        while (curType is not null)
        {
            var orig = curType.OriginalDefinition;
            for (int i = 0; i < orig.TypeParameters.Length && i < curType.TypeArguments.Length; i++)
            {
                builder[orig.TypeParameters[i]] = curType.TypeArguments[i];
            }
            curType = curType.ContainingType;
        }

        IMethodSymbol? curMethod = method;
        while (curMethod is not null)
        {
            var origM = curMethod.OriginalDefinition;
            for (int i = 0; i < origM.TypeParameters.Length && i < curMethod.TypeArguments.Length; i++)
            {
                builder[origM.TypeParameters[i]] = curMethod.TypeArguments[i];
            }
            curMethod = curMethod.ContainingSymbol as IMethodSymbol;
        }

        return builder.ToImmutable();
    }

    private static bool ContainsOpenTypeParameter(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol) return true;
        if (type is INamedTypeSymbol named)
        {
            if (named.ContainingType is not null && ContainsOpenTypeParameter(named.ContainingType))
            {
                return true;
            }
            foreach (var arg in named.TypeArguments)
            {
                if (ContainsOpenTypeParameter(arg)) return true;
            }
        }
        return false;
    }

    private static bool ContainsOpenTypeParameter(IMethodSymbol method)
    {
        if (method.ContainingType is not null && ContainsOpenTypeParameter(method.ContainingType))
        {
            return true;
        }
        foreach (var arg in method.TypeArguments)
        {
            if (ContainsOpenTypeParameter(arg)) return true;
        }
        return false;
    }

    private static List<INamedTypeSymbol> CollectUserDeclaredTypes(CSharpCompilation compilation)
    {
        var result = new List<INamedTypeSymbol>();
        CollectNamespaceTypes(compilation.Assembly.GlobalNamespace, result);
        return result;
    }

    private static void CollectNamespaceTypes(INamespaceSymbol ns, List<INamedTypeSymbol> output)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            CollectNestedTypes(type, output);
        }
        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectNamespaceTypes(childNs, output);
        }
    }

    private static void CollectNestedTypes(INamedTypeSymbol type, List<INamedTypeSymbol> output)
    {
        if (VibraniumSymbolHelpers.IsEmbeddedOrCompilerSynthesizedType(type))
        {
            return;
        }
        if (!(type.IsImplicitlyDeclared && type.Name == "Program"))
        {
            output.Add(type);
        }
        foreach (var nested in type.GetTypeMembers())
        {
            CollectNestedTypes(nested, output);
        }
    }

    private static IMethodSymbol? FindTopLevelMain(CSharpCompilation compilation)
    {
        foreach (var type in compilation.Assembly.GlobalNamespace.GetTypeMembers("Program"))
        {
            foreach (var m in type.GetMembers("<Main>$").OfType<IMethodSymbol>())
            {
                return m;
            }
        }
        return null;
    }

    private static bool IsPurelyEmbeddedTree(SyntaxTree tree, CSharpCompilation compilation)
    {
        var root = tree.GetRoot();
        var typeDecls = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();
        if (typeDecls.Count == 0)
        {
            return false;
        }
        var model = compilation.GetSemanticModel(tree);
        return typeDecls.All(td =>
        {
            var sym = model.GetDeclaredSymbol(td);
            return sym is not null && VibraniumSymbolHelpers.IsEmbeddedOrCompilerSynthesizedType(sym);
        });
    }
}
