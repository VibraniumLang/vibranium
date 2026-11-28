using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Compiler.Core;

public enum MirPassKind
{
    ByValue,
    In,
    Ref,
    Out,
    ImplicitThisShared,
    ImplicitThisMut,
}

public enum MirBinaryOp
{
    Add,
    Sub,
    Mul,
    Div,
    Rem,
    BitAnd,
    BitOr,
    BitXor,
    Shl,
    Shr,
    UShr,
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
}

public enum MirUnaryOp
{
    Neg,
    Not,
    BitNot,
}

public enum MirIntrinsicKind
{
    IoWriteInt64,
    IoWriteUInt64,
    IoWriteBool,
    IoWriteStr,
    IoWriteLineInt64,
    IoWriteLineUInt64,
    IoWriteLineBool,
    IoWriteLineStr,
    IoWriteLineEmpty,
    StdioWriteOut,
    StdioWriteErr,
    Panic,
    OptionAsRef,
    OptionAsMut,
    OptionTake,
    OptionReplace,
    StringAsUtf8,
    SpanToUtf8View,
    Utf8ViewToSpan,
    Utf8ScanRaw,
    OptionUnwrap,
    OptionExpect,
    OptionUnwrapOr,
    OptionUnwrapMut,
    OptionIsSome,
    OptionMutSet,
    OptionMutTake,
    OptionMutReplace,
    OptionMutAsRef,
    ResultUnwrap,
    ResultExpect,
    ResultUnwrapOr,
    ResultIsOk,
    VecNewWithCapacity,
    VecFromLiteral,
    VecCount,
    VecCapacity,
    VecPush,
    VecPop,
    VecClear,
    VecTruncate,
    VecReserve,
    VecExtend,
    VecResize,
    VecIndexRef,
    VecSliceMut,
    VecAsSpan,
    VecAsReadOnlySpan,
    SpanLength,
    SpanIndexRef,
    SpanSlice,
    SpanToReadOnly,
}

public abstract record MirType
{
    public sealed record VoidType : MirType;
    public sealed record BoolType : MirType;
    public sealed record Int8Type : MirType;
    public sealed record UInt8Type : MirType;
    public sealed record Int16Type : MirType;
    public sealed record UInt16Type : MirType;
    public sealed record CharType : MirType;
    public sealed record Int32Type : MirType;
    public sealed record UInt32Type : MirType;
    public sealed record Int64Type : MirType;
    public sealed record UInt64Type : MirType;
    public sealed record NIntType : MirType;
    public sealed record NUIntType : MirType;
    public sealed record Float32Type : MirType;
    public sealed record Float64Type : MirType;
    public sealed record StringLiteralType : MirType;
    public sealed record NoneUnitType : MirType;
    public sealed record EnumType(string Name) : MirType;
    public sealed record ClassType(string Name, bool IsDisposable) : MirType;
    public sealed record StructType(string Name, bool IsRefStruct = false) : MirType;
    public sealed record OptionType(MirType Inner) : MirType;
    public sealed record OptionRefType(MirType Inner) : MirType;
    public sealed record OptionMutType(MirType Inner) : MirType;
    public sealed record SomeWrapperType(MirType Inner) : MirType;
    public sealed record ResultType(MirType Ok, MirType Err) : MirType;
    public sealed record OkWrapperType(MirType Ok) : MirType;
    public sealed record ErrWrapperType(MirType Err) : MirType;
    public sealed record VecType(MirType Element) : MirType;
    public sealed record SpanType(MirType Element, bool IsReadOnly) : MirType;
    public sealed record Utf8ScanViewType(bool IsArgEnumerator) : MirType;
    public sealed record RefType(MirType Target, bool IsMutable) : MirType;

    public static readonly MirType Void = new VoidType();
    public static readonly MirType Bool = new BoolType();
    public static readonly MirType Int8 = new Int8Type();
    public static readonly MirType UInt8 = new UInt8Type();
    public static readonly MirType Int16 = new Int16Type();
    public static readonly MirType UInt16 = new UInt16Type();
    public static readonly MirType Char = new CharType();
    public static readonly MirType Int32 = new Int32Type();
    public static readonly MirType UInt32 = new UInt32Type();
    public static readonly MirType Int64 = new Int64Type();
    public static readonly MirType UInt64 = new UInt64Type();
    public static readonly MirType NInt = new NIntType();
    public static readonly MirType NUInt = new NUIntType();
    public static readonly MirType Float32 = new Float32Type();
    public static readonly MirType Float64 = new Float64Type();
    public static readonly MirType StringLiteral = new StringLiteralType();
    public static readonly MirType NoneUnit = new NoneUnitType();
    public static readonly MirType Utf8Lines = new Utf8ScanViewType(false);
    public static readonly MirType Utf8ArgEnumerator = new Utf8ScanViewType(true);

