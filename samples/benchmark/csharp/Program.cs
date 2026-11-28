long listChecksum = RunLinkedListBenchmark();
long sortChecksum = RunQuickSortBenchmark();
long simdChecksum = RunSaxpyBenchmark();

Console.WriteLine($"list={listChecksum}");
Console.WriteLine($"sort={sortChecksum}");
Console.WriteLine($"simd={simdChecksum}");

static long RunLinkedListBenchmark()
{
    long checksum = 0;
    for (int round = 0; round < 400; round++)
    {
        Node? head = null;
        for (int i = 0; i < 5000; i++)
        {
            head = new Node((i * 31) ^ round, head);
        }
        head?.Add(round);
        checksum += SumList(head);
    }
    return checksum;
}

static long SumList(Node? head)
{
    long total = 0;
    for (Node? cur = head; cur is not null; cur = cur.Next)
    {
        total += cur.Value;
    }
    return total;
}

static long RunQuickSortBenchmark()
{
    int[] scratch = new int[4096];
    int[] stack = new int[128];

    long checksum = 0;
    for (int run = 0; run < 400; run++)
    {
        Span<int> data = scratch;
        FillPseudoRandom(data, run);
        QuickSort(data, stack);
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
        state ^= state >>> 17;
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
            (int leftEnd, int rightStart) = Partition(data, low, high);
            if (leftEnd - low < high - rightStart)
            {
                if (rightStart < high)
                {
                    stack[sp] = rightStart;
                    stack[sp + 1] = high;
                    sp += 2;
                }
                high = leftEnd;
            }
            else
            {
                if (low < leftEnd)
                {
                    stack[sp] = low;
                    stack[sp + 1] = leftEnd;
                    sp += 2;
                }
                low = rightStart;
            }
        }
    }
}

static (int LeftEnd, int RightStart) Partition(Span<int> data, int low, int high)
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
            (data[i], data[j]) = (data[j], data[i]);
            i++;
            j--;
        }
    }
    return (j, i);
}

static long RunSaxpyBenchmark()
{
    float[] yArr = new float[2048];
    float[] xArr = new float[2048];
    for (int i = 0; i < 2048; i++)
    {
        yArr[i] = (i & 15) * 0.25f;
        xArr[i] = ((i * 3) & 31) * 0.125f;
    }

    for (int step = 0; step < 250000; step++)
    {
        Saxpy(yArr, xArr, 0.0001f);
    }

    return (long)(yArr[0] * 100.0f) + (long)(yArr[1024] * 100.0f) + (long)(yArr[2047] * 100.0f);
}

static void Saxpy(Span<float> y, ReadOnlySpan<float> x, float a)
{
    ReadOnlySpan<float> xs = x.Slice(0, y.Length);
    for (int i = 0; i < y.Length; i++)
    {
        y[i] += a * xs[i];
    }
}

sealed class Node(int value, Node? next)
{
    public int Value = value;
    public Node? Next = next;

    public void Add(int delta) => Value += delta;
}
