WriteLine(F());
static int F()
{
    int y;
    return Find(0) switch { Some<int>(var v) => v, NoneType => 0, _ => y };
}
static Option<int> Find(int k) => Some(k);
