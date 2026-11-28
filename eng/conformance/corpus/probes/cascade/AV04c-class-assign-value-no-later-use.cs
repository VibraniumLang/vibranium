Node x = new Node(1);
int k = (x = new Node(2)).V;
WriteLine(k);

sealed class Node(int v) { public int V = v; }
