// TRUSTED MANAGED ADAPTER (.NET 11 BCL). Skipped by VibraniumFrontend / Gatekeeper; never lowered to MIR.
namespace Vibranium.Std;

public static partial class Sys
{
    private const int Other = -1, NotFound = -2, PermissionDenied = -3, IsADirectory = -5, InvalidInput = -6,
        WriteZero = -9, Interrupted = -10, BrokenPipe = -11, ConnectionRefused = -12, ConnectionReset = -13,
        ConnectionAborted = -14, NotConnected = -15, AddrInUse = -16, AddrNotAvailable = -17, TimedOut = -18;

    private static readonly object s_gate = new();
    private static readonly object s_appendGate = new();
    private static readonly global::System.Collections.Generic.List<object?> s_handles = [null, null, null];
    private static readonly global::System.Collections.Generic.HashSet<int> s_appendHandles = new();
    private static readonly global::System.Collections.Generic.Stack<int> s_free = new();
    private static readonly global::System.Text.UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static byte[][]? s_argBytes;

    private static int Register(object resource, bool isAppend = false)
    {
        lock (s_gate)
        {
            int slot;
            if (s_free.TryPop(out int reused))
            {
                s_handles[reused] = resource;
                slot = reused;
            }
            else
            {
                s_handles.Add(resource);
                slot = s_handles.Count - 1;
            }
            if (isAppend) s_appendHandles.Add(slot);
            else s_appendHandles.Remove(slot);
            return slot;
        }
    }

    private static (global::System.IO.FileStream Stream, bool IsAppend) ResolveFile(int handle)
    {
        lock (s_gate)
        {
            return ((global::System.IO.FileStream)s_handles[handle]!, s_appendHandles.Contains(handle));
        }
    }

    private static T Resolve<T>(int handle) where T : class
    {
        lock (s_gate) { return (T)s_handles[handle]!; }
    }

    private static int MapFile(global::System.Exception e, string path) => e switch
    {
        global::System.IO.FileNotFoundException or global::System.IO.DirectoryNotFoundException => NotFound,
        global::System.UnauthorizedAccessException => global::System.IO.Directory.Exists(path) ? IsADirectory : PermissionDenied,
        global::System.IO.PathTooLongException => InvalidInput,
        global::System.IO.IOException => global::System.IO.Directory.Exists(path) ? IsADirectory : Other,
        _ => 0,
    };

    private static int MapSocket(global::System.Net.Sockets.SocketException e) => e.SocketErrorCode switch
    {
        global::System.Net.Sockets.SocketError.AccessDenied => PermissionDenied,
        global::System.Net.Sockets.SocketError.Interrupted => Interrupted,
        global::System.Net.Sockets.SocketError.Shutdown => BrokenPipe,
        global::System.Net.Sockets.SocketError.ConnectionRefused => ConnectionRefused,
        global::System.Net.Sockets.SocketError.ConnectionReset => ConnectionReset,
        global::System.Net.Sockets.SocketError.ConnectionAborted => ConnectionAborted,
        global::System.Net.Sockets.SocketError.NotConnected => NotConnected,
        global::System.Net.Sockets.SocketError.AddressAlreadyInUse => AddrInUse,
        global::System.Net.Sockets.SocketError.AddressNotAvailable => AddrNotAvailable,
        global::System.Net.Sockets.SocketError.TimedOut => TimedOut,
        _ => Other,
    };

    internal static partial int FileOpen(global::System.ReadOnlySpan<byte> path, int mode)
    {
        if (path.Length == 0 || path.Length > 4095) return InvalidInput;
        string p = s_strictUtf8.GetString(path);
        try
        {
            var fs = mode switch
            {
                1 => new global::System.IO.FileStream(p, global::System.IO.FileMode.Create, global::System.IO.FileAccess.Write, global::System.IO.FileShare.ReadWrite | global::System.IO.FileShare.Delete, 0, global::System.IO.FileOptions.None),
                2 => new global::System.IO.FileStream(p, global::System.IO.FileMode.OpenOrCreate, global::System.IO.FileAccess.Write, global::System.IO.FileShare.ReadWrite | global::System.IO.FileShare.Delete, 0, global::System.IO.FileOptions.None),
                _ => new global::System.IO.FileStream(p, global::System.IO.FileMode.Open, global::System.IO.FileAccess.Read, global::System.IO.FileShare.ReadWrite | global::System.IO.FileShare.Delete, 0, global::System.IO.FileOptions.None),
            };
            if ((global::System.IO.File.GetAttributes(fs.SafeFileHandle) & global::System.IO.FileAttributes.Directory) != 0)
            {
                fs.Dispose();
                return IsADirectory;
            }
            return Register(fs, isAppend: mode == 2);
        }
        catch (global::System.Exception e) when (MapFile(e, p) is var k and < 0) { return k; }
    }

