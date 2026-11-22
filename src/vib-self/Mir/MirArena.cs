using System.Diagnostics.CodeAnalysis;

namespace Vibranium.Self;

public enum MirBinOp
{
    Add = 0,
    Sub = 1,
    Mul = 2,
    Div = 3,
    Rem = 4,
    BitAnd = 5,
    BitOr = 6,
    BitXor = 7,
    Shl = 8,
    Shr = 9,
    UShr = 10,
    Eq = 11,
    Ne = 12,
    Lt = 13,
    Le = 14,
    Gt = 15,
    Ge = 16,
}

public enum MirUnOp
{
    Neg = 0,
    Not = 1,
    BitNot = 2,
}

public enum MirIntrinsic
{
    IoWriteInt64 = 0,
    IoWriteUInt64 = 1,
    IoWriteBool = 2,
    IoWriteStr = 3,
    IoWriteLineInt64 = 4,
    IoWriteLineUInt64 = 5,
    IoWriteLineBool = 6,
    IoWriteLineStr = 7,
    IoWriteLineEmpty = 8,
    StdioWriteOut = 9,
    StdioWriteErr = 10,
    Panic = 11,
    OptionAsRef = 12,
    OptionAsMut = 13,
    OptionTake = 14,
    OptionReplace = 15,
    OptionUnwrap = 16,
    OptionUnwrapOr = 17,
    OptionUnwrapMut = 18,
    OptionIsSome = 19,
    OptionMutSet = 20,
    OptionMutTake = 21,
    OptionMutReplace = 22,
    OptionMutAsRef = 23,
    ResultUnwrap = 24,
    ResultUnwrapOr = 25,
    ResultIsOk = 26,
    VecNewWithCapacity = 27,
    VecFromLiteral = 28,
    VecCount = 29,
    VecCapacity = 30,
    VecPush = 31,
    VecPop = 32,
    VecClear = 33,
    VecTruncate = 34,
    VecReserve = 35,
    VecExtend = 36,
    VecResize = 37,
    VecIndexRef = 38,
    VecSliceMut = 39,
    VecReadOnlySlice = 40,
    VecAsSpan = 41,
    VecAsReadOnlySpan = 42,
    SpanLength = 43,
    SpanIndexRef = 44,
    SpanSlice = 45,
    SpanToReadOnly = 46,
    StringAsUtf8 = 47,
    Utf8ViewFromSpan = 48,
    Utf8ViewBytes = 49,
    Utf8Scan = 50,
    OptionExpect = 51,
    ResultExpect = 52,
}

public static class MirBorrowProjections
{
    // These intrinsics create references/views; none invokes user code or mutates
    // the referenced storage. Their preparation belongs to the consuming operand.
    public static bool FromPlace(MirIntrinsic intrinsic) =>
        intrinsic == MirIntrinsic.OptionAsRef || intrinsic == MirIntrinsic.OptionAsMut ||
        intrinsic == MirIntrinsic.OptionUnwrapMut || intrinsic == MirIntrinsic.OptionMutAsRef ||
        intrinsic == MirIntrinsic.VecIndexRef || intrinsic == MirIntrinsic.VecSliceMut ||
        intrinsic == MirIntrinsic.VecReadOnlySlice || intrinsic == MirIntrinsic.VecAsSpan ||
        intrinsic == MirIntrinsic.VecAsReadOnlySpan ||
        intrinsic == MirIntrinsic.Utf8ViewFromSpan || intrinsic == MirIntrinsic.Utf8ViewBytes;

    public static bool FromValue(MirIntrinsic intrinsic) =>
        intrinsic == MirIntrinsic.SpanIndexRef || intrinsic == MirIntrinsic.SpanSlice ||
        intrinsic == MirIntrinsic.SpanToReadOnly;
}

