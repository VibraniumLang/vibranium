namespace Vibranium.Std;

public static class ByteSpanExtensions
{
    public static bool SequenceEqual(this global::System.ReadOnlySpan<byte> span, global::System.ReadOnlySpan<byte> other) =>
        Bytes.Eq(span, other);

    public static bool SequenceEqual(this global::System.ReadOnlySpan<byte> span, string other) =>
        Bytes.Eq(span, other.AsUtf8());

    public static bool StartsWith(this global::System.ReadOnlySpan<byte> span, global::System.ReadOnlySpan<byte> value) =>
        Bytes.StartsWith(span, value);

    public static bool StartsWith(this global::System.ReadOnlySpan<byte> span, string value) =>
        Bytes.StartsWith(span, value.AsUtf8());

    public static bool EndsWith(this global::System.ReadOnlySpan<byte> span, global::System.ReadOnlySpan<byte> value) =>
        Bytes.EndsWith(span, value);

    public static bool EndsWith(this global::System.ReadOnlySpan<byte> span, string value) =>
        Bytes.EndsWith(span, value.AsUtf8());

    public static int IndexOf(this global::System.ReadOnlySpan<byte> span, byte value) =>
        Bytes.IndexOfByte(span, value);

    public static int IndexOf(this global::System.ReadOnlySpan<byte> span, global::System.ReadOnlySpan<byte> value) =>
        Bytes.IndexOf(span, value);

    public static int IndexOf(this global::System.ReadOnlySpan<byte> span, string value) =>
        Bytes.IndexOf(span, value.AsUtf8());

    public static int LastIndexOf(this global::System.ReadOnlySpan<byte> span, byte value) =>
        Bytes.LastIndexOfByte(span, value);

    public static bool Contains(this global::System.ReadOnlySpan<byte> span, byte value) =>
        Bytes.IndexOfByte(span, value) >= 0;

    public static bool Contains(this global::System.ReadOnlySpan<byte> span, global::System.ReadOnlySpan<byte> value) =>
        Bytes.IndexOf(span, value) >= 0;

    public static bool Contains(this global::System.ReadOnlySpan<byte> span, string value) =>
        Bytes.IndexOf(span, value.AsUtf8()) >= 0;

    [return: LifetimeOf("span")]
    public static global::System.ReadOnlySpan<byte> Trim(this global::System.ReadOnlySpan<byte> span)
    {
        int start = 0;
        while (start < span.Length && Bytes.IsAsciiWhitespace(span[start]))
        {
            start += 1;
        }
        int end = span.Length;
        while (end > start && Bytes.IsAsciiWhitespace(span[end - 1]))
        {
            end -= 1;
        }
        return span.Slice(start, end - start);
    }

    [return: LifetimeOf("span")]
    public static global::System.ReadOnlySpan<byte> TrimStart(this global::System.ReadOnlySpan<byte> span)
    {
        int start = 0;
        while (start < span.Length && Bytes.IsAsciiWhitespace(span[start]))
        {
            start += 1;
        }
        return span.Slice(start, span.Length - start);
    }

    [return: LifetimeOf("span")]
    public static global::System.ReadOnlySpan<byte> TrimEnd(this global::System.ReadOnlySpan<byte> span)
    {
        int end = span.Length;
        while (end > 0 && Bytes.IsAsciiWhitespace(span[end - 1]))
        {
            end -= 1;
        }
        return span.Slice(0, end);
    }
}

public static class Bytes
{
    internal static bool IsAsciiWhitespace(byte b) =>
        b == 0x20 || (b >= 0x09 && b <= 0x0D);

    public static bool Eq(global::System.ReadOnlySpan<byte> a, global::System.ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    public static bool Eq(global::System.ReadOnlySpan<byte> a, string b) => Eq(a, b.AsUtf8());

    public static bool Eq(string a, global::System.ReadOnlySpan<byte> b) => Eq(a.AsUtf8(), b);

    public static bool Eq(string a, string b) => Eq(a.AsUtf8(), b.AsUtf8());

    public static bool EqIgnoreAsciiCase(global::System.ReadOnlySpan<byte> a, global::System.ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            int ca = a[i];
            int cb = b[i];
            if (ca >= 'A' && ca <= 'Z') ca += 32;
            if (cb >= 'A' && cb <= 'Z') cb += 32;
            if (ca != cb) return false;
        }
        return true;
    }

