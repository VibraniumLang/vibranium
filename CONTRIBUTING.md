# Contributing to Vibranium

Thank you for contributing to **Vibranium (`vib`)** (`https://github.com/VibraniumLang/vibranium`). Every contribution must preserve Vibranium's core guarantees: compile-time ownership/borrow safety, byte-for-byte `.NET 11` ↔ `LLVM 18` parity, zero authored C runtime shims, and machine-checked soundness.

---

## Pull Request & Git Workflow

1. **Never push directly to `main`.** All changes land through Pull Requests.
2. **Branch naming:** Create a topic branch from `main` using one of these prefixes:
   - `feat/<topic>` — New language, compiler, standard library, or CLI capability
   - `fix/<topic>` — Bug, soundness, diagnostic, or parity fix
   - `docs/<topic>` — Specification, research, or documentation updates
   - `refactor/<topic>` — Internal compiler/library restructuring with no behavioral change
   - `perf/<topic>` — Measurable compile-time or runtime optimization
   - `test/<topic>` — Differential, conformance, sanitizer, or formal test coverage
   - `ci/<topic>` — GitHub Actions or build infrastructure changes
3. **Conventional Commits (`type(scope)!: description`):**
   - Every commit message **and** every Pull Request title must follow the [Conventional Commits 1.0.0](https://www.conventionalcommits.org/) specification:
     ```text
     <type>(<scope>): <imperative summary in lowercase>
     ```
   - Allowed `<type>` values: `feat`, `fix`, `docs`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`.
   - Recommended `<scope>` values: `core`, `std`, `mir`, `analysis`, `roslyn`, `analyzer`, `llvm`, `cli`, `self`, `formal`, `samples`, `spec`, `ci`.
   - Append `!` before `:` (and include a `BREAKING CHANGE:` footer) if changing any locked language, ABI, or diagnostic contract.
   - Examples:
     - `feat(std): add bounded header parser helpers in Bytes`
     - `fix(analysis): reject escaping borrow from temporary struct receiver`
     - `docs(spec): document VIB006 linear Result<R, E> error copy rule`
4. **Squash-merged Pull Requests only:**
   - Every PR is **squash-merged** onto `main` so `main` stays strictly linear and the **PR title becomes the single commit on `main`**.
   - Keep PRs atomic and focused on one logical change.

---

## Non-Negotiable Engineering Rules

1. **Zero Warnings (`TreatWarningsAsErrors=true`):**
   - `Directory.Build.props` enforces `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` across the entire solution, and `Vibranium.App.props` promotes `CS0183`, `CS0184`, `CS8509`, `CS8519`, `CS8520`, `CS8524`, `CS8778`, `CS8793`, `CS8794`, `CS8846`, `CS9191`, `CS9192`, and `CS9193` to errors. Do not suppress compiler or analyzer warnings without an explicit, documented architectural justification.
2. **Zero Authored `.c` or `.h` Runtime Files:**
   - Vibranium's native runtime (`src/Vibranium.Core/Native/x86_64-linux-gnu/vib_rt.ll`) and platform layer (`src/Vibranium.Std/Native/x86_64-linux-gnu/vib_std.ll`) are pure textual LLVM 18 IR linking solely against system `libc`. Adding `.c` or `.h` files under `src/` is forbidden and fails `VibraniumStd_SelfCheck_ZeroDiagnostics_And_ZeroCFiles`.
3. **Analyzer ≡ CLI Diagnostic Parity (`VIB000`–`VIB008`):**
   - `Vibranium.Analyzer` (IDE/MSBuild live Roslyn analyzer) and `vib check` / `vib build` share `Vibranium.Frontend.Roslyn` and `Vibranium.Analysis`. Any change to syntax gatekeeping, MIR lowering, or Polonius borrow/move analysis must produce identical `VIB000`–`VIB008` diagnostic IDs in both paths.
4. **`.NET 11` ↔ `LLVM 18` Differential & Sanitizer Parity:**
   - Every runtime-observable feature in `Vibranium.Core`, `Vibranium.Std`, or `Vibranium.Backend.Llvm` must pass differential execution tests comparing `.NET 11` managed execution against `clang-18 -O3` and `clang-18 -fsanitize=address,leak` (`ASan` + `LSan`), as well as `opt-18 -passes=verify` on emitted LLVM IR.
   - Unrecoverable panics must flush stdout, write `panic: <message>\n` to stderr, and exit immediately with code `101` on both `.NET 11` and native LLVM 18.
5. **Mechanized Metatheory (`formal/`):**
   - Changes to core ownership, NLL borrow, or L1 linear resource rules must keep the Lean 4 suite (`VibraniumFormal`) building cleanly with **zero `sorry`** and **zero custom axioms**.
6. **Privacy & Zero Secrets:**
   - Never commit API keys, tokens, credentials, `.env` files, binary build logs (`.binlog`), or machine-specific local filesystem paths in code, comments, tests, or documentation.

---

## .NET BCL Ergonomics & Zero-Ceremony Rule

Every `Vibranium.Core`, `Vibranium.Std`, compiler, and sample contribution must uphold Vibranium's **`.NET BCL Ergonomics & Zero-Ceremony Rule`**:

1. **Use Standard `.NET` BCL Verb & Property Names:** Match `.NET 11` BCL conventions (`Vec<T>.Add`, span `.SequenceEqual` / `.StartsWith` / `.EndsWith` / `.IndexOf` / `.LastIndexOf` / `.Contains` / `.Trim*`, `int.TryParse` / `long.TryParse`, `File.Exists` / `ReadAllText` / `WriteAllText` / `AppendAllText`, `ReadLine` / `WriteLine`, `this[int]`, `^k`, `a..b`, `foreach`) rather than inventing non-standard names.
2. **Always Pair `ReadOnlySpan<byte>` with Plain `string` Overloads:** Whenever a public API accepts UTF-8 text, paths, keys, prefixes/suffixes, process arguments, or addresses, provide both `ReadOnlySpan<byte>` and plain `string` overloads (`text.AsUtf8()`) so callers never have to sprinkle `"..."u8` suffixes on string literals.
3. **Preserve Zero-GC Native Semantics & Explicit Error Values:** Ergonomic APIs must preserve deterministic ownership, Polonius NLL borrow provenance, exact-once L1 linear `IDisposable` handling, and explicit `Option<T>` / `Result<T, E>` error returns with full parity across `.NET 11`, `Vibranium.Analyzer`, Stage-0 `vib`, and `vib-self`.

---

## Mandatory Pre-PR Verification

Before opening or updating a Pull Request, run all four verification suites from a clean state and confirm zero warnings and zero failures:

```bash
# 1. Build all 15 projects with zero warnings and zero errors
dotnet build --disable-build-servers Vibranium.slnx

# 2. Run the full differential (.NET 11 vs LLVM 18 + ASan/LSan + Analyzer parity) test suite
dotnet test --disable-build-servers Vibranium.slnx --no-build

# 3. Verify the Triple-Identity self-hosting bootstrap fixed point (managed.ll == stage1.ll == stage2.ll == repro.ll)
./eng/bootstrap/bootstrap.sh --skip-benchmark

# 4. Verify the mechanized Lean 4 proofs and executable proof runner (zero sorry)
cd formal && lake build && lake exe vibranium-formal-verify
```

If your change affects code generation, runtime fragments, `Vibranium.Std` I/O or networking, or performance-critical loops, also run the 4-way parity & benchmark suites:

```bash
./samples/benchmark/run-benchmark.sh
./samples/http-server/run-http-benchmark.sh
```
