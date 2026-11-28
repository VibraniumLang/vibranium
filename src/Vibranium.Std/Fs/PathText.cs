namespace Vibranium.Std;

public static class PathText
{
    public const int MaxLength = 4095;

    public static bool Normalize(global::System.ReadOnlySpan<byte> path, ref StringBuf result)
    {
        result.Clear();
        int n = path.Length;
        if (n == 0 || n > MaxLength) return false;
        if (Bytes.IndexOfByte(path, 0) >= 0) return false;
        if (!Utf8.IsValid(path)) return false;
        bool absolute = path[0] == (byte)'/';
        if (n > 1 && path[n - 1] == (byte)'/') return false;
        if (absolute) result.AppendAscii((byte)'/');
        var marks = new Vec<int>();
        int i = 0;
        while (i < n)
        {
            if (path[i] == (byte)'/')
            {
                i += 1;
                continue;
            }
            int start = i;
            while (i < n && path[i] != (byte)'/')
            {
                i += 1;
            }
            var part = path.Slice(start, i - start);
            if (Bytes.Eq(part, "."u8)) continue;
            if (Bytes.Eq(part, ".."u8))
            {
                if (marks.Count > 0)
                {
                    result.Truncate(marks.Pop().Unwrap());
                    continue;
                }
                if (absolute) continue;
            }
            else
            {
                marks.Push(result.Length);
            }
            int length = result.Length;
            if (length > 0 && !(absolute && length == 1)) result.AppendAscii((byte)'/');
            result.Append(part);
        }
        if (result.Length == 0) result.AppendAscii((byte)'.');
        return true;
    }

    public static bool Normalize(string path, ref StringBuf result) =>
        Normalize(path.AsUtf8(), ref result);
}
