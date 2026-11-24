namespace Vibranium.Self;

public struct SourceAssignmentSlot
{
    public int LocalId;
    public int Parent;
    public int TypeId;
    public int ChildStart;
    public int ChildCount;

    public SourceAssignmentSlot(int localId, int parent, int typeId)
    {
        LocalId = localId;
        Parent = parent;
        TypeId = typeId;
        ChildStart = 0;
        ChildCount = 0;
    }
}

public readonly struct SourceSwitchFlow
{
    public readonly int EntryBlock;
    public readonly int AfterInst;
    public readonly int TargetStart;
    public readonly int TargetCount;
    public readonly int BodyStart;
    public readonly int BodyCount;

    public SourceSwitchFlow(int entryBlock, int afterInst, int targetStart, int targetCount, int bodyStart, int bodyCount)
    {
        EntryBlock = entryBlock;
        AfterInst = afterInst;
        TargetStart = targetStart;
        TargetCount = targetCount;
        BodyStart = bodyStart;
        BodyCount = bodyCount;
    }
}

public readonly struct SourceSwitchAssignmentEdge
{
    public readonly int Block;
    public readonly int AfterInst;
    public readonly int Target;

    public SourceSwitchAssignmentEdge(int block, int afterInst, int target)
    {
        Block = block;
        AfterInst = afterInst;
        Target = target;
    }
}

public static class SourceValidityChecker
{
    private const int CallerContext = 0;
    private const int ReturnContext = 1;
    private const int FunctionContext = 2;

    public static void CheckFunction(
        in MirFunc fn,
        in FuncLowerCtx source,
        in AstArena ast,
        in TokenArena ta,
        in TypeTable types,
        in MirModuleArena mir,
        ref DiagnosticBag diags)
    {
        if (fn.BlockCount == 0) return;
        CheckDefiniteAssignment(in fn, in source, in ta, in types, in mir, ref diags);
        CheckEscapeContexts(in fn, in source, in ast, in ta, in types, in mir, ref diags);
        CheckSwitchSections(in fn, in source, in ta, in mir, ref diags);
    }

