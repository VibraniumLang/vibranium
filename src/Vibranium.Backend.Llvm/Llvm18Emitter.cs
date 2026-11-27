using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Vibranium.Compiler.Core;

namespace Vibranium.Backend.Llvm;

public static class Llvm18Backend
{
    private static string? s_vibRtIr;
    private static string? s_vibStdIr;

    internal static string GetVibraniumRuntimeIr() =>
        s_vibRtIr ??= LoadEmbeddedIr("vib_rt.ll");

    internal static string GetVibraniumStdIr() =>
        s_vibStdIr ??= LoadEmbeddedIr("vib_std.ll");

    private static string LoadEmbeddedIr(string resourceName)
    {
        Assembly asm = typeof(Llvm18Backend).Assembly;
        using Stream stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded LLVM IR fragment '{resourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    public static string EmitModuleIr(MirModule module, LlvmEmitOptions? options = null)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        var emitter = new ModuleEmitter(module, options ?? new LlvmEmitOptions());
        return emitter.Emit();
    }

    public static (int ExitCode, string Output) CompileToExecutable(
        string llvmIr,
        string outputBinaryPath,
        int optLevel = 3,
        bool addressSanitizer = false,
        string? clangExecutable = null)
    {
        if (llvmIr is null)
        {
            throw new ArgumentNullException(nameof(llvmIr));
        }

        if (string.IsNullOrWhiteSpace(outputBinaryPath))
        {
            throw new ArgumentException("Output binary path must not be empty.", nameof(outputBinaryPath));
        }

        int clampedOpt = optLevel < 0 ? 0 : (optLevel > 3 ? 3 : optLevel);
        string fullOutputPath = Path.GetFullPath(outputBinaryPath);
        string? outDir = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string tempLlPath = Path.Combine(Path.GetTempPath(), $"vib_{Guid.NewGuid():N}.ll");
        try
        {
            File.WriteAllText(tempLlPath, llvmIr, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            string clangExe = !string.IsNullOrWhiteSpace(clangExecutable) ? clangExecutable! : ResolveClangExecutable();
            var args = new List<string>
            {
                "-x",
                "ir",
                QuoteArg(tempLlPath),
                $"-O{clampedOpt}",
            };

            if (addressSanitizer)
            {
                args.Add("-fsanitize=address,leak");
                args.Add("-fno-omit-frame-pointer");
                args.Add("-g");
            }

            args.Add("-o");
            args.Add(QuoteArg(fullOutputPath));

            var psi = new ProcessStartInfo
            {
                FileName = clangExe,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var outputBuilder = new StringBuilder();
            object sync = new object();

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (sync)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (sync)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            return (proc.ExitCode, outputBuilder.ToString().TrimEnd());
        }
        finally
        {
            try
            {
                if (File.Exists(tempLlPath))
                {
                    File.Delete(tempLlPath);
                }
            }
            catch
            {
                // Best-effort cleanup of temporary IR file.
            }
        }
    }

    private static string ResolveClangExecutable()
    {
        string[] candidates = { "/usr/bin/clang-18", "clang-18", "clang" };
        foreach (string candidate in candidates)
        {
            if (Path.IsPathRooted(candidate))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            else
            {
                return candidate;
            }
        }

        return "clang-18";
    }

    private static string QuoteArg(string arg)
    {
        if (arg.Length > 0 && arg.All(c => !char.IsWhiteSpace(c) && c != '"' && c != '\''))
        {
            return arg;
        }

        return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

internal sealed class ModuleEmitter
{
    private readonly MirModule _module;
    private readonly LlvmEmitOptions _options;

    private readonly Dictionary<string, (string Symbol, int ByteLength, byte[] Bytes)> _stringPool = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Order, string Symbol, byte[] Bytes)> _utf8BytePool = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, MirType Type)> _pendingDrops = new();
    private readonly Dictionary<string, string> _dropSymbols = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingLinearFrees = new();
    private readonly Dictionary<string, string> _linearFreeSymbols = new(StringComparer.Ordinal);
    private readonly HashSet<string> _definedFunctions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _referencedExternalFunctions = new(StringComparer.Ordinal);

    public ModuleEmitter(MirModule module, LlvmEmitOptions options)
    {
        _module = module;
        _options = options;
        foreach (MirFunction fn in _module.Functions)
        {
            _definedFunctions.Add(fn.Signature.Key);
        }
    }

    public string Emit()
    {
        var functionsIr = new StringBuilder();

        foreach (MirFunction fn in _module.Functions)
        {
            var fnEmitter = new FunctionEmitter(this, fn);
            functionsIr.AppendLine(fnEmitter.Emit());
        }

        string? entryKey = ResolveEntryPointKey();
        if (entryKey is not null && LookupSignature(entryKey) is { } entrySig)
        {
            functionsIr.AppendLine(EmitEntryWrapper(entrySig));
        }
        else
        {
            functionsIr.AppendLine(EmitEmptyEntryStub());
        }

        var dropsIr = new StringBuilder();
        while (_pendingDrops.Count > 0 || _pendingLinearFrees.Count > 0)
        {
            while (_pendingDrops.Count > 0)
            {
                (string _, MirType dropType) = _pendingDrops.Dequeue();
                dropsIr.AppendLine(EmitDropFunction(dropType));
            }

            while (_pendingLinearFrees.Count > 0)
            {
                string className = _pendingLinearFrees.Dequeue();
                dropsIr.AppendLine(EmitLinearFreeFunction(className));
            }
        }

        var sb = new StringBuilder();
        EmitRuntimeFragments(sb);
        EmitTypeDefinitions(sb);
        EmitExternalDeclarations(sb);
        EmitStringGlobals(sb);

        if (dropsIr.Length > 0)
        {
            sb.AppendLine("; --- Deterministic Drop & Cleanup Glue ---");
            sb.Append(dropsIr);
        }

        sb.AppendLine("; --- User MIR Functions ---");
        sb.Append(functionsIr);

        EmitAttributeGroups(sb);
        return sb.ToString();
    }

    internal MirModule Module => _module;
    internal LlvmEmitOptions Options => _options;

    internal string FuncAttrRef => _options.AddressSanitizer ? " #1" : " #2";

    internal MirFunctionSignature? LookupSignature(string key)
    {
        if (_module.Signatures.TryGetValue(key, out MirFunctionSignature? sig))
        {
            return sig;
        }

        foreach (MirFunction fn in _module.Functions)
        {
            if (string.Equals(fn.Signature.Key, key, StringComparison.Ordinal))
            {
                return fn.Signature;
            }
        }

        return null;
    }

    internal void MarkCalleeReferenced(string calleeKey)
    {
        if (!_definedFunctions.Contains(calleeKey))
        {
            _referencedExternalFunctions.Add(calleeKey);
        }
    }

    internal string GetFunctionSymbol(string key)
    {
        if (LookupSignature(key) is { NativeSymbol: { Length: > 0 } nativeSym })
        {
            return "@" + nativeSym;
        }

        return "@vib_fn_" + MangleIdentifier(key);
    }

    internal string GetStructTypeName(string structName) => "%struct." + MangleIdentifier(structName);

    internal string GetClassStorageTypeName(string className) => "%class." + MangleIdentifier(className);

    internal string GetReturnLlvmType(MirFunctionSignature sig)
    {
        if (sig.ReturnsByRef)
        {
            return "ptr";
        }

        return sig.ReturnType is MirType.VoidType ? "void" : GetValueLlvmType(sig.ReturnType);
    }

    internal string GetValueLlvmType(MirType type)
    {
        switch (type)
        {
            case MirType.VoidType:
                return "i8";
            case MirType.BoolType:
                return "i1";
            case MirType.Int8Type:
            case MirType.UInt8Type:
                return "i8";
            case MirType.Int16Type:
            case MirType.UInt16Type:
            case MirType.CharType:
                return "i16";
            case MirType.Int32Type:
            case MirType.UInt32Type:
            case MirType.EnumType:
                return "i32";
            case MirType.Int64Type:
            case MirType.UInt64Type:
            case MirType.NIntType:
            case MirType.NUIntType:
                return "i64";
            case MirType.Float32Type:
                return "float";
            case MirType.Float64Type:
                return "double";
            case MirType.NoneUnitType:
                return "i8";
            case MirType.StringLiteralType:
                return "{ ptr, i64 }";
            case MirType.ClassType:
            case MirType.OptionRefType:
            case MirType.OptionMutType:
            case MirType.RefType:
                return "ptr";
            case MirType.StructType st:
                return GetStructTypeName(st.Name);
            case MirType.OptionType opt:
                return $"{{ i1, {GetValueLlvmType(opt.Inner)} }}";
            case MirType.SomeWrapperType sw:
                return GetValueLlvmType(sw.Inner);
            case MirType.OkWrapperType okw:
                return GetValueLlvmType(okw.Ok);
            case MirType.ErrWrapperType errw:
                return GetValueLlvmType(errw.Err);
            case MirType.ResultType res:
                return $"{{ i1, {GetValueLlvmType(res.Ok)}, {GetValueLlvmType(res.Err)} }}";
            case MirType.VecType:
                return "{ ptr, i32, i32 }";
            case MirType.SpanType:
            case MirType.Utf8ScanViewType:
                return "{ ptr, i32 }";
            default:
                throw new InvalidOperationException($"Unsupported MIR type in LLVM lowering: {type}");
        }
    }

    internal static string GetZeroLiteral(string llvmType)
    {
        switch (llvmType)
        {
            case "i1":
                return "false";
            case "i8":
            case "i16":
            case "i32":
            case "i64":
                return "0";
            case "float":
            case "double":
                return "0x0000000000000000";
            case "ptr":
                return "null";
            default:
                return "zeroinitializer";
        }
    }

    internal (string Symbol, int ByteLength) InternString(string text)
    {
        if (_stringPool.TryGetValue(text, out var existing))
        {
            return (existing.Symbol, existing.ByteLength);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string symbol = $"@.str.{_stringPool.Count}";
        _stringPool[text] = (symbol, bytes.Length, bytes);
        return (symbol, bytes.Length);
    }

    internal string InternUtf8Bytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "@.b_empty";
        }

        string key = Convert.ToBase64String(bytes);
        if (_utf8BytePool.TryGetValue(key, out var existing))
        {
            return existing.Symbol;
        }

        int order = _utf8BytePool.Count;
        string sym = $"@.b{order}";
        _utf8BytePool[key] = (order, sym, bytes);
        return sym;
    }

    internal bool RequiresCleanupGlue(MirType type) =>
        _module.NeedsMemoryDrop(type) || _module.IsLinearResource(type);

    internal string GetOrScheduleCleanupSymbol(MirType type)
    {
        if (type is MirType.ClassType cls && IsDisposableClass(cls))
        {
            return GetOrScheduleLinearFreeSymbol(cls.Name);
        }

        string key = GetTypeKey(type);
        if (_dropSymbols.TryGetValue(key, out string? sym))
        {
            return sym;
        }

        string newSym = "@__vib_drop_" + MangleIdentifier(key);
        _dropSymbols[key] = newSym;
        _pendingDrops.Enqueue((key, type));
        return newSym;
    }

    internal string GetOrScheduleWorklistCleanupSymbol(MirType type)
    {
        string baseSym = GetOrScheduleCleanupSymbol(type);
        if (baseSym.StartsWith("@__vib_drop_", StringComparison.Ordinal))
        {
            return "@__vib_drop_wl_" + baseSym.Substring("@__vib_drop_".Length);
        }

        return baseSym;
    }

    internal string GetOrScheduleLinearFreeSymbol(string className)
    {
        if (_linearFreeSymbols.TryGetValue(className, out string? sym))
        {
            return sym;
        }

        string newSym = "@__vib_free_linear_" + MangleIdentifier(className);
        _linearFreeSymbols[className] = newSym;
        _pendingLinearFrees.Enqueue(className);
        return newSym;
    }

    internal bool IsDisposableClass(MirType.ClassType cls) =>
        cls.IsDisposable || (_module.Classes.TryGetValue(cls.Name, out MirClassDef? def) && def.IsDisposable);

    internal List<string> FormatAbiParameters(MirFunctionSignature sig, bool includeNames)
    {
        var result = new List<string>();
        string sharedAttrs = _options.SharedNoAlias ? "noalias readonly nonnull" : "readonly nonnull";
        string sharedSpanAttrs = _options.SharedNoAlias ? "noalias readonly" : "readonly";

        for (int i = 0; i < sig.Params.Count; i++)
        {
            MirParamSig p = sig.Params[i];
            MirType rawType = p.Type;

            if (p.PassKind == MirPassKind.ByValue && rawType is MirType.SpanType spanTy)
            {
                string ptrAttr = spanTy.IsReadOnly ? sharedSpanAttrs : "noalias";
                string ptrPart = includeNames ? $"ptr {ptrAttr} %arg_{i}_ptr" : $"ptr {ptrAttr}";
                string lenPart = includeNames ? $"i32 %arg_{i}_len" : "i32";
                result.Add(ptrPart);
                result.Add(lenPart);
                continue;
            }

            if (p.PassKind == MirPassKind.ByValue)
            {
                string llvmTy = GetValueLlvmType(rawType);
                string attrs = string.Empty;
                if (rawType is MirType.ClassType or MirType.OptionMutType)
                {
                    attrs = " noalias nonnull";
                }
                else if (rawType is MirType.OptionRefType)
                {
                    attrs = " " + sharedAttrs;
                }
                else if (rawType is MirType.RefType rt)
                {
                    attrs = rt.IsMutable ? " noalias nonnull" : (" " + sharedAttrs);
                }

                result.Add(includeNames ? $"{llvmTy}{attrs} %arg_{i}" : $"{llvmTy}{attrs}");
                continue;
            }

            if (p.PassKind is MirPassKind.ImplicitThisShared or MirPassKind.ImplicitThisMut)
            {
                string attrs = p.PassKind == MirPassKind.ImplicitThisMut ? "noalias nonnull" : sharedAttrs;
                result.Add(includeNames ? $"ptr {attrs} %arg_{i}" : $"ptr {attrs}");
                continue;
            }

            {
                string attrs = p.PassKind == MirPassKind.In ? sharedAttrs : "noalias nonnull";
                result.Add(includeNames ? $"ptr {attrs} %arg_{i}" : $"ptr {attrs}");
            }
        }

        return result;
    }

    private string? ResolveEntryPointKey()
    {
        if (!string.IsNullOrEmpty(_module.EntryPointKey))
        {
            return _module.EntryPointKey;
        }

        MirFunction? entryFn = _module.Functions.FirstOrDefault(f => f.IsEntryPoint);
        return entryFn?.Signature.Key;
    }

    private void EmitTypeDefinitions(StringBuilder sb)
    {
        if (_module.Structs.Count == 0 && _module.Classes.Count == 0)
        {
            return;
        }

        sb.AppendLine("; --- Type Definitions ---");

        var emittedStructs = new HashSet<string>(StringComparer.Ordinal);
        var visitingStructs = new HashSet<string>(StringComparer.Ordinal);

        void VisitStruct(string name)
        {
            if (emittedStructs.Contains(name) || !visitingStructs.Add(name))
            {
                return;
            }

            if (_module.Structs.TryGetValue(name, out MirStructDef? sdef))
            {
                foreach (MirFieldDef f in sdef.Fields)
                {
                    VisitNestedStructs(f.Type);
                }

                string fieldsText = sdef.Fields.Count == 0
                    ? "i8"
                    : string.Join(", ", sdef.Fields.Select(f => GetValueLlvmType(f.Type)));
                sb.AppendLine($"{GetStructTypeName(name)} = type {{ {fieldsText} }}");
            }
            else
            {
                sb.AppendLine($"{GetStructTypeName(name)} = type {{ i8 }}");
            }

            emittedStructs.Add(name);
        }

        void VisitNestedStructs(MirType t)
        {
            switch (t)
            {
                case MirType.StructType st:
                    VisitStruct(st.Name);
                    break;
                case MirType.OptionType opt:
                    VisitNestedStructs(opt.Inner);
                    break;
                case MirType.SomeWrapperType sw:
                    VisitNestedStructs(sw.Inner);
                    break;
                case MirType.OkWrapperType okw:
                    VisitNestedStructs(okw.Ok);
                    break;
                case MirType.ErrWrapperType errw:
                    VisitNestedStructs(errw.Err);
                    break;
                case MirType.ResultType res:
                    VisitNestedStructs(res.Ok);
                    VisitNestedStructs(res.Err);
                    break;
            }
        }

        foreach (string structName in _module.Structs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            VisitStruct(structName);
        }

        foreach (MirClassDef cdef in _module.Classes.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            foreach (MirFieldDef f in cdef.Fields)
            {
                VisitNestedStructs(f.Type);
            }

            string fieldsText = cdef.Fields.Count == 0
                ? "i8"
                : string.Join(", ", cdef.Fields.Select(f => GetValueLlvmType(f.Type)));
            sb.AppendLine($"{GetClassStorageTypeName(cdef.Name)} = type {{ {fieldsText} }}");
        }

        sb.AppendLine();
    }

    private void EmitRuntimeFragments(StringBuilder sb)
    {
        string rt = Llvm18Backend.GetVibraniumRuntimeIr();
        if (!string.Equals(_options.TargetTriple, "x86_64-pc-linux-gnu", StringComparison.Ordinal))
        {
            rt = rt.Replace(
                "target triple = \"x86_64-pc-linux-gnu\"",
                $"target triple = \"{EscapeLlvmStringAscii(_options.TargetTriple)}\"");
        }

        if (_options.AddressSanitizer)
        {
            rt = ApplyAsanToRuntimeFragment(rt);
        }

        sb.Append(rt);
        if (!rt.EndsWith("\n", StringComparison.Ordinal))
        {
            sb.AppendLine();
        }
        sb.AppendLine();

        bool usesNativeStd = _referencedExternalFunctions.Any(
            k => LookupSignature(k) is { NativeSymbol: { Length: > 0 } });
        bool includeStd = _options.IncludeStdNative ?? (_module.UsesStdNative || usesNativeStd);
        if (includeStd)
        {
            string std = Llvm18Backend.GetVibraniumStdIr();
            if (_options.AddressSanitizer)
            {
                std = ApplyAsanToRuntimeFragment(std);
            }

            sb.Append(std);
            if (!std.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.AppendLine();
            }
            sb.AppendLine();
        }
    }

    private static string ApplyAsanToRuntimeFragment(string ir)
    {
        return ir
            .Replace(") noreturn cold nounwind {", ") #3 {")
            .Replace(") nounwind {", ") #1 {");
    }

    private void EmitExternalDeclarations(StringBuilder sb)
    {
        bool emittedAny = false;
        foreach (string extKey in _referencedExternalFunctions.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (_module.Signatures.TryGetValue(extKey, out MirFunctionSignature? extSig))
            {
                if (!string.IsNullOrEmpty(extSig.NativeSymbol))
                {
                    continue;
                }

                if (!emittedAny)
                {
                    sb.AppendLine("; --- External MIR Function Declarations ---");
                    emittedAny = true;
                }

                string retTy = GetReturnLlvmType(extSig);
                string argsText = string.Join(", ", FormatAbiParameters(extSig, includeNames: false));
                sb.AppendLine($"declare {retTy} {GetFunctionSymbol(extKey)}({argsText})");
            }
        }

        if (emittedAny)
        {
            sb.AppendLine();
        }
    }

    private void EmitStringGlobals(StringBuilder sb)
    {
        if (_stringPool.Count == 0 && _utf8BytePool.Count == 0)
        {
            return;
        }

        sb.AppendLine("; --- Constant String & UTF-8 Byte Globals ---");
        foreach (var kvp in _stringPool.OrderBy(k => k.Value.Symbol, StringComparer.Ordinal))
        {
            var entry = kvp.Value;
            if (entry.ByteLength == 0)
            {
                sb.AppendLine($"{entry.Symbol} = private unnamed_addr constant [1 x i8] zeroinitializer");
            }
            else
            {
                sb.AppendLine($"{entry.Symbol} = private unnamed_addr constant [{entry.ByteLength} x i8] c\"{EscapeLlvmBytes(entry.Bytes)}\"");
            }
        }

        foreach (var entry in _utf8BytePool.Values.OrderBy(v => v.Order))
        {
            sb.AppendLine($"{entry.Symbol} = private unnamed_addr constant [{entry.Bytes.Length} x i8] c\"{EscapeLlvmBytes(entry.Bytes)}\"");
        }

        sb.AppendLine();
    }

    private string EmitEntryWrapper(MirFunctionSignature entrySig)
    {
        var sb = new StringBuilder();
        string fnSym = GetFunctionSymbol(entrySig.Key);
        sb.AppendLine($"define internal i32 @__vib_entry(){FuncAttrRef} {{");
        sb.AppendLine("entry:");
        if (entrySig.ReturnType is MirType.Int32Type && !entrySig.ReturnsByRef)
        {
            sb.AppendLine($"  %code = call i32 {fnSym}()");
            sb.AppendLine("  ret i32 %code");
        }
        else if (entrySig.ReturnType is MirType.VoidType && !entrySig.ReturnsByRef)
        {
            sb.AppendLine($"  call void {fnSym}()");
            sb.AppendLine("  ret i32 0");
        }
        else
        {
            string retTy = GetReturnLlvmType(entrySig);
            sb.AppendLine($"  %ret_ignored = call {retTy} {fnSym}()");
            if (!entrySig.ReturnsByRef && RequiresCleanupGlue(entrySig.ReturnType))
            {
                string dropSym = GetOrScheduleCleanupSymbol(entrySig.ReturnType);
                sb.AppendLine($"  %tmp = alloca {retTy}");
                sb.AppendLine($"  store {retTy} %ret_ignored, ptr %tmp");
                sb.AppendLine($"  call void {dropSym}(ptr %tmp)");
            }

            sb.AppendLine("  ret i32 0");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private string EmitEmptyEntryStub()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"define internal i32 @__vib_entry(){FuncAttrRef} {{");
        sb.AppendLine("entry:");
        sb.AppendLine("  ret i32 0");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitWorklistRootInit(StringBuilder sb)
    {
        sb.AppendLine("  %inline_buf = alloca [64 x { ptr, ptr }]");
        sb.AppendLine("  %wl = alloca { ptr, i64, i64, i1 }");
        sb.AppendLine("  %wl_dp = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 0");
        sb.AppendLine("  %wl_len = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 1");
        sb.AppendLine("  %wl_cap = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 2");
        sb.AppendLine("  %wl_hp = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 3");
        sb.AppendLine("  store ptr %inline_buf, ptr %wl_dp");
        sb.AppendLine("  store i64 0, ptr %wl_len");
        sb.AppendLine("  store i64 64, ptr %wl_cap");
        sb.AppendLine("  store i1 false, ptr %wl_hp");
    }

    private void EmitDropFieldWithWorklist(StringBuilder sb, MirType fieldType, string fieldPtrSsa)
    {
        if (fieldType is MirType.ClassType cls && IsDisposableClass(cls))
        {
            string linSym = GetOrScheduleLinearFreeSymbol(cls.Name);
            sb.AppendLine($"  call void {linSym}(ptr {fieldPtrSsa})");
        }
        else
        {
            string wlSym = GetOrScheduleWorklistCleanupSymbol(fieldType);
            sb.AppendLine($"  call void {wlSym}(ptr {fieldPtrSsa}, ptr %wl)");
        }
    }

    private string EmitDropFunction(MirType type)
    {
        string key = GetTypeKey(type);
        string sym = _dropSymbols[key];
        string mangled = sym.Substring("@__vib_drop_".Length);
        string wlSym = "@__vib_drop_wl_" + mangled;
        var sb = new StringBuilder();

        // Fast-path root wrapper: allocates bounded 64-entry inline worklist on stack, runs wl drop, drains in O(1) stack frames.
        if (type is MirType.VecType fastVec && !RequiresCleanupGlue(fastVec.Element))
        {
            sb.AppendLine($"define internal void {sym}(ptr %slot){FuncAttrRef} {{");
            sb.AppendLine("entry:");
            sb.AppendLine("  %dp_slot = getelementptr inbounds { ptr, i32, i32 }, ptr %slot, i32 0, i32 0");
            sb.AppendLine("  %data = load ptr, ptr %dp_slot");
            sb.AppendLine("  %is_null = icmp eq ptr %data, null");
            sb.AppendLine("  br i1 %is_null, label %done, label %do_drop");
            sb.AppendLine("do_drop:");
            sb.AppendLine("  store { ptr, i32, i32 } zeroinitializer, ptr %slot");
            sb.AppendLine("  call void @free(ptr %data)");
            sb.AppendLine("  br label %done");
            sb.AppendLine("done:");
            sb.AppendLine("  ret void");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"define internal void {wlSym}(ptr %slot, ptr %wl){FuncAttrRef} {{");
            sb.AppendLine("entry:");
            sb.AppendLine($"  call void {sym}(ptr %slot)");
            sb.AppendLine("  ret void");
            sb.AppendLine("}");
            return sb.ToString();
        }

        sb.AppendLine($"define internal void {sym}(ptr %slot){FuncAttrRef} {{");
        sb.AppendLine("entry:");
        EmitWorklistRootInit(sb);
        sb.AppendLine($"  call void {wlSym}(ptr %slot, ptr %wl)");
        sb.AppendLine("  call void @__vib_wl_drain(ptr %wl)");
        sb.AppendLine("  ret void");
        sb.AppendLine("}");
        sb.AppendLine();

        if (type is MirType.ClassType cls)
        {
            string stepSym = "@__vib_step_" + mangled;
            sb.AppendLine($"define internal void {stepSym}(ptr %obj, ptr %wl){FuncAttrRef} {{");
            sb.AppendLine("entry:");
            if (_module.Classes.TryGetValue(cls.Name, out MirClassDef? cdef))
            {
                string clsTy = GetClassStorageTypeName(cls.Name);
                for (int i = cdef.Fields.Count - 1; i >= 0; i--)
                {
                    MirFieldDef f = cdef.Fields[i];
                    if (RequiresCleanupGlue(f.Type))
                    {
                        sb.AppendLine($"  %fp_{i} = getelementptr inbounds {clsTy}, ptr %obj, i32 0, i32 {i}");
                        EmitDropFieldWithWorklist(sb, f.Type, $"%fp_{i}");
                    }
                }
            }

            sb.AppendLine("  call void @free(ptr %obj)");
            sb.AppendLine("  ret void");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"define internal void {wlSym}(ptr %slot, ptr %wl){FuncAttrRef} {{");
            sb.AppendLine("entry:");
            sb.AppendLine("  %obj = load ptr, ptr %slot");
            sb.AppendLine("  %is_null = icmp eq ptr %obj, null");
            sb.AppendLine("  br i1 %is_null, label %done, label %enqueue");
            sb.AppendLine("enqueue:");
            sb.AppendLine("  store ptr null, ptr %slot");
            sb.AppendLine($"  call void @__vib_wl_push(ptr %wl, ptr %obj, ptr {stepSym})");
            sb.AppendLine("  br label %done");
            sb.AppendLine("done:");
            sb.AppendLine("  ret void");
            sb.AppendLine("}");
            return sb.ToString();
        }

        sb.AppendLine($"define internal void {wlSym}(ptr %slot, ptr %wl){FuncAttrRef} {{");
        sb.AppendLine("entry:");

        switch (type)
        {
            case MirType.VecType vec:
            {
                string elemTy = GetValueLlvmType(vec.Element);
                sb.AppendLine("  %dp_slot = getelementptr inbounds { ptr, i32, i32 }, ptr %slot, i32 0, i32 0");
                sb.AppendLine("  %data = load ptr, ptr %dp_slot");
                sb.AppendLine("  %is_null = icmp eq ptr %data, null");
                sb.AppendLine("  br i1 %is_null, label %done, label %do_drop");
                sb.AppendLine("do_drop:");
                sb.AppendLine("  %cnt_slot = getelementptr inbounds { ptr, i32, i32 }, ptr %slot, i32 0, i32 1");
                sb.AppendLine("  %cnt = load i32, ptr %cnt_slot");
                sb.AppendLine("  store { ptr, i32, i32 } zeroinitializer, ptr %slot");
                sb.AppendLine("  %cnt64 = sext i32 %cnt to i64");
                sb.AppendLine("  %has_elems = icmp sgt i64 %cnt64, 0");
                sb.AppendLine("  br i1 %has_elems, label %elem_loop, label %free_buf");
                sb.AppendLine("elem_loop:");
                sb.AppendLine("  %idx = phi i64 [ 0, %do_drop ], [ %next_idx, %elem_loop ]");
                sb.AppendLine($"  %ep = getelementptr inbounds {elemTy}, ptr %data, i64 %idx");
                EmitDropFieldWithWorklist(sb, vec.Element, "%ep");
                sb.AppendLine("  %next_idx = add i64 %idx, 1");
                sb.AppendLine("  %more = icmp slt i64 %next_idx, %cnt64");
                sb.AppendLine("  br i1 %more, label %elem_loop, label %free_buf");
                sb.AppendLine("free_buf:");
                sb.AppendLine("  call void @free(ptr %data)");
                sb.AppendLine("  br label %done");
                sb.AppendLine("done:");
                sb.AppendLine("  ret void");
                break;
            }

            case MirType.OptionType opt:
            {
                string optLlvm = GetValueLlvmType(opt);
                sb.AppendLine($"  %tag_ptr = getelementptr inbounds {optLlvm}, ptr %slot, i32 0, i32 0");
                sb.AppendLine("  %tag = load i1, ptr %tag_ptr");
                sb.AppendLine("  br i1 %tag, label %drop_some, label %done");
                sb.AppendLine("drop_some:");
                sb.AppendLine("  store i1 false, ptr %tag_ptr");
                if (RequiresCleanupGlue(opt.Inner))
                {
                    sb.AppendLine($"  %val_ptr = getelementptr inbounds {optLlvm}, ptr %slot, i32 0, i32 1");
                    EmitDropFieldWithWorklist(sb, opt.Inner, "%val_ptr");
                }

                sb.AppendLine("  br label %done");
                sb.AppendLine("done:");
                sb.AppendLine("  ret void");
                break;
            }

            case MirType.SomeWrapperType sw:
            {
                if (RequiresCleanupGlue(sw.Inner))
                {
                    EmitDropFieldWithWorklist(sb, sw.Inner, "%slot");
                }

                sb.AppendLine("  ret void");
                break;
            }

            case MirType.OkWrapperType okw:
            {
                if (RequiresCleanupGlue(okw.Ok))
                {
                    EmitDropFieldWithWorklist(sb, okw.Ok, "%slot");
                }

                sb.AppendLine("  ret void");
                break;
            }

            case MirType.ErrWrapperType errw:
            {
                if (RequiresCleanupGlue(errw.Err))
                {
                    EmitDropFieldWithWorklist(sb, errw.Err, "%slot");
                }

                sb.AppendLine("  ret void");
                break;
            }

            case MirType.ResultType res:
            {
                string resLlvm = GetValueLlvmType(res);
                sb.AppendLine($"  %tag_ptr = getelementptr inbounds {resLlvm}, ptr %slot, i32 0, i32 0");
                sb.AppendLine("  %tag = load i1, ptr %tag_ptr");
                sb.AppendLine("  br i1 %tag, label %drop_ok, label %drop_err");
                sb.AppendLine("drop_ok:");
                sb.AppendLine("  store i1 false, ptr %tag_ptr");
                if (RequiresCleanupGlue(res.Ok))
                {
                    sb.AppendLine($"  %ok_ptr = getelementptr inbounds {resLlvm}, ptr %slot, i32 0, i32 1");
                    EmitDropFieldWithWorklist(sb, res.Ok, "%ok_ptr");
                }

                sb.AppendLine("  br label %done");
                sb.AppendLine("drop_err:");
                if (RequiresCleanupGlue(res.Err))
                {
                    sb.AppendLine($"  %err_ptr = getelementptr inbounds {resLlvm}, ptr %slot, i32 0, i32 2");
                    EmitDropFieldWithWorklist(sb, res.Err, "%err_ptr");
                }

                sb.AppendLine("  br label %done");
                sb.AppendLine("done:");
                sb.AppendLine("  ret void");
                break;
            }

            case MirType.StructType st:
            {
                if (_module.Structs.TryGetValue(st.Name, out MirStructDef? sdef))
                {
                    string stLlvm = GetStructTypeName(st.Name);
                    for (int i = sdef.Fields.Count - 1; i >= 0; i--)
                    {
                        MirFieldDef f = sdef.Fields[i];
                        if (RequiresCleanupGlue(f.Type))
                        {
                            sb.AppendLine($"  %fp_{i} = getelementptr inbounds {stLlvm}, ptr %slot, i32 0, i32 {i}");
                            EmitDropFieldWithWorklist(sb, f.Type, $"%fp_{i}");
                        }
                    }
                }

                sb.AppendLine("  ret void");
                break;
            }

            default:
                sb.AppendLine("  ret void");
                break;
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private string EmitLinearFreeFunction(string className)
    {
        string sym = _linearFreeSymbols[className];
        var sb = new StringBuilder();
        sb.AppendLine($"define internal void {sym}(ptr %slot){FuncAttrRef} {{");
        sb.AppendLine("entry:");
        EmitWorklistRootInit(sb);
        sb.AppendLine("  %obj = load ptr, ptr %slot");
        sb.AppendLine("  %is_null = icmp eq ptr %obj, null");
        sb.AppendLine("  br i1 %is_null, label %done, label %do_free");
        sb.AppendLine("do_free:");
        sb.AppendLine("  store ptr null, ptr %slot");
        if (_module.Classes.TryGetValue(className, out MirClassDef? cdef))
        {
            string clsTy = GetClassStorageTypeName(className);
            for (int i = cdef.Fields.Count - 1; i >= 0; i--)
            {
                MirFieldDef f = cdef.Fields[i];
                if (RequiresCleanupGlue(f.Type))
                {
                    sb.AppendLine($"  %fp_{i} = getelementptr inbounds {clsTy}, ptr %obj, i32 0, i32 {i}");
                    EmitDropFieldWithWorklist(sb, f.Type, $"%fp_{i}");
                }
            }
        }

        sb.AppendLine("  call void @free(ptr %obj)");
        sb.AppendLine("  call void @__vib_wl_drain(ptr %wl)");
        sb.AppendLine("  br label %done");
        sb.AppendLine("done:");
        sb.AppendLine("  ret void");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void EmitAttributeGroups(StringBuilder sb)
    {
        sb.AppendLine("attributes #0 = { noreturn nounwind }");
        if (_options.AddressSanitizer)
        {
            sb.AppendLine("attributes #1 = { nounwind sanitize_address }");
            sb.AppendLine("attributes #3 = { noreturn cold nounwind sanitize_address }");
        }
        else
        {
            sb.AppendLine("attributes #2 = { nounwind }");
        }
    }

    internal static string GetTypeKey(MirType type)
    {
        switch (type)
        {
            case MirType.VoidType:
                return "Void";
            case MirType.BoolType:
                return "Bool";
            case MirType.Int8Type:
                return "I8";
            case MirType.UInt8Type:
                return "U8";
            case MirType.Int16Type:
                return "I16";
            case MirType.UInt16Type:
                return "U16";
            case MirType.CharType:
                return "Char";
            case MirType.Int32Type:
                return "I32";
            case MirType.UInt32Type:
                return "U32";
            case MirType.Int64Type:
                return "I64";
            case MirType.UInt64Type:
                return "U64";
            case MirType.NIntType:
                return "NInt";
            case MirType.NUIntType:
                return "NUInt";
            case MirType.Float32Type:
                return "F32";
            case MirType.Float64Type:
                return "F64";
            case MirType.StringLiteralType:
                return "Str";
            case MirType.NoneUnitType:
                return "None";
            case MirType.EnumType e:
                return $"Enum:{e.Name}";
            case MirType.ClassType c:
                return $"Class:{c.Name}";
            case MirType.StructType s:
                return $"Struct:{s.Name}";
            case MirType.OptionType o:
                return $"Opt<{GetTypeKey(o.Inner)}>";
            case MirType.OptionRefType orf:
                return $"OptRef<{GetTypeKey(orf.Inner)}>";
            case MirType.OptionMutType om:
                return $"OptMut<{GetTypeKey(om.Inner)}>";
            case MirType.SomeWrapperType sw:
                return $"Some<{GetTypeKey(sw.Inner)}>";
            case MirType.OkWrapperType okw:
                return $"Ok<{GetTypeKey(okw.Ok)}>";
            case MirType.ErrWrapperType errw:
                return $"Err<{GetTypeKey(errw.Err)}>";
            case MirType.ResultType r:
                return $"Res<{GetTypeKey(r.Ok)},{GetTypeKey(r.Err)}>";
            case MirType.VecType v:
                return $"Vec<{GetTypeKey(v.Element)}>";
            case MirType.SpanType sp:
                return $"Span<{GetTypeKey(sp.Element)}:{(sp.IsReadOnly ? "ro" : "rw")}>";
            case MirType.Utf8ScanViewType uv:
                return uv.IsArgEnumerator ? "Utf8ArgEnum" : "Utf8Lines";
            case MirType.RefType rf:
                return $"Ref<{GetTypeKey(rf.Target)}:{(rf.IsMutable ? "mut" : "in")}>";
            default:
                return type.ToString() ?? "Unknown";
        }
    }

    internal static string MangleIdentifier(string raw)
    {
        uint hash = 2166136261u;
        var sb = new StringBuilder(raw.Length + 12);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            hash ^= c;
            hash *= 16777619u;

            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
            }
            else if (c == '<')
            {
                sb.Append("_LT_");
            }
            else if (c == '>')
            {
                sb.Append("_GT_");
            }
            else
            {
                sb.Append('_');
            }
        }

        if (sb.Length > 48)
        {
            sb.Length = 48;
        }

        sb.Append('_');
        sb.Append(hash.ToString("x8", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string EscapeLlvmStringAscii(string s)
    {
        return EscapeLlvmBytes(Encoding.UTF8.GetBytes(s));
    }

    private static string EscapeLlvmBytes(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            if (b >= 0x20 && b <= 0x7E && b != (byte)'"' && b != (byte)'\\')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('\\');
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }
}

internal sealed class LocalSlotInfo(
    MirLocal local,
    bool isRefSlot,
    MirType referentOrStorageType,
    string storageLlvmType,
    string allocaName,
    string? dropFlagName)
{
    public MirLocal Local { get; } = local;
    public bool IsRefSlot { get; } = isRefSlot;
    public MirType ReferentOrStorageType { get; } = referentOrStorageType;
    public string StorageLlvmType { get; } = storageLlvmType;
    public string AllocaName { get; } = allocaName;
    public string? DropFlagName { get; } = dropFlagName;
}

internal sealed class FunctionEmitter(ModuleEmitter mod, MirFunction fn)
{
    private readonly ModuleEmitter _mod = mod;
    private readonly MirFunction _fn = fn;
    private readonly Dictionary<int, LocalSlotInfo> _locals = new();
    private readonly Dictionary<MirRvalue.StackSpan, string> _stackSpanAllocas = new();
    private readonly List<string> _extraEntryAllocas = new();
    private readonly StringBuilder _body = new();
    private int _tempCounter;
    private int _labelCounter;

    public string Emit()
    {
        PrepareLocalsAndStackSpans();

        foreach (MirBasicBlock block in _fn.Blocks)
        {
            EmitLabel($"bb_{block.Id}");
            foreach (MirStatement stmt in block.Statements)
            {
                EmitStatement(stmt);
            }

            EmitTerminator(block.Terminator);
        }

        var sb = new StringBuilder();
        string retTy = _mod.GetReturnLlvmType(_fn.Signature);
        string fnSym = _mod.GetFunctionSymbol(_fn.Signature.Key);
        string argsText = string.Join(", ", _mod.FormatAbiParameters(_fn.Signature, includeNames: true));
        string linkage = _fn.Signature.IsInternalHelper ? "internal " : string.Empty;

        sb.AppendLine($"define {linkage}{retTy} {fnSym}({argsText}){_mod.FuncAttrRef} {{");
        sb.AppendLine("entry:");

        foreach (MirLocal loc in _fn.Locals)
        {
            if (loc.IsAnalysisOnlyBorrow) continue;
            LocalSlotInfo info = _locals[loc.Id];
            sb.AppendLine($"  {info.AllocaName} = alloca {info.StorageLlvmType}");
            if (info.DropFlagName is not null)
            {
                sb.AppendLine($"  {info.DropFlagName} = alloca i1");
            }
        }

        foreach (var kvp in _stackSpanAllocas)
        {
            MirRvalue.StackSpan ss = kvp.Key;
            int count = ss.Elements.Count > 0 ? ss.Elements.Count : ss.ZeroInitLength;
            int allocCount = count > 0 ? count : 1;
            string elemLlvm = _mod.GetValueLlvmType(ss.ElementType);
            sb.AppendLine($"  {kvp.Value} = alloca [{allocCount} x {elemLlvm}]");
        }

        foreach (string extraAlloca in _extraEntryAllocas)
        {
            sb.AppendLine($"  {extraAlloca}");
        }

        List<MirLocal> paramLocals = _fn.Locals.Where(l => l.IsParam).ToList();
        int paramCount = Math.Min(paramLocals.Count, _fn.Signature.Params.Count);
        var initializedParamIds = new HashSet<int>();
        for (int i = 0; i < paramCount; i++)
        {
            MirLocal pLoc = paramLocals[i];
            MirParamSig pSig = _fn.Signature.Params[i];
            LocalSlotInfo info = _locals[pLoc.Id];
            initializedParamIds.Add(pLoc.Id);

            if (pSig.PassKind == MirPassKind.ByValue && pSig.Type is MirType.SpanType)
            {
                sb.AppendLine($"  %span_init0_{i} = insertvalue {{ ptr, i32 }} poison, ptr %arg_{i}_ptr, 0");
                sb.AppendLine($"  %span_init1_{i} = insertvalue {{ ptr, i32 }} %span_init0_{i}, i32 %arg_{i}_len, 1");
                sb.AppendLine($"  store {{ ptr, i32 }} %span_init1_{i}, ptr {info.AllocaName}");
            }
            else if (info.IsRefSlot)
            {
                sb.AppendLine($"  store ptr %arg_{i}, ptr {info.AllocaName}");
            }
            else
            {
                string paramLlvm = info.StorageLlvmType;
                sb.AppendLine($"  store {paramLlvm} %arg_{i}, ptr {info.AllocaName}");
                if (info.DropFlagName is not null)
                {
                    string dropInit = pSig.PassKind == MirPassKind.ByValue ? "true" : "false";
                    sb.AppendLine($"  store i1 {dropInit}, ptr {info.DropFlagName}");
                }
            }
        }

        foreach (MirLocal loc in _fn.Locals)
        {
            if (loc.IsAnalysisOnlyBorrow || initializedParamIds.Contains(loc.Id))
            {
                continue;
            }

            LocalSlotInfo info = _locals[loc.Id];
            sb.AppendLine($"  store {info.StorageLlvmType} {ModuleEmitter.GetZeroLiteral(info.StorageLlvmType)}, ptr {info.AllocaName}");
            if (info.DropFlagName is not null)
            {
                sb.AppendLine($"  store i1 false, ptr {info.DropFlagName}");
            }
        }

        if (_fn.Blocks.Count > 0)
        {
            sb.AppendLine($"  br label %bb_{_fn.Blocks[0].Id}");
            sb.Append(_body);
        }
        else
        {
            if (retTy == "void")
            {
                sb.AppendLine("  ret void");
            }
            else
            {
                sb.AppendLine($"  ret {retTy} {ModuleEmitter.GetZeroLiteral(retTy)}");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private void PrepareLocalsAndStackSpans()
    {
        foreach (MirLocal loc in _fn.Locals)
        {
            if (loc.IsAnalysisOnlyBorrow) continue;
            MirType unwrapped = loc.Type is MirType.RefType rt ? rt.Target : loc.Type;
            bool isIndirectParam = loc.IsParam &&
                (loc.PassKind is MirPassKind.In or MirPassKind.Ref or MirPassKind.Out ||
                 (loc.PassKind is MirPassKind.ImplicitThisShared or MirPassKind.ImplicitThisMut &&
                  unwrapped is not MirType.ClassType));

            bool isRefSlot = loc.IsRefLocal || loc.Type is MirType.RefType || isIndirectParam;
            MirType refOrStorageType = isRefSlot ? unwrapped : loc.Type;
            string storageLlvm = isRefSlot ? "ptr" : _mod.GetValueLlvmType(loc.Type);

            bool needsDropFlag = !isRefSlot && _mod.RequiresCleanupGlue(loc.Type);
            string allocaName = $"%loc_{loc.Id}";
            string? dropName = needsDropFlag ? $"%drop_{loc.Id}" : null;

            _locals[loc.Id] = new LocalSlotInfo(
                loc,
                isRefSlot,
                refOrStorageType,
                storageLlvm,
                allocaName,
                dropName);
        }

        int ssIndex = 0;
        foreach (MirBasicBlock blk in _fn.Blocks)
        {
            foreach (MirStatement stmt in blk.Statements)
            {
                if (stmt is MirStatement.Assign { Rvalue: MirRvalue.StackSpan ss } &&
                    !_stackSpanAllocas.ContainsKey(ss))
                {
                    _stackSpanAllocas[ss] = $"%stackspan_{ssIndex++}";
                }
            }
        }
    }

    private string NextTemp() => $"%t{_tempCounter++}";

    private string NextLabel(string prefix) => $"{prefix}_{_labelCounter++}";

    private string AllocTempSlot(string llvmType)
    {
        string slot = $"%tmp_slot_{_extraEntryAllocas.Count}";
        _extraEntryAllocas.Add($"{slot} = alloca {llvmType}");
        return slot;
    }

    private void EmitInst(string instruction) => _body.Append("  ").AppendLine(instruction);

    private void EmitLabel(string label) => _body.Append(label).AppendLine(":");

    private void EmitStatement(MirStatement stmt)
    {
        switch (stmt)
        {
            case MirStatement.Assign assign:
                EmitAssign(assign);
                break;

            case MirStatement.RememberRefRepointLoan:
            case MirStatement.KeepAnalysisBorrow:
            case MirStatement.BindRef bindRef when _fn.Locals[bindRef.RefLocalId].IsAnalysisOnlyBorrow:
                break;

            case MirStatement.BindRef bindRef:
            {
                (string targetPtr, _) = EmitPlaceAddress(bindRef.Target);
                LocalSlotInfo refSlot = _locals[bindRef.RefLocalId];
                EmitInst($"store ptr {targetPtr}, ptr {refSlot.AllocaName}");
                break;
            }

            case MirStatement.Call call:
                EmitCall(call);
                break;

            case MirStatement.Intrinsic intr:
                EmitIntrinsic(intr);
                break;

            case MirStatement.DisposeResource disp:
                EmitDisposeResource(disp);
                break;

            case MirStatement.ScopeExit exit:
            {
                foreach (int localId in exit.ExitingLocals.OrderByDescending(id => id))
                {
                    if (_locals.TryGetValue(localId, out LocalSlotInfo? info) && info.DropFlagName is not null)
                    {
                        EmitDropLocalIfLive(info);
                    }
                }

                break;
            }
        }
    }

    private void EmitAssign(MirStatement.Assign assign)
    {
        if (assign.Target.Projections.Count == 0 &&
            _locals.TryGetValue(assign.Target.LocalId, out LocalSlotInfo? dstInfo) &&
            dstInfo.IsRefSlot &&
            GetRvalueType(assign.Rvalue) is MirType.RefType)
        {
            string refPtrVal = EmitRvalue(assign.Rvalue);
            EmitInst($"store ptr {refPtrVal}, ptr {dstInfo.AllocaName}");
            return;
        }

        if (assign.Target.Projections.Count == 0 &&
            _locals.TryGetValue(assign.Target.LocalId, out LocalSlotInfo? directLocal) &&
            !directLocal.IsRefSlot)
        {
            // Evaluate RHS BEFORE dropping LHS so self-moves like `list = Prepend(list, x)` work safely!
            string rhsVal = EmitRvalue(assign.Rvalue);
            StoreIntoDirectLocalWithOverwriteDrop(directLocal, rhsVal);
            return;
        }

        (string dstPtr, MirType dstType) = EmitPlaceAddress(assign.Target);
        string projectedRhsVal = EmitRvalue(assign.Rvalue);
        if (_mod.RequiresCleanupGlue(dstType))
        {
            string dropSym = _mod.GetOrScheduleCleanupSymbol(dstType);
            EmitInst($"call void {dropSym}(ptr {dstPtr})");
        }

        string dstLlvm = _mod.GetValueLlvmType(dstType);
        EmitInst($"store {dstLlvm} {projectedRhsVal}, ptr {dstPtr}");
    }

    private void StoreIntoDirectLocalWithOverwriteDrop(LocalSlotInfo directLocal, string ssaValue)
    {
        if (directLocal.DropFlagName is not null)
        {
            EmitDropLocalIfLive(directLocal);
        }

        EmitInst($"store {directLocal.StorageLlvmType} {ssaValue}, ptr {directLocal.AllocaName}");
        if (directLocal.DropFlagName is not null)
        {
            EmitInst($"store i1 true, ptr {directLocal.DropFlagName}");
        }
    }

    private void StoreIntoPlaceWithOverwriteDrop(MirPlace target, string ssaValue)
    {
        if (target.Projections.Count == 0 &&
            _locals.TryGetValue(target.LocalId, out LocalSlotInfo? directLocal) &&
            !directLocal.IsRefSlot)
        {
            StoreIntoDirectLocalWithOverwriteDrop(directLocal, ssaValue);
            return;
        }

        (string dstPtr, MirType dstType) = EmitPlaceAddress(target);
        if (_mod.RequiresCleanupGlue(dstType))
        {
            string dropSym = _mod.GetOrScheduleCleanupSymbol(dstType);
            EmitInst($"call void {dropSym}(ptr {dstPtr})");
        }

        string dstLlvm = _mod.GetValueLlvmType(dstType);
        EmitInst($"store {dstLlvm} {ssaValue}, ptr {dstPtr}");
    }

    private void SpillAndDropValueIfNeeded(MirType type, string ssaValue)
    {
        if (!_mod.RequiresCleanupGlue(type))
        {
            return;
        }

        string llvmTy = _mod.GetValueLlvmType(type);
        string slot = AllocTempSlot(llvmTy);
        string dropSym = _mod.GetOrScheduleCleanupSymbol(type);
        EmitInst($"store {llvmTy} {ssaValue}, ptr {slot}");
        EmitInst($"call void {dropSym}(ptr {slot})");
    }

    private void EmitDropLocalIfLive(LocalSlotInfo info)
    {
        if (info.DropFlagName is null)
        {
            return;
        }

        string dropSym = _mod.GetOrScheduleCleanupSymbol(info.ReferentOrStorageType);
        string liveFlag = NextTemp();
        string doDropLbl = NextLabel("drop_live");
        string afterDropLbl = NextLabel("drop_cont");

        EmitInst($"{liveFlag} = load i1, ptr {info.DropFlagName}");
        EmitInst($"br i1 {liveFlag}, label %{doDropLbl}, label %{afterDropLbl}");
        EmitLabel(doDropLbl);
        EmitInst($"store i1 false, ptr {info.DropFlagName}");
        EmitInst($"call void {dropSym}(ptr {info.AllocaName})");
        EmitInst($"br label %{afterDropLbl}");
        EmitLabel(afterDropLbl);
    }

    private void EmitDisposeResource(MirStatement.DisposeResource disp)
    {
        if (!_locals.TryGetValue(disp.LocalId, out LocalSlotInfo? info))
        {
            return;
        }

        _mod.MarkCalleeReferenced(disp.DisposeMethodKey);
        string dispSym = _mod.GetFunctionSymbol(disp.DisposeMethodKey);
        MirFunctionSignature? dispSig = _mod.LookupSignature(disp.DisposeMethodKey);

        string doDispLbl = NextLabel("dispose_live");
        string contLbl = NextLabel("dispose_cont");

        if (info.DropFlagName is not null)
        {
            string live = NextTemp();
            EmitInst($"{live} = load i1, ptr {info.DropFlagName}");
            EmitInst($"br i1 {live}, label %{doDispLbl}, label %{contLbl}");
        }
        else
        {
            EmitInst($"br label %{doDispLbl}");
        }

        EmitLabel(doDispLbl);
        if (info.DropFlagName is not null)
        {
            EmitInst($"store i1 false, ptr {info.DropFlagName}");
        }

        string callArgExpr;
        if (info.ReferentOrStorageType is MirType.ClassType)
        {
            string objPtr = NextTemp();
            EmitInst($"{objPtr} = load ptr, ptr {info.AllocaName}");
            callArgExpr = $"ptr {objPtr}";
        }
        else
        {
            callArgExpr = $"ptr {info.AllocaName}";
        }

        if (dispSig is not null && dispSig.ReturnType is not MirType.VoidType)
        {
            string retTy = _mod.GetReturnLlvmType(dispSig);
            string dummy = NextTemp();
            EmitInst($"{dummy} = call {retTy} {dispSym}({callArgExpr})");
        }
        else
        {
            EmitInst($"call void {dispSym}({callArgExpr})");
        }

        if (_mod.RequiresCleanupGlue(info.ReferentOrStorageType))
        {
            string cleanupSym = _mod.GetOrScheduleCleanupSymbol(info.ReferentOrStorageType);
            EmitInst($"call void {cleanupSym}(ptr {info.AllocaName})");
        }

        EmitInst($"br label %{contLbl}");
        EmitLabel(contLbl);
    }

    private void EmitCall(MirStatement.Call call)
    {
        _mod.MarkCalleeReferenced(call.CalleeKey);
        MirFunctionSignature sig = _mod.LookupSignature(call.CalleeKey)
            ?? throw new InvalidOperationException($"Missing MIR function signature for callee '{call.CalleeKey}'.");

        var llvmArgs = new List<string>();
        var postCallOutOrRefLocals = new List<LocalSlotInfo>();

        for (int i = 0; i < call.Args.Count; i++)
        {
            MirCallArg arg = call.Args[i];
            MirParamSig pSig = sig.Params[i];
            MirType pType = pSig.Type;

            if (pSig.PassKind == MirPassKind.ByValue && pType is MirType.SpanType)
            {
                string spanVal = arg.ValueOperand is not null
                    ? EmitOperand(arg.ValueOperand)
                    : LoadPlaceValueWithoutMove(arg.BorrowPlace!, pType);
                string sPtr = NextTemp();
                string sLen = NextTemp();
                EmitInst($"{sPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                EmitInst($"{sLen} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                llvmArgs.Add($"ptr {sPtr}");
                llvmArgs.Add($"i32 {sLen}");
                continue;
            }

            if (pSig.PassKind == MirPassKind.ByValue)
            {
                string argLlvm = _mod.GetValueLlvmType(pType);
                string val = arg.ValueOperand is not null
                    ? EmitOperand(arg.ValueOperand)
                    : EmitOperandFromPlaceByValue(arg.BorrowPlace!, pType);
                llvmArgs.Add($"{argLlvm} {val}");
                continue;
            }

            if (pSig.PassKind is MirPassKind.ImplicitThisShared or MirPassKind.ImplicitThisMut &&
                (pType is MirType.ClassType || (pType is MirType.RefType rt && rt.Target is MirType.ClassType)))
            {
                string objPtr;
                if (arg.BorrowPlace is not null)
                {
                    (string slotAddr, _) = EmitPlaceAddress(arg.BorrowPlace);
                    objPtr = NextTemp();
                    EmitInst($"{objPtr} = load ptr, ptr {slotAddr}");
                }
                else if (arg.ValueOperand is MirOperand.CopyOrMove cm)
                {
                    (string slotAddr, _) = EmitPlaceAddress(cm.Place);
                    objPtr = NextTemp();
                    EmitInst($"{objPtr} = load ptr, ptr {slotAddr}");
                }
                else
                {
                    objPtr = EmitOperand(arg.ValueOperand!);
                }

                llvmArgs.Add($"ptr {objPtr}");
                continue;
            }

            string borrowPtr;
            if (arg.BorrowPlace is not null)
            {
                (borrowPtr, _) = EmitPlaceAddress(arg.BorrowPlace);
                if (pSig.PassKind is MirPassKind.Out or MirPassKind.Ref &&
                    arg.BorrowPlace.Projections.Count == 0 &&
                    _locals.TryGetValue(arg.BorrowPlace.LocalId, out LocalSlotInfo? outLocal) &&
                    !outLocal.IsRefSlot &&
                    outLocal.DropFlagName is not null)
                {
                    postCallOutOrRefLocals.Add(outLocal);
                }
            }
            else if (arg.ValueOperand is MirOperand.CopyOrMove cm)
            {
                (borrowPtr, _) = EmitPlaceAddress(cm.Place);
            }
            else
            {
                string cLlvm = _mod.GetValueLlvmType(pType);
                string cVal = EmitOperand(arg.ValueOperand!);
                borrowPtr = AllocTempSlot(cLlvm);
                EmitInst($"store {cLlvm} {cVal}, ptr {borrowPtr}");
            }

            llvmArgs.Add($"ptr {borrowPtr}");
        }

        string fnSym = _mod.GetFunctionSymbol(call.CalleeKey);
        string joinedArgs = string.Join(", ", llvmArgs);

        if (sig.ReturnsByRef)
        {
            string retPtr = NextTemp();
            EmitInst($"{retPtr} = call ptr {fnSym}({joinedArgs})");
            StoreRefResult(call.RefDestLocalId, call.Dest, retPtr);
        }
        else if (sig.ReturnType is MirType.VoidType)
        {
            EmitInst($"call void {fnSym}({joinedArgs})");
        }
        else
        {
            string retLlvm = _mod.GetValueLlvmType(sig.ReturnType);
            string retVal = NextTemp();
            EmitInst($"{retVal} = call {retLlvm} {fnSym}({joinedArgs})");
            if (call.Dest is not null)
            {
                StoreIntoPlaceWithOverwriteDrop(call.Dest, retVal);
            }
            else
            {
                SpillAndDropValueIfNeeded(sig.ReturnType, retVal);
            }
        }

        foreach (LocalSlotInfo outLocal in postCallOutOrRefLocals)
        {
            EmitInst($"store i1 true, ptr {outLocal.DropFlagName}");
        }
    }

    private void StoreRefResult(int? refDestLocalId, MirPlace? dest, string ptrValue)
    {
        if (refDestLocalId is int refId && _locals.TryGetValue(refId, out LocalSlotInfo? refSlot))
        {
            EmitInst($"store ptr {ptrValue}, ptr {refSlot.AllocaName}");
        }
        else if (dest is not null)
        {
            if (dest.Projections.Count == 0 &&
                _locals.TryGetValue(dest.LocalId, out LocalSlotInfo? dSlot) &&
                dSlot.IsRefSlot)
            {
                EmitInst($"store ptr {ptrValue}, ptr {dSlot.AllocaName}");
            }
            else
            {
                (string dstAddr, _) = EmitPlaceAddress(dest);
                EmitInst($"store ptr {ptrValue}, ptr {dstAddr}");
            }
        }
    }

    private void EmitIntrinsic(MirStatement.Intrinsic intr)
    {
        switch (intr.Kind)
        {
            case MirIntrinsicKind.IoWriteInt64:
            case MirIntrinsicKind.IoWriteLineInt64:
            {
                MirType argType = GetOperandType(intr.ValueArgs[0]);
                string v = EmitOperand(intr.ValueArgs[0]);
                string srcLlvm = _mod.GetValueLlvmType(argType);
                if (srcLlvm != "i64")
                {
                    string ext = NextTemp();
                    EmitInst($"{ext} = sext {srcLlvm} {v} to i64");
                    v = ext;
                }
                EmitInst($"call void @__vib_print_i64(i64 {v})");
                if (intr.Kind == MirIntrinsicKind.IoWriteLineInt64)
                {
                    EmitInst("call void @__vib_print_nl()");
                }
                break;
            }

            case MirIntrinsicKind.IoWriteUInt64:
            case MirIntrinsicKind.IoWriteLineUInt64:
            {
                MirType argType = GetOperandType(intr.ValueArgs[0]);
                string v = EmitOperand(intr.ValueArgs[0]);
                string srcLlvm = _mod.GetValueLlvmType(argType);
                if (srcLlvm != "i64")
                {
                    string ext = NextTemp();
                    EmitInst($"{ext} = zext {srcLlvm} {v} to i64");
                    v = ext;
                }
                EmitInst($"call void @__vib_print_u64(i64 {v})");
                if (intr.Kind == MirIntrinsicKind.IoWriteLineUInt64)
                {
                    EmitInst("call void @__vib_print_nl()");
                }
                break;
            }

            case MirIntrinsicKind.IoWriteBool:
            case MirIntrinsicKind.IoWriteLineBool:
            {
                string v = EmitOperand(intr.ValueArgs[0]);
                EmitInst($"call void @__vib_print_bool(i1 {v})");
                if (intr.Kind == MirIntrinsicKind.IoWriteLineBool)
                {
                    EmitInst("call void @__vib_print_nl()");
                }
                break;
            }

            case MirIntrinsicKind.IoWriteStr:
            case MirIntrinsicKind.IoWriteLineStr:
            {
                string s = EmitOperand(intr.ValueArgs[0]);
                string p = NextTemp();
                string l = NextTemp();
                EmitInst($"{p} = extractvalue {{ ptr, i64 }} {s}, 0");
                EmitInst($"{l} = extractvalue {{ ptr, i64 }} {s}, 1");
                EmitInst($"call void @__vib_print_str(ptr {p}, i64 {l})");
                if (intr.Kind == MirIntrinsicKind.IoWriteLineStr)
                {
                    EmitInst("call void @__vib_print_nl()");
                }
                break;
            }

            case MirIntrinsicKind.IoWriteLineEmpty:
            {
                EmitInst("call void @__vib_print_nl()");
                break;
            }

            case MirIntrinsicKind.StdioWriteOut:
            case MirIntrinsicKind.StdioWriteErr:
            {
                string spanVal = EmitOperand(intr.ValueArgs[0]);
                string p = NextTemp();
                string l = NextTemp();
                EmitInst($"{p} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                EmitInst($"{l} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                string callee = intr.Kind == MirIntrinsicKind.StdioWriteOut ? "@__vib_write_out" : "@__vib_write_err";
                EmitInst($"call void {callee}(ptr {p}, i32 {l})");
                break;
            }

            case MirIntrinsicKind.Panic:
            {
                string s = EmitOperand(intr.ValueArgs[0]);
                string p = NextTemp();
                string l = NextTemp();
                EmitInst($"{p} = extractvalue {{ ptr, i64 }} {s}, 0");
                EmitInst($"{l} = extractvalue {{ ptr, i64 }} {s}, 1");
                EmitInst($"call void @__vib_panic(ptr {p}, i64 {l})");
                break;
            }

            case MirIntrinsicKind.StringAsUtf8:
            {
                if (intr.ValueArgs[0] is MirOperand.Const { Value: MirConstant.StringConst sc })
                {
                    byte[] utf8 = Encoding.UTF8.GetBytes(sc.Value);
                    string sym = _mod.InternUtf8Bytes(utf8);
                    AssignIntrinsicResult(intr, $"{{ ptr {sym}, i32 {utf8.Length} }}");
                }
                else
                {
                    string s = EmitOperand(intr.ValueArgs[0]);
                    string sPtr = NextTemp();
                    string sLen64 = NextTemp();
                    EmitInst($"{sPtr} = extractvalue {{ ptr, i64 }} {s}, 0");
                    EmitInst($"{sLen64} = extractvalue {{ ptr, i64 }} {s}, 1");
                    string badLen = NextTemp();
                    EmitInst($"{badLen} = icmp ugt i64 {sLen64}, 2147483647");
                    EmitPanicIfTrue(badLen, "arithmetic overflow");
                    string sLen32 = NextTemp();
                    string sp0 = NextTemp();
                    string sp1 = NextTemp();
                    EmitInst($"{sLen32} = trunc i64 {sLen64} to i32");
                    EmitInst($"{sp0} = insertvalue {{ ptr, i32 }} poison, ptr {sPtr}, 0");
                    EmitInst($"{sp1} = insertvalue {{ ptr, i32 }} {sp0}, i32 {sLen32}, 1");
                    AssignIntrinsicResult(intr, sp1);
                }
                break;
            }

            case MirIntrinsicKind.SpanToUtf8View:
            case MirIntrinsicKind.Utf8ViewToSpan:
            {
                string viewVal = EmitOperand(intr.ValueArgs[0]);
                AssignIntrinsicResult(intr, viewVal);
                break;
            }

            case MirIntrinsicKind.Utf8ScanRaw:
            {
                string spanVal = EmitOperand(intr.ValueArgs[0]);
                string posVal = EnsureI32(EmitOperand(intr.ValueArgs[1]), GetOperandType(intr.ValueArgs[1]));
                string modeVal = EnsureI32(EmitOperand(intr.ValueArgs[2]), GetOperandType(intr.ValueArgs[2]));
                string dataPtr = NextTemp();
                string dataLen = NextTemp();
                string packed = NextTemp();
                EmitInst($"{dataPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                EmitInst($"{dataLen} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                EmitInst($"{packed} = call i64 @__vib_utf8_scan(ptr {dataPtr}, i32 {dataLen}, i32 {posVal}, i32 {modeVal})");
                AssignIntrinsicResult(intr, packed);
                break;
            }

            case MirIntrinsicKind.OptionAsRef:
            case MirIntrinsicKind.OptionAsMut:
            {
                (string slotPtr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
                AssignIntrinsicResult(intr, slotPtr);
                break;
            }

            case MirIntrinsicKind.OptionTake:
            {
                (string slotPtr, MirType slotType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string optLlvm = _mod.GetValueLlvmType(slotType);
                string prev = NextTemp();
                EmitInst($"{prev} = load {optLlvm}, ptr {slotPtr}");
                EmitInst($"store {optLlvm} zeroinitializer, ptr {slotPtr}");
                AssignIntrinsicResult(intr, prev);
                break;
            }

            case MirIntrinsicKind.OptionReplace:
            {
                string replVal = EmitOperand(intr.ValueArgs[0]);
                (string slotPtr, MirType slotType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string optLlvm = _mod.GetValueLlvmType(slotType);
                string prev = NextTemp();
                EmitInst($"{prev} = load {optLlvm}, ptr {slotPtr}");
                EmitInst($"store {optLlvm} {replVal}, ptr {slotPtr}");
                AssignIntrinsicResult(intr, prev);
                break;
            }

            case MirIntrinsicKind.OptionUnwrap:
            {
                string optLlvm;
                string optVal;
                if (intr.ValueArgs.Count > 0)
                {
                    MirType optTy = GetOperandType(intr.ValueArgs[0]);
                    optLlvm = _mod.GetValueLlvmType(optTy);
                    optVal = EmitOperand(intr.ValueArgs[0]);
                }
                else
                {
                    (_, MirType slotTy) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    optLlvm = _mod.GetValueLlvmType(slotTy);
                    optVal = EmitOperandFromPlaceByValue(intr.PlaceArgs[0], slotTy);
                }

                string hasVal = NextTemp();
                EmitInst($"{hasVal} = extractvalue {optLlvm} {optVal}, 0");
                EmitPanicIfFalse(hasVal, "called Option.Unwrap() on None");
                string payload = NextTemp();
                EmitInst($"{payload} = extractvalue {optLlvm} {optVal}, 1");
                AssignIntrinsicResult(intr, payload);
                break;
            }

            case MirIntrinsicKind.OptionExpect:
            {
                MirType optTy = GetOperandType(intr.ValueArgs[0]);
                string optLlvm = _mod.GetValueLlvmType(optTy);
                string optVal = EmitOperand(intr.ValueArgs[0]);
                string msgVal = EmitOperand(intr.ValueArgs[1]);
                string hasVal = NextTemp();
                EmitInst($"{hasVal} = extractvalue {optLlvm} {optVal}, 0");
                EmitPanicDynamicIfFalse(hasVal, msgVal);
                string payload = NextTemp();
                EmitInst($"{payload} = extractvalue {optLlvm} {optVal}, 1");
                AssignIntrinsicResult(intr, payload);
                break;
            }

            case MirIntrinsicKind.OptionUnwrapOr:
            {
                string optLlvm;
                string optVal;
                string fallbackVal;
                if (intr.ValueArgs.Count >= 2)
                {
                    optLlvm = _mod.GetValueLlvmType(GetOperandType(intr.ValueArgs[0]));
                    optVal = EmitOperand(intr.ValueArgs[0]);
                    fallbackVal = EmitOperand(intr.ValueArgs[1]);
                }
                else
                {
                    (_, MirType slotTy) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    optLlvm = _mod.GetValueLlvmType(slotTy);
                    optVal = EmitOperandFromPlaceByValue(intr.PlaceArgs[0], slotTy);
                    fallbackVal = EmitOperand(intr.ValueArgs[0]);
                }

                string innerLlvm = _mod.GetValueLlvmType(intr.ResultType);
                string has = NextTemp();
                string someLbl = NextLabel("unwrap_or_some");
                string noneLbl = NextLabel("unwrap_or_none");
                string mergeLbl = NextLabel("unwrap_or_merge");

                EmitInst($"{has} = extractvalue {optLlvm} {optVal}, 0");
                EmitInst($"br i1 {has}, label %{someLbl}, label %{noneLbl}");

                EmitLabel(someLbl);
                SpillAndDropValueIfNeeded(intr.ResultType, fallbackVal);
                string someVal = NextTemp();
                EmitInst($"{someVal} = extractvalue {optLlvm} {optVal}, 1");
                string someDoneLbl = NextLabel("unwrap_or_some_done");
                EmitInst($"br label %{someDoneLbl}");
                EmitLabel(someDoneLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(noneLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(mergeLbl);
                string merged = NextTemp();
                EmitInst($"{merged} = phi {innerLlvm} [ {someVal}, %{someDoneLbl} ], [ {fallbackVal}, %{noneLbl} ]");
                AssignIntrinsicResult(intr, merged);
                break;
            }

            case MirIntrinsicKind.OptionUnwrapMut:
            {
                (string slotPtr, MirType slotType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string optLlvm = _mod.GetValueLlvmType(slotType);
                string tagPtr = NextTemp();
                string tagVal = NextTemp();
                EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {slotPtr}, i32 0, i32 0");
                EmitInst($"{tagVal} = load i1, ptr {tagPtr}");
                EmitPanicIfFalse(tagVal, "called Option.UnwrapMut() on None");
                string valPtr = NextTemp();
                EmitInst($"{valPtr} = getelementptr inbounds {optLlvm}, ptr {slotPtr}, i32 0, i32 1");
                StoreRefResult(intr.RefDestLocalId, intr.Dest, valPtr);
                break;
            }

            case MirIntrinsicKind.OptionIsSome:
            {
                string tagVal;
                if (intr.PlaceArgs.Count > 0)
                {
                    (string pAddr, MirType pType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    if (pType is MirType.OptionRefType orf)
                    {
                        string optSlot = NextTemp();
                        string optLlvm = _mod.GetValueLlvmType(new MirType.OptionType(orf.Inner));
                        string tagPtr = NextTemp();
                        tagVal = NextTemp();
                        EmitInst($"{optSlot} = load ptr, ptr {pAddr}");
                        EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {optSlot}, i32 0, i32 0");
                        EmitInst($"{tagVal} = load i1, ptr {tagPtr}");
                    }
                    else if (pType is MirType.OptionMutType om)
                    {
                        string optSlot = NextTemp();
                        string optLlvm = _mod.GetValueLlvmType(new MirType.OptionType(om.Inner));
                        string tagPtr = NextTemp();
                        tagVal = NextTemp();
                        EmitInst($"{optSlot} = load ptr, ptr {pAddr}");
                        EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {optSlot}, i32 0, i32 0");
                        EmitInst($"{tagVal} = load i1, ptr {tagPtr}");
                    }
                    else
                    {
                        string optLlvm = _mod.GetValueLlvmType(pType);
                        string tagPtr = NextTemp();
                        tagVal = NextTemp();
                        EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {pAddr}, i32 0, i32 0");
                        EmitInst($"{tagVal} = load i1, ptr {tagPtr}");
                    }
                }
                else
                {
                    MirType opType = GetOperandType(intr.ValueArgs[0]);
                    if (opType is MirType.OptionRefType orf2)
                    {
                        string optSlot = EmitOperand(intr.ValueArgs[0]);
                        string optLlvm = _mod.GetValueLlvmType(new MirType.OptionType(orf2.Inner));
                        string tagPtr = NextTemp();
                        tagVal = NextTemp();
                        EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {optSlot}, i32 0, i32 0");
                        EmitInst($"{tagVal} = load i1, ptr {tagPtr}");
                    }
                    else if (opType is MirType.OptionMutType om2)
                    {
                        string optSlot = EmitOperand(intr.ValueArgs[0]);
                        string optLlvm = _mod.GetValueLlvmType(new MirType.OptionType(om2.Inner));
                        string tagPtr = NextTemp();
                        tagVal = NextTemp();
                        EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {optSlot}, i32 0, i32 0");
                        EmitInst($"{tagVal} = load i1, ptr {tagPtr}");
                    }
                    else
                    {
                        string optLlvm = _mod.GetValueLlvmType(opType);
                        string optVal = EmitOperand(intr.ValueArgs[0]);
                        tagVal = NextTemp();
                        EmitInst($"{tagVal} = extractvalue {optLlvm} {optVal}, 0");
                        SpillAndDropValueIfNeeded(opType, optVal);
                    }
                }

                AssignIntrinsicResult(intr, tagVal);
                break;
            }

            case MirIntrinsicKind.OptionMutSet:
            {
                (string curAddr, MirType curType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string slotPtr = NextTemp();
                EmitInst($"{slotPtr} = load ptr, ptr {curAddr}");
                string newOptVal = EmitOperand(intr.ValueArgs[0]);

                MirType optType = curType is MirType.OptionMutType om
                    ? new MirType.OptionType(om.Inner)
                    : GetOperandType(intr.ValueArgs[0]);

                if (_mod.RequiresCleanupGlue(optType))
                {
                    string dropSym = _mod.GetOrScheduleCleanupSymbol(optType);
                    EmitInst($"call void {dropSym}(ptr {slotPtr})");
                }

                string optLlvm = _mod.GetValueLlvmType(optType);
                EmitInst($"store {optLlvm} {newOptVal}, ptr {slotPtr}");
                break;
            }

            case MirIntrinsicKind.OptionMutTake:
            {
                (string curAddr, MirType curType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string slotPtr = NextTemp();
                EmitInst($"{slotPtr} = load ptr, ptr {curAddr}");
                MirType optType = curType is MirType.OptionMutType om
                    ? new MirType.OptionType(om.Inner)
                    : intr.ResultType;
                string optLlvm = _mod.GetValueLlvmType(optType);
                string prev = NextTemp();
                EmitInst($"{prev} = load {optLlvm}, ptr {slotPtr}");
                EmitInst($"store {optLlvm} zeroinitializer, ptr {slotPtr}");
                AssignIntrinsicResult(intr, prev);
                break;
            }

            case MirIntrinsicKind.OptionMutReplace:
            {
                (string curAddr, MirType curType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string slotPtr = NextTemp();
                EmitInst($"{slotPtr} = load ptr, ptr {curAddr}");
                string newOptVal = EmitOperand(intr.ValueArgs[0]);
                MirType optType = curType is MirType.OptionMutType om
                    ? new MirType.OptionType(om.Inner)
                    : intr.ResultType;
                string optLlvm = _mod.GetValueLlvmType(optType);
                string prev = NextTemp();
                EmitInst($"{prev} = load {optLlvm}, ptr {slotPtr}");
                EmitInst($"store {optLlvm} {newOptVal}, ptr {slotPtr}");
                AssignIntrinsicResult(intr, prev);
                break;
            }

            case MirIntrinsicKind.OptionMutAsRef:
            {
                (string curAddr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string slotPtr = NextTemp();
                EmitInst($"{slotPtr} = load ptr, ptr {curAddr}");
                AssignIntrinsicResult(intr, slotPtr);
                break;
            }

            case MirIntrinsicKind.ResultIsOk:
            {
                string okBit;
                if (intr.PlaceArgs.Count > 0)
                {
                    (string resAddr, MirType resType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    string resLlvm = _mod.GetValueLlvmType(resType);
                    string tagPtr = NextTemp();
                    okBit = NextTemp();
                    EmitInst($"{tagPtr} = getelementptr inbounds {resLlvm}, ptr {resAddr}, i32 0, i32 0");
                    EmitInst($"{okBit} = load i1, ptr {tagPtr}");
                }
                else
                {
                    MirType resType = GetOperandType(intr.ValueArgs[0]);
                    string resLlvm = _mod.GetValueLlvmType(resType);
                    string resVal = EmitOperand(intr.ValueArgs[0]);
                    okBit = NextTemp();
                    EmitInst($"{okBit} = extractvalue {resLlvm} {resVal}, 0");
                    SpillAndDropValueIfNeeded(resType, resVal);
                }

                AssignIntrinsicResult(intr, okBit);
                break;
            }

            case MirIntrinsicKind.ResultUnwrap:
            {
                MirType resType;
                string resLlvm;
                string resVal;
                if (intr.ValueArgs.Count > 0)
                {
                    resType = GetOperandType(intr.ValueArgs[0]);
                    resLlvm = _mod.GetValueLlvmType(resType);
                    resVal = EmitOperand(intr.ValueArgs[0]);
                }
                else
                {
                    (_, MirType slotTy) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    resType = slotTy;
                    resLlvm = _mod.GetValueLlvmType(slotTy);
                    resVal = EmitOperandFromPlaceByValue(intr.PlaceArgs[0], slotTy);
                }

                string isOk = NextTemp();
                EmitInst($"{isOk} = extractvalue {resLlvm} {resVal}, 0");
                EmitPanicIfFalse(isOk, "called Result.Unwrap() on Err");
                string okVal = NextTemp();
                EmitInst($"{okVal} = extractvalue {resLlvm} {resVal}, 1");
                AssignIntrinsicResult(intr, okVal);
                break;
            }

            case MirIntrinsicKind.ResultExpect:
            {
                MirType resType = GetOperandType(intr.ValueArgs[0]);
                string resLlvm = _mod.GetValueLlvmType(resType);
                string resVal = EmitOperand(intr.ValueArgs[0]);
                string msgVal = EmitOperand(intr.ValueArgs[1]);
                string isOk = NextTemp();
                EmitInst($"{isOk} = extractvalue {resLlvm} {resVal}, 0");
                EmitPanicDynamicIfFalse(isOk, msgVal);
                string okVal = NextTemp();
                EmitInst($"{okVal} = extractvalue {resLlvm} {resVal}, 1");
                AssignIntrinsicResult(intr, okVal);
                break;
            }

            case MirIntrinsicKind.ResultUnwrapOr:
            {
                MirType.ResultType resType;
                string resLlvm;
                string resVal;
                string fallbackVal;
                if (intr.ValueArgs.Count >= 2)
                {
                    resType = (MirType.ResultType)GetOperandType(intr.ValueArgs[0]);
                    resLlvm = _mod.GetValueLlvmType(resType);
                    resVal = EmitOperand(intr.ValueArgs[0]);
                    fallbackVal = EmitOperand(intr.ValueArgs[1]);
                }
                else
                {
                    (_, MirType slotTy) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    resType = (MirType.ResultType)slotTy;
                    resLlvm = _mod.GetValueLlvmType(resType);
                    resVal = EmitOperandFromPlaceByValue(intr.PlaceArgs[0], slotTy);
                    fallbackVal = EmitOperand(intr.ValueArgs[0]);
                }

                string okLlvm = _mod.GetValueLlvmType(intr.ResultType);
                string isOk = NextTemp();
                string okLbl = NextLabel("res_unwrap_ok");
                string okDoneLbl = NextLabel("res_unwrap_ok_done");
                string errLbl = NextLabel("res_unwrap_err");
                string errDoneLbl = NextLabel("res_unwrap_err_done");
                string mergeLbl = NextLabel("res_unwrap_merge");

                EmitInst($"{isOk} = extractvalue {resLlvm} {resVal}, 0");
                EmitInst($"br i1 {isOk}, label %{okLbl}, label %{errLbl}");

                EmitLabel(okLbl);
                SpillAndDropValueIfNeeded(resType.Ok, fallbackVal);
                string okPayload = NextTemp();
                EmitInst($"{okPayload} = extractvalue {resLlvm} {resVal}, 1");
                EmitInst($"br label %{okDoneLbl}");
                EmitLabel(okDoneLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(errLbl);
                if (_mod.RequiresCleanupGlue(resType.Err))
                {
                    string errPayload = NextTemp();
                    EmitInst($"{errPayload} = extractvalue {resLlvm} {resVal}, 2");
                    SpillAndDropValueIfNeeded(resType.Err, errPayload);
                }

                EmitInst($"br label %{errDoneLbl}");
                EmitLabel(errDoneLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(mergeLbl);
                string merged = NextTemp();
                EmitInst($"{merged} = phi {okLlvm} [ {okPayload}, %{okDoneLbl} ], [ {fallbackVal}, %{errDoneLbl} ]");
                AssignIntrinsicResult(intr, merged);
                break;
            }

            case MirIntrinsicKind.VecNewWithCapacity:
            {
                MirType elemType = ((MirType.VecType)intr.ResultType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string cap = EmitOperand(intr.ValueArgs[0]);

                string isNeg = NextTemp();
                EmitInst($"{isNeg} = icmp slt i32 {cap}, 0");
                EmitPanicIfTrue(isNeg, "Vec capacity cannot be negative");

                string exceedsArrayMax = NextTemp();
                EmitInst($"{exceedsArrayMax} = icmp sgt i32 {cap}, 2147483591");
                EmitPanicIfTrue(exceedsArrayMax, "Vec capacity overflow");

                string isZero = NextTemp();
                string allocLbl = NextLabel("vec_alloc");
                string allocDoneLbl = NextLabel("vec_alloc_done");
                string zeroLbl = NextLabel("vec_zero");
                string mergeLbl = NextLabel("vec_merge");

                EmitInst($"{isZero} = icmp eq i32 {cap}, 0");
                EmitInst($"br i1 {isZero}, label %{zeroLbl}, label %{allocLbl}");

                EmitLabel(zeroLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(allocLbl);
                string elemSize = EmitSizeOf(elemLlvm);
                string cap64 = NextTemp();
                string mulPair = NextTemp();
                string rawBytes = NextTemp();
                string mulOvf = NextTemp();
                string bytes = NextTemp();
                EmitInst($"{cap64} = zext i32 {cap} to i64");
                EmitInst($"{mulPair} = call {{ i64, i1 }} @llvm.umul.with.overflow.i64(i64 {cap64}, i64 {elemSize})");
                EmitInst($"{rawBytes} = extractvalue {{ i64, i1 }} {mulPair}, 0");
                EmitInst($"{mulOvf} = extractvalue {{ i64, i1 }} {mulPair}, 1");
                EmitPanicIfTrue(mulOvf, "Vec capacity overflow");
                EmitRecomputeClampedMinOne(rawBytes, bytes);
                string buf = EmitCheckedMalloc(bytes);
                string v0 = NextTemp();
                string v1 = NextTemp();
                string v2 = NextTemp();
                EmitInst($"{v0} = insertvalue {{ ptr, i32, i32 }} zeroinitializer, ptr {buf}, 0");
                EmitInst($"{v1} = insertvalue {{ ptr, i32, i32 }} {v0}, i32 0, 1");
                EmitInst($"{v2} = insertvalue {{ ptr, i32, i32 }} {v1}, i32 {cap}, 2");
                EmitInst($"br label %{allocDoneLbl}");
                EmitLabel(allocDoneLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(mergeLbl);
                string vecVal = NextTemp();
                EmitInst($"{vecVal} = phi {{ ptr, i32, i32 }} [ zeroinitializer, %{zeroLbl} ], [ {v2}, %{allocDoneLbl} ]");
                AssignIntrinsicResult(intr, vecVal);
                break;
            }

            case MirIntrinsicKind.VecFromLiteral:
            {
                MirType elemType = ((MirType.VecType)intr.ResultType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);

                if (intr.ValueArgs.Count == 1 &&
                    GetOperandType(intr.ValueArgs[0]) is MirType.SpanType spanArgTy &&
                    !TypesEqual(spanArgTy, elemType))
                {
                    string spanVal = EmitOperand(intr.ValueArgs[0]);
                    string srcPtr = NextTemp();
                    string len = NextTemp();
                    EmitInst($"{srcPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                    EmitInst($"{len} = extractvalue {{ ptr, i32 }} {spanVal}, 1");

                    string lenNonPositive = NextTemp();
                    string emptyLitLbl = NextLabel("vec_lit_empty");
                    string allocLitLbl = NextLabel("vec_lit_alloc");
                    string allocLitDoneLbl = NextLabel("vec_lit_alloc_done");
                    string mergeLitLbl = NextLabel("vec_lit_merge");
                    EmitInst($"{lenNonPositive} = icmp sle i32 {len}, 0");
                    EmitInst($"br i1 {lenNonPositive}, label %{emptyLitLbl}, label %{allocLitLbl}");

                    EmitLabel(emptyLitLbl);
                    EmitInst($"br label %{mergeLitLbl}");

                    EmitLabel(allocLitLbl);
                    string elemSize = EmitSizeOf(elemLlvm);
                    string len64 = NextTemp();
                    string mulPair = NextTemp();
                    string rawBytes = NextTemp();
                    string mulOvf = NextTemp();
                    string allocBytes = NextTemp();
                    EmitInst($"{len64} = zext i32 {len} to i64");
                    EmitInst($"{mulPair} = call {{ i64, i1 }} @llvm.umul.with.overflow.i64(i64 {len64}, i64 {elemSize})");
                    EmitInst($"{rawBytes} = extractvalue {{ i64, i1 }} {mulPair}, 0");
                    EmitInst($"{mulOvf} = extractvalue {{ i64, i1 }} {mulPair}, 1");
                    EmitPanicIfTrue(mulOvf, "Vec capacity overflow");
                    EmitRecomputeClampedMinOne(rawBytes, allocBytes);
                    string buf = EmitCheckedMalloc(allocBytes);
                    EmitInst($"call void @llvm.memcpy.p0.p0.i64(ptr {buf}, ptr {srcPtr}, i64 {rawBytes}, i1 false)");
                    string v0 = NextTemp();
                    string v1 = NextTemp();
                    string v2 = NextTemp();
                    EmitInst($"{v0} = insertvalue {{ ptr, i32, i32 }} zeroinitializer, ptr {buf}, 0");
                    EmitInst($"{v1} = insertvalue {{ ptr, i32, i32 }} {v0}, i32 {len}, 1");
                    EmitInst($"{v2} = insertvalue {{ ptr, i32, i32 }} {v1}, i32 {len}, 2");
                    EmitInst($"br label %{allocLitDoneLbl}");
                    EmitLabel(allocLitDoneLbl);
                    EmitInst($"br label %{mergeLitLbl}");

                    EmitLabel(mergeLitLbl);
                    string litRes = NextTemp();
                    EmitInst($"{litRes} = phi {{ ptr, i32, i32 }} [ zeroinitializer, %{emptyLitLbl} ], [ {v2}, %{allocLitDoneLbl} ]");
                    AssignIntrinsicResult(intr, litRes);
                    break;
                }

                var evaluatedElems = new List<string>(intr.ValueArgs.Count);
                foreach (MirOperand elemOp in intr.ValueArgs)
                {
                    evaluatedElems.Add(EmitOperand(elemOp));
                }

                int count = evaluatedElems.Count;
                if (count == 0)
                {
                    AssignIntrinsicResult(intr, "zeroinitializer");
                    break;
                }

                string elemSz = EmitSizeOf(elemLlvm);
                string mulLitPair = NextTemp();
                string rawSz = NextTemp();
                string mulLitOvf = NextTemp();
                string clampedSz = NextTemp();
                EmitInst($"{mulLitPair} = call {{ i64, i1 }} @llvm.umul.with.overflow.i64(i64 {count}, i64 {elemSz})");
                EmitInst($"{rawSz} = extractvalue {{ i64, i1 }} {mulLitPair}, 0");
                EmitInst($"{mulLitOvf} = extractvalue {{ i64, i1 }} {mulLitPair}, 1");
                EmitPanicIfTrue(mulLitOvf, "Vec capacity overflow");
                EmitRecomputeClampedMinOne(rawSz, clampedSz);
                string dataBuf = EmitCheckedMalloc(clampedSz);

                for (int i = 0; i < count; i++)
                {
                    string ep = NextTemp();
                    EmitInst($"{ep} = getelementptr inbounds {elemLlvm}, ptr {dataBuf}, i64 {i}");
                    EmitInst($"store {elemLlvm} {evaluatedElems[i]}, ptr {ep}");
                }

                string r0 = NextTemp();
                string r1 = NextTemp();
                string r2 = NextTemp();
                EmitInst($"{r0} = insertvalue {{ ptr, i32, i32 }} zeroinitializer, ptr {dataBuf}, 0");
                EmitInst($"{r1} = insertvalue {{ ptr, i32, i32 }} {r0}, i32 {count}, 1");
                EmitInst($"{r2} = insertvalue {{ ptr, i32, i32 }} {r1}, i32 {count}, 2");
                AssignIntrinsicResult(intr, r2);
                break;
            }

            case MirIntrinsicKind.VecCount:
            {
                string cnt = ReadAggregateI32Field(intr, "{ ptr, i32, i32 }", fieldIndex: 1);
                AssignIntrinsicResult(intr, cnt);
                break;
            }

            case MirIntrinsicKind.VecCapacity:
            {
                string cap = ReadAggregateI32Field(intr, "{ ptr, i32, i32 }", fieldIndex: 2);
                AssignIntrinsicResult(intr, cap);
                break;
            }

            case MirIntrinsicKind.VecPush:
            {
                string itemVal = EmitOperand(intr.ValueArgs[0]);
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);

                string dpSlot = NextTemp();
                string cntSlot = NextTemp();
                string capSlot = NextTemp();
                string dataPtr = NextTemp();
                string cnt = NextTemp();
                string cap = NextTemp();

                EmitInst($"{dpSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 0");
                EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                EmitInst($"{capSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 2");
                EmitInst($"{dataPtr} = load ptr, ptr {dpSlot}");
                EmitInst($"{cnt} = load i32, ptr {cntSlot}");
                EmitInst($"{cap} = load i32, ptr {capSlot}");

                string isFull = NextTemp();
                string growLbl = NextLabel("vec_grow");
                string storeLbl = NextLabel("vec_store");
                string curEntryLbl = NextLabel("vec_check");
                EmitInst($"br label %{curEntryLbl}");
                EmitLabel(curEntryLbl);
                EmitInst($"{isFull} = icmp eq i32 {cnt}, {cap}");
                EmitInst($"br i1 {isFull}, label %{growLbl}, label %{storeLbl}");

                EmitLabel(growLbl);
                string atMax = NextTemp();
                EmitInst($"{atMax} = icmp sge i32 {cap}, 2147483591");
                EmitPanicIfTrue(atMax, "Vec capacity overflow");

                string capZero = NextTemp();
                string overHalf = NextTemp();
                string doubled = NextTemp();
                string nonZeroGrown = NextTemp();
                string nextCap = NextTemp();
                EmitInst($"{capZero} = icmp eq i32 {cap}, 0");
                EmitInst($"{overHalf} = icmp sgt i32 {cap}, 1073741795");
                EmitInst($"{doubled} = shl i32 {cap}, 1");
                EmitInst($"{nonZeroGrown} = select i1 {overHalf}, i32 2147483591, i32 {doubled}");
                EmitInst($"{nextCap} = select i1 {capZero}, i32 4, i32 {nonZeroGrown}");

                string elemSz = EmitSizeOf(elemLlvm);
                string nextCap64 = NextTemp();
                string mulPair = NextTemp();
                string rawBytes = NextTemp();
                string mulOvf = NextTemp();
                string allocBytes = NextTemp();
                EmitInst($"{nextCap64} = zext i32 {nextCap} to i64");
                EmitInst($"{mulPair} = call {{ i64, i1 }} @llvm.umul.with.overflow.i64(i64 {nextCap64}, i64 {elemSz})");
                EmitInst($"{rawBytes} = extractvalue {{ i64, i1 }} {mulPair}, 0");
                EmitInst($"{mulOvf} = extractvalue {{ i64, i1 }} {mulPair}, 1");
                EmitPanicIfTrue(mulOvf, "Vec capacity overflow");
                EmitRecomputeClampedMinOne(rawBytes, allocBytes);

                string grownBuf = EmitCheckedRealloc(dataPtr, allocBytes);
                string grownDoneLbl = NextLabel("vec_grown");
                EmitInst($"br label %{grownDoneLbl}");
                EmitLabel(grownDoneLbl);
                EmitInst($"store ptr {grownBuf}, ptr {dpSlot}");
                EmitInst($"store i32 {nextCap}, ptr {capSlot}");
                EmitInst($"br label %{storeLbl}");

                EmitLabel(storeLbl);
                string activeBuf = NextTemp();
                EmitInst($"{activeBuf} = phi ptr [ {dataPtr}, %{curEntryLbl} ], [ {grownBuf}, %{grownDoneLbl} ]");
                string idx64 = NextTemp();
                string elemPtr = NextTemp();
                string nextCnt = NextTemp();
                EmitInst($"{idx64} = zext i32 {cnt} to i64");
                EmitInst($"{elemPtr} = getelementptr inbounds {elemLlvm}, ptr {activeBuf}, i64 {idx64}");
                EmitInst($"store {elemLlvm} {itemVal}, ptr {elemPtr}");
                EmitInst($"{nextCnt} = add i32 {cnt}, 1");
                EmitInst($"store i32 {nextCnt}, ptr {cntSlot}");
                break;
            }

            case MirIntrinsicKind.VecPop:
            {
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string optLlvm = _mod.GetValueLlvmType(intr.ResultType);

                string dpSlot = NextTemp();
                string cntSlot = NextTemp();
                string dataPtr = NextTemp();
                string cnt = NextTemp();
                EmitInst($"{dpSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 0");
                EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                EmitInst($"{dataPtr} = load ptr, ptr {dpSlot}");
                EmitInst($"{cnt} = load i32, ptr {cntSlot}");

                string isEmpty = NextTemp();
                string emptyLbl = NextLabel("vec_pop_empty");
                string popLbl = NextLabel("vec_pop_some");
                string mergeLbl = NextLabel("vec_pop_merge");

                EmitInst($"{isEmpty} = icmp eq i32 {cnt}, 0");
                EmitInst($"br i1 {isEmpty}, label %{emptyLbl}, label %{popLbl}");

                EmitLabel(emptyLbl);
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(popLbl);
                string newCnt = NextTemp();
                string idx64 = NextTemp();
                string elemPtr = NextTemp();
                string popped = NextTemp();
                EmitInst($"{newCnt} = sub i32 {cnt}, 1");
                EmitInst($"store i32 {newCnt}, ptr {cntSlot}");
                EmitInst($"{idx64} = zext i32 {newCnt} to i64");
                EmitInst($"{elemPtr} = getelementptr inbounds {elemLlvm}, ptr {dataPtr}, i64 {idx64}");
                EmitInst($"{popped} = load {elemLlvm}, ptr {elemPtr}");
                if (_mod.RequiresCleanupGlue(elemType))
                {
                    EmitInst($"store {elemLlvm} {ModuleEmitter.GetZeroLiteral(elemLlvm)}, ptr {elemPtr}");
                }

                string s0 = NextTemp();
                string s1 = NextTemp();
                EmitInst($"{s0} = insertvalue {optLlvm} zeroinitializer, i1 true, 0");
                EmitInst($"{s1} = insertvalue {optLlvm} {s0}, {elemLlvm} {popped}, 1");
                EmitInst($"br label %{mergeLbl}");

                EmitLabel(mergeLbl);
                string resOpt = NextTemp();
                EmitInst($"{resOpt} = phi {optLlvm} [ zeroinitializer, %{emptyLbl} ], [ {s1}, %{popLbl} ]");
                AssignIntrinsicResult(intr, resOpt);
                break;
            }

            case MirIntrinsicKind.VecClear:
            {
                (string vecPtr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string cntSlot = NextTemp();
                EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                EmitInst($"store i32 0, ptr {cntSlot}");
                break;
            }

            case MirIntrinsicKind.VecTruncate:
            {
                (string vecPtr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
                string newLen = EnsureI32(EmitOperand(intr.ValueArgs[0]), GetOperandType(intr.ValueArgs[0]));
                string cntSlot = NextTemp();
                string cnt = NextTemp();
                string bad = NextTemp();
                EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                EmitInst($"{cnt} = load i32, ptr {cntSlot}");
                EmitInst($"{bad} = icmp ugt i32 {newLen}, {cnt}");
                EmitPanicIfTrue(bad, "truncate out of bounds");
                EmitInst($"store i32 {newLen}, ptr {cntSlot}");
                break;
            }

            case MirIntrinsicKind.VecReserve:
            {
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string addVal = EnsureI32(EmitOperand(intr.ValueArgs[0]), GetOperandType(intr.ValueArgs[0]));
                string elemSz = EmitSizeOf(elemLlvm);
                EmitInst($"call void @__vib_vec_reserve(ptr {vecPtr}, i32 {addVal}, i64 {elemSz})");
                break;
            }

            case MirIntrinsicKind.VecExtend:
            {
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string spanVal = EmitOperand(intr.ValueArgs[0]);
                string sPtr = NextTemp();
                string sLen = NextTemp();
                string elemSz = EmitSizeOf(elemLlvm);
                EmitInst($"{sPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                EmitInst($"{sLen} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                EmitInst($"call void @__vib_vec_extend(ptr {vecPtr}, ptr {sPtr}, i32 {sLen}, i64 {elemSz})");
                break;
            }

            case MirIntrinsicKind.VecResize:
            {
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string newLen = EnsureI32(EmitOperand(intr.ValueArgs[0]), GetOperandType(intr.ValueArgs[0]));
                string fillVal = EmitOperand(intr.ValueArgs[1]);
                string fillSlot = AllocTempSlot(elemLlvm);
                EmitInst($"store {elemLlvm} {fillVal}, ptr {fillSlot}");
                string elemSz = EmitSizeOf(elemLlvm);
                EmitInst($"call void @__vib_vec_resize(ptr {vecPtr}, i32 {newLen}, ptr {fillSlot}, i64 {elemSz})");
                break;
            }

            case MirIntrinsicKind.VecIndexRef:
            {
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string idxVal = EnsureI32(EmitOperand(intr.ValueArgs[0]), GetOperandType(intr.ValueArgs[0]));

                string dpSlot = NextTemp();
                string cntSlot = NextTemp();
                string dataPtr = NextTemp();
                string cnt = NextTemp();
                EmitInst($"{dpSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 0");
                EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                EmitInst($"{dataPtr} = load ptr, ptr {dpSlot}");
                EmitInst($"{cnt} = load i32, ptr {cntSlot}");

                EmitBoundsCheck(idxVal, cnt, "index out of bounds");
                string idx64 = NextTemp();
                string elemPtr = NextTemp();
                EmitInst($"{idx64} = zext i32 {idxVal} to i64");
                EmitInst($"{elemPtr} = getelementptr inbounds {elemLlvm}, ptr {dataPtr}, i64 {idx64}");
                StoreRefResult(intr.RefDestLocalId, intr.Dest, elemPtr);
                break;
            }

            case MirIntrinsicKind.VecSliceMut:
            {
                (string vecPtr, MirType vecType) = EmitPlaceAddress(intr.PlaceArgs[0]);
                MirType elemType = ((MirType.VecType)vecType).Element;
                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string start = EnsureI32(EmitOperand(intr.ValueArgs[0]), GetOperandType(intr.ValueArgs[0]));
                string length = EnsureI32(EmitOperand(intr.ValueArgs[1]), GetOperandType(intr.ValueArgs[1]));

                string dpSlot = NextTemp();
                string cntSlot = NextTemp();
                string dataPtr = NextTemp();
                string cnt = NextTemp();
                EmitInst($"{dpSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 0");
                EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                EmitInst($"{dataPtr} = load ptr, ptr {dpSlot}");
                EmitInst($"{cnt} = load i32, ptr {cntSlot}");

                string spanVal = EmitSliceFromPtrAndLen(dataPtr, cnt, start, length, elemLlvm);
                AssignIntrinsicResult(intr, spanVal);
                break;
            }

            case MirIntrinsicKind.VecAsSpan:
            case MirIntrinsicKind.VecAsReadOnlySpan:
            {
                string dataPtr;
                string cnt;
                if (intr.PlaceArgs.Count > 0)
                {
                    (string vecPtr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    string dpSlot = NextTemp();
                    string cntSlot = NextTemp();
                    dataPtr = NextTemp();
                    cnt = NextTemp();
                    EmitInst($"{dpSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 0");
                    EmitInst($"{cntSlot} = getelementptr inbounds {{ ptr, i32, i32 }}, ptr {vecPtr}, i32 0, i32 1");
                    EmitInst($"{dataPtr} = load ptr, ptr {dpSlot}");
                    EmitInst($"{cnt} = load i32, ptr {cntSlot}");
                }
                else
                {
                    string vecVal = EmitOperand(intr.ValueArgs[0]);
                    dataPtr = NextTemp();
                    cnt = NextTemp();
                    EmitInst($"{dataPtr} = extractvalue {{ ptr, i32, i32 }} {vecVal}, 0");
                    EmitInst($"{cnt} = extractvalue {{ ptr, i32, i32 }} {vecVal}, 1");
                }

                string s0 = NextTemp();
                string s1 = NextTemp();
                EmitInst($"{s0} = insertvalue {{ ptr, i32 }} poison, ptr {dataPtr}, 0");
                EmitInst($"{s1} = insertvalue {{ ptr, i32 }} {s0}, i32 {cnt}, 1");
                AssignIntrinsicResult(intr, s1);
                break;
            }

            case MirIntrinsicKind.SpanLength:
            {
                string len = ReadAggregateI32Field(intr, "{ ptr, i32 }", fieldIndex: 1);
                AssignIntrinsicResult(intr, len);
                break;
            }

            case MirIntrinsicKind.SpanIndexRef:
            {
                string dataPtr;
                string len;
                MirType elemType;
                string idxVal;
                if (intr.PlaceArgs.Count > 0)
                {
                    (string spanPtr, MirType spanTy) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    elemType = ((MirType.SpanType)spanTy).Element;
                    string spanVal = NextTemp();
                    dataPtr = NextTemp();
                    len = NextTemp();
                    EmitInst($"{spanVal} = load {{ ptr, i32 }}, ptr {spanPtr}");
                    EmitInst($"{dataPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                    EmitInst($"{len} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                    idxVal = EnsureI32(EmitOperand(intr.ValueArgs[0]), GetOperandType(intr.ValueArgs[0]));
                }
                else
                {
                    MirType spanTy = GetOperandType(intr.ValueArgs[0]);
                    elemType = ((MirType.SpanType)spanTy).Element;
                    string spanVal = EmitOperand(intr.ValueArgs[0]);
                    dataPtr = NextTemp();
                    len = NextTemp();
                    EmitInst($"{dataPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                    EmitInst($"{len} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                    idxVal = EnsureI32(EmitOperand(intr.ValueArgs[1]), GetOperandType(intr.ValueArgs[1]));
                }

                string elemLlvm = _mod.GetValueLlvmType(elemType);
                EmitBoundsCheck(idxVal, len, "index out of bounds");
                string idx64 = NextTemp();
                string elemPtr = NextTemp();
                EmitInst($"{idx64} = zext i32 {idxVal} to i64");
                EmitInst($"{elemPtr} = getelementptr inbounds {elemLlvm}, ptr {dataPtr}, i64 {idx64}");
                StoreRefResult(intr.RefDestLocalId, intr.Dest, elemPtr);
                break;
            }

            case MirIntrinsicKind.SpanSlice:
            {
                string dataPtr;
                string len;
                MirType elemType;
                int argOffset;
                if (intr.PlaceArgs.Count > 0)
                {
                    (string spanPtr, MirType spanTy) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    elemType = ((MirType.SpanType)spanTy).Element;
                    string spanVal = NextTemp();
                    dataPtr = NextTemp();
                    len = NextTemp();
                    EmitInst($"{spanVal} = load {{ ptr, i32 }}, ptr {spanPtr}");
                    EmitInst($"{dataPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                    EmitInst($"{len} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                    argOffset = 0;
                }
                else
                {
                    MirType spanTy = GetOperandType(intr.ValueArgs[0]);
                    elemType = ((MirType.SpanType)spanTy).Element;
                    string spanVal = EmitOperand(intr.ValueArgs[0]);
                    dataPtr = NextTemp();
                    len = NextTemp();
                    EmitInst($"{dataPtr} = extractvalue {{ ptr, i32 }} {spanVal}, 0");
                    EmitInst($"{len} = extractvalue {{ ptr, i32 }} {spanVal}, 1");
                    argOffset = 1;
                }

                string elemLlvm = _mod.GetValueLlvmType(elemType);
                string start = EnsureI32(EmitOperand(intr.ValueArgs[argOffset]), GetOperandType(intr.ValueArgs[argOffset]));
                string sliceVal;
                if (intr.ValueArgs.Count - argOffset >= 2)
                {
                    string sliceLen = EnsureI32(EmitOperand(intr.ValueArgs[argOffset + 1]), GetOperandType(intr.ValueArgs[argOffset + 1]));
                    sliceVal = EmitSliceFromPtrAndLen(dataPtr, len, start, sliceLen, elemLlvm);
                }
                else
                {
                    string badStart = NextTemp();
                    EmitInst($"{badStart} = icmp ugt i32 {start}, {len}");
                    EmitPanicIfTrue(badStart, "slice out of bounds");
                    string remLen = NextTemp();
                    string start64 = NextTemp();
                    string subPtr = NextTemp();
                    string s0 = NextTemp();
                    string s1 = NextTemp();
                    EmitInst($"{remLen} = sub i32 {len}, {start}");
                    EmitInst($"{start64} = zext i32 {start} to i64");
                    EmitInst($"{subPtr} = getelementptr inbounds {elemLlvm}, ptr {dataPtr}, i64 {start64}");
                    EmitInst($"{s0} = insertvalue {{ ptr, i32 }} poison, ptr {subPtr}, 0");
                    EmitInst($"{s1} = insertvalue {{ ptr, i32 }} {s0}, i32 {remLen}, 1");
                    sliceVal = s1;
                }

                AssignIntrinsicResult(intr, sliceVal);
                break;
            }

            case MirIntrinsicKind.SpanToReadOnly:
            {
                string spanVal;
                if (intr.PlaceArgs.Count > 0)
                {
                    (string spanPtr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
                    spanVal = NextTemp();
                    EmitInst($"{spanVal} = load {{ ptr, i32 }}, ptr {spanPtr}");
                }
                else
                {
                    spanVal = EmitOperand(intr.ValueArgs[0]);
                }

                AssignIntrinsicResult(intr, spanVal);
                break;
            }

            default:
                throw new InvalidOperationException($"Unsupported MIR intrinsic: {intr.Kind}");
        }
    }

    private void AssignIntrinsicResult(MirStatement.Intrinsic intr, string ssaVal)
    {
        if (intr.Dest is not null)
        {
            StoreIntoPlaceWithOverwriteDrop(intr.Dest, ssaVal);
        }
        else if (intr.RefDestLocalId is not null)
        {
            StoreRefResult(intr.RefDestLocalId, null, ssaVal);
        }
        else
        {
            SpillAndDropValueIfNeeded(intr.ResultType, ssaVal);
        }
    }

    private string ReadAggregateI32Field(MirStatement.Intrinsic intr, string aggLlvm, int fieldIndex)
    {
        if (intr.PlaceArgs.Count > 0)
        {
            (string addr, _) = EmitPlaceAddress(intr.PlaceArgs[0]);
            string fldPtr = NextTemp();
            string res = NextTemp();
            EmitInst($"{fldPtr} = getelementptr inbounds {aggLlvm}, ptr {addr}, i32 0, i32 {fieldIndex}");
            EmitInst($"{res} = load i32, ptr {fldPtr}");
            return res;
        }
        else
        {
            string val = EmitOperand(intr.ValueArgs[0]);
            string res = NextTemp();
            EmitInst($"{res} = extractvalue {aggLlvm} {val}, {fieldIndex}");
            return res;
        }
    }

    private string EmitSliceFromPtrAndLen(string dataPtr, string totalLen, string start, string sliceLen, string elemLlvm)
    {
        string bad1 = NextTemp();
        string rem = NextTemp();
        string bad2 = NextTemp();
        string oob = NextTemp();
        EmitInst($"{bad1} = icmp ugt i32 {start}, {totalLen}");
        EmitInst($"{rem} = sub i32 {totalLen}, {start}");
        EmitInst($"{bad2} = icmp ugt i32 {sliceLen}, {rem}");
        EmitInst($"{oob} = or i1 {bad1}, {bad2}");
        EmitPanicIfTrue(oob, "slice out of bounds");

        string start64 = NextTemp();
        string subPtr = NextTemp();
        string s0 = NextTemp();
        string s1 = NextTemp();
        EmitInst($"{start64} = zext i32 {start} to i64");
        EmitInst($"{subPtr} = getelementptr inbounds {elemLlvm}, ptr {dataPtr}, i64 {start64}");
        EmitInst($"{s0} = insertvalue {{ ptr, i32 }} poison, ptr {subPtr}, 0");
        EmitInst($"{s1} = insertvalue {{ ptr, i32 }} {s0}, i32 {sliceLen}, 1");
        return s1;
    }

    private string EmitRvalue(MirRvalue rvalue)
    {
        switch (rvalue)
        {
            case MirRvalue.Use use:
                return EmitOperand(use.Operand);

            case MirRvalue.Binary bin:
                return EmitBinary(bin);

            case MirRvalue.Unary un:
                return EmitUnary(un);

            case MirRvalue.Cast cast:
                return EmitCast(cast);

            case MirRvalue.AllocClass alloc:
            {
                var fieldVals = new List<(string Val, string LlvmTy)>(alloc.FieldValues.Count);
                MirClassDef? cdef = _mod.Module.Classes.TryGetValue(alloc.ClassName, out MirClassDef? d) ? d : null;
                for (int i = 0; i < alloc.FieldValues.Count; i++)
                {
                    MirOperand fOp = alloc.FieldValues[i];
                    MirType fTy = cdef is not null && i < cdef.Fields.Count ? cdef.Fields[i].Type : GetOperandType(fOp);
                    fieldVals.Add((EmitOperand(fOp), _mod.GetValueLlvmType(fTy)));
                }

                string clsStorageTy = _mod.GetClassStorageTypeName(alloc.ClassName);
                string rawBytes = EmitSizeOf(clsStorageTy);
                string clampedBytes = NextTemp();
                EmitRecomputeClampedMinOne(rawBytes, clampedBytes);
                string objPtr = EmitCheckedMalloc(clampedBytes);
                EmitInst($"call void @llvm.memset.p0.i64(ptr {objPtr}, i8 0, i64 {clampedBytes}, i1 false)");

                for (int i = 0; i < fieldVals.Count; i++)
                {
                    string fp = NextTemp();
                    EmitInst($"{fp} = getelementptr inbounds {clsStorageTy}, ptr {objPtr}, i32 0, i32 {i}");
                    EmitInst($"store {fieldVals[i].LlvmTy} {fieldVals[i].Val}, ptr {fp}");
                }

                return objPtr;
            }

            case MirRvalue.ConstructStruct cs:
            {
                var fieldVals = new List<(string Val, string LlvmTy)>(cs.FieldValues.Count);
                MirStructDef? sdef = _mod.Module.Structs.TryGetValue(cs.StructName, out MirStructDef? d) ? d : null;
                for (int i = 0; i < cs.FieldValues.Count; i++)
                {
                    MirOperand fOp = cs.FieldValues[i];
                    MirType fTy = sdef is not null && i < sdef.Fields.Count ? sdef.Fields[i].Type : GetOperandType(fOp);
                    fieldVals.Add((EmitOperand(fOp), _mod.GetValueLlvmType(fTy)));
                }

                string stLlvm = _mod.GetStructTypeName(cs.StructName);
                string cur = "zeroinitializer";
                for (int i = 0; i < fieldVals.Count; i++)
                {
                    string next = NextTemp();
                    EmitInst($"{next} = insertvalue {stLlvm} {cur}, {fieldVals[i].LlvmTy} {fieldVals[i].Val}, {i}");
                    cur = next;
                }

                if (fieldVals.Count == 0)
                {
                    string zeroed = NextTemp();
                    EmitInst($"{zeroed} = insertvalue {stLlvm} zeroinitializer, i8 0, 0");
                    return zeroed;
                }

                return cur;
            }

            case MirRvalue.MakeSomeWrapper msw:
                return EmitOperand(msw.Value);

            case MirRvalue.MakeOkWrapper mow:
                return EmitOperand(mow.Value);

            case MirRvalue.MakeErrWrapper mew:
                return EmitOperand(mew.Error);

            case MirRvalue.UnwrapSomeWrapper usw:
                return EmitOperand(usw.Wrapper);

            case MirRvalue.UnwrapOkWrapper uow:
                return EmitOperand(uow.Wrapper);

            case MirRvalue.UnwrapErrWrapper uew:
                return EmitOperand(uew.Wrapper);

            case MirRvalue.LiftOptionSome los:
            {
                string innerLlvm = _mod.GetValueLlvmType(los.InnerType);
                string optLlvm = $"{{ i1, {innerLlvm} }}";
                string val = EmitOperand(los.Value);
                string o0 = NextTemp();
                string o1 = NextTemp();
                EmitInst($"{o0} = insertvalue {optLlvm} zeroinitializer, i1 true, 0");
                EmitInst($"{o1} = insertvalue {optLlvm} {o0}, {innerLlvm} {val}, 1");
                return o1;
            }

            case MirRvalue.LiftOptionNone:
                return "zeroinitializer";

            case MirRvalue.LiftResultOk lro:
            {
                string okLlvm = _mod.GetValueLlvmType(lro.OkType);
                string errLlvm = _mod.GetValueLlvmType(lro.ErrType);
                string resLlvm = $"{{ i1, {okLlvm}, {errLlvm} }}";
                string val = EmitOperand(lro.Value);
                string r0 = NextTemp();
                string r1 = NextTemp();
                EmitInst($"{r0} = insertvalue {resLlvm} zeroinitializer, i1 true, 0");
                EmitInst($"{r1} = insertvalue {resLlvm} {r0}, {okLlvm} {val}, 1");
                return r1;
            }

            case MirRvalue.LiftResultErr lre:
            {
                string okLlvm = _mod.GetValueLlvmType(lre.OkType);
                string errLlvm = _mod.GetValueLlvmType(lre.ErrType);
                string resLlvm = $"{{ i1, {okLlvm}, {errLlvm} }}";
                string err = EmitOperand(lre.Error);
                string r0 = NextTemp();
                string r1 = NextTemp();
                EmitInst($"{r0} = insertvalue {resLlvm} zeroinitializer, i1 false, 0");
                EmitInst($"{r1} = insertvalue {resLlvm} {r0}, {errLlvm} {err}, 2");
                return r1;
            }

            case MirRvalue.StackSpan ss:
            {
                string elemLlvm = _mod.GetValueLlvmType(ss.ElementType);
                int count = ss.Elements.Count > 0 ? ss.Elements.Count : ss.ZeroInitLength;
                if (count <= 0)
                {
                    return "zeroinitializer";
                }

                string arrAlloca = _stackSpanAllocas[ss];
                if (ss.Elements.Count > 0)
                {
                    var vals = new List<string>(ss.Elements.Count);
                    foreach (MirOperand op in ss.Elements)
                    {
                        vals.Add(EmitOperand(op));
                    }

                    for (int i = 0; i < vals.Count; i++)
                    {
                        string ep = NextTemp();
                        EmitInst($"{ep} = getelementptr inbounds [{count} x {elemLlvm}], ptr {arrAlloca}, i64 0, i64 {i}");
                        EmitInst($"store {elemLlvm} {vals[i]}, ptr {ep}");
                    }
                }
                else
                {
                    string elemSz = EmitSizeOf(elemLlvm);
                    string totalSz = NextTemp();
                    EmitInst($"{totalSz} = mul i64 {count}, {elemSz}");
                    EmitInst($"call void @llvm.memset.p0.i64(ptr {arrAlloca}, i8 0, i64 {totalSz}, i1 false)");
                }

                string s0 = NextTemp();
                string s1 = NextTemp();
                EmitInst($"{s0} = insertvalue {{ ptr, i32 }} poison, ptr {arrAlloca}, 0");
                EmitInst($"{s1} = insertvalue {{ ptr, i32 }} {s0}, i32 {count}, 1");
                return s1;
            }

            default:
                throw new InvalidOperationException($"Unsupported MIR rvalue: {rvalue}");
        }
    }

    private string EmitBinary(MirRvalue.Binary bin)
    {
        MirType lhsType = GetOperandType(bin.Left);
        MirType rhsType = GetOperandType(bin.Right);
        string lhs = EmitOperand(bin.Left);
        string rhs = EmitOperand(bin.Right);
        string lhsLlvm = _mod.GetValueLlvmType(lhsType);

        if (lhsType is MirType.StringLiteralType)
        {
            return EmitStringEquality(lhs, rhs, isNotEqual: bin.Op == MirBinaryOp.Ne);
        }

        if (lhsType is MirType.NoneUnitType)
        {
            return bin.Op == MirBinaryOp.Ne ? "false" : "true";
        }

        bool isFloat = lhsLlvm is "float" or "double";
        if (isFloat)
        {
            string res = NextTemp();
            string opText = bin.Op switch
            {
                MirBinaryOp.Add => $"fadd {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Sub => $"fsub {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Mul => $"fmul {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Div => $"fdiv {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Rem => $"call {lhsLlvm} @__vib_rem_{(lhsLlvm == "float" ? "f32" : "f64")}({lhsLlvm} {lhs}, {lhsLlvm} {rhs})",
                MirBinaryOp.Eq => $"fcmp oeq {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Ne => $"fcmp une {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Lt => $"fcmp olt {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Le => $"fcmp ole {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Gt => $"fcmp ogt {lhsLlvm} {lhs}, {rhs}",
                MirBinaryOp.Ge => $"fcmp oge {lhsLlvm} {lhs}, {rhs}",
                _ => throw new InvalidOperationException($"Unsupported float binary op {bin.Op}"),
            };
            EmitInst($"{res} = {opText}");
            return res;
        }

        if (lhsLlvm == "i1")
        {
            string res = NextTemp();
            string opText = bin.Op switch
            {
                MirBinaryOp.BitAnd => $"and i1 {lhs}, {rhs}",
                MirBinaryOp.BitOr => $"or i1 {lhs}, {rhs}",
                MirBinaryOp.BitXor => $"xor i1 {lhs}, {rhs}",
                MirBinaryOp.Eq => $"icmp eq i1 {lhs}, {rhs}",
                MirBinaryOp.Ne => $"icmp ne i1 {lhs}, {rhs}",
                MirBinaryOp.Lt => $"icmp ult i1 {lhs}, {rhs}",
                MirBinaryOp.Le => $"icmp ule i1 {lhs}, {rhs}",
                MirBinaryOp.Gt => $"icmp ugt i1 {lhs}, {rhs}",
                MirBinaryOp.Ge => $"icmp uge i1 {lhs}, {rhs}",
                _ => throw new InvalidOperationException($"Unsupported bool binary op {bin.Op}"),
            };
            EmitInst($"{res} = {opText}");
            return res;
        }

        switch (bin.Op)
        {
            case MirBinaryOp.Add:
            case MirBinaryOp.Sub:
            case MirBinaryOp.Mul:
            {
                if (bin.IsChecked)
                {
                    string intrName = bin.Op switch
                    {
                        MirBinaryOp.Add => $"@llvm.sadd.with.overflow.{lhsLlvm}",
                        MirBinaryOp.Sub => $"@llvm.ssub.with.overflow.{lhsLlvm}",
                        _ => $"@llvm.smul.with.overflow.{lhsLlvm}",
                    };
                    string pair = NextTemp();
                    string val = NextTemp();
                    string ovf = NextTemp();
                    EmitInst($"{pair} = call {{ {lhsLlvm}, i1 }} {intrName}({lhsLlvm} {lhs}, {lhsLlvm} {rhs})");
                    EmitInst($"{val} = extractvalue {{ {lhsLlvm}, i1 }} {pair}, 0");
                    EmitInst($"{ovf} = extractvalue {{ {lhsLlvm}, i1 }} {pair}, 1");
                    EmitPanicIfTrue(ovf, "arithmetic overflow");
                    return val;
                }
                else
                {
                    string opName = bin.Op switch
                    {
                        MirBinaryOp.Add => "add",
                        MirBinaryOp.Sub => "sub",
                        _ => "mul",
                    };
                    string res = NextTemp();
                    EmitInst($"{res} = {opName} {lhsLlvm} {lhs}, {rhs}");
                    return res;
                }
            }

            case MirBinaryOp.Div:
            case MirBinaryOp.Rem:
            {
                string isZero = NextTemp();
                EmitInst($"{isZero} = icmp eq {lhsLlvm} {rhs}, 0");
                EmitPanicIfTrue(isZero, "attempt to divide by zero");

                bool isUnsigned = MirType.IsUnsignedInteger(lhsType);
                if (isUnsigned)
                {
                    string uOpName = bin.Op == MirBinaryOp.Div ? "udiv" : "urem";
                    string uRes = NextTemp();
                    EmitInst($"{uRes} = {uOpName} {lhsLlvm} {lhs}, {rhs}");
                    return uRes;
                }

                string minVal = lhsLlvm switch
                {
                    "i8" => "-128",
                    "i16" => "-32768",
                    "i64" => "-9223372036854775808",
                    _ => "-2147483648",
                };
                string isMin = NextTemp();
                string isNegOne = NextTemp();
                string isOvf = NextTemp();
                EmitInst($"{isMin} = icmp eq {lhsLlvm} {lhs}, {minVal}");
                EmitInst($"{isNegOne} = icmp eq {lhsLlvm} {rhs}, -1");
                EmitInst($"{isOvf} = and i1 {isMin}, {isNegOne}");
                EmitPanicIfTrue(isOvf, "arithmetic overflow");

                string opName = bin.Op == MirBinaryOp.Div ? "sdiv" : "srem";
                string res = NextTemp();
                EmitInst($"{res} = {opName} {lhsLlvm} {lhs}, {rhs}");
                return res;
            }

            case MirBinaryOp.BitAnd:
            case MirBinaryOp.BitOr:
            case MirBinaryOp.BitXor:
            {
                string opName = bin.Op switch
                {
                    MirBinaryOp.BitAnd => "and",
                    MirBinaryOp.BitOr => "or",
                    _ => "xor",
                };
                string res = NextTemp();
                EmitInst($"{res} = {opName} {lhsLlvm} {lhs}, {rhs}");
                return res;
            }

            case MirBinaryOp.Shl:
            case MirBinaryOp.Shr:
            case MirBinaryOp.UShr:
            {
                string rhsLlvm = _mod.GetValueLlvmType(rhsType);
                int lhsBits = lhsLlvm switch { "i8" => 8, "i16" => 16, "i64" => 64, _ => 32 };
                int rhsBits = rhsLlvm switch { "i8" => 8, "i16" => 16, "i64" => 64, _ => 32 };
                string matchedRhs = rhs;
                if (rhsLlvm != lhsLlvm)
                {
                    matchedRhs = NextTemp();
                    if (rhsBits < lhsBits)
                    {
                        EmitInst($"{matchedRhs} = zext {rhsLlvm} {rhs} to {lhsLlvm}");
                    }
                    else
                    {
                        EmitInst($"{matchedRhs} = trunc {rhsLlvm} {rhs} to {lhsLlvm}");
                    }
                }

                string maskConst = (lhsBits - 1).ToString(CultureInfo.InvariantCulture);
                string masked = NextTemp();
                EmitInst($"{masked} = and {lhsLlvm} {matchedRhs}, {maskConst}");

                bool isUnsigned = MirType.IsUnsignedInteger(lhsType);
                string shiftOp = bin.Op switch
                {
                    MirBinaryOp.Shl => "shl",
                    MirBinaryOp.Shr => isUnsigned ? "lshr" : "ashr",
                    _ => "lshr",
                };
                string res = NextTemp();
                EmitInst($"{res} = {shiftOp} {lhsLlvm} {lhs}, {masked}");
                return res;
            }

            case MirBinaryOp.Eq:
            case MirBinaryOp.Ne:
            case MirBinaryOp.Lt:
            case MirBinaryOp.Le:
            case MirBinaryOp.Gt:
            case MirBinaryOp.Ge:
            {
                bool isUnsigned = MirType.IsUnsignedInteger(lhsType);
                string pred = bin.Op switch
                {
                    MirBinaryOp.Eq => "eq",
                    MirBinaryOp.Ne => "ne",
                    MirBinaryOp.Lt => isUnsigned ? "ult" : "slt",
                    MirBinaryOp.Le => isUnsigned ? "ule" : "sle",
                    MirBinaryOp.Gt => isUnsigned ? "ugt" : "sgt",
                    _ => isUnsigned ? "uge" : "sge",
                };
                string res = NextTemp();
                EmitInst($"{res} = icmp {pred} {lhsLlvm} {lhs}, {rhs}");
                return res;
            }

            default:
                throw new InvalidOperationException($"Unsupported integer binary op {bin.Op}");
        }
    }

    private string EmitStringEquality(string lhs, string rhs, bool isNotEqual)
    {
        string lp = NextTemp();
        string ll = NextTemp();
        string rp = NextTemp();
        string rl = NextTemp();
        EmitInst($"{lp} = extractvalue {{ ptr, i64 }} {lhs}, 0");
        EmitInst($"{ll} = extractvalue {{ ptr, i64 }} {lhs}, 1");
        EmitInst($"{rp} = extractvalue {{ ptr, i64 }} {rhs}, 0");
        EmitInst($"{rl} = extractvalue {{ ptr, i64 }} {rhs}, 1");

        string lenEq = NextTemp();
        string checkBytesLbl = NextLabel("str_cmp_bytes");
        string cmpMemLbl = NextLabel("str_memcmp");
        string doneLbl = NextLabel("str_cmp_done");
        string entryCheckLbl = NextLabel("str_cmp_len");

        EmitInst($"br label %{entryCheckLbl}");
        EmitLabel(entryCheckLbl);
        EmitInst($"{lenEq} = icmp eq i64 {ll}, {rl}");
        EmitInst($"br i1 {lenEq}, label %{checkBytesLbl}, label %{doneLbl}");

        EmitLabel(checkBytesLbl);
        string isZeroLen = NextTemp();
        EmitInst($"{isZeroLen} = icmp eq i64 {ll}, 0");
        EmitInst($"br i1 {isZeroLen}, label %{doneLbl}, label %{cmpMemLbl}");

        EmitLabel(cmpMemLbl);
        string cmpInt = NextTemp();
        string bytesEq = NextTemp();
        EmitInst($"{cmpInt} = call i32 @memcmp(ptr {lp}, ptr {rp}, i64 {ll})");
        EmitInst($"{bytesEq} = icmp eq i32 {cmpInt}, 0");
        EmitInst($"br label %{doneLbl}");

        EmitLabel(doneLbl);
        string eqVal = NextTemp();
        EmitInst($"{eqVal} = phi i1 [ false, %{entryCheckLbl} ], [ true, %{checkBytesLbl} ], [ {bytesEq}, %{cmpMemLbl} ]");
        if (!isNotEqual)
        {
            return eqVal;
        }

        string neVal = NextTemp();
        EmitInst($"{neVal} = xor i1 {eqVal}, true");
        return neVal;
    }

    private string EmitUnary(MirRvalue.Unary un)
    {
        MirType opType = GetOperandType(un.Operand);
        string val = EmitOperand(un.Operand);
        string llvmTy = _mod.GetValueLlvmType(opType);

        switch (un.Op)
        {
            case MirUnaryOp.Neg:
            {
                if (llvmTy is "float" or "double")
                {
                    string res = NextTemp();
                    EmitInst($"{res} = fneg {llvmTy} {val}");
                    return res;
                }

                if (un.IsChecked)
                {
                    string pair = NextTemp();
                    string res = NextTemp();
                    string ovf = NextTemp();
                    EmitInst($"{pair} = call {{ {llvmTy}, i1 }} @llvm.ssub.with.overflow.{llvmTy}({llvmTy} 0, {llvmTy} {val})");
                    EmitInst($"{res} = extractvalue {{ {llvmTy}, i1 }} {pair}, 0");
                    EmitInst($"{ovf} = extractvalue {{ {llvmTy}, i1 }} {pair}, 1");
                    EmitPanicIfTrue(ovf, "arithmetic overflow");
                    return res;
                }
                else
                {
                    string res = NextTemp();
                    EmitInst($"{res} = sub {llvmTy} 0, {val}");
                    return res;
                }
            }

            case MirUnaryOp.Not:
            {
                string res = NextTemp();
                EmitInst($"{res} = xor i1 {val}, true");
                return res;
            }

            case MirUnaryOp.BitNot:
            {
                string res = NextTemp();
                EmitInst($"{res} = xor {llvmTy} {val}, -1");
                return res;
            }

            default:
                throw new InvalidOperationException($"Unsupported MIR unary op {un.Op}");
        }
    }

    private string EmitCast(MirRvalue.Cast cast)
    {
        MirType srcType = GetOperandType(cast.Operand);
        string srcVal = EmitOperand(cast.Operand);
        string srcLlvm = _mod.GetValueLlvmType(srcType);
        string dstLlvm = _mod.GetValueLlvmType(cast.TargetType);

        if (srcLlvm == dstLlvm)
        {
            return srcVal;
        }

        string res = NextTemp();
        bool srcIsInt = srcLlvm is "i8" or "i16" or "i32" or "i64";
        bool dstIsInt = dstLlvm is "i8" or "i16" or "i32" or "i64";

        if (srcIsInt && dstIsInt)
        {
            int srcBits = srcLlvm switch { "i8" => 8, "i16" => 16, "i64" => 64, _ => 32 };
            int dstBits = dstLlvm switch { "i8" => 8, "i16" => 16, "i64" => 64, _ => 32 };
            if (srcBits < dstBits)
            {
                string extOp = MirType.IsUnsignedInteger(srcType) ? "zext" : "sext";
                EmitInst($"{res} = {extOp} {srcLlvm} {srcVal} to {dstLlvm}");
                return res;
            }

            EmitInst($"{res} = trunc {srcLlvm} {srcVal} to {dstLlvm}");
            return res;
        }

        if (srcIsInt && (dstLlvm is "float" or "double"))
        {
            string itofp = MirType.IsUnsignedInteger(srcType) ? "uitofp" : "sitofp";
            EmitInst($"{res} = {itofp} {srcLlvm} {srcVal} to {dstLlvm}");
            return res;
        }

        if ((srcLlvm is "float" or "double") && dstIsInt)
        {
            string fSuffix = srcLlvm == "float" ? "f32" : "f64";
            string satKind = MirType.IsUnsignedInteger(cast.TargetType) ? "fptoui" : "fptosi";
            EmitInst($"{res} = call {dstLlvm} @llvm.{satKind}.sat.{dstLlvm}.{fSuffix}({srcLlvm} {srcVal})");
            return res;
        }

        if (srcLlvm == "float" && dstLlvm == "double")
        {
            EmitInst($"{res} = fpext float {srcVal} to double");
            return res;
        }

        if (srcLlvm == "double" && dstLlvm == "float")
        {
            EmitInst($"{res} = fptrunc double {srcVal} to float");
            return res;
        }

        if (srcLlvm == "i1" && dstIsInt)
        {
            EmitInst($"{res} = zext i1 {srcVal} to {dstLlvm}");
            return res;
        }

        throw new InvalidOperationException($"Unsupported MIR cast from {srcType} ({srcLlvm}) to {cast.TargetType} ({dstLlvm})");
    }

    private void EmitTerminator(MirTerminator term)
    {
        switch (term)
        {
            case MirTerminator.Goto g:
                EmitInst($"br label %bb_{g.TargetBlock}");
                break;

            case MirTerminator.Branch br:
            {
                string cond = EmitOperand(br.Condition);
                EmitInst($"br i1 {cond}, label %bb_{br.TrueBlock}, label %bb_{br.FalseBlock}");
                break;
            }

            case MirTerminator.MatchOption mo:
                EmitMatchOption(mo);
                break;

            case MirTerminator.MatchResult mr:
                EmitMatchResult(mr);
                break;

            case MirTerminator.GuardResultIsErr gr:
                EmitGuardResultIsErr(gr);
                break;

            case MirTerminator.GuardOptionIsNone go:
                EmitGuardOptionIsNone(go);
                break;

            case MirTerminator.Return ret:
                EmitReturn(ret);
                break;

            case MirTerminator.Unreachable:
                EmitInst("unreachable");
                break;
        }
    }

    private void EmitMatchOption(MirTerminator.MatchOption mo)
    {
        string someTramp = NextLabel("match_some");

        if (mo.Scrutinee is MirMatchScrutinee.PlaceScrutinee ps)
        {
            (string addr, MirType sType) = EmitPlaceAddress(ps.Place);
            string optPtr;
            MirType innerType;
            if (sType is MirType.OptionRefType orf)
            {
                optPtr = NextTemp();
                EmitInst($"{optPtr} = load ptr, ptr {addr}");
                innerType = orf.Inner;
            }
            else if (sType is MirType.OptionMutType om)
            {
                optPtr = NextTemp();
                EmitInst($"{optPtr} = load ptr, ptr {addr}");
                innerType = om.Inner;
            }
            else
            {
                optPtr = addr;
                innerType = ((MirType.OptionType)sType).Inner;
            }

            string optLlvm = _mod.GetValueLlvmType(new MirType.OptionType(innerType));
            string tagPtr = NextTemp();
            string tag = NextTemp();
            EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {optPtr}, i32 0, i32 0");
            EmitInst($"{tag} = load i1, ptr {tagPtr}");
            EmitInst($"br i1 {tag}, label %{someTramp}, label %bb_{mo.NoneBlock}");

            EmitLabel(someTramp);
            if (mo.SomeBindingLocalId >= 0 && _locals.TryGetValue(mo.SomeBindingLocalId, out LocalSlotInfo? bindInfo))
            {
                string valPtr = NextTemp();
                EmitInst($"{valPtr} = getelementptr inbounds {optLlvm}, ptr {optPtr}, i32 0, i32 1");
                BindMatchedPayloadFromPointer(bindInfo, valPtr, innerType, transfersOwnership: false);
            }

            EmitInst($"br label %bb_{mo.SomeBlock}");
            return;
        }

        var rs = (MirMatchScrutinee.RvalueScrutinee)mo.Scrutinee;
        MirType opType = GetOperandType(rs.Operand);
        if (rs.IsViewCursor || opType is MirType.OptionRefType or MirType.OptionMutType)
        {
            MirType innerType = opType switch
            {
                MirType.OptionRefType orf => orf.Inner,
                MirType.OptionMutType om => om.Inner,
                MirType.OptionType ot => ot.Inner,
                _ => throw new InvalidOperationException($"Unexpected view scrutinee type: {opType}"),
            };
            string optPtr = EmitOperand(rs.Operand);
            string optLlvm = _mod.GetValueLlvmType(new MirType.OptionType(innerType));
            string tagPtr = NextTemp();
            string tag = NextTemp();
            EmitInst($"{tagPtr} = getelementptr inbounds {optLlvm}, ptr {optPtr}, i32 0, i32 0");
            EmitInst($"{tag} = load i1, ptr {tagPtr}");
            EmitInst($"br i1 {tag}, label %{someTramp}, label %bb_{mo.NoneBlock}");

            EmitLabel(someTramp);
            if (mo.SomeBindingLocalId >= 0 && _locals.TryGetValue(mo.SomeBindingLocalId, out LocalSlotInfo? bindInfo))
            {
                string valPtr = NextTemp();
                EmitInst($"{valPtr} = getelementptr inbounds {optLlvm}, ptr {optPtr}, i32 0, i32 1");
                BindMatchedPayloadFromPointer(bindInfo, valPtr, innerType, transfersOwnership: false);
            }

            EmitInst($"br label %bb_{mo.SomeBlock}");
            return;
        }

        {
            MirType innerType = ((MirType.OptionType)opType).Inner;
            string optLlvm = _mod.GetValueLlvmType(opType);
            string optVal = EmitOperand(rs.Operand);
            string tag = NextTemp();
            EmitInst($"{tag} = extractvalue {optLlvm} {optVal}, 0");
            EmitInst($"br i1 {tag}, label %{someTramp}, label %bb_{mo.NoneBlock}");

            EmitLabel(someTramp);
            string payload = NextTemp();
            EmitInst($"{payload} = extractvalue {optLlvm} {optVal}, 1");
            if (mo.SomeBindingLocalId >= 0 && _locals.TryGetValue(mo.SomeBindingLocalId, out LocalSlotInfo? bindInfo))
            {
                BindConsumedPayloadToLocal(bindInfo, payload);
            }
            else
            {
                SpillAndDropValueIfNeeded(innerType, payload);
            }

            EmitInst($"br label %bb_{mo.SomeBlock}");
        }
    }

    private void EmitMatchResult(MirTerminator.MatchResult mr)
    {
        string okTramp = NextLabel("match_ok");
        string errTramp = NextLabel("match_err");

        if (mr.Scrutinee is MirMatchScrutinee.PlaceScrutinee ps)
        {
            (string resPtr, MirType sType) = EmitPlaceAddress(ps.Place);
            var resType = (MirType.ResultType)sType;
            string resLlvm = _mod.GetValueLlvmType(resType);
            string tagPtr = NextTemp();
            string tag = NextTemp();
            EmitInst($"{tagPtr} = getelementptr inbounds {resLlvm}, ptr {resPtr}, i32 0, i32 0");
            EmitInst($"{tag} = load i1, ptr {tagPtr}");
            EmitInst($"br i1 {tag}, label %{okTramp}, label %{errTramp}");

            EmitLabel(okTramp);
            if (mr.OkBindingLocalId >= 0 && _locals.TryGetValue(mr.OkBindingLocalId, out LocalSlotInfo? okBind))
            {
                string okPtr = NextTemp();
                EmitInst($"{okPtr} = getelementptr inbounds {resLlvm}, ptr {resPtr}, i32 0, i32 1");
                BindMatchedPayloadFromPointer(okBind, okPtr, resType.Ok, transfersOwnership: false);
            }

            EmitInst($"br label %bb_{mr.OkBlock}");

            EmitLabel(errTramp);
            if (mr.ErrBindingLocalId >= 0 && _locals.TryGetValue(mr.ErrBindingLocalId, out LocalSlotInfo? errBind))
            {
                string errPtr = NextTemp();
                EmitInst($"{errPtr} = getelementptr inbounds {resLlvm}, ptr {resPtr}, i32 0, i32 2");
                BindMatchedPayloadFromPointer(errBind, errPtr, resType.Err, transfersOwnership: false);
            }

            EmitInst($"br label %bb_{mr.ErrBlock}");
            return;
        }

        var rs = (MirMatchScrutinee.RvalueScrutinee)mr.Scrutinee;
        var rType = (MirType.ResultType)GetOperandType(rs.Operand);
        string rLlvm = _mod.GetValueLlvmType(rType);
        string rVal = EmitOperand(rs.Operand);
        string isOk = NextTemp();
        EmitInst($"{isOk} = extractvalue {rLlvm} {rVal}, 0");
        EmitInst($"br i1 {isOk}, label %{okTramp}, label %{errTramp}");

        EmitLabel(okTramp);
        string okVal = NextTemp();
        EmitInst($"{okVal} = extractvalue {rLlvm} {rVal}, 1");
        if (mr.OkBindingLocalId >= 0 && _locals.TryGetValue(mr.OkBindingLocalId, out LocalSlotInfo? okSlot))
        {
            BindConsumedPayloadToLocal(okSlot, okVal);
        }
        else
        {
            SpillAndDropValueIfNeeded(rType.Ok, okVal);
        }

        EmitInst($"br label %bb_{mr.OkBlock}");

        EmitLabel(errTramp);
        string errVal = NextTemp();
        EmitInst($"{errVal} = extractvalue {rLlvm} {rVal}, 2");
        if (mr.ErrBindingLocalId >= 0 && _locals.TryGetValue(mr.ErrBindingLocalId, out LocalSlotInfo? errSlot))
        {
            BindConsumedPayloadToLocal(errSlot, errVal);
        }
        else
        {
            SpillAndDropValueIfNeeded(rType.Err, errVal);
        }

        EmitInst($"br label %bb_{mr.ErrBlock}");
    }

    private void EmitGuardResultIsErr(MirTerminator.GuardResultIsErr gr)
    {
        var rType = (MirType.ResultType)GetOperandType(gr.ConsumedResult);
        string rLlvm = _mod.GetValueLlvmType(rType);
        string rVal = EmitOperand(gr.ConsumedResult);
        string isOk = NextTemp();
        string okTramp = NextLabel("guard_res_ok");
        string errTramp = NextLabel("guard_res_err");

        EmitInst($"{isOk} = extractvalue {rLlvm} {rVal}, 0");
        EmitInst($"br i1 {isOk}, label %{okTramp}, label %{errTramp}");

        EmitLabel(okTramp);
        string okVal = NextTemp();
        EmitInst($"{okVal} = extractvalue {rLlvm} {rVal}, 1");
        if (gr.OkLocalId >= 0 && _locals.TryGetValue(gr.OkLocalId, out LocalSlotInfo? okSlot))
        {
            BindConsumedPayloadToLocal(okSlot, okVal);
        }
        else
        {
            SpillAndDropValueIfNeeded(rType.Ok, okVal);
        }

        EmitInst($"br label %bb_{gr.OkBlock}");

        EmitLabel(errTramp);
        string errVal = NextTemp();
        EmitInst($"{errVal} = extractvalue {rLlvm} {rVal}, 2");
        if (gr.ErrWrapperLocalId >= 0 && _locals.TryGetValue(gr.ErrWrapperLocalId, out LocalSlotInfo? errSlot))
        {
            BindConsumedPayloadToLocal(errSlot, errVal);
        }
        else
        {
            SpillAndDropValueIfNeeded(rType.Err, errVal);
        }

        EmitInst($"br label %bb_{gr.ErrBlock}");
    }

    private void EmitGuardOptionIsNone(MirTerminator.GuardOptionIsNone go)
    {
        var oType = (MirType.OptionType)GetOperandType(go.ConsumedOption);
        string oLlvm = _mod.GetValueLlvmType(oType);
        string oVal = EmitOperand(go.ConsumedOption);
        string has = NextTemp();
        string someTramp = NextLabel("guard_opt_some");

        EmitInst($"{has} = extractvalue {oLlvm} {oVal}, 0");
        EmitInst($"br i1 {has}, label %{someTramp}, label %bb_{go.NoneBlock}");

        EmitLabel(someTramp);
        string val = NextTemp();
        EmitInst($"{val} = extractvalue {oLlvm} {oVal}, 1");
        if (go.SomeValueLocalId >= 0 && _locals.TryGetValue(go.SomeValueLocalId, out LocalSlotInfo? someSlot))
        {
            BindConsumedPayloadToLocal(someSlot, val);
        }
        else
        {
            SpillAndDropValueIfNeeded(oType.Inner, val);
        }

        EmitInst($"br label %bb_{go.SomeBlock}");
    }

    private void BindMatchedPayloadFromPointer(LocalSlotInfo bindInfo, string payloadPtr, MirType payloadType, bool transfersOwnership)
    {
        if (bindInfo.IsRefSlot)
        {
            EmitInst($"store ptr {payloadPtr}, ptr {bindInfo.AllocaName}");
            return;
        }

        string payloadLlvm = _mod.GetValueLlvmType(payloadType);
        string loaded = NextTemp();
        EmitInst($"{loaded} = load {payloadLlvm}, ptr {payloadPtr}");
        if (transfersOwnership && bindInfo.DropFlagName is not null)
        {
            EmitDropLocalIfLive(bindInfo);
        }

        EmitInst($"store {bindInfo.StorageLlvmType} {loaded}, ptr {bindInfo.AllocaName}");
        if (bindInfo.DropFlagName is not null)
        {
            EmitInst($"store i1 {(transfersOwnership ? "true" : "false")}, ptr {bindInfo.DropFlagName}");
        }
    }

    private void BindConsumedPayloadToLocal(LocalSlotInfo bindInfo, string payloadSsa)
    {
        if (bindInfo.DropFlagName is not null)
        {
            EmitDropLocalIfLive(bindInfo);
        }

        EmitInst($"store {bindInfo.StorageLlvmType} {payloadSsa}, ptr {bindInfo.AllocaName}");
        if (bindInfo.DropFlagName is not null)
        {
            EmitInst($"store i1 true, ptr {bindInfo.DropFlagName}");
        }
    }

    private void EmitReturn(MirTerminator.Return ret)
    {
        string? retSsa = null;
        if (ret.RefReturnPlace is not null)
        {
            (retSsa, _) = EmitPlaceAddress(ret.RefReturnPlace);
        }
        else if (ret.Value is not null)
        {
            retSsa = EmitOperand(ret.Value);
        }

        foreach (MirLocal loc in _fn.Locals.OrderByDescending(l => l.Id))
        {
            if (loc.IsAnalysisOnlyBorrow) continue;
            LocalSlotInfo info = _locals[loc.Id];
            if (info.DropFlagName is not null)
            {
                EmitDropLocalIfLive(info);
            }
        }

        if (_fn.Signature.ReturnsByRef)
        {
            EmitInst($"ret ptr {retSsa ?? "null"}");
        }
        else if (_fn.Signature.ReturnType is MirType.VoidType)
        {
            EmitInst("ret void");
        }
        else
        {
            string retLlvm = _mod.GetValueLlvmType(_fn.Signature.ReturnType);
            EmitInst($"ret {retLlvm} {retSsa ?? ModuleEmitter.GetZeroLiteral(retLlvm)}");
        }
    }

    private string EmitOperand(MirOperand operand)
    {
        switch (operand)
        {
            case MirOperand.Const c:
                return EmitConstant(c.Value, c.Type);

            case MirOperand.CopyOrMove cm:
            {
                if (cm.Type is MirType.RefType &&
                    cm.Place.Projections.Count == 0 &&
                    _locals.TryGetValue(cm.Place.LocalId, out LocalSlotInfo? rInfo) &&
                    rInfo.IsRefSlot)
                {
                    string ptrVal = NextTemp();
                    EmitInst($"{ptrVal} = load ptr, ptr {rInfo.AllocaName}");
                    return ptrVal;
                }

                return EmitOperandFromPlaceByValue(cm.Place, cm.Type);
            }

            default:
                throw new InvalidOperationException($"Unsupported MIR operand: {operand}");
        }
    }

    private string EmitOperandFromPlaceByValue(MirPlace place, MirType operandType)
    {
        (string addr, _) = EmitPlaceAddress(place);
        string llvmTy = _mod.GetValueLlvmType(operandType);
        string loaded = NextTemp();
        EmitInst($"{loaded} = load {llvmTy}, ptr {addr}");

        if (_mod.RequiresCleanupGlue(operandType))
        {
            EmitInst($"store {llvmTy} {ModuleEmitter.GetZeroLiteral(llvmTy)}, ptr {addr}");
            if (place.Projections.Count == 0 &&
                _locals.TryGetValue(place.LocalId, out LocalSlotInfo? slot) &&
                !slot.IsRefSlot &&
                slot.DropFlagName is not null)
            {
                EmitInst($"store i1 false, ptr {slot.DropFlagName}");
            }
        }

        return loaded;
    }

    private string LoadPlaceValueWithoutMove(MirPlace place, MirType type)
    {
        (string addr, _) = EmitPlaceAddress(place);
        string llvmTy = _mod.GetValueLlvmType(type);
        string loaded = NextTemp();
        EmitInst($"{loaded} = load {llvmTy}, ptr {addr}");
        return loaded;
    }

    private string EmitConstant(MirConstant constant, MirType operandType)
    {
        switch (constant)
        {
            case MirConstant.BoolConst b:
                return b.Value ? "true" : "false";

            case MirConstant.Int32Const i32:
                return i32.Value.ToString(CultureInfo.InvariantCulture);

            case MirConstant.Int64Const i64:
                return i64.Value.ToString(CultureInfo.InvariantCulture);

            case MirConstant.Float32Const f32:
                return FormatFloat32Hex(f32.Value);

            case MirConstant.Float64Const f64:
                return FormatFloatHex(f64.Value);

            case MirConstant.StringConst sc:
            {
                if (sc.Value.Length == 0)
                {
                    return "{ ptr @.str.empty, i64 0 }";
                }

                (string sym, int byteLen) = _mod.InternString(sc.Value);
                return $"{{ ptr {sym}, i64 {byteLen} }}";
            }

            case MirConstant.Utf8BytesConst u8:
            {
                string sym = _mod.InternUtf8Bytes(u8.Bytes);
                return $"{{ ptr {sym}, i32 {u8.Bytes.Length} }}";
            }

            case MirConstant.ZeroDefault zd:
            {
                string llvmTy = _mod.GetValueLlvmType(zd.Type);
                return ModuleEmitter.GetZeroLiteral(llvmTy);
            }

            default:
                return ModuleEmitter.GetZeroLiteral(_mod.GetValueLlvmType(operandType));
        }
    }

    private (string PtrSsa, MirType ValueType) EmitPlaceAddress(MirPlace place)
    {
        LocalSlotInfo slot = _locals[place.LocalId];
        string curPtr;
        MirType curType;
        int projIndex = 0;

        if (slot.IsRefSlot)
        {
            curPtr = NextTemp();
            EmitInst($"{curPtr} = load ptr, ptr {slot.AllocaName}");
            curType = slot.ReferentOrStorageType;

            if (place.Projections.Count > 0 &&
                place.Projections[0] is MirProjection.Deref d0 &&
                TypesEqual(d0.ResultType, curType))
            {
                projIndex = 1;
            }
        }
        else
        {
            curPtr = slot.AllocaName;
            curType = slot.ReferentOrStorageType;

            if (slot.Local.PassKind is MirPassKind.ImplicitThisShared or MirPassKind.ImplicitThisMut &&
                curType is MirType.ClassType &&
                place.Projections.Count > 0 &&
                place.Projections[0] is MirProjection.Deref d0 &&
                TypesEqual(d0.ResultType, curType))
            {
                projIndex = 1;
            }
        }

        for (int i = projIndex; i < place.Projections.Count; i++)
        {
            MirProjection proj = place.Projections[i];
            switch (proj)
            {
                case MirProjection.Deref deref:
                {
                    string loadedPtr = NextTemp();
                    EmitInst($"{loadedPtr} = load ptr, ptr {curPtr}");
                    curPtr = loadedPtr;
                    curType = deref.ResultType;
                    break;
                }

                case MirProjection.Field fld:
                {
                    if (fld.ThroughClassHeap || curType is MirType.ClassType)
                    {
                        string clsName = ((MirType.ClassType)curType).Name;
                        string clsTy = _mod.GetClassStorageTypeName(clsName);
                        string objPtr = NextTemp();
                        string fldPtr = NextTemp();
                        EmitInst($"{objPtr} = load ptr, ptr {curPtr}");
                        EmitInst($"{fldPtr} = getelementptr inbounds {clsTy}, ptr {objPtr}, i32 0, i32 {fld.FieldIndex}");
                        curPtr = fldPtr;
                        curType = fld.FieldType;
                    }
                    else
                    {
                        string aggTy = _mod.GetValueLlvmType(curType);
                        string fldPtr = NextTemp();
                        EmitInst($"{fldPtr} = getelementptr inbounds {aggTy}, ptr {curPtr}, i32 0, i32 {fld.FieldIndex}");
                        curPtr = fldPtr;
                        curType = fld.FieldType;
                    }

                    break;
                }

                case MirProjection.Index idx:
                {
                    string aggTy = _mod.GetValueLlvmType(curType);
                    string elemLlvm = _mod.GetValueLlvmType(idx.ElementType);
                    string idxVal = EnsureI32(EmitOperand(idx.IndexOperand), GetOperandType(idx.IndexOperand));

                    string dpSlot = NextTemp();
                    string lenSlot = NextTemp();
                    string dataPtr = NextTemp();
                    string len = NextTemp();
                    EmitInst($"{dpSlot} = getelementptr inbounds {aggTy}, ptr {curPtr}, i32 0, i32 0");
                    EmitInst($"{lenSlot} = getelementptr inbounds {aggTy}, ptr {curPtr}, i32 0, i32 1");
                    EmitInst($"{dataPtr} = load ptr, ptr {dpSlot}");
                    EmitInst($"{len} = load i32, ptr {lenSlot}");

                    EmitBoundsCheck(idxVal, len, "index out of bounds");
                    string idx64 = NextTemp();
                    string elemPtr = NextTemp();
                    EmitInst($"{idx64} = zext i32 {idxVal} to i64");
                    EmitInst($"{elemPtr} = getelementptr inbounds {elemLlvm}, ptr {dataPtr}, i64 {idx64}");
                    curPtr = elemPtr;
                    curType = idx.ElementType;
                    break;
                }
            }
        }

        return (curPtr, curType);
    }

    private string EnsureI32(string ssaVal, MirType opType)
    {
        string llvmTy = _mod.GetValueLlvmType(opType);
        if (llvmTy == "i64")
        {
            string tr = NextTemp();
            EmitInst($"{tr} = trunc i64 {ssaVal} to i32");
            return tr;
        }

        if (llvmTy is "i8" or "i16")
        {
            string extOp = MirType.IsUnsignedInteger(opType) ? "zext" : "sext";
            string ext = NextTemp();
            EmitInst($"{ext} = {extOp} {llvmTy} {ssaVal} to i32");
            return ext;
        }

        return ssaVal;
    }

    private string EmitSizeOf(string llvmType)
    {
        string gep = NextTemp();
        string sz = NextTemp();
        EmitInst($"{gep} = getelementptr {llvmType}, ptr null, i64 1");
        EmitInst($"{sz} = ptrtoint ptr {gep} to i64");
        return sz;
    }

    private void EmitRecomputeClampedMinOne(string rawBytes, string targetTemp)
    {
        string isZero = NextTemp();
        EmitInst($"{isZero} = icmp eq i64 {rawBytes}, 0");
        EmitInst($"{targetTemp} = select i1 {isZero}, i64 1, i64 {rawBytes}");
    }

    private string EmitCheckedMalloc(string bytesI64)
    {
        string ptr = NextTemp();
        string oom = NextTemp();
        EmitInst($"{ptr} = call noalias ptr @malloc(i64 {bytesI64})");
        EmitInst($"{oom} = icmp eq ptr {ptr}, null");
        EmitPanicIfTrue(oom, "out of memory");
        return ptr;
    }

    private string EmitCheckedRealloc(string oldPtr, string bytesI64)
    {
        string ptr = NextTemp();
        string oom = NextTemp();
        EmitInst($"{ptr} = call noalias ptr @realloc(ptr {oldPtr}, i64 {bytesI64})");
        EmitInst($"{oom} = icmp eq ptr {ptr}, null");
        EmitPanicIfTrue(oom, "out of memory");
        return ptr;
    }

    private void EmitBoundsCheck(string idxI32, string lenI32, string panicMsg)
    {
        string oob = NextTemp();
        EmitInst($"{oob} = icmp uge i32 {idxI32}, {lenI32}");
        EmitPanicIfTrue(oob, panicMsg);
    }

    private void EmitPanicIfTrue(string condI1, string message)
    {
        (string sym, int len) = _mod.InternString(message);
        string panicLbl = NextLabel("panic");
        string okLbl = NextLabel("ok");
        EmitInst($"br i1 {condI1}, label %{panicLbl}, label %{okLbl}");
        EmitLabel(panicLbl);
        EmitInst($"call void @__vib_panic(ptr {sym}, i64 {len})");
        EmitInst("unreachable");
        EmitLabel(okLbl);
    }

    private void EmitPanicIfFalse(string condI1, string message)
    {
        (string sym, int len) = _mod.InternString(message);
        string okLbl = NextLabel("ok");
        string panicLbl = NextLabel("panic");
        EmitInst($"br i1 {condI1}, label %{okLbl}, label %{panicLbl}");
        EmitLabel(panicLbl);
        EmitInst($"call void @__vib_panic(ptr {sym}, i64 {len})");
        EmitInst("unreachable");
        EmitLabel(okLbl);
    }

    private void EmitPanicDynamicIfFalse(string condI1, string msgStrPair)
    {
        string okLbl = NextLabel("ok");
        string panicLbl = NextLabel("panic");
        EmitInst($"br i1 {condI1}, label %{okLbl}, label %{panicLbl}");
        EmitLabel(panicLbl);
        string mp = NextTemp();
        string ml = NextTemp();
        EmitInst($"{mp} = extractvalue {{ ptr, i64 }} {msgStrPair}, 0");
        EmitInst($"{ml} = extractvalue {{ ptr, i64 }} {msgStrPair}, 1");
        EmitInst($"call void @__vib_panic(ptr {mp}, i64 {ml})");
        EmitInst("unreachable");
        EmitLabel(okLbl);
    }

    private static MirType GetOperandType(MirOperand op) =>
        op switch
        {
            MirOperand.CopyOrMove cm => cm.Type,
            MirOperand.Const c => c.Type,
            _ => MirType.Void,
        };

    private static MirType GetRvalueType(MirRvalue rv) =>
        rv switch
        {
            MirRvalue.Use u => GetOperandType(u.Operand),
            MirRvalue.Binary b => b.ResultType,
            MirRvalue.Unary u => u.ResultType,
            MirRvalue.Cast c => c.TargetType,
            MirRvalue.AllocClass ac => new MirType.ClassType(ac.ClassName, false),
            MirRvalue.ConstructStruct cs => new MirType.StructType(cs.StructName),
            MirRvalue.MakeSomeWrapper msw => new MirType.SomeWrapperType(msw.InnerType),
            MirRvalue.MakeOkWrapper mow => new MirType.OkWrapperType(mow.OkType),
            MirRvalue.MakeErrWrapper mew => new MirType.ErrWrapperType(mew.ErrType),
            MirRvalue.LiftOptionSome los => new MirType.OptionType(los.InnerType),
            MirRvalue.LiftOptionNone lon => new MirType.OptionType(lon.InnerType),
            MirRvalue.LiftResultOk lro => new MirType.ResultType(lro.OkType, lro.ErrType),
            MirRvalue.LiftResultErr lre => new MirType.ResultType(lre.OkType, lre.ErrType),
            MirRvalue.UnwrapSomeWrapper usw => usw.InnerType,
            MirRvalue.UnwrapOkWrapper uow => uow.OkType,
            MirRvalue.UnwrapErrWrapper uew => uew.ErrType,
            MirRvalue.StackSpan ss => new MirType.SpanType(ss.ElementType, ss.IsReadOnly),
            _ => MirType.Void,
        };

    private static bool TypesEqual(MirType a, MirType b) =>
        string.Equals(ModuleEmitter.GetTypeKey(a), ModuleEmitter.GetTypeKey(b), StringComparison.Ordinal);

    private static string FormatFloatHex(double d)
    {
        if (double.IsNaN(d))
        {
            return "0x7FF8000000000000";
        }

        long bits = BitConverter.DoubleToInt64Bits(d);
        return "0x" + unchecked((ulong)bits).ToString("X16", CultureInfo.InvariantCulture);
    }

    private static string FormatFloat32Hex(float f)
    {
        if (float.IsNaN(f))
        {
            return "0x7FF8000000000000";
        }

        long bits = BitConverter.DoubleToInt64Bits((double)f);
        return "0x" + unchecked((ulong)bits).ToString("X16", CultureInfo.InvariantCulture);
    }
}
