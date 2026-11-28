# Vibranium Round 2 Consult Summary (Null-Free Option/Result, Error Propagation, Plain Classes, Self-Hosting)

All three advisors (`fable-r2`, `astra-r2`, `carbon-r2`) ran empirical checks against `.NET 11.0.100` RTM and `Microsoft.CodeAnalysis.CSharp 5.12.0` and reached unanimous agreement on all four Round 2 questions:

## 1. Ban `null` and `T?` Completely in Favor of C# 15 `[Union]` `Option<T>` & `Result<T, E>`
- **Unanimous YES.** `VIB008` bans `null` literals, nullable reference types (`Node?`), `Nullable<T>` (`int?`), null-forgiving `!`, and `??`/`?.` in Vibranium source code.
- **Zero-Safety (`default` & Array Gate):**
  - *Zero-safe types* (`default` allowed): primitives, enums, `NoneType`, `Option<T>` (zeroed memory = `None`), and structs composed recursively of zero-safe fields.
  - *Non-zero-safe types* (`default`, implicit `new S()`, `new T[n]`, and unassigned fields rejected by `VIB008`): `class` types (`Node`), `Result<T, E>`, `Some<T>`, `Ok<T>`, `Err<E>`, and structs containing non-zero-safe fields. This closes C# NRT's notorious `default(StructWithClass)` and `new Node[n]` soundness holes.
- **Roslyn `5.12.0` CFG Facts (Empirically Verified):**
  - `Microsoft.CodeAnalysis.CSharp 5.12.0` loads cleanly in both `netstandard2.0` (`Vibranium.Analyzer`) and `net11.0` (`vib`).
  - `ControlFlowGraph.Create` preserves high-level `IIsPatternOperation` + `IRecursivePatternOperation` / `ITypePatternOperation` nodes on `[Union]` switches (before `UnionMatchingRewriter` lowers them to `TryGetValue`), plus an unreachable compiler-synthesized `SwitchExpressionException` block which `vib` elides once union exhaustiveness (`CS8509`) is verified.
  - Direct user calls to `.TryGetValue(...)`, `.HasValue`, and `.Value` (the C# 15 union protocol members) are forbidden in Vibranium code so zero boxing occurs, while `.Value` returns the truthful boxed case for `.NET` debugger watch windows.
- **Match & Cursor Ergonomics:**
  - Matching on a **place** (`opt switch { ... }`, `opt is Some<T>(var x)`) **borrows** the payload (for borrowed struct payloads, the binding is read-only since CLR `Deconstruct` copies structs; for class payloads, it borrows the heap node).
  - Matching on an **rvalue** (`Compute() switch { ... }`, `opt.Take() is Some<T>(var x)`, `Move(res) switch { ... }`) **consumes/moves** the payload.
  - Moving out of a field uses `fieldOpt.Take()` (requiring a mutable place receiver; rejected on `in`, `readonly` fields, properties, and rvalues).
  - Clean cursor traversal without `ref readonly var cur = ref head;` ugliness: `Option<T>.AsRef()` / `Option<T>.AsMut()` return stack-only `[Union] readonly ref struct` views (`OptionRef<T>`, `OptionMut<T>`), enabling Rust-clean `var cur = head.AsRef(); while (cur is Some<Node>(var n)) { ... cur = n.Next.AsRef(); }`.

## 2. Rust-Style Error Propagation in Valid C# 15
- **Unanimous Winner: Guard Pattern `(A)`:**
  ```csharp
  if (SafeDiv(x, y).IsErr(out var q, out var err)) return err;
  if (Find(key).IsNone(out var v)) return None;
  ```
  - Verified zero-allocation on `.NET 11.0.100`, works with `in`, `ref`, `Span<T>`, and `using var` deterministic disposal, and C# 15 `[Union]` implicitly converts `Err<E>` to any `Result<U, E>` even when `T != U`.
  - Modeled as a branch-sensitive intrinsic in Vibranium's move/init checker (`err` initialized on true branch, `q` initialized on false branch).
- **Why `(B)` `async` Builder Was Rejected:** `CS1983` rejects 2-parameter `Result<T, E>` task-likes, `CS1988` forbids `in`/`ref` parameters on `async` methods, and un-resumed continuations skip `using var` `Dispose()` blocks!
- **Why `(C)` LINQ Query Syntax Was Deferred:** Lowers to heap-allocating `SelectMany` closures (`104 bytes/call`) that cannot capture `in`/`ref`/`Span<T>`. (`Map`/`AndThen` with `static` lambdas can be added later as sugar.)

## 3. Plain `class Node` Without Mandatory `sealed`
- **Unanimous YES.** `VIB000` rejects `: BaseClass` inheritance, `abstract`, `virtual`, `override`, `protected`, and `new` member hiding. Every Vibranium `class` is concrete and final in the verified world without requiring `sealed` boilerplate (`sealed` remains optional; a lint can recommend `sealed` only when exporting public types to unchecked `.NET` consumers).

## 4. Gradual Self-Hosting ("Writing Vibranium in Vibranium")
- **Unanimous YES (Middle & Backend First):**
  - Because every Vibranium project compiles to a standard `.NET` assembly, Vibranium has a permanent stage-0 compiler (`csc`) with zero throwaway bootstrap language.
  - Keep `Vibranium.Frontend.Roslyn` as a thin managed adapter that snapshots Roslyn symbols/CFG into Roslyn-free arena/ID-indexed MIR structures (`Vibranium.Compiler.Core`).
  - `Vibranium.Compiler.Core` (MIR, NLL borrow/move/linearity solver, drop elaborator, monomorphizer, LLVM 18 emitter) has zero dependency on Roslyn, so it runs as `.NET` inside the IDE analyzer *and* can be compiled by `vib` itself as `Vibranium.Core` grows.
