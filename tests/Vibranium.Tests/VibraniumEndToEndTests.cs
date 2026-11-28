using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
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

public sealed partial class VibraniumEndToEndTests
{
    private static readonly string DotnetExe = ResolveDotnetExe();

    private static string ResolveDotnetExe()
    {
        try
        {
            var (exit, stdout, _) = RunProcess("dotnet", "--version");
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
        return File.Exists(userDotnet) ? userDotnet : "dotnet";
    }

    private static string RepoRoot
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "Vibranium.slnx")))
            {
                dir = Directory.GetParent(dir)!.FullName;
            }
            return dir;
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    private static (int ManagedExit, string ManagedOut, string ManagedErr, int NativeExit, string NativeOut, string NativeErr, string LlvmIr)
        CompileAndRunBoth(string sourceCode, bool withAsan = true, bool sharedNoAlias = false)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcFile = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(srcFile, sourceCode);

            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(srcFile, "VibraniumDiffApp");
            var outcome = VibraniumCompilerDriver.CheckAndEmitIr(
                compilation,
                new LlvmEmitOptions(SharedNoAlias: sharedNoAlias, AddressSanitizer: withAsan));
            Assert.True(outcome.Success, string.Join("\n", outcome.Errors));
            Assert.NotNull(outcome.LlvmIr);

            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = compilation.SyntaxTrees.ToList();
            if (File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedCompilation = CSharpCompilation.Create(
                "VibraniumDiffManaged",
                managedTrees,
                compilation.References,
                compilation.Options);

            string dllPath = Path.Combine(tempDir, "VibraniumDiffManaged.dll");
            var emitRes = managedCompilation.Emit(dllPath);
            Assert.True(emitRes.Success, string.Join("\n", emitRes.Diagnostics));

            string coreDll = typeof(Vibranium.Option<>).Assembly.Location;
            File.Copy(coreDll, Path.Combine(tempDir, "Vibranium.Core.dll"), overwrite: true);
            string runtimeConfig = """
                {
                  "runtimeOptions": {
                    "tfm": "net11.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "11.0.0"
                    },
                    "configProperties": {
                      "System.Globalization.Invariant": true
                    }
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "VibraniumDiffManaged.runtimeconfig.json"), runtimeConfig);

            var (mExit, mOut, mErr) = RunProcess(DotnetExe, dllPath);

            string binPath = Path.Combine(tempDir, "native_bin");
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(outcome.LlvmIr!, binPath, optLevel: 3, addressSanitizer: withAsan);
            Assert.True(clangExit == 0, clangOut);

            var (nExit, nOut, nErr) = RunProcess(binPath);
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

    [Fact]
    public void ShowcaseDemo_MatchesByteForByte_ZeroGc_And_AsanClean()
    {
        string demoSource = File.ReadAllText(Path.Combine(RepoRoot, "samples", "demo", "Program.cs"));
        var res = CompileAndRunBoth(demoSource, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);

        var resSharedNoAlias = CompileAndRunBoth(demoSource, withAsan: false, sharedNoAlias: true);
        Assert.Equal(0, resSharedNoAlias.NativeExit);
        Assert.Equal(res.ManagedOut, resSharedNoAlias.NativeOut);
    }

    [Fact]
    public void NativeBinary_LinksOnlySystemLibc_NoClrOrGc()
    {
        string demoSource = File.ReadAllText(Path.Combine(RepoRoot, "samples", "demo", "Program.cs"));
        var comp = VibraniumCompilerDriver.CreateCompilationFromPath(Path.Combine(RepoRoot, "samples", "demo", "Program.cs"));
        var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
        Assert.True(outcome.Success, string.Join("\n", outcome.Errors));

        string binPath = Path.Combine(Path.GetTempPath(), $"vibranium-ldd-{Guid.NewGuid():N}");
        try
        {
            var (ec, outText) = Llvm18Backend.CompileToExecutable(outcome.LlvmIr!, binPath, optLevel: 3, addressSanitizer: false);
            Assert.True(ec == 0, outText);

            AssertLibcOnlyDependencies(binPath);
        }
        finally
        {
            if (File.Exists(binPath)) File.Delete(binPath);
        }
    }

    private static void AssertLibcOnlyDependencies(string binary)
    {
        // Unlike ldd, DT_NEEDED excludes the ELF interpreter and the vDSO.
        // Call only for unsanitized production binaries, not ASan/LSan builds.
        var (readelfExit, dynamicTable, readelfError) = RunProcess("readelf", "-d", binary);
        Assert.True(readelfExit == 0, readelfError);
        string[] needed = dynamicTable.Split('\n')
            .Where(line => line.Contains("(NEEDED)", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf('[') + 1)..line.IndexOf(']')])
            .ToArray();
        Assert.Equal(new[] { "libc.so.6" }, needed);

        var (nmExit, imports, nmError) = RunProcess("nm", "-D", "--undefined-only", binary);
        Assert.True(nmExit == 0, nmError);
        foreach (string line in imports.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string symbol = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1].Split('@')[0];
            Assert.False(symbol is "fmod" or "fmodf", $"Unexpected remainder libcall: {line}");
        }
    }

    [Fact]
    public void OneMillionNodeList_DropsIteratively_WithoutStackOverflow_OrLeak()
    {
        string src = """
            class Node
            {
                public int Val;
                public Option<Node> Next;
                public Node(int v, Option<Node> n) { Val = v; Next = n; }
            }

            static class BigList
            {
                static int Main()
                {
                    Option<Node> head = None;
                    for (int i = 0; i < 1000000; i++)
                    {
                        head = Some(new Node(i, head));
                    }
                    int top = head switch
                    {
                        Some<Node>(var n) => n.Val,
                        NoneType => -1,
                    };
                    WriteLine(top);
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: false);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("999999\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void DisjointStructFields_And_NllBorrowEnd_MatchManagedAndNative()
    {
        string src = """
            struct Pair
            {
                public int Left;
                public int Right;
                public Pair(int l, int r) { Left = l; Right = r; }
            }

            static class DisjointTest
            {
                static void AddBoth(ref int a, ref int b)
                {
                    a += 10;
                    b += 20;
                }

                static int Main()
                {
                    var p = new Pair(1, 2);
                    ref var l = ref p.Left;
                    ref var r = ref p.Right;
                    l += 5;
                    r += 7;
                    AddBoth(ref p.Left, ref p.Right);
                    WriteLine(p.Left);
                    WriteLine(p.Right);
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("16\n29\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Theory]
    [InlineData("Vec<int> v = [10, 20]; WriteLine(v[5]); return 0;", "panic: index out of bounds\n")]
    [InlineData("int z = 0; int q = 10 / z; WriteLine(q); return 0;", "panic: attempt to divide by zero\n")]
    [InlineData("int m = int.MinValue; int d = -1; int q = m / d; WriteLine(q); return 0;", "panic: arithmetic overflow\n")]
    [InlineData("Option<int> x = None; WriteLine(x.Unwrap()); return 0;", "panic: called Option.Unwrap() on None\n")]
    public void PanicParity_StandaloneManagedAndNative_BothExit101WithIdenticalMessage(string body, string expectedStderr)
    {
        string src = $$"""
            static class PanicApp
            {
                static int Main()
                {
                    {{body}}
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: false);
        Assert.Equal(101, res.ManagedExit);
        Assert.Equal(101, res.NativeExit);
        Assert.Equal(expectedStderr, res.ManagedErr);
        Assert.Equal(expectedStderr, res.NativeErr);
    }

    [Fact]
    public void Saxpy_Llvm18NoAlias_EliminatesVectorMemcheck_AndVectorizes()
    {
        string demoPath = Path.Combine(RepoRoot, "samples", "demo", "Program.cs");
        var comp = VibraniumCompilerDriver.CreateCompilationFromPath(demoPath);
        var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
        Assert.True(outcome.Success);

        string llFile = Path.Combine(Path.GetTempPath(), $"vibranium-saxpy-{Guid.NewGuid():N}.ll");
        try
        {
            File.WriteAllText(llFile, outcome.LlvmIr!);
            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llFile);
            Assert.True(verExit == 0, verErr);

            var (optExit, optIr, optErr) = RunProcess("opt-18", "-O3", "-S", llFile);
            Assert.True(optExit == 0, optErr);

            int saxpyStart = optIr.IndexOf("Saxpy", StringComparison.Ordinal);
            Assert.True(saxpyStart >= 0);
            int saxpyEnd = optIr.IndexOf("\n}\n", saxpyStart, StringComparison.Ordinal);
            string saxpyBody = optIr.Substring(saxpyStart, saxpyEnd - saxpyStart);

            Assert.Contains("<4 x float>", saxpyBody);
            Assert.DoesNotContain("vector.memcheck", saxpyBody);
        }
        finally
        {
            if (File.Exists(llFile)) File.Delete(llFile);
        }
    }

    [Fact]
    public void NegativeControl_DisablingBorrowCheck_CausesRealAsanHeapUseAfterFree()
    {
        string staleFile = Path.Combine(RepoRoot, "samples", "bad", "StaleVecBorrow.cs");
        var comp = VibraniumCompilerDriver.CreateCompilationFromPath(staleFile);

        var safeCheck = VibraniumFrontend.CompileAndCheck(comp);
        Assert.Contains(safeCheck.Diagnostics, d => d.Id == "VIB003");

        var failClosedOutcome = VibraniumCompilerDriver.CheckAndEmitIr(
            comp,
            new LlvmEmitOptions(AddressSanitizer: true));
        Assert.False(failClosedOutcome.Success);
        Assert.Null(failClosedOutcome.LlvmIr);

        Assert.NotNull(safeCheck.Module);
        string unverifiedIr = Llvm18Backend.EmitModuleIr(
            safeCheck.Module!,
            new LlvmEmitOptions(AddressSanitizer: true));

        string binPath = Path.Combine(Path.GetTempPath(), $"vibranium-uaf-{Guid.NewGuid():N}");
        try
        {
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(unverifiedIr, binPath, optLevel: 1, addressSanitizer: true);
            Assert.True(clangExit == 0, clangOut);

            var (exitCode, _, stderr) = RunProcess(binPath);
            Assert.NotEqual(0, exitCode);
            Assert.Contains("heap-use-after-free", stderr);
        }
        finally
        {
            if (File.Exists(binPath)) File.Delete(binPath);
        }
    }


    [Fact]
    public void LegacyPrintAndPrintLine_AreCompletelyRemovedFromPreludeAndIo()
    {
        var ioMethods = typeof(Vibranium.Io).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        var preludeMethods = typeof(Vibranium.Prelude).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain("Print", ioMethods);
        Assert.DoesNotContain("PrintLine", ioMethods);
        Assert.DoesNotContain("Line", ioMethods);
        Assert.DoesNotContain("Print", preludeMethods);
        Assert.DoesNotContain("PrintLine", preludeMethods);
        Assert.Contains("Write", ioMethods);
        Assert.Contains("WriteLine", ioMethods);
        Assert.Contains("Write", preludeMethods);
        Assert.Contains("WriteLine", preludeMethods);
    }

    [Fact]
    public void ConsoleWrite_PreludeWrite_IoWrite_AndEmptyWriteLine_MatchManagedAndNative()
    {
        string src = """
            sbyte sb = -42;
            byte b = 250;
            short sh = -30000;
            ushort ush = 60000;
            int i = -123456;
            uint ui = 4000000000u;
            long l = -9000000000000L;
            ulong ul = 18000000000000000000UL;
            long ni = -77;
            ulong nui = 88;

            Write(sb); Write(" ");
            Io.Write(b); Console.Write(" ");
            Console.WriteLine(sh);
            WriteLine(ush);
            Console.WriteLine(i);
            Io.WriteLine(ui);
            WriteLine(l);
            Console.WriteLine(ul);
            WriteLine(ni);
            Console.WriteLine(nui);
            Console.Write(true); Write(" "); Io.WriteLine(false);
            Console.WriteLine();
            Io.WriteLine();
            WriteLine();
            Console.WriteLine("done");
            return 0;
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void SmallAndUnsignedIntegers_ByteShortCharUintUlongNuint_MatchManagedAndNative()
    {
        string src = """
            static class P
            {
                static int HideInt(int x) => x;
                static long HideLong(long x) => x;
                static double HideDouble(double d) => d;

                static int Main()
                {
                    byte b1 = (byte)HideInt(300);
                    byte b2 = (byte)HideDouble(300.0);
                    byte b3 = (byte)HideDouble(-5.0);
                    byte b4 = 200;
                    b4 += (byte)HideInt(100);
                    WriteLine(b1);
                    WriteLine(b2);
                    WriteLine(b3);
                    WriteLine(b4);

                    sbyte sb = (sbyte)HideInt(200);
                    short s = (short)HideInt(70000);
                    ushort us = (ushort)HideInt(-2);
                    char ch = (char)HideInt(65601);
                    WriteLine(sb);
                    WriteLine(s);
                    WriteLine(us);
                    WriteLine((int)ch);

                    uint q = (uint)HideInt(-1);
                    WriteLine(q > 5u);
                    WriteLine((long)q);
                    WriteLine(q >> 17);
                    WriteLine(q / 3u);
                    WriteLine(q % 7u);

                    ulong nu = (ulong)HideLong(-1);
                    WriteLine(nu > 0);
                    WriteLine((long)(nu / 2));
                    WriteLine((long)(nu % 3));
                    double df = (double)nu;
                    WriteLine(df > 0.0);

                    ulong ul = (ulong)HideLong(-2);
                    WriteLine(ul > 1UL);
                    WriteLine((long)(ul >> 1));
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void VecByte_And_SpanByte_HaveTrueOneByteElementStride_And_PassAsan()
    {
        string src = """
            Vec<byte> v = [1, 2, 255];
            v.Push(42);
            Span<byte> s = v.AsSpan();
            WriteLine(s.IsEmpty);
            s[1] = 99;
            ReadOnlySpan<byte> ro = s.Slice(1, 3);
            WriteLine(ro.IsEmpty);
            Span<byte> empty = default;
            WriteLine(empty.IsEmpty);
            WriteLine(v[0]);
            WriteLine(v[1]);
            WriteLine(v[2]);
            WriteLine(v[3]);
            WriteLine(ro[0] + ro[1] + ro[2]);
            return 0;
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Contains("getelementptr inbounds i8, ptr", res.LlvmIr);
    }

    [Fact]
    public void LeftToRightEvaluationOrder_Operands_CompoundAssignments_And_NamedArgs_MatchManagedAndNative()
    {
        string src = """
            class Node { public int Value; public Node(int v) { Value = v; } }
            class CounterBox
            {
                public int V;
                public CounterBox(int v) { V = v; }
                [Mut] public int Bump() { V += 100; return 7; }
                public int Get() => V;
            }
            struct Pair { public int A; public int B; public Pair(int a, int b) { A = a; B = b; } }

            static class EvalApp
            {
                static int Set(ref int q, int nv, int ret) { q = nv; return ret; }
                static int SwapNode(ref Node x, Node fresh) { x = fresh; return 1; }
                static int ZapPair(ref Pair p) { p = new Pair(100, 200); return 1; }
                static int SumPair(Pair p, int k) => p.A + p.B + k;
                static int Add2(int a, int b) => a * 1000 + b;
                static int Add3(int a, int b, int c) => a * 1000000 + b * 1000 + c;
                static int Next(ref int c) { int o = c; c += 1; return o; }
                static int CombineNamed(int first, int second) => first * 100 + second;

                static int Main()
                {
                    int y = 2; WriteLine(y + Set(ref y, 50, 3));
                    y = 2; WriteLine(Add2(y, Set(ref y, 50, 3)));
                    y = 2; var pr = new Pair(y, Set(ref y, 50, 3)); WriteLine(pr.A); WriteLine(pr.B);

                    var cb = new CounterBox(1); WriteLine(cb.V + cb.Bump());
                    cb = new CounterBox(1); WriteLine(Add2(cb.V, cb.Bump()));
                    cb = new CounterBox(1); WriteLine(Add2(cb.Get(), cb.Bump()));
                    cb = new CounterBox(1); cb.V += cb.Get(); WriteLine(cb.V);
                    pr = new Pair(3, 4); pr.A += Set(ref pr.B, 50, 7); WriteLine(pr.A); WriteLine(pr.B);

                    var nd = new Node(7); WriteLine(nd.Value + SwapNode(ref nd, new Node(100)));
                    var pt = new Pair(1, 2); WriteLine(SumPair(pt, ZapPair(ref pt)));

                    y = 2; WriteLine(Add3(y, Set(ref y, 50, 3), y));
                    y = 2; WriteLine(y < Set(ref y, 50, 3));
                    y = 2; WriteLine(y * y + Set(ref y, 50, 3) + y);

                    int i = 0; Span<int> sp = [10, 20, 30]; sp[i] = Set(ref i, 1, 99); WriteLine(sp[0]); WriteLine(sp[1]);
                    i = 0; Vec<int> v = [10, 20, 30]; v[i] = Set(ref i, 2, 77); WriteLine(v[0]); WriteLine(v[2]);
                    i = 0; v[i] += Set(ref i, 1, 5); WriteLine(v[0]); WriteLine(v[1]);

                    int c = 0; Vec<int> arr = [10, 20]; arr[Next(ref c)] += Next(ref c) * 100; WriteLine(arr[0]); WriteLine(arr[1]);
                    y = 2; y += Set(ref y, 50, 3); WriteLine(y);
                    y = 2; WriteLine(y++ + y);
                    y = 2; WriteLine(y + y++);
                    y = 2; WriteLine(y + (y = 10));
                    y = 2; WriteLine(Add2(y, y = 10));
                    y = 2; WriteLine(Add2(y++, y++));
                    y = 2; WriteLine(y + ++y);

                    int nc = 1;
                    int namedSecond = Next(ref nc);
                    int namedFirst = Next(ref nc);
                    WriteLine(CombineNamed(first: namedFirst, second: namedSecond));
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Theory]
    [InlineData("v[99] = Loud();")]
    [InlineData("v[99] += Loud();")]
    public void LhsBoundsPanicOrder_SimpleAndCompoundAssignment_PanicsBeforeRhsSideEffects(string stmt)
    {
        string src = $$"""
            static class PanicOrderApp
            {
                static int Loud()
                {
                    WriteLine(777);
                    return 5;
                }

                static int Main()
                {
                    Vec<int> v = [10, 20];
                    {{stmt}}
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: false);
        Assert.Equal(101, res.ManagedExit);
        Assert.Equal(101, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedOut);
        Assert.Equal(string.Empty, res.NativeOut);
        Assert.Equal("panic: index out of bounds\n", res.ManagedErr);
        Assert.Equal("panic: index out of bounds\n", res.NativeErr);
    }

    [Fact]
    public void TwoPhaseBorrow_FibVecIndexing_And_SelfMoveOverwrite_MatchManagedAndNative()
    {
        string src = """
            class Boxed { public int V; public Boxed(int v) { V = v; } }
            static class App
            {
                static Boxed Step(Boxed b) => new Boxed(b.V + 10);
                static int Main()
                {
                    var x = new Boxed(5);
                    x = Step(x);
                    x = x;
                    Vec<int> fib = [1, 1, 0, 0, 0, 0];
                    for (int i = 2; i < fib.Count; i++)
                    {
                        fib[i] = fib[i - 1] + fib[i - 2];
                    }
                    WriteLine(x.V);
                    WriteLine(fib[5]);
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("15\n8\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void RefReturn_StaticVecFirst_ClassHolderAt_And_StructBagAt_MatchManagedAndNative()
    {
        string src = """
            class Holder
            {
                public Vec<int> V;
                public Holder(int n)
                {
                    V = new Vec<int>(n);
                    for (int i = 0; i < n; i++) { V.Push(i + 1); }
                }
                [Mut] public ref int At(int i) => ref V[i];
            }

            struct Bag
            {
                public Vec<int> V;
                public Bag(int n)
                {
                    V = new Vec<int>(1);
                    V.Push(n);
                }
                [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int At(int i) => ref V[i];
            }

            static class RefRetApp
            {
                static ref int First(ref Vec<int> v) => ref v[0];
                static ref readonly int FirstRo(in Vec<int> v) => ref v.AsReadOnlySpan()[0];

                static int Main()
                {
                    Vec<int> v = [10, 20];
                    ref var r0 = ref First(ref v);
                    r0 = 99;
                    WriteLine(FirstRo(in v));

                    var h = new Holder(3);
                    ref var hr = ref h.At(1);
                    hr = 77;
                    WriteLine(h.V[1]);

                    var b = new Bag(5);
                    ref var br = ref b.At(0);
                    br = 42;
                    WriteLine(b.V[0]);
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("99\n77\n42\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void StaticLocalFunctions_TopLevelAndGeneric_MatchManagedAndNative()
    {
        string src = """
            static int DoublePlusOne(int x)
            {
                static int Inner(int v) => v * 2;
                return Inner(x) + 1;
            }

            WriteLine(DoublePlusOne(20));
            return 0;
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("41\n", res.NativeOut);
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void DeepMultiChildTree_And_StructWrappedList_DropIteratively_WithoutStackOverflow_OrLeak()
    {
        string src = """
            class TreeNode
            {
                public int Val;
                public Option<TreeNode> Left;
                public Option<TreeNode> Right;
                public TreeNode(int v, Option<TreeNode> l, Option<TreeNode> r) { Val = v; Left = l; Right = r; }
            }

            struct Link
            {
                public Option<Cell> To;
                public Link(Option<Cell> to) { To = to; }
            }

            class Cell
            {
                public int V;
                public Link Next;
                public Cell(int v, Link next) { V = v; Next = next; }
            }

            static class DeepDropApp
            {
                static int Main()
                {
                    Option<TreeNode> leftSpine = None;
                    Option<TreeNode> rightSpine = None;
                    Option<Cell> wrapped = None;
                    for (int i = 0; i < 200000; i++)
                    {
                        leftSpine = Some(new TreeNode(i, leftSpine, Some(new TreeNode(-i, None, None))));
                        rightSpine = Some(new TreeNode(i, None, rightSpine));
                        wrapped = Some(new Cell(i, new Link(wrapped)));
                    }

                    int check = 0;
                    if (leftSpine.AsRef() is Some<TreeNode>(var lt)) check += lt.Val;
                    if (rightSpine.AsRef() is Some<TreeNode>(var rt)) check += rt.Val;
                    if (wrapped.AsRef() is Some<Cell>(var c)) check += c.V;
                    WriteLine(check);
                    return 0;
                }
            }
            """;

        var res = CompileAndRunBoth(src, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    [Fact]
    public void AsanAtOptZero_WithByRefAndStructParameters_DoesNotClobberParameterSlots()
    {
        string src = """
            class BstNode
            {
                public int Key;
                public Option<BstNode> Left;
                public Option<BstNode> Right;
                public BstNode(int k, Option<BstNode> l, Option<BstNode> r) { Key = k; Left = l; Right = r; }
            }

            static class BstApp
            {
                static void Collect(in Option<BstNode> node, ref Vec<int> outKeys)
                {
                    if (node.AsRef() is Some<BstNode>(var n))
                    {
                        Collect(in n.Left, ref outKeys);
                        outKeys.Push(n.Key);
                        Collect(in n.Right, ref outKeys);
                    }
                }

                static int Main()
                {
                    Option<BstNode> root = Some(new BstNode(
                        10,
                        Some(new BstNode(5, None, None)),
                        Some(new BstNode(20, None, None))));
                    Vec<int> keys = [];
                    Collect(in root, ref keys);
                    WriteLine(keys[0] + keys[1] + keys[2]);
                    return 0;
                }
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-asan-o0-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcFile = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(srcFile, src);
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcFile);
            var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp, new LlvmEmitOptions(AddressSanitizer: true));
            Assert.True(outcome.Success, string.Join("\n", outcome.Errors));

            string binPath = Path.Combine(tempDir, "asan_o0_bin");
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(outcome.LlvmIr!, binPath, optLevel: 0, addressSanitizer: true);
            Assert.True(clangExit == 0, clangOut);

            var (nExit, nOut, nErr) = RunProcess(binPath);
            Assert.Equal(0, nExit);
            Assert.Equal("35\n", nOut);
            Assert.Equal(string.Empty, nErr);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }


    [Fact]
    public void CapturedPrimaryConstructors_ClassesStructsGenericsAndVecDrop_MatchManagedAndAsanClean()
    {
        const string code = """
            var counter = new Counter(41);
            Console.WriteLine(counter.Twice);
            Console.WriteLine(counter.Next());

            var scale = new Scale(6);
            Console.WriteLine(scale.Apply(7));

            var holder = new Holder([10, 20, 30]);
            Console.WriteLine(holder.InitialCount);
            holder.Push(40);
            Console.WriteLine(holder.Sum());

            var intBox = new ValueBox<int>(99);
            Console.WriteLine(intBox.Get());

            var optBox = new OptionBox<Vec<int>>(Some<Vec<int>>([3, 4, 5]));
            Console.WriteLine(optBox.HasValue());

            class Counter(int start)
            {
                public readonly int Twice = start * 2;
                public int Next() => start + 1;
            }

            readonly struct Scale(int factor)
            {
                public int Apply(int x) => x * factor;
            }

            class Holder(Vec<int> items)
            {
                public int InitialCount = items.Count;

                [Mut]
                public void Push(int x) => items.Push(x);

                public int Sum()
                {
                    int total = 0;
                    foreach (var v in items)
                    {
                        total += v;
                    }
                    return total;
                }
            }

            class ValueBox<T>(T value) where T : unmanaged
            {
                public T Get() => value;
            }

            class OptionBox<T>(Option<T> opt)
            {
                public bool HasValue() => opt.IsSome;
            }
            """;

        var res = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("82\n42\n42\n3\n100\n99\nTrue\n", res.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(string.Empty, res.NativeErr);
    }

    public static IEnumerable<object[]> NegativeSafetyCases()
    {
        yield return new object[]
        {
            "VIB000",
            """
            class Base { public int X; public Base(int x) { X = x; } }
            class Derived : Base { public Derived(int x) : base(x) {} }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App { static int Main() { throw new InvalidOperationException(); } }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static readonly int Secret = 42;
                static int Main() => Secret;
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static void BadOut(out Span<int> s) { s = default; }
                static int Main() => 0;
            }
            """
        };
        yield return new object[]
        {
            "VIB001",
            """
            class Boxed { public int V; public Boxed(int v) { V = v; } }
            static class App {
                static int Main() {
                    var a = new Boxed(1);
                    var b = a;
                    WriteLine(b.V);
                    return a.V;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB001",
            """
            class Boxed { public int V; public Boxed(int v) { V = v; } }
            static class App {
                static void Consume(Boxed x) { WriteLine(x.V); }
                static int Main() {
                    var a = new Boxed(1);
                    for (int i = 0; i < 2; i++) { Consume(a); }
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB001",
            """
            class Node { public int V; public Option<Node> Next; public Node(int v, Option<Node> n) { V = v; Next = n; } }
            static class App {
                static int Main() {
                    Option<Node> head = Some(new Node(1, None));
                    var c1 = head.AsMut();
                    var c2 = c1;
                    c2.Set(None);
                    c1.Set(None);
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB001",
            """
            enum E { Bad }
            class Boxed { public int V; public Boxed(int v) { V = v; } }
            static class App {
                static Result<Boxed, E> Make() => Err(E.Bad);
                static int Main() {
                    if (Make().IsErr(out var ok, out var err)) {
                        return ok.V;
                    }
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB002",
            """
            static class App {
                static void Both(ref int a, ref int b) { a += b; }
                static int Main() { int x = 1; Both(ref x, ref x); return x; }
            }
            """
        };
        yield return new object[]
        {
            "VIB002",
            """
            static class App {
                static void Overlap(Span<int> a, Span<int> b) { a[0] = b[0]; }
                static int Main() { Span<int> s = [1, 2]; Overlap(s, s); return s[0]; }
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            static class App {
                static int Main() {
                    Vec<int> v = [1, 2];
                    ref var r = ref v[0];
                    v.Push(3);
                    return r;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            class Node { public int V; public Option<Node> Next; public Node(int v, Option<Node> n) { V = v; Next = n; } }
            static class App {
                static int Main() {
                    Option<Node> head = Some(new Node(1, None));
                    var cur = head.AsMut();
                    if (cur is Some<Node>(var n)) {
                        cur = n.Next.AsMut();
                        return n.V;
                    }
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            class Inner { public int X; public Inner(int x) { X = x; } }
            class Outer { public Inner Child; public Outer(Inner c) { Child = c; } }
            static class App {
                static void Bad(in Outer o) { o.Child.X = 99; }
                static int Main() => 0;
            }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            struct Counter { public int X; public void Inc() { X += 1; } }
            static class App {
                static void Bad(in Counter c) { c.Inc(); }
                static int Main() => 0;
            }
            """
        };
        yield return new object[]
        {
            "VIB005",
            """
            class Inner { public int X; public Inner(int x) { X = x; } }
            class Holder { public Inner Item; public Holder(Inner i) { Item = i; } }
            static class App {
                static int Main() {
                    var h = new Holder(new Inner(5));
                    var stolen = h.Item;
                    return stolen.X;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Res : IDisposable { public void Dispose() {} }
            static class App {
                static int Main() {
                    var r = new Res();
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Res : IDisposable { public void Dispose() {} }
            static class App {
                static void Consume(Res r) { using var x = r; }
                static int Main() {
                    using var r = new Res();
                    Consume(r);
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB007",
            """
            class Node { public int V; public Option<Node> Next; public Node(int v, Option<Node> n) { V = v; Next = n; } }
            static class App {
                static OptionRef<Node> Dangle() {
                    Option<Node> local = Some(new Node(1, Some(new Node(2, None))));
                    if (local.AsRef() is Some<Node>(var n)) {
                        return n.Next.AsRef();
                    }
                    return default;
                }
                static int Main() => 0;
            }
            """
        };
        yield return new object[]
        {
            "VIB008",
            """
            class Node { public int V; public Node(int v) { V = v; } }
            static class App {
                static int Main() {
                    Node? n = null;
                    return n is null ? 0 : 1;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB008",
            """
            enum E { Err }
            static class App {
                static int Main() {
                    Result<int, E> bad = default;
                    return bad.IsOk ? 1 : 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            ref struct CustomView { public Span<int> S; public CustomView(Span<int> s) { S = s; } }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static void AssignView<T>(ref T dst, T src) where T : allows ref struct { dst = src; }
                static int Main() => 0;
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Main() {
                    Vec<int> v = [1, 2];
                    var s = v.AsSpan();
                    ref var rs = ref s;
                    return rs[0];
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            class Node { public int V; public Option<Node> Next; public Node(int v, Option<Node> n) { V = v; Next = n; } }
            static class App {
                static int Main() {
                    Option<Node> head = Some(new Node(1, None));
                    var cur = head.AsMut();
                    ref var rc = ref cur;
                    rc.Set(None);
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Hide(int x) => x;
                static int Main() => checked(Hide(int.MaxValue) + 1);
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Hide(int x) => x;
                static int Main() {
                    checked { return Hide(10) + 1; }
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Main() {
                    Span<int> s = stackalloc int[4];
                    return s[0];
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Main() {
                    string a = "hi";
                    string b = "hi";
                    return a == b ? 1 : 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Main() {
                    string bad = "\uD800";
                    WriteLine(bad);
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class App {
                static int Main() {
                    decimal d = 1m;
                    return d > 0m ? 1 : 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            class Node { public int V; public Option<Node> Next; public Node(int v, Option<Node> n) { V = v; Next = n; } }
            static class App {
                static int Main() {
                    var a = new Node(1, None);
                    a.Next = Some(a);
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Res : IDisposable { public void Dispose() {} }
            static class App {
                static int Main() {
                    Option<Res> opt = Some(new Res());
                    return opt.IsSome ? 1 : 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Res : IDisposable { public void Dispose() {} }
            static class App {
                static int Main() {
                    Result<int, Res> r = Err(new Res());
                    return r.IsOk ? 1 : 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Res : IDisposable { public void Dispose() {} }
            static class App {
                static int Main() {
                    var r = new Res();
                    r.Dispose();
                    return 0;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Res : IDisposable { public void Dispose() {} }
            static class App {
                static void BorrowRes(ref Res r) {}
                static int Main() => 0;
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            struct Bag {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(1); V.Push(n); }
                [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int At(int i) => ref V[i];
            }
            static class App {
                static int Main() {
                    var b = new Bag(3);
                    ref var r = ref b.At(0);
                    b.V.Push(99);
                    return r;
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            static class App {
                static int Grow(ref Vec<int> v) { v.Push(99); return 7; }
                static int Main() {
                    Vec<int> v = [1, 2];
                    v[0] += Grow(ref v);
                    return v[0];
                }
            }
            """
        };
        yield return new object[]
        {
            "VIB008",
            """
            class Inner { public int X; public Inner(int x) { X = x; } }
            class BadCtor {
                public Inner Child;
                public BadCtor() {
                    int v = ReadChild();
                    WriteLine(v);
                    Child = new Inner(10);
                }
                public int ReadChild() => Child.X;
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            class Cell { public int V; public Cell(int v) { V = v; } }
            readonly struct Wrapper {
                public readonly Cell Inner;
                public Wrapper(Cell c) { Inner = c; }
                public void SneakyMut() { Inner.V = 999; }
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB001",
            """
            class BadDoubleMove(Vec<int> items) {
                public Vec<int> Data = items;
                public int Len() => items.Count;
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            class Counter(int start) {
                [Mut] public void BadReassign() { start = 99; }
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            struct MutCounter(int start) {
                public void BadCompound() { start += 1; }
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            class Holder(Vec<int> items) {
                public void BadPush() => items.Push(1);
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            class Cell(int v) { public int V = v; }
            readonly struct Wrapper(Cell inner) {
                public void SneakyMut() { inner.V = 999; }
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB005",
            """
            class Holder(Vec<int> items) {
                public Vec<int> Steal() => items;
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB006",
            """
            class Lease : IDisposable {
                public int Id = 1;
                public void Dispose() {}
            }
            class BadCapture(Lease lease) {
                public int Read() => lease.Id;
            }
            static class App { static int Main() => 0; }
            """
        };
        yield return new object[]
        {
            "VIB008",
            """
            class Node(int v) { public int V = v; }
            struct BadStruct(Node node) {
                public int Read() => node.V;
            }
            static class App {
                static int Main() {
                    var b = new BadStruct();
                    return b.Read();
                }
            }
            """
        };

        // P0-1: Option<Vec<int>> / Result<Vec<int>, int> / Option<NonCopyStruct> place scrutinee bindings are shared borrows
        yield return new object[]
        {
            "VIB004",
            """
            Vec<int> xs = [10, 20];
            Option<Vec<int>> opt = Some(xs);
            if (opt is Some<Vec<int>>(var v))
            {
                v.Push(30);
            }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            Vec<int> xs = [10, 20];
            Result<Vec<int>, int> res = Ok(xs);
            if (res is Ok<Vec<int>>(var v))
            {
                v.Push(30);
            }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            Option<Wrap> opt = Some(new Wrap([1, 2]));
            switch (opt)
            {
                case Some<Wrap>(var w):
                    w.Add(3);
                    break;
                case NoneType:
                    break;
            }
            struct Wrap(Vec<int> items)
            {
                public Vec<int> Items = items;
                [Mut] public void Add(int x) { Items.Push(x); }
            }
            """
        };

        // P1-8: Option<Class> / Result<Class, E> place scrutinee bindings are shared borrows unless .AsMut() is used
        yield return new object[]
        {
            "VIB004",
            """
            Option<Node> opt = Some(new Node(1));
            if (opt is Some<Node>(var n))
            {
                n.Inc();
            }
            class Node(int x)
            {
                public int X = x;
                [Mut] public void Inc() { X += 1; }
            }
            """
        };
        yield return new object[]
        {
            "VIB004",
            """
            Result<Node, int> res = Ok(new Node(1));
            if (res is Ok<Node>(var n))
            {
                n.X = 99;
            }
            class Node(int x) { public int X = x; }
            """
        };

        // P0-2: Two-phase receiver borrow conflicts with exclusive borrow / mutation / move during argument evaluation
        yield return new object[]
        {
            "VIB003",
            """
            Vec<int> v = [1, 2, 3];
            v.Push(v.Pop().Unwrap());
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            Vec<int> v = [1, 2];
            v.Push(ResetAndGet(ref v));
            static int ResetAndGet(ref Vec<int> r)
            {
                r = [9];
                return 7;
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            var acc = new Acc(10);
            acc.Add(acc.Drain());
            class Acc(int total)
            {
                public int Total = total;
                [Mut] public void Add(int x) { Total += x; }
                [Mut] public int Drain() { int t = Total; Total = 0; return t; }
            }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            var h = new Holder([1, 2, 3]);
            h.V.Push(Swap(ref h));
            static int Swap(ref Holder r)
            {
                r = new Holder([9]);
                return 42;
            }
            class Holder(Vec<int> v) { public Vec<int> V = v; }
            """
        };
        yield return new object[]
        {
            "VIB003",
            """
            var o = new Outer(new Inner([1, 2]));
            o.I.Add(Swap(ref o.I));
            static int Swap(ref Inner r)
            {
                r = new Inner([9]);
                return 5;
            }
            class Inner(Vec<int> v)
            {
                public Vec<int> V = v;
                [Mut] public void Add(int x) { V.Push(x); }
            }
            struct Outer(Inner i) { public Inner I = i; }
            """
        };

        yield return new object[]
        {
            "VIB003",
            """
            var h = new Holder(10);
            h.X = Swap(ref h);
            static int Swap(ref Holder r)
            {
                r = new Holder(99);
                return 1;
            }
            class Holder(int x) { public int X = x; }
            """
        };

        // P1-7: Shared receiver borrow conflicts with mutating argument on the same receiver
        yield return new object[]
        {
            "VIB003",
            """
            var h = new Holder(10);
            int y = h.Read(Swap(ref h));
            WriteLine(y);
            static int Swap(ref Holder r)
            {
                r = new Holder(99);
                return 1;
            }
            class Holder(int x)
            {
                public int X = x;
                public int Read(int delta) => X + delta;
            }
            """
        };

        // P0-3: default / default(T) / new T() on unconstrained generic T : unmanaged is rejected as non-zero-safe
        yield return new object[]
        {
            "VIB008",
            """
            int x = Zero<int>();
            WriteLine(x);
            static T Zero<T>() where T : unmanaged => default;
            """
        };
        yield return new object[]
        {
            "VIB008",
            """
            int x = MakeNew<int>();
            WriteLine(x);
            static T MakeNew<T>() where T : unmanaged => new T();
            """
        };

        // P1-1: User [Native] declaration is rejected outside official Vibranium.Std.Sys
        yield return new object[]
        {
            "VIB000",
            """
            WriteLine(FakeSys.Bad());
            static class FakeSys
            {
                [Native("__vib_fake")]
                public static int Bad() => 0;
            }
            """
        };
    }

    [Theory]
    [MemberData(nameof(NegativeSafetyCases))]
    public async Task NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds(string expectedId, string source)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"vibranium-neg-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(tempFile, source);
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(tempFile);
            Assert.Empty(comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

            var cliDiags = VibraniumFrontend.CompileAndCheck(comp).Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Contains(expectedId, cliDiags);

            var withAnalyzers = comp.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer()));
            var analyzerDiags = (await withAnalyzers.GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .Select(d => d.Id)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            Assert.Equal(cliDiags, analyzerDiags);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptionAndResult_NonCopyPlaceScrutinee_SharedReads_And_AsMutMutation_MatchClrAndNative()
    {
        string source = """
            Vec<int> xs = [10, 20, 30];
            Option<Vec<int>> optVec = Some(xs);
            int sum = 0;
            if (optVec is Some<Vec<int>>(var v))
            {
                sum += v.Count + v[0] + v[2];
            }
            if (optVec is Some<Vec<int>>(var vAgain))
            {
                sum += vAgain[1];
            }

            Option<Node> optNode = Some(new Node(5));
            if (optNode is Some<Node>(var sharedNode))
            {
                sum += sharedNode.X;
            }
            if (optNode.AsMut() is Some<Node>(var mutNode))
            {
                mutNode.Add(12);
            }
            if (optNode is Some<Node>(var afterNode))
            {
                sum += afterNode.X;
            }

            WriteLine(sum);

            class Node(int x)
            {
                public int X = x;
                [Mut] public void Add(int d) { X += d; }
            }
            """;

        var res = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("85\n", res.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public void ReceiverEvaluationOrder_LeftToRight_And_TwoPhaseSharedReads_MatchClrAndNative()
    {
        string source = """
            Vec<int> v = [3, 4];
            v.Push(v.Count);
            WriteLine(v[2]);

            Vec<int> fib = [0, 1, 0, 0, 0, 0];
            for (int i = 2; i < fib.Count; i++)
            {
                fib[i] = fib[i - 1] + fib[i - 2];
            }
            WriteLine(fib[5]);

            Vec<int> order = [];
            int r = SelectBox(ref order, 100).Compute(RecordArg(ref order, 23));
            WriteLine(r);
            WriteLine(order[0]);
            WriteLine(order[1]);

            static BoxVal SelectBox(ref Vec<int> log, int tag)
            {
                log.Push(tag);
                return new BoxVal(7);
            }

            static int RecordArg(ref Vec<int> log, int val)
            {
                log.Push(val);
                return val;
            }

            class BoxVal(int baseVal)
            {
                public int BaseVal = baseVal;
                public int Compute(int arg) => BaseVal + arg;
            }
            """;

        var res = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("2\n5\n30\n100\n23\n", res.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut, res.NativeOut);
    }

    [Fact]
    public async Task SpoofedFilePaths_SysManaged_And_VibraniumStdDirectory_FailClosed_InCliAndAnalyzer()
    {
        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-spoof-{Guid.NewGuid():N}");
        try
        {
            // 1. User file named Sys.Managed.cs must NOT bypass borrow/move verification
            string spoofManagedDir = Path.Combine(scratchDir, "trusted-name");
            Directory.CreateDirectory(spoofManagedDir);
            string spoofManagedPath = Path.Combine(spoofManagedDir, "Sys.Managed.cs");
            File.WriteAllText(
                spoofManagedPath,
                """
                Vec<int> a = [1, 2, 3];
                Vec<int> b = a;
                WriteLine(a[0]);
                WriteLine(b[0]);
                """);

            var compManaged = VibraniumCompilerDriver.CreateCompilationFromPath(spoofManagedPath);
            var cliManaged = VibraniumFrontend.CompileAndCheck(compManaged).Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Contains("VIB001", cliManaged);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compManaged).Success);

            var anManaged = (await compManaged.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(cliManaged, anManaged);

            // 2. User file under .../Vibranium.Std/ must NOT skip Gatekeeper (try/catch -> VIB000, null -> VIB008)
            string spoofStdDir = Path.Combine(scratchDir, "x", "Vibranium.Std");
            Directory.CreateDirectory(spoofStdDir);
            string spoofTryPath = Path.Combine(spoofStdDir, "Try.cs");
            File.WriteAllText(
                spoofTryPath,
                """
                try
                {
                    string s = null!;
                    WriteLine(s);
                }
                catch
                {
                }
                """);

            var compStd = VibraniumCompilerDriver.CreateCompilationFromPath(spoofTryPath);
            var cliStd = VibraniumFrontend.CompileAndCheck(compStd).Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Contains("VIB000", cliStd);
            Assert.Contains("VIB008", cliStd);
            Assert.False(VibraniumCompilerDriver.CheckAndEmitIr(compStd).Success);

            var anStd = (await compStd.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(cliStd, anStd);

            // 3. User file under .../Vibranium.Std/Sys/Sys.cs must NOT unlock [Native]
            string spoofSysDir = Path.Combine(spoofStdDir, "Sys");
            Directory.CreateDirectory(spoofSysDir);
            string spoofSysPath = Path.Combine(spoofSysDir, "Sys.cs");
            File.WriteAllText(
                spoofSysPath,
                """
                namespace Vibranium.Std;
                public static partial class Sys
                {
                    [Vibranium.Native("__vib_evil")]
                    public static int Evil() => 0;
                }
                """);

            var compSys = VibraniumCompilerDriver.CreateCompilationFromPath(spoofSysPath);
            var cliSys = VibraniumFrontend.CompileAndCheck(compSys).Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Contains("VIB000", cliSys);

            var anSys = (await compSys.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(cliSys, anSys);
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    // =========================================================================
    // Tier-A BCL Ergonomics & Plain-string Core / Stage-0 / Analyzer Parity Tests
    // =========================================================================

    [Fact]
    public void CoreTierA_StringAsUtf8_WriteSpan_StdioString_VecAdd_Expect_UnwrapOrDefault_And_GenericIndexRange_MatchNativeAndAsan()
    {
        string source = """
            // 1. String.AsUtf8: ASCII, non-ASCII, astral UTF-8, BOM, and embedded NUL (constant + runtime string)
            ReadOnlySpan<byte> litAscii = "VIB".AsUtf8();
            ReadOnlySpan<byte> litCafe = "café".AsUtf8();
            ReadOnlySpan<byte> litCrab = "🦀🚀".AsUtf8();
            ReadOnlySpan<byte> litBom = "\uFEFFok".AsUtf8();
            ReadOnlySpan<byte> litNul = "a\0b".AsUtf8();
            string dynStr = PickStr(true);
            ReadOnlySpan<byte> dynSpan = dynStr.AsUtf8();
            WriteLine(litAscii.Length); // 3
            WriteLine(litCafe.Length);  // 5
            WriteLine(litCrab.Length);  // 8
            WriteLine(litBom.Length);   // 5
            WriteLine(litNul.Length);   // 3
            WriteLine(litNul[1]);       // 0
            WriteLine(dynSpan.Length);  // 8

            // 2. Write / WriteLine(ReadOnlySpan<byte>), Io.Write / Io.WriteLine(ReadOnlySpan<byte>), Stdio.WriteOut(string)
            Write("W1:".AsUtf8());
            WriteLine(litCafe);
            Io.Write("W2:".AsUtf8());
            Io.WriteLine(litCrab);
            Stdio.WriteOut("S1:plain-string\n");

            // 3. Vec<T>.Add (including two-phase v.Add(v.Count))
            Vec<int> nums = [10, 20];
            nums.Add(30);
            nums.Add(nums.Count);
            WriteLine(nums[2] + nums[3]); // 33

            // 4. Option/Result Expect & UnwrapOrDefault (including L1 linear Expect)
            Option<int> someVal = Some(42);
            Option<int> noneVal = None;
            WriteLine(someVal.Expect("must have int")); // 42
            WriteLine(someVal.UnwrapOrDefault());       // 42
            WriteLine(noneVal.UnwrapOrDefault());       // 0

            Option<ZeroPair> nonePair = None;
            ZeroPair zp = nonePair.UnwrapOrDefault();
            WriteLine(zp.A + zp.B); // 0

            Result<int, int> okRes = Ok(99);
            Result<int, int> errRes = Err(5);
            WriteLine(okRes.Expect("must be ok")); // 99
            WriteLine(okRes.UnwrapOrDefault());    // 99
            WriteLine(errRes.UnwrapOrDefault());   // 0

            Option<Lease> optLease = Some(new Lease(7));
            {
                using var l1 = optLease.Expect("lease opt");
            }
            Result<Lease, int> resLease = Ok(new Lease(8));
            {
                using var l2 = resLease.Expect("lease res");
            }

            // 5. Generic C# implicit Index (^k) and Range (a..b, ..) on custom structs/classes
            var roView = new ByteWindow([11, 22, 33, 44, 55]);
            WriteLine(roView[^1]); // 55
            WriteLine(roView[^4]); // 22
            ReadOnlySpan<byte> mid = roView[1..^1];
            WriteLine(mid.Length); // 3
            WriteLine(mid[0] + mid[^1]); // 22 + 44 = 66
            ReadOnlySpan<byte> all = roView[..];
            WriteLine(all.Length); // 5

            var mutBox = new RefSlotCounter([5, 6, 7, 8]);
            mutBox[^1] = 80;
            WriteLine(mutBox[^1]);       // 80
            WriteLine(mutBox.LenReads);  // 2 (once for write ^1, once for read ^1)

            var setBox = new SetterPair(10, 20);
            setBox[^1] = 77;
            WriteLine(setBox[0] + setBox[^1]); // 87

            static string PickStr(bool first) => first ? "λ→🦀!" : "fallback";

            readonly struct ZeroPair(int a, int b)
            {
                public readonly int A = a;
                public readonly int B = b;
            }

            class ByteWindow(Vec<byte> data)
            {
                private Vec<byte> _data = data;
                public int Length => _data.Count;
                public byte this[int index] => _data[index];
                public ReadOnlySpan<byte> Slice(int start, int length) => _data.ReadOnlySlice(start, length);
            }

            class RefSlotCounter(Vec<int> items)
            {
                private Vec<int> _items = items;
                public int LenReads;
                public int Count
                {
                    [Mut]
                    get
                    {
                        LenReads += 1;
                        return _items.Count;
                    }
                }
                public ref int this[int index]
                {
                    [Mut] get => ref _items[index];
                }
            }

            struct SetterPair(int a, int b)
            {
                public int A = a;
                public int B = b;
                public readonly int Count => 2;
                public int this[int index]
                {
                    readonly get => index == 0 ? A : B;
                    set
                    {
                        if (index == 0) A = value;
                        else B = value;
                    }
                }
            }

            class Lease(int id) : IDisposable
            {
                public void Dispose()
                {
                    Write("dispose:");
                    WriteLine(id);
                }
            }
            """;

        var res = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedErr);
        Assert.Equal(string.Empty, res.NativeErr);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void CoreTierA_Utf8LinesForeach_AllLineEndings_Nested_And_SavedSliceAfterLoop_MatchNativeAndAsan()
    {
        string source = """
            // 1. Empty string -> 0 lines
            int emptyCount = 0;
            foreach (var ignoredLine in "".AsUtf8().Lines()) emptyCount += 1;
            WriteLine(emptyCount); // 0

            // 2. Trailing \n and \r\n -> no phantom trailing empty line
            PrintLines("a\n".AsUtf8());
            PrintLines("a\r\n".AsUtf8());

            // 3. Single "\n" -> 1 empty line; "a\n\nb\n" -> ["a", "", "b"]
            PrintLines("\n".AsUtf8());
            PrintLines("a\n\nb\n".AsUtf8());

            // 4. Standalone CR and mixed CR / CRLF / LF + unterminated tail
            PrintLines("r1\rr2\r\nr3\ntail".AsUtf8());

            // 5. Span<byte>.Lines() (mutable Span receiver reborrowed shared) + post-loop mutation allowed
            Span<byte> mutBuf = [(byte)'x', (byte)'\n', (byte)'y'];
            foreach (var ln in mutBuf.Lines())
            {
                Write("<");
                Write(ln);
                WriteLine(">");
            }
            mutBuf[0] = (byte)'z';
            WriteLine(mutBuf[0]); // 122

            // 6. Nested foreach + saving slice past loop exit while owner Vec<byte> stays alive
            Vec<byte> owner = [(byte)'f', (byte)'i', (byte)'r', (byte)'s', (byte)'t', (byte)'\n', (byte)'l', (byte)'a', (byte)'s', (byte)'t'];
            scoped ReadOnlySpan<byte> saved = ""u8;
            int pairCount = 0;
            foreach (var outer in owner.AsReadOnlySpan().Lines())
            {
                saved = outer;
                foreach (var inner in owner.AsReadOnlySpan().Lines())
                {
                    pairCount += outer.Length + inner.Length;
                }
            }
            WriteLine(pairCount); // (5+5) + (5+4) + (4+5) + (4+4) = 36
            WriteLine(saved);     // last

            // 7. Returning static line slice is valid
            WriteLine(FirstStaticLine()); // header

            static ReadOnlySpan<byte> FirstStaticLine()
            {
                foreach (var line in "header\r\nbody".AsUtf8().Lines())
                {
                    return line;
                }
                return ""u8;
            }

            static void PrintLines(ReadOnlySpan<byte> src)
            {
                Write("[");
                bool first = true;
                foreach (var line in src.Lines())
                {
                    if (!first) Write("|");
                    first = false;
                    Write(line);
                }
                WriteLine("]");
            }
            """;

        var res = CompileAndRunBoth(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void CoreTierA_ExpectPanics_OnNoneAndErr_Exit101_WithExactMessage_MatchNative()
    {
        string optPanicSrc = """
            Stdio.WriteOut("before-opt\n");
            Option<int> n = None;
            WriteLine(n.Expect("custom option failure 🦀"));
            """;
        var optRes = CompileAndRunBoth(optPanicSrc, withAsan: false);
        Assert.Equal(101, optRes.ManagedExit);
        Assert.Equal(101, optRes.NativeExit);
        Assert.Equal("before-opt\n", optRes.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(optRes.ManagedOut.Replace("\r\n", "\n"), optRes.NativeOut.Replace("\r\n", "\n"));
        Assert.Equal("panic: custom option failure 🦀\n", optRes.NativeErr.Replace("\r\n", "\n"));
        Assert.Equal(optRes.NativeErr.Replace("\r\n", "\n"), optRes.ManagedErr.Replace("\r\n", "\n"));

        string resPanicSrc = """
            Stdio.WriteErr("warn:");
            Result<int, int> e = Err(404);
            WriteLine(e.Expect("custom result failure"));
            """;
        var resRes = CompileAndRunBoth(resPanicSrc, withAsan: false);
        Assert.Equal(101, resRes.ManagedExit);
        Assert.Equal(101, resRes.NativeExit);
        Assert.Equal("warn:panic: custom result failure\n", resRes.NativeErr.Replace("\r\n", "\n"));
        Assert.Equal(resRes.NativeErr.Replace("\r\n", "\n"), resRes.ManagedErr.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity()
    {
        (string Name, string Code, string ExpectedId, bool IncludeStd)[] cases =
        [
            (
                "Utf8LinesLocalBanned",
                """
                var lines = "a\nb".AsUtf8().Lines();
                """,
                "VIB000",
                true
            ),
            (
                "Utf8LinesParameterBanned",
                """
                WriteLine(0);
                static void TakeLines(Utf8Lines l) { }
                """,
                "VIB000",
                true
            ),
            (
                "Utf8LinesRefForeachBanned",
                """
                foreach (ref var line in "a\nb".AsUtf8().Lines())
                {
                    WriteLine(line.Length);
                }
                """,
                "VIB004",
                true
            ),
            (
                "MutateOwnerDuringLinesForeach",
                """
                Vec<byte> buf = [(byte)'a', (byte)'\n', (byte)'b'];
                foreach (var line in buf.AsReadOnlySpan().Lines())
                {
                    buf.Add((byte)'!');
                    WriteLine(line.Length);
                }
                """,
                "VIB003",
                true
            ),
            (
                "MutateOwnerWhileSavedLineSliceLive",
                """
                Vec<byte> buf = [(byte)'a', (byte)'\n', (byte)'b'];
                scoped ReadOnlySpan<byte> saved = ""u8;
                foreach (var line in buf.AsReadOnlySpan().Lines())
                {
                    saved = line;
                }
                buf.Add((byte)'!');
                WriteLine(saved.Length);
                """,
                "VIB003",
                true
            ),
            (
                "ReturnEscapingLineSliceFromLocalVec",
                """
                WriteLine(BadEscape().Length);
                static ReadOnlySpan<byte> BadEscape()
                {
                    Vec<byte> local = [(byte)'a', (byte)'\n'];
                    foreach (var line in local.AsReadOnlySpan().Lines())
                    {
                        return line;
                    }
                    return ""u8;
                }
                """,
                "VIB007",
                true
            ),
            (
                "OptionUnwrapOrDefaultLinearBanned",
                """
                Option<Lease> opt = None;
                using var l = opt.UnwrapOrDefault();
                class Lease : IDisposable { public void Dispose() { } }
                """,
                "VIB006",
                true
            ),
            (
                "ResultUnwrapOrDefaultLinearBanned",
                """
                Result<Lease, int> res = Err(1);
                using var l = res.UnwrapOrDefault();
                class Lease : IDisposable { public void Dispose() { } }
                """,
                "VIB006",
                true
            ),
            (
                "OptionUnwrapOrDefaultNonZeroSafeBanned",
                """
                Option<Vec<int>> opt = None;
                var v = opt.UnwrapOrDefault();
                WriteLine(v.Count);
                """,
                "VIB008",
                true
            ),
            (
                "ResultUnwrapOrDefaultNonZeroSafeBanned",
                """
                Result<string, int> res = Err(1);
                var s = res.UnwrapOrDefault();
                WriteLine(s);
                """,
                "VIB008",
                true
            ),
            (
                "NoStdRejectsIntTryParse",
                """
                if (int.TryParse("42", out int v))
                {
                    WriteLine(v);
                }
                """,
                "VIB000",
                false
            ),
            (
                "NoStdRejectsMemoryExtensions",
                """
                using System;
                ReadOnlySpan<byte> a = "ok"u8;
                WriteLine(a.SequenceEqual("ok"u8));
                """,
                "VIB000",
                false
            ),
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-tiera-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            foreach (var tc in cases)
            {
                string path = Path.Combine(scratchDir, $"{tc.Name}.cs");
                File.WriteAllText(path, tc.Code);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(path, tc.Name, includeStd: tc.IncludeStd);
                var cliIds = VibraniumFrontend.CompileAndCheck(comp).Diagnostics
                    .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.True(cliIds.Contains(tc.ExpectedId), $"Case '{tc.Name}' expected {tc.ExpectedId}, got [{string.Join(", ", cliIds)}]");

                var analyzerIds = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(cliIds, analyzerIds);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Round2_PropertyAndIndexerCompoundAssign_MutProp_DefaultGeneric_ExtRecvOrder_RangeOrder_And_StringLines_MatchNativeAndAsan()
    {
        string code = """
            using System;
            using Vibranium.Std;

            // 1. P0-1 & P2-1: Property & Indexer +=, -=, ++, -- (prefix & postfix), byte promotion, property-level [Mut]
            var c = new PropCounter(10, 250);
            WriteLine(ReadPropShared(in c)); // 10
            c.Value += 5;
            WriteLine(c.Value++);            // 15 (postfix returns old)
            WriteLine(++c.Value);            // 17 (prefix returns new)
            c.Small += 10;                   // 250 + 10 wraps to 4
            WriteLine(c.Small);              // 4
            WriteLine(c.GetCalls);           // 5
            WriteLine(c.SetCalls);           // 4

            var deck = new IndexDeck([10, 20, 30, 40]);
            int idxEvals = 0;
            int recvEvals = 0;
            WriteLine(ReadDeckShared(in deck, 1));                 // 20
            BorrowDeck(ref recvEvals, ref deck)[ Bump(ref idxEvals, 1) ] += 7;
            WriteLine(deck[1]);                                    // 27
            WriteLine(recvEvals);                                  // 1
            WriteLine(idxEvals);                                   // 1
            WriteLine(BorrowDeck(ref recvEvals, ref deck)[^1]++);  // 40
            WriteLine(++deck[^1]);                                 // 42
            WriteLine(recvEvals);                                  // 2

            // User instance void operator += on struct & class
            var acc = new StructAcc(3);
            acc += 9;
            WriteLine(acc.Total); // 12

            // 2. P2-3: default(W<int>), new W<int>(), Option<W<int>>.UnwrapOrDefault(), Result<W<int>, int>.UnwrapOrDefault()
            Wrap<int> wDef = default;
            Wrap<int> wNew = new Wrap<int>();
            Option<Wrap<int>> optWrap = None;
            Result<Wrap<int>, int> resWrap = Err(9);
            WriteLine(wDef.Val + wNew.Val + optWrap.UnwrapOrDefault().Val + resWrap.UnwrapOrDefault().Val); // 0

            // 3. P1-1: Reduced extension-method receiver evaluated strictly before explicit args
            var log = new StringBuf();
            int extSum = TraceVal(ref log, "R,", 10).AddExt(TraceVal(ref log, "A,", 5));
            bool extSw = TraceSpan(ref log, "SR,", "hello"u8).StartsWith(TraceSpan(ref log, "SA,", "he"u8));
            Write(in log);
            WriteLine(extSum); // R,A,SR,SA,15
            WriteLine(extSw);  // True

            // 4. P1-4(a): Implicit Range evaluates Start, then End, BEFORE Length/Count once
            var rbox = new RangeOrderBox(4);
            WriteLine(rbox[^RangeStart()..RangeEnd()]); // start,end,len,slice,12

            Vec<int> rv = [100, 200, 300, 400];
            Span<int> rslice = rv[^VecStart()..VecEnd()];
            WriteLine(rslice.Length); // vstart,vend,2
            WriteLine(rslice[0] + rslice[1]); // 500

            // 5. P2-2: Valid named int.TryParse(s: ..., result: out h.X)
            var holder = new NumHolder(0);
            if (int.TryParse(s: ReadHolderDigits(in holder), result: out holder.X))
            {
                WriteLine(holder.X); // 987
            }

            // 6. StringUtf8Extensions.Lines(this string)
            foreach (var ln in "first\r\nsecond\nthird".Lines())
            {
                Write("<");
                Write(ln);
                WriteLine(">");
            }
            string dynLines = "p1\np2";
            foreach (var ln in dynLines.Lines())
            {
                dynLines = "mutated";
                WriteLine(ln);
            }
            WriteLine(dynLines);

            static int ReadPropShared(in PropCounter pc) => pc.Value;
            static int ReadDeckShared(in IndexDeck d, int i) => d[i];
            static int Bump(ref int c, int v) { c += 1; return v; }
            [return: LifetimeOf("d")]
            static ref IndexDeck BorrowDeck(ref int count, ref IndexDeck d) { count += 1; return ref d; }

            static int TraceVal(ref StringBuf b, string tag, int v) { b += tag; return v; }
            [return: LifetimeOf("s")]
            static ReadOnlySpan<byte> TraceSpan(ref StringBuf b, string tag, ReadOnlySpan<byte> s) { b += tag; return s; }

            static int RangeStart() { Write("start,"); return 3; }
            static int RangeEnd() { Write("end,"); return 3; }
            static int VecStart() { Write("vstart,"); return 3; }
            static int VecEnd() { Write("vend,"); return 3; }
            static string ReadHolderDigits(in NumHolder h) => h.X == 0 ? "987" : "0";

            struct Wrap<T> where T : unmanaged
            {
                public T Val;
            }

            struct StructAcc(int init)
            {
                public int Total = init;
                public void operator +=(int delta) => Total += delta;
            }

            sealed class NumHolder(int x)
            {
                public int X = x;
            }

            sealed class PropCounter(int v, byte b)
            {
                private int _v = v;
                private byte _b = b;
                public int GetCalls;
                public int SetCalls;

                [Mut]
                public int Value
                {
                    get => _v;
                    set { SetCalls += 1; _v = value; }
                }

                public byte Small
                {
                    [Mut] get { GetCalls += 1; return _b; }
                    [Mut] set { SetCalls += 1; _b = value; }
                }
            }

            sealed class IndexDeck(Vec<int> items)
            {
                private Vec<int> _items = items;
                public int Length => _items.Count;

                [Mut]
                public int this[int index]
                {
                    get => _items[index];
                    set => _items[index] = value;
                }
            }

            sealed class RangeOrderBox(int n)
            {
                public int N = n;
                public int Length { get { Write("len,"); return N; } }
                public int Slice(int s, int l) { Write("slice,"); return s * 10 + l; }
            }

            static class Round2Ext
            {
                public static int AddExt(this int x, int y) => x + y;
            }
            """;

        var res = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        Assert.Contains("start,end,len,slice,12\n", res.NativeOut);
        Assert.Contains("R,A,SR,SA,15\n", res.NativeOut);
    }

    [Fact]
    public async Task Round2_Gatekeeper_CrossUnitStdInternal_And_BorrowReservations_AnalyzerParity()
    {
        (string Name, string Code, string ExpectedId, bool IncludeStd)[] cases =
        [
            // P0-1: Compound assignment & ++ on shared receiver or conflicting mutation
            (
                "PropCompoundAssignOnSharedReceiver",
                """
                var c = new C(1);
                Bad(in c);
                static void Bad(in C c) { c.P += 5; }
                sealed class C(int x)
                {
                    private int _x = x;
                    [Mut] public int P { get => _x; set => _x = value; }
                }
                """,
                "VIB004",
                true
            ),
            (
                "PropCompoundAssignConflictRhs",
                """
                var c = new C(1);
                c.P += Swap(ref c);
                static int Swap(ref C c) { c = new C(9); return 2; }
                sealed class C(int x)
                {
                    private int _x = x;
                    [Mut] public int P { get => _x; set => _x = value; }
                }
                """,
                "VIB003",
                true
            ),
            (
                "IndexerIncConflictInArg",
                """
                var d = new D([1, 2]);
                d[^Swap(ref d)]++;
                static int Swap(ref D d) { d = new D([3, 4]); return 1; }
                sealed class D(Vec<int> v)
                {
                    private Vec<int> _v = v;
                    public int Length => _v.Count;
                    [Mut] public int this[int i] { get => _v[i]; set => _v[i] = value; }
                }
                """,
                "VIB003",
                true
            ),
            // P0-2: extern / DllImport / LibraryImport / UserDefinedOperator / Conversion / Auto-properties
            (
                "ExternDllImportLocalFunctionBanned",
                """
                using System.Runtime.InteropServices;
                WriteLine(abs(-5));
                [DllImport("libc")]
                static extern int abs(int x);
                """,
                "VIB000",
                true
            ),
            (
                "ExternDllImportClassMethodBanned",
                """
                using System.Runtime.InteropServices;
                WriteLine(Interop.abs(-3));
                static class Interop
                {
                    [DllImport("libc")]
                    public static extern int abs(int x);
                }
                """,
                "VIB000",
                true
            ),
            (
                "LibraryImportPartialMethodBanned",
                """
                using System.Runtime.InteropServices;
                static partial class Interop
                {
                    [LibraryImport("libc")]
                    public static partial int getpid();
                }
                """,
                "VIB000",
                true
            ),
            (
                "UserBinaryOperatorBanned",
                """
                var a = new V2(1);
                var b = new V2(2);
                var c = a + b;
                WriteLine(c.X);
                readonly struct V2(int x)
                {
                    public readonly int X = x;
                    public static V2 operator +(V2 a, V2 b) => new(a.X + b.X);
                }
                """,
                "VIB000",
                true
            ),
            (
                "UserConversionOperatorBanned",
                """
                var v = new V2(7);
                int x = v;
                WriteLine(x);
                readonly struct V2(int x)
                {
                    public readonly int X = x;
                    public static implicit operator int(V2 a) => a.X;
                }
                """,
                "VIB000",
                true
            ),
            (
                "UserCompoundSubOperatorBanned",
                """
                var v = new Acc(7);
                v -= 2;
                WriteLine(v.X);
                struct Acc(int x)
                {
                    public int X = x;
                    public void operator -=(int d) => X -= d;
                }
                """,
                "VIB000",
                true
            ),
            (
                "AutoPropertyGetSetBanned",
                """
                var c = new C();
                c.P = 5;
                WriteLine(c.P);
                sealed class C { public int P { get; [Mut] set; } }
                """,
                "VIB000",
                true
            ),
            (
                "AutoPropertyGetOnlyBanned",
                """
                var c = new C();
                WriteLine(c.P);
                sealed class C { public int P { get; } }
                """,
                "VIB000",
                true
            ),
            // P1-10: Cross-unit internal Vibranium.Std access ban
            (
                "CrossUnitStdInternalSysBanned",
                """
                using Vibranium.Std;
                WriteLine(Sys.ArgCount());
                """,
                "VIB000",
                true
            ),
            (
                "CrossUnitStdInternalStringBufCtorBanned",
                """
                using Vibranium.Std;
                Vec<byte> raw = [0xFF];
                var sb = new StringBuf(raw);
                WriteLine(sb.Length);
                """,
                "VIB000",
                true
            ),
            (
                "CrossUnitStdInternalIoErrorFromCodeBanned",
                """
                using Vibranium.Std;
                var err = IoError.FromCode(-2, IoOp.Open);
                WriteLine((int)err.Kind);
                """,
                "VIB000",
                true
            ),
            (
                "CrossUnitStdPartialSysSpoofBanned",
                """
                namespace Vibranium.Std;
                static partial class Sys
                {
                    public static void Main() { }
                }
                """,
                "VIB000",
                true
            ),
            // P1-1: Reduced extension receiver borrow reservation
            (
                "ExtMethodSpanReceiverConflictWithRefArg",
                """
                using System;
                using Vibranium.Std;
                var sb = new StringBuf();
                sb.Append("abcd");
                WriteLine(sb.AsUtf8().FirstByte(Reset(ref sb)));
                static int Reset(ref StringBuf sb) { sb = new StringBuf(); sb.Append("zzzz"); return 0; }
                static class Ext
                {
                    public static int FirstByte(this ReadOnlySpan<byte> s, int k) => s[k];
                }
                """,
                "VIB003",
                true
            ),
            (
                "StdByteSpanStartsWithReceiverConflict",
                """
                using System;
                using Vibranium.Std;
                var sb = new StringBuf();
                sb.Append("abcd");
                WriteLine(sb.AsUtf8().StartsWith(ResetSpan(ref sb)));
                static ReadOnlySpan<byte> ResetSpan(ref StringBuf sb) { sb = new StringBuf(); sb.Append("zzzz"); return "zz"u8; }
                """,
                "VIB003",
                true
            ),
            // P1-2: Flow-capture borrow reservation across branching subexpressions + +=
            (
                "BranchingArgSharedReceiverConflict",
                """
                var h = new H(1);
                WriteLine(h.Read(Cond() ? Swap(ref h) : 0));
                static bool Cond() => true;
                static int Swap(ref H h) { h = new H(100); return 7; }
                sealed class H(int n) { public int X = n; public int Read(int k) => X + k; }
                """,
                "VIB003",
                true
            ),
            (
                "BranchingArgInFieldBorrowConflict",
                """
                var h = new H(1);
                WriteLine(UseIn(in h.X, Cond() ? Swap(ref h) : 0));
                static bool Cond() => true;
                static int UseIn(in int x, int y) => x + y;
                static int Swap(ref H h) { h = new H(100); return 7; }
                sealed class H(int n) { public int X = n; }
                """,
                "VIB003",
                true
            ),
            (
                "BranchingArgVecPushConflict",
                """
                var h = new H([1]);
                h.V.Push(Cond() ? Swap(ref h) : 0);
                static bool Cond() => true;
                static int Swap(ref H h) { h = new H([9]); return 2; }
                sealed class H(Vec<int> v) { public Vec<int> V = v; }
                """,
                "VIB003",
                true
            ),
            (
                "BranchingRhsHeapFieldAssignConflict",
                """
                var h = new H(1);
                h.X = Cond() ? Swap(ref h) : 0;
                WriteLine(h.X);
                static bool Cond() => true;
                static int Swap(ref H h) { h = new H(100); return 7; }
                sealed class H(int n) { public int X = n; }
                """,
                "VIB003",
                true
            ),
            (
                "BranchingCtorInArgConflict",
                """
                var h = new H(1);
                var p = new Pair(in h.X, Cond() ? Swap(ref h) : 0);
                WriteLine(p.Sum);
                static bool Cond() => true;
                static int Swap(ref H h) { h = new H(100); return 7; }
                sealed class H(int n) { public int X = n; }
                readonly struct Pair
                {
                    public readonly int Sum;
                    public Pair(in int a, int b) { Sum = a + b; }
                }
                """,
                "VIB003",
                true
            ),
            (
                "HeapFieldCompoundAssignMutMethodConflict",
                """
                var cb = new CounterBox(1);
                cb.V += cb.Bump();
                WriteLine(cb.V);
                sealed class CounterBox(int v)
                {
                    public int V = v;
                    [Mut] public int Bump() { V += 100; return 7; }
                }
                """,
                "VIB003",
                true
            ),
            (
                "HeapFieldCompoundAssignConflict",
                """
                var h = new H(1);
                h.X += Swap(ref h);
                WriteLine(h.X);
                static int Swap(ref H h) { h = new H(100); return 7; }
                sealed class H(int n) { public int X = n; }
                """,
                "VIB003",
                true
            ),
            // P1-3 & P2-2: By-value !Copy move before subsequent args + reordered TryParse out borrow
            (
                "ByValueNonCopyMoveBeforeRefArg",
                """
                var h = new H(1);
                WriteLine(Two(h, Swap(ref h)));
                static int Swap(ref H h) { h = new H(100); return 7; }
                static int Two(H h, int k) => h.X * 2 + k;
                sealed class H(int n) { public int X = n; }
                """,
                "VIB001",
                true
            ),
            (
                "ByValueNonCopyMoveBeforeFieldReadArg",
                """
                var h = new H(1);
                WriteLine(Two(h, h.X));
                static int Two(H h, int k) => h.X + k;
                sealed class H(int n) { public int X = n; }
                """,
                "VIB001",
                true
            ),
            (
                "ByValueVecMoveBeforeRefArg",
                """
                Vec<int> v = [1, 2];
                WriteLine(TakeVec(v, Grow(ref v)));
                static int Grow(ref Vec<int> v) { v.Push(3); return v.Count; }
                static int TakeVec(Vec<int> v, int k) => v.Count + k;
                """,
                "VIB001",
                true
            ),
            (
                "CtorByValueNonCopyMoveBeforeRefArg",
                """
                var h = new H(1);
                var b = new Box(h, Swap(ref h));
                WriteLine(b.K);
                static int Swap(ref H h) { h = new H(100); return 7; }
                sealed class H(int n) { public int X = n; }
                sealed class Box(H h, int k) { public H Inner = h; public int K = k; }
                """,
                "VIB001",
                true
            ),
            (
                "OutFirstWrapperFieldConflict",
                """
                var h = new H(0);
                _ = Parse(out h.X, Txt(ref h));
                static bool Parse(out int result, string s) => int.TryParse(s, out result);
                static string Txt(ref H h) { h = new H(9); return "42"; }
                sealed class H(int x) { public int X = x; }
                """,
                "VIB003",
                true
            ),
            // P1-4(b): Pre-argument receiver reservation on Index & Range
            (
                "GenericRangeReceiverConflict",
                """
                var box = new Box(4);
                WriteLine(box[0..Reset(ref box)]);
                static int Reset(ref Box b) { b = new Box(8); return 1; }
                sealed class Box(int n)
                {
                    public int Length => n;
                    public int Slice(int s, int l) => n + s + l;
                }
                """,
                "VIB003",
                true
            ),
            (
                "GenericFromEndIndexReceiverConflict",
                """
                var box = new Box(4);
                WriteLine(box[^Reset(ref box)]);
                static int Reset(ref Box b) { b = new Box(8); return 1; }
                sealed class Box(int n)
                {
                    public int Length => n;
                    public int this[int i] => n + i;
                }
                """,
                "VIB003",
                true
            ),
            (
                "VecRangeReceiverConflict",
                """
                Vec<int> vc = [1, 2, 3, 4];
                Span<int> s = vc[0..ReplaceVec(ref vc)];
                WriteLine(s.Length);
                static int ReplaceVec(ref Vec<int> v) { v = [9, 9]; return 1; }
                """,
                "VIB003",
                true
            ),
            (
                "VecOpenEndRangeReceiverConflict",
                """
                Vec<int> v = [1, 2, 3];
                Span<int> s = v[Grow(ref v)..];
                WriteLine(s.Length);
                static int Grow(ref Vec<int> v) { v.Push(4); return 0; }
                """,
                "VIB003",
                true
            ),
            (
                "FromEndIndexSetterReceiverConflict",
                """
                var box = new Box([1, 2]);
                box[^Reset(ref box)] = 9;
                static int Reset(ref Box b) { b = new Box([5, 6]); return 1; }
                sealed class Box(Vec<int> v)
                {
                    private Vec<int> _v = v;
                    public int Length => _v.Count;
                    [Mut] public int this[int i] { get => _v[i]; set => _v[i] = value; }
                }
                """,
                "VIB003",
                true
            ),
            (
                "RefFromEndIndexReceiverConflict",
                """
                var box = new Box([1, 2]);
                ref int r = ref box[^Reset(ref box)];
                WriteLine(r);
                static int Reset(ref Box b) { b = new Box([5, 6]); return 1; }
                sealed class Box(Vec<int> v)
                {
                    private Vec<int> _v = v;
                    public int Length => _v.Count;
                    [Mut] public ref int this[int i] => ref _v[i];
                }
                """,
                "VIB003",
                true
            ),
            // P2-3: Open generic default(W<T>) and default(W<string>) rejected by VIB008
            (
                "OpenGenericDefaultWrapBanned",
                """
                WriteLine(Make<int>().Val);
                static Wrap<T> Make<T>() where T : unmanaged => default;
                struct Wrap<T> where T : unmanaged { public T Val; }
                """,
                "VIB008",
                true
            ),
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r2-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            foreach (var tc in cases)
            {
                string path = Path.Combine(scratchDir, $"{tc.Name}.cs");
                File.WriteAllText(path, tc.Code);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(path, tc.Name, includeStd: tc.IncludeStd);
                var cliIds = VibraniumFrontend.CompileAndCheck(comp).Diagnostics
                    .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.True(cliIds.Contains(tc.ExpectedId), $"Case '{tc.Name}' expected {tc.ExpectedId}, got [{string.Join(", ", cliIds)}]");

                var analyzerIds = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(cliIds, analyzerIds);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Round3_Differential_LvalueFlowCaptures_IsPatternExprs_And_GenericStaticCalls()
    {
        const string code = """
            using System;

            bool c = Flag(true);
            bool f = Flag(false);

            Vec<int> v = [1, 2, 3];
            v[0] = c ? 10 : 20;
            WriteLine(v[0]);

            v[^1] = c ? 30 : 40;
            WriteLine(v[2]);

            Span<int> sp = v.AsSpan();
            sp[1] = f ? 70 : 80;
            WriteLine(v[1]);

            SetRef(ref v[0], c ? 5 : 6);
            WriteLine(v[0]);

            if (c) v[0] = f ? 111 : 222;
            else v[1] = f ? 111 : 222;
            WriteLine(v[0]);

            Vec<Slot> vs = [new Slot(4), new Slot(9)];
            vs[0].X = c ? 10 : 20;
            WriteLine(vs[0].X);
            vs[0].Bump(c ? 7 : 8);
            WriteLine(vs[0].X);

            var h = new PropBox(3);
            h.P = c ? 10 : 20;
            WriteLine(h.P);
            h.P += c ? 5 : 6;
            WriteLine(h.P);

            var d = new Deck([100, 200, 300]);
            d[0] = f ? 11 : 12;
            d[0] += c ? 5 : 6;
            d[^1] += c ? 7 : 8;
            Write("D:");
            Write(d[0]);
            Write(",");
            WriteLine(d[2]);

            if (TryGet(42, out int gotPos))
            {
                WriteLine(gotPos);
            }
            if (!TryGet(-3, out int gotNeg))
            {
                WriteLine(gotNeg);
            }

            Vec<int> gv = [1];
            GetVecRef(ref gv).Push(c ? 2 : 3);
            WriteLine(gv[1]);

            Option<Vec<int>> ov = Some<Vec<int>>([9]);
            ov.UnwrapMut().Push(f ? 2 : 3);
            if (ov is Some<Vec<int>>(var innerV))
            {
                WriteLine(innerV[1]);
            }

            var sh = new CtorBranchHolder(c);
            WriteLine(sh.Tag);

            int a = 50;
            bool inRange = a > 3 && a < 100;
            WriteLine(inRange);
            WriteLine(a is 5 or 50);
            WriteLine(a is not 50);

            Option<int> optFive = Some(5);
            WriteLine(optFive is Some<int>(5));
            WriteLine(optFive is not NoneType);

            WriteLine(CallGenericStatic<int>());
            WriteLine('a');

            static bool Flag(bool b) => b;
            static void SetRef(ref int slot, int val) { slot = val; }
            static ref Vec<int> GetVecRef(ref Vec<int> vec) => ref vec;

            static bool TryGet(int k, out int v)
            {
                v = k > 0 ? k : 0;
                return k > 0;
            }

            static int CallGenericStatic<T>() => Factory<T>.Make();

            struct Slot(int x)
            {
                public int X = x;
                public void Bump(int delta) { X += delta; }
            }

            sealed class PropBox(int init)
            {
                private int _p = init;

                [Mut]
                public int P
                {
                    get { Write("G,"); return _p; }
                    set { Write("S,"); _p = value; }
                }
            }

            sealed class Deck(Vec<int> items)
            {
                private Vec<int> _items = items;
                public int Length => _items.Count;

                [Mut]
                public int this[int idx]
                {
                    get => _items[idx];
                    set => _items[idx] = value;
                }
            }

            sealed class CtorBranchHolder
            {
                public string Tag;
                public CtorBranchHolder(bool flag)
                {
                    Tag = flag ? "ctor-true" : "ctor-false";
                }
            }

            static class Factory<T>
            {
                public static int Make() => 73;
            }
            """;

        var res = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        Assert.Contains("10\n30\n80\n5\n222\n10\n17\n", res.NativeOut.Replace("\r\n", "\n"));
        Assert.Contains("D:17,307\n42\n0\n2\n3\nctor-true\nTrue\nTrue\nFalse\nTrue\nTrue\n73\n97\n", res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Round3_Gatekeeper_BorrowConflicts_And_SpecCompliance_AnalyzerParity()
    {
        (string Name, string Code, string ExpectedId, bool IncludeStd)[] cases =
        [
            // P0 / P1: Lvalue flow-capture borrow conflicts must emit VIB003
            (
                "VecElemBranchingRhsGrowConflict",
                """
                bool c = Cond();
                Vec<int> v = [1, 2, 3];
                v[0] = c ? Grow(ref v) : 3;
                WriteLine(v[0]);
                static bool Cond() => true;
                static int Grow(ref Vec<int> v) { v.Push(99); return 7; }
                """,
                "VIB003",
                true
            ),
            (
                "PropCompoundBranchingRhsSwapConflict",
                """
                bool c = Cond();
                var h = new H(1);
                h.P += c ? Swap(ref h) : 0;
                WriteLine(h.P);
                static bool Cond() => true;
                static int Swap(ref H h) { h = new H(50); return 4; }
                sealed class H(int x)
                {
                    private int _x = x;
                    [Mut] public int P { get => _x; set => _x = value; }
                }
                """,
                "VIB003",
                true
            ),
            (
                "RefVecElemShortCircuitGrowConflict",
                """
                bool c = Cond();
                Vec<int> v = [1, 2, 3];
                Set(ref v[0], c && GrowB(ref v));
                WriteLine(v[0]);
                static bool Cond() => true;
                static void Set(ref int slot, bool flag) { slot = flag ? 1 : 0; }
                static bool GrowB(ref Vec<int> v) { v.Push(9); return true; }
                """,
                "VIB003",
                true
            ),
            // P1-3: Reject `with` expressions with VIB000
            (
                "StructWithExpressionBanned",
                """
                var p = new Pt(1, 2);
                var q = p with { X = 5 };
                WriteLine(q.X);
                struct Pt(int x, int y) { public int X = x; public int Y = y; }
                """,
                "VIB000",
                true
            ),
            // P1-4: Enforce SPEC §3.5 strictly on IsErr / IsNone out-guards
            (
                "IsErrFieldOutTargetBanned",
                """
                var h = new Holder(9);
                Result<int, int> r = Ok(4);
                if (r.IsErr(out h.X, out var e)) { WriteLine(e.Error); }
                WriteLine(h.X);
                sealed class Holder(int x) { public int X = x; }
                """,
                "VIB000",
                true
            ),
            (
                "IsErrExistingLocalOkTargetBanned",
                """
                int existing = 0;
                Result<int, int> r = Ok(4);
                if (r.IsErr(out existing, out var e)) { WriteLine(e.Error); }
                WriteLine(existing);
                """,
                "VIB000",
                true
            ),
            (
                "IsErrExistingLocalErrTargetBanned",
                """
                Err<int> existingErr = Err(1);
                Result<int, int> r = Err(4);
                if (r.IsErr(out var ok, out existingErr)) { WriteLine(existingErr.Error); }
                else { WriteLine(ok); }
                """,
                "VIB000",
                true
            ),
            (
                "IsErrNestedInsideLogicalAndBanned",
                """
                bool c = true;
                Result<int, int> r = Ok(4);
                if (c && r.IsErr(out var ok, out var e)) { WriteLine(e.Error); }
                """,
                "VIB000",
                true
            ),
            (
                "IsNoneNestedInsideLogicalOrBanned",
                """
                bool c = false;
                Option<int> o = Some(4);
                if (c || o.IsNone(out var v)) { WriteLine(0); }
                """,
                "VIB000",
                true
            ),
            (
                "IsNoneExistingLocalBanned",
                """
                int v = 0;
                Option<int> o = Some(4);
                if (o.IsNone(out v)) { WriteLine(0); }
                WriteLine(v);
                """,
                "VIB000",
                true
            ),
            // P1-6 & P2: Gatekeeper bans
            (
                "StringImplicitToReadOnlySpanCharBanned",
                """
                using System;
                string a = "é🦀";
                ReadOnlySpan<char> s = a;
                WriteLine(s.Length);
                """,
                "VIB000",
                true
            ),
            (
                "StringSwitchStatementBanned",
                """
                string s = "abc";
                switch (s)
                {
                    case "abc":
                        WriteLine(1);
                        break;
                    default:
                        WriteLine(0);
                        break;
                }
                """,
                "VIB000",
                true
            ),
            (
                "StringSwitchExpressionBanned",
                """
                string s = "abc";
                int r = s switch { "abc" => 1, _ => 0 };
                WriteLine(r);
                """,
                "VIB000",
                true
            ),
            (
                "StringIsConstantPatternBanned",
                """
                string s = "abc";
                bool b = s is "abc";
                WriteLine(b);
                """,
                "VIB000",
                true
            ),
            (
                "OptionStringNestedConstantPatternBanned",
                """
                Option<string> os = Some("abc");
                bool b = os is Some<string>("abc");
                WriteLine(b);
                """,
                "VIB000",
                true
            ),
            (
                "EventDeclarationBanned",
                """
                using System;
                var b = new Bus();
                WriteLine(b.Count);
                sealed class Bus
                {
                    public int Count;
                    public event Action Tick;
                }
                """,
                "VIB000",
                true
            ),
            (
                "DelegateLocalVariableBanned",
                """
                using System;
                static void Nop() { }
                Action a = Nop;
                """,
                "VIB000",
                true
            ),
            (
                "DelegateParameterAndFieldBanned",
                """
                using System;
                WriteLine(1);
                sealed class Cb(Action fn)
                {
                    public Action Fn = fn;
                }
                """,
                "VIB000",
                true
            ),
            (
                "CSharp14ExtensionBlockBanned",
                """
                WriteLine(5);
                static class Ext
                {
                    extension(int x)
                    {
                        public int Twice() => x * 2;
                    }
                }
                """,
                "VIB000",
                true
            ),
            (
                "SizeOfExpressionBanned",
                """
                int n = sizeof(int);
                WriteLine(n);
                """,
                "VIB000",
                true
            ),
            (
                "NameOfExpressionBanned",
                """
                int alpha = 1;
                string s = nameof(alpha);
                WriteLine(s);
                """,
                "VIB000",
                true
            ),
            (
                "ParamsParameterBanned",
                """
                using System;
                WriteLine(Sum(1, 2));
                static int Sum(params ReadOnlySpan<int> xs) => xs.Length;
                """,
                "VIB000",
                true
            ),
            (
                "ObjectInitializerBanned",
                """
                var p = new Pt { X = 3 };
                WriteLine(p.X);
                struct Pt { public int X; }
                """,
                "VIB000",
                true
            ),
            (
                "CompoundAddOperatorInParamBanned",
                """
                var a = new Acc(1);
                WriteLine(a.V);
                struct Acc(int v)
                {
                    public int V = v;
                    public void operator +=(in int d) { V += d; }
                }
                """,
                "VIB000",
                true
            ),
            (
                "NonRefPropertyUserDefinedCompoundAddBanned",
                """
                var box = new Box(new Acc(1));
                box.Item += 5;
                WriteLine(box.Item.V);
                struct Acc(int v)
                {
                    public int V = v;
                    public void operator +=(int d) { V += d; }
                }
                sealed class Box(Acc a)
                {
                    private Acc _a = a;
                    [Mut] public Acc Item { get => _a; set => _a = value; }
                }
                """,
                "VIB000",
                true
            ),
            (
                "NonRefPropertyUserDefinedCompoundAddBranchingRhsBanned",
                """
                bool c = true;
                var box = new Box(new Acc(1));
                box.Item += c ? 5 : 6;
                WriteLine(box.Item.V);
                struct Acc(int v)
                {
                    public int V = v;
                    public void operator +=(int d) { V += d; }
                }
                sealed class Box(Acc a)
                {
                    private Acc _a = a;
                    [Mut] public Acc Item { get => _a; set => _a = value; }
                }
                """,
                "VIB000",
                true
            ),
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r3-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            foreach (var tc in cases)
            {
                string path = Path.Combine(scratchDir, $"{tc.Name}.cs");
                File.WriteAllText(path, tc.Code);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(path, tc.Name, includeStd: tc.IncludeStd);
                var cliIds = VibraniumFrontend.CompileAndCheck(comp).Diagnostics
                    .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.True(cliIds.Contains(tc.ExpectedId), $"Case '{tc.Name}' expected {tc.ExpectedId}, got [{string.Join(", ", cliIds)}]");

                var analyzerIds = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(cliIds, analyzerIds);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round4_DoubleBranchingLvalueFlowCaptures_DifferentialParity()
    {
        const string code = """
            Vec<int> v = [1, 2];
            bool d = v.Count > 5;
            bool c = v.Count > 1;
            v[d ? 0 : 1] = c ? 100 : 200;
            WriteLine(v[1]);

            v[^(d ? 1 : 2)] = c ? 10 : 20;
            WriteLine(v[0]);

            v[d ? 0 : 1] += c ? 1 : 2;
            WriteLine(v[1]);

            v[Id(d ? 0 : 1)] = c ? 1 : 2;
            WriteLine(v[1]);

            var h = new ClassIndexer(10, 20);
            h[d ? 0 : 1] += c ? 5 : 6;
            WriteLine(h[1]);

            var b = new StructIndexer(30, 40);
            b[^(d ? 1 : 2)] = c ? 10 : 20;
            b[d ? 0 : 1] += c ? 5 : 6;
            WriteLine(b[0] + b[1]);

            var g = new Grid2D(1, 2, 3, 4);
            g[d ? 0 : 1, 1] += c ? 2 : 3;
            WriteLine(g[1, 1]);

            var rg = new RefGrid2D(5, 6, 7, 8);
            rg[d ? 0 : 1, 1] += c ? 2 : 3;
            WriteLine(rg[1, 1]);

            Vec<PairItem> vs = [new PairItem(1), new PairItem(2)];
            vs[d ? 0 : 1].X = c ? 4 : 5;
            vs[d ? 0 : 1].Bump(c ? 4 : 5);
            WriteLine(vs[1].X);

            Put(ref v[d ? 0 : 1], c ? 100 : 200);
            WriteLine(v[1]);

            var vb = new VecBox([7, 8]);
            vb.V[d ? 0 : 1] = c ? 1 : 2;
            WriteLine(vb.V[1]);

            Vec<OuterPair> mat = [new OuterPair(new PairItem(1), new PairItem(2)), new OuterPair(new PairItem(3), new PairItem(4))];
            mat[d ? 0 : 1][c ? 0 : 1].X = (d || c) ? 77 : 88;
            WriteLine(mat[1][0].X);

            static int Id(int x) => x;
            static void Put(ref int slot, int val) { slot = val; }

            struct OuterPair(PairItem a, PairItem b)
            {
                private PairItem _a = a;
                private PairItem _b = b;
                [System.Diagnostics.CodeAnalysis.UnscopedRef]
                public ref PairItem this[int i]
                {
                    get
                    {
                        if (i == 0) return ref _a;
                        return ref _b;
                    }
                }
            }

            struct PairItem(int x)
            {
                public int X = x;
                public void Bump(int delta) { X += delta; }
            }

            sealed class VecBox(Vec<int> v)
            {
                public Vec<int> V = v;
            }

            sealed class ClassIndexer(int a, int b)
            {
                private int _a = a;
                private int _b = b;
                public int this[int i]
                {
                    get => i == 0 ? _a : _b;
                    [Mut] set { if (i == 0) _a = value; else _b = value; }
                }
            }

            struct StructIndexer(int a, int b)
            {
                private int _a = a;
                private int _b = b;
                public readonly int Length => 2;
                public int this[int i]
                {
                    readonly get => i == 0 ? _a : _b;
                    set { if (i == 0) _a = value; else _b = value; }
                }
            }

            struct Grid2D(int a, int b, int c, int d)
            {
                private int _a = a;
                private int _b = b;
                private int _c = c;
                private int _d = d;
                public int this[int r, int col]
                {
                    readonly get => (r * 2 + col) switch { 0 => _a, 1 => _b, 2 => _c, _ => _d };
                    set
                    {
                        int k = r * 2 + col;
                        if (k == 0) _a = value;
                        else if (k == 1) _b = value;
                        else if (k == 2) _c = value;
                        else _d = value;
                    }
                }
            }

            struct RefGrid2D(int a, int b, int c, int d)
            {
                private int _a = a;
                private int _b = b;
                private int _c = c;
                private int _d = d;
                [System.Diagnostics.CodeAnalysis.UnscopedRef]
                public ref int this[int r, int col]
                {
                    get
                    {
                        int k = r * 2 + col;
                        if (k == 0) return ref _a;
                        if (k == 1) return ref _b;
                        if (k == 2) return ref _c;
                        return ref _d;
                    }
                }
            }
            """;

        string checkDir = Path.Combine(Path.GetTempPath(), $"vibranium-r4-p1a-{Guid.NewGuid():N}");
        Directory.CreateDirectory(checkDir);
        try
        {
            string srcPath = Path.Combine(checkDir, "Program.cs");
            File.WriteAllText(srcPath, code);
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round4P1A");
            Assert.Empty(VibraniumFrontend.CompileAndCheck(comp).Diagnostics);
            var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(analyzerDiags);
        }
        finally
        {
            if (Directory.Exists(checkDir))
            {
                Directory.Delete(checkDir, recursive: true);
            }
        }

        var res = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("100\n10\n101\n1\n25\n55\n6\n10\n8\n100\n1\n77\n", res.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Round4_DoubleBranchingConflict_And_InvalidGuardNoCascadingTmp_AnalyzerParity()
    {
        (string Name, string Code, string ExpectedOnlyId)[] cases =
        [
            (
                "DoubleBranchVecGrowRhsConflict",
                """
                Vec<int> v = [1, 2];
                bool d = v.Count > 5;
                bool c = v.Count > 1;
                v[d ? 0 : 1] = c ? Grow(ref v) : 3;
                WriteLine(v[0]);
                static int Grow(ref Vec<int> v) { v.Add(9); return 4; }
                """,
                "VIB003"
            ),
            (
                "DoubleBranchVecFromEndGrowRhsConflict",
                """
                Vec<int> v = [1, 2];
                bool d = v.Count > 5;
                bool c = v.Count > 1;
                v[^(d ? 1 : 2)] = c ? Grow(ref v) : 3;
                WriteLine(v[0]);
                static int Grow(ref Vec<int> v) { v.Add(9); return 4; }
                """,
                "VIB003"
            ),
            (
                "DoubleBranchStructSetterConflict",
                """
                var s = new PairBuf(1, 2);
                bool d = false;
                bool c = true;
                s[d ? 0 : 1] += c ? Reset(ref s) : 3;
                WriteLine(s[1]);
                static int Reset(ref PairBuf p) { p[0] = 0; return 9; }
                struct PairBuf(int a, int b)
                {
                    private int _a = a;
                    private int _b = b;
                    public int this[int i]
                    {
                        readonly get => i == 0 ? _a : _b;
                        set { if (i == 0) _a = value; else _b = value; }
                    }
                }
                """,
                "VIB003"
            ),
            (
                "MisplacedIsNoneLocalAssignNoTmpCascade",
                """
                Option<int> o = Some(5);
                bool b = o.IsNone(out var v);
                WriteLine(b);
                WriteLine(v);
                """,
                "VIB000"
            ),
            (
                "MisplacedIsErrLocalAssignNoTmpCascade",
                """
                Result<int, int> r = Ok(5);
                bool b = r.IsErr(out var ok, out var err);
                WriteLine(b);
                WriteLine(ok + err.Error);
                """,
                "VIB000"
            ),
            (
                "MisplacedIsErrLogicalAndNoTmpCascade",
                """
                bool c = true;
                Result<int, int> r = Ok(4);
                if (c && r.IsErr(out var ok, out var e)) { WriteLine(e.Error); }
                """,
                "VIB000"
            ),
            (
                "MisplacedIsNoneLogicalOrNoTmpCascade",
                """
                bool c = false;
                Option<int> o = Some(4);
                if (c || o.IsNone(out var v)) { WriteLine(0); }
                """,
                "VIB000"
            ),
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r4-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            foreach (var tc in cases)
            {
                string path = Path.Combine(scratchDir, $"{tc.Name}.cs");
                File.WriteAllText(path, tc.Code);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(path, tc.Name);
                var cliDiags = VibraniumFrontend.CompileAndCheck(comp).Diagnostics;
                var cliIds = cliDiags.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal([tc.ExpectedOnlyId], cliIds);
                Assert.DoesNotContain(cliDiags, d => d.Message.Contains("__tmp_", StringComparison.Ordinal));

                var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .ToList();
                var analyzerIds = analyzerDiags.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(cliIds, analyzerIds);
                Assert.DoesNotContain(analyzerDiags, d => d.GetMessage().Contains("__tmp_", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round4_UnionPatternCombinatorsAndNestedUnions_DifferentialParity()
    {
        const string code = """
            WriteLine(CheckResultOr(Ok(42)));
            WriteLine(CheckResultOr(Ok(99)));
            WriteLine(CheckResultOr(Err(false)));
            WriteLine(CheckResultOr(Err(true)));

            WriteLine(SwitchResultCombinator(Ok(42)));
            WriteLine(SwitchResultCombinator(Ok(15)));
            WriteLine(SwitchResultCombinator(Err(false)));
            WriteLine(SwitchResultCombinator(Err(true)));

            Option<Option<int>> n1 = Some<Option<int>>(Some(77));
            Option<Option<int>> n2 = Some<Option<int>>(None);
            Option<Option<int>> n3 = None;
            WriteLine(EvalNestedOpt(n1));
            WriteLine(EvalNestedOpt(n2));
            WriteLine(EvalNestedOpt(n3));

            Result<Option<int>, int> ro1 = Ok<Option<int>>(Some(33));
            Result<Option<int>, int> ro2 = Ok<Option<int>>(None);
            Result<Option<int>, int> ro3 = Err(9);
            WriteLine(EvalResultOfOpt(ro1));
            WriteLine(EvalResultOfOpt(ro2));
            WriteLine(EvalResultOfOpt(ro3));

            Option<Option<Vec<int>>> nestedVec = Some<Option<Vec<int>>>(Some<Vec<int>>([10, 20, 30]));
            if (nestedVec is Some<Option<Vec<int>>>(Some<Vec<int>>(var borrowedInner)))
            {
                WriteLine(borrowedInner.Count);
            }
            if (Move(nestedVec) is Some<Option<Vec<int>>>(Some<Vec<int>>(var movedInner)))
            {
                movedInner.Add(40);
                WriteLine(movedInner.Count);
            }

            static bool CheckResultOr(Result<int, bool> r)
            {
                if (r is Ok<int>(42) or Err<bool>(false))
                {
                    return true;
                }
                return false;
            }

            static int SwitchResultCombinator(Result<int, bool> r) => r switch
            {
                Ok<int>(42) or Err<bool>(false) => 100,
                Ok<int>(var value) when value > 10 && value < 30 => 50,
                _ => 0,
            };

            static int EvalNestedOpt(Option<Option<int>> x)
            {
                if (x is Some<Option<int>>(Some<int>(var q)))
                {
                    return q;
                }
                if (x is Some<Option<int>>(NoneType))
                {
                    return -1;
                }
                return -2;
            }

            static int EvalResultOfOpt(Result<Option<int>, int> r) => r switch
            {
                Ok<Option<int>>(Some<int>(var k)) => k,
                Ok<Option<int>>(NoneType) => -10,
                Err<int>(var e) => -e,
            };
            """;

        string checkDir = Path.Combine(Path.GetTempPath(), $"vibranium-r4-pat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(checkDir);
        try
        {
            string srcPath = Path.Combine(checkDir, "Program.cs");
            File.WriteAllText(srcPath, code);
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round4Pat");
            Assert.Empty(VibraniumFrontend.CompileAndCheck(comp).Diagnostics);
            var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(analyzerDiags);
        }
        finally
        {
            if (Directory.Exists(checkDir))
            {
                Directory.Delete(checkDir, recursive: true);
            }
        }

        var res = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal("True\nFalse\nTrue\nFalse\n100\n50\n100\n0\n77\n-1\n-2\n33\n-10\n-9\n3\n4\n", res.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }
    private static string ResolveRound5VibSelfDllPath()
    {
#if DEBUG
        const string preferred = "Debug";
        const string fallback = "Release";
#else
        const string preferred = "Release";
        const string fallback = "Debug";
#endif
        string prefSelf = Path.Combine(RepoRoot, "src", "vib-self", "bin", preferred, "net11.0", "vib-self.dll");
        if (File.Exists(prefSelf))
        {
            return prefSelf;
        }

        string fbSelf = Path.Combine(RepoRoot, "src", "vib-self", "bin", fallback, "net11.0", "vib-self.dll");
        if (File.Exists(fbSelf))
        {
            return fbSelf;
        }

        return prefSelf;
    }

    private static (int ExitCode, string CombinedOutput) RunRound5VibSelfCheckBytes(byte[] utf8SourceBytes)
    {
        string vibSelfDll = ResolveRound5VibSelfDllPath();
        Assert.True(File.Exists(vibSelfDll), $"Expected built vib-self.dll at {vibSelfDll}");
        string sysroot = Path.Combine(RepoRoot, "src");

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-selfchk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            File.WriteAllBytes(srcPath, utf8SourceBytes);
            var (exit, stdout, stderr) = RunProcess(DotnetExe, vibSelfDll, "check", srcPath, "--sysroot", sysroot);
            return (exit, stdout + "\n" + stderr);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static (int ExitCode, string CombinedOutput) RunRound5VibSelfCheck(string sourceCode) =>
        RunRound5VibSelfCheckBytes(System.Text.Encoding.UTF8.GetBytes(sourceCode));

    private static async Task VerifyRound5TrackAFourWayDifferentialAsync(string sourceCode, string expectedStdout)
    {
        string vibSelfDll = ResolveRound5VibSelfDllPath();
        Assert.True(File.Exists(vibSelfDll), $"Expected built vib-self.dll at {vibSelfDll}");
        string sysroot = Path.Combine(RepoRoot, "src");

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(srcPath, sourceCode, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 1. Stage-0 Frontend + Analyzer zero-diagnostic check
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round5TrackADiff", includeStd: true);
            var checkRes = VibraniumFrontend.CompileAndCheck(comp);
            Assert.Empty(checkRes.Diagnostics);

            var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(analyzerDiags);

            // 2. Stage-0 LLVM 18 native build (-O3 + ASan) & execution
            var stage0Ir = VibraniumCompilerDriver.CheckAndEmitIr(
                comp,
                new LlvmEmitOptions(SharedNoAlias: false, AddressSanitizer: true));
            Assert.True(stage0Ir.Success, string.Join("\n", stage0Ir.Errors));
            Assert.NotNull(stage0Ir.LlvmIr);

            string stage0Bin = Path.Combine(tempDir, "stage0_native");
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(stage0Ir.LlvmIr!, stage0Bin, optLevel: 3, addressSanitizer: true);
            Assert.True(clangExit == 0, clangOut);
            var (s0Exit, s0Out, s0Err) = RunProcess(stage0Bin);

            // 3. Managed .NET 11 build & execution
            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = comp.SyntaxTrees.ToList();
            if (File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedComp = CSharpCompilation.Create(
                "Round5TrackAManaged",
                managedTrees,
                comp.References,
                comp.Options);

            string dllPath = Path.Combine(tempDir, "Round5TrackAManaged.dll");
            var emitRes = managedComp.Emit(dllPath);
            Assert.True(emitRes.Success, string.Join("\n", emitRes.Diagnostics));

            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(tempDir, "Vibranium.Core.dll"), overwrite: true);
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(tempDir, "Vibranium.Std.dll"), overwrite: true);
            const string runtimeConfig = """
                {
                  "runtimeOptions": {
                    "tfm": "net11.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "11.0.0"
                    },
                    "configProperties": {
                      "System.Globalization.Invariant": true
                    }
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "Round5TrackAManaged.runtimeconfig.json"), runtimeConfig);
            var (mExit, mOut, mErr) = RunProcess(DotnetExe, dllPath);

            // 4. Self-hosted vib-self emit-ll, opt-18 verify, native --asan build & execution
            string selfLlPath = Path.Combine(tempDir, "self.ll");
            var (selfEmitExit, selfEmitOut, selfEmitErr) = RunProcess(
                DotnetExe, vibSelfDll, "emit-ll", srcPath, "--sysroot", sysroot, "-o", selfLlPath);
            Assert.True(selfEmitExit == 0, $"vib-self emit-ll failed:\n{selfEmitOut}\n{selfEmitErr}");

            var (optExit, _, optErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", selfLlPath);
            Assert.True(optExit == 0, $"opt-18 verify failed:\n{optErr}");

            string selfBin = Path.Combine(tempDir, "self_native");
            var (selfBuildExit, selfBuildOut, selfBuildErr) = RunProcess(
                DotnetExe, vibSelfDll, "build", srcPath, "--sysroot", sysroot, "--cc", "clang-18", "--asan", "-o", selfBin);
            Assert.True(selfBuildExit == 0, $"vib-self build failed:\n{selfBuildOut}\n{selfBuildErr}");
            var (selfRunExit, selfRunOut, selfRunErr) = RunProcess(selfBin);

            Assert.True(mExit == 0, $"Managed failed ({mExit}):\n{mErr}");
            Assert.True(s0Exit == 0, $"Stage-0 native failed ({s0Exit}):\n{s0Err}");
            Assert.True(selfRunExit == 0, $"vib-self native failed ({selfRunExit}):\n{selfRunErr}");

            string normExpected = expectedStdout.Replace("\r\n", "\n");
            string normManaged = mOut.Replace("\r\n", "\n");
            string normStage0 = s0Out.Replace("\r\n", "\n");
            string normSelf = selfRunOut.Replace("\r\n", "\n");

            Assert.Equal(normExpected, normManaged);
            Assert.Equal(normManaged, normStage0);
            Assert.Equal(normManaged, normSelf);
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
    public void Round5TrackA_CiWorkflow_TimeoutIncreasedTo45Minutes()
    {
        string ciPath = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");
        Assert.True(File.Exists(ciPath), $"Expected CI workflow at {ciPath}");
        string ciText = File.ReadAllText(ciPath);
        Assert.Contains("timeout-minutes: 45", ciText);
        Assert.DoesNotContain("timeout-minutes: 20", ciText);
    }

    [Fact]
    public async Task Round5TrackA_UsingStatementOnExistingExpression_4WayDiagnosticParity()
    {
        string[] rejectedCases =
        [
            // Top-level local variable in `using (r) { ... }`
            """
            Res r = new Res();
            using (r)
            {
                r.Ping();
            }

            sealed class Res : IDisposable
            {
                public int X;
                public void Ping() => WriteLine(X);
                public void Dispose() { }
            }
            """,
            // Inside static method on local variable
            """
            Work();

            static void Work()
            {
                Res r = new Res();
                using (r)
                {
                    r.Ping();
                }
            }

            sealed class Res : IDisposable
            {
                public int X;
                public void Ping() => WriteLine(X);
                public void Dispose() { }
            }
            """,
            // Inside static method on parameter
            """
            Take(new Res());

            static void Take(Res r)
            {
                using (r)
                {
                    r.Ping();
                }
            }

            sealed class Res : IDisposable
            {
                public int X;
                public void Ping() => WriteLine(X);
                public void Dispose() { }
            }
            """,
            // Rvalue expression in `using (new Res())`
            """
            using (new Res())
            {
                WriteLine(1);
            }

            sealed class Res : IDisposable
            {
                public void Dispose() { }
            }
            """,
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-using-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < rejectedCases.Length; i++)
            {
                string srcPath = Path.Combine(scratchDir, $"Case{i}.cs");
                File.WriteAllText(srcPath, rejectedCases[i]);

                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Round5Using{i}");
                var feRes = VibraniumFrontend.CompileAndCheck(comp);
                var feIds = feRes.Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(["VIB000"], feIds);
                Assert.DoesNotContain(feRes.Diagnostics, d => d.Message.Contains("__cap_val_", StringComparison.Ordinal));
                Assert.DoesNotContain(feRes.Diagnostics, d => d.Message.Contains("__tmp_", StringComparison.Ordinal));

                var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .ToList();
                var anIds = analyzerDiags.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(["VIB000"], anIds);
                Assert.DoesNotContain(analyzerDiags, d => d.GetMessage().Contains("__cap_val_", StringComparison.Ordinal));
                Assert.DoesNotContain(analyzerDiags, d => d.GetMessage().Contains("__tmp_", StringComparison.Ordinal));

                var (selfExit, selfOut) = RunRound5VibSelfCheck(rejectedCases[i]);
                Assert.NotEqual(0, selfExit);
                Assert.Contains("VIB000", selfOut);
                Assert.DoesNotContain("VIB006", selfOut);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }

        // Valid declaration-form using statements continue to work across all 4 pipelines
        const string validUsingCode = """
            using (var r1 = new Res(10))
            {
                WriteLine(r1.Val);
            }
            using var r2 = new Res(20);
            WriteLine(r2.Val);

            sealed class Res(int val) : IDisposable
            {
                public int Val = val;
                public void Dispose() => WriteLine(Val + 1);
            }
            """;
        await VerifyRound5TrackAFourWayDifferentialAsync(validUsingCode, "10\n11\n20\n21\n");
    }

    [Fact]
    public async Task Round5TrackA_LexerEscapes_EscapeEAndInvalidEscapes_4WayParity()
    {
        const string validEscapeCode = """
            char escChar = '\e';
            WriteLine((int)escChar);

            string ansi = "\e[32mOK\e[0m";
            ReadOnlySpan<byte> ansiBytes = ansi.AsUtf8();
            WriteLine(ansiBytes.Length);
            WriteLine(ansiBytes[0]);
            WriteLine(ansiBytes[1]);
            WriteLine(ansiBytes[7]);

            ReadOnlySpan<byte> u8Ansi = "\e[31m!\e[0m"u8;
            WriteLine(u8Ansi.Length);
            WriteLine(u8Ansi[0]);
            WriteLine(u8Ansi[6]);

            ReadOnlySpan<byte> hexAndUni = "\x1B\u001B\U0000001B\U0001F680"u8;
            WriteLine(hexAndUni.Length);
            WriteLine(hexAndUni[0]);
            WriteLine(hexAndUni[1]);
            WriteLine(hexAndUni[2]);
            WriteLine(hexAndUni[3]);
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(
            validEscapeCode,
            "27\n11\n27\n91\n27\n10\n27\n27\n7\n27\n27\n27\n240\n");

        string[] invalidEscapes =
        [
            "string s = \"\\q\";\nWriteLine(s);\n",
            "ReadOnlySpan<byte> s = \"\\q\"u8;\nWriteLine(s.Length);\n",
            "char c = '\\q';\nWriteLine((int)c);\n",
            "string s = \"\\x\";\nWriteLine(s);\n",
            "string s = \"\\xz\";\nWriteLine(s);\n",
            "char c = '\\x';\nWriteLine((int)c);\n",
            "char c = '\\xz';\nWriteLine((int)c);\n",
            "string s = \"\\u12\";\nWriteLine(s);\n",
            "string s = \"\\u123z\";\nWriteLine(s);\n",
            "char c = '\\u12';\nWriteLine((int)c);\n",
            "string s = \"\\U000012\";\nWriteLine(s);\n",
            "string s = \"\\U00110000\";\nWriteLine(s);\n",
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-esc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < invalidEscapes.Length; i++)
            {
                var (selfExit, selfOut) = RunRound5VibSelfCheck(invalidEscapes[i]);
                Assert.True(selfExit != 0, $"Invalid escape case #{i} unexpectedly succeeded in vib-self: {invalidEscapes[i]}");
                Assert.Contains("VIB000", selfOut);

                string srcPath = Path.Combine(scratchDir, $"BadEsc{i}.cs");
                File.WriteAllText(srcPath, invalidEscapes[i]);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"BadEsc{i}");
                var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(outcome.Success, $"Invalid escape case #{i} unexpectedly succeeded in Stage-0");
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round5TrackA_NonAsciiUtf8Identifiers_4WayParity()
    {
        const string unicodeIdentCode = """
            #region ФЛАГ_ВКЛ
            int café = 14;
            int δέλτα = 28;
            int сумма = café + δέλτα;
            WriteLine(сумма);

            var точка = new Вектор2(3, 4);
            WriteLine(точка.ДлинаКвадрат());
            WriteLine(计算(café, @δέλτα: 5));

            WriteLine(99);
            #endregion

            static int 计算(int α_1, int @δέλτα) => α_1 * 2 + @δέλτα;

            readonly struct Вектор2(int xКоорд, int yКоорд)
            {
                public readonly int XКоорд = xКоорд;
                public readonly int YКоорд = yКоорд;
                public int ДлинаКвадрат() => XКоорд * XКоорд + YКоорд * YКоорд;
            }
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(unicodeIdentCode, "42\n25\n33\n99\n");

        byte[][] invalidUtf8Cases =
        [
            // U+00A0 NBSP is whitespace, not an identifier character after '@'
            System.Text.Encoding.UTF8.GetBytes("int @\u00A0x = 1;\nWriteLine(1);\n"),
            // U+2000 EN QUAD is whitespace, not an identifier character after '@'
            System.Text.Encoding.UTF8.GetBytes("int @\u2000x = 1;\nWriteLine(1);\n"),
            // U+3000 IDEOGRAPHIC SPACE is whitespace, not an identifier character after '@'
            System.Text.Encoding.UTF8.GetBytes("int @\u3000x = 1;\nWriteLine(1);\n"),
            // Mid-file U+FEFF BOM is whitespace/Cf, not an identifier start character after '@'
            System.Text.Encoding.UTF8.GetBytes("int @\uFEFFx = 1;\nWriteLine(1);\n"),
            // Emoji identifier
            System.Text.Encoding.UTF8.GetBytes("int \U0001F680 = 1;\nWriteLine(1);\n"),
            // Math operator ∑ (U+2211)
            System.Text.Encoding.UTF8.GetBytes("int \u2211 = 1;\nWriteLine(1);\n"),
            // Invalid overlong UTF-8 0xC0 0xAF in identifier position
            [.."int "u8, 0xC0, 0xAF, .." = 1;\n"u8],
            // Lone invalid UTF-8 byte 0xFF
            [.."int "u8, 0xFF, .." = 1;\n"u8],
        ];

        for (int i = 0; i < invalidUtf8Cases.Length; i++)
        {
            var (selfExit, selfOut) = RunRound5VibSelfCheckBytes(invalidUtf8Cases[i]);
            Assert.True(selfExit != 0, $"Invalid UTF-8 case #{i} unexpectedly succeeded in vib-self.");
            Assert.Contains("VIB000", selfOut);
        }
    }

    [Fact]
    public async Task Round5TrackA_IDisposableInterfaceValueBanned_4WayParity()
    {
        string[] bannedCases =
        [
            """
            IDisposable d = new Res();
            d.Dispose();
            sealed class Res : IDisposable { public void Dispose() { } }
            """,
            """
            System.IDisposable d = new Res();
            d.Dispose();
            sealed class Res : System.IDisposable { public void Dispose() { } }
            """,
            """
            WriteLine(1);
            static void CloseIt(IDisposable d) { }
            """,
            """
            WriteLine(1);
            static System.IDisposable Create() => new Res();
            sealed class Res : IDisposable { public void Dispose() { } }
            """,
            """
            Option<IDisposable> opt = None;
            WriteLine(opt.IsSome);
            """,
            """
            WriteLine(1);
            struct Holder { public System.IDisposable Item; }
            """,
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-idisp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < bannedCases.Length; i++)
            {
                string srcPath = Path.Combine(scratchDir, $"Disp{i}.cs");
                File.WriteAllText(srcPath, bannedCases[i]);

                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Round5Disp{i}");
                var feRes = VibraniumFrontend.CompileAndCheck(comp);
                Assert.Contains(feRes.Diagnostics, d => d.Id == "VIB000");

                var anDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .ToList();
                Assert.Contains(anDiags, d => d.Id == "VIB000");

                var (selfExit, selfOut) = RunRound5VibSelfCheck(bannedCases[i]);
                Assert.True(selfExit != 0, $"IDisposable case #{i} unexpectedly succeeded in vib-self");
                Assert.Contains("VIB000", selfOut);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }

        // Class declarations implementing `: IDisposable` and `: System.IDisposable` remain valid
        const string validDispClass = """
            using var a = new HandleA();
            using var b = new HandleB();
            WriteLine(7);

            sealed class HandleA : IDisposable
            {
                public void Dispose() => WriteLine(8);
            }

            sealed class HandleB : System.IDisposable
            {
                public void Dispose() => WriteLine(9);
            }
            """;
        await VerifyRound5TrackAFourWayDifferentialAsync(validDispClass, "7\n9\n8\n");
    }

    [Fact]
    public async Task Round5TrackA_OptionMutClassConstraint_4WayParity()
    {
        string[] invalidOptionMutCases =
        [
            """
            WriteLine(1);
            static bool BadInt(OptionMut<int> m) => m.IsSome;
            """,
            """
            WriteLine(1);
            static bool BadStruct(OptionMut<Pair> m) => m.IsSome;
            struct Pair { public int A; public int B; }
            """,
            """
            WriteLine(1);
            static bool BadVec(OptionMut<Vec<int>> m) => m.IsSome;
            """,
            """
            WriteLine(1);
            sealed class BadBox<T>
            {
                public static bool Check(OptionMut<T> m) => m.IsSome;
            }
            """,
            """
            WriteLine(1);
            static bool Check<T>(OptionMut<T> m) => m.IsSome;
            """,
            """
            Outer();
            static void Outer()
            {
                WriteLine(1);
                static bool Inner<T>(OptionMut<T> m) => m.IsSome;
            }
            """,
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-optmut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < invalidOptionMutCases.Length; i++)
            {
                var (selfExit, selfOut) = RunRound5VibSelfCheck(invalidOptionMutCases[i]);
                Assert.True(selfExit != 0, $"OptionMut invalid case #{i} unexpectedly succeeded in vib-self");
                Assert.Contains("VIB000", selfOut);

                string srcPath = Path.Combine(scratchDir, $"OptMut{i}.cs");
                File.WriteAllText(srcPath, invalidOptionMutCases[i]);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"OptMut{i}");
                var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(outcome.Success, $"OptionMut invalid case #{i} unexpectedly succeeded in Stage-0");
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }

        const string validOptionMutCode = """
            Option<CounterBox> slot = Some(new CounterBox(10));
            ReplaceWithTwentyFive(slot.AsMut());
            if (slot.AsRef() is Some<CounterBox>(var b1))
            {
                WriteLine(b1.Value);
            }

            Option<CounterBox> emptySlot = None;
            WriteLine(RefContainer<CounterBox>.IsPresent(emptySlot.AsMut()));

            Option<string> sSlot = Some("hi");
            WriteLine(CheckClassMethod(sSlot.AsMut()));

            static bool LocalGeneric<U>(OptionMut<U> item) where U : class => item.IsSome;
            WriteLine(LocalGeneric(emptySlot.AsMut()));
            WriteLine(LocalGeneric(slot.AsMut()));

            static void ReplaceWithTwentyFive(OptionMut<CounterBox> opt)
            {
                if (opt.IsSome)
                {
                    opt.Set(Some(new CounterBox(25)));
                }
            }

            static bool CheckClassMethod<T>(OptionMut<T> opt) where T : class => opt.IsSome;

            sealed class CounterBox(int value)
            {
                public int Value = value;
            }

            static class RefContainer<T> where T : class
            {
                public static bool IsPresent(OptionMut<T> opt) => opt.IsSome;
            }
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(validOptionMutCode, "25\nFalse\nTrue\nFalse\nTrue\n");
    }

    [Fact]
    public async Task Round5TrackA_ConstFieldEvaluator_TruncationFloatsEnumsCondBool_4WayParity()
    {
        const string validConstCode = """
            WriteLine(Consts.TruncByte);
            WriteLine(Consts.TruncSByte);
            WriteLine(Consts.UncheckedUIntNegOne);
            WriteLine(Consts.TruncUShort);
            WriteLine(Consts.FloatPosInt);
            WriteLine(Consts.FloatNegInt);
            WriteLine(Consts.FloatByte);
            WriteLine(Consts.UncheckedInfInt);
            WriteLine(Consts.UncheckedHugeLong);
            WriteLine((int)Consts.EnumTwo);
            WriteLine((int)Consts.EnumWrapped);
            WriteLine(Consts.EnumAsByte);
            WriteLine((int)Shade.AllBits);
            WriteLine(Consts.LogicTrue);
            WriteLine(Consts.FoldedLeft);
            WriteLine(Consts.FoldedRight);
            WriteLine(Consts.ForwardSum);
            WriteLine(Consts.FloatRemInt);

            enum Shade
            {
                Dark = 0,
                Mid = 2,
                AllBits = unchecked((int)4294967295U),
            }

            static class Consts
            {
                public const int ForwardSum = LaterBase + 12;
                public const int LaterBase = 30;

                public const byte TruncByte = unchecked((byte)257);
                public const sbyte TruncSByte = unchecked((sbyte)255);
                public const long UncheckedUIntNegOne = unchecked((uint)-1);
                public const ushort TruncUShort = unchecked((ushort)-2);

                public const int FloatPosInt = (int)3.9;
                public const int FloatRemInt = (int)(7.75 % 2.0);
                public const int FloatNegInt = (int)-3.9f;
                public const byte FloatByte = (byte)255.9;
                public const int UncheckedInfInt = unchecked((int)(1.0 / 0.0));
                public const long UncheckedHugeLong = unchecked((long)1e30);

                public const Shade EnumTwo = (Shade)2;
                public const Shade EnumWrapped = unchecked((Shade)300);
                public const byte EnumAsByte = (byte)Shade.Mid;

                public const bool LogicTrue = (3 < 5) && (10 >= 10) && !(4 == 5) && (true ^ false) && (2.5 <= 2.5);
                public const int FoldedLeft = 100 + 11;
                public const int FoldedRight = 200 + 22;
            }
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(
            validConstCode,
            "1\n-1\n4294967295\n65534\n3\n-3\n255\n0\n0\n2\n300\n2\n-1\nTrue\n111\n222\n42\n1\n");

        string[] invalidConstCases =
        [
            // Checked integer cast overflow in const field
            """
            WriteLine(C.B);
            static class C { public const byte B = (byte)257; }
            """,
            // Checked negative enum-to-byte cast
            """
            WriteLine(C.B);
            enum E { Neg = -1 }
            static class C { public const byte B = (byte)E.Neg; }
            """,
            // Checked out-of-range long-to-enum cast
            """
            WriteLine((int)C.V);
            enum E { Zero = 0 }
            static class C { public const E V = (E)5000000000L; }
            """,
            // Checked float-to-byte overflow
            """
            WriteLine(C.B);
            static class C { public const byte B = (byte)256.0; }
            """,
            // Checked Infinity-to-int overflow
            """
            WriteLine(C.X);
            static class C { public const int X = (int)(1.0 / 0.0); }
            """,
            // Checked NaN-to-int overflow
            """
            WriteLine(C.X);
            static class C { public const int X = (int)(0.0 / 0.0); }
            """,
            // Non-constant expression in const field initializer
            """
            WriteLine(C.X);
            static class C
            {
                public const int X = Compute();
                public static int Compute() => 7;
            }
            """,
            // Unselected ternary branch with non-constant expression is still rejected in const field
            """
            WriteLine(C.X);
            static class C
            {
                public const int X = true ? 10 : Compute();
                public static int Compute() => 7;
            }
            """,
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-const-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < invalidConstCases.Length; i++)
            {
                var (selfExit, selfOut) = RunRound5VibSelfCheck(invalidConstCases[i]);
                Assert.True(selfExit != 0, $"Invalid const case #{i} unexpectedly succeeded in vib-self");
                Assert.Contains("VIB000", selfOut);

                string srcPath = Path.Combine(scratchDir, $"BadConst{i}.cs");
                File.WriteAllText(srcPath, invalidConstCases[i]);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"BadConst{i}");
                var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(outcome.Success, $"Invalid const case #{i} unexpectedly succeeded in Stage-0");
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round5TrackA_UsingDirectivesAmbiguity_CS0104_4WayParity()
    {
        string[] ambiguousUsingCases =
        [
            // Compilation-unit `using Ns1; using Ns2;` ambiguity
            """
            using Ns1;
            using Ns2;

            namespace Ns1 { public struct Packet { public int A; } }
            namespace Ns2 { public struct Packet { public int B; } }

            static class Program
            {
                public static void Main()
                {
                    Packet p = new Packet();
                    WriteLine(p.A);
                }
            }
            """,
            // Namespace-scoped `using Ns1; using Ns2;` ambiguity
            """
            namespace Ns1 { public struct Item { public int A; } }
            namespace Ns2 { public struct Item { public int B; } }

            namespace App
            {
                using Ns1;
                using Ns2;

                public static class Program
                {
                    public static void Main()
                    {
                        Item x = new Item();
                        WriteLine(x.A);
                    }
                }
            }
            """,
            // User `using MyStd;` colliding with implicit global `using Vibranium.Std;` (`StringBuf`)
            """
            using MyStd;

            namespace MyStd { public struct StringBuf { public int Dummy; } }

            static class Program
            {
                public static void Main()
                {
                    StringBuf b = new StringBuf();
                    WriteLine(1);
                }
            }
            """,
            // User `using MySys;` colliding with implicit global `using System;` (`IntPtr`)
            """
            using MySys;

            namespace MySys { public struct IntPtr { public int X; } }

            static class Program
            {
                public static void Main()
                {
                    IntPtr p = default;
                    WriteLine(1);
                }
            }
            """,
            // Unqualified `: IDisposable` ambiguous when `using MyDisp;` imports `MyDisp.IDisposable`
            """
            using MyDisp;

            namespace MyDisp { public sealed class IDisposable { } }

            sealed class Res : IDisposable
            {
                public void Dispose() { }
            }

            static class Program
            {
                public static void Main() => WriteLine(1);
            }
            """,
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r5a-amb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < ambiguousUsingCases.Length; i++)
            {
                var (selfExit, selfOut) = RunRound5VibSelfCheck(ambiguousUsingCases[i]);
                Assert.True(selfExit != 0, $"Ambiguous using case #{i} unexpectedly succeeded in vib-self");
                Assert.Contains("VIB000", selfOut);

                string srcPath = Path.Combine(scratchDir, $"Amb{i}.cs");
                File.WriteAllText(srcPath, ambiguousUsingCases[i]);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Amb{i}", includeStd: true);
                var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(outcome.Success, $"Ambiguous using case #{i} unexpectedly succeeded in Stage-0");
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }

        // Shadowing in current/global namespace, qualified names, and duplicate usings remain valid
        const string validShadowingAndQualifiedCode = """
            using Ns1;
            using Vibranium.Std;

            namespace Ns1
            {
                public readonly struct Widget(int v) { public readonly int V = v; }
            }

            namespace Ns2
            {
                public readonly struct Widget(int v) { public readonly int V = v * 10; }
            }

            namespace App
            {
                using Ns1;

                // Current-namespace type shadows imported Vibranium.Std.Bytes!
                public readonly struct Bytes(int code)
                {
                    public readonly int Code = code;
                }

                public static class Program
                {
                    public static void Main()
                    {
                        Bytes localShadow = new Bytes(77);
                        WriteLine(localShadow.Code);

                        Widget w1 = new Widget(5);
                        Ns2.Widget w2 = new Ns2.Widget(5);
                        WriteLine(w1.V);
                        WriteLine(w2.V);

                        WriteLine(global::Vibranium.Std.Bytes.Eq("ok"u8, "ok"u8));
                    }
                }
            }
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(validShadowingAndQualifiedCode, "77\n5\n50\nTrue\n");
      }

    private static async Task VerifyRound6TrackAFourWayDifferentialBytesAsync(byte[] utf8SourceBytes, string expectedStdout)
    {
        string vibSelfDll = ResolveRound5VibSelfDllPath();
        Assert.True(File.Exists(vibSelfDll), $"Expected built vib-self.dll at {vibSelfDll}");
        string sysroot = Path.Combine(RepoRoot, "src");

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-r6a-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            File.WriteAllBytes(srcPath, utf8SourceBytes);

            // 1. Stage-0 Frontend + Analyzer zero-diagnostic check
            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round6TrackADiff", includeStd: true);
            var checkRes = VibraniumFrontend.CompileAndCheck(comp);
            Assert.Empty(checkRes.Diagnostics);

            var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(analyzerDiags);

            // 2. Stage-0 LLVM 18 native build (-O3 + ASan) & execution
            var stage0Ir = VibraniumCompilerDriver.CheckAndEmitIr(
                comp,
                new LlvmEmitOptions(SharedNoAlias: false, AddressSanitizer: true));
            Assert.True(stage0Ir.Success, string.Join("\n", stage0Ir.Errors));
            Assert.NotNull(stage0Ir.LlvmIr);

            string stage0Bin = Path.Combine(tempDir, "stage0_native");
            var (clangExit, clangOut) = Llvm18Backend.CompileToExecutable(stage0Ir.LlvmIr!, stage0Bin, optLevel: 3, addressSanitizer: true);
            Assert.True(clangExit == 0, clangOut);
            var (s0Exit, s0Out, s0Err) = RunProcess(stage0Bin);

            // 3. Managed .NET 11 build & execution
            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = comp.SyntaxTrees.ToList();
            if (File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedComp = CSharpCompilation.Create(
                "Round6TrackAManaged",
                managedTrees,
                comp.References,
                comp.Options);

            string dllPath = Path.Combine(tempDir, "Round6TrackAManaged.dll");
            var emitRes = managedComp.Emit(dllPath);
            Assert.True(emitRes.Success, string.Join("\n", emitRes.Diagnostics));

            File.Copy(typeof(Vibranium.Option<>).Assembly.Location, Path.Combine(tempDir, "Vibranium.Core.dll"), overwrite: true);
            File.Copy(typeof(Vibranium.Std.StringBuf).Assembly.Location, Path.Combine(tempDir, "Vibranium.Std.dll"), overwrite: true);
            const string runtimeConfig = """
                {
                  "runtimeOptions": {
                    "tfm": "net11.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "11.0.0"
                    },
                    "configProperties": {
                      "System.Globalization.Invariant": true
                    }
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "Round6TrackAManaged.runtimeconfig.json"), runtimeConfig);
            var (mExit, mOut, mErr) = RunProcess(DotnetExe, dllPath);

            // 4. Self-hosted vib-self emit-ll, opt-18 verify, native --asan build & execution
            string selfLlPath = Path.Combine(tempDir, "self.ll");
            var (selfEmitExit, selfEmitOut, selfEmitErr) = RunProcess(
                DotnetExe, vibSelfDll, "emit-ll", srcPath, "--sysroot", sysroot, "-o", selfLlPath);
            Assert.True(selfEmitExit == 0, $"vib-self emit-ll failed:\n{selfEmitOut}\n{selfEmitErr}");

            var (optExit, _, optErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", selfLlPath);
            Assert.True(optExit == 0, $"opt-18 verify failed:\n{optErr}");

            string selfBin = Path.Combine(tempDir, "self_native");
            var (selfBuildExit, selfBuildOut, selfBuildErr) = RunProcess(
                DotnetExe, vibSelfDll, "build", srcPath, "--sysroot", sysroot, "--cc", "clang-18", "--asan", "-o", selfBin);
            Assert.True(selfBuildExit == 0, $"vib-self build failed:\n{selfBuildOut}\n{selfBuildErr}");
            var (selfRunExit, selfRunOut, selfRunErr) = RunProcess(selfBin);

            Assert.True(mExit == 0, $"Managed failed ({mExit}):\n{mErr}");
            Assert.True(s0Exit == 0, $"Stage-0 native failed ({s0Exit}):\n{s0Err}");
            Assert.True(selfRunExit == 0, $"vib-self native failed ({selfRunExit}):\n{selfRunErr}");

            string normExpected = expectedStdout.Replace("\r\n", "\n");
            string normManaged = mOut.Replace("\r\n", "\n");
            string normStage0 = s0Out.Replace("\r\n", "\n");
            string normSelf = selfRunOut.Replace("\r\n", "\n");

            Assert.Equal(normExpected, normManaged);
            Assert.Equal(normManaged, normStage0);
            Assert.Equal(normManaged, normSelf);
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
    public void Round6TrackA_DocsResearch_ZeroPrivateUseUnicodeCharactersInRepo()
    {
        string astraPath = Path.Combine(RepoRoot, "docs", "research", "02-astra-initial-report.md");
        Assert.True(File.Exists(astraPath), $"Expected research report at {astraPath}");

        string astraText = File.ReadAllText(astraPath, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        Assert.NotEmpty(astraText);

        string[] checkedExtensions = [".md", ".cs", ".ll", ".props", ".targets", ".yml", ".yaml", ".sh", ".lean", ".json"];
        var violations = new List<string>();

        foreach (string file in Directory.EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoRoot, file);
            if (rel.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                rel.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                rel.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                rel.Contains(Path.DirectorySeparatorChar + ".lake" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            string ext = Path.GetExtension(file);
            if (!checkedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch >= '\uE000' && ch <= '\uF8FF')
                {
                    violations.Add($"{rel} (offset {i}, U+{(int)ch:X4})");
                    break;
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public async Task Round6TrackA_ExplicitInterfaceDispose_RejectedWithVib000_4WayParity()
    {
        string[] rejectedCases =
        [
            // Explicit IDisposable.Dispose() only, used in `using`
            """
            using Bad b = new Bad();
            WriteLine(b.X);

            sealed class Bad : IDisposable
            {
                public int X;
                void IDisposable.Dispose() { }
            }
            """,
            // Both explicit IDisposable.Dispose() and public void Dispose()
            """
            using Dual d = new Dual();
            WriteLine(d.X);

            sealed class Dual : IDisposable
            {
                public int X;
                void IDisposable.Dispose() { WriteLine(1); }
                public void Dispose() { WriteLine(2); }
            }
            """,
            // Explicit IDisposable.Dispose() without `using`
            """
            WriteLine(0);

            sealed class UnusedBad : IDisposable
            {
                public int X;
                void IDisposable.Dispose() { }
            }
            """,
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r6a-explicit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < rejectedCases.Length; i++)
            {
                string srcPath = Path.Combine(scratchDir, $"Explicit{i}.cs");
                File.WriteAllText(srcPath, rejectedCases[i]);

                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Round6Explicit{i}");
                var feRes = VibraniumFrontend.CompileAndCheck(comp);
                var feIds = feRes.Diagnostics.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(["VIB000"], feIds);

                var irOutcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(irOutcome.Success, $"Explicit interface case #{i} unexpectedly emitted IR");
                Assert.Null(irOutcome.LlvmIr);

                var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .ToList();
                var anIds = analyzerDiags.Select(d => d.Id).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(["VIB000"], anIds);

                var (selfExit, selfOut) = RunRound5VibSelfCheck(rejectedCases[i]);
                Assert.NotEqual(0, selfExit);
                Assert.Contains("VIB000", selfOut);
                Assert.DoesNotContain("VIB006", selfOut);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(tempDirPath(scratchDir), recursive: true);
            }
        }

        static string tempDirPath(string p) => p;
    }

    [Fact]
    public async Task Round6TrackA_NestedUnionMoveSwitch_4WayDifferentialAndVib005Parity()
    {
        const string validNestedUnionSwitchCode = """
            static class Program
            {
                static int Consume(Vec<int> xs) => xs[0];

                static int UnwrapNested(Option<Option<Vec<int>>> outer) =>
                    Move(outer) switch
                    {
                        Some<Option<Vec<int>>>(Some<Vec<int>>(var inner)) => Consume(Move(inner)),
                        _ => 0,
                    };

                static int UnwrapComplementaryOption(Option<Vec<int>> opt) =>
                    Move(opt) switch
                    {
                        Some<Vec<int>>(var xs) => Consume(Move(xs)),
                        NoneType => -1,
                    };

                static int UnwrapComplementaryResult(Result<Vec<int>, Vec<int>> res) =>
                    Move(res) switch
                    {
                        Ok<Vec<int>>(var ok) => Consume(Move(ok)),
                        Err<Vec<int>>(var err) => Consume(Move(err)) * 10,
                    };

                static int UnwrapNestedResultOption(Result<Option<Vec<int>>, Vec<int>> res) =>
                    Move(res) switch
                    {
                        Ok<Option<Vec<int>>>(Some<Vec<int>>(var inner)) => Consume(Move(inner)),
                        Err<Vec<int>>(var err) => Consume(Move(err)) * 100,
                        _ => 5,
                    };

                public static void Main()
                {
                    Vec<int> v1 = new();
                    v1.Add(99);
                    Option<Vec<int>> inner1 = Some(Move(v1));
                    Option<Vec<int>> innerNone = None;
                    Option<Option<Vec<int>>> outerNone = None;
                    WriteLine(UnwrapNested(Some(Move(inner1))));
                    WriteLine(UnwrapNested(Some(Move(innerNone))));
                    WriteLine(UnwrapNested(Move(outerNone)));

                    Vec<int> v2 = new();
                    v2.Add(42);
                    Option<Vec<int>> optNone = None;
                    WriteLine(UnwrapComplementaryOption(Some(Move(v2))));
                    WriteLine(UnwrapComplementaryOption(Move(optNone)));

                    Vec<int> v3 = new();
                    v3.Add(7);
                    Result<Vec<int>, Vec<int>> errRes = Err(Move(v3));
                    WriteLine(UnwrapComplementaryResult(Move(errRes)));

                    Vec<int> v4 = new();
                    v4.Add(123);
                    Option<Vec<int>> okInner = Some(Move(v4));
                    Result<Option<Vec<int>>, Vec<int>> nestedOk = Ok(Move(okInner));
                    Option<Vec<int>> okEmpty = None;
                    Result<Option<Vec<int>>, Vec<int>> nestedEmpty = Ok(Move(okEmpty));
                    WriteLine(UnwrapNestedResultOption(Move(nestedOk)));
                    WriteLine(UnwrapNestedResultOption(Move(nestedEmpty)));
                }
            }
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(
            validNestedUnionSwitchCode,
            "99\n0\n0\n42\n-1\n70\n123\n5\n");

        // Multi-arm unpacking of the same non-Copy outer variant must still emit VIB005 across all pipelines.
        const string invalidMultiUnpackCode = """
            Vec<int> v = new();
            v.Add(99);
            Option<Vec<int>> innerOpt = Some(Move(v));
            Option<Option<Vec<int>>> outer = Some(Move(innerOpt));

            int x = Move(outer) switch
            {
                Some<Option<Vec<int>>>(Some<Vec<int>>(var inner)) when inner.Count == 1 => Consume(Move(inner)),
                Some<Option<Vec<int>>>(NoneType) => 1,
                _ => 0,
            };
            WriteLine(x);

            static int Consume(Vec<int> xs) => xs[0];
            """;

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r6a-nestedbad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            string srcPath = Path.Combine(scratchDir, "NestedBad.cs");
            File.WriteAllText(srcPath, invalidMultiUnpackCode);

            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round6NestedBad");
            var feRes = VibraniumFrontend.CompileAndCheck(comp);
            Assert.Contains(feRes.Diagnostics, d => d.Id == "VIB005");

            var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Contains(analyzerDiags, d => d.Id == "VIB005");

            var (selfExit, selfOut) = RunRound5VibSelfCheck(invalidMultiUnpackCode);
            Assert.NotEqual(0, selfExit);
            Assert.Contains("VIB005", selfOut);
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round6TrackA_LexerUnicodeFormatIdentifiersAndEscapes_4WayParity()
    {
        byte[] validIdentBytes = System.Text.Encoding.UTF8.GetBytes(
            "int a\u200Bb = 20;\n" +
            "int \\u0061\\U00000062c = 22;\n" +
            "int @\\u0069nt = a\\u200Db + ab\u00ADc;\n" +
            "int v\u200Car = 5;\n" +
            "#region Unicode identifiers\n" +
            "WriteLine(@int + i\u200Bnt - \\u0076\\uFEFFar);\n" +
            "#endregion\n");

        await VerifyRound6TrackAFourWayDifferentialBytesAsync(validIdentBytes, "79\n");

        string[] invalidIdentCases =
        [
            "int \u200B = 1;\nWriteLine(0);\n",
            "int \u200Bx = 1;\nWriteLine(x);\n",
            "int \\u200B = 1;\nWriteLine(0);\n",
            "\\u0069nt x = 1;\nWriteLine(x);\n",
            "int a\\U00010400 = 1;\nWriteLine(0);\n",
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r6a-identbad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < invalidIdentCases.Length; i++)
            {
                byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(invalidIdentCases[i]);
                var (selfExit, selfOut) = RunRound5VibSelfCheckBytes(utf8);
                Assert.NotEqual(0, selfExit);
                Assert.Contains("VIB000", selfOut);

                string srcPath = Path.Combine(scratchDir, $"BadIdent{i}.cs");
                File.WriteAllBytes(srcPath, utf8);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"BadIdent{i}");
                var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(outcome.Success, $"Invalid identifier case #{i} unexpectedly succeeded in Stage-0");
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round6TrackA_LexerUnicodeLineTerminatorsAndWhitespace_4WayParity()
    {
        byte[] validLineTermAndWsBytes = System.Text.Encoding.UTF8.GetBytes(
            "\uFEFF#region CR_OK\r" +
            "#region NEL_OK\u0085" +
            "#endregion\u2028" +
            "int\u00A0x\u2003=\u300010;\r" +
            "// CR comment ends at \\r so next statement executes\rx = x + 5;\n" +
            "// NEL comment ends at U+0085\u0085x = x + 6;\n" +
            "// LS comment ends at U+2028\u2028x = x + 7;\n" +
            "// PS comment ends at U+2029\u2029x = x + 14;\r#endregion\r" +
            "WriteLine(x);\n");

        await VerifyRound6TrackAFourWayDifferentialBytesAsync(validLineTermAndWsBytes, "42\n");

        byte[][] invalidUnescapedNewlineInStringCases =
        [
            System.Text.Encoding.UTF8.GetBytes("string s = \"abc\rdef\";\nWriteLine(s);\n"),
            System.Text.Encoding.UTF8.GetBytes("string s = \"abc\u2028def\";\nWriteLine(s);\n"),
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r6a-ltbad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < invalidUnescapedNewlineInStringCases.Length; i++)
            {
                var (selfExit, selfOut) = RunRound5VibSelfCheckBytes(invalidUnescapedNewlineInStringCases[i]);
                Assert.NotEqual(0, selfExit);
                Assert.Contains("VIB000", selfOut);

                string srcPath = Path.Combine(scratchDir, $"BadLt{i}.cs");
                File.WriteAllBytes(srcPath, invalidUnescapedNewlineInStringCases[i]);
                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"BadLt{i}");
                var outcome = VibraniumCompilerDriver.CheckAndEmitIr(comp);
                Assert.False(outcome.Success, $"Invalid newline-in-string case #{i} unexpectedly succeeded in Stage-0");
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round6TrackA_LexerRawStringPreservesExactLineEndings_4WayParity()
    {
        byte[] rawStringCrlfVsLfBytes = System.Text.Encoding.UTF8.GetBytes(
            "string crlf = \"\"\"\r\n" +
            "    ab\r\n" +
            "    cd\r\n" +
            "    \"\"\";\r\n" +
            "string lf = \"\"\"\n" +
            "    ab\n" +
            "    cd\n" +
            "    \"\"\";\n" +
            "ReadOnlySpan<byte> u8Crlf = \"\"\"\r\n" +
            "    hi\r\n" +
            "    bye\r\n" +
            "    \"\"\"u8;\r\n" +
            "string emptyCrlf = \"\"\"\r\n\r\n    \"\"\";\r\n" +
            "ReadOnlySpan<byte> b1 = crlf.AsUtf8();\n" +
            "ReadOnlySpan<byte> b2 = lf.AsUtf8();\n" +
            "WriteLine(b1.Length);\n" +
            "WriteLine((int)b1[2]);\n" +
            "WriteLine((int)b1[3]);\n" +
            "WriteLine(b2.Length);\n" +
            "WriteLine((int)b2[2]);\n" +
            "WriteLine(u8Crlf.Length);\n" +
            "WriteLine((int)u8Crlf[2]);\n" +
            "WriteLine((int)u8Crlf[3]);\n" +
            "WriteLine(emptyCrlf.AsUtf8().Length);\n");

        await VerifyRound6TrackAFourWayDifferentialBytesAsync(
            rawStringCrlfVsLfBytes,
            "6\n13\n10\n5\n10\n7\n13\n10\n0\n");
    }

    [Fact]
    public async Task Round6TrackA_LexerStringSurrogatePairsAndLoneSurrogates_4WayParity()
    {
        const string validSurrogatePairCode = """
            ReadOnlySpan<byte> a = "\uD83D\uDE00".AsUtf8();
            ReadOnlySpan<byte> b = "\xD83D\xDE00".AsUtf8();
            ReadOnlySpan<byte> c = "\uD83D\xDE00".AsUtf8();
            ReadOnlySpan<byte> d = "\xD83D\uDE00".AsUtf8();
            ReadOnlySpan<byte> e = "\U0000D83D\U0000DE00".AsUtf8();
            ReadOnlySpan<byte> f = "\uD83D\uDE00"u8;

            if (a.SequenceEqual(b) &&
                a.SequenceEqual(c) &&
                a.SequenceEqual(d) &&
                a.SequenceEqual(e) &&
                a.SequenceEqual(f) &&
                a.Length == 4)
            {
                WriteLine((int)a[0]);
                WriteLine((int)a[1]);
                WriteLine((int)a[2]);
                WriteLine((int)a[3]);
            }
            """;

        await VerifyRound5TrackAFourWayDifferentialAsync(
            validSurrogatePairCode,
            "240\n159\n152\n128\n");

        string[] loneSurrogateCases =
        [
            "string s = \"\\uD83D\";\nWriteLine(s);\n",
            "string s = \"\\uDE00\";\nWriteLine(s);\n",
            "string s = \"\\uD83Da\";\nWriteLine(s);\n",
            "string s = \"\\xD800\";\nWriteLine(s);\n",
            "char c = '\\uD83D';\nWriteLine((int)c);\n",
        ];

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r6a-surrbad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            for (int i = 0; i < loneSurrogateCases.Length; i++)
            {
                string srcPath = Path.Combine(scratchDir, $"LoneSurr{i}.cs");
                File.WriteAllText(srcPath, loneSurrogateCases[i]);

                var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"LoneSurr{i}");
                var feRes = VibraniumFrontend.CompileAndCheck(comp);
                Assert.Contains(feRes.Diagnostics, d => d.Id == "VIB000");

                var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                    .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                    .ToList();
                Assert.Contains(analyzerDiags, d => d.Id == "VIB000");

                var (selfExit, selfOut) = RunRound5VibSelfCheck(loneSurrogateCases[i]);
                Assert.NotEqual(0, selfExit);
                Assert.Contains("VIB000", selfOut);
            }
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Round7TrackA_NanConstantPatterns_And_ExhaustiveSwitch_Differential()
    {
        const string code = """
            class EvalCounter(int init)
            {
                public int Calls = init;

                [Mut]
                public double YieldDouble(double v)
                {
                    Calls += 1;
                    return v;
                }

                [Mut]
                public float YieldFloat(float v)
                {
                    Calls += 1;
                    return v;
                }
            }

            static class NanPatternSuite
            {
                const double CustomDoubleNan = 0.0 / 0.0;
                const float CustomFloatNan = 0.0f / 0.0f;

                static double DynamicZero(int seed) => seed == 999 ? 1.0 : 0.0;

                static double MakeDoubleSample(int kind)
                {
                    double z = DynamicZero(kind);
                    return kind switch
                    {
                        0 => -z,             // -0.0
                        1 => z,              // +0.0
                        2 => z - 1.5,        // -1.5
                        3 => z + 2.5,        // +2.5
                        4 => -1.0 / z,       // -Infinity
                        5 => 1.0 / z,        // +Infinity
                        6 => z / z,          // NaN
                        _ => -(z / z),       // -NaN
                    };
                }

                // 4. Exhaustive double & float switch expressions
                static int ClassifyDoubleExpr(double x) => x switch
                {
                    double.NaN => 3,
                    var value when value < 0 => 1,
                    _ => 2,
                };

                static int ClassifyFloatExpr(float x) => x switch
                {
                    float.NaN => 3,
                    var value when value < 0f => 1,
                    _ => 2,
                };

                static int NonExhaustiveDoubleExpr(double x) => x switch
                {
                    double.NaN => 10,
                    0.0 => 20,
                    _ => 30,
                };

                static int NonExhaustiveFloatExpr(float x) => x switch
                {
                    float.NaN => 10,
                    0.0f => 20,
                    _ => 30,
                };

                // 5a. Exhaustive double & float switch statements
                static int ClassifyDoubleStmt(double d)
                {
                    switch (d)
                    {
                        case double.NaN:
                            return 3;
                        case var value when value < 0:
                            return 1;
                        default:
                            return 2;
                    }
                }

                static int ClassifyFloatStmt(float f)
                {
                    switch (f)
                    {
                        case float.NaN:
                            return 3;
                        case var value when value < 0f:
                            return 1;
                        default:
                            return 2;
                    }
                }

                // 5b. Traditional single-value case switch statements (ISingleValueCaseClauseOperation)
                static int SingleCaseDoubleStmt(double d)
                {
                    switch (d)
                    {
                        case double.NaN:
                            return 11;
                        default:
                            return 22;
                    }
                }

                static int MultiCaseDoubleStmt(double d)
                {
                    switch (d)
                    {
                        case 0.0:
                            return 100;
                        case double.NaN:
                            return 200;
                        default:
                            return 300;
                    }
                }

                static int SingleCaseFloatStmt(float f)
                {
                    switch (f)
                    {
                        case float.NaN:
                            return 11;
                        default:
                            return 22;
                    }
                }

                static int GuardedDoubleStmt(double d, bool allow)
                {
                    switch (d)
                    {
                        case double.NaN when allow:
                            return 77;
                        default:
                            return 88;
                    }
                }

                // 3. Exhaustive & non-exhaustive nested union subpatterns
                static int ClassifyOptionDouble(Option<double> od) => od switch
                {
                    Some<double>(double.NaN) => 3,
                    Some<double>(var value) => value < 0 ? 1 : 2,
                    NoneType => 4,
                };

                static int MatchOptionDoubleNan(Option<double> od) => od switch
                {
                    Some<double>(double.NaN) => 1,
                    _ => 2,
                };

                static int MatchOptionFloatNan(Option<float> of) => of switch
                {
                    Some<float>(float.NaN) => 1,
                    _ => 2,
                };

                static int MatchResultDoubleNan(Result<double, int> rd) => rd switch
                {
                    Ok<double>(double.NaN) => 1,
                    _ => 2,
                };

                static void TestValue(int kind, ref EvalCounter counter)
                {
                    double d = MakeDoubleSample(kind);
                    float f = (float)d;

                    WriteLine(kind);

                    // 1. Direct is / is not constant patterns (+ IEEE == / != non-regression)
                    WriteLine(d is double.NaN);
                    WriteLine(d is not double.NaN);
                    WriteLine((float)d is float.NaN);
                    WriteLine(f is float.NaN);
                    WriteLine(f is not float.NaN);
                    WriteLine(d is CustomDoubleNan);
                    WriteLine(f is CustomFloatNan);
                    if (d is double.NaN)
                    {
                        WriteLine(101);
                    }
                    else
                    {
                        WriteLine(102);
                    }
                    if (d is not double.NaN)
                    {
                        WriteLine(103);
                    }
                    else
                    {
                        WriteLine(104);
                    }
                    WriteLine(d == double.NaN);
                    WriteLine(d != double.NaN);

                    // Single-evaluation check on side-effecting scrutinees
                    int before = counter.Calls;
                    bool se1 = counter.YieldDouble(d) is double.NaN;
                    double yieldedDouble = counter.YieldDouble(d);
                    bool se2 = yieldedDouble < 0 || yieldedDouble is double.NaN;
                    float yieldedFloat = counter.YieldFloat(f);
                    bool se3 = yieldedFloat < 0f || yieldedFloat is float.NaN;
                    WriteLine(se1);
                    WriteLine(se2);
                    WriteLine(se3);
                    WriteLine(counter.Calls - before);

                    // 2. Combinators
                    WriteLine(d < 0 || d is double.NaN);
                    WriteLine(!(d < 0 || d >= 0));
                    WriteLine(f < 0f || f is float.NaN);
                    WriteLine(!(f < 0f || f >= 0f));

                    // 3. Nested union subpatterns
                    Option<double> od = Some(d);
                    Option<float> of = Some(f);
                    Result<double, int> rd = Ok(d);
                    WriteLine(od is Some<double>(double.NaN));
                    WriteLine(od is Some<double>(not double.NaN));
                    WriteLine(od is Some<double>(var innerDouble) && (innerDouble < 0 || innerDouble is double.NaN));
                    WriteLine(of is Some<float>(float.NaN));
                    WriteLine(rd is Ok<double>(double.NaN));
                    WriteLine(MatchOptionDoubleNan(od));
                    WriteLine(MatchOptionFloatNan(of));
                    WriteLine(MatchResultDoubleNan(rd));
                    WriteLine(ClassifyOptionDouble(od));

                    // 4. Exhaustive & non-exhaustive switch expressions
                    WriteLine(ClassifyDoubleExpr(d));
                    WriteLine(ClassifyFloatExpr(f));
                    WriteLine(NonExhaustiveDoubleExpr(d));
                    WriteLine(NonExhaustiveFloatExpr(f));

                    // 5. Switch statements
                    WriteLine(ClassifyDoubleStmt(d));
                    WriteLine(ClassifyFloatStmt(f));
                    WriteLine(SingleCaseDoubleStmt(d));
                    WriteLine(MultiCaseDoubleStmt(d));
                    WriteLine(SingleCaseFloatStmt(f));
                    WriteLine(GuardedDoubleStmt(d, true));
                    WriteLine(GuardedDoubleStmt(d, false));

                    // Side-effecting scrutinees in switches + Err<double>(double.NaN) + OptionRef<double>
                    int swBefore = counter.Calls;
                    int sw1 = counter.YieldDouble(d) switch
                    {
                        var value when value < 0 => 1,
                        var value when value >= 0 => 2,
                        _ => 3, // NaN is the only remaining floating value.
                    };
                    int sw2 = SingleCaseDoubleStmt(counter.YieldDouble(d));
                    WriteLine(sw1);
                    WriteLine(sw2);
                    WriteLine(counter.Calls - swBefore);

                    int swInlineBefore = counter.Calls;
                    int swInline;
                    switch (counter.YieldDouble(d))
                    {
                        case -1.5:
                        case double.NaN:
                            swInline = 55;
                            break;
                        default:
                            swInline = 66;
                            break;
                    }
                    int swImplicitFloatNan;
                    switch (d)
                    {
                        case float.NaN:
                            swImplicitFloatNan = 71;
                            break;
                        default:
                            swImplicitFloatNan = 72;
                            break;
                    }
                    WriteLine(swInline);
                    WriteLine(counter.Calls - swInlineBefore);
                    WriteLine(swImplicitFloatNan);
                    WriteLine(d is float.NaN);

                    Result<int, double> rdErr = Err(d);
                    WriteLine(rdErr is Err<double>(double.NaN));
                    WriteLine(od.AsRef() is Some<double>(double.NaN));
                    WriteLine(od is not Some<double>(double.NaN));
                    WriteLine(od is (Some<double>(double.NaN) or NoneType));
                    WriteLine(od is Some<double>(double.NaN));
                }

                static int Main()
                {
                    var counter = new EvalCounter(0);
                    for (int k = 0; k < 8; k++)
                    {
                        TestValue(k, ref counter);
                    }

                    Option<double> emptyOpt = None;
                    WriteLine(emptyOpt is Some<double>(double.NaN));
                    WriteLine(emptyOpt is not Some<double>(double.NaN));
                    WriteLine(emptyOpt is (Some<double>(double.NaN) or NoneType));
                    WriteLine(MatchOptionDoubleNan(emptyOpt));
                    WriteLine(ClassifyOptionDouble(emptyOpt));
                    WriteLine(counter.Calls);
                    return 0;
                }
            }
            """;

        string scratchDir = Path.Combine(Path.GetTempPath(), $"vibranium-r7a-nan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        try
        {
            string srcPath = Path.Combine(scratchDir, "Program.cs");
            File.WriteAllText(srcPath, code);

            var comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "Round7TrackANan");
            var checkRes = VibraniumFrontend.CompileAndCheck(comp);
            Assert.Empty(checkRes.Diagnostics);

            var analyzerDiags = (await comp.WithAnalyzers([new VibraniumDiagnosticAnalyzer()]).GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Id.StartsWith("VIB", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(analyzerDiags);
        }
        finally
        {
            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }

        var resAsan = CompileAndRunBoth(code, withAsan: true);
        Assert.Equal(0, resAsan.ManagedExit);
        Assert.Equal(0, resAsan.NativeExit);
        Assert.Equal(string.Empty, resAsan.NativeErr);
        Assert.Equal(resAsan.ManagedOut, resAsan.NativeOut);

        string llTempPath = Path.Combine(Path.GetTempPath(), $"vibranium-r7a-verify-{Guid.NewGuid():N}.ll");
        try
        {
            File.WriteAllText(llTempPath, resAsan.LlvmIr);
            var (optExit, _, optErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llTempPath);
            Assert.True(optExit == 0, $"opt-18 verify failed:\n{optErr}");
        }
        finally
        {
            if (File.Exists(llTempPath))
            {
                File.Delete(llTempPath);
            }
        }

        var resPureO3 = CompileAndRunBoth(code, withAsan: false, sharedNoAlias: true);
        Assert.Equal(0, resPureO3.NativeExit);
        Assert.Equal(string.Empty, resPureO3.NativeErr);
        Assert.Equal(resAsan.ManagedOut, resPureO3.NativeOut);
    }
}
