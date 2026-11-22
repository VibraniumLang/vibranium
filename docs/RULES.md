# Vibranium v0.2 Rule Ledger

This ledger gives every normative statement of [`SPEC.md`](SPEC.md) §0–§5 a stable rule ID and names the tests that witness it. `tests/Vibranium.Tests/RuleLedgerTests.cs` parses this file in CI. It fails when a testable rule lacks a witness for a frontend, when a named test does not exist, when a quote no longer occurs in SPEC, or when SPEC gains a normative line that no rule quotes.

## How to read and maintain this ledger

**Rule IDs.** A rule heading is `### <ID> — <title>`. An ID is `R<section>-<slug>`, for example `R3.3-return-provenance`, and matches `^R\d+(\.\d+)?-[a-z0-9]+(-[a-z0-9]+)*$`. IDs are stable: never renumber or reuse one. When SPEC rewords a statement, update the rule's quote and keep its ID. When SPEC deletes a statement, delete its rule. Rules for the approved cuts C10–C20 have IDs of the form `R1-cut-c1N-<slug>`.

**Fields.** Each rule is a list of `- Field: value` lines. Any other field name fails the check.

| Field | Meaning |
|---|---|
| `Quote (§x)` | A verbatim fragment of one SPEC line, after removing backticks and `*` and collapsing whitespace. `…` skips text within the same line. A rule may quote several lines. |
| `Kind` | `frontend` (the default), `runtime`, `emitter`, `repo`, `prose` or `pending`. |
| `Expect` | The diagnostic each frontend reports on the rule's reject witnesses. |
| `Reject shape`, `Accept shape` | The shape of the near-miss programs. These are documentation only. |
| ``Reject: `Test` [frontends]`` | A test that runs the named frontends on a program violating the rule and asserts the expected diagnostic. |
| ``Accept: `Test` [frontends]`` | A test that runs the named frontends on a near-miss program, asserts that they accept it, and compares the managed .NET 11 run with the native binary built from that frontend's IR at `-O0` and at `-O3` with ASan/LSan: equal stdout, stderr and exit code. For the Analyzer, the native run comes from a native compiler. |
| `N/A reject [frontends]: reason`, `N/A accept [frontends]: reason` | Excuses a missing witness. The reason is mandatory. |
| ``Evidence: `Test` `` | For `repo` rules: a test that checks the repository property. |
| `Reason` | For `prose` rules: why the statement has no mechanical verdict. |
| `Source` | The cut a rule comes from (`cut C10` … `cut C20`). Required for `pending` rules. |
| `Note`, `See also` | Free text. |

Tests are named by fully qualified method name (`Vibranium.Tests.<Class>.<Method>`). The check resolves each name by reflection to a public `[Fact]` or `[Theory]` method. A `[Theory]` counts as one witness for every rule its rows exercise: the cut rules R1-cut-c10 … c20 share `Round12CutMatrixTests`' theories, whose rows cover every cut.

**Coverage versus measured gaps.** A rule is complete when every required side and frontend has a witness, or an N/A line whose reason is true. Whether a witness passes on a given candidate is measured by running it. A failing witness is a compiler gap; it is tracked outside this file and never excused with an N/A line. A `Note` marked BLOCKED names a clause whose witness waits for a decision that SPEC or the build settings still need; that clause has no witness and is not counted as covered by any N/A line.

**Frontends.**
- `analyzer`: the SDK path. This is Roslyn with the `Vibranium.App.props` options, meaning C# 15, nullable enabled and the promoted warnings as errors, plus `Vibranium.Analyzer`'s `VibraniumDiagnosticAnalyzer`. For a rule whose verdict is a C# error, a CS error in the compilation counts as the Analyzer's rejection.
- `stage0`: the Stage-0 `vib` compiler.
- `self`: the self-hosted `vib-self` compiler.

**Coverage required per kind.**

| Kind | Required witnesses |
|---|---|
| `frontend` | A reject and an accept witness for each of `analyzer`, `stage0` and `self`, or an `N/A` line with a reason. |
| `runtime` | An accept witness for each of the three frontends. Reject witnesses are optional. |
| `emitter` | An accept witness for `stage0` and `self` that inspects the emitted IR or binary. |
| `repo` | At least one `Evidence` test. |
| `prose` | A `Reason` and no witnesses. The statement is an overview, a definition, a tooling claim or otherwise not mechanically testable. |
| `pending` | An approved cut awaiting integration, with `Source: cut C1N`. Witnesses may be `TBD-pending-integration`. Integration replaces them with real tests and changes the kind. No rule is pending: cuts C10–C20 are integrated and cite `Round12CutMatrixTests` and `Round12CutSelfTests`. |

**SPEC coverage heuristic.** A *normative unit* is a non-blank line of `SPEC.md` before the `## 6.` heading that is not one of these:
- a heading;
- a horizontal rule (`---`);
- a table separator row;
- a table header row (the row directly above a separator).

Units and quotes are normalised the same way: backticks and `*` are removed and runs of whitespace collapse to one space. Every unit must contain at least one rule quote, and every quote must occur in some unit. A new bullet, sentence line or table row in SPEC therefore fails the check until a rule quotes it. Changing the wording of a quoted line fails the check until the quote is updated. The heuristic works on whole lines. It cannot tell whether every clause of a long line has its own rule, so reviewers should split a new clause into its own rule when it carries its own verdict.

**Witness conventions.** The Round-12 witnesses in `Round12RuleWitnessTests.cs` and in the other `Round12RuleWitness*Tests.cs` files follow these conventions:
- Each test is named after its rule (`R3_3_ReturnEscape_Accept` witnesses `R3.3-return-escape`).
- Each test runs all three frontends.
- No test contains a C10–C20 cut form, except `R3_7_SwitchDeclarationSpace_Reject`. That test is C#-invalid (CS0128) whatever the cuts, so it stays valid.
- A reject witness asserts the expected ID on every frontend. It also asserts that the Analyzer and Stage-0 report equal ID sets, and that vib-self reports the same set unless the program contains a banned construct (R4-banned-fail-closed).
- For a C#-invalid program (a genuine C# error or a diagnostic the project promotes to an error), a reject witness asserts the CS error from the SDK, the CS error or a named more specific Vibranium diagnostic from Stage-0, and rejection by vib-self with any VIB diagnostic (SPEC §4 "C# validity").

**Decisions applied.** SPEC states the Round-12 decisions F1–F7 and F9, and its §4 "C# validity" paragraph supersedes F8 and states the warning policy. Its §3.3 "Temporaries" bullet and its §3.2 assignment-value sentence state the ledger-v3 decisions. This ledger quotes that text.

| Decision | Content |
|---|---|
| F1 | Stage-0 fails closed on every promoted warning under any suppression: `#pragma warning disable`, `SuppressMessage`, `.editorconfig` severity, generated-code markers and `<NoWarn>`. |
| F2 | `static T Dup<T>(in T x) => x;` is exactly VIB005. |
| F3 | Moving the owner of a `Lines()` loop is exactly VIB003. |
| F4 | Using a moved `OptionMut` is exactly VIB001. |
| F5 | Using a span after `Move(s)` is VIB001. |
| F6 | A `Span<T>` or `ReadOnlySpan<T>` with a !Copy element type is VIB000, with the message "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2." |
| F7 | `{ } x` on a union is VIB000. Only `var x` binds a union directly. |
| F8 | Superseded by SPEC §4 "C# validity": every compiler, vib-stage2 included, rejects every C#-invalid program, including diagnostics the project promotes to errors. IDs and wording are not compared for C#-invalid programs. |
| F9 | Tuple literals, tuple types and deconstruction are all VIB000. |
| Warnings | The 13 diagnostics in `Vibranium.App.props`' `WarningsAsErrors` are errors on every frontend. Any other C# warning (CS0168, CS0219 and the like) is not a language error: the Analyzer, `vib` and `vib-stage2` accept the program, while `dotnet build` under the default profile refuses it because that profile also sets `TreatWarningsAsErrors` (SPEC §4 "C# validity", R4-promoted-diagnostics, R4-default-profile-build-policy). |
| Temporaries | A named variable cannot receive, and a function cannot return, a reference or view whose provenance includes a temporary: VIB007. A use within the expression that creates the temporary, including the payload bindings of the patterns it feeds, is valid. A `foreach` whose iteration variable is a reference or view into a temporary stays VIB007 in v0.2 (SPEC §3.3 "Temporaries"; R3.3-temporary-borrow, R3.3-temporary-return, R3.3-temporary-foreach). |
| Assignment value | Using the value of an assignment expression moves a !Copy destination, `Span<T>` included, so a later use of the destination is VIB001 (SPEC §3.2; R3.2-assignment-value). |

## Preamble: dual execution, scope and trust boundary

### R0-dual-execution — Vibranium is a checked subset of C# 15 with two execution paths
- Quote (§0): Vibranium is a strict, ownership- and borrow-checked subset of base C# 15
- Kind: prose
- Reason: Overview sentence; the concrete obligations it introduces are the rules of §1–§5.

### R0-managed-path — the managed path runs the unmodified source on Roslyn and .NET 11
- Quote (§0): Runs directly on Roslyn 5.12.0 + .NET 11 with zero user-source or IL rewriting
- Kind: prose
- Reason: Tooling claim (IDE support, CLR debugging, live analyzer). Every accept witness compiles the unmodified source with Roslyn for its managed reference run, but no verdict can falsify "full IDE support".

### R0-native-path — vib lowers the verified program to LLVM 18 with zero authored C files
- Quote (§0): Lowers the exact same verified program through Vibranium MIR to standalone, zero-GC, zero-CLR LLVM 18 native machine code
- Quote (§0): zero authored C runtime/shim files in the Vibranium compiler or runtime
- Kind: repo
- Evidence: `Vibranium.Tests.VibraniumStdAndL1Tests.M3_VibraniumStd_SelfCheck_ZeroDiagnostics_And_ZeroCFiles`
- Note: The "zero-GC, standalone" part is witnessed by R5.3-libc-only.

### R0-scope — guarantees are scoped to verified v0.2 programs on Linux x86_64
- Quote (§0): apply strictly to verified programs within the closed Vibranium v0.2 subset on the supported Linux x86_64 native target
- Kind: prose
- Reason: Scope statement (target platform, OOM and stack-exhaustion exclusions, role of ASan and the Lean metatheory); it bounds the other rules and has no verdict of its own.

