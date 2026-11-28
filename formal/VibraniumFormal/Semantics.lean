/-
  VibraniumFormal/Semantics.lean
  Dual Operational Semantics for Core Vibranium MIR:
  1. `evalNative`  — LLVM 18 Native Semantics: explicit heap cells (`live | freed`),
     deterministic `free` on `DropMemory` & `Dispose`, trapping on `UseAfterFree`,
     `DoubleFree`, and `DoubleDispose`.
  2. `evalManaged` — `.NET` 11 CLR Managed Semantics: GC-managed heap where `DropMemory`
     is an invisible no-op (cells are never prematurely `freed`), while scalar values,
     `Print` I/O events, and `Dispose` linear resource events execute directly.
-/

import VibraniumFormal.Syntax

namespace VibraniumFormal

/-- Runtime values stored in MIR locals. -/
inductive Val where
  | int (n : Int)
  | bool (b : Bool)
  | heapRef (a : Addr)
  | optNone
  deriving DecidableEq, Repr

/-- Convert an immediate scalar constant into a runtime value. -/
def Val.ofPrim : PrimVal → Val
  | .int n  => .int n
  | .bool b => .bool b

/-- Default zero-initialized value for zero-safe types (`VIB008`). -/
def zeroVal : Ty → Option Val
  | .CopyPrim .int  => some (.int 0)
  | .CopyPrim .bool => some (.bool false)
  | .OptionTy _     => some .optNone
  | _               => none

theorem zeroVal_of_isZeroSafe : ∀ (ty : Ty), isZeroSafe ty = true → ∃ v, zeroVal ty = some v
  | .CopyPrim .int,  _ => ⟨.int 0, rfl⟩
  | .CopyPrim .bool, _ => ⟨.bool false, rfl⟩
  | .OptionTy _,     _ => ⟨.optNone, rfl⟩
  | .HeapClass _,    h => Bool.noConfusion h
  | .RefShared _,    h => Bool.noConfusion h
  | .RefMut _,       h => Bool.noConfusion h

/-- Heap object payload and optional linear `IDisposable` resource handle. -/
structure HeapObj where
  payload : Int
  resId   : Option ResourceId
  deriving DecidableEq, Repr

/-- Heap cell state (`freed` occurs only under LLVM native execution). -/
inductive HeapCell where
  | live (obj : HeapObj)
  | freed
  deriving DecidableEq, Repr

/-- Lifecycle state of a linear `IDisposable` resource. -/
inductive ResState where
  | openRes
  | disposedRes
  deriving DecidableEq, Repr

/-- Observable external side-effect event (I/O output & deterministic `Dispose`). -/
inductive Event where
  | Printed (val : PrimVal)
  | Disposed (id : ResourceId)
  deriving DecidableEq, Repr

/-- Count occurrences of `Event.Disposed id` in an execution event trace. -/
def disposedCount (id : ResourceId) : List Event → Nat
  | [] => 0
  | .Disposed id' :: rest => (if id = id' then 1 else 0) + disposedCount id rest
  | .Printed _ :: rest    => disposedCount id rest

@[simp] theorem disposedCount_nil (id : ResourceId) :
    disposedCount id [] = 0 := rfl

theorem disposedCount_append (id : ResourceId) :
    ∀ (tr₁ tr₂ : List Event),
      disposedCount id (tr₁ ++ tr₂) = disposedCount id tr₁ + disposedCount id tr₂
  | [], _ => by simp
  | .Disposed id' :: rest, tr₂ => by
    simp [disposedCount, disposedCount_append id rest tr₂, Nat.add_assoc]
  | .Printed _ :: rest, tr₂ => by
    simp [disposedCount, disposedCount_append id rest tr₂]

@[simp] theorem disposedCount_singleton_printed (id : ResourceId) (v : PrimVal) :
    disposedCount id [.Printed v] = 0 := rfl

