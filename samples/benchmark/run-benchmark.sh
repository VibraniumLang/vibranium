#!/usr/bin/env bash
set -euo pipefail

BENCH_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${BENCH_DIR}/../.." && pwd)"
DOTNET="${HOME}/.dotnet/dotnet"
if [[ ! -x "${DOTNET}" ]]; then
  DOTNET="dotnet"
fi

OUT_DIR="$(mktemp -d /tmp/vibranium-bench.XXXXXX)"
cleanup() {
  rm -rf "${OUT_DIR}"
}
trap cleanup EXIT

echo "Building all 4 targets (.NET 11 Vibranium, .NET 11 Optimized C# 15, Rust 1.99 -O3, Vibranium Native LLVM 18 -O3)..."
"${DOTNET}" build --disable-build-servers "${ROOT}/src/vib/vib.csproj" -c Release -v quiet --nologo
"${DOTNET}" build --disable-build-servers "${BENCH_DIR}/vibranium/VibraniumBenchmark.csproj" -c Release -v quiet --nologo
"${DOTNET}" build --disable-build-servers "${BENCH_DIR}/csharp/CSharpBenchmark.csproj" -c Release -v quiet --nologo
"${DOTNET}" build --disable-build-servers "${ROOT}/samples/demo/Demo.csproj" -c Release -v quiet --nologo
CARGO_TARGET_DIR="${OUT_DIR}/rust-target" cargo build --release --manifest-path "${BENCH_DIR}/rust/Cargo.toml" --quiet
cp "${OUT_DIR}/rust-target/release/vibranium-vs-rust-benchmark" "${OUT_DIR}/bench-rust"
rm -rf "${OUT_DIR}/rust-target"
cp "${OUT_DIR}/bench-rust" "${OUT_DIR}/bench-rust-stripped"
strip "${OUT_DIR}/bench-rust-stripped"

"${DOTNET}" "${ROOT}/src/vib/bin/Release/net11.0/vib.dll" build "${BENCH_DIR}/vibranium/Program.cs" -O3 -o "${OUT_DIR}/bench-vibranium-native" >/dev/null
cp "${OUT_DIR}/bench-vibranium-native" "${OUT_DIR}/bench-vibranium-stripped"
strip "${OUT_DIR}/bench-vibranium-stripped"

"${DOTNET}" "${ROOT}/src/vib/bin/Release/net11.0/vib.dll" build "${ROOT}/samples/demo/Program.cs" -O3 -o "${OUT_DIR}/demo-vibranium-native" >/dev/null

DOTNET_VIB_BIN="${BENCH_DIR}/vibranium/bin/Release/net11.0/VibraniumBenchmark"
DOTNET_CS_BIN="${BENCH_DIR}/csharp/bin/Release/net11.0/CSharpBenchmark"
DEMO_DOTNET_BIN="${ROOT}/samples/demo/bin/Release/net11.0/Demo"

echo ""
echo "Verifying 4-way output parity..."
"${DOTNET_VIB_BIN}" > "${OUT_DIR}/dotnet-vibranium.out"
"${DOTNET_CS_BIN}" > "${OUT_DIR}/dotnet-cs.out"
"${OUT_DIR}/bench-rust" > "${OUT_DIR}/rust.out"
"${OUT_DIR}/bench-vibranium-native" > "${OUT_DIR}/vibranium.out"
diff -u "${OUT_DIR}/dotnet-vibranium.out" "${OUT_DIR}/dotnet-cs.out"
diff -u "${OUT_DIR}/dotnet-vibranium.out" "${OUT_DIR}/rust.out"
diff -u "${OUT_DIR}/dotnet-vibranium.out" "${OUT_DIR}/vibranium.out"
echo "All 4 outputs match byte-for-byte:"
sed 's/^/  /' "${OUT_DIR}/vibranium.out"

echo ""
echo "=== 1. Binary Footprint ==="
printf ".NET 11 (Vibranium Source) DLL    : %8s bytes (+ ~75 MB .NET 11 runtime)\n" "$(stat -c%s "${DOTNET_VIB_BIN}.dll")"
printf ".NET 11 (Optimized C# 15) DLL: %8s bytes (+ ~75 MB .NET 11 runtime)\n" "$(stat -c%s "${DOTNET_CS_BIN}.dll")"
printf "Rust 1.99 (cargo --release)  : %8s bytes (stripped: %s bytes)\n" "$(stat -c%s "${OUT_DIR}/bench-rust")" "$(stat -c%s "${OUT_DIR}/bench-rust-stripped")"
printf "Vibranium Native (LLVM 18 -O3)    : %8s bytes (stripped: %s bytes, libc only)\n" "$(stat -c%s "${OUT_DIR}/bench-vibranium-native")" "$(stat -c%s "${OUT_DIR}/bench-vibranium-stripped")"

