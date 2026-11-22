WriteLine(F(-1, true));
WriteLine(F(2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [acc, p0];
    foreach (var e1 in v2)
    {
        for (int i3 = 0; i3 < 4; i3 += 1)
        {
            i3 = i3 << p0;
            if (!p1) break;
            acc += p0;
        }
        float w4 = (float)(p0 + 3);
        WriteLine((int)(w4 % -2.5f));
        if (p1) continue;
        int x5 = p0 % ((int)2.25);
        acc += e1;
    }
    return acc;
}
