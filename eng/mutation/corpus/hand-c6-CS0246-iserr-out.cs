Result<int, int> r = Ok(1);
if (r.IsErr(out var v, out Zz e)) { WriteLine(1); }
return 0;
