Node x = new Node(1);
int k = (x = new Node(2)).V;
WriteLine(k);
WriteLine(x.V);

sealed class Node(int v) { public int V = v; }
