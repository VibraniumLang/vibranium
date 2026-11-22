Feasible and architecturally sound: Vibranium works if every non-copyable Vibranium value is modeled as a C# `struct` (never an unrestricted GC `class`) and deterministic destruction on `.NET` is driven by a Roslyn Source Generator or explicit scope guard rather than C#'s `using var` (which forces `readonly` locals and breaks explicit move invalidation).

---

### 1. Core Feasibility Verdict & Strategic Positioning

#### Verdict: High Feasibility — With One Foundational Invariant
Vibranium is genuinely viable and solves the single hardest adoption barrier of new systems languages: **day-one IDE, debugger, test-runner, and incremental build tooling**. Because Vibranium is a strict syntactic and type-valid subset of C# (on `.NET 10` / C# 13–14), developers get Rider / VS / C# Dev Kit navigation, refactoring, `dotnet test`, and step-through debugging for free, while `vib` compiles the verified subset to standalone zero-GC LLVM 18 native binaries.

The **foundational invariant** that makes Vibranium work is:
> **Vibranium user-defined types are exclusively C# `struct`s (value types) and `readonly struct`s, composed with an intrinsic set of `Vibranium.Core` linear/owned primitives (`Box<T>`, `Vec<T>`, `String`, `Rc<T>`/`Arc<T>`). Arbitrary C# `class` types, GC heap allocation (`new MyClass()`), `null` references, and implicit boxing are banned in Vibranium.**

#### Where the Genuine Tension Lies: C# Value/Reference Model vs. Rust Affine/Borrow Model
1. **Bitwise Copy vs. Affine Move (`=`)**:
   - In C#, `var b = a;` on a `struct` always performs a shallow bitwise copy, and `a` remains legally usable in Roslyn's eyes.
   - In Rust, `let b = a;` is a shallow `memcpy` **plus** compile-time invalidation of `a` (if `T: !Copy`) so `a`'s destructor does not run.
   - **Tension**: Under LLVM, `vib` simply treats `var b = a;` as a move and skips dropping `a`. Under `.NET`, `var b = a;` *also* performs a shallow bitwise copy of the struct's fields (e.g., an `IntPtr` + length + capacity inside `Vec<T>`). If `a`'s destructor/disposer subsequently runs on `.NET`, it will **double-free** or **use-after-free**! Solving `.NET` drop suppression on move is the #1 semantic-equivalence puzzle (solved in Section 2).
2. **Implicit Defensive Copies in C#**:
   - C# silently emits hidden stack copies whenever you call a non-`readonly` method on an `in T` parameter, `readonly` field, or `readonly` local (`ldloc` copy instead of `ldloca`). In an affine/borrow language, silent copies either violate single ownership or silently drop mutations.
   - **Rule**: Vibranium must forbid non-`readonly` instance calls on shared borrows (`in T`) and require explicit `readonly` annotations on pure struct methods (`readonly int Len()`), turning every hidden C# defensive copy into a hard Vibranium error.
