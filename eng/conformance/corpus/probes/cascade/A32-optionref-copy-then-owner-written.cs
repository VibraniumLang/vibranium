Option<Node> o = Some(new Node(1));
OptionRef<Node> r = o.AsRef();
OptionRef<Node> r2 = r;
o = Some(new Node(3));
WriteLine(r2.IsSome);
WriteLine(r.IsSome);
sealed class Node(int v) { public int V = v; }
