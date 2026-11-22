using System.Diagnostics.CodeAnalysis;

namespace Vibranium.Self;

public readonly struct LocalSlot
{
    public readonly bool IsRefSlot;
    public readonly int StorageOrRefTypeId;
    public readonly bool HasDropFlag;

    public LocalSlot(bool isRefSlot, int storageOrRefTypeId, bool hasDropFlag)
    {
        IsRefSlot = isRefSlot;
        StorageOrRefTypeId = storageOrRefTypeId;
        HasDropFlag = hasDropFlag;
    }
}

public readonly struct StackSpanSlot
{
    public readonly int ElemTypeId;
    public readonly int Count;

    public StackSpanSlot(int elemTypeId, int count)
    {
        ElemTypeId = elemTypeId;
        Count = count;
    }
}

public struct EmitCtx
{
    // String pool (`@.str.0`, `@.str.1`, ...)
    public BytePool StrBytes;
    public Vec<ByteSlice> StrEntries;
    private StrMap<int> _strLookup;

    // UTF-8 byte literal pool (`@.b0`, `@.b1`, ...)
    public BytePool Utf8Bytes;
    public Vec<ByteSlice> Utf8Entries;
    private StrMap<int> _utf8Lookup;

    // Drop & linear-free workqueues
    public Vec<int> PendingDropTypes;
    public int NextDropCursor;
    private IntMap<int> _scheduledDropTypes;

    public Vec<int> PendingLinearFreeTypes;
    public int NextLinearFreeCursor;
    private IntMap<int> _scheduledLinearFreeTypes;

    public bool UsesAnyNativeSymbol;

    public EmitCtx(int capacity)
    {
        StrBytes = new BytePool(4096);
        StrEntries = new Vec<ByteSlice>(capacity);
        _strLookup = new StrMap<int>(capacity);

        Utf8Bytes = new BytePool(4096);
        Utf8Entries = new Vec<ByteSlice>(capacity);
        _utf8Lookup = new StrMap<int>(capacity);

        PendingDropTypes = new Vec<int>(32);
        NextDropCursor = 0;
        _scheduledDropTypes = new IntMap<int>(32);

        PendingLinearFreeTypes = new Vec<int>(16);
        NextLinearFreeCursor = 0;
        _scheduledLinearFreeTypes = new IntMap<int>(16);

        UsesAnyNativeSymbol = false;
    }

    public int InternStr(ReadOnlySpan<byte> utf8)
    {
        int existing = _strLookup.Find(utf8);
        if (existing >= 0)
        {
            return existing;
        }
        int id = StrEntries.Count;
        StrEntries.Push(StrBytes.Add(utf8));
        _strLookup.Set(utf8, id);
        return id;
    }

    public int InternUtf8Lit(ReadOnlySpan<byte> bytes)
    {
        // Encode raw bytes as hex key so arbitrary binary slices (including 0x00) hash/compare safely in StrMap
        var hexKey = new StringBuf(bytes.Length * 2 + 2);
        hexKey.AppendAscii((byte)'B');
        for (int i = 0; i < bytes.Length; i++)
        {
            AppendHexByteUpper(bytes[i], ref hexKey);
        }
        int existing = _utf8Lookup.Find(hexKey.AsUtf8());
        if (existing >= 0)
        {
            return existing;
        }
        int id = Utf8Entries.Count;
        Utf8Entries.Push(Utf8Bytes.Add(bytes));
        _utf8Lookup.Set(hexKey.AsUtf8(), id);
        return id;
    }

    public void ScheduleDrop(int typeId)
    {
        if (_scheduledDropTypes.Contains(typeId))
        {
            return;
        }
        _scheduledDropTypes.Set(typeId, 1);
        PendingDropTypes.Push(typeId);
    }

    public void ScheduleLinearFree(int classTypeId)
    {
        if (_scheduledLinearFreeTypes.Contains(classTypeId))
        {
            return;
        }
        _scheduledLinearFreeTypes.Set(classTypeId, 1);
        PendingLinearFreeTypes.Push(classTypeId);
    }

    internal static void AppendHexByteUpper(byte b, ref StringBuf sb)
    {
        ReadOnlySpan<byte> hex = "0123456789ABCDEF"u8;
        sb.AppendAscii(hex[(b >> 4) & 0xF]);
        sb.AppendAscii(hex[b & 0xF]);
    }
}

public static class LlvmEmitter
{
    public static void EmitModule(
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        in ManifestBundle bundle,
        ref StringBuf outIr)
    {
        var ectx = new EmitCtx(128);

        var funcsIr = new StringBuf(65536);
        for (int i = 0; i < mir.Funcs.Count; i++)
        {
            MirFunc fn = mir.Funcs[i];
            EmitFunction(in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref funcsIr);
            funcsIr.AppendAscii((byte)'\n');
        }

        EmitEntryWrapper(in ast, in types, in mir, ref ectx, ref funcsIr);
        funcsIr.AppendAscii((byte)'\n');

        var dropsIr = new StringBuf(8192);
        while (ectx.NextDropCursor < ectx.PendingDropTypes.Count || ectx.NextLinearFreeCursor < ectx.PendingLinearFreeTypes.Count)
        {
            while (ectx.NextDropCursor < ectx.PendingDropTypes.Count)
            {
                int dropTypeId = ectx.PendingDropTypes[ectx.NextDropCursor];
                ectx.NextDropCursor += 1;
                EmitDropGlueForType(dropTypeId, in ast, in names, in types, ref ectx, ref dropsIr);
                dropsIr.AppendAscii((byte)'\n');
            }

            while (ectx.NextLinearFreeCursor < ectx.PendingLinearFreeTypes.Count)
            {
                int clsTypeId = ectx.PendingLinearFreeTypes[ectx.NextLinearFreeCursor];
                ectx.NextLinearFreeCursor += 1;
                EmitLinearFreeGlue(clsTypeId, in ast, in names, in types, ref ectx, ref dropsIr);
                dropsIr.AppendAscii((byte)'\n');
            }
        }

        // 1. Runtime fragments (`vib_rt.ll` + optional `vib_std.ll`)
        outIr.Append(bundle.CoreNativeIr.AsReadOnlySpan());
        if (bundle.CoreNativeIr.Count > 0 && bundle.CoreNativeIr[bundle.CoreNativeIr.Count - 1] != (byte)'\n')
        {
            outIr.AppendAscii((byte)'\n');
        }
        outIr.AppendAscii((byte)'\n');

        if (bundle.StdNativeIr.Count > 0 && (mir.UsesStdNative || ectx.UsesAnyNativeSymbol))
        {
            outIr.Append(bundle.StdNativeIr.AsReadOnlySpan());
            if (bundle.StdNativeIr[bundle.StdNativeIr.Count - 1] != (byte)'\n')
            {
                outIr.AppendAscii((byte)'\n');
            }
            outIr.AppendAscii((byte)'\n');
        }

        // 2. Concrete struct & class type definitions
        EmitTypeDefinitions(in ast, in names, in types, ref outIr);

        // 3. Pooled string & UTF-8 byte globals
        EmitPooledGlobals(in ectx, ref outIr);

        // 4. Deterministic bounded-stack worklist drop glue
        if (dropsIr.Length > 0)
        {
            outIr.Append("; --- Deterministic Drop & Cleanup Glue ---\n"u8);
            outIr.Append(dropsIr.AsUtf8());
        }

        // 5. User MIR functions + `@__vib_entry`
        outIr.Append("; --- User MIR Functions ---\n"u8);
        outIr.Append(funcsIr.AsUtf8());

        // 6. Attribute groups
        outIr.Append("attributes #0 = { noreturn nounwind }\n"u8);
        outIr.Append("attributes #2 = { nounwind }\n"u8);
    }

    private static bool RequiresCleanup(int typeId, in AstArena ast, in TypeTable types) =>
        types.NeedsMemoryDrop(typeId, in ast) || types.IsLinear(typeId, in ast);

    private static bool IsDisposableClassType(int typeId, in AstArena ast, in TypeTable types)
    {
        if (typeId < 0) return false;
        TypeInfo ti = types.Items[typeId];
        return ti.Kind == TypeKinds.Class && (ast.Types[ti.A].Flags & DeclFlags.IsDisposable) != 0;
    }

