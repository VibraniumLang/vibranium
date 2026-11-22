/-
  VibraniumFormal/Checker.lean
  Executable Static Ownership, Borrow (NLL) & Linearity Checker for Core Vibranium MIR.

  Enforces Vibranium's compile-time safety rules (`docs/SPEC.md` §4 & `Diagnostics.cs`):
  - `VIB001`: Forbids reading, borrowing, disposing, or dropping `uninit` or `moved` locals.
  - `VIB002`/`VIB003`: Enforces Aliasing XOR Mutability and forbids mutating/moving/dropping
    borrowing roots while active shared or exclusive loans exist.
  - `VIB004`: Forbids mutation (`WriteMut`) through a shared reference (`RefShared`).
  - `VIB006`: Enforces exact-once consumption/disposal of every `isLinearResource` owner.
  - `VIB007`: Forbids returning escaping stack-bound references (`RefShared` / `RefMut`).
  - `VIB008`: Forbids zero-initializing non-zero-safe types (`HeapClass`, references).
-/

import VibraniumFormal.Syntax

namespace VibraniumFormal

/-- Static abstract ownership/borrow state of a single MIR local. -/
inductive LocalState where
  | uninit
  | owned (ty : Ty)
  | moved
  | sharedLoan (root : LocalId) (ty : Ty)
  | mutLoan (root : LocalId) (ty : Ty)
  deriving DecidableEq, Repr

/-- Static diagnostics corresponding directly to `VibraniumDiagnosticIds`. -/
inductive CheckError where
  | VIB001_UseAfterMoveOrUninit (localId : LocalId)
  | VIB002_BorrowConflict (root : LocalId)
  | VIB003_ConflictWithActiveBorrow (localId : LocalId)
  | VIB004_MutateThroughSharedBorrow (refLocal : LocalId)
  | VIB006_LinearResourceViolation (localId : LocalId)
  | VIB007_EscapingBorrow (localId : LocalId)
  | VIB008_ZeroUnsafeInit (localId : LocalId) (ty : Ty)
  | TypeMismatch (localId : LocalId)
  deriving DecidableEq, Repr

/-- Flow-sensitive static state tracking local states and active root loan counts. -/
structure CheckState where
  localMap  : EnvMap LocalState := []
  sharedMap : EnvMap Nat        := []
  exclMap   : EnvMap Bool       := []
  deriving DecidableEq, Repr

namespace CheckState

def empty : CheckState := {}

def getLocal (Γ : CheckState) (l : LocalId) : LocalState :=
  EnvMap.lookup .uninit Γ.localMap l

def sharedCount (Γ : CheckState) (l : LocalId) : Nat :=
  EnvMap.lookup 0 Γ.sharedMap l

def hasExclusiveLoan (Γ : CheckState) (l : LocalId) : Bool :=
  EnvMap.lookup false Γ.exclMap l

def setLocal (Γ : CheckState) (l : LocalId) (st : LocalState) : CheckState :=
  { Γ with localMap := EnvMap.insert Γ.localMap l st }

def setShared (Γ : CheckState) (l : LocalId) (n : Nat) : CheckState :=
  { Γ with sharedMap := EnvMap.insert Γ.sharedMap l n }

def setExcl (Γ : CheckState) (l : LocalId) (b : Bool) : CheckState :=
  { Γ with exclMap := EnvMap.insert Γ.exclMap l b }

def noActiveBorrows (Γ : CheckState) (l : LocalId) : Bool :=
  Γ.sharedCount l == 0 && !Γ.hasExclusiveLoan l

/-! ### Lookup & Update Simplification Lemmas -/

@[simp] theorem getLocal_empty (l : LocalId) : empty.getLocal l = .uninit := rfl
@[simp] theorem sharedCount_empty (l : LocalId) : empty.sharedCount l = 0 := rfl
@[simp] theorem hasExclusiveLoan_empty (l : LocalId) : empty.hasExclusiveLoan l = false := rfl

@[simp] theorem getLocal_setLocal_eq (Γ : CheckState) (l : LocalId) (st : LocalState) :
    (Γ.setLocal l st).getLocal l = st := by
  simp [getLocal, setLocal]

