static class Program
{
    static int Consume(Vec<int> xs) => xs[0];
    static int UnwrapComplementaryResult(Result<Vec<int>, Vec<int>> res) =>
        Move(res) switch
        {
            Ok<Vec<string>>(var ok) => Consume(Move(ok)),
            Err<Vec<int>>(var err) => Consume(Move(err)) * 10,
        };
    public static void Main()
    {
    }
}
