namespace Vibranium.Std;

public sealed class File : global::System.IDisposable
{
    private readonly int _handle;

    private File(int handle)
    {
        _handle = handle;
    }

    public static Result<File, IoError> OpenRead(global::System.ReadOnlySpan<byte> path) => Open(path, 0);

    public static Result<File, IoError> OpenRead(string path) => Open(path.AsUtf8(), 0);

    public static Result<File, IoError> Create(global::System.ReadOnlySpan<byte> path) => Open(path, 1);

    public static Result<File, IoError> Create(string path) => Open(path.AsUtf8(), 1);

    public static Result<File, IoError> OpenAppend(global::System.ReadOnlySpan<byte> path) => Open(path, 2);

    public static Result<File, IoError> OpenAppend(string path) => Open(path.AsUtf8(), 2);

    public static bool Exists(global::System.ReadOnlySpan<byte> path)
    {
        var normalized = new StringBuf(0);
        if (!PathText.Normalize(path, ref normalized)) return false;
        return Sys.FileExists(normalized.AsUtf8()) == 1;
    }

    public static bool Exists(string path) => Exists(path.AsUtf8());

    public static Result<Vec<byte>, IoError> ReadAllBytes(global::System.ReadOnlySpan<byte> path) => ReadAllBytes(path, 67_108_864);

