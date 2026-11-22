using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Vibranium.Compiler.Core;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class SyntaxAndSymbolGatekeeper
{
    private enum AttributePermission
    {
        MutatingReceiver,
        UnscopedRef,
        StdAssemblyMetadata,
        StdTargetFrameworkMetadata,
        Return,
        NativeCatalog,
        TrustedSource,
    }

    private Dictionary<INamedTypeSymbol, AttributePermission>? _attributePermissions;

    private Dictionary<INamedTypeSymbol, AttributePermission> BuildAttributePermissions()
    {
        var permissions = new Dictionary<INamedTypeSymbol, AttributePermission>(SymbolEqualityComparer.Default);
        // Anchor framework lookups in Roslyn's selected core library rather than
        // the compilation-wide name lookup, which can resolve a source shadow.
        var framework = _compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        Add(framework, "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute", AttributePermission.UnscopedRef);
        Add(framework, "System.Runtime.InteropServices.StructLayoutAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Runtime.InteropServices.ExtendedLayoutAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Runtime.InteropServices.FieldOffsetAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Runtime.CompilerServices.UnionAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Runtime.CompilerServices.InlineArrayAttribute", AttributePermission.TrustedSource);

        Add(framework, "System.Reflection.AssemblyCompanyAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Reflection.AssemblyConfigurationAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Reflection.AssemblyFileVersionAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Reflection.AssemblyInformationalVersionAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Reflection.AssemblyProductAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Reflection.AssemblyTitleAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Reflection.AssemblyVersionAttribute", AttributePermission.StdAssemblyMetadata);
        Add(framework, "System.Runtime.Versioning.TargetFrameworkAttribute", AttributePermission.StdTargetFrameworkMetadata);
        Add(framework, "System.Runtime.CompilerServices.InternalsVisibleToAttribute", AttributePermission.StdAssemblyMetadata);

        Add(framework, "System.Runtime.CompilerServices.CollectionBuilderAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.AttributeUsageAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.ComponentModel.EditorBrowsableAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.ObsoleteAttribute", AttributePermission.TrustedSource);
        Add(framework, "System.Runtime.CompilerServices.ModuleInitializerAttribute", AttributePermission.TrustedSource);

        // Core is a metadata reference in the CLI and in the Analyzer's clients.
        // The source assembly is never a candidate (not even if named Vibranium.Core).
        foreach (var reference in _compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (reference.Name != "Vibranium.Core") continue;
            Add(reference, "Vibranium.MutAttribute", AttributePermission.MutatingReceiver);
            Add(reference, "Vibranium.LifetimeOfAttribute", AttributePermission.Return);
            Add(reference, "Vibranium.NativeAttribute", AttributePermission.NativeCatalog);
        }
        return permissions;

        void Add(IAssemblySymbol? assembly, string metadataName, AttributePermission permission)
        {
            if (assembly?.GetTypeByMetadataName(metadataName) is { } type)
            {
                permissions[type] = permission;
            }
        }
    }

    private void CheckAttribute(AttributeSyntax attribute, SemanticModel model)
    {
        // Visit applications, not just symbols' attributes: return, assembly,
        // module and field/property targets (and local/lambda sites) matter too.
        // The constructor's containing symbol is independent of spelling or alias.
        var list = (AttributeListSyntax)attribute.Parent!;
        // C# interprets target specifiers by identifier value, including verbatim
        // spellings like @return. Unknown targets are distinct from no target.
        var target = list.Target?.Identifier.ValueText switch
        {
            null => SyntaxKind.None,
            "assembly" => SyntaxKind.AssemblyKeyword,
            "module" => SyntaxKind.ModuleKeyword,
            "return" => SyntaxKind.ReturnKeyword,
            "field" => SyntaxKind.FieldKeyword,
            "property" => SyntaxKind.PropertyKeyword,
            "param" => SyntaxKind.ParamKeyword,
            "method" => SyntaxKind.MethodKeyword,
            "type" => SyntaxKind.TypeKeyword,
            "event" => SyntaxKind.EventKeyword,
            "typevar" => SyntaxKind.TypeVarKeyword,
            _ => SyntaxKind.IdentifierToken,
        };
        var type = (model.GetSymbolInfo(attribute).Symbol as IMethodSymbol)?.ContainingType;
        var permissions = _attributePermissions ??= BuildAttributePermissions();
        var permission = type is not null && permissions.TryGetValue(type, out var known)
            ? known : (AttributePermission?)null;
        bool trustedSource = IsTrustedStdTree(attribute.SyntaxTree) || TrustedRuntimeSources.IsBootstrap(attribute.SyntaxTree);
        if (target is SyntaxKind.AssemblyKeyword or SyntaxKind.ModuleKeyword && !trustedSource)
        {
            // The official Std compilation retains SDK metadata and its test
            // friend. This is the existing authenticated sysroot check, not an
            // exemption based on a project name, file name, or generated marker.
            if (target == SyntaxKind.AssemblyKeyword && _isBuildingVibraniumStdItself &&
                list.Parent is CompilationUnitSyntax { Members.Count: 0 } &&
                permission is AttributePermission.StdAssemblyMetadata or AttributePermission.StdTargetFrameworkMetadata)
            {
                return;
            }

            const string assemblyMessage = "Assembly-level attributes are not supported in Vibranium v0.2; ";
            string targetMessage = target == SyntaxKind.ModuleKeyword
                ? "Module-level attributes are not supported in Vibranium v0.2; remove the attribute."
                : GetSdkAssemblyInfoProperty(list, permission) is { } property
                    ? $"{assemblyMessage}set <{property}>false</{property}>."
                    : assemblyMessage + "remove the attribute.";
            Emit(VibraniumDiagnosticIds.Unsupported, targetMessage, VibraniumSymbolHelpers.ToVibraniumSpan(attribute));
            return;
        }

        // Roslyn's semantic model may resolve a constructor even if an invalid
        // target specifier makes C# ignore the application. Conversely, C# need
        // not report a binding error for the attribute it ignored. Fail closed.
        switch (permission)
        {
            case AttributePermission.MutatingReceiver or AttributePermission.UnscopedRef or AttributePermission.Return
                when IsAppliedUserAttribute(attribute, list, target, permission.Value, model):
                return;
            case AttributePermission.StdAssemblyMetadata or AttributePermission.StdTargetFrameworkMetadata
                when target == SyntaxKind.AssemblyKeyword && list.Parent is CompilationUnitSyntax && trustedSource:
                return;
            case AttributePermission.NativeCatalog when list.Parent is MethodDeclarationSyntax declaration &&
                model.GetDeclaredSymbol(declaration) is IMethodSymbol method &&
                VibraniumSymbolHelpers.GetNativeSymbol(method) is { } nativeSymbol &&
                VibraniumSymbolHelpers.TryValidateSysNativeMethod(method, nativeSymbol, _compilation, _trustedSysCatalogTrees):
                return;
            case AttributePermission.TrustedSource when trustedSource:
                return;
        }

        string message = permission switch
        {
            AttributePermission.Return => "'[LifetimeOf]' is only supported as '[return: LifetimeOf(...)]' on methods in Vibranium.",
            AttributePermission.NativeCatalog => "'[Native]' is only valid on Vibranium.Std.Sys catalog methods.",
            _ when type is null || type.TypeKind == TypeKind.Error =>
                $"Attribute '{attribute.Name}' does not resolve to a supported attribute at this location in Vibranium.",
            _ => $"Attribute '{type.ToDisplayString()}' is not supported at this location in Vibranium.",
        };
        Emit(VibraniumDiagnosticIds.Unsupported, message, VibraniumSymbolHelpers.ToVibraniumSpan(attribute));
    }

    private static string? GetSdkAssemblyInfoProperty(AttributeListSyntax list, AttributePermission? permission)
    {
        // These signatures only tune the remedy. They never bypass the ban or
        // substitute for the authenticated sysroot checks above.
        if (list.Parent is not CompilationUnitSyntax { Members.Count: 0 } unit) return null;
        string fileName = Path.GetFileName(unit.SyntaxTree.FilePath);
        string header = unit.GetLeadingTrivia().ToFullString();
        if (permission == AttributePermission.StdTargetFrameworkMetadata &&
            fileName.StartsWith(".NETCoreApp,Version=v", StringComparison.Ordinal) &&
            fileName.EndsWith(".AssemblyAttributes.cs", StringComparison.Ordinal) &&
            header.IndexOf("<autogenerated />", StringComparison.Ordinal) >= 0)
        {
            return "GenerateTargetFrameworkAttribute";
        }
        return fileName.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal) &&
            header.IndexOf("<auto-generated>", StringComparison.Ordinal) >= 0 ? "GenerateAssemblyInfo" : null;
    }

    private static bool IsAppliedUserAttribute(AttributeSyntax attribute, AttributeListSyntax list, SyntaxKind target,
        AttributePermission permission, SemanticModel model)
    {
        if (list.Parent is not { } owner) return false;
        var symbol = model.GetDeclaredSymbol(owner);
        // Consult applied attributes, not candidate constructors. Checking the
        // actual application also accounts for C# target routing (e.g. [field:]
        // on a property must not count as an attribute on the property itself).
        var applied = (permission, target, symbol) switch
        {
            (AttributePermission.Return, SyntaxKind.ReturnKeyword, IMethodSymbol method) => method.GetReturnTypeAttributes(),
            (AttributePermission.MutatingReceiver or AttributePermission.UnscopedRef, SyntaxKind.None or SyntaxKind.MethodKeyword, IMethodSymbol method) when
                method.MethodKind is not (MethodKind.Constructor or MethodKind.StaticConstructor) => method.GetAttributes(),
            (AttributePermission.MutatingReceiver or AttributePermission.UnscopedRef, SyntaxKind.None or SyntaxKind.PropertyKeyword, IPropertySymbol property) =>
                property.GetAttributes(),
            (AttributePermission.UnscopedRef, SyntaxKind.None or SyntaxKind.ParamKeyword, IParameterSymbol parameter) => parameter.GetAttributes(),
            _ => default,
        };
        if (applied.IsDefaultOrEmpty) return false;
        foreach (var application in applied)
        {
            if (application.ApplicationSyntaxReference is { } reference && reference.SyntaxTree == attribute.SyntaxTree &&
                reference.Span == attribute.Span && application.AttributeClass is { TypeKind: not TypeKind.Error })
            {
                return true;
            }
        }
        return false;
    }
}
