# VibraniumFormal — Mechanized Formal Verification Suite for Vibranium MIR (Lean 4)

`VibraniumFormal` is a standalone, zero-dependency **Lean 4** (`leanprover/lean4:v4.35.0`) mechanization of **Core Vibranium MIR**, its static ownership/borrow/linearity checker, its dual operational semantics (**LLVM 18 Native deterministic `free`** vs. **`.NET 11` CLR Garbage Collection**), and machine-checked proofs of Vibranium's three foundational soundness and equivalence theorems — with **zero `sorry`**, **zero custom axioms**, and **zero external package dependencies** (compiles from scratch in ~8 seconds).

---

## Repository Structure

```text
formal/
├── lakefile.toml               # Lake package configuration (`VibraniumFormal` lib + `vibranium-formal-verify` exe)
├── lean-toolchain              # Pinned `leanprover/lean4:v4.35.0`
├── VibraniumFormal.lean             # Root entry point + `#print axioms` audit for all theorems
├── Main.lean                   # Executable verification runner (`lake exe vibranium-formal-verify`)
├── README.md                   # Architecture & theorem reference
└── VibraniumFormal/
    ├── Syntax.lean             # Core Vibranium MIR types, `MirModule` ownership predicates & statements
    ├── Checker.lean            # Flow-sensitive static ownership, NLL borrow & linearity checker
    ├── Semantics.lean          # Dual operational semantics (`evalNative` vs. `evalManaged`)
    ├── Soundness.lean          # Theorems 1 & 2 (`aliasing_xor_mutability`, `native_memory_safety`)
    ├── Equivalence.lean        # Theorem 3 (`clr_llvm_equivalence` lockstep bisimulation)
    └── Tests.lean              # Compile-time `#guard` differential & `VIB001`–`VIB008` test suite