theorem getLocal_setLocal_ne (Γ : CheckState) {l l' : LocalId} (st : LocalState) (h : l ≠ l') :
    (Γ.setLocal l' st).getLocal l = Γ.getLocal l := by
  simp [getLocal, setLocal, EnvMap.lookup_insert_ne _ _ _ h]

theorem getLocal_setLocal (Γ : CheckState) (l l' : LocalId) (st : LocalState) :
    (Γ.setLocal l' st).getLocal l = if l = l' then st else Γ.getLocal l := rfl

@[simp] theorem getLocal_setShared (Γ : CheckState) (l l' : LocalId) (n : Nat) :
    (Γ.setShared l' n).getLocal l = Γ.getLocal l := rfl

@[simp] theorem getLocal_setExcl (Γ : CheckState) (l l' : LocalId) (b : Bool) :
    (Γ.setExcl l' b).getLocal l = Γ.getLocal l := rfl

@[simp] theorem sharedCount_setLocal (Γ : CheckState) (l l' : LocalId) (st : LocalState) :
    (Γ.setLocal l' st).sharedCount l = Γ.sharedCount l := rfl

@[simp] theorem sharedCount_setShared_eq (Γ : CheckState) (l : LocalId) (n : Nat) :
    (Γ.setShared l n).sharedCount l = n := by
  simp [sharedCount, setShared]

theorem sharedCount_setShared_ne (Γ : CheckState) {l l' : LocalId} (n : Nat) (h : l ≠ l') :
    (Γ.setShared l' n).sharedCount l = Γ.sharedCount l := by
  simp [sharedCount, setShared, EnvMap.lookup_insert_ne _ _ _ h]

theorem sharedCount_setShared (Γ : CheckState) (l l' : LocalId) (n : Nat) :
    (Γ.setShared l' n).sharedCount l = if l = l' then n else Γ.sharedCount l := rfl

@[simp] theorem sharedCount_setExcl (Γ : CheckState) (l l' : LocalId) (b : Bool) :
    (Γ.setExcl l' b).sharedCount l = Γ.sharedCount l := rfl

@[simp] theorem hasExclusiveLoan_setLocal (Γ : CheckState) (l l' : LocalId) (st : LocalState) :
    (Γ.setLocal l' st).hasExclusiveLoan l = Γ.hasExclusiveLoan l := rfl

@[simp] theorem hasExclusiveLoan_setShared (Γ : CheckState) (l l' : LocalId) (n : Nat) :
    (Γ.setShared l' n).hasExclusiveLoan l = Γ.hasExclusiveLoan l := rfl

@[simp] theorem hasExclusiveLoan_setExcl_eq (Γ : CheckState) (l : LocalId) (b : Bool) :
    (Γ.setExcl l b).hasExclusiveLoan l = b := by
  simp [hasExclusiveLoan, setExcl]

theorem hasExclusiveLoan_setExcl_ne (Γ : CheckState) {l l' : LocalId} (b : Bool) (h : l ≠ l') :
    (Γ.setExcl l' b).hasExclusiveLoan l = Γ.hasExclusiveLoan l := by
  simp [hasExclusiveLoan, setExcl, EnvMap.lookup_insert_ne _ _ _ h]

theorem hasExclusiveLoan_setExcl (Γ : CheckState) (l l' : LocalId) (b : Bool) :
    (Γ.setExcl l' b).hasExclusiveLoan l = if l = l' then b else Γ.hasExclusiveLoan l := rfl

theorem noActiveBorrows_iff (Γ : CheckState) (l : LocalId) :
    Γ.noActiveBorrows l = true ↔ (Γ.sharedCount l = 0 ∧ Γ.hasExclusiveLoan l = false) := by
  simp [noActiveBorrows]

end CheckState

/-! ### Destination Overwrite & Statement Type/Borrow Checking -/

/-- Validates that `dst` can be safely assigned without clobbering an active loan,
    a borrowed root (`VIB003`), an unconsumed linear resource (`VIB006`), or an
    undropped affine heap owner. -/
def checkWriteTarget (Γ : CheckState) (dst : LocalId) : Except CheckError Unit :=
  if !Γ.noActiveBorrows dst then
    .error (.VIB003_ConflictWithActiveBorrow dst)
  else
    match Γ.getLocal dst with
    | .uninit | .moved => .ok ()
    | .owned ty =>
      if isCopy ty then .ok ()
      else if isLinearResource ty then .error (.VIB006_LinearResourceViolation dst)
      else .error (.TypeMismatch dst)
    | .sharedLoan _ _ | .mutLoan _ _ =>
      .error (.VIB003_ConflictWithActiveBorrow dst)

theorem checkWriteTarget_ok_props {Γ : CheckState} {dst : LocalId}
    (h : checkWriteTarget Γ dst = .ok ()) :
    Γ.sharedCount dst = 0 ∧
    Γ.hasExclusiveLoan dst = false ∧
    (∀ r t, Γ.getLocal dst ≠ .sharedLoan r t) ∧
    (∀ r t, Γ.getLocal dst ≠ .mutLoan r t) ∧
    (∀ t, Γ.getLocal dst = .owned t → isCopy t = true) := by
  unfold checkWriteTarget at h
  cases hnb : Γ.noActiveBorrows dst
  · simp [hnb] at h
  · simp only [hnb, Bool.not_true, Bool.false_eq_true, ↓reduceIte] at h
    have ⟨hsc, hex⟩ := (CheckState.noActiveBorrows_iff Γ dst).mp hnb
    refine ⟨hsc, hex, ?_, ?_, ?_⟩
    · intro r t hloc
      rw [hloc] at h
      cases h
    · intro r t hloc
      rw [hloc] at h
      cases h
    · intro t hloc
      rw [hloc] at h
      cases hc : isCopy t
      · simp only [hc, Bool.false_eq_true, ↓reduceIte] at h
        split at h <;> cases h
      · rfl

/-- Flow-sensitive checker for a single Vibranium MIR statement. -/
def checkStmt (Γ : CheckState) (s : Stmt) : Except CheckError CheckState :=
  match s with
  | .ConstPrim dst val => do
    checkWriteTarget Γ dst
    return Γ.setLocal dst (.owned (.CopyPrim val.ty))

  | .ZeroInit dst ty =>
    if !isZeroSafe ty then
      .error (.VIB008_ZeroUnsafeInit dst ty)
    else do
      checkWriteTarget Γ dst
      return Γ.setLocal dst (.owned ty)

  | .Alloc dst disposable _initVal => do
    checkWriteTarget Γ dst
    return Γ.setLocal dst (.owned (.HeapClass disposable))

  | .CopyOrMove dst src =>
    match Γ.getLocal src with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit src)
    | .owned ty =>
      if isCopy ty then do
        checkWriteTarget Γ dst
        return Γ.setLocal dst (.owned ty)
      else
        if !Γ.noActiveBorrows src then
          .error (.VIB003_ConflictWithActiveBorrow src)
        else do
          checkWriteTarget Γ dst
          return (Γ.setLocal src .moved).setLocal dst (.owned ty)
    | .sharedLoan root ty => do
      checkWriteTarget Γ dst
      return (Γ.setLocal dst (.sharedLoan root ty)).setShared root (Γ.sharedCount root + 1)
    | .mutLoan root ty => do
      checkWriteTarget Γ dst
      return (Γ.setLocal src .moved).setLocal dst (.mutLoan root ty)

  | .BorrowShared dst root =>
    match Γ.getLocal root with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit root)
    | .owned (.HeapClass disp) =>
      if Γ.hasExclusiveLoan root then
        .error (.VIB002_BorrowConflict root)
      else do
        checkWriteTarget Γ dst
        return (Γ.setLocal dst (.sharedLoan root (.RefShared (.HeapClass disp)))).setShared
          root (Γ.sharedCount root + 1)
    | _ =>
      .error (.TypeMismatch root)

  | .BorrowMut dst root =>
    match Γ.getLocal root with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit root)
    | .owned (.HeapClass disp) =>
      if !Γ.noActiveBorrows root then
        .error (.VIB002_BorrowConflict root)
      else do
        checkWriteTarget Γ dst
        return (Γ.setLocal dst (.mutLoan root (.RefMut (.HeapClass disp)))).setExcl root true
    | _ =>
      .error (.TypeMismatch root)

  | .ReadShared dst refLocal =>
    match Γ.getLocal refLocal with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit refLocal)
    | .sharedLoan _ (.RefShared (.HeapClass _)) => do
      checkWriteTarget Γ dst
      return Γ.setLocal dst (.owned (.CopyPrim .int))
    | .mutLoan _ (.RefMut (.HeapClass _)) => do
      checkWriteTarget Γ dst
      return Γ.setLocal dst (.owned (.CopyPrim .int))
    | _ =>
      .error (.TypeMismatch refLocal)

  | .WriteMut refLocal _newVal =>
    match Γ.getLocal refLocal with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit refLocal)
    | .sharedLoan _ _ =>
      .error (.VIB004_MutateThroughSharedBorrow refLocal)
    | .mutLoan _ (.RefMut (.HeapClass _)) =>
      .ok Γ
    | _ =>
      .error (.TypeMismatch refLocal)

  | .EndBorrow refLocal =>
    match Γ.getLocal refLocal with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit refLocal)
    | .sharedLoan root _ =>
      .ok ((Γ.setLocal refLocal .moved).setShared root (Γ.sharedCount root - 1))
    | .mutLoan root _ =>
      .ok ((Γ.setLocal refLocal .moved).setExcl root false)
    | _ =>
      .error (.TypeMismatch refLocal)

  | .Print src =>
    match Γ.getLocal src with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit src)
    | .owned (.CopyPrim _) =>
      .ok Γ
    | _ =>
      .error (.TypeMismatch src)

  | .Dispose src =>
    match Γ.getLocal src with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit src)
    | .owned (.HeapClass true) =>
      if !Γ.noActiveBorrows src then
        .error (.VIB003_ConflictWithActiveBorrow src)
      else
        .ok (Γ.setLocal src .moved)
    | .owned _ =>
      .error (.VIB006_LinearResourceViolation src)
    | _ =>
      .error (.TypeMismatch src)

  | .DropMemory src =>
    match Γ.getLocal src with
    | .uninit | .moved =>
      .error (.VIB001_UseAfterMoveOrUninit src)
    | .owned ty =>
      if isLinearResource ty then
        .error (.VIB006_LinearResourceViolation src)
      else if ty == .HeapClass false then
        if !Γ.noActiveBorrows src then
          .error (.VIB003_ConflictWithActiveBorrow src)
        else
          .ok (Γ.setLocal src .moved)
      else
        .error (.TypeMismatch src)
    | _ =>
      .error (.TypeMismatch src)

/-- Sequentially check a statement block. -/
def checkStmts (Γ : CheckState) : List Stmt → Except CheckError CheckState
  | []      => .ok Γ
  | s :: ss => do
    let Γ' ← checkStmt Γ s
    checkStmts Γ' ss

/-- Predicate identifying an unconsumed linear resource local (`VIB006`). -/
def isLinearState : LocalState → Bool
  | .owned ty => isLinearResource ty
  | _         => false

/-- Search finite local map keys for any unconsumed linear owner (`VIB006`). -/
def findLinearLeftover (Γ : CheckState) : List LocalId → Option LocalId
  | []      => none
  | k :: ks =>
    if isLinearState (Γ.getLocal k) then some k
    else findLinearLeftover Γ ks

theorem findLinearLeftover_none_mem (Γ : CheckState) :
    ∀ (ks : List LocalId), findLinearLeftover Γ ks = none →
      ∀ k ∈ ks, isLinearState (Γ.getLocal k) = false
  | [], _, _, hmem => nomatch hmem
  | k :: ks, hnone, x, hmem => by
    unfold findLinearLeftover at hnone
    cases hlin : isLinearState (Γ.getLocal k)
    · simp only [hlin, Bool.false_eq_true, ↓reduceIte] at hnone
      cases List.mem_cons.mp hmem with
      | inl heq => subst heq; exact hlin
      | inr hin => exact findLinearLeftover_none_mem Γ ks hnone x hin
    · simp [hlin] at hnone

theorem findLinearLeftover_none_all {Γ : CheckState}
    (h : findLinearLeftover Γ (EnvMap.keys Γ.localMap) = none) (l : LocalId) :
    isLinearState (Γ.getLocal l) = false := by
  by_cases hmem : l ∈ EnvMap.keys Γ.localMap
  · exact findLinearLeftover_none_mem Γ (EnvMap.keys Γ.localMap) h l hmem
  · have huninit : Γ.getLocal l = .uninit :=
      EnvMap.lookup_eq_default_of_not_mem LocalState.uninit Γ.localMap l hmem
    simp [huninit, isLinearState]

/-- Validate program return (`VIB006` linear exhaustion + `VIB007` no escaping borrow). -/
def checkReturn (Γ : CheckState) (ret : Option LocalId) : Except CheckError (Option Ty) :=
  match findLinearLeftover Γ (EnvMap.keys Γ.localMap) with
  | some leaked => .error (.VIB006_LinearResourceViolation leaked)
  | none =>
    match ret with
    | none => .ok none
    | some retLocal =>
      match Γ.getLocal retLocal with
      | .uninit | .moved =>
        .error (.VIB001_UseAfterMoveOrUninit retLocal)
      | .sharedLoan _ _ | .mutLoan _ _ =>
        .error (.VIB007_EscapingBorrow retLocal)
      | .owned retTy =>
        if isViewLike retTy then
          .error (.VIB007_EscapingBorrow retLocal)
        else if !Γ.noActiveBorrows retLocal then
          .error (.VIB003_ConflictWithActiveBorrow retLocal)
        else
          .ok (some retTy)

/-- Complete static verifier for an Vibranium MIR program. -/
def checkProg (p : Prog) : Except CheckError (Option Ty) := do
  let Γ ← checkStmts CheckState.empty p.stmts
  checkReturn Γ p.ret

end VibraniumFormal
