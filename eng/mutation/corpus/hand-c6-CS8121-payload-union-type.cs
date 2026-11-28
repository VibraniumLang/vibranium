int k = 4;
switch (MkR(k)) { case Ok<int>(var v): WriteLine(v); break; case Err<Option<int>>(Option<int> e): WriteLine(e.IsSome); break; }
static Result<int, Option<int>> MkR(int a) { if (a > 2) return Ok(a); Option<int> s = Some(a); return Err(s); }
