/-
  VibraniumFormal/Soundness.lean
  Machine-Checked Metatheory of Core Vibranium MIR (Zero `sorry`, Zero custom axioms):

  - **Theorem 1 (`aliasing_xor_mutability`):**
    In every reachable checked configuration, if an exclusive mutable borrow (`RefMut`)
    of heap address `a` is active at local `m`, then:
      1. `a` points to a `live` heap object,
      2. No other local `l ≠ m` holds an active mutable borrow of `a`,
      3. No local `l` holds an active shared borrow (`RefShared`) of `a`, and
      4. No local `l` is an accessible (unborrowed) owner path aliasing `a`.
    (Plus the dual `shared_borrow_immutability` theorem!)

  - **Theorem 2 (`native_memory_safety` — Progress, No UAF/DoubleFree & Exact-Once Linear Disposal):**
    Every well-checked Vibranium MIR program (`checkProg p = .ok retTy`) executing under
    LLVM native semantics (`evalNative p`):
      1. Terminates normally (`.ok run`) — **never** hitting `Error.UseAfterFree`,
         `Error.DoubleFree`, `Error.DoubleDispose`, `Error.UninitializedLocal`,
         `Error.InvalidAddress`, `Error.InvalidResource`, or `Error.TypeFault`; and
      2. Disposes every allocated linear `IDisposable` resource (`id < run.finalState.nextResId`)
         **exact-once** (`run.finalState.getRes id = some .disposedRes` and
         `disposedCount id run.outcome.trace = 1`).
-/

import VibraniumFormal.Syntax
import VibraniumFormal.Checker
import VibraniumFormal.Semantics

namespace VibraniumFormal

/-! ### Self-Contained Duplicate-Free Borrower List Helpers -/

/-- Remove the first occurrence of `x` from a `Nat` list. -/
def removeNat (x : Nat) : List Nat → List Nat
  | []      => []
  | y :: ys => if y = x then ys else y :: removeNat x ys

theorem mem_removeNat_of_nodup {x : Nat} :
    ∀ {bs : List Nat}, bs.Nodup → ∀ l, l ∈ removeNat x bs ↔ (l ∈ bs ∧ l ≠ x)
  | [], _, l => by simp [removeNat]
  | y :: ys, hnd, l => by
    rw [List.nodup_cons] at hnd
    rcases hnd with ⟨hnotmem, hnd_ys⟩
    unfold removeNat
    by_cases hyx : y = x
    · subst y
      simp only [↓reduceIte, List.mem_cons]
      constructor
      · intro hl
        refine ⟨Or.inr hl, ?_⟩
        intro hly; subst l; exact hnotmem hl
      · rintro ⟨hly | hl, hne⟩
        · exact absurd hly hne
        · exact hl
    · simp only [hyx, ↓reduceIte, List.mem_cons]
      rw [mem_removeNat_of_nodup hnd_ys l]
      constructor
      · rintro (hly | ⟨hl, hne⟩)
        · subst l; exact ⟨Or.inl rfl, hyx⟩
        · exact ⟨Or.inr hl, hne⟩
      · rintro ⟨hly | hl, hne⟩
        · exact Or.inl hly
        · exact Or.inr ⟨hl, hne⟩

theorem nodup_removeNat {x : Nat} :
    ∀ {bs : List Nat}, bs.Nodup → (removeNat x bs).Nodup
  | [], hnd => hnd
  | y :: ys, hnd => by
    rw [List.nodup_cons] at hnd
    rcases hnd with ⟨hnotmem, hnd_ys⟩
    unfold removeNat
    by_cases hyx : y = x
    · simp [hyx, hnd_ys]
    · simp only [hyx, ↓reduceIte, List.nodup_cons]
      refine ⟨?_, nodup_removeNat hnd_ys⟩
      intro hmem
      have ⟨hin, _⟩ := (mem_removeNat_of_nodup hnd_ys y).mp hmem
      exact hnotmem hin

theorem length_removeNat_of_mem {x : Nat} :
    ∀ {bs : List Nat}, x ∈ bs → (removeNat x bs).length = bs.length - 1 ∧ 1 ≤ bs.length
  | [], hmem => nomatch hmem
  | y :: ys, hmem => by
    unfold removeNat
    by_cases hyx : y = x
    · simp [hyx]
    · cases List.mem_cons.mp hmem with
      | inl hxy => exact absurd hxy.symm hyx
      | inr hin =>
        have ⟨hlen, _⟩ := length_removeNat_of_mem hin
        simp only [hyx, ↓reduceIte, List.length_cons]
        omega

/-! ### Value Typing & Machine Configuration Invariant (`WfConfig`) -/

/-- Well-typedness of a runtime value `v` owned at type `ty` in machine state `σ`. -/
def ValMatchesOwned (σ : ExecState) : Ty → Val → Prop
  | .CopyPrim .int,   v => ∃ n, v = .int n
  | .CopyPrim .bool,  v => ∃ b, v = .bool b
  | .OptionTy _,      v => v = .optNone
  | .HeapClass false, v =>
      ∃ a payload, a < σ.nextAddr ∧ v = .heapRef a ∧
        σ.getHeap a = some (.live { payload := payload, resId := none })
  | .HeapClass true,  v =>
      ∃ a payload id, a < σ.nextAddr ∧ id < σ.nextResId ∧ v = .heapRef a ∧
        σ.getHeap a = some (.live { payload := payload, resId := some id }) ∧
        σ.getRes id = some .openRes ∧
        disposedCount id σ.trace = 0
  | .RefShared _,     _ => False
  | .RefMut _,        _ => False

theorem ValMatchesOwned.heapRef_is_heapClass {σ : ExecState} {ty : Ty} {a : Addr}
    (h : ValMatchesOwned σ ty (.heapRef a)) :
    ∃ disp, ty = .HeapClass disp := by
  cases ty with
  | CopyPrim p => cases p <;> rcases h with ⟨_, h⟩ <;> cases h
  | OptionTy _ => cases h
  | HeapClass d => exact ⟨d, rfl⟩
  | RefShared _ => exact False.elim h
  | RefMut _ => exact False.elim h

theorem ValMatchesOwned.heapClass_live {σ : ExecState} {disp : Bool} {v : Val}
    (h : ValMatchesOwned σ (.HeapClass disp) v) :
    ∃ a obj, a < σ.nextAddr ∧ v = .heapRef a ∧ σ.getHeap a = some (.live obj) := by
  cases disp with
  | false =>
    rcases h with ⟨a, p, ha, hv, hh⟩
    exact ⟨a, { payload := p, resId := none }, ha, hv, hh⟩
  | true =>
    rcases h with ⟨a, p, id, ha, _, hv, hh, _, _⟩
    exact ⟨a, { payload := p, resId := some id }, ha, hv, hh⟩

theorem ValMatchesOwned.of_nonHeap {σ σ' : ExecState} {ty : Ty} {v : Val}
    (h : ValMatchesOwned σ ty v)
    (hno : ∀ d, ty ≠ .HeapClass d) :
    ValMatchesOwned σ' ty v := by
  cases ty with
  | CopyPrim p => cases p <;> exact h
  | OptionTy _ => exact h
  | HeapClass d => exact absurd rfl (hno d)
  | RefShared _ => exact False.elim h
  | RefMut _ => exact False.elim h

theorem ValMatchesOwned.of_copy {σ σ' : ExecState} {ty : Ty} {v : Val}
    (h : ValMatchesOwned σ ty v) (hc : isCopy ty = true) :
    ValMatchesOwned σ' ty v := by
  apply ValMatchesOwned.of_nonHeap h
  intro d hd; subst ty; simp at hc

theorem ValMatchesOwned.setLocal_irrel {σ : ExecState} {ty : Ty} {v : Val}
    (l : LocalId) (ov : Option Val) (h : ValMatchesOwned σ ty v) :
    ValMatchesOwned (σ.setLocal l ov) ty v := by
  cases ty with
  | CopyPrim p => cases p <;> exact h
  | OptionTy _ => exact h
  | HeapClass d => cases d <;> exact h
  | RefShared _ => exact False.elim h
  | RefMut _ => exact False.elim h

/-- Global Static-Dynamic Coupling & Ownership/Borrow/Resource Invariant. -/
structure WfConfig (Γ : CheckState) (σ : ExecState) : Prop where
  ownedValid :
    ∀ (l : LocalId) (ty : Ty),
      Γ.getLocal l = .owned ty →
      ∃ v, σ.getLocal l = some v ∧ ValMatchesOwned σ ty v

  ownerUnique :
    ∀ (l₁ l₂ : LocalId) (d₁ d₂ : Bool) (a : Addr),
      Γ.getLocal l₁ = .owned (.HeapClass d₁) →
      Γ.getLocal l₂ = .owned (.HeapClass d₂) →
      σ.getLocal l₁ = some (.heapRef a) →
      σ.getLocal l₂ = some (.heapRef a) →
      l₁ = l₂

  sharedLoanValid :
    ∀ (l root : LocalId) (ty : Ty),
      Γ.getLocal l = .sharedLoan root ty →
      ∃ disp a, ty = .RefShared (.HeapClass disp) ∧
        Γ.getLocal root = .owned (.HeapClass disp) ∧
        σ.getLocal l = some (.heapRef a) ∧
        σ.getLocal root = some (.heapRef a)

  mutLoanValid :
    ∀ (l root : LocalId) (ty : Ty),
      Γ.getLocal l = .mutLoan root ty →
      ∃ disp a, ty = .RefMut (.HeapClass disp) ∧
        Γ.getLocal root = .owned (.HeapClass disp) ∧
        σ.getLocal l = some (.heapRef a) ∧
        σ.getLocal root = some (.heapRef a)

  sharedCountExact :
    ∀ (root : LocalId),
      ∃ (bs : List LocalId), bs.Nodup ∧
        (∀ l, l ∈ bs ↔ ∃ ty, Γ.getLocal l = .sharedLoan root ty) ∧
        Γ.sharedCount root = bs.length

  exclLoanExact :
    ∀ (root : LocalId),
      (Γ.hasExclusiveLoan root = false → ∀ l ty, Γ.getLocal l ≠ .mutLoan root ty) ∧
      (Γ.hasExclusiveLoan root = true →
        Γ.sharedCount root = 0 ∧
        ∃ m ty, Γ.getLocal m = .mutLoan root ty ∧
          ∀ l ty', Γ.getLocal l = .mutLoan root ty' → l = m)

  heapAddrBound :
    ∀ (a : Addr), a ≥ σ.nextAddr → σ.getHeap a = none

  heapResBound :
    ∀ (a : Addr) (p : Int) (id : ResourceId),
      σ.getHeap a = some (.live { payload := p, resId := some id }) →
      id < σ.nextResId

  heapResInj :
    ∀ (a₁ a₂ : Addr) (p₁ p₂ : Int) (id : ResourceId),
      σ.getHeap a₁ = some (.live { payload := p₁, resId := some id }) →
      σ.getHeap a₂ = some (.live { payload := p₂, resId := some id }) →
      a₁ = a₂

  traceBound :
    ∀ (id : ResourceId), id ≥ σ.nextResId → disposedCount id σ.trace = 0

  resAccounting :
    ∀ (id : ResourceId), id < σ.nextResId →
      ((σ.getRes id = some .openRes ∧ disposedCount id σ.trace = 0 ∧
        ∃ l a payload,
          Γ.getLocal l = .owned (.HeapClass true) ∧
          σ.getLocal l = some (.heapRef a) ∧
          σ.getHeap a = some (.live { payload := payload, resId := some id })) ∨
       (σ.getRes id = some .disposedRes ∧ disposedCount id σ.trace = 1))

/-! ### Initial Configuration Well-Formedness & Structural Consequences -/

theorem wfConfig_empty : WfConfig CheckState.empty ExecState.empty where
  ownedValid l ty h := by simp at h
  ownerUnique l₁ _ _ _ _ h₁ _ _ _ := by simp at h₁
  sharedLoanValid l _ _ h := by simp at h
  mutLoanValid l _ _ h := by simp at h
  sharedCountExact root := ⟨[], List.nodup_nil, fun l => by simp, rfl⟩
  exclLoanExact root := ⟨fun _ l ty h => by simp at h, fun h => by simp at h⟩
  heapAddrBound _ _ := rfl
  heapResBound _ _ _ h := by simp at h
  heapResInj _ _ _ _ _ h₁ _ := by simp at h₁
  traceBound _ _ := rfl
  resAccounting _ h := nomatch h

theorem WfConfig.no_sharedLoan_of_zero {Γ : CheckState} {σ : ExecState}
    (hwf : WfConfig Γ σ) {root : LocalId} (hz : Γ.sharedCount root = 0) :
    ∀ l ty, Γ.getLocal l ≠ .sharedLoan root ty := by
  rcases hwf.sharedCountExact root with ⟨bs, _, hiff, hlen⟩
  intro l ty hloc
  have hmem : l ∈ bs := (hiff l).mpr ⟨ty, hloc⟩
  cases bs with
  | nil => cases hmem
  | cons _ _ => simp [hz] at hlen

theorem WfConfig.sharedCount_pos_of_sharedLoan {Γ : CheckState} {σ : ExecState}
    (hwf : WfConfig Γ σ) {l root : LocalId} {ty : Ty}
    (hloc : Γ.getLocal l = .sharedLoan root ty) :
    1 ≤ Γ.sharedCount root := by
  rcases hwf.sharedCountExact root with ⟨bs, _, hiff, hlen⟩
  have hmem : l ∈ bs := (hiff l).mpr ⟨ty, hloc⟩
  cases bs with
  | nil => cases hmem
  | cons _ _ => simp [hlen]

theorem WfConfig.hasExcl_of_mutLoan {Γ : CheckState} {σ : ExecState}
    (hwf : WfConfig Γ σ) {l root : LocalId} {ty : Ty}
    (hloc : Γ.getLocal l = .mutLoan root ty) :
    Γ.hasExclusiveLoan root = true := by
  cases hex : Γ.hasExclusiveLoan root
  · exfalso; exact (hwf.exclLoanExact root).1 hex l ty hloc
  · rfl

theorem checkWriteTarget_ne_heapOwner {Γ : CheckState} {dst l : LocalId} {disp : Bool}
    (hw : checkWriteTarget Γ dst = .ok ())
    (hl : Γ.getLocal l = .owned (.HeapClass disp)) :
    dst ≠ l := by
  have ⟨_, _, _, _, hown⟩ := checkWriteTarget_ok_props hw
  intro heq; subst l
  have hc := hown (.HeapClass disp) hl
  simp at hc

theorem checkWriteTarget_ne_sharedLoan {Γ : CheckState} {dst l root : LocalId} {ty : Ty}
    (hw : checkWriteTarget Γ dst = .ok ())
    (hl : Γ.getLocal l = .sharedLoan root ty) :
    dst ≠ l := by
  have ⟨_, _, hns, _, _⟩ := checkWriteTarget_ok_props hw
  intro heq; subst l
  exact hns root ty hl

theorem checkWriteTarget_ne_mutLoan {Γ : CheckState} {dst l root : LocalId} {ty : Ty}
    (hw : checkWriteTarget Γ dst = .ok ())
    (hl : Γ.getLocal l = .mutLoan root ty) :
    dst ≠ l := by
  have ⟨_, _, _, hnm, _⟩ := checkWriteTarget_ok_props hw
  intro heq; subst l
  exact hnm root ty hl

/-! ### Helper Lemma: Writing a Non-Heap Owned Value Preserves `WfConfig` -/

theorem wf_write_nonHeapOwned {Γ : CheckState} {σ : ExecState}
    (hwf : WfConfig Γ σ) {dst : LocalId} {ty : Ty} {v : Val}
    (hw : checkWriteTarget Γ dst = .ok ())
    (hnoHeap : ∀ d, ty ≠ .HeapClass d)
    (hv : ValMatchesOwned σ ty v) :
    WfConfig (Γ.setLocal dst (.owned ty)) (σ.setLocal dst (some v)) := by
  refine
    { ownedValid := ?_
      ownerUnique := ?_
      sharedLoanValid := ?_
      mutLoanValid := ?_
      sharedCountExact := ?_
      exclLoanExact := ?_
      heapAddrBound := hwf.heapAddrBound
      heapResBound := hwf.heapResBound
      heapResInj := hwf.heapResInj
      traceBound := hwf.traceBound
      resAccounting := ?_ }
  · intro l lty hl
    by_cases hld : l = dst
    · subst l
      simp only [CheckState.getLocal_setLocal_eq, LocalState.owned.injEq] at hl
      subst lty
      exact ⟨v, by simp, ValMatchesOwned.setLocal_irrel dst (some v) hv⟩
    · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
      rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvm⟩
      exact ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hld, hlv],
        ValMatchesOwned.setLocal_irrel dst (some v) hvm⟩
  · intro l₁ l₂ d₁ d₂ a hl₁ hl₂ hv₁ hv₂
    have hne₁ : l₁ ≠ dst := by
      intro heq; subst l₁; simp at hl₁; exact hnoHeap d₁ hl₁
    have hne₂ : l₂ ≠ dst := by
      intro heq; subst l₂; simp at hl₂; exact hnoHeap d₂ hl₂
    rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
    rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
    rw [ExecState.getLocal_setLocal_ne _ _ hne₁] at hv₁
    rw [ExecState.getLocal_setLocal_ne _ _ hne₂] at hv₂
    exact hwf.ownerUnique l₁ l₂ d₁ d₂ a hl₁ hl₂ hv₁ hv₂
  · intro l root lty hl
    have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
    rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
    rcases hwf.sharedLoanValid l root lty hl with ⟨disp, a, hty, hroot, hvl, hvr⟩
    have hne_r : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
    refine ⟨disp, a, hty, ?_, ?_, ?_⟩
    · simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hroot]
    · simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl]
    · simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]
  · intro l root lty hl
    have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
    rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
    rcases hwf.mutLoanValid l root lty hl with ⟨disp, a, hty, hroot, hvl, hvr⟩
    have hne_r : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
    refine ⟨disp, a, hty, ?_, ?_, ?_⟩
    · simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hroot]
    · simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl]
    · simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]
  · intro root
    rcases hwf.sharedCountExact root with ⟨bs, hnd, hiff, hlen⟩
    refine ⟨bs, hnd, ?_, by simp [hlen]⟩
    intro l
    rw [hiff l]
    constructor
    · rintro ⟨lty, hl⟩
      have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
      exact ⟨lty, by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
    · rintro ⟨lty, hl⟩
      have hne : l ≠ dst := by intro heq; subst l; simp at hl
      rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
      exact ⟨lty, hl⟩
  · intro root
    rcases hwf.exclLoanExact root with ⟨hfalse, htrue⟩
    constructor
    · intro hex l lty hl
      have hne : l ≠ dst := by intro heq; subst l; simp at hl
      rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
      exact hfalse hex l lty hl
    · intro hex
      rcases htrue hex with ⟨hsc, m, mty, hm, huniq⟩
      have hne_m : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
      refine ⟨hsc, m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
      intro l lty' hl
      have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
      rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
      exact huniq l lty' hl
  · intro id hid
    cases hwf.resAccounting id hid with
    | inl hopen =>
      rcases hopen with ⟨hres, hcnt, l, a, p, hl, hvl, hheap⟩
      have hne : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
      exact Or.inl ⟨hres, hcnt, l, a, p,
        by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl],
        by simp [ExecState.getLocal_setLocal_ne _ _ hne, hvl],
        hheap⟩
    | inr hdisp => exact Or.inr hdisp