    public static bool IsInteger(MirType type) =>
        type is Int8Type or UInt8Type or Int16Type or UInt16Type or CharType or
                Int32Type or UInt32Type or Int64Type or UInt64Type or NIntType or NUIntType or EnumType;

    public static bool IsSignedInteger(MirType type) =>
        type is Int8Type or Int16Type or Int32Type or Int64Type or NIntType or EnumType;

    public static bool IsUnsignedInteger(MirType type) =>
        type is UInt8Type or UInt16Type or CharType or UInt32Type or UInt64Type or NUIntType;

    public static int GetIntegerBitWidth(MirType type) =>
        type switch
        {
            Int8Type or UInt8Type => 8,
            Int16Type or UInt16Type or CharType => 16,
            Int32Type or UInt32Type or EnumType => 32,
            Int64Type or UInt64Type or NIntType or NUIntType => 64,
            _ => 0,
        };
}

public sealed record MirFieldDef(string Name, MirType Type, bool IsReadOnly);

public sealed record MirClassDef(
    string Name,
    bool IsDisposable,
    string? DisposeMethodKey,
    IReadOnlyList<MirFieldDef> Fields);

public sealed record MirStructDef(
    string Name,
    bool IsReadOnly,
    bool IsRefStruct,
    bool IsDisposable,
    IReadOnlyList<MirFieldDef> Fields);

public abstract record MirProjection
{
    public sealed record Deref(MirType ResultType) : MirProjection;
    public sealed record Field(string FieldName, int FieldIndex, MirType FieldType, bool ThroughClassHeap, bool IsReadOnlyField) : MirProjection;
    public sealed record Index(MirOperand IndexOperand, MirType ElementType) : MirProjection;
}

public sealed record MirPlace(int LocalId, IReadOnlyList<MirProjection> Projections)
{
    public static MirPlace Local(int localId) => new(localId, Array.Empty<MirProjection>());
}

public abstract record MirConstant
{
    public sealed record BoolConst(bool Value) : MirConstant;
    public sealed record Int32Const(int Value) : MirConstant;
    public sealed record Int64Const(long Value) : MirConstant;
    public sealed record Float32Const(float Value) : MirConstant;
    public sealed record Float64Const(double Value) : MirConstant;
    public sealed record StringConst(string Value) : MirConstant;
    public sealed record Utf8BytesConst(byte[] Bytes) : MirConstant;
    public sealed record ZeroDefault(MirType Type) : MirConstant;
}

public abstract record MirOperand
{
    public sealed record CopyOrMove(MirPlace Place, MirType Type, VibraniumSpan Span) : MirOperand
    {
        // A move implementing this switch's own pattern, not a move performed by a guard.
        public int? SelectingSwitchMatchBorrowId { get; init; }
        // D1-W, analysis only. A call that reads its receiver after its arguments moves it out before them:
        // that move holds the moved local (HoldsMovedPlace) until the call's own read of the moved value,
        // which names the local to release (ReleasesMovedPlace).
        public bool HoldsMovedPlace { get; init; }
        public int? ReleasesMovedPlace { get; init; }
    }
    public sealed record Const(MirConstant Value, MirType Type, VibraniumSpan Span) : MirOperand;
}

public sealed record MirCallArg(
    MirPassKind PassKind,
    MirOperand? ValueOperand,
    MirPlace? BorrowPlace,
    VibraniumSpan Span)
{
    // N90, analysis only. The receiver or `in` argument copies a Core wrapper's payload that C# reads in place at the
    // call. Hold: the Copy wrapper's analysis-only borrow (MirLocal.IsCallHold), which the call reads.
    // ReleasesMovedPlace: the !Copy wrapper that the copy moved out under a D1-W hold, which the call ends.
    public int? Hold { get; init; }
    public int? ReleasesMovedPlace { get; init; }
}

