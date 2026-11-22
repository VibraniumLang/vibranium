H h = new H();
Span<int> sp = h.V.AsSpan();
h.V = [7];
WriteLine(sp[0]);
sealed class H { public Vec<int> V = [1, 2]; }
