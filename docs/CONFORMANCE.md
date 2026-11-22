# Vibranium v0.2 Differential Conformance

This document defines the v0.2 conformance scope as an explicit feature matrix and describes the
differential conformance fuzzer in `eng/conformance/` that checks it. The goal is to find frontend
disagreements with a tool instead of by hand-written probes, so that review rounds converge.

The matrix is generated from the fuzzer's own catalog (`eng/conformance/Features.cs`,
`vib-conformance features --markdown`), so the generator tags, the expected diagnostics and this
document cannot drift apart.

## 1. What is compared

Every generated program goes through four oracles:

| # | Oracle | How it runs |
|---|---|---|
| 1 | **SDK**: pinned Roslyn `5.12.0` with the `Vibranium.App.props` settings, plus the real `Vibranium.Analyzer` | In process: the `net11.0` reference pack, built `Vibranium.Core`/`Vibranium.Std` as metadata, SDK-style implicit global usings, `VibraniumAppBootstrap.cs` under its real source path (as csc gets it; from the A-F3 trust fix on, the Analyzer authenticates the bootstrap by path and content hash, so release-6 no longer gives it the IDE name `obj/__VibraniumAppBootstrap.cs`), `Nullable=enable`, `WarningLevel=11`, and the `<WarningsAsErrors>` list read from the target tree's `Vibranium.App.props` (on Round-11 candidate 4: `CS0183`, `CS0184`, `CS8509`, `CS8519`, `CS8520`, `CS8524`, `CS8778`, `CS8793`, `CS8794`, `CS8846`, `CS9191`, `CS9192`, `CS9193`). Warnings are kept separate because `TreatWarningsAsErrors` turns them into build errors. The analyzer runs through `CompilationWithAnalyzers`, as in MSBuild and the IDE. |
| 2 | **Stage-0 `vib check`** | In process, through the same path as the CLI: `VibraniumCompilerDriver.CreateCompilationFromPaths` with Std sources, Roslyn errors, then `VibraniumFrontend.CompileAndCheck`. Its VIB IDs must equal the Analyzer's IDs. |
| 3 | **Self-hosted `vib-self check`** | The managed `vib-self.dll` in its own `AssemblyLoadContext` (private Core/Std statics), with arguments injected and stdout/stderr captured in memory. `--self process` runs the real `dotnet vib-self.dll` instead. |
| 4 | **Execution** (only when every frontend accepts) | The managed program (Roslyn emit + `dotnet`) against the Stage-0 native binary (`Llvm18Backend` + the exact `vib build` clang step) and the self native binary (the real `vib-self build`, in process, which runs its own clang command). The three runs must have equal stdout, stderr and exit code; a panic is exit `101` with `panic: <msg>` on stderr. `-O0` is always used; a sampled fraction also runs `-O3` with ASan/LSan. The soundness profile (§2) runs every accepted program at `-O0` and `-O3`, both under ASan/LSan. Each `-O0` binary's `DT_NEEDED` list is checked against the README libc-only contract. From release-7, the IR both native compilers emit for an accepted program is first checked against the static lowering contract (verdict rules below), also with `--no-exec`. |

### Verdict rules

* **C#-invalid** (a C# error under the project's language settings and references): every frontend must reject, including standalone `vib-stage2` without a Roslyn pre-check. If a frontend accepts, the result is `invalid-accepted`, a compiler defect even when it has no safety impact. The self-hosted compiler need not match Roslyn's diagnostic ID or wording. Warnings alone do not establish C# invalidity.

  The separate [invalid-program mutation oracle](MUTATION.md) measures this behavior. Its first-CS-ID buckets, severity classes (safety-relevant and other, formerly `MANDATORY` and `OUTSIDE-CONTRACT`), and known-open list support measurement and prioritization; they do not waive required rejection. A passing run against managed `vib-self` is not a clean native `vib-stage2` release gate.
* **C#-valid**: Stage-0 and self must agree on accept/reject *and* on the exact set of VIB IDs. If both accept, the three executions must be equal.
* **Non-terminating programs.** If the managed reference run times out (20 s), the generated program does not terminate (for example a loop step that folds to `0`). If it ends with a CLR stack overflow (exit 134, stderr starting with `Stack overflow`), it recursed without bound, and SPEC excludes call-stack exhaustion from the parity guarantee. Partial outputs are not comparable, so either program is counted in `nonTerminatingSkipped` and not compared. From release-5 on, `summary.json` also lists each skipped program (`nonTerminatingPrograms`: index, plan, reason; up to 200), and `report.md` prints them, so every skip can be replayed and triaged. A frontend hang cannot hide behind this skip: it applies only when the managed CoreCLR run of the Roslyn-compiled program does not finish, a self check that hangs is a `frontend-crash`, and a native run that hangs while the managed run finished is a `run-timeout`.
* **Native call-stack exhaustion (from release-6).** When the managed run finishes but a native leg ends with SIGSEGV (exit 139) or an ASan `stack-overflow` report, the leg is run again with `ulimit -s unlimited`. If it then matches the managed run, the leg is excused: the program is listed in `nonTerminatingPrograms` with the reason `native stack exhaustion only (...)` and counted in `nativeStackExhausted`, and the other legs are still compared. Deep recursion that fits the CLR's stack but not an 8 MB native stack (self `-O0` frames are larger than Stage-0's) is call-stack exhaustion, which SPEC excludes from the parity guarantee (§6.12). A leg that still differs with an unlimited stack is compared as usual.
* **Slow native leg (from release-7).** When the managed run finishes (exit 0 or 101) but a native leg exceeds the 20 s budget, the leg is run once more with a 200 s budget. If it then matches the managed run, the leg is excused: the program is listed in `nonTerminatingPrograms` with the reason `native leg slower than the 20 s budget only (...)` and counted in `nativeSlowExcused`, `run --exec` prints the leg as `SLOW: ...` with both run times, and the other legs are still compared. An unoptimized `-O0` binary can be much slower than the CLR's optimizing JIT on a tight loop: the default campaign's only buckets were a loop that ends only when an `int` counter wraps (about 4.3 billion iterations), which took 19–20 s at `-O0`, 3 ms at `-O3` and under 20 s managed, with the same panic and exit 101 everywhere (§6.18). Speed is not part of the parity guarantee. A leg that still times out after 200 s, or that finishes with a different result, is compared as usual. *From release-7.1* there is no slow re-run when an earlier leg of the same program (Stage-0's leg of the same level runs first) matched the managed run in under 2 s: the program is not slow, so the leg hangs. `run --exec` prints it as `TIMEOUT: over the 20 s budget while <leg> matched the managed run in N ms (no slow re-run)`, and it is a `run-timeout`. Without this, each self binary that looped on the N19 class (§6.20) cost 220 s per leg, and the soundness `ci` gate took more than 17 minutes.
* **Warning-only** programs (valid C#, but with a warning that `TreatWarningsAsErrors` would turn into an error) are compared like valid ones. They are counted by warning ID (§6), so that semantic warnings that behave like `CS9192` are visible. *Decided (§7.5):* seven semantic warnings are promoted to errors from Round-11 candidate 1 on: CS0183, CS0184, CS8519, CS8520, CS8793, CS8794 and CS9191. The oracle reads the promotion list from the target tree, so these programs become C#-invalid there as soon as the tree lists them.
* **Banned-by-design and cut constructs (decided rule).** A program that contains a banned-by-design or cut construct must be rejected by the Analyzer, Stage-0 and self, and each of them must report the documented ID of a banned construct the program contains (`banned-accepted`, `banned-wrong-id`). Secondary diagnostics reported after such a fail-closed subset rejection are **not** compared between Stage-0 and self. That covers cascades (for example `VIB000` next to `VIB008` for `int?`) and later-phase findings that a frontend skips once the subset check has failed. Full VIB ID-set parity stays required for every program without a banned construct. Analyzer ≡ Stage-0 ID equality (AGENTS.md rule 3) is still checked on every program. SPEC states this rule from `candidate-freeze-3` on.
* **Cuts C10–C21: exact message at the anchor (from release-6; C21 from release-7).** The tool's own Roslyn scan (`CutScan.cs`) finds the first occurrence of each cut C10–C21 in a program (§7.15) and tags it `cut.*` (catalog kind `cut`, expected VIB000). Besides the banned/cut rule above, every frontend that rejects the program must report, for each cut found, exactly one VIB000 with the cut's exact message at the cut's anchor (line and column). Otherwise the result is `cut-anchor`, with the class `missing` (no VIB000 with that message), `duplicate` (more than one) or `position` (one, at another position). The positions are kept out of the signature (they go into the bucket's oracle file and `report.md`), so one wrong anchor rule makes one bucket. Secondary diagnostics are not compared, as for every banned or cut construct.
* **Soundness oracle (soundness profile, from release-6).** Expectation-free: a program that the Analyzer, Stage-0 and self all accept must run ASan/LSan-clean at `-O0` and `-O3` for both native compilers, with the managed run's stdout, stderr and exit code. Any `sanitizer`, `output-mismatch` or `native-build-failure` bucket of such a program is an accepted-but-unsafe (or miscompiled) program, whatever SPEC says about the shape, so it does not depend on the frontends being right about the borrow rules: a shared deviation that the differential oracles cannot see shows up as memory misuse at run time. *From release-7* every soundness scenario also carries its static SPEC expectation (`snd.expect-conflict`: a later use keeps the loan live across a conflicting invalidation, VIB003; `snd.expect-use-after-move`: the loan is re-acquired from a moved owner, VIB001; `snd.expect-field-move`: a field moved out of a class, VIB005), checked by the expectation oracle below, so a shared deviation is also a `spec-accepted` bucket before anything runs, including with `--no-exec`.
* **Expectation oracle (activation profile).** Differential comparison cannot see a SPEC violation that every frontend shares. A program of the activation profile (§2) may carry a *vib-reject* tag: a C#-valid shape that SPEC rejects with a known ID (catalog kind `vib-reject`, §5). If such a program has no banned or cut construct and the Analyzer, Stage-0 and self all accept it, the result is `spec-accepted`. If all three reject it but one of them does not report a tag's expected ID, the result is `spec-wrong-id`. When only some frontends accept, the program is already an `accept-reject` or `analyzer-vs-stage0` disagreement. Like cut tags, vib-reject tags are not emitted for code that Roslyn proves unreachable. The expected IDs come from SPEC and the decided call-activation model (§7). From release-7.1 an *expected-accept* side covers the accepted shapes of the `ViewMove` anchor (§2, §7 item 19): in a C#-valid program without any vib-reject tag, if the Analyzer, Stage-0 and self all report a diagnostic on the line of a `ViewMove` helper (each helper is one line, `static int VmN(int p) { ... }`), the result is `spec-rejected`, a shared over-rejection. Diagnostics on other lines (other anchors, near-miss mutations) do not count.
* **Static IR lowering contract (from release-7).** When every frontend accepts a program, the tool takes the LLVM IR that Stage-0 (`Llvm18Backend`, as `vib emit-ll`) and self (`vib-self emit-ll`, in process) emit for it and checks the SPEC §5.3 rules that keep C#-defined results out of LLVM's undefined behaviour, without clang and without running anything: `shift-mask` (a non-constant shift count is an `and` with the operand width − 1), `shift-count-out-of-range` (a constant count is below the width), `division-unguarded` (every `sdiv`/`udiv`/`srem`/`urem` by a non-constant divisor has an `icmp eq`/`ne` of the divisor with 0 that reaches a `br i1`, directly or through i1 `and`/`or`/`xor`; two loads of one pointer count as one divisor) and `signed-overflow-unguarded` (every `sdiv`/`srem` by a non-constant divisor or by −1 also tests the divisor against −1 and the dividend against the type's minimum, both reaching a branch). Runtime functions (`@__vib_*`) are skipped. A violation is an `ir-contract` disagreement. It runs in every profile and with `--no-exec`, so a backend that drops a guard or a mask is caught even when no generated input reaches the undefined case (§6.16).

### Disagreement kinds

| Kind | Meaning |
|---|---|
| `invalid-accepted` | Roslyn reports an error and some frontend accepts. |
| `sdk-vs-stage0-csharp` | Roslyn (SDK) accepts but Stage-0 reports a CS error (for example a source-vs-metadata visibility gap). |
| `analyzer-vs-stage0` | The Analyzer's VIB ID set differs from Stage-0's. |
| `accept-reject` | On a program without banned constructs, one of Stage-0 and self accepts and the other rejects (`stage0-accepts` / `self-accepts`). |
| `id-mismatch` | Both reject a program without banned constructs with different VIB ID sets. The signature uses the symmetric difference (`stage0-only:{…} self-only:{…}`), so reduction can drop diagnostics both report. |
| `banned-accepted` / `banned-wrong-id` | A banned/cut row is accepted, or rejected without its documented ID. |
| `cut-anchor` | A frontend rejects a program with a C10–C21 cut but does not report exactly one VIB000 with the cut's exact message at its anchor (`missing`, `duplicate` or `position`; the signature names the cut tag, the frontend and the class). |
| `spec-accepted` / `spec-wrong-id` | Expectation oracle: every frontend accepts a vib-reject shape, or every frontend rejects it but one lacks the expected ID. The signature names the tag and, for `spec-wrong-id`, the frontend and the IDs it gave (`tag>frontend:IDs`). |
| `spec-rejected` | Expected-accept oracle (release-7.1): every frontend reports a diagnostic on the line of an accepted `ViewMove` shape. The signature names the accept tags and each frontend's IDs on those lines. |
| `ir-contract` | Static lowering contract (release-7): the IR that Stage-0 or self emits for a program every frontend accepts breaks a SPEC §5.3 rule. The frontends field names the compiler (`stage0`, `self`) and the IDs field the rules (`shift-mask`, `shift-count-out-of-range`, `division-unguarded`, `signed-overflow-unguarded`); the first offending instruction is in the oracle file. |
| `output-mismatch` | A native run differs from the managed run (`exit`/`stdout`/`stderr`). |
| `native-build-failure`, `link-contract`, `sanitizer`, `run-timeout`, `managed-crash`, `frontend-crash` | As named. `link-contract` means a `DT_NEEDED` entry other than libc or the dynamic loader. |

## 2. Generator

* **Grammar/AST based.** Programs are typed ASTs (`Ast.cs`) printed deterministically (`Printer.cs`). Tags are computed from the AST structure, not from generator intent, so a minimized program carries exactly the features it still contains.
* **Anchors.** There are 19 general productions (`SwitchStmt`, `SwitchExpr`, `OutArg`, `Underscore`, `Const`, `LocalFunc`, `StructInit`, `RefParams`, `Collection`, `CondArg`, `Loop`, `Using`, `OptionResult`, `Panic`, `Arith`, `PatternVar`, `ClassInstance`, `Banned`, and from release-7.1 `UnionSwitch`, described below). Each one randomizes its own shape: selector type, labels, guards, placement, container (top-level or class method), static local functions, helper methods and receivers.
* **Pairwise or better interactions.** Program *i* uses ordered anchor pair *i* mod 361 from a seed-shuffled schedule of all 19×19 pairs, so every ordered pair is covered every 361 programs (324 pairs of 18 anchors before release-7.1). With probability 0.3 a third anchor is nested innermost.
* **Near-miss mutations** (rate 0.3, `Mutator.cs`): duplicate name, declaration moved after use, initializer dropped, argument modifier changed, declaration embedded as an `if` body, switch terminator or default dropped, cases reversed, scope leak, `const` turned into `var`, `static` removed from a local function. Most mutants are C#-invalid on purpose, and every frontend must reject them.
* **Bounded and reproducible.** Programs are at most 30 printed lines. Each one is a pure function of `(seed, index)` (SplitMix64), and `replay --seed S --index I` regenerates it exactly. Re-draws for the size bound are derived from the same seed.
* **No hard-coded repros.** Known-root repros are not generator templates. Calibration measures whether the general productions rediscover them.
* **Extended profile (opt-in, `--profile extended` or `CONF_PROFILE=extended`).** Five more anchors widen coverage:
  `SpanSlice` (spans from collections and `Vec`, `a..b`/`..^k` ranges, `^k` indexes, `Slice`, `foreach`/`ref`
  `foreach`, span and read-only span arguments, out-of-range panics), `OptionSlot` (`Option<Cell>` slots through
  `AsRef`/`AsMut`, `OptionRef`/`OptionMut` locals and parameters, `Take`, `Replace`, `Set`, chained `Next` slots),
  `MutReceiver` (`[Mut]` class methods, two-phase receiver calls, `ref`/`in` receivers, mutating and `readonly` struct
  methods, nested `[Mut]` forwarding), `Utf8View` (`foreach` over `Lines()` of string literals, `u8` spans,
  `StringBuf` and `Vec<byte>`, line operations, `TryParse`, a line saved into a `scoped` local past the loop) and
  `StdText` (`IntMap`/`StrMap`, `StringBuf` appends including `AppendHex`/`AppendRune`/`+=`/`Truncate`, byte-span
  `Trim`/`IndexOf`/`StartsWith`/`Contains`, `int`/`long.TryParse` edge inputs). About a quarter of these programs
  also carry one deliberate borrow-rule near-miss that is valid C# but invalid Vibranium (catalog kind
  `vib-invalid`, expected ID in the row): the frontends must then agree on the IDs, and `summary.json`/`report`
  count per tag how often every frontend accepted such a program, as a lead to vet by hand. The extended schedule is
  the 215 ordered pairs of the 24 anchors that contain at least one new anchor (the 361 default pairs are not
  repeated; 205 pairs of 23 anchors before release-7.1), the optional third anchor is drawn from all 24, and programs
  may have up to 45 lines. The default
  profile is unchanged: its anchor list, schedule and draws are the same as before, so every default (seed, index)
  pair still regenerates the same program (checked by replaying four corpus buckets from seeds 4242 and 2026). This
  held until release-7.1, whose `UnionSwitch` anchor changed the default stream (below).
  A (seed, index) pair is reproducible within its profile: pass the same `--profile` to `replay`.
* **Activation profile (opt-in, `--profile activation` or `CONF_PROFILE=activation`).** Nine more anchors target the
  Round-11 audit roots without hard-coding their repros:
  `CaCall`, `CaReceiver` and `CaAssign` put a borrow and a move of related places into one call or assignment, over
  loan kinds (`in` (until release-5 also `ref readonly`), `ref`, `out`, views from `AsSpan`/`AsReadOnlySpan`/slices/`AsRef`/`AsMut`, field
  projections, shared and `[Mut]` receivers, `this in`/`this ref` extension receivers, `[Mut] operator +=`, indexer
  get and set, compound index, ref-local and field assignment targets) × move forms (by value, `Move(x)`, a consuming
  `Unwrap()`) × place relations (same, prefix, extension, and an unrelated owner as the accepted control) × call forms
  (static local function, static method, instance method, constructor) and owners (`Vec`, class, holder class with
  projections, `Option`, linear `IDisposable`, readonly struct). Each scenario is its own helper function whose first
  statement is the conflict, so it is always reachable. `ViewReturn` returns views through parameters under
  `[return: LifetimeOf]` and elision (to `this`, to a single input, ambiguous with two inputs, a reborrow of a
  `Span<T>` parameter), and from release-7 a collection expression assigned to a by-value `ReadOnlySpan<int>`
  parameter that is then returned (directly, on one branch, as `[]` or as a slice; SPEC §3.7: VIB003 and VIB007,
  tags `view.param-stack-escape` and `view.param-stack-scope-exit`), with the parameter re-sliced from itself as the
  control. This shape found N16 (§6.17) and makes seeded bug S3 observable (§6.16). It changed the activation
  stream: an activation (seed, index) pair of release-6 regenerates only with the release-6 tool. The default,
  extended and soundness streams are unchanged (on candidate 6 the default and soundness `ci` gates have the same
  validity and execution counts as release-6 on the combined cut source). `CondTemp` passes conditionals whose arms view call temporaries as arguments, or stores one in
  a local. `FloatNint` converts in-range, out-of-range and special float constants and runtime values to
  `long`/`ulong`/`int`/`uint` (until release-5 to `nint`/`nuint`, which C17 cuts) and prints their bounds. `StdAppend` appends to `/dev/stdout` or
  `/dev/stderr` (a pipe under the harness) as the first statement. `DeclCuts` emits the declaration cuts C5–C9
  (`#if`/`#define`, `file` types, `partial` types and members, a type nested in a generic type, `: this(...)`) with
  their controls (`#region`, nesting in a non-generic type, several constructors without chaining) and an instance
  call on `this` before or after full initialization. `ViewMove` (release-7.1) covers the assignment-value cascade
  class (N17 and N18, §6.19): a `!Copy` view (`Span<int>` or `OptionMut<T>`) moved by a plain move and then used
  through the source and the destination (the owner read or written in between, or the move on one branch only); a
  view declared before a loop from another owner and reassigned in it by a plain move, a call result, or an
  assignment value used as a receiver or an argument; and the value of an assignment to a view used as a receiver,
  an argument or a chain, then the view used again. Real conflicts (the owner used while the destination is live,
  the old value used at the top of the next iteration, a moved source used at the top of the next iteration) and
  accepted controls (only the destination used, the source reassigned from another owner, no later use, a
  `ReadOnlySpan<int>` destination) come with them. A fourth family rebinds a live view to another owner (`t =
  b.AsSpan()`, `t = s`, `t = Id(..)`, a `ReadOnlySpan<int>`, or in a loop) and then mutates its old owner while the
  view is still used, which is accepted because the rebinding ends the old borrows; a view derived from the old value
  that is still live is the real conflict. This family makes seeded bug XV1 observable (§6.19). The expected results
  are in §7 item 19. Program-wide, 12% of activation programs start with
  `// <auto-generated/>` and 8% carry the marker line `// vib-conformance: file-name Program.g.cs`, which makes the
  engine compile the program as `Program.g.cs` (text-based, so `run min.cs` keeps the file name); the Analyzer must
  still report in both. The SPEC-invalid scenarios carry `vib-reject` tags checked by the expectation oracle (§1).
  The schedule is the 440 ordered pairs of the 27 anchors (the default anchors without `Banned`, `UnionSwitch`
  included, plus the nine) that contain at least one activation anchor or `UnionSwitch` (336 pairs of 25 anchors in
  release-7); programs may have up to 60 lines. `ViewMove` is appended after every other anchor, so on its own it
  changed only the activation stream (`gen-stats` seed 12 gave byte-identical statistics for the default, extended
  and soundness profiles with and without it); `UnionSwitch` then changed every stream (below).

* **C10–C20 retargeting (release-6).** The cuts C10–C20 (§7.15) made many generated shapes invalid Vibranium. From
  release-6 on no profile generates them (nor, from release-7, the C21 form `: base(...)`); `gen-stats` prints a
  `cut forms (C10–C21)` line from the same scan the
  engine uses (`CutScan.cs`), and on seed 12 × 1,500 programs it is 0 for the default, extended, activation and
  soundness profiles, mutants included (with the release-5 generator more than half of the programs had one). The
  only exception is deliberate: the C1 shape (a local function directly in a switch section, `SwitchStmt`/`Const`)
  is still generated as a cut test, and since a local function there reports both C1 and C18, those programs (9, 1,
  1 and 1 of 1,500 in the four profiles) expect the C18 message too; `gen-stats` counts them separately. What
  changed: conditions, guards and switch selectors are never C# constants (`Cond`/`Selector` in `Gen.cs`; an operator
  selector whose parsed rightmost operand is constant is parenthesized); patterns use constants, `or` and `not` (a
  complete integral label set is `k` + `not k`) instead of relational and `and` patterns, and guarded arms no longer
  count toward exhaustiveness; a switch section that declares anything is wrapped in a block (`DecutSection`, also
  after mutation), and the cross-section variable shapes are gone; the `Underscore` anchor emits only discards
  (`_ = e`, `out _`, `out var _`, `out int _`, `_` arms); `in` replaces `ref readonly` parameters, and the activation
  call scenarios pass named arguments in position 20 % of the time (`ca.named-in-position`); `FloatNint` targets
  `long`/`ulong`/`int`/`uint`; a conditional chooses between collection locals instead of nesting collection
  expressions (`coll.conditional-locals`); and recursive local functions are called with an argument `% 1000`. The
  tags of the removed shapes stay in the catalog as kind `retired` (§5), so older corpus buckets keep their meaning.
  Each cut has matrix probes, valid controls and, for C11, near-misses with real discards (§5.2). The program streams
  of all profiles changed, so a (seed, index) pair of an earlier release regenerates only with that release's tool.
  The share of unmutated C#-invalid programs fell (default seed 12 × 1,500 on Round-11 candidate 4: 178 instead of
  287 with the release-5 generator; activation: 116).
* **Soundness profile (opt-in, `--profile soundness` or `CONF_PROFILE=soundness`, release-6).** Seven anchors cross
  every loan producer with every invalidator of its owner and every placement of the later use. Producers:
  `SndSpan` (`AsSpan`, `AsReadOnlySpan`), `SndSlice` (`v.AsSpan()[a..b]`, `Slice`, `ReadOnlySlice`, `v[a..b]`),
  `SndView` (a view returned through a helper), `SndRefLocal` (`ref` to a `Vec` element or a class field), `SndOptRef`
  and `SndOptMut` (`AsRef()`/`AsMut()` of an `Option<Cell>`) and `SndRecv` (a view returned by a receiver method).
  Invalidators: move, consuming call (`Sink(v)`, `Sink(Move(v))`), growth, `Clear`, `Resize`, reassignment, a `[Mut]`
  call, moving a field out, `Take`, `Replace`, and the scope exit of a class owner (views of struct owners cannot leave
  their block in C#). Placements: after, before, re-acquired, in a branch, in disjoint branches, in a loop, in a loop
  that re-acquires, in a switch section, after a loop, and as nested call arguments in both orders. The use reads or
  writes through the loan. Each scenario is its own helper function and carries scenario-coordinate tags
  (`snd.producer-*`, `snd.inval-*`, `snd.place-*`, `snd.use-read`/`-write`; catalog kind `scenario`). Many
  combinations are invalid Vibranium on purpose and must be rejected by every frontend. From release-7 the scenario
  carries its expected ID as a vib-reject tag (`snd.expect-*`, §1), derived from the producer, the invalidator and the
  placement (a loan on `h.K` is disjoint from `h.Items`; `before`, `reacquire`, `branch-disjoint`, `loop-reacquire`
  and `nested-use-first` keep no loan live across the invalidation). In addition every accepted program runs under ASan/LSan at `-O0` and `-O3` for both native compilers (100 % sanitizer
  rate) and must match the managed run: this is the soundness oracle of §1, which reports an accepted program that
  dangles even when every frontend agrees. The schedule is the 336 ordered pairs of the 25 anchors (the default
  anchors without `Banned`, `UnionSwitch` included, plus the seven) that contain at least one soundness anchor or
  `UnionSwitch` (287 pairs of 24 anchors in release-7); programs may have up to 60 lines, and near-miss mutation is
  rarer (10 %), because mutants are mostly C#-invalid and never run. On seed 12 × 1,500, 1,158 of the 1,331 unmutated
  programs are C#-valid and 26 more are warning-only (release-7: 1,182 of 1,345 and 27). The default, extended and activation profiles do not use these
  anchors.
* **`UnionSwitch` (release-7.1, every profile).** Switch statements and switch expressions over `Option<T>` and
  `Result<T, int>` whose labels mix the union cases with labels that test the whole union: `not` of a case type
  (`not NoneType`, `not Some<T>`, `not Err<int>`, `not Ok<T>`), an `or` of the case types (`Some<T> or NoneType`), a
  guarded `var w`, a case type with a designation (`Some<int> s`), or a final `var w` catch-all, with and without
  `when` guards, sometimes two guarded fallbacks or a guarded payload arm, exhaustive or with `default`/`_`, and in 15
  % of the statements inside a loop whose whole-union sections end with `continue`. Payloads are `int`, `Vec<int>`, a
  class, and a linear `IDisposable` class; the linear payloads use only the union cases, one arm per case and no
  guard, because a guarded, twice-tested, `not` or `or` variant test over a linear union is banned (exactly {VIB006},
  decided N20, §7 item 20), and those forms are matrix rows (§5.2), not generated. Scrutinees are an owned local
  (borrowed in place and used after the switch), a call, or `Move(o)` of a linear local. Each scenario is a one-line
  helper `int UsN(int p)` called three times (`UsN(lo) + UsN(mid) * 10 + UsN(hi) * 100` with lo ≤ 2 < mid ≤ 5 < hi),
  so the run takes the None/Err path, a payload path whose guards fail and one whose guards hold; a dropped or
  mis-tested label changes the printed value. Every shape is valid and accepted (SPEC §3.4; tags `uswitch.*`, §5):
  managed, Stage-0 native and self native must print the same. The anchor is appended to the default anchors, and the
  activation and soundness profiles keep every pair that contains it, so on seed 12 × 1,500 it is in 167 default, 188
  activation and 236 soundness programs (11–16 %); the extended profile pairs it with its five anchors. This changed
  every profile's stream: a (seed, index) pair of release-7 regenerates only with the release-7 tool. It targets the
  N19 class that /self-fix found by hand (§6.20).

## 3. Buckets, minimization and known roots

* Coarse signature: `kind | frontends | ids | normalized first message` (paths, quoted names and numbers are normalized; native link failures are keyed by the undefined symbol).
* **AST delta reduction** (`Reducer.cs`) greedily deletes list children, drops optional children and simplifies nodes, but only keeps a step when the signature is preserved **and** the C# validity class does not get worse. A valid program must stay warning-free, a warning-only program may only lose warning IDs, and an invalid one must stay invalid. Each signature is minimized at most `--max-min` (default 6) times. A program whose tag set contains an already minimized tag set is skipped. From release-7.1 a `run-timeout` bucket, and every execution bucket of a program with a hanging leg, keeps its original program (`min.cs` is `original.cs`, 0 reduction tests): each reduction test would wait out the 20 s budget of the hanging leg.
* Bucket key = signature + minimized tag set. Each bucket directory holds `min.cs`, `min.oracles.txt` (the exact outputs of every oracle), `original.cs`, `original.oracles.txt` and `info.json` (seed, index, plan, mutations, reduction statistics).
* **Categories.** Classification uses only `eng/conformance/known-roots.json`: predicates over kind, frontends, IDs, message and minimized tags. A root may have several predicates, and the first matching predicate wins. Programs with a C10–C21 cut get `cut.*` tags from `CutScan.cs` (the same Roslyn scan as the `cut-anchor` check), so the classifier and the expectation oracles treat them like the generator's own cut tags, and the root `R12-CUTS` (status `fixed`, first in `known-roots.json`) attributes their `banned-accepted`, `banned-wrong-id` and `cut-anchor` buckets, so on a tree with the cuts every such bucket is a `regression:R12-CUTS` that fails the CI gate. The root `R12-F3` (four predicates at the end of the file: three for the Stage-0/self ID-set difference after a borrow-rejected move found on the combined cut source, §6.14, and one for the `spec-wrong-id` of the release-7 field-move expectation, decided as F3-clarified, §7.17) has status `fixed` from release-7, because candidate 6 carries the fix: a hit is `regression:R12-F3` and fails the gate. On the combined cut source, which predates the fix, its 7 soundness buckets show as `regression:R12-F3` (expected there); on candidate 6 they are gone (§6.17). The root `N16` (status `fixed`, two predicates right after `R12-CUTS`, found by release-7, §6.17) attributes the Stage-0/self ID-set difference and the `spec-wrong-id` of a collection expression assigned to a by-value view parameter that is then returned (decided: {VIB003, VIB007} everywhere, SPEC §3.7). The Roslyn-side fix is in candidate 6 pre5, so a hit is `regression:N16` and fails the gate; on earlier trees its buckets show as `regression:N16` (expected there). The roots `N17` (three predicates) and `N18` (three predicates), status `fixed` (candidate 6 pre9), right after N16, attribute the assignment-value cascade (§6.19, §7 item 19): `N17` is the Roslyn side (a moved `!Copy` view keeps its borrow and an overwritten view's old borrows still count: an extra VIB003 on a use of the moved source, a false VIB003 on a view reassigned in a loop, VIB003 instead of VIB001 for a source moved by an assignment value and used at the top of the next iteration), `N18` is self (V4: the value of an assignment to a `!Copy` view does not move the destination, so a later use is accepted or lacks VIB001). Their predicates need a `ViewMove` tag, so they cannot absorb other roots. Both fixes (roslyn-assign-value-cascade, self-fix V4) are in candidate 6 pre9, so both roots are `fixed`. The root `N19` (three predicates, status `fixed` from release-7.2, right after N18) attributes the self switch-label class (§6.20, §7 item 20): vib-self drops every switch-statement label over `Option`/`Result` that is not a union case, `_`/default or a designation-free `var`/type pattern (native output, panic or loop: `output-mismatch`, `run-timeout` or `sanitizer` of a self leg on a program tagged `uswitch.stmt-nonunion-label`), and rejects valid whole-union labels and arms that Stage-0 accepts (`accept-reject` `stage0-accepts` with `self:` IDs, and the `id-mismatch` form, on `uswitch.stmt-nonunion-label` or `uswitch.expr-whole-union-arm`). Self-fix-v (V5, candidate 6 pre9) removed the wrong values; a residual false VIB001 after a switch statement with only whole-union labels plus `default:` remained (§6.20) until self-fix-w (candidate 6 pre13), so N19 is `fixed` from release-7.2. The root `N20` (two predicates, status `fixed` from release-7.2, decided as a ban, §7 item 20) is the gap to the decided {VIB006} for a linear union switch with a guarded variant arm, a variant tested twice or a `not` label: the Analyzer and Stage-0 add VIB001 and self accepts the guarded forms (`accept-reject` `self-accepts`, `stage0:(VIB001,)VIB006`), or self gives {VIB006} and Stage-0 {VIB001, VIB006} (`id-mismatch`), on tag `uswitch.payload-linear`. The generator does not emit those shapes, so the matrix rows `decided.linear-switch-*` (§5.2), which name N20, and the probes reach it. Both fixes (roslyn-linear-switch and self-fix) are in candidate 6 pre13, so N20 is `fixed` and a violation of those rows is a plain violation. A root's `status` gives the category: `new` (vetted by probes, not yet triaged) gives `new:<root>`; `open` (triaged, assigned, not fixed) gives `known:<root>`; `fixed` (kept as a regression detector) gives `regression:<root>`; and `cut` (an approved cut that the tree does not implement yet) gives `known-cut:<root>`. A bucket that no predicate explains is `new`. Lifecycle: `new` → `open` → `fixed`. The Round-11 audit roots O11-1 to O11-13 are listed with status `fixed`: they were open on Round-11 candidate 3 (§6.11), and the combined Round-11 fixes (candidate 4) pass the checkpoint-5 matrix (64/64) and the default and activation CI gates with 0 buckets, so the predicates now work as regression detectors. Their predicates come first because they are narrower than the older ones (most need an activation-profile tag). N15 has no predicate: it was decided as a SPEC change (§7.14). On Round-11 candidate 1 every listed root is `fixed`: the Round-10 roots (O1–O11, A1–A3), both cuts, and the fuzzer-found roots N1–N12, so every predicate now works as a regression detector. `report` re-attributes the buckets of a finished run with the current `known-roots.json`, and shows the category recorded during the run next to it when the two differ. A predicate added after vetting therefore applies to earlier runs too.

