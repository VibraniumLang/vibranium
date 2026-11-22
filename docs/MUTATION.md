# Vibranium v0.2 Invalid-Program Mutation Oracle

This document describes the invalid-program mutation oracle in `eng/mutation/` (`vib-mutation`) and the
contract it checks. The tool mutates real Vibranium sources into programs that the C# SDK rejects. It then asks
each frontend whether it wrongly accepts them: the self-hosted compiler (`vib-self`, managed or the native
stage2 binary), Stage-0 `vib` and the Analyzer frontend. Results are bucketed by the SDK's first CS error for
triage.

The differential fuzzer (`docs/CONFORMANCE.md`) generates programs from a grammar and compares the
frontends with each other on valid and invalid input; it also owns sanitizer execution of valid programs. This
tool starts from real programs instead: samples, `Vibranium.Std`, `vib-self`, test sources, the conformance
corpus and external probes. It produces C#-invalid shapes that a grammar-driven generator does not reach. The
campaign and the CI gate only ask a frontend to *check* them; a separate `asan` mode builds and runs selected self
accepts to set their priority (§2, §6).

## 1. The contract for C#-invalid input (normative)

Every Vibranium compiler must be able to run without Roslyn in front of it. That includes `vib-self check|build|run`
and the native fixed-point binary `vib-stage2`. So the rule is the same on every path:

* **Accept only input that is both valid C# and valid Vibranium.** A *C#-invalid* program must be rejected by
  every frontend: `vib-self` (managed and native stage2), Stage-0 and the Analyzer. C#-invalid means at least one
  C# error under the project's settings (`docs/SPEC.md`, C# validity). That is either a *genuine error*, a
  diagnostic whose default severity is Error, or a warning that `Vibranium.App.props` promotes explicitly (the
  explicit Vibranium warning policy: CS0183, CS0184, CS8509, CS8519, CS8520, CS8524, CS8778, CS8793, CS8794,
  CS8846, CS9191, CS9192, CS9193). Accepting such a program is a **defect**, whatever its CS IDs are.
* **Severity classes set priority; they waive nothing.** `eng/mutation/severity-safety-ids.txt` lists the
  *safety-relevant* IDs, whose acceptance could break memory or type safety, ownership/linearity, or a
  `Vibranium.Std`/`Vibranium.Core` invariant. Those invariants are access control on internals, `readonly`,
  writes through `in` or `this`, definite assignment, layout-relevant generic constraints, conversions that
  reinterpret values, and control flow that could emit malformed code. A self accept under one of them is **P1**,
  or **P0** when `asan` mode shows a memory error or invalid IR for it. `eng/mutation/severity-other-ids.txt`
  lists the *other* IDs: duplicate members, forbidden or duplicate modifiers, unused-symbol warnings and similar.
  A self accept under one of them is **P2** and is listed under Known issues. An ID in neither file is
  unclassified and is treated as safety-relevant. These two files replaced v1's `mandatory-ids.txt` and
  `outside-contract-ids.txt`. They started from the same IDs, and later rounds added more.
  A former OUTSIDE ID that is a C# error is now an *other*-class
  defect, and a former MANDATORY ID that is only a blanket-promoted warning (CS8602, CS8625, CS9200) is now
  warning-only parity input.
* **The first CS error is triage, not the contract.** Each program counts once, under its first SDK error, with
  Roslyn's order as the key; later errors are often cascades. Fix work is grouped by first key and family.
  An accept is a defect if *any* of the program's errors is genuine or explicitly promoted.
* **Warning-only parity input is reported separately.** Some programs' only CS errors are warnings that only the blanket
  `TreatWarningsAsErrors` promotes. A warning alone does not make a program C#-invalid, so they are valid C#.
  Whether they are valid Vibranium is settled by parity with Stage-0 (`docs/CONFORMANCE.md`). The report lists them
  with self/Stage-0 agreement, and the gate does not fail on them. CS0649 (a field that is never assigned) is such a
  warning. It is split for triage by Stage-0's zero-safety of the field's type: `CS0649.zero-safe` is in the other
  class, and `CS0649` (a null hazard) is safety-relevant.
* **Subset rejections are reported separately.** A C#-invalid program that self rejects, but that also
  contains a construct the v0.2 subset cuts (C10–C20), may have been rejected for the cut, not for the C#
  error. Such a rejection does not show that self checks the C# error. It is counted apart so it cannot be
  mistaken for one.
* **Check, then evidence.** Campaigns and the CI gate only run `check`. The separate `asan` mode builds selected
  self accepts with self's own emitter and runs them once under ASan/LSan. That is how a memory-unsafe accept
  (P0) is told apart from a merely wrong one (P1). It is local evidence on our own compiler and test programs.
  A clean run proves nothing.
* **Stage-0 and the Analyzer.** Their accept of a C#-invalid program is a defect too. Stage-0 promotes only
  the `Vibranium.App.props` warning list (`VibraniumCompilerDriver`), so its accepts of warning-only parity programs
  follow that policy and are reported under warning-only parity. The Analyzer frontend is the SDK plus
  the analyzer, so by construction it cannot accept a program the SDK rejects. The check guards the wiring.

Each line of the two severity files is `ID<TAB>likely self component<TAB>reason`. The component (`parser`, `binder`
for declarations and types, `lowerer` for expression binding, conversions, access and control flow, `checker`
for flow analysis) is a triage hint, not a located fault.

## 2. How it works

1. **Corpus and bases.** The tool reads `samples/`, string literals with a `;` in `tests/Vibranium.Tests/`,
   `src/Vibranium.Std/`, `src/vib-self/` (whole files plus labelled projections of every static method and
   every top-level type, each wrapped as its own program) and `eng/conformance/corpus/`, plus optional
   external probe directories (`--probes DIR[:DIR…]`). A program becomes a mutation **base** only if the
   SDK accepts it and `vib-self check` accepts it. An SDK-invalid probe is kept unmodified as a
   *baseline probe*, and its first single-edit repair that the SDK and self accept becomes a base. Bases are
   picked per source group in a seeded hash order (`--seeds-per-group`).
