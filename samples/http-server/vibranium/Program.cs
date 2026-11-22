if (Env.Args().IsErr(out var cliArgs, out _))
{
    Stdio.WriteErr("http-server: failed to read arguments\n");
    return 1;
}
if (TryParseArgs(in cliArgs).IsNone(out var config))
{
    Stdio.WriteErr("usage: http-server [port] [connection-limit]\n");
    return 2;
}

if (TcpListener.Bind(new Ipv4EndPoint(Ipv4Address.Loopback(), config.Port), 128).IsErr(out var bound, out var bindErr))
{
    ReportBindError(bindErr.Error);
    return 1;
}
using var listener = bound;

if (listener.LocalPort().IsErr(out int actualPort, out _)) return 1;
PrintStartupBanner(actualPort);

var requestBuf = new Vec<byte>(2048);
var bodyBuf = new StringBuf(256);
var responseBuf = new StringBuf(512);
long served = 0;

while (config.MaxConnections < 0 || served < config.MaxConnections)
{
    if (listener.Accept().IsErr(out var conn, out _))
    {
        Stdio.WriteErr("http-server: accept failed\n");
        return 1;
    }
    served += 1;
    HandleConnection(conn, served, ref requestBuf, ref bodyBuf, ref responseBuf);
}
return 0;

static Option<ServerConfig> TryParseArgs(in ArgList args)
{
    int port = 8080;
    int maxConnections = -1;
    if (args.Count > 0)
    {
        if (!int.TryParse(args[0], out port) || port < 0 || port > 65535) return None;
    }
    if (args.Count > 1)
    {
        if (!int.TryParse(args[1], out maxConnections)) return None;
    }
    return Some(new ServerConfig(port, maxConnections));
}

static void ReportBindError(IoError error)
{
    var msg = new StringBuf(64);
    msg.Append("http-server: bind: ");
    msg.AppendLine(error.KindName());
    Stdio.WriteErr(msg[..]);
}

static void PrintStartupBanner(int port)
{
    var banner = new StringBuf(48);
    banner.Append("listening on 127.0.0.1:");
    banner.Append(port);
    banner.AppendLine();
    Write(banner[..]);
}

static void HandleConnection(
    TcpStream conn,
    long serial,
    ref Vec<byte> requestBuf,
    ref StringBuf bodyBuf,
    ref StringBuf responseBuf)
{
    using var stream = conn;
    int headLen = ReadRequestHead(in stream, ref requestBuf);
    if (headLen == 0) return;

    bodyBuf.Clear();
    HttpOutcome outcome = headLen < 0
        ? new HttpOutcome(431, false)
        : RouteRequest(requestBuf.ReadOnlySlice(0, headLen), serial, ref bodyBuf);

    if (outcome.Status != 200)
    {
        bodyBuf.AppendLine(ReasonPhrase(outcome.Status));
    }

    BuildResponse(outcome, in bodyBuf, ref responseBuf);
    if (!stream.WriteAll(responseBuf[..]).IsErr(out _, out _))
    {
        _ = stream.ShutdownWrite();
    }
}

static int ReadRequestHead(in TcpStream stream, ref Vec<byte> buf)
{
    const int MaxHeaderBytes = 16384;
    buf.Clear();
    while (true)
    {
        int old = buf.Count;
        if (old >= MaxHeaderBytes) return -1;

        int remaining = MaxHeaderBytes - old;
        int want = remaining < 1024 ? remaining : 1024;
        buf.Resize(old + want, 0);
        if (stream.Read(buf.Slice(old, want)).IsErr(out int n, out _))
        {
            buf.Truncate(old);
            return 0;
        }
        buf.Truncate(old + n);
        if (n == 0) return 0;

        int scanFrom = old > 3 ? old - 3 : 0;
        int total = buf.Count;
        int endAt = buf.ReadOnlySlice(scanFrom, total - scanFrom).IndexOf("\r\n\r\n");
        if (endAt >= 0) return scanFrom + endAt + 4;
    }
}

