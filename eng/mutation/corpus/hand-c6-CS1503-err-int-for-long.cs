Result<long, long> r = Err(7L);
if (r.IsErr(out var v, out Err<int> e)) { WriteLine(e.Error); }
return 0;