public static class MirLocalFlags
{
    public const int IsParam = 1 << 0;
    public const int IsScoped = 1 << 1;
    public const int IsUsingVar = 1 << 2;
    public const int IsRefLocal = 1 << 3;
    public const int IsRefMut = 1 << 4;
    public const int IsPatternBind = 1 << 5;
    public const int IsTemp = 1 << 6;
    public const int IsUnscopedRefRecv = 1 << 7;
    public const int IsCtorFieldSlot = 1 << 8;
    public const int IsBorrowedBind = 1 << 9;
    public const int IsSharedBind = 1 << 10;
    public const int IsForeachRef = 1 << 11;
    // The hidden enumeration view retains its loan until lexical ScopeExit.
    public const int IsForeachLoan = 1 << 12;
    // A source operand loan retained until its invocation or assignment activates.
    public const int IsCallOperandLoan = 1 << 13;
}

public readonly struct MirLocal
{
    public readonly int NameId;
    public readonly int TypeId;
    public readonly PassMode Mode;
    public readonly int Flags;
    public readonly int TokIdx;
    // Function-local staged instruction identity of the terminal invocation/store.
    public readonly int CallActivationId;

    public MirLocal(int nameId, int typeId, PassMode mode, int flags, int tokIdx)
    {
        NameId = nameId;
        TypeId = typeId;
        Mode = mode;
        Flags = flags;
        TokIdx = tokIdx;
        CallActivationId = -1;
    }

    public MirLocal(int nameId, int typeId, PassMode mode, int flags, int tokIdx, int callActivationId)
    {
        NameId = nameId;
        TypeId = typeId;
        Mode = mode;
        Flags = flags;
        TokIdx = tokIdx;
        CallActivationId = callActivationId;
    }
}

public static class MirProjKinds
{
    public const int Deref = 1;
    public const int Field = 2;
    public const int Index = 3;
}

public readonly struct MirProj
{
    public readonly int Kind;
    public readonly int FieldIdx;
    public readonly int ResultTypeId;
    public readonly int IndexOpId;
    public readonly int Flags; // 1 = ThroughClassHeap, 2 = IsReadOnlyField

    public MirProj(int kind, int fieldIdx, int resultTypeId, int indexOpId, int flags)
    {
        Kind = kind;
        FieldIdx = fieldIdx;
        ResultTypeId = resultTypeId;
        IndexOpId = indexOpId;
        Flags = flags;
    }
}

public readonly struct MirPlace
{
    public readonly int LocalId;
    public readonly int ProjStart;
    public readonly int ProjCount;
    public readonly int TypeId;

    public MirPlace(int localId, int projStart, int projCount, int typeId)
    {
        LocalId = localId;
        ProjStart = projStart;
        ProjCount = projCount;
        TypeId = typeId;
    }
}

public static class MirOpKinds
{
    public const int CopyOrMove = 1;
    public const int BoolConst = 2;
    public const int IntConst = 3;
    public const int FloatConst = 4;
    public const int StrConst = 5;
    public const int Utf8Const = 6;
    public const int ZeroDefault = 7;
}

public readonly struct MirOperand
{
    public readonly int Kind;
    public readonly int PlaceOrLitIdx;
    public readonly long IntVal;
    public readonly int TypeId;
    public readonly int TokIdx;
    public readonly int SourceStartTokIdx;

    public MirOperand(int kind, int placeOrLitIdx, long intVal, int typeId, int tokIdx)
    {
        Kind = kind;
        PlaceOrLitIdx = placeOrLitIdx;
        IntVal = intVal;
        TypeId = typeId;
        TokIdx = tokIdx;
        SourceStartTokIdx = tokIdx;
    }

    public MirOperand(int kind, int placeOrLitIdx, long intVal, int typeId, int tokIdx, int sourceStartTokIdx)
    {
        Kind = kind;
        PlaceOrLitIdx = placeOrLitIdx;
        IntVal = intVal;
        TypeId = typeId;
        TokIdx = tokIdx;
        SourceStartTokIdx = sourceStartTokIdx >= 0 ? sourceStartTokIdx : tokIdx;
    }
}

public readonly struct MirCallArg
{
    public readonly PassMode Mode;
    public readonly int OperandOrPlaceId;
    public readonly int TokIdx;

    public MirCallArg(PassMode mode, int operandOrPlaceId, int tokIdx)
    {
        Mode = mode;
        OperandOrPlaceId = operandOrPlaceId;
        TokIdx = tokIdx;
    }
}

