# Vibranium Roadmap & Future Architecture (`v0.3` – `v0.6+`)

This document captures the planned post-`v0.2` language, target, runtime, and full-stack ecosystem roadmap for **Vibranium (`vib`)**.

> A newer plan, [`ROADMAP-PROPOSAL.md`](ROADMAP-PROPOSAL.md), replaces these milestones from v0.3 on: foundations for a web backend and a browser frontend on Linux and `wasm32` in a stable 1.0, with macOS, Windows and the native-UI and game proofs in 1.1. All of its decisions are settled.

Every roadmap layer preserves Vibranium's foundational contract:
1. **100% Valid C# 15 Source** — full IDE autocomplete, refactoring, step-through `.NET 11` debugging, and `dotnet test`.
2. **Zero-GC Standalone Native & WASM Production Binaries** — deterministic affine/linear drop glue via LLVM 18, zero CLR runtime, zero reflection.
3. **`.NET` BCL Ergonomics & Zero Ceremony** — familiar `.NET` naming, plain `"..."` string literals, C# 15 syntactic sugar, and instant (~2 ms) `vib` feedback for both humans and AI vibe coding.

---

## `v0.3` — Language Ergonomics, Error Propagation & Zero-Allocation Iterators

### 1. Zero-GC String Interpolation (`$"..."`)
Using C#'s `ref struct` `[InterpolatedStringHandler]` lowering across both Stage-0 and `vib-self`:
- **Owned construction (`StringBuf` as its own buffering handler):**
  ```csharp
  StringBuf entry = $"[ ] {args[2]}\n";
  ```
- **Zero-allocation streaming console I/O:**
  ```csharp
  WriteLine($"{n++}. {item}"); // Streams segments directly to stdout with 0 heap allocations
  ```
- Supports `int`, `long`, `uint`, `ulong`, `bool`, `string`, `ReadOnlySpan<byte>`, and `in StringBuf` holes while keeping `Console.WriteLine($"...")` (`System.String` allocation) rejected (`VIB000`).

### 2. Existing-Local `out err` Reuse for `IsErr` / `IsNone` Out-Guards
Fresh-variable guard propagation (`if ([!]Call().IsErr(out var x, out var err)) return err;` and `if ([!]Call().IsNone(out var x)) ...` with fresh inline `out var` / `out T` declarations or `out _` discards) **already ships in `v0.2` (`docs/SPEC.md` §3.5)**:
```csharp
public bool IsErr(out T value, out Err<E> err); // shipped in v0.2 (SPEC.md §3.5)
public bool IsNone(out T value);                // shipped in v0.2 (SPEC.md §3.5)
```
In `v0.2`, every `out` target in `IsErr` / `IsNone` must be a fresh inline declaration (`out var x`) or discard (`out _`) (`VIB000` on existing locals or field targets). **`v0.3` extends out-guards to allow reusing an existing local `out err` (`out _, out err`)** with deterministic drop-before-overwrite when `!Copy` payloads are overwritten across sequential fallible steps:
```csharp
static Result<int, IoError> SyncTodos(string src, string dst)
{
    if (File.ReadAllText(src).IsErr(out var text, out var err)) return err;
    if (File.WriteAllText(dst, in text).IsErr(out _, out err)) return err;

    int count = 0;
    foreach (var _ in text.Lines()) count++;
    return Ok(count);
}
```

### 3. Value-Struct Zero-Allocation LINQ (`Vib.Iter`) & IDE Quick-Fixes
- **Zero-GC Iterator Adapters:** Value-type struct pipelines (`.Iter()`, `.Where(...)`, `.Select(...)`, `.Sum()`, `.Count()`) over `Span<T>`, `ReadOnlySpan<T>`, and `Vec<T>`—no `IEnumerable<T>` boxing—fused by LLVM 18 `-O3` into a single machine-code loop.
- **General `ref struct` Views & Fluent `ref` Chaining:** Field-level Polonius loan provenance for user-defined `ref struct` slices/splitters (`span.Split((byte)',')`) and fluent `ref StringBuf` chaining.
- **Roslyn `CodeFixProvider` (`Vibranium.Analyzer`):** One-keystroke IDE auto-fixes (`Ctrl+.`) to wrap `VIB006` linear resources in `using var`, insert `Move(...)` on explicit ownership transfers, and replace banned BCL calls with their `Vibranium.Std` equivalents.

---

