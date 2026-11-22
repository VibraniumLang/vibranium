Make().X = 1;
WriteLine(1);
static Box Make() => new Box(1);
public struct Box { public int X; public Box(int x) { X = x; } }
