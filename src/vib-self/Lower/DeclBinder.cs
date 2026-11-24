namespace Vibranium.Self;

public struct BinderCtx
{
    public IntMap<int> StdTypeByNameArity;
    public IntMap<int> AppGlobalTypeByNameArity;
    public Vec<int> PendingTypeIdx;
    public int NextPendingTypeCursor;
    public Vec<int> FuncWorkQueue;
    public int NextFuncWorkCursor;
    public bool CompilingStdItself;
    public bool HasStdUnit;
    public Vec<int> ExtensionFuncs;
    public Vec<int> UsingBoundKind;
    public Vec<int> UsingBoundId;
    public Vec<int> UsingBoundTypeId;
    public Vec<int> ConstStrChunks;
    // Set only while the binder evaluates a framework MethodImpl argument: the names
    // MethodImplOptions and System.Runtime.CompilerServices.MethodImplOptions then denote
    // the framework enum (an EnumDeclId of ast.Types.Count, outside the source decls).

    // Cached common interned NameIds
    public int IdThis;
    public int IdCtor;
    public int IdMain;
    public int IdDispose;
    public int IdValue;
    public int IdError;
    public int IdOption;
    public int IdOptionRef;
    public int IdOptionMut;
    public int IdSome;
    public int IdNone;
    public int IdNoneType;
    public int IdResult;
    public int IdOk;
    public int IdErr;
    public int IdUnit;
    public int IdVec;
    public int IdSpan;
    public int IdReadOnlySpan;
    public int IdNullable;
    public int IdPrelude;
    public int IdIo;
    public int IdStdio;
    public int IdVibraniumRuntime;
    public int IdMove;
    public int IdWrite;
    public int IdWriteLine;
    public int IdWriteOut;
    public int IdWriteErr;
    public int IdPanic;
    public int IdConsoleIo;
    public int IdBytes;
    public int IdByteSpanExtensions;
    public int IdOptionExtensions;
    public int IdStringUtf8Extensions;
    public int IdUtf8SpanExtensions;
    public int IdUtf8Lines;
    public int IdUtf8ArgEnumerator;
    public int IdAsUtf8;
    public int IdLines;
    public int IdGetEnumerator;
    public int IdMoveNext;
    public int IdCurrent;
    public int IdExpect;
    public int IdUnwrapOrDefault;
    public int IdAdd;
    public int IdTryParse;
    public int IdTryParseInt32;
    public int IdTryParseInt64;
    public int IdMemoryExtensions;
    public int IdSystem;
    public int IdVibranium;
    public int IdVibraniumStd;
    public int IdGlobalPrefix;
    public int IdGlobalSystem;
    public int IdGlobalVibranium;
    public int IdGlobalVibraniumStd;
    public int IdIDisposable;
    public int IdObject;
    public int IdInt;
    public int IdInt32;
    public int IdLong;
    public int IdInt64;
    public int IdGetItem;
    public int IdSetItem;
    public int IdGetLength;
    public int IdGetCount;
    public int IdLength;
    public int IdCount;
    public int IdSlice;
    public int IdSequenceEqual;
    public int IdStartsWith;
    public int IdEndsWith;
    public int IdIndexOf;
    public int IdLastIndexOf;
    public int IdContains;
    public int IdConsole;
    public int IdArray;

    public BinderCtx(ref Interner names, bool compilingStdItself)
    {
        bool hasStdUnit = true;
        StdTypeByNameArity = new IntMap<int>(64);
        AppGlobalTypeByNameArity = new IntMap<int>(64);
        PendingTypeIdx = new Vec<int>(64);
        NextPendingTypeCursor = 0;
        FuncWorkQueue = new Vec<int>(128);
        NextFuncWorkCursor = 0;
        CompilingStdItself = compilingStdItself;
        HasStdUnit = hasStdUnit;
        ExtensionFuncs = new Vec<int>(32);
        UsingBoundKind = new Vec<int>(64);
        UsingBoundId = new Vec<int>(64);
        UsingBoundTypeId = new Vec<int>(64);
        ConstStrChunks = new Vec<int>(32);
        IdThis = names.Intern("this"u8);
        IdCtor = names.Intern(".ctor"u8);
        IdMain = names.Intern("Main"u8);
        IdDispose = names.Intern("Dispose"u8);
        IdValue = names.Intern("Value"u8);
        IdError = names.Intern("Error"u8);
        IdOption = names.Intern("Option"u8);
        IdOptionRef = names.Intern("OptionRef"u8);
        IdOptionMut = names.Intern("OptionMut"u8);
        IdSome = names.Intern("Some"u8);
        IdNone = names.Intern("None"u8);
        IdNoneType = names.Intern("NoneType"u8);
        IdResult = names.Intern("Result"u8);
        IdOk = names.Intern("Ok"u8);
        IdErr = names.Intern("Err"u8);
        IdUnit = names.Intern("Unit"u8);
        IdVec = names.Intern("Vec"u8);
        IdSpan = names.Intern("Span"u8);
        IdReadOnlySpan = names.Intern("ReadOnlySpan"u8);
        IdNullable = names.Intern("Nullable"u8);
        IdPrelude = names.Intern("Prelude"u8);
        IdIo = names.Intern("Io"u8);
        IdStdio = names.Intern("Stdio"u8);
        IdVibraniumRuntime = names.Intern("VibraniumRuntime"u8);
        IdMove = names.Intern("Move"u8);
        IdWrite = names.Intern("Write"u8);
        IdWriteLine = names.Intern("WriteLine"u8);
        IdWriteOut = names.Intern("WriteOut"u8);
        IdWriteErr = names.Intern("WriteErr"u8);
        IdPanic = names.Intern("Panic"u8);
        IdConsoleIo = names.Intern("ConsoleIo"u8);
        IdBytes = names.Intern("Bytes"u8);
        IdByteSpanExtensions = names.Intern("ByteSpanExtensions"u8);
        IdOptionExtensions = names.Intern("OptionExtensions"u8);
        IdStringUtf8Extensions = names.Intern("StringUtf8Extensions"u8);
        IdUtf8SpanExtensions = names.Intern("Utf8SpanExtensions"u8);
        IdUtf8Lines = names.Intern("Utf8Lines"u8);
        IdUtf8ArgEnumerator = names.Intern("Utf8ArgEnumerator"u8);
        IdAsUtf8 = names.Intern("AsUtf8"u8);
        IdLines = names.Intern("Lines"u8);
        IdGetEnumerator = names.Intern("GetEnumerator"u8);
        IdMoveNext = names.Intern("MoveNext"u8);
        IdCurrent = names.Intern("Current"u8);
        IdExpect = names.Intern("Expect"u8);
        IdUnwrapOrDefault = names.Intern("UnwrapOrDefault"u8);
        IdAdd = names.Intern("Add"u8);
        IdTryParse = names.Intern("TryParse"u8);
        IdTryParseInt32 = names.Intern("TryParseInt32"u8);
        IdTryParseInt64 = names.Intern("TryParseInt64"u8);
        IdMemoryExtensions = names.Intern("MemoryExtensions"u8);
        IdSystem = names.Intern("System"u8);
        IdVibranium = names.Intern("Vibranium"u8);
        IdVibraniumStd = names.Intern("Vibranium.Std"u8);
        IdGlobalPrefix = names.Intern("global::"u8);
        IdGlobalSystem = names.Intern("global::System"u8);
        IdGlobalVibranium = names.Intern("global::Vibranium"u8);
        IdGlobalVibraniumStd = names.Intern("global::Vibranium.Std"u8);
        IdIDisposable = names.Intern("IDisposable"u8);
        IdObject = names.Intern("Object"u8);
        IdInt = names.Intern("int"u8);
        IdInt32 = names.Intern("Int32"u8);
        IdLong = names.Intern("long"u8);
        IdInt64 = names.Intern("Int64"u8);
        IdGetItem = names.Intern("get_Item"u8);
        IdSetItem = names.Intern("set_Item"u8);
        IdGetLength = names.Intern("get_Length"u8);
        IdGetCount = names.Intern("get_Count"u8);
        IdLength = names.Intern("Length"u8);
        IdCount = names.Intern("Count"u8);
        IdSlice = names.Intern("Slice"u8);
        IdSequenceEqual = names.Intern("SequenceEqual"u8);
        IdStartsWith = names.Intern("StartsWith"u8);
        IdEndsWith = names.Intern("EndsWith"u8);
        IdIndexOf = names.Intern("IndexOf"u8);
        IdLastIndexOf = names.Intern("LastIndexOf"u8);
        IdContains = names.Intern("Contains"u8);
        IdConsole = names.Intern("Console"u8);
        IdArray = names.Intern("Array"u8);
    }

    public BinderCtx(ref Interner names, bool compilingStdItself, bool hasStdUnit)
    {
        StdTypeByNameArity = new IntMap<int>(64);
        AppGlobalTypeByNameArity = new IntMap<int>(64);
        PendingTypeIdx = new Vec<int>(64);
        NextPendingTypeCursor = 0;
        FuncWorkQueue = new Vec<int>(128);
        NextFuncWorkCursor = 0;
        CompilingStdItself = compilingStdItself;
        HasStdUnit = hasStdUnit;
        ExtensionFuncs = new Vec<int>(32);
        UsingBoundKind = new Vec<int>(64);
        UsingBoundId = new Vec<int>(64);
        UsingBoundTypeId = new Vec<int>(64);
        ConstStrChunks = new Vec<int>(32);

        IdThis = names.Intern("this"u8);
        IdCtor = names.Intern(".ctor"u8);
        IdMain = names.Intern("Main"u8);
        IdDispose = names.Intern("Dispose"u8);
        IdValue = names.Intern("Value"u8);
        IdError = names.Intern("Error"u8);
        IdOption = names.Intern("Option"u8);
        IdOptionRef = names.Intern("OptionRef"u8);
        IdOptionMut = names.Intern("OptionMut"u8);
        IdSome = names.Intern("Some"u8);
        IdNone = names.Intern("None"u8);
        IdNoneType = names.Intern("NoneType"u8);
        IdResult = names.Intern("Result"u8);
        IdOk = names.Intern("Ok"u8);
        IdErr = names.Intern("Err"u8);
        IdUnit = names.Intern("Unit"u8);
        IdVec = names.Intern("Vec"u8);
        IdSpan = names.Intern("Span"u8);
        IdReadOnlySpan = names.Intern("ReadOnlySpan"u8);
        IdNullable = names.Intern("Nullable"u8);
        IdPrelude = names.Intern("Prelude"u8);
        IdIo = names.Intern("Io"u8);
        IdStdio = names.Intern("Stdio"u8);
        IdVibraniumRuntime = names.Intern("VibraniumRuntime"u8);
        IdMove = names.Intern("Move"u8);
        IdWrite = names.Intern("Write"u8);
        IdWriteLine = names.Intern("WriteLine"u8);
        IdWriteOut = names.Intern("WriteOut"u8);
        IdWriteErr = names.Intern("WriteErr"u8);
        IdPanic = names.Intern("Panic"u8);
        IdConsoleIo = names.Intern("ConsoleIo"u8);
        IdBytes = names.Intern("Bytes"u8);
        IdByteSpanExtensions = names.Intern("ByteSpanExtensions"u8);
        IdOptionExtensions = names.Intern("OptionExtensions"u8);
        IdStringUtf8Extensions = names.Intern("StringUtf8Extensions"u8);
        IdUtf8SpanExtensions = names.Intern("Utf8SpanExtensions"u8);
        IdUtf8Lines = names.Intern("Utf8Lines"u8);
        IdUtf8ArgEnumerator = names.Intern("Utf8ArgEnumerator"u8);
        IdAsUtf8 = names.Intern("AsUtf8"u8);
        IdLines = names.Intern("Lines"u8);
        IdGetEnumerator = names.Intern("GetEnumerator"u8);
        IdMoveNext = names.Intern("MoveNext"u8);
        IdCurrent = names.Intern("Current"u8);
        IdExpect = names.Intern("Expect"u8);
        IdUnwrapOrDefault = names.Intern("UnwrapOrDefault"u8);
        IdAdd = names.Intern("Add"u8);
        IdTryParse = names.Intern("TryParse"u8);
        IdTryParseInt32 = names.Intern("TryParseInt32"u8);
        IdTryParseInt64 = names.Intern("TryParseInt64"u8);
        IdMemoryExtensions = names.Intern("MemoryExtensions"u8);
        IdSystem = names.Intern("System"u8);
        IdVibranium = names.Intern("Vibranium"u8);
        IdVibraniumStd = names.Intern("Vibranium.Std"u8);
        IdGlobalPrefix = names.Intern("global::"u8);
        IdGlobalSystem = names.Intern("global::System"u8);
        IdGlobalVibranium = names.Intern("global::Vibranium"u8);
        IdGlobalVibraniumStd = names.Intern("global::Vibranium.Std"u8);
        IdIDisposable = names.Intern("IDisposable"u8);
        IdObject = names.Intern("Object"u8);
        IdInt = names.Intern("int"u8);
        IdInt32 = names.Intern("Int32"u8);
        IdLong = names.Intern("long"u8);
        IdInt64 = names.Intern("Int64"u8);
        IdGetItem = names.Intern("get_Item"u8);
        IdSetItem = names.Intern("set_Item"u8);
        IdGetLength = names.Intern("get_Length"u8);
        IdGetCount = names.Intern("get_Count"u8);
        IdLength = names.Intern("Length"u8);
        IdCount = names.Intern("Count"u8);
        IdSlice = names.Intern("Slice"u8);
        IdSequenceEqual = names.Intern("SequenceEqual"u8);
        IdStartsWith = names.Intern("StartsWith"u8);
        IdEndsWith = names.Intern("EndsWith"u8);
        IdIndexOf = names.Intern("IndexOf"u8);
        IdLastIndexOf = names.Intern("LastIndexOf"u8);
        IdContains = names.Intern("Contains"u8);
        IdConsole = names.Intern("Console"u8);
        IdArray = names.Intern("Array"u8);
    }
}

public static class BuiltinStaticIds
{
    public const int IDisposable = -10;
    public const int Prelude = -11;
    public const int Io = -12;
    public const int Stdio = -13;
    public const int VibraniumRuntime = -14;
    public const int Console = -15;
    public const int MemoryExtensions = -16;
    public const int StringUtf8Extensions = -17;
    public const int Utf8SpanExtensions = -18;
    public const int Array = -19;
    public const int OptionExtensions = -20;
}

public static class DeclBinder
{
    private const int MaxGenericDepth = 16;

    public static long PackNameArity(int nameId, int arity) =>
        ((long)nameId << 16) | (long)(arity & 0xFFFF);

    public static void BindDeclarations(
        ref AstArena ast,
        in TokenArena ta,
        ref Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        SynthesizeImplicitClassCtorsAndTopLevelMain(ref ast, ref names, ref bctx);

        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            long key = PackNameArity(td.NameId, td.TypeParamCount);
            if (td.OwnerDeclId < 0)
            {
                if (td.UnitKind == UnitKinds.Std)
                {
                    bctx.StdTypeByNameArity.Set(key, i);
                }
                else if (td.UnitKind == UnitKinds.App && td.NsId < 0)
                {
                    bctx.AppGlobalTypeByNameArity.Set(key, i);
                }
            }
            for (int m = 0; m < td.FuncCount; m++)
            {
                int fId = td.FuncStart + m;
                if ((ast.Funcs[fId].Flags & DeclFlags.IsExtension) != 0)
                {
                    bctx.ExtensionFuncs.Push(fId);
                }
            }
        }

        var emptyTpNames = new Vec<int>();
        var emptyTpVals = new Vec<int>();

