Vibranium is feasible as a deliberately restricted, ownership-checked C# dialect, but the literal combination of unchanged ordinary C# execution and arbitrary implicit Rust-style destruction is not feasible. The decisive design choice is the managed ownership/drop protocol, not LLVM code generation. No repository files were created or modified during this initial feasibility review.

## 1. Feasibility and the central trade-off

My recommendation: keep the strict C# subset promise, but make ownership transfer and cleanup registration explicit in source for version 0. Use the same semantic checker in the analyzer and native compiler. Do not claim automatic Rust semantics for arbitrary C# assignments.

The useful product is an embedded systems language with C# tooling: static typing, familiar syntax, existing project navigation, diagnostics, refactoring, debugging, and test runners; a small, closed-world library surface; Rust-like ownership; and a native backend. The existing compiler handles parsing, overload resolution, constant evaluation, generic binding, and ordinary C# diagnostics. The main investment then goes into semantics rather than rebuilding an editor ecosystem.

The costs are substantial:
- C# syntax is not C# semantics. Every accepted construct needs a precise native interpretation, including compiler-inserted copies, conversions, temporaries, and exception paths.
- .NET libraries do not automatically become native Vibranium libraries. Every reachable call must be checked Vibranium code or an audited intrinsic/adapter with explicit ownership, escape, error, and thread effects.
- The CLR will often prevent a bad native lifetime from visibly failing. Managed/native differential tests are necessary but insufficient; native sanitizer tests and checker adversarial tests are essential.
- A mature sound borrow checker, container library, optimizer interface, and portable ABI implementation are a serious compiler project. Six demos establish feasibility, not Rust-grade soundness.

### Why not NativeAOT?

NativeAOT already provides a very good managed-debug/native-release workflow and should be a benchmark baseline, not a straw man. It removes the JIT and produces a self-contained executable; it does not remove the garbage collector or the managed object/type model. Microsoft explicitly documents the statically linked NativeAOT runtime and its GC. [Sources: NativeAOT overview and runtime documentation.]

The differences relevant to Vibranium are:
- Managed heap objects still carry runtime metadata. NativeAOT's .NET 10 source has an object header and an EEType/method-table pointer; on x64 these are normally two pointer-sized components before user payload, before minimum-size/alignment effects. Inline structs are not individually GC objects and should not be accused of paying this overhead.
- Managed reference stores can require GC write barriers; NativeAOT has explicit write-barrier machinery. Not every store needs one: unmanaged data, many stack stores, and eliminated stores are important exceptions.
- AOT compilation does not establish single ownership or exclusivity. C# `ref` does not by itself promise that another parameter cannot alias it; NativeAOT cannot assume Rust-like exclusivity merely from the source spelling. This follows from the ordinary reference-parameter semantics.
- The residual runtime includes GC/type-system support even without a separately installed .NET runtime. “No runtime installation required” is not “no runtime in the executable.”

Vibranium can instead choose payload-only native representations, explicit ownership, monomorphized generic code, and a tiny allocator/I/O support layer. However, “zero CLR and zero GC” must not be advertised as literally zero support code: allocation, OS calls, bounds failures, and startup still require implementation.

## 2. Solve dual semantics before writing the backend

Define the actual equivalence claim: for accepted closed-world programs, identical inputs and external-operation results produce identical values, byte output, and observable resource/drop events. Exclude implementation observations such as addresses, object identity, reflection, GC metrics, allocation timing, and stack traces. Treat resource exhaustion, process termination, and concurrency scheduling explicitly rather than pretending that different runtimes have identical physical behavior.

### Moves and hidden C# copies

A shallow C# copy can represent a semantic move. After `b = a`, if the checker makes `a` inaccessible, forbids hidden aliases, and ensures only `b` owns destruction, neither runtime need observe a difference. A move need not physically erase source bytes.

