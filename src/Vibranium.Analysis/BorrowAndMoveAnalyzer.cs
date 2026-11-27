using System;
using System.Collections.Generic;
using System.Linq;
using Vibranium.Compiler.Core;

namespace Vibranium.Analysis;

public static class BorrowAndMoveAnalyzer
{
    public static List<VibraniumDiagnostic> AnalyzeModule(MirModule module)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        var diagnostics = new List<VibraniumDiagnostic>();
        foreach (var fn in module.Functions)
        {
            diagnostics.AddRange(AnalyzeFunction(module, fn));
        }
        return DeduplicateAndSort(diagnostics);
    }

    public static List<VibraniumDiagnostic> AnalyzeFunction(MirModule module, MirFunction fn)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }
        if (fn is null)
        {
            throw new ArgumentNullException(nameof(fn));
        }
        if (fn.Blocks.Count == 0)
        {
            return new List<VibraniumDiagnostic>();
        }

        var engine = new FunctionAnalysisEngine(module, fn);
        return engine.Run();
    }

    internal static List<VibraniumDiagnostic> DeduplicateAndSort(IEnumerable<VibraniumDiagnostic> items)
    {
        var seen = new HashSet<(string Id, string FilePath, int Start, int Length)>();
        var list = new List<VibraniumDiagnostic>();
        foreach (var d in items)
        {
            if (seen.Add((d.Id, d.Span.FilePath ?? string.Empty, d.Span.Start, d.Span.Length)))
            {
                list.Add(d);
            }
        }

        list.Sort(static (a, b) =>
        {
            int cmp = string.CompareOrdinal(a.Span.FilePath, b.Span.FilePath);
            if (cmp != 0) return cmp;
            cmp = a.Span.Start.CompareTo(b.Span.Start);
            if (cmp != 0) return cmp;
            cmp = a.Span.Length.CompareTo(b.Span.Length);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.Id, b.Id);
        });
        return list;
    }

    private enum LocalInitKind
    {
        Uninitialized,
        PartialStruct,
        Initialized,
        Moved,
    }

    private readonly struct LocalInitState(LocalInitKind kind, string? initFieldsKey = null, bool isHeld = false) : IEquatable<LocalInitState>
    {
        public LocalInitKind Kind { get; } = kind;
        // Sorted comma-joined initialized field names when Kind == PartialStruct
        public string? InitFieldsKey { get; } = initFieldsKey;
        // D1-W: moved by a call that has not read the moved value yet, and not used since. Meet keeps it
        // only when every path holds: a use on any path leaves the plain Moved state (VIB001 only).
        public bool IsHeld { get; } = isHeld;

        public static readonly LocalInitState Uninitialized = new(LocalInitKind.Uninitialized);
        public static readonly LocalInitState Initialized = new(LocalInitKind.Initialized);
        public static readonly LocalInitState Moved = new(LocalInitKind.Moved);
        public static readonly LocalInitState MovedHeld = new(LocalInitKind.Moved, isHeld: true);

        public bool IsFieldInitialized(string fieldName)
        {
            if (Kind == LocalInitKind.Initialized) return true;
            if (Kind != LocalInitKind.PartialStruct || string.IsNullOrEmpty(InitFieldsKey)) return false;
            var parts = InitFieldsKey!.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == fieldName) return true;
            }
            return false;
        }

        public LocalInitState WithFieldInitialized(string fieldName, int totalStructFields)
        {
            if (Kind == LocalInitKind.Initialized) return this;
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (Kind == LocalInitKind.PartialStruct && !string.IsNullOrEmpty(InitFieldsKey))
            {
                foreach (var p in InitFieldsKey!.Split(','))
                {
                    if (p.Length > 0) set.Add(p);
                }
            }
            set.Add(fieldName);
            if (totalStructFields > 0 && set.Count >= totalStructFields)
            {
                return Initialized;
            }
            return new LocalInitState(LocalInitKind.PartialStruct, string.Join(",", set));
        }

        public static LocalInitState Meet(LocalInitState a, LocalInitState b)
        {
            if (a.Equals(b)) return a;
            if (a.Kind == LocalInitKind.Moved || b.Kind == LocalInitKind.Moved)
            {
                return Moved;
            }
            if (a.Kind == LocalInitKind.Uninitialized || b.Kind == LocalInitKind.Uninitialized)
            {
                return Uninitialized;
            }
            // One is Initialized and one is PartialStruct, or both are PartialStruct: intersect fields
            if (a.Kind == LocalInitKind.Initialized && b.Kind == LocalInitKind.PartialStruct)
            {
                return b;
            }
            if (b.Kind == LocalInitKind.Initialized && a.Kind == LocalInitKind.PartialStruct)
            {
                return a;
            }
            var setA = new SortedSet<string>((a.InitFieldsKey ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            var setB = new SortedSet<string>((b.InitFieldsKey ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            setA.IntersectWith(setB);
            if (setA.Count == 0) return Uninitialized;
            return new LocalInitState(LocalInitKind.PartialStruct, string.Join(",", setA));
        }

        public bool Equals(LocalInitState other) =>
            Kind == other.Kind && IsHeld == other.IsHeld && string.Equals(InitFieldsKey, other.InitFieldsKey, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is LocalInitState s && Equals(s);

        public override int GetHashCode() => ((int)Kind * 397) ^ (IsHeld ? 1 : 0) ^ (InitFieldsKey?.GetHashCode() ?? 0);
    }

    private sealed class MoveFlowState : IEquatable<MoveFlowState>
    {
        public readonly LocalInitState[] Init;
        public readonly bool[] MayBeLiveLinear;

        public MoveFlowState(int localCount)
        {
            Init = new LocalInitState[localCount];
            MayBeLiveLinear = new bool[localCount];
        }

        private MoveFlowState(LocalInitState[] init, bool[] mayBeLiveLinear)
        {
            Init = init;
            MayBeLiveLinear = mayBeLiveLinear;
        }

        public MoveFlowState Clone()
        {
            var initCopy = new LocalInitState[Init.Length];
            Array.Copy(Init, initCopy, Init.Length);
            var liveCopy = new bool[MayBeLiveLinear.Length];
            Array.Copy(MayBeLiveLinear, liveCopy, MayBeLiveLinear.Length);
            return new MoveFlowState(initCopy, liveCopy);
        }

        public bool MergeFrom(MoveFlowState incoming)
        {
            bool changed = false;
            for (int i = 0; i < Init.Length; i++)
            {
                var mergedInit = LocalInitState.Meet(Init[i], incoming.Init[i]);
                if (!mergedInit.Equals(Init[i]))
                {
                    Init[i] = mergedInit;
                    changed = true;
                }
                if (incoming.MayBeLiveLinear[i] && !MayBeLiveLinear[i])
                {
                    MayBeLiveLinear[i] = true;
                    changed = true;
                }
            }
            return changed;
        }

        public bool Equals(MoveFlowState? other)
        {
            if (other is null) return false;
            for (int i = 0; i < Init.Length; i++)
            {
                if (!Init[i].Equals(other.Init[i]) || MayBeLiveLinear[i] != other.MayBeLiveLinear[i])
                {
                    return false;
                }
            }
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as MoveFlowState);

        public override int GetHashCode()
        {
            int h = 17;
            for (int i = 0; i < Init.Length; i++)
            {
                h = (h * 31) + Init[i].GetHashCode();
                h = (h * 31) + (MayBeLiveLinear[i] ? 1 : 0);
            }
            return h;
        }
    }

    private readonly record struct PathSegment(bool IsWildcard, string FieldName)
    {
        public static PathSegment Field(string name) => new(false, name);
        public static PathSegment Wildcard => new(true, "*");
    }

    private sealed class PlacePath(int rootLocalId, IReadOnlyList<PathSegment> segments) : IEquatable<PlacePath>
    {
        public int RootLocalId { get; } = rootLocalId;
        public IReadOnlyList<PathSegment> Segments { get; } = segments;

        public static PlacePath FromPlace(MirPlace place)
        {
            if (place.Projections.Count == 0)
            {
                return new PlacePath(place.LocalId, Array.Empty<PathSegment>());
            }
            var segs = new List<PathSegment>(place.Projections.Count);
            foreach (var proj in place.Projections)
            {
                switch (proj)
                {
                    case MirProjection.Field f:
                        segs.Add(PathSegment.Field(f.FieldName));
                        break;
                    case MirProjection.Index:
                    case MirProjection.Deref:
                        segs.Add(PathSegment.Wildcard);
                        break;
                }
            }
            return new PlacePath(place.LocalId, segs);
        }

        public bool Overlaps(PlacePath other)
        {
            if (RootLocalId != other.RootLocalId)
            {
                return false;
            }
            int min = Math.Min(Segments.Count, other.Segments.Count);
            for (int i = 0; i < min; i++)
            {
                var a = Segments[i];
                var b = other.Segments[i];
                if (a.IsWildcard || b.IsWildcard)
                {
                    continue;
                }
                if (!string.Equals(a.FieldName, b.FieldName, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        public bool Equals(PlacePath? other)
        {
            if (other is null || RootLocalId != other.RootLocalId || Segments.Count != other.Segments.Count)
            {
                return false;
            }
            for (int i = 0; i < Segments.Count; i++)
            {
                if (!Segments[i].Equals(other.Segments[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as PlacePath);

        public override int GetHashCode()
        {
            int h = RootLocalId * 397;
            for (int i = 0; i < Segments.Count; i++)
            {
                h = (h * 31) + Segments[i].GetHashCode();
            }
            return h;
        }
    }

    private enum LoanKind
    {
        PlaceBorrow,
        StepCursor,
        LocalStack,
        SnapshotOrigin,
        SwitchMatchRefSlot,
    }

    private sealed class Loan : IEquatable<Loan>
    {
        public LoanKind Kind { get; }
        public PlacePath? Path { get; }
        public int CursorOrStackLocalId { get; }
        public bool IsExclusive { get; }
        public bool IsTwoPhaseReserved { get; }

        public bool IsActiveExclusive => IsExclusive && !IsTwoPhaseReserved;

        private Loan(LoanKind kind, PlacePath? path, int localId, bool isExclusive, bool isTwoPhaseReserved = false)
        {
            Kind = kind;
            Path = path;
            CursorOrStackLocalId = localId;
            IsExclusive = isExclusive;
            IsTwoPhaseReserved = isExclusive && isTwoPhaseReserved;
        }

        public static Loan ForPlace(PlacePath path, bool isExclusive, bool isTwoPhaseReserved = false) =>
            new(LoanKind.PlaceBorrow, path, path.RootLocalId, isExclusive, isTwoPhaseReserved);

        public static Loan ForStep(int cursorLocalId) =>
            new(LoanKind.StepCursor, null, cursorLocalId, true);

        public static Loan ForLocalStack(int spanLocalId) =>
            new(LoanKind.LocalStack, null, spanLocalId, true);

        // Ancestry only: a saved destination borrows the referent, not the ref carrier's storage.
        public static Loan ForSnapshotOrigin(int carrierId) =>
            new(LoanKind.SnapshotOrigin, null, carrierId, false);

        // The match depends on both the evaluated referent and the live access slot for a ref local.
        public static Loan ForSwitchMatchRefSlot(int localId) => new(LoanKind.SwitchMatchRefSlot, null, localId, isExclusive: false);

        public Loan AsActivated() =>
            IsTwoPhaseReserved ? new Loan(Kind, Path, CursorOrStackLocalId, IsExclusive, isTwoPhaseReserved: false) : this;

        public bool Equals(Loan? other)
        {
            if (other is null ||
                Kind != other.Kind ||
                IsExclusive != other.IsExclusive ||
                IsTwoPhaseReserved != other.IsTwoPhaseReserved ||
                CursorOrStackLocalId != other.CursorOrStackLocalId)
            {
                return false;
            }
            return Equals(Path, other.Path);
        }

        public override bool Equals(object? obj) => Equals(obj as Loan);

        public override int GetHashCode()
        {
            int h = (int)Kind;
            h = (h * 31) + CursorOrStackLocalId;
            h = (h * 31) + (IsExclusive ? 1 : 0);
            h = (h * 31) + (IsTwoPhaseReserved ? 1 : 0);
            h = (h * 31) + (Path?.GetHashCode() ?? 0);
            return h;
        }
    }

    private sealed class ProvenanceState(int localCount)
    {
        private readonly HashSet<Loan>?[] _loansByLocal = new HashSet<Loan>?[localCount];

        public IReadOnlyCollection<Loan> GetLoans(int localId)
        {
            if (localId < 0 || localId >= _loansByLocal.Length)
            {
                return Array.Empty<Loan>();
            }
            return (IReadOnlyCollection<Loan>?)_loansByLocal[localId] ?? Array.Empty<Loan>();
        }

        public void SetLoans(int localId, IEnumerable<Loan> loans)
        {
            if (localId < 0 || localId >= _loansByLocal.Length)
            {
                return;
            }
            var set = new HashSet<Loan>(loans);
            _loansByLocal[localId] = set.Count > 0 ? set : null;
        }

        public void AddLoans(int localId, IEnumerable<Loan> loans)
        {
            if (localId < 0 || localId >= _loansByLocal.Length)
            {
                return;
            }
            var set = _loansByLocal[localId];
            foreach (var loan in loans)
            {
                set ??= (_loansByLocal[localId] = new HashSet<Loan>());
                set.Add(loan);
            }
        }

        public void ActivateLoans(int localId)
        {
            if (localId < 0 || localId >= _loansByLocal.Length)
            {
                return;
            }
            var set = _loansByLocal[localId];
            if (set is null || set.Count == 0)
            {
                return;
            }
            bool anyReserved = false;
            foreach (var l in set)
            {
                if (l.IsTwoPhaseReserved)
                {
                    anyReserved = true;
                    break;
                }
            }
            if (!anyReserved)
            {
                return;
            }
            var updated = new HashSet<Loan>();
            foreach (var l in set)
            {
                updated.Add(l.AsActivated());
            }
            _loansByLocal[localId] = updated;
        }

        public void ClearLoans(int localId)
        {
            if (localId >= 0 && localId < _loansByLocal.Length)
            {
                _loansByLocal[localId] = null;
            }
        }

        public void StripPlaceLoansForLocal(int targetLocalId)
        {
            for (int i = 0; i < _loansByLocal.Length; i++)
            {
                var set = _loansByLocal[i];
                if (set is null) continue;
                set.RemoveWhere(l => l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == targetLocalId);
                if (set.Count == 0) _loansByLocal[i] = null;
            }
        }

        public ProvenanceState Clone()
        {
            var copy = new ProvenanceState(_loansByLocal.Length);
            for (int i = 0; i < _loansByLocal.Length; i++)
            {
                if (_loansByLocal[i] is { Count: > 0 } s)
                {
                    copy._loansByLocal[i] = new HashSet<Loan>(s);
                }
            }
            return copy;
        }

        public bool MergeFrom(ProvenanceState incoming)
        {
            bool changed = false;
            for (int i = 0; i < _loansByLocal.Length; i++)
            {
                var inc = incoming._loansByLocal[i];
                if (inc is null || inc.Count == 0) continue;
                var cur = _loansByLocal[i];
                if (cur is null)
                {
                    _loansByLocal[i] = new HashSet<Loan>(inc);
                    changed = true;
                }
                else
                {
                    foreach (var l in inc)
                    {
                        if (cur.Add(l))
                        {
                            changed = true;
                        }
                    }
                }
            }
            return changed;
        }
    }

    private sealed class FunctionAnalysisEngine
    {
        private readonly MirModule _module;
        private readonly MirFunction _fn;
        private readonly int _localCount;
        private readonly Dictionary<int, MirBasicBlock> _blocksById = new();
        private readonly HashSet<int> _reachableBlocks = new();
        private readonly bool[] _isBorrowCarrier;
        private readonly bool[] _isBorrowedPatternBinding;
        private readonly bool[] _isSharedPatternBinding;
        private readonly bool[] _isLinearOwnedLocal;
        private readonly int[] _compilerRefRootLocal;
        private readonly Dictionary<int, MirPlace> _initializationSnapshots = new();
        private readonly Dictionary<int, MirPlace> _twoPhaseReservations = new();
        private readonly Dictionary<string, IReadOnlyList<string>> _structFieldLeaves = new(StringComparer.Ordinal);
        private readonly List<VibraniumDiagnostic> _diagnostics = new();

        // Source ranges identify operands retained by the same call/store, including nested evaluation.
        private readonly Dictionary<int, List<VibraniumSpan>> _activationSpans = new();
        private readonly HashSet<VibraniumSpan> _callInvocations = new();
        private readonly HashSet<(MirPlace Place, VibraniumSpan Span)> _rejectedActivationMoves = new();
        private readonly Dictionary<int, MirPlace> _sourceLocalAliases = new();
        private readonly HashSet<int> _foreachLoanLocals;
        private readonly Dictionary<int, HashSet<int>> _liveEntry = new();
        private readonly Dictionary<(int BlockId, int StmtIndex), HashSet<int>> _liveAfterStmt = new();
        private readonly Dictionary<int, HashSet<int>> _liveBeforeTerm = new();

        public FunctionAnalysisEngine(MirModule module, MirFunction fn)
        {
            _module = module;
            _fn = fn;
            _localCount = fn.Locals.Count;
            _foreachLoanLocals = new HashSet<int>(fn.Locals.Where(local => local.IsForeachLoan).Select(local => local.Id));
            _isBorrowCarrier = new bool[_localCount];
            _isBorrowedPatternBinding = new bool[_localCount];
            _isSharedPatternBinding = new bool[_localCount];
            _isLinearOwnedLocal = new bool[_localCount];
            _compilerRefRootLocal = new int[_localCount];
            for (int i = 0; i < _localCount; i++)
            {
                _compilerRefRootLocal[i] = -1;
            }

            foreach (var b in fn.Blocks)
            {
                _blocksById[b.Id] = b;
                foreach (var statement in b.Statements)
                {
                    if (statement is MirStatement.BindRef snapshot &&
                        snapshot.RefLocalId >= 0 && snapshot.RefLocalId < _localCount &&
                        _fn.Locals[snapshot.RefLocalId].IsCompilerTemp &&
                        (snapshot.IsOut || snapshot.IsAssignmentTargetSnapshot && IsStructFieldStorage(snapshot.Target)))
                    {
                        _initializationSnapshots[snapshot.RefLocalId] = snapshot.Target;
                    }
                    if (Reservation(statement) is (var temp, var reserved))
                    {
                        _twoPhaseReservations[temp] = reserved;
                    }
                }
            }
        }

        public List<VibraniumDiagnostic> Run()
        {
            ComputeReachableBlocks();
            PreclassifyLocals();
            ComputeBorrowLiveness();
            CollectCallActivations();
            // Borrow analysis records invalid transitions before definite-init consumes operands.
            RunBorrowProvenanceAndImmutabilityAnalysis();
            RunMoveAndLinearAnalysis();
            return DeduplicateAndSort(_diagnostics);
        }

        private void Emit(string id, string message, VibraniumSpan span)
        {
            foreach (var local in _fn.Locals)
            {
                if (local.IsCompilerTemp)
                {
                    message = message.Replace($"'{local.Name}'", $"'{DisplayLocal(local.Id)}'");
                }
            }
            _diagnostics.Add(new VibraniumDiagnostic(id, message, span));
        }

        private string DisplayLocal(int localId)
        {
            var seen = new HashSet<int>();
            while (_sourceLocalAliases.TryGetValue(localId, out var sourcePlace) && seen.Add(localId))
            {
                localId = sourcePlace.LocalId;
            }
            if (localId < 0 || localId >= _localCount) return "temporary value";
            var local = _fn.Locals[localId];
            return local.IsCompilerTemp ? "temporary value" : local.Name;
        }

        private string DisplayPlace(MirPlace place)
        {
            var fields = place.Projections.OfType<MirProjection.Field>().Select(field => "." + field.FieldName).ToList();
            int localId = place.LocalId;
            var seen = new HashSet<int>();
            while (_sourceLocalAliases.TryGetValue(localId, out var sourcePlace) && seen.Add(localId))
            {
                fields.InsertRange(0, sourcePlace.Projections.OfType<MirProjection.Field>().Select(field => "." + field.FieldName));
                localId = sourcePlace.LocalId;
            }
            return DisplayLocal(localId) + string.Concat(fields);
        }

        private void CollectCallActivations()
        {
            var preparationInputs = new Dictionary<int, HashSet<int>>();
            foreach (var block in _fn.Blocks)
            foreach (var statement in block.Statements)
            {
                var carriers = new HashSet<int>();
                switch (statement)
                {
                    case MirStatement.BindRef bind:
                        if (_fn.Locals[bind.RefLocalId].IsCompilerTemp) _sourceLocalAliases[bind.RefLocalId] = bind.Target;
                        break;
                    case MirStatement.Assign assign:
                        if (assign.Target.Projections.Count == 0 &&
                            assign.Rvalue is MirRvalue.Use { Operand: MirOperand.CopyOrMove value } &&
                            _fn.Locals[assign.Target.LocalId].IsCompilerTemp)
                        {
                            _sourceLocalAliases[assign.Target.LocalId] = value.Place;
                        }
                        AddPlaceTargetCarrierUses(assign.Target, carriers);
                        break;
                    case MirStatement.Call call:
                        _callInvocations.Add(call.Span);
                        foreach (var arg in call.Args)
                        {
                            if (arg.ValueOperand is not null) AddOperandCarrierUses(arg.ValueOperand, carriers);
                            if (arg.BorrowPlace is not null) AddPlaceReadCarrierUses(arg.BorrowPlace, carriers);
                        }
                        break;
                    case MirStatement.Intrinsic intrinsic:
                        foreach (var operand in intrinsic.ValueArgs) AddOperandCarrierUses(operand, carriers);
                        foreach (var place in intrinsic.PlaceArgs) AddPlaceReadCarrierUses(place, carriers);
                        break;
                }
                int destination = -1;
                var inputs = new HashSet<int>();
                switch (statement)
                {
                    case MirStatement.BindRef bind:
                        destination = bind.RefLocalId;
                        AddPlaceReadCarrierUses(bind.Target, inputs);
                        break;
                    case MirStatement.Assign { Target.Projections.Count: 0, Rvalue: MirRvalue.Use use } assign:
                        if (_isBorrowCarrier[assign.Target.LocalId])
                        {
                            destination = assign.Target.LocalId;
                            AddOperandCarrierUses(use.Operand, inputs);
                        }
                        break;
                    case MirStatement.Intrinsic intrinsic when IsPureBorrowPreparation(intrinsic.Kind):
                        destination = intrinsic.RefDestLocalId ?? (intrinsic.Dest is { Projections.Count: 0 } dest ? dest.LocalId : -1);
                        foreach (var operand in intrinsic.ValueArgs) AddOperandCarrierUses(operand, inputs);
                        foreach (var place in intrinsic.PlaceArgs) AddPlaceReadCarrierUses(place, inputs);
                        break;
                }
                if (destination >= 0 && _fn.Locals[destination].IsCompilerTemp)
                {
                    if (!preparationInputs.TryGetValue(destination, out var previous))
                    {
                        previous = new HashSet<int>();
                        preparationInputs.Add(destination, previous);
                    }
                    previous.UnionWith(inputs.Where(input => _fn.Locals[input].IsCompilerTemp));
                }
                foreach (int carrier in carriers)
                {
                    if (!_activationSpans.TryGetValue(carrier, out var spans))
                    {
                        spans = new List<VibraniumSpan>();
                        _activationSpans.Add(carrier, spans);
                    }
                    spans.Add(statement.Span);
                }
            }
            // Intermediate ref/view projections belong to the same terminal activation.
            // Do not cross a user call: its evaluation is a distinct invocation, even
            // when its result becomes an operand of an enclosing call.
            bool changed;
            do
            {
                changed = false;
                foreach (var preparation in preparationInputs)
                {
                    if (!_activationSpans.TryGetValue(preparation.Key, out var activations)) continue;
                    foreach (int input in preparation.Value)
                    {
                        if (!_activationSpans.TryGetValue(input, out var inherited))
                        {
                            inherited = new List<VibraniumSpan>();
                            _activationSpans.Add(input, inherited);
                        }
                        foreach (var activation in activations.ToArray())
                        {
                            if (!inherited.Contains(activation))
                            {
                                inherited.Add(activation);
                                changed = true;
                            }
                        }
                    }
                }
            } while (changed);
        }

        private static bool IsPureBorrowPreparation(MirIntrinsicKind kind) => kind is
            MirIntrinsicKind.VecAsSpan or MirIntrinsicKind.VecAsReadOnlySpan or MirIntrinsicKind.VecSliceMut or
            MirIntrinsicKind.SpanSlice or MirIntrinsicKind.SpanToReadOnly or MirIntrinsicKind.OptionAsRef or
            MirIntrinsicKind.OptionAsMut or MirIntrinsicKind.OptionMutAsRef or MirIntrinsicKind.SpanToUtf8View or
            MirIntrinsicKind.Utf8ViewToSpan or MirIntrinsicKind.VecIndexRef or MirIntrinsicKind.SpanIndexRef;

        private HashSet<int> CapturePreparationLive(int destination, HashSet<int> live)
        {
            if (destination < 0 || destination >= _localCount || !_fn.Locals[destination].IsCompilerTemp ||
                !_activationSpans.TryGetValue(destination, out var activations)) return live;
            var result = new HashSet<int>(live);
            result.Remove(destination);
            result.RemoveWhere(carrier => _fn.Locals[carrier].IsCompilerTemp &&
                _activationSpans.TryGetValue(carrier, out var siblingActivations) &&
                activations.Any(activation => _callInvocations.Contains(activation) && siblingActivations.Contains(activation)));
            return result;
        }

        private bool IsRetainedCallOperand(int carrier, VibraniumSpan operandSpan) =>
            _activationSpans.TryGetValue(carrier, out var spans) && spans.Any(span =>
                span.FilePath == operandSpan.FilePath && span.Start <= operandSpan.Start &&
                operandSpan.Start + operandSpan.Length <= span.Start + span.Length);

        // N68: a call operand moves or reads its own storage. Only a place reached through a borrow also
        // touches what that borrow names, so only then is a sibling operand's loan on the referent a conflict.
        private List<PlacePath> CallOperandPaths(MirPlace place, List<PlacePath> effectivePaths) =>
            place.Projections.Count > 0 && place.Projections[0] is MirProjection.Deref ||
            place.LocalId >= 0 && place.LocalId < _localCount &&
            (_isBorrowedPatternBinding[place.LocalId] || _fn.Locals[place.LocalId].PassKind != MirPassKind.ByValue)
                ? effectivePaths
                : new List<PlacePath> { PlacePath.FromPlace(place) };

        private void ComputeReachableBlocks()
        {
            int entryId = _fn.Blocks[0].Id;
            var queue = new Queue<int>();
            _reachableBlocks.Add(entryId);
            queue.Enqueue(entryId);

            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                if (!_blocksById.TryGetValue(id, out var block))
                {
                    continue;
                }
                if (BlockPanicsBeforeTerminator(block) >= 0)
                {
                    continue;
                }
                foreach (int succ in GetSuccessors(block.Terminator))
                {
                    if (_blocksById.ContainsKey(succ) && _reachableBlocks.Add(succ))
                    {
                        queue.Enqueue(succ);
                    }
                }
            }
        }

        private static int BlockPanicsBeforeTerminator(MirBasicBlock block)
        {
            for (int i = 0; i < block.Statements.Count; i++)
            {
                if (block.Statements[i] is MirStatement.Intrinsic { Kind: MirIntrinsicKind.Panic })
                {
                    return i;
                }
            }
            return -1;
        }

        private static IEnumerable<int> GetSuccessors(MirTerminator term)
        {
            switch (term)
            {
                case MirTerminator.Goto g:
                    yield return g.TargetBlock;
                    break;
                case MirTerminator.Branch b:
                    yield return b.TrueBlock;
                    yield return b.FalseBlock;
                    break;
                case MirTerminator.MatchOption mo:
                    yield return mo.SomeBlock;
                    yield return mo.NoneBlock;
                    break;
                case MirTerminator.MatchResult mr:
                    yield return mr.OkBlock;
                    yield return mr.ErrBlock;
                    break;
                case MirTerminator.GuardResultIsErr gr:
                    yield return gr.ErrBlock;
                    yield return gr.OkBlock;
                    break;
                case MirTerminator.GuardOptionIsNone go:
                    yield return go.NoneBlock;
                    yield return go.SomeBlock;
                    break;
            }
        }

        private void PreclassifyLocals()
        {
            // Classify pattern bindings from MatchOption / MatchResult terminators (iterative for nested pattern bindings)
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (int bid in _reachableBlocks)
                {
                    var term = _blocksById[bid].Terminator;
                    switch (term)
                    {
                        case MirTerminator.MatchOption mo:
                            ClassifyMatchBinding(mo.Scrutinee, mo.SomeBindingLocalId, ref changed);
                            break;
                        case MirTerminator.MatchResult mr:
                            ClassifyMatchBinding(mr.Scrutinee, mr.OkBindingLocalId, ref changed);
                            ClassifyMatchBinding(mr.Scrutinee, mr.ErrBindingLocalId, ref changed);
                            break;
                    }
                }
            }

            for (int i = 0; i < _localCount; i++)
            {
                var local = _fn.Locals[i];
                bool isRefStruct = local.Type is MirType.StructType st &&
                    (st.IsRefStruct || (_module.Structs.TryGetValue(st.Name, out var sdef) && sdef.IsRefStruct));

                _isBorrowCarrier[i] =
                    local.IsRefLocal ||
                    _module.IsViewLike(local.Type) ||
                    isRefStruct ||
                    _isBorrowedPatternBinding[i];

                _isLinearOwnedLocal[i] =
                    _module.IsLinearResource(local.Type) &&
                    !_isBorrowedPatternBinding[i] &&
                    (!local.IsParam || local.PassKind == MirPassKind.ByValue);
            }
        }

        private bool MatchBindsIndependentCopy(MirMatchScrutinee scrutinee, int bindingId)
        {
            if ((uint)bindingId >= (uint)_localCount) return false;
            var local = _fn.Locals[bindingId];
            // The lowerer gives a value designation an ordinary local. The native match
            // loads and stores its payload on the matched edge before the guard or result.
            // Keep view provenance and mutable-cursor bindings out of this copy case.
            return (scrutinee is MirMatchScrutinee.PlaceScrutinee
                        { Type: MirType.OptionType or MirType.ResultType or MirType.OptionRefType } ||
                    scrutinee is MirMatchScrutinee.RvalueScrutinee { IsViewCursor: true } cursor &&
                        GetOperandType(cursor.Operand) is MirType.OptionRefType) &&
                !local.IsRefLocal && !_module.IsViewLike(local.Type) && _module.IsCopy(local.Type);
        }

        private void ClassifyMatchBinding(MirMatchScrutinee scrutinee, int bindingId, ref bool changed)
        {
            if (bindingId < 0 || bindingId >= _localCount)
            {
                return;
            }

            if (MatchBindsIndependentCopy(scrutinee, bindingId)) return;

            switch (scrutinee)
            {
                case MirMatchScrutinee.PlaceScrutinee ps:
                {
                    if (!_isBorrowedPatternBinding[bindingId])
                    {
                        _isBorrowedPatternBinding[bindingId] = true;
                        changed = true;
                    }

                    bool isNonCopyBinding = !_module.IsCopy(_fn.Locals[bindingId].Type);
                    bool shared = ps.Type is MirType.OptionRefType ||
                        (!ps.IsMutableCursor && ps.Type is MirType.OptionMutType) ||
                        (ps.Type is MirType.OptionType or MirType.ResultType && isNonCopyBinding) ||
                        IsMutatingThroughSharedBorrow(ps.Place, isIndirectOrMethodReceiver: true);

                    if (shared && !_isSharedPatternBinding[bindingId])
                    {
                        _isSharedPatternBinding[bindingId] = true;
                        changed = true;
                    }
                    break;
                }
                case MirMatchScrutinee.RvalueScrutinee rs when rs.IsViewCursor:
                {
                    if (!_isBorrowedPatternBinding[bindingId])
                    {
                        _isBorrowedPatternBinding[bindingId] = true;
                        changed = true;
                    }
                    bool shared = GetOperandType(rs.Operand) is MirType.OptionRefType;
                    if (shared && !_isSharedPatternBinding[bindingId])
                    {
                        _isSharedPatternBinding[bindingId] = true;
                        changed = true;
                    }
                    break;
                }
            }
        }

        private static MirType GetOperandType(MirOperand op) =>
            op switch
            {
                MirOperand.CopyOrMove cm => cm.Type,
                MirOperand.Const c => c.Type,
                _ => MirType.Void,
            };

        private static bool IsMutatingIntrinsicOnFirstPlace(MirIntrinsicKind kind, bool refDestIsMutable) =>
            kind switch
            {
                MirIntrinsicKind.OptionAsMut => true,
                MirIntrinsicKind.OptionTake => true,
                MirIntrinsicKind.OptionReplace => true,
                MirIntrinsicKind.OptionUnwrapMut => true,
                MirIntrinsicKind.OptionMutSet => true,
                MirIntrinsicKind.OptionMutTake => true,
                MirIntrinsicKind.OptionMutReplace => true,
                MirIntrinsicKind.VecPush => true,
                MirIntrinsicKind.VecPop => true,
                MirIntrinsicKind.VecClear => true,
                MirIntrinsicKind.VecTruncate => true,
                MirIntrinsicKind.VecReserve => true,
                MirIntrinsicKind.VecExtend => true,
                MirIntrinsicKind.VecResize => true,
                MirIntrinsicKind.VecSliceMut => true,
                MirIntrinsicKind.VecAsSpan => true,
                MirIntrinsicKind.VecIndexRef => refDestIsMutable,
                MirIntrinsicKind.SpanIndexRef => refDestIsMutable,
                _ => false,
            };

        private bool IsMutatingThroughSharedBorrow(MirPlace place, bool isIndirectOrMethodReceiver)
        {
            if (place.LocalId < 0 || place.LocalId >= _localCount)
            {
                return false;
            }

            var root = _fn.Locals[place.LocalId];
            if (root.PassKind is MirPassKind.In or MirPassKind.ImplicitThisShared)
            {
                return true;
            }

            if (_isSharedPatternBinding[place.LocalId])
            {
                return true;
            }

            if (root.IsRefLocal && !root.IsRefMutable && (place.Projections.Count > 0 || isIndirectOrMethodReceiver))
            {
                return true;
            }

            if (root.Type is MirType.OptionRefType or MirType.SpanType { IsReadOnly: true } or MirType.Utf8ScanViewType &&
                (place.Projections.Count > 0 || isIndirectOrMethodReceiver))
            {
                return true;
            }

            // Check readonly field projections:
            // Direct mutation/ref of a readonly field, or mutating inline value fields inside a readonly struct field (before heap indirection).
            bool insideReadonlyValueProjection = false;
            for (int i = 0; i < place.Projections.Count; i++)
            {
                if (place.Projections[i] is MirProjection.Field f)
                {
                    if (i == place.Projections.Count - 1 && f.IsReadOnlyField)
                    {
                        return true;
                    }
                    if (f.ThroughClassHeap)
                    {
                        insideReadonlyValueProjection = f.IsReadOnlyField && !_module.IsCopy(f.FieldType) && f.FieldType is MirType.StructType;
                    }
                    else if (f.IsReadOnlyField)
                    {
                        insideReadonlyValueProjection = true;
                    }
                }
                else if (place.Projections[i] is MirProjection.Deref)
                {
                    insideReadonlyValueProjection = false;
                }
            }

            return insideReadonlyValueProjection;
        }

        // =====================================================================
        // Backward Liveness of Borrow-Bearing Locals (NLL / Polonius)
        // =====================================================================

        private void ComputeBorrowLiveness()
        {
            foreach (int bid in _reachableBlocks)
            {
                _liveEntry[bid] = new HashSet<int>();
                _liveBeforeTerm[bid] = new HashSet<int>();
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (int bid in _reachableBlocks.Reverse())
                {
                    var block = _blocksById[bid];
                    int panicIdx = BlockPanicsBeforeTerminator(block);

                    var beforeTerm = new HashSet<int>();
                    if (panicIdx < 0)
                    {
                        foreach (int succ in GetSuccessors(block.Terminator))
                        {
                            if (!_liveEntry.TryGetValue(succ, out var succLive))
                            {
                                continue;
                            }
                            foreach (int v in succLive)
                            {
                                if (!IsKilledOnTerminatorEdge(block.Terminator, v))
                                {
                                    beforeTerm.Add(v);
                                }
                            }
                        }
                        AddTerminatorCarrierUses(block.Terminator, beforeTerm);
                    }

                    // Foreach owns a lexical loan, unlike ordinary NLL views. A
                    // flagged carrier has no loans before initialization or after
                    // forward ScopeExit, but remains live on every body path,
                    // including paths which diverge and have no cleanup edge.
                    beforeTerm.UnionWith(_foreachLoanLocals);
                    _liveBeforeTerm[bid] = beforeTerm;

                    var current = new HashSet<int>(beforeTerm);
                    int lastStmt = panicIdx >= 0 ? panicIdx : block.Statements.Count - 1;

                    for (int i = block.Statements.Count - 1; i > lastStmt; i--)
                    {
                        _liveAfterStmt[(bid, i)] = new HashSet<int>();
                    }

                    for (int i = lastStmt; i >= 0; i--)
                    {
                        if (i == panicIdx)
                        {
                            current.Clear();
                        }
                        current.UnionWith(_foreachLoanLocals);
                        _liveAfterStmt[(bid, i)] = new HashSet<int>(current);
                        ApplyBackwardStatementTransfer(block.Statements[i], current);
                    }

                    var prevEntry = _liveEntry[bid];
                    if (!prevEntry.SetEquals(current))
                    {
                        _liveEntry[bid] = current;
                        changed = true;
                    }
                }
            }
        }

        private static bool IsKilledOnTerminatorEdge(MirTerminator term, int localId) =>
            term switch
            {
                MirTerminator.MatchOption mo => mo.SomeBindingLocalId == localId,
                MirTerminator.MatchResult mr => mr.OkBindingLocalId == localId || mr.ErrBindingLocalId == localId,
                MirTerminator.GuardResultIsErr gr => gr.OkLocalId == localId || gr.ErrWrapperLocalId == localId,
                MirTerminator.GuardOptionIsNone go => go.SomeValueLocalId == localId,
                _ => false,
            };

        private void AddTerminatorCarrierUses(MirTerminator term, HashSet<int> uses)
        {
            switch (term)
            {
                case MirTerminator.Branch b:
                    AddOperandCarrierUses(b.Condition, uses);
                    break;
                case MirTerminator.MatchOption mo:
                    AddScrutineeCarrierUses(mo.Scrutinee, uses);
                    break;
                case MirTerminator.MatchResult mr:
                    AddScrutineeCarrierUses(mr.Scrutinee, uses);
                    break;
                case MirTerminator.GuardResultIsErr gr:
                    AddOperandCarrierUses(gr.ConsumedResult, uses);
                    break;
                case MirTerminator.GuardOptionIsNone go:
                    AddOperandCarrierUses(go.ConsumedOption, uses);
                    break;
                case MirTerminator.Return ret:
                    if (ret.Value is not null) AddOperandCarrierUses(ret.Value, uses);
                    if (ret.RefReturnPlace is not null) AddPlaceReadCarrierUses(ret.RefReturnPlace, uses);
                    break;
            }
        }

        private void AddScrutineeCarrierUses(MirMatchScrutinee scrutinee, HashSet<int> uses)
        {
            switch (scrutinee)
            {
                case MirMatchScrutinee.PlaceScrutinee ps:
                    AddPlaceReadCarrierUses(ps.Place, uses);
                    break;
                case MirMatchScrutinee.RvalueScrutinee rs:
                    AddOperandCarrierUses(rs.Operand, uses);
                    break;
            }
        }

        private void ApplyBackwardStatementTransfer(MirStatement stmt, HashSet<int> live)
        {
            switch (stmt)
            {
                case MirStatement.Assign a:
                    if (a.Target.Projections.Count == 0)
                    {
                        live.Remove(a.Target.LocalId);
                    }
                    else
                    {
                        AddPlaceTargetCarrierUses(a.Target, live);
                    }
                    AddRvalueCarrierUses(a.Rvalue, live);
                    break;

                case MirStatement.RememberRefRepointLoan remember:
                    live.Remove(remember.SnapshotLocalId);
                    live.Add(remember.SourceRefLocalId);
                    break;

                case MirStatement.KeepAnalysisBorrow keep:
                    live.Add(keep.CarrierLocalId);
                    break;

                case MirStatement.BindRef br:
                    TransferRefOverwriteLiveness(br, br.RefLocalId, live);
                    AddPlaceReadCarrierUses(br.Target, live);
                    break;

                case MirStatement.Call c:
                    if (c.Dest is not null)
                    {
                        if (c.Dest.Projections.Count == 0) live.Remove(c.Dest.LocalId);
                        else AddPlaceTargetCarrierUses(c.Dest, live);
                    }
                    if (c.RefDestLocalId is int refDest)
                    {
                        TransferRefOverwriteLiveness(c, refDest, live);
                    }
                    foreach (var arg in c.Args)
                    {
                        if (arg.PassKind == MirPassKind.Out && arg.BorrowPlace is { Projections.Count: 0 } outPlace)
                        {
                            live.Remove(outPlace.LocalId);
                        }
                    }
                    foreach (var arg in c.Args)
                    {
                        if (arg.Hold is int callHold) live.Add(callHold);
                        if (arg.ValueOperand is not null) AddOperandCarrierUses(arg.ValueOperand, live);
                        if (arg.BorrowPlace is not null)
                        {
                            if (arg.PassKind == MirPassKind.Out && arg.BorrowPlace.Projections.Count == 0)
                            {
                                continue;
                            }
                            AddPlaceReadCarrierUses(arg.BorrowPlace, live);
                        }
                    }
                    break;

                case MirStatement.Intrinsic intr:
                    if (intr.Dest is not null)
                    {
                        if (intr.Dest.Projections.Count == 0) live.Remove(intr.Dest.LocalId);
                        else AddPlaceTargetCarrierUses(intr.Dest, live);
                    }
                    if (intr.RefDestLocalId is int intrRefDest)
                    {
                        TransferRefOverwriteLiveness(intr, intrRefDest, live);
                    }
                    foreach (var v in intr.ValueArgs) AddOperandCarrierUses(v, live);
                    foreach (var p in intr.PlaceArgs) AddPlaceReadCarrierUses(p, live);
                    if (intr.Hold is int intrHold) live.Add(intrHold);
                    break;

                case MirStatement.DisposeResource dr:
                    AddCarrierIfRelevant(dr.LocalId, live);
                    break;

                case MirStatement.ScopeExit:
                    // ScopeExit does not use borrow carriers; a borrow dies at its last real read/deref.
                    break;
            }
        }

        private static void TransferRefOverwriteLiveness(MirStatement producer, int destination, HashSet<int> live)
        {
            // Kill the new value when going backwards over the store, then generate an old-value use
            // if the source RHS names it. This carries the old loan through all RHS preparation, even
            // across CFG edges and after the last emitted load from the old reference.
            if (producer.RefRepointNamesDestination) live.Add(destination);
            else live.Remove(destination);
        }

        private void AddRvalueCarrierUses(MirRvalue rv, HashSet<int> uses)
        {
            switch (rv)
            {
                case MirRvalue.Use u:
                    AddOperandCarrierUses(u.Operand, uses);
                    break;
                case MirRvalue.Binary b:
                    AddOperandCarrierUses(b.Left, uses);
                    AddOperandCarrierUses(b.Right, uses);
                    break;
                case MirRvalue.Unary u:
                    AddOperandCarrierUses(u.Operand, uses);
                    break;
                case MirRvalue.Cast c:
                    AddOperandCarrierUses(c.Operand, uses);
                    break;
                case MirRvalue.AllocClass ac:
                    foreach (var op in ac.FieldValues) AddOperandCarrierUses(op, uses);
                    break;
                case MirRvalue.ConstructStruct cs:
                    foreach (var op in cs.FieldValues) AddOperandCarrierUses(op, uses);
                    break;
                case MirRvalue.MakeSomeWrapper sw:
                    AddOperandCarrierUses(sw.Value, uses);
                    break;
                case MirRvalue.MakeOkWrapper ow:
                    AddOperandCarrierUses(ow.Value, uses);
                    break;
                case MirRvalue.MakeErrWrapper ew:
                    AddOperandCarrierUses(ew.Error, uses);
                    break;
                case MirRvalue.LiftOptionSome los:
                    AddOperandCarrierUses(los.Value, uses);
                    break;
                case MirRvalue.LiftOptionNone:
                    break;
                case MirRvalue.LiftResultOk lro:
                    AddOperandCarrierUses(lro.Value, uses);
                    break;
                case MirRvalue.LiftResultErr lre:
                    AddOperandCarrierUses(lre.Error, uses);
                    break;
                case MirRvalue.UnwrapSomeWrapper usw:
                    AddOperandCarrierUses(usw.Wrapper, uses);
                    break;
                case MirRvalue.UnwrapOkWrapper uow:
                    AddOperandCarrierUses(uow.Wrapper, uses);
                    break;
                case MirRvalue.UnwrapErrWrapper uew:
                    AddOperandCarrierUses(uew.Wrapper, uses);
                    break;
                case MirRvalue.StackSpan ss:
                    foreach (var el in ss.Elements) AddOperandCarrierUses(el, uses);
                    break;
            }
        }

        private void AddOperandCarrierUses(MirOperand op, HashSet<int> uses)
        {
            if (op is MirOperand.CopyOrMove cm)
            {
                AddPlaceReadCarrierUses(cm.Place, uses);
            }
        }

        private void AddPlaceReadCarrierUses(MirPlace place, HashSet<int> uses)
        {
            AddCarrierIfRelevant(place.LocalId, uses);
            foreach (var proj in place.Projections)
            {
                if (proj is MirProjection.Index idx)
                {
                    AddOperandCarrierUses(idx.IndexOperand, uses);
                }
            }
        }

        private void AddPlaceTargetCarrierUses(MirPlace target, HashSet<int> uses)
        {
            if (target.Projections.Count > 0)
            {
                AddCarrierIfRelevant(target.LocalId, uses);
            }
            foreach (var proj in target.Projections)
            {
                if (proj is MirProjection.Index idx)
                {
                    AddOperandCarrierUses(idx.IndexOperand, uses);
                }
            }
        }

        private void AddCarrierIfRelevant(int localId, HashSet<int> uses)
        {
            if (localId >= 0 && localId < _localCount && _isBorrowCarrier[localId])
            {
                uses.Add(localId);
            }
        }

        // =====================================================================
        // VIB001, VIB005, VIB006: Move, Initialization & Linear Resource Pass
        // =====================================================================

        private void RunMoveAndLinearAnalysis()
        {
            int entryId = _fn.Blocks[0].Id;
            var entryStates = new Dictionary<int, MoveFlowState>();

            var initial = new MoveFlowState(_localCount);
            for (int i = 0; i < _localCount; i++)
            {
                var local = _fn.Locals[i];
                if (local.IsParam && local.PassKind != MirPassKind.Out)
                {
                    initial.Init[i] = LocalInitState.Initialized;
                    if (_isLinearOwnedLocal[i])
                    {
                        initial.MayBeLiveLinear[i] = true;
                    }
                }
                else
                {
                    initial.Init[i] = LocalInitState.Uninitialized;
                }
            }

            entryStates[entryId] = initial;
            var worklist = new Queue<int>();
            var inQueue = new HashSet<int> { entryId };
            worklist.Enqueue(entryId);

            while (worklist.Count > 0)
            {
                int bid = worklist.Dequeue();
                inQueue.Remove(bid);
                if (!_reachableBlocks.Contains(bid)) continue;

                var block = _blocksById[bid];
                var state = entryStates[bid].Clone();
                int panicIdx = BlockPanicsBeforeTerminator(block);
                int stmtLimit = panicIdx >= 0 ? panicIdx + 1 : block.Statements.Count;

                for (int i = 0; i < stmtLimit; i++)
                {
                    ApplyMoveStatement(block.Statements[i], state, emitDiagnostics: false);
                }

                if (panicIdx >= 0)
                {
                    continue;
                }

                foreach (var (succId, edgeState) in ApplyMoveTerminator(block.Terminator, state, emitDiagnostics: false))
                {
                    if (!_reachableBlocks.Contains(succId)) continue;
                    if (!entryStates.TryGetValue(succId, out var existing))
                    {
                        entryStates[succId] = edgeState;
                        if (inQueue.Add(succId)) worklist.Enqueue(succId);
                    }
                    else if (existing.MergeFrom(edgeState))
                    {
                        if (inQueue.Add(succId)) worklist.Enqueue(succId);
                    }
                }
            }

            // Emit diagnostics once on converged entry states
            foreach (int bid in _fn.Blocks.Select(b => b.Id))
            {
                if (!_reachableBlocks.Contains(bid) || !entryStates.TryGetValue(bid, out var startState))
                {
                    continue;
                }
                var block = _blocksById[bid];
                var state = startState.Clone();
                int panicIdx = BlockPanicsBeforeTerminator(block);
                int stmtLimit = panicIdx >= 0 ? panicIdx + 1 : block.Statements.Count;

                for (int i = 0; i < stmtLimit; i++)
                {
                    ApplyMoveStatement(block.Statements[i], state, emitDiagnostics: true);
                }

                if (panicIdx < 0)
                {
                    _ = ApplyMoveTerminator(block.Terminator, state, emitDiagnostics: true).ToList();
                }
            }
        }

        private void ApplyMoveStatement(MirStatement stmt, MoveFlowState state, bool emitDiagnostics)
        {
            switch (stmt)
            {
                case MirStatement.Assign a:
                    EvaluateTargetBaseBeforeWrite(a.Target, state, a.Span, emitDiagnostics);
                    EvaluateMoveRvalue(a.Rvalue, state, emitDiagnostics);
                    VerifyTargetBaseStillValidAfterRhs(a.Target, state, a.Span, emitDiagnostics);
                    ApplyWriteToPlace(a.Target, state, a.Span, emitDiagnostics);
                    break;

                case MirStatement.BindRef br:
                    if (IsSwitchMatchBorrow(br.RefLocalId) || IsCallHold(br.RefLocalId)) break;
                    if (br.RefLocalId >= 0 && br.RefLocalId < _localCount &&
                        _fn.Locals[br.RefLocalId].IsCompilerTemp &&
                        br.Target.LocalId >= 0 && br.Target.LocalId < _localCount)
                    {
                        int rootId = br.Target.LocalId;
                        int hops = 0;
                        while (rootId >= 0 && rootId < _localCount && _compilerRefRootLocal[rootId] >= 0 && hops++ < 32)
                        {
                            rootId = _compilerRefRootLocal[rootId];
                        }
                        _compilerRefRootLocal[br.RefLocalId] = rootId;
                    }
                    EvaluatePlaceBorrowed(br.Target, isOut: br.IsOut || _initializationSnapshots.ContainsKey(br.RefLocalId), state, br.Span, emitDiagnostics);
                    SetLocalInitialized(br.RefLocalId, state, br.Span, emitDiagnostics);
                    break;

                case MirStatement.Call c:
                    if (c.Dest is not null)
                    {
                        EvaluateTargetBaseBeforeWrite(c.Dest, state, c.Span, emitDiagnostics);
                    }
                    foreach (var arg in c.Args)
                    {
                        if (arg.ValueOperand is not null)
                        {
                            EvaluateMoveOperand(arg.ValueOperand, forceCopy: arg.ValueOperand is MirOperand.CopyOrMove { Type: MirType.SpanType or MirType.Utf8ScanViewType }, state, emitDiagnostics);
                        }
                        if (arg.BorrowPlace is not null)
                        {
                            EvaluatePlaceBorrowed(arg.BorrowPlace, isOut: arg.PassKind == MirPassKind.Out, state, arg.Span, emitDiagnostics);
                        }
                    }
                    foreach (var arg in c.Args)
                    {
                        if (arg.PassKind == MirPassKind.Out && arg.BorrowPlace is not null)
                        {
                            VerifyTargetBaseStillValidAfterRhs(arg.BorrowPlace, state, arg.Span, emitDiagnostics);
                            ApplyWriteToPlace(arg.BorrowPlace, state, arg.Span, emitDiagnostics);
                        }
                    }
                    foreach (var arg in c.Args)
                    {
                        if (arg.ReleasesMovedPlace is int held && state.Init[held].IsHeld)
                        {
                            state.Init[held] = LocalInitState.Moved;
                        }
                    }
                    if (c.Dest is not null)
                    {
                        VerifyTargetBaseStillValidAfterRhs(c.Dest, state, c.Span, emitDiagnostics);
                        ApplyWriteToPlace(c.Dest, state, c.Span, emitDiagnostics);
                    }
                    if (c.RefDestLocalId is int callRefDest)
                    {
                        SetLocalInitialized(callRefDest, state, c.Span, emitDiagnostics);
                    }
                    break;

                case MirStatement.Intrinsic intr:
                    if (intr.Dest is not null)
                    {
                        EvaluateTargetBaseBeforeWrite(intr.Dest, state, intr.Span, emitDiagnostics);
                    }
                    bool nonConsumingValueInspection = IsNonConsumingValueIntrinsic(intr.Kind);
                    foreach (var v in intr.ValueArgs)
                    {
                        EvaluateMoveOperand(v, forceCopy: nonConsumingValueInspection, state, emitDiagnostics);
                    }
                    foreach (var p in intr.PlaceArgs)
                    {
                        EvaluatePlaceBorrowed(p, isOut: false, state, intr.Span, emitDiagnostics);
                    }
                    if (intr.ReleasesMovedPlace is int intrHeld && state.Init[intrHeld].IsHeld)
                    {
                        state.Init[intrHeld] = LocalInitState.Moved;
                    }
                    if (intr.Dest is not null)
                    {
                        VerifyTargetBaseStillValidAfterRhs(intr.Dest, state, intr.Span, emitDiagnostics);
                        ApplyWriteToPlace(intr.Dest, state, intr.Span, emitDiagnostics);
                    }
                    if (intr.RefDestLocalId is int intrRefDest)
                    {
                        SetLocalInitialized(intrRefDest, state, intr.Span, emitDiagnostics);
                    }
                    break;

                case MirStatement.DisposeResource dr:
                    if (dr.LocalId >= 0 && dr.LocalId < _localCount)
                    {
                        var cur = state.Init[dr.LocalId];
                        if (emitDiagnostics && cur.Kind != LocalInitKind.Initialized)
                        {
                            Emit(
                                VibraniumDiagnosticIds.UseAfterMove,
                                $"Cannot dispose {(cur.Kind == LocalInitKind.Moved ? "moved" : "uninitialized")} resource '{_fn.Locals[dr.LocalId].Name}'.",
                                dr.Span);
                        }
                        state.Init[dr.LocalId] = LocalInitState.Moved;
                        state.MayBeLiveLinear[dr.LocalId] = false;
                    }
                    break;

                case MirStatement.ScopeExit se:
                    foreach (int id in se.ExitingLocals)
                    {
                        if (id < 0 || id >= _localCount) continue;
                        if (_isLinearOwnedLocal[id] && state.MayBeLiveLinear[id])
                        {
                            if (emitDiagnostics)
                            {
                                var local = _fn.Locals[id];
                                Emit(
                                    VibraniumDiagnosticIds.LinearResourceViolation,
                                    $"Linear IDisposable resource '{local.Name}' must be bound with 'using var' or moved/disposed on all paths.",
                                    se.Span);
                            }
                            state.MayBeLiveLinear[id] = false;
                        }
                        if (!_fn.Locals[id].IsParam)
                        {
                            state.Init[id] = LocalInitState.Uninitialized;
                        }
                    }
                    break;
            }
        }

        private IEnumerable<(int SuccId, MoveFlowState State)> ApplyMoveTerminator(
            MirTerminator term,
            MoveFlowState state,
            bool emitDiagnostics)
        {
            switch (term)
            {
                case MirTerminator.Goto g:
                    yield return (g.TargetBlock, state.Clone());
                    break;

                case MirTerminator.Branch b:
                    EvaluateMoveOperand(b.Condition, forceCopy: false, state, emitDiagnostics);
                    yield return (b.TrueBlock, state.Clone());
                    yield return (b.FalseBlock, state.Clone());
                    break;

                case MirTerminator.MatchOption mo:
                {
                    switch (mo.Scrutinee)
                    {
                        case MirMatchScrutinee.PlaceScrutinee ps:
                            EvaluatePlaceBorrowed(ps.Place, isOut: false, state, mo.Span, emitDiagnostics);
                            break;
                        case MirMatchScrutinee.RvalueScrutinee rs:
                            EvaluateMoveOperand(rs.Operand, forceCopy: false, state, emitDiagnostics);
                            break;
                    }

                    var someState = state.Clone();
                    if (mo.SomeBindingLocalId >= 0 && mo.SomeBindingLocalId < _localCount)
                    {
                        SetLocalInitialized(mo.SomeBindingLocalId, someState, mo.Span, emitDiagnostics);
                    }

                    var noneState = state.Clone();
                    if (mo.SomeBindingLocalId >= 0 && mo.SomeBindingLocalId < _localCount)
                    {
                        noneState.Init[mo.SomeBindingLocalId] = LocalInitState.Uninitialized;
                        noneState.MayBeLiveLinear[mo.SomeBindingLocalId] = false;
                    }

                    yield return (mo.SomeBlock, someState);
                    yield return (mo.NoneBlock, noneState);
                    break;
                }

                case MirTerminator.MatchResult mr:
                {
                    switch (mr.Scrutinee)
                    {
                        case MirMatchScrutinee.PlaceScrutinee ps:
                            EvaluatePlaceBorrowed(ps.Place, isOut: false, state, mr.Span, emitDiagnostics);
                            break;
                        case MirMatchScrutinee.RvalueScrutinee rs:
                            EvaluateMoveOperand(rs.Operand, forceCopy: false, state, emitDiagnostics);
                            break;
                    }

                    var okState = state.Clone();
                    if (mr.OkBindingLocalId >= 0 && mr.OkBindingLocalId < _localCount)
                    {
                        SetLocalInitialized(mr.OkBindingLocalId, okState, mr.Span, emitDiagnostics);
                    }
                    if (mr.ErrBindingLocalId >= 0 && mr.ErrBindingLocalId < _localCount)
                    {
                        okState.Init[mr.ErrBindingLocalId] = LocalInitState.Uninitialized;
                        okState.MayBeLiveLinear[mr.ErrBindingLocalId] = false;
                    }

                    var errState = state.Clone();
                    if (mr.ErrBindingLocalId >= 0 && mr.ErrBindingLocalId < _localCount)
                    {
                        SetLocalInitialized(mr.ErrBindingLocalId, errState, mr.Span, emitDiagnostics);
                    }
                    if (mr.OkBindingLocalId >= 0 && mr.OkBindingLocalId < _localCount)
                    {
                        errState.Init[mr.OkBindingLocalId] = LocalInitState.Uninitialized;
                        errState.MayBeLiveLinear[mr.OkBindingLocalId] = false;
                    }

                    yield return (mr.OkBlock, okState);
                    yield return (mr.ErrBlock, errState);
                    break;
                }

                case MirTerminator.GuardResultIsErr gr:
                {
                    EvaluateMoveOperand(gr.ConsumedResult, forceCopy: false, state, emitDiagnostics);

                    var errState = state.Clone();
                    if (gr.ErrWrapperLocalId >= 0 && gr.ErrWrapperLocalId < _localCount)
                    {
                        SetLocalInitialized(gr.ErrWrapperLocalId, errState, gr.Span, emitDiagnostics);
                    }
                    if (gr.OkLocalId >= 0 && gr.OkLocalId < _localCount)
                    {
                        errState.Init[gr.OkLocalId] = LocalInitState.Uninitialized;
                        errState.MayBeLiveLinear[gr.OkLocalId] = false;
                    }

                    var okState = state.Clone();
                    if (gr.OkLocalId >= 0 && gr.OkLocalId < _localCount)
                    {
                        SetLocalInitialized(gr.OkLocalId, okState, gr.Span, emitDiagnostics);
                    }
                    if (gr.ErrWrapperLocalId >= 0 && gr.ErrWrapperLocalId < _localCount)
                    {
                        okState.Init[gr.ErrWrapperLocalId] = LocalInitState.Uninitialized;
                        okState.MayBeLiveLinear[gr.ErrWrapperLocalId] = false;
                    }

                    yield return (gr.ErrBlock, errState);
                    yield return (gr.OkBlock, okState);
                    break;
                }

                case MirTerminator.GuardOptionIsNone go:
                {
                    EvaluateMoveOperand(go.ConsumedOption, forceCopy: false, state, emitDiagnostics);

                    var noneState = state.Clone();
                    if (go.SomeValueLocalId >= 0 && go.SomeValueLocalId < _localCount)
                    {
                        noneState.Init[go.SomeValueLocalId] = LocalInitState.Uninitialized;
                        noneState.MayBeLiveLinear[go.SomeValueLocalId] = false;
                    }

                    var someState = state.Clone();
                    if (go.SomeValueLocalId >= 0 && go.SomeValueLocalId < _localCount)
                    {
                        SetLocalInitialized(go.SomeValueLocalId, someState, go.Span, emitDiagnostics);
                    }

                    yield return (go.NoneBlock, noneState);
                    yield return (go.SomeBlock, someState);
                    break;
                }

                case MirTerminator.Return ret:
                {
                    if (ret.Value is not null)
                    {
                        EvaluateMoveOperand(ret.Value, forceCopy: false, state, emitDiagnostics);
                    }
                    if (ret.RefReturnPlace is not null)
                    {
                        EvaluatePlaceBorrowed(ret.RefReturnPlace, isOut: false, state, ret.Span, emitDiagnostics);
                    }

                    if (emitDiagnostics)
                    {
                        for (int i = 0; i < _localCount; i++)
                        {
                            var local = _fn.Locals[i];
                            if (_isLinearOwnedLocal[i] && state.MayBeLiveLinear[i])
                            {
                                Emit(
                                    VibraniumDiagnosticIds.LinearResourceViolation,
                                    $"Linear IDisposable resource '{local.Name}' must be bound with 'using var' or moved/disposed before return.",
                                    ret.Span);
                            }
                            if (local.IsParam && local.PassKind == MirPassKind.Out && state.Init[i].Kind != LocalInitKind.Initialized &&
                                !(state.Init[i].Kind != LocalInitKind.Moved && local.Type is MirType.StructType empty &&
                                  _module.Structs.ContainsKey(empty.Name) && GetStructFieldLeaves(empty).Count == 0))
                            {
                                Emit(
                                    VibraniumDiagnosticIds.UseAfterMove,
                                    $"Out parameter '{local.Name}' must be initialized before return.",
                                    ret.Span);
                            }
                        }
                    }
                    break;
                }

                case MirTerminator.Unreachable:
                    break;
            }
        }

        private void EvaluateMoveRvalue(MirRvalue rv, MoveFlowState state, bool emitDiagnostics)
        {
            switch (rv)
            {
                case MirRvalue.Use u:
                    EvaluateMoveOperand(u.Operand, forceCopy: u.IsCallOperandReborrow, state, emitDiagnostics);
                    break;
                case MirRvalue.Binary b:
                    EvaluateMoveOperand(b.Left, forceCopy: false, state, emitDiagnostics);
                    EvaluateMoveOperand(b.Right, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.Unary u:
                    EvaluateMoveOperand(u.Operand, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.Cast c:
                    EvaluateMoveOperand(c.Operand, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.AllocClass ac:
                    foreach (var op in ac.FieldValues) EvaluateMoveOperand(op, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.ConstructStruct cs:
                    foreach (var op in cs.FieldValues) EvaluateMoveOperand(op, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.MakeSomeWrapper sw:
                    EvaluateMoveOperand(sw.Value, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.MakeOkWrapper ow:
                    EvaluateMoveOperand(ow.Value, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.MakeErrWrapper ew:
                    EvaluateMoveOperand(ew.Error, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.LiftOptionSome los:
                    EvaluateMoveOperand(los.Value, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.LiftOptionNone:
                    break;
                case MirRvalue.LiftResultOk lro:
                    EvaluateMoveOperand(lro.Value, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.LiftResultErr lre:
                    EvaluateMoveOperand(lre.Error, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.UnwrapSomeWrapper usw:
                    EvaluateMoveOperand(usw.Wrapper, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.UnwrapOkWrapper uow:
                    EvaluateMoveOperand(uow.Wrapper, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.UnwrapErrWrapper uew:
                    EvaluateMoveOperand(uew.Wrapper, forceCopy: false, state, emitDiagnostics);
                    break;
                case MirRvalue.StackSpan ss:
                    foreach (var el in ss.Elements) EvaluateMoveOperand(el, forceCopy: false, state, emitDiagnostics);
                    break;
            }
        }

        private void EvaluateMoveOperand(MirOperand op, bool forceCopy, MoveFlowState state, bool emitDiagnostics)
        {
            if (op is not MirOperand.CopyOrMove cm)
            {
                return;
            }

            foreach (var proj in cm.Place.Projections)
            {
                if (proj is MirProjection.Index idx)
                {
                    EvaluateMoveOperand(idx.IndexOperand, forceCopy: false, state, emitDiagnostics);
                }
            }

            int lid = cm.Place.LocalId;
            if (lid < 0 || lid >= _localCount)
            {
                return;
            }

            if (cm.ReleasesMovedPlace is int held && state.Init[held].IsHeld)
            {
                // D1-W: here the call reads the value it moved out of that local, which ends the hold.
                state.Init[held] = LocalInitState.Moved;
            }
            var local = _fn.Locals[lid];
            bool wasInitialized = state.Init[lid].Kind == LocalInitKind.Initialized;
            CheckPlaceIsInitialized(cm.Place, state, cm.Span, emitDiagnostics);

            if (forceCopy || _module.IsCopy(cm.Type))
            {
                return;
            }

            // Moving a non-Copy value
            if (local.IsUsingVar)
            {
                if (emitDiagnostics)
                {
                    Emit(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"Cannot move 'using' resource variable '{local.Name}'.",
                        cm.Span);
                }
                return;
            }

            bool isBorrowedRoot =
                local.PassKind is MirPassKind.In or MirPassKind.Ref or MirPassKind.Out or MirPassKind.ImplicitThisShared or MirPassKind.ImplicitThisMut ||
                _isBorrowedPatternBinding[lid];

            if (cm.Place.Projections.Count > 0 || isBorrowedRoot)
            {
                if (emitDiagnostics)
                {
                    string message = cm.Type is MirType.OptionType
                        ? "Cannot move non-Copy Option<T> out of a field, indexed element, or borrowed place; use .Take() on mutable Option<T> places."
                        : "Cannot move non-Copy value out of a field, indexed element, or borrowed place.";
                    Emit(VibraniumDiagnosticIds.IllegalFieldOrElementMove, message, cm.Span);
                }
                return;
            }

            if (_rejectedActivationMoves.Contains((cm.Place, cm.Span)))
            {
                // Borrow rejection suppresses move-state transfer, not the form checks
                // above. Preserve linear-obligation recovery without a later VIB001.
                state.MayBeLiveLinear[lid] = false;
                return;
            }

            // D1-W: a receiver that was already moved is VIB001 at this move and is not held.
            state.Init[lid] = cm.HoldsMovedPlace && wasInitialized ? LocalInitState.MovedHeld : LocalInitState.Moved;
            state.MayBeLiveLinear[lid] = false;
        }

        private void EvaluatePlaceBorrowed(MirPlace place, bool isOut, MoveFlowState state, VibraniumSpan span, bool emitDiagnostics)
        {
            place = ResolveInitializationSnapshot(place);
            foreach (var proj in place.Projections)
            {
                if (proj is MirProjection.Index idx)
                {
                    EvaluateMoveOperand(idx.IndexOperand, forceCopy: false, state, emitDiagnostics);
                }
            }

            if (isOut && (place.Projections.Count == 0 || IsStructFieldStorage(place)))
            {
                // An out destination is written, not read. A stack-struct field
                // path needs valid storage (and an unmoved owner), not an old value.
                EvaluateTargetBaseBeforeWrite(place, state, span, emitDiagnostics);
                return;
            }

            CheckPlaceIsInitialized(place, state, span, emitDiagnostics);
        }

        private bool IsStructFieldStorage(MirPlace place) =>
            place.LocalId >= 0 && place.LocalId < _localCount &&
            _fn.Locals[place.LocalId] is { Type: MirType.StructType, IsRefLocal: false, PassKind: MirPassKind.ByValue or MirPassKind.Out } &&
            place.Projections.Count > 0 && place.Projections.All(p => p is MirProjection.Field { ThroughClassHeap: false });

        private MirPlace ResolveInitializationSnapshot(MirPlace place) =>
            place.Projections.Count == 1 && place.Projections[0] is MirProjection.Deref &&
            _initializationSnapshots.TryGetValue(place.LocalId, out var target) ? target : place;

        private static bool IsFieldOrDescendant(string leaf, string path) =>
            leaf == path || leaf.StartsWith(path + ".", StringComparison.Ordinal);

        private IReadOnlyList<string> GetStructFieldLeaves(MirType.StructType type)
        {
            if (_structFieldLeaves.TryGetValue(type.Name, out var cached))
            {
                return cached;
            }
            var leaves = new List<string>();
            var active = new HashSet<string>(StringComparer.Ordinal);
            AddFields(type, "");
            _structFieldLeaves.Add(type.Name, leaves);
            return leaves;

            void AddFields(MirType.StructType current, string prefix)
            {
                if (!_module.Structs.TryGetValue(current.Name, out var definition))
                {
                    return;
                }
                if (!active.Add(current.Name))
                {
                    leaves.Add(prefix);
                    return;
                }
                // Empty ordinary structs have no assignment obligations. Keep an
                // empty linear resource atomic: its token still needs acquisition.
                if (definition.Fields.Count == 0 && definition.IsDisposable)
                {
                    leaves.Add(prefix);
                }
                foreach (var field in definition.Fields)
                {
                    string path = prefix.Length == 0 ? field.Name : prefix + "." + field.Name;
                    if (field.Type is MirType.StructType nested && _module.Structs.ContainsKey(nested.Name))
                    {
                        AddFields(nested, path);
                    }
                    else
                    {
                        leaves.Add(path);
                    }
                }
                active.Remove(current.Name);
            }
        }

        private static bool IsNonConsumingValueIntrinsic(MirIntrinsicKind kind) =>
            kind is
                MirIntrinsicKind.StringAsUtf8 or
                MirIntrinsicKind.SpanToUtf8View or
                MirIntrinsicKind.Utf8ViewToSpan or
                MirIntrinsicKind.Utf8ScanRaw or
                MirIntrinsicKind.OptionIsSome or
                MirIntrinsicKind.ResultIsOk or
                MirIntrinsicKind.VecCount or
                MirIntrinsicKind.VecCapacity or
                MirIntrinsicKind.SpanLength or
                MirIntrinsicKind.IoWriteInt64 or
                MirIntrinsicKind.IoWriteUInt64 or
                MirIntrinsicKind.IoWriteBool or
                MirIntrinsicKind.IoWriteStr or
                MirIntrinsicKind.IoWriteLineEmpty or
                MirIntrinsicKind.IoWriteLineInt64 or
                MirIntrinsicKind.IoWriteLineUInt64 or
                MirIntrinsicKind.IoWriteLineBool or
                MirIntrinsicKind.IoWriteLineStr or
                MirIntrinsicKind.StdioWriteOut or
                MirIntrinsicKind.StdioWriteErr;

        private void VerifyTargetBaseStillValidAfterRhs(MirPlace target, MoveFlowState state, VibraniumSpan span, bool emitDiagnostics)
        {
            if (target.Projections.Count == 0 || target.LocalId < 0 || target.LocalId >= _localCount)
            {
                return;
            }

            int checkLocalId = target.LocalId;
            if (_fn.Locals[checkLocalId].IsCompilerTemp && _compilerRefRootLocal[checkLocalId] >= 0)
            {
                checkLocalId = _compilerRefRootLocal[checkLocalId];
            }
            var local = _fn.Locals[checkLocalId];
            var cur = state.Init[checkLocalId];
            if (cur.Kind == LocalInitKind.Moved)
            {
                if (emitDiagnostics)
                {
                    Emit(VibraniumDiagnosticIds.UseAfterMove, $"Use of moved value '{local.Name}'.", span);
                }
            }
        }

        private void EvaluateTargetBaseBeforeWrite(MirPlace target, MoveFlowState state, VibraniumSpan span, bool emitDiagnostics)
        {
            foreach (var proj in target.Projections)
            {
                if (proj is MirProjection.Index idx)
                {
                    EvaluateMoveOperand(idx.IndexOperand, forceCopy: false, state, emitDiagnostics);
                }
            }

            if (target.Projections.Count == 0 || target.LocalId < 0 || target.LocalId >= _localCount)
            {
                return;
            }

            var local = _fn.Locals[target.LocalId];
            var cur = state.Init[target.LocalId];

            if (cur.Kind == LocalInitKind.Moved)
            {
                if (emitDiagnostics)
                {
                    Emit(VibraniumDiagnosticIds.UseAfterMove, $"Use of moved value '{local.Name}'.", span);
                }
                return;
            }

            if (cur.Kind is LocalInitKind.Uninitialized or LocalInitKind.PartialStruct)
            {
                bool structFieldInit = IsStructFieldStorage(target);

                if (!structFieldInit && emitDiagnostics)
                {
                    Emit(VibraniumDiagnosticIds.UseAfterMove, $"Use of uninitialized value '{local.Name}'.", span);
                }
            }
        }

        private void CheckPlaceIsInitialized(MirPlace place, MoveFlowState state, VibraniumSpan span, bool emitDiagnostics)
        {
            place = ResolveInitializationSnapshot(place);
            if (place.LocalId >= 0 && place.LocalId < _localCount && state.Init[place.LocalId].IsHeld)
            {
                // D1-W, "moved wins": an argument that uses the moved receiver is VIB001 only, even if it then writes it.
                state.Init[place.LocalId] = LocalInitState.Moved;
            }
            if (!emitDiagnostics || place.LocalId < 0 || place.LocalId >= _localCount)
            {
                return;
            }

            var local = _fn.Locals[place.LocalId];
            var cur = state.Init[place.LocalId];

            if (cur.Kind == LocalInitKind.Initialized)
            {
                return;
            }

            if (cur.Kind != LocalInitKind.Moved && local.Type is MirType.StructType structType)
            {
                var allLeaves = GetStructFieldLeaves(structType);
                if (allLeaves.Count == 0 && _module.Structs.ContainsKey(structType.Name))
                {
                    return;
                }
                var fields = place.Projections.TakeWhile(p => p is MirProjection.Field { ThroughClassHeap: false })
                    .Cast<MirProjection.Field>().ToArray();
                string path = string.Join(".", fields.Select(f => f.FieldName));
                var leaves = allLeaves.Where(leaf => IsFieldOrDescendant(leaf, path)).ToArray();
                if (path.Length > 0 && ((leaves.Length > 0 && leaves.All(cur.IsFieldInitialized)) ||
                    (fields[fields.Length - 1].FieldType is MirType.StructType empty &&
                     _module.Structs.ContainsKey(empty.Name) && GetStructFieldLeaves(empty).Count == 0)))
                {
                    return;
                }
            }

            string reason = cur.Kind == LocalInitKind.Moved ? "moved" : "uninitialized";
            Emit(VibraniumDiagnosticIds.UseAfterMove, $"Use of {reason} value '{local.Name}'.", span);
        }

        private void ApplyWriteToPlace(MirPlace target, MoveFlowState state, VibraniumSpan span, bool emitDiagnostics)
        {
            // Keep field-specific initialization when argument evaluation captured
            // its address before evaluating subsequent arguments. The borrow/loan
            // carrier remains intact; writing it must not initialize sibling fields.
            target = ResolveInitializationSnapshot(target);
            int lid = target.LocalId;
            if (lid < 0 || lid >= _localCount)
            {
                return;
            }

            if (emitDiagnostics && state.Init[lid].IsHeld)
            {
                Emit(VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                    $"Cannot write '{_fn.Locals[lid].Name}' while the call that moved it as its receiver evaluates its arguments.", span);
            }

            if (target.Projections.Count == 0)
            {
                SetLocalInitialized(lid, state, span, emitDiagnostics);
                return;
            }

            var local = _fn.Locals[lid];
            if (local.Type is MirType.StructType st && IsStructFieldStorage(target) &&
                state.Init[lid].Kind is LocalInitKind.Uninitialized or LocalInitKind.PartialStruct)
            {
                if (emitDiagnostics && _module.NeedsMemoryDrop(st))
                {
                    Emit(VibraniumDiagnosticIds.Unsupported,
                        "A struct with owned fields must be initialized in one assignment; initializing it field by field (including through out) is not supported in Vibranium v0.2.",
                        span);
                }
                string path = string.Join(".", target.Projections.Cast<MirProjection.Field>().Select(f => f.FieldName));
                var leaves = GetStructFieldLeaves(st);
                foreach (string leaf in leaves.Where(leaf => IsFieldOrDescendant(leaf, path)))
                {
                    state.Init[lid] = state.Init[lid].WithFieldInitialized(leaf, leaves.Count);
                }
                if (state.Init[lid].Kind == LocalInitKind.Initialized && _isLinearOwnedLocal[lid])
                {
                    state.MayBeLiveLinear[lid] = true;
                }
            }
        }

        private void SetLocalInitialized(int localId, MoveFlowState state, VibraniumSpan span, bool emitDiagnostics)
        {
            if (localId < 0 || localId >= _localCount)
            {
                return;
            }

            if (_isLinearOwnedLocal[localId] && state.MayBeLiveLinear[localId])
            {
                if (emitDiagnostics)
                {
                    Emit(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"Cannot overwrite live linear IDisposable resource '{_fn.Locals[localId].Name}' without disposing or moving it first.",
                        span);
                }
            }

            state.Init[localId] = LocalInitState.Initialized;
            if (_isLinearOwnedLocal[localId])
            {
                state.MayBeLiveLinear[localId] = true;
            }
        }

        // =====================================================================
        // VIB002, VIB003, VIB004, VIB007: Polonius Borrow, Immutability & Escape Pass
        // =====================================================================

        private void RunBorrowProvenanceAndImmutabilityAnalysis()
        {
            int entryId = _fn.Blocks[0].Id;
            var entryProv = new Dictionary<int, ProvenanceState>();

            var initial = new ProvenanceState(_localCount);
            for (int i = 0; i < _localCount; i++)
            {
                var local = _fn.Locals[i];
                if (local.IsParam && _isBorrowCarrier[i])
                {
                    bool isExclusive = local.PassKind is MirPassKind.Ref or MirPassKind.Out or MirPassKind.ImplicitThisMut ||
                        _module.IsExclusiveView(local.Type);
                    initial.SetLoans(i, new[] { Loan.ForPlace(PlacePath.FromPlace(MirPlace.Local(i)), isExclusive) });
                }
            }

            entryProv[entryId] = initial;
            var worklist = new Queue<int>();
            var inQueue = new HashSet<int> { entryId };
            worklist.Enqueue(entryId);

            while (worklist.Count > 0)
            {
                int bid = worklist.Dequeue();
                inQueue.Remove(bid);
                if (!_reachableBlocks.Contains(bid)) continue;

                var block = _blocksById[bid];
                var prov = entryProv[bid].Clone();
                int panicIdx = BlockPanicsBeforeTerminator(block);
                int stmtLimit = panicIdx >= 0 ? panicIdx + 1 : block.Statements.Count;

                for (int i = 0; i < stmtLimit; i++)
                {
                    var liveAfter = _liveAfterStmt.TryGetValue((bid, i), out var la) ? la : EmptyIntSet;
                    ApplyBorrowStatement(block.Statements[i], prov, liveAfter, emitDiagnostics: false);
                }

                if (panicIdx >= 0) continue;

                var liveBeforeTerm = _liveBeforeTerm.TryGetValue(bid, out var lt) ? lt : EmptyIntSet;
                foreach (var (succId, edgeProv) in ApplyBorrowTerminator(block.Terminator, prov, liveBeforeTerm, emitDiagnostics: false))
                {
                    if (!_reachableBlocks.Contains(succId)) continue;
                    if (!entryProv.TryGetValue(succId, out var existing))
                    {
                        entryProv[succId] = edgeProv;
                        if (inQueue.Add(succId)) worklist.Enqueue(succId);
                    }
                    else if (existing.MergeFrom(edgeProv))
                    {
                        if (inQueue.Add(succId)) worklist.Enqueue(succId);
                    }
                }
            }

            // Emit diagnostics once on converged provenance states
            foreach (int bid in _fn.Blocks.Select(b => b.Id))
            {
                if (!_reachableBlocks.Contains(bid) || !entryProv.TryGetValue(bid, out var startProv))
                {
                    continue;
                }

                var block = _blocksById[bid];
                var prov = startProv.Clone();
                int panicIdx = BlockPanicsBeforeTerminator(block);
                int stmtLimit = panicIdx >= 0 ? panicIdx + 1 : block.Statements.Count;

                for (int i = 0; i < stmtLimit; i++)
                {
                    var liveAfter = _liveAfterStmt.TryGetValue((bid, i), out var la) ? la : EmptyIntSet;
                    ApplyBorrowStatement(block.Statements[i], prov, liveAfter, emitDiagnostics: true);
                }

                if (panicIdx < 0)
                {
                    var liveBeforeTerm = _liveBeforeTerm.TryGetValue(bid, out var lt) ? lt : EmptyIntSet;
                    _ = ApplyBorrowTerminator(block.Terminator, prov, liveBeforeTerm, emitDiagnostics: true).ToList();
                }
            }
        }

        private static readonly HashSet<int> EmptyIntSet = new();

        private void ApplyBorrowStatement(
            MirStatement stmt,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            bool emitDiagnostics)
        {
            switch (stmt)
            {
                case MirStatement.Assign a:
                {
                    CheckIndexOperandsInPlace(a.Target, prov, liveAfter, emitDiagnostics);
                    var liveDuringRhs = liveAfter;
                    if (a.Target.Projections.Count == 0 && a.Rvalue is MirRvalue.Use { IsCallOperandReborrow: true })
                    {
                        liveDuringRhs = CapturePreparationLive(a.Target.LocalId, liveAfter);
                    }
                    if (a.Target.Projections.Count == 0)
                    {
                        // The assignment overwrites the whole destination: unless the rvalue reads it, the old value
                        // and its loans are dead while the rvalue is evaluated (it is live after the statement only
                        // through its new value, whose loans the rvalue produces).
                        liveDuringRhs = WithoutOverwrittenDestination(liveDuringRhs, a.Target.LocalId, ReadsOfRvalue(a.Rvalue));
                    }
                    if (a.Target.Projections.Count > 0)
                    {
                        liveDuringRhs = new HashSet<int>(liveAfter);
                        AddPlaceTargetCarrierUses(a.Target, liveDuringRhs);
                    }
                    var rhsLoans = EvaluateBorrowRvalue(a.Rvalue, a.Target, prov, liveDuringRhs, a.Span, emitDiagnostics);

                    if (emitDiagnostics &&
                        (IsMutatingThroughSharedBorrow(a.Target, isIndirectOrMethodReceiver: false) ||
                         IsDirectBorrowedNonCopyPatternBinding(a.Target)))
                    {
                        Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, "Cannot mutate through a shared borrow.", a.Span);
                    }

                    int excludedCarrier = a.Target.Projections.Count == 0 ? a.Target.LocalId : -1;
                    CheckWriteConflictsWithActiveLoans(a.Target, prov, liveAfter, excludedCarrier, a.Span, emitDiagnostics);
                    if (a.Target.Projections.Count > 0 && a.Target.Projections[0] is MirProjection.Deref)
                    {
                        prov.ActivateLoans(a.Target.LocalId);
                    }

                    if (a.Target.Projections.Count == 0 && a.Target.LocalId >= 0 && a.Target.LocalId < _localCount)
                    {
                        int destId = a.Target.LocalId;
                        if (_isBorrowCarrier[destId])
                        {
                            // Strip self step-loan and self place-loan when advancing a cursor `cur = ...`
                            rhsLoans.RemoveWhere(l =>
                                (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == destId) ||
                                (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == destId));
                            CheckDanglingRvalueTempLoans(destId, rhsLoans, a.Span, emitDiagnostics);
                            prov.SetLoans(destId, rhsLoans);
                            if (a.Rvalue is MirRvalue.Use { IsCallOperandReborrow: false, Operand: MirOperand.CopyOrMove { Place.Projections.Count: 0 } moved } &&
                                moved.Place.LocalId != destId &&
                                moved.Place.LocalId >= 0 && moved.Place.LocalId < _localCount &&
                                _isBorrowCarrier[moved.Place.LocalId] &&
                                !_module.IsCopy(moved.Type))
                            {
                                // A whole carrier moved into another one hands its loans over (`t = s;`, and the
                                // temporary holding the value of `(s = b.AsSpan())`). The moved-from local holds none
                                // until it is assigned again, as any later use of it is VIB001.
                                prov.ClearLoans(moved.Place.LocalId);
                            }
                        }
                        else
                        {
                            prov.ClearLoans(destId);
                        }
                    }
                    else if (a.Target.Projections.Count > 0)
                    {
                        PropagateProjectedWriteLoans(a.Target, rhsLoans, prov);
                    }
                    break;
                }

                case MirStatement.RememberRefRepointLoan remember:
                    prov.SetLoans(remember.SnapshotLocalId, prov.GetLoans(remember.SourceRefLocalId));
                    break;

                case MirStatement.BindRef br:
                {
                    if (IsSwitchMatchBorrow(br.RefLocalId))
                    {
                        // This lease represents the existing pattern read, not an extra operand read.
                        // The real pattern/guard still checks its usual read conflicts.
                        var matchLoans = new HashSet<Loan>(ExpandEffectivePaths(br.Target, prov)
                            .Select(path => Loan.ForPlace(path, isExclusive: false)));
                        if (br.SwitchMatchRefSlots is { } slots)
                        {
                            foreach (int slot in slots) matchLoans.Add(Loan.ForSwitchMatchRefSlot(slot));
                        }
                        prov.SetLoans(br.RefLocalId, matchLoans);
                        break;
                    }
                    CheckSwitchMatchRefRebind(br.RefLocalId, prov, liveAfter, br.Span, emitDiagnostics);
                    liveAfter = CapturePreparationLive(br.RefLocalId, liveAfter);
                    var targetReads = new HashSet<int>();
                    AddPlaceReadCarrierUses(br.Target, targetReads);
                    var liveDuringTarget = LiveDuringRefOverwrite(br, liveAfter, br.RefLocalId, targetReads);
                    CheckIndexOperandsInPlace(br.Target, prov, liveDuringTarget, emitDiagnostics);
                    bool isExplicitUserBindRef = br.RefLocalId >= 0 && br.RefLocalId < _localCount && !_fn.Locals[br.RefLocalId].IsCompilerTemp;
                    if (emitDiagnostics && br.IsMutable &&
                        (IsMutatingThroughSharedBorrow(br.Target, isIndirectOrMethodReceiver: true) ||
                         (isExplicitUserBindRef && IsDirectBorrowedNonCopyPatternBinding(br.Target))))
                    {
                        Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, "Cannot take a mutable reference through a shared borrow.", br.Span);
                    }

                    bool reserveTwoPhase = Reservation(br) is not null;
                    CheckBorrowCreationConflicts(
                        br.Target,
                        isExclusive: br.IsMutable && !reserveTwoPhase,
                        destCarrierId: -1,
                        prov,
                        liveDuringTarget,
                        br.Span,
                        emitDiagnostics);

                    var loans = ComputeLoansForPlaceBorrow(br.Target, br.IsMutable, prov, reserveTwoPhase, br.IsAssignmentTargetSnapshot);
                    loans.RemoveWhere(l =>
                        (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == br.RefLocalId) ||
                        (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == br.RefLocalId));
                    CheckDanglingRvalueTempLoans(br.RefLocalId, loans, br.Span, emitDiagnostics);
                    prov.SetLoans(br.RefLocalId, loans);
                    break;
                }

                case MirStatement.Call c:
                {
                    if (c.Dest is not null) CheckIndexOperandsInPlace(c.Dest, prov, liveAfter, emitDiagnostics);

                    // The same overwrite rule applies to a value or reference destination: the old
                    // value's loans are dead during arguments unless an argument actually reads it.
                    var liveDuringArgs = liveAfter;
                    int? overwrittenDestination = c.RefDestLocalId ??
                        (c.Dest is { Projections.Count: 0 } wholeDest ? wholeDest.LocalId : null);
                    if (overwrittenDestination is int destination)
                    {
                        var argReads = new HashSet<int>();
                        foreach (var arg in c.Args)
                        {
                            if (arg.ValueOperand is not null) AddOperandCarrierUses(arg.ValueOperand, argReads);
                            if (arg.BorrowPlace is not null) AddPlaceReadCarrierUses(arg.BorrowPlace, argReads);
                        }
                        liveDuringArgs = c.RefDestLocalId.HasValue
                            ? LiveDuringRefOverwrite(c, liveAfter, destination, argReads)
                            : WithoutOverwrittenDestination(liveAfter, destination, argReads);
                    }

                    // Collect intra-call carriers used by arguments so moves inside call args conflict with sibling borrow args
                    var intraCallLive = new HashSet<int>(liveDuringArgs);
                    foreach (var arg in c.Args)
                    {
                        if (arg.ValueOperand is not null) AddOperandCarrierUses(arg.ValueOperand, intraCallLive);
                        if (arg.BorrowPlace is not null) AddPlaceReadCarrierUses(arg.BorrowPlace, intraCallLive);
                    }

                    // VIB002: Check overlapping borrows within the same call
                    bool hadOverlappingCallBorrows = CheckOverlappingCallBorrows(c.Args, prov, emitDiagnostics);

                    for (int i = 0; i < c.Args.Count; i++)
                    {
                        var arg = c.Args[i];
                        var remainingLive = new HashSet<int>(liveDuringArgs);
                        if (!hadOverlappingCallBorrows)
                        {
                            for (int j = i + 1; j < c.Args.Count; j++)
                            {
                                if (c.Args[j].ValueOperand is not null) AddOperandCarrierUses(c.Args[j].ValueOperand!, remainingLive);
                                if (c.Args[j].BorrowPlace is not null && c.Args[j].PassKind != MirPassKind.Out)
                                {
                                    AddPlaceReadCarrierUses(c.Args[j].BorrowPlace!, remainingLive);
                                }
                            }
                        }

                        if (arg.ValueOperand is not null)
                        {
                            bool reborrow = arg.ValueOperand is MirOperand.CopyOrMove { Type: MirType.SpanType or MirType.Utf8ScanViewType };
                            EvaluateBorrowOperand(arg.ValueOperand, forceCopy: reborrow, prov,
                                reborrow ? remainingLive : intraCallLive, emitDiagnostics);
                            if (arg.ValueOperand is MirOperand.CopyOrMove { Type: MirType.SpanType { IsReadOnly: false } } spanArgument)
                            {
                                // Span arguments implicitly reborrow; a force-copy
                                // read alone misses their exclusive call-time use.
                                CheckBorrowCreationConflicts(spanArgument.Place, isExclusive: true, -1,
                                    prov, remainingLive, arg.Span, emitDiagnostics);
                            }
                        }

                        if (arg.BorrowPlace is not null)
                        {
                            CheckIndexOperandsInPlace(arg.BorrowPlace, prov, remainingLive, emitDiagnostics);
                            bool isExclusive = arg.PassKind is MirPassKind.Ref or MirPassKind.Out or MirPassKind.ImplicitThisMut;

                            if (emitDiagnostics && isExclusive &&
                                (IsMutatingThroughSharedBorrow(arg.BorrowPlace, isIndirectOrMethodReceiver: true) ||
                                 (arg.PassKind is MirPassKind.Ref or MirPassKind.Out && IsDirectBorrowedNonCopyPatternBinding(arg.BorrowPlace))))
                            {
                                Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, "Cannot pass shared borrow as mutable reference or receiver.", arg.Span);
                            }

                            // A ref destination is already handled by liveness. If the RHS needs the
                            // old reference, do not hide its genuine conflicts by exempting the slot.
                            int destCarrier = c.RefDestLocalId is null && c.Dest is { Projections.Count: 0 } d ? d.LocalId : -1;
                            CheckBorrowCreationConflicts(arg.BorrowPlace, isExclusive, destCarrier, prov, remainingLive, arg.Span, emitDiagnostics);
                            if (isExclusive && arg.BorrowPlace.Projections.Count > 0 && arg.BorrowPlace.Projections[0] is MirProjection.Deref)
                            {
                                prov.ActivateLoans(arg.BorrowPlace.LocalId);
                            }
                        }
                    }

                    // Compute return provenance for ref-return or view-return functions
                    var returnLoans = ComputeCallReturnLoans(c, prov);

                    if (c.Dest is not null)
                    {
                        if (emitDiagnostics && IsMutatingThroughSharedBorrow(c.Dest, isIndirectOrMethodReceiver: false))
                        {
                            Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, "Cannot mutate through a shared borrow.", c.Span);
                        }
                        int excluded = c.Dest.Projections.Count == 0 ? c.Dest.LocalId : -1;
                        CheckWriteConflictsWithActiveLoans(c.Dest, prov, liveAfter, excluded, c.Span, emitDiagnostics);
                        if (c.Dest.Projections.Count > 0 && c.Dest.Projections[0] is MirProjection.Deref)
                        {
                            prov.ActivateLoans(c.Dest.LocalId);
                        }

                        if (c.Dest.Projections.Count == 0 && c.Dest.LocalId >= 0 && c.Dest.LocalId < _localCount)
                        {
                            if (_isBorrowCarrier[c.Dest.LocalId])
                            {
                                returnLoans.RemoveWhere(l =>
                                    (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == c.Dest.LocalId) ||
                                    (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == c.Dest.LocalId));
                                CheckDanglingRvalueTempLoans(c.Dest.LocalId, returnLoans, c.Span, emitDiagnostics);
                                prov.SetLoans(c.Dest.LocalId, returnLoans);
                            }
                            else
                            {
                                prov.ClearLoans(c.Dest.LocalId);
                            }
                        }
                        else if (c.Dest.Projections.Count > 0)
                        {
                            PropagateProjectedWriteLoans(c.Dest, returnLoans, prov);
                        }
                    }

                    if (c.RefDestLocalId is int callRefId && callRefId >= 0 && callRefId < _localCount)
                    {
                        CheckSwitchMatchRefRebind(callRefId, prov, liveAfter, c.Span, emitDiagnostics);
                        returnLoans.RemoveWhere(l =>
                            (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == callRefId) ||
                            (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == callRefId));
                        CheckDanglingRvalueTempLoans(callRefId, returnLoans, c.Span, emitDiagnostics);
                        prov.SetLoans(callRefId, returnLoans);
                    }
                    break;
                }

                case MirStatement.Intrinsic intr:
                    ApplyBorrowIntrinsic(intr, prov, liveAfter, emitDiagnostics);
                    break;

                case MirStatement.DisposeResource dr:
                    if (dr.LocalId >= 0 && dr.LocalId < _localCount)
                    {
                        CheckWriteConflictsWithActiveLoans(MirPlace.Local(dr.LocalId), prov, liveAfter, excludedCarrierId: -1, dr.Span, emitDiagnostics);
                    }
                    break;

                case MirStatement.ScopeExit se:
                    foreach (int exitingId in se.ExitingLocals)
                    {
                        if (exitingId < 0 || exitingId >= _localCount) continue;
                        CheckScopeExitConflicts(exitingId, prov, liveAfter, se.Span, emitDiagnostics);
                        if (_isBorrowCarrier[exitingId])
                        {
                            prov.StripPlaceLoansForLocal(exitingId);
                            prov.ClearLoans(exitingId);
                        }
                    }
                    break;
            }
        }

        private void ApplyBorrowIntrinsic(
            MirStatement.Intrinsic intr,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            bool emitDiagnostics)
        {
            int preparationDestination = intr.RefDestLocalId ?? (intr.Dest is { Projections.Count: 0 } viewDestination ? viewDestination.LocalId : -1);
            if (IsPureBorrowPreparation(intr.Kind))
            {
                liveAfter = CapturePreparationLive(preparationDestination, liveAfter);
            }
            if (intr.RefDestLocalId is int overwrittenRef)
            {
                var argReads = new HashSet<int>();
                foreach (var value in intr.ValueArgs) AddOperandCarrierUses(value, argReads);
                foreach (var place in intr.PlaceArgs) AddPlaceReadCarrierUses(place, argReads);
                liveAfter = LiveDuringRefOverwrite(intr, liveAfter, overwrittenRef, argReads);
            }
            if (intr.Dest is not null) CheckIndexOperandsInPlace(intr.Dest, prov, liveAfter, emitDiagnostics);

            bool nonConsumingValueInspection = IsNonConsumingValueIntrinsic(intr.Kind);

            // Evaluate ValueArgs first (for OptionReplace / OptionMutSet / VecPush / etc.)
            var liveDuringArgs = new HashSet<int>(liveAfter);
            foreach (var p in intr.PlaceArgs) AddPlaceReadCarrierUses(p, liveDuringArgs);

            HashSet<Loan>? arg0Loans = null;
            for (int i = 0; i < intr.ValueArgs.Count; i++)
            {
                var rem = new HashSet<int>(liveDuringArgs);
                for (int j = i + 1; j < intr.ValueArgs.Count; j++)
                {
                    AddOperandCarrierUses(intr.ValueArgs[j], rem);
                }
                var evaluated = EvaluateBorrowOperand(intr.ValueArgs[i], forceCopy: nonConsumingValueInspection, prov, rem, emitDiagnostics);
                if (i == 0)
                {
                    arg0Loans = evaluated;
                }
            }

            foreach (var p in intr.PlaceArgs)
            {
                CheckIndexOperandsInPlace(p, prov, liveAfter, emitDiagnostics);
            }

            int destCarrier = intr.RefDestLocalId is null && intr.Dest is { Projections.Count: 0 } d ? d.LocalId : -1;
            var producedLoans = new HashSet<Loan>();

            if (intr.PlaceArgs.Count > 0)
            {
                var receiverPlace = intr.PlaceArgs[0];
                bool mutatesOrExclusivelyBorrows = IsMutatingIntrinsicOnFirstPlace(intr.Kind, intr.RefDestIsMutable);

                if (emitDiagnostics && mutatesOrExclusivelyBorrows && IsMutatingThroughSharedBorrow(receiverPlace, isIndirectOrMethodReceiver: true))
                {
                    Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot call mutating operation '{intr.Kind}' through a shared borrow.", intr.Span);
                }

                switch (intr.Kind)
                {
                    case MirIntrinsicKind.OptionMutSet:
                    case MirIntrinsicKind.OptionMutTake:
                    case MirIntrinsicKind.OptionMutReplace:
                        // Mutates through cursor `cur`: verify no variable live after holds StepCursor(cur) or PlaceBorrow(cur.*)
                        CheckCursorMutationConflicts(receiverPlace, destCarrier, prov, liveAfter, intr.Span, emitDiagnostics);
                        break;

                    case MirIntrinsicKind.OptionTake:
                    case MirIntrinsicKind.OptionReplace:
                    case MirIntrinsicKind.VecPush:
                    case MirIntrinsicKind.VecPop:
                    case MirIntrinsicKind.VecClear:
                    case MirIntrinsicKind.VecTruncate:
                    case MirIntrinsicKind.VecReserve:
                    case MirIntrinsicKind.VecExtend:
                    case MirIntrinsicKind.VecResize:
                    {
                        var liveDuringMutIntrinsic = new HashSet<int>(liveAfter);
                        foreach (var vArg in intr.ValueArgs)
                        {
                            AddOperandCarrierUses(vArg, liveDuringMutIntrinsic);
                        }
                        CheckWriteConflictsWithActiveLoans(receiverPlace, prov, liveDuringMutIntrinsic, excludedCarrierId: destCarrier, intr.Span, emitDiagnostics);
                        if (receiverPlace.Projections.Count > 0 && receiverPlace.Projections[0] is MirProjection.Deref)
                        {
                            prov.ActivateLoans(receiverPlace.LocalId);
                        }
                        break;
                    }

                    case MirIntrinsicKind.OptionAsRef:
                    case MirIntrinsicKind.OptionMutAsRef:
                    case MirIntrinsicKind.VecAsReadOnlySpan:
                    case MirIntrinsicKind.SpanToReadOnly:
                        CheckBorrowCreationConflicts(receiverPlace, isExclusive: false, destCarrier, prov, liveAfter, intr.Span, emitDiagnostics);
                        producedLoans.UnionWith(ComputeLoansForPlaceBorrow(receiverPlace, isExclusive: false, prov));
                        if (intr.Hold is int intrHold) producedLoans.UnionWith(prov.GetLoans(intrHold));
                        if (intr.ReleasesMovedPlace is int intrHeld) producedLoans.UnionWith(ComputeLoansForPlaceBorrow(MirPlace.Local(intrHeld), isExclusive: false, prov));
                        break;

                    case MirIntrinsicKind.OptionAsMut:
                    case MirIntrinsicKind.OptionUnwrapMut:
                    case MirIntrinsicKind.VecSliceMut:
                    case MirIntrinsicKind.VecAsSpan:
                        CheckBorrowCreationConflicts(receiverPlace, isExclusive: true, destCarrier, prov, liveAfter, intr.Span, emitDiagnostics);
                        if (receiverPlace.Projections.Count > 0 && receiverPlace.Projections[0] is MirProjection.Deref)
                        {
                            prov.ActivateLoans(receiverPlace.LocalId);
                        }
                        producedLoans.UnionWith(ComputeLoansForPlaceBorrow(receiverPlace, isExclusive: true, prov));
                        break;

                    case MirIntrinsicKind.VecIndexRef:
                    case MirIntrinsicKind.SpanIndexRef:
                    {
                        bool reserveTwoPhase = Reservation(intr) is not null;
                        CheckBorrowCreationConflicts(receiverPlace, isExclusive: intr.RefDestIsMutable && !reserveTwoPhase, destCarrier, prov, liveAfter, intr.Span, emitDiagnostics);
                        producedLoans.UnionWith(ComputeLoansForPlaceBorrow(ElementPlace(intr), isExclusive: intr.RefDestIsMutable, prov, reserveTwoPhase));
                        break;
                    }

                    case MirIntrinsicKind.SpanSlice:
                    {
                        bool spanExcl = intr.ResultType is MirType.SpanType { IsReadOnly: false };
                        CheckBorrowCreationConflicts(receiverPlace, isExclusive: spanExcl, destCarrier, prov, liveAfter, intr.Span, emitDiagnostics);
                        producedLoans.UnionWith(ComputeLoansForPlaceBorrow(receiverPlace, isExclusive: spanExcl, prov));
                        break;
                    }

                    default:
                        // OptionIsSome, ResultIsOk, VecCount, VecCapacity, SpanLength
                        CheckReadConflictsWithActiveLoans(receiverPlace, prov, liveAfter, intr.Span, emitDiagnostics);
                        break;
                }
            }
            else if (intr.Kind == MirIntrinsicKind.SpanToReadOnly && intr.ValueArgs.Count > 0 && intr.ValueArgs[0] is MirOperand.CopyOrMove spanCm)
            {
                producedLoans.UnionWith(ComputeLoansForPlaceBorrow(spanCm.Place, isExclusive: false, prov));
            }
            else if (intr.Kind is MirIntrinsicKind.SpanSlice or MirIntrinsicKind.SpanToUtf8View or MirIntrinsicKind.Utf8ViewToSpan && arg0Loans is not null)
            {
                producedLoans.UnionWith(arg0Loans);
            }

            if (intr.Dest is not null)
            {
                if (emitDiagnostics && IsMutatingThroughSharedBorrow(intr.Dest, isIndirectOrMethodReceiver: false))
                {
                    Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, "Cannot mutate through a shared borrow.", intr.Span);
                }
                int excluded = intr.Dest.Projections.Count == 0 ? intr.Dest.LocalId : -1;
                CheckWriteConflictsWithActiveLoans(intr.Dest, prov, liveAfter, excluded, intr.Span, emitDiagnostics);
                if (intr.Dest.Projections.Count > 0 && intr.Dest.Projections[0] is MirProjection.Deref)
                {
                    prov.ActivateLoans(intr.Dest.LocalId);
                }

                if (intr.Dest.Projections.Count == 0 && intr.Dest.LocalId >= 0 && intr.Dest.LocalId < _localCount)
                {
                    int dId = intr.Dest.LocalId;
                    if (_isBorrowCarrier[dId])
                    {
                        producedLoans.RemoveWhere(l =>
                            (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == dId) ||
                            (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == dId));
                        CheckDanglingRvalueTempLoans(dId, producedLoans, intr.Span, emitDiagnostics);
                        prov.SetLoans(dId, producedLoans);
                    }
                    else
                    {
                        prov.ClearLoans(dId);
                    }
                }
                else if (intr.Dest.Projections.Count > 0)
                {
                    PropagateProjectedWriteLoans(intr.Dest, producedLoans, prov);
                }
            }

            if (intr.RefDestLocalId is int refId && refId >= 0 && refId < _localCount)
            {
                CheckSwitchMatchRefRebind(refId, prov, liveAfter, intr.Span, emitDiagnostics);
                producedLoans.RemoveWhere(l =>
                    (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == refId) ||
                    (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == refId));
                CheckDanglingRvalueTempLoans(refId, producedLoans, intr.Span, emitDiagnostics);
                prov.SetLoans(refId, producedLoans);
            }
        }

        private IEnumerable<(int SuccId, ProvenanceState Prov)> ApplyBorrowTerminator(
            MirTerminator term,
            ProvenanceState prov,
            HashSet<int> liveBeforeTerm,
            bool emitDiagnostics)
        {
            // Compute liveAfter on outgoing edges from this block
            var liveInSuccessors = new HashSet<int>();
            foreach (int succ in GetSuccessors(term))
            {
                if (_liveEntry.TryGetValue(succ, out var sLive))
                {
                    foreach (int v in sLive)
                    {
                        if (!IsKilledOnTerminatorEdge(term, v))
                        {
                            liveInSuccessors.Add(v);
                        }
                    }
                }
            }

            liveInSuccessors.UnionWith(_foreachLoanLocals);

            switch (term)
            {
                case MirTerminator.Goto g:
                    yield return (g.TargetBlock, prov.Clone());
                    break;

                case MirTerminator.Branch b:
                    EvaluateBorrowOperand(b.Condition, forceCopy: false, prov, liveInSuccessors, emitDiagnostics);
                    yield return (b.TrueBlock, prov.Clone());
                    yield return (b.FalseBlock, prov.Clone());
                    break;

                case MirTerminator.MatchOption mo:
                {
                    var someProv = prov.Clone();
                    var noneProv = prov.Clone();

                    if (mo.SomeBindingLocalId >= 0 && mo.SomeBindingLocalId < _localCount)
                    {
                        someProv.StripPlaceLoansForLocal(mo.SomeBindingLocalId);
                        noneProv.StripPlaceLoansForLocal(mo.SomeBindingLocalId);
                        noneProv.ClearLoans(mo.SomeBindingLocalId);
                    }

                    ApplyMatchScrutineeBorrow(
                        mo.Scrutinee,
                        new[] { mo.SomeBindingLocalId },
                        prov,
                        someProv,
                        liveInSuccessors,
                        mo.Span,
                        emitDiagnostics);

                    yield return (mo.SomeBlock, someProv);
                    yield return (mo.NoneBlock, noneProv);
                    break;
                }

                case MirTerminator.MatchResult mr:
                {
                    var okProv = prov.Clone();
                    var errProv = prov.Clone();

                    if (mr.OkBindingLocalId >= 0 && mr.OkBindingLocalId < _localCount)
                    {
                        okProv.StripPlaceLoansForLocal(mr.OkBindingLocalId);
                        errProv.StripPlaceLoansForLocal(mr.OkBindingLocalId);
                        errProv.ClearLoans(mr.OkBindingLocalId);
                    }
                    if (mr.ErrBindingLocalId >= 0 && mr.ErrBindingLocalId < _localCount)
                    {
                        okProv.StripPlaceLoansForLocal(mr.ErrBindingLocalId);
                        errProv.StripPlaceLoansForLocal(mr.ErrBindingLocalId);
                        okProv.ClearLoans(mr.ErrBindingLocalId);
                    }

                    ApplyMatchScrutineeBorrow(
                        mr.Scrutinee,
                        new[] { mr.OkBindingLocalId },
                        prov,
                        okProv,
                        liveInSuccessors,
                        mr.Span,
                        emitDiagnostics);

                    // Also populate ErrBindingLocalId loans if place/view scrutinee without double-emitting conflict diagnostics
                    ApplyMatchScrutineeBorrow(
                        mr.Scrutinee,
                        new[] { mr.ErrBindingLocalId },
                        prov,
                        errProv,
                        liveInSuccessors,
                        mr.Span,
                        emitDiagnostics: false);

                    yield return (mr.OkBlock, okProv);
                    yield return (mr.ErrBlock, errProv);
                    break;
                }

                case MirTerminator.GuardResultIsErr gr:
                {
                    var inherited = EvaluateBorrowOperand(gr.ConsumedResult, forceCopy: false, prov, liveInSuccessors, emitDiagnostics);
                    var errProv = prov.Clone();
                    var okProv = prov.Clone();

                    if (gr.OkLocalId >= 0 && gr.OkLocalId < _localCount && _isBorrowCarrier[gr.OkLocalId])
                    {
                        okProv.SetLoans(gr.OkLocalId, inherited);
                    }
                    if (gr.ErrWrapperLocalId >= 0 && gr.ErrWrapperLocalId < _localCount && _isBorrowCarrier[gr.ErrWrapperLocalId])
                    {
                        errProv.SetLoans(gr.ErrWrapperLocalId, inherited);
                    }

                    yield return (gr.ErrBlock, errProv);
                    yield return (gr.OkBlock, okProv);
                    break;
                }

                case MirTerminator.GuardOptionIsNone go:
                {
                    var inherited = EvaluateBorrowOperand(go.ConsumedOption, forceCopy: false, prov, liveInSuccessors, emitDiagnostics);
                    var noneProv = prov.Clone();
                    var someProv = prov.Clone();

                    if (go.SomeValueLocalId >= 0 && go.SomeValueLocalId < _localCount && _isBorrowCarrier[go.SomeValueLocalId])
                    {
                        someProv.SetLoans(go.SomeValueLocalId, inherited);
                    }

                    yield return (go.NoneBlock, noneProv);
                    yield return (go.SomeBlock, someProv);
                    break;
                }

                case MirTerminator.Return ret:
                {
                    var returnedLoans = new HashSet<Loan>();
                    if (ret.Value is not null)
                    {
                        returnedLoans.UnionWith(EvaluateBorrowOperand(ret.Value, forceCopy: false, prov, EmptyIntSet, emitDiagnostics));
                    }
                    if (ret.RefReturnPlace is not null)
                    {
                        CheckIndexOperandsInPlace(ret.RefReturnPlace, prov, EmptyIntSet, emitDiagnostics);
                        if (emitDiagnostics && _fn.Signature.ReturnRefIsMutable && IsMutatingThroughSharedBorrow(ret.RefReturnPlace, isIndirectOrMethodReceiver: true))
                        {
                            Emit(VibraniumDiagnosticIds.MutateThroughSharedBorrow, "Cannot return mutable reference through a shared borrow.", ret.Span);
                        }
                        CheckBorrowCreationConflicts(ret.RefReturnPlace, _fn.Signature.ReturnRefIsMutable, -1, prov, EmptyIntSet, ret.Span, emitDiagnostics);
                        returnedLoans.UnionWith(ComputeLoansForPlaceBorrow(ret.RefReturnPlace, _fn.Signature.ReturnRefIsMutable, prov));
                    }

                    if (emitDiagnostics)
                    {
                        CheckParameterScopeExitAtReturn(ret, returnedLoans);
                        CheckReturnEscape(ret, returnedLoans);
                    }
                    break;
                }
            }
        }

        private void ApplyMatchScrutineeBorrow(
            MirMatchScrutinee scrutinee,
            int[] bindingIds,
            ProvenanceState currentProv,
            ProvenanceState targetProv,
            HashSet<int> liveInSuccessors,
            VibraniumSpan span,
            bool emitDiagnostics)
        {
            switch (scrutinee)
            {
                case MirMatchScrutinee.PlaceScrutinee ps:
                {
                    if (emitDiagnostics && _module.IsLinearResource(ps.Type))
                    {
                        Emit(
                            VibraniumDiagnosticIds.LinearResourceViolation,
                            "Cannot borrow-match a linear union in place; consume it by value.",
                            span);
                    }
                    CheckIndexOperandsInPlace(ps.Place, currentProv, liveInSuccessors, emitDiagnostics);
                    bool isExclusive = ps.IsMutableCursor;

                    int firstBinding = -1;
                    foreach (int id in bindingIds) { if (id >= 0 && id < _localCount) { firstBinding = id; break; } }
                    CheckBorrowCreationConflicts(ps.Place, isExclusive, firstBinding, currentProv, liveInSuccessors, span,
                        emitDiagnostics, ps.SelectingSwitchMatchBorrowId);

                    var bindingLoans = new HashSet<Loan>();
                    if (ps.Place.LocalId >= 0 && ps.Place.LocalId < _localCount && _isBorrowCarrier[ps.Place.LocalId])
                    {
                        bindingLoans.UnionWith(currentProv.GetLoans(ps.Place.LocalId));
                    }
                    else
                    {
                        bindingLoans.UnionWith(ComputeLoansForPlaceBorrow(ps.Place, isExclusive, currentProv));
                    }

                    if (ps.Place.Projections.Count == 0 && ps.Place.LocalId >= 0)
                    {
                        bindingLoans.Add(Loan.ForStep(ps.Place.LocalId));
                        if (!_isBorrowCarrier[ps.Place.LocalId])
                        {
                            bindingLoans.Add(Loan.ForPlace(PlacePath.FromPlace(ps.Place), isExclusive));
                        }
                    }
                    else
                    {
                        bindingLoans.UnionWith(ComputeLoansForPlaceBorrow(ps.Place, isExclusive, currentProv));
                    }

                    foreach (int id in bindingIds)
                    {
                        if (id >= 0 && id < _localCount && _isBorrowCarrier[id] && !MatchBindsIndependentCopy(scrutinee, id))
                        {
                            targetProv.SetLoans(id, bindingLoans);
                        }
                    }
                    break;
                }

                case MirMatchScrutinee.RvalueScrutinee rs:
                {
                    var inherited = EvaluateBorrowOperand(rs.Operand, forceCopy: false, currentProv, liveInSuccessors, emitDiagnostics);
                    foreach (int id in bindingIds)
                    {
                        if (id >= 0 && id < _localCount && _isBorrowCarrier[id] && !MatchBindsIndependentCopy(scrutinee, id))
                        {
                            targetProv.SetLoans(id, inherited);
                        }
                    }
                    break;
                }
            }
        }

        private HashSet<Loan> EvaluateBorrowRvalue(
            MirRvalue rv,
            MirPlace assignTarget,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            VibraniumSpan span,
            bool emitDiagnostics)
        {
            var loans = new HashSet<Loan>();
            switch (rv)
            {
                case MirRvalue.Use u:
                    loans.UnionWith(EvaluateBorrowOperand(u.Operand, forceCopy: u.IsCallOperandReborrow, prov, liveAfter, emitDiagnostics));
                    if (u.IsCallOperandReborrow && u.Operand is MirOperand.CopyOrMove view)
                    {
                        loans.UnionWith(ComputeLoansForPlaceBorrow(view.Place, _module.IsExclusiveView(view.Type), prov));
                    }
                    break;
                case MirRvalue.Binary b:
                {
                    var rem = new HashSet<int>(liveAfter);
                    AddOperandCarrierUses(b.Right, rem);
                    EvaluateBorrowOperand(b.Left, forceCopy: false, prov, rem, emitDiagnostics);
                    EvaluateBorrowOperand(b.Right, forceCopy: false, prov, liveAfter, emitDiagnostics);
                    break;
                }
                case MirRvalue.Unary u:
                    EvaluateBorrowOperand(u.Operand, forceCopy: false, prov, liveAfter, emitDiagnostics);
                    break;
                case MirRvalue.Cast c:
                    loans.UnionWith(EvaluateBorrowOperand(c.Operand, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.AllocClass ac:
                    EvaluateOperandList(ac.FieldValues, prov, liveAfter, emitDiagnostics);
                    break;
                case MirRvalue.ConstructStruct cs:
                    loans.UnionWith(EvaluateOperandList(cs.FieldValues, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.MakeSomeWrapper sw:
                    loans.UnionWith(EvaluateBorrowOperand(sw.Value, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.MakeOkWrapper ow:
                    loans.UnionWith(EvaluateBorrowOperand(ow.Value, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.MakeErrWrapper ew:
                    loans.UnionWith(EvaluateBorrowOperand(ew.Error, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.LiftOptionSome los:
                    loans.UnionWith(EvaluateBorrowOperand(los.Value, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.LiftOptionNone:
                    break;
                case MirRvalue.LiftResultOk lro:
                    loans.UnionWith(EvaluateBorrowOperand(lro.Value, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.LiftResultErr lre:
                    loans.UnionWith(EvaluateBorrowOperand(lre.Error, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.UnwrapSomeWrapper usw:
                    loans.UnionWith(EvaluateBorrowOperand(usw.Wrapper, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.UnwrapOkWrapper uow:
                    loans.UnionWith(EvaluateBorrowOperand(uow.Wrapper, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.UnwrapErrWrapper uew:
                    loans.UnionWith(EvaluateBorrowOperand(uew.Wrapper, forceCopy: false, prov, liveAfter, emitDiagnostics));
                    break;
                case MirRvalue.StackSpan ss:
                    EvaluateOperandList(ss.Elements, prov, liveAfter, emitDiagnostics);
                    int stackOwnerId = assignTarget.LocalId >= 0 ? assignTarget.LocalId : 0;
                    loans.Add(Loan.ForLocalStack(stackOwnerId));
                    break;
            }
            return loans;
        }

        private HashSet<Loan> EvaluateOperandList(
            IReadOnlyList<MirOperand> operands,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            bool emitDiagnostics)
        {
            var combined = new HashSet<Loan>();
            for (int i = 0; i < operands.Count; i++)
            {
                var rem = new HashSet<int>(liveAfter);
                for (int j = i + 1; j < operands.Count; j++)
                {
                    AddOperandCarrierUses(operands[j], rem);
                }
                combined.UnionWith(EvaluateBorrowOperand(operands[i], forceCopy: false, prov, rem, emitDiagnostics));
            }
            return combined;
        }

        private HashSet<Loan> EvaluateBorrowOperand(
            MirOperand op,
            bool forceCopy,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            bool emitDiagnostics)
        {
            var result = new HashSet<Loan>();
            if (op is not MirOperand.CopyOrMove cm)
            {
                return result;
            }

            CheckIndexOperandsInPlace(cm.Place, prov, liveAfter, emitDiagnostics);

            bool isMove = !forceCopy && !_module.IsCopy(cm.Type);
            if (isMove)
            {
                // Moving a place conflicts with ANY active loan (shared or exclusive) on an overlapping path or step-loan on it
                CheckMoveConflictsWithActiveLoans(cm.Place, prov, liveAfter, cm.Span, emitDiagnostics,
                    cm.SelectingSwitchMatchBorrowId);
            }
            else
            {
                // Copying/reading a place conflicts with active EXCLUSIVE loans on an overlapping path
                CheckReadConflictsWithActiveLoans(cm.Place, prov, liveAfter, cm.Span, emitDiagnostics);
            }

            if (cm.Place.LocalId >= 0 && cm.Place.LocalId < _localCount && _isBorrowCarrier[cm.Place.LocalId])
            {
                result.UnionWith(prov.GetLoans(cm.Place.LocalId));
            }

            return result;
        }

        private HashSet<int> ReadsOfRvalue(MirRvalue rv)
        {
            var reads = new HashSet<int>();
            AddRvalueCarrierUses(rv, reads);
            return reads;
        }

        private static HashSet<int> LiveDuringRefOverwrite(MirStatement producer, HashSet<int> live, int destination, HashSet<int> reads)
        {
            if (!producer.RefRepointNamesDestination)
            {
                return WithoutOverwrittenDestination(live, destination, reads);
            }
            return live.Contains(destination) ? live : new HashSet<int>(live) { destination };
        }

        // A whole-local overwrite (value or ref slot) kills the old value. Its loans do not constrain
        // evaluation of the RHS unless the RHS uses that old value (`s = s[1..]`, `r = ref Pick(r, s)`).
        private static HashSet<int> WithoutOverwrittenDestination(HashSet<int> live, int destination, HashSet<int> valueReads)
        {
            if (!live.Contains(destination) || valueReads.Contains(destination))
            {
                return live;
            }
            var result = new HashSet<int>(live);
            result.Remove(destination);
            return result;
        }

        private void CheckIndexOperandsInPlace(
            MirPlace place,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            bool emitDiagnostics)
        {
            foreach (var proj in place.Projections)
            {
                if (proj is MirProjection.Index idx)
                {
                    EvaluateBorrowOperand(idx.IndexOperand, forceCopy: false, prov, liveAfter, emitDiagnostics);
                }
            }
        }

        private HashSet<Loan> ComputeLoansForPlaceBorrow(MirPlace place, bool isExclusive, ProvenanceState prov, bool isTwoPhaseReserved = false, bool isAssignmentTargetSnapshot = false)
        {
            var loans = new HashSet<Loan>();
            if (place.LocalId >= 0 && place.LocalId < _localCount && _isBorrowCarrier[place.LocalId])
            {
                foreach (var inherited in prov.GetLoans(place.LocalId))
                {
                    if (inherited.Kind == LoanKind.PlaceBorrow && inherited.Path is not null)
                    {
                        var path = inherited.Path;
                        if (isAssignmentTargetSnapshot && place.Projections.Count > 0 &&
                            place.Projections[0] is MirProjection.Deref)
                        {
                            var segments = new List<PathSegment>(path.Segments);
                            for (int i = 1; i < place.Projections.Count; i++)
                            {
                                segments.Add(place.Projections[i] is MirProjection.Field field
                                    ? PathSegment.Field(field.FieldName)
                                    : PathSegment.Wildcard);
                            }
                            path = new PlacePath(path.RootLocalId, segments);
                        }
                        if (!isExclusive)
                        {
                            loans.Add(Loan.ForPlace(path, isExclusive: false));
                        }
                        else if (isTwoPhaseReserved && inherited.IsExclusive)
                        {
                            loans.Add(Loan.ForPlace(path, isExclusive: true, isTwoPhaseReserved: true));
                        }
                        else
                        {
                            loans.Add(Loan.ForPlace(path, inherited.IsExclusive));
                        }
                    }
                    else
                    {
                        loans.Add(inherited);
                    }
                }
            }
            if (isAssignmentTargetSnapshot && place.Projections.Count > 0 &&
                place.Projections[0] is MirProjection.Deref && loans.Count > 0)
            {
                // Keep every inherited owner/stack/cursor loan. Only the new, synthetic carrier
                // dependency is ancestry rather than a borrow of rebindable reference storage.
                loans.Add(Loan.ForSnapshotOrigin(place.LocalId));
            }
            else
            {
                loans.Add(Loan.ForPlace(PlacePath.FromPlace(place), isExclusive, isTwoPhaseReserved));
            }
            return loans;
        }

        private void PropagateProjectedWriteLoans(MirPlace destPlace, HashSet<Loan> rhsLoans, ProvenanceState prov)
        {
            if (rhsLoans.Count == 0 || destPlace.LocalId < 0 || destPlace.LocalId >= _localCount)
            {
                return;
            }

            var targetCarriers = new HashSet<int>();
            if (_isBorrowCarrier[destPlace.LocalId])
            {
                targetCarriers.Add(destPlace.LocalId);
            }

            if (destPlace.Projections.Count > 0 && destPlace.Projections[0] is MirProjection.Deref)
            {
                foreach (var aliasLoan in prov.GetLoans(destPlace.LocalId))
                {
                    if (aliasLoan.Kind == LoanKind.PlaceBorrow)
                    {
                        int aliasedId = aliasLoan.CursorOrStackLocalId;
                        if (aliasedId >= 0 && aliasedId < _localCount && _isBorrowCarrier[aliasedId])
                        {
                            targetCarriers.Add(aliasedId);
                        }
                    }
                }
            }

            foreach (int carrierId in targetCarriers)
            {
                var filtered = new HashSet<Loan>(rhsLoans);
                filtered.RemoveWhere(l =>
                    (l.Kind == LoanKind.StepCursor && l.CursorOrStackLocalId == carrierId) ||
                    (l.Kind == LoanKind.PlaceBorrow && l.CursorOrStackLocalId == carrierId));
                prov.AddLoans(carrierId, filtered);
            }
        }

        private HashSet<Loan> ComputeCallReturnLoans(MirStatement.Call call, ProvenanceState prov)
        {
            var result = new HashSet<Loan>();
            _module.Signatures.TryGetValue(call.CalleeKey, out var sig);

            bool returnsBorrow = call.RefDestLocalId is not null;
            if (!returnsBorrow && call.Dest is not null && call.Dest.LocalId >= 0 && call.Dest.LocalId < _localCount)
            {
                returnsBorrow = _isBorrowCarrier[call.Dest.LocalId];
            }
            if (!returnsBorrow && sig is not null)
            {
                returnsBorrow = sig.ReturnsByRef ||
                    _module.IsViewLike(sig.ReturnType) ||
                    (sig.ReturnType is MirType.StructType rst &&
                        (rst.IsRefStruct || (_module.Structs.TryGetValue(rst.Name, out var rsdef) && rsdef.IsRefStruct)));
            }
            if (!returnsBorrow)
            {
                return result;
            }

            for (int i = 0; i < call.Args.Count; i++)
            {
                var arg = call.Args[i];
                MirParamSig? paramSig = sig is not null && i < sig.Params.Count ? sig.Params[i] : null;
                if (paramSig is not null && paramSig.IsScoped)
                {
                    continue;
                }

                if (arg.BorrowPlace is not null)
                {
                    if (arg.PassKind is MirPassKind.ImplicitThisShared or MirPassKind.ImplicitThisMut &&
                        paramSig is { IsUnscopedRefReceiver: false, Type: MirType.StructType recvSt } &&
                        !StructContainsIndirectOrViewField(recvSt, new HashSet<string>()))
                    {
                        continue;
                    }
                    bool excl = arg.PassKind is MirPassKind.Ref or MirPassKind.Out or MirPassKind.ImplicitThisMut;
                    result.UnionWith(ComputeLoansForPlaceBorrow(arg.BorrowPlace, excl, prov));
                    if (arg.Hold is int callHold) result.UnionWith(prov.GetLoans(callHold));
                    if (arg.ReleasesMovedPlace is int movedHeld) result.UnionWith(ComputeLoansForPlaceBorrow(MirPlace.Local(movedHeld), isExclusive: false, prov));
                }
                else if (arg.ValueOperand is MirOperand.CopyOrMove cm &&
                         cm.Place.LocalId >= 0 &&
                         cm.Place.LocalId < _localCount &&
                         _isBorrowCarrier[cm.Place.LocalId])
                {
                    result.UnionWith(ComputeLoansForPlaceBorrow(cm.Place,
                        _module.IsExclusiveView(cm.Type), prov));
                }
            }

            return result;
        }

        // VIB002
        private bool CheckOverlappingCallBorrows(
            IReadOnlyList<MirCallArg> args,
            ProvenanceState prov,
            bool emitDiagnostics)
        {
            if (args.Count < 2)
            {
                return false;
            }

            var argPaths = new List<(List<PlacePath> Paths, bool IsExclusive, VibraniumSpan Span)>();
            foreach (var arg in args)
            {
                if (arg.BorrowPlace is not null && arg.PassKind != MirPassKind.ByValue)
                {
                    bool excl = arg.PassKind is MirPassKind.Ref or MirPassKind.Out or MirPassKind.ImplicitThisMut;
                    var paths = ExpandEffectivePaths(arg.BorrowPlace, prov);
                    if (arg.Hold is int callHold) paths.AddRange(ExpandEffectivePaths(MirPlace.Local(callHold), prov));
                    argPaths.Add((paths, excl, arg.Span));
                }
                else if (arg.ValueOperand is MirOperand.CopyOrMove cm &&
                         cm.Place.LocalId >= 0 &&
                         cm.Place.LocalId < _localCount &&
                         _isBorrowCarrier[cm.Place.LocalId])
                {
                    var paths = new List<PlacePath>();
                    bool excl = _module.IsExclusiveView(cm.Type);
                    foreach (var l in prov.GetLoans(cm.Place.LocalId))
                    {
                        if (l.Kind == LoanKind.PlaceBorrow && l.Path is not null)
                        {
                            paths.Add(l.Path);
                        }
                        else if (l.Kind == LoanKind.LocalStack)
                        {
                            paths.Add(new PlacePath(l.CursorOrStackLocalId, Array.Empty<PathSegment>()));
                        }
                    }
                    if (paths.Count > 0)
                    {
                        argPaths.Add((paths, excl, arg.Span));
                    }
                }
            }

            for (int i = 0; i < argPaths.Count; i++)
            {
                for (int j = i + 1; j < argPaths.Count; j++)
                {
                    if (!argPaths[i].IsExclusive && !argPaths[j].IsExclusive)
                    {
                        continue;
                    }
                    if (AnyPathsOverlap(argPaths[i].Paths, argPaths[j].Paths))
                    {
                        if (emitDiagnostics)
                        {
                            Emit(
                                VibraniumDiagnosticIds.OverlappingCallBorrows,
                                "Overlapping borrows in a single call where at least one borrow is exclusive.",
                                argPaths[j].Span);
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        private List<PlacePath> ExpandEffectivePaths(MirPlace place, ProvenanceState prov)
        {
            var list = new List<PlacePath> { PlacePath.FromPlace(place) };
            if (place.LocalId >= 0 && place.LocalId < _localCount && _isBorrowCarrier[place.LocalId])
            {
                foreach (var loan in prov.GetLoans(place.LocalId))
                {
                    if (loan.Kind == LoanKind.PlaceBorrow && loan.Path is not null)
                    {
                        if (place.Projections.Count > 0 && place.Projections[0] is MirProjection.Deref)
                        {
                            var combinedSegs = new List<PathSegment>(loan.Path.Segments);
                            for (int k = 1; k < place.Projections.Count; k++)
                            {
                                combinedSegs.Add(place.Projections[k] is MirProjection.Field f
                                    ? PathSegment.Field(f.FieldName)
                                    : PathSegment.Wildcard);
                            }
                            list.Add(new PlacePath(loan.Path.RootLocalId, combinedSegs));
                        }
                        else
                        {
                            list.Add(loan.Path);
                        }
                    }
                }
            }
            return list;
        }

        private static bool AnyPathsOverlap(List<PlacePath> a, List<PlacePath> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                for (int j = 0; j < b.Count; j++)
                {
                    if (a[i].Overlaps(b[j]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // VIB003 conflict checks
        private bool IsSwitchMatchBorrow(int localId) =>
            localId >= 0 && localId < _localCount && _fn.Locals[localId].IsSwitchMatchBorrow;

        private bool IsCallHold(int localId) =>
            localId >= 0 && localId < _localCount && _fn.Locals[localId].IsCallHold;

        private void CheckSwitchMatchRefRebind(int refLocalId, ProvenanceState prov, HashSet<int> liveAfter,
            VibraniumSpan span, bool emitDiagnostics)
        {
            if (!emitDiagnostics || refLocalId < 0 || refLocalId >= _localCount || _fn.Locals[refLocalId].IsCompilerTemp)
            {
                return;
            }
            var frozenSlot = Loan.ForSwitchMatchRefSlot(refLocalId);
            if (liveAfter.Any(id => IsSwitchMatchBorrow(id) && prov.GetLoans(id).Contains(frozenSlot)))
            {
                Emit(VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                    $"Cannot re-point '{_fn.Locals[refLocalId].Name}' while borrowed by the switch match.", span);
            }
        }

        private string DisplayBorrowHolder(int localId) => _fn.Locals[localId] switch
        {
            { IsSwitchMatchBorrow: true } => "the switch match",
            { RefRepointSnapshotOf: int source } => $"the old value of '{_fn.Locals[source].Name}'",
            var local => $"'{local.Name}'",
        };

        private bool IsBorrowAncestor(int liveId, HashSet<int> ancestors) =>
            ancestors.Contains(liveId) || _fn.Locals[liveId].RefRepointSnapshotOf is int source && ancestors.Contains(source);

        // The loans of the live carrier `liveId` that a use of `place` must respect. An ancestor of the place's carrier is
        // suspended by that use, except for the loans it holds on the carrier itself: those were taken through the
        // carrier (N105: a ref local re-pointed into a borrow made through it), so the carrier is frozen by them.
        // A step loan has no path: a cursor that the ancestor was stepped from is frozen as a whole.
        // A two-phase reservation (an assignment-target snapshot, a reserved receiver or element) is a use of the place it reserved.
        private IEnumerable<Loan> LoansInForce(int liveId, MirPlace place, HashSet<int>? ancestors, ProvenanceState prov)
        {
            var loans = prov.GetLoans(liveId);
            if (ancestors is null || !IsBorrowAncestor(liveId, ancestors)) return loans;
            int carrier = ResolveReservations(place).LocalId;
            if (!ancestors.Contains(liveId) || liveId == place.LocalId || liveId == carrier) return Array.Empty<Loan>();
            var onCarrier = loans.Where(loan => loan.CursorOrStackLocalId == carrier);
            return onCarrier.Any(loan => loan.Kind == LoanKind.StepCursor) ? loans : onCarrier;
        }

        // A two-phase reservation: a mutable two-phase BindRef reserves its target, a mutable two-phase element reference the element,
        // and the call-operand reborrow of a mutable Span argument (checked as a read, used exclusively by the call) the span.
        private static (int Temp, MirPlace Reserved)? Reservation(MirStatement statement) => statement switch
        {
            MirStatement.BindRef { IsMutable: true, IsTwoPhaseBorrow: true } bind => (bind.RefLocalId, bind.Target),
            MirStatement.Intrinsic { Kind: MirIntrinsicKind.VecIndexRef or MirIntrinsicKind.SpanIndexRef, RefDestIsMutable: true,
                IsTwoPhaseBorrow: true, RefDestLocalId: int element, PlaceArgs.Count: > 0 } elementRef => (element, ElementPlace(elementRef)),
            MirStatement.Assign { Target: { Projections.Count: 0 } dest, Rvalue: MirRvalue.Use { IsCallOperandReborrow: true,
                Operand: MirOperand.CopyOrMove { Type: MirType.SpanType { IsReadOnly: false } } span } } => (dest.LocalId, span.Place),
            _ => null,
        };

        private static MirPlace ElementPlace(MirStatement.Intrinsic intr) =>
            new(intr.PlaceArgs[0].LocalId, intr.PlaceArgs[0].Projections.Concat(new MirProjection[] { new MirProjection.Index(intr.ValueArgs.FirstOrDefault() ?? new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, intr.Span), MirType.Int32) }).ToList());

        // The place a use of `place` reaches: a reserving temp stands for the place it reserved, hop by hop (a store's snapshot
        // of a flow capture's reservation, an element of a reserved receiver), keeping the projections after each temp's deref.
        private MirPlace ResolveReservations(MirPlace place)
        {
            for (int hop = 0; hop < _localCount && _twoPhaseReservations.TryGetValue(place.LocalId, out var reserved); hop++)
            {
                place = new MirPlace(reserved.LocalId, reserved.Projections.Concat(place.Projections.Skip(1)).ToList());
            }
            return place;
        }

        private void CheckWriteConflictsWithActiveLoans(
            MirPlace targetPlace,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            int excludedCarrierId,
            VibraniumSpan span,
            bool emitDiagnostics)
        {
            if (!emitDiagnostics) return;

            bool writesWholeLocal = targetPlace.Projections.Count == 0;
            bool isRebindingByValueViewParam = writesWholeLocal &&
                targetPlace.LocalId >= 0 &&
                targetPlace.LocalId < _localCount &&
                _fn.Locals[targetPlace.LocalId] is { IsParam: true, PassKind: MirPassKind.ByValue } viewParam &&
                _module.IsViewLike(viewParam.Type);
            var targetPaths = writesWholeLocal
                ? new List<PlacePath> { PlacePath.FromPlace(targetPlace) }
                : ExpandEffectivePaths(targetPlace, prov);
            var reservedPlace = ResolveReservations(targetPlace);
            // Only a write through a reserving temp reaches the place it reserved; a whole write sets the temp (the reborrow's Assign).
            if (!writesWholeLocal && reservedPlace.LocalId != targetPlace.LocalId)
            {
                targetPaths.Add(PlacePath.FromPlace(reservedPlace));
            }

            HashSet<int>? ancestorCarriers = null;
            if (!writesWholeLocal && targetPlace.LocalId >= 0 && targetPlace.LocalId < _localCount && _isBorrowCarrier[targetPlace.LocalId])
            {
                ancestorCarriers = new HashSet<int> { targetPlace.LocalId };
                foreach (var selfLoan in prov.GetLoans(targetPlace.LocalId))
                {
                    int ancId = selfLoan.CursorOrStackLocalId;
                    if (ancId >= 0 && ancId < _localCount && _isBorrowCarrier[ancId])
                    {
                        ancestorCarriers.Add(ancId);
                    }
                }
            }

            // A store through a shared binding may target a ref temporary formed while
            // preparing a field or index assignment. That preparation already rejects
            // the mutable access (VIB004); do not add a second error from the match loan.
            bool invalidSharedWrite = IsMutatingThroughSharedBorrow(targetPlace, isIndirectOrMethodReceiver: false) ||
                (ancestorCarriers is not null && _fn.Locals[targetPlace.LocalId].IsCompilerTemp &&
                    ancestorCarriers.Any(id => _isSharedPatternBinding[id]));
            foreach (int liveId in liveAfter)
            {
                if ((invalidSharedWrite && IsSwitchMatchBorrow(liveId)) || liveId == excludedCarrierId)
                {
                    continue;
                }
                foreach (var loan in LoansInForce(liveId, targetPlace, ancestorCarriers, prov))
                {
                    if (writesWholeLocal && loan.Kind == LoanKind.StepCursor && loan.CursorOrStackLocalId == targetPlace.LocalId)
                    {
                        Emit(
                            VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Cannot update cursor '{_fn.Locals[targetPlace.LocalId].Name}' while pattern binding '{_fn.Locals[liveId].Name}' is still live.",
                            span);
                        return;
                    }
                    // D3: rebinding a by-value view parameter leaves the loans on its old referent alone; only an F13 match
                    // whose scrutinee is the parameter itself stops it (N91).
                    if (loan.Kind == LoanKind.PlaceBorrow && loan.Path is not null &&
                        (!isRebindingByValueViewParam ||
                         IsSwitchMatchBorrow(liveId) && _sourceLocalAliases.TryGetValue(liveId, out var scrutinee) &&
                         scrutinee.LocalId == targetPlace.LocalId && scrutinee.Projections.Count == 0))
                    {
                        foreach (var tp in targetPaths)
                        {
                            if (loan.Path.Overlaps(tp))
                            {
                                int ownerId = tp.RootLocalId >= 0 && tp.RootLocalId < _localCount ? tp.RootLocalId : targetPlace.LocalId;
                                Emit(
                                    VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                                    $"Cannot mutate '{_fn.Locals[ownerId].Name}' while borrowed by {DisplayBorrowHolder(liveId)}.",
                                    span);
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void CheckCursorMutationConflicts(
            MirPlace cursorPlace,
            int excludedDestCarrierId,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            VibraniumSpan span,
            bool emitDiagnostics)
        {
            if (!emitDiagnostics) return;

            var cursorPath = PlacePath.FromPlace(cursorPlace);
            foreach (int liveId in liveAfter)
            {
                if (liveId == cursorPlace.LocalId || liveId == excludedDestCarrierId ||
                    _fn.Locals[liveId].RefRepointSnapshotOf is int source && CollectAncestorCarriers(cursorPlace, prov).Contains(source))
                {
                    continue;
                }
                foreach (var loan in prov.GetLoans(liveId))
                {
                    if (loan.Kind == LoanKind.StepCursor && cursorPlace.Projections.Count == 0 && loan.CursorOrStackLocalId == cursorPlace.LocalId)
                    {
                        Emit(
                            VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Cannot mutate through cursor '{_fn.Locals[cursorPlace.LocalId].Name}' while derived borrow '{_fn.Locals[liveId].Name}' is still live.",
                            span);
                        return;
                    }
                    if (loan.Kind == LoanKind.PlaceBorrow && loan.Path is not null && loan.Path.Overlaps(cursorPath))
                    {
                        Emit(
                            VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Cannot mutate through '{_fn.Locals[cursorPlace.LocalId].Name}' while borrowed by {DisplayBorrowHolder(liveId)}.",
                            span);
                        return;
                    }
                }
            }
        }

        private void CheckMoveConflictsWithActiveLoans(
            MirPlace movedPlace,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            VibraniumSpan span,
            bool emitDiagnostics,
            int? selectingSwitchMatchBorrowId)
        {
            if (!emitDiagnostics) return;

            var movedPaths = ExpandEffectivePaths(movedPlace, prov);
            var operandPaths = CallOperandPaths(movedPlace, movedPaths);
            var ancestors = CollectAncestorCarriers(movedPlace, prov);
            int ordinaryConflict = -1;
            bool cursorConflict = false;
            foreach (int liveId in liveAfter)
            {
                if (liveId == selectingSwitchMatchBorrowId) continue;
                bool retained = IsRetainedCallOperand(liveId, span);
                foreach (var loan in LoansInForce(liveId, movedPlace, ancestors, prov))
                {
                    bool cursor = movedPlace.Projections.Count == 0 && loan.Kind == LoanKind.StepCursor &&
                        loan.CursorOrStackLocalId == movedPlace.LocalId;
                    bool overlaps = cursor || loan.Kind == LoanKind.PlaceBorrow && loan.Path is not null &&
                        (retained ? operandPaths : movedPaths).Any(loan.Path.Overlaps);
                    if (!overlaps) continue;
                    if (retained)
                    {
                        _rejectedActivationMoves.Add((movedPlace, span));
                        Emit(VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Cannot move '{DisplayPlace(movedPlace)}' while it is borrowed by an earlier argument or receiver of the same call.", span);
                        return;
                    }
                    if (ordinaryConflict < 0)
                    {
                        ordinaryConflict = liveId;
                        cursorConflict = cursor;
                    }
                }
            }
            if (ordinaryConflict >= 0)
            {
                // A borrow-rejected move never consumes its source. In a loop,
                // committing it would invent a VIB001 on the next iteration.
                _rejectedActivationMoves.Add((movedPlace, span));
                string message = cursorConflict
                    ? $"Cannot move '{DisplayPlace(movedPlace)}' while derived borrow '{DisplayLocal(ordinaryConflict)}' is still live."
                    : $"Cannot move '{DisplayPlace(movedPlace)}' while borrowed by {(IsSwitchMatchBorrow(ordinaryConflict) ? "the switch match" : $"'{DisplayLocal(ordinaryConflict)}'")}.";
                Emit(VibraniumDiagnosticIds.ConflictWithActiveBorrow, message, span);
            }
        }

        private bool IsDirectBorrowedNonCopyPatternBinding(MirPlace place) =>
            place.Projections.Count == 0 &&
            place.LocalId >= 0 &&
            place.LocalId < _localCount &&
            _isBorrowedPatternBinding[place.LocalId] &&
            !_module.IsCopy(_fn.Locals[place.LocalId].Type);

        private HashSet<int> CollectAncestorCarriers(MirPlace place, ProvenanceState prov)
        {
            var ancestors = new HashSet<int> { place.LocalId };
            if (place.LocalId >= 0 && place.LocalId < _localCount && _isBorrowCarrier[place.LocalId])
            {
                foreach (var selfLoan in prov.GetLoans(place.LocalId))
                {
                    int ancId = selfLoan.CursorOrStackLocalId;
                    if (ancId >= 0 && ancId < _localCount && _isBorrowCarrier[ancId])
                    {
                        ancestors.Add(ancId);
                    }
                }
            }
            return ancestors;
        }

        private void CheckReadConflictsWithActiveLoans(
            MirPlace readPlace,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            VibraniumSpan span,
            bool emitDiagnostics)
        {
            if (!emitDiagnostics) return;

            var readPaths = ExpandEffectivePaths(readPlace, prov);
            var operandPaths = CallOperandPaths(readPlace, readPaths);
            var ancestorCarriers = CollectAncestorCarriers(readPlace, prov);
            foreach (int liveId in liveAfter)
            {
                var checkedPaths = IsRetainedCallOperand(liveId, span) ? operandPaths : readPaths;
                foreach (var loan in LoansInForce(liveId, readPlace, ancestorCarriers, prov))
                {
                    if (loan.Kind == LoanKind.PlaceBorrow && loan.IsActiveExclusive && loan.Path is not null)
                    {
                        foreach (var rp in checkedPaths)
                        {
                            if (loan.Path.Overlaps(rp))
                            {
                                int ownerId = rp.RootLocalId >= 0 && rp.RootLocalId < _localCount ? rp.RootLocalId : readPlace.LocalId;
                                Emit(
                                    VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                                    $"Cannot read '{_fn.Locals[ownerId].Name}' while exclusively borrowed by {DisplayBorrowHolder(liveId)}.",
                                    span);
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void CheckBorrowCreationConflicts(
            MirPlace targetPlace,
            bool isExclusive,
            int destCarrierId,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            VibraniumSpan span,
            bool emitDiagnostics,
            int? selectingSwitchMatchBorrowId = null)
        {
            if (!emitDiagnostics) return;

            var targetPaths = ExpandEffectivePaths(targetPlace, prov);
            var ancestorCarriers = CollectAncestorCarriers(targetPlace, prov);
            foreach (int liveId in liveAfter)
            {
                // Reborrowing from `targetPlace.LocalId` (or a carrier chain) suspends ancestor carriers rather than conflicting with them;
                // and overwriting `destCarrierId` kills `destCarrierId`'s old borrow.
                if (liveId == destCarrierId || liveId == selectingSwitchMatchBorrowId)
                {
                    continue;
                }
                // A mutable receiver or ref already rejected for going through a shared binding
                // (VIB004) never activates. Do not add an F13 cascade for that same attempt.
                if (isExclusive && IsSwitchMatchBorrow(liveId) &&
                    IsMutatingThroughSharedBorrow(targetPlace, isIndirectOrMethodReceiver: true))
                {
                    continue;
                }
                foreach (var loan in LoansInForce(liveId, targetPlace, ancestorCarriers, prov))
                {
                    if (loan.Kind == LoanKind.PlaceBorrow && loan.Path is not null &&
                        (isExclusive || loan.IsActiveExclusive))
                    {
                        foreach (var tp in targetPaths)
                        {
                            if (loan.Path.Overlaps(tp))
                            {
                                int ownerId = tp.RootLocalId >= 0 && tp.RootLocalId < _localCount ? tp.RootLocalId : targetPlace.LocalId;
                                Emit(
                                    VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                                    $"Cannot borrow '{_fn.Locals[ownerId].Name}' {(isExclusive ? "mutably" : "immutably")} while borrowed by {DisplayBorrowHolder(liveId)}.",
                                    span);
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void CheckScopeExitConflicts(
            int exitingLocalId,
            ProvenanceState prov,
            HashSet<int> liveAfter,
            VibraniumSpan span,
            bool emitDiagnostics)
        {
            if (!emitDiagnostics) return;

            bool isBorrowedNonStackLocal = _isBorrowCarrier[exitingLocalId];
            var exitingPath = new PlacePath(exitingLocalId, Array.Empty<PathSegment>());

            foreach (int liveId in liveAfter)
            {
                if (liveId == exitingLocalId) continue;
                foreach (var loan in prov.GetLoans(liveId))
                {
                    if (loan.Kind == LoanKind.LocalStack && loan.CursorOrStackLocalId == exitingLocalId)
                    {
                        Emit(
                            VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Stack-allocated buffer '{_fn.Locals[exitingLocalId].Name}' exits scope while borrowed by {DisplayBorrowHolder(liveId)}.",
                            span);
                        return;
                    }
                    if (!isBorrowedNonStackLocal && loan.Kind == LoanKind.PlaceBorrow && loan.Path is not null && loan.Path.Overlaps(exitingPath))
                    {
                        Emit(
                            VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Local '{_fn.Locals[exitingLocalId].Name}' goes out of scope while borrowed by {DisplayBorrowHolder(liveId)}.",
                            span);
                        return;
                    }
                }
            }
        }

        // A by-value parameter's lexical scope is the method body, so it ends at every return,
        // although the lowering emits no ScopeExit for parameters. Stack storage of a collection
        // expression assigned to such a parameter is owned by that scope (SPEC 3.7), so a returned
        // view of it outlives its owner: VIB003, as for a local destination, besides VIB007.
        private void CheckParameterScopeExitAtReturn(MirTerminator.Return ret, HashSet<Loan> returnedLoans)
        {
            foreach (var loan in returnedLoans)
            {
                int ownerId = loan.CursorOrStackLocalId;
                if (loan.Kind != LoanKind.LocalStack || ownerId < 0 || ownerId >= _localCount)
                {
                    continue;
                }

                var owner = _fn.Locals[ownerId];
                if (owner.IsParam && owner.PassKind == MirPassKind.ByValue)
                {
                    Emit(
                        VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                        $"Stack-allocated buffer '{owner.Name}' exits scope while borrowed by the returned value.",
                        ret.Span);
                    return;
                }
            }
        }

        // VIB007: Escaping Borrow / Provenance Check
        private void CheckReturnEscape(MirTerminator.Return ret, HashSet<Loan> returnedLoans)
        {
            bool returnsBorrowOrView = ret.RefReturnPlace is not null ||
                _fn.Signature.ReturnsByRef ||
                _module.IsViewLike(_fn.Signature.ReturnType) ||
                (_fn.Signature.ReturnType is MirType.StructType st &&
                    (st.IsRefStruct || (_module.Structs.TryGetValue(st.Name, out var sdef) && sdef.IsRefStruct)));

            if (!returnsBorrowOrView || returnedLoans.Count == 0)
            {
                return;
            }

            foreach (var loan in returnedLoans)
            {
                if (loan.Kind == LoanKind.SnapshotOrigin) continue;
                if (loan.Kind == LoanKind.LocalStack)
                {
                    Emit(
                        VibraniumDiagnosticIds.EscapingBorrow,
                        "Cannot return a reference or view backed by stack-allocated storage.",
                        ret.Span);
                    return;
                }

                int rootId = loan.CursorOrStackLocalId;
                if (rootId < 0 || rootId >= _localCount)
                {
                    continue;
                }

                // A non-parameter borrow carrier (e.g. a ref local / __idx_ref temp / Span / borrowed class binding)
                // whose loan projects through its pointer/view indirection is a relay: its backing owner/stack/parameter
                // loans were already transitively unioned into returnedLoans via prov.GetLoans(rootId).
                var rootLocal = _fn.Locals[rootId];
                if (!rootLocal.IsParam &&
                    _isBorrowCarrier[rootId] &&
                    loan.Kind == LoanKind.PlaceBorrow &&
                    (rootLocal.Type is MirType.SpanType or MirType.OptionRefType or MirType.Utf8ScanViewType ||
                     (loan.Path is { Segments.Count: > 0 } &&
                      (rootLocal.IsRefLocal || rootLocal.Type is MirType.OptionMutType or MirType.ClassType))))
                {
                    continue;
                }

                if (!CanLocalEscapeInReturn(rootId, loan))
                {
                    var local = _fn.Locals[rootId];
                    Emit(
                        VibraniumDiagnosticIds.EscapingBorrow,
                        $"Returned reference or view escapes local or scoped/by-value owner '{local.Name}'.",
                        ret.Span);
                    return;
                }
            }
        }

        private bool CanLocalEscapeInReturn(int localId, Loan loan)
        {
            var local = _fn.Locals[localId];
            if (!local.IsParam || local.IsScoped)
            {
                return false;
            }

            // Find matching MirParamSig (parameters appear in order among IsParam locals)
            MirParamSig? paramSig = null;
            int paramIdx = 0;
            for (int i = 0; i <= localId && i < _localCount; i++)
            {
                if (_fn.Locals[i].IsParam)
                {
                    if (i == localId && paramIdx < _fn.Signature.Params.Count)
                    {
                        paramSig = _fn.Signature.Params[paramIdx];
                    }
                    paramIdx++;
                }
            }

            if (paramSig is not null && paramSig.IsScoped)
            {
                return false;
            }

            switch (local.PassKind)
            {
                case MirPassKind.In:
                case MirPassKind.Ref:
                case MirPassKind.Out:
                    return true;

                case MirPassKind.ImplicitThisShared:
                case MirPassKind.ImplicitThisMut:
                    if (local.Type is MirType.ClassType)
                    {
                        return true;
                    }
                    if (paramSig?.IsUnscopedRefReceiver == true)
                    {
                        return true;
                    }
                    return DoesStructSelfLoanProjectThroughIndirection(local.Type, loan);

                case MirPassKind.ByValue:
                    // Only view-like parameters (Span, ReadOnlySpan, OptionRef, OptionMut) borrow caller memory;
                    // by-value owners (ClassType, Vec, Option<Node>, primitives, structs) die at callee return!
                    return _module.IsViewLike(local.Type) ||
                        (local.Type is MirType.StructType st &&
                            (st.IsRefStruct || (_module.Structs.TryGetValue(st.Name, out var sdef) && sdef.IsRefStruct)));

                default:
                    return false;
            }
        }

        private void CheckDanglingRvalueTempLoans(int destLocalId, IEnumerable<Loan> loans, VibraniumSpan span, bool emitDiagnostics)
        {
            if (!emitDiagnostics || destLocalId < 0 || destLocalId >= _localCount)
            {
                return;
            }
            var destLocal = _fn.Locals[destLocalId];
            if (destLocal.IsCompilerTemp || !_isBorrowCarrier[destLocalId])
            {
                return;
            }
            foreach (var loan in loans)
            {
                if (loan.Kind != LoanKind.PlaceBorrow)
                {
                    continue;
                }
                int rootId = loan.CursorOrStackLocalId;
                if (rootId < 0 || rootId >= _localCount)
                {
                    continue;
                }
                var rootLocal = _fn.Locals[rootId];
                if (rootLocal.IsCompilerTemp &&
                    !rootLocal.IsParam &&
                    !rootLocal.IsPatternBinding &&
                    !_isBorrowCarrier[rootId])
                {
                    Emit(
                        VibraniumDiagnosticIds.EscapingBorrow,
                        $"Borrowed reference or view '{destLocal.Name}' cannot outlive temporary rvalue.",
                        span);
                    return;
                }
            }
        }

        private bool StructContainsIndirectOrViewField(MirType.StructType st, HashSet<string> visited)
        {
            if (st.IsRefStruct) return true;
            if (!visited.Add(st.Name) || !_module.Structs.TryGetValue(st.Name, out var sdef))
            {
                return false;
            }
            if (sdef.IsRefStruct) return true;
            foreach (var f in sdef.Fields)
            {
                if (f.Type is MirType.VecType or MirType.ClassType or MirType.SpanType or MirType.Utf8ScanViewType or MirType.OptionRefType or MirType.OptionMutType)
                {
                    return true;
                }
                if (f.Type is MirType.StructType nested && StructContainsIndirectOrViewField(nested, visited))
                {
                    return true;
                }
            }
            return false;
        }

        private bool DoesStructSelfLoanProjectThroughIndirection(MirType receiverType, Loan loan)
        {
            if (receiverType is not MirType.StructType st || loan.Kind != LoanKind.PlaceBorrow || loan.Path is null || loan.Path.Segments.Count == 0)
            {
                return false;
            }

            MirType current = st;
            foreach (var seg in loan.Path.Segments)
            {
                if (current is MirType.VecType or MirType.ClassType or MirType.SpanType or MirType.Utf8ScanViewType or MirType.OptionRefType or MirType.OptionMutType)
                {
                    return true;
                }
                if (seg.IsWildcard)
                {
                    return false;
                }
                if (current is MirType.StructType curSt && _module.Structs.TryGetValue(curSt.Name, out var sdef))
                {
                    var field = sdef.Fields.FirstOrDefault(f => f.Name == seg.FieldName);
                    if (field is null)
                    {
                        return false;
                    }
                    current = field.Type;
                    if (current is MirType.VecType or MirType.ClassType or MirType.SpanType or MirType.Utf8ScanViewType or MirType.OptionRefType or MirType.OptionMutType)
                    {
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            }

            return false;
        }
    }
}
