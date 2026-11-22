Option<Node> o = Some(new Node(1));
Option<Node> p = Some(new Node(2));
OptionMut<Node> m = o.AsMut();
for (int i = 0; i < 2; i++)
{
    WriteLine(m.IsSome);
    OptionMut<Node> k2 = m;
    WriteLine(k2.IsSome);
}
sealed class Node(int v) { public int V = v; }
