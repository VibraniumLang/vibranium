using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.CodeAnalysis;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;

if (args.Length < 2)
{
    PrintUsage();
    return 2;
}

string command = args[0];
var inputPaths = new List<string>();
string? outputPath = null;
string? emitLlPath = null;
string? clangPath = null;
string? sysrootPath = null;
int optLevel = 3;
bool asan = false;
bool sharedNoAlias = false;
bool includeStd = true;
var appArgs = new List<string>();

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--":
            for (int j = i + 1; j < args.Length; j++)
            {
                appArgs.Add(args[j]);
            }
            i = args.Length;
            break;
        case "-o" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--emit-ll" when i + 1 < args.Length:
            emitLlPath = args[++i];
            break;
        case "--cc" when i + 1 < args.Length:
            clangPath = args[++i];
            break;
        case "--sysroot" when i + 1 < args.Length:
            sysrootPath = args[++i];
            break;
        case "-O0":
            optLevel = 0;
            break;
        case "-O1":
            optLevel = 1;
            break;
        case "-O2":
            optLevel = 2;
            break;
        case "-O3":
            optLevel = 3;
            break;
        case "--asan":
            asan = true;
            break;
        case "--shared-noalias":
            sharedNoAlias = true;
            break;
        case "--no-std":
            includeStd = false;
            break;
        default:
            if (args[i].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                return 2;
            }
            inputPaths.Add(args[i]);
            break;
    }
}

if (inputPaths.Count == 0)
{
    PrintUsage();
    return 2;
}

try
{
    var compilation = VibraniumCompilerDriver.CreateCompilationFromPaths(
        inputPaths,
        includeStd: includeStd,
        sysroot: sysrootPath);

    if (command == "check")
    {
        if (VibraniumFrontend.HasDeclarationCuts(compilation))
        {
            foreach (var diagnostic in VibraniumFrontend.CompileAndCheck(compilation).Diagnostics)
            {
                Console.Error.WriteLine(diagnostic.ToString());
            }
            return 1;
        }
        var errors = compilation.GetDiagnostics();
        bool hasCSharpErrors = false;
        foreach (var d in errors)
        {
            if (VibraniumFrontend.IsPromotedSemanticWarning(d.Id)) continue;
            if (d.Severity == DiagnosticSeverity.Error || d.IsWarningAsError)
            {
                Console.Error.WriteLine(d.ToString());
                hasCSharpErrors = true;
            }
        }
        if (hasCSharpErrors)
        {
            return 1;
        }

        var res = VibraniumFrontend.CompileAndCheck(compilation);
        if (res.Diagnostics.Count > 0)
        {
            foreach (var d in res.Diagnostics)
            {
                Console.Error.WriteLine(d.ToString());
            }
            return 1;
        }
        return 0;
    }

    var emitOpts = new LlvmEmitOptions(
        SharedNoAlias: sharedNoAlias,
        AddressSanitizer: asan,
        IncludeStdNative: includeStd ? null : false);
    var outcome = VibraniumCompilerDriver.CheckAndEmitIr(compilation, emitOpts);
    if (!outcome.Success || outcome.LlvmIr is null)
    {
        foreach (string err in outcome.Errors)
        {
            Console.Error.WriteLine(err);
        }
        return 1;
    }

    if (command is "emit-ll" or "emit-llvm")
    {
        if (outputPath is not null)
        {
            File.WriteAllText(outputPath, outcome.LlvmIr);
        }
        else
        {
            Console.Out.Write(outcome.LlvmIr);
        }
        return 0;
    }

    if (command is "build" or "run")
    {
        if (emitLlPath is not null)
        {
            File.WriteAllText(emitLlPath, outcome.LlvmIr);
        }

        string binPath = outputPath ?? Path.Combine(Path.GetTempPath(), $"vib-bin-{Guid.NewGuid():N}");
        var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(
            outcome.LlvmIr,
            binPath,
            optLevel,
            asan,
            clangPath);
        if (clangExit != 0)
        {
            Console.Error.WriteLine(clangOut);
            return 1;
        }

        if (command == "build")
        {
            if (outputPath is null)
            {
                Console.WriteLine(binPath);
            }
            return 0;
        }

        try
        {
            var psi = new ProcessStartInfo(binPath)
            {
                UseShellExecute = false,
            };
            foreach (string a in appArgs)
            {
                psi.ArgumentList.Add(a);
            }
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        finally
        {
            if (outputPath is null && File.Exists(binPath))
            {
                File.Delete(binPath);
            }
        }
    }

    PrintUsage();
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"vib error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: vib <check|emit-ll|build|run> <path...> [-o output] [-O0..-O3] [--cc clang] [--sysroot dir] [--asan] [--shared-noalias] [--no-std] [-- args...]");
}
