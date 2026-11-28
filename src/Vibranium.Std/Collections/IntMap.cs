namespace Vibranium.Std;

public struct IntMap<V> where V : unmanaged
{
    private Vec<long> _keys;
    private Vec<V> _vals;
    private Vec<byte> _state;
    private Vec<int> _index;
    private int _live;

    public IntMap(int capacity)
    {
        _keys = new Vec<long>(capacity);
        _vals = new Vec<V>(capacity);
        _state = new Vec<byte>(capacity);
        _index = new Vec<int>();
        _live = 0;
    }

    public readonly int Count => _live;
    public readonly int EntryCount => _keys.Count;
    public readonly bool IsLive(int entry) => _state[entry] != 0;
    public readonly long KeyAt(int entry) => _keys[entry];
    public readonly V ValueAt(int entry) => _vals[entry];

    private static int Slot(long key, int mask)
    {
        long z = key;
        z = (z ^ (z >>> 30)) * -0x40A7B892E31B1A47L;
        z = (z ^ (z >>> 27)) * -0x6B2FB644ECCEEE15L;
        z = z ^ (z >>> 31);
        return (int)z & mask;
    }

    public readonly int Find(long key)
    {
        int capacity = _index.Count;
        if (capacity == 0) return -1;
        int mask = capacity - 1;
        int slot = Slot(key, mask);
        while (true)
        {
            int e = _index[slot];
            if (e == 0) return -1;
            if (_state[e - 1] != 0 && _keys[e - 1] == key) return e - 1;
            slot = (slot + 1) & mask;
        }
    }

    public readonly bool Contains(long key) => Find(key) >= 0;

    public readonly Option<V> Get(long key)
    {
        int e = Find(key);
        if (e < 0) return Prelude.None;
        return Prelude.Some(_vals[e]);
    }

    public Option<V> Set(long key, V value)
    {
        int e = Find(key);
        if (e >= 0)
        {
            V previous = _vals[e];
            _vals[e] = value;
            return Prelude.Some(previous);
        }
        if ((_keys.Count + 1) * 4L > _index.Count * 3L) Rehash();
        _keys.Push(key);
        _vals.Push(value);
        _state.Push(1);
        Link(_keys.Count - 1);
        _live += 1;
        return Prelude.None;
    }

    public Option<V> Remove(long key)
    {
        int e = Find(key);
        if (e < 0) return Prelude.None;
        _state[e] = 0;
        _live -= 1;
        return Prelude.Some(_vals[e]);
    }

    public void Clear()
    {
        _keys.Clear();
        _vals.Clear();
        _state.Clear();
        _index.Clear();
        _live = 0;
    }

    private void Link(int entry)
    {
        int mask = _index.Count - 1;
        int slot = Slot(_keys[entry], mask);
        while (_index[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }
        _index[slot] = entry + 1;
    }

    private void Rehash()
    {
        int n = _keys.Count;
        int w = 0;
        for (int r = 0; r < n; r++)
        {
            if (_state[r] != 0)
            {
                if (w != r)
                {
                    _keys[w] = _keys[r];
                    _vals[w] = _vals[r];
                }
                w += 1;
            }
        }
        _keys.Truncate(w);
        _vals.Truncate(w);
        _state.Clear();
        _state.Resize(w, 1);
        if (w >= 0x20000000)
        {
            VibraniumRuntime.Panic("map capacity overflow");
        }
        int capacity = 8;
        while (capacity < (w + 1) * 2)
        {
            capacity *= 2;
        }
        _index.Clear();
        _index.Resize(capacity, 0);
        for (int i = 0; i < w; i++)
        {
            Link(i);
        }
    }
}
