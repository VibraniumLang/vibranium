using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Vibranium.Backend.Llvm;
using Vibranium.Compiler.Core;
using Vibranium.Frontend.Roslyn;

namespace Vibranium.Cli;

public sealed record VibraniumCompileOutcome(
    bool Success,
    IReadOnlyList<string> Errors,
    MirModule? Module,
    string? LlvmIr);

public sealed record VibraniumManifestUnit(
    string ManifestPath,
    string UnitKind,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> ManagedSources,
    IReadOnlyList<string> NativeFragments);

public static class VibraniumCompilerDriver
{
    private const string GlobalUsingsCoreOnly = """
        global using global::System;
        global using global::Vibranium;
        global using static global::Vibranium.Prelude;
        """;

    private const string GlobalUsingsWithStd = """
        global using global::System;
        global using global::Vibranium;
        global using global::Vibranium.Std;
        global using static global::Vibranium.Prelude;
        global using static global::Vibranium.Std.ConsoleIo;
        """;

    public static CSharpCompilation CreateCompilationFromPath(
        string inputPath,
        string assemblyName = "VibraniumProgram",
        bool includeStd = true,
        string? sysroot = null)
    {
        return CreateCompilationFromPaths(new[] { inputPath }, assemblyName, includeStd, sysroot);
    }

    public static CSharpCompilation CreateCompilationFromPaths(
        IReadOnlyList<string> inputPaths,
        string assemblyName = "VibraniumProgram",
        bool includeStd = true,
        string? sysroot = null)
    {
        if (inputPaths is null || inputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input path is required.", nameof(inputPaths));
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp15);
        string firstFullPath = Path.GetFullPath(inputPaths[0]);

        // Check if single manifest input (or directory containing vib.manifest)
        string? rootManifestPath = null;
        if (inputPaths.Count == 1)
        {
            if (File.Exists(firstFullPath) &&
                (string.Equals(Path.GetFileName(firstFullPath), "vib.manifest", StringComparison.OrdinalIgnoreCase) ||
                 firstFullPath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)))
            {
                rootManifestPath = firstFullPath;
            }
            else if (Directory.Exists(firstFullPath))
            {
                string candidateManifest = Path.Combine(firstFullPath, "vib.manifest");
                if (File.Exists(candidateManifest))
                {
                    rootManifestPath = candidateManifest;
                }
            }
        }

        if (rootManifestPath is not null)
        {
            return CreateCompilationFromManifest(rootManifestPath, parseOptions, assemblyName, includeStd, sysroot);
        }

        string? officialStdDir = ResolveVibraniumStdSourceDirectory(firstFullPath, sysroot);
        string? stdDir = includeStd ? officialStdDir : null;

        bool isCompilingStdItself = false;
        if (inputPaths.Count == 1 && officialStdDir is not null)
        {
            string? singleDir = null;
            if (File.Exists(firstFullPath) && firstFullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                singleDir = Path.GetDirectoryName(firstFullPath)!;
            }
            else if (Directory.Exists(firstFullPath))
            {
                singleDir = firstFullPath;
            }

            isCompilingStdItself = singleDir is not null && PathsEqual(singleDir, officialStdDir);
        }

        if (isCompilingStdItself && string.Equals(assemblyName, "VibraniumProgram", StringComparison.Ordinal))
        {
            assemblyName = "Vibranium.Std";
        }

