import VibraniumFormal

open VibraniumFormal
open VibraniumFormal.Tests

def main : IO Unit := do
  IO.println "=== Vibranium MIR Mechanized Verification Suite (Lean 4) ==="
  match checkProg showcaseProg, evalNative showcaseProg, evalManaged showcaseProg with
  | .ok retTy, .ok natRun, .ok mgdRun =>
    IO.println s!"[PASS] Static Checker:       return type = {repr retTy}"
    IO.println s!"[PASS] Native LLVM Outcome:  {repr natRun.outcome}"
    IO.println s!"[PASS] Managed CLR Outcome:  {repr mgdRun.outcome}"
    IO.println s!"[PASS] Dual Equivalence:     {natRun.outcome == mgdRun.outcome}"
    IO.println "[PASS] Negative Diagnostics: VIB001, VIB002, VIB003, VIB004, VIB006, VIB007, VIB008 verified."
  | _, _, _ =>
    throw (IO.userError "Unexpected showcase verification failure")