    internal static partial long FileRead(int handle, global::System.Span<byte> buffer)
    {
        try { return Resolve<global::System.IO.FileStream>(handle).Read(buffer); }
        catch (global::System.UnauthorizedAccessException) { return IsADirectory; }
        catch (global::System.NotSupportedException) { return Other; }
        catch (global::System.IO.IOException) { return Other; }
    }

    internal static partial int FileWriteAll(int handle, global::System.ReadOnlySpan<byte> data)
    {
        try
        {
            var (fs, isAppend) = ResolveFile(handle);
            if (isAppend)
            {
                lock (s_appendGate)
                {
                    // Pipes/FIFOs have no seek position: O_APPEND does not make
                    // them seekable. Keep end-of-file positioning for regular files.
                    if (fs.CanSeek) fs.Seek(0, global::System.IO.SeekOrigin.End);
                    fs.Write(data);
                }
            }
            else
            {
                fs.Write(data);
            }
            return 0;
        }
        catch (global::System.NotSupportedException) { return Other; }
        catch (global::System.IO.IOException) { return Other; }
    }

    internal static partial int FileSync(int handle)
    {
        try { Resolve<global::System.IO.FileStream>(handle).Flush(flushToDisk: true); return 0; }
        catch (global::System.IO.IOException) { return Other; }
    }

    internal static partial int FileExists(global::System.ReadOnlySpan<byte> path)
    {
        if (path.Length <= 0 || path.Length > 4095 || path.IndexOf((byte)0) >= 0) return 0;
        try { return global::System.IO.File.Exists(s_strictUtf8.GetString(path)) ? 1 : 0; }
        catch { return 0; }
    }

    internal static partial void Close(int handle)
    {
        object resource;
        lock (s_gate) { resource = s_handles[handle]!; s_handles[handle] = null; s_appendHandles.Remove(handle); s_free.Push(handle); }
        ((global::System.IDisposable)resource).Dispose();
    }

    internal static partial long StdinRead(global::System.Span<byte> buffer)
    {
        if (buffer.Length <= 0) return 0;
        try { return global::System.Console.OpenStandardInput().Read(buffer); }
        catch (global::System.IO.IOException) { return Other; }
        catch (global::System.ObjectDisposedException) { return Other; }
        catch (global::System.NotSupportedException) { return Other; }
    }

    private static global::System.Net.IPEndPoint EndPoint(int address, int port) =>
        new(new global::System.Net.IPAddress([(byte)(address >>> 24), (byte)(address >>> 16), (byte)(address >>> 8), (byte)address]), port);

    internal static partial int TcpListen(int address, int port, int backlog)
    {
        var s = new global::System.Net.Sockets.Socket(global::System.Net.Sockets.AddressFamily.InterNetwork, global::System.Net.Sockets.SocketType.Stream, global::System.Net.Sockets.ProtocolType.Tcp);
        try
        {
            if (global::System.OperatingSystem.IsLinux()) s.SetRawSocketOption(1, 2, [1, 0, 0, 0]);
            else s.SetSocketOption(global::System.Net.Sockets.SocketOptionLevel.Socket, global::System.Net.Sockets.SocketOptionName.ReuseAddress, true);
            s.Bind(EndPoint(address, port));
            s.Listen(backlog);
            return Register(s);
        }
        catch (global::System.Net.Sockets.SocketException e) { s.Dispose(); return MapSocket(e); }
    }

    internal static partial int TcpAccept(int listener)
    {
        try { return Register(Resolve<global::System.Net.Sockets.Socket>(listener).Accept()); }
        catch (global::System.Net.Sockets.SocketException e) { return MapSocket(e); }
    }

    internal static partial int TcpConnect(int address, int port)
    {
        var s = new global::System.Net.Sockets.Socket(global::System.Net.Sockets.AddressFamily.InterNetwork, global::System.Net.Sockets.SocketType.Stream, global::System.Net.Sockets.ProtocolType.Tcp);
        try { s.Connect(EndPoint(address, port)); return Register(s); }
        catch (global::System.Net.Sockets.SocketException e) { s.Dispose(); return MapSocket(e); }
    }

