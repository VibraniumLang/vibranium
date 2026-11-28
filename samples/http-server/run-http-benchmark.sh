#!/usr/bin/env bash
set -euo pipefail

BENCH_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${BENCH_DIR}/../.." && pwd)"
DOTNET="${HOME}/.dotnet/dotnet"
if [[ ! -x "${DOTNET}" ]]; then
  DOTNET="dotnet"
fi

OUT_DIR="$(mktemp -d /tmp/vibranium-http-bench.XXXXXX)"
cleanup() {
  rm -rf "${OUT_DIR}"
}
trap cleanup EXIT

echo "Building all 4 HTTP/1.1 servers (.NET 11 Vibranium, .NET 11 C# 15, Rust -O3, Vibranium Native LLVM 18 -O3)..."
"${DOTNET}" build --disable-build-servers "${ROOT}/src/vib/vib.csproj" -c Release -v quiet --nologo
"${DOTNET}" build --disable-build-servers "${BENCH_DIR}/vibranium/VibraniumHttpServer.csproj" -c Release -v quiet --nologo
"${DOTNET}" build --disable-build-servers "${BENCH_DIR}/csharp/CSharpHttpServer.csproj" -c Release -v quiet --nologo

CARGO_TARGET_DIR="${OUT_DIR}/rust-target" cargo build --release --manifest-path "${BENCH_DIR}/rust/Cargo.toml" --quiet
cp "${OUT_DIR}/rust-target/release/vibranium-vs-rust-http-server" "${OUT_DIR}/http-rust"
rm -rf "${OUT_DIR}/rust-target"
cp "${OUT_DIR}/http-rust" "${OUT_DIR}/http-rust-stripped"
strip "${OUT_DIR}/http-rust-stripped"

VIB_DLL="${ROOT}/src/vib/bin/Release/net11.0/vib.dll"
"${DOTNET}" "${VIB_DLL}" build "${BENCH_DIR}/vibranium/Program.cs" -O3 -o "${OUT_DIR}/http-vibranium-native" >/dev/null
cp "${OUT_DIR}/http-vibranium-native" "${OUT_DIR}/http-vibranium-stripped"
strip "${OUT_DIR}/http-vibranium-stripped"

"${DOTNET}" "${VIB_DLL}" build "${BENCH_DIR}/vibranium/Program.cs" -O2 --asan -o "${OUT_DIR}/http-vibranium-asan" >/dev/null

cat > "${OUT_DIR}/loadgen.rs" << 'RS'
use std::env;
use std::fs;
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{Ipv4Addr, SocketAddr, SocketAddrV4, TcpStream};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::Instant;

const REQ: &[u8] = b"GET / HTTP/1.1\r\nHost: localhost\r\n\r\n";

fn read_vm_hwm_kib(pid: u32) -> u64 {
    if let Ok(status) = fs::read_to_string(format!("/proc/{pid}/status")) {
        for line in status.lines() {
            if let Some(rest) = line.strip_prefix("VmHWM:") {
                if let Some(num) = rest.split_whitespace().next() {
                    if let Ok(v) = num.parse::<u64>() {
                        return v;
                    }
                }
            }
        }
    }
    0
}

fn spawn_ready(bin: &str, limit: usize) -> (std::process::Child, u16) {
    let mut child = Command::new(bin)
        .arg("0")
        .arg(limit.to_string())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .expect("spawn failed");
    let stdout = child.stdout.take().unwrap();
    let mut reader = BufReader::new(stdout);
    let mut line = String::new();
    reader.read_line(&mut line).expect("read ready banner");
    let port_str = line
        .trim()
        .strip_prefix("listening on 127.0.0.1:")
        .unwrap_or_else(|| panic!("unexpected banner: {line:?}"));
    let port: u16 = port_str.parse().expect("port parse");
    drop(reader);
    (child, port)
}

fn one_request(addr: SocketAddr) {
    let mut s = TcpStream::connect(addr).expect("connect");
    let _ = s.set_nodelay(true);
    s.write_all(REQ).expect("write");
    let mut buf = [0u8; 256];
    let mut total = 0usize;
    while total < 123 {
        let n = s.read(&mut buf[total..]).expect("read");
        if n == 0 {
            break;
        }
        total += n;
    }
    assert!(buf[..total].starts_with(b"HTTP/1.1 200 OK\r\n"));
}

