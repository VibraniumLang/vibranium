namespace Vibranium.Std;

public enum IoErrorKind
{
    Other = 1,
    NotFound = 2,
    PermissionDenied = 3,
    AlreadyExists = 4,
    IsADirectory = 5,
    InvalidInput = 6,
    FileTooLarge = 7,
    UnexpectedEof = 8,
    WriteZero = 9,
    Interrupted = 10,
    BrokenPipe = 11,
    ConnectionRefused = 12,
    ConnectionReset = 13,
    ConnectionAborted = 14,
    NotConnected = 15,
    AddrInUse = 16,
    AddrNotAvailable = 17,
    TimedOut = 18,
}

public enum IoOp
{
    Open = 1,
    Read = 2,
    Write = 3,
    Sync = 4,
    Bind = 5,
    Accept = 6,
    Connect = 7,
    Shutdown = 8,
    Query = 9,
    Spawn = 10,
    Args = 11,
}

public readonly struct IoError
{
    public readonly IoErrorKind Kind;
    public readonly IoOp Op;

    public IoError(IoErrorKind kind, IoOp op)
    {
        Kind = kind;
        Op = op;
    }

    internal static IoError FromCode(long code, IoOp op) => new IoError((IoErrorKind)(int)(-code), op);

    public readonly global::System.ReadOnlySpan<byte> KindName()
    {
        switch (Kind)
        {
            case IoErrorKind.NotFound: return "NotFound"u8;
            case IoErrorKind.PermissionDenied: return "PermissionDenied"u8;
            case IoErrorKind.AlreadyExists: return "AlreadyExists"u8;
            case IoErrorKind.IsADirectory: return "IsADirectory"u8;
            case IoErrorKind.InvalidInput: return "InvalidInput"u8;
            case IoErrorKind.FileTooLarge: return "FileTooLarge"u8;
            case IoErrorKind.UnexpectedEof: return "UnexpectedEof"u8;
            case IoErrorKind.WriteZero: return "WriteZero"u8;
            case IoErrorKind.Interrupted: return "Interrupted"u8;
            case IoErrorKind.BrokenPipe: return "BrokenPipe"u8;
            case IoErrorKind.ConnectionRefused: return "ConnectionRefused"u8;
            case IoErrorKind.ConnectionReset: return "ConnectionReset"u8;
            case IoErrorKind.ConnectionAborted: return "ConnectionAborted"u8;
            case IoErrorKind.NotConnected: return "NotConnected"u8;
            case IoErrorKind.AddrInUse: return "AddrInUse"u8;
            case IoErrorKind.AddrNotAvailable: return "AddrNotAvailable"u8;
            case IoErrorKind.TimedOut: return "TimedOut"u8;
            default: return "Other"u8;
        }
    }
}
