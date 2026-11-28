use std::env;
use std::io::{Read, Write};
use std::net::{Shutdown, TcpListener, TcpStream};

const MAX_HEADER_BYTES: usize = 16384;

struct ServerConfig {
    port: u16,
    max_connections: i64,
}

struct HttpOutcome {
    status: u16,
    head_only: bool,
}

fn main() {
    let args: Vec<String> = env::args().skip(1).collect();
    let Some(config) = try_parse_args(&args) else {
        eprint!("usage: http-server [port] [connection-limit]\n");
        std::process::exit(2);
    };

    let listener = match TcpListener::bind(("127.0.0.1", config.port)) {
        Ok(l) => l,
        Err(e) => {
            eprintln!("http-server: bind: {e}");
            std::process::exit(1);
        }
    };
    let actual_port = listener.local_addr().map(|a| a.port()).unwrap_or(config.port);
    println!("listening on 127.0.0.1:{actual_port}");
    let _ = std::io::stdout().flush();

    let mut request_buf = Vec::with_capacity(2048);
    let mut body_buf = Vec::with_capacity(256);
    let mut response_buf = Vec::with_capacity(512);
    let mut served: i64 = 0;

    while config.max_connections < 0 || served < config.max_connections {
        match listener.accept() {
            Ok((mut stream, _)) => {
                served += 1;
                handle_connection(
                    &mut stream,
                    served,
                    &mut request_buf,
                    &mut body_buf,
                    &mut response_buf,
                );
            }
            Err(e) => {
                eprintln!("http-server: accept: {e}");
                std::process::exit(1);
            }
        }
    }
}

fn try_parse_args(args: &[String]) -> Option<ServerConfig> {
    let port = match args.first() {
        Some(raw) => raw.parse::<u16>().ok()?,
        None => 8080,
    };
    let max_connections = match args.get(1) {
        Some(raw) => raw.parse::<i64>().ok()?,
        None => -1,
    };
    Some(ServerConfig { port, max_connections })
}

fn handle_connection(
    stream: &mut TcpStream,
    serial: i64,
    request_buf: &mut Vec<u8>,
    body_buf: &mut Vec<u8>,
    response_buf: &mut Vec<u8>,
) {
    let head_len = read_request_head(stream, request_buf);
    if head_len == 0 {
        return;
    }

    body_buf.clear();
    let outcome = if head_len < 0 {
        HttpOutcome { status: 431, head_only: false }
    } else {
        route_request(&request_buf[..head_len as usize], serial, body_buf)
    };

    if outcome.status != 200 {
        body_buf.extend_from_slice(reason_phrase(outcome.status));
        body_buf.push(b'\n');
    }

    build_response(&outcome, body_buf, response_buf);
    if stream.write_all(response_buf).is_ok() {
        let _ = stream.shutdown(Shutdown::Write);
    }
}

fn read_request_head(stream: &mut TcpStream, buf: &mut Vec<u8>) -> isize {
    buf.clear();
    loop {
        let old = buf.len();
        if old >= MAX_HEADER_BYTES {
            return -1;
        }
        let want = (MAX_HEADER_BYTES - old).min(1024);
        buf.resize(old + want, 0);
        let n = match stream.read(&mut buf[old..old + want]) {
            Ok(n) => n,
            Err(_) => return 0,
        };
        buf.truncate(old + n);
        if n == 0 {
            return 0;
        }
        let scan_from = old.saturating_sub(3);
        if let Some(end_at) = find_subslice(&buf[scan_from..], b"\r\n\r\n") {
            return (scan_from + end_at + 4) as isize;
        }
    }
}

fn route_request(head: &[u8], serial: i64, body: &mut Vec<u8>) -> HttpOutcome {
    let Some(line_end) = find_subslice(head, b"\r\n").filter(|&i| i > 0) else {
        return HttpOutcome { status: 400, head_only: false };
    };
    let request_line = &head[..line_end];
    let headers = &head[line_end + 2..];

    let Some((method, target, version)) = parse_request_line(request_line) else {
        return HttpOutcome { status: 400, head_only: false };
    };

    let head_only = method == b"HEAD";
    let is_http11 = version == b"HTTP/1.1";
    if !is_http11 && version != b"HTTP/1.0" {
        return HttpOutcome { status: 505, head_only };
    }
    if !validate_headers(headers, is_http11) {
        return HttpOutcome { status: 400, head_only };
    }
    if !head_only && method != b"GET" {
        return HttpOutcome { status: 405, head_only: false };
    }

    match target {
        b"/" => {
            body.extend_from_slice(b"hello from vibranium\n");
            HttpOutcome { status: 200, head_only }
        }
        b"/count" => {
            let _ = write!(body, "connection {serial}\n");
            HttpOutcome { status: 200, head_only }
        }
        _ => HttpOutcome { status: 404, head_only },
    }
}

fn parse_request_line(line: &[u8]) -> Option<(&[u8], &[u8], &[u8])> {
    let sp1 = line.iter().position(|&b| b == b' ').filter(|&i| i > 0)?;
    let sp2 = line[sp1 + 1..].iter().position(|&b| b == b' ').filter(|&i| i > 0)?;
    Some((&line[..sp1], &line[sp1 + 1..sp1 + 1 + sp2], &line[sp1 + sp2 + 2..]))
}

fn validate_headers(mut headers: &[u8], require_host: bool) -> bool {
    let mut host_count = 0usize;
    while headers.len() >= 2 {
        let Some(eol) = find_subslice(headers, b"\r\n").filter(|&i| i > 0) else { break };
        let line = &headers[..eol];
        headers = &headers[eol + 2..];

        let Some(colon) = line.iter().position(|&b| b == b':').filter(|&i| i > 0) else {
            return false;
        };
        let name = &line[..colon];
        let val = trim_ascii(&line[colon + 1..]);
        if name.eq_ignore_ascii_case(b"Host") {
            if val.is_empty() {
                return false;
            }
            host_count += 1;
        } else if name.eq_ignore_ascii_case(b"Transfer-Encoding") {
            return false;
        } else if name.eq_ignore_ascii_case(b"Content-Length") && val != b"0" {
            return false;
        }
    }
    !require_host || host_count == 1
}

fn build_response(outcome: &HttpOutcome, body: &[u8], response: &mut Vec<u8>) {
    let reason = std::str::from_utf8(reason_phrase(outcome.status)).unwrap_or("");
    response.clear();
    let _ = write!(
        response,
        "HTTP/1.1 {} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
        outcome.status,
        body.len()
    );
    if !outcome.head_only {
        response.extend_from_slice(body);
    }
}

fn find_subslice(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    if needle.is_empty() || haystack.len() < needle.len() {
        return None;
    }
    haystack.windows(needle.len()).position(|w| w == needle)
}

fn trim_ascii(mut s: &[u8]) -> &[u8] {
    while let Some((&b, rest)) = s.split_first() {
        if b == b' ' || b == b'\t' {
            s = rest;
        } else {
            break;
        }
    }
    while let Some((&b, rest)) = s.split_last() {
        if b == b' ' || b == b'\t' {
            s = rest;
        } else {
            break;
        }
    }
    s
}

fn reason_phrase(status: u16) -> &'static [u8] {
    match status {
        200 => b"OK",
        400 => b"Bad Request",
        404 => b"Not Found",
        405 => b"Method Not Allowed",
        431 => b"Request Header Fields Too Large",
        _ => b"HTTP Version Not Supported",
    }
}
