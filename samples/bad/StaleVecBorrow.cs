var v = new Vec<int>(1);
v.Push(10);
ref var first = ref v[0];
for (int i = 0; i < 16; i++)
{
    v.Push(i + 100);
}
first = 999;
WriteLine(v[0]);
return 0;