2. **Mutants.** About 100 deterministic single-span operators, in families that aim at the contract:
   access (`field-private`, `member-private`, `accessor-private`, `sem-internal-member`,
   `sem-internal-field-write`, `sem-internal-static-call`), readonly and `in` (`field-add:readonly`,
   `type-add:readonly`, `method-add:readonly`, `parameter-add:in`, `in-parameter-write`,
   `sem-write-readonly-field`, `sem-write-getonly-property`, `sem-write-in-parameter-field`,
   `sem-write-readonly-struct-field`, `sem-write-readonly-indexer`, `sem-write-through-property`,
   `accessor-drop-set`, `foreach-write`, `using-write`, `assign-this`), definite assignment
   (`delete-initializer`, `delete-assignment`, `delete-out-assignment`, `sem-var-uninitialized`,
   `swap-adjacent-statements`, `delete-declaration`), conversions (`literal:*`, `variable-type:*`,
   `var-type:*`, `parameter-type:*`, `return-type:*`, `remove-cast`, `cast:*`, `sem-enum-to-int`,
   `conditional-branch-type`), references (`argument-add:*`, `argument-refkind:*`, `argument-drop-refkind`,
   `parameter-refkind:*`, `ref-local-drop-ref`, `ref-local-readonly`, `ref-return-local`,
   `ref-return-readonly`, `sem-property-as-ref-argument`), generics and layout (`generic-argument:*`,
   `generic-extra-arity`, `constraint-*`, `add-struct-constraint`, `struct-self-field`, `type-kind:*`),
   control flow (`misplaced-break`, `misplaced-continue`, `delete-return`, `return-value-in-void`,
   `switch-drop-terminator`, `duplicate-case-*`, `switch-expression-drop-*`), operators and names
   (`binary-operator:*`, `unary-operator:*`, `compound-assign:*`, `rename-identifier`, `rename-member`,
   `this-in-static`), and declaration noise (`duplicate-member`, `duplicate-local`, modifier
   additions/drops, `optional-parameter`, `swap-parameters`, …). `sem-*` operators use the base's semantic
   model; `sem-internal-*` name the non-public members of `Vibranium.Core`/`Vibranium.Std` read from metadata
   (for example `Vec<T>._count`). Each operator records at most `--per-operator` edits per base, chosen by a
   stable hash of the position so edits spread over the file. All candidates of all bases are then visited
   in one seeded hash order.
3. **Oracles.** A candidate the SDK accepts is discarded. Every SDK-invalid mutant is kept and run through:
   * **SDK**: Roslyn 5.12.0 in process with the `net11.0` reference pack, the app's implicit global
     usings, `VibraniumAppBootstrap.cs`, `Nullable=enable`, `WarningLevel=11`, `TreatWarningsAsErrors` and
     the `<WarningsAsErrors>` list read from the tree's `Vibranium.App.props`. Each error records whether it
     is a real error, an explicitly promoted warning or a blanket-promoted warning. For CS0649 it also records
     whether the field's type is zero-safe by Stage-0's predicate (§1).
     `verify-sdk.py` cross-checks repros against a real `dotnet build`. The first ID must match, and the build must
     report no ID that the oracle did not see. csc stops after declaration errors, so it may report fewer IDs.
   * **self**: `check FILE --sysroot src` only, as a child process. By default this is the managed
     `dotnet vib-self.dll`, under a managed-heap hard limit. With `--self PATH` to a native executable
     (bootstrap's `vib-stage2`), the binary runs directly under an address-space limit (`prlimit --as`).
     Accept means exit 0 and no diagnostic. Reject means exit 1 with a `VIB` diagnostic. Anything else
     (a crash, a timeout, `panic: out of memory`) is a failure, never an accept or a reject.
   * **Stage-0**: `VibraniumCompilerDriver.CreateCompilationFromPath` plus `VibraniumFrontend.CompileAndCheck`
     in process, the same path as `vib check`.
   * **Analyzer frontend**: SDK + `VibraniumDiagnosticAnalyzer` through `CompilationWithAnalyzers`, as in
     MSBuild. By construction it cannot accept a program the SDK rejects; it is kept as a sanity check of
     the pipeline, and its VIB IDs are recorded.
4. **Cut forms.** Each program is tagged with the C10–C20 constructs it contains (main's v0.2 cut list:
   `ref readonly` parameters, declared `_`, optional parameters, out-of-order named arguments, assignment to
   `this`, conditional ref, nested collection expressions, `nint`/`nuint`, declarations directly in a
   switch section, constant conditions, relational/`and` patterns). An ID whose every self accept contains a
   cut form is reported as *reached only through a cut*: those accepts should disappear once the cuts land.
5. **Reduction.** One representative per first key (preferring a self defect without cut forms) is reduced by
   syntax-aware ddmin over members and statements. The reduction keeps the first key, the error kind (so a
   defect never reduces to warning-only parity input), the self verdict and an entry point, and it never adds a cut
   form. Budgets: `--reduce` for self accepts, `--reduce-rejected` otherwise. Results: `repros/<KEY>.cs`, and a
   JSON sidecar with the reduced file's errors, cut forms and verdicts.
6. **Native evidence** (`asan` mode, separate from campaigns and CI). For each safety-relevant (or unclassified)
   first key with a self accept, the reduced repro and the smallest accepted programs are built by self's own
   emitter (`build --asan -O1`, clang-18 with ASan/LSan) and run once with no input, no arguments and a 10 s limit
   in a scratch directory (`--per-id`, default 3; `--all-ids` selects every self accept). Programs that use
   `TcpListener`, `Command(` or `Process.` are built but not run. A sanitizer report or IR that clang rejects marks
   the key P0 in the report; otherwise a safety-relevant accept stays P1. A clean run is not proof of safety.
   Results: `asan.jsonl` and `asan.md` in the results directory, and a column of the defect table.
7. **Growth curve.** The report recomputes, along the run, the distinct first keys, the first keys with a self
   defect, and how many of those are safety-relevant. It lists the defect keys first seen in the last third.
8. **Memory.** Oracle work runs in chunks of 8 × `--workers` programs, and compilations are dropped after each
   program. Each Stage-0 call goes through `VibraniumCompilerDriver`, which opens every platform reference
   with `MetadataReference.CreateFromFile`. That copies about 64 MB of images into native memory, and only
   finalizers free it. So the tool runs a full GC and drains the finalizer queue after every chunk, plus a
   compacting full GC every 10 chunks. `run-mutation.sh` also sets `MALLOC_MMAP_THRESHOLD_=131072` and
   `MALLOC_ARENA_MAX=4`, so freed images go back to the OS instead of fragmenting glibc arenas. Each
   `vib-self` child gets `DOTNET_GCHeapHardLimit` (`--self-heap-mb`, default 1024), because self
   allocates without bound on some inputs (CS0523: a struct field of its own type). The report records the
   peak working set of the tool and, under a cgroup cap, the cgroup's `memory.peak`. A native self child gets an
   address-space limit instead (`--self-native-mb`, default 4096).


## 3. How to run

```bash
eng/mutation/run-mutation.sh self-test                       # the tool's own assertions
out=$(mktemp -d)/m1                                          # must not exist yet; outside the repository
eng/mutation/run-mutation.sh campaign --output "$out" --limit 36000 --seeds-per-group 250 --per-operator 3 --workers 6
eng/mutation/run-mutation.sh report "$out"                   # re-render after editing the severity files
eng/mutation/run-mutation.sh asan "$out" --per-id 3          # native evidence for safety-relevant self accepts (P0/P1)
eng/mutation/run-mutation.sh check prog.cs                   # one program: SDK errors, kind, cuts, verdicts
eng/mutation/run-mutation.sh ci                             # the CI gate (known-open list empty on candidate 6)
# against the native fixed-point compiler instead of the managed one
export OUT_DIR=$(mktemp -d)                                  # exported: the next two lines use it
eng/bootstrap/bootstrap.sh --skip-benchmark                  # leaves vib-stage2 in $OUT_DIR
eng/mutation/run-mutation.sh ci --self "$OUT_DIR/vib-stage2"
python3 eng/mutation/verify-sdk.py --root . --results "$out" --ids CS0266 CS8151
```

With a native `--self`, the SDK oracle still compiles against the managed `Vibranium.Core.dll` and
`Vibranium.Std.dll` that `run-mutation.sh` builds; `--vibranium-references DIR` overrides their location.

The output directory must not exist and must be outside the repository. It holds:
* `report.md` and `defect-table.md` (severity, priority and, after `asan` mode, native evidence per key);
* `asan.jsonl` and `asan.md`, written by `asan` mode;
* `findings.jsonl`: one line per kept program, with its errors and their promotion kind, the error kind,
  the verdicts, the cut forms and the severity class;
* `bases.jsonl`, `bases/`, `mutants/` and `repros/`;
* `statistics.json`;
* `settings.json`: the seed, the budgets, the SHA-256 of the self (managed or native), the SDK oracle's Vibranium
  references, the tool, Roslyn and reference-pack binaries, and of the severity files.

Candidate enumeration and commit order are deterministic for a given seed, tree, self binary and severity tables,
whatever the worker count. Two parts of a run are not fully deterministic (§7). The SDK oracle's error order can
change the first key of a program with several errors. Repro reduction can then end on a different first key and be
counted as an infrastructure failure (3 and 2 in campaign-6 and campaign-6m).

Exit codes:
* 0: clean.
* 1: defect. A frontend accepted a C#-invalid program whose first key is not known-open.
* 2: infrastructure failure. A frontend crashed, timed out or ran out of memory outside a known-open key, or a
  repro stopped reproducing.
  A self crash on a C#-invalid program is also a defect; exit 2 takes precedence over 1 when both occur.

## 4. CI gate

`run-mutation.sh ci` uses a fixed seed (20261118) and budget: 400 mutants, 8 bases per group, no reduction. It
also runs every file at the top level of `eng/mutation/corpus/` unmodified, as a baseline probe. Probes are
deduplicated only among themselves, so a corpus file whose text is also a test literal or a base still runs. The
corpus holds reduced repros of the shapes under which self has accepted, or crashed on, a C#-invalid program; a
shape whose fix has not landed waits in `pending/` instead (below). The corpus gates these inputs, not every shape of
their first keys.
The gate fails when any frontend accepts a C#-invalid program, and when a frontend crashes. Warning-only parity
programs and subset rejections are printed as notes and never fail the gate.

`--known-open eng/mutation/known-open-ids.txt` lists the first keys whose defects are already assigned but
not yet fixed. Their accepts and self crashes are printed but do not fail the gate. The list keeps CI green
during the fix work; it does not waive anything. Remove a key when its fix lands, and the corpus then keeps
it fixed. The gate prints each listed key that no longer reproduces. Any self accept whose first key is not
known-open fails the gate, whatever its severity class. **On candidate 6 the list is empty and CI runs the gate
without `--known-open`** (§5.2).

Shapes that self still accepts while their fix is being written wait in `eng/mutation/corpus/pending/`, with
`PENDING.tsv` naming each file's family and fix. The gate does not read that directory, and the self-test checks
that. A file moves up into the corpus when its fix lands, and from then on it is gated. A whole key can also be
waived with `--known-open`, which hides every shape of that key. `pending/` holds only the listed repros, so new
shapes of the same key still fail the gate.

The gate costs 1 to 2 minutes after the Release builds: 113 s against managed `vib-self.dll` and 75 s against
native `vib-stage2` on candidate 6 pre14 (400 mutants from 35 bases, plus the 104 corpus files).

## 5. Results on candidate 6 (not a release certification)

Candidate 6 is `tree6` at manifest `candidate-c6-pre14.sha256`. It is pre12 (all self-compiler fixes S1–S6, V1–V5,
N19–N21 and O1–O5) plus the Roslyn-side linear-switch patch (pre13) and conformance-only changes (pre14). None of
these changes touches `src/vib-self` or `eng/mutation`. So every pre12 and pre13 self result below holds for pre14's
self as well; it was not re-run there. The bootstrap of the pre14 tree with this update reaches the fixed point
`f83408d9…`, and its native `vib-stage2` (sha256 `efdbbf4c…`) is byte-identical to pre12's. Each number below names
its tree and self binary. They are measured lower bounds, not a certification (§7).

### 5.1 Replay of everything found before

Every program that earlier campaigns or hand triage had found was replayed check-only against pre12 managed
`vib-self.dll` (sha256 `1a109cbd…`) and native `vib-stage2` (`efdbbf4c…`). Stage-0 was not part of this replay. The
set is all self accepts and crashes of campaigns 3–5, their repros, the frozen v1–v3 fixtures, every corpus file and
80 hand shapes: 8,619 programs after deduplication by content.

* **C#-invalid:** 7,658 programs, and both selves reject every one. That includes the 2,436 that c6-pre1 still accepted
  (families S1–S5 and O1–O4) and the S6 shapes (CS9330).
* **Warning-only parity:** 960 programs. Both selves accept the same 604 and reject the same 356.
* **Valid:** the remaining program is a valid control, and both selves accept it.
* **Native vs managed:** the verdicts and `VIB` IDs agree on all 8,619 programs. Neither self crashed or timed out.

So no first key reproduces a recorded shape on candidate 6. `known-open-ids.txt` is empty, and the CI step runs
without `--known-open` (§4). This is about the recorded shapes. The newer campaign-6 shapes (§5.3) are still accepted
under some of the same keys (CS0220, CS0103, CS0161, CS1503 and others), and under keys that have no gated file
yet (CS8337, CS8958).

### 5.2 CI gate on candidate 6

`run-mutation.sh ci` without `--known-open`, on pre14 with this list update. The gate runs the 400 seeded mutants
(seed 20261118) and the 104 top-level corpus files. Among the rejections, 26 are subset rejections: 15 corpus files
and 11 mutants.

| Self | Corpus files run | Self on the corpus files | Seeded mutants (bases) | Self on the mutants | Gate | Time |
|---|---:|---|---|---|---|---:|
| managed `vib-self.dll` | 104 | 104 rejected (15 subset) | 400 (35) | 390 rejected (11 subset); 10 warning-only parity accepts | PASS | 113 s |
| native `vib-stage2` (`efdbbf4c…`) | 104 | 104 rejected (15 subset) | 400 (35) | 390 rejected (11 subset); 10 warning-only parity accepts | PASS | 75 s |

The two selves agree on all 504 programs: the same verdicts and the same `VIB` IDs.

The 104 corpus files are recorded shapes under 62 first keys. They are:
* the r11-4 and c6-pre1 repros (every key of those two known-open lists);
* the O5 shapes (CS8121, CS0103, CS0246).

Self rejects every one of them. That keeps these shapes rejected; it does not show that every shape of those keys
is. The campaign-6 shapes (§5.3) are not gated yet: they wait in `corpus/pending/` (§4). Two of their keys,
CS8337 and CS8958, have no gated file at all.

The unmodified pre13 gate ran only 72 of its 99 corpus files. The other 27 had the same text as a test-source string
literal, and the corpus reader deduplicated probes against bases and test literals, so those 27 never ran. For 8
keys, that left no corpus file in the gate: CS0023, CS0117, CS0192, CS0400, CS1059, CS8156, CS8160 and CS8331.
Probes are now deduplicated only among themselves, and the self-test checks it. With that change, all 104 files run
on pre14, and both selves reject every one.

### 5.3 Campaign-6 and the stopping rule

Campaign-6 is the stopping-rule run on the fixed tree. It was run in full on **pre12** only. pre13 and pre14 have the
same self sources and the same native binary (§5), so the result is inferred for them, not re-measured. Settings:
* seed 20261122;
* 50,000 mutants, 400 bases per group, 3 mutants per operator and base, 6 workers;
* reduction budgets 32 and 8;
* the 18 external probes of campaigns 3–5.

It was run twice with the same seed: once with native `vib-stage2` as `--self` (campaign-6), and once with managed
`vib-self.dll` (campaign-6m). Each run has 50,018 findings: the 18 probes are findings 1–18, then the 50,000 mutants.
The stopping rule asks whether a new defect family still appears in the last third of the findings.

| Run | Self | Bases | SDK-invalid mutants kept | C#-invalid findings (first keys) | **Self accepts of C#-invalid findings (first keys)** | Self crashes | Warning-only parity (self / Stage-0 accepts / disagree) | Subset rejections | Stage-0 accepts of C#-invalid | Time | Peak memory |
|---|---|---:|---:|---:|---|---:|---|---:|---:|---:|---|
| campaign-6 | native `vib-stage2` `efdbbf4c…` | 763 | 50,000 | 48,774 (186) | **30 (9)** | 0 | 1,244 (1,006 / 1,005 / 1) | 1,420 | 0 | 5,054 s | 3.8 GB cgroup |
| campaign-6m | managed `vib-self.dll` `1a109cbd…` | 763 | 50,000 | 48,774 (185) | **30 (9)** | 0 | 1,244 (1,006 / 1,005 / 1) | 1,420 | 0 | 5,578 s | 3.3 GB cgroup |

The two runs kept the same 50,018 programs, compared by hash. **Native and managed self agree on all 50,018 of
them: the same verdict and the same `VIB` IDs (0 divergences).** The first-key counts differ (186 and 185) only
because of the SDK oracle's error order (§7). Both runs exit 2 rather than 0, because some reduced repros changed
first key during reduction: 3 in campaign-6 and 2 in campaign-6m, all rejected by self. That is a tool infrastructure
result, not a clean exit. Neither run had a self crash, timeout or out-of-memory.

The 30 accepts fall into five families. Stage-0 and the Analyzer reject every program in them, so each one is a
self-only defect:

| Family | Class | Missing check | First keys (campaign-6 findings) | First seen (finding index, third) |
|---|---|---|---|---|
| N1 | safety (P1) | Checked constant overflow is not detected when an operand is an int-based enum: `(E)int.MaxValue + 1` | CS0220 (2) | 3,684 (1st) |
| N2 | safety (P1) | Attribute names are not bound, and attribute arguments are not type-checked, on methods and local functions: `[Zz(8)]`, `[MethodImpl("text")]`, `[MethodImpl(65536)]`, `[MethodImpl(1+true)]` | CS1503 (15), CS0220 (4), CS0019 (3), CS0234 (1), CS0103 (1); CS0121 (1, other class) | 1,891 (1st) |
| N3 | safety (P1) | A `this ref` extension on a class or an unconstrained T | CS8337 (1) | 21,646 (2nd) |
| N4 | safety (P1) | Top-level statements with valued returns whose end is reachable | CS0161 (1) | 37,057 (3rd) |
| N5 | other (P2) | A parameterless struct constructor that is not public | CS8958 (1) | 43,325 (3rd) |

The thirds are findings 1–16,672, 16,673–33,345 and 33,346–50,018. New families per third:
* 1st: N1 and N2, both safety-relevant.
* 2nd: N3, safety-relevant. N2 also gained an accepted CS0019 shape.
* 3rd: N4 (safety-relevant) and N5 (other). N2 also gained accepted CS0103 and CS0121 shapes.

Defect keys first seen in the last third are CS0103, CS0121, CS0161 and CS8958. **The stopping rule is not met on
pre12, and by the inference above not on pre14 either.** Every third found a new safety-relevant family, and the
last third found two new families.

* **Hand triage.** 30 hand-written shapes were checked, on managed and native self, on pre12 (`replay/hand4`, `hand5`)
  and pre13 (`hand6`). Every one is a C#-invalid shape of N1–N5 that both selves accept and Stage-0 rejects. They add
  *accepted* N2 shapes under CS0246 (`[Zz(8)]`) and CS1729 (`[UnscopedRef(1)]`). The campaign also had first keys
  CS0246 and CS1729, but only on mutants that self rejected. `corpus/pending/` keeps one to three of these shapes per
  family and key pair: four for N1/N2 under CS0220, and a fifth CS0220 file for the AD0001 crash below.
* **asan mode, campaign-6.** `run-mutation.sh asan --per-id 3 --all-ids` selected 57 programs, and native `vib-stage2`
  built all 57 (`build --asan -O1`). 21 of them are under the nine defect keys: 12 mutants and 9 reduced repros. 19
  of those 21 ran and exited 0 with no sanitizer report. The other 2 are the CS0161 mutant and its repro, which open
  sockets and were not executed. Among the warning-only selections, one CS0652 program timed out: a loop that never
  ends, in .NET too.
* **asan mode, hand shapes.** Managed `vib-self.dll` built and ran the 30 hand shapes (9, 11 and 10 per directory).
  All exited 0 with no sanitizer report. Native `vib-stage2` also built and ran the four N4 fall-through shapes, which
  exited 0, and the valid N4 control, which exited 7.
* **Result.** No P0 evidence in the runs that were made. With these selections, N1–N4 stay P1 and N5 P2. A clean run
  does not prove safety.
* **N4 behaviour.** On the fall-through paths tested, self emits an implicit `return 0`. The program exits 0, and in
  the shape that holds a `Vec<int>`, LSan reports no leak.
* **Earlier families.** None of the S1–S6 or O1–O5 families reappeared.

These shapes await a separate fix in the self front end (self-fix-x); the oracle does not fix them. Their repros wait
in `eng/mutation/corpus/pending/`. That directory is not gated, and its files move into the corpus when the fix lands.

**Stage-0 and Analyzer crash (found by hand, not by the campaign).** On a non-int enum constant that does not fit in
Int32, `CfgMirLowerer.TryMakeConstantOperand` calls `Convert.ToInt32` and throws. Results on pre13 (`adrun.log`):
* Four C#-valid programs crash it: `enum L : long { A, Big = 1L << 40 }` with `L.Big`, directly and through a
  conditional; `unchecked((L)long.MaxValue + 1)`; and `enum U : uint { Hi = 0x80000000 }`. `vib check` stops with
  `vib error: Value was either too large or too small for an Int32.`, and a real `dotnet build` stops with AD0001.
* Self rejects all four with VIB000 (enums must use `int`), which is the expected answer.
* On the C#-invalid `(L)long.MaxValue + 1`, the tool's in-process Analyzer also throws. That repro is
  `pending/hand-c6-CS0220-enum-long-analyzer.cs`.
* No frontend accepts any of these programs. The crash is a defect and a parity break, not an invalid-C# accept. Its
  fix is assigned on the Roslyn side.

### 5.4 Earlier baselines on candidate r11-4

These sections record the r11-4 measurements as they were made. The shapes they found are rejected on candidate 6
(§5.1, §5.2).

#### Candidate r11-4 runs

These are measured baselines: how many C#-invalid programs the candidate's frontends accepted in three campaigns.
They are a lower bound, not a certification. No run reached the stability criterion (§5.4.2). All three runs used
candidate r11-4 (manifest digest `341faa44…`), managed `vib-self.dll` (sha256 `caad49fd…`) and Roslyn 5.12.0 on
`net11.0`, under a 16 GiB cgroup cap on a shared 24-core WSL2 machine. The runs were made under the earlier
MANDATORY/OUTSIDE-CONTRACT rules. Their `findings.jsonl` files were then re-rendered under the contract in §1
(`run-mutation.sh report`), so every number below uses the current rules.

| Run | Seed | Bases | SDK-invalid mutants kept | C#-invalid programs (first keys) | **Self accepts of C#-invalid programs (first keys with an accept; safety-relevant / other)** | Self crashes | Warning-only parity (self accepts / Stage-0 accepts / disagree) | Subset rejections | Stage-0 defects | Time | Peak memory |
|---|---|---:|---:|---:|---|---:|---|---:|---:|---:|---|
| campaign-3 | 20261119 | 383 | 15,000 | 14,690 (163) | **2,441 (43; 27 / 16)** | 46 | 330 (279 / 275 / 4) | 3,722 | 0 | 1,578 s | 6.6 GB cgroup, before the memory fix |
| campaign-4 | 20261120 | 639 | 36,000 | 35,208 (182) | **5,025 (55; 39 / 16)** | 66 | 812 (699 / 683 / 16) | 11,451 | 0 | 3,463 s | 2.9 GB cgroup |
| campaign-5, stopped at 34% | 20261121 | 789 | 17,072 | 16,749 (171) | **2,367 (49; 34 / 15)** | 39 | 343 (286 / 284 / 2) | 5,210 | 0 | ≈1,920 s | 1.4 GB tool |

Each run also kept 20 unmodified invalid external probes, which are counted in the table. Campaign-5 was stopped for
a machine restart. Its `findings.jsonl` is complete, but its `statistics.json` and repros stop at 16,980 mutants.

**Union over the three runs: 60 first keys under which self accepted, or crashed on, a C#-invalid program** (44
safety-relevant, 16 other). 59 of them have an accept. CS8174 only crashes. The 60 keys were r11-4's
`known-open-ids.txt`, and r11-4's `corpus/` held 85 reduced repros for them.
The rule-family inventory groups them into 17 families (handed to the self owner separately).

