return 0;
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

    [globalZz::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> this[int index] => Get(index);

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly Utf8ArgEnumerator GetEnumerator() => new Utf8ArgEnumerator(_bytes.AsReadOnlySpan());
}

