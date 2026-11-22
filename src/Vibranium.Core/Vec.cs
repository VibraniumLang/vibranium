namespace Vibranium;

public static class VecBuilder
{
    public static Vec<T> Create<T>(global::System.ReadOnlySpan<T> items) where T : unmanaged
    {
        var vec = new Vec<T>(items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            vec.Push(items[i]);
        }
        return vec;
    }
}

[global::System.Runtime.CompilerServices.CollectionBuilder(typeof(VecBuilder), nameof(VecBuilder.Create))]
public struct Vec<T> where T : unmanaged
{
    private T[] _buffer;
    private int _count;

    public Vec()
    {
        _buffer = [];
        _count = 0;
    }

    public Vec(int capacity)
    {
        if (capacity < 0)
        {
            VibraniumRuntime.Panic("Vec capacity cannot be negative");
        }
        if (capacity > global::System.Array.MaxLength)
        {
            VibraniumRuntime.Panic("Vec capacity overflow");
        }
        _buffer = capacity == 0 ? [] : new T[capacity];
        _count = 0;
    }

    public readonly int Count => _count;
    public readonly int Capacity => _buffer.Length;

    public void Push(T item)
    {
        if (_count == _buffer.Length)
        {
            if (_count == global::System.Array.MaxLength)
            {
                VibraniumRuntime.Panic("Vec capacity overflow");
            }
            int nextCap = _buffer.Length == 0
                ? 4
                : (_buffer.Length > global::System.Array.MaxLength / 2 ? global::System.Array.MaxLength : _buffer.Length * 2);
            var grown = new T[nextCap];
            global::System.MemoryExtensions.AsSpan(_buffer, 0, _count).CopyTo(grown);
            _buffer = grown;
        }
        _buffer[_count] = item;
        _count += 1;
    }

    public void Add(T item) => Push(item);

    public void operator +=(T item) => Push(item);

    public Option<T> Pop()
    {
        if (_count == 0)
        {
            return Prelude.None;
        }
        _count -= 1;
        return Prelude.Some(_buffer[_count]);
    }

    public void Clear() => _count = 0;

    public void Truncate(int length)
    {
        if ((uint)length > (uint)_count)
        {
            VibraniumRuntime.Panic("truncate out of bounds");
        }
        _count = length;
    }

    public void Reserve(int additional)
    {
        if (additional < 0 || (long)_count + additional > global::System.Array.MaxLength)
        {
            VibraniumRuntime.Panic("Vec capacity overflow");
        }
        int need = _count + additional;
        if (need <= _buffer.Length)
        {
            return;
        }
        int grown = _buffer.Length == 0
            ? 4
            : (_buffer.Length > global::System.Array.MaxLength / 2 ? global::System.Array.MaxLength : _buffer.Length * 2);
        var next = new T[need > grown ? need : grown];
        global::System.MemoryExtensions.AsSpan(_buffer, 0, _count).CopyTo(next);
        _buffer = next;
    }

    public void Extend(global::System.ReadOnlySpan<T> items)
    {
        if (items.Length == 0)
        {
            return;
        }
        Reserve(items.Length);
        items.CopyTo(global::System.MemoryExtensions.AsSpan(_buffer, _count));
        _count += items.Length;
    }

    public void Resize(int length, T fill)
    {
        if (length < 0)
        {
            VibraniumRuntime.Panic("Vec length cannot be negative");
        }
        if (length > _count)
        {
            Reserve(length - _count);
            global::System.MemoryExtensions.AsSpan(_buffer, _count, length - _count).Fill(fill);
        }
        _count = length;
    }

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                VibraniumRuntime.Panic("index out of bounds");
            }
            return ref _buffer[index];
        }
    }

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public global::System.Span<T> Slice(int start, int length)
    {
        if ((uint)start > (uint)_count || (uint)length > (uint)(_count - start))
        {
            VibraniumRuntime.Panic("slice out of bounds");
        }
        return global::System.MemoryExtensions.AsSpan(_buffer, start, length);
    }

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<T> ReadOnlySlice(int start, int length)
    {
        if ((uint)start > (uint)_count || (uint)length > (uint)(_count - start))
        {
            VibraniumRuntime.Panic("slice out of bounds");
        }
        return global::System.MemoryExtensions.AsSpan(_buffer, start, length);
    }

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public global::System.Span<T> AsSpan() => global::System.MemoryExtensions.AsSpan(_buffer, 0, _count);

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<T> AsReadOnlySpan() => global::System.MemoryExtensions.AsSpan(_buffer, 0, _count);

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public global::System.Span<T>.Enumerator GetEnumerator() => global::System.MemoryExtensions.AsSpan(_buffer, 0, _count).GetEnumerator();
}