#### 5.4.1 What the defects are

The largest keys over the three runs (programs accepted or crashed on):
* CS0139 (`break`/`continue` outside a loop): 2,533 programs. CS0122 (inaccessible member): 349. CS0266 (implicit
  narrowing): 260.
* Declaration rules of the other class: CS0106 (forbidden modifier) 1,150, CS0107 (two protection modifiers) 769, CS1004
  (duplicate modifier) 762, CS9048 (`scoped` on a by-value parameter) 602, CS1737 (optional before required) 424,
  CS0101 (duplicate type) 343, CS0238 (`sealed` on a non-override) 330, CS0102 (duplicate member) 280, CS0441
  (`static sealed` class) 223.

Under handoff v1 the other-class keys were OUTSIDE-CONTRACT and not gated. They are now defects like any other. The
earlier figure of 47 gated keys (v1) is not comparable: it left out these 16 keys, and it gated 3 keys that the
current rules class as warning-only parity (CS8602, CS8625 and CS9200; see §5.4.3).

#### 5.4.2 Growth and stability

The set of keys with a self accept had **not** stopped growing in any run. Recomputed from `findings.jsonl` with the
current rules (mutants only, in seeded order). Each cell is *first keys with a self accept / of them safety-relevant*:

| Mutants kept | campaign-3 | campaign-4 | campaign-5 (stopped) |
|---:|---|---|---|
| 2,500 | 28 / 15 | 30 / 16 | 28 / 14 |
| 5,000 | 32 / 18 | 37 / 22 | 36 / 22 |
| 7,500 | 36 / 22 | 38 / 23 | 39 / 25 |
| 10,000 | 37 / 23 | 43 / 28 | 41 / 27 |
| 12,500 | 38 / 23 | 44 / 28 | 43 / 28 |
| 15,000 | 43 / 27 | 47 / 31 | 47 / 32 |
| 17,000 | — | 48 / 32 | 48 / 33 |
| 20,000 | — | 50 / 34 | — |
| 25,000 | — | 52 / 36 | — |
| 30,000 | — | 52 / 36 | — |
| 36,000 | — | 54 / 38 | — |