fn main() {
    let args: Vec<String> = env::args().skip(1).collect();
    let label = &args[0];
    let bin = &args[1];

    // 1. Cold-start time-to-first-200 (median of 15 runs: spawn -> ready -> first 200 OK received)
    let mut cold_samples = Vec::with_capacity(15);
    for _ in 0..15 {
        let t0 = Instant::now();
        let (mut child, port) = spawn_ready(bin, 1);
        let addr = SocketAddr::V4(SocketAddrV4::new(Ipv4Addr::LOCALHOST, port));
        one_request(addr);
        let first_200_ms = t0.elapsed().as_secs_f64() * 1000.0;
        let st = child.wait().expect("wait");
        assert!(st.success());
        cold_samples.push(first_200_ms);
    }
    cold_samples.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let cold_ms = cold_samples[cold_samples.len() / 2];

    // 2. Saturated loopback HTTP/1.1 throughput (8 concurrent client threads, 8,000 requests), p50/p99 latency & Peak RSS
    let warmup = 800usize;
    let total_reqs = 8000usize;
    let workers = 8usize;
    let (mut child, port) = spawn_ready(bin, warmup + total_reqs);
    let pid = child.id();
    let addr = SocketAddr::V4(SocketAddrV4::new(Ipv4Addr::LOCALHOST, port));

    for _ in 0..warmup {
        one_request(addr);
    }
    let mut peak_rss_kib = read_vm_hwm_kib(pid);

    let counter = Arc::new(AtomicUsize::new(0));
    let mid_rss = Arc::new(AtomicUsize::new(0));
    let t_bench = Instant::now();
    let mut handles = Vec::with_capacity(workers);
    for _ in 0..workers {
        let cnt = Arc::clone(&counter);
        let mrss = Arc::clone(&mid_rss);
        handles.push(thread::spawn(move || {
            let mut local_lats = Vec::with_capacity(total_reqs / workers + 8);
            loop {
                let idx = cnt.fetch_add(1, Ordering::Relaxed);
                if idx >= total_reqs {
                    break;
                }
                if idx == total_reqs / 2 {
                    mrss.store(read_vm_hwm_kib(pid) as usize, Ordering::Relaxed);
                }
                let r0 = Instant::now();
                one_request(addr);
                local_lats.push(r0.elapsed().as_secs_f64() * 1_000_000.0);
            }
            local_lats
        }));
    }

    let mut lats_us = Vec::with_capacity(total_reqs);
    for h in handles {
        lats_us.extend(h.join().expect("worker join"));
    }
    let elapsed = t_bench.elapsed().as_secs_f64();
    peak_rss_kib = peak_rss_kib.max(mid_rss.load(Ordering::Relaxed) as u64).max(read_vm_hwm_kib(pid));
    let st = child.wait().expect("wait");
    assert!(st.success());

    lats_us.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let p50 = lats_us[total_reqs / 2];
    let p99 = lats_us[(total_reqs * 99) / 100];
    let rps = (total_reqs as f64) / elapsed;

    println!(
        "{:<26} | {:>8.2} ms | {:>9.0} req/s | {:>6.1} us | {:>6.1} us | {:>7} KiB",
        label, cold_ms, rps, p50, p99, peak_rss_kib
    );
}
RS
rustc --edition=2021 -O "${OUT_DIR}/loadgen.rs" -o "${OUT_DIR}/loadgen"

DOTNET_VIB_BIN="${BENCH_DIR}/vibranium/bin/Release/net11.0/VibraniumHttpServer"
DOTNET_CS_BIN="${BENCH_DIR}/csharp/bin/Release/net11.0/CSharpHttpServer"

echo ""
echo "=== 1. Binary Footprint ==="
printf ".NET 11 (Vibranium Source) DLL    : %8s bytes (+ ~75 MB .NET 11 runtime)\n" "$(stat -c%s "${DOTNET_VIB_BIN}.dll")"
printf ".NET 11 (C# 15 Socket) DLL   : %8s bytes (+ ~75 MB .NET 11 runtime)\n" "$(stat -c%s "${DOTNET_CS_BIN}.dll")"
printf "Rust (std::net -O3)          : %8s bytes (%s bytes stripped)\n" "$(stat -c%s "${OUT_DIR}/http-rust")" "$(stat -c%s "${OUT_DIR}/http-rust-stripped")"
printf "Vibranium Native (LLVM 18 -O3)    : %8s bytes (%s bytes stripped)\n" "$(stat -c%s "${OUT_DIR}/http-vibranium-native")" "$(stat -c%s "${OUT_DIR}/http-vibranium-stripped")"

python3 - "${DOTNET_VIB_BIN}" "${DOTNET_CS_BIN}" "${OUT_DIR}/http-rust-stripped" "${OUT_DIR}/http-vibranium-stripped" "${OUT_DIR}/http-vibranium-asan" << 'PY'
import os
import re
import socket
import subprocess
import sys
import time

dotnet_vib, dotnet_cs, rust_bin, vib_native, vib_asan = sys.argv[1:6]

TARGETS = [
    (".NET 11 (Vibranium Source)", dotnet_vib),
    (".NET 11 (C# 15 Socket)", dotnet_cs),
    ("Rust (std::net -O3)", rust_bin),
    ("Vibranium Native (LLVM 18 -O3)", vib_native),
]

