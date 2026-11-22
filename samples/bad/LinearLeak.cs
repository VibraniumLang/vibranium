var h = new Handle(3);
return 0;

class Handle(int fd) : IDisposable
{
    public void Dispose() => WriteLine(fd);
}
