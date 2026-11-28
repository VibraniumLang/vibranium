# Vibranium Spec Review v3 Consensus (Fable + Astra + Carbon)

Unanimous 3-way agreement (`spec-carbon`, `spec-astra`, `spec-fable`) reached on **Canonical Unified Fix List v3 (`C1`–`C7` for `src/Vibranium.Core/` + SDK props, `S1`–`S11` for `docs/SPEC.md`)**.

> **Applied Resolution Note:**
> - For `C3` + `C6`, `Vec<T>` adds `public Vec() { _buffer = []; _count = 0; }` so `new Vec<T>()` never yields a `null` `_buffer` on CLR, `VecBuilder.Create` uses `new Vec<T>(items.Length)` so `Vec<int> v = [];` allocates 0 bytes, and `VIB008` rejects `default(Vec<T>)` and unassigned `Vec<T>` fields.
> - For `C7`, `src/Vibranium.Core/Bootstrap/VibraniumAppBootstrap.cs` (`Vibranium.Internal.VibraniumAppBootstrap`, excluded from `Vibranium.Core.dll`) is injected into `OutputType == 'Exe'` builds via `Vibranium.App.props`, alongside `<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>`, `<InvariantGlobalization>true</InvariantGlobalization>`, and `<WarningsAsErrors>` promoting `CS8509`, `CS8524`, and `CS9193`.

---

# Part A — Concrete `src/Vibranium.Core/` (+ SDK `.props`) Fixes (`C1`–`C7`)

### `C1` [P0] `src/Vibranium.Core/Vec.cs` — Remove `readonly` from `Slice` and add `ReadOnlySlice`
- **Bug:** `public readonly Span<T> Slice(int start, int length)` returns a mutable `Span<T>` from a `readonly` struct method, allowing `in Vec<int> v` callers in stock C# (`v.Slice(0, 1)[0] = 42` or `v[0..1]`) to mutate `_buffer` with zero compiler errors.
- **Fix:**
  1. Remove `readonly` from `Slice`:
     ```csharp
     [UnscopedRef]
     public Span<T> Slice(int start, int length)
     {
         if ((uint)start > (uint)_count || (uint)length > (uint)(_count - start))
         {
             VibraniumRuntime.Panic("slice out of bounds");
         }
         return _buffer.AsSpan(start, length);
     }
     ```
  2. Add `ReadOnlySlice`:
     ```csharp
     [UnscopedRef]
     public readonly ReadOnlySpan<T> ReadOnlySlice(int start, int length)
     {
         if ((uint)start > (uint)_count || (uint)length > (uint)(_count - start))
         {
             VibraniumRuntime.Panic("slice out of bounds");
         }
         return _buffer.AsSpan(start, length);
     }
     ```

### `C2` [P0] `src/Vibranium.Core/Vec.cs` — Guard `Vec<T>.Push` against capacity & byte-size overflow
- **Bug:** `_buffer.Length * 2` overflows `int` to negative when `_buffer.Length > int.MaxValue / 2` (throwing `OverflowException` on CLR or UB/undersized `malloc` in LLVM).
- **Fix:** Guard against `Array.MaxLength` (`0x7FFFFFC7`) before allocating, cap doubling cleanly, and preserve `_buffer` / `_count` untouched until allocation succeeds (with matching max-capacity + `checked((nuint)nextCap * (nuint)sizeof(T))` byte-overflow -> `"Vec capacity overflow"` and `NULL` -> `"out of memory"` checks in `vib`):
  ```csharp
  public void Push(T item)
  {
      if (_count == _buffer.Length)
      {
          if (_count == Array.MaxLength)
          {
              VibraniumRuntime.Panic("Vec capacity overflow");
          }
          int nextCap = _buffer.Length == 0
              ? 4
              : (_buffer.Length > Array.MaxLength / 2 ? Array.MaxLength : _buffer.Length * 2);
          var grown = new T[nextCap];
          _buffer.AsSpan(0, _count).CopyTo(grown);
          _buffer = grown;
      }
      _buffer[_count] = item;
      _count += 1;
  }
  ```

