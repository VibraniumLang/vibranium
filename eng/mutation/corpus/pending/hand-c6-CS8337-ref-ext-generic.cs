int x = 1;
E.Touch(ref x);
WriteLine(x);
static class E { public static void Touch<T>(this ref T t) { } }
