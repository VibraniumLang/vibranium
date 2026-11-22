Result<long, long> r = Ok(7L);
if (!r.IsErr(out int v, out var e)) { WriteLine(v); }
return 0;