public abstract record MirRvalue
{
    public sealed record Use(MirOperand Operand, bool IsCallOperandReborrow = false) : MirRvalue;
    public sealed record Binary(MirBinaryOp Op, MirOperand Left, MirOperand Right, MirType ResultType, bool IsChecked) : MirRvalue;
    public sealed record Unary(MirUnaryOp Op, MirOperand Operand, MirType ResultType, bool IsChecked) : MirRvalue;
    public sealed record Cast(MirOperand Operand, MirType TargetType, bool IsChecked) : MirRvalue;
    public sealed record AllocClass(string ClassName, IReadOnlyList<MirOperand> FieldValues) : MirRvalue;
    public sealed record ConstructStruct(string StructName, IReadOnlyList<MirOperand> FieldValues) : MirRvalue;
    public sealed record MakeSomeWrapper(MirType InnerType, MirOperand Value) : MirRvalue;
    public sealed record MakeOkWrapper(MirType OkType, MirOperand Value) : MirRvalue;
    public sealed record MakeErrWrapper(MirType ErrType, MirOperand Error) : MirRvalue;
    public sealed record LiftOptionSome(MirType InnerType, MirOperand Value) : MirRvalue;
    public sealed record LiftOptionNone(MirType InnerType) : MirRvalue;
    public sealed record LiftResultOk(MirType OkType, MirType ErrType, MirOperand Value) : MirRvalue;
    public sealed record LiftResultErr(MirType OkType, MirType ErrType, MirOperand Error) : MirRvalue;
    public sealed record UnwrapSomeWrapper(MirType InnerType, MirOperand Wrapper) : MirRvalue;
    public sealed record UnwrapOkWrapper(MirType OkType, MirOperand Wrapper) : MirRvalue;
    public sealed record UnwrapErrWrapper(MirType ErrType, MirOperand Wrapper) : MirRvalue;
    public sealed record StackSpan(MirType ElementType, bool IsReadOnly, IReadOnlyList<MirOperand> Elements, int ZeroInitLength) : MirRvalue;
}

public abstract record MirStatement(VibraniumSpan Span)
{
    // Set on the final producer (BindRef, Call or Intrinsic) of a source ref re-point.
    // The source RHS names the destination, even if this statement's operands no longer do after CFG lowering.
    public bool RefRepointNamesDestination { get; init; }

    public sealed record Assign(MirPlace Target, MirRvalue Rvalue, VibraniumSpan Span) : MirStatement(Span);
    // Assignment-target snapshots retain the evaluated pointer and inherited referent provenance,
    // but track the source carrier only as ancestry, not as borrowed rebindable reference storage.
    public sealed record BindRef(int RefLocalId, bool IsMutable, MirPlace Target, VibraniumSpan Span, bool IsTwoPhaseBorrow = false, bool IsAssignmentTargetSnapshot = false, bool IsOut = false) : MirStatement(Span)
    {
        // For an analysis-only switch-match borrow: ref slots named by the selector's access path.
        public IReadOnlyList<int>? SwitchMatchRefSlots { get; init; }
    }
    public sealed record Call(MirPlace? Dest, int? RefDestLocalId, bool RefDestIsMutable, string CalleeKey, IReadOnlyList<MirCallArg> Args, VibraniumSpan Span) : MirStatement(Span);
    public sealed record Intrinsic(MirIntrinsicKind Kind, MirPlace? Dest, int? RefDestLocalId, bool RefDestIsMutable, IReadOnlyList<MirOperand> ValueArgs, IReadOnlyList<MirPlace> PlaceArgs, MirType ResultType, VibraniumSpan Span, bool IsTwoPhaseBorrow = false) : MirStatement(Span)
    {
        // N90, analysis only (OptionAsRef on a wrapper payload): Hold / ReleasesMovedPlace as on MirCallArg.
        public int? Hold { get; init; }
        public int? ReleasesMovedPlace { get; init; }
    }
    public sealed record DisposeResource(int LocalId, string DisposeMethodKey, VibraniumSpan Span) : MirStatement(Span);
    public sealed record ScopeExit(IReadOnlyList<int> ExitingLocals, VibraniumSpan Span) : MirStatement(Span);
    // Neither of these accesses a runtime slot. Remember captures loan provenance before
    // an enclosing ref re-point's RHS can itself overwrite the same ref local.
    public sealed record RememberRefRepointLoan(int SnapshotLocalId, int SourceRefLocalId, VibraniumSpan Span) : MirStatement(Span);
    public sealed record KeepAnalysisBorrow(int CarrierLocalId, VibraniumSpan Span) : MirStatement(Span);
}