```

---

## Correspondence with `Vibranium.Compiler.Core` & `docs/SPEC.md`

### 1. Core Types & Ownership Classification (`VibraniumFormal/Syntax.lean`)
Models `MirType` and `MirModule` (`src/Vibranium.Compiler.Core/Mir.cs`):
- `Ty`: `CopyPrim (int | bool)`, `HeapClass (disposable : Bool)`, `OptionTy Ty`, `RefShared Ty`, `RefMut Ty`.
- Ownership predicates matching `MirModule`:
  - `isCopy`: `true` for `CopyPrim`, `RefShared`, and `OptionTy` over `Copy` payloads; `false` for `HeapClass` and `RefMut`.
  - `isZeroSafe` (`VIB008`): `true` for `CopyPrim` and `OptionTy` (where zeroed storage encodes `None`); `false` for `HeapClass`, `RefShared`, and `RefMut`.
  - `isLinearResource` (`VIB006`): `true` iff the type owns an `IDisposable` resource (`HeapClass true`, or `OptionTy` wrapping one).
  - `isViewLike` (`VIB007`) & `needsMemoryDrop` (`!isCopy && !isLinearResource && !isViewLike`).
- Core lowered MIR statements (`Stmt`): `ConstPrim`, `ZeroInit`, `Alloc`, `CopyOrMove`, `BorrowShared`, `BorrowMut`, `ReadShared`, `WriteMut`, `EndBorrow` (Non-Lexical Lifetimes loan termination), `Print`, `Dispose`, and `DropMemory`.

### 2. Static Ownership, Borrow & Linearity Checker (`VibraniumFormal/Checker.lean`)
Tracks per-local abstract states `{ uninit | owned ty | moved | sharedLoan root ty | mutLoan root ty }` alongside root loan counters (`sharedCount`, `hasExclusiveLoan`), enforcing Vibranium's diagnostic rules (`src/Vibranium.Compiler.Core/Diagnostics.cs`):
- **`VIB001`**: Rejects reading, borrowing, disposing, dropping, or returning `uninit` or `moved` locals.
- **`VIB002` / `VIB003`**: Enforces **Aliasing XOR Mutability** (`BorrowMut` requires `sharedCount == 0 && !hasExclusiveLoan`; `BorrowShared` requires `!hasExclusiveLoan`; moving, overwriting, disposing, or dropping an owner requires `sharedCount == 0 && !hasExclusiveLoan`).
- **`VIB004`**: Rejects `WriteMut` through a `RefShared` loan.
- **`VIB006`**: Rejects overwriting, silently `DropMemory`-freeing, or exiting a program with any unconsumed `isLinearResource` local.
- **`VIB007`**: Rejects returning stack-bound borrowed references (`RefShared` / `RefMut`).
- **`VIB008`**: Rejects `ZeroInit` on non-zero-safe types (`HeapClass`, `RefShared`, `RefMut`).

### 3. Dual Operational Semantics (`VibraniumFormal/Semantics.lean`)
- **`evalNative` (LLVM 18 Native Backend):** Explicit heap map `Addr -> HeapCell` (`live obj | freed`). `DropMemory` and `Dispose` deterministically transition `live -> freed` (failing with `Error.DoubleFree` if already `freed`), and `ReadShared` / `WriteMut` / `Dispose` fail with `Error.UseAfterFree` if targeting a `freed` cell. `Dispose` transitions linear resources `openRes -> disposedRes` (failing on `Error.DoubleDispose`) and emits `Event.Disposed id`.
- **`evalManaged` (`.NET 11` CLR Backend):** `DropMemory` is a heap no-op (CLR GC reclaims unreachable cells invisibly without premature `freed` transitions), while scalar values, `Event.Printed` I/O events, and `Event.Disposed` resource events execute directly.

---

## Machine-Checked Theorems

### Theorem 1 — Aliasing XOR Mutability (`VibraniumFormal/Soundness.lean`)
- **`VibraniumFormal.aliasing_xor_mutability`**: In every reachable checked configuration `(Γ, σ)` (under either `.native` or `.managed` execution), if local `m` holds an active mutable borrow (`RefMut`) of heap address `a`, then:
  1. `a` points to a `live` heap object (`∃ obj, σ.getHeap a = some (.live obj)`),
  2. Every active mutable borrow of `a` is `m` itself (`∀ l, IsActiveMutBorrowAt Γ σ l a → l = m`),
  3. No local holds an active shared borrow of `a` (`∀ l, ¬ IsActiveSharedBorrowAt Γ σ l a`), and
  4. No local is an unborrowed accessible owner path aliasing `a` (`∀ l, ¬ IsAccessibleOwnerAt Γ σ l a`).
- **`VibraniumFormal.shared_borrow_immutability`** (Dual Corollary): Whenever an active shared borrow of `a` exists, `a` is `live`, no mutable borrow aliases `a`, and no owner path to `a` is unborrowed.

### Theorem 2 — Native Memory Safety & Exact-Once Linear Disposal (`VibraniumFormal/Soundness.lean`)
- **`VibraniumFormal.native_memory_safety`**: For every well-checked Vibranium MIR program (`checkProg p = .ok retTy`):
  1. `evalNative p` never hits `Error.UseAfterFree`, `Error.DoubleFree`, `Error.DoubleDispose`, or any runtime fault (`∃ run, evalNative p = .ok run`); and
  2. Every allocated linear `IDisposable` resource (`id < run.finalState.nextResId`) is closed (`run.finalState.getRes id = some .disposedRes`) and appears in `run.outcome.trace` as `Event.Disposed id` **exact-once** (`disposedCount id run.outcome.trace = 1`).

### Theorem 3 — `.NET` CLR vs. LLVM Dual-Execution Bisimulation (`VibraniumFormal/Equivalence.lean`)
- **`VibraniumFormal.clr_llvm_equivalence`**: For every well-checked Vibranium MIR program (`checkProg p = .ok retTy`), `evalManaged p` (`.NET` GC) and `evalNative p` (LLVM deterministic `free`) both succeed and produce the **exact same return value and identical observable I/O + `Dispose` event trace** (`runManaged.outcome = runNative.outcome`).

---

## Building & Verifying

```bash
cd formal
~/.elan/bin/lake clean
~/.elan/bin/lake build
~/.elan/bin/lake exe vibranium-formal-verify
```

During `lake build`, `VibraniumFormal.lean` audits all foundational theorems via `#print axioms`, confirming reliance solely on Lean 4's standard foundational axioms (`[propext, Classical.choice, Quot.sound]`) with **zero `sorryAx`** and **zero custom axioms**.