The last condition is the hard one. `using var a = MakeOwner(); return a;` cannot mean a Rust move: C# will dispose its registered resource before returning. Making `Dispose` idempotent prevents duplicate disposal, not premature disposal of the returned owner. Likewise, a managed tracking cell cannot distinguish an uninstrumented assignment from no assignment.

C# `using` variables are readonly, so the tempting `using var a = ...; Own.Move(ref a)` is not valid C#. Source generators cannot fix this by inserting moves or `finally` blocks into existing method bodies: they add code rather than rewrite existing user code.

Recommended version-0 source protocol:

    var a = Box<int>.New(7);
    using var aScope = Own.Guard(ref a);
    var b = Own.Move(ref a);
    using var bScope = Own.Guard(ref b);
    return Own.Move(ref b);

Here:
- `Move(ref place)` returns the owner and changes the actual source place to a private empty/tombstone state.
- `Guard(ref place)` returns a trusted ref-struct guard containing a reference to the place, not a copy of its current payload. Disposal consults the place's current state.
- Ordinary reads of a tombstone remain compile-time errors. The tombstone exists for the shim's cleanup protocol, not as an exposed nullable owner.
- A guard is a special compiler-recognized cleanup obligation, not an ordinary user-accessible exclusive borrow lasting to scope exit. User code cannot copy, return, manually invoke, or otherwise inspect it.
- Require adjacent, nonthrowing guard registration after every owning local initialization, and a guard prologue for owning by-value parameters. Reject unregistered owning temporaries and discarded owner-return values initially.
- Ordinary assignment of a non-Copy value is rejected initially. Ownership transfer is `Move`; overwriting an initialized owner is an explicit `Replace(ref destination, newValue)` operation that evaluates the new value first and then drops the old one. Clone is explicit and deep where required.

The necessary C# building blocks are real: ref structs can contain ref fields, and disposable ref structs work with `using`. The proposed guard still needs a compile-only spike to settle exact signatures and escape rules.

This protocol is less beautiful than Rust syntax but is honest, debuggable, and implementable without rewriting source. Add an IDE code fix to insert the guard; do not confuse a code fix with a generator secretly changing semantics.

Alternative policies must be labeled accurately:
1. Ordinary `using` only, with nontransferable scoped resources: simplest, but not a general ownership language.
2. Pure reclamation-only Drop, with managed GC reclaiming later: viable for an abstract-memory subset, but not general RAII resource semantics.
3. Source/IL rewriting to insert all moves and drops: more ergonomic, but relinquishes the “unchanged Roslyn executes the source” promise and introduces source-map/debugging work.

### Owned representations and user structs

Separate compiler-derived Copy types from owning types:
- Copy: primitive numeric/bool values and structural aggregates containing only Copy fields, subject to the chosen validity rules.
- Non-Copy: Box, Buffer, Vec, OS handles, and any struct containing one of these.
- Borrow-only: refs and slice views, with mutable views affine and readonly views shareable.

Use ordinary structs as owning handles, not unconstrained user classes. A managed implementation may hold a private `Cell<T>` reference and a validity/generation token; the native implementation may be a pointer plus lengths/capacity. A logical drop must destroy the payload and release external resources at the specified point even if the managed cell's storage is collected later. No identity or GC observation may escape the abstraction. An alternative first implementation uses NativeMemory-backed payloads for a restricted unmanaged/Copy T; this gives deterministic physical free in the shim while the hosting CLR still has a GC.

For structs with owning fields, a source generator can add `Dispose`/drop glue to partial structs. It is adding a member, not rewriting an existing body. Specify field order, active-union-variant behavior, and exactly-once custom OnDrop semantics. A practical order is user OnDrop followed by fields in declaration order; local scopes unwind in reverse guard order. Rust itself distinguishes field declaration order from reverse local declaration order.

