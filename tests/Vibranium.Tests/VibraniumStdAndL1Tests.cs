using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vibranium.Analyzer;
using Vibranium.Backend.Llvm;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Vibranium.Tests;

public sealed class VibraniumStdAndL1Tests
{
    private static readonly string DotnetExe = ResolveDotnetExe();

    private static string ResolveDotnetExe()
    {
        try
        {
            var (exit, stdout, _) = RunProcess("dotnet", setAsanOptions: false, "--version");
            if (exit == 0)
            {
                string ver = stdout.Trim();
                int dot = ver.IndexOf('.');
                if (dot > 0 && int.TryParse(ver.AsSpan(0, dot), out int major) && major >= 11)
                {
                    return "dotnet";
                }
            }
        }
        catch
        {
            // Fall back to ~/.dotnet/dotnet when `dotnet` is not on PATH.
        }

        string userDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
        return System.IO.File.Exists(userDotnet) ? userDotnet : "dotnet";
    }

    private static string RepoRoot
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            while (!System.IO.File.Exists(Path.Combine(dir, "Vibranium.slnx")))
            {
                dir = Directory.GetParent(dir)!.FullName;
            }
            return dir;
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string fileName,
        bool setAsanOptions,
        params string[] args) =>
        RunProcessWithStdin(fileName, setAsanOptions, stdinBytes: null, args);

    private static (int ExitCode, string Stdout, string Stderr) RunProcessWithStdin(
        string fileName,
        bool setAsanOptions,
        byte[]? stdinBytes,
        params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = stdinBytes is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (setAsanOptions)
        {
            psi.Environment["ASAN_OPTIONS"] = "detect_leaks=1:abort_on_error=1";
        }
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = System.Diagnostics.Process.Start(psi)!;
        if (stdinBytes is not null)
        {
            proc.StandardInput.BaseStream.Write(stdinBytes, 0, stdinBytes.Length);
            proc.StandardInput.BaseStream.Flush();
            proc.StandardInput.Close();
        }
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    private static (int ManagedExit, string ManagedOut, string ManagedErr, int NativeExit, string NativeOut, string NativeErr, string LlvmIr)
        CompileAndRunBoth(string sourceCode, string[]? appArgs = null, bool withAsan = true, byte[]? stdinBytes = null)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-v02-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcFile = Path.Combine(tempDir, "Program.cs");
            System.IO.File.WriteAllText(srcFile, sourceCode);

            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(srcFile, "VibraniumStdDiffApp", includeStd: true);
            var outcome = VibraniumCompilerDriver.CheckAndEmitIr(
                compilation,
                new LlvmEmitOptions(SharedNoAlias: false, AddressSanitizer: withAsan));
            Assert.True(outcome.Success, string.Join("\n", outcome.Errors));
            Assert.NotNull(outcome.LlvmIr);

            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = compilation.SyntaxTrees.ToList();
            if (System.IO.File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    System.IO.File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedCompilation = CSharpCompilation.Create(
                "VibraniumStdDiffManaged",
                managedTrees,
                compilation.References,
                compilation.Options);

            string dllPath = Path.Combine(tempDir, "VibraniumStdDiffManaged.dll");
            var emitRes = managedCompilation.Emit(dllPath);
            Assert.True(emitRes.Success, string.Join("\n", emitRes.Diagnostics));

            string coreDll = typeof(Vibranium.Option<>).Assembly.Location;
            System.IO.File.Copy(coreDll, Path.Combine(tempDir, "Vibranium.Core.dll"), overwrite: true);
            string runtimeConfig = """
                {
                  "runtimeOptions": {
                    "tfm": "net11.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "11.0.0"
                    },
                    "configProperties": {
                      "System.Globalization.Invariant": true,
                      "System.IO.DisableFileLocking": true
                    }
                  }
                }
                """;
            System.IO.File.WriteAllText(Path.Combine(tempDir, "VibraniumStdDiffManaged.runtimeconfig.json"), runtimeConfig);



            string[] cli = appArgs ?? [];
            string[] managedFullArgs = new string[1 + cli.Length];
            managedFullArgs[0] = dllPath;
            Array.Copy(cli, 0, managedFullArgs, 1, cli.Length);
            var (mExit, mOut, mErr) = RunProcessWithStdin(DotnetExe, setAsanOptions: false, stdinBytes, managedFullArgs);

            string binPath = Path.Combine(tempDir, "native_bin");
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(outcome.LlvmIr!, binPath, optLevel: 3, addressSanitizer: withAsan);
            Assert.True(clangExit == 0, clangOut);

            var (nExit, nOut, nErr) = RunProcessWithStdin(binPath, setAsanOptions: withAsan, stdinBytes, cli);
            return (mExit, mOut, mErr, nExit, nOut, nErr, outcome.LlvmIr!);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static IReadOnlyList<Vibranium.Compiler.Core.VibraniumDiagnostic> CheckVibraniumDiagnostics(string sourceCode, string assemblyName = "VibraniumCheckApp")
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-chk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcFile = Path.Combine(tempDir, "Program.cs");
            System.IO.File.WriteAllText(srcFile, sourceCode);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(srcFile, assemblyName, includeStd: true);
            var csErrors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error || d.IsWarningAsError)
                .ToList();
            Assert.Empty(csErrors);
            return VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // =========================================================================
    // Milestone M1 Tests
    // =========================================================================

    [Fact]
    public void M1_Utf8Literals_StaticProvenance_EmptyNonNull_And_Deduplication()
    {
        string src = """
            ReadOnlySpan<byte> empty = ""u8;
            ReadOnlySpan<byte> slicedEmpty = empty.Slice(0, 0);
            ReadOnlySpan<byte> tail = StaticTail();
            ReadOnlySpan<byte> a = "dedup-literal"u8;
            ReadOnlySpan<byte> b = "dedup-literal"u8;
            ReadOnlySpan<byte> utf8 = "λ→🦀"u8;
            WriteLine(slicedEmpty.Length);
            WriteLine(Bytes.Eq(a, b));
            WriteLine(utf8.Length);
            Stdio.WriteOut(tail);
            Stdio.WriteOut("\n"u8);
            return 0;

            static ReadOnlySpan<byte> StaticTail() => "hello-vibranium"u8.Slice(6, 9);
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("0\nTrue\n9\nvibranium\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);

        // Verify empty ""u8 uses non-null @.b_empty and "dedup-literal"u8 is pooled once
        Assert.Contains("ptr @.b_empty, i32 0", res.LlvmIr);
        int dedupCount = res.LlvmIr.Split("dedup-literal", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, dedupCount);
    }

    [Fact]
    public void M1_Unit_PreludeOk_And_StdioWriteOutErr_Parity()
    {
        string src = """
            Stdio.WriteOut(""u8);
            Stdio.WriteErr(""u8);
            if (Step(true).IsErr(out Unit _, out var err1))
            {
                WriteLine(err1.Error);
            }
            else
            {
                Stdio.WriteOut("stdout-ok\n"u8);
            }
            if (Step(false).IsErr(out Unit _, out var err2))
            {
                Stdio.WriteErr("stderr-err\n"u8);
                WriteLine(err2.Error);
            }
            return 0;

            static Result<Unit, int> Step(bool ok) => ok ? Ok() : Err(77);
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("stdout-ok\n77\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal("stderr-err\n", res.NativeErr);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void M1_Vec_Clear_Truncate_Reserve_Extend_Resize_Differential_And_ZeroLenEmptyExtend()
    {
        string src = """
            // Extending an unallocated 0-capacity Vec with an empty span must never trigger UB
            var emptyVec = new Vec<byte>();
            emptyVec.Extend(""u8);
            emptyVec.Reserve(0);
            WriteLine(emptyVec.Count);

            var v = new Vec<int>();
            v.Reserve(10);
            WriteLine(v.Capacity >= 10);
            ReadOnlySpan<int> init = [10, 20, 30];
            v.Extend(init);
            v.Resize(5, 99);
            WriteLine(v.Count);
            WriteLine(v[3]);
            WriteLine(v[4]);
            v.Truncate(5);
            v.Truncate(2);
            WriteLine(v.Count);
            WriteLine(v[1]);
            v.Resize(1, 0);
            WriteLine(v.Count);
            v.Clear();
            WriteLine(v.Count);
            v.Push(42);
            WriteLine(v[0]);
            return 0;
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M1_Vec_TruncateOutOfBounds_And_NegativeResize_PanicParity()
    {
        string truncSrc = """
            var v = new Vec<int>();
            v.Push(1);
            v.Truncate(2);
            return 0;
            """;
        var r1 = CompileAndRunBoth(truncSrc);
        Assert.Equal(101, r1.ManagedExit);
        Assert.Equal(101, r1.NativeExit);
        Assert.Contains("truncate out of bounds", r1.NativeErr);
        Assert.Equal(r1.ManagedErr, r1.NativeErr);

        string resizeSrc = """
            var v = new Vec<int>();
            v.Resize(-1, 5);
            return 0;
            """;
        var r2 = CompileAndRunBoth(resizeSrc);
        Assert.Equal(101, r2.ManagedExit);
        Assert.Equal(101, r2.NativeExit);
        Assert.Contains("Vec length cannot be negative", r2.NativeErr);
        Assert.Equal(r2.ManagedErr, r2.NativeErr);
    }

    [Fact]
    public void M1_Vec_SelfAliasingExtend_And_ClearInvalidatesSpan_Rejected()
    {
        string selfExtend = """
            var v = new Vec<byte>();
            v.Extend("abc"u8);
            v.Extend(v.AsReadOnlySpan());
            return 0;
            """;
        var d1 = CheckVibraniumDiagnostics(selfExtend);
        Assert.Contains(d1, d => d.Id is "VIB002" or "VIB003");

        string clearInvalidates = """
            var v = new Vec<int>();
            v.Push(10);
            Span<int> s = v.AsSpan();
            v.Clear();
            WriteLine(s[0]);
            return 0;
            """;
        var d2 = CheckVibraniumDiagnostics(clearInvalidates);
        Assert.Contains(d2, d => d.Id == "VIB003");
    }

    [Fact]
    public void M1_UnscopedRef_UserStructView_PropagatesReceiverLoan_And_RejectsDanglingTemp()
    {
        string validCode = """
            var sb = new StringBuf(16);
            sb.Append("ok"u8);
            ReadOnlySpan<byte> view = sb.AsUtf8();
            WriteLine(view.Length);
            return 0;
            """;
        Assert.Empty(CheckVibraniumDiagnostics(validCode));

        string mutateWhileBorrowed = """
            var sb = new StringBuf(16);
            sb.Append("ok"u8);
            ReadOnlySpan<byte> view = sb.AsUtf8();
            sb.Clear();
            WriteLine(view.Length);
            return 0;
            """;
        var dMut = CheckVibraniumDiagnostics(mutateWhileBorrowed);
        Assert.Contains(dMut, d => d.Id == "VIB003");

        string userUnscopedStruct = """
            var box = new UserBuf(8);
            ReadOnlySpan<byte> view = box.View();
            box.Reset();
            WriteLine(view.Length);
            return 0;

            struct UserBuf
            {
                private Vec<byte> _data;
                public UserBuf(int cap) { _data = new Vec<byte>(cap); }
                [System.Diagnostics.CodeAnalysis.UnscopedRef]
                public readonly ReadOnlySpan<byte> View() => _data.AsReadOnlySpan();
                public void Reset() => _data.Clear();
            }
            """;
        var dUserStruct = CheckVibraniumDiagnostics(userUnscopedStruct);
        Assert.Contains(dUserStruct, d => d.Id == "VIB003");

        string rvalueTempView = """
            ReadOnlySpan<byte> dangling = new StringBuf(16).AsUtf8();
            WriteLine(dangling.Length);
            return 0;
            """;
        var dTemp = CheckVibraniumDiagnostics(rvalueTempView);
        Assert.Contains(dTemp, d => d.Id == "VIB007");
    }

    [Fact]
    public void M1_VibNoStd_OptOut_ExcludesStdNativeAndCompilesCoreOnly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-nostd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcFile = Path.Combine(tempDir, "Program.cs");
            System.IO.File.WriteAllText(srcFile, """
                Stdio.WriteOut("core-only\n"u8);
                return 0;
                """);
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcFile, "CoreOnlyApp", includeStd: false);
            var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp, new LlvmEmitOptions(IncludeStdNative: false));
            Assert.True(outcome.Success, string.Join("\n", outcome.Errors));
            Assert.DoesNotContain("@__vib_sys_file_open", outcome.LlvmIr!);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // =========================================================================
    // Milestone M2 Tests — Narrow L1 Linear Option<R> / Result<R, E>
    // =========================================================================

    [Fact]
    public void M2_L1_ResultLinearOk_IsErrGuard_Switch_Unwrap_And_Move_ZeroLeaks()
    {
        string src = """
            if (OpenHandle(1, true).IsErr(out var h1, out var e1))
            {
                WriteLine(e1.Error);
            }
            else
            {
                using var g1 = h1;
                WriteLine(g1.Id);
            }

            if (OpenHandle(2, false).IsErr(out var h2, out var e2))
            {
                WriteLine(e2.Error);
            }
            else
            {
                using var g2 = h2;
                WriteLine(g2.Id);
            }

            switch (OpenHandle(3, true))
            {
                case Ok<Handle>(var h3):
                {
                    using var g3 = h3;
                    WriteLine(g3.Id);
                    break;
                }
                case Err<int>(var code):
                    WriteLine(code);
                    break;
            }

            Option<Handle> opt = Some(new Handle(4));
            switch (Move(opt))
            {
                case Some<Handle>(var h4):
                {
                    using var g4 = h4;
                    WriteLine(g4.Id);
                    break;
                }
                case NoneType:
                    WriteLine(-1);
                    break;
            }
            opt = None;
            if (opt.IsNone(out var h4After))
            {
                WriteLine(0);
            }
            else
            {
                using var g4After = h4After;
            }

            using var g5 = OpenHandle(5, true).Unwrap();
            WriteLine(g5.Id);
            return 0;

            static Result<Handle, int> OpenHandle(int id, bool ok) =>
                ok ? Ok(new Handle(id)) : Err(-id);

            sealed class Handle : IDisposable
            {
                public readonly int Id;
                public Handle(int id)
                {
                    Id = id;
                    Write("open:");
                    WriteLine(id);
                }
                public void Dispose()
                {
                    Write("close:");
                    WriteLine(Id);
                }
            }
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M2_L1_Rejects_LinearInErr_Vec_And_NonCopyErr_WithVib006()
    {
        // 1. Linear resource in Result's Err slot
        var dErrSlot = CheckVibraniumDiagnostics("""
            Result<int, File> Bad(File f) => Err(f);
            return 0;
            """);
        Assert.Contains(dErrSlot, d => d.Id == "VIB006");

        // 2. Linear unmanaged struct in Vec<R>
        var dVec = CheckVibraniumDiagnostics("""
            var v = new Vec<LinFd>();
            WriteLine(v.Count);
            return 0;

            struct LinFd : IDisposable
            {
                public int Fd;
                public void Dispose() { }
            }
            """);
        Assert.Contains(dVec, d => d.Id == "VIB006");

        // 3. Linear Ok<R> paired with non-Copy Err<StringBuf>
        var dNonCopyErr = CheckVibraniumDiagnostics("""
            WriteLine(0);
            return 0;

            static Result<File, StringBuf> BadNonCopyErr(File f)
            {
                using var owned = f;
                return Err(new StringBuf(4));
            }
            """);
        Assert.Contains(dNonCopyErr, d => d.Id == "VIB006");
    }

    [Fact]
    public void M2_L1_Rejects_DirectDeconstruct_And_PlaceScrutineeMatch_WithVib006()
    {
        var dDecon = CheckVibraniumDiagnostics("""
            if (File.OpenRead("a"u8).IsErr(out var f, out _)) return 1;
            var wrapped = Some(f);
            wrapped.Deconstruct(out var unwrapped);
            using var g = unwrapped;
            return 0;
            """);
        Assert.Contains(dDecon, d => d.Id == "VIB006");

        var dPlaceMatch = CheckVibraniumDiagnostics("""
            var r = File.OpenRead("missing"u8);
            switch (r)
            {
                case Ok<File>(var f):
                {
                    using var g = f;
                    break;
                }
                case Err<IoError>:
                    break;
            }
            return 0;
            """);
        Assert.Contains(dPlaceMatch, d => d.Id == "VIB006");
    }

    [Fact]
    public void M2_L1_Rejects_UnwrapOr_Take_Replace_AsRef_AsMut_UnwrapMut_And_NestedPattern_WithVib006()
    {
        string[] forbiddenPrograms =
        [
            """
            Option<File> a = None;
            Option<File> b = None;
            using var f = a.UnwrapOr(b.Unwrap());
            return 0;
            """,
            """
            Result<File, IoError> r = File.OpenRead("x"u8);
            Option<File> b = None;
            using var f = r.UnwrapOr(b.Unwrap());
            return 0;
            """,
            """
            Option<File> a = None;
            Option<File> b = a.Take();
            if (!b.IsNone(out var f)) { using var g = f; }
            return 0;
            """,
            """
            Option<File> a = None;
            Option<File> prev = a.Replace(None);
            if (!prev.IsNone(out var f)) { using var g = f; }
            return 0;
            """,
            """
            Option<File> a = None;
            var r = a.AsRef();
            if (!a.IsNone(out var f)) { using var g = f; }
            return 0;
            """,
            """
            Option<File> a = None;
            var r = a.AsMut();
            if (!a.IsNone(out var f)) { using var g = f; }
            return 0;
            """,
            """
            Option<File> a = None;
            ref File rf = ref a.UnwrapMut();
            if (!a.IsNone(out var f)) { using var g = f; }
            return 0;
            """,
            """
            switch (MakeBox(1))
            {
                case Some<LinBox>({ Tag: 1 }):
                    return 1;
                default:
                    return 0;
            }
            static Option<LinBox> MakeBox(int t) => Some(new LinBox(t));
            sealed class LinBox : IDisposable
            {
                public readonly int Tag;
                public LinBox(int tag) { Tag = tag; }
                public void Dispose() { }
            }
            """,
        ];

        foreach (string prog in forbiddenPrograms)
        {
            var diags = CheckVibraniumDiagnostics(prog);
            Assert.Contains(diags, d => d.Id == "VIB006");
        }
    }

    [Fact]
    public void M2_L1_Rejects_DiscardAssignment_UnconsumedLocal_And_OutDiscard_WithVib006()
    {
        // 1. _ = File.OpenRead(...)
        var dDiscardAssign = CheckVibraniumDiagnostics("""
            _ = File.OpenRead("test.txt"u8);
            return 0;
            """);
        Assert.Contains(dDiscardAssign, d => d.Id == "VIB006");

        // 2. Unconsumed Result<File, IoError> local
        var dUnconsumed = CheckVibraniumDiagnostics("""
            var res = File.OpenRead("test.txt"u8);
            return 0;
            """);
        Assert.Contains(dUnconsumed, d => d.Id == "VIB006");

        // 3. Discarding linear Ok value via `out _` in IsErr
        var dOutDiscard = CheckVibraniumDiagnostics("""
            if (File.OpenRead("test.txt"u8).IsErr(out _, out var err))
            {
                return 1;
            }
            return 0;
            """);
        Assert.Contains(dOutDiscard, d => d.Id == "VIB006");
    }

    [Fact]
    public void M2_L1_Rejects_ExplicitDisposeCall_WithVib006()
    {
        var diags = CheckVibraniumDiagnostics("""
            if (File.OpenRead("test.txt"u8).IsErr(out var f, out _)) return 1;
            f.Dispose();
            return 0;
            """);
        Assert.Contains(diags, d => d.Id == "VIB006");
    }

    // =========================================================================
    // Milestone M3 Tests — Vibranium.Std Modules, Zero-C Invariant, Catalog & HTTP
    // =========================================================================

    [Fact]
    public void M3_VibraniumStd_SelfCheck_ZeroDiagnostics_And_ZeroCFiles()
    {
        string stdProj = Path.Combine(RepoRoot, "src", "Vibranium.Std", "Vibranium.Std.csproj");
        var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(stdProj);
        var csErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.IsWarningAsError)
            .ToList();
        Assert.Empty(csErrors);

        var vibRes = VibraniumFrontend.CompileAndCheck(compilation);
        Assert.Empty(vibRes.Diagnostics);

        string srcDir = Path.Combine(RepoRoot, "src");
        string[] cFiles = Directory.GetFiles(srcDir, "*.c", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(srcDir, "*.h", SearchOption.AllDirectories))
            .ToArray();
        Assert.Empty(cFiles);
    }

    [Fact]
    public void M3_NativeAttribute_RejectedOutsideVibraniumStdSys_And_UnknownSysSymbolRejected()
    {
        var dUserNative = CheckVibraniumDiagnostics("""
            WriteLine(Foreign.Puts());
            return 0;

            static partial class Foreign
            {
                [Native("puts")]
                public static partial int Puts();
                public static partial int Puts() => 0;
            }
            """);
        Assert.Contains(dUserNative, d => d.Id == "VIB000");

        var dBogusSys = CheckVibraniumDiagnostics("""
            namespace Vibranium.Std
            {
                public static partial class Sys
                {
                    [Native("__vib_sys_not_in_catalog")]
                    internal static partial int NotInCatalog();
                    internal static partial int NotInCatalog() => 0;
                }
            }
            class App
            {
                static int Main() => 0;
            }
            """);
        Assert.Contains(dBogusSys, d => d.Id == "VIB000");
    }

    [Fact]
    public void M3_Text_Utf8_Bytes_And_StringBuf_Differential_And_BoundaryTests()
    {
        string src = """
            WriteLine(Utf8.IsValid(""u8));
            WriteLine(Utf8.IsValid("ASCII + λ + 🦀"u8));

            // Invalid UTF-8: overlong 2-byte, UTF-16 surrogate U+D800, > U+10FFFF, truncated 3-byte
            ReadOnlySpan<byte> overlong = [0xC0, 0xAF];
            ReadOnlySpan<byte> surrogate = [0xED, 0xA0, 0x80];
            ReadOnlySpan<byte> tooHigh = [0xF4, 0x90, 0x80, 0x80];
            ReadOnlySpan<byte> truncated = [0xE2, 0x82];
            WriteLine(Utf8.IsValid(overlong));
            WriteLine(Utf8.IsValid(surrogate));
            WriteLine(Utf8.IsValid(tooHigh));
            WriteLine(Utf8.IsValid(truncated));

            ReadOnlySpan<byte> crab = "🦀A"u8;
            WriteLine(Utf8.IsBoundary(crab, 0));
            WriteLine(Utf8.IsBoundary(crab, 1));
            WriteLine(Utf8.IsBoundary(crab, 2));
            WriteLine(Utf8.IsBoundary(crab, 3));
            WriteLine(Utf8.IsBoundary(crab, 4));
            WriteLine(Utf8.IsBoundary(crab, 5));

            WriteLine(Bytes.EqIgnoreAsciiCase("Content-Length"u8, "content-LENGTH"u8));
            WriteLine(Bytes.Eq(Bytes.TrimAscii(" \t value \t "u8), "value"u8));
            WriteLine(Bytes.IndexOf("abc\r\n\r\ndef"u8, "\r\n\r\n"u8));
            WriteLine(Bytes.ParseInt("-2147483648"u8).Unwrap());
            WriteLine(Bytes.ParseInt("2147483647"u8).Unwrap());
            WriteLine(Bytes.ParseInt("2147483648"u8).IsSome);
            WriteLine(Bytes.ParseInt("-"u8).IsSome);

            var sb = new StringBuf(16);
            sb.Append("num="u8);
            sb.AppendLong(-9223372036854775808L);
            sb.AppendAscii((byte)',');
            sb.AppendHex(0x1A2BL, 6);
            sb.AppendRune(0x1F980);
            WriteLine(sb.TryAppend(surrogate));
            Stdio.WriteOut(sb.AsUtf8());
            Stdio.WriteOut("\n"u8);
            sb.Truncate(4);
            Stdio.WriteOut(sb.AsUtf8());
            Stdio.WriteOut("\n"u8);
            return 0;
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M3_Collections_IntMap_StrMap_And_Interner_Differential_TombstoneAndRehash()
    {
        string src = """
            var imap = new IntMap<long>(4);
            for (int i = 0; i < 150; i++)
            {
                imap.Set(i * 17, i * 100L);
            }
            WriteLine(imap.Count);
            WriteLine(imap.Get(170).Unwrap());
            WriteLine(imap.Remove(170).Unwrap());
            WriteLine(imap.Remove(170).IsSome);
            WriteLine(imap.Contains(170));
            imap.Set(170, 9999L);
            WriteLine(imap.Get(170).Unwrap());
            WriteLine(imap.Count);
            imap.Clear();
            WriteLine(imap.Count);

            var smap = new StrMap<int>(4);
            var keyBuf = new StringBuf(16);
            for (int i = 0; i < 120; i++)
            {
                keyBuf.Clear();
                keyBuf.Append("k_"u8);
                keyBuf.AppendInt(i);
                smap.Set(keyBuf.AsUtf8(), i + 1);
            }
            WriteLine(smap.Count);
            WriteLine(smap.Get("k_0"u8).Unwrap());
            WriteLine(smap.Get("k_119"u8).Unwrap());
            WriteLine(smap.Remove("k_50"u8).Unwrap());
            WriteLine(smap.Contains("k_50"u8));
            smap.Set("k_50"u8, 777);
            WriteLine(smap.Get("k_50"u8).Unwrap());

            var interner = new Interner(8);
            int idA = interner.Intern("alpha"u8);
            int idB = interner.Intern("beta"u8);
            int idA2 = interner.Intern("alpha"u8);
            WriteLine(idA == idA2);
            WriteLine(idA != idB);
            WriteLine(Bytes.Eq(interner.Text(idB), "beta"u8));
            return 0;
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M3_Fs_PathText_File_And_ReadWriteAllBytes_Differential_And_Errors()
    {
        string tempWork = Path.Combine(Path.GetTempPath(), $"vibranium-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempWork);
        try
        {
            string src = """
                if (Env.Args().IsErr(out var cli, out _)) return 2;
                ReadOnlySpan<byte> dir = cli.Get(0);

                var norm = new StringBuf(32);
                WriteLine(PathText.Normalize("a//b/./c/../d"u8, ref norm));
                Stdio.WriteOut(norm.AsUtf8());
                Stdio.WriteOut("\n"u8);

                WriteLine(PathText.Normalize("/../../etc/hosts"u8, ref norm));
                Stdio.WriteOut(norm.AsUtf8());
                Stdio.WriteOut("\n"u8);

                ReadOnlySpan<byte> nulPath = [(byte)'a', 0, (byte)'b'];
                WriteLine(PathText.Normalize(nulPath, ref norm));
                WriteLine(PathText.Normalize("trailing/"u8, ref norm));

                var filePath = new StringBuf(128);
                filePath.Append(dir);
                filePath.Append("/sample.bin"u8);

                ReadOnlySpan<byte> payload = "vibranium-fs-payload-0123456789"u8;
                if (Fs.WriteAllBytes(filePath.AsUtf8(), payload).IsErr(out _, out var wErr))
                {
                    Stdio.WriteOut(wErr.Error.KindName());
                    return 3;
                }

                // SyncAll check
                if (!File.OpenRead(filePath.AsUtf8()).IsErr(out var rFile, out _))
                {
                    using var f = rFile;
                    var small = new Vec<byte>(8);
                    small.Resize(4, 0);
                    int n = f.Read(small.AsSpan()).Unwrap();
                    WriteLine(n);
                    Stdio.WriteOut(small.AsReadOnlySpan());
                    Stdio.WriteOut("\n"u8);
                }

                // Exact maxBytes succeeds; maxBytes - 1 returns FileTooLarge
                var readOk = Fs.ReadAllBytes(filePath.AsUtf8(), payload.Length).Unwrap();
                WriteLine(Bytes.Eq(readOk.AsReadOnlySpan(), payload));

                if (Fs.ReadAllBytes(filePath.AsUtf8(), payload.Length - 1).IsErr(out _, out var tooLarge))
                {
                    Stdio.WriteOut(tooLarge.Error.KindName());
                    Stdio.WriteOut("\n"u8);
                }

                // Directory open returns IsADirectory; missing file returns NotFound; empty returns InvalidInput
                if (File.OpenRead(dir).IsErr(out var badDirF, out var dirErr))
                {
                    Stdio.WriteOut(dirErr.Error.KindName());
                    Stdio.WriteOut("\n"u8);
                }
                else { using var g = badDirF; }

                var missingPath = new StringBuf(128);
                missingPath.Append(dir);
                missingPath.Append("/does-not-exist"u8);
                if (File.OpenRead(missingPath.AsUtf8()).IsErr(out var badMissF, out var missErr))
                {
                    Stdio.WriteOut(missErr.Error.KindName());
                    Stdio.WriteOut("\n"u8);
                }
                else { using var g = badMissF; }

                if (File.OpenRead(""u8).IsErr(out var badEmptyF, out var emptyErr))
                {
                    Stdio.WriteOut(emptyErr.Error.KindName());
                    Stdio.WriteOut("\n"u8);
                }
                else { using var g = badEmptyF; }

                return 0;
                """;

            var res = CompileAndRunBoth(src, appArgs: [tempWork]);
            Assert.Equal(0, res.ManagedExit);
            Assert.Equal(0, res.NativeExit);
            Assert.Equal(res.ManagedOut, res.NativeOut);
            Assert.Equal(string.Empty, res.NativeErr);
        }
        finally
        {
            if (Directory.Exists(tempWork))
            {
                Directory.Delete(tempWork, recursive: true);
            }
        }
    }

    [Fact]
    public void M3_Env_Args_Differential_WithEmptyUnicodeAndMultiArgs()
    {
        string src = """
            if (Env.Args().IsErr(out var cli, out _)) return 1;
            WriteLine(cli.Count);
            for (int i = 0; i < cli.Count; i++)
            {
                ReadOnlySpan<byte> a = cli.Get(i);
                WriteLine(a.Length);
                Stdio.WriteOut(a);
                Stdio.WriteOut("\n"u8);
            }
            return 0;
            """;

        string longArg = new string('z', 300);
        var res = CompileAndRunBoth(src, appArgs: ["", "hello", "--flag=1", "λ-utf8", longArg]);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M3_Process_Command_Run_Differential_ExitCode_Signal_And_Errors()
    {
        string src = """
            var cTrue = new Command("/bin/true"u8);
            WriteLine(Process.Run(in cTrue).Unwrap());

            var cFalse = new Command("/bin/false"u8);
            WriteLine(Process.Run(in cFalse).Unwrap());

            var cSh = new Command("/bin/sh"u8);
            cSh.Arg("-c"u8);
            cSh.Arg("exit 42"u8);
            WriteLine(Process.Run(in cSh).Unwrap());

            var cRel = new Command("relative-bin"u8);
            if (Process.Run(in cRel).IsErr(out _, out var relErr))
            {
                Stdio.WriteOut(relErr.Error.KindName());
                Stdio.WriteOut("\n"u8);
            }

            var cMiss = new Command("/nonexistent_vib_binary_xyz"u8);
            if (Process.Run(in cMiss).IsErr(out _, out var missErr))
            {
                Stdio.WriteOut(missErr.Error.KindName());
                Stdio.WriteOut("\n"u8);
            }

            var cDir = new Command("/tmp"u8);
            if (Process.Run(in cDir).IsErr(out _, out var dirErr))
            {
                Stdio.WriteOut(dirErr.Error.KindName());
                Stdio.WriteOut("\n"u8);
            }

            return 0;
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M3_Net_TcpListener_And_TcpStream_Loopback_And_ErrorMapping_AsanClean()
    {
        string src = """
            // Invalid port check
            if (TcpListener.Bind(new Ipv4EndPoint(Ipv4Address.Loopback(), 70000), 16).IsErr(out var badL, out var badPortErr))
            {
                Stdio.WriteOut(badPortErr.Error.KindName());
                Stdio.WriteOut("\n"u8);
            }
            else { using var g = badL; }

            // Bind ephemeral loopback port, connect from client TcpStream in same process via kernellisten backlog!
            if (TcpListener.Bind(new Ipv4EndPoint(Ipv4Address.Loopback(), 0), 16).IsErr(out var rawL, out _)) return 1;
            using var listener = rawL;
            int port = listener.LocalPort().Unwrap();

            // Duplicate bind on same port must fail with AddrInUse
            if (TcpListener.Bind(new Ipv4EndPoint(Ipv4Address.Loopback(), port), 16).IsErr(out var dupL, out var dupErr))
            {
                Stdio.WriteOut(dupErr.Error.KindName());
                Stdio.WriteOut("\n"u8);
            }
            else { using var g = dupL; }

            // Non-blocking kernel SYN queue allows synchronous loopback Connect -> Accept -> Write/Read -> Close!
            var ep = new Ipv4EndPoint(Ipv4Address.Loopback(), port);
            if (TcpStream.Connect(ep).IsErr(out var rawClient, out _)) return 2;
            if (listener.Accept().IsErr(out var rawServer, out _)) { using var cDrop = rawClient; return 3; }
            {
                using var client = rawClient;
                using var server = rawServer;
                _ = client.WriteAll("ping-vibranium"u8).Unwrap();
                _ = client.ShutdownWrite().Unwrap();

                var buf = new Vec<byte>(32);
                buf.Resize(32, 0);
                int n = server.Read(buf.AsSpan()).Unwrap();
                buf.Truncate(n);
                Stdio.WriteOut(buf.AsReadOnlySpan());
                Stdio.WriteOut("\n"u8);
                int eof = server.Read(buf.AsSpan()).Unwrap();
                WriteLine(eof);
            }

            return 0;
            """;

        var res = CompileAndRunBoth(src);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("InvalidInput\nAddrInUse\nping-vibranium\n0\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void M3_HttpServerSample_CompilesCleanly_And_ServesAllStatuses_UnderAsan()
    {
        string serverProj = Path.Combine(RepoRoot, "samples", "http-server", "vibranium", "Program.cs");
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-http-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(serverProj, "VibraniumHttpE2E", includeStd: true);
            var outcome = VibraniumCompilerDriver.CheckAndEmitIr(
                compilation,
                new LlvmEmitOptions(SharedNoAlias: false, AddressSanitizer: true));
            Assert.True(outcome.Success, string.Join("\n", outcome.Errors));

            string binPath = Path.Combine(tempDir, "http-server-asan");
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(outcome.LlvmIr!, binPath, optLevel: 2, addressSanitizer: true);
            Assert.True(clangExit == 0, clangOut);

            var psi = new ProcessStartInfo(binPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["ASAN_OPTIONS"] = "detect_leaks=1:abort_on_error=1";
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("8");

            using var server = System.Diagnostics.Process.Start(psi)!;
            try
            {
            string? banner = server.StandardOutput.ReadLine();
            Assert.NotNull(banner);
            Assert.StartsWith("listening on 127.0.0.1:", banner);
            int port = int.Parse(banner.Substring("listening on 127.0.0.1:".Length));

            static string SendRaw(int port, byte[][] chunks, int delayMs = 0)
            {
                using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.NoDelay = true;
                sock.Connect("127.0.0.1", port);
                for (int i = 0; i < chunks.Length; i++)
                {
                    if (i > 0 && delayMs > 0) Thread.Sleep(delayMs);
                    try { sock.Send(chunks[i]); } catch (SocketException) { break; }
                }
                using var ms = new MemoryStream();
                byte[] buf = new byte[4096];
                while (true)
                {
                    int n;
                    try { n = sock.Receive(buf); } catch (SocketException) { break; }
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }

            // 1. 200 GET /
            string r1 = SendRaw(port, ["GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray()]);
            Assert.StartsWith("HTTP/1.1 200 OK\r\n", r1);
            Assert.EndsWith("\r\n\r\nhello from vibranium\n", r1);

            // 2. Fragmented GET /count
            string r2 = SendRaw(
                port,
                ["GET /cou"u8.ToArray(), "nt HTTP/1.1\r\nHo"u8.ToArray(), "st: local\r\n\r"u8.ToArray(), "\n"u8.ToArray()],
                delayMs: 2);
            Assert.StartsWith("HTTP/1.1 200 OK\r\n", r2);
            Assert.EndsWith("\r\n\r\nconnection 2\n", r2);

            // 3. HEAD /
            string r3 = SendRaw(port, ["HEAD / HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray()]);
            Assert.Contains("Content-Length: 21\r\n", r3);
            Assert.EndsWith("\r\n\r\n", r3);

            // 4. 404 Not Found
            string r4 = SendRaw(port, ["GET /nope HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray()]);
            Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", r4);

            // 5. 405 Method Not Allowed
            string r5 = SendRaw(port, ["POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray()]);
            Assert.StartsWith("HTTP/1.1 405 Method Not Allowed\r\n", r5);

            // 6. 400 Bad Request (duplicate Host)
            string r6 = SendRaw(port, ["GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n"u8.ToArray()]);
            Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", r6);

            // 7. 431 Request Header Fields Too Large
            byte[] huge = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\nX-Big: " + new string('x', 16500));
            string r7 = SendRaw(port, [huge]);
            Assert.StartsWith("HTTP/1.1 431 Request Header Fields Too Large\r\n", r7);

            // 8. 505 HTTP Version Not Supported
            string r8 = SendRaw(port, ["GET / HTTP/2.0\r\nHost: localhost\r\n\r\n"u8.ToArray()]);
            Assert.StartsWith("HTTP/1.1 505 HTTP Version Not Supported\r\n", r8);

                string stderr = server.StandardError.ReadToEnd();
                Assert.True(server.WaitForExit(10_000));
                Assert.Equal(0, server.ExitCode);
                Assert.Equal(string.Empty, stderr);
            }
            finally
            {
                if (!server.HasExited)
                {
                    server.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void FileReadOnWriteOnly_And_FileWriteOnReadOnly_ReturnIoErrorOther_MatchingNative()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-fmode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string targetPath = Path.Combine(tempDir, "probe.txt");
        try
        {
            string source = """
                using Vibranium.Std;

                var argList = Env.Args().Unwrap();
                ReadOnlySpan<byte> path = argList.Get(0);

                if (File.Create(path) is Ok<File>(var wf))
                {
                    using var writeOnly = wf;
                    var wRes = writeOnly.WriteAll("hello"u8);
                    if (wRes is Ok<Unit>)
                    {
                        WriteLine(10);
                    }
                    Vec<byte> scratch = [0, 0, 0, 0];
                    var badRead = writeOnly.Read(scratch.AsSpan());
                    if (badRead is Err<IoError>(var e1))
                    {
                        WriteLine(e1.Kind == IoErrorKind.Other ? 20 : 99);
                    }
                }

                if (File.OpenRead(path) is Ok<File>(var rf))
                {
                    using var readOnly = rf;
                    var badWrite = readOnly.WriteAll("nope"u8);
                    if (badWrite is Err<IoError>(var e2))
                    {
                        WriteLine(e2.Kind == IoErrorKind.Other ? 30 : 98);
                    }
                }
                """;

            var res = CompileAndRunBoth(source, appArgs: [targetPath], withAsan: true);
            Assert.Equal(0, res.ManagedExit);
            Assert.Equal(0, res.NativeExit);
            Assert.Equal("10\n20\n30\n", res.ManagedOut.Replace("\r\n", "\n"));
            Assert.Equal(res.ManagedOut, res.NativeOut);
            Assert.Equal(res.ManagedErr, res.NativeErr);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void OptionStringBuf_PlaceScrutineeMutation_IsRejectedWithVib004()
    {
        string source = """
            using Vibranium.Std;

            var sb = new StringBuf(8);
            Option<StringBuf> opt = Some(sb);
            if (opt is Some<StringBuf>(var s))
            {
                s.Append("!"u8);
            }
            """;

        var diags = CheckVibraniumDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "VIB004");
    }

    // =========================================================================
    // Tier-A Vibranium.Std Ergonomics & Sys v2 Differential Tests
    // =========================================================================

    [Fact]
    public void StdTierA_ByteSpanExtensions_MemoryExtensionsAlias_And_IntLongTryParse_MatchNativeAndAsan()
    {
        string source = """
            using System;
            using Vibranium.Std;

            ReadOnlySpan<byte> ro = "  hello/world/hello  \r\n".AsUtf8();
            ReadOnlySpan<byte> trimmed = ro.Trim();
            WriteLine(trimmed);                                    // hello/world/hello
            WriteLine(ro.TrimStart().StartsWith("hello"));         // True
            WriteLine(ro.TrimEnd().EndsWith("hello"));             // True
            WriteLine(trimmed.SequenceEqual("hello/world/hello")); // True
            WriteLine(trimmed.SequenceEqual("hello"u8));           // False
            WriteLine(trimmed.IndexOf((byte)'/'));                 // 5
            WriteLine(trimmed.LastIndexOf((byte)'/'));             // 11
            WriteLine(trimmed.IndexOf("world"));                   // 6
            WriteLine(trimmed.Contains((byte)'w'));                // True
            WriteLine(trimmed.Contains("world"));                  // True
            WriteLine(trimmed.Contains("missing"));                // False

            // Mutable Span<byte> receivers (including self-comparison without VIB002 + System.MemoryExtensions<byte> binding)
            Span<byte> mutSpan = [(byte)'a', (byte)'b', (byte)'c', (byte)'b', (byte)'a'];
            WriteLine(mutSpan.SequenceEqual(mutSpan));       // True
            WriteLine(mutSpan.SequenceEqual("abcba"));       // True
            WriteLine(mutSpan.StartsWith("ab"u8));           // True
            WriteLine(mutSpan.EndsWith("ba"));               // True
            WriteLine(mutSpan.IndexOf((byte)'b'));           // 1
            WriteLine(mutSpan.LastIndexOf((byte)'b'));       // 3
            WriteLine(mutSpan.Contains("bcb"u8));            // True
            mutSpan[2] = (byte)'Z';
            WriteLine(mutSpan.Contains((byte)'Z'));          // True

            // Exact BCL invariant int.TryParse / long.TryParse grammar (ws* [+-]? [0-9]+ ws* '\0'*)
            CheckInt("0");
            CheckInt("  -2147483648\t\r\n");
            CheckInt(" +2147483647 ");
            CheckInt("12 \0\0");
            CheckInt("12\0 ");
            CheckInt("");
            CheckInt("   ");
            CheckInt("+ 1");
            CheckInt("2147483648");
            CheckInt("-2147483649");

            CheckLong("9223372036854775807");
            CheckLong(" -9223372036854775808\0");
            CheckLong("9223372036854775808");
            CheckLong("-9223372036854775809");
            CheckLong("42\0\t");

            static void CheckInt(string s)
            {
                bool ok1 = int.TryParse(s, out int v1);
                bool ok2 = int.TryParse(s.AsUtf8(), out int v2);
                Write(ok1);
                Write(":");
                Write(v1);
                Write(":");
                Write(ok2);
                Write(":");
                WriteLine(v2);
            }

            static void CheckLong(string s)
            {
                bool ok1 = long.TryParse(s, out long v1);
                bool ok2 = long.TryParse(s.AsUtf8(), out long v2);
                Write(ok1);
                Write(":");
                Write(v1);
                Write(":");
                Write(ok2);
                Write(":");
                WriteLine(v2);
            }
            """;

        var res = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void StdTierA_StringBuf_IndexRange_Lines_And_ArgList_Foreach_MatchNativeAndAsan()
    {
        string source = """
            using Vibranium.Std;

            var sb = new StringBuf();
            sb.Append("item=");
            sb.Append(42);
            sb.Append(", big=");
            sb.Append(9000000000L);
            sb.Append(", ok=");
            sb.Append(true);
            sb.AppendLine();
            sb.AppendLine("second-line");
            sb.AppendLine("third-line".AsUtf8());
            WriteLine(sb.TryAppend("🦀"));

            WriteLine(sb[0]);       // 'i' = 105
            WriteLine(sb[^1]);      // 0x9E (last UTF-8 byte of 🦀)
            WriteLine(sb[0..7]);    // item=42
            Write(in sb);
            WriteLine(in sb);

            foreach (var ln in sb.Lines())
            {
                Write("L:");
                WriteLine(ln);
            }

            var cliArgs = Env.Args().Expect("args");
            WriteLine(cliArgs.Count);
            if (cliArgs.Count >= 2)
            {
                WriteLine(cliArgs[0]);
                WriteLine(cliArgs[^1]);
            }
            foreach (var a in cliArgs)
            {
                Write("A:<");
                Write(a);
                WriteLine(">");
            }
            """;

        var res = CompileAndRunBoth(source, appArgs: ["first", "", "--crab=🦀"], withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));

        // Verify borrow-checker rules on StringBuf & ArgList
        Assert.Contains(
            CheckVibraniumDiagnostics(
                """
                using Vibranium.Std;
                var sb = new StringBuf();
                sb.Append("a\nb");
                foreach (var line in sb.Lines())
                {
                    sb.Append("!");
                    WriteLine(line.Length);
                }
                """),
            d => d.Id == "VIB003");

        Assert.Contains(
            CheckVibraniumDiagnostics(
                """
                using Vibranium.Std;
                WriteLine(BadSlice().Length);
                static ReadOnlySpan<byte> BadSlice()
                {
                    var box = new BufBox(new StringBuf());
                    return box.Buf[..];
                }
                class BufBox(StringBuf buf) { public StringBuf Buf = buf; }
                """),
            d => d.Id == "VIB007");

        Assert.Contains(
            CheckVibraniumDiagnostics(
                """
                using Vibranium.Std;
                WriteLine(BadArg().Length);
                static ReadOnlySpan<byte> BadArg()
                {
                    var box = new ArgBox(Env.Args().Unwrap());
                    return box.Args[0];
                }
                class ArgBox(ArgList a) { public ArgList Args = a; }
                """),
            d => d.Id == "VIB007");
    }

    [Fact]
    public void StdTierA_FileExists_AllPosixEdgeCases_And_OneShotFileApis_MatchNativeAndAsan()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-fexists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string regFile = Path.Combine(tempDir, "regular.txt");
            System.IO.File.WriteAllText(regFile, "seed");
            string subDir = Path.Combine(tempDir, "subdir");
            Directory.CreateDirectory(subDir);
            string symToFile = Path.Combine(tempDir, "sym-file");
            System.IO.File.CreateSymbolicLink(symToFile, regFile);
            string symToDir = Path.Combine(tempDir, "sym-dir");
            System.IO.File.CreateSymbolicLink(symToDir, subDir);
            string danglingSym = Path.Combine(tempDir, "sym-dangling");
            System.IO.File.CreateSymbolicLink(danglingSym, Path.Combine(tempDir, "does-not-exist"));
            string loopSym = Path.Combine(tempDir, "sym-loop");
            System.IO.File.CreateSymbolicLink(loopSym, loopSym);
            string fifoPath = Path.Combine(tempDir, "named.fifo");
            var (mkfifoExit, _, mkfifoErr) = RunProcess("mkfifo", setAsanOptions: false, fifoPath);
            Assert.True(mkfifoExit == 0, mkfifoErr);

            string textTarget = Path.Combine(tempDir, "text-ops.txt");
            string binTarget = Path.Combine(tempDir, "bin-ops.dat");
            string untouchedInvalid = Path.Combine(tempDir, "untouched-invalid.txt");

            string source = """
                using Vibranium.Std;

                var cliArgs = Env.Args().Unwrap();
                // 1. File.Exists across POSIX edge cases
                WriteLine(File.Exists("/"));           // False
                WriteLine(File.Exists(""));            // False
                WriteLine(File.Exists("bad\0path"));   // False
                for (int i = 0; i < 7; i++)
                {
                    WriteLine(File.Exists(cliArgs[i]));
                }
                WriteLine(File.Exists(cliArgs[10]));   // FIFO -> True

                // 2. WriteAllText / AppendAllText / ReadAllText (including invalid UTF-8 pre-validation)
                ReadOnlySpan<byte> txtPath = cliArgs[7];
                var sb = new StringBuf();
                sb.Append("buf-chunk\n");

                File.WriteAllText(txtPath, "hello\n").Expect("w1");
                File.AppendAllText(txtPath, "world-\n".AsUtf8()).Expect("a1");
                File.AppendAllText(txtPath, in sb).Expect("a2");

                var loaded = File.ReadAllText(txtPath).Expect("read text");
                Write(in loaded);

                // Invalid UTF-8 must fail BEFORE creating/truncating or modifying existing file!
                Span<byte> badUtf8 = [0xFF, 0xFE];
                ReadOnlySpan<byte> untouched = cliArgs[9];
                if (File.WriteAllText(untouched, badUtf8) is Err<IoError>(var badErr1))
                {
                    WriteLine(badErr1.Kind == IoErrorKind.InvalidInput && badErr1.Op == IoOp.Write);
                    WriteLine(File.Exists(untouched)); // False!
                }
                if (File.AppendAllText(txtPath, badUtf8) is Err<IoError>(var badErr2))
                {
                    WriteLine(badErr2.Kind == IoErrorKind.InvalidInput && badErr2.Op == IoOp.Write);
                }
                var stillIntact = File.ReadAllText(txtPath).Expect("intact");
                WriteLine(stillIntact.Length);

                // ReadAllText maxBytes FileTooLarge
                if (File.ReadAllText(txtPath, maxBytes: 4) is Err<IoError>(var tooBig))
                {
                    WriteLine(tooBig.Kind == IoErrorKind.FileTooLarge);
                }

                // 3. WriteAllBytes / AppendAllBytes / ReadAllBytes & OpenAppend
                ReadOnlySpan<byte> binPath = cliArgs[8];
                File.WriteAllBytes(binPath, [1, 2, 3]).Expect("wb");
                File.AppendAllBytes(binPath, [4, 5]).Expect("ab");
                {
                    using var af = File.OpenAppend(binPath).Expect("oa");
                    af.WriteAll([6]).Expect("w6");
                }
                var raw = File.ReadAllBytes(binPath).Expect("rb");
                WriteLine(raw.Count); // 6
                WriteLine(raw[0] + raw[^1]); // 7

                // Invalid UTF-8 file rejected by ReadAllText
                File.WriteAllBytes(binPath, [0xC3, 0x28]).Expect("bad-utf8-bytes");
                if (File.ReadAllText(binPath) is Err<IoError>(var invalidRead))
                {
                    WriteLine(invalidRead.Kind == IoErrorKind.InvalidInput && invalidRead.Op == IoOp.Read);
                }
                """;

            string[] cliArgs =
            [
                regFile,
                regFile + "/",
                subDir,
                symToFile,
                symToDir,
                danglingSym,
                loopSym,
                textTarget,
                binTarget,
                untouchedInvalid,
                fifoPath,
            ];

            var res = CompileAndRunBoth(source, appArgs: cliArgs, withAsan: true);
            Assert.Equal(0, res.ManagedExit);
            Assert.Equal(0, res.NativeExit);
            Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void StdTierA_ConsoleIo_ReadLine_And_TryReadLine_StreamDiscipline_MatchNativeAndAsan()
    {
        string source = """
            using Vibranium.Std;

            // 1. LF, CRLF, standalone CR preserved, empty line, maxBytes overflow, maxBytes+\r\n ok, invalid UTF-8, tail without newline, EOF
            DumpRead(ReadLine());             // "alpha"
            DumpRead(ReadLine());             // "beta" (from "beta\r\n")
            DumpRead(ReadLine());             // "cr\rinside" (standalone \r preserved!)
            DumpRead(ReadLine());             // "" (empty line)
            DumpRead(ReadLine(maxBytes: 4));  // "abcd\r\n" -> exact maxBytes + \r\n succeeds!
            DumpRead(ReadLine(maxBytes: 4));  // "abcde..." -> immediate FileTooLarge after 5th byte ('e')!
            DumpRead(ReadLine());             // "fg" (remaining bytes on fd 0 still intact!)
            DumpRead(ReadLine());             // invalid UTF-8 0xFF\n -> InvalidInput
            var scratch = new StringBuf();
            if (TryReadLine(ref scratch).Expect("tail read"))
            {
                Write("TRY:<");
                Write(in scratch);
                WriteLine(">");
            }
            DumpRead(ReadLine());                                // clean EOF -> Ok(None)
            WriteLine(TryReadLine(ref scratch).Expect("eof2"));  // False
            WriteLine(scratch.Length);                           // 0 (cleared on Ok(false))

            static void DumpRead(Result<Option<StringBuf>, IoError> r)
            {
                if (r.IsErr(out var opt, out var err))
                {
                    Write("ERR:");
                    Write(err.Error.Op == IoOp.Read);
                    Write(":");
                    WriteLine(err.Error.KindName());
                    return;
                }
                if (opt.IsNone(out var line))
                {
                    WriteLine("EOF");
                    return;
                }
                Write("OK:<");
                Write(line.Length);
                Write(":");
                Write(in line);
                WriteLine(">");
            }
            """;

        var input = new List<byte>();
        input.AddRange(Encoding.UTF8.GetBytes("alpha\nbeta\r\ncr\rinside\n\nabcd\r\nabcdefg\n"));
        input.Add(0xFF);
        input.Add((byte)'\n');
        input.AddRange(Encoding.UTF8.GetBytes("tail-no-nl"));

        var res = CompileAndRunBoth(source, withAsan: true, stdinBytes: input.ToArray());
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        Assert.Contains("OK:<5:alpha>\n", res.NativeOut);
        Assert.Contains("OK:<4:beta>\n", res.NativeOut);
        Assert.Contains("OK:<9:cr\rinside>\n", res.NativeOut);
        Assert.Contains("OK:<0:>\n", res.NativeOut);
        Assert.Contains("OK:<4:abcd>\n", res.NativeOut);
        Assert.Contains("ERR:True:FileTooLarge\n", res.NativeOut);
        Assert.Contains("OK:<2:fg>\n", res.NativeOut);
        Assert.Contains("ERR:True:InvalidInput\n", res.NativeOut);
        Assert.Contains("TRY:<tail-no-nl>\n", res.NativeOut);
        Assert.Contains("EOF\nFalse\n0\n", res.NativeOut);
    }

    [Fact]
    public void StdTierA_PlainStringOverloads_StrMap_PathText_Command_And_Net_MatchNativeAndAsan()
    {
        string source = """
            using Vibranium.Std;

            // 1. StrMap<V> & Interner string key overloads
            var map = new StrMap<int>(8);
            map.Set("alpha", 10);
            map.Set("beta", 20);
            WriteLine(map.Contains("alpha"));             // True
            WriteLine(map.Find("alpha") >= 0);            // True
            WriteLine(map.Get("beta").UnwrapOr(-1));      // 20
            WriteLine(map.Remove("alpha").UnwrapOr(-1));  // 10
            WriteLine(map.Contains("alpha"));             // False

            var interner = new Interner(8);
            int id1 = interner.Intern("symbol");
            int id2 = interner.Intern("symbol");
            WriteLine(id1 == id2); // True

            // 2. PathText.Normalize(string, ref StringBuf)
            var norm = new StringBuf();
            WriteLine(PathText.Normalize("/var/tmp//file.vib", ref norm)); // True
            WriteLine(norm[..].EndsWith(".vib"));                          // True

            // 3. Command & Process string overloads
            var cmd = new Command("/bin/sh");
            cmd.Arg("-c");
            cmd.Arg("exit 7");
            WriteLine(Process.Run(in cmd).Expect("sh exit")); // 7

            // 4. Net string overloads (Ipv4Address.TryParse, TcpListener.Bind, TcpStream.Connect)
            var lo = Ipv4Address.TryParse("127.0.0.1").Expect("parse lo");
            WriteLine(lo.A == 127 && lo.D == 1);                                    // True
            WriteLine(Ipv4Address.TryParse("127.0.01.1").IsEmpty);         // True (leading zero rejected!)
            WriteLine(Ipv4Address.TryParse("0.0.0.0").IsSome);             // True (single 0 valid!)

            using var listener = TcpListener.Bind("127.0.0.1", 0).Expect("bind");
            int port = listener.LocalPort().Expect("port");
            {
                using var client = TcpStream.Connect("127.0.0.1", port).Expect("connect");
                client.WriteAll("ping!".AsUtf8()).Expect("write ping");
                _ = client.ShutdownWrite();

                using var accepted = listener.Accept().Expect("accept");
                Span<byte> recvBuf = [0, 0, 0, 0, 0, 0, 0, 0];
                int n = accepted.Read(recvBuf).Expect("read");
                WriteLine(recvBuf.Slice(0, n)); // ping!
            }
            """;

        var res = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Round2_OpenAppendConcurrentHandles_StringBufPlusEq_And_CompletePlainStringOverloads_MatchNativeAndAsan()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"vib-r2-append-{Guid.NewGuid():N}.txt");
        try
        {
            string escaped = tempFile.Replace("\\", "\\\\");
            string source = $$"""
                using Vibranium.Std;

                // 1. P1-5: Two concurrent File.OpenAppend handles + File.WriteAll(string)
                string path = "{{escaped}}";
                File.WriteAllBytes(path, ""u8).Expect("truncate");
                {
                    using var f1 = File.OpenAppend(path).Expect("open1");
                    using var f2 = File.OpenAppend(path).Expect("open2");
                    f1.WriteAll("A"u8).Expect("w1");
                    f2.WriteAll("B").Expect("w2");
                    f1.WriteAll("C").Expect("w3");
                }
                StringBuf readBack = File.ReadAllText(path).Expect("read");
                WriteLine(in readBack); // ABC

                // 2. StringBuf += (string & ReadOnlySpan<byte>)
                var sb = new StringBuf();
                sb += "hi ";
                sb += "vibranium"u8;
                WriteLine(in sb); // hi vibranium

                // 3. Complete plain-string overloads on Bytes & Utf8 & TcpStream
                WriteLine(Bytes.Eq("abc"u8, "abc"));
                WriteLine(Bytes.Eq("abc", "abc"u8));
                WriteLine(Bytes.Eq("abc", "abd"));
                WriteLine(Bytes.EqIgnoreAsciiCase("AbC"u8, "aBc"));
                WriteLine(Bytes.EqIgnoreAsciiCase("AbC", "aBc"u8));
                WriteLine(Bytes.EqIgnoreAsciiCase("AbC", "aBc"));
                WriteLine(Bytes.TrimAscii("  \thi\t "));
                WriteLine(Bytes.StartsWith("foobar"u8, "foo"));
                WriteLine(Bytes.StartsWith("foobar", "foo"u8));
                WriteLine(Bytes.StartsWith("foobar", "foo"));
                WriteLine(Bytes.EndsWith("foobar"u8, "bar"));
                WriteLine(Bytes.EndsWith("foobar", "bar"u8));
                WriteLine(Bytes.EndsWith("foobar", "bar"));
                WriteLine(Bytes.IndexOfByte("a/b/c", (byte)'/'));
                WriteLine(Bytes.LastIndexOfByte("a/b/c", (byte)'/'));
                WriteLine(Bytes.IndexOf("hello world"u8, "world"));
                WriteLine(Bytes.IndexOf("hello world", "world"u8));
                WriteLine(Bytes.IndexOf("hello world", "world"));
                WriteLine(Bytes.ParseInt("-42").Expect("p32"));
                WriteLine(Bytes.ParseLong("9000000000").Expect("p64"));
                int i32;
                long i64;
                WriteLine(Bytes.TryParseInt32(" 123 ", out i32) ? i32 : -1);
                WriteLine(Bytes.TryParseInt64(" -456 ", out i64) ? i64 : -1L);
                WriteLine(Bytes.Hash("key") == Bytes.Hash("key"u8));

                WriteLine(Utf8.IsValid("café"));
                WriteLine(Utf8.IsBoundary("é", 0));
                WriteLine(Utf8.IsBoundary("é", 1));
                WriteLine(Utf8.IsBoundary("é", 2));

                using var listener = TcpListener.Bind("127.0.0.1", 0).Expect("bind");
                int port = listener.LocalPort().Expect("port");
                {
                    using var client = TcpStream.Connect("127.0.0.1", port).Expect("connect");
                    client.WriteAll("w1:").Expect("writeall str");
                    client.SendAll("s2").Expect("sendall str");
                    client.SendAll("!"u8).Expect("sendall u8");
                    _ = client.ShutdownWrite();

                    using var server = listener.Accept().Expect("accept");
                    Span<byte> buf = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                    int total = 0;
                    while (true)
                    {
                        int n = server.Read(buf.Slice(total, buf.Length - total)).Expect("read");
                        if (n == 0) break;
                        total += n;
                    }
                    WriteLine(buf.Slice(0, total)); // w1:s2!
                }
                """;

            var res = CompileAndRunBoth(source, withAsan: true);
            Assert.Equal(0, res.ManagedExit);
            Assert.Equal(0, res.NativeExit);
            Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
            Assert.StartsWith("ABC\nhi vibranium\n", res.NativeOut.Replace("\r\n", "\n"));
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Round2_LinuxInvalidUtf8Argv_ManagedAndNativeBothReturnInvalidInput()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-r2-badargv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcFile = Path.Combine(tempDir, "Program.cs");
            System.IO.File.WriteAllText(
                srcFile,
                """
                using Vibranium.Std;
                if (Env.Args().IsErr(out var cliArgs, out var err))
                {
                    Write(err.Error.Op == IoOp.Args);
                    Write(":");
                    WriteLine(err.Error.KindName());
                }
                else
                {
                    WriteLine(cliArgs.Count);
                }
                """);

            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(srcFile, "VibraniumBadArgvApp", includeStd: true);
            var outcome = VibraniumCompilerDriver.CheckAndEmitIr(
                compilation,
                new LlvmEmitOptions(SharedNoAlias: false, AddressSanitizer: true));
            Assert.True(outcome.Success, string.Join("\n", outcome.Errors));

            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = compilation.SyntaxTrees.ToList();
            if (System.IO.File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    System.IO.File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedComp = CSharpCompilation.Create("VibraniumBadArgvManaged", managedTrees, compilation.References, compilation.Options);
            string dllPath = Path.Combine(tempDir, "VibraniumBadArgvManaged.dll");
            Assert.True(managedComp.Emit(dllPath).Success);
            System.IO.File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(tempDir, "Vibranium.Core.dll"), overwrite: true);
            System.IO.File.WriteAllText(
                Path.Combine(tempDir, "VibraniumBadArgvManaged.runtimeconfig.json"),
                """
                {
                  "runtimeOptions": {
                    "tfm": "net11.0",
                    "framework": { "name": "Microsoft.NETCore.App", "version": "11.0.0" }
                  }
                }
                """);

            string llFile = Path.Combine(tempDir, "out.ll");
            string binFile = Path.Combine(tempDir, "out_bin");
            System.IO.File.WriteAllText(llFile, outcome.LlvmIr!);
            var optRes = RunProcess("opt-18", setAsanOptions: false, "-passes=verify", "-disable-output", llFile);
            Assert.Equal(0, optRes.ExitCode);
            var clangRes = RunProcess("clang-18", setAsanOptions: false, "-O3", "-fsanitize=address,leak", "-Wno-override-module", llFile, "-o", binFile);
            Assert.Equal(0, clangRes.ExitCode);

            var managedRun = RunProcess("/bin/bash", setAsanOptions: false, "-c", "exec \"$@\" $'\\xff'", "--", DotnetExe, dllPath, "valid-first");
            var nativeRun = RunProcess("/bin/bash", setAsanOptions: true, "-c", "exec \"$@\" $'\\xff'", "--", binFile, "valid-first");

            Assert.Equal(0, managedRun.ExitCode);
            Assert.Equal(0, nativeRun.ExitCode);
            Assert.Equal("True:InvalidInput\n", managedRun.Stdout.Replace("\r\n", "\n"));
            Assert.Equal(managedRun.Stdout.Replace("\r\n", "\n"), nativeRun.Stdout.Replace("\r\n", "\n"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round3_Std_GlobalNamespaceBclShadowing_And_RefPropertyStringBufFlowCapture_DifferentialParity()
    {
        const string code = """
            bool c = Pick(true);
            var w = new BufWrap(new StringBuf());
            w.Ref.Append("ab");
            w.Ref.Append(c ? "c" : "d");
            WriteLine(w.Ref.AsUtf8());

            var sock = new Socket(11);
            var stk = new Stack<int>(22);
            var dir = new Directory(33);
            var math = new Math(44);
            var enc = new Encoding(55);
            var con = new Console(66);
            var env = new Environment(77);
            WriteLine(sock.V + stk.V + dir.V + math.V + enc.V + con.V + env.V);

            WriteLine(Vibranium.Std.File.Exists("/definitely-missing-vib-r3-shadow-check"));

            static bool Pick(bool b) => b;

            sealed class BufWrap(StringBuf sb)
            {
                private StringBuf _sb = sb;
                public ref StringBuf Ref { [Mut] get => ref _sb; }
            }

            struct Socket(int v) { public int V = v; }
            struct Stack<T>(T v) where T : unmanaged { public T V = v; }
            struct Directory(int v) { public int V = v; }
            struct Math(int v) { public int V = v; }
            struct Encoding(int v) { public int V = v; }
            struct Console(int v) { public int V = v; }
            struct Environment(int v) { public int V = v; }
            """;

        string checkDir = Path.Combine(Path.GetTempPath(), $"vibranium-r3-std-shadow-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(checkDir);
        try
        {
            string srcPath = Path.Combine(checkDir, "Program.cs");
            System.IO.File.WriteAllText(srcPath, code);
            var checkComp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round3ShadowCheck", includeStd: true);
            Assert.Empty(checkComp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Empty(VibraniumFrontend.CompileAndCheck(checkComp).Diagnostics);
            var analyzerDiags = (await checkComp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(analyzerDiags);
        }
        finally
        {
            if (System.IO.Directory.Exists(checkDir))
            {
                System.IO.Directory.Delete(checkDir, recursive: true);
            }
        }

        var res = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("abc\n308\nFalse\n", res.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Round4_Std_UserTopLevelPreludeAndTypeShadowing_DifferentialParity()
    {
        const string fnShadowCode = """
            WriteLine(Ok(200));
            WriteLine(Err(500));
            WriteLine(Some(7));
            WriteLine(Move(3));

            var imap = new IntMap<int>(4);
            imap.Set(1, 42);
            WriteLine(imap.Get(1).Unwrap());

            var smap = new StrMap<int>(4);
            smap.Set("k", 99);
            WriteLine(smap.Get("k").Unwrap());

            WriteLine(Bytes.ParseInt("123").Unwrap());
            WriteLine(Ipv4Address.TryParse("127.0.0.1").IsSome);
            WriteLine(Vibranium.Std.File.Exists("/definitely-missing-vib-r4-shadow"));

            static bool Ok(int code) => code == 200;
            static bool Err(int code) => code >= 500;
            static int Some(int x) => x * 2;
            static int Move(int x) => x + 1;
            """;

        const string varShadowCode = """
            bool Ok = true;
            int Err = 10;
            int Some = 20;
            WriteLine(Ok);
            WriteLine(Err + Some);
            WriteLine(Bytes.ParseInt("45").Unwrap());
            """;

        const string structNoneShadowCode = """
            var n = new None(7);
            WriteLine(n.X);
            WriteLine(Bytes.ParseInt("bad").IsEmpty);
            readonly struct None(int x) { public readonly int X = x; }
            """;

        foreach (var (name, code, expectedOut) in new[]
        {
            ("FnShadow", fnShadowCode, "True\nTrue\n14\n4\n42\n99\n123\nTrue\nFalse\n"),
            ("VarShadow", varShadowCode, "True\n30\n45\n"),
            ("StructNoneShadow", structNoneShadowCode, "7\nTrue\n"),
        })
        {
            string checkDir = Path.Combine(Path.GetTempPath(), $"vibranium-r4-std-{name}-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(checkDir);
            try
            {
                string srcPath = Path.Combine(checkDir, "Program.cs");
                System.IO.File.WriteAllText(srcPath, code);
                var checkComp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Round4{name}", includeStd: true);
                Assert.Empty(checkComp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
                Assert.Empty(VibraniumFrontend.CompileAndCheck(checkComp).Diagnostics);
                var analyzerDiags = (await checkComp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .ToList();
                Assert.Empty(analyzerDiags);
            }
            finally
            {
                if (System.IO.Directory.Exists(checkDir))
                {
                    System.IO.Directory.Delete(checkDir, recursive: true);
                }
            }

            var res = CompileAndRunBoth(code, withAsan: true);
            Assert.Equal(0, res.ManagedExit);
            Assert.Equal(0, res.NativeExit);
            Assert.Equal(expectedOut, res.ManagedOut.Replace("\r\n", "\n"));
            Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        }
    }
}
