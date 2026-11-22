// 1. Zero-null Option<T> & slot cursors (.AsMut, .Set, .Take, .AsRef, .Expect)
Option<Node> list = Some(new Node(10, Some(new Node(20, None))));
Append(ref list, 30);
if (list.AsMut() is Some<Node>(var head)) head.Add(5);
_ = Remove(ref list, 20);
Console.WriteLine(Sum(in list)); // 45

// 2. Vec<T>.Add, collection expressions & zero-copy Span<T> / plain-string UTF-8 views
Vec<int> data = [10, 20, 30, 40];
data.Add(50);
Span<float> ys = [1f, 2f, 3f, 4f];
Saxpy(ys, [10f, 20f, 30f, 40f], 2f);
var pkt = new Packet("VIB/1.0".AsUtf8().Slice(0, 3)[0], data[0] + data[4] + (int)ys[3]);
Console.WriteLine(pkt.Tag + pkt.Value); // 230

// 3. Zero-ceremony UTF-8 line iteration & zero-exception Result<T, E> propagation
foreach (var line in "alpha\r\nbeta".AsUtf8().Lines())
{
    WriteLine(line);
}
Console.WriteLine(EvalStep("84".AsUtf8(), 2).Expect("valid step")); // 142
Console.WriteLine(EvalStep("84".AsUtf8(), 0).UnwrapOrDefault()); // 0

// 4. Deterministic linear IDisposable resources (exact-once, zero GC)
using var outer = new Lease(1);
{ using var inner = new Lease(2); } // disposes 2 here, then 1 on scope exit

static void Append(ref Option<Node> head, int value)
{
    var slot = head.AsMut();
    while (slot is Some<Node>(var n)) slot = n.Next.AsMut();
    slot.Set(Some(new Node(value, None)));
}

static bool Remove(ref Option<Node> head, int target)
{
    for (var slot = head.AsMut(); slot is Some<Node>(var n); slot = n.Next.AsMut())
    {
        if (n.Value != target) continue;
        slot.Set(n.Next.Take());
        return true;
    }
    return false;
}

static int Sum(in Option<Node> head)
{
    int total = 0;
    for (var cur = head.AsRef(); cur is Some<Node>(var n); cur = n.Next.AsRef()) total += n.Value;
    return total;
}

static void Saxpy(Span<float> y, ReadOnlySpan<float> x, float a)
{
    var xs = x.Slice(0, y.Length);
    for (int i = 0; i < y.Length; i++) y[i] += a * xs[i];
}

static Result<int, StepErr> ParseStep(ReadOnlySpan<byte> utf8, int scale) =>
    utf8.Length < 2 || scale == 0
        ? Err(StepErr.Invalid)
        : Ok(((utf8[0] - (byte)'0') * 10 + (utf8[1] - (byte)'0')) / scale);

static Result<int, StepErr> EvalStep(ReadOnlySpan<byte> utf8, int scale)
{
    if (ParseStep(utf8, scale).IsErr(out var val, out var err)) return err;
    return Ok(val + 100);
}

enum StepErr { Invalid }

readonly struct Packet(byte tag, int value)
{
    public readonly byte Tag = tag;
    public readonly int Value = value;
}

class Node(int value, Option<Node> next)
{
    public int Value = value;
    public Option<Node> Next = next;
    [Mut] public void Add(int delta) => Value += delta;
}

class Lease(int id) : IDisposable
{
    public void Dispose()
    {
        Console.Write("close ");
        Console.WriteLine(id);
    }
}
