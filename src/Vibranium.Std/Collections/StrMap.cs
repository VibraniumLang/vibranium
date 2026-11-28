namespace Vibranium.Std;

public struct StrMap<V> where V : unmanaged
{
    private Vec<byte> _arena;
    private Vec<int> _start;
    private Vec<int> _len;
    private Vec<long> _hash;
    private Vec<V> _vals;
    private Vec<byte> _state;
    private Vec<int> _index;
    private int _live;

    public StrMap(int capacity)
    {
        _arena = new Vec<byte>();
        _start = new Vec<int>(capacity);
        _len = new Vec<int>(capacity);
        _hash = new Vec<long>(capacity);
        _vals = new Vec<V>(capacity);
        _state = new Vec<byte>(capacity);
        _index = new Vec<int>();
        _live = 0;
    }

    public readonly int Count => _live;
    public readonly int EntryCount => _start.Count;
    public readonly bool IsLive(int entry) => _state[entry] != 0;
    public readonly V ValueAt(int entry) => _vals[entry];

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> KeyAt(int entry) => _arena.AsReadOnlySpan().Slice(_start[entry], _len[entry]);

    public readonly int Find(global::System.ReadOnlySpan<byte> key)
    {
        int capacity = _index.Count;
        if (capacity == 0) return -1;
        int mask = capacity - 1;
        long h = Bytes.Hash(key);
        int slot = (int)(h ^ (h >>> 32)) & mask;
        while (true)
        {
            int e = _index[slot];
            if (e == 0) return -1;
            int i = e - 1;
            if (_state[i] != 0 && _hash[i] == h && Bytes.Eq(_arena.AsReadOnlySpan().Slice(_start[i], _len[i]), key)) return i;
            slot = (slot + 1) & mask;
        }
    }

    public readonly bool Contains(global::System.ReadOnlySpan<byte> key) => Find(key) >= 0;

    public readonly Option<V> Get(global::System.ReadOnlySpan<byte> key)
    {
        int e = Find(key);
        if (e < 0) return Prelude.None;
        return Prelude.Some(_vals[e]);
    }

    public Option<V> Set(global::System.ReadOnlySpan<byte> key, V value)
    {
        int e = Find(key);
        if (e >= 0)
        {
            V previous = _vals[e];
            _vals[e] = value;
            return Prelude.Some(previous);
        }
        if ((_start.Count + 1) * 4L > _index.Count * 3L) Rehash();
        _start.Push(_arena.Count);
        _len.Push(key.Length);
        _hash.Push(Bytes.Hash(key));
        _arena.Extend(key);
        _vals.Push(value);
        _state.Push(1);
        Link(_start.Count - 1);
        _live += 1;
        return Prelude.None;
    }

    public Option<V> Remove(global::System.ReadOnlySpan<byte> key)
    {
        int e = Find(key);
        if (e < 0) return Prelude.None;
        _state[e] = 0;
        _live -= 1;
        return Prelude.Some(_vals[e]);
    }

    public readonly int Find(string key) => Find(key.AsUtf8());

    public readonly bool Contains(string key) => Contains(key.AsUtf8());

    public readonly Option<V> Get(string key) => Get(key.AsUtf8());

    public Option<V> Set(string key, V value) => Set(key.AsUtf8(), value);

    public Option<V> Remove(string key) => Remove(key.AsUtf8());

    public void Clear()
    {
        _arena.Clear();
        _start.Clear();
        _len.Clear();
        _hash.Clear();
        _vals.Clear();
        _state.Clear();
        _index.Clear();
        _live = 0;
    }

    private void Link(int entry)
    {
        int mask = _index.Count - 1;
        long h = _hash[entry];
        int slot = (int)(h ^ (h >>> 32)) & mask;
        while (_index[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }
        _index[slot] = entry + 1;
    }

    private void Rehash()
    {
        int n = _start.Count;
        int w = 0;
        for (int r = 0; r < n; r++)
        {
            if (_state[r] != 0)
            {
                if (w != r)
                {
                    _start[w] = _start[r];
                    _len[w] = _len[r];
                    _hash[w] = _hash[r];
                    _vals[w] = _vals[r];
                }
                w += 1;
            }
        }
        _start.Truncate(w);
        _len.Truncate(w);
        _hash.Truncate(w);
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

public struct Interner
{
    private StrMap<int> _map;

    public Interner(int capacity)
    {
        _map = new StrMap<int>(capacity);
    }

    public readonly int Count => _map.EntryCount;

    public int Intern(global::System.ReadOnlySpan<byte> text)
    {
        int e = _map.Find(text);
        if (e >= 0) return e;
        int id = _map.EntryCount;
        _map.Set(text, id);
        return id;
    }

    public int Intern(string text) => Intern(text.AsUtf8());

    [global::System.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> Text(int id) => _map.KeyAt(id);
}
