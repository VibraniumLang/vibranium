int k = 3;
switch (Mk(7)) { case _ when k > 5: WriteLine(1); break; case Some<int>(var v): WriteLine(v); break; default: WriteLine(0); break; }
static Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }
