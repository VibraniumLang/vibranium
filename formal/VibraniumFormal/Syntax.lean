/-
  VibraniumFormal/Syntax.lean
  Mechanized Core Vibranium MIR Syntax & Ownership Type Classification.

  Mirrors `Vibranium.Compiler.Core.MirType` and `Vibranium.Compiler.Core.MirModule`
  (`src/Vibranium.Compiler.Core/Mir.cs`) and `docs/SPEC.md`.
-/

namespace VibraniumFormal

/-- MIR local variable identifier. -/
abbrev LocalId := Nat

/-- Abstract heap cell address. -/
abbrev Addr := Nat

/-- Linear OS / `IDisposable` resource identifier. -/
abbrev ResourceId := Nat

/-- Copy primitive scalar types (`int`, `bool`). -/
inductive PrimTy where
  | int
  | bool
  deriving DecidableEq, Repr

/-- Immediate primitive scalar constants. -/
inductive PrimVal where
  | int (n : Int)
  | bool (b : Bool)
  deriving DecidableEq, Repr

/-- Static type of a primitive constant. -/
def PrimVal.ty : PrimVal → PrimTy
  | .int _  => .int
  | .bool _ => .bool

/-- Core Vibranium MIR Type universe (`MirType` in `Mir.cs`). -/
inductive Ty where
  | CopyPrim (p : PrimTy)
  | HeapClass (disposable : Bool)
  | OptionTy (inner : Ty)
  | RefShared (target : Ty)
  | RefMut (target : Ty)
  deriving DecidableEq, Repr

/-- `MirModule.IsCopy`: bitwise copyable on `=`, pass, and return. -/
def isCopy : Ty → Bool
  | .CopyPrim _  => true
  | .HeapClass _ => false
  | .OptionTy t  => isCopy t
  | .RefShared _ => true
  | .RefMut _    => false

/-- `MirModule.IsZeroSafe` (`VIB008`): safe to zero-initialize (`default`). -/
def isZeroSafe : Ty → Bool
  | .CopyPrim _  => true
  | .OptionTy _  => true
  | .HeapClass _ => false
  | .RefShared _ => false
  | .RefMut _    => false

/-- `MirModule.IsLinearResource` (`VIB006`): contains a linear `IDisposable` resource. -/
def isLinearResource : Ty → Bool
  | .CopyPrim _     => false
  | .HeapClass disp => disp
  | .OptionTy t     => isLinearResource t
  | .RefShared _    => false
  | .RefMut _       => false

/-- `MirModule.IsViewLike` (`VIB007`): borrowed stack-bound reference/view. -/
def isViewLike : Ty → Bool
  | .RefShared _ => true
  | .RefMut _    => true
  | _            => false

/-- `MirModule.IsExclusiveView`: mutable/exclusive borrowed view. -/
def isExclusiveView : Ty → Bool
  | .RefMut _ => true
  | _         => false

/-- `MirModule.NeedsMemoryDrop`: affine heap/memory owner lowered to deterministic `free` in LLVM. -/
def needsMemoryDrop (ty : Ty) : Bool :=
  !isCopy ty && !isLinearResource ty && !isViewLike ty

/-! ### Foundational Classification Lemmas -/

@[simp] theorem isCopy_prim (p : PrimTy) : isCopy (.CopyPrim p) = true := rfl
@[simp] theorem isCopy_heap (d : Bool) : isCopy (.HeapClass d) = false := rfl
@[simp] theorem isCopy_refShared (t : Ty) : isCopy (.RefShared t) = true := rfl
@[simp] theorem isCopy_refMut (t : Ty) : isCopy (.RefMut t) = false := rfl

@[simp] theorem isLinear_prim (p : PrimTy) : isLinearResource (.CopyPrim p) = false := rfl
@[simp] theorem isLinear_heap (d : Bool) : isLinearResource (.HeapClass d) = d := rfl
@[simp] theorem isLinear_refShared (t : Ty) : isLinearResource (.RefShared t) = false := rfl
@[simp] theorem isLinear_refMut (t : Ty) : isLinearResource (.RefMut t) = false := rfl

@[simp] theorem needsMemoryDrop_heap_affine : needsMemoryDrop (.HeapClass false) = true := rfl
@[simp] theorem needsMemoryDrop_heap_linear : needsMemoryDrop (.HeapClass true) = false := rfl

/-- No `Copy` type is ever a linear `IDisposable` resource. -/
theorem copy_not_linear : ∀ (ty : Ty), isCopy ty = true → isLinearResource ty = false
  | .CopyPrim _,  _ => rfl
  | .HeapClass _, h => Bool.noConfusion h
  | .OptionTy t,  h => copy_not_linear t h
  | .RefShared _, _ => rfl
  | .RefMut _,    h => Bool.noConfusion h

/-- No `Copy` type ever requires LLVM `free` drop glue. -/
theorem copy_not_needsMemoryDrop (ty : Ty) (h : isCopy ty = true) :
    needsMemoryDrop ty = false := by
  simp [needsMemoryDrop, h]

