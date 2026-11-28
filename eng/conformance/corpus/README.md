# Conformance corpus (evidence)

These are minimized Vibranium programs and the exact outputs of every oracle. They are not compiled by the
tool (`Vibranium.Conformance.csproj` removes `corpus/**`). Scratch paths are replaced by `<scratch>`/`<tmp>`.

* `probes/` holds the standalone vetting probes. Each probe has its outputs on the frozen Round-10 tree
  (`*.frozen.txt`), on the integration candidate `candidate-freeze-2` (`*.candidate.txt`), on Round-11
  candidate 1 (`*.r11c1.txt`), on Round-11 candidate 2 (`*.r11c2.txt`, for the N13, N14 and `k*` probes) and on
  Round-11 candidate 3 (`*.r11c3.txt`, for the `n15_*` probes), as far as the probe existed when the tree was current.
  * N1 `ref` → `ref readonly`/`in`: `p1`–`p3`, `p6`, `m1`. N2 constant-true guards: `p4`, `p5`, `p7`, `cg1`, `cg2`.
  * N3 compound assignment order: `q1`–`q4` and `r1`–`r5` (with controls). N4 `(nint)-2`: `n1`, `n2` (control).
  * N5 unchecked `_` arm after complete union arms: `s1`–`s3`, `t1`–`t4`, `u1`, `u2`.
  * N6 never-matching pattern: `pn1`, `pn2` (control). N7 `switch` precedence: `pr1`–`pr4`.
  * N8 `foreach` over `Span<T>`: `sp0`–`sp6` (`sp4` is the `ReadOnlySpan<T>` control, `sp5` the write-in-body control).
  * N9 constant pattern in definite assignment: `da1`, `da2` (control). N10 non-constant `case` label: `cl1`, `cl2`, `cl3` (control).
  * Collection returned as `ReadOnlySpan<T>`, banned by design (`banned.collection-return-span`, CONFORMANCE.md §7.3): `r6`, `r7`.
  * Found on freeze-2 by the extended campaign, fixed in Round-11 candidate 1 (outputs on both trees):
    * N11, unreachable `default:` end point: `x1_*`, `x2_*` (controls `x2_ushort_default_break_control`,
      `x2_int_discard_case_fallout`, `x1_*_return`, `x1_ushort_ge0_da`).
    * N12, `ref` argument naming a readonly variable: `y1_*` (controls `y1_control_*`).
    * N8 with a `Vec`-backed span: `y2_*`.
    * Cut shapes in dead code (CONFORMANCE.md §7.10): `c2u_*`.
  * Cut C4, patterns on a value of open type-parameter type: `c4_01`–`c4_08` (cut rows) and `c4_09`–`c4_11`
    (controls). These are the matrix sources, with Round-11 candidate 1 outputs (`*.r11c1.txt`).
  * Found on Round-11 candidate 1, with outputs on both trees:
    * N13, flow checks in an unreachable `default:`: `z1`–`z3`, `z9` (Stage-0 checks, self does not), `z4`–`z6`,
      `z8` (no frontend checks), `z7` (control).
    * N14, union-pattern variable in a multi-label section with a `when false` label: `q0_bucket_min`,
      `q1_multilabel_*`, `q7`–`q9` (controls `q2_multilabel_*`–`q6`). The N3 probes `q1_compound_out`–`q4_compound_field_out`
      only share the prefix.
  * N15 (decided, SPEC aligned to the implementations, CONFORMANCE.md §7.14), an instance call on `this` in a
    constructor, with Round-11 candidate 3 outputs (`*.r11c3.txt`): `n15_zt1`, `n15_zt2`, `n15_zt6` (every field
    assigned first), `n15_zt3` (only a `Vec` field), `n15_zt4` (only an `int` field), `n15_zt5` (only the zero-safe
    field unassigned) and `n15_zt7` (called before init). All seven are VIB008 in every frontend, plus the O11-11
    `__this_N` VIB001 in Stage-0 and the Analyzer.
  * Native call-stack exhaustion on Round-11 candidate 4 (CONFORMANCE.md §6.12): `r11c4_stack_b1` (the minimized
    default-campaign bucket, seed 7171, index 15120) with its run and the stack probe (`*.r11c4.txt`): self `-O0`
    overflows an 8 MB stack at recursion depth 131,070, and every run agrees with 16 MB or an unlimited stack. The
    release-6 engine re-runs such a leg with an unlimited stack and excuses it.
  * Stage-0/self parity after a borrow-rejected move on the combined cut source (CONFORMANCE.md §6.14), with outputs
    on Round-11 candidate 4 (`*.r11c4.txt`, every frontend agrees) and on the combined cut source (`*.r12cut.txt`):
    `r12f3_loop_move_borrowed` and `r12f3_loop_move_borrowed_reborrow` (a move into a consuming call in a loop while
    a view is live: Stage-0 and the Analyzer now report only VIB003, self still adds VIB001),
    `r12f3_field_move_borrowed` (a field moved out of a class while a view of it is live: Stage-0 and the Analyzer
    report only VIB003, self adds VIB005) and the controls `r12f3_field_move_noborrow_control` and
    `r12f3_struct_field_move_noborrow_control` (no live view: VIB005 everywhere on both trees). On candidate 6
    (`*.c6.txt`, which carries the F3-clarified fix) every frontend agrees: {VIB003, VIB005} for the field move and
    {VIB003} for the loop moves.
  * Cut C21 (`: base(...)`, decided 2026-11-18, CONFORMANCE.md §7.15), the matrix sources with their outputs on the
    combined cut source before C21 (`*.r12cut.txt`, frontend checks only): `c21_01` (`: base()` on a sealed class),
    `c21_02` (with arguments, which needs inheritance), `c21_03` (two classes in one file: one VIB000, at the first),
    `c21_04` (C9 and C21 in one file) and the control `c21_05` (constructors without an initializer). Before the cut
    the Analyzer and Stage-0 accept `c21_01` and `c21_03`, and self rejects them with its older message (one per
    constructor); no frontend reports the C21 message in `c21_02` or `c21_04`. On candidate 6 (`*.c6.txt`, `run
    --exec`) every frontend reports the C21 message exactly once, at the first `base`, and the control is accepted
    with equal output.
  * Static IR lowering contract (release-7, CONFORMANCE.md §1): `ir_01_shift_div` and
    `ir_02_signed_overflow_shift_forms`, valid programs with variable and constant shifts (`int`, `long`, `uint`,
    `ulong`, `byte`) and signed/unsigned division and remainder (by a variable and by `-1`). On candidate 6
    (`*.c6.txt`) both compilers' IR passes the contract and every run equals the managed run.
  * N16 (found by release-7, decided {VIB003, VIB007}, CONFORMANCE.md §6.17): a collection expression assigned to a
    by-value `ReadOnlySpan<int>` parameter that is then returned. `s3p_01` (unconditional), `s3p_04` (on one
    branch), `s3p_05` (returned as a slice) and `s3p_09` (`[]`) give {VIB007} in the Analyzer and Stage-0 and
    {VIB003, VIB007} in self on candidate 6 (`*.c6.txt`), on the combined cut source (`*.r12cut.txt`) and on Round-11
    candidate 4 (`*.r11c4.txt`). Controls and related shapes (`*.c6.txt` only): `s3p_06` (the parameter re-sliced
    and returned, accepted with equal output), `s3p_10` (a local destination, {VIB003, VIB007} everywhere),
    `s3p_02` (a `Span<int>` parameter, CS9203) and `s3p_03`, `s3p_07`, `s3p_08` (`ref`/`out` view parameters,
    VIB000/VIB004 everywhere). The same shape makes seeded bug S3 observable (§6.16). The assignment-value forms
    `s3p_11` (`return p = [1, 2];`), `s3p_12` (`=> p = [1, 2];`), `s3p_13` (`q = p = [1, 2]; return q;` with two
    parameters), `s3p_14` (`return (p = [1, 2, 3])[1..];`) and `s3p_15` (`ReadOnlySpan<int> q = p = [1, 2]; return
    q;`) give the same split before the fix (`*.c6.txt`, candidate 6 pre4). With the Roslyn-side fix (candidate 6
    pre5, `*.c6p5.txt`) every `s3p` probe gets the same IDs from all three frontends, {VIB003, VIB007} for
    `s3p_01`, `s3p_04`, `s3p_05`, `s3p_09`, `s3p_10` and `s3p_11` to `s3p_15`, and the controls are unchanged.
  * Assignment-value cascade (release-7.1, roots N17 and N18, CONFORMANCE.md §6.19 and §7 item 19):
    `probes/cascade/` holds /roslyn-n16's 83 probes (`C*`, `AV*`, `v4r*` and the adversarial `A*`) and seven of
    this tool's (`av_*`), each with its candidate 6 pre8 output (`*.c6p8.txt`, `run --exec`, before both fixes). A
    plain move of a `Span<int>`/`OptionMut<T>` then a use of the source gives {VIB001, VIB003} in the Analyzer and
    Stage-0 and {VIB001} in self (`C01`, `C09`, `C16`, `C28`, `av_01`, `av_04`); a view reassigned in a loop while
    live is rejected with VIB003 by the Analyzer and Stage-0 and accepted by self (`C17`, `C18`, `C20`–`C22`,
    `A30`, `av_10`; straight-line in `av_11`); a use after an assignment value gives {VIB001, VIB003} or {VIB001} in the Analyzer and
    Stage-0 and is accepted by self for some forms (`AV02b`, `AV08`, `v4r01`, `v4r02`, `v4r05`). Apart from these
    splits (also `A01`, `A02`, `A04`, `A07`, `A30`, `A36`, `C12`) and `A29` (ID sets that differ under a banned
    manual `Dispose()`), the adversarial `A*` probes, the real conflicts `C04`, `C06`, `C13`, `C29` and the controls
    `C05`, `C07`, `C15`, `C19`, `AV02d`, `AV03`, `av_02` get the same result from all three; `A26` gets VIB003 from
    all three where the decided result is VIB001. `av_05` shows why the generator starts a loop-reassigned view
    from another owner: `Span<int> t = [];` gives the view a local safe-context, and reassigning it a heap view is
    CS8352. The rebind probes `x1`–`x6` (a live view rebound to another owner, then the old owner mutated) are the
    shapes that make seeded bug XV1 visible: on pre8 (`*.c6p8.txt`) `x1`, `x2`, `x4` and `x5` are accepted by all
    three frontends, `x3` (the loop form) is the N17 split (Stage-0 VIB003, self accepts) and `x6` (a derived view
    of the old value still live) is VIB003 everywhere; with XV1 planted (`*.xv1.txt`) self rejects `x1`, `x2`,
    `x4`, `x5` with VIB003 while Stage-0 accepts. The `*.pre13.txt` outputs are from candidate 6 pre13 (both fixes in): all
    96 probes get the same result from all three, `A26` is now the decided {VIB001} everywhere, and `A29` keeps its
    ID-set difference under the banned manual `Dispose()` (no disagreement: the program is rejected everywhere).
  * Switches over unions (release-7.1, roots N19 and N20, CONFORMANCE.md §6.20 and §7 item 20):
    `probes/union-switch/` holds 49 probes `us01`–`us51` (no `us30`, `us36`), each a helper `Us(int p)` called on
    the None/Err path, a payload path whose guards fail and one whose guards hold, with its candidate 6 pre8 output
    (`*.c6p8.txt`, `run --exec`, before self-fix-v). N19: self drops every switch-statement label that is not a union
    case, so its native run prints another value (`us01`, `us02`, `us04`, `us05`, `us08`, `us15`, `us20`, `us24`,
    `us29`, `us31`, `us32`, `us42`), panics with "index out of bounds" (exit 101) where an exhaustive switch reaches
    its `unreachable` block (`us03`, `us09`), crashes (SIGSEGV, `us33`, a class payload) or loops (`us21`,
    run-timeout); self rejects a guarded or final
    `var w` (`us11`, `us13`, `us14`, `us41`) and whole-union arms of switch expressions (`us16`, `us18`, `us23`,
    `us34`, `us37`) that Stage-0 accepts. Agreeing controls: `us12`, `us17`, `us25`, `us35`, `us38`, `us39`,
    `us50` and the linear `us43`, `us45`, `us47`, `us48`, `us49` (one arm per case, no guard); `us40` (a case type
    with a designation on an owner place) is VIB005 everywhere. N20 (open): a linear payload matched from a call by
    a switch with any `when` guard, a case tested twice or a `not` label (`us06`, `us07`, `us10`, `us22`, `us26`,
    `us27`, `us28`, `us44`, `us46`, `us51`) is {VIB001, VIB006} in the Analyzer and Stage-0 and accepted by self;
    `us19` is {VIB001, VIB006} everywhere. The `*.pre9.txt` outputs are from candidate 6 pre9 (self-fix-v in): every
    non-linear probe agrees, and of the N20 group self now gives {VIB006} for the `not` forms (`us06`, `us07`, `us10`,
    `us19`, `us22`) and still accepts the others. The residual of N19 on pre9 has its own probes `r01`–`r12`
    (`*.pre9.txt`): self reports VIB001 on a later use of an owned `!Copy` place after a switch statement whose labels
    are all whole-union labels plus `default:` (`r01`, `r03`–`r05`, `r07`, `r08`), and agrees without the later use
    (`r02`), with a `Copy` payload (`r06`), in the expression form (`r09`), with `case NoneType:` instead of `default:`
    (`r10`), with a variant arm (`r11`) or with `default:` alone (`r12`). On candidate 6 pre13 (`*.pre13.txt`, self-fix-w and roslyn-linear-switch
    in) all 61 probes get the same result from all three and every accepted probe prints the managed output natively:
    the residual probes are accepted everywhere, and the N20 group is exactly {VIB006} everywhere.
  * Switches over a linear union (release-7.1, decided root N20, CONFORMANCE.md §5.2 and §7 item 20):
    `probes/linear-switch/` holds 59 probes over `Option<R>`/`Result<R, int>` with `R : IDisposable`, named
    `n20-{b,c,q}-{opt,res}-{rv,mv,pl}-{stmt,expr}-<shape>` (b: shapes the decision names, c: controls, q: a type pattern
    over one variant; rv: rvalue, mv: `Move(o)`, pl: place scrutinee). `EXPECT.tsv` gives each probe's expectation
    (exactly VIB006, accept, or `equal`: the same IDs from all three for place scrutinees), `*.pre9.txt` the candidate 6
    pre9 output (`run --exec`) and `VERDICTS.tsv` the comparison: 41 as expected, 18 waiting on N20 (11 with Analyzer
    and Stage-0 {VIB001, VIB006} and self accepting the guarded or twice-tested forms, 7 with self {VIB006} for `not`).
    On candidate 6 pre13 (`*.pre13.txt`, `VERDICTS.pre13.tsv`; roslyn-linear-switch and self-fix in) all 59 are as
    expected: 20 exactly {VIB006}, 18 accepted with equal native output, 21 with equal ID sets on a place scrutinee.
  * Slow native leg (release-7, CONFORMANCE.md §1): `to_03_slow_loop`, a valid 8-billion-iteration `ulong` loop that
    the managed run finishes in about 9 s and an `-O0` binary needs about 25 s for. Both native legs are reported as
    `SLOW: ...` (over the 20 s budget, equal to the managed run with 200 s) and there is no disagreement
    (`*.c6.txt`, measured under machine load).
  * Cut shape in a section of a `switch` on a constant that no label matches (CONFORMANCE.md §7.10), found as a
    tagger artefact on Round-11 candidate 2 (§6.10), with Round-11 candidate 2 outputs: `k0` (the minimized bucket),
    `k1`, `k3`, `k5`, `k6` (unreachable, accepted everywhere) and the controls `k2`, `k4`, `k7` (reachable, VIB000
    everywhere).
