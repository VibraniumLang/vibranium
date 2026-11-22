return 0;
static class CorpusSlice
{
public static void Contains(this global::System.ReadOnlySpan<byte> span, byte value) =>
        Bytes.IndexOfByte(span, value) >= 0;

}
