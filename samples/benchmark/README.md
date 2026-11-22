# Vibranium 4-Way Benchmark Suite

Compares the exact same 3-kernel workload across four targets with byte-for-byte output verification:

1. **`vibranium/` (`VibraniumBenchmark.csproj` + `Program.cs`)** — Top-level **Vibranium** source (`Option<Node>`, `Vec<T>`, `.AsRef()`, `.AsMut()`, `Span<T>`). Runs on **`.NET 11.0.100` (RyuJIT)** for development/debugging *and* compiles via **`vib -O3` (LLVM 18)** to a standalone native binary.
2. **`csharp/` (`CSharpBenchmark.csproj` + `Program.cs`)** — Hand-optimized idiomatic **C# 15** (`sealed class Node?`, raw `int[]` / `float[]`, `Span<T>`, `TieredPGO`, `OptimizationPreference=Speed`).
3. **`rust/` (`Cargo.toml` + `src/main.rs`)** — Idiomatic safe **Rust 2024** (`Option<Box<Node>>` with iterative `Drop`, `Vec<T>`, `&mut [T]` / `&[T]`, `cargo build --release`).

## Kernels

- **Heap Ownership & Deterministic Drop:** 400 rounds × 5,000 heap-allocated `Node`s (`2,000,000` allocations & drops).
- **In-Place Array Mutation:** 400 runs × 4,096-element iterative Quicksort (`1,638,400` ints).
- **Disjoint-Slice SIMD Kernel:** 250,000 passes × 2,048-float `Saxpy` (`512,000,000` FMAs).

## Run

```bash
./samples/benchmark/run-benchmark.sh
```
