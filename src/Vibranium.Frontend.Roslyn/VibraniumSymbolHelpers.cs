using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Vibranium.Compiler.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Frontend.Roslyn;

internal static class VibraniumSymbolHelpers
{
    private static readonly SymbolDisplayFormat TypeKeyFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private sealed record SysLeafSpec(string MethodName, string NativeSymbol, string ReturnType, string[] ParamTypes);

    private static readonly SysLeafSpec[] SysCatalogV1 =
    {
        new("FileOpen", "__vib_sys_file_open", "int", new[] { "System.ReadOnlySpan<byte>", "int" }),
        new("FileRead", "__vib_sys_file_read", "long", new[] { "int", "System.Span<byte>" }),
        new("FileWriteAll", "__vib_sys_file_write_all", "int", new[] { "int", "System.ReadOnlySpan<byte>" }),
        new("FileSync", "__vib_sys_file_sync", "int", new[] { "int" }),
        new("FileExists", "__vib_sys_file_exists", "int", new[] { "System.ReadOnlySpan<byte>" }),
        new("Close", "__vib_sys_close", "void", new[] { "int" }),
        new("StdinRead", "__vib_sys_stdin_read", "long", new[] { "System.Span<byte>" }),
        new("TcpListen", "__vib_sys_tcp_listen", "int", new[] { "int", "int", "int" }),
        new("TcpAccept", "__vib_sys_tcp_accept", "int", new[] { "int" }),
        new("TcpConnect", "__vib_sys_tcp_connect", "int", new[] { "int", "int" }),
        new("SockRecv", "__vib_sys_sock_recv", "long", new[] { "int", "System.Span<byte>" }),
        new("SockSendAll", "__vib_sys_sock_send_all", "int", new[] { "int", "System.ReadOnlySpan<byte>" }),
        new("SockShutdownWrite", "__vib_sys_sock_shutdown_write", "int", new[] { "int" }),
        new("SockLocalPort", "__vib_sys_sock_local_port", "int", new[] { "int" }),
        new("SpawnWait", "__vib_sys_spawn_wait", "int", new[] { "System.ReadOnlySpan<byte>", "System.ReadOnlySpan<int>" }),
        new("ArgCount", "__vib_sys_arg_count", "int", Array.Empty<string>()),
        new("ArgCopy", "__vib_sys_arg_copy", "int", new[] { "int", "System.Span<byte>" }),
    };

    public static VibraniumSpan ToVibraniumSpan(SyntaxNode? node)
    {
        if (node is null)
        {
            return new VibraniumSpan(string.Empty, 0, 0, 1, 1);
        }
        return ToVibraniumSpan(node.GetLocation());
    }

    public static VibraniumSpan ToVibraniumSpan(Location location)
    {
        if (!location.IsInSource || location.SourceTree is null)
        {
            return new VibraniumSpan(string.Empty, 0, 0, 1, 1);
        }

        var lineSpan = location.GetLineSpan();
        return new VibraniumSpan(
            location.SourceTree.FilePath ?? string.Empty,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    public static string NormalizePathSeparators(string? path) =>
        string.IsNullOrEmpty(path) ? string.Empty : path!.Replace('\\', '/');

    public static bool IsTrustedManagedAdapterTree(SyntaxTree? tree, ISet<SyntaxTree>? trustedManagedAdapterTrees) =>
        tree is not null && trustedManagedAdapterTrees is not null && trustedManagedAdapterTrees.Contains(tree);

    public static bool IsDeclaredOnlyInTrustedManagedAdapter(ISymbol symbol, ISet<SyntaxTree>? trustedManagedAdapterTrees)
    {
        if (trustedManagedAdapterTrees is null || trustedManagedAdapterTrees.Count == 0)
        {
            return false;
        }
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.Length == 0)
        {
            return false;
        }
        foreach (var sr in refs)
        {
            if (!IsTrustedManagedAdapterTree(sr.SyntaxTree, trustedManagedAdapterTrees))
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsOfficialStdSysManagedTreeStructure(SyntaxTree? tree) =>
        tree is not null && TrustedRuntimeSources.Authenticate(tree, "Vibranium.Std/Sys/Sys.Managed.cs");

    public static bool IsOfficialStdSysCatalogTreeStructure(SyntaxTree? tree) =>
        tree is not null && TrustedRuntimeSources.Authenticate(tree, "Vibranium.Std/Sys/Sys.cs");

    public static string? GetNativeSymbol(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "NativeAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Vibranium")
            {
                if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string s)
                {
                    return s;
                }
                return string.Empty;
            }
        }
        return null;
    }