3. **Method Receiver Mutability**:
   - In C#, calling `v.Push(x)` passes `this` as `ref Vec<T>` implicitly—there is no `&self` vs `&mut self` syntax at the call site, and on a `struct` declaration only `readonly` distinguishes `&self` (a `readonly` method) from `&mut self` (a non-`readonly` method).
   - **Good news**: C# `struct` instance methods already map 1-to-1 to Rust receivers!
     - `public readonly int Len()` $\Longleftrightarrow$ `fn len(&self) -> i32`
     - `public void Push(T item)` $\Longleftrightarrow$ `fn push(&mut self, item: T)`
     - Static factory / consuming method `public static B IntoB(Vec<T> self)` (or `this Vec<T> self` extension, though value-receiver instance methods don't exist in C#) $\Longleftrightarrow$ consuming `self`.

#### Lessons from Prior Art
- **Midori (M#)**: Proved that a C#-derived language with value-type-heavy data structures, static non-GC memory management, and fail-fast/checked-error discipline (`Result`/recoverable vs abandon) achieves C++/Rust systems performance while preserving C# developer ergonomics. Midori also showed that **banning exceptions for control flow** is essential for predictable zero-cost native codegen and clean drop paths.
- **Hylo (formerly Val) — Mutable Value Semantics**: Hylo demonstrated that `in` (shared projection), `inout` (`ref` in C#), and `sink` (by-value move) parameters cover **90%+ of real-world systems code without explicit lifetime annotations**. Because C# already has `in T`, `ref T`, `out T`, and by-value `T`, Vibranium naturally inherits Hylo-style lifetime-free ergonomics for almost all non-storing functions, needing explicit `[Lifetime]` attributes *only* when returning borrows or storing borrows in `ref struct` views.
- **Project Verona**: Highlighted that isolating linear/owned regions from ambient aliasing makes concurrency race-free by construction (`Send`/`Sync` marker interfaces in Vibranium).
- **C# 11–13 (`scoped`, `ref fields`, `ref readonly`, `allows ref struct`)**:
  - C# 11 added `scoped ref` / `scoped ReadOnlySpan<T>` and `ref` fields inside `ref struct`s.
  - C# 12 added `ref readonly` parameters.
  - **C# 13 (`.NET 9+`, available in our `.NET 10.0.301` SDK)** added `where T : allows ref struct`! This is a massive unlock for Vibranium: generic algorithms (`Slice<T>`, iterators, `Option<T>`) can now accept `Span<T>` / `ref struct` views in standard Roslyn C# without hitting old CLR generic constraints.

---

### 2. Solving Dual-Runtime Semantic Equivalence (`.NET` Debug vs. LLVM Native)

To guarantee **100% identical observable behavior** (including exact drop order, side effects, panic/abort points, arithmetic results, and memory reclamation) between `dotnet test` and `vib` native binaries, Vibranium needs crisp rules across six areas:

#### A. Value Types (`Copy`) vs. Owned Move Types (`!Copy`)
- **Copy types**: Primitives (`int`, `long`, `ulong`, `double`, `bool`, `byte`, `nuint`, enums) and `struct`s implementing marker interface `ICopy` (verified by Vibranium analyzer to have only `ICopy` fields and no `IDrop` implementation). Assignment `var b = a;` copies bits in both `.NET` and LLVM.
- **Affine / Move types (`!Copy`)**: Any `struct` that does not implement `ICopy`—including `Box<T>`, `Vec<T>`, `VibraniumString`, file/socket handles, and any composite `struct` containing an owned field.
  - Under `.NET`, `Box<T>` and `Vec<T>` wrap unmanaged memory (`NativeMemory.Alloc` / `Marshal.AllocHGlobal`), **not** `T[]` on the GC heap! Why?
    1. It exercises the exact same contiguous unmanaged layout and allocator discipline during `dotnet test` (catching native memory leaks in unit tests via deterministic allocation counters!).
    2. Moving a struct via bitwise copy copies `(T* ptr, nuint len, nuint cap)` identically in `.NET` and LLVM.

#### B. Moves, Deterministic `IDrop`, and the `using var` Trap
**The Trap**: It is tempting to use C#'s `IDisposable` + `using var v = Vec<int>.New();` to get deterministic drops on `.NET`. **Do not rely on `using var` for movable values:**
1. In C#, `using var x = ...;` makes `x` an **implicitly `readonly` local variable** (`CS1656`). You cannot call mutating methods properly without defensive-copy hazards (`v.Push(1)` on a `readonly` struct local mutates a temporary copy or fails if `Push` isn't tricked!), and you cannot zero out `x` via `Move(ref x)`.
2. Even if `x` is moved by value (`Consume(x)`), `using var` will unconditionally call `((IDisposable)x).Dispose()` at block exit on the stale bitwise copy of `x`—causing an immediate **double-free** under `dotnet test`!

**The Clean Dual-Runtime Solution (Two-Part Design)**:
1. **Runtime Move-Flag / Null-Pointer Poisoning on `.NET` Owned Primitives + Explicit `Vibranium.Move(ref x)` OR Roslyn Source-Generated Drop Lowering**:
   - **Option 1 (Zero-magic Source Syntax: `Vibranium.Move(ref x)` for moves, or Source Generator)**:
     - Wait: can `var b = a;` work directly without `Move(ref a)`?
     - Let's compare both carefully:
       - **If Vibranium uses plain `var b = a;` and `Consume(a)`**: In C#, pass-by-value `Consume(a)` cannot mutate the caller's stack slot `a` on `.NET`! So if `a` had an outer scope guard on `.NET`, `a`'s stack slot still holds the non-null pointer. And without `using var`, `.NET` won't call `a.Drop()` at scope exit *at all* unless something triggers it!
       - Wait—how *does* `.NET` run `Drop()` at scope exit if you don't write `using var`?
         - In `.NET`, structs do **not** have destructors/finalizers (`~MyStruct()` is illegal in C#). If a user writes:
           ```csharp
           void Foo() {
               var v = Vec<int>.WithCapacity(4);
               v.Push(42);
           } // <- How does v.Drop() run in `dotnet test`?
           ```
         - Unless the user writes a `using` scope, `.NET` will **never** execute cleanup at `}`—so side-effecting `IDrop` implementations (closing files, unlocking mutexes, decrementing `Rc<T>`, decrementing test leak counters) wouldn't run in `dotnet test`, violating 100% observable equivalence!
   - **How to get 100% exact Drop semantics in `.NET` without `using var` brokenness**:
     - **Mechanism A: Explicit `Vibranium.Move(ref x)` + `using` Scope Arena / `Defer`? No—still intrusive.**
     - **Mechanism B (Recommended): C# Interceptors / Source-Generated Wrapper or `Scope` + `Move(ref x)` vs. `.NET` IL Weaving (`Mono.Cecil` / MSBuild task)**:
       Let's evaluate **MSBuild IL Post-Processor (or Source Generator)** vs **Explicit Syntax**:
       - Wait: if you want standard `dotnet build` / `dotnet test` / Rider debugger to have **100% identical drop order** (including reverse declaration order, early `return`, conditional partial moves `if (cond) Consume(x);`), notice that **exact Rust drop elaboration** requires conditional drop flags on stack slots!
       - Can a pure MSBuild task (running right after `CoreCompile` in `.NET`, using `vib --inject-drops` or `System.Reflection.Metadata` / `Cecil` preserving PDB sequence points) inject the exact `Drop` calls emitted by Vibranium's MIR Drop Elaborator into the `.NET` assembly?
       - **YES — and there is an even simpler pure-C# alternative for the prototype before IL weaving**:
         1. **Pure-C# Idiom (`Move(ref a)` + `using` handle OR `Scope`)**: If `Move(ref a)` zeroes `a` (`a = default;`), you still can't pass `using var a` as `ref a` because C# forbids `ref` on `using` variables (`CS1657: Cannot use 'a' as a ref or out argument because it is a 'using variable'`)! That is a critical C# spec fact (`CS1657`).
         2. Because of `CS1657`, **`using var` in C# literally cannot be mutated via `ref` or moved via `Move(ref x)`**.
         3. Therefore, there are **two clean ways** to achieve 100% identical Drop execution on `.NET`:
            - **Path 1 (Compiler-Grade, Zero User Boilerplate — Recommended Architecture)**: Vibranium writes completely natural code (`var a = Box.New(1); var b = a;`). In `vib` (LLVM), MIR Drop Elaboration inserts exact `Drop` calls and drop flags. For `.NET` (`dotnet build`/`dotnet test`), an ultra-thin MSBuild target invokes `vib --rewrite-il` (or Roslyn syntax-tree rewrite prior to `csc` via a custom `.targets` replacing `Csc` inputs with drop-elaborated `.g.cs` files mapped back via `#line` directives!).
              - **Why `#line`-mapped C# lowering before `csc` is pure gold**:
                - Roslyn IDE sees the original `.cs` files untouched (with the Vibranium `DiagnosticAnalyzer` running live in-memory).
                - At `dotnet build` / `dotnet test` time, an MSBuild `BeforeTargets="CoreCompile"` task runs Vibranium's MIR Drop Elaborator on the syntax trees and emits drop-elaborated C# (`try { ... } finally { if (__drop_a) a.Drop(); }` with `#line` directives pointing to the exact user `.cs` lines!).
                - Step-through debugging in Rider/VS steps through the user's exact `.cs` lines seamlessly, `var b = a;` clears `__drop_a = false`, partial moves in `if/else` toggle `__drop_a` identically to LLVM, `a` stays mutable (no `using var` `CS1657`!), and **observable `Drop` side effects and leak checks are 100% identical** between `.NET` and LLVM!
            - **Path 2 (Zero-MSBuild-Hook Prototype Fallback)**: If avoiding MSBuild `CoreCompile` redirection on day 1, `Box<T>`/`Vec<T>` on `.NET` can use an explicit `Vibranium.Drop(ref v)` in tests or a `Scope` arena for debug runs, **but** `#line`-preserving C# drop-lowering in MSBuild takes ~150 lines of code and solves *every* move/drop/debugger equivalence issue permanently without polluting Vibranium syntax.

#### C. Shared Borrows (`in T`, `ReadOnlySpan<T>`) vs. Exclusive Borrows (`ref T`, `Span<T>`)
Vibranium maps Rust's borrow system directly onto existing C# 11–13 primitives:

| Rust Concept | Vibranium (Valid C#) Syntax | Notes |
|---|---|---|
| `&T` parameter | `in T` (or `ref readonly T`) | Immutable borrow; Vibranium bans non-`readonly` calls on `in T` |
| `&mut T` parameter | `ref T` | Exclusive mutable borrow |
| `&[T]` / `&str` | `ReadOnlySpan<T>` / `StrSlice` (`ref struct`) | Fat pointer `(T*, nuint len)` |
| `&mut [T]` | `Span<T>` | Exclusive slice borrow |
| Local `&T` | `ref readonly T r = ref x;` | C# `ref readonly` local |
| Local `&mut T` | `ref T r = ref x;` | C# `ref` local |
| `&T` / `&mut T` return | `ref readonly T` / `ref T` return | Tied to input borrow lifetime |
| Struct holding borrows | `ref struct View` with `ref T` / `ReadOnlySpan<T>` fields | Enforced stack-only by both Roslyn & Vibranium |

- **Reborrowing & Field Projections**:
  - Passing a `ref T` local `r` into `Foo(ref r)` or `Bar(in r)` acts as a **stacked reborrow**: `r` is temporarily frozen for the duration of the call (or lifetime of a returned `ref` / `Span<T>`), then active again afterward.
  - **Disjoint Field Splitting**: `ref var left = ref pair.Left; ref var right = ref pair.Right;` is **valid C#** and Vibranium's place-based NLL borrow checker tracks disjoint projection paths (`pair.Left` $\perp$ `pair.Right`), allowing simultaneous mutable borrows of distinct struct fields just like Rust!
  - Note on `Span<T>` splitting: `span.Slice(...)` cannot prove disjointness statically, so just like Rust's `slice::split_at_mut`, `VibraniumSlices.SplitAtMut(Span<T> s, int mid, out Span<T> left, out Span<T> right)` provides safe disjoint mutable sub-slices.
- **Lifetime Elision vs. `[Lifetime("a")]` Attributes**:
  - **95% of functions need zero attributes** using Rust's exact lifetime elision rules:
    1. No borrowed outputs (`void` or owned return): every `in`/`ref`/`Span`/`ReadOnlySpan` input gets a distinct anonymous lifetime (implicitly `scoped` in C# 11).
    2. Single borrowed input + borrowed output (`ref readonly int First(ReadOnlySpan<int> s)`): output lifetime automatically ties to the single input.
    3. Instance method returning a borrow (`public readonly ReadOnlySpan<T> AsSpan()`): output lifetime automatically ties to `this`.
  - **When `[Lifetime("a")]` IS required** (valid C# attributes in `Vibranium.Annotations`):
    - Functions returning a borrow when **multiple** borrowed inputs exist:
      ```csharp
      [return: Lifetime("a")]
      public static ref readonly int PickFirst(
          [Lifetime("a")] in int x,
          [Lifetime("b")] in int y) => ref x;
      ```
    - `ref struct` types holding borrows from **multiple distinct** lifetimes (`[Lifetime("a", "b")] ref struct PairView { [Lifetime("a")] public ReadOnlySpan<int> A; ... }`), or where `[Outlives("a", "b")]` (`'a: 'b`) is needed.

#### D. Generics, Closures, Strings, Overflow, Bounds Checks, and Errors
To prevent subtle behavioral drift between `.NET` and LLVM:

1. **Generics (`where T : IMyTrait`)**:
   - Vibranium restricts generic constraints to `struct`/interface constraints (using C# 11 **static abstract/virtual interface methods** + normal instance interface methods).
   - **Ban runtime type introspection**: `typeof(T)`, `x is Foo`, `object.ReferenceEquals`, and unconstrained `ToString()`/`GetHashCode()`/`Equals(object)` are banned by the analyzer (`IEquatable<T>` / `IHashable` traits are used instead).
   - Because type-inspection side channels are banned, **CLR generic reification on value types** and **LLVM full monomorphization** become 100% observationally equivalent! (No boxing occurs on `.NET` either when `T` is a `struct` constrained by an interface: RyuJIT specializes value-type generic instantiations and devirtualizes constrained interface calls!).
2. **Closures / Lambdas**:
   - Standard C# capturing lambdas allocate a hidden `<>c__DisplayClass` on the GC heap and alias captured locals freely—violating both zero-GC and borrow rules.
   - **Rule**:
     - `static` lambdas (`static x => x + 1`) coerce to function pointers (`delegate*<int, int>` or generic `IFn` structs) with zero allocation.
     - Stateful closures in Phase 1/2 are modeled explicitly as structs implementing `IFn<TArgs, TRet>` / `IFnMut<TArgs, TRet>` / `IFnOnce<TArgs, TRet>` (exactly what `rustc` desugars closures into!). Later, non-escaping `scoped` lambdas passed to inline iterators can be lowered directly by `vib` into stack-allocated environment structs with borrow-checked captures (`&T` if read-only, `&mut T` if mutated).
3. **Strings**:
   - C#'s `System.String` is a UTF-16 GC-allocated object. Using `System.String` directly would force UTF-16 + GC semantics into `.NET` debug runs.
   - **Solution**: Provide `Vibranium.Utf8Str` (a `readonly ref struct` wrapping `ReadOnlySpan<byte>`, constructible from **C# 11 UTF-8 string literals** `"hello"u8`!) and `Vibranium.String` (owned UTF-8 `Vec<byte>`). C# 11's native `"hello"u8` (`ReadOnlySpan<byte>`) gives zero-allocation static UTF-8 data in *both* `.NET` and LLVM!
4. **Integer Overflow & Floating Point**:
   - Fix default integer arithmetic to **wrapping two's complement** (`unchecked` in C#, standard `add`/`sub`/`mul` without `nsw` poison traps in LLVM) in release, OR **checked panic-on-overflow in debug + wrapping in release**, controlled uniformly:
     - Even better for 100% equivalence: make default `+`, `-`, `*` **wrapping** (matching C# `unchecked` default) OR **checked trap** (`<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` in `.csproj` + `llvm.sadd.with.overflow` -> `vib_panic` in LLVM), and provide explicit `CheckedAdd`, `WrappingAdd`, `SaturatingAdd`. Pick **checked trap by default** or **wrapping by default**—just match `.csproj` `<CheckForOverflowUnderflow>` with LLVM emission!
   - Shift counts: C# masks shift counts (`x << (n & 31)` for 32-bit), whereas LLVM `shl` produces `poison` if `n >= bitwidth`. **Critical trap**: `vib` MUST mask shift rhs (`and i32 %rhs, 31`) in LLVM IR to match C#'s exact semantics!
   - Signed division overflow (`int.MinValue / -1`): traps in C# (`OverflowException`), is UB in LLVM `sdiv`. `vib` must emit an explicit guard before `sdiv`/`srem` (just like `rustc` does).
5. **Bounds Checks**:
   - `Span<T>[i]` and `Vec<T>[i]` always perform unsigned bounds check `(uint)i >= (uint)len` and call `VibraniumRuntime.Panic(...)`.
   - **Equivalence of Panic**: Both `.NET` (`Environment.FailFast` or an uncatchable `VibraniumPanicException` in test harness mode) and LLVM (`vib_panic` -> message + `abort()`—with unwinding disabled, `panic = abort`) terminate deterministically without running normal recovery code. (For negative panic unit tests, `vib_panic` exits with a deterministic status code + stderr message verifiable identically in both modes.)
6. **Error Handling (`Result<T, E>` & `Option<T>`, Zero Exceptions)**:
   - `throw`, `try/catch` are **forbidden** in Vibranium source code.
   - All recoverable errors use `Result<T, E>` and `Option<T>` (`struct`s with tag + payload; layout-optimized when `T` has a non-null niche like `Box<U>`).

---

### 3. Compiler Pipeline Design

#### Unified Frontend Architecture (Shared `Vibranium.Analysis` Engine)
Building the Vibranium frontend and borrow checker in C# atop `Microsoft.CodeAnalysis.CSharp` (Roslyn) gives a **single source of truth**:

```text
                Roslyn CSharpCompilation (Syntax + SemanticModel)
                                      │
                                      ▼
                     ┌─────────────────────────────────┐
                     │       Vibranium.Frontend (C#)        │
                     │  1. Subset Gatekeeper           │
                     │     (bans class, null, throw…)  │
                     │  2. IOperation + CFG Lowering   │
                     │     ──► Vibranium MIR (Explicit      │
                     │         Places & BasicBlocks)   │
                     │  3. Lifetime & Region Inference │
                     │  4. NLL Place Borrow Checker    │
                     │  5. Move & Init Dataflow        │
                     └───────────────┬─────────────────┘
                                     │
              ┌──────────────────────┴──────────────────────┐
              ▼                                             ▼
┌───────────────────────────┐                 ┌───────────────────────────┐
│   Vibranium.Analyzer (IDE)     │                 │   vib (Native Backend)  │
│ - DiagnosticAnalyzer      │                 │ 6. MIR Drop Elaboration   │
│ - Live red squiggles in   │                 │    (conditional drop flags│
│   VS / Rider / VS Code    │                 │     + reverse scope order)│
│ - Optional MSBuild `#line`│                 │ 7. Generic Monomorphizer  │
│   Drop-Lowering for       │                 │ 8. LLVM 18 IR Emitter     │
│   `dotnet test` parity    │                 │    (`noalias`, `nocapture`│
└───────────────────────────┘                 │     `readonly`, `sret`)   │
                                              │ 9. `clang-18 -O3` ──►     │
                                              │    Standalone Native ELF  │
                                              └───────────────────────────┘
```

Why this architecture shines:
- **Zero parser/type-checker duplication**: Roslyn already resolves overload sets, generic type inference, `in`/`ref`/`scoped` modifiers, interface implementations, and constant folding.
- **Zero semantic skew between IDE and `vib`**: Every ownership, move, initialization, or borrow error (`VIB0001: Use of moved value`, `VIB0002: Cannot borrow 'x' as mutable because it is also borrowed as immutable`) is produced over `Vibranium MIR`. An Vibranium program that shows zero diagnostics in the IDE is **guaranteed** to pass `vib` frontend verification.
- **Unlocking LLVM `noalias` (Rust's secret performance weapon)**: Because Vibranium's borrow checker proves that `ref T` (`&mut T`) never aliases any other live reference or `in T` (`&T`), `vib` emits LLVM parameter attributes `noalias nocapture nonnull align N` on `ref T` / `Span<T>` pointers and `readonly nocapture nonnull` on `in T` / `ReadOnlySpan<T>` pointers. `opt-18` / `clang-18 -O3` immediately vectorizes and hoist loads that C/C++ compilers must conservatively reload!

#### Critical Subtleties in Lowering Roslyn `ControlFlowGraph` + `IOperation` to Vibranium MIR
Roslyn's `ControlFlowGraph.From(methodBody)` is a huge head start, **but** it models C# stack/IL semantics rather than Rust place semantics. You must handle **six specific traps** when lowering `IOperation` CFG into Vibranium MIR:

1. **Distinguishing Lvalue Places vs. Rvalue Loads**:
   - Roslyn represents `x.Field` as an `IFieldReferenceOperation` whether it sits on the LHS of `=`, inside `ref x.Field`, as the receiver of `x.Field.Mutate()`, or as a copied value `var y = x.Field`.
   - **MIR Rule**: Lower expressions in two explicit modes—`LowerPlace(IOperation) -> Place` `(LocalId, Projection[])` and `LowerOperand(IOperation) -> Operand::Move(Place) | Operand::Copy(Place) | Operand::Const(...)`. Never let Roslyn's implicit value-load conflate borrowing a place with moving out of a place!
2. **Implicit C# Struct Receiver Copies (Hidden `IConversionOperation` / `IInstanceReferenceOperation`)**:
   - If a struct method is *not* marked `readonly` and is called on an `in T` parameter or `readonly` local, Roslyn's `IOperation` tree subtly wraps or copies the receiver value instead of taking its address.
   - **MIR Rule**: Inspect `IInvocationOperation.Instance` alongside `IMethodSymbol.IsReadOnly`. If `!method.IsReadOnly`, require `Instance` to be a mutable `Place` and emit a two-phase mutable borrow `Borrow(Mut, place)` in MIR; if `Instance` is behind `in` / `ref readonly`, emit `VIB0012: Cannot call mutating method on shared borrow`.
3. **Two-Phase Borrows for Method Calls (`v.Push(v.Len())`)**:
   - Just like Rust (`vec.push(vec.len())`), C# evaluates the receiver reference `ref v` *before* evaluating arguments `v.Len()`. Under naive single-phase mutable borrowing, `ref v` is exclusively borrowed before `v.Len()` tries to shared-borrow `v`!
   - **MIR Rule**: Implement Rust NLL **two-phase borrows** (`BorrowKind::Mut { allow_two_phase: true }`) for implicit `ref this` receivers: reserve the borrow when taking the receiver address, and *activate* exclusivity right after argument temporaries finish evaluating.
4. **Temporary Lifetimes and `StorageLive` / `StorageDead`**:
   - Roslyn's CFG synthesizes `FlowCaptureOperation` (`CaptureId`) for short-circuiting (`&&`, `||`, `?:`), compound assignments, and temporaries.
   - **Trap**: `FlowCaptureOperation` in Roslyn is designed for definite assignment, not RAII/borrow scopes—a `FlowCaptureReference` may merge rvalues across branches without explicit end-of-statement destruction boundaries!
   - **MIR Rule**: Allocate explicit MIR temporaries (`_tmpN`) with explicit `StorageLive(_tmpN)` and statement-end `StorageDead(_tmpN)` boundaries (extending temporary lifetime only when directly bound to a `ref readonly` local, matching Rust/C# ref-extension rules). Drop Elaboration then drops `_tmpN` at `StorageDead` in exact reverse creation order!
5. **`ref` Reassignment vs. Indirect Write (`r = ref y` vs. `r = 5`)**:
   - In C# 7.3+, `r = ref y;` rebinds the reference local `r` to point to `y`, whereas `r = 5;` writes `5` *through* the reference (`*r = 5`).
   - In Roslyn `IOperation`, check `ISimpleAssignmentOperation.IsRef`:
     - `IsRef == true` $\implies$ MIR `_r = &y` (updates the points-to loan set of `_r` in NLL).
     - `IsRef == false` on a `ref` local $\implies$ MIR `(*_r) = 5` (projects a `ProjectionElem::Deref` onto `_r`!). Missing `IsRef` causes catastrophic confusion between overwriting a pointer and mutating its referent.
6. **Non-Lexical Lifetimes (NLL) Liveness & Polonius-Lite Region Constraints**:
   - A borrow `_r = &mut x` only conflicts with another use of `x` at CFG program points where `_r` (or a borrow derived from `_r`) is **live**.
   - Implement liveness-based NLL directly on Vibranium MIR:
     1. Compute standard backward variable/reference liveness over MIR CFG points `(BlockId, StatementIdx)`.
     2. Assign each reference type a region variable `'r`, initialize `'r` with the points where the variable holding `'r` is live, and propagate outlives constraints `'r1 : 'r2` along assignments, reborrows (`_r2 = &(*_r1)`), and call signatures (`[Lifetime]`).
     3. Check every `Access(Place, Read | Write | Move)` and `StorageDead(Local)` against all loans `Loan(LoanPlace, Shared | Mut, 'r)` whose region `'r` contains the current CFG point, checking prefix/suffix overlap on `Place` projections!

---

### 4. Prototype Repository Blueprint

Here is the crispest, highest-leverage vertical-slice structure to build first in the repository root on `.NET 10.0.301` + `LLVM 18`:

#### Recommended Repository & Solution Layout
```text
vibranium/
├── Vibranium.sln
├── src/
│   ├── Vibranium.Core/                 # Zero-dependency standard library (valid C# 13)
│   │   ├── Attributes.cs          # [VibraniumStrict], [Lifetime("a")], [Outlives("a","b")]
│   │   ├── Traits.cs              # ICopy, IDrop, IEquatable<T>
│   │   ├── Box.cs                 # Owned single heap allocation (NativeMemory on .NET)
│   │   ├── Vec.cs                 # Owned growable buffer + AsSpan() / AsReadOnlySpan()
│   │   ├── Option.cs              # Option<T> (allows non-Copy T!)
│   │   ├── Result.cs              # Result<T, E>
│   │   └── Runtime.cs             # Panic(), AssertEq(), AllocationTracker (leak check!)
│   ├── Vibranium.Compiler/             # Shared Frontend + MIR + NLL + Drop + Mono + LLVM 18
│   │   ├── Subset/                # SubsetValidator (bans class, null, throw, GC alloc)
│   │   ├── Mir/                   # MirBody, Place, Projection, Terminator, MirBuilder
│   │   ├── BorrowCheck/           # MoveDataflow, RegionInference, NllBorrowChecker
│   │   ├── Transforms/            # DropElaborator, Monomorphizer, CSharpDropRewriter
│   │   └── Codegen/               # Llvm18TextEmitter (clean textual .ll + ABI/noalias)
│   ├── Vibranium.Analyzer/             # Roslyn DiagnosticAnalyzer wrapping Subset + Mir + NLL
│   └── Vibranium.Cli/                  # `vib build`, `vib run`, `vib check`, `vib emit-mir`
├── runtime/
│   └── vib_rt.c                  # ~80 lines C runtime (malloc/realloc/free, panic, print, alloc counter)
└── tests/
    ├── Vibranium.BorrowTests/          # Exhaustive negative & positive NLL / move / drop unit tests
    └── Vibranium.EquivalenceTests/     # Runs identical programs on `dotnet` AND `vib`+`clang-18`,
                                   # diffing stdout, exit code, drop logs, and zero-leak counters!
```

*Engineering tip on LLVM 18 emission*: Emit well-typed **textual LLVM 18 IR (`.ll`)** (using opaque pointers `ptr`, explicit struct types `%MyStruct = type { ... }`, `noalias` attributes, and invoking `clang-18 -O3 out.ll vib_rt.c -o bin`) rather than wrestling P/Invoke `LLVMSharp` version bindings in Phase 1. Textual `.ll` in LLVM 18 with opaque `ptr` is fast, readable, zero-dependency, verifiable via `opt-18 -passes=verify`, and produces identical machine code through `clang-18 -O3`.

#### Core Vertical-Slice Primitives (`Vibranium.Core`)
1. `ICopy` and `IDrop` (`void Drop();`) marker/lifecycle interfaces.
2. `Box<T>` and `Vec<T>` backed by `NativeMemory.Alloc` / `NativeMemory.Free` on `.NET` (updating an atomic `VibraniumRuntime.LiveAllocations` counter in debug/test mode so every test asserts `LiveAllocations == 0` at exit!) and intrinsic `malloc`/`realloc`/`free` structs `{ ptr, i64, i64 }` in LLVM.
3. `Span<T>` / `ReadOnlySpan<T>` mapped in LLVM to `{ ptr, i64 }` fat-pointer slices with bounds-checked indexing.
4. `Option<T>` and `Result<T, E>` value structs with move-aware `Unwrap()`, `IsSome()`, `Match(...)`.

#### Positive Demo & Benchmark Programs to Build First
1. **Deterministic Drop & Branching Partial-Move Torture Test**:
   - Struct `TrackedResource(int id)` implementing `IDrop` (printing `"drop {id}"`).
   - Exercises nested scopes, `var b = a;` moves, `if (flag) Consume(x);` conditional drops, struct-with-owned-fields recursive drops, and early `return`s.
   - Verified to produce **byte-for-byte identical drop traces** and `0` leaked allocations on both `.NET` and native LLVM.
2. **Disjoint Struct Projection & Two-Phase Slice Processing**:
   - Mutably borrows `state.LeftBuffer` and `state.RightBuffer` simultaneously, passes `ReadOnlySpan<int>` views and `[return: Lifetime("a")] ref readonly int` projections, proving NLL + `[Lifetime]` checking works end-to-end.
3. **`noalias` Numerical / Data-Structure Microbenchmark (`Vec<double>` Stencil / Dot-Product + B-Tree or Arena Graph)**:
   - Compiles the exact same `.cs` source with `dotnet run -c Release` vs `vib -O3` (`clang-18`), inspecting emitted x86_64 AVX2/FMA loops (`llc-18`) to prove LLVM leverages Vibranium's borrow-guaranteed `noalias` attributes and zero-GC layout.

#### Must-Have Negative Borrow-Checker Test Suite (Verified as `VIBxxxx` Diagnostics)
Every single case below must be valid C# (zero `CSxxxx` errors!) that **Vibranium rejects** identically in the Roslyn Analyzer and `vib`:
1. **Use-after-move**: `var a = Box.New(42); var b = a; int x = a.Get();` $\rightarrow$ `VIB0001: Use of moved value 'a'`.
2. **Partial move of struct field**: `var p = new Pair { A = Box.New(1), B = Box.New(2) }; var a = p.A; ConsumePair(p);` $\rightarrow$ `VIB0001: Use of partially moved value 'p'`.
3. **Move inside loop**: Moving an outer `Vec<int>` inside a `while`/`for` body $\rightarrow$ rejected by cyclic CFG move dataflow on loop back-edge.
4. **Shared + Exclusive borrow conflict**: `ref readonly int r = ref v[0]; v.Push(99); Print(r);` $\rightarrow$ `VIB0002: Cannot borrow 'v' as mutable while borrowed as immutable` (classic vector reallocation invalidation!). Note: if `Print(r)` is *removed*, NLL **allows** `v.Push(99)` because `r` is dead!
5. **Double mutable borrow (overlapping place)**: `ref int a = ref pair.X; ref int b = ref pair.X; a = 1; b = 2;` $\rightarrow$ `VIB0003: Second mutable borrow of 'pair.X'` (while `pair.X` and `pair.Y` **succeeds**!).
6. **Dangling borrow / Lifetime escape**: Returning `Span<int>` or `ref int` pointing to a local owned `Vec<int>` dropped at function exit (`return v.AsSpan();`) $\rightarrow$ `VIB0004: 'v' does not live long enough (dropped while borrowed)`.
7. **Lifetime mismatch across `[Lifetime]` parameters**: `[return: Lifetime("a")] ref readonly int Bad([Lifetime("a")] in int x, [Lifetime("b")] in int y) => ref y;` $\rightarrow$ `VIB0005: Lifetime 'b' does not outlive 'a'`.
8. **Mutating through shared borrow (`in T`)**: Calling a non-`readonly` struct method on `in MyStruct s` $\rightarrow$ `VIB0006: Cannot call mutating method on shared borrow` (catching C#'s silent defensive copy hazard!).
9. **Move while borrowed**: `var b = Box.New(10); ref readonly int r = ref b.AsRef(); var b2 = b; Print(r);` $\rightarrow$ `VIB0007: Cannot move out of 'b' because it is borrowed`.
10. **Subset violations**: Declaring `class Foo`, `throw new Exception()`, `null`, or capturing GC closure $\rightarrow$ `VIB0100+` subset errors.