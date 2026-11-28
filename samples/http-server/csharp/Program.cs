using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;

const int MaxHeaderBytes = 16384;

if (!TryParseArgs(args, out ServerConfig config))
{
    Console.Error.Write("usage: http-server [port] [connection-limit]\n");
    return 2;
}

using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
listener.Bind(new IPEndPoint(IPAddress.Loopback, config.Port));
listener.Listen(128);

int actualPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
Console.Out.Write($"listening on 127.0.0.1:{actualPort}\n");
Console.Out.Flush();

byte[] requestBuf = new byte[MaxHeaderBytes];
byte[] bodyBuf = new byte[256];
byte[] responseBuf = new byte[512];
long served = 0;

while (config.MaxConnections < 0 || served < config.MaxConnections)
{
    using Socket conn = listener.Accept();
    served++;
    HandleConnection(conn, served, requestBuf, bodyBuf, responseBuf);
}
return 0;

static bool TryParseArgs(string[] args, out ServerConfig config)
{
    int port = 8080;
    int maxConnections = -1;
    if (args.Length > 0 && (!int.TryParse(args[0], out port) || port < 0 || port > 65535))
    {
        config = default;
        return false;
    }
    if (args.Length > 1 && !int.TryParse(args[1], out maxConnections))
    {
        config = default;
        return false;
    }
    config = new ServerConfig(port, maxConnections);
    return true;
}

static void HandleConnection(Socket conn, long serial, byte[] requestBuf, byte[] bodyBuf, byte[] responseBuf)
{
    int headLen = ReadRequestHead(conn, requestBuf);
    if (headLen == 0) return;

    HttpOutcome outcome = headLen < 0
        ? new HttpOutcome(431, false, 0)
        : RouteRequest(requestBuf.AsSpan(0, headLen), serial, bodyBuf);

    int bodyLen = outcome.BodyLength;
    if (outcome.Status != 200)
    {
        ReadOnlySpan<byte> reason = ReasonPhrase(outcome.Status);
        reason.CopyTo(bodyBuf);
        bodyBuf[reason.Length] = (byte)'\n';
        bodyLen = reason.Length + 1;
    }

    int responseLen = BuildResponse(outcome.Status, outcome.HeadOnly, bodyBuf.AsSpan(0, bodyLen), responseBuf);
    SendAllAndShutdown(conn, responseBuf.AsSpan(0, responseLen));
}

static int ReadRequestHead(Socket conn, byte[] buf)
{
    int total = 0;
    while (true)
    {
        if (total >= MaxHeaderBytes) return -1;
        int want = Math.Min(1024, MaxHeaderBytes - total);
        int n;
        try
        {
            n = conn.Receive(buf.AsSpan(total, want));
        }
        catch (SocketException)
        {
            return 0;
        }
        if (n <= 0) return 0;

        int scanFrom = Math.Max(0, total - 3);
        total += n;
        int endAt = buf.AsSpan(scanFrom, total - scanFrom).IndexOf("\r\n\r\n"u8);
        if (endAt >= 0) return scanFrom + endAt + 4;
    }
}

static HttpOutcome RouteRequest(ReadOnlySpan<byte> head, long serial, byte[] bodyBuf)
{
    int lineEnd = head.IndexOf("\r\n"u8);
    if (lineEnd <= 0) return new HttpOutcome(400, false, 0);

    ReadOnlySpan<byte> requestLine = head.Slice(0, lineEnd);
    ReadOnlySpan<byte> headers = head.Slice(lineEnd + 2);
    int sp1 = requestLine.IndexOf((byte)' ');
    int sp2 = sp1 < 0 ? -1 : requestLine.Slice(sp1 + 1).IndexOf((byte)' ');
    if (sp1 <= 0 || sp2 <= 0) return new HttpOutcome(400, false, 0);

    ReadOnlySpan<byte> method = requestLine.Slice(0, sp1);
    ReadOnlySpan<byte> target = requestLine.Slice(sp1 + 1, sp2);
    ReadOnlySpan<byte> version = requestLine.Slice(sp1 + sp2 + 2);

    bool headOnly = method.SequenceEqual("HEAD"u8);
    bool isHttp11 = version.SequenceEqual("HTTP/1.1"u8);
    if (!isHttp11 && !version.SequenceEqual("HTTP/1.0"u8)) return new HttpOutcome(505, headOnly, 0);
    if (!ValidateHeaders(headers, isHttp11)) return new HttpOutcome(400, headOnly, 0);
    if (!headOnly && !method.SequenceEqual("GET"u8)) return new HttpOutcome(405, false, 0);

    if (target.SequenceEqual("/"u8))
    {
        ReadOnlySpan<byte> msg = "hello from vibranium\n"u8;
        msg.CopyTo(bodyBuf);
        return new HttpOutcome(200, headOnly, msg.Length);
    }
    if (target.SequenceEqual("/count"u8))
    {
        ReadOnlySpan<byte> prefix = "connection "u8;
        prefix.CopyTo(bodyBuf);
        Utf8Formatter.TryFormat(serial, bodyBuf.AsSpan(prefix.Length), out int written);
        bodyBuf[prefix.Length + written] = (byte)'\n';
        return new HttpOutcome(200, headOnly, prefix.Length + written + 1);
    }
    return new HttpOutcome(404, headOnly, 0);
}