public enum MirOp
{
    AssignUse = 1,
    AssignBin = 2,
    AssignUn = 3,
    AssignCast = 4,
    AllocClass = 5,
    ConstructStruct = 6,
    MakeSomeWrap = 7,
    MakeOkWrap = 8,
    MakeErrWrap = 9,
    LiftOptionSome = 10,
    LiftOptionNone = 11,
    LiftResultOk = 12,
    LiftResultErr = 13,
    UnwrapWrap = 14,
    StackSpan = 15,
    BindRef = 16,
    Call = 17,
    Intrinsic = 18,
    DisposeResource = 19,
    ScopeExit = 20,
}

public readonly struct MirInst
{
    public readonly MirOp Op;
    public readonly int DestPlaceId;
    public readonly int RefDestLocalId;
    public readonly int Flags; // 1 = RefDestIsMut / BindRefIsMut, 2 = IsTwoPhaseBorrow, 4 = BindRef assignment-target snapshot, 8 = AssignUse non-consuming Span argument reborrow
    public readonly int TypeId;
    public readonly int A;
    public readonly int B;
    public readonly int C;
    public readonly int D;
    public readonly int TokIdx;

    public MirInst(
        MirOp op,
        int destPlaceId,
        int refDestLocalId,
        int flags,
        int typeId,
        int a,
        int b,
        int c,
        int d,
        int tokIdx)
    {
        Op = op;
        DestPlaceId = destPlaceId;
        RefDestLocalId = refDestLocalId;
        Flags = flags;
        TypeId = typeId;
        A = a;
        B = b;
        C = c;
        D = d;
        TokIdx = tokIdx;
    }
}

public enum MirTermKind
{
    Unset = 0,
    Goto = 1,
    Branch = 2,
    MatchOption = 3,
    MatchResult = 4,
    GuardResultIsErr = 5,
    GuardOptionIsNone = 6,
    Return = 7,
    Unreachable = 8,
}

public readonly struct MirTerm
{
    public readonly MirTermKind Kind;
    public readonly int A;
    public readonly int B;
    public readonly int C;
    public readonly int D;
    public readonly int E;
    public readonly int Flags; // 1 = PlaceScrutinee (0 = RvalueOperand), 2 = IsMutableCursor, 4 = IsViewCursor
    public readonly int TokIdx;

    public MirTerm(MirTermKind kind, int a, int b, int c, int d, int e, int flags, int tokIdx)
    {
        Kind = kind;
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        Flags = flags;
        TokIdx = tokIdx;
    }
}

public struct MirBlock
{
    public int InstStart;
    public int InstCount;
    public MirTerm Term;

    public MirBlock(int instStart, int instCount, MirTerm term)
    {
        InstStart = instStart;
        InstCount = instCount;
        Term = term;
    }
}

public readonly struct MirParamSig
{
    public readonly int NameId;
    public readonly int TypeId;
    public readonly PassMode Mode;
    public readonly int Flags; // 1 = IsScoped, 2 = IsUnscopedRefRecv

    public MirParamSig(int nameId, int typeId, PassMode mode, int flags)
    {
        NameId = nameId;
        TypeId = typeId;
        Mode = mode;
        Flags = flags;
    }
}

public struct FuncInstance
{
    public int FuncDeclId;
    public int OwnerTypeId; // -1 if global/top-level/local
    public int MethodTypeArgStart;
    public int MethodTypeArgCount;
    public int ParamSigStart;
    public int ParamSigCount;
    public int ReturnTypeId;
    public int Flags; // DeclFlags: ReturnsByRef, ReturnRefMut, IsInternalHelper, IsTopLevelMain
    public int NativeSymLitIdx; // >= 0 if [Native("__vib_sys_*")]
    public int MirFuncIdx; // >= 0 once lowered