Initially disallow partial moves and self-referential/pinned structures. Do not expose unrestricted `default(T)`, null, constructors producing invalid ownership states, arrays of uninitialized owners, owner-valued property copies, boxing, owner equality, owner-valued foreach copies, or arbitrary struct methods with implicit defensive copies. C# readonly receiver contexts can introduce defensive copies; this is an actual semantic hazard for ownership handles.

### Deterministic Drop

Native drop elaboration must implement the *same source-visible protocol* as the .NET shim, not invent extra destruction points.

- Scope exit/return/break/continue: cleanup comes from registered guards.
- Explicit early drop: intrinsic clears the place and invokes drop glue.
- Overwrite: explicit Replace performs the specified old-value destruction.
- Move: clears ownership responsibility without calling Drop.
- Conditional move: guard sees the tombstone on .NET; native cleanup uses a corresponding drop flag where needed.
- Destructors may have specified observable effects, but must be nonthrowing and must not resurrect/escape owned storage.

NLL ends borrows at last relevant use; it does NOT imply that a resource may be observably dropped at its last use. Keep destruction at specified cleanup points.

A runtime live-handle registry can detect double disposal or invalid access during debugging, but it is a diagnostic backstop, not the source of the compile-time safety guarantee.

### Borrows and lifetimes

Suggested surface:
- `in T` / `ref readonly T` / `ReadOnlySpan<T>`: shared borrow.
- `ref T` / `Span<T>`: exclusive borrow, or an explicit reborrow from an existing exclusive view.
- `scoped` for nonescaping parameters/locals where C# permits it.
- Ref returns allowed only with compiler-checked provenance. Infer the single-input case; require lifetime/provenance attributes when multiple input roots exist.

Vibranium must add restrictions that C# does not have. A readonly reference is not deep immutability of reachable reference objects; likewise Span is a view, not an exclusivity proof. Enforce transitive shared immutability and unique mutable access over the owned region, not just the handle's fields. C# explicitly distinguishes readonly references from immutable referenced objects.

Reborrowing suspends conflicting use of the parent loan until the child loan ends. The analysis needs places such as `x.field`, `*owner`, `slice[index]`; independent nonoverlapping fields can be borrowed separately. Dynamic indices conservatively overlap unless an audited SplitAtMut intrinsic proves disjoint regions. This deliberately follows the distinction between field splitting and slice splitting in Rust.

A lifetime attribute is additional information for Vibranium, not a way around Roslyn escape errors. Every accepted ref-return/borrow-struct pattern must also compile under C#'s ref-safety rules. Reject safe-but-unexpressible patterns rather than lying about strict subset compatibility.

### Other semantic choices

- Closures: ban captures in version 0. Later use explicit environment structs and static functions, or accept only demonstrably compatible captures. Ordinary C# captured-variable behavior must not silently become Rust move-capture behavior. Ban async/yield initially.
- Strings: choose one representation. C# strings have UTF-16 semantics, so silently compiling their indexing/Length as UTF-8 is wrong. A clean first subset uses static UTF-8 byte literals (`"text"u8`) and Vibranium byte-string/slice APIs, with owned text added later. Do not expose culture-sensitive formatting or nondeterministic hashing.
- Arrays/slices: use Buffer/Vec plus Span views; no arbitrary mutable C# array sharing. Zero-initialize where the managed operation does. Restrict element types to Copy initially, then add element-wise move/drop initialization tracking.
- Generics: check generic bodies against explicit structural capabilities; do not infer Copy merely from `struct` or `unmanaged`. Native code monomorphizes reachable closed instantiations. Avoid boxing, reflection, virtual dispatch, and unconstrained dynamic operations. A generic result's drop behavior belongs in the generic contract.
- Arithmetic: pin overflow settings identically across managed Debug and Release. Choose wrapping arithmetic for ordinary integer operations and explicit checked/result APIs, or implement checked failure paths. .NET integer division has exceptional cases even in unchecked contexts. Preserve C# evaluation order and conversions.
- LLVM arithmetic: wrapping addition must not acquire `nsw`/`nuw`; mask shift counts as required by the language; guard zero and MIN/-1 before signed division/remainder. LLVM 18 gives undefined behavior to exceptional integer division/remainder cases. Avoid fast-math until floating semantics are specified.
- Errors: prefer Result<T,E>. Banning user `throw` does NOT remove built-in CLR exceptions from indexing, arithmetic, or allocation. Native abort and managed unwinding are not equivalent if drops are observable.