New defect keys in the last third of each run (from each `report.md`):
* campaign-3: CS0023, CS0117, CS0221, CS1107, CS1604, CS8657.
* campaign-4: CS0400, CS1604 (over the last 12,000 mutants).
* campaign-5, at its stop: CS0028, CS0201, CS0591, CS1107, CS1612, CS8171, CS8377.

Eight keys appear in only one run: CS0117, CS0161, CS0192, CS0400, CS1059, CS1612, CS8156 and CS8160. Eleven have
at most two programs across all three runs: CS0117, CS0161, CS0192, CS0221, CS0400, CS0432, CS0591, CS1059, CS1612,
CS8156 and CS8160. The tail is long, so the 60 keys are a lower bound. A clean CI run does not mean that self checks
every C# rule.

#### 5.4.3 Warning-only parity programs (reported, not gated)

A warning-only parity program's only CS errors are warnings that only the blanket `TreatWarningsAsErrors` promotes,
so it is valid C#. (Programs whose only errors are warnings that `Vibranium.App.props` promotes explicitly are
C#-invalid under §1. There were 127, 414 and 198 of them, mostly CS8509, CS9191, CS9192 and CS9193, and self and
Stage-0 rejected every one.) Self and Stage-0 agree on all but 22 warning-only parity programs. In each of those 22,
self accepts and Stage-0 rejects: CS8625 (a `null` default reaching a non-nullable reference through an optional
parameter) 3, 14 and 2; CS9200 1 (campaign-3); CS0162 1 and CS8602 1 (campaign-4).

