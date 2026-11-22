WriteLine(F(1, 1L));
static int F(int k, long a)
{
    var r = Try(k);
    int s = r switch { Ok<int>(var v) => a switch { < 0L => v }, Err<int>(var e) => e };
    return s;
}
static Result<int, int> Try(int k) => Ok(k);