    internal static partial long SockRecv(int handle, global::System.Span<byte> buffer)
    {
        try { return Resolve<global::System.Net.Sockets.Socket>(handle).Receive(buffer, global::System.Net.Sockets.SocketFlags.None); }
        catch (global::System.Net.Sockets.SocketException e) { return MapSocket(e); }
    }

    internal static partial int SockSendAll(int handle, global::System.ReadOnlySpan<byte> data)
    {
        try
        {
            var s = Resolve<global::System.Net.Sockets.Socket>(handle);
            while (data.Length > 0)
            {
                int n = s.Send(data, global::System.Net.Sockets.SocketFlags.None);
                if (n == 0) return WriteZero;
                data = data[n..];
            }
            return 0;
        }
        catch (global::System.Net.Sockets.SocketException e) { return MapSocket(e); }
    }

    internal static partial int SockShutdownWrite(int handle)
    {
        try { Resolve<global::System.Net.Sockets.Socket>(handle).Shutdown(global::System.Net.Sockets.SocketShutdown.Send); return 0; }
        catch (global::System.Net.Sockets.SocketException e) { return MapSocket(e); }
    }

    internal static partial int SockLocalPort(int handle)
    {
        try { return ((global::System.Net.IPEndPoint)Resolve<global::System.Net.Sockets.Socket>(handle).LocalEndPoint!).Port; }
        catch (global::System.Net.Sockets.SocketException e) { return MapSocket(e); }
    }

    internal static partial int SpawnWait(global::System.ReadOnlySpan<byte> arena, global::System.ReadOnlySpan<int> starts)
    {
        if (starts.Length < 1 || arena.Length < 1 || arena[arena.Length - 1] != 0) return InvalidInput;
        var argv = new string[starts.Length];
        for (int i = 0; i < starts.Length; i++)
        {
            int st = starts[i];
            if ((uint)st >= (uint)arena.Length) return InvalidInput;
            global::System.ReadOnlySpan<byte> tail = arena[st..];
            argv[i] = s_strictUtf8.GetString(tail[..tail.IndexOf((byte)0)]);
        }
        try
        {
            return global::System.Diagnostics.Process.Run(argv[0], argv[1..]).ExitCode;
        }
        catch (global::System.ComponentModel.Win32Exception e)
        {
            return e.NativeErrorCode switch
            {
                2 or 20 => NotFound,
                13 => PermissionDenied,
                _ => global::System.IO.Directory.Exists(argv[0]) ? PermissionDenied : (global::System.IO.File.Exists(argv[0]) ? Other : NotFound),
            };
        }
    }

    private static byte[][] ArgumentBytes()
    {
        if (s_argBytes is not null) return s_argBytes;
        string[] managedArgs = global::System.Environment.GetCommandLineArgs();
        int userCount = global::System.Math.Max(0, managedArgs.Length - 1);
        if (userCount == 0)
        {
            return s_argBytes = [];
        }

        if (global::System.OperatingSystem.IsLinux())
        {
            try
            {
                byte[] raw = global::System.IO.File.ReadAllBytes("/proc/self/cmdline");
                if (raw.Length > 0)
                {
                    var parts = new global::System.Collections.Generic.List<byte[]>();
                    int start = 0;
                    for (int i = 0; i < raw.Length; i++)
                    {
                        if (raw[i] == 0)
                        {
                            parts.Add(raw[start..i]);
                            start = i + 1;
                        }
                    }
                    if (start < raw.Length)
                    {
                        parts.Add(raw[start..]);
                    }
                    if (parts.Count >= managedArgs.Length)
                    {
                        var tail = new byte[userCount][];
                        int offset = parts.Count - userCount;
                        for (int i = 0; i < userCount; i++)
                        {
                            tail[i] = parts[offset + i];
                        }
                        return s_argBytes = tail;
                    }
                }
            }
            catch
            {
            }
        }

        var fallback = new byte[userCount][];
        for (int i = 0; i < userCount; i++)
        {
            fallback[i] = global::System.Text.Encoding.UTF8.GetBytes(managedArgs[i + 1]);
        }
        return s_argBytes = fallback;
    }

    internal static partial int ArgCount() => ArgumentBytes().Length;

    internal static partial int ArgCopy(int index, global::System.Span<byte> destination)
    {
        byte[][] args = ArgumentBytes();
        if ((uint)index >= (uint)args.Length || destination.Length < 0) return InvalidInput;
        byte[] bytes = args[index];
        int n = global::System.Math.Min(bytes.Length, destination.Length);
        if (n > 0)
        {
            global::System.MemoryExtensions.AsSpan(bytes, 0, n).CopyTo(destination);
        }
        return bytes.Length;
    }
}
