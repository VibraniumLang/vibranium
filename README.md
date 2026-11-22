# Vibranium (`vib`)

> **Status: work in progress (pre-release).** This branch is the candidate for v0.2. It is not released.
> The compiler builds, passes its test suite and compiles itself, but known bugs remain, including rare
> programs that are accepted although they are unsafe or that give a wrong result. Do not rely on the
> safety guarantees described below until v0.2 is published. Linux x86_64 only.

**Write pure C# 15. Debug on `.NET 11`. Ship zero-GC, Rust-safe `LLVM 18` native binaries.**

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Language: C# 15 / .NET 11](https://img.shields.io/badge/C%23%2015-.NET%2011.0-512BD4.svg)](docs/SPEC.md)
[![Backend: LLVM 18 -O3](https://img.shields.io/badge/backend-LLVM%2018%20--O3-262D3A.svg)](#one-source-file-two-execution-worlds)
[![Self-Hosted: 48.6k LOC](https://img.shields.io/badge/self--hosted-48.6k%20LOC-success.svg)](#100-self-hosted-in-pure-vibranium-486k-loc)
[![Formal Proofs: Lean 4 (0 sorry)](https://img.shields.io/badge/Lean%204-0%20sorry-blueviolet.svg)](#mechanized-lean-4-soundness--equivalence-proofs)

| **`2.21 ms`** | **`18.6 KB`** | **`1.4 MB`** | **`0`** |
| :---: | :---: | :---: | :---: |
| `samples/demo` check (`vib-stage2`) | stripped `-O3` ELF (`samples/benchmark`) | peak RSS (`samples/benchmark`) | native GC/CLR · `null` · `sorry` |

*Native compute on par with Rust (`154.3 ms` vs. `152.7 ms` median) · `0.81 ms` `samples/demo` startup + run (vs. `34.44 ms` on `.NET 11`) · `48.6k`-LOC self-hosted compiler · `19/19` Lean 4 Core-MIR proof jobs, `0 sorry`.*

**Vibranium** is a strict, null-free, ownership- and borrow-checked systems language embedded directly inside **100% valid base C# 15 (`.NET 11`)**. Edit, refactor, step-debug, and run `dotnet test` in Rider, Visual Studio, or VS Code with live red-squiggle borrow diagnostics — then compile the **exact same `.cs` source**, unmodified, into a standalone zero-GC native binary (`18.6–22.8 KB` stripped ELF across the repository samples) with Rust-class compute speed and Lean 4 memory-safety and CLR↔LLVM equivalence proofs mechanized for the Core MIR model.

---

## A Taste of Vibranium (`v0.2`)

This example is standard top-level C# 15 — plain `"..."` UTF-8 string ergonomics (no `"..."u8` suffix needed), familiar `.NET` BCL names, zero `null`, zero exceptions, Polonius Non-Lexical Lifetimes (NLL) borrowing, and deterministic scope-exit cleanup without a garbage collector:

```csharp
// Standard top-level C# 15 — debug with `dotnet run`, ship native with `vib build -O3`.
if (Summarize("tasks.log", "summary.txt").IsErr(out var banner, out _)) return 1;
WriteLine(in banner);
return 0;

static Result<StringBuf, IoError> Summarize(string inputPath, string outputPath)
{
    if (File.ReadAllText(inputPath).IsErr(out var text, out var err)) return err;
    var counts = new StrMap<int>(8);
    int urgent = 0;
    foreach (var raw in text.Lines())
    {
        var line = raw.Trim();
        if (line.StartsWith("DONE:")) Bump(ref counts, "done");
        else if (line.StartsWith("TODO:"))
        {
            Bump(ref counts, "todo");
            if (line.EndsWith("!")) urgent++;
        }
    }

    int open = counts.Get("todo") switch { Some<int>(var n) => n, NoneType => 0 };
    var report = new StringBuf(48);
    report += "open=";
    report.Append(open);
    report += " urgent=";
    report.Append(urgent);

    if (File.Create(outputPath).IsErr(out var file, out var createErr)) return createErr;
    using var outLog = file;    // VIB006: linear handle closed exact-once at scope exit
    if (outLog.WriteAll(report[..]).IsErr(out _, out var writeErr)) return writeErr;
    return Ok(Move(report));    // explicit Move(...) transfers StringBuf ownership to caller
}

static void Bump(ref StrMap<int> m, string k) => _ = m.Set(k, m.Get(k).UnwrapOr(0) + 1);
```

---

## One Source File. Two Execution Worlds.

Vibranium eliminates the trade-off between high-velocity managed tooling and standalone native deployment. The exact same verified `.cs` file runs on `.NET 11` during development and compiles directly to native machine code for production:

```text
                   Program.cs · 100% valid C# 15
                                 │
            ┌────────────────────┴────────────────────┐
            ▼                                         ▼
  DEVELOP · dotnet build/run/test         SHIP · vib build -O3
  ───────────────────────────────         ──────────────────────────────────
  Roslyn 5.12 + Vibranium.Analyzer        vib (Stage-0, Roslyn frontend) or
    live VIB000–VIB008 squiggles          vib-stage2 (native, self-hosted)
  .NET 11 CLR + GC                          MIR → NLL borrow + linear check
    breakpoints · step-through debug        2.21 ms on samples/demo (stage2)
    dotnet test / xUnit                   LLVM 18 IR → clang-18 -O3
                                            18.6–22.8 KB ELF · libc.so.6 only
                                            0 GC · iterative drop · noalias
            │                                         │
            └─── identical output + Dispose order ────┘
        (differential tests · Lean 4 Core-MIR bisimulation)
```

| Dimension | Development Mode (`dotnet`) | Production Native (`vib build -O3` / `vib-stage2`) |
| :--- | :--- | :--- |
| **Compiler & Tooling** | Roslyn `5.12.0` + live `Vibranium.Analyzer` in VS / Rider / VS Code | `vib` (Stage-0, Roslyn) or `vib-stage2` (native, self-hosted, zero Roslyn) + `clang-18 -O3` |
| **Verification & Debug** | `.NET 11` step-through debugger, watch windows, `dotnet test` | `--asan` builds (`clang-18 -fsanitize=address,leak`), `emit-ll` IR inspection; step-debug on `.NET 11` |
| **Memory Reclamation** | `.NET 11` Garbage Collector + deterministic `using var` `Dispose()` | Deterministic scope-exit `free` (bounded-stack iterative worklist drop) |
| **Runtime Dependencies** | `.NET 11` CoreCLR (`~75 MB` runtime) | **System `libc.so.6` only** (zero CLR, zero GC, zero `stdc++`, zero `.c` shims) |
| **Sample Footprint** | `7–12 KB` IL `.dll` + host runtime (`41–81 MB` RSS) | **`18.6–22.8 KB` stripped standalone ELF** (`1.4 MB` peak RSS) |

---

## Built for Humans and AI Vibe Coding Alike

LLMs frequently trip over Rust's lifetime sigils (`'a`), borrow-checker edge cases, and unfamiliar crate APIs — yet in standard C#, Python, or Go, hallucinated `null`s, aliased mutations, and leaked handles slip straight into production. Vibranium bridges both worlds:

- **Zero-Ceremony `.NET` Muscle Memory:** Humans and coding agents already know `Vec<T>.Add`, `File.ReadAllText`, `WriteLine`, `StartsWith`, `Trim`, `int.TryParse`, `Span<T>`, `using var`, and `foreach`. Plain `"..."` string literals work everywhere with no `"..."u8` suffix needed and no `'a` lifetime annotations.
- **Millisecond Deterministic Feedback Loops:** Self-hosted native `vib-stage2` runs full type, Polonius NLL borrow, move, and resource-linearity verification in **`2.21 ms`** on `samples/demo` and **`2.53 ms`** vs. `rustc --emit=metadata`'s **`37.08 ms`** on the same benchmark program (`samples/benchmark`, **`14.7×` faster**). Agents can generate → check → fix in milliseconds while human reviewers see the exact same squiggles in their IDE.
- **The Bugs That Actually Ship Become Compile Errors (`VIB000`–`VIB008`):** Null dereferences (`VIB008`), use-after-move (`VIB001`), overlapping mutable borrows (`VIB002`), buffer/cursor invalidation (`VIB003`), mutation through shared views (`VIB004`), escaping stack borrows (`VIB007`), and leaked file/socket handles (`VIB006`) are rejected at compile time.
- **Fails Closed (`VIB000`):** Anything outside the verified deterministic subset (exceptions, inheritance, reflection, hidden boxing, closures, `unsafe`) is rejected immediately with `VIB000` — generated code cannot silently wander into unchecked territory.

---

## Key Language Pillars

### 1. Null-Free by Construction (`VIB008`)
`null` literals, nullable annotations (`T?`), null-forgiving `x!`, `??`, and `?.` are banned at compile time, and `default` is restricted to provably zero-safe types. Absence and recoverable errors use zero-boxing C# 15 `[Union]` structs — **`Option<T>`** (`Some(x)` / `None`) and **`Result<T, E>`** (`Ok(x)` / `Err(e)`) — with 1-line out-guards and compiler-enforced exhaustive `switch` expressions:

```csharp
static Result<int, IoError> ReadPortOrDefault(string path)
{
    if (!File.Exists(path)) return Ok(8080);
    if (File.ReadAllText(path).IsErr(out var raw, out var err)) return err;

    Option<int> parsed = Bytes.ParseInt(raw.AsUtf8().Trim());
    return parsed switch
    {
        Some<int>(var port) when port > 0 && port <= 65535 => Ok(port),
        _ => Err(new IoError(IoErrorKind.InvalidInput, IoOp.Read)),
    };
}
```

### 2. Affine Ownership & Polonius NLL Borrowing (`VIB001`–`VIB005`, `VIB007`)
Primitives and all-`Copy` structs copy implicitly; heap classes (`class Node`) and inline buffer owners (`Vec<T>`, `StringBuf`, `IntMap<V>`, `StrMap<V>`) move on assignment with deterministic `O(1)`-stack iterative drop glue on scope exit. Borrows enforce **Aliasing XOR Mutability**:
- **Shared borrows** (`in T`, `ReadOnlySpan<T>`, `OptionRef<T>`, unmarked `class` methods, `readonly struct` methods) alias freely and enforce deep transitive immutability (`VIB004`).
- **Exclusive borrows** (`ref T`, `out T`, `!Copy` `Span<T>` with implicit reborrowing, `OptionMut<T>`, `[Mut]` class methods) guarantee unique access and unlock LLVM 18 `noalias` SIMD auto-vectorization.

#### When Code Is Buggy, the Compiler Says No

`Vibranium.Analyzer` (live in your IDE) and Stage-0 `vib check` print identical diagnostics; the self-hosted native `vib-stage2` (~2 ms) reports the same codes on the same lines:

```csharp
Vec<int> scores = [90, 85];
ref var top = ref scores[0];
scores.Add(70);                  // could reallocate and invalidate `top`
top = 100;

var a = new Node(1);
var b = a;                       // ownership moves to `b`
WriteLine(a.Value);

var log = File.Create("run.log").Unwrap();   // forgot `using var`
_ = log.WriteAll("hi\n");
return 0;

class Node(int v) { public int Value = v; }
```

```text
$ vib check Program.cs
Program.cs(3,1): error VIB003: Cannot borrow 'scores' immutably while borrowed by 'top'.
Program.cs(8,11): error VIB001: Use of moved value 'a'.
Program.cs(12,8): error VIB006: Linear IDisposable resource 'log' must be bound with 'using var' or moved/disposed on all paths.
```

### 3. Exact-Once Linear `IDisposable` Handles (`VIB006`)
Unlike garbage-collected C# (where omitting `using` leaks OS descriptors until finalization) or Rust (where `std::mem::forget` is safe), Vibranium treats every `class R : IDisposable` (`File`, `TcpListener`, `TcpStream`) — plus L1 fallible wrappers `Option<R>` and `Result<R, E>` — as **statically linear**:

```csharp
if (TcpListener.Bind("127.0.0.1", 8080).IsErr(out var bound, out _)) return 1;
using var listener = bound; // disposed deterministically in LIFO order on scope exit

if (listener.Accept().IsErr(out var conn, out _)) return 1;
using var stream = conn;    // forget `using var` -> VIB006 · move `stream` away -> VIB006
_ = stream.SendAll("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");
return 0;
```

### 4. Rust ↔ Vibranium Rosetta Stone

| Concept | Rust | Vibranium (100% Valid C# 15) |
| :--- | :--- | :--- |
| Move ownership | `let b = a;` | `var b = a;` (or explicit `Move(a)`) |
| Owned heap node | `Box::new(Node { val: 1, next: None })` | `new Node(1, None)` |
| Growable vector & slice | `let mut v = vec![1, 2, 3]; &mut v[1..3]` | `Vec<int> v = [1, 2, 3]; Span<int> s = v[1..3];` |
| Shared vs. mutable borrow | `fn f(x: &T, y: &mut T)` | `static void F(in T x, ref T y)` |
| In-place slice mutation | `for x in &mut v { *x += 1; }` | `foreach (ref var x in v) x += 1;` |
| Method receiver mutability | `&self` vs. `&mut self` | `class`: unmarked vs. `[Mut]` · `struct`: `readonly` vs. unmarked |
| Exhaustive `Option<T>` match | `match opt { Some(x) => x, None => 0 }` | `opt switch { Some<int>(var x) => x, NoneType => 0 }` |
| Non-allocating list cursor | `let mut cur = &mut head;` | `for (var cur = head.AsMut(); cur is Some<Node>(var n); cur = n.Next.AsMut())` |
| Fallible error propagation | `let text = fs::read_to_string(p)?;` | `if (File.ReadAllText(p).IsErr(out var text, out var err)) return err;` |
| Deterministic OS handle cleanup | `Drop` (affine; `mem::forget` allowed) | `using var f = File.Create(p).Unwrap();` (**exact-once linear `VIB006`**) |

---

## Measured 4-Way Benchmarks

The timings below are historical measurements from development snapshots on the stated machine and toolchains, not measurements of every subsequent compiler revision. In particular, the self-compile timings do not describe the current source-line count or bootstrap hash. Rerun the benchmark scripts to measure the current tree on your machine.

Vibranium native binaries achieve **Rust-class compute speed and competitive network throughput** at **~16–18× smaller stripped binary sizes** than Rust and **~30–55× lower peak memory** than `.NET 11`. Measured on Linux `x86_64` (WSL2), `.NET 11.0.100`, `LLVM 18 -O3`, and `Rust 1.99 -O3` with byte-for-byte output verification.

### Cold-Start + Run Latency (`samples/demo/Program.cs`, median of 10 process launches after warmup)

| Target | Median Startup + Run | Relative |
| :--- | ---: | ---: |
| `.NET 11` (`Demo` apphost — CLR startup + JIT + run) | `34.44 ms` | `42.5×` slower |
| **`Vibranium Native` (`vib build -O3`, `libc.so.6` only)** | **`0.81 ms`** | **`1.0×` (baseline)** |

### 1. Compute, Ownership Drop & SIMD Suite (`samples/benchmark/`)
Whole-process wall time across 7 interleaved runs (`./samples/benchmark/run-benchmark.sh`): **2,000,000** heap-allocated linked-list `Node`s with iterative drop + **400 × 4,096-int** iterative Quicksort + **250,000 × 2,048-float** `Saxpy` SIMD passes:

| Target | Artifact Size | Whole-Process Wall Time (Best / Median) | Peak RSS |
| :--- | ---: | ---: | ---: |
| `.NET 11 (Vibranium Source)` | `7,168 B` DLL (+ ~75 MB CLR) | `299.2 ms` / `391.6 ms` | `76,608 KB` (`74.8 MB`) |
| `.NET 11 (Hand-Tuned C# 15)` | `6,656 B` DLL (+ ~75 MB CLR) | `289.8 ms` / `366.6 ms` | `81,532 KB` (`79.6 MB`) |
| `Rust 1.99 (cargo --release -O3)` | `341,152 B` stripped ELF | `127.8 ms` / `152.7 ms` | `2,344 KB` (`2.3 MB`) |
| **`Vibranium Native (vib -O3)`** | **`18,640 B` stripped ELF** | **`124.6 ms` / `154.3 ms`** | **`1,452 KB` (`1.4 MB`)** |

### 2. Zero-GC HTTP/1.1 Server — Pre-Allocated Buffers & Linear Sockets (`samples/http-server/`)
Single-threaded HTTP/1.1 server built on `Vibranium.Std` (`TcpListener`, `TcpStream`, `StringBuf`, `Bytes`, `Env.Args`, L1 linear `using var`) reusing pre-allocated request/body/response buffers across connections. Load-tested with **8 client workers** (`800` warmup + `8,000` measured requests; cold start = median of `15` spawn → first-`200 OK` runs) and verified across 7 wire-parity scenarios + `clang-18 -fsanitize=address,leak` (`./samples/http-server/run-http-benchmark.sh`):

| Target | Artifact Size | First `200 OK` | Throughput (8 workers) | p50 / p99 Latency | Peak RSS (`VmHWM`) |
| :--- | ---: | ---: | ---: | ---: | ---: |
| `.NET 11 (Vibranium Source)` | `12,288 B` DLL (+ ~75 MB CLR) | `41.8 ms` | `8,245 req/s` | `934.7 us` / `1,733.2 us` | `41,444 KiB` |
| `.NET 11 (C# 15 Socket)` | `12,288 B` DLL (+ ~75 MB CLR) | `38.1 ms` | `8,866 req/s` | `879.7 us` / `1,436.3 us` | `43,708 KiB` |
| `Rust 1.99 (std::net -O3)` | `365,760 B` stripped ELF | `20.9 ms` | `8,968 req/s` | `867.7 us` / `1,419.8 us` | `2,108 KiB` |
| **`Vibranium Native (vib -O3)`** | **`22,824 B` stripped ELF** | **`20.9 ms`** | **`8,412 req/s`** | **`907.6 us` / `1,683.6 us`** | **`1,360 KiB`** |

---

## 100% Self-Hosted in Pure Vibranium (`48.6k` LOC)

Vibranium compiles itself with zero Roslyn and zero authored C. `src/vib-self/` (`48.6k` LOC — `48,579` lines of pure, zero-GC Vibranium built solely on `Vibranium` core types and `Vibranium.Std`) implements the lexer, recursive-descent parser for Vibranium's C# 15 subset, generic monomorphizer, CFG MIR lowerer, Polonius NLL borrow & L1 linear `IDisposable` solver (`VIB000`–`VIB008`), and textual LLVM 18 IR emitter.

Running `./eng/bootstrap/bootstrap.sh` verifies a **bit-for-bit Triple-Identity Fixed Point** plus out-of-tree path-independence reproducibility:
- `managed.ll` — emitted by `.NET 11` `vib-self.dll`
- `stage1.ll` — emitted by standalone native `vib-stage1` (compiled via Stage-0 `vib`)
- `stage2.ll` — emitted by self-compiled standalone native `vib-stage2`
- `repro.ll` — emitted by `vib-stage2` from an out-of-tree copy in a distinct temporary path

`managed.ll`, `stage1.ll`, and `stage2.ll` pass `opt-18 -passes=verify` and all four LLVM IR artifacts generated from that compiler source tree match bit-for-bit (`SHA256 = c6fe6cc933d144f219c72e87599c788c136e36d8d99e784b5d7e5bd205911ef7`), while `readelf` confirms that the standalone native compiler binaries `vib-stage1` and `vib-stage2`, the native demo, and dynamic floating-point remainder programs built by every compiler at `-O0` and `-O3` declare **exactly one dependency, system `libc.so.6`** (no `libm`, `coreclr`, `hostfxr`, `stdc++`, or `gcc_s`).

### Headline Compile-Speed Comparison (median of 5 runs)

| Workload | Toolchain | Median Time |
| :--- | :--- | ---: |
| **`samples/demo` type + Polonius NLL borrow check** | **`vib-stage2` (standalone native)** | **`2.21 ms`** |
| `samples/demo` type + Polonius NLL borrow check | Stage-0 `.NET 11` + Roslyn `vib` CLI | `1,517.58 ms` |
| **`samples/benchmark` type + NLL borrow check** | **`vib-stage2` (standalone native)** | **`2.53 ms`** |
| `samples/benchmark` check (`--emit=metadata`) | `rustc 1.99 --edition=2024` | `37.08 ms` |
| **`samples/benchmark` full `-O3` native binary build** | **`vib-stage2` + `clang-18 -O3`** | **`185.11 ms`** |
| `samples/benchmark` full `-O3` native binary build | `rustc 1.99 -O` | `258.43 ms` |

<details>
<summary><strong>Full 13-Row Bootstrap & Self-Compile Benchmark Table (<code>./eng/bootstrap/bootstrap.sh</code>)</strong></summary>

<br>

| Compiler Workload | Stage / Toolchain | Median Time |
| :--- | :--- | ---: |
| **`samples/demo` type + NLL borrow check** | **`vib-stage2` (standalone native)** | **`2.21 ms`** |
| `samples/demo` type + NLL borrow check | Stage-0 `.NET 11` + Roslyn `vib` | `1,517.58 ms` |
| **`samples/demo` LLVM 18 IR emission (`emit-ll`)** | **`vib-stage2` (standalone native)** | **`2.93 ms`** |
| **`samples/demo` full `-O3` native binary build** | **`vib-stage2` + `clang-18 -O3`** | **`171.47 ms`** |
| `samples/demo` full `-O3` native binary build | Stage-0 `vib` + `clang-18 -O3` | `1,692.31 ms` |
| **`samples/benchmark` check** | **`vib-stage2` (standalone native)** | **`2.53 ms`** |
| `samples/benchmark` check (`--emit=metadata`) | `rustc 1.99 --edition=2024` | `37.08 ms` |
| **`samples/benchmark` full `-O3` native build** | **`vib-stage2` + `clang-18 -O3`** | **`185.11 ms`** |
| `samples/benchmark` full `-O3` native build | `rustc 1.99 -O` | `258.43 ms` |
| **`samples/http-server` + `Vibranium.Std` check** | **`vib-stage2` (standalone native)** | **`4.39 ms`** |
| **`src/vib-self` (historical snapshot) full check** | **`vib-stage2` (standalone native)** | **`18,442.14 ms`** |
| **`src/vib-self` (historical snapshot) full `emit-ll`** | **`vib-stage2` (standalone native)** | **`18,988.54 ms`** |
| `src/vib-self` (historical snapshot) full `emit-ll` | `.NET 11` managed `vib-self.dll` | `50,435.79 ms` |

</details>

---

## Mechanized Lean 4 Soundness & Equivalence Proofs

Why trust that running under `.NET 11` GC during development and deterministic `free` on `LLVM 18` in production will behave identically? Alongside **2,447 automated xUnit tests** (covering `.NET 11` ↔ `LLVM 18 -O3` + AddressSanitizer/LeakSanitizer differential execution, `Vibranium.Std`, `vib-self` self-hosting, and `Vibranium.Analyzer` diagnostic parity) and a differential conformance fuzzer that compares Roslyn, the Analyzer and both compilers on generated programs ([`docs/CONFORMANCE.md`](docs/CONFORMANCE.md)), `formal/` (`VibraniumFormal`, Lean 4 `v4.35.0`, **`19/19` build jobs**, **zero `sorry`**, **zero custom axioms**) mechanizes the idealized **Core Vibranium MIR calculus**, its flow-sensitive NLL borrow & linearity checker, and its dual operational semantics:

| Machine-Checked Theorem (`formal/`) | What It Mathematically Proves Over Core MIR |
| :--- | :--- |
| **`aliasing_xor_mutability` & `shared_borrow_immutability`** | Every reachable checked state upholds strict Aliasing XOR Mutability across heap cells and active shared/mutable borrows — justifying LLVM `noalias` emission. |
| **`native_memory_safety`** | Well-checked Core MIR programs under native deterministic `free` never hit use-after-free, double-free, or double-dispose, and close every allocated linear `IDisposable` resource **exact-once**. |
| **`clr_llvm_equivalence`** | Lockstep bisimulation proving managed GC execution (`evalManaged`) and native deterministic-`free` execution (`evalNative`) produce the **exact same return value and observable I/O + `Dispose` trace**. |

*(Scope note: `VibraniumFormal` proves metatheory over the mechanized Core Vibranium MIR calculus; the production Roslyn/self-hosted frontends and LLVM 18 emitter are empirically validated via `.NET 11` ↔ `LLVM 18` + `opt-18` + ASan/LSan differential tests and Triple-Identity bootstrap.)*

The [invalid-program mutation oracle](docs/MUTATION.md) separately tests how `vib-self` handles programs Roslyn rejects.

---

## Quick Start

### Prerequisites (Linux `x86_64` / WSL2)
- **`.NET 11.0.100` SDK** (pinned in `global.json`)
- **LLVM 18 / Clang 18** (`clang-18`, `opt-18`, and `libclang-rt-18-dev` for ASan/LSan verification)
- **Lean 4 (`v4.35.0`)** via `elan` *(optional — only needed to re-verify `formal/`)*

### Build, Check & Run Across Both Worlds

```bash
# 1. Build the compiler, IDE analyzer, standard library, CLI, samples, and tests (0 warnings, 0 errors)
dotnet build --disable-build-servers Vibranium.slnx

# 2. Run all 2,447 automated tests (.NET 11 <-> LLVM 18 + ASan/LSan, Std, vib-self & Analyzer parity)
dotnet test --disable-build-servers Vibranium.slnx --no-build

# 3. Type-check & borrow-check a Vibranium program via the Stage-0 CLI
dotnet run --project src/vib/vib.csproj --no-build -- check samples/demo/Program.cs

# 4. Compile to a standalone zero-GC LLVM 18 native binary (-O3) and compare with .NET 11
dotnet run --project src/vib/vib.csproj --no-build -- build samples/demo/Program.cs -O3 -o ./demo-native
./demo-native
dotnet run --project samples/demo/Demo.csproj --no-build   # same source on .NET 11 — identical output

# 5. Inspect verified textual LLVM 18 IR directly
dotnet run --project src/vib/vib.csproj --no-build -- emit-ll samples/demo/Program.cs -o ./demo.ll

# 6. Build vib-stage1 & vib-stage2 and verify the 48.6k-LOC Triple-Identity fixed point
./eng/bootstrap/bootstrap.sh

# 7. (Optional) Re-check all mechanized Lean 4 proofs from scratch (~8s, 19 jobs, 0 sorry)
cd formal && lake build && lake exe vibranium-formal-verify
```

CLI synopsis: `vib <check|emit-ll|build|run> <path...> [-o output] [-O0..-O3] [--cc clang] [--sysroot dir] [--asan] [--shared-noalias] [--no-std] [-- args...]`

---

## Diagnostic Reference (`VIB000`–`VIB008`)

`Vibranium.Analyzer` (live in your IDE), Stage-0 `vib`, and native `vib-stage2` enforce the exact same 9 diagnostic codes:

| Code | Domain | Compile-Time Guarantee |
| :--- | :--- | :--- |
| **`VIB000`** | Subset Gatekeeper | Rejects C# features outside the deterministic systems subset (inheritance, exceptions, closures, `async` in `v0.2`, reflection, boxing, raw `T[]`, `unsafe`, unwhitelisted BCL/`[Native]`) |
| **`VIB001`** | Move & Init | Rejects use of moved, disposed, or potentially uninitialized values across all control-flow paths (including inactive `IsErr`/`IsNone` branch bindings) |
| **`VIB002`** | Call Aliasing | Enforces **Aliasing XOR Mutability** across overlapping call arguments (`ref`, `out`, `[Mut]` receiver, non-`readonly` struct receiver, `Span<T>`, `OptionMut<T>`) |
| **`VIB003`** | Active Borrows | Rejects mutation, move, resize/clear, conflicting borrow, or scope exit of an owner while an NLL borrow (`ref`, `Span<T>`, cursor reborrow, `foreach` loan, or two-phase reservation) is live |
| **`VIB004`** | Shared Immutability | Rejects field, element, or `[Mut]` / non-`readonly` receiver mutation through a shared borrow (`in T`, `ReadOnlySpan<T>`, `OptionRef<T>`, shared pattern bindings) at any depth |
| **`VIB005`** | Place Integrity | Rejects destructive moves out of fields, property getters, indexed elements, or borrowed places (use `.Take()` on mutable `Option<T>` slots) |
| **`VIB006`** | Linear Resources | Enforces **exact-once disposal** of `class : IDisposable` handles (`File`, `TcpListener`, `TcpStream`) and L1 fallible `Option<R>` / `Result<R, E>` wrappers |
| **`VIB007`** | Escape Analysis | Rejects returned or stored references/views (`ref`, `Span<T>`, `OptionRef<T>`, `OptionMut<T>`) escaping stack locals, temporaries, or by-value owners |
| **`VIB008`** | Null & Zero Safety | Bans `null`, `T?`, `x!`, `??`, `?.`, zeroed `default` on non-zero-safe types (`class`, `string`, `Vec<T>`, `Result<T, E>`), and uninitialized non-zero-safe fields |

---

## Current `v0.2` Scope & Roadmap (`v0.3` – `v0.6+`)

**What ships in `v0.2` today:** Ownership, Polonius NLL borrow, and L1 linear `IDisposable` verification; dual `.NET 11` + `LLVM 18` **Linux `x86_64`** native compilation; live Roslyn IDE analyzer; zero-C `Vibranium.Std` (UTF-8, `StringBuf`, `IntMap`/`StrMap`, `File`/`Fs`, `Env`, `Process`, `TcpListener`/`TcpStream`); 1-line `IsErr(out var ok, out var err)` / `IsNone(out var val)` out-guards; and the `48.6k`-LOC self-hosted native compiler. `v0.2` intentionally excludes OOP inheritance, exceptions, closures, string interpolation (`$"..."`), multi-threading, and `async`/`await` (`VIB000`).

See [`docs/ROADMAP.md`](docs/ROADMAP.md) and [`docs/SPEC.md` §7](docs/SPEC.md) for planned post-`v0.2` releases *(syntax and size targets below reflect planned `v0.3+` design goals)*:

| Release | Planned Milestones |
| :--- | :--- |
| **`v0.3` — Ergonomics & Iterators** | `$"..."` zero-GC `[InterpolatedStringHandler]` · `out err` guard reuse · `ref StringBuf` chaining · user `ref struct` views (`Split`) · `Vib.Iter` value-struct pipelines · buffered `StdinReader` & DNS · IDE quick-fixes |
| **`v0.4` — Cross-Platform & `wasm32`** | Porting the 17 `Vibranium.Std.Sys` leaves to Linux `aarch64` · macOS (`libSystem` Mach-O) · Windows (Win32 + `ucrtbase` `.exe`) · `wasm32` (target: `~10–25 KB` `.wasm`) |
| **`v0.5` — Zero-GC `async` / `await`** | `.NET 11` Runtime Async ↔ LLVM 18 Switched-Resume Coroutines (`CoroElide` HALO frame elision) · `io_uring` / `kqueue` / `IOCP` / JS `Promise` reactors · linear cancellation safety |
| **`v0.6+` — Full-Stack Ecosystem** | `Vib.Ui` (WASM `Signal<T>` UI, target: `~15 KB`, zero Virtual DOM) · `Vib.Web` (`in`/`ref` state → reader/writer locks) · `Vib.Sql` (compile-time `$"""SELECT ..."""`, zero-GC ORM & linear `VIB006` transactions) |

---

## Repository Layout

```text
Vibranium.slnx (.NET 11.0.100 / C# 15 + LLVM 18 — 15 Projects, Zero Authored C Files)
├── src/
│   ├── Vibranium.Core/           # Core types ([Union] Option/Result, OptionRef/Mut, Vec<T>, Prelude, Io, Stdio) + vib_rt.ll
│   ├── Vibranium.Std/            # Zero-C standard library (Utf8, Bytes, StringBuf, ConsoleIo, IntMap, StrMap, File, Env,
│   │                             # Process, TcpListener, TcpStream) + 17-leaf Vibranium.Std.Sys & vib_std.ll
│   ├── Vibranium.Compiler.Core/  # Roslyn-free CFG MIR & canonical diagnostic model (VIB000–VIB008)
│   ├── Vibranium.Analysis/       # Polonius-style NLL borrow, move, initialization, two-phase & L1 linear solver
│   ├── Vibranium.Frontend.Roslyn/# Roslyn 5.12.0 syntax/symbol gatekeeper & ControlFlowGraph → MIR lowerer
│   ├── Vibranium.Analyzer/       # Live IDE/MSBuild Roslyn DiagnosticAnalyzer (VIB000–VIB008)
│   ├── Vibranium.Backend.Llvm/   # Pure textual LLVM 18 emitter (O(1)-stack iterative drop glue + noalias SIMD)
│   ├── vib/                      # Stage-0 Roslyn CLI driver (vib check | emit-ll | build | run)
│   └── vib-self/                 # 48.6k-LOC self-hosted pure-Vibranium native compiler (vib-stage1 / vib-stage2)
├── eng/conformance/              # Differential conformance fuzzer (docs/CONFORMANCE.md)
├── eng/mutation/                 # Invalid-program mutation oracle (docs/MUTATION.md)
├── eng/bootstrap/bootstrap.sh    # Triple-Identity fixed-point verifier (managed.ll == stage1.ll == stage2.ll == repro.ll)
├── formal/                       # Mechanized Lean 4 (v4.35.0) soundness & CLR↔LLVM bisimulation proofs (0 sorry)
├── samples/                      # Showcase demo, negative safety cases (bad/), 4-way compute & HTTP/1.1 benchmarks
├── tests/Vibranium.Tests/        # 2,447 automated tests (.NET 11 <-> LLVM 18 -O3 + ASan/LSan, Std, vib-self & Analyzer parity)
└── docs/                         # Language specification (docs/SPEC.md), roadmap (docs/ROADMAP.md) & research notes
```

---

## Contributing & License

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for Conventional Commits, branch naming, squash-merge PR policy, and verification gates, and [`AGENTS.md`](AGENTS.md) for compiler architecture invariants and coding-agent guidelines.

Licensed under the [MIT License](LICENSE).
