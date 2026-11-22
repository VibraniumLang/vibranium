> **Status: decided plan.** Every decision in section 10 is settled, and this plan replaces the milestone order in [`ROADMAP.md`](ROADMAP.md) from v0.3 on. Its budgets, sizes and costs are estimates, not measurements.

# Vibranium: a foundations roadmap to a stable 1.0

*Planning proposal, 17 November 2026, revision 3. Every decision in section 10 is now settled; none is left open. Nothing in the repository was changed, built or run for this plan. Facts marked [n] come from the sources at the end. Budgets, sizes and cost figures are estimates, not measurements.*

## 1. Summary

**The short version.** The target is a stable language release, 1.0, whose foundations let other people build an ASP.NET-style backend and a Blazor-WebAssembly-style frontend, and which already contains every platform-independent foundation a UI or game framework needs. Vibranium builds none of those frameworks. Each goal is covered by a **possibility proof**: a sample of a few hundred lines that compiles and runs on .NET and natively. macOS, Windows and the native-UI and game proofs follow in 1.1 as additive releases.

v0.2 cannot yet support any of this, because of four gaps, none of which is async or WASM:

- there are no user-defined traits (interfaces are banned except `IDisposable`);
- collections hold only plain values (`Vec<T>` requires `unmanaged`), so there is no `Vec<StringBuf>` or generic map;
- a struct or class cannot own a socket or file (linear resources are banned from fields);
- there is no way to share a library.

| Milestone | Theme | Delivers | Proof | Size |
|---|---|---|---|---|
| v0.3 | Toolchain | `vib install / run / watch / build`, the .NET SDK obtained through dotnetup, NuGet packages (DLL plus source), one bounded build cache, agent-friendly diagnostics, compile-time and agent-loop budgets gated in CI | | 1 |
| v0.4 | Abstraction | traits, collections of owned values, user unions, function values, string formatting | | 2.5 |
| v0.5 | Resources and the outside world | types that own resources, shared state, FFI packages, code generators, borrowing closures, Linux ARM64 | | 2 |
| v0.6 | Browser target | `wasm32`, host imports and exports | P2, first form | 1.5 |
| v0.7 | Threads, dynamic dispatch and panic containment | threads with compile-time race safety, owning closures, interface objects, `TypeId`, contained panics with cleanup | | 4.5 |
| v0.8 | Async | `async`/`await` on a work-stealing executor, async standard library; then the language feature freeze | P1; P2 with `await` | 3.5 |
| v0.9 | Interop and numerics | structs by value in FFI, vector math, native debug info, arenas | two headless samples | 1.5 |
| v1.0 | Stabilisation | numbered spec and freeze, API review, conformance suite, fuzzer soak, release candidates | P1 and P2 re-run | 2.5 |
| 1.1 | Platforms, after 1.0 | macOS and Windows | P3 window, P4 game loop | about 2.5, outside the total |

Sizes are relative to v0.3 = 1. The total to 1.0 is **19 units**, up from 16.5 in revision 2: the work-stealing executor adds about 1, panic containment adds about 2 plus 0.5 of extra stabilisation, and moving macOS, Windows and proofs P3 and P4 to 1.1 removes about 1. P2 lands in its first form after 7 units and P1 after 15.

**Order.** The browser target needs neither threads nor panic containment, so it goes first and P2 lands after 7 units instead of 15. Threads and containment then come before async, because a work-stealing executor needs `Send` checking and task boundaries are where panics are contained.

**Five findings shape the plan.**

1. **No goal needs class inheritance.** Frameworks need traits, unions and a way to call code chosen at run time. The proofs can be written with `static` lambdas that receive their state as a parameter, such as `static (ref Counter c) => c.Count++`, where `in` means read access and `ref` means write access. Stored closures and interface-typed values still arrive before 1.0 (v0.7), because framework authors will choose their own designs.
2. **Async keeps C#'s model.** A task is never silently dropped mid-flight: cancellation is an error value returned from an `await`, and a task ends early only through a contained panic, which runs cleanup first. Because the executor is work-stealing, every value live across an `await` in a spawned task must be `Send`.
3. **Panics are contained without an unwinder.** A hidden panic-return path, checked after calls, keeps binaries libc-only with zero authored C and works the same on `wasm32` (section 3).
4. **The edit loop stays on .NET**, so hot reload, debugging and incremental builds come from standard .NET tooling and `vib` stays a thin front end. Native compile time, a bounded shared cache and a fast check loop for coding agents are release criteria in v0.3, with numbers.
5. **"Stable" is mostly a promise about the accepted language.** What 1.0 accepts keeps compiling and behaving the same in every 1.x; the subset may grow but never shrink; only a soundness fix may reject code that was accepted.

**Decisions.** All fifteen are settled and recorded in section 10. Two went against my recommendation, the work-stealing executor (D3) and panic containment (D4); their costs and consequences are stated there and in sections 9 and 11.

## 2. Ground rules and where v0.2 stands

The plan keeps every decision already made: pure C# with identical behaviour on .NET 11, no exceptions, no class inheritance, a fail-closed subset, Linux first, and fuzzer productions with every feature.

Each language feature must land in six places before it counts: the spec, the Roslyn front end and analyzer, the self-hosted compiler, differential tests, fuzzer productions, and (for ownership rules) the Lean model. That multiplier, not code volume, sets the pace. Decision D1 keeps full parity between the two compilers at every release, so every size in this plan counts both.

Two facts about today's Stage-0 compiler matter for the toolchain work. I read them in `src/vib/VibraniumCompilerDriver.cs` and `src/Vibranium.Frontend.Roslyn/VibraniumSymbolHelpers.cs`:

- It merges the standard library's source and the app's source into **one** Roslyn compilation and re-parses the library on every run. In development they are separate assemblies. Once third-party packages exist, that difference can change which overload C# picks (an `internal` member is visible in one world and not the other).
- The analyzer recognises library value types by the assembly **name** `Vibranium.Std`. Third-party packages need a general mechanism.

## 3. Shared foundations, in dependency order

"Compiler" means the feature needs work in both compilers and the analyzer. "Library" means ordinary Vibranium code. "Needed by" lists the goals: 1 backend, 2 browser, 3 native UI, 4 games.

| # | Foundation | What it is | Where | Needed by | When |
|---|---|---|---|---|---|
| F0 | Toolchain and packages | Share libraries; fast, bounded builds (section 4) | Tooling, compiler driver | everything | v0.3 |
| F1 | Text | `$"..."`, number and float formatting and parsing, `out err` reuse (already planned) | Compiler + library | all | v0.3–v0.4 |
| F2 | Traits | User interfaces as generic constraints, static abstract members, default methods | Compiler | all | v0.4 |
| F3 | Owned collections | `Vec<T>` for any `T`, a generic `Map<K, V>`, user `foreach` enumerators | Compiler + library | all | v0.4 |
| F4 | User unions | User `[Union]` types with exhaustive `switch` | Compiler | all | v0.4 |
| F5 | Function values | `static` lambdas, method groups, user delegate types with `in`/`ref` parameters | Compiler | all | v0.4 |
| F6 | Resource-owning types | A class or struct may own linear resources and must release them in `Dispose` | Compiler, Lean | 1, 3, 4 | v0.5 |
| F7 | Shared state | `Rc<T>`, `Weak<T>`, `RefCell<T>`, a slot-map arena with generation-checked handles | Compiler intrinsics | 1, 2, 3 | v0.5 |
| F8 | FFI packages | Call system libraries from trusted binding packages; callbacks; C layouts | Compiler | 1, 3, 4 | v0.5, v0.9 |
| F9 | Code generators | Roslyn source generators for JSON and similar; output is checked like user code | Tooling | 1, 2 | v0.5 |
| F10 | Borrowing closures | Lambdas that capture locals but cannot outlive the call (`.Where(x => x > limit)`) | Compiler, Lean | ergonomics | v0.5 |
| F12 | WASM target | `wasm32`, allocator, JS imports and exports | Compiler + runtime IR | 2 | v0.6 |
| F13 | Threads | Threads, `Arc`, `Mutex`, channels, compiler-derived `Send`/`Sync` | Compiler, library | 1, 3, 4 | v0.7 |
| F14 | Owning closures, interface objects, `TypeId` | Stored callbacks; interface-typed values for classes; checked downcast | Compiler | 3, 4 | v0.7 |
| F16 | Panic containment | A panicking task ends alone, with cleanup, at a runtime boundary | Compiler + runtime | 1, 2 | v0.7 |
| F11 | Async | `async`/`await`, `Task<T>`, a work-stealing executor, `Memory<T>`, timers | Compiler + library | 1, 2 | v0.8 |
| F15 | Interop, numerics, platforms | Structs by value in FFI, vector math, native debug info (v0.9); macOS and Windows (1.1) | Compiler + runtime IR | 3, 4 | v0.9, 1.1 |

