namespace Vibranium.Self;

public static class LoanKinds
{
    public const int Place = 1;
    public const int StackSpan = 2;
    public const int StepCursor = 3;
    public const int SnapshotOrigin = 4;
}

public readonly struct Loan
{
    public readonly int Kind;
    public readonly int RootLocalId;
    public readonly int Field0;
    public readonly int Field1;
    public readonly int IsMut;
    public readonly int IsReserved;

    public int IsActiveMut => (IsMut != 0 && IsReserved == 0) ? 1 : 0;

    public Loan(int kind, int rootLocalId, int field0, int field1, int isMut)
    {
        Kind = kind;
        RootLocalId = rootLocalId;
        Field0 = field0;
        Field1 = field1;
        IsMut = isMut;
        IsReserved = 0;
    }

    public Loan(int kind, int rootLocalId, int field0, int field1, int isMut, int isReserved)
    {
        Kind = kind;
        RootLocalId = rootLocalId;
        Field0 = field0;
        Field1 = field1;
        IsMut = isMut;
        IsReserved = isMut != 0 ? isReserved : 0;
    }

    public Loan AsActivated() =>
        IsReserved != 0 ? new Loan(Kind, RootLocalId, Field0, Field1, IsMut, 0) : this;

    public bool EqualsLoan(Loan other) =>
        Kind == other.Kind && RootLocalId == other.RootLocalId &&
        Field0 == other.Field0 && Field1 == other.Field1 &&
        IsMut == other.IsMut && IsReserved == other.IsReserved;
}

public readonly struct ArgBorrowSummary
{
    public readonly int IsMut;
    public readonly int LoanStart;
    public readonly int LoanCount;
    public readonly int TokIdx;

    public ArgBorrowSummary(int isMut, int loanStart, int loanCount, int tokIdx)
    {
        IsMut = isMut;
        LoanStart = loanStart;
        LoanCount = loanCount;
        TokIdx = tokIdx;
    }
}

// Slots belong to the existing runtime move/init lattice, not source reachability.
// Root slots retain local IDs; value-struct fields extend the same state vector.
public struct RuntimeInitializationSlot
{
    public int LocalId;
    public int Parent;
    public int TypeId;
    public int ChildStart;
    public int ChildCount;
    public int IsValueStruct;
    public int HasOwnedFields;

    public RuntimeInitializationSlot(int localId, int parent, int typeId)
    {
        LocalId = localId;
        Parent = parent;
        TypeId = typeId;
        ChildStart = 0;
        ChildCount = 0;
        IsValueStruct = 0;
        HasOwnedFields = 0;
    }
}

public static class OwnershipChecker
{
    private const int InitUnreached = 0;
    private const int InitUninit = 1;
    private const int InitOk = 2;
    private const int InitMoved = 3;

    public static void CheckModule(
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        for (int i = 0; i < mir.Funcs.Count; i++)
        {
            MirFunc fn = mir.Funcs[i];
            CheckFunction(in fn, in ast, in ta, in names, in types, in mir, ref diags);
        }
    }

    public static void CheckFunction(
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        if (fn.BlockCount == 0 || fn.LocalCount == 0)
        {
            return;
        }

        // Borrow provenance decides whether an operand move is legal before the
        // move/init lattice consumes it. Rejected moves retain their source in
        // both lattices, including across loop backedges. Values encode owner+1:
        // positive for call-activation rejection, negative for ordinary loans.
        // The sign preserves the more specific activation diagnostic when its
        // precheck uses a longer operand lifetime than the ordinary move check.
        var rejectedMoves = new Vec<int>(mir.Operands.Count);
        rejectedMoves.Resize(mir.Operands.Count, 0);

        int livenessStride = fn.LocalCount;
        var liveIn = new Vec<byte>(fn.BlockCount * livenessStride);
        var liveOut = new Vec<byte>(fn.BlockCount * livenessStride);
        liveIn.Resize(fn.BlockCount * livenessStride, 0);
        liveOut.Resize(fn.BlockCount * livenessStride, 0);
        ComputeBackwardLiveness(in fn, in mir, ref liveIn, ref liveOut);

        RunBorrowAndMutabilityPass(in fn, in ast, in ta, in types, in mir, in liveIn, in liveOut, in names, ref rejectedMoves, ref diags);
        RunMoveAndLinearityPass(in fn, in ast, in ta, in types, in mir, in rejectedMoves, ref diags);
    }

    // =========================================================================
    // Pass 1: Definite Initialization, Move Tracking & L1 Linearity
    // =========================================================================

