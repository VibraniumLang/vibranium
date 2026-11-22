WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Us2(2) + Us2(4) * 10 + Us2(6) * 100;
    acc += Us4(1) + Us4(3) * 10 + Us4(6) * 100;
    return 0;
}
static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
static int Us2(int p) { int n = 0; Option<int> o = UsMk1(p); n += o switch { Some<int>(var v) => v, NoneType => 7 }; n += o.IsSome ? 1000 : 0; return n; }
static Result<Vec<int>, int> UsMk3(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
static int Us4(int p) { int n = 0; n += UsMk3(p) switch { not Err<int> when p > 5 => 100, Ok<Vec<int>>(var v) => v.Count + v[0], _ => 9 }; return n; }
