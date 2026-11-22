#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONFIG="Release"
SKIP_DOTNET_BUILD=0
SKIP_BENCHMARK=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration|-c)
            CONFIG="$2"
            shift 2
            ;;
        --skip-dotnet-build)
            SKIP_DOTNET_BUILD=1
            shift
            ;;
        --skip-benchmark)
            SKIP_BENCHMARK=1
            shift
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 2
            ;;
    esac
done

find_dotnet() {
    if [[ -n "${DOTNET_BIN:-}" && -x "${DOTNET_BIN}" ]]; then
        printf '%s\n' "${DOTNET_BIN}"
        return 0
    fi
    if command -v dotnet >/dev/null 2>&1; then
        command -v dotnet
        return 0
    fi
    if [[ -n "${HOME:-}" && -x "${HOME}/.dotnet/dotnet" ]]; then
        printf '%s\n' "${HOME}/.dotnet/dotnet"
        return 0
    fi
    echo "error: dotnet SDK not found" >&2
    exit 1
}

find_tool() {
    local primary="$1"
    local fallback="${2:-}"
    if command -v "$primary" >/dev/null 2>&1; then
        command -v "$primary"
        return 0
    fi
    if [[ -n "$fallback" ]] && command -v "$fallback" >/dev/null 2>&1; then
        command -v "$fallback"
        return 0
    fi
    echo "error: required tool '$primary' not found" >&2
    exit 1
}

DOTNET_CMD="$(find_dotnet)"
CLANG_CMD="${CLANG_BIN:-$(find_tool clang-18 clang)}"
OPT_CMD="${OPT_BIN:-$(find_tool opt-18 opt)}"
READELF_CMD="$(find_tool readelf)"
NM_CMD="$(find_tool nm)"

# Production Linux ELF files may need libc only. The ELF interpreter is not
# a DT_NEEDED entry; sanitizer builds have their own, separate dependency set.
assert_libc_only() {
    local binary="$1" needed imports
    needed="$("${READELF_CMD}" -d "${binary}" | sed -n 's/.*(NEEDED).*\[\([^]]*\)\].*/\1/p')"
    if [[ "${needed}" != "libc.so.6" ]]; then
        echo "error: $(basename "${binary}") must need exactly libc.so.6; found: ${needed}" >&2
        exit 1
    fi
    imports="$("${NM_CMD}" -D --undefined-only "${binary}")"
    if grep -Eq '[[:space:]]fmodf?(@[^[:space:]]*)?$' <<<"${imports}"; then
        echo "error: $(basename "${binary}") imports a floating remainder libcall!" >&2
        exit 1
    fi
    printf '%s: NEEDED %s\n' "$(basename "${binary}")" "${needed}"
}

CLEANUP_WORK=0
if [[ -n "${OUT_DIR:-}" ]]; then
    mkdir -p "${OUT_DIR}"
    WORK_DIR="$(cd "${OUT_DIR}" && pwd)"
else
    WORK_DIR="$(mktemp -d)"
    CLEANUP_WORK=1
fi
ALT_DIR="$(mktemp -d)"

cleanup() {
    rm -rf "${ALT_DIR}"
    if [[ "${CLEANUP_WORK}" -eq 1 ]]; then
        rm -rf "${WORK_DIR}"
    fi
}
trap cleanup EXIT

echo "========================================================================"
echo " Vibranium Self-Hosting Triple-Identity Bootstrap Verification"
echo "========================================================================"
echo "Configuration : ${CONFIG}"
echo "Toolchain     : $(basename "${DOTNET_CMD}"), $(basename "${CLANG_CMD}"), $(basename "${OPT_CMD}")"

if [[ "${SKIP_DOTNET_BUILD}" -eq 0 ]]; then
    echo ""
    echo "[0/7] Building Stage-0 vib and .NET 11 vib-self (${CONFIG})..."
    "${DOTNET_CMD}" build --disable-build-servers -c "${CONFIG}" -v minimal "${REPO_ROOT}/src/vib/vib.csproj"
    "${DOTNET_CMD}" build --disable-build-servers -c "${CONFIG}" -v minimal "${REPO_ROOT}/src/vib-self/VibraniumSelfCompiler.csproj"