theorem disposedCount_singleton_disposed (id id' : ResourceId) :
    disposedCount id [.Disposed id'] = if id = id' then 1 else 0 := by
  simp [disposedCount]

/-- Runtime memory/resource safety faults. -/
inductive Error where
  | UninitializedLocal (localId : LocalId)
  | InvalidAddress (addr : Addr)
  | UseAfterFree (addr : Addr)
  | DoubleFree (addr : Addr)
  | DoubleDispose (id : ResourceId)
  | InvalidResource (addr : Addr)
  | TypeFault
  deriving DecidableEq, Repr

/-- Execution target backend mode. -/
inductive ExecMode where
  | native  -- LLVM 18 standalone execution with deterministic `free`
  | managed -- .NET 11 CLR execution with garbage-collected heap
  deriving DecidableEq, Repr

/-- Dynamic operational machine configuration. -/
structure ExecState where
  localVals : EnvMap (Option Val)      := []
  heap      : EnvMap (Option HeapCell) := []
  resources : EnvMap (Option ResState) := []
  nextAddr  : Addr                     := 0
  nextResId : ResourceId               := 0
  trace     : List Event               := []
  deriving DecidableEq, Repr

namespace ExecState

def empty : ExecState := {}

def getLocal (σ : ExecState) (l : LocalId) : Option Val :=
  EnvMap.lookup none σ.localVals l

def setLocal (σ : ExecState) (l : LocalId) (v : Option Val) : ExecState :=
  { σ with localVals := EnvMap.insert σ.localVals l v }

def getHeap (σ : ExecState) (a : Addr) : Option HeapCell :=
  EnvMap.lookup none σ.heap a

def setHeap (σ : ExecState) (a : Addr) (c : Option HeapCell) : ExecState :=
  { σ with heap := EnvMap.insert σ.heap a c }

def getRes (σ : ExecState) (id : ResourceId) : Option ResState :=
  EnvMap.lookup none σ.resources id

def setRes (σ : ExecState) (id : ResourceId) (st : Option ResState) : ExecState :=
  { σ with resources := EnvMap.insert σ.resources id st }

def emitEvent (σ : ExecState) (ev : Event) : ExecState :=
  { σ with trace := σ.trace ++ [ev] }

def bumpAddr (σ : ExecState) : ExecState :=
  { σ with nextAddr := σ.nextAddr + 1 }

def bumpAddrAndRes (σ : ExecState) : ExecState :=
  { σ with nextAddr := σ.nextAddr + 1, nextResId := σ.nextResId + 1 }

@[simp] theorem getLocal_empty (l : LocalId) : empty.getLocal l = none := rfl
@[simp] theorem getHeap_empty (a : Addr) : empty.getHeap a = none := rfl
@[simp] theorem getRes_empty (id : ResourceId) : empty.getRes id = none := rfl
@[simp] theorem nextAddr_empty : empty.nextAddr = 0 := rfl
@[simp] theorem nextResId_empty : empty.nextResId = 0 := rfl
@[simp] theorem trace_empty : empty.trace = [] := rfl

@[simp] theorem nextAddr_setLocal (σ : ExecState) (l : LocalId) (v : Option Val) :
    (σ.setLocal l v).nextAddr = σ.nextAddr := rfl
@[simp] theorem nextAddr_setHeap (σ : ExecState) (a : Addr) (c : Option HeapCell) :
    (σ.setHeap a c).nextAddr = σ.nextAddr := rfl
@[simp] theorem nextAddr_setRes (σ : ExecState) (id : ResourceId) (st : Option ResState) :
    (σ.setRes id st).nextAddr = σ.nextAddr := rfl
@[simp] theorem nextAddr_emitEvent (σ : ExecState) (ev : Event) :
    (σ.emitEvent ev).nextAddr = σ.nextAddr := rfl
@[simp] theorem nextAddr_bumpAddr (σ : ExecState) :
    σ.bumpAddr.nextAddr = σ.nextAddr + 1 := rfl
@[simp] theorem nextAddr_bumpAddrAndRes (σ : ExecState) :
    σ.bumpAddrAndRes.nextAddr = σ.nextAddr + 1 := rfl

@[simp] theorem nextResId_setLocal (σ : ExecState) (l : LocalId) (v : Option Val) :
    (σ.setLocal l v).nextResId = σ.nextResId := rfl
@[simp] theorem nextResId_setHeap (σ : ExecState) (a : Addr) (c : Option HeapCell) :
    (σ.setHeap a c).nextResId = σ.nextResId := rfl
@[simp] theorem nextResId_setRes (σ : ExecState) (id : ResourceId) (st : Option ResState) :
    (σ.setRes id st).nextResId = σ.nextResId := rfl
@[simp] theorem nextResId_emitEvent (σ : ExecState) (ev : Event) :
    (σ.emitEvent ev).nextResId = σ.nextResId := rfl
@[simp] theorem nextResId_bumpAddr (σ : ExecState) :
    σ.bumpAddr.nextResId = σ.nextResId := rfl
@[simp] theorem nextResId_bumpAddrAndRes (σ : ExecState) :
    σ.bumpAddrAndRes.nextResId = σ.nextResId + 1 := rfl

@[simp] theorem trace_setLocal (σ : ExecState) (l : LocalId) (v : Option Val) :
    (σ.setLocal l v).trace = σ.trace := rfl
@[simp] theorem trace_setHeap (σ : ExecState) (a : Addr) (c : Option HeapCell) :
    (σ.setHeap a c).trace = σ.trace := rfl
@[simp] theorem trace_setRes (σ : ExecState) (id : ResourceId) (st : Option ResState) :
    (σ.setRes id st).trace = σ.trace := rfl
@[simp] theorem trace_emitEvent (σ : ExecState) (ev : Event) :
    (σ.emitEvent ev).trace = σ.trace ++ [ev] := rfl
@[simp] theorem trace_bumpAddr (σ : ExecState) :
    σ.bumpAddr.trace = σ.trace := rfl
@[simp] theorem trace_bumpAddrAndRes (σ : ExecState) :
    σ.bumpAddrAndRes.trace = σ.trace := rfl

@[simp] theorem getLocal_setLocal_eq (σ : ExecState) (l : LocalId) (v : Option Val) :
    (σ.setLocal l v).getLocal l = v := by
  simp [getLocal, setLocal]

theorem getLocal_setLocal_ne (σ : ExecState) {l l' : LocalId} (v : Option Val) (h : l ≠ l') :
    (σ.setLocal l' v).getLocal l = σ.getLocal l := by
  simp [getLocal, setLocal, EnvMap.lookup_insert_ne _ _ _ h]

@[simp] theorem getLocal_setHeap (σ : ExecState) (l : LocalId) (a : Addr) (c : Option HeapCell) :
    (σ.setHeap a c).getLocal l = σ.getLocal l := rfl
@[simp] theorem getLocal_setRes (σ : ExecState) (l : LocalId) (id : ResourceId) (st : Option ResState) :
    (σ.setRes id st).getLocal l = σ.getLocal l := rfl
@[simp] theorem getLocal_emitEvent (σ : ExecState) (l : LocalId) (ev : Event) :
    (σ.emitEvent ev).getLocal l = σ.getLocal l := rfl
@[simp] theorem getLocal_bumpAddr (σ : ExecState) (l : LocalId) :
    σ.bumpAddr.getLocal l = σ.getLocal l := rfl
@[simp] theorem getLocal_bumpAddrAndRes (σ : ExecState) (l : LocalId) :
    σ.bumpAddrAndRes.getLocal l = σ.getLocal l := rfl

@[simp] theorem getHeap_setHeap_eq (σ : ExecState) (a : Addr) (c : Option HeapCell) :
    (σ.setHeap a c).getHeap a = c := by
  simp [getHeap, setHeap]

theorem getHeap_setHeap_ne (σ : ExecState) {a a' : Addr} (c : Option HeapCell) (h : a ≠ a') :
    (σ.setHeap a' c).getHeap a = σ.getHeap a := by
  simp [getHeap, setHeap, EnvMap.lookup_insert_ne _ _ _ h]

@[simp] theorem getHeap_setLocal (σ : ExecState) (a : Addr) (l : LocalId) (v : Option Val) :
    (σ.setLocal l v).getHeap a = σ.getHeap a := rfl
@[simp] theorem getHeap_setRes (σ : ExecState) (a : Addr) (id : ResourceId) (st : Option ResState) :
    (σ.setRes id st).getHeap a = σ.getHeap a := rfl
@[simp] theorem getHeap_emitEvent (σ : ExecState) (a : Addr) (ev : Event) :
    (σ.emitEvent ev).getHeap a = σ.getHeap a := rfl
@[simp] theorem getHeap_bumpAddr (σ : ExecState) (a : Addr) :
    σ.bumpAddr.getHeap a = σ.getHeap a := rfl
@[simp] theorem getHeap_bumpAddrAndRes (σ : ExecState) (a : Addr) :
    σ.bumpAddrAndRes.getHeap a = σ.getHeap a := rfl

@[simp] theorem getRes_setRes_eq (σ : ExecState) (id : ResourceId) (st : Option ResState) :
    (σ.setRes id st).getRes id = st := by
  simp [getRes, setRes]

theorem getRes_setRes_ne (σ : ExecState) {id id' : ResourceId} (st : Option ResState) (h : id ≠ id') :
    (σ.setRes id' st).getRes id = σ.getRes id := by
  simp [getRes, setRes, EnvMap.lookup_insert_ne _ _ _ h]

@[simp] theorem getRes_setLocal (σ : ExecState) (id : ResourceId) (l : LocalId) (v : Option Val) :
    (σ.setLocal l v).getRes id = σ.getRes id := rfl
@[simp] theorem getRes_setHeap (σ : ExecState) (id : ResourceId) (a : Addr) (c : Option HeapCell) :
    (σ.setHeap a c).getRes id = σ.getRes id := rfl
@[simp] theorem getRes_emitEvent (σ : ExecState) (id : ResourceId) (ev : Event) :
    (σ.emitEvent ev).getRes id = σ.getRes id := rfl
@[simp] theorem getRes_bumpAddr (σ : ExecState) (id : ResourceId) :
    σ.bumpAddr.getRes id = σ.getRes id := rfl
@[simp] theorem getRes_bumpAddrAndRes (σ : ExecState) (id : ResourceId) :
    σ.bumpAddrAndRes.getRes id = σ.getRes id := rfl

end ExecState

/-! ### Operational Small-Step / Statement Evaluation -/

/-- Execute a single Vibranium MIR statement under `mode` (`.native` vs `.managed`). -/
def stepStmt (mode : ExecMode) (σ : ExecState) (s : Stmt) : Except Error ExecState :=
  match s with
  | .ConstPrim dst val =>
    .ok (σ.setLocal dst (some (Val.ofPrim val)))

  | .ZeroInit dst ty =>
    match zeroVal ty with
    | some v => .ok (σ.setLocal dst (some v))
    | none   => .error .TypeFault

  | .Alloc dst disposable initVal =>
    let a := σ.nextAddr
    if disposable then
      let id := σ.nextResId
      let obj : HeapObj := { payload := initVal, resId := some id }
      .ok (((σ.bumpAddrAndRes.setHeap a (some (.live obj))).setRes id (some .openRes)).setLocal
        dst (some (.heapRef a)))
    else
      let obj : HeapObj := { payload := initVal, resId := none }
      .ok ((σ.bumpAddr.setHeap a (some (.live obj))).setLocal dst (some (.heapRef a)))

  | .CopyOrMove dst src =>
    match σ.getLocal src with
    | some v => .ok (σ.setLocal dst (some v))
    | none   => .error (.UninitializedLocal src)

  | .BorrowShared dst root =>
    match σ.getLocal root with
    | some (.heapRef a) => .ok (σ.setLocal dst (some (.heapRef a)))
    | some _            => .error .TypeFault
    | none              => .error (.UninitializedLocal root)

  | .BorrowMut dst root =>
    match σ.getLocal root with
    | some (.heapRef a) => .ok (σ.setLocal dst (some (.heapRef a)))
    | some _            => .error .TypeFault
    | none              => .error (.UninitializedLocal root)

  | .ReadShared dst refLocal =>
    match σ.getLocal refLocal with
    | none              => .error (.UninitializedLocal refLocal)
    | some (.heapRef a) =>
      match σ.getHeap a with
      | none             => .error (.InvalidAddress a)
      | some .freed      => .error (.UseAfterFree a)
      | some (.live obj) => .ok (σ.setLocal dst (some (.int obj.payload)))
    | some _            => .error .TypeFault

  | .WriteMut refLocal newVal =>
    match σ.getLocal refLocal with
    | none              => .error (.UninitializedLocal refLocal)
    | some (.heapRef a) =>
      match σ.getHeap a with
      | none             => .error (.InvalidAddress a)
      | some .freed      => .error (.UseAfterFree a)
      | some (.live obj) =>
        .ok (σ.setHeap a (some (.live { payload := newVal, resId := obj.resId })))
    | some _            => .error .TypeFault

  | .EndBorrow _refLocal =>
    .ok σ

  | .Print src =>
    match σ.getLocal src with
    | none            => .error (.UninitializedLocal src)
    | some (.int n)   => .ok (σ.emitEvent (.Printed (.int n)))
    | some (.bool b)  => .ok (σ.emitEvent (.Printed (.bool b)))
    | some _          => .error .TypeFault

  | .Dispose src =>
    match σ.getLocal src with
    | none              => .error (.UninitializedLocal src)
    | some (.heapRef a) =>
      match σ.getHeap a with
      | none             => .error (.InvalidAddress a)
      | some .freed      => .error (.UseAfterFree a)
      | some (.live obj) =>
        match obj.resId with
        | none    => .error (.InvalidResource a)
        | some id =>
          match σ.getRes id with
          | none              => .error (.InvalidResource a)
          | some .disposedRes => .error (.DoubleDispose id)
          | some .openRes     =>
            let σ₁ := (σ.setRes id (some .disposedRes)).emitEvent (.Disposed id)
            match mode with
            | .native  => .ok (σ₁.setHeap a (some .freed))
            | .managed => .ok σ₁
    | some _            => .error .TypeFault

  | .DropMemory src =>
    match σ.getLocal src with
    | none              => .error (.UninitializedLocal src)
    | some (.heapRef a) =>
      match σ.getHeap a with
      | none             => .error (.InvalidAddress a)
      | some .freed      => .error (.DoubleFree a)
      | some (.live _)   =>
        match mode with
        | .native  => .ok (σ.setHeap a (some .freed))
        | .managed => .ok σ
    | some _            => .error .TypeFault

/-- Native LLVM statement step. -/
abbrev stepNative (σ : ExecState) (s : Stmt) : Except Error ExecState :=
  stepStmt .native σ s

/-- Managed `.NET` CLR statement step. -/
abbrev stepManaged (σ : ExecState) (s : Stmt) : Except Error ExecState :=
  stepStmt .managed σ s

/-- Execute a list of statements sequentially under `mode`. -/
def evalStmts (mode : ExecMode) (σ : ExecState) : List Stmt → Except Error ExecState
  | []      => .ok σ
  | s :: ss => do
    let σ' ← stepStmt mode σ s
    evalStmts mode σ' ss

/-- Observable program result (return value + external I/O and `Dispose` trace). -/
structure ObservableOutcome where
  returnVal : Option Val
  trace     : List Event
  deriving DecidableEq, Repr

/-- Complete program execution result including final machine state and observable outcome. -/
structure ExecRun where
  finalState : ExecState
  outcome    : ObservableOutcome
  deriving DecidableEq, Repr

/-- Evaluate a program's return operand in `σ`. -/
def evalReturn (σ : ExecState) (ret : Option LocalId) : Except Error ObservableOutcome :=
  match ret with
  | none => .ok { returnVal := none, trace := σ.trace }
  | some retLocal =>
    match σ.getLocal retLocal with
    | some v => .ok { returnVal := some v, trace := σ.trace }
    | none   => .error (.UninitializedLocal retLocal)

/-- Parameterized whole-program evaluation. -/
def evalProg (mode : ExecMode) (p : Prog) : Except Error ExecRun := do
  let σ ← evalStmts mode ExecState.empty p.stmts
  let out ← evalReturn σ p.ret
  return { finalState := σ, outcome := out }

/-- Full Native LLVM Semantics (`vib` deterministic `free`). -/
def evalNative (p : Prog) : Except Error ExecRun :=
  evalProg .native p

/-- Full Managed `.NET` CLR Semantics (Roslyn / CLR GC). -/
def evalManaged (p : Prog) : Except Error ExecRun :=
  evalProg .managed p

end VibraniumFormal
