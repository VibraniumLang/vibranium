using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed class VibraniumSelfHostTests
{
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

    private static string DotnetExe
    {
        get
        {
            string homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
            return File.Exists(homeDotnet) ? homeDotnet : "dotnet";
        }
    }

    private static string ResolveActiveBuildConfiguration()
    {
#if DEBUG
        const string preferred = "Debug";
        const string fallback = "Release";
#else
        const string preferred = "Release";
        const string fallback = "Debug";
#endif
        string prefSelf = Path.Combine(RepoRoot, "src", "vib-self", "bin", preferred, "net11.0", "vib-self.dll");
        string prefC0 = Path.Combine(RepoRoot, "src", "vib", "bin", preferred, "net11.0", "vib.dll");
        if (File.Exists(prefSelf) && File.Exists(prefC0))
        {
            return preferred;
        }

        string fbSelf = Path.Combine(RepoRoot, "src", "vib-self", "bin", fallback, "net11.0", "vib-self.dll");
        string fbC0 = Path.Combine(RepoRoot, "src", "vib", "bin", fallback, "net11.0", "vib.dll");
        if (File.Exists(fbSelf) && File.Exists(fbC0))
        {
            return fallback;
        }

        return preferred;
    }

    private static string VibSelfDllPath =>
        Path.Combine(RepoRoot, "src", "vib-self", "bin", ResolveActiveBuildConfiguration(), "net11.0", "vib-self.dll");

    private static string SysrootPath => Path.Combine(RepoRoot, "src");

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

    private static (int ExitCode, string Stdout, string Stderr) RunProcessWithInput(
        string? stdinText,
        string fileName,
        params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = stdinText is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        if (stdinText is not null)
        {
            proc.StandardInput.Write(stdinText);
            proc.StandardInput.Close();
        }
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    private static (int ManagedExit, string ManagedOut, string ManagedErr, int NativeExit, string NativeOut, string NativeErr)
        CompileAndRunSelfHostDifferential(
            string sourceCode,
            bool withAsan = true,
            string? stdinText = null,
            string[]? appArgs = null)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(srcPath, sourceCode);

            // 1. Managed .NET 11 reference build & execution
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, "SelfDiffManaged", includeStd: true);
            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = compilation.SyntaxTrees.ToList();
            if (File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedComp = CSharpCompilation.Create(
                "SelfDiffManaged",
                managedTrees,
                compilation.References,
                compilation.Options);

            string dllPath = Path.Combine(tempDir, "SelfDiffManaged.dll");
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
            File.WriteAllText(Path.Combine(tempDir, "SelfDiffManaged.runtimeconfig.json"), runtimeConfig);

            // 2. Self-hosted vib-self IR emission, opt-18 verification, and native build
            string llPath = Path.Combine(tempDir, "out.ll");
            var (emitExit, emitOut, emitErr) = RunProcess(DotnetExe, VibSelfDllPath, "emit-ll", srcPath, "--sysroot", SysrootPath, "-o", llPath);
            Assert.True(emitExit == 0, $"vib-self emit-ll failed:\n{emitOut}\n{emitErr}");

            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llPath);
            Assert.True(verExit == 0, $"opt-18 verify failed:\n{verErr}");

            string nativeBin = Path.Combine(tempDir, "self_native");
            var buildArgs = new List<string> { VibSelfDllPath, "build", srcPath, "--sysroot", SysrootPath, "--cc", "clang-18" };
            if (withAsan) buildArgs.Add("--asan");
            buildArgs.Add("-o");
            buildArgs.Add(nativeBin);
            var (buildExit, buildOut, buildErr) = RunProcess(DotnetExe, buildArgs.ToArray());
            Assert.True(buildExit == 0, $"vib-self build failed:\n{buildOut}\n{buildErr}");

            var mRunArgs = new List<string> { dllPath };
            var nRunArgs = new List<string>();
            if (appArgs is not null)
            {
                mRunArgs.AddRange(appArgs);
                nRunArgs.AddRange(appArgs);
            }

            var (mExit, mOut, mErr) = RunProcessWithInput(stdinText, DotnetExe, mRunArgs.ToArray());
            var (nExit, nOut, nErr) = RunProcessWithInput(stdinText, nativeBin, nRunArgs.ToArray());
            return (mExit, mOut, mErr, nExit, nOut, nErr);
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
    public void SelfHost_ShowcaseDemo_MatchesManagedByteForByte_And_AsanClean()
    {
        string demoCs = Path.Combine(RepoRoot, "samples", "demo", "Program.cs");
        string cfg = ResolveActiveBuildConfiguration();
        string demoDll = Path.Combine(RepoRoot, "samples", "demo", "bin", cfg, "net11.0", "Demo.dll");
        Assert.True(File.Exists(VibSelfDllPath), $"Expected built vib-self at {VibSelfDllPath}");
        Assert.True(File.Exists(demoDll), $"Expected built Demo.dll at {demoDll}");

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-demo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string llPath = Path.Combine(tempDir, "demo.ll");
            var (emitExit, _, emitErr) = RunProcess(DotnetExe, VibSelfDllPath, "emit-ll", demoCs, "--sysroot", SysrootPath, "-o", llPath);
            Assert.True(emitExit == 0, emitErr);

            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llPath);
            Assert.True(verExit == 0, verErr);

            string nativeBin = Path.Combine(tempDir, "demo-native");
            var (buildExit, _, buildErr) = RunProcess(DotnetExe, VibSelfDllPath, "build", demoCs, "--sysroot", SysrootPath, "--cc", "clang-18", "--asan", "-o", nativeBin);
            Assert.True(buildExit == 0, buildErr);

            var (mExit, mOut, mErr) = RunProcess(DotnetExe, demoDll);
            var (nExit, nOut, nErr) = RunProcess(nativeBin);

            Assert.Equal(0, mExit);
            Assert.Equal(0, nExit);
            Assert.Equal(string.Empty, mErr);
            Assert.Equal(string.Empty, nErr);
            Assert.Equal(mOut.Replace("\r\n", "\n"), nOut.Replace("\r\n", "\n"));
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
    public void SelfHost_StdAndOwnershipFeatures_VerifyOpt18_And_AsanClean()
    {
        const string source = """
            var sb = new StringBuf(32);
            sb.Append("vib:"u8);
            sb.AppendInt(42);

            var map = new StrMap<int>(8);
            map.Set(sb.AsUtf8(), 100);

            var vec = new Vec<int>(4);
            vec += 10;
            vec += 20;
            Span<int> sp = vec.AsSpan();
            sp[0] += 5;

            int lookup = map.Get("vib:42"u8).UnwrapOr(-1);
            int divOk = SafeDiv(lookup + sp[0] + sp[1], 5).UnwrapOr(-1);
            Console.WriteLine(divOk);

            using var guard = new Guard(9);

            static Result<int, ErrKind> SafeDiv(int a, int b) =>
                b == 0 ? Err(ErrKind.Zero) : Ok(a / b);

            enum ErrKind { Zero }

            class Guard(int code) : IDisposable
            {
                private readonly int _code = code;
                public void Dispose()
                {
                    Console.Write("done:");
                    Console.WriteLine(_code);
                }
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-std-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(srcPath, source);

            string llPath = Path.Combine(tempDir, "out.ll");
            var (emitExit, _, emitErr) = RunProcess(DotnetExe, VibSelfDllPath, "emit-ll", srcPath, "--sysroot", SysrootPath, "-o", llPath);
            Assert.True(emitExit == 0, emitErr);

            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llPath);
            Assert.True(verExit == 0, verErr);

            string binPath = Path.Combine(tempDir, "app");
            var (buildExit, _, buildErr) = RunProcess(DotnetExe, VibSelfDllPath, "build", srcPath, "--sysroot", SysrootPath, "--cc", "clang-18", "--asan", "-o", binPath);
            Assert.True(buildExit == 0, buildErr);

            var (runExit, runOut, runErr) = RunProcess(binPath);
            Assert.Equal(0, runExit);
            Assert.Equal(string.Empty, runErr);
            Assert.Equal("27\ndone:9\n", runOut.Replace("\r\n", "\n"));
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
    public void SelfHost_CapturedPrimaryConstructors_And_SubsetParity_VerifyOpt18_And_AsanClean()
    {
        const string source = """
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
            Vec<int> v = [10, 20, 30, 40, 50];
            Span<int> sp = v.AsSpan();
            Span<int> mid = sp.Slice(1, 3);
            mid[0] = 200;
            Span<int> tail = sp[2..^1];
            tail[0] = 300;
            v.Push(v.Count);
            v[0] = v[1] + v[2];
            Console.WriteLine(v[0] + v[^1]);
            int a = 2, b = a + 3;
            Console.WriteLine(Calc(a, 10, 3) + Calc(b, 100, 3));
            Vec<Option<Option<int>>> nested = new Vec<Option<Option<int>>>();
            nested.Push(Some<Option<int>>(Some((42 >> 1) + (64 >>> 2))));
            if (nested[0] is Some<Option<int>> (var innerOpt) && innerOpt is Some<int> (var shifted))
            {
                Console.WriteLine(shifted);
            }

            int idx = 0, acc = 0;
            do
            {
                idx += 1;
                int cls = idx switch
                {
                    1 or 2 => 10,
                    var cutValue0 when ((cutValue0 >= 3) && (cutValue0 <= 4)) => 20,
                    var z when z == 5 => 30,
                    _ => 0,
                };
                acc += cls;
            }
            while (idx < 5);
            double nan = double.NaN;
            bool constOk = (nan != nan) && byte.MaxValue == 255 && short.MinValue == -32768 && uint.MaxValue == 4294967295u;
            Option<int> wrappedAcc = Some(acc);
            switch (wrappedAcc)
            {
                case Some<int> (var total)when constOk && (total is var cutValue2) && (((cutValue2 > 80) && (cutValue2 < 100))):
                    Console.WriteLine(total);
                    break;
                default:
                    Console.WriteLine(-1);
                    break;
            }

            var pair = new Pair(7, 11);
            Select(ref pair, false) += 100;
            Console.WriteLine(pair.A + pair.B);
            static int Calc(int x, int factor, int bias) => x * factor + bias;
            [return: LifetimeOf("p")]
            static ref int Select(ref Pair p, bool first)
            {
                if (first)
                    return ref p.A;
                return ref p.B;
            }

            struct Pair
            {
                public int A;
                public int B;
                public Pair(int a, int b)
                {
                    A = a;
                    B = b;
                }
            }

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
                    foreach (var x in items)
                        total += x;
                    return total;
                }
            }

            class ValueBox<T>(T value)
                where T : unmanaged
            {
                public T Get() => value;
            }

            class OptionBox<T>(Option<T> opt)
            {
                public bool HasValue() => opt.IsSome;
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-pctor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(srcPath, source);

            string llPath = Path.Combine(tempDir, "out.ll");
            var (emitExit, _, emitErr) = RunProcess(DotnetExe, VibSelfDllPath, "emit-ll", srcPath, "--sysroot", SysrootPath, "-o", llPath);
            Assert.True(emitExit == 0, emitErr);

            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llPath);
            Assert.True(verExit == 0, verErr);

            string binPath = Path.Combine(tempDir, "app");
            var (buildExit, _, buildErr) = RunProcess(DotnetExe, VibSelfDllPath, "build", srcPath, "--sysroot", SysrootPath, "--cc", "clang-18", "--asan", "-o", binPath);
            Assert.True(buildExit == 0, buildErr);

            var (runExit, runOut, runErr) = RunProcess(binPath);
            Assert.Equal(0, runExit);
            Assert.Equal(string.Empty, runErr);
            Assert.Equal("82\n42\n42\n3\n100\n99\nTrue\n505\n526\n37\n90\n118\n", runOut.Replace("\r\n", "\n"));
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
    public void SelfHost_SharedBorrows_OmitNoAlias_WhileExclusiveBorrowsRetainNoAlias()
    {
        const string source = """
            var bx = new Box(5);
            Vec<int> v = [10];
            Vec<int> w = [0];
            Option<Box> ob = Some(new Box(3));
            Console.WriteLine(CheckShared(v.AsReadOnlySpan(), ob.AsRef(), in bx, w.AsSpan()));

            static int CheckShared(ReadOnlySpan<int> rs, OptionRef<Box> orf, in Box ib, Span<int> ms)
            {
                ms[0] = rs[0] + ib.Read();
                if (orf is Some<Box>(var b)) return ms[0] + b.V;
                return ms[0];
            }

            class Box(int v)
            {
                public int V = v;
                public int Read() => V;
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-noalias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Program.cs");
            string llPath = Path.Combine(tempDir, "out.ll");
            File.WriteAllText(srcPath, source);

            var (emitExit, _, emitErr) = RunProcess(DotnetExe, VibSelfDllPath, "emit-ll", srcPath, "--sysroot", SysrootPath, "-o", llPath);
            Assert.True(emitExit == 0, emitErr);

            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llPath);
            Assert.True(verExit == 0, verErr);

            string ir = File.ReadAllText(llPath);
            string? checkSharedSig = null;
            string? boxReadSig = null;
            foreach (string line in ir.Split('\n'))
            {
                if (line.StartsWith("define ", StringComparison.Ordinal) && line.Contains("CheckShared", StringComparison.Ordinal))
                {
                    checkSharedSig = line;
                }
                else if (line.StartsWith("define ", StringComparison.Ordinal) && line.Contains("Box_Read", StringComparison.Ordinal))
                {
                    boxReadSig = line;
                }
            }

            Assert.NotNull(checkSharedSig);
            Assert.NotNull(boxReadSig);

            string paramList = checkSharedSig!.Split('(')[1].Split(')')[0];
            string[] parts = paramList.Split(',');
            Assert.Equal(6, parts.Length);

            // Shared borrows (`ReadOnlySpan<T>`, `OptionRef<T>`, `in T`, `ThisShared`) must NOT carry `noalias`
            Assert.DoesNotContain("noalias", parts[0]);
            Assert.Contains("readonly", parts[0]);
            Assert.DoesNotContain("noalias", parts[2]);
            Assert.Contains("readonly", parts[2]);
            Assert.DoesNotContain("noalias", parts[3]);
            Assert.Contains("readonly", parts[3]);
            Assert.DoesNotContain("noalias", boxReadSig!);
            Assert.Contains("readonly", boxReadSig!);

            // Exclusive `Span<T>` retains `noalias`
            Assert.Contains("noalias", parts[4]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    public static IEnumerable<object[]> SelfHostNegativeCases()
    {
        yield return new object[]
        {
            "VIB000",
            """
            int[] bad = new int[3];
            return bad.Length;
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            MissingType.Nonexistent();
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Span<int> s = [1, 2, 3];
            s.Fill(99);
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            _ = Open().GetHashCode();
            static Lease Open() => new Lease();
            class Lease : IDisposable { public void Dispose() {} }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            static class Spoof
            {
                [Native("__vib_sys_close")]
                public static partial void Close(int h);
                static int Main() => 0;
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            namespace Vibranium.Std;
            public static partial class Sys
            {
                [Native("puts")]
                public static partial int Puts(int x);
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
                        Sys.Close(0);
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Console.WriteLine(Add(10));
            static int Add(int a, int b) => a + b;
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            struct S { public int X; }
            int y = 1;
            Console.WriteLine(y);
            """,
        };
        yield return new object[]
        {
            "VIB001",
            """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = a;
            Console.WriteLine(a.Count);
            Console.WriteLine(b.Count);
            """,
        };
        yield return new object[]
        {
            "VIB002",
            """
            Vec<int> v = [1, 2];
            Overlap(ref v, in v);
            static void Overlap(ref Vec<int> m, in Vec<int> s) => m.Push(s.Count);
            """,
        };
        yield return new object[]
        {
            "VIB002",
            """
            Span<int> s = [5, 6];
            Console.WriteLine(Sum2(s, s));
            static int Sum2(Span<int> a, Span<int> b) => a[0] + b[0];
            """,
        };
        yield return new object[]
        {
            "VIB002",
            """
            Span<int> s = [5, 6];
            Console.WriteLine(Mix(s, s));
            static int Mix(Span<int> a, ReadOnlySpan<int> b) => a[0] + b[0];
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            Vec<int> v = [1, 2, 3];
            ref var r = ref v[0];
            v.Push(4);
            Console.WriteLine(r);
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            var v = new Vec<int>();
            v.Push(1);
            Span<int> s = v.AsSpan();
            v.Push(2);
            Console.WriteLine(s[0]);
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            var v = new Vec<int>();
            v.Push(1);
            v.Push(2);
            Span<int> s = v.AsSpan();
            Span<int> sub = s.Slice(0, 1);
            s[0] = 9;
            Console.WriteLine(sub[0]);
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            Option<Vec<int>> opt = Some(new Vec<int>());
            if (opt is Some<Vec<int>>(var v))
            {
                opt = None;
                Console.WriteLine(v.Count);
            }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            Vec<int> v = [1, 2];
            v[v.Count - 1] = v.Pop().UnwrapOr(0);
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            var h = new Holder();
            h.V.Push(Swap(ref h));
            static int Swap(ref Holder h) { h = new Holder(); return 7; }
            class Holder { public Vec<int> V = new Vec<int>(); }
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            class Box(int v)
            {
                public int Value = v;
                public void BadMut() { Value += 1; }
            }
            static class App { static int Main() => 0; }
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            Option<Node> o = Some(new Node(1));
            if (o is Some<Node>(var n)) { n.V = 99; }
            class Node { public int V; public Node(int v) { V = v; } }
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            Option<Vec<int>> o = Some(new Vec<int>());
            if (o is Some<Vec<int>>(var v)) { v.Push(1); }
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            Option<Node> o = Some(new Node(1));
            var m = o.AsMut();
            if (m is Some<Node>(var n)) { n = new Node(2); }
            class Node { public int V; public Node(int v) { V = v; } }
            """,
        };
        yield return new object[]
        {
            "VIB005",
            """
            class Holder(Vec<int> v)
            {
                public Vec<int> Items = v;
            }
            static class App
            {
                static int Main()
                {
                    var h = new Holder([1, 2]);
                    var stolen = h.Items;
                    return stolen.Count;
                }
            }
            """,
        };
        yield return new object[]
        {
            "VIB005",
            """
            Option<Node> o = Some(new Node(1));
            if (o is Some<Node>(var n)) Eat(n);
            static void Eat(Node n) {}
            class Node { public int V; public Node(int v) { V = v; } }
            """,
        };
        yield return new object[]
        {
            "VIB005",
            """
            Option<Node> o = Some(new Node(1));
            int x = o switch { Some<Node>(var n) => Eat(n), _ => 0 };
            Console.WriteLine(x);
            static int Eat(Node n) => n.V;
            class Node { public int V; public Node(int v) { V = v; } }
            """,
        };
        yield return new object[]
        {
            "VIB006",
            """
            var r = new Res();
            return 0;
            class Res : IDisposable { public void Dispose() {} }
            """,
        };
        yield return new object[]
        {
            "VIB006",
            """
                        Result<File, StringBuf> r;
            """,
        };
        yield return new object[]
        {
            "VIB006",
            """
            _ = Open();
            static Lease Open() => new Lease();
            class Lease : IDisposable { public void Dispose() {} }
            """,
        };
        yield return new object[]
        {
            "VIB007",
            """
            Span<int> d = Escape();
            return d.Length;
            static Span<int> Escape()
            {
                Span<int> local = [1, 2, 3];
                return local;
            }
            """,
        };
        yield return new object[]
        {
            "VIB007",
            """
            Option<Node> h = None;
            _ = Bad(ref h);
            [return: LifetimeOf("head")]
            static OptionRef<Node> Bad(ref Option<Node> head)
            {
                scoped var slot = head.AsMut();
                if (slot is Some<Node>(var n)) return n.Next.AsRef();
                return head.AsRef();
            }
            class Node { public Option<Node> Next = None; }
            """,
        };
        yield return new object[]
        {
            "VIB008",
            """
            string? s = null;
            return 0;
            """,
        };
        yield return new object[]
        {
            "VIB008",
            """
            Console.WriteLine(Make<int>());
            static T Make<T>() where T : unmanaged => default;
            """,
        };
        // --- Tier-A overload resolution, extension binding, closed foreach views, Expect/UnwrapOrDefault ---
        yield return new object[]
        {
            "VIB000",
            """
            Console.WriteLine(Over.Amb(1, 2));
            static class Over
            {
                public static int Amb(int a, long b) => 1;
                public static int Amb(long a, int b) => 2;
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Console.WriteLine(Over.BothDef(1));
            static class Over
            {
                public static int BothDef(int a, int b = 10) => a + b;
                public static int BothDef(int a, int b = 10, int c = 20) => a + b + c;
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            OnlyString(123);
            static void OnlyString(string s) => Console.WriteLine(s);
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Console.WriteLine("raw"u8);
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            var lines = "a\nb".AsUtf8().Lines();
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            _ = "a\nb".AsUtf8().Lines();
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            "a\nb".AsUtf8().Lines();
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            using Vibranium.Std;
            var a = Env.Args().Expect("args");
            a.GetEnumerator();
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Vec<Utf8Lines> bad = new Vec<Utf8Lines>();
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            foreach (ref var line in "a\nb".AsUtf8().Lines())
            {
                WriteLine(line.Length);
            }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            using Vibranium.Std;
            var sb = new StringBuf();
            sb.Append("a\nb");
            foreach (var line in sb.Lines())
            {
                sb.Append("!");
                WriteLine(line.Length);
            }
            """,
        };
        yield return new object[]
        {
            "VIB003",
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
        };
        yield return new object[]
        {
            "VIB007",
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
        };
        yield return new object[]
        {
            "VIB006",
            """
            Option<Lease> opt = None;
            using var l = opt.UnwrapOrDefault();
            class Lease : IDisposable { public void Dispose() { } }
            """,
        };
        yield return new object[]
        {
            "VIB006",
            """
            Result<int, Lease> res = Ok(1);
            WriteLine(res.UnwrapOrDefault());
            class Lease : IDisposable { public void Dispose() { } }
            """,
        };
        yield return new object[]
        {
            "VIB008",
            """
            Option<Vec<int>> opt = None;
            var v = opt.UnwrapOrDefault();
            WriteLine(v.Count);
            """,
        };
        yield return new object[]
        {
            "VIB008",
            """
            Result<string, int> res = Err(1);
            var s = res.UnwrapOrDefault();
            WriteLine(s);
            """,
        };
        yield return new object[]
        {
            "VIB007",
            """
            using Vibranium.Std;
            WriteLine(EscapeBufSlice().Length);
            static ReadOnlySpan<byte> EscapeBufSlice()
            {
                var sb = new StringBuf();
                sb.Append("hello");
                return sb[1..^1];
            }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            using Vibranium.Std;
            var sb = new StringBuf();
            sb.Append("hello");
            ReadOnlySpan<byte> view = sb[1..^1];
            sb.Append("!");
            WriteLine(view.Length);
            """,
        };
        // --- 3 Mandatory Stage-0 Parity Fixes (Parity-A, Parity-B, Parity-C) ---
        yield return new object[]
        {
            "VIB003",
            """
            var b = new Box(1);
            b.V = ReplaceAndReturn(ref b);
            WriteLine(b.V);
            static int ReplaceAndReturn(ref Box slot)
            {
                slot = new Box(99);
                return 42;
            }
            class Box(int v) { public int V = v; }
            """,
        };
        yield return new object[]
        {
            "VIB008",
            """
            WriteLine(MakeNew<int>());
            static T MakeNew<T>() where T : unmanaged => new T();
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            int x = 10;
            TakeRefAndVal(ref x, BumpRef(ref x));
            static int BumpRef(ref int r) { r += 1; return r; }
            static void TakeRefAndVal(ref int r, int y) { r += y; }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            int x = 10;
            TakeInAndVal(in x, BumpRef(ref x));
            static int BumpRef(ref int r) { r += 1; return r; }
            static void TakeInAndVal(in int ro, int y) => WriteLine(ro + y);
            """,
        };
        // --- Round-2 Audit Fixes: P0-1, P1-2, P1-5, P1-6/7, Fable P1-6/P1-9, Stage-0 Parity, StringBuf += ---
        yield return new object[]
        {
            "VIB004",
            """
            var c = new Counter(5);
            BadMutProp(in c);
            static void BadMutProp(in Counter ro) { ro.Val += 1; }
            struct Counter(int v)
            {
                private int _v = v;
                public int Val { readonly get => _v; set => _v = value; }
            }
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            Option<ClassCounter> opt = Some(new ClassCounter(5));
            if (opt is Some<ClassCounter>(var ro))
            {
                WriteLine(ro.Val);
                ro.Val += 1;
            }
            sealed class ClassCounter(int v)
            {
                private int _v = v;
                [Mut] public int Val { get => _v; set => _v = value; }
            }
            """,
        };
        yield return new object[]
        {
            "VIB004",
            """
            var b = new Bag([1, 2]);
            BadMutIdx(in b);
            static void BadMutIdx(in Bag ro) { ro[0]++; }
            struct Bag(Vec<int> v)
            {
                private Vec<int> _v = v;
                [Mut] public int this[int i] { get => _v[i]; set => _v[i] = value; }
            }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            var b = new Bag([1, 2]);
            ReadOnlySpan<int> s = b.View();
            b[0] += 10;
            WriteLine(s[0]);
            sealed class Bag(Vec<int> v)
            {
                private Vec<int> _v = v;
                public ReadOnlySpan<int> View() => _v.AsReadOnlySpan();
                [Mut] public int this[int i] { get => _v[i]; set => _v[i] = value; }
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            var ro = new RoProp(3);
            ro.Val += 1;
            struct RoProp(int v) { public int Val => v; }
            """,
        };
        yield return new object[]
        {
            "VIB003",
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
        };
        yield return new object[]
        {
            "VIB003",
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
        };
        yield return new object[]
        {
            "VIB003",
            """
            Vec<int> vc = [1, 2, 3, 4];
            Span<int> s = vc[0..ReplaceVec(ref vc)];
            WriteLine(s.Length);
            static int ReplaceVec(ref Vec<int> v) { v = [9, 9]; return 1; }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            Vec<int> v = [1, 2, 3];
            Span<int> s = v[Grow(ref v)..];
            WriteLine(s.Length);
            static int Grow(ref Vec<int> v) { v.Push(4); return 0; }
            """,
        };
        yield return new object[]
        {
            "VIB003",
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
        };
        yield return new object[]
        {
            "VIB003",
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
        };
        yield return new object[]
        {
            "VIB001",
            """
            var h = new H(1);
            WriteLine(Two(h, Swap(ref h)));
            static int Swap(ref H h) { h = new H(100); return 7; }
            static int Two(H h, int k) => h.X * 2 + k;
            sealed class H(int n) { public int X = n; }
            """,
        };
        yield return new object[]
        {
            "VIB001",
            """
            var h = new H(1);
            WriteLine(Two(h, h.X));
            static int Two(H h, int k) => h.X + k;
            sealed class H(int n) { public int X = n; }
            """,
        };
        yield return new object[]
        {
            "VIB001",
            """
            Vec<int> v = [1, 2];
            WriteLine(TakeVec(v, Grow(ref v)));
            static int Grow(ref Vec<int> v) { v.Push(3); return v.Count; }
            static int TakeVec(Vec<int> v, int k) => v.Count + k;
            """,
        };
        yield return new object[]
        {
            "VIB001",
            """
            var h = new H(1);
            var b = new Box(h, Swap(ref h));
            WriteLine(b.K);
            static int Swap(ref H h) { h = new H(100); return 7; }
            sealed class H(int n) { public int X = n; }
            sealed class Box(H h, int k) { public H Inner = h; public int K = k; }
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
            var h = new H(0);
            _ = Parse(result: out h.X, s: Txt(ref h));
            static bool Parse(out int result, string s) => int.TryParse(s, out result);
            static string Txt(ref H h) { h = new H(9); return "42"; }
            sealed class H(int x) { public int X = x; }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            WriteLine(F(nope: 1, b: 2));
            static int F(int a, int b) => a + b;
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            WriteLine(F(1, a: 2));
            static int F(int a, int b) => a + b;
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            WriteLine(F(b: 1, 2));
            static int F(int a, int b) => a + b;
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            static void WriteLine(int a, int b) => Prelude.WriteLine(a + b);
            WriteLine(42);
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Host.Run();
            static class Host
            {
                private static void WriteLine(int a, int b) => Prelude.WriteLine(a + b);
                public static void Run() => WriteLine(42);
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            Outer.Inner.Run();
            static class Outer
            {
                public static int Pick(int x) => x;
                public static class Inner
                {
                    private static int Pick(int a, int b) => a + b;
                    public static int Run() => Pick(1);
                }
            }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            var ln = "a\nb".Lines();
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            _ = MakeLines();
            static Utf8Lines MakeLines() => default;
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            ReadOnlySpan<byte> raw = "a\0b\0"u8;
            foreach (var item in new Utf8ArgEnumerator(raw))
            {
                WriteLine(item.Length);
            }
            """,
        };
        yield return new object[]
        {
            "VIB008",
            """
            WriteLine(Make<int>().Val);
            static Wrap<T> Make<T>() where T : unmanaged => default;
            struct Wrap<T> where T : unmanaged { public T Val; }
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            TakeNUInt(1L);
            static void TakeNUInt(nuint x) => WriteLine((ulong)x);
            """,
        };
        yield return new object[]
        {
            "VIB000",
            """
            using Vibranium.Std;
            var sb = new StringBuf();
            sb += 123;
            """,
        };
        yield return new object[]
        {
            "VIB003",
            """
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
        };
        yield return new object[]
        {
            "VIB003",
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
        };
        yield return new object[]
        {
            "VIB003",
            """
            var h = new H(1);
            h.X += Swap(ref h);
            WriteLine(h.X);
            static int Swap(ref H h) { h = new H(100); return 7; }
            sealed class H(int n) { public int X = n; }
            """,
        };
    }

    [Theory]
    [MemberData(nameof(SelfHostNegativeCases))]
    public void SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode(string expectedCode, string source)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "Bad.cs");
            File.WriteAllText(srcPath, source);

            var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
            Assert.NotEqual(0, exitCode);
            string combined = stdout + "\n" + stderr;
            Assert.Contains(expectedCode, combined);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(VibraniumEndToEndTests.NegativeSafetyCases), MemberType = typeof(VibraniumEndToEndTests))]
    public void SelfHost_MatchesAllStage0NegativeSafetyCases(string expectedCode, string source)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string srcPath = Path.Combine(tempDir, "BadParity.cs");
            File.WriteAllText(srcPath, source);

            var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
            Assert.NotEqual(0, exitCode);
            string combined = stdout + "\n" + stderr;
            Assert.Contains(expectedCode, combined);
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
    public void SelfHost_BootstrapTripleIdentityFixedPoint_And_PathIndependence()
    {
        string bootstrapScript = Path.Combine(RepoRoot, "eng", "bootstrap", "bootstrap.sh");
        string cfg = ResolveActiveBuildConfiguration();

        var (exitCode, stdout, stderr) = RunProcess(
            "bash",
            bootstrapScript,
            "--configuration",
            cfg,
            "--skip-dotnet-build",
            "--skip-benchmark");

        Assert.True(exitCode == 0, $"bootstrap.sh failed (exit {exitCode}):\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.Contains("BOOTSTRAP FIXED POINT VERIFIED: Triple-Identity (managed.ll == stage1.ll == stage2.ll) == ", stdout);
        Assert.Contains("libc.so", stdout);
    }

    [Fact]
    public void SelfHost_TierACoreAndStdErgonomics_DifferentialManagedAndNative_Opt18_AsanClean()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"vib-self-tiera-{Guid.NewGuid():N}.txt");
        try
        {
            string escapedPath = tempFile.Replace("\\", "\\\\");
            string source = $$"""
                using Vibranium.Std;

                // 1. Overload resolution: numeric widening, constant narrowing, Span->ReadOnlySpan, non-generic vs generic, no-default vs default
                short s16 = 7;
                WriteLine(Overloads.PickNum(s16));          // 107 (int beats long)
                WriteLine(Overloads.PickNum(9L));           // 209 (exact long)
                WriteLine(Overloads.PickByteOrSpan(65));    // 165 (const int -> byte)
                WriteLine(Overloads.PickByteOrSpan("AB"u8));// 2
                Span<byte> mSpan = [(byte)'V', (byte)'I', (byte)'B'];
                WriteLine(Overloads.PickSpan(mSpan));       // 13 (Span<byte> beats ReadOnlySpan<byte>)
                ReadOnlySpan<byte> roSpan = mSpan;
                WriteLine(Overloads.PickSpan(roSpan));      // 23
                WriteLine(Overloads.PickGen(42));           // 142 (non-generic beats generic)
                WriteLine(Overloads.PickGen(true));         // 1
                WriteLine(Overloads.PickDef(5));            // 105 (no-default beats uses-default)
                WriteLine(Overloads.PickDef(5, 6));         // 211

                // Two-phase `ref` call arg followed by shared read (`in`) is allowed
                int tpVal = 10;
                TwoPhaseAdd(ref tpVal, ReadShared(in tpVal));
                WriteLine(tpVal); // 20

                // Constructor overload resolution (`new StringBuf()` vs `new StringBuf(int)`, custom ctors)
                var o1 = new OverBox(3);
                var o2 = new OverBox("hello");
                WriteLine(o1.Code + o2.Code);     // 3 + 505 = 508

                // 2. Lexical bare-call lookup order: local func > enclosing method > using static
                {
                    static void WriteLine(int customVal) => Prelude.WriteLine(customVal + 1000);
                    WriteLine(23); // 1023 (local function shadows Prelude.WriteLine)
                }
                WriteLine(ShadowHost.Run()); // 777 (enclosing ReadLine() shadows ConsoleIo.ReadLine())

                // 3. Static extension methods (ByteSpanExtensions, MemoryExtensions<byte>, custom extensions)
                ReadOnlySpan<byte> sample = "  hello_vibranium  ".AsUtf8();
                ReadOnlySpan<byte> trimmed = sample.Trim();
                WriteLine(trimmed.SequenceEqual("hello_vibranium"));
                WriteLine(trimmed.StartsWith("hello"));
                WriteLine(trimmed.EndsWith("vibranium"));
                WriteLine(trimmed.Contains((byte)'_'));
                WriteLine(trimmed.Contains("vib"));
                WriteLine(trimmed.IndexOf((byte)'_'));
                WriteLine(trimmed.IndexOf("vib"));
                WriteLine(trimmed.LastIndexOf((byte)'i'));
                WriteLine(System.MemoryExtensions.SequenceEqual<byte>(trimmed, "hello_vibranium"u8));
                WriteLine(System.MemoryExtensions.IndexOf(mSpan, (byte)'I'));
                WriteLine(mSpan.SumAscii());      // Span<byte> -> ReadOnlySpan<byte> extension receiver reborrow
                WriteLine(trimmed.DropPrefix(6)); // vibranium (LifetimeOf("span") extension)

                // 4. String.AsUtf8 (const + dynamic), Lines() (CR, LF, CRLF, empty, saved slice), Write/WriteLine(span), Stdio(string)
                string dyn = ChooseText(1);
                Write("DYN:".AsUtf8());
                WriteLine(dyn.AsUtf8());
                Io.Write("IO:".AsUtf8());
                Io.WriteLine("café-🦀".AsUtf8());
                Stdio.WriteOut("STDIO:ok\n");

                var sb = new StringBuf();
                sb.Append("hdr:");
                sb.Append(42);
                sb.Append(":");
                sb.Append(-9000000000L);
                sb.Append(":");
                sb.Append(true);
                sb.AppendLine();
                sb.AppendLine("alpha\r\nbeta\rgamma\n");
                Write(sb);

                scoped ReadOnlySpan<byte> lastNonEmpty = ""u8;
                foreach (var ln in sb.Lines())
                {
                    Write("[");
                    Write(ln);
                    WriteLine("]");
                    if (ln.Length > 0) lastNonEmpty = ln;
                }
                Write("LAST:".AsUtf8());
                WriteLine(lastNonEmpty);

                // 5. Generic Index (^k) & Range (a..b, ..) on StringBuf and custom single-eval counters
                var tag = new StringBuf();
                tag.Append("ABCDEF");
                WriteLine(tag[^1]);     // 70 ('F')
                WriteLine(tag[1..^1]);  // BCDE
                WriteLine(tag[..]);     // ABCDEF

                var ctr = new EvalTracker([10, 20, 30, 40, 50]);
                int sideEffects = 0;
                ReadOnlySpan<int> sub = Touch(ref sideEffects, ref ctr)[1..^1];
                WriteLine(sub[0] + sub[^1]); // 20 + 40 = 60
                WriteLine(sideEffects); // 1 (receiver evaluated once)
                WriteLine(ctr.Reads);   // 1 (Count read once for ^1)

                var refCtr = new RefSlotCounter([4, 5, 6]);
                refCtr[^1] = 60;
                WriteLine(refCtr[^1]);      // 60
                WriteLine(refCtr.LenReads); // 2

                // 6. Option/Result Expect & UnwrapOrDefault (including L1 linear Expect and owned Err drop under LSan)
                Vec<int> v = [1, 2];
                v.Add(3);
                v.Add(v.Count);
                WriteLine(v[^1]); // 3

                Option<int> oSome = Some(88);
                Option<int> oNone = None;
                WriteLine(oSome.Expect("ok"));
                WriteLine(oNone.UnwrapOrDefault());

                var errBuf = new StringBuf();
                errBuf.Append("owned-err-dropped-cleanly");
                Result<int, StringBuf> rErr = Err(errBuf);
                WriteLine(rErr.UnwrapOrDefault()); // 0 (and LSan proves errBuf is dropped!)

                Option<Guard> gOpt = Some(new Guard(1));
                { using var g1 = gOpt.Expect("guard1"); }
                Result<Guard, int> gRes = Ok(new Guard(2));
                { using var g2 = gRes.Expect("guard2"); }

                // 7. BCL int.TryParse / long.TryParse (string and ReadOnlySpan<byte>)
                WriteLine(int.TryParse("  -2147483648 \0", out int minI32) ? minI32 : 0);
                WriteLine(Int32.TryParse("12\0 "u8, out int badNul) ? badNul : -9);
                WriteLine(long.TryParse(" -9223372036854775808\0", out long minI64) ? minI64 : 0L);
                WriteLine(Int64.TryParse("99999999999999999999999", out var ovf) ? ovf : -7L);

                // 8. Plain-string Vibranium.Std APIs (StrMap, Interner, Ipv4Address, File, Env.Args, ConsoleIo.ReadLine/TryReadLine)
                var map = new StrMap<int>(8);
                map.Set("k1", 11);
                map.Set("k2", 22);
                WriteLine(map.Contains("k1"));
                WriteLine(map.Get("k2").Expect("k2"));
                WriteLine(map.Remove("k1").UnwrapOrDefault());
                WriteLine(map.Contains("k1"));

                var interner = new Interner(8);
                int idA = interner.Intern("sym_a");
                int idA2 = interner.Intern("sym_a");
                WriteLine(idA == idA2);

                WriteLine(Ipv4Address.TryParse("127.0.0.1").IsSome);
                WriteLine(Ipv4Address.TryParse("127.0.01.1").IsSome);

                string fpath = "{{escapedPath}}";
                WriteLine(File.Exists("{{escapedPath}}.missing"));
                File.WriteAllText(fpath, "file-line-1\nfile-line-2").Expect("write");
                File.AppendAllText(fpath, "\nfile-line-3").Expect("append");
                WriteLine(File.Exists(fpath));
                StringBuf readBack = File.ReadAllText(fpath).Expect("read");
                foreach (var fl in readBack.Lines())
                {
                    Write("F:".AsUtf8());
                    WriteLine(fl);
                }

                // Env.Args foreach (`Utf8ArgEnumerator`) + `cliArgs[^1]`
                ArgList cliArgs = Env.Args().Expect("args");
                Write("LAST_ARG:".AsUtf8());
                WriteLine(cliArgs[^1]);
                int argIdx = 0;
                foreach (var a in cliArgs)
                {
                    if (argIdx > 0)
                    {
                        Write("ARG:".AsUtf8());
                        WriteLine(a);
                    }
                    argIdx += 1;
                }

                // ConsoleIo.ReadLine & TryReadLine over redirected stdin
                var reuse = new StringBuf();
                while (TryReadLine(ref reuse, 16).Expect("stdin"))
                {
                    Write("IN:".AsUtf8());
                    WriteLine(reuse);
                    if (reuse.AsUtf8().SequenceEqual("stop")) break;
                }
                if (ReadLine().Expect("eof-or-tail") is Some<StringBuf>(var tailLine))
                {
                    Write("TAIL:".AsUtf8());
                    WriteLine(tailLine);
                }

                static int ReadShared(in int v) => v;
                static void TwoPhaseAdd(ref int r, int delta) { r += delta; }
                [return: LifetimeOf("t")]
                static ref EvalTracker Touch(ref int counter, ref EvalTracker t) { counter += 1; return ref t; }
                static string ChooseText(int k) => k == 1 ? "plain-utf8" : "other";

                static class Overloads
                {
                    public static int PickNum(int x) => 100 + x;
                    public static int PickNum(long x) => 200 + (int)x;
                    public static int PickByteOrSpan(byte b) => 100 + b;
                    public static int PickByteOrSpan(ReadOnlySpan<byte> s) => s.Length;
                    public static int PickSpan(Span<byte> s) => 10 + s.Length;
                    public static int PickSpan(ReadOnlySpan<byte> s) => 20 + s.Length;
                    public static int PickGen(int x) => 100 + x;
                    public static int PickGen<T>(T unused) where T : unmanaged => 1;
                    public static int PickDef(int a) => 100 + a;
                    public static int PickDef(int a, int b) => 200 + a + b;
                }

                class OverBox
                {
                    public int Code;
                    public OverBox(int x) { Code = x; }
                    public OverBox(string s) { Code = 500 + s.AsUtf8().Length; }
                }

                static class ShadowHost
                {
                    private static int ReadLine() => 777;
                    public static int Run() => ReadLine();
                }

                class EvalTracker(Vec<int> data)
                {
                    private Vec<int> _data = data;
                    public int Reads;
                    public int Count
                    {
                        [Mut]
                        get { Reads += 1; return _data.Count; }
                    }
                    public ReadOnlySpan<int> Slice(int start, int length) => _data.ReadOnlySlice(start, length);
                }

                class RefSlotCounter(Vec<int> items)
                {
                    private Vec<int> _items = items;
                    public int LenReads;
                    public int Count
                    {
                        [Mut]
                        get { LenReads += 1; return _items.Count; }
                    }
                    public ref int this[int index]
                    {
                        [Mut] get => ref _items[index];
                    }
                }

                class Guard(int id) : IDisposable
                {
                    public void Dispose()
                    {
                        Write("g:");
                        WriteLine(id);
                    }
                }

                static class CustomSpanExt
                {
                    public static int SumAscii(this ReadOnlySpan<byte> span)
                    {
                        int sum = 0;
                        foreach (byte b in span) sum += b;
                        return sum;
                    }

                    [return: LifetimeOf("span")]
                    public static ReadOnlySpan<byte> DropPrefix(this ReadOnlySpan<byte> span, int count) =>
                        span.Slice(count);
                }
                """;

            var res = CompileAndRunSelfHostDifferential(
                source,
                withAsan: true,
                stdinText: "one\r\ntwo\nstop\nfinal-tail",
                appArgs: ["--flag", "hello-arg"]);

            Assert.Equal(0, res.ManagedExit);
            Assert.Equal(0, res.NativeExit);
            Assert.Equal(string.Empty, res.ManagedErr);
            Assert.Equal(string.Empty, res.NativeErr);
            Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SelfHost_ExpectPanics_OnNoneAndErr_Exit101_WithExactMessage_MatchManaged()
    {
        const string optSrc = """
            Stdio.WriteOut("before-opt\n");
            Option<int> n = None;
            WriteLine(n.Expect("self-host option expect 🦀"));
            """;
        var optRes = CompileAndRunSelfHostDifferential(optSrc, withAsan: false);
        Assert.Equal(101, optRes.ManagedExit);
        Assert.Equal(101, optRes.NativeExit);
        Assert.Equal(optRes.ManagedOut.Replace("\r\n", "\n"), optRes.NativeOut.Replace("\r\n", "\n"));
        Assert.Equal("panic: self-host option expect 🦀\n", optRes.NativeErr.Replace("\r\n", "\n"));
        Assert.Equal(optRes.ManagedErr.Replace("\r\n", "\n"), optRes.NativeErr.Replace("\r\n", "\n"));

        const string resSrc = """
            Stdio.WriteErr("warn:");
            Result<int, int> r = Err(500);
            WriteLine(r.Expect("self-host result expect"));
            """;
        var resRes = CompileAndRunSelfHostDifferential(resSrc, withAsan: false);
        Assert.Equal(101, resRes.ManagedExit);
        Assert.Equal(101, resRes.NativeExit);
        Assert.Equal("warn:panic: self-host result expect\n", resRes.NativeErr.Replace("\r\n", "\n"));
        Assert.Equal(resRes.ManagedErr.Replace("\r\n", "\n"), resRes.NativeErr.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelfHost_NoStd_RejectsBclIntTryParse_MemoryExtensions_And_ConsoleIo()
    {
        string[] rejectedNoStd =
        [
            """
            if (int.TryParse("42", out int v)) WriteLine(v);
            """,
            """
            using System;
            ReadOnlySpan<byte> a = "ok"u8;
            WriteLine(MemoryExtensions.SequenceEqual<byte>(a, "ok"u8));
            """,
            """
            _ = ReadLine();
            """,
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-nostd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            for (int i = 0; i < rejectedNoStd.Length; i++)
            {
                string p = Path.Combine(tempDir, $"BadNoStd{i}.cs");
                File.WriteAllText(p, rejectedNoStd[i]);
                var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", p, "--sysroot", SysrootPath, "--no-std");
                Assert.NotEqual(0, exitCode);
                Assert.Contains("VIB000", stdout + "\n" + stderr);
            }

            string okCorePath = Path.Combine(tempDir, "CoreOnlyOk.cs");
            File.WriteAllText(
                okCorePath,
                """
                Vec<int> v = [1, 2];
                v.Add(3);
                foreach (var ln in "a\r\nb".AsUtf8().Lines())
                {
                    WriteLine(ln);
                }
                Option<int> o = Some(v[^1]);
                Option<int> emptyOpt = None;
                WriteLine(o.Expect("ok") + emptyOpt.UnwrapOrDefault());
                """);
            var (okExit, okOut, okErr) = RunProcess(DotnetExe, VibSelfDllPath, "check", okCorePath, "--sysroot", SysrootPath, "--no-std");
            Assert.True(okExit == 0, $"Expected Core-only --no-std check to succeed:\n{okOut}\n{okErr}");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SelfHost_Round2AuditFixes_DifferentialManagedAndNative_Opt18_AsanClean()
    {
        const string source = """
            using Vibranium.Std;

            // 1. P0-1 & P1-5: Property/indexer `=`, `op=`, `++`, `--` (including set-before-get indexer & single-eval)
            var pc = new PropCounter(10, 2);
            WriteLine(ReadSharedProp(in pc)); // 10 (property-level [Mut] keeps getter shared)
            pc.Val += 5; // 15
            WriteLine(++pc.Val); // 16
            WriteLine(pc.Val++); // 16 (now 17)
            pc.Small += 3; // 5
            WriteLine(pc.Small++); // 5 (now 6)
            WriteLine(pc.BumpBare()); // 28 + 6 = 34
            var deck = new SetFirstDeck([10, 20, 30, 40]);
            deck[0] = 4;
            WriteLine(deck[0]); // 4 (set-before-get regression)
            deck[0] += 6; // 10
            WriteLine(++deck[0]); // 11
            WriteLine(deck[0]++); // 11 (now 12)
            deck[^1] += 5; // 45
            WriteLine(deck[^1]--); // 45 (now 44)
            WriteLine(deck[0] + deck[^1]); // 56
            int recvHits = 0;
            int idxHits = 0;
            BorrowDeck(ref recvHits, ref deck)[EvalIdx(ref idxHits)] += 100;
            WriteLine(deck[1]); // 120
            WriteLine(recvHits * 10 + idxHits); // 11
            // 2. P1-2: Range operand evaluation order (Start, End evaluated BEFORE Length/Count)
            var rangeTrack = new RangeOrderTracker(4);
            WriteLine(rangeTrack[^EvalStart()..EvalEnd()]); // start,end,len,slice,12
            // 3. P1-6 & P1-7: Left-to-right by-value argument/operand materialization
            int x = 10;
            WriteLine(AddTwo(x, Bump91(ref x))); // 10 + 101 = 111
            int y = 20;
            var pv = new PairVal(y, Bump91(ref y));
            WriteLine(pv.A * 1000 + pv.B); // 20111
            int z = 5;
            WriteLine(z + Bump91(ref z)); // 5 + 96 = 101
            int w = 7;
            Vec<int> snap = [w, Bump91(ref w)];
            WriteLine(snap[0] * 1000 + snap[1]); // 7098
            // 4. Fable P1-6: Named arguments (calls, defaults, generics, ctors, int.TryParse)
            WriteLine(CalcNamed(3, 2)); // 302
            WriteLine(CalcDefault(1, 9, 0)); // 109
            WriteLine(GenNamed(4, 7)); // 407
            var np = new PairVal(8, 9);
            WriteLine(np.A * 10 + np.B); // 89
            var h = new ParseBox(0);
            if (int.TryParse(s: NextDigits(ref h), result: out h.Val))
            {
                WriteLine(h.Val); // 42
            }

            // 5. P1-10: Nested types accessing outer private static members/consts
            WriteLine(OuterScope.InnerWorker.Compute(5)); // 47
            // 6. String.Lines() & StringBuf `operator +=` & chained slice/index
            var linesJoin = new StringBuf();
            foreach (var ln in "alpha\r\nbeta\ngamma".Lines())
            {
                linesJoin += ln;
                linesJoin += "|";
            }

            Span<byte> tailMark = [(byte)'O', (byte)'K'];
            linesJoin += tailMark;
            WriteLine(linesJoin); // alpha|beta|gamma|OK
            WriteLine(linesJoin[6..][^3]); // 124 ('|')
            Vec<int> nums = [10, 20, 30, 40, 50];
            Span<int> numSpan = nums.AsSpan();
            numSpan[1..4][1] = 333;
            WriteLine(numSpan[1..4][^2]); // 333
            // 7. Concrete default(Wrap<int>) & custom GetEnumerator() -> Utf8ArgEnumerator
            Wrap<int> zeroWrap = default;
            WriteLine(zeroWrap.Val); // 0
            Vec<byte> rawBytes = [(byte)'o', (byte)'n', (byte)'e', 0, (byte)'t', (byte)'w', (byte)'o', 0];
            var nulBag = new NulItems(rawBytes);
            var bagOut = new StringBuf();
            foreach (var part in nulBag)
            {
                bagOut += part;
                bagOut += ",";
            }

            WriteLine(bagOut); // one,two,three,
            // 8. C# 15 nint / nuint conversions, overload tie-breaking, and binary promotion
            long ni = 15;
            ulong nu = 25;
            uint u32 = 5;
            ulong nuSum = nu + u32 + 2;
            WriteLine((long)ni + (long)nuSum); // 47
            WriteLine(NativeOver.Pick(5)); // 105 (nint beats nuint for int literal)
            // 9. Heap-traversing compound assignment with shared receiver read
            var cbox = new HeapCounter(10);
            cbox.V += cbox.Get();
            WriteLine(cbox.V); // 20
            static int ReadSharedProp(in PropCounter c) => c.Val;
            [return: LifetimeOf("d")]
            static ref SetFirstDeck BorrowDeck(ref int hits, ref SetFirstDeck d)
            {
                hits += 1;
                return ref d;
            }

            static int EvalIdx(ref int hits)
            {
                hits += 1;
                return 1;
            }

            static int EvalStart()
            {
                Write("start,");
                return 3;
            }

            static int EvalEnd()
            {
                Write("end,");
                return 3;
            }

            static int Bump91(ref int v)
            {
                v += 91;
                return v;
            }

            static int AddTwo(int a, int b) => a + b;
            static int CalcNamed(int a, int b) => a * 100 + b;
            static int CalcDefault(int a, int b, int c) => a * 100 + b + c;
            static int GenNamed<T>(T left, int right)
                where T : unmanaged => right + 400;
            static string NextDigits(ref ParseBox b)
            {
                b = new ParseBox(1);
                return "42";
            }

            static class NativeOver
            {
                public static long Pick(long x) => 100 + (long)x;
                public static long Pick(ulong x) => 200 + (long)x;
            }

            struct Wrap<T>(T val)
                where T : unmanaged
            {
                public T Val = val;
            }

            sealed class NulItems(Vec<byte> raw)
            {
                private Vec<byte> _raw = raw;
                public Utf8ArgEnumerator GetEnumerator() => new Utf8ArgEnumerator(_raw.AsReadOnlySpan());
            }

            sealed class HeapCounter(int v)
            {
                public int V = v;
                public int Get() => V;
            }

            sealed class ParseBox(int v)
            {
                public int Val = v;
            }

            readonly struct PairVal(int a, int b)
            {
                public readonly int A = a;
                public readonly int B = b;
            }

            sealed class PropCounter(int v, byte small)
            {
                private int _v = v;
                private byte _small = small;
                [Mut]
                public int Val { get => _v; set => _v = value; }

                [Mut]
                public byte Small { get => _small; set => _small = value; }

                [Mut]
                public int BumpBare()
                {
                    Val += 10;
                    Val++;
                    return Val + Small;
                }
            }

            struct SetFirstDeck(Vec<int> items)
            {
                private Vec<int> _items = items;
                public int Length => _items.Count;

                [Mut]
                public int this[int index] { set => _items[index] = value; get => _items[index]; }
            }

            sealed class RangeOrderTracker(int len)
            {
                public int Length
                {
                    get
                    {
                        Write("len,");
                        return len;
                    }
                }

                public int Slice(int s, int l)
                {
                    Write("slice,");
                    return s * 10 + l;
                }
            }

            static class OuterScope
            {
                private const int SecretBias = 40;
                private static int AddTwoSecret(int x) => x + 2;
                public static class InnerWorker
                {
                    public static int Compute(int x) => SecretBias + AddTwoSecret(x);
                }
            }
            """;

        var res = CompileAndRunSelfHostDifferential(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedErr);
        Assert.Equal(string.Empty, res.NativeErr);
        Assert.Contains("start,end,len,slice,12\n", res.NativeOut.Replace("\r\n", "\n"));
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelfHost_Round3_LvalueFlowCaptures_IsPatternExprs_And_GenericStaticCalls_Differential()
    {
        const string source = """
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
            if (ov is Some<Vec<int>> (var innerV))
            {
                WriteLine(innerV[1]);
            }

            var sh = new CtorBranchHolder(c);
            WriteLine(sh.Tag);
            int a = 50;
            bool inRange = (a is var cutValue0) && (((cutValue0 > 3) && (cutValue0 < 100)));
            WriteLine(inRange);
            WriteLine(a is 5 or 50);
            WriteLine(a is not 50);
            Option<int> optFive = Some(5);
            WriteLine(optFive is Some<int> (5));
            WriteLine(optFive is not NoneType);
            WriteLine(CallGenericStatic<int>());
            WriteLine('a');
            static bool Flag(bool b) => b;
            static void SetRef(ref int slot, int val)
            {
                slot = val;
            }

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
                public void Bump(int delta)
                {
                    X += delta;
                }
            }

            sealed class PropBox(int init)
            {
                private int _p = init;
                [Mut]
                public int P
                {
                    get
                    {
                        Write("G,");
                        return _p;
                    }

                    set
                    {
                        Write("S,");
                        _p = value;
                    }
                }
            }

            sealed class Deck(Vec<int> items)
            {
                private Vec<int> _items = items;
                public int Length => _items.Count;

                [Mut]
                public int this[int idx] { get => _items[idx]; set => _items[idx] = value; }
            }

            sealed class CtorBranchHolder
            {
                public string Tag;
                public CtorBranchHolder(bool flag) { Tag = flag ? "ctor-true" : "ctor-false"; }
            }

            static class Factory<T>
            {
                public static int Make() => 73;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedErr);
        Assert.Equal(string.Empty, res.NativeErr);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        Assert.Contains("D:17,307\n42\n0\n2\n3\nctor-true\nTrue\nTrue\nFalse\nTrue\nTrue\n73\n97\n", res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelfHost_Round3_OverloadedIndexers_UIntPromotion_Preprocessor_Enums_And_UnionSubpatterns_Differential()
    {
        const string source = """
            using global::System;
            using Vibranium.Std;

            // 1. Non-conditional directives & leading-dot floats (C5 cuts conditional compilation).
            #region shared_source
            int ppVal = 42;
            WriteLine(ppVal);
            #endregion

            double dHalf = .5 + .25;
            float fQuarter = .25f;
            WriteLine((int)(dHalf * 100.0) + (int)(fQuarter * 100.0f));

            // 2. Overloaded, multi-param, named-arg, ^fromEnd, and ref-returning indexers
            var ov = new OverIdx([10, 20, 30], [1000L, 2000L]);
            ov[1] = 25;
            ov[1] += 5;
            ov[1]++;
            long lvIdx = 0L;
            ov[lvIdx] = 500L;
            ov[lvIdx] += 50L;
            ov[lvIdx]++;
            WriteLine(ov[1]);
            WriteLine(ov[lvIdx]);
            WriteLine(ov[^1]);
            WriteLine(ov["key"]);
            WriteLine(ov["key"u8]);

            var grid = new Grid2D([1, 2, 3, 4, 5, 6], 3);
            grid[1, 0] = 40;
            grid[r: 1, c: 2] += 10;
            grid[r: 0, c: 1]++;
            WriteLine(grid[0, 1] + grid[1, 0] + grid[r: 1, c: 2]);

            var rgrid = new RefGrid([10, 20, 30, 40], 2);
            rgrid[1, 0] += 5;
            rgrid[r: 1, c: 1]++;
            WriteLine(rgrid[1, 0] + rgrid[1, 1]);

            // 3. Unary/assign/incdec numeric widening + decimal MinValue literals
            int i = 5;
            long wNeg = -i;
            long wNot = ~i;
            long wPost = i++;
            long wPre = ++i;
            int j = 0;
            long wAsg = (j = i);
            WriteLine(wNeg + wNot + wPost + wPre + wAsg + NegToLong(3));
            int minI = -2147483648;
            long minL = -9223372036854775808;
            WriteLine(minI == int.MinValue && minL == long.MinValue);

            // 4. Fable P1-3 uint & nint/nuint promotion
            uint m = uint.MaxValue;
            WriteLine(m * 2);
            WriteLine(m + 1 == 0);
            WriteLine((m * 31) % 1000);
            uint ua = uint.MaxValue - 4;
            long ul = ua - 5;
            WriteLine(ul);
            uint u5 = 5;
            WriteLine(-u5);

            // 5. Exhaustive bool switch + when guard on bool pattern
            bool bf = false;
            int bs1 = bf switch { true => 10, false => 20 };
            int extra = 7;
            int bs2 = (!bf) switch
            {
                true when extra == 0 => 1,
                true => 2,
                false => 3,
            };
            WriteLine(bs1 + bs2);

            // 6. Enum bitwise & update operators
            Perm p = Perm.Read | Perm.Write;
            p |= Perm.Exec;
            p &= ~Perm.Write;
            p ^= Perm.Read;
            Perm step = Perm.Read;
            step++;
            Perm step2 = step + 1;
            int diff = step2 - Perm.Read;
            WriteLine((int)p * 100 + diff);

            // 7. Nested union subpatterns
            Result<int, bool> r1 = Err(true);
            Result<int, bool> r2 = Err(false);
            Result<int, bool> r3 = Ok(5);
            Result<int, bool> r4 = Ok(9);
            WriteLine(MatchRes(r1) * 1000 + MatchRes(r2) * 100 + MatchRes(r3) * 10 + MatchRes(r4));
            Option<int> o5 = Some(5);
            Option<int> o8 = Some(8);
            if (o5 is Some<int>(5) && o8 is Some<int>(_) && !(o8 is Some<int>(5)))
            {
                WriteLine(77);
            }

            // 8. WriteLine(char) & generic static call on type using enclosing type param
            WriteLine('A');
            Prelude.WriteLine('B');
            Io.WriteLine('0');
            WriteLine(CallBothGenericStatics<long>());

            static long NegToLong(int x) => -x;

            static int MatchRes(Result<int, bool> r) => r switch
            {
                Err<bool>(true) => 1,
                Err<bool>(false) => 2,
                Ok<int>(5) => 3,
                Ok<int>(_) => 4,
            };

            static int CallBothGenericStatics<T>() where T : unmanaged =>
                GenMaker<T>.Make() + GenStaticMaker<T>.Make();

            enum Perm
            {
                None = 0,
                Read = 1,
                Write = 2,
                Exec = 4,
            }

            struct GenMaker<T> where T : unmanaged
            {
                public static int Make() => 30;
            }

            static class GenStaticMaker<T>
            {
                public static int Make() => 12;
            }

            sealed class OverIdx(Vec<int> ints, Vec<long> longs)
            {
                private Vec<int> _ints = ints;
                private Vec<long> _longs = longs;
                public int Length => _ints.Count;

                [Mut]
                public int this[int index]
                {
                    get => _ints[index];
                    set => _ints[index] = value;
                }

                [Mut]
                public long this[long index]
                {
                    get => _longs[(int)index];
                    set => _longs[(int)index] = value;
                }

                public int this[ReadOnlySpan<byte> key] => key.Length + 100;
                public int this[string key] => key.AsUtf8().Length + 200;
            }

            sealed class Grid2D(Vec<int> cells, int cols)
            {
                private Vec<int> _cells = cells;
                private int _cols = cols;

                [Mut]
                public int this[int r, int c]
                {
                    get => _cells[r * _cols + c];
                    set => _cells[r * _cols + c] = value;
                }
            }

            sealed class RefGrid(Vec<int> cells, int cols)
            {
                private Vec<int> _cells = cells;
                private int _cols = cols;

                public ref int this[int r, int c]
                {
                    [Mut] get => ref _cells[r * _cols + c];
                }
            }
            """;

        var res = CompileAndRunSelfHostDifferential(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedErr);
        Assert.Equal(string.Empty, res.NativeErr);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        Assert.Contains("4294967294\nTrue\n265\n4294967286\n-5\n22\n402\n1234\n77\n65\n66\n48\n42\n", res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelfHost_Round3_NegativeDiagnostics_And_Stage0Parity()
    {
        (string Name, string ExpectedId, string Code)[] cases =
        [
            // Stage-0 Round-3 borrow conflicts & gatekeeper spec compliance parity
            (
                "VecElemBranchingRhsGrowConflict",
                "VIB003",
                """
                bool c = Cond();
                Vec<int> v = [1, 2, 3];
                v[0] = c ? Grow(ref v) : 3;
                WriteLine(v[0]);
                static bool Cond() => true;
                static int Grow(ref Vec<int> v) { v.Push(99); return 7; }
                """
            ),
            (
                "PropCompoundBranchingRhsSwapConflict",
                "VIB003",
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
                """
            ),
            (
                "RefVecElemShortCircuitGrowConflict",
                "VIB003",
                """
                bool c = Cond();
                Vec<int> v = [1, 2, 3];
                Set(ref v[0], c && GrowB(ref v));
                WriteLine(v[0]);
                static bool Cond() => true;
                static void Set(ref int slot, bool flag) { slot = flag ? 1 : 0; }
                static bool GrowB(ref Vec<int> v) { v.Push(9); return true; }
                """
            ),
            (
                "StructWithExpressionBanned",
                "VIB000",
                """
                var p = new Pt(1, 2);
                var q = p with { X = 5 };
                WriteLine(q.X);
                struct Pt(int x, int y) { public int X = x; public int Y = y; }
                """
            ),
            (
                "IsErrFieldOutTargetBanned",
                "VIB000",
                """
                var h = new Holder(9);
                Result<int, int> r = Ok(4);
                if (r.IsErr(out h.X, out var e)) { WriteLine(e.Error); }
                WriteLine(h.X);
                sealed class Holder(int x) { public int X = x; }
                """
            ),
            (
                "IsErrExistingLocalOkTargetBanned",
                "VIB000",
                """
                int existing = 0;
                Result<int, int> r = Ok(4);
                if (r.IsErr(out existing, out var e)) { WriteLine(e.Error); }
                WriteLine(existing);
                """
            ),
            (
                "IsErrExistingLocalErrTargetBanned",
                "VIB000",
                """
                Err<int> existingErr = Err(1);
                Result<int, int> r = Err(4);
                if (r.IsErr(out var ok, out existingErr)) { WriteLine(existingErr.Error); }
                else { WriteLine(ok); }
                """
            ),
            (
                "IsErrNestedInsideLogicalAndBanned",
                "VIB000",
                """
                bool c = true;
                Result<int, int> r = Ok(4);
                if (c && r.IsErr(out var ok, out var e)) { WriteLine(e.Error); }
                """
            ),
            (
                "IsNoneNestedInsideLogicalOrBanned",
                "VIB000",
                """
                bool c = false;
                Option<int> o = Some(4);
                if (c || o.IsNone(out var v)) { WriteLine(0); }
                """
            ),
            (
                "IsNoneExistingLocalBanned",
                "VIB000",
                """
                int v = 0;
                Option<int> o = Some(4);
                if (o.IsNone(out v)) { WriteLine(0); }
                WriteLine(v);
                """
            ),
            (
                "StringImplicitToReadOnlySpanCharBanned",
                "VIB000",
                """
                using System;
                string a = "é🦀";
                ReadOnlySpan<char> s = a;
                WriteLine(s.Length);
                """
            ),
            (
                "StringSwitchStatementBanned",
                "VIB000",
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
                """
            ),
            (
                "StringSwitchExpressionBanned",
                "VIB000",
                """
                string s = "abc";
                int r = s switch { "abc" => 1, _ => 0 };
                WriteLine(r);
                """
            ),
            (
                "StringIsConstantPatternBanned",
                "VIB000",
                """
                string s = "abc";
                bool b = s is "abc";
                WriteLine(b);
                """
            ),
            (
                "OptionStringNestedConstantPatternBanned",
                "VIB000",
                """
                Option<string> os = Some("abc");
                bool b = os is Some<string>("abc");
                WriteLine(b);
                """
            ),
            (
                "EventDeclarationBanned",
                "VIB000",
                """
                using System;
                var b = new Bus();
                WriteLine(b.Count);
                sealed class Bus
                {
                    public int Count;
                    public event Action Tick;
                }
                """
            ),
            (
                "DelegateLocalVariableBanned",
                "VIB000",
                """
                using System;
                static void Nop() { }
                Action a = Nop;
                """
            ),
            (
                "DelegateParameterAndFieldBanned",
                "VIB000",
                """
                using System;
                WriteLine(1);
                sealed class Cb(Action fn)
                {
                    public Action Fn = fn;
                }
                """
            ),
            (
                "CSharp14ExtensionBlockBanned",
                "VIB000",
                """
                WriteLine(5);
                static class Ext
                {
                    extension(int x)
                    {
                        public int Twice() => x * 2;
                    }
                }
                """
            ),
            (
                "SizeOfExpressionBanned",
                "VIB000",
                """
                int n = sizeof(int);
                WriteLine(n);
                """
            ),
            (
                "NameOfExpressionBanned",
                "VIB000",
                """
                int alpha = 1;
                string s = nameof(alpha);
                WriteLine(s);
                """
            ),
            (
                "ParamsParameterBanned",
                "VIB000",
                """
                using System;
                WriteLine(Sum(1, 2));
                static int Sum(params ReadOnlySpan<int> xs) => xs.Length;
                """
            ),
            (
                "ObjectInitializerBanned",
                "VIB000",
                """
                var p = new Pt { X = 3 };
                WriteLine(p.X);
                struct Pt { public int X; }
                """
            ),
            (
                "CompoundAddOperatorInParamBanned",
                "VIB000",
                """
                var a = new Acc(1);
                WriteLine(a.V);
                struct Acc(int v)
                {
                    public int V = v;
                    public void operator +=(in int d) { V += d; }
                }
                """
            ),
            (
                "NonRefPropertyUserDefinedCompoundAddBanned",
                "VIB000",
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
                """
            ),
            (
                "NonRefPropertyUserDefinedCompoundAddBranchingRhsBanned",
                "VIB000",
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
                """
            ),
            // Round-3 P1-1 / P1-2 / Fable P1-3 / Preprocessor / Bool switch / Console.WriteLine(char) rejections
            (
                "IntAssignStringBanned",
                "VIB000",
                """
                int x = "hello";
                WriteLine(x);
                """
            ),
            (
                "LongToNUIntBanned",
                "VIB000",
                """
                long v = 1L;
                nuint u = v;
                WriteLine((ulong)u);
                """
            ),
            (
                "IntDivAssignLongBanned",
                "VIB000",
                """
                int x = 10;
                long d = 4294967298L;
                x /= d;
                WriteLine(x);
                """
            ),
            (
                "UnaryMinusULongBanned",
                "VIB000",
                """
                ulong u = 5UL;
                var x = -u;
                WriteLine(x);
                """
            ),
            (
                "UnaryMinusULongLiteralBanned",
                "VIB000",
                """
                var x = -5UL;
                WriteLine(x);
                """
            ),
            (
                "UnaryMinusHexMinLongBanned",
                "VIB000",
                """
                var x = -0x8000000000000000UL;
                WriteLine(x);
                """
            ),
            (
                "UnaryMinusNUIntBanned",
                "VIB000",
                """
                nuint u = 5;
                var x = -u;
                WriteLine((long)x);
                """
            ),
            (
                "NIntPlusUIntBanned",
                "VIB000",
                """
                nint a = 1;
                uint b = 2u;
                var c = a + b;
                WriteLine((long)c);
                """
            ),
            (
                "NUIntPlusIntVarBanned",
                "VIB000",
                """
                nuint a = 1;
                int b = 2;
                var c = a + b;
                WriteLine((ulong)c);
                """
            ),
            (
                "PreprocessorErrorDirective",
                "VIB000",
                """
                #error active failure
                WriteLine(1);
                """
            ),
            (
                "AmbiguousOverloadedIndexer",
                "VIB000",
                """
                var g = new Grid(0);
                WriteLine(g[1, 2]);
                struct Grid(int v)
                {
                    private int _v = v;
                    public int this[int a, long b] => _v + a + (int)b;
                    public int this[long a, int b] => _v + (int)a + b;
                }
                """
            ),
            (
                "NonExhaustiveBoolSwitchExpression",
                "VIB000",
                """
                bool b = true;
                int x = b switch { true => 1 };
                WriteLine(x);
                """
            ),
            (
                "ConsoleWriteLineCharBanned",
                "VIB000",
                """
                Console.WriteLine('a');
                """
            ),
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r3-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            foreach (var tc in cases)
            {
                string srcPath = Path.Combine(tempDir, $"{tc.Name}.cs");
                File.WriteAllText(srcPath, tc.Code);

                var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
                Assert.True(exitCode != 0, $"Case '{tc.Name}' unexpectedly succeeded.");
                string combined = stdout + "\n" + stderr;
                Assert.True(
                    combined.Contains(tc.ExpectedId, StringComparison.Ordinal),
                    $"Case '{tc.Name}' expected {tc.ExpectedId}, got:\n{combined}");
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

    private static (int ManagedExit, string ManagedOut, string ManagedErr, int NativeExit, string NativeOut, string NativeErr)
        CompileAndRunSelfHostMultiFileDifferential((string FileName, string Code)[] files, bool withAsan = true)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r4-mf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePaths = new List<string>(files.Length);
            foreach (var (fileName, code) in files)
            {
                string fullPath = Path.Combine(tempDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, code);
                filePaths.Add(fullPath);
            }

            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(tempDir, "SelfDiffR4Multi", includeStd: true);
            string bootstrapPath = Path.Combine(RepoRoot, "src", "Vibranium.Core", "Bootstrap", "VibraniumAppBootstrap.cs");
            var managedTrees = compilation.SyntaxTrees.ToList();
            if (File.Exists(bootstrapPath))
            {
                managedTrees.Add(CSharpSyntaxTree.ParseText(
                    File.ReadAllText(bootstrapPath),
                    new CSharpParseOptions(LanguageVersion.CSharp15),
                    path: bootstrapPath));
            }
            var managedComp = CSharpCompilation.Create(
                "SelfDiffR4Multi",
                managedTrees,
                compilation.References,
                compilation.Options);

            string dllPath = Path.Combine(tempDir, "SelfDiffR4Multi.dll");
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
            File.WriteAllText(Path.Combine(tempDir, "SelfDiffR4Multi.runtimeconfig.json"), runtimeConfig);

            string llPath = Path.Combine(tempDir, "out.ll");
            var emitArgs = new List<string> { VibSelfDllPath, "emit-ll" };
            emitArgs.AddRange(filePaths);
            emitArgs.AddRange(new[] { "--sysroot", SysrootPath, "-o", llPath });
            var (emitExit, emitOut, emitErr) = RunProcess(DotnetExe, emitArgs.ToArray());
            Assert.True(emitExit == 0, $"vib-self emit-ll failed:\n{emitOut}\n{emitErr}");

            var (verExit, _, verErr) = RunProcess("opt-18", "-passes=verify", "-disable-output", llPath);
            Assert.True(verExit == 0, $"opt-18 verify failed:\n{verErr}");

            string nativeBin = Path.Combine(tempDir, "self_native_r4");
            var buildArgs = new List<string> { VibSelfDllPath, "build" };
            buildArgs.AddRange(filePaths);
            buildArgs.AddRange(new[] { "--sysroot", SysrootPath, "--cc", "clang-18", "-O3" });
            if (withAsan) buildArgs.Add("--asan");
            buildArgs.Add("-o");
            buildArgs.Add(nativeBin);
            var (buildExit, buildOut, buildErr) = RunProcess(DotnetExe, buildArgs.ToArray());
            Assert.True(buildExit == 0, $"vib-self build failed:\n{buildOut}\n{buildErr}");

            var (mExit, mOut, mErr) = RunProcess(DotnetExe, dllPath);
            var (nExit, nOut, nErr) = RunProcess(nativeBin);
            return (mExit, mOut, mErr, nExit, nOut, nErr);
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
    public void SelfHost_Round4_Casts_FixedWidthWidening_LocalConstEval_ZeroFloatEnum_SwitchWhen_TernaryIs_And_InlineOut_Differential()
    {
        const string source = """
            // 1. Identity casts, explicit union lifts, and shared (ReadOnlySpan<T>)s reborrow
            int idInt = (int)42;
            var pair = (Pair)new Pair(3, 4);
            var holder = (Holder)new Holder(9);
            Vec<int> v = (Vec<int>)[10, 20, 30];
            Span<int> s = v.AsSpan();
            var ro = (ReadOnlySpan<int>)s;
            WriteLine(idInt + pair.A + pair.B + holder.V);
            WriteLine(ro[0]);
            WriteLine(s[1]);
            s[2] = 77;
            WriteLine(s[2]);
            var optSome = (Option<int>)Some(5);
            var optNone = (Option<int>)None;
            var resOk = (Result<int, int>)Ok(11);
            var resErr = (Result<int, int>)Err(13);
            WriteLine(optSome.UnwrapOr(-1) + optNone.UnwrapOr(100) + resOk.UnwrapOr(0) + (resErr.IsOk ? 0 : 13));
            // 2. Explicit numeric & enum casts
            Flags fB = (Flags)2;
            AltFlags af = (AltFlags)fB;
            double fd = (double)fB;
            Flags fFromFloat = (Flags)3.9;
            char chA = (char)65;
            byte byA = (byte)chA;
            WriteLine((int)af * 100 + (int)fd * 10 + (int)fFromFloat + byA);
            // 3. nint / nuint implicit widening to long / ulong & binary promotion
            long ni = -12;
            long wideL = ni;
            long sumNi = ni + 20L;
            ulong nu = 15;
            ulong wideU = nu;
            ulong sumNu = nu + 5UL;
            WriteLine(wideL + sumNi);
            WriteLine(wideU + sumNu);
            // 4. Recursive local const evaluation & unchecked wraparound
            const int cx = 250;
            const int cy = ((cx + 10) >> 1) ^ 3;
            byte narrowedByte = cx + 5;
            const int ushrNeg = (-4) >>> 1;
            const uint maxU = 4294967295;
            uint wrapU = unchecked(maxU * 2);
            int wrapI;
            unchecked
            {
                const int maxI = int.MaxValue;
                wrapI = maxI + 1;
            }

            WriteLine(cy + narrowedByte);
            WriteLine(ushrNeg);
            WriteLine(wrapU);
            WriteLine(wrapI == int.MinValue);
            // 5. Numeric zero constants (including 0.0 / 0.0f / const double 0.0) -> enum
            Flags z1 = 0;
            Flags z2 = 0.0;
            Flags z3 = 0.0f;
            const double zConst = 0.0;
            Flags z4 = zConst;
            WriteLine((int)z1 + (int)z2 + (int)z3 + (int)z4);
            // 6. Ternary after `is Type` pattern (`n is NoneType ? 1 : 2`)
            Option<int> on = None;
            Option<int> os = Some(11);
            Result<int, int> rok = Ok(22);
            int k1 = on is NoneType ? 1 : 2;
            int k2 = os is Some<int> ? 10 : 20;
            int k3 = rok is Ok<int> ? 100 : 200;
            int k4 = on is not NoneType ? 1000 : 2000;
            int ta = 7, tb = 9;
            int k5 = on is NoneType ? ta : tb;
            WriteLine(k1 + k2 + k3 + k4 + k5);
            // 7. Switch expression `when` guards with bare & parenthesized identifiers + pattern vars
            bool guardFlag = true;
            int sx = 15;
            int sw1 = sx switch
            {
                var cutValue0 when ((cutValue0 > 10)) && (guardFlag) => 1,
                _ => 3
            };
            int sw2 = sx switch
            {
                var cutValue1 when ((cutValue1 > 10)) && ((guardFlag)) => 2,
                _ => 4
            };
            var sw3 = os switch
            {
                Some<int> (var q)when guardFlag => q + 4,
                _ => -1
            };
            WriteLine(sw1 * 100 + sw2 * 10 + sw3);
            // 8. Inline `out var` / `out T` in ternary, binary, and multi-arg expressions
            WriteLine(TryGet(5, out int outPos) ? outPos : -outPos);
            WriteLine(TryGet(-6, out int outNeg) ? outNeg : -outNeg);
            WriteLine(PickOut(out var outA) + outA);
            WriteLine(CombineTwo(PickOut(out var outB), outB));
            static bool TryGet(int v, out int doubled)
            {
                doubled = v * 2;
                return v > 0;
            }

            static int PickOut(out long x)
            {
                x = 30L;
                return 12;
            }

            static long CombineTwo(int a, long b) => a * 10L + b;
            enum Flags
            {
                Z = 0,
                A = 1,
                B = 2,
                C = 3
            }

            enum AltFlags
            {
                X = 0,
                Y = 2
            }

            readonly struct Pair(int a, int b)
            {
                public readonly int A = a;
                public readonly int B = b;
            }

            sealed class Holder(int v)
            {
                public int V = v;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(source, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedErr);
        Assert.Equal(string.Empty, res.NativeErr);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelfHost_Round4_NamespaceAndGlobalTypeShadowing_PreservesCoreAndStd_Differential()
    {
        var files = new (string FileName, string Code)[]
        {
            (
                "GlobalShadows.cs",
                """
                public readonly struct Bytes(int v) { public readonly int V = v; }
                public readonly struct Utf8(int v) { public readonly int V = v; }
                public readonly struct Sys(int v) { public readonly int V = v; }
                public readonly struct ConsoleIo(int v) { public readonly int V = v; }
                public readonly struct Unit(int v) { public readonly int V = v; }
                public sealed class String(int v) { public int V = v; }
                public static class Int32
                {
                    public static bool TryParse(string s, out int x)
                    {
                        _ = s;
                        x = 777;
                        return true;
                    }
                }
                public readonly struct Prelude(int v) { public readonly int V = v; }
                public readonly struct Option<T>(T v) { public readonly T V = v; }
                public readonly struct Result<T, E>(T ok, E err) { public readonly T OkV = ok; public readonly E ErrV = err; }
                public sealed class Vec<T>(T item) { public T Item = item; }
                public readonly struct File(int v) { public readonly int V = v; }
                public readonly struct None(int v) { public readonly int V = v; }
                """
            ),
            (
                "ScopedShadows.cs",
                """
                namespace CustomScope;

                public static class Console
                {
                    public static int WriteLine(int x) => x + 100;
                }

                public static class Io
                {
                    public static int WriteLine(int x) => x + 200;
                }

                public static class Stdio
                {
                    public static int WriteOut(int x) => x + 300;
                }

                public static class VibraniumRuntime
                {
                    public static int Panic(int x) => x + 400;
                }

                public static class StringUtf8Extensions
                {
                    public static int AsUtf8(int x) => x + 500;
                }

                public static class Utf8SpanExtensions
                {
                    public static int Lines(int x) => x + 600;
                }

                public static class Int64
                {
                    public static int TryParse(int x) => x + 700;
                }

                public static class MemoryExtensions
                {
                    public static int SequenceEqual(int x) => x + 800;
                }

                public readonly struct Span<T>(T v) { public readonly T V = v; }
                public readonly struct ReadOnlySpan<T>(T v) { public readonly T V = v; }

                public static class Runner
                {
                    public static int RunScoped()
                    {
                        int acc = Console.WriteLine(1)
                                + Io.WriteLine(2)
                                + Stdio.WriteOut(3)
                                + VibraniumRuntime.Panic(4)
                                + StringUtf8Extensions.AsUtf8(5)
                                + Utf8SpanExtensions.Lines(6)
                                + Int64.TryParse(7)
                                + MemoryExtensions.SequenceEqual(8);
                        var sp = new Span<int>(9);
                        var ro = new ReadOnlySpan<int>(10);

                        // Qualified System / Vibranium / Vibranium.Std still resolve to real built-ins
                        var realVec = new global::Vibranium.Vec<int>();
                        realVec.Add(sp.V + ro.V);
                        global::System.ReadOnlySpan<byte> utf8 = global::Vibranium.StringUtf8Extensions.AsUtf8("42");
                        int parsed = 0;
                        if (int.TryParse(utf8, out int pv)) parsed = pv;

                        global::System.Console.WriteLine(acc + realVec[0] + parsed);
                        return acc;
                    }
                }
                """
            ),
            (
                "Program.cs",
                """
                public static class AppEntry
                {
                    public static void Main()
                    {
                        var b = new Bytes(1);
                        var u = new Utf8(2);
                        var sy = new Sys(3);
                        var ci = new ConsoleIo(4);
                        var un = new Unit(5);
                        var st = new String(6);
                        var pr = new Prelude(7);
                        var op = new Option<int>(8);
                        var rs = new Result<int, int>(9, 10);
                        var vc = new Vec<int>(11);
                        var fl = new File(12);
                        var nn = new None(13);

                        _ = Int32.TryParse("123", out int customParsed);
                        _ = int.TryParse("123", out int realParsed);
                        _ = global::System.Int32.TryParse("456", out int sysParsed);
                        string kwStr = "ok";

                        int sum = b.V + u.V + sy.V + ci.V + un.V + st.V + pr.V + op.V + rs.OkV + rs.ErrV + vc.Item + fl.V + nn.V;
                        global::System.Console.WriteLine(sum);
                        global::System.Console.WriteLine(customParsed + realParsed + sysParsed + global::Vibranium.Std.Bytes.Eq( global::Vibranium.StringUtf8Extensions.AsUtf8(kwStr), "ok"u8 ).ToString().Length * 0);
                        _ = CustomScope.Runner.RunScoped();
                    }
                }
                """
            ),
        };

        // Note: avoid `.ToString()` in Program.cs since Vibranium bans ToString; replace with ternary!
        files[2] = (
            "Program.cs",
            """
            public static class AppEntry
            {
                public static void Main()
                {
                    var b = new Bytes(1);
                    var u = new Utf8(2);
                    var sy = new Sys(3);
                    var ci = new ConsoleIo(4);
                    var un = new Unit(5);
                    var st = new String(6);
                    var pr = new Prelude(7);
                    var op = new Option<int>(8);
                    var rs = new Result<int, int>(9, 10);
                    var vc = new Vec<int>(11);
                    var fl = new File(12);
                    var nn = new None(13);

                    _ = Int32.TryParse("123", out int customParsed);
                    _ = int.TryParse("123", out int realParsed);
                    _ = global::System.Int32.TryParse("456", out int sysParsed);
                    string kwStr = "ok";
                    int eqBit = global::Vibranium.Std.Bytes.Eq(global::Vibranium.StringUtf8Extensions.AsUtf8(kwStr), "ok"u8) ? 1 : 0;

                    int sum = b.V + u.V + sy.V + ci.V + un.V + st.V + pr.V + op.V + rs.OkV + rs.ErrV + vc.Item + fl.V + nn.V;
                    global::System.Console.WriteLine(sum);
                    global::System.Console.WriteLine(customParsed + realParsed + sysParsed + eqBit);
                    _ = CustomScope.Runner.RunScoped();
                }
            }
            """
        );

        var res = CompileAndRunSelfHostMultiFileDifferential(files, withAsan: true);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(0, res.NativeExit);
        Assert.Equal(string.Empty, res.ManagedErr);
        Assert.Equal(string.Empty, res.NativeErr);
        Assert.Equal(res.ManagedOut.Replace("\r\n", "\n"), res.NativeOut.Replace("\r\n", "\n"));
        Assert.Equal("91\n1357\n3697\n", res.NativeOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelfHost_Round4_InvalidCasts_ObjectBan_WrapperMismatches_ConstOverflow_And_ShadowedDisposable_Rejected()
    {
        (string Name, string ExpectedId, string Code)[] cases =
        [
            // Item 1.2: Invalid explicit casts
            (
                "ClassToClassCastBanned",
                "VIB000",
                """
                var a = new A(1);
                var b = (B)a;
                WriteLine(b.X);
                sealed class A(int x) { public int X = x; }
                sealed class B(int x) { public int X = x; }
                """
            ),
            (
                "BoolToIntCastBanned",
                "VIB000",
                """
                int x = (int)true;
                WriteLine(x);
                """
            ),
            (
                "IntToBoolCastBanned",
                "VIB000",
                """
                bool b = (bool)1;
                WriteLine(b);
                """
            ),
            (
                "EnumToBoolCastBanned",
                "VIB000",
                """
                bool b = (bool)Flags.A;
                WriteLine(b);
                enum Flags { A = 1 }
                """
            ),
            (
                "BoolToEnumCastBanned",
                "VIB000",
                """
                Flags f = (Flags)true;
                WriteLine((int)f);
                enum Flags { A = 1 }
                """
            ),
            (
                "StringToIntCastBanned",
                "VIB000",
                """
                int x = (int)"hi";
                WriteLine(x);
                """
            ),
            (
                "IntToStructCastBanned",
                "VIB000",
                """
                var s = (S)1;
                WriteLine(s.X);
                readonly struct S(int x) { public readonly int X = x; }
                """
            ),
            (
                "VecToSpanExplicitCastBanned",
                "VIB000",
                """
                Vec<int> v = [1, 2];
                var s = (Span<int>)v;
                WriteLine(s.Length);
                """
            ),
            // Item 1.3 (Fable P2-E): Ban `object` and `System.Object`
            (
                "ObjectLocalBanned",
                "VIB000",
                """
                object x = 5;
                _ = x;
                """
            ),
            (
                "SystemObjectParamBanned",
                "VIB000",
                """
                WriteLine(Take(1));
                static int Take(System.Object o) { _ = o; return 0; }
                """
            ),
            (
                "IsObjectPatternBanned",
                "VIB000",
                """
                int x = 5;
                if (x is object) WriteLine(1);
                """
            ),
            (
                "IsObjectVarPatternBanned",
                "VIB000",
                """
                int x = 5;
                if (x is object o) { _ = o; WriteLine(1); }
                """
            ),
            // Item 2a: Exact union wrapper payload equality
            (
                "SomeIntToOptionLongBanned",
                "VIB000",
                """
                Option<long> o = Some(1);
                WriteLine(o.IsSome);
                """
            ),
            (
                "OkIntToResultLongBanned",
                "VIB000",
                """
                Result<long, int> r = Ok(1);
                WriteLine(r.IsOk);
                """
            ),
            (
                "ErrIntToResultErrLongBanned",
                "VIB000",
                """
                Result<int, long> r = Err(1);
                WriteLine(r.IsOk);
                """
            ),
            (
                "ExplicitCastSomeIntToOptionLongBanned",
                "VIB000",
                """
                var o = (Option<long>)Some(1);
                WriteLine(o.IsSome);
                """
            ),
            // Item 2b: Implicit Vec<T> -> Span<T>/ReadOnlySpan<T> banned
            (
                "ImplicitVecToSpanVarBanned",
                "VIB000",
                """
                Vec<int> v = [1, 2, 3];
                Span<int> s = v;
                WriteLine(s.Length);
                """
            ),
            (
                "ImplicitVecToReadOnlySpanArgBanned",
                "VIB000",
                """
                Vec<int> v = [1, 2, 3];
                WriteLine(Len(v));
                static int Len(ReadOnlySpan<int> s) => s.Length;
                """
            ),
            // Item 2c: Generic method inference across wrapper / Vec boundaries banned
            (
                "GenericInferOptionFromSomeBanned",
                "VIB000",
                """
                WriteLine(UnwrapOrZero(Some(5)));
                static T UnwrapOrZero<T>(Option<T> opt) where T : unmanaged => opt.UnwrapOrDefault();
                """
            ),
            (
                "GenericInferResultFromOkBanned",
                "VIB000",
                """
                WriteLine(UnwrapOk(Ok(5)));
                static T UnwrapOk<T>(Result<T, int> res) where T : unmanaged => res.UnwrapOrDefault();
                """
            ),
            (
                "GenericInferSpanFromVecBanned",
                "VIB000",
                """
                Vec<int> v = [1, 2];
                WriteLine(SpanCount(v));
                static int SpanCount<T>(ReadOnlySpan<T> s) => s.Length;
                """
            ),
            // Item 3b & 3c: Constant integer overflow (CS0220), div-by-zero (CS0020), non-zero float to enum
            (
                "CheckedConstAddOverflowBanned",
                "VIB000",
                """
                int x = int.MaxValue + 1;
                WriteLine(x);
                """
            ),
            (
                "CheckedConstUIntMulOverflowBanned",
                "VIB000",
                """
                const uint m = 4294967295;
                uint x = m * 2;
                WriteLine(x);
                """
            ),
            (
                "CheckedConstUnaryNegMinIntBanned",
                "VIB000",
                """
                const int mn = int.MinValue;
                int x = -mn;
                WriteLine(x);
                """
            ),
            (
                "ConstDivByZeroInUncheckedBanned",
                "VIB000",
                """
                const int a = 10;
                int x = unchecked(a / (1 - 1));
                WriteLine(x);
                """
            ),
            (
                "NonZeroFloatToEnumBanned",
                "VIB000",
                """
                E e = 1.0;
                WriteLine((int)e);
                enum E { A = 0, B = 1 }
                """
            ),
            // Item 4: Unqualified `: IDisposable` shadowed by user type
            (
                "ShadowedIDisposableBaseBanned",
                "VIB000",
                """
                WriteLine(1);
                sealed class IDisposable { }
                sealed class Res : IDisposable
                {
                    public void Dispose() { }
                }
                """
            ),
            // Item 5 (P1-C): `if (x is int? y)` still emits VIB008
            (
                "IsNullableIntPatternBanned",
                "VIB008",
                """
                int x = 5;
                if (x is int? y)
                {
                    _ = y;
                }
                """
            ),
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r4-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            foreach (var tc in cases)
            {
                string srcPath = Path.Combine(tempDir, $"{tc.Name}.cs");
                File.WriteAllText(srcPath, tc.Code);

                var c0Comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Stage0R4_{tc.Name}", includeStd: true);
                var c0Outcome = VibraniumCompilerDriver.CheckAndEmitIr(c0Comp);
                Assert.False(c0Outcome.Success, $"Case '{tc.Name}' unexpectedly succeeded in Stage-0.");
                if (tc.ExpectedId != "VIB000")
                {
                    var (_, c0VibDiags) = Vibranium.Frontend.Roslyn.VibraniumFrontend.CompileAndCheck(c0Comp);
                    Assert.Contains(c0VibDiags, d => d.Id == tc.ExpectedId);
                }

                var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
                Assert.True(exitCode != 0, $"Case '{tc.Name}' unexpectedly succeeded in vib-self.");
                string combined = stdout + "\n" + stderr;
                Assert.True(
                    combined.Contains(tc.ExpectedId, StringComparison.Ordinal),
                    $"Case '{tc.Name}' expected {tc.ExpectedId} from vib-self, got:\n{combined}");
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
    public void SelfHost_Round5_TrackB_ConstCasts_ExhaustiveSwitches_Patterns_CommonType_UncheckedLocalFuncs_And_ContextualIdents_Differential()
    {
        const string source = """
            // 1. Float & enum compile-time constant explicit casts + runtime float saturation
            const double posD = 3.9;
            const float negF = -3.9f;
            int truncPos = (int)posD;
            int truncNeg = (int)negF;
            char chA = (char)65.8;
            BigEnum eTwo = (BigEnum)2.9;
            byte unchk300 = unchecked((byte)300.0);
            int unchk3B = unchecked((int)3000000000.0);
            byte unchk200 = unchecked((byte)200.9);
            int unchkHuge = unchecked((int)1e30);
            int unchkNan = unchecked((int)double.NaN);
            uint unchkNegU = unchecked((uint)-1.5);
            ulong unchkInfU = unchecked((ulong)double.PositiveInfinity);
            byte unchkEnumTrunc = unchecked((byte)BigEnum.Big);
            BigEnum unchkBigEnum = unchecked((BigEnum)4294967296L);
            double rtHuge = 1e30;
            double rtNeg = -5.5;
            int satMax = (int)rtHuge;
            uint satZero = (uint)rtNeg;
            WriteLine(truncPos + truncNeg + (int)chA + (int)eTwo + unchk300 + unchk3B + unchk200 + unchkHuge + unchkNan + (int)unchkNegU + (int)unchkInfU + unchkEnumTrunc + (int)unchkBigEnum);
            WriteLine(satMax == int.MaxValue && satZero == 0 ? 111 : 0);
            // 2. Local const evaluator (?: short-circuit, relational/bool, float/double const, cast truncation)
            const int zeroDiv = 0;
            int safeCond = Round12Runtime.Value(zeroDiv == 0) ? 42 : 99;
            int safeCondFalse = Round12Runtime.Value(zeroDiv != 0) ? 11 : 22;
            const bool relConst = 1 < 2 && 3.5 >= 3 && 'a' < 'b' && BigEnum.Two == BigEnum.Two && (false || true) && !(2 > 5);
            double calcD = (Round12Runtime.Value(relConst) ? 2.5 : 9.0) * 4.0 - 1.0;
            const float calcF = 1.25f + 2.75f;
            const int maskConst = unchecked((byte)257);
            WriteLine(safeCond + safeCondFalse + (Round12Runtime.Value(relConst) ? 10 : 0) + (int)calcD + (int)calcF + maskConst);
            // 3 & 4. Nested [Union] & 64-bit interval switch exhaustiveness + class const bool `not` coverage
            Option<int> innerSome15 = Some(15);
            Option<int> innerNone = None;
            Option<Option<int>> nn1 = Some(innerSome15);
            Option<Option<int>> nn2 = Some(innerNone);
            Option<Option<int>> nn3 = None;
            WriteLine(EvalNestedOpt(nn1) + EvalNestedOpt(nn2) + EvalNestedOpt(nn3));
            Result<Option<int>, int> rn1 = Ok(innerSome15);
            Result<Option<int>, int> rn2 = Ok(innerNone);
            Result<Option<int>, int> rn3 = Err(7);
            WriteLine(EvalNestedRes(rn1) + EvalNestedRes(rn2) + EvalNestedRes(rn3));
            WriteLine(ClassifyInt(-5) + ClassifyInt(0) + ClassifyInt(9) + ClassifyOptRange(Some(-3)) + ClassifyOptRange(Some(4)) + ClassifyOptRange(None));
            WriteLine(ClassifyBounds(int.MaxValue) + ClassifyBounds(0) + ClassifyByte(50) + ClassifyByte(200) + ClassifyULong(5UL) + ClassifyULong(99UL));
            WriteLine(EvalBoolNotConst(true) * 100 + EvalBoolNotConst(false));
            // 5. Nested [Union] Move(...) ownership, wrapper bindings, parenthesized patterns, standalone `is` scope
            Option<Vec<int>> innerVecOpt = Some<Vec<int>>([10, 20]);
            Option<Option<Vec<int>>> nestedVec = Some(innerVecOpt);
            if (Move(nestedVec)is Some<Option<Vec<int>>> (Some<Vec<int>> (var movedInner)))
            {
                movedInner.Add(30);
                WriteLine(movedInner.Count + movedInner[2]);
            }

            Option<int> wrapOpt = Some(33);
            Result<int, int> wrapOk = Ok(44);
            Result<int, int> wrapErr = Err(55);
            int wrapAcc = 0;
            if (wrapOpt is Some<int> sOnly)
                wrapAcc += sOnly.Value;
            if (wrapOpt is Some<int> (var innerW) sBoth)
                wrapAcc += innerW + sBoth.Value;
            if (wrapOk is Ok<int> okOnly)
                wrapAcc += okOnly.Value;
            if (wrapErr is Err<int> errOnly)
                wrapAcc += errOnly.Error;
            if (wrapOpt is (NoneType) or (Some<int> (33)))
                wrapAcc += 1000;
            WriteLine(wrapAcc);
            int pVal = 42;
            Pair pair = new Pair(3, 4);
            bool parenPrim = pVal is (int parenPrimValue);
            bool parenStruct = pair is (Pair parenStructValue);
            bool parenCombo = (pVal is var cutValue0) && ((((cutValue0 > 0) && (((cutValue0 < 100) || (cutValue0 is 100))))));
            WriteLine((parenPrim ? 1 : 0) + (parenStruct ? 10 : 0) + (parenCombo ? 100 : 0));
            bool standOk = pVal is int standY;
            if (standOk)
                WriteLine(standY);
            bool standAnd = wrapOpt is Some<int> (var standZ) && standZ == 33;
            WriteLine(standAnd ? 88 : 0);
            if (pVal is int ifScopedOnly)
            {
                _ = ifScopedOnly;
            }

            int ifScopedOnly2 = 9;
            WriteLine(ifScopedOnly2);
            // 6. Option<Class>.AsMut(), best-common-type inference, unchecked inheritance into local functions
            Option<Box> boxOpt = Some(new Box(12));
            if (boxOpt.AsMut()is Some<Box> (var mutBox))
            {
                mutBox.V = 77;
            }

            WriteLine(boxOpt.AsRef()is Some<Box> (var roBox) ? roBox.V : 0);
            bool chooseFirst = false;
            var widenedCond = chooseFirst ? 5 : 10000000000L;
            int swKey = 2;
            var widenedSw = swKey switch
            {
                1 => 5,
                _ => 20000000000L
            };
            Option<int> baseOpt = Some(9);
            var commonOpt = swKey switch
            {
                1 => None,
                3 => Some(10),
                _ => baseOpt
            };
            Option<int> commonLiftOpt = chooseFirst ? Some(10) : None;
            Result<int, int> baseRes = Ok(11);
            var commonRes = swKey switch
            {
                1 => baseRes,
                _ => Err(22)};
            Span<byte> spMut = [1, 2, 3];
            ReadOnlySpan<byte> spRo = "xy"u8;
            var commonSpan = chooseFirst ? spMut : spRo;
            var commonEnum = chooseFirst ? 0 : BigEnum.Two;
            WriteLine(widenedCond + widenedSw + commonOpt.UnwrapOr(0) + commonLiftOpt.UnwrapOr(5) + (commonRes.IsOk ? 0 : 22) + commonSpan.Length + (int)commonEnum);
            unchecked
            {
                static int OverflowInLocal(int a) => a + 1;
                WriteLine(OverflowInLocal(int.MaxValue) == int.MinValue ? 555 : 0);
            }

            // 7. Contextual C# keywords as identifiers
            int scoped = 1;
            int partial = 2;
            int var = 3;
            int where = 4;
            int yield = 5;
            int async = 6;
            int global = 7;
            int dynamic = 8;
            var ctxHolder = new ContextualHolder(scoped + partial + var + where, yield + async + global + dynamic);
            int usingScopedVal = 0;
            {
                using (var scopedRes = new ScopedRes(11))
                {
                    usingScopedVal += scopedRes.Id;
                }
            }

            usingScopedVal += CheckScopedUsingIdent();
            WriteLine(ctxHolder.Sum(10) + usingScopedVal);
            static int CheckScopedUsingIdent()
            {
                using (var scoped = new ScopedRes(13))
                {
                    return scoped.Id;
                }
            }

            static int EvalNestedOpt(Option<Option<int>> o) => o switch
            {
                Some<Option<int>> (Some<int> (var v)) => v,
                Some<Option<int>> (NoneType) => 3,
                NoneType => 1,
            };
            static int EvalNestedRes(Result<Option<int>, int> r) => r switch
            {
                Ok<Option<int>> (Some<int> (var v)) => v,
                Ok<Option<int>> (NoneType) => 4,
                Err<int> (var e) => e,
            };
            static int ClassifyInt(int x) => x switch
            {
                var cutValue1 when (cutValue1 < 0) => 10,
                0 => 20,
                var cutValue2 when (cutValue2 > 0) => 30,
                _ => default
            };
            static int ClassifyOptRange(Option<int> o) => o switch
            {
                Some<int> (var cutValue3)when ((cutValue3 < 0)) => 1,
                Some<int> (var cutValue4)when ((cutValue4 >= 0)) => 2,
                NoneType => 3,
                _ => default
            };
            static int ClassifyBounds(int x) => x switch
            {
                int.MaxValue => 7,
                var cutValue5 when (cutValue5 < int.MaxValue) => 2,
                _ => default
            };
            static int ClassifyByte(byte b) => b switch
            {
                var cutValue6 when (cutValue6 <= 100) => 4,
                var cutValue7 when (cutValue7 > 100) => 6,
                _ => default
            };
            static int ClassifyULong(ulong u) => u switch
            {
                0UL => 1,
                not 0UL => 8,
            };
            static int EvalBoolNotConst(bool b) => b switch
            {
                not BoolConsts.T => 1,
                _ => 2,
            };
            public enum BigEnum
            {
                Zero = 0,
                Two = 2,
                Big = 300,
            }

            public static class BoolConsts
            {
                public const bool T = true;
            }

            public readonly struct Pair(int a, int b)
            {
                public readonly int A = a;
                public readonly int B = b;
            }

            public sealed class Box(int v)
            {
                public int V = v;
            }

            public sealed class ScopedRes(int id) : System.IDisposable
            {
                public int Id = id;
                public void Dispose()
                {
                    Id = 0;
                }
            }

            public readonly struct ContextualHolder(int scoped, int partial)
            {
                public readonly int ScopedField = scoped;
                public readonly int PartialField = partial;
                public int Sum(int where) => ScopedField + PartialField + where;
            }

            static class Round12Runtime
            {
                public static T Value<T>(T value) => value;
            }
            """;

        string stage0Dir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r5-s0-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage0Dir);
        try
        {
            string s0Path = Path.Combine(stage0Dir, "Program.cs");
            File.WriteAllText(s0Path, source);
            var s0Comp = VibraniumCompilerDriver.CreateCompilationFromPath(s0Path, "Stage0R5Pos", includeStd: true);
            var s0Out = VibraniumCompilerDriver.CheckAndEmitIr(s0Comp);
            Assert.True(s0Out.Success, string.Join("\n", s0Out.Errors));
        }
        finally
        {
            if (Directory.Exists(stage0Dir))
            {
                Directory.Delete(stage0Dir, recursive: true);
            }
        }

        var res = CompileAndRunSelfHostDifferential(source);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round5_TrackB_CheckedFloatAndEnumCasts_NonConstInit_DuplicateOrPartialSwitches_BareIsType_AsMutValueType_LocalFuncOverload_And_UsingAmbiguity_Rejected()
    {
        (string Name, string ExpectedId, string Code)[] cases =
        [
            // Item 1: Checked float & enum constant explicit cast overflow -> VIB000 (CS0221)
            (
                "CheckedFloatByteOverflowCast",
                "VIB000",
                """
                byte b = (byte)300.5;
                WriteLine(b);
                """
            ),
            (
                "CheckedFloatOverflowCast",
                "VIB000",
                """
                int x = (int)1e30;
                WriteLine(x);
                """
            ),
            (
                "CheckedFloatNaNCast",
                "VIB000",
                """
                int x = (int)double.NaN;
                WriteLine(x);
                """
            ),
            (
                "CheckedFloatNegToUintCast",
                "VIB000",
                """
                uint x = (uint)-1.5;
                WriteLine((int)x);
                """
            ),
            (
                "CheckedEnumToByteOverflow",
                "VIB000",
                """
                byte b = (byte)BigEnum.Big;
                WriteLine(b);
                public enum BigEnum { Big = 300 }
                """
            ),
            (
                "CheckedLongToEnumOverflow",
                "VIB000",
                """
                BigEnum e = (BigEnum)4294967296L;
                WriteLine((int)e);
                public enum BigEnum { Big = 300 }
                """
            ),
            // Item 2: Non-constant local `const` initializer -> VIB000 (CS0133)
            (
                "NonConstLocalInitializer",
                "VIB000",
                """
                int runtimeVal = 5;
                const int badConst = runtimeVal + 1;
                WriteLine(badConst);
                """
            ),
            // Item 3: Duplicate 2-arm [Union] switch & non-exhaustive nested/interval switches -> VIB000
            (
                "DuplicateOptionNoneArms",
                "VIB000",
                """
                Option<int> o = Some(1);
                int r = o switch { NoneType => 1, NoneType => 2 };
                WriteLine(r);
                """
            ),
            (
                "DuplicateResultOkArms",
                "VIB000",
                """
                Result<int, int> res = Err(5);
                int r = res switch { Ok<int>(var a) => a, Ok<int>(var b) => b + 1 };
                WriteLine(r);
                """
            ),
            (
                "NonExhaustiveNestedOptionSwitch",
                "VIB000",
                """
                Option<int> inner = Some(1);
                Option<Option<int>> o = Some(inner);
                int r = o switch
                {
                    Some<Option<int>>(Some<int>(var v)) => v,
                    NoneType => 0,
                };
                WriteLine(r);
                """
            ),
            (
                "NonExhaustiveIntIntervalGap",
                "VIB000",
                """
                int x = 0;
                int r = x switch
                {
                    < 0 => 1,
                    > 0 => 2,
                };
                WriteLine(r);
                """
            ),
            // Item 5: Bare unparenthesized non-union `is Type` (`IIsTypeOperation`) -> VIB000
            (
                "BareIsStructTypeBanned",
                "VIB000",
                """
                Pair p = new Pair(1, 2);
                if (p is Pair)
                {
                    WriteLine(1);
                }
                public readonly struct Pair(int a, int b) { public readonly int A = a; public readonly int B = b; }
                """
            ),
            (
                "BareIsPrimTypeBanned",
                "VIB000",
                """
                int x = 5;
                if (x is int)
                {
                    WriteLine(1);
                }
                """
            ),
            // Item 6a: Option<ValueType>.AsMut() violates `where T : class` -> VIB000
            (
                "OptionValueTypeAsMutBanned",
                "VIB000",
                """
                Option<int> o = Some(5);
                var m = o.AsMut();
                WriteLine(m.IsSome ? 1 : 0);
                """
            ),
            // Item 6b: Overloaded local functions in same scope (`CS0128`) -> VIB000
            (
                "OverloadedLocalFunctionsBanned",
                "VIB000",
                """
                Pick(1);
                static void Pick(int v) => WriteLine(v);
                static void Pick(long v) => WriteLine(v);
                """
            ),
            // Item 6c: Ambiguous static target across imported `using` namespaces (`CS0104`)
            (
                "AmbiguousUsingStaticTargetBanned",
                "VIB000",
                """
                using Ns1;
                using Ns2;

                namespace App
                {
                    public static class Program
                    {
                        public static void Main()
                        {
                            WriteLine(DupUtil.Run());
                        }
                    }
                }

                namespace Ns1
                {
                    public static class DupUtil { public static int Run() => 1; }
                }

                namespace Ns2
                {
                    public static class DupUtil { public static int Run() => 2; }
                }
                """
            ),
            // Item 7: Contextual keywords in banned feature slots still rejected
            (
                "DynamicTypeStillBanned",
                "VIB000",
                """
                dynamic x = 1;
                WriteLine(1);
                """
            ),
            (
                "AsyncLocalFuncStillBanned",
                "VIB000",
                """
                WriteLine(1);
                static async void Go() { }
                """
            ),
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r5-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            foreach (var tc in cases)
            {
                string srcPath = Path.Combine(tempDir, $"{tc.Name}.cs");
                File.WriteAllText(srcPath, tc.Code);

                var c0Comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Stage0R5_{tc.Name}", includeStd: true);
                var c0Outcome = VibraniumCompilerDriver.CheckAndEmitIr(c0Comp);
                Assert.False(c0Outcome.Success, $"Case '{tc.Name}' unexpectedly succeeded in Stage-0.");
                if (tc.ExpectedId != "VIB000")
                {
                    var (_, c0VibDiags) = Vibranium.Frontend.Roslyn.VibraniumFrontend.CompileAndCheck(c0Comp);
                    Assert.Contains(c0VibDiags, d => d.Id == tc.ExpectedId);
                }

                var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
                Assert.True(exitCode != 0, $"Case '{tc.Name}' unexpectedly succeeded in vib-self.");
                string combined = stdout + "\n" + stderr;
                Assert.True(
                    combined.Contains(tc.ExpectedId, StringComparison.Ordinal),
                    $"Case '{tc.Name}' expected {tc.ExpectedId} from vib-self, got:\n{combined}");
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
    public void SelfHost_Round6_PatternDefiniteInit_NegatedGuard_And_ConditionalScope()
    {
        const string code = """
            WriteLine(UnwrapOrEarly(Some(42)));
            WriteLine(UnwrapOrEarly(None));
            WriteLine(UnwrapIsNotGuard(Some(14)));
            WriteLine(UnwrapIsNotGuard(None));
            WriteLine(BranchAssign(Some(15)));
            WriteLine(BranchAssign(None));
            WriteLine(TernaryPatterns(Some(7)));
            WriteLine(TernaryPatterns(None));
            int a = 100;
            int b = 200;
            Option<int> pickA = Some(1);
            ref int r = ref b;
            if (pickA is Some<int>(var k) && k == 1) r = ref a;
            r += 5;
            WriteLine(a);
            WriteLine(b);
            int plain = 77;
            bool idOk = plain is int copied;
            if (idOk)
            {
                WriteLine(copied);
            }

            bool refutOk = pickA is Some<int> (var manualInit);
            if (!refutOk)
            {
                manualInit = -9;
            }
            else
            {
                manualInit = 88;
            }

            WriteLine(manualInit);
            static int UnwrapOrEarly(Option<int> opt)
            {
                if (!(opt is Some<int> (var v)))
                {
                    return -1;
                }

                return v * 2;
            }

            static int UnwrapIsNotGuard(Option<int> opt)
            {
                if (opt is not Some<int> (var nv))
                {
                    return -2;
                }

                return nv * 3;
            }

            static int BranchAssign(Option<int> opt)
            {
                if (!(opt is Some<int> (var w)))
                {
                    w = 99;
                }

                return w + 1;
            }

            static int TernaryPatterns(Option<int> opt)
            {
                int x = opt is Some<int> (var p) ? p + 10 : -5;
                int y = !(opt is Some<int> (var q)) ? -3 : q + 20;
                return x + y;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round6_ConstStrings_Concat_And_Utf8Views()
    {
        const string code = """
            const string LocalBase = ConstHolder.Greeting;
            const string LocalEmpty = "";
            const string Picked = ConstHolder.Banner;
            const string Fallback = ConstHolder.AliasGreeting;
            WriteLine(LocalBase);
            WriteLine(LocalEmpty.AsUtf8().Length + string.Empty.AsUtf8().Length);
            WriteLine(Picked);
            WriteLine(Fallback);
            WriteLine(Picked.AsUtf8().Length);
            Write(ConstHolder.MultiLine);
            WriteLine("");
            foreach (var ln in ConstHolder.MultiLine.Lines())
            {
                Write("["u8);
                Write(ln);
                WriteLine("]"u8);
            }

            Stdio.WriteOut(ConstHolder.TrailingNewline.AsUtf8());
            public static class ConstHolder
            {
                public const string Greeting = "Hello";
                public const string AliasGreeting = (Greeting);
                public const string Banner = "Hello, Vibranium!";
                public const string MultiLine = "Hello\nVibranium";
                public const string TrailingNewline = "Hello-Vibranium!\n";
            }

            static class Round12Runtime
            {
                public static T Value<T>(T value) => value;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round6_PositionalUnionPatterns_And_NegatedUnionExhaustiveness()
    {
        const string code = """
            Option<int> s5 = Some(5);
            Option<int> s12 = Some(12);
            Option<int> n = None;
            if ((s12 is Some<int>(var big)) && ((((big > 10) && (big is var _)))))
            {
                WriteLine(big);
            }

            if (s5 is Some<int>)
            {
                WriteLine(111);
            }

            if (n is NoneType)
            {
                WriteLine(222);
            }

            if (s12 is Some<int>(var posV))
            {
                WriteLine(posV + posV);
            }

            Result<int, int> okRes = Ok(33);
            Result<int, int> errRes = Err(44);
            WriteLine(EvalRes(okRes));
            WriteLine(EvalRes(errRes));
            WriteLine(ClassifyFive(s5));
            WriteLine(ClassifyFive(s12));
            WriteLine(ClassifyFive(n));
            WriteLine(ClassifyNegatedVar(s12));
            WriteLine(ClassifyNegatedVar(n));
            static int EvalRes(Result<int, int> r) => r switch
            {
                Ok<int>(var v) => v + 1,
                Err<int>(var e) => -e,
            };
            static int ClassifyFive(Option<int> o) => o switch
            {
                Some<int> (5) => 500,
                not Some<int> (5) => -500,
            };
            static int ClassifyNegatedVar(Option<int> o) => o switch
            {
                not Some<int> (var _) => 0,
                Some<int>(var x) => x,
            };
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round6_NamespaceUsings_Aliases_And_UsingStatic_MultiFile()
    {
        (string FileName, string Code)[] files =
        [
            (
                "GlobalAndLib.cs",
                """
                global using GlobalLib;

                namespace GlobalLib
                {
                    public static class GlobalHelper
                    {
                        public static int Ping() => 10;
                    }
                }

                namespace OuterPool
                {
                    public static class DupPick
                    {
                        public static int Which() => 100;
                    }
                }

                namespace InnerPool
                {
                    public static class DupPick
                    {
                        public static int Which() => 200;
                    }

                    public enum LocalMode
                    {
                        None = 0,
                        Fast = 7,
                    }
                }

                namespace CustomFmt
                {
                    public static class CharPrinter
                    {
                        public const int Tag = 55;

                        public static void WriteLine(char c)
                        {
                            Vibranium.Prelude.WriteLine((int)c + 1000);
                        }

                        public static int MagicProp => Tag + 10;
                    }
                }

                namespace Disposables
                {
                    using DispAlias = System.IDisposable;

                    public sealed class ScopedHandle(int id) : DispAlias
                    {
                        public readonly int Id = id;
                        public void Dispose()
                        {
                            Vibranium.Prelude.WriteLine(Id + 900);
                        }
                    }
                }
                """
            ),
            (
                "App.cs",
                """
                using OuterPool;
                using static CustomFmt.CharPrinter;
                using IntVec = Vibranium.Vec<int>;
                using DispNs = Disposables;
                using SysInt = System.Int32;

                namespace ScopedApp
                {
                    using InnerPool;
                    using static InnerPool.LocalMode;

                    public static class Runner
                    {
                        public static void RunInner()
                        {
                            Vibranium.Prelude.WriteLine(DupPick.Which());
                            LocalMode m = None;
                            LocalMode f = Fast;
                            Vibranium.Prelude.WriteLine((int)m);
                            Vibranium.Prelude.WriteLine((int)f);
                        }
                    }
                }

                namespace SiblingApp
                {
                    public static class Program
                    {
                        public static void Main()
                        {
                            WriteLine(DupPick.Which());
                            WriteLine(GlobalHelper.Ping());

                            WriteLine('A');
                            WriteLine(MagicProp);
                            WriteLine(Tag);

                            ScopedApp.Runner.RunInner();

                            SysInt v0 = SysInt.MaxValue - 2147483600;
                            var vec = new IntVec();
                            vec.Add(v0);
                            WriteLine(vec[0]);

                            using var h = new DispNs.ScopedHandle(23);
                            WriteLine(h.Id);
                        }
                    }
                }
                """
            ),
        ];

        var res = CompileAndRunSelfHostMultiFileDifferential(files);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round6_ExactDecimalFloat_SingleRounding_And_ConstEvalParity()
    {
        const string code = """
            const double ExactBig = 1e300;
            double rtBig = 1e300;
            WriteLine(ExactBig == rtBig);

            const float ConstSingleRound = 4611686293305294849L;
            float rtSingleRound = 4611686293305294849L;
            long nonConstLong = 4611686293305294849L;
            WriteLine((long)ConstSingleRound);
            WriteLine((long)rtSingleRound);
            WriteLine((long)(float)nonConstLong);

            int CommonTernary = (int)((Identity(true) ? 1 : 2.0) / 2 * 10);
            static bool Identity(bool value) => value;
            WriteLine(CommonTernary);

            const bool FloatEqInt = 16777217 == 16777216f;
            WriteLine(FloatEqInt);

            const double FMod = 5.5 % 2.0;
            const double CharAsDouble = 'A';
            const Color Shifted1 = Color.Red + '\x02';
            const Color Shifted2 = '\x01' + Color.Green;
            const int Diff = Color.AliasRed - Color.Red;
            const int DefI = default(int);
            const double DefD = default(double);
            const bool DefB = default(bool);
            const Color DefC = default(Color);

            WriteLine((int)(FMod * 10));
            WriteLine((int)CharAsDouble);
            WriteLine((int)Shifted1);
            WriteLine((int)Shifted2);
            WriteLine(Diff);
            WriteLine(DefI);
            WriteLine((int)DefD);
            WriteLine(DefB);
            WriteLine((int)DefC);
            WriteLine(DeepChain.C12);

            TestColorShadow(Color.Blue);

            static void TestColorShadow(Color initC)
            {
                if (initC is Color Color)
                {
                    WriteLine((int)Color);
                    WriteLine((int)Color.Green);
                }
            }

            public enum Color
            {
                Red = 1,
                AliasRed = Red,
                Green = AliasRed + 3,
                Blue = 9,
            }

            public static class DeepChain
            {
                public const int C12 = C11 + 1;
                public const int C11 = C10 + 1;
                public const int C10 = C9 + 1;
                public const int C9 = C8 + 1;
                public const int C8 = C7 + 1;
                public const int C7 = C6 + 1;
                public const int C6 = C5 + 1;
                public const int C5 = C4 + 1;
                public const int C4 = C3 + 1;
                public const int C3 = C2 + 1;
                public const int C2 = C1 + 1;
                public const int C1 = 1;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round6_NegativeDiagnosticsParity()
    {
        (string Name, string ExpectedId, string Code)[] cases =
        [
            (
                "ValueIsPatternUnassignedAfterBool",
                "VIB001",
                """
                Option<int> opt = Some(1);
                bool ok = opt is Some<int>(var v);
                WriteLine(ok);
                WriteLine(v);
                """
            ),
            (
                "NegatedGuardUnassignedInThen",
                "VIB001",
                """
                Option<int> opt = Some(1);
                if (!(opt is Some<int>(var v)))
                {
                    WriteLine(v);
                }
                """
            ),
            (
                "ConditionalElsePatternUnassigned",
                "VIB001",
                """
                Option<int> opt = Some(1);
                int x = opt is Some<int>(var v) ? 0 : v;
                WriteLine(x);
                """
            ),
            (
                "IsNotGuardUnassignedInThen",
                "VIB001",
                """
                Option<int> opt = Some(1);
                if (opt is not Some<int>(var x))
                {
                    WriteLine(x);
                }
                """
            ),
            (
                "SwitchArmPatternVarInsideNotBanned",
                "VIB000",
                """
                Option<int> opt = Some(1);
                int r = opt switch
                {
                    not Some<int>(var x) => 0,
                    Some<int>(var y) => y,
                };
                WriteLine(r);
                """
            ),
            (
                "NestedPatternVarInsideNotAndBanned",
                "VIB000",
                """
                Option<int> opt = Some(1);
                if (opt is (not Some<int>(var x)) and NoneType)
                {
                    WriteLine(0);
                }
                """
            ),
            (
                "NegatedIrrefutableDeclPatternBanned",
                "VIB000",
                """
                int a = 1;
                if (a is not int y)
                {
                    WriteLine(0);
                }
                """
            ),
            (
                "PatternVarInsideOrBanned",
                "VIB000",
                """
                Option<int> opt = Some(1);
                if (opt is Some<int>(var x) or NoneType)
                {
                    WriteLine(0);
                }
                """
            ),
            (
                "IfPatternVarCollidesWithSubsequentLocal",
                "VIB000",
                """
                Option<int> opt = Some(1);
                if (opt is Some<int>(var v))
                {
                    WriteLine(v);
                }
                int v = 2;
                WriteLine(v);
                """
            ),
            (
                "ChildBlockLocalCollidesWithSubsequentOuterLocal",
                "VIB000",
                """
                {
                    int x = 1;
                    WriteLine(x);
                }
                int x = 2;
                WriteLine(x);
                """
            ),
            (
                "OuterLocalCollidesWithChildBlockLocal",
                "VIB000",
                """
                int x = 1;
                {
                    int x = 2;
                    WriteLine(x);
                }
                WriteLine(x);
                """
            ),
            (
                "ChildLocalFuncCollidesWithOuterLocal",
                "VIB000",
                """
                {
                    WriteLine(Helper());
                    static int Helper() => 1;
                }
                int Helper = 2;
                WriteLine(Helper);
                """
            ),
            (
                "UnionUnknownPropertyPatternBanned",
                "VIB000",
                """
                Option<int> opt = Some(1);
                if (opt is Some<int> { WrongProp: var x })
                {
                    WriteLine(x);
                }
                """
            ),
            (
                "NoneTypeNonEmptyPropertyPatternBanned",
                "VIB000",
                """
                Option<int> opt = None;
                if (opt is NoneType { Value: 1 })
                {
                    WriteLine(1);
                }
                """
            ),
            (
                "NonUnionStructPropertyPatternBanned",
                "VIB000",
                """
                Pair p = new Pair(1, 2);
                if (p is Pair { A: 1 })
                {
                    WriteLine(1);
                }
                public readonly struct Pair(int a, int b) { public readonly int A = a; public readonly int B = b; }
                """
            ),
            (
                "LinearResourceInSomePlacePatternBanned",
                "VIB006",
                """
                using var box = new LinBox(1);
                Option<LinBox> opt = Some(box);
                if (opt is Some<LinBox>(var inner))
                {
                    inner.Dispose();
                }
                public sealed class LinBox(int v) : System.IDisposable
                {
                    public readonly int V = v;
                    public void Dispose() { }
                }
                """
            ),
            (
                "NamespaceUsingDoesNotLeakToSiblingNamespace",
                "VIB000",
                """
                namespace SecretLib
                {
                    public static class SecretTool { public static int Go() => 1; }
                }

                namespace FirstNs
                {
                    using SecretLib;
                    public static class FirstUser { public static int Use() => SecretTool.Go(); }
                }

                namespace SecondNs
                {
                    public static class Program
                    {
                        public static void Main()
                        {
                            WriteLine(SecretTool.Go());
                        }
                    }
                }
                """
            ),
            (
                "AmbiguousUsingStaticMethodCallBanned",
                "VIB000",
                """
                using static LibA.HelperA;
                using static LibB.HelperB;

                namespace App
                {
                    public static class Program
                    {
                        public static void Main()
                        {
                            WriteLine(Compute(1));
                        }
                    }
                }

                namespace LibA
                {
                    public static class HelperA { public static int Compute(int x) => x + 1; }
                }

                namespace LibB
                {
                    public static class HelperB { public static int Compute(int x) => x + 2; }
                }
                """
            ),
            (
                "DoubleLiteralOverflowBanned",
                "VIB000",
                """
                double d = 1e500;
                WriteLine((int)d);
                """
            ),
            (
                "FloatLiteralOverflowBanned",
                "VIB000",
                """
                float f = 1e50f;
                WriteLine((int)f);
                """
            ),
            (
                "ConstTernaryNonConstDeadArmBanned",
                "VIB000",
                """
                const int X = true ? 1 : RuntimeVal();
                WriteLine(X);
                static int RuntimeVal() => 2;
                """
            ),
            (
                "ConstTernaryIncompatibleArmTypesBanned",
                "VIB000",
                """
                const int X = true ? 1 : false;
                WriteLine(X);
                """
            ),
            (
                "ConstLogicalOrNonConstRhsBanned",
                "VIB000",
                """
                const bool B = true || RuntimeFlag();
                WriteLine(B);
                static bool RuntimeFlag() => false;
                """
            ),
            (
                "DeadBranchConstDivByZeroStillRejected",
                "VIB000",
                """
                int x = false ? (1 / 0) : 5;
                WriteLine(x);
                """
            ),
            (
                "ConstDefaultStringBannedWithVib008",
                "VIB008",
                """
                const string S = default(string);
                WriteLine(S);
                """
            ),
            (
                "TypeNamedVarBanned",
                "VIB000",
                """
                WriteLine(1);
                public sealed class var { }
                """
            ),
            (
                "MultiDeclaratorVarBanned",
                "VIB000",
                """
                var a = 1, b = 2;
                WriteLine(a + b);
                """
            ),
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r6-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            foreach (var tc in cases)
            {
                string srcPath = Path.Combine(tempDir, $"{tc.Name}.cs");
                File.WriteAllText(srcPath, tc.Code);

                var c0Comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Stage0R6_{tc.Name}", includeStd: true);
                var c0Outcome = VibraniumCompilerDriver.CheckAndEmitIr(c0Comp);
                Assert.False(c0Outcome.Success, $"Case '{tc.Name}' unexpectedly succeeded in Stage-0.");
                if (tc.ExpectedId != "VIB000")
                {
                    var (_, c0VibDiags) = Vibranium.Frontend.Roslyn.VibraniumFrontend.CompileAndCheck(c0Comp);
                    Assert.Contains(c0VibDiags, d => d.Id == tc.ExpectedId);
                }

                var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
                Assert.True(exitCode != 0, $"Case '{tc.Name}' unexpectedly succeeded in vib-self.");
                string combined = stdout + "\n" + stderr;
                Assert.True(
                    combined.Contains(tc.ExpectedId, StringComparison.Ordinal),
                    $"Case '{tc.Name}' expected {tc.ExpectedId} from vib-self, got:\n{combined}");
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
    public void SelfHost_Round7_FloatDoubleNaNPatterns_And_ExhaustiveSwitch_Differential()
    {
        const string code = """
            double rtZero = MakeZero();
            double rtNaN = 0.0 / rtZero;
            float rtNaNF = 0.0f / (float)rtZero;
            double negZero = -0.0;
            double posInf = 1.0 / rtZero;
            double negInf = -1.0 / rtZero;
            // Keep the scrutinees nonconstant: constant always/never matches are
            // source-language boundaries, covered by Round11Stage0Tests.
            double finiteDouble = 3.5;
            float finiteFloat = 0.0f;
            // 1. `is double.NaN`, `is not double.NaN`, `is float.NaN`, `is not float.NaN`
            WriteLine(rtNaN is double.NaN);
            WriteLine(rtNaN is not double.NaN);
            WriteLine(finiteDouble is double.NaN);
            WriteLine(finiteDouble is not double.NaN);
            WriteLine(rtNaNF is float.NaN);
            WriteLine(rtNaNF is not float.NaN);
            WriteLine(finiteFloat is float.NaN);
            // 2. Single-evaluation guarantee on scrutinee with side effects
            int evalCount = 0;
            bool wasNaN = NextVal(ref evalCount, rtNaN)is double.NaN;
            bool wasNotNaN = NextVal(ref evalCount, 2.5)is not double.NaN;
            WriteLine(wasNaN);
            WriteLine(wasNotNaN);
            WriteLine(evalCount);
            // 3. Nested `[Union]` patterns (`Some<double>(double.NaN)`, `Some<float>(float.NaN)`)
            Option<double> optNaN = Some(rtNaN);
            Option<double> optNum = Some(1.25);
            Option<double> optNone = None;
            WriteLine(optNaN is Some<double> (double.NaN));
            WriteLine(optNum is Some<double> (double.NaN));
            WriteLine(optNone is Some<double> (double.NaN));
            Option<float> optNaNF = Some(rtNaNF);
            Option<float> optNumF = Some(-4.0f);
            WriteLine(optNaNF is Some<float> (float.NaN));
            WriteLine(optNumF is Some<float> (float.NaN));
            // 4. Exhaustive relational + NaN switch expressions (no `_` wildcard needed)
            WriteLine(ClassifyDouble(rtNaN));
            WriteLine(ClassifyDouble(-2.0));
            WriteLine(ClassifyDouble(negZero));
            WriteLine(ClassifyDouble(0.0));
            WriteLine(ClassifyDouble(posInf));
            WriteLine(ClassifyDouble(negInf));
            WriteLine(ClassifyDoubleNegated(rtNaN));
            WriteLine(ClassifyDoubleNegated(-0.5));
            WriteLine(ClassifyDoubleNegated(0.5));
            WriteLine(ClassifyFloat(rtNaNF));
            WriteLine(ClassifyFloat(-1.5f));
            WriteLine(ClassifyFloat(0.0f));
            WriteLine(ClassifyFloat(9.5f));
            // 5. Switch statements with `case double.NaN:` and exhaustive float/double switch return
            WriteLine(SwitchStmtDouble(rtNaN));
            WriteLine(SwitchStmtDouble(0.0));
            WriteLine(SwitchStmtDouble(-9.0));
            WriteLine(SwitchStmtFloat(rtNaNF));
            WriteLine(SwitchStmtFloat(4.0f));
            static double MakeZero() => 0.0;
            static double NextVal(ref int counter, double v)
            {
                counter += 1;
                return v;
            }

            static int ClassifyDouble(double d) => d switch
            {
                var cutValue0 when (cutValue0 < 0.0) => -1,
                var cutValue1 when (cutValue1 >= 0.0) => 1,
                double.NaN => 99,
                _ => default
            };
            static int ClassifyDoubleNegated(double d) => d switch
            {
                var cutValue2 when (cutValue2 < 0.0) => -10,
                var cutValue3 when !(((cutValue3 < 0.0))) => 20,
                _ => default
            };
            static int ClassifyFloat(float f) => f switch
            {
                var cutValue4 when (cutValue4 < 0.0f) => -2,
                var cutValue5 when (cutValue5 >= 0.0f) => 2,
                float.NaN => 77,
                _ => default
            };
            static int SwitchStmtDouble(double d)
            {
                switch (d)
                {
                    case double.NaN:
                        return 500;
                    case var cutValue6 when (cutValue6 < 0.0):
                        return -5;
                    case var cutValue7 when (cutValue7 >= 0.0):
                        return 5;
                    default:
                        return default;
                }
            }

            static int SwitchStmtFloat(float f)
            {
                switch (f)
                {
                    case float.NaN:
                        return 600;
                    case var cutValue8 when (cutValue8 < 0.0f):
                        return -6;
                    case var cutValue9 when (cutValue9 >= 0.0f):
                        return 6;
                    default:
                        return default;
                }
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelfHost_Round7_ExhaustiveReturnPaths_And_Reachability_Differential()
    {
        const string code = """
            WriteLine(FromBoolSwitch(true));
            WriteLine(FromBoolSwitch(false));
            WriteLine(FromIntRelSwitch(-5));
            WriteLine(FromIntRelSwitch(9));
            WriteLine(FromOptionSwitchDefault(Some(42)));
            WriteLine(FromOptionSwitchDefault(None));
            WriteLine(FromInfiniteLoop(0));
            WriteLine(FromConstTrueBranch());
            WriteLine(FromLocalFuncs(7));
            static int FromBoolSwitch(bool b)
            {
                switch (b)
                {
                    case true:
                        return 100;
                    case false:
                        return 200;
                }
            }

            static int FromIntRelSwitch(int x)
            {
                switch (x)
                {
                    case var cutValue0 when (cutValue0 < 0):
                        return -10;
                    case var cutValue1 when (cutValue1 >= 0):
                        return 10;
                    default:
                        return default;
                }
            }

            static int FromOptionSwitchDefault(Option<int> opt)
            {
                switch (opt)
                {
                    case Some<int> (var x):
                        return x + 1;
                    default:
                        return -1;
                }
            }

            static int FromInfiniteLoop(int start)
            {
                int cur = start;
                while (true)
                {
                    if (cur >= 4)
                    {
                        return cur * 10;
                    }

                    cur += 1;
                }
            }

            static int FromConstTrueBranch()
            {
                if (Round12Runtime.Value(true))
                {
                    return 77;
                }
                return 0;
            }

            static int FromLocalFuncs(int x)
            {
                static int InnerDefaultSwitch(int k)
                {
                    switch (k)
                    {
                        case 0:
                            return 11;
                        default:
                            return k + 22;
                    }
                }

                return InnerDefaultSwitch(x);
            }

            static class Round12Runtime
            {
                public static T Value<T>(T value) => value;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);

        // A closed native union does not erase C#'s nominal unmatched source path.
        // The pinned SDK rejects this positional fixture with CS0161 for both
        // functions; self reports the established canonical source ID VIB000.
        const string unionSwitchCode = """
            WriteLine(FromOptionSwitch(Some(42)));
            WriteLine(FromOptionSwitch(None));
            WriteLine(FromResultSwitch(Ok(10)));
            WriteLine(FromResultSwitch(Err(3)));

            static int FromOptionSwitch(Option<int> opt)
            {
                switch (opt)
                {
                    case Some<int>(var x):
                        return x + 1;
                    case NoneType:
                        return -1;
                }
            }

            static int FromResultSwitch(Result<int, int> r)
            {
                switch (r)
                {
                    case Ok<int>(var v):
                        return v * 2;
                    case Err<int>(var e):
                        return -e;
                }
            }
            """;
        string uDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r7-unionsw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uDir);
        try
        {
            string uSrc = Path.Combine(uDir, "Program.cs");
            File.WriteAllText(uSrc, unionSwitchCode);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(uSrc, "Round7UnionSourceInvalid", includeStd: true);
            var sourceErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.Equal(2, sourceErrors.Length);
            Assert.All(sourceErrors, d => Assert.Equal("CS0161", d.Id));

            var analyzed = await Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzerExtensions.WithAnalyzers(
                compilation,
                System.Collections.Immutable.ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(
                    new Vibranium.Analyzer.VibraniumDiagnosticAnalyzer())).GetAllDiagnosticsAsync();
            var analyzedErrors = analyzed.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.Equal(2, analyzedErrors.Length);
            Assert.All(analyzedErrors, d => Assert.Equal("CS0161", d.Id));

            var stage0 = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(stage0.Success);
            Assert.Equal(2, stage0.Errors.Count);
            Assert.All(stage0.Errors, error => Assert.Contains("CS0161", error, StringComparison.Ordinal));

            var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", uSrc, "--sysroot", SysrootPath);
            Assert.NotEqual(0, exitCode);
            string[] selfIds = System.Text.RegularExpressions.Regex.Matches(stdout + stderr, @"\bVIB\d{3}\b")
                .Select(match => match.Value).Distinct().OrderBy(id => id).ToArray();
            Assert.Equal(new[] { "VIB000" }, selfIds);
        }
        finally
        {
            if (Directory.Exists(uDir)) Directory.Delete(uDir, recursive: true);
        }

        // Supplying the C# unmatched path keeps the same native union behavior.
        string unionSwitchWithDefaults = unionSwitchCode
            .Replace("return -1;", "return -1; default: return 0;", StringComparison.Ordinal)
            .Replace("return -e;", "return -e; default: return 0;", StringComparison.Ordinal);
        var unionResult = CompileAndRunSelfHostDifferential(unionSwitchWithDefaults, withAsan: true);
        Assert.Equal(0, unionResult.ManagedExit);
        Assert.Equal(unionResult.ManagedExit, unionResult.NativeExit);
        Assert.Equal("43\n-1\n20\n-3\n", unionResult.ManagedOut.Replace("\r\n", "\n"));
        Assert.Equal(unionResult.ManagedOut, unionResult.NativeOut);
        Assert.Equal(string.Empty, unionResult.ManagedErr);
        Assert.Equal(unionResult.ManagedErr, unionResult.NativeErr);
    }

    [Fact]
    public void SelfHost_Round7_RefReassignment_LocalCarriersAndParameterWrites_Differential()
    {
        const string code = """
            int a = 10;
            int b = 20;
            ref int r = ref a;
            r = 11;
            r = ref b;
            r = 22;
            WriteLine(a);
            WriteLine(b);

            // Ref assignment as expression value and as lvalue target `(r = ref a) = 33`
            int readBack = (r = ref a);
            WriteLine(readBack);
            (r = ref b) = 33;
            WriteLine(a);
            WriteLine(b);

            // `ref readonly` local reassignment
            ref readonly int ro = ref a;
            WriteLine(ro);
            ro = ref b;
            WriteLine(ro);

            // v0.2 rebinds local carriers, not parameter bindings (Round9 parity regressions reject the original parameter forms).
            int p1 = 1;
            int p2 = 2;
            RebindRefParam(ref p1, ref p2);
            WriteLine(p1);
            WriteLine(p2);

            WriteLine(RebindInParam(in p1, in p2));

            int sp = 5;
            WriteLine(RebindScopedRefParam(ref sp));
            WriteLine(sp);

            // NLL loan kill on ref local reassignment: old loan on `v` ends before `v.Add(9)`!
            Vec<int> v = [10, 20];
            ref int elem = ref v[0];
            elem += 5;
            elem = ref a;
            v.Add(30);
            elem += 100;
            WriteLine(v[0]);
            WriteLine(v[2]);
            WriteLine(a);

            static void RebindRefParam(ref int first, ref int second)
            {
                first = 50;
                ref int carrier = ref first;
                carrier = ref second;
                carrier = 60;
            }

            static int RebindInParam(in int first, in int second)
            {
                int sum = first;
                ref readonly int carrier = ref first;
                carrier = ref second;
                return sum + carrier;
            }

            static int RebindScopedRefParam(scoped ref int p)
            {
                p = 70;
                int tempLocal = 80;
                ref int carrier = ref p;
                carrier = ref tempLocal;
                carrier += 5;
                return tempLocal;
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round7_OutVarScopeInSwitchExpr_And_Ternary_Differential()
    {
        const string code = """
            // 1. Switch-expression arm `out var` and pattern bindings must NOT leak into outer `if` body
            int tag = 2;
            if ((tag switch
            {
                1 when TryDouble(tag, out var guardOut) => guardOut > 0,
                2 => TryDouble(tag, out var bodyOut) && bodyOut == 4,
                _ => false,
            }))
            {
                int guardOut = 111;
                int bodyOut = 222;
                WriteLine(guardOut + bodyOut);
            }

            // Scrutinee `out var` inside `if` condition STILL scopes cleanly to the `if` body!
            if ((ProduceScrutinee(5, out var scrutOut) switch
            {
                10 => true,
                _ => false,
            }))
            {
                WriteLine(scrutOut);
            }

            // 2. Switch-arm pattern variable does NOT shadow `this.a` in outer `if` body, while `?:` `out var` / `is` DOES scope into `if` body!
            var shadowHolder = new FieldShadowCheck(777);
            WriteLine(shadowHolder.SwitchArmDoesNotShadow(25));
            WriteLine(shadowHolder.TernaryOutVarInIfCondition("5"));
            WriteLine(shadowHolder.TernaryPatternInIfCondition(Some(19)));

            // `out var` declared in branches of `?:` remains declared in the enclosing scope (can be assigned without redeclaration)
            bool chooseLeft = RuntimeBool(true);
            if (chooseLeft ? TryDouble(6, out var branchVar) : false)
            {
                WriteLine(branchVar);
            }
            bool okStmt = RuntimeBool(false) ? TryDouble(3, out var stL) : TryDouble(8, out var stR);
            stL = 10;
            stR = 20;
            WriteLine(okStmt ? stL + stR : -1);

            static bool RuntimeBool(bool b) => b;

            static bool TryDouble(int input, out int doubled)
            {
                doubled = input * 2;
                return doubled > 0;
            }

            static int ProduceScrutinee(int x, out int captured)
            {
                captured = x + 100;
                return x * 2;
            }

            public readonly struct FieldShadowCheck(int a)
            {
                private readonly int a = a;

                public int SwitchArmDoesNotShadow(int x)
                {
                    if ((x switch
                    {
                        int a when a > 10 => true,
                        _ => true,
                    }))
                    {
                        return a;
                    }
                    return -1;
                }

                public int TernaryOutVarInIfCondition(string text)
                {
                    if ((int.TryParse(text, out var a) ? a > 0 : false))
                    {
                        return a;
                    }
                    return this.a;
                }

                public int TernaryPatternInIfCondition(Option<int> opt)
                {
                    if ((opt is Some<int>(var a) ? a > 0 : false))
                    {
                        return a;
                    }
                    return this.a;
                }
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round7_ConstPromotion_ClosedGenericUsingStatic_Disposable_And_HexNegation_Differential()
    {
        const string code = """
            using static ClosedGenNs.GenericHelper<int>;

            // 1. Fix 5: sbyte/short + uint -> long (-1L)
            const sbyte CsByte = 1;
            const short CShort = 1;
            const long Sub1 = (sbyte)1 - 2U;
            const long Sub2 = (short)1 - 2U;
            const long Sub3 = CsByte - 2U;
            const long Sub4 = CShort - 2U;
            var rtSub1 = (sbyte)1 - 2U;
            var rtSub2 = (short)1 - 2U;
            WriteLine(Sub1);
            WriteLine(Sub2);
            WriteLine(Sub3 + Sub4);
            WriteLine(rtSub1);
            WriteLine(rtSub2);
            WriteLine(TypeProbe.Identify(rtSub1));
            WriteLine(TypeProbe.Identify(rtSub2));
            // 2. Fix 7: `using static GenericHelper<int>;`
            const int FromGenConst = SeedConst + 8;
            WriteLine(FromGenConst);
            WriteLine(ScaleSeed(5));
            WriteLine(EchoValue(123));
            WriteLine(CombineWith<short>(10, 7));
            WriteLine(TagProp);
            WriteLine(ClosedGenNs.SubClient.RunLongHelper());
            // 3. Fix 8a: Valid `: IDisposable` classes (including generic)
            {
                using var d1 = new ScopedLog(901);
                using var d2 = new GenericScopedLog<int>(902);
                WriteLine(900);
            }

            // 4. Fix 8b: Negated hex/binary/decimal literals matching Roslyn/.NET 11 & Stage-0
            const int HexNegMin32 = -0x80000000;
            const int BinNegMin32 = -0b1000_0000_0000_0000_0000_0000_0000_0000;
            const long HexUNegMin32 = -0x80000000U;
            const long HexNegMin64 = -0x8000000000000000;
            const long HexLNegMin64 = -0x8000000000000000L;
            const int DecNegMin32 = -2147483648;
            const long DecNegMin64 = -9223372036854775808;
            var varHexNeg = -0x80000000;
            var varBinNeg = -0b1000_0000_0000_0000_0000_0000_0000_0000;
            var varHexUNeg = -0x80000000U;
            var varHexNeg64 = -0x8000000000000000;
            var varDecNeg = -2147483648;
            WriteLine(HexNegMin32);
            WriteLine(BinNegMin32);
            WriteLine(HexUNegMin32);
            WriteLine(HexNegMin64 == long.MinValue);
            WriteLine(HexLNegMin64 == long.MinValue);
            WriteLine(DecNegMin32);
            WriteLine(DecNegMin64 == long.MinValue);
            WriteLine(TypeProbe.Identify(varHexNeg));
            WriteLine(TypeProbe.Identify(varBinNeg));
            WriteLine(TypeProbe.Identify(varHexUNeg));
            WriteLine(TypeProbe.Identify(varHexNeg64));
            WriteLine(TypeProbe.Identify(varDecNeg));
            public static class TypeProbe
            {
                public static int Identify(int underscoreValue) => 32;
                public static int Identify(long underscoreValue) => 64;
            }

            public sealed class ScopedLog(int code) : IDisposable
            {
                public void Dispose()
                {
                    WriteLine(code);
                }
            }

            public sealed class GenericScopedLog<T>(T item) : IDisposable where T : unmanaged
            {
                public void Dispose()
                {
                    _ = item;
                    WriteLine(903);
                }
            }

            namespace ClosedGenNs
            {
                public static class GenericHelper<T>
                    where T : unmanaged
                {
                    public const int SeedConst = 34;
                    public static int TagProp => 77;

                    public static int ScaleSeed(int mult) => SeedConst * mult;
                    public static T EchoValue(T v) => v;
                    public static int CombineWith<U>(int a, U underscoreValue)
                        where U : unmanaged => a + SeedConst;
                }

                namespace Sub
                {
                    using static ClosedGenNs.GenericHelper<long>;

                    public static class InnerRunner
                    {
                        public static long Eval() => EchoValue(500L) + SeedConst + TagProp;
                    }
                }

                public static class SubClient
                {
                    public static long RunLongHelper() => Sub.InnerRunner.Eval();
                }
            }
            """;

        var res = CompileAndRunSelfHostDifferential(code);
        Assert.Equal(0, res.ManagedExit);
        Assert.Equal(res.ManagedExit, res.NativeExit);
        Assert.Equal(res.ManagedOut, res.NativeOut);
        Assert.Equal(res.ManagedErr, res.NativeErr);
    }

    [Fact]
    public void SelfHost_Round7_NegativeDiagnosticsParity()
    {
        (string Name, string ExpectedId, string Code)[] cases =
        [
            (
                "DoubleSwitchMissingNaNArm",
                "VIB000",
                """
                double d = 1.0;
                int x = d switch
                {
                    < 0.0 => -1,
                    >= 0.0 => 1,
                };
                WriteLine(x);
                """
            ),
            (
                "FloatSwitchMissingNaNArm",
                "VIB000",
                """
                float f = 1.0f;
                int x = f switch
                {
                    < 0.0f => -1,
                    >= 0.0f => 1,
                };
                WriteLine(x);
                """
            ),
            (
                "MissingReturnInNonVoidMethod",
                "VIB000",
                """
                WriteLine(Compute(1));
                static int Compute(int x)
                {
                    if (x > 0) return x;
                }
                """
            ),
            (
                "MissingReturnInLocalFunction",
                "VIB000",
                """
                WriteLine(Outer(0));
                static int Outer(int x)
                {
                    int Inner(int y)
                    {
                        if (y > 0) return y;
                    }
                    return Inner(x);
                }
                """
            ),
            (
                "MissingReturnNonExhaustiveSwitchStmt",
                "VIB000",
                """
                WriteLine(Eval(3));
                static int Eval(int x)
                {
                    switch (x)
                    {
                        case 1:
                            return 10;
                        case 2:
                            return 20;
                    }
                }
                """
            ),
            (
                "BareReturnInNonVoidMethod",
                "VIB000",
                """
                WriteLine(Bad(1));
                static int Bad(int x)
                {
                    if (x > 0) return;
                    return 0;
                }
                """
            ),
            (
                "RefReassignBorrowConflictVecAdd",
                "VIB003",
                """
                int a = 1;
                Vec<int> v = [1, 2, 3];
                ref int r = ref a;
                r = ref v[0];
                v.Add(4);
                r = 10;
                WriteLine(a);
                """
            ),
            (
                "ScopedRefParamReassignUnsupported",
                "VIB000",
                """
                int a = 1;
                Test(ref a);
                static void Test(scoped ref int r)
                {
                    Vec<int> v = [1, 2, 3];
                    r = ref v[0];
                    v.Add(4);
                    r = 10;
                }
                """
            ),
            (
                "UnscopedRefParamReassignToLocalRejected",
                "VIB000",
                """
                int a = 1;
                Test(ref a);
                static void Test(ref int r)
                {
                    int local = 5;
                    r = ref local;
                }
                """
            ),
            (
                "RefParamReassignmentFromReadonlyUnsupported",
                "VIB000",
                """
                int a = 1;
                int b = 2;
                Test(ref a, in b);
                static void Test(ref int r, in int ro)
                {
                    r = ref ro;
                }
                """
            ),
            (
                "RefAssignNonRefLocalLhsRejected",
                "VIB000",
                """
                int a = 1;
                int b = 2;
                a = ref b;
                WriteLine(a);
                """
            ),
            (
                "RefAssignRvalueRhsRejected",
                "VIB000",
                """
                int a = 1;
                ref int r = ref a;
                r = ref (a + 1);
                WriteLine(r);
                """
            ),
            (
                "RefAssignPointeeTypeMismatchRejected",
                "VIB000",
                """
                int a = 1;
                long b = 2;
                ref int r = ref a;
                r = ref b;
                WriteLine(r);
                """
            ),
            (
                "ForeachRefVarReassignRejected",
                "VIB000",
                """
                int other = 9;
                Vec<int> v = [1, 2];
                foreach (ref int item in v.AsSpan())
                {
                    item = ref other;
                }
                """
            ),
            (
                "TernarySingleBranchOutVarUnassignedAfterExpr",
                "VIB001",
                """
                bool c = Flag(true);
                bool ok = c ? TryMake(5, out var branchOnly) : false;
                WriteLine(ok);
                WriteLine(branchOnly);
                static bool Flag(bool b) => b;
                static bool TryMake(int a, out int b) { b = a; return true; }
                """
            ),
            (
                "LocalConstStringEmptyRejected",
                "VIB000",
                """
                const string Empty = string.Empty;
                WriteLine(Empty);
                """
            ),
            (
                "SwitchExprArmOutVarDoesNotLeakIntoIfBody",
                "VIB000",
                """
                int x = 1;
                if ((x switch
                {
                    1 when TryMake(x, out var leaked) => leaked > 0,
                    _ => false,
                }))
                {
                    WriteLine(leaked);
                }
                static bool TryMake(int a, out int b) { b = a; return true; }
                """
            ),
            (
                "SBytePlusULongConstRejected",
                "VIB000",
                """
                var x = (sbyte)1 + 2UL;
                WriteLine((long)x);
                """
            ),
            (
                "ShortPlusULongDeclConstRejected",
                "VIB000",
                """
                WriteLine((long)C.Bad);
                public static class C
                {
                    public const ulong Bad = (short)1 + 2UL;
                }
                """
            ),
            (
                "ConstStringPlusBanned",
                "VIB000",
                """
                const string S = "Hello" + "World";
                WriteLine(S);
                """
            ),
            (
                "FieldConstStringPlusBanned",
                "VIB000",
                """
                WriteLine(C.S);
                public static class C
                {
                    public const string A = "Hello";
                    public const string S = A + "!";
                }
                """
            ),
            (
                "ConstStringEqualityBanned",
                "VIB000",
                """
                const bool Same = "abc" == "abc";
                WriteLine(Same);
                """
            ),
            (
                "LocalStringInequalityBanned",
                "VIB000",
                """
                const string A = "abc";
                bool diff = A != "def";
                WriteLine(diff);
                """
            ),
            (
                "DisposableMissingDisposeMethod",
                "VIB000",
                """
                using System;
                WriteLine(1);
                public sealed class Bad1 : IDisposable
                {
                }
                """
            ),
            (
                "DisposableInternalDisposeMethod",
                "VIB000",
                """
                using System;
                WriteLine(1);
                public sealed class Bad2 : IDisposable
                {
                    internal void Dispose() { }
                }
                """
            ),
            (
                "DisposableNonVoidDisposeMethod",
                "VIB000",
                """
                using System;
                WriteLine(1);
                public sealed class Bad3 : IDisposable
                {
                    public int Dispose() => 0;
                }
                """
            ),
            (
                "DisposableParameterizedDisposeMethod",
                "VIB000",
                """
                using System;
                WriteLine(1);
                public sealed class Bad4 : IDisposable
                {
                    public void Dispose(int x) { }
                }
                """
            ),
            (
                "DisposableStaticDisposeMethod",
                "VIB000",
                """
                using System;
                WriteLine(1);
                public sealed class Bad5 : IDisposable
                {
                    public static void Dispose() { }
                }
                """
            ),
            (
                "DisposableGenericDisposeMethod",
                "VIB000",
                """
                using System;
                WriteLine(1);
                public sealed class Bad6 : IDisposable
                {
                    public void Dispose<T>() { }
                }
                """
            ),
            (
                "NegatedHexUInt64SuffixURejected",
                "VIB000",
                """
                var x = -0x8000000000000000U;
                WriteLine(x);
                """
            ),
            (
                "NegatedHexULongSuffixULFieldConstRejected",
                "VIB000",
                """
                WriteLine(C.Bad);
                public static class C
                {
                    public const long Bad = -0x8000000000000000UL;
                }
                """
            ),
            (
                "HexLiteralExceeding64BitsRejected",
                "VIB000",
                """
                ulong x = 0x1_0000_0000_0000_0000UL;
                WriteLine((long)x);
                """
            ),
            (
                "BinaryLiteralExceeding64BitsRejected",
                "VIB000",
                """
                ulong x = 0b1_00000000_00000000_00000000_00000000_00000000_00000000_00000000_00000000UL;
                WriteLine((long)x);
                """
            ),
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), $"vibranium-self-r7-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            foreach (var tc in cases)
            {
                string srcPath = Path.Combine(tempDir, $"{tc.Name}.cs");
                File.WriteAllText(srcPath, tc.Code);

                var c0Comp = VibraniumCompilerDriver.CreateCompilationFromPath(srcPath, $"Stage0R7_{tc.Name}", includeStd: true);
                var c0Outcome = VibraniumCompilerDriver.CheckAndEmitIr(c0Comp);
                Assert.False(c0Outcome.Success, $"Case '{tc.Name}' unexpectedly succeeded in Stage-0.");

                var (exitCode, stdout, stderr) = RunProcess(DotnetExe, VibSelfDllPath, "check", srcPath, "--sysroot", SysrootPath);
                Assert.True(exitCode != 0, $"Case '{tc.Name}' unexpectedly succeeded in vib-self.");
                string combined = stdout + "\n" + stderr;
                Assert.True(
                    combined.Contains(tc.ExpectedId, StringComparison.Ordinal),
                    $"Case '{tc.Name}' expected {tc.ExpectedId} from vib-self, got:\n{combined}");
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
}
