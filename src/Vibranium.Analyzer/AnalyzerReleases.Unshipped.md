; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VIB000 | Vibranium.Language | Error | Unsupported C# construct
VIB001 | Vibranium.Ownership | Error | Use of moved or uninitialized value
VIB002 | Vibranium.Borrowing | Error | Overlapping borrows in a single call
VIB003 | Vibranium.Borrowing | Error | Conflict with active borrow
VIB004 | Vibranium.Borrowing | Error | Mutation through shared borrow
VIB005 | Vibranium.Ownership | Error | Illegal move out of field, element, or borrowed place
VIB006 | Vibranium.Resources | Error | Linear IDisposable resource violation
VIB007 | Vibranium.Borrowing | Error | Escaping borrow or view
VIB008 | Vibranium.Safety | Error | Null or non-zero-safe initialization
