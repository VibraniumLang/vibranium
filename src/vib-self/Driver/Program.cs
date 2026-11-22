using Vibranium.Self;

if (Env.Args().IsErr(out var cliArgs, out _))
{
    Stdio.WriteErr("vib: failed to read command-line arguments\n"u8);
    return 2;
}

if (cliArgs.Count < 1)
{
    PrintUsage();
    return 2;
}

ReadOnlySpan<byte> cmd = cliArgs.Get(0);
if (Bytes.Eq(cmd, "--help"u8) || Bytes.Eq(cmd, "-h"u8) || Bytes.Eq(cmd, "help"u8))
{
    PrintUsage();
    return 0;
}
if (Bytes.Eq(cmd, "--version"u8) || Bytes.Eq(cmd, "version"u8))
{
    Stdio.WriteOut("vib 0.2.0-selfhost\n"u8);
    return 0;
}

int mode; // 0 = check, 1 = emit-ll, 2 = build, 3 = run
if (Bytes.Eq(cmd, "check"u8)) mode = 0;
else if (Bytes.Eq(cmd, "emit-ll"u8)) mode = 1;
else if (Bytes.Eq(cmd, "build"u8)) mode = 2;
else if (Bytes.Eq(cmd, "run"u8)) mode = 3;
else
{
    Stdio.WriteErr("vib: unknown command '"u8);
    Stdio.WriteErr(cmd);
    Stdio.WriteErr("'\n"u8);
    PrintUsage();
    return 2;
}

var cliPool = new BytePool(1024);
var inputs = new Vec<ByteSlice>(8);
var runAppArgs = new Vec<ByteSlice>(8);

ByteSlice outPath = new ByteSlice(0, 0);
bool hasOutPath = false;
ByteSlice emitLlPath = new ByteSlice(0, 0);
bool hasEmitLlPath = false;
ByteSlice sysrootPath = new ByteSlice(0, 0);
ByteSlice ccPath = cliPool.Add("/usr/bin/clang-18"u8);
ByteSlice optFlag = cliPool.Add("-O2"u8);
bool asan = false;
bool includeStd = true;

int i = 1;
while (i < cliArgs.Count)
{
    ReadOnlySpan<byte> a = cliArgs.Get(i);
    if (Bytes.Eq(a, "--"u8))
    {
        i += 1;
        while (i < cliArgs.Count)
        {
            runAppArgs.Push(cliPool.Add(cliArgs.Get(i)));
            i += 1;
        }
        break;
    }
    if (Bytes.Eq(a, "-o"u8) || Bytes.Eq(a, "--output"u8))
    {
        if (i + 1 >= cliArgs.Count)
        {
            Stdio.WriteErr("vib: missing argument after '-o'\n"u8);
            return 2;
        }
        outPath = cliPool.Add(cliArgs.Get(i + 1));
        hasOutPath = true;
        i += 2;
        continue;
    }
    if (Bytes.Eq(a, "--emit-ll"u8))
    {
        if (i + 1 >= cliArgs.Count)
        {
            Stdio.WriteErr("vib: missing argument after '--emit-ll'\n"u8);
            return 2;
        }
        emitLlPath = cliPool.Add(cliArgs.Get(i + 1));
        hasEmitLlPath = true;
        i += 2;
        continue;
    }
    if (Bytes.Eq(a, "--sysroot"u8))
    {
        if (i + 1 >= cliArgs.Count)
        {
            Stdio.WriteErr("vib: missing argument after '--sysroot'\n"u8);
            return 2;
        }
        sysrootPath = cliPool.Add(cliArgs.Get(i + 1));
        i += 2;
        continue;
    }
    if (Bytes.Eq(a, "--cc"u8))
    {
        if (i + 1 >= cliArgs.Count)
        {
            Stdio.WriteErr("vib: missing argument after '--cc'\n"u8);
            return 2;
        }
        ccPath = cliPool.Add(cliArgs.Get(i + 1));
        i += 2;
        continue;
    }
    if (Bytes.Eq(a, "-O0"u8) || Bytes.Eq(a, "-O1"u8) || Bytes.Eq(a, "-O2"u8) || Bytes.Eq(a, "-O3"u8))
    {
        optFlag = cliPool.Add(a);
        i += 1;
        continue;
    }
    if (Bytes.Eq(a, "--asan"u8))
    {
        asan = true;
        i += 1;
        continue;
    }
    if (Bytes.Eq(a, "--no-std"u8))
    {
        includeStd = false;
        i += 1;
        continue;
    }
    if (a.Length > 0 && a[0] == (byte)'-')
    {
        Stdio.WriteErr("vib: unrecognized option '"u8);
        Stdio.WriteErr(a);
        Stdio.WriteErr("'\n"u8);
        return 2;
    }

    inputs.Push(cliPool.Add(a));
    i += 1;
}

