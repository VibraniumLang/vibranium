using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Vibranium.Tests;

internal static class HeavyGateProcess
{
    internal static bool IsCiEnvironment(string? ciValue = null) =>
        string.Equals(ciValue ?? Environment.GetEnvironmentVariable("CI"), "true", StringComparison.Ordinal);

    internal static ProcessStartInfo CreateStartInfo(string command, IReadOnlyList<string> arguments, string? ciValue = null)
    {
        bool isCi = IsCiEnvironment(ciValue);
        var start = new ProcessStartInfo(isCi ? command : "heavy")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!isCi)
        {
            start.ArgumentList.Add(command);
        }
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