## `v0.4` — Cross-Platform Targets (`Linux ARM64`, `macOS`, `Windows`, `WebAssembly`)

Vibranium's OS surface in `Vibranium.Std` goes through **17 `[Native]` leaf functions** in `Vibranium.Std.Sys` (`src/Vibranium.Std/Native/<target-triple>/vib_std.ll`). These bindings separate platform-specific work from the higher-level Vibranium libraries. Each new target still needs its ABI/layout support, runtime bindings, compiler configuration, and differential verification; LLVM support alone does not implement a Vibranium target. Only Linux `x86_64` is implemented in v0.2. Other rows below are planned targets, and their size figures are goals rather than measurements:

| Target Triple | Native Backing | Artifact Profile |
|---|---|---|
| `x86_64-pc-linux-gnu` | System `libc.so.6` | ~18–25 KB standalone ELF (shipped in `v0.2`) |
| `aarch64-unknown-linux-gnu` | System `libc.so.6` | Planned for `v0.4`; no native size measurement yet |
| `aarch64-apple-darwin` / `x86_64-apple-darwin` | macOS `libSystem.B.dylib` | Size goal: ~20–28 KB standalone Mach-O binary |
| `x86_64-pc-windows-msvc` / `aarch64-pc-windows-msvc` | Win32 (`kernel32`, `ws2_32`) + `ucrtbase.dll` | Size goal: ~22–30 KB standalone `.exe` |
| `wasm32-unknown-unknown` / `wasm32-wasip2` | LLVM 18 `wasm32` + lightweight bump/free-list allocator + JS host imports | Size goal: **~10–25 KB `.wasm`**; compression and framework comparisons unmeasured |

---

## `v0.5` — Zero-GC `async` / `await` (`.NET 11` Runtime Async + LLVM 18 Coroutines)

`.NET 11` introduced **Runtime Async** (`<Features>runtime-async=on</Features>`), where Roslyn stops synthesizing heap-heavy `IAsyncStateMachine` classes and instead emits flat IL methods marked `MethodImplOptions.Async` with explicit `AsyncHelpers.Await` suspension points.

This maps **1-to-1** onto **LLVM 18 Switched-Resume Coroutines** (`@llvm.coro.id`, `@llvm.coro.begin`, `@llvm.coro.suspend`, `@llvm.coro.end`):

1. **Standard `.NET` `Task<T>` / `ValueTask<T>` Signatures (Unified Flat-CFG Lowering):**
   - **On `.NET 11` (`runtime-async=on`):** Native runtime suspension with clean debugger call stacks and intact local variables.
   - **On `LLVM 18` (Native & `wasm32`):** Lowered directly from Vibranium's flat CFG MIR to LLVM coroutine intrinsics. LLVM 18's `CoroSplit` and **`CoroElide` (HALO — Heap Allocation eLision Optimization)** inline awaited child coroutine frames directly into parent frames with **zero heap allocation** on hot paths.
2. **Tiny Native Event Reactors:**
   - Linux: `io_uring` / `epoll`
   - macOS: `kqueue`
   - Windows: `IOCP`
   - Browser `wasm32`: zero-overhead bridge to native JavaScript `Promise`s (`fetch`, timers, WebSockets).
3. **Polonius Borrow & Linear Cancellation Safety Across `await`:**
   - Stack-bound `Span<T>` / `ReadOnlySpan<T>` borrows are statically rejected across `await` suspension points (matching Roslyn `CS4007`).
   - Linear `IDisposable` resources (`TcpStream`, `File`, `PgTx`) held across an `await` live inside the coroutine frame and run deterministic drop/cleanup glue automatically even if a task is cancelled while suspended.

---

## `v0.6+` — Full-Stack Vibranium Ecosystem (`Vib.Ui` + `Vib.Web` + `Vib.Sql`)

One memory-safe C# 15 language across browser, server, and database—zero TypeScript/C# DTO drift, zero Virtual DOM overhead, and zero GC pauses.

### 1. `Vib.Ui` — 15 KB Fine-Grained Reactive Browser Framework (`wasm32`)
Inspired by **SolidJS**, **Leptos (Rust)**, and **VanJS**, built on C# 15 collection expressions (`[...]`):
- **Fine-Grained `Signal<T>` & `SignalVec<T>` (No Virtual DOM):** Component functions execute once; signal writes patch individual DOM text/attribute nodes in $O(1)$.
- **Ownership-Driven DOM Teardown:** DOM elements and event listeners are owned handles—removing an item from `SignalVec<T>` deterministically unmounts its DOM subtree and frees its WASM listener closures with zero memory leaks.

