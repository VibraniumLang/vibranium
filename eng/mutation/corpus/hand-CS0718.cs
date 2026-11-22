static class Program
{
    static int Consume(Vec<int> xs) => xs[0];
    static int UnwrapNested(Option<Option<Vec<int>>> outer) =>
        Move(outer) switch
        {
            Some<Program>(Some<Vec<int>>(var inner)) => Consume(Move(inner)),
            _ => 0,
        };
    public static void Main()
    {
    }
}
