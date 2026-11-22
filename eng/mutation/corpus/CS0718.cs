static class Program
{
    static int Consume(Vec<int> xs) => xs[0];

    static int UnwrapNested(Option<Option<Vec<int>>> outer) =>
        Move(outer) switch
        {
            Some<Option<Vec<int>>>(Some<Vec<int>>(var inner)) => Consume(Move(inner)),
            _ => 0,
        };

    static int UnwrapComplementaryOption(Option<Vec<int>> opt) =>
        Move(opt) switch
        {
            Some<Vec<int>>(var xs) => Consume(Move(xs)),
            NoneType => -1,
        };

    static int UnwrapComplementaryResult(Result<Vec<int>, Vec<int>> res) =>
        Move(res) switch
        {
            Ok<Vec<int>>(var ok) => Consume(Move(ok)),
            Err<Vec<int>>(var err) => Consume(Move(err)) * 10,
        };

    static int UnwrapNestedResultOption(Result<Option<Vec<int>>, Vec<int>> res) =>
        Move(res) switch
        {
            Ok<Option<Vec<int>>>(Some<Program>(var inner)) => Consume(Move(inner)),
            Err<Vec<int>>(var err) => Consume(Move(err)) * 100,
            _ => 5,
        };

    public static void Main()
    {
        Vec<int> v1 = new();
        v1.Add(99);
        Option<Vec<int>> inner1 = Some(Move(v1));
        Option<Vec<int>> innerNone = None;
        Option<Option<Vec<int>>> outerNone = None;
        WriteLine(UnwrapNested(Some(Move(inner1))));
        WriteLine(UnwrapNested(Some(Move(innerNone))));
        WriteLine(UnwrapNested(Move(outerNone)));

        Vec<int> v2 = new();
        v2.Add(42);
        Option<Vec<int>> optNone = None;
        WriteLine(UnwrapComplementaryOption(Some(Move(v2))));
        WriteLine(UnwrapComplementaryOption(Move(optNone)));

        Vec<int> v3 = new();
        v3.Add(7);
        Result<Vec<int>, Vec<int>> errRes = Err(Move(v3));
        WriteLine(UnwrapComplementaryResult(Move(errRes)));

        Vec<int> v4 = new();
        v4.Add(123);
        Option<Vec<int>> okInner = Some(Move(v4));
        Result<Option<Vec<int>>, Vec<int>> nestedOk = Ok(Move(okInner));
        Option<Vec<int>> okEmpty = None;
        Result<Option<Vec<int>>, Vec<int>> nestedEmpty = Ok(Move(okEmpty));
        WriteLine(UnwrapNestedResultOption(Move(nestedOk)));
        WriteLine(UnwrapNestedResultOption(Move(nestedEmpty)));
    }
}