```csharp
using Vib.Ui;
using static Vib.Ui.Html;

Mount("#app", TodoApp());

static Element TodoApp()
{
    var draft = Signal(new StringBuf());
    var items = SignalVec<StringBuf>(["Ship Vibranium v0.2", "Build Vib.Ui WASM"]);

    return Div(className: "card", [
        H1($"Vibranium Tasks ({items.Count})"),
        Form(onSubmit: () =>
        {
            if (draft.Read().Length == 0) return;
            items += draft.Take(); // Moves buffer into list & resets input with 0 copies
        }, [
            Input(placeholder: "Add a task...", bind: draft),
            Button("Add")
        ]),
        Ul(items.Each((in item, index) => Li([
            Span(item),
            Button("Done", onClick: () => items.RemoveAt(index))
        ])))
    ]);
}
```

### 2. `Vib.Web` — Zero-Reflection Minimal API Server
Inspired by **ASP.NET Core Minimal APIs** and **Rust's Axum**:
- **Automatic `RwLock` Concurrency from `in` vs `ref`:**
  - Handlers borrowing state via **`in TState`** automatically acquire a **shared reader lock** (parallel concurrent `GET`s).
  - Handlers borrowing state via **`ref TState`** automatically acquire an **exclusive writer lock** (compile-time data-race freedom with zero manual `lock` / `Arc<RwLock<T>>` ceremony).
- **Compile-Time `[Json]` Codecs & Zero-Copy Moves:** Source-generated UTF-8 span deserializers let handlers `Move(...)` parsed `StringBuf` fields directly into domain state without copying.

```csharp
using Vib.Web;
using static Vib.Web.Reply;

using var app = WebApp.Create(new TodoStore());

app.Get("/health", static () => Text("ok"));
app.Get("/todos", static (in TodoStore db) => Json(in db.Items));

app.Post("/todos", static (ref TodoStore db, CreateTodo body) =>
{
    if (body.Title.Trim().Length == 0)
        return BadRequest("Title cannot be empty");

    int id = db.NextId++;
    db.Items += new TodoItem(id, Move(body.Title), Done: false);
    return Created($"/todos/{id}", id);
});

return app.Run("127.0.0.1", 8080).IsOk ? 0 : 1;
```

### 3. `Vib.Sql` — Compile-Time Verified SQL (`sqlx`-Style) & Zero-GC Typed ORM (`Drizzle`/`Diesel`-Style)
1. **Compile-Time Checked Raw SQL (`$"""..."""`):**
   - `[InterpolatedStringHandler]` binds `{userId}` and `{title}` as binary wire parameters (`$1`, `$2`), making SQL injection impossible at compile time.
   - Roslyn analyzer validates column names/types against `schema.sql` and enforces `Option<T>` on nullable SQL columns.
   - **Linear Database Transactions (`VIB006`):** Unlike Rust's `sqlx` (where forgetting `.commit().await?` silently rolls back on `Drop`), `PgTx` / `SqliteTx` is linear—every code path must explicitly consume `tx` via `tx.Commit()` or `tx.Rollback()` at compile time!
   ```csharp
   static Result<int, SqlError> CreateTodoAtomic(in PgPool db, int userId, string title)
   {
       if (db.Begin().IsErr(out var tx, out var err)) return err;

       if (tx.QueryScalar<int>($"""
           INSERT INTO todos (user_id, title)
           VALUES ({userId}, {title})
           RETURNING id
           """).IsErr(out int newId, out err))
       {
           tx.Rollback();
           return err;
       }

       if (tx.Commit().IsErr(out _, out err)) return err;
       return Ok(newId);
   }
   ```
2. **Compile-Time Typed Query Builder ORM (Zero Reflection, Zero GC):**
   - `[Table("todos")]` generates zero-sized value-struct column descriptors (`Todo.UserId`, `Todo.Done`) that compile fluent chains (`db.Select(Todo.Table).Where(...).OrderByDesc(...).Limit(50)`) down to identical static parameterized SQL with full IDE autocomplete and rename-refactoring:
   ```csharp
   var openTodos = db.Select(Todo.Table)
       .Where(Todo.UserId.Eq(userId).And(Todo.Done.Eq(false)))
       .OrderByDesc(Todo.Id)
       .Limit(50)
       .FetchAll();
   ```