        // Register all non-generic concrete types first so using aliases, field layout, enum, and const queries work immediately
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if (td.TypeParamCount > 0) continue;
            if (td.Kind == TypeDeclKinds.Enum)
            {
                types.MakeEnum(i, names.Text(td.NameId));
            }
            else if ((td.Flags & DeclFlags.Static) == 0)
            {
                int tk = td.Kind == TypeDeclKinds.Class ? TypeKinds.Class : TypeKinds.Struct;
                types.MakeUserType(tk, i, names.Text(td.NameId), emptyTpVals.AsReadOnlySpan());
            }
        }

        BindAndValidateUsings(in ast, in ta, ref names, ref bctx, ref types, ref diags);
        BindTypeBaseLists(ref ast, in ta, in names, ref bctx, ref types, ref diags);

        EvaluateConstFields(ref ast, in ta, in names, ref bctx, ref types, ref diags);
        BindReturnLifetimeArguments(ref ast, in ta, ref names, in bctx, ref diags);

        DrainConcreteTypeQueue(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);

        // Gatekeeper check on user declarations
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if (!bctx.CompilingStdItself && td.UnitKind == UnitKinds.Std)
            {
                // Still validate Sys [Native] signatures to register them cleanly
                RegisterSysNativeMethodsIfAny(i, in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
                continue;
            }
            CheckTypeDeclGatekeeper(i, in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
        }

        // Gatekeeper check on local functions
        for (int f = 0; f < ast.Funcs.Count; f++)
        {
            FuncDecl lfd = ast.Funcs[f];
            if ((lfd.Flags & DeclFlags.IsLocalFunc) == 0) continue;
            CheckLocalFuncGatekeeper(in lfd, in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
        }

        // Seed entry point and user non-generic methods (matching VibraniumFrontend.cs)
        int entryInstId = FindAndEnqueueEntryPoint(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
        mir.EntryFuncInstId = entryInstId;

        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if (!bctx.CompilingStdItself && td.UnitKind == UnitKinds.Std) continue;
            if (td.TypeParamCount > 0 || td.Kind == TypeDeclKinds.Enum) continue;

            int ownerTypeId = -1;
            if ((td.Flags & DeclFlags.Static) == 0)
            {
                int tk = td.Kind == TypeDeclKinds.Class ? TypeKinds.Class : TypeKinds.Struct;
                ownerTypeId = types.MakeUserType(tk, i, names.Text(td.NameId), emptyTpVals.AsReadOnlySpan());
            }

            for (int m = 0; m < td.FuncCount; m++)
            {
                int fId = td.FuncStart + m;
                FuncDecl fd = ast.Funcs[fId];
                if (fd.TypeParamCount > 0) continue;
                _ = EnsureFuncInstance(fId, ownerTypeId, emptyTpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
            }
        }

        DrainConcreteTypeQueue(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
    }

    public static void ValidateSpanElementTypes(in AstArena ast, in TokenArena ta, in TypeTable types, in MirModuleArena mir, ref DiagnosticBag diags)
    {
        // Inferred intrinsic results (for example Vec<T>.AsSpan()) have no
        // explicit Span<T> type syntax, so validate their resolved MIR types too.
        for (int i = 0; i < mir.Locals.Count; i++)
        {
            MirLocal local = mir.Locals[i];
            ValidateSpanElementType(local.TypeId, local.TokIdx, in ast, in ta, in types, ref diags);
        }
        for (int i = 0; i < mir.Operands.Count; i++)
        {
            MirOperand operand = mir.Operands[i];
            ValidateSpanElementType(operand.TypeId, operand.TokIdx, in ast, in ta, in types, ref diags);
        }

        // Recursive population temporarily exposes an empty layout. Recheck
        // constraints after all lowering so Vec<Self> cannot treat Self as
        // unmanaged before Self's managed buffer field has been populated.
        int fallbackToken = 0;
        for (int i = 0; i < ast.Types.Count; i++)
        {
            if (ast.Types[i].UnitKind == UnitKinds.App)
            {
                fallbackToken = ast.Types[i].TokIdx;
                break;
            }
        }
        for (int i = 0; i < types.Items.Count; i++)
        {
            TypeInfo type = types.Items[i];
            if (type.Kind == TypeKinds.Vec && !types.IsUnmanaged(type.A))
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fallbackToken], "Vec<T> requires unmanaged Copy element type."u8);
            }
            else if (type.Kind == TypeKinds.Class || type.Kind == TypeKinds.Struct)
            {
                TypeDecl td = ast.Types[type.A];
                ValidateTypeArguments(td.TypeParamCount, td.TypeParamFlagsStart,
                    types.TypeArgs.AsReadOnlySpan().Slice(type.B, type.C), ta.Tokens[td.TokIdx], in ast, in types, ref diags);
            }
        }
    }

    private static void ValidateSpanElementType(int typeId, int token, in AstArena ast, in TokenArena ta, in TypeTable types, ref DiagnosticBag diags)
    {
        if (typeId < 0 || typeId >= types.Items.Count || token < 0 || token >= ta.Tokens.Count) return;
        TypeInfo type = types.Items[typeId];
        if ((type.Kind == TypeKinds.Span || type.Kind == TypeKinds.ReadOnlySpan) && !types.IsCopy(type.A, in ast))
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[token], "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2."u8);
    }

    private static void SynthesizeImplicitClassCtorsAndTopLevelMain(
        ref AstArena ast,
        ref Interner names,
        ref BinderCtx bctx)
    {
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if (td.Kind != TypeDeclKinds.Class || (td.Flags & DeclFlags.Static) != 0) continue;
            bool hasCtor = false;
            for (int m = 0; m < td.FuncCount; m++)
            {
                if ((ast.Funcs[td.FuncStart + m].Flags & DeclFlags.IsCtor) != 0)
                {
                    hasCtor = true;
                    break;
                }
            }
            if (!hasCtor)
            {
                // Synthesize default constructor assigning any field initializers
                var stmts = new Vec<int>();
                for (int f = 0; f < td.FieldCount; f++)
                {
                    FieldDecl fld = ast.Fields[td.FieldStart + f];
                    if ((fld.Flags & DeclFlags.Static) == 0 && fld.InitExpr >= 0)
                    {
                        int thisNode = ast.AddNode(AstKind.ExprThis, fld.TokIdx, 0, 0, 0, 0);
                        int memNode = ast.AddNode(AstKind.ExprMember, fld.TokIdx, thisNode, fld.NameId, 0, 0);
                        int asgn = ast.AddNode(AstKind.ExprAssign, fld.TokIdx, (int)TokKind.Eq, memNode, fld.InitExpr, 0);
                        stmts.Push(ast.AddNode(AstKind.StmtExpr, fld.TokIdx, asgn, 0, 0, 0));
                    }
                }
                int stmtsStart = ast.PushExtraSpan(stmts.AsReadOnlySpan());
                int blk = ast.AddNode(AstKind.StmtBlock, td.TokIdx, stmtsStart, stmts.Count, 0, 0);
                int voidNode = ast.AddNode(AstKind.TypePrim, td.TokIdx, (int)TokKind.KwVoid, 0, 0, 0);

                // Copy existing funcs + new implicit ctor into contiguous slice
                var merged = new Vec<FuncDecl>(td.FuncCount + 1);
                for (int m = 0; m < td.FuncCount; m++)
                {
                    merged.Push(ast.Funcs[td.FuncStart + m]);
                }
                merged.Push(new FuncDecl(i, bctx.IdCtor, voidNode, 0, 0, 0, 0, 0, blk, DeclFlags.Public | DeclFlags.IsCtor | DeclFlags.Partial, -1, 0, 0, td.SourceId, td.TokIdx, td.UsingExtraStart, td.UsingCount));
                td.FuncStart = ast.Funcs.Count;
                td.FuncCount = merged.Count;
                for (int m = 0; m < merged.Count; m++) ast.Funcs.Push(merged[m]);
                ast.Types[i] = td;
            }
        }

        if (ast.TopStmts.Count > 0 || ast.TopLocalFuncs.Count > 0)
        {
            bool returnsInt = TopStmtsHaveValueReturn(in ast, ast.TopStmts.AsReadOnlySpan());
            int retTokKind = returnsInt ? (int)TokKind.KwInt : (int)TokKind.KwVoid;
            int retTypeNode = ast.AddNode(AstKind.TypePrim, ast.TopTokIdx, retTokKind, 0, 0, 0);
            int topStmtCount = ast.TopStmts.Count;
            int topExtraStart = ast.Extra.Count;
            for (int ts = 0; ts < topStmtCount; ts++)
            {
                ast.Extra.Push(ast.TopStmts[ts]);
            }
            int bodyNode = ast.AddNode(AstKind.StmtBlock, ast.TopTokIdx, topExtraStart, topStmtCount, 0, 0);

            int topUStart = ast.TopSourceId >= 0 && ast.TopSourceId < ast.SourceUsingStarts.Count ? ast.SourceUsingStarts[ast.TopSourceId] : 0;
            int topUCount = ast.TopSourceId >= 0 && ast.TopSourceId < ast.SourceUsingCounts.Count ? ast.SourceUsingCounts[ast.TopSourceId] : 0;
            int progDeclId = ast.Types.Count;
            var progTd = new TypeDecl(TypeDeclKinds.Class, UnitKinds.App, -1, names.Intern("__TopLevelProgram"u8), 0, 0, 0, DeclFlags.Static, ast.TopSourceId, ast.TopTokIdx, -1, topUStart, topUCount);
            var mainFd = new FuncDecl(progDeclId, names.Intern("<Main>$"u8), retTypeNode, 0, 0, 0, 0, 0, bodyNode, DeclFlags.Static | DeclFlags.IsTopLevelMain, -1, 0, 0, ast.TopSourceId, ast.TopTokIdx, topUStart, topUCount);
            if (ast.TopLocalFuncs.Count > 0)
            {
                int lfCount = ast.TopLocalFuncs.Count;
                int lfStart = ast.Extra.Count;
                for (int lf = 0; lf < lfCount; lf++)
                {
                    ast.Extra.Push(ast.TopLocalFuncs[lf]);
                }
                mainFd.LocalFuncExtraStart = lfStart;
                mainFd.LocalFuncCount = lfCount;
            }
            progTd.FuncStart = ast.Funcs.Count;
            progTd.FuncCount = 1;
            ast.Funcs.Push(mainFd);
            ast.Types.Push(progTd);
        }
    }

    private static bool TopStmtsHaveValueReturn(in AstArena ast, ReadOnlySpan<int> stmts)
    {
        for (int i = 0; i < stmts.Length; i++)
        {
            if (StmtHasValueReturn(in ast, stmts[i])) return true;
        }
        return false;
    }

    private static bool StmtHasValueReturn(in AstArena ast, int stmtId)
    {
        if (stmtId < 0) return false;
        AstNode n = ast.Nodes[stmtId];
        switch (n.Kind)
        {
            case AstKind.StmtReturn:
                return n.A >= 0;
            case AstKind.StmtBlock:
                for (int i = 0; i < n.B; i++)
                {
                    if (StmtHasValueReturn(in ast, ast.Extra[n.A + i])) return true;
                }
                return false;
            case AstKind.StmtIf:
                return StmtHasValueReturn(in ast, n.B) || StmtHasValueReturn(in ast, n.C);
            case AstKind.StmtMulti:
                for (int i = 0; i < n.B; i++)
                {
                    if (StmtHasValueReturn(in ast, ast.Extra[n.A + i])) return true;
                }
                return false;
            case AstKind.StmtWhile:
                return StmtHasValueReturn(in ast, n.B);
            case AstKind.StmtDoWhile:
            case AstKind.StmtUncheckedBlock:
                return StmtHasValueReturn(in ast, n.A);
            case AstKind.StmtFor:
                return StmtHasValueReturn(in ast, n.D);
            case AstKind.StmtForeach:
                return StmtHasValueReturn(in ast, n.D);
            case AstKind.StmtSwitch:
                for (int i = 0; i < n.C; i++)
                {
                    AstNode sec = ast.Nodes[ast.Extra[n.B + i]];
                    for (int k = 0; k < sec.D; k++)
                    {
                        if (StmtHasValueReturn(in ast, ast.Extra[sec.C + k])) return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    private struct ConstEvalVal
    {
        public int Kind;       // 0 = Invalid, 1 = Int/Char/Enum, 2 = Bool, 3 = F32, 4 = F64, 5 = Str
        public int TypeId;     // TypeIds.*
        public int EnumDeclId; // >= 0 when enum value, else -1
        public long IntVal;    // truncated/extended int, 0/1 bool, IEEE-754 bits for float/double, or LitSliceIdx for Str
        public double FltVal;  // floating-point value for Kind 3 / 4

        public ConstEvalVal(int kind, int typeId, int enumDeclId, long intVal, double fltVal)
        {
            Kind = kind;
            TypeId = typeId;
            EnumDeclId = enumDeclId;
            IntVal = intVal;
            FltVal = fltVal;
        }

        public static ConstEvalVal Invalid() => new ConstEvalVal(0, TypeIds.Error, -1, 0, 0.0);
        public static ConstEvalVal MakeInt(int primTypeId, long rawVal) =>
            new ConstEvalVal(1, primTypeId, -1, TruncateConstPrim(rawVal, primTypeId), 0.0);
        public static ConstEvalVal MakeEnum(int enumDeclId, long rawVal) =>
            new ConstEvalVal(1, TypeIds.I32, enumDeclId, (int)rawVal, 0.0);
        public static ConstEvalVal MakeBool(bool b) =>
            new ConstEvalVal(2, TypeIds.Bool, -1, b ? 1L : 0L, 0.0);
        public static ConstEvalVal MakeF32(float f) =>
            new ConstEvalVal(3, TypeIds.F32, -1, FloatToBits(f), (double)f);
        public static ConstEvalVal MakeF32Bits(int bits) =>
            new ConstEvalVal(3, TypeIds.F32, -1, bits, (double)BitsToFloat(bits));
        public static ConstEvalVal MakeF64(double d) =>
            new ConstEvalVal(4, TypeIds.F64, -1, DoubleToBits(d), d);
        public static ConstEvalVal MakeF64Bits(long bits) =>
            new ConstEvalVal(4, TypeIds.F64, -1, bits, BitsToDouble(bits));
        public static ConstEvalVal MakeStr(long strCode) =>
            new ConstEvalVal(5, TypeIds.Str, -1, strCode, 0.0);
        public static ConstEvalVal MakeDefaultLiteral() =>
            new ConstEvalVal(6, TypeIds.Void, -1, 0L, 0.0);
    }

    private static void BindReturnLifetimeArguments(ref AstArena ast, in TokenArena ta, ref Interner names,
        in BinderCtx bctx, ref DiagnosticBag diags)
    {
        var strChunks = new Vec<int>();
        for (int c = 0; c < bctx.ConstStrChunks.Count; c++) strChunks.Push(bctx.ConstStrChunks[c]);
        for (int f = 0; f < ast.Funcs.Count; f++)
        {
            FuncDecl fd = ast.Funcs[f];
            if ((fd.Flags & DeclFlags.HasReturnLifetimeAttr) == 0 || fd.LifetimeCount == 0) continue;
            var outerBindings = new Vec<SourceLexicalBinding>();
            SourceLexicalScopeChecker.CollectEnclosingBindings(f, in ast, ref outerBindings);
            var resolved = new Vec<int>(fd.LifetimeCount);
            for (int j = 0; j < fd.LifetimeCount; j++)
            {
                AstNode arg = ast.Nodes[ast.Extra[fd.LifetimeExtraStart + j]];
                bool hadDiag = false;
                AstNode expr = ast.Nodes[arg.A];
                ConstEvalVal value = ConstEvalVal.Invalid();
                if (expr.Kind == AstKind.ExprDefault && expr.A >= 0 &&
                    (ast.Nodes[expr.A].Kind == AstKind.TypeArray ||
                     (TryResolveConstTypeSyntaxInBinder(expr.A, fd.LifetimeLexicalOwner, in ast, in names, in bctx,
                         out int target, out _, out _, out _) && target == TypeIds.Str)))
                {
                    // Typed string or array default is a null name; the parser handles bare default.
                    diags.ReportTok(VibCodes.NullOrZeroUnsafe, ta.Tokens[expr.TokIdx], "LifetimeOf cannot use a null name or array."u8);
                    hadDiag = true;
                }
                else if (expr.Kind == AstKind.ExprStrLit || expr.Kind == AstKind.ExprIdent || expr.Kind == AstKind.ExprMember)
                {
                    value = EvalConstNode(arg.A, fd.LifetimeLexicalOwner, 0, emitDiags: true,
                        in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag,
                        outerBindings.AsReadOnlySpan(), -1, primaryParamsFirstInOwner: false);
                }
                if (value.Kind != 5)
                {
                    if (!hadDiag)
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[arg.TokIdx], "LifetimeOf arguments must be string literals or constant string names (CS1503 for non-string arguments)."u8);
                }
                else
                {
                    var text = new StringBuf();
                    AppendConstStringBytes(in ta, in strChunks, value.IntVal, ref text);
                    resolved.Push(names.Intern(text.AsUtf8()));
                }
            }
            if (resolved.Count == fd.LifetimeCount)
            {
                fd.LifetimeExtraStart = ast.PushExtraSpan(resolved.AsReadOnlySpan());
            }
            else
            {
                // Preserve the distinction between an invalid contract and no
                // contract. Do not run elision and demand a second annotation.
                fd.LifetimeCount = -1;
            }
            ast.Funcs[f] = fd;
        }
    }

    private static void EvaluateConstFields(
        ref AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        // Reset HasConstVal on non-enum const fields and explicit enum initializers so target-type truncation
        // and checked overflow rules always run (Parser.TryEvalSimpleIntExpr had no target type or cast info).
        for (int t = 0; t < ast.Types.Count; t++)
        {
            TypeDecl td = ast.Types[t];
            for (int f = 0; f < td.FieldCount; f++)
            {
                int idx = td.FieldStart + f;
                FieldDecl fd = ast.Fields[idx];
                if ((fd.Flags & DeclFlags.IsConst) != 0 && fd.InitExpr >= 0)
                {
                    ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, 0, 0, fd.TokIdx);
                }
                else if (td.Kind == TypeDeclKinds.Enum && fd.InitExpr < 0)
                {
                    ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, 0, 0, fd.TokIdx);
                }
            }
        }

        var strChunks = new Vec<int>(32);
        for (int ci = 0; ci < bctx.ConstStrChunks.Count; ci++) strChunks.Push(bctx.ConstStrChunks[ci]);

        // Multi-pass resolution so forward/cross-type const references settle regardless of declaration order.
        int maxPasses = ast.Fields.Count + 2;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool progress = false;
            for (int t = 0; t < ast.Types.Count; t++)
            {
                TypeDecl td = ast.Types[t];
                if (td.Kind == TypeDeclKinds.Enum)
                {
                    long nextEnumVal = 0;
                    bool prevReady = true;
                    for (int f = 0; f < td.FieldCount; f++)
                    {
                        int idx = td.FieldStart + f;
                        FieldDecl fd = ast.Fields[idx];
                        if (fd.InitExpr >= 0)
                        {
                            if (fd.HasConstVal != 0)
                            {
                                nextEnumVal = fd.ConstVal + 1;
                                prevReady = true;
                                continue;
                            }
                            bool dummyHadDiag = false;
                            ConstEvalVal ev = EvalConstNode(fd.InitExpr, t, 0, emitDiags: false, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref dummyHadDiag, default, -1, primaryParamsFirstInOwner: false);
                            long convE = 0;
                            bool okEnum = ev.Kind != 0 && ev.EnumDeclId < 0 &&
                                TryConvertConstToFieldTarget(ev, TypeIds.I32, -1, ta.Tokens[fd.TokIdx], emitDiags: false, in types, ref diags, out convE);
                            if (okEnum)
                            {
                                ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, (int)convE, 1, fd.TokIdx);
                                nextEnumVal = (long)((int)convE) + 1L;
                                prevReady = true;
                                progress = true;
                            }
                            else
                            {
                                prevReady = false;
                            }
                        }
                        else
                        {
                            if (prevReady && nextEnumVal >= -2147483648L && nextEnumVal <= 2147483647L)
                            {
                                if (fd.HasConstVal == 0 || fd.ConstVal != nextEnumVal)
                                {
                                    ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, (int)nextEnumVal, 1, fd.TokIdx);
                                    progress = true;
                                }
                                nextEnumVal += 1L;
                            }
                            else
                            {
                                prevReady = false;
                            }
                        }
                    }
                    continue;
                }

                for (int f = 0; f < td.FieldCount; f++)
                {
                    int idx = td.FieldStart + f;
                    FieldDecl fd = ast.Fields[idx];
                    if ((fd.Flags & DeclFlags.IsConst) == 0 || fd.HasConstVal != 0 || fd.InitExpr < 0) continue;

                    bool okType = TryResolveConstTargetType(fd.TypeNode, t, in ast, in ta, in names, ref bctx, ref types, ref diags, reportErrors: false, out int fPrimOrTypeId, out int fEnumDeclId);
                    if (!okType)
                    {
                        continue;
                    }
                    bool dummyHadDiag = false;
                    ConstEvalVal ev = EvalConstNode(fd.InitExpr, t, 0, emitDiags: false, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref dummyHadDiag, default, -1, primaryParamsFirstInOwner: true);
                    if (ev.Kind != 0 && TryConvertConstToFieldTarget(ev, fPrimOrTypeId, fEnumDeclId, ta.Tokens[fd.TokIdx], emitDiags: false, in types, ref diags, out long storedBits))
                    {
                        ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, storedBits, 1, fd.TokIdx);
                        progress = true;
                    }
                }
            }
            if (!progress) break;
        }

        // Final diagnostic validation pass for unresolved or invalid user `const` fields and enum members
        for (int t = 0; t < ast.Types.Count; t++)
        {
            TypeDecl td = ast.Types[t];
            if (!bctx.CompilingStdItself && td.UnitKind == UnitKinds.Std) continue;

            if (td.Kind == TypeDeclKinds.Enum)
            {
                long nextEnumVal = 0;
                bool prevReady = true;
                for (int f = 0; f < td.FieldCount; f++)
                {
                    int idx = td.FieldStart + f;
                    FieldDecl fd = ast.Fields[idx];
                    if (fd.HasConstVal != 0)
                    {
                        nextEnumVal = fd.ConstVal + 1L;
                        prevReady = true;
                        continue;
                    }
                    if (fd.InitExpr >= 0)
                    {
                        bool hadDiag = false;
                        ConstEvalVal ev = EvalConstNode(fd.InitExpr, t, 0, emitDiags: true, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, default, -1, primaryParamsFirstInOwner: false);
                        if (ev.Kind == 0)
                        {
                            if (!hadDiag)
                            {
                                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Enum member initializer must be a compile-time constant expression (CS0133)."u8);
                            }
                        }
                        else if (ev.EnumDeclId >= 0)
                        {
                            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Cannot implicitly convert enum-typed initializer to the underlying type (CS0266)."u8);
                        }
                        else if (!TryConvertConstToFieldTarget(ev, TypeIds.I32, -1, ta.Tokens[fd.TokIdx], emitDiags: true, in types, ref diags, out _))
                        {
                            // Diagnostic already emitted
                        }
                        ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, 0, 1, fd.TokIdx);
                        prevReady = false;
                    }
                    else
                    {
                        if (prevReady && (nextEnumVal < -2147483648L || nextEnumVal > 2147483647L))
                        {
                            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Enum member value exceeds 'int.MaxValue' (CS0543)."u8);
                        }
                        ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, 0, 1, fd.TokIdx);
                        prevReady = false;
                    }
                }
                continue;
            }

            for (int f = 0; f < td.FieldCount; f++)
            {
                int idx = td.FieldStart + f;
                FieldDecl fd = ast.Fields[idx];
                if ((fd.Flags & DeclFlags.IsConst) == 0 || fd.HasConstVal != 0 || fd.InitExpr < 0) continue;

                bool okTarget = TryResolveConstTargetType(fd.TypeNode, t, in ast, in ta, in names, ref bctx, ref types, ref diags, reportErrors: true, out int fPrimOrTypeId, out int fEnumDeclId);
                if (!okTarget)
                {
                    ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, 0, 1, fd.TokIdx);
                    continue;
                }

                bool hadDiag = false;
                ConstEvalVal ev = EvalConstNode(fd.InitExpr, t, 0, emitDiags: true, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, default, -1, primaryParamsFirstInOwner: true);
                if (ev.Kind == 0)
                {
                    if (!hadDiag)
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Initializer of 'const' field must be a compile-time constant expression (CS0133)."u8);
                    }
                }
                else
                {
                    _ = TryConvertConstToFieldTarget(ev, fPrimOrTypeId, fEnumDeclId, ta.Tokens[fd.TokIdx], emitDiags: true, in types, ref diags, out _);
                }
                ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, fd.InitExpr, fd.Flags, 0, 1, fd.TokIdx);
            }
        }

        bctx.ConstStrChunks.Clear();
        for (int ci = 0; ci < strChunks.Count; ci++) bctx.ConstStrChunks.Push(strChunks[ci]);

        for (int t = 0; t < ast.Types.Count; t++)
        {
            TypeDecl td = ast.Types[t];
            if (td.Kind == TypeDeclKinds.Enum) continue;
            for (int f = 0; f < td.FieldCount; f++)
            {
                int idx = td.FieldStart + f;
                FieldDecl fd = ast.Fields[idx];
                if ((fd.Flags & DeclFlags.IsConst) != 0 && fd.InitExpr >= 0)
                {
                    ast.Fields[idx] = new FieldDecl(fd.NameId, fd.TypeNode, -1, fd.Flags, fd.ConstVal, fd.HasConstVal, fd.TokIdx);
                }
            }
        }
    }

    private static bool IsConstFieldAccessibleFromOwner(
        int callerOwnerDeclId,
        int targetDeclId,
        in FieldDecl fd,
        in AstArena ast,
        in BinderCtx bctx)
    {
        for (int d = callerOwnerDeclId; d >= 0; d = ast.Types[d].OwnerDeclId)
        {
            if (d == targetDeclId) return true;
        }
        TypeDecl td = ast.Types[targetDeclId];
        int callerUnit = callerOwnerDeclId >= 0 ? ast.Types[callerOwnerDeclId].UnitKind : UnitKinds.App;
        bool typeVisible = bctx.CompilingStdItself || td.UnitKind == callerUnit || (td.Flags & DeclFlags.Public) != 0;
        if (!typeVisible) return false;
        if (td.Kind == TypeDeclKinds.Enum) return true;
        return (fd.Flags & DeclFlags.Private) == 0 &&
               ((fd.Flags & DeclFlags.Public) != 0 || ((fd.Flags & DeclFlags.Internal) != 0 && (bctx.CompilingStdItself || td.UnitKind == callerUnit)));
    }

    public static bool TryEvalBuiltinNumericConstMember(ReadOnlySpan<byte> typeName, ReadOnlySpan<byte> memberName, out int typeId, out long bitsOrVal, out bool isFloat)
    {
        typeId = TypeIds.Error;
        bitsOrVal = 0;
        isFloat = false;
        bool isMax = Bytes.Eq(memberName, "MaxValue"u8);
        bool isMin = Bytes.Eq(memberName, "MinValue"u8);

        if (Bytes.Eq(typeName, "byte"u8) || Bytes.Eq(typeName, "Byte"u8))
        {
            if (isMax) { typeId = TypeIds.U8; bitsOrVal = 255L; return true; }
            if (isMin) { typeId = TypeIds.U8; bitsOrVal = 0L; return true; }
        }
        else if (Bytes.Eq(typeName, "sbyte"u8) || Bytes.Eq(typeName, "SByte"u8))
        {
            if (isMax) { typeId = TypeIds.I8; bitsOrVal = 127L; return true; }
            if (isMin) { typeId = TypeIds.I8; bitsOrVal = -128L; return true; }
        }
        else if (Bytes.Eq(typeName, "short"u8) || Bytes.Eq(typeName, "Int16"u8))
        {
            if (isMax) { typeId = TypeIds.I16; bitsOrVal = 32767L; return true; }
            if (isMin) { typeId = TypeIds.I16; bitsOrVal = -32768L; return true; }
        }
        else if (Bytes.Eq(typeName, "ushort"u8) || Bytes.Eq(typeName, "UInt16"u8))
        {
            if (isMax) { typeId = TypeIds.U16; bitsOrVal = 65535L; return true; }
            if (isMin) { typeId = TypeIds.U16; bitsOrVal = 0L; return true; }
        }
        else if (Bytes.Eq(typeName, "char"u8) || Bytes.Eq(typeName, "Char"u8))
        {
            if (isMax) { typeId = TypeIds.Char; bitsOrVal = 65535L; return true; }
            if (isMin) { typeId = TypeIds.Char; bitsOrVal = 0L; return true; }
        }
        else if (Bytes.Eq(typeName, "int"u8) || Bytes.Eq(typeName, "Int32"u8))
        {
            if (isMax) { typeId = TypeIds.I32; bitsOrVal = 2147483647L; return true; }
            if (isMin) { typeId = TypeIds.I32; bitsOrVal = -2147483648L; return true; }
        }
        else if (Bytes.Eq(typeName, "uint"u8) || Bytes.Eq(typeName, "UInt32"u8))
        {
            if (isMax) { typeId = TypeIds.U32; bitsOrVal = 4294967295L; return true; }
            if (isMin) { typeId = TypeIds.U32; bitsOrVal = 0L; return true; }
        }
        else if (Bytes.Eq(typeName, "long"u8) || Bytes.Eq(typeName, "Int64"u8) || Bytes.Eq(typeName, "nint"u8) || Bytes.Eq(typeName, "IntPtr"u8))
        {
            int tid = (Bytes.Eq(typeName, "nint"u8) || Bytes.Eq(typeName, "IntPtr"u8)) ? TypeIds.NInt : TypeIds.I64;
            if (isMax) { typeId = tid; bitsOrVal = 9223372036854775807L; return true; }
            if (isMin) { typeId = tid; bitsOrVal = -9223372036854775808L; return true; }
        }
        else if (Bytes.Eq(typeName, "ulong"u8) || Bytes.Eq(typeName, "UInt64"u8) || Bytes.Eq(typeName, "nuint"u8) || Bytes.Eq(typeName, "UIntPtr"u8))
        {
            int tid = (Bytes.Eq(typeName, "nuint"u8) || Bytes.Eq(typeName, "UIntPtr"u8)) ? TypeIds.NUInt : TypeIds.U64;
            if (isMax) { typeId = tid; bitsOrVal = -1L; return true; }
            if (isMin) { typeId = tid; bitsOrVal = 0L; return true; }
        }
        else if (Bytes.Eq(typeName, "float"u8) || Bytes.Eq(typeName, "Single"u8))
        {
            typeId = TypeIds.F32;
            isFloat = true;
            if (isMax) { bitsOrVal = 0x7F7FFFFFL; return true; }
            if (isMin) { bitsOrVal = unchecked((int)0xFF7FFFFFU); return true; }
            if (Bytes.Eq(memberName, "Epsilon"u8)) { bitsOrVal = 1L; return true; }
            if (Bytes.Eq(memberName, "PositiveInfinity"u8)) { bitsOrVal = 0x7F800000L; return true; }
            if (Bytes.Eq(memberName, "NegativeInfinity"u8)) { bitsOrVal = unchecked((int)0xFF800000U); return true; }
            if (Bytes.Eq(memberName, "NaN"u8)) { bitsOrVal = unchecked((int)0xFFC00000U); return true; }
        }
        else if (Bytes.Eq(typeName, "double"u8) || Bytes.Eq(typeName, "Double"u8))
        {
            typeId = TypeIds.F64;
            isFloat = true;
            if (isMax) { bitsOrVal = 0x7FEFFFFFFFFFFFFFL; return true; }
            if (isMin) { bitsOrVal = unchecked((long)0xFFEFFFFFFFFFFFFFUL); return true; }
            if (Bytes.Eq(memberName, "Epsilon"u8)) { bitsOrVal = 1L; return true; }
            if (Bytes.Eq(memberName, "PositiveInfinity"u8)) { bitsOrVal = 0x7FF0000000000000L; return true; }
            if (Bytes.Eq(memberName, "NegativeInfinity"u8)) { bitsOrVal = unchecked((long)0xFFF0000000000000UL); return true; }
            if (Bytes.Eq(memberName, "NaN"u8)) { bitsOrVal = unchecked((long)0xFFF8000000000000UL); return true; }
        }
        return false;
    }

    public static bool TryEvalConstExpr(
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in BinderCtx bctx,
        int currentOwnerDeclId,
        int exprId,
        out long val)
    {
        val = 0;
        if (exprId < 0) return false;
        AstNode n = ast.Nodes[exprId];
        switch (n.Kind)
        {
            case AstKind.ExprIntLit:
            {
                Token lit = ta.Tokens[n.A];
                if ((lit.AuxId & 4) != 0) return false;
                val = lit.IntVal;
                return true;
            }
            case AstKind.ExprCharLit:
                val = n.A & 0xFFFFL;
                return true;
            case AstKind.ExprBoolLit:
                val = n.A != 0 ? 1L : 0L;
                return true;
            case AstKind.ExprIdent:
            case AstKind.ExprDefault:
            case AstKind.ExprMember:
            case AstKind.ExprUnary:
            case AstKind.ExprBinary:
            case AstKind.ExprCast:
            case AstKind.ExprCond:
                break;
            default:
                return false;
        }
        var dummyDiags = new DiagnosticBag(0);
        var scratchStrChunks = new Vec<int>();
        bool dummyHadDiag = false;
        ConstEvalVal ev = EvalConstNode(exprId, currentOwnerDeclId, 0, emitDiags: false, in ast, in ta, in names, in bctx, ref scratchStrChunks, ref dummyDiags, ref dummyHadDiag, default, -1, primaryParamsFirstInOwner: false);
        if (ev.Kind == 1 || ev.Kind == 2)
        {
            val = ev.IntVal;
            return true;
        }
        return false;
    }

    private static bool TryResolveConstTargetType(
        int typeNode,
        int ownerDeclId,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags,
        bool reportErrors,
        out int primOrTypeId,
        out int enumDeclId)
    {
        primOrTypeId = TypeIds.Error;
        enumDeclId = -1;

        TypeDecl ownerTd = ast.Types[ownerDeclId];
        var tpNames = new Vec<int>(ownerTd.TypeParamCount);
        var tpVals = new Vec<int>(ownerTd.TypeParamCount);
        for (int i = 0; i < ownerTd.TypeParamCount; i++)
        {
            int tpName = ast.Extra[ownerTd.TypeParamStart + i];
            int constraintFlags = ast.Extra[ownerTd.TypeParamFlagsStart + i];
            tpNames.Push(tpName);
            tpVals.Push(types.MakeTypeParam(tpName, names.Text(tpName), constraintFlags));
        }

        var localDiags = new DiagnosticBag(4);
        int resolved = ResolveTypeNode(typeNode, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref localDiags, reportLinearErrors: reportErrors);
        if (reportErrors)
        {
            for (int i = 0; i < localDiags.Items.Count; i++)
            {
                DiagEntry e = localDiags.Items[i];
                diags.Report(e.Code, e.SourceId, e.ByteOffset, e.Length, localDiags.MsgPool.Get(e.Message));
            }
        }
        if (resolved == TypeIds.Error)
        {
            return false;
        }
        TypeInfo ti = types.Items[resolved];
        if (ti.Kind == TypeKinds.Enum)
        {
            primOrTypeId = TypeIds.I32;
            enumDeclId = ti.A;
            return true;
        }
        if ((resolved >= TypeIds.Bool && resolved <= TypeIds.Str))
        {
            primOrTypeId = resolved;
            enumDeclId = -1;
            return true;
        }
        if (reportErrors)
        {
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[ast.Nodes[typeNode].TokIdx], "Type cannot be declared 'const' in C# / Vibranium (CS0283)."u8);
        }
        return false;
    }

    private static bool TryResolveConstTypeSyntaxInBinder(
        int typeNode,
        int ownerDeclId,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        out int primTypeId,
        out int enumDeclId,
        out bool isAmbiguous,
        out bool isRefClassOrObject)
    {
        primTypeId = TypeIds.Error;
        enumDeclId = -1;
        isAmbiguous = false;
        isRefClassOrObject = false;
        if (typeNode < 0) return false;
        AstNode n = ast.Nodes[typeNode];
        if (n.Kind == AstKind.TypePrim)
        {
            if ((TokKind)n.A == TokKind.KwObject)
            {
                isRefClassOrObject = true;
                return false;
            }
            int m = MapPrimTokKind((TokKind)n.A);
            if (m != TypeIds.Error && m != TypeIds.Void)
            {
                primTypeId = m;
                return true;
            }
            return false;
        }
        if (n.Kind != AstKind.TypeNamed || n.C != 0)
        {
            return false;
        }

        int nameId = n.A;
        TypeRefContext trc = n.D >= 0 && n.D < ast.TypeRefs.Count
            ? ast.TypeRefs[n.D]
            : new TypeRefContext(-1, ownerDeclId >= 0 ? ast.Types[ownerDeclId].UnitKind : UnitKinds.App, ownerDeclId >= 0 ? ast.Types[ownerDeclId].NsId : -1, ownerDeclId, ownerDeclId >= 0 ? ast.Types[ownerDeclId].UsingExtraStart : 0, ownerDeclId >= 0 ? ast.Types[ownerDeclId].UsingCount : 0);

        int declId = -1;
        if (trc.PrefixNsId >= 0)
        {
            if (trc.PrefixNsId == bctx.IdGlobalPrefix)
            {
                if (trc.UnitKind == UnitKinds.App)
                {
                    Option<int> gOpt = bctx.AppGlobalTypeByNameArity.Get(PackNameArity(nameId, 0));
                    if (gOpt.IsSome) declId = gOpt.Unwrap();
                }
            }
            else if (trc.PrefixNsId == bctx.IdGlobalSystem)
            {
                if (nameId == bctx.IdObject) { isRefClassOrObject = true; return false; }
                if (TryMapSystemPrimTypeName(names.Text(nameId), out primTypeId)) return true;
                return false;
            }
            else if (trc.PrefixNsId == bctx.IdGlobalVibraniumStd)
            {
                if (bctx.HasStdUnit || trc.UnitKind == UnitKinds.Std)
                {
                    Option<int> stdOpt = bctx.StdTypeByNameArity.Get(PackNameArity(nameId, 0));
                    if (stdOpt.IsSome) declId = stdOpt.Unwrap();
                }
            }
            else
            {
                if (TryResolvePrefixViaAlias(trc.PrefixNsId, nameId, 0, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out bool prefIsNs, out int prefResolvedNs, out int aliasNestedDecl, out bool prefAmb))
                {
                    if (prefAmb) { isAmbiguous = true; return false; }
                    if (prefIsNs)
                    {
                        if (prefResolvedNs == bctx.IdSystem)
                        {
                            if (nameId == bctx.IdObject) { isRefClassOrObject = true; return false; }
                            if (TryMapSystemPrimTypeName(names.Text(nameId), out primTypeId)) return true;
                            return false;
                        }
                        declId = FindDeclaredTypeInExactNamespace(nameId, 0, prefResolvedNs, trc.UnitKind, in ast, in bctx);
                    }
                    else
                    {
                        declId = aliasNestedDecl;
                    }
                }
                else
                {
                    declId = LookupQualifiedTypeDecl(nameId, 0, trc.PrefixNsId, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx);
                    if (declId < 0 && trc.PrefixNsId == bctx.IdSystem)
                    {
                        if (nameId == bctx.IdObject) { isRefClassOrObject = true; return false; }
                        if (TryMapSystemPrimTypeName(names.Text(nameId), out primTypeId)) return true;
                    }
                }
            }
        }
        else if (trc.UnitKind == UnitKinds.App)
        {
            int resKind = ResolveAppUnqualifiedName(
                nameId,
                0,
                trc.CurrentNsId,
                trc.EnclosingOwnerDeclId,
                trc.UsingExtraStart,
                trc.UsingCount,
                in ast,
                in names,
                in bctx,
                out int foundDecl,
                out int foundAliasIdx,
                out bool matchSys,
                out _);
            if (resKind == -2)
            {
                isAmbiguous = true;
                return false;
            }
            if (resKind == 1)
            {
                declId = foundDecl;
            }
            else if (resKind == 2 && foundAliasIdx >= 0 && foundAliasIdx < bctx.UsingBoundKind.Count)
            {
                int bk = bctx.UsingBoundKind[foundAliasIdx];
                if (bk == 2)
                {
                    declId = bctx.UsingBoundId[foundAliasIdx];
                }
                else if (bk == 3)
                {
                    int bt = bctx.UsingBoundTypeId[foundAliasIdx];
                    if (bt >= TypeIds.Bool && bt <= TypeIds.Str)
                    {
                        primTypeId = bt;
                        return true;
                    }
                }
            }
            else if (resKind == 3)
            {
                if (matchSys)
                {
                    if (nameId == bctx.IdObject) { isRefClassOrObject = true; return false; }
                    return TryMapSystemPrimTypeName(names.Text(nameId), out primTypeId);
                }
                declId = foundDecl;
            }
        }
        else
        {
            declId = FindNestedTypeInEnclosingChain(nameId, 0, trc.EnclosingOwnerDeclId, in ast);
            if (declId < 0)
            {
                Option<int> stdOpt = bctx.StdTypeByNameArity.Get(PackNameArity(nameId, 0));
                if (stdOpt.IsSome) declId = stdOpt.Unwrap();
            }
            if (declId < 0 && TryMapSystemPrimTypeName(names.Text(nameId), out primTypeId))
            {
                return true;
            }
        }

        if (declId >= 0)
        {
            if (ast.Types[declId].Kind == TypeDeclKinds.Enum)
            {
                primTypeId = TypeIds.I32;
                enumDeclId = declId;
                return true;
            }
            if (ast.Types[declId].Kind == TypeDeclKinds.Class && (ast.Types[declId].Flags & DeclFlags.Static) == 0)
            {
                isRefClassOrObject = true;
            }
        }
        return false;
    }

    public static bool TryMapSystemPrimTypeName(ReadOnlySpan<byte> nm, out int primTypeId)
    {
        primTypeId = TypeIds.Error;
        if (Bytes.Eq(nm, "Boolean"u8)) { primTypeId = TypeIds.Bool; return true; }
        if (Bytes.Eq(nm, "Byte"u8)) { primTypeId = TypeIds.U8; return true; }
        if (Bytes.Eq(nm, "SByte"u8)) { primTypeId = TypeIds.I8; return true; }
        if (Bytes.Eq(nm, "Int16"u8)) { primTypeId = TypeIds.I16; return true; }
        if (Bytes.Eq(nm, "UInt16"u8)) { primTypeId = TypeIds.U16; return true; }
        if (Bytes.Eq(nm, "Char"u8)) { primTypeId = TypeIds.Char; return true; }
        if (Bytes.Eq(nm, "Int32"u8)) { primTypeId = TypeIds.I32; return true; }
        if (Bytes.Eq(nm, "UInt32"u8)) { primTypeId = TypeIds.U32; return true; }
        if (Bytes.Eq(nm, "Int64"u8)) { primTypeId = TypeIds.I64; return true; }
        if (Bytes.Eq(nm, "UInt64"u8)) { primTypeId = TypeIds.U64; return true; }
        if (Bytes.Eq(nm, "IntPtr"u8)) { primTypeId = TypeIds.NInt; return true; }
        if (Bytes.Eq(nm, "UIntPtr"u8)) { primTypeId = TypeIds.NUInt; return true; }
        if (Bytes.Eq(nm, "Single"u8)) { primTypeId = TypeIds.F32; return true; }
        if (Bytes.Eq(nm, "Double"u8)) { primTypeId = TypeIds.F64; return true; }
        if (Bytes.Eq(nm, "String"u8)) { primTypeId = TypeIds.Str; return true; }
        return false;
    }

    private static long TruncateConstPrim(long val, int primTypeId)
    {
        switch (primTypeId)
        {
            case TypeIds.Bool: return val != 0 ? 1L : 0L;
            case TypeIds.I8: return (sbyte)val;
            case TypeIds.U8: return (byte)val;
            case TypeIds.I16: return (short)val;
            case TypeIds.U16:
            case TypeIds.Char: return (ushort)val;
            case TypeIds.I32: return (int)val;
            case TypeIds.U32: return (uint)val;
            default: return val;
        }
    }

    private static bool IsUnsignedConstPrim(int primTypeId) =>
        primTypeId == TypeIds.U8 || primTypeId == TypeIds.U16 || primTypeId == TypeIds.Char ||
        primTypeId == TypeIds.U32 || primTypeId == TypeIds.U64 || primTypeId == TypeIds.NUInt;

    private static int ConstIntBitWidth(int primTypeId)
    {
        if (primTypeId == TypeIds.I8 || primTypeId == TypeIds.U8) return 8;
        if (primTypeId == TypeIds.I16 || primTypeId == TypeIds.U16 || primTypeId == TypeIds.Char) return 16;
        if (primTypeId == TypeIds.I32 || primTypeId == TypeIds.U32) return 32;
        if (primTypeId == TypeIds.I64 || primTypeId == TypeIds.U64 || primTypeId == TypeIds.NInt || primTypeId == TypeIds.NUInt) return 64;
        return 0;
    }

    private static bool ConstantIntFitsInTarget(int srcPrimTypeId, long val, int dstPrimTypeId)
    {
        bool srcUns = IsUnsignedConstPrim(srcPrimTypeId);
        bool dstUns = IsUnsignedConstPrim(dstPrimTypeId);
        ulong maxUns = dstPrimTypeId == TypeIds.U8 ? 255UL :
                       (dstPrimTypeId == TypeIds.U16 || dstPrimTypeId == TypeIds.Char) ? 65535UL :
                       dstPrimTypeId == TypeIds.U32 ? 4294967295UL : ulong.MaxValue;
        long minSig = dstPrimTypeId == TypeIds.I8 ? -128L :
                      dstPrimTypeId == TypeIds.I16 ? -32768L :
                      dstPrimTypeId == TypeIds.I32 ? -2147483648L : long.MinValue;
        long maxSig = dstPrimTypeId == TypeIds.I8 ? 127L :
                      dstPrimTypeId == TypeIds.I16 ? 32767L :
                      dstPrimTypeId == TypeIds.I32 ? 2147483647L : long.MaxValue;

        if (srcUns)
        {
            ulong u = (ulong)val;
            if (dstUns) return u <= maxUns;
            return u <= (ulong)maxSig;
        }

        if (dstUns)
        {
            if (val < 0L) return false;
            return (ulong)val <= maxUns;
        }
        return val >= minSig && val <= maxSig;
    }

    private static bool TryConvertFloatToIntChecked(double d, int dstPrimTypeId, out long convInt)
    {
        convInt = 0L;
        if (IsNaNDouble(d) || d == double.PositiveInfinity || d == double.NegativeInfinity)
        {
            return false;
        }
        switch (dstPrimTypeId)
        {
            case TypeIds.I8:
                if (d > -129.0 && d < 128.0) { convInt = (sbyte)(int)d; return true; }
                return false;
            case TypeIds.U8:
                if (d > -1.0 && d < 256.0) { convInt = (byte)(int)d; return true; }
                return false;
            case TypeIds.I16:
                if (d > -32769.0 && d < 32768.0) { convInt = (short)(int)d; return true; }
                return false;
            case TypeIds.U16:
            case TypeIds.Char:
                if (d > -1.0 && d < 65536.0) { convInt = (ushort)(int)d; return true; }
                return false;
            case TypeIds.I32:
            case TypeIds.NInt:
                if (d > -2147483649.0 && d < 2147483648.0) { convInt = (int)(long)d; return true; }
                return false;
            case TypeIds.U32:
            case TypeIds.NUInt:
                if (d > -1.0 && d < 4294967296.0) { convInt = (uint)(long)d; return true; }
                return false;
            case TypeIds.I64:
                if (d >= -9223372036854775808.0 && d < 9223372036854775808.0) { convInt = (long)d; return true; }
                return false;
            case TypeIds.U64:
                if (d > -1.0 && d < 18446744073709551616.0)
                {
                    convInt = d <= 0.0 ? 0L : unchecked((long)(ulong)d);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static bool CanImplicitlyConvertConstInt(int srcTypeId, long val, int dstTypeId)
    {
        if (srcTypeId == dstTypeId) return true;
        switch (srcTypeId)
        {
            case TypeIds.I8:
                return dstTypeId == TypeIds.I16 || dstTypeId == TypeIds.I32 || dstTypeId == TypeIds.I64 || dstTypeId == TypeIds.NInt;
            case TypeIds.U8:
                return dstTypeId == TypeIds.I16 || dstTypeId == TypeIds.U16 || dstTypeId == TypeIds.I32 || dstTypeId == TypeIds.U32 ||
                       dstTypeId == TypeIds.I64 || dstTypeId == TypeIds.U64 || dstTypeId == TypeIds.NInt || dstTypeId == TypeIds.NUInt;
            case TypeIds.I16:
                return dstTypeId == TypeIds.I32 || dstTypeId == TypeIds.I64 || dstTypeId == TypeIds.NInt;
            case TypeIds.U16:
            case TypeIds.Char:
                return dstTypeId == TypeIds.U16 || dstTypeId == TypeIds.I32 || dstTypeId == TypeIds.U32 ||
                       dstTypeId == TypeIds.I64 || dstTypeId == TypeIds.U64 || dstTypeId == TypeIds.NInt || dstTypeId == TypeIds.NUInt;
            case TypeIds.I32:
                if (dstTypeId == TypeIds.I64 || dstTypeId == TypeIds.NInt) return true;
                if (dstTypeId == TypeIds.Char) return false;
                return ConstantIntFitsInTarget(TypeIds.I32, val, dstTypeId);
            case TypeIds.U32:
                return dstTypeId == TypeIds.I64 || dstTypeId == TypeIds.U64 || dstTypeId == TypeIds.NUInt;
            case TypeIds.I64:
                return (dstTypeId == TypeIds.U64 || dstTypeId == TypeIds.NUInt) && val >= 0L;
            default:
                return false;
        }
    }

    private static bool TryConvertConstToFieldTarget(
        ConstEvalVal ev,
        int targetPrimTypeId,
        int targetEnumDeclId,
        Token errTok,
        bool emitDiags,
        in TypeTable types,
        ref DiagnosticBag diags,
        out long storedVal)
    {
        storedVal = 0L;
        if (ev.Kind == 6)
        {
            if (targetPrimTypeId == TypeIds.Str)
            {
                if (emitDiags)
                {
                    diags.ReportTok(VibCodes.NullOrZeroUnsafe, errTok, "'default' initialization of 'string' is banned in Vibranium."u8);
                }
                return false;
            }
            storedVal = 0L;
            return true;
        }
        if (targetEnumDeclId >= 0)
        {
            if (ev.Kind == 1 && ev.EnumDeclId == targetEnumDeclId)
            {
                storedVal = (int)ev.IntVal;
                return true;
            }
            if ((ev.Kind == 1 && ev.EnumDeclId < 0 && ev.TypeId != TypeIds.Char && ev.IntVal == 0L) ||
                ((ev.Kind == 3 || ev.Kind == 4) && ev.FltVal == 0.0))
            {
                storedVal = 0L;
                return true;
            }
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, errTok, "Cannot implicitly convert constant value to enum type."u8);
            }
            return false;
        }

        if (targetPrimTypeId == TypeIds.Bool)
        {
            if (ev.Kind == 2)
            {
                storedVal = ev.IntVal != 0 ? 1L : 0L;
                return true;
            }
            if (emitDiags) diags.ReportTok(VibCodes.Unsupported, errTok, "Constant initializer cannot be converted to 'bool'."u8);
            return false;
        }

        if (targetPrimTypeId == TypeIds.Str)
        {
            if (ev.Kind == 5)
            {
                storedVal = ev.IntVal;
                return true;
            }
            if (emitDiags) diags.ReportTok(VibCodes.Unsupported, errTok, "Constant initializer cannot be converted to 'string'."u8);
            return false;
        }

        if (targetPrimTypeId == TypeIds.F32)
        {
            if (ev.Kind == 3)
            {
                storedVal = ev.IntVal;
                return true;
            }
            if (ev.Kind == 1 && ev.EnumDeclId < 0)
            {
                storedVal = IsUnsignedConstPrim(ev.TypeId)
                    ? ULongToSingleBits((ulong)ev.IntVal)
                    : LongToSingleBits(ev.IntVal);
                return true;
            }
            if (emitDiags) diags.ReportTok(VibCodes.Unsupported, errTok, "Constant initializer cannot be implicitly converted to 'float'."u8);
            return false;
        }

        if (targetPrimTypeId == TypeIds.F64)
        {
            if (ev.Kind == 4)
            {
                storedVal = ev.IntVal;
                return true;
            }
            if (ev.Kind == 3)
            {
                storedVal = DoubleToBits(ev.FltVal);
                return true;
            }
            if (ev.Kind == 1 && ev.EnumDeclId < 0)
            {
                double d = IsUnsignedConstPrim(ev.TypeId) ? (double)(ulong)ev.IntVal : (double)ev.IntVal;
                storedVal = DoubleToBits(d);
                return true;
            }
            if (emitDiags) diags.ReportTok(VibCodes.Unsupported, errTok, "Constant initializer cannot be implicitly converted to 'double'."u8);
            return false;
        }

        if (types.IsInteger(targetPrimTypeId))
        {
            if (ev.Kind == 1 && ev.EnumDeclId < 0 && CanImplicitlyConvertConstInt(ev.TypeId, ev.IntVal, targetPrimTypeId))
            {
                storedVal = TruncateConstPrim(ev.IntVal, targetPrimTypeId);
                return true;
            }
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, errTok, "Constant value cannot be converted to target integer type (CS0031)."u8);
            }
            return false;
        }

        return false;
    }

    // C# enum members: within an enum member initializer, the values of enum constants
    // (of any enum type) are treated as having the enum's underlying type, so they
    // combine with integers without casts. Vibranium enums always have underlying 'int'.
    // Only a constant field access gets this typing; a cast '(E)k' keeps its enum type.
    private static ConstEvalVal AsEnumInitializerOperand(ConstEvalVal v, int currentOwnerDeclId, in AstArena ast)
    {
        if (v.Kind == 1 && v.EnumDeclId >= 0 && currentOwnerDeclId >= 0 &&
            ast.Types[currentOwnerDeclId].Kind == TypeDeclKinds.Enum)
        {
            return ConstEvalVal.MakeInt(TypeIds.I32, unchecked((int)v.IntVal));
        }
        return v;
    }

    private static ConstEvalVal ReconstructConstFieldVal(
        int ownerDeclId,
        in FieldDecl fd,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx)
    {
        TypeDecl owner = ast.Types[ownerDeclId];
        if (owner.Kind == TypeDeclKinds.Enum)
        {
            return ConstEvalVal.MakeEnum(ownerDeclId, fd.ConstVal);
        }
        if (TryResolveConstTypeSyntaxInBinder(fd.TypeNode, ownerDeclId, in ast, in names, in bctx, out int primT, out int enumDecl, out _, out _))
        {
            if (enumDecl >= 0) return ConstEvalVal.MakeEnum(enumDecl, fd.ConstVal);
            if (primT == TypeIds.Bool) return ConstEvalVal.MakeBool(fd.ConstVal != 0);
            if (primT == TypeIds.F32) return new ConstEvalVal(3, TypeIds.F32, -1, fd.ConstVal, (double)BitsToFloat(unchecked((int)fd.ConstVal)));
            if (primT == TypeIds.F64) return new ConstEvalVal(4, TypeIds.F64, -1, fd.ConstVal, BitsToDouble(fd.ConstVal));
            if (primT == TypeIds.Str) return ConstEvalVal.MakeStr(fd.ConstVal);
            return ConstEvalVal.MakeInt(primT, fd.ConstVal);
        }
        return ConstEvalVal.MakeInt(TypeIds.I32, fd.ConstVal);
    }

    private static bool TryExtractNamespaceChainInConst(int exprId, in AstArena ast, in Interner names, ref StringBuf outNs, out bool rootedGlobal)
    {
        rootedGlobal = false;
        AstNode n = ast.Nodes[exprId];
        if (n.Kind == AstKind.ExprIdent && n.C == 0)
        {
            rootedGlobal = n.D == 1;
            outNs.Append(names.Text(n.A));
            return true;
        }
        if (n.Kind == AstKind.ExprMember && n.D == 0)
        {
            if (!TryExtractNamespaceChainInConst(n.A, in ast, in names, ref outNs, out rootedGlobal))
            {
                return false;
            }
            outNs.AppendAscii((byte)'.');
            outNs.Append(names.Text(n.B));
            return true;
        }
        return false;
    }

    private static int FindInternedNameReadOnly(ReadOnlySpan<byte> text, in AstArena ast, in Interner names)
    {
        for (int i = 0; i < ast.Types.Count; i++)
        {
            int nsId = ast.Types[i].NsId;
            if (nsId >= 0 && Bytes.Eq(names.Text(nsId), text)) return nsId;
            int nmId = ast.Types[i].NameId;
            if (nmId >= 0 && Bytes.Eq(names.Text(nmId), text)) return nmId;
        }
        return -1;
    }

    private static bool HasPrimaryCtorParam(in TypeDecl owner, int nameId, in AstArena ast)
    {
        for (int m = 0; m < owner.FuncCount; m++)
        {
            FuncDecl ctor = ast.Funcs[owner.FuncStart + m];
            if ((ctor.Flags & DeclFlags.IsCtor) == 0 || (ctor.Flags & DeclFlags.Partial) != 0 || ctor.TokIdx != owner.TokIdx) continue;
            for (int p = 0; p < ctor.ParamCount; p++)
            {
                if (ast.Params[ctor.ParamStart + p].NameId == nameId) return true;
            }
        }
        return false;
    }

    private static ConstEvalVal EvalConstNode(
        int exprId,
        int currentOwnerDeclId,
        int uncheckedDepth,
        bool emitDiags,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in BinderCtx bctx,
        ref Vec<int> strChunks,
        ref DiagnosticBag diags,
        ref bool hadDiag,
        ReadOnlySpan<SourceLexicalBinding> localBindings,
        int localLimitTokIdx,
        bool primaryParamsFirstInOwner)
    {
        if (exprId < 0) return ConstEvalVal.Invalid();
        AstNode n = ast.Nodes[exprId];
        Token tok = ta.Tokens[n.TokIdx];

        switch (n.Kind)
        {
            case AstKind.ExprIntLit:
            {
                Token lit = ta.Tokens[n.A];
                if ((lit.AuxId & 4) != 0)
                {
                    if (emitDiags)
                    {
                        diags.ReportTok(VibCodes.Unsupported, lit, "Integral constant is too large."u8);
                        hadDiag = true;
                    }
                    return ConstEvalVal.Invalid();
                }
                int tid = TypeIds.I32;
                if (lit.Kind == TokKind.LongLit) tid = TypeIds.I64;
                else if (lit.Kind == TokKind.UIntLit) tid = TypeIds.U32;
                else if (lit.Kind == TokKind.ULongLit) tid = TypeIds.U64;
                return ConstEvalVal.MakeInt(tid, lit.IntVal);
            }

            case AstKind.ExprCharLit:
                return ConstEvalVal.MakeInt(TypeIds.Char, n.A & 0xFFFFL);

            case AstKind.ExprBoolLit:
                return ConstEvalVal.MakeBool(n.A != 0);

            case AstKind.ExprStrLit:
                return ConstEvalVal.MakeStr(n.A);

            case AstKind.ExprFloatLit:
            {
                Token fTok = ta.Tokens[n.A];
                bool isSingle = fTok.Kind == TokKind.FloatLit;
                if (!TryParseExactDecimalFloat(ta.GetLit(fTok.AuxId), isSingle, out long exactBits))
                {
                    if (emitDiags)
                    {
                        diags.ReportTok(VibCodes.Unsupported, fTok, "Floating-point constant is outside the range of the target type (CS0594)."u8);
                        hadDiag = true;
                    }
                    return ConstEvalVal.Invalid();
                }
                return isSingle
                    ? ConstEvalVal.MakeF32Bits(unchecked((int)exactBits))
                    : ConstEvalVal.MakeF64Bits(exactBits);
            }

            case AstKind.ExprDefault:
            {
                if (n.A < 0)
                {
                    return ConstEvalVal.MakeDefaultLiteral();
                }
                if (TryResolveConstTypeSyntaxInBinder(n.A, currentOwnerDeclId, in ast, in names, in bctx, out int defPrim, out int defEnum, out bool defAmb, out bool defIsRefClass))
                {
                    if (defPrim == TypeIds.Str)
                    {
                        if (emitDiags)
                        {
                            diags.ReportTok(VibCodes.NullOrZeroUnsafe, tok, "'default' initialization of 'string' is banned in Vibranium."u8);
                            hadDiag = true;
                        }
                        return ConstEvalVal.Invalid();
                    }
                    if (defEnum >= 0) return ConstEvalVal.MakeEnum(defEnum, 0L);
                    if (defPrim == TypeIds.Bool) return ConstEvalVal.MakeBool(false);
                    if (defPrim == TypeIds.F32) return ConstEvalVal.MakeF32(0.0f);
                    if (defPrim == TypeIds.F64) return ConstEvalVal.MakeF64(0.0);
                    if (defPrim >= TypeIds.I8 && defPrim <= TypeIds.NUInt) return ConstEvalVal.MakeInt(defPrim, 0L);
                }
                else
                {
                    if (defIsRefClass && emitDiags)
                    {
                        diags.ReportTok(VibCodes.NullOrZeroUnsafe, tok, "'default' initialization of reference type is banned in Vibranium."u8);
                        hadDiag = true;
                    }
                    else if (defAmb && emitDiags)
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference in 'default(...)'."u8);
                        hadDiag = true;
                    }
                }
                return ConstEvalVal.Invalid();
            }

            case AstKind.ExprIdent:
            {
                if (n.C != 0 || n.D != 0) return ConstEvalVal.Invalid();
                int limitTok = localLimitTokIdx >= 0 ? localLimitTokIdx : n.TokIdx;
                for (int i = localBindings.Length - 1; i >= 0; i--)
                {
                    SourceLexicalBinding binding = localBindings[i];
                    if (binding.NameId != n.A) continue;
                    if (!SourceLexicalScopeChecker.IsConstant(in binding, in ast) || binding.TokIdx >= limitTok)
                        return ConstEvalVal.Invalid();
                    int scopeEnd = i + 1;
                    while (scopeEnd < localBindings.Length &&
                           localBindings[scopeEnd].FunctionId == binding.FunctionId &&
                           localBindings[scopeEnd].Scope == binding.Scope)
                    {
                        scopeEnd += 1;
                    }
                    // The initializer can only reference constants in this or outer scopes declared before this binding.
                    return EvalConstNode(ast.Nodes[binding.NodeId].C, currentOwnerDeclId, uncheckedDepth, emitDiags,
                        in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag,
                        localBindings.Slice(0, scopeEnd), binding.TokIdx, primaryParamsFirstInOwner: false);
                }
                ReadOnlySpan<byte> identText = names.Text(n.A);
                for (int d = currentOwnerDeclId; d >= 0; d = ast.Types[d].OwnerDeclId)
                {
                    TypeDecl owner = ast.Types[d];
                    bool primaryFirst = primaryParamsFirstInOwner && d == currentOwnerDeclId;
                    if (primaryFirst && HasPrimaryCtorParam(in owner, n.A, in ast))
                        return ConstEvalVal.Invalid();
                    for (int f = 0; f < owner.FieldCount; f++)
                    {
                        FieldDecl fd = ast.Fields[owner.FieldStart + f];
                        if ((fd.Flags & DeclFlags.IsCapturedPrimaryParam) != 0 || fd.NameId != n.A) continue;
                        if ((fd.Flags & DeclFlags.IsConst) == 0 || fd.HasConstVal == 0) return ConstEvalVal.Invalid();
                        return AsEnumInitializerOperand(ReconstructConstFieldVal(d, in fd, in ast, in names, in bctx), currentOwnerDeclId, in ast);
                    }
                    for (int m = 0; m < owner.FuncCount; m++)
                    {
                        FuncDecl mf = ast.Funcs[owner.FuncStart + m];
                        if ((mf.Flags & (DeclFlags.IsLocalFunc | DeclFlags.IsCtor | DeclFlags.IsTopLevelMain | DeclFlags.IsIndexer | DeclFlags.IsOpAddAssign)) != 0) continue;
                        int kind = (mf.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet)) != 0 ? MemberProperty : MemberMethod;
                        if (Bytes.Eq(MemberNameText(owner.FuncStart + m, kind, in ast, in names), identText))
                            return ConstEvalVal.Invalid();
                    }
                    if (!primaryFirst && HasPrimaryCtorParam(in owner, n.A, in ast))
                        return ConstEvalVal.Invalid();
                }
                int identNs = currentOwnerDeclId >= 0 ? ast.Types[currentOwnerDeclId].NsId : -1;
                int identSrcId = tok.SourceId;
                int identUStart = currentOwnerDeclId >= 0
                    ? ast.Types[currentOwnerDeclId].UsingExtraStart
                    : (identSrcId >= 0 && identSrcId < ast.SourceUsingStarts.Count ? ast.SourceUsingStarts[identSrcId] : 0);
                int identUCount = currentOwnerDeclId >= 0
                    ? ast.Types[currentOwnerDeclId].UsingCount
                    : (identSrcId >= 0 && identSrcId < ast.SourceUsingCounts.Count ? ast.SourceUsingCounts[identSrcId] : 0);
                if (TryEvalImportedStaticConstMember(n.A, currentOwnerDeclId, identNs, identUStart, identUCount, tok, emitDiags, in ast, in names, in bctx, ref diags, ref hadDiag, out ConstEvalVal staticVal))
                {
                    return AsEnumInitializerOperand(staticVal, currentOwnerDeclId, in ast);
                }
                return ConstEvalVal.Invalid();
            }

            case AstKind.ExprMember:
            {
                if (n.D != 0) return ConstEvalVal.Invalid();
                int callerUnit = currentOwnerDeclId >= 0 ? ast.Types[currentOwnerDeclId].UnitKind : UnitKinds.App;
                int callerNs = currentOwnerDeclId >= 0 ? ast.Types[currentOwnerDeclId].NsId : -1;
                int srcId = tok.SourceId;
                int uStart = currentOwnerDeclId >= 0
                    ? ast.Types[currentOwnerDeclId].UsingExtraStart
                    : (srcId >= 0 && srcId < ast.SourceUsingStarts.Count ? ast.SourceUsingStarts[srcId] : 0);
                int uCount = currentOwnerDeclId >= 0
                    ? ast.Types[currentOwnerDeclId].UsingCount
                    : (srcId >= 0 && srcId < ast.SourceUsingCounts.Count ? ast.SourceUsingCounts[srcId] : 0);

                AstNode target = ast.Nodes[n.A];
                if (target.Kind == AstKind.ExprIdent && target.C == 0)
                {
                    if (target.D == 2)
                    {
                        ReadOnlySpan<byte> primTxt = names.Text(target.A);
                        if (Bytes.Eq(primTxt, "string"u8) && Bytes.Eq(names.Text(n.B), "Empty"u8))
                        {
                            return ConstEvalVal.Invalid();
                        }
                        if (TryEvalBuiltinNumericConstMember(primTxt, names.Text(n.B), out int pTid, out long pBits, out bool pIsFloat))
                        {
                            if (!pIsFloat) return ConstEvalVal.MakeInt(pTid, pBits);
                            if (pTid == TypeIds.F32) return ConstEvalVal.MakeF32Bits(unchecked((int)pBits));
                            return ConstEvalVal.MakeF64Bits(pBits);
                        }
                        return ConstEvalVal.Invalid();
                    }

                    int resolvedDecl = -1;
                    int resolvedPrimType = -1;
                    int resolvedBuiltinStatic = 0;
                    int unqualifiedKind = 0;

                    if (target.D == 1)
                    {
                        if (callerUnit == UnitKinds.App)
                        {
                            Option<int> gOpt = bctx.AppGlobalTypeByNameArity.Get(PackNameArity(target.A, 0));
                            if (gOpt.IsSome) resolvedDecl = gOpt.Unwrap();
                        }
                    }
                    else if (callerUnit == UnitKinds.App)
                    {
                        int rk = ResolveAppUnqualifiedName(target.A, 0, callerNs, currentOwnerDeclId, uStart, uCount, in ast, in names, in bctx, out int foundDecl, out int foundAliasIdx, out bool mSys, out _);
                        unqualifiedKind = rk;
                        if (rk == -2)
                        {
                            if (emitDiags)
                            {
                                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[target.TokIdx], "Ambiguous type reference between imported namespaces (CS0104)."u8);
                                hadDiag = true;
                            }
                            return ConstEvalVal.Invalid();
                        }
                        if (rk == 1)
                        {
                            resolvedDecl = foundDecl;
                        }
                        else if (rk == 2 && foundAliasIdx >= 0 && foundAliasIdx < bctx.UsingBoundKind.Count)
                        {
                            int bk = bctx.UsingBoundKind[foundAliasIdx];
                            if (bk == 2) resolvedDecl = bctx.UsingBoundId[foundAliasIdx];
                            else if (bk == 3)
                            {
                                resolvedBuiltinStatic = bctx.UsingBoundId[foundAliasIdx];
                                resolvedPrimType = bctx.UsingBoundTypeId[foundAliasIdx];
                            }
                        }
                        else if (rk == 3)
                        {
                            if (!mSys) resolvedDecl = foundDecl;
                        }
                    }
                    else
                    {
                        resolvedDecl = LookupUnqualifiedTypeDecl(target.A, 0, callerUnit, callerNs, currentOwnerDeclId, uStart, uCount, in ast, in names, in bctx);
                    }

                    if (resolvedDecl < 0)
                    {
                        int bTid = 0;
                        long bBits = 0;
                        bool bIsFloat = false;
                        bool matchedNumConst;
                        if (resolvedPrimType > 0)
                        {
                            matchedNumConst = TryEvalBuiltinNumericConstMember(PrimTypeIdBuiltinName(resolvedPrimType), names.Text(n.B), out bTid, out bBits, out bIsFloat);
                        }
                        else
                        {
                            ReadOnlySpan<byte> tgtTxt = names.Text(target.A);
                            matchedNumConst = TryEvalBuiltinNumericConstMember(tgtTxt, names.Text(n.B), out bTid, out bBits, out bIsFloat);
                        }
                        if (matchedNumConst)
                        {
                            if (!bIsFloat) return ConstEvalVal.MakeInt(bTid, bBits);
                            if (bTid == TypeIds.F32) return ConstEvalVal.MakeF32Bits(unchecked((int)bBits));
                            return ConstEvalVal.MakeF64Bits(bBits);
                        }
                    }
                    else
                    {
                        TypeDecl td = ast.Types[resolvedDecl];
                        for (int f = 0; f < td.FieldCount; f++)
                        {
                            FieldDecl fd = ast.Fields[td.FieldStart + f];
                            if (fd.NameId == n.B && (fd.Flags & DeclFlags.IsConst) != 0)
                            {
                                if (!IsConstFieldAccessibleFromOwner(currentOwnerDeclId, resolvedDecl, in fd, in ast, in bctx))
                                {
                                    if (emitDiags)
                                    {
                                        diags.ReportTok(VibCodes.Unsupported, tok, "Constant member is inaccessible due to its protection level."u8);
                                        hadDiag = true;
                                    }
                                    return ConstEvalVal.Invalid();
                                }
                                if (fd.HasConstVal == 0) return ConstEvalVal.Invalid();
                                return AsEnumInitializerOperand(ReconstructConstFieldVal(resolvedDecl, in fd, in ast, in names, in bctx), currentOwnerDeclId, in ast);
                            }
                        }
                    }
                    return ConstEvalVal.Invalid();
                }

                if (target.Kind == AstKind.ExprMember && target.D == 0)
                {
                    var nsBuf = new StringBuf(64);
                    if (TryExtractNamespaceChainInConst(target.A, in ast, in names, ref nsBuf, out bool rootedGlobal))
                    {
                        ReadOnlySpan<byte> nsSpan = nsBuf.AsUtf8();
                        int nsNameId = FindInternedTextReadOnly(nsSpan, in names);
                        int qDecl = -1;
                        bool isSysNs = Bytes.Eq(nsSpan, "System"u8);

                        if (!rootedGlobal && nsNameId >= 0 &&
                            TryResolvePrefixViaAlias(nsNameId, target.B, 0, callerUnit, callerNs, currentOwnerDeclId, uStart, uCount, in ast, in names, in bctx, out bool prefIsNs, out int prefResolvedNs, out int aliasNested, out bool prefAmb))
                        {
                            if (prefAmb)
                            {
                                if (emitDiags)
                                {
                                    diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference between imported namespaces (CS0104)."u8);
                                    hadDiag = true;
                                }
                                return ConstEvalVal.Invalid();
                            }
                            if (prefIsNs)
                            {
                                isSysNs = prefResolvedNs == bctx.IdSystem;
                                if (!isSysNs) qDecl = FindDeclaredTypeInExactNamespace(target.B, 0, prefResolvedNs, callerUnit, in ast, in bctx);
                            }
                            else
                            {
                                qDecl = aliasNested;
                            }
                        }
                        else if (nsNameId >= 0 && !isSysNs)
                        {
                            qDecl = rootedGlobal
                                ? FindDeclaredTypeInExactNamespace(target.B, 0, nsNameId, callerUnit, in ast, in bctx)
                                : LookupQualifiedTypeDecl(target.B, 0, nsNameId, callerUnit, callerNs, currentOwnerDeclId, uStart, uCount, in ast, in names, in bctx);
                        }

                        if (isSysNs)
                        {
                            if (TryEvalBuiltinNumericConstMember(names.Text(target.B), names.Text(n.B), out int sTid, out long sBits, out bool sIsFlt))
                            {
                                if (!sIsFlt) return ConstEvalVal.MakeInt(sTid, sBits);
                                if (sTid == TypeIds.F32) return ConstEvalVal.MakeF32Bits(unchecked((int)sBits));
                                return ConstEvalVal.MakeF64Bits(sBits);
                            }
                        }
                        if (qDecl >= 0)
                        {
                            TypeDecl qtd = ast.Types[qDecl];
                            for (int f = 0; f < qtd.FieldCount; f++)
                            {
                                FieldDecl fd = ast.Fields[qtd.FieldStart + f];
                                if (fd.NameId == n.B && (fd.Flags & DeclFlags.IsConst) != 0)
                                {
                                    if (!IsConstFieldAccessibleFromOwner(currentOwnerDeclId, qDecl, in fd, in ast, in bctx))
                                    {
                                        if (emitDiags)
                                        {
                                            diags.ReportTok(VibCodes.Unsupported, tok, "Constant member is inaccessible due to its protection level."u8);
                                            hadDiag = true;
                                        }
                                        return ConstEvalVal.Invalid();
                                    }
                                    if (fd.HasConstVal == 0) return ConstEvalVal.Invalid();
                                    return AsEnumInitializerOperand(ReconstructConstFieldVal(qDecl, in fd, in ast, in names, in bctx), currentOwnerDeclId, in ast);
                                }
                            }
                        }
                    }
                }
                return ConstEvalVal.Invalid();
            }

            case AstKind.ExprUnary:
            {
                if (n.C != 0) return ConstEvalVal.Invalid();
                TokKind uop = (TokKind)n.A;
                if (uop == TokKind.KwUnchecked)
                {
                    return EvalConstNode(n.B, currentOwnerDeclId, uncheckedDepth + 1, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
                }
                if (uop == TokKind.KwChecked)
                {
                    if (emitDiags)
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "'checked(...)' expressions are banned in Vibranium."u8);
                        hadDiag = true;
                    }
                    return ConstEvalVal.Invalid();
                }

                // C# §12.8.7.2: `-2147483648` -> int.MinValue and `-9223372036854775808` / `-9223372036854775808L` -> long.MinValue
                if (uop == TokKind.Minus && ast.Nodes[n.B].Kind == AstKind.ExprIntLit)
                {
                    Token lit = ta.Tokens[ast.Nodes[n.B].A];
                    if ((lit.AuxId & 5) == 0 && (ulong)lit.IntVal == 2147483648UL && lit.Kind != TokKind.LongLit)
                    {
                        return ConstEvalVal.MakeInt(TypeIds.I32, -2147483648L);
                    }
                    if ((lit.AuxId & 5) == 0 && (ulong)lit.IntVal == 9223372036854775808UL)
                    {
                        return ConstEvalVal.MakeInt(TypeIds.I64, unchecked((long)9223372036854775808UL));
                    }
                }

                ConstEvalVal inner = EvalConstNode(n.B, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
                if (inner.Kind == 0) return ConstEvalVal.Invalid();

                if (uop == TokKind.Bang)
                {
                    if (inner.Kind == 2) return ConstEvalVal.MakeBool(inner.IntVal == 0);
                    return ConstEvalVal.Invalid();
                }
                if (uop == TokKind.Tilde)
                {
                    if (inner.Kind != 1) return ConstEvalVal.Invalid();
                    if (inner.EnumDeclId >= 0) return ConstEvalVal.MakeEnum(inner.EnumDeclId, ~inner.IntVal);
                    int promT = ConstIntBitWidth(inner.TypeId) < 32 ? TypeIds.I32 : inner.TypeId;
                    return ConstEvalVal.MakeInt(promT, ~inner.IntVal);
                }
                if (uop == TokKind.Plus)
                {
                    if (inner.Kind == 3 || inner.Kind == 4) return inner;
                    if (inner.Kind == 1 && inner.EnumDeclId < 0)
                    {
                        int promT = ConstIntBitWidth(inner.TypeId) < 32 ? TypeIds.I32 : inner.TypeId;
                        return ConstEvalVal.MakeInt(promT, inner.IntVal);
                    }
                    return ConstEvalVal.Invalid();
                }
                if (uop == TokKind.Minus)
                {
                    if (inner.Kind == 3) return ConstEvalVal.MakeF32((float)(-inner.FltVal));
                    if (inner.Kind == 4) return ConstEvalVal.MakeF64(-inner.FltVal);
                    if (inner.Kind == 1 && inner.EnumDeclId < 0)
                    {
                        if (inner.TypeId == TypeIds.U64 || inner.TypeId == TypeIds.NUInt)
                        {
                            if (emitDiags)
                            {
                                diags.ReportTok(VibCodes.Unsupported, tok, "Unary '-' cannot be applied to 'ulong' or 'nuint'."u8);
                                hadDiag = true;
                            }
                            return ConstEvalVal.Invalid();
                        }
                        if (inner.TypeId == TypeIds.U32)
                        {
                            return ConstEvalVal.MakeInt(TypeIds.I64, -inner.IntVal);
                        }
                        int promT = ConstIntBitWidth(inner.TypeId) < 32 ? TypeIds.I32 : inner.TypeId;
                        if ((promT == TypeIds.I32 && inner.IntVal == -2147483648L) ||
                            ((promT == TypeIds.I64 || promT == TypeIds.NInt) && inner.IntVal == long.MinValue))
                        {
                            if (uncheckedDepth == 0)
                            {
                                if (emitDiags)
                                {
                                    diags.ReportTok(VibCodes.Unsupported, tok, "Compile-time constant negation overflows in checked mode (CS0220)."u8);
                                    hadDiag = true;
                                }
                                return ConstEvalVal.Invalid();
                            }
                        }
                        return ConstEvalVal.MakeInt(promT, unchecked(-inner.IntVal));
                    }
                    return ConstEvalVal.Invalid();
                }
                return ConstEvalVal.Invalid();
            }

            case AstKind.ExprCast:
            {
                if (!TryResolveConstTypeSyntaxInBinder(n.A, currentOwnerDeclId, in ast, in names, in bctx, out int dstPrim, out int dstEnum, out bool castAmb, out _))
                {
                    if (castAmb && emitDiags)
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[ast.Nodes[n.A].TokIdx], "Ambiguous type reference between imported namespaces (CS0104)."u8);
                        hadDiag = true;
                    }
                    return ConstEvalVal.Invalid();
                }

                ConstEvalVal src = EvalConstNode(n.B, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
                if (src.Kind == 0) return ConstEvalVal.Invalid();

                if (dstEnum >= 0 || (dstPrim >= TypeIds.I8 && dstPrim <= TypeIds.NUInt))
                {
                    int undDst = dstEnum >= 0 ? TypeIds.I32 : dstPrim;
                    if (src.Kind == 1)
                    {
                        int undSrc = src.EnumDeclId >= 0 ? TypeIds.I32 : src.TypeId;
                        // Native integer constant conversions must fit the portable
                        // Int32/UInt32 range, even in an unchecked context. A wider
                        // source is a runtime cast, not a constant-field initializer.
                        if ((undDst == TypeIds.NInt || undDst == TypeIds.NUInt) &&
                            !ConstantIntFitsInTarget(undSrc, src.IntVal, undDst == TypeIds.NInt ? TypeIds.I32 : TypeIds.U32))
                        {
                            return ConstEvalVal.Invalid();
                        }
                        if (uncheckedDepth == 0 && !ConstantIntFitsInTarget(undSrc, src.IntVal, undDst))
                        {
                            if (emitDiags)
                            {
                                diags.ReportTok(VibCodes.Unsupported, tok, "Constant value cannot be converted to target integer or enum type in checked context (CS0221); wrap in 'unchecked(...)' if truncation is intended."u8);
                                hadDiag = true;
                            }
                            return ConstEvalVal.Invalid();
                        }
                        return dstEnum >= 0
                            ? ConstEvalVal.MakeEnum(dstEnum, src.IntVal)
                            : ConstEvalVal.MakeInt(dstPrim, src.IntVal);
                    }
                    if (src.Kind == 3 || src.Kind == 4)
                    {
                        if (TryConvertFloatToIntChecked(src.FltVal, undDst, out long convInt))
                        {
                            return dstEnum >= 0
                                ? ConstEvalVal.MakeEnum(dstEnum, convInt)
                                : ConstEvalVal.MakeInt(dstPrim, convInt);
                        }
                        // Roslyn native constants are limited to Int32/UInt32.
                        // Outside that range (also NaN/infinity), this is a runtime
                        // cast, not a legal constant-field initializer.
                        if (undDst == TypeIds.NInt || undDst == TypeIds.NUInt) return ConstEvalVal.Invalid();
                        if (uncheckedDepth == 0)
                        {
                            if (emitDiags)
                            {
                                diags.ReportTok(VibCodes.Unsupported, tok, "Floating-point constant cannot be converted to target integer or enum type in checked context (CS0221); wrap in 'unchecked(...)' if intended."u8);
                                hadDiag = true;
                            }
                            return ConstEvalVal.Invalid();
                        }
                        // Roslyn ConstantValue folding: out-of-range / NaN / Infinity float-to-integer in unchecked folds to 0!
                        return dstEnum >= 0
                            ? ConstEvalVal.MakeEnum(dstEnum, 0L)
                            : ConstEvalVal.MakeInt(dstPrim, 0L);
                    }
                    return ConstEvalVal.Invalid();
                }

                if (dstPrim == TypeIds.F32)
                {
                    if (src.Kind == 3) return src;
                    if (src.Kind == 4) return ConstEvalVal.MakeF32((float)src.FltVal);
                    if (src.Kind == 1)
                    {
                        int undSrc = src.EnumDeclId >= 0 ? TypeIds.I32 : src.TypeId;
                        int bits = IsUnsignedConstPrim(undSrc)
                            ? ULongToSingleBits((ulong)src.IntVal)
                            : LongToSingleBits(src.IntVal);
                        return ConstEvalVal.MakeF32Bits(bits);
                    }
                    if (src.Kind == 6) return ConstEvalVal.MakeF32(0.0f);
                    return ConstEvalVal.Invalid();
                }

                if (dstPrim == TypeIds.F64)
                {
                    if (src.Kind == 3 || src.Kind == 4) return ConstEvalVal.MakeF64(src.FltVal);
                    if (src.Kind == 1)
                    {
                        int undSrc = src.EnumDeclId >= 0 ? TypeIds.I32 : src.TypeId;
                        double d = IsUnsignedConstPrim(undSrc) ? (double)(ulong)src.IntVal : (double)src.IntVal;
                        return ConstEvalVal.MakeF64(d);
                    }
                    return ConstEvalVal.Invalid();
                }

                if (dstPrim == TypeIds.Bool && src.Kind == 2) return src;
                if (dstPrim == TypeIds.Str && src.Kind == 5) return src;
                return ConstEvalVal.Invalid();
            }

            case AstKind.ExprCond:
            {
                ConstEvalVal cond = EvalConstNode(n.A, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
                ConstEvalVal whenTrue = EvalConstNode(n.B, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
                ConstEvalVal whenFalse = EvalConstNode(n.C, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
                if (cond.Kind != 2 || whenTrue.Kind == 0 || whenFalse.Kind == 0)
                {
                    return ConstEvalVal.Invalid();
                }
                if (TryMergeConstCondArms(whenTrue, whenFalse, cond.IntVal != 0, tok, emitDiags, ref diags, ref hadDiag, out ConstEvalVal merged))
                {
                    return merged;
                }
                return ConstEvalVal.Invalid();
            }

            case AstKind.ExprBinary:
                return EvalConstBinary(in n, tok, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);

            default:
                return ConstEvalVal.Invalid();
        }
    }

    private static ConstEvalVal EvalConstBinary(
        in AstNode n,
        Token tok,
        int currentOwnerDeclId,
        int uncheckedDepth,
        bool emitDiags,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in BinderCtx bctx,
        ref Vec<int> strChunks,
        ref DiagnosticBag diags,
        ref bool hadDiag,
        ReadOnlySpan<SourceLexicalBinding> localBindings,
        int localLimitTokIdx,
        bool primaryParamsFirstInOwner)
    {
        TokKind bop = (TokKind)n.A;
        ConstEvalVal l = EvalConstNode(n.B, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
        ConstEvalVal r = EvalConstNode(n.C, currentOwnerDeclId, uncheckedDepth, emitDiags, in ast, in ta, in names, in bctx, ref strChunks, ref diags, ref hadDiag, localBindings, localLimitTokIdx, primaryParamsFirstInOwner);
        if (l.Kind == 0 || r.Kind == 0) return ConstEvalVal.Invalid();

        // String '+' and '=='/'!=' are banned in Vibranium (docs/SPEC.md:58)
        if (l.Kind == 5 || r.Kind == 5)
        {
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "String '+' concatenation and '=='/'!=' operators are banned in Vibranium; use UTF-8 byte spans instead."u8);
                hadDiag = true;
            }
            return ConstEvalVal.Invalid();
        }

        // Boolean operators
        if (l.Kind == 2 && r.Kind == 2)
        {
            switch (bop)
            {
                case TokKind.AmpAmp: return ConstEvalVal.MakeBool(l.IntVal != 0 && r.IntVal != 0);
                case TokKind.PipePipe: return ConstEvalVal.MakeBool(l.IntVal != 0 || r.IntVal != 0);
                case TokKind.Amp: return ConstEvalVal.MakeBool((l.IntVal & r.IntVal) != 0);
                case TokKind.Pipe: return ConstEvalVal.MakeBool((l.IntVal | r.IntVal) != 0);
                case TokKind.Caret: return ConstEvalVal.MakeBool((l.IntVal ^ r.IntVal) != 0);
                case TokKind.EqEq: return ConstEvalVal.MakeBool(l.IntVal == r.IntVal);
                case TokKind.BangEq: return ConstEvalVal.MakeBool(l.IntVal != r.IntVal);
                default: return ReportNoConstBinaryOperator(tok, emitDiags, ref diags, ref hadDiag);
            }
        }

        // 'bool' has no conversion to or from the numeric and enum types: '1 + true',
        // 'true + 3' and 'E.A | false' have no applicable operator (CS0019).
        if ((l.Kind == 2) != (r.Kind == 2) && l.Kind != 6 && r.Kind != 6)
        {
            return ReportNoConstBinaryOperator(tok, emitDiags, ref diags, ref hadDiag);
        }

        // Floating-point arithmetic and relational/equality
        if ((l.Kind == 3 || l.Kind == 4 || r.Kind == 3 || r.Kind == 4) &&
            (l.Kind == 1 || l.Kind == 3 || l.Kind == 4) &&
            (r.Kind == 1 || r.Kind == 3 || r.Kind == 4) &&
            l.EnumDeclId < 0 && r.EnumDeclId < 0)
        {
            bool useF64 = l.Kind == 4 || r.Kind == 4;
            if (useF64)
            {
                double ld = l.Kind == 1 ? (IsUnsignedConstPrim(l.TypeId) ? (double)(ulong)l.IntVal : (double)l.IntVal) : l.FltVal;
                double rd = r.Kind == 1 ? (IsUnsignedConstPrim(r.TypeId) ? (double)(ulong)r.IntVal : (double)r.IntVal) : r.FltVal;
                switch (bop)
                {
                    case TokKind.Plus: return ConstEvalVal.MakeF64(ld + rd);
                    case TokKind.Minus: return ConstEvalVal.MakeF64(ld - rd);
                    case TokKind.Star: return ConstEvalVal.MakeF64(ld * rd);
                    case TokKind.Slash: return ConstEvalVal.MakeF64(ld / rd);
                    case TokKind.Percent: return ConstEvalVal.MakeF64(FModDouble(ld, rd));
                    case TokKind.LAngle: return ConstEvalVal.MakeBool(ld < rd);
                    case TokKind.LtEq: return ConstEvalVal.MakeBool(ld <= rd);
                    case TokKind.RAngle: return ConstEvalVal.MakeBool(ld > rd);
                    case TokKind.GtEq: return ConstEvalVal.MakeBool(ld >= rd);
                    case TokKind.EqEq: return ConstEvalVal.MakeBool(ld == rd);
                    case TokKind.BangEq: return ConstEvalVal.MakeBool(ld != rd);
                    default: return ConstEvalVal.Invalid();
                }
            }
            else
            {
                float lf = l.Kind == 1
                    ? BitsToFloat(IsUnsignedConstPrim(l.TypeId) ? ULongToSingleBits((ulong)l.IntVal) : LongToSingleBits(l.IntVal))
                    : (float)l.FltVal;
                float rf = r.Kind == 1
                    ? BitsToFloat(IsUnsignedConstPrim(r.TypeId) ? ULongToSingleBits((ulong)r.IntVal) : LongToSingleBits(r.IntVal))
                    : (float)r.FltVal;
                switch (bop)
                {
                    case TokKind.Plus: return ConstEvalVal.MakeF32(lf + rf);
                    case TokKind.Minus: return ConstEvalVal.MakeF32(lf - rf);
                    case TokKind.Star: return ConstEvalVal.MakeF32(lf * rf);
                    case TokKind.Slash: return ConstEvalVal.MakeF32(lf / rf);
                    case TokKind.Percent: return ConstEvalVal.MakeF32((float)FModDouble((double)lf, (double)rf));
                    case TokKind.LAngle: return ConstEvalVal.MakeBool(lf < rf);
                    case TokKind.LtEq: return ConstEvalVal.MakeBool(lf <= rf);
                    case TokKind.RAngle: return ConstEvalVal.MakeBool(lf > rf);
                    case TokKind.GtEq: return ConstEvalVal.MakeBool(lf >= rf);
                    case TokKind.EqEq: return ConstEvalVal.MakeBool(lf == rf);
                    case TokKind.BangEq: return ConstEvalVal.MakeBool(lf != rf);
                    default: return ConstEvalVal.Invalid();
                }
            }
        }

        bool lFloatZeroAsEnum = r.EnumDeclId >= 0 && (l.Kind == 3 || l.Kind == 4) && l.FltVal == 0.0;
        bool rFloatZeroAsEnum = l.EnumDeclId >= 0 && (r.Kind == 3 || r.Kind == 4) && r.FltVal == 0.0;
        if ((l.Kind != 1 && !lFloatZeroAsEnum) || (r.Kind != 1 && !rFloatZeroAsEnum))
        {
            if (l.Kind != 6 && r.Kind != 6) return ReportNoConstBinaryOperator(tok, emitDiags, ref diags, ref hadDiag);
            return ConstEvalVal.Invalid();
        }

        // Enum binary operations (C# predefined enum operators, Roslyn's binding):
        //   E op E for &, |, ^ and comparisons; E - E -> int; E + U, U + E, E - U, U - E -> E,
        //   where U is int and the operand converts to it implicitly. Numeric constant zero
        //   also converts to E (including floating zero, but not char), which makes 'e | 0',
        //   'e < 0', 'e - 0L' and '0f - e' (E - E -> int) valid.
        if (l.EnumDeclId >= 0 || r.EnumDeclId >= 0)
        {
            bool lIsEnum = l.EnumDeclId >= 0;
            bool rIsEnum = r.EnumDeclId >= 0;
            bool lZeroToEnum = lFloatZeroAsEnum || (!lIsEnum && l.TypeId != TypeIds.Char && l.IntVal == 0L);
            bool rZeroToEnum = rFloatZeroAsEnum || (!rIsEnum && r.TypeId != TypeIds.Char && r.IntVal == 0L);
            bool lToInt = l.Kind == 1 && !lIsEnum && CanImplicitlyConvertConstInt(l.TypeId, l.IntVal, TypeIds.I32);
            bool rToInt = r.Kind == 1 && !rIsEnum && CanImplicitlyConvertConstInt(r.TypeId, r.IntVal, TypeIds.I32);
            int enumId = lIsEnum ? l.EnumDeclId : r.EnumDeclId;
            bool asEnumPair = (lIsEnum && rIsEnum && l.EnumDeclId == r.EnumDeclId) ||
                (lIsEnum && rZeroToEnum) || (rIsEnum && lZeroToEnum);
            long li = lFloatZeroAsEnum ? 0L : (int)l.IntVal;
            long ri = rFloatZeroAsEnum ? 0L : (int)r.IntVal;

            if (bop == TokKind.Plus || bop == TokKind.Minus)
            {
                // E + U / U + E / E - U / U - E: Roslyn prefers E - U for 'e - 0', and
                // E - E for '0 - e' (its native-compiler-compatible operator priorities).
                bool enumResult =
                    (lIsEnum && rToInt) ||
                    (rIsEnum && lToInt && (bop == TokKind.Plus || !lZeroToEnum));
                bool intResult = !enumResult && bop == TokKind.Minus && asEnumPair;
                if (enumResult || intResult)
                {
                    long res = bop == TokKind.Plus ? (li + ri) : (li - ri);
                    if (uncheckedDepth == 0 && (res < -2147483648L || res > 2147483647L))
                    {
                        if (emitDiags) { diags.ReportTok(VibCodes.Unsupported, tok, "Compile-time constant enum arithmetic overflows in checked mode (CS0220)."u8); hadDiag = true; }
                        return ConstEvalVal.Invalid();
                    }
                    int wrapped = unchecked((int)res);
                    return enumResult ? ConstEvalVal.MakeEnum(enumId, wrapped) : ConstEvalVal.MakeInt(TypeIds.I32, wrapped);
                }
            }
            else if (asEnumPair)
            {
                switch (bop)
                {
                    case TokKind.Pipe: return ConstEvalVal.MakeEnum(enumId, li | ri);
                    case TokKind.Amp: return ConstEvalVal.MakeEnum(enumId, li & ri);
                    case TokKind.Caret: return ConstEvalVal.MakeEnum(enumId, li ^ ri);
                    case TokKind.EqEq: return ConstEvalVal.MakeBool(li == ri);
                    case TokKind.BangEq: return ConstEvalVal.MakeBool(li != ri);
                    case TokKind.LAngle: return ConstEvalVal.MakeBool(li < ri);
                    case TokKind.LtEq: return ConstEvalVal.MakeBool(li <= ri);
                    case TokKind.RAngle: return ConstEvalVal.MakeBool(li > ri);
                    case TokKind.GtEq: return ConstEvalVal.MakeBool(li >= ri);
                }
            }
            return ReportNoConstBinaryOperator(tok, emitDiags, ref diags, ref hadDiag);
        }

        // Shifts `<<`, `>>`, `>>>`
        if (bop == TokKind.LtLt || bop == TokKind.GtGt || bop == TokKind.GtGtGt)
        {
            if (ConstIntBitWidth(r.TypeId) > 32 || r.TypeId == TypeIds.U32)
            {
                if (!CanImplicitlyConvertConstInt(r.TypeId, r.IntVal, TypeIds.I32))
                {
                    return ConstEvalVal.Invalid();
                }
            }
            int sT = ConstIntBitWidth(l.TypeId) < 32 ? TypeIds.I32 : l.TypeId;
            int mask = (sT == TypeIds.I32 || sT == TypeIds.U32) ? 31 : 63;
            int sh = (int)r.IntVal & mask;
            if (bop == TokKind.LtLt)
            {
                return ConstEvalVal.MakeInt(sT, unchecked(l.IntVal << sh));
            }
            if (bop == TokKind.GtGt)
            {
                if (sT == TypeIds.U32) return ConstEvalVal.MakeInt(sT, (long)((uint)l.IntVal >> sh));
                if (sT == TypeIds.U64 || sT == TypeIds.NUInt) return ConstEvalVal.MakeInt(sT, unchecked((long)((ulong)l.IntVal >> sh)));
                return ConstEvalVal.MakeInt(sT, l.IntVal >> sh);
            }
            if (sT == TypeIds.I32 || sT == TypeIds.U32)
            {
                return ConstEvalVal.MakeInt(sT, (long)((uint)l.IntVal >> sh));
            }
            return ConstEvalVal.MakeInt(sT, unchecked((long)((ulong)l.IntVal >> sh)));
        }

        // Promoted integer binary type
        int promT = PromoteConstBinaryIntType(l.TypeId, l.IntVal, r.TypeId, r.IntVal);
        if (promT == TypeIds.Error)
        {
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Operator cannot be applied to signed and unsigned 64-bit integer operands (CS0034)."u8);
                hadDiag = true;
            }
            return ConstEvalVal.Invalid();
        }

        bool isUns = IsUnsignedConstPrim(promT);
        long lv = TruncateConstPrim(l.IntVal, promT);
        long rv = TruncateConstPrim(r.IntVal, promT);

        switch (bop)
        {
            case TokKind.LAngle: return ConstEvalVal.MakeBool(isUns ? (ulong)lv < (ulong)rv : lv < rv);
            case TokKind.LtEq: return ConstEvalVal.MakeBool(isUns ? (ulong)lv <= (ulong)rv : lv <= rv);
            case TokKind.RAngle: return ConstEvalVal.MakeBool(isUns ? (ulong)lv > (ulong)rv : lv > rv);
            case TokKind.GtEq: return ConstEvalVal.MakeBool(isUns ? (ulong)lv >= (ulong)rv : lv >= rv);
            case TokKind.EqEq: return ConstEvalVal.MakeBool(lv == rv);
            case TokKind.BangEq: return ConstEvalVal.MakeBool(lv != rv);
            case TokKind.Pipe: return ConstEvalVal.MakeInt(promT, lv | rv);
            case TokKind.Amp: return ConstEvalVal.MakeInt(promT, lv & rv);
            case TokKind.Caret: return ConstEvalVal.MakeInt(promT, lv ^ rv);
        }

        if ((bop == TokKind.Slash || bop == TokKind.Percent) && rv == 0L)
        {
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Division by constant zero (CS0020)."u8);
                hadDiag = true;
            }
            return ConstEvalVal.Invalid();
        }

        bool overflow = false;
        long result = 0L;
        if (promT == TypeIds.I32)
        {
            long ll = (int)lv;
            long rr = (int)rv;
            if (bop == TokKind.Plus) { result = ll + rr; overflow = result < -2147483648L || result > 2147483647L; }
            else if (bop == TokKind.Minus) { result = ll - rr; overflow = result < -2147483648L || result > 2147483647L; }
            else if (bop == TokKind.Star) { result = ll * rr; overflow = result < -2147483648L || result > 2147483647L; }
            else if (bop == TokKind.Slash)
            {
                if (ll == -2147483648L && rr == -1L) { result = -2147483648L; overflow = true; }
                else { result = ll / rr; }
            }
            else if (bop == TokKind.Percent)
            {
                result = (ll == -2147483648L && rr == -1L) ? 0L : (ll % rr);
            }
            else return ConstEvalVal.Invalid();
        }
        else if (promT == TypeIds.U32)
        {
            ulong lu = (uint)lv;
            ulong ru = (uint)rv;
            if (bop == TokKind.Plus) { ulong s = lu + ru; overflow = s > 4294967295UL; result = (long)(uint)s; }
            else if (bop == TokKind.Minus) { overflow = lu < ru; result = (long)(uint)unchecked(lu - ru); }
            else if (bop == TokKind.Star) { ulong p = lu * ru; overflow = p > 4294967295UL; result = (long)(uint)p; }
            else if (bop == TokKind.Slash) { result = (long)(uint)(lu / ru); }
            else if (bop == TokKind.Percent) { result = (long)(uint)(lu % ru); }
            else return ConstEvalVal.Invalid();
        }
        else if (isUns)
        {
            ulong lu = (ulong)lv;
            ulong ru = (ulong)rv;
            if (bop == TokKind.Plus) { ulong s = unchecked(lu + ru); overflow = s < lu; result = unchecked((long)s); }
            else if (bop == TokKind.Minus) { overflow = lu < ru; result = unchecked((long)(lu - ru)); }
            else if (bop == TokKind.Star) { ulong p = unchecked(lu * ru); overflow = ru != 0UL && (p / ru) != lu; result = unchecked((long)p); }
            else if (bop == TokKind.Slash) { result = unchecked((long)(lu / ru)); }
            else if (bop == TokKind.Percent) { result = unchecked((long)(lu % ru)); }
            else return ConstEvalVal.Invalid();
        }
        else
        {
            if (bop == TokKind.Plus)
            {
                result = unchecked(lv + rv);
                overflow = ((lv ^ result) & (rv ^ result)) < 0L;
            }
            else if (bop == TokKind.Minus)
            {
                result = unchecked(lv - rv);
                overflow = ((lv ^ rv) & (lv ^ result)) < 0L;
            }
            else if (bop == TokKind.Star)
            {
                result = unchecked(lv * rv);
                if (lv != 0L && rv != 0L)
                {
                    if ((lv == long.MinValue && rv == -1L) || (rv == long.MinValue && lv == -1L))
                    {
                        overflow = true;
                    }
                    else
                    {
                        overflow = (result / rv) != lv;
                    }
                }
            }
            else if (bop == TokKind.Slash)
            {
                if (lv == long.MinValue && rv == -1L) { result = long.MinValue; overflow = true; }
                else { result = lv / rv; }
            }
            else if (bop == TokKind.Percent)
            {
                result = (lv == long.MinValue && rv == -1L) ? 0L : (lv % rv);
            }
            else return ConstEvalVal.Invalid();
        }

        if (overflow && uncheckedDepth == 0)
        {
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Compile-time constant integer operation overflows in checked mode (CS0220); wrap in 'unchecked(...)' if wraparound is intended."u8);
                hadDiag = true;
            }
            return ConstEvalVal.Invalid();
        }

        return ConstEvalVal.MakeInt(promT, result);
    }

    private static ConstEvalVal ReportNoConstBinaryOperator(Token tok, bool emitDiags, ref DiagnosticBag diags, ref bool hadDiag)
    {
        if (emitDiags)
        {
            diags.ReportTok(VibCodes.Unsupported, tok, "Operator cannot be applied to operands of these types (CS0019)."u8);
            hadDiag = true;
        }
        return ConstEvalVal.Invalid();
    }

    private static int PromoteConstBinaryIntType(int lt, long lv, int rt, long rv)
    {
        if (lt == TypeIds.U64 || rt == TypeIds.U64)
        {
            if (lt == TypeIds.U64 && !IsUnsignedConstPrim(rt) && !((rt == TypeIds.I32 || rt == TypeIds.I64) && rv >= 0L)) return TypeIds.Error;
            if (rt == TypeIds.U64 && !IsUnsignedConstPrim(lt) && !((lt == TypeIds.I32 || lt == TypeIds.I64) && lv >= 0L)) return TypeIds.Error;
            return TypeIds.U64;
        }
        if (lt == TypeIds.NUInt || rt == TypeIds.NUInt)
        {
            if (lt == TypeIds.NUInt && !IsUnsignedConstPrim(rt) && !(rt == TypeIds.I32 && rv >= 0L)) return TypeIds.Error;
            if (rt == TypeIds.NUInt && !IsUnsignedConstPrim(lt) && !(lt == TypeIds.I32 && lv >= 0L)) return TypeIds.Error;
            return TypeIds.NUInt;
        }
        if (lt == TypeIds.I64 || rt == TypeIds.I64) return TypeIds.I64;
        if (lt == TypeIds.NInt || rt == TypeIds.NInt)
        {
            if (lt == TypeIds.U32 || rt == TypeIds.U32) return TypeIds.Error;
            return TypeIds.NInt;
        }
        if (lt == TypeIds.U32 || rt == TypeIds.U32)
        {
            bool otherSigned = (lt == TypeIds.U32 && !IsUnsignedConstPrim(rt)) || (rt == TypeIds.U32 && !IsUnsignedConstPrim(lt));
            if (otherSigned)
            {
                if (lt == TypeIds.U32 && rt == TypeIds.I32 && rv >= 0L) return TypeIds.U32;
                if (rt == TypeIds.U32 && lt == TypeIds.I32 && lv >= 0L) return TypeIds.U32;
                return TypeIds.I64;
            }
            return TypeIds.U32;
        }
        return TypeIds.I32;
    }

    private static bool IsNaNDouble(double d)
    {
        double copy = d;
        return d != copy;
    }

    private static bool IsNaNFloat(float f)
    {
        float copy = f;
        return f != copy;
    }

    public static double FModDouble(double x, double y)
    {
        ulong ux = (ulong)DoubleToBits(x);
        ulong uy = (ulong)DoubleToBits(y);
        ulong signX = ux & 0x8000000000000000UL;
        ulong absX = ux & 0x7FFFFFFFFFFFFFFFUL;
        ulong absY = uy & 0x7FFFFFFFFFFFFFFFUL;
        int ex = (int)(absX >> 52);
        ulong fx = absX & 0x000FFFFFFFFFFFFFUL;
        int ey = (int)(absY >> 52);
        ulong fy = absY & 0x000FFFFFFFFFFFFFUL;

        if (absY == 0UL || ex == 0x7FF || (ey == 0x7FF && fy != 0UL))
        {
            return double.NaN;
        }
        if (absX < absY)
        {
            return x;
        }
        if (absX == absY)
        {
            return BitsToDouble(unchecked((long)signX));
        }

        if (ex == 0)
        {
            while ((fx & (1UL << 52)) == 0UL)
            {
                fx <<= 1;
                ex -= 1;
            }
            ex += 1;
        }
        else
        {
            fx |= (1UL << 52);
        }

        if (ey == 0)
        {
            while ((fy & (1UL << 52)) == 0UL)
            {
                fy <<= 1;
                ey -= 1;
            }
            ey += 1;
        }
        else
        {
            fy |= (1UL << 52);
        }

        for (int step = ex - ey; step > 0; step--)
        {
            if (fx >= fy) fx -= fy;
            if (fx == 0UL) return BitsToDouble(unchecked((long)signX));
            fx <<= 1;
        }
        if (fx >= fy) fx -= fy;
        if (fx == 0UL) return BitsToDouble(unchecked((long)signX));

        int er = ey;
        while ((fx & (1UL << 52)) == 0UL)
        {
            fx <<= 1;
            er -= 1;
        }

        ulong outBits;
        if (er >= 1)
        {
            outBits = signX | ((ulong)er << 52) | (fx & 0x000FFFFFFFFFFFFFUL);
        }
        else
        {
            int shift = 1 - er;
            ulong sub = shift >= 64 ? 0UL : (fx >> shift);
            outBits = signX | (sub & 0x000FFFFFFFFFFFFFUL);
        }
        return BitsToDouble(unchecked((long)outBits));
    }

    private static double ScalePow2Double(double x, int exp)
    {
        double v = x;
        int e = exp;
        while (e >= 32) { v *= 4294967296.0; e -= 32; }
        while (e > 0) { v *= 2.0; e -= 1; }
        while (e <= -32) { v /= 4294967296.0; e += 32; }
        while (e < 0) { v *= 0.5; e += 1; }
        return v;
    }

    public static long DoubleToBits(double d)
    {
        if (IsNaNDouble(d)) return unchecked((long)0xFFF8000000000000UL);
        if (d == double.PositiveInfinity) return 0x7FF0000000000000L;
        if (d == double.NegativeInfinity) return unchecked((long)0xFFF0000000000000UL);
        if (d == 0.0) return (1.0 / d) < 0.0 ? unchecked((long)0x8000000000000000UL) : 0L;

        ulong sign = 0UL;
        double mag = d;
        if (mag < 0.0)
        {
            sign = 0x8000000000000000UL;
            mag = -mag;
        }

        double minNormal = ScalePow2Double(1.0, -1022);
        if (mag < minNormal)
        {
            double subScaled = ScalePow2Double(mag, 511);
            subScaled = ScalePow2Double(subScaled, 511 + 52);
            ulong subFrac = (ulong)subScaled;
            return unchecked((long)(sign | (subFrac & 0x000FFFFFFFFFFFFFUL)));
        }

        int exp = 0;
        while (mag >= 4294967296.0) { mag /= 4294967296.0; exp += 32; }
        while (mag >= 2.0) { mag *= 0.5; exp += 1; }
        double invPow32 = 1.0 / 4294967296.0;
        while (mag < invPow32) { mag *= 4294967296.0; exp -= 32; }
        while (mag < 1.0) { mag *= 2.0; exp -= 1; }

        ulong biased = (ulong)(exp + 1023);
        ulong frac = (ulong)((mag - 1.0) * 4503599627370496.0);
        return unchecked((long)(sign | (biased << 52) | (frac & 0x000FFFFFFFFFFFFFUL)));
    }

    public static double BitsToDouble(long rawBits)
    {
        ulong u = (ulong)rawBits;
        bool neg = (u >> 63) != 0UL;
        int biased = (int)((u >> 52) & 0x7FFUL);
        ulong frac = u & 0x000FFFFFFFFFFFFFUL;
        if (biased == 0x7FF)
        {
            if (frac != 0UL) return double.NaN;
            return neg ? double.NegativeInfinity : double.PositiveInfinity;
        }
        double val;
        if (biased == 0)
        {
            if (frac == 0UL) return neg ? -0.0 : 0.0;
            val = ScalePow2Double((double)frac / 4503599627370496.0, -1022);
        }
        else
        {
            val = ScalePow2Double(1.0 + ((double)frac / 4503599627370496.0), biased - 1023);
        }
        return neg ? -val : val;
    }

    public static long FloatToBits(float f)
    {
        if (IsNaNFloat(f)) return unchecked((int)0xFFC00000U);
        if (f == float.PositiveInfinity) return 0x7F800000L;
        if (f == float.NegativeInfinity) return unchecked((int)0xFF800000U);
        if (f == 0.0f) return (1.0f / f) < 0.0f ? unchecked((int)0x80000000U) : 0L;

        uint sign = 0U;
        double mag = (double)f;
        if (mag < 0.0)
        {
            sign = 0x80000000U;
            mag = -mag;
        }

        double minNormal = ScalePow2Double(1.0, -126);
        if (mag < minNormal)
        {
            ulong subFrac = (ulong)ScalePow2Double(mag, 126 + 23);
            uint rawSub = sign | ((uint)subFrac & 0x7FFFFFU);
            return unchecked((int)rawSub);
        }

        int exp = 0;
        while (mag >= 2.0) { mag *= 0.5; exp += 1; }
        while (mag < 1.0) { mag *= 2.0; exp -= 1; }

        uint biased = (uint)(exp + 127);
        uint frac = (uint)((mag - 1.0) * 8388608.0);
        uint raw = sign | (biased << 23) | (frac & 0x7FFFFFU);
        return unchecked((int)raw);
    }

    public static float BitsToFloat(int rawBits)
    {
        uint u = (uint)rawBits;
        bool neg = (u >> 31) != 0U;
        int biased = (int)((u >> 23) & 0xFFU);
        uint frac = u & 0x7FFFFFU;
        if (biased == 0xFF)
        {
            if (frac != 0U) return float.NaN;
            return neg ? float.NegativeInfinity : float.PositiveInfinity;
        }
        double val;
        if (biased == 0)
        {
            if (frac == 0U) return neg ? -0.0f : 0.0f;
            val = ScalePow2Double((double)frac / 8388608.0, -126);
        }
        else
        {
            val = ScalePow2Double(1.0 + ((double)frac / 8388608.0), biased - 127);
        }
        float res = (float)val;
        return neg ? -res : res;
    }

    public static int ULongToSingleBits(ulong u)
    {
        if (u == 0UL) return 0;
        int msb = 63;
        while (msb > 0 && ((u >> msb) & 1UL) == 0UL)
        {
            msb -= 1;
        }
        if (msb <= 23)
        {
            uint exactMant = (uint)(u << (23 - msb));
            uint exactBiased = (uint)(msb + 127);
            return unchecked((int)((exactBiased << 23) | (exactMant & 0x7FFFFFU)));
        }
        int shift = msb - 23;
        ulong sig = u >> shift;
        ulong rem = u & ((1UL << shift) - 1UL);
        ulong half = 1UL << (shift - 1);
        if (rem > half || (rem == half && (sig & 1UL) != 0UL))
        {
            sig += 1UL;
            if (sig == (1UL << 24))
            {
                sig >>= 1;
                msb += 1;
            }
        }
        uint biased = (uint)(msb + 127);
        return unchecked((int)((biased << 23) | ((uint)sig & 0x7FFFFFU)));
    }

    public static int LongToSingleBits(long v)
    {
        if (v == 0L) return 0;
        if (v < 0L)
        {
            ulong mag = unchecked((ulong)(-v));
            return unchecked((int)(0x80000000U | (uint)ULongToSingleBits(mag)));
        }
        return ULongToSingleBits((ulong)v);
    }

    private static void BigTrim(ref Vec<uint> limbs)
    {
        while (limbs.Count > 1 && limbs[limbs.Count - 1] == 0U)
        {
            limbs.Pop();
        }
    }

    private static void BigMulAddSmall(ref Vec<uint> limbs, uint mul, uint add)
    {
        ulong carry = add;
        for (int i = 0; i < limbs.Count; i++)
        {
            ulong prod = (ulong)limbs[i] * mul + carry;
            limbs[i] = (uint)prod;
            carry = prod >> 32;
        }
        if (carry != 0UL)
        {
            limbs.Push((uint)carry);
        }
    }

    private static void BigMulPow5(ref Vec<uint> limbs, int p)
    {
        int rem = p;
        while (rem >= 13)
        {
            BigMulAddSmall(ref limbs, 1220703125U, 0U);
            rem -= 13;
        }
        if (rem > 0)
        {
            uint factor = 1U;
            for (int k = 0; k < rem; k++) factor *= 5U;
            BigMulAddSmall(ref limbs, factor, 0U);
        }
    }

    private static int BigBitLength(in Vec<uint> limbs)
    {
        int n = limbs.Count;
        while (n > 0 && limbs[n - 1] == 0U) n -= 1;
        if (n == 0) return 0;
        uint top = limbs[n - 1];
        int hi = 31;
        while (hi > 0 && ((top >> hi) & 1U) == 0U) hi -= 1;
        return (n - 1) * 32 + hi + 1;
    }

    private static void BigShl(ref Vec<uint> limbs, int shiftBits)
    {
        if (shiftBits <= 0) return;
        int wordShift = shiftBits >> 5;
        int bitShift = shiftBits & 31;
        int oldLen = limbs.Count;
        if (bitShift != 0)
        {
            uint carry = 0U;
            for (int i = 0; i < oldLen; i++)
            {
                uint w = limbs[i];
                limbs[i] = (w << bitShift) | carry;
                carry = w >> (32 - bitShift);
            }
            if (carry != 0U)
            {
                limbs.Push(carry);
            }
        }
        if (wordShift > 0)
        {
            int curLen = limbs.Count;
            for (int k = 0; k < wordShift; k++) limbs.Push(0U);
            for (int i = curLen - 1; i >= 0; i--)
            {
                limbs[i + wordShift] = limbs[i];
            }
            for (int i = 0; i < wordShift; i++)
            {
                limbs[i] = 0U;
            }
        }
        BigTrim(ref limbs);
    }

    private static void BigShr1(ref Vec<uint> limbs)
    {
        uint carry = 0U;
        for (int i = limbs.Count - 1; i >= 0; i--)
        {
            uint w = limbs[i];
            limbs[i] = (w >> 1) | (carry << 31);
            carry = w & 1U;
        }
        BigTrim(ref limbs);
    }

    private static int BigCmp(in Vec<uint> a, in Vec<uint> b)
    {
        int na = a.Count;
        while (na > 0 && a[na - 1] == 0U) na -= 1;
        int nb = b.Count;
        while (nb > 0 && b[nb - 1] == 0U) nb -= 1;
        if (na != nb) return na < nb ? -1 : 1;
        for (int i = na - 1; i >= 0; i--)
        {
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        }
        return 0;
    }

    private static int BigCmpShifted(in Vec<uint> num, in Vec<uint> den, int denLeftShift)
    {
        if (denLeftShift == 0) return BigCmp(in num, in den);
        if (denLeftShift > 0)
        {
            var tmpDen = new Vec<uint>(den.Count + (denLeftShift >> 5) + 2);
            for (int i = 0; i < den.Count; i++) tmpDen.Push(den[i]);
            BigShl(ref tmpDen, denLeftShift);
            return BigCmp(in num, in tmpDen);
        }
        var tmpNum = new Vec<uint>(num.Count + ((-denLeftShift) >> 5) + 2);
        for (int i = 0; i < num.Count; i++) tmpNum.Push(num[i]);
        BigShl(ref tmpNum, -denLeftShift);
        return BigCmp(in tmpNum, in den);
    }

    private static void BigSubInPlace(ref Vec<uint> a, in Vec<uint> b)
    {
        long borrow = 0L;
        for (int i = 0; i < a.Count; i++)
        {
            long bv = i < b.Count ? (long)b[i] : 0L;
            long diff = (long)a[i] - bv - borrow;
            if (diff < 0L)
            {
                diff += 4294967296L;
                borrow = 1L;
            }
            else
            {
                borrow = 0L;
            }
            a[i] = (uint)diff;
        }
        BigTrim(ref a);
    }

    public static bool TryParseExactDecimalFloat(ReadOnlySpan<byte> raw, bool isSingle, out long outBits)
    {
        outBits = 0L;
        var digits = new Vec<byte>(32);
        bool seenDot = false;
        int decExp = 0;
        bool truncatedNonZero = false;
        int i = 0;

        while (i < raw.Length)
        {
            byte b = raw[i];
            if (b == (byte)'e' || b == (byte)'E')
            {
                i += 1;
                break;
            }
            if (b == (byte)'_')
            {
                i += 1;
                continue;
            }
            if (b == (byte)'.')
            {
                seenDot = true;
                i += 1;
                continue;
            }
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                byte d = (byte)(b - (byte)'0');
                if (digits.Count == 0 && d == 0)
                {
                    if (seenDot) decExp -= 1;
                }
                else if (digits.Count < 800)
                {
                    digits.Push(d);
                    if (seenDot) decExp -= 1;
                }
                else
                {
                    if (!seenDot) decExp += 1;
                    if (d != 0) truncatedNonZero = true;
                }
                i += 1;
                continue;
            }
            break;
        }

        if (i < raw.Length)
        {
            bool expNeg = false;
            if (raw[i] == (byte)'-')
            {
                expNeg = true;
                i += 1;
            }
            else if (raw[i] == (byte)'+')
            {
                i += 1;
            }
            int parsedExp = 0;
            while (i < raw.Length)
            {
                byte b = raw[i];
                if (b == (byte)'_')
                {
                    i += 1;
                    continue;
                }
                if (b >= (byte)'0' && b <= (byte)'9')
                {
                    if (parsedExp < 10000000)
                    {
                        parsedExp = parsedExp * 10 + (b - (byte)'0');
                    }
                    i += 1;
                }
                else
                {
                    break;
                }
            }
            decExp += expNeg ? -parsedExp : parsedExp;
        }

        if (digits.Count == 0)
        {
            outBits = 0L;
            return true;
        }

        if (truncatedNonZero)
        {
            digits.Push(1);
            decExp -= 1;
        }

        while (digits.Count > 1 && digits[digits.Count - 1] == 0)
        {
            digits.Pop();
            decExp += 1;
        }

        int sciExp = decExp + digits.Count - 1;
        int prec;
        int eMax;
        int eMin;
        int bias;
        if (isSingle)
        {
            if (sciExp > 38) return false;
            if (sciExp < -46) { outBits = 0L; return true; }
            prec = 24;
            eMax = 127;
            eMin = -126;
            bias = 127;
        }
        else
        {
            if (sciExp > 308) return false;
            if (sciExp < -325) { outBits = 0L; return true; }
            prec = 53;
            eMax = 1023;
            eMin = -1022;
            bias = 1023;
        }

        var num = new Vec<uint>(36);
        num.Push(0U);
        int idx = 0;
        while (idx + 9 <= digits.Count)
        {
            uint chunk = 0U;
            for (int k = 0; k < 9; k++)
            {
                chunk = chunk * 10U + digits[idx + k];
            }
            BigMulAddSmall(ref num, 1000000000U, chunk);
            idx += 9;
        }
        if (idx < digits.Count)
        {
            uint mul = 1U;
            uint chunk = 0U;
            while (idx < digits.Count)
            {
                mul *= 10U;
                chunk = chunk * 10U + digits[idx];
                idx += 1;
            }
            BigMulAddSmall(ref num, mul, chunk);
        }

        var den = new Vec<uint>(36);
        den.Push(1U);
        if (decExp > 0)
        {
            BigMulPow5(ref num, decExp);
        }
        else if (decExp < 0)
        {
            BigMulPow5(ref den, -decExp);
        }

        int binExp = decExp;
        int numBits = BigBitLength(in num);
        int denBits = BigBitLength(in den);
        int shift = numBits - denBits;
        int eGuess = shift + binExp;
        int unbExp = BigCmpShifted(in num, in den, shift) < 0 ? (eGuess - 1) : eGuess;

        if (unbExp > eMax)
        {
            return false;
        }

        int q = unbExp >= eMin ? (unbExp - (prec - 1)) : (eMin - (prec - 1));
        int s = binExp - q;
        if (s > 0)
        {
            BigShl(ref num, s);
        }
        else if (s < 0)
        {
            BigShl(ref den, -s);
        }

        var denScaled = new Vec<uint>(den.Count + 4);
        for (int k = 0; k < den.Count; k++) denScaled.Push(den[k]);
        BigShl(ref denScaled, prec - 1);

        ulong quot = 0UL;
        for (int bit = prec - 1; bit >= 0; bit--)
        {
            if (BigCmp(in num, in denScaled) >= 0)
            {
                BigSubInPlace(ref num, in denScaled);
                quot |= (1UL << bit);
            }
            if (bit > 0)
            {
                BigShr1(ref denScaled);
            }
        }

        BigShl(ref num, 1);
        int remCmp = BigCmp(in num, in den);
        if (remCmp > 0 || (remCmp == 0 && (quot & 1UL) != 0UL))
        {
            quot += 1UL;
        }

        int biasedExp;
        ulong frac;
        if (unbExp < eMin)
        {
            if (quot == (1UL << (prec - 1)))
            {
                biasedExp = 1;
                frac = 0UL;
            }
            else
            {
                biasedExp = 0;
                frac = quot;
            }
        }
        else
        {
            if (quot == (1UL << prec))
            {
                quot >>= 1;
                unbExp += 1;
            }
            if (unbExp > eMax)
            {
                return false;
            }
            biasedExp = unbExp + bias;
            frac = quot & ((1UL << (prec - 1)) - 1UL);
        }

        if (isSingle)
        {
            uint raw32 = ((uint)biasedExp << 23) | (uint)frac;
            outBits = unchecked((int)raw32);
        }
        else
        {
            ulong raw64 = ((ulong)biasedExp << 52) | frac;
            outBits = unchecked((long)raw64);
        }
        return true;
    }

    public static void AppendConstStringBytes(in TokenArena ta, in Vec<int> strChunks, long code, ref StringBuf dst)
    {
        if (code == -1L) return;
        if (code >= 0L)
        {
            dst.Append(ta.GetLit((int)code));
            return;
        }
        int off = (int)(-code - 2L);
        if (off < 0 || off >= strChunks.Count) return;
        int cnt = strChunks[off];
        for (int i = 0; i < cnt; i++)
        {
            int sliceIdx = strChunks[off + 1 + i];
            if (sliceIdx >= 0)
            {
                dst.Append(ta.GetLit(sliceIdx));
            }
        }
    }

    public static void CollectConstStringChunks(long code, in Vec<int> strChunks, in TokenArena ta, ref Vec<int> outSlices)
    {
        if (code == -1L) return;
        if (code >= 0L)
        {
            if (ta.GetLit((int)code).Length > 0)
            {
                outSlices.Push((int)code);
            }
            return;
        }
        int off = (int)(-code - 2L);
        if (off < 0 || off >= strChunks.Count) return;
        int cnt = strChunks[off];
        for (int i = 0; i < cnt; i++)
        {
            int sliceIdx = strChunks[off + 1 + i];
            if (sliceIdx >= 0 && ta.GetLit(sliceIdx).Length > 0)
            {
                outSlices.Push(sliceIdx);
            }
        }
    }

    public static long ConcatConstStrings(in TokenArena ta, ref Vec<int> strChunks, long leftCode, long rightCode)
    {
        var slices = new Vec<int>(4);
        CollectConstStringChunks(leftCode, in strChunks, in ta, ref slices);
        CollectConstStringChunks(rightCode, in strChunks, in ta, ref slices);
        if (slices.Count == 0) return -1L;
        if (slices.Count == 1) return slices[0];

        var combined = new StringBuf(64);
        for (int i = 0; i < slices.Count; i++)
        {
            combined.Append(ta.GetLit(slices[i]));
        }
        ReadOnlySpan<byte> full = combined.AsUtf8();
        for (int s = 0; s < ta.LitSlices.Count; s++)
        {
            if (Bytes.Eq(ta.GetLit(s), full))
            {
                return s;
            }
        }

        int off = strChunks.Count;
        strChunks.Push(slices.Count);
        for (int i = 0; i < slices.Count; i++)
        {
            strChunks.Push(slices[i]);
        }
        return -(off + 2L);
    }

    public static bool ConstStringsEqual(in TokenArena ta, in Vec<int> strChunks, long leftCode, long rightCode)
    {
        if (leftCode == rightCode) return true;
        var a = new StringBuf(32);
        var b = new StringBuf(32);
        AppendConstStringBytes(in ta, in strChunks, leftCode, ref a);
        AppendConstStringBytes(in ta, in strChunks, rightCode, ref b);
        return Bytes.Eq(a.AsUtf8(), b.AsUtf8());
    }

    private static bool CanImplicitNumericWidenPrim(int src, int dst)
    {
        if (src == dst) return true;
        switch (src)
        {
            case TypeIds.I8:
                return dst == TypeIds.I16 || dst == TypeIds.I32 || dst == TypeIds.I64 || dst == TypeIds.NInt || dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.U8:
                return dst == TypeIds.I16 || dst == TypeIds.U16 || dst == TypeIds.I32 || dst == TypeIds.U32 || dst == TypeIds.I64 || dst == TypeIds.U64 || dst == TypeIds.NInt || dst == TypeIds.NUInt || dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.I16:
                return dst == TypeIds.I32 || dst == TypeIds.I64 || dst == TypeIds.NInt || dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.U16:
            case TypeIds.Char:
                return dst == TypeIds.U16 || dst == TypeIds.I32 || dst == TypeIds.U32 || dst == TypeIds.I64 || dst == TypeIds.U64 || dst == TypeIds.NInt || dst == TypeIds.NUInt || dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.I32:
                return dst == TypeIds.I64 || dst == TypeIds.NInt || dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.U32:
                return dst == TypeIds.I64 || dst == TypeIds.U64 || dst == TypeIds.NUInt || dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.I64:
            case TypeIds.U64:
            case TypeIds.NInt:
            case TypeIds.NUInt:
                return dst == TypeIds.F32 || dst == TypeIds.F64;
            case TypeIds.F32:
                return dst == TypeIds.F64;
            default:
                return false;
        }
    }

    private static bool TryMergeConstCondArms(
        ConstEvalVal a,
        ConstEvalVal b,
        bool takeFirst,
        Token tok,
        bool emitDiags,
        ref DiagnosticBag diags,
        ref bool hadDiag,
        out ConstEvalVal merged)
    {
        merged = ConstEvalVal.Invalid();
        ConstEvalVal sel = takeFirst ? a : b;

        if (a.Kind == 6 && b.Kind == 6)
        {
            merged = ConstEvalVal.MakeDefaultLiteral();
            return true;
        }
        if (a.Kind == 6 || b.Kind == 6)
        {
            ConstEvalVal other = a.Kind == 6 ? b : a;
            if (other.Kind == 5)
            {
                if (emitDiags)
                {
                    diags.ReportTok(VibCodes.NullOrZeroUnsafe, tok, "'default' initialization of 'string' is banned in Vibranium."u8);
                    hadDiag = true;
                }
                return false;
            }
            if ((a.Kind == 6 && takeFirst) || (b.Kind == 6 && !takeFirst))
            {
                if (other.Kind == 2) { merged = ConstEvalVal.MakeBool(false); return true; }
                if (other.Kind == 3) { merged = ConstEvalVal.MakeF32(0.0f); return true; }
                if (other.Kind == 4) { merged = ConstEvalVal.MakeF64(0.0); return true; }
                if (other.Kind == 1 && other.EnumDeclId >= 0) { merged = ConstEvalVal.MakeEnum(other.EnumDeclId, 0L); return true; }
                if (other.Kind == 1) { merged = ConstEvalVal.MakeInt(other.TypeId, 0L); return true; }
            }
            else
            {
                merged = other;
                return true;
            }
        }

        if (a.Kind == 2 && b.Kind == 2) { merged = sel; return true; }
        if (a.Kind == 5 && b.Kind == 5) { merged = sel; return true; }
        if (a.Kind == 3 && b.Kind == 3) { merged = sel; return true; }
        if (a.Kind == 4 && b.Kind == 4) { merged = sel; return true; }

        if ((a.Kind == 1 && a.EnumDeclId >= 0) || (b.Kind == 1 && b.EnumDeclId >= 0))
        {
            if (a.Kind == 1 && b.Kind == 1 && a.EnumDeclId == b.EnumDeclId && a.EnumDeclId >= 0)
            {
                merged = sel;
                return true;
            }
            if (a.Kind == 1 && a.EnumDeclId >= 0 && IsZeroNumericConstVal(b))
            {
                merged = ConstEvalVal.MakeEnum(a.EnumDeclId, takeFirst ? a.IntVal : 0L);
                return true;
            }
            if (b.Kind == 1 && b.EnumDeclId >= 0 && IsZeroNumericConstVal(a))
            {
                merged = ConstEvalVal.MakeEnum(b.EnumDeclId, takeFirst ? 0L : b.IntVal);
                return true;
            }
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Type of conditional expression cannot be determined between incompatible arms (CS0173)."u8);
                hadDiag = true;
            }
            return false;
        }

        bool aNum = a.Kind == 1 || a.Kind == 3 || a.Kind == 4;
        bool bNum = b.Kind == 1 || b.Kind == 3 || b.Kind == 4;
        if (aNum && bNum)
        {
            if (a.Kind == 4 || b.Kind == 4)
            {
                if (sel.Kind == 4) { merged = sel; return true; }
                if (sel.Kind == 3) { merged = ConstEvalVal.MakeF64(sel.FltVal); return true; }
                double dv = IsUnsignedConstPrim(sel.TypeId) ? (double)(ulong)sel.IntVal : (double)sel.IntVal;
                merged = ConstEvalVal.MakeF64(dv);
                return true;
            }
            if (a.Kind == 3 || b.Kind == 3)
            {
                if (sel.Kind == 3) { merged = sel; return true; }
                int fb = IsUnsignedConstPrim(sel.TypeId) ? ULongToSingleBits((ulong)sel.IntVal) : LongToSingleBits(sel.IntVal);
                merged = ConstEvalVal.MakeF32Bits(fb);
                return true;
            }
            int commonT = TypeIds.Error;
            if (a.TypeId == b.TypeId) commonT = a.TypeId;
            else if (CanImplicitNumericWidenPrim(a.TypeId, b.TypeId) && !CanImplicitNumericWidenPrim(b.TypeId, a.TypeId)) commonT = b.TypeId;
            else if (CanImplicitNumericWidenPrim(b.TypeId, a.TypeId) && !CanImplicitNumericWidenPrim(a.TypeId, b.TypeId)) commonT = a.TypeId;
            else if (CanImplicitlyConvertConstInt(b.TypeId, b.IntVal, a.TypeId) && !CanImplicitlyConvertConstInt(a.TypeId, a.IntVal, b.TypeId)) commonT = a.TypeId;
            else if (CanImplicitlyConvertConstInt(a.TypeId, a.IntVal, b.TypeId) && !CanImplicitlyConvertConstInt(b.TypeId, b.IntVal, a.TypeId)) commonT = b.TypeId;
            else if (CanImplicitlyConvertConstInt(b.TypeId, b.IntVal, a.TypeId)) commonT = a.TypeId;

            if (commonT != TypeIds.Error)
            {
                merged = ConstEvalVal.MakeInt(commonT, sel.IntVal);
                return true;
            }
        }

        if (emitDiags)
        {
            diags.ReportTok(VibCodes.Unsupported, tok, "Type of conditional expression cannot be determined between incompatible arms (CS0173)."u8);
            hadDiag = true;
        }
        return false;
    }

    private static bool IsZeroNumericConstVal(ConstEvalVal v) =>
        (v.Kind == 1 && v.EnumDeclId < 0 && v.TypeId != TypeIds.Char && v.IntVal == 0L) ||
        ((v.Kind == 3 || v.Kind == 4) && v.FltVal == 0.0);

    public static void DrainConcreteTypeQueue(
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        while (bctx.NextPendingTypeCursor < types.ConcreteUserTypes.Count)
        {
            int typeId = types.ConcreteUserTypes[bctx.NextPendingTypeCursor];
            bctx.NextPendingTypeCursor += 1;

            TypeInfo ti = types.Items[typeId];
            TypeDecl td = ast.Types[ti.A];
            PopulateTypeFieldsOnly(typeId, in ast, in ta, in names, ref bctx, ref types, ref diags);
            TypeInfo updated = types.Items[typeId];
            if (updated.DisposeFuncInstId >= 0) continue;

            // If disposable class, bind its validated public void Dispose() method instance
            if (td.Kind == TypeDeclKinds.Class && (td.Flags & DeclFlags.IsDisposable) != 0 && !types.ContainsTypeParam(typeId))
            {
                var dispTpNames = new Vec<int>(td.TypeParamCount);
                var dispTpVals = new Vec<int>(td.TypeParamCount);
                for (int k = 0; k < td.TypeParamCount; k++)
                {
                    dispTpNames.Push(ast.Extra[td.TypeParamStart + k]);
                    dispTpVals.Push(types.TypeArgs[ti.B + k]);
                }
                for (int m = 0; m < td.FuncCount; m++)
                {
                    int fId = td.FuncStart + m;
                    FuncDecl mfd = ast.Funcs[fId];
                    if (IsValidDisposableDisposeMethod(in mfd, dispTpNames.AsReadOnlySpan(), dispTpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types))
                    {
                        var emptyMArgs = new Vec<int>();
                        updated.DisposeFuncInstId = EnsureFuncInstance(fId, typeId, emptyMArgs.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
                        break;
                    }
                }
            }

            types.Items[typeId] = updated;
        }
    }

    public static int ResolveTypeNode(int nodeId, ReadOnlySpan<int> tpNames, ReadOnlySpan<int> tpVals, in AstArena ast, in TokenArena ta, in Interner names, ref BinderCtx bctx, ref TypeTable types, ref DiagnosticBag diags) => ResolveTypeNode(nodeId, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref diags, true);

    public static int ResolveTypeNode(
        int nodeId,
        ReadOnlySpan<int> tpNames,
        ReadOnlySpan<int> tpVals,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags,
        bool reportLinearErrors)
    {
        if (nodeId < 0) return TypeIds.Void;
        AstNode n = ast.Nodes[nodeId];
        Token tok = ta.Tokens[n.TokIdx];

        switch (n.Kind)
        {
            case AstKind.TypePrim:
            {
                TokKind pk = (TokKind)n.A;
                if (pk == TokKind.KwObject)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "'object' type is banned in Vibranium."u8);
                    return TypeIds.Error;
                }
                return MapPrimTokKind(pk);
            }

            case AstKind.TypeNullable:
            {
                int inner = ResolveTypeNode(n.A, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors);
                return types.MakeOption(inner);
            }

            case AstKind.TypeArray:
            {
                int elem = ResolveTypeNode(n.A, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false);
                if (reportLinearErrors)
                {
                    if (types.IsLinear(elem, in ast))
                    {
                        diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot be used as an array element type."u8);
                    }
                    else
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "Managed array types 'T[]' are not supported in Vibranium; use Vec<T>, Span<T>, or ReadOnlySpan<T>."u8);
                    }
                }
                return TypeIds.I32;
            }

            case AstKind.TypeNamed:
            {
                int nameId = n.A;
                int argCount = n.C;
                TypeRefContext trc = n.D >= 0 && n.D < ast.TypeRefs.Count
                    ? ast.TypeRefs[n.D]
                    : new TypeRefContext(-1, UnitKinds.App, -1, -1, 0, 0);

                // Check type parameter substitution first
                if (argCount == 0 && trc.PrefixNsId < 0)
                {
                    for (int i = 0; i < tpNames.Length; i++)
                    {
                        if (tpNames[i] == nameId)
                        {
                            return tpVals[i];
                        }
                    }
                }

                // Resolve generic type arguments
                var resolvedArgs = new Vec<int>(argCount);
                for (int i = 0; i < argCount; i++)
                {
                    int argNode = ast.Extra[n.B + i];
                    int argTid = ResolveTypeNode(argNode, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors);
                    if (reportLinearErrors && types.IsView(argTid))
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "Borrowed view / ref struct cannot be used as a generic type argument in Vibranium."u8);
                    }
                    resolvedArgs.Push(argTid);
                }

                int declId = -1;

                if (trc.PrefixNsId >= 0)
                {
                    ReadOnlySpan<byte> rawPrefTxt = names.Text(trc.PrefixNsId);
                    if (trc.PrefixNsId == bctx.IdGlobalPrefix)
                    {
                        if (trc.UnitKind == UnitKinds.App)
                        {
                            Option<int> gOpt = bctx.AppGlobalTypeByNameArity.Get(PackNameArity(nameId, argCount));
                            if (gOpt.IsSome) declId = gOpt.Unwrap();
                        }
                    }
                    else if (trc.PrefixNsId == bctx.IdGlobalSystem)
                    {
                        declId = FindDeclaredTypeInExactNamespace(nameId, argCount, bctx.IdSystem, trc.UnitKind, in ast, in bctx);
                        if (declId < 0 && TryResolveSystemBuiltinType(nameId, resolvedArgs.AsReadOnlySpan(), tok, reportLinearErrors, in ast, in names, in bctx, ref types, ref diags, out int sysTid))
                        {
                            return sysTid;
                        }
                    }
                    else if (trc.PrefixNsId == bctx.IdGlobalVibranium)
                    {
                        declId = FindDeclaredTypeInExactNamespace(nameId, argCount, bctx.IdVibranium, trc.UnitKind, in ast, in bctx);
                        if (declId < 0 && TryResolveVibraniumCoreType(nameId, resolvedArgs.AsReadOnlySpan(), tok, n.TokIdx, trc.EnclosingOwnerDeclId, reportLinearErrors, in ast, in ta, in names, in bctx, ref types, ref diags, out int vibTid))
                        {
                            return vibTid;
                        }
                    }
                    else if (trc.PrefixNsId == bctx.IdGlobalVibraniumStd)
                    {
                        declId = FindDeclaredTypeInExactNamespace(nameId, argCount, bctx.IdVibraniumStd, trc.UnitKind, in ast, in bctx);
                    }
                    else if (rawPrefTxt.StartsWith("global::"u8))
                    {
                        ReadOnlySpan<byte> stripped = rawPrefTxt.Slice(8);
                        declId = LookupGlobalQualifiedTypeByBytes(nameId, argCount, stripped, trc.UnitKind, in ast, in names, in bctx);
                    }
                    else if (TryResolvePrefixViaAlias(trc.PrefixNsId, nameId, argCount, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out bool prefIsNs, out int prefResolvedNs, out int aliasNestedDecl, out bool prefAmb))
                    {
                        if (prefAmb)
                        {
                            diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference between imported namespaces (CS0104)."u8);
                            return TypeIds.Error;
                        }
                        if (prefIsNs)
                        {
                            if (prefResolvedNs >= 0)
                            {
                                declId = FindDeclaredTypeInExactNamespace(nameId, argCount, prefResolvedNs, trc.UnitKind, in ast, in bctx);
                                if (declId < 0 && prefResolvedNs == bctx.IdSystem &&
                                    TryResolveSystemBuiltinType(nameId, resolvedArgs.AsReadOnlySpan(), tok, reportLinearErrors, in ast, in names, in bctx, ref types, ref diags, out int sysTid))
                                {
                                    return sysTid;
                                }
                                if (declId < 0 && prefResolvedNs == bctx.IdVibranium &&
                                    TryResolveVibraniumCoreType(nameId, resolvedArgs.AsReadOnlySpan(), tok, n.TokIdx, trc.EnclosingOwnerDeclId, reportLinearErrors, in ast, in ta, in names, in bctx, ref types, ref diags, out int vibTid))
                                {
                                    return vibTid;
                                }
                            }
                        }
                        else
                        {
                            declId = aliasNestedDecl;
                        }
                    }
                    else
                    {
                        if (trc.UnitKind == UnitKinds.App && !rawPrefTxt.Contains((byte)'.'))
                        {
                            int outerRk = ResolveAppUnqualifiedName(trc.PrefixNsId, 0, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out _, out _, out _, out _);
                            if (outerRk == -2 && !IsDeclaredNamespaceId(trc.PrefixNsId, in ast, in bctx) && !HasRelativeNamespacePrefix(trc.PrefixNsId, trc.CurrentNsId, in ast, in names))
                            {
                                diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference between imported namespaces (CS0104)."u8);
                                return TypeIds.Error;
                            }
                        }
                        declId = LookupQualifiedTypeDecl(nameId, argCount, trc.PrefixNsId, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx);
                        if (declId < 0)
                        {
                            if (trc.PrefixNsId == bctx.IdSystem &&
                                TryResolveSystemBuiltinType(nameId, resolvedArgs.AsReadOnlySpan(), tok, reportLinearErrors, in ast, in names, in bctx, ref types, ref diags, out int sysTid))
                            {
                                return sysTid;
                            }
                            if (trc.PrefixNsId == bctx.IdVibranium &&
                                TryResolveVibraniumCoreType(nameId, resolvedArgs.AsReadOnlySpan(), tok, n.TokIdx, trc.EnclosingOwnerDeclId, reportLinearErrors, in ast, in ta, in names, in bctx, ref types, ref diags, out int vibTid))
                            {
                                return vibTid;
                            }
                        }
                    }
                }
                else if (trc.UnitKind == UnitKinds.App)
                {
                    int resKind = ResolveAppUnqualifiedName(
                        nameId,
                        argCount,
                        trc.CurrentNsId,
                        trc.EnclosingOwnerDeclId,
                        trc.UsingExtraStart,
                        trc.UsingCount,
                        in ast,
                        in names,
                        in bctx,
                        out int foundDecl,
                        out int foundAliasIdx,
                        out bool matchSys,
                        out bool matchVibCore);
                    if (resKind == -2)
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference between imported namespaces (CS0104)."u8);
                        return TypeIds.Error;
                    }
                    if (resKind == 1)
                    {
                        declId = foundDecl;
                    }
                    else if (resKind == 2 && foundAliasIdx >= 0 && foundAliasIdx < bctx.UsingBoundKind.Count)
                    {
                        int bk = bctx.UsingBoundKind[foundAliasIdx];
                        if (bk == 1)
                        {
                            diags.ReportTok(VibCodes.Unsupported, tok, "Namespace alias cannot be used as a type."u8);
                            return TypeIds.Error;
                        }
                        if (bk == 4)
                        {
                            diags.ReportTok(VibCodes.Unsupported, tok, "Framework attribute aliases are only valid in attribute positions."u8);
                            return TypeIds.Error;
                        }
                        if (bk == 2)
                        {
                            int aliasedTid = bctx.UsingBoundTypeId[foundAliasIdx];
                            if (aliasedTid >= 0)
                            {
                                PopulateTypeFieldsOnly(aliasedTid, in ast, in ta, in names, ref bctx, ref types, ref diags);
                                return aliasedTid;
                            }
                            declId = bctx.UsingBoundId[foundAliasIdx];
                        }
                        else if (bk == 3)
                        {
                            int bStatic = bctx.UsingBoundId[foundAliasIdx];
                            if (bStatic == BuiltinStaticIds.IDisposable)
                            {
                                diags.ReportTok(VibCodes.Unsupported, tok, "Interface type 'IDisposable' cannot be used as a variable, parameter, return, or type argument in Vibranium."u8);
                                return TypeIds.I32;
                            }
                            if (bStatic < 0)
                            {
                                diags.ReportTok(VibCodes.Unsupported, tok, "Static helper class cannot be used as a variable, parameter, return, or type argument in Vibranium."u8);
                                return TypeIds.Error;
                            }
                            int bt = bctx.UsingBoundTypeId[foundAliasIdx];
                            if (bt != TypeIds.Error)
                            {
                                return bt;
                            }
                            return TypeIds.Error;
                        }
                    }
                    else if (resKind == 3)
                    {
                        if (matchSys &&
                            TryResolveSystemBuiltinType(nameId, resolvedArgs.AsReadOnlySpan(), tok, reportLinearErrors, in ast, in names, in bctx, ref types, ref diags, out int sysTid))
                        {
                            return sysTid;
                        }
                        if (matchVibCore &&
                            TryResolveVibraniumCoreType(nameId, resolvedArgs.AsReadOnlySpan(), tok, n.TokIdx, trc.EnclosingOwnerDeclId, reportLinearErrors, in ast, in ta, in names, in bctx, ref types, ref diags, out int vibTid))
                        {
                            return vibTid;
                        }
                        declId = foundDecl;
                    }
                }
                else
                {
                    // In Core / Std units, resolve within Std + Core/System built-ins ONLY (never polluted by App global types!).
                    declId = FindNestedTypeInEnclosingChain(nameId, argCount, trc.EnclosingOwnerDeclId, in ast);
                    if (declId < 0)
                    {
                        Option<int> stdOpt = bctx.StdTypeByNameArity.Get(PackNameArity(nameId, argCount));
                        if (stdOpt.IsSome) declId = stdOpt.Unwrap();
                    }
                    if (declId < 0)
                    {
                        if (TryResolveSystemBuiltinType(nameId, resolvedArgs.AsReadOnlySpan(), tok, reportLinearErrors, in ast, in names, in bctx, ref types, ref diags, out int sysTid))
                        {
                            return sysTid;
                        }
                        if (TryResolveVibraniumCoreType(nameId, resolvedArgs.AsReadOnlySpan(), tok, n.TokIdx, trc.EnclosingOwnerDeclId, reportLinearErrors, in ast, in ta, in names, in bctx, ref types, ref diags, out int vibTid))
                        {
                            return vibTid;
                        }
                    }
                }

                if (declId < 0)
                {
                    // Span<T>.Enumerator from GetEnumerator()
                    if (Bytes.Eq(names.Text(nameId), "Enumerator"u8))
                    {
                        return TypeIds.I32;
                    }
                    diags.ReportTok(VibCodes.Unsupported, tok, "Unknown type name."u8);
                    return TypeIds.Error;
                }

                TypeDecl td = ast.Types[declId];
                if (!bctx.CompilingStdItself && trc.UnitKind == UnitKinds.App && td.UnitKind == UnitKinds.Std && (td.Flags & DeclFlags.Public) == 0)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Internal standard library type is inaccessible from user code."u8);
                    return TypeIds.Error;
                }
                if ((td.Flags & DeclFlags.Static) != 0)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Static types cannot be used as variables, parameters, return types, or type arguments."u8);
                    return TypeIds.Error;
                }
                if (td.Kind == TypeDeclKinds.Enum)
                {
                    return types.MakeEnum(declId, names.Text(td.NameId));
                }

                if (reportLinearErrors)
                {
                    for (int i = 0; i < resolvedArgs.Count; i++)
                    {
                        if (types.IsLinear(resolvedArgs[i], in ast))
                        {
                            diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot instantiate user generic type."u8);
                            break;
                        }
                    }
                }

                ValidateTypeArguments(td.TypeParamCount, td.TypeParamFlagsStart, resolvedArgs.AsReadOnlySpan(), tok, in ast, in types, ref diags);
                int tKind = td.Kind == TypeDeclKinds.Class ? TypeKinds.Class : TypeKinds.Struct;
                int uTid = types.MakeUserType(tKind, declId, names.Text(td.NameId), resolvedArgs.AsReadOnlySpan());
                if (types.GetNestingDepth(uTid) > MaxGenericDepth)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Polymorphic recursion or excessive generic type nesting is banned in Vibranium."u8);
                    return TypeIds.Error;
                }
                PopulateTypeFieldsOnly(uTid, in ast, in ta, in names, ref bctx, ref types, ref diags);
                return uTid;
            }

            default:
                return TypeIds.Error;
        }
    }

    public static void BindAndValidateUsings(
        in AstArena ast,
        in TokenArena ta,
        ref Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        var emptyTp = new Vec<int>();
        for (int uIdx = 0; uIdx < ast.Usings.Count; uIdx++)
        {
            UsingEntry ue = ast.Usings[uIdx];
            bctx.UsingBoundKind.Push(0);
            bctx.UsingBoundId.Push(-1);
            bctx.UsingBoundTypeId.Push(TypeIds.Error);

            Token uTok = ta.Tokens[ue.TokIdx];
            if (ue.TargetNode < 0) continue;
            AstNode tn = ast.Nodes[ue.TargetNode];

            if (ue.Kind == UsingKinds.Namespace)
            {
                if (tn.Kind != AstKind.TypeNamed || tn.C != 0)
                {
                    diags.ReportTok(VibCodes.Unsupported, uTok, "A 'using' namespace directive can only be applied to namespaces."u8);
                    continue;
                }
                if (TryResolveUsingTargetAsNamespace(ue.TargetNode, ref names, in ast, in bctx, out int resolvedNsId, out bool isInternalStdSys))
                {
                    if (isInternalStdSys && !bctx.CompilingStdItself && ue.UnitKind == UnitKinds.App)
                    {
                        diags.ReportTok(VibCodes.Unsupported, uTok, "'Vibranium.Std.Sys' is an internal type, not a public namespace."u8);
                        continue;
                    }
                    bctx.UsingBoundKind[uIdx] = 1;
                    bctx.UsingBoundId[uIdx] = resolvedNsId;
                }
                else
                {
                    diags.ReportTok(VibCodes.Unsupported, uTok, "A 'using' directive can only be applied to an existing namespace; use 'using static' for types."u8);
                }
                continue;
            }

            if (ue.Kind == UsingKinds.Static)
            {
                if (tn.Kind != AstKind.TypeNamed)
                {
                    diags.ReportTok(VibCodes.Unsupported, uTok, "'using static' requires a named class, struct, or enum type."u8);
                    continue;
                }
                if (TryResolveStaticOrAliasSpecialTarget(ue.TargetNode, isUsingStatic: true, in ast, in ta, in names, ref bctx, ref types, ref diags, out int bKind, out int bId, out int bTid))
                {
                    bctx.UsingBoundKind[uIdx] = bKind;
                    bctx.UsingBoundId[uIdx] = bId;
                    bctx.UsingBoundTypeId[uIdx] = bTid;
                }
                continue;
            }

            if (ue.Kind == UsingKinds.Alias)
            {
                // First check if target is a namespace (`using Ns = MyApp.Sub;` or `using Sys = System;`)
                if (tn.Kind == AstKind.TypeNamed && tn.C == 0 &&
                    TryResolveUsingTargetAsNamespace(ue.TargetNode, ref names, in ast, in bctx, out int aliasNsId, out bool isIntSys))
                {
                    if (isIntSys && !bctx.CompilingStdItself && ue.UnitKind == UnitKinds.App)
                    {
                        diags.ReportTok(VibCodes.Unsupported, uTok, "Internal type 'Vibranium.Std.Sys' is inaccessible from user code."u8);
                        continue;
                    }
                    bctx.UsingBoundKind[uIdx] = 1;
                    bctx.UsingBoundId[uIdx] = aliasNsId;
                    continue;
                }

                // A known but denied framework attribute cannot enter the using
                // table, even when no attribute application ever refers to it.
                bool isAttribute = Parser.TryResolveFrameworkAttributeAlias(ue.TargetNode, in ast, in names, out int attributeIdentity, out bool attributePermitted);
                if (isAttribute && !attributePermitted)
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[tn.TokIdx], "A 'using' alias cannot target an attribute that is not permitted in this source unit."u8);
                    continue;
                }

                // Next check static/interface/user/builtin type target
                if (tn.Kind == AstKind.TypeNamed &&
                    TryResolveStaticOrAliasSpecialTarget(ue.TargetNode, isUsingStatic: false, in ast, in ta, in names, ref bctx, ref types, ref diags, out int aKind, out int aId, out int aTid))
                {
                    bctx.UsingBoundKind[uIdx] = aKind;
                    bctx.UsingBoundId[uIdx] = aId;
                    bctx.UsingBoundTypeId[uIdx] = aTid;
                    continue;
                }

                // Permitted attribute aliases are metadata-only, after ordinary
                // source types, and never acquire a runtime TypeId here.
                if (isAttribute)
                {
                    bctx.UsingBoundKind[uIdx] = 4;
                    bctx.UsingBoundId[uIdx] = attributeIdentity;
                    continue;
                }

                // Otherwise resolve as an ordinary type expression (primitives `using MyInt = int;`, `ReadOnlySpan<byte>`, etc.)
                int resolvedTid = ResolveTypeNode(ue.TargetNode, emptyTp.AsReadOnlySpan(), emptyTp.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: true);
                if (resolvedTid != TypeIds.Error)
                {
                    TypeInfo ti = types.Items[resolvedTid];
                    if (ti.Kind == TypeKinds.Class || ti.Kind == TypeKinds.Struct || ti.Kind == TypeKinds.Enum)
                    {
                        bctx.UsingBoundKind[uIdx] = 2;
                        bctx.UsingBoundId[uIdx] = ti.A;
                        bctx.UsingBoundTypeId[uIdx] = resolvedTid;
                    }
                    else
                    {
                        bctx.UsingBoundKind[uIdx] = 3;
                        bctx.UsingBoundId[uIdx] = 0;
                        bctx.UsingBoundTypeId[uIdx] = resolvedTid;
                    }
                }
            }
        }
    }

    private static bool TryResolveUsingTargetAsNamespace(
        int targetNodeId,
        ref Interner names,
        in AstArena ast,
        in BinderCtx bctx,
        out int outNsId,
        out bool outIsInternalStdSys)
    {
        outNsId = -1;
        outIsInternalStdSys = false;
        AstNode tn = ast.Nodes[targetNodeId];
        if (tn.Kind != AstKind.TypeNamed || tn.C != 0) return false;
        TypeRefContext trc = tn.D >= 0 && tn.D < ast.TypeRefs.Count
            ? ast.TypeRefs[tn.D]
            : new TypeRefContext(-1, UnitKinds.App, -1, -1, 0, 0);

        var tailCopy = new StringBuf(names.Text(tn.A).Length);
        tailCopy.Append(names.Text(tn.A));
        ReadOnlySpan<byte> tailTxt = tailCopy.AsUtf8();
        if (trc.PrefixNsId >= 0)
        {
            var prefCopy = new StringBuf(names.Text(trc.PrefixNsId).Length);
            prefCopy.Append(names.Text(trc.PrefixNsId));
            ReadOnlySpan<byte> prefTxt = prefCopy.AsUtf8();
            bool hadGlobal = false;
            if (Bytes.Eq(prefTxt, "global::"u8))
            {
                if (IsKnownNamespaceBytes(tailTxt, in ast, in names, in bctx))
                {
                    outNsId = tn.A;
                    return true;
                }
                return false;
            }
            if (prefTxt.StartsWith("global::"u8))
            {
                prefTxt = prefTxt.Slice(8);
                hadGlobal = true;
            }
            if (!hadGlobal && trc.UnitKind == UnitKinds.App)
            {
                // Check if leftmost segment of `prefTxt` is an outer-scope namespace alias
                int dotIdx = prefTxt.IndexOf((byte)'.');
                ReadOnlySpan<byte> firstSeg = dotIdx < 0 ? prefTxt : prefTxt.Slice(0, dotIdx);
                ReadOnlySpan<byte> midRest = dotIdx < 0 ? default : prefTxt.Slice(dotIdx + 1);
                if (TryFindOuterNamespaceAlias(firstSeg, trc.CurrentNsId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out int aliasedPrefNs))
                {
                    ReadOnlySpan<byte> baseTxt = names.Text(aliasedPrefNs);
                    var sbAlias = new StringBuf(baseTxt.Length + midRest.Length + tailTxt.Length + 2);
                    sbAlias.Append(baseTxt);
                    if (midRest.Length > 0)
                    {
                        sbAlias.AppendAscii((byte)'.');
                        sbAlias.Append(midRest);
                    }
                    sbAlias.AppendAscii((byte)'.');
                    sbAlias.Append(tailTxt);
                    if (Bytes.Eq(sbAlias.AsUtf8(), "Vibranium.Std.Sys"u8))
                    {
                        outIsInternalStdSys = true;
                        return true;
                    }
                    if (IsKnownNamespaceBytes(sbAlias.AsUtf8(), in ast, in names, in bctx))
                    {
                        outNsId = names.Intern(sbAlias.AsUtf8());
                        return true;
                    }
                    return false;
                }
            }

            var fullBuf = new StringBuf(prefTxt.Length + 1 + tailTxt.Length);
            fullBuf.Append(prefTxt);
            fullBuf.AppendAscii((byte)'.');
            fullBuf.Append(tailTxt);
            if (Bytes.Eq(fullBuf.AsUtf8(), "Vibranium.Std.Sys"u8))
            {
                outIsInternalStdSys = true;
                return true;
            }
            if (!hadGlobal && trc.CurrentNsId >= 0)
            {
                if (TryResolveRelativeNamespaceBytes(fullBuf.AsUtf8(), trc.CurrentNsId, ref names, in ast, in bctx, out outNsId))
                {
                    return true;
                }
            }
            if (IsKnownNamespaceBytes(fullBuf.AsUtf8(), in ast, in names, in bctx))
            {
                outNsId = names.Intern(fullBuf.AsUtf8());
                return true;
            }
            return false;
        }

        // Unqualified target (`using Ns;` or `using X = Ns;`)
        if (trc.UnitKind == UnitKinds.App)
        {
            // Check outer-scope alias first if no relative namespace matches
            if (trc.CurrentNsId >= 0 && TryResolveRelativeNamespaceBytes(tailTxt, trc.CurrentNsId, ref names, in ast, in bctx, out outNsId))
            {
                // Make sure a type in `trc.CurrentNsId` doesn't shadow it
                if (FindDeclaredTypeInExactNamespace(tn.A, 0, trc.CurrentNsId, UnitKinds.App, in ast, in bctx) < 0)
                {
                    return true;
                }
            }
            if (TryFindOuterNamespaceAlias(tailTxt, trc.CurrentNsId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out int aliasedNs))
            {
                outNsId = aliasedNs;
                return true;
            }
        }
        if (IsKnownNamespaceBytes(tailTxt, in ast, in names, in bctx))
        {
            // If an App global type has the exact same name AND no namespace with that name was declared, `IsKnownNamespaceBytes` is false unless it's a real namespace
            outNsId = tn.A;
            return true;
        }
        return false;
    }

    private static bool TryFindOuterNamespaceAlias(
        ReadOnlySpan<byte> segBytes,
        int currentNsId,
        int usingStart,
        int usingCount,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        out int outAliasedNsId)
    {
        outAliasedNsId = -1;
        int step = 0;
        while (TryStepEnclosingNamespace(currentNsId, ref step, in ast, in names, out int nsCursor))
        {
            for (int scopePass = 0; scopePass < 2; scopePass++)
            {
                if (nsCursor >= 0 && scopePass == 1) break;
                for (int u = 0; u < usingCount; u++)
                {
                    int uIdx = ast.Extra[usingStart + u];
                    if (uIdx < 0 || uIdx >= bctx.UsingBoundKind.Count) continue;
                    UsingEntry ue = ast.Usings[uIdx];
                    if (ue.EnclosingNsId != nsCursor || ue.Kind != UsingKinds.Alias) continue;
                    if (nsCursor < 0 && ((scopePass == 0 && ue.ScopeLevel != 1) || (scopePass == 1 && ue.ScopeLevel != 0))) continue;
                    if (Bytes.Eq(names.Text(ue.AliasOrNsId), segBytes))
                    {
                        if (bctx.UsingBoundKind[uIdx] == 1)
                        {
                            outAliasedNsId = bctx.UsingBoundId[uIdx];
                            return true;
                        }
                        return false;
                    }
                }
            }
        }
        return false;
    }

    private static bool TryResolveRelativeNamespaceBytes(
        ReadOnlySpan<byte> relBytes,
        int currentNsId,
        ref Interner names,
        in AstArena ast,
        in BinderCtx bctx,
        out int outNsId)
    {
        outNsId = -1;
        if (currentNsId < 0) return false;
        ReadOnlySpan<byte> fullCur = names.Text(currentNsId);
        int curLen = fullCur.Length;
        while (curLen > 0)
        {
            ReadOnlySpan<byte> pref = fullCur.Slice(0, curLen);
            var sb = new StringBuf(pref.Length + 1 + relBytes.Length);
            sb.Append(pref);
            sb.AppendAscii((byte)'.');
            sb.Append(relBytes);
            if (IsKnownNamespaceBytes(sb.AsUtf8(), in ast, in names, in bctx))
            {
                outNsId = names.Intern(sb.AsUtf8());
                return true;
            }
            int dot = -1;
            for (int k = curLen - 1; k >= 0; k--)
            {
                if (fullCur[k] == (byte)'.') { dot = k; break; }
            }
            curLen = dot;
        }
        return false;
    }

    private static bool IsKnownNamespaceBytes(
        ReadOnlySpan<byte> nsBytes,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx)
    {
        for (int i = 0; i < ast.DeclaredNamespaces.Count; i++)
        {
            if (Bytes.Eq(names.Text(ast.DeclaredNamespaces[i]), nsBytes))
            {
                return true;
            }
        }
        if (Bytes.Eq(nsBytes, "System"u8) ||
            Bytes.Eq(nsBytes, "System.Diagnostics"u8) ||
            Bytes.Eq(nsBytes, "System.Diagnostics.CodeAnalysis"u8) ||
            Bytes.Eq(nsBytes, "System.Runtime"u8) ||
            Bytes.Eq(nsBytes, "System.Runtime.CompilerServices"u8) ||
            Bytes.Eq(nsBytes, "System.Runtime.InteropServices"u8) ||
            Bytes.Eq(nsBytes, "System.Buffers"u8) ||
            Bytes.Eq(nsBytes, "System.Collections"u8) ||
            Bytes.Eq(nsBytes, "System.Collections.Generic"u8) ||
            Bytes.Eq(nsBytes, "System.Text"u8) ||
            Bytes.Eq(nsBytes, "System.IO"u8) ||
            Bytes.Eq(nsBytes, "System.Numerics"u8) ||
            Bytes.Eq(nsBytes, "Vibranium"u8) ||
            Bytes.Eq(nsBytes, "Vibranium.Bootstrap"u8))
        {
            return true;
        }
        if ((bctx.HasStdUnit || bctx.CompilingStdItself) && Bytes.Eq(nsBytes, "Vibranium.Std"u8))
        {
            return true;
        }
        return false;
    }

    private static bool IsDeclaredNamespaceId(int nsId, in AstArena ast, in BinderCtx bctx)
    {
        if (nsId == bctx.IdSystem || nsId == bctx.IdVibranium || (bctx.HasStdUnit && nsId == bctx.IdVibraniumStd))
        {
            return true;
        }
        for (int i = 0; i < ast.DeclaredNamespaces.Count; i++)
        {
            if (ast.DeclaredNamespaces[i] == nsId) return true;
        }
        return false;
    }

    private static bool HasRelativeNamespacePrefix(int prefixNsId, int currentNsId, in AstArena ast, in Interner names)
    {
        if (currentNsId < 0) return false;
        ReadOnlySpan<byte> fullCur = names.Text(currentNsId);
        ReadOnlySpan<byte> rel = names.Text(prefixNsId);
        int curLen = fullCur.Length;
        while (curLen > 0)
        {
            ReadOnlySpan<byte> pref = fullCur.Slice(0, curLen);
            var sb = new StringBuf(pref.Length + 1 + rel.Length);
            sb.Append(pref);
            sb.AppendAscii((byte)'.');
            sb.Append(rel);
            for (int i = 0; i < ast.DeclaredNamespaces.Count; i++)
            {
                if (Bytes.Eq(names.Text(ast.DeclaredNamespaces[i]), sb.AsUtf8()))
                {
                    return true;
                }
            }
            int dot = -1;
            for (int k = curLen - 1; k >= 0; k--)
            {
                if (fullCur[k] == (byte)'.') { dot = k; break; }
            }
            curLen = dot;
        }
        return false;
    }

    private static int MapSpecialStaticName(int nameId, bool inSystem, bool inVibranium, in Interner names, in BinderCtx bctx)
    {
        if (inSystem)
        {
            if (nameId == bctx.IdIDisposable) return BuiltinStaticIds.IDisposable;
            if (nameId == bctx.IdConsole) return BuiltinStaticIds.Console;
            if (nameId == bctx.IdMemoryExtensions) return BuiltinStaticIds.MemoryExtensions;
        }
        if (inVibranium)
        {
            if (nameId == bctx.IdPrelude) return BuiltinStaticIds.Prelude;
            if (nameId == bctx.IdIo) return BuiltinStaticIds.Io;
            if (nameId == bctx.IdStdio) return BuiltinStaticIds.Stdio;
            if (nameId == bctx.IdVibraniumRuntime) return BuiltinStaticIds.VibraniumRuntime;
            if (nameId == bctx.IdOptionExtensions) return BuiltinStaticIds.OptionExtensions;
            if (nameId == bctx.IdStringUtf8Extensions) return BuiltinStaticIds.StringUtf8Extensions;
            if (nameId == bctx.IdUtf8SpanExtensions) return BuiltinStaticIds.Utf8SpanExtensions;
        }
        return 0;
    }

    private static bool TryResolveStaticOrAliasSpecialTarget(
        int targetNodeId,
        bool isUsingStatic,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags,
        out int outKind,
        out int outId,
        out int outTypeId)
    {
        outKind = 0;
        outId = -1;
        outTypeId = TypeIds.Error;

        AstNode tn = ast.Nodes[targetNodeId];
        Token tok = ta.Tokens[tn.TokIdx];
        int nameId = tn.A;
        int argCount = tn.C;
        TypeRefContext trc = tn.D >= 0 && tn.D < ast.TypeRefs.Count
            ? ast.TypeRefs[tn.D]
            : new TypeRefContext(-1, UnitKinds.App, -1, -1, 0, 0);

        if (isUsingStatic && argCount == 0)
        {
            var dummyNames = new Interner(16);
            // Reject `using static MyNamespace;` cleanly if target is purely a namespace and not a type
            if (trc.PrefixNsId < 0 && IsDeclaredNamespaceId(nameId, in ast, in bctx) &&
                LookupUnqualifiedTypeDecl(nameId, 0, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx) < 0)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "'using static' cannot be applied to a namespace."u8);
                return false;
            }
            _ = dummyNames.Count;
        }

        var emptyTp = new Vec<int>();
        var resolvedArgs = new Vec<int>(argCount);
        for (int i = 0; i < argCount; i++)
        {
            int argTid = ResolveTypeNode(ast.Extra[tn.B + i], emptyTp.AsReadOnlySpan(), emptyTp.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: true);
            if (types.IsView(argTid))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Borrowed view / ref struct cannot be used as a generic type argument in Vibranium."u8);
            }
            resolvedArgs.Push(argTid);
        }

        int declId = -1;
        bool checkSysSpecial = false;
        bool checkVibSpecial = false;

        if (trc.PrefixNsId >= 0)
        {
            ReadOnlySpan<byte> rawPrefTxt = names.Text(trc.PrefixNsId);
            if (trc.PrefixNsId == bctx.IdGlobalPrefix)
            {
                if (trc.UnitKind == UnitKinds.App)
                {
                    Option<int> gOpt = bctx.AppGlobalTypeByNameArity.Get(PackNameArity(nameId, argCount));
                    if (gOpt.IsSome) declId = gOpt.Unwrap();
                }
            }
            else if (trc.PrefixNsId == bctx.IdGlobalSystem || trc.PrefixNsId == bctx.IdSystem)
            {
                declId = FindDeclaredTypeInExactNamespace(nameId, argCount, bctx.IdSystem, trc.UnitKind, in ast, in bctx);
                if (declId < 0) checkSysSpecial = true;
            }
            else if (trc.PrefixNsId == bctx.IdGlobalVibranium || trc.PrefixNsId == bctx.IdVibranium)
            {
                declId = FindDeclaredTypeInExactNamespace(nameId, argCount, bctx.IdVibranium, trc.UnitKind, in ast, in bctx);
                if (declId < 0) checkVibSpecial = true;
            }
            else if (trc.PrefixNsId == bctx.IdGlobalVibraniumStd)
            {
                declId = FindDeclaredTypeInExactNamespace(nameId, argCount, bctx.IdVibraniumStd, trc.UnitKind, in ast, in bctx);
            }
            else if (rawPrefTxt.StartsWith("global::"u8))
            {
                declId = LookupGlobalQualifiedTypeByBytes(nameId, argCount, rawPrefTxt.Slice(8), trc.UnitKind, in ast, in names, in bctx);
            }
            else if (TryResolvePrefixViaAlias(trc.PrefixNsId, nameId, argCount, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out bool pIsNs, out int pNs, out int aNested, out bool pAmb))
            {
                if (pAmb)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference between imported namespaces (CS0104)."u8);
                    return false;
                }
                if (pIsNs)
                {
                    declId = FindDeclaredTypeInExactNamespace(nameId, argCount, pNs, trc.UnitKind, in ast, in bctx);
                    if (declId < 0 && pNs == bctx.IdSystem) checkSysSpecial = true;
                    if (declId < 0 && pNs == bctx.IdVibranium) checkVibSpecial = true;
                }
                else
                {
                    declId = aNested;
                }
            }
            else
            {
                declId = LookupQualifiedTypeDecl(nameId, argCount, trc.PrefixNsId, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx);
            }
        }
        else if (trc.UnitKind == UnitKinds.App)
        {
            int rk = ResolveAppUnqualifiedName(nameId, argCount, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out int fDecl, out int fAliasIdx, out bool mSys, out bool mVib);
            if (rk == -2)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous type reference between imported namespaces (CS0104)."u8);
                return false;
            }
            if (rk == 1)
            {
                declId = fDecl;
            }
            else if (rk == 2 && fAliasIdx >= 0 && fAliasIdx < bctx.UsingBoundKind.Count)
            {
                int bk = bctx.UsingBoundKind[fAliasIdx];
                if (bk == 1)
                {
                    if (isUsingStatic)
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "'using static' cannot be applied to a namespace."u8);
                    }
                    return false;
                }
                if (bk == 4 && isUsingStatic)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Framework attribute aliases cannot be used with 'using static'."u8);
                    return false;
                }
                if (bk == 3 && isUsingStatic && bctx.UsingBoundId[fAliasIdx] == BuiltinStaticIds.IDisposable)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Interface type 'IDisposable' cannot be used with 'using static'."u8);
                    return false;
                }
                outKind = bk;
                outId = bctx.UsingBoundId[fAliasIdx];
                outTypeId = bctx.UsingBoundTypeId[fAliasIdx];
                return bk != 0;
            }
            else if (rk == 3)
            {
                if (mSys) checkSysSpecial = true;
                else if (mVib) checkVibSpecial = true;
                else declId = fDecl;
            }
        }
        else
        {
            declId = LookupUnqualifiedTypeDecl(nameId, argCount, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx);
            if (declId < 0)
            {
                checkSysSpecial = CanMatchSystemBuiltinType(nameId, argCount, in names, in bctx);
                checkVibSpecial = CanMatchVibraniumCoreBuiltinType(nameId, argCount, in names, in bctx);
            }
        }

        if (declId < 0 && (checkSysSpecial || checkVibSpecial))
        {
            if (argCount == 0)
            {
                int sp = MapSpecialStaticName(nameId, checkSysSpecial, checkVibSpecial, in names, in bctx);
                if (sp < 0)
                {
                    if (isUsingStatic && sp == BuiltinStaticIds.IDisposable)
                    {
                        diags.ReportTok(VibCodes.Unsupported, tok, "Interface type 'IDisposable' cannot be used with 'using static'."u8);
                        return false;
                    }
                    outKind = 3;
                    outId = sp;
                    outTypeId = TypeIds.Error;
                    return true;
                }
                if (checkSysSpecial && TryMapSystemPrimTypeName(names.Text(nameId), out int sysPrimTid))
                {
                    outKind = 3;
                    outId = 0;
                    outTypeId = sysPrimTid;
                    return true;
                }
            }
            if (!isUsingStatic)
            {
                return false;
            }
            if (checkSysSpecial && TryResolveSystemBuiltinType(nameId, resolvedArgs.AsReadOnlySpan(), tok, reportLinearErrors: true, in ast, in names, in bctx, ref types, ref diags, out int sTid))
            {
                if (types.IsView(sTid))
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "'using static' cannot be applied to a ref struct view."u8);
                    return false;
                }
                outKind = 3;
                outId = 0;
                outTypeId = sTid;
                return true;
            }
            if (checkVibSpecial && TryResolveVibraniumCoreType(nameId, resolvedArgs.AsReadOnlySpan(), tok, tn.TokIdx, trc.EnclosingOwnerDeclId, reportLinearErrors: true, in ast, in ta, in names, in bctx, ref types, ref diags, out int vTid))
            {
                if (types.IsView(vTid))
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "'using static' cannot be applied to a ref struct view."u8);
                    return false;
                }
                outKind = 3;
                outId = 0;
                outTypeId = vTid;
                return true;
            }
        }

        if (declId < 0)
        {
            if (isUsingStatic)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "'using static' target does not resolve to an accessible type."u8);
            }
            return false;
        }

        TypeDecl td = ast.Types[declId];
        // The Std.Sys catalog has public .NET metadata but is internal to Vibranium.
        bool isStdSys = td.OwnerDeclId < 0 && td.NsId == bctx.IdVibraniumStd && Bytes.Eq(names.Text(td.NameId), "Sys"u8);
        if (!bctx.CompilingStdItself && trc.UnitKind == UnitKinds.App && td.UnitKind == UnitKinds.Std &&
            ((td.Flags & DeclFlags.Public) == 0 || isStdSys))
        {
            diags.ReportTok(VibCodes.Unsupported, tok, "Internal standard library type is inaccessible from user code."u8);
            return false;
        }
        if (td.OwnerDeclId >= 0 && (td.Flags & DeclFlags.Private) != 0 && !IsInsideDecl(trc.EnclosingOwnerDeclId, td.OwnerDeclId, in ast))
        {
            diags.ReportTok(VibCodes.Unsupported, tok, "Private nested type is inaccessible from 'using' directive."u8);
            return false;
        }

        int boundTypeId = -1;
        if (td.Kind == TypeDeclKinds.Enum)
        {
            boundTypeId = types.MakeEnum(declId, names.Text(td.NameId));
        }
        else if ((td.Flags & DeclFlags.Static) == 0 || td.TypeParamCount > 0)
        {
            for (int i = 0; i < resolvedArgs.Count; i++)
            {
                if (types.IsLinear(resolvedArgs[i], in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot instantiate user generic type."u8);
                    break;
                }
            }
            ValidateTypeArguments(td.TypeParamCount, td.TypeParamFlagsStart, resolvedArgs.AsReadOnlySpan(), tok, in ast, in types, ref diags);
            int tk = td.Kind == TypeDeclKinds.Class ? TypeKinds.Class : TypeKinds.Struct;
            boundTypeId = types.MakeUserType(tk, declId, names.Text(td.NameId), resolvedArgs.AsReadOnlySpan());
        }

        outKind = 2;
        outId = declId;
        outTypeId = boundTypeId;
        return true;
    }

    public static void BindTypeBaseLists(
        ref AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        _ = types.Items.Count;
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if (td.BaseTypeCount == 0) continue;

            for (int b = 0; b < td.BaseTypeCount; b++)
            {
                int baseNodeId = ast.Extra[td.BaseTypeExtraStart + b];
                AstNode bn = ast.Nodes[baseNodeId];
                Token baseTok = ta.Tokens[bn.TokIdx];

                bool isIdisp = false;
                bool isAmbiguous = false;
                if (bn.Kind == AstKind.TypeNamed && bn.C == 0)
                {
                    int bNameId = bn.A;
                    TypeRefContext trc = bn.D >= 0 && bn.D < ast.TypeRefs.Count
                        ? ast.TypeRefs[bn.D]
                        : new TypeRefContext(-1, td.UnitKind, td.NsId, td.OwnerDeclId, td.UsingExtraStart, td.UsingCount);

                    if (trc.PrefixNsId >= 0)
                    {
                        if ((trc.PrefixNsId == bctx.IdGlobalSystem || trc.PrefixNsId == bctx.IdSystem) && bNameId == bctx.IdIDisposable)
                        {
                            if (trc.PrefixNsId == bctx.IdGlobalSystem ||
                                FindDeclaredTypeInExactNamespace(bNameId, 0, bctx.IdSystem, trc.UnitKind, in ast, in bctx) < 0)
                            {
                                isIdisp = true;
                            }
                        }
                        else if (TryResolvePrefixViaAlias(trc.PrefixNsId, bNameId, 0, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out bool pIsNs, out int pNs, out _, out bool pAmb))
                        {
                            if (pAmb) isAmbiguous = true;
                            else if (pIsNs && pNs == bctx.IdSystem && bNameId == bctx.IdIDisposable &&
                                     FindDeclaredTypeInExactNamespace(bNameId, 0, bctx.IdSystem, trc.UnitKind, in ast, in bctx) < 0)
                            {
                                isIdisp = true;
                            }
                        }
                    }
                    else if (trc.UnitKind == UnitKinds.App)
                    {
                        int rk = ResolveAppUnqualifiedName(bNameId, 0, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx, out _, out int fAliasIdx, out bool mSys, out _);
                        if (rk == -2)
                        {
                            isAmbiguous = true;
                        }
                        else if (rk == 2 && fAliasIdx >= 0 && fAliasIdx < bctx.UsingBoundKind.Count)
                        {
                            if (bctx.UsingBoundKind[fAliasIdx] == 3 && bctx.UsingBoundId[fAliasIdx] == BuiltinStaticIds.IDisposable)
                            {
                                isIdisp = true;
                            }
                        }
                        else if (rk == 3 && mSys && bNameId == bctx.IdIDisposable)
                        {
                            isIdisp = true;
                        }
                    }
                    else
                    {
                        if (bNameId == bctx.IdIDisposable &&
                            LookupUnqualifiedTypeDecl(bNameId, 0, trc.UnitKind, trc.CurrentNsId, trc.EnclosingOwnerDeclId, trc.UsingExtraStart, trc.UsingCount, in ast, in names, in bctx) < 0)
                        {
                            isIdisp = true;
                        }
                    }
                }

                if (isAmbiguous)
                {
                    diags.ReportTok(VibCodes.Unsupported, baseTok, "Ambiguous type reference 'IDisposable' between imported namespaces (CS0104)."u8);
                    continue;
                }

                if (isIdisp)
                {
                    if (td.Kind == TypeDeclKinds.Struct)
                    {
                        diags.ReportTok(VibCodes.Unsupported, baseTok, "IDisposable linear resources must be classes in Vibranium."u8);
                    }
                    else if ((td.Flags & DeclFlags.Static) != 0)
                    {
                        diags.ReportTok(VibCodes.Unsupported, baseTok, "Static classes cannot implement interfaces."u8);
                    }
                    else
                    {
                        td.Flags |= DeclFlags.IsDisposable;
                        ast.Types[i] = td;
                    }
                }
                else
                {
                    diags.ReportTok(VibCodes.Unsupported, baseTok, "Class inheritance and non-IDisposable interfaces are banned in Vibranium."u8);
                }
            }
        }
    }

    public static bool IsInsideDecl(int innerDeclId, int outerDeclId, in AstArena ast)
    {
        for (int d = innerDeclId; d >= 0; d = ast.Types[d].OwnerDeclId)
        {
            if (d == outerDeclId) return true;
        }
        return false;
    }

    public static bool TryStepEnclosingNamespace(
        int currentNsId,
        ref int stepState,
        in AstArena ast,
        in Interner names,
        out int outNsCursor)
    {
        // `stepState`:
        //  0        => initial call
        //  > 0      => remaining prefix length before last '.' + 1 (or 1 when ready to yield -1)
        //  -1       => finished
        if (stepState < 0)
        {
            outNsCursor = -1;
            return false;
        }
        if (currentNsId < 0)
        {
            stepState = -1;
            outNsCursor = -1;
            return true;
        }

        ReadOnlySpan<byte> fullNs = names.Text(currentNsId);
        int curLen;
        if (stepState == 0)
        {
            curLen = fullNs.Length;
        }
        else if (stepState == 1)
        {
            stepState = -1;
            outNsCursor = -1;
            return true;
        }
        else
        {
            curLen = stepState - 2;
        }

        while (curLen > 0)
        {
            int nextDot = -1;
            for (int k = curLen - 1; k >= 0; k--)
            {
                if (fullNs[k] == (byte)'.')
                {
                    nextDot = k;
                    break;
                }
            }
            stepState = nextDot >= 0 ? (nextDot + 2) : 1;

            if (curLen == fullNs.Length)
            {
                outNsCursor = currentNsId;
                return true;
            }

            ReadOnlySpan<byte> slice = fullNs.Slice(0, curLen);
            int matchedNs = FindInternedDeclaredNamespace(slice, in ast, in names);
            if (matchedNs >= 0)
            {
                outNsCursor = matchedNs;
                return true;
            }
            if (stepState == 1)
            {
                stepState = -1;
                outNsCursor = -1;
                return true;
            }
            curLen = stepState - 2;
        }

        stepState = -1;
        outNsCursor = -1;
        return true;
    }

    public static ReadOnlySpan<byte> BuiltinStaticName(int builtinStaticId)
    {
        switch (builtinStaticId)
        {
            case BuiltinStaticIds.IDisposable: return "IDisposable"u8;
            case BuiltinStaticIds.Prelude: return "Prelude"u8;
            case BuiltinStaticIds.Io: return "Io"u8;
            case BuiltinStaticIds.Stdio: return "Stdio"u8;
            case BuiltinStaticIds.VibraniumRuntime: return "VibraniumRuntime"u8;
            case BuiltinStaticIds.Console: return "Console"u8;
            case BuiltinStaticIds.MemoryExtensions: return "MemoryExtensions"u8;
            case BuiltinStaticIds.OptionExtensions: return "OptionExtensions"u8;
            case BuiltinStaticIds.StringUtf8Extensions: return "StringUtf8Extensions"u8;
            case BuiltinStaticIds.Utf8SpanExtensions: return "Utf8SpanExtensions"u8;
            case BuiltinStaticIds.Array: return "Array"u8;
            default: return default;
        }
    }

    public static ReadOnlySpan<byte> PrimTypeIdBuiltinName(int primTypeId)
    {
        switch (primTypeId)
        {
            case TypeIds.Bool: return "bool"u8;
            case TypeIds.I8: return "sbyte"u8;
            case TypeIds.U8: return "byte"u8;
            case TypeIds.I16: return "short"u8;
            case TypeIds.U16: return "ushort"u8;
            case TypeIds.Char: return "char"u8;
            case TypeIds.I32: return "int"u8;
            case TypeIds.U32: return "uint"u8;
            case TypeIds.I64: return "long"u8;
            case TypeIds.U64: return "ulong"u8;
            case TypeIds.NInt: return "nint"u8;
            case TypeIds.NUInt: return "nuint"u8;
            case TypeIds.F32: return "float"u8;
            case TypeIds.F64: return "double"u8;
            case TypeIds.Str: return "string"u8;
            default: return default;
        }
    }

    public static int FindInternedTextReadOnly(ReadOnlySpan<byte> text, in Interner names)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (Bytes.Eq(names.Text(i), text)) return i;
        }
        return -1;
    }

    private static int FindInternedDeclaredNamespace(ReadOnlySpan<byte> nsBytes, in AstArena ast, in Interner names)
    {
        for (int i = 0; i < ast.DeclaredNamespaces.Count; i++)
        {
            int id = ast.DeclaredNamespaces[i];
            if (Bytes.Eq(names.Text(id), nsBytes))
            {
                return id;
            }
        }
        return -1;
    }

    public static int ResolveAppUnqualifiedName(
        int nameId,
        int arity,
        int currentNsId,
        int enclosingOwnerDeclId,
        int usingStart,
        int usingCount,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        out int outDeclId,
        out int outAliasUsingIdx,
        out bool outMatchSys,
        out bool outMatchVibCore)
    {
        outDeclId = -1;
        outAliasUsingIdx = -1;
        outMatchSys = false;
        outMatchVibCore = false;

        int nested = FindNestedTypeInEnclosingChain(nameId, arity, enclosingOwnerDeclId, in ast);
        if (nested >= 0)
        {
            outDeclId = nested;
            return 1;
        }

        var seenDecls = new Vec<int>(4);
        var seenNs = new Vec<int>(8);
        int step = 0;
        while (TryStepEnclosingNamespace(currentNsId, ref step, in ast, in names, out int nsCursor))
        {
            // (a) Types declared directly in `nsCursor`
            if (nsCursor >= 0)
            {
                for (int i = 0; i < ast.Types.Count; i++)
                {
                    TypeDecl td = ast.Types[i];
                    if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId == nsCursor &&
                        td.NameId == nameId && td.TypeParamCount == arity)
                    {
                        outDeclId = i;
                        return 1;
                    }
                }
            }
            else
            {
                Option<int> globOpt = bctx.AppGlobalTypeByNameArity.Get(PackNameArity(nameId, arity));
                if (globOpt.IsSome)
                {
                    outDeclId = globOpt.Unwrap();
                    return 1;
                }
            }

            // (b) `using Alias = ...;` in `nsCursor` (when arity == 0; compilation-unit alias shadows global using alias)
            if (arity == 0)
            {
                for (int scopePass = 0; scopePass < 2; scopePass++)
                {
                    if (nsCursor >= 0 && scopePass == 1) break;
                    for (int u = 0; u < usingCount; u++)
                    {
                        int uIdx = ast.Extra[usingStart + u];
                        if (uIdx < 0 || uIdx >= ast.Usings.Count) continue;
                        UsingEntry ue = ast.Usings[uIdx];
                        if (ue.EnclosingNsId != nsCursor || ue.Kind != UsingKinds.Alias) continue;
                        if (nsCursor < 0 && ((scopePass == 0 && ue.ScopeLevel != 1) || (scopePass == 1 && ue.ScopeLevel != 0))) continue;
                        if (ue.AliasOrNsId == nameId)
                        {
                            outAliasUsingIdx = uIdx;
                            return 2;
                        }
                    }
                }
            }

            // (c) `using Namespace;` + `using static OuterType;` nested types at `nsCursor` (+ implicit globals at `nsCursor == -1`)
            seenDecls.Clear();
            seenNs.Clear();
            bool matchSys = false;
            bool matchVibCore = false;
            int singleDecl = -1;
            int count = 0;
            int internalStdFallback = -1;

            for (int u = 0; u < usingCount; u++)
            {
                int uIdx = ast.Extra[usingStart + u];
                if (uIdx < 0 || uIdx >= ast.Usings.Count) continue;
                UsingEntry ue = ast.Usings[uIdx];
                if (ue.EnclosingNsId != nsCursor) continue;

                if (ue.Kind == UsingKinds.Namespace)
                {
                    int uNs = uIdx < bctx.UsingBoundKind.Count && bctx.UsingBoundKind[uIdx] == 1
                        ? bctx.UsingBoundId[uIdx]
                        : ue.AliasOrNsId;
                    if (uNs >= 0)
                    {
                        ImportNamespaceCandidatesAtTier(uNs, nameId, arity, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref matchSys, ref matchVibCore, ref count, ref singleDecl, ref internalStdFallback);
                    }
                }
                else if (ue.Kind == UsingKinds.Static && uIdx < bctx.UsingBoundKind.Count && bctx.UsingBoundKind[uIdx] == 2)
                {
                    int staticDeclId = bctx.UsingBoundId[uIdx];
                    for (int i = 0; i < ast.Types.Count; i++)
                    {
                        TypeDecl td = ast.Types[i];
                        if (td.OwnerDeclId == staticDeclId && td.NameId == nameId && td.TypeParamCount == arity)
                        {
                            if ((td.Flags & DeclFlags.Private) == 0 || IsInsideDecl(enclosingOwnerDeclId, staticDeclId, in ast))
                            {
                                AddUniqueDeclCandidate(i, ref seenDecls, ref count, ref singleDecl);
                            }
                        }
                    }
                }
            }

            if (nsCursor < 0)
            {
                ImportNamespaceCandidatesAtTier(bctx.IdSystem, nameId, arity, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref matchSys, ref matchVibCore, ref count, ref singleDecl, ref internalStdFallback);
                ImportNamespaceCandidatesAtTier(bctx.IdVibranium, nameId, arity, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref matchSys, ref matchVibCore, ref count, ref singleDecl, ref internalStdFallback);
                if (bctx.HasStdUnit)
                {
                    ImportNamespaceCandidatesAtTier(bctx.IdVibraniumStd, nameId, arity, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref matchSys, ref matchVibCore, ref count, ref singleDecl, ref internalStdFallback);
                }
            }

            if (count == 0 && internalStdFallback >= 0)
            {
                outDeclId = internalStdFallback;
                return 3;
            }
            if (count > 1)
            {
                return -2;
            }
            if (count == 1)
            {
                outMatchSys = matchSys;
                outMatchVibCore = matchVibCore;
                outDeclId = singleDecl;
                return 3;
            }
        }

        return 0;
    }

    private static void ImportNamespaceCandidatesAtTier(
        int uNs,
        int nameId,
        int arity,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        ref Vec<int> seenNs,
        ref Vec<int> seenDecls,
        ref bool matchSys,
        ref bool matchVibCore,
        ref int count,
        ref int singleDeclId,
        ref int internalStdFallback)
    {
        for (int i = 0; i < seenNs.Count; i++)
        {
            if (seenNs[i] == uNs) return;
        }
        seenNs.Push(uNs);

        if (uNs == bctx.IdSystem && !matchSys && CanMatchSystemBuiltinType(nameId, arity, in names, in bctx))
        {
            matchSys = true;
            count += 1;
        }
        if (uNs == bctx.IdVibranium && !matchVibCore && CanMatchVibraniumCoreBuiltinType(nameId, arity, in names, in bctx))
        {
            matchVibCore = true;
            count += 1;
        }

        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId == uNs &&
                td.NameId == nameId && td.TypeParamCount == arity)
            {
                AddUniqueDeclCandidate(i, ref seenDecls, ref count, ref singleDeclId);
            }
        }

        if (bctx.HasStdUnit && uNs == bctx.IdVibraniumStd)
        {
            Option<int> stdOpt = bctx.StdTypeByNameArity.Get(PackNameArity(nameId, arity));
            if (stdOpt.IsSome)
            {
                int stdDecl = stdOpt.Unwrap();
                if ((ast.Types[stdDecl].Flags & DeclFlags.Public) != 0)
                {
                    AddUniqueDeclCandidate(stdDecl, ref seenDecls, ref count, ref singleDeclId);
                }
                else if (internalStdFallback < 0)
                {
                    internalStdFallback = stdDecl;
                }
            }
        }
    }

    public static bool TryResolvePrefixViaAlias(
        int prefixNsId,
        int memberNameId,
        int memberArity,
        int callerUnitKind,
        int currentNsId,
        int enclosingOwnerDeclId,
        int usingStart,
        int usingCount,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        out bool outPrefIsNs,
        out int outPrefResolvedNs,
        out int outNestedDeclId,
        out bool outIsAmbiguous)
    {
        outPrefIsNs = false;
        outPrefResolvedNs = -1;
        outNestedDeclId = -1;
        outIsAmbiguous = false;

        if (callerUnitKind != UnitKinds.App || prefixNsId < 0) return false;
        ReadOnlySpan<byte> prefTxt = names.Text(prefixNsId);
        if (prefTxt.StartsWith("global::"u8)) return false;

        int dotIdx = prefTxt.IndexOf((byte)'.');
        ReadOnlySpan<byte> firstSeg = dotIdx < 0 ? prefTxt : prefTxt.Slice(0, dotIdx);
        ReadOnlySpan<byte> restSegs = dotIdx < 0 ? default : prefTxt.Slice(dotIdx + 1);

        // 0. Check nested type on enclosing type chain
        for (int d = enclosingOwnerDeclId; d >= 0; d = ast.Types[d].OwnerDeclId)
        {
            for (int i = 0; i < ast.Types.Count; i++)
            {
                TypeDecl td = ast.Types[i];
                if (td.OwnerDeclId == d && td.TypeParamCount == 0 && Bytes.Eq(names.Text(td.NameId), firstSeg))
                {
                    return false;
                }
            }
        }

        int step = 0;
        while (TryStepEnclosingNamespace(currentNsId, ref step, in ast, in names, out int nsCursor))
        {
            // 1. Declared 0-arity type or subnamespace in `nsCursor` shadows using aliases at `nsCursor`
            if (nsCursor >= 0)
            {
                for (int i = 0; i < ast.Types.Count; i++)
                {
                    TypeDecl td = ast.Types[i];
                    if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId == nsCursor &&
                        td.TypeParamCount == 0 && Bytes.Eq(names.Text(td.NameId), firstSeg))
                    {
                        return false;
                    }
                }
                ReadOnlySpan<byte> curTxt = names.Text(nsCursor);
                var subNs = new StringBuf(curTxt.Length + 1 + firstSeg.Length);
                subNs.Append(curTxt);
                subNs.AppendAscii((byte)'.');
                subNs.Append(firstSeg);
                if (IsKnownNamespaceBytes(subNs.AsUtf8(), in ast, in names, in bctx))
                {
                    return false;
                }
            }
            else
            {
                for (int i = 0; i < ast.Types.Count; i++)
                {
                    TypeDecl td = ast.Types[i];
                    if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId < 0 &&
                        td.TypeParamCount == 0 && Bytes.Eq(names.Text(td.NameId), firstSeg))
                    {
                        return false;
                    }
                }
                if (IsKnownNamespaceBytes(firstSeg, in ast, in names, in bctx))
                {
                    return false;
                }
            }

            // 2. Check `using` aliases at `nsCursor`
            for (int scopePass = 0; scopePass < 2; scopePass++)
            {
                if (nsCursor >= 0 && scopePass == 1) break;
                for (int u = 0; u < usingCount; u++)
                {
                    int uIdx = ast.Extra[usingStart + u];
                    if (uIdx < 0 || uIdx >= bctx.UsingBoundKind.Count) continue;
                    UsingEntry ue = ast.Usings[uIdx];
                    if (ue.EnclosingNsId != nsCursor || ue.Kind != UsingKinds.Alias) continue;
                    if (nsCursor < 0 && ((scopePass == 0 && ue.ScopeLevel != 1) || (scopePass == 1 && ue.ScopeLevel != 0))) continue;
                    if (Bytes.Eq(names.Text(ue.AliasOrNsId), firstSeg))
                    {
                        int bk = bctx.UsingBoundKind[uIdx];
                        if (bk == 1)
                        {
                            outPrefIsNs = true;
                            int baseNsId = bctx.UsingBoundId[uIdx];
                            if (restSegs.Length == 0)
                            {
                                outPrefResolvedNs = baseNsId;
                            }
                            else if (baseNsId >= 0)
                            {
                                ReadOnlySpan<byte> baseTxt = names.Text(baseNsId);
                                var comb = new StringBuf(baseTxt.Length + 1 + restSegs.Length);
                                comb.Append(baseTxt);
                                comb.AppendAscii((byte)'.');
                                comb.Append(restSegs);
                                outPrefResolvedNs = FindExistingNamespaceIdByBytes(comb.AsUtf8(), in ast, in names, in bctx);
                            }
                            return true;
                        }
                        if (bk == 2)
                        {
                            outPrefIsNs = false;
                            int curDecl = bctx.UsingBoundId[uIdx];
                            int remStart = 0;
                            while (remStart < restSegs.Length && curDecl >= 0)
                            {
                                int dPos = -1;
                                for (int k = remStart; k < restSegs.Length; k++)
                                {
                                    if (restSegs[k] == (byte)'.') { dPos = k; break; }
                                }
                                int segLen = dPos < 0 ? (restSegs.Length - remStart) : (dPos - remStart);
                                int nextDecl = -1;
                                for (int i = 0; i < ast.Types.Count; i++)
                                {
                                    TypeDecl td = ast.Types[i];
                                    if (td.OwnerDeclId == curDecl && td.TypeParamCount == 0 && Bytes.Eq(names.Text(td.NameId), restSegs.Slice(remStart, segLen)))
                                    {
                                        nextDecl = i;
                                        break;
                                    }
                                }
                                remStart = dPos < 0 ? restSegs.Length : (dPos + 1);
                                curDecl = nextDecl;
                            }
                            if (curDecl >= 0)
                            {
                                for (int i = 0; i < ast.Types.Count; i++)
                                {
                                    TypeDecl td = ast.Types[i];
                                    if (td.OwnerDeclId == curDecl && td.NameId == memberNameId && td.TypeParamCount == memberArity)
                                    {
                                        outNestedDeclId = i;
                                        break;
                                    }
                                }
                            }
                            return true;
                        }
                        if (bk == 3)
                        {
                            outPrefIsNs = false;
                            outNestedDeclId = -1;
                            return true;
                        }
                        return false;
                    }
                }
            }

            // 3. If single-segment prefix matches an imported type at `nsCursor`, stop before outer aliases
            if (restSegs.Length == 0)
            {
                var seenDecls = new Vec<int>(4);
                var seenNs = new Vec<int>(4);
                bool mSys = false;
                bool mVib = false;
                int c = 0;
                int sDecl = -1;
                int intFallback = -1;
                for (int u = 0; u < usingCount; u++)
                {
                    int uIdx = ast.Extra[usingStart + u];
                    if (uIdx < 0 || uIdx >= ast.Usings.Count) continue;
                    UsingEntry ue = ast.Usings[uIdx];
                    if (ue.EnclosingNsId != nsCursor) continue;
                    if (ue.Kind == UsingKinds.Namespace)
                    {
                        int uNs = uIdx < bctx.UsingBoundKind.Count && bctx.UsingBoundKind[uIdx] == 1
                            ? bctx.UsingBoundId[uIdx]
                            : ue.AliasOrNsId;
                        if (uNs >= 0)
                        {
                            ImportNamespaceCandidatesAtTier(uNs, prefixNsId, 0, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref mSys, ref mVib, ref c, ref sDecl, ref intFallback);
                        }
                    }
                    else if (ue.Kind == UsingKinds.Static && uIdx < bctx.UsingBoundKind.Count && bctx.UsingBoundKind[uIdx] == 2)
                    {
                        int stDecl = bctx.UsingBoundId[uIdx];
                        for (int i = 0; i < ast.Types.Count; i++)
                        {
                            TypeDecl td = ast.Types[i];
                            if (td.OwnerDeclId == stDecl && td.NameId == prefixNsId && td.TypeParamCount == 0)
                            {
                                if ((td.Flags & DeclFlags.Private) == 0 || IsInsideDecl(enclosingOwnerDeclId, stDecl, in ast))
                                {
                                    AddUniqueDeclCandidate(i, ref seenDecls, ref c, ref sDecl);
                                }
                            }
                        }
                    }
                }
                if (nsCursor < 0)
                {
                    ImportNamespaceCandidatesAtTier(bctx.IdSystem, prefixNsId, 0, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref mSys, ref mVib, ref c, ref sDecl, ref intFallback);
                    ImportNamespaceCandidatesAtTier(bctx.IdVibranium, prefixNsId, 0, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref mSys, ref mVib, ref c, ref sDecl, ref intFallback);
                    if (bctx.HasStdUnit)
                    {
                        ImportNamespaceCandidatesAtTier(bctx.IdVibraniumStd, prefixNsId, 0, in ast, in names, in bctx, ref seenNs, ref seenDecls, ref mSys, ref mVib, ref c, ref sDecl, ref intFallback);
                    }
                }
                if (c > 1)
                {
                    outIsAmbiguous = true;
                    return true;
                }
                if (c == 1)
                {
                    return false;
                }
            }
        }

        return false;
    }

    private static int FindExistingNamespaceIdByBytes(ReadOnlySpan<byte> nsBytes, in AstArena ast, in Interner names, in BinderCtx bctx)
    {
        if (Bytes.Eq(nsBytes, "System"u8)) return bctx.IdSystem;
        if (Bytes.Eq(nsBytes, "Vibranium"u8)) return bctx.IdVibranium;
        if (Bytes.Eq(nsBytes, "Vibranium.Std"u8)) return bctx.IdVibraniumStd;
        return FindInternedDeclaredNamespace(nsBytes, in ast, in names);
    }

    private static int LookupGlobalQualifiedTypeByBytes(
        int nameId,
        int arity,
        ReadOnlySpan<byte> prefixBytes,
        int callerUnitKind,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx)
    {
        // Exact namespace match `global::Ns.Sub.Type`
        int nsId = FindExistingNamespaceIdByBytes(prefixBytes, in ast, in names, in bctx);
        if (nsId >= 0)
        {
            int exact = FindDeclaredTypeInExactNamespace(nameId, arity, nsId, callerUnitKind, in ast, in bctx);
            if (exact >= 0) return exact;
        }

        // Nested type chain `global::Ns.Outer.Inner` or `global::Outer.Inner`
        int curOwner = -1;
        int start = 0;
        for (int pos = 0; pos <= prefixBytes.Length; pos++)
        {
            if (pos == prefixBytes.Length || prefixBytes[pos] == (byte)'.')
            {
                ReadOnlySpan<byte> prefixUpTo = prefixBytes.Slice(0, pos);
                ReadOnlySpan<byte> seg = prefixBytes.Slice(start, pos - start);
                start = pos + 1;

                if (curOwner >= 0)
                {
                    int nextNested = -1;
                    for (int i = 0; i < ast.Types.Count; i++)
                    {
                        TypeDecl td = ast.Types[i];
                        if (td.OwnerDeclId == curOwner && td.TypeParamCount == 0 && Bytes.Eq(names.Text(td.NameId), seg))
                        {
                            nextNested = i;
                            break;
                        }
                    }
                    curOwner = nextNested;
                    if (curOwner < 0) return -1;
                }
                else
                {
                    ReadOnlySpan<byte> nsSlice = start - 1 == seg.Length ? default : prefixUpTo.Slice(0, prefixUpTo.Length - seg.Length - 1);
                    for (int i = 0; i < ast.Types.Count; i++)
                    {
                        TypeDecl td = ast.Types[i];
                        if (td.OwnerDeclId >= 0 || td.TypeParamCount != 0 || !Bytes.Eq(names.Text(td.NameId), seg)) continue;
                        if (callerUnitKind != UnitKinds.App && td.UnitKind == UnitKinds.App) continue;
                        if (!bctx.HasStdUnit && callerUnitKind != UnitKinds.Std && td.UnitKind == UnitKinds.Std) continue;
                        if (nsSlice.Length == 0)
                        {
                            if (td.NsId < 0) { curOwner = i; break; }
                        }
                        else if (td.NsId >= 0 && Bytes.Eq(names.Text(td.NsId), nsSlice))
                        {
                            curOwner = i;
                            break;
                        }
                    }
                }
            }
        }
        if (curOwner >= 0)
        {
            for (int i = 0; i < ast.Types.Count; i++)
            {
                TypeDecl td = ast.Types[i];
                if (td.OwnerDeclId == curOwner && td.NameId == nameId && td.TypeParamCount == arity)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    public static bool TryLookupImportedStaticConst(
        int memberNameId,
        int currentOwnerDeclId,
        int currentNsId,
        int usingStart,
        int usingCount,
        Token tok,
        bool emitDiags,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        ref DiagnosticBag diags,
        ref bool hadDiag,
        out int outKind,
        out int outTypeId,
        out int outEnumDeclId,
        out long outIntVal,
        out double outFltVal)
    {
        if (TryEvalImportedStaticConstMember(memberNameId, currentOwnerDeclId, currentNsId, usingStart, usingCount, tok, emitDiags, in ast, in names, in bctx, ref diags, ref hadDiag, out ConstEvalVal v))
        {
            outKind = v.Kind;
            outTypeId = v.TypeId;
            outEnumDeclId = v.EnumDeclId;
            outIntVal = v.IntVal;
            outFltVal = v.FltVal;
            return true;
        }
        outKind = 0;
        outTypeId = TypeIds.Error;
        outEnumDeclId = -1;
        outIntVal = 0;
        outFltVal = 0.0;
        return false;
    }

    private static bool TryEvalImportedStaticConstMember(
        int memberNameId,
        int currentOwnerDeclId,
        int currentNsId,
        int usingStart,
        int usingCount,
        Token tok,
        bool emitDiags,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        ref DiagnosticBag diags,
        ref bool hadDiag,
        out ConstEvalVal outVal)
    {
        outVal = ConstEvalVal.Invalid();
        var seenOwners = new Vec<long>(4);
        int step = 0;
        while (TryStepEnclosingNamespace(currentNsId, ref step, in ast, in names, out int nsCursor))
        {
            seenOwners.Clear();
            int matchCount = 0;
            int matchedOwnerDecl = -1;
            FieldDecl matchedFd = default;
            ConstEvalVal builtinMatch = ConstEvalVal.Invalid();
            bool hasBuiltinMatch = false;

            for (int u = 0; u < usingCount; u++)
            {
                int uIdx = ast.Extra[usingStart + u];
                if (uIdx < 0 || uIdx >= bctx.UsingBoundKind.Count) continue;
                UsingEntry ue = ast.Usings[uIdx];
                if (ue.EnclosingNsId != nsCursor || ue.Kind != UsingKinds.Static) continue;

                int bk = bctx.UsingBoundKind[uIdx];
                if (bk == 2)
                {
                    int stDecl = bctx.UsingBoundId[uIdx];
                    int stTid = bctx.UsingBoundTypeId[uIdx];
                    long ownerKey = ((long)stDecl << 32) | (uint)(stTid + 1);
                    bool dup = false;
                    for (int s = 0; s < seenOwners.Count; s++)
                    {
                        if (seenOwners[s] == ownerKey) { dup = true; break; }
                    }
                    if (dup) continue;
                    seenOwners.Push(ownerKey);

                    TypeDecl stTd = ast.Types[stDecl];
                    for (int f = 0; f < stTd.FieldCount; f++)
                    {
                        FieldDecl fd = ast.Fields[stTd.FieldStart + f];
                        if (fd.NameId == memberNameId && (fd.Flags & DeclFlags.IsConst) != 0 &&
                            IsConstFieldAccessibleFromOwner(currentOwnerDeclId, stDecl, in fd, in ast, in bctx))
                        {
                            matchCount += 1;
                            matchedOwnerDecl = stDecl;
                            matchedFd = fd;
                            break;
                        }
                    }
                }
                else if (bk == 3)
                {
                    int bId = bctx.UsingBoundId[uIdx];
                    int bTid = bctx.UsingBoundTypeId[uIdx];
                    long dedupKey = -1000000L + ((long)bId << 16) + bTid;
                    bool dup = false;
                    for (int s = 0; s < seenOwners.Count; s++)
                    {
                        if (seenOwners[s] == dedupKey) { dup = true; break; }
                    }
                    if (dup) continue;
                    seenOwners.Push(dedupKey);

                    if (bTid > 0)
                    {
                        if (TryEvalBuiltinNumericConstMember(PrimTypeIdBuiltinName(bTid), names.Text(memberNameId), out int pTid, out long pBits, out bool pIsFloat))
                        {
                            matchCount += 1;
                            hasBuiltinMatch = true;
                            if (!pIsFloat)
                            {
                                builtinMatch = ConstEvalVal.MakeInt(pTid, pBits);
                            }
                            else if (pTid == TypeIds.F32)
                            {
                                builtinMatch = ConstEvalVal.MakeF32Bits(unchecked((int)pBits));
                            }
                            else
                            {
                                builtinMatch = ConstEvalVal.MakeF64Bits(pBits);
                            }
                        }
                    }
                }
            }

            if (matchCount > 1)
            {
                if (emitDiags)
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Ambiguous constant or enum member reference across 'using static' directives (CS0229/CS0104)."u8);
                    hadDiag = true;
                }
                return false;
            }
            if (matchCount == 1)
            {
                if (hasBuiltinMatch)
                {
                    outVal = builtinMatch;
                    return true;
                }
                if (matchedFd.HasConstVal == 0)
                {
                    return false;
                }
                outVal = ReconstructConstFieldVal(matchedOwnerDecl, in matchedFd, in ast, in names, in bctx);
                return outVal.Kind != 0;
            }
        }
        return false;
    }

    public static int FindNestedTypeInEnclosingChain(int nameId, int arity, int enclosingOwnerDeclId, in AstArena ast)
    {
        for (int d = enclosingOwnerDeclId; d >= 0; d = ast.Types[d].OwnerDeclId)
        {
            for (int i = 0; i < ast.Types.Count; i++)
            {
                TypeDecl td = ast.Types[i];
                if (td.OwnerDeclId == d && td.NameId == nameId && td.TypeParamCount == arity)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    public static int FindAppTypeInLexicalScope(
        int nameId,
        int arity,
        int currentNsId,
        int enclosingOwnerDeclId,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx)
    {
        int nested = FindNestedTypeInEnclosingChain(nameId, arity, enclosingOwnerDeclId, in ast);
        if (nested >= 0) return nested;

        if (currentNsId >= 0)
        {
            ReadOnlySpan<byte> fullNs = names.Text(currentNsId);
            int curLen = fullNs.Length;
            while (curLen > 0)
            {
                ReadOnlySpan<byte> prefixNs = fullNs.Slice(0, curLen);
                for (int i = 0; i < ast.Types.Count; i++)
                {
                    TypeDecl td = ast.Types[i];
                    if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId >= 0 &&
                        td.NameId == nameId && td.TypeParamCount == arity &&
                        Bytes.Eq(names.Text(td.NsId), prefixNs))
                    {
                        return i;
                    }
                }
                int dot = -1;
                for (int k = curLen - 1; k >= 0; k--)
                {
                    if (fullNs[k] == (byte)'.')
                    {
                        dot = k;
                        break;
                    }
                }
                curLen = dot;
            }
        }

        Option<int> globOpt = bctx.AppGlobalTypeByNameArity.Get(PackNameArity(nameId, arity));
        if (globOpt.IsSome) return globOpt.Unwrap();
        return -1;
    }

    private static bool CanMatchSystemBuiltinType(int nameId, int arity, in Interner names, in BinderCtx bctx)
    {
        if (arity == 0)
        {
            if (nameId == bctx.IdObject || nameId == bctx.IdIDisposable || nameId == bctx.IdMemoryExtensions)
            {
                return true;
            }
            ReadOnlySpan<byte> nm = names.Text(nameId);
            return Bytes.Eq(nm, "Object"u8) || Bytes.Eq(nm, "IntPtr"u8) || Bytes.Eq(nm, "UIntPtr"u8) ||
                   Bytes.Eq(nm, "Boolean"u8) || Bytes.Eq(nm, "Byte"u8) || Bytes.Eq(nm, "SByte"u8) ||
                   Bytes.Eq(nm, "Int16"u8) || Bytes.Eq(nm, "UInt16"u8) || Bytes.Eq(nm, "Char"u8) ||
                   Bytes.Eq(nm, "Int32"u8) || Bytes.Eq(nm, "UInt32"u8) || Bytes.Eq(nm, "Int64"u8) ||
                   Bytes.Eq(nm, "UInt64"u8) || Bytes.Eq(nm, "Single"u8) || Bytes.Eq(nm, "Double"u8) ||
                   Bytes.Eq(nm, "String"u8) || Bytes.Eq(nm, "Half"u8) || Bytes.Eq(nm, "Int128"u8) ||
                   Bytes.Eq(nm, "UInt128"u8) || Bytes.Eq(nm, "IDisposable"u8) || Bytes.Eq(nm, "Console"u8) ||
                   Bytes.Eq(nm, "Array"u8) || Bytes.Eq(nm, "MemoryExtensions"u8) || Bytes.Eq(nm, "Index"u8) ||
                   Bytes.Eq(nm, "Range"u8);
        }
        if (arity == 1)
        {
            return nameId == bctx.IdNullable || nameId == bctx.IdSpan || nameId == bctx.IdReadOnlySpan;
        }
        return false;
    }

    private static bool CanMatchVibraniumCoreBuiltinType(int nameId, int arity, in Interner names, in BinderCtx bctx)
    {
        if (arity == 0)
        {
            if (nameId == bctx.IdUtf8Lines || nameId == bctx.IdUtf8ArgEnumerator ||
                nameId == bctx.IdUnit || nameId == bctx.IdNoneType ||
                nameId == bctx.IdPrelude || nameId == bctx.IdIo || nameId == bctx.IdStdio ||
                nameId == bctx.IdVibraniumRuntime || nameId == bctx.IdStringUtf8Extensions ||
                nameId == bctx.IdUtf8SpanExtensions || nameId == bctx.IdOptionExtensions)
            {
                return true;
            }
            ReadOnlySpan<byte> nm = names.Text(nameId);
            return Bytes.Eq(nm, "Unit"u8) || Bytes.Eq(nm, "NoneType"u8);
        }
        if (arity == 1)
        {
            return nameId == bctx.IdOption || nameId == bctx.IdSome || nameId == bctx.IdOk ||
                   nameId == bctx.IdErr || nameId == bctx.IdOptionRef || nameId == bctx.IdOptionMut ||
                   nameId == bctx.IdVec;
        }
        if (arity == 2)
        {
            return nameId == bctx.IdResult;
        }
        return false;
    }

    private static void AddUniqueDeclCandidate(int declId, ref Vec<int> seenDecls, ref int count, ref int singleDeclId)
    {
        for (int i = 0; i < seenDecls.Count; i++)
        {
            if (seenDecls[i] == declId) return;
        }
        seenDecls.Push(declId);
        singleDeclId = declId;
        count += 1;
    }

    public static int LookupUnqualifiedTypeDecl(
        int nameId,
        int arity,
        int unitKind,
        int currentNsId,
        int enclosingOwnerDeclId,
        int usingStart,
        int usingCount,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx)
    {
        if (unitKind == UnitKinds.App)
        {
            int rk = ResolveAppUnqualifiedName(
                nameId,
                arity,
                currentNsId,
                enclosingOwnerDeclId,
                usingStart,
                usingCount,
                in ast,
                in names,
                in bctx,
                out int foundDecl,
                out int foundAliasIdx,
                out bool mSys,
                out bool mVib);
            if (rk == 1) return foundDecl;
            if (rk == 2 && foundAliasIdx >= 0 && foundAliasIdx < bctx.UsingBoundKind.Count)
            {
                if (bctx.UsingBoundKind[foundAliasIdx] == 2)
                {
                    int aliasedDecl = bctx.UsingBoundId[foundAliasIdx];
                    if (ast.Types[aliasedDecl].TypeParamCount == arity)
                    {
                        return aliasedDecl;
                    }
                }
                return -1;
            }
            if (rk == 3 && !mSys && !mVib)
            {
                return foundDecl;
            }
            return -1;
        }
        int nested = FindNestedTypeInEnclosingChain(nameId, arity, enclosingOwnerDeclId, in ast);
        if (nested >= 0) return nested;
        Option<int> stdOpt = bctx.StdTypeByNameArity.Get(PackNameArity(nameId, arity));
        return stdOpt.IsSome ? stdOpt.Unwrap() : -1;
    }

    public static int FindDeclaredTypeInExactNamespace(
        int nameId,
        int arity,
        int exactNsId,
        int callerUnitKind,
        in AstArena ast,
        in BinderCtx bctx)
    {
        if (callerUnitKind == UnitKinds.App)
        {
            for (int i = 0; i < ast.Types.Count; i++)
            {
                TypeDecl td = ast.Types[i];
                if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId == exactNsId &&
                    td.NameId == nameId && td.TypeParamCount == arity)
                {
                    return i;
                }
            }
        }
        if (bctx.HasStdUnit || callerUnitKind == UnitKinds.Std)
        {
            for (int i = 0; i < ast.Types.Count; i++)
            {
                TypeDecl td = ast.Types[i];
                if (td.UnitKind == UnitKinds.Std && td.OwnerDeclId < 0 && td.NsId == exactNsId &&
                    td.NameId == nameId && td.TypeParamCount == arity)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    public static int LookupQualifiedTypeDecl(
        int nameId,
        int arity,
        int prefixNsId,
        int callerUnitKind,
        int currentNsId,
        int enclosingOwnerDeclId,
        int usingStart,
        int usingCount,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx)
    {
        if (callerUnitKind == UnitKinds.App &&
            TryResolvePrefixViaAlias(prefixNsId, nameId, arity, callerUnitKind, currentNsId, enclosingOwnerDeclId, usingStart, usingCount, in ast, in names, in bctx, out bool pIsNs, out int pNs, out int aNested, out bool pAmb))
        {
            if (pAmb) return -1;
            if (pIsNs) return pNs >= 0 ? FindDeclaredTypeInExactNamespace(nameId, arity, pNs, callerUnitKind, in ast, in bctx) : -1;
            return aNested;
        }

        // 1. Prefix might be an enclosing/imported non-generic outer type (`Outer.Inner`)
        int outerDecl = LookupUnqualifiedTypeDecl(prefixNsId, 0, callerUnitKind, currentNsId, enclosingOwnerDeclId, usingStart, usingCount, in ast, in names, in bctx);
        if (outerDecl >= 0)
        {
            for (int i = 0; i < ast.Types.Count; i++)
            {
                TypeDecl td = ast.Types[i];
                if (td.OwnerDeclId == outerDecl && td.NameId == nameId && td.TypeParamCount == arity)
                {
                    return i;
                }
            }
        }

        // 2. Relative namespace inside `currentNsId` (`namespace A.B { C.Type }` -> `A.B.C`, then `A.C`)
        if (currentNsId >= 0 && callerUnitKind == UnitKinds.App)
        {
            ReadOnlySpan<byte> curNsTxt = names.Text(currentNsId);
            ReadOnlySpan<byte> subTxt = names.Text(prefixNsId);
            int curLen = curNsTxt.Length;
            while (curLen > 0)
            {
                ReadOnlySpan<byte> prefSlice = curNsTxt.Slice(0, curLen);
                var combined = new StringBuf(prefSlice.Length + 1 + subTxt.Length);
                combined.Append(prefSlice);
                combined.AppendAscii((byte)'.');
                combined.Append(subTxt);
                for (int i = 0; i < ast.Types.Count; i++)
                {
                    TypeDecl td = ast.Types[i];
                    if (td.UnitKind == UnitKinds.App && td.OwnerDeclId < 0 && td.NsId >= 0 &&
                        td.NameId == nameId && td.TypeParamCount == arity &&
                        Bytes.Eq(names.Text(td.NsId), combined.AsUtf8()))
                    {
                        return i;
                    }
                }
                int dot = -1;
                for (int k = curLen - 1; k >= 0; k--)
                {
                    if (curNsTxt[k] == (byte)'.') { dot = k; break; }
                }
                curLen = dot;
            }
        }

        // 3. Exact namespace match (`Ns.SubNs.Type`) or nested type inside namespace (`Ns.Outer.Inner`)
        int exact = FindDeclaredTypeInExactNamespace(nameId, arity, prefixNsId, callerUnitKind, in ast, in bctx);
        if (exact >= 0) return exact;

        return LookupGlobalQualifiedTypeByBytes(nameId, arity, names.Text(prefixNsId), callerUnitKind, in ast, in names, in bctx);
    }

    private static bool TryResolveSystemBuiltinType(
        int nameId,
        ReadOnlySpan<int> resolvedArgs,
        Token tok,
        bool reportLinearErrors,
        in AstArena ast,
        in Interner names,
        in BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags,
        out int resolvedTypeId)
    {
        resolvedTypeId = TypeIds.Error;
        int argCount = resolvedArgs.Length;
        if (argCount == 0)
        {
            ReadOnlySpan<byte> nm = names.Text(nameId);
            if (nameId == bctx.IdObject || Bytes.Eq(nm, "Object"u8))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "'System.Object' type is banned in Vibranium."u8);
                resolvedTypeId = TypeIds.Error;
                return true;
            }
            if (Bytes.Eq(nm, "IntPtr"u8)) { resolvedTypeId = TypeIds.NInt; return true; }
            if (Bytes.Eq(nm, "UIntPtr"u8)) { resolvedTypeId = TypeIds.NUInt; return true; }
            if (Bytes.Eq(nm, "Boolean"u8)) { resolvedTypeId = TypeIds.Bool; return true; }
            if (Bytes.Eq(nm, "Byte"u8)) { resolvedTypeId = TypeIds.U8; return true; }
            if (Bytes.Eq(nm, "SByte"u8)) { resolvedTypeId = TypeIds.I8; return true; }
            if (Bytes.Eq(nm, "Int16"u8)) { resolvedTypeId = TypeIds.I16; return true; }
            if (Bytes.Eq(nm, "UInt16"u8)) { resolvedTypeId = TypeIds.U16; return true; }
            if (Bytes.Eq(nm, "Char"u8)) { resolvedTypeId = TypeIds.Char; return true; }
            if (Bytes.Eq(nm, "Int32"u8)) { resolvedTypeId = TypeIds.I32; return true; }
            if (Bytes.Eq(nm, "UInt32"u8)) { resolvedTypeId = TypeIds.U32; return true; }
            if (Bytes.Eq(nm, "Int64"u8)) { resolvedTypeId = TypeIds.I64; return true; }
            if (Bytes.Eq(nm, "UInt64"u8)) { resolvedTypeId = TypeIds.U64; return true; }
            if (Bytes.Eq(nm, "Single"u8)) { resolvedTypeId = TypeIds.F32; return true; }
            if (Bytes.Eq(nm, "Double"u8)) { resolvedTypeId = TypeIds.F64; return true; }
            if (Bytes.Eq(nm, "String"u8)) { resolvedTypeId = TypeIds.Str; return true; }
            if (Bytes.Eq(nm, "Half"u8) || Bytes.Eq(nm, "Int128"u8) || Bytes.Eq(nm, "UInt128"u8))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Half, Int128, and UInt128 are not supported in Vibranium."u8);
                resolvedTypeId = TypeIds.I32;
                return true;
            }
            if (Bytes.Eq(nm, "IDisposable"u8))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Interface type 'IDisposable' cannot be used as a variable, parameter, return, or type argument in Vibranium."u8);
                resolvedTypeId = TypeIds.I32;
                return true;
            }
            if (Bytes.Eq(nm, "Index"u8) || Bytes.Eq(nm, "Range"u8))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Standalone 'Index' and 'Range' types are not supported in Vibranium."u8);
                resolvedTypeId = TypeIds.Error;
                return true;
            }
            if (Bytes.Eq(nm, "Console"u8) || Bytes.Eq(nm, "Array"u8) || Bytes.Eq(nm, "MemoryExtensions"u8))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Static or managed array class cannot be used as a variable, parameter, return, or type argument in Vibranium."u8);
                resolvedTypeId = TypeIds.Error;
                return true;
            }
            return false;
        }
        if (argCount == 1)
        {
            int a0 = resolvedArgs[0];
            if (nameId == bctx.IdNullable)
            {
                diags.ReportTok(VibCodes.NullOrZeroUnsafe, tok, "System.Nullable<T> is banned in Vibranium; use Option<T>."u8);
                resolvedTypeId = types.MakeOption(a0);
                return true;
            }
            if (nameId == bctx.IdSpan)
            {
                if (reportLinearErrors && !types.IsCopy(a0, in ast))
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2."u8);
                }
                resolvedTypeId = types.MakeSpan(a0, isReadOnly: false);
                return true;
            }
            if (nameId == bctx.IdReadOnlySpan)
            {
                if (reportLinearErrors && !types.IsCopy(a0, in ast))
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2."u8);
                }
                resolvedTypeId = types.MakeSpan(a0, isReadOnly: true);
                return true;
            }
        }
        return false;
    }

    private static int FindFuncEndTokIdx(in FuncDecl fd, in AstArena ast, in TokenArena ta)
    {
        if (fd.BodyNode >= 0)
        {
            int bodyTok = ast.Nodes[fd.BodyNode].TokIdx;
            if (bodyTok >= 0 && bodyTok < ta.Tokens.Count && ta.Tokens[bodyTok].Kind == TokKind.LBrace)
            {
                int depth = 1;
                int t = bodyTok + 1;
                while (t < ta.Tokens.Count && depth > 0)
                {
                    TokKind k = ta.Tokens[t].Kind;
                    if (k == TokKind.LBrace) depth += 1;
                    else if (k == TokKind.RBrace) depth -= 1;
                    t += 1;
                }
                return t - 1;
            }
        }
        int scan = fd.ParamCount > 0 ? ast.Params[fd.ParamStart + fd.ParamCount - 1].TokIdx : fd.TokIdx;
        if (fd.BodyNode >= 0 && ast.Nodes[fd.BodyNode].TokIdx > scan)
        {
            scan = ast.Nodes[fd.BodyNode].TokIdx;
        }
        int pDepth = 0;
        while (scan < ta.Tokens.Count)
        {
            TokKind k = ta.Tokens[scan].Kind;
            if (k == TokKind.LParen || k == TokKind.LBracket || k == TokKind.LBrace) pDepth += 1;
            else if (k == TokKind.RParen || k == TokKind.RBracket || k == TokKind.RBrace)
            {
                if (pDepth > 0) pDepth -= 1;
                else if (k == TokKind.RBrace) return scan;
            }
            else if (k == TokKind.Semi && pDepth == 0)
            {
                return scan;
            }
            scan += 1;
        }
        return ta.Tokens.Count - 1;
    }

    private static bool IsClassConstrainedTypeParam(
        int tpNameId,
        int useTokIdx,
        int enclosingOwnerDeclId,
        in AstArena ast,
        in TokenArena ta)
    {
        int srcId = useTokIdx >= 0 && useTokIdx < ta.Tokens.Count ? ta.Tokens[useTokIdx].SourceId : -1;
        int bestSpan = 2147483647;
        int bestFlags = -1;
        if (srcId >= 0)
        {
            for (int f = 0; f < ast.Funcs.Count; f++)
            {
                FuncDecl fd = ast.Funcs[f];
                if (fd.SourceId != srcId || fd.TypeParamCount == 0) continue;
                int matchedIdx = -1;
                for (int k = 0; k < fd.TypeParamCount; k++)
                {
                    if (ast.Extra[fd.TypeParamStart + k] == tpNameId)
                    {
                        matchedIdx = k;
                        break;
                    }
                }
                if (matchedIdx < 0) continue;
                int startTok = fd.RetTypeNode >= 0 ? ast.Nodes[fd.RetTypeNode].TokIdx : fd.TokIdx;
                if (fd.TokIdx < startTok) startTok = fd.TokIdx;
                if (useTokIdx < startTok) continue;
                int endTok = FindFuncEndTokIdx(in fd, in ast, in ta);
                if (useTokIdx <= endTok)
                {
                    int span = endTok - startTok;
                    if (span < bestSpan)
                    {
                        bestSpan = span;
                        bestFlags = ast.Extra[fd.TypeParamFlagsStart + matchedIdx];
                    }
                }
            }
        }
        if (bestFlags >= 0)
        {
            return (bestFlags & DeclFlags.ClassConstraint) != 0;
        }

        for (int d = enclosingOwnerDeclId; d >= 0; d = ast.Types[d].OwnerDeclId)
        {
            TypeDecl td = ast.Types[d];
            for (int k = 0; k < td.TypeParamCount; k++)
            {
                if (ast.Extra[td.TypeParamStart + k] == tpNameId)
                {
                    return (ast.Extra[td.TypeParamFlagsStart + k] & DeclFlags.ClassConstraint) != 0;
                }
            }
        }
        return false;
    }

    private static bool IsValidOptionMutElementType(
        int elemTypeId,
        int useTokIdx,
        int enclosingOwnerDeclId,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types)
    {
        if (elemTypeId == TypeIds.Error || elemTypeId == TypeIds.Str) return true;
        TypeInfo ti = types.Items[elemTypeId];
        if (ti.Kind == TypeKinds.Class) return true;
        if (ti.Kind == TypeKinds.TypeParam)
        {
            if (ti.A != 0) return false;
            return IsClassConstrainedTypeParam(ti.B, useTokIdx, enclosingOwnerDeclId, in ast, in ta);
        }
        return false;
    }

    private static bool TryResolveVibraniumCoreType(
        int nameId,
        ReadOnlySpan<int> resolvedArgs,
        Token tok,
        int useTokIdx,
        int enclosingOwnerDeclId,
        bool reportLinearErrors,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags,
        out int resolvedTypeId)
    {
        resolvedTypeId = TypeIds.Error;
        int argCount = resolvedArgs.Length;
        if (argCount == 0)
        {
            if (nameId == bctx.IdUtf8Lines) { resolvedTypeId = TypeIds.Utf8Lines; return true; }
            if (nameId == bctx.IdUtf8ArgEnumerator) { resolvedTypeId = TypeIds.Utf8ArgEnum; return true; }
            ReadOnlySpan<byte> nm = names.Text(nameId);
            if (Bytes.Eq(nm, "Unit"u8) || Bytes.Eq(nm, "NoneType"u8))
            {
                resolvedTypeId = TypeIds.NoneUnit;
                return true;
            }
            if (nameId == bctx.IdPrelude || nameId == bctx.IdIo || nameId == bctx.IdStdio ||
                nameId == bctx.IdVibraniumRuntime || nameId == bctx.IdStringUtf8Extensions ||
                nameId == bctx.IdUtf8SpanExtensions || nameId == bctx.IdOptionExtensions)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Static helper class cannot be used as a variable, parameter, return, or type argument in Vibranium."u8);
                resolvedTypeId = TypeIds.Error;
                return true;
            }
            return false;
        }
        if (argCount == 1)
        {
            int a0 = resolvedArgs[0];
            if (nameId == bctx.IdOption) { resolvedTypeId = types.MakeOption(a0); return true; }
            if (nameId == bctx.IdSome) { resolvedTypeId = types.MakeSomeWrap(a0); return true; }
            if (nameId == bctx.IdOk) { resolvedTypeId = types.MakeOkWrap(a0); return true; }
            if (nameId == bctx.IdErr)
            {
                if (reportLinearErrors && types.IsLinear(a0, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot instantiate 'Err<E>'."u8);
                }
                resolvedTypeId = types.MakeErrWrap(a0);
                return true;
            }
            if (nameId == bctx.IdOptionRef)
            {
                if (reportLinearErrors && types.IsLinear(a0, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot instantiate 'OptionRef<T>'."u8);
                }
                resolvedTypeId = types.MakeOptionRef(a0);
                return true;
            }
            if (nameId == bctx.IdOptionMut)
            {
                if (!IsValidOptionMutElementType(a0, useTokIdx, enclosingOwnerDeclId, in ast, in ta, in types))
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "OptionMut<T> requires reference type 'T' ('where T : class')."u8);
                }
                else if (reportLinearErrors && types.IsLinear(a0, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot instantiate 'OptionMut<T>'."u8);
                }
                resolvedTypeId = types.MakeOptionMut(a0);
                return true;
            }
            if (nameId == bctx.IdVec)
            {
                if (reportLinearErrors && types.IsLinear(a0, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource cannot instantiate 'Vec<T>'."u8);
                }
                else if (!types.IsUnmanaged(a0))
                {
                    diags.ReportTok(VibCodes.Unsupported, tok, "Vec<T> requires unmanaged Copy element type."u8);
                }
                resolvedTypeId = types.MakeVec(a0);
                return true;
            }
            return false;
        }
        if (argCount == 2 && nameId == bctx.IdResult)
        {
            int okT = resolvedArgs[0];
            int errT = resolvedArgs[1];
            if (reportLinearErrors)
            {
                if (types.IsLinear(errT, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource is only permitted as the Ok payload 'T' of 'Result<T, E>', never as 'E'."u8);
                }
                else if (types.IsLinear(okT, in ast) && !types.IsCopy(errT, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, tok, "Linear 'Result<R, E>' requires Copy error type 'E'."u8);
                }
            }
            resolvedTypeId = types.MakeResult(okT, errT);
            return true;
        }
        return false;
    }

    public static int MapPrimTokKind(TokKind k)
    {
        switch (k)
        {
            case TokKind.KwVoid: return TypeIds.Void;
            case TokKind.KwBool: return TypeIds.Bool;
            case TokKind.KwSByte: return TypeIds.I8;
            case TokKind.KwByte: return TypeIds.U8;
            case TokKind.KwShort: return TypeIds.I16;
            case TokKind.KwUShort: return TypeIds.U16;
            case TokKind.KwChar: return TypeIds.Char;
            case TokKind.KwInt: return TypeIds.I32;
            case TokKind.KwUInt: return TypeIds.U32;
            case TokKind.KwLong: return TypeIds.I64;
            case TokKind.KwULong: return TypeIds.U64;
            case TokKind.KwNInt: return TypeIds.NInt;
            case TokKind.KwNUInt: return TypeIds.NUInt;
            case TokKind.KwFloat: return TypeIds.F32;
            case TokKind.KwDouble: return TypeIds.F64;
            case TokKind.KwString: return TypeIds.Str;
            default: return TypeIds.Error;
        }
    }

    private static void RegisterSysNativeMethodsIfAny(
        int declId,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        TypeDecl td = ast.Types[declId];
        var emptyArgs = new Vec<int>();
        for (int m = 0; m < td.FuncCount; m++)
        {
            int fId = td.FuncStart + m;
            FuncDecl fd = ast.Funcs[fId];
            if ((fd.Flags & DeclFlags.HasNativeAttr) != 0)
            {
                if (!ValidateSysCatalogEntry(declId, in fd, in ast, in ta, in names, ref bctx, ref types, ref diags))
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Method with '[Native]' is not in the trusted Vibranium.Std.Sys catalog."u8);
                }
                else
                {
                    _ = EnsureFuncInstance(fId, -1, emptyArgs.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
                }
            }
        }
    }

    private static void CheckTypeDeclGatekeeper(
        int declId,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        TypeDecl td = ast.Types[declId];

        var tpNames = new Vec<int>(td.TypeParamCount);
        var tpVals = new Vec<int>(td.TypeParamCount);
        for (int i = 0; i < td.TypeParamCount; i++)
        {
            int tpName = ast.Extra[td.TypeParamStart + i];
            int constraintFlags = ast.Extra[td.TypeParamFlagsStart + i];
            tpNames.Push(tpName);
            tpVals.Push(types.MakeTypeParam(tpName, names.Text(tpName), constraintFlags));
        }

        CheckTypeMemberDeclarations(declId, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
        if (td.Kind == TypeDeclKinds.Enum) return;

        if (td.Kind == TypeDeclKinds.Class && (td.Flags & DeclFlags.IsDisposable) != 0)
        {
            bool hasValidDispose = false;
            for (int m = 0; m < td.FuncCount; m++)
            {
                FuncDecl mfd = ast.Funcs[td.FuncStart + m];
                if (IsValidDisposableDisposeMethod(in mfd, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types))
                {
                    hasValidDispose = true;
                    break;
                }
            }
            if (!hasValidDispose)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[td.TokIdx], "'IDisposable' class must declare a public, non-static, non-generic, parameterless 'void Dispose()' method."u8);
            }
        }

        bool hasExplicitCtor = false;
        for (int m = 0; m < td.FuncCount; m++)
        {
            FuncDecl fd = ast.Funcs[td.FuncStart + m];
            if ((fd.Flags & DeclFlags.IsCtor) != 0 && (fd.Flags & DeclFlags.Partial) == 0)
            {
                hasExplicitCtor = true;
                break;
            }
        }

        for (int f = 0; f < td.FieldCount; f++)
        {
            FieldDecl fld = ast.Fields[td.FieldStart + f];
            int fType = ResolveTypeNode(fld.TypeNode, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
            if (fType != TypeIds.Error && IsRefStructType(fType, in types))
            {
                if ((fld.Flags & DeclFlags.IsCapturedPrimaryParam) != 0)
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fld.TokIdx], "A ref-like primary-constructor parameter cannot be used inside an instance member (CS9110)."u8);
                else
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fld.TokIdx], "A ref-like field is only permitted as an instance member of a ref struct (CS8345)."u8);
                continue;
            }
            if ((fld.Flags & DeclFlags.Static) != 0) continue;
            if (td.Kind == TypeDeclKinds.Struct && fld.InitExpr >= 0 && !hasExplicitCtor)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fld.TokIdx], "A struct with instance field initializers requires an explicit constructor (CS8983)."u8);
            if (td.Kind == TypeDeclKinds.Struct && (td.Flags & DeclFlags.Readonly) != 0 &&
                (fld.Flags & DeclFlags.Readonly) == 0)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fld.TokIdx], "Instance fields of readonly structs must be readonly (CS8340)."u8);
            }
            DrainConcreteTypeQueue(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);

            if (fType == TypeIds.Utf8Lines || fType == TypeIds.Utf8ArgEnum)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fld.TokIdx], "'Utf8Lines' and 'Utf8ArgEnumerator' are closed foreach views and cannot be stored in fields."u8);
            }
            else if (types.IsLinear(fType, in ast))
            {
                diags.ReportTok(VibCodes.LinearViolation, ta.Tokens[fld.TokIdx], "Linear IDisposable resource cannot be stored in a field."u8);
            }
            else if (td.Kind == TypeDeclKinds.Class && (td.Flags & DeclFlags.Static) == 0 && !hasExplicitCtor && fld.InitExpr < 0)
            {
                if (!types.IsZeroSafe(fType, in ast))
                {
                    diags.ReportTok(VibCodes.NullOrZeroUnsafe, ta.Tokens[fld.TokIdx], "Field of non-zero-safe type must be initialized."u8);
                }
            }
        }

        for (int m = 0; m < td.FuncCount; m++)
        {
            int fId = td.FuncStart + m;
            FuncDecl fd = ast.Funcs[fId];
            if ((fd.Flags & DeclFlags.HasNativeAttr) != 0)
            {
                if (!ValidateSysCatalogEntry(declId, in fd, in ast, in ta, in names, ref bctx, ref types, ref diags))
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Method with '[Native]' is not in the trusted Vibranium.Std.Sys catalog."u8);
                }
            }

            var allTpNames = new Vec<int>(tpNames.Count + fd.TypeParamCount);
            var allTpVals = new Vec<int>(tpVals.Count + fd.TypeParamCount);
            for (int i = 0; i < tpNames.Count; i++) { allTpNames.Push(tpNames[i]); allTpVals.Push(tpVals[i]); }
            for (int i = 0; i < fd.TypeParamCount; i++)
            {
                int mTpName = ast.Extra[fd.TypeParamStart + i];
                int constraintFlags = ast.Extra[fd.TypeParamFlagsStart + i];
                allTpNames.Push(mTpName);
                allTpVals.Push(types.MakeTypeParam(mTpName, names.Text(mTpName), constraintFlags));
            }

            if ((fd.Flags & DeclFlags.UnscopedRefAttr) != 0 &&
                (td.Kind != TypeDeclKinds.Struct || (fd.Flags & (DeclFlags.Static | DeclFlags.IsCtor)) != 0))
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "UnscopedRef is only valid on struct instance methods or properties (CS9101)."u8);

            if (td.Kind == TypeDeclKinds.Struct && fd.ParamCount == 0 &&
                (fd.Flags & (DeclFlags.IsCtor | DeclFlags.Static | DeclFlags.Public)) == DeclFlags.IsCtor)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "The parameterless struct constructor must be public."u8);
            }

            int retType = ResolveTypeNode(fd.RetTypeNode, allTpNames.AsReadOnlySpan(), allTpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
            if ((fd.Flags & DeclFlags.IsOpAddAssign) != 0)
            {
                if ((fd.Flags & DeclFlags.Static) != 0 ||
                    retType != TypeIds.Void ||
                    fd.ParamCount != 1 ||
                    ast.Params[fd.ParamStart].Mode != PassMode.ByValue)
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Only instance 'void operator +=(T)' with a single by-value parameter is supported in Vibranium."u8);
                }
            }
            if ((retType == TypeIds.Utf8Lines || retType == TypeIds.Utf8ArgEnum) &&
                (fd.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet | DeclFlags.IsIndexer | DeclFlags.ReturnsByRef)) != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "'Utf8Lines' and 'Utf8ArgEnumerator' are only permitted as ordinary method return types or direct 'foreach' collections."u8);
            }
            for (int p = 0; p < fd.ParamCount; p++)
            {
                ParamDecl pd = ast.Params[fd.ParamStart + p];
                int pType = ResolveTypeNode(pd.TypeNode, allTpNames.AsReadOnlySpan(), allTpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
                DrainConcreteTypeQueue(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
                CheckScopedParameter(in pd, pType, in ta, in types, ref diags);
                if (p == 0 && (fd.Flags & DeclFlags.IsExtension) != 0 &&
                    (pd.Mode == PassMode.Ref || pd.Mode == PassMode.In) && pType != TypeIds.Error)
                {
                    // A ref extension receiver must be statically known to be a value type.
                    // Ref structs are value types too, though the view gate below can still reject them.
                    TypeInfo receiver = types.Items[pType];
                    bool referenceType = receiver.Kind == TypeKinds.Class || pType == TypeIds.Str;
                    bool typeParam = receiver.Kind == TypeKinds.TypeParam;
                    if (pd.Mode == PassMode.In && (referenceType || typeParam))
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "The first 'in' or 'ref readonly' parameter of an extension method must be a concrete (non-generic) value type (CS8338)."u8);
                    }
                    else if (pd.Mode == PassMode.Ref && (referenceType ||
                             (typeParam && (receiver.C & (DeclFlags.StructConstraint | DeclFlags.UnmanagedConstraint)) == 0)))
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "The receiver of a ref extension method must be a value type or a type parameter constrained to struct (CS8337)."u8);
                    }
                }

                if (pType == TypeIds.Utf8Lines || pType == TypeIds.Utf8ArgEnum)
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "'Utf8Lines' and 'Utf8ArgEnumerator' cannot be used as method parameters."u8);
                }
                else if (pd.Mode == PassMode.Ref || pd.Mode == PassMode.Out)
                {
                    if ((fd.Flags & DeclFlags.IsIndexer) != 0)
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "'ref' and 'out' parameters are not permitted on indexers."u8);
                    }
                    if (types.IsView(pType))
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "Parameter cannot pass borrowed view by ref or out in Vibranium."u8);
                    }
                    else if (types.IsLinear(pType, in ast))
                    {
                        diags.ReportTok(VibCodes.LinearViolation, ta.Tokens[pd.TokIdx], "Parameter cannot pass linear IDisposable resource by ref or out."u8);
                    }
                }
            }
        }
    }

    // Member kinds of one type's declaration space.
    private const int MemberField = 0;
    private const int MemberProperty = 1;
    private const int MemberMethod = 2;
    private const int MemberNestedType = 3;
    private const int MemberTypeParam = 4;
    private const int MemberIndexer = 5;

    // C#'s member declaration space of one type: one declaration per name,
    // except overloads of methods, constructors and indexers with different
    // signatures (CS0102, CS0111, CS0663) and nested types of different arity;
    // no member named like its enclosing type (CS0542); and a static class
    // holds only static members (CS0708, CS0710, CS0715, CS0720).
    private static void CheckTypeMemberDeclarations(
        int declId,
        ReadOnlySpan<int> tpNames,
        ReadOnlySpan<int> tpVals,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        TypeDecl td = ast.Types[declId];
        if (td.Kind == TypeDeclKinds.Class && (td.Flags & DeclFlags.Static) != 0)
            CheckStaticClassMembers(in td, in ast, in ta, ref diags);

        var memberNames = new Vec<int>(td.FieldCount + td.FuncCount + 4);
        var memberKinds = new Vec<int>(td.FieldCount + td.FuncCount + 4);
        var memberToks = new Vec<int>(td.FieldCount + td.FuncCount + 4);
        var memberArity = new Vec<int>(td.FieldCount + td.FuncCount + 4);
        for (int i = 0; i < tpNames.Length; i++)
        {
            memberNames.Push(tpNames[i]); memberKinds.Push(MemberTypeParam); memberToks.Push(td.TokIdx); memberArity.Push(0);
        }
        for (int f = 0; f < td.FieldCount; f++)
        {
            FieldDecl fld = ast.Fields[td.FieldStart + f];
            // Captured primary-constructor parameters are synthesized storage, not declarations.
            if (names.Text(fld.NameId).StartsWith("__pctor_"u8)) continue;
            memberNames.Push(fld.NameId); memberKinds.Push(MemberField); memberToks.Push(fld.TokIdx); memberArity.Push(0);
        }
        for (int m = 0; m < td.FuncCount; m++)
        {
            FuncDecl fd = ast.Funcs[td.FuncStart + m];
            if ((fd.Flags & (DeclFlags.IsLocalFunc | DeclFlags.IsCtor | DeclFlags.IsTopLevelMain)) != 0) continue;
            int kind = MemberMethod;
            if ((fd.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet)) != 0)
            {
                kind = (fd.Flags & DeclFlags.IsIndexer) != 0 ? MemberIndexer : MemberProperty;
                // The accessors of one property share its declaration token.
                bool seen = false;
                for (int k = 0; k < memberToks.Count; k++)
                    if (memberToks[k] == fd.TokIdx && memberKinds[k] == kind) { seen = true; break; }
                if (seen) continue;
            }
            memberNames.Push(td.FuncStart + m); memberKinds.Push(kind); memberToks.Push(fd.TokIdx); memberArity.Push(fd.TypeParamCount);
        }
        for (int t = 0; t < ast.Types.Count; t++)
        {
            TypeDecl nested = ast.Types[t];
            if (nested.OwnerDeclId != declId) continue;
            memberNames.Push(nested.NameId); memberKinds.Push(MemberNestedType); memberToks.Push(nested.TokIdx); memberArity.Push(nested.TypeParamCount);
        }

        ReadOnlySpan<byte> typeName = names.Text(td.NameId);
        for (int a = 0; a < memberKinds.Count; a++)
        {
            ReadOnlySpan<byte> aName = MemberNameText(memberNames[a], memberKinds[a], in ast, in names);
            if (td.Kind != TypeDeclKinds.Enum && memberKinds[a] != MemberTypeParam && memberKinds[a] != MemberIndexer && Bytes.Eq(aName, typeName))
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[memberToks[a]], "Member names cannot be the same as their enclosing type (CS0542)."u8);
            for (int b = 0; b < a; b++)
            {
                int ka = memberKinds[a];
                int kb = memberKinds[b];
                // Methods and indexers overload by signature (checked below);
                // nested types of different arity are different names.
                if ((ka == MemberMethod && kb == MemberMethod) || (ka == MemberIndexer && kb == MemberIndexer)) continue;
                if (ka == MemberNestedType && kb == MemberNestedType && memberArity[a] != memberArity[b]) continue;
                if (ka == MemberTypeParam && kb == MemberTypeParam) continue;
                if (!Bytes.Eq(aName, MemberNameText(memberNames[b], kb, in ast, in names))) continue;
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[memberToks[a]], "The type already contains a definition for this member name (CS0102)."u8);
                break;
            }
        }

        CheckDuplicateSignatures(declId, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref diags);
    }

    [return: LifetimeOf("names")]
    private static ReadOnlySpan<byte> MemberNameText(int member, int kind, in AstArena ast, in Interner names)
    {
        if (kind == MemberIndexer) return "Item"u8;
        if (kind == MemberProperty) return names.Text(ast.Funcs[member].NameId).Slice(4);
        if (kind == MemberMethod) return names.Text(ast.Funcs[member].NameId);
        return names.Text(member);
    }

    private static void CheckStaticClassMembers(in TypeDecl td, in AstArena ast, in TokenArena ta, ref DiagnosticBag diags)
    {
        for (int f = 0; f < td.FieldCount; f++)
        {
            FieldDecl fld = ast.Fields[td.FieldStart + f];
            if ((fld.Flags & (DeclFlags.Static | DeclFlags.IsConst)) == 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fld.TokIdx], "Cannot declare instance members in a static class (CS0708)."u8);
        }
        int lastReported = -1;
        for (int m = 0; m < td.FuncCount; m++)
        {
            FuncDecl fd = ast.Funcs[td.FuncStart + m];
            if ((fd.Flags & DeclFlags.IsLocalFunc) != 0 || fd.TokIdx == lastReported) continue;
            if ((fd.Flags & DeclFlags.IsCtor) != 0)
            {
                if ((fd.Flags & DeclFlags.Static) != 0) continue;
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Static classes cannot have instance constructors (CS0710)."u8);
            }
            else if ((fd.Flags & DeclFlags.IsIndexer) != 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Static classes cannot contain indexers (CS0720)."u8);
            else if ((fd.Flags & DeclFlags.IsOpAddAssign) != 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Static classes cannot contain user-defined operators (CS0715)."u8);
            else if ((fd.Flags & DeclFlags.Static) == 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Cannot declare instance members in a static class (CS0708)."u8);
            else continue;
            lastReported = fd.TokIdx;
        }
    }

    // Two methods, constructors or indexers of one type with the same name,
    // type parameter count and parameter types (method type parameters
    // compared by position) are the same signature (CS0111); differing only in
    // ref, out and in is CS0663. Return types, constraints, defaults, params
    // and `this` are not part of the signature.
    private static void CheckDuplicateSignatures(
        int declId,
        ReadOnlySpan<int> tpNames,
        ReadOnlySpan<int> tpVals,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        TypeDecl td = ast.Types[declId];
        var sigFuncs = new Vec<int>(td.FuncCount);
        var sigParamCounts = new Vec<int>(td.FuncCount);
        var sigTypeStart = new Vec<int>(td.FuncCount);
        var sigTypes = new Vec<int>(td.FuncCount * 2);
        var scratch = new DiagnosticBag(4);
        for (int m = 0; m < td.FuncCount; m++)
        {
            int fId = td.FuncStart + m;
            FuncDecl fd = ast.Funcs[fId];
            if ((fd.Flags & (DeclFlags.IsLocalFunc | DeclFlags.IsTopLevelMain | DeclFlags.Partial)) != 0) continue;
            int paramCount = fd.ParamCount;
            if ((fd.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet)) != 0)
            {
                // Properties are names, not signatures. An indexer is its
                // getter's signature, or its setter's without `value`.
                if ((fd.Flags & DeclFlags.IsIndexer) == 0) continue;
                if ((fd.Flags & DeclFlags.IsPropSet) != 0)
                {
                    if (HasIndexerGetter(in td, fd.TokIdx, in ast)) continue;
                    paramCount -= 1;
                }
            }
            var allNames = new Vec<int>(tpNames.Length + fd.TypeParamCount);
            var allVals = new Vec<int>(tpNames.Length + fd.TypeParamCount);
            for (int i = 0; i < tpNames.Length; i++) { allNames.Push(tpNames[i]); allVals.Push(tpVals[i]); }
            for (int i = 0; i < fd.TypeParamCount; i++)
            {
                var canonical = new StringBuf(8);
                canonical.Append("!!"u8);
                canonical.AppendInt(i);
                allNames.Push(ast.Extra[fd.TypeParamStart + i]);
                allVals.Push(types.MakeTypeParam(-1, canonical.AsUtf8(), 0));
            }
            sigFuncs.Push(fId);
            sigParamCounts.Push(paramCount);
            sigTypeStart.Push(sigTypes.Count);
            for (int p = 0; p < paramCount; p++)
                sigTypes.Push(ResolveTypeNode(ast.Params[fd.ParamStart + p].TypeNode, allNames.AsReadOnlySpan(), allVals.AsReadOnlySpan(),
                    in ast, in ta, in names, ref bctx, ref types, ref scratch, reportLinearErrors: false));
        }

        for (int a = 0; a < sigFuncs.Count; a++)
        {
            FuncDecl fa = ast.Funcs[sigFuncs[a]];
            for (int b = 0; b < a; b++)
            {
                FuncDecl fb = ast.Funcs[sigFuncs[b]];
                if (sigParamCounts[a] != sigParamCounts[b] || fa.TypeParamCount != fb.TypeParamCount) continue;
                bool aIndexer = (fa.Flags & DeclFlags.IsIndexer) != 0;
                if (aIndexer != ((fb.Flags & DeclFlags.IsIndexer) != 0)) continue;
                if (!aIndexer && fa.NameId != fb.NameId) continue;
                // A static constructor and an instance constructor coexist.
                if ((fa.Flags & DeclFlags.IsCtor) != 0 && (fa.Flags & DeclFlags.Static) != (fb.Flags & DeclFlags.Static)) continue;
                bool sameTypes = true;
                bool sameModes = true;
                bool sameByRef = true;
                for (int p = 0; p < sigParamCounts[a]; p++)
                {
                    int ta0 = sigTypes[sigTypeStart[a] + p];
                    int tb0 = sigTypes[sigTypeStart[b] + p];
                    if (ta0 != tb0 || ta0 == TypeIds.Error) { sameTypes = false; break; }
                    PassMode ma = ast.Params[fa.ParamStart + p].Mode;
                    PassMode mb = ast.Params[fb.ParamStart + p].Mode;
                    if (ma != mb) sameModes = false;
                    if ((ma == PassMode.ByValue) != (mb == PassMode.ByValue)) sameByRef = false;
                }
                if (!sameTypes || !sameByRef) continue;
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fa.TokIdx], sameModes
                    ? "The type already defines a member with the same parameter types (CS0111)."u8
                    : "Overloads cannot differ only in ref, out and in (CS0663)."u8);
                break;
            }
        }

        // A property or indexer reserves both accessor names, whichever
        // accessors it declares: get_P(index parameters) and set_P(index
        // parameters, value) (get_Item/set_Item for an indexer). A method with
        // one of those names and the same parameter types clashes with it
        // (CS0082); its type parameters do not tell it apart.
        for (int m = 0; m < td.FuncCount; m++)
        {
            FuncDecl acc = ast.Funcs[td.FuncStart + m];
            if ((acc.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet)) == 0 || (acc.Flags & DeclFlags.IsLocalFunc) != 0) continue;
            bool firstOfDeclaration = true;
            for (int k = 0; k < m; k++)
            {
                FuncDecl other = ast.Funcs[td.FuncStart + k];
                if (other.TokIdx == acc.TokIdx && (other.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet)) != 0) { firstOfDeclaration = false; break; }
            }
            if (!firstOfDeclaration) continue;
            bool isGetter = (acc.Flags & DeclFlags.IsPropGet) != 0;
            int indexCount = isGetter ? acc.ParamCount : acc.ParamCount - 1;
            int valueTypeNode = isGetter ? acc.RetTypeNode : ast.Params[acc.ParamStart + acc.ParamCount - 1].TypeNode;
            var reserved = new Vec<int>(indexCount + 1);
            for (int p = 0; p < indexCount; p++)
                reserved.Push(ResolveTypeNode(ast.Params[acc.ParamStart + p].TypeNode, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref scratch, reportLinearErrors: false));
            reserved.Push(ResolveTypeNode(valueTypeNode, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref scratch, reportLinearErrors: false));
            ReadOnlySpan<byte> propName = names.Text(acc.NameId).Slice(4);
            for (int b = 0; b < sigFuncs.Count; b++)
            {
                FuncDecl method = ast.Funcs[sigFuncs[b]];
                if ((method.Flags & (DeclFlags.IsPropGet | DeclFlags.IsPropSet | DeclFlags.IsCtor)) != 0) continue;
                ReadOnlySpan<byte> methodName = names.Text(method.NameId);
                if (methodName.Length != propName.Length + 4 || !Bytes.Eq(methodName.Slice(4), propName)) continue;
                bool reservesGet = Bytes.Eq(methodName.Slice(0, 4), "get_"u8);
                if (!reservesGet && !Bytes.Eq(methodName.Slice(0, 4), "set_"u8)) continue;
                int expectedCount = reservesGet ? indexCount : indexCount + 1;
                if (sigParamCounts[b] != expectedCount) continue;
                bool same = true;
                for (int p = 0; p < expectedCount; p++)
                {
                    int rt = p < indexCount ? reserved[p] : reserved[indexCount];
                    int mt = sigTypes[sigTypeStart[b] + p];
                    if (rt != mt || rt == TypeIds.Error || ast.Params[method.ParamStart + p].Mode != PassMode.ByValue) { same = false; break; }
                }
                if (!same) continue;
                int laterTok = method.TokIdx > acc.TokIdx ? method.TokIdx : acc.TokIdx;
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[laterTok], "The type already reserves a member with this accessor name and parameter types (CS0082)."u8);
            }
        }
    }

    private static bool HasIndexerGetter(in TypeDecl td, int declTokIdx, in AstArena ast)
    {
        for (int m = 0; m < td.FuncCount; m++)
        {
            FuncDecl fd = ast.Funcs[td.FuncStart + m];
            if (fd.TokIdx == declTokIdx && (fd.Flags & DeclFlags.IsPropGet) != 0 && (fd.Flags & DeclFlags.IsIndexer) != 0) return true;
        }
        return false;
    }

    public static void PopulateTypeFieldsOnly(
        int typeId,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        TypeInfo ti = types.Items[typeId];
        if (ti.FieldStart >= 0 || (ti.Kind != TypeKinds.Class && ti.Kind != TypeKinds.Struct))
        {
            return;
        }
        // Mark in-progress with 0 fields first so cyclic struct declarations terminate cleanly
        ti.FieldStart = types.Fields.Count;
        ti.FieldCount = 0;
        types.Items[typeId] = ti;

        TypeDecl td = ast.Types[ti.A];
        var tpNames = new Vec<int>(td.TypeParamCount);
        var tpVals = new Vec<int>(td.TypeParamCount);
        for (int i = 0; i < td.TypeParamCount; i++)
        {
            tpNames.Push(ast.Extra[td.TypeParamStart + i]);
            tpVals.Push(types.TypeArgs[ti.B + i]);
        }

        var resolvedFields = new Vec<ConcreteField>();
        for (int f = 0; f < td.FieldCount; f++)
        {
            FieldDecl fd = ast.Fields[td.FieldStart + f];
            if ((fd.Flags & DeclFlags.Static) != 0) continue;
            int fTypeId = ResolveTypeNode(fd.TypeNode, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false);
            int capParamId = (fd.Flags & DeclFlags.IsCapturedPrimaryParam) != 0 ? (int)fd.ConstVal : -1;
            resolvedFields.Push(new ConcreteField(fd.NameId, fTypeId, fd.Flags, capParamId));
        }

        TypeInfo updated = types.Items[typeId];
        updated.FieldStart = types.Fields.Count;
        updated.FieldCount = resolvedFields.Count;
        for (int i = 0; i < resolvedFields.Count; i++)
        {
            types.Fields.Push(resolvedFields[i]);
        }
        types.Items[typeId] = updated;
        if (updated.Kind == TypeKinds.Struct && HasRecursiveValueLayout(typeId, in types))
        {
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[td.TokIdx], "A struct cannot contain itself through instance value-type fields (CS0523)."u8);
            // Keep error recovery finite too: later layout and definite-assignment
            // passes must never expand the invalid cycle after this diagnostic.
            updated.FieldCount = 0;
            types.Items[typeId] = updated;
        }
    }

    private static bool HasRecursiveValueLayout(int rootType, in TypeTable types)
    {
        var pending = new Vec<int>();
        var visited = new Vec<byte>(types.Items.Count);
        visited.Resize(types.Items.Count, 0);
        TypeInfo root = types.Items[rootType];
        for (int field = 0; field < root.FieldCount; field++)
            pending.Push(types.Fields[root.FieldStart + field].TypeId);
        while (pending.Count > 0)
        {
            int current = pending.Pop().Unwrap();
            if (current == rootType) return true;
            if (current < 0 || current >= types.Items.Count || visited[current] != 0) continue;
            visited[current] = 1;
            TypeInfo type = types.Items[current];
            if (type.Kind == TypeKinds.Struct)
            {
                for (int field = 0; field < type.FieldCount; field++)
                    pending.Push(types.Fields[type.FieldStart + field].TypeId);
            }
            else if (type.Kind == TypeKinds.Option || type.Kind == TypeKinds.SomeWrap ||
                type.Kind == TypeKinds.OkWrap || type.Kind == TypeKinds.ErrWrap)
            {
                pending.Push(type.A);
            }
            else if (type.Kind == TypeKinds.Result)
            {
                pending.Push(type.A);
                pending.Push(type.B);
            }
            // Classes, owned buffers, references and views contain indirection,
            // not an inline instance of their payload type.
        }
        return false;
    }

    // `scoped` applies to a ref, in or out parameter, or a by-value ref struct (CS9048).
    private static void CheckScopedParameter(in ParamDecl pd, int pType, in TokenArena ta, in TypeTable types, ref DiagnosticBag diags)
    {
        if ((pd.Flags & DeclFlags.UnscopedRefAttr) != 0)
        {
            if (pd.Mode == PassMode.ByValue)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "UnscopedRef requires a by-reference parameter (CS9063)."u8);
            else if ((pd.Flags & DeclFlags.IsScoped) != 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "UnscopedRef cannot be applied to a scoped parameter (CS9066)."u8);
        }
        if ((pd.Flags & DeclFlags.IsScoped) == 0 || pd.Mode != PassMode.ByValue) return;
        if (IsRefStructType(pType, in types)) return;
        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "The 'scoped' modifier can be used for refs and ref struct values only (CS9048)."u8);
    }

    // Span<T>, ReadOnlySpan<T>, OptionRef<T>, OptionMut<T>, Utf8Lines and
    // Utf8ArgEnumerator are ref structs. A type parameter never is: Vibranium
    // bans the 'allows ref struct' anti-constraint. Error types are not
    // reported again.
    public static bool IsRefStructType(int typeId, in TypeTable types)
    {
        if (typeId < 0 || typeId == TypeIds.Error) return true;
        int kind = types.Items[typeId].Kind;
        return kind == TypeKinds.Span || kind == TypeKinds.ReadOnlySpan ||
            kind == TypeKinds.OptionRef || kind == TypeKinds.OptionMut ||
            kind == TypeKinds.Utf8Lines || kind == TypeKinds.Utf8ArgEnum;
    }

    private static void CheckLocalFuncGatekeeper(
        in FuncDecl fd,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        var allTpNames = new Vec<int>(fd.TypeParamCount);
        var allTpVals = new Vec<int>(fd.TypeParamCount);
        for (int i = 0; i < fd.TypeParamCount; i++)
        {
            int mTpName = ast.Extra[fd.TypeParamStart + i];
            int constraintFlags = ast.Extra[fd.TypeParamFlagsStart + i];
            allTpNames.Push(mTpName);
            allTpVals.Push(types.MakeTypeParam(mTpName, names.Text(mTpName), constraintFlags));
        }

        int lfRetType = ResolveTypeNode(fd.RetTypeNode, allTpNames.AsReadOnlySpan(), allTpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
        if ((lfRetType == TypeIds.Utf8Lines || lfRetType == TypeIds.Utf8ArgEnum) &&
            (fd.Flags & DeclFlags.ReturnsByRef) != 0)
        {
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "'Utf8Lines' and 'Utf8ArgEnumerator' cannot be returned by ref."u8);
        }
        for (int p = 0; p < fd.ParamCount; p++)
        {
            ParamDecl pd = ast.Params[fd.ParamStart + p];
            int pType = ResolveTypeNode(pd.TypeNode, allTpNames.AsReadOnlySpan(), allTpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
            CheckScopedParameter(in pd, pType, in ta, in types, ref diags);
            DrainConcreteTypeQueue(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
            if (pType == TypeIds.Utf8Lines || pType == TypeIds.Utf8ArgEnum)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "'Utf8Lines' and 'Utf8ArgEnumerator' cannot be used as local function parameters."u8);
            }
            else if (pd.Mode == PassMode.Ref || pd.Mode == PassMode.Out)
            {
                if (types.IsView(pType))
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "Parameter cannot pass borrowed view by ref or out in Vibranium."u8);
                }
                else if (types.IsLinear(pType, in ast))
                {
                    diags.ReportTok(VibCodes.LinearViolation, ta.Tokens[pd.TokIdx], "Parameter cannot pass linear IDisposable resource by ref or out."u8);
                }
            }
        }
    }

    private static bool ValidateSysCatalogEntry(
        int ownerDeclId,
        in FuncDecl fd,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref DiagnosticBag diags)
    {
        TypeDecl owner = ast.Types[ownerDeclId];
        if (owner.UnitKind != UnitKinds.Std) return false;
        if ((fd.Flags & DeclFlags.Static) == 0 || fd.TypeParamCount != 0) return false;
        if (!Bytes.Eq(names.Text(owner.NameId), "Sys"u8)) return false;
        if (owner.NsId < 0 || !Bytes.Eq(names.Text(owner.NsId), "Vibranium.Std"u8)) return false;
        if (fd.NativeSymLitIdx < 0) return false;

        ReadOnlySpan<byte> mName = names.Text(fd.NameId);
        ReadOnlySpan<byte> sym = ta.GetLit(fd.NativeSymLitIdx);
        var empty = new Vec<int>();
        int retT = ResolveTypeNode(fd.RetTypeNode, empty.AsReadOnlySpan(), empty.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false);

        int pCount = fd.ParamCount;
        var pTypes = new Vec<int>(pCount);
        for (int i = 0; i < pCount; i++)
        {
            ParamDecl pd = ast.Params[fd.ParamStart + i];
            if (pd.Mode != PassMode.ByValue) return false;
            pTypes.Push(ResolveTypeNode(pd.TypeNode, empty.AsReadOnlySpan(), empty.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false));
        }

        int roSpanU8 = types.MakeSpan(TypeIds.U8, isReadOnly: true);
        int rwSpanU8 = types.MakeSpan(TypeIds.U8, isReadOnly: false);
        int roSpanI32 = types.MakeSpan(TypeIds.I32, isReadOnly: true);

        if (Bytes.Eq(mName, "FileOpen"u8)) return Bytes.Eq(sym, "__vib_sys_file_open"u8) && retT == TypeIds.I32 && pCount == 2 && pTypes[0] == roSpanU8 && pTypes[1] == TypeIds.I32;
        if (Bytes.Eq(mName, "FileRead"u8)) return Bytes.Eq(sym, "__vib_sys_file_read"u8) && retT == TypeIds.I64 && pCount == 2 && pTypes[0] == TypeIds.I32 && pTypes[1] == rwSpanU8;
        if (Bytes.Eq(mName, "FileWriteAll"u8)) return Bytes.Eq(sym, "__vib_sys_file_write_all"u8) && retT == TypeIds.I32 && pCount == 2 && pTypes[0] == TypeIds.I32 && pTypes[1] == roSpanU8;
        if (Bytes.Eq(mName, "FileSync"u8)) return Bytes.Eq(sym, "__vib_sys_file_sync"u8) && retT == TypeIds.I32 && pCount == 1 && pTypes[0] == TypeIds.I32;
        if (Bytes.Eq(mName, "Close"u8)) return Bytes.Eq(sym, "__vib_sys_close"u8) && retT == TypeIds.Void && pCount == 1 && pTypes[0] == TypeIds.I32;
        if (Bytes.Eq(mName, "TcpListen"u8)) return Bytes.Eq(sym, "__vib_sys_tcp_listen"u8) && retT == TypeIds.I32 && pCount == 3 && pTypes[0] == TypeIds.I32 && pTypes[1] == TypeIds.I32 && pTypes[2] == TypeIds.I32;
        if (Bytes.Eq(mName, "TcpAccept"u8)) return Bytes.Eq(sym, "__vib_sys_tcp_accept"u8) && retT == TypeIds.I32 && pCount == 1 && pTypes[0] == TypeIds.I32;
        if (Bytes.Eq(mName, "TcpConnect"u8)) return Bytes.Eq(sym, "__vib_sys_tcp_connect"u8) && retT == TypeIds.I32 && pCount == 2 && pTypes[0] == TypeIds.I32 && pTypes[1] == TypeIds.I32;
        if (Bytes.Eq(mName, "SockRecv"u8)) return Bytes.Eq(sym, "__vib_sys_sock_recv"u8) && retT == TypeIds.I64 && pCount == 2 && pTypes[0] == TypeIds.I32 && pTypes[1] == rwSpanU8;
        if (Bytes.Eq(mName, "SockSendAll"u8)) return Bytes.Eq(sym, "__vib_sys_sock_send_all"u8) && retT == TypeIds.I32 && pCount == 2 && pTypes[0] == TypeIds.I32 && pTypes[1] == roSpanU8;
        if (Bytes.Eq(mName, "SockShutdownWrite"u8)) return Bytes.Eq(sym, "__vib_sys_sock_shutdown_write"u8) && retT == TypeIds.I32 && pCount == 1 && pTypes[0] == TypeIds.I32;
        if (Bytes.Eq(mName, "SockLocalPort"u8)) return Bytes.Eq(sym, "__vib_sys_sock_local_port"u8) && retT == TypeIds.I32 && pCount == 1 && pTypes[0] == TypeIds.I32;
        if (Bytes.Eq(mName, "SpawnWait"u8)) return Bytes.Eq(sym, "__vib_sys_spawn_wait"u8) && retT == TypeIds.I32 && pCount == 2 && pTypes[0] == roSpanU8 && pTypes[1] == roSpanI32;
        if (Bytes.Eq(mName, "ArgCount"u8)) return Bytes.Eq(sym, "__vib_sys_arg_count"u8) && retT == TypeIds.I32 && pCount == 0;
        if (Bytes.Eq(mName, "ArgCopy"u8)) return Bytes.Eq(sym, "__vib_sys_arg_copy"u8) && retT == TypeIds.I32 && pCount == 2 && pTypes[0] == TypeIds.I32 && pTypes[1] == rwSpanU8;
        if (Bytes.Eq(mName, "StdinRead"u8)) return Bytes.Eq(sym, "__vib_sys_stdin_read"u8) && retT == TypeIds.I64 && pCount == 1 && pTypes[0] == rwSpanU8;
        if (Bytes.Eq(mName, "FileExists"u8)) return Bytes.Eq(sym, "__vib_sys_file_exists"u8) && retT == TypeIds.I32 && pCount == 1 && pTypes[0] == roSpanU8;
        return false;
    }

    private static void ValidateTypeArguments(
        int paramCount,
        int flagsStart,
        ReadOnlySpan<int> args,
        Token tok,
        in AstArena ast,
        in TypeTable types,
        ref DiagnosticBag diags)
    {
        if (args.Length != paramCount)
        {
            diags.ReportTok(VibCodes.Unsupported, tok, "Generic type argument count must match the declaration (CS0305)."u8);
        }
        for (int i = 0; i < paramCount && i < args.Length; i++)
        {
            if (!types.SatisfiesTypeConstraint(args[i], ast.Extra[flagsStart + i]))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Type argument does not satisfy the declared class, struct, or unmanaged constraint."u8);
            }
        }
    }

    public static int EnsureFuncInstance(
        int funcDeclId,
        int ownerTypeId,
        ReadOnlySpan<int> methodTypeArgs,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        FuncDecl fd = ast.Funcs[funcDeclId];
        ValidateTypeArguments(fd.TypeParamCount, fd.TypeParamFlagsStart, methodTypeArgs, ta.Tokens[fd.TokIdx], in ast, in types, ref diags);

        // Check generic nesting depth
        int maxDepth = ownerTypeId >= 0 ? types.GetNestingDepth(ownerTypeId) : 0;
        for (int i = 0; i < methodTypeArgs.Length; i++)
        {
            int d = types.GetNestingDepth(methodTypeArgs[i]);
            if (d > maxDepth) maxDepth = d;
        }
        if (maxDepth > MaxGenericDepth)
        {
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Polymorphic recursion in generic method is banned in Vibranium."u8);
            return -1;
        }

        // Build canonical function key
        var kb = new StringBuf(96);
        kb.Append("fn:"u8);
        kb.AppendInt(funcDeclId);
        kb.AppendAscii((byte)':');
        if (ownerTypeId >= 0)
        {
            kb.Append(types.GetKey(ownerTypeId));
        }
        else if (fd.OwnerDeclId >= 0)
        {
            kb.Append(names.Text(ast.Types[fd.OwnerDeclId].NameId));
        }
        else
        {
            kb.Append("local"u8);
        }
        kb.AppendAscii((byte)'.');
        kb.Append(names.Text(fd.NameId));
        if (methodTypeArgs.Length > 0)
        {
            kb.AppendAscii((byte)'<');
            for (int i = 0; i < methodTypeArgs.Length; i++)
            {
                if (i > 0) kb.AppendAscii((byte)',');
                kb.Append(types.GetKey(methodTypeArgs[i]));
            }
            kb.AppendAscii((byte)'>');
        }

        int existing = mir.FindFuncInstance(kb.AsUtf8());
        if (existing >= 0)
        {
            return existing;
        }

        // Build type substitution list
        var tpNames = new Vec<int>();
        var tpVals = new Vec<int>();
        if (ownerTypeId >= 0)
        {
            TypeInfo oti = types.Items[ownerTypeId];
            TypeDecl otd = ast.Types[oti.A];
            for (int i = 0; i < otd.TypeParamCount; i++)
            {
                tpNames.Push(ast.Extra[otd.TypeParamStart + i]);
                tpVals.Push(types.TypeArgs[oti.B + i]);
            }
        }
        for (int i = 0; i < fd.TypeParamCount && i < methodTypeArgs.Length; i++)
        {
            tpNames.Push(ast.Extra[fd.TypeParamStart + i]);
            tpVals.Push(methodTypeArgs[i]);
        }

        bool isCtorFactory = (fd.Flags & DeclFlags.IsCtor) != 0;
        int retTypeId = isCtorFactory && ownerTypeId >= 0
            ? ownerTypeId
            : ResolveTypeNode(fd.RetTypeNode, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false);

        bool returnsByRef = !isCtorFactory && (fd.Flags & DeclFlags.ReturnsByRef) != 0;
        bool returnsView = returnsByRef || types.IsView(retTypeId);

        bool hasImplicitThis = (fd.Flags & DeclFlags.Static) == 0 && !isCtorFactory && ownerTypeId >= 0;
        bool hasExplicitLifetimeAttr = (fd.Flags & DeclFlags.HasReturnLifetimeAttr) != 0;

        var paramSigs = new Vec<MirParamSig>(fd.ParamCount + 1);
        if (hasImplicitThis)
        {
            PassMode recvMode = GetReceiverPassMode(in fd, ownerTypeId, in ast, in types, bctx.IdDispose);
            int recvSigFlags = 0;
            if ((fd.Flags & DeclFlags.UnscopedRefAttr) != 0)
            {
                recvSigFlags |= 2;
            }
            if (hasExplicitLifetimeAttr && !LifetimeListContains(in ast, in fd, bctx.IdThis))
            {
                recvSigFlags |= 1;
            }
            paramSigs.Push(new MirParamSig(bctx.IdThis, ownerTypeId, recvMode, recvSigFlags));
        }

        int unannotatedBorrowedParams = 0;
        if (returnsView && !hasExplicitLifetimeAttr && !hasImplicitThis)
        {
            for (int p = 0; p < fd.ParamCount; p++)
            {
                ParamDecl pd = ast.Params[fd.ParamStart + p];
                if ((pd.Flags & DeclFlags.IsScoped) != 0 ||
                    (pd.Mode == PassMode.Out && (pd.Flags & DeclFlags.UnscopedRefAttr) == 0)) continue;
                int pTid = ResolveTypeNode(pd.TypeNode, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false);
                if (pd.Mode == PassMode.In || pd.Mode == PassMode.Ref || pd.Mode == PassMode.Out || types.IsView(pTid))
                {
                    unannotatedBorrowedParams += 1;
                }
            }
        }

        for (int p = 0; p < fd.ParamCount; p++)
        {
            ParamDecl pd = ast.Params[fd.ParamStart + p];
            int pTid = ResolveTypeNode(pd.TypeNode, tpNames.AsReadOnlySpan(), tpVals.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags, reportLinearErrors: false);
            bool isScoped = (pd.Flags & DeclFlags.IsScoped) != 0 ||
                (pd.Mode == PassMode.Out && (pd.Flags & DeclFlags.UnscopedRefAttr) == 0);
            if (hasExplicitLifetimeAttr)
            {
                if (!LifetimeListContains(in ast, in fd, pd.NameId))
                {
                    isScoped = true;
                }
            }
            else if (returnsView && (hasImplicitThis || unannotatedBorrowedParams >= 2))
            {
                isScoped = true;
            }
            int sigFlags = isScoped ? 1 : 0;
            // Bit 2 describes the C# declaration even when Vibranium's return
            // lifetime excludes this parameter (bit 1).
            if ((pd.Flags & DeclFlags.IsScoped) == 0 && (pd.Flags & DeclFlags.UnscopedRefAttr) != 0) sigFlags |= 2;
            paramSigs.Push(new MirParamSig(pd.NameId, pTid, pd.Mode, sigFlags));
        }

        int mtaStart = mir.MethodTypeArgs.Count;
        mir.MethodTypeArgs.Extend(methodTypeArgs);
        int psStart = mir.ParamSigs.Count;
        for (int i = 0; i < paramSigs.Count; i++) mir.ParamSigs.Push(paramSigs[i]);

        int instFlags = fd.Flags;
        if (fd.OwnerDeclId >= 0 && ast.Types[fd.OwnerDeclId].UnitKind == UnitKinds.Std && !bctx.CompilingStdItself)
        {
            instFlags |= DeclFlags.Internal;
        }

        int nativeLit = (fd.Flags & DeclFlags.HasNativeAttr) != 0 && fd.OwnerDeclId >= 0 && ast.Types[fd.OwnerDeclId].UnitKind == UnitKinds.Std ? fd.NativeSymLitIdx : -1;
        if (nativeLit >= 0)
        {
            mir.UsesStdNative = true;
        }

        var inst = new FuncInstance(funcDeclId, ownerTypeId, mtaStart, methodTypeArgs.Length, psStart, paramSigs.Count, retTypeId, instFlags, nativeLit);
        int instId = mir.RegisterFuncInstance(kb.AsUtf8(), inst);

        DrainConcreteTypeQueue(in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);

        if (nativeLit < 0 && fd.BodyNode >= 0 && !HasAnyOpenTypeParam(ownerTypeId, methodTypeArgs, in types))
        {
            bctx.FuncWorkQueue.Push(instId);
        }

        return instId;
    }

    private static bool HasAnyOpenTypeParam(int ownerTypeId, ReadOnlySpan<int> methodTypeArgs, in TypeTable types)
    {
        if (ownerTypeId >= 0 && types.ContainsTypeParam(ownerTypeId)) return true;
        for (int i = 0; i < methodTypeArgs.Length; i++)
        {
            if (types.ContainsTypeParam(methodTypeArgs[i])) return true;
        }
        return false;
    }

    private static bool LifetimeListContains(in AstArena ast, in FuncDecl fd, int nameId)
    {
        // An already rejected annotation cannot constrain otherwise valid returns.
        if (fd.LifetimeCount < 0) return true;
        for (int i = 0; i < fd.LifetimeCount; i++)
        {
            if (ast.Extra[fd.LifetimeExtraStart + i] == nameId) return true;
        }
        return false;
    }

    public static PassMode GetReceiverPassMode(in FuncDecl fd, int ownerTypeId, in AstArena ast, in TypeTable types, int idDispose)
    {
        TypeInfo ti = types.Items[ownerTypeId];
        TypeDecl td = ast.Types[ti.A];
        if (td.Kind == TypeDeclKinds.Class)
        {
            bool isDisp = fd.NameId == idDispose &&
                          fd.ParamCount == 0 &&
                          fd.TypeParamCount == 0 &&
                          (fd.Flags & DeclFlags.Public) != 0 &&
                          (fd.Flags & DeclFlags.Private) == 0 &&
                          (fd.Flags & DeclFlags.Static) == 0 &&
                          (fd.Flags & (DeclFlags.ReturnsByRef | DeclFlags.IsPropGet | DeclFlags.IsPropSet | DeclFlags.IsIndexer | DeclFlags.IsOpAddAssign | DeclFlags.IsCtor | DeclFlags.IsExtension | DeclFlags.IsLocalFunc)) == 0 &&
                          (td.Flags & DeclFlags.IsDisposable) != 0;
            return ((fd.Flags & DeclFlags.MutAttr) != 0 || isDisp) ? PassMode.ThisMut : PassMode.ThisShared;
        }
        if (td.Kind == TypeDeclKinds.Struct)
        {
            bool isRo = (fd.Flags & DeclFlags.Readonly) != 0 || (td.Flags & DeclFlags.Readonly) != 0;
            return isRo ? PassMode.ThisShared : PassMode.ThisMut;
        }
        return PassMode.ThisShared;
    }

    private static bool IsValidDisposableDisposeMethod(
        in FuncDecl mfd,
        ReadOnlySpan<int> tpNames,
        ReadOnlySpan<int> tpVals,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types)
    {
        if (mfd.NameId != bctx.IdDispose ||
            mfd.ParamCount != 0 ||
            mfd.TypeParamCount != 0 ||
            mfd.BodyNode < 0 ||
            mfd.RetTypeNode < 0 ||
            (mfd.Flags & DeclFlags.Public) == 0 ||
            (mfd.Flags & DeclFlags.Private) != 0 ||
            (mfd.Flags & DeclFlags.Static) != 0 ||
            (mfd.Flags & (DeclFlags.ReturnsByRef | DeclFlags.IsPropGet | DeclFlags.IsPropSet | DeclFlags.IsIndexer | DeclFlags.IsOpAddAssign | DeclFlags.IsCtor | DeclFlags.IsExtension | DeclFlags.IsLocalFunc)) != 0)
        {
            return false;
        }
        var discardDiags = new DiagnosticBag(0);
        int retTid = ResolveTypeNode(mfd.RetTypeNode, tpNames, tpVals, in ast, in ta, in names, ref bctx, ref types, ref discardDiags, reportLinearErrors: false);
        return discardDiags.Items.Count == 0 && retTid == TypeIds.Void;
    }

    private static int FindAndEnqueueEntryPoint(
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        ref BinderCtx bctx,
        ref TypeTable types,
        ref MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        var empty = new Vec<int>();

        // 1. Check synthesized top-level `<Main>$`
        for (int i = 0; i < ast.Funcs.Count; i++)
        {
            FuncDecl fd = ast.Funcs[i];
            if ((fd.Flags & DeclFlags.IsTopLevelMain) != 0)
            {
                return EnsureFuncInstance(i, -1, empty.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
            }
        }

        // 2. Check explicit parameterless int/void Main in App units. A Main
        // in a generic type, or in a type nested in one, is not a candidate.
        int invalidMainToken = -1;
        int entryFuncId = -1;
        for (int t = 0; t < ast.Types.Count; t++)
        {
            TypeDecl td = ast.Types[t];
            if (td.UnitKind != UnitKinds.App || IsInGenericTypeContext(t, in ast)) continue;
            for (int m = 0; m < td.FuncCount; m++)
            {
                int fId = td.FuncStart + m;
                FuncDecl fd = ast.Funcs[fId];
                if (fd.NameId == bctx.IdMain && (fd.Flags & DeclFlags.Static) != 0 && fd.TypeParamCount == 0)
                {
                    int returnType = ResolveTypeNode(fd.RetTypeNode, empty.AsReadOnlySpan(), empty.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref diags);
                    if (fd.ParamCount == 0 && (fd.Flags & DeclFlags.ReturnsByRef) == 0 &&
                        (returnType == TypeIds.Void || returnType == TypeIds.I32))
                    {
                        if (entryFuncId >= 0)
                        {
                            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[fd.TokIdx], "Program has more than one entry point defined (CS0017)."u8);
                            continue;
                        }
                        entryFuncId = fId;
                        continue;
                    }
                    invalidMainToken = fd.TokIdx;
                }
            }
        }

        if (entryFuncId >= 0)
            return EnsureFuncInstance(entryFuncId, -1, empty.AsReadOnlySpan(), in ast, in ta, in names, ref bctx, ref types, ref mir, ref diags);
        if (invalidMainToken >= 0)
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[invalidMainToken], "The entry point must be parameterless static void Main() or static int Main() (CS0028)."u8);
        return -1;
    }

    private static bool IsInGenericTypeContext(int typeDeclId, in AstArena ast)
    {
        for (int t = typeDeclId; t >= 0; t = ast.Types[t].OwnerDeclId)
            if (ast.Types[t].TypeParamCount > 0) return true;
        return false;
    }
}