* `frozen-calibration-2026/` is the recall run on the frozen tree (seed 2026, 1000 programs). It holds
  `summary.json`, `curve.csv`, `report.md`, every bucket attributed to a new root, and the first bucket of
  each known root.
* `candidate-calibration-2026/` and `candidate-campaign-4242/` are the like-for-like run and the 45-minute
  campaign on the integration candidate, with every bucket. `candidate-ci-seed1/` is the `ci` mode run (seed 1,
  300 programs, no buckets). `info.json` keeps the category recorded during the run; `report.md` shows the
  buckets re-attributed with the final `known-roots.json` (`current (run: recorded)` where they differ).
* Buckets 041, 044, 048 and 049 of the campaign are tool artefacts (non-terminating generated programs). The
  current tool skips such programs instead of reporting them.
* `candidate-extended-9090/` is the 90-minute extended-profile campaign on freeze-2 (CONFORMANCE.md §6.8), with
  every bucket. Buckets 073/075 and 091/094 are unbounded-recursion artefacts, and bucket 054 is a cut tag in dead
  code. The current tool no longer reports either.
* `r11c1-extended-9090/` and `r11c1-default-4545/` are the campaigns on Round-11 candidate 1 (§6.9), with every bucket.
  The extended one uses the same seed as `candidate-extended-9090/`, so each index is the same program on both trees.
  `r11c1-ci-seed1/` is the `ci` gate run with `--fail-on open`. Their `info.json` files record the categories of the
  run (`new`, `regression:O6`, `regression:O2b`); `report.md` attributes the buckets to N13 and N14.
