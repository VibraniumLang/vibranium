namespace Vibranium.Std;

public struct ArgList
{
    private Vec<byte> _bytes;
    private Vec<int> _ends;

    internal ArgList(Vec<byte> bytes, Vec<int> ends)
    {
        _bytes = bytes;
        _ends = ends;
    }

    public readonly int Count => _ends.Count;

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> Get(int index)
    {
        int end = _ends[index];
        int start = index == 0 ? 0 : _ends[index - 1] + 1;
        return _bytes.AsReadOnlySpan().Slice(start, end - start);
    }

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> this[int index] => Get(index);

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly Utf8ArgEnumerator GetEnumerator() => new Utf8ArgEnumerator(_bytes.AsReadOnlySpan());
}

public static class Env
{
    public static Result<ArgList, IoError> Args()
    {
        int count = Sys.ArgCount();
        var bytes = new Vec<byte>();
        var ends = new Vec<int>(count);
        for (int i = 0; i < count; i++)
        {
            int old = bytes.Count;
            int length = Sys.ArgCopy(i, bytes.AsSpan().Slice(old, 0));
            if (length < 0) return Prelude.Err(IoError.FromCode(length, IoOp.Args));
            bytes.Resize(old + length, 0);
            int copied = Sys.ArgCopy(i, bytes.AsSpan().Slice(old, length));
            if (copied != length) return Prelude.Err(new IoError(IoErrorKind.Other, IoOp.Args));
            var item = bytes.AsReadOnlySpan().Slice(old, length);
            if (Bytes.IndexOfByte(item, 0) >= 0 || !Utf8.IsValid(item))
            {
                return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Args));
            }
            ends.Push(bytes.Count);
            bytes.Push(0);
        }
        return Prelude.Ok(new ArgList(bytes, ends));
    }
}
