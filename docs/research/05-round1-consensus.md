# Vibranium Round 1 Roundtable Consensus (Fable + Astra + Carbon)

## Converged Core Architecture

All three advisors (`fable` on `claude-fable-5-2` xhigh, `astra` on `gpt-6-astra` xhigh, and `carbon` on `gemini-4-carbon-high`) converged on **Zero-Rewriting Dual Execution**:

1. **No Source or IL Rewriting:**
   - Vibranium never alters what valid C# means on `.NET`; it rejects unsafe or divergent programs via a single shared Roslyn + MIR analysis engine powering both `Vibranium.Analyzer` (live IDE diagnostics) and `vib` (native LLVM 18 compiler).
2. **Affine Memory Owners vs. Linear Resource Owners (Mojo 1.2 & Austral Model):**
   - **Memory-only types** (heap `class` instances, `Vec<T>`, structs holding owned memory): deallocating pure memory has zero observable side effects. On `.NET`, unreachable memory is collected invisibly by GC; on LLVM, `vib` emits deterministic `free` drop glue at scope exit / overwrite (verified leak-free under `clang-18 -fsanitize=address,leak`). Move is plain `var b = a;`.
   - **Side-effecting resource types (`IDisposable`)**: enforced **linear** (`VIB006`). Every path must either scope the resource with `using var` (non-movable) or move it to a consumer. Both `.NET` and LLVM execute `Dispose()` in identical deterministic order.
3. **Borrowing &SPARK-Style Borrowed Views:**
   - `in T` / `ReadOnlySpan<T>` = deep/transitive shared borrow (`VIB004` forbids any reachable write or `[Mut]` call).
   - `ref T` / `Span<T>` = exclusive mutable borrow (`VIB002`, `VIB003`).
   - Class methods: unmarked = `&self` (deeply immutable), `[Mut]` = `&mut self`.
   - Struct methods: `readonly` = `&self`, non-`readonly` (and C# 14 `void operator +=`) = `&mut self`.
   - Borrowed views (from SPARK Ada): reading a class reference out of borrowed data yields a borrowed view local holding an NLL loan that may monotonically advance into sub-parts.
   - Lifetimes: C# `scoped` + `[UnscopedRef]` + Vibranium heap-origin tracking (reserving `[return: LifetimeOf(nameof(x))]` for multi-borrow cases beyond `scoped`).
4. **Panics & Arithmetic Equivalence (Empirically Verified on CoreCLR + LLVM 18):**
   - Standalone unhandled CLR exceptions do not execute `using` `Dispose` / `finally` frames before `AppDomain.UnhandledException` calls `Environment.Exit(101)`, matching native `vib_panic` (`_exit(101)`). (Note from Astra: catching test runners do unwind `finally`, so panic-parity tests run as subprocesses.)
   - LLVM 18 masks shift counts (`rhs & 31` / `rhs & 63`) and guards `/ 0` and `MIN / -1` before `sdiv`/`srem`.
5. **LLVM 18 Aliasing & Capture Policy:**
   - `noalias` on `ref`, mutable `Span<T>`, `[Mut]` receivers, and moved-in owned pointers (sufficient to eliminate LLVM's runtime overlap check on vectorized loops).
   - `readonly` on `in` / `ReadOnlySpan<T>` (with optional `--shared-noalias` matching `rustc` `Freeze`).
   - Do not manually emit `nocapture`; let LLVM `opt-18` infer capture attributes soundly.