For the first prototype, use Result-returning indexing/division/allocation APIs and reject raw potentially-failing operations not yet supported. For the full language, implement a defined internal panic protocol: a generated managed entry/test boundary catches the chosen panic category; native code propagates an explicit failure status through cleanup edges (or implements native unwinding); both produce the same resource trace and normalized terminal result. Do not rely on an arbitrary unhandled CLR exception to supply the language's cleanup contract.

### Threads

Version 0 should reject threads, Tasks, unmanaged callbacks, and mutable globals. This establishes a single-threaded safety subset, not a demonstration of a concurrent language.

Later introduce compiler-derived Send/Sync-like capabilities, scoped joins, ownership transfer to workers, and audited Mutex/Atomic APIs. User-written marker interfaces must not be sufficient to assert thread safety. Rust's Send/Sync contracts are unsafe to implement incorrectly and incorporate interior-mutability rules.

## 3. Compiler architecture

Yes: implement the frontend and checker in C# and reuse the exact core library from DiagnosticAnalyzer and CLI. Neither the analyzer nor compiler should contain a separate ad-hoc version of ownership analysis.

Recommended dependency shape:

    Roslyn compilation + symbols + operations
      -> subset/effect validator
      -> typed HIR
      -> place-based Vibranium MIR + cleanup obligations
      -> initialization/move/loan/lifetime checks
      -> checked MIR
      -> drop elaboration
      -> monomorphization + native layouts
      -> simple LLVM IR
      -> LLVM optimization/codegen/link

Roslyn provides ControlFlowGraph.Create overloads for method/constructor bodies and other executable roots. Respect the root-operation requirement rather than attempting to create CFGs from arbitrary nested operations. Use its bound operations and CFG as input, not as the ownership IR itself.

### Specific stages

1. Closed-world subset validation
   Whitelist syntax, bound operation kinds, types, conversions, intrinsics, attributes, and effects. Unknown operation/symbol means reject, not silently ignore. Validate generated code too, except a narrowly identified trusted-runtime boundary. Pin language version, constants, target architecture, nullable/overflow settings, and references for both paths.

2. HIR normalization
   Preserve overload-resolved calls and left-to-right evaluation. Make every hidden conversion/copy/temporary explicit. Normalize short-circuit expressions, pattern tests, loops, calls, return temporaries, and cleanup scopes. Reject unimplemented constructs before lowering.

3. Vibranium MIR
   Keep mutable storage identity until after checking. Suggested objects: LocalId, Place(root, projections), MovePath, TypeId, RegionId, LoanId, SourceSpan. Instructions include StorageLive/Dead, Read/Copy/Move, Initialize, BorrowShared/Exclusive, Reborrow, Replace, DropObligation, Call, bounds/error checks. Terminators include branch, switch, return, and explicit cleanup/failure successors. Calls carry owned/shared/exclusive and escape effects.

4. Sound dataflow checker
   Forward definite/maybe initialization and move-path analysis; backward value liveness; reference-origin and region constraints; fixed-point loan propagation across joins and backedges. Track borrowed references through assignments and phi-like joins, not merely the lexical variable initially holding a ref. Enforce overlap conflicts, owner lifetime, reborrows, destructor uses, and result provenance. Start conservatively; rejection of a safe program is acceptable, acceptance of an unsafe program is not. MIR-based move analysis and region inference are established architecture in rustc.

   A “Polonius-lite” label is not a design. Specify facts such as loan-issued-at, origin-contains-loan, origin-subset, loan-killed-at, and origin-live-on-entry, and test loop/branch propagation. Do not approximate NLL by taking the last textual use.

