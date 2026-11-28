/-
  VibraniumFormal.lean
  Root Entry Point & Axiom Audit for the Vibranium MIR Mechanized Verification Suite.

  Imports all core modules, machine-checked soundness & bisimulation proofs, and
  compile-time `#guard` differential tests, and audits every foundational theorem
  with `#print axioms` to confirm ZERO `sorryAx` and ZERO custom axioms.
-/

import VibraniumFormal.Syntax
import VibraniumFormal.Checker
import VibraniumFormal.Semantics
import VibraniumFormal.Soundness
import VibraniumFormal.Equivalence
import VibraniumFormal.Tests

-- Axiom Audit: confirm zero `sorryAx` and zero custom axioms across all theorems.
#print axioms VibraniumFormal.aliasing_xor_mutability
#print axioms VibraniumFormal.shared_borrow_immutability
#print axioms VibraniumFormal.native_memory_safety
#print axioms VibraniumFormal.clr_llvm_equivalence
