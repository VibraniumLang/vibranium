using var keep = new R(9);
R r = new R(1);
r.Dispose();
R q = (r = new R(2));
WriteLine(r.Id);
q.Dispose();
sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
