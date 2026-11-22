var c = new Counter(1);
Sneaky(in c);
return c.Value;

static void Sneaky(in Counter c)
{
    c.Inc();
}

class Counter(int v)
{
    public int Value = v;
    [Mut] public void Inc() => Value += 1;
}