### R0-fail-closed-pragma — vib fails closed regardless of #pragma suppressions and analyzer settings
- Quote (§0): vib always independently runs full frontend + MIR ownership/borrow verification and unconditionally fails closed
- Expect: the suppressed ID (for example VIB001) from vib and vib-self for a `#pragma`. An attempted `[SuppressMessage]` application is itself rejected with VIB000 under F15 by the SDK/Analyzer, not honoured as a suppression.
- Reject shape: `#pragma warning disable VIB001` around a use after move; Stage-0 and self still reject.
- Accept shape: the same file without the use after move.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Reject_PragmaWarningDisable` [stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Reject_SuppressMessageAttribute` [stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Reject_GeneratedCodeMarkerAndName` [stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Reject_EditorConfigSeverityNone` [stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Reject_PromotedWarningUnderPragma` [stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Reject_NoWarnOption` [stage0, self]
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.GeneratedMarkersCannotDisableOwnershipAnalysis` [stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R0_FailClosedPragma_Accept` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.SuppressMessageCannotSilenceOwnershipCompilerDiagnostics` [analyzer, stage0]
- Note: The rejection witness on Analyzer covers `[SuppressMessage]`. An SDK build using `#pragma` to suppress diagnostics remains outside the safety boundary in §0.

## §1 Core design principles

### R1.1-csharp-valid — every Vibranium source file is valid C# 15
- Quote (§1.1): Every Vibranium source file is 100% valid C# 15.
- Expect: the Roslyn CS error from the SDK; the CS error or a more specific Vibranium diagnostic from Stage-0; rejection by vib-self on its own (§4 C# validity; IDs are not compared)
- Reject shape: a C#-invalid program (type error such as `int x = "a";`).
- Accept shape: the same program with the type error fixed.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_CSharpValid_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_CSharpInvalidSelf_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round7_ExhaustiveReturnPaths_And_Reachability_Differential` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_CSharpValid_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_CSharpInvalidSelf_Accept` [analyzer, stage0, self]

### R1.1-app-props — Vibranium.App.props injects the implicit usings and build settings
- Quote (§1.1): Vibranium.App.props injects implicit System, Vibranium, and static Vibranium.Prelude imports
- Quote (§1.1): pins <CheckForOverflowUnderflow>false</CheckForOverflowUnderflow> and <InvariantGlobalization>true</InvariantGlobalization>
- Kind: prose
- Reason: MSBuild configuration fact about Vibranium.App.props, not a frontend verdict. Its observable consequences are rules of their own: wrapping arithmetic (R5.3-wrapping), the promoted warnings (R1.1-promoted-*) and the build policy for other warnings (R4-default-profile-build-policy). The test harness mirrors the props through `VibraniumCompilerDriver.CreateCompilationFromPath`.

### R1.1-promoted-cs0183 — CS0183 (expression is always of the provided type) is an error
- Quote (§1.1): promotes CS0183, CS0184, CS8509, CS8519, CS8520, CS8524, CS8778, CS8793, CS8794, CS8846, CS9191, CS9192, and CS9193 to errors
- Expect: CS0183 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS0183 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject shape: `int x = 1; bool b = x is int;`
- Accept shape: a type test whose outcome is not statically known.
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.SourcePatternControlsPreserveBindingsAndConstants` [analyzer, stage0, self]

### R1.1-promoted-cs0184 — CS0184 (expression is never of the provided type) is an error
- Quote (§1.1): promotes CS0183, CS0184
- Expect: CS0184 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS0184 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject shape: `int x = 1; bool b = x is long;`
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs0184_Accept` [analyzer, stage0, self]

### R1.1-promoted-cs8509 — CS8509 (non-exhaustive switch expression) is an error
- Quote (§1.1): CS8509
- Expect: CS8509 as an error (SDK, Stage-0); VIB000 from vib-self
- Reject shape: `int r = x switch { 1 => 1 };`
- Accept shape: the same switch with a discard arm.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8509_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round3_NegativeDiagnostics_And_Stage0Parity` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8509_Accept` [analyzer, stage0, self]

### R1.1-nonexhaustive-switch-fail-closed — vib rejects non-exhaustive switch expressions regardless of #pragma
- Quote (§1.1): non-exhaustive switch expressions are likewise rejected by vib regardless of #pragma/MSBuild settings
- Expect: CS8509 / VIB000 from vib and vib-self even under `#pragma warning disable CS8509`
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_NonexhaustiveSwitchFailClosed_Reject` [stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_NonexhaustiveSwitchFailClosed_Accept` [analyzer, stage0, self]
- N/A reject [analyzer]: the SDK honours `#pragma warning disable CS8509` by design (R0-fail-closed-pragma); only `vib` and `vib-self` must fail closed.

### R1.1-promoted-cs8519 — CS8519 (expression never matches the pattern) is an error
- Quote (§1.1): CS8519
- Expect: CS8519 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS8519 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_NoWarningControlsRemainAccepted` [analyzer, stage0, self]

### R1.1-promoted-cs8520 — CS8520 (expression always matches the constant) is an error
- Quote (§1.1): CS8520
- Expect: CS8520 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS8520 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_NoWarningControlsRemainAccepted` [analyzer, stage0, self]

### R1.1-promoted-cs8524 — CS8524 (switch expression misses unnamed enum values) is an error
- Quote (§1.1): CS8524
- Expect: CS8524 as an error (SDK, Stage-0); VIB000 from vib-self
- Reject shape: `e switch { Mode.A => 1, Mode.B => 2 }` over an enum.
- Accept shape: the same switch with a discard arm.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8524_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8524_Accept` [analyzer, stage0, self]

### R1.1-promoted-cs8778 — CS8778 (constant may overflow a native integer at runtime) is an error
- Quote (§1.1): CS8778
- Kind: prose
- Reason: Not mechanically testable in v0.2: CS8778 needs a constant converted to `nint`/`nuint` inside a `checked` context, and `checked` is banned (R1-sub-checked) while cut C17 bans `nint`/`nuint`, so no Vibranium program can raise it. The promotion stays in Vibranium.App.props as defence in depth (cut-sensitive).

### R1.1-promoted-cs8793 — CS8793 (input always matches the pattern) is an error
- Quote (§1.1): CS8793
- Expect: CS8793 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS8793 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8793_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8793_Accept` [analyzer, stage0, self]

### R1.1-promoted-cs8794 — CS8794 (input always matches the provided pattern) is an error
- Quote (§1.1): CS8794
- Expect: CS8794 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS8794 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8794_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.SourcePatternControlsPreserveBindingsAndConstants` [analyzer, stage0, self]

### R1.1-promoted-cs8846 — CS8846 (switch expression not exhaustive because of a when clause) is an error
- Quote (§1.1): CS8846
- Expect: CS8846 as an error (SDK, Stage-0); VIB000 from vib-self
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8846_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8846_Accept` [analyzer, stage0, self]

### R1.1-promoted-cs9191 — CS9191 (ref argument for an in parameter) is an error
- Quote (§1.1): CS9191
- Expect: CS9191 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS9191 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject shape: `F(ref x)` for `static void F(in int x)`.
- Accept shape: `F(in x)`.
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.SourcePatternControlsPreserveBindingsAndConstants` [analyzer, stage0, self]

### R1.1-promoted-cs9192 — CS9192 (ref readonly argument without in or ref) is an error
- Quote (§1.1): CS9192
- Quote (§1-subset): An argument for a ref readonly parameter written without in or ref (CS9192, promoted to an error).
- Expect: CS9192 as an error (SDK, Stage-0); VIB000 from vib-self
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs9192_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs9192_Accept` [analyzer, stage0, self]
- Note: A user method cannot raise CS9192 after cut C10 (no user `ref readonly` parameters); the C10-free reject witness passes a plain argument to the BCL `MemoryMarshal.CreateReadOnlySpan(ref readonly T, int)`.

### R1.1-promoted-cs9193 — CS9193 (non-variable argument for ref readonly) is an error
- Quote (§1.1): CS9193
- Quote (§1-subset): Pass a variable as in x or ref x; a non-variable argument is CS9193.
- Expect: CS9193 as an error (SDK); Stage-0 reports it inside VIB000 "C# warning CS9193 is not permitted in Vibranium: …"; vib-self must reject it too (§4 C# validity: promoted diagnostics are errors; IDs are not compared)
- Reject shape: `MemoryMarshal.CreateReadOnlySpan(5, 1)`: a non-variable argument for a BCL `ref readonly` parameter (Core and Std have no `ref readonly` parameters after cut C10).
- Accept shape: `.AsRef()` on an `Option<T>` local.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs9193_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Note: After cut C10 only a BCL `ref readonly` parameter can raise CS9193, and such a call is also an unwhitelisted BCL call (R1-sub-unwhitelisted-bcl, VIB000). The negative control therefore cannot isolate CS9193: vib-self may reject it for the ban rather than for C# validity.

### R1.1-app-bootstrap — Exe builds link VibraniumAppBootstrap; vib skips it
- Quote (§1.1): links Vibranium.Internal.VibraniumAppBootstrap ([ModuleInitializer] calling VibraniumRuntime.Initialize()) into Exe builds
- Kind: prose
- Reason: Build wiring of the managed path. Its observable effect, the panic exit code 101 on .NET, is witnessed by R5.4-panic-contract.

### R1.2-null-literal — null literals are banned
- Quote (§1.2): null literals
- Expect: VIB008
- Reject shape: `Node n = null;` (C#-valid with nullable warnings off) or `null` passed anywhere.
- Accept shape: absence through `Option<Node>` and `None`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_2_NullLiteral_Accept` [analyzer, stage0, self]

### R1.2-nullable-annotation — nullable type annotations T? are banned
- Quote (§1.2): nullable type annotations T? (Node?, int?; conditional c ? a : b is permitted)
- Expect: VIB008
- Reject shape: `int? x = 1;`
- Accept shape: a conditional expression `c ? a : b`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round4_InvalidCasts_ObjectBan_WrapperMismatches_ConstOverflow_And_ShadowedDisposable_Rejected` [stage0, self]
- Accept: `Vibranium.Tests.Round11ParserTests.Round11_ConditionalDotDigit_RemainsANumericTrueBranch` [analyzer, stage0, self]

### R1.2-null-forgiving — the null-forgiving postfix x! is banned
- Quote (§1.2): null-forgiving postfix x! (logical negation !b is permitted) are banned (VIB008)
- Expect: VIB008
- Reject shape: `x!.Value`
- Accept shape: logical negation `!b`.
- Reject: `Vibranium.Tests.Round8RegressionTests.Round8_Rejections_PreserveAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_2_NullForgiving_Accept` [analyzer, stage0, self]

### R1.2-option-result — absence and recoverable errors use Option and Result
- Quote (§1.2): Absence and recoverable errors use zero-boxing C# 15 [Union] structs
- Kind: runtime
- Accept shape: `Some`/`None` and `Ok`/`Err` construction and matching with equal managed and native output.
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_2_OptionResult_Accept` [analyzer, stage0, self]

### R1.3-affine-move — memory-only owners are affine: var b = a moves
- Quote (§1.3): Affine Memory vs. Exact-Once Linear Resources:
- Quote (§1.3): are affine: var b = a; moves ownership cleanly.
- Expect: VIB001
- Reject shape: `var b = a; Use(a);` for a class instance or `Vec<int>`.
- Accept shape: use `b` only; native drops at scope exit and overwrite (ASan/LSan clean).
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ReachableOwnershipViolationsRemainRejected` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_3_AffineMove_Accept` [analyzer, stage0, self]

### R1.3-linear-exact-once — side-effecting resources are linear with exact-once disposal
- Quote (§1.3): are linear with exact-once disposal (VIB006): every normal control-flow path must dispose or move them exactly once
- Expect: VIB006
- Reject shape: a `class R : IDisposable` local that one branch never disposes or moves.
- Accept shape: `using var r = new R();` printing from `Dispose()` in the same order managed and native.
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_DivergentPartialCoverageRetainsLinearExit` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_3_LinearExactOnce_Accept` [analyzer, stage0, self]

### R1.4-shared-core — Analyzer and vib share the core; vib-self reaches a fixed point
- Quote (§1.4): invoke the exact same Roslyn-free Vibranium.Compiler.Core, Vibranium.Analysis, and Vibranium.Backend.Llvm pipeline
- Kind: repo
- Evidence: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_BootstrapTripleIdentityFixedPoint_And_PathIndependence`
- Note: The authoritative fixed-point check is `eng/bootstrap/bootstrap.sh` (AGENTS.md rule 5), which is not a test method. Diagnostic parity between the frontends is R4-diagnostic-parity.

### R1.5-bcl-ergonomics — BCL names and paired string overloads
- Quote (§1.5): Vibranium APIs and compiler lowerings follow standard .NET 11 BCL names
- Kind: prose
- Reason: Design rule for API authors (naming and overload pairing). The concrete aliases it promises are witnessed by R3.5-bcl-aliases and R2-asutf8-static.

## §1 Closed v0.2 subset (VIB000)

### R1-subset-fail-closed — any C# feature outside the verified subset is VIB000
- Quote (§1-subset): Vibranium v0.2 fails closed: any C# feature outside the verified subset is rejected with VIB000
- Kind: prose
- Reason: Umbrella sentence; each listed construct is its own rule below, and the BCL fail-closed rule is R3.5-fail-closed-members.

### R1-sub-ref-reassign-param — ref reassignment targets must be ref locals, not parameters
- Quote (§1-subset): Ref reassignment (r = ref value) targets must be ref locals (including ref readonly locals), not ref/in/out parameters
- Expect: VIB000
- Reject shape: `static void F(ref int p, ref int q) { p = ref q; }`
- Accept shape: ref reassignment of a `ref` local, and an ordinary write `p = 1` through a `ref` parameter.
- Reject: `Vibranium.Tests.Round9RegressionTests.Round9_ParameterRebinding_IsConsistentlyOutsideSubset` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]

### R1-sub-ref-assign-value-consumed — the value of a simple assignment through a ref-assignment cannot be consumed
- Quote (§1-subset): a simple assignment whose direct left-hand side is a ref-assignment expression cannot have its result consumed
- Expect: VIB000
- Reject shape: `int value = ((r = ref a) = 2);`
- Accept shape: the discarded statement `(r = ref a) = 2;`.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubRefAssignValueConsumed_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubRefAssignValueConsumed_Accept` [analyzer, stage0, self]

### R1-sub-inheritance — class inheritance is banned
- Quote (§1-subset): Class inheritance (: BaseClass)
- Expect: VIB000
- Reject shape: `class B {} class D : B {}`
- Accept shape: composition (a field of the other class).
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubInheritance_Accept` [analyzer, stage0, self]

### R1-sub-virtual — virtual, abstract and override are banned
- Quote (§1-subset): virtual, abstract, override
- Expect: VIB000
- Reject shape: `public virtual int F() => 1;` in a class.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubVirtual_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubVirtual_Accept` [analyzer, stage0, self]

### R1-sub-interfaces — arbitrary interfaces are banned (except class R : IDisposable)
- Quote (§1-subset): arbitrary interfaces (except class R : IDisposable
- Expect: VIB000
- Reject shape: `interface IShape {}` implemented by a class.
- Accept shape: `class R : IDisposable`.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubInterfaces_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubInterfaces_Accept` [analyzer, stage0, self]

### R1-sub-interface-typed — interface-typed variables, parameters, casts and boxing are banned
- Quote (§1-subset): as well as interface-typed variables, parameters, casts, or boxing.
- Expect: VIB000
- Reject shape: `IDisposable d = new R();`
- Accept shape: `R r = new R();` disposed through `using`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round5TrackA_IDisposableInterfaceValueBanned_4WayParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubInterfaceTyped_Accept` [analyzer, stage0, self]

### R1-sub-object-initializer — object and collection initializers on user types are banned
- Quote (§1-subset): Object and collection initializer syntax on user types (new S { A = 1 })
- Expect: VIB000
- Reject shape: `new S { A = 1 }`
- Accept shape: constructor or field assignment.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round3_Gatekeeper_BorrowConflicts_And_SpecCompliance_AnalyzerParity` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round3_NegativeDiagnostics_And_Stage0Parity` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubObjectInitializer_Accept` [analyzer, stage0, self]

### R1-sub-object-dynamic — object, dynamic and boxing conversions are banned
- Quote (§1-subset): object, dynamic, and any boxing conversion
- Expect: VIB000
- Reject shape: `object o = 1;`
- Accept shape: the concrete type.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubObjectDynamic_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round4_InvalidCasts_ObjectBan_WrapperMismatches_ConstOverflow_And_ShadowedDisposable_Rejected` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubObjectDynamic_Accept` [analyzer, stage0, self]

### R1-sub-union-declarations — union declarations, closed hierarchies and user [Union] types are banned
- Quote (§1-subset): C# 15 shorthand union declarations and closed hierarchies, user-defined [Union] types
- Expect: VIB000
- Reject shape: a user `[Union]` struct.
- Accept shape: `Option<T>`/`Result<T, E>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnionDeclarations_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnionDeclarations_Accept` [analyzer, stage0, self]

### R1-sub-ref-struct — user ref struct types and allows ref struct constraints are banned
- Quote (§1-subset): user-defined ref struct types, user where T : allows ref struct constraints
- Expect: VIB000
- Reject shape: `ref struct View { }`
- Accept shape: a plain struct.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubRefStruct_Accept` [analyzer, stage0, self]

### R1-sub-ref-local-view — ref locals bound to borrowed views are banned
- Quote (§1-subset): ref locals bound to borrowed views (Span<T>, ReadOnlySpan<T>, OptionRef<T>, OptionMut<T>)
- Expect: VIB000
- Reject shape: `ref Span<int> r = ref s;`
- Accept shape: `ref int r = ref s[0];`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubRefLocalView_Accept` [analyzer, stage0, self]

### R1-sub-explicit-layout — explicit layout and field offsets are banned
- Quote (§1-subset): [StructLayout(LayoutKind.Explicit)], [FieldOffset]
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubExplicitLayout_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubExplicitLayout_Accept` [analyzer, stage0, self]

### R1-sub-inline-array — [InlineArray] is banned
- Quote (§1-subset): [InlineArray]
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubInlineArray_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubInlineArray_Accept` [analyzer, stage0, self]

### R1-sub-union-protocol — direct calls to the obsolete union protocol members are banned
- Quote (§1-subset): Direct calls to [Obsolete(..., error: true)] internal [Union] protocol members
- Expect: VIB000 (C# also reports CS0619)
- Reject shape: `opt.HasValue` or `opt.TryGetValue(out var v)`.
- Accept shape: `opt.IsSome` or a pattern match.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnionProtocol_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnionProtocol_Accept` [analyzer, stage0, self]

### R1-sub-deconstruct — direct .Deconstruct calls are VIB006 on linear unions and VIB000 otherwise
- Quote (§1-subset): direct .Deconstruct(...) calls (VIB006 on linear union wrappers, VIB000 otherwise)
- Expect: VIB000 (non-linear), VIB006 (linear)
- Reject shape: `Some(1).Deconstruct(out var v);`
- Accept shape: a positional pattern `Some<int>(var v)`.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubDeconstruct_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumStdAndL1Tests.M2_L1_Rejects_DirectDeconstruct_And_PlaceScrutineeMatch_WithVib006` [stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubDeconstruct_Accept` [analyzer, stage0, self]

### R1-sub-tuples — tuples are banned: literals, tuple types and deconstruction
- Quote (§1-subset): Tuples: tuple literals, tuple types, and deconstruction declarations and assignments
- Expect: VIB000
- Reject shape: `var (a, b) = (new Vec<int>(), 1);`, `var (a, b) = (1, 2);` and a tuple literal `var t = (1, 2);`.
- Accept shape: the same values in separate locals.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubTuples_Reject_NoncopyDeconstruction` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubTuples_Reject_CopyDeconstruction` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubTuples_Reject_TupleLiteral` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubTuples_Accept` [analyzer, stage0, self]
- Note: Decision F9.

### R1-sub-property-noncopy — a by-value property getter returning a !Copy field is an illegal field move
- Quote (§1-subset): by-value property getters returning !Copy fields are rejected separately, as illegal field moves under VIB005
- Expect: VIB005
- Reject shape: `public Vec<int> Items => _items;`
- Accept shape: a getter returning a Copy field.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubPropertyNoncopy_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubPropertyNoncopy_Accept` [analyzer, stage0, self]

### R1-sub-unsafe — unsafe code, pointers, function pointers and fixed are banned
- Quote (§1-subset): unsafe, pointers, function pointers, fixed
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnsafe_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnsafe_Accept` [analyzer, stage0, self]

### R1-sub-stackalloc — user stackalloc is banned
- Quote (§1-subset): and user stackalloc.
- Expect: VIB000
- Reject shape: `Span<int> s = stackalloc int[4];`
- Accept shape: `Span<int> s = [0, 0, 0, 0];`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubStackalloc_Accept` [analyzer, stage0, self]

### R1-sub-async — async/await is banned
- Quote (§1-subset): async/await
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubAsync_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round5_TrackB_CheckedFloatAndEnumCasts_NonConstInit_DuplicateOrPartialSwitches_BareIsType_AsMutValueType_LocalFuncOverload_And_UsingAmbiguity_Rejected` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubAsync_Accept` [analyzer, stage0, self]

### R1-sub-iterator — iterator yield is banned
- Quote (§1-subset): iterator yield
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubIterator_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubIterator_Accept` [analyzer, stage0, self]

### R1-sub-threading — multi-threading APIs are banned
- Quote (§1-subset): and multi-threading APIs.
- Expect: VIB000
- Reject shape: `System.Threading.Thread.Sleep(1);`
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubThreading_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubThreading_Accept` [analyzer, stage0, self]

### R1-sub-lambda — delegates, lambdas, closures and events are banned
- Quote (§1-subset): Delegates, lambdas, closures, events
- Expect: VIB000
- Reject shape: a lambda assigned to `Func<int>`.
- Accept shape: a `static` local function.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round3_Gatekeeper_BorrowConflicts_And_SpecCompliance_AnalyzerParity` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round3_NegativeDiagnostics_And_Stage0Parity` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubLambda_Accept` [analyzer, stage0, self]

### R1-sub-capturing-local-function — non-static local functions are banned
- Quote (§1-subset): non-static (capturing) local functions (static local functions and static/instance methods are supported)
- Expect: VIB000
- Reject shape: `int F() => 1;` declared without `static` inside a method.
- Accept shape: `static int F() => 1;`
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubCapturingLocalFunction_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round7_NegativeDiagnosticsParity` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubCapturingLocalFunction_Accept` [analyzer, stage0, self]

### R1-sub-exceptions — try, catch, finally and throw are banned
- Quote (§1-subset): Exceptions (try, catch, finally, throw)
- Expect: VIB000
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubExceptions_Accept` [analyzer, stage0, self]

### R1-sub-finalizer — finalizers are banned
- Quote (§1-subset): finalizers (~C())
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubFinalizer_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubFinalizer_Accept` [analyzer, stage0, self]

### R1-sub-static-ctor — static constructors are banned
- Quote (§1-subset): static constructors (static C())
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubStaticCtor_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubStaticCtor_Accept` [analyzer, stage0, self]

### R1-sub-static-field — static fields and static auto-properties other than const are banned
- Quote (§1-subset): all static fields or static auto-properties other than const (including static readonly)
- Expect: VIB000
- Reject shape: `static readonly int X = 1;`
- Accept shape: `const int X = 1;`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubStaticField_Accept` [analyzer, stage0, self]

### R1-sub-reflection — reflection is banned
- Quote (§1-subset): Reflection
- Expect: VIB000
- Reject shape: `typeof(Program).Name`
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubReflection_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubReflection_Accept` [analyzer, stage0, self]

### R1-sub-array — raw T[] arrays are banned in user code
- Quote (§1-subset): raw T[] arrays in user code
- Expect: VIB000
- Reject shape: `int[] a = new int[3];`
- Accept shape: `Vec<int>` or a span collection expression.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubArray_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubArray_Accept` [analyzer, stage0, self]

### R1-sub-goto — unstructured goto is banned
- Quote (§1-subset): unstructured goto
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubGoto_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubGoto_Accept` [analyzer, stage0, self]

### R1-sub-checked — checked blocks and expressions are banned
- Quote (§1-subset): checked(...) blocks/expressions
- Expect: VIB000
- Accept shape: wrapping arithmetic without `checked` (R5.3-wrapping).
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubChecked_Accept` [analyzer, stage0, self]

### R1-sub-get-enumerator — explicit GetEnumerator calls and enumerator locals are banned
- Quote (§1-subset): explicit .GetEnumerator() calls or enumerator locals/types outside foreach
- Quote (§3.2): Explicit .GetEnumerator() calls and enumerator locals/types outside foreach are banned (VIB000).
- Expect: VIB000
- Reject shape: `var e = v.GetEnumerator();`
- Accept shape: `foreach (var x in v)`.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubGetEnumerator_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubGetEnumerator_Accept` [analyzer, stage0, self]

### R1-sub-unwhitelisted-bcl — unwhitelisted external BCL or P/Invoke calls are banned
- Quote (§1-subset): any unwhitelisted external BCL or P/Invoke call.
- Expect: VIB000
- Reject shape: `System.Math.Abs(-1)` or a `[DllImport]` method.
- Accept shape: a whitelisted call such as `WriteLine(1)`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round3_NegativeDiagnostics_And_Stage0Parity` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubUnwhitelistedBcl_Accept` [analyzer, stage0, self]

### R1-sub-switch-local-function — local functions directly in a switch section are banned (C1)
- Quote (§1-subset): Local functions declared directly in a switch section.
- Quote (§1-subset): Local functions must be declared inside a block; declaring one directly in a switch section is not supported in Vibranium v0.2.
- Expect: VIB000 with the quoted message
- Reject shape: `case 1: static int F() => 1; ...`
- Accept shape: `case 1: { static int F() => 1; ... }`
- Reject: `Vibranium.Tests.Round10BindingTests.Round10_DirectSwitchLocalFunctions_AreOutsideVersion02` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Note: Cut C18 also bans declarations directly in a switch section; C1 keeps its own message (cuts file, C18).

### R1-sub-struct-piecewise — field-by-field initialization of a struct with owned fields is banned (C2)
- Quote (§1-subset): Field-by-field initialization of a struct with owned fields
- Quote (§1-subset): A struct with owned fields must be initialized in one assignment; initializing it field by field (including through out) is not supported in Vibranium v0.2.
- Expect: VIB000 with the quoted message
- Reject shape: `S s; s.Items = []; s.N = 1;` for a struct with a `Vec<int>` field.
- Accept shape: a plain-value struct initialized field by field, and a fully initialized owned struct whose field is overwritten.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.OwnedStructInitialization_PiecewiseStorageIsOutsideV02` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10SourceValidityTests.OwnedStructInitialization_WholeConstructionAndInitializedWritesRemainValid` [analyzer, stage0, self]

### R1-sub-generic-pattern — patterns other than var and discard on a generic-typed value are banned (C4)
- Quote (§1-subset): Patterns other than var and discard on a value whose type is a generic type parameter
- Quote (§1-subset): Patterns other than var and discard on a value of generic type parameter type are not supported in Vibranium v0.2.
- Expect: VIB000 with the quoted message
- Reject shape: `static bool F<T>(T x) => x is int;`
- Accept shape: `x is var v` and a `_` arm on a generic value.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_SubGenericPattern_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round11Stage0Tests.OpenGenericPatternCut_DoesNotHideIndependentCallerInitialization` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.GenericVarAndDiscardPatternsRemainSupported` [analyzer, stage0, self]

### R1-sub-conditional-compilation — conditional compilation directives are banned (C5)
- Quote (§1-subset): Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef); .NET and native builds must compile the same code.
- Quote (§1-subset): Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef) are not supported in Vibranium v0.2; .NET and native builds must compile the same code.
- Expect: VIB000 with the quoted message
- Accept shape: `#region`/`#endregion` and `#pragma`.
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.CanonicalCutsHaveOneDiagnosticAtTheirFirstKeyword` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditParserTests.Round11Audit_AllowedControls_PreserveFullDifferentialParity` [analyzer, stage0, self]

### R1-sub-file-local-type — file-local types are banned (C6)
- Quote (§1-subset): File-local types (file class, file struct).
- Quote (§1-subset): File-local types (file class, file struct) are not supported in Vibranium v0.2; declare the type without the file modifier.
- Expect: VIB000 with the quoted message
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.CanonicalCutsHaveOneDiagnosticAtTheirFirstKeyword` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditParserTests.Round11Audit_AllowedControls_PreserveFullDifferentialParity` [analyzer, stage0, self]

### R1-sub-partial — partial types and members are banned (C7)
- Quote (§1-subset): partial types and members, except the locked Vibranium.Std.Sys [Native] catalog.
- Quote (§1-subset): Partial types and members are not supported in Vibranium v0.2.
- Expect: VIB000 with the quoted message
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.CanonicalCutsHaveOneDiagnosticAtTheirFirstKeyword` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditParserTests.Round11Audit_AllowedControls_PreserveFullDifferentialParity` [analyzer, stage0, self]

### R1-sub-nested-in-generic — types nested in a generic type are banned (C8)
- Quote (§1-subset): Types nested inside a generic type (types nested in non-generic types remain supported).
- Quote (§1-subset): Types nested inside a generic type are not supported in Vibranium v0.2; declare the type at namespace level.
- Expect: VIB000 with the quoted message
- Accept shape: a class nested in a non-generic class.
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.CanonicalCutsHaveOneDiagnosticAtTheirFirstKeyword` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditParserTests.Round11Audit_AllowedControls_PreserveFullDifferentialParity` [analyzer, stage0, self]

### R1-sub-ctor-chaining — constructor chaining is banned (C9)
- Quote (§1-subset): Constructor chaining (: this(...)).
- Quote (§1-subset): Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method.
- Expect: VIB000 with the quoted message
- Accept shape: two constructors without chaining.
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.CanonicalCutsHaveOneDiagnosticAtTheirFirstKeyword` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditParserTests.Round11Audit_AllowedControls_PreserveFullDifferentialParity` [analyzer, stage0, self]

### R1-cut-c10-ref-readonly-parameter — ref readonly parameters are banned (cut C10)
- Quote (§1-subset): A ref readonly parameter in any method, local function, constructor, operator or indexer. in parameters, ref readonly locals and ref readonly returns stay supported.
- Source: cut C10
- Expect: VIB000 "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: a `ref readonly` parameter in a method, local function, constructor, operator or indexer (anchor: `ref`).
- Accept shape: an `in` parameter; `ref readonly` locals and returns.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c11-underscore-identifier — declaring an identifier named _ is banned (cut C11)
- Quote (§1-subset): Declaring any symbol named _: local, parameter, field, property, method, type, type parameter, label, foreach variable, or pattern/out/deconstruction designation where C# binds a real variable named _. Namespace _, using alias _, query range variables, and tuple element names _ are covered too. Discards stay: _ = e, out _, out var _, _ patterns, case _, and every place C# itself treats _ as a discard.
- Source: cut C11
- Expect: VIB000 "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: a local, parameter, field, type, label or foreach variable named `_` (anchor: `_`).
- Accept shape: discards `_ = e`, `out _`, `out var _`, `var (_, y)`, `_` patterns, `case _`.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c12-optional-parameter — optional parameters are banned (cut C12)
- Quote (§1-subset): An optional parameter: a parameter with a default value (int x = 0), or with [Optional] or [DefaultParameterValue].
- Source: cut C12
- Expect: VIB000 "Optional parameters are not supported in Vibranium v0.2; declare an overload instead." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `int x = 0`, `[Optional]` or `[DefaultParameterValue]` (anchor: the parameter's first token).
- Accept shape: two overloads.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutSelfTests.QualifiedOptionalAttributesIgnoreUnrelatedAliases` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.DistinctCutsAtTheSameTokenDoNotCollapse` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c13-named-argument-order — named arguments out of parameter order are banned (cut C13)
- Quote (§1-subset): A named argument out of its parameter's position, meaning its position in the argument list differs from its parameter's index. Named arguments in their own position stay.
- Source: cut C13
- Expect: VIB000 "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `F(b: 1, a: 2)` (anchor: the argument name).
- Accept shape: named arguments in their own position, `F(a: 2, b: 1)`.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c14-assign-this — assigning to this is banned (cut C14)
- Quote (§1-subset): Any assignment whose target is this (simple, compound, deconstruction, and prefix/postfix ++/-- on this), and ref this passed or bound as a writable ref (ref this, out this). Reading this and field access through it stay.
- Source: cut C14
- Expect: VIB000 "Assigning to this is not supported in Vibranium v0.2; assign the fields individually." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `this = other;` in a struct method; `ref this`/`out this` as a writable ref (anchor: `this`).
- Accept shape: reading `this` and assigning fields through it.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c15-conditional-ref — conditional ref expressions are banned (cut C15)
- Quote (§1-subset): A conditional ref expression, c ? ref a : ref b, in any position.
- Source: cut C15
- Expect: VIB000 "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `ref int r = ref c ? ref a : ref b;` (anchor: `?`).
- Accept shape: an `if` statement choosing the ref local.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c16-nested-collection — nested and branch collection expressions are banned (cut C16)
- Quote (§1-subset): A collection expression that is an element or spread operand of another collection expression, or a branch of ?:, or an arm value of a switch expression.
- Source: cut C16
- Expect: VIB000 "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `[.. [1, 2]]`, `c ? [1] : [2]`, `x switch { _ => [1] }` (anchor: the inner `[`).
- Accept shape: the inner collection assigned to a local first.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c17-native-integers — nint and nuint are banned (cut C17)
- Quote (§1-subset): The native-sized integer types nint and nuint, including System.IntPtr and System.UIntPtr and their aliases, in any type position, cast, sizeof, generic argument or member access (nint.MaxValue).
- Source: cut C17
- Expect: VIB000 "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `nint`, `nuint`, `IntPtr`, `UIntPtr` in any type position, cast, `sizeof`, generic argument or member access (anchor: the type token).
- Accept shape: `long` and `ulong`.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.DistinctCutsAtTheSameTokenDoNotCollapse` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c18-switch-section-declaration — declarations directly in a switch section are banned (cut C18)
- Quote (§1-subset): A statement directly inside a switch section (not inside a nested block) that declares a local whose scope is the switch block: a local declaration, a local function (which also reports the local-function cut above), or any out variable, pattern designation or deconstruction designation that C# scopes to the switch block, including those in an if or expression-statement condition, because they leak into the enclosing statement list. Pattern variables in case labels stay supported.
- Source: cut C18
- Expect: VIB000 "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `case 1: int x = 1; ...`, and `out var`/pattern designations in section statements (anchor: the declaring statement's first token).
- Accept shape: the section's statements wrapped in `{ }`; pattern variables in `case` labels.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.DistinctCutsAtTheSameTokenDoNotCollapse` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c19-constant-condition — constant conditions, guards and selectors are banned (cut C19)
- Quote (§1-subset): A condition that is a C# constant expression (Roslyn ConstantValue present, including const locals/fields, !true, 1 == 1): the condition of if, while, do, for and ?:; a when clause; or the governing expression of a switch statement or switch expression. Exempt: a literal true as the condition of while, do ... while or for, and for (;;). Constant operands inside a non-constant condition stay.
- Source: cut C19
- Expect: VIB000 "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `if (false)`, `when false`, `switch (1)`, a `const bool` condition (anchor: the condition's first token).
- Accept shape: `while (true)`, `do ... while (true)`, `for (;;)`, and constant operands inside a non-constant condition.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutSelfTests.ConstantConditionsUseDeclarationAndExpressionInformation` [self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c20-relational-and-pattern — relational patterns and and-patterns are banned (cut C20)
- Quote (§1-subset): Relational patterns (< e, <= e, > e, >= e) and the and pattern combinator, anywhere a pattern appears (is, switch statement labels, switch expression arms, nested and property patterns). or, not, constant, type, declaration, var, discard, property and positional patterns stay.
- Source: cut C20
- Expect: VIB000 "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `x is > 0`, `x is >= 1 and <= 9` (anchor: the relational operator or `and`).
- Accept shape: `or`, `not`, constant, type and property patterns; a `when` guard.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryConstructUsesTheCanonicalCutAndAnchor` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12CutMatrixTests.NearMissControlsKeepManagedAndNativeParity` [analyzer, stage0, self]

### R1-cut-c21-base-constructor — base constructor calls are banned (cut C21)
- Quote (§1-subset): A base constructor initializer : base(...), with or without arguments, on any constructor. Constructor chaining with : this(...) is covered by its own cut above.
- Source: cut C21
- Expect: VIB000 "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance." on every frontend, once per file at the first occurrence (anchor in the Reject shape)
- Reject shape: `public C(int x) : base() { X = x; }` on a sealed class, and two such constructors in one file (anchor: `base`).
- Accept shape: the same constructor without the initializer.
- Reject: `Vibranium.Tests.Round12RuleWitnessC21Tests.R1_CutC21BaseConstructor_Reject_SealedClass` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessC21Tests.R1_CutC21BaseConstructor_Reject_OncePerFile` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessC21Tests.R1_CutC21BaseConstructor_Accept` [analyzer, stage0, self]

### R1-cut-c22-list-patterns — list and slice patterns are banned (cut C22)
- Quote (§1-subset): List patterns and slice patterns ([7, 8], [var a, ..], [.., var rest]), anywhere a pattern appears (is, switch statement labels, switch expression arms, nested patterns), on any receiver.
- Source: cut C22
- Expect: VIB000 on all frontends. The Roslyn diagnostic is "List patterns are not supported in Vibranium v0.2; compare the length and index the elements."
- Reject shape: `if (span is [7, ..]) WriteLine(7);`.
- Accept shape: `if (span.Length == 2 && span[0] == 7) WriteLine(7);`.
- Reject: `Vibranium.Tests.RoslynSubsetGateTests.ListPatternsRejectEveryCheckedPosition` [analyzer, stage0]
- Reject: `Vibranium.Tests.RoslynSubsetGateTests.ListPatternStillRejectedBySelf` [self]
- Accept: `Vibranium.Tests.RoslynSubsetGateTests.ExplicitLengthAndIndexPreserveNativeOutput` [analyzer, stage0, self]

### R1-sub-scoped-foreach — foreach iteration variables cannot be scoped
- Quote (§1-subset): A foreach iteration variable marked scoped.
- Expect: VIB000 at scoped (Roslyn), regardless of whether the iteration variable is by value, ref, or ref readonly.
- Reject: `Vibranium.Tests.RoslynSubsetGateTests.ScopedForeachIterationVariablesAreRejected` [analyzer, stage0]
- Reject: `Vibranium.Tests.RoslynSubsetGateTests.ScopedForeachStillRejectedBySelf` [self]
- Accept: `Vibranium.Tests.RoslynSubsetGateTests.OrdinaryForeachIterationVariablesRemainSupported` [analyzer, stage0]
- Accept: `Vibranium.Tests.RoslynSubsetGateTests.OrdinaryForeachRemainsSupportedBySelf` [self]

### R1-sub-closure-matrix — diagnostic index for the closed subset
- Kind: prose
- Quote (§1-subset): `'<keyword>' is not supported in Vibranium.`
- Quote (§1-subset): `User alias-qualified names are not supported in Vibranium v0.2; use a qualified name or 'global::'.`
- Quote (§1-subset): `'extern alias' directives are not supported in Vibranium v0.2.`
- Quote (§1-subset): `'volatile' fields are not supported in Vibranium v0.2; use an ordinary field.`
- Quote (§1-subset): `'required' members are not supported in Vibranium v0.2; initialize members in a constructor.`
- Quote (§1-subset): `'as' is not supported in Vibranium v0.2; use a type pattern ('x is C c').`
- Quote (§1-subset): `The 'notnull' constraint is not supported in Vibranium v0.2; Vibranium types are never null, so omit it.`
- Quote (§1-subset): `Assembly-level attributes are not supported in Vibranium v0.2; remove the attribute.`
- Quote (§1-subset): `Module-level attributes are not supported in Vibranium v0.2; remove the attribute.`
- Quote (§1-subset): `Assembly-level attributes are not supported in Vibranium v0.2; set <GenerateAssemblyInfo>false</GenerateAssemblyInfo>.`
- Quote (§1-subset): `Assembly-level attributes are not supported in Vibranium v0.2; set <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>.`
- Quote (§1-subset): `Attribute '<bound type>' is not supported at this location in Vibranium.`
- Quote (§1-subset): `'init' accessors are not supported in Vibranium v0.2; use an explicit backing field and accessor bodies.`
- Quote (§1-subset): `List patterns are not supported in Vibranium v0.2; compare the length and index the elements.`
- Quote (§1-subset): `Labeled statements are not supported in Vibranium v0.2 ('goto' is not supported).`
- Quote (§1-subset): `'scoped' iteration variables are not supported in Vibranium v0.2; omit 'scoped' from the 'foreach' variable.`
- Quote (§1-subset): `Named positional elements in union-case patterns are not supported in Vibranium v0.2; drop the element name.`
- Reason: This reference table catalogs the frontend cuts and their messages; its last row records the independently implemented and tested roslyn-followup-2b union-pattern restriction.

### R1-sub-one-per-file — each cut reports one VIB000 per file at its first occurrence
- Quote (§1-subset): Each cut above reports one VIB000 per source file, at its first occurrence.
- Expect: exactly one VIB000 per cut per file, at the first occurrence
- Reject shape: two occurrences of one cut in one file; one VIB000 at the first.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryCutReportsOncePerFileAtItsFirstOccurrence` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutSelfTests.LateGenericAndNestedOccurrencesHaveOneDiagnosticPerFile` [self]
- Reject: `Vibranium.Tests.Round11AuditStage0Tests.CanonicalCutsHaveOneDiagnosticAtTheirFirstKeyword` [analyzer, stage0, self]
- N/A accept [analyzer, stage0, self]: a counting rule about rejected files; the accepted near-misses are those of each cut rule.

### R1-sub-banlist-not-cuts — the ban-list constructs below the cuts are bans, not cuts
- Quote (§1-subset): The constructs below are outside the subset too. They are not cuts: the one-per-file rule above does not apply to them, and the compilers' messages for them may differ, because §4 compares diagnostic IDs only.
- Kind: prose
- Reason: Scope statement for the ban-list bullets that follow it; each of them is its own rule with witnesses, and R4-banned-fail-closed covers the ID comparison.
- Note: SPEC text from the Round-12 ban-list proposal.

### R1-sub-null-operators — ?. ?[] ?? and ??= are banned (VIB008)
- Quote (§1-subset): The null-conditional operators ?. and ?[] and the null-coalescing operators ?? and ??=, rejected with VIB008 rather than VIB000 because no Vibranium value is null. Use Option<T> (IsSome, UnwrapOr) for absence.
- Expect: VIB008
- Reject shape: `c?.Touch();`, `s ?? "b"`, `s ??= "b";`
- Accept shape: `Option<int>.UnwrapOr(2)` for absence.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNullOperators_Reject_Conditional` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNullOperators_Reject_Coalescing` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNullOperators_Reject_CoalescingAssignment` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNullOperators_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-with — with expressions are banned
- Quote (§1-subset): with expressions (p with { X = 2 }, including p with { }). Copy the value and assign its fields.
- Expect: VIB000
- Reject shape: `p with { X = 2 }` and `p with { }` on a struct.
- Accept shape: `var q = p; q.X = 2;`
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubWith_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubWith_Reject_Empty` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubWith_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-sizeof — sizeof expressions are banned
- Quote (§1-subset): sizeof(...) expressions. Write the size as a literal.
- Expect: VIB000
- Reject shape: `WriteLine(sizeof(int));`, `sizeof(long)`
- Accept shape: a literal size, `3L * 4`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubSizeof_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubSizeof_Reject_Long` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubSizeof_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-nameof — nameof expressions are banned
- Quote (§1-subset): nameof(...) expressions. Write the name as a string literal.
- Expect: VIB000
- Reject shape: `nameof(x)` of a local and `nameof(Pick)` of a method.
- Accept shape: the string literal `"count"`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNameof_Reject_Local` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNameof_Reject_Method` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubNameof_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-params — params parameters are banned
- Quote (§1-subset): params parameters of any type (params int[], params ReadOnlySpan<T>). Declare a ReadOnlySpan<T> parameter and pass a collection expression (F([1, 2, 3])).
- Expect: VIB000
- Reject shape: `static int F(params ReadOnlySpan<int> xs)` and `params int[] xs`.
- Accept shape: a `ReadOnlySpan<int>` parameter called as `F([1, 2, 3])`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubParams_Reject_Span` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubParams_Reject_Array` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubParams_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-decimal — decimal is banned
- Quote (§1-subset): The decimal type and m-suffixed literals. Use a scaled long or double.
- Expect: VIB000
- Reject shape: `decimal d = Pick(); … static decimal Pick() => 1.5m;`
- Accept shape: a scaled `long` (cents).
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubDecimal_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubDecimal_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-extension-block — C# 14 extension blocks are banned; classic extension methods stay
- Quote (§1-subset): C# 14 extension(...) blocks. Classic extension methods (a this parameter in a static class) stay supported.
- Expect: VIB000
- Reject shape: `static class E { extension(int x) { public int Twice() => x * 2; } }` and a static extension member.
- Accept shape: `public static int Twice(this int x)` in a `static class`, called both as `3.Twice()` and `E.Twice(4)`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubExtensionBlock_Reject_Instance` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubExtensionBlock_Reject_Static` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubExtensionBlock_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-string-operators — string operators, constant string patterns and switch on string are banned
- Quote (§1-subset): string operators (==, !=, +), constant string patterns (s is "a") and switch on string. Compare UTF-8 bytes instead (s.AsUtf8().SequenceEqual("a".AsUtf8())).
- Expect: VIB000
- Reject shape: `s == "a"`, `s != "a"`, `s + "b"`, `s is "a"`, a switch statement and a switch expression on a `string`.
- Accept shape: `s.AsUtf8().SequenceEqual("a".AsUtf8())`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Reject_Equality` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Reject_Inequality` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Reject_Concatenation` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Reject_ConstantPattern` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Reject_SwitchStatement` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Reject_SwitchExpression` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubStringOperators_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-user-operators — user-defined operators and conversions are banned, except instance void operator +=
- Quote (§1-subset): User-defined operators and conversions: static unary, binary, equality and comparison operators (operator +, operator ==) and implicit/explicit conversion operators. The C# 14 instance compound-assignment operator void operator +=(T) stays supported.
- Expect: VIB000
- Reject shape: static `operator +`, unary `operator -`, `operator ==`/`!=`, `explicit operator int` and `implicit operator P`.
- Accept shape: an instance `public void operator +=(int d)` on a struct.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUserOperators_Reject_Binary` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUserOperators_Reject_Unary` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUserOperators_Reject_Equality` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUserOperators_Reject_ExplicitConversion` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUserOperators_Reject_ImplicitConversion` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUserOperators_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-spread — spread elements in collection expressions are banned
- Quote (§1-subset): Spread elements ..e in collection expressions. Add the elements one by one (Vec<T>.Add).
- Expect: VIB000
- Reject shape: `Vec<int> b = [..a.AsReadOnlySpan(), 3];` and `Span<int> b = [..a, 3];`
- Accept shape: a collection expression without spread, `[1, 2, 3]`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubSpread_Reject_Vec` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubSpread_Reject_Span` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubSpread_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-explicit-interface — explicit interface implementations are banned
- Quote (§1-subset): Explicit interface implementations (void IDisposable.Dispose()). Declare public void Dispose() on the linear type.
- Expect: VIB000
- Reject shape: `void IDisposable.Dispose() { … }` on `class R : IDisposable`.
- Accept shape: `public void Dispose()`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubExplicitInterface_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubExplicitInterface_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-goto-case — goto case and goto default are banned
- Quote (§1-subset): goto case and goto default, which count as unstructured goto.
- Expect: VIB000
- Reject shape: `goto case 2;` and `goto default;` in a switch section.
- Accept shape: the shared statements repeated in each section.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubGotoCase_Reject_Case` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubGotoCase_Reject_Default` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubGotoCase_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-auto-property — instance auto-properties and init accessors are banned
- Quote (§1-subset): Instance auto-properties ({ get; set; }, { get; }) and init accessors. Declare a field, or a property whose accessors have bodies over a field ([Mut] set on a class, §3.1).
- Expect: VIB000
- Reject shape: `public int X { get; set; }`, `{ get; }` assigned in the constructor, `{ get; init; } = 3`.
- Accept shape: `public int X { get { return _x; } [Mut] set { _x = value; } }` and an expression-bodied getter `public int X => _x;`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubAutoProperty_Reject_GetSet` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubAutoProperty_Reject_GetOnly` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubAutoProperty_Reject_Init` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubAutoProperty_Accept_Accessors` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubAutoProperty_Accept_ExpressionBodied` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-using-existing — using statements on an existing variable or expression are banned
- Quote (§1-subset): A using statement on an existing variable or expression (using (r) { ... }). Declare the resource in the statement: using (var r = ...) { ... } or using var r = ...;.
- Expect: VIB000
- Reject shape: `var r = new R(1); using (r) { … }`
- Accept shape: `using (var r = new R(1)) { … }` followed by `using var s = new R(2);`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUsingExisting_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUsingExisting_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-sub-unchecked — unchecked expressions and blocks stay supported; only checked is banned
- Quote (§1-subset): checked is the only banned overflow context: unchecked(...) expressions and unchecked { ... } blocks stay supported and change nothing, because integer arithmetic already wraps (§5.3).
- Expect: VIB000 for `checked(...)`; `unchecked` is accepted
- Reject shape: `checked(a - 1)`.
- Accept shape: `unchecked(a + 1)`, an `unchecked { }` block and `unchecked((int)0xFFFFFFFF)`, managed and native equal.
- Reject: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUnchecked_Reject_Checked` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBanListTests.R1_SubUnchecked_Accept` [analyzer, stage0, self]
- Note: SPEC text from the Round-12 ban-list proposal. Messages differ between frontends; witnesses compare IDs only (R4-banned-fail-closed).

### R1-entry-point — entry point forms: top-level statements without args, or parameterless Main
- Quote (§1-subset): Top-level statements (that do not reference implicit string[] args) or parameterless static void Main() / static int Main()
- Expect: VIB000
- Reject shape: top-level statements that read `args`, or `static void Main(string[] args)`.
- Accept shape: `static int Main()` returning an exit code; arguments through `Env.Args()`.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_EntryPoint_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_EntryPoint_Accept` [analyzer, stage0, self]

## §2 Type system, UTF-8 and string, zero-safety

### R2-copy-types — Copy value types copy bitwise on assignment, argument passing and return
- Quote (§2-types): Bitwise copy on =, argument pass, and return; zero drop glue.
- Quote (§2-types): and non-ref structs whose fields are all Copy
- Expect: VIB001 (reuse of a struct with a !Copy field)
- Reject shape: `var b = a; Use(a);` where the struct holds a `Vec<int>`.
- Accept shape: the same with a struct whose fields are all Copy; both copies stay usable.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.RuntimeStructInitialization_MovedParentsStillInvalidateFields` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_CopyTypes_Accept` [analyzer, stage0, self]

### R2-union-copy-iff-payload — Option, Some, Ok, Err are Copy iff the payload is; Result iff both are
- Quote (§2-types): are Copy iff their payload type is Copy. Result<T, E> is Copy iff both T and E are Copy.
- Expect: VIB001
- Reject shape: `var b = a; Use(a);` for `Option<Node>`, or `Result<int, Node>`.
- Accept shape: the same for `Option<int>` and `Result<int, int>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_UnionCopyIffPayload_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_UnionCopyIffPayload_Reject_Result` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_UnionCopyIffPayload_Accept` [analyzer, stage0, self]

### R2-u8-static — u8 literals are static, non-null even when empty, and may be returned
- Quote (§2-types): Static immutable UTF-8 byte slice
- Quote (§2-string): Evaluate directly to ReadOnlySpan<byte> backed by deduplicated static global constants
- Expect: VIB007 (the near-miss: returning a view of a local owner)
- Reject shape: returning `v.AsReadOnlySpan()` of a local `Vec<byte>`.
- Accept shape: `static ReadOnlySpan<byte> F() => "hi"u8;` and an empty `""u8`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_U8Static_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_U8Static_Accept` [analyzer, stage0, self]

### R2-string-copy — string is a Copy UTF-8 literal slice
- Quote (§2-types): Copy, non-zero-safe immutable UTF-8 literal slice
- Expect: VIB008 (the near-miss: `default(string)`, see R2-string-zero)
- Accept shape: `string a = "x"; string b = a; WriteLine(a); WriteLine(b);`
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_StringZero_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_StringCopy_Accept` [analyzer, stage0, self]

### R2-enum-int — user enums must have an int underlying type
- Quote (§2-types): non-int underlying rejected by VIB000
- Expect: VIB000
- Reject shape: `enum E : byte { A }`
- Accept shape: an `int` enum used in casts and a `switch`.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.UnsupportedEnumWidths_RemainOutsideSubset` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_EnumInt_Accept` [analyzer, stage0, self]

### R2-class-owned — a class instance is a single-owner affine heap allocation
- Quote (§2-types): Plain class Node (sealed optional)
- Quote (§2-types): !Copy (affine) single-owner non-null heap allocation
- Expect: VIB001
- Reject shape: use of a class local after it was moved.
- Accept shape: building and dropping a list of nodes, ASan/LSan clean.
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ReachableOwnershipViolationsRemainRejected` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_ClassOwned_Accept` [analyzer, stage0, self]

### R2-owned-struct — Vec<T> and structs with !Copy fields are affine inline owners
- Quote (§2-types): Vec<T> where T : unmanaged
- Quote (§2-types): !Copy (affine) inline value owning heap storage
- Expect: VIB001
- Reject shape: `var w = v; v.Push(1);` for a `Vec<int>`.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.RuntimeStructInitialization_MovedParentsStillInvalidateFields` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_OwnedStruct_Accept` [analyzer, stage0, self]

### R2-linear-types — IDisposable classes and L1 unions are linear owners
- Quote (§2-types): Linear Resource & L1 Unions
- Quote (§2-types): !Copy linear owner; exact-once disposal via using var or consumption/move by value (VIB006).
- Kind: prose
- Reason: Table summary of the linear policy; the testable obligations are R2-lin-* and R1.3-linear-exact-once.

### R2-shared-views — in T, ReadOnlySpan<T> and OptionRef<T> are shared Copy views
- Quote (§2-types): Shared Borrowed Views
- Quote (§2-types): Stack-bound shared borrows (ReadOnlySpan<T> and OptionRef<T> are Copy).
- Expect: VIB004 (writing through a shared view)
- Reject shape: `static void F(ReadOnlySpan<int> s) { s[0] = 1; }` (C# rejects this as CS8331; see Note) or a write through an `in` parameter's field.
- Accept shape: `ReadOnlySpan<int> a = v.AsReadOnlySpan(); var b = a;` with both read.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_SharedViews_Accept` [analyzer, stage0, self]

### R2-exclusive-views — Span<T> and OptionMut<T> are !Copy exclusive views
- Quote (§2-types): Exclusive Borrowed Views
- Quote (§2-types): Span<T> and OptionMut<T> are !Copy
- Expect: VIB001
- Reject shape: `var o2 = o1; o1.Set(None);` for an `OptionMut<Node>`.
- Accept shape: moving the `OptionMut<Node>` and using only the new name.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_ExclusiveViews_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_ExclusiveViews_Accept` [analyzer, stage0, self]

### R2-string-zero — string is non-zero-safe: default(string) and null are VIB008
- Quote (§2-string): string is Copy and non-zero-safe (default(string) and null are rejected by VIB008)
- Expect: VIB008
- Reject shape: `string s = default;`
- Accept shape: `string s = "";`
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_StringZero_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round6_NegativeDiagnosticsParity` [stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_StringZero_Accept` [analyzer, stage0, self]

### R2-string-surrogate — lone UTF-16 surrogates in string literals are VIB000
- Quote (§2-string): lone UTF-16 surrogates \uD800–\uDFFF are rejected by VIB000
- Expect: VIB000
- Reject shape: `WriteLine("\uD800");`
- Accept shape: a well-formed non-ASCII literal (`"é😀"`) printed identically managed and native.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round6TrackA_LexerStringSurrogatePairsAndLoneSurrogates_4WayParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_StringSurrogate_Accept` [analyzer, stage0, self]

### R2-asutf8-static — text.AsUtf8() yields a 'static ReadOnlySpan<byte>
- Quote (§2-string): yields a ReadOnlySpan<byte> with 'static provenance (empty loan set)
- Quote (§2-string): Every Vibranium.Core and Vibranium.Std UTF-8 text/path/key API provides plain string overloads alongside ReadOnlySpan<byte>.
- Expect: VIB007 (the near-miss: returning a view of a local `StringBuf`)
- Accept shape: `static ReadOnlySpan<byte> F() => "abc".AsUtf8();`
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_Asutf8Static_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_Asutf8Static_Accept` [analyzer, stage0, self]

### R2-string-ops — string.Length, indexing, ==, + and interpolation are VIB000
- Quote (§2-string): string.Length, character indexing s[i], ==/!=, concatenation +, and $"..." remain VIB000
- Expect: VIB000
- Reject shape: `"ab".Length`, `s[0]`, `a == b`, `a + b`, `$"{x}"`.
- Accept shape: `s.AsUtf8().Length`.
- Reject: `Vibranium.Tests.Round8RegressionTests.Round8_Rejections_PreserveAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_StringOps_Accept` [analyzer, stage0, self]

### R2-utf8-views-closed — Utf8Lines and Utf8ArgEnumerator only as return values and foreach collections
- Quote (§2-string): permitted solely as method return values (span.Lines(), StringBuf.Lines(), ArgList.GetEnumerator()) and direct foreach collections
- Expect: VIB000
- Reject shape: `var lines = text.Lines();` (a local of view type).
- Accept shape: `foreach (var line in text.Lines())`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_Utf8ViewsClosed_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_Utf8ViewsClosed_Accept` [analyzer, stage0, self]
- See also: R2-utf8-views-ref-foreach.

### R2-utf8-views-ref-foreach — foreach (ref var …) over the closed views is C#-invalid and every compiler rejects it
- Quote (§2-string): foreach (ref var ...) over these views is C#-invalid, CS1510, and every compiler rejects it
- Expect: CS1510 (SDK, Stage-0); vib-self rejects it (§4 C# validity; IDs are not compared)
- Reject shape: `foreach (ref var line in text.Lines())`.
- Accept shape: `foreach (var line in text.Lines())`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_Utf8ViewsRefForeach_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_Utf8ViewsRefForeach_Accept` [analyzer, stage0, self]

### R2-lines-split — Lines() splits on \r\n, \n and standalone \r
- Quote (§2-string): Lines() splits on \r\n, \n, or standalone \r
- Kind: runtime
- Accept shape: `""`, `"a\n"`, `"a\r\n"`, `"\n"` and interior empty lines give the documented line counts managed and native.
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinesSplit_Accept` [analyzer, stage0, self]

### R2-lines-loan — each yielded line inherits only the owner's shared loan
- Quote (§2-string): Each yielded ReadOnlySpan<byte> inherits only the source owner's shared loan
- Expect: VIB003 (mutation of the owner inside the loop); VIB001 (move)
- Reject shape: `foreach (var l in buf.Lines()) buf.Append("x");`
- Accept shape: a line saved into an outer variable and read after the loop while the owner is live.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinesLoan_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinesLoan_Reject_MoveOwner` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinesLoan_Accept` [analyzer, stage0, self]

### R2-native-byte-lengths — native output writes explicit byte lengths, preserving NUL bytes
- Quote (§2-string): Native output writes explicit byte lengths (write(fd, ptr, byte_len)), never NUL-terminated %s / strlen
- Kind: runtime
- Accept shape: writing `"a\0b"u8` produces the same three bytes managed and native.
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_NativeByteLengths_Accept` [analyzer, stage0, self]

### R2-zero-safe-types — the zero-safe types may be default-initialized
- Quote (§2-zero): Zero-safe types:
- Expect: VIB008 (the near-miss: a non-zero-safe type, R2.1-default-nonzerosafe)
- Accept shape: `default` of `int`, `Option<int>`, `Span<int>`, an `int` enum and an all-zero-safe struct, printed identically.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_NonZeroSafeTypes_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_ZeroSafeTypes_Accept` [analyzer, stage0, self]

### R2-non-zero-safe-types — classes, string, Vec, Result, Some, OptionRef, OptionMut and type parameters are non-zero-safe
- Quote (§2-zero): Non-zero-safe types:
- Expect: VIB008
- Reject shape: `default(Result<int, int>)`, `default(Some<int>)`, `default(OptionRef<Node>)`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_NonZeroSafeTypes_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_NonZeroSafeTypes_Reject_Some` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_ZeroSafeTypes_Accept` [analyzer, stage0, self]

### R2-init-rules — header of the VIB008 initialization rules
- Quote (§2-zero): Initialization Rules (VIB008):
- Kind: prose
- Reason: Heading of the numbered list R2.1–R2.4.

### R2.1-default-nonzerosafe — default, implicit zeroing and new T[n] of a non-zero-safe type are VIB008
- Quote (§2-zero): For any non-zero-safe type, default / default(T), implicit zeroing new S() / new T(), new T[n], and unassigned fields are compile-time errors (VIB008).
- Expect: VIB008
- Reject shape: `Vec<int> v = default;`
- Accept shape: `Vec<int> v = [];` and `new Vec<int>()`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_1_DefaultNonzerosafe_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_1_DefaultNonzerosafe_Accept` [analyzer, stage0, self]

### R2.1-implicit-new-struct — implicit zeroing new S() of a struct with a non-zero-safe field is VIB008
- Quote (§2-zero): implicit zeroing new S() / new T()
- Expect: VIB008
- Reject shape: `var s = new S();` for `struct S { public Vec<int> V; ... }` without a parameterless constructor.
- Accept shape: `new P()` for an all-zero-safe struct.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_1_ImplicitNewStruct_Accept` [analyzer, stage0, self]

### R2.1-vec-ctor — new Vec<T>() is explicit, default(Vec<T>) is VIB008
- Quote (§2-zero): Vibranium.Core provides an explicit public Vec() constructor
- Expect: VIB008
- Reject shape: a class with an unassigned `Vec<int>` field.
- Accept shape: `new Vec<int>()` and `new Vec<int>(4)` with `Count` 0.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_1_VecCtor_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecConstruct_Accept` [analyzer, stage0, self]

### R2.2-ctor-fields-assigned — every non-zero-safe field must be assigned before constructor exit
- Quote (§2-zero): all non-zero-safe fields must be definitely assigned before normal constructor exit (VIB008)
- Expect: VIB008
- Reject shape: a constructor that assigns a `Vec<int>` field on one branch only.
- Accept shape: a zero-safe `int` field the constructor leaves unassigned (assigned later outside the constructor, so C# raises no CS0649).
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_2_CtorFieldsAssigned_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_2_CtorFieldsAssigned_Accept` [analyzer, stage0, self]

### R2.2-ctor-this — evaluating this as a value or calling an instance member on this in a constructor is VIB008
- Quote (§2-zero): Throughout a constructor, even after every field is assigned, evaluating this as a value (passing, storing or aliasing it) or using explicit or implicit this as the receiver of an instance method, property or indexer is also VIB008
- Expect: VIB008
- Reject shape: `public C(int k) { K = k; K += Peek(); }` and `Register(this)`.
- Accept shape: direct field reads and writes, and a static helper called with the parameter.
- Reject: `Vibranium.Tests.Round11AuditSelfTests.ConstructorThisRecovery_DoesNotInventDefiniteAssignmentFailures` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_2_CtorThis_Accept` [analyzer, stage0, self]

### R2.3-pctor-supported — primary constructors and captured parameters are supported
- Quote (§2-zero): Primary Constructors & Captured <param>P Fields
- Quote (§2-zero): are supported. Captured parameters lower to real MIR instance fields
- Kind: runtime
- Accept shape: `class Counter(int start) { public int Next() => start + 1; }` and `readonly struct Scale(int factor)` running identically managed and native.
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_3_PctorSupported_Accept` [analyzer, stage0, self]

### R2.3-pctor-move-out — moving a captured !Copy parameter out of this is VIB005
- Quote (§2-zero): move-out-of-this rejection (VIB005)
- Expect: VIB005
- Reject shape: `class Holder(Vec<int> items) { public Vec<int> Take() => items; }`
- Accept shape: `public int Count() => items.Count;`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_3_PctorMoveOut_Accept` [analyzer, stage0, self]

### R2.3-pctor-double-move — capturing a parameter that a field initializer already moved is VIB001
- Quote (§2-zero): double-move detection across field initializers + capture (VIB001)
- Expect: VIB001
- Reject shape: `class H(Vec<int> v) { public Vec<int> A = v; public int N() => v.Count; }`
- Accept shape: a !Copy parameter used only by a field initializer next to a Copy parameter used only by a capture (a Copy parameter used by both raises the C# warning CS9124, so it cannot be a zero-warning accept witness).
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_3_PctorDoubleMove_Accept` [analyzer, stage0, self]

### R2.3-pctor-immutable — captured parameters are immutable bindings
- Quote (§2-zero): immutable parameter binding (VIB004 on direct reassignment/ref/out
- Expect: VIB004
- Reject shape: `class C(int x) { [Mut] public void F() { x = 2; } }`
- Accept shape: reading `x`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_3_PctorImmutable_Accept` [analyzer, stage0, self]

### R2.3-pctor-receiver — interior mutation of a captured parameter follows [Mut]/readonly receivers
- Quote (§2-zero): [Mut]/readonly receiver enforcement on interior mutations
- Expect: VIB004
- Reject shape: an unmarked method of `class Holder(Vec<int> items)` calling `items.Push(1)`.
- Accept shape: the same call in a `[Mut]` method.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_3_PctorReceiver_Accept` [analyzer, stage0, self]

### R2.3-pctor-linear — a captured linear parameter is VIB006
- Quote (§2-zero): linear IDisposable prohibition (VIB006)
- Expect: VIB006
- Reject shape: `class Holder(R r) { public int Id() => r.Id; }` for `class R : IDisposable`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_3_PctorLinear_Accept` [analyzer, stage0, self]

### R2.4-code-style — IDE0161, IDE0290, IDE0211 and IDE0063 are build errors
- Quote (§2-zero): enforce file-scoped namespaces (IDE0161), primary constructors (IDE0290), top-level statements (IDE0211), and simple using var declarations (IDE0063) as build errors
- Kind: prose
- Reason: SDK code-style enforcement only. CONFORMANCE §7.8 decided that IDE rules are style, not language validity; `vib` and `vib-self` do not check them, so there is no frontend verdict to witness.

## §2 Linear IDisposable and L1 fallible unions (VIB006)

### R2-lin-struct-idisposable — only classes may implement IDisposable
- Quote (§2-linear): Only class types may implement IDisposable (struct : IDisposable is rejected by VIB000/VIB006).
- Quote (§1-subset): struct S : IDisposable is banned by VIB000/VIB006
- Expect: VIB000 or VIB006
- Reject shape: `struct S : IDisposable { public void Dispose() {} }`
- Accept shape: `sealed class R : IDisposable`.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.UnsupportedStructResource_UnacquiredStorageIsNotVacuouslyInitialized` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinStructIdisposable_Accept` [analyzer, stage0, self]

### R2-lin-l1-wrappers — a linear R may instantiate Option, Some, Ok and Result
- Quote (§2-linear): L1 Fallible Linear Wrappers (Option<R> & Result<R, E>):
- Quote (§2-linear): may instantiate Option<R>, Some<R>, Ok<R>, and Result<R, E>
- Kind: runtime
- Accept shape: `Option<R>` and `Result<R, int>` built with `Some`/`Ok`/`Move` and consumed, Dispose order identical.
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinL1Wrappers_Accept` [analyzer, stage0, self]

### R2-lin-result-error-copy — in Result<R, E>, E must be Copy and non-linear
- Quote (§2-linear): In Result<R, E>, E must be Copy (and non-linear; a linear resource may never appear as the error type E) (VIB006).
- Expect: VIB006
- Reject shape: `Result<R, Vec<int>>`, `Result<int, R>`.
- Accept shape: `Result<R, int>`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinResultErrorCopy_Accept` [analyzer, stage0, self]

### R2-lin-generic-instantiation — Vec<R>, user generic types and methods with a linear type argument are VIB006
- Quote (§2-linear): Instantiating any other generic type (Vec<R>, user Box<R>) or any user generic method with a linear type argument (explicitly or via type inference) is rejected (VIB006).
- Quote (§2-generics): Instantiating any user generic type or user generic method with a linear IDisposable or L1 linear union type argument is rejected (VIB006).
- Expect: VIB006
- Reject shape: `Box<R>`, `Id<R>(r)` and `Id(r)` (inferred).
- Accept shape: `Prelude.Move(r)` and `Some(r)`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinGenericInstantiation_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinGenericInstantiation_Reject_InferredMethod` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumStdAndL1Tests.M2_L1_Rejects_LinearInErr_Vec_And_NonCopyErr_WithVib006` [stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinGenericInstantiation_Accept` [analyzer, stage0, self]

### R2-lin-union-linear — Option<R> and Result<R, E> are linear owners
- Quote (§2-linear): Option<R> and Result<R, E> are themselves linear (!Copy) owners:
- Kind: prose
- Reason: Heading for R2-lin-union-consume and R2-lin-union-banned-*.

### R2-lin-union-consume — linear unions must be consumed on every path
- Quote (§2-linear): They must be consumed on every normal control-flow path
- Quote (§2-linear): (VIB006 if unconsumed, discarded, or overwritten while live)
- Expect: VIB006
- Reject shape: an `Option<R>` local left unconsumed on one path; `_ = Open();`; overwriting a live `Option<R>`.
- Accept shape: consumption through `IsNone`, `IsErr`, `Unwrap`, `Move(opt) switch`, passing and returning.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionConsume_Accept` [analyzer, stage0, self]

### R2-lin-union-field — storing a linear union in a field or collection is VIB006
- Quote (§2-linear): Banned Operations on Linear Unions (VIB006): Storing in fields or arrays/collections
- Expect: VIB006
- Reject shape: `class H(Option<R> r) { public Option<R> Slot = r; }`
- Accept shape: an `Option<Node>` field (non-linear payload).
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionField_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionField_Accept` [analyzer, stage0, self]

### R2-lin-union-unwrapor — UnwrapOr on a linear union is VIB006
- Quote (§2-linear): .UnwrapOr(...)
- Expect: VIB006
- Accept shape: `UnwrapOr` on `Option<int>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionUnwrapor_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_UnwrapOr_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumStdAndL1Tests.M2_L1_Rejects_UnwrapOr_Take_Replace_AsRef_AsMut_UnwrapMut_And_NestedPattern_WithVib006` [stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionUnwrapor_Accept` [analyzer, stage0, self]

### R2-lin-union-slot-ops — AsRef, AsMut, Take, Replace, UnwrapMut and OptionMut on a linear union are VIB006
- Quote (§2-linear): OptionExtensions.{AsRef, AsMut, Take, Replace}, .UnwrapMut(), OptionMut.*
- Expect: VIB006
- Accept shape: the same operations on `Option<Node>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionSlotOps_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumStdAndL1Tests.M2_L1_Rejects_UnwrapOr_Take_Replace_AsRef_AsMut_UnwrapMut_And_NestedPattern_WithVib006` [stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionSlotOps_Accept` [analyzer, stage0, self]

### R2-lin-union-place-match — matching a linear union in place is VIB006
- Quote (§2-linear): in-place borrow pattern matching (PlaceScrutinee such as if (opt is Some<R>(var r)) without Move(opt))
- Quote (§3.4): rejected by VIB006 on linear Option<R> / Result<R, E>
- Expect: VIB006
- Reject shape: `if (opt is Some<R>(var r)) ...` without `Move`.
- Accept shape: `Move(opt) switch { Some<R>(var r) => ..., _ => ... }` consuming the resource.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionPlaceMatch_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round6_NegativeDiagnosticsParity` [stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionPlaceMatch_Accept` [analyzer, stage0, self]

### R2-lin-union-nested-pattern — nested subpatterns on a linear union are VIB006
- Quote (§2-linear): nested subpatterns (Some<R>({ ... }) / Ok<R>({ ... }))
- Expect: VIB006
- Accept shape: a nested subpattern on `Option<Node>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionNestedPattern_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round6_NegativeDiagnosticsParity` [stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionNestedPattern_Accept` [analyzer, stage0, self]

### R2-lin-union-switch-arms — a switch that consumes a linear union tests each variant in one unguarded arm, without `not` or `or`
- Quote (§2-linear): A switch statement or expression that consumes a linear union rvalue (Open(...) switch { ... }, switch (Move(opt))) may test each variant in at most one arm, with no when guard on a variant arm and no not or or pattern (VIB006 at the arm)
- Quote (§2-linear): _, default, var x and a guarded _ arm stay allowed, so write one arm per variant and branch inside it
- Quote (§3.4): On a linear Option<R> / Result<R, E>, each variant is tested by at most one arm, without a when guard, and not / or patterns are banned
- Expect: VIB006
- Reject shape: `Open(p) switch { Some<R>(var v) when p > 5 => ..., Some<R>(var v) => ..., NoneType => 7 }`, `case NoneType when p < 2:` on `switch (Move(o))`, `case not Ok<R>:`, `case NoneType or Some<R>:`.
- Accept shape: one unguarded arm per variant plus `_` / `default`, `case var u:`, a guarded `_` arm after the variant arms, and branching inside the arm (`Ok<R>(var v) => p > 5 ? 100 + Eat(v) : Eat(v)`), ASan/LSan clean with each resource disposed exactly once.
- Reject: `Vibranium.Tests.Round12RuleWitnessLinearSwitchTests.R2_LinUnionSwitchArms_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12LinearUnionSwitchBanTests.BannedArm_IsExactlyVib006AtEachMarkedArmOnAllThree_NeedsSelfFixW` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessLinearSwitchTests.R2_LinUnionSwitchArms_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12LinearUnionSwitchBanTests.Accepted_StaysAcceptedAndRunsTheSameNatively` [analyzer, stage0, self]
- Note: Finding N20. Each offending arm gets one VIB006 at the first token of its pattern, with the same message on all three frontends. A place scrutinee gets no arm diagnostic; R2-lin-union-place-match already rejects it. A variant arm with a nested subpattern keeps only its R2-lin-union-nested-pattern VIB006 but still counts as its variant's arm. The reject witnesses need both halves of the fix: the Roslyn ban (Analyzer and Stage-0) and self-fix W (vib-self).

### R2-lin-union-deconstruct — direct .Deconstruct on a linear union is VIB006
- Quote (§2-linear): and direct .Deconstruct(...) calls.
- Expect: VIB006
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionDeconstruct_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumStdAndL1Tests.M2_L1_Rejects_DirectDeconstruct_And_PlaceScrutineeMatch_WithVib006` [stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinUnionDeconstruct_Accept` [analyzer, stage0, self]

### R2-lin-exact-once — header of the exact-once disposal rules
- Quote (§2-linear): Exact-Once Disposal:
- Kind: prose
- Reason: Heading of R2-lin-manual-dispose, R2-lin-ref-alias, R2-lin-using and R2-lin-local-consume.

### R2-lin-manual-dispose — manual .Dispose() calls are VIB006
- Quote (§2-linear): Manual .Dispose() calls
- Expect: VIB006
- Reject shape: `r.Dispose();`
- Accept shape: `using var r = new R();`
- Reject: `Vibranium.Tests.Round10SourceValidityTests.RuntimeStructInitialization_LinearFieldsRemainForbidden` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinManualDispose_Accept` [analyzer, stage0, self]

### R2-lin-ref-alias — ref/out aliasing of linear values is VIB006; in borrows are allowed
- Quote (§2-linear): ref/out aliasing of linear IDisposable or L1 linear union types (in R shared borrows of a live IDisposable are allowed) are banned (VIB006)
- Expect: VIB006
- Reject shape: `static void F(ref R r) {}` called as `F(ref r)`.
- Accept shape: `static int F(in R r) => r.Id;`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_LinRefAlias_Accept` [analyzer, stage0, self]

### R2-lin-using — using binds a non-movable resource disposed in LIFO order
- Quote (§2-linear): binds r as non-movable (VIB006) and disposes r deterministically in reverse lexical (LIFO) declaration order on every normal scope exit
- Expect: VIB006
- Reject shape: `using var r = new R(1); Sink(r);`
- Accept shape: two `using var` resources and an early `return`, Dispose order printed identically.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.VibraniumEndToEndTests.Round11_AuditStd_AppendRegularFile_PreservesContentsAcrossHandles` [analyzer, stage0, self]

### R2-lin-local-consume — non-using linear locals and parameters must be moved on every path
- Quote (§2-linear): Non-using linear locals and parameters must be consumed on every normal control-flow path by move
- Expect: VIB006
- Reject shape: `var r = new R(1); if (c) Sink(r);`
- Accept shape: `if (c) Sink(r); else { using var d = r; }`
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_DivergentPartialCoverageRetainsLinearExit` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10SourceValidityTests.OwnedStructInitialization_WholeConstructionAndInitializedWritesRemainValid` [analyzer, stage0, self]

## §2 Generics and monomorphization

### R2-gen-constraints — interface, base-class, new() and allows ref struct constraints are VIB000
- Quote (§2-generics): Interface constraints, base-class constraints, new() constraints, and user allows ref struct constraints are rejected (VIB000).
- Quote (§2-generics): where T : class, where T : struct, and where T : unmanaged on user generic declarations
- Expect: VIB000
- Reject shape: `where T : new()`.
- Accept shape: `where T : unmanaged`, `where T : struct`, `where T : class`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_GenConstraints_Accept` [analyzer, stage0, self]

### R2-gen-affine-definition — an unconstrained T is affine at the definition
- Quote (§2-generics): A generic type parameter T is checked at the generic definition as affine (!Copy), non-zero-safe, and non-linear unless constrained with where T : unmanaged.
- Expect: VIB005
- Reject shape: `static T Dup<T>(in T x) => x;`
- Accept shape: `static T Dup<T>(in T x) where T : unmanaged => x;`
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_GenAffineDefinition_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_GenAffineDefinition_Accept` [analyzer, stage0, self]

### R2-gen-unmanaged-nonzero — where T : unmanaged grants Copy but stays non-zero-safe
- Quote (§2-generics): Adding where T : unmanaged grants Copy, while keeping T non-zero-safe
- Expect: VIB008
- Reject shape: `static T Zero<T>() where T : unmanaged => default;`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_GenUnmanagedNonzero_Accept` [analyzer, stage0, self]

### R2-gen-polymorphic-recursion — vib monomorphizes and rejects polymorphic recursion
- Quote (§2-generics): monomorphizes all reachable generic types and methods into concrete specializations and rejects polymorphic recursion
- Expect: VIB000
- Reject shape: `static int F<T>(T x, int n) => n == 0 ? 0 : F(new Wrap<T>(x), n - 1);`
- Accept shape: bounded generic recursion `F<T>` calling `F<T>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_GenPolymorphicRecursion_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_GenPolymorphicRecursion_Accept` [analyzer, stage0, self]

## §3 Borrowing, mutability and pattern matching

### R3-rust-mapping — the Rust-to-Vibranium mapping table is informative
- Quote (§3): let b = a; (move) | var b = a; (move)
- Quote (§3): Box::new(Node { val: 1, next: None }) | new Node(1, None)
- Quote (§3): vec![1, 2, 3], &mut v[1..3] | Vec<int> v = [1, 2, 3];, Span<int> s = v[1..3];
- Quote (§3): b"HTTP/1.1 200 OK\r\n" | "HTTP/1.1 200 OK\r\n"u8
- Quote (§3): fn f(x: &T, y: &mut T) | static void F(in T x, ref T y)
- Quote (§3): let r = &mut v[0]; | ref var r = ref v[0];
- Quote (§3): for x in &mut v { *x += 1; } | foreach (ref var x in v) x += 1;
- Quote (§3): &self / &mut self (class vs struct) | class: unmarked / [Mut]; struct: readonly / unmarked
- Quote (§3): match opt { Some(x) => x, None => 0 } | opt switch { Some<int>(var x) => x, NoneType => 0 }
- Quote (§3): let mut cur = &head; while let Some(n) = cur { cur = &n.next; }
- Quote (§3): while let Some(n) = cur { cur = &mut n.next; }
- Quote (§3): *cur = Some(x);, n.next.take()
- Quote (§3): let f = File::open(p)?;
- Quote (§3): Drop on OS resource
- Kind: prose
- Reason: Informative translation table; each row restates a rule witnessed elsewhere (R1.3-affine-move, R3.2-*, R3.3-*, R2-lin-*).

### R3.1-class-receiver — unmarked class members borrow this shared; [Mut] members exclusively
- Quote (§3.1): Unmarked class methods, getters, and setters borrow this as shared (&self)
- Expect: VIB004
- Reject shape: an unmarked class method (or setter) assigning a field.
- Accept shape: the same assignment in a `[Mut]` method and a `[Mut] set` accessor.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_ClassReceiver_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_ClassReceiver_Accept` [analyzer, stage0, self]

### R3.1-mut-property — [Mut] on a property or indexer makes set exclusive and keeps get shared
- Quote (§3.1): [Mut] on a property or indexer propagates exclusively to set while keeping get shared unless [Mut] get is placed on the getter itself
- Expect: VIB004
- Reject shape: a `[Mut]` property whose plain `get` mutates a field.
- Accept shape: a `[Mut]` property whose `set` writes a field, read through `get` on an `in`-like shared path.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_MutProperty_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round2_Gatekeeper_CrossUnitStdInternal_And_BorrowReservations_AnalyzerParity` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_MutProperty_Accept` [analyzer, stage0, self]

### R3.1-struct-receiver — readonly struct members borrow this shared, others exclusively
- Quote (§3.1): readonly struct methods/getters borrow this as shared; non-readonly struct methods, set accessors, and instance void operator +=(T) borrow this exclusively. Constructors initialize this mutably.
- Expect: VIB004
- Reject shape: a non-readonly struct method called on an `in` parameter (R3.1-defensive-copy).
- Accept shape: a mutating struct method on a local and `+=` through an instance operator.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_StructReceiver_Accept` [analyzer, stage0, self]

### R3.1-defensive-copy — non-readonly struct calls and writes on a shared place are VIB004
- Quote (§3.1): Calling any non-readonly struct method, property accessor, Vec.Slice, Vec.AsSpan, +=, or write/ref indexer on a shared place
- Quote (§3.1): is rejected with VIB004 (preventing silent C# struct defensive-copy lost writes)
- Expect: VIB004
- Reject shape: `static void F(in Vec<int> v) { v.Push(1); }`, `v.AsSpan()` on an `in Vec<int>`.
- Accept shape: `v[0]` value reads and `foreach (var x in v)` on an `in Vec<int>`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_DefensiveCopy_Accept` [analyzer, stage0, self]

### R3.1-deep-immutability — unmarked class and readonly struct bodies are deeply immutable
- Quote (§3.1): uphold deep transitive immutability on this (VIB004)
- Expect: VIB004
- Reject shape: a `readonly struct` method writing `Inner.W = 1` through a class field; an unmarked class method mutating a nested class field.
- Accept shape: the same write in a `[Mut]` class method.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_1_DeepImmutability_Accept` [analyzer, stage0, self]

### R3.2-span-copy-move — ReadOnlySpan<T> is Copy, Span<T> is !Copy
- Quote (§3.2): ReadOnlySpan<T> is Copy. Span<T> is !Copy (var s2 = s1; moves s1)
- Expect: VIB001
- Reject shape: `Span<int> t = s; s[0] = 1;`
- Accept shape: `ReadOnlySpan<int> t = r;` with both read.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanCopyMove_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanCopyMove_Accept` [analyzer, stage0, self]

### R3.2-span-reborrow — passing, slicing, indexing and iterating a Span<T> reborrows and freezes it
- Quote (§3.2): Vibranium implicitly reborrows s instead of moving it: parent s is frozen while the derived borrow is live (VIB003)
- Expect: VIB003
- Reject shape: `Span<int> t = s[0..2]; s[0] = 9; t[0] = 1;`
- Accept shape: the same with `t` dead before the write to `s`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanReborrow_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round11AuditSelfTests.IndependentSpanOperands_ReleaseTheirParentReborrowsAfterInvocation` [analyzer, stage0, self]

### R3.2-span-overlap — overlapping exclusive span borrows in one call are VIB002
- Quote (§3.2): passing overlapping exclusive borrows in one call (Foo(s, s), Foo(s[0..2], s[1..3])) is rejected by VIB002
- Expect: VIB002
- Reject shape: `Foo(s, s)`.
- Accept shape: `Foo(s, t)` with two different owners.
- Reject: `Vibranium.Tests.Round11AuditSelfTests.CapturedSpanOperands_ActivateTogetherWithoutSpuriousConflict` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditSelfTests.IndependentSpanOperands_ReleaseTheirParentReborrowsAfterInvocation` [analyzer, stage0, self]

### R3.2-span-explicit-move — Move(s) moves a span without reborrowing
- Quote (§3.2): Explicit Move(s) moves s without reborrowing.
- Expect: VIB001
- Reject shape: `Consume(Move(s)); s[0] = 1;`
- Accept shape: `Consume(Move(s));` with `s` not used again.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanExplicitMove_Reject_DirectUse` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round11SourceValidityTests.ForeachExplicitMoveConsumesItsCollection` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanExplicitMove_Accept_DirectMove` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.ForeachSharedReborrowAndOrdinaryNllControls` [analyzer, stage0, self]

### R3.2-assignment-value — using an assignment's value moves a !Copy destination
- Quote (§3.2): The value of an assignment expression is a copy or move of its destination, so using that value ((d = e).Length, F(d = e), a = d = e) moves a !Copy destination d, Span<T> included, and a later use of d is VIB001 (int n = (s = []).Length; WriteLine(s.Length);); a Copy destination such as a ReadOnlySpan<T> stays usable.
- Expect: VIB001, exactly, on every frontend
- Reject shape: `int n = (v = MakeVec(7)).Count; WriteLine(v.Count);`, `Count(v = MakeVec(7))`, `a = b = MakeVec(7);` then `b.Count`, and a class destination, each with a later use of the destination.
- Accept shape: the same without a later use of the destination (or after reassigning it); a `ReadOnlySpan<int>` destination read again; a `Span<int>` destination not used again.
- Reject: `Vibranium.Tests.Round12RuleWitnessAssignmentValueTests.R3_2_AssignmentValue_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSelfFixTests.R3_2_AssignmentValue_Reject_Span` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessAssignmentValueTests.R3_2_AssignmentValue_Accept` [analyzer, stage0, self]
- Note: The `Span<T>` reject witness is `Span<int> s = a.AsSpan(); int n = (s = []).Length; WriteLine(s.Length);`. With a view source (`(s = b.AsSpan()).Length`, or `Len(s = b.AsSpan())`) and a later use of `s`, the Analyzer and Stage-0 also report a secondary VIB003 ("Cannot read 'b' while exclusively borrowed by 's'") at the assignment.
- See also: R3.2-span-copy-move, R3.2-span-explicit-move.

### R3.2-span-element-copy — Span<T> and ReadOnlySpan<T> elements must be Copy
- Quote (§3.2): Span<T> and ReadOnlySpan<T> elements must be Copy: any declaration or expression of a span type with a !Copy element type (including Vec<T>.AsSpan() of a !Copy T) is VIB000 ("Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2.").
- Expect: VIB000 with the quoted message (decision F6)
- Reject shape: `Span<Node> s = [new Node()];`, a `ReadOnlySpan<T>` parameter of a !Copy `T`, and `Vec<Node>.AsSpan()`.
- Accept shape: `Span<int>` and a `ReadOnlySpan<P>` of a Copy struct `P`.
- Reject: `Vibranium.Tests.Round12CutMatrixTests.EveryNonCopySpanDeclarationAndExpressionIsRejected` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12CutSelfTests.RuleLedgerSpanElementsRequireCopy` [self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanElementCopy_Reject_SpanLocal` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanElementCopy_Reject_ReadOnlySpanParameter` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_SpanElementCopy_Accept` [analyzer, stage0, self]

### R3.2-index-shared-read — value-read indexing needs only a shared borrow
- Quote (§3.2): in value-read position require only a shared borrow (valid on in Vec<T> and ReadOnlySpan<T>)
- Expect: VIB004 (the near-miss: a write through the same `in` place)
- Reject shape: `static void F(in Vec<int> v) { v[0] = 1; }`
- Accept shape: `static int F(in Vec<int> v) => v[0] + v[^1];`
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_IndexSharedRead_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_IndexSharedRead_Accept` [analyzer, stage0, self]

### R3.2-index-exclusive-write — writes, compound writes and ref/out taking need an exclusive borrow
- Quote (§3.2): Direct writes (v[i] = x), compound assignments (v[i] += x), and ref/out taking (ref var r = ref v[i];, F(ref v[i]), F(out v[i])) require an exclusive borrow (VIB004 on shared places)
- Expect: VIB004
- Reject shape: `ref int r = ref v[0];` on an `in Vec<int>`.
- Accept shape: the same on a local `Vec<int>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_IndexExclusiveWrite_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_IndexExclusiveWrite_Accept` [analyzer, stage0, self]

### R3.2-range-slice — v[a..b] on Vec<T> is an exclusive Slice; shared places use ReadOnlySlice
- Quote (§3.2): Range slicing v[a..b] on Vec<T> lowers to v.Slice(...) (exclusive -> Span<T>); shared places slice via v.ReadOnlySlice(start, len) or v.AsReadOnlySpan()[a..b].
- Expect: VIB004
- Reject shape: `v[1..2]` on an `in Vec<int>`.
- Accept shape: `v.ReadOnlySlice(1, 1)` and `v.AsReadOnlySpan()[1..2]` on an `in Vec<int>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_RangeSlice_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_RangeSlice_Accept` [analyzer, stage0, self]

### R3.2-implicit-index-range — x[^k] and x[a..b] lower through Length/Count, this[int] and Slice
- Quote (§3.2): On any struct or class exposing int Length or int Count, x[^k] lowers to x[len - k] (via this[int]) and x[a..b] lowers to x.Slice(start, end - start) (via Slice(int, int)), evaluating x once and reading Length/Count at most once
- Kind: runtime
- Accept shape: `StringBuf[^1]`, `StringBuf[..]` and a user type with `Count`, `this[int]` and `Slice(int, int)` evaluated once, managed and native equal.
- Accept: `Vibranium.Tests.Round11Stage0Tests.UpdateTargets_PreserveOldValueAndSingleEvaluation` [analyzer, stage0, self]

### R3.2-index-range-values — standalone System.Index and System.Range variables are VIB000
- Quote (§3.2): standalone System.Index / System.Range variables remain VIB000
- Expect: VIB000
- Reject shape: `Index i = ^1;`, `Range r = 1..2;`
- Accept shape: `v[^1]`, `s[1..2]` inline.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_IndexRangeValues_Reject_IndexLocal` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_IndexRangeValues_Reject_RangeLocal` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11ParserTests.Round11_RangeOperands_KeepExplicitArithmeticGrouping` [analyzer, stage0, self]

### R3.2-two-phase — receivers and targets are reserved before arguments; shared reads may intervene
- Quote (§3.2): Mutable receivers and ref arguments are reserved before argument evaluation and activated at the store or invocation.
- Quote (§3.2): Between reservation and activation, shared reads of the same root (v[i] = v[i - 1] + fib, v.Push(v.Count)) are allowed
- Expect: VIB003
- Reject shape: `v.Push(v.Pop().Unwrap());`
- Accept shape: `v.Push(v.Count);` and `v[i] = v[i - 1] + 1;`
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ReachableOwnershipViolationsRemainRejected` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11CallActivationMatrixTests.NllTwoPhaseReborrowsAndPlainSlotReplacementStayLegal` [analyzer, stage0, self]

### R3.2-receiver-first — receivers and assignment targets are evaluated strictly before arguments
- Quote (§3.2): evaluate their receiver/target place strictly before arguments/RHS in C# left-to-right order
- Kind: runtime
- Accept shape: receiver and argument expressions that print, in the same order managed and native.
- Accept: `Vibranium.Tests.Round11Stage0Tests.UpdateTargets_PreserveOldValueAndSingleEvaluation` [analyzer, stage0, self]

### R3.2-foreach-vec — foreach over a Vec holds a shared loan (var) or an exclusive loan (ref var)
- Quote (§3.2): foreach (var x in v) holds a shared loan on v across the entire loop body (valid on in Vec<T>)
- Quote (§3.2): foreach (ref var x in v) holds an exclusive loan on v across the entire loop body
- Expect: VIB003 (mutation inside the loop); VIB004 (`ref var` loop over a shared place)
- Reject shape: `foreach (var x in v) v.Push(x);`
- Accept shape: `foreach (var x in v)` over an `in Vec<int>`, and `foreach (ref var x in v) x += 1;` over a local.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_ForeachVec_Reject_MutationInLoop` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_ForeachVec_Reject_RefLoopOverSharedPlace` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_2_ForeachVec_Accept` [analyzer, stage0, self]

### R3.2-foreach-span — foreach over a Span<T> is a shared (var) or exclusive (ref var) reborrow
- Quote (§3.2): The same holds for foreach (var x in s) over a Span<T>: it is a shared reborrow of s
- Expect: VIB003
- Reject shape: `foreach (var x in s) s[0] = x;` and `foreach (ref var x in s) WriteLine(s.Length);`
- Accept shape: `foreach (var x in s) WriteLine(s.Length + s[0]);` and `s` used after the loop.
- Reject: `Vibranium.Tests.Round11SourceValidityTests.ForeachLoanRemainsLiveThroughAllBodyExits` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.ForeachSharedReborrowAndOrdinaryNllControls` [analyzer, stage0, self]

### R3.2-foreach-loan-extent — the enumeration loan covers break, return and divergence and ends at loop exit
- Quote (§3.2): The hidden enumeration view's loan remains live throughout the foreach body, including paths ending in break, return, or divergence
- Expect: VIB003
- Reject shape: a mutation of `v` on a path that then `break`s out of `foreach (var x in v)`.
- Accept shape: `v.Push(1)` after the loop.
- Reject: `Vibranium.Tests.Round11SourceValidityTests.ForeachLoanRemainsLiveThroughAllBodyExits` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.ForeachSharedReborrowAndOrdinaryNllControls` [analyzer, stage0, self]

### R3.3-call-activation — earlier operand loans live through the call; moving the place later is VIB003
- Quote (§3.3): Moving a place that equals, prefixes or extends a place under such a loan while evaluating a later operand is VIB003, reported once at the moving operand
- Quote (§3.3): A call's operands (receiver or assignment target place first, then arguments in source order) are evaluated left to right
- Expect: VIB003
- Reject shape: `Eat(v.AsReadOnlySpan(), v)`, `Eat(ref n, n)`, `b.Absorb(b)`.
- Accept shape: the same calls with an unrelated owner as the moved argument.
- Reject: `Vibranium.Tests.Round11AuditSelfTests.NestedSpanCall_StillConflictsWithEnclosingLiveOperand` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11CallActivationMatrixTests.EveryUnrelatedReceiverIndexerAndOperatorControlExecutes` [analyzer, stage0, self]

### R3.3-plain-assignment — an assignment to a plain local creates no loan
- Quote (§3.3): An assignment to a plain local or parameter creates no loan, so v = F(v) stays valid.
- Expect: VIB001 (the near-miss: `s.F = Move(s)` on a plain struct local keeps its moved-struct diagnostic)
- Reject shape: `s.F = Take(s);` for a plain struct local.
- Accept shape: `v = F(v);` and `v = Move(v);`
- Reject: `Vibranium.Tests.Round11CallActivationMatrixTests.PlainStructFieldStoreRetainsItsExistingMovedStructDiagnostic` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11CallActivationMatrixTests.NllTwoPhaseReborrowsAndPlainSlotReplacementStayLegal` [analyzer, stage0, self]

### R3.3-reserved-target — a place-evaluated assignment target reserves its base
- Quote (§3.3): creates a reserved exclusive loan on that base, so moving the base or any prefix or extension of it on the right-hand side is VIB003
- Expect: VIB003
- Reject shape: `h.N = Move(h);` with `h` a class; `r = Move(v);` with `ref var r = ref v`.
- Accept shape: `h.N = Make();`
- Reject: `Vibranium.Tests.Round8RegressionTests.Round8_Rejections_PreserveAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_ReservedTarget_Accept` [analyzer, stage0, self]

### R3.3-span-call-reborrow — a view returned from a call on a span is a reborrow of that span
- Quote (§3.3): A view returned from a call on a span s is a reborrow of s, not a fresh borrow of its owner.
- Expect: VIB003 (the near-miss: using the parent span while the child is live)
- Reject shape: `Span<int> t = First(s); WriteLine(s.Length); WriteLine(t[0]);`
- Accept shape: `Span<int> t = First(s); t[0] = 4; WriteLine(s[0]);`
- Reject: `Vibranium.Tests.Round11CallActivationMatrixTests.NestedInvocationAndLiveReturnedChildStillFreezeTheirParent` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11CallActivationMatrixTests.NllTwoPhaseReborrowsAndPlainSlotReplacementStayLegal` [analyzer, stage0, self]

### R3.3-exclusive-set — overlapping borrows in one call conflict when one is exclusive
- Quote (§3.3): two receiver/argument borrows with overlapping root provenance conflict under VIB002 whenever at least one belongs to the exclusive set
- Expect: VIB002
- Reject shape: `F(out x, out x)`, `F(ref x, out x)`, `F(out x, in x)`, `F(s, s)`.
- Accept shape: `F(in x, in x)` (R3.3-shared-alias).
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ReachableOwnershipViolationsRemainRejected` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_SharedAlias_Accept` [analyzer, stage0, self]

### R3.3-shared-alias — multiple shared borrows may alias freely
- Quote (§3.3): Multiple shared borrows (in, ReadOnlySpan<T>, OptionRef<T>, unmarked class receiver, readonly struct receiver) may alias freely.
- Expect: VIB002 (the near-miss: one exclusive borrow added)
- Reject shape: `G(ref x, in x)`.
- Accept shape: `F(in x, in x)` and `H(v.AsReadOnlySpan(), v.AsReadOnlySpan())`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_SharedAlias_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_SharedAlias_Accept` [analyzer, stage0, self]

### R3.3-slot-cursor-header — header of the slot-cursor rules
- Quote (§3.3): Slot-Cursor Field & Freezing Rules:
- Kind: prose
- Reason: Heading of R3.3-slot-fields, R3.3-optionmut-asref-freeze and R3.3-cursor-provenance.

### R3.3-slot-fields — AsMut, Take and Replace need mutable fields, not properties (CS0206)
- Quote (§3.3): are this ref Option<T> extensions and therefore require mutable fields (public Option<Node> Next;), not C# properties (CS0206)
- Expect: CS0206 (SDK, Stage-0); VIB000 from vib-self
- Reject shape: `h.Next.Take()` where `Next` is a property.
- Accept shape: the same on a field.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_SlotFields_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_SlotFields_Accept` [analyzer, stage0, self]

### R3.3-asref-storage-location — Option.AsRef needs a receiver with a storage location, not a temporary
- Quote (§3.3): .AsRef() is a this in Option<T> extension whose receiver must be a storage location, not a temporary: Make().AsRef() is rejected (VIB000), while o.AsRef() on a local o stays valid.
- Expect: VIB000 on every frontend (decision F12)
- Reject shape: `MakeNode().AsRef()` and `OptionExtensions.AsRef(MakeNode())` on a temporary `Option<Node>`.
- Accept shape: `o.AsRef()` and `OptionExtensions.AsRef(o)` on a local `Option<Node> o`, read through a pattern.
- Reject: `Vibranium.Tests.Round12RuleWitnessAsRefTests.R3_3_AsRefStorageLocation_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessAsRefTests.R3_3_AsRefStorageLocation_Reject_StaticForm` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessAsRefTests.R3_3_AsRefStorageLocation_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessAsRefTests.R3_3_AsRefStorageLocation_Accept_StaticForm` [analyzer, stage0, self]

### R3.3-optionmut-asref-freeze — OptionMut.AsRef() freezes the parent OptionMut while the OptionRef lives
- Quote (§3.3): OptionMut<T>.AsRef() creates a shared OptionRef<T> reborrow that freezes the parent OptionMut<T>
- Expect: VIB003
- Reject shape: `var r = m.AsRef(); m.Set(None); Use(r);`
- Accept shape: `m.Set(None)` after the last use of `r`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_OptionMutAsRefFreeze_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_OptionMutAsRefFreeze_Accept` [analyzer, stage0, self]

### R3.3-cursor-provenance — cursor step-reborrows keep root provenance
- Quote (§3.3): Polonius-style per-point loan provenance tracks cursor step-reborrows
- Expect: VIB003 (stale `n`), VIB007 (local heap-field escape)
- Reject shape: reading `n` after `cur = n.Next.AsMut()` and a write through `cur`.
- Accept shape: list `Append` and mid-list `RemoveFirst` through an `OptionMut<Node>` cursor.
- Reject: `Vibranium.Tests.Round8RegressionTests.Round8_Rejections_PreserveAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_CursorProvenance_Accept` [analyzer, stage0, self]

### R3.3-lifetimes-header — header of the lifetime rules
- Quote (§3.3): Lifetimes & [LifetimeOf] (VIB007, VIB000):
- Kind: prose
- Reason: Heading of R3.3-return-escape, R3.3-elision and the [LifetimeOf] rules.

### R3.3-return-escape — a returned borrow must not escape a local or by-value owner
- Quote (§3.3): must never escape a local variable or by-value parameter, including heap-field projections of local/by-value owners (VIB007)
- Expect: VIB007 for heap-field projections of by-value owners (C#-valid, every frontend); for a view of a local, C# reports CS8166/CS8168/CS8352 (SDK, Stage-0) and vib-self reports VIB007
- Reject shape: returning `v.AsSpan()` of a local `Vec<int>`; returning a view of a by-value parameter's field.
- Accept shape: returning a view of an `in`/`ref` parameter.
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_U8Static_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round11AuditSelfTests.ReturnedViews_PreserveParameterContractsAndTemporaryEscape` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_ReturnEscape_Accept` [analyzer, stage0, self]

### R3.3-temporary-borrow — a named variable cannot receive a borrow rooted at a temporary
- Quote (§3.3): Temporaries (VIB007): A temporary (a call result, conversion, collection expression, Move(...), or conditional or switch expression that is not stored in a variable) can be borrowed only within the expression that creates it, including the payload bindings of the patterns that expression feeds
- Quote (§3.3): A named variable (a local, ref local, foreach iteration variable or is var binding) cannot receive a reference or view whose provenance includes a temporary
- Quote (§3.3): Bind the owner to a local first (var v = MakeVec(7); Span<int> s = v.AsSpan();).
- Expect: VIB007, exactly, on every frontend (every reject program is C#-valid)
- Reject shape: `Span<int> s = MakeVec(7).AsSpan();`, `var r = Peek(MakeNode(7));`, `ref readonly Vec<int> r = ref Id(MakeVec(7));`, `Peek(MakeNode(7)) is var r`, `ReadOnlySpan<byte> k = MakeMap().KeyAt(0);`.
- Accept shape: the same borrows used within the creating expression (`WriteLine(MakeVec(7).AsSpan()[1]);`) or through pattern payload bindings (`is Some<Node>(var n)`, also `is not … return`); the rejected programs with the owner bound to a local first.
- Reject: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryBorrow_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryBorrow_Accept_SameExpression` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryBorrow_Accept_OwnerLocal` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSelfFixTests.R3_3_TemporaryBorrow_Reject_InParameterConversion` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSelfFixTests.R3_3_TemporaryBorrow_Reject_CollectionToInParameter` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSelfFixTests.R3_3_TemporaryBorrow_Accept_InParameterConversion` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSelfFixTests.R3_3_TemporaryBorrow_Accept_CollectionToInParameter` [analyzer, stage0, self]
- Note: The rule was enforced by every compiler (`BorrowAndMoveAnalyzer.CheckDanglingRvalueTempLoans`, vib-self `OwnershipChecker.CheckTempRvalueEscape`, message "Borrowed reference or view … cannot outlive temporary rvalue.") before SPEC stated it (views-of-temporaries probe, finding P2).
- See also: R3.3-temporary-return, R3.3-temporary-foreach, R3.3-asref-storage-location.

### R3.3-temporary-return — a function cannot return a borrow rooted at a temporary
- Quote (§3.3): and a function cannot return one: both are VIB007 (Span<int> s = MakeVec(7).AsSpan();, ref readonly Vec<int> r = ref Id(MakeVec(7));).
- Expect: VIB007, exactly, on every frontend (the reject programs are C#-valid; returning a view of a temporary `Vec` directly is CS8156, see R3.3-return-escape)
- Reject shape: `static Span<int> Bad() => MakeHolder(7).Items.AsSpan();`; `static ref int Bad() => ref MakeBox(7).First();`.
- Accept shape: `static ReadOnlySpan<int> View(in Vec<int> v) => v.AsReadOnlySpan();` called as `View(MakeVec(7))[0]`.
- Reject: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryReturn_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryReturn_Accept` [analyzer, stage0, self]
- See also: R3.3-return-escape.

### R3.3-temporary-foreach — a foreach iteration variable into a temporary is VIB007 in v0.2
- Quote (§3.3): As a consequence, a foreach whose iteration variable is a reference or view into a temporary (foreach (var line in MakeBuf(t).Lines()), foreach (ref var x in MakeVec(7))) is VIB007 in v0.2, while a by-value loop over the Copy elements of a temporary (foreach (var x in MakeVec(7))) is valid.
- Expect: VIB007, exactly, on every frontend
- Reject shape: `foreach (var line in MakeBuf("a\nbb\nccc").Lines())`; `foreach (ref var x in MakeVec(7))`.
- Accept shape: the same loops over a local owner; `foreach (var x in MakeVec(7))` over `Copy` elements.
- Reject: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryForeach_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessTemporaryTests.R3_3_TemporaryForeach_Accept` [analyzer, stage0, self]
- Note: Main decided that these loops stay VIB007 in v0.2 (views-of-temporaries probe, finding P3), although the temporary outlives the loop natively. `Env.Args().Unwrap()` and `….AsUtf8().Lines()` loops over a temporary are rejected the same way.
- See also: R2-utf8-views-closed, R3.2-foreach-vec.

### R3.3-u8-return — u8 literals may always be returned
- Quote (§3.3): "..."u8 literals have 'static provenance and may always be returned.
- Expect: VIB007 (the near-miss: R3.3-return-escape)
- Accept shape: `static ReadOnlySpan<byte> Name(int k) => k == 0 ? "zero"u8 : "other"u8;`
- Reject: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_U8Static_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessTypeTests.R2_U8Static_Accept` [analyzer, stage0, self]

### R3.3-return-provenance — elided returns tie provenance to this or the sole borrowed input
- Quote (§3.3): Unannotated borrow-returning functions tie returned provenance to borrowed this when the receiver is borrowed, or else to the sole borrowed input parameter
- Expect: VIB003 at the call site (mutating the input while the returned view lives); VIB007 at the return site when the returned view does not derive from the tied input
- Reject shape: `Span<int> t = First(v.AsSpan()); v.Push(1); t[0] = 1;` and the receiver form `var s = h.View(); h.Mutate(); s[0]`.
- Accept shape: the same with the mutation after the last use of the returned view.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round11AuditSelfTests.ReturnedViews_KeepValidExplicitAndElidedOrigins` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSelfFixTests.R3_3_ReturnProvenance_Accept_StructThisClassField` [analyzer, stage0, self]
- Note: This is the rule that was missing from vib-self for ten rounds (strategy review).

### R3.3-elision-ambiguous — zero or several borrowed inputs need [return: LifetimeOf]
- Quote (§3.3): Functions with zero borrowed inputs (returning non-static borrows) or multiple borrowed inputs (and no borrowed this) require [return: LifetimeOf("p1", ...)] (VIB007).
- Expect: VIB007
- Reject shape: `static ReadOnlySpan<int> Pick(ReadOnlySpan<int> a, ReadOnlySpan<int> b, bool f) => f ? a : b;`
- Accept shape: the same with `[return: LifetimeOf("a", "b")]`.
- Reject: `Vibranium.Tests.Round11AuditSelfTests.ReturnedViews_PreserveParameterContractsAndTemporaryEscape` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_ElisionAmbiguous_Accept` [analyzer, stage0, self]

### R3.3-scoped — scoped parameters never contribute return provenance
- Quote (§3.3): scoped parameters never contribute return provenance.
- Expect: CS8352 (SDK, Stage-0); VIB007 from vib-self
- Reject shape: returning a view of a `scoped` parameter.
- Accept shape: a `scoped` parameter next to a returned one.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_Scoped_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.PanicSourceValidityTests.PanicSourceValidity_InvalidCSharp_IsRejectedByEveryFrontend` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_Scoped_Accept` [analyzer, stage0, self]

### R3.3-lifetimeof-verify — return sites must stay within the named parameters; call sites union them
- Quote (§3.3): VIB007 checks every return site to ensure returned loan provenance is a subset of the named borrowed parameters ("this" permitted) and propagates that union of loans at call sites.
- Expect: VIB007 (return site), VIB003 (call site)
- Reject shape: `[return: LifetimeOf("a")]` returning `b`; mutating `b`'s owner while the result of `LifetimeOf("a", "b")` lives.
- Accept shape: mutating an owner not named in `LifetimeOf`.
- Reject: `Vibranium.Tests.Round11AuditSelfTests.ReturnedViews_PreserveParameterContractsAndTemporaryEscape` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditSelfTests.ReturnedViews_KeepValidExplicitAndElidedOrigins` [analyzer, stage0, self]

### R3.3-lifetimeof-placement — [LifetimeOf] on parameters or user types is VIB000
- Quote (§3.3): Applying [LifetimeOf] to parameters or user types is banned (VIB000).
- Expect: VIB000
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_LifetimeOfPlacement_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_LifetimeOfPlacement_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_ElisionAmbiguous_Accept` [analyzer, stage0, self]

### R3.3-view-ref-param — ref/out parameters of borrowed-view type are VIB000
- Quote (§3.3): ref/out parameters whose type is a borrowed view
- Expect: VIB000
- Reject shape: `static void F(ref Span<int> s) {}`
- Accept shape: `static void F(Span<int> s) {}`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_ViewRefParam_Accept` [analyzer, stage0, self]

### R3.4-union-boxing — object/IUnion and { } patterns on unions are VIB000; var x binds the union without boxing
- Quote (§3.4): object, IUnion, or reference-widening patterns on [Union] types (opt is object x) that lower via .Value and heap-box are rejected (VIB000).
- Quote (§1-subset): including object / IUnion / reference-widening patterns on [Union] types that lower through .Value
- Quote (§3.4): var x binds the union struct directly without boxing; { } x on a union type is not supported (VIB000).
- Expect: VIB000
- Reject shape: `opt is object x` and `opt is { } y` (decision F7).
- Accept shape: `opt is var x`.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_UnionBoxing_Reject_ObjectPattern` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_UnionBoxing_Reject_EmptyPropertyPattern` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_UnionBoxing_Accept` [analyzer, stage0, self]

### R3.4-case-pattern — case patterns are positional; property patterns are not supported (C24)
- Quote (§3.4): Property patterns are not supported in Vibranium v0.2; Option and Result cases are matched with the positional form.
- Expect: VIB000 at the first case-pattern property's opening brace per file (including an empty clause, an aliased case and a nested case).
- Reject shape: `o is Some<int> { Value: 7 }`; `Some<int>(7) { } => 1`.
- Accept shape: `o is Some<int>(int x)`; `o is Some<Option<int>>(Some<int>(var n))`.
- Reject: `Vibranium.Tests.Round12UnionPayloadPatternTests.C24_RejectsAtFirstCaseBraceInEachFile` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12UnionPayloadPatternTests.TypedAndNestedPositionalPayloadsRemainValid` [analyzer, stage0, self]
- N/A reject [self]: Self-host C24 enforcement is deferred until the self cut lands.
- Note: Self-host C24 enforcement is deferred; the exact current verdicts are in the follow-up replay table. Named positional elements and extension deconstruction on union cases are not supported (`VIB000`).
- Reject: `Vibranium.Tests.Round12UnionPayloadPatternTests.NamedPositionalElement_Rejects` [analyzer, stage0]
- Reject: `Vibranium.Tests.Round12UnionPayloadPatternTests.ExtensionDeconstruction_RejectsWithoutAnUninitializedBindingCascade` [analyzer, stage0]

### R3.4-rvalue-scrutinee — rvalue scrutinees are consumed and consuming bindings own their payloads
- Quote (§3.4): Rvalue Scrutinees (Call(), opt.Take(), Move(x)): Consume the scrutinee
- Quote (§3.4): and when its arm has no when guard and is immediately followed by an unguarded arm for the other variant, with no arm after that one testing or binding the scrutinee again.
- Expect: VIB001 (use of the scrutinee after `Move(x) switch`); VIB006 (a linear variant dropped by `_`); VIB003 (a view of an owned binding that outlives the binding's scope)
- Reject shape: `var n = Move(opt) switch { ... }; Use(opt);`
- Accept shape: `Move(opt) switch { Some<Node>(var n) => Keep(n), _ => 0 }`, ASan/LSan clean.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_RvalueScrutinee_Reject_UseAfterMove` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_RvalueScrutinee_Reject_LinearDiscarded` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIsNotRvalueTests.R3_4_RvalueScrutinee_Reject_NegatedPatternViewOutlivesBinding` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_RvalueScrutinee_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12PositionalOwnershipTests.ComplementWithoutRetestTransfersOwnership` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12PositionalOwnershipTests.RefutableBindingBeforeLaterRetestRemainsBorrowed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIsNotRvalueTests.R3_4_RvalueScrutinee_Accept_NegatedPatternBinding` [analyzer, stage0, self]
- Note: The negated-pattern witnesses (`if (Make() is not Some<T>(var x)) return;` on an rvalue) show that the binding owns its payload: it may be moved, mutated and viewed, and it is dropped at the end of its scope (views-of-temporaries probe, finding P1-4).

### R3.4-place-scrutinee — place scrutinees are shared borrows
- Quote (§3.4): Place Scrutinees (Option<T>, Result<T, E>) & OptionRef<T>: Borrow the scrutinee as shared
- Quote (§3.4): Copy payloads bind as independent copies.
- Expect: VIB004 (mutating a !Copy binding)
- Accept shape: `if (opt is Some<int>(var x)) { x = x + 1; }` leaving `opt` unchanged.
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_PlaceScrutinee_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_PlaceBinding_Accept` [analyzer, stage0, self]

### R3.4-place-binding — !Copy place bindings are shared: no mutation (VIB004) and no move (VIB005)
- Quote (§3.4): unconditionally bind as shared/immutable borrows
- Expect: VIB004 (reassignment, field writes, [Mut] and non-readonly calls, ref/out); VIB005 (move)
- Reject shape: `if (opt is Some<Vec<int>>(var v)) v.Push(1);` and `Sink(v)`.
- Accept shape: `if (opt is Some<Vec<int>>(var v)) WriteLine(v.Count);` and mutation through `ref var v = ref opt.UnwrapMut();`
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_PlaceBinding_Reject_Move` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_PlaceScrutinee_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_PlaceBinding_Accept` [analyzer, stage0, self]

### R3.4-optionmut-binding — OptionMut pattern bindings allow heap writes but no move, reassignment or ref
- Quote (§3.4): OptionMut<T> where T : class Pattern Bindings (Some<Node>(var n)):
- Quote (§3.4): Allow heap field writes (n.Val = 1) and [Mut] / ref-field calls
- Quote (§3.4): Forbid moving n (VIB005), reassigning n = ... (VIB004)
- Expect: VIB005 (move), VIB004 (reassignment, ref/out)
- Reject shape: `if (m is Some<Node>(var n)) Bump(ref n);`
- Accept shape: `if (m is Some<Node>(var n)) { n.Val = 1; n.Add(1); }`
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_OptionMutBinding_Reject_RefArgument` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_OptionMutBinding_Reject_Move` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_4_OptionMutBinding_Accept` [analyzer, stage0, self]

## §3.5 Fail-closed Core and Span intrinsic contract

### R3.5-fail-closed-members — Core and BCL members outside the whitelist are VIB000
- Quote (§3.5): Every Vibranium.Core or BCL member not listed in the table below
- Expect: VIB000
- Reject shape: `s.CopyTo(t)`, `s.Fill(0)`, `x.ToString()`, `x.GetHashCode()`.
- Accept shape: whitelisted members (`s.Length`, `s.Slice(1)`, `v.Count`).
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round3_Gatekeeper_BorrowConflicts_And_SpecCompliance_AnalyzerParity` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_FailClosedMembers_Accept` [analyzer, stage0, self]

### R3.5-prelude — Prelude factories consume their arguments; None and Unit are Copy
- Quote (§3.5): Some(T), Ok(), Ok(T), Err(E), None, Move(T) (allows ref struct)
- Quote (§3.5): Consumes T/E (Copy copies; Ok() produces Ok<Unit>); None and Unit are zero-safe Copy.
- Expect: VIB001 (use of a !Copy argument after `Some(x)`)
- Reject shape: `var o = Some(n); Use(n);` for a class `n`.
- Accept shape: `Ok()` as `Result<Unit, int>`, `Some(1)` copies.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Prelude_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Prelude_Accept` [analyzer, stage0, self]

### R3.5-wrapper-value — Some/Ok/Err .Value and .Error consume the receiver
- Quote (§3.5): Direct .Value / .Error consumes receiver (VIB005 if non-movable !Copy
- Expect: VIB005
- Reject shape: `h.Slot.Value` where `h.Slot` is a `Some<Node>` field.
- Accept shape: `new Some<int>(3).Value`.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_WrapperValue_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_WrapperValue_Accept` [analyzer, stage0, self]

### R3.5-option-conversions — Option and Result are built from their wrappers
- Quote (§3.5): Option(Some<T>), Option(NoneType) (implicit/explicit)
- Quote (§3.5): Result(Ok<T>), Result(Err<E>) (implicit/explicit), .IsOk
- Quote (§3.5): Constructors consume wrapper (VIB006 if T is linear and E is !Copy, or if E is linear); .IsOk takes shared borrow.
- Kind: runtime
- Accept shape: implicit and explicit conversions from `Some`/`None`/`Ok`/`Err`, and `IsOk`/`IsSome`/`IsEmpty` reads, managed and native equal.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_OptionConversions_Accept` [analyzer, stage0, self]

### R3.5-option-queries — IsSome and IsEmpty take a shared borrow
- Quote (§3.5): .IsSome, .IsEmpty | shared borrow of receiver.
- Quote (§3.5): .IsSome, .IsEmpty, pattern Some<T> / NoneType | shared borrow; !Copy pattern binding is shared borrow with OptionRef provenance.
- Kind: runtime
- Accept shape: `opt.IsSome` on an `in Option<Node>` parameter.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_OptionQueries_Accept` [analyzer, stage0, self]

### R3.5-guard-only — IsNone and IsErr are valid only as an if guard with fresh out declarations
- Quote (§3.5): valid only in if ([!]opt.IsNone(out var val)) guard (VIB000 elsewhere)
- Quote (§3.5): valid only in if ([!]res.IsErr(out var ok, out var err)) guard (VIB000 elsewhere)
- Quote (§3.5): Must appear directly as an if (guard) or if (!guard) condition with fresh inline out var or out _ declarations (VIB000 otherwise)
- Expect: VIB000
- Reject shape: `bool b = opt.IsNone(out var v);` and `if (opt.IsNone(out existing))`.
- Accept shape: `if (!opt.IsNone(out var v)) WriteLine(v);`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round3_Gatekeeper_BorrowConflicts_And_SpecCompliance_AnalyzerParity` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round3_NegativeDiagnostics_And_Stage0Parity` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_GuardOnly_Accept` [analyzer, stage0, self]

### R3.5-unwrap — Unwrap consumes the receiver; on a !Copy place it is VIB005
- Quote (§3.5): .Unwrap() | Consumes receiver (Copy copies; !Copy/linear moves local or VIB005 on place).
- Expect: VIB005
- Reject shape: `h.Next.Unwrap()` on an `Option<Node>` field.
- Accept shape: `h.Next.Take().Unwrap()`.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Unwrap_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Unwrap_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]

### R3.5-unwrapor — UnwrapOr consumes both sides and drops the unselected fallback
- Quote (§3.5): Consumes both receiver and eager fallback; drops unselected !Copy fallback on Some (VIB006 if T is linear).
- Quote (§3.5): Consumes receiver + eager fallback; drops !Copy fallback on Ok, or drops !Copy E on Err (VIB006 if T is linear).
- Expect: VIB006 (linear T, see R2-lin-union-unwrapor)
- Accept shape: `Some(v1).UnwrapOr(v2)` for `Vec<int>` values, LSan clean.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_UnwrapOr_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumStdAndL1Tests.M2_L1_Rejects_UnwrapOr_Take_Replace_AsRef_AsMut_UnwrapMut_And_NestedPattern_WithVib006` [stage0]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_UnwrapOr_Accept` [analyzer, stage0, self]

### R3.5-unwrapmut — UnwrapMut returns ref T tied to the receiver loan
- Quote (§3.5): exclusive borrow of receiver -> returns ref T with receiver loan provenance (VIB006 if T is linear).
- Expect: VIB003 (using the slot while the returned ref lives)
- Reject shape: `ref var v = ref opt.UnwrapMut(); opt = None; v.Push(1);`
- Accept shape: `ref var v = ref opt.UnwrapMut(); v.Push(1);`
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_UnwrapMut_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_UnwrapMut_Accept` [analyzer, stage0, self]

### R3.5-slot-extensions — AsRef, AsMut, Take and Replace on Option slots
- Quote (§3.5): shared borrow of Option<T> place -> OptionRef<T> tied to slot (VIB006 if T is linear).
- Quote (§3.5): exclusive borrow of mutable Option<T> field/local -> OptionMut<T> tied to slot (VIB006 if T is linear).
- Quote (§3.5): exclusive borrow of mutable Option<T> field/local -> returns owned Option<T>, writes None (no drop; VIB006 if T is linear).
- Quote (§3.5): exclusive two-phase borrow of slot, consumes v -> returns prior owned Option<T> (no drop; VIB006 if T is linear).
- Expect: VIB003 (using the slot while an `AsMut` cursor lives)
- Reject shape: `var m = slot.AsMut(); slot = None; m.Set(None);`
- Accept shape: `Take` leaves `None`, `Replace` returns the prior value, ASan/LSan clean.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_SlotExtensions_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_SlotExtensions_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]

### R3.5-optionmut-ops — OptionMut queries, AsRef, Set, Replace and Take
- Quote (§3.5): .IsSome, .IsEmpty, .AsRef() | shared borrow; .AsRef() freezes parent OptionMut<T> (VIB003) while OptionRef<T>/binding is live.
- Quote (§3.5): exclusive on OptionMut<T>; Set drops overwritten Some; Replace/Take return prior Option<T> un-dropped (VIB006 if T is linear).
- Kind: runtime
- Accept shape: `m.Set(Some(n))` dropping the old node and `m.Take()` returning it, ASan/LSan clean.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_OptionMutOps_Accept` [analyzer, stage0, self]

### R3.5-vec-construct — Vec construction allocates lazily
- Quote (§3.5): new Vec<T>(), new Vec<T>(int cap), [e1, ...] (VecBuilder.Create)
- Quote (§3.5): Returns owned Vec<T> ([] and new Vec<T>() allocate 0 bytes).
- Kind: runtime
- Accept shape: `[]`, `new Vec<int>()`, `new Vec<int>(8)` and `[1, 2, 3]` with `Count`/`Capacity` printed.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecConstruct_Accept` [analyzer, stage0, self]

### R3.5-vec-shared — Count, Capacity, AsReadOnlySpan and ReadOnlySlice take a shared borrow
- Quote (§3.5): .Count, .Capacity, .AsReadOnlySpan(), .ReadOnlySlice(int, int)
- Expect: VIB003 (mutating the Vec while a returned ReadOnlySpan lives)
- Reject shape: `var r = v.AsReadOnlySpan(); v.Push(1); WriteLine(r[0]);`
- Accept shape: all four on an `in Vec<int>`.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecShared_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecShared_Accept` [analyzer, stage0, self]

### R3.5-vec-exclusive — Push, +=, Pop, Clear, Truncate, Reserve, Resize need an exclusive borrow
- Quote (§3.5): .Push(T), +=, .Pop(), .Clear()
- Quote (§3.5): exclusive borrow (Push/+= two-phase reservable; Clear resets Count to 0).
- Quote (§3.5): .Truncate(int length)
- Quote (§3.5): .Resize(int length, T fill)
- Expect: VIB004
- Reject shape: `v.Clear()` on an `in Vec<int>`.
- Accept shape: each operation on a local with `Count` printed.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecExclusive_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecExclusive_Accept` [analyzer, stage0, self]

### R3.5-vec-extend-self — Extend from the same Vec is VIB003
- Quote (§3.5): .Reserve(int additional), .Extend(ReadOnlySpan<T> items)
- Quote (§3.5): Extend rejects items borrowing the same Vec under VIB003
- Expect: VIB003
- Reject shape: `v.Extend(v.AsReadOnlySpan());`
- Accept shape: `v.Extend(w.AsReadOnlySpan());`
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecExtendSelf_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecExtendSelf_Accept` [analyzer, stage0, self]

### R3.5-vec-index — Vec indexing is shared on read and exclusive on write
- Quote (§3.5): v[int i], v[Index] | shared on value-read; exclusive (two-phase reservable on =/+=) on write/+=/ref/out.
- Kind: runtime
- Accept shape: `v[0] = v[1] + v[^1];` printed identically.
- Accept: `Vibranium.Tests.Round10SourceValidityTests.RuntimeReferenceSnapshots_OnlyStableStructPathsAffectInitialization` [analyzer, stage0, self]

### R3.5-vec-slice — Slice, ranges and AsSpan return a Span tied to an exclusive Vec loan
- Quote (§3.5): .Slice(int, int), v[Range], .AsSpan() | exclusive borrow -> Span<T> tied to Vec loan.
- Expect: VIB003
- Reject shape: `Span<int> s = v.AsSpan(); WriteLine(v.Count); s[0] = 1;`
- Accept shape: `Span<int> s = v[1..3]; s[0] = 9;` then `v` read after the span's last use.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecSlice_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_VecSlice_Accept` [analyzer, stage0, self]

### R3.5-vec-foreach — foreach over a Vec holds the loan across the body
- Quote (§3.5): foreach (var x in v) / foreach (ref var x in v) | shared (var) or exclusive (ref var) loan held across entire loop body.
- Kind: prose
- Reason: Restates R3.2-foreach-vec, which carries the witnesses.

### R3.5-span-members — Span/ReadOnlySpan members reborrow and freeze the parent
- Quote (§3.5): .Length, .IsEmpty, s[int], s[Index], .Slice(int), .Slice(int, int), s[Range], implicit Span<T> -> ReadOnlySpan<T>, foreach
- Quote (§3.5): Span<T> implicitly reborrows (shared on read/ReadOnlySpan conversion/var loop; exclusive on write/+=/ref/Span slice/ref var loop), freezing parent Span<T> (VIB003).
- Expect: VIB003
- Reject shape: `ReadOnlySpan<int> r = s; s[0] = 1; WriteLine(r[0]);`
- Accept shape: `ReadOnlySpan<int> r = s; WriteLine(r[0]); s[0] = 1;`
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_SpanMembers_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_SpanMembers_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.ForeachSharedReborrowAndOrdinaryNllControls` [analyzer, stage0, self]

### R3.5-expect — Expect consumes the receiver (linear included) and panics with the message
- Quote (§3.5): .Expect(string message)
- Quote (§3.5): Consumes receiver (including L1 linear Option<R> / Result<R, E>); panics with exact <message> on None/Err.
- Kind: runtime
- Accept shape: `Open().Expect("no file")` on a linear `Option<R>` that is `Some`, and a `None` that panics with `panic: no file`, exit 101.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Expect_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_OptionExpect_Accept` [analyzer, stage0, self]

### R3.5-unwrap-or-default — UnwrapOrDefault needs a zero-safe non-linear payload
- Quote (§3.5): .UnwrapOrDefault()
- Quote (§3.5): Consumes non-linear receiver (VIB006 if linear); requires IsZeroSafe(T) (VIB008 otherwise); returns payload or zeroed default(T).
- Expect: VIB008 (non-zero-safe T), VIB006 (linear)
- Reject shape: `Option<Node> o = None; var n = o.UnwrapOrDefault();`
- Accept shape: `Option<int> o = None; WriteLine(o.UnwrapOrDefault());`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_UnwrapOrDefault_Accept` [analyzer, stage0, self]

### R3.5-vec-add — Vec.Add is an exact alias of Push
- Quote (§3.5): .Add(T item) | Exact BCL alias for .Push(T) (two-phase reservable exclusive borrow).
- Kind: runtime
- Accept shape: `v.Add(v.Count);` printed identically.
- Accept: `Vibranium.Tests.Round11CallActivationMatrixTests.NllTwoPhaseReborrowsAndPlainSlotReplacementStayLegal` [analyzer, stage0, self]

### R3.5-utf8-extensions — AsUtf8, Lines and span Lines
- Quote (§3.5): text.AsUtf8(), text.Lines()
- Quote (§3.5): span.Lines() (ReadOnlySpan<byte> or Span<byte>)
- Quote (§3.5): shared borrow (reborrows Span<byte> as shared) -> Utf8Lines view tied to span owner.
- Kind: prose
- Reason: Restates R2-asutf8-static, R2-utf8-views-closed and R2-lines-loan, which carry the witnesses.

### R3.5-stdio — Stdio, Prelude and Io writes emit exact bytes
- Quote (§3.5): Stdio.WriteOut(ReadOnlySpan<byte> \| string), Stdio.WriteErr(ReadOnlySpan<byte> \| string)
- Quote (§3.5): Write(ReadOnlySpan<byte>), WriteLine(ReadOnlySpan<byte>)
- Kind: runtime
- Accept shape: `Stdio.WriteOut`, `Stdio.WriteErr`, `Write` and `WriteLine` of byte spans and strings, stdout and stderr compared byte for byte.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Stdio_Accept` [analyzer, stage0, self]

### R3.5-write-formatting — Write/WriteLine of integers and bool use invariant formatting
- Quote (§3.5): invariant ASCII decimal for signed/unsigned integers, "True"/"False" for bool, UTF-8 string, '\n'.
- Kind: runtime
- Accept shape: `WriteLine` of `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong` extremes and `bool`.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_WriteFormatting_Accept` [analyzer, stage0, self]

### R3.5-bcl-aliases — MemoryExtensions and int/long.TryParse alias to Std (VIB000 under --no-std)
- Quote (§3.5): Aliased when Vibranium.Std is referenced (VIB000 under --no-std) to ByteSpanExtensions.* and Bytes.TryParseInt32/TryParseInt64.
- Expect: VIB000 under `--no-std`
- Reject shape: `int.TryParse("1", out var n)` compiled without Std.
- Accept shape: the same with Std, plus `SequenceEqual`/`StartsWith`/`IndexOf`.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.CoreTierA_NegativeDiagnostics_And_AnalyzerCliParity` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NoStd_RejectsBclIntTryParse_MemoryExtensions_And_ConsoleIo` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_BclAliases_Accept` [analyzer, stage0, self]

### R3.5-panic — VibraniumRuntime.Panic diverges with the panic contract
- Quote (§3.5): Reads string; diverges ([DoesNotReturn]), flushes stdout, writes UTF-8 panic to stderr, exits 101.
- Kind: runtime
- Accept shape: `Write("x"); VibraniumRuntime.Panic("boom");` gives stdout `x`, stderr `panic: boom\n`, exit 101.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Panic_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_PanicContract_Accept` [analyzer, stage0, self]

### R3.5-consuming-intrinsics — consuming intrinsics copy Copy receivers and move owned locals; places are VIB005
- Quote (§3.5): On Copy receivers, consuming intrinsics copy freely (including through in or fields).
- Quote (§3.5): owned locals are marked moved (VIB001 on reuse; outer scope drop suppressed), while fields, indexed elements, and borrowed places are rejected with VIB005
- Expect: VIB001 (reuse of a moved local), VIB005 (field or borrowed place)
- Reject shape: `var n = o.Unwrap(); var m = o.Unwrap();` for `Option<Node> o`.
- Accept shape: `Unwrap()` on an `in Option<int>` parameter and on an `Option<int>` field.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_ConsumingIntrinsics_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_ConsumingIntrinsics_Accept` [analyzer, stage0, self]

### R3.5-guard-bindings — IsErr/IsNone bindings are initialized only on their active branch
- Quote (§3.5): err is initialized only when IsErr is true, and ok is initialized only when IsErr is false
- Quote (§3.5): opt.IsNone(out var val) obeys the identical rule (val initialized only when IsNone is false).
- Expect: VIB001
- Reject shape: `if (res.IsErr(out var ok, out var err)) WriteLine(ok);`
- Accept shape: `if (res.IsErr(out var ok, out var err)) WriteLine(err); else WriteLine(ok);`
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_MatchesAllStage0NegativeSafetyCases` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_GuardBindings_Accept` [analyzer, stage0, self]

### R3.5-guard-divergence — a diverging branch keeps the other branch's binding after the if
- Quote (§3.5): If one branch diverges (return, break, continue, Panic), the non-diverging branch's active binding remains initialized after the if
- Expect: VIB001
- Reject shape: `if (opt.IsNone(out var v)) WriteLine(0); WriteLine(v);`
- Accept shape: `if (opt.IsNone(out var v)) return; WriteLine(v);`
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_GuardDivergence_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_GuardDivergence_Accept` [analyzer, stage0, self]

## §3.6 Zero-C standard library and the Sys catalog

### R3.6-two-tier — header of the two-tier Std architecture
- Quote (§3.6): Two-Tier Architecture:
- Kind: prose
- Reason: Heading of R3.6-t1-verified and R3.6-native-catalog.

### R3.6-t1-verified — T1 Std modules pass the full gatekeeper and analyzer with no exemptions
- Quote (§3.6): Verified by the full SyntaxAndSymbolGatekeeper and BorrowAndMoveAnalyzer with zero special exemptions
- Kind: repo
- Evidence: `Vibranium.Tests.VibraniumStdAndL1Tests.M3_VibraniumStd_SelfCheck_ZeroDiagnostics_And_ZeroCFiles`

### R3.6-text-bytes — Utf8, ByteSpanExtensions, Bytes and StringBuf behave identically
- Quote (§3.6): Utf8 (strict RFC 3629 validation), ByteSpanExtensions
- Quote (§3.6): invariant BCL-exact TryParseInt32/TryParseInt64 accepting ws* [+-]? [0-9]+ ws* '\0'*
- Kind: runtime
- Accept shape: UTF-8 validation, byte search and trim, `TryParseInt32` edge inputs and `StringBuf` appends, managed and native equal.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_6_TextBytes_Accept` [analyzer, stage0, self]

### R3.6-console-io — ReadLine and TryReadLine stream discipline
- Quote (§3.6): Console I/O (ConsoleIo, imported via using static):
- Quote (§3.6): Ok(None) on clean EOF, strips \n or \r\n, preserves standalone \r
- Kind: runtime
- Accept shape: `ReadLine` over stdin with `\n`, `\r\n`, lone `\r`, invalid UTF-8 and an over-long line.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_6_ConsoleIo_Accept` [analyzer, stage0, self]
- Note: SPEC (candidate 5) still shows `ReadLine(int maxBytes = 1_048_576)` and `TryReadLine(ref StringBuf, int maxBytes = 1_048_576)`; Std now has overloads without optional parameters (cut C12).

### R3.6-collections — IntMap, StrMap and Interner behave identically
- Quote (§3.6): IntMap<V> where V : unmanaged, StrMap<V> where V : unmanaged
- Kind: runtime
- Accept shape: inserts, removals with tombstones, rehash and interning, managed and native equal.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_6_Collections_Accept` [analyzer, stage0, self]

### R3.6-filesystem — File, Fs and PathText behave identically
- Quote (§3.6): Filesystem & Errors:
- Quote (§3.6): linear File : IDisposable
- Kind: runtime
- Accept shape: create, append, read, one-shot APIs and error kinds, managed and native equal.
- Accept: `Vibranium.Tests.VibraniumEndToEndTests.Round11_AuditStd_AppendRegularFile_PreservesContentsAcrossHandles` [analyzer, stage0, self]

### R3.6-env-process-net — ArgList, Env, Command, Process and TCP behave identically
- Quote (§3.6): Environment, Process & Networking:
- Kind: runtime
- Accept shape: argument enumeration, process exit codes and loopback TCP, managed and native equal.
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_6_EnvProcessNet_Accept` [analyzer, stage0, self]
- Note: SPEC (candidate 5) still shows `Bind(…, int backlog = 128)`; Std now has overloads without optional parameters (cut C12).

### R3.6-native-catalog — [Native] is permitted only on the 17 Sys catalog methods
- Quote (§3.6): T0 Trusted Platform Leaves (Vibranium.Std.Sys — Hosted-ABI Catalog v2, 17 Leaves):
- Quote (§3.6): is permitted solely on the 17 static partial methods of Vibranium.Std.Sys in the official Vibranium.Std manifest
- Quote (§1-subset): [Native] outside the locked Vibranium.Std.Sys catalog.
- Expect: VIB000
- Reject shape: a user `[Native("puts")]` method, and a user `Vibranium.Std.Sys` spoof.
- Accept shape: a program that reaches a Sys leaf through Std (`File.Exists`).
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.NegativeCases_RoslynAnalyzer_And_VibFrontend_ProduceIdenticalDiagnosticIds` [analyzer, stage0]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_NegativeSafetyDiagnostics_ReportExpectedCode` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_6_NativeCatalog_Accept` [analyzer, stage0, self]

### R3.6-sys-abi — Sys return conventions, FileOpen modes and FileExists semantics
- Quote (§3.6): Return values >= 0 denote success/counts/handles, and < 0 encode -(int)IoErrorKind.
- Quote (§3.6): Sys.FileExists matches .NET 11 System.IO.File.Exists on Linux
- Kind: runtime
- Accept shape: `File.Exists` on files, directories, FIFOs, valid and dangling symlinks; open modes 0, 1, 2.
- Accept: `Vibranium.Tests.VibraniumEndToEndTests.Round11_AuditStd_Fifo_AllFileModesRespectNonSeekability` [analyzer, stage0, self]

### R3.6-sys-catalog — the 17 Sys leaves and their native symbols
- Quote (§3.6): Sys.FileOpen | @__vib_sys_file_open
- Quote (§3.6): Sys.FileRead | @__vib_sys_file_read
- Quote (§3.6): Sys.FileWriteAll | @__vib_sys_file_write_all
- Quote (§3.6): Sys.FileSync | @__vib_sys_file_sync
- Quote (§3.6): Sys.FileExists | @__vib_sys_file_exists
- Quote (§3.6): Sys.StdinRead | @__vib_sys_stdin_read
- Quote (§3.6): Sys.Close | @__vib_sys_close
- Quote (§3.6): Sys.TcpListen | @__vib_sys_tcp_listen
- Quote (§3.6): Sys.TcpAccept | @__vib_sys_tcp_accept
- Quote (§3.6): Sys.TcpConnect | @__vib_sys_tcp_connect
- Quote (§3.6): Sys.SockRecv | @__vib_sys_sock_recv
- Quote (§3.6): Sys.SockSendAll | @__vib_sys_sock_send_all
- Quote (§3.6): Sys.SockShutdownWrite | @__vib_sys_sock_shutdown_write
- Quote (§3.6): Sys.SockLocalPort | @__vib_sys_sock_local_port
- Quote (§3.6): Sys.SpawnWait | @__vib_sys_spawn_wait
- Quote (§3.6): Sys.ArgCount | @__vib_sys_arg_count
- Quote (§3.6): Sys.ArgCopy | @__vib_sys_arg_copy
- Kind: repo
- Evidence: `Vibranium.Tests.VibraniumStdAndL1Tests.M3_NativeAttribute_RejectedOutsideVibraniumStdSys_And_UnknownSysSymbolRejected`

## §3.7 Source validity and runtime control flow

### R3.7-three-analyses — the self-hosted frontend keeps three analyses distinct
- Quote (§3.7): The self-hosted frontend keeps three analyses distinct
- Kind: prose
- Reason: Architecture statement; its observable consequences are R3.7-endpoint-reachability, R3.7-definite-assignment and R3.7-runtime-reachability.

### R3.7-endpoint-reachability — a reachable switch-section endpoint without a terminator is invalid
- Quote (§3.7): Reachable section endpoints without break, return, or another permitted terminating statement are invalid (VIB000).
- Expect: CS0163 or CS8070 (SDK, Stage-0); VIB000 from vib-self
- Reject shape: `case 1: WriteLine(1); case 2: break;`
- Accept shape: every section ends in `break` or `return`.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.UnionStatement_RetainsSourceUnmatchedPath` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_EndpointReachability_Accept` [analyzer, stage0, self]

### R3.7-panic-continuation — a call to Panic has a source continuation
- Quote (§3.7): A call to VibraniumRuntime.Panic has a source continuation because its managed declaration is not a C# control-flow terminator.
- Expect: CS0161 (SDK, Stage-0); VIB000 from vib-self
- Reject shape: `static int F() { VibraniumRuntime.Panic("x"); }`
- Accept shape: the same followed by `return 0;`, which panics with exit 101 identically.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.UnionStatement_RetainsSourceUnmatchedPath` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_PanicContinuation_Accept` [analyzer, stage0, self]

### R3.7-definite-assignment — definite assignment is tracked per struct field, independently of moves
- Quote (§3.7): SourceValidityChecker tracks assignment, including individual struct fields, independently of moves and loans (VIB001).
- Expect: CS0165/CS0170/CS0177 (SDK, Stage-0); VIB001 from vib-self
- Reject shape: `P p; p.X = 1; WriteLine(p.Y);`
- Accept shape: `P p; p.X = 1; p.Y = 2; WriteLine(p.X + p.Y);`
- Reject: `Vibranium.Tests.Round10SourceValidityTests.RuntimeStructInitialization_PartialReadsRemainSourceInvalid` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11SourceValidityTests.SourcePatternControlsPreserveBindingsAndConstants` [analyzer, stage0, self]

### R3.7-switch-guard-state — switch dispatch snapshots the state; failed when guards do not assign later labels
- Quote (§3.7): Switch dispatch snapshots the state immediately after evaluating the selector.
- Expect: CS0165 (SDK, Stage-0); VIB001 from vib-self
- Reject shape: an assignment inside a failed `when` guard read in a later section.
- Accept shape: the assignment read inside the guarded section's own body.
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_IncompleteSourceAssignmentRemainsRejected` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]

### R3.7-union-switch-exit — a union switch statement keeps its unmatched exit; every arm is checked
- Quote (§3.7): a switch statement keeps its unmatched exit even when its labels name every variant, as in C# (CS0161, CS0165, CS0163)
- Quote (§3.7): every switch-expression arm body is checked, including a _ arm that follows complete variant arms
- Expect: CS0161 (SDK, Stage-0) and VIB000 from vib-self for the missing return; CS0165 (SDK, Stage-0) and VIB001 from vib-self for a read after the switch
- Reject shape: an `int` method whose only statement is a switch over `Option<int>` naming `Some` and `NoneType`.
- Accept shape: the same with a trailing `return`.
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round7_ExhaustiveReturnPaths_And_Reachability_Differential` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_UnionSwitchExit_Accept` [analyzer, stage0, self]

### R3.7-dispatch-bounds — dispatch reachability stops at section bodies
- Quote (§3.7): Dispatch reachability stops at section/arm bodies
- Kind: prose
- Reason: Implementation invariant of the self-hosted frontend; it is exercised through R3.7-switch-guard-state and R3.7-union-switch-exit and has no verdict of its own.

### R3.7-runtime-reachability — ownership checks run on runtime-reachable paths only
- Quote (§3.7): Runtime ownership and linearity: the ownership solver uses runtime MIR, where Panic diverges immediately.
- Quote (§3.7): Runtime-reachable paths exclude code that C# proves unreachable (CS0162)
- Expect: VIB001 (the reachable control)
- Reject shape: a use after move on a reachable path.
- Accept shape: a use after move after an unconditional `Panic`, and in a `default:` section that earlier cases cover.
- Reject: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ReachableOwnershipViolationsRemainRejected` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_RuntimeReachability_Accept` [analyzer, stage0, self]
- Note: SPEC's other examples (`when false`, `if (false)`) become cut forms under C19 (constant conditions and guards).

### R3.7-lexical-ref-safety — C# lexical ref/view safe contexts are checked as VIB007
- Quote (§3.7): The source-validity pass also checks C# lexical ref-/view-safe contexts (VIB007)
- Expect: CS8352 (SDK, Stage-0); VIB003 and VIB007 from vib-self
- Reject shape: storing a view of an inner-scope collection in an outer-scope local (narrower-scope rebinding).
- Accept shape: the same view used inside the inner scope.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_LexicalRefSafety_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.PanicSourceValidityTests.PanicSourceValidity_InvalidCSharp_IsRejectedByEveryFrontend` [self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_LexicalRefSafety_Accept` [analyzer, stage0, self]

### R3.7-collection-storage — collection expressions get their destination's storage
- Quote (§3.7): Span-typed collection expressions follow the C# safe-context rules.
- Quote (§3.7): returning one as a span (static ReadOnlySpan<int> G() => [1, 2];, and []) is rejected with VIB007 even where C# accepts it.
- Expect: VIB007 (returned), VIB003 (outliving its scope)
- Reject shape: `static ReadOnlySpan<int> G() => [1, 2];`
- Accept shape: a collection expression passed directly as an argument and used for the whole call.
- Reject: `Vibranium.Tests.Round10BindingTests.Round10_CollectionStorage_TransparentConversionPreservesNamedOwner` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessN16Tests.R3_7_CollectionStorage_Reject_ByValueViewParameter` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10BindingTests.Round10_CollectionStorage_TransparentConversionUsesActualDestination` [analyzer, stage0, self]
- Note: A collection expression assigned to a by-value `ReadOnlySpan<T>` parameter and then returned (`p = [1, 2]; return p;`, the branch, slice and `[]` forms, and `return p = [1, 2];`) is exactly VIB003 and VIB007 on every frontend (conformance-fuzz root N16).

### R3.7-switch-declaration-space — switch-section locals share the switch block's declaration space
- Quote (§3.7): direct switch-section locals share the switch-block declaration space
- Expect: CS0128 or CS0136 (SDK); Stage-0 and vib-self reject, reporting cut C18 (VIB000) first, a more specific Vibranium rule (§4)
- Reject shape: the same local name declared in two sections.
- Accept shape: a local declared in one section and assigned and used in a later one.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_SwitchDeclarationSpace_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round10ParserTests.Round10_SwitchSectionLocalFunction_InvalidScopeOrDuplicate_IsRejected` [analyzer]
- N/A accept [analyzer, stage0, self]: the only accepted near-miss would declare a local directly in one switch section and use it in a later one, which cut C18 bans, so no accepted program exercises the shared declaration space.
- Note: Cut C18 bans declarations directly in a switch section, so every witness program of this rule also contains a C18 form.

### R3.7-switch-using — a using declaration in a switch section needs a block
- Quote (§3.7): A using declaration in a switch section requires an explicit enclosing block, as in C#.
- Expect: CS8647 (SDK); Stage-0 and vib-self reject, reporting cut C18 (VIB000) first, a more specific Vibranium rule (§4)
- Reject shape: `case 1: using var r = new R(); break;`
- Accept shape: `case 1: { using var r = new R(); break; }`
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_SwitchUsing_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_7_SwitchUsing_Accept` [analyzer, stage0, self]

### R3.7-validation-scope — source analyses are validated by regressions, not by the Lean development
- Quote (§3.7): These implementation invariants are validated by source-diagnostic and managed/native differential regressions.
- Kind: prose
- Reason: Statement about evidence and the scope of the formal development.

## §4 Diagnostic catalogue and parity

### R4-catalog — the VIB000–VIB008 catalogue
- Quote (§4): Unsupported C# construct or unwhitelisted Vibranium.Core/BCL/[Native] member
- Quote (§4): Use of moved, consumed (Dispose()), or uninitialized value
- Quote (§4): Overlapping borrows in a single call where at least one is exclusive
- Quote (§4): Mutation, move, exclusive borrow, or scope exit of a place while an active borrow
- Quote (§4): Mutation, reassignment, ref/out aliasing, [Mut] call, or non-readonly struct call through a shared borrow
- Quote (§4): Illegal move or !Copy consuming call out of a field, property getter, indexed element, generic in T
- Quote (§4): Linear IDisposable / L1 linear union violation
- Quote (§4): Returned ref, Span, or borrowed view escapes a local or by-value owner
- Quote (§4): Forbidden null, nullable type annotation T?, null-forgiving postfix x!
- Kind: prose
- Reason: Definitions of the IDs. Every testable rule names the ID it expects, so each catalogue row is witnessed through those rules; R4-scope-exit-borrow and R4-moved-dispose cover the two catalogue clauses no other rule names.

### R4-scope-exit-borrow — scope exit of a place while a borrow is live is VIB003
- Quote (§4): or scope exit of a place while an active borrow
- Expect: VIB003 or VIB007
- Reject shape: a span of a block-local `Vec<int>` stored in an outer local and used after the block.
- Accept shape: the span used only inside the block.
- Reject: `Vibranium.Tests.Round10SourceValidityTests.StackAllocation_ActualBackingScopeInvalidatesLiveDerivedBorrow` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11AuditSelfTests.ConditionalArgumentOwnerTemporaries_LiveThroughBothSelectedArms` [analyzer, stage0, self]

### R4-moved-dispose — use of a resource after it was consumed is VIB001
- Quote (§4): Use of moved, consumed (Dispose()), or uninitialized value
- Expect: VIB001
- Reject shape: `Sink(r); WriteLine(r.Id);` for a linear `R`.
- Accept shape: `WriteLine(r.Id); Sink(r);`
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_MovedDispose_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_MovedDispose_Accept` [analyzer, stage0, self]

### R4-diagnostic-parity — the three frontends report the same IDs on programs without banned constructs
- Quote (§4): For every rejected C#-valid program without a subset-banned construct, Vibranium.Analyzer, vib, and the self-hosted compiler report the same set of diagnostic IDs.
- Expect: equal ID sets from the Analyzer, Stage-0 and self
- Reject shape: any non-banned rejected program checked on all three frontends.
- Accept shape: any accepted program checked on all three frontends.
- Reject: `Vibranium.Tests.Round8RegressionTests.Round8_Rejections_PreserveAnalyzerStage0SelfParity` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]

### R4-banned-fail-closed — a banned construct is rejected with its own ID; secondary diagnostics may differ
- Quote (§4): A program containing a construct this specification bans is rejected fail-closed
- Expect: the banned construct's ID from every frontend
- Reject shape: a program with a banned construct and a later independent finding.
- Reject: `Vibranium.Tests.VibraniumEndToEndTests.Round5TrackA_IDisposableInterfaceValueBanned_4WayParity` [analyzer, stage0, self]
- N/A accept [analyzer, stage0, self]: a statement about rejected programs; the accepted near-misses are those of the individual banned-construct rules.

### R4-csharp-validity — an accepted program is valid C# under the project settings and valid Vibranium
- Quote (§4): Every accepted program must be both valid C# under the project's language settings and references and valid Vibranium.
- Expect: every frontend rejects a C#-invalid program (R4-genuine-csharp-error, R1.1-promoted-*) and a C#-valid program that breaks a Vibranium rule (that rule's ID)
- Reject shape: a C# type error; a write through an `OptionMut` while its `AsRef()` reborrow lives, in otherwise valid C# (VIB003).
- Accept shape: the corrected programs.
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_CSharpValid_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_OptionMutAsRefFreeze_Reject` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_CSharpValid_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessBorrowTests.R3_3_OptionMutAsRefFreeze_Accept` [analyzer, stage0, self]

### R4-genuine-csharp-error — a genuine C# error is rejected even when it would not affect safety
- Quote (§4): A genuine C# error requires rejection, even if accepting the program would not affect safety.
- Expect: the CS error from the SDK and Stage-0; rejection by vib-self (IDs are not compared)
- Reject shape: a duplicate local (CS0128), a duplicate method (CS0111), a call with the wrong argument count (CS1501) and a duplicate type (CS0101).
- Accept shape: the corrected program.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_DuplicateLocal` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_DuplicateMethod` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_ArgumentCount` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_DuplicateType` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Accept` [analyzer, stage0, self]

### R4-promoted-diagnostics — only C# errors make a program C#-invalid; the 13 promoted diagnostics are errors, other warnings are not
- Quote (§4): A program is C#-invalid only if it has a C# error. The 13 diagnostics that Vibranium.App.props lists in WarningsAsErrors (§1, principle 1) are errors on every compiler, so a program that raises one of them is C#-invalid.
- Quote (§4): Any other C# warning, such as an unused local (CS0168, CS0219), is not a language error: Vibranium.Analyzer, vib, and vib-stage2 accept a program whose only C# diagnostics are such warnings
- Expect: every frontend rejects each promoted diagnostic (R1.1-promoted-*); every frontend accepts a program whose only C# diagnostic is another warning (CS0168, CS0219), and its native output equals the managed run
- Reject shape: a non-exhaustive switch expression (CS8509) and the promoted pattern and argument warnings.
- Accept shape: the near-miss programs that raise none of the promoted diagnostics (a `var` pattern, an `in` argument, a non-constant `is` test), and warning-only programs: an unused assigned local (`int unused = 1;`, CS0219) and an unused declared local (`int declared;`, CS0168).
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_PromotedCs8509_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_NoWarningControlsRemainAccepted` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_PromotedDiagnostics_Accept_WarningOnly` [analyzer, stage0, self]
- Note: For the Analyzer, accepting a warning-only program means that the SDK compilation under the Vibranium language options (the 13 promotions, without `TreatWarningsAsErrors`) reports the warning and no error, and that `VibraniumDiagnosticAnalyzer` reports nothing. The `dotnet build` refusal under the default profile is the build policy of R4-default-profile-build-policy.

### R4-default-profile-build-policy — dotnet build under the default profile refuses a warning-only program
- Quote (§4): while dotnet build under the default Vibranium.App.props profile refuses it because that profile also sets TreatWarningsAsErrors.
- Quote (§1.1): as build policy (dotnet build under this profile also refuses a program with any other warning, which does not make the program C#-invalid; see §4 "C# validity")
- Kind: repo
- Evidence: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_DefaultProfileBuild_RefusesWarningOnly`
- Note: The evidence builds a temporary Exe project that imports this repository's `Vibranium.App.props` and references the built `Vibranium.Core` assembly and the `Vibranium.Analyzer` assemblies. `dotnet build` fails with error CS0219 and error CS0168 on the two warning-only programs that R4-promoted-diagnostics accepts on every frontend, with no VIB diagnostic, and builds the corrected program.

### R4-csharp-invalid-self — vib-stage2 rejects invalid C# on its own; accepting invalid C# is a compiler defect
- Quote (§4): Direct vib-stage2 has no Roslyn prerequisite and must reject invalid C# on its own; any acceptance of invalid C# is a compiler defect.
- Expect: vib-self rejects every C#-invalid program (genuine C# errors and promoted diagnostics) with its own diagnostic; it need not match Roslyn's ID or wording
- Reject shape: a C# type error, safety-neutral C# errors (R4-genuine-csharp-error) and promoted diagnostics (R1.1-promoted-*).
- Accept shape: the corrected program.
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_DuplicateLocal` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_DuplicateMethod` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_ArgumentCount` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_GenuineCSharpError_Reject_DuplicateType` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round11Stage0Tests.PromotedWarning_IsSourceBoundaryEvenWhenSuppressed` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_CSharpInvalidSelf_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_CSharpValid_Reject` [analyzer, stage0, self]
- Reject: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_Round7_ExhaustiveReturnPaths_And_Reachability_Differential` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R4_CSharpInvalidSelf_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessSubsetTests.R1_1_CSharpValid_Accept` [analyzer, stage0, self]

### R4-roslyn-paths-diagnostics — Roslyn paths may report C# or more specific Vibranium diagnostics; vib-self need not match Roslyn
- Quote (§4): The Roslyn-based paths can report C# diagnostics or more specific Vibranium diagnostics. The self-hosted compiler need not match Roslyn's diagnostic ID or wording for invalid C#.
- Kind: prose
- Reason: A permission about diagnostic identity, not a verdict. The C#-invalid witnesses accept the CS ID or a named more specific Vibranium diagnostic from Stage-0, and any VIB error from vib-self.

### R4-official-pipeline — the official pipeline validates source with Roslyn
- Quote (§4): The official pipeline (vib check, vib build, vib run, dotnet build) validates source with Roslyn.
- Kind: prose
- Reason: Describes the pipeline. Its Roslyn verdicts are witnessed per frontend by R4-genuine-csharp-error and R1.1-promoted-* on the SDK and Stage-0 paths.

### R4-mutation-oracle — the invalid-program mutation oracle measures rejection behaviour
- Quote (§4): The [invalid-program mutation oracle](MUTATION.md) measures rejection behavior.
- Kind: prose
- Reason: A tooling statement. The oracle measures rejection rates and gives no per-program verdict to witness here.

## §5 MIR drop elaboration, LLVM 18 lowering and runtime parity

### R5.1-target — v0.2 supports Linux x86_64 only
- Quote (§5.1): v0.2 supports Linux x86_64 only. Linux aarch64 and other native targets are planned for v0.4 and are not implemented in v0.2.
- Kind: prose
- Reason: Target statement; the test suite runs only on Linux x86_64 and there is no other target to reject.

### R5.1-heap-alloc — class instances use malloc(max(1, size)); drop frees owned fields
- Quote (§5.1): class instances allocate via malloc(max(1, byte_size))
- Kind: runtime
- Accept shape: allocation and drop of an empty class and a class with owned fields, ASan/LSan clean.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_1_HeapAlloc_Accept` [analyzer, stage0, self]
- Note: The "NULL panics with out of memory" clause is R5.4-oom, which is not testable.

### R5.1-iterative-drop — deep heap graphs drop with a bounded native stack
- Quote (§5.1): vib emits a bounded-stack iterative worklist drop
- Kind: runtime
- Accept shape: a 1,000,000-node linked list and a deep tree dropped without stack overflow, ASan/LSan clean.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_1_IterativeDrop_Accept` [analyzer, stage0, self]

### R5.1-span-loop-attributes — no nonnull/dereferenceable on span pointers, no mustprogress on loops
- Quote (§5.1): Never emit LLVM nonnull or dereferenceable attributes on Span<T> / ReadOnlySpan<T> data pointers
- Quote (§5.1): Never emit LLVM mustprogress on loops
- Kind: emitter
- Accept shape: IR of a program with span parameters and an empty-span call has none of those attributes; it runs identically.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_1_SpanLoopAttributes_Accept` [analyzer, stage0, self]

### R5.2-lifo-drop-flags — live !Copy locals drop LIFO; conditional moves use drop flags
- Quote (§5.2): Live !Copy locals drop in reverse declaration (LIFO) order on normal scope exits
- Quote (§5.2): use false-initialized stack drop flags updated on initialization and move
- Kind: runtime
- Accept shape: owners moved on one branch or inside `&&`/`||`/`?:`, ASan/LSan clean.
- Accept: `Vibranium.Tests.Round10Stage0DefiniteAssignmentTests.Round10_StatementSwitch_ManagedAnalyzerStage0SelfParity` [analyzer, stage0, self]

### R5.2-assignment-order — lhs places, then rhs, then drop-if-initialized, then store
- Quote (§5.2): Assignment lhs = rhs & out Overwrites:
- Quote (§5.2): Evaluate lhs place operands first in C# left-to-right order
- Quote (§5.2): Evaluate rhs.
- Quote (§5.2): Drop lhs's prior !Copy value iff lhs remains initialized/unmoved after rhs completes
- Quote (§5.2): Store the result into lhs and mark lhs initialized.
- Kind: runtime
- Accept shape: `x = x;`, `x = Step(x);`, a conditional move inside the rhs, and an out-of-bounds index target whose rhs prints, managed and native equal and ASan/LSan clean.
- Accept: `Vibranium.Tests.Round11Stage0Tests.CompoundAssignment_SnapshotsBeforeBranchingRhs` [analyzer, stage0, self]

### R5.2-slot-writes — Set and *ref = v drop the old value; Take and Replace move it out
- Quote (§5.2): OptionMut<T>.Set(v) and *ref = v drop the overwritten value; Take() and Replace(v) move the prior value out without dropping it.
- Kind: runtime
- Accept shape: `Set` and `ref` writes over live nodes, `Take`/`Replace` results used afterwards, ASan/LSan clean.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_2_SlotWrites_Accept` [analyzer, stage0, self]

### R5.3-wrapping — integer +, -, * and unary - wrap
- Quote (§5.3): Default unchecked two's-complement wrapping for +, -, *, and unary -
- Kind: runtime
- Accept shape: `int.MaxValue + 1`, `-int.MinValue`, `long` products and `byte` promotion with runtime operands (the constant form `int.MaxValue + 1` is CS0220), managed and native equal.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_3_Wrapping_Accept` [analyzer, stage0, self]

### R5.3-division-checks — division and remainder by zero and MIN / -1 panic
- Quote (§5.3): Before LLVM sdiv/udiv/srem/urem, emit explicit branch checks
- Quote (§5.3): divisor == 0 -> panic "attempt to divide by zero".
- Quote (§5.3): Signed MIN / -1 and signed MIN % -1
- Kind: runtime
- Accept shape: see R5.4-divide-by-zero and R5.4-arithmetic-overflow.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_DivideByZero_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_ArithmeticOverflow_Accept` [analyzer, stage0, self]

### R5.3-shift-mask — shift counts are masked
- Quote (§5.3): Mask shift counts per C# (count & 31 for 32-bit operands, count & 63 for 64-bit operands).
- Kind: runtime
- Accept shape: `1 << 33`, `1L << 65`, `-1 >> 40` with runtime counts.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_3_ShiftMask_Accept` [analyzer, stage0, self]

### R5.3-float — strict IEEE-754, constant folding as Roslyn, saturating runtime casts
- Quote (§5.3): Strict IEEE-754 float/double semantics
- Quote (§5.3): runtime float-to-integer casts lower directly to llvm.fptosi.sat.iN / llvm.fptoui.sat.iN
- Kind: runtime
- Accept shape: NaN and out-of-range runtime casts to `int`, `byte`, `long`, and the constant `unchecked((byte)300.0)`.
- Accept: `Vibranium.Tests.VibraniumEndToEndTests.Round10_FloatingRemainder_ActualCliBuilds_AreExactAndLibcOnly` [analyzer, stage0, self]

### R5.3-frem — float remainder uses runtime helpers, not libm
- Quote (§5.3): float/double % lowers to the internal runtime helpers __vib_rem_f32/__vib_rem_f64
- Kind: runtime
- Accept shape: `%` on `float` and `double` with negative, infinite and NaN operands.
- Accept: `Vibranium.Tests.VibraniumEndToEndTests.Round10_FloatingRemainder_ActualCliBuilds_AreExactAndLibcOnly` [analyzer, stage0, self]

### R5.3-libc-only — production binaries depend only on libc.so.6
- Quote (§5.3): Production binaries depend only on libc.so.6.
- Kind: emitter
- Accept shape: `DT_NEEDED` of a native binary lists only `libc.so.6` (and the loader), and it runs identically.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_3_LibcOnly_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.VibraniumEndToEndTests.NativeBinary_LinksOnlySystemLibc_NoClrOrGc` [stage0]

### R5.3-noalias — noalias only on exclusive pointer parameters
- Quote (§5.3): Emit noalias strictly on verified exclusive pointer parameters
- Kind: emitter
- Accept shape: IR of a function with `in`/`ReadOnlySpan` parameters has no `noalias` on them; `ref`/`Span` parameters may carry it.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_3_NoAlias_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.VibraniumSelfHostTests.SelfHost_SharedBorrows_OmitNoAlias_WhileExclusiveBorrowsRetainNoAlias` [self]

### R5.4-panic-contract — panics flush stdout, print panic: msg, exit 101 without unwinding
- Quote (§5.4): flush stdout, write "panic: " + message + "\n" in UTF-8 to stderr, and terminate immediately with exit code 101
- Quote (§5.4): without unwinding or running affine drops / IDisposable.Dispose()
- Kind: runtime
- Accept shape: a live `using` resource and pending output before `Panic`; Dispose is not printed and the exit code is 101.
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_PanicContract_Accept` [analyzer, stage0, self]
- Accept: `Vibranium.Tests.Round12RuleWitnessIntrinsicTests.R3_5_Panic_Accept` [analyzer, stage0, self]

### R5.4-index-out-of-bounds — index out of bounds panics
- Quote (§5.4): Vec<T>[i], Span<T>[i], ReadOnlySpan<T>[i] out of bounds | "index out of bounds"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_IndexOutOfBounds_Accept` [analyzer, stage0, self]

### R5.4-slice-out-of-bounds — slice out of bounds panics
- Quote (§5.4): or Range out of bounds | "slice out of bounds"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_SliceOutOfBounds_Accept` [analyzer, stage0, self]

### R5.4-truncate — Truncate out of bounds panics
- Quote (§5.4): Vec<T>.Truncate(length) with length < 0 or length > Count | "truncate out of bounds"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_Truncate_Accept` [analyzer, stage0, self]

### R5.4-resize-negative — Resize with a negative length panics
- Quote (§5.4): Vec<T>.Resize(length, fill) with length < 0 | "Vec length cannot be negative"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_ResizeNegative_Accept` [analyzer, stage0, self]

### R5.4-divide-by-zero — integer division or remainder by zero panics
- Quote (§5.4): Integer / or % by 0 | "attempt to divide by zero"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_DivideByZero_Accept` [analyzer, stage0, self]

### R5.4-arithmetic-overflow — MIN / -1 and MIN % -1 panic
- Quote (§5.4): Signed integer MIN / -1 or MIN % -1 | "arithmetic overflow"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_ArithmeticOverflow_Accept` [analyzer, stage0, self]

### R5.4-option-unwrap — Option.Unwrap on None panics
- Quote (§5.4): Option<T>.Unwrap() on None | "called Option.Unwrap() on None"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_OptionUnwrap_Accept` [analyzer, stage0, self]

### R5.4-option-expect — Option.Expect on None panics with the message
- Quote (§5.4): Option<T>.Expect(msg) on None | <msg>
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_OptionExpect_Accept` [analyzer, stage0, self]

### R5.4-option-unwrapmut — Option.UnwrapMut on None panics
- Quote (§5.4): Option<T>.UnwrapMut() on None | "called Option.UnwrapMut() on None"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_OptionUnwrapMut_Accept` [analyzer, stage0, self]

### R5.4-result-unwrap — Result.Unwrap on Err panics
- Quote (§5.4): Result<T, E>.Unwrap() on Err | "called Result.Unwrap() on Err"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_ResultUnwrap_Accept` [analyzer, stage0, self]

### R5.4-result-expect — Result.Expect on Err panics with the message
- Quote (§5.4): Result<T, E>.Expect(msg) on Err | <msg>
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_ResultExpect_Accept` [analyzer, stage0, self]

### R5.4-capacity-negative — new Vec<T>(negative) panics
- Quote (§5.4): new Vec<T>(capacity) with capacity < 0 | "Vec capacity cannot be negative"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_CapacityNegative_Accept` [analyzer, stage0, self]

### R5.4-capacity-overflow — capacity beyond Array.MaxLength panics
- Quote (§5.4): exceeding Array.MaxLength (0x7FFFFFC7) or byte-size overflow | "Vec capacity overflow"
- Kind: runtime
- Accept: `Vibranium.Tests.Round12RuleWitnessRuntimeTests.R5_4_CapacityOverflow_Accept` [analyzer, stage0, self]

### R5.4-oom — allocation failure panics with out of memory (best effort)
- Quote (§5.4): Heap allocation failure (malloc/realloc NULL or CLR OutOfMemoryException; best-effort) | "out of memory"
- Kind: prose
- Reason: Best-effort by SPEC's own wording (§0 excludes host out-of-memory from the guarantees); a test cannot exhaust host memory deterministically on a shared machine.
