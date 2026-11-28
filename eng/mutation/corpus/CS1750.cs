return 0;
static class CorpusSlice
{
public static Result<Vec<byte>, IoError> ReadAllBytes(string path, int maxBytes = 1.5) =>
        File.ReadAllBytes(path.AsUtf8(), maxBytes);

}