    public FuncInstance(
        int funcDeclId,
        int ownerTypeId,
        int methodTypeArgStart,
        int methodTypeArgCount,
        int paramSigStart,
        int paramSigCount,
        int returnTypeId,
        int flags,
        int nativeSymLitIdx)
    {
        FuncDeclId = funcDeclId;
        OwnerTypeId = ownerTypeId;
        MethodTypeArgStart = methodTypeArgStart;
        MethodTypeArgCount = methodTypeArgCount;
        ParamSigStart = paramSigStart;
        ParamSigCount = paramSigCount;
        ReturnTypeId = returnTypeId;
        Flags = flags;
        NativeSymLitIdx = nativeSymLitIdx;
        MirFuncIdx = -1;
    }
}

public struct MirFunc
{
    public int FuncInstId;
    public int LocalStart;
    public int LocalCount;
    public int BlockStart;
    public int BlockCount;
    public int StackSpanCount;
    public int TokIdx;

    public MirFunc(int funcInstId, int localStart, int localCount, int blockStart, int blockCount, int stackSpanCount, int tokIdx)
    {
        FuncInstId = funcInstId;
        LocalStart = localStart;
        LocalCount = localCount;
        BlockStart = blockStart;
        BlockCount = blockCount;
        StackSpanCount = stackSpanCount;
        TokIdx = tokIdx;
    }
}

public struct MirModuleArena
{
    public Vec<MirParamSig> ParamSigs;
    public Vec<int> MethodTypeArgs;
    public Vec<FuncInstance> FuncInstances;
    private StrMap<int> _instByKey;

    public Vec<MirLocal> Locals;
    public Vec<MirProj> Projs;
    public Vec<MirPlace> Places;
    public Vec<MirOperand> Operands;
    public Vec<MirCallArg> CallArgs;
    public Vec<int> ExtraLists;
    public Vec<MirInst> Insts;
    public Vec<MirBlock> Blocks;
    public Vec<MirFunc> Funcs;

    public int EntryFuncInstId;
    public bool UsesStdNative;

    public MirModuleArena(int capacity)
    {
        ParamSigs = new Vec<MirParamSig>(capacity);
        MethodTypeArgs = new Vec<int>(64);
        FuncInstances = new Vec<FuncInstance>(capacity);
        _instByKey = new StrMap<int>(capacity);

        Locals = new Vec<MirLocal>(capacity * 4);
        Projs = new Vec<MirProj>(capacity * 2);
        Places = new Vec<MirPlace>(capacity * 4);
        Operands = new Vec<MirOperand>(capacity * 8);
        CallArgs = new Vec<MirCallArg>(capacity * 4);
        ExtraLists = new Vec<int>(capacity * 2);
        Insts = new Vec<MirInst>(capacity * 8);
        Blocks = new Vec<MirBlock>(capacity * 2);
        Funcs = new Vec<MirFunc>(capacity);

        EntryFuncInstId = -1;
        UsesStdNative = false;
    }

    public readonly int FindFuncInstance(ReadOnlySpan<byte> key) => _instByKey.Find(key);

    [UnscopedRef]
    public readonly ReadOnlySpan<byte> GetFuncKey(int funcInstId) => _instByKey.KeyAt(funcInstId);

    public int RegisterFuncInstance(ReadOnlySpan<byte> key, FuncInstance inst)
    {
        int existing = _instByKey.Find(key);
        if (existing >= 0)
        {
            return existing;
        }
        int id = FuncInstances.Count;
        FuncInstances.Push(inst);
        _instByKey.Set(key, id);
        return id;
    }

    public int AddPlaceLocal(int localId, int typeId)
    {
        int id = Places.Count;
        Places.Push(new MirPlace(localId, 0, 0, typeId));
        return id;
    }

    public int AddPlace(int localId, ReadOnlySpan<MirProj> projs, int resultTypeId)
    {
        int pStart = Projs.Count;
        Projs.Extend(projs);
        int id = Places.Count;
        Places.Push(new MirPlace(localId, pStart, projs.Length, resultTypeId));
        return id;
    }

    public int AddOperand(MirOperand op)
    {
        int id = Operands.Count;
        Operands.Push(op);
        return id;
    }

    public int PushIdList(ReadOnlySpan<int> ids)
    {
        int s = ExtraLists.Count;
        ExtraLists.Extend(ids);
        return s;
    }
}
