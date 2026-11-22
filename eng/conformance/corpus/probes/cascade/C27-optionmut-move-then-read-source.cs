Option<Node> o = Some(new Node(1));
OptionMut<Node> m = o.AsMut();
OptionMut<Node> m2 = m;
WriteLine(o.IsSome);
WriteLine(m2.IsSome);
sealed class Node(int v) { public int V = v; }
