/-
  VibraniumFormal/Tests.lean
  Compile-time `#guard` Differential & Diagnostic Verification Suite.

  Exercises both:
  1. Positive end-to-end NLL + Affine Drop + Linear `IDisposable` + I/O programs
     across `checkProg`, `evalNative` (LLVM `free`), and `evalManaged` (`.NET` GC).
  2. Negative compile-time rejection tests for `VIB001`, `VIB002`, `VIB003`,
     `VIB004`, `VIB006`, `VIB007`, and `VIB008`.
-/

import VibraniumFormal.Syntax
import VibraniumFormal.Checker
import VibraniumFormal.Semantics
import VibraniumFormal.Soundness
import VibraniumFormal.Equivalence

namespace VibraniumFormal.Tests

deriving instance DecidableEq for Except

/-- End-to-end showcase program exercising:
    - Affine heap allocation (`x0 = new Node(10)`)
    - Exclusive mutable borrow (`m1 = &mut x0`), mutation (`*m1 = 99`), and NLL `EndBorrow m1`
    - Aliasing shared borrows (`s2 = &x0`, `s3 = &x0`), `ReadShared`, and NLL `EndBorrow`
    - Destructive affine move (`x4 = move x0`) and deterministic LLVM `DropMemory x4`
    - Linear `IDisposable` allocation (`f5 = new FileHandle(7)`), borrow, move (`f7 = move f5`),
      and exact-once `Dispose f7`
    - Observable `Print` I/O and scalar `Return` -/
def showcaseProg : Prog :=
  { stmts :=
      [ .Alloc 0 false 10       -- x0: owned HeapClass(false) @ addr 0
      , .BorrowMut 1 0          -- m1 = &mut x0
      , .WriteMut 1 99          -- *m1 = 99
      , .EndBorrow 1            -- NLL kill m1 -> x0 unlocked
      , .BorrowShared 2 0       -- s2 = &x0 (sharedCount = 1)
      , .BorrowShared 3 0       -- s3 = &x0 (sharedCount = 2)
      , .ReadShared 8 2         -- v8 = *s2 (99)
      , .EndBorrow 2            -- NLL kill s2 (sharedCount = 1)
      , .EndBorrow 3            -- NLL kill s3 (sharedCount = 0)
      , .CopyOrMove 4 0         -- x4 = move x0 (x0 -> moved)
      , .DropMemory 4           -- LLVM free(addr 0); CLR GC no-op
      , .Alloc 5 true 7         -- f5: owned HeapClass(true) @ addr 1, resId 0
      , .BorrowShared 6 5       -- s6 = &f5
      , .ReadShared 9 6         -- v9 = *s6 (7)
      , .EndBorrow 6            -- NLL kill s6
      , .CopyOrMove 7 5         -- f7 = move f5
      , .Print 8                -- emit Printed(99)
      , .Dispose 7              -- emit Disposed(0), close resId 0
      ]
    ret := some 8 }

-- 1. Static verification succeeds with `int` return type:
#guard checkProg showcaseProg == .ok (some (.CopyPrim .int))

-- 2. Native LLVM and Managed .NET produce the EXACT same return value and event trace:
#guard (evalNative showcaseProg).map (·.outcome) ==
  .ok { returnVal := some (.int 99), trace := [.Printed (.int 99), .Disposed 0] }

#guard (evalManaged showcaseProg).map (·.outcome) ==
  (evalNative showcaseProg).map (·.outcome)

-- 3. Native LLVM deterministically freed both heap cells, whereas Managed .NET kept them live for GC:
#guard (evalNative showcaseProg).map (fun r => (r.finalState.getHeap 0, r.finalState.getHeap 1)) ==
  .ok (some .freed, some .freed)

#guard (evalManaged showcaseProg).map (fun r => (r.finalState.getHeap 0, r.finalState.getHeap 1)) ==
  .ok (some (.live { payload := 99, resId := none }),
       some (.live { payload := 7, resId := some 0 }))

-- 4. Negative Safety Diagnostic Tests (`VIB001`–`VIB008`):

-- VIB001: Use after destructive move
def negUseAfterMove : Prog :=
  { stmts := [.Alloc 0 false 1, .CopyOrMove 1 0, .DropMemory 1, .DropMemory 0]
    ret := none }
#guard checkProg negUseAfterMove == .error (.VIB001_UseAfterMoveOrUninit 0)

-- VIB002: Mutable borrow while shared borrow is active (Aliasing XOR Mutability)
def negMutWhileShared : Prog :=
  { stmts := [.Alloc 0 false 1, .BorrowShared 1 0, .BorrowMut 2 0]
    ret := none }
#guard checkProg negMutWhileShared == .error (.VIB002_BorrowConflict 0)

-- VIB003: Dropping heap memory while an active shared borrow exists
def negDropWhileBorrowed : Prog :=
  { stmts := [.Alloc 0 false 1, .BorrowShared 1 0, .DropMemory 0]
    ret := none }
#guard checkProg negDropWhileBorrowed == .error (.VIB003_ConflictWithActiveBorrow 0)

-- VIB004: Mutating heap cell through a shared reference (`RefShared`)
def negWriteThroughShared : Prog :=
  { stmts := [.Alloc 0 false 1, .BorrowShared 1 0, .WriteMut 1 42]
    ret := none }
#guard checkProg negWriteThroughShared == .error (.VIB004_MutateThroughSharedBorrow 1)

-- VIB006: Leaking an undisposed linear `IDisposable` resource at program exit
def negLeakLinearResource : Prog :=
  { stmts := [.Alloc 0 true 5]
    ret := none }
#guard checkProg negLeakLinearResource == .error (.VIB006_LinearResourceViolation 0)

-- VIB006: Attempting silent `DropMemory` instead of `Dispose` on linear `IDisposable`
def negDropLinearResource : Prog :=
  { stmts := [.Alloc 0 true 5, .DropMemory 0]
    ret := none }
#guard checkProg negDropLinearResource == .error (.VIB006_LinearResourceViolation 0)

-- VIB007: Returning an escaping stack-bound borrow
def negEscapingBorrow : Prog :=
  { stmts := [.Alloc 0 false 1, .BorrowShared 1 0]
    ret := some 1 }
#guard checkProg negEscapingBorrow == .error (.VIB007_EscapingBorrow 1)

-- VIB008: Zero-initializing a non-zero-safe class type (`null` construction ban)
def negZeroUnsafeClass : Prog :=
  { stmts := [.ZeroInit 0 (.HeapClass false)]
    ret := none }
#guard checkProg negZeroUnsafeClass == .error (.VIB008_ZeroUnsafeInit 0 (.HeapClass false))

end VibraniumFormal.Tests
