var h1=new Holder(new Node(1),5); var h2=new Holder(new Node(2),6);
ref Holder r;
r.N.Val=K(ref (r=ref h2))+10;
WriteLine(h1.N.Val); WriteLine(h2.N.Val);
static int K(ref Holder h)=>h.K;
public struct Holder(Node n,int k) { public readonly Node N=n; public int K=k; }
public class Node(int v) { public int Val=v; }