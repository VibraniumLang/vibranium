namespace Vibranium;

public sealed class VibraniumPanicException(string message) : global::System.Exception(message);

public static class VibraniumRuntime
{
    private static int _initialized;
    public static bool InProcessTestMode { get; set; }

    [global::System.Runtime.CompilerServices.ModuleInitializer]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Installs Vibranium standalone panic-exit parity hook")]
    public static void Initialize()
    {
        if (global::System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        global::System.Console.OutputEncoding = global::System.Text.Encoding.UTF8;
        global::System.Console.Out.NewLine = "\n";

        global::System.AppDomain.CurrentDomain.UnhandledException += static (_, args) =>
        {
            string message = args.ExceptionObject switch
            {
                VibraniumPanicException p => p.Message,
                global::System.IndexOutOfRangeException => "index out of bounds",
                global::System.ArgumentOutOfRangeException => "slice out of bounds",
                global::System.DivideByZeroException => "attempt to divide by zero",
                global::System.OverflowException => "arithmetic overflow",
                global::System.OutOfMemoryException => "out of memory",
                var obj => "internal error: " + (obj?.GetType().Name ?? "unknown"),
            };
            WritePanicAndExit(message);
        };
    }

    [global::System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void Panic(string message)
    {
        if (InProcessTestMode)
        {
            throw new VibraniumPanicException(message);
        }

        WritePanicAndExit(message);
    }

    [global::System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void WritePanicAndExit(string message)
    {
        try
        {
            global::System.Console.Out.Flush();
            var stderr = global::System.Console.OpenStandardError();
            stderr.Write("panic: "u8);
            global::System.Span<byte> buf = stackalloc byte[256];
            if (global::System.Text.Encoding.UTF8.TryGetBytes(message, buf, out int written))
            {
                stderr.Write(buf[..written]);
            }
            else
            {
                byte[] bytes = global::System.Text.Encoding.UTF8.GetBytes(message);
                stderr.Write(bytes, 0, bytes.Length);
            }
            stderr.Write("\n"u8);
            stderr.Flush();
        }
        catch (global::System.IO.IOException)
        {
        }
        global::System.Environment.Exit(101);
    }
}

public static class StringUtf8Extensions
{
    private static readonly global::System.Text.UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<string, byte[]> s_cache = new();

    public static global::System.ReadOnlySpan<byte> AsUtf8(this string text)
    {
        if (text is null)
        {
            VibraniumRuntime.Panic("null string");
        }
        if (text.Length == 0)
        {
            return ""u8;
        }
        return s_cache.GetValue(text, static s => s_strictUtf8.GetBytes(s));
    }

    public static Utf8Lines Lines(this string text) => new(text.AsUtf8());
}

public static class Utf8SpanExtensions
{
    public static Utf8Lines Lines(this global::System.ReadOnlySpan<byte> text) => new(text);
}

public ref struct Utf8Lines
{
    private readonly global::System.ReadOnlySpan<byte> _text;
    private int _pos;
    private global::System.ReadOnlySpan<byte> _current;

    internal Utf8Lines(global::System.ReadOnlySpan<byte> text)
    {
        _text = text;
        _pos = 0;
        _current = default;
    }

    public readonly Utf8Lines GetEnumerator() => this;

    public bool MoveNext()
    {
        int len = _text.Length;
        if (_pos >= len)
        {
            return false;
        }

        int start = _pos;
        int i = start;
        int end = len;
        int next = len;
        while (i < len)
        {
            byte b = _text[i];
            if (b == (byte)'\n')
            {
                end = i;
                next = i + 1;
                break;
            }
            if (b == (byte)'\r')
            {
                end = i;
                next = (i + 1 < len && _text[i + 1] == (byte)'\n') ? (i + 2) : (i + 1);
                break;
            }
            i += 1;
        }

        _current = _text.Slice(start, end - start);
        _pos = next;
        return true;
    }

    public readonly global::System.ReadOnlySpan<byte> Current => _current;
}

public ref struct Utf8ArgEnumerator
{
    private readonly global::System.ReadOnlySpan<byte> _items;
    private int _pos;
    private global::System.ReadOnlySpan<byte> _current;

    public Utf8ArgEnumerator(global::System.ReadOnlySpan<byte> nulTerminatedItems)
    {
        _items = nulTerminatedItems;
        _pos = 0;
        _current = default;
    }

    public bool MoveNext()
    {
        int len = _items.Length;
        if (_pos >= len)
        {
            return false;
        }

        int start = _pos;
        int i = start;
        int end = len;
        int next = len;
        while (i < len)
        {
            if (_items[i] == 0)
            {
                end = i;
                next = i + 1;
                break;
            }
            i += 1;
        }

        _current = _items.Slice(start, end - start);
        _pos = next;
        return true;
    }

    public readonly global::System.ReadOnlySpan<byte> Current => _current;
}

public static class Io
{
    public static void Write(sbyte value) => Write((long)value);
    public static void Write(byte value) => Write((ulong)value);
    public static void Write(short value) => Write((long)value);
    public static void Write(ushort value) => Write((ulong)value);
    public static void Write(int value) => Write((long)value);
    public static void Write(uint value) => Write((ulong)value);

    public static void Write(long value)
    {
        global::System.Span<char> buf = stackalloc char[32];
        value.TryFormat(buf, out int n, default, global::System.Globalization.CultureInfo.InvariantCulture);
        global::System.Console.Out.Write(buf[..n]);
    }

    public static void Write(ulong value)
    {
        global::System.Span<char> buf = stackalloc char[32];
        value.TryFormat(buf, out int n, default, global::System.Globalization.CultureInfo.InvariantCulture);
        global::System.Console.Out.Write(buf[..n]);
    }

    public static void Write(bool value) => global::System.Console.Out.Write(value ? "True" : "False");

    public static void Write(string value) => global::System.Console.Out.Write(value);

    public static void Write(global::System.ReadOnlySpan<byte> utf8) => Stdio.WriteOut(utf8);

    public static void WriteLine() => global::System.Console.Out.Write('\n');

    public static void WriteLine(sbyte value) => WriteLine((long)value);
    public static void WriteLine(byte value) => WriteLine((ulong)value);
    public static void WriteLine(short value) => WriteLine((long)value);
    public static void WriteLine(ushort value) => WriteLine((ulong)value);
    public static void WriteLine(int value) => WriteLine((long)value);
    public static void WriteLine(uint value) => WriteLine((ulong)value);

    public static void WriteLine(long value)
    {
        global::System.Span<char> buf = stackalloc char[32];
        value.TryFormat(buf, out int n, default, global::System.Globalization.CultureInfo.InvariantCulture);
        buf[n] = '\n';
        global::System.Console.Out.Write(buf[..(n + 1)]);
    }

    public static void WriteLine(ulong value)
    {
        global::System.Span<char> buf = stackalloc char[32];
        value.TryFormat(buf, out int n, default, global::System.Globalization.CultureInfo.InvariantCulture);
        buf[n] = '\n';
        global::System.Console.Out.Write(buf[..(n + 1)]);
    }

    public static void WriteLine(bool value) => global::System.Console.Out.Write(value ? "True\n" : "False\n");

    public static void WriteLine(string value)
    {
        global::System.Console.Out.Write(value);
        global::System.Console.Out.Write('\n');
    }

    public static void WriteLine(global::System.ReadOnlySpan<byte> utf8) => Stdio.WriteOutLine(utf8);
}

/// <summary>Raw, unbuffered, best-effort byte output sharing ordering with Write/WriteLine (Core intrinsic).</summary>
public static class Stdio
{
    private static readonly global::System.IO.Stream s_out = global::System.Console.OpenStandardOutput();
    private static readonly global::System.IO.Stream s_err = global::System.Console.OpenStandardError();

    public static void WriteOut(global::System.ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }
        try
        {
            global::System.Console.Out.Flush();
            s_out.Write(bytes);
            s_out.Flush();
        }
        catch (global::System.IO.IOException)
        {
        }
    }

    public static void WriteOut(string text) => WriteOut(text.AsUtf8());

    internal static void WriteOutLine(global::System.ReadOnlySpan<byte> bytes)
    {
        try
        {
            global::System.Console.Out.Flush();
            if (!bytes.IsEmpty)
            {
                s_out.Write(bytes);
            }
            s_out.Write("\n"u8);
            s_out.Flush();
        }
        catch (global::System.IO.IOException)
        {
        }
    }

    public static void WriteErr(global::System.ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }
        try
        {
            global::System.Console.Out.Flush();
            s_err.Write(bytes);
            s_err.Flush();
        }
        catch (global::System.IO.IOException)
        {
        }
    }

    public static void WriteErr(string text) => WriteErr(text.AsUtf8());
}