    private static void AppendMangled(ReadOnlySpan<byte> raw, ref StringBuf sb)
    {
        uint hash = 2166136261u;
        int startLen = sb.Length;
        int written = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            byte c = raw[i];
            hash ^= c;
            hash *= 16777619u;

            if (written < 48)
            {
                if ((c >= (byte)'a' && c <= (byte)'z') ||
                    (c >= (byte)'A' && c <= (byte)'Z') ||
                    (c >= (byte)'0' && c <= (byte)'9'))
                {
                    sb.AppendAscii(c);
                    written += 1;
                }
                else if (c == (byte)'<')
                {
                    sb.Append("_LT_"u8);
                    written += 4;
                }
                else if (c == (byte)'>')
                {
                    sb.Append("_GT_"u8);
                    written += 4;
                }
                else
                {
                    sb.AppendAscii((byte)'_');
                    written += 1;
                }
            }
        }
        _ = startLen;
        sb.AppendAscii((byte)'_');
        ReadOnlySpan<byte> hex = "0123456789abcdef"u8;
        for (int s = 28; s >= 0; s -= 4)
        {
            sb.AppendAscii(hex[(int)((hash >> s) & 0xFu)]);
        }
    }

    private static void AppendUserTypeIdent(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref StringBuf rawKey)
    {
        TypeInfo ti = types.Items[typeId];
        TypeDecl td = ast.Types[ti.A];
        rawKey.Append(names.Text(td.NameId));
        rawKey.AppendAscii((byte)'$');
        rawKey.AppendInt(ti.A);
        if (ti.C > 0)
        {
            rawKey.AppendAscii((byte)'<');
            for (int i = 0; i < ti.C; i++)
            {
                if (i > 0) rawKey.AppendAscii((byte)',');
                rawKey.Append(types.GetKey(types.TypeArgs[ti.B + i]));
            }
            rawKey.AppendAscii((byte)'>');
        }
    }

    private static void AppendStructTypeName(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref StringBuf sb)
    {
        var raw = new StringBuf(64);
        AppendUserTypeIdent(typeId, in ast, in names, in types, ref raw);
        sb.Append("%struct."u8);
        AppendMangled(raw.AsUtf8(), ref sb);
    }

    private static void AppendClassStorageTypeName(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref StringBuf sb)
    {
        var raw = new StringBuf(64);
        AppendUserTypeIdent(typeId, in ast, in names, in types, ref raw);
        sb.Append("%class."u8);
        AppendMangled(raw.AsUtf8(), ref sb);
    }

    private static void AppendFuncSymbol(int funcInstId, in TokenArena ta, in MirModuleArena mir, ref EmitCtx ectx, ref StringBuf sb)
    {
        FuncInstance fi = mir.FuncInstances[funcInstId];
        if (fi.NativeSymLitIdx >= 0)
        {
            ectx.UsesAnyNativeSymbol = true;
            sb.AppendAscii((byte)'@');
            sb.Append(ta.GetLit(fi.NativeSymLitIdx));
            return;
        }
        sb.Append("@vib_fn_"u8);
        AppendMangled(mir.GetFuncKey(funcInstId), ref sb);
    }

    private static void AppendCleanupSymbol(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref EmitCtx ectx, ref StringBuf sb)
    {
        if (IsDisposableClassType(typeId, in ast, in types))
        {
            ectx.ScheduleLinearFree(typeId);
            var raw = new StringBuf(64);
            AppendUserTypeIdent(typeId, in ast, in names, in types, ref raw);
            sb.Append("@__vib_free_linear_"u8);
            AppendMangled(raw.AsUtf8(), ref sb);
            return;
        }

        ectx.ScheduleDrop(typeId);
        sb.Append("@__vib_drop_"u8);
        AppendMangled(types.GetKey(typeId), ref sb);
    }

    private static void AppendWorklistCleanupSymbol(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref EmitCtx ectx, ref StringBuf sb)
    {
        if (IsDisposableClassType(typeId, in ast, in types))
        {
            AppendCleanupSymbol(typeId, in ast, in names, in types, ref ectx, ref sb);
            return;
        }
        ectx.ScheduleDrop(typeId);
        sb.Append("@__vib_drop_wl_"u8);
        AppendMangled(types.GetKey(typeId), ref sb);
    }

    private static void AppendValueLlvmType(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref StringBuf sb)
    {
        switch (typeId)
        {
            case TypeIds.Void:
            case TypeIds.NoneUnit:
            case TypeIds.I8:
            case TypeIds.U8:
                sb.Append("i8"u8);
                return;
            case TypeIds.Bool:
                sb.Append("i1"u8);
                return;
            case TypeIds.I16:
            case TypeIds.U16:
            case TypeIds.Char:
                sb.Append("i16"u8);
                return;
            case TypeIds.I32:
            case TypeIds.U32:
            case TypeIds.Error:
                sb.Append("i32"u8);
                return;
            case TypeIds.I64:
            case TypeIds.U64:
            case TypeIds.NInt:
            case TypeIds.NUInt:
                sb.Append("i64"u8);
                return;
            case TypeIds.F32:
                sb.Append("float"u8);
                return;
            case TypeIds.F64:
                sb.Append("double"u8);
                return;
            case TypeIds.Str:
                sb.Append("{ ptr, i64 }"u8);
                return;
        }

        TypeInfo ti = types.Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.Enum:
                sb.Append("i32"u8);
                return;
            case TypeKinds.Class:
            case TypeKinds.OptionRef:
            case TypeKinds.OptionMut:
            case TypeKinds.Ref:
            case TypeKinds.RefReadOnly:
                sb.Append("ptr"u8);
                return;
            case TypeKinds.Struct:
                AppendStructTypeName(typeId, in ast, in names, in types, ref sb);
                return;
            case TypeKinds.Option:
                sb.Append("{ i1, "u8);
                AppendValueLlvmType(ti.A, in ast, in names, in types, ref sb);
                sb.Append(" }"u8);
                return;
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
                AppendValueLlvmType(ti.A, in ast, in names, in types, ref sb);
                return;
            case TypeKinds.Result:
                sb.Append("{ i1, "u8);
                AppendValueLlvmType(ti.A, in ast, in names, in types, ref sb);
                sb.Append(", "u8);
                AppendValueLlvmType(ti.B, in ast, in names, in types, ref sb);
                sb.Append(" }"u8);
                return;
            case TypeKinds.Vec:
                sb.Append("{ ptr, i32, i32 }"u8);
                return;
            case TypeKinds.Span:
            case TypeKinds.ReadOnlySpan:
            case TypeKinds.Utf8Lines:
            case TypeKinds.Utf8ArgEnum:
                sb.Append("{ ptr, i32 }"u8);
                return;
            default:
                sb.Append("i32"u8);
                return;
        }
    }

    private static void AppendReturnLlvmType(in FuncInstance fi, in AstArena ast, in Interner names, in TypeTable types, ref StringBuf sb)
    {
        if ((fi.Flags & DeclFlags.ReturnsByRef) != 0)
        {
            sb.Append("ptr"u8);
            return;
        }
        if (fi.ReturnTypeId == TypeIds.Void)
        {
            sb.Append("void"u8);
            return;
        }
        AppendValueLlvmType(fi.ReturnTypeId, in ast, in names, in types, ref sb);
    }

    private static ReadOnlySpan<byte> ZeroLiteralForLlvmType(ReadOnlySpan<byte> llvmTy)
    {
        if (Bytes.Eq(llvmTy, "i1"u8)) return "false"u8;
        if (Bytes.Eq(llvmTy, "i8"u8) || Bytes.Eq(llvmTy, "i16"u8) || Bytes.Eq(llvmTy, "i32"u8) || Bytes.Eq(llvmTy, "i64"u8)) return "0"u8;
        if (Bytes.Eq(llvmTy, "float"u8) || Bytes.Eq(llvmTy, "double"u8)) return "0x0000000000000000"u8;
        if (Bytes.Eq(llvmTy, "ptr"u8)) return "null"u8;
        return "zeroinitializer"u8;
    }

    private static void EmitTypeDefinitions(
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref StringBuf sb)
    {
        if (types.ConcreteUserTypes.Count == 0)
        {
            return;
        }

        sb.Append("; --- Type Definitions ---\n"u8);

        var state = new IntMap<int>(types.ConcreteUserTypes.Count + 8);
        for (int i = 0; i < types.ConcreteUserTypes.Count; i++)
        {
            int tid = types.ConcreteUserTypes[i];
            if (types.Items[tid].Kind == TypeKinds.Struct)
            {
                VisitStructTypeDef(tid, in ast, in names, in types, ref state, ref sb);
            }
        }

        for (int i = 0; i < types.ConcreteUserTypes.Count; i++)
        {
            int tid = types.ConcreteUserTypes[i];
            TypeInfo ti = types.Items[tid];
            if (ti.Kind == TypeKinds.Class)
            {
                for (int f = 0; f < ti.FieldCount; f++)
                {
                    VisitNestedStructTypes(types.Fields[ti.FieldStart + f].TypeId, in ast, in names, in types, ref state, ref sb);
                }

                AppendClassStorageTypeName(tid, in ast, in names, in types, ref sb);
                sb.Append(" = type { "u8);
                if (ti.FieldCount == 0)
                {
                    sb.Append("i8"u8);
                }
                else
                {
                    for (int f = 0; f < ti.FieldCount; f++)
                    {
                        if (f > 0) sb.Append(", "u8);
                        AppendValueLlvmType(types.Fields[ti.FieldStart + f].TypeId, in ast, in names, in types, ref sb);
                    }
                }
                sb.Append(" }\n"u8);
            }
        }

        sb.AppendAscii((byte)'\n');
    }

    private static void VisitStructTypeDef(
        int structTypeId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref IntMap<int> state,
        ref StringBuf sb)
    {
        if (state.Contains(structTypeId))
        {
            return;
        }
        state.Set(structTypeId, 1);

        TypeInfo ti = types.Items[structTypeId];
        for (int f = 0; f < ti.FieldCount; f++)
        {
            VisitNestedStructTypes(types.Fields[ti.FieldStart + f].TypeId, in ast, in names, in types, ref state, ref sb);
        }

        AppendStructTypeName(structTypeId, in ast, in names, in types, ref sb);
        sb.Append(" = type { "u8);
        if (ti.FieldCount == 0)
        {
            sb.Append("i8"u8);
        }
        else
        {
            for (int f = 0; f < ti.FieldCount; f++)
            {
                if (f > 0) sb.Append(", "u8);
                AppendValueLlvmType(types.Fields[ti.FieldStart + f].TypeId, in ast, in names, in types, ref sb);
            }
        }
        sb.Append(" }\n"u8);
    }

    private static void VisitNestedStructTypes(
        int typeId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref IntMap<int> state,
        ref StringBuf sb)
    {
        if (typeId < 0) return;
        TypeInfo ti = types.Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.Struct:
                VisitStructTypeDef(typeId, in ast, in names, in types, ref state, ref sb);
                break;
            case TypeKinds.Option:
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
                VisitNestedStructTypes(ti.A, in ast, in names, in types, ref state, ref sb);
                break;
            case TypeKinds.Result:
                VisitNestedStructTypes(ti.A, in ast, in names, in types, ref state, ref sb);
                VisitNestedStructTypes(ti.B, in ast, in names, in types, ref state, ref sb);
                break;
        }
    }

    private static void EmitPooledGlobals(in EmitCtx ectx, ref StringBuf sb)
    {
        if (ectx.StrEntries.Count == 0 && ectx.Utf8Entries.Count == 0)
        {
            return;
        }

        sb.Append("; --- Constant String & UTF-8 Byte Globals ---\n"u8);
        for (int i = 0; i < ectx.StrEntries.Count; i++)
        {
            ReadOnlySpan<byte> bytes = ectx.StrBytes.Get(ectx.StrEntries[i]);
            sb.Append("@.str."u8);
            sb.AppendInt(i);
            if (bytes.Length == 0)
            {
                sb.Append(" = private unnamed_addr constant [1 x i8] zeroinitializer\n"u8);
            }
            else
            {
                sb.Append(" = private unnamed_addr constant ["u8);
                sb.AppendInt(bytes.Length);
                sb.Append(" x i8] c\""u8);
                AppendEscapedLlvmBytes(bytes, ref sb);
                sb.Append("\"\n"u8);
            }
        }

        for (int i = 0; i < ectx.Utf8Entries.Count; i++)
        {
            ReadOnlySpan<byte> bytes = ectx.Utf8Bytes.Get(ectx.Utf8Entries[i]);
            sb.Append("@.b"u8);
            sb.AppendInt(i);
            sb.Append(" = private unnamed_addr constant ["u8);
            sb.AppendInt(bytes.Length);
            sb.Append(" x i8] c\""u8);
            AppendEscapedLlvmBytes(bytes, ref sb);
            sb.Append("\"\n"u8);
        }

        sb.AppendAscii((byte)'\n');
    }

    private static void AppendEscapedLlvmBytes(ReadOnlySpan<byte> bytes, ref StringBuf sb)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b >= 0x20 && b <= 0x7E && b != (byte)'"' && b != (byte)'\\')
            {
                sb.AppendAscii(b);
            }
            else
            {
                sb.AppendAscii((byte)'\\');
                EmitCtx.AppendHexByteUpper(b, ref sb);
            }
        }
    }

    private static void EmitEntryWrapper(
        in AstArena ast,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref StringBuf sb)
    {
        sb.Append("define internal i32 @__vib_entry() #2 {\nentry:\n"u8);
        if (mir.EntryFuncInstId < 0)
        {
            sb.Append("  ret i32 0\n}\n"u8);
            return;
        }

        FuncInstance entryFi = mir.FuncInstances[mir.EntryFuncInstId];
        var fnSym = new StringBuf(64);
        fnSym.Append("@vib_fn_"u8);
        AppendMangled(mir.GetFuncKey(mir.EntryFuncInstId), ref fnSym);

        if (entryFi.ReturnTypeId == TypeIds.I32 && (entryFi.Flags & DeclFlags.ReturnsByRef) == 0)
        {
            sb.Append("  %code = call i32 "u8);
            sb.Append(fnSym.AsUtf8());
            sb.Append("()\n  ret i32 %code\n}\n"u8);
            return;
        }

        if (entryFi.ReturnTypeId == TypeIds.Void && (entryFi.Flags & DeclFlags.ReturnsByRef) == 0)
        {
            sb.Append("  call void "u8);
            sb.Append(fnSym.AsUtf8());
            sb.Append("()\n  ret i32 0\n}\n"u8);
            return;
        }

        sb.Append("  ret i32 0\n}\n"u8);
    }

    private static void EmitWorklistRootInit(ref StringBuf sb)
    {
        sb.Append("  %inline_buf = alloca [64 x { ptr, ptr }]\n"u8);
        sb.Append("  %wl = alloca { ptr, i64, i64, i1 }\n"u8);
        sb.Append("  %wl_dp = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 0\n"u8);
        sb.Append("  %wl_len = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 1\n"u8);
        sb.Append("  %wl_cap = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 2\n"u8);
        sb.Append("  %wl_hp = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 3\n"u8);
        sb.Append("  store ptr %inline_buf, ptr %wl_dp\n"u8);
        sb.Append("  store i64 0, ptr %wl_len\n"u8);
        sb.Append("  store i64 64, ptr %wl_cap\n"u8);
        sb.Append("  store i1 false, ptr %wl_hp\n"u8);
    }

    private static void EmitDropFieldWithWorklist(
        int fieldTypeId,
        ReadOnlySpan<byte> fieldPtrSsa,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref StringBuf sb)
    {
        if (IsDisposableClassType(fieldTypeId, in ast, in types))
        {
            sb.Append("  call void "u8);
            AppendCleanupSymbol(fieldTypeId, in ast, in names, in types, ref ectx, ref sb);
            sb.Append("(ptr "u8);
            sb.Append(fieldPtrSsa);
            sb.Append(")\n"u8);
        }
        else
        {
            sb.Append("  call void "u8);
            AppendWorklistCleanupSymbol(fieldTypeId, in ast, in names, in types, ref ectx, ref sb);
            sb.Append("(ptr "u8);
            sb.Append(fieldPtrSsa);
            sb.Append(", ptr %wl)\n"u8);
        }
    }

    private static void EmitDropGlueForType(
        int typeId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref StringBuf sb)
    {
        var mangled = new StringBuf(64);
        AppendMangled(types.GetKey(typeId), ref mangled);

        TypeInfo ti = types.Items[typeId];

        // Fast path for Vec<T> where T needs no cleanup
        if (ti.Kind == TypeKinds.Vec && !RequiresCleanup(ti.A, in ast, in types))
        {
            sb.Append("define internal void @__vib_drop_"u8);
            sb.Append(mangled.AsUtf8());
            sb.Append("(ptr %slot) #2 {\nentry:\n"u8);
            sb.Append("  %dp_slot = getelementptr inbounds { ptr, i32, i32 }, ptr %slot, i32 0, i32 0\n"u8);
            sb.Append("  %data = load ptr, ptr %dp_slot\n"u8);
            sb.Append("  %is_null = icmp eq ptr %data, null\n"u8);
            sb.Append("  br i1 %is_null, label %done, label %do_drop\n"u8);
            sb.Append("do_drop:\n"u8);
            sb.Append("  store { ptr, i32, i32 } zeroinitializer, ptr %slot\n"u8);
            sb.Append("  call void @free(ptr %data)\n"u8);
            sb.Append("  br label %done\n"u8);
            sb.Append("done:\n  ret void\n}\n\n"u8);

            sb.Append("define internal void @__vib_drop_wl_"u8);
            sb.Append(mangled.AsUtf8());
            sb.Append("(ptr %slot, ptr %wl) #2 {\nentry:\n  call void @__vib_drop_"u8);
            sb.Append(mangled.AsUtf8());
            sb.Append("(ptr %slot)\n  ret void\n}\n"u8);
            return;
        }

        sb.Append("define internal void @__vib_drop_"u8);
        sb.Append(mangled.AsUtf8());
        sb.Append("(ptr %slot) #2 {\nentry:\n"u8);
        EmitWorklistRootInit(ref sb);
        sb.Append("  call void @__vib_drop_wl_"u8);
        sb.Append(mangled.AsUtf8());
        sb.Append("(ptr %slot, ptr %wl)\n"u8);
        sb.Append("  call void @__vib_wl_drain(ptr %wl)\n"u8);
        sb.Append("  ret void\n}\n\n"u8);

        if (ti.Kind == TypeKinds.Class)
        {
            sb.Append("define internal void @__vib_step_"u8);
            sb.Append(mangled.AsUtf8());
            sb.Append("(ptr %obj, ptr %wl) #2 {\nentry:\n"u8);

            var clsTy = new StringBuf(64);
            AppendClassStorageTypeName(typeId, in ast, in names, in types, ref clsTy);
            for (int f = ti.FieldCount - 1; f >= 0; f--)
            {
                int ft = types.Fields[ti.FieldStart + f].TypeId;
                if (RequiresCleanup(ft, in ast, in types))
                {
                    var fp = new StringBuf(16);
                    fp.Append("%fp_"u8);
                    fp.AppendInt(f);
                    sb.Append("  "u8);
                    sb.Append(fp.AsUtf8());
                    sb.Append(" = getelementptr inbounds "u8);
                    sb.Append(clsTy.AsUtf8());
                    sb.Append(", ptr %obj, i32 0, i32 "u8);
                    sb.AppendInt(f);
                    sb.AppendAscii((byte)'\n');
                    EmitDropFieldWithWorklist(ft, fp.AsUtf8(), in ast, in names, in types, ref ectx, ref sb);
                }
            }
            sb.Append("  call void @free(ptr %obj)\n  ret void\n}\n\n"u8);

            sb.Append("define internal void @__vib_drop_wl_"u8);
            sb.Append(mangled.AsUtf8());
            sb.Append("(ptr %slot, ptr %wl) #2 {\nentry:\n"u8);
            sb.Append("  %obj = load ptr, ptr %slot\n"u8);
            sb.Append("  %is_null = icmp eq ptr %obj, null\n"u8);
            sb.Append("  br i1 %is_null, label %done, label %enqueue\n"u8);
            sb.Append("enqueue:\n  store ptr null, ptr %slot\n"u8);
            sb.Append("  call void @__vib_wl_push(ptr %wl, ptr %obj, ptr @__vib_step_"u8);
            sb.Append(mangled.AsUtf8());
            sb.Append(")\n  br label %done\ndone:\n  ret void\n}\n"u8);
            return;
        }

        sb.Append("define internal void @__vib_drop_wl_"u8);
        sb.Append(mangled.AsUtf8());
        sb.Append("(ptr %slot, ptr %wl) #2 {\nentry:\n"u8);

        switch (ti.Kind)
        {
            case TypeKinds.Vec:
            {
                var elemTy = new StringBuf(64);
                AppendValueLlvmType(ti.A, in ast, in names, in types, ref elemTy);
                sb.Append("  %dp_slot = getelementptr inbounds { ptr, i32, i32 }, ptr %slot, i32 0, i32 0\n"u8);
                sb.Append("  %data = load ptr, ptr %dp_slot\n"u8);
                sb.Append("  %is_null = icmp eq ptr %data, null\n"u8);
                sb.Append("  br i1 %is_null, label %done, label %do_drop\n"u8);
                sb.Append("do_drop:\n"u8);
                sb.Append("  %cnt_slot = getelementptr inbounds { ptr, i32, i32 }, ptr %slot, i32 0, i32 1\n"u8);
                sb.Append("  %cnt = load i32, ptr %cnt_slot\n"u8);
                sb.Append("  store { ptr, i32, i32 } zeroinitializer, ptr %slot\n"u8);
                sb.Append("  %cnt64 = sext i32 %cnt to i64\n"u8);
                sb.Append("  %has_elems = icmp sgt i64 %cnt64, 0\n"u8);
                sb.Append("  br i1 %has_elems, label %elem_loop, label %free_buf\n"u8);
                sb.Append("elem_loop:\n"u8);
                sb.Append("  %idx = phi i64 [ 0, %do_drop ], [ %next_idx, %elem_loop ]\n"u8);
                sb.Append("  %ep = getelementptr inbounds "u8);
                sb.Append(elemTy.AsUtf8());
                sb.Append(", ptr %data, i64 %idx\n"u8);
                EmitDropFieldWithWorklist(ti.A, "%ep"u8, in ast, in names, in types, ref ectx, ref sb);
                sb.Append("  %next_idx = add i64 %idx, 1\n"u8);
                sb.Append("  %more = icmp slt i64 %next_idx, %cnt64\n"u8);
                sb.Append("  br i1 %more, label %elem_loop, label %free_buf\n"u8);
                sb.Append("free_buf:\n  call void @free(ptr %data)\n  br label %done\ndone:\n  ret void\n"u8);
                break;
            }

            case TypeKinds.Option:
            {
                var optTy = new StringBuf(64);
                AppendValueLlvmType(typeId, in ast, in names, in types, ref optTy);
                sb.Append("  %tag_ptr = getelementptr inbounds "u8);
                sb.Append(optTy.AsUtf8());
                sb.Append(", ptr %slot, i32 0, i32 0\n"u8);
                sb.Append("  %tag = load i1, ptr %tag_ptr\n"u8);
                sb.Append("  br i1 %tag, label %drop_some, label %done\n"u8);
                sb.Append("drop_some:\n  store i1 false, ptr %tag_ptr\n"u8);
                if (RequiresCleanup(ti.A, in ast, in types))
                {
                    sb.Append("  %val_ptr = getelementptr inbounds "u8);
                    sb.Append(optTy.AsUtf8());
                    sb.Append(", ptr %slot, i32 0, i32 1\n"u8);
                    EmitDropFieldWithWorklist(ti.A, "%val_ptr"u8, in ast, in names, in types, ref ectx, ref sb);
                }
                sb.Append("  br label %done\ndone:\n  ret void\n"u8);
                break;
            }

            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
            {
                if (RequiresCleanup(ti.A, in ast, in types))
                {
                    EmitDropFieldWithWorklist(ti.A, "%slot"u8, in ast, in names, in types, ref ectx, ref sb);
                }
                sb.Append("  ret void\n"u8);
                break;
            }

            case TypeKinds.Result:
            {
                var resTy = new StringBuf(64);
                AppendValueLlvmType(typeId, in ast, in names, in types, ref resTy);
                sb.Append("  %tag_ptr = getelementptr inbounds "u8);
                sb.Append(resTy.AsUtf8());
                sb.Append(", ptr %slot, i32 0, i32 0\n"u8);
                sb.Append("  %tag = load i1, ptr %tag_ptr\n"u8);
                sb.Append("  br i1 %tag, label %drop_ok, label %drop_err\n"u8);
                sb.Append("drop_ok:\n  store i1 false, ptr %tag_ptr\n"u8);
                if (RequiresCleanup(ti.A, in ast, in types))
                {
                    sb.Append("  %ok_ptr = getelementptr inbounds "u8);
                    sb.Append(resTy.AsUtf8());
                    sb.Append(", ptr %slot, i32 0, i32 1\n"u8);
                    EmitDropFieldWithWorklist(ti.A, "%ok_ptr"u8, in ast, in names, in types, ref ectx, ref sb);
                }
                sb.Append("  br label %done\ndrop_err:\n"u8);
                if (RequiresCleanup(ti.B, in ast, in types))
                {
                    sb.Append("  %err_ptr = getelementptr inbounds "u8);
                    sb.Append(resTy.AsUtf8());
                    sb.Append(", ptr %slot, i32 0, i32 2\n"u8);
                    EmitDropFieldWithWorklist(ti.B, "%err_ptr"u8, in ast, in names, in types, ref ectx, ref sb);
                }
                sb.Append("  br label %done\ndone:\n  ret void\n"u8);
                break;
            }

            case TypeKinds.Struct:
            {
                var stTy = new StringBuf(64);
                AppendStructTypeName(typeId, in ast, in names, in types, ref stTy);
                for (int f = ti.FieldCount - 1; f >= 0; f--)
                {
                    int ft = types.Fields[ti.FieldStart + f].TypeId;
                    if (RequiresCleanup(ft, in ast, in types))
                    {
                        var fp = new StringBuf(16);
                        fp.Append("%fp_"u8);
                        fp.AppendInt(f);
                        sb.Append("  "u8);
                        sb.Append(fp.AsUtf8());
                        sb.Append(" = getelementptr inbounds "u8);
                        sb.Append(stTy.AsUtf8());
                        sb.Append(", ptr %slot, i32 0, i32 "u8);
                        sb.AppendInt(f);
                        sb.AppendAscii((byte)'\n');
                        EmitDropFieldWithWorklist(ft, fp.AsUtf8(), in ast, in names, in types, ref ectx, ref sb);
                    }
                }
                sb.Append("  ret void\n"u8);
                break;
            }

            default:
                sb.Append("  ret void\n"u8);
                break;
        }

        sb.Append("}\n"u8);
    }

    private static void EmitLinearFreeGlue(
        int classTypeId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref StringBuf sb)
    {
        var raw = new StringBuf(64);
        AppendUserTypeIdent(classTypeId, in ast, in names, in types, ref raw);
        sb.Append("define internal void @__vib_free_linear_"u8);
        AppendMangled(raw.AsUtf8(), ref sb);
        sb.Append("(ptr %slot) #2 {\nentry:\n"u8);
        EmitWorklistRootInit(ref sb);
        sb.Append("  %obj = load ptr, ptr %slot\n"u8);
        sb.Append("  %is_null = icmp eq ptr %obj, null\n"u8);
        sb.Append("  br i1 %is_null, label %done, label %do_free\n"u8);
        sb.Append("do_free:\n  store ptr null, ptr %slot\n"u8);

        TypeInfo ti = types.Items[classTypeId];
        var clsTy = new StringBuf(64);
        AppendClassStorageTypeName(classTypeId, in ast, in names, in types, ref clsTy);
        for (int f = ti.FieldCount - 1; f >= 0; f--)
        {
            int ft = types.Fields[ti.FieldStart + f].TypeId;
            if (RequiresCleanup(ft, in ast, in types))
            {
                var fp = new StringBuf(16);
                fp.Append("%fp_"u8);
                fp.AppendInt(f);
                sb.Append("  "u8);
                sb.Append(fp.AsUtf8());
                sb.Append(" = getelementptr inbounds "u8);
                sb.Append(clsTy.AsUtf8());
                sb.Append(", ptr %obj, i32 0, i32 "u8);
                sb.AppendInt(f);
                sb.AppendAscii((byte)'\n');
                EmitDropFieldWithWorklist(ft, fp.AsUtf8(), in ast, in names, in types, ref ectx, ref sb);
            }
        }

        sb.Append("  call void @free(ptr %obj)\n"u8);
        sb.Append("  call void @__vib_wl_drain(ptr %wl)\n"u8);
        sb.Append("  br label %done\ndone:\n  ret void\n}\n"u8);
    }

    private static void AppendAbiParamList(
        in FuncInstance fi,
        bool includeNames,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref StringBuf sb)
    {
        bool first = true;
        for (int i = 0; i < fi.ParamSigCount; i++)
        {
            MirParamSig p = mir.ParamSigs[fi.ParamSigStart + i];
            TypeInfo pti = types.Items[p.TypeId];

            if (p.Mode == PassMode.ByValue && (pti.Kind == TypeKinds.Span || pti.Kind == TypeKinds.ReadOnlySpan))
            {
                if (!first) sb.Append(", "u8);
                first = false;
                bool isRo = pti.Kind == TypeKinds.ReadOnlySpan;
                sb.Append(isRo ? "ptr readonly"u8 : "ptr noalias"u8);
                if (includeNames)
                {
                    sb.Append(" %arg_"u8);
                    sb.AppendInt(i);
                    sb.Append("_ptr"u8);
                }
                sb.Append(", i32"u8);
                if (includeNames)
                {
                    sb.Append(" %arg_"u8);
                    sb.AppendInt(i);
                    sb.Append("_len"u8);
                }
                continue;
            }

            if (!first) sb.Append(", "u8);
            first = false;

            if (p.Mode == PassMode.ByValue)
            {
                AppendValueLlvmType(p.TypeId, in ast, in names, in types, ref sb);
                if (pti.Kind == TypeKinds.Class || pti.Kind == TypeKinds.OptionMut || pti.Kind == TypeKinds.Ref)
                {
                    sb.Append(" noalias nonnull"u8);
                }
                else if (pti.Kind == TypeKinds.OptionRef || pti.Kind == TypeKinds.RefReadOnly)
                {
                    sb.Append(" readonly nonnull"u8);
                }
            }
            else if (p.Mode == PassMode.ThisShared || p.Mode == PassMode.In)
            {
                sb.Append("ptr readonly nonnull"u8);
            }
            else
            {
                sb.Append("ptr noalias nonnull"u8);
            }

            if (includeNames)
            {
                sb.Append(" %arg_"u8);
                sb.AppendInt(i);
            }
        }
    }

    private struct FnEmitState
    {
        public Vec<LocalSlot> Slots;
        public Vec<StackSpanSlot> StackSpans;
        public BytePool Scratch;
        public StringBuf ExtraAllocas;
        public int TempCounter;
        public int LabelCounter;
        public int ExtraAllocaCounter;

        public FnEmitState(int localCount, int stackSpanCount)
        {
            Slots = new Vec<LocalSlot>(localCount);
            StackSpans = new Vec<StackSpanSlot>(stackSpanCount);
            Scratch = new BytePool(4096);
            ExtraAllocas = new StringBuf(256);
            TempCounter = 0;
            LabelCounter = 0;
            ExtraAllocaCounter = 0;
        }

        [UnscopedRef]
        public readonly ReadOnlySpan<byte> Text(ByteSlice s) => Scratch.Get(s);

        public ByteSlice InternSsa(ReadOnlySpan<byte> s) => Scratch.Add(s);

        public ByteSlice NextTemp()
        {
            var sb = new StringBuf(16);
            sb.Append("%t"u8);
            sb.AppendInt(TempCounter);
            TempCounter += 1;
            return Scratch.Add(sb.AsUtf8());
        }

        public ByteSlice NextLabel(ReadOnlySpan<byte> prefix)
        {
            var sb = new StringBuf(32);
            sb.Append(prefix);
            sb.AppendAscii((byte)'_');
            sb.AppendInt(LabelCounter);
            LabelCounter += 1;
            return Scratch.Add(sb.AsUtf8());
        }

        public ByteSlice AllocTempSlot(ReadOnlySpan<byte> llvmTy)
        {
            var slot = new StringBuf(24);
            slot.Append("%tmp_slot_"u8);
            slot.AppendInt(ExtraAllocaCounter);
            ExtraAllocaCounter += 1;

            ExtraAllocas.Append("  "u8);
            ExtraAllocas.Append(slot.AsUtf8());
            ExtraAllocas.Append(" = alloca "u8);
            ExtraAllocas.Append(llvmTy);
            ExtraAllocas.AppendAscii((byte)'\n');

            return Scratch.Add(slot.AsUtf8());
        }

        public readonly void EmitLabel(ByteSlice lbl, ref StringBuf body)
        {
            body.Append(Scratch.Get(lbl));
            body.Append(":\n"u8);
        }

        public static void EmitBlockLabel(int blkId, ref StringBuf body)
        {
            body.Append("bb_"u8);
            body.AppendInt(blkId);
            body.Append(":\n"u8);
        }
    }

    private static ByteSlice LocalAllocaSsa(int localId, ref FnEmitState fs)
    {
        var sb = new StringBuf(16);
        sb.Append("%loc_"u8);
        sb.AppendInt(localId);
        return fs.InternSsa(sb.AsUtf8());
    }

    private static ByteSlice LocalDropFlagSsa(int localId, ref FnEmitState fs)
    {
        var sb = new StringBuf(16);
        sb.Append("%drop_"u8);
        sb.AppendInt(localId);
        return fs.InternSsa(sb.AsUtf8());
    }

    private static ByteSlice TypeToSlice(int typeId, in AstArena ast, in Interner names, in TypeTable types, ref FnEmitState fs)
    {
        var sb = new StringBuf(64);
        AppendValueLlvmType(typeId, in ast, in names, in types, ref sb);
        return fs.InternSsa(sb.AsUtf8());
    }

    private static void EmitFunction(
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref StringBuf outIr)
    {
        FuncInstance fi = mir.FuncInstances[fn.FuncInstId];
        var fs = new FnEmitState(fn.LocalCount, fn.StackSpanCount);
        var body = new StringBuf(4096);

        for (int i = 0; i < fn.LocalCount; i++)
        {
            MirLocal loc = mir.Locals[fn.LocalStart + i];
            int unwrapped = loc.TypeId;
            TypeInfo lti = types.Items[loc.TypeId];
            if (lti.Kind == TypeKinds.Ref || lti.Kind == TypeKinds.RefReadOnly)
            {
                unwrapped = lti.A;
            }

            bool isParam = (loc.Flags & MirLocalFlags.IsParam) != 0;
            bool isIndirectParam = isParam &&
                (loc.Mode == PassMode.In || loc.Mode == PassMode.Ref || loc.Mode == PassMode.Out ||
                 ((loc.Mode == PassMode.ThisShared || loc.Mode == PassMode.ThisMut) &&
                  types.Items[unwrapped].Kind != TypeKinds.Class));

            bool isRefSlot = (loc.Flags & MirLocalFlags.IsRefLocal) != 0 ||
                lti.Kind == TypeKinds.Ref ||
                lti.Kind == TypeKinds.RefReadOnly ||
                isIndirectParam;

            int storageOrRefType = isRefSlot ? unwrapped : loc.TypeId;
            bool hasDrop = !isRefSlot && (loc.Flags & MirLocalFlags.IsBorrowedBind) == 0 && RequiresCleanup(loc.TypeId, in ast, in types);
            fs.Slots.Push(new LocalSlot(isRefSlot, storageOrRefType, hasDrop));
        }

        for (int s = 0; s < fn.StackSpanCount; s++)
        {
            fs.StackSpans.Push(new StackSpanSlot(TypeIds.U8, 1));
        }

        for (int b = 0; b < fn.BlockCount; b++)
        {
            MirBlock blk = mir.Blocks[fn.BlockStart + b];
            for (int k = 0; k < blk.InstCount; k++)
            {
                MirInst inst = mir.Insts[blk.InstStart + k];
                if (inst.Op == MirOp.StackSpan && inst.C >= 0 && inst.C < fs.StackSpans.Count)
                {
                    int elemT = types.Items[inst.TypeId].A;
                    int cnt = inst.B > 0 ? inst.B : 1;
                    fs.StackSpans[inst.C] = new StackSpanSlot(elemT, cnt);
                }
            }
        }

        for (int b = 0; b < fn.BlockCount; b++)
        {
            MirBlock blk = mir.Blocks[fn.BlockStart + b];
            FnEmitState.EmitBlockLabel(b, ref body);
            for (int k = 0; k < blk.InstCount; k++)
            {
                MirInst inst = mir.Insts[blk.InstStart + k];
                EmitInst(in inst, in fn, in fi, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            }
            EmitTerminator(in blk.Term, in fn, in fi, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        }

        outIr.Append("define "u8);
        if ((fi.Flags & DeclFlags.IsLocalFunc) != 0)
        {
            outIr.Append("internal "u8);
        }
        AppendReturnLlvmType(in fi, in ast, in names, in types, ref outIr);
        outIr.AppendAscii((byte)' ');
        AppendFuncSymbol(fn.FuncInstId, in ta, in mir, ref ectx, ref outIr);
        outIr.AppendAscii((byte)'(');
        AppendAbiParamList(in fi, includeNames: true, in ast, in names, in types, in mir, ref outIr);
        outIr.Append(") #2 {\nentry:\n"u8);

        // Local allocas + drop flags
        for (int i = 0; i < fn.LocalCount; i++)
        {
            LocalSlot sl = fs.Slots[i];
            outIr.Append("  %loc_"u8);
            outIr.AppendInt(i);
            outIr.Append(" = alloca "u8);
            if (sl.IsRefSlot)
            {
                outIr.Append("ptr"u8);
            }
            else
            {
                AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref outIr);
            }
            outIr.AppendAscii((byte)'\n');

            if (sl.HasDropFlag)
            {
                outIr.Append("  %drop_"u8);
                outIr.AppendInt(i);
                outIr.Append(" = alloca i1\n"u8);
            }
        }

        // StackSpan allocas
        for (int s = 0; s < fs.StackSpans.Count; s++)
        {
            StackSpanSlot ss = fs.StackSpans[s];
            outIr.Append("  %stackspan_"u8);
            outIr.AppendInt(s);
            outIr.Append(" = alloca ["u8);
            outIr.AppendInt(ss.Count);
            outIr.Append(" x "u8);
            AppendValueLlvmType(ss.ElemTypeId, in ast, in names, in types, ref outIr);
            outIr.Append("]\n"u8);
        }

        if (fs.ExtraAllocas.Length > 0)
        {
            outIr.Append(fs.ExtraAllocas.AsUtf8());
        }

        // Initialize parameter locals vs non-parameter locals
        var paramInited = new Vec<byte>(fn.LocalCount);
        paramInited.Resize(fn.LocalCount, 0);

        int pOrd = 0;
        for (int i = 0; i < fn.LocalCount && pOrd < fi.ParamSigCount; i++)
        {
            MirLocal loc = mir.Locals[fn.LocalStart + i];
            if ((loc.Flags & MirLocalFlags.IsParam) == 0) continue;

            MirParamSig pSig = mir.ParamSigs[fi.ParamSigStart + pOrd];
            LocalSlot sl = fs.Slots[i];
            paramInited[i] = 1;
            TypeInfo pti = types.Items[pSig.TypeId];

            if (pSig.Mode == PassMode.ByValue && (pti.Kind == TypeKinds.Span || pti.Kind == TypeKinds.ReadOnlySpan))
            {
                outIr.Append("  %span_init0_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append(" = insertvalue { ptr, i32 } poison, ptr %arg_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append("_ptr, 0\n  %span_init1_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append(" = insertvalue { ptr, i32 } %span_init0_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append(", i32 %arg_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append("_len, 1\n  store { ptr, i32 } %span_init1_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append(", ptr %loc_"u8);
                outIr.AppendInt(i);
                outIr.AppendAscii((byte)'\n');
            }
            else if (sl.IsRefSlot)
            {
                outIr.Append("  store ptr %arg_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append(", ptr %loc_"u8);
                outIr.AppendInt(i);
                outIr.AppendAscii((byte)'\n');
            }
            else
            {
                outIr.Append("  store "u8);
                AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref outIr);
                outIr.Append(" %arg_"u8);
                outIr.AppendInt(pOrd);
                outIr.Append(", ptr %loc_"u8);
                outIr.AppendInt(i);
                outIr.AppendAscii((byte)'\n');

                if (sl.HasDropFlag)
                {
                    outIr.Append("  store i1 "u8);
                    outIr.Append(pSig.Mode == PassMode.ByValue ? "true"u8 : "false"u8);
                    outIr.Append(", ptr %drop_"u8);
                    outIr.AppendInt(i);
                    outIr.AppendAscii((byte)'\n');
                }
            }

            pOrd += 1;
        }

        for (int i = 0; i < fn.LocalCount; i++)
        {
            if (paramInited[i] != 0) continue;
            LocalSlot sl = fs.Slots[i];
            if (sl.IsRefSlot)
            {
                outIr.Append("  store ptr null, ptr %loc_"u8);
                outIr.AppendInt(i);
                outIr.AppendAscii((byte)'\n');
            }
            else
            {
                var stTy = new StringBuf(64);
                AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref stTy);
                outIr.Append("  store "u8);
                outIr.Append(stTy.AsUtf8());
                outIr.AppendAscii((byte)' ');
                outIr.Append(ZeroLiteralForLlvmType(stTy.AsUtf8()));
                outIr.Append(", ptr %loc_"u8);
                outIr.AppendInt(i);
                outIr.AppendAscii((byte)'\n');

                if (sl.HasDropFlag)
                {
                    outIr.Append("  store i1 false, ptr %drop_"u8);
                    outIr.AppendInt(i);
                    outIr.AppendAscii((byte)'\n');
                }
            }
        }

        if (fn.BlockCount > 0)
        {
            outIr.Append("  br label %bb_0\n"u8);
            outIr.Append(body.AsUtf8());
        }
        else
        {
            var retTy = new StringBuf(64);
            AppendReturnLlvmType(in fi, in ast, in names, in types, ref retTy);
            if (Bytes.Eq(retTy.AsUtf8(), "void"u8))
            {
                outIr.Append("  ret void\n"u8);
            }
            else
            {
                outIr.Append("  ret "u8);
                outIr.Append(retTy.AsUtf8());
                outIr.AppendAscii((byte)' ');
                outIr.Append(ZeroLiteralForLlvmType(retTy.AsUtf8()));
                outIr.AppendAscii((byte)'\n');
            }
        }

        outIr.Append("}\n"u8);
    }

    private static void EmitDropLocalIfLive(
        int localId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        LocalSlot sl = fs.Slots[localId];
        if (!sl.HasDropFlag)
        {
            return;
        }

        ByteSlice liveFlag = fs.NextTemp();
        ByteSlice doDropLbl = fs.NextLabel("drop_live"u8);
        ByteSlice contLbl = fs.NextLabel("drop_cont"u8);

        body.Append("  "u8);
        body.Append(fs.Text(liveFlag));
        body.Append(" = load i1, ptr %drop_"u8);
        body.AppendInt(localId);
        body.Append("\n  br i1 "u8);
        body.Append(fs.Text(liveFlag));
        body.Append(", label %"u8);
        body.Append(fs.Text(doDropLbl));
        body.Append(", label %"u8);
        body.Append(fs.Text(contLbl));
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(doDropLbl, ref body);
        body.Append("  store i1 false, ptr %drop_"u8);
        body.AppendInt(localId);
        body.Append("\n  call void "u8);
        AppendCleanupSymbol(sl.StorageOrRefTypeId, in ast, in names, in types, ref ectx, ref body);
        body.Append("(ptr %loc_"u8);
        body.AppendInt(localId);
        body.Append(")\n"u8);
        var zeroTy = new StringBuf(64);
        AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref zeroTy);
        body.Append("  store "u8);
        body.Append(zeroTy.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(ZeroLiteralForLlvmType(zeroTy.AsUtf8()));
        body.Append(", ptr %loc_"u8);
        body.AppendInt(localId);
        body.Append("\n  br label %"u8);
        body.Append(fs.Text(contLbl));
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(contLbl, ref body);
    }

    private static void StoreIntoDirectLocalWithOverwriteDrop(
        int localId,
        ByteSlice ssaVal,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        LocalSlot sl = fs.Slots[localId];
        if (sl.HasDropFlag)
        {
            EmitDropLocalIfLive(localId, in ast, in names, in types, ref ectx, ref fs, ref body);
        }

        body.Append("  store "u8);
        AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref body);
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(ssaVal));
        body.Append(", ptr %loc_"u8);
        body.AppendInt(localId);
        body.AppendAscii((byte)'\n');

        if (sl.HasDropFlag)
        {
            body.Append("  store i1 true, ptr %drop_"u8);
            body.AppendInt(localId);
            body.AppendAscii((byte)'\n');
        }
    }

    private static void StoreIntoPlaceWithOverwriteDrop(
        int placeId,
        ByteSlice ssaVal,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        MirPlace pl = mir.Places[placeId];
        if (pl.ProjCount == 0 && !fs.Slots[pl.LocalId].IsRefSlot)
        {
            StoreIntoDirectLocalWithOverwriteDrop(pl.LocalId, ssaVal, in ast, in names, in types, ref ectx, ref fs, ref body);
            return;
        }

        ByteSlice dstPtr = EmitPlaceAddress(placeId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int dstTypeId);
        if (RequiresCleanup(dstTypeId, in ast, in types))
        {
            body.Append("  call void "u8);
            AppendCleanupSymbol(dstTypeId, in ast, in names, in types, ref ectx, ref body);
            body.Append("(ptr "u8);
            body.Append(fs.Text(dstPtr));
            body.Append(")\n"u8);
        }

        body.Append("  store "u8);
        AppendValueLlvmType(dstTypeId, in ast, in names, in types, ref body);
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(ssaVal));
        body.Append(", ptr "u8);
        body.Append(fs.Text(dstPtr));
        body.AppendAscii((byte)'\n');
    }

    private static void SpillAndDropValueIfNeeded(
        int typeId,
        ByteSlice ssaVal,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        if (!RequiresCleanup(typeId, in ast, in types))
        {
            return;
        }

        var llvmTy = new StringBuf(64);
        AppendValueLlvmType(typeId, in ast, in names, in types, ref llvmTy);
        ByteSlice slot = fs.AllocTempSlot(llvmTy.AsUtf8());

        body.Append("  store "u8);
        body.Append(llvmTy.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(ssaVal));
        body.Append(", ptr "u8);
        body.Append(fs.Text(slot));
        body.Append("\n  call void "u8);
        AppendCleanupSymbol(typeId, in ast, in names, in types, ref ectx, ref body);
        body.Append("(ptr "u8);
        body.Append(fs.Text(slot));
        body.Append(")\n"u8);
    }

    private static void StoreRefResult(
        int refDestLocalId,
        int destPlaceId,
        ByteSlice ptrVal,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        if (refDestLocalId >= 0)
        {
            body.Append("  store ptr "u8);
            body.Append(fs.Text(ptrVal));
            body.Append(", ptr %loc_"u8);
            body.AppendInt(refDestLocalId);
            body.AppendAscii((byte)'\n');
            return;
        }

        if (destPlaceId >= 0)
        {
            MirPlace pl = mir.Places[destPlaceId];
            if (pl.ProjCount == 0 && fs.Slots[pl.LocalId].IsRefSlot)
            {
                body.Append("  store ptr "u8);
                body.Append(fs.Text(ptrVal));
                body.Append(", ptr %loc_"u8);
                body.AppendInt(pl.LocalId);
                body.AppendAscii((byte)'\n');
            }
            else
            {
                ByteSlice dstAddr = EmitPlaceAddress(destPlaceId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                body.Append("  store ptr "u8);
                body.Append(fs.Text(ptrVal));
                body.Append(", ptr "u8);
                body.Append(fs.Text(dstAddr));
                body.AppendAscii((byte)'\n');
            }
        }
    }

    private static void EmitInst(
        in MirInst inst,
        in MirFunc fn,
        in FuncInstance fi,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        _ = fi;
        switch (inst.Op)
        {
            case MirOp.AssignUse:
            case MirOp.AssignBin:
            case MirOp.AssignUn:
            case MirOp.AssignCast:
            case MirOp.AllocClass:
            case MirOp.ConstructStruct:
            case MirOp.MakeSomeWrap:
            case MirOp.MakeOkWrap:
            case MirOp.MakeErrWrap:
            case MirOp.LiftOptionSome:
            case MirOp.LiftOptionNone:
            case MirOp.LiftResultOk:
            case MirOp.LiftResultErr:
            case MirOp.UnwrapWrap:
            case MirOp.StackSpan:
                EmitAssignRvalue(in inst, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirOp.BindRef:
            {
                ByteSlice targetPtr = EmitPlaceAddress(inst.B, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                body.Append("  store ptr "u8);
                body.Append(fs.Text(targetPtr));
                body.Append(", ptr %loc_"u8);
                body.AppendInt(inst.A);
                body.AppendAscii((byte)'\n');
                break;
            }

            case MirOp.Call:
                EmitCallInst(in inst, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirOp.Intrinsic:
                EmitIntrinsicInst(in inst, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirOp.DisposeResource:
                EmitDisposeResourceInst(in inst, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirOp.ScopeExit:
            {
                // Drop exiting locals in reverse declaration order (LIFO)
                for (int i = inst.B - 1; i >= 0; i--)
                {
                    int lid = mir.ExtraLists[inst.A + i];
                    if (lid >= 0 && lid < fs.Slots.Count && fs.Slots[lid].HasDropFlag)
                    {
                        EmitDropLocalIfLive(lid, in ast, in names, in types, ref ectx, ref fs, ref body);
                    }
                }
                break;
            }
        }
    }

    private static void EmitAssignRvalue(
        in MirInst inst,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        MirPlace dst = mir.Places[inst.DestPlaceId];
        TypeInfo rti = types.Items[inst.TypeId];

        if (dst.ProjCount == 0 && fs.Slots[dst.LocalId].IsRefSlot &&
            (rti.Kind == TypeKinds.Ref || rti.Kind == TypeKinds.RefReadOnly))
        {
            ByteSlice refPtr = EmitRvalueValue(in inst, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            body.Append("  store ptr "u8);
            body.Append(fs.Text(refPtr));
            body.Append(", ptr %loc_"u8);
            body.AppendInt(dst.LocalId);
            body.AppendAscii((byte)'\n');
            return;
        }

        if (dst.ProjCount == 0 && !fs.Slots[dst.LocalId].IsRefSlot)
        {
            // Evaluate RHS before overwriting/dropping LHS so self-moves (`x = F(x)`) are safe!
            ByteSlice rhsVal = EmitRvalueValue(in inst, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            StoreIntoDirectLocalWithOverwriteDrop(dst.LocalId, rhsVal, in ast, in names, in types, ref ectx, ref fs, ref body);
            return;
        }

        ByteSlice dstPtr = EmitPlaceAddress(inst.DestPlaceId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int dstTypeId);
        ByteSlice projVal = EmitRvalueValue(in inst, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        if (RequiresCleanup(dstTypeId, in ast, in types))
        {
            body.Append("  call void "u8);
            AppendCleanupSymbol(dstTypeId, in ast, in names, in types, ref ectx, ref body);
            body.Append("(ptr "u8);
            body.Append(fs.Text(dstPtr));
            body.Append(")\n"u8);
        }

        body.Append("  store "u8);
        AppendValueLlvmType(dstTypeId, in ast, in names, in types, ref body);
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(projVal));
        body.Append(", ptr "u8);
        body.Append(fs.Text(dstPtr));
        body.AppendAscii((byte)'\n');
    }

    private static ByteSlice EmitRvalueValue(
        in MirInst inst,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        switch (inst.Op)
        {
            case MirOp.AssignUse:
            case MirOp.MakeSomeWrap:
            case MirOp.MakeOkWrap:
            case MirOp.MakeErrWrap:
            case MirOp.UnwrapWrap:
                return EmitOperand(inst.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

            case MirOp.LiftOptionNone:
                return fs.InternSsa("zeroinitializer"u8);

            case MirOp.LiftOptionSome:
            {
                int innerT = inst.TypeId;
                var innerLlvm = new StringBuf(64);
                AppendValueLlvmType(innerT, in ast, in names, in types, ref innerLlvm);
                var optLlvm = new StringBuf(72);
                optLlvm.Append("{ i1, "u8);
                optLlvm.Append(innerLlvm.AsUtf8());
                optLlvm.Append(" }"u8);

                ByteSlice val = EmitOperand(inst.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice o0 = fs.NextTemp();
                ByteSlice o1 = fs.NextTemp();

                body.Append("  "u8);
                body.Append(fs.Text(o0));
                body.Append(" = insertvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(" zeroinitializer, i1 true, 0\n  "u8);
                body.Append(fs.Text(o1));
                body.Append(" = insertvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(o0));
                body.Append(", "u8);
                body.Append(innerLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(val));
                body.Append(", 1\n"u8);
                return o1;
            }

            case MirOp.LiftResultOk:
            case MirOp.LiftResultErr:
            {
                var resLlvm = new StringBuf(96);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref resLlvm);
                TypeInfo rti = types.Items[inst.TypeId];
                bool isOk = inst.Op == MirOp.LiftResultOk;
                int payloadT = isOk ? rti.A : rti.B;
                var payloadLlvm = new StringBuf(64);
                AppendValueLlvmType(payloadT, in ast, in names, in types, ref payloadLlvm);

                ByteSlice val = EmitOperand(inst.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice r0 = fs.NextTemp();
                ByteSlice r1 = fs.NextTemp();

                body.Append("  "u8);
                body.Append(fs.Text(r0));
                body.Append(" = insertvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.Append(isOk ? " zeroinitializer, i1 true, 0\n  "u8 : " zeroinitializer, i1 false, 0\n  "u8);
                body.Append(fs.Text(r1));
                body.Append(" = insertvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(r0));
                body.Append(", "u8);
                body.Append(payloadLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(val));
                body.Append(isOk ? ", 1\n"u8 : ", 2\n"u8);
                return r1;
            }

            case MirOp.AllocClass:
            {
                TypeInfo cti = types.Items[inst.TypeId];
                var evalVals = new Vec<ByteSlice>(inst.B);
                for (int i = 0; i < inst.B; i++)
                {
                    evalVals.Push(EmitOperand(mir.ExtraLists[inst.A + i], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body));
                }

                var clsTy = new StringBuf(64);
                AppendClassStorageTypeName(inst.TypeId, in ast, in names, in types, ref clsTy);
                ByteSlice rawBytes = EmitSizeOf(clsTy.AsUtf8(), ref fs, ref body);
                ByteSlice clampedBytes = fs.NextTemp();
                EmitClampMinOne(rawBytes, clampedBytes, ref fs, ref body);
                ByteSlice objPtr = EmitCheckedMalloc(clampedBytes, ref ectx, ref fs, ref body);

                body.Append("  call void @llvm.memset.p0.i64(ptr "u8);
                body.Append(fs.Text(objPtr));
                body.Append(", i8 0, i64 "u8);
                body.Append(fs.Text(clampedBytes));
                body.Append(", i1 false)\n"u8);

                for (int i = 0; i < inst.B; i++)
                {
                    int fType = i < cti.FieldCount
                        ? types.Fields[cti.FieldStart + i].TypeId
                        : mir.Operands[mir.ExtraLists[inst.A + i]].TypeId;
                    ByteSlice fp = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(fp));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(clsTy.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(objPtr));
                    body.Append(", i32 0, i32 "u8);
                    body.AppendInt(i);
                    body.Append("\n  store "u8);
                    AppendValueLlvmType(fType, in ast, in names, in types, ref body);
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(evalVals[i]));
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(fp));
                    body.AppendAscii((byte)'\n');
                }

                return objPtr;
            }

            case MirOp.ConstructStruct:
            {
                TypeInfo sti = types.Items[inst.TypeId];
                var evalVals = new Vec<ByteSlice>(inst.B);
                for (int i = 0; i < inst.B; i++)
                {
                    evalVals.Push(EmitOperand(mir.ExtraLists[inst.A + i], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body));
                }

                var stTy = new StringBuf(64);
                AppendStructTypeName(inst.TypeId, in ast, in names, in types, ref stTy);
                if (inst.B == 0)
                {
                    ByteSlice zeroed = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(zeroed));
                    body.Append(" = insertvalue "u8);
                    body.Append(stTy.AsUtf8());
                    body.Append(" zeroinitializer, i8 0, 0\n"u8);
                    return zeroed;
                }

                ByteSlice cur = fs.InternSsa("zeroinitializer"u8);
                for (int i = 0; i < inst.B; i++)
                {
                    int fType = i < sti.FieldCount
                        ? types.Fields[sti.FieldStart + i].TypeId
                        : mir.Operands[mir.ExtraLists[inst.A + i]].TypeId;
                    ByteSlice next = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(next));
                    body.Append(" = insertvalue "u8);
                    body.Append(stTy.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(cur));
                    body.Append(", "u8);
                    AppendValueLlvmType(fType, in ast, in names, in types, ref body);
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(evalVals[i]));
                    body.Append(", "u8);
                    body.AppendInt(i);
                    body.AppendAscii((byte)'\n');
                    cur = next;
                }
                return cur;
            }

            case MirOp.StackSpan:
            {
                if (inst.B <= 0)
                {
                    return fs.InternSsa("zeroinitializer"u8);
                }

                int elemT = types.Items[inst.TypeId].A;
                var elemTy = new StringBuf(64);
                AppendValueLlvmType(elemT, in ast, in names, in types, ref elemTy);

                var vals = new Vec<ByteSlice>(inst.B);
                for (int i = 0; i < inst.B; i++)
                {
                    vals.Push(EmitOperand(mir.ExtraLists[inst.A + i], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body));
                }

                var arrName = new StringBuf(24);
                arrName.Append("%stackspan_"u8);
                arrName.AppendInt(inst.C);

                for (int i = 0; i < inst.B; i++)
                {
                    ByteSlice ep = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(ep));
                    body.Append(" = getelementptr inbounds ["u8);
                    body.AppendInt(inst.B);
                    body.Append(" x "u8);
                    body.Append(elemTy.AsUtf8());
                    body.Append("], ptr "u8);
                    body.Append(arrName.AsUtf8());
                    body.Append(", i64 0, i64 "u8);
                    body.AppendInt(i);
                    body.Append("\n  store "u8);
                    body.Append(elemTy.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(vals[i]));
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(ep));
                    body.AppendAscii((byte)'\n');
                }

                ByteSlice s0 = fs.NextTemp();
                ByteSlice s1 = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(s0));
                body.Append(" = insertvalue { ptr, i32 } poison, ptr "u8);
                body.Append(arrName.AsUtf8());
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(s1));
                body.Append(" = insertvalue { ptr, i32 } "u8);
                body.Append(fs.Text(s0));
                body.Append(", i32 "u8);
                body.AppendInt(inst.B);
                body.Append(", 1\n"u8);
                return s1;
            }

            case MirOp.AssignBin:
                return EmitBinaryValue((MirBinOp)inst.A, inst.B, inst.C, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

            case MirOp.AssignUn:
                return EmitUnaryValue((MirUnOp)inst.A, inst.B, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

            case MirOp.AssignCast:
                return EmitCastValue(inst.A, inst.TypeId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

            default:
            {
                var defTy = new StringBuf(64);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref defTy);
                return fs.InternSsa(ZeroLiteralForLlvmType(defTy.AsUtf8()));
            }
        }
    }

    private static ByteSlice EmitBinaryValue(
        MirBinOp op,
        int leftOpId,
        int rightOpId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        int lhsType = mir.Operands[leftOpId].TypeId;
        int rhsType = mir.Operands[rightOpId].TypeId;
        ByteSlice lhs = EmitOperand(leftOpId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        ByteSlice rhs = EmitOperand(rightOpId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

        if (lhsType == TypeIds.NoneUnit)
        {
            return fs.InternSsa(op == MirBinOp.Ne ? "false"u8 : "true"u8);
        }

        var lhsLlvm = new StringBuf(32);
        AppendValueLlvmType(lhsType, in ast, in names, in types, ref lhsLlvm);

        if (lhsType == TypeIds.F32 || lhsType == TypeIds.F64)
        {
            ByteSlice res = fs.NextTemp();
            if (op == MirBinOp.Rem)
            {
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = call "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.Append(lhsType == TypeIds.F32 ? " @__vib_rem_f32("u8 : " @__vib_rem_f64("u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(rhs));
                body.Append(")\n"u8);
                return res;
            }
            ReadOnlySpan<byte> fop = op switch
            {
                MirBinOp.Add => "fadd"u8,
                MirBinOp.Sub => "fsub"u8,
                MirBinOp.Mul => "fmul"u8,
                MirBinOp.Div => "fdiv"u8,
                MirBinOp.Eq => "fcmp oeq"u8,
                MirBinOp.Ne => "fcmp une"u8,
                MirBinOp.Lt => "fcmp olt"u8,
                MirBinOp.Le => "fcmp ole"u8,
                MirBinOp.Gt => "fcmp ogt"u8,
                _ => "fcmp oge"u8,
            };
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = "u8);
            body.Append(fop);
            body.AppendAscii((byte)' ');
            body.Append(lhsLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(lhs));
            body.Append(", "u8);
            body.Append(fs.Text(rhs));
            body.AppendAscii((byte)'\n');
            return res;
        }

        if (lhsType == TypeIds.Bool)
        {
            ByteSlice res = fs.NextTemp();
            ReadOnlySpan<byte> bop = op switch
            {
                MirBinOp.BitAnd => "and"u8,
                MirBinOp.BitOr => "or"u8,
                MirBinOp.BitXor => "xor"u8,
                MirBinOp.Eq => "icmp eq"u8,
                MirBinOp.Ne => "icmp ne"u8,
                MirBinOp.Lt => "icmp ult"u8,
                MirBinOp.Le => "icmp ule"u8,
                MirBinOp.Gt => "icmp ugt"u8,
                _ => "icmp uge"u8,
            };
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = "u8);
            body.Append(bop);
            body.Append(" i1 "u8);
            body.Append(fs.Text(lhs));
            body.Append(", "u8);
            body.Append(fs.Text(rhs));
            body.AppendAscii((byte)'\n');
            return res;
        }

        switch (op)
        {
            case MirBinOp.Add:
            case MirBinOp.Sub:
            case MirBinOp.Mul:
            {
                ReadOnlySpan<byte> opName = op == MirBinOp.Add ? "add"u8 : (op == MirBinOp.Sub ? "sub"u8 : "mul"u8);
                ByteSlice res = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = "u8);
                body.Append(opName);
                body.AppendAscii((byte)' ');
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(fs.Text(rhs));
                body.AppendAscii((byte)'\n');
                return res;
            }

            case MirBinOp.Div:
            case MirBinOp.Rem:
            {
                ByteSlice isZero = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(isZero));
                body.Append(" = icmp eq "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(rhs));
                body.Append(", 0\n"u8);
                EmitPanicIfTrue(isZero, "attempt to divide by zero"u8, ref ectx, ref fs, ref body);

                if (types.IsUnsignedInteger(lhsType))
                {
                    ByteSlice uRes = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(uRes));
                    body.Append(op == MirBinOp.Div ? " = udiv "u8 : " = urem "u8);
                    body.Append(lhsLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(lhs));
                    body.Append(", "u8);
                    body.Append(fs.Text(rhs));
                    body.AppendAscii((byte)'\n');
                    return uRes;
                }

                ReadOnlySpan<byte> minVal = Bytes.Eq(lhsLlvm.AsUtf8(), "i8"u8) ? "-128"u8 :
                    Bytes.Eq(lhsLlvm.AsUtf8(), "i16"u8) ? "-32768"u8 :
                    Bytes.Eq(lhsLlvm.AsUtf8(), "i64"u8) ? "-9223372036854775808"u8 : "-2147483648"u8;

                ByteSlice isMin = fs.NextTemp();
                ByteSlice isNegOne = fs.NextTemp();
                ByteSlice isOvf = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(isMin));
                body.Append(" = icmp eq "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(minVal);
                body.Append("\n  "u8);
                body.Append(fs.Text(isNegOne));
                body.Append(" = icmp eq "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(rhs));
                body.Append(", -1\n  "u8);
                body.Append(fs.Text(isOvf));
                body.Append(" = and i1 "u8);
                body.Append(fs.Text(isMin));
                body.Append(", "u8);
                body.Append(fs.Text(isNegOne));
                body.AppendAscii((byte)'\n');
                EmitPanicIfTrue(isOvf, "arithmetic overflow"u8, ref ectx, ref fs, ref body);

                ByteSlice res = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(op == MirBinOp.Div ? " = sdiv "u8 : " = srem "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(fs.Text(rhs));
                body.AppendAscii((byte)'\n');
                return res;
            }

            case MirBinOp.BitAnd:
            case MirBinOp.BitOr:
            case MirBinOp.BitXor:
            {
                ReadOnlySpan<byte> opName = op == MirBinOp.BitAnd ? "and"u8 : (op == MirBinOp.BitOr ? "or"u8 : "xor"u8);
                ByteSlice res = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = "u8);
                body.Append(opName);
                body.AppendAscii((byte)' ');
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(fs.Text(rhs));
                body.AppendAscii((byte)'\n');
                return res;
            }

            case MirBinOp.Shl:
            case MirBinOp.Shr:
            case MirBinOp.UShr:
            {
                var rhsLlvm = new StringBuf(32);
                AppendValueLlvmType(rhsType, in ast, in names, in types, ref rhsLlvm);
                int lhsBits = LlvmIntBitWidth(lhsLlvm.AsUtf8());
                int rhsBits = LlvmIntBitWidth(rhsLlvm.AsUtf8());
                ByteSlice matchedRhs = rhs;
                if (!Bytes.Eq(rhsLlvm.AsUtf8(), lhsLlvm.AsUtf8()))
                {
                    matchedRhs = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(matchedRhs));
                    body.Append(rhsBits < lhsBits ? " = zext "u8 : " = trunc "u8);
                    body.Append(rhsLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(rhs));
                    body.Append(" to "u8);
                    body.Append(lhsLlvm.AsUtf8());
                    body.AppendAscii((byte)'\n');
                }

                ByteSlice masked = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(masked));
                body.Append(" = and "u8);
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(matchedRhs));
                body.Append(", "u8);
                body.AppendInt(lhsBits - 1);
                body.AppendAscii((byte)'\n');

                bool isUnsigned = types.IsUnsignedInteger(lhsType);
                ReadOnlySpan<byte> shiftOp = op == MirBinOp.Shl
                    ? "shl"u8
                    : (op == MirBinOp.Shr && !isUnsigned ? "ashr"u8 : "lshr"u8);

                ByteSlice res = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = "u8);
                body.Append(shiftOp);
                body.AppendAscii((byte)' ');
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(fs.Text(masked));
                body.AppendAscii((byte)'\n');
                return res;
            }

            default:
            {
                bool isUnsigned = types.IsUnsignedInteger(lhsType);
                ReadOnlySpan<byte> pred = op switch
                {
                    MirBinOp.Eq => "eq"u8,
                    MirBinOp.Ne => "ne"u8,
                    MirBinOp.Lt => isUnsigned ? "ult"u8 : "slt"u8,
                    MirBinOp.Le => isUnsigned ? "ule"u8 : "sle"u8,
                    MirBinOp.Gt => isUnsigned ? "ugt"u8 : "sgt"u8,
                    _ => isUnsigned ? "uge"u8 : "sge"u8,
                };
                ByteSlice res = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = icmp "u8);
                body.Append(pred);
                body.AppendAscii((byte)' ');
                body.Append(lhsLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(lhs));
                body.Append(", "u8);
                body.Append(fs.Text(rhs));
                body.AppendAscii((byte)'\n');
                return res;
            }
        }
    }

    private static int LlvmIntBitWidth(ReadOnlySpan<byte> llvmTy)
    {
        if (Bytes.Eq(llvmTy, "i8"u8)) return 8;
        if (Bytes.Eq(llvmTy, "i16"u8)) return 16;
        if (Bytes.Eq(llvmTy, "i64"u8)) return 64;
        return 32;
    }

    private static ByteSlice EmitUnaryValue(
        MirUnOp op,
        int subOpId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        int opType = mir.Operands[subOpId].TypeId;
        ByteSlice val = EmitOperand(subOpId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        var llvmTy = new StringBuf(32);
        AppendValueLlvmType(opType, in ast, in names, in types, ref llvmTy);

        ByteSlice res = fs.NextTemp();
        switch (op)
        {
            case MirUnOp.Neg:
                if (opType == TypeIds.F32 || opType == TypeIds.F64)
                {
                    body.Append("  "u8);
                    body.Append(fs.Text(res));
                    body.Append(" = fneg "u8);
                    body.Append(llvmTy.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(val));
                    body.AppendAscii((byte)'\n');
                }
                else
                {
                    body.Append("  "u8);
                    body.Append(fs.Text(res));
                    body.Append(" = sub "u8);
                    body.Append(llvmTy.AsUtf8());
                    body.Append(" 0, "u8);
                    body.Append(fs.Text(val));
                    body.AppendAscii((byte)'\n');
                }
                return res;

            case MirUnOp.Not:
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = xor i1 "u8);
                body.Append(fs.Text(val));
                body.Append(", true\n"u8);
                return res;

            default:
                body.Append("  "u8);
                body.Append(fs.Text(res));
                body.Append(" = xor "u8);
                body.Append(llvmTy.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(val));
                body.Append(", -1\n"u8);
                return res;
        }
    }

    private static ByteSlice EmitCastValue(
        int srcOpId,
        int dstTypeId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        int srcTypeId = mir.Operands[srcOpId].TypeId;
        ByteSlice srcVal = EmitOperand(srcOpId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

        var srcLlvm = new StringBuf(32);
        var dstLlvm = new StringBuf(32);
        AppendValueLlvmType(srcTypeId, in ast, in names, in types, ref srcLlvm);
        AppendValueLlvmType(dstTypeId, in ast, in names, in types, ref dstLlvm);

        if (Bytes.Eq(srcLlvm.AsUtf8(), dstLlvm.AsUtf8()))
        {
            return srcVal;
        }

        ByteSlice res = fs.NextTemp();
        bool srcIsInt = Bytes.Eq(srcLlvm.AsUtf8(), "i8"u8) || Bytes.Eq(srcLlvm.AsUtf8(), "i16"u8) || Bytes.Eq(srcLlvm.AsUtf8(), "i32"u8) || Bytes.Eq(srcLlvm.AsUtf8(), "i64"u8);
        bool dstIsInt = Bytes.Eq(dstLlvm.AsUtf8(), "i8"u8) || Bytes.Eq(dstLlvm.AsUtf8(), "i16"u8) || Bytes.Eq(dstLlvm.AsUtf8(), "i32"u8) || Bytes.Eq(dstLlvm.AsUtf8(), "i64"u8);

        if (srcIsInt && dstIsInt)
        {
            int srcBits = LlvmIntBitWidth(srcLlvm.AsUtf8());
            int dstBits = LlvmIntBitWidth(dstLlvm.AsUtf8());
            body.Append("  "u8);
            body.Append(fs.Text(res));
            if (srcBits < dstBits)
            {
                body.Append(types.IsUnsignedInteger(srcTypeId) ? " = zext "u8 : " = sext "u8);
            }
            else
            {
                body.Append(" = trunc "u8);
            }
            body.Append(srcLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(srcVal));
            body.Append(" to "u8);
            body.Append(dstLlvm.AsUtf8());
            body.AppendAscii((byte)'\n');
            return res;
        }

        if (srcIsInt && (dstTypeId == TypeIds.F32 || dstTypeId == TypeIds.F64))
        {
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(types.IsUnsignedInteger(srcTypeId) ? " = uitofp "u8 : " = sitofp "u8);
            body.Append(srcLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(srcVal));
            body.Append(" to "u8);
            body.Append(dstLlvm.AsUtf8());
            body.AppendAscii((byte)'\n');
            return res;
        }

        if ((srcTypeId == TypeIds.F32 || srcTypeId == TypeIds.F64) && dstIsInt)
        {
            ReadOnlySpan<byte> fSuffix = srcTypeId == TypeIds.F32 ? "f32"u8 : "f64"u8;
            ReadOnlySpan<byte> satKind = types.IsUnsignedInteger(dstTypeId) ? "fptoui"u8 : "fptosi"u8;
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = call "u8);
            body.Append(dstLlvm.AsUtf8());
            body.Append(" @llvm."u8);
            body.Append(satKind);
            body.Append(".sat."u8);
            body.Append(dstLlvm.AsUtf8());
            body.AppendAscii((byte)'.');
            body.Append(fSuffix);
            body.AppendAscii((byte)'(');
            body.Append(srcLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(srcVal));
            body.Append(")\n"u8);
            return res;
        }

        if (srcTypeId == TypeIds.F32 && dstTypeId == TypeIds.F64)
        {
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = fpext float "u8);
            body.Append(fs.Text(srcVal));
            body.Append(" to double\n"u8);
            return res;
        }

        if (srcTypeId == TypeIds.F64 && dstTypeId == TypeIds.F32)
        {
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = fptrunc double "u8);
            body.Append(fs.Text(srcVal));
            body.Append(" to float\n"u8);
            return res;
        }

        if (Bytes.Eq(srcLlvm.AsUtf8(), "i1"u8) && dstIsInt)
        {
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = zext i1 "u8);
            body.Append(fs.Text(srcVal));
            body.Append(" to "u8);
            body.Append(dstLlvm.AsUtf8());
            body.AppendAscii((byte)'\n');
            return res;
        }

        return srcVal;
    }

    private static void EmitDisposeResourceInst(
        in MirInst inst,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        int lid = inst.A;
        int dispInstId = inst.B;
        if (lid < 0 || lid >= fs.Slots.Count || dispInstId < 0)
        {
            return;
        }

        LocalSlot sl = fs.Slots[lid];
        FuncInstance dispFi = mir.FuncInstances[dispInstId];
        ByteSlice doDispLbl = fs.NextLabel("dispose_live"u8);
        ByteSlice contLbl = fs.NextLabel("dispose_cont"u8);

        if (sl.HasDropFlag)
        {
            ByteSlice live = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(live));
            body.Append(" = load i1, ptr %drop_"u8);
            body.AppendInt(lid);
            body.Append("\n  br i1 "u8);
            body.Append(fs.Text(live));
            body.Append(", label %"u8);
            body.Append(fs.Text(doDispLbl));
            body.Append(", label %"u8);
            body.Append(fs.Text(contLbl));
            body.AppendAscii((byte)'\n');
        }
        else
        {
            body.Append("  br label %"u8);
            body.Append(fs.Text(doDispLbl));
            body.AppendAscii((byte)'\n');
        }

        fs.EmitLabel(doDispLbl, ref body);
        if (sl.HasDropFlag)
        {
            body.Append("  store i1 false, ptr %drop_"u8);
            body.AppendInt(lid);
            body.AppendAscii((byte)'\n');
        }

        ByteSlice argPtr;
        if (types.Items[sl.StorageOrRefTypeId].Kind == TypeKinds.Class)
        {
            argPtr = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(argPtr));
            body.Append(" = load ptr, ptr %loc_"u8);
            body.AppendInt(lid);
            body.AppendAscii((byte)'\n');
        }
        else
        {
            argPtr = LocalAllocaSsa(lid, ref fs);
        }

        if (dispFi.ReturnTypeId != TypeIds.Void)
        {
            ByteSlice dummy = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(dummy));
            body.Append(" = call "u8);
            AppendReturnLlvmType(in dispFi, in ast, in names, in types, ref body);
            body.AppendAscii((byte)' ');
            AppendFuncSymbol(dispInstId, in ta, in mir, ref ectx, ref body);
            body.Append("(ptr "u8);
            body.Append(fs.Text(argPtr));
            body.Append(")\n"u8);
        }
        else
        {
            body.Append("  call void "u8);
            AppendFuncSymbol(dispInstId, in ta, in mir, ref ectx, ref body);
            body.Append("(ptr "u8);
            body.Append(fs.Text(argPtr));
            body.Append(")\n"u8);
        }

        if (RequiresCleanup(sl.StorageOrRefTypeId, in ast, in types))
        {
            body.Append("  call void "u8);
            AppendCleanupSymbol(sl.StorageOrRefTypeId, in ast, in names, in types, ref ectx, ref body);
            body.Append("(ptr %loc_"u8);
            body.AppendInt(lid);
            body.Append(")\n"u8);
        }

        body.Append("  br label %"u8);
        body.Append(fs.Text(contLbl));
        body.AppendAscii((byte)'\n');
        fs.EmitLabel(contLbl, ref body);
    }

    private static void EmitCallInst(
        in MirInst inst,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        FuncInstance sig = mir.FuncInstances[inst.A];
        var callArgsBuf = new Vec<ByteSlice>(inst.C * 2);
        var postOutOrRefLocals = new Vec<int>(4);

        for (int i = 0; i < inst.C; i++)
        {
            MirCallArg ca = mir.CallArgs[inst.B + i];
            MirParamSig pSig = mir.ParamSigs[sig.ParamSigStart + i];
            int pType = pSig.TypeId;
            TypeInfo pti = types.Items[pType];

            if (pSig.Mode == PassMode.ByValue && (pti.Kind == TypeKinds.Span || pti.Kind == TypeKinds.ReadOnlySpan))
            {
                ByteSlice spanVal = ca.Mode == PassMode.ByValue
                    ? EmitOperand(ca.OperandOrPlaceId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body)
                    : LoadPlaceValueWithoutMove(ca.OperandOrPlaceId, pType, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice sPtr = fs.NextTemp();
                ByteSlice sLen = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(sPtr));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(spanVal));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(sLen));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(spanVal));
                body.Append(", 1\n"u8);

                var arg0 = new StringBuf(32);
                arg0.Append("ptr "u8);
                arg0.Append(fs.Text(sPtr));
                callArgsBuf.Push(fs.InternSsa(arg0.AsUtf8()));

                var arg1 = new StringBuf(32);
                arg1.Append("i32 "u8);
                arg1.Append(fs.Text(sLen));
                callArgsBuf.Push(fs.InternSsa(arg1.AsUtf8()));
                continue;
            }

            if (pSig.Mode == PassMode.ByValue)
            {
                ByteSlice val = EmitOperand(ca.OperandOrPlaceId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                var argSb = new StringBuf(64);
                AppendValueLlvmType(pType, in ast, in names, in types, ref argSb);
                argSb.AppendAscii((byte)' ');
                argSb.Append(fs.Text(val));
                callArgsBuf.Push(fs.InternSsa(argSb.AsUtf8()));
                continue;
            }

            if ((pSig.Mode == PassMode.ThisShared || pSig.Mode == PassMode.ThisMut) && pti.Kind == TypeKinds.Class)
            {
                // Class receiver passed as operand (`IsOperandCallArg` in FuncLowerer/OwnershipChecker!)
                MirOperand rop = mir.Operands[ca.OperandOrPlaceId];
                ByteSlice objPtr;
                if (rop.Kind == MirOpKinds.CopyOrMove)
                {
                    ByteSlice slotAddr = EmitPlaceAddress(rop.PlaceOrLitIdx, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                    objPtr = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(objPtr));
                    body.Append(" = load ptr, ptr "u8);
                    body.Append(fs.Text(slotAddr));
                    body.AppendAscii((byte)'\n');
                }
                else
                {
                    objPtr = EmitOperand(ca.OperandOrPlaceId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }

                var argSb = new StringBuf(32);
                argSb.Append("ptr "u8);
                argSb.Append(fs.Text(objPtr));
                callArgsBuf.Push(fs.InternSsa(argSb.AsUtf8()));
                continue;
            }

            // Borrowed/indirect place (`in`, `ref`, `out`, or struct `this`)
            int borrowPlaceId = ca.OperandOrPlaceId;
            ByteSlice borrowPtr = EmitPlaceAddress(borrowPlaceId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
            if ((pSig.Mode == PassMode.Out || pSig.Mode == PassMode.Ref) &&
                mir.Places[borrowPlaceId].ProjCount == 0)
            {
                int outLid = mir.Places[borrowPlaceId].LocalId;
                if (!fs.Slots[outLid].IsRefSlot && fs.Slots[outLid].HasDropFlag)
                {
                    if (pSig.Mode == PassMode.Out)
                    {
                        EmitDropLocalIfLive(outLid, in ast, in names, in types, ref ectx, ref fs, ref body);
                    }
                    postOutOrRefLocals.Push(outLid);
                }
            }

            var ptrArg = new StringBuf(32);
            ptrArg.Append("ptr "u8);
            ptrArg.Append(fs.Text(borrowPtr));
            callArgsBuf.Push(fs.InternSsa(ptrArg.AsUtf8()));
        }

        var argsJoined = new StringBuf(128);
        for (int i = 0; i < callArgsBuf.Count; i++)
        {
            if (i > 0) argsJoined.Append(", "u8);
            argsJoined.Append(fs.Text(callArgsBuf[i]));
        }

        if ((sig.Flags & DeclFlags.ReturnsByRef) != 0)
        {
            ByteSlice retPtr = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(retPtr));
            body.Append(" = call ptr "u8);
            AppendFuncSymbol(inst.A, in ta, in mir, ref ectx, ref body);
            body.AppendAscii((byte)'(');
            body.Append(argsJoined.AsUtf8());
            body.Append(")\n"u8);
            StoreRefResult(inst.RefDestLocalId, inst.DestPlaceId, retPtr, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        }
        else if (sig.ReturnTypeId == TypeIds.Void)
        {
            body.Append("  call void "u8);
            AppendFuncSymbol(inst.A, in ta, in mir, ref ectx, ref body);
            body.AppendAscii((byte)'(');
            body.Append(argsJoined.AsUtf8());
            body.Append(")\n"u8);
        }
        else
        {
            ByteSlice retVal = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(retVal));
            body.Append(" = call "u8);
            AppendValueLlvmType(sig.ReturnTypeId, in ast, in names, in types, ref body);
            body.AppendAscii((byte)' ');
            AppendFuncSymbol(inst.A, in ta, in mir, ref ectx, ref body);
            body.AppendAscii((byte)'(');
            body.Append(argsJoined.AsUtf8());
            body.Append(")\n"u8);

            if (inst.DestPlaceId >= 0)
            {
                StoreIntoPlaceWithOverwriteDrop(inst.DestPlaceId, retVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            }
            else
            {
                SpillAndDropValueIfNeeded(sig.ReturnTypeId, retVal, in ast, in names, in types, ref ectx, ref fs, ref body);
            }
        }

        for (int i = 0; i < postOutOrRefLocals.Count; i++)
        {
            body.Append("  store i1 true, ptr %drop_"u8);
            body.AppendInt(postOutOrRefLocals[i]);
            body.AppendAscii((byte)'\n');
        }
    }

    private static void AssignIntrinsicResult(
        in MirInst inst,
        ByteSlice ssaVal,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        if (inst.DestPlaceId >= 0)
        {
            StoreIntoPlaceWithOverwriteDrop(inst.DestPlaceId, ssaVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        }
        else if (inst.RefDestLocalId >= 0)
        {
            StoreRefResult(inst.RefDestLocalId, -1, ssaVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        }
        else if (inst.TypeId != TypeIds.Void)
        {
            SpillAndDropValueIfNeeded(inst.TypeId, ssaVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
    }

    private static void EmitIntrinsicInst(
        in MirInst inst,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        MirIntrinsic kind = (MirIntrinsic)inst.A;
        int valStart = inst.B;
        int valCount = inst.C;
        int plCount = inst.D >>> 20;
        int plStart = inst.D & 0xFFFFF;

        switch (kind)
        {
            case MirIntrinsic.IoWriteInt64:
            case MirIntrinsic.IoWriteLineInt64:
            {
                int opId = mir.ExtraLists[valStart];
                int argT = mir.Operands[opId].TypeId;
                ByteSlice v = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                var srcLlvm = new StringBuf(16);
                AppendValueLlvmType(argT, in ast, in names, in types, ref srcLlvm);
                if (!Bytes.Eq(srcLlvm.AsUtf8(), "i64"u8))
                {
                    ByteSlice ext = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(ext));
                    body.Append(" = sext "u8);
                    body.Append(srcLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(v));
                    body.Append(" to i64\n"u8);
                    v = ext;
                }
                body.Append("  call void @__vib_print_i64(i64 "u8);
                body.Append(fs.Text(v));
                body.Append(")\n"u8);
                if (kind == MirIntrinsic.IoWriteLineInt64)
                {
                    body.Append("  call void @__vib_print_nl()\n"u8);
                }
                break;
            }

            case MirIntrinsic.IoWriteUInt64:
            case MirIntrinsic.IoWriteLineUInt64:
            {
                int opId = mir.ExtraLists[valStart];
                int argT = mir.Operands[opId].TypeId;
                ByteSlice v = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                var srcLlvm = new StringBuf(16);
                AppendValueLlvmType(argT, in ast, in names, in types, ref srcLlvm);
                if (!Bytes.Eq(srcLlvm.AsUtf8(), "i64"u8))
                {
                    ByteSlice ext = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(ext));
                    body.Append(" = zext "u8);
                    body.Append(srcLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(v));
                    body.Append(" to i64\n"u8);
                    v = ext;
                }
                body.Append("  call void @__vib_print_u64(i64 "u8);
                body.Append(fs.Text(v));
                body.Append(")\n"u8);
                if (kind == MirIntrinsic.IoWriteLineUInt64)
                {
                    body.Append("  call void @__vib_print_nl()\n"u8);
                }
                break;
            }

            case MirIntrinsic.IoWriteBool:
            case MirIntrinsic.IoWriteLineBool:
            {
                ByteSlice v = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                body.Append("  call void @__vib_print_bool(i1 "u8);
                body.Append(fs.Text(v));
                body.Append(")\n"u8);
                if (kind == MirIntrinsic.IoWriteLineBool)
                {
                    body.Append("  call void @__vib_print_nl()\n"u8);
                }
                break;
            }

            case MirIntrinsic.IoWriteStr:
            case MirIntrinsic.IoWriteLineStr:
            {
                ByteSlice s = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice p = fs.NextTemp();
                ByteSlice l = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(p));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(s));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(l));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(s));
                body.Append(", 1\n  call void @__vib_print_str(ptr "u8);
                body.Append(fs.Text(p));
                body.Append(", i64 "u8);
                body.Append(fs.Text(l));
                body.Append(")\n"u8);
                if (kind == MirIntrinsic.IoWriteLineStr)
                {
                    body.Append("  call void @__vib_print_nl()\n"u8);
                }
                break;
            }

            case MirIntrinsic.IoWriteLineEmpty:
                body.Append("  call void @__vib_print_nl()\n"u8);
                break;

            case MirIntrinsic.StdioWriteOut:
            case MirIntrinsic.StdioWriteErr:
            {
                ByteSlice spanVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice p = fs.NextTemp();
                ByteSlice l = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(p));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(spanVal));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(l));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(spanVal));
                body.Append(", 1\n  call void "u8);
                body.Append(kind == MirIntrinsic.StdioWriteOut ? "@__vib_write_out(ptr "u8 : "@__vib_write_err(ptr "u8);
                body.Append(fs.Text(p));
                body.Append(", i32 "u8);
                body.Append(fs.Text(l));
                body.Append(")\n"u8);
                break;
            }

            case MirIntrinsic.Panic:
            {
                ByteSlice s = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice p = fs.NextTemp();
                ByteSlice l = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(p));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(s));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(l));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(s));
                body.Append(", 1\n  call void @__vib_panic(ptr "u8);
                body.Append(fs.Text(p));
                body.Append(", i64 "u8);
                body.Append(fs.Text(l));
                body.Append(")\n"u8);
                break;
            }

            case MirIntrinsic.OptionAsRef:
            case MirIntrinsic.OptionAsMut:
            {
                ByteSlice slotPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                AssignIntrinsicResult(in inst, slotPtr, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionTake:
            {
                ByteSlice slotPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int slotType);
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(slotType, in ast, in names, in types, ref optLlvm);
                ByteSlice prev = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(prev));
                body.Append(" = load "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.Append("\n  store "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(" zeroinitializer, ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.AppendAscii((byte)'\n');
                AssignIntrinsicResult(in inst, prev, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionReplace:
            {
                ByteSlice replVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice slotPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int slotType);
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(slotType, in ast, in names, in types, ref optLlvm);
                ByteSlice prev = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(prev));
                body.Append(" = load "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.Append("\n  store "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(replVal));
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.AppendAscii((byte)'\n');
                AssignIntrinsicResult(in inst, prev, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionExpect:
            {
                int op0 = mir.ExtraLists[valStart];
                int optType = mir.Operands[op0].TypeId;
                ByteSlice optVal = EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice msgVal = EmitOperand(mir.ExtraLists[valStart + 1], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(optType, in ast, in names, in types, ref optLlvm);
                ByteSlice hasVal = fs.NextTemp();
                ByteSlice okLbl = fs.NextLabel("opt_expect_some"u8);
                ByteSlice panicLbl = fs.NextLabel("opt_expect_none"u8);
                body.Append("  "u8);
                body.Append(fs.Text(hasVal));
                body.Append(" = extractvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(optVal));
                body.Append(", 0\n  br i1 "u8);
                body.Append(fs.Text(hasVal));
                body.Append(", label %"u8);
                body.Append(fs.Text(okLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(panicLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(panicLbl, ref body);
                ByteSlice mp = fs.NextTemp();
                ByteSlice ml = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(mp));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(msgVal));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(ml));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(msgVal));
                body.Append(", 1\n  call void @__vib_panic(ptr "u8);
                body.Append(fs.Text(mp));
                body.Append(", i64 "u8);
                body.Append(fs.Text(ml));
                body.Append(")\n  unreachable\n"u8);
                fs.EmitLabel(okLbl, ref body);
                ByteSlice payload = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(payload));
                body.Append(" = extractvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(optVal));
                body.Append(", 1\n"u8);
                AssignIntrinsicResult(in inst, payload, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionUnwrap:
            {
                int optType;
                ByteSlice optVal;
                if (valCount > 0)
                {
                    int opId = mir.ExtraLists[valStart];
                    optType = mir.Operands[opId].TypeId;
                    optVal = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                else
                {
                    int plId = mir.ExtraLists[plStart];
                    optType = mir.Places[plId].TypeId;
                    optVal = EmitOperandFromPlaceByValue(plId, optType, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(optType, in ast, in names, in types, ref optLlvm);
                ByteSlice hasVal = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(hasVal));
                body.Append(" = extractvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(optVal));
                body.Append(", 0\n"u8);
                EmitPanicIfFalse(hasVal, "called Option.Unwrap() on None"u8, ref ectx, ref fs, ref body);
                ByteSlice payload = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(payload));
                body.Append(" = extractvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(optVal));
                body.Append(", 1\n"u8);
                AssignIntrinsicResult(in inst, payload, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionUnwrapOr:
            {
                int optType;
                ByteSlice optVal;
                ByteSlice fallbackVal;
                if (valCount >= 2)
                {
                    int op0 = mir.ExtraLists[valStart];
                    optType = mir.Operands[op0].TypeId;
                    optVal = EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    fallbackVal = EmitOperand(mir.ExtraLists[valStart + 1], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                else
                {
                    int plId = mir.ExtraLists[plStart];
                    optType = mir.Places[plId].TypeId;
                    optVal = EmitOperandFromPlaceByValue(plId, optType, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    fallbackVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(optType, in ast, in names, in types, ref optLlvm);
                var innerLlvm = new StringBuf(64);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref innerLlvm);

                ByteSlice has = fs.NextTemp();
                ByteSlice someLbl = fs.NextLabel("unwrap_or_some"u8);
                ByteSlice someDoneLbl = fs.NextLabel("unwrap_or_some_done"u8);
                ByteSlice noneLbl = fs.NextLabel("unwrap_or_none"u8);
                ByteSlice mergeLbl = fs.NextLabel("unwrap_or_merge"u8);

                body.Append("  "u8);
                body.Append(fs.Text(has));
                body.Append(" = extractvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(optVal));
                body.Append(", 0\n  br i1 "u8);
                body.Append(fs.Text(has));
                body.Append(", label %"u8);
                body.Append(fs.Text(someLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(noneLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(someLbl, ref body);
                SpillAndDropValueIfNeeded(inst.TypeId, fallbackVal, in ast, in names, in types, ref ectx, ref fs, ref body);
                ByteSlice someVal = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(someVal));
                body.Append(" = extractvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(optVal));
                body.Append(", 1\n  br label %"u8);
                body.Append(fs.Text(someDoneLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(someDoneLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(noneLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(mergeLbl, ref body);
                ByteSlice merged = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(merged));
                body.Append(" = phi "u8);
                body.Append(innerLlvm.AsUtf8());
                body.Append(" [ "u8);
                body.Append(fs.Text(someVal));
                body.Append(", %"u8);
                body.Append(fs.Text(someDoneLbl));
                body.Append(" ], [ "u8);
                body.Append(fs.Text(fallbackVal));
                body.Append(", %"u8);
                body.Append(fs.Text(noneLbl));
                body.Append(" ]\n"u8);
                AssignIntrinsicResult(in inst, merged, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionUnwrapMut:
            {
                ByteSlice slotPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int slotType);
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(slotType, in ast, in names, in types, ref optLlvm);
                ByteSlice tagPtr = fs.NextTemp();
                ByteSlice tagVal = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(tagPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.Append(", i32 0, i32 0\n  "u8);
                body.Append(fs.Text(tagVal));
                body.Append(" = load i1, ptr "u8);
                body.Append(fs.Text(tagPtr));
                body.AppendAscii((byte)'\n');
                EmitPanicIfFalse(tagVal, "called Option.UnwrapMut() on None"u8, ref ectx, ref fs, ref body);
                ByteSlice valPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(valPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.Append(", i32 0, i32 1\n"u8);
                StoreRefResult(inst.RefDestLocalId, inst.DestPlaceId, valPtr, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionIsSome:
            {
                ByteSlice tagVal;
                if (plCount > 0)
                {
                    ByteSlice pAddr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int pType);
                    TypeInfo pti = types.Items[pType];
                    if (pti.Kind == TypeKinds.OptionRef || pti.Kind == TypeKinds.OptionMut)
                    {
                        var optLlvm = new StringBuf(64);
                        optLlvm.Append("{ i1, "u8);
                        AppendValueLlvmType(pti.A, in ast, in names, in types, ref optLlvm);
                        optLlvm.Append(" }"u8);
                        ByteSlice optSlot = fs.NextTemp();
                        ByteSlice tagPtr = fs.NextTemp();
                        tagVal = fs.NextTemp();
                        body.Append("  "u8);
                        body.Append(fs.Text(optSlot));
                        body.Append(" = load ptr, ptr "u8);
                        body.Append(fs.Text(pAddr));
                        body.Append("\n  "u8);
                        body.Append(fs.Text(tagPtr));
                        body.Append(" = getelementptr inbounds "u8);
                        body.Append(optLlvm.AsUtf8());
                        body.Append(", ptr "u8);
                        body.Append(fs.Text(optSlot));
                        body.Append(", i32 0, i32 0\n  "u8);
                        body.Append(fs.Text(tagVal));
                        body.Append(" = load i1, ptr "u8);
                        body.Append(fs.Text(tagPtr));
                        body.AppendAscii((byte)'\n');
                    }
                    else
                    {
                        var optLlvm = new StringBuf(64);
                        AppendValueLlvmType(pType, in ast, in names, in types, ref optLlvm);
                        ByteSlice tagPtr = fs.NextTemp();
                        tagVal = fs.NextTemp();
                        body.Append("  "u8);
                        body.Append(fs.Text(tagPtr));
                        body.Append(" = getelementptr inbounds "u8);
                        body.Append(optLlvm.AsUtf8());
                        body.Append(", ptr "u8);
                        body.Append(fs.Text(pAddr));
                        body.Append(", i32 0, i32 0\n  "u8);
                        body.Append(fs.Text(tagVal));
                        body.Append(" = load i1, ptr "u8);
                        body.Append(fs.Text(tagPtr));
                        body.AppendAscii((byte)'\n');
                    }
                }
                else
                {
                    int opId = mir.ExtraLists[valStart];
                    int opType = mir.Operands[opId].TypeId;
                    TypeInfo oti = types.Items[opType];
                    if (oti.Kind == TypeKinds.OptionRef || oti.Kind == TypeKinds.OptionMut)
                    {
                        ByteSlice optSlot = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                        var optLlvm = new StringBuf(64);
                        optLlvm.Append("{ i1, "u8);
                        AppendValueLlvmType(oti.A, in ast, in names, in types, ref optLlvm);
                        optLlvm.Append(" }"u8);
                        ByteSlice tagPtr = fs.NextTemp();
                        tagVal = fs.NextTemp();
                        body.Append("  "u8);
                        body.Append(fs.Text(tagPtr));
                        body.Append(" = getelementptr inbounds "u8);
                        body.Append(optLlvm.AsUtf8());
                        body.Append(", ptr "u8);
                        body.Append(fs.Text(optSlot));
                        body.Append(", i32 0, i32 0\n  "u8);
                        body.Append(fs.Text(tagVal));
                        body.Append(" = load i1, ptr "u8);
                        body.Append(fs.Text(tagPtr));
                        body.AppendAscii((byte)'\n');
                    }
                    else
                    {
                        var optLlvm = new StringBuf(64);
                        AppendValueLlvmType(opType, in ast, in names, in types, ref optLlvm);
                        ByteSlice optVal = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                        tagVal = fs.NextTemp();
                        body.Append("  "u8);
                        body.Append(fs.Text(tagVal));
                        body.Append(" = extractvalue "u8);
                        body.Append(optLlvm.AsUtf8());
                        body.AppendAscii((byte)' ');
                        body.Append(fs.Text(optVal));
                        body.Append(", 0\n"u8);
                        SpillAndDropValueIfNeeded(opType, optVal, in ast, in names, in types, ref ectx, ref fs, ref body);
                    }
                }
                AssignIntrinsicResult(in inst, tagVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionMutSet:
            {
                ByteSlice curAddr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int curType);
                ByteSlice slotPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(slotPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(curAddr));
                body.AppendAscii((byte)'\n');

                int opId = mir.ExtraLists[valStart];
                int optType = mir.Operands[opId].TypeId;
                ByteSlice newOptVal = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                _ = curType;

                if (RequiresCleanup(optType, in ast, in types))
                {
                    body.Append("  call void "u8);
                    AppendCleanupSymbol(optType, in ast, in names, in types, ref ectx, ref body);
                    body.Append("(ptr "u8);
                    body.Append(fs.Text(slotPtr));
                    body.Append(")\n"u8);
                }

                body.Append("  store "u8);
                AppendValueLlvmType(optType, in ast, in names, in types, ref body);
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(newOptVal));
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.AppendAscii((byte)'\n');
                break;
            }

            case MirIntrinsic.OptionMutTake:
            {
                ByteSlice curAddr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                ByteSlice slotPtr = fs.NextTemp();
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref optLlvm);
                ByteSlice prev = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(slotPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(curAddr));
                body.Append("\n  "u8);
                body.Append(fs.Text(prev));
                body.Append(" = load "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.Append("\n  store "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(" zeroinitializer, ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.AppendAscii((byte)'\n');
                AssignIntrinsicResult(in inst, prev, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionMutReplace:
            {
                ByteSlice curAddr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                ByteSlice slotPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(slotPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(curAddr));
                body.AppendAscii((byte)'\n');

                ByteSlice newOptVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref optLlvm);
                ByteSlice prev = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(prev));
                body.Append(" = load "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.Append("\n  store "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(newOptVal));
                body.Append(", ptr "u8);
                body.Append(fs.Text(slotPtr));
                body.AppendAscii((byte)'\n');
                AssignIntrinsicResult(in inst, prev, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.OptionMutAsRef:
            {
                ByteSlice curAddr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                ByteSlice slotPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(slotPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(curAddr));
                body.AppendAscii((byte)'\n');
                AssignIntrinsicResult(in inst, slotPtr, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            default:
                EmitIntrinsicCollectionsAndResult(kind, in inst, valStart, valCount, plStart, plCount, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
        }
    }

    private static void EmitIntrinsicCollectionsAndResult(
        MirIntrinsic kind,
        in MirInst inst,
        int valStart,
        int valCount,
        int plStart,
        int plCount,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        switch (kind)
        {
            case MirIntrinsic.ResultIsOk:
            {
                ByteSlice okBit;
                if (plCount > 0)
                {
                    ByteSlice resAddr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int resType);
                    var resLlvm = new StringBuf(64);
                    AppendValueLlvmType(resType, in ast, in names, in types, ref resLlvm);
                    ByteSlice tagPtr = fs.NextTemp();
                    okBit = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(tagPtr));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(resLlvm.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(resAddr));
                    body.Append(", i32 0, i32 0\n  "u8);
                    body.Append(fs.Text(okBit));
                    body.Append(" = load i1, ptr "u8);
                    body.Append(fs.Text(tagPtr));
                    body.AppendAscii((byte)'\n');
                }
                else
                {
                    int opId = mir.ExtraLists[valStart];
                    int resType = mir.Operands[opId].TypeId;
                    var resLlvm = new StringBuf(64);
                    AppendValueLlvmType(resType, in ast, in names, in types, ref resLlvm);
                    ByteSlice resVal = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    okBit = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(okBit));
                    body.Append(" = extractvalue "u8);
                    body.Append(resLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(resVal));
                    body.Append(", 0\n"u8);
                    SpillAndDropValueIfNeeded(resType, resVal, in ast, in names, in types, ref ectx, ref fs, ref body);
                }
                AssignIntrinsicResult(in inst, okBit, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.ResultExpect:
            {
                int op0 = mir.ExtraLists[valStart];
                int resType = mir.Operands[op0].TypeId;
                ByteSlice resVal = EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice msgVal = EmitOperand(mir.ExtraLists[valStart + 1], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                var resLlvm = new StringBuf(64);
                AppendValueLlvmType(resType, in ast, in names, in types, ref resLlvm);
                ByteSlice isOk = fs.NextTemp();
                ByteSlice okLbl = fs.NextLabel("res_expect_ok"u8);
                ByteSlice panicLbl = fs.NextLabel("res_expect_err"u8);
                body.Append("  "u8);
                body.Append(fs.Text(isOk));
                body.Append(" = extractvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(resVal));
                body.Append(", 0\n  br i1 "u8);
                body.Append(fs.Text(isOk));
                body.Append(", label %"u8);
                body.Append(fs.Text(okLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(panicLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(panicLbl, ref body);
                ByteSlice mp = fs.NextTemp();
                ByteSlice ml = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(mp));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(msgVal));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(ml));
                body.Append(" = extractvalue { ptr, i64 } "u8);
                body.Append(fs.Text(msgVal));
                body.Append(", 1\n  call void @__vib_panic(ptr "u8);
                body.Append(fs.Text(mp));
                body.Append(", i64 "u8);
                body.Append(fs.Text(ml));
                body.Append(")\n  unreachable\n"u8);
                fs.EmitLabel(okLbl, ref body);
                ByteSlice okVal = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(okVal));
                body.Append(" = extractvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(resVal));
                body.Append(", 1\n"u8);
                AssignIntrinsicResult(in inst, okVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.ResultUnwrap:
            {
                int resType;
                ByteSlice resVal;
                if (valCount > 0)
                {
                    int opId = mir.ExtraLists[valStart];
                    resType = mir.Operands[opId].TypeId;
                    resVal = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                else
                {
                    int plId = mir.ExtraLists[plStart];
                    resType = mir.Places[plId].TypeId;
                    resVal = EmitOperandFromPlaceByValue(plId, resType, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                var resLlvm = new StringBuf(64);
                AppendValueLlvmType(resType, in ast, in names, in types, ref resLlvm);
                ByteSlice isOk = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(isOk));
                body.Append(" = extractvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(resVal));
                body.Append(", 0\n"u8);
                EmitPanicIfFalse(isOk, "called Result.Unwrap() on Err"u8, ref ectx, ref fs, ref body);
                ByteSlice okVal = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(okVal));
                body.Append(" = extractvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(resVal));
                body.Append(", 1\n"u8);
                AssignIntrinsicResult(in inst, okVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.ResultUnwrapOr:
            {
                int resType;
                ByteSlice resVal;
                ByteSlice fallbackVal;
                if (valCount >= 2)
                {
                    int op0 = mir.ExtraLists[valStart];
                    resType = mir.Operands[op0].TypeId;
                    resVal = EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    fallbackVal = EmitOperand(mir.ExtraLists[valStart + 1], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                else
                {
                    int plId = mir.ExtraLists[plStart];
                    resType = mir.Places[plId].TypeId;
                    resVal = EmitOperandFromPlaceByValue(plId, resType, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    fallbackVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }

                TypeInfo rti = types.Items[resType];
                var resLlvm = new StringBuf(64);
                AppendValueLlvmType(resType, in ast, in names, in types, ref resLlvm);
                var okLlvm = new StringBuf(64);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref okLlvm);

                ByteSlice isOk = fs.NextTemp();
                ByteSlice okLbl = fs.NextLabel("res_unwrap_ok"u8);
                ByteSlice okDoneLbl = fs.NextLabel("res_unwrap_ok_done"u8);
                ByteSlice errLbl = fs.NextLabel("res_unwrap_err"u8);
                ByteSlice errDoneLbl = fs.NextLabel("res_unwrap_err_done"u8);
                ByteSlice mergeLbl = fs.NextLabel("res_unwrap_merge"u8);

                body.Append("  "u8);
                body.Append(fs.Text(isOk));
                body.Append(" = extractvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(resVal));
                body.Append(", 0\n  br i1 "u8);
                body.Append(fs.Text(isOk));
                body.Append(", label %"u8);
                body.Append(fs.Text(okLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(errLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(okLbl, ref body);
                SpillAndDropValueIfNeeded(rti.A, fallbackVal, in ast, in names, in types, ref ectx, ref fs, ref body);
                ByteSlice okPayload = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(okPayload));
                body.Append(" = extractvalue "u8);
                body.Append(resLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(resVal));
                body.Append(", 1\n  br label %"u8);
                body.Append(fs.Text(okDoneLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(okDoneLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(errLbl, ref body);
                if (RequiresCleanup(rti.B, in ast, in types))
                {
                    ByteSlice errPayload = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(errPayload));
                    body.Append(" = extractvalue "u8);
                    body.Append(resLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(resVal));
                    body.Append(", 2\n"u8);
                    SpillAndDropValueIfNeeded(rti.B, errPayload, in ast, in names, in types, ref ectx, ref fs, ref body);
                }
                body.Append("  br label %"u8);
                body.Append(fs.Text(errDoneLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(errDoneLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(mergeLbl, ref body);
                ByteSlice merged = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(merged));
                body.Append(" = phi "u8);
                body.Append(okLlvm.AsUtf8());
                body.Append(" [ "u8);
                body.Append(fs.Text(okPayload));
                body.Append(", %"u8);
                body.Append(fs.Text(okDoneLbl));
                body.Append(" ], [ "u8);
                body.Append(fs.Text(fallbackVal));
                body.Append(", %"u8);
                body.Append(fs.Text(errDoneLbl));
                body.Append(" ]\n"u8);
                AssignIntrinsicResult(in inst, merged, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecNewWithCapacity:
            {
                int elemType = types.Items[inst.TypeId].A;
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(elemType, in ast, in names, in types, ref elemLlvm);

                int capOp = mir.ExtraLists[valStart];
                ByteSlice cap = EnsureI32(EmitOperand(capOp, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[capOp].TypeId, in ast, in names, in types, ref fs, ref body);

                ByteSlice isNeg = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(isNeg));
                body.Append(" = icmp slt i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 0\n"u8);
                EmitPanicIfTrue(isNeg, "Vec capacity cannot be negative"u8, ref ectx, ref fs, ref body);

                ByteSlice exceedsMax = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(exceedsMax));
                body.Append(" = icmp sgt i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 2147483591\n"u8);
                EmitPanicIfTrue(exceedsMax, "Vec capacity overflow"u8, ref ectx, ref fs, ref body);

                ByteSlice isZero = fs.NextTemp();
                ByteSlice allocLbl = fs.NextLabel("vec_alloc"u8);
                ByteSlice allocDoneLbl = fs.NextLabel("vec_alloc_done"u8);
                ByteSlice zeroLbl = fs.NextLabel("vec_zero"u8);
                ByteSlice mergeLbl = fs.NextLabel("vec_merge"u8);

                body.Append("  "u8);
                body.Append(fs.Text(isZero));
                body.Append(" = icmp eq i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 0\n  br i1 "u8);
                body.Append(fs.Text(isZero));
                body.Append(", label %"u8);
                body.Append(fs.Text(zeroLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(allocLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(zeroLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(allocLbl, ref body);
                ByteSlice elemSz = EmitSizeOf(elemLlvm.AsUtf8(), ref fs, ref body);
                ByteSlice cap64 = fs.NextTemp();
                ByteSlice mulPair = fs.NextTemp();
                ByteSlice rawBytes = fs.NextTemp();
                ByteSlice mulOvf = fs.NextTemp();
                ByteSlice bytes = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(cap64));
                body.Append(" = zext i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(" to i64\n  "u8);
                body.Append(fs.Text(mulPair));
                body.Append(" = call { i64, i1 } @llvm.umul.with.overflow.i64(i64 "u8);
                body.Append(fs.Text(cap64));
                body.Append(", i64 "u8);
                body.Append(fs.Text(elemSz));
                body.Append(")\n  "u8);
                body.Append(fs.Text(rawBytes));
                body.Append(" = extractvalue { i64, i1 } "u8);
                body.Append(fs.Text(mulPair));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(mulOvf));
                body.Append(" = extractvalue { i64, i1 } "u8);
                body.Append(fs.Text(mulPair));
                body.Append(", 1\n"u8);
                EmitPanicIfTrue(mulOvf, "Vec capacity overflow"u8, ref ectx, ref fs, ref body);
                EmitClampMinOne(rawBytes, bytes, ref fs, ref body);
                ByteSlice buf = EmitCheckedMalloc(bytes, ref ectx, ref fs, ref body);

                ByteSlice v0 = fs.NextTemp();
                ByteSlice v1 = fs.NextTemp();
                ByteSlice v2 = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(v0));
                body.Append(" = insertvalue { ptr, i32, i32 } zeroinitializer, ptr "u8);
                body.Append(fs.Text(buf));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(v1));
                body.Append(" = insertvalue { ptr, i32, i32 } "u8);
                body.Append(fs.Text(v0));
                body.Append(", i32 0, 1\n  "u8);
                body.Append(fs.Text(v2));
                body.Append(" = insertvalue { ptr, i32, i32 } "u8);
                body.Append(fs.Text(v1));
                body.Append(", i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 2\n  br label %"u8);
                body.Append(fs.Text(allocDoneLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(allocDoneLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(mergeLbl, ref body);
                ByteSlice vecVal = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(vecVal));
                body.Append(" = phi { ptr, i32, i32 } [ zeroinitializer, %"u8);
                body.Append(fs.Text(zeroLbl));
                body.Append(" ], [ "u8);
                body.Append(fs.Text(v2));
                body.Append(", %"u8);
                body.Append(fs.Text(allocDoneLbl));
                body.Append(" ]\n"u8);
                AssignIntrinsicResult(in inst, vecVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecFromLiteral:
            {
                int elemType = types.Items[inst.TypeId].A;
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(elemType, in ast, in names, in types, ref elemLlvm);

                var elems = new Vec<ByteSlice>(valCount);
                for (int i = 0; i < valCount; i++)
                {
                    elems.Push(EmitOperand(mir.ExtraLists[valStart + i], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body));
                }

                if (valCount == 0)
                {
                    AssignIntrinsicResult(in inst, fs.InternSsa("zeroinitializer"u8), in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    break;
                }

                ByteSlice elemSz = EmitSizeOf(elemLlvm.AsUtf8(), ref fs, ref body);
                ByteSlice mulPair = fs.NextTemp();
                ByteSlice rawSz = fs.NextTemp();
                ByteSlice mulOvf = fs.NextTemp();
                ByteSlice clampedSz = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(mulPair));
                body.Append(" = call { i64, i1 } @llvm.umul.with.overflow.i64(i64 "u8);
                body.AppendInt(valCount);
                body.Append(", i64 "u8);
                body.Append(fs.Text(elemSz));
                body.Append(")\n  "u8);
                body.Append(fs.Text(rawSz));
                body.Append(" = extractvalue { i64, i1 } "u8);
                body.Append(fs.Text(mulPair));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(mulOvf));
                body.Append(" = extractvalue { i64, i1 } "u8);
                body.Append(fs.Text(mulPair));
                body.Append(", 1\n"u8);
                EmitPanicIfTrue(mulOvf, "Vec capacity overflow"u8, ref ectx, ref fs, ref body);
                EmitClampMinOne(rawSz, clampedSz, ref fs, ref body);
                ByteSlice dataBuf = EmitCheckedMalloc(clampedSz, ref ectx, ref fs, ref body);

                for (int i = 0; i < valCount; i++)
                {
                    ByteSlice ep = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(ep));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(elemLlvm.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(dataBuf));
                    body.Append(", i64 "u8);
                    body.AppendInt(i);
                    body.Append("\n  store "u8);
                    body.Append(elemLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(fs.Text(elems[i]));
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(ep));
                    body.AppendAscii((byte)'\n');
                }

                ByteSlice r0 = fs.NextTemp();
                ByteSlice r1 = fs.NextTemp();
                ByteSlice r2 = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(r0));
                body.Append(" = insertvalue { ptr, i32, i32 } zeroinitializer, ptr "u8);
                body.Append(fs.Text(dataBuf));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(r1));
                body.Append(" = insertvalue { ptr, i32, i32 } "u8);
                body.Append(fs.Text(r0));
                body.Append(", i32 "u8);
                body.AppendInt(valCount);
                body.Append(", 1\n  "u8);
                body.Append(fs.Text(r2));
                body.Append(" = insertvalue { ptr, i32, i32 } "u8);
                body.Append(fs.Text(r1));
                body.Append(", i32 "u8);
                body.AppendInt(valCount);
                body.Append(", 2\n"u8);
                AssignIntrinsicResult(in inst, r2, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecCount:
            case MirIntrinsic.VecCapacity:
            {
                int fldIdx = kind == MirIntrinsic.VecCount ? 1 : 2;
                ByteSlice res = ReadAggregateI32Field(valStart, valCount, plStart, plCount, "{ ptr, i32, i32 }"u8, fldIdx, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                AssignIntrinsicResult(in inst, res, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecPush:
            {
                ByteSlice itemVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                int elemType = types.Items[vecType].A;
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(elemType, in ast, in names, in types, ref elemLlvm);

                ByteSlice dpSlot = fs.NextTemp();
                ByteSlice cntSlot = fs.NextTemp();
                ByteSlice capSlot = fs.NextTemp();
                ByteSlice dataPtr = fs.NextTemp();
                ByteSlice cnt = fs.NextTemp();
                ByteSlice cap = fs.NextTemp();

                body.Append("  "u8);
                body.Append(fs.Text(dpSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 0\n  "u8);
                body.Append(fs.Text(cntSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 1\n  "u8);
                body.Append(fs.Text(capSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 2\n  "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(dpSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(cnt));
                body.Append(" = load i32, ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(cap));
                body.Append(" = load i32, ptr "u8);
                body.Append(fs.Text(capSlot));
                body.AppendAscii((byte)'\n');

                ByteSlice isFull = fs.NextTemp();
                ByteSlice curEntryLbl = fs.NextLabel("vec_check"u8);
                ByteSlice growLbl = fs.NextLabel("vec_grow"u8);
                ByteSlice grownDoneLbl = fs.NextLabel("vec_grown"u8);
                ByteSlice storeLbl = fs.NextLabel("vec_store"u8);

                body.Append("  br label %"u8);
                body.Append(fs.Text(curEntryLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(curEntryLbl, ref body);
                body.Append("  "u8);
                body.Append(fs.Text(isFull));
                body.Append(" = icmp eq i32 "u8);
                body.Append(fs.Text(cnt));
                body.Append(", "u8);
                body.Append(fs.Text(cap));
                body.Append("\n  br i1 "u8);
                body.Append(fs.Text(isFull));
                body.Append(", label %"u8);
                body.Append(fs.Text(growLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(storeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(growLbl, ref body);
                ByteSlice atMax = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(atMax));
                body.Append(" = icmp sge i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 2147483591\n"u8);
                EmitPanicIfTrue(atMax, "Vec capacity overflow"u8, ref ectx, ref fs, ref body);

                ByteSlice capZero = fs.NextTemp();
                ByteSlice overHalf = fs.NextTemp();
                ByteSlice doubled = fs.NextTemp();
                ByteSlice nonZeroGrown = fs.NextTemp();
                ByteSlice nextCap = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(capZero));
                body.Append(" = icmp eq i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(overHalf));
                body.Append(" = icmp sgt i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 1073741795\n  "u8);
                body.Append(fs.Text(doubled));
                body.Append(" = shl i32 "u8);
                body.Append(fs.Text(cap));
                body.Append(", 1\n  "u8);
                body.Append(fs.Text(nonZeroGrown));
                body.Append(" = select i1 "u8);
                body.Append(fs.Text(overHalf));
                body.Append(", i32 2147483591, i32 "u8);
                body.Append(fs.Text(doubled));
                body.Append("\n  "u8);
                body.Append(fs.Text(nextCap));
                body.Append(" = select i1 "u8);
                body.Append(fs.Text(capZero));
                body.Append(", i32 4, i32 "u8);
                body.Append(fs.Text(nonZeroGrown));
                body.AppendAscii((byte)'\n');

                ByteSlice elemSz = EmitSizeOf(elemLlvm.AsUtf8(), ref fs, ref body);
                ByteSlice nextCap64 = fs.NextTemp();
                ByteSlice mulPair = fs.NextTemp();
                ByteSlice rawBytes = fs.NextTemp();
                ByteSlice mulOvf = fs.NextTemp();
                ByteSlice allocBytes = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(nextCap64));
                body.Append(" = zext i32 "u8);
                body.Append(fs.Text(nextCap));
                body.Append(" to i64\n  "u8);
                body.Append(fs.Text(mulPair));
                body.Append(" = call { i64, i1 } @llvm.umul.with.overflow.i64(i64 "u8);
                body.Append(fs.Text(nextCap64));
                body.Append(", i64 "u8);
                body.Append(fs.Text(elemSz));
                body.Append(")\n  "u8);
                body.Append(fs.Text(rawBytes));
                body.Append(" = extractvalue { i64, i1 } "u8);
                body.Append(fs.Text(mulPair));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(mulOvf));
                body.Append(" = extractvalue { i64, i1 } "u8);
                body.Append(fs.Text(mulPair));
                body.Append(", 1\n"u8);
                EmitPanicIfTrue(mulOvf, "Vec capacity overflow"u8, ref ectx, ref fs, ref body);
                EmitClampMinOne(rawBytes, allocBytes, ref fs, ref body);
                ByteSlice grownBuf = EmitCheckedRealloc(dataPtr, allocBytes, ref ectx, ref fs, ref body);

                body.Append("  br label %"u8);
                body.Append(fs.Text(grownDoneLbl));
                body.AppendAscii((byte)'\n');
                fs.EmitLabel(grownDoneLbl, ref body);
                body.Append("  store ptr "u8);
                body.Append(fs.Text(grownBuf));
                body.Append(", ptr "u8);
                body.Append(fs.Text(dpSlot));
                body.Append("\n  store i32 "u8);
                body.Append(fs.Text(nextCap));
                body.Append(", ptr "u8);
                body.Append(fs.Text(capSlot));
                body.Append("\n  br label %"u8);
                body.Append(fs.Text(storeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(storeLbl, ref body);
                ByteSlice activeBuf = fs.NextTemp();
                ByteSlice idx64 = fs.NextTemp();
                ByteSlice elemPtr = fs.NextTemp();
                ByteSlice nextCnt = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(activeBuf));
                body.Append(" = phi ptr [ "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(", %"u8);
                body.Append(fs.Text(curEntryLbl));
                body.Append(" ], [ "u8);
                body.Append(fs.Text(grownBuf));
                body.Append(", %"u8);
                body.Append(fs.Text(grownDoneLbl));
                body.Append(" ]\n  "u8);
                body.Append(fs.Text(idx64));
                body.Append(" = zext i32 "u8);
                body.Append(fs.Text(cnt));
                body.Append(" to i64\n  "u8);
                body.Append(fs.Text(elemPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(elemLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(activeBuf));
                body.Append(", i64 "u8);
                body.Append(fs.Text(idx64));
                body.Append("\n  store "u8);
                body.Append(elemLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(itemVal));
                body.Append(", ptr "u8);
                body.Append(fs.Text(elemPtr));
                body.Append("\n  "u8);
                body.Append(fs.Text(nextCnt));
                body.Append(" = add i32 "u8);
                body.Append(fs.Text(cnt));
                body.Append(", 1\n  store i32 "u8);
                body.Append(fs.Text(nextCnt));
                body.Append(", ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.AppendAscii((byte)'\n');
                break;
            }

            case MirIntrinsic.VecPop:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                int elemType = types.Items[vecType].A;
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(elemType, in ast, in names, in types, ref elemLlvm);
                var optLlvm = new StringBuf(64);
                AppendValueLlvmType(inst.TypeId, in ast, in names, in types, ref optLlvm);

                ByteSlice dpSlot = fs.NextTemp();
                ByteSlice cntSlot = fs.NextTemp();
                ByteSlice dataPtr = fs.NextTemp();
                ByteSlice cnt = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(dpSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 0\n  "u8);
                body.Append(fs.Text(cntSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 1\n  "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(dpSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(cnt));
                body.Append(" = load i32, ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.AppendAscii((byte)'\n');

                ByteSlice isEmpty = fs.NextTemp();
                ByteSlice emptyLbl = fs.NextLabel("vec_pop_empty"u8);
                ByteSlice popLbl = fs.NextLabel("vec_pop_some"u8);
                ByteSlice mergeLbl = fs.NextLabel("vec_pop_merge"u8);

                body.Append("  "u8);
                body.Append(fs.Text(isEmpty));
                body.Append(" = icmp eq i32 "u8);
                body.Append(fs.Text(cnt));
                body.Append(", 0\n  br i1 "u8);
                body.Append(fs.Text(isEmpty));
                body.Append(", label %"u8);
                body.Append(fs.Text(emptyLbl));
                body.Append(", label %"u8);
                body.Append(fs.Text(popLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(emptyLbl, ref body);
                body.Append("  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(popLbl, ref body);
                ByteSlice newCnt = fs.NextTemp();
                ByteSlice idx64 = fs.NextTemp();
                ByteSlice elemPtr = fs.NextTemp();
                ByteSlice popped = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(newCnt));
                body.Append(" = sub i32 "u8);
                body.Append(fs.Text(cnt));
                body.Append(", 1\n  store i32 "u8);
                body.Append(fs.Text(newCnt));
                body.Append(", ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(idx64));
                body.Append(" = zext i32 "u8);
                body.Append(fs.Text(newCnt));
                body.Append(" to i64\n  "u8);
                body.Append(fs.Text(elemPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(elemLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(", i64 "u8);
                body.Append(fs.Text(idx64));
                body.Append("\n  "u8);
                body.Append(fs.Text(popped));
                body.Append(" = load "u8);
                body.Append(elemLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(elemPtr));
                body.AppendAscii((byte)'\n');
                if (RequiresCleanup(elemType, in ast, in types))
                {
                    body.Append("  store "u8);
                    body.Append(elemLlvm.AsUtf8());
                    body.AppendAscii((byte)' ');
                    body.Append(ZeroLiteralForLlvmType(elemLlvm.AsUtf8()));
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(elemPtr));
                    body.AppendAscii((byte)'\n');
                }

                ByteSlice s0 = fs.NextTemp();
                ByteSlice s1 = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(s0));
                body.Append(" = insertvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(" zeroinitializer, i1 true, 0\n  "u8);
                body.Append(fs.Text(s1));
                body.Append(" = insertvalue "u8);
                body.Append(optLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(s0));
                body.Append(", "u8);
                body.Append(elemLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(popped));
                body.Append(", 1\n  br label %"u8);
                body.Append(fs.Text(mergeLbl));
                body.AppendAscii((byte)'\n');

                fs.EmitLabel(mergeLbl, ref body);
                ByteSlice resOpt = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(resOpt));
                body.Append(" = phi "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(" [ zeroinitializer, %"u8);
                body.Append(fs.Text(emptyLbl));
                body.Append(" ], [ "u8);
                body.Append(fs.Text(s1));
                body.Append(", %"u8);
                body.Append(fs.Text(popLbl));
                body.Append(" ]\n"u8);
                AssignIntrinsicResult(in inst, resOpt, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecClear:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                ByteSlice cntSlot = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(cntSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 1\n  store i32 0, ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.AppendAscii((byte)'\n');
                break;
            }

            case MirIntrinsic.VecTruncate:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                int op0 = mir.ExtraLists[valStart];
                ByteSlice newLen = EnsureI32(EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op0].TypeId, in ast, in names, in types, ref fs, ref body);
                ByteSlice cntSlot = fs.NextTemp();
                ByteSlice cnt = fs.NextTemp();
                ByteSlice bad = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(cntSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 1\n  "u8);
                body.Append(fs.Text(cnt));
                body.Append(" = load i32, ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(bad));
                body.Append(" = icmp ugt i32 "u8);
                body.Append(fs.Text(newLen));
                body.Append(", "u8);
                body.Append(fs.Text(cnt));
                body.AppendAscii((byte)'\n');
                EmitPanicIfTrue(bad, "truncate out of bounds"u8, ref ectx, ref fs, ref body);
                body.Append("  store i32 "u8);
                body.Append(fs.Text(newLen));
                body.Append(", ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.AppendAscii((byte)'\n');
                break;
            }

            case MirIntrinsic.VecReserve:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(types.Items[vecType].A, in ast, in names, in types, ref elemLlvm);
                int op0 = mir.ExtraLists[valStart];
                ByteSlice addVal = EnsureI32(EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op0].TypeId, in ast, in names, in types, ref fs, ref body);
                ByteSlice elemSz = EmitSizeOf(elemLlvm.AsUtf8(), ref fs, ref body);
                body.Append("  call void @__vib_vec_reserve(ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 "u8);
                body.Append(fs.Text(addVal));
                body.Append(", i64 "u8);
                body.Append(fs.Text(elemSz));
                body.Append(")\n"u8);
                break;
            }

            case MirIntrinsic.VecExtend:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(types.Items[vecType].A, in ast, in names, in types, ref elemLlvm);
                ByteSlice spanVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice sPtr = fs.NextTemp();
                ByteSlice sLen = fs.NextTemp();
                ByteSlice elemSz = EmitSizeOf(elemLlvm.AsUtf8(), ref fs, ref body);
                body.Append("  "u8);
                body.Append(fs.Text(sPtr));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(spanVal));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(sLen));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(spanVal));
                body.Append(", 1\n  call void @__vib_vec_extend(ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", ptr "u8);
                body.Append(fs.Text(sPtr));
                body.Append(", i32 "u8);
                body.Append(fs.Text(sLen));
                body.Append(", i64 "u8);
                body.Append(fs.Text(elemSz));
                body.Append(")\n"u8);
                break;
            }

            case MirIntrinsic.VecResize:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(types.Items[vecType].A, in ast, in names, in types, ref elemLlvm);
                int op0 = mir.ExtraLists[valStart];
                ByteSlice newLen = EnsureI32(EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op0].TypeId, in ast, in names, in types, ref fs, ref body);
                ByteSlice fillVal = EmitOperand(mir.ExtraLists[valStart + 1], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                ByteSlice fillSlot = fs.AllocTempSlot(elemLlvm.AsUtf8());
                body.Append("  store "u8);
                body.Append(elemLlvm.AsUtf8());
                body.AppendAscii((byte)' ');
                body.Append(fs.Text(fillVal));
                body.Append(", ptr "u8);
                body.Append(fs.Text(fillSlot));
                body.AppendAscii((byte)'\n');
                ByteSlice elemSz = EmitSizeOf(elemLlvm.AsUtf8(), ref fs, ref body);
                body.Append("  call void @__vib_vec_resize(ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 "u8);
                body.Append(fs.Text(newLen));
                body.Append(", ptr "u8);
                body.Append(fs.Text(fillSlot));
                body.Append(", i64 "u8);
                body.Append(fs.Text(elemSz));
                body.Append(")\n"u8);
                break;
            }

            case MirIntrinsic.VecIndexRef:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(types.Items[vecType].A, in ast, in names, in types, ref elemLlvm);
                int op0 = mir.ExtraLists[valStart];
                ByteSlice idxVal = EnsureI32(EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op0].TypeId, in ast, in names, in types, ref fs, ref body);

                ByteSlice dpSlot = fs.NextTemp();
                ByteSlice cntSlot = fs.NextTemp();
                ByteSlice dataPtr = fs.NextTemp();
                ByteSlice cnt = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(dpSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 0\n  "u8);
                body.Append(fs.Text(cntSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 1\n  "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(dpSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(cnt));
                body.Append(" = load i32, ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.AppendAscii((byte)'\n');

                EmitBoundsCheck(idxVal, cnt, "index out of bounds"u8, ref ectx, ref fs, ref body);
                ByteSlice idx64 = fs.NextTemp();
                ByteSlice elemPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(idx64));
                body.Append(" = zext i32 "u8);
                body.Append(fs.Text(idxVal));
                body.Append(" to i64\n  "u8);
                body.Append(fs.Text(elemPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(elemLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(", i64 "u8);
                body.Append(fs.Text(idx64));
                body.AppendAscii((byte)'\n');
                StoreRefResult(inst.RefDestLocalId, inst.DestPlaceId, elemPtr, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecSliceMut:
            case MirIntrinsic.VecReadOnlySlice:
            {
                ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int vecType);
                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(types.Items[vecType].A, in ast, in names, in types, ref elemLlvm);
                int op0 = mir.ExtraLists[valStart];
                int op1 = mir.ExtraLists[valStart + 1];
                ByteSlice start = EnsureI32(EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op0].TypeId, in ast, in names, in types, ref fs, ref body);
                ByteSlice length = EnsureI32(EmitOperand(op1, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op1].TypeId, in ast, in names, in types, ref fs, ref body);

                ByteSlice dpSlot = fs.NextTemp();
                ByteSlice cntSlot = fs.NextTemp();
                ByteSlice dataPtr = fs.NextTemp();
                ByteSlice cnt = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(dpSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 0\n  "u8);
                body.Append(fs.Text(cntSlot));
                body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                body.Append(fs.Text(vecPtr));
                body.Append(", i32 0, i32 1\n  "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(dpSlot));
                body.Append("\n  "u8);
                body.Append(fs.Text(cnt));
                body.Append(" = load i32, ptr "u8);
                body.Append(fs.Text(cntSlot));
                body.AppendAscii((byte)'\n');

                ByteSlice spanVal = EmitSliceFromPtrAndLen(dataPtr, cnt, start, length, elemLlvm.AsUtf8(), ref ectx, ref fs, ref body);
                AssignIntrinsicResult(in inst, spanVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.VecAsSpan:
            case MirIntrinsic.VecAsReadOnlySpan:
            {
                ByteSlice dataPtr;
                ByteSlice cnt;
                if (plCount > 0)
                {
                    ByteSlice vecPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                    ByteSlice dpSlot = fs.NextTemp();
                    ByteSlice cntSlot = fs.NextTemp();
                    dataPtr = fs.NextTemp();
                    cnt = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(dpSlot));
                    body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                    body.Append(fs.Text(vecPtr));
                    body.Append(", i32 0, i32 0\n  "u8);
                    body.Append(fs.Text(cntSlot));
                    body.Append(" = getelementptr inbounds { ptr, i32, i32 }, ptr "u8);
                    body.Append(fs.Text(vecPtr));
                    body.Append(", i32 0, i32 1\n  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = load ptr, ptr "u8);
                    body.Append(fs.Text(dpSlot));
                    body.Append("\n  "u8);
                    body.Append(fs.Text(cnt));
                    body.Append(" = load i32, ptr "u8);
                    body.Append(fs.Text(cntSlot));
                    body.AppendAscii((byte)'\n');
                }
                else
                {
                    ByteSlice vecVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    dataPtr = fs.NextTemp();
                    cnt = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = extractvalue { ptr, i32, i32 } "u8);
                    body.Append(fs.Text(vecVal));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(cnt));
                    body.Append(" = extractvalue { ptr, i32, i32 } "u8);
                    body.Append(fs.Text(vecVal));
                    body.Append(", 1\n"u8);
                }

                ByteSlice s0 = fs.NextTemp();
                ByteSlice s1 = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(s0));
                body.Append(" = insertvalue { ptr, i32 } poison, ptr "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(s1));
                body.Append(" = insertvalue { ptr, i32 } "u8);
                body.Append(fs.Text(s0));
                body.Append(", i32 "u8);
                body.Append(fs.Text(cnt));
                body.Append(", 1\n"u8);
                AssignIntrinsicResult(in inst, s1, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.SpanLength:
            {
                ByteSlice len = ReadAggregateI32Field(valStart, valCount, plStart, plCount, "{ ptr, i32 }"u8, 1, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                AssignIntrinsicResult(in inst, len, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.SpanIndexRef:
            {
                ByteSlice dataPtr;
                ByteSlice len;
                int elemType;
                ByteSlice idxVal;
                if (plCount > 0)
                {
                    ByteSlice spanPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int spanTy);
                    elemType = types.Items[spanTy].A;
                    ByteSlice spanVal = fs.NextTemp();
                    dataPtr = fs.NextTemp();
                    len = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(" = load { ptr, i32 }, ptr "u8);
                    body.Append(fs.Text(spanPtr));
                    body.Append("\n  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(len));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 1\n"u8);
                    int op0 = mir.ExtraLists[valStart];
                    idxVal = EnsureI32(EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op0].TypeId, in ast, in names, in types, ref fs, ref body);
                }
                else
                {
                    int op0 = mir.ExtraLists[valStart];
                    int spanTy = mir.Operands[op0].TypeId;
                    elemType = types.Items[spanTy].A;
                    ByteSlice spanVal = EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    dataPtr = fs.NextTemp();
                    len = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(len));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 1\n"u8);
                    int op1 = mir.ExtraLists[valStart + 1];
                    idxVal = EnsureI32(EmitOperand(op1, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[op1].TypeId, in ast, in names, in types, ref fs, ref body);
                }

                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(elemType, in ast, in names, in types, ref elemLlvm);
                EmitBoundsCheck(idxVal, len, "index out of bounds"u8, ref ectx, ref fs, ref body);
                ByteSlice idx64 = fs.NextTemp();
                ByteSlice elemPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(idx64));
                body.Append(" = zext i32 "u8);
                body.Append(fs.Text(idxVal));
                body.Append(" to i64\n  "u8);
                body.Append(fs.Text(elemPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(elemLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(dataPtr));
                body.Append(", i64 "u8);
                body.Append(fs.Text(idx64));
                body.AppendAscii((byte)'\n');
                StoreRefResult(inst.RefDestLocalId, inst.DestPlaceId, elemPtr, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.SpanSlice:
            {
                ByteSlice dataPtr;
                ByteSlice len;
                int elemType;
                int argOffset;
                if (plCount > 0)
                {
                    ByteSlice spanPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int spanTy);
                    elemType = types.Items[spanTy].A;
                    ByteSlice spanVal = fs.NextTemp();
                    dataPtr = fs.NextTemp();
                    len = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(" = load { ptr, i32 }, ptr "u8);
                    body.Append(fs.Text(spanPtr));
                    body.Append("\n  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(len));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 1\n"u8);
                    argOffset = 0;
                }
                else
                {
                    int op0 = mir.ExtraLists[valStart];
                    int spanTy = mir.Operands[op0].TypeId;
                    elemType = types.Items[spanTy].A;
                    ByteSlice spanVal = EmitOperand(op0, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    dataPtr = fs.NextTemp();
                    len = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(len));
                    body.Append(" = extractvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(", 1\n"u8);
                    argOffset = 1;
                }

                var elemLlvm = new StringBuf(64);
                AppendValueLlvmType(elemType, in ast, in names, in types, ref elemLlvm);
                int startOp = mir.ExtraLists[valStart + argOffset];
                ByteSlice start = EnsureI32(EmitOperand(startOp, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[startOp].TypeId, in ast, in names, in types, ref fs, ref body);

                ByteSlice sliceVal;
                if (valCount - argOffset >= 2)
                {
                    int lenOp = mir.ExtraLists[valStart + argOffset + 1];
                    ByteSlice sliceLen = EnsureI32(EmitOperand(lenOp, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[lenOp].TypeId, in ast, in names, in types, ref fs, ref body);
                    sliceVal = EmitSliceFromPtrAndLen(dataPtr, len, start, sliceLen, elemLlvm.AsUtf8(), ref ectx, ref fs, ref body);
                }
                else
                {
                    ByteSlice badStart = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(badStart));
                    body.Append(" = icmp ugt i32 "u8);
                    body.Append(fs.Text(start));
                    body.Append(", "u8);
                    body.Append(fs.Text(len));
                    body.AppendAscii((byte)'\n');
                    EmitPanicIfTrue(badStart, "slice out of bounds"u8, ref ectx, ref fs, ref body);

                    ByteSlice remLen = fs.NextTemp();
                    ByteSlice start64 = fs.NextTemp();
                    ByteSlice subPtr = fs.NextTemp();
                    ByteSlice s0 = fs.NextTemp();
                    ByteSlice s1 = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(remLen));
                    body.Append(" = sub i32 "u8);
                    body.Append(fs.Text(len));
                    body.Append(", "u8);
                    body.Append(fs.Text(start));
                    body.Append("\n  "u8);
                    body.Append(fs.Text(start64));
                    body.Append(" = zext i32 "u8);
                    body.Append(fs.Text(start));
                    body.Append(" to i64\n  "u8);
                    body.Append(fs.Text(subPtr));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(elemLlvm.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(", i64 "u8);
                    body.Append(fs.Text(start64));
                    body.Append("\n  "u8);
                    body.Append(fs.Text(s0));
                    body.Append(" = insertvalue { ptr, i32 } poison, ptr "u8);
                    body.Append(fs.Text(subPtr));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(s1));
                    body.Append(" = insertvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(s0));
                    body.Append(", i32 "u8);
                    body.Append(fs.Text(remLen));
                    body.Append(", 1\n"u8);
                    sliceVal = s1;
                }
                AssignIntrinsicResult(in inst, sliceVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.SpanToReadOnly:
            case MirIntrinsic.Utf8ViewFromSpan:
            case MirIntrinsic.Utf8ViewBytes:
            {
                ByteSlice spanVal;
                if (plCount > 0)
                {
                    ByteSlice spanPtr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
                    spanVal = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(spanVal));
                    body.Append(" = load { ptr, i32 }, ptr "u8);
                    body.Append(fs.Text(spanPtr));
                    body.AppendAscii((byte)'\n');
                }
                else
                {
                    spanVal = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                AssignIntrinsicResult(in inst, spanVal, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }

            case MirIntrinsic.StringAsUtf8:
            {
                int opId = mir.ExtraLists[valStart];
                MirOperand sop = mir.Operands[opId];
                if (sop.Kind == MirOpKinds.StrConst)
                {
                    ByteSlice folded;
                    if (sop.PlaceOrLitIdx < 0)
                    {
                        folded = fs.InternSsa("{ ptr @.b_empty, i32 0 }"u8);
                    }
                    else
                    {
                        ReadOnlySpan<byte> u8Bytes = ta.GetLit(sop.PlaceOrLitIdx);
                        if (u8Bytes.Length == 0)
                        {
                            folded = fs.InternSsa("{ ptr @.b_empty, i32 0 }"u8);
                        }
                        else
                        {
                            int bId = ectx.InternUtf8Lit(u8Bytes);
                            var sb = new StringBuf(40);
                            sb.Append("{ ptr @.b"u8);
                            sb.AppendInt(bId);
                            sb.Append(", i32 "u8);
                            sb.AppendInt(u8Bytes.Length);
                            sb.Append(" }"u8);
                            folded = fs.InternSsa(sb.AsUtf8());
                        }
                    }
                    AssignIntrinsicResult(in inst, folded, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                else
                {
                    ByteSlice sVal = EmitOperand(opId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                    ByteSlice pTmp = fs.NextTemp();
                    ByteSlice l64 = fs.NextTemp();
                    ByteSlice badLen = fs.NextTemp();
                    ByteSlice l32 = fs.NextTemp();
                    ByteSlice pk0 = fs.NextTemp();
                    ByteSlice pk1 = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(pTmp));
                    body.Append(" = extractvalue { ptr, i64 } "u8);
                    body.Append(fs.Text(sVal));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(l64));
                    body.Append(" = extractvalue { ptr, i64 } "u8);
                    body.Append(fs.Text(sVal));
                    body.Append(", 1\n  "u8);
                    body.Append(fs.Text(badLen));
                    body.Append(" = icmp ugt i64 "u8);
                    body.Append(fs.Text(l64));
                    body.Append(", 2147483647\n"u8);
                    EmitPanicIfTrue(badLen, "UTF-8 string exceeds ReadOnlySpan<byte> length limit"u8, ref ectx, ref fs, ref body);
                    body.Append("  "u8);
                    body.Append(fs.Text(l32));
                    body.Append(" = trunc i64 "u8);
                    body.Append(fs.Text(l64));
                    body.Append(" to i32\n  "u8);
                    body.Append(fs.Text(pk0));
                    body.Append(" = insertvalue { ptr, i32 } poison, ptr "u8);
                    body.Append(fs.Text(pTmp));
                    body.Append(", 0\n  "u8);
                    body.Append(fs.Text(pk1));
                    body.Append(" = insertvalue { ptr, i32 } "u8);
                    body.Append(fs.Text(pk0));
                    body.Append(", i32 "u8);
                    body.Append(fs.Text(l32));
                    body.Append(", 1\n"u8);
                    AssignIntrinsicResult(in inst, pk1, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                }
                break;
            }

            case MirIntrinsic.Utf8Scan:
            {
                ByteSlice srcSpan = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                int posOp = mir.ExtraLists[valStart + 1];
                int modeOp = mir.ExtraLists[valStart + 2];
                ByteSlice posI32 = EnsureI32(EmitOperand(posOp, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[posOp].TypeId, in ast, in names, in types, ref fs, ref body);
                ByteSlice modeI32 = EnsureI32(EmitOperand(modeOp, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[modeOp].TypeId, in ast, in names, in types, ref fs, ref body);
                ByteSlice spPtr = fs.NextTemp();
                ByteSlice spLen = fs.NextTemp();
                ByteSlice scanRet = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(spPtr));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(srcSpan));
                body.Append(", 0\n  "u8);
                body.Append(fs.Text(spLen));
                body.Append(" = extractvalue { ptr, i32 } "u8);
                body.Append(fs.Text(srcSpan));
                body.Append(", 1\n  "u8);
                body.Append(fs.Text(scanRet));
                body.Append(" = call i64 @__vib_utf8_scan(ptr "u8);
                body.Append(fs.Text(spPtr));
                body.Append(", i32 "u8);
                body.Append(fs.Text(spLen));
                body.Append(", i32 "u8);
                body.Append(fs.Text(posI32));
                body.Append(", i32 "u8);
                body.Append(fs.Text(modeI32));
                body.Append(")\n"u8);
                AssignIntrinsicResult(in inst, scanRet, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;
            }
        }
    }

    private static ByteSlice ReadAggregateI32Field(
        int valStart,
        int valCount,
        int plStart,
        int plCount,
        ReadOnlySpan<byte> aggLlvm,
        int fieldIndex,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        _ = valCount;
        if (plCount > 0)
        {
            ByteSlice addr = EmitPlaceAddress(mir.ExtraLists[plStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
            ByteSlice fldPtr = fs.NextTemp();
            ByteSlice res = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(fldPtr));
            body.Append(" = getelementptr inbounds "u8);
            body.Append(aggLlvm);
            body.Append(", ptr "u8);
            body.Append(fs.Text(addr));
            body.Append(", i32 0, i32 "u8);
            body.AppendInt(fieldIndex);
            body.Append("\n  "u8);
            body.Append(fs.Text(res));
            body.Append(" = load i32, ptr "u8);
            body.Append(fs.Text(fldPtr));
            body.AppendAscii((byte)'\n');
            return res;
        }
        else
        {
            ByteSlice val = EmitOperand(mir.ExtraLists[valStart], in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            ByteSlice res = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(res));
            body.Append(" = extractvalue "u8);
            body.Append(aggLlvm);
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(val));
            body.Append(", "u8);
            body.AppendInt(fieldIndex);
            body.AppendAscii((byte)'\n');
            return res;
        }
    }

    private static ByteSlice EmitSliceFromPtrAndLen(
        ByteSlice dataPtr,
        ByteSlice totalLen,
        ByteSlice start,
        ByteSlice sliceLen,
        ReadOnlySpan<byte> elemLlvm,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice bad1 = fs.NextTemp();
        ByteSlice rem = fs.NextTemp();
        ByteSlice bad2 = fs.NextTemp();
        ByteSlice oob = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(bad1));
        body.Append(" = icmp ugt i32 "u8);
        body.Append(fs.Text(start));
        body.Append(", "u8);
        body.Append(fs.Text(totalLen));
        body.Append("\n  "u8);
        body.Append(fs.Text(rem));
        body.Append(" = sub i32 "u8);
        body.Append(fs.Text(totalLen));
        body.Append(", "u8);
        body.Append(fs.Text(start));
        body.Append("\n  "u8);
        body.Append(fs.Text(bad2));
        body.Append(" = icmp ugt i32 "u8);
        body.Append(fs.Text(sliceLen));
        body.Append(", "u8);
        body.Append(fs.Text(rem));
        body.Append("\n  "u8);
        body.Append(fs.Text(oob));
        body.Append(" = or i1 "u8);
        body.Append(fs.Text(bad1));
        body.Append(", "u8);
        body.Append(fs.Text(bad2));
        body.AppendAscii((byte)'\n');
        EmitPanicIfTrue(oob, "slice out of bounds"u8, ref ectx, ref fs, ref body);

        ByteSlice start64 = fs.NextTemp();
        ByteSlice subPtr = fs.NextTemp();
        ByteSlice s0 = fs.NextTemp();
        ByteSlice s1 = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(start64));
        body.Append(" = zext i32 "u8);
        body.Append(fs.Text(start));
        body.Append(" to i64\n  "u8);
        body.Append(fs.Text(subPtr));
        body.Append(" = getelementptr inbounds "u8);
        body.Append(elemLlvm);
        body.Append(", ptr "u8);
        body.Append(fs.Text(dataPtr));
        body.Append(", i64 "u8);
        body.Append(fs.Text(start64));
        body.Append("\n  "u8);
        body.Append(fs.Text(s0));
        body.Append(" = insertvalue { ptr, i32 } poison, ptr "u8);
        body.Append(fs.Text(subPtr));
        body.Append(", 0\n  "u8);
        body.Append(fs.Text(s1));
        body.Append(" = insertvalue { ptr, i32 } "u8);
        body.Append(fs.Text(s0));
        body.Append(", i32 "u8);
        body.Append(fs.Text(sliceLen));
        body.Append(", 1\n"u8);
        return s1;
    }

    private static void EmitTerminator(
        in MirTerm term,
        in MirFunc fn,
        in FuncInstance fi,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        switch (term.Kind)
        {
            case MirTermKind.Goto:
                body.Append("  br label %bb_"u8);
                body.AppendInt(term.A);
                body.AppendAscii((byte)'\n');
                break;

            case MirTermKind.Branch:
            {
                ByteSlice cond = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                body.Append("  br i1 "u8);
                body.Append(fs.Text(cond));
                body.Append(", label %bb_"u8);
                body.AppendInt(term.B);
                body.Append(", label %bb_"u8);
                body.AppendInt(term.C);
                body.AppendAscii((byte)'\n');
                break;
            }

            case MirTermKind.MatchOption:
                EmitMatchOptionTerm(in term, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirTermKind.MatchResult:
                EmitMatchResultTerm(in term, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirTermKind.GuardResultIsErr:
                EmitGuardResultIsErrTerm(in term, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirTermKind.GuardOptionIsNone:
                EmitGuardOptionIsNoneTerm(in term, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            case MirTermKind.Return:
                EmitReturnTerm(in term, in fn, in fi, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
                break;

            default:
                body.Append("  unreachable\n"u8);
                break;
        }
    }

    private static void BindMatchedPayloadFromPointer(
        int bindLocalId,
        ByteSlice payloadPtr,
        int payloadTypeId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref FnEmitState fs, ref StringBuf body)
    {
        LocalSlot sl = fs.Slots[bindLocalId];
        if (sl.IsRefSlot)
        {
            body.Append("  store ptr "u8);
            body.Append(fs.Text(payloadPtr));
            body.Append(", ptr %loc_"u8);
            body.AppendInt(bindLocalId);
            body.AppendAscii((byte)'\n');
            return;
        }

        var payloadLlvm = new StringBuf(64);
        AppendValueLlvmType(payloadTypeId, in ast, in names, in types, ref payloadLlvm);
        ByteSlice loaded = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(loaded));
        body.Append(" = load "u8);
        body.Append(payloadLlvm.AsUtf8());
        body.Append(", ptr "u8);
        body.Append(fs.Text(payloadPtr));
        body.Append("\n  store "u8);
        AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref body);
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(loaded));
        body.Append(", ptr %loc_"u8);
        body.AppendInt(bindLocalId);
        body.AppendAscii((byte)'\n');
        if (sl.HasDropFlag)
        {
            body.Append("  store i1 false, ptr %drop_"u8);
            body.AppendInt(bindLocalId);
            body.AppendAscii((byte)'\n');
        }
    }

    private static void BindConsumedPayloadToLocal(
        int bindLocalId,
        ByteSlice payloadSsa,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        LocalSlot sl = fs.Slots[bindLocalId];
        if (sl.HasDropFlag)
        {
            EmitDropLocalIfLive(bindLocalId, in ast, in names, in types, ref ectx, ref fs, ref body);
        }

        body.Append("  store "u8);
        AppendValueLlvmType(sl.StorageOrRefTypeId, in ast, in names, in types, ref body);
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(payloadSsa));
        body.Append(", ptr %loc_"u8);
        body.AppendInt(bindLocalId);
        body.AppendAscii((byte)'\n');

        if (sl.HasDropFlag)
        {
            body.Append("  store i1 true, ptr %drop_"u8);
            body.AppendInt(bindLocalId);
            body.AppendAscii((byte)'\n');
        }
    }

    private static void EmitMatchOptionTerm(
        in MirTerm term,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice someTramp = fs.NextLabel("match_some"u8);

        if ((term.Flags & 1) != 0)
        {
            ByteSlice addr = EmitPlaceAddress(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int sType);
            TypeInfo sti = types.Items[sType];
            int innerType = sti.A;
            ByteSlice optPtr;
            if (sti.Kind == TypeKinds.OptionRef || sti.Kind == TypeKinds.OptionMut)
            {
                optPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(optPtr));
                body.Append(" = load ptr, ptr "u8);
                body.Append(fs.Text(addr));
                body.AppendAscii((byte)'\n');
            }
            else
            {
                optPtr = addr;
            }

            var optLlvm = new StringBuf(64);
            optLlvm.Append("{ i1, "u8);
            AppendValueLlvmType(innerType, in ast, in names, in types, ref optLlvm);
            optLlvm.Append(" }"u8);

            ByteSlice tagPtr = fs.NextTemp();
            ByteSlice tag = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(tagPtr));
            body.Append(" = getelementptr inbounds "u8);
            body.Append(optLlvm.AsUtf8());
            body.Append(", ptr "u8);
            body.Append(fs.Text(optPtr));
            body.Append(", i32 0, i32 0\n  "u8);
            body.Append(fs.Text(tag));
            body.Append(" = load i1, ptr "u8);
            body.Append(fs.Text(tagPtr));
            body.Append("\n  br i1 "u8);
            body.Append(fs.Text(tag));
            body.Append(", label %"u8);
            body.Append(fs.Text(someTramp));
            body.Append(", label %bb_"u8);
            body.AppendInt(term.D);
            body.AppendAscii((byte)'\n');

            fs.EmitLabel(someTramp, ref body);
            if (term.B >= 0 && term.B < fs.Slots.Count)
            {
                ByteSlice valPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(valPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(optPtr));
                body.Append(", i32 0, i32 1\n"u8);
                BindMatchedPayloadFromPointer(term.B, valPtr, innerType, in ast, in names, in types, ref fs, ref body);
            }
            body.Append("  br label %bb_"u8);
            body.AppendInt(term.C);
            body.AppendAscii((byte)'\n');
            return;
        }

        int opType = mir.Operands[term.A].TypeId;
        TypeInfo oti = types.Items[opType];
        if ((term.Flags & 4) != 0 || oti.Kind == TypeKinds.OptionRef || oti.Kind == TypeKinds.OptionMut)
        {
            int innerType = oti.A;
            ByteSlice optPtr = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            var optLlvm = new StringBuf(64);
            optLlvm.Append("{ i1, "u8);
            AppendValueLlvmType(innerType, in ast, in names, in types, ref optLlvm);
            optLlvm.Append(" }"u8);

            ByteSlice tagPtr = fs.NextTemp();
            ByteSlice tag = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(tagPtr));
            body.Append(" = getelementptr inbounds "u8);
            body.Append(optLlvm.AsUtf8());
            body.Append(", ptr "u8);
            body.Append(fs.Text(optPtr));
            body.Append(", i32 0, i32 0\n  "u8);
            body.Append(fs.Text(tag));
            body.Append(" = load i1, ptr "u8);
            body.Append(fs.Text(tagPtr));
            body.Append("\n  br i1 "u8);
            body.Append(fs.Text(tag));
            body.Append(", label %"u8);
            body.Append(fs.Text(someTramp));
            body.Append(", label %bb_"u8);
            body.AppendInt(term.D);
            body.AppendAscii((byte)'\n');

            fs.EmitLabel(someTramp, ref body);
            if (term.B >= 0 && term.B < fs.Slots.Count)
            {
                ByteSlice valPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(valPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(optLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(optPtr));
                body.Append(", i32 0, i32 1\n"u8);
                BindMatchedPayloadFromPointer(term.B, valPtr, innerType, in ast, in names, in types, ref fs, ref body);
            }
            body.Append("  br label %bb_"u8);
            body.AppendInt(term.C);
            body.AppendAscii((byte)'\n');
            return;
        }

        {
            int innerType = oti.A;
            var optLlvm = new StringBuf(64);
            AppendValueLlvmType(opType, in ast, in names, in types, ref optLlvm);
            ByteSlice optVal = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            ByteSlice tag = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(tag));
            body.Append(" = extractvalue "u8);
            body.Append(optLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(optVal));
            body.Append(", 0\n  br i1 "u8);
            body.Append(fs.Text(tag));
            body.Append(", label %"u8);
            body.Append(fs.Text(someTramp));
            body.Append(", label %bb_"u8);
            body.AppendInt(term.D);
            body.AppendAscii((byte)'\n');

            fs.EmitLabel(someTramp, ref body);
            ByteSlice payload = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(payload));
            body.Append(" = extractvalue "u8);
            body.Append(optLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(optVal));
            body.Append(", 1\n"u8);
            if (term.B >= 0 && term.B < fs.Slots.Count)
            {
                BindConsumedPayloadToLocal(term.B, payload, in ast, in names, in types, ref ectx, ref fs, ref body);
            }
            else
            {
                SpillAndDropValueIfNeeded(innerType, payload, in ast, in names, in types, ref ectx, ref fs, ref body);
            }
            body.Append("  br label %bb_"u8);
            body.AppendInt(term.C);
            body.AppendAscii((byte)'\n');
        }
    }

    private static void EmitMatchResultTerm(
        in MirTerm term,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice okTramp = fs.NextLabel("match_ok"u8);
        ByteSlice errTramp = fs.NextLabel("match_err"u8);

        if ((term.Flags & 1) != 0)
        {
            ByteSlice resPtr = EmitPlaceAddress(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out int sType);
            TypeInfo rti = types.Items[sType];
            var resLlvm = new StringBuf(64);
            AppendValueLlvmType(sType, in ast, in names, in types, ref resLlvm);

            ByteSlice tagPtr = fs.NextTemp();
            ByteSlice tag = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(tagPtr));
            body.Append(" = getelementptr inbounds "u8);
            body.Append(resLlvm.AsUtf8());
            body.Append(", ptr "u8);
            body.Append(fs.Text(resPtr));
            body.Append(", i32 0, i32 0\n  "u8);
            body.Append(fs.Text(tag));
            body.Append(" = load i1, ptr "u8);
            body.Append(fs.Text(tagPtr));
            body.Append("\n  br i1 "u8);
            body.Append(fs.Text(tag));
            body.Append(", label %"u8);
            body.Append(fs.Text(okTramp));
            body.Append(", label %"u8);
            body.Append(fs.Text(errTramp));
            body.AppendAscii((byte)'\n');

            fs.EmitLabel(okTramp, ref body);
            if (term.B >= 0 && term.B < fs.Slots.Count)
            {
                ByteSlice okPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(okPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(resLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(resPtr));
                body.Append(", i32 0, i32 1\n"u8);
                BindMatchedPayloadFromPointer(term.B, okPtr, rti.A, in ast, in names, in types, ref fs, ref body);
            }
            body.Append("  br label %bb_"u8);
            body.AppendInt(term.C);
            body.AppendAscii((byte)'\n');

            fs.EmitLabel(errTramp, ref body);
            if (term.D >= 0 && term.D < fs.Slots.Count)
            {
                ByteSlice errPtr = fs.NextTemp();
                body.Append("  "u8);
                body.Append(fs.Text(errPtr));
                body.Append(" = getelementptr inbounds "u8);
                body.Append(resLlvm.AsUtf8());
                body.Append(", ptr "u8);
                body.Append(fs.Text(resPtr));
                body.Append(", i32 0, i32 2\n"u8);
                BindMatchedPayloadFromPointer(term.D, errPtr, rti.B, in ast, in names, in types, ref fs, ref body);
            }
            body.Append("  br label %bb_"u8);
            body.AppendInt(term.E);
            body.AppendAscii((byte)'\n');
            return;
        }

        int rType = mir.Operands[term.A].TypeId;
        TypeInfo rti2 = types.Items[rType];
        var rLlvm = new StringBuf(64);
        AppendValueLlvmType(rType, in ast, in names, in types, ref rLlvm);
        ByteSlice rVal = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
        ByteSlice isOk = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(isOk));
        body.Append(" = extractvalue "u8);
        body.Append(rLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(rVal));
        body.Append(", 0\n  br i1 "u8);
        body.Append(fs.Text(isOk));
        body.Append(", label %"u8);
        body.Append(fs.Text(okTramp));
        body.Append(", label %"u8);
        body.Append(fs.Text(errTramp));
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(okTramp, ref body);
        ByteSlice okVal = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(okVal));
        body.Append(" = extractvalue "u8);
        body.Append(rLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(rVal));
        body.Append(", 1\n"u8);
        if (term.B >= 0 && term.B < fs.Slots.Count)
        {
            BindConsumedPayloadToLocal(term.B, okVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        else
        {
            SpillAndDropValueIfNeeded(rti2.A, okVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        body.Append("  br label %bb_"u8);
        body.AppendInt(term.C);
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(errTramp, ref body);
        ByteSlice errVal = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(errVal));
        body.Append(" = extractvalue "u8);
        body.Append(rLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(rVal));
        body.Append(", 2\n"u8);
        if (term.D >= 0 && term.D < fs.Slots.Count)
        {
            BindConsumedPayloadToLocal(term.D, errVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        else
        {
            SpillAndDropValueIfNeeded(rti2.B, errVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        body.Append("  br label %bb_"u8);
        body.AppendInt(term.E);
        body.AppendAscii((byte)'\n');
    }

    private static void EmitGuardResultIsErrTerm(
        in MirTerm term,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        // term.A = consumedResultOp, term.B = okLocalId, term.C = errWrapperLocalId, term.D = errBlock, term.E = okBlock
        int rType = mir.Operands[term.A].TypeId;
        TypeInfo rti = types.Items[rType];
        var rLlvm = new StringBuf(64);
        AppendValueLlvmType(rType, in ast, in names, in types, ref rLlvm);
        ByteSlice rVal = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

        ByteSlice isOk = fs.NextTemp();
        ByteSlice okTramp = fs.NextLabel("guard_res_ok"u8);
        ByteSlice errTramp = fs.NextLabel("guard_res_err"u8);

        body.Append("  "u8);
        body.Append(fs.Text(isOk));
        body.Append(" = extractvalue "u8);
        body.Append(rLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(rVal));
        body.Append(", 0\n  br i1 "u8);
        body.Append(fs.Text(isOk));
        body.Append(", label %"u8);
        body.Append(fs.Text(okTramp));
        body.Append(", label %"u8);
        body.Append(fs.Text(errTramp));
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(okTramp, ref body);
        ByteSlice okVal = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(okVal));
        body.Append(" = extractvalue "u8);
        body.Append(rLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(rVal));
        body.Append(", 1\n"u8);
        if (term.B >= 0 && term.B < fs.Slots.Count)
        {
            BindConsumedPayloadToLocal(term.B, okVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        else
        {
            SpillAndDropValueIfNeeded(rti.A, okVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        body.Append("  br label %bb_"u8);
        body.AppendInt(term.E);
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(errTramp, ref body);
        ByteSlice errVal = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(errVal));
        body.Append(" = extractvalue "u8);
        body.Append(rLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(rVal));
        body.Append(", 2\n"u8);
        if (term.C >= 0 && term.C < fs.Slots.Count)
        {
            BindConsumedPayloadToLocal(term.C, errVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        else
        {
            SpillAndDropValueIfNeeded(rti.B, errVal, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        body.Append("  br label %bb_"u8);
        body.AppendInt(term.D);
        body.AppendAscii((byte)'\n');
    }

    private static void EmitGuardOptionIsNoneTerm(
        in MirTerm term,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        // term.A = consumedOptionOp, term.B = someValLocalId, term.C = noneBlock, term.D = someBlock
        int oType = mir.Operands[term.A].TypeId;
        TypeInfo oti = types.Items[oType];
        var oLlvm = new StringBuf(64);
        AppendValueLlvmType(oType, in ast, in names, in types, ref oLlvm);
        ByteSlice oVal = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);

        ByteSlice has = fs.NextTemp();
        ByteSlice someTramp = fs.NextLabel("guard_opt_some"u8);

        body.Append("  "u8);
        body.Append(fs.Text(has));
        body.Append(" = extractvalue "u8);
        body.Append(oLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(oVal));
        body.Append(", 0\n  br i1 "u8);
        body.Append(fs.Text(has));
        body.Append(", label %"u8);
        body.Append(fs.Text(someTramp));
        body.Append(", label %bb_"u8);
        body.AppendInt(term.C);
        body.AppendAscii((byte)'\n');

        fs.EmitLabel(someTramp, ref body);
        ByteSlice val = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(val));
        body.Append(" = extractvalue "u8);
        body.Append(oLlvm.AsUtf8());
        body.AppendAscii((byte)' ');
        body.Append(fs.Text(oVal));
        body.Append(", 1\n"u8);
        if (term.B >= 0 && term.B < fs.Slots.Count)
        {
            BindConsumedPayloadToLocal(term.B, val, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        else
        {
            SpillAndDropValueIfNeeded(oti.A, val, in ast, in names, in types, ref ectx, ref fs, ref body);
        }
        body.Append("  br label %bb_"u8);
        body.AppendInt(term.D);
        body.AppendAscii((byte)'\n');
    }

    private static void EmitReturnTerm(
        in MirTerm term,
        in MirFunc fn,
        in FuncInstance fi,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        bool hasRetVal = false;
        ByteSlice retSsa = default;

        if (term.B >= 0)
        {
            retSsa = EmitPlaceAddress(term.B, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
            hasRetVal = true;
        }
        else if (term.A >= 0)
        {
            retSsa = EmitOperand(term.A, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            hasRetVal = true;
        }

        for (int i = fn.LocalCount - 1; i >= 0; i--)
        {
            if (fs.Slots[i].HasDropFlag)
            {
                EmitDropLocalIfLive(i, in ast, in names, in types, ref ectx, ref fs, ref body);
            }
        }

        if ((fi.Flags & DeclFlags.ReturnsByRef) != 0)
        {
            body.Append("  ret ptr "u8);
            body.Append(hasRetVal ? fs.Text(retSsa) : "null"u8);
            body.AppendAscii((byte)'\n');
        }
        else if (fi.ReturnTypeId == TypeIds.Void)
        {
            body.Append("  ret void\n"u8);
        }
        else
        {
            var retLlvm = new StringBuf(64);
            AppendValueLlvmType(fi.ReturnTypeId, in ast, in names, in types, ref retLlvm);
            body.Append("  ret "u8);
            body.Append(retLlvm.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(hasRetVal ? fs.Text(retSsa) : ZeroLiteralForLlvmType(retLlvm.AsUtf8()));
            body.AppendAscii((byte)'\n');
        }
    }

    private static ByteSlice EmitOperand(
        int opId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        MirOperand op = mir.Operands[opId];
        switch (op.Kind)
        {
            case MirOpKinds.CopyOrMove:
            {
                TypeInfo oti = types.Items[op.TypeId];
                MirPlace pl = mir.Places[op.PlaceOrLitIdx];
                if ((oti.Kind == TypeKinds.Ref || oti.Kind == TypeKinds.RefReadOnly) &&
                    pl.ProjCount == 0 && fs.Slots[pl.LocalId].IsRefSlot)
                {
                    ByteSlice ptrVal = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(ptrVal));
                    body.Append(" = load ptr, ptr %loc_"u8);
                    body.AppendInt(pl.LocalId);
                    body.AppendAscii((byte)'\n');
                    return ptrVal;
                }
                return EmitOperandFromPlaceByValue(op.PlaceOrLitIdx, op.TypeId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body);
            }

            case MirOpKinds.BoolConst:
                return fs.InternSsa(op.IntVal != 0 ? "true"u8 : "false"u8);

            case MirOpKinds.IntConst:
            {
                var intTy = new StringBuf(32);
                AppendValueLlvmType(op.TypeId, in ast, in names, in types, ref intTy);
                if (Bytes.Eq(intTy.AsUtf8(), "ptr"u8))
                {
                    return fs.InternSsa("null"u8);
                }
                if (Bytes.Eq(intTy.AsUtf8(), "i1"u8))
                {
                    return fs.InternSsa(op.IntVal != 0 ? "true"u8 : "false"u8);
                }
                if (!Bytes.Eq(intTy.AsUtf8(), "i8"u8) && !Bytes.Eq(intTy.AsUtf8(), "i16"u8) &&
                    !Bytes.Eq(intTy.AsUtf8(), "i32"u8) && !Bytes.Eq(intTy.AsUtf8(), "i64"u8))
                {
                    return fs.InternSsa(ZeroLiteralForLlvmType(intTy.AsUtf8()));
                }
                var sb = new StringBuf(24);
                sb.AppendLong(op.IntVal);
                return fs.InternSsa(sb.AsUtf8());
            }

            case MirOpKinds.FloatConst:
            {
                if (op.PlaceOrLitIdx < 0)
                {
                    ByteSlice bitsTmp = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(bitsTmp));
                    if (op.TypeId == TypeIds.F32)
                    {
                        body.Append(" = bitcast i32 "u8);
                        body.AppendInt(unchecked((int)op.IntVal));
                        body.Append(" to float\n"u8);
                    }
                    else
                    {
                        body.Append(" = bitcast i64 "u8);
                        body.AppendLong(op.IntVal);
                        body.Append(" to double\n"u8);
                    }
                    return bitsTmp;
                }
                ReadOnlySpan<byte> normDec = ta.GetLit(op.PlaceOrLitIdx);
                if (op.TypeId == TypeIds.F32)
                {
                    // `opt-18 -passes=verify` rejects non-exact decimal literals directly on `float`;
                    // `fptrunc double <dec> to float` verifies cleanly and constant-folds to exact binary32!
                    ByteSlice tmp = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(tmp));
                    body.Append(" = fptrunc double "u8);
                    body.Append(normDec);
                    body.Append(" to float\n"u8);
                    return tmp;
                }
                return fs.InternSsa(normDec);
            }

            case MirOpKinds.StrConst:
            {
                if (op.PlaceOrLitIdx < 0)
                {
                    return fs.InternSsa("{ ptr @.str.empty, i64 0 }"u8);
                }
                ReadOnlySpan<byte> strBytes = ta.GetLit(op.PlaceOrLitIdx);
                if (strBytes.Length == 0)
                {
                    return fs.InternSsa("{ ptr @.str.empty, i64 0 }"u8);
                }
                int strId = ectx.InternStr(strBytes);
                var sb = new StringBuf(40);
                sb.Append("{ ptr @.str."u8);
                sb.AppendInt(strId);
                sb.Append(", i64 "u8);
                sb.AppendInt(strBytes.Length);
                sb.Append(" }"u8);
                return fs.InternSsa(sb.AsUtf8());
            }

            case MirOpKinds.Utf8Const:
            {
                ReadOnlySpan<byte> u8Bytes = ta.GetLit(op.PlaceOrLitIdx);
                if (u8Bytes.Length == 0)
                {
                    return fs.InternSsa("{ ptr @.b_empty, i32 0 }"u8);
                }
                int bId = ectx.InternUtf8Lit(u8Bytes);
                var sb = new StringBuf(40);
                sb.Append("{ ptr @.b"u8);
                sb.AppendInt(bId);
                sb.Append(", i32 "u8);
                sb.AppendInt(u8Bytes.Length);
                sb.Append(" }"u8);
                return fs.InternSsa(sb.AsUtf8());
            }

            default:
            {
                var llvmTy = new StringBuf(64);
                AppendValueLlvmType(op.TypeId, in ast, in names, in types, ref llvmTy);
                return fs.InternSsa(ZeroLiteralForLlvmType(llvmTy.AsUtf8()));
            }
        }
    }

    private static ByteSlice EmitOperandFromPlaceByValue(
        int placeId,
        int operandTypeId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice addr = EmitPlaceAddress(placeId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
        var llvmTy = new StringBuf(64);
        AppendValueLlvmType(operandTypeId, in ast, in names, in types, ref llvmTy);
        ByteSlice loaded = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(loaded));
        body.Append(" = load "u8);
        body.Append(llvmTy.AsUtf8());
        body.Append(", ptr "u8);
        body.Append(fs.Text(addr));
        body.AppendAscii((byte)'\n');

        if (RequiresCleanup(operandTypeId, in ast, in types))
        {
            body.Append("  store "u8);
            body.Append(llvmTy.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(ZeroLiteralForLlvmType(llvmTy.AsUtf8()));
            body.Append(", ptr "u8);
            body.Append(fs.Text(addr));
            body.AppendAscii((byte)'\n');

            MirPlace pl = mir.Places[placeId];
            if (pl.ProjCount == 0 && !fs.Slots[pl.LocalId].IsRefSlot && fs.Slots[pl.LocalId].HasDropFlag)
            {
                body.Append("  store i1 false, ptr %drop_"u8);
                body.AppendInt(pl.LocalId);
                body.AppendAscii((byte)'\n');
            }
        }

        return loaded;
    }

    private static ByteSlice LoadPlaceValueWithoutMove(
        int placeId,
        int valueTypeId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice addr = EmitPlaceAddress(placeId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body, out _);
        var llvmTy = new StringBuf(64);
        AppendValueLlvmType(valueTypeId, in ast, in names, in types, ref llvmTy);
        ByteSlice loaded = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(loaded));
        body.Append(" = load "u8);
        body.Append(llvmTy.AsUtf8());
        body.Append(", ptr "u8);
        body.Append(fs.Text(addr));
        body.AppendAscii((byte)'\n');
        return loaded;
    }

    private static ByteSlice EmitPlaceAddress(
        int placeId,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref EmitCtx ectx,
        ref FnEmitState fs,
        ref StringBuf body,
        out int resultTypeId)
    {
        MirPlace pl = mir.Places[placeId];
        LocalSlot sl = fs.Slots[pl.LocalId];
        MirLocal loc = mir.Locals[fn.LocalStart + pl.LocalId];

        ByteSlice curPtr;
        int curType = sl.StorageOrRefTypeId;
        int projIdx = 0;

        if (sl.IsRefSlot)
        {
            curPtr = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(curPtr));
            body.Append(" = load ptr, ptr %loc_"u8);
            body.AppendInt(pl.LocalId);
            body.AppendAscii((byte)'\n');

            if (pl.ProjCount > 0)
            {
                MirProj p0 = mir.Projs[pl.ProjStart];
                if (p0.Kind == MirProjKinds.Deref && p0.ResultTypeId == curType)
                {
                    projIdx = 1;
                }
            }
        }
        else
        {
            curPtr = LocalAllocaSsa(pl.LocalId, ref fs);
            if ((loc.Mode == PassMode.ThisShared || loc.Mode == PassMode.ThisMut) &&
                types.Items[curType].Kind == TypeKinds.Class &&
                pl.ProjCount > 0)
            {
                MirProj p0 = mir.Projs[pl.ProjStart];
                if (p0.Kind == MirProjKinds.Deref && p0.ResultTypeId == curType)
                {
                    projIdx = 1;
                }
            }
        }

        for (int i = projIdx; i < pl.ProjCount; i++)
        {
            MirProj pr = mir.Projs[pl.ProjStart + i];
            switch (pr.Kind)
            {
                case MirProjKinds.Deref:
                {
                    ByteSlice loadedPtr = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(loadedPtr));
                    body.Append(" = load ptr, ptr "u8);
                    body.Append(fs.Text(curPtr));
                    body.AppendAscii((byte)'\n');
                    curPtr = loadedPtr;
                    curType = pr.ResultTypeId;
                    break;
                }

                case MirProjKinds.Field:
                {
                    if ((pr.Flags & 1) != 0 || types.Items[curType].Kind == TypeKinds.Class)
                    {
                        var clsTy = new StringBuf(64);
                        AppendClassStorageTypeName(curType, in ast, in names, in types, ref clsTy);
                        ByteSlice objPtr = fs.NextTemp();
                        ByteSlice fldPtr = fs.NextTemp();
                        body.Append("  "u8);
                        body.Append(fs.Text(objPtr));
                        body.Append(" = load ptr, ptr "u8);
                        body.Append(fs.Text(curPtr));
                        body.Append("\n  "u8);
                        body.Append(fs.Text(fldPtr));
                        body.Append(" = getelementptr inbounds "u8);
                        body.Append(clsTy.AsUtf8());
                        body.Append(", ptr "u8);
                        body.Append(fs.Text(objPtr));
                        body.Append(", i32 0, i32 "u8);
                        body.AppendInt(pr.FieldIdx);
                        body.AppendAscii((byte)'\n');
                        curPtr = fldPtr;
                        curType = pr.ResultTypeId;
                    }
                    else
                    {
                        var aggTy = new StringBuf(64);
                        AppendValueLlvmType(curType, in ast, in names, in types, ref aggTy);
                        ByteSlice fldPtr = fs.NextTemp();
                        body.Append("  "u8);
                        body.Append(fs.Text(fldPtr));
                        body.Append(" = getelementptr inbounds "u8);
                        body.Append(aggTy.AsUtf8());
                        body.Append(", ptr "u8);
                        body.Append(fs.Text(curPtr));
                        body.Append(", i32 0, i32 "u8);
                        body.AppendInt(pr.FieldIdx);
                        body.AppendAscii((byte)'\n');
                        curPtr = fldPtr;
                        curType = pr.ResultTypeId;
                    }
                    break;
                }

                case MirProjKinds.Index:
                {
                    var aggTy = new StringBuf(64);
                    AppendValueLlvmType(curType, in ast, in names, in types, ref aggTy);
                    var elemLlvm = new StringBuf(64);
                    AppendValueLlvmType(pr.ResultTypeId, in ast, in names, in types, ref elemLlvm);

                    ByteSlice idxVal = EnsureI32(EmitOperand(pr.IndexOpId, in fn, in ast, in ta, in names, in types, in mir, ref ectx, ref fs, ref body), mir.Operands[pr.IndexOpId].TypeId, in ast, in names, in types, ref fs, ref body);
                    ByteSlice dpSlot = fs.NextTemp();
                    ByteSlice lenSlot = fs.NextTemp();
                    ByteSlice dataPtr = fs.NextTemp();
                    ByteSlice len = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(dpSlot));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(aggTy.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(curPtr));
                    body.Append(", i32 0, i32 0\n  "u8);
                    body.Append(fs.Text(lenSlot));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(aggTy.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(curPtr));
                    body.Append(", i32 0, i32 1\n  "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(" = load ptr, ptr "u8);
                    body.Append(fs.Text(dpSlot));
                    body.Append("\n  "u8);
                    body.Append(fs.Text(len));
                    body.Append(" = load i32, ptr "u8);
                    body.Append(fs.Text(lenSlot));
                    body.AppendAscii((byte)'\n');

                    EmitBoundsCheck(idxVal, len, "index out of bounds"u8, ref ectx, ref fs, ref body);
                    ByteSlice idx64 = fs.NextTemp();
                    ByteSlice elemPtr = fs.NextTemp();
                    body.Append("  "u8);
                    body.Append(fs.Text(idx64));
                    body.Append(" = zext i32 "u8);
                    body.Append(fs.Text(idxVal));
                    body.Append(" to i64\n  "u8);
                    body.Append(fs.Text(elemPtr));
                    body.Append(" = getelementptr inbounds "u8);
                    body.Append(elemLlvm.AsUtf8());
                    body.Append(", ptr "u8);
                    body.Append(fs.Text(dataPtr));
                    body.Append(", i64 "u8);
                    body.Append(fs.Text(idx64));
                    body.AppendAscii((byte)'\n');

                    curPtr = elemPtr;
                    curType = pr.ResultTypeId;
                    break;
                }
            }
        }

        resultTypeId = curType;
        return curPtr;
    }

    private static ByteSlice EnsureI32(
        ByteSlice ssaVal,
        int opTypeId,
        in AstArena ast,
        in Interner names,
        in TypeTable types,
        ref FnEmitState fs, ref StringBuf body)
    {
        var llvmTy = new StringBuf(16);
        AppendValueLlvmType(opTypeId, in ast, in names, in types, ref llvmTy);
        if (Bytes.Eq(llvmTy.AsUtf8(), "i64"u8))
        {
            ByteSlice tr = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(tr));
            body.Append(" = trunc i64 "u8);
            body.Append(fs.Text(ssaVal));
            body.Append(" to i32\n"u8);
            return tr;
        }
        if (Bytes.Eq(llvmTy.AsUtf8(), "i8"u8) || Bytes.Eq(llvmTy.AsUtf8(), "i16"u8))
        {
            ByteSlice ext = fs.NextTemp();
            body.Append("  "u8);
            body.Append(fs.Text(ext));
            body.Append(types.IsUnsignedInteger(opTypeId) ? " = zext "u8 : " = sext "u8);
            body.Append(llvmTy.AsUtf8());
            body.AppendAscii((byte)' ');
            body.Append(fs.Text(ssaVal));
            body.Append(" to i32\n"u8);
            return ext;
        }
        return ssaVal;
    }

    private static ByteSlice EmitSizeOf(ReadOnlySpan<byte> llvmType, ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice gep = fs.NextTemp();
        ByteSlice sz = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(gep));
        body.Append(" = getelementptr "u8);
        body.Append(llvmType);
        body.Append(", ptr null, i64 1\n  "u8);
        body.Append(fs.Text(sz));
        body.Append(" = ptrtoint ptr "u8);
        body.Append(fs.Text(gep));
        body.Append(" to i64\n"u8);
        return sz;
    }

    private static void EmitClampMinOne(ByteSlice rawBytes, ByteSlice targetTemp, ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice isZero = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(isZero));
        body.Append(" = icmp eq i64 "u8);
        body.Append(fs.Text(rawBytes));
        body.Append(", 0\n  "u8);
        body.Append(fs.Text(targetTemp));
        body.Append(" = select i1 "u8);
        body.Append(fs.Text(isZero));
        body.Append(", i64 1, i64 "u8);
        body.Append(fs.Text(rawBytes));
        body.AppendAscii((byte)'\n');
    }

    private static ByteSlice EmitCheckedMalloc(ByteSlice bytesI64, ref EmitCtx ectx, ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice ptr = fs.NextTemp();
        ByteSlice oom = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(ptr));
        body.Append(" = call noalias ptr @malloc(i64 "u8);
        body.Append(fs.Text(bytesI64));
        body.Append(")\n  "u8);
        body.Append(fs.Text(oom));
        body.Append(" = icmp eq ptr "u8);
        body.Append(fs.Text(ptr));
        body.Append(", null\n"u8);
        EmitPanicIfTrue(oom, "out of memory"u8, ref ectx, ref fs, ref body);
        return ptr;
    }

    private static ByteSlice EmitCheckedRealloc(ByteSlice oldPtr, ByteSlice bytesI64, ref EmitCtx ectx, ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice ptr = fs.NextTemp();
        ByteSlice oom = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(ptr));
        body.Append(" = call noalias ptr @realloc(ptr "u8);
        body.Append(fs.Text(oldPtr));
        body.Append(", i64 "u8);
        body.Append(fs.Text(bytesI64));
        body.Append(")\n  "u8);
        body.Append(fs.Text(oom));
        body.Append(" = icmp eq ptr "u8);
        body.Append(fs.Text(ptr));
        body.Append(", null\n"u8);
        EmitPanicIfTrue(oom, "out of memory"u8, ref ectx, ref fs, ref body);
        return ptr;
    }

    private static void EmitBoundsCheck(ByteSlice idxI32, ByteSlice lenI32, ReadOnlySpan<byte> msg, ref EmitCtx ectx, ref FnEmitState fs, ref StringBuf body)
    {
        ByteSlice oob = fs.NextTemp();
        body.Append("  "u8);
        body.Append(fs.Text(oob));
        body.Append(" = icmp uge i32 "u8);
        body.Append(fs.Text(idxI32));
        body.Append(", "u8);
        body.Append(fs.Text(lenI32));
        body.AppendAscii((byte)'\n');
        EmitPanicIfTrue(oob, msg, ref ectx, ref fs, ref body);
    }

    private static void EmitPanicIfTrue(ByteSlice condI1, ReadOnlySpan<byte> msg, ref EmitCtx ectx, ref FnEmitState fs, ref StringBuf body)
    {
        int strId = ectx.InternStr(msg);
        ByteSlice panicLbl = fs.NextLabel("panic"u8);
        ByteSlice okLbl = fs.NextLabel("ok"u8);
        body.Append("  br i1 "u8);
        body.Append(fs.Text(condI1));
        body.Append(", label %"u8);
        body.Append(fs.Text(panicLbl));
        body.Append(", label %"u8);
        body.Append(fs.Text(okLbl));
        body.AppendAscii((byte)'\n');
        fs.EmitLabel(panicLbl, ref body);
        body.Append("  call void @__vib_panic(ptr @.str."u8);
        body.AppendInt(strId);
        body.Append(", i64 "u8);
        body.AppendInt(msg.Length);
        body.Append(")\n  unreachable\n"u8);
        fs.EmitLabel(okLbl, ref body);
    }

    private static void EmitPanicIfFalse(ByteSlice condI1, ReadOnlySpan<byte> msg, ref EmitCtx ectx, ref FnEmitState fs, ref StringBuf body)
    {
        int strId = ectx.InternStr(msg);
        ByteSlice okLbl = fs.NextLabel("ok"u8);
        ByteSlice panicLbl = fs.NextLabel("panic"u8);
        body.Append("  br i1 "u8);
        body.Append(fs.Text(condI1));
        body.Append(", label %"u8);
        body.Append(fs.Text(okLbl));
        body.Append(", label %"u8);
        body.Append(fs.Text(panicLbl));
        body.AppendAscii((byte)'\n');
        fs.EmitLabel(panicLbl, ref body);
        body.Append("  call void @__vib_panic(ptr @.str."u8);
        body.AppendInt(strId);
        body.Append(", i64 "u8);
        body.AppendInt(msg.Length);
        body.Append(")\n  unreachable\n"u8);
        fs.EmitLabel(okLbl, ref body);
    }
}