Design notes, where pure C# and ownership pull in different directions:

**Traits (F2).** Interfaces are used only as generic constraints, so dispatch is static and monomorphised, as generics are today. Two rules keep .NET and native identical. First, when a struct does not override a default interface method, .NET runs it on a boxed copy and loses any mutation, so default methods borrow `this` as shared and mutating helpers are generic static methods taking `ref T`. Second, generic bodies stay checked once at the definition, so a dependency never produces a diagnostic in a consumer's build; that is what makes per-package caching sound. C# lets a type implement an interface only in its own declaration, so there is no orphan-rule problem. Prefer BCL trait names such as `IEquatable<T>`, `IComparable<T>` and `IUtf8SpanFormattable`.

**Error propagation (F1, D2).** C# through version 15 has no equivalent of Rust's `?`, and I found no scheduled proposal for one (not exhaustively checked). The `await`-as-`?` trick was rejected in the project's research notes, and the reasons still hold. The one-line guard stays as today's form, with `out err` reuse, automatic error conversion on `return err` and an IDE fix that writes it. The search for a shorter form continues until the feature freeze at v0.8. A candidate must be valid C#, add no hidden control flow on .NET and keep `using` cleanup exact; if none qualifies by then, the guard is the 1.0 form.

**Function values and closures (F5, F10, F14).** C# lambdas capture variables by reference into a hidden heap object. Three tiers keep that compatible with ownership:

1. *Static lambdas and method groups* become plain function pointers. User delegate types are needed because `Func<>` cannot express `in` or `ref` parameters.
2. *Borrowing closures* may capture locals but may only be passed to a parameter marked non-escaping. Captures are borrows that last for the call, like `ref` and `in` arguments today.
3. *Owning closures* may be stored. A captured owned value moves into the closure, and a captured plain value must not be reassigned after capture. Under that rule C#'s capture-by-reference and native capture-by-value cannot be told apart.

**Shared state (F7).** These types are the GC-free substitute for object graphs with back-pointers. `Rc<T>` must be non-copyable with an explicit `.Clone()`, because native code has to count references and a silent C# struct copy would not.

**Async (F11, D3).**

- C# already forbids `ref`, `in` and `out` parameters on async methods, and spans held across an `await`. The async-safe borrow is the BCL's own `Memory<T>`: `await stream.ReadAsync(buf.AsMemory())`. The returned `Task` carries the loan until awaited, and a task holding a loan cannot be stored or spawned.
- An async call runs immediately up to its first real suspension, as in C#. Frames live on the heap and never move, so borrows of frame-local owners across `await` are sound without Rust's `Pin`.
- A task is never silently dropped. Cancellation and time-outs arrive as `Err(Cancelled)` or `Err(TimedOut)` from the pending `await`, and normal control flow runs every `using`. A task ends early only through a contained panic, which runs cleanup first.
- **Work-stealing executor.** A spawned task may move between worker threads at any `await`, so every value live across an `await` in a spawned task must be `Send`; the compiler checks this from the async frame. `Rc` and `RefCell` therefore cannot cross an `await` in a spawned task, while `Arc` and `Mutex` can. A thread-pinned local spawn covers non-`Send` state, and it is the only mode on `wasm32`, which has no threads.
- On .NET a Vibranium scheduler mirrors the native one. With several workers, interleaving is unspecified in both worlds. A simulated reactor with a seeded schedule gives reproducible runs for differential tests and the fuzzer.
- The reactor is readiness-based (`epoll`). When a task fails, its pending operations are deregistered before their buffers are freed, which is sound for readiness I/O. Completion-based I/O is where Rust needs owned buffers [9], so `io_uring` stays out of 1.0.
- Native lowering uses LLVM coroutines, as `docs/ROADMAP.md` proposes, after a spike under `-O3` with `noalias` and under ASan. The fallback is a state-machine transform in Vibranium's own MIR. Nothing depends on .NET 11's runtime async, which is still opt-in for application code [1].

**FFI (F8).** On .NET a binding is an ordinary P/Invoke to the same C function, so both worlds call identical code. The first version allows scalars, pointers and pointers to structs, which are the same on every ABI; small structs by value follow in v0.9. `[Native]` stays fail-closed: only packages flagged as trusted may declare it, and an app opts in to each by name. Bindings are generated from machine-readable API descriptions, not parsed from C headers.

**Code generators (F9).** These are ordinary Roslyn incremental generators. `vib build` hosts Roslyn, so it runs them too, and packages ship their generated code as source. Routing and parameter binding need no generator: static abstract trait members (`TBody : IJsonRead<TBody>`) do it without reflection.

**Threads (F13).** `Send` and `Sync` are derived by the compiler from a type's fields and required through an attribute on type parameters; user code can never assert them. A lock guard is a `using var`. With static fields banned, there is no global mutable state to protect. Binaries stay libc-only because the thread functions live in `libc.so.6` from glibc 2.34 onward; older glibc is not supported.

**Panic containment (F16, D4).** A panic in a spawned task or thread ends that task, not the process. User code still cannot catch a panic: there is no `try`/`catch`, and `Result` and `Option` remain the error model. Containment happens only at task and thread boundaries inside the runtime, which hand the failure to the owner as a value.

- **Mechanism: a hidden panic-return path.** Every function that may panic returns a hidden flag. After each such call the caller tests it and branches to a cleanup block that runs drops and returns the flag upward, until a boundary is reached. This is ordinary control flow in MIR and LLVM IR: no unwinder, no unwind tables, no personality routine, no assembly. Binaries stay libc-only with zero authored C, and the same lowering works on `wasm32`.
- **Rejected alternatives.** Landing pads with the system unwinder need `libgcc_s.so.1`, which breaks the libc-only gate. A statically linked third-party unwinder keeps the gate and the letter of "zero authored C", but puts third-party C or C++ and unwind tables into every binary and needs a different mechanism on WASM. A Vibranium-owned unwinder in LLVM IR would need per-architecture register restoration, which IR cannot express without assembly.
- **.NET parity.** `VibraniumPanicException` is thrown and caught only at the same boundaries, inside the managed runtime twin, never in checked Vibranium code. `using` scopes are disposed in LIFO order as the panic passes: by C#'s own `finally` on .NET, by generated cleanup natively. Linear resources the failing task owns outside a `using` are disposed at the boundary in reverse creation order, from a per-task registry that both runtimes implement the same way.
- **Shared state.** A task can have changed only three kinds of state. State it owns is destroyed. State behind `Mutex`, `RwLock` or `RefCell` is poisoned when a panic releases a guard, as with Rust's `Mutex`; later access returns an error until the owner clears it. Data a scoped child borrowed from its parent is not contained: the panic is re-raised in the parent when the scope ends.
- **Limits.** A panic during cleanup aborts the process with exit code 101. A panic on the main task, outside any boundary, keeps today's behaviour: message, exit 101, no cleanup.
- **Cost (estimates).** Code size grows 5–15% from the checks and cleanup blocks. Run time grows 1–3% on call-heavy code, with no cost for calls the compiler proves cannot panic. The work is about 2 units across both compilers, the runtime twins, the spec and the fuzzer.

**Global state (D5, decided).** Static fields stay banned at 1.0. A framework that wants an ambient context, as signal-based UI libraries do, passes it as a parameter. Allowing thread-local statics later would be a compatible addition.

**Standard library at 1.0 (D8, decided).** In: collections; text and formatting; files and paths; TCP, UDP and name lookup; clocks and timers; process, environment and signals; secure random bytes; the async runtime; threads and synchronisation; arenas; math and vector types under their BCL names; a test attribute and assertions. Out, shipped as packages: HTTP, JSON, TLS and crypto, compression, databases, windowing and GPU bindings, time zones, UI. The rule: the standard library holds what needs compiler knowledge or an operating-system leaf, and everything else is a package.

## 4. Toolchain: `vib`, packages, cache, compile speed, watch (v0.3)

**In plain words.** `vib` is a thin front end. In development it drives `dotnet` (Roslyn, the analyzer, the debugger, NuGet, `dotnet watch`); for release it runs the native pipeline. Development requires the .NET SDK, and debugging and IDE support are whatever Visual Studio, Rider and VS Code give an ordinary C# project. Because it reuses `dotnet` for everything except the native pipeline, the cache and the checks, this is the smallest milestone in the plan.

### 4.1 Commands