## 4. How to run

All modes go through `eng/conformance/run-conformance.sh`. By default it builds `src/vib-self` and the tool in `Release` first; set `CONF_SKIP_BUILD=1` to reuse existing builds. It needs the .NET 11 SDK, `clang-18`, `llvm-readelf-18` and the ASan/LSan runtime, which are the same prerequisites as the test suite. Generated programs, `.ll` files and native binaries are written only under the OS temp directory and are deleted by the tool. Results go to `CONF_OUT`, or to a fresh `mktemp -d` directory whose path is printed.

| Mode | Purpose | Default budget |
|---|---|---|
| `ci` | Fixed-budget gate: exits 1 when buckets match `CONF_FAIL_ON`. `new` = unexplained buckets, roots with status `new`, and regressions of `fixed` roots; `open` = also roots with status `open`; `any` = also `cut` roots; `none` never fails. CI uses `open` (§9). | `CONF_SEED=1`, `CONF_COUNT=300`, `CONF_FAIL_ON=open` |
| `campaign` | Long run; read `summary.json` and `curve.csv`. In-flight reductions stop 5 minutes after the budget. | `CONF_MINUTES=45`, `CONF_SEED=<epoch>` |
| `calibrate` | Fixed-seed recall run against `known-roots.json` | `CONF_SEED=2026`, `CONF_COUNT=1000` |
| `matrix` | One reference probe per banned/cut row, the decided rows (§5.2) and valid controls. Exits 1 when a row is a `violation` or a `probe-error` (release-7; a CI step, §9); the summary goes to stderr. From release-7.1 a decided row may wait on a root of `known-roots.json` (the N20 rows): while that root is not `fixed`, a violation of the row is reported as the root's category (`known:N20`) and fails like a gate bucket of that root under `CONF_FAIL_ON` (default `open`; `new` passes an `open` root); once the root is `fixed` it is a plain violation. An expectation written `=VIB006` is exact (no secondary IDs). The linear-switch rows run like the soundness profile (ASan/LSan at `-O0` and `-O3` for both native compilers). | `CONF_FAIL_ON=open` |
| `selfcheck` | Oracle trust: in-process `vib-self` against process-mode `vib-self` (`check` output and `emit-ll` bytes) | 200 programs |
| `sdk-crosscheck` | Oracle trust: the in-process SDK oracle against a real `dotnet build` with `Vibranium.App.props` + Analyzer (temporary projects in `eng/sdkcheck.tmp`, removed afterwards) | 40 programs |
| `replay`, `run`, `features`, `gen-stats` | Regenerate one program (`--seed S --index I [--exec] [--profile P]`), run all oracles on a file, print the catalog, show generator quality (C# validity and error/warning IDs; with `--vib` also Stage-0 acceptance per anchor and per near-miss tag) | – |

Other options can be passed after the mode, for example `--workers 6`, `--exec-rate 0.5`, `--asan-rate 0.05`, `--self process`, `--max-min 6`, `--profile extended`, `--profile activation`, `--profile soundness`, `--no-exec` or `--roots FILE`. `CONF_PROFILE=extended`, `CONF_PROFILE=activation` or `CONF_PROFILE=soundness` selects that generator profile for `ci`, `campaign` and `calibrate`. The soundness profile ignores `--exec-rate` and `--asan-rate` (every accepted program runs at `-O0` and `-O3` under ASan/LSan), and `replay --exec`/`run --exec` with `--profile soundness` use the same legs. `--no-exec` (release-7, `fuzz`/`ci`
and `matrix`) runs the frontend checks, the expectation oracles and the static IR contract only: the compilers emit IR, but
nothing is compiled by clang or run, in any profile. It is a fast frontend-only mode (no clang, no runs); the seeded-bug kit's `no-exec` protocol uses it (§6.16). `run FILE.cs --tags T1,T2` passes feature tags to the expectation oracles (banned, cut and vib-reject), for example a bucket's minimized tags from `info.json`. `gen-stats --dump DIR` writes every generated program with its plan, tags, C# diagnostics and (with `--vib`) the Stage-0 result, for template work; `gen-stats` also counts the programs that contain a C10–C21 cut (`cut forms (C10–C21)`), which must stay 0. `vib-conformance help` lists all options.

Output layout: `summary.json` holds the options (including the profile), throughput, validity counts, the non-terminating programs skipped (index, plan, reason), warning-only counts by ID, signature hit counts, buckets, the first hit of each known root, tag coverage, catalog tags not yet covered (rows of the extended profile are skipped in a default-profile run), the anchor-pair matrix with the schedule size, and, for `vib-invalid` and `vib-reject` tags, how many programs carried each one and how many of those every frontend accepted. Catalog rows of another profile are not listed as uncovered. `curve.csv` has the columns `seconds,programs,signatures,buckets,new,unattributed`, with one row per new signature and per new bucket (`new` counts categories starting with `new`, `unattributed` counts buckets that no predicate explains). `report` renders it per 5 minutes; this is the convergence metric. Each bucket is a `buckets/NNN-hash/` directory.

**Reproducing a bucket:** `run-conformance.sh replay --seed <seed> --index <index> --exec` regenerates the original program and prints every oracle's output. `run-conformance.sh run buckets/NNN-*/min.cs --exec` re-runs the minimized program.

**Adding a vetted root:** write standalone probes (`run-conformance.sh run probe.cs --exec`, on the target tree and, if available, on an older one), then add a predicate to `known-roots.json`. A predicate gives `kinds`, a regular expression over `frontends`/`ids`/`msg`, and the minimized tags it needs (`tagsAll`, `tagsAny`). Use status `new` for a vetted root, `open` once it is triaged and assigned, and `fixed` once the fix has landed. Keep the predicate after the fix: it then reports `regression:<root>`, which fails the CI gate. The first matching predicate wins, so put narrow predicates first. `run-conformance.sh report --out DIR` re-renders an old run with the new predicates.

## 5. Feature matrix (v0.2 scope)

The catalog below is printed by `vib-conformance features --markdown`. *Supported* rows must be accepted by
every frontend with equal execution, *near-miss* rows are C#-invalid shapes that every frontend must reject,
*vib-invalid* rows are C#-valid shapes that SPEC rejects (the extended profile's near-misses; the frontends must
agree on the ID set, and the expected ID is in the description), *vib-reject* rows are the activation profile's
C#-valid shapes that SPEC rejects with the ID in the Expected column (expectation oracle, §1), and *banned*/*cut* rows
must be rejected with the documented ID (decided rule in §1 and §7). *scenario* rows are the soundness profile's
coordinates (no expectation: every accepted program must run sanitizer-clean with the managed output, §1), and
*retired* rows are tags of shapes that the generator no longer emits since the C10–C20 cuts (kept so that older corpus
buckets keep their meaning; they are never reported as uncovered). The C10–C21 rows are tagged by the tool's own scan
(`CutScan.cs`), not by a generator production. Rows whose generator ends in `(extended)`, `(activation)` or
`(soundness)` belong to that profile. Rows without such a suffix belong to every profile (from release-7.1 this includes
`UnionSwitch`'s `uswitch.*` rows).

#### supported

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `local.decl` | locals | typed local declaration with initializer | §1 | accept; managed = Stage-0 native = self native | * |
| `local.var` | locals | `var` local | §1 | accept; managed = Stage-0 native = self native | * |
| `local.uninit` | locals | local declared without initializer, assigned later (definite assignment) | §3.7(2) | accept; managed = Stage-0 native = self native | SwitchStmt/Panic/StructInit |
| `assign.underscore` | locals | `_ = e` discard | §1 | accept; managed = Stage-0 native = self native | OutArg/Underscore |
| `local.ref` | locals | ref / ref readonly local | §3, §1 ref reassignment rule | accept; managed = Stage-0 native = self native | RefParams |
| `out.var` | out | `out var x` | §1, §3.5 guards | accept; managed = Stage-0 native = self native | OutArg |
| `out.typed` | out | `out int x` | §1 | accept; managed = Stage-0 native = self native | OutArg |
| `out.underscore` | out | `out _` discard | §3.7 | accept; managed = Stage-0 native = self native | OutArg/Underscore |
| `out.var-discard` | out | `out var _` | §1 | accept; managed = Stage-0 native = self native | OutArg |
| `out.typed-discard` | out | `out int _` | §1 | accept; managed = Stage-0 native = self native | OutArg |
| `out.existing-local` | out | `out x` to an existing local | §1 | accept; managed = Stage-0 native = self native | OutArg |
| `out.field` | out | `out s.A` to a field of a fully initialized plain struct | §3.1, §3.7 | accept; managed = Stage-0 native = self native | OutArg |
| `out.other` | out | other out targets | §1 | accept; managed = Stage-0 native = self native | OutArg |
| `bcl.tryparse` | out | `int.TryParse`/`long.TryParse` BCL alias | §3.5 BCL aliases | accept; managed = Stage-0 native = self native | OutArg |
| `const.local` | const | local `const` (top-level, function, used from static local functions) | §1 static fields rule | accept; managed = Stage-0 native = self native | Const |
| `const.class` | const | class `const` field used by simple name (incl. inside static local functions) | §1 | accept; managed = Stage-0 native = self native | Const |
| `type.enum` | const | user enum with int underlying type | §2 | accept; managed = Stage-0 native = self native | Const/Collection |
| `pattern.named-const` | const | named constant (const/enum member) as a case pattern | §3.4 | accept; managed = Stage-0 native = self native | Const |
| `expr.qualified-member` | const | qualified `E.A` / `C.K` access | §2 | accept; managed = Stage-0 native = self native | * |
| `switch.stmt` | switch | switch statement | §3.4, §3.7(1) | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.expr` | switch | switch expression | §3.4, CS8509 promoted | accept; managed = Stage-0 native = self native | SwitchExpr |
| `switch.sel.int` | switch | int/long selector | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.sel.bool` | switch | bool selector (true/false cases) | §3.7 | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.sel.unsigned` | switch | unsigned selector | §3.7(1) | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.sel.char` | switch | char selector | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.sel.enum` | switch | enum selector | §2, §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/SwitchExpr |
| `switch.sel.option` | switch | Option<int> selector with Some/NoneType patterns | §3.4, §3.7 | accept; managed = Stage-0 native = self native | SwitchStmt/OptionResult |
| `switch.sel.result` | switch | Result<int,int> selector with Ok/Err patterns | §3.4, §3.7 | accept; managed = Stage-0 native = self native | SwitchStmt/OptionResult |
| `switch.sel.float` | switch | float/double selector (never exhaustive: NaN) | §5.3 | accept; managed = Stage-0 native = self native | SwitchStmt/SwitchExpr |
| `switch.sel-unparenthesized` | switch | operator expression before `switch` without parentheses (`a % b switch { … }` parses as `a % (b switch { … })`; `-x switch` as `(-x) switch`) | §1 (C# 15 grammar subset) | accept; managed = Stage-0 native = self native | SwitchExpr |
| `switch.default` | switch | `default:` section | §3.7 | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.no-default` | switch | switch statement without default (exhaustive or not) | §3.7(1)(2) | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.guard` | switch | non-constant `when` guard | §3.7(2) | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.nested` | switch | switch nested in a switch section | §3.7 | accept; managed = Stage-0 native = self native | SwitchStmt |
| `switch.expr-no-discard` | switch | switch expression without discard arm | CS8509 promoted, §1 | accept; managed = Stage-0 native = self native | SwitchExpr |
| `pattern.const` | patterns | constant pattern | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt |
| `pattern.combinator` | patterns | `or`/`not` patterns (`and` is cut C20) | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/PatternVar |
| `pattern.decl` | patterns | declaration pattern `int n` | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/PatternVar |
| `pattern.var` | patterns | var pattern | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/PatternVar |
| `pattern.discard` | patterns | discard pattern `_` | §3.4 | accept; managed = Stage-0 native = self native | SwitchExpr |
| `pattern.type` | patterns | type pattern | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/SwitchExpr |
| `pattern.type-discard` | patterns | `int _` designation | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/SwitchExpr |
| `pattern.union` | patterns | Some<T>/NoneType/Ok<T>/Err<E> union patterns | §3.4 | accept; managed = Stage-0 native = self native | SwitchStmt/OptionResult |
| `pattern.is` | patterns | `e is pattern` expression (pattern variables) | §3.4 | accept; managed = Stage-0 native = self native | PatternVar |
| `localfunc.static` | functions | static local function (forward call, recursion) | §1 | accept; managed = Stage-0 native = self native | LocalFunc |
| `localfunc.nested` | functions | static local function nested in another function body | §1 | accept; managed = Stage-0 native = self native | LocalFunc/Const |
| `localfunc.in-class-method` | functions | static local function inside a class method | §1 | accept; managed = Stage-0 native = self native | Const/LocalFunc |
| `method.static` | functions | static method of a class | §1 | accept; managed = Stage-0 native = self native | * |
| `method.instance` | functions | instance method ([Mut] or shared receiver) | §3.1 | accept; managed = Stage-0 native = self native | ClassInstance |
| `func.expr-body` | functions | expression-bodied function | §1 | accept; managed = Stage-0 native = self native | * |
| `func.returns-span` | functions | function returning ReadOnlySpan<T> | §3.3 lifetimes, §3.7 source contexts | accept; managed = Stage-0 native = self native | Collection |
| `param.ref` | params | `ref` parameter | §3, §3.3 | accept; managed = Stage-0 native = self native | RefParams |
| `param.in` | params | `in` parameter | §3 | accept; managed = Stage-0 native = self native | RefParams/Const |
| `param.out` | params | `out` parameter | §3 | accept; managed = Stage-0 native = self native | RefParams/OutArg |
| `param.scoped` | params | `scoped` span parameter | §3.3 | accept; managed = Stage-0 native = self native | Collection |
| `arg.ref` | params | `ref` argument | §3.3 | accept; managed = Stage-0 native = self native | RefParams |
| `arg.in` | params | explicit `in` argument | §3 | accept; managed = Stage-0 native = self native | RefParams/Const |
| `arg.out` | params | `out` argument | §3 | accept; managed = Stage-0 native = self native | OutArg/RefParams |
| `expr.conditional-arg` | params | conditional expression as an argument | §1 | accept; managed = Stage-0 native = self native | CondArg |
| `type.struct` | types | user struct | §2 | accept; managed = Stage-0 native = self native | StructInit |
| `struct.local` | types | struct local | §2 | accept; managed = Stage-0 native = self native | StructInit |
| `struct.fieldwise-init` | types | plain-value struct initialized field by field | §3.7(2) | accept; managed = Stage-0 native = self native | StructInit |
| `struct.out-fieldwise` | types | plain-value struct out parameter initialized field by field | §3.7(2) | accept; managed = Stage-0 native = self native | StructInit |
| `struct.owned-field` | types | struct with an owned field (Vec<int> or class) | §2 | accept; managed = Stage-0 native = self native | StructInit |
| `struct.new-default` | types | `new S()` of a struct | §2 zero-safety | accept; managed = Stage-0 native = self native | StructInit |
| `struct.ctor` | types | struct primary constructor `new SO(a, b, owned)` (the fully initialized form of an owned struct) | §2 | accept; managed = Stage-0 native = self native | StructInit/RefParams/OutArg |
| `type.class` | types | sealed class (primary ctor, fields) | §2 | accept; managed = Stage-0 native = self native | ClassInstance/StructInit |
| `class.primary-ctor` | types | primary constructor | §2 rule 3 | accept; managed = Stage-0 native = self native | ClassInstance |
| `type.field` | types | instance field | §2 | accept; managed = Stage-0 native = self native | * |
| `type.resource` | types | `class R : IDisposable` | §2 linear policy | accept; managed = Stage-0 native = self native | Using |
| `using.var` | resources | `using var r = ...` (LIFO disposal) | §2 exact-once disposal | accept; managed = Stage-0 native = self native | Using |
| `using.block` | resources | `using (var r = ...) { }` | §2 | accept; managed = Stage-0 native = self native | Using |
| `coll.rospan` | collections | ReadOnlySpan<T> collection expression | §2, §3.2 | accept; managed = Stage-0 native = self native | Collection |
| `coll.span` | collections | Span<T> collection expression | §3.2 | accept; managed = Stage-0 native = self native | Collection/Loop |
| `coll.vec` | collections | Vec<int> collection expression | §3.5 | accept; managed = Stage-0 native = self native | Collection/Loop |
| `coll.empty` | collections | empty collection `[]` | §3.5 | accept; managed = Stage-0 native = self native | Collection |
| `coll.local-init` | collections | collection expression as a local initializer | §2 | accept; managed = Stage-0 native = self native | Collection |
| `coll.arg` | collections | collection expression as an argument | §2 | accept; managed = Stage-0 native = self native | Collection |
| `coll.conditional-locals` | collections | a conditional choosing between two collection locals (the C16-compliant form: collections assigned to locals first) | §2, §3.7 | accept; managed = Stage-0 native = self native | Collection |
| `coll.returned` | collections | collection expression returned as ReadOnlySpan<T> after an unconditional Panic (runtime-unreachable, accepted; a reachable return is `banned.collection-return-span`) | §3.3, §3.7 | accept; managed = Stage-0 native = self native | Collection |
| `coll.elem-char` | collections | char elements | §2 | accept; managed = Stage-0 native = self native | Collection |
| `coll.elem-byte` | collections | byte elements | §2 | accept; managed = Stage-0 native = self native | Collection |
| `coll.elem-long` | collections | long elements | §2 | accept; managed = Stage-0 native = self native | Collection |
| `coll.elem-enum` | collections | enum elements | §2 | accept; managed = Stage-0 native = self native | Collection |
| `expr.index` | collections | indexing (`s[i]`, `s[^1]`) | §3.2 | accept; managed = Stage-0 native = self native | Collection |
| `assign.index` | collections | element write `v[i] = x` | §3.2 | accept; managed = Stage-0 native = self native | Collection |
| `loop.for` | loops | for loop | §1 | accept; managed = Stage-0 native = self native | Loop |
| `loop.while` | loops | while loop | §1 | accept; managed = Stage-0 native = self native | Loop/PatternVar |
| `loop.do` | loops | do-while loop | §1 | accept; managed = Stage-0 native = self native | Loop |
| `loop.foreach` | loops | foreach (value) | §3.2(4) | accept; managed = Stage-0 native = self native | Loop/Collection |
| `loop.foreach-ref` | loops | foreach (ref var x in v) | §3.2(4) | accept; managed = Stage-0 native = self native | Loop |
| `loop.foreach-vec` | loops | foreach over Vec<int> | §3.2(4) | accept; managed = Stage-0 native = self native | Loop |
| `loop.continue` | loops | continue (incl. from switch sections) | §1 | accept; managed = Stage-0 native = self native | Loop/SwitchStmt |
| `union.ctor` | unions | Some/None/Ok/Err | §3.5 | accept; managed = Stage-0 native = self native | OptionResult |
| `union.guard` | unions | `if ([!]x.IsNone(out var v))` / `IsErr(out var ok, out var err)` | §3.5 guards | accept; managed = Stage-0 native = self native | OptionResult |
| `union.consume` | unions | Unwrap/Expect/UnwrapOr/UnwrapOrDefault/IsSome | §3.5 | accept; managed = Stage-0 native = self native | OptionResult |
| `panic.call` | panic | `VibraniumRuntime.Panic` (source continuation, runtime divergence, exit 101) | §3.7(1)(3), §5.4 | accept; managed = Stage-0 native = self native | Panic |
| `arith.int` | arith | int arithmetic (wrapping) | §5.3 | accept; managed = Stage-0 native = self native | Arith/* |
| `arith.long` | arith | long arithmetic | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.unsigned` | arith | unsigned arithmetic | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.float` | arith | float arithmetic | §5.3 IEEE | accept; managed = Stage-0 native = self native | Arith |
| `arith.double` | arith | double arithmetic | §5.3 IEEE | accept; managed = Stage-0 native = self native | Arith |
| `arith.div-int` | arith | int division (zero / MIN/-1 panics) | §5.3, §5.4 | accept; managed = Stage-0 native = self native | Arith |
| `arith.div-long` | arith | long division | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.div-unsigned` | arith | unsigned division | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.div-float` | arith | float division | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.div-double` | arith | double division | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.rem-int` | arith | int `%` | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.rem-long` | arith | long `%` | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.rem-unsigned` | arith | unsigned `%` | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.rem-float` | arith | float `%` (libc-only link contract) | §5.3, README libc-only | accept; managed = Stage-0 native = self native | Arith |
| `arith.rem-double` | arith | double `%` (libc-only link contract) | §5.3, README libc-only | accept; managed = Stage-0 native = self native | Arith |
| `arith.shift` | arith | shifts (count masking) | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `arith.bitwise` | arith | & \| ^ | §5.3 | accept; managed = Stage-0 native = self native | * |
| `arith.float-to-int-cast` | arith | saturating float->int cast | §5.3 | accept; managed = Stage-0 native = self native | Arith |
| `expr.cast` | arith | numeric/enum casts | §5.3 | accept; managed = Stage-0 native = self native | * |
| `expr.compare` | expr | comparisons | §1 | accept; managed = Stage-0 native = self native | * |
| `expr.logical` | expr | && / \|\| short-circuit | §5.2 drop flags | accept; managed = Stage-0 native = self native | * |
| `expr.conditional` | expr | conditional `c ? a : b` | §1 | accept; managed = Stage-0 native = self native | * |
| `expr.u8-literal` | expr | "..."u8 literal | §2 | accept; managed = Stage-0 native = self native | Collection |
| `assign.simple` | expr | simple assignment | §5.2 | accept; managed = Stage-0 native = self native | * |
| `assign.compound` | expr | compound assignment | §5.2 | accept; managed = Stage-0 native = self native | * |
| `assign.field` | expr | field assignment | §3.1 | accept; managed = Stage-0 native = self native | StructInit/ClassInstance |
| `stmt.if` | statements | if | §1 | accept; managed = Stage-0 native = self native | * |
| `stmt.if-else` | statements | if/else | §1 | accept; managed = Stage-0 native = self native | * |
| `stmt.nested-block` | statements | nested block scopes | §3.7 | accept; managed = Stage-0 native = self native | * |
| `span.from-coll` | span | `Span<int> s = [..]` from a collection expression | §3.7 storage contract | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.from-vec` | span | `Span<int>` borrowed from a `Vec<int>` (`AsSpan()`, `Slice`, `v[..]`) | §3.5 Vec table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.vec-range` | span | `v[a..b]` on a `Vec<T>` (implicit Range pattern over `Count` + `Slice`) | §3.5 Vec table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.vec-slice-call` | span | `v.AsSpan()` / `v.Slice(start, length)` | §3.5 Vec table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.vec-after-borrow` | span | the owning `Vec` read after its span borrow ended (NLL) | §3.2, §3.4 | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.range` | span | `s[a..b]`, `s[a..]` slicing | §3.5 Span table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.range-from-end` | span | `s[..^k]`, `s[a..^k]` ranges from the end | §3.5 Span table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.index-from-end` | span | `s[^k]` and `r[^1]` reads | §3.2(2) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.slice-local` | span | a `Span<int>` slice local kept live across nested code | §3.2 | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.slice-call` | span | `s.Slice(a)` / `s.Slice(a, n)` | §3.5 Span table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.to-rospan` | span | implicit `Span<int>` → `ReadOnlySpan<int>` shared reborrow | §3.2(1) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.write-through-slice` | span | write through a slice: `t[0] = e` | §3.2(2) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.write` | span | `s[i] = e` / `s[i] += e` | §3.2(2) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.read` | span | `s[i]` value read | §3.2(2) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.length` | span | `s.Length` / `s.IsEmpty` | §3.5 Span table | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.foreach` | span | `foreach (var e in slice)` | §3.2 foreach (decided N8 rule) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.foreach-ref` | span | `foreach (ref var e in s) e += k` | §3.2 foreach | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.arg-rospan` | span | a span or slice passed to a `ReadOnlySpan<int>` parameter | §3.2(1) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.arg-span` | span | a span or slice passed to a `Span<int>` parameter, then the span is reused | §3.2(1) reborrow | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.write-after-shared` | span | write to a span after its `ReadOnlySpan` copy is dead | §3.2, NLL | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.oob-panic` | span | out-of-range slice: panic `slice out of bounds`, exit 101 everywhere | §3.5 panics | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `span.index-oob-panic` | span | out-of-range index write: panic, exit 101 everywhere | §3.2(2) | accept; managed = Stage-0 native = self native | SpanSlice (extended) |
| `optslot.local` | option slots | `Option<Cell>` local holding a class | §2 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optslot.take` | option slots | `o.Take()` | §3.5 Option table | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optslot.replace` | option slots | `o.Replace(Some(..))` | §3.5 Option table | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optslot.issome` | option slots | `o.IsSome` | §3.5 Option table | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optref.asref-pattern` | option slots | `o.AsRef() is Some<Cell>(var c)` shared read | §3.1, §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optref.param` | option slots | `OptionRef<Cell>` parameter | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optref.chain` | option slots | nested `c.Next.AsRef()` pattern | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optref.switch` | option slots | `o.AsRef() switch { Some<Cell>(var c) => .., _ => .. }` | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.local` | option slots | `OptionMut<Cell> m = o.AsMut();` | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.set` | option slots | `m.Set(Some(..))` | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.replace` | option slots | `m.Replace(..)` | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.take` | option slots | `m.Take()` | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.pattern-write` | option slots | `o.AsMut() is Some<Cell>(var c)` then `c.V += k` | §3.1, §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.param` | option slots | `OptionMut<Cell>` parameter written through | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `optmut.chain-set` | option slots | `c.Next = Some(..)` through an `AsMut()` binding | §3.5 | accept; managed = Stage-0 native = self native | OptionSlot (extended) |
| `mut.local` | [Mut] receivers | class with `[Mut]` methods held in a local | §3.1 receiver conventions | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.local-call` | [Mut] receivers | `a.Add(e)` on a `[Mut]` method | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.two-phase-read` | [Mut] receivers | `a.Add(a.Total)` two-phase receiver borrow | §3.2 two-phase | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.calls-mut` | [Mut] receivers | a `[Mut]` method calling another `[Mut]` method on `this` | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.drain` | [Mut] receivers | `[Mut]` method clearing a `Vec` field | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.shared-call` | [Mut] receivers | unmarked (shared `this`) method iterating a `Vec` field | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.ref-param` | [Mut] receivers | `[Mut]` call through a `ref` parameter | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.in-param-shared` | [Mut] receivers | shared calls through an `in` parameter | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.field-foreach` | [Mut] receivers | `foreach (var e in a.Items)` | §3.2 foreach | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.struct-local` | [Mut] receivers | struct local with mutating and `readonly` methods | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.struct-method` | [Mut] receivers | non-`readonly` struct method (exclusive `this`) | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.struct-readonly-method` | [Mut] receivers | `readonly` struct method (shared `this`) | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `mut.nested-field` | [Mut] receivers | `[Mut]` method forwarding to a `[Mut]` method of a class field | §3.1 | accept; managed = Stage-0 native = self native | MutReceiver (extended) |
| `utf8.string-lines` | UTF-8 views | `foreach (var l in "..".Lines())` | §2 closed foreach views | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.span-local` | UTF-8 views | `ReadOnlySpan<byte> u = ".."u8;` | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.span-lines` | UTF-8 views | `foreach (var l in u.Lines())` | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.stringbuf` | UTF-8 views | `StringBuf` built with `Append`/`AppendLine` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.buf-lines` | UTF-8 views | `foreach (var l in sb.Lines())` | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.vec-bytes` | UTF-8 views | `Vec<byte>` built from byte literals | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.vec-lines` | UTF-8 views | `foreach (var l in v.AsReadOnlySpan().Lines())` | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.foreach` | UTF-8 views | line loop body (with nested code in the body) | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.line-ops` | UTF-8 views | `StartsWith`, `IndexOf`, `Trim`, `SequenceEqual` on a line | §3.6 ByteSpanExtensions | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.line-tryparse` | UTF-8 views | `int.TryParse(line.Trim(), out var v)` | §3.5 BCL aliases | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `utf8.slice-escapes-loop` | UTF-8 views | a line saved into a `scoped` local and used after the loop (owner unmutated) | §2 | accept; managed = Stage-0 native = self native | Utf8View (extended) |
| `std.intmap` | Std | `IntMap<int>` `Set`/`Get`/`Contains`/`Count` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.intmap-rehash` | Std | `IntMap` growth past its capacity | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.intmap-remove` | Std | `IntMap.Remove` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.strmap` | Std | `StrMap<int>` with `string` and `u8` keys | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf` | Std | `StringBuf` `Append`/`AppendLine`/`AsUtf8`/`Length` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-int` | Std | `StringBuf.Append(int)` incl. `int.MinValue` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-long` | Std | `StringBuf.AppendLong` incl. `long.MinValue` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-bool` | Std | `StringBuf.Append(bool)` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-hex` | Std | `StringBuf.AppendHex(bits, minDigits)` | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-rune` | Std | `StringBuf.AppendRune` across UTF-8 length boundaries | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-rune-panic` | Std | `AppendRune` of a surrogate or out-of-range scalar: panic, exit 101 | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-op` | Std | `sb += ".."` (`[Mut] void operator +=`) | §3.1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.stringbuf-truncate` | Std | `sb.Truncate(sb.Length - 1)` two-phase | §3.6 Std T1 | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.bytes` | Std | `ReadOnlySpan<byte>` text local | §3.6 ByteSpanExtensions | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.bytes-trim` | Std | `Trim`/`TrimStart`/`TrimEnd` | §3.6 ByteSpanExtensions | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.bytes-indexof` | Std | `IndexOf`/`LastIndexOf` of a byte | §3.6 ByteSpanExtensions | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.bytes-prefix` | Std | `StartsWith(string)` | §3.6 ByteSpanExtensions | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.bytes-contains` | Std | `Contains(string)` | §3.6 ByteSpanExtensions | accept; managed = Stage-0 native = self native | StdText (extended) |
| `std.tryparse` | Std | `int`/`long.TryParse` over `string` and `u8` inputs incl. edge values | §3.5 BCL aliases | accept; managed = Stage-0 native = self native | StdText (extended) |
| `ca.owner-vec` | call-activation | owner is a `Vec<int>` local | §3.2, §3.4 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.owner-class` | call-activation | owner is a sealed class instance | §3.2, §3.4 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver (activation) |
| `ca.owner-holder` | call-activation | owner is a class holding a class field `N` and a `Vec<int>` field `Items` (projections) | §3.2, §3.4 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.owner-option` | call-activation | owner is an `Option<T>` of a class | §3.2, §3.4 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.owner-linear` | call-activation | owner is an `IDisposable` class (linear) | §2, §3.4 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.owner-struct` | call-activation | owner is a readonly struct holding a `Vec<int>` | §3.2, §3.4 | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.rel-same` | call-activation | the loaned and the moved place are the same local | §3.2 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.rel-ext` | call-activation | the loaned place extends the moved place (`x.N` loaned, `x` moved) | §3.2 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.rel-prefix` | call-activation | the moved place extends the loaned place (`x` loaned, `x.N` moved) | §3.2 | accept; managed = Stage-0 native = self native | CaCall/CaAssign (activation) |
| `ca.rel-unrelated` | call-activation | loaned and moved places are distinct owners | §3.2 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.loan-in` | call-activation | `in` argument | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.loan-in-implicit` | call-activation | an lvalue passed to an `in` parameter without a modifier (by reference, like `in`; replaces the `ref readonly` parameter forms cut by C10) | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.named-in-position` | call-activation | the call names its arguments in parameter order (`Eat(a: in x, b: x)`; out-of-order names are cut C13) | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.loan-ref` | call-activation | `ref` argument | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.loan-out` | call-activation | `out` argument | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.loan-view` | call-activation | view argument: `AsSpan()`, `AsReadOnlySpan()`, a range slice, `AsRef()`/`AsMut()` of an Option | §3.2, §3.3 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.loan-ref-proj` | call-activation | `in`/`ref` argument of a field projection (`x.N`, `x.Items`) | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.loan-shared-recv` | call-activation | shared receiver of an instance method or indexer | §3.2 two-phase receiver | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.loan-mut-recv` | call-activation | `[Mut]` receiver (method or `[Mut] operator +=`) | §3.2 two-phase receiver | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.loan-this-in` | call-activation | `this in` extension receiver | §3.2 | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.loan-this-ref` | call-activation | `this ref` extension receiver | §3.2 | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.loan-target` | call-activation | the assignment target is the loan (indexer set, compound index, ref local, field) | §3.2 | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `ca.move-bare` | call-activation | move by passing the owner by value | §3.4 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.move-explicit` | call-activation | move through `Move(x)` | §3.4 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.move-consume` | call-activation | move through a consuming call (`o.Unwrap()`) | §3.4 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.form-local` | call-activation | the call is to a static local function | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.form-static` | call-activation | the call is to a static method | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.form-instance` | call-activation | the call is to an instance method | §3.2 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver (activation) |
| `ca.form-ctor` | call-activation | the call is a constructor (`new W(..)`) | §3.2 | accept; managed = Stage-0 native = self native | CaCall (activation) |
| `ca.form-extension` | call-activation | the call is an extension method with a `this in`/`this ref` receiver | §3.2 | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.form-indexer-get` | call-activation | indexer read whose index expression moves the receiver (`x[Idx(x)]`) | §3.2 | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.form-indexer-set` | call-activation | indexer write whose value moves the target owner (`x[0] = Len(x)`) | §3.2 | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `ca.form-compound-index` | call-activation | compound indexer assignment (`x[0] += Len(x)`) | §3.2 | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `ca.form-operator` | call-activation | `[Mut] operator +=` whose operand moves the receiver | §3.2 | accept; managed = Stage-0 native = self native | CaReceiver (activation) |
| `ca.form-assign-ref` | call-activation | assignment through a `ref` local (`ref var t = ref x; t = Move(..)`) | §3.2 | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `ca.form-assign-field` | call-activation | field assignment whose value moves the owner (`x.N = Take(x)`, `x.N.V = PeekH(x)`) | §3.2 | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `ca.control-unrelated` | call-activation | control: the same shapes on distinct owners (accepted; executed) | §3.2 | accept; managed = Stage-0 native = self native | CaCall/CaReceiver/CaAssign (activation) |
| `ca.control-shared-read-reserved` | call-activation | control: shared reads of the target owner in the value (`x[0] = x.Count + x[1]`) | §3.2 two-phase | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `ca.control-own-slot` | call-activation | control: `x = Move(x)` (own slot, legal) | §3.4 | accept; managed = Stage-0 native = self native | CaAssign (activation) |
| `view.lifetimeof` | views | `[return: LifetimeOf("x")]` that returns a view of `x` (or a slice of it) | §3.3 LifetimeOf | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.lifetimeof-two` | views | `[return: LifetimeOf("x", "y")]` | §3.3 LifetimeOf | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.call-site-other-mutated` | views | the call site mutates the input not named by `LifetimeOf` while the result lives | §3.3 | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.elision-this` | views | instance method returning a view of `this` with a view parameter (elision ties the result to `this`) | §3.3 elision | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.elision-single` | views | a single borrowed input: elision ties the result to it | §3.3 elision | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.reborrow-parent-after` | views | `First(s)` returns a reborrow of span `s`; `s` is used after the reborrow's last use | §3.2 reborrow | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.param-reslice-return` | views | a by-value `ReadOnlySpan<int>` parameter re-sliced from itself (`x = x[1..]`) and returned (control for the two rows below) | §3.3 elision | accept; managed = Stage-0 native = self native | ViewReturn (activation) |
| `view.move-use-dest` | views | a `!Copy` view moved by a plain move (`Span<int> t = s;`); only the destination is used, then the owner (control) | §3.2 item 1 (`Span<T>` is `!Copy`) | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.move-owner-between` | views | with `view.move-use-source`: the owner is read or written after the destination's last use and before the source's use | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.move-optionmut` | views | the moved view (or assignment-value destination) is an `OptionMut<T>` instead of a `Span<int>` | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.move-reassign-source` | views | a `Span<int>` moved by a plain move, the destination used, the source reassigned from another owner, then the first owner and the source used (control: the reassignment ends the source's old borrows) | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.loop-reassign-move` | views | a `Span<int>` declared before a loop from another owner and reassigned in it by a plain move (`t = s;`), used after the assignment (and maybe after the loop): accepted; the overwritten value's borrows end | §3.2 item 1 (decision A+) | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.loop-reassign-call` | views | the same with a call result (`t = Id(s);`) | §3.2 item 1 (decision A+) | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.loop-assign-value-receiver` | views | in a loop, the value of an assignment to a view declared before it used as a receiver (`n += (s = b.AsSpan()).Length`, `[0]`, `[1..].Length`; maybe reassigned and used again): accepted | §3.2 item 1 (decision A+) | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.loop-assign-value-argument` | views | in a loop, the value of an assignment to a view used as an argument (`n += Len(s = b.AsSpan())`): accepted | §3.2 item 1 (decision A+) | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.rebind-old-owner-mutated` | views | a live view rebound to another owner (`t = b.AsSpan()`, `t = s`, `t = Id(..)`, also `ReadOnlySpan<int>`), then its old owner mutated (`a.Add(7)`) while the view is still used: accepted, the rebinding ends the old borrows | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.loop-rebind-old-owner-mutated` | views | the same in a loop: the view is used at the top of each iteration, rebound, then its old owner mutated: accepted | §3.2 item 1 (decision A+) | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.assign-value-no-later-use` | views | the same assignment values without a later use of the view (control) | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.assign-value-copy-view` | views | the same with a `ReadOnlySpan<int>` destination, which is `Copy`, then the view used again (control) | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `view.assign-value-chain` | views | with an assignment-value row: the chain form `Span<int> t = s = b.AsSpan();` | §3.2 item 1 | accept; managed = Stage-0 native = self native | ViewMove (activation) |
| `uswitch.stmt` | union-switch | a `switch` statement over `Option<T>` or `Result<T, int>` in a helper `int UsN(int p)` that is called on the None/Err path, on a payload path whose guards fail and on one whose guards hold | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.expr` | union-switch | the same arms as a switch expression (`n += s switch { .. };`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.option` | union-switch | the scrutinee is an `Option<T>` (cases `Some<T>`, `NoneType`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.result` | union-switch | the scrutinee is a `Result<T, int>` (cases `Ok<T>`, `Err<int>`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.payload-copy` | union-switch | `Copy` payload (`int`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.payload-owner` | union-switch | owner payload `Vec<int>` (bound as a shared borrow from a place, owned from a call) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.payload-class` | union-switch | owner payload: a class instance | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.payload-linear` | union-switch | linear payload (an `IDisposable` class, consumed by `using var` in a helper): union cases only, one arm per case and no guard, from a call or `Move(o)` | §2 linear policy, §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.place` | union-switch | the scrutinee is an owned local (borrowed in place, then used after the switch) | §3.4 place scrutinees | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.rvalue` | union-switch | the scrutinee is a call (consumed) | §3.4 rvalue scrutinees | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.move-place` | union-switch | the scrutinee is `Move(o)` of a linear local | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.not-case` | union-switch | a `not` label of a case type (`not NoneType`, `not Some<T>`, `not Err<int>`, `not Ok<T>`), guarded or not | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.or-cases` | union-switch | an `or` of the case types with a guard (`Some<T> or NoneType when ..`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.var` | union-switch | a guarded `var w` designation of the whole union (`Copy` payloads) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.var-final` | union-switch | a final `var w` catch-all after a guarded payload arm (`Copy` payloads, or an owner from a call) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.type-designation` | union-switch | a case type with a designation and a guard (`Some<int> s when ..`, `Ok<int> s`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.guard` | union-switch | the first whole-union label has a `when` guard | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.two-fallbacks` | union-switch | two guarded whole-union labels before the union cases | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.case-guard` | union-switch | a guarded payload arm before the unguarded one | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.exhaustive` | union-switch | no default: the labels cover both cases (e.g. `case not Some<T>:` then `case Some<T>(var v):`) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.default` | union-switch | a `default:` section or `_` arm takes the rest | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.loop` | union-switch | the switch statement is in a loop and a whole-union section ends with `continue` | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.nonunion-label` | union-switch | at least one label is not a union case (`Some/None/Ok/Err(..)`), a discard or a designation-free `var`/type pattern | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.stmt-nonunion-label` | union-switch | a switch statement with such a label (root N19: self dropped these labels) | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `uswitch.expr-whole-union-arm` | union-switch | a switch expression with such an arm | §3.4 | accept; managed = Stage-0 native = self native | UnionSwitch |
| `cond.temp-arm-arg` | temporaries | a temporary `Vec` viewed in one arm of a conditional passed as an argument | §3.7 temporaries | accept; managed = Stage-0 native = self native | CondTemp (activation) |
| `cond.temp-both-arms` | temporaries | temporaries viewed in both arms of a conditional argument | §3.7 temporaries | accept; managed = Stage-0 native = self native | CondTemp (activation) |
| `arith.float-int-const` | arith | `unchecked((long)c)` / `(ulong)`, `(int)`, `(uint)` of an in-range float constant (replaces the `nint`/`nuint` forms cut by C17) | §5 numeric lowering | accept; managed = Stage-0 native = self native | FloatNint (activation) |
| `arith.float-int-const-oob` | arith | the same conversions of an out-of-range float constant (saturating) | §5 numeric lowering | accept; managed = Stage-0 native = self native | FloatNint (activation) |
| `arith.float-int-const-special` | arith | the same conversions of an infinity or NaN constant | §5 numeric lowering | accept; managed = Stage-0 native = self native | FloatNint (activation) |
| `arith.float-int-runtime` | arith | the same conversions of a runtime `double`/`float` local | §5 numeric lowering | accept; managed = Stage-0 native = self native | FloatNint (activation) |
| `arith.int-bounds` | arith | `long.MinValue`/`MaxValue`, `ulong.MaxValue`, `int.MinValue`/`MaxValue`, `uint.MaxValue` | §5 numeric lowering | accept; managed = Stage-0 native = self native | FloatNint (activation) |
| `std.append-nonseekable` | Std | `File.AppendAllText` / `File.OpenAppend(..).WriteAll` to `/dev/stdout` or `/dev/stderr` (a pipe in the harness) | §3.6 File | accept; managed = Stage-0 native = self native | StdAppend (activation) |
| `gen.auto-generated-header` | generated | `// <auto-generated/>` first line: the Analyzer must still report | §6 analyzer parity | accept; managed = Stage-0 native = self native | * (activation) |
| `gen.file-name-g-cs` | generated | the program is compiled as `Program.g.cs`: the Analyzer must still report | §6 analyzer parity | accept; managed = Stage-0 native = self native | * (activation) |
| `pp.region` | declarations | control for C5: `#region`/`#endregion` stay valid | decided C5 scope | accept; managed = Stage-0 native = self native | DeclCuts (activation) |
| `type.nested-nongeneric` | declarations | control for C8: a class nested in a non-generic class | decided C8 scope | accept; managed = Stage-0 native = self native | DeclCuts (activation) |
| `type.multi-ctor` | declarations | control for C9: two constructors without chaining | decided C9 scope | accept; managed = Stage-0 native = self native | DeclCuts (activation) |
| `decl.ctor-static-call` | declarations | control for N15: a constructor assigns its fields and calls a static method with its parameter | §2 zero-safety | accept; managed = Stage-0 native = self native | DeclCuts (activation) |
| `flow.unreachable-section` | flow | borrow/move checks follow Roslyn reachability: a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162) is not flow-checked, code after `return` and code after an unconditional Panic (`when false` and `if (false)` are cut C19 since 2026-11-17); a use after move there is accepted, the same body in a reachable `default:` is VIB001 | decided N13 rule (SPEC text pending) | accept; managed = Stage-0 native = self native | matrix probes only |

#### retired

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `local.underscore` | locals | a real local named `_` — retired: cut C11; the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`) | user-approved v0.2 cut C11 | accept; managed = Stage-0 native = self native | none (retired) |
| `local.underscore-read` | locals | reading a variable named `_` — retired: cut C11; the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`) | user-approved v0.2 cut C11 | accept; managed = Stage-0 native = self native | none (retired) |
| `param.underscore` | locals | a parameter named `_` — retired: cut C11; the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`) | user-approved v0.2 cut C11 | accept; managed = Stage-0 native = self native | none (retired) |
| `field.underscore` | locals | a field named `_` — retired: cut C11; the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`) | user-approved v0.2 cut C11 | accept; managed = Stage-0 native = self native | none (retired) |
| `switch.guard-const` | switch | `when` guard that is a constant expression of literals (`when 0.0 >= 0.0`); C# folds it like `when true`/`when false` — retired: cut C19; guards are non-constant | user-approved v0.2 cut C19 | accept; managed = Stage-0 native = self native | none (retired) |
| `switch.guard-true` | switch | `when true` guard — retired: cut C19; guards are non-constant | user-approved v0.2 cut C19 | accept; managed = Stage-0 native = self native | none (retired) |
| `switch.guard-false` | switch | `when false` guard — retired: cut C19; guards are non-constant | user-approved v0.2 cut C19 | accept; managed = Stage-0 native = self native | none (retired) |
| `switch.section-local` | switch | declaration directly in a switch section (switch-block declaration space) — retired: cut C18; a section that declares a local is wrapped in a block (Decut) | user-approved v0.2 cut C18 | accept; managed = Stage-0 native = self native | none (retired) |
| `switch.cross-section-var` | switch | variable declared directly in one section (local or `out var`), assigned and read in a later section — retired: cut C18; a section that declares a local is wrapped in a block (Decut) | user-approved v0.2 cut C18 | accept; managed = Stage-0 native = self native | none (retired) |
| `switch.cross-section-dup` | switch | the same name declared directly in two sections of one switch block (C# CS0128) — retired: cut C18; a section that declares a local is wrapped in a block (Decut) | user-approved v0.2 cut C18 | accept; managed = Stage-0 native = self native | none (retired) |
| `pattern.relational` | patterns | relational pattern — retired: cut C20; case labels use constant, `or` and `not` patterns; relational tests move to `when` guards or `&&` conditions | user-approved v0.2 cut C20 | accept; managed = Stage-0 native = self native | none (retired) |
| `pattern.and` | patterns | `and` pattern combinator — retired: cut C20; case labels use constant, `or` and `not` patterns | user-approved v0.2 cut C20 | accept; managed = Stage-0 native = self native | none (retired) |
| `param.ref-readonly` | params | `ref readonly` parameter (CS9193 promoted) — retired: cut C10; `in` parameters instead | user-approved v0.2 cut C10 | accept; managed = Stage-0 native = self native | none (retired) |
| `coll.conditional` | collections | conditional between collection expressions — retired: cut C16; a conditional chooses between collection locals instead | user-approved v0.2 cut C16 | accept; managed = Stage-0 native = self native | none (retired) |
| `coll.conditional-unparenthesized` | collections | `c ? [a] : [b]` without parentheses — retired: cut C16; a conditional chooses between collection locals instead | user-approved v0.2 cut C16 | accept; managed = Stage-0 native = self native | none (retired) |
| `ca.loan-ref-readonly` | call-activation | `ref readonly` parameter (`in` or `ref` argument) — retired: cut C10; `in` parameters, with an explicit `in` or an implicit by-reference argument (`ca.loan-in-implicit`) | user-approved v0.2 cut C10 | accept; managed = Stage-0 native = self native | none (retired) |
| `arith.float-nint-const` | arith | `unchecked((nint)c)` / `(nuint)` of an in-range float constant — retired: cut C17; the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`) | user-approved v0.2 cut C17 | accept; managed = Stage-0 native = self native | none (retired) |
| `arith.float-nint-const-oob` | arith | `unchecked((nint)c)` / `(nuint)` of an out-of-range float constant (saturating) — retired: cut C17; the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`) | user-approved v0.2 cut C17 | accept; managed = Stage-0 native = self native | none (retired) |
| `arith.float-nint-const-special` | arith | `unchecked((nint)c)` / `(nuint)` of an infinity or NaN constant — retired: cut C17; the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`) | user-approved v0.2 cut C17 | accept; managed = Stage-0 native = self native | none (retired) |
| `arith.float-nint-runtime` | arith | the same conversions of a runtime `double`/`float` local — retired: cut C17; the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`) | user-approved v0.2 cut C17 | accept; managed = Stage-0 native = self native | none (retired) |
| `arith.nint-bounds` | arith | `nint.MinValue`/`MaxValue`, `nuint.MinValue`/`MaxValue` — retired: cut C17; the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`) | user-approved v0.2 cut C17 | accept; managed = Stage-0 native = self native | none (retired) |

#### near-miss

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `switch.section-no-terminator` | switch | section end reachable (fallthrough) | §3.7(1) VIB000 / CS0163 | C#-invalid: every frontend rejects | Mutator drop-terminator |
| `arg.rvalue-to-ref-param` | params | literal/rvalue passed with an explicit `in` (CS8156) | CS9193 / CS8156 | C#-invalid: every frontend rejects | RefParams/Const |
| `coll.foreach-literal` | collections | foreach directly over a bare collection literal (no natural type in C#: every frontend must reject) | C# CS9176 | C#-invalid: every frontend rejects | Loop |
| `mut.embedded-decl` | near-miss | declaration as an embedded statement (`if (c) int x = 1;`) | C# CS1023 | C#-invalid: every frontend rejects | Mutator embed-decl |

#### vib-invalid

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `span.two-slices-one-call` | span | `Pair(s[..k], s[k..])`: two `Span<int>` arguments sliced from one span in one call — expected VIB002 | §3.3 expanded exclusive set (root provenance) | C#-valid, SPEC rejects: frontends agree on IDs | SpanSlice (extended) |
| `span.nm-overlap` | span | `Over(s, s)` / `Over(s[0..2], s[1..3])` with two `Span<int>` parameters — expected VIB002 | §3.3 | C#-valid, SPEC rejects: frontends agree on IDs | SpanSlice (extended) |
| `span.nm-parent-while-live` | span | write to the parent span while a slice of it is live — expected VIB003 | §3.2 | C#-valid, SPEC rejects: frontends agree on IDs | SpanSlice (extended) |
| `span.nm-moved` | span | `var m = s;` moves a `Span<T>` (`!Copy`), then `s` is used — expected VIB001 | §3.2(1) | C#-valid, SPEC rejects: frontends agree on IDs | SpanSlice (extended) |
| `span.nm-vec-grow` | span | `v.Add(1)` while a span borrowed from `v` is live — expected VIB003 | §3.2, §3.5 | C#-valid, SPEC rejects: frontends agree on IDs | SpanSlice (extended) |
| `optmut.nm-alias` | option slots | `o` read while an `OptionMut` of it is live — expected VIB003 | §3.2 | C#-valid, SPEC rejects: frontends agree on IDs | OptionSlot (extended) |
| `optref.nm-write` | option slots | field write through an `AsRef()` (shared) binding — expected VIB004 | §3.1 shared pattern binding | C#-valid, SPEC rejects: frontends agree on IDs | OptionSlot (extended) |
| `optslot.nm-place-write` | option slots | field write through a plain `o is Some<Cell>(var c)` place binding (shared unless `.AsMut()`) — expected VIB004 | §3.1 | C#-valid, SPEC rejects: frontends agree on IDs | OptionSlot (extended) |
| `optmut.nm-move` | option slots | an `AsMut()` binding passed by value (move out of a borrow) — expected a rejection (ID not fixed by SPEC) | §3 | C#-valid, SPEC rejects: frontends agree on IDs | OptionSlot (extended) |
| `optmut.nm-overlap` | option slots | `Two(o.AsMut(), o.AsMut())` — expected VIB002 | §3.3 | C#-valid, SPEC rejects: frontends agree on IDs | OptionSlot (extended) |
| `mut.nm-from-shared` | [Mut] receivers | unmarked method calling a `[Mut]` method on `this` — expected VIB004 | §3.1 `[Mut]` body verification | C#-valid, SPEC rejects: frontends agree on IDs | MutReceiver (extended) |
| `mut.nm-in-param` | [Mut] receivers | `[Mut]` call through an `in` parameter — expected VIB004 | §3.1 | C#-valid, SPEC rejects: frontends agree on IDs | MutReceiver (extended) |
| `mut.nm-two-phase` | [Mut] receivers | `a.Add(a.Drain())`: exclusive argument under a reserved exclusive receiver — expected VIB002 or VIB003 | §3.2 two-phase | C#-valid, SPEC rejects: frontends agree on IDs | MutReceiver (extended) |
| `mut.nm-borrow-live` | [Mut] receivers | `a.Add(1)` while a span of `a.Items` is live — expected VIB003 | §3.2 | C#-valid, SPEC rejects: frontends agree on IDs | MutReceiver (extended) |
| `mut.nm-foreach-mutate` | [Mut] receivers | `foreach (var g in a.Items) a.Add(g)` — expected VIB003 | §3.2 foreach | C#-valid, SPEC rejects: frontends agree on IDs | MutReceiver (extended) |
| `mut.nm-moved` | [Mut] receivers | `var h = a;` moves the class, then `a.Add(1)` — expected VIB001 | §3.4 | C#-valid, SPEC rejects: frontends agree on IDs | MutReceiver (extended) |
| `utf8.nm-mutate-owner` | UTF-8 views | `sb.Append(..)` inside `foreach (var l in sb.Lines())` — expected VIB003 | §2 | C#-valid, SPEC rejects: frontends agree on IDs | Utf8View (extended) |
| `utf8.nm-slice-after-clear` | UTF-8 views | `sb.Clear()` while a slice of `sb` is live — expected VIB003 | §2, §3.2 | C#-valid, SPEC rejects: frontends agree on IDs | Utf8View (extended) |
| `utf8.nm-view-local` | UTF-8 views | `var lines = text.Lines();` (view in a local) — expected VIB000 | §2 closed foreach views | C#-valid, SPEC rejects: frontends agree on IDs | Utf8View (extended) |
| `std.nm-map-moved` | Std | `var m2 = m;` moves an `IntMap`, then `m.Set(..)` — expected VIB001 | §3.4 | C#-valid, SPEC rejects: frontends agree on IDs | StdText (extended) |
| `std.nm-map-key-borrow` | Std | `m.Set(..)` while a `KeyAt` slice of `m` is live — expected VIB003 | §3.2 | C#-valid, SPEC rejects: frontends agree on IDs | StdText (extended) |
| `std.nm-buf-borrow` | Std | `sb.Append(..)` while `sb.AsUtf8()` is live — expected VIB003 | §3.2 | C#-valid, SPEC rejects: frontends agree on IDs | StdText (extended) |

#### vib-reject

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `ca.borrow-then-move` | call-activation | an earlier argument, receiver or assignment target borrows a place that a later operand of the same call or assignment moves | §3.2 call activation (model; SPEC text pending) | C#-valid, SPEC rejects with VIB003 | CaCall/CaReceiver/CaAssign (activation) |
| `ca.move-then-use` | call-activation | an earlier argument moves a place that a later argument borrows | §3.4 use after move | C#-valid, SPEC rejects with VIB001 | CaCall (activation) |
| `view.lifetimeof-violated` | views | `[return: LifetimeOf("x")]` with a body that may return `y` | §3.3 LifetimeOf | C#-valid, SPEC rejects with VIB007 | ViewReturn (activation) |
| `view.elision-this-violated` | views | instance method whose elided result is tied to `this` returns the view parameter | §3.3 elision | C#-valid, SPEC rejects with VIB007 | ViewReturn (activation) |
| `view.elision-ambiguous` | views | two borrowed inputs (views or `OptionRef`) and a returned view without `LifetimeOf` | §3.3 elision | C#-valid, SPEC rejects with VIB007 | ViewReturn (activation) |
| `view.use-after-mutate` | views | an input named by `LifetimeOf` (or the single elided input) is mutated while the result lives | §3.2 | C#-valid, SPEC rejects with VIB003 | ViewReturn (activation) |
| `view.reborrow-parent-while-live` | views | write through span `s` while `First(s)` is still live | §3.2 reborrow | C#-valid, SPEC rejects with VIB003 | ViewReturn (activation) |
| `view.param-stack-escape` | views | a by-value `ReadOnlySpan<int>` parameter assigned a collection expression (`x = [1, 2]`, on one branch, `x = []`, or returned as `x[1..]`) and returned: the storage is owned by the parameter's scope and the view escapes the method (C# accepts constant elements) | §3.7 collection storage | C#-valid, SPEC rejects with VIB007 | ViewReturn (activation) |
| `view.param-stack-scope-exit` | views | the same shape: the parameter's scope ends while the returned view borrows its collection storage | §3.7 collection storage | C#-valid, SPEC rejects with VIB003 | ViewReturn (activation) |
| `view.move-use-source` | views | a `!Copy` view (`Span<int>`, `OptionMut<T>`) moved by a plain move, then the source used (before or after the destination): exactly VIB001; the moved source keeps no borrow (decision A+) | §3.2 item 1 | C#-valid, SPEC rejects with VIB001 | ViewMove (activation) |
| `view.move-dest-live-owner-used` | views | a `!Copy` view moved by a plain move; the owner is read while the destination is still live (a real conflict) | §3.2 | C#-valid, SPEC rejects with VIB003 | ViewMove (activation) |
| `view.move-conditional-use-source` | views | a `!Copy` view moved on one branch only, then the owner read and the source used: VIB001 and VIB003 (the oracle checks that one of them is reported) | §3.2 item 1 | C#-valid, SPEC rejects with VIB001 or VIB003 | ViewMove (activation) |
| `view.loop-old-value-used-top` | views | a loop-reassigned view's old value used at the top of the next iteration while its owner is written (a real conflict) | §3.2 | C#-valid, SPEC rejects with VIB003 | ViewMove (activation) |
| `view.loop-moved-source-used` | views | a view moved by an assignment value used at the top of the next iteration (a real use after move) | §3.2 item 1 | C#-valid, SPEC rejects with VIB001 | ViewMove (activation) |
| `view.rebind-old-derived-live` | views | a view derived from the old value (`u = t[..1]`) is still live when the old owner is mutated after the rebinding (a real conflict) | §3.2 | C#-valid, SPEC rejects with VIB003 | ViewMove (activation) |
| `view.assign-value-then-use` | views | the value of an assignment to a `!Copy` view used as a receiver, an argument or a chain (`(s = b.AsSpan()).Length`, `[0]`, `[1..].Length`, `Len(s = ..)`, `t = s = ..`, `(m = o2.AsMut()).IsSome`), then the view used again: exactly VIB001 | §3.2 item 1 | C#-valid, SPEC rejects with VIB001 | ViewMove (activation) |
| `cond.temp-escapes-statement` | temporaries | a conditional whose arm views a temporary is stored in a local | §3.7 temporaries | C#-valid, SPEC rejects with VIB007 | CondTemp (activation) |
| `decl.this-call-after-init` | declarations | a constructor calls an instance method on `this` after every field is assigned (decided N15: in v0.2 a constructor may not call an instance method on `this`, read an instance property through `this` or let `this` escape, whatever is assigned) | §2 zero-safety (N15: SPEC aligned to the implementations) | C#-valid, SPEC rejects with VIB008 | DeclCuts (activation) |
| `decl.this-call-before-init` | declarations | a constructor calls an instance method on `this` before a non-zero-safe field is initialized | §2 zero-safety | C#-valid, SPEC rejects with VIB008 | DeclCuts (activation) |
| `snd.expect-conflict` | soundness | a later use keeps the loan live across a conflicting invalidation of its owner (placements after, branch, loop, switch, after-loop, nested-use-last, scope-exit) | §3.2 borrows | C#-valid, SPEC rejects with VIB003 | Snd* (soundness) |
| `snd.expect-use-after-move` | soundness | the loan is re-acquired from an owner that an earlier statement moved (placements reacquire, loop-reacquire with a moving invalidator) | §3.1 moves | C#-valid, SPEC rejects with VIB001 | Snd* (soundness) |
| `snd.expect-field-move` | soundness | a `Vec` field is moved out of a class (`var it = h.Items;`), whatever the placement | §3.1 moves | C#-valid, SPEC rejects with VIB005 | Snd* (soundness) |

#### banned

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `banned.null` | banned | `null` literal | §1(2), §4 VIB008 | reject with VIB008 | Banned |
| `banned.nullable` | banned | nullable annotation `T?` | §1(2), §4 VIB008 | reject with VIB008 | Banned |
| `banned.null-forgiving` | banned | null-forgiving `x!` | §1(2), §4 VIB008 | reject with VIB008 | Banned |
| `banned.goto` | banned | `goto`/labels | §1 closed subset (unstructured goto) | reject with VIB000 | Banned |
| `banned.string-concat` | banned | string `+` | §2 string restrictions | reject with VIB000 | Banned |
| `banned.string-equality` | banned | string `==` | §2 string restrictions | reject with VIB000 | Banned |
| `banned.string-length` | banned | `string.Length` | §2 string restrictions | reject with VIB000 | Banned |
| `banned.interpolation` | banned | `$"..."` interpolation | §2 string restrictions, §7(1) | reject with VIB000 | Banned |
| `banned.struct-idisposable` | banned | `struct S : IDisposable` | §1, §2 linear policy | reject with VIB000 or VIB006 | Banned |
| `banned.manual-dispose` | banned | manual `.Dispose()` call | §2 exact-once disposal (1) | reject with VIB006 | Banned |
| `banned.linear-field` | banned | IDisposable resource stored in a field | §2, §4 VIB006 | reject with VIB006 | Banned |
| `banned.try` | banned | try/finally | §1 exceptions | reject with VIB000 | Banned |
| `banned.throw` | banned | throw | §1 exceptions | reject with VIB000 | Banned |
| `banned.lambda` | banned | lambda / delegate | §1 delegates, lambdas | reject with VIB000 | Banned |
| `banned.nonstatic-localfunc` | banned | non-static local function | §1 | reject with VIB000 | Banned |
| `banned.static-field` | banned | static (non-const) field | §1 | reject with VIB000 | Banned |
| `banned.array` | banned | raw `T[]` array | §1 | reject with VIB000 | Banned |
| `banned.object` | banned | `object` / boxing | §1 | reject with VIB000 | Banned |
| `banned.checked` | banned | `checked(...)` | §1 | reject with VIB000 | Banned |
| `banned.inheritance` | banned | class inheritance | §1 | reject with VIB000 | Banned |
| `banned.object-initializer` | banned | object/collection member initializer `new S { A = 1 }` (listed in SPEC from candidate-freeze-3; freeze-2 frontends already fail closed) | §1 closed subset | reject with VIB000 | Banned |
| `banned.collection-return-span` | banned | collection expression returned as `ReadOnlySpan<T>`/`Span<T>` on a reachable path (`return [];`, `return [1, 2];`, `=> [1]`); a collection rvalue has its destination's storage and there is no static-data promotion | §3.7 storage contract (decided; SPEC text pending) | reject with VIB007 | Banned |
| `banned.default-nonzerosafe` | banned | `default` of a non-zero-safe type (Vec<T>) | §2 zero-safety (1) | reject with VIB008 | Banned |

#### scenario

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `snd.producer-span` | soundness | loan producer: `Span<int> s = v.AsSpan()` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-ros` | soundness | loan producer: `ReadOnlySpan<int> s = v.AsReadOnlySpan()` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-slice-range` | soundness | loan producer: `v.AsSpan()[0..2]` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-slice-method` | soundness | loan producer: `v.Slice(0, 2)` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-slice-ro` | soundness | loan producer: `v.ReadOnlySlice(1, 2)` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-vec-range` | soundness | loan producer: `v[0..2]` (Vec Range pattern) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-view-helper` | soundness | loan producer: a view returned by a helper (`Head(v.AsReadOnlySpan())`, elided lifetime) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-ref-elem` | soundness | loan producer: `ref int s = ref v[0]` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-ref-field` | soundness | loan producer: `ref int s = ref h.K` (class field) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-optref` | soundness | loan producer: `var s = o.AsRef()` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-optmut` | soundness | loan producer: `var s = o.AsMut()` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.producer-recv-view` | soundness | loan producer: a view returned by a receiver method (`h.View()`) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-move` | soundness | invalidator: move of the owner (`var w = v;`) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-sink` | soundness | invalidator: the owner passed to a consuming parameter | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-sink-explicit` | soundness | invalidator: `Sink(Move(v))` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-growth` | soundness | invalidator: `v.Add(p)` (may reallocate) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-clear` | soundness | invalidator: `v.Clear()` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-resize` | soundness | invalidator: `v.Resize(8, p)` (reallocates) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-reassign` | soundness | invalidator: the owner reassigned (`v = [p]`, `h = new …`, `o = None`) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-scope-exit` | soundness | invalidator: the owner's block ends while the view (declared outside) lives | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-mut-call` | soundness | invalidator: a `[Mut]` method on the owner (`h.Push(p)`) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-field-move` | soundness | invalidator: a field moved out of the owner (`var it = h.Items`) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-items-growth` | soundness | invalidator: growth through a field (`h.Items.Add(p)`) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-take` | soundness | invalidator: `o.Take()` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.inval-replace` | soundness | invalidator: `o.Replace(Some(..))` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-after` | soundness | placement: use after the invalidation (straight line) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-before` | soundness | placement: use before the invalidation (control: the loan has ended) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-reacquire` | soundness | placement: invalidate, re-acquire the loan, use | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-branch` | soundness | placement: invalidation in one branch, use after the `if` | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-branch-disjoint` | soundness | placement: use and invalidation in disjoint branches (control) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-loop` | soundness | placement: use, then invalidation in the first iteration (loop-carried) | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-loop-reacquire` | soundness | placement: acquire, use and invalidate in every iteration | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-switch` | soundness | placement: invalidation in a switch section, use after the switch | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-after-loop` | soundness | placement: invalidation in a loop, use after it | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-nested-use-first` | soundness | placement: use and invalidation as arguments of one call, the use first | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-nested-use-last` | soundness | placement: use and invalidation as arguments of one call, the invalidation first | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.place-scope-exit` | soundness | placement: view declared outside the owner's block, used after it | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.use-read` | soundness | the later use reads through the loan | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |
| `snd.use-write` | soundness | the later use writes through the loan | §3 ownership and borrows (expectation-free) | accept; managed = Stage-0 native = self native | Snd* (soundness) |

#### cut

| Tag | Area | Feature | SPEC | Expected | Generator |
|---|---|---|---|---|---|
| `cut.localfunc-in-switch-section` | cut | local function declared directly in a switch section (inside a braced block stays valid) | user-approved v0.2 cut (not yet in SPEC) | reject with VIB000 | SwitchStmt/Const |
| `cut.struct-piecewise-owned` | cut | piecewise init (field assignment or out) of a not-yet-fully-initialized struct with owned/drop fields, directly or nested | user-approved v0.2 cut (not yet in SPEC) | reject with VIB000 | StructInit/RefParams |
| `cut.generic-pattern` | cut | any pattern other than `var` and discard on a value of open type-parameter type: constant, relational, type and declaration patterns (`x is 0`, `x is > 0 or <= 0`, `x is int`, `x is Box b`, `x is int and > 0`) and switch arms or case labels of those kinds; one VIB000 at the governing expression (`x is var v` and `_` stay valid) | decided v0.2 cut C4 (SPEC/cut list pending) | reject with VIB000 | matrix probes only |
| `cut.preprocessor` | cut | conditional compilation directives (`#if`, `#elif`, `#else`, `#endif`, `#define`, `#undef`); one VIB000 per file at the first directive keyword (`#region`, `#pragma`, `#nullable` stay valid) | decided v0.2 cut (SPEC/cut list pending) | reject with VIB000 | DeclCuts (activation) |
| `cut.file-type` | cut | file-local types (`file class`, `file struct`); VIB000 at `file` | decided v0.2 cut (SPEC/cut list pending) | reject with VIB000 | DeclCuts (activation) |
| `cut.partial` | cut | partial types and members (single-part `partial` classes included; the Std Sys catalog is exempt); VIB000 at `partial` | decided v0.2 cut (SPEC/cut list pending) | reject with VIB000 | DeclCuts (activation) |
| `cut.nested-in-generic` | cut | a type nested in a generic type (`Outer<T>.Inner`); VIB000 at the nested type's identifier (non-generic nesting stays valid) | decided v0.2 cut (SPEC/cut list pending) | reject with VIB000 | DeclCuts (activation) |
| `cut.ctor-chaining` | cut | constructor chaining `: this(...)`; VIB000 at `this` (`: base(...)` is C21) | decided v0.2 cut (SPEC/cut list pending) | reject with VIB000 | DeclCuts (activation) |
| `cut.ref-readonly-param` | cut | C10: a `ref readonly` parameter (method, local function, constructor, operator, indexer); anchor `ref` (`in` parameters and `ref readonly` locals/returns stay) | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.underscore-decl` | cut | C11: declaring any symbol named `_` (local, parameter, field, property, method, type, type parameter, label, foreach variable, namespace, using alias, query range variable, tuple element name); anchor `_` (discards stay: `_ = e`, `out _`, `out var _`, `_` patterns) | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.optional-param` | cut | C12: a parameter with a default value or `[Optional]`/`[DefaultParameterValue]`; anchor the parameter's first token | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.named-arg-order` | cut | C13: a named argument whose position differs from its parameter's index; anchor the argument name (named arguments in position stay) | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.this-assign` | cut | C14: assignment to `this` (simple, compound, deconstruction, prefix/postfix `++`/`--`) and `ref this`/`out this` as a writable ref; anchor `this` | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.conditional-ref` | cut | C15: a conditional ref expression `c ? ref a : ref b`; anchor `?` | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.nested-collection` | cut | C16: a collection expression as an element or spread operand of another collection expression, a `?:` branch or a switch-expression arm value; anchor the inner `[` | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.native-int` | cut | C17: `nint`/`nuint` (and `IntPtr`/`UIntPtr`) in any type position, cast, `sizeof`, generic argument or member access; anchor the type token | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.switch-section-decl` | cut | C18: a statement directly in a switch section that declares a switch-block-scoped local (local declaration, or an out/pattern/deconstruction variable in an expression statement or `if` condition); anchor the statement's first token (a local function reports both C1 and C18) | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.constant-condition` | cut | C19: a C# constant condition of `if`/`while`/`do`/`for`/`?:`, a constant `when` guard or a constant switch selector (literal `true` loop conditions and `for (;;)` stay); anchor the condition's first token | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.relational-and-pattern` | cut | C20: a relational pattern (`< e`, `<= e`, `> e`, `>= e`) or an `and` pattern anywhere; anchor the operator or `and` (`or`, `not`, constant, type, declaration, `var`, discard, property, positional and list patterns stay) | user-approved v0.2 cut (2026-11-17) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |
| `cut.base-ctor-call` | cut | C21: a base constructor initializer `: base(...)`, with or without arguments, on any constructor; anchor `base` (C9 `: this(...)` is a separate cut; a constructor without an initializer stays) | decided v0.2 cut C21 (2026-11-18) | reject with VIB000 | matrix probes only; tagged by the C10–C21 scan |

### 5.1 Pairwise interactions

Every ordered pair of the 19 generator anchors is scheduled once per 361 programs (18 anchors and 324 programs before
release-7.1's `UnionSwitch`). The matrix shows, for the candidate 45-minute campaign (seed 4242, 18 anchors), `C#-valid programs / programs` per ordered pair (outer anchor in the
row, inner anchor in the column), with the number of programs that had any disagreement in **bold**. Column
headers abbreviate the anchor names in the rows (`SwExpr` = `SwitchExpr`, `SwStmt` = `SwitchStmt`, `Ref` = `RefParams`, and so on).
324 of 324 ordered pairs were covered by 12655 programs.

| outer \ inner | Arith | Ban | Class | Coll | Cond | Const | LFunc | Loop | OptRes | Out | Panic | PatVar | Ref | Struct | SwExpr | SwStmt | Under | Using |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Arith | 25/39 | 28/39 | 27/40 | 22/39 | 27/39 | 15/39 | 29/39 | 18/40 | 22/40 | 23/39 | 17/39 **1** | 25/39 | 12/40 **2** | 18/39 | 20/39 **1** | 18/39 | 14/39 | 33/39 |
| Banned | 25/39 | 23/39 | 31/39 | 24/39 | 25/39 | 20/39 | 30/39 | 23/39 | 24/39 | 22/39 | 18/39 | 27/39 | 21/40 | 27/39 | 16/39 | 18/39 | 17/39 | 28/39 |
| ClassInstance | 21/39 | 22/39 | 26/39 | 30/39 | 24/39 | 15/39 | 29/39 | 24/39 | 25/39 **1** | 25/39 | 18/39 | 19/39 | 16/39 **1** | 27/39 | 17/39 **2** | 19/39 | 13/39 | 25/39 |
| Collection | 26/39 | 30/39 | 25/39 | 30/39 | 27/39 | 17/39 | 22/39 **1** | 22/39 **1** | 22/39 | 28/39 | 16/39 | 28/39 **1** | 18/39 **3** | 26/39 | 13/39 **1** | 16/39 | 16/39 | 26/39 |
| CondArg | 30/39 | 23/39 | 27/39 | 29/39 | 26/39 | 16/39 | 29/39 | 24/39 | 16/39 | 26/39 | 18/39 | 23/39 | 21/39 **2** | 21/39 **1** | 21/39 **2** | 13/39 | 11/39 **1** | 27/39 |
| Const | 18/39 | 17/39 | 20/39 | 23/39 | 14/39 **1** | 13/39 | 20/39 | 18/39 **1** | 18/39 | 18/39 | 9/39 **1** | 12/39 **1** | 10/40 | 18/39 | 11/39 **2** | 13/40 | 16/39 | 20/39 |
| LocalFunc | 27/39 | 26/39 | 25/39 | 25/39 | 21/40 | 18/39 | 27/39 | 32/39 **1** | 25/39 | 26/39 | 14/40 | 25/39 | 19/39 **1** | 22/39 | 11/39 **2** | 17/39 | 20/39 | 28/39 **1** |
| Loop | 25/40 **1** | 26/39 | 20/39 | 22/39 | 19/39 | 25/39 **1** | 28/39 **1** | 23/39 | 15/39 | 24/39 **1** | 13/39 | 26/39 | 16/39 **1** | 27/39 | 20/39 **2** | 11/39 **3** | 19/39 **2** | 20/39 |
| OptionResult | 26/39 | 18/39 | 27/39 **1** | 19/39 | 25/39 | 16/39 | 27/39 | 19/39 **1** | 18/39 | 16/39 | 15/39 | 21/39 | 20/39 **1** | 24/39 | 13/39 **1** | 13/39 | 16/39 **1** | 19/39 **2** |
| OutArg | 21/39 | 28/39 | 31/40 | 29/39 | 24/39 | 20/39 | 26/39 | 23/39 | 26/39 | 25/39 | 16/40 | 23/39 | 18/39 **1** | 22/39 | 15/39 **2** | 16/39 | 21/39 | 25/39 |
| Panic | 15/39 | 18/39 | 15/39 | 14/39 | 22/40 | 12/39 | 15/40 | 17/39 | 20/39 | 12/39 | 13/39 | 16/39 | 13/39 **1** | 18/39 | 14/39 **3** | 11/39 | 13/39 | 15/39 |
| PatternVar | 22/39 | 25/39 | 25/40 | 20/39 | 28/39 | 16/39 **1** | 17/39 | 21/39 | 21/39 | 22/39 **1** | 13/39 | 25/39 | 14/39 **2** | 21/39 | 11/39 | 14/39 | 15/39 | 21/39 **1** |
| RefParams | 19/40 **1** | 9/39 | 12/39 **2** | 16/39 | 15/39 **1** | 11/39 **4** | 24/39 | 14/39 **4** | 15/39 **1** | 18/39 **2** | 7/39 | 20/39 **3** | 11/39 **2** | 13/39 **2** | 11/39 **3** | 13/40 **1** | 12/39 **1** | 21/39 **2** |
| StructInit | 24/39 | 25/39 | 23/39 **1** | 26/39 | 19/39 | 18/39 **2** | 27/39 | 27/39 | 17/39 | 25/39 | 10/39 | 23/39 | 21/40 **4** | 21/39 | 13/39 **1** | 14/39 | 14/39 | 27/39 |
| SwitchExpr | 13/39 | 18/39 | 13/39 **3** | 14/39 **3** | 15/39 **1** | 15/39 **1** | 13/39 **2** | 11/39 **3** | 20/39 **1** | 18/39 **3** | 9/39 **1** | 16/39 **3** | 10/39 **4** | 15/39 **1** | 12/39 | 6/39 **1** | 14/39 | 20/39 **3** |
| SwitchStmt | 13/39 | 14/39 | 12/39 | 11/40 **1** | 11/39 | 14/39 | 12/39 | 11/39 | 11/39 | 16/39 | 10/39 | 11/39 | 10/39 | 14/39 | 10/39 **1** | 10/39 | 11/39 | 14/39 |
| Underscore | 15/39 **1** | 22/39 | 20/39 | 21/39 | 25/39 | 16/39 | 24/39 | 22/39 | 15/39 | 11/39 | 11/39 | 17/39 | 15/39 | 15/39 | 15/39 **4** | 10/39 **1** | 19/39 | 15/39 |
| Using | 25/39 | 27/39 | 26/39 | 27/39 | 23/39 | 17/39 **1** | 28/39 **1** | 25/39 | 21/39 | 25/39 | 17/39 | 21/39 | 15/39 | 28/39 | 14/39 **1** | 13/39 | 23/39 | 29/39 |

### 5.2 Banned-by-design and cut rows (reference probes)

One hand-written reference probe per row, run with `run-conformance.sh matrix` on the frozen tree, on the
candidate and on Round-11 candidate 1 (§6.9). These probes are fixed test inputs, not generator templates. On Round-11
candidates 2 (§6.10) and 3 (§6.11) every row below gives the same result as on Round-11 candidate 1. The declaration
cuts C5–C9 have their own table below.

| Row | Construct | SPEC | Expected | Frozen tree: Analyzer / Stage-0 / self | Verdict | Candidate: Analyzer / Stage-0 / self | Verdict | Round-11 candidate 1: Analyzer / Stage-0 / self | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| `banned.null` | `null` literal | §1(2), §4 VIB008 | VIB008 | VIB000,VIB008 / VIB000,VIB008 / VIB008 | OK* | VIB000,VIB008 / VIB000,VIB008 / VIB008 | OK* | VIB000,VIB008 / VIB000,VIB008 / VIB008 | OK* |
| `banned.nullable` | nullable annotation `T?` | §1(2), §4 VIB008 | VIB008 | VIB000,VIB008 / VIB000,VIB008 / VIB008 | OK* | VIB000,VIB008 / VIB000,VIB008 / VIB008 | OK* | VIB000,VIB008 / VIB000,VIB008 / VIB008 | OK* |
| `banned.null-forgiving` | null-forgiving `x!` | §1(2), §4 VIB008 | VIB008 | VIB008 / VIB008 / VIB008 | OK | VIB008 / VIB008 / VIB008 | OK | VIB008 / VIB008 / VIB008 | OK |
| `banned.goto` | `goto`/labels | §1 closed subset (unstructured goto) | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.string-concat` | string `+` | §2 string restrictions | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.string-equality` | string `==` | §2 string restrictions | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.string-length` | `string.Length` | §2 string restrictions | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.interpolation` | `$"..."` interpolation | §2 string restrictions, §7(1) | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.struct-idisposable` | `struct S : IDisposable` | §1, §2 linear policy | VIB000 or VIB006 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.manual-dispose` | manual `.Dispose()` call | §2 exact-once disposal (1) | VIB006 | VIB006 / VIB006 / VIB006 | OK | VIB006 / VIB006 / VIB006 | OK | VIB006 / VIB006 / VIB006 | OK |
| `banned.linear-field` | IDisposable resource stored in a field | §2, §4 VIB006 | VIB006 | VIB006 / VIB006 / VIB006 | OK | VIB006 / VIB006 / VIB006 | OK | VIB006 / VIB006 / VIB006 | OK |
| `banned.try` | try/finally | §1 exceptions | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.throw` | throw | §1 exceptions | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.lambda` | lambda / delegate | §1 delegates, lambdas | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.nonstatic-localfunc` | non-static local function | §1 | VIB000 | VIB000,VIB001 / VIB000,VIB001 / VIB000 | OK* | VIB000,VIB001 / VIB000,VIB001 / VIB000 | OK* | VIB000,VIB001 / VIB000,VIB001 / VIB000 | OK* |
| `banned.static-field` | static (non-const) field | §1 | VIB000 | VIB000,VIB001 / VIB000,VIB001 / VIB000 | OK* | VIB000,VIB001 / VIB000,VIB001 / VIB000 | OK* | VIB000,VIB001 / VIB000,VIB001 / VIB000 | OK* |
| `banned.array` | raw `T[]` array | §1 | VIB000 | VIB000 / VIB000 / VIB000,VIB001 | OK* | VIB000 / VIB000 / VIB000,VIB001 | OK* | VIB000 / VIB000 / VIB000,VIB001 | OK* |
| `banned.object` | `object` / boxing | §1 | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.checked` | `checked(...)` | §1 | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.inheritance` | class inheritance | §1 | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.object-initializer` | object/collection member initializer `new S { A = 1 }` | §1 closed subset (listed from candidate-freeze-3; not in freeze-2's SPEC) | VIB000 | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `banned.default-nonzerosafe` | `default` of a non-zero-safe type (Vec<T>) | §2 zero-safety (1) | VIB008 | VIB008 / VIB008 / VIB008 | OK | VIB008 / VIB008 / VIB008 | OK | VIB008 / VIB008 / VIB008 | OK |
| `banned.collection-return-span` (corpus r6) | `return [];` of an enum-typed `ReadOnlySpan<T>` on a reachable path | §3.7 storage contract (decided; SPEC text pending) | VIB007 | not run | — | VIB007 / VIB007 / VIB007 | OK | VIB007 / VIB007 / VIB007 | OK |
| `banned.collection-return-span` (corpus r7) | `return [1, 2];` of a `ReadOnlySpan<int>` on a reachable path | §3.7 storage contract (decided; SPEC text pending) | VIB007 | not run | — | VIB007 / VIB007 / VIB007 | OK | VIB007 / VIB007 / VIB007 | OK |
| `cut.localfunc-in-switch-section` | local function declared directly in a switch section | user-approved v0.2 cut | VIB000 | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `cut.struct-piecewise-owned` | piecewise init of a not-yet-initialized struct with owned fields | user-approved v0.2 cut | VIB000 | accept / accept / VIB001 | violation | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | `x is 0` on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | `x is > 0 or <= 0` (T = long) on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000,VIB001 | violation | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | `x is int` (T = int and long) on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | VIB000 / VIB000 / VIB000 | OK | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | `x is Box b` on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | `x is int and > 0` on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | switch arm `> 0 =>` on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | switch arm `int i =>` on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK |
| `cut.generic-pattern` | `case 5:` on `T x` on a value of open type-parameter type | decided v0.2 cut C4 (SPEC/cut list pending) | VIB000 | not run | — | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK |
| `control.localfunc-in-braced-section-block` | valid control next to a cut | user-approved v0.2 cut | accept | accept / accept / accept | OK | accept / accept / accept | OK | accept / accept / accept | OK |
| `control.struct-plain-fieldwise` | valid control next to a cut | user-approved v0.2 cut | accept | accept / accept / VIB001 | violation | accept / accept / accept | OK | accept / accept / accept | OK |
| `control.struct-owned-overwrite-after-full-init` | valid control next to a cut | user-approved v0.2 cut | accept | accept / accept / accept | OK | accept / accept / accept | OK | accept / accept / accept | OK |
| `control.generic-var-pattern` | `x is var v` on `T x`: valid control next to C4 | decided v0.2 cut C4 | accept | not run | — | accept / accept / accept | OK | accept / accept / accept | OK |
| `control.generic-discard-arm` | switch arm `_ =>` on `T x`: valid control next to C4 | decided v0.2 cut C4 | accept | not run | — | accept / accept / accept | OK | accept / accept / accept | OK |
| `control.generic-var-arm` | switch arm `var v =>` on `T x`: valid control next to C4 | decided v0.2 cut C4 | accept | not run | — | accept / accept / accept | OK | accept / accept / accept | OK |

OK* means every frontend reports the documented ID, but secondary IDs differ. Under the decided rule (§7.1) those are not compared.

The C4 rows (`cut.generic-pattern`) were added after the cut was decided, so the freeze-2 candidate rows are violations.
On `candidate-freeze-2` self already rejects every C4 row (its message is about runtime type tests or generic lowering,
not the cut message), the Analyzer and Stage-0 reject only `x is int` called with both `int` and `long`, and all three
controls are accepted with equal output. On Round-11 candidate 1 every C4 row is rejected with VIB000 by all three
frontends (with the cut message) and the three controls are accepted: all eleven rows are OK and are now regression
probes. The cut message is "Patterns other than var and discard on a value of generic type parameter type are not supported in
Vibranium v0.2." with one VIB000 at the governing expression; the matrix compares IDs only.

From release-6 on, three C4 probes use forms without relational or `and` patterns, because C20 cuts those
everywhere: `x is > 0 or <= 0` became `x is 1L or -1L` (T = long), `x is int and > 0` became `x is int or long`, and
the arm `> 0 =>` became `3 =>`. The table above shows the forms as they were run then. A separate probe keeps a
relational pattern on an open type parameter on purpose (`x is > 0`): it carries both cuts, and every frontend must
report the C4 VIB000 and the C20 VIB000 at its anchor (one per cut). It is listed with the C20 rows below.

#### Decided rule probes (N8, `foreach` over `Span<T>`)

The decided rule (§7) is that `foreach (var e in span)` takes a shared reborrow of the span: reads of the span in the
body are valid, writes and other exclusive uses in the body are VIB003, and the span is usable after the loop. A
`ref var` loop is an exclusive reborrow. `run-conformance.sh matrix` prints these rows after the banned and cut rows.
They fail on the freeze-2 candidate (N8). N8 is fixed in Round-11 candidate 1, where all five rows are OK; they are now
regression probes.

| Row | Expected | Candidate: Analyzer / Stage-0 / self | Verdict | Round-11 candidate 1: Analyzer / Stage-0 / self | Verdict |
|---|---|---|---|---|---|
| `decided.span-foreach-read-in-body` | accept | VIB001 / VIB001 / VIB003 | violation | accept / accept / accept | OK |
| `decided.span-foreach-use-after-loop` | accept | VIB001 / VIB001 / accept | violation | accept / accept / accept | OK |
| `decided.span-param-foreach-read-in-body` | accept | VIB001,VIB003 / VIB001,VIB003 / VIB003 | violation | accept / accept / accept | OK |
| `decided.span-foreach-write-in-body` | VIB003 | VIB001 / VIB001 / VIB003 | violation | VIB003 / VIB003 / VIB003 | OK |
| `decided.span-foreach-ref-read-in-body` | VIB003 | VIB001 / VIB001 / VIB003 | violation | VIB003 / VIB003 / VIB003 | OK |

#### Decided rule probes (N13, flow checks follow Roslyn reachability)

The decided rule (§7.11) is that borrow, move and definite-assignment checks run only on code that Roslyn considers
reachable. A `default:` section after value cases that cover the input is unreachable (Roslyn warns CS0162), so a use
after move or an overlapping borrow inside it is not an error; the same code in a reachable `default:` is. Stage-0 and the
Analyzer check the unreachable section on Round-11 candidate 1, so the three decided rows are violations there. The
owner's fix (`/fix-r10-stage0-da`) is in Round-11 candidate 2, where all four rows are OK; they are now regression
probes. Corpus probes `z1`, `z2`, `z3` and `z7` (control) are the same shapes.

| Row | Expected | Round-11 candidate 1: Analyzer / Stage-0 / self | Verdict | Round-11 candidate 2: Analyzer / Stage-0 / self | Verdict |
|---|---|---|---|---|---|
| `decided.unreachable-default-use-after-move` | accept | VIB001 / VIB001 / accept | violation | accept / accept / accept | OK |
| `decided.unreachable-bool-default-use-after-move` | accept | VIB001 / VIB001 / accept | violation | accept / accept / accept | OK |
| `decided.unreachable-default-overlap` | accept | VIB002,VIB003 / VIB002,VIB003 / accept | violation | accept / accept / accept | OK |
| `decided.reachable-default-use-after-move` | VIB001 | VIB001 / VIB001 / VIB001 | OK | VIB001 / VIB001 / VIB001 | OK |

#### Declaration cut rows (C5–C9)

The decided cuts C5–C9 reject, with one VIB000 per cut per file at the first occurrence: conditional compilation
directives (C5, at the directive keyword; `#region`, `#pragma` and `#nullable` stay valid), file-local types (C6, at
`file`), partial types and members (C7, at `partial`; the Std Sys catalog is exempt), a type nested in a generic type
(C8, at the nested type's identifier; nesting in a non-generic type stays valid) and constructor chaining
`: this(...)` (C9, at `this`; `: base(...)` keeps its own rule). The probes are the Round-11 audit repros `pp1`,
`fl1`, `y11`, `pp8` and `y02` with a single-part `partial class` and an `#if` inside a method body added, and three
controls. Round-11 candidate 3 does not implement the cuts yet (O11-8a, O11-8b, O11-9), so every cut row is a
violation there; the second and third C5 rows also print different output natively, because the native builds define
no symbols (`DEBUG`, `NET11_0_OR_GREATER`) while the managed build does. The controls are OK.

| Row | Construct | Expected | Round-11 candidate 3: Analyzer / Stage-0 / self | Verdict |
|---|---|---|---|---|
| `cut.preprocessor` | `#define FOO` + `#if FOO`/`#else`/`#endif` at top level | VIB000 | accept / accept / accept | violation |
| `cut.preprocessor` | `#if DEBUG` at top level | VIB000 | accept / accept / accept (native output differs) | violation |
| `cut.preprocessor` | `#if NET11_0_OR_GREATER`/`#else` in a method body | VIB000 | accept / accept / accept (native output differs) | violation |
| `cut.file-type` | `file static class Hidden` | VIB000 | accept / accept / VIB000 | violation |
| `cut.partial` | two-part `static partial class` with a partial method | VIB000 | accept / accept / VIB000 | violation |
| `cut.partial` | single-part `sealed partial class` | VIB000 | accept / accept / accept | violation |
| `cut.nested-in-generic` | `B<int>.Inner` nested in a generic class | VIB000 | accept / accept / VIB000 | violation |
| `cut.ctor-chaining` | `public C(int a) : this(a, a * 10) { }` | VIB000 | VIB001,VIB008 / VIB001,VIB008 / VIB000 | violation |
| `control.region` | `#region`/`#endregion` | accept | accept / accept / accept | OK |
| `control.nested-in-nongeneric` | `B.Inner` nested in a non-generic class | accept | accept / accept / accept | OK |
| `control.multi-ctor-no-chaining` | two constructors that each initialize every field | accept | accept / accept / accept | OK |

#### Cut rows C10–C20 (release-6)

The cuts of §7.15. For each cut row the expected result is VIB000 from every frontend, and the matrix also checks
that each rejecting frontend reports, for every cut the tool's scan finds in the probe, exactly one VIB000 with the
cut's exact message at the cut's anchor (`cuts=` gives the scan's cuts and positions, `anchors=OK` the check). A
probe whose scan does not find its own cut, or a control in which it finds one, is a `probe-error`. The controls and
the C11 near-misses (real discards) must be accepted with equal output. Candidate 4 does not implement these cuts,
so its cut rows are violations by construction (§6.13). Since a local function directly in a switch section reports
both C1 and C18, the C1 row `cut.localfunc-in-switch-section` of the first table now also needs the C18 message at
the local function (a violation on candidate 4 for that reason only). On the combined cut source (§6.14) every row
below is OK, and every cut the scan finds is reported exactly once at its anchor by every frontend (last column:
cut, line.column of the anchor, check); the C1 row is OK there too (`C18@8.13 OK`).

| Row | Probe | Expected | Round-11 candidate 4: Analyzer / Stage-0 / self | Verdict | Combined cut source: Analyzer / Stage-0 / self | Verdict | Cuts found and anchors (combined source) |
|---|---|---|---|---|---|---|---|
| `cut.relational-and-pattern` | `x is > 0` on `T x` (C4 and C20 in one file) | VIB000 | VIB000 / VIB000 / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C20@2.31 OK |
| `cut.ref-readonly-param` | `static int F(ref readonly int p)` called with `in x` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C10@3.14 OK |
| `cut.ref-readonly-param` | `ref readonly` parameters of a constructor and a local function | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C10@3.14 OK |
| `cut.underscore-decl` | `int _ = 3;` read as `_ + 1` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@1.5 OK |
| `cut.underscore-decl` | `var _ = 7;` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@1.5 OK |
| `cut.underscore-decl` | parameter `_` of a static local function | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@2.18 OK |
| `cut.underscore-decl` | field `_` of a class | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@4.16 OK |
| `cut.underscore-decl` | `foreach (var _ in v)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@3.14 OK |
| `cut.underscore-decl` | type parameter `_` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@2.14 OK |
| `cut.underscore-decl` | namespace `_` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@2.11 OK |
| `cut.underscore-decl` | using alias `_ = System.Int64` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@1.7 OK |
| `cut.optional-param` | `int b = 2` in a local function | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C12@2.21 OK |
| `cut.optional-param` | `int b = 5` in a constructor | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C12@5.21 OK |
| `cut.optional-param` | `[Optional] int b` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C12@2.21 OK |
| `cut.named-arg-order` | `F(b: 2, a: 1)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C13@1.13 OK |
| `cut.named-arg-order` | `F(1, c: 3, b: 2)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C13@1.16 OK |
| `cut.this-assign` | `this = new S(0, 0);` in a `[Mut]` struct method | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C14@8.33 OK |
| `cut.this-assign` | `Inc(ref this)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C14@7.40 OK |
| `cut.conditional-ref` | `ref int r = ref (c ? ref a : ref b);` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C15@4.20 OK |
| `cut.conditional-ref` | `Inc(ref (c ? ref a : ref b))` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C15@4.12 OK |
| `cut.nested-collection` | `k > 1 ? [1, 2] : [3]` as a `ReadOnlySpan<int>` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C16@2.31 OK |
| `cut.nested-collection` | `int[][] jag = [[1], [2, 3]];` | VIB000 | VIB000 / VIB000 / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C16@1.16 OK |
| `cut.nested-collection` | switch-expression arms `1 => [1], _ => [2, 3]` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C16@2.39 OK |
| `cut.native-int` | `nint n = 5;` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C17@1.1 OK |
| `cut.native-int` | `nint.MaxValue` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C17@1.17 OK |
| `cut.native-int` | cast `(nuint)x` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C17@2.18 OK |
| `cut.native-int` | `IntPtr p = IntPtr.Zero;` | VIB000 | VIB000,VIB001 / VIB000,VIB001 / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C17@1.1 OK |
| `cut.native-int` | `Vec<nint>` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C17@1.5 OK |
| `cut.switch-section-decl` | `int t = k * 2;` directly in a case section | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C18@7.13 OK |
| `cut.switch-section-decl` | `int.TryParse("5", out var q);` directly in a section | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C18@7.13 OK |
| `cut.switch-section-decl` | `if (k is int z && z > 1) return z;` directly in a section | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C18@7.13 OK |
| `cut.constant-condition` | `if (true)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C19@1.5 OK |
| `cut.constant-condition` | `do { … } while (false)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C19@5.10 OK |
| `cut.constant-condition` | `K > 2 ? 1 : 0` with `const int K` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C19@2.11 OK |
| `cut.constant-condition` | `when true` guard | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C19@2.29 OK |
| `cut.constant-condition` | constant selector `4 switch { … }` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C19@1.11 OK |
| `cut.constant-condition` | `if (1 == 1)` and `if (2 > 1)` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C19@1.5 OK |
| `cut.relational-and-pattern` | `k is > 3` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C20@2.16 OK |
| `cut.relational-and-pattern` | `case < 0:` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C20@6.14 OK |
| `cut.relational-and-pattern` | `k is not 0 and not 5` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C20@2.22 OK |
| `cut.relational-and-pattern` | arm `>= 10 =>` | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C20@2.22 OK |
| `cut.underscore-decl` | two cuts in one file: `int _ = 1;` and `if (true)` (C11 and C19) | VIB000 | accept / accept / accept | violation | VIB000 / VIB000 / VIB000 | OK | C11@1.5,C19@2.5 OK |
| `control.c10-in-param` | `in` parameter, called with and without `in` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c10-ref-readonly-local` | `ref readonly int r = ref x;` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `nearmiss.c11-discard-assign` | `_ = F(v);` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `nearmiss.c11-discard-out` | `out _` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `nearmiss.c11-discard-out-var` | `out var _`, `out int _` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `nearmiss.c11-discard-arm` | `_ =>` arm | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `nearmiss.c11-discard-type-pattern` | `case long _:` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c12-overloads` | an overload instead of an optional parameter | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c13-named-in-position` | `F(a: 1, b: 2)`, `F(1, b: 2)` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c14-field-assign` | `this.A = 0; B = 0;` in a `[Mut]` method | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c15-if-ref` | `if (c) Inc(ref a); else Inc(ref b);` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c16-collection-locals` | `k > 1 ? a : b` over two collection locals | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c17-long` | `long`/`ulong` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c18-braced-section` | a declaring section wrapped in `{ }`, a `case int n when …` label variable | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c19-loop-true` | `while (true)`, `for (;;)`, `do … while (true)` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c19-constant-operand` | `k > K` with `const int K` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |
| `control.c20-or-not-when` | `k is 1 or 4`, `k is not 0`, `int n when n > 3` | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |

#### Cut row C21 (release-7)

C21 (§7.15, decided 2026-11-18): a base constructor initializer `: base(...)`, with or without arguments, is VIB000
with the message "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no
inheritance.", at the `base` token, once per file at the first occurrence. `CutScan.cs` finds it like C10–C20, so the
anchor check of §1 applies. The combined cut source predates C21, so its C21 rows are violations by construction; the
candidate-6 results are in §6.17.

| Row | Probe | Expected | Combined cut source: Analyzer / Stage-0 / self | Verdict | Candidate 6: Analyzer / Stage-0 / self | Verdict | Cuts found and anchors (candidate 6) |
|---|---|---|---|---|---|---|---|
| `cut.base-ctor-call` | `public C(int a) : base() { A = a; }` in a sealed class without a base type | VIB000 | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C21@6.23 OK |
| `cut.base-ctor-call` | `public D(int v) : base(v + 1) { }` in `sealed class D : B` | VIB000 | VIB000,VIB008 / VIB000,VIB008 / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C21@9.23 OK |
| `cut.base-ctor-call` | two classes with `: base()` each (one VIB000, at the first) | VIB000 | accept / accept / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C21@5.23 OK |
| `cut.base-ctor-call` | C9 and C21 in one class: `: this(a, a * 10)` and `: base()` | VIB000 | VIB000 / VIB000 / VIB000 | violation | VIB000 / VIB000 / VIB000 | OK | C21@8.30 OK |
| `control.c21-ctor-without-initializer` | two constructors without an initializer | accept | accept / accept / accept | OK | accept / accept / accept | OK | — |

On the combined cut source no frontend reports the C21 message: self's VIB000 is its older constructor-chaining
message (once per constructor), and in the second and fourth rows the Analyzer's and Stage-0's VIB000s are the
inheritance ban and C9. On candidate 6 each frontend reports the C21 message exactly once, at the first `base`; in
the second row the Analyzer and Stage-0 also report the inheritance ban (a secondary diagnostic, not compared).

The generator emits no `: base(` (`gen-stats`: 0 C10–C21 forms in every profile), so C21 is covered by these rows
and by the scan of every generated program.

#### Decided rule probes (pattern-binding reassignment, release-7)

SPEC §3.4: a `!Copy` payload of a place scrutinee binds as a shared borrow, and reassigning the binding (`x = ...`) is
VIB004; a `Copy` payload binds as an independent copy. The rows check the plain local assignment path of the shared
borrow check, which no generator profile reached (seeded bug S4, §6.16).

| Row | Probe | Expected | Combined cut source: Analyzer / Stage-0 / self | Verdict | Candidate 6 | Verdict |
|---|---|---|---|---|---|---|
| `decided.noncopy-pattern-binding-reassign` | `if (o is Some<C>(var c)) { c = d; WriteLine(c.F); }` with `sealed class C` | VIB004 | VIB004 / VIB004 / VIB004 | OK | VIB004 / VIB004 / VIB004 | OK |
| `decided.copy-pattern-binding-reassign-control` | `if (o is Some<int>(var k)) { k = 3; WriteLine(k); }` | accept | accept / accept / accept | OK | accept / accept / accept | OK |

With the release-7 tool the matrix has 130 rows. On the combined cut source 121 are OK, 5 OK* (the same as on
candidate 4) and 4 are violations: the four C21 rows. On candidate 6, 125 are OK and 5 OK*, with no violation (§6.17).

#### Decided rule probes (N15, no instance call on `this` in a constructor)

The decided rule (§7.14) aligns SPEC with the frontends: in v0.2 a constructor may not call an instance method on
`this`, read an instance property through `this` or let `this` escape, whatever is assigned (VIB008). The rows are
OK* on Round-11 candidate 3 because Stage-0 and the Analyzer add VIB001 for an internal `__this_N` temporary (O11-11).

| Row | Expected | Round-11 candidate 3: Analyzer / Stage-0 / self | Verdict |
|---|---|---|---|
| `decided.ctor-this-call-after-full-init` (only a `Vec` field, assigned, then `WriteLine(Peek())`) | VIB008 | VIB001,VIB008 / VIB001,VIB008 / VIB008 | OK* |
| `decided.ctor-this-call-zero-safe-fields-only` (only an `int` field, `K = k; K += Peek();`) | VIB008 | VIB001,VIB008 / VIB001,VIB008 / VIB008 | OK* |
| `decided.ctor-this-call-before-init` (`K = Peek(); Items = [k];`) | VIB008 | VIB001,VIB008 / VIB001,VIB008 / VIB008 | OK* |
| `decided.ctor-static-call-control` (fields assigned, `K = Twice(k)` with a static `Twice`) | accept | accept / accept / accept | OK |

With the release-5 tool the matrix has 64 rows. On Round-11 candidate 3 the 8 C5–C9 cut rows are violations and the
other 56 rows are OK or OK*.

#### Decided rule probes (N20, switch over a linear union, release-7.1)

The decision of §7 item 20: over a linear union with an rvalue or `Move(o)` scrutinee, a guarded variant arm, a second
arm for a variant, and a `not` or `or` pattern are exactly {VIB006} (the `=VIB006` expectation is exact: a secondary
ID is a violation); `var u` over the whole union and a guarded discard arm stay valid. Every row is a one-line helper
`Us(int p)` called on the None/Err path and on two payload paths, with `R : IDisposable` printing its id when
disposed. The seven banned rows name root N20 (§4): while N20 was not `fixed` a violation was reported as `known:N20`;
N20 is `fixed` from release-7.2, so a violation is a plain violation. The controls run like the soundness profile,
ASan/LSan at `-O0` and `-O3` for both native compilers.

| Row | Shape (scrutinee `MkL(p)` unless noted) | Expected | Candidate 6 pre9: Analyzer / Stage-0 / self | Verdict | Candidate 6 pre13 | Verdict |
|---|---|---|---|---|---|---|
| `decided.linear-switch-guard-repeat-variant` | `case Some<R>(var v) when p > 5:`, `case Some<R>(var v):`, `case NoneType:` | =VIB006 | VIB001,VIB006 / VIB001,VIB006 / accept | known:N20 | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-guarded-none-default` | `case Some<R>(var v):`, `case NoneType when p < 2:`, `default:` | =VIB006 | VIB001,VIB006 / VIB001,VIB006 / accept | known:N20 | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-expr-guard-repeat-variant` | `Some<R>(var v) when p > 5 => .., Some<R>(var v) => .., NoneType => 7` | =VIB006 | VIB001,VIB006 / VIB001,VIB006 / accept | known:N20 | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-result-guarded-err` | `MkRL(p)`: `case Ok<R>(var v):`, `case Err<int>(var e) when e > 21:`, `default:` | =VIB006 | VIB001,VIB006 / VIB001,VIB006 / accept | known:N20 | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-move-place-guarded-none` | `Move(o)`: `case NoneType when p < 2:`, `case Some<R>(var v):`, `case NoneType:` | =VIB006 | VIB001,VIB006 / VIB001,VIB006 / accept | known:N20 | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-not-label` | `case not Some<R>:`, `case Some<R>(var v):` | =VIB006 | VIB001,VIB006 / VIB001,VIB006 / VIB006 | known:N20 | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-or-label` | `case NoneType or Some<R>:` | =VIB006 | VIB006 / VIB006 / VIB006 | OK | VIB006 / VIB006 / VIB006 | OK |
| `decided.linear-switch-plain-control` | `case Some<R>(var v):`, `case NoneType:` | accept | accept / accept / accept | OK | accept / accept / accept | OK |
| `decided.linear-switch-expr-discard-control` | `Some<R>(var v) => Eat(v), _ => 7` | accept | accept / accept / accept | OK | accept / accept / accept | OK |
| `decided.linear-switch-move-place-control` | `Move(o)`: `case NoneType:`, `case Some<R>(var v):`, `default:` | accept | accept / accept / accept | OK | accept / accept / accept | OK |
| `decided.linear-switch-result-plain-control` | `MkRL(p)`: `case Ok<R>(var v):`, `case Err<int>(var e):` | accept | accept / accept / accept | OK | accept / accept / accept | OK |
| `decided.linear-switch-var-whole-union-control` | `case var u: n += Take(u);` (`Take` consumes `Move(u)`) | accept | accept / accept / accept | OK | accept / accept / accept | OK |
| `decided.linear-switch-move-place-expr-var-control` | `Move(o) switch { var u => Take(u) }` | accept | accept / accept / accept | OK | accept / accept / accept | OK |
| `decided.linear-switch-expr-guarded-discard-control` | `Some<R>(var v) => Eat(v), _ when p < 2 => 3, _ => 7` | accept | accept / accept / accept | OK | accept / accept / accept | OK |

With the release-7.1 tool the matrix has 144 rows. On candidate 6 pre9, 133 are OK, 5 OK* and 6 `known:N20`, so
`matrix` exits 1 under the default `--fail-on open` and 0 under `CONF_FAIL_ON=new` (§9). The managed run and the four
ASan/LSan native runs of every control agree. The 59 probes of `probes/linear-switch/` cover the same shapes on place,
rvalue and `Move(o)` scrutinees (41 as expected, 18 waiting on N20). On candidate 6 pre13 (roslyn-linear-switch and
self-fix in) all 144 rows pass (139 OK, 5 OK*) and all 59 probes are as expected (§6.22).

## 6. Results

All runs used 3–5 workers on a shared, heavily loaded 24-core WSL2 machine (load 15–40), under the
`heavy` slot limit. "Frozen" is the Round-10 review tree. "Candidate" is the integration tree
`candidate-freeze-2` (manifest digest `e0df31e9…a8`, 1017/1017 tests), copied read-only. "Round-11 candidate 1" (r11c1)
is the integration tree after the Round-11 fixes (manifest `candidate-r11-1.sha256`, 711 files, digest
`6d535fd0…1c60`, 1572/1572 tests); §6.9 has its results. "Round-11 candidate 2" (r11c2) adds the Stage-0 N13 fix,
the self N13/N14 fix and this tool's release-3 (manifest `candidate-r11-2.sha256`, 1460 files, digest `2fa59a8f…ef29`,
1625/1625 tests); §6.10 has its results. The later freezes 3–5 differ
from freeze-2 only in documentation (`AGENTS.md`, `README.md`, `docs/ROADMAP*.md`, `docs/SPEC.md`); all 52 `src/` C#
files are identical, so the compiler results below apply to them unchanged. Every run's
`summary.json`, `curve.csv`, `report.md` and buckets are in `eng/conformance/corpus/`. The tables in §6.2–§6.4 show categories
as they were at the first delivery, when N1–N10 had status `new`. Every root of those runs is now `fixed`: the Round-10 roots, both
cuts, N1–N12 from Round-11 candidate 1 on, and N13 and N14 from Round-11 candidate 2 on (§3, §6.7). `report` renders
the corpus runs with the current statuses. So in every re-rendered
report a bucket of a fixed root shows as `regression:` (found on that tree, fixed later), with the category of the run
in brackets.

### 6.1 Oracle trust

Oracle trust was checked before any bucket was reported, on both trees.

| Check | Frozen | Candidate |
|---|---|---|
| `selfcheck`: in-process `vib-self check` vs `dotnet vib-self.dll check` (stdout, stderr, exit) | 200/200 identical | 100/100 identical |
| `selfcheck`: in-process vs process `emit-ll` bytes (programs both accept) | 80/80 identical | 42/42 identical |
| `sdk-crosscheck`: in-process Roslyn + Analyzer vs a real `dotnet build` with `Vibranium.App.props` (error/warning/VIB ID sets) | 40/40 identical | 40/40 identical |

### 6.2 Calibration on the frozen tree (recall)

Seed 2026, 1000 programs, 5 workers: 612 s, 98 programs/min. All 14 Round-10 roots (O1–O11, A1–A3) and both
cut rows were rediscovered without hard-coded repros. O2 was found through O2a; the O2b sub-shape was not
hit. 324 of 324 ordered anchor pairs were covered, and only `out.other` was never generated. The run also found
N1–N3 and N5. N4 was found only by the first frozen calibration run (programs 610 and 967); after that, the
generator stopped printing most negative `nint` literals as `(nint)-v`. This run used the classifier from
before the decided banned-construct rule (§7.1).

The last column is the like-for-like run on the candidate (same seed and budget, current classifier).

| Root | Title | Frozen: first hit (program / s) | Frozen buckets | Candidate (same seed and budget) |
|---|---|---|---|---|
| O1 | named `out _` ignores an in-scope variable `_` (self wrong output / accepts field form) | 70 / 94 | 14 | not reproduced |
| O2a | switch-section expression variables scoped to the section in self (accepts CS0128) | 367 / 268 | 1 | not reproduced |
| O2b | switch-section expression variable used across sections: self false VIB000 | not found | 0 | not reproduced |
| O3 | class const by simple name inside a static local function in a class method: self false VIB000 | 609 / 378 | 1 | not reproduced |
| O4 | embedded declaration statement accepted by self (CS1023) | 3 / 19 | 7 | not reproduced |
| O5 | Option/Result switch statements treated as exhaustive by self (accepts CS0161/CS0165/CS0163) | 349 / 243 | 1 | not reproduced |
| O6 | Stage0/Analyzer false VIB001 after exhaustive bool/relational switch statement | 80 / 92 | 9 | not reproduced |
| O7 | self false VIB001 on fieldwise-initialized plain struct | 12 / 43 | 9 | not reproduced |
| O8 | conditional collection-expression argument not target-typed by self overload resolution | 165 / 137 | 5 | not reproduced |
| O9 | self accepts rvalue arguments to ref readonly (CS9193) / explicit in (CS8156) | 19 / 38 | 7 | not reproduced |
| O10 | self does not diagnose subsumed/impossible patterns (CS8510/CS8120) | 43 / 41 | 10 | not reproduced |
| O11 | static local function directly in a switch section (now a v0.2 cut: both must reject VIB000) | 204 / 164 | 4 | not reproduced |
| A1 | dynamic float/double `%` breaks the libc-only link contract: Stage0 `vib build` fails with undefined fmod/fmodf | 62 / 94 | 4 | not reproduced |
| A1-self | A1 (same group): self `build` links every program against libm (DT_NEEDED libm.so.6 beyond the libc-only contract) | 2 / 35 | 2 | not reproduced |
| A2 | constant enum collection returned as ReadOnlySpan after Panic: self false VIB007 | 575 / 354 | 1 | not reproduced |
| A3 | `c ? [x] : [y]` parsed as null-conditional `?[` by self (false VIB008/VIB000) | 124 / 126 | 8 | not reproduced |
| N1 | NEW: self rejects a `ref` argument for a `ref readonly` (or `in`) parameter (no applicable overload) | 57 / 64 | 4 | **still reproduces**, `in` form only (warning-only CS9191; first program 57, 2 bucket(s)) |
| N2 | NEW: self treats a constant `when true` guard as a real guard (false 'switch expression must be exhaustive' / 'not all code paths return') | 46 / 60 | 2 | **still reproduces**, switch expressions only (first program 46, 2 bucket(s)) |
| N3 | NEW: Stage0 native compound assignment `x += <rhs writing x via out/ref>` reads x after the rhs (managed/self read it first) | 314 / 250 | 1 | **still reproduces** (first program 314, 1 bucket(s)) |
| N4 | NEW: self parses `(nint)-2` as a cast; C# parses a binary minus on the type name (CS0119) | not found | 0 | not reproduced |
| N5 | NEW: self skips checks inside a switch-expression arm it deems unreachable (`_` after complete Ok/Err) and accepts a non-exhaustive nested switch (CS8846/CS8509); possibly O5-family | 844 / 535 | 1 | **still reproduces** (first program 844, 1 bucket(s)) |
| CUT2 | piecewise init of a not-yet-initialized struct with owned fields (v0.2 cut: both must reject VIB000) | 77 / 82 | 14 | not reproduced |

N6–N10 (§6.7) were not hit in either 1000-program calibration. The 45-minute campaign found them, and probes
show that all of them also reproduce on the frozen tree.

### 6.3 Like-for-like calibration on the candidate

Seed 2026, 1000 programs, 3 workers: 172 s, **349 programs/min**. Validity: 496 valid, 66 warning-only,
438 invalid; 186 executed. There are 5 signatures and 6 buckets, and every bucket belongs to a fuzzer-found
root: N2 (2), N1 `in` form (2, warning-only CS9191), N3 (1) and N5 (1). **No Round-10 root and no cut reproduces
on the candidate.** The banned/cut matrix (§5.2) passes every row, and all three cut controls are accepted.

After the status change (N1–N10 `open`, the Round-10 roots and cuts `fixed`), the same run was repeated with
`--fail-on new` on the wired candidate: 1000 programs at 372 programs/min, the same 6 buckets, each attributed
`known:` to N1, N2, N3 or N5, 0 failing buckets, exit 0.

### 6.4 Candidate 45-minute campaign (seed 4242)

* 12,655 programs in the 45-minute generation window, which is **281 programs/min** at 3 workers. Validity: 6,290 valid,
  732 warning-only, 5,633 invalid; 2,413 executed (all frontends accept). Each program costs 195 ms in the frontends,
  and each executed program 752 ms in execution. All 324 ordered anchor pairs and every catalog tag of that tool version were covered.
* Draining in-flight reductions took another 29 minutes (4,430 s in total, 171 programs/min overall). The cause was
  non-terminating generated programs, whose every reduction test waited for three 20 s timeouts. The tool has been
  fixed since (below).
* 16 signatures, 49 buckets. **No Round-10 root and no cut reproduces.**
* 45 buckets belong to fuzzer-found roots: N1 (9), N2 (10), N3 (7), N5 (1), N6 (4), N7 (8), N8 (4), N9 (1) and N10 (1).
* 4 buckets (041, 044, 048, 049) are **tool artefacts**: the generated program does not terminate (for example
  `for (int i = 0; i < 3; i += 0)`, or a nested anchor that writes the loop counter). The managed reference times out,
  so the run reported `managed-crash timeout` and compared partial outputs.

Fixes made after the run, before delivery:

1. A managed timeout now marks the program as non-terminating. Its native legs are skipped, it is counted in
   `summary.json` (`nonTerminatingSkipped`), and it is not a disagreement. Replaying indexes 3001 and 6019 with the
   fixed tool shows `managed: TIMEOUT: non-terminating program, native legs skipped` and no disagreement.
2. Reductions still running 5 minutes after the campaign's time budget stop and keep their best result.
3. `report` re-attributes every bucket with the current `known-roots.json`. When the category differs from the one
   recorded during the run, it shows `current (run: recorded)`. During the run, predicates existed only for N1–N5, so
   N6–N10 and the new N2/N5 forms were recorded as `new`. Bucket 043 was recorded as `known:O6` because the O6
   `id-mismatch` predicate matched first; it is N8 (a `foreach` over `Span<T>` with a switch statement inside).
4. New AST tags `switch.guard-const` (a guard folded to a constant) and `switch.sel-unparenthesized` (an operator
   before `switch`). New predicates in `known-roots.json`: N6–N10, plus variants for N2 (`id-mismatch`, constant
   guards), N5 (`CS0165`) and N7 (runs from before the tag existed).

Roots with at least one bucket (the full table is in the corpus `report.md`):

| Root | Status | First hit (program / s) | Buckets | Title |
|---|---|---|---|---|
| N1 | new | 20 / 33 | 9 | NEW: self rejects a `ref` argument for a `ref readonly` or `in` parameter (no applicable overload); on the candidate only the `in` form (warning-only CS9191) remains |
| N2 | new | 142 / 55 | 10 | NEW: self treats a constant-true guard (`when true`, or a folded constant such as `when 0.0 >= 0.0`) as a real guard (false 'switch expression must be exhaustive' / 'not all code paths return') |
| N3 | new | 253 / 114 | 7 | NEW: Stage0 native compound assignment `x += <rhs writing x via out/ref>` reads x after the rhs (managed/self read it first) |
| N5 | new | 10331 / 2274 (re-attributed) | 1 | NEW: self skips checks inside a switch-expression arm it deems unreachable (`_` after complete Ok/Err) and accepts a non-exhaustive nested switch (CS8846/CS8509); possibly O5-family |
| N6 | new | 1374 / 240 (re-attributed) | 4 | NEW (candidate campaign): self accepts a pattern that can never match (CS8518, e.g. `x is > 0 and < 0`); residual of the O10 family |
| N7 | new | 2617 / 412 (re-attributed) | 8 | NEW (candidate campaign): self parses `a % b switch { … }` as `(a % b) switch { … }`; C# binds `switch` tighter than binary operators |
| N8 | new | 233 / 76 (re-attributed) | 4 | NEW (candidate campaign): reading a `Span<T>` local inside `foreach (var\|ref var e in span)`: Stage-0 reports VIB001 (treats foreach as a move), self VIB003; SPEC §3.2 says foreach reborrows (shared for `var`, so reading is allowed; exclusive for `ref var`, so VIB003) |
| N9 | new | 5664 / 914 (re-attributed) | 1 | NEW (candidate campaign): self ignores an always-true constant `is` pattern (`0 is > -1 and var q`) in definite assignment and reports VIB001; the program is warning-only (CS8793) |
| N10 | new | 9981 / 2227 (re-attributed) | 1 | NEW (candidate campaign): self accepts a non-constant `case` label or pattern (`case k:` with a non-const local `k`; CS9135/CS0150) |

**Discovery curve (convergence metric).** This is the curve as recorded during the run. The "Unattributed" column
uses the run-time predicates (N1–N5 only):

| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |
|---|---|---|---|---|---|
| ≤5 | 1867 | 7 | 14 | 14 | 4 |
| ≤10 | 3596 | 10 | 25 | 25 | 9 |
| ≤15 | 5209 | 10 | 33 | 33 | 15 |
| ≤20 | 6211 | 12 | 35 | 35 | 17 |
| ≤30 | 8295 | 13 | 41 | 41 | 23 |
| ≤35 | 9354 | 14 | 43 | 42 | 23 |
| ≤40 | 10362 | 16 | 46 | 45 | 26 |
| ≤45 | 12287 | 16 | 47 | 46 | 26 |
| ≤70 | 12655 | 16 | 48 | 47 | 27 |
| ≤75 | 12655 | 16 | 49 | 48 | 28 |

The same buckets re-attributed with the final `known-roots.json`, counting distinct roots. "Programs" is the
count at the last bucket event in the window:

| Minute | Programs | Roots with a bucket (newly added) | Root buckets | Tool-artefact buckets |
|---|---|---|---|---|
| ≤5 | 1867 | 5 (N1, N2, N3, N6, N8) | 14 | 0 |
| ≤10 | 3596 | 6 (N7) | 25 | 0 |
| ≤15 | 5209 | 6 (–) | 33 | 0 |
| ≤20 | 6211 | 7 (N9) | 35 | 0 |
| ≤25 | 6211 | 7 (–) | 35 | 0 |
| ≤30 | 8295 | 7 (–) | 40 | 1 |
| ≤35 | 9354 | 7 (–) | 42 | 1 |
| ≤40 | 10362 | 9 (N5, N10) | 44 | 2 |
| ≤45 | 12287 | 9 (–) | 45 | 2 |
| ≤74 (drain) | 12655 | 9 (–) | 45 | 4 |

The curve is **not flat yet**. Signatures and roots grew quickly in the first 10 minutes (6 of the 9 roots).
They grew again at minutes 15–20 (N9) and 35–40 (N10 and the `CS0165` form of N5), and the last two were single hits.
Longer campaigns should be expected to find more single-hit roots. Most roots found so far are in self's front end:
parsing (N7), overload resolution (N1), constant folding (N2, N9) and pattern checks (N5, N6, N10). The others are
one Stage-0 code-generation root (N3) and one Stage-0/self borrow-model disagreement (N8).

All buckets:

| Bucket | Category | Signature | Hits | First program | min.cs lines |
|---|---|---|---|---|---|
| 001-3683251e3c | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 20 | 9 |
| 002-0519758768 | new:N2 | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 142 | 9 |
| 003-6ce551d71a | new:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 177 | 17 |
| 004-116f4d3e66 | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable method overload on enclosing type in vibranium. | 1 | 235 | 11 |
| 005-8abb80c05b | new:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 233 | 13 |
| 006-d54ce4d693 | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 302 | 14 |
| 007-64c6908691 | new:N2 | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 378 | 10 |
| 008-354c085b8d | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 253 | 12 |
| 009-fbc01502e5 | new:N2 | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 393 | 15 |
| 010-3eec6bcee3 | new:N2 | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 476 | 11 |
| 011-09712d394d | new:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 1374 | 9 |
| 012-6c7cd0dcf8 | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 1501 | 15 |
| 013-38a3fd867f | new:N2 (run: new) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 1737 | 10 |
| 014-ab1b789aaf | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 1762 | 12 |
| 015-dfbfc3688b | new:N2 (run: new) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 1921 | 15 |
| 016-9222bc6dcf | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 2340 | 14 |
| 017-4a90cc7cf7 | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 2230 | 12 |
| 018-4f822cc3f6 | new:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 2617 | 9 |
| 019-1dafc6baec | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 2473 | 15 |
| 020-d4f47f29ce | new:N7 (run: new) | invalid-accepted \| self \| CS1503 \|  | 1 | 2633 | 22 |
| 021-b2d4737737 | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 2873 | 15 |
| 022-e8f062f2c6 | new:N1 | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 3193 | 12 |
| 023-45ceb18582 | new:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 3370 | 19 |
| 024-f2eeef5c2f | new:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 3489 | 14 |
| 025-69462bf092 | new:N1 | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 3574 | 13 |
| 026-8268e2b25f | new:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 3950 | 18 |
| 027-aaa19d665c | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 3897 | 15 |
| 028-5598833815 | new:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 4370 | 12 |
| 029-a0fef00b51 | new:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 4504 | 12 |
| 030-7dc2933c8c | new:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 4497 | 15 |
| 031-53bb3a9009 | new:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 4792 | 11 |
| 032-fbd229b543 | new:N1 | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 4838 | 16 |
| 033-e15cbdaca6 | new:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 5181 | 13 |
| 034-8ec95c7221 | new:N9 (run: new) | accept-reject \| stage0-accepts \| self:VIB001 \| c# requires this variable or struct field to be definitely assigned before it is read (csn | 1 | 5664 | 10 |
| 035-9495c7fd7c | new:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 5669 | 10 |
| 036-632e89decf | new:N7 (run: new) | invalid-accepted \| self \| CS0266,CS1503,CS8422 \|  | 1 | 7614 | 10 |
| 037-24a0edd589 | new:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 7625 | 9 |
| 038-9d3d686b41 | new:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 7934 | 9 |
| 039-0deb14406c | new:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 8019 | 8 |
| 040-234e98f74d | new:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 8177 | 14 |
| 041-ce9f24f50c | new | managed-crash \| managed \| timeout \|  | 1 | 3001 | 13 |
| 042-e9ecfef67c | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0,stage0-O3-asan | 1 | 8690 | 12 |
| 043-c65b3b8e5c | new:N8 (run: known:O6) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 9353 | 12 |
| 044-7c6428b033 | new | output-mismatch \| stage0+self \| stdout \| stage0-O0,self-O0 | 1 | 6019 | 15 |
| 045-857cb11cc4 | new:N10 (run: new) | invalid-accepted \| self \| CS9135 \|  | 1 | 9981 | 21 |
| 046-5c1f2e2a42 | new:N5 (run: new) | invalid-accepted \| self \| CS0165 \|  | 1 | 10331 | 10 |
| 047-34e7f579ee | new:N3 | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 12193 | 12 |
| 048-ebd0e76a56 | new | output-mismatch \| stage0+self \| stdout \| stage0-O0,self-O0 | 1 | 12369 | 12 |
| 049-ded166c421 | new | managed-crash \| managed \| timeout \|  | 1 | 8764 | 14 |

### 6.5 CI mode

`CONF_SKIP_BUILD=1 CONF_WORKERS=3 CONF_FAIL_ON=new eng/conformance/run-conformance.sh ci` (seed 1, 300 programs) on
the candidate took 50 s (357 programs/min), produced 0 buckets and exited 0. 300 of 324 anchor pairs were covered. In
seeds 2026 and 4242 the first N-root hits came at programs 20–142, so a 300-program gate catches the frequent roots
on some seeds and not on others. Use the campaign mode for discovery.

### 6.6 Warning-only programs

Warning-only programs are valid C# with a warning that `TreatWarningsAsErrors` turns into a build error. They are
compared like valid programs (§1). In the campaign:

| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |
|---|---|---|---|---|---|
| CS0162 | 469 | 343 | 343 | 335 | 22 |
| CS0168 | 115 | 82 | 82 | 81 | 5 |
| CS0219 | 81 | 59 | 59 | 57 | 186 |
| CS9191 | 49 | 36 | 36 | 0 | 20 |
| CS8321 | 27 | 20 | 20 | 20 | 356 |
| CS8793 | 8 | 6 | 6 | 5 | 1968 |
| CS8519 | 5 | 4 | 4 | 4 | 1197 |
| CS1522 | 5 | 4 | 4 | 4 | 1334 |
| CS1718 | 5 | 3 | 3 | 3 | 3465 |
| CS1717 | 4 | 4 | 4 | 4 | 6044 |
| CS8520 | 3 | 2 | 2 | 1 | 380 |
| CS9195 | 2 | 0 | 0 | 0 | 9662 |

* **CS9191** (`ref` argument for an `in` parameter) is the remaining N1 form: self rejects all 36 analyzer-clean
  programs, and Stage-0 accepts them.
* **CS8793** (an `is` pattern that always matches) is N9: self rejects exactly 1 of the 6 analyzer-clean programs,
  and that is program 5664, which became bucket 034.
* **CS9192 no longer appears.** The candidate's `Vibranium.App.props` promotes it to an error, and the oracle reads
  that list from the target tree.
* Other warnings also have a few self rejections: 8 of 343 CS0162 programs, 1 of 82 CS0168, 2 of 59 CS0219 and 1 of 2
  CS8520. Each of them carries one of the `accept-reject` signatures above, which belong to N1, N2 and N9. A different
  root hiding behind the same coarse signature cannot be excluded (§8).
* Whether semantic warnings such as CS9191, CS8793 and CS8794/CS8520 should be promoted like CS9192 is a question for
  the owner. *Decided (§7.5): they are being promoted to errors, together with CS0183, CS0184 and CS8519.*

### 6.7 New roots found by the fuzzer (N1–N14)

Each root was vetted with standalone probes (`corpus/probes/`, with outputs on each tree). Roslyn and the Analyzer are clean unless stated otherwise. "Candidate" is the integration tree (freeze manifest digest `e0df31e9…a8`). N11 and N12 were found by the extended-profile campaign on that tree (§6.8). **N1–N12 are all fixed on Round-11 candidate 1** (§6.9): every probe below now gives the expected result (`*.r11c1.txt`), and the predicates in `known-roots.json` have status `fixed`. N13 and N14 were found on Round-11 candidate 1 (N13 follows a rule decided after it was found, §7.11). **Both are fixed on Round-11 candidate 2** (§6.10): every N13 and N14 probe gives the expected result there (`*.r11c2.txt`), and both have status `fixed`.

**N1: self rejects a `ref` argument for a `ref readonly` / `in` parameter.** *Candidate: fixed for `ref readonly`; the `in` form still reproduces, but that program is warning-only (CS9191).*

```csharp
int x = 1;
WriteLine(Id(ref x));
static int Id(ref readonly int p) => p;
```

* Frozen: Stage-0 accepts. Self reports `VIB000 Unsupported, unresolved, or inapplicable method call` (in a class method: `No applicable method overload on enclosing type`).
* Candidate: all accept and all three runs print `1`.
* `G(ref x)` with `static int G(in int p)` (`m1_ref_to_in`): CS9191 warning; Stage-0 accepts; self reports `VIB000 No applicable local function overload`.

**N2: self treats a constant `when true` guard as a real guard.** *Candidate: fixed for switch statements; still reproduces for switch expressions.*

```csharp
WriteLine(F(3));
static int F(int a)
{
    int s = a switch { < 0 => 0, >= 0 when true => 1 };
    return s;
}
```

* Roslyn valid, Stage-0 accepts. Self reports `VIB000 Switch expression must be exhaustive (include a '_' or 'var' catch-all arm)` on both trees (and the same for `b switch { true => 0, false when true => 1 }`).
* The statement form `case >= 0 when true: return 1;` gave self `VIB000 Not all code paths return a value` on the frozen tree; on the candidate all accept and print `1`.

**N3: Stage-0 native compound assignment with a conditional right-hand side re-reads its target after the right-hand side.** *Candidate: still reproduces.*

```csharp
WriteLine(F(0));
static int F(int a)
{
    a += Set(out a) > 0 ? 0 : 0;
    return a;
}
static int Set(out int p)
{
    p = 5;
    return 1;
}
```

* Managed `0`, self native `0`, Stage-0 native `5`, at `-O0`.
* `a += c ? Set(out a) : 0` (with `c` true): managed `1`, self `1`, Stage-0 `6`.
* Controls that agree: plain `a += Set(out a)`, `a += Bump(ref a)`, `s.A += Set(out s.A)` and `a = a + (Set(out a) > 0 ? 0 : 0)`.
* This violates SPEC §5.2 (the left-hand side is read first in C# order) and the AGENTS.md rule 3 output parity.

**N4: self accepts `(nint)-2`.** *Candidate: still reproduces (probe `n1_nint_neg_cast`).*

```csharp
WriteLine(F(1));
static int F(int k)
{
    nint w = (nint)k;
    WriteLine(w / (nint)-2);
    return 0;
}
```

* C# parses `(nint)-2` as a binary minus on the type name (`nint` is a contextual keyword) and reports `CS0119 'nint' is a type, which is not valid in the given context`. Stage-0 reports CS0119. Self accepts.
* Found on the frozen tree by the first calibration run (programs 610 and 967). The generator now prints negative `nint` literals as `(nint)(-2)` 85 % of the time, so the like-for-like runs hit it rarely.

**N5: self skips a switch-expression arm it deems unreachable.** *Candidate: still reproduces.*

```csharp
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var _ = 0;
    long x1 = 0L;
    var s5 = Try2(0) switch { Ok<int>(var v3) => 0, Err<int>(var e4) => 0, _ => x1 switch { < 0L => 0, >= 0L when false => 0 } };
    WriteLine(_);
    return acc;
}
static Result<int, int> Try2(int k) => Ok(0);
```

* Roslyn and Stage-0 report `CS8846` for the nested switch in the `_` arm. Self accepts.
* The same nested switch is rejected by self everywhere else: standalone, inside an `int` switch, inside a union switch without complete arms, and inside an `Ok` arm (`s1`–`s3`, `t1`–`t4`).
* Self apparently treats `_` after complete `Ok`/`Err` arms as dead code and does not check it. This is possibly the switch-expression twin of O5. *Decided (§7.7): every arm is checked, so this is a self bug.*
The candidate campaign (§6.4) found five more roots, N6–N10, and new forms of N2 and N5. Each one was vetted with standalone probes on both trees. **All of them also reproduce on the frozen tree**, so none is a regression of the integration. They were not hit by the 1000-program calibrations, only by the 12,655-program campaign.

**N1 (campaign).** All nine N1 buckets of the campaign are the warning-only `in` form (CS9191: 49 warning-only programs, which self rejects every time). No `ref readonly` form reproduces on the candidate.

**N2, more forms (campaign).** A guard that is a folded constant behaves like `when true`. With `x switch { < 0 => 0, >= 0 when 1 > 0 => 1 }` (`cg2`) or `… when 0.0 >= 0.0 …` (`cg1`), Roslyn and Stage-0 accept and self reports `VIB000 Switch expression must be exhaustive`. Several campaign buckets show N2 only as an extra self `VIB000` next to an error that both frontends report (`id-mismatch`).

**N5, more forms (campaign).** Self also skips definite assignment inside the `_` arm after complete `Some`/`None` arms (`u2_n5_unassigned_in_discard_arm`):

```csharp
WriteLine(F());
static int F()
{
    int y;
    return Find(0) switch { Some<int>(var v) => v, NoneType => 0, _ => y };
}
static Option<int> Find(int k) => Some(k);
```

* Roslyn and Stage-0 report `CS0165 Use of unassigned local variable 'y'`. Self accepts.

**N6: self accepts a pattern that can never match.** *Candidate: reproduces. Residual of the O10 family (O10 itself no longer reproduces).*

```csharp
WriteLine(F(1));
static int F(int x)
{
    return x is > 0 and < 0 ? 1 : 2;
}
```

* Roslyn and Stage-0 report `CS8518 An expression of type 'int' can never match the provided pattern` (an error). Self accepts (`pn1`).
* The always-matching twin `x is > 0 or <= 0` (`pn2`) is warning-only (CS8794). All frontends accept it, and all three runs print `1`.

**N7: self binds an operator before `switch` the wrong way, and the output changes.** *Candidate: reproduces. Silent wrong code.*

```csharp
WriteLine(F(7, 4));
static int F(int x, int y)
{
    return x % y switch { 4 => 1, _ => 2 };
}
```

* In C# the `switch` expression binds tighter than the multiplicative and additive operators, so this is `x % (y switch { … })` = `7 % 1` = `0`. Managed and Stage-0 native print `0`. Self native prints `2`, which means it computed `(x % y) switch { … }` (`pr1`).
* `x + y switch { 4 => 10, _ => 20 }` with `F(1, 4)` (`pr2`): managed and Stage-0 print `11`, self prints `20`.
* A unary operator is handled correctly. `-x switch { -4 => 1, _ => 2 }` (`pr3`) prints `1` everywhere.
* When the misparse changes the type, self accepts C#-invalid programs. `int s = 0.0 % 0.0 switch { _ => 0 };` (`pr4`) gets `CS0266` from Roslyn and Stage-0. This is how the campaign found it, 11 hits in 8 buckets.
* This violates AGENTS.md rule 3 (output parity) for valid programs.

**N8: `foreach` over a `Span<T>` local or parameter.** *Candidate: reproduces. The frontends disagree with each other and with SPEC §3.2.*

```csharp
WriteLine(F());
static int F()
{
    Span<int> s = [1, 2];
    int n = 0;
    foreach (var e in s)
    {
        n += e + s.Length;
    }
    return n;
}
```

* SPEC §3.2(1) says a `Span<T>` "iterated via `foreach`" is implicitly reborrowed, not moved. The §3.2 table says a `var` loop is a **shared** reborrow, so reading `s.Length` or `s[0]` in the body should be accepted. C# prints `7`, as the `ReadOnlySpan<int>` control `sp4` shows.
* Stage-0 and the Analyzer report `VIB001 Use of moved value 's'`: they treat the loop as a move (`sp1`, and `sp2` for `s[0]`). Self reports `VIB003 Borrow conflicts with an active borrow`: it takes an exclusive loan.
* After the loop (`sp3`: `foreach (var e in s) { n += e; } n += s.Length;`), Stage-0 still reports `VIB001`. Self accepts. This is an `accept-reject` disagreement on a program that SPEC says is valid.
* Writing in the body (`sp5`: `s[0] = e;`) is correctly rejected by self with `VIB003`. Stage-0 reports `VIB001` instead.
* With a `Span<int>` parameter (`sp6`), Stage-0 reports `VIB001` and `VIB003` ("exclusively borrowed by `__enum_span_0`"), and self reports `VIB003`.
* `ReadOnlySpan<int>` (`sp4`) is accepted everywhere and prints `7`.
* *Decided (§7.4): SPEC stands.* A `var` loop is a shared reborrow, so `sp1`, `sp2`, `sp3` and `sp6` are valid, and `sp5` (a write in the body) is `VIB003`. Stage-0 is wrong on every probe; self is wrong on `sp1`, `sp2` and `sp6`.

**N9: self ignores an always-true constant pattern in definite assignment.** *Candidate: reproduces. Low severity: the program is warning-only.*

```csharp
WriteLine(F());
static int F()
{
    if (0 is > -1 and var q) { }
    return q;
}
```

* Roslyn warns `CS8793 The given expression always matches the provided pattern`, which `TreatWarningsAsErrors` turns into a build error. It still treats `q` as definitely assigned, and Stage-0 accepts. Self reports `VIB001 … must be definitely assigned (CS0165/CS0170)` (`da1`).
* With a non-constant input (`da2`: `x is > -1 and var q`), every frontend rejects.
* This is probably the same constant-folding gap as N2.

**N10: self accepts a non-constant `case` label or pattern.** *Candidate: reproduces.*

```csharp
WriteLine(F(1));
static int F(int x)
{
    int k = 1;
    switch (x)
    {
        case k: return 1;
        default: return 2;
    }
}
```

* Roslyn and Stage-0 report `CS9135 A constant value of type 'int' is expected`. Self accepts (`cl1`), and does the same for the switch-expression arm `x switch { k => 1, _ => 2 }` (`cl2`).
* With `const int k = 1;` (`cl3`), all frontends accept, and all three runs print `1`.

**N11: self checks the end point of a switch section that Roslyn proves unreachable.** *Found on freeze-2 by the extended campaign (bucket 026; also 057 on a `char` selector). Fixed in Round-11 candidate 1.*

```csharp
WriteLine(F(3));
static int F(ushort x)
{
    int r = 0;
    switch (x)
    {
        case >= (ushort)0: r = 1; break;
        default: WriteLine(2);
    }
    return r;
}
```

* Roslyn proves the `default:` section unreachable (the earlier cases cover every value) and reports only warning CS0162, so it does not check the section's end point. Stage-0 and the Analyzer accept, and all runs print `1`.
* Self reported `VIB000 C# switch section endpoints must not be reachable … (CS0163/CS8070)` (`x1_ushort_default_unreachable`; the same for int `>= 0`/`< 0` in `x2_int_exhaustive_default_fallout` and bool `true`/`false` in `x2_bool_exhaustive_default_fallout`).
* Controls: the same `default` with `break` (`x2_ushort_default_break_control`), `case int:` before the `default` (`x2_int_discard_case_fallout`), and exhaustive relational switches without a `default` (`x1_*_return`, `x1_ushort_ge0_da`) were already accepted by self.

**N12: self accepts a `ref` argument that names a readonly variable.** *Found on freeze-2 by the extended campaign (buckets 067, 089). Fixed in Round-11 candidate 1.*

```csharp
WriteLine(F());
static int F()
{
    int x = 1;
    ref readonly int r = ref x;
    return M(ref r, 2);
}
static int M(ref readonly int p, int k) => p + k;
```

* Roslyn and Stage-0 report `CS1510 A ref or out value must be an assignable variable` (`y1_ref_arg_readonly_local_to_ref_readonly`). Forwarding an `in` or `ref readonly` parameter with `ref` gives `CS8329` (`y1_ref_arg_in_param_forward`, `y1_ref_arg_readonly_param_forward`).
* Self accepted all three. For a plain `ref` parameter it already rejected (`VIB004`, `y1_ref_arg_readonly_local_to_ref`). Sibling of O9.
* Controls: `in r`, and `ref r` from a `ref int` local, are accepted everywhere with equal output.

**N13: Stage-0 and the Analyzer run flow checks in a `default:` section that Roslyn proves unreachable.** *Found on Round-11 candidate 1 by the extended campaign (5 buckets, §6.9). Decided rule §7.11 (option A). Fixed in Round-11 candidate 2.*

```csharp
WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case < 0: return 1;
        case >= 0: return 2;
        default:
            Vec<int> v = [1];
            var w = v;
            return v.Count;
    }
}
```

* Roslyn reports only warning CS0162 for the `default:` section. Self accepts. Stage-0 and the Analyzer report `VIB001 Use of moved value 'v'` (`z1`; a `bool` selector in `z2`, `ushort` in `z9`). Two `o.AsMut()` arguments in one call there give `VIB002`/`VIB003` (`z3`).
* Definite assignment is affected too. In buckets 003 and 004 a local is assigned in every reachable section, and Stage-0 reports `VIB001 Use of uninitialized value` after the switch because the unreachable `default:` does not assign it.
* On freeze-2 self also checked the section, so no frontend disagreed. Self's N11 fix made it follow Roslyn reachability, which exposed the split. The owner decided that every frontend follows Roslyn (§7.11).
* Other unreachable code is not flow-checked by any frontend: a `when false` section (`z4`), code after `return` (`z5`), `if (false)` (`z6`) and code after an unconditional Panic (`z8`). Control: with `case > 0` the `default:` is reachable and every frontend reports VIB001 (`z7`).

**N14: self cannot resolve a union-pattern variable in a multi-label section whose other label is `when false`.** *Found on Round-11 candidate 1 by the default campaign (bucket 001, §6.9). Also on freeze-2. Fixed in Round-11 candidate 2.*

```csharp
Result<int, int> r = Err(7);
WriteLine(F(r));
static int F(Result<int, int> x)
{
    switch (x)
    {
        case Ok<int>(var v) when false:
        case Err<int>(var e): return e;
        default: return 0;
    }
}
```

* Roslyn, the Analyzer and Stage-0 accept. Self reports `VIB000 Unresolved or captured identifier` at the use of `e` (`q1`; with `WriteLine` in the section `q7`; the labels swapped `q9`; `Option<int>` with `Some<int>(var v)` `q8`; the minimized bucket `q0`).
* Controls accepted by self: an `int` selector with `case 1 when false:` and `case var n:` (`q2`), a folded constant-false guard (`q3`), a single label (`q4`), two labels without pattern variables (`q5`) and a pattern variable in the live label of an `int` switch (`q6`). So the trigger is a union deconstruction pattern variable together with a `when false` label in the same section.

### 6.8 Extended-profile campaign on the candidate (seed 9090, 90 minutes)

`--profile extended`, seed 9090, 90 minutes, 3 workers, on `candidate-freeze-2`: **39,863 programs at 443
programs/min**. Validity: 24,275 valid, 1,969 warning-only, 13,619 invalid; 7,088 executed (8 skipped as
non-terminating). All 205 ordered pairs of the extended schedule were covered, every catalog tag was generated
(each of the 103 extended tags in at least 125 programs), and the run produced 26 signatures and 100 buckets
(`corpus/candidate-extended-9090/`).

| Root | Buckets | First hit (program / s) | Note |
|---|---|---|---|
| N8 | 47 | 4 / 33 | `foreach (ref var e in span)` is in about a third of the `SpanSlice` programs, so N8 dominated. Four more predicates were added for its extended shapes (Stage-0 `VIB001`/`VIB003` on the owning `Vec`, self-only `VIB003`) |
| N2 | 10 | 238 / 100 | |
| N3 | 9 | 445 / 195 | |
| N1 | 8 | 197 / 77 | `in` form only (CS9191, 79 warning-only programs) |
| N6, N7 | 6 each | 3109 / 636, 2630 / 521 | |
| N4 | 3 | 3909 / 800 | one bucket shows Roslyn's CS0075 for `(nint)-2`; a predicate was added |
| N10 | 2 | 12481 / 1768 | |
| **N11** (new) | 2 | 987 / 286 | §6.7; routed to `/source-validity` |
| **N12** (new) | 2 | 18649 / 2562 | §6.7; routed to `/source-validity` |
| CUT2 | 1 | 8773 / 1399 | tool artefact, see below |
| none | 4 | 20809 | tool artefact, see below |

N5 and N9 were not hit. **Tool artefacts, fixed in the tool after the run:**

* *Cut shape in dead code* (bucket 054, `regression:CUT2`). The piecewise owned-struct init sat in a `case … when
  false:` section. Every frontend accepts it, because they all apply the cut in their flow analysis, and so does a
  cut shape after `return`, in `if (false)` or after an unconditional Panic (probes `c2u_*`). The same shape in
  reachable code is rejected with VIB000 everywhere (`c2u_reachable_section`). The tagger now emits no `cut.*` tag
  for code that Roslyn proves unreachable or that follows an unconditional Panic (§7.10).
* *Unbounded recursion* (buckets 073/075 and 091/094: one program each, two signatures). A generated local function
  calls itself, so the managed run ends with a CLR stack overflow (exit 134) and the native runs with SIGSEGV
  (exit 139). SPEC excludes call-stack exhaustion from the parity guarantee, so the engine now skips such a program
  like a non-terminating one.

**Near-miss acceptance.** Each `vib-invalid` tag was carried by 206–1,831 programs. Every frontend accepted 0–3 of
them per tag (25 program–tag pairs in all). Replaying the first accepted program of each of the 15 tags concerned showed the
same cause every time: the near-miss line sat in code that every frontend treats as unreachable. That was after a
Panic on every path (for example a constant `switch` whose only section ends in `Panic`), after a constant-true `if`
that returns, or in a `when false` section. No frontend accepted a reachable near-miss.

**Curve.** New roots (N11, N12 and the artefacts) appeared at minutes 30–75, and the unattributed count was still
rising at minute 75 (15 by the end). The extended profile had not converged after 90 minutes.

### 6.9 Round-11 candidate 1

Round-11 candidate 1 (r11c1) contains the fixes for N1–N12, cut C4 and the seven warning promotions (§7). Every run
below used this delivery's tool with 3 workers; the two campaigns ran at the same time.

* **Gate.** `Vibranium.slnx` (with the tool) builds with 0 warnings and 0 errors, and so do the `Release` builds of
  `vib-self` and the tool.
* **Matrix (§5.2).** 49 rows: 41 OK, 5 OK* and 3 violations. The violations are the three N13 decided rows. Every
  banned, cut (all eight C4 rows included), control and N8 row passes.
* **Replay.** The four stored corpus programs regenerated from `(seed, index)` are identical to the originals, so the
  extended-profile work did not change the default profile.
* **CI.** `run-conformance.sh ci` with `--fail-on open` (seed 1, 300 programs): 60 s (299 programs/min), 0 signatures,
  0 buckets, exit 0 (`corpus/r11c1-ci-seed1/`). The same mode with `--profile extended` was also clean.
* **Probes.** Every probe in `corpus/probes/` has its r11c1 output (`*.r11c1.txt`). The N1–N12 probes, the C4 rows and
  the dead-code cut probes give the expected results. The N13 (`z*`) and N14 (`q*`) probes reproduce as in §6.7.

**Extended campaign** (seed 9090, 90 minutes). This is the seed of §6.8, so each index is the same program on both
trees. **41,979 programs at 465.9 programs/min.** Validity: 25,523 valid, 1,987 warning-only, 14,469 invalid; 7,888
executed (11 skipped as non-terminating or call-stack overflow). All 205 ordered pairs were covered and every catalog
tag was generated. There were **3 signatures and 5 buckets, all N13** (`corpus/r11c1-extended-9090/`):

| Bucket | First hit (program / s) | Stage-0 and Analyzer report, in a `default:` that Roslyn proves unreachable |
|---|---|---|
| 001 | 987 / 170 | two `AsMut()` borrows in one call (`ushort` selector): VIB002/VIB003 |
| 002 | 1651 / 270 | use of a moved `Span<int>` (`int` selector): VIB001 |
| 003 | 20787 / 2754 | a local assigned in every reachable section is "uninitialized" after the switch (`byte`): VIB001 |
| 004 | 22749 / 3000 | the same with a `bool` selector |
| 005 | 40601 / 5213 | use after move (`uint` selector): VIB001 |

None of the roots found on freeze-2 in §6.8 reappears (100 buckets and 26 signatures there). The run recorded the
buckets as `new` or `regression:O6`, because O6's predicate also matches "self accepts, Stage-0 VIB001". The N13
predicates now come before O6, so `report` attributes all five to N13 and shows them as `regression:N13` (N13 is
`fixed`, but the fix is not in r11c1). **Near-miss acceptance** is unchanged from §6.8: the 22 `vib-invalid` tags were
carried by 218–1,916 programs each. Every frontend accepted 26 program–tag pairs in 15 tags, and the first accepted
program of each tag had its near-miss line in dead code, as on freeze-2.

**Default campaign** (seed 4545, 45 minutes): **22,361 programs at 496.8 programs/min.** Validity: 11,125 valid, 1,141
warning-only, 10,095 invalid; 4,300 executed (9 skipped). All 324 pairs were covered and every tag was generated. There
was **1 signature and 1 bucket, N14**, first hit at program 17,201 (2,137 s; `corpus/r11c1-default-4545/`). The run
recorded it as `regression:O2b`, whose predicate matches the same self message. The N14 predicate now comes before
O2b.

**Curve.** Extended: buckets at 170 s, 270 s, 2,754 s, 3,000 s and 5,213 s, all of one root, with new shapes (definite
assignment, other selector types) still appearing at minute 87. Default: the only bucket appeared at minute 36. The
fixed tree is much quieter than freeze-2, but each campaign still found a new root (N13 in minute 3, N14 in minute 36),
so discovery has not converged.

### 6.10 Round-11 candidate 2: cycle 3 (convergence)

Round-11 candidate 2 (r11c2) adds the Stage-0 N13 fix and the self N13/N14 fix to Round-11 candidate 1. Cycle 3 ran two
campaigns with new seeds at the same time, 3 workers each, with the release-3 tool. The convergence criterion was that
both campaigns finish with no bucket outside the fixed roots.

* **Gate (final tool).** `Vibranium.slnx` builds with 0 warnings and 0 errors, as does the `Release` build of the tool.
  The matrix (§5.2) passes all 49 rows (44 OK, 5 OK*). The four stored corpus programs replay identically. CI
  (seed 1 × 300, `--fail-on open`) found 0 buckets and exited 0 (`corpus/r11c2-ci-seed1/`).
* **Probes.** Every N13 (`z*`) and N14 (`q*`) probe is accepted or rejected alike by every frontend, with equal output
  (`*.r11c2.txt`).

| Campaign | Programs (per min) | Valid / warning-only / invalid | Executed (skipped) | Pairs | Signatures | Buckets |
|---|---|---|---|---|---|---|
| extended, seed 7373, 120 min (`corpus/r11c2-extended-7373/`) | 77,475 (645.6) | 46,417 / 3,607 / 27,451 | 14,534 (14) | 205/205 | 0 | 0 |
| default, seed 6161, 60 min (`corpus/r11c2-default-6161/`) | 35,950 (599.1) | 18,065 / 1,819 / 16,066 | 7,090 (20) | 324/324 | 1 | 1 |

Every catalog tag was generated in both campaigns. **No frontend disagreement was found.** The only bucket was a tool
artefact:

* **Default bucket 001 (program 28,828, minute 49, recorded as `regression:CUT2`).** The piecewise owned-struct init
  sat in `case E.A:` of `switch (E.C)`. The selector is a constant, so Roslyn proves the section unreachable (CS0162),
  and all three frontends accept it under the decided rule (§7.10). The tagger did not know this shape of dead code,
  so it tagged the cut. It now evaluates constant selectors (§8). Probes `k1`, `k3`, `k5` and `k6` (an enum, a const
  local, a relational label and an unreachable `default:`) are accepted everywhere with equal output. The controls
  `k2`, `k4` and `k7` (matching label, variable selector) are rejected everywhere with VIB000. With the fixed tagger
  the program is compared normally, and all three runs agree (exit 101, the same panic).

**Near-miss acceptance (extended).** The 22 `vib-invalid` tags were carried by 359–3,382 programs each. Every frontend
accepted 36 program–tag pairs in 15 tags. Replaying the first accepted program of each tag (14 programs) showed the
near-miss line in dead code every time:
* after a constant-true condition that panics (`if (4.25 != -2.25) Panic(…)`, `if (!(-1 == 8)) Panic(…)`);
* after a switch whose only live sections panic or return;
* in a `when false` section, an unreachable `default:`, or a section of a switch on a constant that no label matches.

No frontend accepted a reachable near-miss.

**Curve.** The extended campaign recorded no signature in 120 minutes. The default campaign recorded one, the artefact
above, at minute 49. Compared with Round-11 candidate 1 (§6.9: N13 in minute 3 and N14 in minute 36 with fewer
programs), **cycle 3 meets the convergence criterion**. Both campaigns finished with no bucket outside the fixed roots,
and no compiler disagreement at all, across 113,425 programs (21,624 executed).

### 6.11 Round-11 candidate 3: activation profile recall

Round-11 candidate 3 (r11c3) is the tree the Round-11 audit (Opus and Astra repros, roots O11-1 to O11-14) was run
against; its conformance tool is release-4. The activation profile (§2) was written from the audit's root
descriptions and the call-activation model, not from its repro files, and the question was whether it rediscovers
the audit roots, in particular O11-1 to O11-4, and how fast. The O11 predicates in `known-roots.json` were written
from the ledger descriptions before the run (status `open` then; release-5 lists them as `fixed`, §3, so the stored
`report.md` shows `regression:O11-…` where the run recorded `known:O11-…`).

* **Gate.** `Vibranium.slnx` builds with 0 warnings and 0 errors, as does the `Release` build of the tool. Four default and two extended corpus programs replay
  identically, so the default and extended profiles are unchanged. The matrix (§5.2, 64 rows) gives 48 OK, 8 OK* and
  8 violations, the 8 C5–C9 cut rows that candidate 3 does not implement yet (O11-8a, O11-8b, O11-9).
* **Smoke run** (seed 8282, 200 programs, 4 minutes): 71 buckets; O11-1 to O11-13 were all hit, O11-1 with the first
  program.
* **Recall run** (seed 8181, 60 minutes, 3 workers, `corpus/r11c3-activation-8181/`): 13,484 programs (224.7 per
  minute; 8,155 valid, 680 warning-only, 4,649 invalid; 1,639 executed, 3 non-terminating skipped), all 336 ordered
  pairs, every catalog tag generated, 228 signatures and 673 buckets.
* **Activation CI gate** (seed 1 × 300, `--fail-on open`, `corpus/r11c3-ci-activation-seed1/`): 90 buckets, all of
  open O11 roots, so the gate exits 1 as it should on this tree.

**Time to first hit** (program index / seconds since start, from `summary.json`; the first program finishes after
about 19 s of start-up):

| Root | First hit | Buckets | How it shows |
|---|---|---|---|
| O11-1 | 0 / 19 s | 146 | `spec-accepted` for `ca.borrow-then-move`; native output mismatches and ASan heap-use-after-free on the same programs |
| O11-2 | 20 / 23 s | 17 | self accepts, Stage-0 VIB003, for a two-phase loan (`[Mut]` receiver, `ref`, `out`, assignment target) |
| O11-3 | 28 / 37 s | 81 | self accepts, Stage-0 VIB007, for a violated `LifetimeOf`/elision; or self lacks that VIB007 |
| O11-4 | 39 / 49 s | 12 | self native output differs for out-of-range or special float constants converted to `nint`/`nuint` |
| O11-9 | 4 / 26 s | 47 | C5 accepted by every frontend; native output differs under `#if DEBUG` |
| O11-13 | 21 / 26 s | 177 | `analyzer:{}` against Stage-0 under `// <auto-generated/>` or the `Program.g.cs` file name |
| O11-12 | 26 / 32 s | 10 | appending to `/dev/stderr` or `/dev/stdout` (pipes under the harness): the managed run prints the IoError kind `Other`, both native runs append and print `ok` |
| O11-6 | 27 / 36 s | 24 | self VIB003 for a conditional temporary passed as an argument |
| O11-11 | 31 / 45 s | 6 | Stage-0/Analyzer add VIB001 `__this_N` to VIB008 |
| O11-8b | 42 / 54 s | 24 | C8 accepted by the Analyzer and Stage-0 |
| O11-8a | 53 / 84 s | 99 | C6/C7 accepted, C9 rejected with VIB001,VIB008 instead of VIB000 |
| O11-5 | 71 / 109 s | 4 | Analyzer and Stage-0 VIB003 for a reborrow of a `Span<T>` parameter's result |
| O11-10 | 102 / 161 s | 6 | self adds VIB001 to VIB003 for `x[Idx(Move(x))]` |
| O11-7 | 169 / 200 s | 20 | Stage-0 VIB000 for `nint.MaxValue` and friends |

**All four target roots were rediscovered within the first minute** (O11-1 to O11-4 by program 39, at 49 s), and all
thirteen compiler roots of the audit within 3.5 minutes. O11-14 is a documentation finding outside the tool's scope.

**Vetting.** With the predicates written before the run, 80 of the 673 buckets were unattributed. All 80 were vetted
and belong to known roots, seen through another signature: self lacks the VIB007 of a returned view when the program
has another error (63 buckets, O11-3, incl. 47 `spec-wrong-id`), self's extra VIB003 for a conditional temporary next
to another error (9, O11-6), Stage-0's extra VIB000 for `nint` bounds next to another error (7, O11-7), and the
`__this_N` VIB001 on the after-init constructor (1, O11-11). The predicates now cover these shapes, and the final
`report.md` attributes every bucket. Further vetted shapes:
* O11-1 has a self side that Stage-0 does not share: self accepts `Eat(in x, Move(x))`, `x.R(Move(x))` on a readonly
  struct and `new W(in x, Move(x))`, where Stage-0 reports VIB003 (bucket 110). Pre-model IDs show up as
  `spec-wrong-id`: a moved field projection `Eat(in x, Move(x.Items))` is VIB005 (and VIB003 in Stage-0 only, bucket
  052), and `Eat(out x, x)` is VIB001 in Stage-0. The call-activation model makes all of them VIB003.
* O11-13 also reproduces with the file name alone (`Program.g.cs`, no header; bucket 012).
* The CI gate run found one more O11-10 form: `new W(in x, x.Unwrap())`, where self adds VIB001 at the `in` argument
  (Stage-0 VIB003 only).
* **N15 (new, decided).** The `decl.this-call-after-init` control (every field assigned, then `K += Peek()`) was
  rejected with VIB008 by all three frontends (bucket 057, seen through the O11-11 VIB001). Probes `n15_zt1`–`zt7`
  show that every frontend rejects any instance call on `this` in a constructor, whatever is assigned, while SPEC
  §2(2) only forbade it before the non-zero-safe fields are assigned. Decided: SPEC is aligned to the
  implementations (§7.14); no compiler change. The tag is now a vib-reject row (VIB008) with a static-call control.

**Expectation oracle.** `ca.borrow-then-move` was carried by 3,646 programs, and every frontend accepted 510 of them
(14 %), each one a `spec-accepted` bucket candidate of O11-1. No other vib-reject tag (8 tags, 146–522 programs each)
was ever accepted by every frontend.

**Curve.** Buckets keep growing (673 at minute 60) because each open root has many signatures (O11-13 alone pairs the
Analyzer's silence with every Stage-0 ID set). The unattributed column rose to 80 by minute 60 with the pre-run
predicates; with the vetted predicates it is 0. This run measures recall, not convergence: convergence is for the next
candidate, once the O11 fixes are in.

**Replay.** The run used the build before the N15 decision. Since then the `DeclCuts` constructor shape draws one of
three variants (before init, after init, static-call control) instead of two, with the same single random draw. So a
program that reaches that shape may print another variant; everything else replays identically (18 of the 19 stored
buckets, all but 010).

### 6.12 Round-11 candidate 4 (release-5 tool)

Round-11 candidate 4 combines the Round-11 fixes (O11-1 to O11-13, the C5–C9 cuts and the semantic-warning
promotions). With the release-5 tool:

* **Matrix:** 64 of 64 rows as expected (59 OK, 5 OK*), including the 8 C5–C9 rows and the 4 N15 rows.
* **Replay:** 25 of the 26 stored corpus buckets regenerate identically; bucket 010 differs as documented in §6.11.
* **CI gates** (seed 1 × 300, `--fail-on open`): default 0 buckets in 48 s (150 valid, 13 warning-only, 137 invalid,
  50 executed); activation 0 buckets in 62 s (172 valid, 14 warning-only, 114 invalid, 36 executed; index 242 skipped
  as a managed timeout, the known `Loop` artefact of §8).
* **Campaigns** (3 workers each, run at the same time): activation seed 9191 and default seed 7171. Main stopped both
  after about 54 minutes, when the C10–C20 cuts were decided, because the release-5 generator still produced the cut
  forms. Activation: 26,001 programs, 0 buckets. Default: 25,637 programs, 1 bucket.
* **The one bucket is call-stack exhaustion, not a root.** Index 15120 (`Const>Loop`): the self `-O0` binary ends with
  SIGSEGV, while the managed run and the Stage-0 `-O0` binary print the same output. The minimized program calls a
  recursive static local function to depth 131,070. With the default 8 MB stack self's `-O0` frames run out; with a
  16 MB or an unlimited stack all three runs agree. SPEC excludes call-stack exhaustion from the parity guarantee, so
  from release-6 on such a leg is re-run with an unlimited stack and excused when it then matches (§1, §7.16), and
  the generator calls recursive local functions with an argument `% 1000`. Note for the self backend owners: self
  `-O0` frames are larger than Stage-0's.

The minimized program and its stack probe are the corpus probe `probes/r11c4_stack_b1` (the campaign logs themselves were not kept).

### 6.13 Release-6 on Round-11 candidate 4 (before the cuts)

Candidate 4 does not implement C10–C20, so this run checks the release-6 tool itself:

* **Matrix:** 123 rows: 75 OK, 5 OK* and 43 violations, all expected without the cuts. 42 are the C10–C20 probe rows:
  every frontend accepts 39 of them and rejects three without the cut's message (the C4+C20 probe with the C4
  message, the jagged C16 probe for its raw arrays, and the `IntPtr` C17 probe). The 43rd is the C1 row
  `cut.localfunc-in-switch-section`, which since the C18 decision must also carry the C18 message. Every control,
  every C11 near-miss with a real discard, the C4 rows in their new `or` forms and the N13 rows in their `0`/`not 0`
  forms are OK, and every probe is C#-valid.
* **Generator:** no program with a C10–C20 form on seed 12 × 1,500 in any profile, apart from the deliberate C1
  shapes (§2).
* **CI gates** (seed 1 × 300, `--fail-on open`): default 0 buckets in 59 s (175 valid, 6 warning-only, 119 invalid,
  57 executed); activation 0 buckets in 51 s (196 valid, 6 warning-only, 98 invalid, 41 executed); peak 2.1 GiB.
* **Soundness smoke** (seed 31 × 200, 3 workers, 44 s): 165 valid, 4 warning-only, 31 invalid; 51 programs accepted
  by every frontend, each run at `-O0` and `-O3` under ASan/LSan for both native compilers, all clean and equal to the
  managed run. The one bucket is the expected C18 `cut-anchor` of a deliberate C1 shape (`regression:R12-CUTS` on
  this tree). The whole job (two builds, matrix, four `gen-stats` runs and this run) peaked at 1.9 GiB.
* **Seeded bugs:** the shared deviations SX2, SX5 and SX6 are found by the soundness oracle, and S1 and R1 by the
  default gate (§6.15).

### 6.14 Release-6 on the combined cut source (Round-12 cuts)

The combined cut source is Round-11 candidate 4 with the C10–C20 cuts: the Roslyn-side cuts (checkpoint 10), the
self cuts (checkpoint 5) and the F10 `Vec` capacity check, frozen at manifest `9ba44361…` (1,645 files). Its
`eng/conformance` and this document are the same as candidate 4's, so release-6 applies to both trees unchanged.

* **Release-5 drift.** The release-5 default gate on this tree had 11 buckets, all tool and expectation drift, none a
  compiler bug. Six were Analyzer-only VIB000s on the injected bootstrap. Two were C19 programs (constant conditions
  in `banned.default-nonzerosafe` shapes) that expected VIB008. Three were cut programs (two C11, one C19) whose
  secondary VIB004 was compared. Release-6 fixes all three:
  * The in-process SDK oracle now gives the bootstrap its real source path, as csc gets it (§1). The A-F3 trust
    check authenticates the bootstrap by path suffix and content hash, and it correctly refused the old IDE name
    `obj/__VibraniumAppBootstrap.cs`. The compiler's trust is unchanged.
  * The generator no longer produces C10–C20 forms (§2).
  * A program with a cut is judged only on the cut's documented VIB000 at its anchor (§1).
* **Oracle trust:** `sdk-crosscheck` (seed 5 × 40) agrees with a real `dotnet build` with `Vibranium.App.props` on
  all 40 programs. The in-process Analyzer therefore sees the same trusted bootstrap as MSBuild.
* **Matrix:** 123 rows, 118 OK and 5 OK*, no violation.
  * Every C10–C20 row is VIB000 in every frontend, and each frontend reports each cut exactly once at its anchor. 43
    rows show `anchors=OK`, including the C4+C20 two-cut probe, the namespace and using-alias C11 probes, and the C1
    row, which now carries C18 too (§5.2).
  * Every control and every C11 near-miss is accepted with equal output.
  * The 5 OK* rows are the same as on candidate 4.
* **CI gates** (seed 1 × 300, 3 workers, `--fail-on open`):

  | Gate | Buckets | Time | Valid | Warning-only | Invalid | Executed |
  |---|---|---|---|---|---|---|
  | default | 0 | 69 s | 175 | 6 | 119 | 57 |
  | activation | 0 | 61 s | 196 | 6 | 98 | 41 |

  The program streams and the counts are the same as on candidate 4 (§6.13). The job that ran the build, the
  matrix, three gates, `sdk-crosscheck` and the candidate-4 matrix check peaked at 2.7 GiB.
* **Soundness profile** (`ci` mode, 3 workers):

  | Seed × programs | Time | Valid | Warning-only | Invalid | Accepted by all and run natively | Buckets |
  |---|---|---|---|---|---|---|
  | 1 × 300 | 113 s | 241 | 4 | 55 | 70 | 7 |
  | 2 × 300 | 121 s | 246 | 7 | 47 | 77 | 10 |

  Every accepted program is ASan/LSan-clean at `-O0` and `-O3` for both native compilers and equal to the managed
  run, so there is no accepted-but-unsafe program and no native leg needed the stack excuse. All 17 buckets are
  `id-mismatch` between Stage-0 and self, on programs that every frontend rejects: the root R12-F3 below. The 2-seed
  job peaked at 2.4 GiB.
* **R12-F3 (new root, found here; decided as F3-clarified, §7.17).** The Roslyn-side F3 change records a
  borrow-rejected move without consuming its source, so that no VIB001 follows on a later loop iteration. Self has
  no counterpart, and the change also dropped a VIB005. There are two shapes:
  * **A move into a consuming call in a loop while a view is live.** Stage-0 and the Analyzer report {VIB003}. Self
    reports {VIB001, VIB003} at the same position.
    Probes: `r12f3_loop_move_borrowed` and `r12f3_loop_move_borrowed_reborrow`.
  * **A field moved out of a class while a view of it is live.** Stage-0 and the Analyzer report {VIB003}. Self
    reports {VIB003, VIB005}.
    Probe: `r12f3_field_move_borrowed`.

  Without a live view, every frontend still reports VIB005 for a class or a struct holder (controls
  `r12f3_*_noborrow_control`). On candidate 4 all three frontends agree on every probe ({VIB001, VIB003} and
  {VIB003, VIB005}). The decision: the Roslyn side restores VIB005 and self stops adding VIB001, both for
  candidate 6. Until then, `known-roots.json` lists `R12-F3` with status `new`:
  * two predicates for the self-only VIB001 (soundness move invalidators, or a loop tag in the other profiles);
  * one predicate for the self-only VIB005.

  Its buckets are `new:R12-F3`, so a soundness gate still fails while the root is open, but a campaign attributes
  them instead of listing them as unexplained. *(Release-7: the root is `fixed`, with a fourth predicate for the
  field-move expectation; §3.)* The default and activation streams do not reach these shapes.
* **Candidate 4 still passes:** with the bootstrap-path fix (the matrix does not use `known-roots.json`), the
  candidate-4 matrix is unchanged (75 OK, 5 OK*, 43 expected cut violations), so §6.13 holds.
* **Seeded bugs:** `check` applies all 31 replacements on this tree. The S2 find text was shortened because the F3
  change inserted lines after it; the same text still applies on candidate 4. The shared deviation
  `SX2-shared-move-while-borrowed` (seed 1 × 300, soundness detector) is detected with 37 open buckets: sanitizer 21,
  output-mismatch 4, accept-reject 6 and id-mismatch 6, with 107 programs run natively; the job peaked at 2.7 GiB. On
  candidate 4 the same run had 22, 4, 6 and 6 (§6.15).

### 6.15 Seeded-bug recall support

Recall against known roots (§6.2, §6.11) only measures bugs that were already found. For the Cycle A recall target,
release-6 comes with a seeded-bug kit, delivered next to the tool but not in the tree because it edits compiler
sources: a catalog of 25 mutations (`mutations.py`) and a runner (`seed-bug.py`) that plants one mutation into a
throw-away copy of a tree, rebuilds, runs the matrix, the default and activation `ci` gates and the soundness profile,
and records which detectors saw it. The catalog covers the shared analysis (Analyzer and Stage-0: use after move,
move/mutation/scope exit while borrowed, escaping stack views, writes through shared borrows), the Roslyn gatekeeper
and cuts, the Analyzer alone, the self checker, both LLVM backends (shift masks, arithmetic shifts, division by zero,
`Vec` growth, float-to-integer saturation) and the native runtime (`vib_rt.ll`). Three entries are shared deviations:
the same check removed from both the Roslyn-side analysis and the self checker, so that every frontend accepts the
same unsafe program and only the soundness oracle can see it.

Sample on Round-11 candidate 4 (seed 1 × 300 per detector, 3 workers; whole job peak 3.3 GiB):

| Mutation | Detector | Result | Buckets by kind |
|---|---|---|---|
| (unmutated baseline) | soundness | clean | 0 (70 programs run natively) |
| `SX2-shared-move-while-borrowed` | soundness | detected | sanitizer 22, output-mismatch 4, accept-reject 6, id-mismatch 6 |
| `SX5-shared-mutate-while-borrowed` | soundness | detected | sanitizer 24, accept-reject 7, id-mismatch 6 |
| `SX6-shared-scope-exit` | soundness | detected | sanitizer 6 |
| `S1-use-after-move` | ci-default | detected | accept-reject 2, id-mismatch 1 |
| `R1-runtime-print-neg-one` | ci-default | detected | output-mismatch 3 |

All three shared deviations are caught through `sanitizer` buckets (heap-use-after-free in ASan builds of programs
every frontend accepted), which is the evidence that the soundness oracle sees what the differential oracles cannot.
The full measurement on candidate 6, with execution, is in §6.16. The kit's README lists every mutation and how to
run it.

### 6.16 Seeded-bug recall on candidate 6 (release-7, Cycle A)

This is the Cycle A recall measurement: the seeded-bug kit (§6.15) run on candidate 6 with the release-7 tool, the way
it was designed.

**Protocol (v3, `seed-bug.py --protocol exec`, the default).** For each of the 25 mutations (31 source replacements;
three are planted through a tree alternative, because candidate 6 changed the code they edit) the runner copies the
tree, plants the mutation, rebuilds, and runs five detectors as CI runs them, with execution:

* the matrix (§5.2);
* the default and activation `ci` gates, seed 1 × 300 programs at 3 workers;
* the soundness profile, seed 1 × 300, where every program that all frontends accept runs at `-O0` and `-O3` under
  ASan/LSan;
* `sdk-crosscheck`.

Detection is relative to a baseline run of the unmutated tree with the same settings. A detector catches a mutation
when it shows a disagreement signature that the baseline does not have, more programs with a baseline signature, or a
changed matrix row (verdict, IDs, cut anchors or run signatures). The baseline is clean except for the N16 signatures
of the activation gate (§6.17), which do not count.

**Result: 25 of 25 caught (100 %; target ≥ 90 %).** "Earliest first detection" names the detector that saw the
mutation first, the index of the first program with a new signature in that detector's seeded stream (0 is the first
program) and the seconds into that detector's run.

| Mutation | Layer | Caught by | Earliest first detection (program, s) | How |
|---|---|---|---|---|
| `S1-use-after-move` | analysis | matrix, ci-default, ci-activation, soundness | soundness: 0, 17.7 | accept-reject, id-mismatch, spec-wrong-id; matrix row `decided.reachable-default-use-after-move` |
| `S2-move-while-borrowed` | analysis | soundness | soundness: 11, 35.8 | spec-wrong-id (soundness expectation), accept-reject, id-mismatch |
| `S3-escape-stack-view` | analysis | ci-activation | ci-activation: 11, 22.8 | accept-reject on the release-7 N16 shape (Stage-0 accepts, self VIB003+VIB007) |
| `S4-mutate-through-shared` | analysis | matrix | matrix | release-7 matrix row `decided.noncopy-pattern-binding-reassign` |
| `S5-mutate-while-borrowed` | analysis | matrix, ci-activation, soundness | soundness: 6, 19.1 | accept-reject, id-mismatch, spec-wrong-id; matrix row `decided.span-foreach-write-in-body` |
| `S6-scope-exit-while-borrowed` | analysis | soundness | soundness: 176, 63.3 | accept-reject (Stage-0 accepts, self VIB003) |
| `F1-goto-accepted` | frontend | matrix, ci-default | ci-default: 28, 32.5 | banned-accepted; matrix row `banned.goto` |
| `F2-preprocessor-cut-off` | frontend | matrix, ci-activation | ci-activation: 31, 25.5 | banned-wrong-id; matrix row `cut.preprocessor` |
| `A1-analyzer-drops-vib007` | analyzer | matrix, ci-default, ci-activation, soundness | ci-activation: 11, 25.3 | analyzer-vs-stage0, banned-accepted; matrix row `banned.collection-return-span` |
| `X1-self-use-after-move` | self-checker | matrix, ci-default, ci-activation, soundness | soundness: 0, 19.8 | accept-reject, id-mismatch, spec-wrong-id; matrix row |
| `X2-self-move-while-borrowed` | self-checker | soundness | soundness: 0, 17 | id-mismatch, accept-reject, spec-wrong-id |
| `X3-self-scope-exit` | self-checker | soundness | soundness: 176, 63.1 | accept-reject (self accepts, Stage-0 VIB003) |
| `X4-self-escape` | self-checker | ci-activation | ci-activation: 77, 47 | accept-reject (self accepts, Stage-0 VIB007) |
| `X5-self-borrow-while-borrowed` | self-checker | matrix, ci-activation, soundness | ci-activation: 22, 19.1 | accept-reject, id-mismatch, spec-wrong-id; matrix row |
| `B1-stage0-shift-mask` | stage0-backend | ci-default, ci-activation, soundness | ci-activation: 7, 29.2 | `ir-contract` (shift-mask) in every gate; output-mismatch |
| `B2-stage0-arith-shift` | stage0-backend | ci-activation | ci-activation: 57, 72.1 | output-mismatch |
| `B3-stage0-div-zero` | stage0-backend | ci-default, ci-activation, soundness | soundness: 20, 26 | `ir-contract` (division-unguarded) in every gate; output-mismatch, sanitizer |
| `B4-stage0-vec-growth` | stage0-backend | soundness | soundness: 2, 37.2 | sanitizer: ASan heap-buffer-overflow (9 programs) |
| `XB1-self-shift-mask` | self-backend | ci-default, ci-activation, soundness | ci-activation: 7, 29.4 | `ir-contract` (shift-mask) in every gate; output-mismatch |
| `XB2-self-float-to-unsigned` | self-backend | ci-default, ci-activation, soundness | ci-activation: 19, 41.9 | output-mismatch |
| `R1-runtime-print-neg-one` | runtime | ci-default, ci-activation, soundness | ci-activation: 26, 53.4 | output-mismatch (Stage-0 and self) |
| `R2-runtime-reserve-short` | runtime | soundness | soundness: 34, 53 | sanitizer: ASan heap-buffer-overflow (11 programs) |
| `SX2-shared-move-while-borrowed` | analysis+self-checker | soundness | soundness: 0, 21.4 | spec-accepted 37, sanitizer heap-use-after-free 46, output-mismatch 5 |
| `SX5-shared-mutate-while-borrowed` | analysis+self-checker | matrix, ci-default, ci-activation, soundness | ci-activation: 22, 15.8 | spec-accepted 72, sanitizer 75; matrix rows |
| `SX6-shared-scope-exit` | analysis+self-checker | soundness | soundness: 176, 69.1 | spec-accepted 4, sanitizer 8 |

* **S3 was not an equivalent mutant.** It was the release-6 miss: no generated program reached the check it removes.
  The check is observable when a collection expression is assigned to a by-value `ReadOnlySpan<int>` parameter that
  is then returned. Release-7 teaches the activation generator that shape (`view.param-*` tags, §2), which catches S3
  at program 11 and also found N16 (§6.17).
* **S4** is caught by the release-7 decided matrix rows (§5.2); no generated program reaches it in 300 programs.
* **B1, XB1 and B3** are caught by the static IR contract (§1) in every gate, before anything runs. They also show as
  `output-mismatch` or `sanitizer`.
* **B4 and R2** need execution: they show only as ASan `heap-buffer-overflow` in the soundness profile.
* **The three shared deviations** (SX2, SX5, SX6), which every frontend accepts alike, are caught by the soundness
  expectation oracle (`spec-accepted`) and by ASan. The differential oracles cannot see them.
* `sdk-crosscheck` caught nothing, as designed: no mutation touches Roslyn or the SDK oracle.
* With the `no-exec` protocol (frontend checks, expectation oracles and the IR contract only), an earlier measurement
  on 9ba44361, before the S4 rows, the IR contract and the S3 shape, caught 18 of 25.

**Recall of the CI steps.** Before release-7, CI ran only the default `ci` gate, which catches 10 of the 25
mutations. With the soundness step it catches 20, which is under the target. S3, X4 and B2 are caught only by the
activation gate, and S4 only by the matrix. So release-7 adds three CI steps (decided by main): activation, soundness
and matrix (§9), which together with the default gate catch 25 of 25. The `matrix` command now exits 1 when a row is a
`violation` or a `probe-error`, so it can gate.

| CI steps | Caught | Missed |
|---|---|---|
| default `ci` gate (before release-7) | 10/25 | S2–S6, F2, X2–X5, B2, B4, R2, SX2, SX6 |
| default + soundness | 20/25 | S3, S4, F2, X4, B2 |
| default + soundness + matrix | 22/25 | S3, X4, B2 |
| default + activation + soundness + matrix | 25/25 | – |

**Cost.** One mutation takes about 8 minutes: rebuilding the compilers and the tool, then the five detectors. Shards
peaked at 2.3–2.8 GiB. The 25 mutations ran in six shards on the shared machine; a machine crash lost one shard, whose
mutations were re-run in three new shards, and `seed-bug.py report` merges the shards. The kit's README has the
per-detector table (`results.md`).

### 6.17 Release-7 on candidate 6

The release-7 tool on candidate 6 (manifest pre1; pre2 to pre4 change only `docs/CONFORMANCE.md` §1, `docs/MUTATION.md`,
`eng/mutation` and one test file, none of which the tool reads; pre5 adds the Roslyn-side N16 fix, see the end of this
section):

* **Matrix:** 130 rows: 125 OK, 5 OK*, 0 violations. Every cut row has `anchors=OK` (47 rows). The four C21 probes get
  exactly one VIB000 with the C21 message at the `base` token from every frontend, and the S4 decided rows
  (`decided.noncopy-pattern-binding-reassign` and its Copy control) are OK.
* **CI gates** (seed 1 × 300, 3 workers, with execution), before the N16 shape was added to the activation profile:

  | Profile | Buckets | Time | Valid / warning-only / invalid | Run natively |
  |---|---|---|---|---|
  | default | 0 | 55 s | 175 / 6 / 119 | 57 |
  | activation | 0 | 64 s | 196 / 6 / 98 | 41 |
  | soundness | 0 | 69 s | 241 / 4 / 55 | 70 |

* **R12-F3 is fixed.** The 7 `regression:R12-F3` soundness buckets of the combined cut source are gone; its 4
  predicates in `known-roots.json` are now `fixed`, so a return fails the gate as `regression:R12-F3`.
* `sdk-crosscheck`: 40/40 identical ID sets.
* **IR contract v2** (§1): no `ir-contract` signature on 9ba44361 (default, activation and soundness runs) or on
  candidate 6 (every gate and the campaigns in §6.18). A test mutant that emits unmasked shifts and unguarded `sdiv`
  fires `shift-mask` and `signed-overflow-unguarded`; the seeded backend bugs B1, XB1 and B3 fire `shift-mask` and
  `division-unguarded` (§6.16).
* **N16, a new root found through S3** (§7 item 18). With the release-7 view-parameter shape, the activation gate on pre1
  (seed 1) has 5 buckets, all `new:N16` (3 `id-mismatch`, 2 `spec-wrong-id`), from the 6 programs that have the
  shape. The Analyzer and Stage-0 report {VIB007}, self reports {VIB003, VIB007}; main decided that {VIB003, VIB007}
  is right and that the Roslyn side is fixed. N16 also reproduces on 9ba44361 and on Round-11 candidate 4, so it
  predates the cuts, and every frontend still rejects the program, so there is no safety impact. The fix is in
  candidate 6 pre5 (below), and both N16 predicates in `known-roots.json` are now `fixed`, so a return fails the gate
  as `regression:N16`. The new shape keeps the generator clean: `gen-stats --profile activation --seed 12 --count 1500`
  shows 0 cut forms and 38 programs with `view.param-*` tags, whose unmutated programs are C#-valid except 2 with
  CS8510 from another anchor. The default, extended and soundness streams are unchanged.
* **Final check on candidate 6 pre4** (a copy verified against the manifest, with the release-7 tool):
  `dotnet build --disable-build-servers Vibranium.slnx` gives 0 warnings and 0 errors, and the `Release` builds of
  `vib-self` and the tool give none. `run-conformance.sh matrix` prints `matrix: 130 rows; OK 125, OK* 5; pass` and
  exits 0. On Round-11 candidate 4, which predates the cuts, the same tool reports the 47 cut rows as violations and
  exits 1.
* **Candidate 6 pre5: N16 fixed.** Pre5 adds the Roslyn-side fix (`BorrowAndMoveAnalyzer`, and the lowering of an
  assignment expression's value when the destination is a parameter). On a copy verified against the pre5 manifest,
  every N16 probe (corpus `probes/s3p_*`) gets the same IDs from the Analyzer, Stage-0 and self: {VIB003, VIB007} for
  the statement forms (`p = [1, 2]; return p;`, on one branch, `[]`, `p[1..]`) and for the assignment-value forms
  `return p = [1, 2];`, `=> p = [1, 2];`, `q = p = [1, 2]; return q;` (two parameters), `return (p = [1, 2,
  3])[1..];` and `ReadOnlySpan<int> q = p = [1, 2]; return q;`, which on pre4 still gave {VIB007} in the Analyzer
  and Stage-0. The controls are unchanged. All four CI steps of §9 pass with N16 `fixed` (seed 1 × 300, 3 workers,
  on the loaded machine):

  | Step | Result | Time | Valid / warning-only / invalid | Run natively |
  |---|---|---|---|---|
  | default | 0 buckets | 62 s | 175 / 6 / 119 | 57 |
  | activation (with the N16 shape) | 0 buckets | 71 s | 199 / 5 / 96 | 39 |
  | soundness | 0 buckets | 105 s | 241 / 4 / 55 | 70 |
  | matrix | 130 rows: 125 OK, 5 OK*; pass | about 1 min | – | – |

### 6.18 Root-finding campaign on candidate 6 (release-7)

Three campaigns with the release-7 tool on candidate 6 (pre1, before the N16 fix), one per profile, 100,000 programs in
all. Each ran with 6 workers and execution as in CI (`--exec-rate 0.5`, `--asan-rate 0.05`; in the soundness profile
every accepted program at `-O0` and `-O3` under ASan/LSan). The default and soundness campaigns ran at the same time
on the shared machine. This is the root-finding run; the release evidence is one more campaign of the same size on the
final candidate 6 manifest.

| Profile | Seed | Programs | Time | Programs/min | Run natively | Valid / warning-only / invalid | Signatures | Buckets | Non-terminating | Peak |
|---|---|---|---|---|---|---|---|---|---|---|
| default | 7700 | 40,000 | 4,195 s | 572 | 8,910 | 24,076 / 987 / 14,937 | 2 | 2 (`run-timeout`, one program, speed only) | 17 | 3,967 MiB |
| activation | 7701 | 35,000 | 3,306 s | 635 | 5,101 | 23,297 / 878 / 10,825 | 12 | 30, all `new:N16` | 3 | 3,951 MiB |
| soundness | 7702 | 25,000 | 3,368 s | 445 | 5,787 | 20,213 / 505 / 4,282 | 0 | 0 | 10 | 3,980 MiB |
| total | | 100,000 | | | 19,798 | | 14 | 32 | 30 | |

* **No new root.** Every bucket is N16 (fixed in pre5, §6.17) or the speed artefact below. The IR contract, the
  sanitizer and the soundness expectation found nothing, and every catalog tag of each profile was covered.
* **Second half.** Default: its only signatures came from one program, number 16,039 (minute 29), in the first half.
  Soundness: no signature at all. Activation: N16 first appeared at program 102 (23 s), and 5 of its 12 signatures
  first appeared in the second half (programs 17,615 to 28,337). All of them are N16: the `spec-wrong-id` signature
  lists each frontend's full ID set, so an N16 program whose other anchor adds VIB001, VIB004, VIB006 or VIB008 gets
  a signature of its own. No other root appeared in the second half; the N16 signature count grows with combinations,
  not with roots.
* **The run-timeout artefact.** Default program 16,039 loops until an `int` counter wraps (about 4.3 billion
  iterations) and then panics with a division by zero. The managed run finishes within 20 s; the `-O0` binaries took
  19–20 s (the minimized program 19.1 s) and `-O3` 3 ms, with the same panic and exit 101 everywhere. Under load
  both `-O0` legs crossed the 20 s budget. The campaign ran with the tool before the slow-leg rule (§1); with the
  rule, such a leg is re-run with 200 s and excused when it matches. A replay with the release-7 tool shows no
  disagreement (that time the `-O0` legs finished just under 20 s); corpus probe `to_03_slow_loop` is the rule's
  positive test.
* The 30 non-terminating programs are generator loops that never end (managed timeout), skipped and listed in
  `summary.json`. No native leg ran out of stack, and the activation and soundness runs needed no slow-leg excuse.
* The corpus has the three runs (`c6-default-7700/`, `c6-activation-7701/`, `c6-soundness-7702/`).

### 6.19 Release-7.1: the assignment-value cascade (N17, N18)

/roslyn-n16 found this class by hand, and the generator had never produced it. On the Roslyn side a moved `!Copy` view
keeps its borrow and an overwritten view's old borrows still count; in self the value of an assignment to a view does
not move it (self-fix V4). Release-7.1 teaches the activation profile the shapes, roots them and adds a seeded bug.

* **Probes** (corpus `probes/cascade/`, candidate 6 pre8, `run --exec`): /roslyn-n16's 83 probes, seven of this tool's
  and the six rebind probes `x1`–`x6`. The splits are listed in the corpus README: {VIB001, VIB003} in the Analyzer and
  Stage-0 against {VIB001} in self after a plain move; VIB003 against acceptance for a view reassigned in a loop; and
  {VIB001, VIB003} or {VIB001} against acceptance after some assignment values. The real conflicts and controls agree.
  `A26` is VIB003 everywhere, where the decided result is VIB001 (§7 item 19).
* **Generator.** The `ViewMove` activation anchor (§2), four families with the expected results of §7 item 19 as
  vib-reject tags (rejected shapes) and expected-accept tags (accepted shapes, the `spec-rejected` oracle of §1). On
  seed 12 × 1,500 (activation, final release-7.1 stream) 197 programs carry a `ViewMove` tag, 144 of them unmutated.
  Stage-0 rejects every program with a `ViewMove` vib-reject tag (0 accepted), C# errors in these programs come only
  from other anchors, and there are 0 cut forms.
* **CI gate** (activation, seed 1 × 300, 3 workers, pre8, final tool): 18 buckets in 126 s, all attributed: 10
  `new:N17`, 1 `new:N18` and 7 `new:N19` (§6.20); 207 / 2 / 91 valid / warning-only / invalid, 33 run natively.
  With the `ViewMove` anchor alone the gate had 18–20 buckets, all N17 or N18.
* **Roots.** `N17` (Roslyn side, fix roslyn-assign-value-cascade) and `N18` (self V4, fix self-fix-v), first with
  status `new` (§3). Every predicate needs a `ViewMove` tag. Both fixes are in candidate 6 pre9: the 96 cascade probes
  agree there, the activation gate has no N17 or N18 bucket (§6.21), and both roots are `fixed`.
* **Expected-accept oracle** (§1, `spec-rejected`). It fires only when all three frontends reject an accepted shape on
  its helper line, so on pre8, where self accepts the loop forms that the Roslyn side rejects, it is silent; it guards
  against both sides converging on an over-rejection. With seeded bug XV1 planted it fired once (below).
* **Seeded bug XV1** (`XV1-self-overwrite-keeps-old-loans`, self checker: an overwritten borrow carrier keeps its old
  loans, so reassigning a view no longer ends its old borrows). The first three families did not catch it in any of
  the four detectors. Liveness gates the conflicts, and in the loop forms the Roslyn side has the same behaviour, so
  the differential oracles see nothing. Probes `x1`–`x6` showed the observable shape: a live view rebound to another
  owner, then the old owner mutated while the view is still used, which pre8 accepts everywhere and XV1 makes self
  reject with VIB003 (`*.xv1.txt` in the corpus). The fourth family (rebind) generates it, and the activation gate
  then catches XV1 at program 130 (79 s): an `accept-reject` (`stage0-accepts`, `self:VIB003`) and one
  `spec-rejected`, 4 programs more than the baseline. The full recall with the release-7.1 streams is in §6.21.

### 6.20 Release-7.1: switches over unions (N19, P0; N20 decided)

/self-fix found while fixing V5 that vib-self miscompiles valid switch *statements* over `Option`/`Result`: every case
label that is not a union case (`Some/None/Ok/Err(..)`), `_`/default or a designation-free `var`/type pattern is
silently dropped. Managed and Stage-0 agree; self native prints another value, and an exhaustive `case not
Some<Vec<int>>:` + `case Some<Vec<int>>(var v):` switch reaches its `unreachable` block on None and panics ("index
out of bounds", exit 101). The generator never produced such labels: `SwitchStmt`, `SwitchExpr`, `OptionResult` and
`PatternVar` label Option/Result switches with union cases only.

* **Probes** (corpus `probes/union-switch/`, 49 probes, candidate 6 pre8, `run --exec`). Statements with a whole-union
  label give a self `output-mismatch` in 12 probes, a panic (exit 101) in 2, a SIGSEGV in 1 (a class payload) and a
  self loop (`run-timeout`) in 1 (`case not NoneType:` + `case NoneType:` on a place). Self rejects a guarded or final
  `var w` (VIB000, from its own lowering of the dropped designation, or VIB001) and some switch-*expression* forms
  that Stage-0 accepts (whole-union arm before the payload arm: VIB001; on a place: VIB005), so the expressions are
  right at run time but not in the checker. Controls agree (union cases only, `Copy` place expressions, `Some<int> s`
  designations).
* **Generator.** The `UnionSwitch` anchor (§2), in every profile: statements and expressions over `Option<T>` and
  `Result<T, int>`; `Copy`, owner, class and linear payloads; place, call and `Move(o)` scrutinees; `not`, `or`, `var`
  and designation labels with and without guards, exhaustive or with a default; and the non-matching path taken at
  run time. On seed 12 × 1,500, 167 default, 188 activation and 236 soundness programs contain it, 427 of these 591
  with a whole-union label (`uswitch.nonunion-label`). Of the 466 unmutated ones, 42 are C#-invalid and 110 more are
  rejected by Stage-0, all because of their other anchors: no C# error and no Stage-0 diagnostic is on a
  `UnionSwitch` helper line.
* **CI budget: the class shows within it in every profile** (seed 1 × 300, 3 workers, pre8, final tool):

  | Gate | N19 buckets (programs) | First N19 program (s) | Run-time N19 programs | All buckets | Time |
  |---|---|---|---|---|---|
  | default | 8 (8) | 10 (18 s) | 2 `output-mismatch` | 8, all N19 | 95 s |
  | activation | 7 (7) | 41 (38 s) | 1 `output-mismatch` (panic) | 18 (10 N17, 1 N18, 7 N19) | 126 s |
  | soundness | 11 (15) | 9 (23 s) | 10 (`output-mismatch`, `run-timeout`, ASan `report`) | 11, all N19 | 141 s |

  The static N19 forms (self rejects what Stage-0 accepts) need no execution; the run-time forms need the program to
  be run, which happens for half the accepted programs in the default and activation gates and for all of them in
  the soundness gate. No campaign is needed to see the class.
* **Hanging self legs.** The self loops made the first soundness gate on pre8 take more than 17 minutes: each hanging
  leg got the 200 s slow re-run (§1), and the reducer re-ran it for every test. Release-7.1 skips the slow re-run when
  another leg of the program matched the managed run in under 2 s, and does not minimize a program with a hanging leg
  (§3); the gate then takes 141 s.
* **Root N19** (three predicates, fix self-fix-v V5, §3): self native results on programs tagged
  `uswitch.stmt-nonunion-label`, and self rejections (`accept-reject`, `id-mismatch`) on programs tagged
  `uswitch.stmt-nonunion-label` or `uswitch.expr-whole-union-arm`. All N19 buckets of the three gates match a
  predicate, and no other bucket does.
* **Candidate 6 pre9 (self-fix-v in).** The wrong values, panics and loops are gone: all 49 `union-switch` probes
  agree except the linear ones of N20, and the soundness gate has 0 buckets. One residual remains: self reports a
  false VIB001 on a later use of an owned `!Copy` `Option`/`Result` place after a switch statement whose labels are
  all whole-union labels plus `default:` (`case not NoneType: .. default: ..` then `o.IsSome`), guarded or not.
  It does not occur with a `Copy` payload, without the later use, in the expression form, with `case NoneType:`
  instead of `default:`, with a variant arm added, or with `default:` alone (probes `r01`–`r12` in
  `probes/union-switch/`, pre9 outputs).
  The default and activation gates find it within the CI budget (§6.21). Main confirmed it as a vib-self bug (fix
  self-fix-w), so `N19` stayed `open`. Self-fix-w is in candidate 6 pre13, where the twelve probes agree, and N19 is
  `fixed` from release-7.2 (§6.22).
* **N20, decided as a ban (§7 item 20).** For a linear payload matched from a call, the Analyzer and Stage-0 report
  {VIB001, VIB006} as soon as any variant arm has a `when` guard (even `case NoneType when ..`), a variant is tested
  twice or a label is `not`, and self accepted the guarded forms. Main decided that these forms, and `or`, are exactly
  {VIB006}, while `var u` over the whole union and a guarded discard arm stay valid. The decision is checked by 14
  matrix rows (§5.2) and 59 probes (`probes/linear-switch/`). Both fixes (roslyn-linear-switch and self-fix) are in
  candidate 6 pre13, where every row and probe is as decided, and N20 is `fixed` from release-7.2 (§6.22). The
  generator keeps to the allowed linear forms (one arm per variant, no guard).
* **Seeded bug F4** (`F4-stage0-drop-nonunion-label`, frontend): Stage-0's lowering never takes a switch-statement label
  whose pattern is a `not` or an `or` over the union cases, as self did. The Analyzer shares the lowering, and Stage-0
  native then prints what self prints, so the bucket signature becomes `output-mismatch | stage0+self` (probes on the
  mutated tree). No older production writes such labels. Its recall is in §6.21.

### 6.21 Release-7.1: seeded-bug recall and candidate 6 pre9

**Seeded-bug recall with the release-7.1 streams.** The kit (§6.15, v4: 27 mutations, 33 replacements, `XV1` and
`F4` added) ran with the exec protocol on candidate 6 pre8 with the release-7.1 generator (the streams of this
release; the N20 matrix rows and the root status changes came later and change no stream), seed 1 × 300 at 3
workers, as three shards of 9 mutations (about 11 minutes per mutation under load). All 33 replacements also apply
to pre9.

| Mutation | Caught by | Earliest first detection |
|---|---|---|
| S1-use-after-move | matrix, ci-activation, soundness | ci-activation: program 8, 14.2 s |
| S2-move-while-borrowed | soundness | soundness: program 10, 38.5 s |
| S3-escape-stack-view | ci-activation | ci-activation: program 39, 35.3 s |
| S4-mutate-through-shared | matrix | matrix row |
| S5-mutate-while-borrowed | matrix, ci-activation, soundness | soundness: program 8, 13.9 s |
| S6-scope-exit-while-borrowed | soundness | soundness: program 27, 26.5 s |
| F1-goto-accepted | matrix, ci-default | ci-default: program 195, 80 s |
| F2-preprocessor-cut-off | matrix, ci-activation | ci-activation: program 185, 96 s |
| A1-analyzer-drops-vib007 | matrix, ci-default, ci-activation, soundness | ci-activation: program 37, 37.9 s |
| X1-self-use-after-move | matrix, ci-default, ci-activation, soundness | ci-activation: program 8, 17 s |
| X2-self-move-while-borrowed | soundness | soundness: program 8, 18.4 s |
| X3-self-scope-exit | soundness | soundness: program 27, 27.5 s |
| X4-self-escape | ci-activation | ci-activation: program 37, 37.1 s |
| X5-self-borrow-while-borrowed | matrix, ci-default, ci-activation, soundness | soundness: program 0, 10.1 s |
| B1-stage0-shift-mask | ci-default, ci-activation, soundness | ci-activation: program 15, 22.3 s |
| B2-stage0-arith-shift | ci-default | ci-default: program 11, 62.8 s |
| B3-stage0-div-zero | ci-default, ci-activation, soundness | ci-activation: program 5, 28.4 s |
| B4-stage0-vec-growth | ci-activation, soundness | soundness: program 32, 69.4 s |
| XB1-self-shift-mask | ci-default, ci-activation, soundness | ci-activation: program 15, 22.7 s |
| XB2-self-float-to-unsigned | ci-activation | ci-activation: program 166, 122 s |
| R1-runtime-print-neg-one | ci-default, soundness | ci-default: program 11, 63.3 s |
| R2-runtime-reserve-short | soundness | soundness: program 63, 66.5 s |
| SX2-shared-move-while-borrowed | soundness | soundness: program 8, 18 s |
| SX5-shared-mutate-while-borrowed | matrix, ci-default, ci-activation, soundness | soundness: program 0, 12.1 s |
| SX6-shared-scope-exit | soundness | soundness: program 27, 31.9 s |
| XV1-self-overwrite-keeps-old-loans | ci-activation | ci-activation: program 66, 57.3 s (+9 programs) |
| F4-stage0-drop-nonunion-label | ci-default, ci-activation, soundness | ci-default: program 57, 41.3 s |

**Recall: 27 of 27 (100 %).** By CI step:

| CI steps | Caught | Missed |
|---|---|---|
| default `ci` gate | 11/27 | S1–S6, F2, X2–X4, B4, XB2, R2, SX2, SX6, XV1 |
| default + soundness | 21/27 | S3, S4, F2, X4, XB2, XV1 |
| default + soundness + matrix | 23/27 | S3, X4, XB2, XV1 |
| default + activation + soundness | 26/27 | S4 |
| all four steps (§9) | 27/27 | – |

* **The new bugs.** F4 (Stage-0 drops a `not`/`or` statement label) is caught by every fuzz step through the
  `UnionSwitch` anchor (`output-mismatch`, 2 programs in default, 1 in activation, 10 in soundness); the matrix has
  no such row. XV1 (self keeps an overwritten view's old loans) is caught only by the activation step, through the
  rebind family (`accept-reject` `stage0-accepts self:VIB003`, `id-mismatch` and one `spec-rejected`).
* **Against release-7 (§6.16).** The streams changed (new anchors), so single detections moved: S1 is no longer
  caught by the default step (the matrix, activation and soundness still catch it), B2 moved from the activation step
  to the default step, XB2 is now caught only by the activation step, B4 also by the activation step, and R1 no longer
  by the activation step. Each step is still the only detector of some bug: the default step of B2, the activation
  step of S3, X4, XB2 and XV1, the soundness step of S2, S6, X2, X3, R2, SX2 and SX6, and the matrix of S4. The value
  bugs B2 and XB2 rest on one sampled execution each.

**Candidate 6 pre9** (pre8 + self-fix-s + self-fix-v + roslyn-assign-value-cascade; a fresh copy verified against
its manifest; release-7.1 tool; seed 1 × 300 at 3 workers):

| Step | Result | Buckets | Programs valid / warning-only / invalid, run natively | Time |
|---|---|---|---|---|
| default | exit 1 | 1 `known:N19` (the residual, program 10) | 177 / 11 / 112, 65 | 68 s |
| activation | exit 1 | 2 `known:N19` (the residual, programs 61 and 119); no N17 or N18 | 207 / 2 / 91, 40 | 73 s |
| soundness | exit 0 | 0 | 248 / 9 / 43, 80 (ASan/LSan at `-O0` and `-O3`) | 88 s |
| matrix | exit 1 | 144 rows: 133 OK, 5 OK*, 6 `known:N20` | – | 66 s |

With `CONF_FAIL_ON=new` the default, activation and matrix steps pass, because N19 and N20 are `open`. The
`Vibranium.slnx` Debug build with the tool has 0 warnings and 0 errors; `vib-self` and the tool build in Release with
0 warnings.

### 6.22 Release-7.2 on candidate 6 pre13

Candidate 6 pre13 carries self-fix-w (the N19 residual), roslyn-linear-switch (N20 on the Roslyn side: exactly
{VIB006}, the knock-on VIB001 removed, and the SPEC sentences in §2 "Banned Operations on Linear Unions" and §3.4
"Rvalue Scrutinees"), the self side of N20, and self-fix-o; its `eng/conformance` is release-7.1. Release-7.2 sets
N19 and N20 to `fixed`; the program streams are those of release-7.1. Everything below ran on a fresh copy verified
against the pre13 manifest (no extra files), with seed 1 × 300 at 3 workers and `CONF_FAIL_ON=open`:

| Step | Result | Buckets | Programs valid / warning-only / invalid, run natively | Time |
|---|---|---|---|---|
| default | exit 0 | 0 | 177 / 11 / 112, 65 | 50 s |
| activation | exit 0 | 0 | 207 / 2 / 91, 40 | 54 s |
| soundness | exit 0 | 0 | 248 / 9 / 43, 80 (ASan/LSan at `-O0` and `-O3`) | 80 s |
| matrix | exit 0 | 144 rows: 139 OK, 5 OK*, 0 violations | – | 63 s |

* **N19.** All 61 `probes/union-switch/` probes (`us01`–`us51` and the residual probes `r01`–`r12`) get the same
  result from all three frontends, and every accepted one prints the managed output natively; the residual probes are
  accepted everywhere. No gate has an N19 bucket.
* **N20.** The 14 `decided.linear-switch-*` rows are OK: the 7 banned rows are exactly {VIB006} on all three, and the
  7 controls are accepted and match the managed run in all four ASan/LSan native runs. All 59 `probes/linear-switch/`
  probes are as expected (`VERDICTS.pre13.tsv`): 20 exactly {VIB006}, 18 accepted with equal native output, 21 with
  equal ID sets on a place scrutinee; the N20 group of `probes/union-switch/` is exactly {VIB006} everywhere.
* **N17, N18.** All 96 `probes/cascade/` probes agree; `A26` now gets the decided {VIB001} from all three.
* **Builds.** `dotnet build --disable-build-servers Vibranium.slnx` (Debug, with the tool): 0 warnings, 0 errors;
  `vib-self` and the tool build in Release with 0 warnings.
* **Seeded-bug kit.** All 33 replacements of the 27 mutations still apply to pre13 (`seed-bug.py check`); the recall
  of §6.21 was not re-measured, since the streams are unchanged.

## 7. Decided rules and questions

All the questions raised by the first delivery have been decided by the owner. The catalog, the matrix (§5.2) and
`known-roots.json` follow these decisions.

1. **Banned-construct parity.** A program that contains a banned-by-design or cut construct only needs to be
   rejected by every frontend, and each frontend must report the documented ID of a banned construct the program
   contains. Secondary diagnostics after such a fail-closed rejection are not compared. Full VIB ID-set parity is
   required for every other program. The classifier (`Engine.ClassifyFrontends`) implements this rule, and SPEC states
   it from `candidate-freeze-3` on (paragraph "Diagnostic parity across compilers"). *Interpretation:* when a program
   contains several banned or cut constructs, each frontend has to report the documented ID of at least one of them,
   not of all of them, because a fail-closed frontend may stop at the first one.
2. **Object and collection member initializers** (`new S { A = 1 }`) are banned by design, and every frontend reports
   `VIB000`. SPEC lists them in the §1 closed subset from `candidate-freeze-3` on. Catalog row:
   `banned.object-initializer`.
3. **Collection expression returned as a span** (`static ReadOnlySpan<int> G() { return [1, 2]; }`, `return [];`,
   `=> [1]`) is banned by design, and every frontend reports `VIB007`. This follows from the SPEC §3.7 storage
   contract: a collection rvalue has its destination's storage, and there is no static-data promotion. Main adds the
   SPEC text. Catalog row: `banned.collection-return-span`. The tagger sets it for a reachable return; a return
   after an unconditional `Panic` stays `coll.returned`, which every frontend accepts. Matrix probes: `r6`, `r7`.
4. **`foreach (var e in span)` over a `Span<T>` is a shared reborrow (N8).** SPEC §3.2 stands. Reads of the span in
   the body are valid, writes and other exclusive uses in the body are `VIB003`, the span is usable after the loop, and
   a `ref var` loop is exclusive (reading the span in its body is `VIB003`). The matrix has one `decided.*` row for
   each of these points. N8 (Stage-0 moves the span; self takes an exclusive loan for a `var` loop) is open and
   assigned.
5. **Semantic warnings promoted to errors.** Seven IDs are promoted to errors (`VIB000` in self), like CS9192:
   CS0183 and CS0184 (the expression is always / never of the provided type), CS8519, CS8520, CS8793 and CS8794
   (the expression never or always matches the provided pattern or constant), and CS9191 (a `ref` argument for an
   `in` parameter). The promotion goes into the Stage-0 driver, `Vibranium.App.props` and the Std project; the owners
   of the Stage-0 and the self-checker fixes agreed this final list against the pinned SDK. The SDK oracle reads
   `<WarningsAsErrors>` from the target tree's `Vibranium.App.props`, so it models them as errors as soon as the
   target tree lists them. On `candidate-freeze-2` they are still warnings, and warning-only programs are compared
   like valid ones (§1). Round-11 candidate 1 has the promotion, so there the N1 `in` form (CS9191) and N9 (CS8793) are
   C#-invalid programs that every frontend must reject.
6. **Expression variables across switch sections follow Roslyn** (former question 1). `out var` and pattern variables
   declared in section statements belong to the switch block: a duplicate across sections is CS0128, and use across
   sections is valid. SPEC will state it. Catalog rows: `switch.cross-section-var` (supported) and
   `switch.cross-section-dup` (near-miss).
7. **Union switch statements follow Roslyn** (former question 2). An unmatched exit is kept (CS0161, CS0165, CS0163), and
   every arm's body is checked, including a `_` arm after complete arms. Self skipping that arm is N5 (open).
8. **IDE code-style rules are style, not language validity** (former question 3). They are outside conformance.
   `sdk-crosscheck` keeps reporting IDE-only differences separately.
9. **Cut C4: patterns on a value of open type-parameter type.** Every pattern except `var` and discard whose input is a
   value of an open generic type parameter (`x is 0`, `x is > 0 or <= 0`, `x is int`, `x is Box b`, and switch arms or
   case labels of those kinds) is rejected by the Analyzer, Stage-0 and self with one VIB000 at the governing
   expression: "Patterns other than var and discard on a value of generic type parameter type are not supported in
   Vibranium v0.2." `x is var v` and `_` stay valid. Reason: Stage-0 miscompiled `x is > 0 or <= 0` for `T = long`
   (native true, managed false), and the compilers disagreed on `x is Box b`. Owners: `/source-validity` (self) and
   `/fix-r10-stage0-da` (Analyzer, Stage-0). Catalog row `cut.generic-pattern` (matrix probes only: the generator has
   no generic-method production); matrix rows in §5.2.
10. **Cut rules apply where code can run** (follows from item 11). All three frontends
   implement the cuts in their flow analysis, so a cut
   shape in code that Roslyn proves unreachable (after `return`/`break`, in a `when false` section, in `if (false)`, in
   a section of a `switch` on a constant that none of its labels can match) or after an unconditional Panic is accepted
   by every frontend, as the VIB007 rule after a Panic is (§5 `coll.returned`). The tagger follows this: it does not
   emit `cut.*` tags for such code (probes `c2u_*`, §6.8; constant selectors: probes `k*`, §6.10). The declaration
   cuts C5–C9 are the exception: they are syntactic (one VIB000 per file at the first occurrence), so the tagger keeps
   their tags in unreachable code. The vib-reject tags of the activation profile follow the flow rule.
11. **Flow checks follow Roslyn reachability (N13, decided: option A).** Borrow, move and definite-assignment checks
   do not run in code that Roslyn proves unreachable: a `when false` section, code after `return`/`break`/`continue`,
   the body of `if (false)`, and a `default:` section after value cases that already cover the input (CS0162). As
   decided earlier for VIB007, they also do not run in code after an unconditional Panic. A use after move or an
   overlapping exclusive borrow there is accepted. Self follows this rule since its N11 fix. On Round-11 candidate 1, Stage-0 and the Analyzer
   still check an unreachable `default:` section. That is N13 (owner `/fix-r10-stage0-da`). The Stage-0 fix was
   delivered after candidate 1, and its owner verified it with this tool: all four N13 matrix rows as expected, CI
   seed 1 × 300 with `--fail-on open` clean. So N13 has status `fixed`. Catalog row
   `flow.unreachable-section`; matrix rows `decided.unreachable-default-use-after-move`, `…-bool-default-…`,
   `…-default-overlap` (accept) and the control `decided.reachable-default-use-after-move` (VIB001).
12. **Call activation (Round-11 model, decided).** Within one call, receiver call or assignment, an earlier argument,
    receiver or assignment target creates a loan that stays live until the call or store: `in`, `ref readonly`,
    `ref`, `out`, view arguments, shared and `[Mut]` receivers, `this in`/`this ref` extension receivers and
    assignment targets (two-phase: `[Mut]` receivers, `ref`/`out` arguments and assignment targets are reserved and
    allow shared reads of the base until activation). Moving a place that is equal to, a prefix of or an extension of a
    place under such a loan is VIB003; moving a place first and then borrowing it in a later argument is VIB001. An
    unrelated owner is accepted, `x = Move(x)` (own slot) is legal, and a plain struct-field target `s.F = Move(s)`
    keeps the existing rules (VIB001; not generated). Moving `h.N` while `h` is under a loan is VIB003, not VIB005; an
    unrelated field move stays VIB005, so the accepted controls move whole distinct owners. The activation profile
    encodes these as the vib-reject tags `ca.borrow-then-move` (VIB003) and `ca.move-then-use` (VIB001).
13. **Views returned through parameters, temporaries and declarations (Round-11 audit).** A `[return: LifetimeOf]`
    or elided result that may return a view of another input is VIB007 at the declaration (two borrowed inputs without
    `LifetimeOf` are ambiguous, VIB007); mutating an input the result depends on while the result lives is VIB003; a
    span returned from a call on a `Span<T>` is a reborrow of that span (`view.reborrow-parent-after` is valid,
    O11-5). A conditional whose arm views a call temporary may be passed as an argument but not stored in a local
    (VIB007). Constant
    out-of-range and special float → `nint`/`nuint` conversions follow the runtime saturating conversion (O11-4), and
    `nint`/`nuint` `MinValue`/`MaxValue` are supported (SPEC §5.3, O11-7).
14. **No instance call on `this` in a constructor (N15, decided: SPEC aligned to the implementations).** In v0.2 a
    constructor may not call an instance method on `this`, read an instance property through `this` or let `this`
    escape, whatever is assigned: VIB008, as the Analyzer, Stage-0 and self already report. SPEC §2(2) said this only
    while a non-zero-safe field is unassigned; the fuzzer's `decl.this-call-after-init` control (every field assigned,
    then `K += Peek()`) was rejected by all three frontends, which the differential oracles cannot see but the
    activation run exposed through the O11-11 `__this_N` VIB001 that Stage-0 and the Analyzer add. The restriction is
    listed in SPEC and can be relaxed after v0.2. `decl.this-call-after-init` is now a vib-reject row (VIB008), the
    valid control is `decl.ctor-static-call` (fields assigned, a static helper called with the parameter), and the
    matrix has the rows `decided.ctor-this-call-after-full-init`, `…-zero-safe-fields-only`, `…-before-init` (VIB008)
    and `decided.ctor-static-call-control` (accept).

15. **Cuts C10–C20 (user-approved 2026-11-17).** Eleven more constructs are cut from v0.2, with the C5–C9 policy:
    every frontend reports exactly one VIB000 per file per cut, at the cut's first occurrence in source order, with the
    exact message; secondary diagnostics are not compared, and the programs stay C#-valid.

    | Cut | Construct | Anchor | Catalog tag |
    |---|---|---|---|
    | C10 | a `ref readonly` parameter (`in` parameters, `ref readonly` locals and returns stay) | `ref` | `cut.ref-readonly-param` |
    | C11 | declaring any symbol named `_` (local, parameter, field, property, method, type, type parameter, label, `foreach` variable, namespace, using alias, query range variable, tuple element name; discards stay) | `_` | `cut.underscore-decl` |
    | C12 | an optional parameter (default value, `[Optional]`, `[DefaultParameterValue]`) | the parameter's first token | `cut.optional-param` |
    | C13 | a named argument out of its parameter's position (named arguments in position stay) | the argument name | `cut.named-arg-order` |
    | C14 | assigning to `this` (simple, compound, deconstruction, `++`/`--`) and `ref this`/`out this` as a writable ref | `this` | `cut.this-assign` |
    | C15 | a conditional ref expression `c ? ref a : ref b` | `?` | `cut.conditional-ref` |
    | C16 | a collection expression nested in another one, or as a `?:` branch or a switch-expression arm value | the inner `[` | `cut.nested-collection` |
    | C17 | `nint`/`nuint`/`IntPtr`/`UIntPtr` in any position | the type token | `cut.native-int` |
    | C18 | a statement directly in a switch section that declares a switch-block local, including a local function (which reports both C1 and C18) | the statement's first token | `cut.switch-section-decl` |
    | C19 | a C# constant condition of `if`/`while`/`do`/`for`/`?:`, a constant `when` guard or switch selector (literal `true` loops and `for (;;)` stay) | the condition's first token | `cut.constant-condition` |
    | C20 | relational patterns and the `and` combinator, anywhere | the operator or `and` | `cut.relational-and-pattern` |

    The messages are the ones in `CutScan.Cuts` (copied from the owner's cut list). The tool checks each frontend's
    message and anchor (`cut-anchor`, §1) with its own Roslyn scan, so the check does not depend on any frontend's
    implementation; the generator no longer produces these forms (§2), and the matrix has probes, controls and C11
    near-misses for each cut (§5.2). Following item 10, the cut-flow rule does not apply to C10–C20: they are
    syntactic and are reported in unreachable code too. C10 and C17 supersede the parts of items 12 and 13 about
    `ref readonly` parameters and `nint`/`nuint`, and C20 the relational forms of the C4 examples in item 9.

    **C21 (decided by main 2026-11-18, from rule-ledger finding F11; in candidate 6).** A base constructor initializer
    `: base(...)`, with or without arguments, on any constructor, is VIB000 with the message `Base constructor calls
    (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance.`, anchored at the `base` token,
    with the same policy (one per file at the first occurrence, secondary diagnostics not compared). C9 (`: this(...)`)
    is unchanged. Before the cut, the Analyzer and Stage-0 accepted `: base()` on a sealed class while self rejected it
    with another message: a parity gap on C#-valid input. Catalog tag `cut.base-ctor-call`; the matrix has the plain
    form, a form with arguments (which needs inheritance, banned by design), two classes with `: base()` in one file
    (one VIB000, at the first) and C9 with C21 in one file, plus the control `control.c21-ctor-without-initializer`
    (§5.2). No generator emits `: base(` (`gen-stats`: 0 C21 forms in every profile).
16. **Native call-stack exhaustion is excused (tool rule, release-6).** SPEC excludes call-stack exhaustion from the
    parity guarantee, and the stack a frame needs depends on the compiler and the optimization level. A native leg
    that crashes while the managed run finished is run again with an unlimited stack; when it then matches, it is
    excused and listed (§1). Found on Round-11 candidate 4 (§6.12).
17. **A borrow-rejected move is not a move (F3-clarified, decided by main for candidate 6).** A move that is rejected
    because the place is borrowed (VIB003) does not consume its source, so no VIB001 follows it, on later loop
    iterations or later uses, in any frontend and any shape (loops, views from `AsReadOnlySpan`/`Lines`, call
    arguments). It still reports every violation of the move's own form: moving a field out of a class or struct is
    VIB005 whether or not the move is also borrow-rejected, so `var it = h.Items;` while `h` is borrowed is exactly
    {VIB003, VIB005}. Found by the soundness profile on the combined cut source (§6.14): the Roslyn-side F3 change
    dropped both the VIB001 and the VIB005, self kept both. Owners: the Roslyn side restores VIB005, self ports the
    no-VIB001 part. The root `R12-F3` tracked it with status `new`; candidate 6 carries both parts, and from
    release-7 the root is `fixed` (§6.17).
18. **A collection expression assigned to a by-value view parameter (N16, decided by main 2026-11-18: option a).**
    `static ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2]; return p; }` (also on one branch, with `[]` or
    returning `p[1..]`) is rejected with exactly {VIB003, VIB007} by every frontend, as SPEC §3.7 states (the storage
    belongs to the destination's lexical scope; VIB003 when a view outlives it, VIB007 when it escapes the method)
    and as the local-destination form already behaves. Self reports both; the Analyzer and Stage-0 miss VIB003 when
    the destination is a parameter. Owner: the Roslyn side (`BorrowAndMoveAnalyzer`). Fixed in candidate 6 pre5,
    together with the value of an assignment expression whose destination is a parameter (`return p = [1, 2];`,
    `=> p = [1, 2];`, `q = p = [1, 2]; return q;`, `return (p = [1, 2, 3])[1..];`), which gets the same
    {VIB003, VIB007}. The root `N16` is `fixed` (§6.17).
19. **Moved views, loop-reassigned views and assignment values (N17, N18; decided A+ on the Roslyn side, V4 in
    self).** SPEC §3.2 item 1: `Span<T>` and `OptionMut<T>` are `!Copy`, so `Span<int> t = s;` moves `s`, and the
    value of an assignment `(s = e)` used as a receiver, argument or chain moves `s`. A moved source carries no
    borrow and an overwritten view's old borrows end. Hence:
    * a use of the moved source after a plain move is exactly {VIB001} (`t = s; use t; use s`, also with the owner
      read or written after the destination's last use, and for `OptionMut<T>`); with the move on one branch only it
      is {VIB001, VIB003}; reading the owner while the destination is still live is {VIB003};
    * a view declared before a loop and reassigned in it by `t = s`, `t = Id(s)`, `n += (s = b.AsSpan()).Length` or
      `n += Len(s = b.AsSpan())`, and used after the assignment, is accepted, with managed output equal to native;
      the old value used at the top of the next iteration while its owner is written stays {VIB003}, and a source
      moved by an assignment value and used at the top of the next iteration is {VIB001};
    * a use of the view after its assignment value was used (`(s = ..).Length`, `[0]`, `[1..].Length`,
      `Len(s = ..)`, `t = s = ..`, `(m = o2.AsMut()).IsSome`) is exactly {VIB001}; without a later use, or with a
      `ReadOnlySpan<T>` (`Copy`) destination, the program is accepted;
    * a live view rebound to another owner (`t = b.AsSpan()`, `t = s`, `t = Id(..)`, a `ReadOnlySpan<int>`), then
      its old owner mutated (`a.Add(7)`) while the view is still used, is accepted, straight-line and in a loop; a
      view derived from the old value (`u = t[..1]`) that is still live is {VIB003}. On pre8 the Analyzer and Stage-0
      reject the loop form with VIB003 (N17); the straight-line forms are accepted everywhere.
    On candidate 6 pre8 the Analyzer and Stage-0 added VIB003 or rejected the loop forms (root `N17`, fix
    roslyn-assign-value-cascade), and self accepted or lacked VIB001 after some assignment values (root `N18`,
    self-fix V4); both fixes are in pre9 and both roots are `fixed`. The `ViewMove` anchor
    (§2) generates every form; the shapes come from /roslyn-n16's 83 probes (corpus `probes/cascade/`).

20. **Switches over unions with whole-union labels (N19; N20 decided as a ban).** A switch over `Option<T>` or
    `Result<T, E>` tests its labels in order on one scrutinee value, as in C#; SPEC §3.4 then borrows a place
    scrutinee and consumes a call scrutinee, whose taken arm's bindings own the payload. Labels that test the whole
    union are valid: `not` of a case type, an `or` of the case types, a guarded `var w`, a case type with a
    designation, a final `var w`, with or without `when`, in statements and expressions, exhaustive or with a default,
    from a place (borrowed in place) or a call (consumed). Managed, Stage-0 native and self native must print the
    same. On candidate 6 pre8 vib-self dropped such statement labels and rejected some valid forms (root `N19`,
    self-fix-v V5, in pre9); on pre9 a false VIB001 after a statement with only whole-union labels plus `default:`
    remained (self-fix-w, in pre13; N19 is `fixed`). The `UnionSwitch` anchor (§2) generates every form (§6.20).

    **Linear unions (N20, decided by main 2026-11-18, narrowed the same day).** In a switch statement or expression
    over a linear union (`Option<R>`, `Result<R, E>` with `R : IDisposable`) with an rvalue or `Move(o)` scrutinee, an
    arm that tests a variant (`Some<R>(...)`, `NoneType`, `Ok<R>(...)`, `Err<E>(...)`) may not carry a `when` guard, a
    variant may be tested by at most one arm, and `not` and `or` patterns are not allowed: each is exactly {VIB006} on
    all three frontends. A form that all three frontends accept and that runs correctly stays valid: `var u` over the
    whole union (statement and expression, rvalue and `Move(o)`) and a guarded discard arm (`_ when p < 2 => 3`) after
    the variant arms. A place scrutinee keeps what all three report today (VIB006 for borrow-matching a linear union
    in place, plus VIB005 where an arm moves the payload): the expectation is equal sets, not a fixed set. A type
    pattern over the whole union (`case Option<R> u:`) is C#-invalid (CS8121). On candidate 6 pre9 the Analyzer and
    Stage-0 add VIB001 "Use of moved value 'temporary value'" at the scrutinee to every banned form except `or`, and
    self accepts the guarded and twice-tested forms (it gives {VIB006} for `not`); both fixes are in candidate 6 pre13
    (roslyn-linear-switch, with the SPEC sentences in §2 and §3.4, and self-fix), and root `N20` is `fixed`. The
    matrix rows `decided.linear-switch-*` (§5.2) check the decision, and the 59 probes of `probes/linear-switch/`
    (expectations in `EXPECT.tsv`, pre9 verdicts in `VERDICTS.tsv`) cover every shape.

No question is open.

## 8. Honest limits

* **Scope of the grammar.** The generator covers the catalog in §5 and nothing else. The default profile does not generate Std APIs, `[Mut]` receivers beyond the class-instance anchor, `OptionRef`/`OptionMut`, `Span` slicing and ranges or `foreach` over UTF-8 views. The extended profile adds those, as statement templates with their own tags, but only in the shapes listed in §2. Files, processes, sockets, `Interner`, generics (C4 is covered only by the matrix), user `ref struct`s, `[LifetimeOf]` and multi-file programs are generated by neither profile. Clean results say nothing about them.
* **Program size.** Programs have at most 30 printed lines (45 in the extended profile, 60 in the activation profile; deterministic re-draws), so deep nesting and long-range interactions are only sampled through the optional third anchor.
* **Extended templates are coarser than the AST anchors.** The extended anchors print whole statements as raw lines. The reducer can delete those lines but cannot simplify inside them, so extended buckets are less minimal. Their tags come from the template rather than from the parse.
* **The expectation oracle is only as good as its expected IDs.** vib-reject rows encode SPEC and the decided
  call-activation model (§7.12–7.14) as the IDs a frontend must report. A shared deviation outside those rows is still
  invisible (N15 was found only because the O11-11 VIB001 made the ID sets differ), and a row whose expectation is
  wrong would report every frontend; the rows were checked against the model's owners before the run. The oracle
  checks the IDs, not the positions or messages, and it is off for programs that contain a banned or cut construct.
* **Activation templates are raw lines.** Like the extended anchors, the activation scenarios are printed as whole
  helper functions and types. The reducer can drop a helper but not simplify inside it, so activation buckets keep a
  complete scenario, and many signatures map to one root (673 buckets for 13 roots in §6.11).
* **Near-miss acceptance is a statistic, not an oracle.** A `vib-invalid` program that every frontend accepts is counted per tag (§4) but is not a disagreement. A reduction that kept the tag while removing the offending line could not be told apart from a real hole. The counts must be vetted by hand (§6.8).
* **Cut tags follow reachability only for generator shapes.** The tagger skips `cut.*` tags after `return`/`break`/`continue`/`Panic` in the same block, in `when false` sections, in `if (false)`, and in the sections of a `switch` on a constant (an enum member, a const local or field, or a literal) that no label can match, including a `default:` after a label that matches for certain. Labels it cannot evaluate count as reachable. Other dead code (for example after a `switch` whose sections all end in Panic, or an `if` on a const local) still carries the tag. A cut shape there that every frontend accepts would show as a `banned-accepted` bucket and needs the same vetting.
* **Near-miss realism.** About 25 % of unmutated programs were C#-invalid up to release-5 (17 % in the default profile and 11 % in the activation profile with the release-6 generator, §2). Most of this is deliberate: Panic source continuation (CS0165), union switch exhaustiveness (CS0161/CS0165), rvalues to `ref readonly` (CS9193/CS8156; gone with C10) and subsumed patterns (CS8510). The share of valid programs that reach execution is therefore lower: about 15 % of all programs are executed.
* **Reduction is greedy and signature-preserving, not root-preserving.** Two root causes with the same coarse signature share a bucket until their minimized tag sets differ. Different minimized tag sets of one root become separate buckets, which is why a known root can own several buckets. The `--max-min` cap (6 per signature) bounds the cost but can hide a second root behind the same signature.
* **Oracles.**
  * The SDK oracle is Roslyn in process. `sdk-crosscheck` validated it against a real `dotnet build` (40/40 identical ID sets), but it does not model IDE code-style rules.
  * Self runs in process in its own `AssemblyLoadContext`. `selfcheck-modes` validated it against `dotnet vib-self.dll` (check output and IR byte-identical).
  * The native self leg calls the real `vib-self build` in process. Clang's own messages then go to the tool's console rather than into the bucket, so a self link failure is keyed only by `vib: clang-18 exited with code N`.
* **Execution.** Only `-O0` runs always. `-O3` with ASan/LSan runs on a 5 % sample (every accepted program in the soundness profile). Inputs are fixed (no stdin or arguments), and a 20 s per-run timeout applies. A native leg that exceeds it after the managed run finished gets one re-run with 200 s and is excused if it then matches (§1), so a slower native build is not reported as a disagreement unless it needs more than 200 s.
* **Non-terminating programs.** The generator can emit loops that never end, for example a step constant that is `0` or a nested anchor that overwrites the loop counter. It can also emit unbounded recursion. In the candidate campaigns this produced artefact buckets (4 in §6.4, 4 in §6.8). They are not fixed in the generator, because that would change the program behind every `(seed, index)` in the corpus. Instead, the comparison is skipped (`nonTerminatingSkipped`, listed in `nonTerminatingPrograms`) when the managed run times out or ends in a CLR stack overflow. Call-stack exhaustion is outside the SPEC parity guarantee. A native hang is still reported when the managed run finishes. Triage example: the one program skipped by the activation CI gate (seed 1, index 242, `CaCall>Loop`) is a `Loop` anchor `do { c3 += 1; c3 %= 3; if (acc <= 1) continue; c3 -= acc; } while (c3 < 2);` that, for `acc = 29`, keeps `c3` negative forever in C# semantics, so it is a generator artefact; every frontend accepted it in milliseconds. The activation recall run (§6.11) skipped 3 of 13,484 programs (before the list existed).
* **Both frontends wrong in the same way is invisible.** The oracles compare the frontends with each other and with Roslyn. A SPEC-valid program that Stage-0 and self both reject with the *same* IDs is not reported, and neither is a SPEC-invalid program that C# accepts and both frontends accept. N8 was visible only because the two frontends reported different IDs. The SPEC-level checks that do not depend on a disagreement are the matrix (§5.2: banned, cut and decided rows), the expectation oracle for vib-reject tags (`spec-accepted`, `spec-wrong-id`), the soundness oracle (§1) and, from release-7.1, the expected-accept oracle for the `ViewMove` shapes (`spec-rejected`); a shared over-rejection of any other accepted shape stays invisible.
* **Printed shape vs AST.** The printer does not add parentheses around an operator selector before `switch` (`a % b switch { … }`). C# then binds `switch` to `b`, so such a program's type annotations and tags describe the AST, not the C# parse. This was kept on purpose, because it found N7, and it is tagged `switch.sel-unparenthesized`.
* **Recall measurement.** The recall figures in §6.2 are measured against the Round-10 roots on the *frozen* tree. §6.11 measures the activation profile against the Round-11 audit roots on candidate 3; that profile was written knowing the audit's root descriptions, so it shows that the profile reaches those shapes, not that it would have found them blind. Rediscovery means the classifier's predicate in `known-roots.json` matched, and the predicates were written from the Round-10 descriptions. The N-root predicates were written *after* vetting the buckets they explain, so they say nothing about recall.
* **The soundness oracle sees only misuse that happens.** ASan reports a dangling view only when the owner's buffer
  was really freed or moved (a growth past capacity, a consuming call that drops the owner, a reassignment, a scope
  exit) and the program then reads it; a view of storage that stays allocated (a move into a local that lives on, a
  `Take` that leaves the slot in place) reads stale but valid memory, which shows up only if the output differs from
  the managed run. Its templates are raw helper functions (like the activation profile), and many scenarios are
  rejected on purpose, so a program with two scenarios runs only when both are accepted.
* **The static IR contract checks four local rules.** It reads each emitted function as text: the definition of
  each shift count and the `icmp` tests of each divisor and dividend, and whether a test reaches a `br i1`. It does not
  check that the branch leads to the panic, that the test dominates the division, or anything about memory: a wrong
  `Vec` capacity, a missing bounds check or a wrong drop is outside it (B4 and R2 in §6.16). Runtime functions
  (`@__vib_*`) are skipped, and IR that a compiler fails to emit is listed as `not emitted`, not as a disagreement
  (an execution run reports it as `native-build-failure`).
* **Hanging legs are not minimized (release-7.1).** A `run-timeout` bucket, and any execution bucket of a program with
  a hanging leg, keeps the whole original program (§3), because every reduction test would wait out the 20 s budget.
  The self loops of the N19 class (§6.20) made a soundness `ci` gate take more than 17 minutes before this rule and
  the no-slow-re-run rule of §1.
* **The banned linear switch forms are not generated.** `UnionSwitch` matches a linear payload only with the union
  cases, one arm per case and no guard (§2). The banned forms of N20 (§7 item 20) and the valid `var u` and guarded
  discard forms over a linear union are checked by the matrix rows of §5.2 and the `probes/linear-switch/` probes
  only, so they are not combined with other anchors.
* **Seeded-bug recall is the author's own kit.** The 27 mutations (§6.16, §6.21) were written by the tool's author, who
  knew the detectors. A bug counts as caught when any detector shows a new or more frequent signature than on the
  unmutated tree; the finding is checked by hand to name the bug, not by the runner.
* **Machine load.** All numbers were measured under the shared `heavy` slot limit on a loaded 24-core WSL2 machine, with 5 workers on the frozen tree and 3 on the candidates. The two Round-11 campaigns (§6.9) ran at the same time. Expect higher throughput on an idle CI runner.
* **Convergence is a sample, not a proof.** The candidate campaign's root curve was still rising at minute 38 (§6.4). On Round-11 candidate 1 the default campaign found N14 at minute 36, and the extended campaign found a new N13 shape at minute 87 (§6.9). On Round-11 candidate 2, 180 campaign minutes with new seeds found no disagreement (§6.10). That covers the generator's grammar (§2) and nothing outside it, and longer or differently seeded runs may still find roots.

## 9. Wiring (applied)

Main decided to wire the tool in. This deliverable changes two existing files, `Vibranium.slnx` and
`.github/workflows/ci.yml`. The change against `candidate-freeze-2`:

```diff
--- a/Vibranium.slnx
+++ b/Vibranium.slnx
@@ -18,4 +18,7 @@
   <Folder Name="/tests/">
     <Project Path="tests/Vibranium.Tests/Vibranium.Tests.csproj" />
   </Folder>
+  <Folder Name="/eng/">
+    <Project Path="eng/conformance/Vibranium.Conformance.csproj" />
+  </Folder>
 </Solution>
--- a/.github/workflows/ci.yml
+++ b/.github/workflows/ci.yml
@@ -60,6 +60,11 @@
       - name: Verify Self-Hosting Triple-Identity Bootstrap (vib-self)
         run: ./eng/bootstrap/bootstrap.sh --skip-benchmark
 
+      - name: Differential conformance fuzzer (fixed budget, see docs/CONFORMANCE.md)
+        env:
+          CONF_FAIL_ON: open
+        run: ./eng/conformance/run-conformance.sh ci
+
   formal-proofs:
     name: Lean 4 Formal Proofs (VibraniumFormal)
     runs-on: ubuntu-24.04
```

* `Vibranium.slnx` builds the tool with the solution, so the AGENTS.md gate keeps it at 0 warnings and 0 errors. The
  tool has no tests of its own, so `dotnet test` is unchanged.
* The CI step runs `run-conformance.sh ci`, which builds `vib-self` and the tool in `Release` and then runs seed 1 with
  300 programs. On the candidate this takes 50 s at 3 workers, finds 0 buckets and exits 0 (60 s, 0 buckets and exit 0
  on Round-11 candidate 1).
* The step uses `CONF_FAIL_ON=open`: it fails on any bucket except one attributed to a `cut` root that the tree does
  not implement yet. Every root in `known-roots.json` is `fixed` on Round-11 candidate 2: O1–O11, A1–A3, both cuts and
  N1–N14. So a hit is either an unexplained disagreement or a regression.
* The step started with `CONF_FAIL_ON=new` while N1–N10 were `open` (assigned, not fixed). Once the Round-11 cycle-1
  fixes landed, all N roots were set to `fixed` and the step was switched to `open`. Use the same lifecycle for later
  roots: add a vetted root as `new`, set it to `open` when it is assigned (switching the step back to `new` if the
  tree is shipped before it is fixed), and to `fixed` when the fix lands.
* Checked on the candidate with the wiring applied: `dotnet build --disable-build-servers Vibranium.slnx` (now with
  the tool) exits 0 with 0 warnings and 0 errors, and the CI step's command exits 0 (seed 1, 300 programs, 0 buckets).
* Long campaigns (`run-conformance.sh campaign`) are better run on a schedule than on every pull request.

**Conformance steps (release-7).** Three more steps run the activation and soundness profiles with the same gate,
and the matrix, reusing the first step's build (`CONF_SKIP_BUILD`):

```diff
--- a/.github/workflows/ci.yml
+++ b/.github/workflows/ci.yml
@@ -65,6 +65,25 @@
           CONF_FAIL_ON: open
         run: ./eng/conformance/run-conformance.sh ci
 
+      - name: Conformance activation profile (fixed budget, see docs/CONFORMANCE.md)
+        env:
+          CONF_FAIL_ON: open
+          CONF_PROFILE: activation
+          CONF_SKIP_BUILD: "1"
+        run: ./eng/conformance/run-conformance.sh ci
+
+      - name: Conformance soundness profile (accepted programs run ASan/LSan-clean at -O0 and -O3, see docs/CONFORMANCE.md)
+        env:
+          CONF_FAIL_ON: open
+          CONF_PROFILE: soundness
+          CONF_SKIP_BUILD: "1"
+        run: ./eng/conformance/run-conformance.sh ci
+
+      - name: Conformance feature matrix (banned, cut and decided rows on every frontend, see docs/CONFORMANCE.md)
+        env:
+          CONF_SKIP_BUILD: "1"
+        run: ./eng/conformance/run-conformance.sh matrix
+
       - name: Mutation oracle self-test
         run: ./eng/mutation/run-mutation.sh self-test
 
```

* The seeded-bug recall (§6.16) needs all four steps: the default gate alone catches 10 of the 25 mutations, with the
  soundness step 20, with soundness and the matrix 22, and with all four 25. With the release-7.1 streams (§6.21)
  the numbers are 11, 21, 23 and 27 of 27.
* Each fuzz step runs seed 1 with 300 programs. On candidate 6 at 3 workers the default gate takes 55–62 s,
  activation 64–71 s, soundness 69–105 s (depending on machine load) and the matrix about a minute, well inside the
  job's 45-minute timeout.
* In the soundness step every program that is C#-valid and accepted by every frontend runs at `-O0` and `-O3` under
  ASan/LSan, and must match the managed run. On the combined cut source it failed on the 7 `regression:R12-F3`
  buckets; on candidate 6 it passes with 0 buckets (§6.17).
* The matrix step fails when any row is a `violation` or a `probe-error` (and, from release-7.1, on a `known:` row of
  an open root under `CONF_FAIL_ON=open`). On candidate 6 all 130 rows of release-7 pass (125 OK, 5 OK*).
* The activation step failed on candidate 6 before pre5 on the N16 buckets (`new:N16`). With the Roslyn-side fix in
  pre5 and N16 set to `fixed`, all four steps pass (§6.17), so all three are enabled.
* From release-7.1 the activation step generates the assignment-value cascade shapes (§6.19), and every fuzz step
  generates switches over unions with whole-union labels (§6.20). On candidate 6 pre8 the default step failed on 8
  `new:N19` buckets, the activation step on 10 `new:N17`, 1 `new:N18` and 7 `new:N19`, and the soundness step on 11
  `new:N19` (95 s, 126 s and 141 s). On candidate 6 pre9, with those fixes in, N17 and N18 are `fixed`; the default
  and activation steps fail only on the N19 residual (`known:N19`, fix self-fix-w), the soundness step passes, and the
  matrix fails only on 6 `known:N20` rows (fixes roslyn-linear-switch and self-fix), §6.21. The matrix step now
  honours `CONF_FAIL_ON` like the fuzz steps. On candidate 6 pre13 all those fixes are in, N19 and N20 are `fixed`
  (release-7.2), and all four steps pass with `CONF_FAIL_ON=open`: default, activation and soundness with 0 buckets
  in 50 s, 54 s and 80 s, the matrix with 144 rows (139 OK, 5 OK*) in 63 s (§6.22).