READY_RE = re.compile(rb"^listening on 127\.0\.0\.1:(\d+)\n$")


def start_server(bin_path: str, limit: int, env_extra=None):
    env = os.environ.copy()
    if env_extra:
        env.update(env_extra)
    proc = subprocess.Popen(
        [bin_path, "0", str(limit)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        env=env,
    )
    line = proc.stdout.readline()
    m = READY_RE.match(line)
    if not m:
        err = proc.stderr.read().decode("utf-8", errors="replace")
        proc.kill()
        raise RuntimeError(f"Failed to start {bin_path}: stdout={line!r}, stderr={err}")
    return proc, int(m.group(1))


def send_raw(port: int, chunks: list[bytes], delay_s: float = 0.0) -> bytes:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(5.0)
        s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        s.connect(("127.0.0.1", port))
        for idx, ch in enumerate(chunks):
            if idx > 0 and delay_s > 0.0:
                time.sleep(delay_s)
            try:
                s.sendall(ch)
            except BrokenPipeError:
                break
        out = bytearray()
        while True:
            try:
                data = s.recv(4096)
            except ConnectionResetError:
                break
            if not data:
                break
            out.extend(data)
        return bytes(out)


def run_parity_suite(bin_path: str, env_extra=None) -> list[bytes]:
    scenarios = [
        # 1. GET /
        ([b"GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"], 0.0),
        # 2. TCP-fragmented GET /count splitting request-line, header, and \r\n\r\n across reads
        ([b"GET /cou", b"nt HTTP/1.1\r\nHo", b"st: localh", b"ost\r\n\r", b"\n"], 0.002),
        # 3. HEAD / (Content-Length: 21, empty body)
        ([b"HEAD / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"], 0.0),
        # 4. 404 Not Found
        ([b"GET /missing HTTP/1.1\r\nHost: localhost\r\n\r\n"], 0.0),
        # 5. 405 Method Not Allowed
        ([b"POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"], 0.0),
        # 6. 400 Bad Request (HTTP/1.1 missing Host)
        ([b"GET / HTTP/1.1\r\nUser-Agent: bench\r\n\r\n"], 0.0),
        # 7. 431 Request Header Fields Too Large (> 16 KiB without \r\n\r\n)
        ([b"GET / HTTP/1.1\r\nHost: localhost\r\nX-Pad: " + (b"a" * 16500)], 0.0),
    ]
    proc, port = start_server(bin_path, len(scenarios), env_extra=env_extra)
    responses = []
    for chunks, delay in scenarios:
        responses.append(send_raw(port, chunks, delay_s=delay))
    rc = proc.wait(timeout=5.0)
    stderr = proc.stderr.read().decode("utf-8", errors="replace")
    if rc != 0 or stderr:
        raise RuntimeError(f"{bin_path} exited rc={rc}, stderr={stderr}")
    return responses


print("\n=== 2. 7-Scenario HTTP/1.1 Wire Parity & ASan/LSan Verification ===")
baseline = None
for label, path in TARGETS:
    resps = run_parity_suite(path)
    if baseline is None:
        baseline = resps
    else:
        for idx, (exp, got) in enumerate(zip(baseline, resps), 1):
            if exp != got:
                raise AssertionError(
                    f"Mismatch on scenario #{idx} for {label}:\n  expected: {exp!r}\n  actual:   {got!r}"
                )
    print(f"  [OK] {label:<26} matched all 7 HTTP/1.1 wire responses byte-for-byte")

asan_resps = run_parity_suite(vib_asan, env_extra={"ASAN_OPTIONS": "detect_leaks=1:abort_on_error=1"})
assert asan_resps == baseline, "ASan responses diverged from baseline"
print("  [OK] Vibranium Native (-fsanitize=address,leak) passed all 7 scenarios with 0 leaks / 0 errors")
PY

echo ""
echo "=== 3. Cold-Start Time-to-First-200, Throughput, Latency & Server Peak RSS ==="
echo "Target                     | Cold Start |    Throughput |   p50 Lat |   p99 Lat |    Peak RSS"
echo "---------------------------------------------------------------------------------------------"
"${OUT_DIR}/loadgen" ".NET 11 (Vibranium Source)"     "${DOTNET_VIB_BIN}"
"${OUT_DIR}/loadgen" ".NET 11 (C# 15 Socket)"    "${DOTNET_CS_BIN}"
"${OUT_DIR}/loadgen" "Rust (std::net -O3)"       "${OUT_DIR}/http-rust-stripped"
"${OUT_DIR}/loadgen" "Vibranium Native (LLVM 18 -O3)" "${OUT_DIR}/http-vibranium-stripped"