All 22 contain a cut construct (C12 in 21 of them, C19 in the CS0162 one), and Stage-0 rejects each with a
Vibranium diagnostic (VIB008, or VIB000/VIB001 for CS0162), not a C# one. So each is invalid Vibranium, and self's
cut checks should reject them once the cuts land. Self accepting them now is a gap in Vibranium-rule parity with
Stage-0, which `docs/CONFORMANCE.md` owns, not an invalid-C# defect. The gate does not fail on them, and the
report lists every one.

#### 5.4.4 Subset rejections (reported, not counted as checks)

Self rejected 3,722, 11,451 and 5,210 C#-invalid programs that also contain a C10–C20 cut construct. Those
rejections may come from the cut, not from the C# error. They show nothing about self's C# checks, so they are
reported apart. A rejection without a cut construct does not prove a check either, since self may reject for an
unrelated reason. The oracle measures accepts; it never certifies a check.

#### 5.4.5 Stage-0, Analyzer and self failures

* **Stage-0:** no accept of a C#-invalid program in any run. Its accepts (275, 683 and 284) are warning-only parity
  programs.
* **Analyzer frontend:** no accepts. It is the SDK plus the analyzer, so it cannot accept such a program.
* **Self crashes** on C#-invalid input (r11-4). Each one was a defect, excused only through `known-open-ids.txt`;
  candidate 6 rejects all of these shapes:
  * CS0523 (27, 39 and 21 programs): a struct that contains itself, including through `Option<Node>`, led to
    unbounded allocation and `panic: out of memory` (exit 101).
  * CS8340 (17, 18 and 12): the same cycle inside a readonly struct.
  * CS8174 (2, 9 and 6): `ref int r;` gave `panic: index out of bounds`.
* **SDK cross-check:** `verify-sdk.py` passed for 44 of 44 campaign-4 repros. 38 match the real `dotnet build`
  exactly. In the other 6, the real build reports a subset that keeps the first ID, because csc stops after
  declaration errors. The shipped script applies this first-ID rule.

## 6. Native stage2 and native evidence

### 6.1 Native stage2 as a self oracle

