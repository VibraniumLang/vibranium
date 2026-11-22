# Pending regression repros (not gated)

C#-invalid programs found by campaign-6 (seed 20261122) and its hand triage that self still accepts on candidate
c6 (checked on tree6 pre13; pre14 keeps the same self sources). The exception is
`hand-c6-CS0220-enum-long-analyzer.cs`: self rejects it, but the tool's in-process Analyzer still throws on it.
`run-mutation.sh ci` reads only the top level of `eng/mutation/corpus/`, so these files do not fail the gate yet.

`PENDING.tsv` lists each file with its family and the fix it waits for:

* N1: checked constant overflow with an int-based enum operand is not detected (CS0220).
* N2: attribute names are not bound and attribute arguments are not type-checked on methods and local functions
  (CS0246, CS0234, CS1503, CS1729, CS0019, CS0220, CS0103, CS0121).
* N3: a `this ref` extension on a class or an unconstrained T (CS8337).
* N4: top-level statements with valued returns whose end is reachable (CS0161).
* N5: a parameterless struct constructor that is not public (CS8958).
* AD0001: the Analyzer and Stage-0 crash in `TryMakeConstantOperand` on a non-int enum constant outside Int32.

When a fix is in the tree, move its files up one level (one `mv` per file, or the handoff's
`after-fixes/land-pending.sh`), delete their rows here, and run the gate on managed and native self without
`--known-open`. Delete this directory when it is empty.
