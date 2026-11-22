/-
  VibraniumFormal/Equivalence.lean
  Machine-Checked Dual-Execution Bisimulation (Zero `sorry`, Zero custom axioms):

  - **Theorem 3 (`clr_llvm_equivalence` — `.NET` CLR GC vs. LLVM Deterministic `free`):**
    For every well-checked Vibranium MIR program (`checkProg p = .ok retTy`), Managed `.NET`
    CLR execution (`evalManaged p`, where `DropMemory` is a GC no-op and heap cells are
    never prematurely freed) and Native LLVM execution (`evalNative p`, where `DropMemory`
    and `Dispose` deterministically transition heap cells `live -> freed`) both terminate
    successfully and produce the **exact same return value and identical observable I/O +
    `Dispose` event trace** (`runMgd.outcome = runNat.outcome`).
-/

import VibraniumFormal.Syntax
import VibraniumFormal.Checker
import VibraniumFormal.Semantics
import VibraniumFormal.Soundness

namespace VibraniumFormal

/-! ### Lockstep Bisimulation Relation (`Bisim`) -/

/-- Coupling relation between LLVM native state `σNat` and `.NET` managed state `σMgd`
    at static state `Γ`. Dead/dropped heap addresses may be `.freed` in `σNat` while
    remaining `.live` (awaiting GC) in `σMgd`; every live owned heap cell agrees. -/
structure Bisim (Γ : CheckState) (σNat σMgd : ExecState) : Prop where
  wfNat      : WfConfig Γ σNat
  wfMgd      : WfConfig Γ σMgd
  localsEq   : ∀ (l : LocalId), σNat.getLocal l = σMgd.getLocal l
  nextAddrEq : σNat.nextAddr = σMgd.nextAddr
  nextResEq  : σNat.nextResId = σMgd.nextResId
  resEq      : ∀ (id : ResourceId), σNat.getRes id = σMgd.getRes id
  traceEq    : σNat.trace = σMgd.trace
  liveHeapEq :
    ∀ (l : LocalId) (disp : Bool) (a : Addr),
      Γ.getLocal l = .owned (.HeapClass disp) →
      σNat.getLocal l = some (.heapRef a) →
      σNat.getHeap a = σMgd.getHeap a

theorem bisim_empty : Bisim CheckState.empty ExecState.empty ExecState.empty where
  wfNat      := wfConfig_empty
  wfMgd      := wfConfig_empty
  localsEq _ := rfl
  nextAddrEq := rfl
  nextResEq  := rfl
  resEq _    := rfl
  traceEq    := rfl
  liveHeapEq _ _ _ h _ := by simp at h

/-! ### Single-Step Bisimulation Preservation -/

