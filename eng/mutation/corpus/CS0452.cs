WriteLine(F(0));WriteLine(F(1));
static long F(int key)
{
    switch(key)
    {
        default:C.Set(result:out var value,input:5L);return value;
        case 0:value=9L;return value;
    }
}
public static class C
{
    public static bool Set<T>(T input,out T result) where T : class {result=input;return true;}
    public static bool Set(bool input,out bool result) {result=input;return true;}
}