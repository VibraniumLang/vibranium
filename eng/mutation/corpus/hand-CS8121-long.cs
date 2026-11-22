Result<int, int> x = Err(424242);
switch (x)
{
    case Err<long>(var e): WriteLine(e); break;
    default: WriteLine(0); break;
}
return 0;
