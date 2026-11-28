namespace Vibranium;

[global::System.Runtime.CompilerServices.Union]
public readonly struct Result<T, E>
{
    private readonly bool _isOk;
    private readonly T _ok;
    private readonly E _err;

    public Result(Ok<T> ok)
    {
        _isOk = true;
        _ok = ok.Value;
        _err = default!;
    }

    public Result(Err<E> err)
    {
        _isOk = false;
        _ok = default!;
        _err = err.Error;
    }

    public bool IsOk => _isOk;

    public bool IsErr(out T ok, out Err<E> err)
    {
        ok = _ok;
        err = new Err<E>(_err);
        return !_isOk;
    }

    public T Unwrap()
    {
        if (!_isOk)
        {
            VibraniumRuntime.Panic("called Result.Unwrap() on Err");
        }
        return _ok;
    }

    public T Expect(string message)
    {
        if (!_isOk)
        {
            VibraniumRuntime.Panic(message);
        }
        return _ok;
    }

    public T UnwrapOr(T fallback) => _isOk ? _ok : fallback;

    public T UnwrapOrDefault() => _isOk ? _ok : default!;

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public bool HasValue => true;

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public bool TryGetValue(out Ok<T> ok)
    {
        ok = new Ok<T>(_ok);
        return _isOk;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public bool TryGetValue(out Err<E> err)
    {
        err = new Err<E>(_err);
        return !_isOk;
    }

    [global::System.Obsolete("Internal union protocol", error: true)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public object Value => _isOk ? new Ok<T>(_ok) : new Err<E>(_err);
}