    private static void RunMoveAndLinearityPass(
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        int lc = fn.LocalCount;
        int bc = fn.BlockCount;
        FuncInstance fnInst = mir.FuncInstances[fn.FuncInstId];
        bool isCtor = (ast.Funcs[fnInst.FuncDeclId].Flags & DeclFlags.IsCtor) != 0;

        var slots = new Vec<RuntimeInitializationSlot>();
        for (int i = 0; i < lc; i++)
        {
            slots.Push(new RuntimeInitializationSlot(i, -1, mir.Locals[fn.LocalStart + i].TypeId));
        }
        for (int i = 0; i < slots.Count; i++)
        {
            RuntimeInitializationSlot slot = slots[i];
            TypeInfo type = types.Items[slot.TypeId];
            MirLocal root = mir.Locals[fn.LocalStart + slot.LocalId];
            bool requiresBinding = slot.Parent < 0 &&
                ((root.Flags & (MirLocalFlags.IsRefLocal | MirLocalFlags.IsBorrowedBind | MirLocalFlags.IsUsingVar)) != 0);
            if (type.Kind != TypeKinds.Struct || requiresBinding) continue;
            TypeDecl declaration = ast.Types[type.A];
            // Every application struct interface/base list is already rejected
            // by the closed-subset binder. Keep such invalid forms atomic instead
            // of treating an unacquired disposable struct as a vacuous value.
            if (declaration.UnitKind == UnitKinds.App && declaration.BaseTypeCount > 0) continue;
            slot.HasOwnedFields = types.NeedsMemoryDrop(slot.TypeId, in ast) ? 1 : 0;
            // An empty IDisposable value still owns a linear token: no fields do
            // not imply that the resource has been constructed or transferred.
            if (type.FieldCount == 0 && types.IsLinear(slot.TypeId, in ast)) continue;
            slot.IsValueStruct = 1;
            slot.ChildStart = slots.Count;
            slot.ChildCount = type.FieldCount;
            slots[i] = slot;
            for (int f = 0; f < type.FieldCount; f++)
            {
                slots.Push(new RuntimeInitializationSlot(slot.LocalId, i, types.Fields[type.FieldStart + f].TypeId));
            }
        }
        int width = slots.Count;
        var snapshots = new Vec<int>(lc);
        snapshots.Resize(lc, -1);
        for (int b = 0; b < bc; b++)
        {
            MirBlock block = mir.Blocks[fn.BlockStart + b];
            for (int k = 0; k < block.InstCount; k++)
            {
                MirInst inst = mir.Insts[block.InstStart + k];
                if (inst.Op == MirOp.BindRef &&
                    (mir.Locals[fn.LocalStart + inst.A].Flags & MirLocalFlags.IsTemp) != 0)
                {
                    // A conditional reference is a phi-like carrier with one
                    // BindRef per arm, not a stable address snapshot. Substituting
                    // its last arm would read that arm's branch-local index/ref
                    // temporaries on paths where they were never evaluated.
                    snapshots[inst.A] = snapshots[inst.A] == -1 ? inst.B : -2;
                }
            }
        }

        var isCopyLocal = new Vec<byte>(lc);
        var isLinearOwned = new Vec<byte>(lc);
        for (int i = 0; i < lc; i++)
        {
            MirLocal loc = mir.Locals[fn.LocalStart + i];
            bool isIndir = (loc.Flags & MirLocalFlags.IsRefLocal) != 0 ||
                           (loc.Flags & MirLocalFlags.IsBorrowedBind) != 0 ||
                           loc.Mode == PassMode.In || loc.Mode == PassMode.Ref || loc.Mode == PassMode.Out ||
                           loc.Mode == PassMode.ThisShared || loc.Mode == PassMode.ThisMut;
            isCopyLocal.Push(isIndir || types.IsCopy(loc.TypeId, in ast) ? (byte)1 : (byte)0);
            isLinearOwned.Push(!isIndir && types.IsLinear(loc.TypeId, in ast) ? (byte)1 : (byte)0);
        }

        var blockReached = new Vec<byte>(bc);
        blockReached.Resize(bc, 0);
        var inInit = new Vec<byte>(bc * width);
        inInit.Resize(bc * width, InitUnreached);
        var inLin = new Vec<byte>(bc * lc);
        inLin.Resize(bc * lc, 0);

        // Parameters start initialized recursively; empty value structs are vacuous.
        blockReached[0] = 1;
        for (int i = 0; i < width; i++)
        {
            MirLocal loc = mir.Locals[fn.LocalStart + slots[i].LocalId];
            bool assigned = (loc.Flags & MirLocalFlags.IsParam) != 0 && loc.Mode != PassMode.Out;
            inInit[i] = assigned ? (byte)InitOk : (byte)InitUninit;
            if (i < lc && assigned && isLinearOwned[i] != 0 && loc.Mode == PassMode.ByValue)
            {
                inLin[i] = 1;
            }
        }
        for (int i = width - 1; i >= 0; i--)
        {
            if (slots[i].IsValueStruct != 0) RecomputeRuntimeParent(i, in slots, ref inInit);
        }

        var curInit = new Vec<byte>(width);
        curInit.Resize(width, 0);
        var curLin = new Vec<byte>(lc);
        curLin.Resize(lc, 0);

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int b = 0; b < bc; b++)
            {
                if (blockReached[b] == 0) continue;
                for (int i = 0; i < width; i++) curInit[i] = inInit[b * width + i];
                for (int i = 0; i < lc; i++) curLin[i] = inLin[b * lc + i];
                TransferMoveBlock(b, emitDiags: false, isCtor, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref blockReached, ref inInit, ref inLin, ref changed, in rejectedMoves, ref diags);
            }
        }

        // Final diagnostic emission pass
        bool dummyChanged = false;
        for (int b = 0; b < bc; b++)
        {
            if (blockReached[b] == 0) continue;
            for (int i = 0; i < width; i++) curInit[i] = inInit[b * width + i];
            for (int i = 0; i < lc; i++) curLin[i] = inLin[b * lc + i];
            TransferMoveBlock(b, emitDiags: true, isCtor, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref blockReached, ref inInit, ref inLin, ref dummyChanged, in rejectedMoves, ref diags);
        }
    }

    private static void TransferMoveBlock(
        int b,
        bool emitDiags,
        bool isCtor,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isCopyLocal,
        in Vec<byte> isLinearOwned,
        in Vec<RuntimeInitializationSlot> slots,
        in Vec<int> snapshots,
        ref Vec<byte> curInit,
        ref Vec<byte> curLin,
        ref Vec<byte> blockReached,
        ref Vec<byte> inInit,
        ref Vec<byte> inLin,
        ref bool changed,
        in Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        int lc = fn.LocalCount;
        MirBlock blk = mir.Blocks[fn.BlockStart + b];

        for (int k = 0; k < blk.InstCount; k++)
        {
            MirInst inst = mir.Insts[blk.InstStart + k];
            Token tok = ta.Tokens[inst.TokIdx];

            switch (inst.Op)
            {
                case MirOp.AssignUse:
                case MirOp.MakeSomeWrap:
                case MirOp.MakeOkWrap:
                case MirOp.MakeErrWrap:
                case MirOp.LiftOptionSome:
                case MirOp.LiftResultOk:
                case MirOp.LiftResultErr:
                case MirOp.UnwrapWrap:
                    CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    if (inst.Op == MirOp.AssignUse && (inst.Flags & 8) != 0)
                    {
                        MirOperand reborrow = mir.Operands[inst.A];
                        CheckPlaceInitialized(reborrow.PlaceOrLitIdx, tok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    }
                    else
                    {
                        ConsumeOperandMove(inst.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                    }
                    ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    break;

                case MirOp.LiftOptionNone:
                    CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    break;

                case MirOp.AssignBin:
                    CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    ConsumeOperandMove(inst.B, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                    ConsumeOperandMove(inst.C, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                    ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    break;

                case MirOp.AssignUn:
                    CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    ConsumeOperandMove(inst.B, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                    ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    break;

                case MirOp.AssignCast:
                    CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    ConsumeOperandMove(inst.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                    ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    break;

                case MirOp.AllocClass:
                case MirOp.ConstructStruct:
                case MirOp.StackSpan:
                {
                    CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    for (int a = 0; a < inst.B; a++)
                    {
                        int fOpId = mir.ExtraLists[inst.A + a];
                        if (isCtor && inst.Op != MirOp.StackSpan)
                        {
                            MirOperand fop = mir.Operands[fOpId];
                            if (fop.Kind == MirOpKinds.CopyOrMove)
                            {
                                MirPlace fpl = mir.Places[fop.PlaceOrLitIdx];
                                MirLocal floc = mir.Locals[fn.LocalStart + fpl.LocalId];
                                if ((floc.Flags & MirLocalFlags.IsCtorFieldSlot) != 0 && curInit[fpl.LocalId] != InitOk)
                                {
                                    if (!types.IsZeroSafe(floc.TypeId, in ast) && emitDiags)
                                    {
                                        diags.ReportTok(VibCodes.NullOrZeroUnsafe, tok, "Constructor must initialize all non-zero-safe fields before returning."u8);
                                    }
                                    continue;
                                }
                            }
                        }
                        ConsumeOperandMove(fOpId, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                    }
                    ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    break;
                }

                case MirOp.BindRef:
                    if ((inst.Flags & 4) != 0)
                    {
                        _ = ResolveRuntimeSlot(inst.B, tok, emitDiags, 0, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    }
                    else
                    {
                        CheckPlaceInitialized(inst.B, tok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    }
                    SetRuntimeState(inst.A, InitOk, in slots, ref curInit);
                    break;

                case MirOp.Call:
                {
                    for (int a = 0; a < inst.C; a++)
                    {
                        MirCallArg ca = mir.CallArgs[inst.B + a];
                        Token argTok = ta.Tokens[ca.TokIdx];
                        if (ca.Mode == PassMode.ByValue || (ca.Mode == PassMode.ThisShared && IsOperandCallArg(inst.A, a, in types, in mir)) || (ca.Mode == PassMode.ThisMut && IsOperandCallArg(inst.A, a, in types, in mir)))
                        {
                            if (ca.Mode == PassMode.ByValue)
                            {
                                MirOperand cop = mir.Operands[ca.OperandOrPlaceId];
                                if (cop.Kind == MirOpKinds.CopyOrMove && types.Items[cop.TypeId].Kind == TypeKinds.Span)
                                {
                                    CheckPlaceInitialized(cop.PlaceOrLitIdx, argTok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                                }
                                else
                                {
                                    ConsumeOperandMove(ca.OperandOrPlaceId, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                                }
                            }
                            else
                            {
                                // Class receiver passed as operand (borrowed, not moved!)
                                MirOperand rop = mir.Operands[ca.OperandOrPlaceId];
                                if (rop.Kind == MirOpKinds.CopyOrMove)
                                {
                                    CheckPlaceInitialized(rop.PlaceOrLitIdx, argTok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                                }
                            }
                        }
                        else if (ca.Mode == PassMode.In || ca.Mode == PassMode.Ref || ca.Mode == PassMode.ThisShared || ca.Mode == PassMode.ThisMut)
                        {
                            CheckPlaceInitialized(ca.OperandOrPlaceId, argTok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                        }
                        else if (ca.Mode == PassMode.Out)
                        {
                            CheckIndexOpsInPlace(ca.OperandOrPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                            ApplyWritePlace(ca.OperandOrPlaceId, argTok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                        }
                    }
                    if (inst.RefDestLocalId >= 0)
                    {
                        SetRuntimeState(inst.RefDestLocalId, InitOk, in slots, ref curInit);
                    }
                    else if (inst.DestPlaceId >= 0)
                    {
                        CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                        ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    }
                    break;
                }

                case MirOp.Intrinsic:
                {
                    int plCount = inst.D >>> 20;
                    int plStart = inst.D & 0xFFFFF;
                    for (int p = 0; p < plCount; p++)
                    {
                        CheckPlaceInitialized(mir.ExtraLists[plStart + p], tok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                    }
                    bool nonConsuming = IsNonConsumingValueIntrinsic((MirIntrinsic)inst.A);
                    for (int v = 0; v < inst.C; v++)
                    {
                        int vOpId = mir.ExtraLists[inst.B + v];
                        MirOperand vop = mir.Operands[vOpId];
                        if (nonConsuming && vop.Kind == MirOpKinds.CopyOrMove)
                        {
                            CheckPlaceInitialized(vop.PlaceOrLitIdx, ta.Tokens[vop.TokIdx], emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                        }
                        else
                        {
                            ConsumeOperandMove(vOpId, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                        }
                    }
                    if (inst.RefDestLocalId >= 0)
                    {
                        SetRuntimeState(inst.RefDestLocalId, InitOk, in slots, ref curInit);
                    }
                    else if (inst.DestPlaceId >= 0)
                    {
                        CheckIndexOpsInPlace(inst.DestPlaceId, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                        ApplyWritePlace(inst.DestPlaceId, tok, emitDiags, in fn, in ta, in mir, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, ref diags);
                    }
                    break;
                }

                case MirOp.DisposeResource:
                {
                    int dlid = inst.A;
                    if (emitDiags && curInit[dlid] != InitOk)
                    {
                        diags.ReportTok(VibCodes.UseAfterMove, tok, "Cannot dispose moved or uninitialized resource."u8);
                    }
                    SetRuntimeState(dlid, InitMoved, in slots, ref curInit);
                    curLin[dlid] = 0;
                    break;
                }

                case MirOp.ScopeExit:
                {
                    for (int s = 0; s < inst.B; s++)
                    {
                        int slid = mir.ExtraLists[inst.A + s];
                        if (isLinearOwned[slid] != 0 && curLin[slid] != 0)
                        {
                            if (emitDiags)
                            {
                                diags.ReportTok(VibCodes.LinearViolation, tok, "Linear IDisposable resource must be bound with 'using var' or moved/disposed on all paths."u8);
                            }
                            curLin[slid] = 0;
                        }
                        if (mir.Locals[fn.LocalStart + slid].Mode != PassMode.Out)
                        {
                            SetRuntimeState(slid, InitUninit, in slots, ref curInit);
                        }
                    }
                    break;
                }
            }
        }

        // Terminator transfer
        MirTerm term = blk.Term;
        Token tTok = ta.Tokens[term.TokIdx];
        switch (term.Kind)
        {
            case MirTermKind.Goto:
                JoinIntoSuccessor(term.A, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                break;

            case MirTermKind.Branch:
                ConsumeOperandMove(term.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                JoinIntoSuccessor(term.B, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                JoinIntoSuccessor(term.C, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                break;

            case MirTermKind.MatchOption:
            {
                if ((term.Flags & 1) != 0)
                {
                    CheckPlaceInitialized(term.A, tTok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                }
                else
                {
                    ConsumeOperandMove(term.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                }
                // None branch
                JoinIntoSuccessor(term.D, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                // Some branch binds `term.B`
                SetRuntimeState(term.B, InitOk, in slots, ref curInit);
                if (isLinearOwned[term.B] != 0) curLin[term.B] = 1;
                JoinIntoSuccessor(term.C, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                break;
            }

            case MirTermKind.MatchResult:
            {
                if ((term.Flags & 1) != 0)
                {
                    CheckPlaceInitialized(term.A, tTok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                }
                else
                {
                    ConsumeOperandMove(term.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                }
                byte savedErrInit = curInit[term.D];
                byte savedErrLin = curLin[term.D];
                // Ok branch binds `term.B` -> block `term.C`
                SetRuntimeState(term.B, InitOk, in slots, ref curInit);
                if (isLinearOwned[term.B] != 0) curLin[term.B] = 1;
                JoinIntoSuccessor(term.C, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                // Restore & bind Err `term.D` -> block `term.E`
                SetRuntimeState(term.B, InitUninit, in slots, ref curInit);
                curLin[term.B] = 0;
                SetRuntimeState(term.D, InitOk, in slots, ref curInit);
                if (isLinearOwned[term.D] != 0) curLin[term.D] = 1;
                JoinIntoSuccessor(term.E, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                SetRuntimeState(term.D, savedErrInit, in slots, ref curInit);
                curLin[term.D] = savedErrLin;
                break;
            }

            case MirTermKind.GuardResultIsErr:
            {
                ConsumeOperandMove(term.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                // Err branch (`term.D`): `errWrapper` (`term.C`) is initialized; `okLocal` (`term.B`) is uninitialized!
                byte saveOkInit = curInit[term.B];
                byte saveOkLin = curLin[term.B];
                SetRuntimeState(term.B, InitUninit, in slots, ref curInit);
                curLin[term.B] = 0;
                SetRuntimeState(term.C, InitOk, in slots, ref curInit);
                if (isLinearOwned[term.C] != 0) curLin[term.C] = 1;
                JoinIntoSuccessor(term.D, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);

                // Ok branch (`term.E`): `okLocal` (`term.B`) is initialized!
                SetRuntimeState(term.C, InitUninit, in slots, ref curInit);
                curLin[term.C] = 0;
                SetRuntimeState(term.B, InitOk, in slots, ref curInit);
                curLin[term.B] = isLinearOwned[term.B];
                JoinIntoSuccessor(term.E, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                SetRuntimeState(term.B, saveOkInit, in slots, ref curInit);
                curLin[term.B] = saveOkLin;
                break;
            }

            case MirTermKind.GuardOptionIsNone:
            {
                ConsumeOperandMove(term.A, emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                // None branch (`term.C`): `someVal` (`term.B`) is uninitialized
                byte saveSomeInit = curInit[term.B];
                byte saveSomeLin = curLin[term.B];
                SetRuntimeState(term.B, InitUninit, in slots, ref curInit);
                curLin[term.B] = 0;
                JoinIntoSuccessor(term.C, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);

                // Some branch (`term.D`): `someVal` (`term.B`) is initialized
                SetRuntimeState(term.B, InitOk, in slots, ref curInit);
                curLin[term.B] = isLinearOwned[term.B];
                JoinIntoSuccessor(term.D, lc, in curInit, in curLin, ref blockReached, ref inInit, ref inLin, ref changed);
                SetRuntimeState(term.B, saveSomeInit, in slots, ref curInit);
                curLin[term.B] = saveSomeLin;
                break;
            }

            case MirTermKind.Return:
            {

                if (term.A >= 0)
                {
                    ConsumeOperandMove(term.A, !isCtor && emitDiags, in fn, in ast, in ta, in types, in mir, in isCopyLocal, in isLinearOwned, in slots, in snapshots, ref curInit, ref curLin, in rejectedMoves, ref diags);
                }
                else if (term.B >= 0)
                {
                    CheckPlaceInitialized(term.B, tTok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                }

                if (emitDiags)
                {
                    for (int i = 0; i < lc; i++)
                    {
                        MirLocal loc = mir.Locals[fn.LocalStart + i];
                        if (isLinearOwned[i] != 0 && curLin[i] != 0)
                        {
                            diags.ReportTok(VibCodes.LinearViolation, tTok, "Linear IDisposable resource must be bound with 'using var' or moved/disposed before return."u8);
                        }
                        if ((loc.Flags & MirLocalFlags.IsParam) != 0 && loc.Mode == PassMode.Out && curInit[i] != InitOk)
                        {
                            diags.ReportTok(VibCodes.UseAfterMove, tTok, "Out parameter must be initialized before return."u8);
                        }
                    }
                }
                break;
            }
        }
    }

    private static bool IsOperandCallArg(int calleeInstId, int argIdx, in TypeTable types, in MirModuleArena mir)
    {
        if (argIdx != 0) return false;
        FuncInstance ci = mir.FuncInstances[calleeInstId];
        return ci.OwnerTypeId >= 0 && types.Items[ci.OwnerTypeId].Kind == TypeKinds.Class;
    }

    private static void JoinIntoSuccessor(
        int succBlk,
        int lc,
        in Vec<byte> curInit,
        in Vec<byte> curLin,
        ref Vec<byte> blockReached,
        ref Vec<byte> inInit,
        ref Vec<byte> inLin,
        ref bool changed)
    {
        if (blockReached[succBlk] == 0)
        {
            blockReached[succBlk] = 1;
            changed = true;
        }
        int initBase = succBlk * curInit.Count;
        for (int i = 0; i < curInit.Count; i++)
        {
            byte oldI = inInit[initBase + i];
            byte newI = JoinInit(oldI, curInit[i]);
            if (newI != oldI)
            {
                inInit[initBase + i] = newI;
                changed = true;
            }
        }
        int baseIdx = succBlk * lc;
        for (int i = 0; i < lc; i++)
        {
            byte oldL = inLin[baseIdx + i];
            byte newL = (byte)(oldL | curLin[i]);
            if (newL != oldL)
            {
                inLin[baseIdx + i] = newL;
                changed = true;
            }
        }
    }

    private static byte JoinInit(byte a, byte b)
    {
        if (a == InitUnreached) return b;
        if (b == InitUnreached) return a;
        if (a == b) return a;
        if (a == InitMoved || b == InitMoved) return InitMoved;
        return InitUninit;
    }

    private static void CheckIndexOpsInPlace(
        int placeId,
        bool emitDiags,
        in MirFunc fn,
        in TokenArena ta,
        in MirModuleArena mir,
        in Vec<RuntimeInitializationSlot> slots,
        in Vec<int> snapshots,
        in Vec<byte> curInit,
        ref DiagnosticBag diags)
    {
        if (placeId < 0) return;
        MirPlace pl = mir.Places[placeId];
        for (int i = 0; i < pl.ProjCount; i++)
        {
            MirProj pr = mir.Projs[pl.ProjStart + i];
            if (pr.Kind == MirProjKinds.Index && pr.IndexOpId >= 0)
            {
                MirOperand iop = mir.Operands[pr.IndexOpId];
                if (iop.Kind == MirOpKinds.CopyOrMove)
                {
                    CheckPlaceInitialized(iop.PlaceOrLitIdx, ta.Tokens[iop.TokIdx], emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
                }
            }
        }
    }

    private static void CheckPlaceInitialized(
        int placeId,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in TokenArena ta,
        in MirModuleArena mir,
        in Vec<RuntimeInitializationSlot> slots,
        in Vec<int> snapshots,
        in Vec<byte> curInit,
        ref DiagnosticBag diags)
    {
        int slot = ResolveRuntimeSlot(placeId, tok, emitDiags, 0, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
        CheckRuntimeSlot(slot, tok, emitDiags, in curInit, ref diags);
    }

    private static void ConsumeOperandMove(
        int opId,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isCopyLocal,
        in Vec<byte> isLinearOwned,
        in Vec<RuntimeInitializationSlot> slots,
        in Vec<int> snapshots,
        ref Vec<byte> curInit,
        ref Vec<byte> curLin,
        in Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        if (opId < 0) return;
        MirOperand op = mir.Operands[opId];
        if (op.Kind != MirOpKinds.CopyOrMove) return;

        Token tok = ta.Tokens[op.TokIdx];
        MirPlace pl = mir.Places[op.PlaceOrLitIdx];
        CheckPlaceInitialized(op.PlaceOrLitIdx, tok, emitDiags, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);

        if (types.IsCopy(op.TypeId, in ast)) return;

        MirLocal loc = mir.Locals[fn.LocalStart + pl.LocalId];
        bool isBorrowedParam = (loc.Flags & MirLocalFlags.IsParam) != 0 &&
            (loc.Mode == PassMode.In || loc.Mode == PassMode.Ref || loc.Mode == PassMode.Out ||
             loc.Mode == PassMode.ThisShared || loc.Mode == PassMode.ThisMut);
        bool isBorrowedBind = (loc.Flags & MirLocalFlags.IsBorrowedBind) != 0;
        bool illegalMoveForm = pl.ProjCount > 0 || isBorrowedParam || isBorrowedBind;
        if (illegalMoveForm && emitDiags)
        {
            // A borrow conflict does not erase independent field/borrowed-place
            // move restrictions: such a move still reports VIB005 as well.
            diags.ReportTok(VibCodes.IllegalFieldMove, tok, "Cannot move non-Copy value out of a field, indexed element, or borrowed place."u8);
        }

        int rejection = rejectedMoves[opId];
        if (rejection != 0)
        {
            // The failed consuming operand owns this obligation's error. Keep
            // initialization intact for recovery, but do not invent a second
            // undisposed-resource error because its transfer was rejected.
            int rejectedOwner = rejection > 0 ? rejection - 1 : -rejection - 1;
            if (isLinearOwned[rejectedOwner] != 0) curLin[rejectedOwner] = 0;
            return;
        }
        if (pl.ProjCount == 0 && (loc.Flags & MirLocalFlags.IsUsingVar) != 0)
        {
            if (emitDiags)
            {
                diags.ReportTok(VibCodes.LinearViolation, tok, "Cannot move 'using' resource variable."u8);
            }
            return;
        }
        if (illegalMoveForm) return;

        SetRuntimeState(pl.LocalId, InitMoved, in slots, ref curInit);
        if (isLinearOwned[pl.LocalId] != 0)
        {
            curLin[pl.LocalId] = 0;
        }
    }

    private static void ApplyWritePlace(
        int placeId,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in TokenArena ta,
        in MirModuleArena mir,
        in Vec<byte> isLinearOwned,
        in Vec<RuntimeInitializationSlot> slots,
        in Vec<int> snapshots,
        ref Vec<byte> curInit,
        ref Vec<byte> curLin,
        ref DiagnosticBag diags)
    {
        int slotId = ResolveRuntimeSlot(placeId, tok, emitDiags, 0, in fn, in ta, in mir, in slots, in snapshots, in curInit, ref diags);
        if (slotId < 0) return;
        RuntimeInitializationSlot slot = slots[slotId];
        int lid = slot.LocalId;
        MirLocal loc = mir.Locals[fn.LocalStart + lid];
        if (slot.Parent >= 0 && slots[lid].HasOwnedFields != 0 && curInit[lid] != InitOk &&
            curInit[lid] != InitMoved && emitDiags)
        {
            diags.ReportTok(VibCodes.Unsupported, tok,
                "A struct with owned fields must be initialized in one assignment; initializing it field by field (including through out) is not supported in Vibranium v0.2."u8);
        }
        if (slot.Parent < 0)
        {
            if ((loc.Flags & MirLocalFlags.IsUsingVar) != 0 && curInit[lid] == InitOk && emitDiags)
            {
                diags.ReportTok(VibCodes.LinearViolation, tok, "Cannot reassign 'using' resource variable."u8);
            }
            else if (isLinearOwned[lid] != 0 && curLin[lid] != 0 && emitDiags)
            {
                diags.ReportTok(VibCodes.LinearViolation, tok, "Cannot overwrite live linear IDisposable resource without disposing or moving it first."u8);
            }
            if (isLinearOwned[lid] != 0) curLin[lid] = 1;
        }
        SetRuntimeState(slotId, InitOk, in slots, ref curInit);
        int parent = slot.Parent;
        while (parent >= 0)
        {
            RecomputeRuntimeParent(parent, in slots, ref curInit);
            if (slots[parent].Parent < 0 && curInit[parent] == InitOk && isLinearOwned[parent] != 0)
            {
                curLin[parent] = 1;
            }
            parent = slots[parent].Parent;
        }
    }

    private static void SetRuntimeState(int slotId, byte value, in Vec<RuntimeInitializationSlot> slots, ref Vec<byte> state)
    {
        state[slotId] = value;
        RuntimeInitializationSlot slot = slots[slotId];
        for (int c = 0; c < slot.ChildCount; c++) SetRuntimeState(slot.ChildStart + c, value, in slots, ref state);
    }

    private static void RecomputeRuntimeParent(int slotId, in Vec<RuntimeInitializationSlot> slots, ref Vec<byte> state)
    {
        RuntimeInitializationSlot slot = slots[slotId];
        byte value = InitOk;
        for (int c = 0; c < slot.ChildCount; c++) value = JoinInit(value, state[slot.ChildStart + c]);
        state[slotId] = value;
    }

    private static void CheckRuntimeSlot(int slotId, Token tok, bool emitDiags, in Vec<byte> state, ref DiagnosticBag diags)
    {
        if (!emitDiags || slotId < 0 || state[slotId] == InitOk) return;
        ReadOnlySpan<byte> message = state[slotId] == InitMoved ? "Use of moved value."u8 : "Use of uninitialized value."u8;
        diags.ReportTok(VibCodes.UseAfterMove, tok, message);
    }

    private static int FindRuntimeSnapshotSlot(
        int placeId, int hops, in MirFunc fn, in MirModuleArena mir,
        in Vec<RuntimeInitializationSlot> slots, in Vec<int> snapshots)
    {
        if (placeId < 0 || hops > fn.LocalCount) return -1;
        MirPlace place = mir.Places[placeId];
        int slot = place.LocalId;
        if (slot < 0 || slot >= fn.LocalCount) return -1;
        MirLocal root = mir.Locals[fn.LocalStart + slot];
        bool stableSnapshot = snapshots[slot] >= 0;
        if (stableSnapshot)
        {
            slot = FindRuntimeSnapshotSlot(snapshots[slot], hops + 1, in fn, in mir, in slots, in snapshots);
        }
        else if ((root.Flags & (MirLocalFlags.IsRefLocal | MirLocalFlags.IsBorrowedBind)) != 0)
        {
            // Opaque/merged references retain their definitely-bound carrier
            // state. Their pointees are validated where each binding is formed.
            return -1;
        }
        for (int p = 0; p < place.ProjCount; p++)
        {
            MirProj projection = mir.Projs[place.ProjStart + p];
            if (slot < 0) return -1;
            if (projection.Kind == MirProjKinds.Deref && stableSnapshot) continue;
            if (projection.Kind != MirProjKinds.Field || (projection.Flags & 1) != 0) return -1;
            RuntimeInitializationSlot parent = slots[slot];
            if (projection.FieldIdx < 0 || projection.FieldIdx >= parent.ChildCount) return -1;
            slot = parent.ChildStart + projection.FieldIdx;
        }
        return slot;
    }

    private static int ResolveRuntimeSlot(
        int placeId, Token tok, bool emitDiags, int hops, in MirFunc fn, in TokenArena ta,
        in MirModuleArena mir, in Vec<RuntimeInitializationSlot> slots, in Vec<int> snapshots,
        in Vec<byte> state, ref DiagnosticBag diags)
    {
        if (placeId < 0 || hops > fn.LocalCount) return -1;
        MirPlace place = mir.Places[placeId];
        int slot = place.LocalId;
        if (slot < 0 || slot >= fn.LocalCount) return -1;
        MirLocal root = mir.Locals[fn.LocalStart + slot];
        if (snapshots[slot] >= 0)
        {
            CheckRuntimeSlot(slot, tok, emitDiags, in state, ref diags);
            int origin = snapshots[slot];
            int originSlot = ResolveRuntimeSlot(origin, tok, emitDiags, hops + 1, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
            if (FindRuntimeSnapshotSlot(origin, 0, in fn, in mir, in slots, in snapshots) >= 0)
            {
                slot = originSlot;
            }
            else
            {
                // Heap/index/opaque pointees do not become struct-init slots.
                // Still recheck their stable origin after RHS evaluation: forming
                // an address does not permit moving its owner before writing it.
                CheckRuntimeSlot(originSlot, tok, emitDiags, in state, ref diags);
                slot = -1;
            }
        }
        else if ((root.Flags & (MirLocalFlags.IsRefLocal | MirLocalFlags.IsBorrowedBind)) != 0)
        {
            // A named reference must be bound before reading/writing its pointee.
            CheckRuntimeSlot(slot, tok, emitDiags, in state, ref diags);
        }
        for (int p = 0; p < place.ProjCount; p++)
        {
            MirProj projection = mir.Projs[place.ProjStart + p];
            if (slot >= 0 && state[slot] == InitMoved)
            {
                CheckRuntimeSlot(slot, tok, emitDiags, in state, ref diags);
                return -1;
            }
            if (projection.Kind == MirProjKinds.Index && projection.IndexOpId >= 0)
            {
                MirOperand index = mir.Operands[projection.IndexOpId];
                if (index.Kind == MirOpKinds.CopyOrMove)
                {
                    CheckPlaceInitialized(index.PlaceOrLitIdx, ta.Tokens[index.TokIdx], emitDiags, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                }
            }
            if ((projection.Flags & 1) != 0 || projection.Kind == MirProjKinds.Index)
            {
                CheckRuntimeSlot(slot, tok, emitDiags, in state, ref diags);
                slot = -1;
            }
            else if (projection.Kind == MirProjKinds.Field && slot >= 0)
            {
                RuntimeInitializationSlot parent = slots[slot];
                if (projection.FieldIdx >= 0 && projection.FieldIdx < parent.ChildCount)
                {
                    slot = parent.ChildStart + projection.FieldIdx;
                }
                else
                {
                    CheckRuntimeSlot(slot, tok, emitDiags, in state, ref diags);
                    slot = -1;
                }
            }
        }
        return slot;
    }

    // =========================================================================
    // Pass 2: Backward Local Liveness for Non-Lexical Lifetimes (NLL)
    // =========================================================================

    private static void ComputeBackwardLiveness(
        in MirFunc fn,
        in MirModuleArena mir,
        ref Vec<byte> liveIn,
        ref Vec<byte> liveOut)
    {
        int lc = fn.LocalCount;
        int bc = fn.BlockCount;
        var cur = new Vec<byte>(lc);
        cur.Resize(lc, 0);

        bool changed = true;
        int iter = 0;
        while (changed && iter < 200)
        {
            changed = false;
            iter += 1;

            for (int b = bc - 1; b >= 0; b--)
            {
                MirBlock blk = mir.Blocks[fn.BlockStart + b];
                for (int i = 0; i < lc; i++) cur[i] = 0;

                // Union LiveIn of successor blocks (killing pattern bindings defined on specific edges!)
                MirTerm term = blk.Term;
                switch (term.Kind)
                {
                    case MirTermKind.Goto:
                        OrLivenessFrom(term.A, lc, -1, in liveIn, ref cur);
                        break;
                    case MirTermKind.Branch:
                        OrLivenessFrom(term.B, lc, -1, in liveIn, ref cur);
                        OrLivenessFrom(term.C, lc, -1, in liveIn, ref cur);
                        break;
                    case MirTermKind.MatchOption:
                        OrLivenessFrom(term.C, lc, term.B, in liveIn, ref cur);
                        OrLivenessFrom(term.D, lc, -1, in liveIn, ref cur);
                        break;
                    case MirTermKind.MatchResult:
                        OrLivenessFrom(term.C, lc, term.B, in liveIn, ref cur);
                        OrLivenessFrom(term.E, lc, term.D, in liveIn, ref cur);
                        break;
                    case MirTermKind.GuardResultIsErr:
                        OrLivenessFrom(term.D, lc, term.C, in liveIn, ref cur);
                        OrLivenessFrom(term.E, lc, term.B, in liveIn, ref cur);
                        break;
                    case MirTermKind.GuardOptionIsNone:
                        OrLivenessFrom(term.C, lc, -1, in liveIn, ref cur);
                        OrLivenessFrom(term.D, lc, term.B, in liveIn, ref cur);
                        break;
                }

                for (int i = 0; i < lc; i++)
                {
                    if (liveOut[b * lc + i] != cur[i])
                    {
                        liveOut[b * lc + i] = cur[i];
                        changed = true;
                    }
                }

                StepBackwardTerminatorUses(in term, in mir, ref cur);

                for (int k = blk.InstCount - 1; k >= 0; k--)
                {
                    MirInst inst = mir.Insts[blk.InstStart + k];
                    StepBackwardInst(in inst, in fn, in mir, ref cur);
                }

                for (int i = 0; i < lc; i++)
                {
                    if (liveIn[b * lc + i] != cur[i])
                    {
                        liveIn[b * lc + i] = cur[i];
                        changed = true;
                    }
                }
            }
        }
    }

    private static void OrLivenessFrom(int succBlk, int lc, int edgeDefinedLocal, in Vec<byte> liveIn, ref Vec<byte> dst)
    {
        int baseIdx = succBlk * lc;
        for (int i = 0; i < lc; i++)
        {
            if (i == edgeDefinedLocal) continue;
            if (liveIn[baseIdx + i] != 0) dst[i] = 1;
        }
    }

    private static void StepBackwardTerminatorUses(in MirTerm term, in MirModuleArena mir, ref Vec<byte> live)
    {
        switch (term.Kind)
        {
            case MirTermKind.Branch:
                MarkOperandRead(term.A, in mir, ref live);
                break;
            case MirTermKind.MatchOption:
            case MirTermKind.MatchResult:
                if ((term.Flags & 1) != 0) MarkPlaceRead(term.A, in mir, ref live);
                else MarkOperandRead(term.A, in mir, ref live);
                break;
            case MirTermKind.GuardResultIsErr:
            case MirTermKind.GuardOptionIsNone:
                MarkOperandRead(term.A, in mir, ref live);
                break;
            case MirTermKind.Return:
                if (term.A >= 0) MarkOperandRead(term.A, in mir, ref live);
                if (term.B >= 0) MarkPlaceRead(term.B, in mir, ref live);
                break;
        }
    }

    private static void StepBackwardInst(in MirInst inst, in MirFunc fn, in MirModuleArena mir, ref Vec<byte> live)
    {
        // Kill direct whole-local definitions first (then Gen RHS uses!)
        if (inst.RefDestLocalId >= 0)
        {
            live[inst.RefDestLocalId] = 0;
        }
        if (inst.DestPlaceId >= 0)
        {
            MirPlace dpl = mir.Places[inst.DestPlaceId];
            MirLocal dloc = mir.Locals[fn.LocalStart + dpl.LocalId];
            bool isIndirectRefParam = dloc.Mode == PassMode.Ref || dloc.Mode == PassMode.In;
            if (dpl.ProjCount == 0 && !isIndirectRefParam)
            {
                live[dpl.LocalId] = 0;
            }
            else
            {
                // Projected write (`x.f = v`, `*r = v`, or writing through `ref` parameter `p = v`) reads the base carrier!
                live[dpl.LocalId] = 1;
                MarkPlaceIndexReads(inst.DestPlaceId, in mir, ref live);
            }
        }

        switch (inst.Op)
        {
            case MirOp.AssignUse:
            case MirOp.AssignCast:
            case MirOp.MakeSomeWrap:
            case MirOp.MakeOkWrap:
            case MirOp.MakeErrWrap:
            case MirOp.LiftOptionSome:
            case MirOp.LiftResultOk:
            case MirOp.LiftResultErr:
            case MirOp.UnwrapWrap:
                MarkOperandRead(inst.A, in mir, ref live);
                break;
            case MirOp.AssignBin:
                MarkOperandRead(inst.B, in mir, ref live);
                MarkOperandRead(inst.C, in mir, ref live);
                break;
            case MirOp.AssignUn:
                MarkOperandRead(inst.B, in mir, ref live);
                break;
            case MirOp.AllocClass:
            case MirOp.ConstructStruct:
            case MirOp.StackSpan:
                for (int i = 0; i < inst.B; i++)
                {
                    MarkOperandRead(mir.ExtraLists[inst.A + i], in mir, ref live);
                }
                break;
            case MirOp.BindRef:
                live[inst.A] = 0;
                MarkPlaceRead(inst.B, in mir, ref live);
                break;
            case MirOp.Call:
                for (int a = 0; a < inst.C; a++)
                {
                    MirCallArg ca = mir.CallArgs[inst.B + a];
                    if (ca.Mode == PassMode.Out)
                    {
                        MirPlace opl = mir.Places[ca.OperandOrPlaceId];
                        MirLocal oloc = mir.Locals[fn.LocalStart + opl.LocalId];
                        bool outThroughRefParam = oloc.Mode == PassMode.Ref || oloc.Mode == PassMode.In;
                        if (opl.ProjCount == 0 && !outThroughRefParam) live[opl.LocalId] = 0;
                        else { live[opl.LocalId] = 1; MarkPlaceIndexReads(ca.OperandOrPlaceId, in mir, ref live); }
                    }
                }
                for (int a = 0; a < inst.C; a++)
                {
                    MirCallArg ca = mir.CallArgs[inst.B + a];
                    if (ca.Mode == PassMode.ByValue)
                    {
                        MarkOperandRead(ca.OperandOrPlaceId, in mir, ref live);
                    }
                    else if (ca.Mode != PassMode.Out)
                    {
                        // Check if class receiver operand vs place
                        if ((ca.Mode == PassMode.ThisShared || ca.Mode == PassMode.ThisMut) &&
                            mir.FuncInstances[inst.A].OwnerTypeId >= 0 &&
                            ca.OperandOrPlaceId < mir.Operands.Count &&
                            IsClassOwnerFunc(inst.A, in mir))
                        {
                            MarkOperandRead(ca.OperandOrPlaceId, in mir, ref live);
                        }
                        else
                        {
                            MarkPlaceRead(ca.OperandOrPlaceId, in mir, ref live);
                        }
                    }
                }
                break;
            case MirOp.Intrinsic:
            {
                int plCount = inst.D >>> 20;
                int plStart = inst.D & 0xFFFFF;
                for (int p = 0; p < plCount; p++)
                {
                    MarkPlaceRead(mir.ExtraLists[plStart + p], in mir, ref live);
                }
                for (int v = 0; v < inst.C; v++)
                {
                    MarkOperandRead(mir.ExtraLists[inst.B + v], in mir, ref live);
                }
                break;
            }
            case MirOp.DisposeResource:
                live[inst.A] = 1;
                break;
            case MirOp.ScopeExit:
                for (int s = 0; s < inst.B; s++)
                {
                    live[mir.ExtraLists[inst.A + s]] = 0;
                }
                break;
        }
    }

    private static bool IsClassOwnerFunc(int funcInstId, in MirModuleArena mir)
    {
        // Note: OwnerTypeId key starts with "Class:" for classes
        int ownerTid = mir.FuncInstances[funcInstId].OwnerTypeId;
        if (ownerTid < 0) return false;
        ReadOnlySpan<byte> fk = mir.GetFuncKey(funcInstId);
        return Bytes.IndexOf(fk, ":Class:"u8) >= 0;
    }

    private static void MarkOperandRead(int opId, in MirModuleArena mir, ref Vec<byte> live)
    {
        if (opId < 0) return;
        MirOperand op = mir.Operands[opId];
        if (op.Kind == MirOpKinds.CopyOrMove)
        {
            MarkPlaceRead(op.PlaceOrLitIdx, in mir, ref live);
        }
    }

    private static void MarkPlaceRead(int placeId, in MirModuleArena mir, ref Vec<byte> live)
    {
        if (placeId < 0) return;
        MirPlace pl = mir.Places[placeId];
        live[pl.LocalId] = 1;
        MarkPlaceIndexReads(placeId, in mir, ref live);
    }

    private static void MarkPlaceIndexReads(int placeId, in MirModuleArena mir, ref Vec<byte> live)
    {
        MirPlace pl = mir.Places[placeId];
        for (int i = 0; i < pl.ProjCount; i++)
        {
            MirProj pr = mir.Projs[pl.ProjStart + i];
            if (pr.Kind == MirProjKinds.Index && pr.IndexOpId >= 0)
            {
                MarkOperandRead(pr.IndexOpId, in mir, ref live);
            }
        }
    }

    // =========================================================================
    // Pass 3: Mutability (VIB004), Call Overlap (VIB002), Active Conflicts (VIB003), Escapes (VIB007)
    // =========================================================================

    private readonly struct BlockCarrierLoan
    {
        public readonly int CarrierId;
        public readonly Loan LoanVal;
        public readonly int NextIdx;

        public BlockCarrierLoan(int carrierId, Loan loanVal, int nextIdx)
        {
            CarrierId = carrierId;
            LoanVal = loanVal;
            NextIdx = nextIdx;
        }
    }

    private readonly struct CarrierLoan
    {
        public readonly int CarrierId;
        public readonly Loan LoanVal;

        public CarrierLoan(int carrierId, Loan loanVal)
        {
            CarrierId = carrierId;
            LoanVal = loanVal;
        }
    }

    private static void RunBorrowAndMutabilityPass(
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> liveIn,
        in Vec<byte> liveOut,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        int lc = fn.LocalCount;
        int bc = fn.BlockCount;

        var isBorrowCarrier = new Vec<byte>(lc);
        for (int i = 0; i < lc; i++)
        {
            MirLocal loc = mir.Locals[fn.LocalStart + i];
            bool carrier = (loc.Flags & MirLocalFlags.IsRefLocal) != 0 ||
                           types.IsView(loc.TypeId) ||
                           (loc.Flags & MirLocalFlags.IsBorrowedBind) != 0;
            isBorrowCarrier.Push(carrier ? (byte)1 : (byte)0);
        }

        // StackSpan loans name an allocation id, not a view/carrier local. Preserve
        // the actual backing allocation owner so copied views cannot shorten it.
        var stackAllocationOwners = new Vec<int>(fn.StackSpanCount);
        stackAllocationOwners.Resize(fn.StackSpanCount, -1);
        for (int b = 0; b < bc; b++)
        {
            MirBlock block = mir.Blocks[fn.BlockStart + b];
            for (int i = 0; i < block.InstCount; i++)
            {
                MirInst instruction = mir.Insts[block.InstStart + i];
                if (instruction.Op == MirOp.StackSpan && instruction.DestPlaceId >= 0)
                {
                    stackAllocationOwners[instruction.C] = mir.Places[instruction.DestPlaceId].LocalId;
                }
            }
        }

        // Pre-scan whether any pattern binding local is mutated (for shared PlaceScrutinee check)
        var isPatternMutated = new Vec<byte>(lc);
        isPatternMutated.Resize(lc, 0);
        PreScanPatternBindingMutations(in fn, in types, in mir, ref isPatternMutated);

        var blockInLoans = new Vec<BlockCarrierLoan>(64);
        var blockLoanHead = new Vec<int>(bc);
        blockLoanHead.Resize(bc, -1);
        var blockLoanTail = new Vec<int>(bc);
        blockLoanTail.Resize(bc, -1);
        var blockReached = new Vec<byte>(bc);
        blockReached.Resize(bc, 0);
        blockReached[0] = 1;

        // Seed entry parameter loans (for `in`, `ref`, `out`, `ThisShared`, `ThisMut`, or view-typed params)
        for (int i = 0; i < lc; i++)
        {
            MirLocal loc = mir.Locals[fn.LocalStart + i];
            if ((loc.Flags & MirLocalFlags.IsParam) == 0) continue;
            if (isBorrowCarrier[i] != 0 || loc.Mode != PassMode.ByValue)
            {
                bool mut = loc.Mode == PassMode.Ref || loc.Mode == PassMode.Out || loc.Mode == PassMode.ThisMut || types.IsExclusiveView(loc.TypeId);
                _ = AddBlockLoanUnique(0, i, new Loan(LoanKinds.Place, i, -1, -1, mut ? 1 : 0), ref blockLoanHead, ref blockLoanTail, ref blockInLoans);
            }
        }

        var curLoans = new Vec<CarrierLoan>(32);
        var instLiveAfter = new Vec<byte>(16);

        bool changed = true;
        int iter = 0;
        while (changed && iter < 100)
        {
            changed = false;
            iter += 1;
            for (int b = 0; b < bc; b++)
            {
                if (blockReached[b] == 0) continue;
                LoadBlockLoans(b, in blockLoanHead, in blockInLoans, ref curLoans);
                TransferBorrowBlock(b, emitDiags: false, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in isPatternMutated, in stackAllocationOwners, in liveIn, in liveOut, ref curLoans, ref instLiveAfter, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed, in names, ref rejectedMoves, ref diags);
            }
        }

        bool dummyChanged = false;
        for (int b = 0; b < bc; b++)
        {
            if (blockReached[b] == 0) continue;
            LoadBlockLoans(b, in blockLoanHead, in blockInLoans, ref curLoans);
            TransferBorrowBlock(b, emitDiags: true, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in isPatternMutated, in stackAllocationOwners, in liveIn, in liveOut, ref curLoans, ref instLiveAfter, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref dummyChanged, in names, ref rejectedMoves, ref diags);
        }
    }

    private static void PreScanPatternBindingMutations(
        in MirFunc fn,
        in TypeTable types,
        in MirModuleArena mir,
        ref Vec<byte> isPatternMutated)
    {
        for (int b = 0; b < fn.BlockCount; b++)
        {
            MirBlock blk = mir.Blocks[fn.BlockStart + b];
            for (int k = 0; k < blk.InstCount; k++)
            {
                MirInst inst = mir.Insts[blk.InstStart + k];
                if (inst.DestPlaceId >= 0)
                {
                    MirPlace dp = mir.Places[inst.DestPlaceId];
                    if (dp.ProjCount > 0) isPatternMutated[dp.LocalId] = 1;
                }
                if (inst.Op == MirOp.BindRef && (inst.Flags & 1) != 0)
                {
                    isPatternMutated[mir.Places[inst.B].LocalId] = 1;
                }
                if (inst.Op == MirOp.Call)
                {
                    for (int a = 0; a < inst.C; a++)
                    {
                        MirCallArg ca = mir.CallArgs[inst.B + a];
                        if (ca.Mode == PassMode.Ref || ca.Mode == PassMode.Out)
                        {
                            isPatternMutated[mir.Places[ca.OperandOrPlaceId].LocalId] = 1;
                        }
                        else if (ca.Mode == PassMode.ThisMut)
                        {
                            if (IsClassOwnerFunc(inst.A, in mir))
                            {
                                MirOperand rop = mir.Operands[ca.OperandOrPlaceId];
                                if (rop.Kind == MirOpKinds.CopyOrMove)
                                {
                                    isPatternMutated[mir.Places[rop.PlaceOrLitIdx].LocalId] = 1;
                                }
                            }
                            else
                            {
                                isPatternMutated[mir.Places[ca.OperandOrPlaceId].LocalId] = 1;
                            }
                        }
                    }
                }
                if (inst.Op == MirOp.Intrinsic)
                {
                    int plCount = inst.D >>> 20;
                    int plStart = inst.D & 0xFFFFF;
                    if (plCount > 0 && IsMutatingIntrinsic((MirIntrinsic)inst.A, (inst.Flags & 1) != 0))
                    {
                        isPatternMutated[mir.Places[mir.ExtraLists[plStart]].LocalId] = 1;
                    }
                }
            }
        }
    }

    private static void LoadBlockLoans(
        int blockId,
        in Vec<int> blockLoanHead,
        in Vec<BlockCarrierLoan> blockInLoans,
        ref Vec<CarrierLoan> outLoans)
    {
        outLoans.Clear();
        int idx = blockLoanHead[blockId];
        while (idx >= 0)
        {
            BlockCarrierLoan bcl = blockInLoans[idx];
            outLoans.Push(new CarrierLoan(bcl.CarrierId, bcl.LoanVal));
            idx = bcl.NextIdx;
        }
    }

    private static void JoinLoansIntoSuccessor(
        int succBlk,
        int edgeBindLocal,
        in Vec<Loan> edgeBindLoans,
        in Vec<CarrierLoan> curLoans,
        ref Vec<byte> blockReached,
        ref Vec<int> blockLoanHead,
        ref Vec<int> blockLoanTail,
        ref Vec<BlockCarrierLoan> blockInLoans,
        ref bool changed)
    {
        if (blockReached[succBlk] == 0)
        {
            blockReached[succBlk] = 1;
            changed = true;
        }
        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            if (cl.CarrierId == edgeBindLocal) continue;
            if (AddBlockLoanUnique(succBlk, cl.CarrierId, cl.LoanVal, ref blockLoanHead, ref blockLoanTail, ref blockInLoans))
            {
                changed = true;
            }
        }
        if (edgeBindLocal >= 0)
        {
            for (int i = 0; i < edgeBindLoans.Count; i++)
            {
                if (AddBlockLoanUnique(succBlk, edgeBindLocal, edgeBindLoans[i], ref blockLoanHead, ref blockLoanTail, ref blockInLoans))
                {
                    changed = true;
                }
            }
        }
    }

    private static bool AddBlockLoanUnique(
        int blockId,
        int carrierId,
        Loan l,
        ref Vec<int> blockLoanHead,
        ref Vec<int> blockLoanTail,
        ref Vec<BlockCarrierLoan> list)
    {
        int idx = blockLoanHead[blockId];
        while (idx >= 0)
        {
            BlockCarrierLoan e = list[idx];
            if (e.CarrierId == carrierId && e.LoanVal.EqualsLoan(l))
            {
                return false;
            }
            idx = e.NextIdx;
        }
        int newIdx = list.Count;
        list.Push(new BlockCarrierLoan(carrierId, l, -1));
        int prevTail = blockLoanTail[blockId];
        if (prevTail < 0)
        {
            blockLoanHead[blockId] = newIdx;
        }
        else
        {
            BlockCarrierLoan prev = list[prevTail];
            list[prevTail] = new BlockCarrierLoan(prev.CarrierId, prev.LoanVal, newIdx);
        }
        blockLoanTail[blockId] = newIdx;
        return true;
    }

    private static void RemoveCarrierEntries(int localId, bool stripPlaceRootToo, ref Vec<CarrierLoan> curLoans) => RemoveCarrierEntries(localId, stripPlaceRootToo, ref curLoans, false);

    private static void RemoveCarrierEntries(int localId, bool stripPlaceRootToo, ref Vec<CarrierLoan> curLoans, bool stripStepCursorToo)
    {
        int w = 0;
        for (int r = 0; r < curLoans.Count; r++)
        {
            CarrierLoan cl = curLoans[r];
            if (cl.CarrierId == localId) continue;
            if (stripPlaceRootToo && cl.LoanVal.Kind == LoanKinds.Place && cl.LoanVal.RootLocalId == localId)
            {
                continue;
            }
            if (stripStepCursorToo && cl.LoanVal.Kind == LoanKinds.StepCursor && cl.LoanVal.RootLocalId == localId)
            {
                continue;
            }
            if (w != r) curLoans[w] = cl;
            w += 1;
        }
        curLoans.Truncate(w);
    }

    private static void SetCarrierLoans(int carrierId, in Vec<Loan> newLoans, ref Vec<CarrierLoan> curLoans)
    {
        int w = 0;
        for (int r = 0; r < curLoans.Count; r++)
        {
            if (curLoans[r].CarrierId != carrierId)
            {
                if (w != r) curLoans[w] = curLoans[r];
                w += 1;
            }
        }
        curLoans.Truncate(w);
        for (int i = 0; i < newLoans.Count; i++)
        {
            Loan nl = newLoans[i];
            if ((nl.Kind == LoanKinds.StepCursor || nl.Kind == LoanKinds.Place) && nl.RootLocalId == carrierId)
            {
                continue;
            }
            AddCarrierLoanUnique(carrierId, nl, ref curLoans);
        }
    }

    private static void AddCarrierLoanUnique(int carrierId, Loan l, ref Vec<CarrierLoan> curLoans)
    {
        for (int i = 0; i < curLoans.Count; i++)
        {
            if (curLoans[i].CarrierId == carrierId && curLoans[i].LoanVal.EqualsLoan(l))
            {
                return;
            }
        }
        curLoans.Push(new CarrierLoan(carrierId, l));
    }

    private static void AddLoanUnique(Loan l, ref Vec<Loan> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].EqualsLoan(l)) return;
        }
        list.Push(l);
    }

    private static void TransferBorrowBlock(
        int b,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        in Vec<byte> isPatternMutated,
        in Vec<int> stackAllocationOwners,
        in Vec<byte> liveIn,
        in Vec<byte> liveOut,
        ref Vec<CarrierLoan> curLoans,
        ref Vec<byte> instLiveAfter,
        ref Vec<byte> blockReached,
        ref Vec<int> blockLoanHead,
        ref Vec<int> blockLoanTail,
        ref Vec<BlockCarrierLoan> blockInLoans,
        ref bool changed,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        int lc = fn.LocalCount;
        MirBlock blk = mir.Blocks[fn.BlockStart + b];

        // Precompute exact per-instruction liveAfter[k * lc + lid] for k = 0 .. blk.InstCount - 1
        instLiveAfter.Clear();
        instLiveAfter.Resize((blk.InstCount + 1) * lc, 0);
        var stepLive = new Vec<byte>(lc);
        stepLive.Resize(lc, 0);
        for (int i = 0; i < lc; i++)
        {
            byte lo = liveOut[b * lc + i];
            stepLive[i] = lo;
            instLiveAfter[blk.InstCount * lc + i] = lo;
        }
        MirTerm term = blk.Term;
        StepBackwardTerminatorUses(in term, in mir, ref stepLive);
        for (int k = blk.InstCount - 1; k >= 0; k--)
        {
            for (int i = 0; i < lc; i++)
            {
                instLiveAfter[k * lc + i] = stepLive[i];
            }
            MirInst backInst = mir.Insts[blk.InstStart + k];
            StepBackwardInst(in backInst, in fn, in mir, ref stepLive);
        }

        var rhsLoans = new Vec<Loan>(8);
        var argPool = new Vec<Loan>(16);
        var argSummaries = new Vec<ArgBorrowSummary>(8);
        var liveSlice = new Vec<byte>(lc);
        liveSlice.Resize(lc, 0);

        for (int k = 0; k < blk.InstCount; k++)
        {
            MirInst inst = mir.Insts[blk.InstStart + k];
            Token tok = ta.Tokens[inst.TokIdx];
            for (int i = 0; i < lc; i++)
            {
                // Foreach's hidden view is lexical, not an ordinary NLL view.
                // Forward provenance still controls activation and ScopeExit
                // removes it, including early exits and divergent body paths.
                liveSlice[i] = (mir.Locals[fn.LocalStart + i].Flags & MirLocalFlags.IsForeachLoan) != 0
                    ? (byte)1 : instLiveAfter[k * lc + i];
            }
            if (inst.Op == MirOp.ScopeExit)
            {
                for (int s = 0; s < inst.B; s++) liveSlice[mir.ExtraLists[inst.A + s]] = 0;
            }

            bool isTwoPhase = (inst.Flags & 2) != 0;

            switch (inst.Op)
            {
                case MirOp.AssignUse:
                case MirOp.AssignCast:
                case MirOp.MakeSomeWrap:
                case MirOp.MakeOkWrap:
                case MirOp.MakeErrWrap:
                case MirOp.LiftOptionSome:
                case MirOp.LiftResultOk:
                case MirOp.LiftResultErr:
                case MirOp.UnwrapWrap:
                {
                    rhsLoans.Clear();
                    int excluded = (inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount == 0) ? mir.Places[inst.DestPlaceId].LocalId : -1;
                    // A saved assignment target remains an operand until the
                    // store completes, even when the store is its final use.
                    if (inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount > 0)
                    {
                        var storeLive = new Vec<byte>(lc);
                        storeLive.Resize(lc, 0);
                        for (int i = 0; i < lc; i++) storeLive[i] = liveSlice[i];
                        MarkPlaceRead(inst.DestPlaceId, in mir, ref storeLive);
                        CheckAndRecordCallActivationMove(inst.A, false, excluded, emitDiags,
                            in fn, in ast, in ta, in names, in types, in mir, in curLoans, in storeLive, ref rejectedMoves, ref diags);
                    }
                    if (inst.Op == MirOp.AssignUse && (inst.Flags & 8) != 0)
                    {
                        MirOperand reborrow = mir.Operands[inst.A];
                        CheckBorrowCreationConflicts(reborrow.PlaceOrLitIdx, isExclusive: true, excluded, tok, emitDiags,
                            in fn, in mir, in curLoans, in liveSlice, ref diags, deferCallOperandOverlap: true);
                        ComputeLoansForPlaceBorrow(reborrow.PlaceOrLitIdx, isMut: true, in curLoans, in mir, ref rhsLoans);
                    }
                    else
                    {
                        bool operandCapture = excluded >= 0 &&
                            (mir.Locals[fn.LocalStart + excluded].Flags & MirLocalFlags.IsCallOperandLoan) != 0;
                        CheckAndCollectOperandLoansEx(inst.A, false, excluded, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags,
                            deferCallOperandOverlap: operandCapture);
                    }
                    ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.LiftOptionNone:
                {
                    rhsLoans.Clear();
                    ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.AssignBin:
                {
                    rhsLoans.Clear();
                    int excluded = (inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount == 0) ? mir.Places[inst.DestPlaceId].LocalId : -1;
                    CheckAndCollectOperandLoans(inst.B, excluded, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                    CheckAndCollectOperandLoans(inst.C, excluded, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                    rhsLoans.Clear();
                    ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.AssignUn:
                {
                    rhsLoans.Clear();
                    int excluded = (inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount == 0) ? mir.Places[inst.DestPlaceId].LocalId : -1;
                    CheckAndCollectOperandLoans(inst.B, excluded, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                    rhsLoans.Clear();
                    ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.AllocClass:
                case MirOp.ConstructStruct:
                {
                    rhsLoans.Clear();
                    int excluded = (inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount == 0) ? mir.Places[inst.DestPlaceId].LocalId : -1;
                    for (int a = 0; a < inst.B; a++)
                    {
                        CheckAndCollectOperandLoans(mir.ExtraLists[inst.A + a], excluded, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                    }
                    ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.StackSpan:
                {
                    rhsLoans.Clear();
                    for (int a = 0; a < inst.B; a++)
                    {
                        CheckAndCollectOperandLoans(mir.ExtraLists[inst.A + a], -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                    }
                    rhsLoans.Clear();
                    AddLoanUnique(new Loan(LoanKinds.StackSpan, inst.C, -1, -1, 1), ref rhsLoans);
                    ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.BindRef:
                {
                    bool isMut = (inst.Flags & 1) != 0;
                    bool reserveTwoPhase = isMut && isTwoPhase;
                    if (emitDiags && isMut && IsMutatingThroughShared(inst.B, isMethodOrRef: true, in fn, in types, in mir, isReceiverCall: reserveTwoPhase))
                    {
                        diags.ReportTok(VibCodes.MutateSharedBorrow, tok, "Cannot take a mutable reference through a shared borrow."u8);
                    }
                    bool operandCapture = (mir.Locals[fn.LocalStart + inst.A].Flags & MirLocalFlags.IsCallOperandLoan) != 0;
                    CheckBorrowCreationConflicts(inst.B, isMut && !reserveTwoPhase, inst.A, tok, emitDiags, in fn, in mir, in curLoans, in liveSlice, ref diags,
                        deferCallOperandOverlap: operandCapture);
                    rhsLoans.Clear();
                    ComputeLoansForPlaceBorrow(inst.B, isMut, in curLoans, in mir, ref rhsLoans, reserveTwoPhase, (inst.Flags & 4) != 0);
                    CheckTempRvalueEscape(inst.A, in rhsLoans, tok, emitDiags, in fn, in mir, in isBorrowCarrier, ref diags);
                    if (emitDiags && inst.A >= 0)
                    {
                        MirLocal bindDest = mir.Locals[fn.LocalStart + inst.A];
                        if ((bindDest.Flags & MirLocalFlags.IsParam) != 0 &&
                            (bindDest.Flags & MirLocalFlags.IsScoped) == 0)
                        {
                            CheckRefParamAssignEscape(in rhsLoans, tok, in fn, in types, in mir, in isBorrowCarrier, ref diags);
                        }
                    }
                    SetCarrierLoans(inst.A, in rhsLoans, ref curLoans);
                    break;
                }

                case MirOp.Call:
                {
                    HandleCallBorrows(in inst, isTwoPhase, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, ref rhsLoans, ref argPool, ref argSummaries, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.Intrinsic:
                {
                    HandleIntrinsicBorrows(in inst, isTwoPhase, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveSlice, ref curLoans, ref rhsLoans, ref argPool, ref argSummaries, in names, ref rejectedMoves, ref diags);
                    break;
                }

                case MirOp.DisposeResource:
                {
                    if (emitDiags)
                    {
                        for (int ci = 0; ci < curLoans.Count; ci++)
                        {
                            CarrierLoan cl = curLoans[ci];
                            if (cl.CarrierId == inst.A || liveSlice[cl.CarrierId] == 0) continue;
                            if (cl.LoanVal.Kind == LoanKinds.Place && cl.LoanVal.RootLocalId == inst.A)
                            {
                                diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Cannot dispose resource while borrowed."u8);
                                break;
                            }
                        }
                    }
                    break;
                }

                case MirOp.ScopeExit:
                {
                    if (emitDiags)
                    {
                        FuncInstance scopeFunction = mir.FuncInstances[fn.FuncInstId];
                        bool returningBorrow = term.Kind == MirTermKind.Return &&
                            ((scopeFunction.Flags & DeclFlags.ReturnsByRef) != 0 || types.IsView(scopeFunction.ReturnTypeId));
                        // Only the final cleanup suffix belongs to the Return.
                        // Earlier lexical scope exits keep their own conflicts.
                        for (int next = k + 1; returningBorrow && next < blk.InstCount; next++)
                        {
                            MirInst following = mir.Insts[blk.InstStart + next];
                            if (following.Op != MirOp.ScopeExit && following.Op != MirOp.DisposeResource)
                                returningBorrow = false;
                        }
                        for (int s = 0; s < inst.B; s++)
                        {
                            int exitingId = mir.ExtraLists[inst.A + s];
                            CheckScopeExitConflicts(exitingId, returningBorrow, tok, in fn, in types, in mir, in isBorrowCarrier, in stackAllocationOwners, in curLoans, in liveSlice, ref diags);
                        }
                    }
                    for (int s = 0; s < inst.B; s++)
                    {
                        int exitingId = mir.ExtraLists[inst.A + s];
                        // Parameter provenance must survive cleanup in a return
                        // snapshot so scoped/elided LifetimeOf contracts can be
                        // validated at the Return terminator.
                        bool parameterOrigin = (mir.Locals[fn.LocalStart + exitingId].Flags & MirLocalFlags.IsParam) != 0;
                        RemoveCarrierEntries(exitingId, isBorrowCarrier[exitingId] != 0 && !parameterOrigin, ref curLoans);
                    }
                    break;
                }
            }
        }

        // Terminator borrow & escape checks + propagation
        for (int i = 0; i < lc; i++)
        {
            liveSlice[i] = (mir.Locals[fn.LocalStart + i].Flags & MirLocalFlags.IsForeachLoan) != 0
                ? (byte)1 : liveOut[b * lc + i];
        }
        var emptyEdgeLoans = new Vec<Loan>();
        Token tTok = ta.Tokens[term.TokIdx];

        switch (term.Kind)
        {
            case MirTermKind.Goto:
                JoinLoansIntoSuccessor(term.A, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                break;

            case MirTermKind.Branch:
                rhsLoans.Clear();
                CheckAndCollectOperandLoans(term.A, -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                JoinLoansIntoSuccessor(term.B, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                JoinLoansIntoSuccessor(term.C, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                break;

            case MirTermKind.MatchOption:
            {
                if (term.B >= 0)
                {
                    RemoveCarrierEntries(term.B, stripPlaceRootToo: true, ref curLoans, stripStepCursorToo: true);
                }
                var someLoans = new Vec<Loan>(4);
                if ((term.Flags & 1) != 0)
                {
                    // PlaceScrutinee: Option<T> and OptionRef<T> place scrutinees always borrow shared; only OptionMut<T> (`(Flags & 6) == 6`) borrows mutably!
                    bool isOptionMutCursor = (term.Flags & 6) == 6;
                    bool mutBind = isOptionMutCursor && term.B >= 0 && isPatternMutated[term.B] != 0;
                    CheckBorrowCreationConflicts(term.A, isOptionMutCursor, term.B, tTok, emitDiags, in fn, in mir, in curLoans, in liveSlice, ref diags);
                    if ((term.Flags & 4) != 0 && mir.Places[term.A].ProjCount == 0)
                    {
                        // Cursor place (`while (cur is Some<Node>(var n))`): attach StepCursor loan on `cur`!
                        int curLid = mir.Places[term.A].LocalId;
                        CopyLoansOfCarrier(curLid, in curLoans, ref someLoans);
                        AddLoanUnique(new Loan(LoanKinds.StepCursor, curLid, -1, -1, isOptionMutCursor ? 1 : 0), ref someLoans);
                    }
                    else
                    {
                        ComputeLoansForPlaceBorrow(term.A, mutBind, in curLoans, in mir, ref someLoans);
                    }
                }
                else
                {
                    CheckAndCollectOperandLoans(term.A, -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref someLoans, in names, ref rejectedMoves, ref diags);
                }
                JoinLoansIntoSuccessor(term.D, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                JoinLoansIntoSuccessor(term.C, (term.B >= 0 && isBorrowCarrier[term.B] != 0) ? term.B : -1, in someLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                break;
            }

            case MirTermKind.MatchResult:
            {
                var rLoans = new Vec<Loan>(4);
                if ((term.Flags & 1) != 0)
                {
                    // Result<T, E> place scrutinees always borrow shared (`!Copy` bindings carry `IsSharedBind`)
                    CheckBorrowCreationConflicts(term.A, isExclusive: false, -1, tTok, emitDiags, in fn, in mir, in curLoans, in liveSlice, ref diags);
                    ComputeLoansForPlaceBorrow(term.A, isMut: false, in curLoans, in mir, ref rLoans);
                }
                else
                {
                    CheckAndCollectOperandLoans(term.A, -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rLoans, in names, ref rejectedMoves, ref diags);
                }
                JoinLoansIntoSuccessor(term.C, (term.B >= 0 && isBorrowCarrier[term.B] != 0) ? term.B : -1, in rLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                JoinLoansIntoSuccessor(term.E, (term.D >= 0 && isBorrowCarrier[term.D] != 0) ? term.D : -1, in rLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                break;
            }

            case MirTermKind.GuardResultIsErr:
            case MirTermKind.GuardOptionIsNone:
            {
                rhsLoans.Clear();
                CheckAndCollectOperandLoans(term.A, -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveSlice, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                if (term.Kind == MirTermKind.GuardResultIsErr)
                {
                    JoinLoansIntoSuccessor(term.D, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                    JoinLoansIntoSuccessor(term.E, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                }
                else
                {
                    JoinLoansIntoSuccessor(term.C, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                    JoinLoansIntoSuccessor(term.D, -1, in emptyEdgeLoans, in curLoans, ref blockReached, ref blockLoanHead, ref blockLoanTail, ref blockInLoans, ref changed);
                }
                break;
            }

            case MirTermKind.Return:
            {
                rhsLoans.Clear();
                FuncInstance fnInst = mir.FuncInstances[fn.FuncInstId];
                bool retRefMut = (fnInst.Flags & DeclFlags.ReturnRefMut) != 0;
                var emptyLive = new Vec<byte>(lc);
                emptyLive.Resize(lc, 0);

                if (term.B >= 0)
                {
                    if (emitDiags && retRefMut && IsMutatingThroughShared(term.B, isMethodOrRef: true, in fn, in types, in mir))
                    {
                        diags.ReportTok(VibCodes.MutateSharedBorrow, tTok, "Cannot return mutable reference through a shared borrow."u8);
                    }
                    CheckBorrowCreationConflicts(term.B, retRefMut, -1, tTok, emitDiags, in fn, in mir, in curLoans, in emptyLive, ref diags);
                    ComputeLoansForPlaceBorrow(term.B, retRefMut, in curLoans, in mir, ref rhsLoans);
                }
                else if (term.A >= 0)
                {
                    CheckAndCollectOperandLoans(term.A, -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in emptyLive, ref rhsLoans, in names, ref rejectedMoves, ref diags);
                }

                bool returnsBorrowOrView = (fnInst.Flags & DeclFlags.ReturnsByRef) != 0 || types.IsView(fnInst.ReturnTypeId);
                if (emitDiags && returnsBorrowOrView)
                {
                    CheckReturnEscapeLoans(in rhsLoans, tTok, in fn, in types, in mir, in isBorrowCarrier, ref diags);
                }
                break;
            }
        }
    }

    private static void HandleCallBorrows(
        in MirInst inst,
        bool isTwoPhase,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        in Vec<byte> liveAfter,
        ref Vec<CarrierLoan> curLoans,
        ref Vec<Loan> rhsLoans,
        ref Vec<Loan> argPool,
        ref Vec<ArgBorrowSummary> argSummaries,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        FuncInstance callee = mir.FuncInstances[inst.A];
        bool calleeReturnsBorrow = (callee.Flags & DeclFlags.ReturnsByRef) != 0 || types.IsView(callee.ReturnTypeId);
        int destCarrier = inst.RefDestLocalId >= 0
            ? inst.RefDestLocalId
            : ((inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount == 0) ? mir.Places[inst.DestPlaceId].LocalId : -1);

        argPool.Clear();
        argSummaries.Clear();
        rhsLoans.Clear();

        // Activation retains all captures, independently of formal parameter order.
        // Ordinary borrow/pairwise checks keep their existing two-phase liveness.
        var activationLive = new Vec<byte>(fn.LocalCount);
        activationLive.Resize(fn.LocalCount, 0);
        for (int i = 0; i < fn.LocalCount; i++) activationLive[i] = liveAfter[i];
        for (int a = 0; a < inst.C; a++)
        {
            MirCallArg argument = mir.CallArgs[inst.B + a];
            if (argument.Mode == PassMode.ByValue ||
                ((argument.Mode == PassMode.ThisShared || argument.Mode == PassMode.ThisMut) && IsClassOwnerFunc(inst.A, in mir)))
                MarkOperandRead(argument.OperandOrPlaceId, in mir, ref activationLive);
            else
                MarkPlaceRead(argument.OperandOrPlaceId, in mir, ref activationLive);
        }

        var remLive = new Vec<byte>(fn.LocalCount);
        remLive.Resize(fn.LocalCount, 0);

        for (int a = 0; a < inst.C; a++)
        {
            for (int i = 0; i < fn.LocalCount; i++) remLive[i] = liveAfter[i];
            for (int j = a + 1; j < inst.C; j++)
            {
                MirCallArg later = mir.CallArgs[inst.B + j];
                if (later.Mode == PassMode.ByValue || ((later.Mode == PassMode.ThisShared || later.Mode == PassMode.ThisMut) && IsClassOwnerFunc(inst.A, in mir)))
                {
                    MarkOperandRead(later.OperandOrPlaceId, in mir, ref remLive);
                }
                else if (later.Mode != PassMode.Out)
                {
                    MarkPlaceRead(later.OperandOrPlaceId, in mir, ref remLive);
                }
            }

            MirCallArg ca = mir.CallArgs[inst.B + a];
            Token argTok = ta.Tokens[ca.TokIdx];
            MirParamSig psig = a < callee.ParamSigCount ? mir.ParamSigs[callee.ParamSigStart + a] : new MirParamSig(-1, TypeIds.I32, ca.Mode, 0);
            bool paramIsScoped = (psig.Flags & 1) != 0;
            bool paramIsUnscopedRecv = (psig.Flags & 2) != 0;

            var thisArgLoans = new Vec<Loan>(4);
            bool argIsExclusive = false;

            if (ca.Mode == PassMode.ByValue)
            {
                MirOperand cop = mir.Operands[ca.OperandOrPlaceId];
                bool isSpanReborrow = types.Items[cop.TypeId].Kind == TypeKinds.Span;
                argIsExclusive = types.IsExclusiveView(cop.TypeId);
                if (isSpanReborrow && cop.Kind == MirOpKinds.CopyOrMove)
                {
                    // Reborrowing Span<T> as a Span<T> call argument freezes parent Span<T> and conflicts with active sub-slices!
                    CheckBorrowCreationConflicts(cop.PlaceOrLitIdx, isExclusive: true, destCarrier, argTok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags);
                    ComputeLoansForPlaceBorrow(cop.PlaceOrLitIdx, isMut: true, in curLoans, in mir, ref thisArgLoans);
                }
                else
                {
                    CheckAndRecordCallActivationMove(ca.OperandOrPlaceId, isSpanReborrow, destCarrier, emitDiags,
                        in fn, in ast, in ta, in names, in types, in mir, in curLoans, in activationLive, ref rejectedMoves, ref diags);
                    CheckAndCollectOperandLoansEx(ca.OperandOrPlaceId, isSpanReborrow, destCarrier, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in remLive, ref thisArgLoans, in names, ref rejectedMoves, ref diags);
                }
            }
            else if ((ca.Mode == PassMode.ThisShared || ca.Mode == PassMode.ThisMut) && IsClassOwnerFunc(inst.A, in mir))
            {
                // Class receiver passed as operand
                argIsExclusive = ca.Mode == PassMode.ThisMut;
                MirOperand rop = mir.Operands[ca.OperandOrPlaceId];
                if (rop.Kind == MirOpKinds.CopyOrMove)
                {
                    if (emitDiags && argIsExclusive && IsMutatingThroughShared(rop.PlaceOrLitIdx, isMethodOrRef: true, in fn, in types, in mir, isReceiverCall: true))
                    {
                        diags.ReportTok(VibCodes.MutateSharedBorrow, argTok, "Cannot call [Mut] method through shared borrow."u8);
                    }
                    CheckBorrowCreationConflicts(rop.PlaceOrLitIdx, argIsExclusive, destCarrier, argTok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags);
                    MirPlace rpl = mir.Places[rop.PlaceOrLitIdx];
                    if (argIsExclusive && rpl.ProjCount > 0 && mir.Projs[rpl.ProjStart].Kind == MirProjKinds.Deref)
                    {
                        ActivateCarrierLoans(rpl.LocalId, ref curLoans);
                    }
                    ComputeLoansForPlaceBorrow(rop.PlaceOrLitIdx, argIsExclusive, in curLoans, in mir, ref thisArgLoans);
                }
            }
            else
            {
                // Struct receiver or `in` / `ref` / `out` place
                argIsExclusive = ca.Mode == PassMode.Ref || ca.Mode == PassMode.Out || ca.Mode == PassMode.ThisMut;
                bool isRecv = ca.Mode == PassMode.ThisShared || ca.Mode == PassMode.ThisMut;
                if (emitDiags && argIsExclusive && IsMutatingThroughShared(ca.OperandOrPlaceId, isMethodOrRef: true, in fn, in types, in mir, isReceiverCall: isRecv))
                {
                    diags.ReportTok(VibCodes.MutateSharedBorrow, argTok, "Cannot pass shared borrow as mutable reference or receiver."u8);
                }
                if (ca.Mode == PassMode.Out)
                {
                    var emptyOutLoans = new Vec<Loan>();
                    ApplyBorrowWriteToPlace(ca.OperandOrPlaceId, in emptyOutLoans, argTok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveAfter, ref curLoans, in names, ref rejectedMoves, ref diags);
                }
                else
                {
                    CheckBorrowCreationConflicts(ca.OperandOrPlaceId, argIsExclusive, destCarrier, argTok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags);
                    MirPlace apl = mir.Places[ca.OperandOrPlaceId];
                    if (argIsExclusive && apl.ProjCount > 0 && mir.Projs[apl.ProjStart].Kind == MirProjKinds.Deref)
                    {
                        ActivateCarrierLoans(apl.LocalId, ref curLoans);
                    }
                }
                ComputeLoansForPlaceBorrow(ca.OperandOrPlaceId, argIsExclusive, in curLoans, in mir, ref thisArgLoans);
            }

            if (thisArgLoans.Count > 0)
            {
                int lStart = argPool.Count;
                for (int i = 0; i < thisArgLoans.Count; i++) argPool.Push(thisArgLoans[i]);
                argSummaries.Push(new ArgBorrowSummary(argIsExclusive ? 1 : 0, lStart, thisArgLoans.Count, ca.TokIdx));
            }

            // Propagate non-scoped argument loans to returned view/ref!
            if (calleeReturnsBorrow)
            {
                bool canFlowToReturn = !paramIsScoped;
                if ((ca.Mode == PassMode.ThisShared || ca.Mode == PassMode.ThisMut) &&
                    callee.OwnerTypeId >= 0 &&
                    types.Items[callee.OwnerTypeId].Kind == TypeKinds.Struct &&
                    !paramIsUnscopedRecv &&
                    (ast.Funcs[callee.FuncDeclId].Flags & DeclFlags.HasReturnLifetimeAttr) == 0 &&
                    !StructContainsIndirectOrViewField(callee.OwnerTypeId, in types, 0))
                {
                    // A struct receiver without [UnscopedRef] cannot flow into the
                    // returned borrow unless it reaches storage behind an
                    // indirection (a class object, a Vec buffer or a view), which
                    // its members may return tied to `this` (SPEC §3.3).
                    canFlowToReturn = false;
                }
                if (canFlowToReturn)
                {
                    bool retMut = (callee.Flags & DeclFlags.ReturnRefMut) != 0 || types.IsExclusiveView(callee.ReturnTypeId);
                    for (int i = 0; i < thisArgLoans.Count; i++)
                    {
                        Loan ln = thisArgLoans[i];
                        AddLoanUnique(new Loan(ln.Kind, ln.RootLocalId, ln.Field0, ln.Field1, retMut ? 1 : ln.IsMut, 0), ref rhsLoans);
                    }
                }
            }
        }

        _ = isTwoPhase;
        if (emitDiags)
        {
            CheckPairwiseCallOverlap(in argPool, in argSummaries, in ta, ref diags);
        }

        if (inst.RefDestLocalId >= 0)
        {
            CheckTempRvalueEscape(inst.RefDestLocalId, in rhsLoans, tok, emitDiags, in fn, in mir, in isBorrowCarrier, ref diags);
            SetCarrierLoans(inst.RefDestLocalId, in rhsLoans, ref curLoans);
        }
        else if (inst.DestPlaceId >= 0)
        {
            ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveAfter, ref curLoans, in names, ref rejectedMoves, ref diags);
        }
    }

    private static void HandleIntrinsicBorrows(
        in MirInst inst,
        bool isTwoPhase,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        in Vec<byte> liveAfter,
        ref Vec<CarrierLoan> curLoans,
        ref Vec<Loan> rhsLoans,
        ref Vec<Loan> argPool,
        ref Vec<ArgBorrowSummary> argSummaries,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        MirIntrinsic intr = (MirIntrinsic)inst.A;
        bool refMut = (inst.Flags & 1) != 0;
        int plCount = inst.D >>> 20;
        int plStart = inst.D & 0xFFFFF;
        int destCarrier = inst.RefDestLocalId >= 0
            ? inst.RefDestLocalId
            : ((inst.DestPlaceId >= 0 && mir.Places[inst.DestPlaceId].ProjCount == 0) ? mir.Places[inst.DestPlaceId].LocalId : -1);

        bool operandViewPreparation = destCarrier >= 0 &&
            mir.Locals[fn.LocalStart + destCarrier].CallActivationId >= 0 &&
            (MirBorrowProjections.FromPlace(intr) || MirBorrowProjections.FromValue(intr));

        argPool.Clear();
        argSummaries.Clear();
        rhsLoans.Clear();

        var liveDuringIntr = new Vec<byte>(fn.LocalCount);
        liveDuringIntr.Resize(fn.LocalCount, 0);
        for (int i = 0; i < fn.LocalCount; i++) liveDuringIntr[i] = liveAfter[i];
        for (int p = 0; p < plCount; p++) MarkPlaceRead(mir.ExtraLists[plStart + p], in mir, ref liveDuringIntr);
        for (int v = 0; v < inst.C; v++) MarkOperandRead(mir.ExtraLists[inst.B + v], in mir, ref liveDuringIntr);

        var remLive = new Vec<byte>(fn.LocalCount);
        remLive.Resize(fn.LocalCount, 0);
        for (int v = 0; v < inst.C; v++)
        {
            for (int i = 0; i < fn.LocalCount; i++) remLive[i] = liveAfter[i];
            for (int p = 0; p < plCount; p++) MarkPlaceRead(mir.ExtraLists[plStart + p], in mir, ref remLive);
            for (int j = v + 1; j < inst.C; j++) MarkOperandRead(mir.ExtraLists[inst.B + j], in mir, ref remLive);

            int vOp = mir.ExtraLists[inst.B + v];
            var vLoans = new Vec<Loan>(4);
            CheckAndRecordCallActivationMove(vOp, IsNonConsumingValueIntrinsic(intr), destCarrier, emitDiags,
                in fn, in ast, in ta, in names, in types, in mir, in curLoans, in liveDuringIntr, ref rejectedMoves, ref diags);
            CheckAndCollectOperandLoansEx(vOp, IsNonConsumingValueIntrinsic(intr), destCarrier, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in remLive, ref vLoans, in names, ref rejectedMoves, ref diags, deferCallOperandOverlap: operandViewPreparation);

            if (v == 0 && (intr == MirIntrinsic.SpanIndexRef || intr == MirIntrinsic.SpanSlice))
            {
                bool spanExcl = intr == MirIntrinsic.SpanIndexRef ? refMut : types.IsExclusiveView(inst.TypeId);
                MirOperand vop0 = mir.Operands[vOp];
                if (vop0.Kind == MirOpKinds.CopyOrMove)
                {
                    bool reserveSpanRef = intr == MirIntrinsic.SpanIndexRef && spanExcl && isTwoPhase;
                    CheckBorrowCreationConflicts(vop0.PlaceOrLitIdx, spanExcl && !reserveSpanRef, destCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags, deferCallOperandOverlap: operandViewPreparation);
                    MirPlace spl0 = mir.Places[vop0.PlaceOrLitIdx];
                    if (spanExcl && !reserveSpanRef && spl0.ProjCount > 0 && mir.Projs[spl0.ProjStart].Kind == MirProjKinds.Deref)
                    {
                        ActivateCarrierLoans(spl0.LocalId, ref curLoans);
                    }
                    var spBorrowLoans = new Vec<Loan>(4);
                    ComputeLoansForPlaceBorrow(vop0.PlaceOrLitIdx, spanExcl, in curLoans, in mir, ref spBorrowLoans, reserveSpanRef);
                    for (int i = 0; i < spBorrowLoans.Count; i++) AddLoanUnique(spBorrowLoans[i], ref rhsLoans);
                }
            }
            else if (v == 0 && intr == MirIntrinsic.SpanToReadOnly)
            {
                MirOperand vop = mir.Operands[vOp];
                if (vop.Kind == MirOpKinds.CopyOrMove)
                {
                    CheckBorrowCreationConflicts(vop.PlaceOrLitIdx, isExclusive: false, destCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags, deferCallOperandOverlap: operandViewPreparation);
                    var spBorrowLoans = new Vec<Loan>(4);
                    ComputeLoansForPlaceBorrow(vop.PlaceOrLitIdx, isMut: false, in curLoans, in mir, ref spBorrowLoans);
                    for (int i = 0; i < spBorrowLoans.Count; i++) AddLoanUnique(spBorrowLoans[i], ref rhsLoans);
                }
            }
        }

        if (plCount > 0)
        {
            int recvPl = mir.ExtraLists[plStart];
            bool mutRecv = IsMutatingIntrinsic(intr, refMut);
            bool reserveVecIdx = intr == MirIntrinsic.VecIndexRef && mutRecv && isTwoPhase;
            if (emitDiags && mutRecv && IsMutatingThroughShared(recvPl, isMethodOrRef: true, in fn, in types, in mir, isReceiverCall: true))
            {
                diags.ReportTok(VibCodes.MutateSharedBorrow, tok, "Cannot call mutating operation through a shared borrow."u8);
            }

            if (intr == MirIntrinsic.OptionMutSet || intr == MirIntrinsic.OptionMutTake || intr == MirIntrinsic.OptionMutReplace)
            {
                CheckCursorMutationConflicts(recvPl, destCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags);
            }

            bool checkWithValueArgs = mutRecv && !IntrinsicPropagatesPlaceLoan(intr);
            if (checkWithValueArgs)
            {
                CheckBorrowCreationConflicts(recvPl, isExclusive: true, destCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveDuringIntr, ref diags, deferCallOperandOverlap: operandViewPreparation);
            }
            else
            {
                CheckBorrowCreationConflicts(recvPl, isExclusive: mutRecv && !reserveVecIdx, destCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags, deferCallOperandOverlap: operandViewPreparation);
            }

            MirPlace rpl = mir.Places[recvPl];
            if (mutRecv && !reserveVecIdx && rpl.ProjCount > 0 && mir.Projs[rpl.ProjStart].Kind == MirProjKinds.Deref)
            {
                ActivateCarrierLoans(rpl.LocalId, ref curLoans);
            }

            var recvLoans = new Vec<Loan>(4);
            ComputeLoansForPlaceBorrow(recvPl, mutRecv, in curLoans, in mir, ref recvLoans, reserveVecIdx);


            if (IntrinsicPropagatesPlaceLoan(intr))
            {
                for (int i = 0; i < recvLoans.Count; i++) AddLoanUnique(recvLoans[i], ref rhsLoans);
            }
        }


        if (inst.RefDestLocalId >= 0)
        {
            CheckTempRvalueEscape(inst.RefDestLocalId, in rhsLoans, tok, emitDiags, in fn, in mir, in isBorrowCarrier, ref diags);
            SetCarrierLoans(inst.RefDestLocalId, in rhsLoans, ref curLoans);
        }
        else if (inst.DestPlaceId >= 0)
        {
            ApplyBorrowWriteToPlace(inst.DestPlaceId, in rhsLoans, tok, emitDiags, in fn, in ast, in ta, in types, in mir, in isBorrowCarrier, in liveAfter, ref curLoans, in names, ref rejectedMoves, ref diags);
        }
    }

    private static void StripStepCursorLoans(ref Vec<Loan> loans)
    {
        int w = 0;
        for (int r = 0; r < loans.Count; r++)
        {
            if (loans[r].Kind != LoanKinds.StepCursor)
            {
                if (w != r) loans[w] = loans[r];
                w += 1;
            }
        }
        loans.Truncate(w);
    }

    private static bool IsMutatingIntrinsic(MirIntrinsic intr, bool refDestIsMut)
    {
        switch (intr)
        {
            case MirIntrinsic.OptionAsMut:
            case MirIntrinsic.OptionTake:
            case MirIntrinsic.OptionReplace:
            case MirIntrinsic.OptionUnwrapMut:
            case MirIntrinsic.OptionMutSet:
            case MirIntrinsic.OptionMutTake:
            case MirIntrinsic.OptionMutReplace:
            case MirIntrinsic.VecPush:
            case MirIntrinsic.VecPop:
            case MirIntrinsic.VecClear:
            case MirIntrinsic.VecTruncate:
            case MirIntrinsic.VecReserve:
            case MirIntrinsic.VecExtend:
            case MirIntrinsic.VecResize:
            case MirIntrinsic.VecSliceMut:
            case MirIntrinsic.VecAsSpan:
                return true;
            case MirIntrinsic.VecIndexRef:
                return refDestIsMut;
            default:
                return false;
        }
    }

    private static bool IntrinsicPropagatesPlaceLoan(MirIntrinsic intr) => MirBorrowProjections.FromPlace(intr);

    private static bool IntrinsicPropagatesValueLoan(MirIntrinsic intr) => MirBorrowProjections.FromValue(intr);

    private static void CheckPairwiseCallOverlap(
        in Vec<Loan> pool,
        in Vec<ArgBorrowSummary> summaries,
        in TokenArena ta,
        ref DiagnosticBag diags)
    {
        for (int i = 0; i < summaries.Count; i++)
        {
            ArgBorrowSummary si = summaries[i];
            for (int j = i + 1; j < summaries.Count; j++)
            {
                ArgBorrowSummary sj = summaries[j];
                if (si.IsMut == 0 && sj.IsMut == 0) continue;
                if (SummaryLoansOverlap(in pool, si, sj))
                {
                    diags.ReportTok(VibCodes.OverlappingCallBorrows, ta.Tokens[sj.TokIdx], "Overlapping borrows in a single call where at least one borrow is exclusive."u8);
                    return;
                }
            }
        }
    }

    private static bool SummaryLoansOverlap(in Vec<Loan> pool, ArgBorrowSummary a, ArgBorrowSummary b)
    {
        for (int i = 0; i < a.LoanCount; i++)
        {
            Loan la = pool[a.LoanStart + i];
            for (int j = 0; j < b.LoanCount; j++)
            {
                Loan lb = pool[b.LoanStart + j];
                if (la.Kind == LoanKinds.StackSpan && lb.Kind == LoanKinds.StackSpan && la.RootLocalId == lb.RootLocalId)
                {
                    return true;
                }
                if (la.Kind == LoanKinds.Place && lb.Kind == LoanKinds.Place &&
                    PathsOverlap(la.RootLocalId, la.Field0, la.Field1, lb.RootLocalId, lb.Field0, lb.Field1))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool PathsOverlap(int r1, int f10, int f11, int r2, int f20, int f21)
    {
        if (r1 != r2) return false;
        if (f10 < 0 || f20 < 0) return true;
        if (f10 != f20) return false;
        if (f11 < 0 || f21 < 0) return true;
        return f11 == f21;
    }

    private static bool IsMutatingThroughShared(
        int placeId,
        bool isMethodOrRef,
        in MirFunc fn,
        in TypeTable types,
        in MirModuleArena mir) =>
        IsMutatingThroughShared(placeId, isMethodOrRef, in fn, in types, in mir, false);

    private static bool IsMutatingThroughShared(
        int placeId,
        bool isMethodOrRef,
        in MirFunc fn,
        in TypeTable types,
        in MirModuleArena mir,
        bool isReceiverCall)
    {
        MirPlace pl = mir.Places[placeId];
        MirLocal root = mir.Locals[fn.LocalStart + pl.LocalId];

        if ((root.Flags & MirLocalFlags.IsSharedBind) != 0)
        {
            return true;
        }
        if ((root.Flags & MirLocalFlags.IsBorrowedBind) != 0 && pl.ProjCount == 0 && !isReceiverCall)
        {
            return true;
        }

        bool rootIsSharedRef =
            root.Mode == PassMode.In ||
            ((root.Flags & MirLocalFlags.IsRefLocal) != 0 && (root.Flags & MirLocalFlags.IsRefMut) == 0);

        if (rootIsSharedRef && (pl.ProjCount > 0 || isMethodOrRef))
        {
            return true;
        }

        if (root.Mode == PassMode.ThisShared && (pl.ProjCount > 0 || isMethodOrRef))
        {
            return true;
        }

        if (types.Items[root.TypeId].Kind == TypeKinds.ReadOnlySpan || types.Items[root.TypeId].Kind == TypeKinds.OptionRef ||
            types.Items[root.TypeId].Kind == TypeKinds.Utf8Lines || types.Items[root.TypeId].Kind == TypeKinds.Utf8ArgEnum)
        {
            if (pl.ProjCount > 0 || isMethodOrRef) return true;
        }

        // Readonly value fields remain readonly through value projections, but
        // crossing into a class referent starts a new mutable storage location.
        bool readonlyDestination = false;
        for (int i = 0; i < pl.ProjCount; i++)
        {
            MirProj pr = mir.Projs[pl.ProjStart + i];
            if ((pr.Flags & 1) != 0) readonlyDestination = false;
            if (pr.Kind == MirProjKinds.Field && (pr.Flags & 2) != 0) readonlyDestination = true;
        }
        if (readonlyDestination)
        {
            MirProj last = mir.Projs[pl.ProjStart + pl.ProjCount - 1];
            if (!isMethodOrRef || types.Items[last.ResultTypeId].Kind != TypeKinds.Class) return true;
        }

        return false;
    }

    private static void CopyLoansOfCarrier(int carrierId, in Vec<CarrierLoan> curLoans, ref Vec<Loan> outLoans)
    {
        for (int i = 0; i < curLoans.Count; i++)
        {
            if (curLoans[i].CarrierId == carrierId)
            {
                AddLoanUnique(curLoans[i].LoanVal, ref outLoans);
            }
        }
    }

    private static void ActivateCarrierLoans(int carrierId, ref Vec<CarrierLoan> curLoans)
    {
        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            if (cl.CarrierId == carrierId && cl.LoanVal.IsReserved != 0)
            {
                curLoans[i] = new CarrierLoan(carrierId, cl.LoanVal.AsActivated());
            }
        }
    }

    private static void ComputeLoansForPlaceBorrow(
        int placeId,
        bool isMut,
        in Vec<CarrierLoan> curLoans,
        in MirModuleArena mir,
        ref Vec<Loan> outLoans) =>
        ComputeLoansForPlaceBorrow(placeId, isMut, in curLoans, in mir, ref outLoans, false, false);

    private static void ComputeLoansForPlaceBorrow(
        int placeId,
        bool isMut,
        in Vec<CarrierLoan> curLoans,
        in MirModuleArena mir,
        ref Vec<Loan> outLoans,
        bool isReserved) =>
        ComputeLoansForPlaceBorrow(placeId, isMut, in curLoans, in mir, ref outLoans, isReserved, false);

    private static void ComputeLoansForPlaceBorrow(
        int placeId,
        bool isMut,
        in Vec<CarrierLoan> curLoans,
        in MirModuleArena mir,
        ref Vec<Loan> outLoans,
        bool isReserved,
        bool isAssignmentTargetSnapshot)
    {
        MirPlace pl = mir.Places[placeId];
        int f0 = -1;
        int f1 = -1;
        bool startsWithDeref = pl.ProjCount > 0 && mir.Projs[pl.ProjStart].Kind == MirProjKinds.Deref;

        int projIdx = startsWithDeref ? 1 : 0;
        if (projIdx < pl.ProjCount && mir.Projs[pl.ProjStart + projIdx].Kind == MirProjKinds.Field)
        {
            f0 = mir.Projs[pl.ProjStart + projIdx].FieldIdx;
            projIdx += 1;
            if (projIdx < pl.ProjCount && mir.Projs[pl.ProjStart + projIdx].Kind == MirProjKinds.Field)
            {
                f1 = mir.Projs[pl.ProjStart + projIdx].FieldIdx;
            }
        }

        int resBit = (isMut && isReserved) ? 1 : 0;
        for (int i = 0; i < curLoans.Count; i++)
        {
            if (curLoans[i].CarrierId != pl.LocalId) continue;
            Loan baseLoan = curLoans[i].LoanVal;
            if (baseLoan.Kind == LoanKinds.Place)
            {
                int nf0 = baseLoan.Field0;
                int nf1 = baseLoan.Field1;
                if (nf0 < 0 && f0 >= 0) { nf0 = f0; nf1 = f1; }
                else if (nf0 >= 0 && nf1 < 0 && f0 >= 0) { nf1 = f0; }
                int outMut = isMut ? 1 : 0;
                AddLoanUnique(new Loan(LoanKinds.Place, baseLoan.RootLocalId, nf0, nf1, outMut, resBit), ref outLoans);
            }
            else
            {
                AddLoanUnique(baseLoan, ref outLoans);
            }
        }

        if (isAssignmentTargetSnapshot && startsWithDeref && outLoans.Count > 0)
        {
            // Keep all inherited owner/stack/cursor loans, but record the carrier only as
            // ancestry: rebinding it must not change or invalidate the saved destination.
            AddLoanUnique(new Loan(LoanKinds.SnapshotOrigin, pl.LocalId, -1, -1, 0), ref outLoans);
        }
        else
        {
            AddLoanUnique(new Loan(LoanKinds.Place, pl.LocalId, f0, f1, isMut ? 1 : 0, resBit), ref outLoans);
        }
    }

    private static bool IsNonConsumingValueIntrinsic(MirIntrinsic intr) =>
        intr == MirIntrinsic.SpanLength ||
        intr == MirIntrinsic.SpanIndexRef ||
        intr == MirIntrinsic.SpanSlice ||
        intr == MirIntrinsic.SpanToReadOnly ||
        intr == MirIntrinsic.OptionIsSome ||
        intr == MirIntrinsic.ResultIsOk ||
        intr == MirIntrinsic.VecCount ||
        intr == MirIntrinsic.VecCapacity ||
        intr == MirIntrinsic.VecAsSpan ||
        intr == MirIntrinsic.VecAsReadOnlySpan ||
        intr == MirIntrinsic.Utf8Scan;

    private static void CheckAndCollectOperandLoans(
        int opId,
        int excludedCarrier,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        ref Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref Vec<Loan> outLoans,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags) =>
        CheckAndCollectOperandLoansEx(opId, forceCopy: false, excludedCarrier, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveAfter, ref outLoans, in names, ref rejectedMoves, ref diags);

    private static void CheckAndCollectOperandLoansEx(
        int opId,
        bool forceCopy,
        int excludedCarrier,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        ref Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref Vec<Loan> outLoans,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags) =>
        CheckAndCollectOperandLoansEx(opId, forceCopy, excludedCarrier, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveAfter, ref outLoans, in names, ref rejectedMoves, ref diags, false);

    private static void CheckAndCollectOperandLoansEx(
        int opId,
        bool forceCopy,
        int excludedCarrier,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        ref Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref Vec<Loan> outLoans,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags,
        bool deferCallOperandOverlap)
    {
        if (opId < 0) return;
        MirOperand op = mir.Operands[opId];
        if (op.Kind != MirOpKinds.CopyOrMove) return;

        Token tok = ta.Tokens[op.TokIdx];
        int plId = op.PlaceOrLitIdx;
        MirPlace pl = mir.Places[plId];

        // Check index operands inside `pl`
        for (int i = 0; i < pl.ProjCount; i++)
        {
            MirProj pr = mir.Projs[pl.ProjStart + i];
            if (pr.Kind == MirProjKinds.Index && pr.IndexOpId >= 0)
            {
                var dummy = new Vec<Loan>();
                CheckAndCollectOperandLoans(pr.IndexOpId, excludedCarrier, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveAfter, ref dummy, in names, ref rejectedMoves, ref diags);
            }
        }

        bool isMove = !forceCopy && !types.IsCopy(op.TypeId, in ast);
        if (isMove)
        {
            // Detect rejection during the fixed point as well as the reporting
            // sweep, so a failed move cannot discard carrier provenance.
            if (CheckCallActivationMove(plId, excludedCarrier, ta.Tokens[op.SourceStartTokIdx], emitDiags,
                out int rejectedOwner, in fn, in names, in types, in mir, in curLoans, in liveAfter, ref diags))
            {
                rejectedMoves[opId] = rejectedOwner + 1;
            }
            else if (rejectedMoves[opId] <= 0 &&
                CheckWriteOrMoveConflicts(plId, isMove: true, excludedCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags))
            {
                rejectedMoves[opId] = -(pl.LocalId + 1);
            }
        }
        else
        {
            // Shared read: conflicts only with active EXCLUSIVE loans!
            CheckBorrowCreationConflicts(plId, isExclusive: false, excludedCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags, deferCallOperandOverlap);
        }

        if (pl.ProjCount == 0)
        {
            CopyLoansOfCarrier(pl.LocalId, in curLoans, ref outLoans);
        }
        else if (types.IsView(op.TypeId))
        {
            ComputeLoansForPlaceBorrow(plId, types.IsExclusiveView(op.TypeId), in curLoans, in mir, ref outLoans);
        }

        if (isMove && pl.ProjCount == 0 && rejectedMoves[opId] == 0)
        {
            MirLocal source = mir.Locals[fn.LocalStart + pl.LocalId];
            bool borrowedParameter = (source.Flags & MirLocalFlags.IsParam) != 0 && source.Mode != PassMode.ByValue;
            if (!borrowedParameter && (source.Flags & (MirLocalFlags.IsBorrowedBind | MirLocalFlags.IsUsingVar | MirLocalFlags.IsCallOperandLoan)) == 0)
            {
                // Provenance moves with the value. A later use of the moved
                // carrier is an initialization error, not a fresh activation of
                // its former loans. Keep other carriers' owner roots intact.
                // Hidden call captures are different: their loans remain live
                // through activation of every operand, not just their own turn
                // in formal-parameter order. Their existing liveness ends them.
                RemoveCarrierEntries(pl.LocalId, stripPlaceRootToo: false, ref curLoans);
            }
        }
    }

    private static void ApplyBorrowWriteToPlace(
        int destPlaceId,
        in Vec<Loan> rhsLoans,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        in Vec<byte> liveAfter,
        ref Vec<CarrierLoan> curLoans,
        in Interner names,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        if (destPlaceId < 0) return;
        MirPlace dpl = mir.Places[destPlaceId];

        for (int i = 0; i < dpl.ProjCount; i++)
        {
            MirProj pr = mir.Projs[dpl.ProjStart + i];
            if (pr.Kind == MirProjKinds.Index && pr.IndexOpId >= 0)
            {
                var dummy = new Vec<Loan>();
                CheckAndCollectOperandLoans(pr.IndexOpId, -1, emitDiags, in fn, in ast, in ta, in types, in mir, ref curLoans, in liveAfter, ref dummy, in names, ref rejectedMoves, ref diags);
            }
        }

        if (emitDiags && IsMutatingThroughShared(destPlaceId, isMethodOrRef: false, in fn, in types, in mir))
        {
            diags.ReportTok(VibCodes.MutateSharedBorrow, tok, "Cannot mutate through a shared borrow."u8);
        }

        int excluded = dpl.ProjCount == 0 ? dpl.LocalId : -1;
        _ = CheckWriteOrMoveConflicts(destPlaceId, isMove: false, excluded, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags);
        if (dpl.ProjCount > 0 && mir.Projs[dpl.ProjStart].Kind == MirProjKinds.Deref)
        {
            ActivateCarrierLoans(dpl.LocalId, ref curLoans);
        }

        if (dpl.ProjCount == 0 && isBorrowCarrier[dpl.LocalId] != 0)
        {
            CheckTempRvalueEscape(dpl.LocalId, in rhsLoans, tok, emitDiags, in fn, in mir, in isBorrowCarrier, ref diags);
            SetCarrierLoans(dpl.LocalId, in rhsLoans, ref curLoans);
        }
    }

    private static void DirectPlaceFields(in MirPlace pl, in MirModuleArena mir, out int f0, out int f1)
    {
        f0 = -1;
        f1 = -1;
        int idx = (pl.ProjCount > 0 && mir.Projs[pl.ProjStart].Kind == MirProjKinds.Deref) ? 1 : 0;
        if (idx < pl.ProjCount && mir.Projs[pl.ProjStart + idx].Kind == MirProjKinds.Field)
        {
            f0 = mir.Projs[pl.ProjStart + idx].FieldIdx;
            idx += 1;
            if (idx < pl.ProjCount && mir.Projs[pl.ProjStart + idx].Kind == MirProjKinds.Field)
            {
                f1 = mir.Projs[pl.ProjStart + idx].FieldIdx;
            }
        }
    }

    private static bool IsAncestorCarrier(int childCarrierId, int candidateAncId, in Vec<CarrierLoan> curLoans)
    {
        if (childCarrierId == candidateAncId) return true;
        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            if (cl.CarrierId == childCarrierId && cl.LoanVal.RootLocalId == candidateAncId)
            {
                return true;
            }
        }
        return false;
    }

    private static void CheckAndRecordCallActivationMove(
        int opId,
        bool forceCopy,
        int excludedCarrier,
        bool emitDiags,
        in MirFunc fn,
        in AstArena ast,
        in TokenArena ta,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> activationLive,
        ref Vec<int> rejectedMoves,
        ref DiagnosticBag diags)
    {
        if (opId < 0 || forceCopy) return;
        MirOperand operand = mir.Operands[opId];
        if (operand.Kind != MirOpKinds.CopyOrMove || types.IsCopy(operand.TypeId, in ast)) return;
        if (CheckCallActivationMove(operand.PlaceOrLitIdx, excludedCarrier, ta.Tokens[operand.SourceStartTokIdx], emitDiags,
            out int rejectedOwner, in fn, in names, in types, in mir, in curLoans, in activationLive, ref diags))
        {
            rejectedMoves[opId] = rejectedOwner + 1;
        }
    }

    // Every operand capture is a loan of this invocation until its final use.
    // Reservation permits reads, never moving the storage whose address was saved.
    private static bool CheckCallActivationMove(
        int placeId,
        int excludedCarrier,
        Token tok,
        bool emitDiags,
        out int rejectedOwner,
        in MirFunc fn,
        in Interner names,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref DiagnosticBag diags)
    {
        rejectedOwner = -1;
        MirPlace place = mir.Places[placeId];
        int field0 = -1;
        int field1 = -1;
        if (place.ProjCount > 0 && mir.Projs[place.ProjStart].Kind == MirProjKinds.Field)
        {
            field0 = mir.Projs[place.ProjStart].FieldIdx;
            if (place.ProjCount > 1 && mir.Projs[place.ProjStart + 1].Kind == MirProjKinds.Field)
                field1 = mir.Projs[place.ProjStart + 1].FieldIdx;
        }
        MirLocal source = mir.Locals[fn.LocalStart + place.LocalId];
        bool throughBorrow = (place.ProjCount > 0 && mir.Projs[place.ProjStart].Kind == MirProjKinds.Deref) ||
            (source.Flags & MirLocalFlags.IsBorrowedBind) != 0 || source.Mode != PassMode.ByValue;
        var targets = new Vec<Loan>(4);
        if (throughBorrow)
            ComputeLoansForPlaceBorrow(placeId, isMut: false, in curLoans, in mir, ref targets);
        else
            targets.Push(new Loan(LoanKinds.Place, place.LocalId, field0, field1, 0));

        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan carrier = curLoans[i];
            int id = carrier.CarrierId;
            if (id == excludedCarrier || id == place.LocalId || liveAfter[id] == 0) continue;
            if (throughBorrow && IsAncestorCarrier(place.LocalId, id, in curLoans)) continue;
            MirLocal local = mir.Locals[fn.LocalStart + id];
            if ((local.Flags & MirLocalFlags.IsCallOperandLoan) == 0) continue;
            Loan loan = carrier.LoanVal;
            if (loan.Kind != LoanKinds.Place) continue;
            int overlappingOwner = -1;
            for (int target = 0; target < targets.Count; target++)
            {
                Loan moving = targets[target];
                if (moving.Kind == LoanKinds.Place &&
                    PathsOverlap(moving.RootLocalId, moving.Field0, moving.Field1, loan.RootLocalId, loan.Field0, loan.Field1))
                {
                    overlappingOwner = moving.RootLocalId;
                    break;
                }
            }
            if (overlappingOwner < 0) continue;
            rejectedOwner = overlappingOwner;
            if (!emitDiags) return true;

            var message = new StringBuf();
            message.Append("Cannot move '"u8);
            if (source.NameId >= 0) message.Append(names.Text(source.NameId));
            else message.Append("value"u8);
            int currentType = source.TypeId;
            for (int p = 0; p < place.ProjCount; p++)
            {
                MirProj projection = mir.Projs[place.ProjStart + p];
                if (projection.Kind == MirProjKinds.Field)
                {
                    TypeInfo owner = types.Items[currentType];
                    ConcreteField field = types.Fields[owner.FieldStart + projection.FieldIdx];
                    message.Append("."u8);
                    message.Append(names.Text(field.NameId));
                }
                currentType = projection.ResultTypeId;
            }
            message.Append("' while it is borrowed by an earlier argument or receiver of the same call."u8);
            diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, message.AsUtf8());
            return true;
        }
        return false;
    }

    private static void CheckBorrowCreationConflicts(
        int targetPlaceId,
        bool isExclusive,
        int excludedCarrier,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in MirModuleArena mir,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref DiagnosticBag diags) =>
        CheckBorrowCreationConflicts(targetPlaceId, isExclusive, excludedCarrier, tok, emitDiags, in fn, in mir, in curLoans, in liveAfter, ref diags, false);

    private static void CheckBorrowCreationConflicts(
        int targetPlaceId,
        bool isExclusive,
        int excludedCarrier,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in MirModuleArena mir,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref DiagnosticBag diags,
        bool deferCallOperandOverlap)
    {
        if (!emitDiags || targetPlaceId < 0) return;
        MirPlace tpl = mir.Places[targetPlaceId];
        bool throughCarrier = HasAnyCarrierLoan(tpl.LocalId, in curLoans) ||
            (tpl.ProjCount > 0 && mir.Projs[tpl.ProjStart].Kind == MirProjKinds.Deref);

        var targetLoans = new Vec<Loan>(4);
        ComputeLoansForPlaceBorrow(targetPlaceId, isMut: isExclusive, in curLoans, in mir, ref targetLoans);

        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            int liveId = cl.CarrierId;
            if (liveId == excludedCarrier || liveId == tpl.LocalId || liveAfter[liveId] == 0) continue;
            // Pure preparations of sibling operands activate together (VIB002).
            // An enclosing invocation has a distinct identity and stays active.
            int activation = excludedCarrier >= 0 ? mir.Locals[fn.LocalStart + excludedCarrier].CallActivationId : -1;
            if (deferCallOperandOverlap && activation >= 0 &&
                mir.Locals[fn.LocalStart + liveId].CallActivationId == activation) continue;
            if (throughCarrier && IsAncestorCarrier(tpl.LocalId, liveId, in curLoans)) continue;
            Loan active = cl.LoanVal;
            if (!isExclusive && active.IsActiveMut == 0) continue;
            if (active.Kind != LoanKinds.Place) continue;

            for (int t = 0; t < targetLoans.Count; t++)
            {
                Loan tl = targetLoans[t];
                if (tl.Kind == LoanKinds.Place &&
                    PathsOverlap(active.RootLocalId, active.Field0, active.Field1, tl.RootLocalId, tl.Field0, tl.Field1))
                {
                    diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Borrow conflicts with an active borrow."u8);
                    return;
                }
            }
        }
        _ = fn;
    }

    private static bool CheckWriteOrMoveConflicts(
        int targetPlaceId,
        bool isMove,
        int excludedCarrier,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in MirModuleArena mir,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref DiagnosticBag diags)
    {
        if ((!emitDiags && !isMove) || targetPlaceId < 0) return false;
        MirPlace tpl = mir.Places[targetPlaceId];
        bool writesWholeLocal = tpl.ProjCount == 0;

        // If writing/moving whole local `x`, check direct loans on `x` (NOT transitive loans that `x` itself holds when `x` is a cursor being reassigned!)
        int f0 = -1;
        int f1 = -1;
        if (!writesWholeLocal && mir.Projs[tpl.ProjStart].Kind == MirProjKinds.Field)
        {
            f0 = mir.Projs[tpl.ProjStart].FieldIdx;
            if (tpl.ProjCount > 1 && mir.Projs[tpl.ProjStart + 1].Kind == MirProjKinds.Field)
            {
                f1 = mir.Projs[tpl.ProjStart + 1].FieldIdx;
            }
        }

        MirLocal rootLoc = mir.Locals[fn.LocalStart + tpl.LocalId];
        bool isRebindingViewParam = !isMove && writesWholeLocal &&
            (rootLoc.Flags & MirLocalFlags.IsParam) != 0 &&
            rootLoc.Mode == PassMode.ByValue &&
            HasAnyCarrierLoan(tpl.LocalId, in curLoans);

        var derefTargetLoans = new Vec<Loan>(4);
        bool throughCarrier = !isMove && tpl.ProjCount > 0 && (mir.Projs[tpl.ProjStart].Kind == MirProjKinds.Deref || HasAnyCarrierLoan(tpl.LocalId, in curLoans));
        if (throughCarrier)
        {
            ComputeLoansForPlaceBorrow(targetPlaceId, isMut: true, in curLoans, in mir, ref derefTargetLoans);
        }

        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            int liveId = cl.CarrierId;
            if (liveId == excludedCarrier || liveAfter[liveId] == 0) continue;
            if (writesWholeLocal && isMove && liveId == tpl.LocalId) continue;
            if (throughCarrier && IsAncestorCarrier(tpl.LocalId, liveId, in curLoans)) continue;

            Loan active = cl.LoanVal;
            if (writesWholeLocal && active.Kind == LoanKinds.StepCursor && active.RootLocalId == tpl.LocalId)
            {
                if (emitDiags) diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Cannot update cursor while derived pattern binding is still live."u8);
                return true;
            }
            if (active.Kind != LoanKinds.Place) continue;

            if (throughCarrier)
            {
                for (int t = 0; t < derefTargetLoans.Count; t++)
                {
                    Loan tl = derefTargetLoans[t];
                    if (tl.Kind == LoanKinds.Place && PathsOverlap(active.RootLocalId, active.Field0, active.Field1, tl.RootLocalId, tl.Field0, tl.Field1))
                    {
                        if (emitDiags) diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Cannot mutate through reference while borrowed by another live reference."u8);
                        return true;
                    }
                }
            }
            else if (!isRebindingViewParam)
            {
                if (PathsOverlap(active.RootLocalId, active.Field0, active.Field1, tpl.LocalId, f0, f1))
                {
                    if (emitDiags) diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, isMove ? "Cannot move value while borrowed."u8 : "Cannot mutate value while borrowed."u8);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasAnyCarrierLoan(int lid, in Vec<CarrierLoan> curLoans)
    {
        for (int i = 0; i < curLoans.Count; i++)
        {
            if (curLoans[i].CarrierId == lid) return true;
        }
        return false;
    }

    private static void CheckCursorMutationConflicts(
        int cursorPlaceId,
        int excludedCarrier,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in MirModuleArena mir,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref DiagnosticBag diags)
    {
        if (!emitDiags || cursorPlaceId < 0) return;
        MirPlace cpl = mir.Places[cursorPlaceId];
        var targetLoans = new Vec<Loan>(4);
        ComputeLoansForPlaceBorrow(cursorPlaceId, isMut: true, in curLoans, in mir, ref targetLoans);

        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            int liveId = cl.CarrierId;
            if (liveId == excludedCarrier || liveId == cpl.LocalId || liveAfter[liveId] == 0) continue;
            Loan active = cl.LoanVal;
            if (cpl.ProjCount == 0 && active.Kind == LoanKinds.StepCursor && active.RootLocalId == cpl.LocalId)
            {
                diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Cannot mutate through cursor while pattern binding is still live."u8);
                return;
            }
            if (active.Kind == LoanKinds.Place)
            {
                for (int t = 0; t < targetLoans.Count; t++)
                {
                    Loan tl = targetLoans[t];
                    if (tl.Kind == LoanKinds.Place && PathsOverlap(active.RootLocalId, active.Field0, active.Field1, tl.RootLocalId, tl.Field0, tl.Field1))
                    {
                        diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Cannot mutate through cursor while borrowed."u8);
                        return;
                    }
                }
            }
        }
        _ = fn;
    }

    private static void CheckScopeExitConflicts(
        int exitingLocalId,
        bool returningBorrow,
        Token tok,
        in MirFunc fn,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        in Vec<int> stackAllocationOwners,
        in Vec<CarrierLoan> curLoans,
        in Vec<byte> liveAfter,
        ref DiagnosticBag diags)
    {
        MirLocal exitingLoc = mir.Locals[fn.LocalStart + exitingLocalId];
        if ((exitingLoc.Flags & MirLocalFlags.IsParam) != 0 && exitingLoc.Mode != PassMode.ByValue)
        {
            return;
        }

        for (int i = 0; i < curLoans.Count; i++)
        {
            CarrierLoan cl = curLoans[i];
            int liveId = cl.CarrierId;
            if (liveId == exitingLocalId || liveAfter[liveId] == 0) continue;
            Loan active = cl.LoanVal;
            if (active.Kind == LoanKinds.StackSpan && active.RootLocalId >= 0 &&
                active.RootLocalId < stackAllocationOwners.Count && stackAllocationOwners[active.RootLocalId] == exitingLocalId)
            {
                diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Stack-allocated buffer exits scope while borrowed by a live reference or view."u8);
                return;
            }
            // A view/ref alias ending does not end the underlying owner's lifetime.
            if (isBorrowCarrier[exitingLocalId] == 0 && active.Kind == LoanKinds.Place && active.RootLocalId == exitingLocalId)
            {
                // Method-frame parameter and anonymous-owner escapes are
                // diagnosed from the preserved provenance by Return (VIB007),
                // not as a shorter lexical-scope borrow conflict (VIB003).
                if (returningBorrow && (exitingLoc.Flags & (MirLocalFlags.IsParam | MirLocalFlags.IsTemp)) != 0) continue;
                diags.ReportTok(VibCodes.ConflictActiveBorrow, tok, "Local goes out of scope while borrowed by an outer reference or view."u8);
                return;
            }
        }
    }

    private static void CheckTempRvalueEscape(
        int destLocalId,
        in Vec<Loan> rhsLoans,
        Token tok,
        bool emitDiags,
        in MirFunc fn,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        ref DiagnosticBag diags)
    {
        if (!emitDiags) return;
        MirLocal destLoc = mir.Locals[fn.LocalStart + destLocalId];
        if ((destLoc.Flags & MirLocalFlags.IsTemp) != 0) return;

        for (int i = 0; i < rhsLoans.Count; i++)
        {
            Loan ln = rhsLoans[i];
            if (ln.Kind != LoanKinds.Place) continue;
            MirLocal rootLoc = mir.Locals[fn.LocalStart + ln.RootLocalId];
            if ((rootLoc.Flags & MirLocalFlags.IsTemp) != 0 &&
                (rootLoc.Flags & MirLocalFlags.IsParam) == 0 &&
                (rootLoc.Flags & MirLocalFlags.IsPatternBind) == 0 &&
                isBorrowCarrier[ln.RootLocalId] == 0)
            {
                diags.ReportTok(VibCodes.EscapingBorrow, tok, "Borrowed reference or view cannot outlive temporary rvalue."u8);
                return;
            }
        }
    }

    private static void CheckReturnEscapeLoans(
        in Vec<Loan> returnedLoans,
        Token tok,
        in MirFunc fn,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        ref DiagnosticBag diags)
    {
        FuncInstance fi = mir.FuncInstances[fn.FuncInstId];
        bool returnsByRef = (fi.Flags & DeclFlags.ReturnsByRef) != 0;

        for (int i = 0; i < returnedLoans.Count; i++)
        {
            Loan ln = returnedLoans[i];
            if (ln.Kind == LoanKinds.StackSpan)
            {
                diags.ReportTok(VibCodes.EscapingBorrow, tok, "Cannot return a reference or view backed by stack-allocated storage."u8);
                return;
            }
            if (ln.Kind == LoanKinds.StepCursor)
            {
                MirLocal cRoot = mir.Locals[fn.LocalStart + ln.RootLocalId];
                if ((cRoot.Flags & MirLocalFlags.IsScoped) != 0 ||
                    ((cRoot.Flags & MirLocalFlags.IsParam) != 0 && !CanRootLocalEscapeInReturn(in cRoot, in types)))
                {
                    diags.ReportTok(VibCodes.EscapingBorrow, tok, "Returned reference or view escapes scoped cursor."u8);
                    return;
                }
                continue;
            }
            if (ln.Kind != LoanKinds.Place) continue;

            MirLocal root = mir.Locals[fn.LocalStart + ln.RootLocalId];
            if ((root.Flags & MirLocalFlags.IsParam) == 0 &&
                isBorrowCarrier[ln.RootLocalId] != 0 &&
                !(returnsByRef && fi.ReturnTypeId == root.TypeId && (root.Flags & MirLocalFlags.IsRefLocal) == 0))
            {
                continue;
            }

            if (!CanRootLocalEscapeInReturn(in root, in types) &&
                !DoesStructThisLoanProjectThroughIndirection(in root, in ln, in types))
            {
                diags.ReportTok(VibCodes.EscapingBorrow, tok, "Returned reference or view escapes local or scoped/by-value owner."u8);
                return;
            }
        }
    }

    private static void CheckRefParamAssignEscape(
        in Vec<Loan> rhsLoans,
        Token tok,
        in MirFunc fn,
        in TypeTable types,
        in MirModuleArena mir,
        in Vec<byte> isBorrowCarrier,
        ref DiagnosticBag diags)
    {
        for (int i = 0; i < rhsLoans.Count; i++)
        {
            Loan ln = rhsLoans[i];
            if (ln.Kind == LoanKinds.StackSpan)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Cannot ref-assign stack-backed storage to an unscoped 'ref' or 'in' parameter."u8);
                return;
            }
            if (ln.Kind != LoanKinds.Place) continue;

            MirLocal root = mir.Locals[fn.LocalStart + ln.RootLocalId];
            if (types.Items[root.TypeId].Kind == TypeKinds.Class) continue;

            if ((root.Flags & MirLocalFlags.IsParam) == 0 && isBorrowCarrier[ln.RootLocalId] == 0)
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Cannot ref-assign a local variable to an unscoped 'ref' or 'in' parameter (use 'scoped ref' or 'scoped in')."u8);
                return;
            }
            if ((root.Flags & MirLocalFlags.IsParam) != 0 && !CanRootLocalEscapeInReturn(in root, in types))
            {
                diags.ReportTok(VibCodes.Unsupported, tok, "Cannot ref-assign a scoped parameter to an unscoped 'ref' or 'in' parameter."u8);
                return;
            }
        }
    }

    // Stage-0's StructContainsIndirectOrViewField: a field, directly or in a
    // nested struct, of a class, Vec or view type.
    private static bool StructContainsIndirectOrViewField(int structTypeId, in TypeTable types, int depth)
    {
        // Recursive struct layouts are rejected (CS0523); the bound only stops the walk.
        if (structTypeId < 0 || depth > 32) return false;
        TypeInfo owner = types.Items[structTypeId];
        if (owner.Kind != TypeKinds.Struct) return false;
        for (int i = 0; i < owner.FieldCount; i++)
        {
            int fieldType = types.Fields[owner.FieldStart + i].TypeId;
            if (fieldType < 0) continue;
            if (IsIndirectOrViewKind(types.Items[fieldType].Kind)) return true;
            if (types.Items[fieldType].Kind == TypeKinds.Struct &&
                StructContainsIndirectOrViewField(fieldType, in types, depth + 1)) return true;
        }
        return false;
    }

    private static bool IsIndirectOrViewKind(int kind) =>
        kind == TypeKinds.Vec || kind == TypeKinds.Class ||
        kind == TypeKinds.Span || kind == TypeKinds.ReadOnlySpan ||
        kind == TypeKinds.OptionRef || kind == TypeKinds.OptionMut ||
        kind == TypeKinds.Utf8Lines || kind == TypeKinds.Utf8ArgEnum;

    // SPEC §3.3 elision ties an unannotated borrow-returning struct member to
    // its borrowed `this`. A loan whose field path reaches storage behind an
    // indirection (a class object, a Vec buffer or a view) is not the struct's
    // own frame storage, so it may escape with `this` (Stage-0's
    // DoesStructSelfLoanProjectThroughIndirection). Loans keep two field
    // levels; a deeper struct-only prefix stays rejected.
    private static bool DoesStructThisLoanProjectThroughIndirection(in MirLocal root, in Loan loan, in TypeTable types)
    {
        if ((root.Flags & MirLocalFlags.IsParam) == 0 || (root.Flags & MirLocalFlags.IsScoped) != 0) return false;
        if (root.Mode != PassMode.ThisShared && root.Mode != PassMode.ThisMut) return false;
        if (loan.Kind != LoanKinds.Place || loan.Field0 < 0) return false;
        int current = root.TypeId;
        for (int level = 0; level < 2; level++)
        {
            int fieldIdx = level == 0 ? loan.Field0 : loan.Field1;
            if (fieldIdx < 0) return false;
            TypeInfo owner = types.Items[current];
            if (owner.Kind != TypeKinds.Struct || fieldIdx >= owner.FieldCount) return false;
            current = types.Fields[owner.FieldStart + fieldIdx].TypeId;
            if (current < 0) return false;
            if (IsIndirectOrViewKind(types.Items[current].Kind)) return true;
        }
        return false;
    }

    private static bool CanRootLocalEscapeInReturn(in MirLocal root, in TypeTable types)
    {
        if ((root.Flags & MirLocalFlags.IsParam) == 0) return false;
        if ((root.Flags & MirLocalFlags.IsScoped) != 0) return false;

        if (root.Mode == PassMode.In || root.Mode == PassMode.Ref || root.Mode == PassMode.Out)
        {
            return true;
        }
        if (root.Mode == PassMode.ByValue)
        {
            // View parameter (`Span<T>`, `ReadOnlySpan<T>`, `OptionRef<T>`, `OptionMut<T>`) or heap class reference (`this`/param)
            return types.IsView(root.TypeId);
        }
        if (root.Mode == PassMode.ThisShared || root.Mode == PassMode.ThisMut)
        {
            if (types.Items[root.TypeId].Kind == TypeKinds.Class) return true;
            if (types.IsView(root.TypeId)) return true;
            // Struct `this` requires `[UnscopedRef]`!
            return (root.Flags & MirLocalFlags.IsUnscopedRefRecv) != 0;
        }
        return false;
    }
}
