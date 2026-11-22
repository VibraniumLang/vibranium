Option<Node> o = Some(new Node(1));
OptionMut<Node> m = o.AsMut();
OptionMut<Node> m2 = m;
WriteLine(m2.IsSome);
WriteLine(m.IsSome);
sealed class Node(int v) { public int V = v; }
