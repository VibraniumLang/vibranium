return 0;
public readonly struct IoError
{
    
    

    public IoError(IoErrorKind kind, IoOp op)
    {
        
        
    }

    readonly internal static IoError FromCode(long code, IoOp op) => new IoError((IoErrorKind)(int)(-code), op);

    
}

