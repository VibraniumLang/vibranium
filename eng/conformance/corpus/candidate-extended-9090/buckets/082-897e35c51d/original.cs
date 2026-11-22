WriteLine(F(1, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var m1 = new IntMap<int>(5);
    _ = m1.Set(4, 6);
    m1.Set(1, p0);
    for (int m1_i = 0; m1_i < 11; m1_i++) m1.Set(m1_i * 3, m1_i);
    var b2 = p1;
    var s3 = 3.5f - 1.5f switch { 2.5f when true => acc, _ => 1 };
    acc += s3;
    acc += m1.Get(0).UnwrapOr(-1);
    _ = m1.Remove(9);
    WriteLine(m1.Count);
    WriteLine(m1.Contains(1));
    return acc;
}
