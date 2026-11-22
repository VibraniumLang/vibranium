long listChecksum = RunLinkedListBenchmark();
long sortChecksum = RunQuickSortBenchmark();
long simdChecksum = RunSaxpyBenchmark();

Console.Write("list=");
Console.WriteLine(listChecksum);
Console.Write("sort=");
Console.WriteLine(sortChecksum);
Console.Write("simd=");
Console.WriteLine(simdChecksum);

static long RunLinkedListBenchmark()
{
    long checksum = 0;
    for (int round = 0; round < 400; round++)
    {
        Option<Node> head = None;
        for (int i = 0; i < 5000; i++)
        {
            head = Some(new Node((i * 31) ^ round, head));
        }
        if (head.AsMut() is Some<Node>(var top))
        {
            top.Add(round);
        }
        checksum += SumList(in head);
    }
    return checksum;
}

static long SumList(in Option<Node> head)
{
    long total = 0;
    for (var cur = head.AsRef(); cur is Some<Node>(var node); cur = node.Next.AsRef())
    {
        total += node.Value;
    }
    return total;
}

static long RunQuickSortBenchmark()
{
    var scratch = new Vec<int>(4096);
    scratch.Resize(4096, 0);
    var stack = new Vec<int>(128);
    stack.Resize(128, 0);

    long checksum = 0;
    for (int run = 0; run < 400; run++)
    {
        Span<int> data = scratch.AsSpan();
        FillPseudoRandom(data, run);
        QuickSort(data, stack.AsSpan());
        checksum += data[0] + data[2048] + data[4095];
    }
    return checksum;
}

static void FillPseudoRandom(Span<int> data, int seedIndex)
{
    uint state = 0x9E3779B9u + (uint)seedIndex * 7919u;
    for (int i = 0; i < data.Length; i++)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        data[i] = (int)(state & 0x7FFFFFFFu);
    }
}

static void QuickSort(Span<int> data, Span<int> stack)
{
    if (data.Length <= 1) return;

    int sp = 0;
    stack[sp] = 0;
    stack[sp + 1] = data.Length - 1;
    sp += 2;

    while (sp > 0)
    {
        sp -= 2;
        int low = stack[sp];
        int high = stack[sp + 1];

        while (low < high)
        {
            Split split = Partition(data, low, high);
            if (split.LeftEnd - low < high - split.RightStart)
            {
                if (split.RightStart < high)
                {
                    stack[sp] = split.RightStart;
                    stack[sp + 1] = high;
                    sp += 2;
                }
                high = split.LeftEnd;
            }
            else
            {
                if (low < split.LeftEnd)
                {
                    stack[sp] = low;
                    stack[sp + 1] = split.LeftEnd;
                    sp += 2;
                }
                low = split.RightStart;
            }
        }
    }
}

static Split Partition(Span<int> data, int low, int high)
{
    int pivot = data[low + ((high - low) >> 1)];
    int i = low;
    int j = high;

    while (i <= j)
    {
        while (data[i] < pivot) i++;
        while (data[j] > pivot) j--;
        if (i <= j)
        {
            int tmp = data[i];
            data[i] = data[j];
            data[j] = tmp;
            i++;
            j--;
        }
    }
    return new Split(j, i);
}

static long RunSaxpyBenchmark()
{
    var yVec = new Vec<float>(2048);
    var xVec = new Vec<float>(2048);
    for (int i = 0; i < 2048; i++)
    {
        yVec += (i & 15) * 0.25f;
        xVec += ((i * 3) & 31) * 0.125f;
    }

    for (int step = 0; step < 250000; step++)
    {
        Saxpy(yVec.AsSpan(), xVec.AsReadOnlySpan(), 0.0001f);
    }

    return (long)(yVec[0] * 100.0f) + (long)(yVec[1024] * 100.0f) + (long)(yVec[2047] * 100.0f);
}

static void Saxpy(Span<float> y, ReadOnlySpan<float> x, float a)
{
    var xs = x.Slice(0, y.Length);
    for (int i = 0; i < y.Length; i++)
    {
        y[i] += a * xs[i];
    }
}

readonly struct Split(int leftEnd, int rightStart)
{
    public readonly int LeftEnd = leftEnd;
    public readonly int RightStart = rightStart;
}

class Node(int value, Option<Node> next)
{
    public int Value = value;
    public Option<Node> Next = next;

    [Mut]
    public void Add(int delta) => Value += delta;
}
