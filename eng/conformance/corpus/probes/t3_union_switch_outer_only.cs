WriteLine(F(1));
static int F(int k)
{
    int s = Try(k) switch { Ok<int>(var v) => v };
    return s;
}
static Result<int, int> Try(int k) => Ok(k);
