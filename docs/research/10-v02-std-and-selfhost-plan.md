# Vibranium v0.2 Unified Implementation Plan & Compiler Audit

**Roundtable Synthesis:** `v02-astra` (`gpt-6-astra`), `v02-fable` (`claude-fable-5-2`), and `v02-carbon` (`gemini-4-carbon-high`)
**Date:** 2026-11-15
**Inputs Reconciled:**
- Astra v0.2 blueprint (primary-source glibc/man-pages/.NET 11 `v11.0.0` PAL audit & contracts)
- Fable v0.2 blueprint and prototype (measured T0/T1 prototype, `vib_rt.ll`, `vib_std.ll`, `http-server`)
- Read-only C0 source audit of `src/{Vibranium.Compiler.Core,Vibranium.Frontend.Roslyn,Vibranium.Analysis,Vibranium.Backend.Llvm,vib}`

---

## 1. Roundtable Convergence & Explicit Reconciliations (Astra vs. Fable)

Astra and Fable converged on all 10 core architectural pillars (two-tier `Vibranium.Std.Sys` with zero `.c` files, verbatim `vib_rt.ll` + `vib_std.ll` fragments, `"..."u8` + affine `StringBuf`, `IntMap<V>` + `StrMap<V>` over `Vec<T>`, L1 linear `Option<R>`/`Result<R, E>`, `{ Kind, Op }` `IoError`, Copy-record arenas instead of multi-variant `[Union]`, closed `Vib-B` bootstrap profile, `managed.ll == stage1.ll == stage2.ll` gate, and absolute-path `Process.Run`).

Where the two documents had minor naming, flag, or header differences, this unified plan locks a **single canonical choice** for implementation:

| Area | Astra (`V02_BLUEPRINT.md`) | Fable (`V02_BLUEPRINT.md` + `proto/`) | Locked Unified v0.2 Contract |
|---|---|---|---|
| **LLVM target triple & datalayout** | `x86_64-unknown-linux-gnu`; datalayout omitted `-i128:128` (§7) | `x86_64-pc-linux-gnu`; datalayout includes `-i128:128` (`vib_rt.ll`) | **Lock Fable's exact header** (`target triple = "x86_64-pc-linux-gnu"`, `target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"`), verified to match `clang-18 -S -emit-llvm` on the host without `-Wno-override-module`. Update `LlvmEmitOptions.TargetTriple` accordingly. |
| **Namespace layout in `Vibranium.Std`** | Sub-folder conceptual modules (`Text/`, `Collections/`, `IO/`, …) (§2) | Single C# namespace `Vibranium.Std` across folders (`Fs/`, `Text/`, `Collections/`, `Env/`, `Process/`, `Net/`, `Sys/`) (§1.1) | **Single namespace `Vibranium.Std`**, `<Using Include="Vibranium.Std" />` in `Vibranium.App.props`. Avoids `Vibranium.Std.Process.Process` collisions and keeps `Vib-B` import resolution fixed-list and unambiguous. |
| **CLI arguments carrier** | `Env.Args() -> Result<Args, IoError>` (§6) | `Env.Args() -> Result<ArgList, IoError>` (§4.4) | **`ArgList`** (`Count`, `Get(int)` returning `ReadOnlySpan<byte>`). Avoids `Args` method vs. `Args` type collision. |
| **Process builder method** | `Command.AddArg(...)` (§6) | `new Command(program)` + `cmd.Arg(value)` (§4.4) | **`Command(ReadOnlySpan<byte> program)` + `void Arg(ReadOnlySpan<byte> value)`**, `Process.Run(in Command) -> Result<int, IoError>`. |
| **IPv4 endpoint surface** | Listed `Ipv4Address`/`Ipv4EndPoint` in §2, `Ipv4Addr` + 3-arg `Bind(addr, port, backlog)` in §8 | `Ipv4Address` + `Ipv4EndPoint`; `TcpListener.Bind(Ipv4EndPoint, int backlog)`; `TcpStream.Connect(Ipv4EndPoint)` (§4.5) | **`Ipv4Address` + `Ipv4EndPoint`** (`new Ipv4Address(a, b, c, d)`, `Loopback()`, `Any()`, `new Ipv4EndPoint(addr, port)`). Endian-unambiguous octets; validates `0 <= port <= 65535`. |
| **`FileOpen` flags & symlink/dotdot parity** | Base `0x80000` / `0x80241`, flagged `O_NOCTTY` & symlink `..` as validation gates (§6, §7) | `0x80100` (`O_RDONLY\|O_CLOEXEC\|O_NOCTTY`) / `0x80341` (`O_WRONLY\|O_CREAT\|O_TRUNC\|O_CLOEXEC\|O_NOCTTY`), `PathText.Normalize` in T1, `System.IO.DisableFileLocking=true` (§5.1, §5.3) | **Adopt Fable's measured triple fix:** `O_NOCTTY` on native `open`, lexical `PathText.Normalize` in T1 before T0, and `System.IO.DisableFileLocking=true` in `Vibranium.App.props` so BCL `FileStream` never acquires `flock`. |
| **`IoErrorKind` errno coarsening** | Flagged `EROFS`/`EFBIG`/`EOVERFLOW` as BCL parity gate (§6) | Measured `.NET 11` maps `EROFS`/`ENOSPC`/`EEXIST`/`EFBIG` to plain `IOException`; coarsened both sides to `Other` (§5.2) | **Coarsen to `IoErrorKind.Other` on both sides** whenever `.NET 11` BCL does not distinguish via public exception type or `SocketError`. `FileTooLarge` is produced deterministically by T1 `Fs.ReadAllBytes` when exceeding `maxBytes`. |
| **Resource disposal syntax** | Wording mentioned `Dispose()` consuming receiver (§4) | Prototype T1 + `http-server` use `using var` exclusively; C0 (`CfgMirLowerer` line 1295) bans manual `.Dispose()` calls (`VIB006`) | **Keep manual `.Dispose()` calls banned (`VIB006`) in user and T1 Vibranium.** Disposal in Vibranium happens exclusively via `using var` (which lowers to `MirStatement.DisposeResource`: runs `Dispose()` then `@free(ptr)` on native, and C# `using` on managed, preventing use-after-dispose divergence in C#). Add `Prelude.Drop<T>(T value)` (`where T : class, IDisposable { using var _ = value; }`) only if early explicit drop is ever needed. |
| **HTTP/1.1 header rules in `samples/http-server`** | Require `Host` on HTTP/1.1; reject `Transfer-Encoding` and unsupported request bodies (§8) | Prototype parsed request line + 16 KiB CRLFCRLF cap (`GET`/`HEAD`, `200`/`400`/`404`/`405`/`431`/`505`) (`proto/samples/http-server/Program.cs`) | **Include bounded header validation loop (~35 extra LOC)** on top of Fable's prototype: require exactly one non-empty `Host` header for `HTTP/1.1` (`400` if missing/duplicate), reject `Transfer-Encoding` (`501`/`400`), and reject non-zero `Content-Length` (`400`/`413`). |

---

## 2. Practical Compiler Audit: Mapping v0.2 onto C0 (`SyntaxAndSymbolGatekeeper`, `CfgMirLowerer`, `BorrowAndMoveAnalyzer`, `Llvm18Emitter`)

Direct read-only inspection of `src/` surfaced **9 exact implementation requirements and bug hazards** that must be handled in Milestones M1–M3.

### 2.1 L1 Linear `Option<R>` / `Result<R, E>` — Soundness & Lowering Traps Found in C0

Good news first: in `Mir.cs` (`IsLinearCore`) and `VibraniumSymbolHelpers.cs` (`IsLinearResourceSymbol`), `Option<R>`, `Some<R>`, `Result<R, E>`, and `Ok<R>` already propagate linearity, and `CfgMirLowerer.Expressions.cs` (lines 443–454) already lowers `some.Value` / `ok.Value` as consuming `UnwrapSomeWrapper` / `UnwrapOkWrapper` rather than `MirProjection.Field`. Furthermore, `ExtractOutArgumentLocalId` (line 1106) and `LowerInvocation` with `destPlace: null` (line 1353) already allocate `CreateTempLocal(...)`, which `_isLinearOwnedLocal` tracks and rejects at `Return`/`ScopeExit` (`VIB006`) if unconsumed!

**Five specific gaps MUST be closed when lifting the v0.1 generic-linear ban:**

1. **Narrow the Generic Type-Argument Gate (`SyntaxAndSymbolGatekeeper.cs:810` & `CfgMirLowerer.Expressions.cs:1176`):**
   - Permit linear `R` **only** as:
     - `Vibranium.Option<R>`, `Vibranium.Some<R>`, `Vibranium.Prelude.Some<R>(R)`
     - `Vibranium.Result<R, E>` at **type argument index 0 (`T`) ONLY** — linear `E` at index 1 (`Result<_, R>`, `Err<R>`, `Prelude.Err<R>`) MUST remain rejected with `VIB006`! Update `MirModule.IsLinearCore` / `Gatekeeper` so `Err<R>` is never legal.
     - `Vibranium.Ok<R>`, `Vibranium.Prelude.Ok<R>(R)`, `Vibranium.Prelude.Move<R>(R)`.
   - All other generic types/methods (`Vec<R>`, `IntMap<R>`, `StrMap<R>`, user `Box<R>`) continue to emit `VIB006`.
2. **Close the `Some<R>.Deconstruct` / `Ok<R>.Deconstruct` Aliasing Hole (`Vibranium.Core/Prelude.cs:6,14`):**
   - `Some<T>` and `Ok<T>` are `readonly struct`s with `public void Deconstruct(out T value) => value = Value;`. Calling `w.Deconstruct(out var f1); w.Deconstruct(out var f2);` or positional deconstruction takes `ImplicitThisShared` (`in`) without consuming `w`, duplicating a linear handle on `.NET`!
   - **Fix:** Mark `Some<T>.Deconstruct` and `Ok<T>.Deconstruct` with `[Obsolete("Internal union protocol", error: true), EditorBrowsable(EditorBrowsableState.Never)]` (or gatekeep `Deconstruct` in `SyntaxAndSymbolGatekeeper` as `VIB000`/`VIB006`, since `CfgMirLowerer` already extracts union arms via Roslyn `IRecursivePatternOperation` / `TryExtractResultArm` without calling `Deconstruct` at runtime when lowered to MIR, or restrict positional deconstruction on linear `Some<R>`/`Ok<R>`/`Result<R,E>`/`Option<R>` when `Scrutinee` is a `PlaceScrutinee`). Specifically: in `Gatekeeper`, ban direct `.Deconstruct(...)` invocations, and in `CfgMirLowerer` / `BorrowAndMoveAnalyzer`, reject `MirMatchScrutinee.PlaceScrutinee` whenever `_module.IsLinearResource(ps.Type)` (`VIB006`).
3. **Ban Non-Linear Intrinsics & Nested Subpatterns on Linear Unions (`CfgMirLowerer` & `BorrowAndMoveAnalyzer`):**
   - Emit `VIB006` if the union's payload `R` is linear on:
     - `OptionUnwrapOr`, `ResultUnwrapOr` (would drop one of two linear values without `Dispose()`)
     - `OptionTake`, `OptionReplace`, `OptionAsRef`, `OptionAsMut`, `OptionUnwrapMut`
     - `MatchOption` / `MatchResult` with `PlaceScrutinee`, or with `extraSubpattern != null` (`if (File.OpenRead(p) is Ok<File> { ... })`, because a failed subpattern branches to `errTarget` after `okBindingId` was already extracted).
4. **Handle Discard Target `IDiscardOperation` in `LowerSimpleAssignment` (`CfgMirLowerer.Expressions.cs:138`):**
   - Currently `IDiscardOperation` (`_`) is handled for `out _` arguments (line 2571) and guard `out _` (line 1106), **not** in `LowerPlace` for `_ = File.OpenRead(path);`! Add `case IDiscardOperation:` in `LowerSimpleAssignment` / `LowerPlace` creating `CreateTempLocal(targetMirType, span, "__discard_assign")`, ensuring `_ = File.OpenRead(path);` immediately triggers `VIB006` at scope exit / return.
5. **Prevent `EmitDropFunction` from Generating Non-Disposing Drop Glue on `Result<R, E>` / `Option<R>` (`Mir.cs:461` & `Llvm18Emitter.cs:952`):**
   - Because `MirModule.NeedsMemoryDrop(type)` is `!IsCopy(type) && !IsLinearResource(type) && !IsViewLike(type)`, any `Result<R, E>` where `R` is linear has `IsLinearResource == true` and `NeedsMemoryDrop == false`.
   - **Important subtlety:** What if `E` in `Result<R, E>` were an affine heap-owning struct (like `StringBuf`) while `R` is linear? If `Result<R, AffineE>` took the `Err` branch or `Unwrap()` panicked, drop flags would need per-variant split cleanup. In v0.2, `IoError` is `Copy`, and to keep L1 completely airtight without mixed linear+affine drop glue, **restrict L1 `Result<R, E>` so that when `R` is linear, `E` must be `Copy`** (`!NeedsMemoryDrop(E)` and `!IsLinearResource(E)` — emit `VIB006` otherwise). Every v0.2 use (`Result<File, IoError>`, `Result<TcpListener, IoError>`, `Result<TcpStream, IoError>`) has `Copy` `IoError`!

### 2.2 Prototype IR Bugs Caught in `vib_rt.ll` & `vib_std.ll` (Must Fix When Porting to Repo)

1. **LLVM 18 Undefined Behavior on Zero-Length `llvm.memcpy` with Null Span Pointer (`vib_rt.ll` & `vib_std.ll`):**
   - In LLVM 18 LangRef, `@llvm.memcpy.p0.p0.i64(ptr %dst, ptr %src, i64 %len, i1 false)` requires valid non-null pointers **even when `%len == 0`**.
   - In `Env.Args()` (`proto/src/Vibranium.Std/Env/Env.cs:39`), `var bytes = new Vec<byte>();` starts with `data = null`, so `Sys.ArgCopy(i, bytes.AsSpan().Slice(0, 0))` passes `%dst.ptr = null, %dst.len = 0` into `@__vib_sys_arg_copy`, which executes `@llvm.memcpy(ptr null, ptr %s, i64 0, i1 false)` — **UB under `clang-18 -O2/-O3` and UBSan!**
   - Likewise, `@__vib_vec_extend` (`vib_rt.ll:136`) called with `%n == 0` on a zero-capacity `Vec` (`data == null`) executes `@llvm.memcpy(ptr null, ptr %src, i64 0, i1 false)`.
   - **Fix:**
     - In `@__vib_vec_extend`: check `icmp sle i32 %n, 0` at entry (after negative check -> panic, `0` -> immediate `ret void`).
     - In `@__vib_sys_arg_copy`: check `icmp eq i64 %n, 0` and branch over `@llvm.memcpy` directly to returning `%len32`.
     - In `@__vib_sys_file_open`: check `icmp sle i32 %path.len, 0` -> `ret i32 -6` (`InvalidInput`) before `@llvm.memcpy`.
     - For empty `"..."u8` (`""u8`) literals in both C0 and `vib-self`: emit `{ ptr @.b_empty, i32 0 }` backed by `@.b_empty = private unnamed_addr constant [1 x i8] zeroinitializer`, never `{ ptr null, i32 0 }`.
2. **Enforce Defensive Bounds & Final-NUL Check Inside `@__vib_sys_spawn_wait` (`vib_std.ll:448`):**
   - Fable's §14 notes that `@__vib_sys_spawn_wait` re-checks arena offsets and NUL termination so T1 bugs cannot cause out-of-bounds reads, while the prototype `vib_std.ll` currently checks only `%starts.len < 1`.
   - **Fix:** Before `malloc`, check `%arena.len < 1` or `arena[%arena.len - 1] != 0` -> `ret i32 -6`; inside `%fillbody`, check `icmp uge i32 %s, %arena.len` -> `free(%argv)` and `ret i32 -6`. (~12 lines of IR).

### 2.3 Exact `Vibranium.Std.Sys` Catalog Plumbing in C0 & `Llvm18Emitter`

The 15 T0 functions in `Vibranium.Std.Sys` (`FileOpen`, `FileRead`, `FileWriteAll`, `FileSync`, `Close`, `TcpListen`, `TcpAccept`, `TcpConnect`, `SockRecv`, `SockSendAll`, `SockShutdownWrite`, `SockLocalPort`, `SpawnWait`, `ArgCount`, `ArgCopy`) require **zero custom MIR statement kinds** — they lower as ordinary `MirStatement.Call` with Vibranium's existing call ABI (`int` -> `i32`, `long` -> `i64`, by-value `Span<byte>`/`ReadOnlySpan<byte>`/`ReadOnlySpan<int>` -> flattened `(ptr, i32)`).

To connect them cleanly to `Llvm18Emitter.cs`:
1. **Catalog Gate (`SyntaxAndSymbolGatekeeper` & `vib-self` Declare):**
   - Hard-code the versioned 15-entry table `(MethodName, NativeSymbol, ReturnType, ParamTypes[])`.
   - Any `[Native]` attribute outside `Vibranium.Std.Sys` (in unit `std`) or with a mismatched signature emits `VIB000`.
   - Manifest-listed `managed` files (`Sys/Sys.Managed.cs`) are included in Roslyn's `CSharpCompilation` so `partial` method declarations bind cleanly, and skipped during `SyntaxAndSymbolGatekeeper` + `VibraniumFrontend` MIR lowering.
2. **External Symbol Binding Without Duplicate `declare` (`Mir.cs` & `Llvm18Emitter.cs:592`):**
   - Add `string? NativeSymbol = null` to `MirFunctionSignature`.
   - When `sig.NativeSymbol` is set, `ModuleEmitter.GetFunctionSymbol` returns `$"@{sig.NativeSymbol}"` directly, and `EmitExternalDeclarations` **does not** emit `declare ...` (since `vib_std.ll` defines `define internal ... @__vib_sys_*` in the same LLVM module, and `clang-18` rejects duplicate declarations).
3. **Runtime Fragment Composition (`Llvm18Emitter.cs:197–228`):**
   - Move `@__vib_panic`, `@__vib_print_i64`, `@__vib_print_u64`, `@__vib_print_i32`, `@__vib_print_bool`, `@__vib_print_str`, `@__vib_print_nl`, `@__vib_write_out`, `@__vib_write_err`, `@__vib_vec_reserve`, `@__vib_vec_extend`, `@__vib_vec_resize`, `main(i32 %argc, ptr %argv)`, and all libc / `llvm.sadd.with.overflow.*` / `llvm.fptosi.sat.*` declarations into `src/Vibranium.Core/Native/x86_64-linux-gnu/vib_rt.ll`.
   - Both C0 and `vib-self` emit `define internal i32 @__vib_entry()` for user `Main` (handling both `void Main()` returning `0` and `int Main()` returning its `i32`).
   - Prepend `vib_rt.ll` always, and `vib_std.ll` iff `Vibranium.Std` is in the manifest closure.

### 2.4 Core Intrinsics & `[UnscopedRef]` Lowering Checklist

1. **`Unit` & `Prelude.Ok()`:**
   - `public readonly struct Unit;` in `Vibranium.Core`; `public static Ok<Unit> Ok() => new(default);` in `Vibranium.Prelude`.
   - Lowers as ordinary zero-field Copy struct (`{ i8 }` in LLVM IR, matching `NoneType`).
2. **`Stdio.WriteOut(ReadOnlySpan<byte>)` & `Stdio.WriteErr(ReadOnlySpan<byte>)`:**
   - Add `MirIntrinsicKind.StdioWriteOut` and `MirIntrinsicKind.StdioWriteErr` calling `@__vib_write_all(i32 1, ptr %p, i64 %len64)` and `@__vib_write_all(i32 2, ptr %p, i64 %len64)` in `vib_rt.ll`.
3. **Five `Vec<T>` Primitives (`MirIntrinsicKind`):**
   - `VecClear`: inline store `0` to `count` (`MirPassKind.ImplicitThisMut` conflict check invalidates active span borrows!).
   - `VecTruncate`: inline `icmp ugt i32 %len, %cnt` -> panic `"truncate out of bounds"`, else store `%len` to `count`.
   - `VecReserve`: `call void @__vib_vec_reserve(ptr %vecSlot, i32 %add, i64 sizeof(T))`.
   - `VecExtend`: `call void @__vib_vec_extend(ptr %vecSlot, ptr %spanPtr, i32 %spanLen, i64 sizeof(T))` (note: in `BorrowAndMoveAnalyzer`, `Extend` takes `ImplicitThisMut` on `vec` and reads `span`, so `v.Extend(v.AsReadOnlySpan())` is automatically rejected with `VIB002`/`VIB003`!).
   - `VecResize`: spill `%fillVal` to temp `alloca`, `call void @__vib_vec_resize(ptr %vecSlot, i32 %newLen, ptr %fillSlot, i64 sizeof(T))`.
4. **`"..."u8` UTF-8 String Literals:**
   - In `CfgMirLowerer.Expressions.cs`: handle `IUtf8StringOperation` (and `IConversionOperation` wrapping UTF-8 literal) by emitting `MirConstant.Utf8BytesConst(byte[] Bytes)` with type `SpanType(UInt8, IsReadOnly: true)`.
   - In `BorrowAndMoveAnalyzer`: `MirOperand.Const` produces an empty `HashSet<Loan>` (static provenance — legal to return from any function or store in locals).
   - In `Llvm18Emitter`: pool byte arrays structurally into `@.b0`, `@.b1`, … (`[N x i8] c"..."`, or `[1 x i8] zeroinitializer` when `N == 0`) and materialize `{ ptr @.bK, i32 N }`.
5. **`[UnscopedRef]` on User Struct Methods (`StringBuf.AsUtf8`, `StrMap.KeyAt`, `Interner.Text`, `ArgList.Get`, `Command.Arena/Starts`):**
   - Let `SyntaxAndSymbolGatekeeper` permit `[System.Diagnostics.CodeAnalysis.UnscopedRef]` on `struct` instance methods/properties.
   - Verify `BorrowAndMoveAnalyzer`'s call-return loan summary treats `this` (`MirPassKind.ImplicitThisShared` / `ImplicitThisMut`) on `[UnscopedRef]` struct methods identically to lifetime elision from parameter 0 (`Loan.ForPlace(recvPlace)`), and rejects returning stack-local loans if `this` was a materialized rvalue temp (`new StringBuf(8).AsUtf8()` -> `VIB007` dangling borrow if kept past statement!).

---

## 3. Self-Hosted `vib` (`src/vib-self/`) — Concrete Scope & Module Budget (~4,650 LOC)

To guarantee `src/vib-self/` is genuinely buildable in clean `Vib-B` within **~4,500–5,000 LOC** and hits `sha256(managed.ll) == sha256(stage1.ll) == sha256(stage2.ll)`, we lock the exact module decomposition and trim the `Vib-B` Core catalog to the **32 intrinsics actually used** by `Vibranium.Std`, `samples/http-server`, and `vib-self` itself (no cursors, no pattern `switch`, no floats/128-bit ints, no collection expressions):

### 3.1 Exact Module Decomposition (`src/vib-self/`)

| File | Responsibility | Key Arena / Table Types | Est. LOC |
|---|---|---|---|
| `src/vib-self/vib.manifest` | Deterministic build manifest (`unit app`, `use ../Vibranium.Core`, `use ../Vibranium.Std`, ordered `source` list) | — | 25 |
| `Driver/Program.cs` | CLI (`check`, `emit-ll`, `build`, `--sysroot`, `-o`, `--cc`, `-O0..-O3`), exit codes (`0`/`1`/`2`), `Process.Run` clang driver | `CliOptions` | 180 |
| `Driver/Manifest.cs` | Parser for `vib.manifest` closure (`core` -> `std` -> `app`), unit tracking, source loading via `Fs.ReadAllBytes` | `UnitKind`, `ManifestEntry` | 190 |
| `Diagnostics/DiagnosticBag.cs` | Stable codes (`VIB000`–`VIB009`), lazy byte-offset -> `(line, col)` lookup, canonical `<logicalPath>(<line>,<col>): error <code>: <msg>` rendering | `Diagnostic`, `SourceFile` | 160 |
| `Ast/IdsAndNodes.cs` | Distinct `enum` IDs (`SourceId`, `TokId`, `NodeId`, `DeclId`, `TypeId`, `LocalId`, `BlockId`, `InstId`), `Token`, `AstNode` (`Kind`, `Tok`, `A`, `B`, `C`), tag-checked helpers | `AstKind`, `TokKind`, `AstNode` | 260 |
| `Lex/Lexer.cs` | Strict UTF-8 check, ASCII identifier/keyword classification, decimal/`0x` hex/`L` ints, char escapes, `"..."` & `"..."u8` literals, `>>>` | `Token` arena | 320 |
| `Parse/Parser.cs` | Recursive-descent declarations/statements + Pratt expression parser with nesting cap (`200`), fixed `namespace`/`using` profile gate | `TypeDecl`, `FieldDecl`, `FuncDecl`, `Vec<int> extra` | 820 |
| `Types/TypeTable.cs` | Hash-consed `TypeInfo` via `IntMap<int>`, generic `struct`/`class` substitution, `Copy`/`ZeroSafe`/`Linear`/`NeedsDrop`/`View` bitflags, SysV/LLVM layout & injective length-prefixed mangling | `TypeInfo`, `TypeFlags` | 520 |
| `Mir/MirArena.cs` | Flat per-function MIR arenas (`MirLocal`, `MirPlace`, `MirProj`, `MirInst`, `MirTerm`, `MirBlock`, `MirConst`), deterministic text dump for M5 differential testing | `MirOp`, `MirTermKind`, `MirFunc` | 280 |
| `Lower/DeclBinder.cs` | Global type table (`StrMap<int>`), member table (`IntMap<int>` keyed by `(typeDecl << 32) \| nameId`), 15-entry `Sys` catalog check, reachability worklist + polymorphic-recursion cycle check | `MemberSig`, `WorkItem` | 340 |
| `Lower/FuncLowerer.cs` | Single-pass typecheck + CFG/MIR builder: `byte`->`int` C# integer promotion, constant narrowing, L-to-R evaluation, two-phase receiver borrows, `using var` LIFO region exits, `IsErr`/`IsNone` guards, 32-entry Core intrinsic table | `FuncLowerState`, `ScopeRegion` | 1,050 |
| `Check/OwnershipChecker.cs` | Forward init/move/L1-linear dataflow (`VIB005`, `VIB006`, `VIB008`), backward local liveness, forward loan propagation + two-phase activation + return-view elision check (`VIB002`–`VIB004`, `VIB007`), drop-flag elaboration | `Loan`, `BitSet`, `DropElab` | 760 |
| `Llvm/LlvmEmitter.cs` | Prepends `vib_rt.ll` (+ `vib_std.ll`), emits types/pooled byte constants/drop glue/functions/`@__vib_entry` into a single `StringBuf` using strict first-use & BFS worklist ordering | `EmitCtx` | 720 |
| **Total** | **12 `.cs` files + `vib.manifest`** | **100% pure `Vib-B`, zero GC in native binary** | **~4,625 LOC** |

### 3.2 Exact 32-Entry Core Intrinsic Catalog for `Vib-B` (`FuncLowerer.cs`)

Keeping `Vib-B` tight means `FuncLowerer.cs` only needs these 32 built-in operations (plus the 15 `Vibranium.Std.Sys` ordinary calls):
- **`Prelude` / `VibraniumRuntime` / `Stdio` (10):** `Some(x)`, `None`, `Ok(x)`, `Ok()`, `Err(e)`, `Move(x)`, `Write(int|long|bool|string)`, `WriteLine(|int|long|bool|string)`, `VibraniumRuntime.Panic(string)`, `Stdio.WriteOut(u8)` / `Stdio.WriteErr(u8)`.
- **`Option<T>` & `Result<T, E>` & `Err<E>` (9):** `Option.IsSome`, `Option.IsNone(out v)` (guard), `Option.Unwrap()`, `Option.UnwrapOr(v)`, `Result.IsOk`, `Result.IsErr(out v, out e)` (guard), `Result.Unwrap()`, `Result.UnwrapOr(v)`, `Err.Error`.
- **`Vec<T>` (13):** `new Vec<T>()`, `new Vec<T>(int)`, `Count`, `Capacity`, `Push(T)`, `Pop()`, `this[int]`, `Slice(int, int)`, `ReadOnlySlice(int, int)`, `AsSpan()`, `AsReadOnlySpan()`, `Clear()`, `Truncate(int)`, `Reserve(int)`, `Extend(ReadOnlySpan<T>)`, `Resize(int, T)`, `GetEnumerator()` (with `Span<T>`: `Length`, `this[int]`, `Slice(int)`, `Slice(int, int)`, `Span->ReadOnlySpan`, `GetEnumerator()`).

### 3.3 Why `sha256(managed.ll) == sha256(stage1.ll) == sha256(stage2.ll)` Holds by Construction

1. **Identical source `S`:** `managed.ll` (`M(S)`), `stage1.ll` (`C1(S)`), and `stage2.ll` (`C2(S)`) are all outputs of the **exact same `vib-self` program** executing on `S` (first on `.NET 11` CLR, then as native ELF compiled by `C0`, then as native ELF compiled by `C1`).
2. **Zero host/hash/pointer leakage:**
   - All maps (`IntMap<V>`, `StrMap<V>`, `Interner`) use unseeded wrapping `long` SplitMix64 / FNV-1a64 and dense insertion-order arrays.
   - Type IDs, constant pool IDs (`@.b0..`), drop glue functions, and specialized function instances are emitted in deterministic AST/MIR traversal & BFS queue order starting from `Main`.
   - Local/temp/block SSA names (`%l0..`, `%t0..`, `bb0..`) reset to `0` per function.
   - Manifest-relative logical paths only; `\n` line endings only; zero timestamps, `argv[0]`, env vars, or pointers converted to integers.
   - `Vec<T>` growth math (`0 -> 4 -> *2`, `MAX = 0x7FFFFFC7`) is identical in `Vec.cs` and `vib_rt.ll`.

---

## 4. Unified Step-by-Step Implementation Checklist (Milestones M0–M7)

### Milestone M0 — Rebase onto Hardened v0.1 Baseline
- [ ] Rebase onto hardened v0.1 baseline in repository root and verify full `dotnet test` + `lake build` pass cleanly.
- [ ] Confirm exact `i8` (`MirType.UInt8`) storage in `Vec<byte>`, `Span<byte>`, struct fields, and `memcpy` byte counts, plus `>>>` (`MirBinaryOp.UShr`) and left-to-right evaluation tests.

### Milestone M1 — C0 Plumbing, Runtime Fragments, `"..."u8`, `Unit`, `Stdio`, `Vec` Extensions
- [ ] Check in `src/Vibranium.Core/Native/x86_64-linux-gnu/vib_rt.ll` (with `x86_64-pc-linux-gnu` triple, full `-i128:128` datalayout, `main` storing `argc`/`argv` + `signal(SIGPIPE, SIG_IGN)` + calling `@__vib_entry()`, console printers, `@__vib_write_all`, and `@__vib_vec_reserve/extend/resize` **with the `%n <= 0` null-pointer `llvm.memcpy` guard fixed**).
- [ ] Refactor `Llvm18Emitter.cs` to prepend `vib_rt.ll`, emit `@__vib_entry()`, support `MirFunctionSignature.NativeSymbol` without emitting duplicate `declare`, and align `LlvmEmitOptions.TargetTriple` to `"x86_64-pc-linux-gnu"`.
- [ ] Add `Unit`, `Prelude.Ok()`, `Stdio.WriteOut/WriteErr(ReadOnlySpan<byte>)`, and `Vec<T>.{Clear, Truncate, Reserve, Extend, Resize}` to `Vibranium.Core`, `MirIntrinsicKind`, `CfgMirLowerer`, `BorrowAndMoveAnalyzer`, and `Llvm18Emitter`.
- [ ] Add `"..."u8` (`IUtf8StringOperation`) lowering with static provenance and non-null `@.b_empty` backing for `""u8`.
- [ ] Allow `[UnscopedRef]` on user `struct` methods/properties in `SyntaxAndSymbolGatekeeper` and verify receiver-loan provenance in `BorrowAndMoveAnalyzer`.
- [ ] Add `vib.manifest` reader/generator to `vib` (`unit core|std|app`, `use`, `source`, `managed`, `native`).

### Milestone M2 — Narrow L1 Linear `Option<R>` / `Result<R, E>` + Formal Update
- [ ] Update `SyntaxAndSymbolGatekeeper` and `CfgMirLowerer` to allow linear `R` only in `Option<R>`, `Some<R>`, `Result<R, E>` (with `Copy` `E`!), `Ok<R>`, and `Prelude.{Some, Ok, Move}`.
- [ ] Close all 5 L1 soundness gaps from §2.1:
  - [ ] Gatekeep/obsolete `Some<T>.Deconstruct` & `Ok<T>.Deconstruct` so `in` receivers can never duplicate linear `R`.
  - [ ] Reject `Err<R>` and `Result<T, R>` (linear `E`) and mixed `Result<LinearR, AffineE>` with `VIB006`.
  - [ ] Reject `UnwrapOr`, `Take`, `Replace`, `AsRef`, `AsMut`, `UnwrapMut`, `PlaceScrutinee`, and nested `extraSubpattern` on linear `Option<R>`/`Result<R, E>` with `VIB006`.
  - [ ] Handle `IDiscardOperation` (`_ = expr;`) in `LowerSimpleAssignment` via `CreateTempLocal` so discarded linear results hit `VIB006`.
  - [ ] Keep manual `.Dispose()` banned (`VIB006`); all disposal goes through `using var` -> `MirStatement.DisposeResource`.
- [ ] Add positive & negative L1 conformance tests (`Result<File, IoError>` guard, `Unwrap`, early-return cleanup, ignored `Result<File, IoError>`, `out _` discard, `UnwrapOr`, linear field ban, double-dispose, borrow across scope exit) and extend `formal/VibraniumFormal` for L1.

### Milestone M3 — `src/Vibranium.Std`, `Sys` Catalog, `vib_std.ll`, and `samples/http-server/`
- [ ] Port and harden Fable's `proto/native/vib_std.ll` into `src/Vibranium.Std/Native/x86_64-linux-gnu/vib_std.ll`, **adding the zero-length `llvm.memcpy` guard in `@__vib_sys_arg_copy`/`@__vib_sys_file_open` and the arena bounds + trailing-NUL check in `@__vib_sys_spawn_wait`**.
- [ ] Enforce the 15-entry `Vibranium.Std.Sys` `[Native]` catalog in `SyntaxAndSymbolGatekeeper` (`VIB000` on any non-catalog `[Native]` or spoofed namespace) and `eng/bootstrap/trust.lock` SHA-256 verification in C0.
- [ ] Add `System.IO.DisableFileLocking=true` and `<Using Include="Vibranium.Std" />` (when referencing `Vibranium.Std`) to `Vibranium.App.props`.
- [ ] Land `src/Vibranium.Std/` T0 (`Sys.cs`, `Sys.Managed.cs`) + T1 (`Utf8`, `Bytes`, `StringBuf`, `IntMap<V>`, `StrMap<V>`, `Interner`, `IoError`, `PathText`, `File`, `Fs`, `ArgList`, `Env`, `Command`, `Process`, `Ipv4Address`, `Ipv4EndPoint`, `TcpListener`, `TcpStream`) with **zero map/text intrinsics in `Llvm18Emitter`** (verified by backend symbol-inspection unit test).
- [ ] Land `samples/http-server/` with bounded 16 KiB header parsing, `HTTP/1.1` `Host` validation, `Transfer-Encoding` / `Content-Length` rejection, `GET`/`HEAD` handling, buffer reuse across connections, and `Connection: close`.
- [ ] Run `tests/Vibranium.Std.Tests/` differential managed-vs-native parity suite (files, directories, symlinks/dotdot, `O_CLOEXEC`, `EINTR`/short-write fault injection, `Process.Run` exit & `128+signal`, loopback TCP + fragmented HTTP requests + 10,000-connection fd leak check + ASan).

### Milestone M4 — `--profile vib-b` Gate in C0 & Shared Conformance Corpus
- [ ] Add `--profile vib-b` to `src/vib` enforcing the exact `Vib-B` grammar/type/name rules (§1 & §3.2).
- [ ] Verify all of `src/Vibranium.Std` (T1) and `samples/http-server` pass `vib --profile vib-b` with zero warnings/errors.
- [ ] Freeze `tests/conformance/{accept,reject}/` covering ownership, moves, borrows, two-phase receivers, spans, `[UnscopedRef]`, `Vec<T>`, `IntMap`/`StrMap`, L1 linear `Result`, and `Vib-B` profile rejections.

### Milestone M5 — Implement `src/vib-self/` on .NET 11 (`M`)
- [ ] Implement the 12 `Vib-B` modules in `src/vib-self/` (~4,625 LOC per §3.1), verified continuously by `Vibranium.Analyzer` and `vib --profile vib-b`.
- [ ] Differential-test `M` (`dotnet run --project src/vib-self`) against `C0` across the entire `tests/conformance/{accept,reject}` corpus (matching accept/reject, `VIB0xx` diagnostic code, and native execution output when `M`'s `.ll` is linked with `clang-18`).

### Milestone M6 — Native Self-Hosting Bootstrap & Triple-Identity Gate
- [ ] Build `C1` (`vib-stage1`) from `src/vib-self/vib.manifest` via `C0` + `clang-18 -O2`.
- [ ] Run `M(S) -> managed.ll`, `C1(S) -> stage1.ll`, compile `stage1.ll -> vib-stage2` (`C2`), and run `C2(S) -> stage2.ll`.
- [ ] Verify:
  - `opt-18 -passes=verify` passes on `seed.ll`, `managed.ll`, `stage1.ll`, and `stage2.ll`.
  - **`sha256(managed.ll) == sha256(stage1.ll) == sha256(stage2.ll)`** (Gates G1 + G2).
  - `C2` passes the full `tests/conformance/{accept,reject}` and `Vibranium.Std` end-to-end test suites (Gate G4).

### Milestone M7 — Reproducibility Torture Tests & v0.2 Spec Update
- [ ] Run `eng/bootstrap/bootstrap.sh` across distinct working directories, varied `LC_ALL`/`TZ`/environment-variable orders, and repeated invocations, asserting identical SHA-256 digests on `managed.ll`, `stage1.ll`, and `stage2.ll` (Gate G3).
- [ ] Update `docs/SPEC.md` and `README.md` for v0.2 (`Vibranium.Std`, T0 catalog, L1 linear unions, `"..."u8`, `Vib-B`, honest parity domain, and zero-C native / zero-GC native claims).