    private static void CheckDefiniteAssignment(
        in MirFunc fn, in FuncLowerCtx source, in TokenArena ta,
        in TypeTable types, in MirModuleArena mir, ref DiagnosticBag diags)
    {
        var slots = new Vec<SourceAssignmentSlot>();
        for (int i = 0; i < fn.LocalCount; i++)
        {
            slots.Push(new SourceAssignmentSlot(i, -1, mir.Locals[fn.LocalStart + i].TypeId));
        }
        for (int i = 0; i < slots.Count; i++)
        {
            SourceAssignmentSlot slot = slots[i];
            TypeInfo type = types.Items[slot.TypeId];
            if (type.Kind != TypeKinds.Struct) continue;
            slot.ChildStart = slots.Count;
            slot.ChildCount = type.FieldCount;
            slots[i] = slot;
            for (int j = 0; j < type.FieldCount; j++)
            {
                slots.Push(new SourceAssignmentSlot(slot.LocalId, i, types.Fields[type.FieldStart + j].TypeId));
            }
        }
        var snapshots = new Vec<int>(fn.LocalCount);
        snapshots.Resize(fn.LocalCount, -1);
        for (int i = 0; i < source.StagedInsts.Count; i++)
        {
            MirInst inst = source.StagedInsts[i];
            if (inst.Op == MirOp.BindRef && (inst.Flags & 4) != 0)
            {
                snapshots[inst.A] = inst.B;
            }
        }
        var switchEdges = new Vec<SourceSwitchAssignmentEdge>();
        BuildSwitchAssignmentEdges(in fn, in source, in mir, ref switchEdges);
        int width = slots.Count;
        var reached = new Vec<byte>(fn.BlockCount);
        reached.Resize(fn.BlockCount, 0);
        var inputs = new Vec<byte>(fn.BlockCount * width);
        inputs.Resize(fn.BlockCount * width, 1);
        var state = new Vec<byte>(width);
        state.Resize(width, 0);
        for (int i = 0; i < width; i++)
        {
            MirLocal local = mir.Locals[fn.LocalStart + slots[i].LocalId];
            bool assigned = ((local.Flags & MirLocalFlags.IsParam) != 0 && local.Mode != PassMode.Out) ||
                (local.Flags & MirLocalFlags.IsCtorFieldSlot) != 0;
            inputs[i] = assigned ? (byte)1 : (byte)0;
        }
        for (int i = width - 1; i >= 0; i--)
        {
            SourceAssignmentSlot slot = slots[i];
            if (types.Items[slot.TypeId].Kind != TypeKinds.Struct) continue;
            byte assigned = 1;
            for (int j = 0; j < slot.ChildCount; j++) assigned &= inputs[slot.ChildStart + j];
            inputs[i] = assigned;
        }
        reached[0] = 1;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int b = 0; b < fn.BlockCount; b++)
            {
                if (reached[b] == 0) continue;
                for (int s = 0; s < width; s++) state[s] = inputs[b * width + s];
                TransferBlock(b, false, in fn, in source, in ta, in types, in mir, in slots, in snapshots, in switchEdges,
                    ref state, ref reached, ref inputs, ref changed, ref diags);
            }
        }
        for (int b = 0; b < fn.BlockCount; b++)
        {
            if (reached[b] == 0) continue;
            for (int s = 0; s < width; s++) state[s] = inputs[b * width + s];
            TransferBlock(b, true, in fn, in source, in ta, in types, in mir, in slots, in snapshots, in switchEdges,
                ref state, ref reached, ref inputs, ref changed, ref diags);
        }
    }

    private static void TransferBlock(
        int blockId, bool report, in MirFunc fn, in FuncLowerCtx source, in TokenArena ta,
        in TypeTable types, in MirModuleArena mir, in Vec<SourceAssignmentSlot> slots, in Vec<int> snapshots,
        in Vec<SourceSwitchAssignmentEdge> switchEdges, ref Vec<byte> state, ref Vec<byte> reached, ref Vec<byte> inputs, ref bool changed, ref DiagnosticBag diags)
    {
        MirBlock block = mir.Blocks[fn.BlockStart + blockId];
        int staged = source.BlockHead[blockId];
        PropagateSwitchAssignments(blockId, -1, in switchEdges, in state, ref reached, ref inputs, ref changed);
        for (int i = 0; i < block.InstCount; i++)
        {
            MirInst inst = mir.Insts[block.InstStart + i];
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
                case MirOp.AssignCast:
                    ReadOperand(inst.A, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                    break;
                case MirOp.AssignBin:
                    ReadOperand(inst.B, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                    ReadOperand(inst.C, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                    break;
                case MirOp.AssignUn:
                    ReadOperand(inst.B, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                    break;
                case MirOp.AllocClass:
                case MirOp.ConstructStruct:
                case MirOp.StackSpan:
                    for (int a = 0; a < inst.B; a++)
                    {
                        ReadOperand(mir.ExtraLists[inst.A + a], report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                    }
                    break;
                case MirOp.BindRef:
                    CheckPlace(inst.B, (inst.Flags & 4) == 0, inst.TokIdx, report, in fn, in ta, in mir,
                        in slots, in snapshots, in state, ref diags);
                    SetAssigned(inst.A, in slots, ref state);
                    break;
                case MirOp.Call:
                    for (int a = 0; a < inst.C; a++)
                    {
                        MirCallArg arg = mir.CallArgs[inst.B + a];
                        if (ArgumentIsOperand(inst.A, a, arg.Mode, in types, in mir))
                        {
                            ReadOperand(arg.OperandOrPlaceId, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                        }
                        else
                        {
                            CheckPlace(arg.OperandOrPlaceId, arg.Mode != PassMode.Out, arg.TokIdx, report,
                                in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                        }
                    }
                    for (int a = 0; a < inst.C; a++)
                    {
                        MirCallArg arg = mir.CallArgs[inst.B + a];
                        if (arg.Mode == PassMode.Out)
                        {
                            WritePlace(arg.OperandOrPlaceId, arg.TokIdx, report, in fn, in ta, in mir,
                                in slots, in snapshots, ref state, ref diags);
                        }
                    }
                    break;
                case MirOp.Intrinsic:
                {
                    for (int a = 0; a < inst.C; a++)
                    {
                        ReadOperand(mir.ExtraLists[inst.B + a], report, in fn, in ta, in mir,
                            in slots, in snapshots, in state, ref diags);
                    }
                    int placeCount = inst.D >>> 20;
                    int placeStart = inst.D & 0xFFFFF;
                    for (int a = 0; a < placeCount; a++)
                    {
                        CheckPlace(mir.ExtraLists[placeStart + a], true, inst.TokIdx, report, in fn,
                            in ta, in mir, in slots, in snapshots, in state, ref diags);
                    }
                    break;
                }
                case MirOp.DisposeResource:
                case MirOp.ScopeExit:
                    break;
            }
            if (inst.RefDestLocalId >= 0) SetAssigned(inst.RefDestLocalId, in slots, ref state);
            else if (inst.DestPlaceId >= 0)
            {
                WritePlace(inst.DestPlaceId, inst.TokIdx, report, in fn, in ta, in mir,
                    in slots, in snapshots, ref state, ref diags);
            }
            PropagateSwitchAssignments(blockId, staged, in switchEdges, in state, ref reached, ref inputs, ref changed);
            staged = source.StagedNext[staged];
        }
        MirTerm term = source.SourceAssignmentTerms[blockId];
        if (term.Kind == MirTermKind.Unset) term = block.Term;
        if (source.PanicContinuations[blockId] >= 0)
        {
            Join(source.PanicContinuations[blockId], in state, ref reached, ref inputs, ref changed);
            return;
        }
        switch (term.Kind)
        {
            case MirTermKind.Goto:
                Join(term.A, in state, ref reached, ref inputs, ref changed);
                break;
            case MirTermKind.Branch:
                ReadOperand(term.A, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                if (term.A >= 0 && mir.Operands[term.A].Kind == MirOpKinds.BoolConst)
                {
                    Join(mir.Operands[term.A].IntVal != 0 ? term.B : term.C, in state, ref reached, ref inputs, ref changed);
                }
                else
                {
                    Join(term.B, in state, ref reached, ref inputs, ref changed);
                    Join(term.C, in state, ref reached, ref inputs, ref changed);
                }
                break;
            case MirTermKind.MatchOption:
            case MirTermKind.MatchResult:
                if ((term.Flags & 1) != 0)
                {
                    CheckPlace(term.A, true, term.TokIdx, report, in fn, in ta, in mir,
                        in slots, in snapshots, in state, ref diags);
                }
                else ReadOperand(term.A, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                if (term.Kind == MirTermKind.MatchOption)
                {
                    Join(term.D, in state, ref reached, ref inputs, ref changed);
                    SetAssigned(term.B, in slots, ref state);
                    Join(term.C, in state, ref reached, ref inputs, ref changed);
                }
                else
                {
                    var other = new Vec<byte>(state.Count);
                    for (int s = 0; s < state.Count; s++) other.Push(state[s]);
                    SetAssigned(term.B, in slots, ref state);
                    Join(term.C, in state, ref reached, ref inputs, ref changed);
                    SetAssigned(term.D, in slots, ref other);
                    Join(term.E, in other, ref reached, ref inputs, ref changed);
                }
                break;
            case MirTermKind.GuardOptionIsNone:
            case MirTermKind.GuardResultIsErr:
                ReadOperand(term.A, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                SetAssigned(term.B, in slots, ref state);
                if (term.Kind == MirTermKind.GuardResultIsErr)
                {
                    SetAssigned(term.C, in slots, ref state);
                    Join(term.D, in state, ref reached, ref inputs, ref changed);
                    Join(term.E, in state, ref reached, ref inputs, ref changed);
                }
                else
                {
                    Join(term.C, in state, ref reached, ref inputs, ref changed);
                    Join(term.D, in state, ref reached, ref inputs, ref changed);
                }
                break;
            case MirTermKind.Return:
                if (term.A >= 0) ReadOperand(term.A, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
                if (term.B >= 0)
                {
                    CheckPlace(term.B, true, term.TokIdx, report, in fn, in ta, in mir,
                        in slots, in snapshots, in state, ref diags);
                }
                if (report)
                {
                    for (int i = 0; i < fn.LocalCount; i++)
                    {
                        MirLocal local = mir.Locals[fn.LocalStart + i];
                        if ((local.Flags & MirLocalFlags.IsParam) != 0 && local.Mode == PassMode.Out && state[i] == 0)
                        {
                            diags.ReportTok(VibCodes.UseAfterMove, ta.Tokens[term.TokIdx],
                                "C# requires every out parameter to be definitely assigned before a normal source return (CS0177)."u8);
                        }
                    }
                }
                break;
        }
    }

    private static void BuildSwitchAssignmentEdges(in MirFunc fn, in FuncLowerCtx source,
        in MirModuleArena mir, ref Vec<SourceSwitchAssignmentEdge> edges)
    {
        for (int i = 0; i < source.SourceSwitchContinuations.Count; i++) edges.Push(source.SourceSwitchContinuations[i]);
        for (int f = 0; f < source.SourceSwitchFlows.Count; f++)
        {
            SourceSwitchFlow flow = source.SourceSwitchFlows[f];
            var reached = new Vec<byte>(fn.BlockCount);
            reached.Resize(fn.BlockCount, 0);
            var stops = new Vec<byte>(fn.BlockCount);
            stops.Resize(fn.BlockCount, 0);
            for (int i = 0; i < flow.BodyCount; i++) stops[source.SourceSwitchBodies[flow.BodyStart + i]] = 1;
            var queue = new Vec<int>(fn.BlockCount);
            Enqueue(flow.EntryBlock, ref reached, ref queue);
            int head = 0;
            while (head < queue.Count)
            {
                int block = queue[head];
                head += 1;
                if (stops[block] != 0) continue;
                EnqueueSourceContinuations(block, in source, ref reached, ref queue);
                MirTerm term = source.SourceBlockTerms[block];
                if (term.Kind == MirTermKind.Unset) term = mir.Blocks[fn.BlockStart + block].Term;
                EnqueueTerm(in term, in mir, ref reached, ref queue);
            }
            for (int i = 0; i < flow.TargetCount; i++)
            {
                int target = source.SourceSwitchTargets[flow.TargetStart + i];
                if (target != flow.EntryBlock && reached[target] != 0)
                {
                    edges.Push(new SourceSwitchAssignmentEdge(flow.EntryBlock, flow.AfterInst, target));
                }
            }
        }
    }

    private static void PropagateSwitchAssignments(int block, int afterInst, in Vec<SourceSwitchAssignmentEdge> edges,
        in Vec<byte> state, ref Vec<byte> reached, ref Vec<byte> inputs, ref bool changed)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            SourceSwitchAssignmentEdge edge = edges[i];
            if (edge.Block == block && edge.AfterInst == afterInst)
            {
                Join(edge.Target, in state, ref reached, ref inputs, ref changed);
            }
        }
    }

    private static void Join(int successor, in Vec<byte> state, ref Vec<byte> reached, ref Vec<byte> inputs, ref bool changed)
    {
        if (successor < 0 || successor >= reached.Count) return;
        bool first = reached[successor] == 0;
        if (first)
        {
            reached[successor] = 1;
            changed = true;
        }
        int start = successor * state.Count;
        for (int i = 0; i < state.Count; i++)
        {
            byte next = first ? state[i] : (byte)(inputs[start + i] & state[i]);
            if (inputs[start + i] != next)
            {
                inputs[start + i] = next;
                changed = true;
            }
        }
    }

    private static void SetAssigned(int slotId, in Vec<SourceAssignmentSlot> slots, ref Vec<byte> state)
    {
        if (slotId < 0) return;
        AssignChildren(slotId, in slots, ref state);
        int parent = slots[slotId].Parent;
        while (parent >= 0)
        {
            SourceAssignmentSlot slot = slots[parent];
            byte assigned = 1;
            for (int i = 0; i < slot.ChildCount; i++) assigned &= state[slot.ChildStart + i];
            state[parent] = assigned;
            parent = slot.Parent;
        }
    }

    private static void AssignChildren(int slotId, in Vec<SourceAssignmentSlot> slots, ref Vec<byte> state)
    {
        state[slotId] = 1;
        SourceAssignmentSlot slot = slots[slotId];
        for (int i = 0; i < slot.ChildCount; i++) AssignChildren(slot.ChildStart + i, in slots, ref state);
    }

    private static void ReadOperand(int operandId, bool report, in MirFunc fn, in TokenArena ta,
        in MirModuleArena mir, in Vec<SourceAssignmentSlot> slots, in Vec<int> snapshots,
        in Vec<byte> state, ref DiagnosticBag diags)
    {
        if (operandId < 0) return;
        MirOperand operand = mir.Operands[operandId];
        if (operand.Kind == MirOpKinds.CopyOrMove)
        {
            CheckPlace(operand.PlaceOrLitIdx, true, operand.TokIdx, report, in fn, in ta, in mir,
                in slots, in snapshots, in state, ref diags);
        }
    }

    private static void WritePlace(int placeId, int tokIdx, bool report, in MirFunc fn, in TokenArena ta,
        in MirModuleArena mir, in Vec<SourceAssignmentSlot> slots, in Vec<int> snapshots,
        ref Vec<byte> state, ref DiagnosticBag diags)
    {
        int slot = ResolveSlot(placeId, tokIdx, report, 0, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
        SetAssigned(slot, in slots, ref state);
    }

    private static void CheckPlace(int placeId, bool read, int tokIdx, bool report, in MirFunc fn, in TokenArena ta,
        in MirModuleArena mir, in Vec<SourceAssignmentSlot> slots, in Vec<int> snapshots,
        in Vec<byte> state, ref DiagnosticBag diags)
    {
        int slot = ResolveSlot(placeId, tokIdx, report, 0, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
        if (read) CheckSlot(slot, tokIdx, report, in ta, in state, ref diags);
    }

    private static int ResolveSlot(int placeId, int tokIdx, bool report, int hops, in MirFunc fn, in TokenArena ta,
        in MirModuleArena mir, in Vec<SourceAssignmentSlot> slots, in Vec<int> snapshots,
        in Vec<byte> state, ref DiagnosticBag diags)
    {
        if (placeId < 0 || hops > fn.LocalCount) return -1;
        MirPlace place = mir.Places[placeId];
        int slot = place.LocalId;
        if (slot < 0 || slot >= fn.LocalCount) return -1;
        int snapshot = snapshots[slot];
        if (snapshot >= 0)
        {
            CheckSlot(slot, tokIdx, report, in ta, in state, ref diags);
            slot = ResolveSlot(snapshot, tokIdx, report, hops + 1, in fn, in ta, in mir,
                in slots, in snapshots, in state, ref diags);
        }
        for (int p = 0; p < place.ProjCount; p++)
        {
            MirProj projection = mir.Projs[place.ProjStart + p];
            if (projection.Kind == MirProjKinds.Index)
            {
                ReadOperand(projection.IndexOpId, report, in fn, in ta, in mir, in slots, in snapshots, in state, ref diags);
            }
            if ((projection.Flags & 1) != 0 || projection.Kind == MirProjKinds.Index)
            {
                CheckSlot(slot, tokIdx, report, in ta, in state, ref diags);
                slot = -1;
            }
            else if (projection.Kind == MirProjKinds.Field && slot >= 0)
            {
                SourceAssignmentSlot parent = slots[slot];
                if (projection.FieldIdx >= 0 && projection.FieldIdx < parent.ChildCount)
                {
                    slot = parent.ChildStart + projection.FieldIdx;
                }
                else
                {
                    CheckSlot(slot, tokIdx, report, in ta, in state, ref diags);
                    slot = -1;
                }
            }
        }
        return slot;
    }

    private static void CheckSlot(int slot, int tokIdx, bool report, in TokenArena ta, in Vec<byte> state, ref DiagnosticBag diags)
    {
        if (report && slot >= 0 && state[slot] == 0)
        {
            diags.ReportTok(VibCodes.UseAfterMove, ta.Tokens[tokIdx],
                "C# requires this variable or struct field to be definitely assigned before it is read (CS0165/CS0170)."u8);
        }
    }

    private static bool ArgumentIsOperand(int calleeId, int argIndex, PassMode mode, in TypeTable types, in MirModuleArena mir)
    {
        if (mode == PassMode.ByValue) return true;
        if (argIndex != 0 || (mode != PassMode.ThisShared && mode != PassMode.ThisMut)) return false;
        int owner = mir.FuncInstances[calleeId].OwnerTypeId;
        return owner >= 0 && types.Items[owner].Kind == TypeKinds.Class;
    }

    private static void CheckEscapeContexts(
        in MirFunc fn, in FuncLowerCtx source, in AstArena ast, in TokenArena ta, in TypeTable types,
        in MirModuleArena mir, ref DiagnosticBag diags)
    {
        var references = new Vec<int>(fn.LocalCount);
        var values = new Vec<int>(fn.LocalCount);
        for (int i = 0; i < fn.LocalCount; i++)
        {
            MirLocal local = mir.Locals[fn.LocalStart + i];
            bool parameter = (local.Flags & MirLocalFlags.IsParam) != 0;
            bool scoped = parameter ? IsSourceScoped(fn.FuncInstId, i, in ast, in mir) : (local.Flags & MirLocalFlags.IsScoped) != 0;
            int declaration = DeclarationContext(source.LocalScopeDepths[i]);
            int reference = declaration;
            int value = CallerContext;
            if (parameter)
            {
                reference = FunctionContext;
                if (local.Mode == PassMode.In || local.Mode == PassMode.Ref)
                {
                    reference = scoped ? FunctionContext : ReturnContext;
                }
                else if (local.Mode == PassMode.ThisMut || local.Mode == PassMode.ThisShared || local.Mode == PassMode.Out)
                {
                    reference = (local.Flags & MirLocalFlags.IsUnscopedRefRecv) != 0 ? ReturnContext : FunctionContext;
                }
                if (types.IsView(local.TypeId))
                {
                    if (scoped && local.Mode == PassMode.ByValue) value = FunctionContext;
                    else if (local.Mode == PassMode.Out) value = ReturnContext;
                }
            }
            else
            {
                if ((local.Flags & MirLocalFlags.IsRefLocal) != 0)
                {
                    reference = scoped ? declaration : CallerContext;
                }
                if (scoped && types.IsView(local.TypeId) && (local.Flags & MirLocalFlags.IsRefLocal) == 0)
                {
                    value = declaration;
                }
            }
            references.Push(reference);
            values.Push(value);
        }
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < source.StagedInsts.Count; i++)
            {
                MirInst inst = source.StagedInsts[i];
                int reference = InstructionContext(in inst, true, in fn, in source, in ast, in types, in mir, in references, in values);
                int value = InstructionContext(in inst, false, in fn, in source, in ast, in types, in mir, in references, in values);
                int destination = inst.RefDestLocalId;
                if (destination < 0 && inst.DestPlaceId >= 0)
                {
                    MirPlace place = mir.Places[inst.DestPlaceId];
                    if (place.ProjCount == 0) destination = place.LocalId;
                }
                if (destination >= 0)
                {
                    InferLocalContext(destination, i, reference, value, inst.RefDestLocalId >= 0,
                        in fn, in source, in types, in mir, ref references, ref values, ref changed);
                }
                if (inst.Op == MirOp.Call)
                {
                    for (int a = 0; a < inst.C; a++)
                    {
                        MirCallArg arg = mir.CallArgs[inst.B + a];
                        if (arg.Mode != PassMode.Out) continue;
                        MirPlace place = mir.Places[arg.OperandOrPlaceId];
                        if (place.ProjCount != 0) continue;
                        int output = CallContext(in inst, false, in fn, in ast, in types, in mir, in references, in values);
                        InferLocalContext(place.LocalId, i, CallerContext, output, false,
                            in fn, in source, in types, in mir, ref references, ref values, ref changed);
                    }
                }
            }
        }
        for (int i = 0; i < source.StagedInsts.Count; i++)
        {
            MirInst inst = source.StagedInsts[i];
            if (inst.Op == MirOp.BindRef)
            {
                int target = inst.A;
                int right = PlaceContext(inst.B, true, in fn, in types, in mir, in references, in values);
                if (right > references[target])
                {
                    diags.ReportTok(VibCodes.EscapingBorrow, ta.Tokens[inst.TokIdx],
                        "C# ref reassignment cannot narrow the target's declared ref-safe-context (CS8374)."u8);
                }
            }
            else if (inst.DestPlaceId >= 0 && types.IsView(mir.Places[inst.DestPlaceId].TypeId))
            {
                int right = InstructionContext(in inst, false, in fn, in source, in ast, in types, in mir, in references, in values);
                int target = PlaceContext(inst.DestPlaceId, false, in fn, in types, in mir, in references, in values);
                if (right > target)
                {
                    diags.ReportTok(VibCodes.EscapingBorrow, ta.Tokens[inst.TokIdx],
                        "C# assignment cannot copy a borrowed value into a wider declared safe-context (CS8352/CS8353)."u8);
                }
            }
            if (inst.Op == MirOp.Call)
            {
                CheckCallContexts(in inst, in fn, in ast, in ta, in types, in mir, in references, in values, ref diags);
            }
        }
        for (int b = 0; b < fn.BlockCount; b++)
        {
            MirTerm term = mir.Blocks[fn.BlockStart + b].Term;
            if (term.Kind != MirTermKind.Return) continue;
            if (term.B >= 0 && PlaceContext(term.B, true, in fn, in types, in mir, in references, in values) > ReturnContext)
            {
                diags.ReportTok(VibCodes.EscapingBorrow, ta.Tokens[term.TokIdx],
                    "C# ref return requires a returnable ref-safe-context, not a local, scoped reference, or by-value struct parameter (CS8157/CS8168/CS8169)."u8);
            }
            if (term.A >= 0 && OperandContext(term.A, in fn, in types, in mir, in references, in values) > ReturnContext)
            {
                diags.ReportTok(VibCodes.EscapingBorrow, ta.Tokens[term.TokIdx],
                    "C# value return requires a returnable safe-context (CS8352/CS8347)."u8);
            }
        }
    }

    private static int DeclarationContext(int depth) => depth < FunctionContext ? FunctionContext : depth;

    private static void InferLocalContext(int localId, int instruction, int reference, int value, bool isReference,
        in MirFunc fn, in FuncLowerCtx source, in TypeTable types, in MirModuleArena mir,
        ref Vec<int> references, ref Vec<int> values, ref bool changed)
    {
        MirLocal local = mir.Locals[fn.LocalStart + localId];
        if ((local.Flags & MirLocalFlags.IsParam) != 0) return;
        if ((local.Flags & MirLocalFlags.IsTemp) == 0 && source.LocalInitializerInsts[localId] != instruction) return;
        if (isReference && reference > references[localId] && (local.Flags & MirLocalFlags.IsScoped) == 0)
        {
            references[localId] = reference;
            changed = true;
        }
        if (types.IsView(local.TypeId) && value > values[localId] && (local.Flags & MirLocalFlags.IsScoped) == 0)
        {
            values[localId] = value;
            changed = true;
        }
    }

    private static int PlaceContext(int placeId, bool reference, in MirFunc fn, in TypeTable types,
        in MirModuleArena mir, in Vec<int> references, in Vec<int> values)
    {
        if (placeId < 0) return CallerContext;
        MirPlace place = mir.Places[placeId];
        if (place.LocalId < 0 || place.LocalId >= fn.LocalCount) return FunctionContext;
        if (!reference && !types.IsView(place.TypeId)) return CallerContext;
        int context = reference ? references[place.LocalId] : values[place.LocalId];
        int currentType = mir.Locals[fn.LocalStart + place.LocalId].TypeId;
        for (int p = 0; p < place.ProjCount; p++)
        {
            MirProj projection = mir.Projs[place.ProjStart + p];
            if (reference && ((projection.Flags & 1) != 0 ||
                (projection.Kind == MirProjKinds.Field && types.Items[currentType].Kind == TypeKinds.Class)))
            {
                context = CallerContext;
            }
            if (reference && projection.Kind == MirProjKinds.Index)
            {
                int kind = types.Items[currentType].Kind;
                if (kind == TypeKinds.Span || kind == TypeKinds.ReadOnlySpan)
                {
                    context = values[place.LocalId];
                }
            }
            currentType = projection.ResultTypeId;
        }
        return context;
    }

    private static int OperandContext(int operandId, in MirFunc fn, in TypeTable types,
        in MirModuleArena mir, in Vec<int> references, in Vec<int> values)
    {
        if (operandId < 0) return CallerContext;
        MirOperand operand = mir.Operands[operandId];
        if (operand.Kind != MirOpKinds.CopyOrMove || !types.IsView(operand.TypeId)) return CallerContext;
        return PlaceContext(operand.PlaceOrLitIdx, false, in fn, in types, in mir, in references, in values);
    }

    private static int InstructionContext(in MirInst inst, bool reference, in MirFunc fn, in FuncLowerCtx source, in AstArena ast, in TypeTable types,
        in MirModuleArena mir, in Vec<int> references, in Vec<int> values)
    {
        if (inst.Op == MirOp.BindRef)
        {
            return PlaceContext(inst.B, reference, in fn, in types, in mir, in references, in values);
        }
        if (inst.Op == MirOp.Call)
        {
            return CallContext(in inst, false, in fn, in ast, in types, in mir, in references, in values);
        }
        if (inst.Op == MirOp.StackSpan)
        {
            int stackContext = source.SourceStackSpanContexts[inst.C];
            return stackContext == CallerContext ? CallerContext : DeclarationContext(stackContext - FunctionContext);
        }
        if (inst.Op == MirOp.AssignUse || inst.Op == MirOp.AssignCast)
        {
            return OperandContext(inst.A, in fn, in types, in mir, in references, in values);
        }
        if (inst.Op != MirOp.Intrinsic) return CallerContext;
        MirIntrinsic intrinsic = (MirIntrinsic)inst.A;
        bool receiverReference = intrinsic == MirIntrinsic.OptionAsRef || intrinsic == MirIntrinsic.OptionAsMut ||
            intrinsic == MirIntrinsic.OptionUnwrapMut || intrinsic == MirIntrinsic.VecIndexRef ||
            intrinsic == MirIntrinsic.VecSliceMut || intrinsic == MirIntrinsic.VecReadOnlySlice ||
            intrinsic == MirIntrinsic.VecAsSpan || intrinsic == MirIntrinsic.VecAsReadOnlySpan;
        bool receiverValue = intrinsic == MirIntrinsic.SpanIndexRef || intrinsic == MirIntrinsic.SpanSlice ||
            intrinsic == MirIntrinsic.SpanToReadOnly || intrinsic == MirIntrinsic.OptionMutAsRef ||
            intrinsic == MirIntrinsic.Utf8ViewFromSpan || intrinsic == MirIntrinsic.Utf8ViewBytes;
        if (intrinsic == MirIntrinsic.StringAsUtf8) return CallerContext;
        if (!receiverReference && !receiverValue)
        {
            if (inst.RefDestLocalId >= 0 || (inst.DestPlaceId >= 0 && types.IsView(mir.Places[inst.DestPlaceId].TypeId)))
            {
                return FunctionContext;
            }
            return CallerContext;
        }
        int context = CallerContext;
        for (int a = 0; a < inst.C; a++)
        {
            int operandId = mir.ExtraLists[inst.B + a];
            int next = OperandContext(operandId, in fn, in types, in mir, in references, in values);
            if (next > context) context = next;
        }
        int placeCount = inst.D >>> 20;
        int placeStart = inst.D & 0xFFFFF;
        for (int a = 0; a < placeCount; a++)
        {
            int next = PlaceContext(mir.ExtraLists[placeStart + a], receiverReference,
                in fn, in types, in mir, in references, in values);
            if (next > context) context = next;
        }
        return context;
    }

    private static int CallContext(in MirInst inst, bool assignmentToRef, in MirFunc fn, in AstArena ast, in TypeTable types,
        in MirModuleArena mir, in Vec<int> references, in Vec<int> values)
    {
        FuncInstance callee = mir.FuncInstances[inst.A];
        int context = CallerContext;
        for (int a = 0; a < inst.C; a++)
        {
            MirCallArg arg = mir.CallArgs[inst.B + a];
            MirParamSig parameter = mir.ParamSigs[callee.ParamSigStart + a];
            if (arg.Mode == PassMode.Out && (parameter.Flags & 2) == 0) continue;
            bool operand = ArgumentIsOperand(inst.A, a, arg.Mode, in types, in mir);
            bool scoped = IsSourceScoped(inst.A, a, in ast, in mir);
            int value = operand
                ? OperandContext(arg.OperandOrPlaceId, in fn, in types, in mir, in references, in values)
                : PlaceContext(arg.OperandOrPlaceId, false, in fn, in types, in mir, in references, in values);
            if ((!scoped || parameter.Mode != PassMode.ByValue) && value > context) context = value;
            bool referenceContributes = !operand && !scoped;
            if (assignmentToRef && (parameter.Flags & 2) == 0) referenceContributes = false;
            if (referenceContributes)
            {
                int reference = PlaceContext(arg.OperandOrPlaceId, true, in fn, in types, in mir, in references, in values);
                if (reference > context) context = reference;
            }
        }
        return context;
    }

    private static bool IsSourceScoped(int functionId, int parameterIndex, in AstArena ast, in MirModuleArena mir)
    {
        FuncInstance function = mir.FuncInstances[functionId];
        FuncDecl declaration = ast.Funcs[function.FuncDeclId];
        int offset = function.ParamSigCount - declaration.ParamCount;
        if (parameterIndex < offset) return (declaration.Flags & DeclFlags.UnscopedRefAttr) == 0;
        return (ast.Params[declaration.ParamStart + parameterIndex - offset].Flags & DeclFlags.IsScoped) != 0;
    }

    private static void CheckCallContexts(in MirInst inst, in MirFunc fn, in AstArena ast, in TokenArena ta, in TypeTable types,
        in MirModuleArena mir, in Vec<int> references, in Vec<int> values, ref DiagnosticBag diags)
    {
        for (int a = 0; a < inst.C; a++)
        {
            MirCallArg arg = mir.CallArgs[inst.B + a];
            if (arg.Mode != PassMode.Ref && arg.Mode != PassMode.Out && arg.Mode != PassMode.ThisMut) continue;
            if (ArgumentIsOperand(inst.A, a, arg.Mode, in types, in mir)) continue;
            MirPlace place = mir.Places[arg.OperandOrPlaceId];
            if (!types.IsView(place.TypeId)) continue;
            int target = PlaceContext(arg.OperandOrPlaceId, false, in fn, in types, in mir, in references, in values);
            int context = CallContext(in inst, arg.Mode != PassMode.Out, in fn, in ast, in types, in mir, in references, in values);
            if (context > target)
            {
                diags.ReportTok(VibCodes.EscapingBorrow, ta.Tokens[arg.TokIdx],
                    "C# call arguments cannot expose a narrower borrowed value through a wider ref or out argument (CS8350)."u8);
            }
        }
    }

    private static void CheckSwitchSections(in MirFunc fn, in FuncLowerCtx source, in TokenArena ta,
        in MirModuleArena mir, ref DiagnosticBag diags)
    {
        if (source.SourceSwitchSectionEnds.Count == 0) return;
        var reached = new Vec<byte>(fn.BlockCount);
        reached.Resize(fn.BlockCount, 0);
        var queue = new Vec<int>(fn.BlockCount);
        Enqueue(0, ref reached, ref queue);
        int head = 0;
        while (head < queue.Count)
        {
            int b = queue[head];
            head += 1;
            EnqueueSourceContinuations(b, in source, ref reached, ref queue);
            MirTerm term = source.SourceBlockTerms[b];
            if (term.Kind == MirTermKind.Unset) term = mir.Blocks[fn.BlockStart + b].Term;
            EnqueueTerm(in term, in mir, ref reached, ref queue);
        }
        for (int i = 0; i < source.SourceSwitchSectionEnds.Count; i++)
        {
            int b = source.SourceSwitchSectionEnds[i];
            if (b >= 0 && b < fn.BlockCount && reached[b] != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[mir.Blocks[fn.BlockStart + b].Term.TokIdx],
                    "C# switch section endpoints must not be reachable; Panic is an ordinary invocation for source reachability (CS0163/CS8070)."u8);
            }
        }
    }

    private static void EnqueueSourceContinuations(int block, in FuncLowerCtx source, ref Vec<byte> reached, ref Vec<int> queue)
    {
        for (int i = 0; i < source.SourceSwitchContinuations.Count; i++)
        {
            SourceSwitchAssignmentEdge edge = source.SourceSwitchContinuations[i];
            if (edge.Block == block) Enqueue(edge.Target, ref reached, ref queue);
        }
    }

    private static void EnqueueTerm(in MirTerm term, in MirModuleArena mir, ref Vec<byte> reached, ref Vec<int> queue)
    {
        switch (term.Kind)
        {
            case MirTermKind.Goto:
                Enqueue(term.A, ref reached, ref queue);
                break;
            case MirTermKind.Branch:
                if (term.A >= 0 && mir.Operands[term.A].Kind == MirOpKinds.BoolConst)
                {
                    Enqueue(mir.Operands[term.A].IntVal != 0 ? term.B : term.C, ref reached, ref queue);
                }
                else
                {
                    Enqueue(term.B, ref reached, ref queue);
                    Enqueue(term.C, ref reached, ref queue);
                }
                break;
            case MirTermKind.MatchOption:
                Enqueue(term.C, ref reached, ref queue);
                Enqueue(term.D, ref reached, ref queue);
                break;
            case MirTermKind.MatchResult:
                Enqueue(term.C, ref reached, ref queue);
                Enqueue(term.E, ref reached, ref queue);
                break;
            case MirTermKind.GuardOptionIsNone:
                Enqueue(term.C, ref reached, ref queue);
                Enqueue(term.D, ref reached, ref queue);
                break;
            case MirTermKind.GuardResultIsErr:
                Enqueue(term.D, ref reached, ref queue);
                Enqueue(term.E, ref reached, ref queue);
                break;
        }
    }

    private static void Enqueue(int block, ref Vec<byte> reached, ref Vec<int> queue)
    {
        if (block < 0 || block >= reached.Count || reached[block] != 0) return;
        reached[block] = 1;
        queue.Push(block);
    }
}
