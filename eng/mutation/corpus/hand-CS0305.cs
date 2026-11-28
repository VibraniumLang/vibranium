Option<int> option = Some(1);
switch (option)
{
    case Some<int, int>(var v): WriteLine(v); break;
    default: break;
}
return 0;