### `C3` [P1] `src/Vibranium.Core/Vec.cs` — Null-safe `new Vec<T>()` (`_buffer == null` hazard)
- **Bug:** `new Vec<int>()` / `default(Vec<int>)` leaves `_buffer == null` on CLR, throwing `NullReferenceException` on `Capacity`/`Push`/`AsSpan`.
- **Fix:** Provide `public Vec() { _buffer = []; _count = 0; }` on `Vec<T>` so `new Vec<T>()` constructs a valid zero-capacity empty vector on CLR, while `VIB008` rejects `default(Vec<T>)` and unassigned `Vec<T>` fields.

### `C4` [P1] `src/Vibranium.Core/Runtime.cs` — Culture-invariant `Io`, UTF-8 output, stdout flush, & closed panic mapping
- **Bugs:**
  1. `Io.Print(int|long)` / `PrintLine(int|long)` call `Console.Out.Write(value)`, which uses `CultureInfo.CurrentCulture` (diverges on custom locale minus signs vs LLVM 18 ASCII formatting).
  2. `Span` range OOB throws `ArgumentOutOfRangeException` (falling into `ex.Message` -> CLR English message instead of `"slice out of bounds"`), and arbitrary CLR `ex.Message` strings cannot be matched natively.
  3. Buffered `Console.Out` must flush before `_exit(101)` / `Environment.Exit(101)`, and UTF-8 must be enforced without using nonexistent `Console.ErrorEncoding`.
- **Fix in `Runtime.cs`:**
  - In `VibraniumRuntime.Initialize()`: set `Console.OutputEncoding = System.Text.Encoding.UTF8;`.
  - In `UnhandledException`:
    ```csharp
    string message = args.ExceptionObject switch
    {
        VibraniumPanicException p => p.Message,
        IndexOutOfRangeException => "index out of bounds",
        ArgumentOutOfRangeException => "slice out of bounds",
        DivideByZeroException => "attempt to divide by zero",
        OverflowException => "arithmetic overflow",
        OutOfMemoryException => "out of memory",
        var obj => "internal error: " + (obj?.GetType().Name ?? "unknown"),
    };
    ```
  - In both `UnhandledException` and `Panic(string message)` (non-`InProcessTestMode`): call `Console.Out.Flush();`, write `"panic: " + message + "\n"` as raw UTF-8 bytes to `Console.OpenStandardError()`, flush, and call `Environment.Exit(101);`.
  - In `Io`: format `int` and `long` with `CultureInfo.InvariantCulture` (`Span<char> buf = stackalloc char[32]; value.TryFormat(buf, out int n, default, CultureInfo.InvariantCulture); Console.Out.Write(buf[..n]);`).

### `C5` [P1] `src/Vibranium.Core/Prelude.cs` — Allow `!Copy` `ref struct` in `Prelude.Move<T>`
- **Fix:** Update `Move<T>` to `public static T Move<T>(T value) where T : allows ref struct => value;` so `!Copy` `ref struct` views (`Span<T>`, `OptionMut<T>`) work with `Move(...)`.