/-- Linear `IDisposable` resources use `Dispose` rather than silent `DropMemory`. -/
theorem linear_not_needsMemoryDrop (ty : Ty) (h : isLinearResource ty = true) :
    needsMemoryDrop ty = false := by
  simp [needsMemoryDrop, h]

/-- Borrowed views never own heap memory or linear resources. -/
theorem view_not_linear : ∀ (ty : Ty), isViewLike ty = true → isLinearResource ty = false
  | .CopyPrim _,  h => Bool.noConfusion h
  | .HeapClass _, h => Bool.noConfusion h
  | .OptionTy _,  h => Bool.noConfusion h
  | .RefShared _, _ => rfl
  | .RefMut _,    _ => rfl

/-! ### Core Vibranium MIR Statements & Program Representation -/

/-- Core lowered Vibranium MIR statement forms. -/
inductive Stmt where
  /-- Bind a scalar constant (`int` or `bool`) to `dst`. -/
  | ConstPrim (dst : LocalId) (val : PrimVal)
  /-- Zero-initialize `dst` with `default(ty)` (checked by `VIB008`). -/
  | ZeroInit (dst : LocalId) (ty : Ty)
  /-- Allocate a single-owner heap object (`disposable = true` for `IDisposable`). -/
  | Alloc (dst : LocalId) (disposable : Bool) (initVal : Int)
  /-- Copy `src` if `isCopy`, or destructively move `src` into `dst` if affine/linear. -/
  | CopyOrMove (dst : LocalId) (src : LocalId)
  /-- Create a shared borrow `in T` / `&T` (`RefShared`) of `root`. -/
  | BorrowShared (dst : LocalId) (root : LocalId)
  /-- Create an exclusive mutable borrow `ref T` / `&mut T` (`RefMut`) of `root`. -/
  | BorrowMut (dst : LocalId) (root : LocalId)
  /-- Read the `Int` payload of a heap object through a shared or mutable borrow. -/
  | ReadShared (dst : LocalId) (refLocal : LocalId)
  /-- Mutate the `Int` payload of a heap object through an exclusive mutable borrow (`VIB004`). -/
  | WriteMut (refLocal : LocalId) (newVal : Int)
  /-- Non-Lexical Lifetimes (NLL) loan termination (`EndBorrow`). -/
  | EndBorrow (refLocal : LocalId)
  /-- Emit an observable `Print` I/O event for a `CopyPrim` scalar local. -/
  | Print (src : LocalId)
  /-- Deterministically close a linear `IDisposable` resource, emitting `Event.Disposed`. -/
  | Dispose (src : LocalId)
  /-- Deterministic native heap reclamation (`free` in LLVM; GC no-op in `.NET` CLR). -/
  | DropMemory (src : LocalId)
  deriving DecidableEq, Repr

/-- Complete Vibranium MIR function/entry-point body with optional return local. -/
structure Prog where
  stmts : List Stmt
  ret   : Option LocalId
  deriving DecidableEq, Repr

/-! ### Total Finite-Support Environment Map (`Nat → α` with Decidable Domain) -/

/-- Association-list finite map backed by a default value, enabling both
    computable exhaustive domain checks (`VIB006`) and clean algebraic lookup laws. -/
abbrev EnvMap (α : Type) := List (Nat × α)

namespace EnvMap

def lookup (default : α) : EnvMap α → Nat → α
  | [], _ => default
  | (k', v) :: rest, k => if k = k' then v else lookup default rest k

def insert (m : EnvMap α) (k : Nat) (v : α) : EnvMap α :=
  (k, v) :: m

def keys (m : EnvMap α) : List Nat :=
  m.map Prod.fst

@[simp] theorem lookup_empty (d : α) (k : Nat) :
    lookup d [] k = d := rfl

@[simp] theorem lookup_insert_eq (d : α) (m : EnvMap α) (k : Nat) (v : α) :
    lookup d (insert m k v) k = v := by
  simp [insert, lookup]

theorem lookup_insert_ne (d : α) (m : EnvMap α) {k k' : Nat} (v : α) (h : k ≠ k') :
    lookup d (insert m k' v) k = lookup d m k := by
  simp [insert, lookup, h]

theorem lookup_insert (d : α) (m : EnvMap α) (k k' : Nat) (v : α) :
    lookup d (insert m k' v) k = if k = k' then v else lookup d m k := rfl

theorem lookup_eq_default_of_not_mem (d : α) :
    ∀ (m : EnvMap α) (k : Nat), k ∉ keys m → lookup d m k = d
  | [], _, _ => rfl
  | (k', v) :: rest, k, hnot => by
    simp only [keys, List.map_cons, List.mem_cons, not_or] at hnot
    simp only [lookup, hnot.1, ↓reduceIte]
    exact lookup_eq_default_of_not_mem d rest k hnot.2

end EnvMap

end VibraniumFormal
