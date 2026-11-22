#!/usr/bin/env bash
# Vibranium v0.2 differential conformance fuzzer driver (see docs/CONFORMANCE.md).
#
#   eng/conformance/run-conformance.sh [ci|campaign|calibrate|matrix|selfcheck|sdk-crosscheck|features|replay|run|gen-stats] [extra tool options]
#
#   ci              fixed budget (CONF_SEED=1, CONF_COUNT=300), exits 1 when buckets match CONF_FAIL_ON (default: open;
#                   new = unexplained buckets, roots with status "new" and regressions of "fixed" roots)
#   campaign        long campaign (CONF_MINUTES=45, CONF_SEED=<epoch>), never fails; read summary.json/curve.csv
#   calibrate       fixed-seed recall run against eng/conformance/known-roots.json (CONF_SEED=2026, CONF_COUNT=1000)
#   matrix          banned-by-design / cut / decided matrix probes and controls; exits 1 when a row is a violation
#                   or a probe error, or when a decided row waiting on a root that is not fixed fails and its
#                   category matches CONF_FAIL_ON (default: open)
#   selfcheck       oracle trust: in-process vib-self vs process-mode vib-self (check + emit-ll)
#   sdk-crosscheck  oracle trust: in-process Roslyn+Analyzer vs a real `dotnet build` with Vibranium.App.props
#
# Environment: CONF_OUT (output dir, default: a fresh mktemp dir), CONF_WORKERS (default 4),
# CONF_PROFILE (generator profile for ci/campaign/calibrate: default; extended for the Span, Option-slot,
# [Mut]-receiver, UTF-8-view and Std anchors; or activation for call activation, returned views, conditional
# temporaries, float->integer conversions, Std append, the C5-C9 cuts and generated code, with the vib-reject
# expectation oracle; or soundness for loan producer x invalidator x use placement, where every program all
# frontends accept runs under ASan/LSan at O0 and O3; a (seed, index) pair is reproducible within its profile;
# no profile generates the C10-C21 cut forms, and the tool's own scan tags any that appear),
# CONF_SKIP_BUILD=1 (reuse existing Release builds). Generated programs, native binaries and
# .ll files live only in the OS temp directory and are deleted by the tool.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONFIG="Release"
MODE="${1:-ci}"
[[ $# -gt 0 ]] && shift
TOOL="$REPO_ROOT/eng/conformance/bin/$CONFIG/net11.0/vib-conformance.dll"
WORKERS="${CONF_WORKERS:-4}"

build() {
    if [[ "${CONF_SKIP_BUILD:-0}" == "1" && -f "$TOOL" ]]; then
        return
    fi
    dotnet build --disable-build-servers -c "$CONFIG" -nologo -v q "$REPO_ROOT/src/vib-self/VibraniumSelfCompiler.csproj"
    dotnet build --disable-build-servers -c "$CONFIG" -nologo -v q "$REPO_ROOT/eng/conformance/Vibranium.Conformance.csproj"
}

tool() {
    dotnet "$TOOL" "$@" --repo "$REPO_ROOT" --config "$CONFIG"
}

out_dir() {
    if [[ -n "${CONF_OUT:-}" ]]; then
        mkdir -p "$CONF_OUT"
        echo "$CONF_OUT"
    else
        mktemp -d "${TMPDIR:-/tmp}/vib-conformance-out.XXXXXX"
    fi
}

case "$MODE" in
    ci)
        build
        OUT="$(out_dir)"
        echo "conformance output: $OUT"
        status=0
        tool fuzz --seed "${CONF_SEED:-1}" --count "${CONF_COUNT:-300}" --workers "$WORKERS" --out "$OUT" \
            --fail-on "${CONF_FAIL_ON:-open}" --profile "${CONF_PROFILE:-default}" "$@" || status=$?
        tool report --out "$OUT" | tee "$OUT/report.md"
        exit "$status"
        ;;
    campaign)
        build
        OUT="$(out_dir)"
        echo "conformance output: $OUT"
        tool fuzz --seed "${CONF_SEED:-$(date +%s)}" --minutes "${CONF_MINUTES:-45}" --workers "$WORKERS" --out "$OUT" \
            --profile "${CONF_PROFILE:-default}" "$@"
        tool report --out "$OUT" | tee "$OUT/report.md"
        ;;
    calibrate)
        build
        OUT="$(out_dir)"
        echo "conformance output: $OUT"
        tool fuzz --seed "${CONF_SEED:-2026}" --count "${CONF_COUNT:-1000}" --workers "$WORKERS" --out "$OUT" \
            --profile "${CONF_PROFILE:-default}" "$@"
        tool report --out "$OUT" | tee "$OUT/report.md"
        ;;
    matrix)
        build
        tool matrix --fail-on "${CONF_FAIL_ON:-open}" "$@"
        ;;
    selfcheck)
        build
        tool selfcheck-modes --seed "${CONF_SEED:-7}" --count "${CONF_COUNT:-200}" "$@"
        ;;
    sdk-crosscheck)
        build
        DIR="$REPO_ROOT/eng/sdkcheck.tmp"
        LOG="$(mktemp "${TMPDIR:-/tmp}/vib-sdkcheck.XXXXXX.log")"
        trap 'rm -rf "$DIR" "$LOG"' EXIT
        tool sdk-crosscheck --seed "${CONF_SEED:-5}" --count "${CONF_COUNT:-40}" --dir "$DIR"
        # a failing build is expected: many generated programs are invalid on purpose. The repository
        # projects are referenced, not built, so they must already exist in $CONFIG (build() above);
        # ShouldUnsetParentConfigurationAndPlatform=false keeps them in $CONFIG although they are not in the .slnx.
        dotnet build "$DIR/sdkcheck.slnx" -c "$CONFIG" -p:BuildProjectReferences=false -p:ShouldUnsetParentConfigurationAndPlatform=false --disable-build-servers -nologo -v q -m:4 >"$LOG" 2>&1 || true
        tool sdk-compare --dir "$DIR" --log "$LOG"
        ;;
    features|replay|run|gen-stats|fuzz|selfcheck-modes|report|help)
        build
        tool "$MODE" "$@"
        ;;
    *)
        echo "unknown mode: $MODE" >&2
        exit 2
        ;;
esac
