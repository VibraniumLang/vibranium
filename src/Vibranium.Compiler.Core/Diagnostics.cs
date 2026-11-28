namespace Vibranium.Compiler.Core;

public readonly record struct VibraniumSpan(string FilePath, int Start, int Length, int Line, int Column);

public sealed record VibraniumDiagnostic(string Id, string Message, VibraniumSpan Span)
{
    public override string ToString() =>
        $"{Span.FilePath}({Span.Line},{Span.Column}): error {Id}: {Message}";
}

public static class VibraniumDiagnosticIds
{
    public const string Unsupported = "VIB000";
    public const string UseAfterMove = "VIB001";
    public const string OverlappingCallBorrows = "VIB002";
    public const string ConflictWithActiveBorrow = "VIB003";
    public const string MutateThroughSharedBorrow = "VIB004";
    public const string IllegalFieldOrElementMove = "VIB005";
    public const string LinearResourceViolation = "VIB006";
    public const string EscapingBorrow = "VIB007";
    public const string NullOrZeroUnsafe = "VIB008";
}