### `C6` [P2] `src/Vibranium.Core/Vec.cs` — Zero-alloc empty collection expression `[]`
- **Fix:** Change `VecBuilder.Create` from `items.Length == 0 ? 4 : items.Length` to `items.Length` so `Vec<int> v = [];` does not heap-allocate capacity `4` when empty (`Push` already grows `0 -> 4`).
- *(Note on `Option.cs` / `Result.cs`: Keep `readonly` on `Option<T>.Unwrap()`, `UnwrapOr()`, `IsNone()` and `readonly struct Result<T, E>` so `Copy` receivers like `in Option<int>` continue compiling cleanly in C#, while `S2` enforces consuming semantics on `!Copy` receivers.)*

### `C7` [P0] `Vibranium.App.props` / SDK Build Integration — Entry-assembly panic bootstrap & compiler flags
- **Empirically Reproduced Bug:** If an Vibranium app (`Program.cs`) traps (e.g., `int z = 0; return 1 / z;` or `Span` OOB) before touching any symbol in `Vibranium.Core.dll`, CoreCLR never loads `Vibranium.Core.dll`, its `[ModuleInitializer]` never runs, and `.NET` crashes with a CLR stack trace and exit code `134` instead of `"panic: attempt to divide by zero"` and exit `101`. Also, unnamed enum `switch` exhaustiveness warning `CS8524` was not promoted to an error in `Vibranium.App.props`.
- **Fix in `Vibranium.App.props` + `src/Vibranium.Core/Bootstrap/VibraniumAppBootstrap.cs`:**
  1. Inject `Vibranium.Internal.VibraniumAppBootstrap` (`[ModuleInitializer]` calling `global::Vibranium.VibraniumRuntime.Initialize();`) into `Exe` app compilations via MSBuild — zero user-source rewriting, trivially skipped by `Vibranium.Frontend.Roslyn` / `vib`.
  2. Add `CS8524` to `<WarningsAsErrors>$(WarningsAsErrors);CS8509;CS8524;CS9193</WarningsAsErrors>`.
  3. Pin `<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>` and `<InvariantGlobalization>true</InvariantGlobalization>`.

---

# Part B — Concrete `docs/SPEC.md` & Analyzer/Backend Contract Fixes (`S1`–`S11`)

### `S1` [P0] `Span<T>` is `!Copy` (with Reborrowing), Intrinsic `Vec`/`Span` Place Projections, Two-Phase Borrows, & `foreach` (`VIB000`, `VIB002`, `VIB003`, `VIB004`)
1. **`Span<T>` vs `ReadOnlySpan<T>`:** Classify `ReadOnlySpan<T>` as `Copy` and **`Span<T>` as `!Copy`** (move on `var s2 = s1;`, with implicit reborrowing when passed as a `Span<T>`/`ReadOnlySpan<T>` call arg, sliced via `s[a..b]`/`s.Slice(...)`, indexed, or `foreach`-iterated — freezing parent `s` under `VIB003` and rejecting overlapping spans in one call like `Foo(s, s)` or `Foo(s[0..2], s[1..3])` under `VIB002`). Mandatory for Aliasing XOR Mutability and sound LLVM 18 `noalias` SIMD. Restrict `Span<T>`/`ReadOnlySpan<T>` elements to `Copy` types in v0.1, and state explicitly in §2 that `Vec<T>` requires `where T : unmanaged` (which implies Vibranium `Copy` once `struct : IDisposable` is banned in `S6`).
2. **Intrinsic Indexing & Two-Phase Borrows:**
   - `v[i]` and `span[i]` in **value-read position** require only a **shared** borrow (legal on `in Vec<T>`); writes, compound assignment (`+=`), and `ref v[i]` require an **exclusive** borrow (`VIB004` on `in Vec<T>`).
   - **Two-phase reservation:** Assignment LHS `ref`s (`v[i] = rhs`) and mutable call receivers / `ref` args (`v.Push(v.Count)`) are **reserved** when evaluated and **activated** at the store/call. Between reservation and activation, shared reads of the same root (`v[i] = v[i - 1] + v[i - 2]`, `v.Push(v.Count)`) are permitted, whereas any intervening mutation, move, or exclusive borrow (`v[0] = PushAndGet(ref v)`) is rejected by **`VIB003`**. Preserve C# left-to-right order (LHS index bounds check/panic runs before RHS evaluation).
3. **Loops & Enumerators:** Ban explicit `.GetEnumerator()` calls and enumerator locals/types outside `foreach` (`VIB000`). `foreach (var x in v)` holds a shared loan on `v` (legal on `in Vec<T>`); `foreach (ref var x in v)` holds an exclusive loan on `v` (`VIB004` on `in Vec<T>`), both held across the entire loop body (`v.Push(...)` inside loop = `VIB003`).

### `S2` [P0] Fail-Closed `Vibranium.Core` / `Span` Intrinsic Table & Consuming/Guard Semantics (`VIB000`, `VIB001`, `VIB005`)
1. **Fail-Closed Member Table:** Explicit contract table in `SPEC.md` listing every supported `Vibranium.Core` member and whitelisted `Span<T>`/`ReadOnlySpan<T>` member (`this[int]`, `this[Index]`, `this[Range]`, `Slice(int)`, `Slice(int, int)`, `Length`, `IsEmpty`, `foreach`, implicit `Span<T> -> ReadOnlySpan<T>`), with receiver mode (`shared` / `exclusive` / `consume`), loan provenance, and canonical panic message. Any unlisted `Vibranium.Core` or BCL member (`Span.CopyTo`/`Fill`/`Clear`/`ToArray`, etc.) is rejected by **`VIB000`**.
2. **Consuming Intrinsics:** `Option<T>.Unwrap()`, `Option<T>.UnwrapOr(T)`, `Option<T>.IsNone(out T)`, `Result<T, E>.Unwrap()`, `Result<T, E>.UnwrapOr(T)`, `Result<T, E>.IsErr(out T, out Err<E>)`, and **explicit** direct accesses to `Some<T>`/`Ok<T>`/`Err<E>` `.Value`/`.Error`/`.Deconstruct` consume their receiver:
   - `Copy` receivers copy freely (including through `in`).
   - `!Copy` receivers mark owned locals **moved** (`VIB001` on reuse; outer scope drop suppressed) and reject fields, indexed elements, and borrowed places with **`VIB005`** (use `.Take()` first).
   - `UnwrapOr(fallback)` consumes both receiver and eagerly evaluated `fallback`; LLVM 18 drops unselected `!Copy` `fallback` (on `Some`/`Ok`) or `Err` payload `E` (on `Err`).
3. **Branch-Sensitive Guard Intrinsics:** `IsErr(out var ok, out var err)` and `IsNone(out var val)` are only valid as `if ([!]...)` guard conditions with fresh `out var` / `out _` declarations (`VIB000` otherwise):
   - For `res.IsErr(out var ok, out var err)`: `err` is initialized ONLY on the `true` branch and `ok` ONLY on the `false` branch (`VIB001` on reading inactive binding; LLVM 18 drops ONLY the active variant if unmoved). When one branch diverges (`return`/`break`/`continue`/`Panic`), the non-diverging branch's active binding remains initialized after the `if`; if neither diverges, neither is initialized after the `if`. Same rule for `opt.IsNone(out var val)` (`val` active only on `false`).

### `S3` [P0] Non-`readonly` Defensive-Copy Ban, `[Mut]` Body Verification, Pattern Bindings, & `.Value` Boxing Ban (`VIB004`, `VIB005`, `VIB000`)
1. **Defensive-Copy Ban:** Reject calling any non-`readonly` struct method, property accessor, `Slice`, `AsSpan`, `+=`, or write/ref indexer on a shared place (`in`, `readonly` field, unmarked class `this` field, shared pattern binding) under **`VIB004`** (except intrinsic `Vec<T>` value-read indexing and value-`foreach` in `S1`).
2. **`[Mut]` & `readonly` Body Verification:** Compiler verifies bodies of unmarked `class` methods and `readonly` `struct` methods enforce **deep transitive immutability** on `this` (`VIB004`, catching mutations through nested `class` references inside `readonly` struct methods that stock `csc` allows). Constructors initialize `this` mutably; property `set` and `void operator +=` require mutable receivers.
3. **Pattern Bindings (Compiler-Generated Positional `Deconstruct`):**
   - Rvalue scrutinees (`Call()`, `opt.Take()`, `Move(x)`) consume; bindings take ownership, and unmatched active `!Copy` variants are still dropped at match end.
   - Place scrutinees (`Option<T>`, `Result<T, E>`) and `OptionRef<T>` borrow: `Copy` payloads yield ordinary independent copies; `!Copy` payloads (classes and `!Copy` structs like `Vec<int>`) yield **shared/immutable borrows** (`VIB004` bans reassignment, field mutation, `[Mut]`, non-`readonly` struct calls, and `ref`/`out` aliasing; `VIB005` bans moves — preventing `.NET` struct-copy lost writes on `Option<Vec<int>>`).
   - `OptionMut<T> where T : class` binding `Some<Node>(var n)` allows heap field writes (`n.Val = 1`) and `[Mut]` calls (`n.Next.AsMut()`, `n.Next.Take()`), while banning moving `n` (`VIB005`), reassigning `n = ...` (`VIB004`), **and passing or aliasing `n` by `ref` or `out` (`Bump(ref n)`, `Bump(out n)`, `ref var r = ref n`)** (`VIB004`/`VIB005`).
4. **Union `.Value` Boxing Ban:** Forbid `object` / `IUnion` / reference-widening patterns on `[Union]` types (`opt is object x`) that lower through `.Value` and heap-box (`VIB000`), while `var x` / `{ } x` binds the union struct itself without boxing.

### `S4` [P0] Expanded `VIB002` Exclusive Set, Reborrow Freezing, & `[LifetimeOf]` (`VIB002`, `VIB003`, `VIB007`)
1. Expand `VIB002`'s exclusive-borrow set explicitly to **`ref`, `out`, `[Mut]` class receivers, non-`readonly` struct receivers, `Span<T>`, and `OptionMut<T>`** (rejecting `F(out x, out x)`, `F(ref x, out x)`, `F(out x, in x)`, etc.).
2. `OptionMut<T>.AsRef()` creates a shared reborrow that **freezes** the parent `OptionMut<T>` against mutation (`Set`, `Replace`, `Take`, `AsMut`, mutable match) and move while the `OptionRef<T>` or any derived binding is live (`VIB003`). Cursor step-reborrows (`cur = n.Next.AsMut()`, `cur = n.Next.AsRef()`) preserve original root provenance plus moved cursor loan chains.
3. Document `LifetimeOfAttribute` (`Attributes.cs`): specify borrow-return lifetime elision (`this` when receiver is borrowed, else single borrowed input parameter) and `[return: LifetimeOf("p1", ...)]` verification in `VIB007` (returned provenance must be a subset of named borrowed parameters; cannot forge provenance or escape locals/by-value owners). Ban user-defined `ref struct`s and struct/parameter `[LifetimeOf]` in v0.1 (`VIB000`).

### `S5` [P0] Type Table (`string`, Composite `Copy`), Zero-Safety, & Constructors (`VIB008`, `VIB000`)
1. **Composite `Copy` Rule:** State explicitly that `Option<T>`, `Some<T>`, `Ok<T>`, `Err<E>` are `Copy` iff their payload is `Copy`, and `Result<T, E>` is `Copy` iff **both** `T` and `E` are `Copy`.
2. **`string` in §2:** Add `string` as a v0.1 `Copy`, **non-zero-safe** immutable UTF-8 literal slice (`{ ptr, i64 byte_len }` in LLVM 18, static storage, no drop glue). Restrict v0.1 `string` strictly to well-formed Unicode string literals (ban lone surrogates `\uD800`–`\uDFFF` under `VIB000`), variable pass/return, `Io.Print`/`PrintLine`, and `VibraniumRuntime.Panic`; forbid `string.Length`, indexing, comparison/equality, concatenation, and interpolation in v0.1 (`VIB000`) so CLR UTF-16 vs native UTF-8 never diverge. Native `string` output uses explicit byte length (`write(fd, ptr, byte_len)`), never NUL-terminated `%s`/`strlen` (embedded `\0` is preserved identically).
3. **Zero-Safety & Constructors (`VIB008`):**
   - List `Vec<T>` and `string` under **Non-zero-safe types** (`default(Vec<T>)` banned; `[]` or `new Vec<T>(capacity)` preferred, with null-safe `public Vec()` on CLR) and `Span<T>` / `ReadOnlySpan<T>` under **Zero-safe types** (empty `{ null, 0 }` span).
   - Require all non-zero-safe fields of a `class` or `struct` constructor to be definitely assigned before normal exit **AND before escaping `this` or calling any instance method on `this`** (`VIB008`).
   - Clarify `VIB008` wording: ban **null-forgiving postfix `x!`** (not boolean `!b`) and **nullable type annotations `T?`** (not conditional `c ? a : b`).

### `S6` [P0] Linear `IDisposable` Exact-Once Policy (`VIB006`, `VIB000`)
- Explicitly include banning `IDisposable` types in **fields, arrays/collections, and generic type arguments** under `VIB006` (noting fallible `Option<L>`/`Result<L, E>` resource constructors as a known v0.1 limitation), and ban `struct : IDisposable` (`VIB000`/`VIB006`).
- Reject discarded linear returns/temporaries (`Open(p);`, `_ = Open(p);`, `Open(p).M()`) and overwriting a live unconsumed linear local (`VIB006`).
- Exact-once disposal: `using var r = ...` is non-movable, disposes deterministically in LIFO lexical order on normal scope exits, and forbids manual `r.Dispose()` (`VIB006`); non-`using` linear locals/parameters must be consumed on every normal path either by move (`using var _ = f;` / call / return) or by a single terminal `f.Dispose()` call that marks `f` moved/consumed (`VIB001` on reuse).

### `S7` [P0] Closed Supported Subset & Fail-Closed Trust Boundary (`VIB000`)
- Define the closed v0.1 C# subset under `VIB000`: forbid `: BaseClass` inheritance, arbitrary interfaces (except `class : IDisposable`) and interface variables/casts/boxing, `object`/`dynamic`/boxing conversions, C# 15 shorthand `union` and `closed` hierarchies, user-defined `[Union]` and user-defined `ref struct` types, `[StructLayout(Explicit)]`/`[FieldOffset]`/`[InlineArray]`, by-value property getters returning `!Copy` fields (`VIB005`) & `!Copy` tuple deconstruction, `unsafe`/pointers/`fixed`/`stackalloc`, `async`/`await`/`yield`/threading, delegates/lambdas/events/capturing local functions, `try`/`catch`/`finally`/`throw`, finalizers (`~C()`), static constructors & mutable `static` fields, reflection, raw `T[]` arrays in user code, unstructured `goto`, `checked`, and unwhitelisted external BCL/PInvoke calls.
- Entry point: parameterless `static void Main()` / `static int Main()` (or top-level statements not referencing `args`, since `string[] args` is a banned `T[]`).
- Trust boundary: `#pragma warning disable`, disabled analyzers, and unverified external C# callers are outside CLR IDE guarantees; **`vib` always independently runs full verification and fails closed**.

### `S8` [P1] MIR Drop Elaboration, Iterative Drop, LLVM 18 Lowering, Arithmetic, IEEE-754 FP, Traps & `noalias`
1. **Target & Layouts:** 64-bit targets (`nuint`/`nint` = `i64`); `class` -> `malloc(max(1, bytes))` (`NULL` -> `"out of memory"` panic), recursive field drop + `free`; **emit an iterative drop loop for `class` types with a single self-recursive `Option<Self>` field (e.g. linked-list `Node`)** so deep lists drop in $O(1)$ stack space without segfaulting on LLVM while GC succeeds on `.NET`. Never emit `nonnull`/`dereferenceable` on `Span`/`ReadOnlySpan` data pointers; never emit LLVM `mustprogress` on loops.
2. **Evaluation Order & Overwrite Drops:** Normal-exit LIFO drops with `false`-initialized dynamic drop flags for branched/short-circuited (`&&`/`||`/`?:`) values. For assignment `lhs = rhs`: **evaluate `lhs` place operands first in C# order (including `Vec`/`Span` index bounds check/panic), then evaluate `rhs`, then drop `lhs`'s prior value iff `lhs` is still initialized/unmoved after `rhs` completes, then store** (handling `x = x;`, `x = Step(x);`, and `out` overwrites cleanly). `OptionMut.Set` and `*ref = v` overwrite-drop; `Take` and `Replace` move out without dropping.
3. **Canonical Panic Table & Abort Semantics:** Require entry-assembly bootstrap (`C7`); list the 10 canonical panic strings (`index out of bounds`, `slice out of bounds`, `attempt to divide by zero`, `arithmetic overflow`, `called Option.Unwrap() on None`, `called Option.UnwrapMut() on None`, `called Result.Unwrap() on Err`, `Vec capacity cannot be negative`, `Vec capacity overflow`, `out of memory`); flush `stdout` before `_exit(101)`; panic aborts immediately without running drops/`Dispose` (`InProcessTestMode` is test-host-only; OOM and stack exhaustion are best-effort exclusions; document exact v0.1 `Io` overloads: `int`, `long`, `bool`, `string`, `Line()`).
4. **Arithmetic, IEEE-754 FP, & `noalias`:**
   - Default `unchecked` wrapping two's-complement `+`, `-`, `*`, unary `-` (no `nsw`/`nuw`; `byte` promotes to `int`); explicit pre-checks before `sdiv`/`udiv`/`srem`/`urem` (`divisor == 0` -> `"attempt to divide by zero"`; signed `MIN / -1` **and** `MIN % -1` -> `"arithmetic overflow"`); masked shift counts (`& 31` for 32-bit, `& 63` for 64-bit).
   - Strict IEEE-754 FP (`float`/`double`, no `-ffast-math`/`fast` flags). **Preserve Roslyn `SemanticModel` constant-folded values for compile-time constant expressions (where C# folds `unchecked((byte)300d)` to `0`), and lower runtime float-to-integer casts directly to `llvm.fptosi.sat.iN` / `llvm.fptoui.sat.iN` at the destination integer width** (`NaN -> 0`, saturating to `[Min, Max]`, empirically verified identical to `.NET 11` runtime).
   - Emit LLVM `noalias` strictly on verified exclusive pointer parameters (`ref`, `out`, `[Mut]` `this`, `Span<T>`, `OptionMut<T>`, owned `!Copy` `class`/`Vec` params); never assume two shared `in`/`ReadOnlySpan`/`OptionRef` parameters cannot alias each other.

### `S9` [P2] `SPEC.md` §3 Idiom & Property Clarifications
- Replace nonexistent `foreach (var n in head.Iter())` with `for (var cur = head.AsRef(); cur is Some<Node>(var n); cur = n.Next.AsRef())`.
- Document `Option.IsSome`/`IsEmpty` and `Result.IsOk`, and note explicitly that `.AsMut()` / `.Take()` require mutable **fields** (`public Option<Node> Next;`) rather than properties (`CS0206`).

### `S10` [P1] `SPEC.md` Intro & Scope Qualifications
- Scope memory-safety, leak-freedom, and `.NET`-vs-LLVM parity guarantees strictly to verified programs within the closed v0.1 subset on supported 64-bit targets (ASan/LSan runs are validation evidence, not formal proof).
- Clarify "zero C runtime files" means zero C source/shim files in the Vibranium repository (`clang-18` still links platform libc/CRT for `main`/`malloc`/`free`/`write`/`_exit`).

### `S11` [P0] Generics & Monomorphization Specification
- Definition-site checking treats a type parameter `T` as **affine (`!Copy`), non-zero-safe, and non-linear** unless constrained with `where T : unmanaged` (which grants `Copy`, while keeping `T` **non-zero-safe** so `default(T)` / `new T()` is rejected by `VIB008`, and `static T Dup<T>(in T x) => x;` without `where T : unmanaged` is rejected by `VIB005`).
- Supported v0.1 generic constraints: `class`, `struct`, `unmanaged`, `allows ref struct` only (interface/base/`new()` constraints = `VIB000`).
- Linear `IDisposable` types are banned as generic type arguments everywhere, including inferred generic method calls (`VIB006`).
- `vib` monomorphizes all generic instantiations and rejects polymorphic recursion (`F<T>` transitively instantiating `F<Wrap<T>>`, `VIB000`).