/-! ### Single-Step Progress & Preservation (`stepStmt` under both `.native` & `.managed`) -/

theorem stepStmt_sound (mode : ExecMode) {Γ Γ' : CheckState} {σ : ExecState} {s : Stmt}
    (hwf : WfConfig Γ σ)
    (hchk : checkStmt Γ s = .ok Γ') :
    ∃ σ', stepStmt mode σ s = .ok σ' ∧ WfConfig Γ' σ' := by
  cases s with
  | ConstPrim dst val =>
    unfold checkStmt at hchk; dsimp only at hchk
    cases hw : checkWriteTarget Γ dst with
    | error e => rw [hw] at hchk; cases hchk
    | ok _ =>
      rw [hw] at hchk; cases hchk
      refine ⟨σ.setLocal dst (some (Val.ofPrim val)), rfl, ?_⟩
      apply wf_write_nonHeapOwned hwf hw (fun d h => by cases h)
      cases val with
      | int n => exact ⟨n, rfl⟩
      | bool b => exact ⟨b, rfl⟩

  | ZeroInit dst ty =>
    unfold checkStmt at hchk
    cases hzs : isZeroSafe ty
    · simp [hzs] at hchk
    · simp only [hzs, Bool.not_true, Bool.false_eq_true, ↓reduceIte] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ =>
        rw [hw] at hchk; cases hchk
        rcases zeroVal_of_isZeroSafe ty hzs with ⟨zv, hzv⟩
        refine ⟨σ.setLocal dst (some zv), by simp [stepStmt, hzv], ?_⟩
        apply wf_write_nonHeapOwned hwf hw (fun d hd => by subst ty; simp [isZeroSafe] at hzs)
        cases ty with
        | CopyPrim p =>
          cases p <;> simp [zeroVal] at hzv <;> subst zv
          · exact ⟨0, rfl⟩
          · exact ⟨false, rfl⟩
        | OptionTy _ => simp [zeroVal] at hzv; exact hzv.symm
        | HeapClass _ => simp [isZeroSafe] at hzs
        | RefShared _ => simp [isZeroSafe] at hzs
        | RefMut _ => simp [isZeroSafe] at hzs

  | Alloc dst disp initVal =>
    unfold checkStmt at hchk; dsimp only at hchk
    cases hw : checkWriteTarget Γ dst with
    | error e => rw [hw] at hchk; cases hchk
    | ok _ =>
      rw [hw] at hchk; cases hchk
      cases disp with
      | false =>
        refine ⟨(σ.bumpAddr.setHeap σ.nextAddr
            (some (.live { payload := initVal, resId := none }))).setLocal
            dst (some (.heapRef σ.nextAddr)),
          by simp [stepStmt], ?_⟩
        refine
          { ownedValid := ?_
            ownerUnique := ?_
            sharedLoanValid := ?_
            mutLoanValid := ?_
            sharedCountExact := ?_
            exclLoanExact := ?_
            heapAddrBound := ?_
            heapResBound := ?_
            heapResInj := ?_
            traceBound := hwf.traceBound
            resAccounting := ?_ }
        · intro l lty hl
          by_cases hld : l = dst
          · subst l
            simp only [CheckState.getLocal_setLocal_eq, LocalState.owned.injEq] at hl
            subst lty
            exact ⟨.heapRef σ.nextAddr, by simp,
              ⟨σ.nextAddr, initVal, by simp, rfl, by simp⟩⟩
          · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
            rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvm⟩
            refine ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hld, hlv], ?_⟩
            cases lty with
            | CopyPrim p => exact ValMatchesOwned.of_copy hvm rfl
            | OptionTy _ => exact hvm
            | RefShared _ => exact False.elim hvm
            | RefMut _ => exact False.elim hvm
            | HeapClass d =>
              cases d with
              | false =>
                rcases hvm with ⟨al, pl, hal, hveq, hhl⟩
                have hnea : al ≠ σ.nextAddr := Nat.ne_of_lt hal
                exact ⟨al, pl, by simp; exact Nat.lt_succ_of_lt hal, hveq,
                  by simp [ExecState.getHeap_setHeap_ne _ _ hnea, hhl]⟩
              | true =>
                rcases hvm with ⟨al, pl, idl, hal, hidl, hveq, hhl, hrl, hcl⟩
                have hnea : al ≠ σ.nextAddr := Nat.ne_of_lt hal
                exact ⟨al, pl, idl, by simp; exact Nat.lt_succ_of_lt hal, hidl, hveq,
                  by simp [ExecState.getHeap_setHeap_ne _ _ hnea, hhl], hrl, hcl⟩
        · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
          by_cases h1 : l₁ = dst
          · subst l₁
            simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq, Val.heapRef.injEq] at hv₁
            subst a₀
            by_cases h2 : l₂ = dst
            · exact h2.symm
            · exfalso
              rw [CheckState.getLocal_setLocal_ne _ _ h2] at hl₂
              simp only [ExecState.getLocal_setLocal_ne _ _ h2,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddr] at hv₂
              rcases hwf.ownedValid l₂ (.HeapClass d₂) hl₂ with ⟨v₂, hv₂', hvm₂⟩
              rcases ValMatchesOwned.heapClass_live hvm₂ with ⟨al, _, hal, rfl, _⟩
              rw [hv₂] at hv₂'; injection hv₂' with hv₂'; injection hv₂' with haeq
              exact Nat.ne_of_lt hal haeq.symm
          · by_cases h2 : l₂ = dst
            · subst l₂
              exfalso
              simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq, Val.heapRef.injEq] at hv₂
              subst a₀
              rw [CheckState.getLocal_setLocal_ne _ _ h1] at hl₁
              simp only [ExecState.getLocal_setLocal_ne _ _ h1,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddr] at hv₁
              rcases hwf.ownedValid l₁ (.HeapClass d₁) hl₁ with ⟨v₁, hv₁', hvm₁⟩
              rcases ValMatchesOwned.heapClass_live hvm₁ with ⟨al, _, hal, rfl, _⟩
              rw [hv₁] at hv₁'; injection hv₁' with hv₁'; injection hv₁' with haeq
              exact Nat.ne_of_lt hal haeq.symm
            · rw [CheckState.getLocal_setLocal_ne _ _ h1] at hl₁
              rw [CheckState.getLocal_setLocal_ne _ _ h2] at hl₂
              simp only [ExecState.getLocal_setLocal_ne _ _ h1,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddr] at hv₁
              simp only [ExecState.getLocal_setLocal_ne _ _ h2,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddr] at hv₂
              exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
        · intro l root lty hl
          have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
          rcases hwf.sharedLoanValid l root lty hl with ⟨d, ar, hty, hroot, hvl, hvr⟩
          have hne_r : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
          exact ⟨d, ar, hty,
            by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hroot],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
        · intro l root lty hl
          have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
          rcases hwf.mutLoanValid l root lty hl with ⟨d, ar, hty, hroot, hvl, hvr⟩
          have hne_r : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
          exact ⟨d, ar, hty,
            by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hroot],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
        · intro root
          rcases hwf.sharedCountExact root with ⟨bs, hnd, hiff, hlen⟩
          refine ⟨bs, hnd, ?_, by simp [hlen]⟩
          intro l; rw [hiff l]
          constructor
          · rintro ⟨lty, hl⟩
            have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
            exact ⟨lty, by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
          · rintro ⟨lty, hl⟩
            have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact ⟨lty, hl⟩
        · intro root
          rcases hwf.exclLoanExact root with ⟨hf, ht⟩
          refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
          · have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact hf hex l lty hl
          · rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
            have hne_m : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
            refine ⟨hsc, m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
            intro l lty' hl
            have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
            exact huniq l lty' hl
        · intro a' ha'
          simp only [ExecState.nextAddr_setLocal, ExecState.nextAddr_setHeap,
            ExecState.nextAddr_bumpAddr] at ha'
          have hne : a' ≠ σ.nextAddr := Nat.ne_of_gt ha'
          have hge : a' ≥ σ.nextAddr := Nat.le_of_lt ha'
          simp [ExecState.getHeap_setHeap_ne _ _ hne, hwf.heapAddrBound a' hge]
        · intro a' p' id' hh'
          by_cases ha : a' = σ.nextAddr
          · subst a'; simp at hh'
          · simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setHeap_ne _ _ ha,
              ExecState.getHeap_bumpAddr] at hh'
            exact hwf.heapResBound a' p' id' hh'
        · intro a₁ a₂ p₁ p₂ id' hh₁ hh₂
          have hn₁ : a₁ ≠ σ.nextAddr := by intro heq; subst a₁; simp at hh₁
          have hn₂ : a₂ ≠ σ.nextAddr := by intro heq; subst a₂; simp at hh₂
          simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setHeap_ne _ _ hn₁,
            ExecState.getHeap_bumpAddr] at hh₁
          simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setHeap_ne _ _ hn₂,
            ExecState.getHeap_bumpAddr] at hh₂
          exact hwf.heapResInj a₁ a₂ p₁ p₂ id' hh₁ hh₂
        · intro id hid
          simp only [ExecState.nextResId_setLocal, ExecState.nextResId_setHeap,
            ExecState.nextResId_bumpAddr] at hid
          cases hwf.resAccounting id hid with
          | inl hopen =>
            rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
            have hne_l : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
            have hne_a : al ≠ σ.nextAddr := by
              intro haeq; subst al
              have hnone := hwf.heapAddrBound σ.nextAddr (Nat.le_refl _)
              rw [hnone] at hhl; cases hhl
            exact Or.inl ⟨by simp [hres], by simp [hcnt], l, al, pl,
              by simp [CheckState.getLocal_setLocal_ne _ _ hne_l, hl],
              by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
              by simp [ExecState.getHeap_setHeap_ne _ _ hne_a, hhl]⟩
          | inr hdisp =>
            rcases hdisp with ⟨hres, hcnt⟩
            exact Or.inr ⟨by simp [hres], by simp [hcnt]⟩

      | true =>
        refine ⟨(((σ.bumpAddrAndRes.setHeap σ.nextAddr
            (some (.live { payload := initVal, resId := some σ.nextResId }))).setRes
            σ.nextResId (some .openRes)).setLocal dst (some (.heapRef σ.nextAddr))),
          by simp [stepStmt], ?_⟩
        refine
          { ownedValid := ?_
            ownerUnique := ?_
            sharedLoanValid := ?_
            mutLoanValid := ?_
            sharedCountExact := ?_
            exclLoanExact := ?_
            heapAddrBound := ?_
            heapResBound := ?_
            heapResInj := ?_
            traceBound := ?_
            resAccounting := ?_ }
        · intro l lty hl
          by_cases hld : l = dst
          · subst l
            simp only [CheckState.getLocal_setLocal_eq, LocalState.owned.injEq] at hl
            subst lty
            exact ⟨.heapRef σ.nextAddr, by simp,
              ⟨σ.nextAddr, initVal, σ.nextResId, by simp, by simp, rfl,
                by simp, by simp, hwf.traceBound σ.nextResId (Nat.le_refl _)⟩⟩
          · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
            rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvm⟩
            refine ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hld, hlv], ?_⟩
            cases lty with
            | CopyPrim p => exact ValMatchesOwned.of_copy hvm rfl
            | OptionTy _ => exact hvm
            | RefShared _ => exact False.elim hvm
            | RefMut _ => exact False.elim hvm
            | HeapClass d =>
              cases d with
              | false =>
                rcases hvm with ⟨al, pl, hal, hveq, hhl⟩
                have hnea : al ≠ σ.nextAddr := Nat.ne_of_lt hal
                exact ⟨al, pl, by simp; exact Nat.lt_succ_of_lt hal, hveq,
                  by simp [ExecState.getHeap_setHeap_ne _ _ hnea, hhl]⟩
              | true =>
                rcases hvm with ⟨al, pl, idl, hal, hidl, hveq, hhl, hrl, hcl⟩
                have hnea : al ≠ σ.nextAddr := Nat.ne_of_lt hal
                have hneid : idl ≠ σ.nextResId := Nat.ne_of_lt hidl
                exact ⟨al, pl, idl,
                  by simp; exact Nat.lt_succ_of_lt hal,
                  by simp; exact Nat.lt_succ_of_lt hidl,
                  hveq,
                  by simp [ExecState.getHeap_setHeap_ne _ _ hnea, hhl],
                  by simp [ExecState.getRes_setRes_ne _ _ hneid, hrl],
                  by simp [hcl]⟩
        · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
          by_cases h1 : l₁ = dst
          · subst l₁
            simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq, Val.heapRef.injEq] at hv₁
            subst a₀
            by_cases h2 : l₂ = dst
            · exact h2.symm
            · exfalso
              rw [CheckState.getLocal_setLocal_ne _ _ h2] at hl₂
              simp only [ExecState.getLocal_setLocal_ne _ _ h2, ExecState.getLocal_setRes,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddrAndRes] at hv₂
              rcases hwf.ownedValid l₂ (.HeapClass d₂) hl₂ with ⟨v₂, hv₂', hvm₂⟩
              rcases ValMatchesOwned.heapClass_live hvm₂ with ⟨al, _, hal, rfl, _⟩
              rw [hv₂] at hv₂'; injection hv₂' with hv₂'; injection hv₂' with haeq
              exact Nat.ne_of_lt hal haeq.symm
          · by_cases h2 : l₂ = dst
            · subst l₂
              exfalso
              simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq, Val.heapRef.injEq] at hv₂
              subst a₀
              rw [CheckState.getLocal_setLocal_ne _ _ h1] at hl₁
              simp only [ExecState.getLocal_setLocal_ne _ _ h1, ExecState.getLocal_setRes,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddrAndRes] at hv₁
              rcases hwf.ownedValid l₁ (.HeapClass d₁) hl₁ with ⟨v₁, hv₁', hvm₁⟩
              rcases ValMatchesOwned.heapClass_live hvm₁ with ⟨al, _, hal, rfl, _⟩
              rw [hv₁] at hv₁'; injection hv₁' with hv₁'; injection hv₁' with haeq
              exact Nat.ne_of_lt hal haeq.symm
            · rw [CheckState.getLocal_setLocal_ne _ _ h1] at hl₁
              rw [CheckState.getLocal_setLocal_ne _ _ h2] at hl₂
              simp only [ExecState.getLocal_setLocal_ne _ _ h1, ExecState.getLocal_setRes,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddrAndRes] at hv₁
              simp only [ExecState.getLocal_setLocal_ne _ _ h2, ExecState.getLocal_setRes,
                ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddrAndRes] at hv₂
              exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
        · intro l root lty hl
          have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
          rcases hwf.sharedLoanValid l root lty hl with ⟨d, ar, hty, hroot, hvl, hvr⟩
          have hne_r : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
          exact ⟨d, ar, hty,
            by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hroot],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
        · intro l root lty hl
          have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
          rcases hwf.mutLoanValid l root lty hl with ⟨d, ar, hty, hroot, hvl, hvr⟩
          have hne_r : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
          exact ⟨d, ar, hty,
            by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hroot],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
        · intro root
          rcases hwf.sharedCountExact root with ⟨bs, hnd, hiff, hlen⟩
          refine ⟨bs, hnd, ?_, by simp [hlen]⟩
          intro l; rw [hiff l]
          constructor
          · rintro ⟨lty, hl⟩
            have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
            exact ⟨lty, by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
          · rintro ⟨lty, hl⟩
            have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact ⟨lty, hl⟩
        · intro root
          rcases hwf.exclLoanExact root with ⟨hf, ht⟩
          refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
          · have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact hf hex l lty hl
          · rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
            have hne_m : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
            refine ⟨hsc, m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
            intro l lty' hl
            have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
            exact huniq l lty' hl
        · intro a' ha'
          simp only [ExecState.nextAddr_setLocal, ExecState.nextAddr_setRes,
            ExecState.nextAddr_setHeap, ExecState.nextAddr_bumpAddrAndRes] at ha'
          have hne : a' ≠ σ.nextAddr := Nat.ne_of_gt ha'
          have hge : a' ≥ σ.nextAddr := Nat.le_of_lt ha'
          simp [ExecState.getHeap_setHeap_ne _ _ hne, hwf.heapAddrBound a' hge]
        · intro a' p' id' hh'
          by_cases ha : a' = σ.nextAddr
          · subst a'
            simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
              ExecState.getHeap_setHeap_eq, Option.some.injEq, HeapCell.live.injEq,
              HeapObj.mk.injEq] at hh'
            rcases hh' with ⟨_, hid'⟩
            subst id'
            simp
          · simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
              ExecState.getHeap_setHeap_ne _ _ ha, ExecState.getHeap_bumpAddrAndRes] at hh'
            simp only [ExecState.nextResId_setLocal, ExecState.nextResId_setRes,
              ExecState.nextResId_setHeap, ExecState.nextResId_bumpAddrAndRes]
            exact Nat.lt_succ_of_lt (hwf.heapResBound a' p' id' hh')
        · intro a₁ a₂ p₁ p₂ id' hh₁ hh₂
          by_cases ha₁ : a₁ = σ.nextAddr
          · subst a₁
            simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
              ExecState.getHeap_setHeap_eq, Option.some.injEq, HeapCell.live.injEq,
              HeapObj.mk.injEq] at hh₁
            rcases hh₁ with ⟨_, hid₁⟩
            subst id'
            by_cases ha₂ : a₂ = σ.nextAddr
            · exact ha₂.symm
            · exfalso
              simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
                ExecState.getHeap_setHeap_ne _ _ ha₂, ExecState.getHeap_bumpAddrAndRes] at hh₂
              exact Nat.ne_of_lt (hwf.heapResBound a₂ p₂ σ.nextResId hh₂) rfl
          · by_cases ha₂ : a₂ = σ.nextAddr
            · subst a₂
              exfalso
              simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
                ExecState.getHeap_setHeap_eq, Option.some.injEq, HeapCell.live.injEq,
                HeapObj.mk.injEq] at hh₂
              rcases hh₂ with ⟨_, hid₂⟩
              subst id'
              simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
                ExecState.getHeap_setHeap_ne _ _ ha₁, ExecState.getHeap_bumpAddrAndRes] at hh₁
              exact Nat.ne_of_lt (hwf.heapResBound a₁ p₁ σ.nextResId hh₁) rfl
            · simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
                ExecState.getHeap_setHeap_ne _ _ ha₁, ExecState.getHeap_bumpAddrAndRes] at hh₁
              simp only [ExecState.getHeap_setLocal, ExecState.getHeap_setRes,
                ExecState.getHeap_setHeap_ne _ _ ha₂, ExecState.getHeap_bumpAddrAndRes] at hh₂
              exact hwf.heapResInj a₁ a₂ p₁ p₂ id' hh₁ hh₂
        · intro id hid
          simp only [ExecState.nextResId_setLocal, ExecState.nextResId_setRes,
            ExecState.nextResId_setHeap, ExecState.nextResId_bumpAddrAndRes] at hid
          simp [hwf.traceBound id (Nat.le_of_lt hid)]
        · intro id hid
          simp only [ExecState.nextResId_setLocal, ExecState.nextResId_setRes,
            ExecState.nextResId_setHeap, ExecState.nextResId_bumpAddrAndRes] at hid
          by_cases heq : id = σ.nextResId
          · subst id
            exact Or.inl ⟨by simp, by simp [hwf.traceBound σ.nextResId (Nat.le_refl _)],
              dst, σ.nextAddr, initVal, by simp, by simp, by simp⟩
          · have hlt : id < σ.nextResId :=
              Nat.lt_of_le_of_ne (Nat.le_of_lt_succ hid) heq
            cases hwf.resAccounting id hlt with
            | inl hopen =>
              rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
              have hne_l : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
              have hne_a : al ≠ σ.nextAddr := by
                intro haeq; subst al
                have hnone := hwf.heapAddrBound σ.nextAddr (Nat.le_refl _)
                rw [hnone] at hhl; cases hhl
              exact Or.inl ⟨by simp [ExecState.getRes_setRes_ne _ _ heq, hres],
                by simp [hcnt], l, al, pl,
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_l, hl],
                by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
                by simp [ExecState.getHeap_setHeap_ne _ _ hne_a, hhl]⟩
            | inr hdisp =>
              rcases hdisp with ⟨hres, hcnt⟩
              exact Or.inr ⟨by simp [ExecState.getRes_setRes_ne _ _ heq, hres], by simp [hcnt]⟩

  | CopyOrMove dst src =>
    unfold checkStmt at hchk
    cases hsrc : Γ.getLocal src with
    | uninit => simp [hsrc] at hchk
    | moved => simp [hsrc] at hchk
    | owned ty =>
      rcases hwf.ownedValid src ty hsrc with ⟨v, hvsrc, hvm⟩
      cases hc : isCopy ty with
      | true =>
        simp only [hsrc, hc, ↓reduceIte] at hchk
        cases hw : checkWriteTarget Γ dst with
        | error e => rw [hw] at hchk; cases hchk
        | ok _ =>
          rw [hw] at hchk; cases hchk
          refine ⟨σ.setLocal dst (some v), by simp [stepStmt, hvsrc], ?_⟩
          exact wf_write_nonHeapOwned hwf hw (fun d hd => by subst ty; simp at hc) hvm
      | false =>
        simp only [hsrc, hc, Bool.false_eq_true, ↓reduceIte] at hchk
        cases hnb : Γ.noActiveBorrows src with
        | false => simp [hnb] at hchk
        | true =>
          simp only [hnb, Bool.not_true, Bool.false_eq_true, ↓reduceIte] at hchk
          cases hw : checkWriteTarget Γ dst with
          | error e => rw [hw] at hchk; cases hchk
          | ok _ =>
            rw [hw] at hchk; cases hchk
            have ⟨hsc_src, hex_src⟩ := (CheckState.noActiveBorrows_iff Γ src).mp hnb
            refine ⟨σ.setLocal dst (some v), by simp [stepStmt, hvsrc], ?_⟩
            refine
              { ownedValid := ?_
                ownerUnique := ?_
                sharedLoanValid := ?_
                mutLoanValid := ?_
                sharedCountExact := ?_
                exclLoanExact := ?_
                heapAddrBound := hwf.heapAddrBound
                heapResBound := hwf.heapResBound
                heapResInj := hwf.heapResInj
                traceBound := hwf.traceBound
                resAccounting := ?_ }
            · intro l lty hl
              by_cases hld : l = dst
              · subst l
                simp only [CheckState.getLocal_setLocal_eq, LocalState.owned.injEq] at hl
                subst lty
                exact ⟨v, by simp, ValMatchesOwned.setLocal_irrel dst (some v) hvm⟩
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                by_cases hls : l = src
                · subst l; simp at hl
                · rw [CheckState.getLocal_setLocal_ne _ _ hls] at hl
                  rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
                  exact ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hld, hlv],
                    ValMatchesOwned.setLocal_irrel dst (some v) hvml⟩
            · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
              have getOrig : ∀ l d,
                  ((Γ.setLocal src .moved).setLocal dst (.owned ty)).getLocal l = .owned (.HeapClass d) →
                  (σ.setLocal dst (some v)).getLocal l = some (.heapRef a₀) →
                  ∃ l', (if l = dst then l' = src else l' = l) ∧
                    Γ.getLocal l' = .owned (.HeapClass d) ∧
                    σ.getLocal l' = some (.heapRef a₀) := by
                intro l d hgl hvgl
                by_cases hld : l = dst
                · subst l
                  simp only [CheckState.getLocal_setLocal_eq, LocalState.owned.injEq] at hgl
                  subst ty
                  simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq] at hvgl
                  subst v
                  exact ⟨src, by simp, hsrc, hvsrc⟩
                · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hgl
                  by_cases hls : l = src
                  · subst l; simp at hgl
                  · rw [CheckState.getLocal_setLocal_ne _ _ hls] at hgl
                    rw [ExecState.getLocal_setLocal_ne _ _ hld] at hvgl
                    exact ⟨l, by simp [hld], hgl, hvgl⟩
              rcases getOrig l₁ d₁ hl₁ hv₁ with ⟨o₁, ho₁, hgo₁, hvo₁⟩
              rcases getOrig l₂ d₂ hl₂ hv₂ with ⟨o₂, ho₂, hgo₂, hvo₂⟩
              have hoeq := hwf.ownerUnique o₁ o₂ d₁ d₂ a₀ hgo₁ hgo₂ hvo₁ hvo₂
              subst o₂
              by_cases h1 : l₁ = dst <;> by_cases h2 : l₂ = dst
              · rw [h1, h2]
              · simp only [h1, ↓reduceIte] at ho₁
                simp only [h2, ↓reduceIte] at ho₂
                subst o₁; subst l₂
                rw [CheckState.getLocal_setLocal_ne _ _ h2] at hl₂
                simp at hl₂
              · simp only [h1, ↓reduceIte] at ho₁
                simp only [h2, ↓reduceIte] at ho₂
                subst o₁; subst l₁
                rw [CheckState.getLocal_setLocal_ne _ _ h1] at hl₁
                simp at hl₁
              · simp only [h1, ↓reduceIte] at ho₁
                simp only [h2, ↓reduceIte] at ho₂
                exact ho₁.symm.trans ho₂
            · intro l root lty hl
              have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
              have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
              rcases hwf.sharedLoanValid l root lty hl with ⟨disp, ar, hty, hroot, hvl, hvr⟩
              have hne_rs : root ≠ src := by
                intro heq; subst root
                exact hwf.no_sharedLoan_of_zero hsc_src l lty hl
              have hne_rd : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
              refine ⟨disp, ar, hty, ?_, ?_, ?_⟩
              · rw [CheckState.getLocal_setLocal_ne _ _ hne_rd,
                    CheckState.getLocal_setLocal_ne _ _ hne_rs]
                exact hroot
              · simp [ExecState.getLocal_setLocal_ne _ _ hne_ld, hvl]
              · simp [ExecState.getLocal_setLocal_ne _ _ hne_rd, hvr]
            · intro l root lty hl
              have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
              have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
              rcases hwf.mutLoanValid l root lty hl with ⟨disp, ar, hty, hroot, hvl, hvr⟩
              have hne_rs : root ≠ src := by
                intro heq; subst root
                exact (hwf.exclLoanExact src).1 hex_src l lty hl
              have hne_rd : root ≠ dst := (checkWriteTarget_ne_heapOwner hw hroot).symm
              refine ⟨disp, ar, hty, ?_, ?_, ?_⟩
              · rw [CheckState.getLocal_setLocal_ne _ _ hne_rd,
                    CheckState.getLocal_setLocal_ne _ _ hne_rs]
                exact hroot
              · simp [ExecState.getLocal_setLocal_ne _ _ hne_ld, hvl]
              · simp [ExecState.getLocal_setLocal_ne _ _ hne_rd, hvr]
            · intro root
              rcases hwf.sharedCountExact root with ⟨bs, hnd, hiff, hlen⟩
              refine ⟨bs, hnd, ?_, by simp [hlen]⟩
              intro l; rw [hiff l]
              constructor
              · rintro ⟨lty, hl⟩
                have hne_ld : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
                have hne_ls : l ≠ src := by intro heq; subst l; rw [hsrc] at hl; cases hl
                exact ⟨lty, by rw [CheckState.getLocal_setLocal_ne _ _ hne_ld,
                  CheckState.getLocal_setLocal_ne _ _ hne_ls]; exact hl⟩
              · rintro ⟨lty, hl⟩
                have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
                have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
                exact ⟨lty, hl⟩
            · intro root
              rcases hwf.exclLoanExact root with ⟨hf, ht⟩
              refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
              · have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
                have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
                exact hf hex l lty hl
              · rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
                have hne_md : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
                have hne_ms : m ≠ src := by intro heq; subst m; rw [hsrc] at hm; cases hm
                refine ⟨hsc, m, mty, ?_, ?_⟩
                · rw [CheckState.getLocal_setLocal_ne _ _ hne_md,
                      CheckState.getLocal_setLocal_ne _ _ hne_ms]
                  exact hm
                · intro l lty' hl
                  have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
                  rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
                  have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
                  rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
                  exact huniq l lty' hl
            · intro id hid
              cases hwf.resAccounting id hid with
              | inl hopen =>
                rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
                by_cases hls : l = src
                · subst l
                  rw [hsrc] at hl; injection hl with hty; subst ty
                  rw [hvsrc] at hvl; injection hvl with hveq; subst v
                  exact Or.inl ⟨hres, hcnt, dst, al, pl, by simp, by simp, hhl⟩
                · have hne_ld : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
                  exact Or.inl ⟨hres, hcnt, l, al, pl,
                    by rw [CheckState.getLocal_setLocal_ne _ _ hne_ld,
                           CheckState.getLocal_setLocal_ne _ _ hls]; exact hl,
                    by simp [ExecState.getLocal_setLocal_ne _ _ hne_ld, hvl],
                    hhl⟩
              | inr hdisp => exact Or.inr hdisp

    | sharedLoan root ty =>
      simp only [hsrc] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ =>
        rw [hw] at hchk; cases hchk
        rcases hwf.sharedLoanValid src root ty hsrc with ⟨disp, a, hty, hroot, hvsrc, hvroot⟩
        have hdst_ne_root : dst ≠ root := checkWriteTarget_ne_heapOwner hw hroot
        refine ⟨σ.setLocal dst (some (.heapRef a)), by simp [stepStmt, hvsrc], ?_⟩
        refine
          { ownedValid := ?_
            ownerUnique := ?_
            sharedLoanValid := ?_
            mutLoanValid := ?_
            sharedCountExact := ?_
            exclLoanExact := ?_
            heapAddrBound := hwf.heapAddrBound
            heapResBound := hwf.heapResBound
            heapResInj := hwf.heapResInj
            traceBound := hwf.traceBound
            resAccounting := ?_ }
        · intro l lty hl
          simp only [CheckState.getLocal_setShared] at hl
          have hne : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
          rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
          exact ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hne, hlv],
            ValMatchesOwned.setLocal_irrel dst _ hvml⟩
        · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
          simp only [CheckState.getLocal_setShared] at hl₁ hl₂
          have hne₁ : l₁ ≠ dst := by intro heq; subst l₁; simp at hl₁
          have hne₂ : l₂ ≠ dst := by intro heq; subst l₂; simp at hl₂
          rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
          rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
          rw [ExecState.getLocal_setLocal_ne _ _ hne₁] at hv₁
          rw [ExecState.getLocal_setLocal_ne _ _ hne₂] at hv₂
          exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
        · intro l r lty hl
          simp only [CheckState.getLocal_setShared] at hl
          by_cases hld : l = dst
          · subst l
            simp only [CheckState.getLocal_setLocal_eq, LocalState.sharedLoan.injEq] at hl
            rcases hl with ⟨hr, hty'⟩; subst r; subst lty
            exact ⟨disp, a, hty,
              by simp [CheckState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hroot],
              by simp,
              by simp [ExecState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hvroot]⟩
          · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
            rcases hwf.sharedLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
            have hne_r : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
            exact ⟨d', ar, hty',
              by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
              by simp [ExecState.getLocal_setLocal_ne _ _ hld, hvl],
              by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
        · intro l r lty hl
          simp only [CheckState.getLocal_setShared] at hl
          have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
          rcases hwf.mutLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
          have hne_r : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
          exact ⟨d', ar, hty',
            by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
        · intro r
          rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
          by_cases hrr : r = root
          · subst r
            have hnotmem : dst ∉ bs := by
              intro hmem
              rcases (hiff dst).mp hmem with ⟨t', hloc⟩
              exact checkWriteTarget_ne_sharedLoan hw hloc rfl
            refine ⟨dst :: bs, List.nodup_cons.mpr ⟨hnotmem, hnd⟩, ?_, by simp [hlen]⟩
            intro l; simp only [List.mem_cons, CheckState.getLocal_setShared]
            constructor
            · rintro ( rfl | hmem )
              · exact ⟨ty, by simp⟩
              · rcases (hiff l).mp hmem with ⟨t', hl⟩
                have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
                exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
            · rintro ⟨t', hl⟩
              by_cases hld : l = dst
              · exact Or.inl hld
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                exact Or.inr ((hiff l).mpr ⟨t', hl⟩)
          · refine ⟨bs, hnd, ?_, by simp [CheckState.sharedCount_setShared_ne _ _ hrr, hlen]⟩
            intro l; simp only [CheckState.getLocal_setShared]; rw [hiff l]
            constructor
            · rintro ⟨t', hl⟩
              have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
              exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
            · rintro ⟨t', hl⟩
              by_cases hld : l = dst
              · subst l
                simp only [CheckState.getLocal_setLocal_eq, LocalState.sharedLoan.injEq] at hl
                exact absurd hl.1.symm hrr
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                exact ⟨t', hl⟩
        · intro r
          rcases hwf.exclLoanExact r with ⟨hf, ht⟩
          refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
          · simp only [CheckState.getLocal_setShared] at hl
            have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact hf hex l lty hl
          · simp only [CheckState.hasExclusiveLoan_setShared,
              CheckState.hasExclusiveLoan_setLocal] at hex
            rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
            have hne_rr : r ≠ root := by
              intro heq; subst r
              have hpos := hwf.sharedCount_pos_of_sharedLoan hsrc
              omega
            have hne_md : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
            refine ⟨by simp [CheckState.sharedCount_setShared_ne _ _ hne_rr, hsc],
              m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_md, hm], ?_⟩
            intro l lty' hl
            simp only [CheckState.getLocal_setShared] at hl
            have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
            exact huniq l lty' hl
        · intro id hid
          cases hwf.resAccounting id hid with
          | inl hopen =>
            rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
            have hne : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
            exact Or.inl ⟨hres, hcnt, l, al, pl,
              by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl],
              by simp [ExecState.getLocal_setLocal_ne _ _ hne, hvl],
              hhl⟩
          | inr hdisp => exact Or.inr hdisp

    | mutLoan root ty =>
      simp only [hsrc] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ =>
        rw [hw] at hchk; cases hchk
        rcases hwf.mutLoanValid src root ty hsrc with ⟨disp, a, hty, hroot, hvsrc, hvroot⟩
        have hdst_ne_root : dst ≠ root := checkWriteTarget_ne_heapOwner hw hroot
        have hsrc_ne_root : src ≠ root := by intro heq; subst src; rw [hsrc] at hroot; cases hroot
        refine ⟨σ.setLocal dst (some (.heapRef a)), by simp [stepStmt, hvsrc], ?_⟩
        refine
          { ownedValid := ?_
            ownerUnique := ?_
            sharedLoanValid := ?_
            mutLoanValid := ?_
            sharedCountExact := ?_
            exclLoanExact := ?_
            heapAddrBound := hwf.heapAddrBound
            heapResBound := hwf.heapResBound
            heapResInj := hwf.heapResInj
            traceBound := hwf.traceBound
            resAccounting := ?_ }
        · intro l lty hl
          have hne_d : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_d] at hl
          have hne_s : l ≠ src := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_s] at hl
          rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
          exact ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hne_d, hlv],
            ValMatchesOwned.setLocal_irrel dst _ hvml⟩
        · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
          have hne₁d : l₁ ≠ dst := by intro heq; subst l₁; simp at hl₁
          have hne₂d : l₂ ≠ dst := by intro heq; subst l₂; simp at hl₂
          rw [CheckState.getLocal_setLocal_ne _ _ hne₁d] at hl₁
          rw [CheckState.getLocal_setLocal_ne _ _ hne₂d] at hl₂
          have hne₁s : l₁ ≠ src := by intro heq; subst l₁; simp at hl₁
          have hne₂s : l₂ ≠ src := by intro heq; subst l₂; simp at hl₂
          rw [CheckState.getLocal_setLocal_ne _ _ hne₁s] at hl₁
          rw [CheckState.getLocal_setLocal_ne _ _ hne₂s] at hl₂
          rw [ExecState.getLocal_setLocal_ne _ _ hne₁d] at hv₁
          rw [ExecState.getLocal_setLocal_ne _ _ hne₂d] at hv₂
          exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
        · intro l r lty hl
          have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
          have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
          rcases hwf.sharedLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
          have hne_rd : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
          have hne_rs : r ≠ src := by intro heq; subst r; rw [hsrc] at hr'; cases hr'
          exact ⟨d', ar, hty',
            by rw [CheckState.getLocal_setLocal_ne _ _ hne_rd,
                   CheckState.getLocal_setLocal_ne _ _ hne_rs]; exact hr',
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_ld, hvl],
            by simp [ExecState.getLocal_setLocal_ne _ _ hne_rd, hvr]⟩
        · intro l r lty hl
          by_cases hld : l = dst
          · subst l
            simp only [CheckState.getLocal_setLocal_eq, LocalState.mutLoan.injEq] at hl
            rcases hl with ⟨hr, hty'⟩; subst r; subst lty
            exact ⟨disp, a, hty,
              by rw [CheckState.getLocal_setLocal_ne _ _ hdst_ne_root.symm,
                     CheckState.getLocal_setLocal_ne _ _ hsrc_ne_root.symm]; exact hroot,
              by simp,
              by simp [ExecState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hvroot]⟩
          · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
            have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
            rcases hwf.mutLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
            have hne_rd : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
            have hne_rs : r ≠ src := by intro heq; subst r; rw [hsrc] at hr'; cases hr'
            exact ⟨d', ar, hty',
              by rw [CheckState.getLocal_setLocal_ne _ _ hne_rd,
                     CheckState.getLocal_setLocal_ne _ _ hne_rs]; exact hr',
              by simp [ExecState.getLocal_setLocal_ne _ _ hld, hvl],
              by simp [ExecState.getLocal_setLocal_ne _ _ hne_rd, hvr]⟩
        · intro r
          rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
          refine ⟨bs, hnd, ?_, by simp [hlen]⟩
          intro l; rw [hiff l]
          constructor
          · rintro ⟨t', hl⟩
            have hne_ld : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
            have hne_ls : l ≠ src := by intro heq; subst l; rw [hsrc] at hl; cases hl
            exact ⟨t', by rw [CheckState.getLocal_setLocal_ne _ _ hne_ld,
              CheckState.getLocal_setLocal_ne _ _ hne_ls]; exact hl⟩
          · rintro ⟨t', hl⟩
            have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
            have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
            exact ⟨t', hl⟩
        · intro r
          rcases hwf.exclLoanExact r with ⟨hf, ht⟩
          refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
          · simp only [CheckState.hasExclusiveLoan_setLocal] at hex
            by_cases hld : l = dst
            · subst l
              simp only [CheckState.getLocal_setLocal_eq, LocalState.mutLoan.injEq] at hl
              rcases hl with ⟨hr, _⟩; subst root
              exact hf hex src ty hsrc
            · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
              have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
              exact hf hex l lty hl
          · simp only [CheckState.hasExclusiveLoan_setLocal] at hex
            rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
            by_cases hrr : r = root
            · subst r
              have hmsrc : src = m := huniq src ty hsrc
              subst m
              refine ⟨hsc, dst, ty, by simp, ?_⟩
              intro l lty' hl
              by_cases hld : l = dst
              · exact hld
              · exfalso
                rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
                exact hne_ls (huniq l lty' hl)
            · have hne_md : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
              have hne_ms : m ≠ src := by
                intro heq; subst m; rw [hsrc] at hm; injection hm with hr _; exact hrr hr.symm
              refine ⟨hsc, m, mty,
                by rw [CheckState.getLocal_setLocal_ne _ _ hne_md,
                       CheckState.getLocal_setLocal_ne _ _ hne_ms]; exact hm, ?_⟩
              intro l lty' hl
              by_cases hld : l = dst
              · subst l
                simp only [CheckState.getLocal_setLocal_eq, LocalState.mutLoan.injEq] at hl
                exact absurd hl.1.symm hrr
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                have hne_ls : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ls] at hl
                exact huniq l lty' hl
        · intro id hid
          cases hwf.resAccounting id hid with
          | inl hopen =>
            rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
            have hne_ld : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
            have hne_ls : l ≠ src := by intro heq; subst l; rw [hsrc] at hl; cases hl
            exact Or.inl ⟨hres, hcnt, l, al, pl,
              by rw [CheckState.getLocal_setLocal_ne _ _ hne_ld,
                     CheckState.getLocal_setLocal_ne _ _ hne_ls]; exact hl,
              by simp [ExecState.getLocal_setLocal_ne _ _ hne_ld, hvl],
              hhl⟩
          | inr hdisp => exact Or.inr hdisp

  | BorrowShared dst root =>
    unfold checkStmt at hchk
    cases hroot : Γ.getLocal root with
    | uninit => simp [hroot] at hchk
    | moved => simp [hroot] at hchk
    | sharedLoan _ _ => simp [hroot] at hchk
    | mutLoan _ _ => simp [hroot] at hchk
    | owned rty =>
      cases rty with
      | CopyPrim _ => simp [hroot] at hchk
      | OptionTy _ => simp [hroot] at hchk
      | RefShared _ => simp [hroot] at hchk
      | RefMut _ => simp [hroot] at hchk
      | HeapClass disp =>
        simp only [hroot] at hchk
        cases hex : Γ.hasExclusiveLoan root with
        | true => simp [hex] at hchk
        | false =>
          simp only [hex, Bool.false_eq_true, ↓reduceIte] at hchk
          cases hw : checkWriteTarget Γ dst with
          | error e => rw [hw] at hchk; cases hchk
          | ok _ =>
            rw [hw] at hchk; cases hchk
            rcases hwf.ownedValid root (.HeapClass disp) hroot with ⟨v, hvroot, hvm⟩
            rcases ValMatchesOwned.heapClass_live hvm with ⟨a, _, _, rfl, _⟩
            have hdst_ne_root : dst ≠ root := checkWriteTarget_ne_heapOwner hw hroot
            refine ⟨σ.setLocal dst (some (.heapRef a)), by simp [stepStmt, hvroot], ?_⟩
            refine
              { ownedValid := ?_
                ownerUnique := ?_
                sharedLoanValid := ?_
                mutLoanValid := ?_
                sharedCountExact := ?_
                exclLoanExact := ?_
                heapAddrBound := hwf.heapAddrBound
                heapResBound := hwf.heapResBound
                heapResInj := hwf.heapResInj
                traceBound := hwf.traceBound
                resAccounting := ?_ }
            · intro l lty hl
              simp only [CheckState.getLocal_setShared] at hl
              have hne : l ≠ dst := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
              rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
              exact ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hne, hlv],
                ValMatchesOwned.setLocal_irrel dst _ hvml⟩
            · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
              simp only [CheckState.getLocal_setShared] at hl₁ hl₂
              have hne₁ : l₁ ≠ dst := by intro heq; subst l₁; simp at hl₁
              have hne₂ : l₂ ≠ dst := by intro heq; subst l₂; simp at hl₂
              rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
              rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
              rw [ExecState.getLocal_setLocal_ne _ _ hne₁] at hv₁
              rw [ExecState.getLocal_setLocal_ne _ _ hne₂] at hv₂
              exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
            · intro l r lty hl
              simp only [CheckState.getLocal_setShared] at hl
              by_cases hld : l = dst
              · subst l
                simp only [CheckState.getLocal_setLocal_eq, LocalState.sharedLoan.injEq] at hl
                rcases hl with ⟨hr, hty'⟩; subst r; subst lty
                exact ⟨disp, a, rfl,
                  by simp [CheckState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hroot],
                  by simp,
                  by simp [ExecState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hvroot]⟩
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                rcases hwf.sharedLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
                have hne_r : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
                exact ⟨d', ar, hty',
                  by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                  by simp [ExecState.getLocal_setLocal_ne _ _ hld, hvl],
                  by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
            · intro l r lty hl
              simp only [CheckState.getLocal_setShared] at hl
              have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.mutLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
              have hne_r : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
              exact ⟨d', ar, hty',
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
                by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
            · intro r
              rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
              by_cases hrr : r = root
              · subst r
                have hnotmem : dst ∉ bs := by
                  intro hmem
                  rcases (hiff dst).mp hmem with ⟨t', hloc⟩
                  exact checkWriteTarget_ne_sharedLoan hw hloc rfl
                refine ⟨dst :: bs, List.nodup_cons.mpr ⟨hnotmem, hnd⟩, ?_, by simp [hlen]⟩
                intro l; simp only [List.mem_cons, CheckState.getLocal_setShared]
                constructor
                · rintro ( rfl | hmem )
                  · exact ⟨.RefShared (.HeapClass disp), by simp⟩
                  · rcases (hiff l).mp hmem with ⟨t', hl⟩
                    have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
                    exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
                · rintro ⟨t', hl⟩
                  by_cases hld : l = dst
                  · exact Or.inl hld
                  · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                    exact Or.inr ((hiff l).mpr ⟨t', hl⟩)
              · refine ⟨bs, hnd, ?_, by simp [CheckState.sharedCount_setShared_ne _ _ hrr, hlen]⟩
                intro l; simp only [CheckState.getLocal_setShared]; rw [hiff l]
                constructor
                · rintro ⟨t', hl⟩
                  have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
                  exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
                · rintro ⟨t', hl⟩
                  by_cases hld : l = dst
                  · subst l
                    simp only [CheckState.getLocal_setLocal_eq, LocalState.sharedLoan.injEq] at hl
                    exact absurd hl.1.symm hrr
                  · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                    exact ⟨t', hl⟩
            · intro r
              rcases hwf.exclLoanExact r with ⟨hf, ht⟩
              refine ⟨fun hexr l lty hl => ?_, fun hexr => ?_⟩
              · simp only [CheckState.getLocal_setShared] at hl
                have hne : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                exact hf hexr l lty hl
              · simp only [CheckState.hasExclusiveLoan_setShared,
                  CheckState.hasExclusiveLoan_setLocal] at hexr
                have hne_rr : r ≠ root := by intro heq; subst r; rw [hex] at hexr; cases hexr
                rcases ht hexr with ⟨hsc, m, mty, hm, huniq⟩
                have hne_md : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
                refine ⟨by simp [CheckState.sharedCount_setShared_ne _ _ hne_rr, hsc],
                  m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_md, hm], ?_⟩
                intro l lty' hl
                simp only [CheckState.getLocal_setShared] at hl
                have hne_ld : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_ld] at hl
                exact huniq l lty' hl
            · intro id hid
              cases hwf.resAccounting id hid with
              | inl hopen =>
                rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
                have hne : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
                exact Or.inl ⟨hres, hcnt, l, al, pl,
                  by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl],
                  by simp [ExecState.getLocal_setLocal_ne _ _ hne, hvl],
                  hhl⟩
              | inr hdisp => exact Or.inr hdisp

  | BorrowMut dst root =>
    unfold checkStmt at hchk
    cases hroot : Γ.getLocal root with
    | uninit => simp [hroot] at hchk
    | moved => simp [hroot] at hchk
    | sharedLoan _ _ => simp [hroot] at hchk
    | mutLoan _ _ => simp [hroot] at hchk
    | owned rty =>
      cases rty with
      | CopyPrim _ => simp [hroot] at hchk
      | OptionTy _ => simp [hroot] at hchk
      | RefShared _ => simp [hroot] at hchk
      | RefMut _ => simp [hroot] at hchk
      | HeapClass disp =>
        simp only [hroot] at hchk
        cases hnb : Γ.noActiveBorrows root with
        | false => simp [hnb] at hchk
        | true =>
          simp only [hnb, Bool.not_true, Bool.false_eq_true, ↓reduceIte] at hchk
          cases hw : checkWriteTarget Γ dst with
          | error e => rw [hw] at hchk; cases hchk
          | ok _ =>
            rw [hw] at hchk; cases hchk
            have ⟨hsc_root, hex_root⟩ := (CheckState.noActiveBorrows_iff Γ root).mp hnb
            rcases hwf.ownedValid root (.HeapClass disp) hroot with ⟨v, hvroot, hvm⟩
            rcases ValMatchesOwned.heapClass_live hvm with ⟨a, _, _, rfl, _⟩
            have hdst_ne_root : dst ≠ root := checkWriteTarget_ne_heapOwner hw hroot
            refine ⟨σ.setLocal dst (some (.heapRef a)), by simp [stepStmt, hvroot], ?_⟩
            refine
              { ownedValid := ?_
                ownerUnique := ?_
                sharedLoanValid := ?_
                mutLoanValid := ?_
                sharedCountExact := ?_
                exclLoanExact := ?_
                heapAddrBound := hwf.heapAddrBound
                heapResBound := hwf.heapResBound
                heapResInj := hwf.heapResInj
                traceBound := hwf.traceBound
                resAccounting := ?_ }
            · intro l lty hl
              simp only [CheckState.getLocal_setExcl] at hl
              have hne : l ≠ dst := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
              rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
              exact ⟨lv, by simp [ExecState.getLocal_setLocal_ne _ _ hne, hlv],
                ValMatchesOwned.setLocal_irrel dst _ hvml⟩
            · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
              simp only [CheckState.getLocal_setExcl] at hl₁ hl₂
              have hne₁ : l₁ ≠ dst := by intro heq; subst l₁; simp at hl₁
              have hne₂ : l₂ ≠ dst := by intro heq; subst l₂; simp at hl₂
              rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
              rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
              rw [ExecState.getLocal_setLocal_ne _ _ hne₁] at hv₁
              rw [ExecState.getLocal_setLocal_ne _ _ hne₂] at hv₂
              exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
            · intro l r lty hl
              simp only [CheckState.getLocal_setExcl] at hl
              have hne_l : l ≠ dst := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.sharedLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
              have hne_r : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
              exact ⟨d', ar, hty',
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                by simp [ExecState.getLocal_setLocal_ne _ _ hne_l, hvl],
                by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
            · intro l r lty hl
              simp only [CheckState.getLocal_setExcl] at hl
              by_cases hld : l = dst
              · subst l
                simp only [CheckState.getLocal_setLocal_eq, LocalState.mutLoan.injEq] at hl
                rcases hl with ⟨hr, hty'⟩; subst r; subst lty
                exact ⟨disp, a, rfl,
                  by simp [CheckState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hroot],
                  by simp,
                  by simp [ExecState.getLocal_setLocal_ne _ _ hdst_ne_root.symm, hvroot]⟩
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                rcases hwf.mutLoanValid l r lty hl with ⟨d', ar, hty', hr', hvl, hvr⟩
                have hne_r : r ≠ dst := (checkWriteTarget_ne_heapOwner hw hr').symm
                exact ⟨d', ar, hty',
                  by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                  by simp [ExecState.getLocal_setLocal_ne _ _ hld, hvl],
                  by simp [ExecState.getLocal_setLocal_ne _ _ hne_r, hvr]⟩
            · intro r
              rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
              refine ⟨bs, hnd, ?_, by simp [hlen]⟩
              intro l; simp only [CheckState.getLocal_setExcl]; rw [hiff l]
              constructor
              · rintro ⟨t', hl⟩
                have hne : l ≠ dst := (checkWriteTarget_ne_sharedLoan hw hl).symm
                exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
              · rintro ⟨t', hl⟩
                have hne : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                exact ⟨t', hl⟩
            · intro r
              rcases hwf.exclLoanExact r with ⟨hf, ht⟩
              by_cases hrr : r = root
              · subst r
                refine ⟨fun hexr => by simp at hexr, fun _ => ?_⟩
                refine ⟨by simp [hsc_root], dst, .RefMut (.HeapClass disp), by simp, ?_⟩
                intro l lty' hl
                simp only [CheckState.getLocal_setExcl] at hl
                by_cases hld : l = dst
                · exact hld
                · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                  exact absurd hl (hf hex_root l lty')
              · refine ⟨fun hexr l lty hl => ?_, fun hexr => ?_⟩
                · simp only [CheckState.hasExclusiveLoan_setExcl_ne _ _ hrr,
                    CheckState.hasExclusiveLoan_setLocal] at hexr
                  simp only [CheckState.getLocal_setExcl] at hl
                  by_cases hld : l = dst
                  · subst l
                    simp only [CheckState.getLocal_setLocal_eq, LocalState.mutLoan.injEq] at hl
                    exact absurd hl.1.symm hrr
                  · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                    exact hf hexr l lty hl
                · simp only [CheckState.hasExclusiveLoan_setExcl_ne _ _ hrr,
                    CheckState.hasExclusiveLoan_setLocal] at hexr
                  rcases ht hexr with ⟨hsc, m, mty, hm, huniq⟩
                  have hne_md : m ≠ dst := (checkWriteTarget_ne_mutLoan hw hm).symm
                  refine ⟨by simp [hsc], m, mty,
                    by simp [CheckState.getLocal_setLocal_ne _ _ hne_md, hm], ?_⟩
                  intro l lty' hl
                  simp only [CheckState.getLocal_setExcl] at hl
                  by_cases hld : l = dst
                  · subst l
                    simp only [CheckState.getLocal_setLocal_eq, LocalState.mutLoan.injEq] at hl
                    exact absurd hl.1.symm hrr
                  · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                    exact huniq l lty' hl
            · intro id hid
              cases hwf.resAccounting id hid with
              | inl hopen =>
                rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
                have hne : l ≠ dst := (checkWriteTarget_ne_heapOwner hw hl).symm
                exact Or.inl ⟨hres, hcnt, l, al, pl,
                  by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl],
                  by simp [ExecState.getLocal_setLocal_ne _ _ hne, hvl],
                  hhl⟩
              | inr hdisp => exact Or.inr hdisp

  | ReadShared dst refLocal =>
    unfold checkStmt at hchk
    have readHelper : ∀ (root : LocalId) (disp : Bool),
        Γ.getLocal root = .owned (.HeapClass disp) →
        (∃ a, σ.getLocal refLocal = some (.heapRef a) ∧ σ.getLocal root = some (.heapRef a)) →
        checkWriteTarget Γ dst = .ok () →
        Γ' = Γ.setLocal dst (.owned (.CopyPrim .int)) →
        ∃ σ', stepStmt mode σ (.ReadShared dst refLocal) = .ok σ' ∧ WfConfig Γ' σ' := by
      intro root disp hroot ⟨a, hvref, hvroot⟩ hw hΓ'
      subst Γ'
      rcases hwf.ownedValid root (.HeapClass disp) hroot with ⟨v, hvroot', hvm⟩
      rcases ValMatchesOwned.heapClass_live hvm with ⟨a', obj, _, rfl, hheap⟩
      rw [hvroot] at hvroot'; injection hvroot' with hvroot'; injection hvroot' with haeq; subst a'
      refine ⟨σ.setLocal dst (some (.int obj.payload)),
        by simp [stepStmt, hvref, hheap], ?_⟩
      exact wf_write_nonHeapOwned hwf hw (fun d hd => by cases hd) ⟨obj.payload, rfl⟩
    cases href : Γ.getLocal refLocal with
    | uninit => simp [href] at hchk
    | moved => simp [href] at hchk
    | owned _ => simp [href] at hchk
    | sharedLoan root rty =>
      rcases hwf.sharedLoanValid refLocal root rty href with ⟨disp, a, hty, hroot, hvref, hvroot⟩
      subst rty
      simp only [href] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ =>
        rw [hw] at hchk; cases hchk
        exact readHelper root disp hroot ⟨a, hvref, hvroot⟩ hw rfl
    | mutLoan root rty =>
      rcases hwf.mutLoanValid refLocal root rty href with ⟨disp, a, hty, hroot, hvref, hvroot⟩
      subst rty
      simp only [href] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ =>
        rw [hw] at hchk; cases hchk
        exact readHelper root disp hroot ⟨a, hvref, hvroot⟩ hw rfl

  | WriteMut refLocal newVal =>
    unfold checkStmt at hchk
    cases href : Γ.getLocal refLocal with
    | uninit => simp [href] at hchk
    | moved => simp [href] at hchk
    | owned _ => simp [href] at hchk
    | sharedLoan _ _ => simp [href] at hchk
    | mutLoan root rty =>
      rcases hwf.mutLoanValid refLocal root rty href with ⟨disp, a, hty, hroot, hvref, hvroot⟩
      subst rty
      simp only [href, Except.ok.injEq] at hchk
      subst Γ'
      rcases hwf.ownedValid root (.HeapClass disp) hroot with ⟨v, hvroot', hvm⟩
      rcases ValMatchesOwned.heapClass_live hvm with ⟨a', obj, _, rfl, hheap⟩
      rw [hvroot] at hvroot'; injection hvroot' with hvroot'; injection hvroot' with haeq; subst a'
      refine ⟨σ.setHeap a (some (.live { payload := newVal, resId := obj.resId })),
        by simp [stepStmt, hvref, hheap], ?_⟩
      refine
        { ownedValid := ?_
          ownerUnique := hwf.ownerUnique
          sharedLoanValid := hwf.sharedLoanValid
          mutLoanValid := hwf.mutLoanValid
          sharedCountExact := hwf.sharedCountExact
          exclLoanExact := hwf.exclLoanExact
          heapAddrBound := ?_
          heapResBound := ?_
          heapResInj := ?_
          traceBound := hwf.traceBound
          resAccounting := ?_ }
      · intro l lty hl
        rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
        refine ⟨lv, hlv, ?_⟩
        cases lty with
        | CopyPrim p => exact ValMatchesOwned.of_copy hvml rfl
        | OptionTy _ => exact hvml
        | RefShared _ => exact False.elim hvml
        | RefMut _ => exact False.elim hvml
        | HeapClass d =>
          cases d with
          | false =>
            rcases hvml with ⟨al, pl, hal, hveq, hhl⟩
            by_cases ha : al = a
            · subst al
              rw [hheap] at hhl
              simp only [Option.some.injEq, HeapCell.live.injEq] at hhl
              subst obj
              exact ⟨a, newVal, hal, hveq, by simp⟩
            · exact ⟨al, pl, hal, hveq, by simp [ExecState.getHeap_setHeap_ne _ _ ha, hhl]⟩
          | true =>
            rcases hvml with ⟨al, pl, idl, hal, hidl, hveq, hhl, hrl, hcl⟩
            by_cases ha : al = a
            · subst al
              rw [hheap] at hhl
              simp only [Option.some.injEq, HeapCell.live.injEq] at hhl
              subst obj
              exact ⟨a, newVal, idl, hal, hidl, hveq, by simp, hrl, hcl⟩
            · exact ⟨al, pl, idl, hal, hidl, hveq,
                by simp [ExecState.getHeap_setHeap_ne _ _ ha, hhl], hrl, hcl⟩
      · intro a' ha'
        simp only [ExecState.nextAddr_setHeap] at ha'
        have hnea : a' ≠ a := by
          intro heq; subst a'
          have hnone := hwf.heapAddrBound a ha'
          rw [hnone] at hheap; cases hheap
        simp [ExecState.getHeap_setHeap_ne _ _ hnea, hwf.heapAddrBound a' ha']
      · intro a' p' id' hh'
        simp only [ExecState.nextResId_setHeap]
        by_cases ha : a' = a
        · subst a'
          simp only [ExecState.getHeap_setHeap_eq, Option.some.injEq, HeapCell.live.injEq,
            HeapObj.mk.injEq] at hh'
          rcases hh' with ⟨_, hres⟩
          have hprev : σ.getHeap a = some (.live { payload := obj.payload, resId := some id' }) := by
            cases obj; simp_all
          exact hwf.heapResBound a obj.payload id' hprev
        · rw [ExecState.getHeap_setHeap_ne _ _ ha] at hh'
          exact hwf.heapResBound a' p' id' hh'
      · intro a₁ a₂ p₁ p₂ id' hh₁ hh₂
        have toOrig : ∀ ax px,
            (σ.setHeap a (some (.live { payload := newVal, resId := obj.resId }))).getHeap ax =
              some (.live { payload := px, resId := some id' }) →
            ∃ px', σ.getHeap ax = some (.live { payload := px', resId := some id' }) := by
          intro ax px hhx
          by_cases hax : ax = a
          · subst ax
            simp only [ExecState.getHeap_setHeap_eq, Option.some.injEq, HeapCell.live.injEq,
              HeapObj.mk.injEq] at hhx
            rcases hhx with ⟨_, hres⟩
            refine ⟨obj.payload, ?_⟩
            cases obj; simp_all
          · rw [ExecState.getHeap_setHeap_ne _ _ hax] at hhx
            exact ⟨px, hhx⟩
        rcases toOrig a₁ p₁ hh₁ with ⟨q₁, hq₁⟩
        rcases toOrig a₂ p₂ hh₂ with ⟨q₂, hq₂⟩
        exact hwf.heapResInj a₁ a₂ q₁ q₂ id' hq₁ hq₂
      · intro id hid
        simp only [ExecState.nextResId_setHeap] at hid
        cases hwf.resAccounting id hid with
        | inl hopen =>
          rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
          by_cases ha : al = a
          · subst al
            rw [hheap] at hhl
            simp only [Option.some.injEq, HeapCell.live.injEq] at hhl
            subst obj
            exact Or.inl ⟨hres, hcnt, l, a, newVal, hl, hvl, by simp⟩
          · exact Or.inl ⟨hres, hcnt, l, al, pl, hl, hvl,
              by simp [ExecState.getHeap_setHeap_ne _ _ ha, hhl]⟩
        | inr hdisp => exact Or.inr hdisp

  | EndBorrow refLocal =>
    unfold checkStmt at hchk
    cases href : Γ.getLocal refLocal with
    | uninit => simp [href] at hchk
    | moved => simp [href] at hchk
    | owned _ => simp [href] at hchk
    | sharedLoan root rty =>
      simp only [href, Except.ok.injEq] at hchk
      subst Γ'
      refine ⟨σ, rfl, ?_⟩
      refine
        { ownedValid := ?_
          ownerUnique := ?_
          sharedLoanValid := ?_
          mutLoanValid := ?_
          sharedCountExact := ?_
          exclLoanExact := ?_
          heapAddrBound := hwf.heapAddrBound
          heapResBound := hwf.heapResBound
          heapResInj := hwf.heapResInj
          traceBound := hwf.traceBound
          resAccounting := ?_ }
      · intro l lty hl
        simp only [CheckState.getLocal_setShared] at hl
        have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
        rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
        exact hwf.ownedValid l lty hl
      · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
        simp only [CheckState.getLocal_setShared] at hl₁ hl₂
        have hne₁ : l₁ ≠ refLocal := by intro heq; subst l₁; simp at hl₁
        have hne₂ : l₂ ≠ refLocal := by intro heq; subst l₂; simp at hl₂
        rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
        rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
        exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
      · intro l r lty hl
        simp only [CheckState.getLocal_setShared] at hl
        have hne_l : l ≠ refLocal := by intro heq; subst l; simp at hl
        rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
        rcases hwf.sharedLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
        have hne_r : r ≠ refLocal := by intro heq; subst r; rw [href] at hr'; cases hr'
        exact ⟨disp, ar, hty,
          by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'], hvl, hvr⟩
      · intro l r lty hl
        simp only [CheckState.getLocal_setShared] at hl
        have hne_l : l ≠ refLocal := by intro heq; subst l; simp at hl
        rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
        rcases hwf.mutLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
        have hne_r : r ≠ refLocal := by intro heq; subst r; rw [href] at hr'; cases hr'
        exact ⟨disp, ar, hty,
          by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'], hvl, hvr⟩
      · intro r
        rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
        by_cases hrr : r = root
        · subst r
          have hmem : refLocal ∈ bs := (hiff refLocal).mpr ⟨rty, href⟩
          have ⟨hlen_rem, _⟩ := length_removeNat_of_mem hmem
          refine ⟨removeNat refLocal bs, nodup_removeNat hnd, ?_,
            by simp [hlen, hlen_rem]⟩
          intro l
          rw [mem_removeNat_of_nodup hnd l, hiff l]
          simp only [CheckState.getLocal_setShared]
          constructor
          · rintro ⟨⟨t', hl⟩, hne⟩
            exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
          · rintro ⟨t', hl⟩
            have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact ⟨⟨t', hl⟩, hne⟩
        · refine ⟨bs, hnd, ?_, by simp [CheckState.sharedCount_setShared_ne _ _ hrr, hlen]⟩
          intro l; simp only [CheckState.getLocal_setShared]; rw [hiff l]
          constructor
          · rintro ⟨t', hl⟩
            have hne : l ≠ refLocal := by
              intro heq; subst l; rw [href] at hl; injection hl with hr _; exact hrr hr.symm
            exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
          · rintro ⟨t', hl⟩
            have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact ⟨t', hl⟩
      · intro r
        rcases hwf.exclLoanExact r with ⟨hf, ht⟩
        refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
        · simp only [CheckState.getLocal_setShared] at hl
          have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
          exact hf hex l lty hl
        · simp only [CheckState.hasExclusiveLoan_setShared,
            CheckState.hasExclusiveLoan_setLocal] at hex
          rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
          have hne_rr : r ≠ root := by
            intro heq; subst r
            have hpos := hwf.sharedCount_pos_of_sharedLoan href
            omega
          have hne_m : m ≠ refLocal := by intro heq; subst m; rw [href] at hm; cases hm
          refine ⟨by simp [CheckState.sharedCount_setShared_ne _ _ hne_rr, hsc],
            m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
          intro l lty' hl
          simp only [CheckState.getLocal_setShared] at hl
          have hne_l : l ≠ refLocal := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
          exact huniq l lty' hl
      · intro id hid
        cases hwf.resAccounting id hid with
        | inl hopen =>
          rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
          have hne : l ≠ refLocal := by intro heq; subst l; rw [href] at hl; cases hl
          exact Or.inl ⟨hres, hcnt, l, al, pl,
            by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl], hvl, hhl⟩
        | inr hdisp => exact Or.inr hdisp

    | mutLoan root rty =>
      simp only [href, Except.ok.injEq] at hchk
      subst Γ'
      have hex_root : Γ.hasExclusiveLoan root = true := hwf.hasExcl_of_mutLoan href
      refine ⟨σ, rfl, ?_⟩
      refine
        { ownedValid := ?_
          ownerUnique := ?_
          sharedLoanValid := ?_
          mutLoanValid := ?_
          sharedCountExact := ?_
          exclLoanExact := ?_
          heapAddrBound := hwf.heapAddrBound
          heapResBound := hwf.heapResBound
          heapResInj := hwf.heapResInj
          traceBound := hwf.traceBound
          resAccounting := ?_ }
      · intro l lty hl
        simp only [CheckState.getLocal_setExcl] at hl
        have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
        rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
        exact hwf.ownedValid l lty hl
      · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
        simp only [CheckState.getLocal_setExcl] at hl₁ hl₂
        have hne₁ : l₁ ≠ refLocal := by intro heq; subst l₁; simp at hl₁
        have hne₂ : l₂ ≠ refLocal := by intro heq; subst l₂; simp at hl₂
        rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
        rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
        exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
      · intro l r lty hl
        simp only [CheckState.getLocal_setExcl] at hl
        have hne_l : l ≠ refLocal := by intro heq; subst l; simp at hl
        rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
        rcases hwf.sharedLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
        have hne_r : r ≠ refLocal := by intro heq; subst r; rw [href] at hr'; cases hr'
        exact ⟨disp, ar, hty,
          by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'], hvl, hvr⟩
      · intro l r lty hl
        simp only [CheckState.getLocal_setExcl] at hl
        have hne_l : l ≠ refLocal := by intro heq; subst l; simp at hl
        rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
        rcases hwf.mutLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
        have hne_r : r ≠ refLocal := by intro heq; subst r; rw [href] at hr'; cases hr'
        exact ⟨disp, ar, hty,
          by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'], hvl, hvr⟩
      · intro r
        rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
        refine ⟨bs, hnd, ?_, by simp [hlen]⟩
        intro l; simp only [CheckState.getLocal_setExcl]; rw [hiff l]
        constructor
        · rintro ⟨t', hl⟩
          have hne : l ≠ refLocal := by intro heq; subst l; rw [href] at hl; cases hl
          exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
        · rintro ⟨t', hl⟩
          have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
          exact ⟨t', hl⟩
      · intro r
        rcases hwf.exclLoanExact r with ⟨hf, ht⟩
        by_cases hrr : r = root
        · subst r
          rcases ht hex_root with ⟨_, m, _, _, huniq⟩
          have hmref : refLocal = m := huniq refLocal rty href
          subst m
          refine ⟨fun _ l lty hl => ?_, fun hexr => by simp at hexr⟩
          simp only [CheckState.getLocal_setExcl] at hl
          have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
          rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
          exact hne (huniq l lty hl)
        · refine ⟨fun hexr l lty hl => ?_, fun hexr => ?_⟩
          · simp only [CheckState.hasExclusiveLoan_setExcl_ne _ _ hrr,
              CheckState.hasExclusiveLoan_setLocal] at hexr
            simp only [CheckState.getLocal_setExcl] at hl
            have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact hf hexr l lty hl
          · simp only [CheckState.hasExclusiveLoan_setExcl_ne _ _ hrr,
              CheckState.hasExclusiveLoan_setLocal] at hexr
            rcases ht hexr with ⟨hsc, m, mty, hm, huniq⟩
            have hne_m : m ≠ refLocal := by
              intro heq; subst m; rw [href] at hm; injection hm with hr _; exact hrr hr.symm
            refine ⟨by simp [hsc], m, mty,
              by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
            intro l lty' hl
            simp only [CheckState.getLocal_setExcl] at hl
            have hne_l : l ≠ refLocal := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
            exact huniq l lty' hl
      · intro id hid
        cases hwf.resAccounting id hid with
        | inl hopen =>
          rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
          have hne : l ≠ refLocal := by intro heq; subst l; rw [href] at hl; cases hl
          exact Or.inl ⟨hres, hcnt, l, al, pl,
            by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl], hvl, hhl⟩
        | inr hdisp => exact Or.inr hdisp

  | Print src =>
    unfold checkStmt at hchk
    cases hsrc : Γ.getLocal src with
    | uninit => simp [hsrc] at hchk
    | moved => simp [hsrc] at hchk
    | sharedLoan _ _ => simp [hsrc] at hchk
    | mutLoan _ _ => simp [hsrc] at hchk
    | owned sty =>
      cases sty with
      | HeapClass _ => simp [hsrc] at hchk
      | OptionTy _ => simp [hsrc] at hchk
      | RefShared _ => simp [hsrc] at hchk
      | RefMut _ => simp [hsrc] at hchk
      | CopyPrim p =>
        simp only [hsrc, Except.ok.injEq] at hchk
        subst Γ'
        rcases hwf.ownedValid src (.CopyPrim p) hsrc with ⟨v, hvsrc, hvm⟩
        have ⟨pv, hpv⟩ : ∃ pv : PrimVal, v = Val.ofPrim pv := by
          cases p with
          | int => rcases hvm with ⟨n, rfl⟩; exact ⟨.int n, rfl⟩
          | bool => rcases hvm with ⟨b, rfl⟩; exact ⟨.bool b, rfl⟩
        subst v
        refine ⟨σ.emitEvent (.Printed pv), ?_, ?_⟩
        · cases pv <;> simp [stepStmt, hvsrc, Val.ofPrim]
        · refine
            { ownedValid := ?_
              ownerUnique := hwf.ownerUnique
              sharedLoanValid := hwf.sharedLoanValid
              mutLoanValid := hwf.mutLoanValid
              sharedCountExact := hwf.sharedCountExact
              exclLoanExact := hwf.exclLoanExact
              heapAddrBound := hwf.heapAddrBound
              heapResBound := hwf.heapResBound
              heapResInj := hwf.heapResInj
              traceBound := ?_
              resAccounting := ?_ }
          · intro l lty hl
            rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
            refine ⟨lv, hlv, ?_⟩
            cases lty with
            | CopyPrim _ => exact ValMatchesOwned.of_copy hvml rfl
            | OptionTy _ => exact hvml
            | RefShared _ => exact False.elim hvml
            | RefMut _ => exact False.elim hvml
            | HeapClass d =>
              cases d with
              | false => exact hvml
              | true =>
                rcases hvml with ⟨al, pl, idl, hal, hidl, hveq, hhl, hrl, hcl⟩
                refine ⟨al, pl, idl, hal, hidl, hveq, hhl, hrl, ?_⟩
                simp [disposedCount_append, hcl]
          · intro id hid
            simp [disposedCount_append, hwf.traceBound id hid]
          · intro id hid
            cases hwf.resAccounting id hid with
            | inl hopen =>
              rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
              exact Or.inl ⟨hres, by simp [disposedCount_append, hcnt],
                l, al, pl, hl, hvl, hhl⟩
            | inr hdisp =>
              rcases hdisp with ⟨hres, hcnt⟩
              exact Or.inr ⟨hres, by simp [disposedCount_append, hcnt]⟩

  | Dispose src =>
    unfold checkStmt at hchk
    cases hsrc : Γ.getLocal src with
    | uninit => simp [hsrc] at hchk
    | moved => simp [hsrc] at hchk
    | sharedLoan _ _ => simp [hsrc] at hchk
    | mutLoan _ _ => simp [hsrc] at hchk
    | owned sty =>
      cases sty with
      | CopyPrim _ => simp [hsrc] at hchk
      | OptionTy _ => simp [hsrc] at hchk
      | RefShared _ => simp [hsrc] at hchk
      | RefMut _ => simp [hsrc] at hchk
      | HeapClass d =>
        cases d with
        | false => simp [hsrc] at hchk
        | true =>
          simp only [hsrc] at hchk
          cases hnb : Γ.noActiveBorrows src with
          | false => simp [hnb] at hchk
          | true =>
            simp only [hnb, Bool.not_true, Bool.false_eq_true, ↓reduceIte, Except.ok.injEq] at hchk
            subst Γ'
            have ⟨hsc_src, hex_src⟩ := (CheckState.noActiveBorrows_iff Γ src).mp hnb
            rcases hwf.ownedValid src (.HeapClass true) hsrc with
              ⟨v, hvsrc, a, payload, id₀, ha_lt, hid₀_lt, rfl, hheap, hres_open, hcnt₀⟩
            let σ₁ := (σ.setRes id₀ (some .disposedRes)).emitEvent (.Disposed id₀)
            let σ' := match mode with
              | .native  => σ₁.setHeap a (some .freed)
              | .managed => σ₁
            have hstep : stepStmt mode σ (.Dispose src) = .ok σ' := by
              cases mode <;> simp [stepStmt, hvsrc, hheap, hres_open, σ₁, σ']
            have hgetLocal_σ' : ∀ l, σ'.getLocal l = σ.getLocal l := by
              intro l; cases mode <;> rfl
            have hgetRes_σ' : ∀ id, σ'.getRes id = (σ.setRes id₀ (some .disposedRes)).getRes id := by
              intro id; cases mode <;> rfl
            have hgetHeap_ne_σ' : ∀ a', a' ≠ a → σ'.getHeap a' = σ.getHeap a' := by
              intro a' hne
              cases mode
              · simp [σ', σ₁, ExecState.getHeap_setHeap_ne _ _ hne]
              · rfl
            have htrace_σ' : σ'.trace = σ.trace ++ [.Disposed id₀] := by
              cases mode <;> rfl
            have hnextAddr_σ' : σ'.nextAddr = σ.nextAddr := by cases mode <;> rfl
            have hnextRes_σ' : σ'.nextResId = σ.nextResId := by cases mode <;> rfl
            refine ⟨σ', hstep, ?_⟩
            refine
              { ownedValid := ?_
                ownerUnique := ?_
                sharedLoanValid := ?_
                mutLoanValid := ?_
                sharedCountExact := ?_
                exclLoanExact := ?_
                heapAddrBound := ?_
                heapResBound := ?_
                heapResInj := ?_
                traceBound := ?_
                resAccounting := ?_ }
            · intro l lty hl
              have hne_l : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
              refine ⟨lv, by rw [hgetLocal_σ']; exact hlv, ?_⟩
              cases lty with
              | CopyPrim _ => exact ValMatchesOwned.of_copy hvml rfl
              | OptionTy _ => exact hvml
              | RefShared _ => exact False.elim hvml
              | RefMut _ => exact False.elim hvml
              | HeapClass d' =>
                cases d' with
                | false =>
                  rcases hvml with ⟨al, pl, hal, rfl, hhl⟩
                  have hnea : al ≠ a := by
                    intro haeq; subst al
                    exact hne_l (hwf.ownerUnique l src false true a hl hsrc hlv hvsrc)
                  exact ⟨al, pl, by rw [hnextAddr_σ']; exact hal, rfl,
                    by rw [hgetHeap_ne_σ' al hnea]; exact hhl⟩
                | true =>
                  rcases hvml with ⟨al, pl, idl, hal, hidl, rfl, hhl, hrl, hcl⟩
                  have hnea : al ≠ a := by
                    intro haeq; subst al
                    exact hne_l (hwf.ownerUnique l src true true a hl hsrc hlv hvsrc)
                  have hneid : idl ≠ id₀ := by
                    intro hideq; subst idl
                    exact hnea (hwf.heapResInj al a pl payload id₀ hhl hheap)
                  refine ⟨al, pl, idl, by rw [hnextAddr_σ']; exact hal,
                    by rw [hnextRes_σ']; exact hidl, rfl,
                    by rw [hgetHeap_ne_σ' al hnea]; exact hhl,
                    by rw [hgetRes_σ', ExecState.getRes_setRes_ne _ _ hneid]; exact hrl, ?_⟩
                  rw [htrace_σ', disposedCount_append, disposedCount_singleton_disposed]
                  simp [hneid, hcl]
            · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
              have hne₁ : l₁ ≠ src := by intro heq; subst l₁; simp at hl₁
              have hne₂ : l₂ ≠ src := by intro heq; subst l₂; simp at hl₂
              rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
              rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
              rw [hgetLocal_σ'] at hv₁ hv₂
              exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
            · intro l r lty hl
              have hne_l : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.sharedLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
              have hne_r : r ≠ src := by
                intro heq; subst r
                exact hwf.no_sharedLoan_of_zero hsc_src l lty hl
              exact ⟨disp, ar, hty,
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                by rw [hgetLocal_σ']; exact hvl,
                by rw [hgetLocal_σ']; exact hvr⟩
            · intro l r lty hl
              have hne_l : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.mutLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
              have hne_r : r ≠ src := by
                intro heq; subst r
                exact (hwf.exclLoanExact src).1 hex_src l lty hl
              exact ⟨disp, ar, hty,
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                by rw [hgetLocal_σ']; exact hvl,
                by rw [hgetLocal_σ']; exact hvr⟩
            · intro r
              rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
              refine ⟨bs, hnd, ?_, by simp [hlen]⟩
              intro l; rw [hiff l]
              constructor
              · rintro ⟨t', hl⟩
                have hne : l ≠ src := by intro heq; subst l; rw [hsrc] at hl; cases hl
                exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
              · rintro ⟨t', hl⟩
                have hne : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                exact ⟨t', hl⟩
            · intro r
              rcases hwf.exclLoanExact r with ⟨hf, ht⟩
              refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
              · have hne : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                exact hf hex l lty hl
              · rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
                have hne_m : m ≠ src := by intro heq; subst m; rw [hsrc] at hm; cases hm
                refine ⟨hsc, m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
                intro l lty' hl
                have hne_l : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
                exact huniq l lty' hl
            · intro a' ha'
              rw [hnextAddr_σ'] at ha'
              have hnea : a' ≠ a := Nat.ne_of_gt (Nat.lt_of_lt_of_le ha_lt ha')
              rw [hgetHeap_ne_σ' a' hnea]
              exact hwf.heapAddrBound a' ha'
            · intro a' p' id' hh'
              rw [hnextRes_σ']
              cases mode with
              | native =>
                have hnea : a' ≠ a := by intro heq; subst a'; simp [σ', σ₁] at hh'
                simp only [σ', σ₁, ExecState.getHeap_setHeap_ne _ _ hnea,
                  ExecState.getHeap_emitEvent, ExecState.getHeap_setRes] at hh'
                exact hwf.heapResBound a' p' id' hh'
              | managed =>
                exact hwf.heapResBound a' p' id' hh'
            · intro a₁ a₂ p₁ p₂ id' hh₁ hh₂
              cases mode with
              | native =>
                have hn₁ : a₁ ≠ a := by intro heq; subst a₁; simp [σ', σ₁] at hh₁
                have hn₂ : a₂ ≠ a := by intro heq; subst a₂; simp [σ', σ₁] at hh₂
                simp only [σ', σ₁, ExecState.getHeap_setHeap_ne _ _ hn₁,
                  ExecState.getHeap_emitEvent, ExecState.getHeap_setRes] at hh₁
                simp only [σ', σ₁, ExecState.getHeap_setHeap_ne _ _ hn₂,
                  ExecState.getHeap_emitEvent, ExecState.getHeap_setRes] at hh₂
                exact hwf.heapResInj a₁ a₂ p₁ p₂ id' hh₁ hh₂
              | managed =>
                exact hwf.heapResInj a₁ a₂ p₁ p₂ id' hh₁ hh₂
            · intro id hid
              rw [hnextRes_σ'] at hid
              have hneid : id ≠ id₀ := Nat.ne_of_gt (Nat.lt_of_lt_of_le hid₀_lt hid)
              rw [htrace_σ', disposedCount_append, disposedCount_singleton_disposed]
              simp [hneid, hwf.traceBound id hid]
            · intro id hid
              rw [hnextRes_σ'] at hid
              by_cases heq : id = id₀
              · subst id
                refine Or.inr ⟨by rw [hgetRes_σ']; simp, ?_⟩
                rw [htrace_σ', disposedCount_append, disposedCount_singleton_disposed]
                simp [hcnt₀]
              · cases hwf.resAccounting id hid with
                | inl hopen =>
                  rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
                  have hnea : al ≠ a := by
                    intro haeq; subst al
                    rw [hheap] at hhl
                    simp only [Option.some.injEq, HeapCell.live.injEq, HeapObj.mk.injEq] at hhl
                    rcases hhl with ⟨_, hid_eq⟩
                    exact heq hid_eq.symm
                  have hne_l : l ≠ src := by
                    intro hleq; subst l
                    rw [hvsrc] at hvl
                    injection hvl with hveq; injection hveq with haeq
                    exact hnea haeq.symm
                  refine Or.inl ⟨by rw [hgetRes_σ', ExecState.getRes_setRes_ne _ _ heq]; exact hres,
                    ?_, l, al, pl,
                    by simp [CheckState.getLocal_setLocal_ne _ _ hne_l, hl],
                    by rw [hgetLocal_σ']; exact hvl,
                    by rw [hgetHeap_ne_σ' al hnea]; exact hhl⟩
                  rw [htrace_σ', disposedCount_append, disposedCount_singleton_disposed]
                  simp [heq, hcnt]
                | inr hdisp =>
                  rcases hdisp with ⟨hres, hcnt⟩
                  refine Or.inr ⟨by rw [hgetRes_σ', ExecState.getRes_setRes_ne _ _ heq]; exact hres, ?_⟩
                  rw [htrace_σ', disposedCount_append, disposedCount_singleton_disposed]
                  simp [heq, hcnt]

  | DropMemory src =>
    unfold checkStmt at hchk
    cases hsrc : Γ.getLocal src with
    | uninit => simp [hsrc] at hchk
    | moved => simp [hsrc] at hchk
    | sharedLoan _ _ => simp [hsrc] at hchk
    | mutLoan _ _ => simp [hsrc] at hchk
    | owned sty =>
      simp only [hsrc] at hchk
      cases hlin : isLinearResource sty with
      | true => simp [hlin] at hchk
      | false =>
        simp only [hlin, Bool.false_eq_true, ↓reduceIte] at hchk
        cases htyeq : (sty == .HeapClass false) with
        | false => simp [htyeq] at hchk
        | true =>
          have hsty : sty = .HeapClass false := eq_of_beq htyeq
          subst sty
          simp only [htyeq, ↓reduceIte] at hchk
          cases hnb : Γ.noActiveBorrows src with
          | false => simp [hnb] at hchk
          | true =>
            simp only [hnb, Bool.not_true, Bool.false_eq_true, ↓reduceIte, Except.ok.injEq] at hchk
            subst Γ'
            have ⟨hsc_src, hex_src⟩ := (CheckState.noActiveBorrows_iff Γ src).mp hnb
            rcases hwf.ownedValid src (.HeapClass false) hsrc with
              ⟨v, hvsrc, a, payload, ha_lt, rfl, hheap⟩
            let σ' := match mode with
              | .native  => σ.setHeap a (some .freed)
              | .managed => σ
            have hstep : stepStmt mode σ (.DropMemory src) = .ok σ' := by
              cases mode <;> simp [stepStmt, hvsrc, hheap, σ']
            have hgetLocal_σ' : ∀ l, σ'.getLocal l = σ.getLocal l := by
              intro l; cases mode <;> rfl
            have hgetRes_σ' : ∀ id, σ'.getRes id = σ.getRes id := by
              intro id; cases mode <;> rfl
            have hgetHeap_ne_σ' : ∀ a', a' ≠ a → σ'.getHeap a' = σ.getHeap a' := by
              intro a' hne
              cases mode
              · simp [σ', ExecState.getHeap_setHeap_ne _ _ hne]
              · rfl
            have htrace_σ' : σ'.trace = σ.trace := by cases mode <;> rfl
            have hnextAddr_σ' : σ'.nextAddr = σ.nextAddr := by cases mode <;> rfl
            have hnextRes_σ' : σ'.nextResId = σ.nextResId := by cases mode <;> rfl
            refine ⟨σ', hstep, ?_⟩
            refine
              { ownedValid := ?_
                ownerUnique := ?_
                sharedLoanValid := ?_
                mutLoanValid := ?_
                sharedCountExact := ?_
                exclLoanExact := ?_
                heapAddrBound := ?_
                heapResBound := ?_
                heapResInj := ?_
                traceBound := ?_
                resAccounting := ?_ }
            · intro l lty hl
              have hne_l : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.ownedValid l lty hl with ⟨lv, hlv, hvml⟩
              refine ⟨lv, by rw [hgetLocal_σ']; exact hlv, ?_⟩
              cases lty with
              | CopyPrim _ => exact ValMatchesOwned.of_copy hvml rfl
              | OptionTy _ => exact hvml
              | RefShared _ => exact False.elim hvml
              | RefMut _ => exact False.elim hvml
              | HeapClass d' =>
                cases d' with
                | false =>
                  rcases hvml with ⟨al, pl, hal, rfl, hhl⟩
                  have hnea : al ≠ a := by
                    intro haeq; subst al
                    exact hne_l (hwf.ownerUnique l src false false a hl hsrc hlv hvsrc)
                  exact ⟨al, pl, by rw [hnextAddr_σ']; exact hal, rfl,
                    by rw [hgetHeap_ne_σ' al hnea]; exact hhl⟩
                | true =>
                  rcases hvml with ⟨al, pl, idl, hal, hidl, rfl, hhl, hrl, hcl⟩
                  have hnea : al ≠ a := by
                    intro haeq; subst al
                    exact hne_l (hwf.ownerUnique l src true false a hl hsrc hlv hvsrc)
                  exact ⟨al, pl, idl, by rw [hnextAddr_σ']; exact hal,
                    by rw [hnextRes_σ']; exact hidl, rfl,
                    by rw [hgetHeap_ne_σ' al hnea]; exact hhl,
                    by rw [hgetRes_σ']; exact hrl,
                    by rw [htrace_σ']; exact hcl⟩
            · intro l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
              have hne₁ : l₁ ≠ src := by intro heq; subst l₁; simp at hl₁
              have hne₂ : l₂ ≠ src := by intro heq; subst l₂; simp at hl₂
              rw [CheckState.getLocal_setLocal_ne _ _ hne₁] at hl₁
              rw [CheckState.getLocal_setLocal_ne _ _ hne₂] at hl₂
              rw [hgetLocal_σ'] at hv₁ hv₂
              exact hwf.ownerUnique l₁ l₂ d₁ d₂ a₀ hl₁ hl₂ hv₁ hv₂
            · intro l r lty hl
              have hne_l : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.sharedLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
              have hne_r : r ≠ src := by
                intro heq; subst r
                exact hwf.no_sharedLoan_of_zero hsc_src l lty hl
              exact ⟨disp, ar, hty,
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                by rw [hgetLocal_σ']; exact hvl,
                by rw [hgetLocal_σ']; exact hvr⟩
            · intro l r lty hl
              have hne_l : l ≠ src := by intro heq; subst l; simp at hl
              rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
              rcases hwf.mutLoanValid l r lty hl with ⟨disp, ar, hty, hr', hvl, hvr⟩
              have hne_r : r ≠ src := by
                intro heq; subst r
                exact (hwf.exclLoanExact src).1 hex_src l lty hl
              exact ⟨disp, ar, hty,
                by simp [CheckState.getLocal_setLocal_ne _ _ hne_r, hr'],
                by rw [hgetLocal_σ']; exact hvl,
                by rw [hgetLocal_σ']; exact hvr⟩
            · intro r
              rcases hwf.sharedCountExact r with ⟨bs, hnd, hiff, hlen⟩
              refine ⟨bs, hnd, ?_, by simp [hlen]⟩
              intro l; rw [hiff l]
              constructor
              · rintro ⟨t', hl⟩
                have hne : l ≠ src := by intro heq; subst l; rw [hsrc] at hl; cases hl
                exact ⟨t', by simp [CheckState.getLocal_setLocal_ne _ _ hne, hl]⟩
              · rintro ⟨t', hl⟩
                have hne : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                exact ⟨t', hl⟩
            · intro r
              rcases hwf.exclLoanExact r with ⟨hf, ht⟩
              refine ⟨fun hex l lty hl => ?_, fun hex => ?_⟩
              · have hne : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                exact hf hex l lty hl
              · rcases ht hex with ⟨hsc, m, mty, hm, huniq⟩
                have hne_m : m ≠ src := by intro heq; subst m; rw [hsrc] at hm; cases hm
                refine ⟨hsc, m, mty, by simp [CheckState.getLocal_setLocal_ne _ _ hne_m, hm], ?_⟩
                intro l lty' hl
                have hne_l : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
                exact huniq l lty' hl
            · intro a' ha'
              rw [hnextAddr_σ'] at ha'
              have hnea : a' ≠ a := Nat.ne_of_gt (Nat.lt_of_lt_of_le ha_lt ha')
              rw [hgetHeap_ne_σ' a' hnea]
              exact hwf.heapAddrBound a' ha'
            · intro a' p' id' hh'
              rw [hnextRes_σ']
              cases mode with
              | native =>
                have hnea : a' ≠ a := by intro heq; subst a'; simp [σ'] at hh'
                simp only [σ', ExecState.getHeap_setHeap_ne _ _ hnea] at hh'
                exact hwf.heapResBound a' p' id' hh'
              | managed =>
                exact hwf.heapResBound a' p' id' hh'
            · intro a₁ a₂ p₁ p₂ id' hh₁ hh₂
              cases mode with
              | native =>
                have hn₁ : a₁ ≠ a := by intro heq; subst a₁; simp [σ'] at hh₁
                have hn₂ : a₂ ≠ a := by intro heq; subst a₂; simp [σ'] at hh₂
                simp only [σ', ExecState.getHeap_setHeap_ne _ _ hn₁] at hh₁
                simp only [σ', ExecState.getHeap_setHeap_ne _ _ hn₂] at hh₂
                exact hwf.heapResInj a₁ a₂ p₁ p₂ id' hh₁ hh₂
              | managed =>
                exact hwf.heapResInj a₁ a₂ p₁ p₂ id' hh₁ hh₂
            · intro id hid
              rw [hnextRes_σ'] at hid
              rw [htrace_σ']
              exact hwf.traceBound id hid
            · intro id hid
              rw [hnextRes_σ'] at hid
              cases hwf.resAccounting id hid with
              | inl hopen =>
                rcases hopen with ⟨hres, hcnt, l, al, pl, hl, hvl, hhl⟩
                have hne_l : l ≠ src := by intro heq; subst l; rw [hsrc] at hl; cases hl
                have hnea : al ≠ a := by
                  intro haeq; subst al
                  rw [hheap] at hhl; cases hhl
                exact Or.inl ⟨by rw [hgetRes_σ']; exact hres,
                  by rw [htrace_σ']; exact hcnt,
                  l, al, pl,
                  by simp [CheckState.getLocal_setLocal_ne _ _ hne_l, hl],
                  by rw [hgetLocal_σ']; exact hvl,
                  by rw [hgetHeap_ne_σ' al hnea]; exact hhl⟩
              | inr hdisp =>
                rcases hdisp with ⟨hres, hcnt⟩
                exact Or.inr ⟨by rw [hgetRes_σ']; exact hres, by rw [htrace_σ']; exact hcnt⟩

/-! ### Multi-Step Preservation & Reachable Configurations -/

theorem evalStmts_sound (mode : ExecMode) :
    ∀ {ss : List Stmt} {Γ Γ' : CheckState} {σ : ExecState},
      WfConfig Γ σ →
      checkStmts Γ ss = .ok Γ' →
      ∃ σ', evalStmts mode σ ss = .ok σ' ∧ WfConfig Γ' σ'
  | [], Γ, Γ', σ, hwf, hchk => by
    simp only [checkStmts, Except.ok.injEq] at hchk
    subst Γ'
    exact ⟨σ, rfl, hwf⟩
  | s :: ss, Γ, Γ', σ, hwf, hchk => by
    unfold checkStmts at hchk
    cases hs : checkStmt Γ s with
    | error e => rw [hs] at hchk; cases hchk
    | ok Γ₁ =>
      rw [hs] at hchk
      change checkStmts Γ₁ ss = .ok Γ' at hchk
      rcases stepStmt_sound mode hwf hs with ⟨σ₁, hstep₁, hwf₁⟩
      rcases evalStmts_sound mode hwf₁ hchk with ⟨σ', heval, hwf'⟩
      refine ⟨σ', ?_, hwf'⟩
      unfold evalStmts
      rw [hstep₁]
      exact heval

/-- A configuration `(Γ, σ)` is reachable under `mode` if some checked statement prefix
    transitions `(CheckState.empty, ExecState.empty)` to `(Γ, σ)`. -/
def ReachableConfig (mode : ExecMode) (Γ : CheckState) (σ : ExecState) : Prop :=
  ∃ (stmts : List Stmt),
    checkStmts CheckState.empty stmts = .ok Γ ∧
    evalStmts mode ExecState.empty stmts = .ok σ

theorem reachableConfig_wf {mode : ExecMode} {Γ : CheckState} {σ : ExecState}
    (hreach : ReachableConfig mode Γ σ) :
    WfConfig Γ σ := by
  rcases hreach with ⟨stmts, hchk, heval⟩
  rcases evalStmts_sound mode wfConfig_empty hchk with ⟨σ', heval', hwf'⟩
  rw [heval] at heval'
  injection heval' with heq
  subst σ'
  exact hwf'

/-! ### Predicates for Active Borrows & Accessible Owner Paths -/

/-- Local `l` holds an active exclusive mutable borrow of heap address `a`. -/
def IsActiveMutBorrowAt (Γ : CheckState) (σ : ExecState) (l : LocalId) (a : Addr) : Prop :=
  (∃ root ty, Γ.getLocal l = .mutLoan root ty) ∧ σ.getLocal l = some (.heapRef a)

/-- Local `l` holds an active shared borrow of heap address `a`. -/
def IsActiveSharedBorrowAt (Γ : CheckState) (σ : ExecState) (l : LocalId) (a : Addr) : Prop :=
  (∃ root ty, Γ.getLocal l = .sharedLoan root ty) ∧ σ.getLocal l = some (.heapRef a)

/-- Local `l` is an accessible (currently unborrowed) owner path aliasing heap address `a`. -/
def IsAccessibleOwnerAt (Γ : CheckState) (σ : ExecState) (l : LocalId) (a : Addr) : Prop :=
  (∃ ty, Γ.getLocal l = .owned ty) ∧
  Γ.noActiveBorrows l = true ∧
  σ.getLocal l = some (.heapRef a)

/-!
  =============================================================================
  THEOREM 1: `aliasing_xor_mutability`
  =============================================================================
-/

/-- **Theorem 1 (`aliasing_xor_mutability`):**
    In every reachable checked configuration `(Γ, σ)` (under either LLVM native or
    `.NET` CLR managed semantics), if local `m` holds an active mutable borrow of
    heap address `a`, then:
      1. `a` is a `live` heap object (`∃ obj, σ.getHeap a = some (.live obj)`),
      2. No other local `l ≠ m` holds an active mutable borrow of `a`,
      3. No local `l` holds an active shared borrow of `a`, and
      4. No local `l` is an accessible owner path aliasing `a`. -/
theorem aliasing_xor_mutability
    {mode : ExecMode} {Γ : CheckState} {σ : ExecState} {m : LocalId} {a : Addr}
    (hreach : ReachableConfig mode Γ σ)
    (hmut : IsActiveMutBorrowAt Γ σ m a) :
    (∃ obj, σ.getHeap a = some (.live obj)) ∧
    (∀ l, IsActiveMutBorrowAt Γ σ l a → l = m) ∧
    (∀ l, ¬ IsActiveSharedBorrowAt Γ σ l a) ∧
    (∀ l, ¬ IsAccessibleOwnerAt Γ σ l a) := by
  have hwf := reachableConfig_wf hreach
  rcases hmut with ⟨⟨root, mty, hm_loc⟩, hm_val⟩
  rcases hwf.mutLoanValid m root mty hm_loc with ⟨disp, a₀, _, hroot_loc, hm_val', hroot_val⟩
  rw [hm_val] at hm_val'; injection hm_val' with haeq; injection haeq with haeq; subst a₀
  have hex_root : Γ.hasExclusiveLoan root = true := hwf.hasExcl_of_mutLoan hm_loc
  rcases (hwf.exclLoanExact root).2 hex_root with ⟨hsc_root, m₀, _, _, huniq⟩
  have hm₀ : m = m₀ := huniq m mty hm_loc
  subst m₀
  refine ⟨?_, ?_, ?_, ?_⟩
  · rcases hwf.ownedValid root (.HeapClass disp) hroot_loc with ⟨v, hrv, hvm⟩
    rcases ValMatchesOwned.heapClass_live hvm with ⟨a', obj, _, rfl, hh⟩
    rw [hroot_val] at hrv; injection hrv with hrv; injection hrv with haeq; subst a'
    exact ⟨obj, hh⟩
  · intro l ⟨⟨root', lty, hl_loc⟩, hl_val⟩
    rcases hwf.mutLoanValid l root' lty hl_loc with ⟨disp', a', _, hr'_loc, hl_val', hr'_val⟩
    rw [hl_val] at hl_val'; injection hl_val' with ha'; injection ha' with ha'; subst a'
    have hrooteq : root' = root :=
      hwf.ownerUnique root' root disp' disp a hr'_loc hroot_loc hr'_val hroot_val
    subst root'
    exact huniq l lty hl_loc
  · intro l ⟨⟨root', lty, hl_loc⟩, hl_val⟩
    rcases hwf.sharedLoanValid l root' lty hl_loc with ⟨disp', a', _, hr'_loc, hl_val', hr'_val⟩
    rw [hl_val] at hl_val'; injection hl_val' with ha'; injection ha' with ha'; subst a'
    have hrooteq : root' = root :=
      hwf.ownerUnique root' root disp' disp a hr'_loc hroot_loc hr'_val hroot_val
    subst root'
    exact hwf.no_sharedLoan_of_zero hsc_root l lty hl_loc
  · intro l ⟨⟨lty, hl_loc⟩, hl_nb, hl_val⟩
    rcases hwf.ownedValid l lty hl_loc with ⟨lv, hl_val', hvm_l⟩
    rw [hl_val] at hl_val'; injection hl_val' with hveq; subst lv
    rcases ValMatchesOwned.heapRef_is_heapClass hvm_l with ⟨disp', hd'⟩
    subst lty
    have hleq : l = root :=
      hwf.ownerUnique l root disp' disp a hl_loc hroot_loc hl_val hroot_val
    subst l
    have ⟨_, hex_false⟩ := (CheckState.noActiveBorrows_iff Γ root).mp hl_nb
    rw [hex_root] at hex_false
    cases hex_false

/-- Dual Corollary (`shared_borrow_immutability`):
    Whenever an active shared borrow of `a` exists, `a` is `live`, no mutable borrow
    aliases `a`, and no owner path to `a` is unborrowed. -/
theorem shared_borrow_immutability
    {mode : ExecMode} {Γ : CheckState} {σ : ExecState} {s : LocalId} {a : Addr}
    (hreach : ReachableConfig mode Γ σ)
    (hsh : IsActiveSharedBorrowAt Γ σ s a) :
    (∃ obj, σ.getHeap a = some (.live obj)) ∧
    (∀ l, ¬ IsActiveMutBorrowAt Γ σ l a) ∧
    (∀ l, ¬ IsAccessibleOwnerAt Γ σ l a) := by
  have hwf := reachableConfig_wf hreach
  rcases hsh with ⟨⟨root, sty, hs_loc⟩, hs_val⟩
  rcases hwf.sharedLoanValid s root sty hs_loc with ⟨disp, a₀, _, hroot_loc, hs_val', hroot_val⟩
  rw [hs_val] at hs_val'; injection hs_val' with haeq; injection haeq with haeq; subst a₀
  have hpos : 1 ≤ Γ.sharedCount root := hwf.sharedCount_pos_of_sharedLoan hs_loc
  refine ⟨?_, ?_, ?_⟩
  · rcases hwf.ownedValid root (.HeapClass disp) hroot_loc with ⟨v, hrv, hvm⟩
    rcases ValMatchesOwned.heapClass_live hvm with ⟨a', obj, _, rfl, hh⟩
    rw [hroot_val] at hrv; injection hrv with hrv; injection hrv with haeq; subst a'
    exact ⟨obj, hh⟩
  · intro l hmut
    exact (aliasing_xor_mutability hreach hmut).2.2.1 s ⟨⟨root, sty, hs_loc⟩, hs_val⟩
  · intro l ⟨⟨lty, hl_loc⟩, hl_nb, hl_val⟩
    rcases hwf.ownedValid l lty hl_loc with ⟨lv, hl_val', hvm_l⟩
    rw [hl_val] at hl_val'; injection hl_val' with hveq; subst lv
    rcases ValMatchesOwned.heapRef_is_heapClass hvm_l with ⟨disp', hd'⟩
    subst lty
    have hleq : l = root :=
      hwf.ownerUnique l root disp' disp a hl_loc hroot_loc hl_val hroot_val
    subst l
    have ⟨hsc_zero, _⟩ := (CheckState.noActiveBorrows_iff Γ root).mp hl_nb
    omega

/-!
  =============================================================================
  THEOREM 2: `native_memory_safety`
  =============================================================================
-/

/-- Return evaluation succeeds on every well-formed configuration passing `checkReturn`. -/
theorem evalReturn_sound {Γ : CheckState} {σ : ExecState} {ret : Option LocalId} {retTy : Option Ty}
    (hwf : WfConfig Γ σ)
    (hret : checkReturn Γ ret = .ok retTy) :
    (∃ out, evalReturn σ ret = .ok out ∧ out.trace = σ.trace) ∧
    (∀ id, id < σ.nextResId →
      σ.getRes id = some .disposedRes ∧ disposedCount id σ.trace = 1) := by
  unfold checkReturn at hret
  cases hfind : findLinearLeftover Γ (EnvMap.keys Γ.localMap) with
  | some bad => simp [hfind] at hret
  | none =>
    simp only [hfind] at hret
    have hnoLin : ∀ l, isLinearState (Γ.getLocal l) = false :=
      findLinearLeftover_none_all hfind
    have hallDisposed : ∀ id, id < σ.nextResId →
        σ.getRes id = some .disposedRes ∧ disposedCount id σ.trace = 1 := by
      intro id hid
      cases hwf.resAccounting id hid with
      | inl hopen =>
        rcases hopen with ⟨_, _, l, _, _, hl, _, _⟩
        have hlin := hnoLin l
        rw [hl] at hlin
        simp [isLinearState] at hlin
      | inr hdisp => exact hdisp
    refine ⟨?_, hallDisposed⟩
    cases ret with
    | none =>
      exact ⟨{ returnVal := none, trace := σ.trace }, rfl, rfl⟩
    | some retLocal =>
      cases hrl : Γ.getLocal retLocal with
      | uninit => simp [hrl] at hret
      | moved => simp [hrl] at hret
      | sharedLoan _ _ => simp [hrl] at hret
      | mutLoan _ _ => simp [hrl] at hret
      | owned rty =>
        rcases hwf.ownedValid retLocal rty hrl with ⟨rv, hrv, _⟩
        exact ⟨{ returnVal := some rv, trace := σ.trace },
          by simp [evalReturn, hrv], rfl⟩

/-- Parameterized whole-program soundness for both `.native` and `.managed` execution. -/
theorem evalProg_sound (mode : ExecMode) {p : Prog} {retTy : Option Ty}
    (hchk : checkProg p = .ok retTy) :
    ∃ run,
      evalProg mode p = .ok run ∧
      (∀ id, id < run.finalState.nextResId →
        run.finalState.getRes id = some .disposedRes ∧
        disposedCount id run.outcome.trace = 1) := by
  unfold checkProg at hchk
  cases hss : checkStmts CheckState.empty p.stmts with
  | error e => rw [hss] at hchk; cases hchk
  | ok Γ =>
    rw [hss] at hchk
    change checkReturn Γ p.ret = .ok retTy at hchk
    rcases evalStmts_sound mode wfConfig_empty hss with ⟨σ, heval, hwf⟩
    rcases evalReturn_sound hwf hchk with ⟨⟨out, hret, htr⟩, hallDisp⟩
    refine ⟨{ finalState := σ, outcome := out }, ?_, ?_⟩
    · unfold evalProg
      rw [heval]
      change (do let out ← evalReturn σ p.ret; return ({ finalState := σ, outcome := out } : ExecRun)) = .ok _
      rw [hret]
      rfl
    · intro id hid
      rw [htr]
      exact hallDisp id hid

/-- **Theorem 2 (`native_memory_safety` — Progress / Absence of Memory Faults & Exact-Once Disposal):**
    Every well-checked Vibranium MIR program (`checkProg p = .ok retTy`) executing under
    LLVM native semantics (`evalNative p`):
      1. Succeeds (`∃ run, evalNative p = .ok run`), **never** encountering
         `Error.UseAfterFree`, `Error.DoubleFree`, `Error.DoubleDispose`, or any runtime fault;
      2. Disposes every allocated linear `IDisposable` resource (`id < run.finalState.nextResId`)
         **exact-once** (`run.finalState.getRes id = some .disposedRes` and
         `disposedCount id run.outcome.trace = 1`). -/
theorem native_memory_safety {p : Prog} {retTy : Option Ty}
    (hchk : checkProg p = .ok retTy) :
    (∀ a, evalNative p ≠ .error (.UseAfterFree a)) ∧
    (∀ a, evalNative p ≠ .error (.DoubleFree a)) ∧
    (∀ id, evalNative p ≠ .error (.DoubleDispose id)) ∧
    (∃ run,
      evalNative p = .ok run ∧
      (∀ id, id < run.finalState.nextResId →
        run.finalState.getRes id = some .disposedRes ∧
        disposedCount id run.outcome.trace = 1)) := by
  rcases evalProg_sound .native hchk with ⟨run, hrun, hdisp⟩
  refine ⟨?_, ?_, ?_, ⟨run, hrun, hdisp⟩⟩
  · intro a herr; unfold evalNative at herr; rw [hrun] at herr; cases herr
  · intro a herr; unfold evalNative at herr; rw [hrun] at herr; cases herr
  · intro id herr; unfold evalNative at herr; rw [hrun] at herr; cases herr

end VibraniumFormal