fi

VIB_C0_DLL="${REPO_ROOT}/src/vib/bin/${CONFIG}/net11.0/vib.dll"
VIB_SELF_DLL="${REPO_ROOT}/src/vib-self/bin/${CONFIG}/net11.0/vib-self.dll"
MANIFEST="${REPO_ROOT}/src/vib-self/vib.manifest"
STD_SYSROOT="${REPO_ROOT}/src"

echo ""
echo "[1/7] Stage M: Emitting managed.ll via .NET 11 vib-self..."
"${DOTNET_CMD}" "${VIB_SELF_DLL}" emit-ll "${MANIFEST}" -o "${WORK_DIR}/managed.ll"

echo "[2/7] Stage C0 -> C1: Compiling native vib-stage1 via Stage-0 Roslyn vib..."
"${DOTNET_CMD}" "${VIB_C0_DLL}" build "${MANIFEST}" -O3 --cc "${CLANG_CMD}" -o "${WORK_DIR}/vib-stage1"

echo "      Verifying vib-stage1 libc-only ELF dependencies and remainder imports..."
assert_libc_only "${WORK_DIR}/vib-stage1"

echo "[3/7] Stage C1 -> C2: Emitting stage1.ll and building native vib-stage2 via vib-stage1..."
"${WORK_DIR}/vib-stage1" emit-ll "${MANIFEST}" -o "${WORK_DIR}/stage1.ll"
"${WORK_DIR}/vib-stage1" build "${MANIFEST}" -O3 --cc "${CLANG_CMD}" -o "${WORK_DIR}/vib-stage2"

assert_libc_only "${WORK_DIR}/vib-stage2"

echo "[4/7] Stage C2: Emitting stage2.ll via native vib-stage2..."
"${WORK_DIR}/vib-stage2" emit-ll "${MANIFEST}" -o "${WORK_DIR}/stage2.ll"

echo "[5/7] Validating managed.ll, stage1.ll, and stage2.ll with $(basename "${OPT_CMD}") -passes=verify..."
"${OPT_CMD}" -passes=verify -disable-output "${WORK_DIR}/managed.ll"
"${OPT_CMD}" -passes=verify -disable-output "${WORK_DIR}/stage1.ll"
"${OPT_CMD}" -passes=verify -disable-output "${WORK_DIR}/stage2.ll"

echo "[6/7] Asserting Triple-Identity Fixed Point (managed.ll == stage1.ll == stage2.ll)..."
SHA_MANAGED="$(sha256sum "${WORK_DIR}/managed.ll" | awk '{print $1}')"
SHA_STAGE1="$(sha256sum "${WORK_DIR}/stage1.ll" | awk '{print $1}')"
SHA_STAGE2="$(sha256sum "${WORK_DIR}/stage2.ll" | awk '{print $1}')"

echo "      SHA256(managed.ll) = ${SHA_MANAGED}"
echo "      SHA256(stage1.ll)  = ${SHA_STAGE1}"
echo "      SHA256(stage2.ll)  = ${SHA_STAGE2}"

if [[ "${SHA_MANAGED}" != "${SHA_STAGE1}" ]]; then
    echo "error: managed.ll != stage1.ll fixed-point mismatch!" >&2
    exit 1
fi
if [[ "${SHA_STAGE1}" != "${SHA_STAGE2}" ]]; then
    echo "error: stage1.ll != stage2.ll fixed-point mismatch!" >&2
    exit 1
fi

echo "[7/7] Verifying cross-directory path-independence reproducibility..."
mkdir -p "${ALT_DIR}/src"
cp -R "${REPO_ROOT}/src/Vibranium.Core" "${ALT_DIR}/src/Vibranium.Core"
cp -R "${REPO_ROOT}/src/Vibranium.Std" "${ALT_DIR}/src/Vibranium.Std"
cp -R "${REPO_ROOT}/src/vib-self" "${ALT_DIR}/src/vib-self"
"${WORK_DIR}/vib-stage2" emit-ll "${ALT_DIR}/src/vib-self/vib.manifest" -o "${WORK_DIR}/repro.ll"
SHA_REPRO="$(sha256sum "${WORK_DIR}/repro.ll" | awk '{print $1}')"
echo "      SHA256(repro.ll)   = ${SHA_REPRO}"

