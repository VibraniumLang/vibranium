# 4-Way HTTP/1.1 Server Sample & Benchmark (`Vibranium.Std` v0.2)

This directory contains four wire-identical single-threaded HTTP/1.1 servers comparing **Vibranium v0.2 (`Vibranium.Std`)** on both .NET 11 and pure LLVM 18 native compilation (zero C files, zero GC) against idiomatic .NET 11 `Socket` C# 15 and Rust `std::net`:

| Implementation | Path | Stack |
|---|---|---|
| **Vibranium (Dual-Target Source)** | `vibranium/Program.cs` + `vibranium/VibraniumHttpServer.csproj` | Top-level Vibranium C# 15 using `Vibranium.Std` (`TcpListener`, `TcpStream`, `StringBuf`, `Bytes`, `Env.Args`, `Stdio`), verified at compile time by `Vibranium.Analyzer` and compiled natively via `vib` + LLVM 18 `-O3` |
| **C# 15 (.NET 11 Socket)** | `csharp/Program.cs` + `csharp/CSharpHttpServer.csproj` | Pre-allocated `byte[]` buffers, `Span<byte>`, `Utf8Formatter`, `System.Net.Sockets.Socket` |
| **Rust (`std::net`)** | `rust/src/main.rs` + `rust/Cargo.toml` | Pre-allocated `Vec<u8>`, `std::net::{TcpListener, TcpStream}`, `cargo build --release` |

## Wire Contract & Ownership Guarantees

All four servers accept `[port] [connection-limit]`, print `listening on 127.0.0.1:<port>\n` immediately on bind, reuse pre-allocated request/body/response buffers across connections instead of allocating them per request, and enforce:

- **Bounded 16 KiB header accumulator** handling arbitrary TCP packet fragmentation across `\r\n\r\n` boundaries (`431 Request Header Fields Too Large` above 16,384 bytes).
- **Strict HTTP/1.1 framing checks**: mandatory single non-empty `Host` header in `HTTP/1.1`, rejection of `Transfer-Encoding` and non-zero `Content-Length` (`400 Bad Request`), and version validation (`505 HTTP Version Not Supported`).
- **Routes & methods**: `GET /` (`"hello from vibranium\n"`), `GET /count` (`"connection <N>\n"`), `HEAD /` (`Content-Length: 21`, empty body), `404 Not Found`, and `405 Method Not Allowed`, all closed cleanly via `Connection: close` + `ShutdownWrite` + deterministic L1 linear `using var stream = conn;` disposal.

## Running the Benchmark

```bash
heavy ./samples/http-server/run-http-benchmark.sh
```

## Measured Results (Linux x86_64 WSL2, LLVM 18 `-O3`, .NET 11.0.100)

| Target | Artifact Size | Cold Start (First `200`) | Throughput (8 workers) | p50 Latency | p99 Latency | Peak RSS (`VmHWM`) |
|---|---:|---:|---:|---:|---:|---:|
| `.NET 11 (Vibranium Source)` | `12,288 B` DLL (+ runtime) | `41.78 ms` | `8,245 req/s` | `934.7 us` | `1,733.2 us` | `41,444 KiB` |
| `.NET 11 (C# 15 Socket)` | `12,288 B` DLL (+ runtime) | `38.07 ms` | `8,866 req/s` | `879.7 us` | `1,436.3 us` | `43,708 KiB` |
| `Rust (std::net -O3)` | `365,760 B` stripped ELF | `20.87 ms` | `8,968 req/s` | `867.7 us` | `1,419.8 us` | `2,108 KiB` |
| **`Vibranium Native (LLVM 18 -O3)`** | **`22,824 B` stripped ELF** | **`20.86 ms`** | **`8,412 req/s`** | **`907.6 us`** | **`1,683.6 us`** | **`1,360 KiB`** |

All 7 wire parity scenarios (including TCP-fragmented `GET /count` and `>16 KiB` `431` rejection) match byte-for-byte across all four targets and pass `clang-18 -fsanitize=address,leak` with 0 errors and 0 leaks.