if (inputs.Count == 0)
{
    Stdio.WriteErr("vib: missing input <manifest|dir|file.cs>\n"u8);
    return 2;
}

// Phase 0: Load manifest closure / source inputs
var sources = new SourceTable(64);
var bundle = new ManifestBundle(0);
var loadErr = new StringBuf(128);
if (!ManifestLoader.LoadInputs(
    in inputs,
    in cliPool,
    cliPool.Get(sysrootPath),
    includeStd,
    ref sources,
    ref bundle,
    ref loadErr))
{
    Stdio.WriteErr(loadErr.AsUtf8());
    Stdio.WriteErr("\n"u8);
    return 1;
}

// Phase 1: Lexing
var diags = new DiagnosticBag(32);
var names = new Interner(512);
var tokens = new TokenArena(16384);
Lexer.LexAll(ref sources, ref names, ref tokens, ref diags);

// Phase 2: Parsing -> AST arena
var ast = new AstArena(4096);
Parser.ParseAll(in sources, in tokens, ref names, ref ast, ref diags);

if (diags.HasErrors)
{
    var diagText = new StringBuf(512);
    diags.FormatAll(in sources, ref diagText);
    Stdio.WriteErr(diagText.AsUtf8());
    return 1;
}

// Phase 3: Type & declaration binding + gatekeeper checks
bool compilingStdItself = bundle.IsLibraryOnly && bundle.HasStdUnit;
var bctx = new BinderCtx(ref names, compilingStdItself, bundle.HasStdUnit);
var types = new TypeTable(256);
var mir = new MirModuleArena(256);

DeclBinder.BindDeclarations(ref ast, in tokens, ref names, ref bctx, ref types, ref mir, ref diags);

// Phase 4: Lower reachable functions to CFG MIR + check open-generic functions
FuncLowerer.LowerAllReachable(in ast, in tokens, ref names, ref bctx, ref types, ref mir, ref diags);
DeclBinder.ValidateSpanElementTypes(in ast, in tokens, in types, in mir, ref diags);

// An application needs an entry point for every command (C# CS5001), as Stage-0
// compiles it as an executable for check, emit-ll, build and run alike. `check`
// and `emit-ll` still report the ownership diagnostics of the declared code.
bool missingEntryPoint = !bundle.IsLibraryOnly && mir.EntryFuncInstId < 0 && !diags.HasErrors;
if (missingEntryPoint && (mode == 2 || mode == 3))
{
    Stdio.WriteErr("error VIB000: Program does not contain a static 'Main' method or top-level statements suitable for an entry point.\n"u8);
    return 1;
}

// Phase 5: Ownership, move, definite-init, borrow, escape, and linear resource solver
OwnershipChecker.CheckModule(in ast, in tokens, in names, in types, in mir, ref diags);
if (missingEntryPoint)
{
    // A program-level diagnostic: it has no source location (`vib: error ...`).
    diags.Report(VibCodes.Unsupported, -1, 0, 0, "Program does not contain a static 'Main' method or top-level statements suitable for an entry point (CS5001)."u8);
}

if (diags.HasErrors)
{
    var diagText = new StringBuf(512);
    diags.FormatAll(in sources, ref diagText);
    Stdio.WriteErr(diagText.AsUtf8());
    return 1;
}

if (mode == 0)
{
    return 0;
}

// Phase 6: Emit textual LLVM 18 IR
var rawIr = new StringBuf(131072);
LlvmEmitter.EmitModule(in ast, in tokens, in names, in types, in mir, in bundle, ref rawIr);

var finalIr = new StringBuf(asan ? rawIr.Length + 256 : 0);
if (asan)
{
    ApplyAsanAttributes(rawIr.AsUtf8(), ref finalIr);
}

ReadOnlySpan<byte> irBytes = asan ? finalIr.AsUtf8() : rawIr.AsUtf8();

