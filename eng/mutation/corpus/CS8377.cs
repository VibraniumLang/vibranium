Vec<int> xs = [10, 20, 30];
Option<Vec<int>> optVec = Some(xs);
int sum = 0;
if (optVec is Some<Vec<string>>(var v))
{
    sum += v.Count + v[0] + v[2];
}
if (optVec is Some<Vec<int>>(var vAgain))
{
    sum += vAgain[1];
}

Option<Node> optNode = Some(new Node(5));
if (optNode is Some<Node>(var sharedNode))
{
    sum += sharedNode.X;
}
if (optNode.AsMut() is Some<Node>(var mutNode))
{
    mutNode.Add(12);
}
if (optNode is Some<Node>(var afterNode))
{
    sum += afterNode.X;
}

WriteLine(sum);

class Node(int x)
{
    public int X = x;
    [Mut] public void Add(int d) { X += d; }
}