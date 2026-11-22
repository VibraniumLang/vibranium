#!/usr/bin/env bash
# Invalid-program mutation oracle (docs/MUTATION.md).
#   run-mutation.sh campaign --output NEW_DIR [tool options]   measurement campaign (report.md, findings.jsonl, repros/)
#   run-mutation.sh ci [--known-open FILE]                     fixed-budget CI gate; exit 1 when a frontend accepts a program
#                                                              that is C#-invalid and whose first key is not listed in FILE
#   run-mutation.sh report RESULTS_DIR                         re-render report.md with the current severity tables and rules
#   run-mutation.sh asan RESULTS_DIR [--per-id 3] [--all-ids]  native evidence: self build --asan, one run per program
#                                                              (default: safety-relevant defect keys; marks P0 on a memory error)
#   run-mutation.sh check FILE                                 SDK errors, cut forms and frontend verdicts for one program
#   run-mutation.sh self-test                                  the tool's own assertions
# Resource gate: on a shared developer machine every build and run goes through `heavy` (one slot for the whole
# script) and exit 75 from heavy is passed on, never bypassed. A CI runner (CI=true) has no heavy and runs directly.
# campaign, ci and check ask self only to `check`; asan mode builds and runs selected self accepts (needs clang-18).
# --self PATH picks the self compiler: vib-self.dll (default, run under dotnet) or a native executable such as
# bootstrap's vib-stage2 (run directly).
# Memory: a managed self child has a managed-heap hard limit (--self-heap-mb, default 1024), a native one an
# address-space limit through prlimit (--self-native-mb, default 4096); run the whole script under a cgroup memory cap
# where one is available. MUTATION_SKIP_BUILD=1 reuses existing Release builds.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd -- "$script_dir/../.." && pwd)
if [ -z "${HEAVY_HELD:-}" ] && [ "${CI:-}" != "true" ]; then
  command -v heavy >/dev/null || { echo 'heavy is required outside CI; refusing an ungated build or campaign.' >&2; exit 75; }
  exec heavy "$script_dir/run-mutation.sh" "$@"
fi
# Stage0 copies every platform reference (about 64 MB) into malloc'd memory per program. A fixed mmap threshold gives each
# image its own mapping, returned to the OS when freed, instead of fragmenting glibc arenas; fewer arenas cap the rest.
export MALLOC_MMAP_THRESHOLD_=${MALLOC_MMAP_THRESHOLD_:-131072} MALLOC_ARENA_MAX=${MALLOC_ARENA_MAX:-4}
mode=${1:-help}
[ $# -gt 0 ] && shift
tool="$script_dir/bin/Release/net11.0/vib-mutation.dll"
build() {
  if [ "${MUTATION_SKIP_BUILD:-0}" = "1" ] && [ -f "$tool" ]; then return; fi
  dotnet build --disable-build-servers "$repo/src/vib-self/VibraniumSelfCompiler.csproj" -c Release -v:q -nologo
  dotnet build --disable-build-servers "$script_dir/Vibranium.Mutation.csproj" -c Release -v:q -nologo
}
case "$mode" in
  campaign) build; exec dotnet "$tool" --root "$repo" "$@" ;;
  ci)
    build
    output=$(mktemp -d "${TMPDIR:-/tmp}/vib-mutation-ci.XXXXXX")
    rmdir "$output"
    status=0
    dotnet "$tool" --root "$repo" --ci --output "$output" "$@" || status=$?
    [ "$status" -eq 0 ] && rm -rf "$output" || echo "CI results kept in $output" >&2
    exit "$status" ;;
  report) build; results=$1; shift; exec dotnet "$tool" --root "$repo" --report "$results" "$@" ;;
  asan) build; results=$1; shift; exec dotnet "$tool" --root "$repo" --asan "$results" "$@" ;;
  check) build; exec dotnet "$tool" --root "$repo" --check "$@" ;;
  self-test) build; exec dotnet "$tool" --root "$repo" --self-test "$@" ;;
  *) dotnet "$tool" --help 2>/dev/null || sed -n 2,8p "$0"; exit 64 ;;
esac