if (mode == 1)
{
    if (hasOutPath)
    {
        if (Fs.WriteAllBytes(cliPool.Get(outPath), irBytes).IsErr(out _, out var ioErr))
        {
            ReportIoError("failed to write LLVM IR output"u8, ioErr.Error);
            return 1;
        }
    }
    else
    {
        Stdio.WriteOut(irBytes);
    }
    return 0;
}

// Mode 2 (`build`) or Mode 3 (`run`): compile `.ll` -> native ELF binary via `clang-18`
var binPathBuf = new StringBuf(128);
var llPathBuf = new StringBuf(128);
bool deleteLlAfter = false;
bool deleteBinAfter = false;

if (mode == 2)
{
    if (hasOutPath)
    {
        binPathBuf.Append(cliPool.Get(outPath));
    }
    else
    {
        DeriveDefaultExePath(cliPool.Get(inputs[0]), ref binPathBuf);
    }

    if (hasEmitLlPath)
    {
        llPathBuf.Append(cliPool.Get(emitLlPath));
    }
    else
    {
        llPathBuf.Append(binPathBuf.AsUtf8());
        llPathBuf.Append(".tmp.ll"u8);
        deleteLlAfter = true;
    }
}
else
{
    // `vib run`: allocate collision-free paths in `/tmp` (starts with `/`, required by `Command`)
    long argHash = ComputeArgsHash(in cliArgs);
    binPathBuf.Append("/tmp/vib_run_"u8);
    binPathBuf.AppendHex(argHash, 16);
    llPathBuf.Append(binPathBuf.AsUtf8());
    llPathBuf.Append(".ll"u8);
    deleteLlAfter = true;
    deleteBinAfter = true;
}

if (Fs.WriteAllBytes(llPathBuf.AsUtf8(), irBytes).IsErr(out _, out var writeLlErr))
{
    ReportIoError("failed to write temporary LLVM IR"u8, writeLlErr.Error);
    return 1;
}

var resolvedCc = new StringBuf(64);
ResolveCcExecutable(cliPool.Get(ccPath), ref resolvedCc);

var clangCmd = new Command(resolvedCc.AsUtf8());
clangCmd.Arg("-Wno-override-module"u8);
clangCmd.Arg(cliPool.Get(optFlag));
if (asan)
{
    clangCmd.Arg("-fsanitize=address,leak"u8);
    clangCmd.Arg("-fno-omit-frame-pointer"u8);
}
clangCmd.Arg(llPathBuf.AsUtf8());
clangCmd.Arg("-o"u8);
clangCmd.Arg(binPathBuf.AsUtf8());

var clangRes = Process.Run(in clangCmd);
if (deleteLlAfter)
{
    RemoveFileQuiet(llPathBuf.AsUtf8());
}

if (clangRes.IsErr(out int clangRc, out var spawnErr))
{
    ReportIoError("failed to invoke clang-18"u8, spawnErr.Error);
    if (deleteBinAfter) RemoveFileQuiet(binPathBuf.AsUtf8());
    return 1;
}
if (clangRc != 0)
{
    Stdio.WriteErr("vib: clang-18 exited with code "u8);
    var rcBuf = new StringBuf(16);
    rcBuf.AppendInt(clangRc);
    rcBuf.AppendLine();
    Stdio.WriteErr(rcBuf.AsUtf8());
    if (deleteBinAfter) RemoveFileQuiet(binPathBuf.AsUtf8());
    return clangRc;
}

if (mode == 2)
{
    return 0;
}

// Execute built binary for `vib run`
var runCmd = new Command(binPathBuf.AsUtf8());
for (int a = 0; a < runAppArgs.Count; a++)
{
    runCmd.Arg(cliPool.Get(runAppArgs[a]));
}

var runRes = Process.Run(in runCmd);
RemoveFileQuiet(binPathBuf.AsUtf8());

if (runRes.IsErr(out int appExitCode, out var runErr))
{
    ReportIoError("failed to execute compiled program"u8, runErr.Error);
    return 1;
}

return appExitCode;

static void ResolveCcExecutable(ReadOnlySpan<byte> rawCc, ref StringBuf outPath)
{
    if (rawCc.Length > 0 && rawCc[0] == (byte)'/')
    {
        outPath.Append(rawCc);
        return;
    }
    if (Bytes.Eq(rawCc, "clang-18"u8))
    {
        outPath.Append("/usr/bin/clang-18"u8);
        return;
    }
    if (Bytes.Eq(rawCc, "clang"u8))
    {
        outPath.Append("/usr/bin/clang"u8);
        return;
    }
    outPath.Append("/usr/bin/"u8);
    outPath.Append(rawCc);
}