The contract covers the native fixed-point compiler `vib-stage2` run on its own, with no Roslyn in front of it.
The oracle checks it directly: `--self PATH` with a native executable runs `PATH check FILE` under an address-space
limit through `prlimit` (`--self-native-mb`, default 4096). The SDK oracle still compiles against the managed
`Vibranium.Core.dll` and `Vibranium.Std.dll` (`--vibranium-references`, by default the managed `vib-self` build).
`settings.json` records the hash of the native executable and of those references.

On candidate 6 the gate passes on both selves with an empty known-open list. The verdicts and `VIB` IDs of the
two selves agree on all 504 gate programs and on all 8,619 replayed programs (§5.1).
Campaign-6 and campaign-6m agree on all 50,018 programs as well (§5.3).

On candidate r11-4, `eng/bootstrap/bootstrap.sh --skip-benchmark` reached its fixed point
(`managed.ll == stage1.ll == stage2.ll == repro.ll`, sha256 `c6fe6cc9…`). The CI gate was then run with the same
seed, budget and corpus against managed `vib-self.dll` and against that `vib-stage2` (sha256 `0163155d…`):

| Self | Corpus files | Seeded mutants | Bases | Self verdict on the 85 corpus files | Self on the 400 mutants | With `--known-open` | Without |
|---|---:|---:|---:|---|---|---|---|
| managed `vib-self.dll` | 85 | 400 | 53 | 82 accept, 3 crash (exit 101), 0 reject | 46 defects, 348 rejects (85 subset), 6 warning-only parity | GATE PASS, 90 s | exit 2, 88 s |
| native `vib-stage2` | 85 | 400 | 53 | 82 accept, 3 crash (exit 101), 0 reject | 46 defects, 348 rejects (85 subset), 6 warning-only parity | GATE PASS, 54 s | exit 2, 58 s |

With `--known-open`, all 60 listed keys reproduced on both. Without it, both runs failed with exit 2 because of the
crashes (CS0523 once, CS8174 twice), and both printed defects under 59 first keys. Both chose the same bases. On
all 485 programs, the native and managed verdicts match, and so do the `VIB` diagnostic IDs of every rejection.
Every one of the 60 known-open keys was therefore confirmed on the native compiler as well: `vib-stage2`, run on
its own, accepted or crashed on C#-invalid programs. These were the managed defects carried through the bootstrap,
not separate ones, and fixes in the self front end reached the native compiler the same way. Run the gate against
both; a divergence between them is a defect in itself.

### 6.2 Native evidence (`asan` mode)

`run-mutation.sh asan RESULTS --per-id 2` builds up to two self accepts per safety-relevant key with self's own
emitter (`build --asan -O1`, clang-18 with ASan/LSan) and runs each once, with no input and a 10 s limit. On
campaign-4 (r11-4, handoff v1, then under the MANDATORY keys) that was 82 programs over 41 keys:

* 69 built and exited 0 with no sanitizer report.
* 11 had no entry point, so they were not run.
* One CS0523 program (`struct Node { … Option<Node> … }`) was emitted as IR that clang rejects:
  `base element of getelementptr must be sized`, for `{ i1, %struct.Node }` inside `Node`. This is malformed
  code from C#-invalid input, so CS0523 is P0.
* One CS1525 program ran into the base program's own `panic: stop` (exit 101).

On candidate 6 (§5.3), asan mode built 57 selected campaign-6 programs with native `vib-stage2`. 21 of them are
under the nine defect keys; 19 of those ran and exited 0, and 2 open sockets and were skipped. Managed
`vib-self.dll` built 30 hand shapes, and all exited 0. There is no P0 evidence in the runs that were made.

ASan finds little because most of these defects are silent. A wrong conversion, a write through an `in` reference
or a readonly field, or an ignored access modifier produces wrong values in valid memory, not an out-of-bounds
access. The hand-written repros show the effects directly. Each was built by self and run under ASan:

| Key | Repro (corpus) | Observed |
|---|---|---|
| CS8151 | `hand-ref-long-as-int.cs`: `ref int` returned to a `long` field | prints 1234605615003729920 (0x1122334400000000): an `int` write through the reference zeroed half of the `long`, a type confusion in native memory |
| CS8121 | `hand-CS8121-long.cs`: `case Err<long>(var e)` on a `Result<int, int>` | the impossible pattern matches and prints 424242 |
| CS8331 | `CS8331.cs`: `static void F(in int x) { x = 2; }` | the caller's variable becomes 2 through an `in` parameter |
| CS0201 | `hand-owned-discard.cs`: an owned value evaluated as a statement | builds and runs (prints 3); the dropped temporary's ownership is undefined by the contract |
| CS0266 | `CS0266.cs`: `char c = 65` | prints 65 (the harmless shape; `long` to `int` is already rejected) |

On r11-4, self also crashed (exit 101) on some C#-invalid input; candidate 6 rejects these shapes:

* CS0523, and CS8340 with the same cycle: a struct with a field of its own type made self allocate without bound
  (about 1.3 GB within a second, then `panic: out of memory` under the 1 GiB heap cap). This is why every self
  child runs with a heap or address-space limit.
* CS8174, `ref int r;`: `panic: index out of bounds`.

## 7. Limits

* **Baselines, not certification.** §5 counts what the campaigns found. The mutants are bounded, deterministic single
  edits of sampled real programs. A C# rule with no self accept may still have accepted shapes that no operator
  produces, and a rule that is never reached is not covered at all. A clean gate does not certify that self rejects
  all invalid C#.
* **Not converged.** No run has reached the stopping rule, which asks for no new defect key, and no new family, in
  the last third of a run. In campaigns 3–5 (r11-4) new keys appeared in every last third; campaign-5 was stopped
  at 34% for a machine restart. Campaign-6, on the fixed tree (pre12), found a new safety-relevant family in each
  third of its findings, and two new families (N4, N5) in its last third (§5.3).
  The keys found are what was found, not a bound.
* **The first key is triage.** A bucket can mix several self defects. Every program counts once, under its first SDK
  error, although any genuine or explicitly promoted error makes an accept a defect. Repros are reduced with a
  budget and are not globally minimal. The reducer keeps the first key, the error kind, self's verdict and the entry
  point, and never adds a cut form. Roslyn's in-process error order is not fully deterministic. In
  campaign-6 and campaign-6m (the same 50,018 programs), 59 programs with several errors got a different first
  key, although they had the same errors and the same verdicts. Repro reduction can then drift to another first
  key, which the run counts as an infrastructure failure (3 and 2 there; exit 2). A deterministic order (sorted
  by location, say) is not implemented yet.