    public static Result<Vec<byte>, IoError> ReadAllBytes(global::System.ReadOnlySpan<byte> path, int maxBytes)
    {
        if (maxBytes < 0) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Read));
        if (OpenRead(path).IsErr(out var opened, out var openError)) return openError;
        using var file = opened;
        var data = new Vec<byte>();
        int chunk = 4096;
        while (true)
        {
            int old = data.Count;
            int room = maxBytes - old;
            int want = room < chunk ? room + 1 : chunk;
            data.Resize(old + want, 0);
            if (file.Read(data.AsSpan().Slice(old, want)).IsErr(out var n, out var readError)) return readError;
            data.Truncate(old + n);
            if (n == 0) break;
            if (data.Count > maxBytes) return Prelude.Err(new IoError(IoErrorKind.FileTooLarge, IoOp.Read));
            if (chunk < 1048576) chunk *= 2;
        }
        return Prelude.Ok(data);
    }

    public static Result<Vec<byte>, IoError> ReadAllBytes(string path) => ReadAllBytes(path, 67_108_864);

    public static Result<Vec<byte>, IoError> ReadAllBytes(string path, int maxBytes) =>
        ReadAllBytes(path.AsUtf8(), maxBytes);

    public static Result<StringBuf, IoError> ReadAllText(global::System.ReadOnlySpan<byte> path) => ReadAllText(path, 67_108_864);

    public static Result<StringBuf, IoError> ReadAllText(global::System.ReadOnlySpan<byte> path, int maxBytes)
    {
        if (ReadAllBytes(path, maxBytes).IsErr(out var data, out var err)) return err;
        if (!Utf8.IsValid(data.AsReadOnlySpan())) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Read));
        return Prelude.Ok(new StringBuf(data));
    }

    public static Result<StringBuf, IoError> ReadAllText(string path) => ReadAllText(path, 67_108_864);

    public static Result<StringBuf, IoError> ReadAllText(string path, int maxBytes) =>
        ReadAllText(path.AsUtf8(), maxBytes);

    public static Result<Unit, IoError> WriteAllBytes(global::System.ReadOnlySpan<byte> path, global::System.ReadOnlySpan<byte> data)
    {
        if (Create(path).IsErr(out var created, out var createError)) return createError;
        using var file = created;
        return file.WriteAll(data);
    }

    public static Result<Unit, IoError> WriteAllBytes(string path, global::System.ReadOnlySpan<byte> data) =>
        WriteAllBytes(path.AsUtf8(), data);

    public static Result<Unit, IoError> AppendAllBytes(global::System.ReadOnlySpan<byte> path, global::System.ReadOnlySpan<byte> data)
    {
        if (OpenAppend(path).IsErr(out var opened, out var openError)) return openError;
        using var file = opened;
        return file.WriteAll(data);
    }

    public static Result<Unit, IoError> AppendAllBytes(string path, global::System.ReadOnlySpan<byte> data) =>
        AppendAllBytes(path.AsUtf8(), data);

    public static Result<Unit, IoError> WriteAllText(global::System.ReadOnlySpan<byte> path, global::System.ReadOnlySpan<byte> text)
    {
        if (!Utf8.IsValid(text)) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Write));
        return WriteAllBytes(path, text);
    }

    public static Result<Unit, IoError> WriteAllText(string path, global::System.ReadOnlySpan<byte> text) =>
        WriteAllText(path.AsUtf8(), text);

    public static Result<Unit, IoError> WriteAllText(global::System.ReadOnlySpan<byte> path, string text) =>
        WriteAllBytes(path, text.AsUtf8());

    public static Result<Unit, IoError> WriteAllText(string path, string text) =>
        WriteAllBytes(path.AsUtf8(), text.AsUtf8());

    public static Result<Unit, IoError> WriteAllText(global::System.ReadOnlySpan<byte> path, in StringBuf text) =>
        WriteAllBytes(path, text.AsUtf8());

    public static Result<Unit, IoError> WriteAllText(string path, in StringBuf text) =>
        WriteAllBytes(path.AsUtf8(), text.AsUtf8());

    public static Result<Unit, IoError> AppendAllText(global::System.ReadOnlySpan<byte> path, global::System.ReadOnlySpan<byte> text)
    {
        if (!Utf8.IsValid(text)) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Write));
        return AppendAllBytes(path, text);
    }

    public static Result<Unit, IoError> AppendAllText(string path, global::System.ReadOnlySpan<byte> text) =>
        AppendAllText(path.AsUtf8(), text);

    public static Result<Unit, IoError> AppendAllText(global::System.ReadOnlySpan<byte> path, string text) =>
        AppendAllBytes(path, text.AsUtf8());

    public static Result<Unit, IoError> AppendAllText(string path, string text) =>
        AppendAllBytes(path.AsUtf8(), text.AsUtf8());

    public static Result<Unit, IoError> AppendAllText(global::System.ReadOnlySpan<byte> path, in StringBuf text) =>
        AppendAllBytes(path, text.AsUtf8());

    public static Result<Unit, IoError> AppendAllText(string path, in StringBuf text) =>
        AppendAllBytes(path.AsUtf8(), text.AsUtf8());

    private static Result<File, IoError> Open(global::System.ReadOnlySpan<byte> path, int mode)
    {
        var normalized = new StringBuf(0);
        if (!PathText.Normalize(path, ref normalized)) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Open));
        int handle = Sys.FileOpen(normalized.AsUtf8(), mode);
        if (handle < 0) return Prelude.Err(IoError.FromCode(handle, IoOp.Open));
        return Prelude.Ok(new File(handle));
    }

    public Result<int, IoError> Read(global::System.Span<byte> buffer)
    {
        if (buffer.Length == 0) return Prelude.Ok(0);
        long n = Sys.FileRead(_handle, buffer);
        if (n < 0) return Prelude.Err(IoError.FromCode(n, IoOp.Read));
        return Prelude.Ok((int)n);
    }

    public Result<Unit, IoError> WriteAll(global::System.ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return Prelude.Ok();
        int rc = Sys.FileWriteAll(_handle, data);
        if (rc < 0) return Prelude.Err(IoError.FromCode(rc, IoOp.Write));
        return Prelude.Ok();
    }

    public Result<Unit, IoError> WriteAll(string data) => WriteAll(data.AsUtf8());

    public Result<Unit, IoError> SyncAll()
    {
        int rc = Sys.FileSync(_handle);
        if (rc < 0) return Prelude.Err(IoError.FromCode(rc, IoOp.Sync));
        return Prelude.Ok();
    }

    public void Dispose()
    {
        Sys.Close(_handle);
    }
}

public static class Fs
{
    public static Result<Vec<byte>, IoError> ReadAllBytes(global::System.ReadOnlySpan<byte> path) => ReadAllBytes(path, 67_108_864);

    public static Result<Vec<byte>, IoError> ReadAllBytes(global::System.ReadOnlySpan<byte> path, int maxBytes) =>
        File.ReadAllBytes(path, maxBytes);

    public static Result<Vec<byte>, IoError> ReadAllBytes(string path) => ReadAllBytes(path, 67_108_864);

    public static Result<Vec<byte>, IoError> ReadAllBytes(string path, int maxBytes) =>
        File.ReadAllBytes(path.AsUtf8(), maxBytes);

    public static Result<Unit, IoError> WriteAllBytes(global::System.ReadOnlySpan<byte> path, global::System.ReadOnlySpan<byte> data) =>
        File.WriteAllBytes(path, data);

    public static Result<Unit, IoError> WriteAllBytes(string path, global::System.ReadOnlySpan<byte> data) =>
        File.WriteAllBytes(path.AsUtf8(), data);
}
