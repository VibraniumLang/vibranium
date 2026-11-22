Option<Node> o = Some(new Node(1));
Option<Node> p = Some(new Node(2));
OptionMut<Node> m = o.AsMut();
OptionMut<Node> m2 = (m = p.AsMut());
WriteLine(p.IsSome);
WriteLine(m2.IsSome);
sealed class Node(int v) { public int V = v; }
