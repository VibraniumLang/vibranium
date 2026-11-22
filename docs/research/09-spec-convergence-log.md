# Vibranium v0.1 — Multi-Round Spec & Core Convergence Log

This log records iterative 3-way Spec & `Vibranium.Core` convergence rounds (`Fable` + `Astra` + `Carbon`) following Round 1 (`docs/research/08-spec-review-v3-consensus.md`, `C1`–`C7` + `S1`–`S11`).

---

## Round 2 (`fable-r2`, `astra-r2`, `carbon-r2` → `fixer-r2`)

- **Reviewers:** `fable-r2` (`claude-fable-5-2`), `astra-r2` (`gpt-6-astra`), `carbon-r2` (`gemini-4-carbon-high`)
- **Unanimous Verdict:** `3 FIXES AGREED` (`src/Vibranium.Core/*` required 0 changes; 3 narrowing/parity fixes applied across `Vibranium.App.props` and `docs/SPEC.md` by `fixer-r2`, verified with `dotnet build src/Vibranium.Core/Vibranium.Core.csproj --no-restore` → 0 warnings, 0 errors).

### Agreed & Applied Fixes (`R2-F1` – `R2-F3`)

1. **`R2-F1` — Promote `CS8846` (`when`-guarded non-exhaustive `switch` expressions) & require independent `vib` exhaustiveness (`Vibranium.App.props`, `docs/SPEC.md` §1 item 1):**
   - **Defect:** When an uncovered `switch` expression case is touched by a `when`-guarded arm (e.g., `opt switch { Some<int>(var x) when b => x, NoneType => 0 }`), Roslyn emits warning `CS8846` rather than `CS8509`/`CS8524`. Under shipped `Vibranium.App.props`, user projects built with 0 errors and trapped at runtime with `panic: internal error: SwitchExpressionException` (exit `101`), which has no canonical panic or native lowering in `docs/SPEC.md`.
   - **Resolution:** Added `CS8846` to `<WarningsAsErrors>` in `Vibranium.App.props` (`CS8509;CS8524;CS8846;CS9193`) and documented in `docs/SPEC.md` §1 item 1 that `CS8509`, `CS8524`, `CS8846`, and `CS9193` are promoted to errors and non-exhaustive `switch` expressions are likewise rejected by `vib` regardless of `#pragma`/MSBuild settings.

2. **`R2-F2` — Ban all non-`const` `static` fields and `static` auto-properties (`VIB000`) (`docs/SPEC.md` §1 Closed v0.1 C# Subset):**
   - **Defect:** Banning only "mutable `static` fields" left `static readonly` fields (and `static` get-only auto-properties) inside the subset. On `.NET 11`, non-constant static initializers run lazily under CLR `beforefieldinit` rules, wrap traps in `TypeInitializationException` (`panic: internal error: TypeInitializationException` instead of canonical panic messages), lack specified native initialization order/drop glue, and permit global interior mutation aliasing when holding `class` or `[Mut]` owners.
   - **Resolution:** Narrowed `docs/SPEC.md` §1 (`VIB000`) to ban all `static` fields or `static` auto-properties other than `const` (including `static readonly`).

3. **`R2-F3` — Ban `ref` / `out` parameters whose type is a borrowed view (`VIB000`) (`docs/SPEC.md` §3.3 Lifetimes & `[LifetimeOf]`):**
   - **Defect:** Lifetime checking (`VIB007`), elision, `[return: LifetimeOf(...)]`, and call-site loan propagation apply strictly to return values, while `[LifetimeOf]` on parameters is banned (`VIB000`). However, C# 15 permits borrowed-view `ref`/`out` parameters (`out Span<T>`, `out OptionRef<T>`, `ref Span<T>`, or via `where T : allows ref struct` instantiation) because `Vec<T>` view methods use `[UnscopedRef]` over a GC array and `class` fields live on the GC heap — enabling untracked loan escape and native use-after-free when a `Vec<T>` reallocates or a by-value owner drops at callee exit.
   - **Resolution:** Specified in `docs/SPEC.md` §3.3 Verification that `ref`/`out` parameters whose type is a borrowed view (`Span<T>`, `ReadOnlySpan<T>`, `OptionRef<T>`, `OptionMut<T>`, including via `allows ref struct` generic instantiation) are banned in v0.1 (`VIB000`); borrowed views leave a function only through its return value.
