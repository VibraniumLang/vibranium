namespace Vibranium;

public readonly struct Unit;

public readonly struct Some<T>(T value)
{
    public readonly T Value = value;
    public void Deconstruct(out T value) => value = Value;
}

public readonly struct NoneType;

public readonly struct Ok<T>(T value)
{
    public readonly T Value = value;
    public void Deconstruct(out T value) => value = Value;
}

public readonly struct Err<E>(E error)
{
    public readonly E Error = error;
    public void Deconstruct(out E error) => error = Error;
}

public static class Prelude
{
    public static Some<T> Some<T>(T value) => new(value);
    public static NoneType None => default;
    public static Ok<Unit> Ok() => new(default);
    public static Ok<T> Ok<T>(T value) => new(value);
    public static Err<E> Err<E>(E error) => new(error);
    public static T Move<T>(T value) where T : allows ref struct => value;

    public static void Write(sbyte value) => Io.Write(value);
    public static void Write(byte value) => Io.Write(value);
    public static void Write(short value) => Io.Write(value);
    public static void Write(ushort value) => Io.Write(value);
    public static void Write(int value) => Io.Write(value);
    public static void Write(uint value) => Io.Write(value);
    public static void Write(long value) => Io.Write(value);
    public static void Write(ulong value) => Io.Write(value);
    public static void Write(bool value) => Io.Write(value);
    public static void Write(string value) => Io.Write(value);
    public static void Write(global::System.ReadOnlySpan<byte> utf8) => Io.Write(utf8);

    public static void WriteLine() => Io.WriteLine();
    public static void WriteLine(sbyte value) => Io.WriteLine(value);
    public static void WriteLine(byte value) => Io.WriteLine(value);
    public static void WriteLine(short value) => Io.WriteLine(value);
    public static void WriteLine(ushort value) => Io.WriteLine(value);
    public static void WriteLine(int value) => Io.WriteLine(value);
    public static void WriteLine(uint value) => Io.WriteLine(value);
    public static void WriteLine(long value) => Io.WriteLine(value);
    public static void WriteLine(ulong value) => Io.WriteLine(value);
    public static void WriteLine(bool value) => Io.WriteLine(value);
    public static void WriteLine(string value) => Io.WriteLine(value);
    public static void WriteLine(global::System.ReadOnlySpan<byte> utf8) => Io.WriteLine(utf8);
}