    public static bool EqIgnoreAsciiCase(global::System.ReadOnlySpan<byte> a, string b) => EqIgnoreAsciiCase(a, b.AsUtf8());

    public static bool EqIgnoreAsciiCase(string a, global::System.ReadOnlySpan<byte> b) => EqIgnoreAsciiCase(a.AsUtf8(), b);

    public static bool EqIgnoreAsciiCase(string a, string b) => EqIgnoreAsciiCase(a.AsUtf8(), b.AsUtf8());

    public static global::System.ReadOnlySpan<byte> TrimAscii(global::System.ReadOnlySpan<byte> text)
    {
        int start = 0;
        while (start < text.Length && (text[start] == (byte)' ' || text[start] == (byte)'\t'))
        {
            start += 1;
        }
        int end = text.Length;
        while (end > start && (text[end - 1] == (byte)' ' || text[end - 1] == (byte)'\t'))
        {
            end -= 1;
        }
        return text.Slice(start, end - start);
    }

    public static global::System.ReadOnlySpan<byte> TrimAscii(string text) => TrimAscii(text.AsUtf8());

    public static bool StartsWith(global::System.ReadOnlySpan<byte> text, global::System.ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length > text.Length) return false;
        return Eq(text.Slice(0, prefix.Length), prefix);
    }

    public static bool StartsWith(global::System.ReadOnlySpan<byte> text, string prefix) => StartsWith(text, prefix.AsUtf8());

    public static bool StartsWith(string text, global::System.ReadOnlySpan<byte> prefix) => StartsWith(text.AsUtf8(), prefix);

    public static bool StartsWith(string text, string prefix) => StartsWith(text.AsUtf8(), prefix.AsUtf8());

    public static bool EndsWith(global::System.ReadOnlySpan<byte> text, global::System.ReadOnlySpan<byte> suffix)
    {
        if (suffix.Length > text.Length) return false;
        return Eq(text.Slice(text.Length - suffix.Length, suffix.Length), suffix);
    }

    public static bool EndsWith(global::System.ReadOnlySpan<byte> text, string suffix) => EndsWith(text, suffix.AsUtf8());

    public static bool EndsWith(string text, global::System.ReadOnlySpan<byte> suffix) => EndsWith(text.AsUtf8(), suffix);

    public static bool EndsWith(string text, string suffix) => EndsWith(text.AsUtf8(), suffix.AsUtf8());

    public static int IndexOfByte(global::System.ReadOnlySpan<byte> text, byte value)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == value) return i;
        }
        return -1;
    }

    public static int IndexOfByte(string text, byte value) => IndexOfByte(text.AsUtf8(), value);

    public static int LastIndexOfByte(global::System.ReadOnlySpan<byte> text, byte value)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == value) return i;
        }
        return -1;
    }

    public static int LastIndexOfByte(string text, byte value) => LastIndexOfByte(text.AsUtf8(), value);

    public static int IndexOf(global::System.ReadOnlySpan<byte> text, global::System.ReadOnlySpan<byte> needle)
    {
        int last = text.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            if (Eq(text.Slice(i, needle.Length), needle)) return i;
        }
        return -1;
    }

    public static int IndexOf(global::System.ReadOnlySpan<byte> text, string needle) => IndexOf(text, needle.AsUtf8());

    public static int IndexOf(string text, global::System.ReadOnlySpan<byte> needle) => IndexOf(text.AsUtf8(), needle);

    public static int IndexOf(string text, string needle) => IndexOf(text.AsUtf8(), needle.AsUtf8());

    public static Option<int> ParseInt(global::System.ReadOnlySpan<byte> text)
    {
        int n = text.Length;
        int i = 0;
        bool negative = false;
        if (n > 0 && text[0] == (byte)'-')
        {
            negative = true;
            i = 1;
        }
        if (i == n) return Prelude.None;
        long value = 0;
        while (i < n)
        {
            int d = text[i] - (byte)'0';
            if (d < 0 || d > 9) return Prelude.None;
            value = value * 10 + d;
            if (value > 2147483648L) return Prelude.None;
            i += 1;
        }
        if (negative) value = -value;
        if (value > 2147483647L) return Prelude.None;
        return Prelude.Some((int)value);
    }

    public static Option<int> ParseInt(string text) => ParseInt(text.AsUtf8());

    public static Option<long> ParseLong(global::System.ReadOnlySpan<byte> text)
    {
        int n = text.Length;
        int i = 0;
        bool negative = false;
        if (n > 0 && text[0] == (byte)'-')
        {
            negative = true;
            i = 1;
        }
        if (i == n) return Prelude.None;
        ulong limit = negative ? 9223372036854775808UL : 9223372036854775807UL;
        ulong maxBeforeMul = limit / 10UL;
        ulong maxLastDigit = limit % 10UL;
        ulong value = 0;
        while (i < n)
        {
            int d = text[i] - (byte)'0';
            if (d < 0 || d > 9) return Prelude.None;
            ulong ud = (ulong)d;
            if (value > maxBeforeMul || (value == maxBeforeMul && ud > maxLastDigit)) return Prelude.None;
            value = value * 10UL + ud;
            i += 1;
        }
        if (negative)
        {
            return value == 9223372036854775808UL ? Prelude.Some(long.MinValue) : Prelude.Some(-(long)value);
        }
        return Prelude.Some((long)value);
    }

    public static Option<long> ParseLong(string text) => ParseLong(text.AsUtf8());

    public static bool TryParseInt32(global::System.ReadOnlySpan<byte> utf8Text, out int result)
    {
        result = 0;
        int n = utf8Text.Length;
        int i = 0;
        while (i < n && IsAsciiWhitespace(utf8Text[i]))
        {
            i += 1;
        }
        if (i == n) return false;

        bool negative = false;
        if (utf8Text[i] == (byte)'-')
        {
            negative = true;
            i += 1;
        }
        else if (utf8Text[i] == (byte)'+')
        {
            i += 1;
        }

        if (i == n || utf8Text[i] < (byte)'0' || utf8Text[i] > (byte)'9') return false;

        ulong limit = negative ? 2147483648UL : 2147483647UL;
        ulong value = 0;
        while (i < n && utf8Text[i] >= (byte)'0' && utf8Text[i] <= (byte)'9')
        {
            ulong d = (ulong)(utf8Text[i] - (byte)'0');
            value = value * 10UL + d;
            if (value > limit) return false;
            i += 1;
        }

        while (i < n && IsAsciiWhitespace(utf8Text[i]))
        {
            i += 1;
        }
        while (i < n && utf8Text[i] == 0)
        {
            i += 1;
        }
        if (i != n) return false;

        result = negative
            ? (value == 2147483648UL ? int.MinValue : -(int)value)
            : (int)value;
        return true;
    }

    public static bool TryParseInt32(string utf8Text, out int result) => TryParseInt32(utf8Text.AsUtf8(), out result);

    public static bool TryParseInt64(global::System.ReadOnlySpan<byte> utf8Text, out long result)
    {
        result = 0;
        int n = utf8Text.Length;
        int i = 0;
        while (i < n && IsAsciiWhitespace(utf8Text[i]))
        {
            i += 1;
        }
        if (i == n) return false;

        bool negative = false;
        if (utf8Text[i] == (byte)'-')
        {
            negative = true;
            i += 1;
        }
        else if (utf8Text[i] == (byte)'+')
        {
            i += 1;
        }

        if (i == n || utf8Text[i] < (byte)'0' || utf8Text[i] > (byte)'9') return false;

        ulong limit = negative ? 9223372036854775808UL : 9223372036854775807UL;
        ulong maxBeforeMul = limit / 10UL;
        ulong maxLastDigit = limit % 10UL;
        ulong value = 0;
        while (i < n && utf8Text[i] >= (byte)'0' && utf8Text[i] <= (byte)'9')
        {
            ulong d = (ulong)(utf8Text[i] - (byte)'0');
            if (value > maxBeforeMul || (value == maxBeforeMul && d > maxLastDigit)) return false;
            value = value * 10UL + d;
            i += 1;
        }

        while (i < n && IsAsciiWhitespace(utf8Text[i]))
        {
            i += 1;
        }
        while (i < n && utf8Text[i] == 0)
        {
            i += 1;
        }
        if (i != n) return false;

        result = negative
            ? (value == 9223372036854775808UL ? long.MinValue : -(long)value)
            : (long)value;
        return true;
    }

    public static bool TryParseInt64(string utf8Text, out long result) => TryParseInt64(utf8Text.AsUtf8(), out result);

    public static long Hash(global::System.ReadOnlySpan<byte> text)
    {
        long h = -0x340D631B7BDDDCDBL;
        for (int i = 0; i < text.Length; i++)
        {
            h = (h ^ text[i]) * 0x100000001B3L;
        }
        return h;
    }

    public static long Hash(string text) => Hash(text.AsUtf8());
}