if [[ "${SHA_REPRO}" != "${SHA_STAGE2}" ]]; then
    echo "error: cross-directory reproducibility mismatch (host path leaked into IR)!" >&2
    exit 1
fi

echo "      Smoke-testing vib-stage2 on samples/demo, samples/benchmark, samples/http-server, and samples/bad..."
"${WORK_DIR}/vib-stage2" build "${REPO_ROOT}/samples/demo/Program.cs" \
    --sysroot "${STD_SYSROOT}" --cc "${CLANG_CMD}" -o "${WORK_DIR}/demo-native"
assert_libc_only "${WORK_DIR}/demo-native"
DEMO_OUT="$("${WORK_DIR}/demo-native")"
EXPECTED_DEMO_OUT=$'45\n230\nalpha\nbeta\n142\n0\nclose 2\nclose 1'
if [[ "${DEMO_OUT}" != "${EXPECTED_DEMO_OUT}" ]]; then
    echo "error: demo-native produced unexpected output:" >&2
    printf '%s\n' "${DEMO_OUT}" >&2
    exit 1
fi

echo "      Checking dynamic floating remainder and libc-only CLI builds at -O0/-O3..."
cat >"${WORK_DIR}/remainder.cs" <<'CS'
var arguments = Vibranium.Std.Env.Args().Unwrap();
double d = arguments.Count + 5.5;
float f = arguments.Count + 5.5f;
WriteLine((int)(d % 2.0));
WriteLine((int)(f % 2.0f));
double z = arguments.Count + 4.0;
WriteLine(1.0 / (-z % 2.0) < 0.0);
CS
for compiler in stage0 managed-self stage2; do
    case "${compiler}" in
        stage0) compiler_command=("${DOTNET_CMD}" "${VIB_C0_DLL}") ;;
        managed-self) compiler_command=("${DOTNET_CMD}" "${VIB_SELF_DLL}") ;;
        stage2) compiler_command=("${WORK_DIR}/vib-stage2") ;;
    esac
    for optimization in 0 3; do
        remainder_binary="${WORK_DIR}/remainder-${compiler}-O${optimization}"
        "${compiler_command[@]}" build "${WORK_DIR}/remainder.cs" --sysroot "${STD_SYSROOT}" \
            --cc "${CLANG_CMD}" "-O${optimization}" -o "${remainder_binary}"
        assert_libc_only "${remainder_binary}"
        if [[ "$("${remainder_binary}")" != $'1\n1\nTrue' ]] || \
           [[ "$("${remainder_binary}" runtime-input)" != $'0\n0\nTrue' ]]; then
            echo "error: ${compiler} -O${optimization} floating remainder mismatch!" >&2
            exit 1
        fi
    done
done

"${WORK_DIR}/vib-stage2" check "${REPO_ROOT}/samples/benchmark/vibranium/Program.cs" \
    --sysroot "${STD_SYSROOT}"

"${WORK_DIR}/vib-stage2" check "${REPO_ROOT}/samples/http-server/vibranium/Program.cs" \
    --sysroot "${STD_SYSROOT}"

