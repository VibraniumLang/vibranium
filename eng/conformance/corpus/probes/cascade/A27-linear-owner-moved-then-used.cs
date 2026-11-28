R r = new R(1);
R q = r;
WriteLine(r.Id);
q.Dispose();
sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