    public static bool TryValidateSysNativeMethod(
        IMethodSymbol method,
        string symbol,
        CSharpCompilation compilation,
        ISet<SyntaxTree>? trustedSysCatalogTrees = null)
    {
        if (!method.IsStatic || method.IsGenericMethod)
        {
            return false;
        }
        if (method.ContainingType?.Name != "Sys" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "Vibranium.Std")
        {
            return false;
        }

        // Prevent user code from spoofing namespace Vibranium.Std { class Sys { [Native(...)] } }
        bool trustedOrigin = method.DeclaringSyntaxReferences.Any(sr =>
            trustedSysCatalogTrees?.Contains(sr.SyntaxTree) == true || IsOfficialStdSysCatalogTreeStructure(sr.SyntaxTree));
        if (!trustedOrigin)
        {
            return false;
        }

        string retKey = GetTypeKey(method.ReturnType);
        foreach (var spec in SysCatalogV1)
        {
            if (spec.MethodName != method.Name)
            {
                continue;
            }
            if (spec.NativeSymbol != symbol || spec.ReturnType != retKey || spec.ParamTypes.Length != method.Parameters.Length)
            {
                return false;
            }
            for (int i = 0; i < spec.ParamTypes.Length; i++)
            {
                var p = method.Parameters[i];
                if (p.RefKind != RefKind.None || GetTypeKey(p.Type) != spec.ParamTypes[i])
                {
                    return false;
                }
            }
            return true;
        }

        return false;
    }

    public static bool IsVibraniumCoreType(ITypeSymbol? symbol, string name)
    {
        if (symbol is not INamedTypeSymbol named)
        {
            return false;
        }
        return named.Name == name && named.ContainingNamespace?.ToDisplayString() == "Vibranium";
    }

    // OptionExtensions is a runtime type, not any same-named user or nested type.
    public static bool IsCoreOptionExtensions(INamedTypeSymbol type) =>
        type.Name == "OptionExtensions" && type.ContainingAssembly.Name == "Vibranium.Core" &&
        SymbolEqualityComparer.Default.Equals(type, type.ContainingAssembly.GetTypeByMetadataName("Vibranium.OptionExtensions"));

    public static bool IsClosedUtf8ViewType(ITypeSymbol? symbol) =>
        IsVibraniumCoreType(symbol, "Utf8Lines") || IsVibraniumCoreType(symbol, "Utf8ArgEnumerator");

    public static bool IsSystemIndexOrRangeType(ITypeSymbol? symbol) =>
        symbol is INamedTypeSymbol named &&
        named.ContainingNamespace?.ToDisplayString() == "System" &&
        named.Name is "Index" or "Range";

