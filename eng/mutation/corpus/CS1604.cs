var c = new C();
c.M();
return 0;
class C { public int N; [Mut] public void M() { this = new C(); } }
