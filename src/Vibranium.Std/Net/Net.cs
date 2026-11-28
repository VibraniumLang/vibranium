namespace Vibranium.Std;

public readonly struct Ipv4Address
{
    public readonly byte A;
    public readonly byte B;
    public readonly byte C;
    public readonly byte D;

    public Ipv4Address(byte a, byte b, byte c, byte d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }

    public static Ipv4Address Loopback() => new Ipv4Address(127, 0, 0, 1);
    public static Ipv4Address Any() => new Ipv4Address(0, 0, 0, 0);

    public static Option<Ipv4Address> TryParse(global::System.ReadOnlySpan<byte> text)
    {
        int n = text.Length;
        int i = 0;
        System.Span<byte> octets = [0, 0, 0, 0];
        for (int part = 0; part < 4; part++)
        {
            if (part > 0)
            {
                if (i >= n || text[i] != (byte)'.') return Prelude.None;
                i += 1;
            }
            int start = i;
            int val = 0;
            while (i < n && text[i] >= (byte)'0' && text[i] <= (byte)'9')
            {
                if (i - start >= 3) return Prelude.None;
                val = val * 10 + (text[i] - (byte)'0');
                i += 1;
            }
            int digits = i - start;
            if (digits == 0 || val > 255) return Prelude.None;
            if (digits > 1 && text[start] == (byte)'0') return Prelude.None;
            octets[part] = (byte)val;
        }
        if (i != n) return Prelude.None;
        return Prelude.Some(new Ipv4Address(octets[0], octets[1], octets[2], octets[3]));
    }

    public static Option<Ipv4Address> TryParse(string text) => TryParse(text.AsUtf8());

    internal readonly int Bits() => (A << 24) | (B << 16) | (C << 8) | D;
}

public readonly struct Ipv4EndPoint
{
    public readonly Ipv4Address Address;
    public readonly int Port;

    public Ipv4EndPoint(Ipv4Address address, int port)
    {
        Address = address;
        Port = port;
    }

    internal readonly bool IsValid => Port >= 0 && Port <= 65535;
}

public sealed class TcpListener : global::System.IDisposable
{
    private readonly int _handle;

    private TcpListener(int handle)
    {
        _handle = handle;
    }

    public static Result<TcpListener, IoError> Bind(Ipv4EndPoint endPoint) => Bind(endPoint, 128);

    public static Result<TcpListener, IoError> Bind(Ipv4EndPoint endPoint, int backlog)
    {
        if (!endPoint.IsValid || backlog < 0) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Bind));
        int handle = Sys.TcpListen(endPoint.Address.Bits(), endPoint.Port, backlog);
        if (handle < 0) return Prelude.Err(IoError.FromCode(handle, IoOp.Bind));
        return Prelude.Ok(new TcpListener(handle));
    }

    public static Result<TcpListener, IoError> Bind(global::System.ReadOnlySpan<byte> address, int port) => Bind(address, port, 128);

    public static Result<TcpListener, IoError> Bind(global::System.ReadOnlySpan<byte> address, int port, int backlog)
    {
        if (Ipv4Address.TryParse(address).IsNone(out var ip))
        {
            return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Bind));
        }
        return Bind(new Ipv4EndPoint(ip, port), backlog);
    }

    public static Result<TcpListener, IoError> Bind(string address, int port) => Bind(address, port, 128);

    public static Result<TcpListener, IoError> Bind(string address, int port, int backlog) =>
        Bind(address.AsUtf8(), port, backlog);

    public Result<TcpStream, IoError> Accept()
    {
        int handle = Sys.TcpAccept(_handle);
        if (handle < 0) return Prelude.Err(IoError.FromCode(handle, IoOp.Accept));
        return Prelude.Ok(new TcpStream(handle));
    }

    public Result<int, IoError> LocalPort()
    {
        int port = Sys.SockLocalPort(_handle);
        if (port < 0) return Prelude.Err(IoError.FromCode(port, IoOp.Query));
        return Prelude.Ok(port);
    }

    public void Dispose()
    {
        Sys.Close(_handle);
    }
}

public sealed class TcpStream : global::System.IDisposable
{
    private readonly int _handle;

    internal TcpStream(int handle)
    {
        _handle = handle;
    }

    public static Result<TcpStream, IoError> Connect(Ipv4EndPoint endPoint)
    {
        if (!endPoint.IsValid) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Connect));
        int handle = Sys.TcpConnect(endPoint.Address.Bits(), endPoint.Port);
        if (handle < 0) return Prelude.Err(IoError.FromCode(handle, IoOp.Connect));
        return Prelude.Ok(new TcpStream(handle));
    }

    public static Result<TcpStream, IoError> Connect(global::System.ReadOnlySpan<byte> address, int port)
    {
        if (Ipv4Address.TryParse(address).IsNone(out var ip))
        {
            return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Connect));
        }
        return Connect(new Ipv4EndPoint(ip, port));
    }

    public static Result<TcpStream, IoError> Connect(string address, int port) =>
        Connect(address.AsUtf8(), port);

    public Result<int, IoError> Read(global::System.Span<byte> buffer)
    {
        if (buffer.Length == 0) return Prelude.Ok(0);
        long n = Sys.SockRecv(_handle, buffer);
        if (n < 0) return Prelude.Err(IoError.FromCode(n, IoOp.Read));
        return Prelude.Ok((int)n);
    }

    public Result<Unit, IoError> WriteAll(global::System.ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return Prelude.Ok();
        int rc = Sys.SockSendAll(_handle, data);
        if (rc < 0) return Prelude.Err(IoError.FromCode(rc, IoOp.Write));
        return Prelude.Ok();
    }

    public Result<Unit, IoError> WriteAll(string data) => WriteAll(data.AsUtf8());

    public Result<Unit, IoError> SendAll(global::System.ReadOnlySpan<byte> data) => WriteAll(data);

    public Result<Unit, IoError> SendAll(string data) => WriteAll(data.AsUtf8());

    public Result<Unit, IoError> ShutdownWrite()
    {
        int rc = Sys.SockShutdownWrite(_handle);
        if (rc < 0) return Prelude.Err(IoError.FromCode(rc, IoOp.Shutdown));
        return Prelude.Ok();
    }

    public void Dispose()
    {
        Sys.Close(_handle);
    }
}
