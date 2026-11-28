namespace Vibranium.Std;

public struct StringBuf
{
    private Vec<byte> _bytes;

    public StringBuf()
    {
        _bytes = new Vec<byte>();
    }

    public StringBuf(int capacity)
    {
        _bytes = new Vec<byte>(capacity);
    }

    internal StringBuf(Vec<byte> validatedBytes)
    {
        _bytes = validatedBytes;
    }

    public readonly int Length => _bytes.Count;

    public readonly byte this[int index] => _bytes[index];

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> Slice(int start, int length) => _bytes.ReadOnlySlice(start, length);

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly Utf8Lines Lines() => AsUtf8().Lines();

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> AsUtf8() => _bytes.AsReadOnlySpan();

    public void Append(global::System.ReadOnlySpan<byte> text)
    {
        if (!Utf8.IsValid(text))
        {
            VibraniumRuntime.Panic("invalid UTF-8");
        }
        _bytes.Extend(text);
    }

    public void Append(string text) => _bytes.Extend(text.AsUtf8());

    public void operator +=(global::System.ReadOnlySpan<byte> text) => Append(text);

    public void operator +=(string text) => Append(text);

    public bool TryAppend(global::System.ReadOnlySpan<byte> text)
    {
        if (!Utf8.IsValid(text)) return false;
        _bytes.Extend(text);
        return true;
    }

    public bool TryAppend(string text)
    {
        _bytes.Extend(text.AsUtf8());
        return true;
    }

    public void Append(int value) => AppendInt(value);

    public void Append(long value) => AppendLong(value);

    public void Append(bool value) => AppendBool(value);

    public void AppendAscii(byte value)
    {
        if (value >= 0x80)
        {
            VibraniumRuntime.Panic("non-ASCII byte");
        }
        _bytes.Push(value);
    }

    public void AppendLine() => _bytes.Push((byte)'\n');

    public void AppendLine(global::System.ReadOnlySpan<byte> text)
    {
        Append(text);
        AppendLine();
    }

    public void AppendLine(string text)
    {
        _bytes.Extend(text.AsUtf8());
        AppendLine();
    }

    public void AppendBool(bool value)
    {
        if (value) _bytes.Extend("True"u8);
        else _bytes.Extend("False"u8);
    }

    public void AppendInt(int value) => AppendLong(value);

    public void AppendLong(long value)
    {
        int start = _bytes.Count;
        if (value == 0)
        {
            _bytes.Push((byte)'0');
            return;
        }
        long v = value < 0 ? value : -value;
        while (v != 0)
        {
            int digit = (int)-(v % 10);
            _bytes.Push((byte)('0' + digit));
            v /= 10;
        }
        if (value < 0) _bytes.Push((byte)'-');
        int lo = start;
        int hi = _bytes.Count - 1;
        while (lo < hi)
        {
            byte t = _bytes[lo];
            _bytes[lo] = _bytes[hi];
            _bytes[hi] = t;
            lo += 1;
            hi -= 1;
        }
    }

    public void AppendHex(long bits, int minDigits)
    {
        int digits = 16;
        while (digits > 1 && digits > minDigits && ((bits >>> ((digits - 1) * 4)) & 15) == 0)
        {
            digits -= 1;
        }
        for (int i = digits - 1; i >= 0; i--)
        {
            int nibble = (int)((bits >>> (i * 4)) & 15);
            _bytes.Push((byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10));
        }
    }

    public void AppendRune(int scalar)
    {
        if (scalar < 0 || scalar > 0x10FFFF || (scalar >= 0xD800 && scalar <= 0xDFFF))
        {
            VibraniumRuntime.Panic("invalid Unicode scalar");
        }
        if (scalar < 0x80)
        {
            _bytes.Push((byte)scalar);
        }
        else if (scalar < 0x800)
        {
            _bytes.Push((byte)(0xC0 | (scalar >> 6)));
            _bytes.Push((byte)(0x80 | (scalar & 0x3F)));
        }
        else if (scalar < 0x10000)
        {
            _bytes.Push((byte)(0xE0 | (scalar >> 12)));
            _bytes.Push((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            _bytes.Push((byte)(0x80 | (scalar & 0x3F)));
        }
        else
        {
            _bytes.Push((byte)(0xF0 | (scalar >> 18)));
            _bytes.Push((byte)(0x80 | ((scalar >> 12) & 0x3F)));
            _bytes.Push((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            _bytes.Push((byte)(0x80 | (scalar & 0x3F)));
        }
    }

    public void Clear() => _bytes.Clear();

    public void Truncate(int byteLength)
    {
        if (byteLength < 0 || byteLength > _bytes.Count)
        {
            VibraniumRuntime.Panic("truncate out of bounds");
        }
        if (!Utf8.IsBoundary(_bytes.AsReadOnlySpan(), byteLength))
        {
            VibraniumRuntime.Panic("truncate inside UTF-8 sequence");
        }
        _bytes.Truncate(byteLength);
    }
}
