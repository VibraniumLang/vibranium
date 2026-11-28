namespace Vibranium.Std;

// T0: the only trusted platform layer. Declarations only; scalar and byte/int-span parameters only.
// Results: >= 0 success, < 0 is -(int)IoErrorKind. Managed bodies: Sys.Managed.cs (skipped by vib).
// Native bodies: Native/x86_64-linux-gnu/vib_std.ll. The set below IS the hosted-ABI catalog v2 (17 leaves).
public static partial class Sys
{
    [Native("__vib_sys_file_open")]           internal static partial int FileOpen(global::System.ReadOnlySpan<byte> path, int mode);
    [Native("__vib_sys_file_read")]           internal static partial long FileRead(int handle, global::System.Span<byte> buffer);
    [Native("__vib_sys_file_write_all")]      internal static partial int FileWriteAll(int handle, global::System.ReadOnlySpan<byte> data);
    [Native("__vib_sys_file_sync")]           internal static partial int FileSync(int handle);
    [Native("__vib_sys_file_exists")]         internal static partial int FileExists(global::System.ReadOnlySpan<byte> path);
    [Native("__vib_sys_close")]               internal static partial void Close(int handle);
    [Native("__vib_sys_stdin_read")]          internal static partial long StdinRead(global::System.Span<byte> buffer);

    [Native("__vib_sys_tcp_listen")]          internal static partial int TcpListen(int address, int port, int backlog);
    [Native("__vib_sys_tcp_accept")]          internal static partial int TcpAccept(int listener);
    [Native("__vib_sys_tcp_connect")]         internal static partial int TcpConnect(int address, int port);
    [Native("__vib_sys_sock_recv")]           internal static partial long SockRecv(int handle, global::System.Span<byte> buffer);
    [Native("__vib_sys_sock_send_all")]       internal static partial int SockSendAll(int handle, global::System.ReadOnlySpan<byte> data);
    [Native("__vib_sys_sock_shutdown_write")] internal static partial int SockShutdownWrite(int handle);
    [Native("__vib_sys_sock_local_port")]     internal static partial int SockLocalPort(int handle);

    [Native("__vib_sys_spawn_wait")]          internal static partial int SpawnWait(global::System.ReadOnlySpan<byte> arena, global::System.ReadOnlySpan<int> starts);

    [Native("__vib_sys_arg_count")]           internal static partial int ArgCount();
    [Native("__vib_sys_arg_copy")]            internal static partial int ArgCopy(int index, global::System.Span<byte> destination);
}