| Command | What it does |
|---|---|
| `vib install [pkg]` | Makes sure the pinned .NET SDK is present (4.8), then `dotnet add package` and a locked `dotnet restore`, plus the compliance and consistency checks (4.2). `vib install --native` fetches LLVM 18 on request (4.8) |
| `vib run` | `dotnet run`. `vib run --native` builds a debug-native binary and runs it, which is what `vib run` does today |
| `vib watch` | `dotnet watch` with hot reload, plus a live ownership check (4.6) |
| `vib build` | Native release binary. `--debug` gives a fast unoptimised native build |
| `vib check`, `vib emit-ll` | Exist today. `check` is the agent's main loop (4.7) |
| `vib test` | Thin test runner (4.7) |
| `vib cache` | Size report; `vib cache clean` |

Not built: `vib new` (a `dotnet new` template does it) and `vib publish` (`dotnet pack` and `dotnet nuget push` work once Vibranium's MSBuild props add the source payload). `vib` itself ships as a `dotnet tool`.

**Possible extra, not required.** `vib` could export the resolved package sources and a build plan, and the self-hosted compiler could build a release binary from them on a machine with LLVM but no .NET, such as a minimal CI container. No milestone depends on this. Full compiler parity (D1) keeps it feasible.

### 4.2 Packages: DLL plus source, one graph

Your working model stands: a library is an ordinary C# project or NuGet package that is a DLL on .NET and is statically linked natively.

- **Manifest, lock file, registry (decided: NuGet).** The manifest is the `.csproj`. The lock file is NuGet's `packages.lock.json`, which pins a content hash per package and fails a locked restore if anything drifts [5]. The registry is nuget.org or any NuGet feed, under a reserved ID prefix [5]. There is no second manifest, resolver or registry.
- **One dependency graph.** NuGet resolves once. The native build reads the same restore output that `dotnet build` uses, so the two worlds cannot pick different versions. NuGet selects a single version per package, so a binary never holds two copies of a library.
- **Decided: packages ship both a .NET DLL and the source, with a Vibranium marker.** `lib/` holds the DLL and a PDB with embedded source or Source Link. A separate source folder and a small descriptor feed the native build. The descriptor records the spec version, source hash, DLL hash, the SDK that built it, an FFI flag, supported targets and native libraries to link. The marker is that descriptor plus an assembly attribute; `vib install` and the analyzer both look for it. The reasons are yours: ordinary .NET apps can take a Vibranium package as a normal NuGet dependency, development builds are faster, and the debugger steps into library code.
- **Keeping DLL and source consistent.** Builds are already deterministic (`Directory.Build.props`), so the DLL is a function of the source, the SDK version and the options. Three checks, cheapest first:
  1. `vib install` always checks the source folder against the recorded hash. This catches accidents.
  2. Packing through Vibranium's MSBuild targets, and CI before publishing, rebuilds the DLL from the packaged source and compares bytes.
  3. `vib install --verify` repeats that rebuild on the consumer's machine. Only this catches a DLL deliberately built from other source. It needs the SDK version the package was built with; without it, `vib` reports the package as unverified rather than guessing.

  A mismatch could make .NET mode behave differently from the native binary. It cannot make the native binary unsafe, because the native build compiles and checks the source and never reads the DLL.
- **A limit to state plainly.** Ownership guarantees cover only Vibranium-checked code. A plain C# app can reference a Vibranium package, but it gets the ownership and linear-resource checks only if it enables the Vibranium analyzer. Without it the caller can copy an owner or skip a `using`, which `docs/SPEC.md` already places outside the safety boundary.
- **Layouts not chosen.** Source-only packages remove the consistency question, but plain .NET apps could not consume them normally and every consumer would compile every dependency. Source carried only inside the PDB would force the native build to extract it from debug metadata. MIR would need a format stable across compiler versions and two compilers, and LLVM IR cannot represent generics that are not yet instantiated; both are cached locally instead (4.3).
- **Reproducibility.** The SDK is pinned by `global.json`, packages by the lock file's hashes, and the LLVM version is recorded with each build. The same inputs must give the same `.ll` and binary on any machine and path.
- **Assembly boundaries are kept natively.** Stage-0 builds one Roslyn compilation per package, in dependency order, so `internal` and overload resolution behave exactly as in development. This replaces today's merged compilation (section 2).
- **Ownership across packages.** Checking is already modular: a caller needs only the callee's signature and attributes (`in`/`ref`/`out`, `[Mut]`, `readonly`, `[LifetimeOf]`), never its body. What a signature does not show is per-type facts (copyable, zero-safe, linear; later `Send`/`Sync`), which are derived from fields. The native compilers derive them from the package source. The analyzer reads them from a summary written into the package when it is packed, replacing today's name check, and `vib` asserts that the summary matches what it derives from source.
- **Only compliant packages.** The analyzer already rejects calls into unlisted assemblies with `VIB000`. The new part is saying so early: `vib install` refuses a package with no Vibranium marker ("X is a .NET-only package and cannot be used from Vibranium code"), and the analyzer names the package. Host-only dependencies such as generators and xUnit are marked as such and never enter the native graph. Trusted FFI packages need a per-package opt-in.
- **Static linking and size.** Only reachable code is emitted, as today. Generic-heavy libraries will still grow binaries, so per-sample size budgets become CI tests, helped by LLVM function merging and a size-optimised profile for WASM.

### 4.3 One bounded cache

There is one global, content-addressed cache (default cap 2 GB, configurable) and no per-project `target/` folder. A project directory holds only .NET's usual `obj/` and `bin/` and the final native binary.

| Entry | Keyed by | Saves |
|---|---|---|
| Verification result | package hash, .NET SDK version | re-verifying a package's DLL against its source |
| Checked MIR | package hash, dependency summaries, compiler hash | parse, bind, lower and ownership check, once per package version |
| Object or bitcode, non-generic code | checked-MIR key, target, profile, LLVM version | LLVM work for dependencies |
| Generic instantiation | generic definition hash, type-argument hashes, target, profile | each instantiation compiled once per machine |

Entries are immutable, written atomically and shared by every project. When the cache exceeds its cap, least-recently-used entries are evicted at the end of a build. `vib cache` prints total and per-package size, `vib cache clean` empties it, and deleting it by hand is always safe. The key space stays small because there are two profiles (debug, release) and no Cargo-style feature flags. The cache is local only, so MIR needs no stable format.

One honest limit: NuGet's package folder, which holds the dependency DLLs and source, and each project's `obj/` belong to .NET. `vib cache` reports them, but the cap does not govern them. Vibranium packages are small.

### 4.4 Can packages be compiled to a cached intermediate state?

Yes. All four ideas you raised are adopted:

1. **Checked MIR plus ownership summaries** make checking an app proportional to the app, not its dependencies.
2. **Objects per package plus an instantiation cache** are the biggest saving, because LLVM dominates build time. In the debug profile a generic type is instantiated in full, so the artifact does not depend on which methods one program calls and can be shared across projects. Release keeps reachable-only code.
3. **The edit loop on .NET** is the main structural answer. Debug-native builds must still be fast, because some bugs appear only natively and CI runs native tests.
4. **Parallel release optimisation** uses per-package bitcode and ThinLTO, with its incremental cache inside the global cache. Single-module optimisation stays available for size-critical builds such as WASM.

Together these avoid the usual causes of slow Rust builds and full disks: no instantiation is compiled twice, there is one cache rather than one per project and feature set, no build scripts or macros run during native builds, and debug-native builds carry no debug info until v0.9.

### 4.5 Compile-speed workstream and budgets

Your measurement: about 2.7–3.0 s for `vib check` or `vib build` on a 175-line app on a loaded machine, against about 0.3 s for rustc and 1.5 s for an incremental `dotnet build`. The README also records the self-hosted compiler checking small programs in about 2 ms but needing 18 s for its own 45,000 lines (a historical figure). So there are two problems: start-up cost, and a front end that does not scale linearly.

Work items, in order: (1) a benchmark harness, a baseline and a profile; (2) start-up: run `vib` from its own precompiled executable, keep a compile server alive between invocations as the C# compiler does, and reference dependency DLLs instead of re-parsing library source; (3) the cache in 4.3; (4) find and remove the super-linear step; (5) code generation: an `-O0` debug profile, one LLVM module per package or file compiled in parallel, a fast linker; (6) optionally, the native self-hosted checker as an accelerator where it supports the program.

Proposed budgets, as release criteria for v0.3:

| Scenario | Fixture | Budget (median) |
|---|---|---|
| Small-app check | 175-line app | ≤ 0.1 s warm, ≤ 0.8 s cold |
| Small-app debug-native build | same | ≤ 0.3 s warm, ≤ 1.0 s cold |
| Debug-native rebuild after a one-file change | medium app: 50,000 lines including dependencies (`src/vib-self` until a larger one exists) | ≤ 1.5 s |
| Clean debug-native build | medium app, dependencies cached | ≤ 8 s |
| Clean release build | medium app, 4 cores | ≤ 45 s |
| Second project with the same 20 dependencies | two projects | zero dependency compilations, zero bytes added to the cache |
| Cache bound | test cap of 50 MB | size stays under the cap after eviction |

"Warm" means the compile server is running and caches are populated. "Cold" means no server but a populated disk cache.

**How CI measures it.** A fixed hosted runner runs each scenario seven times after one warm-up and takes the median. Wall-clock time is compared with a checked-in budget file at 25% tolerance. The compiler's instruction count is compared with a stored baseline at 3%, because instruction counts are stable on shared runners and wall time is not. A nightly job reports the same fixtures against rustc and `dotnet build`. The numbers are proposals: they are adopted (D10), to be re-baselined once after the first profile, and then they gate releases.

### 4.6 `vib watch` and hot reload

- **Development.** `vib watch` is `dotnet watch`. .NET hot reload applies method-body edits and many structural edits to the running process and keeps its state; unsupported "rude" edits prompt a restart [6]. Hot reload from Visual Studio or Rider works with no vib involvement.
- **Ownership diagnostics stay live.** The IDE analyzer keeps showing squiggles regardless of watch. For terminal users and coding agents, `vib watch` also runs the fast checker on every save and prints `VIB` diagnostics. To confirm in a spike: whether `dotnet watch` withholds an edit that has analyzer errors. If it does not, an edit that breaks ownership rules can be live in the .NET process. That is memory-safe there but not a verified program, so `vib watch` shows a banner and the native build refuses it.
- **Limits to document.** A hot-reloaded process is outside the parity guarantee. Adding a field whose type has no zero value to a class with live instances leaves it unset, so that edit should force a restart. Start-up code does not re-run.
- **Native watch.** Realistically this is rebuild and restart within the budget above. True native code swapping exists in Rust (Dioxus's Subsecond patches functions through a jump table, and Bevy adopted it with limits [7]). It is not planned, because .NET mode already swaps code.
- **What this gives UI and game frameworks.** Flutter's hot reload is its Dart VM injecting code and then rebuilding the widget tree [8]. A Vibranium framework whose view is a function of kept state gets the same on .NET by re-rendering from a hot-reload hook, and an edited game `Update` body takes effect on the next frame. The foundation only has to expose the hook and document what frameworks must avoid, such as state hidden in start-up-only code.

### 4.7 Built for coding agents

Agents iterate in seconds and often run many at once, each in its own git worktree. The toolchain treats that as the normal case.

- **`vib check` is the main feedback loop.** It verifies types, ownership and the subset without building, within the budgets above.
- **Machine-readable diagnostics.** `--json` emits one object per diagnostic under a versioned schema: the stable ID; a stable reason slug under the broad `VIB000` (for example `closure-capture`); the exact span; related spans such as "moved here"; and, where a mechanical fix exists, the concrete edit as a range plus replacement text. The same edit data drives the IDE quick-fixes already planned.
- **Short, precise messages.** One line says what is wrong and how to fix it. Long explanations move to `vib explain VIB003`. Errors caused by an earlier error are suppressed, and default output is capped with a count of the rest.
- **Deterministic output.** Diagnostics are sorted by file and position, paths are project-relative, and there are no timestamps, colours or progress lines when output is not a terminal. Exit codes are documented. The same input gives byte-identical output.
- **Worktrees share everything shareable.** Dependency DLLs and source exist once in NuGet's package folder; checked MIR, objects and instantiations exist once in the global cache. Dependency DLLs are hard-linked into a worktree's output rather than copied, which MSBuild supports as an option. One compile server per user serves all worktrees. Concurrent builds are safe because entries are immutable and written atomically.
- **A thin `vib test`.** The agent loop needs a test step whose latency we control, and `dotnet test` start-up is not ours to fix. A test is a static method marked `[Test]`, a failure is a panic, and the runner prints JSON. It runs on .NET or debug-native. This is the one command beyond the minimal list, approved in D12.

Agent-loop targets, as release criteria for v0.3:

| Measure | Target |
|---|---|
| Edit one file, then `vib check --json`, then `vib test` for that package: small app | ≤ 0.5 s in total |
| The same on the medium app | ≤ 3 s in total, of which the check is ≤ 0.5 s |
| Eight agents checking at once on eight cores | each within twice the single-agent time |
| Disk for each extra worktree, medium app | the checkout plus ≤ 25 MB, with zero bytes of dependency artifacts |

### 4.8 Getting the toolchain: dotnetup for .NET, a separate route for LLVM

**In plain words.** `vib install` makes sure the right .NET SDK is on the machine, without admin rights and without touching the shell or PATH. It does this through dotnetup, Microsoft's rustup-style toolchain manager. dotnetup is still in preview, so `vib` has a second route. dotnetup does not install the native compiler; `vib` finds or fetches that itself.

**The .NET SDK.**

1. *Pin.* The project's `global.json` names the SDK version. This is the pin the reproducibility rule in 4.2 already relies on.
2. *Reuse.* `vib` first looks for an installed SDK that satisfies the pin: an explicit `VIB_DOTNET_ROOT` (for containers and distribution packages), the `dotnet` on PATH, then dotnetup's own folder. If one is found, nothing is downloaded.
3. *Install.* Otherwise `vib install` runs `dotnetup sdk install` non-interactively in the project folder. With no argument, that command takes the version from the nearest `global.json` and installs into a user-owned folder [13]. The SDK brings its own runtime, so `vib` asks for no separate runtime; the preview reads only SDK pins from `global.json` in any case. If dotnetup itself is missing, `vib` asks before fetching it with Microsoft's install script, which verifies a checksum; `--yes` answers for CI.
4. *Use.* `vib` uses dotnetup's `none` access mode: it starts .NET through `dotnetup dotnet`, or by the SDK's full path, for its own child processes only. It never edits a shell profile or PATH. One consequence: an editor started outside `vib` cannot see an SDK that exists only in dotnetup's folder. `vib install` prints the one dotnetup command that exposes it and leaves that choice to the user.

SDKs installed this way stay under dotnetup's management (update, uninstall with clean-up) and outside `vib`'s bounded cache. `vib install --verify` (4.2) uses the same route to get the SDK version a package was built with.

**Because dotnetup is in preview.** Microsoft says its commands and options may change before general availability, and recommends the established installers for production machines [13]. The command names above are those of the October 2026 preview. So:

- Every dotnetup call sits behind one small adapter that needs three operations: install the pinned SDK, list what is installed, run `dotnet`.
- `vib` records the dotnetup versions it was tested with. A missing, declined, untested or misbehaving dotnetup triggers the fallback: the long-established `dotnet-install` script, which reads the same `global.json` and installs into a `vib`-owned folder [13]. `vib` says in one line which route it took. If both fail (for example, no network), `vib` stops and names the SDK version it needs and both ways to get it.
- Both routes run in CI on every release, and a scheduled job runs against the newest dotnetup build. An interface change then shows up as a failing job and costs one adapter update, not a broken install.
- The 1.0 promise covers the result (the pinned SDK, no change to the user's environment), not the tool behind it. dotnetup stays the first route at 1.0 even if it is still in preview, because a `vib` 1.0 that meets a later, untested dotnetup uses the script instead.

**The native toolchain is separate.** dotnetup manages .NET SDKs and runtimes only. `vib run --native` and `vib build` also need LLVM 18 (`clang` and its linker, with `wasm-ld` for the browser target) and the C library's development files.

- *Not needed for the daily loop.* `vib install`, `run`, `watch`, `test` and `check` work with .NET alone. Only native commands look for LLVM.
- *Find.* In order: `VIB_LLVM_ROOT`; versioned tools on PATH (`clang-18`); an unversioned `clang` that reports major version 18; a copy `vib` fetched earlier. Other major versions are refused, because the emitted IR is LLVM 18 text and builds must be reproducible (4.2).
- *Fetch, on request.* If nothing is found, the native command stops and offers two choices: the distribution's LLVM 18 package (needs admin rights), or `vib install --native`. The second downloads the official LLVM 18 release archive for the host [13], checks it against hashes shipped inside `vib`, and unpacks it into a user-owned folder: no admin rights and no PATH change. The archive is large, so it is never fetched silently. It sits outside the bounded build cache; `vib cache` reports it and removes it only on request.
- *What `vib` cannot supply.* The C library's development files (start-up objects and `libc.so`) come from the system. `vib` names the missing package and the install command for common distributions. Cross targets need the target's files too; v0.5 defines where the Linux ARM64 ones come from.

**Effect on the estimate.** This adds an adapter, a fallback and a fetch step to v0.3. v0.3 stays at size 1 on the half-unit scale, but it now has the least slack of any milestone. If it overruns, the managed LLVM download moves to v0.5; SDK management stays in v0.3.

## 5. Possibility proofs

A proof replaces a framework milestone. 1.0 has two proofs (P2, P1) and two headless samples. The window and game-loop proofs move to 1.1 with the platforms they need (D7). Rules for all of them:

- It lives in `samples/`, is at most about 500 lines of its own code (not counting generated code), and uses only the standard library plus at most one generated FFI binding package.
- It builds with zero warnings on .NET and natively, and produces identical output or an identical operation trace in both.
- It is clean under the sanitizers and runs in CI on every tier-1 target it applies to.
- Its write-up becomes a chapter of the guide for framework authors.

### P2 Browser (v0.6, re-run at v0.8)

**The sample.** A WASM page with a counter and a list: exported event entry points; DOM operations sent through a batched buffer of host imports; integer node handles that ownership releases; a `fetch` of a JSON document. At v0.6 the fetch result arrives through a callback (another exported entry point), because async does not exist yet. At v0.8 the same page is rewritten with `await` on the thread-pinned local executor and shares its type definitions with P1.

**How it runs on .NET.** Host imports get a managed twin, as OS leaves already have in `Sys.Managed.cs`. The twin records the operation stream. The proof passes when that stream equals the one the WASM build produces in a headless browser. A framework can later use the same twin to drive a real page from a .NET process, which gives breakpoints and hot reload; that part is the framework's job.

**What the foundation must make solvable.** GC-free UI state in both common styles. State passed by reference with static handlers needs only v0.4 and is the v0.6 form. Signals need owning closures, `Rc`, `RefCell` and arena handles (Leptos and Dioxus do it with copyable handles and runtime borrow checks [4]); those are complete by v0.7, so the v0.8 re-run adds a signal. The `nint` trap: 64-bit on the development host, 32-bit in WASM, so width-dependent behaviour must be banned or defined. Download size, measured here and then gated. Panics: at v0.6 a panic in a handler traps the module; from v0.7 each exported entry point is a containment boundary, so the re-run shows a handler that panics and a page that keeps working.

**Context.** Blazor WebAssembly ships the .NET runtime, about 1.4–2.3 MB compressed; minimal Rust framework apps land around 50–100 KB. The figures are from mixed sources and not like-for-like [3]. WASI 0.3 was ratified in June 2026, but browsers still run only core modules [2], so the target is core WASM plus JavaScript glue. Designs checked: Blazor's render batches, Elm, Xilem [10].

### P1 Backend (v0.8)

**The sample.** A small async HTTP/1.1 JSON service on the work-stealing executor: a route table of static-lambda handlers that take app state by `in` or `ref`; request and response types through a sample derive generator; a store reached through an FFI package (SQLite); 1,000 idle connections on a small fixed number of worker threads; a per-request time-out delivered as an error value; and one route that panics on purpose, which gets an error response while every other connection continues.

**What it proves.** Handlers and parameter binding without reflection (each parameter type implements a static abstract "extract from request" trait, as axum's extractors do, with one overload per arity because C# has no variadic generics). Async I/O under ownership, with `Send` checked across every `await`. A connection type that owns its socket and is closed when its task panics. FFI and code generation.

**What the foundation must make solvable.** State shared by requests that run on different workers: shared through `Arc`, changed through a lock, and poisoned if a handler panics while holding it; the sample shows the clear-or-restart choice. Hostile input: the standard `Map` needs seeded hashing, with insertion-ordered iteration so output stays identical on .NET and native. Output order: interleaving across workers is unspecified, so the proof compares one transcript per connection in real runs and the whole trace under the seeded simulated reactor.

**Checked against.** ASP.NET Core Minimal APIs and its request-delegate generator; axum; tokio's reactor and executor split, and its cancel-safety pitfalls as the thing to avoid [9].

### Two headless samples (v0.9)

These keep the foundations for UI and game frameworks honest without a window or any OS-specific code.

- **Retained tree.** About 50 nodes with parent links in a slot-map arena, laid out in one pass (constraints down, sizes up, as Flutter does [8]) and driven by a scripted event list. A worker thread loads a file and posts the result over a channel. The output is the layout as text.
- **Mini-ECS.** A fixed-timestep loop over 10,000 entities with zero heap allocation per frame, using a frame arena. Component columns are stored by `TypeId` behind an interface. Three systems are static methods whose `in` and `ref` parameters declare access; two of them run in parallel on scoped threads. Vector math uses BCL-named types. The output is one checksum per frame.

**What they prove.** Retained trees without a GC, type-erased storage in safe code, parallel systems without data races, and frame-time memory control.

**What the foundation must make solvable.** Float determinism: .NET's JIT and LLVM must agree on fused multiply-add, and `sin` and `cos` come from the platform's math library, so the spec needs a stated tolerance or a Vibranium-owned math library. Designs checked: XNA, MonoGame and FNA; Bevy, whose systems declare access through parameters and whose scheduler checks conflicts when it builds and runs the schedule [12]; EnTT.

### P3 Window and P4 Game loop (1.1)

**P3.** Opens a window through SDL3 using a generated binding, draws rectangles and text from a glyph atlas, handles keyboard, mouse and resize through a C callback, and displays the retained-tree sample. It proves one source for Linux, macOS and Windows. **P4.** The mini-ECS drawing 10,000 sprites in that window.

Both use only language and FFI features that 1.0 already contains, so 1.1 adds platforms and samples, not rules.

**Left to framework authors.** Text shaping, input methods, accessibility and the renderer. The foundation only has to keep them reachable: system text libraries and the platform toolkits are all callable through C-level FFI with callbacks. Graphics stacks stay with the ecosystem: SDL's GPU API has no web backend, and the stable WebGPU C header is not yet fully implemented by wgpu-native [11]. Designs checked: Flutter's narrow embedder interface, Xilem, AccessKit, MAUI.

## 6. What "stable" means

### 6.1 The compatibility promise

From 1.0, for every 1.x release:

- **Source stays valid.** Every program 1.0 accepts is accepted by every 1.x, with the same observable behaviour on .NET and natively: output, exit codes, panic messages, `Dispose` order, and what a contained panic cleans up and poisons. Where several worker threads interleave, the promise covers what does not depend on the schedule.
- **The subset only grows.** 1.x may accept more C#, never less.
- **One exception: soundness.** A program that was accepted but breaks memory safety, exact-once disposal or .NET/native parity may be rejected by a later 1.x. Each such fix ships with an advisory, a conformance test, a fuzzer production and, where possible, a machine-applicable fix.
- **Standard library.** Nothing public is removed, and nothing changes signature or documented behaviour, in 1.x. CI enforces this with .NET's own API-baseline tooling (public-API files and package validation).
- **Tooling.** `vib` commands, flags and exit codes, the `--json` schema, diagnostic IDs and reason slugs are stable. Message wording is not.
- **Packages.** A 1.x toolchain builds any package whose descriptor declares spec 1.0 or later. Pre-1.0 packages carry no promise. The descriptor's spec version doubles as the opt-in mechanism for any future incompatible edition of the language.
- **Preview features.** A feature may ship switched off by default and outside the promise. Published packages may not depend on one.

Not promised: any binary compatibility (no stable ABI, MIR, LLVM text or cache format, because native builds compile from source); message text; and behaviour the spec lists as unspecified, such as running out of memory, stack exhaustion, timing and thread interleaving.

**Platforms.** Each release names its .NET SDK and LLVM versions. Tier-1 targets carry the full promise and every gate. Tier 1 at 1.0 is Linux x86-64, Linux ARM64 and wasm32 (D7). macOS and Windows arrive in 1.1 and are tier 2 until then: built and tested where possible, without the promise. A move to a newer .NET or C# happens in a minor release. The accepted subset stays defined against a named C# version, and newer syntax is rejected until admitted.

### 6.2 Spec freeze

- **Feature freeze.** After v0.8 no new language feature enters before 1.0. After v0.9 no new target or FFI capability does.
- **A numbered, normative spec.** `docs/SPEC.md` is restructured so that every rule has a number, every numbered rule has at least one accepting and one rejecting conformance test, and unspecified behaviour is listed explicitly.
- **Freeze at the first release candidate.** From then on, only clarifications and defect fixes.
- **After 1.0, change by written proposal.** A proposal carries the spec diff, the .NET-parity rule, fuzzer productions, proof impact and migration. Within 1.x it must be additive.

### 6.3 Deprecation policy

- **Before 1.0.** Breaking changes are allowed at each 0.x. Each is listed in the release notes with a machine-applicable migration where possible, using the same fix-edit data as diagnostics, since agents will do most migrations.
- **From 1.0.** An API is deprecated with C#'s own `[Obsolete]` and a named replacement. It stays for the rest of 1.x and at least twelve months, and can be removed only in 2.0. Language constructs are not deprecated within 1.x.
- **At 1.0 itself.** Everything known to be provisional is removed or renamed before the freeze, not after.

### 6.4 Release criteria for 1.0

1. **Suites.** The four verification suites pass on every tier-1 target, clean under ASan, LSan and ThreadSanitizer.
2. **Conformance fuzzer.** Generator productions reach every numbered rule, shown by a coverage report. A differential campaign across all pipelines and tier-1 targets finds zero discrepancies over an agreed soak (10 million programs per target, D14). Reject-side fuzzing passes: valid programs mutated into violations are rejected everywhere with the same ID. Every bug the fuzzer has ever found has a regression test.
3. **Review gate.** Two consecutive independent review rounds of spec and implementation end with no blocking finding. Where the project's existing gate is stricter, it applies.
4. **No open soundness or parity defect.** Known limitations are documented.
5. **Proofs.** P1, P2 and the two headless samples pass. Lean covers owned collections, resource-owning types and borrowing closures. A published table says which rules are proved and which are tested only; async, threads and panic containment are in the tested column (D11).
6. **Budgets.** Compile-time, cache, agent-loop and size budgets pass on the 1.0 corpus.
7. **Compatibility machinery exists.** API baseline, versioned conformance suite, proposal process, advisory process.
8. **Documentation.** The specification, a standard-library reference, and a guide for framework authors built from the four proofs.
9. **Release candidates.** At least two. 1.0 is a release candidate shipped unchanged after its soak with no blocking report.
10. **Agent benchmark (D14).** A fixed set of tasks is solved by coding agents from the documentation alone. The task set and the required pass rate are fixed at v0.8.

## 7. Milestones to 1.0

Every milestone also requires the existing gates: all four verification suites, fuzzer productions for each new construct, and a clean fuzzer run.

**v0.3 Toolchain (size 1).** Scope: section 4, plus `$"..."` and `out err` reuse if they fit. Exit criteria:
1. `Vibranium.Std` and one sample library are published as packages (DLL plus source) to a test feed and consumed by every sample through `vib install`. The `Vibranium.Std` name check is gone.
2. Development and native builds resolve the identical package set (CI compares hashes). A test that uses a dependency's `internal` member fails the same way in both.
3. A .NET-only package is refused by `vib install` and by the analyzer, naming the package. A package whose DLL was built from different source is caught by `vib install --verify`. A plain C# project consumes the standard-library package as an ordinary NuGet dependency.
4. All budgets in 4.5 and 4.7 pass in CI.
5. Cache tests pass: bound, eviction, sharing across projects and worktrees, size report.
6. The same lock file and toolchain give a bit-identical `.ll` and binary from two different paths, extending today's `repro.ll` check.
7. `vib watch`: a body edit shows in the running app without a restart, and an ownership error introduced during watch is printed within 1 s.
8. The `--json` schema is documented, tested for determinism, and carries fix edits for at least the `using var` and `Move(...)` cases.
9. The fuzzer generates multi-package programs.
10. Toolchain acquisition (4.8), tested on clean machine images. With no .NET SDK, `vib install` obtains the pinned SDK without admin rights and leaves the shell profile and PATH unchanged. With dotnetup absent or at an untested version, the fallback route gives the same result. With a matching SDK present, nothing is downloaded. Without LLVM 18, `vib run` and `vib test` work, and `vib build` stops with a message that names what is missing and how to get it.

Cut: `vib new`, publishing polish, builds without .NET.

**v0.4 Abstraction (2.5).** Scope: F2–F5 and the rest of F1. Exit criteria:
1. `Vec<T>` holds any `T`. A generic `Map<K, V>` exists, with seeded hashing and insertion-ordered iteration; `IntMap` and `StrMap` are built on it.
2. A small trait-based library package is consumed from a second package, and the HTTP sample is rebuilt on a route table of static-lambda handlers with identical wire output on .NET and native.
3. The self-hosted compiler uses traits and owned collections in its own source and still reaches the bootstrap fixed point, and both compilers accept and reject the same programs (D1).
4. The Lean model covers collections of owned elements.

Cut: capturing closures, interface-typed values, `yield`.

**v0.5 Resources and the outside world (2).** Scope: F6–F10 and Linux ARM64. Exit criteria:
1. A connection type owns a `TcpStream` in a field with no leaks under LSan, and the Lean model covers resource-owning types.
2. A trusted FFI binding package, generated from an API description, is used from a sample. An app that declares `[Native]` without trust is rejected. A callback from C into Vibranium works in both worlds.
3. A sample derive generator produces JSON readers and writers; round-trips, floats included, match byte for byte on .NET and native.
4. Borrowing closures have exact .NET-parity rules in the spec and are covered by the Lean model, or are explicitly moved to v0.7.
5. The full suite passes on Linux ARM64, and 4.8 says where the cross-target files come from.

Cut: structs by value in FFI; anything HTTP beyond the sample.

**v0.6 Browser target (1.5).** Scope: F12. Exit criteria:
1. `vib build --target wasm32` passes the compute differential suite against .NET.
2. Host imports and exports are declared through attributes, fail-closed like `[Native]`, each with a managed twin.
3. The width rules for `nint` are in the spec.
4. **Proof P2 passes in its first form** (fetch through a callback), and its compressed size is recorded as a budget and gated in CI.

Cut: WASI, threads on WASM, `await` in the browser (arrives with v0.8).

**v0.7 Threads, dynamic dispatch and panic containment (4.5).** Scope: F13, F14, F16. Exit criteria:
1. A corpus of data-race programs is rejected at compile time by both compilers and the analyzer, and accepted programs are clean under ThreadSanitizer. The `Send` and `Sync` rules are in the spec. The minimum glibc version (2.34) is stated and checked at build time.
2. Owning closures, interface objects, `TypeId` and checked downcast have exact .NET-parity rules in the spec.
3. Decisions D5 and D6 are implemented as decided: static fields stay banned and there is no `unsafe` tier.
4. Containment works at thread and scoped-task boundaries. A test corpus gives identical output, exit codes and `Dispose` order on .NET and natively, from both compilers, for: `using` scopes (last in, first out); linear resources owned outside a `using` (reverse creation order, from the per-task registry); poisoned locks; a panic during cleanup (abort, exit 101); and a main-task panic (unchanged from v0.2).
5. Native binaries still list only `libc.so.6` under `readelf -d` and contain no unwind tables and no personality routine. No authored C enters the repository.
6. The cost of the hidden panic-return path is measured, replaces the estimates in section 3, and is gated as a budget. If code size or run time costs more than twice the estimate, the result goes back to the user before v0.8 starts.
7. The fuzzer generates panics at every kind of may-panic site and checks leaks (LSan) and disposal order against .NET.
8. `docs/SPEC.md` §5.4 is rewritten for the new contract, and the proved-versus-tested table states that the Lean theorems assume abort on panic.

Cut: catching panics in user code (never planned); containment of data a scoped child borrowed from its parent.

**v0.8 Async on a work-stealing executor (3.5).** Scope: F11. Exit criteria:
1. The spike result is recorded: LLVM coroutines pass the fuzzer at `-O0`, `-O3` and under ASan, or the MIR fallback is chosen.
2. The borrow rules across `await` and the `Send`-across-`await` rule are in the spec and the fuzzer. A corpus of programs that hold a non-`Send` value across an `await` in a spawned task is rejected with the same diagnostic ID by both compilers and the analyzer.
3. The async standard library covers TCP, timers, channels and cancellation as a value. A seeded simulated reactor gives identical traces on .NET and native.
4. The work-stealing executor is clean under ThreadSanitizer and under schedule fuzzing (steal order randomised from a seed). The thread-pinned local executor passes the same suite and is the only executor on wasm32.
5. A task that panics is contained: its join handle returns an error and other tasks continue.
6. **Proof P1 passes. Proof P2 passes again in its `await` form.**
7. **Language feature freeze:** the list of C# constructs accepted at 1.0 is final. The search for a shorter error-propagation form (D2) closes here. The agent benchmark's task set and pass rate are fixed (D14).

Cut: `io_uring`, threads on WASM.

**v0.9 Interop and numerics (1.5).** Scope: the rest of F8 and F15. Exit criteria:
1. FFI passes small structs by value and honours C layouts and callbacks on the three tier-1 ABIs, tested against libc functions such as `div` and `qsort`. The binding generator is documented.
2. BCL vector types lower to SIMD, and the spec states the rule for math functions.
3. gdb and lldb step through native code with source lines and locals.
4. The standard library has a frame arena and a slot-map arena.
5. **The two headless samples pass** on every tier-1 target.
6. **Target and FFI freeze.**

Cut: macOS, Windows, windows and graphics (all 1.1); mobile.

**v1.0 Stabilisation (2.5).** Work: number the spec and write the conformance suite; review and baseline the standard-library API; remove provisional APIs; run the fuzzer soak, including panicking programs and seeded schedules; write the guide for framework authors; set up the proposal and advisory processes; ship at least two release candidates. Exit criteria: section 6.4.

**1.1 Platforms (about 2.5, outside the total).** macOS (ARM64) and Windows (x86-64) pass the full suite and become tier 1. Proofs P3 and P4 pass on all desktop targets. The release adds targets and samples only, so it falls under the 6.1 promise. The toolchain route in 4.8 is re-checked for each new host.

**How this differs from the current `docs/ROADMAP.md`.** Tooling comes first. Traits, owned collections and resource-owning types come before any new platform. The browser target comes before threads and async, because it needs neither. Threads and panic containment are added (the current roadmap has neither) and come before async, because the 1.0 executor is multi-threaded. `Vib.Ui`, `Vib.Web` and `Vib.Sql` leave the roadmap and become possibility proofs or ecosystem work. macOS and Windows move to 1.1. The roadmap ends at a stable release with defined criteria.

## 8. What you might be missing

1. **Types that own resources (F6) and shared ownership without a GC (F7).** A connection object, a window, a tree with parent links: none can be written in v0.2. These block more than any single item on your list.
2. **A panic policy (D4, decided).** In v0.2 a panic exits the process and runs no cleanup. 1.0 contains panics at task boundaries (F16 in section 3). The behaviour is observable, so it is fixed for good at 1.0.
3. **How low-level library code gets written (D6, decided).** `Vec`, `Rc`, locks, the executor and the allocator cannot be written in safe Vibranium, and 1.0 has no `unsafe` tier and no C. They stay compiler intrinsics and runtime IR. That trusted base grows with threads, the executor and containment, and only tests, sanitizers and the fuzzer check it.
4. **Host imports on .NET.** "Develop on .NET" needs a managed twin for every host import, such as the DOM (proof P2).
5. **Failures that appear only natively.** `dotnet test` exercises .NET only. Native code needs a native test run (4.7), debug info (v0.9) and sanitizers on each platform.
6. **The stability machinery is work.** A numbered spec, a conformance suite, API baselines, and proposal and advisory processes are budgeted as the v1.0 milestone, 2.5 units.
7. **Small proofs can freeze a mistake.** No framework will have exercised closures, async or threads at scale before the freeze. Mitigations: each proof is written by someone other than the feature's implementer; risky features may ship as previews; an outside framework author is invited during the release candidates; the agent benchmark (D14).
8. **Text.** Float formatting that matches .NET exactly, and how much Unicode the standard library promises.
9. **What the ecosystem needs that is not a framework.** An FFI binding generator, package discoverability, the guide for framework authors, and guidance that TLS and crypto come through FFI rather than being rewritten.
10. **Upkeep after 1.0.** .NET 11 is a short-support release, C# changes yearly, LLVM is pinned at 18, and someone must own the proposal process. macOS and Windows arrive in 1.1. Mobile targets, time zones, `io_uring`, thread-local state and an `unsafe` tier are left for later; all are compatible additions.
11. **Getting started.** A newcomer needs a pinned .NET SDK and, for native builds, LLVM 18. Neither should require admin rights or a changed shell. Section 4.8 covers both, and the tool behind the first one is still in preview.

## 9. Risks

- **Parity cost compounds.** Each feature lands in six places (section 2), and later features interact with all earlier ones. D1 keeps the self-hosted compiler in step at every release, so none of that work can be deferred.
- **Work-stealing soundness.** The `Send` check depends on computing exactly which values are alive in a task's frame at each `await`, identically in both compilers and the analyzer. One missed value is a data race in safe code. The executor's queues and wake-ups are lock-free runtime IR that nothing checks statically. Mitigations: start with a conservative check, a corpus of rejected programs, ThreadSanitizer, seeded schedule fuzzing.
- **Containment soundness.** Every call that may panic needs a correct cleanup path. A missed drop is a leak and a doubled drop is a double free, and both happen only on the panic path, which ordinary tests rarely take. Poisoning must cover every guard type and the registry every linear resource. Mitigations: cleanup paths come from the same drop logic as normal exits, and the fuzzer injects panics under ASan and LSan.
- **Containment parity across three implementations.** .NET unwinds with an exception and `finally`; Stage-0 and the self-hosted compiler generate cleanup branches. All three must agree on disposal order, on poisoning, and on aborting when cleanup itself panics, which on .NET needs an explicit check because a second exception would otherwise replace the first.
- **Parity under real threads.** Interleaving across workers is unspecified in both worlds. Parity can cover only schedule-independent output plus seeded simulated runs, so a schedule-dependent bug may show in one world and not the other.
- **Cost of the panic-return path.** The size and speed figures are estimates. v0.7 measures them, with a stated point at which the result returns to the user.
- **A locked contract changes.** `docs/SPEC.md` §5.4 says a panic ends the process without running drops. Containment replaces that for spawned tasks and threads, and the Lean theorems, which assume abort, do not cover the new path.
- **glibc floor.** Threads keep the libc-only rule only on glibc 2.34 or newer, where the thread functions live in `libc.so.6`. Older systems are unsupported for threaded programs.
- **Two heavy milestones in a row.** v0.7 and v0.8 hold 8 of the 19 units and the three least certain items: containment, work stealing and async lowering.
- **C# semantics pull against ownership.** Closures capture by reference, default methods box structs, async methods cannot take `ref`. Each needs a restriction, and each restriction makes "pure C#" feel less like C#.
- **Freezing too early.** See item 7 in section 8. This is the main risk of not building frameworks ourselves.
- **Async correctness.** LLVM coroutines combined with `noalias` and Vibranium's drop rules are untested territory, hence the spike and the fallback.
- **dotnetup is in preview.** Its commands may change before general availability, and Microsoft recommends the established installers for production machines [13]. The adapter, the tested-version list and the `dotnet-install` fallback (4.8) turn a change into a slower first install. Two residual risks: `vib` downloads installers, which is a supply-chain surface (consent, checksums and pinned hashes limit it), and CI images should provide the SDK directly, not through a preview tool.
- **LLVM is not managed by anyone.** No official tool installs LLVM 18 per user. `vib` carries that itself (4.8), and the C library's development files still come from the system.
- **DLL and source consistency.** Byte-for-byte verification needs the SDK version a package was built with. Across SDK versions a package can only be reported as unverified.
- **Compile-time targets.** The cause of the 18 s self-check figure is unknown until profiled, so the budgets may need re-baselining.
- **The soundness exception spends trust.** Every use of it breaks someone's code. The fuzzer soak and the proofs exist to keep it rare.
- **A foundation does not make frameworks appear.** 1.0 makes them possible; someone still has to build them.

## 10. Decisions

Every item is decided. This section is the record. D3 and D4 were decided against this plan's recommendation; their consequences are stated in full.

| ID | Question | Decided | Reason or effect |
|---|---|---|---|
| D1 | How closely must the self-hosted compiler follow Stage-0? | (a) Full parity at every release | The bootstrap fixed point and identical diagnostics stay a release gate; every feature is built twice |
| D2 | Error-propagation form | (c) Keep searching for a shorter pure-C# form until the v0.8 freeze; the one-line guard is the form until then | A candidate must be valid C#, add no hidden control flow on .NET and keep `using` cleanup exact; otherwise the guard is the 1.0 form |
| D3 | Async executor at 1.0 | (b) Multi-threaded, work-stealing | Servers use every core without a rewrite later. Consequences below |
| D4 | Panic policy at 1.0 | (b) Contained per task or request, never catchable in user code | One bad request no longer ends a server. Consequences below |
| D5 | Static mutable state | (a) The static-field ban stays | Keeps thread safety and parity simple; state is passed explicitly |
| D6 | An `unsafe` tier | (a) None at 1.0 | Safe code stays the only code; low-level pieces remain trusted runtime IR (section 8, item 3) |
| D7 | Scope and targets of 1.0 | Foundations for a web backend and a browser frontend, plus every platform-independent foundation. Tier 1: Linux x86-64, Linux ARM64, wasm32 | macOS, Windows and proofs P3 and P4 ship in 1.1 as additions |
| D8 | Standard-library size | (a) Small; HTTP, JSON and TLS are packages | Less to freeze at 1.0, and packages can version independently |
| D9 | Wording of the compatibility promise | As written in 6.1 | Adopted as recommended |
| D10 | Compile-time and agent-loop budgets | As written in 4.5 and 4.7 | Adopted as recommended; re-baselined once after profiling |
| D11 | How much is mechanised in Lean | (b) Owned collections, resource-owning types, borrowing closures; a published proved-versus-tested table | Async, threads and containment are tested, not proved |
| D12 | Tool behaviour | `vib run` runs on .NET, with `vib run --native`; a thin `vib test` in v0.3; a reserved NuGet ID prefix | Adopted as recommended |
| D13 | Milestone order | Left to this plan: toolchain, abstraction, resources, browser, threads with containment, async, interop | The browser target needs neither threads nor containment, so P2 lands after 7 units; the work-stealing executor needs both, so they precede async |
| D14 | Release evidence | Soak of 10 million programs per target; the review gate is two consecutive clean rounds; the agent benchmark is a 1.0 criterion, fixed at v0.8 | Adopted as recommended |
| D15 | How `vib` gets the .NET SDK | Through dotnetup, pinned by `global.json`, without changing the user's environment | Added in revision 3; dotnetup is in preview, so 4.8 defines a fallback |

Decided earlier and unchanged: Vibranium builds foundations, not frameworks. `vib` is a thin front end over `dotnet`, and development requires the .NET SDK. Packages are NuGet packages with a Vibranium marker that ship both a DLL and the source (4.2).

**Consequences of D3 (work-stealing executor).**

- Every value alive across an `await` in a spawned task must be `Send`. `Rc` and `RefCell` cannot cross an `await` there; code that needs them uses the thread-pinned local spawn.
- Thread safety and panic containment must exist before async, so proof P1 arrives after 15 units, not 8.
- Output order across workers is unspecified, so the .NET-and-native parity rule covers schedule-independent output and seeded simulated runs only.
- The executor is lock-free runtime code that is tested but neither proved nor statically checked.
- It adds about 1 unit.

**Consequences of D4 (panic containment).**

- Mechanism: a hidden panic-return path. Every function that may panic returns a hidden flag; the caller tests it, runs its cleanup and passes it up to the nearest task boundary. It uses no unwinder, no unwind tables and no assembly, so binaries stay libc-only and no C is authored. It works the same on wasm32.
- Estimated cost, unmeasured: 5–15% more code and 1–3% more run time on call-heavy code. v0.7 measures both.
- `docs/SPEC.md` §5.4 changes: a panic in a spawned task or thread now runs that task's cleanup. A panic on the main task behaves as before (message, exit 101, no cleanup).
- State behind a lock or `RefCell` is poisoned when a panic releases its guard. A panic during cleanup aborts the process with exit 101.
- On .NET, `VibraniumPanicException` is caught only inside the managed runtime, at the same boundaries. Checked Vibranium code still has no `try` or `catch`.
- Both runtimes keep an identical per-task registry of linear resources, so disposal order matches.
- The Lean theorems assume abort on panic and do not cover the contained path.
- It adds about 2.5 units, including 0.5 of stabilisation.

## 11. Effort reality check

- **Scale.** 19 units where the v0.3 toolchain is 1, up from 16.5 in revision 2. The work-stealing executor adds about 1, panic containment about 2.5, and moving macOS, Windows, P3 and P4 to 1.1 removes about 1. Toolchain acquisition through dotnetup (4.8) fits inside v0.3 and changes no figure.
- **When things arrive.** Proof P2 in its first form after 7 units. Proof P1 after 15, where revision 2 had it after 8. The language is feature-complete after 15. 1.1 is about 2.5 more.
- **What does not compress.** Review convergence, the parity matrix, testing on each target, and the fuzzer soak. Writing code is not the bottleneck.
- **A smaller 1.0 was possible and was partly taken.** Rust's 1.0 shipped without async, which arrived compatibly more than four years later. D7 moved the desktop platforms out; D3 and D4 moved async, a multi-threaded executor and containment in.
- **What 1.0 will not mean.** No framework will exist. The proofs are a few hundred lines each. Bevy, for scale, has been in development since 2020, is at 0.20, and its October 2026 release alone had 227 contributors [12].
- **Highest uncertainty.** Panic containment and work stealing (v0.7, v0.8), async lowering (v0.8), compile-time scaling (v0.3), and freezing rules that only small proofs have exercised. The first three sit in 8 consecutive units.
- **Suggested checkpoints.** After v0.4, ask whether the language still feels like C#. After v0.7, compare the measured cost of containment with the estimate. At the v0.8 feature freeze, ask an outside framework author what is missing before it is too late to add.

## Sources

1. .NET 11 runtime async: [.NET 11 release discussion](https://github.com/dotnet/core/discussions/10711); [Preview 1 runtime notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/preview1/runtime.md); [opt-in behaviour](https://milanjovanovic.tech/blog/dotnet-11-runtime-async). Mostly secondary sources; the preview label is disputed.
2. WASI and components: [WASI roadmap](https://wasi.dev/roadmap); [WASI 0.3 announcement](https://bytecodealliance.org/articles/WASI-0.3); [components in browsers](https://component-model.bytecodealliance.org/language-support/building-a-simple-component/javascript.html).
3. Download sizes (not like-for-like): [Blazor on .NET 10](https://www.csharp-coder.com/2026/08/blazor-webassembly-tutorial-2026-build.html); [Blazor on .NET 11 preview](https://startdebugging.net/2026/05/blazor-server-vs-webassembly-vs-united-in-dotnet-11/); [Leptos islands](https://book.leptos.dev/islands.html); [Dioxus size issue](https://github.com/DioxusLabs/dioxus/issues/732).
4. Signals: [Leptos `RwSignal`](https://docs.rs/leptos/latest/leptos/prelude/struct.RwSignal.html); [Leptos discussion 2552](https://github.com/leptos-rs/leptos/discussions/2552); [Dioxus `Signal`](https://docs.rs/dioxus/latest/dioxus/prelude/struct.Signal.html).
5. NuGet: [lock files and locked mode](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files); [ID prefix reservation](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation).
6. Hot reload: [`dotnet watch`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch); [.NET 11 SDK changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk).
7. Native hot-patching: [Subsecond](https://docs.rs/subsecond/latest/subsecond/); [Bevy 0.17](https://bevy.org/news/bevy-0-17/).
8. Flutter: [architectural overview](https://docs.flutter.dev/resources/architectural-overview); [`BoxConstraints`](https://api.flutter.dev/flutter/rendering/BoxConstraints-class.html); [hot reload (mirror of the Flutter docs)](https://github.com/rock3r/flutter-website/blob/master/hot-reload.md).
9. Async cancellation and completion I/O in Rust: [Notes on io-uring](https://boats.gitlab.io/blog/post/io-uring/); [completion-model I/O](https://www.ncameron.org/blog/async-io-with-completion-model-io-systems/); [Cancelling async Rust](https://sunshowers.io/posts/cancelling-async-rust/).
10. Xilem: [architecture post (2022; the current code may differ)](https://raphlinus.github.io/rust/gui/2022/05/07/ui-architecture.html).
11. Graphics: [SDL3 FAQ](https://wiki.libsdl.org/SDL3/FAQDevelopment); [`webgpu.h`](https://github.com/webgpu-native/webgpu-headers); [WebGPU browser support](https://web.dev/blog/webgpu-supported-major-browsers); [WebGPU on Wikipedia](https://en.wikipedia.org/wiki/WebGPU).
12. Bevy: [Bevy 0.20](https://bevy.org/news/bevy-0-20/); [system parameters](https://docs.rs/bevy/latest/bevy/ecs/system/index.html); [executor conflict checks](https://github.com/bevyengine/bevy/pull/16885).
13. Toolchain acquisition: [dotnetup overview (preview status)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnetup/); [`dotnetup sdk install`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnetup/reference/dotnetup-sdk-install); [announcement, 15 October 2026](https://devblogs.microsoft.com/dotnet/introducing-dotnetup/); [InfoQ report](https://www.infoq.com/news/2026/10/dotnetup-preview/); [`dotnet-install` script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script); [LLVM 18.1.8 release archives](https://github.com/llvm/llvm-project/releases/tag/llvmorg-18.1.8).