for bad_cs in "${REPO_ROOT}"/samples/bad/*.cs; do
    if "${WORK_DIR}/vib-stage2" check "${bad_cs}" --sysroot "${STD_SYSROOT}" >/dev/null 2>&1; then
        echo "error: vib-stage2 unexpectedly accepted negative sample $(basename "${bad_cs}")!" >&2
        exit 1
    fi
done

if [[ "${SKIP_BENCHMARK}" -eq 0 ]]; then
    echo ""
    echo "========================================================================"
    echo " Compile-Speed Benchmark (median of 5 runs)"
    echo "========================================================================"
    python3 - "${WORK_DIR}" "${REPO_ROOT}" "${DOTNET_CMD}" "${VIB_C0_DLL}" "${CLANG_CMD}" "${CONFIG}" <<'PY'
import os, shutil, statistics, subprocess, sys, time

work_dir, repo_root, dotnet_cmd, vib_c0_dll, clang_cmd, cfg = sys.argv[1:7]
stage2 = os.path.join(work_dir, "vib-stage2")
sysroot = os.path.join(repo_root, "src")
demo_cs = os.path.join(repo_root, "samples/demo/Program.cs")
http_cs = os.path.join(repo_root, "samples/http-server/vibranium/Program.cs")
self_manifest = os.path.join(repo_root, "src/vib-self/vib.manifest")

def bench(cmd, runs=5):
    times = []
    for _ in range(runs):
        t0 = time.perf_counter()
        subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        times.append((time.perf_counter() - t0) * 1000.0)
    return statistics.median(times)

rows = []
rows.append(("demo check (vib-stage2 native)", bench([stage2, "check", demo_cs, "--sysroot", sysroot])))
rows.append(("demo check (Stage-0 .NET 11 vib)", bench([dotnet_cmd, vib_c0_dll, "check", demo_cs])))
rows.append(("demo emit-ll (vib-stage2 native)", bench([stage2, "emit-ll", demo_cs, "--sysroot", sysroot, "-o", os.path.join(work_dir, "b_demo.ll")])))
rows.append(("demo build -O3 (vib-stage2 + clang-18)", bench([stage2, "build", demo_cs, "-O3", "--sysroot", sysroot, "--cc", clang_cmd, "-o", os.path.join(work_dir, "b_demo")])))
rows.append(("demo build -O3 (Stage-0 vib + clang-18)", bench([dotnet_cmd, vib_c0_dll, "build", demo_cs, "-O3", "--cc", clang_cmd, "-o", os.path.join(work_dir, "b_demo_c0")])))

rustc = shutil.which("rustc") or (os.path.expanduser("~/.cargo/bin/rustc") if os.path.exists(os.path.expanduser("~/.cargo/bin/rustc")) else None)
rust_bench_rs = os.path.join(repo_root, "samples/benchmark/rust/src/main.rs")
if rustc and os.path.exists(rust_bench_rs):
    rows.append(("benchmark/rust check-emit (rustc --emit=metadata)", bench([rustc, "--edition=2024", "-C", "opt-level=3", "--emit=metadata", rust_bench_rs, "-o", os.path.join(work_dir, "b_rust.rmeta")])))
    rows.append(("benchmark/rust build -O3 (rustc -O)", bench([rustc, "--edition=2024", "-C", "opt-level=3", rust_bench_rs, "-o", os.path.join(work_dir, "b_rust")])))

vib_bench_cs = os.path.join(repo_root, "samples/benchmark/vibranium/Program.cs")
if os.path.exists(vib_bench_cs):
    rows.append(("benchmark/vib check (vib-stage2 native)", bench([stage2, "check", vib_bench_cs, "--sysroot", sysroot])))
    rows.append(("benchmark/vib build -O3 (vib-stage2 + clang-18)", bench([stage2, "build", vib_bench_cs, "-O3", "--sysroot", sysroot, "--cc", clang_cmd, "-o", os.path.join(work_dir, "b_vibbench")])))

rows.append(("http-server check + Std (vib-stage2 native)", bench([stage2, "check", http_cs, "--sysroot", sysroot])))
rows.append(("vib-self check (vib-stage2 native)", bench([stage2, "check", self_manifest])))
rows.append(("vib-self emit-ll (vib-stage2 native)", bench([stage2, "emit-ll", self_manifest, "-o", os.path.join(work_dir, "b_self.ll")])))
rows.append(("vib-self emit-ll (.NET 11 vib-self)", bench([dotnet_cmd, os.path.join(repo_root, f"src/vib-self/bin/{cfg}/net11.0/vib-self.dll"), "emit-ll", self_manifest, "-o", os.path.join(work_dir, "b_self_m.ll")])))

for label, ms in rows:
    print(f"  {label:<50} {ms:8.2f} ms")
PY
fi

echo ""
echo "BOOTSTRAP FIXED POINT VERIFIED: Triple-Identity (managed.ll == stage1.ll == stage2.ll) == ${SHA_STAGE2}"