        bool useStdGlobalUsing = (includeStd && stdDir is not null) || isCompilingStdItself;
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                useStdGlobalUsing ? GlobalUsingsWithStd : GlobalUsingsCoreOnly,
                parseOptions,
                path: "__VibraniumGlobalUsings.g.cs"),
        };

        var addedTreesByPath = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        var trustedManagedAdapterTrees = new HashSet<SyntaxTree>();
        var trustedSysCatalogTrees = new HashSet<SyntaxTree>();
        var officialStdTrees = new HashSet<SyntaxTree>();

        if ((includeStd || isCompilingStdItself) && officialStdDir is not null)
        {
            string stdManifest = Path.Combine(officialStdDir, "vib.manifest");
            if (File.Exists(stdManifest))
            {
                var stdUnits = LoadManifestClosure(stdManifest);
                AddManifestUnitsWithTrust(
                    stdUnits,
                    officialStdDir,
                    includeStd: true,
                    isCompilingStdItself,
                    parseOptions,
                    syntaxTrees,
                    addedTreesByPath,
                    trustedManagedAdapterTrees,
                    trustedSysCatalogTrees,
                    officialStdTrees);
            }
            else if (isCompilingStdItself || includeStd)
            {
                AddDirectorySources(officialStdDir, parseOptions, syntaxTrees, addedTreesByPath);
            }
        }

        if (!isCompilingStdItself)
        {
            foreach (string rawInput in inputPaths)
            {
                string fullPath = Path.GetFullPath(rawInput);
                if (File.Exists(fullPath))
                {
                    if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        string projDir = Path.GetDirectoryName(fullPath)!;
                        string projManifest = Path.Combine(projDir, "vib.manifest");
                        if (File.Exists(projManifest))
                        {
                            var units = LoadManifestClosure(projManifest);
                            AddManifestUnitsWithTrust(
                                units,
                                officialStdDir,
                                includeStd,
                                isCompilingStdItself: false,
                                parseOptions,
                                syntaxTrees,
                                addedTreesByPath,
                                trustedManagedAdapterTrees,
                                trustedSysCatalogTrees,
                                officialStdTrees);
                        }
                        else
                        {
                            AddDirectorySources(projDir, parseOptions, syntaxTrees, addedTreesByPath);
                        }
                    }
                    else
                    {
                        AddSourceFileOnce(fullPath, parseOptions, syntaxTrees, addedTreesByPath);
                    }
                }
                else if (Directory.Exists(fullPath))
                {
                    AddDirectorySources(fullPath, parseOptions, syntaxTrees, addedTreesByPath);
                }
                else
                {
                    throw new FileNotFoundException($"Input path not found: {rawInput}");
                }
            }
        }

        var compilation = BuildCompilation(assemblyName, syntaxTrees, isLibrary: isCompilingStdItself);
        VibraniumFrontend.RegisterTrustedTrees(
            compilation,
            trustedManagedAdapterTrees,
            trustedSysCatalogTrees,
            officialStdTrees,
            trustedStdDirectory: officialStdDir);
        return compilation;
    }

    public static IReadOnlyList<VibraniumManifestUnit> LoadManifestClosure(string manifestPath)
    {
        var ordered = new List<VibraniumManifestUnit>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        VisitManifest(Path.GetFullPath(manifestPath), visited, visiting, ordered);
        return ordered;
    }

    private static void VisitManifest(
        string fullManifestPath,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<VibraniumManifestUnit> ordered)
    {
        if (visited.Contains(fullManifestPath))
        {
            return;
        }
        if (!visiting.Add(fullManifestPath))
        {
            throw new InvalidOperationException($"Cyclic 'use' dependency in manifest: {fullManifestPath}");
        }
        if (!File.Exists(fullManifestPath))
        {
            throw new FileNotFoundException($"Manifest not found: {fullManifestPath}");
        }

        string baseDir = Path.GetDirectoryName(fullManifestPath)!;
        string unitKind = "app";
        var uses = new List<string>();
        var sources = new List<string>();
        var managedSources = new List<string>();
        var nativeFragments = new List<string>();

        string[] lines = File.ReadAllLines(fullManifestPath);
        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int sp = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    sp = i;
                    break;
                }
            }
            if (sp <= 0)
            {
                throw new InvalidOperationException($"{fullManifestPath}({lineIdx + 1},1): invalid manifest directive '{line}'.");
            }

            string directive = line.Substring(0, sp);
            string arg = line.Substring(sp + 1).Trim();
            if (arg.Length == 0)
            {
                throw new InvalidOperationException($"{fullManifestPath}({lineIdx + 1},1): missing argument for '{directive}'.");
            }

            switch (directive)
            {
                case "unit":
                    if (arg is not ("core" or "std" or "app"))
                    {
                        throw new InvalidOperationException($"{fullManifestPath}({lineIdx + 1},1): unknown unit kind '{arg}'.");
                    }
                    unitKind = arg;
                    break;

                case "use":
                {
                    string depTarget = Path.GetFullPath(Path.Combine(baseDir, arg));
                    string depManifest = Directory.Exists(depTarget)
                        ? Path.Combine(depTarget, "vib.manifest")
                        : (File.Exists(depTarget) ? depTarget : Path.Combine(depTarget, "vib.manifest"));
                    uses.Add(Path.GetFullPath(depManifest));
                    break;
                }

                case "source":
                {
                    string srcPath = Path.GetFullPath(Path.Combine(baseDir, arg));
                    if (!File.Exists(srcPath))
                    {
                        throw new FileNotFoundException($"Manifest source not found: {srcPath}");
                    }
                    sources.Add(srcPath);
                    break;
                }

                case "managed":
                {
                    string mPath = Path.GetFullPath(Path.Combine(baseDir, arg));
                    if (!File.Exists(mPath))
                    {
                        throw new FileNotFoundException($"Manifest managed source not found: {mPath}");
                    }
                    managedSources.Add(mPath);
                    break;
                }

                case "native":
                {
                    string nPath = Path.GetFullPath(Path.Combine(baseDir, arg));
                    if (!File.Exists(nPath))
                    {
                        throw new FileNotFoundException($"Manifest native fragment not found: {nPath}");
                    }
                    nativeFragments.Add(nPath);
                    break;
                }

                default:
                    throw new InvalidOperationException($"{fullManifestPath}({lineIdx + 1},1): unknown manifest directive '{directive}'.");
            }
        }

        foreach (string dep in uses)
        {
            VisitManifest(dep, visited, visiting, ordered);
        }

        visiting.Remove(fullManifestPath);
        visited.Add(fullManifestPath);
        ordered.Add(new VibraniumManifestUnit(fullManifestPath, unitKind, sources, managedSources, nativeFragments));
    }

    private static CSharpCompilation CreateCompilationFromManifest(
        string rootManifestPath,
        CSharpParseOptions parseOptions,
        string assemblyName,
        bool includeStd,
        string? sysroot)
    {
        string? officialStdDir = ResolveVibraniumStdSourceDirectory(rootManifestPath, sysroot);
        var units = LoadManifestClosure(rootManifestPath);
        var rootUnit = units[units.Count - 1];
        bool isCompilingStdItself =
            string.Equals(rootUnit.UnitKind, "std", StringComparison.Ordinal) &&
            officialStdDir is not null &&
            PathsEqual(Path.GetDirectoryName(rootUnit.ManifestPath)!, officialStdDir);
        if (isCompilingStdItself && string.Equals(assemblyName, "VibraniumProgram", StringComparison.Ordinal))
        {
            assemblyName = "Vibranium.Std";
        }

        bool hasStdUnit = units.Any(u =>
            string.Equals(u.UnitKind, "std", StringComparison.Ordinal) &&
            officialStdDir is not null &&
            PathsEqual(Path.GetDirectoryName(u.ManifestPath)!, officialStdDir));
        bool useStdGlobalUsing = (includeStd && hasStdUnit) || isCompilingStdItself;

        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                useStdGlobalUsing ? GlobalUsingsWithStd : GlobalUsingsCoreOnly,
                parseOptions,
                path: "__VibraniumGlobalUsings.g.cs"),
        };

        var addedTreesByPath = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        var trustedManagedAdapterTrees = new HashSet<SyntaxTree>();
        var trustedSysCatalogTrees = new HashSet<SyntaxTree>();
        var officialStdTrees = new HashSet<SyntaxTree>();

        AddManifestUnitsWithTrust(
            units,
            officialStdDir,
            includeStd,
            isCompilingStdItself,
            parseOptions,
            syntaxTrees,
            addedTreesByPath,
            trustedManagedAdapterTrees,
            trustedSysCatalogTrees,
            officialStdTrees);

        bool isLibrary = isCompilingStdItself || string.Equals(rootUnit.UnitKind, "core", StringComparison.Ordinal);
        var compilation = BuildCompilation(assemblyName, syntaxTrees, isLibrary);
        VibraniumFrontend.RegisterTrustedTrees(
            compilation,
            trustedManagedAdapterTrees,
            trustedSysCatalogTrees,
            officialStdTrees,
            trustedStdDirectory: officialStdDir);
        return compilation;
    }

    private static void AddManifestUnitsWithTrust(
        IReadOnlyList<VibraniumManifestUnit> units,
        string? officialStdDir,
        bool includeStd,
        bool isCompilingStdItself,
        CSharpParseOptions parseOptions,
        List<SyntaxTree> syntaxTrees,
        Dictionary<string, SyntaxTree> addedTreesByPath,
        HashSet<SyntaxTree> trustedManagedAdapterTrees,
        HashSet<SyntaxTree> trustedSysCatalogTrees,
        HashSet<SyntaxTree> officialStdTrees)
    {
        string? expectedSysCatalogPath = officialStdDir is not null
            ? Path.GetFullPath(Path.Combine(officialStdDir, "Sys", "Sys.cs"))
            : null;

        foreach (var unit in units)
        {
            bool isOfficialCoreUnit = unit.UnitKind == "core" && officialStdDir is not null &&
                PathsEqual(unit.ManifestPath, Path.Combine(officialStdDir, "..", "Vibranium.Core", "vib.manifest"));
            bool isOfficialStdUnit = unit.UnitKind == "std" && officialStdDir is not null &&
                PathsEqual(unit.ManifestPath, Path.Combine(officialStdDir, "vib.manifest"));
            if (unit.UnitKind is "core" or "std" && !isOfficialCoreUnit && !isOfficialStdUnit)
                throw new InvalidOperationException("error VIB000: Only the selected sysroot manifests may declare core or std units.");
            if (!isOfficialCoreUnit && !isOfficialStdUnit && unit.NativeFragments.Count > 0)
                throw new InvalidOperationException("error VIB000: Native fragments are permitted only in the selected sysroot manifests.");
            if (isOfficialCoreUnit) continue;

            if (!isOfficialStdUnit && unit.ManagedSources.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{unit.ManifestPath}: 'managed' directives are restricted to the official Vibranium.Std manifest.");
            }

            if (!includeStd && !isCompilingStdItself && isOfficialStdUnit)
            {
                continue;
            }

            foreach (string src in unit.Sources)
            {
                var tree = AddSourceFileOnce(src, parseOptions, syntaxTrees, addedTreesByPath);
                if (isOfficialStdUnit)
                {
                    officialStdTrees.Add(tree);
                    if (expectedSysCatalogPath is not null && PathsEqual(src, expectedSysCatalogPath))
                    {
                        trustedSysCatalogTrees.Add(tree);
                    }
                }
            }

            foreach (string msrc in unit.ManagedSources)
            {
                var tree = AddSourceFileOnce(msrc, parseOptions, syntaxTrees, addedTreesByPath);
                if (isOfficialStdUnit)
                {
                    trustedManagedAdapterTrees.Add(tree);
                }
            }
        }
    }

    private static CSharpCompilation BuildCompilation(
        string assemblyName,
        List<SyntaxTree> syntaxTrees,
        bool isLibrary)
    {
        var references = GetPlatformAndCoreReferences();
        var outputKind = isLibrary
            ? OutputKind.DynamicallyLinkedLibrary
            : OutputKind.ConsoleApplication;
        var options = new CSharpCompilationOptions(
            outputKind,
            nullableContextOptions: NullableContextOptions.Enable,
            checkOverflow: false,
            allowUnsafe: false,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                ["CS0183"] = ReportDiagnostic.Error,
                ["CS0184"] = ReportDiagnostic.Error,
                ["CS8509"] = ReportDiagnostic.Error,
                ["CS8519"] = ReportDiagnostic.Error,
                ["CS8520"] = ReportDiagnostic.Error,
                ["CS8524"] = ReportDiagnostic.Error,
                ["CS8778"] = ReportDiagnostic.Error,
                ["CS8793"] = ReportDiagnostic.Error,
                ["CS8794"] = ReportDiagnostic.Error,
                ["CS8846"] = ReportDiagnostic.Error,
                ["CS9191"] = ReportDiagnostic.Error,
                ["CS9192"] = ReportDiagnostic.Error,
                ["CS9193"] = ReportDiagnostic.Error,
            });

        return CSharpCompilation.Create(assemblyName, syntaxTrees, references, options);
    }

    public static VibraniumCompileOutcome CheckAndEmitIr(
        CSharpCompilation compilation,
        LlvmEmitOptions? emitOptions = null)
    {
        var errors = new List<string>();

        if (VibraniumFrontend.HasDeclarationCuts(compilation))
        {
            var rejected = VibraniumFrontend.CompileAndCheck(compilation);
            errors.AddRange(rejected.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            return new VibraniumCompileOutcome(false, errors, rejected.Module, null);
        }

        foreach (var diag in compilation.GetDiagnostics())
        {
            if (VibraniumFrontend.IsPromotedSemanticWarning(diag.Id)) continue;
            if (diag.Severity == DiagnosticSeverity.Error || diag.IsWarningAsError)
            {
                errors.Add(diag.ToString());
            }
        }

        if (errors.Count > 0)
        {
            return new VibraniumCompileOutcome(false, errors, null, null);
        }

        var checkResult = VibraniumFrontend.CompileAndCheck(compilation);
        if (checkResult.Diagnostics.Count > 0)
        {
            foreach (var d in checkResult.Diagnostics)
            {
                errors.Add(d.ToString());
            }
            return new VibraniumCompileOutcome(false, errors, checkResult.Module, null);
        }

        string ir = Llvm18Backend.EmitModuleIr(checkResult.Module, emitOptions);
        return new VibraniumCompileOutcome(true, errors, checkResult.Module, ir);
    }

    private static string? ResolveVibraniumStdSourceDirectory(string hintPath, string? sysroot)
    {
        if (!string.IsNullOrWhiteSpace(sysroot))
        {
            string fullSysroot = Path.GetFullPath(sysroot!);
            string directCandidate = Path.Combine(fullSysroot, "Vibranium.Std");
            if (Directory.Exists(directCandidate))
            {
                return directCandidate;
            }
            string srcCandidate = Path.Combine(fullSysroot, "src", "Vibranium.Std");
            if (Directory.Exists(srcCandidate))
            {
                return srcCandidate;
            }
        }

        string?[] roots =
        {
            AppContext.BaseDirectory,
            File.Exists(hintPath) ? Path.GetDirectoryName(hintPath) : hintPath,
            Environment.CurrentDirectory,
        };

        foreach (string? start in roots)
        {
            if (string.IsNullOrEmpty(start))
            {
                continue;
            }

            DirectoryInfo? dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "src", "Vibranium.Std");
                if (File.Exists(Path.Combine(candidate, "Vibranium.Std.csproj")) ||
                    File.Exists(Path.Combine(candidate, "vib.manifest")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }

    private static bool PathsEqual(string a, string b)
    {
        string na = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        string nb = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        return string.Equals(na, nb, StringComparison.Ordinal);
    }

    private static SyntaxTree AddSourceFileOnce(
        string fullFilePath,
        CSharpParseOptions parseOptions,
        List<SyntaxTree> trees,
        Dictionary<string, SyntaxTree> addedTreesByPath)
    {
        string normalized = Path.GetFullPath(fullFilePath);
        if (!addedTreesByPath.TryGetValue(normalized, out var tree))
        {
            tree = CSharpSyntaxTree.ParseText(File.ReadAllText(normalized), parseOptions, path: normalized);
            addedTreesByPath[normalized] = tree;
            trees.Add(tree);
        }
        return tree;
    }

    private static void AddDirectorySources(
        string dir,
        CSharpParseOptions parseOptions,
        List<SyntaxTree> trees,
        Dictionary<string, SyntaxTree> addedTreesByPath)
    {
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(dir, file);
            if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }
            AddSourceFileOnce(file, parseOptions, trees, addedTreesByPath);
        }
    }

    private static List<MetadataReference> GetPlatformAndCoreReferences()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpa))
        {
            foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(path), "Vibranium.Std", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (seen.Add(path) && File.Exists(path))
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        string corePath = typeof(Vibranium.Option<>).Assembly.Location;
        if (seen.Add(corePath) && File.Exists(corePath))
        {
            refs.Add(MetadataReference.CreateFromFile(corePath));
        }

        return refs;
    }
}
