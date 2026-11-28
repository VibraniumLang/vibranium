namespace Vibranium.Backend.Llvm;

public sealed record LlvmEmitOptions(
    bool SharedNoAlias = false,
    bool AddressSanitizer = false,
    string TargetTriple = "x86_64-pc-linux-gnu",
    bool? IncludeStdNative = null);