static bool ValidateHeaders(ReadOnlySpan<byte> headers, bool requireHost)
{
    int hostCount = 0;
    while (headers.Length >= 2)
    {
        int eol = headers.IndexOf("\r\n"u8);
        if (eol <= 0) break;

        ReadOnlySpan<byte> line = headers.Slice(0, eol);
        headers = headers.Slice(eol + 2);
        int colon = line.IndexOf((byte)':');
        if (colon <= 0) return false;

        ReadOnlySpan<byte> name = line.Slice(0, colon);
        ReadOnlySpan<byte> val = TrimAscii(line.Slice(colon + 1));
        if (AsciiEqualsIgnoreCase(name, "Host"u8))
        {
            if (val.IsEmpty) return false;
            hostCount++;
        }
        else if (AsciiEqualsIgnoreCase(name, "Transfer-Encoding"u8))
        {
            return false;
        }
        else if (AsciiEqualsIgnoreCase(name, "Content-Length"u8) && !val.SequenceEqual("0"u8))
        {
            return false;
        }
    }
    return !requireHost || hostCount == 1;
}

static int BuildResponse(int status, bool headOnly, ReadOnlySpan<byte> body, Span<byte> response)
{
    int pos = 0;
    "HTTP/1.1 "u8.CopyTo(response.Slice(pos));
    pos += 9;
    Utf8Formatter.TryFormat(status, response.Slice(pos), out int stLen);
    pos += stLen;
    response[pos++] = (byte)' ';

    ReadOnlySpan<byte> reason = ReasonPhrase(status);
    reason.CopyTo(response.Slice(pos));
    pos += reason.Length;

    ReadOnlySpan<byte> mid = "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: "u8;
    mid.CopyTo(response.Slice(pos));
    pos += mid.Length;
    Utf8Formatter.TryFormat(body.Length, response.Slice(pos), out int clLen);
    pos += clLen;

    ReadOnlySpan<byte> tail = "\r\nConnection: close\r\n\r\n"u8;
    tail.CopyTo(response.Slice(pos));
    pos += tail.Length;

    if (!headOnly && !body.IsEmpty)
    {
        body.CopyTo(response.Slice(pos));
        pos += body.Length;
    }
    return pos;
}

static void SendAllAndShutdown(Socket conn, ReadOnlySpan<byte> data)
{
    try
    {
        int sent = 0;
        while (sent < data.Length)
        {
            int n = conn.Send(data.Slice(sent));
            if (n <= 0) return;
            sent += n;
        }
        conn.Shutdown(SocketShutdown.Send);
    }
    catch (SocketException)
    {
    }
}

static ReadOnlySpan<byte> TrimAscii(ReadOnlySpan<byte> s)
{
    int start = 0;
    while (start < s.Length && (s[start] == (byte)' ' || s[start] == (byte)'\t')) start++;
    int end = s.Length;
    while (end > start && (s[end - 1] == (byte)' ' || s[end - 1] == (byte)'\t')) end--;
    return s.Slice(start, end - start);
}

static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
{
    if (a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++)
    {
        byte ca = a[i];
        byte cb = b[i];
        if (ca is >= (byte)'A' and <= (byte)'Z') ca = (byte)(ca + 32);
        if (cb is >= (byte)'A' and <= (byte)'Z') cb = (byte)(cb + 32);
        if (ca != cb) return false;
    }
    return true;
}

static ReadOnlySpan<byte> ReasonPhrase(int status) => status switch
{
    200 => "OK"u8,
    400 => "Bad Request"u8,
    404 => "Not Found"u8,
    405 => "Method Not Allowed"u8,
    431 => "Request Header Fields Too Large"u8,
    _ => "HTTP Version Not Supported"u8,
};

readonly record struct ServerConfig(int Port, int MaxConnections);
readonly record struct HttpOutcome(int Status, bool HeadOnly, int BodyLength);
