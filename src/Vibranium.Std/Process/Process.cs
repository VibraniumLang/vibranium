namespace Vibranium.Std;

public struct Command
{
    private Vec<byte> _arena;
    private Vec<int> _starts;
    private bool _valid;

    public Command(global::System.ReadOnlySpan<byte> program)
    {
        _arena = new Vec<byte>();
        _starts = new Vec<int>();
        if (program.Length == 0 || program[0] != (byte)'/' || Bytes.IndexOfByte(program, 0) >= 0 || !Utf8.IsValid(program))
        {
            _valid = false;
        }
        else
        {
            _valid = true;
            _starts.Push(0);
            _arena.Extend(program);
            _arena.Push(0);
        }
    }

    public Command(string program)
    {
        var utf8 = program.AsUtf8();
        _arena = new Vec<byte>();
        _starts = new Vec<int>();
        if (utf8.Length == 0 || utf8[0] != (byte)'/' || Bytes.IndexOfByte(utf8, 0) >= 0)
        {
            _valid = false;
        }
        else
        {
            _valid = true;
            _starts.Push(0);
            _arena.Extend(utf8);
            _arena.Push(0);
        }
    }

    public void Arg(global::System.ReadOnlySpan<byte> value)
    {
        if (Bytes.IndexOfByte(value, 0) >= 0 || !Utf8.IsValid(value))
        {
            _valid = false;
            return;
        }
        _starts.Push(_arena.Count);
        _arena.Extend(value);
        _arena.Push(0);
    }

    public void Arg(string value)
    {
        var utf8 = value.AsUtf8();
        if (Bytes.IndexOfByte(utf8, 0) >= 0)
        {
            _valid = false;
            return;
        }
        _starts.Push(_arena.Count);
        _arena.Extend(utf8);
        _arena.Push(0);
    }

    internal readonly bool IsValid => _valid;

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    internal readonly global::System.ReadOnlySpan<byte> Arena() => _arena.AsReadOnlySpan();

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    internal readonly global::System.ReadOnlySpan<int> Starts() => _starts.AsReadOnlySpan();
}

public static class Process
{
    public static Result<int, IoError> Run(in Command command)
    {
        if (!command.IsValid) return Prelude.Err(new IoError(IoErrorKind.InvalidInput, IoOp.Spawn));
        int rc = Sys.SpawnWait(command.Arena(), command.Starts());
        if (rc < 0) return Prelude.Err(IoError.FromCode(rc, IoOp.Spawn));
        return Prelude.Ok(rc);
    }
}
