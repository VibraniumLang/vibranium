Option<Node> o = Some(new Node(1));
Option<Node> p = Some(new Node(2));
OptionMut<Node> m = o.AsMut();
for (int i = 0; i < 2; i++)
{
    OptionMut<Node> k2 = p.AsMut();
    m = k2;
    WriteLine(m.IsSome);
}
sealed class Node(int v) { public int V = v; }