* **Severity classes are a judgement.** Safety-relevant means acceptance *could* break memory or type safety,
  ownership or a Std/Core invariant. The other class sets priority (P2); it waives nothing.
* **Subset rejections hide checks.** While a C10–C20 cut is still in the tree, a self rejection of a program that
  uses the cut says nothing about the C# rule. When a cut is lifted, its subset rejections may turn into accepts.
  Re-run the campaigns after each cut lands.
* **Warning-only parity is not gated.** Only the 13 warnings that `Vibranium.App.props` promotes are errors.
  Other warnings are not language errors (Stage-0's policy), so programs whose only errors are blanket-promoted
  warnings are reported as warning-only parity and never gated. Self accepting one that Stage-0 rejects (CS8625 and
  others, §5.4.3) is a Vibranium-rule parity gap for `docs/CONFORMANCE.md`.
* **Known-open caveats.** `known-open-ids.txt` excuses a whole key: every self accept and every self crash under it,
  including shapes not yet seen. Until a key leaves the file, CI cannot tell a partial fix from no fix. When a key is
  removed, its corpus files and the 400 seeded mutants keep those shapes rejected; they do not keep every shape of
  the key fixed. A different shape of the same key is caught only if a mutant happens to produce it. The gate prints
  every listed key that no longer reproduces. Files in `corpus/pending/` are not gated at all until they are moved
  up into the corpus.
* **Oracle scope.** The SDK oracle is Roslyn in process. `verify-sdk.py` cross-checks repros against a real
  `dotnet build` with `Vibranium.App.props`. Multi-file projects, emit-only diagnostics, IDE code-style rules, other
  analyzers and other platforms are not covered. Campaigns and the gate ask every frontend only to `check`; only
  `asan` mode builds and runs.
* **Dependence on Stage-0.** The CS0649 triage split calls Stage-0's internal `IsZeroSafeTypeSymbol` by reflection.
  If that method is renamed, the tool stops at startup, and the self-test covers it.
* **Analyzer frontend.** It is the SDK plus the analyzer, so it cannot accept an SDK-invalid program. The check only
  guards the pipeline wiring. Before this update the tool parsed the trusted bootstrap source under a synthetic
  `obj/__VibraniumAppBootstrap.cs` path. MSBuild passes the physical path; `App.props`' `Link` is display metadata
  only. The analyzer trusts that runtime source only by its path suffix and digest. So the in-process Analyzer
  reported VIB000 (`VibraniumRuntime.Initialize`) in the bootstrap tree, which is part of every compilation, and it
  rejected SDK-valid programs that a real build accepts. The tool
  now parses it under its physical path, and the self-test checks that a valid app is accepted. Invalid-C# verdicts
  never depended on this, since the SDK errors already make them rejects.
* **Machine.** Throughput, about 10 mutants/s with 6 workers, was measured under the shared `heavy` slot limit and
  a 16 GiB cgroup cap on a loaded 24-core WSL2 machine. Peak memory was 2.9 GB for the cgroup in campaign-4. That
  was after per-chunk finalizer draining and the fixed glibc mmap threshold. Before them it was 6.6 GB with 4
  workers, and still growing. Campaign-6 (native self) ran 50,000 mutants in 5,054 s, with a peak of 3.8 GB for
  the cgroup.

## 8. Files and wiring

| Path | Role |
|---|---|
| `eng/mutation/Vibranium.Mutation.csproj` | The `vib-mutation` tool: C# 15, `net11.0`, `TreatWarningsAsErrors`. It references `src/vib` (Stage-0 and the Roslyn frontend) and `src/Vibranium.Analyzer`. |
| `eng/mutation/*.cs` | `Program` (modes and gate), `Campaign` (bases, chunks, growth, memory), `Mutations` (operators), `Oracles` (SDK, self managed or native, Stage-0, Analyzer), `Classification` (severity tables, error kind, keys, cut forms), `Reducer`, `Evidence` (asan mode), `Reports`, `Corpus`, `Models`, `Options`, `SelfTests` |
| `eng/mutation/run-mutation.sh` | Entry point. It builds the tool and `vib-self` (Release), runs under `heavy` on a shared machine and runs directly when `CI=true`. Campaign, ci and check only run `check`; `asan` mode builds and runs selected self accepts and needs `clang-18`. |
| `eng/mutation/severity-safety-ids.txt`, `severity-other-ids.txt` | Severity classes (§1): safety-relevant (P1, P0 with asan evidence) and other (P2), one ID per line with the likely self component and a reason. Priority only: they waive nothing. |
| `eng/mutation/known-open-ids.txt` | First keys under which self still accepts or crashes on a C#-invalid program, assigned to the self fixes (empty on candidate 6) |
| `eng/mutation/corpus/` | Regression corpus: reduced repros of recorded defect shapes whose fix has landed (plus hand-written shapes), run by `ci` (top level only) |
| `eng/mutation/corpus/pending/` | Repros of assigned but unfixed shapes, with `PENDING.tsv` (family, first key, fix); not gated, moved up into the corpus when their fix lands |
| `eng/mutation/verify-sdk.py` | Cross-checks repros against a real `dotnet build` with `Vibranium.App.props` |

Outputs per run: `report.md` (summary, growth, defects by first key, warning-only parity, subset rejections, Stage-0 and
Analyzer accepts, operator families), `defect-table.md`, `findings.jsonl`, `statistics.json` and `settings.json`.

The solution entry (`eng/mutation/Vibranium.Mutation.csproj` in `Vibranium.slnx`) builds the tool with the rest of
the tree. CI runs, after the conformance fuzzer:

```yaml
      - name: Mutation diagnostic regression gate (empty known-open list, not release sign-off)
        env:
          MUTATION_SKIP_BUILD: "1"
        run: ./eng/mutation/run-mutation.sh ci
```

To gate the native compiler as well, run the same step with `--self` pointing at `vib-stage2` from the bootstrap
job's output directory.