static HttpOutcome RouteRequest(ReadOnlySpan<byte> head, long serial, ref StringBuf body)
{
    int lineEnd = head.IndexOf("\r\n");
    if (lineEnd <= 0) return new HttpOutcome(400, false);

    ReadOnlySpan<byte> requestLine = head.Slice(0, lineEnd);
    ReadOnlySpan<byte> headers = head.Slice(lineEnd + 2);
    if (ParseRequestLine(requestLine).IsNone(out var spans)) return new HttpOutcome(400, false);

    ReadOnlySpan<byte> method = requestLine.Slice(0, spans.MethodLen);
    ReadOnlySpan<byte> target = requestLine.Slice(spans.TargetStart, spans.TargetLen);
    ReadOnlySpan<byte> version = requestLine.Slice(spans.VersionStart);

    bool headOnly = method.SequenceEqual("HEAD");
    bool isHttp11 = version.SequenceEqual("HTTP/1.1");
    if (!isHttp11 && !version.SequenceEqual("HTTP/1.0")) return new HttpOutcome(505, headOnly);
    if (!ValidateHeaders(headers, isHttp11)) return new HttpOutcome(400, headOnly);
    if (!headOnly && !method.SequenceEqual("GET")) return new HttpOutcome(405, false);

    if (target.SequenceEqual("/"))
    {
        body.Append("hello from vibranium\n");
        return new HttpOutcome(200, headOnly);
    }
    if (target.SequenceEqual("/count"))
    {
        body.Append("connection ");
        body.Append(serial);
        body.AppendLine();
        return new HttpOutcome(200, headOnly);
    }
    return new HttpOutcome(404, headOnly);
}

static Option<LineSpans> ParseRequestLine(ReadOnlySpan<byte> line)
{
    int sp1 = line.IndexOf((byte)' ');
    if (sp1 <= 0) return None;
    int sp2 = line.Slice(sp1 + 1).IndexOf((byte)' ');
    if (sp2 <= 0) return None;
    return Some(new LineSpans(sp1, sp1 + 1, sp2, sp1 + sp2 + 2));
}

static bool ValidateHeaders(ReadOnlySpan<byte> headers, bool requireHost)
{
    int hostCount = 0;
    while (headers.Length >= 2)
    {
        int eol = headers.IndexOf("\r\n");
        if (eol <= 0) break;

        ReadOnlySpan<byte> line = headers.Slice(0, eol);
        headers = headers.Slice(eol + 2);
        int colon = line.IndexOf((byte)':');
        if (colon <= 0) return false;

        ReadOnlySpan<byte> name = line.Slice(0, colon);
        ReadOnlySpan<byte> val = Bytes.TrimAscii(line.Slice(colon + 1));
        if (Bytes.EqIgnoreAsciiCase(name, "Host".AsUtf8()))
        {
            if (val.Length == 0) return false;
            hostCount += 1;
        }
        else if (Bytes.EqIgnoreAsciiCase(name, "Transfer-Encoding".AsUtf8()))
        {
            return false;
        }
        else if (Bytes.EqIgnoreAsciiCase(name, "Content-Length".AsUtf8()) && !val.SequenceEqual("0"))
        {
            return false;
        }
    }
    return !requireHost || hostCount == 1;
}

static void BuildResponse(HttpOutcome outcome, in StringBuf body, ref StringBuf response)
{
    response.Clear();
    response.Append("HTTP/1.1 ");
    response.Append(outcome.Status);
    response.AppendAscii((byte)' ');
    response.Append(ReasonPhrase(outcome.Status));
    response.Append("\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: ");
    response.Append(body.Length);
    response.Append("\r\nConnection: close\r\n\r\n");
    if (!outcome.HeadOnly && body.Length > 0)
    {
        response.Append(body[..]);
    }
}

static ReadOnlySpan<byte> ReasonPhrase(int status) => status switch
{
    200 => "OK".AsUtf8(),
    400 => "Bad Request".AsUtf8(),
    404 => "Not Found".AsUtf8(),
    405 => "Method Not Allowed".AsUtf8(),
    431 => "Request Header Fields Too Large".AsUtf8(),
    _ => "HTTP Version Not Supported".AsUtf8(),
};

readonly struct ServerConfig(int port, int maxConnections)
{
    public readonly int Port = port;
    public readonly int MaxConnections = maxConnections;
}

readonly struct LineSpans(int methodLen, int targetStart, int targetLen, int versionStart)
{
    public readonly int MethodLen = methodLen;
    public readonly int TargetStart = targetStart;
    public readonly int TargetLen = targetLen;
    public readonly int VersionStart = versionStart;
}

readonly struct HttpOutcome(int status, bool headOnly)
{
    public readonly int Status = status;
    public readonly bool HeadOnly = headOnly;
}