* `r11c2-extended-7373/` and `r11c2-default-6161/` are the cycle-3 campaigns on Round-11 candidate 2 (CONFORMANCE.md
  §6.10), run with the release-3 tool. The extended run has no bucket. The default run's one bucket (001) is the
  constant-selector tagger artefact, which the current tool no longer reports (probes `k*`). `r11c2-ci-seed1/` is the
  `ci` gate run with the current tool.

* `r11c3-activation-8181/` is the activation-profile recall run on Round-11 candidate 3 (CONFORMANCE.md §6.11):
  `summary.json`, `curve.csv`, `report.md` (re-attributed with the release-5 `known-roots.json`, where the O11 roots
  are `fixed`, so categories read `regression:O11-…` with the run's `known:O11-…` next to them), the first bucket of
  each O11 root and the vetted examples: 110 (O11-1 self side), 052 (O11-1 moved field projection, `spec-wrong-id`),
  012 (O11-13 through the `Program.g.cs` file name), 566 (O11-1 ASan heap-use-after-free in the Stage-0 `-O3` build)
  and 057 (N15 through the O11-11 VIB001). The run used the build before the N15 decision: replaying index 31
  (bucket 010) now prints another constructor variant; the other buckets replay identically.
* `r11c3-ci-activation-seed1/` is the activation `ci` gate (seed 1 × 300, `--fail-on open`) on Round-11 candidate 3:
  90 buckets of open O11 roots (exit 1), with bucket 023, the `new W(in x, x.Unwrap())` form of O11-10. Its one
  non-terminating program (index 242, `CaCall>Loop`) is a generator artefact: the `Loop` anchor's counter stays
  negative for `acc = 29`, so the C# program never ends (CONFORMANCE.md §8).

* `r11c4-soundness-smoke-seed31/` is the first soundness-profile run (release-6 tool, seed 31 × 200, `ci` mode) on
  Round-11 candidate 4, which does not implement C10–C20: 51 programs accepted by every frontend, each run at `-O0`
  and `-O3` under ASan/LSan for both native compilers, all clean and equal to the managed run. Its one bucket is the
  expected `cut-anchor` of C18 on a deliberate C1 shape (a local function directly in a switch section reports C1 and
  C18; candidate 4 reports only C1).
* `r12cut-ci-default-seed1/`, `r12cut-ci-activation-seed1/` and `r12cut-ci-soundness-seed1/` are the release-6 `ci`
  gates (seed 1 × 300, `--fail-on open`) on the combined cut source (Round-11 candidate 4 with the C10–C20 cuts,
  frozen manifest `9ba44361…`; CONFORMANCE.md §6.14). Default and activation have no bucket. The soundness run's 70
  programs accepted by every frontend are all sanitizer-clean and equal to the managed run; its 7 buckets are the
  Stage-0/self ID-set difference after a borrow-rejected move (probes `r12f3_*`), with every bucket, attributed to
  the root `R12-F3` (`new:R12-F3`; decided as F3-clarified, CONFORMANCE.md §7.17).

* `c6-ci-default-seed1/`, `c6-ci-activation-seed1/` and `c6-ci-soundness-seed1/` are the release-7 `ci` gates
  (seed 1 × 300, `--fail-on open`, with execution) on candidate 6 (pre1; CONFORMANCE.md §6.17). Default and
  soundness have no bucket (the soundness run's 70 programs accepted by every frontend are sanitizer-clean at `-O0`
  and `-O3` and equal to the managed run; R12-F3 is gone). The activation run, with the release-7 production for the
  N16 shape, has 5 buckets, all `new:N16` (3 `id-mismatch`, 2 `spec-wrong-id`).
* `c6p5-ci-default-seed1/`, `c6p5-ci-activation-seed1/` and `c6p5-ci-soundness-seed1/` are the same three gates on
  candidate 6 pre5, with the Roslyn-side N16 fix and N16 set to `fixed`: no bucket in any of them (the activation
  run has the N16 shape and no `regression:N16`).
* `c6p8-ci-default-seed1/`, `c6p8-ci-activation-seed1/` and `c6p8-ci-soundness-seed1/` are the release-7.1 `ci`
  gates on candidate 6 pre8 (before roslyn-assign-value-cascade and self-fix-v; CONFORMANCE.md §6.19, §6.20). Every
  bucket is attributed: default 8 `new:N19`; activation 10 `new:N17`, 1 `new:N18` and 7 `new:N19`; soundness 11
  `new:N19` (output mismatches, two self loops kept unminimized as `run-timeout`, one ASan report). The release-7.1
  streams differ from release-7's in every profile (`UnionSwitch`). Their `report.md` was rendered with the statuses
  of that run; re-rendered with the final `known-roots.json`, the N17 and N18 buckets show as `regression:` and the
  N19 buckets as `known:N19`.
* `c6p9-ci-default-seed1/`, `c6p9-ci-activation-seed1/` and `c6p9-ci-soundness-seed1/` are the same gates on
  candidate 6 pre9 (roslyn-assign-value-cascade, self-fix-s and self-fix-v in; CONFORMANCE.md §6.21): default 1 and
  activation 2 `known:N19` buckets, all the residual (self VIB001 on a later use of the place after
  `case not NoneType: .. default: ..`); soundness has no bucket (80 programs run at `-O0` and `-O3` under ASan/LSan);
  no N17 or N18 bucket.
* `c6p13-ci-default-seed1/`, `c6p13-ci-activation-seed1/` and `c6p13-ci-soundness-seed1/` are the same gates on
  candidate 6 pre13 (self-fix-w, roslyn-linear-switch and self-fix-o in; release-7.2, CONFORMANCE.md §6.22): no
  bucket in any of them (65, 40 and 80 programs run natively; the soundness ones at `-O0` and `-O3` under ASan/LSan).
* `c6-default-7700/`, `c6-activation-7701/` and `c6-soundness-7702/` are the release-7 root-finding campaign on
  candidate 6 (pre1, before the N16 fix; CONFORMANCE.md §6.18): 40,000, 35,000 and 25,000 programs with execution, 6
  workers. Default has 2 `run-timeout` buckets from one program (a loop until an `int` wraps that needs about 20 s at
  `-O0`; the slow-leg rule of release-7 excuses it), activation has 30 buckets, all N16, and soundness has none. Their
  `report.md` was rendered with the campaign-time statuses (N16 `new`); re-rendered now, the N16 buckets show as
  `regression:N16`.

The release-6 generator no longer produces the C10–C20 forms, which changed the program streams of every profile:
a (seed, index) pair of an earlier run regenerates only with the tool release that made the run (the release-5 tree
for the runs above, except `r11c4-soundness-smoke-seed31/` and the `r12cut-*` runs). Release-7 adds the N16 shape
to the activation profile, so activation (seed, index) pairs before release-7 regenerate only with the release-6 tool;
the default, extended and soundness streams are unchanged.

To re-run a bucket: `eng/conformance/run-conformance.sh run eng/conformance/corpus/<run>/buckets/<id>/min.cs --exec`
(for activation buckets add `--tags` with the `minTags` of `info.json`, so the expectation oracle runs).
To regenerate the original program: `eng/conformance/run-conformance.sh replay --seed <seed> --index <index> --exec`, with the seed and index from `info.json` (add `--profile <profile>` from `info.json` for extended and activation runs).
