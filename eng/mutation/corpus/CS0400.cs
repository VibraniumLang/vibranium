return 0;
public struct Interner
{
    private StrMap<int> _map;

    

    

    public int Intern(global::System.ReadOnlySpan<byte> text)
    {
        
        
        int id = _map.EntryCount;
        _map.Set(text, id);
        return id;
    }

    public int Intern(string text) => Intern(text.AsUtf8());

    [global::SystemZz.Diagnostics.CodeAnalysis.UnscopedRef]
    public readonly global::System.ReadOnlySpan<byte> Text(int id) => _map.KeyAt(id);
}