echo ""
echo "=== 2. Compile Speed (3 runs after warmup, $(wc -l < "${BENCH_DIR}/vibranium/Program.cs" | tr -d " ")-line Benchmark App) ==="
python3 - "${DOTNET}" "${ROOT}/src/vib/bin/Release/net11.0/vib.dll" "${BENCH_DIR}/vibranium/Program.cs" "${BENCH_DIR}/rust/src/main.rs" "${BENCH_DIR}/csharp/CSharpBenchmark.csproj" "${OUT_DIR}" << 'PY'
import subprocess, sys, time, statistics

dotnet, vib, vib_src, rust_src, cs_proj, out_dir = sys.argv[1:7]

compile_targets = [
    ("vib check (Roslyn + MIR + Polonius)", [dotnet, vib, "check", vib_src]),
    ("vib build -O0 (Native Debug ELF)", [dotnet, vib, "build", vib_src, "-O0", "-o", f"{out_dir}/tmp-vibranium-o0"]),
    ("vib build -O3 (Native Release ELF)", [dotnet, vib, "build", vib_src, "-O3", "-o", f"{out_dir}/tmp-vibranium-o3"]),
    ("rustc -C opt-level=0 (Debug ELF)", ["rustc", "--edition=2024", "-C", "opt-level=0", rust_src, "-o", f"{out_dir}/tmp-rust-o0"]),
    ("rustc -C opt-level=3 (Release ELF)", ["rustc", "--edition=2024", "-C", "opt-level=3", rust_src, "-o", f"{out_dir}/tmp-rust-o3"]),
    ("dotnet build (Incremental C# Debug)", [dotnet, "build", "--disable-build-servers", "--no-restore", cs_proj]),
]

for label, cmd in compile_targets:
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
    times = []
    for _ in range(3):
        t0 = time.perf_counter()
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        times.append((time.perf_counter() - t0) * 1000.0)
    print(f"{label:<38} | median: {statistics.median(times):7.1f} ms | best: {min(times):7.1f} ms")
PY

echo ""
echo "=== 3. Cold-Start + Short App Latency (samples/demo/Program.cs, 10 runs) ==="
python3 - "${DEMO_DOTNET_BIN}" "${OUT_DIR}/demo-vibranium-native" << 'PY'
import subprocess, sys, time, statistics

for label, cmd in [
    (".NET 11 (CLR startup + JIT + run)", [sys.argv[1]]),
    ("Vibranium Native (ELF startup + run)", [sys.argv[2]]),
]:
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
    times = []
    for _ in range(10):
        t0 = time.perf_counter()
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        times.append((time.perf_counter() - t0) * 1000.0)
    print(f"{label:<38} | median: {statistics.median(times):7.2f} ms | best: {min(times):7.2f} ms")
PY

echo ""
echo "=== 4. Sustained Compute & Memory (7 interleaved runs: 2M heap nodes + 400x Quicksort + 250k SIMD SAXPY) ==="
python3 - "${DOTNET_VIB_BIN}" "${DOTNET_CS_BIN}" "${OUT_DIR}/bench-rust" "${OUT_DIR}/bench-vibranium-native" << 'PY'
import subprocess, sys, time, re, statistics

targets = [
    (".NET 11 (Vibranium Source)", [sys.argv[1]]),
    (".NET 11 (Optimized C# 15)", [sys.argv[2]]),
    ("Rust 1.99 (cargo --release)", [sys.argv[3]]),
    ("Vibranium Native (LLVM 18 -O3)", [sys.argv[4]]),
]

results = {name: [] for name, _ in targets}
rss = {name: 0 for name, _ in targets}

for name, cmd in targets:
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)

for _ in range(7):
    for name, cmd in targets:
        t0 = time.perf_counter()
        p = subprocess.run(["/usr/bin/time", "-f", "RSS=%M"] + cmd, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, check=True)
        dt = (time.perf_counter() - t0) * 1000.0
        results[name].append(dt)
        m = re.search(r"RSS=(\d+)", p.stderr)
        if m:
            rss[name] = max(rss[name], int(m.group(1)))

for name, _ in targets:
    times = results[name]
    med_ms = statistics.median(times)
    best_ms = min(times)
    avg_ms = sum(times) / len(times)
    peak_rss = rss[name]
    print(f"{name:<28} | best: {best_ms:6.1f} ms | median: {med_ms:6.1f} ms | avg: {avg_ms:6.1f} ms | peak RSS: {peak_rss:6d} KB ({peak_rss/1024:.1f} MB)")
PY
