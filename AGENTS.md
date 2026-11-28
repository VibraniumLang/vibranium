# AGENTS.md — Instructions for Coding Agents (`VibraniumLang/vibranium`)

Welcome to **Vibranium (`vib`)** (`https://github.com/VibraniumLang/vibranium`): a memory-safe systems subset of C# 15 (`.NET 11`) with Rust-grade ownership and borrow guarantees, live Roslyn IDE/debugger support during development, and standalone zero-GC LLVM 18 native compilation for production.

Read this file and `CONTRIBUTING.md` before modifying any source, test, build, or proof file.

---

## Repository Architecture Map

| Path | Layer & Responsibility |
|---|---|
| `Vibranium.slnx` | Solution containing all compiler libraries, `vib` CLI, samples, and xUnit differential tests |
| `.editorconfig` | Repo-wide compile-time C# 15 style rules (`IDE0161` file-scoped namespaces, `IDE0290` primary constructors, `IDE0063` simple `using`) |
| `Directory.Build.props` | Repo-wide MSBuild defaults (`Nullable=enable`, `ImplicitUsings=disable`, `Deterministic=true`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`) |
| `Vibranium.App.props` | Vibranium application MSBuild profile (`net11.0`, C# `15.0`, implicit `Vibranium`, `static Vibranium.Prelude`, and `static Vibranium.Std.ConsoleIo` usings, `VibraniumAppBootstrap` panic-exit `101` hook, `CS0183`/`CS0184`/`CS8509`/`CS8519`/`CS8520`/`CS8524`/`CS8778`/`CS8793`/`CS8794`/`CS8846`/`CS9191`/`CS9192`/`CS9193` as errors) |
| `src/Vibranium.Core/` | Core types & intrinsics (`Option<T>`, `Result<T, E>`, `OptionRef<T>`, `OptionMut<T>`, `Vec<T>`, `Unit`, `Utf8Lines`, `Utf8ArgEnumerator`, `StringUtf8Extensions`, `Utf8SpanExtensions`, `[Mut]`, `[LifetimeOf]`, `[Native]`, `Prelude`, `Io`, `Stdio`, `VibraniumRuntime`, `VibraniumPanicException`, `Bootstrap/VibraniumAppBootstrap.cs`, and `Native/x86_64-linux-gnu/vib_rt.ll` defining `@__vib_*` internal runtime symbols including `@__vib_utf8_scan`) |
| `src/Vibranium.Std/` | Zero-C standard library (`Vibranium.Std` & `Vibranium.Std.Sys` 17-leaf T0 Catalog v2, `Sys/Sys.Managed.cs` `.NET 11` BCL adapter, `Native/x86_64-linux-gnu/vib_std.ll` defining `@__vib_sys_*` symbols, and pure-Vibranium T1 modules: `Utf8`, `Bytes`, `ByteSpanExtensions`, `StringBuf`, `ConsoleIo`, `IntMap<V>`, `StrMap<V>`, `Interner`, `PathText`, `IoError`, `File`, `Fs`, `ArgList`, `Env`, `Command`, `Process`, `Ipv4Address`, `Ipv4EndPoint`, `TcpListener`, `TcpStream`) |
| `src/Vibranium.Compiler.Core/` | Roslyn-free control-flow-graph MIR (`Mir.cs`) and canonical diagnostics (`VIB000`–`VIB008` in `Diagnostics.cs`) |
| `src/Vibranium.Analysis/` | Roslyn-free Polonius-style flow-sensitive move, definite-initialization, NLL borrow, two-phase receiver borrow, escape provenance, and L1 linear `IDisposable` solver (`BorrowAndMoveAnalyzer.cs`) |
| `src/Vibranium.Frontend.Roslyn/` | Roslyn `5.12.0` syntax/symbol gatekeeper (`SyntaxAndSymbolGatekeeper.cs`) and `ControlFlowGraph` → MIR lowerer (`CfgMirLowerer.cs`, `CfgMirLowerer.Expressions.cs`, `VibraniumFrontend.cs`, `VibraniumSymbolHelpers.cs`) |
| `src/Vibranium.Analyzer/` | Live IDE/MSBuild Roslyn `DiagnosticAnalyzer` (`VibraniumDiagnosticAnalyzer.cs`, `AnalyzerReleases.*.md`, reporting `VIB000`–`VIB008`) |
| `src/Vibranium.Backend.Llvm/` | Pure textual LLVM 18 IR emitter (`Llvm18Emitter.cs`, `@__vib_*` runtime symbols, bounded-stack iterative worklist drop glue, unpacked `Span<T>` `noalias` vectorization metadata) |
| `src/vib/` | Stage-0 Roslyn native compiler CLI (`vib check`, `vib emit-ll`, `vib build`, `vib run`) |
| `src/vib-self/` | Self-hosted pure-Vibranium compiler (`vib-self`, `vib-stage1`, `vib-stage2`) with zero Roslyn/C dependencies |
| `eng/bootstrap/bootstrap.sh` | Triple-Identity self-hosting fixed-point verifier (`managed.ll == stage1.ll == stage2.ll == repro.ll`) & compile-speed benchmark |
| `formal/` | Mechanized Lean 4 (`v4.35.0`) formal verification suite (`VibraniumFormal`, executable `vibranium-formal-verify`) proving `aliasing_xor_mutability`, `native_memory_safety`, and `clr_llvm_equivalence` with zero `sorry` |
| `samples/` | Showcase (`samples/demo/`), negative safety examples (`samples/bad/`), 4-way compute benchmark (`samples/benchmark/`), and 4-way HTTP/1.1 server (`samples/http-server/`) |
| `tests/Vibranium.Tests/` | Differential `.NET 11` ↔ `LLVM 18` (`-O3` + `ASan`/`LSan`), `opt-18` verification & SIMD vectorization, `Vibranium.Std` parity, and `Vibranium.Analyzer` ≡ `vib` diagnostic parity tests |
| `docs/SPEC.md` | Authoritative Vibranium language, ownership/borrow, and LLVM 18 lowering specification |
| `docs/ROADMAP.md` | Post-`v0.2` language, cross-platform/`wasm32`, `async`/`await`, and full-stack ecosystem roadmap |
| `docs/ROADMAP-PROPOSAL.md` | Decided foundations roadmap to a stable 1.0 (web backend and browser frontend; native UI and games in 1.1), replacing `ROADMAP.md`'s milestones from v0.3 on |

---

## Strict Rules for Agents

1. **Follow `CONTRIBUTING.md` Workflow:**
   - Never commit or push directly to `main`.
   - Use topic branches (`feat/...`, `fix/...`, `docs/...`, `refactor/...`, `perf/...`, `test/...`, `ci/...`).
   - Format all commit messages and Pull Request titles using **Conventional Commits** (`type(scope)!: description`). Every PR is **squash-merged** onto `main`.
2. **Zero Authored `.c` or `.h` Files:**
   - Never introduce `.c` or `.h` files into `src/`. All low-level runtime and OS leaves live in textual LLVM 18 IR (`vib_rt.ll` and `vib_std.ll`) linking only system `libc`.
3. **Strict `.NET 11` ↔ `LLVM 18` & Analyzer Parity:**
   - Every valid Vibranium program must compile with zero warnings under `.NET 11` (`dotnet build`) **and** `vib build`, producing identical stdout, stderr, exit codes (including `101` on panic), and observable I/O effects.
   - `Vibranium.Analyzer` and `vib check` must emit identical `VIB000`–`VIB008` diagnostic IDs on every rejected program.
   - All emitted LLVM IR must pass `opt-18 -passes=verify` and execute cleanly under `clang-18 -fsanitize=address,leak`.
4. **Privacy, Hygiene & Zero Secrets:**
   - Never commit secrets, tokens, `.env` files, `.binlog` artifacts, generated `.ll`/native binaries, or machine-specific local user home paths or hardcoded local clone paths anywhere in the repository or logs.
   - Allocate scratch files/directories in the OS temporary directory (e.g., `mktemp -d` or `Path.GetTempPath()`) and remove them before finishing.
5. **Pre-Delivery Verification:**
   - Run `dotnet build --disable-build-servers Vibranium.slnx`, `dotnet test --disable-build-servers Vibranium.slnx --no-build`, `./eng/bootstrap/bootstrap.sh --skip-benchmark`, and `(cd formal && lake build && lake exe vibranium-formal-verify)` before concluding any task.

---

## .NET BCL Ergonomics & Zero-Ceremony Rule

Vibranium is a memory-safe systems subset of C# 15 / `.NET 11` built for both human C# engineers and AI coding agents. Every new library API, compiler intrinsic, and sample **must** follow the **`.NET BCL Ergonomics & Zero-Ceremony Rule`**:

1. **Standard `.NET` BCL Names & Structural Patterns First:** Always prefer standard BCL method, property, and pattern names (`Vec<T>.Add`, `ByteSpanExtensions.{SequenceEqual, StartsWith, EndsWith, IndexOf, LastIndexOf, Contains, Trim, TrimStart, TrimEnd}`, `int.TryParse` / `long.TryParse`, `File.{Exists, ReadAllText, WriteAllText, AppendAllText, ReadAllBytes, WriteAllBytes, AppendAllBytes}`, `ReadLine` / `TryReadLine` / `WriteLine`, `this[int]`, `^k`, `a..b`, `foreach`) over unfamiliar custom names so idiomatic C# compiles on the first try without guesswork.
2. **Dual `ReadOnlySpan<byte>` + Plain `string` Overloads Everywhere:** Every public API accepting UTF-8 text, file paths, map keys, search needles, CLI commands/arguments, IPv4 addresses, or panic/error messages must provide a plain `string` overload alongside `ReadOnlySpan<byte>` (forwarding via `text.AsUtf8()`), eliminating `"..."u8` suffix ceremony at call sites while lowering to zero-copy `{ ptr, i32 }` pooled UTF-8 bytes in native LLVM 18.
3. **Zero Hidden Allocation, Zero Exceptions, Zero Safety Compromise:** Ergonomic BCL APIs must remain thin, allocation-transparent wrappers or compile-time aliases over Vibranium's ownership, Polonius NLL borrow, L1 linear `IDisposable`, and `Option<T>` / `Result<T, E>` primitives — never introducing hidden native allocations, exceptions, or lifetime holes.
4. **4-Way Pipeline Parity:** Every ergonomic overload, BCL symbol alias (`System.MemoryExtensions.*<byte>`, `int`/`long.TryParse`), implicit `Index`/`Range` pattern, and closed `foreach` view (`Utf8Lines`, `Utf8ArgEnumerator`) must behave identically across `.NET 11`, `Vibranium.Analyzer`, Stage-0 `vib`, and self-hosted `vib-self`.

---

## External `.NET`, C#, MSBuild & Analyzer Skills

When working on C# 15 language features, `.NET 11` APIs, Roslyn analyzers, MSBuild `.props`/`.targets`, testing, benchmarking, or GitHub Actions workflows, consult Microsoft's official `.NET` & C# agent skills repository at **`https://github.com/dotnet/skills`**:

- **C# 15 & `.NET 11` Development:** `csharp-expert`, `csharp-refactoring`, `process-api-net11`, `migrate-dotnet10-to-dotnet11`, `vectorization`
- **MSBuild & Repository Layout:** `msbuild`, `directory-build-organization`
- **Testing & Verification Quality:** `code-testing`, `assertion-quality`, `test-anti-patterns`, `run-tests`
- **Performance & Benchmarking:** `microbenchmarking`, `analyzing-dotnet-performance`
- **GitHub Actions Workflows:** `authoring-github-workflows`

When modifying `src/Vibranium.Analyzer/` or Roslyn semantic/CFG analysis in `src/Vibranium.Frontend.Roslyn/`, also consult the **`roslyn-analyzers`** skill at **`https://github.com/JeremyKuhne/agent-skills`**.

**How to access skills (zero local path assumptions):** Do not assume any pre-existing local clone or hardcoded host path on disk. Agents may freely choose whether to fetch and read individual `SKILL.md` files directly from GitHub (`https://github.com/dotnet/skills` and `https://github.com/JeremyKuhne/agent-skills`) or shallow-clone the repository (`git clone --depth 1 ...`) into a disposable temporary scratch directory outside this workspace (cleaning it up when finished).