    public static bool IsSpanType(ITypeSymbol? symbol, out ITypeSymbol elementType, out bool isReadOnly)
    {
        elementType = null!;
        isReadOnly = false;
        if (symbol is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
        {
            return false;
        }

        if (named.ContainingNamespace?.ToDisplayString() == "System")
        {
            if (named.Name == "Span")
            {
                elementType = named.TypeArguments[0];
                isReadOnly = false;
                return true;
            }
            if (named.Name == "ReadOnlySpan")
            {
                elementType = named.TypeArguments[0];
                isReadOnly = true;
                return true;
            }
        }
        return false;
    }

    public static bool ImplementsIDisposable(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type.SpecialType == SpecialType.System_IDisposable) return true;
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.SpecialType == SpecialType.System_IDisposable ||
                iface.ToDisplayString() == "System.IDisposable")
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsLinearResourceSymbol(ITypeSymbol? type, HashSet<ITypeSymbol>? visited = null)
    {
        if (type is null) return false;
        if (ImplementsIDisposable(type)) return true;

        if (type is INamedTypeSymbol named)
        {
            if (IsVibraniumCoreType(named, "Option") || IsVibraniumCoreType(named, "Some") || IsVibraniumCoreType(named, "Ok") || IsVibraniumCoreType(named, "Err"))
            {
                return named.TypeArguments.Length == 1 && IsLinearResourceSymbol(named.TypeArguments[0], visited);
            }
            if (IsVibraniumCoreType(named, "Result") && named.TypeArguments.Length == 2)
            {
                return IsLinearResourceSymbol(named.TypeArguments[0], visited) ||
                       IsLinearResourceSymbol(named.TypeArguments[1], visited);
            }
            if (named.TypeKind == TypeKind.Struct)
            {
                visited ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                if (!visited.Add(named)) return false;
                try
                {
                    foreach (var field in GetInstanceFields(named))
                    {
                        if (IsLinearResourceSymbol(field.Type, visited))
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    visited.Remove(named);
                }
            }
        }

        return false;
    }

    public static bool IsCopyTypeSymbol(ITypeSymbol? type, HashSet<ITypeSymbol>? visited = null)
    {
        if (type is null) return false;
        switch (type.SpecialType)
        {
            case SpecialType.System_Void:
            case SpecialType.System_Boolean:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Char:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
                return true;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (type is ITypeParameterSymbol tp)
        {
            return tp.HasUnmanagedTypeConstraint;
        }

        if (IsSpanType(type, out _, out bool isRo))
        {
            return isRo;
        }

        if (type is INamedTypeSymbol named)
        {
            if (IsVibraniumCoreType(named, "NoneType") || IsVibraniumCoreType(named, "Unit") || IsVibraniumCoreType(named, "OptionRef") || IsClosedUtf8ViewType(named))
            {
                return true;
            }
            if (IsVibraniumCoreType(named, "Vec") || IsVibraniumCoreType(named, "OptionMut"))
            {
                return false;
            }
            if (IsVibraniumCoreType(named, "Option") || IsVibraniumCoreType(named, "Some") || IsVibraniumCoreType(named, "Ok") || IsVibraniumCoreType(named, "Err"))
            {
                return named.TypeArguments.Length == 1 && IsCopyTypeSymbol(named.TypeArguments[0], visited);
            }
            if (IsVibraniumCoreType(named, "Result") && named.TypeArguments.Length == 2)
            {
                return IsCopyTypeSymbol(named.TypeArguments[0], visited) && IsCopyTypeSymbol(named.TypeArguments[1], visited);
            }
            if (named.TypeKind == TypeKind.Class)
            {
                return false;
            }
            if (named.TypeKind == TypeKind.Struct)
            {
                if (named.IsRefLikeType || ImplementsIDisposable(named))
                {
                    return false;
                }
                visited ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                if (!visited.Add(named))
                {
                    return false;
                }
                try
                {
                    foreach (var f in GetInstanceFields(named))
                    {
                        if (!IsCopyTypeSymbol(f.Type, visited))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                finally
                {
                    visited.Remove(named);
                }
            }
        }

        return false;
    }

    public static bool ValidateL1LinearTypeArguments(
        ISymbol? genericTarget,
        ImmutableArray<ITypeSymbol> typeArgs,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        bool anyLinear = false;
        foreach (var ta in typeArgs)
        {
            if (IsLinearResourceSymbol(ta))
            {
                anyLinear = true;
                if (!ValidateNestedLinearTypeSymbol(ta, out errorMessage))
                {
                    return false;
                }
            }
        }

        if (!anyLinear)
        {
            return true;
        }

        if (genericTarget is IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } ctorOwner })
        {
            return ValidateL1LinearNamedType(ctorOwner, ctorOwner.TypeArguments, out errorMessage);
        }

        if (genericTarget is INamedTypeSymbol named)
        {
            return ValidateL1LinearNamedType(named, typeArgs, out errorMessage);
        }

        if (genericTarget is IMethodSymbol method)
        {
            bool isAllowedPreludeFactory =
                method.ContainingType?.Name == "Prelude" &&
                method.ContainingNamespace?.ToDisplayString() == "Vibranium" &&
                method.Name is "Some" or "Ok" or "Move" &&
                typeArgs.Length == 1;

            if (isAllowedPreludeFactory)
            {
                return true;
            }

            var badArg = typeArgs.First(ta => IsLinearResourceSymbol(ta));
            errorMessage = $"Linear IDisposable resource '{badArg.Name}' cannot be used as a generic type argument in '{method.Name}'.";
            return false;
        }

        var firstLinear = typeArgs.First(ta => IsLinearResourceSymbol(ta));
        errorMessage = $"Linear IDisposable resource '{firstLinear.Name}' cannot be used as a generic type argument here.";
        return false;
    }

    private static bool ValidateL1LinearNamedType(
        INamedTypeSymbol named,
        ImmutableArray<ITypeSymbol> typeArgs,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if ((IsVibraniumCoreType(named, "Option") || IsVibraniumCoreType(named, "Some") || IsVibraniumCoreType(named, "Ok")) &&
            typeArgs.Length == 1)
        {
            return true;
        }

        if (IsVibraniumCoreType(named, "Result") && typeArgs.Length == 2)
        {
            var okArg = typeArgs[0];
            var errArg = typeArgs[1];
            if (IsLinearResourceSymbol(errArg))
            {
                errorMessage = $"Linear IDisposable resource '{errArg.Name}' is only permitted as the Ok payload 'T' of 'Result<T, E>', never as 'E'.";
                return false;
            }
            if (IsLinearResourceSymbol(okArg) && !IsCopyTypeSymbol(errArg))
            {
                errorMessage = $"Linear 'Result<{okArg.Name}, {errArg.Name}>' requires Copy error type 'E', but '{errArg.Name}' is non-Copy.";
                return false;
            }
            return true;
        }

        var bad = typeArgs.First(ta => IsLinearResourceSymbol(ta));
        errorMessage = $"Linear IDisposable resource '{bad.Name}' cannot instantiate generic type '{named.Name}'.";
        return false;
    }

    private static bool ValidateNestedLinearTypeSymbol(ITypeSymbol type, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            if (!ValidateL1LinearNamedType(named, named.TypeArguments, out errorMessage))
            {
                return false;
            }
            foreach (var ta in named.TypeArguments)
            {
                if (!ValidateNestedLinearTypeSymbol(ta, out errorMessage))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public static bool IsEmbeddedOrCompilerSynthesizedType(INamedTypeSymbol type)
    {
        // Top-level Program is synthesized but contains user code. Compiler
        // embedded attributes have no source declarations; user-applied
        // CompilerGenerated/Embedded attributes grant no trust whatsoever.
        if (type.Name == "Program" && type.GetMembers().OfType<IMethodSymbol>().Any(method => method.Name == "<Main>$"))
            return false;
        if (type.IsImplicitlyDeclared && type.DeclaringSyntaxReferences.Length == 0)
            return true;
        return type.DeclaringSyntaxReferences.Length > 0 &&
            type.DeclaringSyntaxReferences.All(reference => TrustedRuntimeSources.IsBootstrap(reference.SyntaxTree));
    }

    public static bool HasMutAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "MutAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Vibranium")
            {
                return true;
            }
        }
        if (symbol is IMethodSymbol { MethodKind: MethodKind.PropertySet, AssociatedSymbol: IPropertySymbol prop })
        {
            foreach (var attr in prop.GetAttributes())
            {
                if (attr.AttributeClass?.Name == "MutAttribute" &&
                    attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Vibranium")
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static bool HasInteropImportAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is "DllImportAttribute" or "LibraryImportAttribute" or "UnmanagedCallersOnlyAttribute")
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsSupportedCompoundAddAssignOperator(IMethodSymbol method) =>
        method.MethodKind == MethodKind.UserDefinedOperator &&
        !method.IsStatic &&
        method.ReturnsVoid &&
        method.Name == "op_AdditionAssignment" &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].RefKind == RefKind.None;

    public static bool IsAutoOrInitProperty(IPropertySymbol prop)
    {
        if (prop.IsIndexer || prop.IsAbstract || prop.IsImplicitlyDeclared)
        {
            return false;
        }

        foreach (var acc in new[] { prop.GetMethod, prop.SetMethod })
        {
            if (acc is null)
            {
                continue;
            }
            if (acc.IsImplicitlyDeclared || acc.IsInitOnly)
            {
                return true;
            }
            foreach (var sr in acc.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (sr.GetSyntax() is AccessorDeclarationSyntax accSyntax &&
                    accSyntax.Body is null &&
                    accSyntax.ExpressionBody is null)
                {
                    return true;
                }
            }
        }

        if (prop.ContainingType is { } owner)
        {
            foreach (var member in owner.OriginalDefinition.GetMembers())
            {
                if (member is IFieldSymbol { IsImplicitlyDeclared: true } backing &&
                    SymbolEqualityComparer.Default.Equals(backing.AssociatedSymbol, prop.OriginalDefinition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsZeroSafeTypeSymbol(
        ITypeSymbol? type,
        CSharpCompilation? compilation = null,
        HashSet<ITypeSymbol>? visited = null)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return false;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Void:
            case SpecialType.System_Boolean:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Char:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return true;
            case SpecialType.System_String:
            case SpecialType.System_Decimal:
            case SpecialType.System_Object:
                return false;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (type is ITypeParameterSymbol)
        {
            return false;
        }

        if (IsSpanType(type, out _, out _))
        {
            return true;
        }

        if (type is INamedTypeSymbol named)
        {
            if (IsVibraniumCoreType(named, "NoneType") ||
                IsVibraniumCoreType(named, "Unit") ||
                IsVibraniumCoreType(named, "Option"))
            {
                return true;
            }
            if (IsVibraniumCoreType(named, "Vec") ||
                IsVibraniumCoreType(named, "Result") ||
                IsVibraniumCoreType(named, "Some") ||
                IsVibraniumCoreType(named, "Ok") ||
                IsVibraniumCoreType(named, "Err") ||
                IsVibraniumCoreType(named, "OptionRef") ||
                IsVibraniumCoreType(named, "OptionMut") ||
                IsClosedUtf8ViewType(named))
            {
                return false;
            }
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }
            if (named.TypeKind == TypeKind.Class)
            {
                return false;
            }
            if (named.TypeKind == TypeKind.Struct)
            {
                if (named.IsRefLikeType || ImplementsIDisposable(named))
                {
                    return false;
                }
                string fullName = named.ToDisplayString();
                if (fullName is "System.Half" or "System.Int128" or "System.UInt128")
                {
                    return false;
                }
                bool inUserOrStd =
                    named.DeclaringSyntaxReferences.Length > 0 ||
                    string.Equals(named.ContainingAssembly?.Name, "Vibranium.Std", StringComparison.Ordinal) ||
                    (compilation is not null && SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly));
                if (!inUserOrStd)
                {
                    return false;
                }

                visited ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                if (!visited.Add(named))
                {
                    return false;
                }
                try
                {
                    foreach (var f in GetInstanceFields(named, compilation))
                    {
                        if (!IsZeroSafeTypeSymbol(f.Type, compilation, visited))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                finally
                {
                    visited.Remove(named);
                }
            }
        }

        return false;
    }

    public static bool HasUnscopedRefAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "UnscopedRefAttribute")
            {
                return true;
            }
        }
        return false;
    }

    public static bool HasObsoleteAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "ObsoleteAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "System")
            {
                return true;
            }
        }
        return false;
    }

    public static HashSet<string>? GetReturnLifetimeOfParameters(IMethodSymbol method)
    {
        HashSet<string>? allowed = null;
        foreach (var attr in method.GetReturnTypeAttributes())
        {
            if (attr.AttributeClass?.Name == "LifetimeOfAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Vibranium")
            {
                allowed ??= new HashSet<string>(StringComparer.Ordinal);
                foreach (var arg in attr.ConstructorArguments)
                {
                    if (arg.Kind == TypedConstantKind.Array)
                    {
                        foreach (var elem in arg.Values)
                        {
                            if (elem.Value is string s) allowed.Add(s);
                        }
                    }
                    else if (arg.Value is string single)
                    {
                        allowed.Add(single);
                    }
                }
            }
        }
        return allowed;
    }


    internal readonly record struct InstanceFieldDescriptor(
        string Name,
        ITypeSymbol Type,
        bool IsReadOnly,
        IFieldSymbol? ExplicitField,
        IParameterSymbol? CapturedParameter);

    public static string GetSynthesizedCaptureFieldName(IParameterSymbol parameter) =>
        $"<{parameter.Name}>P";

    public static IMethodSymbol? TryGetPrimaryConstructor(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsStatic)
        {
            return null;
        }

        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.IsStatic)
            {
                continue;
            }
            foreach (var sr in ctor.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (sr.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null })
                {
                    return ctor;
                }
            }
        }

        return null;
    }

    public static ImmutableArray<IParameterSymbol> GetCapturedPrimaryConstructorParameters(
        INamedTypeSymbol type,
        CSharpCompilation? compilation = null)
    {
        var primaryCtor = TryGetPrimaryConstructor(type);
        if (primaryCtor is null || primaryCtor.Parameters.Length == 0)
        {
            return ImmutableArray<IParameterSymbol>.Empty;
        }

        var origCtor = primaryCtor.OriginalDefinition;
        var capturedOrigSet = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);

        foreach (var sr in type.OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (sr.GetSyntax() is not TypeDeclarationSyntax typeDecl)
            {
                continue;
            }

            SemanticModel? model = (compilation is not null && compilation.ContainsSyntaxTree(typeDecl.SyntaxTree))
                ? compilation.GetSemanticModel(typeDecl.SyntaxTree)
                : null;

            foreach (var member in typeDecl.Members)
            {
                switch (member)
                {
                    case MethodDeclarationSyntax methodSyntax when !methodSyntax.Modifiers.Any(SyntaxKind.StaticKeyword):
                        ScanInstanceMemberForCaptures(
                            (SyntaxNode?)methodSyntax.Body ?? methodSyntax.ExpressionBody,
                            methodSyntax.ParameterList,
                            origCtor.Parameters,
                            model,
                            capturedOrigSet);
                        break;

                    case PropertyDeclarationSyntax propSyntax when !propSyntax.Modifiers.Any(SyntaxKind.StaticKeyword):
                        if (propSyntax.ExpressionBody is not null)
                        {
                            ScanInstanceMemberForCaptures(propSyntax.ExpressionBody, null, origCtor.Parameters, model, capturedOrigSet);
                        }
                        if (propSyntax.AccessorList is not null)
                        {
                            ScanInstanceMemberForCaptures(propSyntax.AccessorList, null, origCtor.Parameters, model, capturedOrigSet);
                        }
                        break;

                    case IndexerDeclarationSyntax indexerSyntax:
                        if (indexerSyntax.ExpressionBody is not null)
                        {
                            ScanInstanceMemberForCaptures(indexerSyntax.ExpressionBody, indexerSyntax.ParameterList, origCtor.Parameters, model, capturedOrigSet);
                        }
                        if (indexerSyntax.AccessorList is not null)
                        {
                            ScanInstanceMemberForCaptures(indexerSyntax.AccessorList, indexerSyntax.ParameterList, origCtor.Parameters, model, capturedOrigSet);
                        }
                        break;
                }
            }
        }

        if (capturedOrigSet.Count == 0)
        {
            return ImmutableArray<IParameterSymbol>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<IParameterSymbol>(capturedOrigSet.Count);
        for (int i = 0; i < primaryCtor.Parameters.Length; i++)
        {
            var p = primaryCtor.Parameters[i];
            if (p.RefKind == RefKind.None && capturedOrigSet.Contains(origCtor.Parameters[i]))
            {
                builder.Add(p);
            }
        }
        return builder.ToImmutable();
    }

    private static void ScanInstanceMemberForCaptures(
        SyntaxNode? bodyRoot,
        BaseParameterListSyntax? memberParams,
        ImmutableArray<IParameterSymbol> origPrimaryParams,
        SemanticModel? model,
        HashSet<IParameterSymbol> capturedOrigSet)
    {
        if (bodyRoot is null)
        {
            return;
        }

        if (model is not null)
        {
            foreach (var id in bodyRoot.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var symInfo = model.GetSymbolInfo(id);
                var sym = symInfo.Symbol ?? symInfo.CandidateSymbols.FirstOrDefault();
                if (sym is IParameterSymbol ps && origPrimaryParams.Contains(ps.OriginalDefinition, SymbolEqualityComparer.Default))
                {
                    capturedOrigSet.Add(ps.OriginalDefinition);
                }
            }
            return;
        }

        var shadowed = new HashSet<string>(StringComparer.Ordinal);
        if (memberParams is not null)
        {
            foreach (var mp in memberParams.Parameters)
            {
                shadowed.Add(mp.Identifier.ValueText);
            }
        }

        foreach (var id in bodyRoot.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Parent is MemberAccessExpressionSyntax mae && mae.Name == id)
            {
                continue;
            }
            string name = id.Identifier.ValueText;
            if (shadowed.Contains(name))
            {
                continue;
            }
            foreach (var op in origPrimaryParams)
            {
                if (string.Equals(op.Name, name, StringComparison.Ordinal))
                {
                    capturedOrigSet.Add(op);
                }
            }
        }
    }

    public static bool TryMatchPrimaryConstructorCaptureField(
        IFieldSymbol field,
        IMethodSymbol? primaryCtor,
        out IParameterSymbol? capturedParameter)
    {
        capturedParameter = null;
        if (primaryCtor is null || !field.IsImplicitlyDeclared || field.IsStatic || field.AssociatedSymbol is not null)
        {
            return false;
        }

        foreach (var p in primaryCtor.Parameters)
        {
            if (p.RefKind == RefKind.None &&
                string.Equals(field.Name, GetSynthesizedCaptureFieldName(p), StringComparison.Ordinal))
            {
                capturedParameter = p;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<InstanceFieldDescriptor> GetInstanceFields(
        INamedTypeSymbol type,
        CSharpCompilation? compilation = null)
    {
        var primaryCtor = TryGetPrimaryConstructor(type);
        var matchedCaptureOriginals = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        var result = new List<InstanceFieldDescriptor>();

        foreach (var f in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (f.IsStatic)
            {
                continue;
            }
            if (TryMatchPrimaryConstructorCaptureField(f, primaryCtor, out var matchedParam) && matchedParam is not null)
            {
                matchedCaptureOriginals.Add(matchedParam.OriginalDefinition);
                result.Add(new InstanceFieldDescriptor(
                    f.Name,
                    f.Type,
                    type.IsReadOnly,
                    ExplicitField: null,
                    CapturedParameter: matchedParam));
                continue;
            }
            result.Add(new InstanceFieldDescriptor(f.Name, f.Type, f.IsReadOnly, f, null));
        }

        var capturedParams = GetCapturedPrimaryConstructorParameters(type, compilation);
        foreach (var cp in capturedParams)
        {
            if (matchedCaptureOriginals.Add(cp.OriginalDefinition))
            {
                result.Add(new InstanceFieldDescriptor(
                    GetSynthesizedCaptureFieldName(cp),
                    cp.Type,
                    type.IsReadOnly,
                    ExplicitField: null,
                    CapturedParameter: cp));
            }
        }

        return result;
    }

    public static string GetTypeKey(ITypeSymbol type)
    {
        return type.ToDisplayString(TypeKeyFormat);
    }

    public static string GetMethodKey(IMethodSymbol method)
    {
        string owner;
        if (method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol parentMethod)
        {
            var loc = method.Locations.FirstOrDefault(l => l.IsInSource);
            string posSuffix = loc is not null ? $"@{loc.SourceSpan.Start}" : string.Empty;
            owner = GetMethodKey(parentMethod) + posSuffix;
        }
        else
        {
            owner = method.ContainingType is not null ? GetTypeKey(method.ContainingType) : "<global>";
        }

        string name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;

        string typeArgs = string.Empty;
        if (method.TypeArguments.Length > 0)
        {
            typeArgs = "<" + string.Join(", ", method.TypeArguments.Select(GetTypeKey)) + ">";
        }

        var paramParts = new List<string>(method.Parameters.Length);
        foreach (var p in method.Parameters)
        {
            string prefix = p.RefKind switch
            {
                RefKind.In or RefKind.RefReadOnlyParameter => "in ",
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                _ => string.Empty,
            };
            paramParts.Add(prefix + GetTypeKey(p.Type));
        }

        return $"{owner}.{name}{typeArgs}({string.Join(", ", paramParts)})";
    }

    public static MirPassKind GetParamPassKind(IParameterSymbol param) =>
        param.RefKind switch
        {
            RefKind.In or RefKind.RefReadOnlyParameter => MirPassKind.In,
            RefKind.Ref => MirPassKind.Ref,
            RefKind.Out => MirPassKind.Out,
            _ => MirPassKind.ByValue,
        };

    public static MirPassKind GetReceiverPassKind(IMethodSymbol method)
    {
        var containing = method.ContainingType;
        if (containing is null)
        {
            return MirPassKind.ImplicitThisShared;
        }

        if (containing.TypeKind == TypeKind.Class)
        {
            bool isDisposeMethod = method.Name == "Dispose" &&
                                   method.Parameters.Length == 0 &&
                                   ImplementsIDisposable(containing);
            return (HasMutAttribute(method) || isDisposeMethod)
                ? MirPassKind.ImplicitThisMut
                : MirPassKind.ImplicitThisShared;
        }

        if (containing.TypeKind == TypeKind.Struct)
        {
            bool isReadOnly = method.IsReadOnly || containing.IsReadOnly;
            return isReadOnly ? MirPassKind.ImplicitThisShared : MirPassKind.ImplicitThisMut;
        }

        return MirPassKind.ImplicitThisShared;
    }

    public static MirType MapType(ITypeSymbol? type, MirModule module, Action<ITypeSymbol>? onUnsupported = null)
    {
        if (type is null)
        {
            return MirType.Void;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Void:
                return MirType.Void;
            case SpecialType.System_Boolean:
                return MirType.Bool;
            case SpecialType.System_SByte:
                return MirType.Int8;
            case SpecialType.System_Byte:
                return MirType.UInt8;
            case SpecialType.System_Int16:
                return MirType.Int16;
            case SpecialType.System_UInt16:
                return MirType.UInt16;
            case SpecialType.System_Char:
                return MirType.Char;
            case SpecialType.System_Int32:
                return MirType.Int32;
            case SpecialType.System_UInt32:
                return MirType.UInt32;
            case SpecialType.System_Int64:
                return MirType.Int64;
            case SpecialType.System_UInt64:
                return MirType.UInt64;
            case SpecialType.System_IntPtr:
                return MirType.NInt;
            case SpecialType.System_UIntPtr:
                return MirType.NUInt;
            case SpecialType.System_Single:
                return MirType.Float32;
            case SpecialType.System_Double:
                return MirType.Float64;
            case SpecialType.System_String:
                return MirType.StringLiteral;
            case SpecialType.System_Decimal:
                onUnsupported?.Invoke(type);
                return MirType.Int32;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return new MirType.EnumType(GetTypeKey(type));
        }

        if (type is ITypeParameterSymbol tp)
        {
            if (tp.HasUnmanagedTypeConstraint)
            {
                string unmanagedKey = $"__UnmanagedTypeParam_{tp.Name}";
                if (!module.Structs.ContainsKey(unmanagedKey))
                {
                    module.Structs[unmanagedKey] = new MirStructDef(
                        unmanagedKey,
                        IsReadOnly: true,
                        IsRefStruct: false,
                        IsDisposable: false,
                        Fields: new[] { new MirFieldDef("_", MirType.StringLiteral, true) });
                }
                return new MirType.StructType(unmanagedKey, false);
            }

            string moveOnlyKey = $"__MoveTypeParam_{tp.Name}";
            if (!module.Classes.ContainsKey("__OpaqueOwner"))
            {
                module.Classes["__OpaqueOwner"] = new MirClassDef("__OpaqueOwner", false, null, Array.Empty<MirFieldDef>());
            }
            if (!module.Structs.ContainsKey(moveOnlyKey))
            {
                module.Structs[moveOnlyKey] = new MirStructDef(
                    moveOnlyKey,
                    IsReadOnly: false,
                    IsRefStruct: false,
                    IsDisposable: false,
                    Fields: new[] { new MirFieldDef("_", new MirType.ClassType("__OpaqueOwner", false), false) });
            }
            return new MirType.StructType(moveOnlyKey, false);
        }

        if (IsSpanType(type, out var spanElem, out bool spanRo))
        {
            return new MirType.SpanType(MapType(spanElem, module, onUnsupported), spanRo);
        }

        if (type is INamedTypeSymbol named)
        {
            if (IsVibraniumCoreType(named, "NoneType") || IsVibraniumCoreType(named, "Unit"))
            {
                return MirType.NoneUnit;
            }
            if (IsVibraniumCoreType(named, "Utf8Lines"))
            {
                return MirType.Utf8Lines;
            }
            if (IsVibraniumCoreType(named, "Utf8ArgEnumerator"))
            {
                return MirType.Utf8ArgEnumerator;
            }
            if (IsVibraniumCoreType(named, "Option") && named.TypeArguments.Length == 1)
            {
                return new MirType.OptionType(MapType(named.TypeArguments[0], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "OptionRef") && named.TypeArguments.Length == 1)
            {
                return new MirType.OptionRefType(MapType(named.TypeArguments[0], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "OptionMut") && named.TypeArguments.Length == 1)
            {
                return new MirType.OptionMutType(MapType(named.TypeArguments[0], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "Some") && named.TypeArguments.Length == 1)
            {
                return new MirType.SomeWrapperType(MapType(named.TypeArguments[0], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "Result") && named.TypeArguments.Length == 2)
            {
                return new MirType.ResultType(
                    MapType(named.TypeArguments[0], module, onUnsupported),
                    MapType(named.TypeArguments[1], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "Ok") && named.TypeArguments.Length == 1)
            {
                return new MirType.OkWrapperType(MapType(named.TypeArguments[0], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "Err") && named.TypeArguments.Length == 1)
            {
                return new MirType.ErrWrapperType(MapType(named.TypeArguments[0], module, onUnsupported));
            }
            if (IsVibraniumCoreType(named, "Vec") && named.TypeArguments.Length == 1)
            {
                return new MirType.VecType(MapType(named.TypeArguments[0], module, onUnsupported));
            }

            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                var inner = named.TypeArguments.Length == 1 ? MapType(named.TypeArguments[0], module, onUnsupported) : MirType.Int32;
                return new MirType.OptionType(inner);
            }

            if (named.TypeKind == TypeKind.Class && named.SpecialType != SpecialType.System_Object)
            {
                return new MirType.ClassType(GetTypeKey(named), ImplementsIDisposable(named));
            }

            if (named.TypeKind == TypeKind.Struct)
            {
                string fullName = named.ToDisplayString();
                if (fullName is "System.Half" or "System.Int128" or "System.UInt128")
                {
                    onUnsupported?.Invoke(type);
                    return MirType.Int32;
                }
                return new MirType.StructType(GetTypeKey(named), named.IsRefLikeType);
            }
        }

        onUnsupported?.Invoke(type);
        return MirType.Int32;
    }
}
