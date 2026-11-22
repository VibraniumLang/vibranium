Option<Node> o = Some(new Node(1));
Option<Node> q = Some(new Node(2));
OptionRef<Node> r = o.AsRef();
bool x = (r = q.AsRef()).IsSome;
o = Some(new Node(5));
WriteLine(x);
WriteLine(r.IsSome);
sealed class Node(int v) { public int V = v; }