5. Drop elaboration
   The checker sees potential cleanup obligations first, including destructor reads; after checking, elaborate them into concrete drop calls and conditional flags. Keep flags only when initialization is path-dependent. Rust's drop elaboration uses initializedness information to distinguish unconditional, dead, conditional, and partially initialized drops.

   For the strict dual path, audit that each native obligation corresponds to source guard/Replace/Drop behavior. The backend may optimize invisible bookkeeping, not observable destruction order.

6. Monomorphization and layouts
   Instantiate reachable functions, structs, and recursive drop glue; detect unbounded generic expansion. Establish target sizes, alignment, padding, variant layouts, and checked allocation-size multiplication. Keep managed layout out of the native ABI contract.

7. LLVM 18 emission
   Start with textual IR and explicit stack slots, then let LLVM promote suitable storage; do not build a sophisticated SSA constructor before semantics work. Emit a fixed x86_64 Linux target first. Verify IR before and after optimization, then compile/link with LLVM 18 tools.

   Attribute policy: `noalias` only where the full relevant access contract is proved; `nocapture` only when the argument does not escape, including by return; `sret` for the chosen aggregate-return ABI; alignment/non-null/dereferenceability only when established. A read-only borrow is not automatically a unique borrow. Treat attributes as proof obligations, not decorative optimization flags. These are LLVM 18 semantics; do not copy newer `captures(...)` syntax.

   Use an internal ABI you control. Initially expose only scalar/pointer/length C-ABI runtime calls. General aggregate interoperability needs target ABI classification; `sret` alone is not an ABI implementation. Consider local alias-scope metadata only after parameter-level contracts are tested.

### IDE and build guarantees

The analyzer should be cancellable, function-incremental, deterministic, and cache summaries by syntax/semantic dependencies. It should issue source-located diagnostics with the origin of the move/loan and the conflicting later use. Do not run LLVM inside the IDE.

IDE analyzers can be disabled or diagnostics suppressed. Therefore the authoritative CLI/MSBuild gate must run the core checker independently and refuse code generation on any safety failure. An absent squiggle is not a safety certificate. Cross-module libraries need checked ownership/effect summaries or rechecking, not arbitrary C# DLL trust.

## 4. Concrete prototype proposal

Suggested layout, to be created later by the implementation agent:

    Vibranium.sln
    global.json / Directory.Build.props
    docs/semantics-v0.md
    src/Vibranium.Compiler.Core/       types, HIR/MIR, analyses, diagnostics
    src/Vibranium.Frontend.Roslyn/     symbols, subset validator, MIR lowering
    src/Vibranium.Analyzers/           thin live-analysis adapter
    src/Vibranium.Generators/          additive drop glue / entry adapters
    src/Vibranium.Runtime/             managed shim and intrinsic contracts
    src/Vibranium.Backend.Llvm/        layout, monomorphization, LLVM18 text
    src/Vibranium.Cli/                 check, dump-mir, emit-llvm, build
    native/vib_rt.c              alloc/free, byte I/O, trace helpers
    tests/Vibranium.Compiler.Tests/    MIR/analysis goldens and API tests
    tests/Vibranium.Differential/      managed/native trace comparison
    tests/negative/              ordinary-C#-valid Vibranium-invalid cases
    examples/                    six small programs

Pin the provided .NET SDK 10.0.301 and LLVM 18 instead of chasing the newest toolchain. For IDE loading, choose the analyzer's compatible target/Roslyn dependency baseline after checking the actual installed hosts; keep LLVM and heavy build dependencies out of the analyzer load path.

### Runtime primitives

- Own.Move, Own.Drop, Own.Replace, Own.Guard.
- Box<T>, Buffer<T>, Vec<T>, initially Copy-element payloads; composites with owned fields next.
- Shared/exclusive borrowing accessors and Span/ReadOnlySpan adapters, plus audited SplitAtMut later.
- Option/Result with validated discriminants and explicit payload moves.
- Nonthrowing trace/resource-handle primitive, deterministic byte I/O, allocation failure results.
- Generated field-wise drop glue and a documented managed dispatch mechanism for generic payload drops. Start with restricted payloads if generic drop dispatch would otherwise obscure the first milestone.

Match intrinsics by resolved assembly/type/member identity, never by method name alone. Runtime implementation code may use unsafe C#/native pointers; application Vibranium may not. This runtime is part of the trusted computing base.

### Six demonstrations

1. Move + resource escape: create an owned resource, register its guard, move through a function, return it to an outer scope, and drop there. Trace must prove no premature source drop and exactly one final drop. Include owning parameter cleanup and early return.

2. Drop control flow and owned fields: a composite with two owned fields; nested scopes; conditional move; explicit replacement; break/continue/return. Compare the entire drop trace, including field order and the unchanged owner when producing the replacement returns an error.

3. NLL and reborrowing: several shared borrows, then mutation after their last use; exclusive reborrow; disjoint field borrows. Include a branch and loop so a syntax-only checker cannot pass the demo.

4. Buffer/Vec: append, grow, mutate through a temporary exclusive slice, then resume owner access. Growth while a view is live must fail. Verify initialized-element tracking, allocation-size overflow handling, element values, and final allocation balance.

5. Generic native kernel: a generic function instantiated at two concrete Copy types, plus a nonaliasing integer slice kernel. Assert distinct monomorphized functions and expected parameter contracts; compare outputs against managed execution and a small C/Rust reference. Report measured performance, not a universal C-speed claim.

6. Result and arithmetic boundaries: parse static byte text into integers with explicit errors; exercise wrapping overflow, checked-result operations, division corner cases, and bounds failure. Ensure error paths preserve the specified drop trace. If raw panic support is deferred, these examples must use Result APIs rather than quietly permitting exception mismatches.

### Negative safety suite

All key negative samples should pass Roslyn's ordinary C# compilation but fail Vibranium, proving added semantics rather than merely reusing C# errors:
- use-after-move, move twice, ignored owned return;
- two overlapping mutable refs; mutable plus live shared borrow;
- owner move/drop/replacement or Vec growth during a live loan;
- borrower escaping through a return/field/result/closure;
- branch-only initialization and loop-carried move;
- lost borrower origin after ref assignment/join;
- ordinary non-Copy struct copy, owner-valued getter/foreach, boxing;
- invalid default/null construction or wrong Result variant access;
- user access to a cleanup guard;
- unrecognized BCL calls/unsafe APIs/reflection;
- threading and mutable globals in the single-threaded profile.

Keep a separate set for C#-invalid constructs so it is clear which compiler rejected what.

### Acceptance gates

- Same source files build/run/test using ordinary .NET plus shim/analyzer, with genuine PDB stepping through user code.
- Analyzer and CLI diagnostics agree on identifiers, locations, and explanations.
- Managed Debug, managed optimized Release, native unoptimized, and native optimized runs agree on values, output bytes, exit/result category, and resource traces across randomized inputs.
- LLVM verification passes; native sanitizer/allocator audits catch no invalid accesses or outstanding owned allocations in the chosen terminating examples.
- Native binary dependencies and symbols establish no CLR/GC dependency; report any libc/native support dependency honestly.
- Tests mutate valid programs into ownership violations and ensure rejection. Fuzz MIR joins, loops, and cleanup edges before claiming safety maturity.

Bottom line: build the ownership/guard shim and two move/drop demonstrations first. If their C# execution and native trace cannot be aligned cleanly, stop and revise the surface language before implementing Vec, NLL optimizations, or LLVM noalias. That experiment determines whether Vibranium is a coherent language or merely two different programs sharing C# syntax.