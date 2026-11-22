namespace Vibranium.Internal;

internal static class VibraniumAppBootstrap
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Installs Vibranium standalone panic-exit parity hook in entry assembly")]
    internal static void Init() => global::Vibranium.VibraniumRuntime.Initialize();
}