static void RemoveFileQuiet(ReadOnlySpan<byte> path)
{
    var rm = new Command("/bin/rm"u8);
    rm.Arg("-f"u8);
    rm.Arg(path);
    _ = Process.Run(in rm);
}

static void DeriveDefaultExePath(ReadOnlySpan<byte> firstInput, ref StringBuf outBin)
{
    int slash = -1;
    for (int k = 0; k < firstInput.Length; k++)
    {
        if (firstInput[k] == (byte)'/') slash = k;
    }
    ReadOnlySpan<byte> baseName = slash >= 0 ? firstInput.Slice(slash + 1, firstInput.Length - slash - 1) : firstInput;
    if (Bytes.Eq(baseName, "vib.manifest"u8) && slash > 0)
    {
        ReadOnlySpan<byte> parent = firstInput.Slice(0, slash);
        int pSlash = -1;
        for (int k = 0; k < parent.Length; k++)
        {
            if (parent[k] == (byte)'/') pSlash = k;
        }
        baseName = pSlash >= 0 ? parent.Slice(pSlash + 1, parent.Length - pSlash - 1) : parent;
    }
    else if (baseName.Length > 3 &&
             baseName[baseName.Length - 3] == (byte)'.' &&
             baseName[baseName.Length - 2] == (byte)'c' &&
             baseName[baseName.Length - 1] == (byte)'s')
    {
        baseName = baseName.Slice(0, baseName.Length - 3);
    }

    if (baseName.Length == 0 || Bytes.Eq(baseName, "."u8))
    {
        outBin.Append("a.out"u8);
    }
    else
    {
        outBin.Append(baseName);
    }
}

static long ComputeArgsHash(in ArgList cliArgs)
{
    var all = new StringBuf(128);
    for (int k = 0; k < cliArgs.Count; k++)
    {
        all.Append(cliArgs.Get(k));
        all.AppendAscii((byte)'|');
    }
    return Bytes.Hash(all.AsUtf8());
}

static void ApplyAsanAttributes(ReadOnlySpan<byte> src, ref StringBuf dst)
{
    ReadOnlySpan<byte> pCold = ") noreturn cold nounwind {"u8;
    ReadOnlySpan<byte> pNounwind = ") nounwind {"u8;
    ReadOnlySpan<byte> pAttr2 = ") #2 {"u8;
    ReadOnlySpan<byte> pDefAttr2 = "attributes #2 = { nounwind }"u8;

    int k = 0;
    while (k < src.Length)
    {
        if (MatchesAt(src, k, pCold))
        {
            dst.Append(") #3 {"u8);
            k += pCold.Length;
            continue;
        }
        if (MatchesAt(src, k, pNounwind))
        {
            dst.Append(") #1 {"u8);
            k += pNounwind.Length;
            continue;
        }
        if (MatchesAt(src, k, pAttr2))
        {
            dst.Append(") #1 {"u8);
            k += pAttr2.Length;
            continue;
        }
        if (MatchesAt(src, k, pDefAttr2))
        {
            dst.Append("attributes #1 = { nounwind sanitize_address }\nattributes #3 = { noreturn cold nounwind sanitize_address }"u8);
            k += pDefAttr2.Length;
            continue;
        }
        dst.AppendAscii(src[k]);
        k += 1;
    }
}

static bool MatchesAt(ReadOnlySpan<byte> text, int pos, ReadOnlySpan<byte> pat)
{
    if (pos + pat.Length > text.Length) return false;
    return Bytes.Eq(text.Slice(pos, pat.Length), pat);
}

static void ReportIoError(ReadOnlySpan<byte> context, IoError err)
{
    var sb = new StringBuf(96);
    sb.Append("vib: "u8);
    sb.Append(context);
    sb.Append(" ("u8);
    sb.Append(err.KindName());
    sb.Append(")\n"u8);
    Stdio.WriteErr(sb.AsUtf8());
}

static void PrintUsage()
{
    Stdio.WriteErr("Usage: vib <check|emit-ll|build|run> <manifest|dir|file.cs...> [-o out] [--emit-ll out.ll] [-O0..-O3] [--cc clang] [--sysroot dir] [--asan] [--no-std] [-- args...]\n"u8);
}