public abstract record MirMatchScrutinee
{
    public sealed record PlaceScrutinee(MirPlace Place, MirType Type, bool IsMutableCursor) : MirMatchScrutinee
    {
        // An unguarded cursor binding selects the arm. This loan may still be live along the failed tag test.
        public int? SelectingSwitchMatchBorrowId { get; init; }
    }
    public sealed record RvalueScrutinee(MirOperand Operand, bool IsViewCursor) : MirMatchScrutinee;
}

public abstract record MirTerminator(VibraniumSpan Span)
{
    public sealed record Goto(int TargetBlock, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record Branch(MirOperand Condition, int TrueBlock, int FalseBlock, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record MatchOption(MirMatchScrutinee Scrutinee, int SomeBindingLocalId, int SomeBlock, int NoneBlock, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record MatchResult(MirMatchScrutinee Scrutinee, int OkBindingLocalId, int OkBlock, int ErrBindingLocalId, int ErrBlock, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record GuardResultIsErr(MirOperand ConsumedResult, int OkLocalId, int ErrWrapperLocalId, int ErrBlock, int OkBlock, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record GuardOptionIsNone(MirOperand ConsumedOption, int SomeValueLocalId, int NoneBlock, int SomeBlock, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record Return(MirOperand? Value, MirPlace? RefReturnPlace, VibraniumSpan Span) : MirTerminator(Span);
    public sealed record Unreachable(VibraniumSpan Span) : MirTerminator(Span);
}

public sealed record MirLocal(
    int Id,
    string Name,
    MirType Type,
    bool IsParam,
    MirPassKind PassKind,
    bool IsScoped,
    bool IsUsingVar,
    bool IsRefLocal,
    bool IsRefMutable,
    bool IsPatternBinding,
    bool IsCompilerTemp,
    VibraniumSpan Span,
    bool IsForeachLoan = false,
    bool IsSwitchMatchBorrow = false,
    int? RefRepointSnapshotOf = null,
    bool IsCallHold = false)
{
    // IsCallHold: no slot; it keeps a call argument's place borrowed in the checker from where C# evaluates the argument
    // (a wrapper whose payload is read in place, N90; an `out` target before a branching later argument, N116).
    public bool IsAnalysisOnlyBorrow => IsSwitchMatchBorrow || RefRepointSnapshotOf.HasValue || IsCallHold;
}

public sealed record MirBasicBlock(
    int Id,
    IReadOnlyList<MirStatement> Statements,
    MirTerminator Terminator);

public sealed record MirFunctionSignature(
    string Key,
    string DisplayName,
    IReadOnlyList<MirParamSig> Params,
    MirType ReturnType,
    bool ReturnsByRef,
    bool ReturnRefIsMutable,
    string? NativeSymbol = null,
    bool IsInternalHelper = false);

public sealed record MirParamSig(
    string Name,
    MirType Type,
    MirPassKind PassKind,
    bool IsScoped,
    bool IsUnscopedRefReceiver);

public sealed record MirFunction(
    MirFunctionSignature Signature,
    bool IsEntryPoint,
    IReadOnlyList<MirLocal> Locals,
    IReadOnlyList<MirBasicBlock> Blocks,
    VibraniumSpan Span);

public sealed class MirModule
{
    public Dictionary<string, MirClassDef> Classes { get; } = new();
    public Dictionary<string, MirStructDef> Structs { get; } = new();
    public Dictionary<string, MirFunctionSignature> Signatures { get; } = new();
    public List<MirFunction> Functions { get; } = new();
    public string? EntryPointKey { get; set; }
    public bool UsesStdNative { get; set; }

    public bool IsCopy(MirType type) => IsCopyCore(type, new HashSet<string>());

    private bool IsCopyCore(MirType type, HashSet<string> visited)
    {
        switch (type)
        {
            case MirType.VoidType:
            case MirType.BoolType:
            case MirType.Int8Type:
            case MirType.UInt8Type:
            case MirType.Int16Type:
            case MirType.UInt16Type:
            case MirType.CharType:
            case MirType.Int32Type:
            case MirType.UInt32Type:
            case MirType.Int64Type:
            case MirType.UInt64Type:
            case MirType.NIntType:
            case MirType.NUIntType:
            case MirType.Float32Type:
            case MirType.Float64Type:
            case MirType.StringLiteralType:
            case MirType.NoneUnitType:
            case MirType.EnumType:
            case MirType.OptionRefType:
            case MirType.Utf8ScanViewType:
                return true;
            case MirType.SpanType span:
                return span.IsReadOnly;
            case MirType.RefType r:
                return !r.IsMutable;
            case MirType.ClassType:
            case MirType.VecType:
            case MirType.OptionMutType:
                return false;
            case MirType.OptionType opt:
                return IsCopyCore(opt.Inner, visited);
            case MirType.SomeWrapperType s:
                return IsCopyCore(s.Inner, visited);
            case MirType.ResultType res:
                return IsCopyCore(res.Ok, visited) && IsCopyCore(res.Err, visited);
            case MirType.OkWrapperType ok:
                return IsCopyCore(ok.Ok, visited);
            case MirType.ErrWrapperType err:
                return IsCopyCore(err.Err, visited);
            case MirType.StructType st:
                if (!Structs.TryGetValue(st.Name, out var def))
                {
                    return !st.IsRefStruct;
                }
                if (def.IsDisposable || !visited.Add(st.Name))
                {
                    return false;
                }
                try
                {
                    return def.Fields.All(f => IsCopyCore(f.Type, visited));
                }
                finally
                {
                    visited.Remove(st.Name);
                }
            default:
                return false;
        }
    }

    public bool IsZeroSafe(MirType type) => IsZeroSafeCore(type, new HashSet<string>());

    private bool IsZeroSafeCore(MirType type, HashSet<string> visited)
    {
        switch (type)
        {
            case MirType.VoidType:
            case MirType.BoolType:
            case MirType.Int8Type:
            case MirType.UInt8Type:
            case MirType.Int16Type:
            case MirType.UInt16Type:
            case MirType.CharType:
            case MirType.Int32Type:
            case MirType.UInt32Type:
            case MirType.Int64Type:
            case MirType.UInt64Type:
            case MirType.NIntType:
            case MirType.NUIntType:
            case MirType.Float32Type:
            case MirType.Float64Type:
            case MirType.NoneUnitType:
            case MirType.EnumType:
            case MirType.OptionType:
            case MirType.SpanType:
                return true;
            case MirType.Utf8ScanViewType:
            case MirType.ClassType:
            case MirType.ResultType:
            case MirType.SomeWrapperType:
            case MirType.OkWrapperType:
            case MirType.ErrWrapperType:
            case MirType.OptionRefType:
            case MirType.OptionMutType:
            case MirType.VecType:
            case MirType.RefType:
                return false;
            case MirType.StructType st:
                if (st.IsRefStruct || !Structs.TryGetValue(st.Name, out var def))
                {
                    return false;
                }
                if (def.IsRefStruct || def.IsDisposable || !visited.Add(st.Name))
                {
                    return false;
                }
                try
                {
                    return def.Fields.All(f => IsZeroSafeCore(f.Type, visited));
                }
                finally
                {
                    visited.Remove(st.Name);
                }
            default:
                return false;
        }
    }

    public bool IsLinearResource(MirType type) => IsLinearCore(type, new HashSet<string>());

    private bool IsLinearCore(MirType type, HashSet<string> visited)
    {
        switch (type)
        {
            case MirType.ClassType cls:
                return cls.IsDisposable || (Classes.TryGetValue(cls.Name, out var cdef) && cdef.IsDisposable);
            case MirType.OptionType opt:
                return IsLinearCore(opt.Inner, visited);
            case MirType.SomeWrapperType s:
                return IsLinearCore(s.Inner, visited);
            case MirType.ResultType res:
                return IsLinearCore(res.Ok, visited) || IsLinearCore(res.Err, visited);
            case MirType.OkWrapperType ok:
                return IsLinearCore(ok.Ok, visited);
            case MirType.ErrWrapperType err:
                return IsLinearCore(err.Err, visited);
            case MirType.StructType st:
                if (!Structs.TryGetValue(st.Name, out var def))
                {
                    return false;
                }
                if (def.IsDisposable)
                {
                    return true;
                }
                if (!visited.Add(st.Name))
                {
                    return false;
                }
                try
                {
                    return def.Fields.Any(f => IsLinearCore(f.Type, visited));
                }
                finally
                {
                    visited.Remove(st.Name);
                }
            default:
                return false;
        }
    }

    public bool IsViewLike(MirType type) =>
        type is MirType.OptionRefType or MirType.OptionMutType or MirType.SpanType or MirType.Utf8ScanViewType or MirType.RefType;

    public bool IsExclusiveView(MirType type) =>
        type is MirType.OptionMutType or MirType.SpanType { IsReadOnly: false } or MirType.RefType { IsMutable: true };

    public bool NeedsMemoryDrop(MirType type) =>
        !IsCopy(type) && !IsLinearResource(type) && !IsViewLike(type);
}