theorem stepStmt_bisim {Γ Γ' : CheckState} {σNat σMgd : ExecState} {s : Stmt}
    (hbis : Bisim Γ σNat σMgd)
    (hchk : checkStmt Γ s = .ok Γ') :
    ∃ σNat' σMgd',
      stepNative σNat s = .ok σNat' ∧
      stepManaged σMgd s = .ok σMgd' ∧
      Bisim Γ' σNat' σMgd' := by
  rcases stepStmt_sound .native hbis.wfNat hchk with ⟨σNat', hstepNat, hwfNat'⟩
  rcases stepStmt_sound .managed hbis.wfMgd hchk with ⟨σMgd', hstepMgd, hwfMgd'⟩
  refine ⟨σNat', σMgd', hstepNat, hstepMgd, ?_⟩
  cases s with
  | ConstPrim dst val =>
    unfold checkStmt at hchk; dsimp only at hchk
    cases hw : checkWriteTarget Γ dst with
    | error e => rw [hw] at hchk; cases hchk
    | ok _ =>
      rw [hw] at hchk; cases hchk
      simp only [stepStmt, Except.ok.injEq] at hstepNat hstepMgd
      subst σNat'; subst σMgd'
      refine
        { wfNat := hwfNat'
          wfMgd := hwfMgd'
          localsEq := fun l => by
            by_cases hld : l = dst
            · subst l; simp
            · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
          nextAddrEq := hbis.nextAddrEq
          nextResEq := hbis.nextResEq
          resEq := hbis.resEq
          traceEq := hbis.traceEq
          liveHeapEq := fun l disp a hl hvl => by
            have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
            exact hbis.liveHeapEq l disp a hl hvl }

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
        simp only [stepStmt, hzv, Except.ok.injEq] at hstepNat hstepMgd
        subst σNat'; subst σMgd'
        refine
          { wfNat := hwfNat'
            wfMgd := hwfMgd'
            localsEq := fun l => by
              by_cases hld : l = dst
              · subst l; simp
              · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
            nextAddrEq := hbis.nextAddrEq
            nextResEq := hbis.nextResEq
            resEq := hbis.resEq
            traceEq := hbis.traceEq
            liveHeapEq := fun l disp a hl hvl => by
              have hne : l ≠ dst := by
                intro heq; subst l; simp at hl; subst ty; simp [isZeroSafe] at hzs
              rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
              rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
              exact hbis.liveHeapEq l disp a hl hvl }

  | Alloc dst disp initVal =>
    unfold checkStmt at hchk; dsimp only at hchk
    cases hw : checkWriteTarget Γ dst with
    | error e => rw [hw] at hchk; cases hchk
    | ok _ =>
      rw [hw] at hchk; cases hchk
      cases disp with
      | false =>
        simp only [stepStmt, ↓reduceIte, Bool.false_eq_true,
          Except.ok.injEq] at hstepNat hstepMgd
        subst σNat'; subst σMgd'
        refine
          { wfNat := hwfNat'
            wfMgd := hwfMgd'
            localsEq := fun l => by
              by_cases hld : l = dst
              · subst l; simp [hbis.nextAddrEq]
              · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
            nextAddrEq := by simp [hbis.nextAddrEq]
            nextResEq := by simp [hbis.nextResEq]
            resEq := fun id => by simp [hbis.resEq id]
            traceEq := by simp [hbis.traceEq]
            liveHeapEq := fun l disp' a hl hvl => by
              by_cases hld : l = dst
              · subst l
                simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq,
                  Val.heapRef.injEq] at hvl
                subst a
                simp [hbis.nextAddrEq]
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                simp only [ExecState.getLocal_setLocal_ne _ _ hld,
                  ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddr] at hvl
                rcases hbis.wfNat.ownedValid l (.HeapClass disp') hl with ⟨v, hvl', hvm⟩
                rcases ValMatchesOwned.heapClass_live hvm with ⟨al, _, hal, rfl, _⟩
                rw [hvl] at hvl'; injection hvl' with hvl'; injection hvl' with haeq; subst al
                have hnea_nat : a ≠ σNat.nextAddr := Nat.ne_of_lt hal
                have hnea_mgd : a ≠ σMgd.nextAddr := by rw [← hbis.nextAddrEq]; exact hnea_nat
                simp [ExecState.getHeap_setHeap_ne _ _ hnea_nat,
                      ExecState.getHeap_setHeap_ne _ _ hnea_mgd,
                      hbis.liveHeapEq l disp' a hl hvl] }
      | true =>
        simp only [stepStmt, ↓reduceIte,
          Except.ok.injEq] at hstepNat hstepMgd
        subst σNat'; subst σMgd'
        refine
          { wfNat := hwfNat'
            wfMgd := hwfMgd'
            localsEq := fun l => by
              by_cases hld : l = dst
              · subst l; simp [hbis.nextAddrEq]
              · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
            nextAddrEq := by simp [hbis.nextAddrEq]
            nextResEq := by simp [hbis.nextResEq]
            resEq := fun id => by
              by_cases hid : id = σNat.nextResId
              · subst id; simp [hbis.nextResEq]
              · have hid_mgd : id ≠ σMgd.nextResId := by rw [← hbis.nextResEq]; exact hid
                simp [ExecState.getRes_setRes_ne _ _ hid,
                      ExecState.getRes_setRes_ne _ _ hid_mgd,
                      hbis.resEq id]
            traceEq := by simp [hbis.traceEq]
            liveHeapEq := fun l disp' a hl hvl => by
              by_cases hld : l = dst
              · subst l
                simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq,
                  Val.heapRef.injEq] at hvl
                subst a
                simp [hbis.nextAddrEq, hbis.nextResEq]
              · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                simp only [ExecState.getLocal_setLocal_ne _ _ hld, ExecState.getLocal_setRes,
                  ExecState.getLocal_setHeap, ExecState.getLocal_bumpAddrAndRes] at hvl
                rcases hbis.wfNat.ownedValid l (.HeapClass disp') hl with ⟨v, hvl', hvm⟩
                rcases ValMatchesOwned.heapClass_live hvm with ⟨al, _, hal, rfl, _⟩
                rw [hvl] at hvl'; injection hvl' with hvl'; injection hvl' with haeq; subst al
                have hnea_nat : a ≠ σNat.nextAddr := Nat.ne_of_lt hal
                have hnea_mgd : a ≠ σMgd.nextAddr := by rw [← hbis.nextAddrEq]; exact hnea_nat
                simp [ExecState.getHeap_setHeap_ne _ _ hnea_nat,
                      ExecState.getHeap_setHeap_ne _ _ hnea_mgd,
                      hbis.liveHeapEq l disp' a hl hvl] }

  | CopyOrMove dst src =>
    have hv_eq : σMgd.getLocal src = σNat.getLocal src := (hbis.localsEq src).symm
    cases hvsrc : σNat.getLocal src with
    | none =>
      simp [stepStmt, hvsrc] at hstepNat
    | some v =>
      rw [hvsrc] at hv_eq
      simp only [stepStmt, hvsrc, hv_eq,
        Except.ok.injEq] at hstepNat hstepMgd
      subst σNat'; subst σMgd'
      refine
        { wfNat := hwfNat'
          wfMgd := hwfMgd'
          localsEq := fun l => by
            by_cases hld : l = dst
            · subst l; simp
            · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
          nextAddrEq := hbis.nextAddrEq
          nextResEq := hbis.nextResEq
          resEq := hbis.resEq
          traceEq := hbis.traceEq
          liveHeapEq := fun l disp a hl hvl => by
            unfold checkStmt at hchk
            cases hsrc : Γ.getLocal src with
            | uninit => simp [hsrc] at hchk
            | moved => simp [hsrc] at hchk
            | owned ty =>
              cases hc : isCopy ty with
              | true =>
                simp only [hsrc, hc, ↓reduceIte] at hchk
                cases hw : checkWriteTarget Γ dst with
                | error e => rw [hw] at hchk; cases hchk
                | ok _ =>
                  rw [hw] at hchk; cases hchk
                  have hne : l ≠ dst := by
                    intro heq; subst l; simp at hl; subst ty; simp at hc
                  rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                  rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
                  exact hbis.liveHeapEq l disp a hl hvl
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
                    by_cases hld : l = dst
                    · subst l
                      simp only [CheckState.getLocal_setLocal_eq, LocalState.owned.injEq] at hl
                      subst ty
                      simp only [ExecState.getLocal_setLocal_eq, Option.some.injEq] at hvl
                      subst v
                      exact hbis.liveHeapEq src disp a hsrc hvsrc
                    · rw [CheckState.getLocal_setLocal_ne _ _ hld] at hl
                      have hne_s : l ≠ src := by intro heq; subst l; simp at hl
                      rw [CheckState.getLocal_setLocal_ne _ _ hne_s] at hl
                      rw [ExecState.getLocal_setLocal_ne _ _ hld] at hvl
                      exact hbis.liveHeapEq l disp a hl hvl
            | sharedLoan root ty =>
              simp only [hsrc] at hchk
              cases hw : checkWriteTarget Γ dst with
              | error e => rw [hw] at hchk; cases hchk
              | ok _ =>
                rw [hw] at hchk; cases hchk
                simp only [CheckState.getLocal_setShared] at hl
                have hne : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
                exact hbis.liveHeapEq l disp a hl hvl
            | mutLoan root ty =>
              simp only [hsrc] at hchk
              cases hw : checkWriteTarget Γ dst with
              | error e => rw [hw] at hchk; cases hchk
              | ok _ =>
                rw [hw] at hchk; cases hchk
                have hne_d : l ≠ dst := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_d] at hl
                have hne_s : l ≠ src := by intro heq; subst l; simp at hl
                rw [CheckState.getLocal_setLocal_ne _ _ hne_s] at hl
                rw [ExecState.getLocal_setLocal_ne _ _ hne_d] at hvl
                exact hbis.liveHeapEq l disp a hl hvl }

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
            rcases hbis.wfNat.ownedValid root (.HeapClass disp) hroot with ⟨v, hvroot_nat, hvm⟩
            rcases ValMatchesOwned.heapClass_live hvm with ⟨a, _, _, rfl, _⟩
            have hvroot_mgd : σMgd.getLocal root = some (.heapRef a) := by
              rw [← hbis.localsEq root]; exact hvroot_nat
            simp only [stepStmt, hvroot_nat, hvroot_mgd,
              Except.ok.injEq] at hstepNat hstepMgd
            subst σNat'; subst σMgd'
            refine
              { wfNat := hwfNat'
                wfMgd := hwfMgd'
                localsEq := fun l => by
                  by_cases hld : l = dst
                  · subst l; simp
                  · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
                nextAddrEq := hbis.nextAddrEq
                nextResEq := hbis.nextResEq
                resEq := hbis.resEq
                traceEq := hbis.traceEq
                liveHeapEq := fun l disp' a' hl hvl => by
                  simp only [CheckState.getLocal_setShared] at hl
                  have hne : l ≠ dst := by intro heq; subst l; simp at hl
                  rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                  rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
                  exact hbis.liveHeapEq l disp' a' hl hvl }

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
            rcases hbis.wfNat.ownedValid root (.HeapClass disp) hroot with ⟨v, hvroot_nat, hvm⟩
            rcases ValMatchesOwned.heapClass_live hvm with ⟨a, _, _, rfl, _⟩
            have hvroot_mgd : σMgd.getLocal root = some (.heapRef a) := by
              rw [← hbis.localsEq root]; exact hvroot_nat
            simp only [stepStmt, hvroot_nat, hvroot_mgd,
              Except.ok.injEq] at hstepNat hstepMgd
            subst σNat'; subst σMgd'
            refine
              { wfNat := hwfNat'
                wfMgd := hwfMgd'
                localsEq := fun l => by
                  by_cases hld : l = dst
                  · subst l; simp
                  · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
                nextAddrEq := hbis.nextAddrEq
                nextResEq := hbis.nextResEq
                resEq := hbis.resEq
                traceEq := hbis.traceEq
                liveHeapEq := fun l disp' a' hl hvl => by
                  simp only [CheckState.getLocal_setExcl] at hl
                  have hne : l ≠ dst := by intro heq; subst l; simp at hl
                  rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
                  rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
                  exact hbis.liveHeapEq l disp' a' hl hvl }

  | ReadShared dst refLocal =>
    unfold checkStmt at hchk
    have readBisim : ∀ (root : LocalId) (disp : Bool) (a : Addr),
        Γ.getLocal root = .owned (.HeapClass disp) →
        σNat.getLocal refLocal = some (.heapRef a) →
        σNat.getLocal root = some (.heapRef a) →
        Γ' = Γ.setLocal dst (.owned (.CopyPrim .int)) →
        Bisim Γ' σNat' σMgd' := by
      intro root disp a hroot hvref_nat hvroot_nat hΓ'
      subst Γ'
      rcases hbis.wfNat.ownedValid root (.HeapClass disp) hroot with ⟨v, hrv, hvm⟩
      rcases ValMatchesOwned.heapClass_live hvm with ⟨a', obj, _, rfl, hheap_nat⟩
      rw [hvroot_nat] at hrv; injection hrv with hrv; injection hrv with haeq; subst a'
      have hvref_mgd : σMgd.getLocal refLocal = some (.heapRef a) := by
        rw [← hbis.localsEq refLocal]; exact hvref_nat
      have hheap_mgd : σMgd.getHeap a = some (.live obj) := by
        rw [← hbis.liveHeapEq root disp a hroot hvroot_nat]; exact hheap_nat
      simp only [stepStmt, hvref_nat, hvref_mgd,
        hheap_nat, hheap_mgd, Except.ok.injEq] at hstepNat hstepMgd
      subst σNat'; subst σMgd'
      refine
        { wfNat := hwfNat'
          wfMgd := hwfMgd'
          localsEq := fun l => by
            by_cases hld : l = dst
            · subst l; simp
            · simp [ExecState.getLocal_setLocal_ne _ _ hld, hbis.localsEq l]
          nextAddrEq := hbis.nextAddrEq
          nextResEq := hbis.nextResEq
          resEq := hbis.resEq
          traceEq := hbis.traceEq
          liveHeapEq := fun l disp' a' hl hvl => by
            have hne : l ≠ dst := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            rw [ExecState.getLocal_setLocal_ne _ _ hne] at hvl
            exact hbis.liveHeapEq l disp' a' hl hvl }
    cases href : Γ.getLocal refLocal with
    | uninit => simp [href] at hchk
    | moved => simp [href] at hchk
    | owned _ => simp [href] at hchk
    | sharedLoan root rty =>
      rcases hbis.wfNat.sharedLoanValid refLocal root rty href with ⟨disp, a, rfl, hroot, hvref, hvroot⟩
      simp only [href] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ => rw [hw] at hchk; cases hchk; exact readBisim root disp a hroot hvref hvroot rfl
    | mutLoan root rty =>
      rcases hbis.wfNat.mutLoanValid refLocal root rty href with ⟨disp, a, rfl, hroot, hvref, hvroot⟩
      simp only [href] at hchk
      cases hw : checkWriteTarget Γ dst with
      | error e => rw [hw] at hchk; cases hchk
      | ok _ => rw [hw] at hchk; cases hchk; exact readBisim root disp a hroot hvref hvroot rfl

  | WriteMut refLocal newVal =>
    unfold checkStmt at hchk
    cases href : Γ.getLocal refLocal with
    | uninit => simp [href] at hchk
    | moved => simp [href] at hchk
    | owned _ => simp [href] at hchk
    | sharedLoan _ _ => simp [href] at hchk
    | mutLoan root rty =>
      rcases hbis.wfNat.mutLoanValid refLocal root rty href with ⟨disp, a, rfl, hroot, hvref_nat, hvroot_nat⟩
      simp only [href, Except.ok.injEq] at hchk
      subst Γ'
      rcases hbis.wfNat.ownedValid root (.HeapClass disp) hroot with ⟨v, hrv, hvm⟩
      rcases ValMatchesOwned.heapClass_live hvm with ⟨a', obj, _, rfl, hheap_nat⟩
      rw [hvroot_nat] at hrv; injection hrv with hrv; injection hrv with haeq; subst a'
      have hvref_mgd : σMgd.getLocal refLocal = some (.heapRef a) := by
        rw [← hbis.localsEq refLocal]; exact hvref_nat
      have hheap_mgd : σMgd.getHeap a = some (.live obj) := by
        rw [← hbis.liveHeapEq root disp a hroot hvroot_nat]; exact hheap_nat
      simp only [stepStmt, hvref_nat, hvref_mgd,
        hheap_nat, hheap_mgd, Except.ok.injEq] at hstepNat hstepMgd
      subst σNat'; subst σMgd'
      refine
        { wfNat := hwfNat'
          wfMgd := hwfMgd'
          localsEq := hbis.localsEq
          nextAddrEq := hbis.nextAddrEq
          nextResEq := hbis.nextResEq
          resEq := hbis.resEq
          traceEq := hbis.traceEq
          liveHeapEq := fun l disp' a' hl hvl => by
            by_cases ha : a' = a
            · subst a'; simp
            · simp [ExecState.getHeap_setHeap_ne _ _ ha, hbis.liveHeapEq l disp' a' hl hvl] }

  | EndBorrow refLocal =>
    unfold checkStmt at hchk
    simp only [stepStmt, Except.ok.injEq] at hstepNat hstepMgd
    subst σNat'; subst σMgd'
    refine
      { wfNat := hwfNat'
        wfMgd := hwfMgd'
        localsEq := hbis.localsEq
        nextAddrEq := hbis.nextAddrEq
        nextResEq := hbis.nextResEq
        resEq := hbis.resEq
        traceEq := hbis.traceEq
        liveHeapEq := fun l disp a hl hvl => by
          cases href : Γ.getLocal refLocal with
          | uninit => simp [href] at hchk
          | moved => simp [href] at hchk
          | owned _ => simp [href] at hchk
          | sharedLoan root _ =>
            simp only [href, Except.ok.injEq] at hchk; subst Γ'
            simp only [CheckState.getLocal_setShared] at hl
            have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact hbis.liveHeapEq l disp a hl hvl
          | mutLoan root _ =>
            simp only [href, Except.ok.injEq] at hchk; subst Γ'
            simp only [CheckState.getLocal_setExcl] at hl
            have hne : l ≠ refLocal := by intro heq; subst l; simp at hl
            rw [CheckState.getLocal_setLocal_ne _ _ hne] at hl
            exact hbis.liveHeapEq l disp a hl hvl }

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
        rcases hbis.wfNat.ownedValid src (.CopyPrim p) hsrc with ⟨v, hv_nat, hvm⟩
        have hv_mgd : σMgd.getLocal src = some v := by
          rw [← hbis.localsEq src]; exact hv_nat
        have ⟨pv, hpv⟩ : ∃ pv : PrimVal, v = Val.ofPrim pv := by
          cases p with
          | int => rcases hvm with ⟨n, rfl⟩; exact ⟨.int n, rfl⟩
          | bool => rcases hvm with ⟨b, rfl⟩; exact ⟨.bool b, rfl⟩
        subst v
        have heq_step : σNat' = σNat.emitEvent (.Printed pv) ∧
            σMgd' = σMgd.emitEvent (.Printed pv) := by
          cases pv <;> simp_all [stepStmt, Val.ofPrim]
        rcases heq_step with ⟨rfl, rfl⟩
        refine
          { wfNat := hwfNat'
            wfMgd := hwfMgd'
            localsEq := hbis.localsEq
            nextAddrEq := hbis.nextAddrEq
            nextResEq := hbis.nextResEq
            resEq := hbis.resEq
            traceEq := by simp [hbis.traceEq]
            liveHeapEq := hbis.liveHeapEq }

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
            rcases hbis.wfNat.ownedValid src (.HeapClass true) hsrc with
              ⟨v, hv_nat, a, payload, id₀, _, _, rfl, hheap_nat, hres_nat, _⟩
            have hv_mgd : σMgd.getLocal src = some (.heapRef a) := by
              rw [← hbis.localsEq src]; exact hv_nat
            have hheap_mgd : σMgd.getHeap a =
                some (.live { payload := payload, resId := some id₀ }) := by
              rw [← hbis.liveHeapEq src true a hsrc hv_nat]; exact hheap_nat
            have hres_mgd : σMgd.getRes id₀ = some .openRes := by
              rw [← hbis.resEq id₀]; exact hres_nat
            simp only [stepStmt, hv_nat, hv_mgd,
              hheap_nat, hheap_mgd, hres_nat, hres_mgd, Except.ok.injEq] at hstepNat hstepMgd
            subst σNat'; subst σMgd'
            refine
              { wfNat := hwfNat'
                wfMgd := hwfMgd'
                localsEq := hbis.localsEq
                nextAddrEq := hbis.nextAddrEq
                nextResEq := hbis.nextResEq
                resEq := fun id => by
                  by_cases hid : id = id₀
                  · subst id; simp
                  · simp [ExecState.getRes_setRes_ne _ _ hid, hbis.resEq id]
                traceEq := by simp [hbis.traceEq]
                liveHeapEq := fun l disp' a' hl hvl => by
                  have hne_l : l ≠ src := by intro heq; subst l; simp at hl
                  rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
                  simp only [ExecState.getLocal_setHeap, ExecState.getLocal_emitEvent,
                    ExecState.getLocal_setRes] at hvl
                  have hnea : a' ≠ a := by
                    intro haeq; subst a'
                    exact hne_l (hbis.wfNat.ownerUnique l src disp' true a hl hsrc hvl hv_nat)
                  simp [ExecState.getHeap_setHeap_ne _ _ hnea,
                    hbis.liveHeapEq l disp' a' hl hvl] }

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
            rcases hbis.wfNat.ownedValid src (.HeapClass false) hsrc with
              ⟨v, hv_nat, a, payload, _, rfl, hheap_nat⟩
            have hv_mgd : σMgd.getLocal src = some (.heapRef a) := by
              rw [← hbis.localsEq src]; exact hv_nat
            have hheap_mgd : σMgd.getHeap a =
                some (.live { payload := payload, resId := none }) := by
              rw [← hbis.liveHeapEq src false a hsrc hv_nat]; exact hheap_nat
            simp only [stepStmt, hv_nat, hv_mgd,
              hheap_nat, hheap_mgd, Except.ok.injEq] at hstepNat hstepMgd
            subst σNat'; subst σMgd'
            refine
              { wfNat := hwfNat'
                wfMgd := hwfMgd'
                localsEq := hbis.localsEq
                nextAddrEq := hbis.nextAddrEq
                nextResEq := hbis.nextResEq
                resEq := hbis.resEq
                traceEq := hbis.traceEq
                liveHeapEq := fun l disp' a' hl hvl => by
                  have hne_l : l ≠ src := by intro heq; subst l; simp at hl
                  rw [CheckState.getLocal_setLocal_ne _ _ hne_l] at hl
                  simp only [ExecState.getLocal_setHeap] at hvl
                  have hnea : a' ≠ a := by
                    intro haeq; subst a'
                    exact hne_l (hbis.wfNat.ownerUnique l src disp' false a hl hsrc hvl hv_nat)
                  simp [ExecState.getHeap_setHeap_ne _ _ hnea,
                    hbis.liveHeapEq l disp' a' hl hvl] }

/-! ### Multi-Step Lockstep Bisimulation -/

theorem evalStmts_bisim :
    ∀ {ss : List Stmt} {Γ Γ' : CheckState} {σNat σMgd : ExecState},
      Bisim Γ σNat σMgd →
      checkStmts Γ ss = .ok Γ' →
      ∃ σNat' σMgd',
        evalStmts .native σNat ss = .ok σNat' ∧
        evalStmts .managed σMgd ss = .ok σMgd' ∧
        Bisim Γ' σNat' σMgd'
  | [], Γ, Γ', σNat, σMgd, hbis, hchk => by
    simp only [checkStmts, Except.ok.injEq] at hchk
    subst Γ'
    exact ⟨σNat, σMgd, rfl, rfl, hbis⟩
  | s :: ss, Γ, Γ', σNat, σMgd, hbis, hchk => by
    unfold checkStmts at hchk
    cases hs : checkStmt Γ s with
    | error e => rw [hs] at hchk; cases hchk
    | ok Γ₁ =>
      rw [hs] at hchk
      change checkStmts Γ₁ ss = .ok Γ' at hchk
      rcases stepStmt_bisim hbis hs with ⟨σNat₁, σMgd₁, hstepNat, hstepMgd, hbis₁⟩
      rcases evalStmts_bisim hbis₁ hchk with ⟨σNat', σMgd', hevalNat, hevalMgd, hbis'⟩
      refine ⟨σNat', σMgd', ?_, ?_, hbis'⟩
      · unfold evalStmts; change stepStmt .native σNat s = .ok σNat₁ at hstepNat; rw [hstepNat]; exact hevalNat
      · unfold evalStmts; change stepStmt .managed σMgd s = .ok σMgd₁ at hstepMgd; rw [hstepMgd]; exact hevalMgd

/-!
  =============================================================================
  THEOREM 3: `clr_llvm_equivalence` (Dual-Execution Bisimulation)
  =============================================================================
-/

/-- **Theorem 3 (`clr_llvm_equivalence` — Dual-Execution Bisimulation):**
    For every well-checked Vibranium MIR program (`checkProg p = .ok retTy`), Managed `.NET`
    CLR execution (`evalManaged p`) and Native LLVM execution (`evalNative p`) both
    succeed and produce the **exact same return value and identical observable I/O +
    `Dispose` event trace** (`runManaged.outcome = runNative.outcome`). -/
theorem clr_llvm_equivalence {p : Prog} {retTy : Option Ty}
    (hchk : checkProg p = .ok retTy) :
    ∃ (runManaged runNative : ExecRun),
      evalManaged p = .ok runManaged ∧
      evalNative p = .ok runNative ∧
      runManaged.outcome = runNative.outcome ∧
      runManaged.outcome.returnVal = runNative.outcome.returnVal ∧
      runManaged.outcome.trace = runNative.outcome.trace := by
  unfold checkProg at hchk
  cases hss : checkStmts CheckState.empty p.stmts with
  | error e => rw [hss] at hchk; cases hchk
  | ok Γ =>
    rw [hss] at hchk
    change checkReturn Γ p.ret = .ok retTy at hchk
    rcases evalStmts_bisim bisim_empty hss with ⟨σNat, σMgd, hevalNat, hevalMgd, hbis⟩
    rcases evalReturn_sound hbis.wfNat hchk with ⟨⟨outNat, hretNat, _⟩, _⟩
    have hretEq : evalReturn σMgd p.ret = evalReturn σNat p.ret := by
      cases p.ret with
      | none => simp [evalReturn, hbis.traceEq]
      | some retLocal => simp [evalReturn, hbis.localsEq retLocal, hbis.traceEq]
    have hretMgd : evalReturn σMgd p.ret = .ok outNat := hretEq.trans hretNat
    refine ⟨{ finalState := σMgd, outcome := outNat },
            { finalState := σNat, outcome := outNat }, ?_, ?_, rfl, rfl, rfl⟩
    · unfold evalManaged evalProg
      rw [hevalMgd]
      change (do let out ← evalReturn σMgd p.ret; return ({ finalState := σMgd, outcome := out } : ExecRun)) = .ok _
      rw [hretMgd]
      rfl
    · unfold evalNative evalProg
      rw [hevalNat]
      change (do let out ← evalReturn σNat p.ret; return ({ finalState := σNat, outcome := out } : ExecRun)) = .ok _
      rw [hretNat]
      rfl

end VibraniumFormal
