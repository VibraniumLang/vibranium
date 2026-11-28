long acc = 0;
ulong i = 0;
while (i < 8_000_000_000UL)
{
    ulong m = (i * 3UL) ^ (i >> 2);
    acc += (long)(m & 15UL) - (long)(i & 1UL);
    i++;
}
WriteLine(acc);
