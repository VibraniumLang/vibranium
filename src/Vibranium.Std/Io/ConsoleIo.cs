namespace Vibranium.Std;

public static class ConsoleIo
{
    public static void Write(in StringBuf text) => Stdio.WriteOut(text.AsUtf8());

    public static void WriteLine(in StringBuf text) => Vibranium.Io.WriteLine(text.AsUtf8());

    public static Result<Option<StringBuf>, IoError> ReadLine() => ReadLine(1_048_576);

    public static Result<Option<StringBuf>, IoError> ReadLine(int maxBytes)
    {
        var line = new StringBuf(0);
        if (TryReadLine(ref line, maxBytes).IsErr(out bool hasLine, out var err))
        {
            return err;
        }
        if (!hasLine)
        {
            Option<StringBuf> empty = Prelude.None;
            return Prelude.Ok(empty);
        }
        Option<StringBuf> some = Prelude.Some(line);
        return Prelude.Ok(some);
    }

    public static Result<bool, IoError> TryReadLine(ref StringBuf line) => TryReadLine(ref line, 1_048_576);

    public static Result<bool, IoError> TryReadLine(ref StringBuf line, int maxBytes)
    {
        line.Clear();
        if (maxBytes < 0)
        {
            return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Read));
        }

        var raw = new Vec<byte>();
        System.Span<byte> one = [0];
        while (true)
        {
            long rc = Sys.StdinRead(one);
            if (rc < 0)
            {
                return Prelude.Err(IoError.FromCode(rc, IoOp.Read));
            }
            if (rc == 0)
            {
                if (raw.Count == 0)
                {
                    return Prelude.Ok(false);
                }
                if (raw.Count > maxBytes)
                {
                    return Prelude.Err(new IoError(IoErrorKind.FileTooLarge, IoOp.Read));
                }
                break;
            }

            byte b = one[0];
            if (b == (byte)'\n')
            {
                if (raw.Count > 0 && raw[raw.Count - 1] == (byte)'\r')
                {
                    raw.Truncate(raw.Count - 1);
                }
                break;
            }

            if (raw.Count > maxBytes || (raw.Count == maxBytes && b != (byte)'\r'))
            {
                return Prelude.Err(new IoError(IoErrorKind.FileTooLarge, IoOp.Read));
            }
            raw.Push(b);
        }

        if (!Utf8.IsValid(raw.AsReadOnlySpan()))
        {
            return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Read));
        }

        line = new StringBuf(raw);
        return Prelude.Ok(true);
    }
}
