Result<int, int> r = Ok(7);
if (!r.IsErr(out long v, out var e)) { WriteLine(v); }
return 0;
