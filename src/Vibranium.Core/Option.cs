namespace Vibranium;

[global::System.Runtime.CompilerServices.Union]
public struct Option<T>
{
    internal bool _hasValue;
    internal T _value;

    public Option(Some<T> some)
    {
        _hasValue = true;
        _value = some.Value;
    }

    public Option(NoneType _)
    {
        _hasValue = false;
        _value = default!;
    }

    public readonly bool IsSome => _hasValue;
    public readonly bool IsEmpty => !_hasValue;

    public readonly bool IsNone(out T value)
    {
        value = _value;
        return !_hasValue;
    }

    public readonly T Unwrap()
    {
        if (!_hasValue)
        {
            VibraniumRuntime.Panic("called Option.Unwrap() on None");
        }
        return _value;
    }

    public readonly T Expect(string message)
    {
        if (!_hasValue)
        {
            VibraniumRuntime.Panic(message);
        }
        return _value;
    }

    public readonly T UnwrapOr(T fallback) => _hasValue ? _value : fallback;

    public readonly T UnwrapOrDefault() => _hasValue ? _value : default!;

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public ref T UnwrapMut()
    {
        if (!_hasValue)
        {
            VibraniumRuntime.Panic("called Option.UnwrapMut() on None");
        }
        return ref _value;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly bool HasValue => true;

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly bool TryGetValue(out Some<T> some)
    {
        some = new Some<T>(_value);
        return _hasValue;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly bool TryGetValue(out NoneType none)
    {
        none = default;
        return !_hasValue;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly object Value => _hasValue ? new Some<T>(_value) : new NoneType();
}

[global::System.Runtime.CompilerServices.Union]
public readonly ref struct OptionRef<T>
{
    private readonly ref readonly Option<T> _slot;

    internal OptionRef(in Option<T> slot)
    {
        _slot = ref slot;
    }

    [global::System.Obsolete("Use AsRef()", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public OptionRef(Some<T> _) => throw new global::System.NotSupportedException();

    [global::System.Obsolete("Use AsRef()", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public OptionRef(NoneType _) => throw new global::System.NotSupportedException();

    public bool IsSome => _slot._hasValue;

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public bool HasValue => true;

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public bool TryGetValue(out Some<T> some)
    {
        some = new Some<T>(_slot._value);
        return _slot._hasValue;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public bool TryGetValue(out NoneType none)
    {
        none = default;
        return !_slot._hasValue;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public object Value => _slot._hasValue ? new Some<T>(_slot._value) : new NoneType();
}

[global::System.Runtime.CompilerServices.Union]
public ref struct OptionMut<T> where T : class
{
    private readonly ref Option<T> _slot;

    internal OptionMut(ref Option<T> slot)
    {
        _slot = ref slot;
    }

    [global::System.Obsolete("Use AsMut()", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public OptionMut(Some<T> _) => throw new global::System.NotSupportedException();

    [global::System.Obsolete("Use AsMut()", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public OptionMut(NoneType _) => throw new global::System.NotSupportedException();

    public readonly bool IsSome => _slot._hasValue;

    public readonly OptionRef<T> AsRef() => new(in _slot);

    public void Set(Option<T> value)
    {
        _slot = value;
    }

    public Option<T> Replace(Option<T> value)
    {
        var previous = _slot;
        _slot = value;
        return previous;
    }

    public Option<T> Take()
    {
        var previous = _slot;
        _slot = new Option<T>(Prelude.None);
        return previous;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly bool HasValue => true;

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly bool TryGetValue(out Some<T> some)
    {
        some = new Some<T>(_slot._value);
        return _slot._hasValue;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly bool TryGetValue(out NoneType none)
    {
        none = default;
        return !_slot._hasValue;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public readonly object Value => _slot._hasValue ? new Some<T>(_slot._value) : new NoneType();
}

public static class OptionExtensions
{
    public static OptionRef<T> AsRef<T>([global::System.Diagnostics.CodeAnalysis.UnscopedRef] this in Option<T> slot)
        => new(in slot);

    public static OptionMut<T> AsMut<T>([global::System.Diagnostics.CodeAnalysis.UnscopedRef] this ref Option<T> slot) where T : class
        => new(ref slot);

    public static Option<T> Take<T>(this ref Option<T> slot)
    {
        var previous = slot;
        slot = new Option<T>(Prelude.None);
        return previous;
    }

    public static Option<T> Replace<T>(this ref Option<T> slot, Option<T> replacement)
    {
        var previous = slot;
        slot = replacement;
        return previous;
    }
}
